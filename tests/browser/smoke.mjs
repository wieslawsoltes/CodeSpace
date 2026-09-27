import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
const output = process.env.OUTPUT_DIR ?? 'artifacts/browser-tests';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const messages = [], errors = [];
page.on('console', message => { const text = message.type() + ': ' + message.text(); messages.push(text); if (text.includes('[CodeSpace]')) console.log(text); });
page.on('pageerror', error => errors.push(error.stack ?? String(error)));
let passed = 0;
async function check(name, test) { console.log('START ' + name); await test(); console.log('PASS ' + name); passed++; }
async function enableAccessibility() { const enable = page.locator('#uno-enable-accessibility'); if (await enable.count()) await enable.dispatchEvent('click'); }
async function clickAction(name) {
  const action = page.getByRole('button', { name, exact: true }).first();
  const deadline = Date.now() + 10000;
  while (Date.now() < deadline) {
    await action.waitFor({ state: 'attached' });
    const bounds = await action.boundingBox();
    if (bounds && bounds.width > 0 && bounds.height > 0) {
      await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2); return;
    }
    // Uno's automation peer can appear before its first layout pass.
    await page.waitForTimeout(50);
  }
  assert.fail('Missing rendered control bounds: ' + name);
}
async function enterQuickPick(query) {
  const input = page.getByRole('textbox', { name: 'Command palette input', exact: true });
  await input.waitFor({ state: 'attached' }); await input.fill(query);
  await page.waitForFunction(value => [...document.querySelectorAll('input,textarea')].some(input => input.value === value), query);
  await page.waitForTimeout(300);
  await page.screenshot({ path: output + '/quick-pick-' + passed + '.png', fullPage: true });
  await input.press('Enter');
}
async function palette(query) { await clickAction('Search files (Ctrl+P)'); await enterQuickPick(query); }
async function command(query) { await page.keyboard.press('F1'); await enterQuickPick(query); }
async function focusEditor() { await page.mouse.click(620, 114); await page.waitForTimeout(200); }
try {
  await page.goto((process.env.BASE_URL ?? 'http://127.0.0.1:4173/') + '?e2e=1', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => !!globalThis.__codespaceTestState, null, { timeout: 90000 });
  await page.locator('.uno-loader').waitFor({ state: 'detached', timeout: 30000 });
  await enableAccessibility(); await page.waitForTimeout(600);
  await check('Uno startup and custom workbench', async () => {
    assert.equal(await page.locator('canvas').count() > 0, true);
    assert.equal(await page.evaluate(() => __codespaceTestState.groups[0].activeTab), 'src/Program.cs');
    await page.getByRole('button', { name: 'Search files (Ctrl+P)', exact: true }).waitFor({ state: 'attached' });
  });
  await page.screenshot({ path: output + '/workbench.png', fullPage: true });
  await check('quick open changes active document', async () => { await palette('src/Workbench.cs'); await page.waitForFunction(() => __codespaceTestState.groups[0].activeTab === 'src/Workbench.cs'); });
  await check('custom editor typing and undo preserve tab controls', async () => {
    await focusEditor(); await page.keyboard.press('Control+End');
    const split = page.getByRole('button', { name: 'Split editor right (Ctrl+\\)', exact: true });
    const identity = await split.getAttribute('id'); assert(identity);
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await page.keyboard.type('// browser smoke', { delay: 20 });
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].endsWith('// browser smoke'));
    for (let i = 0; i < '// browser smoke'.length; i++) await page.keyboard.press('Control+z');
    await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
    await page.waitForTimeout(300); assert.equal(await split.getAttribute('id'), identity, 'Typing must not rebuild the tab strip');
  });
  await check('selection, deletion and redo use the document history', async () => {
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await page.keyboard.press('Control+Home');
    for (let i = 0; i < 5; i++) await page.keyboard.press('Shift+ArrowRight');
    await page.keyboard.press('Delete');
    await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value.slice(5), original);
    await page.keyboard.press('Control+z'); await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
    await page.keyboard.press('Control+y'); await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value.slice(5), original);
    await page.keyboard.press('Control+z'); await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
    await page.keyboard.press('Control+a'); await page.keyboard.type('X');
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'] === 'X');
    await page.keyboard.press('Control+z'); await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
    await page.keyboard.press('Escape');
  });
  await check('multiple matching selections edit and undo atomically', async () => {
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await page.keyboard.press('Control+Home'); for (let i = 0; i < 3; i++) await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Home'); await page.keyboard.press('Control+ArrowRight');
    await page.keyboard.press('Control+d'); await page.keyboard.press('Control+d'); await page.keyboard.type('T');
    await page.waitForFunction(() => { const text = __codespaceTestState.files['src/Workbench.cs']; return text.includes('T Theme') && text.includes('public T[] Panels'); });
    await page.keyboard.press('Control+z'); await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
    await page.keyboard.press('Escape'); await page.keyboard.press('Control+End');
  });
  await check('split editor creates a real second group', async () => { await clickAction('Split editor right (Ctrl+\\)'); await page.waitForFunction(() => __codespaceTestState.groups.length === 2); });
  await page.screenshot({ path: output + '/split-editors.png', fullPage: true });
  await check('sidebar toggle is functional', async () => {
    await clickAction('Toggle side bar'); await page.waitForFunction(() => !__codespaceTestState.sidebarVisible);
    await clickAction('Toggle side bar'); await page.waitForFunction(() => __codespaceTestState.sidebarVisible);
  });
  await check('extension worker and UI RPC integration', async () => {
    await command('Run bundled compatibility probe');
    await page.getByRole('button', { name: 'Run probe', exact: true }).dispatchEvent('click');
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Hello from the VS Code API compatibility probe.')), null, { timeout: 30000 });
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Executed codespace.hello')));
  });
  await check('fold and unfold operate on real visual rows', async () => {
    await command('Editor: Fold All'); await page.waitForFunction(() => __codespaceTestState.editors.some(e => e.folds.length > 0));
    await page.screenshot({ path: output + '/folding.png', fullPage: true });
    await command('Editor: Unfold All'); await page.waitForFunction(() => __codespaceTestState.editors.every(e => e.folds.length === 0));
  });
  await check('extension completion inserts into the custom editor and undoes', async () => {
    await focusEditor(); await page.keyboard.press('Control+End');
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await command('Editor: Suggest Completions'); await enterQuickPick('CodeSpaceProviderCompletion');
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].includes('CodeSpaceProviderCompletion'));
    await page.keyboard.press('Control+z'); await page.waitForFunction(text => __codespaceTestState.files['src/Workbench.cs'] === text, original);
  });
  await check('extension formatting is applied as one undoable transaction', async () => {
    await focusEditor(); await page.keyboard.press('Control+End'); await page.keyboard.type('// format probe   ', { delay: 15 });
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].endsWith('// format probe   '));
    const before = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await command('Editor: Format Document'); await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].endsWith('// format probe'));
    await page.keyboard.press('Control+z'); await page.waitForFunction(text => __codespaceTestState.files['src/Workbench.cs'] === text, before);
  });
  await check('extension hover and symbols reach real workbench controls', async () => {
    await command('Editor: Show Hover');
    await page.getByLabel('CodeSpace extension hover is connected to the custom Uno editor.', { exact: true }).waitFor({ state: 'attached' });
    await page.screenshot({ path: output + '/extension-hover.png', fullPage: true });
    await page.getByRole('button', { name: 'Close', exact: true }).dispatchEvent('click');
    await command('Go to Symbol in Editor');
    await page.getByRole('button', { name: 'Extension symbol', exact: true }).waitFor({ state: 'attached' });
    await page.keyboard.press('Escape');
  });
  await check('settings JSON binds live and survives malformed intermediate text', async () => {
    await palette('.vscode/settings.json');
    await focusEditor(); await page.keyboard.press('Control+a');
    await page.keyboard.insertText('{"editor.fontSize":18,"editor.tabSize":2,"editor.minimap.enabled":false}');
    await page.waitForFunction(() => __codespaceTestState.options.FontSize === 18 && __codespaceTestState.options.TabSize === 2 && !__codespaceTestState.options.Minimap);
    await page.keyboard.press('Control+End'); await page.keyboard.type('{');
    await page.waitForFunction(() => __codespaceTestState.files['.vscode/settings.json'].endsWith('}{'));
    assert.equal(await page.evaluate(() => __codespaceTestState.options.FontSize), 18);
    await page.keyboard.press('Control+z'); await page.waitForFunction(() => __codespaceTestState.files['.vscode/settings.json'].endsWith('}'));
  });
  await check('dirty baselines and settings recover after reload', async () => {
    await page.waitForTimeout(1800); await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !!globalThis.__codespaceTestState, null, { timeout: 90000 });
    await page.waitForFunction(() => __codespaceTestState.groups.length === 2);
    await page.waitForFunction(() => __codespaceTestState.options.FontSize === 18 && __codespaceTestState.editors.some(e => e.dirty));
  });
  console.log(JSON.stringify({ passed, pageErrors: errors }, null, 2));
  if (errors.length) throw new Error('Browser page errors: ' + errors.join('\n'));
} catch (error) {
  console.error('FAILED WORKFLOW', passed + 1, error.stack);
  await writeFile(output + '/failure.txt', String(error.stack));
  await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/failure.html', await page.content());
  const diagnostics = await page.evaluate(() => ({ url: location.href, state: globalThis.__codespaceTestState, events: globalThis.__codespaceTestEvents, workerAvailable: !!globalThis.CodeSpaceHost, focused: document.activeElement?.outerHTML, inputs: [...document.querySelectorAll('input,textarea')].map(input => ({ id: input.id, value: input.value, bounds: input.getBoundingClientRect().toJSON() })) }));
  console.error('Browser diagnostics:', JSON.stringify(diagnostics)); await writeFile(output + '/diagnostics.json', JSON.stringify(diagnostics, null, 2)); throw error;
} finally {
  await writeFile(output + '/console.log', messages.join('\n') + '\n' + errors.join('\n'));
  await writeFile(output + '/result.json', JSON.stringify({ passed, errors }, null, 2)); await browser.close();
}
