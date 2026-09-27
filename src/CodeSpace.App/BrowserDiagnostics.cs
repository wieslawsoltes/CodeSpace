using System.Text.Json;
using CodeSpace.Workbench.Uno;
using Microsoft.UI.Xaml;

namespace CodeSpace.App;

/// <summary>Opt-in, read-only diagnostics for real browser interaction tests. No command or code execution endpoint.</summary>
internal sealed class BrowserDiagnostics
{
#if __WASM__
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public BrowserDiagnostics(WorkbenchView workbench)
    {
        if (Uno.Foundation.WebAssemblyRuntime.InvokeJS("String(new URLSearchParams(location.search).get('e2e') === '1')") != "true") return;
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestEvents = []; 'ready'");
        workbench.StatusChanged += (_, message) => Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestEvents.push(" + JsonSerializer.Serialize(message) + "); 'recorded'");
        _timer.Tick += (_, _) =>
        {
            var state = JsonSerializer.Serialize(new
            {
                files = workbench.Workspace.Files.ToDictionary(p => p.Key, p => p.Value.Buffer.ToString()),
                groups = workbench.Docking.Groups.Select(g => new { id = g.Id, tabs = g.Tabs, activeTab = g.ActiveTab }),
                sidebarVisible = workbench.Docking.State.SideBarVisible,
                panelVisible = workbench.Docking.State.PanelVisible
            });
            Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.__codespaceTestState = JSON.parse(" + JsonSerializer.Serialize(state) + "); 'updated'");
        };
        workbench.Unloaded += (_, _) => _timer.Stop(); _timer.Start();
    }
#else
    public BrowserDiagnostics(WorkbenchView workbench) { }
#endif
}
