using System.Text;
using System.Text.RegularExpressions;
using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>
/// "Live log": BepInEx\LogOutput.log as the game writes it, coloured by level, filtered by level and by text (a mod's
/// name), with Pause. The app's own activity is the second view on the same page.
/// </summary>
internal sealed partial class MainForm
{
    private enum LogLevel { Debug, Info, Message, Warning, Error, Fatal }
    private sealed record LogLine(LogLevel Level, string Text);

    private RichTextBox _live = null!;
    private Label _liveState = null!;
    private ComboBox _liveLevel = null!;
    private TextBox _liveSearch = null!;
    private Button _livePause = null!, _liveGameBtn = null!, _liveAppBtn = null!;
    private readonly List<LogLine> _liveLines = new();
    private readonly System.Windows.Forms.Timer _liveTimer = new() { Interval = 800 };
    private long _livePos;
    private string _livePartial = "";
    private string? _liveFile;
    private bool _livePaused, _showingApp;
    private LogLevel _liveLast = LogLevel.Info;
    private const int LiveMax = 20000;
    private static readonly Regex LevelRx = new(@"^\[(Debug|Info|Message|Warning|Error|Fatal)\s*:", RegexOptions.Compiled);

    private Control BuildLiveLogPage()
    {
        var page = new GlassPanel { Padding = new Padding(16, 16, 16, 12) };

        // Which log: the game's (BepInEx) or this app's own activity.
        var views = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        _liveGameBtn = Theme.Button(L.T("Game log"), true);
        _liveAppBtn = Theme.Button(L.T("App activity"));
        _liveGameBtn.Click += (_, _) => ShowLiveView(app: false);
        _liveAppBtn.Click += (_, _) => ShowLiveView(app: true);
        var export = Theme.Button("📦 " + L.T("Export logs for mod devs"));
        export.Click += (_, _) => ExportLogs();
        var updates = Theme.Button(L.T("Check for app updates"));
        updates.Click += async (_, _) => await RunExclusive(() => CheckForAppUpdateAsync(silent: false));
        foreach (var b in new[] { _liveGameBtn, _liveAppBtn, export, updates }) { b.Margin = new Padding(0, 0, 8, 6); views.Controls.Add(b); }
        views.Controls.Add(new Label { Text = L.F("Version {0}", Updater.Current.ToString(3)), AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, BackColor = Color.Transparent, Margin = new Padding(4, 10, 0, 0) });

        // Filters for the game log
        var tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        _liveLevel = Theme.Combo(210);
        _liveLevel.Items.AddRange(new object[] { L.T("Everything"), L.T("Warnings and errors"), L.T("Errors only") });
        _liveLevel.SelectedIndex = 0;
        _liveLevel.SelectedIndexChanged += (_, _) => RedrawLive();
        _liveSearch = new TextBox { Width = 230, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, PlaceholderText = L.T("Filter: a mod's name or any text") };
        _liveSearch.TextChanged += (_, _) => { _liveFilterDelay.Stop(); _liveFilterDelay.Start(); };
        _liveFilterDelay.Tick += (_, _) => { _liveFilterDelay.Stop(); RedrawLive(); };
        _livePause = Theme.Button(L.T("Pause"));
        _livePause.Click += (_, _) => { _livePaused = !_livePaused; _livePause.Text = _livePaused ? L.T("Resume") : L.T("Pause"); if (!_livePaused) RedrawLive(); };
        var clear = Theme.Button(L.T("Clear"));
        clear.Click += (_, _) => { _liveLines.Clear(); RedrawLive(); };
        var open = Theme.Button(L.T("Open the log file"));
        open.Click += (_, _) => { if (_liveFile != null && File.Exists(_liveFile)) Web.Open(_liveFile); else SetStatus(L.T("There's no BepInEx log yet. Play the game once first.")); };
        var copy = Theme.Button(L.T("Copy"));
        copy.Click += (_, _) =>
        {
            var text = _showingApp ? _log.Text : _live.Text;
            try { if (text.Length > 0) { Clipboard.SetText(text); SetStatus(L.T("Copied."), good: true); } } catch { SetStatus(L.T("Couldn't copy to the clipboard.")); }
        };
        foreach (Control c in new Control[] { _liveLevel, _liveSearch, _livePause, clear, open, copy }) { c.Margin = new Padding(0, 2, 8, 6); tools.Controls.Add(c); }

        _liveState = new Label { Dock = DockStyle.Top, Height = Theme.Small.Height + 12, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };

        _live = new RichTextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
            Font = Theme.Mono, WordWrap = false, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Both, HideSelection = false,
        };
        _live.HandleCreated += (_, _) => Theme.DarkScrollbars(_live);
        _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono, WordWrap = true, Dock = DockStyle.Fill, Visible = false };
        _log.HandleCreated += (_, _) => Theme.DarkScrollbars(_log);

        page.Controls.Add(_live);
        page.Controls.Add(_log);
        page.Controls.Add(_liveState);
        page.Controls.Add(tools);
        page.Controls.Add(views);
        _liveTools = tools;

        _liveTimer.Tick += (_, _) => PollLiveLog();
        _liveTimer.Start();
        FormClosed += (_, _) => _liveTimer.Stop();
        return page;
    }

    private FlowLayoutPanel _liveTools = null!;
    private readonly System.Windows.Forms.Timer _liveFilterDelay = new() { Interval = 300 };

    private void ShowLiveView(bool app)
    {
        _showingApp = app;
        _live.Visible = !app; _liveTools.Visible = !app; _liveState.Visible = !app;
        _log.Visible = app;
        ((GameButton)_liveGameBtn).Primary = !app; ((GameButton)_liveAppBtn).Primary = app;
        _liveGameBtn.Invalidate(); _liveAppBtn.Invalidate();
    }

    /// <summary>Reads what the game added to the log since last time (every 0.8 s, also while another page is open).</summary>
    private void PollLiveLog()
    {
        var game = _game;
        if (game == null) { _liveState.Text = L.T("Choose your Nomad Drive folder first."); return; }
        var file = LogAnalyzer.BepInExLog(game);
        if (file != _liveFile) { _liveFile = file; _livePos = 0; _livePartial = ""; _liveLines.Clear(); RedrawLive(); }
        var running = game.IsRunning();
        if (!File.Exists(file))
        {
            _liveState.Text = L.T("No BepInEx log yet: it appears once the game starts with BepInEx.");
            return;
        }
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _livePos)
            {
                // A new game session started a new log.
                _livePos = 0; _livePartial = "";
                _liveLines.Clear();
                AddLiveLines(new[] { "— " + L.F("New game session, {0:HH:mm:ss}", DateTime.Now) + " —" }, initial: false);
                RedrawLive();
            }
            if (fs.Length > _livePos)
            {
                var first = _livePos == 0;
                if (first && fs.Length > 2 * 1024 * 1024) _livePos = fs.Length - 2 * 1024 * 1024; // a huge log: its last 2 MB
                fs.Position = _livePos;
                var buf = new byte[fs.Length - _livePos];
                var n = fs.Read(buf, 0, buf.Length);
                _livePos += n;
                var text = _livePartial + Encoding.UTF8.GetString(buf, 0, n);
                var lines = text.Split('\n');
                _livePartial = lines[^1];
                AddLiveLines(lines[..^1].Select(l => l.TrimEnd('\r')), initial: first);
            }
            var errors = _liveLines.Count(l => l.Level >= LogLevel.Error);
            _liveState.Text = (running ? "● " + L.T("Game running: new lines appear as they're written.") : L.T("The game isn't running: showing its last session."))
                              + "   " + L.F("{0} line(s), {1} error(s)", _liveLines.Count, errors) + (_livePaused ? "   · " + L.T("paused") : "");
            _liveState.ForeColor = running ? Theme.Good : Theme.Muted;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void AddLiveLines(IEnumerable<string> lines, bool initial)
    {
        var added = new List<LogLine>();
        foreach (var raw in lines)
        {
            var m = LevelRx.Match(raw);
            if (m.Success) _liveLast = Enum.Parse<LogLevel>(m.Groups[1].Value);
            else if (raw.Length > 0 && !char.IsWhiteSpace(raw[0]) && !raw.StartsWith("at ") && !raw.StartsWith("—")) _liveLast = LogLevel.Info; // a plain line (Unity's own)
            var line = new LogLine(_liveLast, raw); // stack-trace lines keep the level of the line above
            _liveLines.Add(line);
            added.Add(line);
        }
        if (_liveLines.Count > LiveMax) { _liveLines.RemoveRange(0, _liveLines.Count - LiveMax); RedrawLive(); return; }
        if (initial) { RedrawLive(); return; }
        if (!_livePaused) Append(added.Where(Shows));
    }

    private bool Shows(LogLine l)
    {
        var min = _liveLevel.SelectedIndex switch { 1 => LogLevel.Warning, 2 => LogLevel.Error, _ => LogLevel.Debug };
        if (l.Level < min && !l.Text.StartsWith("—")) return false;
        var q = _liveSearch.Text.Trim();
        return q.Length == 0 || l.Text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RedrawLive()
    {
        if (_live == null) return;
        _live.SuspendLayout();
        _live.Clear();
        Append(_liveLines.Where(Shows).TakeLast(5000), scroll: true);
        _live.ResumeLayout();
    }

    private static Color LevelColor(LogLevel l) => l switch
    {
        LogLevel.Fatal or LogLevel.Error => Theme.Bad,
        LogLevel.Warning => Theme.Warn,
        LogLevel.Message => Theme.Accent,
        LogLevel.Debug => Theme.Muted,
        _ => Theme.Text,
    };

    private void Append(IEnumerable<LogLine> lines, bool scroll = true)
    {
        var atEnd = _live.TextLength == 0 || _live.SelectionStart >= _live.TextLength - 1;
        foreach (var l in lines)
        {
            _live.SelectionStart = _live.TextLength;
            _live.SelectionLength = 0;
            _live.SelectionColor = l.Text.StartsWith("—") ? Theme.Good : LevelColor(l.Level);
            _live.AppendText(l.Text + "\n");
        }
        if (scroll && atEnd) { _live.SelectionStart = _live.TextLength; _live.ScrollToCaret(); }
    }

    private void ExportLogs()
    {
        try
        {
            var checks = _game != null ? string.Join(Environment.NewLine, BepInExChecker.Run(_game, _manifest, null).Select(c => $"[{c.Level}] {c.Area} — {c.Title}: {c.Detail}")) : "";
            var path = LogExport.Run(_game, _manifest.Mods, checks);
            Log(L.F("Exported logs to {0}", path));
            SetStatus(L.F("Saved {0} to your Desktop. Send it to the mod's author — your Windows user name is hidden in it.", Path.GetFileName(path)), good: true);
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
        }
        catch (Exception ex) { ShowError(L.T("Export logs"), ex); }
    }
}
