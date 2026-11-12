namespace NomadModDownloader.Core;

/// <summary>
/// The Nexus edition (dotnet publish -p:NexusEdition=true) keeps to Nexus Mods' file rules: it never updates itself and
/// never talks to nomaddrivemods.online. BepInEx 5 comes built into the app, so installing and repairing it needs no
/// download. The only download left is Unity Compatibility's Unity libraries (they can't be shared), and only after the
/// player says yes, from BepInEx's own server.
/// </summary>
public static class Edition
{
#if NEXUS_EDITION
    public const bool Nexus = true;
#else
    public const bool Nexus = false;
#endif
    /// <summary>The BepInEx 5 release built into the Nexus edition (official, unchanged).</summary>
    public const string BuiltInBepInEx = "5.4.23.6";
    public const string BuiltInBepInExResource = "Assets.BepInEx_win_x64_5.4.23.6.zip";
    public const string ModsMenuNexusPage = "https://www.nexusmods.com/nomaddrive/mods/51";
    public static string NexusSearch(string text) => "https://www.nexusmods.com/games/nomaddrive/mods?keyword=" + Uri.EscapeDataString(text);
}
