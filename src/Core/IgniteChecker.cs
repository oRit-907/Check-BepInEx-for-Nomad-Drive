using System.Text.RegularExpressions;

namespace NomadModDownloader.Core;

/// <summary>
/// The Check page when Ignite Loader is the chosen loader (3.4.0): Ignite Loader's install, what it said at the last
/// game start (LoaderStatus.txt, CompatibilityReport.json), and the BepInEx left beside it.
/// </summary>
public static class IgniteChecker
{
    public const string AreaInstall = "Ignite Loader install";
    public const string AreaMods = BepInExChecker.AreaMods;
    public const string AreaOther = BepInExChecker.AreaBreakages;
    public static readonly string[] Areas = { AreaInstall, AreaMods, AreaOther };

    public static List<CheckItem> Run(GameInstall game, string? latest)
    {
        var list = new List<CheckItem>();
        void Add(string area, CheckLevel level, string title, string detail = "", CheckFix fix = CheckFix.None, IEnumerable<string>? paths = null)
            => list.Add(new CheckItem { Area = area, Level = level, Title = title, Detail = detail, Fix = fix, Paths = paths?.ToList() ?? new() });

        if (game.IsIl2Cpp)
        {
            Add(AreaInstall, CheckLevel.Error, L.T("This copy of Nomad Drive is an IL2CPP build."),
                L.T("Ignite Loader runs on the normal (Mono) build of Nomad Drive. Try Steam's \"Verify integrity of game files\"."));
            return list;
        }

        // ---------------- install ----------------
        if (!IgniteLoaderManager.IsInstalled(game))
        {
            Add(AreaInstall, CheckLevel.Error, L.T("Ignite Loader isn't installed."),
                L.T("Ignite Loader is Nomad Drive's own mod loader. It replaces BepInEx and still runs BepInEx 5 and 6 mods (also the ones in BepInEx\\plugins) and MelonLoader mods, and it brings its own .NET libraries, so the .NET fix and Unity Compatibility aren't needed.") + "\n\n" +
                (Edition.Nexus ? L.T("The fix installs the copy built into this app (no download).") : L.T("The fix downloads it from the Nomad Drive Mods site (checked with the site's SHA-256).")) + " " +
                L.T("BepInEx stays in its folder but no longer starts; you can switch back here any time."),
                CheckFix.InstallIgnite);
            OtherLoaders(game, Add);
            return list;
        }

        var version = IgniteLoaderManager.InstalledVersion(game) ?? "?";
        var edition = IgniteLoaderManager.InstalledEdition(game);
        Add(AreaInstall, CheckLevel.Ok, L.F("Ignite Loader {0} is installed.", version) + (edition != null ? " (" + edition + ")" : ""));
        if (latest != null && Version.TryParse(latest, out var nv) && Version.TryParse(version, out var hv) && nv > hv)
            Add(AreaInstall, CheckLevel.Info, L.F("Ignite Loader {0} is available.", latest), L.F("You have {0}. Updating keeps your plugins, settings and logs.", version), CheckFix.UpdateIgnite);

        var loaderDir = IgniteLoaderManager.LoaderDir(game);
        var missing = IgniteLoaderManager.CoreFiles.Where(f => !File.Exists(Path.Combine(loaderDir, f)) || new FileInfo(Path.Combine(loaderDir, f)).Length == 0).ToList();
        if (missing.Count > 0)
            Add(AreaInstall, CheckLevel.Error, L.T("Some of Ignite Loader's own files are missing or empty."),
                L.T("Antivirus programs sometimes remove them. Missing: ") + string.Join(", ", missing), CheckFix.RepairIgnite);
        else
            Add(AreaInstall, CheckLevel.Ok, L.T("Ignite Loader's files are all there."));

        var runtime = Path.Combine(IgniteLoaderManager.Dir(game), "runtime");
        var hasRuntime = Directory.Exists(runtime) && Directory.EnumerateFiles(runtime, "mscorlib.dll", SearchOption.AllDirectories).Any();
        if (!hasRuntime)
            Add(AreaInstall, CheckLevel.Error, L.T("Ignite Loader's .NET libraries (IgniteLoader\\runtime) are missing."),
                L.T("Nomad Drive's own .NET libraries are cut down, so Ignite Loader brings full ones. Without them it can't start."), CheckFix.RepairIgnite);

        var proxy = Path.Combine(game.Root, "winhttp.dll");
        if (!File.Exists(proxy))
            Add(AreaInstall, CheckLevel.Error, L.T("winhttp.dll is missing, so Ignite Loader never starts."),
                L.T("It's the small file next to Nomad Drive.exe that starts the loader. Antivirus programs sometimes remove it."), CheckFix.RepairIgnite);
        else if (new FileInfo(proxy).Length == 0)
            Add(AreaInstall, CheckLevel.Error, L.T("winhttp.dll is empty (0 bytes)."), L.T("Probably blocked by antivirus."), CheckFix.RepairIgnite);

        var ini = BepInExChecker.DoorstopIni(game);
        var target = IgniteLoaderManager.DoorstopTarget(game);
        if (!File.Exists(ini) || target == null)
            Add(AreaInstall, CheckLevel.Error, L.T("doorstop_config.ini is missing."), L.T("It tells winhttp.dll which loader to start."), CheckFix.RepairIgnite);
        else if (!IgniteLoaderManager.IsActive(game))
            Add(AreaInstall, CheckLevel.Error, L.T("BepInEx starts instead of Ignite Loader."),
                L.F("doorstop_config.ini starts {0}. The fix makes Ignite Loader the loader that starts; BepInEx's files are kept so you can switch back.", target),
                IgniteLoaderManager.CanSwitchToIgnite(game) ? CheckFix.SwitchToIgnite : CheckFix.RepairIgnite);
        else if (Regex.IsMatch(File.ReadAllText(ini), @"(?im)^\s*enabled\s*=\s*false"))
            Add(AreaInstall, CheckLevel.Warning, L.T("Ignite Loader is switched off (enabled = false in doorstop_config.ini)."), L.T("Mods won't load until it's switched back on."), CheckFix.EnableDoorstop);
        else if (!File.Exists(Path.Combine(game.Root, BepInExChecker.NormPathPublic(target))))
            Add(AreaInstall, CheckLevel.Error, L.T("doorstop_config.ini points at a file that isn't there."), L.F("target_assembly = {0}", target), CheckFix.RepairIgnite);
        else
            Add(AreaInstall, CheckLevel.Ok, L.T("doorstop_config.ini starts Ignite Loader."));

        // ---------------- the last game start ----------------
        var status = IgniteLoaderManager.StatusFile(game);
        var log = IgniteLoaderManager.LogFile(game);
        var err = IgniteLoaderManager.ErrorFile(game);
        if (!File.Exists(status) && !File.Exists(log))
            Add(AreaInstall, CheckLevel.Info, L.T("Ignite Loader hasn't run yet."), L.T("Play the game once to the main menu, then check again."), CheckFix.Play);
        else
        {
            var text = SafeRead(status);
            var bad = Regex.Matches(text, @"(?im)^.*\b(error|failed|can't|cannot|couldn't)\b.*$").Select(m => m.Value.Trim()).Distinct().Take(8).ToList();
            if (bad.Count > 0)
                Add(AreaInstall, CheckLevel.Warning, L.T("Ignite Loader reported a problem at the last game start."), string.Join("\n", bad) + "\n\n" + L.T("Full details: IgniteLoader\\LoaderStatus.txt"), CheckFix.RepairIgnite, new[] { status });
            else if (text.Length > 0)
                Add(AreaInstall, CheckLevel.Ok, L.T("Ignite Loader started normally last time."), text.Trim());
            if (File.Exists(err) && (!File.Exists(log) || File.GetLastWriteTimeUtc(err) >= File.GetLastWriteTimeUtc(log).AddMinutes(-1)))
                Add(AreaInstall, CheckLevel.Error, L.T("Ignite Loader failed to start mods at the last game start."),
                    string.Join("\n", SafeRead(err).Split('\n').Take(12)) + "\n\n" + L.T("If it happens again, use \"Copy for a bug report\" and send it to AkoRit."), CheckFix.RepairIgnite, new[] { err });
        }

        // ---------------- mods ----------------
        var report = IgniteLoaderManager.ReadReport(game);
        if (report != null)
        {
            var mods = report.Where(r => r.Kind is not ("Library" or "NativeDll")).ToList();
            var cant = mods.Where(r => r.Status == "Incompatible").ToList();
            var warn = mods.Where(r => r.Status == "Warning").ToList();
            var converted = mods.Count(r => r.Status is "OkViaBepInEx6Backend" or "OkViaIl2CppBackend");
            Add(AreaMods, cant.Count > 0 ? CheckLevel.Warning : CheckLevel.Ok,
                L.F("{0} mod(s) checked at the last game start: {1} can run, {2} can't.", mods.Count, mods.Count - cant.Count, cant.Count) + (converted > 0 ? " " + L.F("({0} converted)", converted) : ""),
                L.T("Ignite Loader checks every mod before the game starts. Full report: IgniteLoader\\CompatibilityReport.txt"),
                cant.Count + warn.Count > 0 ? CheckFix.OpenIgniteReport : CheckFix.None);
            foreach (var r in cant.Take(30))
                Add(AreaMods, CheckLevel.Warning, L.F("{0} can't run.", Label(r)), string.Join("\n", r.Problems.Select(p => "• " + p)), CheckFix.OpenIgniteReport);
            foreach (var r in warn.Take(30))
                Add(AreaMods, CheckLevel.Info, L.F("{0}: worth a look.", Label(r)), string.Join("\n", r.Problems.Select(p => "• " + p)), CheckFix.OpenIgniteReport);
        }

        // ---------------- BepInEx beside it ----------------
        if (Directory.Exists(game.BepInExDir))
        {
            var oldPlugins = Path.Combine(game.BepInExDir, "plugins");
            var count = Directory.Exists(oldPlugins) ? Directory.EnumerateFiles(oldPlugins, "*.dll", SearchOption.AllDirectories).Count() : 0;
            Add(AreaOther, CheckLevel.Info, L.T("BepInEx is still in the game folder, switched off."),
                (count > 0 ? L.F("Its {0} plugin file(s) in BepInEx\\plugins still load through Ignite Loader. ", count) : "") +
                L.T("To go back to BepInEx, pick BepInEx 5 at the top of this page."));
        }
        OtherLoaders(game, Add);
        return list;
    }

