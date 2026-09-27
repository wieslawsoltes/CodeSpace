# Compatibility and remaining boundaries

CodeSpace 0.1.0 is a **development preview**, not a complete or pixel-exact Visual Studio Code clone. An exposed API, successful compilation and passing fixture are distinct from broad third-party compatibility.

## Workbench and editor

| Area | Implemented | Remaining |
|---|---|---|
| Shell | Dark Modern-inspired menus, activity bar, explorer, command center, tabs, breadcrumbs, status and panel | Pixel-reference qualification, exact fonts/icons, native title bars, complete themes and responsive parity |
| Text | Persistent rope, line index, atomic batches, undo/redo, multiple selections, grapheme navigation | Extreme memory-churn qualification, huge lines, undo coalescing, complete box selection |
| Editing | Find/regex replace, comments, indent, duplicate line, next selection, word suggestions | Snippets, structural editing, refactoring, diff/merge editor, complete keybinding/when-clause semantics |
| Typography/input | Cluster-aware navigation and measured glyph placement | Contextual shaping, bidi, fallback, ligatures, full IME and document accessibility |
| Language services | Twelve custom lexical profiles, keyword/document suggestions, regex symbols, bracket diagnostics, brace folding ranges | TextMate/Oniguruma, semantic tokens, LSP/compiler services, hover/definition/reference/refactor, folding UI |
| Docking | Nested horizontal/vertical splits, resizable panels, tab drag/reorder/move, layout persistence | Floating/native/browser windows, every drop target, preview tabs and full editor history |
| Files | Virtual UTF-8 files, create/import/rename/delete/save/export, backup/recovery | Watched folders, remote providers, binary editors, encoding detection, multi-root and conflicts |
| Source control | Local unsaved-document list | Actual Git operations, status, staging, commit/history/diffs/authentication |
| Terminal | Labeled virtual workspace shell: help/ls/pwd/cat/open/echo/clear | OS process/PTY terminal, ANSI rendering, shell integration, tasks |
| Debug | Local gutter markers, clearly unconnected debug panel | DAP sessions, active breakpoints, launch/attach, stepping, watches/evaluation |
| Persistence | Content/layout recovery and workspace backup | Full settings/history/dirty-metadata restoration, cloud sync, fault-injection qualification |
| Accessibility | Names/roles on workbench actions and input bridge | Complete editor automation and keyboard/screen-reader tree support |

The editable `.vscode/settings.json` file is not automatically bound to the workbench. UI setting switches apply immediately, but recovery does not yet restore all editor settings. Browser recovery is subject to localStorage quota and is not a backup. Closing without export retains virtual workspace content rather than reproducing all VS Code discard semantics.

## Extension hosts

| Capability | Browser | Desktop |
|---|---|---|
| Local VSIX inspection | Bounded, non-executing | Same |
| CommonJS `browser` entry | Module worker, after trust | Runtime available; `main` preferred |
| Node `main` entry | Not available in static browser | Optional installed Node.js 22+ process |
| Relative bundled JS/JSON | Supported | Supported |
| npm resolution / native dependencies | Limited bundled dependencies only | Limited Node fallback; no dependency installation or native qualification |
| ESM entry points | Not implemented | Not implemented |
| Marketplace / Open VSX integration | Not implemented | Not implemented |
| Publisher/signature verification | Not implemented | Not implemented |
| Untrusted-code sandbox | **No** | **No** |
| Universal existing-extension compatibility | **No** | **No** |

The manifest's browser entry is necessary but insufficient: API, contribution, dependency and lifecycle requirements must fit this host. Full `engines.vscode` semver enforcement, dependency activation and all activation events are not implemented. A static Pages site cannot itself run native programs, a Node service, language servers, remote Git or debug adapters.

## `vscode` API subset

| Surface | Behavior |
|---|---|
| Disposable/EventEmitter | Basic lifetime/listener semantics |
| Position/Range/Selection | UTF-16 value types and comparisons |
| Uri | Basic parsing/combining/serialization, not complete cross-platform URI semantics |
| TextEdit/WorkspaceEdit | Single-document edits; multi-document atomic edits explicitly rejected |
| commands | Registration, invocation, disposal and selected workbench bridging |
| Documents | Virtual snapshots, text/line/position/word APIs; open only existing workspace documents |
| Document events | Emitted on snapshot synchronization, not continuous incremental streaming |
| workspace.fs | Text read/write, basic directory view; stat timestamps unknown/zero; no full watch/rename/delete API |
| Configuration | Caller defaults; persistent updates explicitly reject |
| activeTextEditor/showTextDocument | Basic adapter; selection values not synchronized with Uno selection |
| TextEditor.edit | Single-document edits; incomplete undo/formatting options |
| Notifications | Simple status/output messages; action items reject |
| Output channels | Shared log; independent clear/visibility/channel UI incomplete |
| Context | Subscriptions and in-memory mementos; no secrets/global persistence/storage URI/settings sync |
| extensions | Activated packages in this host |
| Language/diagnostic/semantic providers | Not implemented |
| Webviews/custom editors/tree views/SCM/terminals/tasks/debugger | Not implemented |
| Declarative contributions | Inspected; theme/snippet/grammar/menu/keybinding installation not integrated |

Unknown API properties throw an explicit error. Known-shaped APIs can still have partial semantics as listed above. The exposed `version` is the **1.90.0 API-shape baseline**, not a claim that the entire API exists.

The bundled unmodified-API hello fixture verifies `require('vscode')`, `activate(context)`, command registration, context subscriptions and an information message. Additional tests cover snapshots, single-file edit requests, relative modules, trust rejection, deactivation and RPC. No broad third-party extension corpus has been qualified.

## Next parity milestones

Complete shaping/input/accessibility; folding and richer editor semantics; LSP and semantic language features; declarative extension contributions and real-package compatibility qualification; filesystem/Git/PTY/DAP backends; extension lifecycle/storage/settings; and visual/interaction qualification across desktop platforms and browsers. These are remaining implementation work, not simulated successes.
