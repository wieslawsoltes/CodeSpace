# @codespace/extension-host

MIT-licensed, renderer-independent **experimental VS Code API subset**. This is not the VS Code extension host and does not offer universal extension compatibility.

```js
import { createExtensionHost } from '@codespace/extension-host';
const host = createExtensionHost(message => transport.send(message), { browser: true });
await host.handle({ type: 'workspace', name: 'demo', documents: [], activeUri: null });
// Activate a package only after explicit user trust.
```

`worker.mjs` runs CommonJS browser extensions outside the UI thread. `node-host.mjs` provides a newline-delimited JSON stdin/stdout process host. The Uno application supplies the filesystem and UI bridge. Relative bundled JS/JSON dependencies are supported; ESM entry points, arbitrary npm resolution and native modules are not qualified. `api-types.mjs`, `configuration.mjs` and `language-features.mjs` separate the reusable implementation areas.

**Security:** all activated extensions are trusted executable code. Workers do not isolate network access or protect workspace content. The Node process has the authority of the launching user. This is not a security sandbox. No extension executes merely by opening a VSIX. No registry downloads or install scripts run automatically.

Coverage includes selected value types, commands, document snapshots/events, version-checked atomic multi-document text edits, workspace text-file read/write, simple notifications and output logging. Unsupported API properties throw explicit errors. Notification action items, webviews, semantic tokens, debugging, terminals, tasks, extension-state persistence and settings sync are not implemented. Output-channel clear/visibility operations do not correspond to independent Uno output tabs.

Completion, hover, definition, document-symbol and formatting providers and diagnostic collections use featureRequest/featureResult messages. The embedding workbench enforces stale-document checks and applies edits. WorkspaceDelta frames preserve unchanged documents; configuration frames supply default/user/workspace layers and changes. Configuration updates and selection setters are asynchronous workbench requests. Snippet completions, resource edits inside WorkspaceEdit, full provider resolution/options, resource-scoped settings and grouped cross-file Undo remain unsupported. The standard API-shaped fixture is not evidence of universal compatibility.

Run `npm test` for independent tests and `npm pack` for a local archive. No registry publishing is required.
