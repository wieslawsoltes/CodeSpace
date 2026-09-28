import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

// A fresh browser context proves caret/fold/scroll changes save independently of edits.
const output = (process.env.OUTPUT_DIR ?? 'artifacts/browser-tests') + '/view-recovery';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_EXECUTABLE_PATH, headless: true,
  args: ['--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
const errors = [], log = [];
page.on('pageerror', error => errors.push(String(error)));
page.on('console', message => log.push(message.type() + ': ' + message.text()));
let passed = 0;
try {
  await page.goto((process.env.BASE_URL ?? 'http://127.0.0.1:4173/') + '?e2e=1', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => !!globalThis.__codespaceTestState, null, { timeout: 90000 });
  await page.locator('.uno-loader').waitFor({ state: 'detached' });
  const enable = page.locator('#uno-enable-accessibility');
  if (await enable.count()) await enable.dispatchEvent('click');
  await page.waitForTimeout(400);
  const files = await page.evaluate(() => __codespaceTestState.files);
  await page.mouse.click(620, 114); await page.keyboard.press('Control+Home');
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('ArrowRight');
  await page.waitForFunction(() => JSON.parse(localStorage.getItem('codespace.recovery.v1') ?? '{}').selections?.['src/Program.cs']?.[0]?.Active === 2);
  console.log('PASS caret-only change is persisted'); passed++;

  await page.keyboard.press('F1');
  await page.waitForFunction(() => __codespaceTestState.interaction.quickPickReady);
  const input = page.getByRole('textbox', { name: 'Command palette input', exact: true });
  await input.fill('Editor: Fold All'); await page.waitForTimeout(300); await input.press('Enter');
  await page.waitForFunction(() => JSON.parse(localStorage.getItem('codespace.recovery.v1') ?? '{}').views?.['primary\nsrc/Program.cs']?.Folds?.length > 0);
  console.log('PASS fold-only change is persisted'); passed++;

  // Fold All moves the caret to zero. Select a position outside the collapsed range.
  await page.keyboard.press('ArrowRight'); await page.keyboard.press('ArrowRight');
  await page.waitForFunction(() => __codespaceTestState.editors[0].selection.Active === 2);
  await page.mouse.move(620, 160); await page.keyboard.down('Shift');
  await page.mouse.wheel(0, 480); await page.keyboard.up('Shift');
  await page.waitForFunction(() => JSON.parse(localStorage.getItem('codespace.recovery.v1') ?? '{}').views?.['primary\nsrc/Program.cs']?.ScrollX > 0);
  const expected = await page.evaluate(() => ({
    view: JSON.parse(localStorage.getItem('codespace.recovery.v1')).views['primary\nsrc/Program.cs'],
    selection: JSON.parse(localStorage.getItem('codespace.recovery.v1')).selections['src/Program.cs'][0]
  }));
  assert.deepEqual(await page.evaluate(() => __codespaceTestState.files), files);
  console.log('PASS scroll-only change is persisted without changing files'); passed++;

  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForFunction(value => globalThis.__codespaceTestState?.editors.some(editor => editor.path === 'src/Program.cs'
    && editor.selection.Active === value.selection.Active && editor.scrollX === value.view.ScrollX
    && JSON.stringify(editor.folds) === JSON.stringify(value.view.Folds)), expected, { timeout: 90000 });
  assert.deepEqual(await page.evaluate(() => __codespaceTestState.files), files);
  assert.deepEqual(errors, []);
  await page.screenshot({ path: output + '/restored-view.png', fullPage: true });
  console.log('PASS selection, folds and scroll restore together'); passed++;
} catch (error) {
  await writeFile(output + '/failure.txt', String(error.stack));
  await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/state.json', JSON.stringify(await page.evaluate(() => ({ state: globalThis.__codespaceTestState,
    recovery: localStorage.getItem('codespace.recovery.v1') })), null, 2));
  throw error;
} finally {
  await writeFile(output + '/console.log', log.join('\n'));
  await writeFile(output + '/result.json', JSON.stringify({ passed, errors }, null, 2));
  await browser.close();
}
