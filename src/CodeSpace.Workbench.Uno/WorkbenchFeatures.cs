using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Languages;
using CodeSpace.Controls.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private readonly EditorConfiguration _configuration = new();
    private readonly DispatcherTimer _extensionSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly SemaphoreSlim _extensionSyncLock = new(1, 1);
    private readonly Dictionary<string, (TextBuffer Buffer, long Version, bool Dirty)> _extensionDocuments = [];
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _featureRequests = [];
    private readonly HashSet<string> _extensionCommands = [];
    private readonly Dictionary<string, Dictionary<string, List<Diagnostic>>> _extensionDiagnostics = [];
    private int _featureRequestId;
    private bool _extensionStarted, _extensionSynced;
    private EditorOptions? _appliedOptions;
    public sealed record EditorViewSnapshot(double ScrollX, double ScrollY, CollapsedRegion[] Folds);
    private Dictionary<string, EditorViewSnapshot> _viewStates = [];
    private readonly Dictionary<CodeEditorControl, string> _viewKeys = [];
    private Dictionary<string, EditorViewSnapshot> CaptureViews()
    {
        foreach (var editor in _editors) if (_viewKeys.TryGetValue(editor, out var key))
            _viewStates[key] = new(editor.Viewport.ScrollX, editor.Viewport.ScrollY, editor.Viewport.Folding.Regions.ToArray());
        return _viewStates;
    }
    private void RestoreView(CodeEditorControl editor, string group)
    {
        var key = group + "\n" + editor.Session.File.Path; _viewKeys[editor] = key;
        editor.Viewport.ViewChanged += (_, _) => QueueRecovery();
        if (!_viewStates.TryGetValue(key, out var view)) return;
        editor.Viewport.ScrollX = double.IsFinite(view.ScrollX) ? Math.Max(0, view.ScrollX) : 0;
        editor.Viewport.ScrollY = double.IsFinite(view.ScrollY) ? Math.Max(0, view.ScrollY) : 0;
        foreach (var fold in view.Folds ?? []) if (fold.StartLine >= 0 && fold.EndLine > fold.StartLine && fold.EndLine < editor.Session.Buffer.LineCount)
            editor.Viewport.Folding.Collapse(fold.StartLine, fold.EndLine);
    }
    public EditorOptions EditorOptions => _configuration.Options;
    public object EditorDiagnostics => _editors.Select(e => new {
        path = e.Session.File.Path, group = _viewKeys.GetValueOrDefault(e)?.Split('\n')[0], scrollX = e.Viewport.ScrollX, scrollY = e.Viewport.ScrollY, selection = e.Session.Primary, visibleLines = e.Viewport.Folding.VisibleLineCount(e.Session.Buffer.LineCount),
        folds = e.Viewport.Folding.Regions.ToArray(), cpuPaintMs = e.Renderer.Metrics.CpuMilliseconds,
        textDrawCalls = e.Renderer.Metrics.TextDrawCalls, layoutBuilds = e.Renderer.Metrics.LayoutBuilds,
        cachedLines = e.Renderer.Metrics.CachedLines, dirty = e.Session.IsDirty
    }).ToArray();
    private void InitializeFeatures()
    {
        RefreshConfiguration();
        _extensionSyncTimer.Tick += (_, _) => { _extensionSyncTimer.Stop(); RunAsync(SyncExtensionDocumentsAsync); };
        void Add(string id, string title, string key, Action execute) => _commands.Register(new(id, title, key, execute));
        Add("editor.fold", "Editor: Toggle Fold at Cursor", "", () => _activeEditor?.ToggleFold());
        Add("editor.foldAll", "Editor: Fold All", "", () => _activeEditor?.FoldAll());
        Add("editor.unfoldAll", "Editor: Unfold All", "", () => _activeEditor?.UnfoldAll());
        Add("editor.action.showHover", "Editor: Show Hover", "", () => RunAsync(ShowHoverAsync));
        Add("editor.action.revealDefinition", "Editor: Go to Definition", "F12", () => RunAsync(ShowDefinitionAsync));
        Add("editor.action.formatDocument", "Editor: Format Document", "Shift+Alt+F", () => RunAsync(FormatDocumentAsync));
        Add("workbench.action.gotoSymbol", "Go to Symbol in Editor…", "", () => RunAsync(ShowSymbolsAsync));
        Add("editor.action.triggerSuggest", "Editor: Suggest Completions", "Ctrl+Space", () => RunAsync(ShowCompletionsAsync));
    }
    private void RefreshConfiguration()
    {
        try { _configuration.LoadWorkspace(_workspace.Files.TryGetValue(".vscode/settings.json", out var file) ? file.Buffer.ToString() : "{}"); }
        catch (Exception error) { Notify("Invalid settings; keeping the last valid configuration: " + error.Message); return; }
        var options = _configuration.Options; _fontSize = options.FontSize; _minimap = options.Minimap; _whitespace = options.RenderWhitespace;
        if (_appliedOptions != options)
        {
            _appliedOptions = options;
            foreach (var editor in _editors) { ConfigureEditor(editor); editor.InvalidateEditor(); }
        }
        if (_extensionStarted) RunAsync(SyncConfigurationAsync);
    }
    private void ConfigureEditor(CodeEditorControl editor)
    {
        var options = _configuration.Options;
        editor.Session.TabSize = options.TabSize; editor.Viewport.FontSize = options.FontSize;
        editor.Viewport.ShowMinimap = options.Minimap; editor.Viewport.ShowWhitespace = options.RenderWhitespace;
        editor.Viewport.ShowLineNumbers = options.LineNumbers;
        editor.Viewport.Diagnostics = ExtensionDiagnosticsFor(editor.Session.File.Path).ToArray();
    }
    private void WriteEditorSettings()
    {
        var values = new Dictionary<string, JsonElement>(_configuration.Workspace, StringComparer.Ordinal)
        {
            ["editor.fontSize"] = JsonSerializer.SerializeToElement(_fontSize),
            ["editor.minimap.enabled"] = JsonSerializer.SerializeToElement(_minimap),
            ["editor.renderWhitespace"] = JsonSerializer.SerializeToElement(_whitespace ? "all" : "none")
        };
        SetSettingsText(JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
    private void SetSettingsText(string text)
    {
        const string path = ".vscode/settings.json";
        if (_workspace.Files.ContainsKey(path)) SessionFor(path).Apply([new(0, _workspace.Files[path].Buffer.Length, text)]);
        else _workspace.Add(path, text);
        RefreshConfiguration(); QueueRecovery();
    }
    private Task SyncConfigurationAsync() => _extensionStarted && _platform.ExtensionBridge is { } bridge
        ? bridge.SendAsync(JsonSerializer.Serialize(new { type = "configuration", defaults = _configuration.Defaults, global = _configuration.User, workspace = _configuration.Workspace }))
        : Task.CompletedTask;
    private void RestoreFeatureState(JsonElement root, Workspace workspace)
    {
        if (root.TryGetProperty("views", out var views) && views.ValueKind == JsonValueKind.Object)
            _viewStates = JsonSerializer.Deserialize<Dictionary<string, EditorViewSnapshot>>(views.GetRawText()) ?? [];
        if (root.TryGetProperty("savedBaselines", out var baselines) && baselines.ValueKind == JsonValueKind.Object)
            foreach (var baseline in baselines.EnumerateObject()) if (workspace.Files.TryGetValue(baseline.Name, out var file) && baseline.Value.ValueKind == JsonValueKind.String)
            {
                var current = file.Buffer; workspace.Add(file.Path, baseline.Value.GetString()!, overwrite: true).Buffer = current;
            }
        if (root.TryGetProperty("userSettings", out var settings) && settings.ValueKind == JsonValueKind.Object)
            foreach (var property in settings.EnumerateObject()) _configuration.SetUser(property.Name, property.Value);
    }
    private void RestoreSelections(JsonElement root)
    {
        if (!root.TryGetProperty("selections", out var selections) || selections.ValueKind != JsonValueKind.Object) return;
        foreach (var property in selections.EnumerateObject()) if (_workspace.Files.ContainsKey(property.Name))
        {
            var values = property.Value.EnumerateArray().Select(v => new Selection(v.GetProperty("Anchor").GetInt32(), v.GetProperty("Active").GetInt32())).ToArray();
            SessionFor(property.Name).SetSelections(values);
        }
    }
    private void QueueDocumentSync()
    {
        if (!_extensionStarted || _disposed) return;
        if (!_extensionSyncTimer.IsEnabled) _extensionSyncTimer.Start();
    }
    private static string WorkspaceUri(string path) => "codespace:///" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    private static string WorkspacePath(string value)
    {
        var uri = new Uri(value, UriKind.Absolute);
        if (uri.Scheme != "codespace" || uri.Host.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Only local codespace workspace resources are supported.");
        return Workspace.NormalizePath(Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/'));
    }
}
