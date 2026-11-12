using NomadModDownloader.Core;
using NomadModDownloader.UI;

namespace NomadModDownloader;

/// <summary>"Backups": every backup the app made, with Restore and Delete, and Uninstall BepInEx.</summary>
internal sealed partial class MainForm
{
    private ListView _bkList = null!;
    private TextBox _bkDetail = null!;
    private Label _bkHeader = null!;
    private Button _bkRestore = null!, _bkDelete = null!, _bkOpen = null!;

    private Control BuildBackupsPage()
    {
        var page = new GlassPanel { Padding = new Padding(16, 16, 16, 12) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, WrapContents = true, Padding = new Padding(0, 0, 0, 6) };
        _bkRestore = Theme.Button(L.T("Restore"), true);
        _bkRestore.Click += async (_, _) => await RunExclusive(RestoreBackupAsync);
        _bkOpen = Theme.Button(L.T("Open folder"));
        _bkOpen.Click += (_, _) => { if (SelectedBackup() is { } b) Web.Open(b.Folder); };
        _bkDelete = Theme.Button(L.T("Delete"));
        _bkDelete.Click += (_, _) => DeleteBackup();
        var uninstall = Theme.Button(L.T("Uninstall BepInEx…"));
        uninstall.Click += async (_, _) => await RunExclusive(UninstallBepInExAsync);
        foreach (var b in new[] { _bkRestore, _bkOpen, _bkDelete, uninstall }) { b.Margin = new Padding(0, 0, 8, 6); toolbar.Controls.Add(b); }

        _bkHeader = new Label { Dock = DockStyle.Top, Height = Theme.Small.Height + 14, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };
        _bkList = Theme.List();
        _bkList.Columns.Add(L.T("Backup"), 380);
        _bkList.Columns.Add(L.T("Made"), 170);
        _bkList.Columns.Add(L.T("Files"), 80);
        _bkList.Columns.Add(L.T("Size"), 100);
        _bkList.SelectedIndexChanged += (_, _) => ShowBackupDetail();
        _bkDetail = new TextBox { Dock = DockStyle.Bottom, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = Theme.Mono, WordWrap = false };
        _bkDetail.HandleCreated += (_, _) => Theme.DarkScrollbars(_bkDetail);
        var gap = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = Color.Transparent };
        page.Controls.Add(_bkList);
        page.Controls.Add(gap);
        page.Controls.Add(_bkDetail);
        page.Controls.Add(_bkHeader);
        page.Controls.Add(toolbar);
        return page;
    }

    private BackupInfo? SelectedBackup() => _bkList.SelectedItems.Count > 0 ? _bkList.SelectedItems[0].Tag as BackupInfo : null;

    private void RefreshBackups()
    {
        if (_bkList == null) return;
        var list = Backups.List();
        _bkList.BeginUpdate();
        _bkList.Items.Clear();
        foreach (var b in list)
        {
            var item = new ListViewItem(b.What) { Tag = b };
            item.SubItems.Add(Fmt.Date(b.When) + " " + b.When.ToString("HH:mm"));
            item.SubItems.Add(b.Files.ToString());
            item.SubItems.Add(Fmt.Size(b.Bytes));
            _bkList.Items.Add(item);
        }
        _bkList.EndUpdate();
        _bkHeader.Text = list.Count == 0
            ? L.T("No backups yet. The app makes one before every fix, update or repair that changes files.")
            : L.F("{0} backup(s), {1} in all, in {2}", list.Count, Fmt.Size(list.Sum(b => b.Bytes)), AppPaths.Backups);
        ShowBackupDetail();
    }

    private void ShowBackupDetail()
    {
        var b = SelectedBackup();
        _bkRestore.Enabled = _bkDelete.Enabled = _bkOpen.Enabled = b != null;
        if (b == null) { _bkDetail.Text = ""; return; }
        try
        {
            var files = Directory.EnumerateFiles(b.Folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(b.Folder, f)).OrderBy(f => f).ToList();
            _bkDetail.Text = string.Join("\r\n", files.Take(300)) + (files.Count > 300 ? "\r\n" + L.F("…and {0} more", files.Count - 300) : "");
        }
        catch (Exception ex) { _bkDetail.Text = ex.Message; }
    }

    private async Task RestoreBackupAsync()
    {
        var game = RequireGame();
        if (game == null || SelectedBackup() is not { } b) return;
        if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var replace = Backups.WouldReplace(b, game);
        var ask = L.F("Copy the {0} file(s) in this backup back into the game folder?", b.Files) + "\n\n" + b.What
                  + (replace.Count > 0 ? "\n\n" + L.F("{0} file(s) in the game folder will be replaced, for example:", replace.Count) + "\n" + string.Join("\n", replace.Take(6).Select(r => "• " + r)) : "")
                  + "\n\n" + L.T("The backup itself is kept.");
        if (MessageBox.Show(this, ask, L.T("Restore"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var n = Backups.Restore(b, game);
        Log(L.F("Restored {0} file(s) from the backup \"{1}\".", n, b.Name));
        SetStatus(L.F("Restored {0} file(s).", n), good: true);
        RefreshGame();
        await RefreshChecksAsync(lookOnline: false);
    }

    private void DeleteBackup()
    {
        if (SelectedBackup() is not { } b) return;
        var ask = L.F("Delete this backup for good? Its {0} file(s) ({1}) can't be brought back afterwards.", b.Files, Fmt.Size(b.Bytes)) + "\n\n" + b.What;
        if (MessageBox.Show(this, ask, L.T("Delete backup"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { Backups.Delete(b); Log(L.F("Deleted the backup \"{0}\".", b.Name)); SetStatus(L.T("Backup deleted.")); }
        catch (Exception ex) { ShowError(L.T("Delete backup"), ex); }
        RefreshBackups();
    }

    private async Task UninstallBepInExAsync()
    {
        var game = RequireGame();
        if (game == null) return;
        if (game.IsRunning()) { MessageBox.Show(this, L.T("Close Nomad Drive first, then try again."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (BepInExManager.Check(game).State == BepInExState.NotInstalled) { SetStatus(L.T("BepInEx isn't installed.")); return; }
        var keep = new TaskDialogCommandLinkButton(L.T("Remove BepInEx, keep my mods"), L.T("Mods, their settings and the Mods menu's data stay in the game folder (unused), ready for when BepInEx comes back."));
        var all = new TaskDialogCommandLinkButton(L.T("Remove BepInEx and my mods"), L.T("The game goes back to how it was before mods. Everything goes into one backup."));
        var page = new TaskDialogPage
        {
            Caption = L.T("Uninstall BepInEx"), Heading = L.T("Uninstall BepInEx?"),
            Text = L.T("Nomad Drive starts without mods afterwards. Nothing is deleted: everything that's removed goes into a backup, and Restore on this page puts it back."),
            Buttons = { keep, all, TaskDialogButton.Cancel }, DefaultButton = keep, Icon = TaskDialogIcon.Warning, AllowCancel = true,
        };
        var r = TaskDialog.ShowDialog(this, page);
        if (r != keep && r != all) return;
        var (backup, moved) = Backups.Uninstall(game, withMods: r == all);
        Log(L.F("Uninstalled BepInEx: {0} file(s) moved to {1}.", moved, backup));
        SetStatus(L.F("BepInEx is uninstalled. To bring it back, Restore the \"{0}\" backup, or press Install BepInEx.", Path.GetFileName(backup)), good: true);
        RefreshGame();
        RefreshBackups();
        await RefreshChecksAsync(lookOnline: false);
    }
}
