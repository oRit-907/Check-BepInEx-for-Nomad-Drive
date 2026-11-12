using System.IO.Pipes;
using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>
/// Check BepInEx: finds Nomad Drive, checks BepInEx for everything that stops mods loading (and fixes it), explains why
/// a mod didn't start, and installs or updates the in-game Mods menu. Mods themselves are installed from the Mods menu
/// in the game. (This app used to be Nomad Mod Downloader; it updates in place, so it keeps the old exe and data names.)
/// </summary>
internal sealed partial class MainForm : Form
{
    public const string AppName = "Check BepInEx";

    private readonly AppSettings _settings = AppSettings.Load();
    private Manifest _manifest = Manifest.Load();
    private readonly HttpClient _http = CreateHttpClient();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly string? _startupLink;

    private GameInstall? _game;

    // status card
    private Label _gameLabel = null!, _bepLabel = null!, _menuLabel = null!;
    private Button _bepButton = null!;

    private Panel _pageHost = null!;
    private TextBox _log = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;

    // sidebar menu (like the game's main menu)
    private readonly Dictionary<string, Label> _nav = new();
    private readonly Dictionary<string, string> _badges = new();
    private Bitmap? _bgCache;
    private PictureBox _logo = null!;
    private DateTime _lastQuickFind;
    private Button _playButton = null!;
    private bool _watchingGame;

    private Button _appUpdateButton = null!;
    private UpdateInfo? _appUpdate;

