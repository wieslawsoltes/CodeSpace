using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeSpace.Core;

public sealed class WorkspaceFile
{
    public string Path { get; }
    public TextBuffer Buffer { get; set; }
    public string SavedText { get; private set; }
    public bool IsDirty => Buffer.ToString() != SavedText;
    public WorkspaceFile(string path, string text) { Path = Workspace.NormalizePath(path); Buffer = new TextBuffer(text); SavedText = text; }
    public void MarkSaved() => SavedText = Buffer.ToString();
}
public sealed record SearchHit(string Path, int Line, int Column, int Length, string Preview);
public sealed record WorkspaceSnapshot(int SchemaVersion, string Name, Dictionary<string, string> Files);

/// <summary>Virtual filesystem shared by browser and desktop workspaces. Paths are relative and traversal-free.</summary>
public sealed class Workspace
{
    private readonly SortedDictionary<string, WorkspaceFile> _files = new(StringComparer.Ordinal);
    public string Name { get; set; } = "codespace-workspace";
    public IReadOnlyDictionary<string, WorkspaceFile> Files => _files;
    public event EventHandler? Changed;
    public static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Contains(':') || path.IndexOf('\0') >= 0) throw new ArgumentException("A relative workspace path is required.");
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p is "." or "..")) throw new ArgumentException("Path traversal is not allowed.");
        return string.Join('/', parts);
    }
    public WorkspaceFile Add(string path, string text, bool overwrite = false)
    {
        path = NormalizePath(path);
        if (!overwrite && _files.ContainsKey(path)) throw new InvalidOperationException("The file already exists.");
        var file = new WorkspaceFile(path, text); _files[path] = file; Changed?.Invoke(this, EventArgs.Empty); return file;
    }
    public bool Remove(string path) { var result = _files.Remove(NormalizePath(path)); if (result) Changed?.Invoke(this, EventArgs.Empty); return result; }
    public WorkspaceFile Rename(string path, string destination)
    {
        path = NormalizePath(path); destination = NormalizePath(destination);
        if (!_files.TryGetValue(path, out var file)) throw new FileNotFoundException(path);
        if (_files.ContainsKey(destination)) throw new InvalidOperationException("The destination exists.");
        var result = new WorkspaceFile(destination, file.SavedText) { Buffer = file.Buffer };
        _files.Remove(path); _files[destination] = result; Changed?.Invoke(this, EventArgs.Empty); return result;
    }
    public IReadOnlyList<SearchHit> Search(string query, bool matchCase = false, bool regex = false, int limit = 2000, CancellationToken cancellation = default)
    {
        if (query.Length == 0) return [];
        var pattern = new Regex(regex ? query : Regex.Escape(query), matchCase ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
        var results = new List<SearchHit>();
        foreach (var file in _files.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var line = 0; line < file.Buffer.LineCount; line++)
            {
                var text = file.Buffer.GetLine(line).Text;
                foreach (Match match in pattern.Matches(text))
                {
                    results.Add(new SearchHit(file.Path, line, match.Index, match.Length, text));
                    if (results.Count >= limit) return results;
                }
            }
        }
        return results;
    }
    public string Serialize() => JsonSerializer.Serialize(new WorkspaceSnapshot(1, Name, _files.ToDictionary(x => x.Key, x => x.Value.Buffer.ToString())));
    public static Workspace Deserialize(string json)
    {
        var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(json) ?? throw new FormatException("Invalid workspace.");
        if (snapshot.SchemaVersion != 1 || snapshot.Files is null || snapshot.Files.Count > 10000) throw new FormatException("Unsupported or oversized workspace.");
        var result = new Workspace { Name = snapshot.Name };
        long total = 0;
        foreach (var file in snapshot.Files)
        {
            total += file.Value.Length; if (total > 64 * 1024 * 1024) throw new FormatException("Workspace exceeds 64 million characters.");
            result.Add(file.Key, file.Value);
        }
        return result;
    }
}

public sealed record Command(string Id, string Title, string Shortcut, Action Execute);
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> _commands = new(StringComparer.Ordinal);
    public IEnumerable<Command> All => _commands.Values;
    public void Register(Command command) => _commands[command.Id] = command;
    public bool Execute(string id) { if (!_commands.TryGetValue(id, out var command)) return false; command.Execute(); return true; }
    public IReadOnlyList<Command> Search(string query, int limit = 50) => _commands.Values.Select(c => (Command: c, Score: FuzzyScore(c.Title, query)))
        .Where(x => x.Score >= 0).OrderByDescending(x => x.Score).ThenBy(x => x.Command.Title).Take(limit).Select(x => x.Command).ToArray();
    public static int FuzzyScore(string text, string query)
    {
        var position = 0; var score = 0; var previous = -2;
        foreach (var ch in query.TrimStart('>'))
        {
            var found = text.IndexOf(ch.ToString(), position, StringComparison.OrdinalIgnoreCase); if (found < 0) return -1;
            score += 10 + (found == previous + 1 ? 8 : 0) + (found == 0 || !char.IsLetterOrDigit(text[found - 1]) ? 12 : 0) - Math.Min(found - position, 8);
            previous = found; position = found + 1;
        }
        return score;
    }
}
