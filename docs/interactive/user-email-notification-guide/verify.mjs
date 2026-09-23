import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {dirname,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import vm from 'node:vm';

const here=dirname(fileURLToPath(import.meta.url));
const context={window:{}};
vm.runInNewContext(await readFile(resolve(here,'content.js'),'utf8'),context);
const data=context.window.EMAIL_NOTIFICATION_GUIDE;

test('guide covers the implemented notification contract',()=>{
  assert.equal(data.chapters.length,8);
  assert.equal(data.schema.length,15);
  assert.equal(data.checklist.length,8);
  assert.deepEqual(Array.from(data.flowNodes,node=>node.id),['request','plugin','outbox','attempt','send','receipt']);
  const content=data.chapters.map(chapter=>chapter.html).join('\n');
  for(const value of ['789120000','789121000','789121001','789121002','789121003'])assert.match(content,new RegExp(value));
  assert.match(content,/fmc_notification/);
  assert.match(content,/fmc_sharedoffice365outlook/);
  assert.match(content,/Send an email \(V2\)/);
  assert.match(content,/retry policy = None/i);
});

test('HTML ids are unique and local assets exist',async()=>{
  const html=await readFile(resolve(here,'index.html'),'utf8');
  const ids=[...html.matchAll(/\bid="([^"]+)"/g)].map(match=>match[1]);
  assert.equal(new Set(ids).size,ids.length);
  for(const file of ['styles.css','content.js','app.js','server.mjs'])assert.ok((await readFile(resolve(here,file))).length>100);
});
