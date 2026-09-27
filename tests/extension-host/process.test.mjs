import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';

const entry = fileURLToPath(new URL('../../src/CodeSpace.ExtensionHost/node-host.mjs', import.meta.url));
const manifest = { publisher: 'codespace', name: 'process-probe', version: '0.1.0', main: './extension.js' };

async function startHost(t) {
  const child = spawn(process.execPath, [entry], { stdio: ['pipe', 'pipe', 'pipe'] });
  const messages = [], requests = [], waiters = new Set(); let stderr = '';
  child.stderr.setEncoding('utf8'); child.stderr.on('data', text => stderr += text);
  const exited = new Promise((resolve, reject) => { child.once('exit', (code, signal) => resolve({ code, signal })); child.once('error', reject); });
  const lines = createInterface({ input: child.stdout });
  const send = message => child.stdin.write(JSON.stringify(message) + '\n');
  lines.on('line', line => {
    let message;
    try { message = JSON.parse(line); } catch { message = { type: 'invalid-protocol', line }; }
    messages.push(message);
    if (message.type === 'request') {
      requests.push(message);
      // This is an isolated process/transport test, not a desktop UI test.
      // Reply outside the host's serialized activation queue, as the Uno adapter does.
      send({ type: 'response', id: message.id, result: message.method === 'workspace.applyEdits' ? true : null });
    }
    for (const waiter of [...waiters]) if (waiter.predicate(message)) { clearTimeout(waiter.timer); waiters.delete(waiter); waiter.resolve(message); }
  });
  function waitFor(predicate) {
    const existing = messages.find(predicate); if (existing) return Promise.resolve(existing);
    return new Promise((resolve, reject) => {
      const waiter = { predicate, resolve, timer: setTimeout(() => { waiters.delete(waiter); reject(new Error('Host response timed out. ' + JSON.stringify(messages) + '\n' + stderr)); }, 5000) };
      waiters.add(waiter);
    });
  }
  t.after(async () => {
    for (const waiter of waiters) clearTimeout(waiter.timer);
    child.stdin.end(); const killer = setTimeout(() => child.kill(), 3000);
    try { const result = await exited; assert.equal(result.code, 0, 'Node host must exit cleanly: ' + stderr); }
    finally { clearTimeout(killer); lines.close(); }
    assert.equal(messages.some(message => message.type === 'invalid-protocol'), false, 'stdout must contain only JSON protocol records');
  });
  await waitFor(message => message.type === 'log');
  return { send, waitFor, messages, requests };
}
function activate(host, source, trusted = true) { host.send({ type: 'activate', manifest, trusted, files: { 'extension.js': source } }); }

test('Node process activation awaits UI RPC, resolves builtins, and disposes commands', { timeout: 10000 }, async t => {
  const host = await startHost(t);
  activate(host, `
    const vscode = require('vscode'); const path = require('node:path');
    exports.activate = async context => {
      await vscode.window.showInformationMessage('activation ready');
      context.subscriptions.push(vscode.commands.registerCommand('process.basename', value => path.basename(value)));
    };
  `);
  // Deliberately queue execution while activation is awaiting its UI response.
  host.send({ type: 'execute', command: 'process.basename', args: ['src/Program.cs'] });
  const result = await host.waitFor(message => message.type === 'result' && message.command === 'process.basename');
  assert.equal(result.result, 'Program.cs');
  assert(host.requests.some(request => request.method === 'window.showInformationMessage' && request.params.message === 'activation ready'));
  host.send({ type: 'deactivate', id: 'codespace.process-probe' });
  await host.waitFor(message => message.type === 'unregisterCommand' && message.command === 'process.basename');
});

test('Node process document edits preserve UTF-16 offsets across the wire', { timeout: 10000 }, async t => {
  const host = await startHost(t);
  host.send({ type: 'workspace', name: 'probe', documents: [{ uri: 'codespace:///a.cs', path: 'a.cs', text: 'A😀B', languageId: 'csharp', version: 1 }], activeUri: 'codespace:///a.cs' });
  activate(host, `
    const vscode = require('vscode');
    exports.activate = context => context.subscriptions.push(vscode.commands.registerCommand('process.edit', async () => {
      return vscode.window.activeTextEditor.edit(edit => edit.replace(new vscode.Range(0, 1, 0, 3), 'X'));
    }));
  `);
  host.send({ type: 'execute', command: 'process.edit', args: [] });
  const result = await host.waitFor(message => message.type === 'result' && message.command === 'process.edit');
  assert.equal(result.result, true);
  assert.deepEqual(host.requests.find(request => request.method === 'workspace.applyEdits').params,
    { uri: 'codespace:///a.cs', edits: [{ start: 1, length: 2, text: 'X' }] });
});

test('Node process rejects untrusted activation and remains usable after unsupported APIs', { timeout: 10000 }, async t => {
  const host = await startHost(t);
  activate(host, `throw new Error('must never execute');`, false);
  await host.waitFor(message => message.type === 'error' && message.message.includes('Explicit extension trust'));
  assert.equal(host.messages.some(message => message.message?.includes('must never execute')), false);
  activate(host, `exports.activate = () => require('vscode').window.createWebviewPanel();`);
  await host.waitFor(message => message.type === 'error' && message.message.includes('does not implement vscode.window.createWebviewPanel'));
  activate(host, `exports.activate = context => context.subscriptions.push(require('vscode').commands.registerCommand('process.alive', () => 'alive'));`);
  host.send({ type: 'execute', command: 'process.alive', args: [] });
  assert.equal((await host.waitFor(message => message.type === 'result' && message.command === 'process.alive')).result, 'alive');
});
