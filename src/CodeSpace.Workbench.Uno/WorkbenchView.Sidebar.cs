using CodeSpace.Core;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
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
                    AddWrapped(stack, "Workspace settings are applied live. UI changes update settings.json; invalid JSON keeps the last valid configuration.");
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
    private void ApplyEditorSettings() => WriteEditorSettings();
}
