using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>
/// "Mods not loading": every mod in BepInEx\plugins with whether it started at the last game start and, if it didn't,
/// why and how to fix it; then the errors and crashes from the game's logs.
/// </summary>
internal sealed partial class MainForm
{
    private ListView _loadList = null!;
    private TextBox _loadDetail = null!;
    private Label _loadHeader = null!;
    private Button _loadFix = null!;
    private int _notStarting, _clashCount;

    /// <summary>A mod's row: where it is, and what the doctor said.</summary>
    private sealed record LoadRow(string Name, string Rel, bool Disabled, InstalledMod? Tracked, LoadDiagnosis Diagnosis);
    private sealed record LogRow(Problem Problem);
    private sealed record ClashRow(Clash Clash);

    private Control BuildLoadingPage()
    {
        var page = new GlassPanel { Padding = new Padding(16, 16, 16, 12) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        var again = Theme.Button(L.T("Check again"), true);
        again.Click += (_, _) => RefreshLoading();
        _loadFix = Theme.Button(L.T("Fix"));
        _loadFix.Enabled = false;
        _loadFix.Click += async (_, _) => await RunExclusive(FixSelectedLoadAsync);
        var log = Theme.Button(L.T("Open BepInEx log"));
        log.Click += (_, _) => { if (_game != null && File.Exists(LogAnalyzer.BepInExLog(_game))) Web.Open(LogAnalyzer.BepInExLog(_game)); else SetStatus(L.T("There's no BepInEx log yet. Play the game once first.")); };
        var folder = Theme.Button(L.T("Open mods folder"));
        folder.Click += (_, _) => { if (_game != null) { Directory.CreateDirectory(_game.PluginsDir); Web.Open(_game.PluginsDir); } };
        var copy = Theme.Button(L.T("Copy for a bug report"));
        copy.Click += (_, _) => CopyLoadReport();
        foreach (var b in new[] { again, _loadFix, log, folder, copy }) { b.Margin = new Padding(0, 0, 8, 6); toolbar.Controls.Add(b); }

        _loadHeader = new Label { Dock = DockStyle.Top, Height = Theme.Bold.Height + 16, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _loadList = Theme.List();
        _loadList.Columns.Add("", 44);
        _loadList.Columns.Add(L.T("Mod"), 300);
        _loadList.Columns.Add(L.T("Last game start"), 420);
        _loadList.Columns.Add(L.T("Fix"), 200);
        _loadList.SelectedIndexChanged += (_, _) => ShowLoadDetail();
        _loadList.DoubleClick += async (_, _) => { if (_loadFix.Enabled) await RunExclusive(FixSelectedLoadAsync); };
        _loadDetail = new TextBox { Dock = DockStyle.Bottom, Height = 150, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Body, WordWrap = true };
        _loadDetail.HandleCreated += (_, _) => Theme.DarkScrollbars(_loadDetail);
        var gap = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Color.Transparent };
        page.Controls.Add(_loadList);
        page.Controls.Add(gap);
        page.Controls.Add(_loadDetail);
        page.Controls.Add(_loadHeader);
        page.Controls.Add(toolbar);
        return page;
    }

    private void RefreshLoading()
    {
        if (_loadList == null) return;
        _loadList.Items.Clear();
        _loadDetail.Text = "";
        _loadFix.Enabled = false;
        var game = _game;
        if (game == null) { _loadHeader.Text = L.T("Choose your Nomad Drive folder first."); _loadHeader.ForeColor = Theme.Warn; return; }

        _manifest = Manifest.Load();
        try { if (ModInstaller.Reconcile(_manifest, game)) _manifest.Save(); } catch { }
        ModLoadDoctor.Context ctx;
        try { ctx = ModLoadDoctor.Gather(game, _manifest); }
        catch (Exception ex) { Log(L.F("Couldn't read the last game session: {0}", ex.Message)); _loadHeader.Text = L.T("Couldn't read the last game session."); return; }

        var rows = new List<LoadRow>();
        LoadDiagnosis? Diagnose(IEnumerable<string> files, bool disabled) { try { return ModLoadDoctor.Diagnose(ctx, files, disabled); } catch { return null; } }
        foreach (var m in _manifest.Mods)
            if (Diagnose(m.Files, m.Disabled) is { } d) rows.Add(new LoadRow(m.Name, m.Location, m.Disabled, m, d));
        foreach (var (rel, disabled) in ModInstaller.FindUntracked(game, _manifest))
        {
            if (BepInEx6Support.IsAnyCompatFolder(rel)) continue; // part of BepInEx, not a mod
            if (Diagnose(new[] { rel }, disabled) is not { } d) continue;
            var name = d.Plugins.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(rel);
            rows.Add(new LoadRow(name, rel, disabled, null, d));
        }

        SessionReport? report = null;
        try { report = LogAnalyzer.Analyze(game, _manifest); } catch (Exception ex) { Log(L.F("Couldn't read the game's logs: {0}", ex.Message)); }

        static int Rank(LoadState s) => s switch { LoadState.DidntStart => 0, LoadState.StartedWithErrors => 1, LoadState.InstalledSince or LoadState.NoSession => 2, LoadState.Started => 3, _ => 4 };
        _loadList.BeginUpdate();
        var head = L.F("Mods ({0})", rows.Count(r => r.Diagnosis.State != LoadState.SupportFiles));
        _loadList.Items.Add(new ListViewItem(head) { Tag = new ListHeader(head) });
        foreach (var r in rows.OrderBy(r => Rank(r.Diagnosis.State)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var d = r.Diagnosis;
            var (mark, color) = d.State switch
            {
                LoadState.Started => ("✓", Theme.Good),
                LoadState.StartedWithErrors => ("!", Theme.Warn),
                LoadState.DidntStart => ("✕", Theme.Bad),
                _ => ("•", Theme.Muted),
            };
            var item = new ListViewItem(mark) { Tag = r, ForeColor = color };
            item.SubItems.Add(r.Name).ForeColor = d.State is LoadState.Off or LoadState.SupportFiles ? Theme.Muted : Theme.Text;
            item.SubItems.Add(d.Short).ForeColor = color;
            item.SubItems.Add(FixLabel(r) ?? "").ForeColor = Theme.Accent;
            _loadList.Items.Add(item);
        }
        List<Clash> clashes;
        try { clashes = ClashFinder.Find(game); } catch (Exception ex) { Log(L.F("Couldn't look for clashes: {0}", ex.Message)); clashes = new(); }
        if (clashes.Count > 0)
        {
            var hc = L.F("Duplicates and clashes ({0})", clashes.Count);
            _loadList.Items.Add(new ListViewItem(hc) { Tag = new ListHeader(hc) });
            foreach (var c in clashes.OrderBy(c => c.Kind))
            {
                var serious = c.Kind is ClashKind.Duplicate or ClashKind.TwoVersions;
                var item = new ListViewItem(serious ? "!" : "i") { Tag = new ClashRow(c), ForeColor = serious ? Theme.Warn : Theme.Accent };
                item.SubItems.Add(string.Join(", ", c.Mods));
                item.SubItems.Add(c.Title);
                item.SubItems.Add(c.FixRel != null ? L.T("Switch off the older copy") : "").ForeColor = Theme.Accent;
                _loadList.Items.Add(item);
            }
        }
        _clashCount = clashes.Count(c => c.Kind is ClashKind.Duplicate or ClashKind.TwoVersions);

        var problems = report?.Problems.Where(p => p.Level >= ProblemLevel.Error).OrderByDescending(p => p.Level).ToList() ?? new();
        if (problems.Count > 0)
        {
            var h2 = L.F("Errors in the last game session ({0})", problems.Count);
            _loadList.Items.Add(new ListViewItem(h2) { Tag = new ListHeader(h2) });
            foreach (var p in problems)
            {
                var item = new ListViewItem(p.Level == ProblemLevel.Crash ? "✕" : "!") { Tag = new LogRow(p), ForeColor = p.Level == ProblemLevel.Crash ? Theme.Bad : Theme.Warn };
                item.SubItems.Add(p.ModName ?? p.Plugin ?? "—");
                item.SubItems.Add(p.Title + (p.Count > 1 ? "  ×" + p.Count : ""));
                item.SubItems.Add("");
                _loadList.Items.Add(item);
            }
        }
        _loadList.EndUpdate();

        _notStarting = rows.Count(r => r.Diagnosis.State == LoadState.DidntStart);
        var withErrors = rows.Count(r => r.Diagnosis.State == LoadState.StartedWithErrors);
        _loadHeader.Text = report is { BepInExRan: false } && rows.All(r => r.Diagnosis.State != LoadState.DidntStart)
            ? L.T("No game session with BepInEx yet: press PLAY, close the game, then check here.")
            : _notStarting > 0 ? L.F("{0} mod(s) didn't start. Select one to see why.", _notStarting)
            : withErrors > 0 ? L.F("Every mod started; {0} had errors.", withErrors)
            : L.T("✓  Every mod started at the last game start.");
        _loadHeader.ForeColor = _notStarting > 0 ? Theme.Bad : withErrors > 0 ? Theme.Warn : Theme.Good;
        SetBadge("Mods not loading", _notStarting + _clashCount > 0 ? (_notStarting + _clashCount).ToString() : null);
    }

    private static string? FixLabel(LoadRow r) => r.Diagnosis.Fix switch
    {
        LoadFix.EnableRequirement => L.F("Switch on {0}", r.Diagnosis.FixTarget ?? ""),
        LoadFix.FindRequirement => Edition.Nexus ? L.T("Find it on Nexus Mods") : L.T("Find it on the website"),
        LoadFix.DisableThis => L.T("Switch this mod off"),
        LoadFix.Unblock => L.T("Unblock"),
        LoadFix.Reinstall or LoadFix.UpdateThis => L.T("Use the Mods menu"),
        LoadFix.OpenChecker => L.T("Open Check BepInEx"),
        LoadFix.OpenProblems or LoadFix.OpenLog => L.T("Open BepInEx log"),
        LoadFix.Play => L.T("Play"),
        _ => null,
    };

    private void ShowLoadDetail()
    {
        var tag = _loadList.SelectedItems.Count > 0 ? _loadList.SelectedItems[0].Tag : null;
        _loadFix.Enabled = false;
        _loadFix.Text = L.T("Fix");
        switch (tag)
        {
            case LoadRow r:
            {
                var d = r.Diagnosis;
                var text = r.Name + "\r\n" + r.Rel.Replace('/', '\\') + "\r\n\r\n" + d.Short;
                if (d.Why.Length > 0) text += "\r\n\r\n" + L.T("Why: ") + d.Why;
                if (d.HowToFix.Length > 0) text += "\r\n" + L.T("How to fix: ") + d.HowToFix;
                _loadDetail.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (FixLabel(r) is { } label) { _loadFix.Text = label; _loadFix.Enabled = true; }
                break;
            }
            case ClashRow c:
                _loadDetail.Text = (c.Clash.Title + "\n\n" + c.Clash.Detail).Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (c.Clash.FixRel != null) { _loadFix.Text = L.T("Switch off the older copy"); _loadFix.Enabled = true; }
                break;
            case LogRow l:
                _loadDetail.Text = (l.Problem.Title + "\n\n" + l.Problem.Detail).Replace("\r\n", "\n").Replace("\n", "\r\n");
                break;
            default:
                _loadDetail.Text = "";
                break;
        }
    }

    private async Task FixSelectedLoadAsync()
    {
        var game = RequireGame();
        if (game == null || _loadList.SelectedItems.Count == 0) return;
        if (_loadList.SelectedItems[0].Tag is ClashRow { Clash.FixRel: { } fixRel } clash)
        {
            if (game.IsRunning() && !game.HasModManager) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var entry = ModEntries.Scan(game).FirstOrDefault(e => e.Rel == fixRel);
            if (entry == null) return;
            var ask = L.F("Switch off the older copy, {0}? Its files are kept, renamed so BepInEx skips them.", fixRel.Replace('/', '\\'));
            if (MessageBox.Show(this, ask, clash.Clash.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ModEntries.SwitchOff(entry.Dlls);
            Log(L.F("Switched off {0}.", fixRel));
            SetStatus(L.F("Switched off the older copy of {0}.", entry.Name), good: true);
            RefreshLoading();
            return;
        }
        if (_loadList.SelectedItems[0].Tag is not LoadRow r) return;
        var d = r.Diagnosis;
        bool NeedClosed()
        {
            if (!game.IsRunning() || game.HasModManager) return false;
            MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        switch (d.Fix)
        {
            case LoadFix.EnableRequirement:
            {
                if (NeedClosed()) return;
                var rel = d.FixTargetRel?.Replace(".dll.disabled", ".dll");
                var owner = _manifest.Mods.FirstOrDefault(m => m.Files.Any(f => rel != null && (f.Equals(rel, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(f.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))));
                if (owner != null) ModInstaller.SetDisabled(owner, false, game, _manifest);
                else if (rel != null)
                {
                    var parts = rel.Split('/');
                    ModInstaller.SetUntrackedDisabled(game, parts.Length > 3 ? string.Join("/", parts.Take(3)) : rel, false);
                }
                Log(L.F("Switched on {0}.", d.FixTarget ?? rel ?? ""));
                SetStatus(L.F("Switched on {0}. Play the game to check.", d.FixTarget ?? ""), good: true);
                break;
            }
            case LoadFix.FindRequirement:
                if (Edition.Nexus)
                {
                    Web.Open(Edition.NexusSearch(d.FixTarget ?? ""));
                    SetStatus(L.F("Searching Nexus Mods for {0}.", d.FixTarget ?? ""));
                    return;
                }
                Web.Open("https://nomaddrivemods.online/index.php?p=browse&q=" + Uri.EscapeDataString(d.FixTarget ?? ""));
                SetStatus(L.F("Searching the website for {0}. Install it from the Mods menu in the game.", d.FixTarget ?? ""));
                return;
            case LoadFix.DisableThis:
            {
                if (NeedClosed()) return;
                var ask = L.F("Switch off {0}? Its files are kept, renamed so BepInEx skips them. Switch it back on from the Mods menu.", r.Name);
                if (MessageBox.Show(this, ask, L.T("Switch this mod off"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                if (r.Tracked != null) ModInstaller.SetDisabled(r.Tracked, true, game, _manifest);
                else ModInstaller.SetUntrackedDisabled(game, r.Rel, true);
                Log(L.F("Switched off {0}.", r.Name));
                SetStatus(L.F("Switched off {0}.", r.Name), good: true);
                break;
            }
            case LoadFix.Unblock:
            {
                var n = 0;
                foreach (var f in d.Files) { try { File.Delete(f + ":Zone.Identifier"); n++; } catch { } }
                Log(L.F("Unblocked {0} file(s).", n));
                SetStatus(L.T("Unblocked. Play the game to check."), good: true);
                break;
            }
            case LoadFix.Reinstall:
            case LoadFix.UpdateThis:
                MessageBox.Show(this, L.F("Reinstall or update {0} from the Mods menu in the game (Installed, or Browse mods).", r.Name), L.T("Use the Mods menu"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            case LoadFix.OpenChecker: ShowPage("Check BepInEx"); return;
            case LoadFix.OpenProblems:
            case LoadFix.OpenLog:
                if (File.Exists(LogAnalyzer.BepInExLog(game))) Web.Open(LogAnalyzer.BepInExLog(game));
                return;
            case LoadFix.Play: await PlayAsync(); return;
        }
        RefreshLoading();
    }

    private void CopyLoadReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Check BepInEx {Updater.Current.ToString(3)} - mods at the last game start");
        foreach (ListViewItem item in _loadList.Items)
        {
            if (item.Tag is ListHeader h) { sb.AppendLine().AppendLine("[" + h.Text + "]"); continue; }
            sb.AppendLine($"{item.Text} {item.SubItems[1].Text}: {item.SubItems[2].Text}");
            if (item.Tag is LoadRow { Diagnosis.State: LoadState.DidntStart or LoadState.StartedWithErrors } r && r.Diagnosis.Why.Length > 0) sb.AppendLine("      " + r.Diagnosis.Why);
        }
        try { Clipboard.SetText(sb.ToString()); SetStatus(L.T("Copied. Paste it into your bug report."), good: true); }
        catch { SetStatus(L.T("Couldn't copy to the clipboard.")); }
    }
}
