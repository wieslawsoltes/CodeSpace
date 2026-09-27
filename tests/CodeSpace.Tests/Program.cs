using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Docking;
using System.Diagnostics;

var passed = 0;
void Test(string name, Action test) { try { test(); Console.WriteLine($"PASS {name}"); passed++; } catch (Exception e) { Console.Error.WriteLine($"FAIL {name}: {e}"); Environment.ExitCode = 1; } }
void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected [{expected}], actual [{actual}]"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected exception"); }

Test("empty buffer", () => { var b = new TextBuffer(); Equal(0, b.Length); Equal(1, b.LineCount); Equal("", b.GetLine(0).Text); });
Test("CRLF and trailing newline", () => { var b = new TextBuffer("a\r\nb\n"); Equal(3, b.LineCount); Equal("a", b.GetLine(0).Text); Equal(3, b.GetLine(1).Start); Equal("", b.GetLine(2).Text); });
Test("snapshot isolation", () => { var a = new TextBuffer("hello"); var b = a.Replace(1, 3, "i"); Equal("hello", a.ToString()); Equal("hio", b.ToString()); });
Test("range checks", () => { var b = new TextBuffer("abc"); Throws(() => b.Replace(-1, 0, "")); Throws(() => b.Replace(2, 2, "")); Throws(() => b.Slice(0, 4)); });
Test("atomic overlapping edits", () => { var b = new TextBuffer("abcd"); Throws(() => b.Apply([new(0, 3, "a"), new(2, 1, "b")])); Equal("abcd", b.ToString()); });
Test("batch edit order", () => Equal("aXcYe", new TextBuffer("abcde").Apply([new(1, 1, "X"), new(3, 1, "Y")]).ToString()));
Test("offset and position roundtrip", () => { var b = new TextBuffer("abc\ndef\n\nghi"); for (var i = 0; i <= b.Length; i++) Equal(i, b.OffsetAt(b.PositionAt(i))); });
Test("randomized rope differential 10000 edits", () =>
{
    var random = new Random(4127); var text = ""; var rope = new TextBuffer();
    for (var i = 0; i < 10000; i++)
    {
        var start = random.Next(text.Length + 1); var length = random.Next(Math.Min(80, text.Length - start) + 1);
        var insert = string.Concat(Enumerable.Range(0, random.Next(100)).Select(_ => "abc def\n\r\t"[random.Next(10)]));
        text = text.Remove(start, length).Insert(start, insert); rope = rope.Replace(start, length, insert);
        Equal(text, rope.ToString()); Equal(text.Count(c => c == '\n') + 1, rope.LineCount);
        var line = random.Next(rope.LineCount); var starts = new List<int> { 0 }; for (var j = 0; j < text.Length; j++) if (text[j] == '\n') starts.Add(j + 1);
        Equal(starts[line], rope.GetLineStart(line));
        if (rope.TreeHeight > 32) throw new Exception("AVL tree lost balance");
    }
});
Test("large buffer line index", () => { var b = new TextBuffer(string.Concat(Enumerable.Repeat("0123456789\n", 100000))); Equal(100001, b.LineCount); Equal(1099989, b.GetLineStart(99999)); Equal("0123456789", b.GetLine(54321).Text); });
Test("typing undo redo", () => { var e = new EditorSession(new("a.cs", "abc")); e.Select(3, 3); e.Insert("d"); Equal("abcd", e.Buffer.ToString()); e.Undo(); Equal("abc", e.Buffer.ToString()); e.Redo(); Equal("abcd", e.Buffer.ToString()); });
Test("multi cursor", () => { var e = new EditorSession(new("a", "abc")); e.Select(0, 0); e.Select(3, 3, true); e.Insert("!"); Equal("!abc!", e.Buffer.ToString()); e.Undo(); Equal("abc", e.Buffer.ToString()); });
Test("grapheme backspace", () => { var e = new EditorSession(new("a", "a👩‍💻é")); e.Select(e.Buffer.Length, e.Buffer.Length); e.Delete(true); Equal("a👩‍💻", e.Buffer.ToString()); e.Delete(true); Equal("a", e.Buffer.ToString()); });
Test("CRLF backspace", () => { var e = new EditorSession(new("a", "a\r\nb")); e.Select(3, 3); e.Delete(true); Equal("ab", e.Buffer.ToString()); });
Test("autoindent", () => { var e = new EditorSession(new("a.cs", "  if (true) {")); e.Select(e.Buffer.Length, e.Buffer.Length); e.InsertNewLine(); Equal("  if (true) {\n      ", e.Buffer.ToString()); });
Test("find literal", () => { var e = new EditorSession(new("a", "foo FOO food")); Equal(3, e.Find("foo").Count); Equal(1, e.Find("foo", true, false, true).Count); });
Test("regex replace groups", () => { var e = new EditorSession(new("a", "a1 b2")); Equal(2, e.ReplaceAll("([a-z])(\\d)", "$2$1", regex: true)); Equal("1a 2b", e.Buffer.ToString()); });
Test("comment roundtrip", () => { var e = new EditorSession(new("a.cs", "  alpha\n beta")); e.SelectAll(); e.ToggleLineComment(); Equal("  // alpha\n // beta", e.Buffer.ToString()); e.SelectAll(); e.ToggleLineComment(); Equal("  alpha\n beta", e.Buffer.ToString()); });
Test("cursor preferred column", () => { var e = new EditorSession(new("a", "abcdef\nx\nabcdef")); e.Select(5, 5); e.Move(CursorMove.Down); e.Move(CursorMove.Down); Equal(new TextPosition(2, 5), e.Buffer.PositionAt(e.Primary.Active)); });
Test("next occurrence", () => { var e = new EditorSession(new("a", "foo bar foo")); e.AddNextOccurrence(); e.AddNextOccurrence(); Equal(2, e.Selections.Count); e.Insert("x"); Equal("x bar x", e.Buffer.ToString()); });
Test("workspace traversal rejected", () => { Throws(() => Workspace.NormalizePath("../x")); Throws(() => Workspace.NormalizePath("C:\\x")); Throws(() => Workspace.NormalizePath("/x")); Throws(() => Workspace.NormalizePath("a/../x")); });
Test("workspace serialization", () => { var w = new Workspace(); w.Add("src/a.cs", "class A {}"); var copy = Workspace.Deserialize(w.Serialize()); Equal("class A {}", copy.Files["src/a.cs"].Buffer.ToString()); });
Test("workspace search", () => { var w = new Workspace(); w.Add("a", "hello\nworld hello"); Equal(2, w.Search("hello").Count); Equal(1, w.Search("hello", limit: 1).Count); });
Test("workspace rename collision", () => { var w = new Workspace(); w.Add("a", "a"); w.Add("b", "b"); Throws(() => w.Rename("a", "b")); Equal(2, w.Files.Count); });
Test("fuzzy commands", () => { if (CommandRegistry.FuzzyScore("Toggle Side Bar", "tsb") < 0) throw new Exception(); Equal(-1, CommandRegistry.FuzzyScore("Save", "xyz")); });
Test("dock split move close", () => { var d = new DockLayout(); d.Open("a"); d.Open("b"); var id = d.Split("primary", SplitAxis.Horizontal); d.Move("b", "primary", id); Equal("a", d.Groups.First().ActiveTab); Equal("b", d.Groups.Last().ActiveTab); d.Close("b", id); Equal(0, d.Groups.Last().Tabs.Length); });
Test("dock serialization", () => { var d = new DockLayout(); d.Open("a"); d.Split("primary", SplitAxis.Vertical, "b"); var c = new DockLayout(); c.Restore(d.Serialize()); Equal(2, c.Groups.Count()); Equal(d.Serialize(), c.Serialize()); });
Test("dock same-group reorder", () => { var d = new DockLayout(); d.Open("a"); d.Open("b"); d.Open("c"); d.Move("c", "primary", "primary", 0); Equal("c", d.Groups.First().Tabs[0]); Equal(3, d.Groups.First().Tabs.Length); });
Console.WriteLine($"\n{passed} tests passed; exit code {Environment.ExitCode}.");
