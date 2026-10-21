import assert from 'node:assert/strict';
import vm from 'node:vm';
import { build } from 'esbuild';

// Exercise the extracted transport through the same public compatibility entry point.
const bundle = await build({
  stdin: { contents: 'export { api, ApiError, listFrom } from "./src/api";', resolveDir: process.cwd(), loader: 'ts' },
  bundle: true, write: false, format: 'cjs', platform: 'node',
});
let calls = [];
let respond;
const module = { exports: {} };
vm.runInNewContext(bundle.outputFiles[0].text, {
  module, exports: module.exports, URLSearchParams, Headers, AbortSignal, Intl, Date, Set, Map,
  fetch: async (url, init) => { calls.push({ url, init }); return respond(); },
});
const { api, ApiError, listFrom } = module.exports;
respond = () => ({ ok: true, status: 200, text: async () => JSON.stringify({ data: { id: 'fixture-owner' } }) });
assert.equal((await api.auth.login('owner@example.invalid', 'fixture-password')).id, 'fixture-owner');
assert.equal(calls.length, 1);
assert.equal(calls[0].url, '/api/auth/login');
assert.equal(calls[0].init.method, 'POST');
assert.equal(calls[0].init.credentials, 'same-origin');
assert.equal(calls[0].init.headers.get('Accept-Language'), 'vi-VN');
assert.equal(calls[0].init.headers.get('Content-Type'), 'application/json');
assert.equal(calls[0].init.headers.get('Accept'), 'application/json');
assert.ok(calls[0].init.signal instanceof AbortSignal);
assert.equal(JSON.parse(calls[0].init.body).email, 'owner@example.invalid');

calls = [];
respond = () => ({ ok: false, status: 409, text: async () => JSON.stringify({ message: 'fixture-conflict', version: 2 }) });
await assert.rejects(api.invoices.addReceipt('fixture-invoice', { amount: 100, note: '', idempotencyKey: 'fixture-key' }), error =>
  error instanceof ApiError && error.status === 409 && error.details.version === 2);
assert.equal(calls.length, 1, 'A rejected financial write must not be retried by the transport');

respond = () => { throw new Error('fixture-network-failure'); };
await assert.rejects(api.auth.me(), error => error instanceof ApiError && error.status === 0);
respond = () => ({ ok: true, status: 204, text: async () => '' });
assert.equal(await api.auth.logout(), undefined);
for (const value of [[1], { data: [1] }, { items: [1] }, { results: [1] }, { records: [1] }]) {
  assert.equal(JSON.stringify(listFrom(value)), '[1]');
}

const blob = new Blob(['fixture-xlsx']);
respond = () => ({ ok: true, blob: async () => blob });
assert.equal(await api.reports.export('receipts', { dateFrom: '2026-10-01', dateTo: '2026-10-31', page: 2 }), blob);
assert.ok(!calls.at(-1).url.includes('page='), 'Export must preserve the full filtered result');
assert.equal(calls.at(-1).init.headers.get('Accept'), 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
respond = () => ({ ok: false, status: 401, text: async () => 'fixture-unauthorized' });
await assert.rejects(api.reports.export('debt', { dateFrom: '2026-10-01', dateTo: '2026-10-31' }), error =>
  error instanceof ApiError && error.status === 401);
console.log('PASS: extracted HTTP transport preserves headers, errors, envelopes, single financial submissions and exports');

respond = () => ({ ok: false, status: 409, text: async () => JSON.stringify({ message: 'Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.' }) });
await assert.rejects(api.invoices.addReceipt('fixture-invoice', { amount: 100, idempotencyKey: 'stale-invoice' }), error =>
  error.message === 'Hóa đơn đã thay đổi. Vui lòng kiểm tra khoản tiền thực nhận trước khi ghi thu.');
