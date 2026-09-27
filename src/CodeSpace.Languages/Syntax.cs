using System.Text.RegularExpressions;
using CodeSpace.Core;

namespace CodeSpace.Languages;

public enum TokenKind { Plain, Keyword, Type, String, Number, Comment, Operator, Punctuation, Function, Property, Heading, Tag }
public readonly record struct SyntaxToken(int Start, int Length, TokenKind Kind);
public readonly record struct LexerState(bool BlockComment = false, char MultilineQuote = '\0');
public sealed record TokenizedLine(SyntaxToken[] Tokens, LexerState EndState);
public sealed record LanguageDefinition(string Id, string DisplayName, string[] Extensions, string LineComment, HashSet<string> Keywords);
public sealed record Symbol(string Name, string Kind, int Line, int Character);
public sealed record Diagnostic(string Message, int Line, int Character, int Length, string Severity = "warning");
public sealed record FoldingRange(int StartLine, int EndLine);

public static class LanguageCatalog
{
    private static HashSet<string> Words(string value) => new(value.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    private const string CWords = "abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly record ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while yield required init partial where get set value";
    private const string JsWords = "as async await break case catch class const continue debugger declare default delete do else enum export extends false finally for from function get if implements import in instanceof interface let new null of package private protected public readonly return satisfies set static super switch this throw true try type typeof undefined var void while with yield";
    public static IReadOnlyList<LanguageDefinition> All { get; } = [
        new("csharp", "C#", [".cs"], "//", Words(CWords)),
        new("typescript", "TypeScript", [".ts", ".tsx"], "//", Words(JsWords)),
        new("javascript", "JavaScript", [".js", ".mjs", ".cjs", ".jsx"], "//", Words(JsWords)),
        new("json", "JSON", [".json", ".jsonc"], "//", Words("true false null")),
        new("python", "Python", [".py", ".pyi"], "#", Words("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield")),
        new("html", "HTML", [".html", ".htm"], "", Words("")),
        new("xml", "XML", [".xml", ".xaml", ".csproj", ".props", ".slnx", ".svg"], "", Words("")),
        new("css", "CSS", [".css", ".scss"], "", Words("important media supports keyframes from to")),
        new("markdown", "Markdown", [".md", ".mdx"], "", Words("")),
        new("yaml", "YAML", [".yml", ".yaml"], "#", Words("true false null yes no on off")),
        new("shellscript", "Shell Script", [".sh", ".bash"], "#", Words("if then else fi for do done function case esac export in")),
        new("plaintext", "Plain Text", [".txt"], "", Words(""))
    ];
    public static LanguageDefinition ForPath(string path) => All.FirstOrDefault(l => l.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) ?? All[^1];
    public static LanguageDefinition ForId(string id) => All.FirstOrDefault(l => l.Id == id) ?? All[^1];
}

/// <summary>Custom stateful lexical tokenizer, not a TextMate/Oniguruma implementation.</summary>
public static class SyntaxLexer
{
    public static TokenizedLine Tokenize(string text, LanguageDefinition language, LexerState state = default)
    {
        var tokens = new List<SyntaxToken>(); var i = 0; var block = state.BlockComment; var quote = state.MultilineQuote;
        if (language.Id == "plaintext") return new([new(0, text.Length, TokenKind.Plain)], default);
        if (language.Id == "markdown" && text.TrimStart().StartsWith('#')) return new([new(0, text.Length, TokenKind.Heading)], default);
        while (i < text.Length)
        {
            var start = i;
            if (block)
            {
                var end = text.IndexOf("*/", i, StringComparison.Ordinal);
                if (end < 0) { tokens.Add(new(i, text.Length - i, TokenKind.Comment)); return new(tokens.ToArray(), new(true, quote)); }
                i = end + 2; block = false; tokens.Add(new(start, i - start, TokenKind.Comment)); continue;
            }
            if (quote != '\0')
            {
                while (i < text.Length) { var ch = text[i++]; if (ch == '\\' && i < text.Length) i++; else if (ch == quote) { quote = '\0'; break; } }
                tokens.Add(new(start, i - start, TokenKind.String)); continue;
            }
            var current = text[i];
            if (language.LineComment.Length > 0 && text.AsSpan(i).StartsWith(language.LineComment, StringComparison.Ordinal))
            { tokens.Add(new(i, text.Length - i, TokenKind.Comment)); break; }
            if (current == '/' && i + 1 < text.Length && text[i + 1] == '*') { block = true; i += 2; continue; }
            if ((language.Id is "html" or "xml") && text.AsSpan(i).StartsWith("<!--"))
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal); i = end < 0 ? text.Length : end + 3;
                tokens.Add(new(start, i - start, TokenKind.Comment)); continue;
            }
            if (current is '"' or '\'' or '`')
            {
                i++; var closed = false;
                while (i < text.Length) { var ch = text[i++]; if (ch == '\\' && i < text.Length) i++; else if (ch == current) { closed = true; break; } }
                if (!closed && current == '`') quote = '`';
                var rest = text.AsSpan(i).TrimStart();
                tokens.Add(new(start, i - start, language.Id == "json" && rest.StartsWith(":") ? TokenKind.Property : TokenKind.String)); continue;
            }
            if (char.IsDigit(current))
            {
                i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '_')) i++;
                tokens.Add(new(start, i - start, TokenKind.Number)); continue;
            }
            if (char.IsLetter(current) || current is '_' or '$' || current == '@' && language.Id == "csharp")
            {
                i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '$')) i++;
                var word = text[start..i]; var rest = text.AsSpan(i).TrimStart();
                var kind = language.Keywords.Contains(word) ? TokenKind.Keyword : rest.StartsWith("(") ? TokenKind.Function : char.IsUpper(word[0]) ? TokenKind.Type : TokenKind.Plain;
                if ((language.Id is "html" or "xml") && start > 0 && (text[start - 1] == '<' || text[start - 1] == '/')) kind = TokenKind.Tag;
                tokens.Add(new(start, i - start, kind)); continue;
            }
            i++; tokens.Add(new(start, 1, char.IsWhiteSpace(current) ? TokenKind.Plain : "{}[]();,.".Contains(current) ? TokenKind.Punctuation : TokenKind.Operator));
        }
        return new(tokens.ToArray(), new(block, quote));
    }
}

