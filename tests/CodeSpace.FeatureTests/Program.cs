using System.Diagnostics;
using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Rendering.Skia;
using SkiaSharp;

var passed = 0; var metrics = new Dictionary<string, object>();
void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); Environment.ExitCode = 1; } }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected [{expected}], actual [{actual}]"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected exception"); }
Test("allocation-free line metadata matches text lines", () => {
    var buffer = new TextBuffer("abc\r\n\nlong line\n");
    for (var line = 0; line < buffer.LineCount; line++) { var info = buffer.GetLineInfo(line); var text = buffer.GetLine(line); Equal(text.Start, info.Start); Equal(text.Length, info.Length); Equal(text.EndIncludingBreak, info.EndIncludingBreak); }
});
Test("span copies cross rope leaf boundaries", () => {
    var text = string.Concat(Enumerable.Repeat("abcdef\n", 2000)); var buffer = new TextBuffer(text).Replace(3000, 100, "replacement"); text = text.Remove(3000, 100).Insert(3000, "replacement");
    var output = new char[4000]; buffer.CopyTo(1900, output); Equal(text.Substring(1900, output.Length), new string(output)); Throws(() => buffer.CopyTo(buffer.Length - 1, new char[3]));
});
Test("large-line navigation allocates no line strings", () => {
    var buffer = new TextBuffer(new string('x', 2_000_000)); var position = new TextPosition(0, 1_000_000); buffer.OffsetAt(position);
    var before = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
    for (var i = 0; i < 10000; i++) if (buffer.OffsetAt(position) != 1_000_000) throw new Exception();
    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds; var indexed = GC.GetAllocatedBytesForCurrentThread() - before;
    before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 10; i++) _ = buffer.GetLine(0); var materialized = GC.GetAllocatedBytesForCurrentThread() - before;
    metrics["lineNavigation"] = new { indexedOperations = 10000, indexedAllocatedBytes = indexed, indexedMilliseconds = elapsed, materializedBaselineOperations = 10, materializedBaselineAllocatedBytes = materialized };
    if (indexed > 1024) throw new Exception("Navigation materializes line strings.");
});
Test("session startup shares saved buffer without flattening", () => {
    var file = new WorkspaceFile("big.cs", new string('x', 2_000_000)); var before = GC.GetAllocatedBytesForCurrentThread(); var session = new EditorSession(file);
    if (GC.GetAllocatedBytesForCurrentThread() - before > 4096) throw new Exception("Session startup copied the document."); Equal(false, session.IsDirty);
});
Test("clean file rename remains clean", () => { var workspace = new Workspace(); workspace.Add("a", "text"); Equal(false, workspace.Rename("a", "b").IsDirty); });
Test("multi-file validation failure is atomic", () => {
    var a = new EditorSession(new("a", "abc")); var b = new EditorSession(new("b", "def"));
    Throws(() => WorkspaceEditTransaction.Apply([new(a, [new(0, 1, "A")]), new(b, [new(99, 1, "B")])]));
    Equal("abc", a.Buffer.ToString()); Equal("def", b.Buffer.ToString()); Equal(false, a.CanUndo); Equal(0L, a.Version);
});
Test("multi-file expected versions protect against stale edits", () => {
    var a = new EditorSession(new("a", "abc")); var b = new EditorSession(new("b", "def")); b.Insert("x");
    Throws(() => WorkspaceEditTransaction.Apply([new(a, [new(0, 1, "A")], 0), new(b, [new(0, 1, "B")], 0)])); Equal("abc", a.Buffer.ToString());
});
Test("multi-file observers see committed buffers and independent undo", () => {
    var a = new EditorSession(new("a", "abc")); var b = new EditorSession(new("b", "def"));
    a.Changed += (_, _) => Equal("Def", b.Buffer.ToString());
    Equal(2, WorkspaceEditTransaction.Apply([new(a, [new(0, 1, "A")]), new(b, [new(0, 1, "D")])]));
    Equal("Abc", a.Buffer.ToString()); a.Undo(); Equal("abc", a.Buffer.ToString()); b.Undo(); Equal("def", b.Buffer.ToString());
});
Test("duplicate document in workspace transaction rejects", () => {
    var a = new EditorSession(new("a", "abc")); Throws(() => WorkspaceEditTransaction.Apply([new(a, [new(0, 0, "x")]), new(a, [new(1, 0, "y")])])); Equal("abc", a.Buffer.ToString());
});
Test("rectangular selection is a single multi-cursor edit", () => {
    var a = new EditorSession(new("a", "abcd\nx\n1234")); a.SelectRectangle(new(0, 1), new(2, 3)); Equal(3, a.Selections.Count); a.Insert("!"); Equal("a!d\nx!\n1!4", a.Buffer.ToString()); a.Undo(); Equal("abcd\nx\n1234", a.Buffer.ToString());
});
Test("fold mapping preserves headers and handles nesting", () => {
    var folds = new FoldingState(); folds.Collapse(2, 8); folds.Collapse(4, 6); Equal(6, folds.VisibleLineCount(12)); Equal(9, folds.RowToLine(3, 12)); Equal(2, folds.LineToRow(6, 12));
    folds.Expand(2); Equal(10, folds.VisibleLineCount(12)); folds.Reveal(5); Equal(12, folds.VisibleLineCount(12));
});
Test("fold map scales with folds instead of million-line documents", () => {
    var folds = new FoldingState(); folds.Collapse(4, 999_999); var before = GC.GetAllocatedBytesForCurrentThread(); Equal(5, folds.VisibleLineCount(1_000_000));
    for (var i = 0; i < 5; i++) Equal(i, folds.RowToLine(i, 1_000_000));
    if (GC.GetAllocatedBytesForCurrentThread() - before > 4096) throw new Exception("Allocated per-document-line folding storage.");
});
Test("randomized folding agrees with explicit visible lines", () => {
    var random = new Random(438);
    for (var iteration = 0; iteration < 200; iteration++) {
        var folds = new FoldingState(); var hidden = new bool[300];
        for (var i = 0; i < 12; i++) { var start = random.Next(299); var end = random.Next(start + 1, 300); folds.Collapse(start, end); }
        foreach (var fold in folds.Regions) for (var i = fold.StartLine + 1; i <= fold.EndLine; i++) hidden[i] = true;
        var visible = Enumerable.Range(0, 300).Where(i => !hidden[i]).ToArray(); Equal(visible.Length, folds.VisibleLineCount(300));
        for (var i = 0; i < visible.Length; i++) { Equal(visible[i], folds.RowToLine(i, 300)); Equal(i, folds.LineToRow(visible[i], 300)); }
    }
});
Test("settings JSONC, precedence, validation and fallback", () => {
    var config = new EditorConfiguration(); config.SetUser("editor.tabSize", JsonSerializer.SerializeToElement(2));
    config.LoadWorkspace("{ // a comment\n \"editor.tabSize\": 8, \"editor.fontSize\": 100, }"); Equal(8, config.Options.TabSize); Equal(32f, config.Options.FontSize);
    Throws(() => config.LoadWorkspace("{")); Equal(8, config.Options.TabSize); config.LoadWorkspace("{}"); Equal(2, config.Options.TabSize);
    config.LoadWorkspace(config.WithWorkspaceValue("editor.minimap.enabled", JsonSerializer.SerializeToElement(false))); Equal(false, config.Options.Minimap);
});
Test("settings reject prototype-like keys and oversized input", () => { Throws(() => EditorConfiguration.Parse("{\"__proto__.x\":1}")); Throws(() => EditorConfiguration.Parse(new string(' ', 1024 * 1024 + 1))); });
Test("coalesced lexical punctuation retains nested folding ranges", () => { var ranges = LanguageServices.FoldingRanges(new TextBuffer("class A {{\n // test\n}}"), "a.cs"); Equal(2, ranges.Count); });
Test("block-comment opener is fully tokenized", () => { var line = SyntaxLexer.Tokenize("/* hi */", LanguageCatalog.ForId("csharp")); Equal(new SyntaxToken(0, 8, TokenKind.Comment), line.Tokens[0]); });
Test("cached batched rendering reduces calls and preserves raster pixels", () => {
    var text = string.Concat(Enumerable.Repeat("public static string Message(string name) => \"Hello, world!\"; // GPU text batches\n", 80));
    var session = new EditorSession(new("test.cs", text)); using var renderer = new EditorRenderer();
    var viewport = new EditorViewport { ShowMinimap = false, ShowLineNumbers = false, UseTextBlobs = false };
    using var reference = new SKBitmap(1100, 700); using var optimized = new SKBitmap(1100, 700);
    using var first = new SKCanvas(reference); using var second = new SKCanvas(optimized); var bounds = new SKRect(0, 0, 1100, 700);
    renderer.Draw(first, bounds, session, viewport); var scalarCalls = renderer.Metrics.TextDrawCalls;
    var started = Stopwatch.GetTimestamp(); for (var i = 0; i < 100; i++) renderer.Draw(first, bounds, session, viewport); var scalarMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    viewport.UseTextBlobs = true; renderer.Draw(second, bounds, session, viewport); var batchCalls = renderer.Metrics.TextDrawCalls; var builds = renderer.Metrics.LayoutBuilds;
    var before = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
    for (var i = 0; i < 100; i++) renderer.Draw(second, bounds, session, viewport);
    var batchMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds; var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(builds, renderer.Metrics.LayoutBuilds);
    var left = reference.Pixels; var right = optimized.Pixels; var differing = left.Zip(right).Count(pair => pair.First != pair.Second);
    metrics["rasterRendering"] = new { frames = 100, scalarCallsPerFrame = scalarCalls, batchCallsPerFrame = batchCalls, scalarMilliseconds = scalarMs, batchMilliseconds = batchMs, batchAllocatedBytes = allocated, differingPixels = differing, totalPixels = left.Length };
    if (batchCalls * 2 >= scalarCalls) throw new Exception("Expected at least a 2x text-call reduction.");
    if (differing > left.Length / 1000) throw new Exception($"Batched text changes too many reference pixels: {differing}");
});
Test("folded renderer hit testing maps visual rows to document lines", () => {
    var session = new EditorSession(new("a.cs", "a\nb\nc\nd\ne")); using var renderer = new EditorRenderer();
    var viewport = new EditorViewport(); viewport.Folding.Collapse(0, 2);
    Equal(6, renderer.HitTest(session, viewport, viewport.GutterWidth, 8 + viewport.LineHeight + 2));
});
var output = Environment.GetEnvironmentVariable("CODESPACE_METRICS_PATH") ?? "artifacts/performance.json";
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(new { testsPassed = passed, runner = Environment.OSVersion.ToString(), note = "CPU/raster measurements, not physical-GPU timings. Scalar reference and batched path run in the same process.", metrics }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(File.ReadAllText(output)); Console.WriteLine($"{passed} feature/performance tests passed; exit code {Environment.ExitCode}.");
