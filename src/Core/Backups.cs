namespace NomadModDownloader.Core;

/// <summary>A backup this app made (in %AppData%\NomadModDownloader\backups), laid out like the game folder.</summary>
public sealed record BackupInfo(string Folder, string Name, string What, DateTime When, long Bytes, int Files);

/// <summary>
/// The backups Check BepInEx makes before it changes anything (BepInEx updates and repairs, files a fix moved away,
/// an uninstall), restoring one, deleting one, and uninstalling BepInEx (into a backup, so it can come back).
/// </summary>
public static class Backups
{
    public static List<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(AppPaths.Backups)) return list;
        foreach (var dir in Directory.EnumerateDirectories(AppPaths.Backups))
        {
            try
            {
                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f)).ToList();
                if (files.Count == 0) continue;
                var name = Path.GetFileName(dir);
                list.Add(new BackupInfo(dir, name, Describe(name), Directory.GetCreationTime(dir), files.Sum(f => f.Length), files.Count));
            }
            catch { }
        }
        return list.OrderByDescending(b => b.When).ToList();
    }

    /// <summary>What a backup holds, from the name the app gave it.</summary>
    private static string Describe(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.StartsWith("uninstall-")) return L.T("BepInEx, as it was before Uninstall");
        if (n.StartsWith("bepinex-repair-")) return L.T("BepInEx before a repair");
        if (n.StartsWith("bepinex6-leftovers")) return L.T("Leftovers from BepInEx 6");
        if (n.StartsWith("bepinex-")) return L.F("BepInEx {0}, before an update", name["bepinex-".Length..]);
        if (n.StartsWith("melonloader")) return L.T("MelonLoader, moved away by a fix");
        if (n.StartsWith("other-loaders")) return L.T("Other mod loaders, moved away by a fix");
        if (n.StartsWith("crash")) return L.T("Old crash logs");
        if (n.Contains("empty")) return L.T("Empty files moved away by a fix");
        if (n.Contains("nested")) return L.T("Mods moved into place by a fix");
        if (n.Length > 0 && char.IsDigit(n[0])) return L.T("A mod's files, before Nomad Mod Downloader changed them");
        return L.T("Files moved away by a fix");
    }

    /// <summary>Files in the backup that would replace something in the game folder.</summary>
    public static List<string> WouldReplace(BackupInfo b, GameInstall game) =>
        Directory.EnumerateFiles(b.Folder, "*", SearchOption.AllDirectories)
                 .Select(f => Path.GetRelativePath(b.Folder, f))
                 .Where(rel => File.Exists(Path.Combine(game.Root, rel))).ToList();

    /// <summary>Copies the backup back into the game folder (the backup stays, so it can be used again).</summary>
    public static int Restore(BackupInfo b, GameInstall game)
    {
        if (game.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        var n = 0;
        foreach (var f in Directory.EnumerateFiles(b.Folder, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(b.Folder, f);
            var to = ModInstaller.SafeJoin(game.Root, rel.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to, true);
            n++;
        }
        return n;
    }

    public static void Delete(BackupInfo b)
    {
        var full = Path.GetFullPath(b.Folder);
        if (!full.StartsWith(Path.GetFullPath(AppPaths.Backups) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Not a backup folder.");
        Directory.Delete(full, true);
    }

    /// <summary>
    /// Takes BepInEx out of the game: its core, patchers, compatibility parts, Unity Compatibility and the doorstop
    /// files that start it, all moved into a backup. With <paramref name="withMods"/> the mods (plugins) and their
    /// settings (config) go too, leaving the plain game; otherwise they (and the Mods menu's own data) stay in the game folder, unused.
    /// </summary>
    public static (string Backup, int Moved) Uninstall(GameInstall game, bool withMods)
    {
        if (game.IsRunning()) throw new InvalidOperationException(L.T("Close Nomad Drive first, then try again."));
        var dest = Path.Combine(AppPaths.Backups, $"uninstall-{DateTime.Now:yyyyMMdd-HHmmss}");
        var moved = 0;
        void Move(string rel)
        {
            var from = Path.Combine(game.Root, rel);
            var to = Path.Combine(dest, rel);
            if (Directory.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                moved += Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Count();
                BepInExChecker.MoveDir(from, to);
            }
            else if (File.Exists(from))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to, true);
                moved++;
            }
        }
        foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" }) Move(f);
        if (withMods) Move("BepInEx");
        else
        {
            if (Directory.Exists(game.BepInExDir))
                foreach (var d in Directory.EnumerateFileSystemEntries(game.BepInExDir))
                {
                    var name = Path.GetFileName(d);
                    // Mods, their settings and the Mods menu's own data (its records and in-game browser) stay.
                    if (name is "plugins" or "config" or "NomadModManager" || name.Equals("plugins", StringComparison.OrdinalIgnoreCase) || name.Equals("config", StringComparison.OrdinalIgnoreCase)) continue;
                    Move(Path.Combine("BepInEx", name));
                }
        }
        return (dest, moved);
    }
}
