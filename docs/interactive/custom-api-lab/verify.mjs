import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {dirname,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import vm from 'node:vm';
import './api-model.js';

const here=dirname(fileURLToPath(import.meta.url));
const root=resolve(here,'../../..');
const {simulate,checkContract}=globalThis.CustomApiModel;

test('simulator models create, retry, active pipeline, race and invalid input',()=>{
  const valid='11111111-1111-4111-8111-111111111111';
  assert.equal(simulate({clientRequestId:valid}).status,'created');
  assert.equal(simulate({clientRequestId:null}).status,'created');
  assert.equal(simulate({clientRequestId:valid,sameCorrelation:true}).created,false);
  assert.deepEqual(simulate({clientRequestId:valid,sameCorrelation:true}).trace,['START','REUSE_BY_ID']);
  assert.equal(simulate({clientRequestId:valid,activeRequest:true}).title,'Pipeline đang có request active');
  assert.ok(simulate({clientRequestId:valid,race:true}).trace.includes('RACE_REUSE'));
  assert.equal(simulate({clientRequestId:'bad'}).code,400);
});

test('contract designer detects incompatible Custom API choices',()=>{
  const valid={name:'fmc_RequestBmsSync',kind:'action',binding:'0',processing:'1',privilege:'prvCreatefmc_syncrequest',input:'ClientRequestId:String?',output:'RequestId:Guid\nCreated:Boolean\nStatus:Integer\nMessage:String'};
  assert.deepEqual(checkContract(valid),[]);
  assert.ok(checkContract({...valid,kind:'function'}).some(x=>x.includes('Action')));
  assert.ok(checkContract({...valid,binding:'1'}).some(x=>x.includes('Global')));
  assert.ok(checkContract({...valid,output:'Message:String'}).length>=3);
});

test('generated handbook tracks reviewed source and annotates every source line',async()=>{
  const context={window:{}};
  vm.runInNewContext(await readFile(resolve(here,'content.js'),'utf8'),context);
  const data=context.window.CUSTOM_API_DOCS;
  assert.equal(data.chapters.length,13);
  const source=(await readFile(resolve(root,data.sourcePath),'utf8')).replace(/^\uFEFF/,'').replace(/\r\n/g,'\n');
  assert.equal(data.sourceHash,createHash('sha256').update(source).digest('hex'));
  assert.equal(data.snippets.plugin.code,source.trimEnd());
  assert.equal(data.annotations.length,source.trimEnd().split('\n').length);
  data.annotations.forEach((note,index)=>{assert.equal(note.from,index+1);assert.equal(note.to,index+1);assert.ok(note.html.length>25);});
  assert.match(data.annotations[79].html,/helper đọc optional input/);
  assert.match(data.annotations[93].html,/query tối đa một row/);
  assert.match(data.annotations[122].html,/gán output contract/);
  for(const chapter of data.chapters){assert.ok(chapter.html.length>350,'chapter '+chapter.number+' is too short');assert.ok(chapter.snippets.includes('plugin'));}
  assert.ok(data.snippets.ui.code.includes("fetch(base + '/api/data/v9.2/fmc_RequestBmsSync'"));
  assert.ok(data.snippets.provisioner.code.includes('EnsureRequestCustomApi'));
  assert.ok(data.snippets.flow.code.includes('BusinessEventsTrigger'));
  assert.ok(data.snippets['custom-api-xml'].code.includes('<workflowsdkstepenabled>1</workflowsdkstepenabled>'));
  for(const path of Object.keys(data.resources)){assert.ok(!path.endsWith('.snk'));assert.ok(!path.endsWith('appsettings.json'));assert.ok(!path.toLowerCase().includes('token'));}
  const html=await readFile(resolve(here,'index.html'),'utf8');
  const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(match=>match[1]);
  assert.equal(new Set(ids).size,ids.length,'HTML ids must be unique');
});
