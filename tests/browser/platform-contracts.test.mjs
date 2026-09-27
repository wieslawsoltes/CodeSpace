// Desktop process transport contracts run alongside browser input adapter contracts.
import '../extension-host/process.test.mjs';
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

// Exercise the exact JavaScript embedded in the production browser adapter.
const source = readFileSync(new URL('../../src/CodeSpace.App/BrowserKeyboardInput.cs', import.meta.url), 'utf8');
const script = /InvokeJS\("""\s*([\s\S]*?)\s*"""\)/.exec(source)?.[1];
assert(script, 'The browser keyboard adapter must contain its initialization script.');
function fixture() {
  const listeners = { window: new Map(), document: new Map() };
  const target = name => ({ addEventListener(type, callback, options) {
    const entries = listeners[name].get(type) ?? [];
    entries.push({ callback, options }); listeners[name].set(type, entries);
  } });
  const context = { window: target('window'), document: { ...target('document'), hidden: false } };
  runInNewContext(script, context);
  return { context, listeners, state: context.CodeSpaceKeys,
    emit(type, values = {}, owner = 'window') {
      const event = { isTrusted: true, ctrlKey: false, shiftKey: false, altKey: false, metaKey: false, ...values };
      for (const listener of listeners[owner].get(type) ?? []) listener.callback(event);
    }
  };
}

test('Control release is observed after native input changes focus', () => {
  const f = fixture(); f.emit('keydown', { key: 'Control', ctrlKey: true });
  assert.equal(f.state.control, true);
  f.emit('keydown', { key: 'End', ctrlKey: true });
  f.emit('keyup', { key: 'End', ctrlKey: true });
  f.emit('keyup', { key: 'Control' });
  assert.equal(f.state.control, false);
  f.emit('keydown', { key: 'n' }); assert.equal(f.state.control, false);
});
test('synthetic accessibility replays cannot relatch a released key', () => {
  const f = fixture(); f.emit('keyup', { key: 'Control' });
  f.emit('keydown', { key: 'Control', ctrlKey: true, isTrusted: false });
  assert.equal(f.state.control, false);
});
test('each physical event carries a complete current modifier snapshot', () => {
  const f = fixture(); f.emit('keydown', { shiftKey: true, altKey: true, metaKey: true });
  assert.equal(f.state.shift, true); assert.equal(f.state.alt, true); assert.equal(f.state.meta, true);
  f.emit('keydown', { key: 'a' });
  assert(Object.values(f.state).every(value => value === false));
});
test('window blur clears keys whose keyup occurred outside the application', () => {
  const f = fixture(); f.emit('keydown', { ctrlKey: true, shiftKey: true }); f.emit('blur');
  assert(Object.values(f.state).every(value => value === false));
});
test('hidden-page transition clears modifiers without disturbing visible-page input', () => {
  const f = fixture(); f.emit('keydown', { ctrlKey: true });
  f.emit('visibilitychange', {}, 'document'); assert.equal(f.state.control, true);
  f.context.document.hidden = true; f.emit('visibilitychange', {}, 'document');
  assert.equal(f.state.control, false);
});
test('pointer and passive wheel listeners refresh zoom/selection modifiers', () => {
  const f = fixture(); f.emit('pointerdown', { altKey: true }); assert.equal(f.state.alt, true);
  f.emit('pointermove'); assert.equal(f.state.alt, false);
  f.emit('wheel', { ctrlKey: true }); assert.equal(f.state.control, true);
  const options = f.listeners.window.get('wheel')[0].options;
  assert.equal(options.capture, true); assert.equal(options.passive, true);
  assert.equal(f.listeners.window.get('keydown')[0].options, true);
});
test('initialization is idempotent and preserves registered listener identity', () => {
  const f = fixture(); const state = f.context.CodeSpaceKeys;
  runInNewContext(script, f.context);
  assert.equal(f.context.CodeSpaceKeys, state);
  assert.equal(f.listeners.window.get('keydown').length, 1);
});
