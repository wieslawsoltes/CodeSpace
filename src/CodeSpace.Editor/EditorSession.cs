using System.Globalization;
using System.Text.RegularExpressions;
using CodeSpace.Core;

namespace CodeSpace.Editor;

public readonly record struct Selection(int Anchor, int Active)
{
    public int Start => Math.Min(Anchor, Active);
    public int End => Math.Max(Anchor, Active);
    public int Length => End - Start;
}
public sealed record EditorSnapshot(TextBuffer Buffer, Selection[] Selections);
public sealed class DocumentChangedEventArgs(int firstChangedLine, long version) : EventArgs
{
    public int FirstChangedLine { get; } = firstChangedLine;
    public long Version { get; } = version;
}
public enum CursorMove { Left, Right, Up, Down, Home, End, DocumentStart, DocumentEnd, PageUp, PageDown }

/// <summary>Undoable, multi-selection editing independent of rendering and input platforms.</summary>
public sealed class EditorSession
{
    private readonly List<EditorSnapshot> _undo = [];
    private readonly Stack<EditorSnapshot> _redo = new();
    private Selection[] _selections = [new(0, 0)];
    private TextBuffer _savedBuffer;
    private int? _preferredColumn;
    private int _tabSize = 4;
    public WorkspaceFile File { get; }
    public TextBuffer Buffer => File.Buffer;
    public IReadOnlyList<Selection> Selections => _selections;
    public Selection Primary => _selections[0];
    public long Version { get; private set; }
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public bool IsDirty => !ReferenceEquals(Buffer, _savedBuffer);
    public int TabSize { get => _tabSize; set => _tabSize = Math.Clamp(value, 1, 16); }
    public string Eol { get; }
    public event EventHandler<DocumentChangedEventArgs>? Changed;
    public event EventHandler? SelectionChanged;
    public EditorSession(WorkspaceFile file)
    {
        File = file; var text = file.Buffer.ToString();
        _savedBuffer = string.Equals(text, file.SavedText, StringComparison.Ordinal) ? file.Buffer : new TextBuffer(file.SavedText);
        Eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
    public void MarkSaved() { File.MarkSaved(); _savedBuffer = Buffer; SelectionChanged?.Invoke(this, EventArgs.Empty); }
    public void Select(int anchor, int active, bool add = false)
    {
        var selection = new Selection(Math.Clamp(anchor, 0, Buffer.Length), Math.Clamp(active, 0, Buffer.Length));
        _selections = add ? Normalize(_selections.Append(selection)) : [selection]; _preferredColumn = null; SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SelectAll() => Select(0, Buffer.Length);
    public void SelectWord(int offset)
    {
        offset = Math.Clamp(offset, 0, Buffer.Length); var start = offset; var end = offset;
        while (start > 0 && IsWord(Buffer[start - 1])) start--; while (end < Buffer.Length && IsWord(Buffer[end])) end++;
        Select(start, end);
    }
    public string SelectedText => string.Join(Eol, _selections.Select(s => Buffer.Slice(s.Start, s.Length)));
    private static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
    private static Selection[] Normalize(IEnumerable<Selection> selections)
    {
        var result = new List<Selection>();
        foreach (var selection in selections.OrderBy(s => s.Start).ThenBy(s => s.End))
        {
            if (result.Count > 0 && selection.Start <= result[^1].End)
            { var previous = result[^1]; result[^1] = new Selection(previous.Start, Math.Max(previous.End, selection.End)); }
            else result.Add(selection);
        }
        return result.Count == 0 ? [new(0, 0)] : result.ToArray();
    }
    private EditorSnapshot Snapshot() => new(Buffer, _selections.ToArray());
    private void BeforeEdit()
    {
        _undo.Add(Snapshot()); if (_undo.Count > 500) _undo.RemoveAt(0); _redo.Clear();
    }
    private void Notify(int firstLine)
    {
        // Dirty tracking is a structural snapshot comparison: never flatten the document on a keystroke.
        Version++; Changed?.Invoke(this, new DocumentChangedEventArgs(firstLine, Version)); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Insert(string text) => ReplaceSelections(_ => text);
    public void InsertNewLine() => ReplaceSelections(selection =>
    {
        var line = Buffer.GetLine(Buffer.PositionAt(selection.Start).Line);
        var leading = new string(line.Text.TakeWhile(c => c is ' ' or '\t').ToArray());
        var before = Buffer.Slice(line.Start, Math.Min(selection.Start - line.Start, line.Length)).TrimEnd();
        return Eol + leading + (before.EndsWith('{') || before.EndsWith(':') ? new string(' ', TabSize) : "");
    });
    private void ReplaceSelections(Func<Selection, string> replacement)
    {
        var selections = Normalize(_selections); var edits = selections.Select(s => new TextEdit(s.Start, s.Length, replacement(s))).ToArray(); Apply(edits);
    }
    public void Apply(IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderBy(e => e.Start).ToArray(); if (ordered.Length == 0) return;
        var updated = Buffer.Apply(ordered); // Validate the complete transaction before recording history.
        if (ReferenceEquals(updated, Buffer)) return;
        var firstLine = Buffer.PositionAt(ordered[0].Start).Line; BeforeEdit();
        var delta = 0; var cursors = new List<Selection>();
        foreach (var edit in ordered) { var end = edit.Start + delta + edit.Text.Length; cursors.Add(new Selection(end, end)); delta += edit.Text.Length - edit.Length; }
        File.Buffer = updated; _selections = Normalize(cursors); _preferredColumn = null; Notify(firstLine);
    }
    public void Delete(bool backward)
    {
        var expanded = Normalize(_selections.Select(s => s.Length != 0 ? s : backward ? new Selection(PreviousGrapheme(s.Active), s.Active) : new Selection(s.Active, NextGrapheme(s.Active))));
        // Preserve the original caret(s) in the undo snapshot, not the temporary deletion ranges.
        Apply(expanded.Where(s => s.Length != 0).Select(s => new TextEdit(s.Start, s.Length, "")));
    }
    public int PreviousGrapheme(int offset)
    {
        offset = Math.Clamp(offset, 0, Buffer.Length); if (offset <= 0) return 0;
        if (Buffer[offset - 1] is >= ' ' and <= '~' or '\t') return offset - 1;
        var position = Buffer.PositionAt(offset); var start = Buffer.GetLineStart(position.Line);
        if (offset == start) return offset >= 2 && Buffer[offset - 2] == '\r' ? offset - 2 : offset - 1;
        var text = Buffer.Slice(start, offset - start); var elements = StringInfo.ParseCombiningCharacters(text);
        return elements.Length == 0 ? start : start + elements[^1];
    }
    public int NextGrapheme(int offset)
    {
        offset = Math.Clamp(offset, 0, Buffer.Length); if (offset >= Buffer.Length) return Buffer.Length;
        if (Buffer[offset] == '\r' && offset + 1 < Buffer.Length && Buffer[offset + 1] == '\n') return offset + 2;
        if (Buffer[offset] == '\n') return offset + 1;
        if (Buffer[offset] <= 0x7f && (offset + 1 == Buffer.Length || Buffer[offset + 1] <= 0x7f)) return offset + 1;
        var line = Buffer.GetLine(Buffer.PositionAt(offset).Line); var remaining = Math.Max(1, line.Start + line.Length - offset);
        var text = Buffer.Slice(offset, Math.Min(remaining, Buffer.Length - offset));
        return offset + StringInfo.GetNextTextElementLength(text.AsSpan());
    }
    public void Move(CursorMove direction, bool extend = false, bool word = false, int pageSize = 30)
    {
        var primaryPosition = Buffer.PositionAt(Primary.Active);
        if (direction is CursorMove.Up or CursorMove.Down or CursorMove.PageUp or CursorMove.PageDown) _preferredColumn ??= primaryPosition.Character;
        else _preferredColumn = null;
        _selections = _selections.Select(selection =>
        {
            var offset = selection.Active; var pos = Buffer.PositionAt(offset);
            var target = direction switch
            {
                CursorMove.Left => !extend && selection.Length > 0 ? selection.Start : PreviousGrapheme(offset),
                CursorMove.Right => !extend && selection.Length > 0 ? selection.End : NextGrapheme(offset),
                CursorMove.Up => Buffer.OffsetAt(new(pos.Line - 1, _preferredColumn ?? pos.Character)),
                CursorMove.Down => Buffer.OffsetAt(new(pos.Line + 1, _preferredColumn ?? pos.Character)),
                CursorMove.PageUp => Buffer.OffsetAt(new(pos.Line - pageSize, _preferredColumn ?? pos.Character)),
                CursorMove.PageDown => Buffer.OffsetAt(new(pos.Line + pageSize, _preferredColumn ?? pos.Character)),
                CursorMove.Home => Buffer.GetLineStart(pos.Line),
                CursorMove.End => Buffer.GetLineStart(pos.Line) + Buffer.GetLine(pos.Line).Length,
                CursorMove.DocumentStart => 0,
                CursorMove.DocumentEnd => Buffer.Length,
                _ => offset
            };
            if (word && direction == CursorMove.Left) { while (target > 0 && char.IsWhiteSpace(Buffer[target])) target--; while (target > 0 && IsWord(Buffer[target - 1])) target--; }
            if (word && direction == CursorMove.Right) { while (target < Buffer.Length && IsWord(Buffer[target])) target++; while (target < Buffer.Length && char.IsWhiteSpace(Buffer[target])) target++; }
            return new Selection(extend ? selection.Anchor : target, target);
        }).ToArray();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Undo()
    {
        if (_undo.Count == 0) return; _redo.Push(Snapshot()); var snapshot = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        File.Buffer = snapshot.Buffer; _selections = snapshot.Selections; Notify(0);
    }
    public void Redo()
    {
        if (_redo.Count == 0) return; _undo.Add(Snapshot()); var snapshot = _redo.Pop(); File.Buffer = snapshot.Buffer; _selections = snapshot.Selections; Notify(0);
    }
    public IReadOnlyList<TextRange> Find(string query, bool matchCase = false, bool regex = false, bool wholeWord = false)
    {
        if (query.Length == 0) return [];
        var pattern = regex ? query : Regex.Escape(query); if (wholeWord) pattern = "\\b(?:" + pattern + ")\\b";
        var expression = new Regex(pattern, matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        return expression.Matches(Buffer.ToString()).Cast<Match>().Take(20000).Select(m => new TextRange(m.Index, m.Length)).ToArray();
    }
    public int ReplaceAll(string query, string replacement, bool matchCase = false, bool regex = false)
    {
        if (query.Length == 0) return 0;
        var expression = new Regex(regex ? query : Regex.Escape(query), matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        var matches = expression.Matches(Buffer.ToString()).Cast<Match>().ToArray();
        Apply(matches.Select(m => new TextEdit(m.Index, m.Length, regex ? m.Result(replacement) : replacement))); return matches.Length;
    }
    public void AddNextOccurrence()
    {
        if (Primary.Length == 0) { SelectWord(Primary.Active); return; }
        var text = Buffer.Slice(Primary.Start, Primary.Length);
        var matches = Find(text, true).Where(r => !_selections.Any(s => s.Start == r.Start && s.End == r.End)).ToArray();
        if (matches.Length == 0) return;
        var next = matches.FirstOrDefault(r => r.Start >= _selections.Max(s => s.End)); if (next.Length == 0) next = matches[0];
        Select(next.Start, next.End, true);
    }
    public void ToggleLineComment(string prefix = "//")
    {
        if (string.IsNullOrEmpty(prefix)) throw new ArgumentException("A nonempty comment prefix is required.", nameof(prefix));
        var start = Buffer.PositionAt(Primary.Start).Line; var end = Buffer.PositionAt(Primary.End).Line;
        if (Primary.Length > 0 && end > start && Primary.End == Buffer.GetLineStart(end)) end--;
        var lines = Enumerable.Range(start, end - start + 1).Select(Buffer.GetLine).ToArray();
        var remove = lines.All(l => l.Text.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        Apply(lines.Select(line =>
        {
            var indent = line.Text.Length - line.Text.TrimStart().Length; var offset = line.Start + indent;
            return remove ? new TextEdit(offset, prefix.Length + (line.Text.AsSpan(indent + prefix.Length).StartsWith(" ") ? 1 : 0), "") : new TextEdit(offset, 0, prefix + " ");
        }));
    }
    public void Indent(bool unindent)
    {
        if (!unindent && Primary.Length == 0) { Insert(new string(' ', TabSize)); return; }
        var start = Buffer.PositionAt(Primary.Start).Line; var end = Buffer.PositionAt(Primary.End).Line;
        if (Primary.Length > 0 && end > start && Primary.End == Buffer.GetLineStart(end)) end--;
        Apply(Enumerable.Range(start, end - start + 1).Select(i =>
        {
            var line = Buffer.GetLine(i); var count = line.Text.StartsWith('\t') ? 1 : line.Text.TakeWhile(c => c == ' ').Take(TabSize).Count();
            return new TextEdit(line.Start, unindent ? count : 0, unindent ? "" : new string(' ', TabSize));
        }));
    }
    public void DuplicateLine()
    {
        var line = Buffer.GetLine(Buffer.PositionAt(Primary.Active).Line);
        Apply([new TextEdit(line.Start, 0, line.Text + Eol)]);
    }
}
