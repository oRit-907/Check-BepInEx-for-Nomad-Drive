using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>
/// Game update watch: notices when Nomad Drive itself updates (Steam's build id, or the game's code changing), at start
/// and every two minutes while the app is open. Then it checks BepInEx and the mods again by itself, and says so in a
/// banner, with a warning when the update changed the game's Unity version (Unity Compatibility needs new libraries).
/// </summary>
internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _gameWatch = new() { Interval = 2 * 60 * 1000 };
    private GlassPanel _gameBanner = null!;
    private Label _gameBannerText = null!;

    private Control BuildGameUpdateBanner()
    {
        _gameBanner = new GlassPanel { Dock = DockStyle.Top, Height = 10, Padding = new Padding(16, 12, 16, 12), Visible = false, Border = Theme.Accent, Radius = 12 };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, BackColor = Color.Transparent, WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        _gameBannerText = new Label { AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Theme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 6, 16, 6) };
        var check = Theme.Button(L.T("See the check"), true);
        check.Click += (_, _) => { _gameBanner.Visible = false; ShowPage("Check BepInEx"); };
        var loading = Theme.Button(L.T("Mods not loading"));
        loading.Click += (_, _) => { _gameBanner.Visible = false; ShowPage("Mods not loading"); };
        var dismiss = Theme.Button(L.T("Dismiss"));
        dismiss.Click += (_, _) => _gameBanner.Visible = false;
        foreach (var b in new[] { check, loading, dismiss }) b.Margin = new Padding(0, 0, 8, 0);
        row.Controls.AddRange(new Control[] { _gameBannerText, check, loading, dismiss });
        _gameBanner.Controls.Add(row);
        row.SizeChanged += (_, _) => _gameBanner.Height = row.Height + _gameBanner.Padding.Vertical;
        var holder = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 12) };
        holder.Controls.Add(_gameBanner);
        return holder;
    }

    private void CheckGameUpdate(GameInstall g)
    {
        if (g.IsRunning()) return; // Steam doesn't update a running game; look again once it's closed
        string fp, unity;
        try { fp = GameVersion.Fingerprint(g); unity = UnityCompat.UnityVersion(g) ?? ""; } catch { return; }
        if (fp.Length == 0) return;
        var before = _settings.LastGameFingerprint;
        var unityBefore = _settings.LastUnityVersion;
        if (before == fp && unityBefore == unity) return;
        _settings.LastGameFingerprint = fp;
        _settings.LastUnityVersion = unity;
        _settings.Save();
        if (before == null) return; // first run: nothing to compare with

        var unityChanged = !string.IsNullOrEmpty(unityBefore) && unity.Length > 0 && unityBefore != unity;
        Log(L.T("Nomad Drive has been updated.") + (unityChanged ? " " + L.F("Its Unity version changed from {0} to {1}.", unityBefore!, unity) : "") + " " + L.T("Checking BepInEx and the mods again."));
        _gameBannerText.Text = L.T("Nomad Drive was updated. BepInEx and your mods were checked again.")
            + (unityChanged ? " " + L.F("The game moved to Unity {0}, so Unity Compatibility needs that version's libraries: Check BepInEx shows the fix.", unity) : "")
            + " " + L.T("If a mod stops working, look for an update to it in the Mods menu.");
        _gameBanner.Visible = true;
        RefreshGame();
        _ = RefreshChecksAsync(lookOnline: false);
        RefreshLoading();
    }
}
