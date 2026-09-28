using System.Text.Json;

namespace CodeSpace.Core;

public sealed record EditorOptions(float FontSize, int TabSize, bool Minimap, bool RenderWhitespace, bool LineNumbers);

/// <summary>JSONC workspace settings and user/default precedence, independent of the UI.</summary>
public sealed class EditorConfiguration
{
    private readonly Dictionary<string, JsonElement> _defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _user = new(StringComparer.Ordinal);
    private Dictionary<string, JsonElement> _workspace = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, JsonElement> Defaults => _defaults;
    public IReadOnlyDictionary<string, JsonElement> User => _user;
    public IReadOnlyDictionary<string, JsonElement> Workspace => _workspace;
    public EditorConfiguration()
    {
        foreach (var (key, value) in new (string, object)[] { ("editor.fontSize", 14), ("editor.tabSize", 4),
            ("editor.minimap.enabled", true), ("editor.renderWhitespace", "none"), ("editor.lineNumbers", "on") })
            _defaults[key] = JsonSerializer.SerializeToElement(value);
    }
    public void SetDefault(string key, JsonElement value) { ValidateKey(key); _defaults[key] = value.Clone(); }
    public void SetUser(string key, JsonElement? value)
    {
        ValidateKey(key); if (value.HasValue) _user[key] = value.Value.Clone(); else _user.Remove(key);
    }
    public static Dictionary<string, JsonElement> Parse(string json)
    {
        if (json.Length > 1024 * 1024) throw new FormatException("Settings exceed the 1 MiB limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Settings must be a JSON object.");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject()) { ValidateKey(property.Name); result[property.Name] = property.Value.Clone(); }
        return result;
    }
    public void LoadWorkspace(string json) => _workspace = Parse(json);
    public JsonElement? Get(string key) => _workspace.TryGetValue(key, out var w) ? w : _user.TryGetValue(key, out var u) ? u : _defaults.TryGetValue(key, out var d) ? d : null;
    public EditorOptions Options
    {
        get
        {
            float Number(string key, float fallback) => Get(key) is { ValueKind: JsonValueKind.Number } element && element.TryGetSingle(out var value) && float.IsFinite(value) ? value : fallback;
            bool Bool(string key, bool fallback) => Get(key) is { ValueKind: JsonValueKind.True } ? true : Get(key) is { ValueKind: JsonValueKind.False } ? false : fallback;
            string Text(string key, string fallback) => Get(key) is { ValueKind: JsonValueKind.String } element ? element.GetString()! : fallback;
            return new(Math.Clamp(Number("editor.fontSize", 14), 9, 32), (int)Math.Clamp(Number("editor.tabSize", 4), 1, 16), Bool("editor.minimap.enabled", true), Text("editor.renderWhitespace", "none") != "none", Text("editor.lineNumbers", "on") != "off");
        }
    }
    public string WithWorkspaceValue(string key, JsonElement? value)
    {
        ValidateKey(key); var values = new Dictionary<string, JsonElement>(_workspace, StringComparer.Ordinal);
        if (value.HasValue) values[key] = value.Value.Clone(); else values.Remove(key);
        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 512 || key.Split('.').Any(part => part is "__proto__" or "constructor" or "prototype"))
            throw new ArgumentException("Unsupported configuration key.", nameof(key));
    }
}
