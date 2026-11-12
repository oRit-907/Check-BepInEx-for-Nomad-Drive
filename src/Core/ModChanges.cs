using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace NomadModDownloader.Core;

/// <summary>
/// "Mod Files patcher": a small BepInEx patcher (the same code as the in-game Mods menu's patcher, under its own name
/// and folder) that applies changes waiting for a restart when the game starts: newer copies of mod files that were in
/// use (*.nmm-new), removals and switching mods on and off (BepInEx\NomadModManager\pending.txt). This app and the Mods
/// menu both leave such changes while the game runs; without a patcher built for the game's BepInEx they never apply
/// Shipped inside the app (the BepInEx 5 build).
/// </summary>
public static class ModChanges
{
    // Named for what it patches: your mod files. Its old name was NomadModChanges (removed when this one goes in).
    public const string Folder = "NomadModFiles";
    public const string LegacyFolder = "NomadModChanges";
    public static string Dll(GameInstall g) => Path.Combine(g.BepInExDir, "patchers", Folder, "NomadModFiles.dll");
    private static string LegacyDir(GameInstall g) => Path.Combine(g.BepInExDir, "patchers", LegacyFolder);
    public static bool IsInstalled(GameInstall g) => File.Exists(Dll(g));

    /// <summary>Which BepInEx the installed copy is built for (5 or 6), 0 if unknown.</summary>
    public static int Major(GameInstall g)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(Dll(g)));
            var md = pe.GetMetadataReader();
            var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
            return refs.Contains("BepInEx.Preloader.Core") || refs.Contains("BepInEx.Core") ? 6 : refs.Contains("BepInEx") ? 5 : 0;
        }
        catch { return 0; }
    }

    /// <summary>Installed and built for the game's BepInEx.</summary>
    public static bool IsReady(GameInstall g) => IsInstalled(g) && Major(g) == BepInExManager.MajorOf(g);

    /// <summary>The BepInEx 5 build. Returns the files written (relative to the game folder).</summary>
    public static List<string> Install(GameInstall g)
    {
        if (BepInExManager.MajorOf(g) != 5) throw new InvalidOperationException(L.T("Install BepInEx 5 first."));
        using var src = typeof(ModChanges).Assembly.GetManifestResourceStream("Assets.ModChanges5.dll")
                        ?? throw new InvalidOperationException("The app is missing its Mod Files patcher files.");
        var dll = Dll(g);
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        var tmp = dll + ".tmp";
        using (var fs = File.Create(tmp)) src.CopyTo(fs);
        File.Move(tmp, dll, true);
        try { if (Directory.Exists(LegacyDir(g))) Directory.Delete(LegacyDir(g), true); } catch { }   // the same patcher under its old name
        return new List<string> { Path.GetRelativePath(g.Root, dll).Replace('\\', '/') };
    }

    public static void Remove(GameInstall g)
    {
        try { var d = Path.GetDirectoryName(Dll(g))!; if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        try { if (Directory.Exists(LegacyDir(g))) Directory.Delete(LegacyDir(g), true); } catch { }
    }
}
