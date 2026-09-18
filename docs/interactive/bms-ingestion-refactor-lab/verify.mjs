import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {dirname, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../../..');

test('interactive page has unique ids and local assets', async () => {
  const html = await readFile(resolve(here, 'index.html'), 'utf8');
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length);
  assert.match(html, /styles\.css/); assert.match(html, /raw-map\.css/); assert.match(html, /app\.js/); assert.match(html, /BMS Ingestion Refactor Lab/);
});

test('lab describes all checkpoints and preserves ingestion invariants', async () => {
  const app = await readFile(resolve(here, 'app.js'), 'utf8');
  assert.equal([...app.matchAll(/check:\s*'/g)].length, 12);
  for (const expected of ['--no-sql','localhost:5200','DECIMAL(18,4)','ct.ThrowIfCancellationRequested','BMS.Ingestion.Business','BMS.Ingestion.DataAccess']) assert.ok(app.includes(expected), `missing ${expected}`);
  assert.doesNotMatch(app, /Fmc\.Bms\.Ingestion|DataAccess\.Sql|DataAccess\.Metasys/);
});

test('markdown plan is scoped and references files that exist', async () => {
  const plan = await readFile(resolve(root, 'docs/plans/bms-ingestion-layered-refactor.vi.md'), 'utf8');
  assert.match(plan, /đã triển khai trong source local/); assert.match(plan, /Ngoài phạm vi/); assert.match(plan, /Definition of Done/);
  for (const path of [
    'BMS.Ingestion/BMS.Ingestion.Business/Services/CovIngestionWorker.cs',
    'BMS.Ingestion/BMS.Ingestion.DataAccess/Services/MetasysClient.cs',
    'BMS.Ingestion/BMS.Ingestion.DataAccess/Services/BmsReadingRepository.cs',
    'BMS.Ingestion/BMS.Ingestion.Domain/Models/CovEvent.cs',
    'tests/MetasysPoc.Tests/CovIngestionWorkerTests.cs'
  ]) await readFile(resolve(root,path),'utf8');
});
