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
    private static TextEdit FromRange(EditorSession session, JsonElement range, string text)
    {
        int Offset(JsonElement point)
        {
            var line = point.GetProperty("line").GetInt32(); var character = point.GetProperty("character").GetInt32();
            if (line < 0 || line >= session.Buffer.LineCount || character < 0 || character > session.Buffer.GetLineInfo(line).Length)
                throw new ArgumentOutOfRangeException(nameof(range), "Extension range is outside the document.");
            return session.Buffer.GetLineStart(line) + character;
        }
        var start = Offset(range.GetProperty("start")); var end = Offset(range.GetProperty("end"));
        if (end < start) throw new ArgumentException("Reversed extension edit range.");
        return new(start, end - start, text);
    }
    private async Task ShowCompletionsAsync()
    {
        if (_activeEditor is null) return; var session = _activeEditor.Session; var version = session.Version;
        var offset = session.Primary.Active; var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(session.Buffer[start - 1]) || session.Buffer[start - 1] is '_' or '$')) start--;
        var prefix = session.Buffer.Slice(start, offset - start); var replaceStart = start;
        var results = await RequestLanguageAsync("completion", session);
        if (session.Version != version) return;
        var items = new List<QuickPickItem>();
        foreach (var result in results)
        {
            var label = result.GetProperty("label").GetString()!;
            items.Add(new(label, result.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "Extension completion", "extension", () =>
            {
                if (session.Version != version) { Notify("Completion discarded because the document changed."); return; }
                var text = result.TryGetProperty("insertText", out var insert) && insert.ValueKind == JsonValueKind.String ? insert.GetString()! : label;
                var edit = result.TryGetProperty("textEdit", out var textEdit) && textEdit.ValueKind == JsonValueKind.Object
                    ? FromRange(session, textEdit.GetProperty("range"), textEdit.GetProperty("newText").GetString()!)
                    : result.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object ? FromRange(session, range, text) : new TextEdit(replaceStart, offset - replaceStart, text);
                var edits = new List<TextEdit> { edit };
                if (result.TryGetProperty("additionalTextEdits", out var additional)) edits.AddRange(additional.EnumerateArray().Select(e => FromRange(session, e.GetProperty("range"), e.GetProperty("newText").GetString()!)));
                session.Apply(edits);
            }));
        }
        items.AddRange(LanguageServices.Complete(session.Buffer, prefix, LanguageCatalog.ForPath(session.File.Path).Id).Select(word => new QuickPickItem(word, "Document word / keyword", "", () => {
            if (session.Version == version) session.Apply([new(replaceStart, offset - replaceStart, word)]);
        })));
        _quickPick.Show(items, "Completions from extensions and document words");
    }
    private async Task ShowHoverAsync()
    {
        if (_activeEditor is null) return;
        var results = await RequestLanguageAsync("hover", _activeEditor.Session);
        var text = string.Join("\n\n", results.SelectMany(r => r.GetProperty("contents").EnumerateArray().Select(c => c.GetString())));
        if (text.Length == 0) { Notify("No hover information from registered extensions."); return; }
        // Plain text, never executable HTML or trusted Markdown command links.
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Hover", Content = new ScrollViewer { MaxHeight = 400,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
    private async Task ShowDefinitionAsync()
    {
        if (_activeEditor is null) return; var results = await RequestLanguageAsync("definition", _activeEditor.Session);
        void OpenResult(JsonElement result) { var start = result.GetProperty("range").GetProperty("start"); Open(WorkspacePath(result.GetProperty("uri").GetString()!), start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32()); }
        if (results.Length == 1) OpenResult(results[0]);
        else if (results.Length > 1) _quickPick.Show(results.Select(r => new QuickPickItem(r.GetProperty("uri").GetString()!, "Definition", "", () => OpenResult(r))), "Definitions");
        else Notify("No definition from registered extensions.");
    }
    private async Task ShowSymbolsAsync()
    {
        if (_activeEditor is null) return; var session = _activeEditor.Session; var results = await RequestLanguageAsync("symbols", session);
        if (results.Length == 0) { _quickPick.Show(LanguageServices.Symbols(session.Buffer).Select(s => new QuickPickItem(s.Kind + "  " + s.Name, session.File.Path, ":" + (s.Line + 1), () => Open(session.File.Path, s.Line, s.Character))), "Document symbols"); return; }
        _quickPick.Show(results.Select(r => new QuickPickItem(r.GetProperty("name").GetString()!, "Extension symbol", "", () => {
            var start = r.GetProperty("range").GetProperty("start");
            Open(r.TryGetProperty("uri", out var uri) ? WorkspacePath(uri.GetString()!) : session.File.Path, start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32());
        })), "Extension document symbols");
    }
    private async Task FormatDocumentAsync()
    {
        if (_activeEditor is null) return; var session = _activeEditor.Session; var version = session.Version;
        var results = await RequestLanguageAsync("format", session);
        if (results.Length == 0) { Notify("No formatting edits from registered extensions."); return; }
        if (session.Version != version) throw new InvalidOperationException("Document changed before formatting could be applied.");
        session.Apply(results.Select(r => FromRange(session, r.GetProperty("range"), r.GetProperty("newText").GetString()!)));
        Notify("Applied extension formatting as one undoable edit.");
    }
}
