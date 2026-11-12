using System.IO.Compression;
using System.Text;

namespace NomadModDownloader.Core;

/// <summary>
/// "Export logs for mod devs" from the Downloader, for when the game won't even start: one zip with the BepInEx log,
/// Unity's Player.log (this run and the one before), the Mods menu's records, the loader's config, the installed
/// mods with versions and this app's own log. The Windows user name is replaced with &lt;user&gt; in every file.
/// </summary>
public static class LogExport
{
    public static string Run(GameInstall? game, IEnumerable<InstalledMod> installed, string checks)
    {
        var name = $"NomadDrive-logs-{DateTime.Now:yyyy-MM-dd_HH-mm}.zip";
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(Directory.Exists(desktop) ? desktop : AppPaths.Root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        void Text(string entry, string text)
        {
            var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(Redact(text));
        }
        void Copy(string entry, string? file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var r = new StreamReader(fs);
                Text(entry, r.ReadToEnd());
            }
            catch (Exception ex) { Text(entry + ".unreadable.txt", $"Couldn't read {file}: {ex.Message}"); }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Nomad Drive logs, exported by Nomad Mod Downloader {Updater.Current.ToString(3)} on {DateTime.Now:yyyy-MM-dd HH:mm}.");
        sb.AppendLine("Send this zip to the mod's author (Nexus Bugs tab, the website's Report a bug, or Discord). Your Windows user name was replaced with <user>.");
        sb.AppendLine();
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($"Game folder: {game?.Root ?? "not found"}");
        if (game != null)
        {
            var bep = BepInExManager.Check(game);
            sb.AppendLine($"BepInEx: {bep.State} {bep.Version}");
        }
        Text("README.txt", sb.ToString());

        var mods = new StringBuilder();
        foreach (var m in installed.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var src = m.SiteId > 0 ? $"website #{m.SiteId} (file {m.SiteVersionId})" : m.ModId > 0 ? $"Nexus #{m.ModId} (file {m.FileId})" : "by hand";
            mods.AppendLine($"{(m.Disabled ? "[off]" : "[on] ")} {m.Name}  v{m.Version}  — {src}  — {m.Location}");
            foreach (var f in m.Files) mods.AppendLine($"      {f}");
        }
        Text("installed-by-downloader.txt", mods.ToString());
        if (checks.Length > 0) Text("check-bepinex.txt", checks);
        Copy("downloader-log.txt", AppPaths.LogFile);

        if (game != null)
        {
            var bepDir = game.BepInExDir;
            Copy("BepInEx/LogOutput.log", Path.Combine(bepDir, "LogOutput.log"));
            Copy("BepInEx/LogOutput.log.1", Path.Combine(bepDir, "LogOutput.log.1"));
            Copy("BepInEx/BepInEx.cfg", Path.Combine(bepDir, "config", "BepInEx.cfg"));
            Copy("BepInEx/NomadModManager.cfg", Directory.Exists(Path.Combine(bepDir, "config")) ? Directory.GetFiles(Path.Combine(bepDir, "config"), "*NomadModManager*.cfg").FirstOrDefault() : null);
            Copy("doorstop_config.ini", Path.Combine(game.Root, "doorstop_config.ini"));
            var menu = Pending.Dir(game.Root);
            foreach (var f in new[] { "pending.txt", "activity.log", "community.json", "match-host.json" }) Copy("NomadModManager/" + f, Path.Combine(menu, f));
            try
            {
                var plugins = new StringBuilder();
                foreach (var f in Directory.EnumerateFiles(game.PluginsDir, "*", SearchOption.AllDirectories).OrderBy(x => x))
                {
                    var fi = new FileInfo(f);
                    plugins.AppendLine($"{Path.GetRelativePath(game.Root, f)}  {fi.Length:N0} bytes  {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                }
                Text("BepInEx/plugins-files.txt", plugins.ToString());
            }
            catch { }
            // Unity's own log: LocalLow\<company>\<product>\Player.log (names from the game's app.info).
            try
            {
                var info = File.ReadAllLines(Path.Combine(game.DataFolder, "app.info"));
                if (info.Length >= 2)
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", info[0].Trim(), info[1].Trim());
                    Copy("Unity/Player.log", Path.Combine(dir, "Player.log"));
                    Copy("Unity/Player-prev.log", Path.Combine(dir, "Player-prev.log"));
                }
            }
            catch { }
        }
        return path;
    }

    private static string Redact(string text)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(home)) text = text.Replace(home, @"C:\Users\<user>", StringComparison.OrdinalIgnoreCase);
        if (user.Length > 1) text = text.Replace(@"\" + user + @"\", @"\<user>\", StringComparison.OrdinalIgnoreCase).Replace("/" + user + "/", "/<user>/", StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
