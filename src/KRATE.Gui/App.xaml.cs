using Microsoft.UI.Xaml;

namespace Krate.Gui;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        // A silent WinUI crash tells the user nothing; leave a trace next to the app. And keep the app
        // alive: one tool's stray exception shouldn't drop the whole window to the desktop — log it,
        // mark it handled, and let the user carry on with the other tools. crash.log still records it.
        UnhandledException += (_, e) =>
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), e.Exception.ToString()); } catch { }
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Probe the Rust core once, up front. It would load lazily on the first tool run anyway,
        // but doing it here means the interface language is handed over before anything asks for a
        // string, and a missing krate_core.dll is discovered at launch rather than mid-use. A
        // failed probe is not fatal: RustCore falls back to the managed implementation.
        _ = Krate.Core.RustCore.Available;

        var window = new MainWindow();
        MainWindow = window;
        window.Activate();

        // Right-click-menu launch: "KRATE.exe --<verb> <path>", or "--tool <id> <path>".
        var argv = Environment.GetCommandLineArgs();
        if (argv.Length >= 4 && argv[1] == "--tool")
            window.OpenToolWithFile(argv[2], argv[3]);
        else if (argv.Length >= 3 && argv[1] is "--compress" or "--extract" or "--encrypt" or "--convert" or "--edit")
            window.OpenForFile(argv[2], argv[1][2..]); // strip the "--"
    }
}
