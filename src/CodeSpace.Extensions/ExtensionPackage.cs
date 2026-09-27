using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeSpace.Core;

namespace CodeSpace.Extensions;

public enum ExtensionHostKind { Declarative, Browser, Node, Unsupported }
public sealed record ExtensionCommand(string Command, string Title, string? Category);
public sealed record ExtensionManifest(string Id, string Name, string DisplayName, string Publisher, string Version, string Description,
    string Engine, string? Browser, string? Main, string? License, string[] ActivationEvents, string[] Dependencies, JsonElement Contributions)
{
    public static ExtensionManifest Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); var root = document.RootElement;
        string Get(string name, string fallback = "") => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
        string[] List(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToArray() : [];
        var name = Get("name"); var publisher = Get("publisher");
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$") || !Regex.IsMatch(publisher, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$")) throw new FormatException("A valid extension name and publisher are required.");
        var engine = root.TryGetProperty("engines", out var engines) && engines.TryGetProperty("vscode", out var version) ? version.GetString() ?? "" : "";
        if (engine.Length == 0) throw new FormatException("Missing engines.vscode.");
        JsonElement contributions = root.TryGetProperty("contributes", out var c) ? c.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
        return new(publisher + "." + name, name, Get("displayName", name), publisher, Get("version", "0.0.0"), Get("description"), engine,
            root.TryGetProperty("browser", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null,
            root.TryGetProperty("main", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null,
            Get("license"), List("activationEvents"), List("extensionDependencies"), contributions);
    }
    public ExtensionHostKind SelectHost(bool browser) => browser ? Browser is not null ? ExtensionHostKind.Browser : Main is null ? ExtensionHostKind.Declarative : ExtensionHostKind.Unsupported
        : Main is not null ? ExtensionHostKind.Node : Browser is not null ? ExtensionHostKind.Browser : ExtensionHostKind.Declarative;
    public IEnumerable<ExtensionCommand> Commands
    {
        get
        {
            if (!Contributions.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array) yield break;
            foreach (var command in commands.EnumerateArray())
                if (command.TryGetProperty("command", out var id) && command.TryGetProperty("title", out var title)) yield return new(id.GetString()!, title.GetString()!, command.TryGetProperty("category", out var category) ? category.GetString() : null);
        }
    }
}

/// <summary>Bounded VSIX reader. Inspection never executes code and never writes archive paths to disk.</summary>
public sealed class ExtensionPackage
{
    public const long MaxExpandedSize = 100 * 1024 * 1024;
    public ExtensionManifest Manifest { get; }
    public IReadOnlyDictionary<string, byte[]> Files { get; }
    private ExtensionPackage(ExtensionManifest manifest, Dictionary<string, byte[]> files) { Manifest = manifest; Files = files; }
    public static ExtensionPackage Read(Stream input)
    {
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 10000) throw new InvalidDataException("VSIX contains too many entries.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/'); if (name.EndsWith('/')) continue;
            name = Workspace.NormalizePath(name);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate archive entry.");
            if (entry.Length > 16 * 1024 * 1024 || entry.Length < 0 || (total += entry.Length) > MaxExpandedSize) throw new InvalidDataException("VSIX expansion limit exceeded.");
            if (!name.StartsWith("extension/", StringComparison.Ordinal)) continue;
            name = name[10..]; using var stream = entry.Open(); using var memory = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + read > Math.Min(entry.Length, 16 * 1024 * 1024)) throw new InvalidDataException("Invalid uncompressed size.");
                memory.Write(buffer, 0, read);
            }
            files.Add(name, memory.ToArray());
        }
        if (!files.TryGetValue("package.json", out var package)) throw new InvalidDataException("VSIX has no extension/package.json.");
        var manifest = ExtensionManifest.Parse(new UTF8Encoding(false, true).GetString(package));
        foreach (var entry in new[] { manifest.Browser, manifest.Main }.Where(x => x is not null))
        {
            var path = NormalizeEntry(entry!);
            if (!files.ContainsKey(path) && !files.ContainsKey(path + ".js")) throw new InvalidDataException("Extension entry point is missing: " + path);
        }
        return new(manifest, files);
    }
    public static string NormalizeEntry(string path) => Workspace.NormalizePath(path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path);
    public Dictionary<string, string> TextFiles()
    {
        var result = new Dictionary<string, string>(); var utf8 = new UTF8Encoding(false, true);
        foreach (var item in Files.Where(p => p.Key.EndsWith(".js") || p.Key.EndsWith(".cjs") || p.Key.EndsWith(".json"))) result[item.Key] = utf8.GetString(item.Value);
        return result;
    }
}
public sealed record ExtensionCapabilityReport(string ExtensionId, ExtensionHostKind Host, bool RequiresTrust, bool ApiCompatibilityVerified, string[] Limitations);
public static class ExtensionCompatibility
{
    public static ExtensionCapabilityReport Inspect(ExtensionManifest manifest, bool browser)
    {
        var host = manifest.SelectHost(browser); var limitations = new List<string>();
        if (host == ExtensionHostKind.Unsupported) limitations.Add("This extension needs a Node.js host. A static browser deployment cannot execute it.");
        if (manifest.Dependencies.Length > 0) limitations.Add("Extension dependencies are not automatically installed.");
        if (host != ExtensionHostKind.Declarative) limitations.Add("Only the documented VS Code API subset is implemented; arbitrary extension compatibility is not verified.");
        foreach (var contribution in manifest.Contributions.EnumerateObject())
            if (contribution.Name is not ("commands" or "configuration" or "themes" or "snippets" or "languages")) limitations.Add("Contribution requires further integration: " + contribution.Name);
        limitations.Add("Extension license and provenance must be reviewed by the user. No signature verification is performed.");
        return new(manifest.Id, host, host is ExtensionHostKind.Browser or ExtensionHostKind.Node, false, limitations.ToArray());
    }
}
