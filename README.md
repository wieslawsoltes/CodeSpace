# CodeSpace

**A custom, modular developer workbench for Uno Platform.**

[![Build and test](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/CodeSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

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

The .NET libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=CodeSpace), e.g. `dotnet add package CodeSpace.Core`.

## Independently reusable packages

| Package | Responsibility | UI dependency |
|---|---|---|
| CodeSpace.Core | Persistent text, virtual workspace, commands, configuration | None |
| CodeSpace.Editor | Transactions, selections, history, folding indexes | None |
| CodeSpace.Docking | Immutable split/tab layouts and persistence | None |
| CodeSpace.Languages | Tokenizer and lexical services | None |
| CodeSpace.Extensions | VSIX inspection, compatibility reports, RPC framing | None |
| CodeSpace.Rendering.Skia | Editor renderer and viewport | SkiaSharp |
| CodeSpace.Controls.Uno | Editor, tree, tabs, splitter, activity bar, quick pick | Uno |
| CodeSpace.Workbench.Uno | Composable workbench and platform adapter contract | Uno |
| @codespace/extension-host | Browser/Node extension runtime and provider adapters | None |

CI verifies the actual contents of eight NuGet packages and one npm archive, including both target assemblies in the Uno packages. Version tags publish the eight NuGet packages (with symbols) to NuGet.org; the npm extension host is attached to the GitHub Release as an archive and is not published to the npm registry. Engines do not depend on the sample application.

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

Embed EditorRenderer on a caller-owned SKCanvas, CodeEditorControl in a Uno app, or the whole WorkbenchView with your own IWorkbenchPlatform storage/extension transport.

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
