import test from 'node:test';
import assert from 'node:assert/strict';
import { createExtensionHost, Uri, Position, Range, WorkspaceEdit, Selection } from '../runtime.mjs';
const uri = 'codespace:///src/test.cs';
async function fixture() {
  const messages = []; let host;
  host = createExtensionHost(message => {
    messages.push(message);
    if (message.type === 'request') queueMicrotask(() => host.handle({ type: 'response', id: message.id, result: true }));
  });
  await host.handle({ type: 'workspace', documents: [{ uri, path: 'src/test.cs', text: 'class Test {}\n', version: 1, languageId: 'csharp' }], activeUri: uri });
  return { host, messages, feature: async kind => { await host.handle({ type: 'featureRequest', id: 42, kind, uri, version: 1, position: { line: 0, character: 3 } }); return messages.filter(m => m.type === 'featureResult').at(-1); } };
}
test('completion providers run against real text documents and normalize edits', async () => {
  const { host, feature } = await fixture();
  host.api.languages.registerCompletionItemProvider('csharp', { provideCompletionItems(document) {
    assert.equal(document.getText(), 'class Test {}\n'); const item = new host.api.CompletionItem('Test', 6); item.range = new Range(0, 0, 0, 5); item.additionalTextEdits = [host.api.TextEdit.insert(new Position(1, 0), '// more')]; return new host.api.CompletionList([item]);
  } });
  const result = await feature('completion'); assert.equal(result.results[0].label, 'Test'); assert.equal(result.results[0].additionalTextEdits[0].newText, '// more'); await host.dispose();
});
test('selector language and scheme filters exclude unrelated providers', async () => {
  const { host, feature } = await fixture(); let calls = 0;
  host.api.languages.registerHoverProvider('python', { provideHover() { calls++; } });
  host.api.languages.registerHoverProvider({ language: 'csharp', scheme: 'file' }, { provideHover() { calls++; } });
  assert.equal((await feature('hover')).results.length, 0); assert.equal(calls, 0); await host.dispose();
});
test('glob selector and registration disposal', async () => {
  const { host, feature } = await fixture(); const registration = host.api.languages.registerHoverProvider({ language: 'csharp', pattern: '**/*.cs' }, { provideHover() { return new host.api.Hover(new host.api.MarkdownString('hello')); } });
  assert.equal((await feature('hover')).results[0].contents[0], 'hello'); registration.dispose(); assert.equal((await feature('hover')).results.length, 0); await host.dispose();
});
test('definition locations survive transport', async () => {
  const { host, feature } = await fixture(); host.api.languages.registerDefinitionProvider('*', { provideDefinition(document) { return new host.api.Location(document.uri, new Range(0, 6, 0, 10)); } });
  const result = (await feature('definition')).results[0]; assert.equal(result.uri, uri); assert.equal(result.range.start.character, 6); await host.dispose();
});
test('nested document symbols flatten with their selection ranges', async () => {
  const { host, feature } = await fixture(); host.api.languages.registerDocumentSymbolProvider('csharp', { provideDocumentSymbols() { const root = new host.api.DocumentSymbol('Root', '', 4, new Range(0, 0, 1, 0), new Range(0, 6, 0, 10)); root.children.push(new host.api.DocumentSymbol('Child', '', 5, new Range(0, 0, 0, 1), new Range(0, 0, 0, 1))); return [root]; } });
  assert.deepEqual((await feature('symbols')).results.map(r => r.name), ['Root', 'Child']); await host.dispose();
});
test('formatters receive options and produce range-based edits', async () => {
  const { host, feature } = await fixture(); host.api.languages.registerDocumentFormattingEditProvider('csharp', { provideDocumentFormattingEdits(document, options) { assert.equal(options.tabSize, 4); return [host.api.TextEdit.insert(new Position(0, 0), '// formatted\n')]; } });
  assert.equal((await feature('format')).results[0].newText, '// formatted\n'); await host.dispose();
});
test('a failed provider does not suppress other providers', async () => {
  const { host, feature, messages } = await fixture(); host.api.languages.registerHoverProvider('*', { provideHover() { throw new Error('expected failure'); } }); host.api.languages.registerHoverProvider('*', { provideHover() { return new host.api.Hover('valid'); } });
  assert.equal((await feature('hover')).results[0].contents[0], 'valid'); assert(messages.some(m => m.message?.includes('expected failure'))); await host.dispose();
});
test('cancelled asynchronous feature does not return stale results', async () => {
  const { host, messages } = await fixture(); let release, cancelled = false;
  host.api.languages.registerHoverProvider('*', { async provideHover(document, position, token) { token.onCancellationRequested(() => cancelled = true); await new Promise(r => release = r); return new host.api.Hover('late'); } });
  const pending = host.handle({ type: 'featureRequest', id: 5, kind: 'hover', uri, version: 1 });
  await host.handle({ type: 'featureCancel', id: 5 }); release(); await pending;
  assert(cancelled); const result = messages.find(m => m.type === 'featureResult'); assert(result.cancelled); assert.equal(result.results.length, 0); await host.dispose();
});
test('version changes reject stale language responses', async () => {
  const { host, messages } = await fixture(); let release;
  host.api.languages.registerHoverProvider('*', { async provideHover() { await new Promise(r => release = r); return new host.api.Hover('late'); } });
  const pending = host.handle({ type: 'featureRequest', id: 6, kind: 'hover', uri, version: 1 });
  await host.handle({ type: 'workspaceDelta', documents: [{ uri, text: 'changed', version: 2, languageId: 'csharp' }], activeUri: uri }); release(); await pending;
  assert.match(messages.find(m => m.type === 'featureResult').error, /changed/); await host.dispose();
});
test('diagnostic collections retain ownership and clear on disposal', async () => {
  const { host, messages } = await fixture(); const a = host.api.languages.createDiagnosticCollection('a'), b = host.api.languages.createDiagnosticCollection('b'); const resource = Uri.parse(uri);
  a.set(resource, [new host.api.Diagnostic(new Range(0, 0, 0, 1), 'a')]); b.set(resource, [new host.api.Diagnostic(new Range(0, 0, 0, 1), 'b')]);
  assert.equal(host.api.languages.getDiagnostics(resource).length, 2); a.dispose(); assert.equal(host.api.languages.getDiagnostics(resource).length, 1); assert.deepEqual(messages.filter(m => m.type === 'diagnostics').at(-1).entries, []); await host.dispose();
});
test('configuration precedence, inspection and defensive copies', async () => {
  const { host } = await fixture(); await host.handle({ type: 'configuration', defaults: { 'editor.tabSize': 4, 'ext.object': { a: 1 } }, global: { 'editor.tabSize': 2 }, workspace: { 'editor.tabSize': 8, 'ext.object': { b: 2 } } });
  const config = host.api.workspace.getConfiguration('editor'); assert.equal(config.get('tabSize'), 8); assert.deepEqual(config.inspect('tabSize'), { key: 'editor.tabSize', defaultValue: 4, globalValue: 2, workspaceValue: 8 });
  const copy = host.api.workspace.getConfiguration('ext').get('object'); copy.a = 9; assert.equal(host.api.workspace.getConfiguration('ext').get('object').a, 1); await host.dispose();
});
test('configuration hierarchy and affected-section events', async () => {
  const { host } = await fixture(); let event;
  host.api.workspace.onDidChangeConfiguration(e => event = e);
  await host.handle({ type: 'configuration', workspace: { 'editor.minimap.enabled': false } });
  assert(event.affectsConfiguration('editor')); assert(event.affectsConfiguration('editor.minimap')); assert(!event.affectsConfiguration('files'));
  assert.equal(host.api.workspace.getConfiguration('editor').get('minimap').enabled, false); await host.dispose();
});
test('configuration update is bridged and unsupported scope rejects explicitly', async () => {
  const { host, messages } = await fixture(); await host.api.workspace.getConfiguration('editor').update('tabSize', 2, host.api.ConfigurationTarget.Workspace);
  assert.deepEqual(messages.find(m => m.method === 'configuration.update').params, { key: 'editor.tabSize', value: 2, remove: false, target: 2 });
  assert.throws(() => host.api.workspace.getConfiguration('editor', Uri.parse(uri)), /scoped/);
  await assert.rejects(host.api.workspace.getConfiguration('editor').update('tabSize', 2, 3), /targets/); await host.dispose();
});
test('workspace delta retains unchanged document objects and correct previous ranges', async () => {
  const { host } = await fixture(); const before = host.api.workspace.textDocuments[0]; let event;
  host.api.workspace.onDidChangeTextDocument(e => event = e);
  await host.handle({ type: 'workspaceDelta', documents: [{ uri: 'codespace:///other', text: 'other', version: 1 }], activeUri: uri });
  assert.equal(host.api.workspace.textDocuments[0], before); assert.equal(event, undefined);
  await host.handle({ type: 'workspaceDelta', documents: [{ uri, text: 'new', version: 2 }], activeUri: uri });
  assert.equal(event.contentChanges[0].range.end.line, 1); assert.equal(event.contentChanges[0].rangeLength, 14); await host.dispose();
});
test('delta deletion closes only the requested documents', async () => {
  const { host } = await fixture(); let closed = 0; host.api.workspace.onDidCloseTextDocument(() => closed++);
  await host.handle({ type: 'workspaceDelta', documents: [{ uri: 'codespace:///keep', text: 'keep' }], deleted: [uri] });
  assert.equal(closed, 1); assert.equal(host.api.workspace.textDocuments.length, 1); await host.dispose();
});
test('multi-file edit is a single versioned atomic transport request', async () => {
  const { host, messages } = await fixture(); await host.handle({ type: 'workspaceDelta', documents: [{ uri: 'codespace:///b', text: 'abc', version: 4 }], activeUri: uri });
  const edit = new WorkspaceEdit(); edit.insert(Uri.parse(uri), new Position(0, 0), 'a'); edit.replace(Uri.parse('codespace:///b'), new Range(0, 1, 0, 2), 'X');
  assert.equal(await host.api.workspace.applyEdit(edit), true); const requests = messages.filter(m => m.method === 'workspace.applyWorkspaceEdit'); assert.equal(requests.length, 1); assert.equal(requests[0].params.documents[1].version, 4); await host.dispose();
});
test('stable editor objects synchronize selections and report actual changes', async () => {
  const { host, messages } = await fixture(); const editor = host.api.window.activeTextEditor; let events = 0;
  host.api.window.onDidChangeTextEditorSelection(() => events++);
  await host.handle({ type: 'workspaceDelta', documents: [], activeUri: uri, selections: [{ uri, values: [{ anchor: 1, active: 4 }] }] });
  assert.equal(host.api.window.activeTextEditor, editor); assert.equal(editor.selection.active.character, 4); assert.equal(events, 1);
  editor.selection = new Selection(0, 2, 0, 5); await Promise.resolve(); assert.equal(messages.find(m => m.method === 'window.setSelections').params.selections[0].active, 5); await host.dispose();
});
test('unchanged snapshots retain the same line-index allocation', async () => {
  const { host } = await fixture(); const document = host.api.workspace.textDocuments[0]; const starts = document.starts;
  await host.handle({ type: 'workspaceDelta', documents: [{ uri, text: document.getText(), version: 1, isDirty: true }], activeUri: uri });
  assert.equal(document.starts, starts); assert.equal(document.isDirty, true); await host.dispose();
});
