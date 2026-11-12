namespace NomadModDownloader.Core;

/// <summary>
/// The in-game Mods menu (Nomad Mod Manager): which version is installed. Players get it from its page on
/// nomaddrivemods.online.
/// </summary>
public static class ModsMenuFiles
{
    public const string Guid = "akorit.nomadmodmanager";
    public static string SitePage => Edition.Nexus ? Edition.ModsMenuNexusPage : "https://nomaddrivemods.online/mod/1";

    /// <summary>The installed Mods menu's version (from its BepInPlugin attribute), or null when it isn't installed.</summary>
    public static string? InstalledVersion(GameInstall game)
    {
        try
        {
            if (!Directory.Exists(game.PluginsDir)) return null;
            var dll = Directory.EnumerateFiles(game.PluginsDir, "NomadModManager.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dll == null) return null;
            // A newer copy waiting for the next game start counts: that's what will run.
            var staged = dll + Pending.NewSuffix;
            var p = PluginInspector.Read(File.Exists(staged) ? staged : dll).FirstOrDefault(x => x.Guid == Guid) ?? PluginInspector.Read(dll).FirstOrDefault();
            return p?.Version;
        }
        catch { return null; }
    }
}
