using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SharpCompress.Archives;

namespace NomadModDownloader.Core;

/// <summary>Which mod loader the player picked on the Check page (3.4.0).</summary>
public enum ModLoader { BepInEx5, IgniteLoader }

public static class LoaderChoice
{
    public static ModLoader Current { get; set; } = ModLoader.BepInEx5;
    public static bool Ignite => Current == ModLoader.IgniteLoader;
    public static string Name => Ignite ? "Ignite Loader" : "BepInEx 5";
}

/// <param name="Sha256">The site's hash of the download (lowercase hex). Empty for the copy built into the Nexus edition.</param>
public sealed record IgnitePlan(string Version, Uri Url, long SizeBytes, string Sha256 = "");

/// <summary>
/// Ignite Loader (nomaddrivemods.online/mod/20): Nomad Drive's own mod loader. It replaces BepInEx and still runs
/// BepInEx 5 and 6 mods (also the ones left in BepInEx\plugins) and MelonLoader mods. Everything it adds is
/// winhttp.dll, doorstop_config.ini, .doorstop_version and the IgniteLoader folder. BepInEx and Ignite Loader both
/// start through winhttp.dll and doorstop_config.ini, so only one of them runs: switching swaps those three files
/// (the other loader's copies are kept in the app's backups, so switching back needs no download).
/// </summary>
public static class IgniteLoaderManager
{
    public const string SiteListing = "Ignite Loader";
    public const string Folder = "IgniteLoader";
    public const string Bootstrap = @"IgniteLoader\loader\IgniteLoader.Bootstrap.dll";
    private static readonly string[] Starter = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" };
    public static readonly string[] CoreFiles = { "IgniteLoader.Bootstrap.dll", "IgniteLoader.dll", "BepInEx.dll", "0Harmony.dll", "Mono.Cecil.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll" };

    /// <summary>Backups of each loader's starter files, for switching back without a download.</summary>
    private static string StarterBackup(string loader) => Path.Combine(AppPaths.Backups, "starter-" + loader);

    public static string Dir(GameInstall g) => Path.Combine(g.Root, Folder);
    public static string LoaderDir(GameInstall g) => Path.Combine(Dir(g), "loader");

    public static bool IsInstalled(GameInstall g) => File.Exists(Path.Combine(LoaderDir(g), "IgniteLoader.dll"));

