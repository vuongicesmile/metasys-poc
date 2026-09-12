import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import test from 'node:test';

const html = readFileSync(fileURLToPath(new URL('../../dataverse/app-source/BmsEventDemo.html', import.meta.url)), 'utf8');
const script = html.match(/<script>\s*([\s\S]*?)<\/script>/)[1];

function mount({ stored = null, blocked = false, context = true, fetchImpl } = {}) {
  const nodes = new Map();
  function element(id, key) {
    const listeners = new Map();
    const node = { id, dataset: { ...(key ? { i18n: key } : {}) }, value: '', textContent: '', disabled: false,
      addEventListener: (name, handler) => listeners.set(name, handler),
      setAttribute(name, value) { this[name] = value; },
      dispatch: name => listeners.get(name)?.()
    };
    if (id) nodes.set(id, node);
    return node;
  }
  for (const id of ['send', 'status', 'language', 'event-card']) element(id);
  const translated = [...html.matchAll(/<[^>]*data-i18n="([^"]+)"[^>]*>/g)].map(match => {
    const id = match[0].match(/\bid="([^"]+)"/)?.[1];
    const node = id && nodes.get(id) || element(id, match[1]);
    node.dataset.i18n = match[1];
    return node;
  });
  const windowListeners = new Map();
  const calls = [];
  const storage = {
    getItem: () => { if (blocked) throw Error('storage denied'); return stored; },
    setItem: (key, value) => { if (blocked) throw Error('storage denied'); assert.equal(key, 'fmc.bms.language'); stored = value; }
  };
  const document = { documentElement: { lang: '' }, getElementById: id => nodes.get(id), querySelectorAll: () => translated };
  vm.runInNewContext(script, {
    document, window: { addEventListener: (name, handler) => windowListeners.set(name, handler) }, localStorage: storage,
    crypto: { randomUUID: () => 'click-123' }, AbortController, setTimeout: () => 1, clearTimeout: () => {},
    ...(context ? { GetGlobalContext: () => ({ getClientUrl: () => 'https://example.crm.dynamics.com' }) } : {}),
    fetch: async (...args) => { calls.push(args); return fetchImpl ? fetchImpl(...args) : { ok: true, json: async () => ({ RequestId: 'request-456', Created: true, Status: 'Queued' }) }; }
  });
  return {
    document, calls, nodes, storage,
    saved: () => stored,
    choose(value) { nodes.get('language').value = value; nodes.get('language').dispatch('change'); },
    crossTab(value, key = 'fmc.bms.language') { windowListeners.get('storage')({ key, newValue: value, storageArea: storage }); },
    send: () => nodes.get('send').dispatch('click'),
    status: () => nodes.get('status').textContent
  };
}

test('defaults to English, restores valid choices, ignores invalid values and tolerates blocked storage', () => {
  assert.equal(mount().document.documentElement.lang, 'en');
  assert.equal(mount({ stored: 'fr' }).document.documentElement.lang, 'en');
  assert.equal(mount({ stored: 'vi' }).document.documentElement.lang, 'vi');
  const app = mount({ stored: 'vi', blocked: true });
  assert.equal(app.document.documentElement.lang, 'en');
  app.choose('vi');
  assert.equal(app.document.documentElement.lang, 'vi');
  assert.match(app.status(), /Sẵn sàng/);
  assert.equal(app.calls.length, 0);
});

test('persists selection and synchronizes storage events without requests', () => {
  const app = mount();
  app.choose('vi');
  assert.equal(app.saved(), 'vi');
  assert.equal(mount({ stored: app.saved() }).document.documentElement.lang, 'vi');
  app.crossTab('en', 'unrelated');
  assert.equal(app.document.documentElement.lang, 'vi');
  app.crossTab('en');
  assert.match(app.status(), /^Ready/);
  app.crossTab('vi');
  app.crossTab(null, null);
  assert.equal(app.document.documentElement.lang, 'en');
  assert.equal(app.calls.length, 0);
});

test('switches pending and accepted messages without repeating a request or changing its payload', async () => {
  let finish;
  const app = mount({ fetchImpl: () => new Promise(resolve => { finish = resolve; }) });
  const pending = app.send();
  assert.match(app.status(), /Sending event/);
  app.choose('vi');
  assert.match(app.status(), /Đang gửi event/);
  await app.send();
  assert.equal(app.calls.length, 1);
  const [url, options] = app.calls[0];
  assert.equal(url, 'https://example.crm.dynamics.com/api/data/v9.2/fmc_RequestBmsSync');
  assert.equal(options.method, 'POST');
  assert.equal(options.credentials, 'same-origin');
  assert.deepEqual(JSON.parse(options.body), { ClientRequestId: 'click-123' });
  finish({ ok: true, json: async () => ({ RequestId: 'request-456', Created: true, Status: 'Queued' }) });
  await pending;
  assert.match(app.status(), /Dataverse đã nhận/);
  assert.match(app.status(), /Trạng thái: Đang chờ/);
  app.choose('en');
  assert.match(app.status(), /Dataverse accepted/);
  assert.match(app.status(), /Request ID: request-456/);
  assert.match(app.status(), /Created new: Yes/);
  assert.equal(app.calls.length, 1);
  assert.equal(app.nodes.get('send').disabled, false);
});

test('existing error state translates while retaining backend details and retry guidance', async () => {
  const app = mount({ fetchImpl: async () => ({ ok: false, status: 403, json: async () => ({ error: { message: 'Missing prvExecute privilege' } }) }) });
  await app.send();
  assert.match(app.status(), /The event could not be sent/);
  app.choose('vi');
  assert.match(app.status(), /Gửi event chưa thành công/);
  assert.match(app.status(), /Missing prvExecute privilege/);
  assert.match(app.status(), /Mã lần bấm: click-123/);
  assert.equal(app.calls.length, 1);
});

test('missing host and timeout errors remain translatable after failure', async () => {
  const noHost = mount({ context: false });
  await noHost.send();
  assert.match(noHost.status(), /Open this page from/);
  noHost.choose('vi');
  assert.match(noHost.status(), /Hãy mở trang này/);
  assert.equal(noHost.calls.length, 0);
  const timeout = mount({ fetchImpl: async () => { const error = new Error('aborted'); error.name = 'AbortError'; throw error; } });
  await timeout.send();
  assert.match(timeout.status(), /unknown after 30 seconds/);
  timeout.choose('vi');
  assert.match(timeout.status(), /sau 30 giây/);
  assert.equal(timeout.nodes.get('send').disabled, false);
});

test('deployment placeholders and link isolation remain intact', () => {
  assert.match(html, /environments\/__ENVIRONMENT_ID__\/flows\/__FLOW_ID__\/details/);
  assert.match(html, /target="_blank" rel="noopener noreferrer"/);
});
