using Cascade.UI;
using Cascade.UI.Backend.Etch;
using Cascade.UI.Installer.Update;   // Updater, UpdateConfig, UpdateCheckResult
using QuickFixMyPics2;

// ── Auto-update ─────────────────────────────────────────────────────────────
// Configure the updater so the app can offer updates from GitHub Releases. We do NOT
// apply anything silently: MainView checks in the background and, if a newer version
// exists, shows an in-app banner asking the user to update (see MainView.CheckForUpdate).
// The app's only part in crash-rollback is Updater.MarkHealthy() once it is up (MainView).
// The launch marker and the crash check belong to the update shim (cascade-update.exe):
// calling UpdateBootstrap.DetectCrashAndRollback here found the marker the shim had just
// written for THIS launch and rolled every update straight back out (Cascade INSTALL-002).
// All best-effort: never block app startup.
try
{
    string installDir = AppContext.BaseDirectory;

    string appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{v.Build}"
        : "1.0.0";

    Updater.Configure(
        new UpdateConfig
        {
            ManifestUrl = "https://github.com/Echostorm44/QuickFixMyPics2/releases/latest/download/manifest.json",
            CheckOnStartup = true,
            CheckInterval = TimeSpan.FromHours(6),
            AutoDownload = false,  // nothing is downloaded or applied without the user asking for it
            Channel = "stable",
        },
        appVersion,
        "win-x64",
        installDir,
        Environment.ProcessPath ?? System.IO.Path.Combine(installDir, "QuickFixMyPics2.exe"));
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
