/** MIT. Renderer-independent, cancellable VS Code language-provider adapters. */
export class CancellationTokenSource {
  #listeners = new Set();
  constructor() {
    this.token = { isCancellationRequested: false, onCancellationRequested: listener => {
      if (this.token.isCancellationRequested) queueMicrotask(listener);
      else this.#listeners.add(listener);
      return { dispose: () => this.#listeners.delete(listener) };
    } };
  }
  cancel() { if (this.token.isCancellationRequested) return; this.token.isCancellationRequested = true; for (const listener of this.#listeners) listener(); this.#listeners.clear(); }
  dispose() { this.#listeners.clear(); }
}
export class CompletionItem { constructor(label, kind) { this.label = label; this.kind = kind; } }
export class CompletionList { constructor(items = [], isIncomplete = false) { this.items = items; this.isIncomplete = isIncomplete; } }
export class MarkdownString {
  constructor(value = '', supportThemeIcons = false) { this.value = value; this.supportThemeIcons = supportThemeIcons; this.isTrusted = false; }
  appendText(value) { this.value += value.replace(/[\\`*_{}[\]()#+.!<>]/g, '\\$&'); return this; }
  appendMarkdown(value) { this.value += value; return this; }
  appendCodeblock(value, language = '') { this.value += '\n```' + language + '\n' + value + '\n```\n'; return this; }
}
export class Hover { constructor(contents, range) { this.contents = Array.isArray(contents) ? contents : [contents]; this.range = range; } }
export class Location { constructor(uri, range) { this.uri = uri; this.range = range; } }
export class Diagnostic { constructor(range, message, severity = 0) { Object.assign(this, { range, message, severity }); } }
export class DocumentSymbol { constructor(name, detail, kind, range, selectionRange) { Object.assign(this, { name, detail, kind, range, selectionRange, children: [] }); } }
export const CompletionItemKind = Object.freeze(Object.fromEntries('Text Method Function Constructor Field Variable Class Interface Module Property Unit Value Enum Keyword Snippet Color File Reference Folder EnumMember Constant Struct Event Operator TypeParameter'.split(' ').map((key, i) => [key, i])));
export const SymbolKind = Object.freeze(Object.fromEntries('File Module Namespace Package Class Method Property Field Constructor Enum Interface Function Variable Constant String Number Boolean Array Object Key Null EnumMember Struct Event Operator TypeParameter'.split(' ').map((key, i) => [key, i])));
export const DiagnosticSeverity = Object.freeze({ Error: 0, Warning: 1, Information: 2, Hint: 3 });

export function scoreSelector(selector, document) {
  if (Array.isArray(selector)) return Math.max(0, ...selector.map(s => scoreSelector(s, document)));
  if (typeof selector === 'string') return selector === '*' ? 5 : selector === document.languageId ? 10 : 0;
  if (!selector || typeof selector !== 'object') return 0;
  let score = 0;
  for (const [key, actual] of [['language', document.languageId], ['scheme', document.uri.scheme]]) {
    if (selector[key] !== undefined) { if (selector[key] !== '*' && selector[key] !== actual) return 0; score = Math.max(score, selector[key] === '*' ? 5 : 10); }
  }
  if (selector.pattern !== undefined) {
    if (typeof selector.pattern !== 'string') throw new Error('RelativePattern document selectors are not implemented.');
    const pattern = selector.pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replaceAll('**/', '\u0001').replaceAll('**', '\u0002').replaceAll('*', '[^/]*').replaceAll('?', '[^/]').replaceAll('\u0001', '(?:.*/)?').replaceAll('\u0002', '.*');
    if (!new RegExp('^' + pattern + '$').test(document.uri.path.replace(/^\//, ''))) return 0;
    score = Math.max(score, 10);
  }
  return score;
}

export function createLanguageFeatures({ send, documentFor, Position, Range, Uri, Disposable }) {
  const providers = new Map(), diagnostics = new Set(), inflight = new Map(); let next = 0;
  const register = (kind, selector, provider, method) => {
    if (typeof provider?.[method] !== 'function') throw new TypeError('Provider must implement ' + method);
    const id = ++next; providers.set(id, { kind, selector, provider, method });
    send({ type: 'providerRegistered', kind, id });
    return new Disposable(() => { providers.delete(id); send({ type: 'providerUnregistered', kind, id }); });
  };
  const publish = (owner, values) => send({ type: 'diagnostics', owner, entries: [...values].map(([uri, diagnostics]) => ({ uri, diagnostics })) });
  const languages = {
    match: scoreSelector,
    registerCompletionItemProvider: (selector, provider) => register('completion', selector, provider, 'provideCompletionItems'),
    registerHoverProvider: (selector, provider) => register('hover', selector, provider, 'provideHover'),
    registerDefinitionProvider: (selector, provider) => register('definition', selector, provider, 'provideDefinition'),
    registerDocumentSymbolProvider: (selector, provider) => register('symbols', selector, provider, 'provideDocumentSymbols'),
    registerDocumentFormattingEditProvider: (selector, provider) => register('format', selector, provider, 'provideDocumentFormattingEdits'),
    createDiagnosticCollection(name = '') {
      const owner = 'diagnostics-' + ++next; const values = new Map(); let disposed = false;
      const collection = {
        name,
        set(uriOrEntries, items) {
          if (disposed) throw new Error('Diagnostic collection is disposed.');
          const entries = Array.isArray(uriOrEntries) ? uriOrEntries : [[uriOrEntries, items]];
          for (const [uri, items] of entries) { const key = uri.toString(); if (items === undefined) values.delete(key); else values.set(key, items.slice(0, 1000)); }
          publish(owner, values);
        },
        get: uri => values.get(uri.toString()) ?? [], has: uri => values.has(uri.toString()),
        delete(uri) { values.delete(uri.toString()); publish(owner, values); },
        clear() { values.clear(); publish(owner, values); },
        forEach(callback, thisArg) { for (const [uri, entries] of values) callback.call(thisArg, Uri.parse(uri), entries, collection); },
        *[Symbol.iterator]() { for (const [uri, entries] of values) yield [Uri.parse(uri), entries]; },
        dispose() { if (disposed) return; disposed = true; values.clear(); publish(owner, values); diagnostics.delete(collection); }
      };
      diagnostics.add(collection); return collection;
    },
    getDiagnostics(uri) {
      const merged = new Map();
      for (const collection of diagnostics) for (const [key, values] of collection) merged.set(key.toString(), [...(merged.get(key.toString()) ?? []), ...values]);
      return uri ? merged.get(uri.toString()) ?? [] : [...merged].map(([key, values]) => [Uri.parse(key), values]);
    }
  };
  const plain = value => typeof value === 'string' ? value : value?.value ?? '';
  const range = value => value ? { start: { line: value.start.line, character: value.start.character }, end: { line: value.end.line, character: value.end.character } } : undefined;
  const edits = values => values.map(edit => ({ range: range(edit.range), newText: edit.newText }));
  async function handle(message) {
    if (message.type === 'featureCancel') { inflight.get(message.id)?.cancel(); return; }
    const source = new CancellationTokenSource(); inflight.set(message.id, source);
    try {
      const document = documentFor(message.uri);
      if (message.version !== undefined && message.version !== document.version) throw new Error('Language request document version is stale.');
      const version = document.version;
      const position = new Position(message.position?.line ?? 0, message.position?.character ?? 0);
      const matches = [...providers.values()].filter(p => p.kind === message.kind && scoreSelector(p.selector, document) > 0).sort((a, b) => scoreSelector(b.selector, document) - scoreSelector(a.selector, document));
      const results = [];
      for (const entry of matches) {
        if (source.token.isCancellationRequested) break;
        try {
          const args = message.kind === 'completion' ? [document, position, source.token, { triggerKind: 0 }]
            : message.kind === 'symbols' ? [document, source.token]
            : message.kind === 'format' ? [document, message.options ?? { tabSize: 4, insertSpaces: true }, source.token]
            : [document, position, source.token];
          const result = await entry.provider[entry.method](...args);
          if (source.token.isCancellationRequested || !result) continue;
          if (message.kind === 'completion') for (const item of (Array.isArray(result) ? result : result.items ?? []).slice(0, 500)) {
            if (item.insertText !== undefined && typeof item.insertText !== 'string') throw new Error('Snippet completion insertion is not implemented.');
            results.push({ label: typeof item.label === 'string' ? item.label : item.label.label, detail: item.detail ?? '', insertText: item.insertText,
              range: range(item.range?.replacing ?? item.range), textEdit: item.textEdit ? edits([item.textEdit])[0] : undefined, additionalTextEdits: edits(item.additionalTextEdits ?? []), sortText: item.sortText, kind: item.kind });
          }
          else if (message.kind === 'hover') results.push({ contents: (Array.isArray(result.contents) ? result.contents : [result.contents]).map(plain), range: range(result.range) });
          else if (message.kind === 'definition') for (const location of Array.isArray(result) ? result : [result]) results.push({ uri: (location.uri ?? location.targetUri).toString(), range: range(location.range ?? location.targetSelectionRange) });
          else if (message.kind === 'symbols') {
            const visit = (values, depth = 0) => { if (depth > 20) return; for (const value of values.slice(0, 2000)) { results.push({ name: value.name, detail: value.detail ?? '', kind: value.kind, range: range(value.selectionRange ?? value.range ?? value.location?.range), uri: value.location?.uri?.toString() }); if (value.children) visit(value.children, depth + 1); } };
            visit(result);
          } else if (message.kind === 'format') { results.push(...edits(result)); break; }
        } catch (error) { send({ type: 'log', message: 'Language provider failed: ' + error.message }); }
      }
      if (document.version !== version) throw new Error('Document changed while the language provider was running.');
      send({ type: 'featureResult', id: message.id, version, results: results.slice(0, 2000), cancelled: source.token.isCancellationRequested });
    } catch (error) { send({ type: 'featureResult', id: message.id, error: error.message }); }
    finally { inflight.delete(message.id); source.dispose(); }
  }
  function dispose() { for (const source of inflight.values()) source.cancel(); for (const collection of [...diagnostics]) collection.dispose(); providers.clear(); }
  return { languages, handle, dispose };
}