    private static string Label(IgniteLoaderManager.ReportEntry r) =>
        !string.IsNullOrEmpty(r.Name) ? r.Name + (string.IsNullOrEmpty(r.Version) ? "" : " " + r.Version) : r.File;

    /// <summary>Another loader that would also start (MelonLoader's version.dll, other proxies).</summary>
    private static void OtherLoaders(GameInstall game, Action<string, CheckLevel, string, string, CheckFix, IEnumerable<string>?> add)
    {
        if (File.Exists(Path.Combine(game.Root, "version.dll")) && Directory.Exists(Path.Combine(game.Root, "MelonLoader")))
            add(AreaOther, CheckLevel.Warning, L.T("MelonLoader is installed too."),
                L.T("Ignite Loader runs MelonLoader mods itself (from IgniteLoader\\MelonMods, and the game's Mods folder when MelonLoader isn't installed). With both, MelonLoader loads its mods a second time. Remove version.dll and the MelonLoader folder, or move the mods to IgniteLoader\\MelonMods."),
                CheckFix.None, null);
        foreach (var p in new[] { "winmm.dll", "dinput8.dll" })
            if (File.Exists(Path.Combine(game.Root, p)))
                add(AreaOther, CheckLevel.Info, L.F("{0} is in the game folder.", p), L.T("Other mod loaders and tools use this name. If mods load twice or the game crashes at start, it's worth a look."), CheckFix.None, null);
    }

    private static string SafeRead(string p) { try { return File.Exists(p) ? File.ReadAllText(p) : ""; } catch { return ""; } }
}
