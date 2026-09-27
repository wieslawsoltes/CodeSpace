using System.Text;
using System.Text.Json;
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Extensions;
using Microsoft.UI.Xaml.Controls;

namespace CodeSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private async Task InstallVsixAsync()
    {
        foreach (var file in await _platform.PickFilesAsync(".vsix"))
        {
            using var stream = new MemoryStream(file.Bytes); var package = ExtensionPackage.Read(stream); _extensions[package.Manifest.Id] = package;
            var report = ExtensionCompatibility.Inspect(package.Manifest, _platform.IsBrowser); Log("Inspected " + package.Manifest.Id + " — " + report.Host);
            foreach (var limit in report.Limitations) Log("  " + limit);
        }
        ShowSide("extensions");
    }
    private async Task ActivatePackageAsync(ExtensionPackage package)
    {
        var manifest = package.Manifest; var report = ExtensionCompatibility.Inspect(manifest, _platform.IsBrowser);
        if (report.Host == ExtensionHostKind.Unsupported) { Notify(string.Join(" ", report.Limitations)); return; }
        if (report.Host == ExtensionHostKind.Declarative) { Notify("Manifest inspected. Declarative theme/grammar/snippet contribution installation is not yet implemented."); return; }
        var bridge = _platform.ExtensionBridge;
        if (bridge is null || !bridge.IsAvailable) { Notify("No extension host is available on this platform. The VSIX was inspected but no code was executed."); return; }
        var trust = new ContentDialog { XamlRoot = XamlRoot, Title = "Trust and run " + manifest.DisplayName + "?", Content = "Extension code is executable and is not independently verified. A worker is not a security sandbox: an extension can read workspace data and access the network; a desktop Node host also has operating-system access. Install only code you trust. API compatibility is partial.\n\n" + string.Join("\n", report.Limitations), PrimaryButtonText = "Trust and run", CloseButtonText = "Cancel" };
        if (await trust.ShowAsync() != ContentDialogResult.Primary) return;
        await bridge.StartAsync(); _extensionStarted = true; await SyncExtensionDocumentsAsync(); await SyncConfigurationAsync();
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "activate", manifest = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(package.Files["package.json"])), files = package.TextFiles(), trusted = true }));
    }
    private async Task RunExtensionProbeAsync()
    {
        var bridge = _platform.ExtensionBridge;
        if (bridge is null || !bridge.IsAvailable) { Notify("The extension host has not been connected. No compatibility probe was executed."); return; }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Run the bundled extension probe?", Content = "This runs the repository's small MIT-licensed extension fixture using require('vscode'), activate(context), registerCommand and showInformationMessage. It also registers C# completion, hover, symbol, definition, formatting and diagnostic providers for manual testing. It does not qualify arbitrary extensions.", PrimaryButtonText = "Run probe", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await bridge.StartAsync(); _extensionStarted = true; await SyncExtensionDocumentsAsync(); await SyncConfigurationAsync();
        var source = """
            const vscode = require('vscode');
            exports.activate = context => {
              const add = value => context.subscriptions.push(value);
              add(vscode.commands.registerCommand('codespace.hello', () => vscode.window.showInformationMessage('Hello from the VS Code API compatibility probe.')));
              add(vscode.languages.registerCompletionItemProvider('csharp', {
                provideCompletionItems() { const item = new vscode.CompletionItem('CodeSpaceProviderCompletion', vscode.CompletionItemKind.Function); item.detail = 'Completion provided by the activated extension'; return [item]; }
              }));
              add(vscode.languages.registerHoverProvider('csharp', { provideHover() { return new vscode.Hover('CodeSpace extension hover is connected to the custom Uno editor.'); } }));
              add(vscode.languages.registerDefinitionProvider('csharp', { provideDefinition(document) { return new vscode.Location(document.uri, new vscode.Range(0, 0, 0, 0)); } }));
              add(vscode.languages.registerDocumentSymbolProvider('csharp', { provideDocumentSymbols(document) { return [new vscode.DocumentSymbol('Extension document symbol', 'Probe', vscode.SymbolKind.Class, new vscode.Range(0, 0, 0, 0), new vscode.Range(0, 0, 0, 0))]; } }));
              add(vscode.languages.registerDocumentFormattingEditProvider('csharp', {
                provideDocumentFormattingEdits(document) { const before = document.getText(); const after = before.replace(/[ \t]+$/gm, ''); return before === after ? [] : [vscode.TextEdit.replace(new vscode.Range(new vscode.Position(0,0), document.positionAt(before.length)), after)]; }
              }));
              const diagnostics = vscode.languages.createDiagnosticCollection('codespace-probe'); add(diagnostics);
              const document = vscode.window.activeTextEditor?.document;
              if (document) diagnostics.set(document.uri, [new vscode.Diagnostic(new vscode.Range(0, 0, 0, 1), 'Probe diagnostic from the extension host', vscode.DiagnosticSeverity.Information)]);
            };
            """;
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "activate", manifest = new { publisher = "codespace", name = "hello", version = "0.1.0", browser = "extension.js", main = "extension.js" }, files = new Dictionary<string, string> { ["extension.js"] = source }, trusted = true }));
        await bridge.SendAsync(JsonSerializer.Serialize(new { type = "execute", command = "codespace.hello", args = Array.Empty<object>() }));
    }
    private void QueueExtensionDocumentSync() => QueueDocumentSync();
    private Task SyncExtensionDocumentsAsync() => SendDocumentChangesAsync();
    private void ExtensionMessage(object? sender, string json) => DispatcherQueue.TryEnqueue(() => RunAsync(() => HandleExtensionMessageAsync(json)));
    private async Task HandleExtensionMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement; var type = root.GetProperty("type").GetString();
        if (type == "featureResult") { if (_featureRequests.TryGetValue(root.GetProperty("id").GetInt32(), out var completion)) completion.TrySetResult(root.Clone()); return; }
        if (type == "diagnostics") { ReceiveDiagnostics(root); return; }
        if (type == "unregisterCommand") { var commandId = root.GetProperty("command").GetString()!; if (_extensionCommands.Remove(commandId)) _commands.Unregister(commandId); return; }
        if (type == "registerCommand")
        {
            var id = root.GetProperty("command").GetString()!;
            if (_commands.Contains(id) && !_extensionCommands.Contains(id)) { Notify("Extension command conflicts with a workbench command: " + id); return; }
            _extensionCommands.Add(id); _commands.Register(new(id, "Extension: " + id, "", () => RunAsync(async () => { if (_platform.ExtensionBridge is { } bridge) { await SyncExtensionDocumentsAsync(); await bridge.SendAsync(JsonSerializer.Serialize(new { type = "execute", command = id, args = Array.Empty<object>() })); } })));
        }
        else if (type is "log" or "error" or "activated" or "result") Notify("Extension host: " + (root.TryGetProperty("message", out var message) ? message.GetString() : root.ToString()));
        else if (type == "request")
        {
            var id = root.GetProperty("id").GetInt32(); object? result = null; string? error = null;
            try
            {
                var method = root.GetProperty("method").GetString(); var args = root.TryGetProperty("params", out var p) ? p : default;
                string PathArg() => WorkspacePath(args.GetProperty("uri").GetString()!);
                switch (method)
                {
                    case "window.showInformationMessage": case "window.showWarningMessage": case "window.showErrorMessage": Notify(args.GetProperty("message").GetString() ?? ""); break;
                    case "window.showTextDocument": var path = PathArg(); Open(path); result = new { uri = WorkspaceUri(path) }; break;
                    case "workspace.readFile": result = Convert.ToBase64String(Encoding.UTF8.GetBytes(_workspace.Files[PathArg()].Buffer.ToString())); break;
                    case "workspace.writeFile":
                        var writePath = PathArg(); var text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(args.GetProperty("data").GetString()!));
                        if (_workspace.Files.ContainsKey(writePath)) SessionFor(writePath).Apply([new(0, _workspace.Files[writePath].Buffer.Length, text)]); else _workspace.Add(writePath, text);
                        result = true; break;
                    case "workspace.applyEdits": WorkspaceEditTransaction.Apply([ParseEditBatch(args)]); result = true; break;
                    case "workspace.applyWorkspaceEdit": WorkspaceEditTransaction.Apply(args.GetProperty("documents").EnumerateArray().Select(ParseEditBatch)); result = true; break;
                    case "window.setSelections":
                        var selectionSession = SessionFor(PathArg());
                        if (args.GetProperty("version").GetInt64() != selectionSession.Version + 1) throw new InvalidOperationException("Stale selection update.");
                        selectionSession.SetSelections(args.GetProperty("selections").EnumerateArray().Select(v => new Selection(v.GetProperty("anchor").GetInt32(), v.GetProperty("active").GetInt32())));
                        result = true; break;
                    case "configuration.update": await UpdateExtensionConfigurationAsync(args); result = true; break;
                    case "commands.executeCommand": var command = args.GetProperty("command").GetString()!; result = _commands.Execute(command); break;
                    case "window.createOutputChannel": case "window.appendOutput": Log(args.TryGetProperty("text", out var output) ? output.GetString() ?? "" : args.ToString()); break;
                    default: throw new NotSupportedException("Unimplemented extension bridge method: " + method);
                }
            }
            catch (Exception exception) { error = exception.Message; }
            if (_platform.ExtensionBridge is { } bridge) { await bridge.SendAsync(JsonSerializer.Serialize(new { type = "response", id, result, error })); await SyncExtensionDocumentsAsync(); }
        }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _recoveryTimer.Stop(); _refreshTimer.Stop(); _extensionSyncTimer.Stop(); foreach (var request in _featureRequests.Values) request.TrySetCanceled(); _featureRequests.Clear(); _workspace.Changed -= WorkspaceChanged; _layout.Changed -= LayoutChanged; _tree.Workspace = null;
        foreach (var editor in _editors) editor.Dispose();
        if (_platform.ExtensionBridge is { } bridge) bridge.MessageReceived -= ExtensionMessage;
    }
}
