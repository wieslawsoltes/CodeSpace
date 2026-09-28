# Compatibility and remaining boundaries

CodeSpace 0.1.0 is a **development preview**, not a complete or pixel-exact Visual Studio Code clone. An exposed API, successful compilation and passing fixture are distinct from broad third-party compatibility.

## Workbench and editor

| Area | Implemented | Remaining |
|---|---|---|
| Shell | Dark Modern-inspired menus, activity bar, explorer, command center, tabs, breadcrumbs, status and panel | Pixel-reference qualification, exact fonts/icons, native title bars, complete themes and responsive parity |
| Text | Persistent rope, line index, atomic batches, undo/redo, multiple selections, grapheme navigation | Extreme memory-churn qualification, huge lines, undo coalescing, full virtual-space/keyboard box selection |
| Editing | Find/regex replace, comments, indent, duplicate line, next selection, rectangular pointer selection, extension/word suggestions, version-checked atomic multi-file text transactions | Snippets, structural editing, refactoring, diff/merge editor, complete keybinding/when-clause semantics |
| Typography/input | Cluster-aware navigation and measured glyph placement | Contextual shaping, bidi, fallback, ligatures, full IME and document accessibility |
| Language services | Twelve custom lexical profiles, keyword/document suggestions, regex symbols, bracket diagnostics, indexed folding UI and completion/hover/definition/symbol/format providers | TextMate/Oniguruma, semantic tokens, LSP transport/client integration, signature help, references/refactoring and complete provider-option semantics |
| Docking | Nested horizontal/vertical splits, resizable panels, tab drag/reorder/move, layout persistence | Floating/native/browser windows, every drop target, preview tabs and full editor history |
| Files | Virtual UTF-8 files, create/import/rename/delete/save/export, backup/recovery | Watched folders, remote providers, binary editors, encoding detection, multi-root and conflicts |
| Source control | Local unsaved-document list | Actual Git operations, status, staging, commit/history/diffs/authentication |
| Terminal | Labeled virtual workspace shell: help/ls/pwd/cat/open/echo/clear | OS process/PTY terminal, ANSI rendering, shell integration, tasks |
| Debug | Local gutter markers, clearly unconnected debug panel | DAP sessions, active breakpoints, launch/attach, stepping, watches/evaluation |
| Persistence | Content/layout/selection/viewport/fold and dirty-baseline recovery, user settings, workspace backup | History persistence, language/resource-specific settings, cloud sync, fault-injection qualification |
| Accessibility | Names/roles on workbench actions and input bridge | Complete editor automation and keyboard/screen-reader tree support |

The JSONC `.vscode/settings.json` file binds font size, tab size, minimap, whitespace and line-number visibility live. Invalid intermediate JSON retains the last valid configuration. UI switches rewrite workspace settings (comments/formatting are not preserved by UI or extension updates). User and workspace values persist in recovery; language/resource-scoped overrides are explicitly unsupported. Browser recovery is subject to localStorage quota and is not a backup. Closing without export retains virtual workspace content rather than reproducing all VS Code discard semantics.

## Extension hosts

| Capability | Browser | Desktop |
|---|---|---|
| Local VSIX inspection | Bounded, non-executing | Same |
| CommonJS browser entry | Module worker, after trust | Runtime available; main preferred |
| Node main entry | Not available in static browser | Optional installed Node.js 22+ process |
| Relative bundled JS/JSON | Supported | Supported |
| npm resolution / native dependencies | Limited bundled dependencies only | Limited Node fallback; no dependency installation or native qualification |
| ESM entry points | Not implemented | Not implemented |
| Marketplace / Open VSX integration | Not implemented | Not implemented |
| Publisher/signature verification | Not implemented | Not implemented |
| Untrusted-code sandbox | **No** | **No** |
| Universal existing-extension compatibility | **No** | **No** |

The manifest's browser entry is necessary but insufficient: API, contribution, dependency and lifecycle requirements must fit this host. Full engines.vscode semver enforcement, dependency activation and all activation events are not implemented. A static Pages site cannot itself run native programs, a Node service, language servers, remote Git or debug adapters.

## vscode API subset

