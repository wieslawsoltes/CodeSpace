using System.Text;
using CodeSpace.Core;
using CodeSpace.Docking;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
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
        Add("workbench.action.terminal.toggleTerminal", "Terminal: Open Workspace Shell", "", () => { _panelTab = "TERMINAL"; _layout.SetPanel(visible: true); BuildPanel(); });
        Add("workbench.action.problems.focus", "View: Problems", "", () => { _panelTab = "PROBLEMS"; _layout.SetPanel(visible: true); BuildPanel(); });
        Add("workbench.extensions.action.installVSIX", "Extensions: Install from VSIX…", "", () => RunAsync(InstallVsixAsync));
        Add("codespace.extensions.probe", "Extensions: Run bundled compatibility probe", "", () => RunAsync(RunExtensionProbeAsync));
        Add("codespace.help", "Help: Welcome", "", () => { if (_workspace.Files.ContainsKey("README.md")) Open("README.md"); else Notify("Ctrl+P opens files. Ctrl+Shift+P opens all commands. Use Workspace: Export Backup to preserve local work."); });
        Add("codespace.about", "Help: About CodeSpace", "", () => RunAsync(async () => { var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "CodeSpace 0.1.0", Content = "An independent Uno Platform workbench with custom GPU-backed editor components. Development preview: not a complete or pixel-exact Visual Studio Code clone. MIT licensed. No Microsoft Marketplace integration.", CloseButtonText = "Close" }; await dialog.ShowAsync(); }));
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
}
