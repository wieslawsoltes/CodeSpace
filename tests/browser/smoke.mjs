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
async function check(name, test) { await test(); console.log('PASS ' + name); passed++; }
async function enableAccessibility() { const enable = page.locator('#uno-enable-accessibility'); if (await enable.count()) await enable.dispatchEvent('click'); }
async function clickControl(role, name) {
  // Semantic bounds locate the custom-drawn control; input goes to the real Skia surface.
  const action = page.getByRole(role, { name, exact: true }).first();
  await action.waitFor({ state: 'attached' });
  const bounds = await action.boundingBox();
  assert(bounds && bounds.width > 0 && bounds.height > 0, 'Missing control bounds: ' + name);
  await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
}
const clickAction = name => clickControl('button', name);
async function enterQuickPick(query) {
  await clickControl('textbox', 'Command palette input');
  await page.waitForTimeout(200);
  await page.keyboard.press('Control+a');
  await page.keyboard.type(query, { delay: 20 });
  // Uno commits native text-input events asynchronously. Do not select a stale result.
  await page.waitForFunction(value => [...document.querySelectorAll('input,textarea')].some(input => input.value === value), query);
  await page.waitForTimeout(300);
  await page.screenshot({ path: output + '/quick-pick-' + passed + '.png', fullPage: true });
  await page.keyboard.press('Enter');
}
async function palette(query) { await clickAction('Search files (Ctrl+P)'); await enterQuickPick(query); }
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
  await check('custom editor typing and undo', async () => {
    await page.mouse.click(620, 114); await page.keyboard.press('Control+End');
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await page.keyboard.type('// browser smoke', { delay: 20 });
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].endsWith('// browser smoke'));
    for (let i = 0; i < '// browser smoke'.length; i++) await page.keyboard.press('Control+z');
    await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
  });
  await check('split editor creates a real second group', async () => { await clickAction('Split editor right (Ctrl+\\)'); await page.waitForFunction(() => __codespaceTestState.groups.length === 2); });
  await page.screenshot({ path: output + '/split-editors.png', fullPage: true });
  await check('sidebar toggle is functional', async () => {
    await clickAction('Toggle side bar'); await page.waitForFunction(() => !__codespaceTestState.sidebarVisible);
    await clickAction('Toggle side bar'); await page.waitForFunction(() => __codespaceTestState.sidebarVisible);
  });
  await check('extension worker and UI RPC integration', async () => {
    await page.keyboard.press('F1'); await enterQuickPick('Run bundled compatibility probe');
    await clickAction('Run probe');
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Hello from the VS Code API compatibility probe.')), null, { timeout: 30000 });
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Executed codespace.hello')));
  });
  await check('recovery survives reload', async () => {
    await page.waitForTimeout(1800); await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !!globalThis.__codespaceTestState, null, { timeout: 90000 });
    await page.waitForFunction(() => __codespaceTestState.groups.length === 2);
  });
  console.log(JSON.stringify({ passed, pageErrors: errors }, null, 2));
  if (errors.length) throw new Error('Browser page errors: ' + errors.join('\n'));
} catch (error) {
  await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/failure.html', await page.content());
  const diagnostics = await page.evaluate(() => ({ url: location.href, state: globalThis.__codespaceTestState, events: globalThis.__codespaceTestEvents, workerAvailable: !!globalThis.CodeSpaceHost, title: document.title, focused: document.activeElement?.outerHTML, inputs: [...document.querySelectorAll('input,textarea')].map(input => ({ id: input.id, value: input.value, bounds: input.getBoundingClientRect().toJSON() })) }));
  console.error('Browser diagnostics:', JSON.stringify(diagnostics)); await writeFile(output + '/diagnostics.json', JSON.stringify(diagnostics, null, 2)); throw error;
} finally {
  await writeFile(output + '/console.log', messages.join('\n') + '\n' + errors.join('\n'));
  await writeFile(output + '/result.json', JSON.stringify({ passed, errors }, null, 2)); await browser.close();
}
