import assert from 'node:assert/strict';
import { mkdir } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(resolve(here, '../../../dataverse/app-source/fmc-bms-demo/package.json'));
const { chromium } = require('playwright');
const artifacts = resolve(here, '.artifacts/browser-qa');
await mkdir(artifacts, { recursive: true });
const port = 5432;
const server = spawn(process.execPath, ['server.mjs'], {
  cwd: here, env: { ...process.env, CRUD_GUIDE_PORT: String(port) },
  stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true,
});
let output = ''; server.stdout.on('data', c => output += c); server.stderr.on('data', c => output += c);
const base = `http://127.0.0.1:${port}/`;
let browser;
try {
  for (let i = 0; i < 80; i++) {
    if (server.exitCode !== null) throw new Error('Test server exited: ' + output);
    try { if ((await fetch(base)).ok) break; } catch {}
    if (i === 79) throw new Error('Server not ready: ' + output);
    await new Promise(r => setTimeout(r, 100));
  }
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1080 }, reducedMotion: 'reduce', permissions: ['clipboard-read', 'clipboard-write'] });
  const page = await context.newPage();
  const errors = [], externalRequests = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });
  page.on('request', req => { if (!req.url().startsWith(base)) externalRequests.push(req.url()); });
  await page.goto(base, { waitUntil: 'networkidle' });
  assert.equal(await page.locator('.nav-item').count(), 10);
  await page.screenshot({ path: resolve(artifacts, 'desktop.png') });
  await page.getByRole('textbox', { name: 'Tìm bài học' }).fill('concurrency');
  assert.ok(await page.locator('.nav-item').count() < 10);
  await page.getByRole('textbox', { name: 'Tìm bài học' }).fill('zzzz-not-found');
  assert.equal(await page.locator('.nav-item').count(), 0);
  await page.getByRole('textbox', { name: 'Tìm bài học' }).fill('');
  for (let i = 0; i < 10; i++) {
    await page.locator('.nav-item').nth(i).click();
    assert.match(await page.locator('article .section-title').innerText(), new RegExp('BÀI ' + String(i + 1).padStart(2, '0')));
    assert.ok((await page.locator('article').innerText()).length > 500);
  }
  await page.getByRole('checkbox', { name: 'Tôi đã kiểm tra' }).check();
  await page.reload();
  await page.locator('#tab-check').click();
  assert.equal(await page.locator('.check-item input:checked').count(), 1);
  await page.getByRole('button', { name: 'Đặt lại tiến độ' }).click();
  assert.equal(await page.locator('.check-item input:checked').count(), 0);
  await page.locator('#tab-code').click();
  for (let i = 0; i < 7; i++) {
    await page.locator('.file-picker button').nth(i).click();
    assert.ok((await page.locator('.source-code').innerText()).length > 250);
  }
  await page.getByRole('button', { name: '04 · React API service', exact: true }).click();
  await page.getByRole('button', { name: 'Copy code', exact: true }).click();
  await page.getByRole('button', { name: '✓ Đã copy', exact: true }).waitFor();
  assert.match(await page.evaluate(() => navigator.clipboard.readText()), /fmc_GetPointIssue/);
  await page.screenshot({ path: resolve(artifacts, 'code.png') });
  await page.locator('#tab-play').click();
  await page.getByRole('button', { name: 'C · Create', exact: true }).click();
  assert.match(await page.getByTestId('demo-log').innerText(), /REJECT/);
  await page.getByLabel('Tiêu đề', { exact: false }).fill('QA issue sample');
  await page.getByRole('button', { name: 'C · Create', exact: true }).click();
  assert.equal(await page.locator('.issue-row').count(), 3);
  assert.match(await page.getByTestId('demo-log').innerText(), /ISS-003/);
  await page.getByRole('button', { name: 'U · Update', exact: true }).click();
  assert.match(await page.getByTestId('demo-log').innerText(), /RowVersion 2/);
  await page.getByRole('button', { name: 'Giả lập người khác sửa', exact: false }).click();
  await page.getByRole('button', { name: 'U · Update', exact: true }).click();
  assert.match(await page.getByTestId('demo-log').innerText(), /CONFLICT/);
  await page.locator('.issue-row').filter({ hasText: 'ISS-003' }).click();
  await page.getByRole('button', { name: 'U · Update', exact: true }).click();
  assert.match(await page.getByTestId('demo-log').innerText(), /RowVersion 4/);
  await page.getByRole('button', { name: 'D · Delete', exact: true }).click();
  assert.equal(await page.locator('.issue-row').count(), 3);
  await page.getByRole('button', { name: 'Xác nhận xóa', exact: true }).click();
  assert.equal(await page.locator('.issue-row').count(), 2);
  await page.locator('.issue-row').filter({ hasText: 'ISS-002' }).click();
  await page.getByRole('button', { name: 'D · Delete', exact: true }).click();
  assert.match(await page.getByTestId('demo-log').innerText(), /Only Draft/);
  await page.screenshot({ path: resolve(artifacts, 'playground.png'), fullPage: true });
  assert.equal((await fetch(base + 'src/snippets.ts')).status, 404);
  assert.equal((await fetch(base + 'package.json')).status, 404);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('button', { name: 'Mở mục lục' }).click();
  await page.locator('.nav-item').first().click();
  await page.screenshot({ path: resolve(artifacts, 'mobile.png'), fullPage: true, animations: 'disabled' });
  for (const tab of ['learn', 'code', 'play', 'check']) {
    await page.locator('#tab-' + tab).click();
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Mobile overflow: ' + tab);
  }
  // Endpoint protection on this Windows machine injects its own script into
  // browser traffic. Do not disable it; distinguish that traffic from the app.
  const antivirusRequests = externalRequests.filter(url => new URL(url).hostname === 'me.kes.v2.scr.kaspersky-labs.com');
  assert.deepEqual(externalRequests.filter(url => !antivirusRequests.includes(url)), []);
  assert.deepEqual(errors, []);
  console.log('PASS: 10 lessons, 7 snippets, search, copy, persistent progress, CRUD, concurrency, delete guard, mobile, no application external calls, no JS errors.');
  if (antivirusRequests.length) console.log('Host antivirus injected ' + antivirusRequests.length + ' requests; excluded from application-network assertion.');
  console.log('Screenshots: ' + artifacts);
} finally {
  await browser?.close(); server.kill();
}
