using System.Text;
using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Docking;
using CodeSpace.Editor;
using CodeSpace.Extensions;
using CodeSpace.Languages;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace CodeSpace.Workbench.Uno;

/// <summary>Embeddable workbench composed entirely from Uno controls and CodeSpace's custom editor.</summary>
public sealed partial class WorkbenchView : Grid, IDisposable
{
    private readonly IWorkbenchPlatform _platform;
    private readonly DockLayout _layout = new();
    private readonly CommandRegistry _commands = new();
    private readonly Dictionary<string, EditorSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Border> _tabHosts = [];
    private readonly List<CodeEditorControl> _editors = [];
    private readonly Dictionary<string, ExtensionPackage> _extensions = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly ActivityBar _activity = new();
    private readonly Grid _main = new(), _center = new();
    private readonly Border _sidebar = new(), _editorHost = new(), _panel = new();
    private readonly QuickPickControl _quickPick = new();
    private readonly TextBlock _statusLeft = WorkbenchColors.Text("Local workspace", 12), _statusRight = WorkbenchColors.Text("", 12);
    private readonly TextBlock _message = WorkbenchColors.Text("", 12, "#9d9d9d");
    private readonly List<string> _output = [];
    private readonly StackPanel _panelBody = new();
    private readonly FileTreeControl _tree = new();
    private Workspace _workspace;
    private DockNode? _renderedRoot;
    private string _activeGroup = "primary", _side = "files", _panelTab = "OUTPUT";
    private CodeEditorControl? _activeEditor;
    private bool _suppressLayout, _restoring, _disposed;
    private bool _minimap = true, _whitespace;
    private float _fontSize = 14;
    private int _untitled = 1;
    public Workspace Workspace => _workspace;
    public DockLayout Docking => _layout;
    public CommandRegistry Commands => _commands;
    public event EventHandler<string>? StatusChanged;
    public WorkbenchView(IWorkbenchPlatform platform)
    {
        _platform = platform; _workspace = SampleWorkspace.Create(); Background = WorkbenchColors.Background;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
        Children.Add(BuildTitleBar()); Children.Add(_main); SetRow(_main, 1);
        _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(254) });
        _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _main.ColumnDefinitions.Add(new ColumnDefinition());
        _main.Children.Add(_activity); _main.Children.Add(_sidebar); SetColumn(_sidebar, 1);
        var sideSplitter = new DockSplitter(); sideSplitter.Delta += (_, delta) => _layout.SetSideBar(width: _layout.State.SideBarWidth + delta); _main.Children.Add(sideSplitter); SetColumn(sideSplitter, 2);
        _main.Children.Add(_center); SetColumn(_center, 3);
        _center.RowDefinitions.Add(new RowDefinition()); _center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); _center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
        _center.Children.Add(_editorHost); var panelSplitter = new DockSplitter(false); panelSplitter.Delta += (_, delta) => _layout.SetPanel(height: _layout.State.PanelHeight - delta); _center.Children.Add(panelSplitter); SetRow(panelSplitter, 1); _center.Children.Add(_panel); SetRow(_panel, 2);
        Children.Add(BuildStatusBar()); SetRow(Children[^1] as FrameworkElement, 2);
        Children.Add(_quickPick); SetRow(_quickPick, 1); Canvas.SetZIndex(_quickPick, 100);
        _quickPick.Dismissed += (_, _) => _activeEditor?.FocusEditor();
        _activity.Selected += (_, side) => { _side = side; _layout.SetSideBar(visible: true); BuildSidebar(); };
        _tree.Workspace = _workspace; _tree.FileActivated += (_, path) => Open(path); _tree.EntryContextRequested += (_, entry) => ShowEntryActions(entry);
        _layout.Changed += LayoutChanged;
        _workspace.Changed += WorkspaceChanged;
        _recoveryTimer.Tick += async (_, _) => { _recoveryTimer.Stop(); await SaveRecoveryAsync(); };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshTabs(); if (_side == "source") BuildSidebar(); if (_panelTab == "PROBLEMS") RefreshPanelBody(); UpdateStatus(); };
        RegisterCommands(); InitializeFeatures(); BuildSidebar(); BuildPanel();
        _suppressLayout = true; _layout.Open("README.md"); _layout.Open("src/Program.cs"); _suppressLayout = false; RenderDock(); ApplyDimensions();
        Log("CodeSpace 0.1.0 · Uno Platform · custom Skia editor");
        Log("Local workspace ready. Ctrl+P opens files; Ctrl+Shift+P runs commands.");
        Log("Extensions: a tested API subset, not universal VS Code compatibility.");
        KeyDown += WorkbenchKeyDown;
        Loaded += (_, _) => { _activeEditor?.FocusEditor(); UpdateStatus(); };
    }
    public async Task InitializeAsync()
    {
        try
        {
            var json = await _platform.LoadRecoveryAsync();
            if (!string.IsNullOrEmpty(json))
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement;
                if (root.TryGetProperty("workspace", out var stored))
                {
                    var workspace = Workspace.Deserialize(stored.GetString()!); _restoring = true; RestoreFeatureState(root, workspace); ReplaceWorkspace(workspace);
                    if (root.TryGetProperty("layout", out var layout))
                    {
                        try { _layout.Restore(layout.GetString()!); } catch (Exception error) { Log("Layout recovery skipped: " + error.Message); }
                    }
                    RestoreSelections(root); RenderDock(); _restoring = false; RefreshConfiguration(); Log("Recovered local workspace. Export a backup before clearing browser data.");
                }
            }
        }
        catch (Exception error) { _restoring = false; Notify("Recovery could not be loaded: " + error.Message); }
        if (_platform.ExtensionBridge is { } bridge) bridge.MessageReceived += ExtensionMessage;
        _activeEditor?.FocusEditor();
    }
}
