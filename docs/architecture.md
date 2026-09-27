# Architecture

## Dependency direction

```text
App (desktop / browser adapters)
  └─ Workbench.Uno
       ├─ Controls.Uno
       │    ├─ Rendering.Skia → Editor → Core
       │    └─ Docking
       └─ Extensions → Core
Rendering.Skia → Languages → Core
ExtensionHost (independent JavaScript package)
  ├─ browser module worker
  └─ Node stdin/stdout process
       ↕ JSON messages / IExtensionBridge
```

The six portable libraries target `net10.0` without Uno dependencies. The two Uno libraries target `net10.0-desktop` and `net10.0-browserwasm`. The application is their consumer, not a required engine dependency. The JavaScript host has no npm runtime dependencies.

## Text storage and editing

`TextBuffer` is a persistent AVL rope. Leaves reference immutable strings in chunks of at most 2,048 UTF-16 units. Branches cache length, height and newline count; leaves index newlines locally. Replacements split and rejoin balanced nodes, sharing unaffected subtrees with undo snapshots. All edit offsets are UTF-16. Source line endings are preserved.

`EditorSession` validates the entire edit batch before mutation. History contains a buffer root and selection array, bounded to 500 snapshots. Dirty tracking compares the current and saved snapshot in O(1), not the complete text on every keystroke. Manually replacing content with equivalent text remains a new edit until save or return to the saved history snapshot. Grapheme navigation sits above the raw offset engine.

Tests include 10,000 seeded randomized edits compared with ordinary string operations. An allocation gate measures 100 inserts into a two-million-character document. It is not a wall-clock or GPU performance guarantee.

## Graphics and input

`EditorRenderer` draws to a caller-owned `SKCanvas`. Uno's `SKCanvasElement` supplies the existing composition surface; the editor does not upload a CPU bitmap per frame. Uno/Skia select the actual hardware backend and fallback behavior. The project does not contain a separately qualified Vulkan, Metal or WebGPU backend.

Visible line layouts and lexical state are cached. The minimap is a simplified line-length representation. Large single lines and distant jumps requiring lexical prefix processing remain optimization targets. Render metrics describe CPU paint work, not GPU timestamps.

Clusters are measured and drawn independently. Full contextual shaping, bidi, ligatures, variable fonts and font fallback are not qualified. The browser loads the framework's licensed Open Sans fallback; it is not an exact VS Code monospace match. An embedding host may supply a licensed `SKTypeface` through `EditorRenderer.DefaultTypeface`.

`CodeEditorControl` owns viewport, pointer/keyboard editing and find UI. A tiny Uno `TextBox` is used only as a text-input bridge, never as document storage or renderer. Full IME and screen-reader document navigation remain open. `FileTreeControl` and original vector icons also use custom Skia drawing. The other custom controls compose Uno primitives with workbench styling.

## Docking, files and recovery

Docking is an immutable split/tab model independent of controls. Each group chooses an active document; multiple views share a session. Split resizing updates host geometry during drag and persists the final ratio. Empty groups remain valid; floating and multi-window behavior is not implemented.

`IWorkbenchPlatform` separates imports, exports, recovery and extension transport. Files live in a virtual workspace, not a watched OS folder. Browser recovery uses one atomic localStorage replacement after a debounce; quota and security exceptions surface to the user. Desktop recovery writes a temporary file then replaces the previous recovery file. Export is the durable backup workflow. Recovery is not encrypted, synchronized or a complete history/settings restoration.

## Extension boundary

VSIX inspection is bounded and non-executing. Activation requires explicit trust. CommonJS packages run in a browser worker or optional Node process behind a deliberately limited `require('vscode')` facade. Unknown APIs fail visibly. Host requests have timeouts; responses bypass activation/command serialization so activation can await UI RPC without deadlock.

Documents synchronize before extension commands and after bridged requests; continuous incremental synchronization is not implemented. Workers isolate CPU work from the UI thread, not hostile code from workspace data or network access. Node runs with the current user's process authority. This is not a security sandbox.

Read-only browser state is available only in `?e2e=1` diagnostic mode for real UI tests. That mode exposes document text to same-origin diagnostic JavaScript, but no command-execution endpoint. Never use it for sensitive documents.
