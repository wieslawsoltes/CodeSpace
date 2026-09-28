import * as Language from './language-features.mjs';
import { createConfiguration } from './configuration.mjs';
/** MIT. An explicit VS Code API subset. Executed extensions are trusted code, not sandboxed content. */
import { Disposable, EventEmitter, Position, Range, Selection, Uri, TextEdit, WorkspaceEdit, TextDocument } from './api-types.mjs';
export { Disposable, EventEmitter, Position, Range, Selection, Uri, TextEdit, WorkspaceEdit, TextDocument } from './api-types.mjs';
function strictApi(name, target) { return new Proxy(target, { get(object, key, receiver) { if (typeof key === 'symbol' || key === 'then' || key in object) return Reflect.get(object, key, receiver); throw new Error(`CodeSpace does not implement vscode.${name ? name + '.' : ''}${key}. See docs/compatibility.md.`); } }); }
function normalizePath(path) { const result = []; for (const part of path.replaceAll('\\', '/').split('/')) { if (part === '..') { if (!result.length) throw new Error('Module path escapes extension.'); result.pop(); } else if (part && part !== '.') result.push(part); } return result.join('/'); }
const encodeBytes = bytes => typeof Buffer !== 'undefined' ? Buffer.from(bytes).toString('base64') : btoa(Array.from(bytes, b => String.fromCharCode(b)).join(''));
const decodeBytes = base64 => typeof Buffer !== 'undefined' ? new Uint8Array(Buffer.from(base64, 'base64')) : Uint8Array.from(atob(base64), c => c.charCodeAt(0));

