import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
import test from 'node:test';

const source = readFileSync(new URL('../../dataverse/app-source/EquipmentPluginTest.js', import.meta.url), 'utf8');
function run({ entity = 'fmc_bmsequipment', id = '{4e2cf907-5c8b-4fa5-9bfb-55faf81fd889}', lookup = true, selector = true } = {}) {
    const calls = [];
    const context = vm.createContext({ window: {} });
    vm.runInContext(source, context);
    context.FMC.EquipmentPluginTest.onLoad({ getFormContext: () => ({
        data: { entity: { getEntityName: () => entity } },
        getAttribute: name => {
            assert.equal(name, 'fmc_buildingid');
            return lookup ? { setRequiredLevel: value => calls.push(['required', value]) } : null;
        },
        ui: {
            formSelector: { getCurrentItem: () => selector ? { getId: () => id } : null },
            setFormNotification: (...args) => calls.push(['notification', ...args]),
            tabs: { get: name => { assert.equal(name, 'fmc_bmsrelations'); return { setFocus: () => calls.push(['focus']) }; } }
        }
    }) });
    return calls;
}
test('test form relaxes only Building and shows real-data warning', () => {
    const calls = run();
    assert.deepEqual(calls[0], ['required', 'none']);
    assert.match(calls[1][1], /BMS-EQUIPMENT-001/);
    assert.match(calls[1][1], /du lieu that/);
    assert.equal(calls[1][2], 'WARNING');
    assert.deepEqual(calls[2], ['focus']);
});
test('normal form and other tables are unchanged', () => {
    assert.deepEqual(run({ id: '16116cbe-739c-40c0-820f-985ae43ca22d' }), []);
    assert.deepEqual(run({ entity: 'fmc_bmsbuilding' }), []);
});
test('missing lookup is safe; one-form UI without selector is supported', () => {
    assert.deepEqual(run({ lookup: false }), []);
    assert.equal(run({ selector: false })[0][1], 'none');
});