    /// <summary>doorstop_config.ini starts Ignite Loader (not BepInEx).</summary>
    public static bool IsActive(GameInstall g)
    {
        var target = DoorstopTarget(g);
        return target != null && target.Replace('/', '\\').TrimStart('.', '\\').StartsWith(Folder + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static string? DoorstopTarget(GameInstall g)
    {
        try
        {
            var ini = Path.Combine(g.Root, "doorstop_config.ini");
            if (!File.Exists(ini)) return null;
            var m = Regex.Match(File.ReadAllText(ini), @"(?im)^\s*target_?assembly\s*=\s*(.+?)\s*$");
            return m.Success ? m.Groups[1].Value.Trim('"') : null;
        }
        catch { return null; }
    }

    public static string? InstalledVersion(GameInstall g)
    {
        try
        {
            var dll = Path.Combine(LoaderDir(g), "IgniteLoader.dll");
            if (!File.Exists(dll)) return null;
            var v = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll);
            return v.ProductVersion?.Split('+')[0] ?? v.FileVersion;
        }
        catch { return null; }
    }

    /// <summary>"site edition" / "Nexus edition" from Ignite Loader's README, or null.</summary>
    public static string? InstalledEdition(GameInstall g)
    {
        try
        {
            var readme = Path.Combine(Dir(g), "README.txt");
            if (!File.Exists(readme)) return null;
            var first = File.ReadLines(readme).FirstOrDefault() ?? "";
            return first.Contains("site edition", StringComparison.OrdinalIgnoreCase) ? "site edition"
                 : first.Contains("Nexus edition", StringComparison.OrdinalIgnoreCase) ? "Nexus edition" : null;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // Where Ignite Loader comes from

    /// <summary>
    /// The newest Ignite Loader: the site's listing (checked with the site's SHA-256), or, in the Nexus edition, the
    /// copy built into the app (its Nexus edition, which never goes online). Null if it can't be found right now.
    /// </summary>
    public static async Task<IgnitePlan?> LatestAsync(HttpClient http, CancellationToken ct)
    {
        if (Edition.Nexus) return BuiltIn();
        try
        {
            var api = Net.Site + "index.php?p=api&";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var list = JsonNode.Parse(await http.GetStringAsync(api + "do=mods&per=48&q=" + Uri.EscapeDataString(SiteListing), cts.Token));
            var id = (list?["mods"] as JsonArray ?? new()).FirstOrDefault(m => string.Equals(m?["name"]?.GetValue<string>(), SiteListing, StringComparison.OrdinalIgnoreCase))?["id"]?.GetValue<int>() ?? 0;
            if (id <= 0) return null;
            var mod = JsonNode.Parse(await http.GetStringAsync(api + "do=mod&id=" + id, cts.Token));
            foreach (var v in mod?["versions"] as JsonArray ?? new())
            {
                if (v == null || v["current"]?.GetValue<bool>() == false || (v["kind"]?.GetValue<string>() ?? "main") != "main") continue;
                var label = v["version"]?.GetValue<string>() ?? "";
                var file = v["file"]?.GetValue<string>() ?? "";
                var url = v["download"]?.GetValue<string>() ?? "";
                var m = Regex.Match(label, @"^v?(\d+(?:\.\d+){1,3})");
                if (!m.Success || !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
                return new IgnitePlan(m.Groups[1].Value, uri, v["size"]?.GetValue<long>() ?? 0, (v["sha256"]?.GetValue<string>() ?? "").ToLowerInvariant());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch { }
        return null;
    }

    private static IgnitePlan? BuiltIn()
    {
        using var s = typeof(IgniteLoaderManager).Assembly.GetManifestResourceStream(Edition.BuiltInIgniteResource);
        if (s == null) return null;
        return new IgnitePlan(Edition.BuiltInIgnite, new Uri("builtin:///" + Edition.BuiltInIgniteResource), s.Length);
    }

    /// <summary>A newer Ignite Loader than the installed one, or null.</summary>
    public static async Task<IgnitePlan?> CheckForUpdateAsync(HttpClient http, GameInstall g, CancellationToken ct)
    {
        if (!IsInstalled(g) || !Version.TryParse(InstalledVersion(g) ?? "", out var have)) return null;
        var latest = await LatestAsync(http, ct);
        if (latest == null || !Version.TryParse(latest.Version, out var newest)) return null;
        return Norm(newest) > Norm(have) ? latest : null;
    }

    private static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    // ------------------------------------------------------------------
    // Install, update, repair, switch

    /// <summary>
    /// Installs (or updates, or repairs) Ignite Loader and makes it the loader that starts. BepInEx's starter files are
    /// kept in the app's backups first; the BepInEx folder itself isn't touched (its plugins keep loading through
    /// Ignite Loader). Settings, plugins and logs in IgniteLoader\ are kept. Only call after the user said yes.
    /// </summary>
    public static async Task<List<string>> InstallAsync(HttpClient http, GameInstall g, IgnitePlan plan, IProgress<double>? progress, CancellationToken ct)
    {
        if (g.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive before installing Ignite Loader."));
        string zip;
        if (plan.Url.Scheme == "builtin")
        {
            Directory.CreateDirectory(AppPaths.Downloads);
            zip = Path.Combine(AppPaths.Downloads, "IgniteLoader-" + plan.Version + ".zip");
            using var s = typeof(IgniteLoaderManager).Assembly.GetManifestResourceStream(Edition.BuiltInIgniteResource)
                          ?? throw new InvalidOperationException("Ignite Loader isn't built into this copy of Check BepInEx.");
            using var fs = File.Create(zip);
            s.CopyTo(fs);
        }
        else zip = await Net.DownloadFileAsync(http, plan.Url, AppPaths.Downloads, "IgniteLoader-" + plan.Version, progress, ct);

        if (plan.Sha256.Length == 64)
        {
            string got;
            await using (var fs = File.OpenRead(zip)) got = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (got != plan.Sha256)
            {
                try { File.Delete(zip); } catch { }
                throw new InvalidOperationException(L.T("The Ignite Loader download doesn't match the file on the website (it may have been cut off). Nothing was installed; try again."));
            }
        }

        var written = new List<string>();
        using var archive = ArchiveFactory.OpenArchive(zip, new SharpCompress.Readers.ReaderOptions());
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        if (!entries.Any(e => ModInstaller.Norm(e.Key ?? "").Equals("IgniteLoader/loader/IgniteLoader.dll", StringComparison.OrdinalIgnoreCase))
            || !entries.Any(e => ModInstaller.Norm(e.Key ?? "").Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.T("The downloaded file doesn't look like Ignite Loader. Nothing was installed."));

        // BepInEx's starter files go to the app's backups (for switching back), unless they're Ignite Loader's already.
        if (!IsActive(g)) SaveStarter(g, "bepinex");

        foreach (var e in entries)
        {
            var rel = ModInstaller.Norm(e.Key ?? "");
            var dest = ModInstaller.SafeJoin(g.Root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using (var src = e.OpenEntryStream())
            using (var fs = File.Create(dest)) src.CopyTo(fs);
            written.Add(rel);
        }
        Directory.CreateDirectory(Path.Combine(Dir(g), "plugins"));
        SaveStarter(g, "ignite");
        return written;
    }

    /// <summary>Puts Ignite Loader's starter files back (no download), keeping BepInEx's in the backups. False if they were never saved.</summary>
    public static bool SwitchToIgnite(GameInstall g)
    {
        if (g.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        if (!IsInstalled(g) || !HasStarter("ignite")) return false;
        if (!IsActive(g)) SaveStarter(g, "bepinex");
        RestoreStarter(g, "ignite");
        return IsActive(g);
    }

    /// <summary>
    /// Makes BepInEx the loader that starts again: BepInEx's own starter files come back from the backups, or a fresh
    /// doorstop_config.ini is written for it. Ignite Loader's files stay in IgniteLoader\ (not used) for switching back.
    /// </summary>
    public static void SwitchToBepInEx(GameInstall g)
    {
        if (g.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        if (IsActive(g)) SaveStarter(g, "ignite");
        if (HasStarter("bepinex")) RestoreStarter(g, "bepinex");
        if (IsActive(g) || DoorstopTarget(g) == null) BepInExChecker.WriteDoorstop(g);   // winhttp.dll is Doorstop 4 for both
    }

    /// <summary>Ignite Loader is installed and its starter files are in the backups, so switching to it needs no download.</summary>
    public static bool CanSwitchToIgnite(GameInstall g) => IsInstalled(g) && HasStarter("ignite");

    /// <summary>Called before BepInEx is installed over Ignite Loader: its starter files go to the backups for switching back.</summary>
    public static void KeepStarterIfActive(GameInstall g) { try { if (IsActive(g)) SaveStarter(g, "ignite"); } catch { } }

    private static bool HasStarter(string loader) => File.Exists(Path.Combine(StarterBackup(loader), "doorstop_config.ini"));

    private static void SaveStarter(GameInstall g, string loader)
    {
        var src = Path.Combine(g.Root, "doorstop_config.ini");
        if (!File.Exists(src)) return;
        var dir = StarterBackup(loader);
        Directory.CreateDirectory(dir);
        foreach (var f in Starter)
        {
            var p = Path.Combine(g.Root, f);
            if (File.Exists(p)) File.Copy(p, Path.Combine(dir, f), true);
        }
    }

    private static void RestoreStarter(GameInstall g, string loader)
    {
        var dir = StarterBackup(loader);
        foreach (var f in Starter)
        {
            var p = Path.Combine(dir, f);
            if (File.Exists(p)) File.Copy(p, Path.Combine(g.Root, f), true);
        }
    }

    // ------------------------------------------------------------------
    // What Ignite Loader wrote at the last game start

    public static string StatusFile(GameInstall g) => Path.Combine(Dir(g), "LoaderStatus.txt");
    public static string ReportFile(GameInstall g) => Path.Combine(Dir(g), "CompatibilityReport.txt");
    public static string ReportJson(GameInstall g) => Path.Combine(Dir(g), "CompatibilityReport.json");
    public static string LogFile(GameInstall g) => Path.Combine(Dir(g), "LogOutput.log");
    public static string ErrorFile(GameInstall g) => Path.Combine(Dir(g), "preloader_error.log");

    public sealed record ReportEntry(string File, string Kind, string Status, string? Name, string? Version, List<string> Problems);

    /// <summary>CompatibilityReport.json from the last game start, or null.</summary>
    public static List<ReportEntry>? ReadReport(GameInstall g)
    {
        try
        {
            var p = ReportJson(g);
            if (!File.Exists(p)) return null;
            var root = JsonNode.Parse(File.ReadAllText(p));
            var list = new List<ReportEntry>();
            foreach (var x in root?["plugins"] as JsonArray ?? new())
            {
                if (x == null) continue;
                list.Add(new ReportEntry(x["file"]?.GetValue<string>() ?? "", x["kind"]?.GetValue<string>() ?? "", x["status"]?.GetValue<string>() ?? "",
                    x["name"]?.GetValue<string>(), x["version"]?.GetValue<string>(),
                    (x["problems"] as JsonArray ?? new()).Select(p => p?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList()));
            }
            return list;
        }
        catch { return null; }
    }
}
