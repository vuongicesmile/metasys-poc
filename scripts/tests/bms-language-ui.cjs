// Run after installing the UI test dependencies described in the demo runbook.
// This test uses synthetic data and never calls Dataverse or starts the sync worker.
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { createRequire } = require('node:module');
const repo = path.resolve(__dirname, '../..');
const testDir = path.join(repo, '.artifacts/language-tests');
const dependency = createRequire(path.join(testDir, 'package.json'));
const esbuild = dependency('esbuild');
const { chromium } = dependency('@playwright/test');

async function main() {
    const bundle = await esbuild.build({
        stdin: { contents: `
import React from 'react';
import ReactDOM from 'react-dom';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import Dashboard from '../../dataverse/app-source/fmc-bms-demo/trung-tam-van-hanh';
window.queries = 0;
const dataApi = { queryTable: async (table) => {
    window.queries++;
    if (location.search.includes('fail')) throw Error('synthetic failure');
    const rows = {
        fmc_bmspoint: [{fmc_bmspointid:'point-1',fmc_name:'Temperature demo',fmc_building:'Building A',fmc_currentvalue:1234.56,fmc_lastreadingtime:new Date('2026-09-12T08:00:00Z')}],
        fmc_syncrequest: [{fmc_syncrequestid:'sync-1',fmc_name:'Request demo',fmc_status:789100000,fmc_deliveredrows:1234,fmc_startedat:new Date('2026-09-12T08:00:00Z')}],
        fmc_spofile: [{fmc_spofileid:'file-1',fmc_filename:'demo.csv',fmc_status:789110002,fmc_importstatus:789111000}]
    };
    return { rows: rows[table] || [{id:'fixture'}], hasMoreRows: table === 'fmc_bmsreading' };
}};
ReactDOM.render(<FluentProvider theme={webLightTheme}><Dashboard dataApi={dataApi} pageInput={{}} /></FluentProvider>,document.getElementById('root'));
`, resolveDir: testDir, loader: 'tsx' },
        bundle: true, write: false, format: 'iife', jsx: 'automatic',
        nodePaths: [path.join(testDir, 'node_modules')], logLevel: 'silent'
    });
    const server = http.createServer((req, res) => {
        if (req.url === '/app.js') {
            res.setHeader('Content-Type', 'text/javascript'); res.end(bundle.outputFiles[0].contents);
        } else {
            res.setHeader('Content-Type', 'text/html; charset=utf-8');
            res.end('<!doctype html><html><head><meta charset="utf-8"></head><body><div id="root"></div><script src="/app.js"></script></body></html>');
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    let browser;
    try {
        browser = await chromium.launch({ channel: 'msedge', headless: true });
        const context = await browser.newContext({ locale: 'vi-VN' });
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', error => errors.push(error.message));
        const url = `http://127.0.0.1:${server.address().port}`;
        await page.goto(url);
        await page.getByRole('heading', { name: 'BMS Operations Center' }).waitFor();
        await page.getByText('Queued', { exact: true }).waitFor();
        assert.equal(await page.getByRole('combobox', { name: 'Language' }).inputValue(), 'en');
        assert.equal(await page.locator('main').getAttribute('lang'), 'en');
        assert.ok((await page.locator('main').innerText()).includes('1,234.56'));
        assert.ok((await page.locator('main').innerText()).includes('9/12/26'));
        const count = await page.evaluate(() => window.queries);
        assert.equal(count, 8);
        await page.getByRole('combobox', { name: 'Language' }).selectOption('vi');
        await page.getByRole('heading', { name: 'Trung tâm vận hành BMS' }).waitFor();
        await page.getByText('Đang chờ', { exact: true }).waitFor();
        await page.getByText('Đã lưu', { exact: true }).waitFor();
        assert.ok((await page.locator('main').innerText()).includes('1.234,56'));
        assert.ok((await page.locator('main').innerText()).includes('12/9/26'));
        assert.equal(await page.evaluate(() => window.queries), count, 'Changing language must not reload business data');
        assert.equal(await page.evaluate(() => localStorage.getItem('fmc.bms.language')), 'vi');
        await page.reload();
        await page.getByRole('heading', { name: 'Trung tâm vận hành BMS' }).waitFor();
        // A same-origin tab should receive storage events without remounting.
        const other = await context.newPage();
        await other.goto(url);
        await other.getByRole('combobox', { name: 'Ngôn ngữ' }).selectOption('en');
        await page.getByRole('heading', { name: 'BMS Operations Center' }).waitFor();
        await other.close();
        await page.goto(url + '?fail');
        await page.getByText('Unable to load data', { exact: true }).waitFor();
        await page.getByRole('combobox', { name: 'Language' }).selectOption('vi');
        await page.getByText('Không tải được dữ liệu', { exact: true }).waitFor();
        assert.ok((await page.getByRole('alert').innerText()).includes('Không thể tải dữ liệu vận hành'));
        await page.goto(url);
        await page.getByRole('button', { name: 'Thêm tòa nhà' }).click();
        await page.getByRole('alert').waitFor();
        await page.getByRole('combobox', { name: 'Ngôn ngữ' }).selectOption('en');
        assert.ok((await page.getByRole('alert').innerText()).includes('Unable to open'));
        await page.setViewportSize({ width: 390, height: 844 });
        await page.screenshot({ path: path.join(testDir, 'dashboard-english-mobile.png'), fullPage: true });
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth), false);
        const blocked = await browser.newContext();
        await blocked.addInitScript(() => Object.defineProperty(window, 'localStorage', { get() { throw new Error('Storage blocked'); } }));
        const blockedPage = await blocked.newPage();
        blockedPage.on('pageerror', error => errors.push(error.message));
        await blockedPage.goto(url);
        await blockedPage.getByRole('heading', { name: 'BMS Operations Center' }).waitFor();
        await blockedPage.getByRole('combobox', { name: 'Language' }).selectOption('vi');
        await blockedPage.getByRole('heading', { name: 'Trung tâm vận hành BMS' }).waitFor();
        await blocked.close();
        assert.deepEqual(errors, []);
        console.log('PASS: English default, Vietnamese switch, locale formats, statuses, persistence, cross-tab sync, translated errors, no refetch, blocked storage and mobile layout.');
    } finally {
        if (browser) await browser.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