public sealed class SyntaxDocument
{
    private readonly List<TokenizedLine> _lines = [];
    public LanguageDefinition Language { get; private set; }
    public SyntaxDocument(string path) => Language = LanguageCatalog.ForPath(path);
    public void SetLanguage(string id) { Language = LanguageCatalog.ForId(id); _lines.Clear(); }
    public void Invalidate(int firstLine) { firstLine = Math.Clamp(firstLine, 0, _lines.Count); if (firstLine < _lines.Count) _lines.RemoveRange(firstLine, _lines.Count - firstLine); }
    public TokenizedLine GetLine(TextBuffer buffer, int line)
    {
        if (line < 0 || line >= buffer.LineCount) throw new ArgumentOutOfRangeException(nameof(line));
        while (_lines.Count <= line)
        {
            var state = _lines.Count == 0 ? default : _lines[^1].EndState;
            _lines.Add(SyntaxLexer.Tokenize(buffer.GetLine(_lines.Count).Text, Language, state));
        }
        return _lines[line];
    }
}

public static class LanguageServices
{
    private static readonly Regex SymbolPattern = new(@"\b(class|interface|struct|record|enum|namespace|function|def|const|let)\s+([\p{L}_$][\p{L}\p{N}_$]*)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));
    public static IReadOnlyList<Symbol> Symbols(TextBuffer buffer)
    {
        var result = new List<Symbol>();
        for (var i = 0; i < Math.Min(buffer.LineCount, 100000); i++)
            foreach (Match match in SymbolPattern.Matches(buffer.GetLine(i).Text)) result.Add(new(match.Groups[2].Value, match.Groups[1].Value, i, match.Groups[2].Index));
        return result;
    }
    public static IReadOnlyList<string> Complete(TextBuffer buffer, string prefix, string languageId, int limit = 100)
    {
        var words = new HashSet<string>(LanguageCatalog.ForId(languageId).Keywords, StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(buffer.Slice(0, Math.Min(buffer.Length, 1024 * 1024)), @"[\p{L}_$][\p{L}\p{N}_$]*", RegexOptions.None, TimeSpan.FromMilliseconds(100))) words.Add(match.Value);
        return words.Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && w != prefix).OrderBy(w => w).Take(limit).ToArray();
    }
    public static IReadOnlyList<Diagnostic> Diagnostics(TextBuffer buffer, string path)
    {
        var result = new List<Diagnostic>(); var stack = new Stack<(char Character, int Line, int Column)>(); var syntax = new SyntaxDocument(path);
        for (var line = 0; line < Math.Min(buffer.LineCount, 100000); line++)
        {
            var text = buffer.GetLine(line).Text;
            foreach (var token in syntax.GetLine(buffer, line).Tokens)
            {
                if (token.Kind is TokenKind.String or TokenKind.Comment) continue;
                for (var i = token.Start; i < token.Start + token.Length; i++)
                {
                    var ch = text[i]; if ("{[(".Contains(ch)) stack.Push((ch, line, i));
                    else if ("}])".Contains(ch))
                    {
                        if (stack.Count == 0 || "{[(".IndexOf(stack.Peek().Character) != "}])".IndexOf(ch)) result.Add(new($"Unmatched '{ch}'", line, i, 1, "error")); else stack.Pop();
                    }
                }
            }
            if (result.Count > 1000) break;
        }
        foreach (var open in stack.Take(1000)) result.Add(new($"Unclosed '{open.Character}'", open.Line, open.Column, 1, "error"));
        return result.OrderBy(d => d.Line).ThenBy(d => d.Character).ToArray();
    }
    public static IReadOnlyList<FoldingRange> FoldingRanges(TextBuffer buffer, string path)
    {
        var ranges = new List<FoldingRange>(); var stack = new Stack<int>(); var syntax = new SyntaxDocument(path);
        for (var line = 0; line < buffer.LineCount; line++)
        {
            var text = buffer.GetLine(line).Text;
            foreach (var token in syntax.GetLine(buffer, line).Tokens.Where(t => t.Kind == TokenKind.Punctuation))
            {
                if (text[token.Start] == '{') stack.Push(line);
                else if (text[token.Start] == '}' && stack.Count > 0) { var start = stack.Pop(); if (line > start) ranges.Add(new(start, line)); }
            }
        }
        return ranges;
    }
}
