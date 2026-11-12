using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>"Find the bad mod": a step-by-step search that halves the suspects each round (see BadModFinder).</summary>
internal sealed partial class MainForm
{
    private Label _bmTitle = null!, _bmText = null!, _bmProgress = null!;
    private FlowLayoutPanel _bmButtons = null!;
    private ListView _bmList = null!;
    private BadModFinder? _finder;

    private Control BuildBadModPage()
    {
        var page = new GlassPanel { Padding = new Padding(16, 16, 16, 12) };
        _bmTitle = new Label { Dock = DockStyle.Top, AutoSize = false, Height = Theme.H2.Height + 12, Font = Theme.H2, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _bmText = new Label { Dock = DockStyle.Top, AutoSize = false, Height = Theme.Body.Height * 5 + 8, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _bmProgress = new Label { Dock = DockStyle.Top, Height = Theme.Small.Height + 10, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };
        _bmButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 4, 0, 10) };
        _bmList = Theme.List();
        _bmList.Columns.Add("", 44);
        _bmList.Columns.Add(L.T("Mod"), 340);
        _bmList.Columns.Add(L.T("This round"), 360);
        page.Controls.Add(_bmList);
        page.Controls.Add(_bmButtons);
        page.Controls.Add(_bmProgress);
        page.Controls.Add(_bmText);
        page.Controls.Add(_bmTitle);
        return page;
    }

    private Button BmButton(string text, bool primary, Func<Task> click)
    {
        var b = Theme.Button(text, primary);
        b.Margin = new Padding(0, 0, 8, 6);
        b.Click += async (_, _) => await RunExclusive(click);
        _bmButtons.Controls.Add(b);
        return b;
    }

    private void RefreshBadMod()
    {
        if (_bmTitle == null) return;
        _finder ??= BadModFinder.Load();
        if (_finder != null && _game != null && !string.Equals(Path.GetFullPath(_finder.GameRoot), Path.GetFullPath(_game.Root), StringComparison.OrdinalIgnoreCase)) _finder = null;
        _bmButtons.Controls.Clear();
        _bmList.Items.Clear();
        var game = _game;
        if (game == null) { _bmTitle.Text = L.T("Choose your Nomad Drive folder first."); _bmText.Text = ""; _bmProgress.Text = ""; return; }

        if (_finder == null)
        {
            var mods = ModEntries.Scan(game).Where(e => e.On).ToList();
            _bmTitle.Text = L.T("Something broke? Find which mod does it.");
            _bmText.Text = L.T("The game crashes, won't start, or something odd happens since you added mods? This switches half of your mods off, you play, and you say whether the problem is still there. Each round halves the list, so even 30 mods take about 5 rounds.")
                           + "\n\n" + L.T("Only mods that are on now are switched, and they all come back on when you stop, even if the app was closed in between.");
            _bmProgress.Text = L.F("{0} mod(s) are switched on.", mods.Count);
            BmButton(L.T("Start"), true, async () =>
            {
                if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                _finder = BadModFinder.Start(game);
                Log(L.F("Find the bad mod: started with {0} mod(s).", _finder.Mods.Count));
                RefreshBadMod();
                await Task.CompletedTask;
            }).Enabled = mods.Count > 0;
            foreach (var m in mods)
            {
                var item = new ListViewItem("•") { ForeColor = Theme.Muted };
                item.SubItems.Add(m.Name).ForeColor = Theme.Text;
                item.SubItems.Add(m.Rel.Replace('/', '\\')).ForeColor = Theme.Muted;
                _bmList.Items.Add(item);
            }
            return;
        }

        var f = _finder;
        if (f.Result != null)
        {
            var bad = f.Result.Length > 0 ? f.Find(f.Result) : null;
            _bmTitle.Text = bad != null ? L.F("Found it: {0}", bad.Name) : L.T("It isn't one mod on its own");
            _bmTitle.ForeColor = bad != null ? Theme.Accent : Theme.Warn;
            _bmText.Text = bad != null
                ? L.F("With {0} switched off the problem went away, and with it on it came back. Leave it off for now, look for an update in the Mods menu, and tell its author (Export logs for mod devs on Live log helps).", bad.Name)
                : L.T("The problem stayed even with every suspect switched off. It may come from BepInEx itself (run Check BepInEx), from the game, or from two mods only together. Your mods are all back on.");
            _bmProgress.Text = L.F("Took {0} round(s).", f.Round);
            if (bad != null)
            {
                BmButton(L.F("Switch everything back on except {0}", bad.Name), true, async () => { f.Stop(game, leaveOff: bad.Rel); Log(L.F("Find the bad mod: {0} is off; every other mod is back on.", bad.Name)); _finder = null; RefreshBadMod(); await Task.CompletedTask; });
                BmButton(L.T("Switch everything back on"), false, async () => { f.Stop(game); Log(L.T("Find the bad mod: every mod is back on.")); _finder = null; RefreshBadMod(); await Task.CompletedTask; });
            }
            else BmButton(L.T("Done"), true, async () => { f.Stop(game); _finder = null; RefreshBadMod(); await Task.CompletedTask; });
            return;
        }

        var off = f.Off.ToHashSet();
        _bmTitle.Text = L.F("Round {0}: {1} of {2} mods are off", f.Round, off.Count, f.Mods.Count);
        _bmTitle.ForeColor = Theme.Text;
        _bmText.Text = L.T("1. Press PLAY and try to make the problem happen again.") + "\n"
                     + L.T("2. Close the game.") + "\n"
                     + L.T("3. Say whether the problem was still there.");
        _bmProgress.Text = L.F("{0} suspect(s) left, about {1} more round(s).", f.Suspects.Count, (int)Math.Ceiling(Math.Log2(Math.Max(1, f.Suspects.Count))));
        BmButton("▶  " + L.T("Play"), false, PlayAsync);
        BmButton(L.T("The problem is still there"), true, async () => { if (!ClosedFirst(game)) return; f.Answer(game, problemStillThere: true); RefreshBadMod(); await Task.CompletedTask; });
        BmButton(L.T("The problem is gone"), true, async () => { if (!ClosedFirst(game)) return; f.Answer(game, problemStillThere: false); RefreshBadMod(); await Task.CompletedTask; });
        BmButton(L.T("Stop and switch everything back on"), false, async () => { if (!ClosedFirst(game)) return; f.Stop(game); Log(L.T("Find the bad mod: stopped; every mod is back on.")); _finder = null; RefreshBadMod(); await Task.CompletedTask; });

        var suspects = f.Suspects.ToHashSet();
        foreach (var m in f.Mods.OrderBy(m => off.Contains(m.Rel) ? 0 : 1).ThenBy(m => m.Name))
        {
            var isOff = off.Contains(m.Rel);
            var item = new ListViewItem(isOff ? "○" : "●") { ForeColor = isOff ? Theme.Warn : Theme.Good };
            item.SubItems.Add(m.Name).ForeColor = suspects.Contains(m.Rel) ? Theme.Text : Theme.Muted;
            item.SubItems.Add(isOff ? (f.Tested.Contains(m.Rel) ? L.T("Off this round") : L.T("Off (it needs a mod that's off)"))
                                    : suspects.Contains(m.Rel) ? L.T("On, still a suspect") : L.T("On, cleared")).ForeColor = isOff ? Theme.Warn : Theme.Muted;
            _bmList.Items.Add(item);
        }
    }

    private bool ClosedFirst(GameInstall game)
    {
        if (!game.IsRunning()) return true;
        MessageBox.Show(this, L.T("Close Nomad Drive first, then answer."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }
}