| Surface | Behavior |
|---|---|
| Disposable/EventEmitter | Basic lifetime/listener semantics |
| Position/Range/Selection | UTF-16 value types and comparisons |
| Uri | Basic parsing/combining/serialization, not complete cross-platform URI semantics |
| TextEdit/WorkspaceEdit | Version-checked atomic text-only multi-document edits. Create/delete/rename entries and grouped cross-document Undo are not implemented; Undo histories remain per document. Invalid/stale edit requests reject with a bridge error |
| commands | Registration, invocation, disposal and selected workbench bridging; built-in IDs are protected from extension replacement |
| Documents | Virtual snapshots, text/line/position/word APIs; open only existing workspace documents |
| Document events | Live debounced changed-document snapshots, including dirty changes and previous full-document ranges. Unchanged documents are retained; per-character delta transport and filesystem watches remain open |
| workspace.fs | Text read/write, basic directory view; stat timestamps unknown/zero; no full watch/rename/delete API |
| Configuration | Default/user/workspace get/has/inspect/update and change events; object merging in the extension facade. Language/resource scopes and workspace-folder targets explicitly reject |
| activeTextEditor/showTextDocument | Stable editor adapters with synchronized selection values and version-checked selection updates; multiple view columns and full reveal/options semantics remain open |
| TextEditor.edit | Version-checked single-document edits; incomplete undo options |
| Notifications | Simple status/output messages; action items reject |
| Output channels | Shared log; independent clear/visibility/channel UI incomplete |
| Context | Subscriptions and in-memory mementos; no secrets/global persistence/storage URI/settings sync |
| extensions | Activated packages in this host |
| Language providers | Completion, hover, definition, document symbols, formatting and diagnostic collections bridged to Uno. Cancellation and stale-result checks. Semantic tokens, resolve-completion, snippets, inlay hints, signature help, references and LSP integration remain open |
| Webviews/custom editors/tree views/SCM/terminals/tasks/debugger | Not implemented |
| Declarative contributions | Inspected; theme/snippet/grammar/menu/keybinding installation not integrated |

Unknown API properties throw an explicit error. Known-shaped APIs can still have partial semantics as listed above. The exposed version is the **1.90.0 API-shape baseline**, not a claim that the entire API exists.

Completion is explicitly invoked, not automatically triggered on every character. Hover is displayed as plain text in a dialog, not a Markdown popup. The probe formatter removes trailing whitespace; it is not a C# compiler formatter. Provider results are version-checked, but full provider priority/resolution/options, every document selector, and every diagnostic range/lifecycle behavior are not qualified.

The bundled standard-API fixture verifies activation, commands, notifications and selected language providers. Tests cover delta snapshots, atomic multi-file edit requests, configuration precedence/events, language-provider selection/cancellation/disposal, diagnostics, relative modules, trust rejection, deactivation and RPC. No broad third-party extension corpus has been qualified.

## Rendering qualification

The positioned-text fast path batches ASCII glyphs with cached advances and retains a scalar path for other text. CI compares raster pixels and draw calls between both paths, with runner measurements retained as artifacts. This does not supply contextual Unicode shaping, bidi or a new GPU backend. For line-preserving edits, an incremental lexer reuses unchanged suffixes after lexical-state convergence; newline/history changes invalidate conservatively. Lexer caches still grow with scanned prefixes; huge-file cold lexing and extraordinarily long rendered lines need further work. Folding clears conservatively after edits and is rebuilt automatically for smaller files or explicitly by commands. See [performance.md](performance.md) for measured scope.

## Save and recovery semantics

Caret, fold and scroll changes schedule recovery even without text edits. Selections restore before the final viewport restoration, avoiding caret-visibility logic overwriting a deliberately scrolled view. Recovery writes are serialized to prevent overlapping writes to the same backing file. Explicit file saves mark the captured buffer snapshot as saved: changes made while a picker or write is pending remain dirty. Recovery still serializes the full virtual workspace, is subject to browser quota, and is not crash-proof backup or continuous cloud storage.

## Next parity milestones

Complete shaping/input/accessibility; richer folding/editing semantics; LSP and semantic language features; declarative extension contributions and real-package qualification; filesystem/Git/PTY/DAP backends; extension lifecycle/storage/settings; and visual/interaction qualification across desktop platforms and browsers. These are remaining implementation work, not simulated successes.
