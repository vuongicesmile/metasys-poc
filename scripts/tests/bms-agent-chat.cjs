const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const app = path.resolve(__dirname, '../../dataverse/app-source/fmc-bms-demo');
const ts = require(path.join(app, 'node_modules/typescript'));
const source = fs.readFileSync(path.join(app, 'src/services/agentChatService.ts'), 'utf8');
const compiled = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const loaded = { exports: {} };
new Function('module', 'exports', compiled)(loaded, loaded.exports);
const { openAgentChat } = loaded.exports;

test('no model-driven Copilot host does not open a fake chat', async () => {
    global.window = {};
    assert.equal(await openAgentChat(), 'unavailable');
});

test('disabled M365 Copilot does not open its pane', async () => {
    let opened = false;
    global.window = { Xrm: { Copilot: {
        isM365CopilotEnabled: async () => false,
        openM365CopilotPanel: async () => { opened = true; },
    } } };
    assert.equal(await openAgentChat(), 'unavailable');
    assert.equal(opened, false);
});

test('enabled M365 Copilot opens the native pane once', async () => {
    let calls = 0;
    global.window = { Xrm: { Copilot: {
        isM365CopilotEnabled: async () => true,
        openM365CopilotPanel: async () => { calls += 1; },
    } } };
    assert.equal(await openAgentChat(), 'opened');
    assert.equal(calls, 1);
});

test('host failure propagates so the UI can show an error', async () => {
    global.window = { Xrm: { Copilot: {
        isM365CopilotEnabled: async () => true,
        openM365CopilotPanel: async () => { throw new Error('host failed'); },
    } } };
    await assert.rejects(openAgentChat(), /host failed/);
});
