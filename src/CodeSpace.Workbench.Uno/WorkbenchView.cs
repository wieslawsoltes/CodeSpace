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
public sealed class WorkbenchView : Grid, IDisposable
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
        RegisterCommands(); BuildSidebar(); BuildPanel();
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
                    var workspace = Workspace.Deserialize(stored.GetString()!); _restoring = true; ReplaceWorkspace(workspace);
                    if (root.TryGetProperty("layout", out var layout))
                    {
                        try { _layout.Restore(layout.GetString()!); } catch (Exception error) { Log("Layout recovery skipped: " + error.Message); }
                    }
                    _restoring = false; RenderDock(); Log("Recovered local workspace. Export a backup before clearing browser data.");
                }
            }
        }
        catch (Exception error) { _restoring = false; Notify("Recovery could not be loaded: " + error.Message); }
        if (_platform.ExtensionBridge is { } bridge) bridge.MessageReceived += ExtensionMessage;
        _activeEditor?.FocusEditor();
    }

    private UIElement BuildTitleBar()
    {
        var bar = new Grid { Background = WorkbenchColors.Brush("#181818"), BorderBrush = WorkbenchColors.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var menu = new StackPanel { Orientation = Orientation.Horizontal };
        var brand = WorkbenchColors.Text("⌘", 21, "#4daafc"); brand.Margin = new Thickness(13, 0, 12, 0); menu.Children.Add(brand);
        var menus = new Dictionary<string, string[]>
        {
            ["File"] = ["workbench.action.files.newUntitledFile", "workbench.action.files.openFile", "workbench.action.files.save", "codespace.exportWorkspace", "codespace.importWorkspace", "workbench.action.closeActiveEditor"],
            ["Edit"] = ["undo", "redo", "editor.action.clipboardCutAction", "editor.action.clipboardCopyAction", "editor.action.clipboardPasteAction", "actions.find", "editor.action.startFindReplaceAction"],
            ["Selection"] = ["editor.action.selectAll", "editor.action.addSelectionToNextFindMatch", "editor.action.copyLinesDownAction"],
            ["View"] = ["workbench.action.showCommands", "workbench.action.toggleSidebarVisibility", "workbench.action.togglePanel", "workbench.action.splitEditor", "editor.action.toggleMinimap", "editor.action.toggleRenderWhitespace"],
            ["Go"] = ["workbench.action.quickOpen", "workbench.action.gotoLine", "workbench.action.gotoSymbol"],
            ["Run"] = ["workbench.view.debug", "codespace.extensions.probe"],
            ["Terminal"] = ["workbench.action.terminal.toggleTerminal"],
            ["Help"] = ["codespace.help", "codespace.about"]
        };
        foreach (var entry in menus)
        {
            var button = new WorkbenchButton(entry.Key) { Height = 34, Padding = new Thickness(7, 0, 7, 0) };
            button.Click += (_, _) =>
            {
                var flyout = new MenuFlyout();
                foreach (var id in entry.Value)
                {
                    var command = _commands.All.FirstOrDefault(c => c.Id == id); if (command is null) continue;
                    var item = new MenuFlyoutItem { Text = command.Title + (command.Shortcut.Length > 0 ? "   " + command.Shortcut : "") };
                    item.Click += (_, _) => Execute(id); flyout.Items.Add(item);
                }
                flyout.ShowAt(button);
            };
            menu.Children.Add(button);
        }
        bar.Children.Add(menu);
        var search = new WorkbenchButton("⌕    CodeSpace", () => Execute("workbench.action.quickOpen"), "Search files (Ctrl+P)") { Height = 25, MaxWidth = 470, MinWidth = 110, Margin = new Thickness(24, 3, 20, 3), BorderThickness = new Thickness(1), BorderBrush = WorkbenchColors.Brush("#3c3c3c"), CornerRadius = new CornerRadius(5), Background = WorkbenchColors.Brush("#222222"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(search, "CommandCenter"); bar.Children.Add(search); SetColumn(search, 1);
        var layouts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
        layouts.Children.Add(new WorkbenchButton("▥", () => Execute("workbench.action.toggleSidebarVisibility"), "Toggle side bar")); layouts.Children.Add(new WorkbenchButton("▤", () => Execute("workbench.action.togglePanel"), "Toggle panel")); layouts.Children.Add(new WorkbenchButton("◫", () => Execute("workbench.action.splitEditor"), "Split editor"));
        bar.Children.Add(layouts); SetColumn(layouts, 2);
        SizeChanged += (_, _) => menu.Visibility = ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
        return bar;
    }
    private UIElement BuildStatusBar()
    {
        var bar = new Grid { Background = WorkbenchColors.Brush("#181818"), BorderBrush = WorkbenchColors.Border, BorderThickness = new Thickness(0, 1, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var remote = new WorkbenchButton("><", () => Notify("Local workspace. Remote development is not connected."), "Workspace location") { Width = 35, Background = WorkbenchColors.Accent, Padding = new Thickness(8, 0, 8, 0) }; bar.Children.Add(remote);
        _statusLeft.Margin = new Thickness(10, 0, 12, 0); bar.Children.Add(_statusLeft); SetColumn(_statusLeft, 1);
        _message.Margin = new Thickness(4, 0, 8, 0); bar.Children.Add(_message); SetColumn(_message, 2);
        _statusRight.Margin = new Thickness(8, 0, 14, 0); bar.Children.Add(_statusRight); SetColumn(_statusRight, 3); return bar;
    }
    private void ApplyDimensions()
    {
        _main.ColumnDefinitions[1].Width = new GridLength(_layout.State.SideBarVisible ? _layout.State.SideBarWidth : 0);
        _main.ColumnDefinitions[2].Width = new GridLength(_layout.State.SideBarVisible ? 4 : 0);
        _sidebar.Visibility = _layout.State.SideBarVisible ? Visibility.Visible : Visibility.Collapsed;
        _center.RowDefinitions[1].Height = new GridLength(_layout.State.PanelVisible ? 4 : 0);
        _center.RowDefinitions[2].Height = new GridLength(_layout.State.PanelVisible ? _layout.State.PanelHeight : 0);
        _panel.Visibility = _layout.State.PanelVisible ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LayoutChanged(object? sender, EventArgs e)
    {
        if (_suppressLayout) return; ApplyDimensions(); if (!ReferenceEquals(_renderedRoot, _layout.State.Root)) RenderDock(); QueueRecovery();
    }
    private void RenderDock()
    {
        foreach (var editor in _editors) editor.Dispose(); _editors.Clear(); _tabHosts.Clear(); _activeEditor = null;
        _renderedRoot = _layout.State.Root; _editorHost.Child = BuildDockNode(_layout.State.Root);
        if (!_layout.Groups.Any(g => g.Id == _activeGroup)) _activeGroup = _layout.Groups.First().Id;
        _activeEditor ??= _editors.FirstOrDefault(); UpdateStatus();
        DispatcherQueue.TryEnqueue(() => _activeEditor?.FocusEditor());
    }
    private UIElement BuildDockNode(DockNode node)
    {
        if (node is SplitNode split)
        {
            var grid = new Grid(); var horizontal = split.Axis == SplitAxis.Horizontal;
            if (horizontal)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(split.Ratio, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            }
            else
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(split.Ratio, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(4) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1 - split.Ratio, GridUnitType.Star) });
            }
            var first = BuildDockNode(split.First); var second = BuildDockNode(split.Second); var splitter = new DockSplitter(horizontal); var ratio = split.Ratio;
            splitter.Delta += (_, delta) =>
            {
                ratio = Math.Clamp(ratio + delta / Math.Max(1, horizontal ? grid.ActualWidth : grid.ActualHeight), 0.1, 0.9);
                if (horizontal) { grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star); grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star); }
                else { grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star); grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star); }
            };
            splitter.PointerReleased += (_, _) => _layout.Resize(split.Id, ratio);
            grid.Children.Add(first); grid.Children.Add(splitter); grid.Children.Add(second);
            if (horizontal) { SetColumn(splitter, 1); SetColumn(second as FrameworkElement, 2); } else { SetRow(splitter, 1); SetRow(second as FrameworkElement, 2); }
            return grid;
        }
        var group = (TabGroup)node; var container = new Grid(); container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) }); container.RowDefinitions.Add(new RowDefinition());
        var tabs = new Border(); _tabHosts[group.Id] = tabs; tabs.Child = CreateTabs(group); container.Children.Add(tabs);
        var path = group.ActiveTab;
        var breadcrumb = WorkbenchColors.Text(path is null ? "No editor open" : "  " + path.Replace("/", "  ›  "), 12, "#969696"); breadcrumb.Margin = new Thickness(10, 0, 0, 0); container.Children.Add(breadcrumb); SetRow(breadcrumb, 1);
        if (path is not null && _workspace.Files.ContainsKey(path))
        {
            var session = SessionFor(path); var editor = new CodeEditorControl(session); editor.Viewport.FontSize = _fontSize; editor.Viewport.ShowMinimap = _minimap; editor.Viewport.ShowWhitespace = _whitespace;
            editor.CommandRequested += (_, command) => { _activeGroup = group.Id; _activeEditor = editor; Execute(command); };
            editor.Error += (_, error) => Notify(error);
            editor.CaretChanged += (_, _) => { if (ReferenceEquals(_activeEditor, editor)) UpdateStatus(); };
            editor.GotFocus += (_, _) => { _activeGroup = group.Id; _activeEditor = editor; _tree.SelectedPath = path; _tree.Invalidate(); UpdateStatus(); };
            container.Children.Add(editor); SetRow(editor, 2); _editors.Add(editor); if (group.Id == _activeGroup) _activeEditor = editor;
        }
        else
        {
            var welcome = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 12 };
            welcome.Children.Add(WorkbenchColors.Text("CodeSpace", 32, "#707070")); welcome.Children.Add(WorkbenchColors.Text("Open a file                       Ctrl+P", 13, "#777777")); welcome.Children.Add(WorkbenchColors.Text("Show all commands    Ctrl+Shift+P", 13, "#777777")); welcome.Children.Add(new WorkbenchButton("New Text File", () => Execute("workbench.action.files.newUntitledFile")));
            container.Children.Add(welcome); SetRow(welcome, 2);
        }
        return container;
    }
    private EditorTabs CreateTabs(TabGroup group)
    {
        var tabs = new EditorTabs(group, path => _sessions.TryGetValue(path, out var session) && session.IsDirty);
        tabs.Activated += (_, path) => { _activeGroup = group.Id; Open(path); };
        tabs.Closed += (_, path) => RunAsync(() => CloseAsync(path, group.Id));
        tabs.SplitRequested += (_, _) => { _activeGroup = group.Id; SplitEditor(); };
        tabs.Moved += (_, move) => { _activeGroup = move.TargetGroup; _layout.Move(move.Path, move.SourceGroup, move.TargetGroup, move.Index); };
        return tabs;
    }
    private void RefreshTabs()
    {
        foreach (var group in _layout.Groups) if (_tabHosts.TryGetValue(group.Id, out var host)) host.Child = CreateTabs(group);
    }
    private EditorSession SessionFor(string path)
    {
        if (_sessions.TryGetValue(path, out var existing)) return existing;
        var session = new EditorSession(_workspace.Files[path]); _sessions[path] = session;
        session.Changed += (_, _) => { QueueRecovery(); _refreshTimer.Stop(); _refreshTimer.Start(); QueueExtensionDocumentSync(); };
        return session;
    }
    public void Open(string path, int? line = null, int character = 0)
    {
        if (!_workspace.Files.ContainsKey(path)) { Notify("File not found: " + path); return; }
        if (!_layout.Groups.Any(g => g.Id == _activeGroup)) _activeGroup = _layout.Groups.First().Id;
        _layout.Open(path, _activeGroup); _tree.SelectedPath = path; _tree.Invalidate();
        if (line.HasValue) { var session = SessionFor(path); var offset = session.Buffer.OffsetAt(new(line.Value, character)); session.Select(offset, offset); }
    }
    private void SplitEditor()
    {
        var path = _layout.Groups.FirstOrDefault(g => g.Id == _activeGroup)?.ActiveTab;
        _suppressLayout = true; _activeGroup = _layout.Split(_activeGroup, SplitAxis.Horizontal, path); _suppressLayout = false; RenderDock(); QueueRecovery();
    }
    private async Task CloseAsync(string path, string group)
    {
        if (_sessions.TryGetValue(path, out var session) && session.IsDirty && _layout.Groups.Count(g => g.Tabs.Contains(path)) <= 1)
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Save changes to " + Path.GetFileName(path) + "?", Content = "Unsaved changes will remain in local recovery until replaced, but closing without saving is not an export.", PrimaryButtonText = "Save", SecondaryButtonText = "Close without export", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            var result = await dialog.ShowAsync(); if (result == ContentDialogResult.None) return; if (result == ContentDialogResult.Primary) await SaveFileAsync(session);
        }
        _layout.Close(path, group);
    }

    private void BuildSidebar()
    {
        var grid = new Grid { Background = WorkbenchColors.Sidebar }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) }); grid.RowDefinitions.Add(new RowDefinition());
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = WorkbenchColors.Text(_side switch { "files" => "EXPLORER", "search" => "SEARCH", "source" => "SOURCE CONTROL", "debug" => "RUN AND DEBUG", "extensions" => "EXTENSIONS", "settings" => "SETTINGS", _ => "WORKSPACE" }, 11); title.Margin = new Thickness(20, 0, 0, 0); header.Children.Add(title);
        var more = new WorkbenchButton("···", () => Execute("workbench.action.showCommands"), "More actions") { Margin = new Thickness(0, 0, 10, 0) }; header.Children.Add(more); SetColumn(more, 1); grid.Children.Add(header);
        UIElement content;
        switch (_side)
        {
            case "files":
                var explorer = new Grid(); explorer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) }); explorer.RowDefinitions.Add(new RowDefinition()); explorer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
                var folderHeader = new Grid(); folderHeader.ColumnDefinitions.Add(new ColumnDefinition()); folderHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var folderName = WorkbenchColors.Text("⌄  " + _workspace.Name.ToUpperInvariant(), 11); folderName.Margin = new Thickness(8, 0, 0, 0); folderHeader.Children.Add(folderName);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(new WorkbenchButton("+", () => Execute("workbench.action.files.newUntitledFile"), "New file")); buttons.Children.Add(new WorkbenchButton("↥", () => Execute("workbench.action.files.openFile"), "Import files")); folderHeader.Children.Add(buttons); SetColumn(buttons, 1); explorer.Children.Add(folderHeader);
                if (_tree.Parent is Panel oldPanel) oldPanel.Children.Remove(_tree); else if (_tree.Parent is Border oldBorder) oldBorder.Child = null;
                explorer.Children.Add(_tree); SetRow(_tree, 1);
                var outline = new WorkbenchButton("›  OUTLINE", () => Execute("workbench.action.gotoSymbol"), "Go to symbol") { HorizontalAlignment = HorizontalAlignment.Stretch }; explorer.Children.Add(outline); SetRow(outline, 2); content = explorer; break;
            case "search": content = BuildSearchSidebar(); break;
            case "source":
                content = SideStack(stack =>
                {
                    AddWrapped(stack, "Local changes", 16); AddWrapped(stack, "This view compares open documents with their last saved snapshot. It is not connected to a Git repository.");
                    foreach (var session in _sessions.Values.Where(s => s.IsDirty)) stack.Children.Add(new WorkbenchButton("M  " + session.File.Path, () => Open(session.File.Path)));
                    if (!_sessions.Values.Any(s => s.IsDirty)) AddWrapped(stack, "No unsaved documents.");
                    stack.Children.Add(new WorkbenchButton("Save active file", () => Execute("workbench.action.files.save")));
                }); break;
            case "debug": content = SideStack(stack => { AddWrapped(stack, "Run and Debug", 19); AddWrapped(stack, "A debug adapter is not connected. Gutter breakpoint markers are local editor markers, not active debugger breakpoints."); AddWrapped(stack, "LSP/DAP message framing is available as a reusable library. Launch configurations, process execution and debugger sessions require further implementation."); stack.Children.Add(new WorkbenchButton("Open extension compatibility probe", () => Execute("codespace.extensions.probe"))); }); break;
            case "extensions": content = SideStack(stack =>
                {
                    AddWrapped(stack, "Extensions", 18); AddWrapped(stack, "Install a local VSIX to inspect its manifest. Executable extensions require explicit trust and only the documented API subset is supported.");
                    stack.Children.Add(new WorkbenchButton("Install from VSIX…", () => Execute("workbench.extensions.action.installVSIX")) { Background = WorkbenchColors.Accent });
                    stack.Children.Add(new WorkbenchButton("Run bundled compatibility probe", () => Execute("codespace.extensions.probe")));
                    foreach (var package in _extensions.Values)
                    {
                        var manifest = package.Manifest; AddWrapped(stack, manifest.DisplayName + "  " + manifest.Version, 15); AddWrapped(stack, manifest.Description);
                        AddWrapped(stack, "Host: " + manifest.SelectHost(_platform.IsBrowser)); stack.Children.Add(new WorkbenchButton("Review and activate", () => RunAsync(() => ActivatePackageAsync(package))));
                    }
                    AddWrapped(stack, "No Microsoft Marketplace access. No claim of universal extension compatibility.");
                }); break;
            case "settings": content = SideStack(stack =>
                {
                    AddWrapped(stack, "Editor settings", 18); AddWrapped(stack, "These settings apply immediately to every visible editor.");
                    var minimap = new CheckBox { Content = "Show minimap", IsChecked = _minimap, Foreground = WorkbenchColors.Foreground }; minimap.Click += (_, _) => { _minimap = minimap.IsChecked == true; ApplyEditorSettings(); }; stack.Children.Add(minimap);
                    var whitespace = new CheckBox { Content = "Render whitespace", IsChecked = _whitespace, Foreground = WorkbenchColors.Foreground }; whitespace.Click += (_, _) => { _whitespace = whitespace.IsChecked == true; ApplyEditorSettings(); }; stack.Children.Add(whitespace);
                    AddWrapped(stack, "Font size"); var size = new Slider { Minimum = 9, Maximum = 32, Value = _fontSize, StepFrequency = 1 }; size.ValueChanged += (_, e) => { _fontSize = (float)e.NewValue; ApplyEditorSettings(); }; stack.Children.Add(size);
                    stack.Children.Add(new WorkbenchButton("Open settings.json", () => { if (!_workspace.Files.ContainsKey(".vscode/settings.json")) _workspace.Add(".vscode/settings.json", "{}\n"); Open(".vscode/settings.json"); }));
                    AddWrapped(stack, "The JSON file is editable workspace content; automatic settings-file binding is not yet implemented.");
                }); break;
            default: content = SideStack(stack => { AddWrapped(stack, "Local-first workspace", 19); AddWrapped(stack, _platform.IsBrowser ? "Your files stay in this browser unless you export them or explicitly execute an extension. Browser storage is not a backup." : "Files are imported into the workbench. Saving uses an explicit file dialog. No folder watcher is connected."); stack.Children.Add(new WorkbenchButton("Export workspace…", () => Execute("codespace.exportWorkspace"))); AddWrapped(stack, "CodeSpace is independent software, not Microsoft Visual Studio Code or GitHub Codespaces."); }); break;
        }
        grid.Children.Add(content); SetRow(content as FrameworkElement, 1); _sidebar.Child = grid;
    }
    private static UIElement SideStack(Action<StackPanel> populate)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 8, 12, 12), Spacing = 10 }; populate(stack); return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private static void AddWrapped(StackPanel stack, string text, double size = 12)
    {
        stack.Children.Add(new TextBlock { Text = text, FontSize = size, Foreground = WorkbenchColors.Foreground, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.6 });
    }
    private UIElement BuildSearchSidebar()
    {
        var stack = new StackPanel { Margin = new Thickness(14, 5, 12, 12), Spacing = 8 };
        var input = new TextBox { PlaceholderText = "Search workspace", FontSize = 13, Background = WorkbenchColors.Brush("#313131"), Foreground = WorkbenchColors.Foreground, BorderBrush = WorkbenchColors.Border, CornerRadius = new CornerRadius(0) };
        AutomationProperties.SetName(input, "Search workspace"); stack.Children.Add(input);
        var results = new StackPanel { Spacing = 2 }; var count = WorkbenchColors.Text("Enter a search term", 11, "#9d9d9d");
        void Search()
        {
            try
            {
                results.Children.Clear(); var hits = _workspace.Search(input.Text, limit: 300); count.Text = hits.Count + " results";
                foreach (var hit in hits)
                {
                    var row = new StackPanel(); row.Children.Add(WorkbenchColors.Text(Path.GetFileName(hit.Path) + " : " + (hit.Line + 1), 12)); row.Children.Add(WorkbenchColors.Text(hit.Preview.Trim(), 11, "#969696"));
                    results.Children.Add(new WorkbenchButton(hit.Path, () => { Open(hit.Path, hit.Line, hit.Column); var session = SessionFor(hit.Path); var start = session.Buffer.OffsetAt(new(hit.Line, hit.Column)); session.Select(start, start + hit.Length); }, hit.Path) { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
                }
            }
            catch (Exception error) { count.Text = error.Message; }
        }
        input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Search(); e.Handled = true; } }; stack.Children.Add(new WorkbenchButton("Search", Search)); stack.Children.Add(count); stack.Children.Add(results);
        return new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private void ShowEntryActions(FileTreeEntry entry)
    {
        if (entry.IsFolder) { _quickPick.Prompt("New file in " + entry.Path, name => CreateFile(entry.Path + "/" + name)); return; }
        _quickPick.Show([
            new("Open " + entry.Name, entry.Path, "", () => Open(entry.Path)),
            new("Rename " + entry.Name, entry.Path, "", () => _quickPick.Prompt("New path", name => RenameFile(entry.Path, name), entry.Path)),
            new("Delete " + entry.Name, "Removes the file from this virtual workspace", "", () => RunAsync(() => DeleteFileAsync(entry.Path)))
        ], "File actions");
    }
    private void CreateFile(string path)
    {
        try { _workspace.Add(path, ""); Open(path); } catch (Exception error) { Notify(error.Message); }
    }
    private void RenameFile(string path, string destination)
    {
        try
        {
            var groups = _layout.Groups.Where(g => g.Tabs.Contains(path)).Select(g => g.Id).ToArray(); var file = _workspace.Rename(path, destination); _sessions.Remove(path);
            _suppressLayout = true; foreach (var id in groups) { _layout.Close(path, id); _layout.Open(file.Path, id); } _suppressLayout = false; RenderDock(); QueueRecovery();
        }
        catch (Exception error) { _suppressLayout = false; Notify(error.Message); }
    }
    private async Task DeleteFileAsync(string path)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete " + path + "?", Content = "This removes the file and its unsaved edits from the virtual workspace. Export a backup first.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _suppressLayout = true; foreach (var group in _layout.Groups.ToArray()) if (group.Tabs.Contains(path)) _layout.Close(path, group.Id); _workspace.Remove(path); _sessions.Remove(path); _suppressLayout = false; RenderDock(); QueueRecovery();
    }
    private void ApplyEditorSettings()
    {
        foreach (var editor in _editors) { editor.Viewport.FontSize = _fontSize; editor.Viewport.ShowMinimap = _minimap; editor.Viewport.ShowWhitespace = _whitespace; editor.InvalidateEditor(); }
        QueueRecovery();
    }

    private void BuildPanel()
    {
        var grid = new Grid { Background = WorkbenchColors.Background }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(33) }); grid.RowDefinitions.Add(new RowDefinition());
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var name in new[] { "PROBLEMS", "OUTPUT", "DEBUG CONSOLE", "TERMINAL" })
        {
            var button = new WorkbenchButton(name, () => { _panelTab = name; BuildPanel(); }) { Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(0, 0, 0, name == _panelTab ? 1 : 0), BorderBrush = WorkbenchColors.Accent };
            button.Content = WorkbenchColors.Text(name, 11, name == _panelTab ? "#ffffff" : "#9d9d9d"); tabs.Children.Add(button);
        }
        header.Children.Add(tabs); var close = new WorkbenchButton("×", () => _layout.SetPanel(visible: false), "Close panel"); header.Children.Add(close); SetColumn(close, 1); grid.Children.Add(header);
        if (_panelBody.Parent is ScrollViewer old) old.Content = null;
        _panelBody.Margin = new Thickness(20, 6, 15, 10); _panelBody.Spacing = 4;
        var scroll = new ScrollViewer { Content = _panelBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; grid.Children.Add(scroll); SetRow(scroll, 1); _panel.Child = grid; RefreshPanelBody();
    }
    private void RefreshPanelBody()
    {
        _panelBody.Children.Clear();
        if (_panelTab == "OUTPUT") { foreach (var line in _output.TakeLast(200)) _panelBody.Children.Add(WorkbenchColors.Text(line, 12, "#b5b5b5")); }
        else if (_panelTab == "PROBLEMS")
        {
            if (_activeEditor is null) { _panelBody.Children.Add(WorkbenchColors.Text("No active document.")); return; }
            var session = _activeEditor.Session; var diagnostics = LanguageServices.Diagnostics(session.Buffer, session.File.Path);
            _panelBody.Children.Add(WorkbenchColors.Text("Lexical bracket diagnostics · not compiler or language-server diagnostics", 11, "#969696"));
            foreach (var diagnostic in diagnostics.Take(200)) _panelBody.Children.Add(new WorkbenchButton("×  " + diagnostic.Message + "   " + Path.GetFileName(session.File.Path) + ":" + (diagnostic.Line + 1), () => Open(session.File.Path, diagnostic.Line, diagnostic.Character)));
            if (diagnostics.Count == 0) _panelBody.Children.Add(WorkbenchColors.Text("No unmatched brackets detected in the active document."));
        }
        else if (_panelTab == "DEBUG CONSOLE") _panelBody.Children.Add(WorkbenchColors.Text("No debug session. A debug adapter has not been connected.", 12, "#969696"));
        else
        {
            _panelBody.Children.Add(WorkbenchColors.Text("WORKSPACE SHELL  ·  virtual files only; not an operating-system terminal", 11, "#969696"));
            foreach (var line in _shellOutput.TakeLast(100)) _panelBody.Children.Add(WorkbenchColors.Text(line, 12));
            var prompt = new Grid(); prompt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); prompt.ColumnDefinitions.Add(new ColumnDefinition());
            prompt.Children.Add(WorkbenchColors.Text("workspace $ ", 12, "#65ad80"));
            var input = new TextBox { FontSize = 12, MinHeight = 24, Background = WorkbenchColors.Background, Foreground = WorkbenchColors.Foreground, BorderThickness = new Thickness(0), Padding = new Thickness(0), PlaceholderText = "help" }; AutomationProperties.SetName(input, "Workspace shell command"); prompt.Children.Add(input); SetColumn(input, 1);
            input.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { RunShell(input.Text); input.Text = ""; e.Handled = true; } }; _panelBody.Children.Add(prompt);
        }
    }
    private readonly List<string> _shellOutput = [];
    private void RunShell(string command)
    {
        _shellOutput.Add("workspace $ " + command); var parts = command.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries); if (parts.Length == 0) return;
        var arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "help": _shellOutput.Add("help · ls · pwd · cat <path> · open <path> · echo <text> · clear"); break;
            case "pwd": _shellOutput.Add("/" + _workspace.Name); break;
            case "ls": _shellOutput.AddRange(_workspace.Files.Keys); break;
            case "cat": if (_workspace.Files.TryGetValue(arg, out var file)) _shellOutput.AddRange(file.Buffer.ToString().Split('\n').Take(200)); else _shellOutput.Add("File not found: " + arg); break;
            case "open": Open(arg); break;
            case "echo": _shellOutput.Add(arg); break;
            case "clear": _shellOutput.Clear(); break;
            default: _shellOutput.Add("Unsupported workspace command. This is not a process shell. Type help."); break;
        }
        RefreshPanelBody();
    }
    public void Log(string text) { _output.Add(text); if (_output.Count > 500) _output.RemoveAt(0); if (_panelTab == "OUTPUT") RefreshPanelBody(); }
    public void Notify(string text) { _message.Text = text; Log(text); StatusChanged?.Invoke(this, text); }
    private void UpdateStatus()
    {
        var session = _activeEditor?.Session;
        _statusLeft.Text = "◇  " + _workspace.Name.ToLowerInvariant() + "    " + (_platform.IsBrowser ? "Browser" : "Desktop");
        if (session is null) { _statusRight.Text = "UTF-8    CodeSpace"; return; }
        var position = session.Buffer.PositionAt(session.Primary.Active);
        _statusRight.Text = $"Ln {position.Line + 1}, Col {position.Character + 1}" + (session.Selections.Count > 1 ? $" ({session.Selections.Count} cursors)" : "") + $"     Spaces: {session.TabSize}     UTF-8     {(session.Eol == "\r\n" ? "CRLF" : "LF")}     {LanguageCatalog.ForPath(session.File.Path).DisplayName}";
    }
    private void RegisterCommands()
    {
        void Add(string id, string title, string shortcut, Action execute) => _commands.Register(new(id, title, shortcut, execute));
        Add("workbench.action.showCommands", "Show All Commands", "Ctrl+Shift+P", () => _quickPick.Show(_commands.All.Select(c => new QuickPickItem(c.Title, c.Id, c.Shortcut, () => Execute(c.Id))), "Type a command", ">"));
        Add("workbench.action.quickOpen", "Go to File…", "Ctrl+P", () => _quickPick.Show(_workspace.Files.Keys.Select(path => new QuickPickItem(path, path, "", () => Open(path))), "Search files by name"));
        Add("workbench.action.files.newUntitledFile", "New Text File…", "Ctrl+N", () => _quickPick.Prompt("New relative file path", CreateFile, "Untitled-" + _untitled++ + ".txt"));
        Add("workbench.action.files.openFile", "Open / Import Files…", "Ctrl+O", () => RunAsync(ImportFilesAsync));
        Add("workbench.action.files.save", "Save Active File…", "Ctrl+S", () => RunAsync(async () => { if (_activeEditor is not null) await SaveFileAsync(_activeEditor.Session); }));
        Add("workbench.action.files.saveAs", "Save Active File As…", "Ctrl+Shift+S", () => Execute("workbench.action.files.save"));
        Add("codespace.exportWorkspace", "Workspace: Export Backup…", "", () => RunAsync(async () => await _platform.SaveFileAsync(_workspace.Name + ".codespace.json", Encoding.UTF8.GetBytes(_workspace.Serialize()))));
        Add("codespace.importWorkspace", "Workspace: Import Backup…", "", () => RunAsync(ImportWorkspaceAsync));
        Add("workbench.action.closeActiveEditor", "Close Editor", "Ctrl+W", () => RunAsync(async () => { if (_activeEditor is not null) await CloseAsync(_activeEditor.Session.File.Path, _activeGroup); }));
        Add("undo", "Undo", "Ctrl+Z", () => _activeEditor?.Session.Undo()); Add("redo", "Redo", "Ctrl+Y", () => _activeEditor?.Session.Redo());
        Add("editor.action.selectAll", "Select All", "Ctrl+A", () => _activeEditor?.Session.SelectAll());
        Add("editor.action.clipboardCopyAction", "Copy", "Ctrl+C", () => RunAsync(async () => { if (_activeEditor is not null) await _activeEditor.CopyAsync(); }));
        Add("editor.action.clipboardCutAction", "Cut", "Ctrl+X", () => RunAsync(async () => { if (_activeEditor is not null) await _activeEditor.CopyAsync(true); }));
        Add("editor.action.clipboardPasteAction", "Paste", "Ctrl+V", () => RunAsync(async () => { if (_activeEditor is not null) await _activeEditor.PasteAsync(); }));
        Add("actions.find", "Find", "Ctrl+F", () => _activeEditor?.ShowFind()); Add("editor.action.startFindReplaceAction", "Replace", "Ctrl+H", () => _activeEditor?.ShowFind(true));
        Add("editor.action.addSelectionToNextFindMatch", "Add Selection to Next Find Match", "Ctrl+D", () => _activeEditor?.Session.AddNextOccurrence());
        Add("editor.action.copyLinesDownAction", "Duplicate Line Down", "", () => _activeEditor?.Session.DuplicateLine());
        Add("editor.action.commentLine", "Toggle Line Comment", "Ctrl+/", () => _activeEditor?.Session.ToggleLineComment());
        Add("workbench.action.toggleSidebarVisibility", "View: Toggle Primary Side Bar", "Ctrl+B", () => _layout.SetSideBar(visible: !_layout.State.SideBarVisible));
        Add("workbench.action.togglePanel", "View: Toggle Panel", "Ctrl+J", () => _layout.SetPanel(visible: !_layout.State.PanelVisible));
        Add("workbench.action.splitEditor", "View: Split Editor Right", "Ctrl+\\", SplitEditor);
        Add("workbench.action.splitEditorDown", "View: Split Editor Down", "", () => { var path = _activeEditor?.Session.File.Path; _suppressLayout = true; _activeGroup = _layout.Split(_activeGroup, SplitAxis.Vertical, path); _suppressLayout = false; RenderDock(); QueueRecovery(); });
        Add("editor.action.toggleMinimap", "View: Toggle Minimap", "", () => { _minimap = !_minimap; ApplyEditorSettings(); });
        Add("editor.action.toggleRenderWhitespace", "View: Toggle Render Whitespace", "", () => { _whitespace = !_whitespace; ApplyEditorSettings(); });
        Add("workbench.action.findInFiles", "Search: Find in Files", "Ctrl+Shift+F", () => ShowSide("search"));
        Add("workbench.view.explorer", "View: Explorer", "Ctrl+Shift+E", () => ShowSide("files")); Add("workbench.view.extensions", "View: Extensions", "Ctrl+Shift+X", () => ShowSide("extensions")); Add("workbench.view.debug", "View: Run and Debug", "", () => ShowSide("debug"));
        Add("workbench.action.openSettings", "Preferences: Open Settings", "", () => ShowSide("settings"));
        Add("workbench.action.gotoLine", "Go to Line/Column…", "Ctrl+G", () => _quickPick.Prompt("Line:column", value => { var parts = value.TrimStart(':').Split(':'); if (_activeEditor is not null && int.TryParse(parts[0], out var line)) { var column = parts.Length > 1 && int.TryParse(parts[1], out var col) ? col : 1; var offset = _activeEditor.Session.Buffer.OffsetAt(new(line - 1, column - 1)); _activeEditor.Session.Select(offset, offset); } }));
        Add("workbench.action.gotoSymbol", "Go to Symbol in Editor…", "", () => { if (_activeEditor is null) return; var session = _activeEditor.Session; _quickPick.Show(LanguageServices.Symbols(session.Buffer).Select(s => new QuickPickItem(s.Kind + "  " + s.Name, session.File.Path, ":" + (s.Line + 1), () => Open(session.File.Path, s.Line, s.Character))), "Search document symbols"); });
        Add("editor.action.triggerSuggest", "Editor: Suggest Words", "Ctrl+Space", ShowCompletions);
        Add("workbench.action.terminal.toggleTerminal", "Terminal: Open Workspace Shell", "", () => { _panelTab = "TERMINAL"; _layout.SetPanel(visible: true); BuildPanel(); });
        Add("workbench.action.problems.focus", "View: Problems", "", () => { _panelTab = "PROBLEMS"; _layout.SetPanel(visible: true); BuildPanel(); });
        Add("workbench.extensions.action.installVSIX", "Extensions: Install from VSIX…", "", () => RunAsync(InstallVsixAsync));
        Add("codespace.extensions.probe", "Extensions: Run bundled compatibility probe", "", () => RunAsync(RunExtensionProbeAsync));
        Add("codespace.help", "Help: Welcome", "", () => { if (_workspace.Files.ContainsKey("README.md")) Open("README.md"); else Notify("Ctrl+P opens files. Ctrl+Shift+P opens all commands. Use Workspace: Export Backup to preserve local work."); });
        Add("codespace.about", "Help: About CodeSpace", "", () => RunAsync(async () => { var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "CodeSpace 0.1.0", Content = "An independent Uno Platform workbench with custom GPU-backed editor components. Development preview: not a complete or pixel-exact Visual Studio Code clone. MIT licensed. No Microsoft Marketplace integration.", CloseButtonText = "Close" }; await dialog.ShowAsync(); }));
    }
    private void ShowCompletions()
    {
        if (_activeEditor is null) return; var session = _activeEditor.Session; var offset = session.Primary.Active; var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(session.Buffer[start - 1]) || session.Buffer[start - 1] is '_' or '$')) start--;
        var prefix = session.Buffer.Slice(start, offset - start); var replaceStart = start;
        _quickPick.Show(LanguageServices.Complete(session.Buffer, prefix, LanguageCatalog.ForPath(session.File.Path).Id).Select(word => new QuickPickItem(word, "Document word / keyword completion", "", () => session.Apply([new(replaceStart, offset - replaceStart, word)]))), "Document words and language keywords");
    }
    public void Execute(string id)
    {
        try { if (!_commands.Execute(id)) Notify("Command is not registered: " + id); } catch (Exception error) { Notify(error.Message); }
    }
    private void ShowSide(string side) { _side = side; _activity.Activate(side); _layout.SetSideBar(visible: true); BuildSidebar(); }
    private async void RunAsync(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { Notify("Operation cancelled."); } catch (Exception error) { Notify(error.Message); } }
    private void WorkbenchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _quickPick.Visibility == Visibility.Visible) return;
        if (e.Key == VirtualKey.F1) { Execute("workbench.action.showCommands"); e.Handled = true; return; }
        if (!KeyModifiers.Control) return;
        string? command = e.Key switch
        {
            VirtualKey.P => KeyModifiers.Shift ? "workbench.action.showCommands" : "workbench.action.quickOpen",
            VirtualKey.E when KeyModifiers.Shift => "workbench.view.explorer",
            VirtualKey.X when KeyModifiers.Shift => "workbench.view.extensions",
            VirtualKey.F when KeyModifiers.Shift => "workbench.action.findInFiles",
            VirtualKey.B => "workbench.action.toggleSidebarVisibility", VirtualKey.J => "workbench.action.togglePanel", _ => null
        };
        if (command is not null) { Execute(command); e.Handled = true; }
    }
    private async Task ImportFilesAsync()
    {
        foreach (var file in await _platform.PickFilesAsync())
        {
            if (file.Bytes.Length > 16 * 1024 * 1024) { Notify("File exceeds the 16 MiB import limit: " + file.Path); continue; }
            try
            {
                var text = new UTF8Encoding(false, true).GetString(file.Bytes); if (text.Contains('\0')) throw new FormatException("Binary file detected.");
                var path = Workspace.NormalizePath(file.Path); if (_workspace.Files.ContainsKey(path)) { var ext = Path.GetExtension(path); path = path[..^ext.Length] + "-import-" + DateTime.UtcNow.ToString("HHmmssfff") + ext; }
                _workspace.Add(path, text.TrimStart('\ufeff')); Open(path);
            }
            catch (Exception error) { Notify("Import skipped for " + file.Path + ": " + error.Message); }
        }
    }
    private async Task SaveFileAsync(EditorSession session)
    {
        await _platform.SaveFileAsync(Path.GetFileName(session.File.Path), Encoding.UTF8.GetBytes(session.Buffer.ToString())); session.MarkSaved(); RefreshTabs(); QueueRecovery(); Notify("Saved " + session.File.Path);
    }
    private async Task ImportWorkspaceAsync()
    {
        var files = await _platform.PickFilesAsync(".json"); if (files.Count == 0) return; var workspace = Workspace.Deserialize(Encoding.UTF8.GetString(files[0].Bytes));
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Replace the current workspace?", Content = "Export a backup first. Import replaces the current virtual files, open editors and local recovery.", PrimaryButtonText = "Replace workspace", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) { ReplaceWorkspace(workspace); QueueRecovery(); Notify("Imported workspace " + workspace.Name); }
    }
    private void ReplaceWorkspace(Workspace workspace)
    {
        _workspace.Changed -= WorkspaceChanged; _workspace = workspace; _workspace.Changed += WorkspaceChanged; _sessions.Clear(); _tree.Workspace = workspace;
        _suppressLayout = true; _layout.Reset(); _activeGroup = "primary"; if (workspace.Files.Count > 0) _layout.Open(workspace.Files.Keys.First()); _suppressLayout = false; RenderDock(); BuildSidebar();
    }
    private void WorkspaceChanged(object? sender, EventArgs e) { QueueRecovery(); QueueExtensionDocumentSync(); }
    private void QueueRecovery() { if (_restoring || _disposed) return; _recoveryTimer.Stop(); _recoveryTimer.Start(); }
    private async Task SaveRecoveryAsync()
    {
        try { await _platform.SaveRecoveryAsync(JsonSerializer.Serialize(new { workspace = _workspace.Serialize(), layout = _layout.Serialize(), fontSize = _fontSize, minimap = _minimap, whitespace = _whitespace })); }
        catch (Exception error) { Notify("Local recovery failed; export a backup: " + error.Message); }
    }

    private async Task InstallVsixAsync()
    {
        foreach (var file in await _platform.PickFilesAsync(".vsix"))
        {
            using var stream = new MemoryStream(file.Bytes); var package = ExtensionPackage.Read(stream); _extensions[package.Manifest.Id] = package;
            var report = ExtensionCompatibility.Inspect(package.Manifest, _platform.IsBrowser); Log("Inspected " + package.Manifest.Id + " — " + report.Host);
            foreach (var limit in report.Limitations) Log("  " + limit);
        }
        ShowSide("extensions");
    }
    private async Task ActivatePackageAsync(ExtensionPackage package)
    {
        var manifest = package.Manifest; var report = ExtensionCompatibility.Inspect(manifest, _platform.IsBrowser);
        if (report.Host == ExtensionHostKind.Unsupported) { Notify(string.Join(" ", report.Limitations)); return; }
        if (report.Host == ExtensionHostKind.Declarative) { Notify("Manifest inspected. Declarative theme/grammar/snippet contribution installation is not yet implemented."); return; }
        var bridge = _platform.ExtensionBridge;
        if (bridge is null || !bridge.IsAvailable) { Notify("No extension host is available on this platform. The VSIX was inspected but no code was executed."); return; }
        var trust = new ContentDialog { XamlRoot = XamlRoot, Title = "Trust and run " + manifest.DisplayName + "?", Content = "Extension code is executable and is not independently verified. A worker is not a security sandbox: an extension can read workspace data and access the network; a desktop Node host also has operating-system access. Install only code you trust. API compatibility is partial.\n\n" + string.Join("\n", report.Limitations), PrimaryButtonText = "Trust and run", CloseButtonText = "Cancel" };
        if (await trust.ShowAsync() != ContentDialogResult.Primary) return;
        await bridge.StartAsync(); await SyncExtensionDocumentsAsync();
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "activate", manifest = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(package.Files["package.json"])), files = package.TextFiles(), trusted = true }));
    }
    private async Task RunExtensionProbeAsync()
    {
        var bridge = _platform.ExtensionBridge;
        if (bridge is null || !bridge.IsAvailable) { Notify("The extension host has not been connected. No compatibility probe was executed."); return; }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Run the bundled extension probe?", Content = "This runs the repository's small MIT-licensed extension fixture using require('vscode'), activate(context), registerCommand and showInformationMessage. It tests only that API path, not arbitrary extensions.", PrimaryButtonText = "Run probe", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await bridge.StartAsync(); await SyncExtensionDocumentsAsync();
        var source = "const vscode = require('vscode'); exports.activate = context => { context.subscriptions.push(vscode.commands.registerCommand('codespace.hello', () => vscode.window.showInformationMessage('Hello from the VS Code API compatibility probe.'))); };";
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "activate", manifest = new { publisher = "codespace", name = "hello", version = "0.1.0", browser = "extension.js", main = "extension.js" }, files = new Dictionary<string, string> { ["extension.js"] = source }, trusted = true }));
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "execute", command = "codespace.hello", args = Array.Empty<object>() }));
    }
    private void QueueExtensionDocumentSync() { /* Synchronization is debounced with local recovery and explicit host requests. */ }
    private async Task SyncExtensionDocumentsAsync()
    {
        if (_platform.ExtensionBridge is not { } bridge) return;
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "workspace", name = _workspace.Name, documents = _workspace.Files.Values.Select(file => new { uri = "codespace:///" + file.Path, path = file.Path, text = file.Buffer.ToString(), languageId = LanguageCatalog.ForPath(file.Path).Id, version = _sessions.TryGetValue(file.Path, out var session) ? session.Version + 1 : 1 }), activeUri = _activeEditor is null ? null : "codespace:///" + _activeEditor.Session.File.Path }));
    }
    private void ExtensionMessage(object? sender, string json) => DispatcherQueue.TryEnqueue(() => RunAsync(() => HandleExtensionMessageAsync(json)));
    private async Task HandleExtensionMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement; var type = root.GetProperty("type").GetString();
        if (type == "registerCommand")
        {
            var id = root.GetProperty("command").GetString()!; _commands.Register(new(id, "Extension: " + id, "", () => RunAsync(async () => { if (_platform.ExtensionBridge is { } bridge) { await SyncExtensionDocumentsAsync(); await bridge.SendAsync(JsonSerializer.Serialize(new { type = "execute", command = id, args = Array.Empty<object>() })); } })));
        }
        else if (type is "log" or "error" or "activated" or "result") Notify("Extension host: " + (root.TryGetProperty("message", out var message) ? message.GetString() : root.ToString()));
        else if (type == "request")
        {
            var id = root.GetProperty("id").GetInt32(); object? result = null; string? error = null;
            try
            {
                var method = root.GetProperty("method").GetString(); var args = root.TryGetProperty("params", out var p) ? p : default;
                string PathArg() { var uri = args.GetProperty("uri").GetString()!; return Workspace.NormalizePath(new Uri(uri).AbsolutePath.TrimStart('/')); }
                switch (method)
                {
                    case "window.showInformationMessage": case "window.showWarningMessage": case "window.showErrorMessage": Notify(args.GetProperty("message").GetString() ?? ""); break;
                    case "window.showTextDocument": var path = PathArg(); Open(path); result = new { uri = "codespace:///" + path }; break;
                    case "workspace.readFile": result = Convert.ToBase64String(Encoding.UTF8.GetBytes(_workspace.Files[PathArg()].Buffer.ToString())); break;
                    case "workspace.writeFile":
                        var writePath = PathArg(); var text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(args.GetProperty("data").GetString()!));
                        if (_workspace.Files.ContainsKey(writePath)) SessionFor(writePath).Apply([new(0, _workspace.Files[writePath].Buffer.Length, text)]); else _workspace.Add(writePath, text);
                        result = true; break;
                    case "workspace.applyEdits":
                        var editPath = PathArg(); var session = SessionFor(editPath);
                        var edits = args.GetProperty("edits").EnumerateArray().Select(edit => new TextEdit(edit.GetProperty("start").GetInt32(), edit.GetProperty("length").GetInt32(), edit.GetProperty("text").GetString()!)).ToArray(); session.Apply(edits); result = true; break;
                    case "commands.executeCommand": var command = args.GetProperty("command").GetString()!; result = _commands.Execute(command); break;
                    case "window.createOutputChannel": case "window.appendOutput": Log(args.TryGetProperty("text", out var output) ? output.GetString() ?? "" : args.ToString()); break;
                    default: throw new NotSupportedException("Unimplemented extension bridge method: " + method);
                }
            }
            catch (Exception exception) { error = exception.Message; }
            if (_platform.ExtensionBridge is { } bridge) { await bridge.SendAsync(JsonSerializer.Serialize(new { type = "response", id, result, error })); await SyncExtensionDocumentsAsync(); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _recoveryTimer.Stop(); _refreshTimer.Stop(); _workspace.Changed -= WorkspaceChanged; _layout.Changed -= LayoutChanged; _tree.Workspace = null;
        foreach (var editor in _editors) editor.Dispose();
        if (_platform.ExtensionBridge is { } bridge) bridge.MessageReceived -= ExtensionMessage;
    }
}
