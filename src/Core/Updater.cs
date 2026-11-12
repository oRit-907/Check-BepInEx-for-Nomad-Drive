using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace NomadModDownloader.Core;

/// <param name="Url">The file this copy updates with: the installer when Check BepInEx is installed, else the app itself.</param>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string Url, long Size, string PageUrl, string Sha256, bool IsInstaller);

public enum UpdateCheck { UpToDate, Available, Failed }

/// <summary>
/// 3.2.0: self-update from nomaddrivemods.online (no GitHub). The site's /app answer gives the newest version, its notes
/// and each file with its size and SHA-256. An installed copy (it has the installer's uninstaller next to it) updates by
/// running the new installer quietly; a copy run on its own (the single EXE) swaps itself for the new EXE, as before.
/// Every download is checked against the site's SHA-256 before anything is changed.
/// </summary>
public sealed class Updater
{
    public const string Site = "https://www.nomaddrivemods.online";
    public const string InfoUrl = Site + "/app";
    public const string PageUrl = Site + "/bepinex";
    public const string ExeName = "CheckBepInEx.exe";
    /// <summary>The uninstaller the installer puts next to the app: its being there means this copy was installed.</summary>
    public const string UninstallerName = "Uninstall Check BepInEx.exe";
    public static Version Current => typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    /// <summary>Whether this copy was set up by the installer (then updates go through the installer too).</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
                return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, UninstallerName));
            }
            catch { return false; }
        }
    }

    private readonly HttpClient _http;
    /// <summary>Not used any more (updates come from the website); kept so older settings still load.</summary>
    public string? Token { get; set; }

    public Updater(HttpClient http) { _http = http; }

    public async Task<(UpdateCheck Result, UpdateInfo? Info, string? Error)> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, InfoUrl);
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode == 503) return (UpdateCheck.UpToDate, null, null);   // nothing published right now
            if (!resp.IsSuccessStatusCode) return (UpdateCheck.Failed, null, $"nomaddrivemods.online ({(int)resp.StatusCode})");
            var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
            if (j["ok"]?.GetValue<bool>() != true) return (UpdateCheck.UpToDate, null, null);
            var tag = j["version"]?.GetValue<string>() ?? "";
            if (!Version.TryParse(tag, out var v)) return (UpdateCheck.Failed, null, "Bad version: " + tag);
            v = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            // Installed: the installer. On its own: the app (or the installer when the site only has that).
            var files = j["files"];
            var file = (IsInstalled ? files?["setup"] : files?["exe"]) ?? files?["setup"];
            if (file == null) return (UpdateCheck.Failed, null, L.T("The website has no download for this version yet."));
            var url = file["url"]?.GetValue<string>() ?? "";
            var sha = (file["sha256"]?.GetValue<string>() ?? "").ToLowerInvariant();
            if (!url.StartsWith(Site + "/", StringComparison.OrdinalIgnoreCase) || sha.Length != 64) return (UpdateCheck.Failed, null, L.T("The website's update information isn't complete."));
            var isSetup = (file["name"]?.GetValue<string>() ?? "").Contains("Setup", StringComparison.OrdinalIgnoreCase);
            var info = new UpdateInfo(v, "v" + v.ToString(3), j["notes"]?.GetValue<string>() ?? "", url, file["size"]?.GetValue<long>() ?? 0,
                j["page"]?.GetValue<string>() ?? PageUrl, sha, isSetup);
            return (v > Current ? UpdateCheck.Available : UpdateCheck.UpToDate, info, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (UpdateCheck.Failed, null, ex.Message); }
    }

    /// <summary>Downloads the new installer or app next to the app's data (not over the running one) and checks it.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var dir = Path.Combine(AppPaths.Root, "update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, info.IsInstaller ? "CheckBepInEx-Setup.new.exe" : "CheckBepInEx.new.exe");
        var total = resp.Content.Headers.ContentLength ?? info.Size;
        using (var sha = SHA256.Create())
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var fs = File.Create(path))
            {
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var got = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            if (got != info.Sha256)
            {
                File.Delete(path);
                throw new InvalidOperationException(L.T("The update download didn't match its checksum, so it wasn't used. Nothing was changed."));
            }
        }
        var head = new byte[2];
        await using (var check = File.OpenRead(path)) await check.ReadExactlyAsync(head, ct);
        if (head[0] != 'M' || head[1] != 'Z') { File.Delete(path); throw new InvalidOperationException(L.T("The downloaded update isn't a valid program. Nothing was changed.")); }
        return path;
    }

    /// <summary>
    /// After an update: the new version has started fine, so remove the previous exe (kept as "&lt;name&gt;.old.exe"
    /// until now, so a failed swap could be undone) and the downloaded copy. Retries for a few seconds while the
    /// update script finishes.
    /// </summary>
    public static void CleanUpAfterUpdate()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var current = Environment.ProcessPath;
                if (current == null) return;
                var dir = Path.GetDirectoryName(current)!;
                var targets = new List<string> { Path.Combine(dir, Path.GetFileNameWithoutExtension(current) + ".old.exe") };
                var updateDir = Path.Combine(AppPaths.Root, "update");
                if (Directory.Exists(updateDir)) targets.AddRange(Directory.EnumerateFiles(updateDir, "*.new.exe"));
                for (var attempt = 0; attempt < 10 && targets.Count > 0; attempt++)
                {
                    foreach (var f in targets.ToList())
                    {
                        try { if (File.Exists(f)) File.Delete(f); targets.Remove(f); }
                        catch { /* still in use: try again shortly */ }
                    }
                    if (targets.Count > 0) await Task.Delay(1000);
                }
            }
            catch { }
        });
    }

    /// <summary>
    /// Starts a helper that, once this process has exited, either runs the new installer quietly into this copy's
    /// folder (installed copies) or swaps the exe (a copy run on its own), then starts the new version.
    /// </summary>
    public static void ApplyAndRestart(string downloaded, bool isInstaller)
    {
        var current = Environment.ProcessPath!;
        var dir = Path.GetDirectoryName(current)!;
        var script = Path.Combine(AppPaths.Root, "update", "apply-update.cmd");
        string body;
        if (isInstaller)
        {
            // NSIS: /S quiet, /D= the folder to install into (must be last, without quotes).
            var app = Path.Combine(dir, ExeName);
            body = $"start \"\" /wait \"{downloaded}\" /S /UPDATE /D={dir}\r\nif exist \"{app}\" (start \"\" \"{app}\") else (start \"\" \"{current}\")";
        }
        else
        {
            var old = Path.Combine(dir, Path.GetFileNameWithoutExtension(current) + ".old.exe");
            body = $"del /f /q \"{old}\" >nul 2>&1\r\nmove /y \"{current}\" \"{old}\" >nul\r\nmove /y \"{downloaded}\" \"{current}\" >nul\r\nif errorlevel 1 move /y \"{old}\" \"{current}\" >nul\r\nstart \"\" \"{current}\"";
        }
        File.WriteAllText(script,
            "@echo off\r\nsetlocal\r\n" +
            $"set PID={Environment.ProcessId}\r\n" +
            ":wait\r\n" +
            "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            body + "\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        // The caller closes the app; the script waits for that, updates and starts it again.
    }
}
