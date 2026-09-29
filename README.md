# CodeSpace

**A custom, modular developer workbench for Uno Platform.**

[![Build and test](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/CodeSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Core.svg)](https://www.nuget.org/packages/CodeSpace.Core)

[**Open the browser workbench**](https://wieslawsoltes.github.io/CodeSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Performance measurements](docs/performance.md) · [Build and release](docs/building.md)

CodeSpace brings the familiar VS Code-style activity bar, explorer, command center, editor groups and bottom panel to a native **Uno Platform / C#** application. The editor, highlighting, file tree, icons, docking model and composition are custom-built. No Monaco, Electron, embedded VS Code workbench or screenshot-based UI is used.

> **0.1.0 development preview.** This is not a complete or pixel-exact clone of Visual Studio Code. Existing extensions work only where their requirements fit the implemented API subset. Debugging, language servers, Git operations, real process terminals, TextMate grammars and broad extension contributions are not yet integrated. Selected extension language-provider APIs are connected to the custom editor; this is not an LSP client or universal extension host. The [compatibility matrix](docs/compatibility.md) states the remaining boundaries.

## Implemented workflows

**Editing:** folding/unfolding with indexed visual rows; Alt+Shift rectangular pointer selection; version-checked atomic multi-document text edits; persistent AVL-rope text storage; indexed line navigation; structurally shared undo/redo; multiple selections; grapheme-aware deletion; autoindent; comments; duplicate line; literal/regex find and replace; next-occurrence selection; suggestions; symbol navigation; line numbers; caret/selection painting; minimap; scrolling; zoom and whitespace visualization.

**Workbench:** custom vector activity icons; virtualized explorer; file create/import/rename/delete/export; dirty tab markers; draggable tab reordering and moves; nested horizontal/vertical editor splits; resizable side/bottom panels; fuzzy commands and quick-open; breadcrumbs; document status; workspace search; explicit save dialogs; local recovery with dirty baselines, selections and viewport/fold state; live JSONC settings; portable workspace backups.

**Languages:** lexical profiles for C#, JavaScript, TypeScript, JSON, Python, HTML, XML/XAML, CSS, Markdown, YAML, shell scripts and plain text. Stateful tokens, bracket diagnostics, folding ranges and declaration extraction are independent APIs. They are lexical services, not compiler-backed IntelliSense.

**Extensions:** bounded VSIX inspection, manifest/host classification, explicit activation trust, bundled CommonJS loading, browser worker and Node process hosts; selected vscode value types and commands; live changed-document snapshots; synchronized selections; user/workspace configuration; atomic text-only multi-document edits; completion/hover/definition/symbol/format providers; diagnostic collections with Problems entries and editor squiggles; virtual-file read/write; simple notifications and output. The bundled standard-API fixture exercises real activation and provider paths without modifying the extension source's API calls.

## Technology and rendering

| Component | Pinned baseline |
|---|---|
| .NET | 10; SDK feature-band roll-forward enabled |
| Uno SDK | 6.7.30 |
| Uno graphics | 6.7.135, resolved by Uno SDK |
| SkiaSharp | 3.119.2, aligned with Uno's native runtime |
| Extension process | Node.js 22+, optional on desktop |
| CodeSpace license | MIT; retain third-party notices |

SKCanvasElement draws into Uno's existing Skia composition canvas, without a per-frame CPU bitmap upload. Uno/Skia select the actual graphics backend; software fallback is possible. CodeSpace does not contain a separate Vulkan/Metal/WebGPU engine or establish a fastest-in-class result.

The renderer caches up to 512 line layouts with positioned-text batches. Line metadata and position lookup do not materialize line strings. ASCII advances and minimap samples are cached; non-ASCII clusters retain the scalar fallback. Fold mapping uses storage proportional to collapsed intervals. Dirty tracking compares structural snapshots instead of flattening text on each keystroke.

Regression gates cover two-million-character documents, zero-allocation indexed navigation, cached layout reuse, raster equivalence and draw-call reduction. An initial same-process CPU/raster fixture measured approximately **9.74× less rendering time** and **74.3% fewer text calls**, with zero differing pixels. This is not a whole-IDE or physical-GPU speedup claim. See [methodology and original run](docs/performance.md); each CI run produces its own performance.json.

## Download

Every [release](https://github.com/wieslawsoltes/CodeSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `CodeSpace-<version>-win-x64.zip` | `CodeSpace-<version>-win-arm64.zip` |
| macOS | `CodeSpace-<version>-osx-x64.tar.gz` | `CodeSpace-<version>-osx-arm64.tar.gz` |
| Linux | `CodeSpace-<version>-linux-x64.tar.gz` | `CodeSpace-<version>-linux-arm64.tar.gz` |

Extract and run `CodeSpace` (`CodeSpace.exe` on Windows). Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine CodeSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS.txt`. Node.js is optional (for the local extension host) and not bundled.

## NuGet packages

CodeSpace ships as eight MIT-licensed .NET packages on [NuGet.org](https://www.nuget.org/packages?q=CodeSpace), versioned together and published by release tags with symbol packages (`.snupkg`) and SourceLink. The six engine packages target `net10.0` and have no UI dependency (only `CodeSpace.Rendering.Skia` needs SkiaSharp); the two Uno packages target `net10.0-desktop` and `net10.0-browserwasm`, and CI verifies that both target assemblies are in each package. Engines do not depend on the sample application. The JavaScript extension host (`src/CodeSpace.ExtensionHost`, `@codespace/extension-host`) is not on NuGet or the npm registry; it is attached to each GitHub Release as an archive.

Embed `EditorRenderer` on a caller-owned `SKCanvas`, `CodeEditorControl` in a Uno app, or the whole `WorkbenchView` with your own `IWorkbenchPlatform` storage and extension transport.

```sh
dotnet add package CodeSpace.Core
```

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [CodeSpace.Core](https://www.nuget.org/packages/CodeSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Core.svg)](https://www.nuget.org/packages/CodeSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Core.svg)](https://www.nuget.org/packages/CodeSpace.Core) | Persistent rope text buffers, virtual workspace, commands and JSONC configuration |
| [CodeSpace.Docking](https://www.nuget.org/packages/CodeSpace.Docking) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Docking.svg)](https://www.nuget.org/packages/CodeSpace.Docking) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Docking.svg)](https://www.nuget.org/packages/CodeSpace.Docking) | Immutable split/tab editor-group layouts and layout persistence |
| [CodeSpace.Editor](https://www.nuget.org/packages/CodeSpace.Editor) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Editor.svg)](https://www.nuget.org/packages/CodeSpace.Editor) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Editor.svg)](https://www.nuget.org/packages/CodeSpace.Editor) | Editing transactions, multi-selections, undo/redo history and folding indexes |
| [CodeSpace.Languages](https://www.nuget.org/packages/CodeSpace.Languages) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Languages.svg)](https://www.nuget.org/packages/CodeSpace.Languages) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Languages.svg)](https://www.nuget.org/packages/CodeSpace.Languages) | Incremental tokenizer and lexical services (symbols, completions, diagnostics, folding) |
| [CodeSpace.Extensions](https://www.nuget.org/packages/CodeSpace.Extensions) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Extensions.svg)](https://www.nuget.org/packages/CodeSpace.Extensions) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Extensions.svg)](https://www.nuget.org/packages/CodeSpace.Extensions) | VSIX inspection, VS Code API compatibility reports and extension-host RPC framing |
| [CodeSpace.Rendering.Skia](https://www.nuget.org/packages/CodeSpace.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/CodeSpace.Rendering.Skia) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/CodeSpace.Rendering.Skia) | SkiaSharp editor renderer and viewport: cached line layout, highlighting, selections and caret |
| [CodeSpace.Controls.Uno](https://www.nuget.org/packages/CodeSpace.Controls.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Controls.Uno.svg)](https://www.nuget.org/packages/CodeSpace.Controls.Uno) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Controls.Uno.svg)](https://www.nuget.org/packages/CodeSpace.Controls.Uno) | Uno editor, input bridge, explorer, activity bar, tabs, splitter and quick-pick controls |
| [CodeSpace.Workbench.Uno](https://www.nuget.org/packages/CodeSpace.Workbench.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/CodeSpace.Workbench.Uno.svg)](https://www.nuget.org/packages/CodeSpace.Workbench.Uno) | [![Downloads](https://img.shields.io/nuget/dt/CodeSpace.Workbench.Uno.svg)](https://www.nuget.org/packages/CodeSpace.Workbench.Uno) | Composable VS Code-style workbench and the `IWorkbenchPlatform` adapter contract |

Dependencies follow the real project references: `Core ← Editor`, `Core ← Languages`, `Core ← Extensions`, `Editor + Languages ← Rendering.Skia`, `Rendering.Skia + Docking ← Controls.Uno`, `Controls.Uno + Extensions ← Workbench.Uno`; `Docking` stands alone.

### CodeSpace.Core

The text and workspace engine: an immutable AVL-rope `TextBuffer` with indexed line lookup and structural sharing, a virtual in-memory `Workspace` with search and JSON backups, a fuzzy `CommandRegistry` and layered JSONC settings. Use it for any headless text-processing or document model. No dependencies and no UI.

```sh
dotnet add package CodeSpace.Core
```

**Key types**

- `TextBuffer` — persistent text; `Replace`/`Apply` return new buffers, plus `GetLine`, `PositionAt`, `OffsetAt`, `Slice`.
- `Workspace` / `WorkspaceFile` — virtual files with dirty tracking, `Search`, `Serialize`/`Deserialize`.
- `CommandRegistry` / `Command` — registered commands with fuzzy `Search` and `Execute`.
- `EditorConfiguration` — default/user/workspace settings resolved into `EditorOptions`.

**Usage**

```csharp
using CodeSpace.Core;

var buffer = new TextBuffer("line one\nline two\n");
TextBuffer edited = buffer.Replace(5, 3, "1");    // `buffer` still holds the original text
Console.WriteLine(edited.GetLine(0).Text);         // line 1

var workspace = new Workspace { Name = "example" };
workspace.Add("src/hello.cs", "class Hello {}\n");
foreach (var hit in workspace.Search("Hello"))
    Console.WriteLine($"{hit.Path}:{hit.Line + 1} {hit.Preview}");
var restored = Workspace.Deserialize(workspace.Serialize());

var commands = new CommandRegistry();
commands.Register(new Command("demo.hello", "Say Hello", "Ctrl+Alt+H", () => Console.WriteLine("Hello")));
commands.Execute(commands.Search("hello")[0].Id);

var settings = new EditorConfiguration();
settings.LoadWorkspace("{ \"editor.fontSize\": 16, // JSONC\n }");
EditorOptions options = settings.Options;          // FontSize = 16
```

### CodeSpace.Docking

An immutable model of editor groups: tab groups inside nested horizontal/vertical splits, side bar and panel sizes, and JSON persistence. Hosts decide how to present each node, so it can back any UI framework. No dependencies and no UI.

```sh
dotnet add package CodeSpace.Docking
```

**Key types**

- `DockLayout` — mutable façade over the current `DockState`: `Open`, `Close`, `Split`, `Move`, `Resize`, `Serialize`/`Restore`, `Changed`.
- `DockState` — root node plus side bar/panel dimensions and visibility.
- `DockNode`, `TabGroup`, `SplitNode`, `SplitAxis` — the immutable layout tree.

**Usage**

```csharp
using CodeSpace.Docking;

var layout = new DockLayout();
layout.Open("README.md");
layout.Open("src/Program.cs");
string right = layout.Split("primary", SplitAxis.Horizontal, "src/Program.cs");
layout.SetPanel(visible: false);

foreach (TabGroup group in layout.Groups)
    Console.WriteLine($"{group.Id}: {string.Join(", ", group.Tabs)} (active {group.ActiveTab})");

string json = layout.Serialize();
var restored = new DockLayout();
restored.Restore(json);
```

### CodeSpace.Editor

The editing engine for one document: multiple and rectangular selections, grapheme-aware movement and deletion, autoindent, comments, find/replace, structurally shared undo/redo, folding indexes and version-checked multi-document transactions. Use it to drive any editor surface or to script edits. Depends on `CodeSpace.Core`; no UI.

```sh
dotnet add package CodeSpace.Editor
```

**Key types**

- `EditorSession` — selections, `Insert`, `Delete`, `Move`, `Find`, `ReplaceAll`, `AddNextOccurrence`, `ToggleLineComment`, `Undo`/`Redo`, `Changed`.
- `Selection` — anchor/active pair (`Start`, `End`, `Length`).
- `FoldingState` — collapsed regions with line↔visual-row mapping.
- `WorkspaceEditTransaction` / `DocumentEditBatch` — atomic edits across several documents.

**Usage**

```csharp
using CodeSpace.Core;
using CodeSpace.Editor;

var workspace = new Workspace { Name = "example" };
var file = workspace.Add("src/hello.cs", "class Hello {}\n");
var editor = new EditorSession(file);
editor.Select(6, 11);
editor.Insert("World");
editor.Undo();

editor.Select(0, 0);
editor.AddNextOccurrence();                        // Ctrl+D-style multi-selection
int replaced = editor.ReplaceAll("Hello", "Greeter");
Console.WriteLine($"{replaced} replaced, dirty: {editor.IsDirty}");

// Version-checked edit spanning several open documents
WorkspaceEditTransaction.Apply([new DocumentEditBatch(editor, [new TextEdit(0, 0, "// header\n")], editor.Version)]);
```

### CodeSpace.Languages

Lexical language services for C#, JavaScript, TypeScript, JSON, Python, HTML, XML/XAML, CSS, Markdown, YAML, shell and plain text: a stateful tokenizer with incremental re-tokenization, plus declaration symbols, word completions, bracket diagnostics and folding ranges. These are lexical services, not compiler-backed IntelliSense. Depends on `CodeSpace.Core`; no UI.

```sh
dotnet add package CodeSpace.Languages
```

**Key types**

- `LanguageCatalog` — built-in `LanguageDefinition`s by path (`ForPath`) or id (`ForId`).
- `SyntaxLexer.Tokenize` — tokenizes a line, carrying a `LexerState` across lines.
- `IncrementalSyntaxDocument` / `SyntaxDocument` — cached per-line `TokenizedLine`s for a `TextBuffer`.
- `LanguageServices` — `Symbols`, `Complete`, `Diagnostics` and `FoldingRanges`.

**Usage**

```csharp
using CodeSpace.Core;
using CodeSpace.Languages;

var buffer = new TextBuffer("namespace Demo;\nclass Hello\n{\n    void Run() { }\n");
var syntax = new SyntaxDocument("Hello.cs");        // language chosen from the extension
foreach (SyntaxToken token in syntax.GetLine(buffer, 1).Tokens)
    Console.WriteLine($"{token.Kind}: {buffer.GetLine(1).Text.Substring(token.Start, token.Length)}");

foreach (var symbol in LanguageServices.Symbols(buffer))
    Console.WriteLine($"{symbol.Kind} {symbol.Name} (line {symbol.Line + 1})");
foreach (var problem in LanguageServices.Diagnostics(buffer, "Hello.cs"))
    Console.WriteLine($"{problem.Severity}: {problem.Message} at {problem.Line + 1}:{problem.Character + 1}");
IReadOnlyList<string> words = LanguageServices.Complete(buffer, "Ru", "csharp");
```

### CodeSpace.Extensions

Host-side VS Code extension support: bounded VSIX reading (size and entry limits, no execution), manifest parsing, host selection (browser worker, Node process, declarative) and compatibility reports, plus Content-Length JSON-RPC framing and the `IExtensionBridge` transport contract. Depends on `CodeSpace.Core`; no UI. Extensions are trusted executable code; this package only inspects them.

```sh
dotnet add package CodeSpace.Extensions
```

**Key types**

- `ExtensionPackage.Read` — reads a `.vsix` stream into a `Manifest` and bounded `Files`.
- `ExtensionManifest` — parsed `package.json` (`Parse`, `SelectHost`, `Commands`).
- `ExtensionCompatibility.Inspect` — `ExtensionCapabilityReport` with host, trust and limitations.
- `JsonRpcFraming` — `WriteAsync`/`ReadAsync` of framed JSON messages on a `Stream`.
- `IExtensionBridge` — transport implemented by hosts (worker, Node process).

**Usage**

```csharp
using CodeSpace.Extensions;

await using var vsix = File.OpenRead("hello-0.1.0.vsix");
var package = ExtensionPackage.Read(vsix);           // inspected, never executed
ExtensionManifest manifest = package.Manifest;
foreach (var command in manifest.Commands)
    Console.WriteLine($"{command.Command}: {command.Title}");

var report = ExtensionCompatibility.Inspect(manifest, browser: true);
Console.WriteLine($"{report.ExtensionId} -> {report.Host}, trust required: {report.RequiresTrust}");
foreach (var limitation in report.Limitations) Console.WriteLine("  " + limitation);

using var stream = new MemoryStream();
await JsonRpcFraming.WriteAsync(stream, new { jsonrpc = "2.0", method = "initialize", id = 1 });
```

### CodeSpace.Rendering.Skia

The editor renderer: draws an `EditorSession` onto any caller-owned `SKCanvas` with cached positioned-text line layouts, syntax colors, selections, caret, find matches, diagnostics, folding gutter, whitespace and minimap, and maps points back to text offsets. Use it to render code outside Uno (images, other UI toolkits). Depends on Editor, Languages and SkiaSharp 3.119; no UI framework.

```sh
dotnet add package CodeSpace.Rendering.Skia
```

**Key types**

- `EditorRenderer` — `Draw`, `HitTest`, `EnsureCaretVisible`, `Theme`, `Metrics`; `DefaultTypeface` for the font.
- `EditorViewport` — scroll, font size, minimap/whitespace/line-number toggles, `Folding`, `FindMatches`, `Diagnostics`.
- `EditorTheme` — background, gutter, selection and per-`TokenKind` colors.
- `RenderMetrics` — visible/cached lines, glyphs and draw calls of the last frame.

**Usage**

```csharp
using CodeSpace.Core;
using CodeSpace.Editor;
using CodeSpace.Rendering.Skia;
using SkiaSharp;

var file = new Workspace().Add("src/hello.cs", "class Hello\n{\n    void Run() { }\n}\n");
var session = new EditorSession(file);
var view = new EditorViewport { FontSize = 15, ShowMinimap = false };

using var renderer = new EditorRenderer();
using var surface = SKSurface.Create(new SKImageInfo(800, 300));
renderer.Draw(surface.Canvas, new SKRect(0, 0, 800, 300), session, view);
Console.WriteLine($"{renderer.Metrics.VisibleLines} lines, {renderer.Metrics.TextDrawCalls} text calls");

int offset = renderer.HitTest(session, view, 120, 30);   // pointer -> text offset
using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("hello.png", png.ToArray());
```

### CodeSpace.Controls.Uno

Custom Uno Platform controls built on the engines: `CodeEditorControl` (Skia editor surface with a hidden text-input bridge, find box, folding and clipboard), the virtualized `FileTreeControl`, `EditorTabs`, `ActivityBar`, `QuickPickControl`, `DockSplitter`, vector icons and colors. Use them to compose your own editor UI. Depends on Rendering.Skia and Docking; requires Uno Platform (Skia renderer).

```sh
dotnet add package CodeSpace.Controls.Uno
```

**Key types**

- `CodeEditorControl` — hosts an `EditorSession`; exposes `Viewport`, `Renderer`, `ShowFind`, `FoldAll`, `FocusEditor`, `CommandRequested`.
- `FileTreeControl` — explorer for a `Workspace`; `FileActivated` event.
- `EditorTabs` — tab strip for a `TabGroup` with activate/close/move events.
- `QuickPickControl` / `QuickPickItem` — command palette and quick-open list.
- `ActivityBar`, `DockSplitter`, `VectorIcon`, `WorkbenchColors` — workbench chrome.

**Usage**

```csharp
using CodeSpace.Controls.Uno;
using CodeSpace.Core;
using CodeSpace.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

var workspace = new Workspace();
workspace.Add("src/hello.cs", "class Hello\n{\n}\n");
var editor = new CodeEditorControl(new EditorSession(workspace.Files["src/hello.cs"]));
editor.Viewport.ShowMinimap = false;

var tree = new FileTreeControl { Workspace = workspace, Width = 240 };
tree.FileActivated += (_, path) => Console.WriteLine("open " + path);

var root = new Grid();
root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
root.ColumnDefinitions.Add(new ColumnDefinition());
root.Children.Add(tree);
root.Children.Add(editor);
Grid.SetColumn(editor, 1);

var window = new Window { Title = "Editor", Content = root };
window.Activate();
editor.FocusEditor();
```

### CodeSpace.Workbench.Uno

The complete VS Code-style workbench as a single `Grid`: activity bar, explorer, search, extensions view, editor groups, bottom panel, status bar, command palette, recovery and extension activation. Hosts supply storage, file pickers and the optional extension transport through `IWorkbenchPlatform`. Depends on Controls.Uno and Extensions; requires Uno Platform.

```sh
dotnet add package CodeSpace.Workbench.Uno
```

**Key types**

- `WorkbenchView` — the workbench; `InitializeAsync`, `Open(path, line)`, `Execute(commandId)`, `Workspace`, `Docking`, `Commands`, `StatusChanged`.
- `IWorkbenchPlatform` — file import/export, recovery storage and optional `IExtensionBridge`.
- `ImportedFile` — a picked file's path and bytes.
- `SampleWorkspace.Create` — the demo workspace shown on first start.

**Usage**

```csharp
using CodeSpace.Extensions;
using CodeSpace.Workbench.Uno;
using Microsoft.UI.Xaml;

sealed class MemoryPlatform : IWorkbenchPlatform
{
    private string? _recovery;
    public bool IsBrowser => OperatingSystem.IsBrowser();
    public Task<IReadOnlyList<ImportedFile>> PickFilesAsync(string extension = "*") => Task.FromResult<IReadOnlyList<ImportedFile>>([]);
    public Task SaveFileAsync(string name, byte[] content) => Task.CompletedTask;   // show a save picker here
    public Task<string?> LoadRecoveryAsync() => Task.FromResult(_recovery);
    public Task SaveRecoveryAsync(string workspaceJson) { _recovery = workspaceJson; return Task.CompletedTask; }
    public IExtensionBridge? ExtensionBridge => null;                              // no extension host
}

// In Application.OnLaunched:
var window = new Window { Title = "CodeSpace" };
var workbench = new WorkbenchView(new MemoryPlatform());
window.Content = workbench;
window.Activate();
await workbench.InitializeAsync();
workbench.Open("README.md");
window.Closed += (_, _) => workbench.Dispose();
```

## Build and run

```sh
dotnet build CodeSpace.slnx -c Release
dotnet run --project tests/CodeSpace.Tests -c Release
dotnet run --project tests/CodeSpace.ProtocolTests -c Release
dotnet run --project tests/CodeSpace.FeatureTests -c Release
npm test --prefix src/CodeSpace.ExtensionHost
node --test tests/browser/platform-contracts.test.mjs

# Desktop
dotnet run --project src/CodeSpace.App -f net10.0-desktop \
  -p:CodeSpaceTargetFrameworks=net10.0-desktop

# Browser
dotnet workload install wasm-tools
dotnet publish src/CodeSpace.App -f net10.0-browserwasm -c Release \
  -p:CodeSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CodeSpace/ -o artifacts/browser
```

See [building.md](docs/building.md) for serving the static output, native prerequisites and packaging. The Pages workflow runs Chromium interactions before deployment and retains screenshots/logs. Headless graphics are not physical-GPU qualification.

## Everyday use

Import UTF-8 text through **File → Open / Import Files**. Imports populate a virtual workspace, not a watched disk folder. Saving opens a file picker/download. **Workspace: Export Backup** preserves files in a .codespace.json file. Local recovery is convenience storage, not a backup or synchronization service.

| Shortcut | Action |
|---|---|
| Ctrl+P | Go to file |
| Ctrl+Shift+P / F1 | Commands |
| Ctrl+S | Save/export active file |
| Ctrl+F / Ctrl+H | Find / replace |
| Ctrl+D | Add next matching selection |
| Ctrl+Space | Extension and document completions |
| Ctrl+G | Go to line/column |
| F12 / Shift+Alt+F | Extension definition / document formatting |
| Command palette: Fold All / Unfold All | Collapse / reveal regions |
| Ctrl+backslash | Split right |
| Ctrl+B / Ctrl+J | Toggle side bar / panel |
| Ctrl+mouse wheel | Font size |
| Shift+mouse wheel | Horizontal scrolling |

OS/browser-reserved shortcuts may require the equivalent command. Full platform-specific shortcuts, IME, bidi, shaping, font fallback and screen-reader navigation remain qualification work.

## Extension trust and compatibility

**Extensions → Install from VSIX** inspects a local package without executing it. **Review and activate** requires explicit trust. Browser execution needs a compatible browser entry point; desktop main entries need optional installed Node.js. Microsoft Marketplace is not contacted.

**Extensions are trusted executable code, not sandboxed content.** Workers can access the network and exposed workspace content; Node has local process authority. Signatures and publisher identity are not independently verified. Dependencies are not downloaded automatically. The bundled fixture is not evidence that arbitrary extensions work.

Unsupported API properties throw errors; partially implemented semantics are documented. Static GitHub Pages cannot itself host a local Node process, terminal, debugger or remote filesystem. Complete LSP, TextMate, Git, PTY, DAP, webviews, snippets, declarative contribution installation and production qualification remain substantial work.

## Contribution and distribution

Read [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md). Build workflows validate libraries, desktop compilation, extension behavior and browser workflows. Tagged releases attach self-contained single-file desktop executables (Windows, macOS and Linux, x64 and arm64), browser/source archives, packages and checksums to a GitHub Release, and publish the NuGet packages with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing); executables are unsigned and not production-qualified.

CodeSpace is independent of Microsoft Visual Studio Code and GitHub Codespaces. Original simple vector icons are used; Microsoft logos, proprietary assets and Marketplace content are not bundled.

### Primary references

[Uno SDK](https://www.nuget.org/packages/Uno.Sdk/6.7.30) · [SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html) · [VS Code API](https://code.visualstudio.com/api/references/vscode-api) · [Extension hosts](https://code.visualstudio.com/api/advanced-topics/extension-host) · [Web extensions](https://code.visualstudio.com/api/extension-guides/web-extensions)
