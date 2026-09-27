import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
const output = process.env.OUTPUT_DIR ?? 'artifacts/browser-tests';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const messages = [], errors = [];
page.on('console', message => messages.push(message.type() + ': ' + message.text()));
page.on('pageerror', error => errors.push(error.stack ?? String(error)));
let passed = 0;
async function check(name, test) { await test(); console.log('PASS ' + name); passed++; }
async function palette(query) {
  await page.getByRole('button', { name: 'Search files (Ctrl+P)', exact: true }).click();
  const input = page.getByRole('textbox', { name: 'Command palette input', exact: true });
  await input.fill(query); await input.press('Enter');
}
try {
  await page.goto((process.env.BASE_URL ?? 'http://127.0.0.1:4173/') + '?e2e=1', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => !!globalThis.__codespaceTestState, { timeout: 180000 });
  await check('Uno startup and custom workbench', async () => {
    assert.equal(await page.locator('canvas').count() > 0, true);
    assert.equal(await page.evaluate(() => __codespaceTestState.groups[0].activeTab), 'src/Program.cs');
    assert.equal(await page.getByRole('button', { name: 'Search files (Ctrl+P)', exact: true }).count(), 1);
  });
  await page.screenshot({ path: output + '/workbench.png', fullPage: true });
  await check('quick open changes active document', async () => {
    await palette('src/Workbench.cs');
    await page.waitForFunction(() => __codespaceTestState.groups[0].activeTab === 'src/Workbench.cs');
  });
  await check('custom editor typing and undo', async () => {
    const input = page.getByRole('textbox', { name: 'Code editor input: src/Workbench.cs', exact: true });
    await input.focus(); await page.keyboard.press('Control+End');
    const original = await page.evaluate(() => __codespaceTestState.files['src/Workbench.cs']);
    await page.keyboard.insertText('// browser smoke');
    await page.waitForFunction(() => __codespaceTestState.files['src/Workbench.cs'].endsWith('// browser smoke'));
    await page.keyboard.press('Control+z');
    await page.waitForFunction(value => __codespaceTestState.files['src/Workbench.cs'] === value, original);
  });
  await check('split editor creates a real second group', async () => {
    await page.getByRole('button', { name: 'Split editor right (Ctrl+\\)', exact: true }).first().click();
    await page.waitForFunction(() => __codespaceTestState.groups.length === 2);
  });
  await page.screenshot({ path: output + '/split-editors.png', fullPage: true });
  await check('sidebar toggle is functional', async () => {
    await page.getByRole('button', { name: 'Toggle side bar', exact: true }).click();
    await page.waitForFunction(() => !__codespaceTestState.sidebarVisible);
    await page.getByRole('button', { name: 'Toggle side bar', exact: true }).click();
    await page.waitForFunction(() => __codespaceTestState.sidebarVisible);
  });
  await check('extension worker and UI RPC integration', async () => {
    await page.keyboard.press('F1');
    const input = page.getByRole('textbox', { name: 'Command palette input', exact: true });
    await input.fill('Run bundled compatibility probe'); await input.press('Enter');
    await page.getByRole('button', { name: 'Run probe', exact: true }).click();
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Hello from the VS Code API compatibility probe.')), { timeout: 30000 });
    await page.waitForFunction(() => __codespaceTestEvents.some(message => message.includes('Executed codespace.hello')));
  });
  await check('recovery survives reload', async () => {
    await page.waitForTimeout(1600);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !!globalThis.__codespaceTestState, { timeout: 180000 });
    await page.waitForFunction(() => __codespaceTestState.groups.length === 2);
  });
  console.log(JSON.stringify({ passed, pageErrors: errors }, null, 2));
  if (errors.length) throw new Error('Browser page errors: ' + errors.join('\n'));
} catch (error) {
  await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/failure.html', await page.content());
  throw error;
} finally {
  await writeFile(output + '/console.log', messages.join('\n') + '\n' + errors.join('\n'));
  await writeFile(output + '/result.json', JSON.stringify({ passed, errors }, null, 2));
  await browser.close();
}
