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
    private void ReceiveDiagnostics(JsonElement root)
    {
        var owner = root.GetProperty("owner").GetString()!; var documents = new Dictionary<string, List<Diagnostic>>();
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var path = WorkspacePath(entry.GetProperty("uri").GetString()!); if (!_workspace.Files.TryGetValue(path, out var file)) continue;
            var list = new List<Diagnostic>();
            foreach (var item in entry.GetProperty("diagnostics").EnumerateArray().Take(1000))
            {
                var range = item.GetProperty("range"); var start = range.GetProperty("start"); var end = range.GetProperty("end");
                var line = start.GetProperty("line").GetInt32(); var column = start.GetProperty("character").GetInt32();
                if (line < 0 || line >= file.Buffer.LineCount) continue;
                var length = end.GetProperty("line").GetInt32() == line ? end.GetProperty("character").GetInt32() - column : file.Buffer.GetLineInfo(line).Length - column;
                list.Add(new(item.GetProperty("message").GetString()!, line, column, Math.Max(1, length), item.GetProperty("severity").GetInt32() switch { 0 => "error", 1 => "warning", 2 => "information", _ => "hint" }));
            }
            documents[path] = list;
        }
        if (documents.Count == 0) _extensionDiagnostics.Remove(owner); else _extensionDiagnostics[owner] = documents;
        foreach (var editor in _editors) { editor.Viewport.Diagnostics = ExtensionDiagnosticsFor(editor.Session.File.Path).ToArray(); editor.Redraw(); }
        if (_panelTab == "PROBLEMS") RefreshPanelBody();
    }
    private IEnumerable<Diagnostic> ExtensionDiagnosticsFor(string path) => _extensionDiagnostics.Values.Where(d => d.ContainsKey(path)).SelectMany(d => d[path]);
    private DocumentEditBatch ParseEditBatch(JsonElement args)
    {
        var session = SessionFor(WorkspacePath(args.GetProperty("uri").GetString()!));
        var version = args.TryGetProperty("version", out var value) ? value.GetInt64() - 1 : (long?)null;
        var edits = args.GetProperty("edits").EnumerateArray().Select(edit => new TextEdit(edit.GetProperty("start").GetInt32(), edit.GetProperty("length").GetInt32(), edit.GetProperty("text").GetString()!)).ToArray();
        return new(session, edits, version);
    }
    private async Task UpdateExtensionConfigurationAsync(JsonElement args)
    {
        var key = args.GetProperty("key").GetString()!;
        JsonElement? value = args.GetProperty("remove").GetBoolean() ? null : args.GetProperty("value").Clone();
        if (args.GetProperty("target").GetInt32() == 1) { _configuration.SetUser(key, value); RefreshConfiguration(); }
        else if (args.GetProperty("target").GetInt32() == 2) SetSettingsText(_configuration.WithWorkspaceValue(key, value));
        else throw new NotSupportedException("Only user and workspace settings are supported.");
        QueueRecovery(); await SyncConfigurationAsync();
    }
}
