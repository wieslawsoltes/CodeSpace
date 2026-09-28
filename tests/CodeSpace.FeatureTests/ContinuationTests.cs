using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Rendering.Skia;
using SkiaSharp;

internal static class ContinuationTests
{
    public static void Run(Action<string, Action> test, IDictionary<string, object> metrics)
    {
        static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}"); }
        static void CheckSyntax(IncrementalSyntaxDocument syntax, TextBuffer buffer)
        {
            var reference = new SyntaxDocument("a.cs");
            for (var line = 0; line < buffer.LineCount; line++)
            {
                var expected = reference.GetLine(buffer, line); var actual = syntax.GetLine(buffer, line);
                Equal(expected.EndState, actual.EndState);
                if (!expected.Tokens.AsSpan().SequenceEqual(actual.Tokens)) throw new Exception("Token mismatch on line " + line);
            }
        }
        test("single-line edit retains ten-thousand-line lexical suffix", () =>
        {
            var before = new TextBuffer(string.Concat(Enumerable.Repeat("public string Value;\n", 10_000)));
            var syntax = new IncrementalSyntaxDocument("a.cs"); var tail = syntax.GetLine(before, 9999); var count = syntax.LinesTokenized;
            var edits = new TextEdit[] { new(14, 5, "Name") }; var after = before.Apply(edits);
            Equal(true, syntax.InvalidateEdits(before, edits)); Equal(true, ReferenceEquals(tail, syntax.GetLine(after, 9999)));
            Equal(1L, syntax.LinesTokenized - count);
            metrics["incrementalLexing"] = new { cachedLines = 10000, linesRetokenized = syntax.LinesTokenized - count };
            CheckSyntax(syntax, after);
        });
        test("lexical state propagates through a changed multiline comment", () =>
        {
            var before = new TextBuffer("// start\nclass A\n{\n}\n*/\nvar n = 0;\n"); var syntax = new IncrementalSyntaxDocument("a.cs");
            syntax.GetLine(before, 6); var edits = new TextEdit[] { new(0, 2, "/*") }; var after = before.Apply(edits);
            syntax.InvalidateEdits(before, edits); Equal(TokenKind.Comment, syntax.GetLine(after, 1).Tokens[0].Kind); CheckSyntax(syntax, after);
        });
        test("several edits before repaint cannot skip dirty lexical state", () =>
        {
            var before = new TextBuffer("// start\nclass A\n{\n}\n*/\nvar n = 0;\n"); var syntax = new IncrementalSyntaxDocument("a.cs"); syntax.GetLine(before, 6);
            var edits = new TextEdit[] { new(0, 2, "/*") }; var after = before.Apply(edits); syntax.InvalidateEdits(before, edits);
            var more = new TextEdit[] { new(after.GetLineStart(5), 3, "const") }; var final = after.Apply(more); syntax.InvalidateEdits(after, more); CheckSyntax(syntax, final);
        });
        test("newline invalidation includes an earlier pending edit", () =>
        {
            var before = new TextBuffer("// start\nclass A\n{\n}\n*/\n"); var syntax = new IncrementalSyntaxDocument("a.cs"); syntax.GetLine(before, 5);
            var edits = new TextEdit[] { new(0, 2, "/*") }; var after = before.Apply(edits); syntax.InvalidateEdits(before, edits);
            var more = new TextEdit[] { new(after.GetLineStart(3), 0, "extra\n") }; var final = after.Apply(more); Equal(false, syntax.InvalidateEdits(after, more)); CheckSyntax(syntax, final);
        });
        test("incremental lexing matches fresh lexing through 300 randomized edits", () =>
        {
            var random = new Random(88213); var buffer = new TextBuffer(string.Concat(Enumerable.Repeat("class A { /* comment */ string x = \"value\"; }\n", 40)));
            var syntax = new IncrementalSyntaxDocument("a.cs"); CheckSyntax(syntax, buffer);
            string[] inserts = ["x", "/*", "*/", "\"", "\n", "", "//", " ` ", "\r\n"];
            for (var i = 0; i < 300; i++)
            {
                var start = random.Next(buffer.Length + 1); var edit = new TextEdit(start, random.Next(Math.Min(6, buffer.Length - start) + 1), inserts[random.Next(inserts.Length)]);
                var updated = buffer.Apply([edit]); syntax.InvalidateEdits(buffer, new[] { edit }); buffer = updated;
                if (i % 3 == 0) CheckSyntax(syntax, buffer);
            }
            CheckSyntax(syntax, buffer);
        });
        test("same-line typing rebuilds one rendered layout and preserves raster output", () =>
        {
            var session = new EditorSession(new("a.cs", string.Concat(Enumerable.Repeat("public string Value = \"text\";\n", 80))));
            using var renderer = new EditorRenderer(); var view = new EditorViewport { ShowMinimap = false, ShowLineNumbers = false };
            using var cached = new SKBitmap(1100, 700); using var canvas = new SKCanvas(cached); var bounds = new SKRect(0, 0, 1100, 700);
            renderer.Draw(canvas, bounds, session, view); var builds = renderer.Metrics.LayoutBuilds;
            session.Select(14, 14); session.Insert("New"); renderer.Draw(canvas, bounds, session, view);
            Equal(1L, renderer.Metrics.LayoutBuilds - builds);
            using var fresh = new EditorRenderer(); using var expected = new SKBitmap(1100, 700); using var expectedCanvas = new SKCanvas(expected);
            fresh.Draw(expectedCanvas, bounds, session, view);
            Equal(0, cached.Pixels.Zip(expected.Pixels).Count(pair => pair.First != pair.Second));
            var baselineBuilds = fresh.Metrics.LayoutBuilds;
            builds = renderer.Metrics.LayoutBuilds;
            for (var i = 0; i < 100; i++)
            {
                session.Insert("x"); renderer.Draw(canvas, bounds, session, view);
                fresh.Invalidate(); fresh.Draw(expectedCanvas, bounds, session, view);
            }
            Equal(100L, renderer.Metrics.LayoutBuilds - builds);
            metrics["typingLayoutReuse"] = new { edits = 100, retainedCacheBuilds = renderer.Metrics.LayoutBuilds - builds, fullInvalidationBuilds = fresh.Metrics.LayoutBuilds - baselineBuilds };
            session.Select(0, 0); session.Insert("/*"); renderer.Draw(canvas, bounds, session, view);
            using var commentReference = new EditorRenderer(); commentReference.Draw(expectedCanvas, bounds, session, view);
            Equal(0, cached.Pixels.Zip(expected.Pixels).Count(pair => pair.First != pair.Second));
            session.Undo(); renderer.Draw(canvas, bounds, session, view);
            using var undoReference = new EditorRenderer(); undoReference.Draw(expectedCanvas, bounds, session, view);
            Equal(0, cached.Pixels.Zip(expected.Pixels).Count(pair => pair.First != pair.Second));
        });
        test("viewport change events skip no-op scrolling and track folds", () =>
        {
            var view = new EditorViewport(); var changed = 0; view.ViewChanged += (_, _) => changed++;
            view.ScrollY = 42; view.ScrollY = 42; view.ScrollX = 20; view.Folding.Collapse(1, 4); view.Folding.Collapse(1, 4); view.Folding.Expand(1);
            Equal(4, changed);
            try { view.ScrollX = double.NaN; throw new Exception("NaN accepted"); } catch (ArgumentOutOfRangeException) { }
            Equal(20d, view.ScrollX);
        });
        test("saving a captured snapshot leaves later edits dirty", () =>
        {
            var session = new EditorSession(new("a.cs", "original")); session.Insert("saved "); var captured = session.Buffer;
            session.Insert("later "); session.MarkSaved(captured);
            Equal("saved original", session.File.SavedText); Equal(true, session.IsDirty); Equal(true, session.File.IsDirty);
            session.Undo(); Equal(false, session.IsDirty); Equal(false, session.File.IsDirty);
            session.Redo(); Equal(true, session.IsDirty); session.MarkSaved(); Equal(false, session.IsDirty);
        });
    }
}
