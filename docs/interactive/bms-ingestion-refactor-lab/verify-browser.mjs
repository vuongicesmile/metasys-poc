import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {spawn} from 'node:child_process';
import {dirname, resolve} from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(resolve(here, '../plugin-lab/package.json'));
const {chromium} = require('playwright');
const artifacts = resolve(here, '.artifacts/browser-qa');
await mkdir(artifacts, {recursive:true});
const port = 5412;
const server = spawn(process.execPath, ['server.mjs'], {cwd:here, env:{...process.env,BMS_REFACTOR_LAB_PORT:String(port)}, stdio:['ignore','pipe','pipe']});
let output = '';
server.stdout.on('data', chunk => output += chunk);
server.stderr.on('data', chunk => output += chunk);
const base = `http://127.0.0.1:${port}/`;
for (let attempt = 0; attempt < 80; attempt++) {
  try { if ((await fetch(base)).ok) break; } catch {}
  await new Promise(resolveWait => setTimeout(resolveWait, 50));
  if (attempt === 79) throw new Error('Local lab server did not start. ' + output);
}

const browser = await chromium.launch({channel:'msedge', headless:true});
try {
  const page = await browser.newPage({viewport:{width:1440,height:1000}});
  const errors = [], responses = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('response', response => responses.push(`${response.status()} ${response.url()}`));
  await page.goto(pathToFileURL(resolve(here, 'index.html')).href, {waitUntil:'load'});
  await page.waitForTimeout(100);
  const scriptState = await page.evaluate(() => [...document.scripts].map(script => ({src:script.src,loaded:script.readyState})));
  assert.equal(await page.locator('.step').count(), 12, `page errors: ${errors.join(' | ')}; scripts: ${JSON.stringify(scriptState)}; responses: ${responses.join(' | ')}`);
  assert.equal(await page.locator('.nav-link').count(), 12);
  assert.equal(await page.locator('.raw-card').count(), 3);
  assert.match(await page.locator('.raw-map').innerText(), /raw\.bms_reading/);
  assert.match(await page.locator('h1').innerText(), /Tách BMS ingestion/);
  await page.locator('[data-step="0"]').check();
  assert.match(await page.locator('#done-count').innerText(), /1\/12/);
  await page.locator('#search').fill('SQL adapter');
  assert.ok(await page.locator('.nav-link').count() >= 1);
  await page.locator('#search').fill('');
  await page.screenshot({path:resolve(artifacts,'desktop.png'),fullPage:false});
  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No horizontal overflow on mobile');
  await page.screenshot({path:resolve(artifacts,'mobile.png'),fullPage:false});
  assert.deepEqual(errors, []);
  console.log(`PASS: 12 checkpoints, progress, search, desktop and mobile render. Screenshots: ${artifacts}`);
} finally {
  await browser.close();
  server.kill();
}
