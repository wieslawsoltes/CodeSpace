using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Extensions;

var passed = 0;
void Test(string name, Action action) { try { action(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); Environment.ExitCode = 1; } }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected [{expected}], actual [{actual}]"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected exception"); }
string Manifest(string extra = "") => "{\"name\":\"fixture\",\"publisher\":\"codespace\",\"version\":\"0.1.0\",\"engines\":{\"vscode\":\"^1.90.0\"}" + extra + "}";
MemoryStream Vsix(params (string Path, string Text)[] files)
{
    var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var file in files) { var entry = zip.CreateEntry(file.Path); using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)); writer.Write(file.Text); }
    stream.Position = 0; return stream;
}
Test("language detection", () => { Equal("csharp", LanguageCatalog.ForPath("src/File.CS").Id); Equal("xml", LanguageCatalog.ForPath("App.xaml").Id); Equal("plaintext", LanguageCatalog.ForPath("LICENSE").Id); });
Test("keyword and string tokens", () => { var line = SyntaxLexer.Tokenize("var name = \"class\"; // true", LanguageCatalog.ForId("csharp")); Equal(true, line.Tokens.Any(t => t.Kind == TokenKind.Keyword)); Equal(1, line.Tokens.Count(t => t.Kind == TokenKind.String)); Equal(1, line.Tokens.Count(t => t.Kind == TokenKind.Comment)); });
Test("multiline comment state", () => { var first = SyntaxLexer.Tokenize("/* open", LanguageCatalog.ForId("csharp")); Equal(true, first.EndState.BlockComment); var second = SyntaxLexer.Tokenize("close */ class A", LanguageCatalog.ForId("csharp"), first.EndState); Equal(false, second.EndState.BlockComment); Equal(true, second.Tokens.Any(t => t.Kind == TokenKind.Keyword)); });
Test("syntax invalidation", () => { var syntax = new SyntaxDocument("a.cs"); var buffer = new TextBuffer("/*\nclass A {}\n*/"); Equal(TokenKind.Comment, syntax.GetLine(buffer, 1).Tokens[0].Kind); buffer = buffer.Replace(0, 2, "//"); syntax.Invalidate(0); Equal(TokenKind.Keyword, syntax.GetLine(buffer, 1).Tokens[0].Kind); });
Test("diagnostics skip string braces", () => Equal(0, LanguageServices.Diagnostics(new TextBuffer("var a = \"{\"; // }"), "a.cs").Count));
Test("diagnostics detect unclosed braces", () => Equal(1, LanguageServices.Diagnostics(new TextBuffer("class A {"), "a.cs").Count));
Test("folding ranges", () => { var ranges = LanguageServices.FoldingRanges(new TextBuffer("class A {\n void F() {\n }\n}"), "a.cs"); Equal(2, ranges.Count); Equal(true, ranges.Any(r => r.StartLine == 0 && r.EndLine == 3)); });
Test("symbol extraction", () => Equal("Example", LanguageServices.Symbols(new TextBuffer("public class Example {}"))[0].Name));
Test("document keyword completion", () => Equal(true, LanguageServices.Complete(new TextBuffer("var widget = 1;"), "wid", "csharp").Contains("widget")));
Test("declarative host selection", () => Equal(ExtensionHostKind.Declarative, ExtensionManifest.Parse(Manifest()).SelectHost(true)));
Test("browser and node host selection", () => { var manifest = ExtensionManifest.Parse(Manifest(",\"browser\":\"index.js\",\"main\":\"node.js\"")); Equal(ExtensionHostKind.Browser, manifest.SelectHost(true)); Equal(ExtensionHostKind.Node, manifest.SelectHost(false)); });
Test("desktop-only extension rejected in browser", () => Equal(ExtensionHostKind.Unsupported, ExtensionManifest.Parse(Manifest(",\"main\":\"index.js\"")).SelectHost(true)));
Test("invalid extension publisher rejected", () => Throws(() => ExtensionManifest.Parse(Manifest().Replace("codespace", "../bad"))));
Test("valid VSIX reading", () => { using var stream = Vsix(("extension/package.json", Manifest(",\"browser\":\"./index.js\"")), ("extension/index.js", "exports.activate=()=>{};")); var package = ExtensionPackage.Read(stream); Equal("codespace.fixture", package.Manifest.Id); Equal(2, package.Files.Count); });
Test("VSIX path traversal rejected", () => { using var stream = Vsix(("extension/package.json", Manifest()), ("extension/../../escape.js", "bad")); Throws(() => ExtensionPackage.Read(stream)); });
Test("duplicate VSIX names rejected", () => { using var stream = Vsix(("extension/package.json", Manifest()), ("extension/PACKAGE.JSON", Manifest())); Throws(() => ExtensionPackage.Read(stream)); });
Test("missing extension entrypoint rejected", () => { using var stream = Vsix(("extension/package.json", Manifest(",\"browser\":\"missing.js\""))); Throws(() => ExtensionPackage.Read(stream)); });
Test("compatibility report does not overclaim", () => Equal(false, ExtensionCompatibility.Inspect(ExtensionManifest.Parse(Manifest()), true).ApiCompatibilityVerified));
Test("RPC unicode framing roundtrip", () => { using var stream = new MemoryStream(); JsonRpcFraming.WriteAsync(stream, new { method = "hello", text = "👩‍💻 Zażółć" }).GetAwaiter().GetResult(); stream.Position = 0; using var message = JsonRpcFraming.ReadAsync(stream).GetAwaiter().GetResult(); Equal("👩‍💻 Zażółć", message!.RootElement.GetProperty("text").GetString()); });
Test("RPC duplicate lengths rejected", () => { using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")); Throws(() => JsonRpcFraming.ReadAsync(stream).GetAwaiter().GetResult()); });
Test("RPC truncated body rejected", () => { using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 5\r\n\r\n{}")); Throws(() => JsonRpcFraming.ReadAsync(stream).GetAwaiter().GetResult()); });
Test("RPC size limit enforced", () => { using var stream = new MemoryStream(Encoding.ASCII.GetBytes("Content-Length: 1000\r\n\r\n")); Throws(() => JsonRpcFraming.ReadAsync(stream, 100).GetAwaiter().GetResult()); });
Test("delete undo restores original caret", () => { var editor = new EditorSession(new WorkspaceFile("a", "abc")); editor.Select(2, 2); editor.Delete(true); editor.Undo(); Equal(new Selection(2, 2), editor.Primary); Equal("abc", editor.Buffer.ToString()); });
Test("saved snapshot dirty tracking", () => { var editor = new EditorSession(new WorkspaceFile("a", "abc")); Equal(false, editor.IsDirty); editor.Insert("x"); Equal(true, editor.IsDirty); editor.Undo(); Equal(false, editor.IsDirty); editor.Redo(); editor.MarkSaved(); Equal(false, editor.IsDirty); editor.Undo(); Equal(true, editor.IsDirty); editor.Redo(); Equal(false, editor.IsDirty); });
Test("line selection does not comment next line", () => { var editor = new EditorSession(new WorkspaceFile("a", "one\ntwo")); editor.Select(0, 4); editor.ToggleLineComment(); Equal("// one\ntwo", editor.Buffer.ToString()); });
Test("large-document edit allocation gate", () =>
{
    var editor = new EditorSession(new WorkspaceFile("large.txt", new string('a', 2_000_000))); editor.Select(1_000_000, 1_000_000); editor.Insert("x");
    var before = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
    for (var i = 0; i < 100; i++) editor.Insert("x");
    watch.Stop(); var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"METRIC large-document 100 inserts: {allocated} bytes allocated, {watch.Elapsed.TotalMilliseconds:F2} ms (runner-specific; not a UI/GPU benchmark)");
    if (allocated > 16 * 1024 * 1024) throw new Exception("Small edits are flattening or copying the whole document.");
    Equal(2_000_101, editor.Buffer.Length);
});
Console.WriteLine($"\n{passed} protocol/language/performance tests passed; exit code {Environment.ExitCode}.");
