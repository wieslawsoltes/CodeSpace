using CodeSpace.Core;
using CodeSpace.Docking;
using CodeSpace.Editor;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
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
        if (!_restoring) CaptureViews(); _viewKeys.Clear();
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
            var session = SessionFor(path); var editor = new CodeEditorControl(session); ConfigureEditor(editor);
            editor.CommandRequested += (_, command) => { _activeGroup = group.Id; _activeEditor = editor; Execute(command); };
            editor.Error += (_, error) => Notify(error);
            editor.CaretChanged += (_, _) => { if (ReferenceEquals(_activeEditor, editor)) UpdateStatus(); };
            editor.GotFocus += (_, _) => { _activeGroup = group.Id; _activeEditor = editor; _tree.SelectedPath = path; _tree.Invalidate(); UpdateStatus(); QueueDocumentSync(); };
            container.Children.Add(editor); SetRow(editor, 2); _editors.Add(editor); RestoreView(editor, group.Id); if (group.Id == _activeGroup) _activeEditor = editor;
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
        session.Changed += (_, _) => { if (path == ".vscode/settings.json") RefreshConfiguration(); QueueRecovery(); _refreshTimer.Stop(); _refreshTimer.Start(); QueueExtensionDocumentSync(); };
        session.SelectionChanged += (_, _) => QueueDocumentSync();
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
}
