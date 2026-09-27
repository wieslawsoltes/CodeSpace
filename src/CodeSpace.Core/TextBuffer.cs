using System.Text;

namespace CodeSpace.Core;

public readonly record struct TextPosition(int Line, int Character);
public readonly record struct TextRange(int Start, int Length)
{
    public int End => checked(Start + Length);
}
public readonly record struct TextEdit(int Start, int Length, string Text);
public readonly record struct TextLine(int Number, int Start, int Length, int EndIncludingBreak, string Text);

/// <summary>Persistent AVL rope. UTF-16 offsets, structurally shared snapshots, indexed line navigation.</summary>
public sealed class TextBuffer
{
    private const int ChunkSize = 2048;
    private sealed class Node
    {
        public readonly Node? Left, Right;
        public readonly string? Source;
        public readonly int Start, Length, Height, Breaks;
        public readonly int[]? Newlines;
        public bool IsLeaf => Source is not null;
        public Node(string source, int start, int length)
        {
            Source = source; Start = start; Length = length; Height = 1;
            var breaks = new List<int>();
            for (var i = 0; i < length; i++) if (source[start + i] == '\n') breaks.Add(i);
            Newlines = breaks.ToArray(); Breaks = Newlines.Length;
        }
        public Node(Node left, Node right)
        {
            Left = left; Right = right; Length = checked(left.Length + right.Length);
            Height = 1 + Math.Max(left.Height, right.Height); Breaks = left.Breaks + right.Breaks;
        }
    }
    private readonly Node? _root;
    public int Length => _root?.Length ?? 0;
    public int LineCount => (_root?.Breaks ?? 0) + 1;
    public int TreeHeight => _root?.Height ?? 0;
    public TextBuffer(string text = "") { ArgumentNullException.ThrowIfNull(text); _root = Build(text, 0, text.Length); }
    private TextBuffer(Node? root) => _root = root;
    private static Node? Build(string text, int start, int length)
    {
        if (length == 0) return null;
        if (length <= ChunkSize) return new Node(text, start, length);
        var mid = length / 2; return Join(Build(text, start, mid), Build(text, start + mid, length - mid));
    }
    private static int H(Node? node) => node?.Height ?? 0;
    private static Node? Join(Node? left, Node? right)
    {
        if (left is null) return right; if (right is null) return left;
        if (left.IsLeaf && right.IsLeaf && left.Source == right.Source && left.Start + left.Length == right.Start && left.Length + right.Length <= ChunkSize)
            return new Node(left.Source!, left.Start, left.Length + right.Length);
        if (left.Height > right.Height + 1) return Balance(new Node(left.Left!, Join(left.Right, right)!));
        if (right.Height > left.Height + 1) return Balance(new Node(Join(left, right.Left)!, right.Right!));
        return new Node(left, right);
    }
    private static Node Balance(Node node)
    {
        var left = node.Left!; var right = node.Right!;
        if (left.Height > right.Height + 1)
        {
            if (H(left.Left) >= H(left.Right)) return new Node(left.Left!, new Node(left.Right!, right));
            var middle = left.Right!; return new Node(new Node(left.Left!, middle.Left!), new Node(middle.Right!, right));
        }
        if (right.Height > left.Height + 1)
        {
            if (H(right.Right) >= H(right.Left)) return new Node(new Node(left, right.Left!), right.Right!);
            var middle = right.Left!; return new Node(new Node(left, middle.Left!), new Node(middle.Right!, right.Right!));
        }
        return node;
    }
    private static (Node? Left, Node? Right) Split(Node? node, int offset)
    {
        if (node is null) return (null, null);
        if (offset == 0) return (null, node); if (offset == node.Length) return (node, null);
        if (node.IsLeaf) return (new Node(node.Source!, node.Start, offset), new Node(node.Source!, node.Start + offset, node.Length - offset));
        if (offset < node.Left!.Length)
        {
            var pair = Split(node.Left, offset); return (pair.Left, Join(pair.Right, node.Right));
        }
        var other = Split(node.Right, offset - node.Left.Length); return (Join(node.Left, other.Left), other.Right);
    }
    public TextBuffer Replace(int start, int length, string text)
    {
        ValidateRange(start, length); ArgumentNullException.ThrowIfNull(text);
        if (length == 0 && text.Length == 0) return this;
        var first = Split(_root, start); var second = Split(first.Right, length);
        return new TextBuffer(Join(Join(first.Left, Build(text, 0, text.Length)), second.Right));
    }
    public TextBuffer Apply(IEnumerable<TextEdit> edits)
    {
        var ordered = edits.OrderBy(e => e.Start).ToArray();
        var end = -1;
        foreach (var edit in ordered)
        {
            ValidateRange(edit.Start, edit.Length); ArgumentNullException.ThrowIfNull(edit.Text);
            if (edit.Start < end) throw new ArgumentException("Edits must not overlap.", nameof(edits));
            end = edit.Start + edit.Length;
        }
        var result = this;
        for (var i = ordered.Length - 1; i >= 0; i--) result = result.Replace(ordered[i].Start, ordered[i].Length, ordered[i].Text);
        return result;
    }
    public char this[int offset]
    {
        get
        {
            if ((uint)offset >= (uint)Length) throw new ArgumentOutOfRangeException(nameof(offset));
            var node = _root!;
            while (!node.IsLeaf) { if (offset < node.Left!.Length) node = node.Left; else { offset -= node.Left.Length; node = node.Right!; } }
            return node.Source![node.Start + offset];
        }
    }
    public string Slice(int start, int length)
    {
        ValidateRange(start, length); var result = new StringBuilder(length); Append(_root, start, length, result); return result.ToString();
    }
    private static void Append(Node? node, int start, int length, StringBuilder output)
    {
        if (node is null || length == 0) return;
        if (node.IsLeaf) { output.Append(node.Source, node.Start + start, length); return; }
        var leftLength = node.Left!.Length;
        if (start < leftLength)
        {
            var count = Math.Min(length, leftLength - start); Append(node.Left, start, count, output);
            Append(node.Right, 0, length - count, output);
        }
        else Append(node.Right, start - leftLength, length, output);
    }
    private int BreakOffset(int index)
    {
        if (index < 0 || index >= (_root?.Breaks ?? 0)) throw new ArgumentOutOfRangeException(nameof(index));
        var node = _root!; var offset = 0;
        while (!node.IsLeaf)
        {
            if (index < node.Left!.Breaks) node = node.Left;
            else { index -= node.Left.Breaks; offset += node.Left.Length; node = node.Right!; }
        }
        return offset + node.Newlines![index];
    }
    private int BreaksBefore(int offset)
    {
        var node = _root; var count = 0;
        if (node is null) return 0;
        while (!node.IsLeaf)
        {
            if (offset < node.Left!.Length) node = node.Left;
            else { count += node.Left.Breaks; offset -= node.Left.Length; node = node.Right!; }
        }
        var i = Array.BinarySearch(node.Newlines!, offset);
        return count + (i >= 0 ? i : ~i);
    }
    public int GetLineStart(int line)
    {
        if ((uint)line >= (uint)LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        return line == 0 ? 0 : BreakOffset(line - 1) + 1;
    }
    public TextLine GetLine(int line)
    {
        var start = GetLineStart(line); var next = line + 1 < LineCount ? GetLineStart(line + 1) : Length;
        var end = next; if (end > start && this[end - 1] == '\n') end--; if (end > start && this[end - 1] == '\r') end--;
        return new TextLine(line, start, end - start, next, Slice(start, end - start));
    }
    public TextPosition PositionAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Length); var line = BreaksBefore(offset);
        return new TextPosition(line, offset - GetLineStart(line));
    }
    public int OffsetAt(TextPosition position)
    {
        var line = GetLine(Math.Clamp(position.Line, 0, LineCount - 1)); return line.Start + Math.Clamp(position.Character, 0, line.Length);
    }
    public override string ToString() => Slice(0, Length);
    private void ValidateRange(int start, int length)
    {
        if (start < 0 || length < 0 || start > Length - length) throw new ArgumentOutOfRangeException(nameof(start));
    }
}
