namespace NomadModDownloader.Core;

/// <summary>
/// One mod as it sits in BepInEx\plugins: a top-level folder, or a DLL on its own. Mods are switched off the same way
/// the in-game Mods menu does it: each of its DLLs renamed to *.dll.disabled.
/// </summary>
public sealed class ModEntry
{
    public string Rel = "";                          // BepInEx/plugins/<entry>, '/' separated
    public string Full = "";
    public bool IsFolder;
    public List<string> Dlls = new();                // switched on (full paths)
    public List<string> OffDlls = new();             // switched off (*.dll.disabled, full paths)
    public List<PluginInfo> Plugins = new();         // from the DLLs, on or off
    public string Name => Plugins.FirstOrDefault()?.Name is { Length: > 0 } n ? n : System.IO.Path.GetFileNameWithoutExtension(Full.Replace(".dll.disabled", ".dll"));
    public bool On => Dlls.Count > 0;
}

public static class ModEntries
{
    public const string OffSuffix = ".disabled";

    /// <summary>Every mod in BepInEx\plugins, leaving out BepInEx's own parts (its compatibility layers).</summary>
    public static List<ModEntry> Scan(GameInstall game)
    {
        var list = new List<ModEntry>();
        if (!Directory.Exists(game.PluginsDir)) return list;
        IEnumerable<string> tops;
        try { tops = Directory.EnumerateFileSystemEntries(game.PluginsDir).ToList(); } catch { return list; }
        foreach (var top in tops)
        {
            var isDir = Directory.Exists(top);
            var name = Path.GetFileName(top);
            if (!isDir && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".dll" + OffSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = "BepInEx/plugins/" + name;
            if (BepInEx6Support.IsAnyCompatFolder(rel)) continue;
            var e = new ModEntry { Rel = rel, Full = top, IsFolder = isDir };
            IEnumerable<string> files;
            try { files = isDir ? Directory.EnumerateFiles(top, "*", SearchOption.AllDirectories).ToList() : new List<string> { top }; } catch { continue; }
            foreach (var f in files)
            {
                if (f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) e.Dlls.Add(f);
                else if (f.EndsWith(".dll" + OffSuffix, StringComparison.OrdinalIgnoreCase)) e.OffDlls.Add(f);
                else continue;
                e.Plugins.AddRange(PluginInspector.Read(f));
            }
            if (e.Dlls.Count + e.OffDlls.Count > 0) list.Add(e);
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Switches these DLLs off (renamed to *.dll.disabled). Returns the new paths.</summary>
    public static List<string> SwitchOff(IEnumerable<string> dlls)
    {
        var done = new List<string>();
        foreach (var d in dlls)
        {
            if (!File.Exists(d)) continue;
            var to = d + OffSuffix;
            if (File.Exists(to)) File.Delete(to);
            File.Move(d, to);
            done.Add(to);
        }
        return done;
    }

    /// <summary>Switches these DLLs back on (given by their switched-on path).</summary>
    public static void SwitchOn(IEnumerable<string> dlls)
    {
        foreach (var d in dlls)
        {
            var off = d + OffSuffix;
            if (!File.Exists(off) || File.Exists(d)) continue;
            File.Move(off, d);
        }
    }
}
