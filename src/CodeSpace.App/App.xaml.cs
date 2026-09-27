using CodeSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private WorkbenchView? _workbench;
    public App() { InitializeComponent(); UnhandledException += (_, e) => { System.Diagnostics.Debug.WriteLine(e.Exception); }; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "CodeSpace" };
        _window.Content = new TextBlock { Text = "Starting CodeSpace…", Margin = new Thickness(28), FontSize = 20 };
        _window.Activate();
        try
        {
            await ApplicationFonts.InitializeAsync();
            var platform = new WorkbenchPlatform(); _workbench = new WorkbenchView(platform); _window.Content = _workbench;
            await _workbench.InitializeAsync();
        }
        catch (Exception exception)
        {
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "CodeSpace could not start.\n\n" + exception, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), IsTextSelectionEnabled = true } };
        }
    }
}
