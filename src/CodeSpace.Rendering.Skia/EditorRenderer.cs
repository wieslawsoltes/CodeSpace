using System.Diagnostics;
using System.Globalization;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using SkiaSharp;

namespace CodeSpace.Rendering.Skia;

public sealed class EditorTheme
{
    public SKColor Background { get; set; } = SKColor.Parse("#1f1f1f");
    public SKColor Foreground { get; set; } = SKColor.Parse("#d4d4d4");
    public SKColor Gutter { get; set; } = SKColor.Parse("#858585");
    public SKColor ActiveLine { get; set; } = SKColor.Parse("#292929");
    public SKColor Selection { get; set; } = SKColor.Parse("#264f78");
    public SKColor Accent { get; set; } = SKColor.Parse("#0078d4");
    public SKColor Find { get; set; } = SKColor.Parse("#613e17");
    public Dictionary<TokenKind, SKColor> TokenColors { get; } = new()
    {
        [TokenKind.Keyword] = SKColor.Parse("#569cd6"), [TokenKind.Type] = SKColor.Parse("#4ec9b0"),
        [TokenKind.String] = SKColor.Parse("#ce9178"), [TokenKind.Number] = SKColor.Parse("#b5cea8"),
        [TokenKind.Comment] = SKColor.Parse("#6a9955"), [TokenKind.Function] = SKColor.Parse("#dcdcaa"),
        [TokenKind.Property] = SKColor.Parse("#9cdcfe"), [TokenKind.Heading] = SKColor.Parse("#569cd6"),
        [TokenKind.Tag] = SKColor.Parse("#569cd6"), [TokenKind.Operator] = SKColor.Parse("#d4d4d4"),
        [TokenKind.Punctuation] = SKColor.Parse("#ffd700")
    };
    public SKColor Color(TokenKind kind) => TokenColors.GetValueOrDefault(kind, Foreground);
}
public sealed class EditorViewport
{
    private double _scrollY, _scrollX;
    public event EventHandler? ViewChanged;
    public EditorViewport() => Folding.Changed += (_, _) => ViewChanged?.Invoke(this, EventArgs.Empty);
    public double ScrollY { get => _scrollY; set => SetScroll(ref _scrollY, value); }
    public double ScrollX { get => _scrollX; set => SetScroll(ref _scrollX, value); }
    private void SetScroll(ref double field, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        value = Math.Max(0, value); if (field == value) return;
        field = value; ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public float FontSize { get; set; } = 14;
    public float LineHeight => FontSize * 1.55f;
    public bool ShowMinimap { get; set; } = true;
    public bool ShowWhitespace { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    public bool CaretVisible { get; set; } = true;
    public bool Focused { get; set; }
    public float GutterWidth => ShowLineNumbers ? 66 : 16;
    public float MinimapWidth => ShowMinimap ? 80 : 0;
    public IReadOnlyList<TextRange> FindMatches { get; set; } = [];
    public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = [];
    public bool UseTextBlobs { get; set; } = true;
    public FoldingState Folding { get; } = new();
    public IReadOnlyDictionary<int, int> FoldRanges { get; set; } = new Dictionary<int, int>();
    public HashSet<int> Breakpoints { get; } = [];
}
public sealed record RenderMetrics(double CpuMilliseconds, int VisibleLines, int CachedLines, int GlyphsDrawn, int TextDrawCalls = 0, long LayoutBuilds = 0);

/// <summary>Draws into a caller-owned Skia canvas, sharing the Uno composition surface.</summary>
public sealed partial class EditorRenderer : IDisposable
{
    private readonly record struct Glyph(string Text, int Offset, int Length, float X, float Width);
    private sealed record TextBatch(SKTextBlob Blob, TokenKind Kind, float StartX, float EndX, int GlyphCount);
    private sealed record LineLayout(string Text, Glyph[] Glyphs, float Width, SyntaxToken[] Tokens, TextBatch[]? Batches) : IDisposable
    {
        public void Dispose() { if (Batches is not null) foreach (var batch in Batches) batch.Blob.Dispose(); }
    }
    private readonly Dictionary<int, LineLayout> _cache = [];
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private SKFont? _font;
    private float _lastSize;
    private int _lastTabSize;
    private readonly float[] _asciiWidths = new float[128];
    private readonly string[] _ascii = Enumerable.Range(0, 128).Select(i => ((char)i).ToString()).ToArray();
    private long _layoutBuilds;
    private int _textDrawCalls;
    private TextBuffer? _minimapBuffer;
    private float[] _minimap = [];
    private EditorSession? _session;
    private IncrementalSyntaxDocument? _syntax;
    public static SKTypeface? DefaultTypeface { get; set; }
    public EditorTheme Theme { get; set; } = new();
    public RenderMetrics Metrics { get; private set; } = new(0, 0, 0, 0);
    public string LanguageId => _syntax?.Language.Id ?? "plaintext";
    private void Bind(EditorSession session, EditorViewport view)
    {
        if (!ReferenceEquals(_session, session))
        {
            if (_session is not null) _session.Changed -= Changed;
            _session = session; _syntax = new IncrementalSyntaxDocument(session.File.Path); ClearCache(); session.Changed += Changed;
        }
        if (_font is null || _lastSize != view.FontSize || _lastTabSize != session.TabSize)
        {
            _font?.Dispose();
            _font = new SKFont(DefaultTypeface ?? SKTypeface.FromFamilyName("Cascadia Code") ?? SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default, view.FontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            _lastSize = view.FontSize; _lastTabSize = session.TabSize; ClearCache();
            for (var i = 32; i < 127; i++) _asciiWidths[i] = _font.MeasureText(_ascii[i]);
        }
    }
    private void Changed(object? sender, DocumentChangedEventArgs e)
    {
        var stable = e.PreviousBuffer is not null && e.Edits is { Count: > 0 } &&
            _syntax?.InvalidateEdits(e.PreviousBuffer, e.Edits) == true;
        if (!stable) _syntax?.Invalidate(e.FirstChangedLine);
        var last = stable ? e.Edits!.Max(edit => e.PreviousBuffer!.PositionAt(edit.Start + edit.Length).Line) : int.MaxValue;
        foreach (var key in _cache.Keys.Where(k => k >= e.FirstChangedLine && k <= last).ToArray())
        { _cache[key].Dispose(); _cache.Remove(key); }
        _minimapBuffer = null;
    }
    private void ClearCache() { foreach (var line in _cache.Values) line.Dispose(); _cache.Clear(); }
    public void Invalidate() { ClearCache(); _font?.Dispose(); _font = null; }
    private LineLayout Layout(EditorSession session, int line, EditorViewport view)
    {
        Bind(session, view);
        var tokens = _syntax!.GetLine(session.Buffer, line).Tokens;
        if (_cache.TryGetValue(line, out var cached))
        {
            if (ReferenceEquals(cached.Tokens, tokens)) return cached;
            cached.Dispose(); _cache.Remove(line);
        }
        var text = session.Buffer.GetLine(line).Text; var glyphs = new List<Glyph>(); var x = 0f;
        var space = Math.Max(1, _asciiWidths[32]); var tab = space * session.TabSize;
        var asciiOnly = true;
        for (var offset = 0; offset < text.Length;)
        {
            var ch = text[offset];
            var simple = ch < 128 && (offset + 1 == text.Length || text[offset + 1] < 128);
            var length = simple ? 1 : StringInfo.GetNextTextElementLength(text.AsSpan(offset));
            var glyph = simple ? _ascii[ch] : text.Substring(offset, length);
            asciiOnly &= simple;
            var width = glyph == "\t" ? tab - x % tab : Math.Max(space * 0.25f, simple && ch >= 32 && ch < 127 ? _asciiWidths[ch] : _font!.MeasureText(glyph));
            glyphs.Add(new(glyph, offset, length, x, width)); x += width; offset += length;
        }
        TextBatch[]? batches = null;
        if (asciiOnly && glyphs.Count > 0)
        {
            var list = new List<TextBatch>(); var tokenIndex = 0; var start = 0;
            while (start < glyphs.Count)
            {
                while (tokenIndex < tokens.Length - 1 && glyphs[start].Offset >= tokens[tokenIndex].Start + tokens[tokenIndex].Length) tokenIndex++;
                var kind = tokens.Length == 0 ? TokenKind.Plain : tokens[tokenIndex].Kind;
                var end = start + 1;
                while (end < glyphs.Count && end - start < 128 && (tokens.Length == 0 || glyphs[end].Offset < tokens[tokenIndex].Start + tokens[tokenIndex].Length)) end++;
                var chars = new char[end - start]; var positions = new SKPoint[chars.Length];
                for (var i = start; i < end; i++) { chars[i - start] = glyphs[i].Text == "\t" ? ' ' : glyphs[i].Text[0]; positions[i - start] = new(glyphs[i].X, 0); }
                var blob = SKTextBlob.CreatePositioned(chars.AsSpan(), _font!, positions);
                if (blob is not null) list.Add(new(blob, kind, glyphs[start].X, glyphs[end - 1].X + glyphs[end - 1].Width, end - start));
                start = end;
            }
            batches = list.ToArray();
        }
        var result = new LineLayout(text, glyphs.ToArray(), x, tokens, batches); _layoutBuilds++;
        if (_cache.Count >= 512)
        {
            var victim = _cache.Keys.MaxBy(k => Math.Abs((long)k - line)); _cache[victim].Dispose(); _cache.Remove(victim);
        }
        _cache[line] = result; return result;
    }
    private static float ColumnX(LineLayout line, int column)
    {
        if (column <= 0) return 0; if (column >= line.Text.Length) return line.Width;
        var lo = 0; var hi = line.Glyphs.Length;
        while (lo < hi) { var mid = (lo + hi) / 2; if (line.Glyphs[mid].Offset + line.Glyphs[mid].Length <= column) lo = mid + 1; else hi = mid; }
        return lo < line.Glyphs.Length ? line.Glyphs[lo].X : line.Width;
    }
    public int HitTest(EditorSession session, EditorViewport view, float x, float y)
    {
        Bind(session, view); var line = view.Folding.RowToLine((int)((y + view.ScrollY - 8) / view.LineHeight), session.Buffer.LineCount);
        var layout = Layout(session, line, view); var local = x - view.GutterWidth + (float)view.ScrollX;
        var lo = 0; var hi = layout.Glyphs.Length;
        while (lo < hi) { var mid = (lo + hi) / 2; var glyph = layout.Glyphs[mid]; if (local >= glyph.X + glyph.Width / 2) lo = mid + 1; else hi = mid; }
        return session.Buffer.GetLineStart(line) + (lo < layout.Glyphs.Length ? layout.Glyphs[lo].Offset : layout.Text.Length);
    }
    public void EnsureCaretVisible(EditorSession session, EditorViewport view, float width, float height)
    {
        Bind(session, view); var position = session.Buffer.PositionAt(session.Primary.Active); view.Folding.Reveal(position.Line); var y = view.Folding.LineToRow(position.Line, session.Buffer.LineCount) * view.LineHeight;
        if (y < view.ScrollY) view.ScrollY = y;
        else if (y + view.LineHeight > view.ScrollY + height - 20) view.ScrollY = Math.Max(0, y + view.LineHeight - height + 20);
        var x = ColumnX(Layout(session, position.Line, view), position.Character); var available = Math.Max(40, width - view.GutterWidth - view.MinimapWidth - 20);
        if (x < view.ScrollX) view.ScrollX = x; else if (x > view.ScrollX + available) view.ScrollX = x - available;
    }
    private void Fill(SKCanvas canvas, SKRect rect, SKColor color) { _paint.Color = color; canvas.DrawRect(rect, _paint); }
    private void Text(SKCanvas canvas, string text, float x, float y, SKColor color) { _paint.Color = color; _textDrawCalls++; canvas.DrawText(text, x, y, SKTextAlign.Left, _font!, _paint); }
    public void Dispose() { if (_session is not null) _session.Changed -= Changed; _font?.Dispose(); _paint.Dispose(); ClearCache(); }
}
