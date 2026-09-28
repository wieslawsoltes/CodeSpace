using System.Text.Json;
using CodeSpace.Workbench.Uno;
using Microsoft.UI.Xaml;

namespace CodeSpace.App;

/// <summary>Opt-in read-only state for browser interaction tests, without command execution.</summary>
internal sealed class BrowserDiagnostics
{
#if __WASM__
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public BrowserDiagnostics(WorkbenchView workbench)
    {
        var enabled = Uno.Foundation.WebAssemblyRuntime.InvokeJS("new URLSearchParams(location.search).get('e2e') === '1' ? 'codespace-e2e' : 'disabled'");
        Console.WriteLine("[CodeSpace] Browser diagnostics mode: " + enabled);
        if (enabled != "codespace-e2e") return;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestEvents = []; 'ready'");
        workbench.StatusChanged += (_, message) => Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestEvents.push(" + JsonSerializer.Serialize(message) + "); 'recorded'");
        void Publish()
        {
            var state = JsonSerializer.Serialize(new
            {
                files = workbench.Workspace.Files.ToDictionary(p => p.Key, p => p.Value.Buffer.ToString()),
                groups = workbench.Docking.Groups.Select(g => new { id = g.Id, tabs = g.Tabs, activeTab = g.ActiveTab }),
                sidebarVisible = workbench.Docking.State.SideBarVisible,
                panelVisible = workbench.Docking.State.PanelVisible,
                options = workbench.EditorOptions, editors = workbench.EditorDiagnostics, interaction = workbench.InteractionDiagnostics
            });
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestState = JSON.parse(" + JsonSerializer.Serialize(state) + "); 'updated'");
        }
        _timer.Tick += (_, _) => Publish();
        workbench.Unloaded += (_, _) => _timer.Stop(); Publish(); _timer.Start();
    }
#else
    public BrowserDiagnostics(WorkbenchView workbench) => Console.WriteLine("[CodeSpace] Desktop diagnostics adapter.");
#endif
}
