using CodeSpace.Core;
using CodeSpace.Extensions;

namespace CodeSpace.Workbench.Uno;

public sealed record ImportedFile(string Path, byte[] Bytes);
public interface IWorkbenchPlatform
{
    bool IsBrowser { get; }
    Task<IReadOnlyList<ImportedFile>> PickFilesAsync(string extension = "*");
    Task SaveFileAsync(string name, byte[] content);
    Task<string?> LoadRecoveryAsync();
    Task SaveRecoveryAsync(string workspaceJson);
    IExtensionBridge? ExtensionBridge { get; }
}
public static class SampleWorkspace
{
    public static Workspace Create()
    {
        var workspace = new Workspace { Name = "CODESPACE" };
        workspace.Add("src/Program.cs", "using System;\nusing CodeSpace.Core;\nusing CodeSpace.Editor;\n\nnamespace CodeSpace.Demo;\n\n/// <summary>\n/// A developer workbench, built for every screen.\n/// </summary>\npublic sealed class Program\n{\n    private readonly Workspace workspace = new();\n\n    public void Run()\n    {\n        var file = workspace.Add(\"hello.cs\", \"Hello, CodeSpace!\");\n        var editor = new EditorSession(file);\n\n        // Custom editor. Persistent text. GPU composition.\n        editor.Select(file.Buffer.Length, file.Buffer.Length);\n        editor.Insert(\" Welcome aboard.\");\n\n        Console.WriteLine(editor.Buffer);\n    }\n}\n");
        workspace.Add("src/Workbench.cs", "namespace CodeSpace.Demo;\n\npublic record WorkbenchOptions(\n    string Theme = \"Dark Modern\",\n    int FontSize = 14,\n    bool Minimap = true);\n\npublic sealed class Workbench\n{\n    public WorkbenchOptions Options { get; } = new();\n\n    public string[] Panels =>\n    [\n        \"Explorer\",\n        \"Search\",\n        \"Extensions\",\n        \"Output\"\n    ];\n}\n");
        workspace.Add("src/theme.json", "{\n  \"name\": \"CodeSpace Dark Modern\",\n  \"colors\": {\n    \"editor.background\": \"#1f1f1f\",\n    \"sideBar.background\": \"#181818\",\n    \"activityBar.background\": \"#181818\",\n    \"focusBorder\": \"#0078d4\"\n  },\n  \"editor.fontSize\": 14,\n  \"editor.tabSize\": 4,\n  \"editor.minimap.enabled\": true\n}\n");
        workspace.Add("extensions/hello/extension.js", "const vscode = require('vscode');\n\nexports.activate = (context) => {\n  const command = vscode.commands.registerCommand(\n    'codespace.hello',\n    () => vscode.window.showInformationMessage(\n      'Hello from an unmodified VS Code extension entry point!'\n    )\n  );\n\n  context.subscriptions.push(command);\n};\n\nexports.deactivate = () => {};\n");
        workspace.Add("extensions/hello/package.json", "{\n  \"name\": \"hello\",\n  \"publisher\": \"codespace\",\n  \"displayName\": \"Hello CodeSpace\",\n  \"version\": \"0.1.0\",\n  \"engines\": { \"vscode\": \"^1.90.0\" },\n  \"browser\": \"./extension.js\",\n  \"main\": \"./extension.js\",\n  \"license\": \"MIT\",\n  \"contributes\": {\n    \"commands\": [{\n      \"command\": \"codespace.hello\",\n      \"title\": \"Hello from extension\"\n    }]\n  }\n}\n");
        workspace.Add(".vscode/settings.json", "{\n  \"editor.fontSize\": 14,\n  \"editor.tabSize\": 4,\n  \"editor.minimap.enabled\": true,\n  \"files.autoSave\": \"off\"\n}\n");
        workspace.Add("README.md", "# Welcome to CodeSpace\n\nA custom Uno Platform developer workbench.\n\n## Start editing\n\n- Ctrl+P: open a file\n- Ctrl+Shift+P or F1: command palette\n- Ctrl+S: save / download the active file\n- Ctrl+F: find and replace\n- Ctrl+D: select the next matching occurrence\n- Ctrl+\\: split the editor\n- Ctrl+B: toggle the sidebar\n- Ctrl+J: toggle the bottom panel\n\n## Extension compatibility\n\nRun **Extensions: Run bundled compatibility probe** from the command palette.\nThe fixture uses the VS Code extension activation and command APIs.\nOnly the documented API subset is supported; arbitrary extensions, language\nservers, debuggers, remote services and Marketplace access are not implemented.\n\nYour workspace is recovered locally in this browser. Export it to keep a backup.\n");
        workspace.Add("LICENSE", "MIT License\n\nCopyright (c) 2026 CodeSpace contributors\n");
        return workspace;
    }
}
