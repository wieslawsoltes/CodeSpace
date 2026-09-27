using System.Diagnostics;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using SkiaSharp;

namespace CodeSpace.Rendering.Skia;

public sealed partial class EditorRenderer
{
    public void Draw(SKCanvas canvas, SKRect bounds, EditorSession session, EditorViewport view)
    {
        var started = Stopwatch.GetTimestamp(); _textDrawCalls = 0; Bind(session, view); canvas.Save(); canvas.ClipRect(bounds); canvas.Translate(bounds.Left, bounds.Top);
        var width = bounds.Width; var height = bounds.Height; Fill(canvas, new(0, 0, width, height), Theme.Background);
        var rowCount = view.Folding.VisibleLineCount(session.Buffer.LineCount);
        view.ScrollY = Math.Clamp(view.ScrollY, 0, Math.Max(0, rowCount * view.LineHeight - height + view.LineHeight * 3));
        view.ScrollX = Math.Max(0, view.ScrollX);
        var first = Math.Clamp((int)(view.ScrollY / view.LineHeight), 0, rowCount - 1);
        var last = Math.Min(rowCount - 1, first + (int)(height / view.LineHeight) + 2);
        var activeLine = session.Buffer.PositionAt(session.Primary.Active).Line; var glyphCount = 0;
        var right = Math.Max(view.GutterWidth + 20, width - view.MinimapWidth - 12);
        for (var row = first; row <= last; row++)
        {
            var number = view.Folding.RowToLine(row, session.Buffer.LineCount);
            var y = 8 + row * view.LineHeight - (float)view.ScrollY; var line = session.Buffer.GetLineInfo(number); var layout = Layout(session, number, view);
            if (number == activeLine) Fill(canvas, new(0, y, right, y + view.LineHeight), Theme.ActiveLine);
            if (view.ShowLineNumbers) Text(canvas, (number + 1).ToString(), view.GutterWidth - 16 - _font!.MeasureText((number + 1).ToString()), y + view.FontSize + 2, number == activeLine ? Theme.Foreground : Theme.Gutter);
            if (view.Breakpoints.Contains(number)) { _paint.Color = SKColor.Parse("#e51400"); canvas.DrawCircle(10, y + view.LineHeight / 2, 4, _paint); }
            if (view.FoldRanges.ContainsKey(number)) Text(canvas, view.Folding.IsCollapsed(number) ? "›" : "⌄", view.GutterWidth - 13, y + view.FontSize + 2, Theme.Gutter);
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
            if (view.UseTextBlobs && layout.Batches is not null && !view.ShowWhitespace)
            {
                foreach (var batch in layout.Batches)
                {
                    var origin = view.GutterWidth - (float)view.ScrollX;
                    if (origin + batch.EndX < view.GutterWidth || origin + batch.StartX > right) continue;
                    _paint.Color = Theme.Color(batch.Kind); canvas.DrawText(batch.Blob, origin, y + view.FontSize + 2, _paint);
                    glyphCount += batch.GlyphCount; _textDrawCalls++;
                }
            }
            else
            {
                var tokenIndex = 0;
                foreach (var glyph in layout.Glyphs)
                {
                    var x = view.GutterWidth + glyph.X - (float)view.ScrollX; if (x + glyph.Width < view.GutterWidth) continue; if (x > right) break;
                    while (tokenIndex < layout.Tokens.Length - 1 && glyph.Offset >= layout.Tokens[tokenIndex].Start + layout.Tokens[tokenIndex].Length) tokenIndex++;
                    var kind = layout.Tokens.Length == 0 ? TokenKind.Plain : layout.Tokens[tokenIndex].Kind;
                    if (glyph.Text is not (" " or "\t")) { Text(canvas, glyph.Text, x, y + view.FontSize + 2, Theme.Color(kind)); glyphCount++; }
                    else if (view.ShowWhitespace) Text(canvas, glyph.Text == "\t" ? "→" : "·", x, y + view.FontSize + 2, Theme.Gutter);
                }
            }
            foreach (var diagnostic in view.Diagnostics)
            {
                if (diagnostic.Line != number) continue;
                var x1 = view.GutterWidth + ColumnX(layout, diagnostic.Character) - (float)view.ScrollX;
                var x2 = view.GutterWidth + ColumnX(layout, diagnostic.Character + diagnostic.Length) - (float)view.ScrollX;
                _paint.Color = SKColor.Parse(diagnostic.Severity switch { "error" => "#f14c4c", "warning" => "#cca700", "information" => "#3794ff", _ => "#8b8b8b" });
                var baseline = y + view.LineHeight - 2;
                for (var x = Math.Max(view.GutterWidth, x1); x < Math.Min(right, Math.Max(x1 + 4, x2)); x += 4)
                { canvas.DrawLine(x, baseline, x + 2, baseline - 2, _paint); canvas.DrawLine(x + 2, baseline - 2, x + 4, baseline, _paint); }
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
            var sampleCount = Math.Min(session.Buffer.LineCount, Math.Max(0, (int)(height / 2)));
            if (!ReferenceEquals(_minimapBuffer, session.Buffer) || _minimap.Length != sampleCount)
            {
                _minimapBuffer = session.Buffer; _minimap = new float[sampleCount];
                for (var i = 0; i < sampleCount; i++)
                {
                    var number = (int)((long)i * session.Buffer.LineCount / Math.Max(1, sampleCount));
                    _minimap[i] = Math.Min(65, session.Buffer.GetLineInfo(number).Length * 1.2f);
                }
            }
            for (var i = 0; i < sampleCount; i++) if (_minimap[i] > 0)
                Fill(canvas, new(left + 5, 8 + i * 2, left + 5 + _minimap[i], 9 + i * 2), Theme.Gutter.WithAlpha(90));
            var totalHeight = Math.Max(height, rowCount * view.LineHeight);
            var thumbTop = (float)(view.ScrollY / totalHeight * height); var thumbHeight = Math.Max(12, height / totalHeight * height);
            Fill(canvas, new(left, thumbTop, width - 10, thumbTop + thumbHeight), Theme.Foreground.WithAlpha(22));
        }
        var documentHeight = Math.Max(height, rowCount * view.LineHeight);
        var top = (float)(view.ScrollY / documentHeight * height); var size = Math.Max(20, height / documentHeight * height);
        Fill(canvas, new(width - 10, top, width - 2, Math.Min(height, top + size)), Theme.Gutter.WithAlpha(100));
        canvas.Restore(); Metrics = new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, last - first + 1, _cache.Count, glyphCount, _textDrawCalls, _layoutBuilds);
    }
}
