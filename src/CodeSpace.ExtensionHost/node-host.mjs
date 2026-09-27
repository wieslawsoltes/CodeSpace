import { createInterface } from 'node:readline';
import { createRequire } from 'node:module';
import { createExtensionHost } from './runtime.mjs';
// The Node host is a full-authority local process, NOT a security sandbox.
const send = message => process.stdout.write(JSON.stringify(message) + '\n');
const host = createExtensionHost(send, { browser: false, externalRequire: createRequire(import.meta.url) });
const input = createInterface({ input: process.stdin, crlfDelay: Infinity });
let operations = Promise.resolve();
const report = error => send({ type: 'error', message: error?.stack ?? String(error) });
input.on('line', line => {
  if (line.length > 128 * 1024 * 1024) { report(new Error('Host message too large.')); return; }
  try {
    const message = JSON.parse(line);
    if (message.type === 'response' || message.type === 'workspace') Promise.resolve(host.handle(message)).catch(report);
    else operations = operations.then(() => host.handle(message)).catch(report);
  } catch (error) { report(error); }
});
input.on('close', () => host.dispose().catch(report));
process.on('unhandledRejection', report);
send({ type: 'log', message: 'Node extension host ready. Running trusted local code with full process authority.' });
