# CodeSpace

**A custom, modular developer workbench for Uno Platform.**

[![Build and test](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

[**Open the browser workbench**](https://wieslawsoltes.github.io/CodeSpace/) · [Architecture](docs/architecture.md) · [Compatibility matrix](docs/compatibility.md) · [Build and release](docs/building.md)

CodeSpace brings the familiar VS Code-style activity bar, explorer, command center, editor groups and bottom panel to a native **Uno Platform / C#** application. The editor, highlighting, file tree, icons, docking model and composition are custom-built. No Monaco, Electron, embedded VS Code workbench or screenshot-based UI is used.

> **0.1.0 development preview.** This is not a complete or pixel-exact clone of Visual Studio Code. Existing extensions work only where their requirements fit the implemented API subset. Debugging, language servers, Git operations, real process terminals, TextMate grammars and broad extension contributions are not yet integrated. The [compatibility matrix](docs/compatibility.md) is part of the product specification, not fine print.

## What is implemented

**Editing:** persistent AVL-rope text storage; indexed line navigation; structurally shared undo/redo; multiple selections; grapheme-aware deletion; autoindent; line comments; duplicate line; find/replace with regular expressions; next-occurrence selection; word/keyword suggestions; declaration-symbol navigation; line numbers; selection and caret painting; minimap; horizontal and vertical scrolling; zoom and whitespace visualization.

**Workbench:** custom vector activity icons; virtualized explorer; file create/import/rename/delete/export; open tabs with dirty markers; draggable tab reordering and moves; nested horizontal/vertical editor splits; resizable side and bottom panels; fuzzy command palette and file picker; breadcrumbs; active-document status; workspace text search; explicit save dialogs; local recovery and portable JSON workspace backups.

**Language layer:** built-in lexical profiles for C#, JavaScript, TypeScript, JSON, Python, HTML, XML/XAML, CSS, Markdown, YAML, shell scripts and plain text. Stateful token caching, bracket diagnostics, brace folding ranges and declaration extraction are available as independent APIs. These are lexical services, not compiler-backed IntelliSense.

**Extension foundation:** bounded local VSIX inspection, manifest/host classification, explicit activation trust, CommonJS loading, a browser worker, a Node process host, selected `vscode` value types, commands, document snapshots/events, single-document edits, virtual-file read/write, simple notifications and output logging. A bundled unmodified-API fixture exercises `require('vscode')`, `activate(context)`, `context.subscriptions`, `registerCommand` and `showInformationMessage`.

## Technology and rendering

| Component | Pinned baseline |
|---|---|
| .NET | 10; SDK feature-band roll-forward enabled |
| Uno SDK | **6.7.30**, latest stable verified when the project was created |
| Uno graphics | **6.7.135**, resolved by Uno SDK |
| SkiaSharp | **3.119.2**, aligned with Uno's native runtime |
| Extension process | Node.js 22 or newer, optional on desktop |
| License | MIT for CodeSpace; retain all third-party notices |

`SKCanvasElement` draws directly into Uno's existing Skia composition canvas. The custom editor does not render into a CPU bitmap and upload it on each frame. The actual graphics backend is selected by Uno/Skia for the platform and device; software fallback remains possible. This project does not contain a separate Vulkan/Metal/WebGPU engine and does not claim a measured fastest-in-class result.

The editor lays out and paints the visible viewport, caches line layouts and uses indexed persistent text edits. Dirty tracking compares saved structural snapshots instead of flattening the whole document on every keystroke. A regression gate measures allocation for 100 small edits in a two-million-character document. Native frame timing, battery usage, huge-line behavior and physical GPU performance still require qualification.

## Independently reusable packages

| Package | Responsibility | UI dependency |
|---|---|---|
| `CodeSpace.Core` | Persistent text buffer, virtual workspace, commands | None |
| `CodeSpace.Editor` | Editing transactions, selections, navigation, history | None |
| `CodeSpace.Docking` | Immutable split/tab layout and serialization | None |
| `CodeSpace.Languages` | Custom tokenizer and lexical services | None |
| `CodeSpace.Extensions` | VSIX inspection, compatibility reporting, RPC framing | None |
| `CodeSpace.Rendering.Skia` | Custom editor renderer and viewport model | SkiaSharp only |
| `CodeSpace.Controls.Uno` | Editor, tree, tabs, splitter, activity bar, quick pick | Uno |
| `CodeSpace.Workbench.Uno` | Composable workbench and platform adapter contract | Uno |
| `@codespace/extension-host` | CommonJS browser/Node extension runtime | None |

NuGet and npm archives are built as CI/release artifacts. No packages are automatically published to public registries. The application is a consumer of the libraries, not a required dependency of the engines.

```csharp
using CodeSpace.Core;
using CodeSpace.Editor;

var workspace = new Workspace { Name = "example" };
var file = workspace.Add("src/hello.cs", "class Hello {}\n");
var editor = new EditorSession(file);
editor.Select(6, 11);
editor.Insert("World");
editor.Undo();
```

A renderer host can call `EditorRenderer.Draw(SKCanvas, SKRect, EditorSession, EditorViewport)` on its own canvas. A Uno application can embed `CodeEditorControl` or the whole `WorkbenchView`, supplying its own `IWorkbenchPlatform` implementation for storage and extension transport.

## Build and run

Install .NET 10. No IDE is required for engine tests.

```sh
# Portable libraries and regression suites
dotnet build CodeSpace.slnx -c Release
dotnet run --project tests/CodeSpace.Tests -c Release
dotnet run --project tests/CodeSpace.ProtocolTests -c Release
npm test --prefix src/CodeSpace.ExtensionHost

# Desktop: Windows, macOS or Linux
dotnet run --project src/CodeSpace.App -f net10.0-desktop \
  -p:CodeSpaceTargetFrameworks=net10.0-desktop

# Browser
dotnet workload install wasm-tools
dotnet publish src/CodeSpace.App -f net10.0-browserwasm -c Release \
  -p:CodeSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/CodeSpace/ -o artifacts/browser
```

See [building.md](docs/building.md) for static hosting, packaging and desktop native prerequisites. The Pages workflow runs actual Chromium input/navigation/extension smoke tests before deployment. Screenshots and logs are downloadable from the workflow run. Headless Chromium's software graphics path is not a physical-GPU benchmark.

## Everyday workflows

Open **File → Open / Import Files** to import UTF-8 text. Imports populate a virtual workspace: they do not attach a watched OS folder. Saving opens an explicit file picker/download. **Workspace: Export Backup** preserves all virtual files in a `.codespace.json` file. Browser-local recovery is convenience storage, not a backup or synchronization service.

| Shortcut | Action |
|---|---|
| `Ctrl+P` | Go to file |
| `Ctrl+Shift+P` / `F1` | Command palette |
| `Ctrl+S` | Save/export active file |
| `Ctrl+F` / `Ctrl+H` | Find / replace |
| `Ctrl+D` | Add next matching selection |
| `Ctrl+Space` | Word/keyword suggestions |
| `Ctrl+G` | Go to line/column |
| `Ctrl+\\` | Split editor right |
| `Ctrl+B` / `Ctrl+J` | Toggle side bar / panel |
| `Ctrl+mouse wheel` | Editor font size |
| `Shift+mouse wheel` | Horizontal editor scroll |

Browser- or OS-reserved shortcuts may need the equivalent menu command. Platform-specific command-key behavior and IME composition are not fully qualified.

## Extensions: read this before installing

Use **Extensions → Install from VSIX** to inspect a package. Inspection does not execute it. **Review and activate** asks for explicit trust. A browser extension needs a `browser` entry point and browser-compatible dependencies; a desktop `main` extension needs the optional Node host. Microsoft Marketplace is not contacted.

**Extension code is trusted executable code, not sandboxed content.** A worker can access the network and workspace content exposed to it. A Node host has local process authority. VSIX signatures and publisher identity are not independently verified. Dependencies are not downloaded automatically. The built-in fixture is a narrow compatibility test, not evidence that arbitrary third-party extensions work.

Unsupported API accesses throw clear errors. Some implemented-shaped APIs have deliberately limited semantics, documented in [compatibility.md](docs/compatibility.md). A static GitHub Pages deployment cannot run local native programs, spawn a Node process, attach to arbitrary debuggers or provide a server-backed remote filesystem.

## Project quality and contribution

CI builds the portable engines and desktop targets, runs deterministic and randomized regression tests, checks the extension host independently, publishes the WebAssembly application, and runs browser interaction tests. Tagged releases produce application/library archives and checksums. Read [CONTRIBUTING.md](CONTRIBUTING.md) and [SECURITY.md](SECURITY.md) before extending execution or package-installation paths.

CodeSpace is not affiliated with Microsoft Visual Studio Code or GitHub Codespaces. Visual Studio Code is a Microsoft trademark. The UI is independently implemented and uses original vector icons; no Microsoft logo, proprietary product asset or extension marketplace content is bundled.

### Primary references

[Uno 6.7](https://platform.uno/blog/uno-platform-6-7/) · [Uno SDK package](https://www.nuget.org/packages/Uno.Sdk/6.7.30) · [SKCanvasElement](https://platform.uno/docs/articles/controls/SKCanvasElement.html) · [VS Code extension hosts](https://code.visualstudio.com/api/advanced-topics/extension-host) · [Web extensions](https://code.visualstudio.com/api/extension-guides/web-extensions) · [VS Code FAQ / distribution and Marketplace](https://code.visualstudio.com/docs/supporting/faq)
