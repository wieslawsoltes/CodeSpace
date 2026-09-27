using System.Text;
using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
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
        if (!_restoring) { _viewStates.Clear(); _viewKeys.Clear(); }
        _workspace.Changed -= WorkspaceChanged; _workspace = workspace; _workspace.Changed += WorkspaceChanged; _sessions.Clear(); _tree.Workspace = workspace;
        _suppressLayout = true; _layout.Reset(); _activeGroup = "primary"; if (workspace.Files.Count > 0) _layout.Open(workspace.Files.Keys.First()); _suppressLayout = false; RenderDock(); RefreshConfiguration(); BuildSidebar(); QueueDocumentSync();
    }
    private void WorkspaceChanged(object? sender, EventArgs e) { RefreshConfiguration(); QueueRecovery(); QueueExtensionDocumentSync(); }
    private void QueueRecovery() { if (_restoring || _disposed) return; _recoveryTimer.Stop(); _recoveryTimer.Start(); }
    private async Task SaveRecoveryAsync()
    {
        try { await _platform.SaveRecoveryAsync(JsonSerializer.Serialize(new { workspace = _workspace.Serialize(), layout = _layout.Serialize(), savedBaselines = _workspace.Files.Values.Where(f => f.IsDirty).ToDictionary(f => f.Path, f => f.SavedText), userSettings = _configuration.User, selections = _sessions.ToDictionary(p => p.Key, p => p.Value.Selections.ToArray()), views = CaptureViews(), schemaVersion = 2 })); }
        catch (Exception error) { Notify("Local recovery failed; export a backup: " + error.Message); }
    }
}
