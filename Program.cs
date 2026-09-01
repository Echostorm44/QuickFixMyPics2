using Cascade.UI;
using Cascade.UI.Backend.Etch;
using Cascade.UI.Installer.Update;   // Updater, UpdateConfig, UpdateCheckResult
using Cascade.UI.Updater.Core;       // UpdateBootstrap
using QuickFixMyPics2;

// ── Auto-update ─────────────────────────────────────────────────────────────
// The installer drops a `cascade-update` shim next to the app. Apply any update a
// prior run staged, then configure the updater to check GitHub Releases and stage
// new versions in the background — applied automatically on the next launch. All of
// this is best-effort: it must never stop the app from starting.
try
{
    string installDir = AppContext.BaseDirectory;
    UpdateBootstrap.DetectCrashAndRollback(installDir);
    UpdateBootstrap.ApplyPendingIfAny(installDir);
    UpdateBootstrap.BeginLaunch(installDir);

    string appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "1.0.0";

    Updater.Configure(
        new UpdateConfig
        {
            ManifestUrl = "https://github.com/Echostorm44/QuickFixMyPics2/releases/latest/download/manifest.json",
            CheckOnStartup = true,
            CheckInterval = TimeSpan.FromHours(6),
            AutoDownload = true,   // stage in the background; applied on next launch
            Channel = "stable",
        },
        appVersion,
        "win-x64",
        installDir,
        Environment.ProcessPath ?? System.IO.Path.Combine(installDir, "QuickFixMyPics2.exe"));

    _ = Task.Run(async () =>
    {
        try
        {
            UpdateCheckResult result = await Updater.CheckNowAsync();
            if (result.IsAvailable)
            {
                await Updater.DownloadAsync();
            }
        }
        catch
        {
            // A failed update check must be invisible to the user.
        }
    });
}
catch
{
    // Any updater wiring failure is non-fatal — the app runs regardless.
}

// Files passed on the command line (right-click "Open with"). Read straight from
// the environment — App.Args isn't populated until App.Run starts, which is after
// this line. Everything that isn't a flag is treated as a path.
FileIntake.QueueInitial(
    Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith('-')));

// A second launch forwards its file arguments to this running instance instead
// of opening a new window (see config.SingleInstance below).
App.OnSecondInstanceLaunched(args =>
    FileIntake.Receive(args.Where(a => !a.StartsWith('-'))));

// Files dragged from Explorer and dropped onto the window.
App.OnFilesDropped(FileIntake.Receive);

App.Run<MainView>(config =>
{
    config.Theme = new AppleTheme(ThemeMode.Dark);
    config.SingleInstance = true;
    config.WindowSize = new Size(560, 720);
});