    private static readonly string[] Pages = { "Check BepInEx", "Mods not loading", "Find the bad mod", "Backups", "Live log" };

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"CheckBepInEx/{Updater.Current.ToString(3)} (Nomad Drive)");
        return http;
    }

    public MainForm(string? startupLink)
    {
        _startupLink = startupLink;
        Text = L.T(AppName);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        MinimumSize = new Size(1040, 660);
        Size = new Size(1240, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        BuildUi();
        HandleCreated += (_, _) => Theme.DarkTitleBar(this);
        Shown += async (_, _) => await StartupAsync();
        FormClosed += (_, _) => _closing.Cancel();
        Resize += (_, _) => { _bgCache?.Dispose(); _bgCache = null; };
    }

    /// <summary>
    /// The background: night asphalt with a street-light glow and a dashed lane line along the bottom (drawn, so no
    /// pictures from the game are used).
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        if (_bgCache == null || _bgCache.Size != ClientSize)
        {
            _bgCache?.Dispose();
            _bgCache = new Bitmap(ClientSize.Width, ClientSize.Height);
            using var g = Graphics.FromImage(_bgCache);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(Point.Empty, ClientSize);
            using (var road = new System.Drawing.Drawing2D.LinearGradientBrush(rect, Color.FromArgb(0x1C, 0x1F, 0x23), Color.FromArgb(0x11, 0x13, 0x15), 90f))
                g.FillRectangle(road, rect);
            void Glow(float cx, float cy, float rx, float ry, Color c)
            {
                using var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(cx - rx, cy - ry, rx * 2, ry * 2);
                using var b = new System.Drawing.Drawing2D.PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } };
                g.FillPath(b, path);
            }
            Glow(rect.Width * 0.78f, -rect.Height * 0.08f, rect.Width * 0.6f, rect.Height * 0.55f, Color.FromArgb(20, Theme.Accent));
            Glow(rect.Width * 0.1f, rect.Height * 1.1f, rect.Width * 0.5f, rect.Height * 0.6f, Color.FromArgb(26, Theme.Sign));
            using var lane = new SolidBrush(Color.FromArgb(70, Theme.Accent));
            for (var x = 0; x < rect.Width; x += 128) g.FillRectangle(lane, x, rect.Height - 19, 72, 5);
        }
        e.Graphics.DrawImageUnscaled(_bgCache, 0, 0);
    }

    // =====================================================================
    // Layout
    // =====================================================================

    private void BuildUi()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = Color.Transparent, Padding = new Padding(28, 18, 16, 16) };

        // The app's own logo: a highway sign (not the game's).
        var logo = _logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Dock = DockStyle.Top, Height = 118, Image = Theme.Art("logo.png") };
        logo.Paint += (_, e) =>
        {
            if (logo.Image != null) return;
            using var f = new Font(Theme.Bold.FontFamily, 30f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(e.Graphics, "NOMAD DRIVE", f, logo.ClientRectangle, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
        var subtitle = new Label { Text = L.T("CHECK BEPINEX"), Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Muted, Font = Theme.Bold, BackColor = Color.Transparent, TextAlign = ContentAlignment.TopCenter };

        var menu = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 12, 0, 0)
        };
        foreach (var name in Pages)
        {
            var item = new Label { Text = L.T(name), AutoSize = true, Font = Theme.Nav, ForeColor = Theme.Text, BackColor = Color.Transparent, Cursor = Cursors.Hand, Margin = new Padding(0, 1, 0, 3), Tag = name };
            item.Click += (_, _) => ShowPage(name);
            item.MouseEnter += (_, _) => { if (_current != name) item.ForeColor = Theme.Accent; };
            item.MouseLeave += (_, _) => { if (_current != name) item.ForeColor = Theme.Text; };
            menu.Controls.Add(item);
            _nav[name] = item;
        }

        // PLAY
        var play = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Padding = new Padding(0, 14, 0, 0) };
        _playButton = Theme.Button("▶  " + L.T("PLAY"), true);
        _playButton.Font = Theme.H2;
        _playButton.Width = 204;
        _playButton.Height = Theme.H2.Height + 22;
        _playButton.Margin = new Padding(0);
        _playButton.Click += async (_, _) => await PlayAsync();
        play.Controls.Add(_playButton);

        // status card: game, BepInEx, Mods menu + buttons
        var statusCard = new GlassPanel { Dock = DockStyle.Bottom, Padding = new Padding(16, 14, 16, 14), Height = 10 };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _gameLabel = Theme.Label(L.T("Looking for Nomad Drive…"), Theme.Small, Theme.Muted);
        _bepLabel = Theme.Label("—", Theme.Small, Theme.Muted);
        _menuLabel = Theme.Label("—", Theme.Small, Theme.Muted);
        Label Key(string t) => new() { Text = t, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Color.Transparent, Margin = new Padding(0, 3, 12, 3) };
        Label Val(Label l) { l.AutoSize = true; l.MaximumSize = new Size(190, 0); l.Margin = new Padding(0, 3, 0, 3); return l; }
        grid.Controls.Add(Key(L.T("Game")), 0, 0); grid.Controls.Add(Val(_gameLabel), 1, 0);
        grid.Controls.Add(Key("BepInEx"), 0, 1); grid.Controls.Add(Val(_bepLabel), 1, 1);
        grid.Controls.Add(Key(L.T("Mods menu")), 0, 2); grid.Controls.Add(Val(_menuLabel), 1, 2);

        var gameBtn = Theme.Button(L.T("Change folder…"));
        gameBtn.Click += (_, _) => PickGameFolder();
        _bepButton = Theme.Button(L.T("Install BepInEx"), true);
        _bepButton.Visible = false;
        _bepButton.Click += async (_, _) => await RunExclusive(() => EnsureBepInExAsync());
        _appUpdateButton = Theme.Button(L.T("Update app"), true);
        _appUpdateButton.Visible = false;
        _appUpdateButton.Click += async (_, _) => await RunExclusive(InstallAppUpdateAsync);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0), WrapContents = true };
        foreach (var b in new[] { _appUpdateButton, _bepButton, gameBtn }) { b.Margin = new Padding(0, 0, 8, 8); buttons.Controls.Add(b); }
        statusCard.Controls.Add(buttons);
        statusCard.Controls.Add(grid);
        void FitCard() => statusCard.Height = statusCard.Padding.Vertical + grid.Height + buttons.Height;
        grid.SizeChanged += (_, _) => FitCard();
        buttons.SizeChanged += (_, _) => FitCard();

        // language, under the card
        var langRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        var langPick = Theme.Combo(170);
        foreach (var lang in L.Languages) langPick.Items.Add(lang.Name);
        langPick.SelectedIndex = Math.Max(0, L.Languages.ToList().FindIndex(x => x.Code == L.Current.Code));
        langPick.SelectedIndexChanged += (_, _) => ChangeLanguage(L.Languages[langPick.SelectedIndex]);
        langRow.Controls.Add(new Label { Text = "🌐 " + L.T("Language"), AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Color.Transparent, Margin = new Padding(0, 8, 8, 0) });
        langRow.Controls.Add(langPick);

        side.Controls.Add(menu);
        side.Controls.Add(play);
        side.Controls.Add(subtitle);
        side.Controls.Add(logo);
        side.Controls.Add(statusCard);
        side.Controls.Add(langRow);

        // Short windows: smaller menu text, then a smaller logo, then no logo, so the menu never runs into the card.
        var fitting = false;
        void FitSidebar()
        {
            if (fitting || side.Height <= 0) return;
            fitting = true;
            try
            {
                int Need() => (logo.Visible ? logo.Height : 0) + (subtitle.Visible ? subtitle.Height : 0) + play.GetPreferredSize(new Size(side.Width, 0)).Height + menu.GetPreferredSize(new Size(side.Width, 0)).Height
                              + statusCard.Height + langRow.GetPreferredSize(new Size(side.Width, 0)).Height + side.Padding.Vertical + 6;
                var steps = new (Font Nav, int Logo, bool Subtitle)[] { (Theme.Nav, 118, true), (Theme.H2, 118, true), (Theme.H2, 80, true), (Theme.Bold, 64, true), (Theme.Bold, 0, false) };
                foreach (var (nav, logoH, sub) in steps)
                {
                    foreach (var item in _nav.Values) item.Font = nav;
                    logo.Visible = logoH > 0;
                    if (logoH > 0) logo.Height = logoH;
                    subtitle.Visible = sub;
                    if (Need() <= side.Height) break;
                }
            }
            finally { fitting = false; }
        }
        side.SizeChanged += (_, _) => FitSidebar();
        statusCard.SizeChanged += (_, _) => FitSidebar();

        // ================= right: pages + status line =================
        var main = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(4, 22, 22, 8) };
        _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var statusLine = new Panel { Dock = DockStyle.Bottom, Height = Theme.Body.Height + 14, BackColor = Color.Transparent, Padding = new Padding(4, 6, 0, 0) };
        _status = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Text = L.T("Ready"), AutoEllipsis = true, BackColor = Color.Transparent, Font = Theme.Small };
        _progress = new ProgressBar { Dock = DockStyle.Right, Width = 220, Visible = false, Style = ProgressBarStyle.Continuous };
        statusLine.Controls.Add(_status);
        statusLine.Controls.Add(_progress);

        AddPage("Check BepInEx", BuildCheckerPage());
        AddPage("Mods not loading", BuildLoadingPage());
        AddPage("Find the bad mod", BuildBadModPage());
        AddPage("Backups", BuildBackupsPage());
        AddPage("Live log", BuildLiveLogPage());

        main.Controls.Add(_pageHost);
        main.Controls.Add(BuildGameUpdateBanner());
        main.Controls.Add(BuildUpdateBanner());
        main.Controls.Add(statusLine);
        Controls.Add(main);
        Controls.Add(side);
        ShowPage("Check BepInEx");
    }

    private readonly Dictionary<string, Control> _pageControls = new();
    private string _current = "";

    private static string PageSubtitle(string name) => name switch
    {
        "Check BepInEx" => L.T("Everything that stops Nomad Drive mods from loading, with one-click fixes."),
        "Mods not loading" => L.T("Which mods started at the last game start, and why the others didn't."),
        "Find the bad mod" => L.T("Switches half your mods off at a time to find the one causing a crash or problem."),
        "Backups" => L.T("Everything the app put aside before changing files, ready to restore. Uninstall BepInEx is here too."),
        "Live log" => L.T("The game's BepInEx log as it's written, and what this app did."),
        _ => "",
    };

    private void AddPage(string name, Control page)
    {
        // One big panel per page with the heading inside, like the in-game menu.
        var frame = new GlassPanel { Padding = new Padding(26, 20, 26, 18) };
        var head = new Panel { Dock = DockStyle.Top, BackColor = Color.Transparent };
        var big = new Font(Theme.H2.FontFamily, Theme.H2.Size * 1.5f, Theme.H2.Style);
        var title = new Label { Text = L.T(name), Font = big, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = true, Location = new Point(0, 0) };
        var sub = new Label { Text = PageSubtitle(name), Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, AutoSize = true };
        title.SizeChanged += (_, _) => sub.Location = new Point(2, title.Bottom);
        head.Controls.Add(title);
        head.Controls.Add(sub);
        head.Height = big.Height + Theme.Small.Height + 16;
        if (page is GlassPanel gp) { gp.Fill = Color.Transparent; gp.Padding = new Padding(0); }
        page.Dock = DockStyle.Fill;
        frame.Controls.Add(page);
        frame.Controls.Add(head);
        frame.Dock = DockStyle.Fill;
        frame.Visible = false;
        _pageHost.Controls.Add(frame);
        _pageControls[name] = frame;
    }

    private void ShowPage(string name)
    {
        _current = name;
        foreach (var (key, page) in _pageControls) page.Visible = key == name;
        foreach (var (key, item) in _nav) { item.ForeColor = key == name ? Theme.Accent : Theme.Text; item.Text = NavText(key); }
        if (name == "Check BepInEx") _ = RefreshChecksAsync(lookOnline: false);
        if (name == "Mods not loading") RefreshLoading();
        if (name == "Find the bad mod") RefreshBadMod();
        if (name == "Backups") RefreshBackups();
        if (name == "Live log") PollLiveLog();
    }

    private void SetBadge(string page, string? text)
    {
        if (string.IsNullOrEmpty(text)) _badges.Remove(page); else _badges[page] = text;
        if (_nav.TryGetValue(page, out var item)) item.Text = NavText(page);
    }

    private string NavText(string page) => L.T(page) + (_badges.TryGetValue(page, out var b) ? "  · " + b : "");

    // =====================================================================
    // Startup
    // =====================================================================

    private async Task StartupAsync()
    {
        ReleaseOldLinks();
        _ = LookForAppUpdateAsync(); // only shows an Update button, never installs by itself
        _updateTimer.Tick += async (_, _) => await LookForAppUpdateAsync();
        _updateTimer.Start();
        FormClosed += (_, _) => _updateTimer.Stop();
        StartLinkListener();
        if (GameLauncher.RestoreIfPending()) Log(L.T("Turned mods back on after the last \"Play without mods\"."));

        _game = GameInstall.TryOpen(_settings.GamePath) ?? await Task.Run(() => GameLocator.Find());
        if (_game != null) UseGame(_game, announce: _settings.GamePath != _game.Root);
        else { RefreshGame(); _ = DeepSearchAsync(); }
        Activated += (_, _) => QuickFindOnFocus();

        if (_settings.AutoUpdateBepInEx) await RunExclusive(AutoUpdateBepInExAsync);
        if (_game != null) CheckGameUpdate(_game);
        _gameWatch.Tick += (_, _) => { if (_game != null) CheckGameUpdate(_game); };
        _gameWatch.Start();
        FormClosed += (_, _) => _gameWatch.Stop();
        if (BadModFinder.Load() is { Result: null } open)
        {
            Log(L.F("Find the bad mod is still running (round {0}): some of your mods are switched off.", open.Round));
            SetStatus(L.T("Find the bad mod is still running: some mods are switched off. Finish it or press Stop to switch them back on."));
            ShowPage("Find the bad mod");
        }
        if (_startupLink != null) HandleIncoming(_startupLink);
        if (_game != null) _ = RefreshChecksAsync(lookOnline: true);
    }

    /// <summary>
    /// Nomad Mod Downloader handled nxm:// (Nexus "Mod manager download") and nomadmods:// links. This app doesn't
    /// install mods, so those links go back to whatever had them before (Vortex, MO2…), or are removed.
    /// </summary>
    private void ReleaseOldLinks()
    {
        if (StoreApp.IsPackaged || Edition.Nexus) return;   // the Nexus edition never touches link handlers in the registry
        try
        {
            var cmd = ProtocolHandler.CurrentCommand();
            if (cmd != null && (ProtocolHandler.IsOurs() || cmd.Contains("NomadModDownloader", StringComparison.OrdinalIgnoreCase)))
            {
                ProtocolHandler.Release(_settings);
                Log(L.T("Nexus \"Mod manager download\" links are no longer opened by this app (mods install from the Mods menu in the game)."));
            }
            ProtocolHandler.RemoveInvites();
        }
        catch { }
    }

    private void UseGame(GameInstall g, bool announce)
    {
        _game = g;
        if (_settings.GamePath != g.Root) { _settings.GamePath = g.Root; _settings.Save(); }
        RefreshGame();
        if (announce) Log(L.T("Found Nomad Drive at ") + g.Root);
        EnsureCompatLayer(g);
        if (_current == "Mods not loading") RefreshLoading();
    }

    private async Task DeepSearchAsync()
    {
        _gameLabel.Text = L.T("Searching for Nomad Drive…");
        _gameLabel.ForeColor = Theme.Muted;
        var g = await Task.Run(() => GameLocator.DeepSearch(_closing.Token));
        if (IsDisposed || _game != null) return;
        if (g != null) { UseGame(g, announce: true); _ = RefreshChecksAsync(lookOnline: true); return; }
        RefreshGame();
        Log(L.T("Couldn't find Nomad Drive automatically. Use \"Change folder…\" to point at the game folder."));
    }

    private async void QuickFindOnFocus()
    {
        if (_game != null && Directory.Exists(_game.Root)) return;
        if (DateTime.UtcNow - _lastQuickFind < TimeSpan.FromSeconds(10)) return;
        _lastQuickFind = DateTime.UtcNow;
        var g = await Task.Run(() => GameLocator.Find());
        if (g != null && !IsDisposed && (_game == null || !Directory.Exists(_game.Root))) UseGame(g, announce: true);
    }

    /// <summary>
    /// BepInEx 5 gets BepInEx 6 support (so mods built for BepInEx 6 load), the Mod Files patcher and the BepInEx 5 build
    /// of Unity Compatibility: installed when missing, updated when this app has a newer copy. Not while the game runs.
    /// </summary>
    private void EnsureCompatLayer(GameInstall g)
    {
        try
        {
            if (BepInExManager.Check(g).Major != 5 || g.IsRunning()) return;
            if (UnityCompat.PatcherInstalled(g) && !UnityCompat.PatcherMatches(g))
            {
                UnityCompat.InstallPatcher(g);
                UnityCompat.EnsureSearchPath(g);
                Log(L.T("Switched Unity Compatibility to the BepInEx 5 build."));
            }
            if (!ModChanges.IsReady(g))
            {
                var w = ModChanges.Install(g);
                Log(L.F("Set up the Mod Files patcher ({0}): changes that wait for a restart are applied when the game starts.", w[0]));
            }
            BepInExManager.RemoveBepInEx6Leftovers(g);
            if (!BepInEx6Support.IsInstalled(g) || BepInEx6Support.IsOutdated(g))
            {
                var w = BepInEx6Support.Install(g);
                Log(L.F("Installed BepInEx 6 support, so BepInEx 6 mods run on BepInEx 5 ({0} files).", w.Count));
            }
        }
        catch (Exception e) { Log(L.F("Couldn't set up BepInEx 6 support: {0}", e.Message)); }
    }

    private void RefreshGame()
    {
        if (_game == null)
        {
            _gameLabel.Text = L.T("Nomad Drive not found");
            _gameLabel.ForeColor = Theme.Bad;
            _bepLabel.Text = "—";
            _menuLabel.Text = "—";
            _bepButton.Visible = false;
            return;
        }
        _gameLabel.Text = _game.LooksLikeNomadDrive ? _game.Root : _game.Root + "   " + L.T("(this doesn't look like Nomad Drive)");
        if (_game.LooksLikeNomadDrive) _game.RegisterWithModManager();
        _gameLabel.ForeColor = _game.LooksLikeNomadDrive ? Theme.Text : Theme.Warn;

        var s = BepInExManager.Check(_game);
        _bepLabel.Text = (s.Version != null ? "v" + s.Version + " — " : "") + s.Message;
        _bepLabel.ForeColor = s.State switch { BepInExState.Ready => Theme.Good, BepInExState.NeedsFirstRun or BepInExState.Disabled => Theme.Warn, _ => Theme.Bad };
        _bepButton.Visible = s.State is BepInExState.NotInstalled or BepInExState.Broken or BepInExState.Disabled;
        _bepButton.Text = s.State switch { BepInExState.Broken => L.T("Repair BepInEx"), BepInExState.Disabled => L.T("Turn BepInEx on"), _ => L.T("Install BepInEx") };

        var menuVersion = ModsMenuFiles.InstalledVersion(_game);
        _menuLabel.Text = menuVersion != null ? "v" + menuVersion : (Edition.Nexus ? L.T("Not installed (get it on Nexus Mods)") : L.T("Not installed (get it on nomaddrivemods.online)"));
        _menuLabel.ForeColor = menuVersion != null ? Theme.Good : Theme.Warn;
    }

    private void ChangeLanguage(L.Language lang)
    {
        if (lang.Code == L.Current.Code) return;
        _settings.Language = lang.Code;
        _settings.Save();
        L.Init(lang.Code);
        if (MessageBox.Show(this, L.T("Restart the app now to switch language?"), L.T("Language"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        { Application.Restart(); Environment.Exit(0); }
        else SetStatus(L.T("The new language is used next time the app starts."));
    }

    // =====================================================================
    // Play
    // =====================================================================

    private async Task PlayAsync()
    {
        var game = RequireGame();
        if (game == null) return;
        if (game.IsRunning()) { SetStatus(L.T("Nomad Drive is already running.")); return; }
        try
        {
            if (BepInExManager.Check(game).State is BepInExState.NotInstalled or BepInExState.Broken or BepInExState.Disabled)
                if (!await EnsureBepInExAsync()) return;
            GameLauncher.Launch(game);
            SetStatus(L.T("Starting Nomad Drive…"));
            Log(L.T("Started Nomad Drive."));
            _ = WatchSessionAsync(game);
        }
        catch (Exception ex) { ShowError(L.T("Couldn't start the game"), ex); }
    }

    /// <summary>Waits for the game to start and close again, then checks what loaded.</summary>
    private async Task WatchSessionAsync(GameInstall game)
    {
        if (_watchingGame) return;
        _watchingGame = true;
        try
        {
            var until = DateTime.UtcNow.AddMinutes(3);
            while (!game.IsRunning() && DateTime.UtcNow < until) await Task.Delay(1500, _closing.Token);
            if (!game.IsRunning()) return;
            _playButton.Enabled = false;
            _playButton.Text = L.T("Game running");
            while (game.IsRunning()) await Task.Delay(3000, _closing.Token);
            if (IsDisposed) return;
            RefreshLoading();
            var bad = await RefreshChecksAsync(lookOnline: false);
            SetStatus(bad == 0 && _notStarting == 0 ? L.T("The game closed. BepInEx and your mods loaded fine.")
                      : L.F("The game closed. {0} BepInEx problem(s), {1} mod(s) didn't start.", bad, _notStarting), good: bad == 0 && _notStarting == 0);
        }
        catch (OperationCanceledException) { }
        finally { _watchingGame = false; if (!IsDisposed) { _playButton.Enabled = true; _playButton.Text = "▶  " + L.T("PLAY"); } }
    }

    // =====================================================================
    // BepInEx
    // =====================================================================

    private async Task<bool> EnsureBepInExAsync()
    {
        var game = RequireGame();
        if (game == null) return false;
        var s = BepInExManager.Check(game);
        switch (s.State)
        {
            case BepInExState.Ready:
            case BepInExState.NeedsFirstRun:
                return true;
            case BepInExState.Il2CppGame:
                MessageBox.Show(this, s.Message, "BepInEx", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            case BepInExState.Disabled:
            {
                if (MessageBox.Show(this, L.T("BepInEx is installed but switched off (enabled = false in doorstop_config.ini), so mods won't load.\n\nTurn it back on?"),
                        L.T("BepInEx is off"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
                var ini = Path.Combine(game.Root, "doorstop_config.ini");
                File.WriteAllText(ini, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(ini), @"(?im)^(\s*enabled\s*=\s*)false", "${1}true"));
                Log(L.T("Turned BepInEx back on in doorstop_config.ini."));
                RefreshGame();
                return true;
            }
        }

        if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
        var broken = s.State == BepInExState.Broken;
        var why = broken ? L.F("BepInEx looks broken: {0}", s.Message) : L.T("Nomad Drive mods need BepInEx, and it isn't installed yet.");
        var ask = why + "\n\n" + (broken ? L.T("Reinstall BepInEx 5? The old files go to a backup; your plugins and settings are kept.")
                                         : L.F("Install BepInEx 5 into {0}? Nothing outside the game folder is touched. Afterwards, start the game once so BepInEx finishes setting up.", game.Root));
        if (MessageBox.Show(this, ask, L.T("Install BepInEx"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { Log(L.T("BepInEx install declined.")); return false; }

        var plan = await BepInExManager.Plan5Async(_http, game, _closing.Token);
        SetStatus(L.F("Downloading BepInEx {0}…", plan.Shown));
        var written = broken ? (await BepInExManager.RepairAsync(_http, game, plan, Progress(), _closing.Token)).Written
                             : await BepInExManager.InstallAsync(_http, game, plan, Progress(), _closing.Token);
        EnsureCompatLayer(game);
        HideProgress();
        Log(L.F("Installed BepInEx {0} ({1} files) into {2}", plan.Shown, written.Count, game.Root));
        RefreshGame();
        SetStatus(L.F("BepInEx {0} installed.", plan.Shown), good: true);
        _ = RefreshChecksAsync(lookOnline: false);
        return true;
    }

    private async Task AutoUpdateBepInExAsync()
    {
        if (_game == null) return;
        var plan = await BepInExManager.CheckForUpdateAsync(_http, _game, _closing.Token);
        if (plan == null) return;
        var have = BepInExManager.Check(_game).Version;
        if (_game.IsRunning()) { Log(L.F("BepInEx {0} is available (you have {1}). It updates next time the app starts with the game closed.", plan.Shown, have ?? "?")); return; }
        SetStatus(L.F("Updating BepInEx to {0}…", plan.Shown));
        var written = await BepInExManager.UpdateAsync(_http, _game, plan, Progress(), _closing.Token);
        HideProgress();
        Log(L.F("Updated BepInEx {0} → {1} ({2} files). Your plugins, settings and doorstop_config.ini were kept.", have ?? "?", plan.Shown, written.Count));
        RefreshGame();
        SetStatus(L.F("BepInEx updated to {0}.", plan.Shown), good: true);
    }

    // =====================================================================
    // App updates (from nomaddrivemods.online, 3.2.0)
    // =====================================================================

    private async Task LookForAppUpdateAsync()
    {
        if (Edition.Nexus) return;   // the Nexus edition doesn't update itself: new versions come from its Nexus page
        if (_lookingForUpdate) return;
        _lookingForUpdate = true;
        try
        {
            var (result, info, error) = await new Updater(_http) { Token = _settings.GetGitHubToken() }.CheckAsync(_closing.Token);
            if (IsDisposed) return;
            if (result == UpdateCheck.Failed) { if (!_updateFailLogged) Log(L.F("Update check failed: {0}", error ?? "")); _updateFailLogged = true; return; }
            _updateFailLogged = false;
            if (result == UpdateCheck.Available && info != null) ShowAppUpdate(info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_updateFailLogged) Log(L.F("Update check failed: {0}", ex.Message)); _updateFailLogged = true; }
        finally { _lookingForUpdate = false; }
    }

    /// <summary>
    /// The Microsoft Store version was taken off the Store, so it never updates again: the Update button opens the
    /// download page for the EXE instead (it can't replace a Store-installed copy itself).
    /// </summary>
    private void ShowStoreRetired()
    {
        _appUpdateButton.Text = "⬆ " + L.T("Get the new version");
        _appUpdateButton.Visible = true;
        Log(L.T("This copy came from the Microsoft Store, which doesn't have Check BepInEx any more, so it won't update. Download the EXE from nomaddrivemods.online (How to install › Get BepInEx), then uninstall this copy."));
        SetStatus(L.T("This Microsoft Store copy doesn't get updates any more. Press \"Get the new version\" for the EXE."));
    }

    private void ShowAppUpdate(UpdateInfo info)
    {
        if (StoreApp.IsPackaged) { ShowStoreRetired(); return; }
        var same = _appUpdate?.Version == info.Version;
        _appUpdate = info;
        _appUpdateButton.Text = "⬆ " + L.F("Update to v{0}", info.Version.ToString(3));
        _appUpdateButton.Visible = true;
        ShowUpdateBanner(info);
        if (!same) Log(L.F("Version {0} is available (you have {1}). Click \"Update to v{0}\" to install it.", info.Version.ToString(3), Updater.Current.ToString(3)));
    }

    private async Task InstallAppUpdateAsync()
    {
        if (StoreApp.IsPackaged) { Web.Open("https://nomaddrivemods.online/index.php?p=bepinex"); return; }
        if (_appUpdate is not { } info) return;
        Log(L.F("Updating the app to version {0}…", info.Version.ToString(3)));
        SetStatus(L.F("Downloading version {0}…", info.Version.ToString(3)));
        string path;
        try { path = await new Updater(_http) { Token = _settings.GetGitHubToken() }.DownloadAsync(info, Progress(), _closing.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            HideProgress();
            SetStatus(L.F("Update failed: {0}", ex.Message));
            Log(L.F("Update failed: {0}", ex.Message));
            return;
        }
        HideProgress();
        Log(L.F("Downloaded version {0}. Restarting…", info.Version.ToString(3)));
        Updater.ApplyAndRestart(path, info.IsInstaller);
        Close();
    }

    private async Task CheckForAppUpdateAsync(bool silent)
    {
        if (StoreApp.IsPackaged) { ShowStoreRetired(); return; }
        if (!silent) SetStatus(L.T("Checking for app updates…"));
        var (result, info, error) = await new Updater(_http) { Token = _settings.GetGitHubToken() }.CheckAsync(_closing.Token);
        if (result == UpdateCheck.Failed) { Log(L.F("Update check failed: {0}", error ?? "")); if (!silent) SetStatus(L.F("Update check failed: {0}", error ?? "")); return; }
        if (result == UpdateCheck.UpToDate || info == null) { if (!silent) SetStatus(L.F("You have the latest version ({0}).", Updater.Current.ToString(3)), good: true); return; }
        ShowAppUpdate(info);
        if (silent) return;
        var notes = string.IsNullOrWhiteSpace(info.Notes) ? "" : "\n\n" + (info.Notes.Length > 600 ? info.Notes[..600] + "…" : info.Notes);
        var ask = L.F("Version {0} is available (you have {1}).", info.Version.ToString(3), Updater.Current.ToString(3)) + notes + "\n\n" + L.T("Download and install it now? The app restarts when it's done.");
        if (MessageBox.Show(this, ask, L.T("App updates"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) { SetStatus(L.T("Ready")); return; }
        await InstallAppUpdateAsync();
    }

    // =====================================================================
    // Game folder, links from the Mods menu
    // =====================================================================

    private void PickGameFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = L.T("Select the Nomad Drive game folder (the one with the game's .exe)"), UseDescriptionForTitle = true, InitialDirectory = _game?.Root ?? "" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var g = GameLocator.OpenNear(dlg.SelectedPath);
        if (g == null) { MessageBox.Show(this, L.T("That folder doesn't contain a Unity game (.exe with a matching _Data folder)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        UseGame(g, announce: false);
        Log(L.T("Game folder set to ") + g.Root);
        _ = RefreshChecksAsync(lookOnline: true);
    }

    /// <summary>A second start of the app (from the Mods menu's Check BepInEx button, or a double-click) shows this window.</summary>
    private void StartLinkListener()
    {
        _ = Task.Run(async () =>
        {
            while (!_closing.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_closing.Token);
                    using var r = new StreamReader(server);
                    var line = await r.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line)) BeginInvoke(() => HandleIncoming(line));
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500); }
            }
        });
    }

    private void HandleIncoming(string line)
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        if (line.StartsWith(Program.CheckLink, StringComparison.OrdinalIgnoreCase)) ShowPage("Check BepInEx");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private GameInstall? RequireGame()
    {
        if (_game != null && Directory.Exists(_game.Root)) return _game;
        MessageBox.Show(this, L.T("Point the app at your Nomad Drive folder first (\"Change folder…\")."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        PickGameFolder();
        return _game;
    }

    private async Task RunExclusive(Func<Task> work)
    {
        if (!await _busy.WaitAsync(0)) { SetStatus(L.T("Busy — wait for the current job to finish.")); return; }
        UseWaitCursor = true;
        try { await work(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { HideProgress(); ShowError(L.T("Something went wrong"), ex); }
        finally { UseWaitCursor = false; _busy.Release(); }
    }

    private IProgress<double> Progress()
    {
        _progress.Value = 0;
        _progress.Visible = true;
        return new Progress<double>(p => _progress.Value = Math.Clamp((int)(p * 100), 0, 100));
    }

    private void HideProgress() => _progress.Visible = false;

    private void SetStatus(string text, bool good = false)
    {
        _status.Text = string.IsNullOrEmpty(text) ? L.T("Ready") : text;
        _status.ForeColor = good ? Theme.Good : Theme.Muted;
    }

    private void Log(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        try { File.AppendAllText(AppPaths.LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}"); } catch { }
    }

    /// <summary>Program.SmokeTest: a picture of every page.</summary>
    internal void SaveSmokePictures(string folder)
    {
        foreach (var page in Pages)
        {
            ShowPage(page);
            Application.DoEvents();
            using var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(bmp, new Rectangle(Point.Empty, ClientSize));
            bmp.Save(Path.Combine(folder, page.Replace(' ', '-') + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllText(Path.Combine(folder, "activity.txt"), _log.Text);
    }

    private void ShowError(string title, Exception ex)
    {
        Log($"{title}: {ex.Message}");
        SetStatus(ex.Message);
        MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
