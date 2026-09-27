using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private async Task SendDocumentChangesAsync()
    {
        if (!_extensionStarted || _platform.ExtensionBridge is not { } bridge) return;
        await _extensionSyncLock.WaitAsync();
        try
        {
            var next = _workspace.Files.Values.ToDictionary(f => f.Path, f => (Buffer: f.Buffer,
                Version: _sessions.TryGetValue(f.Path, out var session) ? session.Version + 1 : 1, Dirty: f.IsDirty));
            var changed = next.Where(pair => !_extensionDocuments.TryGetValue(pair.Key, out var old) || !ReferenceEquals(old.Buffer, pair.Value.Buffer) || old.Version != pair.Value.Version || old.Dirty != pair.Value.Dirty).ToArray();
            var payload = JsonSerializer.Serialize(new {
                type = _extensionSynced ? "workspaceDelta" : "workspace", name = _workspace.Name,
                documents = changed.Select(pair => new { uri = WorkspaceUri(pair.Key), path = pair.Key, text = pair.Value.Buffer.ToString(),
                    languageId = LanguageCatalog.ForPath(pair.Key).Id, version = pair.Value.Version, isDirty = pair.Value.Dirty }),
                deleted = _extensionDocuments.Keys.Except(next.Keys).Select(WorkspaceUri),
                selections = _sessions.Values.Select(s => new { uri = WorkspaceUri(s.File.Path), values = s.Selections.Select(v => new { anchor = v.Anchor, active = v.Active }) }),
                activeUri = _activeEditor is null ? null : WorkspaceUri(_activeEditor.Session.File.Path)
            });
            await bridge.SendAsync(payload);
            _extensionDocuments.Clear(); foreach (var pair in next) _extensionDocuments[pair.Key] = pair.Value;
            _extensionSynced = true;
        }
        finally { _extensionSyncLock.Release(); }
    }
    private async Task<JsonElement[]> RequestLanguageAsync(string kind, EditorSession session)
    {
        if (!_extensionStarted || _platform.ExtensionBridge is not { } bridge) return [];
        await SyncExtensionDocumentsAsync(); var id = ++_featureRequestId;
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); _featureRequests[id] = completion;
        try
        {
            var position = session.Buffer.PositionAt(session.Primary.Active);
            await bridge.SendAsync(JsonSerializer.Serialize(new { type = "featureRequest", id, kind, uri = WorkspaceUri(session.File.Path), version = session.Version + 1,
                position = new { line = position.Line, character = position.Character }, options = new { tabSize = session.TabSize, insertSpaces = true } }));
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
            if (result.GetProperty("version").GetInt64() != session.Version + 1) throw new InvalidOperationException("Document changed while the language feature was running.");
            return result.GetProperty("results").EnumerateArray().Select(r => r.Clone()).ToArray();
        }
        catch (TimeoutException)
        {
            await bridge.SendAsync(JsonSerializer.Serialize(new { type = "featureCancel", id }));
            throw new TimeoutException("The extension language provider timed out.");
        }
        finally { _featureRequests.Remove(id); }
    }
}
