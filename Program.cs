using Cascade.UI;
using Cascade.UI.Backend.Etch;
using QuickFixMyPics2;

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
