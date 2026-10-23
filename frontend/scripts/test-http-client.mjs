import assert from 'node:assert/strict';
import vm from 'node:vm';
import { build } from 'esbuild';

// Exercise the extracted transport through the same public compatibility entry point.
const bundle = await build({
  stdin: { contents: 'export { api, ApiError, listFrom } from "./src/api"; export { request } from "./src/shared/http/client";', resolveDir: process.cwd(), loader: 'ts' },
  bundle: true, write: false, format: 'cjs', platform: 'node',
});
let calls = [];
let respond;
const module = { exports: {} };
vm.runInNewContext(bundle.outputFiles[0].text, {
  module, exports: module.exports, URLSearchParams, Headers, AbortSignal, Intl, Date, Set, Map, FormData, Blob,
  fetch: async (url, init) => { calls.push({ url, init }); return respond(); },
});
const { api, ApiError, listFrom, request } = module.exports;
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
assert.equal(JSON.parse(calls[0].init.body).rememberMe, false);
await api.auth.login('owner@example.invalid', 'fixture-password', true);
assert.equal(JSON.parse(calls[1].init.body).rememberMe, true);

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

// Exercise new roomsApi document & Word export endpoints, promotionsApi, and subscriptionApi.redeemCode
const featureBundle = await build({
  stdin: {
    contents: `
      export { roomsApi } from "./src/features/rooms/api";
      export { promotionsApi } from "./src/features/admin/api";
      export { subscriptionApi } from "./src/features/subscription/api";
    `,
    resolveDir: process.cwd(),
    loader: 'ts',
  },
  bundle: true,
  write: false,
  format: 'cjs',
  platform: 'node',
});
const featureMod = { exports: {} };
vm.runInNewContext(featureBundle.outputFiles[0].text, {
  module: featureMod,
  exports: featureMod.exports,
  URLSearchParams,
  Headers,
  AbortSignal,
  Intl,
  Date,
  Set,
  Map,
  fetch: async (url, init) => {
    calls.push({ url, init });
    return respond();
  },
});
const { roomsApi, promotionsApi, subscriptionApi } = featureMod.exports;

calls = [];
respond = () => ({ ok: true, status: 200, text: async () => JSON.stringify({ roomId: 'room-1', occupancyId: 'occ-1' }) });
await roomsApi.getDocuments('room 1');
assert.equal(calls.at(-1).url, '/api/rooms/room%201/documents');

await roomsApi.saveDocuments('room-1', { occupancyId: 'occ-1', ownerName: 'Chủ trọ mới', tenantCitizenId: '079099001234', paymentDueDay: 5 });
assert.equal(calls.at(-1).url, '/api/rooms/room-1/documents');
assert.equal(calls.at(-1).init.method, 'PUT');
assert.equal(JSON.parse(calls.at(-1).init.body).ownerName, 'Chủ trọ mới');
assert.equal(JSON.parse(calls.at(-1).init.body).tenantCitizenId, '079099001234');

respond = () => ({ ok: true, status: 200, text: async () => JSON.stringify({ id: 'fixture-owner', name: 'Chủ trọ cập nhật', phone: '0909888777', citizenId: '079088009999' }) });
const updatedProfile = await api.auth.updateProfile({ name: 'Chủ trọ cập nhật', phone: '0909888777', citizenId: '079088009999' });
assert.equal(calls.at(-1).url, '/api/auth/profile');
assert.equal(calls.at(-1).init.method, 'PUT');
assert.equal(JSON.parse(calls.at(-1).init.body).name, 'Chủ trọ cập nhật');
assert.equal(updatedProfile.citizenId, '079088009999');

const docxBlob = new Blob(['fixture-docx']);
respond = () => ({ ok: true, blob: async () => docxBlob });
assert.equal(await roomsApi.downloadContractDocx('room-1'), docxBlob);
assert.equal(calls.at(-1).url, '/api/rooms/room-1/documents/contract.docx');
assert.equal(calls.at(-1).init.headers.get('Accept'), 'application/vnd.openxmlformats-officedocument.wordprocessingml.document');

assert.equal(await roomsApi.downloadCt01Docx('room-1'), docxBlob);
assert.equal(calls.at(-1).url, '/api/rooms/room-1/documents/ct01.docx');
assert.equal(calls.at(-1).init.headers.get('Accept'), 'application/vnd.openxmlformats-officedocument.wordprocessingml.document');

respond = () => ({ ok: true, status: 200, text: async () => JSON.stringify({ welcome: { id: 'welcome', isEnabled: true, rewardMonths: 3 }, codes: [] }) });
await promotionsApi.get();
assert.equal(calls.at(-1).url, '/api/admin/promotions');

await promotionsApi.updateWelcome({ isEnabled: true, rewardMonths: 6, startsAt: null, endsAt: null });
assert.equal(calls.at(-1).url, '/api/admin/promotions/welcome');
assert.equal(calls.at(-1).init.method, 'PUT');
assert.equal(JSON.parse(calls.at(-1).init.body).rewardMonths, 6);

await promotionsApi.createCode({ code: 'TET2026', rewardMonths: 3, maxRedemptions: 50, isActive: true });
assert.equal(calls.at(-1).url, '/api/admin/promotions/codes');
assert.equal(calls.at(-1).init.method, 'POST');

await promotionsApi.updateCode('code-1', { description: 'Updated', rewardMonths: 3, maxRedemptions: 100, isActive: false });
assert.equal(calls.at(-1).url, '/api/admin/promotions/codes/code-1');
assert.equal(calls.at(-1).init.method, 'PATCH');

respond = () => ({ ok: true, status: 204, text: async () => '' });
await promotionsApi.deleteCode('code-1');
assert.equal(calls.at(-1).url, '/api/admin/promotions/codes/code-1');
assert.equal(calls.at(-1).init.method, 'DELETE');

respond = () => ({ ok: true, status: 200, text: async () => JSON.stringify({ success: true, addedMonths: 3, subscriptionTier: 'Pro', subscriptionExpiresAt: '2027-01-23T00:00:00Z' }) });
const redeemRes = await subscriptionApi.redeemCode('TET2026');
assert.equal(calls.at(-1).url, '/api/subscription/redeem-code');
assert.equal(calls.at(-1).init.method, 'POST');
assert.equal(JSON.parse(calls.at(-1).init.body).code, 'TET2026');
assert.equal(redeemRes.addedMonths, 3);
console.log('PASS: roomsApi documents/docx, promotionsApi, and subscriptionApi.redeemCode verified');

// Multipart uploads must keep the browser-generated boundary instead of a JSON content type.
calls = [];
respond = () => ({ ok: true, status: 201, text: async () => JSON.stringify({ id: 'fixture-attachment' }) });
const form = new FormData();
form.append('file', new Blob(['%PDF-1.4']), 'receipt.pdf');
assert.equal((await request('/api/expenses/fixture/attachments', { method: 'POST', body: form })).id, 'fixture-attachment');
assert.equal(calls[0].init.headers.get('Content-Type'), null, 'FormData uploads must not be forced to application/json');
assert.equal(calls[0].init.body, form);
console.log('PASS: multipart uploads keep the browser boundary');
