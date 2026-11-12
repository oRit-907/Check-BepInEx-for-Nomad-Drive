using System.IO.Pipes;
using NomadModDownloader.Core;

namespace NomadModDownloader;

internal static class Program
{
    private const string MutexName = @"Local\NomadModDownloader.SingleInstance";
    public const string PipeName = "NomadModDownloader.Links";
    /// <summary>Sent by the in-game Mods menu's Check BepInEx button ("--game-check"): open on the Check BepInEx page.</summary>
    public const string CheckLink = "nomadmods://check";

    [STAThread]
    private static void Main(string[] args)
    {
        L.Init(AppSettings.Load().Language);
        if (args.Length >= 2 && args[0] == "--smoke-test")
        {
            if (args.Length >= 3) { var st = AppSettings.Load(); st.GamePath = args[2]; st.AutoUpdateBepInEx = false; st.Save(); } // a test game folder
            SmokeTest(args[1]);
            return;
        }
        // Older Mods menus start the app with "--game-install <id>" or "--game-endorse <id>", and old nxm:// links may
        // still point here: this app doesn't install mods any more, so all of them just open it.
        var link = args.Any(a => a.Equals("--game-check", StringComparison.OrdinalIgnoreCase)) ? CheckLink : null;

        using var mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            // Already running: bring that window to the front.
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(3000);
                using var w = new StreamWriter(client) { AutoFlush = true };
                w.WriteLine(link ?? "show");
            }
            catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        NomadModDownloader.UI.Theme.Init();
        if (!StoreApp.IsPackaged && !Edition.Nexus) Updater.CleanUpAfterUpdate(); // remove the previous version's exe after an update
        Application.Run(new MainForm(link));
    }

    /// <summary>For the build checks: opens the window, saves a picture of each page into <paramref name="folder"/>, closes.</summary>
    private static void SmokeTest(string folder)
    {
        ApplicationConfiguration.Initialize();
        NomadModDownloader.UI.Theme.Init();
        Directory.CreateDirectory(folder);
        var form = new MainForm(null);
        var timer = new System.Windows.Forms.Timer { Interval = 9000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { form.SaveSmokePictures(folder); File.WriteAllText(Path.Combine(folder, "ok.txt"), "ok"); }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString()); }
            form.Close();
        };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
    }
}
