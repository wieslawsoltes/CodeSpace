import { createExtensionHost } from './runtime.mjs';
const host = createExtensionHost(message => postMessage(message), { browser: true });
let operations = Promise.resolve();
self.onmessage = event => {
  const message = event.data;
  // RPC responses must bypass the operation queue: activation can await a UI request.
  if (message?.type === 'response' || message?.type === 'workspace') {
    Promise.resolve(host.handle(message)).catch(report);
  } else operations = operations.then(() => host.handle(message)).catch(report);
};
function report(error) { postMessage({ type: 'error', message: error?.stack ?? String(error) }); }
self.onunhandledrejection = event => { report(event.reason); event.preventDefault(); };
postMessage({ type: 'log', message: 'Browser extension worker ready. Only explicitly trusted code should be activated.' });
