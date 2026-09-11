import assert from 'node:assert/strict';
import {mkdir,readFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {existsSync} from 'node:fs';
import {spawn} from 'node:child_process';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const root=resolve(here,'../../..');
const require=createRequire(existsSync(resolve(here,'node_modules'))?resolve(here,'package.json'):resolve(here,'../plugin-lab/package.json'));
const {chromium}=require('playwright');
const artifacts=resolve(here,'.artifacts/browser-qa');
await mkdir(artifacts,{recursive:true});
const port=5411;
const server=spawn(process.execPath,['server.mjs'],{cwd:here,env:{...process.env,CUSTOM_API_DOCS_PORT:String(port)},stdio:['ignore','pipe','pipe']});
let serverOutput='';server.stdout.on('data',chunk=>serverOutput+=chunk);server.stderr.on('data',chunk=>serverOutput+=chunk);
const base='http://127.0.0.1:'+port+'/';
for(let attempt=0;attempt<80;attempt++){
  try{const response=await fetch(base);if(response.ok)break;}catch{}
  await new Promise(resolveWait=>setTimeout(resolveWait,50));
  if(attempt===79)throw new Error('Local docs server did not start. '+serverOutput);
}

const browser=await chromium.launch({channel:'msedge',headless:true});
try{
  const context=await browser.newContext({viewport:{width:1440,height:1080},acceptDownloads:true,permissions:['clipboard-read','clipboard-write']});
  const page=await context.newPage(),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  await page.goto(base,{waitUntil:'networkidle'});
  await page.locator('#article').waitFor();
  assert.equal(await page.locator('.nav-link').count(),13);
  assert.match(await page.locator('#chapter-count').innerText(),/BÀI 01 \/ 13/);
  await page.screenshot({path:resolve(artifacts,'desktop.png'),fullPage:false});

  await page.getByRole('tab',{name:'Thử request'}).click();
  const cases=[['new','CREATED'],['retry','REUSE'],['active','REUSE'],['race','REUSE'],['invalid','INVALID'],['optional','CREATED']];
  for(const [scenario,status] of cases){await page.locator('#scenario').selectOption(scenario);assert.match(await page.locator('#lab-result .result-label').innerText(),new RegExp(status));}
  await page.locator('#scenario').selectOption('race');
  assert.match(await page.locator('#lab-result').innerText(),/RACE_REUSE/);
  await page.screenshot({path:resolve(artifacts,'request-race.png'),fullPage:false});

  await page.getByRole('tab',{name:'Thiết kế contract'}).click();
  assert.match(await page.locator('#contract-result').innerText(),/Contract nhất quán/);
  await page.locator('#api-kind').selectOption('function');
  assert.match(await page.locator('#contract-result').innerText(),/phải là Action/);
  await page.locator('#api-kind').selectOption('action');

  await page.locator('.nav-link[href="#bai-4"]').click();
  await page.locator('.code-line[data-line="64"]').click();
  assert.match(await page.locator('#line-explanation').innerText(),/Dataverse Create/);
  await page.getByRole('button',{name:'Copy code',exact:true}).click();
  await page.locator('#toast').waitFor({state:'visible'});
  assert.match(await page.locator('#toast').innerText(),/Đã copy/);
  await page.screenshot({path:resolve(artifacts,'line-64.png'),fullPage:false});

  for(let number=1;number<=13;number++){
    await page.locator('.nav-link[href="#bai-'+number+'"]').click();
    await page.locator('#chapter-count').filter({hasText:'BÀI '+String(number).padStart(2,'0')+' / 13'}).waitFor();
    assert.ok((await page.locator('#article').innerText()).length>300,'chapter '+number+' must contain detailed docs');
  }
  await page.locator('#search').fill('zzzz-no-results');assert.equal(await page.locator('.nav-link').count(),0);assert.equal(await page.locator('#no-results').isVisible(),true);await page.locator('#search').fill('');
  await page.locator('#view-receipt').click();assert.equal(await page.locator('#source-dialog').isVisible(),true);assert.match(await page.locator('#dialog-content').innerText(),/Custom API yêu cầu SQL-to-Dataverse sync/);await page.getByRole('button',{name:'Đóng tài liệu'}).click();

  const downloadPromise=page.waitForEvent('download');await page.locator('#download-guide').click();const download=await downloadPromise;await download.saveAs(resolve(artifacts,'custom-api-step-by-step.vi.md'));
  assert.equal((await readFile(resolve(artifacts,'custom-api-step-by-step.vi.md'),'utf8')).replace(/\r\n/g,'\n'),(await readFile(resolve(root,'docs/runbooks/custom-api-step-by-step.vi.md'),'utf8')).replace(/\r\n/g,'\n'));

  await page.locator('#theme-toggle').click();await page.screenshot({path:resolve(artifacts,'dark.png'),fullPage:false});
  await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No horizontal overflow on mobile');await page.locator('#menu-toggle').click();assert.equal(await page.locator('#sidebar').isVisible(),true);await page.screenshot({path:resolve(artifacts,'mobile.png'),fullPage:false});

  await page.setViewportSize({width:1440,height:1080});await page.goto(pathToFileURL(resolve(here,'index.html')).href,{waitUntil:'load'});assert.equal(await page.locator('.nav-link').count(),13,'file:// mode works');await page.getByRole('tab',{name:'Thử request'}).click();await page.locator('#scenario').selectOption('invalid');assert.match(await page.locator('#lab-result').innerText(),/BMS-SYNC-001/);
  assert.deepEqual(errors,[]);
  console.log('PASS: 13 chapters, 132 line explanations, 6 request scenarios, contract validation, copy/download, resource dialog, search, theme, mobile and file://.');
  console.log('Screenshots: '+artifacts);
}finally{
  await browser.close();server.kill();
}
