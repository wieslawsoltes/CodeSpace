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
    public double ScrollY { get; set; }
    public double ScrollX { get; set; }
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
    public HashSet<int> Breakpoints { get; } = [];
}
public sealed record RenderMetrics(double CpuMilliseconds, int VisibleLines, int CachedLines, int GlyphsDrawn);

/// <summary>Draws into a caller-owned Skia canvas. With Uno SKCanvasElement it uses the existing GPU composition surface without a bitmap upload.</summary>
public sealed class EditorRenderer : IDisposable
{
    private sealed record Glyph(string Text, int Offset, int Length, float X, float Width);
    private sealed record LineLayout(string Text, Glyph[] Glyphs, float Width, SyntaxToken[] Tokens);
    private readonly Dictionary<int, LineLayout> _cache = [];
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private SKFont? _font;
    private float _lastSize;
    private EditorSession? _session;
    private SyntaxDocument? _syntax;
    public static SKTypeface? DefaultTypeface { get; set; }
    public EditorTheme Theme { get; set; } = new();
    public RenderMetrics Metrics { get; private set; } = new(0, 0, 0, 0);
    public string LanguageId => _syntax?.Language.Id ?? "plaintext";
    private void Bind(EditorSession session, EditorViewport view)
    {
        if (!ReferenceEquals(_session, session))
        {
            if (_session is not null) _session.Changed -= Changed;
            _session = session; _syntax = new SyntaxDocument(session.File.Path); _cache.Clear(); session.Changed += Changed;
        }
        if (_font is null || _lastSize != view.FontSize)
        {
            _font?.Dispose();
            _font = new SKFont(DefaultTypeface ?? SKTypeface.FromFamilyName("Cascadia Code") ?? SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default, view.FontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            _lastSize = view.FontSize; _cache.Clear();
        }
    }
    private void Changed(object? sender, DocumentChangedEventArgs e)
    {
        _syntax?.Invalidate(e.FirstChangedLine);
        foreach (var key in _cache.Keys.Where(k => k >= e.FirstChangedLine).ToArray()) _cache.Remove(key);
    }
    public void Invalidate() { _cache.Clear(); _font?.Dispose(); _font = null; }
    private LineLayout Layout(EditorSession session, int line, EditorViewport view)
    {
        Bind(session, view); if (_cache.TryGetValue(line, out var cached)) return cached;
        var text = session.Buffer.GetLine(line).Text; var glyphs = new List<Glyph>(); var x = 0f;
        var space = Math.Max(1, _font!.MeasureText(" ")); var tab = space * session.TabSize;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var glyph = elements.GetTextElement(); var width = glyph == "\t" ? tab - x % tab : Math.Max(space * 0.25f, _font.MeasureText(glyph));
            glyphs.Add(new(glyph, elements.ElementIndex, glyph.Length, x, width)); x += width;
        }
        var result = new LineLayout(text, glyphs.ToArray(), x, _syntax!.GetLine(session.Buffer, line).Tokens);
        if (_cache.Count > 1200) _cache.Clear(); _cache[line] = result; return result;
    }
    private static float ColumnX(LineLayout line, int column)
    {
        if (column <= 0) return 0; if (column >= line.Text.Length) return line.Width;
        foreach (var glyph in line.Glyphs) if (column < glyph.Offset + glyph.Length) return glyph.X;
        return line.Width;
    }
    public int HitTest(EditorSession session, EditorViewport view, float x, float y)
    {
        Bind(session, view); var line = Math.Clamp((int)((y + view.ScrollY - 8) / view.LineHeight), 0, session.Buffer.LineCount - 1);
        var layout = Layout(session, line, view); var local = x - view.GutterWidth + (float)view.ScrollX;
        foreach (var glyph in layout.Glyphs) if (local < glyph.X + glyph.Width / 2) return session.Buffer.GetLineStart(line) + glyph.Offset;
        return session.Buffer.GetLineStart(line) + layout.Text.Length;
    }
    public void EnsureCaretVisible(EditorSession session, EditorViewport view, float width, float height)
    {
        Bind(session, view); var position = session.Buffer.PositionAt(session.Primary.Active); var y = position.Line * view.LineHeight;
        if (y < view.ScrollY) view.ScrollY = y;
        else if (y + view.LineHeight > view.ScrollY + height - 20) view.ScrollY = Math.Max(0, y + view.LineHeight - height + 20);
        var x = ColumnX(Layout(session, position.Line, view), position.Character); var available = Math.Max(40, width - view.GutterWidth - view.MinimapWidth - 20);
        if (x < view.ScrollX) view.ScrollX = x; else if (x > view.ScrollX + available) view.ScrollX = x - available;
    }
    public void Draw(SKCanvas canvas, SKRect bounds, EditorSession session, EditorViewport view)
    {
        var watch = Stopwatch.StartNew(); Bind(session, view); canvas.Save(); canvas.ClipRect(bounds); canvas.Translate(bounds.Left, bounds.Top);
        var width = bounds.Width; var height = bounds.Height; Fill(canvas, new(0, 0, width, height), Theme.Background);
        view.ScrollY = Math.Clamp(view.ScrollY, 0, Math.Max(0, session.Buffer.LineCount * view.LineHeight - height + view.LineHeight * 3));
        view.ScrollX = Math.Max(0, view.ScrollX);
        var first = Math.Clamp((int)(view.ScrollY / view.LineHeight), 0, session.Buffer.LineCount - 1);
        var last = Math.Min(session.Buffer.LineCount - 1, first + (int)(height / view.LineHeight) + 2);
        var activeLine = session.Buffer.PositionAt(session.Primary.Active).Line; var glyphCount = 0;
        var right = Math.Max(view.GutterWidth + 20, width - view.MinimapWidth - 12);
        for (var number = first; number <= last; number++)
        {
            var y = 8 + number * view.LineHeight - (float)view.ScrollY; var line = session.Buffer.GetLine(number); var layout = Layout(session, number, view);
            if (number == activeLine) Fill(canvas, new(0, y, right, y + view.LineHeight), Theme.ActiveLine);
            if (view.ShowLineNumbers) Text(canvas, (number + 1).ToString(), view.GutterWidth - 16 - _font!.MeasureText((number + 1).ToString()), y + view.FontSize + 2, number == activeLine ? Theme.Foreground : Theme.Gutter);
            if (view.Breakpoints.Contains(number)) { _paint.Color = SKColor.Parse("#e51400"); canvas.DrawCircle(10, y + view.LineHeight / 2, 4, _paint); }
            canvas.Save(); canvas.ClipRect(new(view.GutterWidth, y, right, y + view.LineHeight));
            foreach (var selection in session.Selections)
            {
                if (selection.Length == 0 || selection.End <= line.Start || selection.Start > line.Start + line.Length) continue;
                var a = Math.Max(0, selection.Start - line.Start); var b = Math.Min(line.Length, selection.End - line.Start);
                var x1 = view.GutterWidth + ColumnX(layout, a) - (float)view.ScrollX; var x2 = view.GutterWidth + ColumnX(layout, b) - (float)view.ScrollX;
                if (selection.End > line.Start + line.Length) x2 += view.FontSize * 0.6f;
                Fill(canvas, new(x1, y, Math.Max(x1 + 2, x2), y + view.LineHeight), Theme.Selection);
            }
            foreach (var match in view.FindMatches)
            {
                if (match.End <= line.Start || match.Start > line.Start + line.Length) continue;
                var a = Math.Max(0, match.Start - line.Start); var b = Math.Min(line.Length, match.End - line.Start);
                Fill(canvas, new(view.GutterWidth + ColumnX(layout, a) - (float)view.ScrollX, y, view.GutterWidth + ColumnX(layout, b) - (float)view.ScrollX, y + view.LineHeight), Theme.Find);
            }
            var tokenIndex = 0;
            foreach (var glyph in layout.Glyphs)
            {
                var x = view.GutterWidth + glyph.X - (float)view.ScrollX; if (x + glyph.Width < view.GutterWidth) continue; if (x > right) break;
                while (tokenIndex < layout.Tokens.Length - 1 && glyph.Offset >= layout.Tokens[tokenIndex].Start + layout.Tokens[tokenIndex].Length) tokenIndex++;
                var kind = layout.Tokens.Length == 0 ? TokenKind.Plain : layout.Tokens[tokenIndex].Kind;
                if (glyph.Text is not (" " or "\t")) { Text(canvas, glyph.Text, x, y + view.FontSize + 2, Theme.Color(kind)); glyphCount++; }
                else if (view.ShowWhitespace) Text(canvas, glyph.Text == "\t" ? "→" : "·", x, y + view.FontSize + 2, Theme.Gutter);
            }
            if (view.Focused && view.CaretVisible)
            {
                foreach (var selection in session.Selections)
                {
                    var position = session.Buffer.PositionAt(selection.Active); if (position.Line != number) continue;
                    var x = view.GutterWidth + ColumnX(layout, position.Character) - (float)view.ScrollX;
                    Fill(canvas, new(x, y + 1, x + 1.4f, y + view.LineHeight - 1), Theme.Foreground);
                }
            }
            canvas.Restore();
        }
        if (view.ShowMinimap && width > 240)
        {
            var left = width - view.MinimapWidth; Fill(canvas, new(left, 0, width, height), Theme.Background);
            var visibleRatio = height / Math.Max(height, session.Buffer.LineCount * 2f);
            var sampleCount = Math.Min(session.Buffer.LineCount, (int)(height / 2));
            for (var i = 0; i < sampleCount; i++)
            {
                var number = (int)((long)i * session.Buffer.LineCount / Math.Max(1, sampleCount));
                var line = session.Buffer.GetLine(number); var length = Math.Min(65, line.Length * 1.2f);
                if (length > 0) Fill(canvas, new(left + 5, 8 + i * 2, left + 5 + length, 9 + i * 2), Theme.Gutter.WithAlpha(90));
            }
            var totalHeight = Math.Max(height, session.Buffer.LineCount * view.LineHeight);
            var thumbTop = (float)(view.ScrollY / totalHeight * height); var thumbHeight = Math.Max(12, height / totalHeight * height);
            Fill(canvas, new(left, thumbTop, width - 10, thumbTop + thumbHeight), Theme.Foreground.WithAlpha(22));
        }
        var documentHeight = Math.Max(height, session.Buffer.LineCount * view.LineHeight);
        var top = (float)(view.ScrollY / documentHeight * height); var size = Math.Max(20, height / documentHeight * height);
        Fill(canvas, new(width - 10, top, width - 2, Math.Min(height, top + size)), Theme.Gutter.WithAlpha(100));
        canvas.Restore(); watch.Stop(); Metrics = new(watch.Elapsed.TotalMilliseconds, last - first + 1, _cache.Count, glyphCount);
    }
    private void Fill(SKCanvas canvas, SKRect rect, SKColor color) { _paint.Color = color; canvas.DrawRect(rect, _paint); }
    private void Text(SKCanvas canvas, string text, float x, float y, SKColor color) { _paint.Color = color; canvas.DrawText(text, x, y, SKTextAlign.Left, _font!, _paint); }
    public void Dispose() { if (_session is not null) _session.Changed -= Changed; _font?.Dispose(); _paint.Dispose(); _cache.Clear(); }
}
