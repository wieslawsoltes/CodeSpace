/** MIT. An intentionally explicit VS Code API subset, independent of Uno and any renderer.
 * Executed extension code is TRUSTED code. Neither Function nor a Worker is a security sandbox.
 */
export class Disposable {
  #callback;
  constructor(callback = () => {}) { this.#callback = callback; }
  dispose() { const callback = this.#callback; this.#callback = undefined; callback?.(); }
  static from(...items) { return new Disposable(() => { for (const item of items) item?.dispose(); }); }
}
export class EventEmitter {
  #listeners = new Set();
  event = (listener, thisArgs, disposables) => {
    if (typeof listener !== 'function') throw new TypeError('Event listener must be a function.');
    const entry = { listener, thisArgs }; this.#listeners.add(entry);
    const disposable = new Disposable(() => this.#listeners.delete(entry)); disposables?.push(disposable); return disposable;
  };
  fire(value) { for (const entry of [...this.#listeners]) entry.listener.call(entry.thisArgs, value); }
  dispose() { this.#listeners.clear(); }
}
export class Position {
  constructor(line, character) {
    if (!Number.isInteger(line) || !Number.isInteger(character) || line < 0 || character < 0) throw new RangeError('Position must contain non-negative integers.');
    this.line = line; this.character = character;
  }
  compareTo(other) { return this.line - other.line || this.character - other.character; }
  isBefore(other) { return this.compareTo(other) < 0; }
  isBeforeOrEqual(other) { return this.compareTo(other) <= 0; }
  isAfter(other) { return this.compareTo(other) > 0; }
  isAfterOrEqual(other) { return this.compareTo(other) >= 0; }
  isEqual(other) { return this.compareTo(other) === 0; }
  translate(lineDelta = 0, characterDelta = 0) { if (typeof lineDelta === 'object') return this.translate(lineDelta.lineDelta ?? 0, lineDelta.characterDelta ?? 0); return new Position(this.line + lineDelta, this.character + characterDelta); }
  with(line = this.line, character = this.character) { if (typeof line === 'object') return new Position(line.line ?? this.line, line.character ?? this.character); return new Position(line, character); }
  toJSON() { return { line: this.line, character: this.character }; }
}
export class Range {
  constructor(a, b, c, d) {
    let start = typeof a === 'number' ? new Position(a, b) : a;
    let end = typeof a === 'number' ? new Position(c, d) : b;
    if (!(start instanceof Position) || !(end instanceof Position)) throw new TypeError('Range requires Position values.');
    if (start.isAfter(end)) [start, end] = [end, start]; this.start = start; this.end = end;
  }
  get isEmpty() { return this.start.isEqual(this.end); }
  get isSingleLine() { return this.start.line === this.end.line; }
  contains(value) { return value instanceof Range ? this.contains(value.start) && this.contains(value.end) : this.start.isBeforeOrEqual(value) && this.end.isAfterOrEqual(value); }
  isEqual(other) { return this.start.isEqual(other.start) && this.end.isEqual(other.end); }
  intersection(other) { const start = this.start.isAfter(other.start) ? this.start : other.start; const end = this.end.isBefore(other.end) ? this.end : other.end; return start.isAfter(end) ? undefined : new Range(start, end); }
  union(other) { return new Range(this.start.isBefore(other.start) ? this.start : other.start, this.end.isAfter(other.end) ? this.end : other.end); }
  with(start = this.start, end = this.end) { if (!(start instanceof Position)) return new Range(start.start ?? this.start, start.end ?? this.end); return new Range(start, end); }
}
export class Selection extends Range {
  constructor(a, b, c, d) { super(a, b, c, d); this.anchor = typeof a === 'number' ? new Position(a, b) : a; this.active = typeof a === 'number' ? new Position(c, d) : b; }
  get isReversed() { return this.anchor.isAfter(this.active); }
}
export class Uri {
  constructor(scheme, authority = '', path = '', query = '', fragment = '') { Object.assign(this, { scheme, authority, path, query, fragment }); }
  static parse(value) { const match = /^([a-zA-Z][a-zA-Z\d+.-]*):(?:\/\/([^/?#]*))?([^?#]*)(?:\?([^#]*))?(?:#(.*))?$/.exec(value); if (!match) throw new TypeError('Invalid URI: ' + value); return new Uri(match[1], match[2] ?? '', decodeURIComponent(match[3]), match[4] ?? '', match[5] ?? ''); }
  static file(path) { path = path.replaceAll('\\', '/'); return new Uri('file', '', path.startsWith('/') ? path : '/' + path); }
  static from(parts) { return new Uri(parts.scheme, parts.authority, parts.path, parts.query, parts.fragment); }
  static joinPath(base, ...paths) { const parts = (base.path + '/' + paths.join('/')).split('/'); const out = []; for (const p of parts) { if (p === '..') out.pop(); else if (p && p !== '.') out.push(p); } return base.with({ path: '/' + out.join('/') }); }
  with(change) { return Uri.from({ ...this, ...change }); }
  get fsPath() { return this.path; }
  toString(skipEncoding = false) { const path = skipEncoding ? this.path : this.path.split('/').map(encodeURIComponent).join('/'); return this.scheme + ':' + (this.authority || path.startsWith('/') ? '//' + this.authority : '') + path + (this.query ? '?' + this.query : '') + (this.fragment ? '#' + this.fragment : ''); }
  toJSON() { return { ...this }; }
}
export class TextEdit {
  constructor(range, newText) { this.range = range; this.newText = newText; }
  static replace(range, newText) { return new TextEdit(range, newText); }
  static insert(position, newText) { return new TextEdit(new Range(position, position), newText); }
  static delete(range) { return new TextEdit(range, ''); }
}
export class WorkspaceEdit {
  #edits = new Map();
  set(uri, edits) { this.#edits.set(uri.toString(), { uri, edits: [...edits] }); }
  get(uri) { return this.#edits.get(uri.toString())?.edits ?? []; }
  replace(uri, range, text) { this.set(uri, [...this.get(uri), TextEdit.replace(range, text)]); }
  insert(uri, position, text) { this.replace(uri, new Range(position, position), text); }
  delete(uri, range) { this.replace(uri, range, ''); }
  has(uri) { return this.#edits.has(uri.toString()); }
  get size() { return this.#edits.size; }
  entries() { return [...this.#edits.values()].map(({ uri, edits }) => [uri, edits]); }
}
export class TextDocument {
  constructor(data) { this.update(data); }
  update(data) { this.uri = Uri.parse(data.uri); this.fileName = data.path ?? this.uri.path; this.languageId = data.languageId ?? 'plaintext'; this.version = data.version ?? 1; this.isDirty = data.isDirty ?? false; this.isUntitled = this.uri.scheme === 'untitled'; this.isClosed = false; this.text = data.text ?? ''; this.eol = this.text.includes('\r\n') ? 2 : 1; this.starts = [0]; for (let i = 0; i < this.text.length; i++) if (this.text[i] === '\n') this.starts.push(i + 1); }
  get lineCount() { return this.starts.length; }
  getText(range) { return range ? this.text.slice(this.offsetAt(range.start), this.offsetAt(range.end)) : this.text; }
  lineAt(line) {
    line = typeof line === 'number' ? line : line.line;
    if (!Number.isInteger(line) || line < 0 || line >= this.lineCount) throw new RangeError('Line out of range.');
    const start = this.starts[line], next = this.starts[line + 1] ?? this.text.length; let end = next;
    if (this.text[end - 1] === '\n') end--; if (this.text[end - 1] === '\r') end--;
    const text = this.text.slice(start, end), range = new Range(line, 0, line, text.length);
    return { lineNumber: line, text, range, rangeIncludingLineBreak: line + 1 < this.lineCount ? new Range(line, 0, line + 1, 0) : range, firstNonWhitespaceCharacterIndex: text.length - text.trimStart().length, isEmptyOrWhitespace: !text.trim() };
  }
  offsetAt(position) { const line = Math.max(0, Math.min(this.lineCount - 1, position.line)); return this.starts[line] + Math.max(0, Math.min(this.lineAt(line).text.length, position.character)); }
  positionAt(offset) { offset = Math.max(0, Math.min(this.text.length, offset)); let lo = 0, hi = this.starts.length; while (lo + 1 < hi) { const mid = (lo + hi) >> 1; if (this.starts[mid] <= offset) lo = mid; else hi = mid; } return new Position(lo, offset - this.starts[lo]); }
  validatePosition(position) { return this.positionAt(this.offsetAt(position)); }
  validateRange(range) { return new Range(this.validatePosition(range.start), this.validatePosition(range.end)); }
  getWordRangeAtPosition(position, regex = /[\p{L}\p{N}_$]+/gu) { const line = this.lineAt(position.line); const flags = regex.flags.includes('g') ? regex.flags : regex.flags + 'g'; for (const match of line.text.matchAll(new RegExp(regex.source, flags))) if (match[0].length && position.character >= match.index && position.character <= match.index + match[0].length) return new Range(position.line, match.index, position.line, match.index + match[0].length); }
}
function strictApi(name, target) { return new Proxy(target, { get(object, key, receiver) { if (typeof key === 'symbol' || key === 'then' || key in object) return Reflect.get(object, key, receiver); throw new Error(`CodeSpace does not implement vscode.${name ? name + '.' : ''}${key}. See docs/compatibility.md.`); } }); }
function normalizePath(path) { const result = []; for (const part of path.replaceAll('\\', '/').split('/')) { if (part === '..') { if (!result.length) throw new Error('Module path escapes extension.'); result.pop(); } else if (part && part !== '.') result.push(part); } return result.join('/'); }
const encodeBytes = bytes => typeof Buffer !== 'undefined' ? Buffer.from(bytes).toString('base64') : btoa(Array.from(bytes, b => String.fromCharCode(b)).join(''));
const decodeBytes = base64 => typeof Buffer !== 'undefined' ? new Uint8Array(Buffer.from(base64, 'base64')) : Uint8Array.from(atob(base64), c => c.charCodeAt(0));

export function createExtensionHost(send, { browser = true, externalRequire, requestTimeout = 30000 } = {}) {
  if (typeof send !== 'function') throw new TypeError('A message transport is required.');
  const commands = new Map(), documents = new Map(), extensions = new Map(), pending = new Map();
  const opened = new EventEmitter(), changed = new EventEmitter(), closed = new EventEmitter(), activeChanged = new EventEmitter(), configChanged = new EventEmitter();
  let sequence = 0, activeUri, workspaceName = 'workspace', disposed = false;
  const request = (method, params) => new Promise((resolve, reject) => {
    if (disposed) { reject(new Error('Extension host is disposed.')); return; }
    const id = ++sequence;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error('Extension request timed out: ' + method)); }, requestTimeout); timer.unref?.();
    pending.set(id, { resolve, reject, timer }); send({ type: 'request', id, method, params });
  });
  const documentFor = uri => { const key = typeof uri === 'string' ? uri : uri.toString(); const document = documents.get(key); if (!document) throw new Error('Document is not open: ' + key); return document; };
  const editDocument = async (document, edits) => request('workspace.applyEdits', { uri: document.uri.toString(), edits: edits.map(edit => ({ start: document.offsetAt(edit.range.start), length: document.offsetAt(edit.range.end) - document.offsetAt(edit.range.start), text: edit.newText })) });
  const editorFor = document => ({
    document, selection: new Selection(0, 0, 0, 0), selections: [new Selection(0, 0, 0, 0)], options: { tabSize: 4, insertSpaces: true }, viewColumn: 1,
    edit: callback => { const edits = []; callback({ replace: (range, text) => edits.push(TextEdit.replace(range, text)), insert: (position, text) => edits.push(TextEdit.insert(position, text)), delete: range => edits.push(TextEdit.delete(range)) }); return editDocument(document, edits); }
  });
  const api = strictApi('', {
    version: '1.90.0', // Declared API shape baseline, not an assertion of full compatibility.
    Disposable, EventEmitter, Position, Range, Selection, Uri, TextEdit, WorkspaceEdit,
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
      getConfiguration(section = '') { return { get: (name, defaultValue) => defaultValue, has: () => false, inspect: () => undefined, update: () => Promise.reject(new Error('Persistent configuration API is not implemented.')) }; },
      async applyEdit(edit) { if (!(edit instanceof WorkspaceEdit)) throw new TypeError('Expected WorkspaceEdit.'); if (edit.size > 1) throw new Error('Atomic multi-document WorkspaceEdit is not yet implemented.'); for (const [uri, edits] of edit.entries()) if (!await editDocument(documentFor(uri), edits)) return false; return true; },
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
    if (message.type === 'workspace') {
      workspaceName = message.name ?? 'workspace'; const seen = new Set();
      for (const data of message.documents ?? []) {
        seen.add(data.uri); const existing = documents.get(data.uri);
        if (existing) { const previousVersion = existing.version; const previousText = existing.text; existing.update(data); if (existing.version !== previousVersion || existing.text !== previousText) changed.fire({ document: existing, contentChanges: [{ rangeOffset: 0, rangeLength: previousText.length, text: existing.text }] }); }
        else { const document = new TextDocument(data); documents.set(data.uri, document); opened.fire(document); }
      }
      for (const [uri, document] of documents) if (!seen.has(uri)) { document.isClosed = true; documents.delete(uri); closed.fire(document); }
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
  async function dispose() { for (const id of [...extensions.keys()]) await deactivate(id); disposed = true; for (const entry of pending.values()) { clearTimeout(entry.timer); entry.reject(new Error('Extension host disposed.')); } pending.clear(); for (const event of [opened, changed, closed, activeChanged, configChanged]) event.dispose(); }
  return { handle, dispose, api, capabilities: Object.freeze({ universalCompatibility: false, browser, commonjs: true, commands: true, textDocuments: true, workspaceEdits: 'single-document', webviews: false, languageProviders: false, debugging: false, terminal: false, textmate: false }) };
}
