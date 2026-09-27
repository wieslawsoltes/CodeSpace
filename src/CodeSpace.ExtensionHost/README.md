# @codespace/extension-host

MIT-licensed, renderer-independent **experimental VS Code API subset**. This is not the VS Code extension host and does not offer universal extension compatibility.

```js
import { createExtensionHost } from '@codespace/extension-host';
const host = createExtensionHost(message => transport.send(message), { browser: true });
await host.handle({ type: 'workspace', name: 'demo', documents: [], activeUri: null });
// Accept activation only after the user has reviewed and explicitly trusted the package.
```

`worker.mjs` runs CommonJS browser extensions outside the UI thread. `node-host.mjs` provides a newline-delimited JSON stdin/stdout process host. The Uno application supplies the filesystem and UI bridge. Relative bundled JS/JSON dependencies are supported; ESM entry points, arbitrary npm resolution and native modules are not qualified.

**Security:** all activated extensions are trusted executable code. Workers do not isolate network access or protect workspace content. The Node process has the authority of the launching user. This package is not an extension security sandbox. No extension executes merely by opening a VSIX. No registry downloads or install scripts are run.

API coverage includes selected value types, command registration/execution, document snapshots/events, single-document edits, workspace file read/write, simple notifications and output logging. Unsupported API properties throw explicit errors. Notification action items, webviews, language providers, debugging, terminals, task execution, extension persistence and settings sync are not implemented. Output-channel clear/visibility operations do not correspond to independent Uno output tabs yet. See the repository compatibility matrix.

Run `npm test` for independent Node tests. Run `npm pack` to create a local tarball; publishing is not required.
