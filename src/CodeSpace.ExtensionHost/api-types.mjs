/** MIT. Portable VS Code-compatible value types and document snapshots. */
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
  update(data) { this.uri = Uri.parse(data.uri); this.fileName = data.path ?? this.uri.path; this.languageId = data.languageId ?? 'plaintext'; this.version = data.version ?? 1; this.isDirty = data.isDirty ?? false; this.isUntitled = this.uri.scheme === 'untitled'; this.isClosed = false; const sameText = this.text === (data.text ?? ''); this.text = data.text ?? ''; if (sameText && this.starts) return; this.eol = this.text.includes('\r\n') ? 2 : 1; this.starts = [0]; for (let i = 0; i < this.text.length; i++) if (this.text[i] === '\n') this.starts.push(i + 1); }
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
