using CodeSpace.Core;

namespace CodeSpace.Languages;

/// <summary>
/// Stateful lexical cache that preserves unchanged suffixes after line-preserving edits.
/// Re-tokenization is lazy and stops when outgoing lexical state converges. Newline edits
/// and history replacement use conservative suffix invalidation.
/// </summary>
public sealed class IncrementalSyntaxDocument
{
    private readonly List<TokenizedLine> _lines = [];
    private int _dirtyFrom = int.MaxValue, _dirtyThrough = -1;
    public LanguageDefinition Language { get; private set; }
    public long LinesTokenized { get; private set; }
    public IncrementalSyntaxDocument(string path) => Language = LanguageCatalog.ForPath(path);
    public void SetLanguage(string id) { Language = LanguageCatalog.ForId(id); Invalidate(0); }

    public void Invalidate(int firstLine)
    {
        // Do not forget an earlier edit that has not been visited yet.
        firstLine = Math.Clamp(Math.Min(firstLine, _dirtyFrom), 0, _lines.Count);
        if (firstLine < _lines.Count) _lines.RemoveRange(firstLine, _lines.Count - firstLine);
        _dirtyFrom = int.MaxValue; _dirtyThrough = -1;
    }

    /// <returns>True when all cached line numbers remain stable.</returns>
    public bool InvalidateEdits(TextBuffer previous, IReadOnlyList<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0) return true;
        var first = int.MaxValue; var last = -1; var stable = true;
        foreach (var edit in edits)
        {
            if (edit.Start < 0 || edit.Length < 0 || edit.Start > previous.Length - edit.Length)
                throw new ArgumentOutOfRangeException(nameof(edits));
            ArgumentNullException.ThrowIfNull(edit.Text);
            var startLine = previous.PositionAt(edit.Start).Line;
            var endLine = previous.PositionAt(edit.Start + edit.Length).Line;
            first = Math.Min(first, startLine); last = Math.Max(last, endLine);
            stable &= startLine == endLine && !edit.Text.Contains('\n');
        }
        if (!stable) { Invalidate(first); return false; }
        if (first < _lines.Count)
        {
            _dirtyFrom = Math.Min(_dirtyFrom, first);
            _dirtyThrough = Math.Max(_dirtyThrough, Math.Min(last, _lines.Count - 1));
        }
        return true;
    }

    public TokenizedLine GetLine(TextBuffer buffer, int line)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (line < 0 || line >= buffer.LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        while (_dirtyFrom <= line && _dirtyFrom < _lines.Count)
        {
            var index = _dirtyFrom; var old = _lines[index];
            var state = index == 0 ? default : _lines[index - 1].EndState;
            var updated = Tokenize(buffer, index, state);
            // Preserve token identity so positioned glyph caches can keep unchanged lines.
            if (updated.EndState != old.EndState || !updated.Tokens.AsSpan().SequenceEqual(old.Tokens))
                _lines[index] = updated;
            _dirtyFrom = index + 1;
            if (index >= _dirtyThrough && updated.EndState == old.EndState)
            { _dirtyFrom = int.MaxValue; _dirtyThrough = -1; }
        }
        if (_dirtyFrom >= _lines.Count) { _dirtyFrom = int.MaxValue; _dirtyThrough = -1; }
        while (_lines.Count <= line)
        {
            var state = _lines.Count == 0 ? default : _lines[^1].EndState;
            _lines.Add(Tokenize(buffer, _lines.Count, state));
        }
        return _lines[line];
    }
    private TokenizedLine Tokenize(TextBuffer buffer, int line, LexerState state)
    { LinesTokenized++; return SyntaxLexer.Tokenize(buffer.GetLine(line).Text, Language, state); }
}
