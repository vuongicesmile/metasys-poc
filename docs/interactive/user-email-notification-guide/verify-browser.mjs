import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {spawn} from 'node:child_process';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const require=createRequire(resolve(here,'../../../dataverse/app-source/fmc-bms-demo/package.json'));
const {chromium}=require('playwright');
const artifacts=resolve(here,'.artifacts/browser-qa');
await mkdir(artifacts,{recursive:true});
const port=5422;
const server=spawn(process.execPath,['server.mjs'],{cwd:here,env:{...process.env,EMAIL_GUIDE_PORT:String(port)},stdio:['ignore','pipe','pipe']});
let serverOutput='';server.stdout.on('data',chunk=>serverOutput+=chunk);server.stderr.on('data',chunk=>serverOutput+=chunk);
const base='http://127.0.0.1:'+port+'/';
for(let attempt=0;attempt<80;attempt++){
  try{const response=await fetch(base);if(response.ok)break;}catch{}
  await new Promise(resolveWait=>setTimeout(resolveWait,50));
  if(attempt===79)throw new Error('Local guide server did not start. '+serverOutput);
}

const browser=await chromium.launch({channel:'msedge',headless:true});
try{
  const context=await browser.newContext({viewport:{width:1440,height:1050},permissions:['clipboard-read','clipboard-write']});
  const page=await context.newPage(),errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto(base,{waitUntil:'networkidle'});
  assert.equal(await page.locator('.nav-link').count(),8);
  assert.match(await page.locator('#chapter-count').innerText(),/BƯỚC 01 \/ 08/);
  assert.equal(await page.locator('.flow-node').count(),6);
  await page.screenshot({path:resolve(artifacts,'desktop.png'),fullPage:false});

  await page.locator('.nav-link[href="#buoc-6"]').click();
  await page.locator('#chapter-count').filter({hasText:'BƯỚC 06 / 08'}).waitFor();
  assert.match(await page.locator('#article').innerText(),/Filter rows/);
  await page.locator('#article [data-copy]').last().click();
  await page.locator('#toast').waitFor({state:'visible'});
  assert.match(await page.locator('#toast').innerText(),/Đã copy/);

  await page.getByRole('tab',{name:'Schema'}).click();
  assert.equal(await page.locator('.schema-row').count(),15);
  await page.locator('#schema-search').fill('correlation');
  assert.equal(await page.locator('.schema-row').count(),1);
  assert.match(await page.locator('.schema-row').innerText(),/fmc_correlationkey/);

  await page.getByRole('tab',{name:'Checklist'}).click();
  await page.locator('.check-row input').first().check();
  assert.match(await page.locator('#check-count').innerText(),/1 \/ 8/);
  await page.locator('#reset-checklist').click();
  assert.match(await page.locator('#check-count').innerText(),/0 \/ 8/);

  await page.getByRole('tab',{name:'Flow map'}).click();
  await page.locator('[data-flow="send"]').click();
  assert.match(await page.locator('#flow-detail').innerText(),/Retry policy None/);
  await page.locator('#theme-toggle').click();
  await page.screenshot({path:resolve(artifacts,'dark.png'),fullPage:false});

  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal overflow on mobile');
  await page.locator('#menu-toggle').click();
  assert.equal(await page.locator('#sidebar').isVisible(),true);
  await page.screenshot({path:resolve(artifacts,'mobile.png'),fullPage:false});

  await page.setViewportSize({width:1280,height:900});
  await page.goto(pathToFileURL(resolve(here,'index.html')).href,{waitUntil:'load'});
  assert.equal(await page.locator('.nav-link').count(),8,'file:// mode works');
  assert.deepEqual(errors,[]);
  console.log('PASS: 8 steps, flow map, schema search, copy, checklist, theme, mobile and file:// mode.');
  console.log('Screenshots: '+artifacts);
}finally{await browser.close();server.kill();}