export function createExtensionHost(send, { browser = true, externalRequire, requestTimeout = 30000 } = {}) {
  if (typeof send !== 'function') throw new TypeError('A message transport is required.');
  const commands = new Map(), documents = new Map(), extensions = new Map(), pending = new Map();
  const selectionChanged = new EventEmitter();
  const opened = new EventEmitter(), changed = new EventEmitter(), closed = new EventEmitter(), activeChanged = new EventEmitter(), configChanged = new EventEmitter();
  let sequence = 0, activeUri, workspaceName = 'workspace', disposed = false;
  const request = (method, params) => new Promise((resolve, reject) => {
    if (disposed) { reject(new Error('Extension host is disposed.')); return; }
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Extension request timed out: ' + method)); }, requestTimeout); timer.unref?.();
    pending.set(id, { resolve, reject, timer }); send({ type: 'request', id, method, params });
  });
  const documentFor = uri => { const key = typeof uri === 'string' ? uri : uri.toString(); const document = documents.get(key); if (!document) throw new Error('Document is not open: ' + key); return document; };
  const editDocument = async (document, edits) => request('workspace.applyEdits', { uri: document.uri.toString(), version: document.version, edits: edits.map(edit => ({ start: document.offsetAt(edit.range.start), length: document.offsetAt(edit.range.end) - document.offsetAt(edit.range.start), text: edit.newText })) });
  const editorCache = new Map();
  const editorFor = document => {
    const key = document.uri.toString(); if (editorCache.has(key)) return editorCache.get(key);
    let selections = [new Selection(0, 0, 0, 0)];
    const editor = {
      document, get selection() { return selections[0]; }, set selection(value) { this.selections = [value]; },
      get selections() { return [...selections]; }, set selections(values) {
        if (!Array.isArray(values) || values.length === 0 || values.some(s => !(s instanceof Selection))) throw new TypeError('Expected a nonempty Selection array.');
        const offsets = values.map(s => ({ anchor: document.offsetAt(s.anchor), active: document.offsetAt(s.active) }));
        request('window.setSelections', { uri: key, selections: offsets, version: document.version }).catch(error => send({ type: 'error', message: error.message }));
      },
      _sync(values) {
        const next = values?.length ? values.map(s => new Selection(document.positionAt(s.anchor), document.positionAt(s.active))) : selections;
        if (JSON.stringify(next) !== JSON.stringify(selections)) { selections = next; selectionChanged.fire({ textEditor: editor, selections: [...next], kind: undefined }); }
      },
      options: { tabSize: 4, insertSpaces: true }, viewColumn: 1,
      edit: callback => { const edits = []; callback({ replace: (range, text) => edits.push(TextEdit.replace(range, text)), insert: (position, text) => edits.push(TextEdit.insert(position, text)), delete: range => edits.push(TextEdit.delete(range)) }); return editDocument(document, edits); }
    };
    editorCache.set(key, editor); return editor;
  };
  const configuration = createConfiguration(request, configChanged);
  const language = Language.createLanguageFeatures({ send, documentFor, Position, Range, Uri, Disposable });
  const api = strictApi('', {
    version: '1.90.0',
    Disposable, EventEmitter, Position, Range, Selection, Uri, TextEdit, WorkspaceEdit,
    CompletionItem: Language.CompletionItem, CompletionList: Language.CompletionList, CompletionItemKind: Language.CompletionItemKind,
    MarkdownString: Language.MarkdownString, Hover: Language.Hover, Location: Language.Location, DocumentSymbol: Language.DocumentSymbol,
    SymbolKind: Language.SymbolKind, Diagnostic: Language.Diagnostic, DiagnosticSeverity: Language.DiagnosticSeverity,
    CancellationTokenSource: Language.CancellationTokenSource, ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 },
    languages: strictApi('languages', language.languages),
    EndOfLine: { LF: 1, CRLF: 2 }, ViewColumn: { Active: -1, Beside: -2, One: 1, Two: 2, Three: 3 },
    FileType: { Unknown: 0, File: 1, Directory: 2, SymbolicLink: 64 },
    StatusBarAlignment: { Left: 1, Right: 2 }, ExtensionMode: { Production: 1, Development: 2, Test: 3 }, UIKind: { Desktop: 1, Web: 2 },
    env: strictApi('env', { appName: 'CodeSpace', appHost: browser ? 'web' : 'desktop', language: 'en', uiKind: browser ? 2 : 1, isTelemetryEnabled: false }),
    commands: strictApi('commands', {
      registerCommand(command, callback, thisArg) {
        if (commands.has(command)) throw new Error('Command already registered: ' + command);
        commands.set(command, { callback, thisArg }); send({ type: 'registerCommand', command });
        return new Disposable(() => { commands.delete(command); send({ type: 'unregisterCommand', command }); });
      },
      async executeCommand(command, ...args) { const entry = commands.get(command); return entry ? entry.callback.apply(entry.thisArg, args) : request('commands.executeCommand', { command, args }); },
      async getCommands() { return [...commands.keys()]; }
    }),
    workspace: strictApi('workspace', {
      get name() { return workspaceName; }, get isTrusted() { return true; }, get textDocuments() { return [...documents.values()]; },
      get workspaceFolders() { return [{ uri: new Uri('codespace', '', '/'), name: workspaceName, index: 0 }]; },
      onDidOpenTextDocument: opened.event, onDidChangeTextDocument: changed.event, onDidCloseTextDocument: closed.event, onDidChangeConfiguration: configChanged.event,
      async openTextDocument(value) { return documentFor(value); },
      asRelativePath(uri) { return (typeof uri === 'string' ? uri.replace(/^codespace:\/\//, '') : uri.path).replace(/^\//, ''); },
      getWorkspaceFolder(uri) { return uri.scheme === 'codespace' ? { uri: new Uri('codespace', '', '/'), name: workspaceName, index: 0 } : undefined; },
      getConfiguration: configuration.getConfiguration,
      async applyEdit(edit) {
        if (!(edit instanceof WorkspaceEdit)) throw new TypeError('Expected WorkspaceEdit.');
        if (edit.size <= 1) { for (const [uri, edits] of edit.entries()) if (!await editDocument(documentFor(uri), edits)) return false; return true; }
        const documents = edit.entries().map(([uri, edits]) => {
          const document = documentFor(uri);
          return { uri: uri.toString(), version: document.version, edits: edits.map(edit => ({ start: document.offsetAt(edit.range.start), length: document.offsetAt(edit.range.end) - document.offsetAt(edit.range.start), text: edit.newText })) };
        });
        return request('workspace.applyWorkspaceEdit', { documents });
      },
      fs: strictApi('workspace.fs', {
        async readFile(uri) { return decodeBytes(await request('workspace.readFile', { uri: uri.toString() })); },
        async writeFile(uri, bytes) { await request('workspace.writeFile', { uri: uri.toString(), data: encodeBytes(bytes) }); },
        async stat(uri) { const document = documentFor(uri); return { type: 1, ctime: 0, mtime: 0, size: new TextEncoder().encode(document.getText()).length }; },
        async readDirectory(uri) { const prefix = uri.path.replace(/^\//, '').replace(/\/?$/, '/'); const entries = new Map(); for (const document of documents.values()) { const path = document.uri.path.replace(/^\//, ''); const start = prefix === '/' ? '' : prefix; if (!path.startsWith(start)) continue; const rest = path.slice(start.length); const parts = rest.split('/'); entries.set(parts[0], parts.length > 1 ? 2 : 1); } return [...entries]; }
      })
    }),
    window: strictApi('window', {
      get activeTextEditor() { return activeUri && documents.has(activeUri) ? editorFor(documents.get(activeUri)) : undefined; },
      get visibleTextEditors() { return this.activeTextEditor ? [this.activeTextEditor] : []; },
      onDidChangeActiveTextEditor: activeChanged.event,
      onDidChangeTextEditorSelection: selectionChanged.event,
      async showInformationMessage(message, ...items) { if (items.length) throw new Error('Notification action items are not implemented.'); await request('window.showInformationMessage', { message }); },
      async showWarningMessage(message, ...items) { if (items.length) throw new Error('Notification action items are not implemented.'); await request('window.showWarningMessage', { message }); },
      async showErrorMessage(message, ...items) { if (items.length) throw new Error('Notification action items are not implemented.'); await request('window.showErrorMessage', { message }); },
      async showTextDocument(value) { const document = value instanceof TextDocument ? value : documentFor(value); await request('window.showTextDocument', { uri: document.uri.toString() }); activeUri = document.uri.toString(); return editorFor(document); },
      createOutputChannel(name) { let dead = false; return { name, append: text => { if (!dead) send({ type: 'log', message: `[${name}] ${text}` }); }, appendLine: text => { if (!dead) send({ type: 'log', message: `[${name}] ${text}` }); }, replace: text => { if (!dead) send({ type: 'log', message: `[${name}] ${text}` }); }, clear() { if (!dead) send({ type: 'log', message: `[${name}] (clear requested)` }); }, show() {}, hide() {}, dispose() { dead = true; } }; }
    }),
    extensions: strictApi('extensions', {
      get all() { return [...extensions.values()].map(entry => entry.extension); }, getExtension(id) { return extensions.get(id)?.extension; }
    })
  });
  function loadModule(path, files, cache, parent = '') {
    path = normalizePath(path);
    const resolved = [path, path + '.js', path + '.cjs', path + '.json', path + '/index.js'].find(candidate => Object.hasOwn(files, candidate));
    if (!resolved) throw new Error('Cannot resolve bundled module: ' + path);
    if (cache.has(resolved)) return cache.get(resolved).exports;
    const module = { exports: {} }; cache.set(resolved, module);
    if (resolved.endsWith('.json')) { module.exports = JSON.parse(files[resolved]); return module.exports; }
    const directory = resolved.includes('/') ? resolved.slice(0, resolved.lastIndexOf('/')) : '';
    const require = name => {
      if (name === 'vscode') return api;
      if (name.startsWith('.')) return loadModule(directory + '/' + name, files, cache, resolved);
      const bundled = normalizePath('node_modules/' + name);
      if ([bundled, bundled + '.js', bundled + '/index.js'].some(candidate => Object.hasOwn(files, candidate))) return loadModule(bundled, files, cache, resolved);
      if (!browser && externalRequire) return externalRequire(name);
      throw new Error('Unavailable module in browser extension host: ' + name + '. Bundle the dependency or use a Node host.');
    };
    const extensionConsole = Object.fromEntries(['log', 'info', 'warn', 'error', 'debug'].map(level => [level, (...args) => send({ type: 'log', message: `[${level}] ${args.map(String).join(' ')}` })]));
    try { new Function('require', 'module', 'exports', '__filename', '__dirname', 'console', '"use strict";\n' + files[resolved] + '\n//# sourceURL=codespace-extension/' + resolved)(require, module, module.exports, resolved, directory, extensionConsole); }
    catch (error) { cache.delete(resolved); throw error; }
    return module.exports;
  }
  const memento = () => { const values = new Map(); return { keys: () => [...values.keys()], get: (key, fallback) => values.has(key) ? values.get(key) : fallback, update: async (key, value) => { if (value === undefined) values.delete(key); else values.set(key, value); }, setKeysForSync() { throw new Error('Settings sync is not implemented.'); } }; };
  async function deactivate(id) { const entry = extensions.get(id); if (!entry) return; try { await entry.module.deactivate?.(); } finally { for (const subscription of entry.context.subscriptions) subscription?.dispose?.(); extensions.delete(id); } }
  async function handle(message) {
    if (!message || typeof message.type !== 'string') throw new TypeError('Invalid extension host message.');
    if (message.type === 'response') { const entry = pending.get(message.id); if (!entry) return; pending.delete(message.id); clearTimeout(entry.timer); message.error ? entry.reject(new Error(message.error)) : entry.resolve(message.result); return; }
    if (disposed) throw new Error('Extension host is disposed.');
    if (message.type === 'featureRequest' || message.type === 'featureCancel') return language.handle(message);
    if (message.type === 'configuration') { configuration.update(message); return; }
    if (message.type === 'workspace' || message.type === 'workspaceDelta') {
      workspaceName = message.name ?? 'workspace'; const seen = new Set();
      for (const data of message.documents ?? []) {
        seen.add(data.uri); const existing = documents.get(data.uri);
        if (existing) { const previousVersion = existing.version; const previousText = existing.text; const previousEnd = existing.positionAt(previousText.length); const previousDirty = existing.isDirty; existing.update(data); if (existing.version !== previousVersion || existing.text !== previousText || previousDirty !== existing.isDirty) changed.fire({ document: existing, contentChanges: previousText === existing.text ? [] : [{ range: new Range(new Position(0, 0), previousEnd), rangeOffset: 0, rangeLength: previousText.length, text: existing.text }] }); }
        else { const document = new TextDocument(data); documents.set(data.uri, document); opened.fire(document); }
      }
      for (const [uri, document] of documents) if (message.type === 'workspace' ? !seen.has(uri) : message.deleted?.includes(uri)) { document.isClosed = true; documents.delete(uri); editorCache.delete(uri); closed.fire(document); }
      for (const state of message.selections ?? []) if (documents.has(state.uri)) editorFor(documents.get(state.uri))._sync(state.values);
      if (activeUri !== message.activeUri) { activeUri = message.activeUri; activeChanged.fire(api.window.activeTextEditor); } return;
    }
    if (message.type === 'activate') {
      if (message.trusted !== true) throw new Error('Explicit extension trust is required.');
      const manifest = message.manifest; if (!manifest?.name || !manifest.publisher) throw new Error('Invalid extension manifest.');
      const id = manifest.publisher + '.' + manifest.name; const entryPoint = browser ? manifest.browser : manifest.main ?? manifest.browser;
      if (typeof entryPoint !== 'string') throw new Error(browser ? 'No browser extension entry point.' : 'No extension entry point.');
      await deactivate(id); const module = loadModule(entryPoint, message.files ?? {}, new Map());
      if (typeof module.activate !== 'function') throw new Error('Extension must export activate(context).');
      const extensionUri = new Uri('codespace-extension', id, '/');
      const context = { subscriptions: [], workspaceState: memento(), globalState: memento(), extensionUri, extensionPath: extensionUri.toString(), extensionMode: 1, storageUri: undefined, globalStorageUri: undefined, logUri: undefined, asAbsolutePath: path => Uri.joinPath(extensionUri, path).toString() };
      const extension = { id, extensionUri, extensionPath: context.extensionPath, packageJSON: manifest, isActive: false, exports: undefined };
      context.extension = extension; extensions.set(id, { context, extension, module });
      try { extension.exports = await module.activate(context); extension.isActive = true; send({ type: 'activated', message: id + ' activated (partial API compatibility)' }); }
      catch (error) { await deactivate(id); throw error; } return;
    }
    if (message.type === 'execute') { const entry = commands.get(message.command); if (!entry) throw new Error('Extension command is not registered: ' + message.command); const result = await entry.callback.apply(entry.thisArg, message.args ?? []); send({ type: 'result', command: message.command, result: result ?? null, message: 'Executed ' + message.command }); return result; }
    if (message.type === 'deactivate') { await deactivate(message.id); return; }
    throw new Error('Unknown extension host message: ' + message.type);
  }
  async function dispose() { for (const id of [...extensions.keys()]) await deactivate(id); language.dispose(); disposed = true; for (const entry of pending.values()) { clearTimeout(entry.timer); entry.reject(new Error('Extension host disposed.')); } pending.clear(); for (const event of [opened, changed, closed, activeChanged, configChanged, selectionChanged]) event.dispose(); }
  return { handle, dispose, api, capabilities: Object.freeze({ universalCompatibility: false, browser, commonjs: true, commands: true, textDocuments: true, workspaceEdits: 'atomic-text-multi-document', webviews: false, languageProviders: true, debugging: false, terminal: false, textmate: false }) };
}
