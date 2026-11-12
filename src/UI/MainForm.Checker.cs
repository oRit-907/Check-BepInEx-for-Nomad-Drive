using System.Diagnostics;
using NomadModDownloader.Core;
using NomadModDownloader.UI;


namespace NomadModDownloader;

/// <summary>The "Check BepInEx" page: checks the install for everything that stops mods loading, with fixes.</summary>
internal sealed partial class MainForm
{
    private ListView _checkList = null!;
    private TextBox _checkDetail = null!;
    private Label _checkHeader = null!;
    private Button _checkFix = null!, _checkFixAll = null!;
    private List<CheckItem> _checks = new();
    private string? _latestBepInEx;
    private bool _latestLooked;

    private Control BuildCheckerPage()
    {
        var page = new GlassPanel { Padding = new Padding(16, 16, 16, 12) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        var again = Theme.Button(L.T("Check again"), true);
        again.Click += async (_, _) => await RunExclusive(() => RefreshChecksAsync(lookOnline: true));
        _checkFix = Theme.Button(L.T("Fix this"));
        _checkFix.Enabled = false;
        _checkFix.Click += async (_, _) => { if (SelectedCheck() is { } c) await RunExclusive(() => FixChecksAsync(new[] { c })); };
        _checkFixAll = Theme.Button(L.T("Fix all"));
        _checkFixAll.Enabled = false;
        _checkFixAll.Click += async (_, _) => await RunExclusive(() => FixChecksAsync(_checks.Where(c => c.Level >= CheckLevel.Warning && c.FileFix).ToList()));
        var folder = Theme.Button(L.T("Open game folder"));
        folder.Click += (_, _) => { if (_game != null) Web.Open(_game.Root); };
        var copy = Theme.Button(L.T("Copy for a bug report"));
        copy.Click += (_, _) => CopyCheckReport();
        var verify = Theme.Button(L.T("Verify game files"));
        verify.Click += (_, _) => VerifyGameFiles();
        foreach (var b in new[] { again, _checkFix, _checkFixAll, folder, verify, copy }) { b.Margin = new Padding(0, 0, 8, 6); toolbar.Controls.Add(b); }

        _checkHeader = new Label { Dock = DockStyle.Top, Height = Theme.Bold.Height + 16, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _checkList = Theme.List();
        _checkList.Columns.Add("", 44);
        _checkList.Columns.Add(L.T("Check"), 560);
        _checkList.Columns.Add(L.T("Fix"), 220);
        _checkList.SelectedIndexChanged += (_, _) => ShowCheckDetail();
        _checkList.DoubleClick += async (_, _) => { if (SelectedCheck() is { Fix: not CheckFix.None } c) await RunExclusive(() => FixChecksAsync(new[] { c })); };
        _checkDetail = new TextBox { Dock = DockStyle.Bottom, Height = 150, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body, WordWrap = true };
        _checkDetail.HandleCreated += (_, _) => Theme.DarkScrollbars(_checkDetail);
        var gap = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Color.Transparent };
        page.Controls.Add(_checkList);
        page.Controls.Add(gap);
        page.Controls.Add(_checkDetail);
        page.Controls.Add(_checkHeader);
        page.Controls.Add(toolbar);
        return page;
    }

    /// <returns>How many errors and warnings were found.</returns>
    private async Task<int> RefreshChecksAsync(bool lookOnline)
    {
        if (_checkList == null) return 0;
        var game = _game;
        if (game == null)
        {
            _checks = new();
            _checkHeader.Text = L.T("Choose your Nomad Drive folder first.");
            _checkHeader.ForeColor = Theme.Warn;
            _checkList.Items.Clear();
            SetBadge("Check BepInEx", null);
            return 0;
        }
        if (lookOnline || !_latestLooked)
        {
            _latestLooked = true;
            try { _latestBepInEx = (await BepInExManager.LatestAsync(_http, game, _closing.Token))?.Version; } catch { }
        }
        _manifest = Manifest.Load();
        try { _checks = await Task.Run(() => BepInExChecker.Run(game, _manifest, _latestBepInEx)); }
        catch (Exception ex) { Log(L.F("Couldn't finish the BepInEx check: {0}", ex.Message)); _checks = new(); }

        _checkList.BeginUpdate();
        _checkList.Items.Clear();
        foreach (var area in BepInExChecker.Areas)
        {
            var items = _checks.Where(c => c.Area == area).OrderByDescending(c => c.Level).ToList();
            if (items.Count == 0) continue;
            var head = L.T(area);
            _checkList.Items.Add(new ListViewItem(head) { Tag = new ListHeader(head) });
            foreach (var c in items)
            {
                var (mark, color) = c.Level switch
                {
                    CheckLevel.Error => ("✕", Theme.Bad),
                    CheckLevel.Warning => ("!", Theme.Warn),
                    CheckLevel.Info => ("i", Theme.Accent),
                    _ => ("✓", Theme.Good),
                };
                var item = new ListViewItem(mark) { Tag = c, ForeColor = color };
                item.SubItems.Add(c.Title).ForeColor = c.Level == CheckLevel.Ok ? Theme.Muted : Theme.Text;
                item.SubItems.Add(FixLabel(c.Fix)).ForeColor = Theme.Accent;
                _checkList.Items.Add(item);
            }
        }
        _checkList.EndUpdate();
        _checkDetail.Text = "";
        _checkFix.Enabled = false;

        var bad = _checks.Count(c => c.Level >= CheckLevel.Warning);
        var errors = _checks.Count(c => c.Level == CheckLevel.Error);
        _checkHeader.Text = errors > 0 ? L.F("{0} problem(s) stop mods from working. Select one to see what's wrong.", bad)
                          : bad > 0 ? L.F("BepInEx works, with {0} thing(s) worth fixing.", bad)
                          : L.T("✓  BepInEx is set up correctly for Nomad Drive.");
        _checkHeader.ForeColor = errors > 0 ? Theme.Bad : bad > 0 ? Theme.Warn : Theme.Good;
        _checkFixAll.Enabled = _checks.Any(c => c.Level >= CheckLevel.Warning && c.FileFix);
        SetBadge("Check BepInEx", errors > 0 ? errors.ToString() : null);
        return bad;
    }

    private CheckItem? SelectedCheck() => _checkList.SelectedItems.Count > 0 ? _checkList.SelectedItems[0].Tag as CheckItem : null;

    private void ShowCheckDetail()
    {
        var c = SelectedCheck();
        _checkFix.Enabled = c != null && c.Fix != CheckFix.None;
        _checkFix.Text = c != null && c.Fix != CheckFix.None ? FixLabel(c.Fix) : L.T("Fix this");
        _checkDetail.Text = c == null ? "" : (c.Title + (c.Detail.Length > 0 ? "\r\n\r\n" + c.Detail : "")).Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private static string FixLabel(CheckFix f) => f switch
    {
        CheckFix.None => "",
        CheckFix.InstallBepInEx => L.T("Install BepInEx"),
        CheckFix.RepairBepInEx => L.T("Repair BepInEx"),
        CheckFix.UpdateBepInEx => L.T("Update BepInEx"),
        CheckFix.InstallCompat6 => L.T("Install BepInEx 6 support"),
        CheckFix.OpenInstalled => L.T("Open Mods not loading"),
        CheckFix.OpenModsMenuPage => L.T("Get the Mods menu"),
        CheckFix.WriteDoorstop => L.T("Fix doorstop_config.ini"),
        CheckFix.EnableDoorstop => L.T("Switch BepInEx on"),
        CheckFix.InstallDotNetFix => L.T("Set up the .NET fix"),
        CheckFix.InstallUnityCompat => L.T("Set up Unity Compatibility"),
        CheckFix.InstallModChanges => L.T("Set up the Mod Files patcher"),
        CheckFix.EnableDiskLog => L.T("Switch the log on"),
        CheckFix.Unblock => L.T("Unblock files"),
        CheckFix.RemoveMelonLoader => L.T("Move MelonLoader away"),
        CheckFix.RemoveOtherLoader => L.T("Move them away"),
        CheckFix.RemoveEmptyFiles => L.T("Move them away"),
        CheckFix.FlattenNested => L.T("Move into place"),
        CheckFix.ClearOldCrashLogs => L.T("Clear them"),
        CheckFix.OpenProblems => L.T("Open Mods not loading"),
        CheckFix.Play => L.T("Play"),
        _ => "",
    };

    /// <summary>Shows exactly what will change, waits for a yes, then fixes.</summary>
    private async Task FixChecksAsync(IReadOnlyList<CheckItem> items)
    {
        var game = RequireGame();
        if (game == null || items.Count == 0) return;

        if (items.Count == 1 && items[0].Fix is CheckFix.OpenProblems or CheckFix.OpenInstalled) { ShowPage("Mods not loading"); return; }
        if (items.Count == 1 && items[0].Fix == CheckFix.OpenModsMenuPage) { Web.Open(ModsMenuFiles.SitePage); return; }
        if (items.Count == 1 && items[0].Fix == CheckFix.Play) { await PlayAsync(); return; }
        items = items.Where(i => i.FileFix).ToList();
        if (items.Count == 0) return;
        if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // One BepInEx download covers install, repair and update; the other fixes are quick file changes.
        static bool IsBep(CheckFix f) => f is CheckFix.InstallBepInEx or CheckFix.RepairBepInEx or CheckFix.UpdateBepInEx;
        var bep = items.Where(i => IsBep(i.Fix)).OrderBy(i => i.Fix).FirstOrDefault();
        var rest = BepInExChecker.Merge(items.Where(i => !IsBep(i.Fix)));

        BepInExPlan? plan = null;
        if (bep != null)
        {
            // Install, update and repair all give BepInEx 5 (a broken BepInEx 6 is reinstalled as BepInEx 5); the list below is the confirmation.
            plan = await BepInExManager.Plan5Async(_http, game, _closing.Token);
        }

        var lines = new List<string>();
        if (bep != null && plan != null)
            lines.Add("• " + (bep.Fix switch
            {
                CheckFix.InstallBepInEx => L.F("Install BepInEx {0} ({1}) from {2}", plan.Shown, plan.Architecture, plan.Url.Host),
                CheckFix.UpdateBepInEx => L.F("Update BepInEx to {0} (plugins, settings and doorstop_config.ini are kept)", plan.Shown),
                _ => L.F("Reinstall BepInEx {0} ({1}). The old files go to a backup; plugins and settings are kept.", plan.Shown, plan.Architecture),
            }));
        foreach (var i in rest) lines.Add("• " + FixLabel(i.Fix) + ": " + i.Title);
        var ask = L.T("This will:") + "\n\n" + string.Join("\n", lines.Distinct()) + "\n\n" + L.T("Only files in the game folder change, and anything removed goes to a backup first. Go ahead?");
        if (MessageBox.Show(this, ask, L.T("Fix BepInEx"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { SetStatus(L.T("Nothing changed.")); return; }

        var done = new List<string>();
        try
        {
            if (bep != null && plan != null)
            {
                SetStatus(L.F("Downloading BepInEx {0}…", plan.Shown));
                if (bep.Fix == CheckFix.UpdateBepInEx)
                {
                    var w = await BepInExManager.UpdateAsync(_http, game, plan, Progress(), _closing.Token);
                    done.Add(L.F("Updated BepInEx to {0} ({1} files).", plan.Shown, w.Count));
                }
                else if (bep.Fix is CheckFix.RepairBepInEx)
                {
                    var (w, backup) = await BepInExManager.RepairAsync(_http, game, plan, Progress(), _closing.Token);
                    done.Add(L.F("Reinstalled BepInEx {0} ({1} files). Old files: {2}", plan.Shown, w.Count, backup));
                }
                else
                {
                    var w = await BepInExManager.InstallAsync(_http, game, plan, Progress(), _closing.Token);
                    done.Add(L.F("Installed BepInEx {0} ({1} files).", plan.Shown, w.Count));
                }
                HideProgress();
                EnsureCompatLayer(game); // BepInEx 6 support and the BepInEx 5 builds of the helpers (also after replacing a BepInEx 6 install)
                BepInExChecker.ClearCrashLogs(game, items.SelectMany(i => i.Paths)); // the reinstall dealt with the crash
            }
            // Ordered so a doorstop rewrite happens before the .NET fix updates it.
            foreach (var i in rest.OrderBy(i => i.Fix == CheckFix.InstallDotNetFix ? 1 : 0))
            {
                if (i.Fix == CheckFix.InstallUnityCompat) continue; // downloads: below

                // A reinstall above already wrote a fresh doorstop_config.ini and the .NET fix.
                if (bep != null && i.Fix is CheckFix.WriteDoorstop or CheckFix.EnableDoorstop or CheckFix.InstallCompat6) continue;
                var msg = BepInExChecker.Apply(i, game);
                if (msg.Length > 0) done.Add(msg);
            }
            if (rest.Any(i => i.Fix == CheckFix.InstallUnityCompat) && !(bep != null && UnityCompat.IsInstalled(game) && !UnityCompat.IsOutdated(game)))
            {
                // Nexus edition: Unity's libraries are the one download, so say what and from where, and ask first.
                if (Edition.Nexus && MessageBox.Show(this,
                        L.T("Unity Compatibility needs Unity's own libraries for Nomad Drive's Unity version. They can't be shared, so Check BepInEx downloads them from BepInEx's official server (unity.bepinex.dev), once. Nothing else is downloaded.\n\nDownload them now?"),
                        L.T("Set up Unity Compatibility"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    throw new OperationCanceledException(L.T("Unity Compatibility wasn't set up: you chose not to download Unity's libraries."));
                SetStatus(L.T("Setting up Unity Compatibility…"));
                var uw = await UnityCompat.InstallAsync(_http, game, Progress(), _closing.Token);
                HideProgress();
                done.Add(L.F("Set up Unity Compatibility ({0} item(s)). It finishes on the next game start.", uw.Count));
            }
        }
        catch (Exception ex)
        {
            HideProgress();
            Log(L.F("Fix failed: {0}", ex.Message));
            MessageBox.Show(this, L.F("That didn't work: {0}", ex.Message), L.T("Fix BepInEx"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        foreach (var d in done) Log(d);
        RefreshGame();
        var left = await RefreshChecksAsync(lookOnline: false);
        SetStatus(done.Count == 0 ? L.T("Nothing changed.") : left == 0 ? L.T("Fixed. BepInEx is set up correctly for Nomad Drive.") : L.F("Fixed {0} thing(s). {1} still need a look.", done.Count, left), good: done.Count > 0 && left == 0);
    }

    /// <summary>
    /// Steam's "Verify integrity of game files" for Nomad Drive: it re-downloads any of the game's own files that are
    /// damaged or changed. BepInEx and mods are extra files, so Steam leaves them alone.
    /// </summary>
    private void VerifyGameFiles()
    {
        var game = RequireGame();
        if (game == null) return;
        if (!GameLauncher.IsSteamCopy(game))
        {
            MessageBox.Show(this, L.T("This copy of Nomad Drive isn't from Steam, so Steam can't check it. Reinstall it from where you got it if its files are damaged."), L.T("Verify game files"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var ask = L.T("Steam checks every Nomad Drive game file and downloads again any that are damaged or changed. It can take a few minutes.")
                  + "\n\n" + L.T("BepInEx, your mods and your saves aren't game files, so Steam leaves them alone.") + "\n\n" + L.T("Open Steam to verify now?");
        if (MessageBox.Show(this, ask, L.T("Verify game files"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("steam://validate/" + GameLocator.SteamAppId) { UseShellExecute = true });
            Log(L.T("Asked Steam to verify Nomad Drive's game files."));
            SetStatus(L.T("Steam is verifying the game files. When it's done, press Check again."));
        }
        catch (Exception ex) { ShowError(L.T("Verify game files"), ex); }
    }

    private void CopyCheckReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Check BepInEx {Updater.Current.ToString(3)}");
        if (_game != null) sb.AppendLine("Game: " + _game.Root);
        foreach (var area in BepInExChecker.Areas)
        {
            var items = _checks.Where(c => c.Area == area).ToList();
            if (items.Count == 0) continue;
            sb.AppendLine().AppendLine("[" + area + "]");
            foreach (var c in items)
            {
                sb.AppendLine($"{(c.Level switch { CheckLevel.Error => "ERROR", CheckLevel.Warning => "WARN ", CheckLevel.Info => "INFO ", _ => "OK   " })} {c.Title}");
                if (c.Level >= CheckLevel.Warning && c.Detail.Length > 0) foreach (var l in c.Detail.Split('\n')) sb.AppendLine("      " + l.TrimEnd());
            }
        }
        try { Clipboard.SetText(sb.ToString()); SetStatus(L.T("Copied. Paste it into your bug report."), good: true); }
        catch { SetStatus(L.T("Couldn't copy to the clipboard.")); }
    }
}
