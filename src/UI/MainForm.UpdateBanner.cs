using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>
/// "A new version is out" banner across the top of the app, like the website's notice banners: a tinted strip with a
/// border, the message, Update now / What's new, and ✕ to dismiss. Nothing installs until Update now (or the sidebar's
/// Update button) is clicked; ✕ only hides the banner until the next start.
/// </summary>
internal sealed partial class MainForm
{
    private GlassPanel _updateBanner = null!;
    private Label _updateBannerText = null!;
    private Label _updateBannerNotes = null!;
    private string? _updateDismissed; // the version whose banner was closed this session
    /// <summary>Asks nomaddrivemods.online again every 5 minutes while the app is open.</summary>
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 5 * 60 * 1000 };
    private bool _lookingForUpdate, _updateFailLogged;

    private Control BuildUpdateBanner()
    {
        // The site's "info" banner: blue tint (rgba(47,128,237,.16) over the panel) with a light-blue border.
        _updateBanner = new GlassPanel
        {
            Dock = DockStyle.Top, Height = 10, Padding = new Padding(16, 10, 10, 10), Visible = false, Radius = 12,
            Fill = Blend(Color.FromArgb(47, 128, 237), Theme.Panel, 0.16), Border = Blend(Color.FromArgb(86, 204, 242), Theme.Panel, 0.5),
        };
        var close = new Label { Text = "✕", AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Muted, BackColor = Color.Transparent, Cursor = Cursors.Hand, Dock = DockStyle.Right, Padding = new Padding(8, 4, 4, 4) };
        close.MouseEnter += (_, _) => close.ForeColor = Theme.Text;
        close.MouseLeave += (_, _) => close.ForeColor = Theme.Muted;
        close.Click += (_, _) => { _updateDismissed = _appUpdate?.Version.ToString(3); _updateBanner.Visible = false; };

        var row = new FlowLayoutPanel { Dock = DockStyle.Top, BackColor = Color.Transparent, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var words = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0, 2, 16, 2) };
        _updateBannerText = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 2) };
        _updateBannerNotes = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 2) };
        words.Controls.AddRange(new Control[] { _updateBannerText, _updateBannerNotes });

        var update = Theme.Button("⬆ " + L.T("Update now"), true);
        update.Click += async (_, _) => await RunExclusive(InstallAppUpdateAsync);
        var whatsNew = Theme.Button(L.T("What's new"));
        whatsNew.Click += (_, _) => { if (_appUpdate != null) Web.Open(_appUpdate.PageUrl); };
        foreach (var b in new[] { update, whatsNew }) b.Margin = new Padding(0, 4, 8, 4);
        row.Controls.AddRange(new Control[] { words, update, whatsNew });

        _updateBanner.Controls.Add(row);
        _updateBanner.Controls.Add(close);
        row.SizeChanged += (_, _) => _updateBanner.Height = row.Height + _updateBanner.Padding.Vertical;
        // Long text wraps to the banner's width.
        _updateBanner.SizeChanged += (_, _) =>
        {
            var w = Math.Max(240, _updateBanner.Width - _updateBanner.Padding.Horizontal - close.Width - 340);
            _updateBannerText.MaximumSize = _updateBannerNotes.MaximumSize = new Size(w, 0);
            if (_updateBanner.Visible) BeginInvoke(FitUpdateBanner);
        };

        var holder = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 12) };
        holder.Controls.Add(_updateBanner);
        return holder;
    }

    /// <summary>Shows the banner for <paramref name="info"/> (unless that version's banner was closed this session).</summary>
    private void ShowUpdateBanner(UpdateInfo info)
    {
        var v = info.Version.ToString(3);
        if (_updateDismissed == v) return;
        _updateBannerText.Text = L.F("Check BepInEx {0} is available — you have {1}.", v, Updater.Current.ToString(3));
        _updateBannerNotes.Text = FirstNoteLine(info.Notes) is { Length: > 0 } note ? note : L.T("Click Update now to install it. The app restarts when it's done.");
        _updateBanner.Visible = true;
        FitUpdateBanner();
    }

    /// <summary>Banner height = its contents' (the row is sized by its buttons and wrapped text).</summary>
    private void FitUpdateBanner()
    {
        var row = _updateBanner.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
        if (row == null) return;
        _updateBanner.PerformLayout();
        _updateBanner.Height = row.GetPreferredSize(new Size(Math.Max(1, row.Width), 0)).Height + _updateBanner.Padding.Vertical;
    }

    /// <summary>The first real line of the release notes (no headings or bullets), cut to one line.</summary>
    private static string FirstNoteLine(string notes)
    {
        foreach (var raw in (notes ?? "").Split('\n'))
        {
            var line = raw.Trim().TrimStart('#', '-', '*', '•', ' ').Replace("**", "").Trim();
            if (line.Length < 4) continue;
            return line.Length > 160 ? line[..157] + "…" : line;
        }
        return "";
    }

    private static Color Blend(Color top, Color under, double alpha) => Color.FromArgb(
        (int)(top.R * alpha + under.R * (1 - alpha)), (int)(top.G * alpha + under.G * (1 - alpha)), (int)(top.B * alpha + under.B * (1 - alpha)));
}
