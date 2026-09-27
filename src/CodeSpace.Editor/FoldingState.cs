namespace CodeSpace.Editor;

public readonly record struct CollapsedRegion(int StartLine, int EndLine);

/// <summary>Nested line folding using O(fold-count) storage, not an array per document line.</summary>
public sealed class FoldingState
{
    private readonly SortedDictionary<int, int> _collapsed = [];
    private readonly List<(int Start, int End, int RemovedBefore)> _hidden = [];
    private int _lineCount = -1, _removed;
    public int Revision { get; private set; }
    public IEnumerable<CollapsedRegion> Regions => _collapsed.Select(p => new CollapsedRegion(p.Key, p.Value));
    public bool IsCollapsed(int startLine) => _collapsed.ContainsKey(startLine);
    public void Collapse(int startLine, int endLine)
    {
        if (startLine < 0 || endLine <= startLine) throw new ArgumentOutOfRangeException(nameof(endLine));
        _collapsed[startLine] = endLine; Invalidate();
    }
    public void Expand(int startLine) { if (_collapsed.Remove(startLine)) Invalidate(); }
    public void Clear() { if (_collapsed.Count == 0) return; _collapsed.Clear(); Invalidate(); }
    public void Reveal(int line)
    {
        var removed = false;
        foreach (var pair in _collapsed.Where(p => p.Key < line && p.Value >= line).ToArray()) removed |= _collapsed.Remove(pair.Key);
        if (removed) Invalidate();
    }
    private void Invalidate() { _lineCount = -1; Revision++; }
    private void Build(int lineCount)
    {
        if (lineCount < 1) throw new ArgumentOutOfRangeException(nameof(lineCount));
        if (_lineCount == lineCount) return;
        _lineCount = lineCount; _hidden.Clear(); _removed = 0;
        foreach (var region in _collapsed)
        {
            var start = region.Key + 1; var end = Math.Min(region.Value, lineCount - 1);
            if (start > end) continue;
            if (_hidden.Count > 0 && start <= _hidden[^1].End + 1)
            {
                var previous = _hidden[^1]; var added = Math.Max(0, end - previous.End);
                _hidden[^1] = (previous.Start, Math.Max(end, previous.End), previous.RemovedBefore); _removed += added;
            }
            else { _hidden.Add((start, end, _removed)); _removed += end - start + 1; }
        }
    }
    public int VisibleLineCount(int lineCount) { Build(lineCount); return lineCount - _removed; }
    public int LineToRow(int line, int lineCount)
    {
        Build(lineCount); line = Math.Clamp(line, 0, lineCount - 1);
        var lo = 0; var hi = _hidden.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (_hidden[mid].Start <= line) lo = mid + 1; else hi = mid; }
        if (lo == 0) return line;
        var region = _hidden[lo - 1];
        return line <= region.End ? region.Start - 1 - region.RemovedBefore : line - region.RemovedBefore - (region.End - region.Start + 1);
    }
    public int RowToLine(int row, int lineCount)
    {
        Build(lineCount); row = Math.Clamp(row, 0, lineCount - _removed - 1);
        var lo = 0; var hi = _hidden.Count;
        while (lo < hi) { var mid = (lo + hi) / 2; if (_hidden[mid].Start - _hidden[mid].RemovedBefore <= row) lo = mid + 1; else hi = mid; }
        if (lo == 0) return row;
        var region = _hidden[lo - 1]; return row + region.RemovedBefore + region.End - region.Start + 1;
    }
}
