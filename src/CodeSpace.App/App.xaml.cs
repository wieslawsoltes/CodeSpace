using CodeSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private WorkbenchView? _workbench;
    private BrowserDiagnostics? _diagnostics;
    private WorkbenchPlatform? _platform;
    public App() { InitializeComponent(); UnhandledException += (_, e) => Console.Error.WriteLine("[CodeSpace] " + e.Exception); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "CodeSpace" };
        _window.Content = new TextBlock { Text = "Starting CodeSpace…", Margin = new Thickness(28), FontSize = 20 };
        _window.Activate();
        try
        {
            Console.WriteLine("[CodeSpace] Initializing fonts.");
            await ApplicationFonts.InitializeAsync();
            BrowserKeyboardInput.Initialize();
            _platform = new WorkbenchPlatform(); _workbench = new WorkbenchView(_platform); _window.Content = _workbench;
            Console.WriteLine("[CodeSpace] Workbench constructed.");
            _diagnostics = new BrowserDiagnostics(_workbench);
            await _workbench.InitializeAsync();
            Console.WriteLine("[CodeSpace] Recovery and platform initialization complete.");
            _window.Closed += async (_, _) => { _workbench.Dispose(); if (_platform.ExtensionBridge is { } bridge) await bridge.DisposeAsync(); };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("[CodeSpace] Startup failed: " + exception);
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "CodeSpace could not start.\n\n" + exception, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), IsTextSelectionEnabled = true } };
        }
    }
}
