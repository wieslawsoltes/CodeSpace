# Architecture

```text
App (desktop / browser adapters)
  └─ Workbench.Uno
       ├─ Controls.Uno
       │    ├─ Rendering.Skia → Editor → Core
       │    └─ Docking
       └─ Extensions → Core
Rendering.Skia → Languages → Core
ExtensionHost (independent JavaScript)
  ├─ browser module worker
  └─ Node stdin/stdout process
       ↕ JSON messages / IExtensionBridge
```

Six portable libraries target net10.0 without Uno dependencies. Two Uno libraries target net10.0-desktop and net10.0-browserwasm. The app consumes them. The JS host has no npm runtime dependencies. WorkbenchView is split into partial files by UI, storage, extension transport and language integration concerns.

## Text and transactions

TextBuffer is a persistent AVL rope with source-string slices of at most 2,048 UTF-16 units per leaf. Branches cache length, height and newline count; leaves index newlines. Split/join edits share unaffected subtrees with history. GetLineInfo and OffsetAt use metadata without allocating strings. Slice creates only the destination string, and CopyTo accepts caller-owned spans.

EditorSession validates batches before mutation. History stores a root and selection array, bounded to 500 snapshots. Saved-state comparison is O(1); equivalent manually retyped text remains a new edit until save or return to the saved snapshot. Grapheme navigation is above raw UTF-16 offsets. Rectangular selections are represented as multi-selection transactions.

WorkspaceEditTransaction checks all documents, duplicates, versions and edit ranges before committing any roots. All buffers change before observers are notified. Undo remains per-document, not a grouped cross-file undo manager. Resource creation/deletion/rename is outside this transaction API.

FoldingState retains collapsed regions and merged hidden intervals with prefix counts. Binary row/line lookups use space proportional to folds, not total lines. Editing clears folds conservatively. Lexical ranges rebuild after a debounce for files below 512 KiB; explicit folding commands can scan larger files.

## Rendering and input

EditorRenderer accepts a caller-owned SKCanvas. Uno SKCanvasElement supplies the existing composition surface, without a CPU bitmap upload. Uno/Skia own backend selection and fallback. The project does not contain a separate qualified Vulkan/Metal/WebGPU backend.

Up to 512 line layouts own disposable positioned-text blobs. ASCII advances are cached, batches culled, adjacent equivalent lexical tokens coalesced, minimap samples reused, and hit testing uses binary search. Non-ASCII text keeps the measured cluster path. The minimap represents line lengths. Metrics are CPU paint duration, calls and cache counts, not GPU timestamps. See performance.md for reference comparisons.

Full contextual shaping, bidi, ligatures, variable fonts, fallback and IME remain open. The browser loads the framework's licensed Open Sans fallback, not an exact VS Code monospace match. Embedders may provide a licensed SKTypeface. EditorInputBridge gives document commands priority over native TextBox history; the tiny input control never owns the full document. The file tree and vector icons also use custom Skia drawing.

## Workbench, files and recovery

DockLayout is independent of controls. Groups choose active files, multiple views share sessions, and resizes update geometry before persisting ratios. Empty groups remain valid. Floating/multi-window behavior is not implemented.

IWorkbenchPlatform supplies import/export/recovery/extension transports. Files are virtual, not attached to watched disk folders. Recovery saves content, dirty baselines, selections, viewport/folds and user settings; undo history is not saved. Browser recovery atomically replaces one localStorage value; quota/privacy failures are reported. Desktop writes a temporary file before replacement. Recovery is neither encrypted nor a backup.

EditorConfiguration accepts bounded JSONC. Built-in defaults, user settings and workspace values determine supported editor settings; invalid intermediate JSON leaves the previous configuration applied. UI/extension updates rewrite the JSON and do not preserve comments.

## Extension boundary

VSIX inspection is bounded and non-executing. Explicit trust is required for CommonJS activation in a worker or optional Node process. The limited require('vscode') facade rejects unknown properties. Configuration and language-provider adapters are separate modules; API value types are in api-types.mjs.

After activation, a 120 ms timer synchronizes changed-file snapshots, deleted URIs and selection metadata. Unchanged files and JS line indexes are retained. This is not per-character delta encoding. Requests are also synchronized before commands and after bridged edits. A semaphore serializes outgoing snapshot construction.

Language requests carry IDs and document versions. Cancellation/UI responses bypass serialized activation operations to prevent deadlock. The workbench rejects stale results and times out requests. Completions use the custom QuickPick, hover is non-executable plain text, definitions navigate files, formatting is one undoable batch and diagnostic collections update squiggles/Problems. No general LSP client or language server is supplied.

A worker isolates work from the UI thread, not malicious code from data/network access. Node has user process authority. The hosts are not security sandboxes.

Read-only browser state is enabled only with ?e2e=1 for UI tests. It exposes document text to same-origin diagnostic JS but provides no command-execution endpoint. Do not enable it for sensitive documents.
