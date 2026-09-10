import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { createHash } from 'node:crypto';
import { dirname,resolve } from 'node:path';
import {fileURLToPath} from 'node:url';
import './rule-model.js';

const here=dirname(fileURLToPath(import.meta.url));
const {simulate,checkStep}=globalThis.PluginRuleModel;
const base={message:'Create',table:'fmc_bmsequipment'};
const reference={logicalName:'fmc_bmsbuilding',id:'11111111-1111-4111-8111-111111111111'};
test('handler simulation preserves Create / partial Update / explicit null semantics',()=>{
  assert.equal(simulate({...base,attributes:{}}).status,'block');
  assert.equal(simulate({...base,attributes:{fmc_buildingid:null}}).status,'block');
  assert.equal(simulate({...base,attributes:{fmc_buildingid:reference}}).status,'pass');
  assert.equal(simulate({...base,message:'Update',attributes:{fmc_name:'renamed'}}).status,'skip');
  assert.equal(simulate({...base,message:'Update',filter:false,attributes:{fmc_name:'renamed'}}).status,'skip');
  assert.equal(simulate({...base,message:'Update',attributes:{fmc_buildingid:null}}).status,'block');
  assert.equal(simulate({...base,message:'Update',attributes:{fmc_buildingid:reference}}).status,'pass');
  assert.equal(simulate({...base,table:'fmc_bmspoint',attributes:{}}).status,'skip');
});
test('misconfiguration and malformed examples never receive a misleading PASS',()=>{
  assert.equal(simulate({...base,stage:20,attributes:{}}).status,'skip');
  assert.equal(simulate({...base,enabled:false,attributes:{}}).status,'skip');
  for(const attributes of [null,[],{fmc_buildingid:'invalid'},{fmc_buildingid:{id:'bad'}}]){
    assert.equal(simulate({...base,attributes}).status,'invalid');
  }
  const valid={...base,filter:'',stage:10,mode:0,rank:10};
  assert.deepEqual(checkStep(valid),[]);
  assert.deepEqual(checkStep({...valid,message:'Update',filter:'fmc_buildingid'}),[]);
  assert.ok(checkStep({...valid,mode:1}).some(x=>x.includes('PostOperation')));
  assert.ok(checkStep({...valid,rank:0}).length>0);
});
test('all 13 chapters, annotated source and linked API script are present in generated docs',async()=>{
  const context={window:{}};
  vm.runInNewContext(await readFile(resolve(here,'content.js'),'utf8'),context);
  const data=context.window.PLUGIN_DOCS;
  assert.equal(data.chapters.length,13);
  assert.equal(data.annotations.length,17);
  for(const chapter of data.chapters){assert.ok(chapter.html.length>500);for(const key of chapter.snippets)assert.ok(data.snippets[key]);}
  const source=(await readFile(resolve(here,'../../../',data.sourcePath),'utf8')).replace(/^\uFEFF/,'').replace(/\r\n/g,'\n');
  assert.equal(data.sourceHash,createHash('sha256').update(source).digest('hex'));
  assert.equal(data.snippets.plugin.code,source.trimEnd());
  assert.ok(data.snippets['api-test'].code.includes('finally'));
  assert.ok(data.chapters[8].html.includes('AssemblyVersion'));
  assert.ok(data.chapters[9].html.includes('BMS-EQUIPMENT-001'));
  for(const path of Object.keys(data.resources)){assert.ok(!path.endsWith('.snk'));assert.ok(!path.endsWith('appsettings.json'));}
  const html=await readFile(resolve(here,'index.html'),'utf8');
  const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(m=>m[1]);
  assert.equal(new Set(ids).size,ids.length,'HTML ids must be unique');
});
