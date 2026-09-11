import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {mkdir,readFile} from 'node:fs/promises';
import {dirname,resolve} from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const artifacts=resolve(here,'../../../.artifacts/plugin-docs-qa');
await mkdir(artifacts,{recursive:true});
const browser=await chromium.launch({channel:'msedge',headless:true});
try{
  const context=await browser.newContext({viewport:{width:1440,height:1080},permissions:['clipboard-read','clipboard-write']});
  const page=await context.newPage();
  const errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  const base=process.env.PLUGIN_DOCS_TEST_URL||'http://127.0.0.1:5400/';
  await page.goto(base,{waitUntil:'networkidle'});
  await page.locator('#article').waitFor();
  assert.equal(await page.locator('.nav-link').count(),13);
  await page.screenshot({path:resolve(artifacts,'desktop.png'),fullPage:false});
  await page.getByRole('tab',{name:'Thử payload'}).click();
  const cases=[
    ['create-valid','pass'],['create-missing','block'],['create-null','block'],
    ['update-name','skip'],['update-clear','block'],['update-valid','pass'],['other-table','skip']
  ];
  for(const [scenario,status] of cases){
    await page.locator('#scenario').selectOption(scenario);
    assert.match(await page.locator('#lab-result').getAttribute('class'),new RegExp(status));
  }
  await page.locator('#payload').fill('{broken');
  await page.getByRole('button',{name:'Chạy mô phỏng'}).click();
  assert.match(await page.locator('#lab-result').innerText(),/JSON chưa hợp lệ/);
  await page.locator('#scenario').selectOption('update-clear');
  await page.screenshot({path:resolve(artifacts,'payload-rejected.png'),fullPage:false});
  await page.getByRole('tab',{name:'Tạo step'}).click();
  await page.locator('#step-message').selectOption('Update');
  assert.equal(await page.locator('#step-filter').inputValue(),'fmc_buildingid');
  await page.locator('#step-stage').selectOption('20');
  assert.match(await page.locator('#step-result').innerText(),/Stage cần là PreValidation/);
  await page.locator('#step-stage').selectOption('10');
  await page.locator('#step-mode').selectOption('1');
  assert.match(await page.locator('#step-result').innerText(),/PostOperation/);
  await page.locator('#step-mode').selectOption('0');
  await page.locator('.nav-link[href="#bai-4"]').click();
  await page.locator('.code-line[data-line="40"]').click();
  assert.match(await page.locator('#line-explanation').innerText(),/Update.*fmc_buildingid/);
  await page.getByRole('button',{name:'Copy code',exact:true}).click();
  await page.locator('#toast').waitFor({state:'visible'});
  assert.match(await page.locator('#toast').innerText(),/Đã copy/);
  await page.screenshot({path:resolve(artifacts,'code-annotation.png'),fullPage:false});
  for(let i=1;i<=13;i++){
    await page.locator('.nav-link[href="#bai-'+i+'"]').click();
    await page.locator('#chapter-count').filter({hasText:'BÀI '+String(i).padStart(2,'0')+' / 13'}).waitFor();
    assert.ok((await page.locator('#article').innerText()).length>500,'chapter '+i+' must contain full documentation');
  }
  await page.locator('#search').fill('zzzz-no-results');
  assert.equal(await page.locator('.nav-link').count(),0);
  assert.equal(await page.locator('#no-results').isVisible(),true);
  await page.locator('#search').fill('');
  await page.locator('#view-receipt').click();
  assert.equal(await page.locator('#source-dialog').isVisible(),true);
  assert.match(await page.locator('#dialog-content').innerText(),/Deployment receipt/);
  await page.getByRole('button',{name:'Đóng tài liệu',exact:true}).click();
  const downloadEvent=page.waitForEvent('download');
  await page.locator('#download-guide').click();
  const download=await downloadEvent;
  await download.saveAs(resolve(artifacts,'downloaded-guide.md'));
  assert.equal((await readFile(resolve(artifacts,'downloaded-guide.md'),'utf8')).replace(/\r\n/g,'\n'),
    (await readFile(resolve(here,'../../runbooks/dataverse-plugin-step-by-step.vi.md'),'utf8')).replace(/\r\n/g,'\n'));
  await page.locator('.nav-link[href="#bai-1"]').click();
  await page.locator('#theme-toggle').click();
  await page.screenshot({path:resolve(artifacts,'dark.png'),fullPage:false});
  await page.locator('#theme-toggle').click();
  await page.setViewportSize({width:390,height:844});
  assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'No page overflow on mobile');
  await page.locator('#menu-toggle').click();
  assert.equal(await page.locator('#sidebar').isVisible(),true);
  await page.locator('.nav-link[href="#bai-7"]').click();
  await page.locator('#sidebar').waitFor({state:'hidden'});
  assert.equal(await page.locator('#sidebar').isVisible(),false);
  await page.goto(base,{waitUntil:'networkidle'});
  await page.screenshot({path:resolve(artifacts,'mobile.png'),fullPage:false});
  await page.setViewportSize({width:1440,height:1080});
  await page.goto(pathToFileURL(resolve(here,'index.html')).href,{waitUntil:'load'});
  assert.equal(await page.locator('.nav-link').count(),13,'Works as a local file without a server');
  await page.getByRole('tab',{name:'Thử payload'}).click();
  await page.locator('#scenario').selectOption('create-missing');
  assert.match(await page.locator('#lab-result').innerText(),/BMS-EQUIPMENT-001/);
  assert.deepEqual(errors,[]);
  console.log('PASS: 13 chapters, 7 payload scenarios, invalid JSON, step configuration, code annotations, copy, download, resource modal, search, theme, mobile, file://. No page errors.');
  console.log('Screenshots: '+artifacts);
}finally{await browser.close();}
