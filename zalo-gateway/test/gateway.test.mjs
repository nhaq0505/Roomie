import { test } from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { Gateway } from '../src/gateway.mjs';
import { EncryptedStore } from '../src/store.mjs';
import { vietqr, crc16 } from '../src/vietqr.mjs';

const owner = '11111111-1111-1111-1111-111111111111';
const other = '22222222-2222-2222-2222-222222222222';
function fixture(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'roomie-zalo-gateway-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const key = randomBytes(32).toString('base64');
  const store = new EncryptedStore(dir, key);
  const listener = new EventEmitter();
  listener.start = () => listener.emit('connected'); listener.stop = () => {};
  let sends = 0, fail = false, findFail = false;
  const api = { listener, getOwnId: () => 'fixture-uid', findUser: async () => { if (findFail) throw Error('fixture'); return { uid: 'fixture-tenant' }; },
    sendMessage: async (message, uid, type) => { sends++; if (fail) throw Error('fixture timeout');
      assert.equal(uid, 'fixture-tenant'); assert.equal(type, 0); assert.equal(message.attachments[0].filename, 'hoa-don-qr.png');
      assert.deepEqual(message.attachments[0].data.subarray(0, 8), Buffer.from([137,80,78,71,13,10,26,10]));
      return { message: null, attachment: [{ msgId: 'fixture-message' }] }; } };
  let now = Date.parse('2026-10-20T01:00:00Z');
  const factory = () => ({ login: async () => api, loginQR: async (_, callback) => {
    callback({ type: 0, data: { image: 'fixture-base64' }, actions: { abort() {} } });
    callback({ type: 4, data: { cookie: [{ name: 'fixture', value: 'COOKIE_SECRET' }], imei: 'fixture-imei', userAgent: 'fixture-UA' } });
    return api;
  } });
  const gateway = new Gateway(store, factory, () => now);
  const body = () => ({ attemptId: randomUUID(), phone: '0912345678', message: 'Fixture invoice', bankBin: '970436', bankAccount: '123456789', amount: 75000, transferContent: 'R1234567890ABCDEF' });
  return { dir, key, store, api, gateway, factory, body, advance: ms => { now += ms; }, set: value => { now = Date.parse(value); }, sends: () => sends, fail: () => { fail = true; }, findFail: () => { findFail = true; } };
}
async function connect(f) { f.gateway.connect(owner); await new Promise(resolve => setImmediate(resolve)); assert.equal((await f.gateway.status(owner)).status, 'Connected'); }
function fields(text) {
  const out = {}; let i = 0;
  while (i < text.length) { const id = text.slice(i, i + 2); const n = Number(text.slice(i + 2, i + 4)); assert.ok(Number.isInteger(n)); out[id] = text.slice(i + 4, i + 4 + n); i += 4 + n; }
  assert.equal(i, text.length); return out;
}
test('VietQR contains the exact bank, account, VND amount, transfer reference and valid CRC', () => {
  assert.equal(crc16('123456789'), '29B1');
  const payload = vietqr({ bankBin: '970436', bankAccount: '123456789', amount: 75000, transferContent: 'R1234567890ABCDEF' });
  const root = fields(payload); const merchant = fields(root['38']); const bank = fields(merchant['01']);
  assert.equal(root['53'], '704'); assert.equal(root['54'], '75000'); assert.equal(root['58'], 'VN');
  assert.equal(merchant['00'], 'A000000727'); assert.equal(merchant['02'], 'QRIBFTTA');
  assert.equal(bank['00'], '970436'); assert.equal(bank['01'], '123456789'); assert.equal(fields(root['62'])['08'], 'R1234567890ABCDEF');
  assert.equal(root['63'], crc16(payload.slice(0, -4)));
});
test('VietQR rejects invalid bank fields, amount and transfer content', () => {
  const base = { bankBin: '970436', bankAccount: '123456789', amount: 75000, transferContent: 'R123' };
  for (const patch of [{ bankBin: 'bad' }, { bankAccount: '123\n456' }, { amount: 0 }, { amount: 1.2 }, { transferContent: 'x'.repeat(26) }]) assert.throws(() => vietqr({ ...base, ...patch }));
});
test('saved session is encrypted and authentication failures do not reveal credentials', t => {
  const f = fixture(t); f.store.write(owner, { cookie: 'COOKIE_SECRET' });
  assert.deepEqual(f.store.read(owner), { cookie: 'COOKIE_SECRET' });
  assert.equal(fs.readFileSync(f.store.file(owner)).includes('COOKIE_SECRET'), false);
  assert.throws(() => new EncryptedStore(f.dir, randomBytes(32).toString('base64')).read(owner));
  assert.throws(() => f.store.read('../../secret'));
});
test('QR login persists credentials and a restart restores the real session', async t => {
  const f = fixture(t); await connect(f);
  const restarted = new Gateway(f.store, f.factory);
  assert.equal((await restarted.status(owner)).status, 'Restoring');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await restarted.status(owner)).status, 'Connected');
});
test('single image caption response is success and duplicate attempt never resends', async t => {
  const f = fixture(t); await connect(f); const body = f.body();
  assert.equal((await f.gateway.send(owner, body)).status, 'Sent');
  assert.equal((await f.gateway.send(owner, body)).status, 'Sent'); assert.equal(f.sends(), 1);
  assert.equal((await f.gateway.send(owner, { ...body, amount: 1 })).status, 'Unknown'); assert.equal(f.sends(), 1);
});
test('replayed success survives gateway process restart', async t => {
  const f = fixture(t); await connect(f); const body = f.body(); await f.gateway.send(owner, body);
  const restarted = new Gateway(f.store, f.factory);
  assert.equal((await restarted.send(owner, body)).status, 'Sent'); assert.equal(f.sends(), 1);
});
test('unknown network outcome is durable and never resent', async t => {
  const f = fixture(t); await connect(f); f.fail(); const body = f.body();
  assert.equal((await f.gateway.send(owner, body)).status, 'Unknown');
  assert.equal((await f.gateway.send(owner, body)).status, 'Unknown'); assert.equal(f.sends(), 1);
});
test('crash after reserving submission returns unknown without contacting Zalo', async t => {
  const f = fixture(t); await connect(f); const body = f.body();
  await f.gateway.send(owner, body); const saved = f.store.read(owner); saved.attempts[body.attemptId].status = 'Sending'; f.store.write(owner, saved);
  const restarted = new Gateway(f.store, f.factory);
  assert.equal((await restarted.send(owner, body)).status, 'Unknown'); assert.equal(f.sends(), 1);
});
test('spacing and time boundaries are also enforced by gateway', async t => {
  const f = fixture(t); await connect(f); await f.gateway.send(owner, f.body());
  assert.equal((await f.gateway.send(owner, f.body())).status, 'Deferred');
  f.advance(5000); assert.equal((await f.gateway.send(owner, f.body())).status, 'Sent');
  f.set('2026-10-20T14:00:00Z'); assert.equal((await f.gateway.send(owner, f.body())).status, 'Deferred');
  f.set('2026-10-20T23:59:59Z'); assert.equal((await f.gateway.send(owner, f.body())).status, 'Deferred');
  f.set('2026-10-21T00:00:00Z'); assert.equal((await f.gateway.send(owner, f.body())).status, 'Sent');
});
test('lookup failure does not submit a message', async t => {
  const f = fixture(t); await connect(f); f.findFail();
  assert.equal((await f.gateway.send(owner, f.body())).status, 'Failed'); assert.equal(f.sends(), 0);
});
test('session loss clears saved credentials and cannot report connected', async t => {
  const f = fixture(t); await connect(f); f.api.listener.emit('closed', 3003, 'fixture signout');
  assert.equal((await f.gateway.status(owner)).status, 'Disconnected');
  assert.equal(f.store.read(owner).credentials, null);
  assert.equal((await f.gateway.send(owner, f.body())).status, 'Disconnected');
});
test('disconnect deletes session and releases account for another owner', async t => {
  const f = fixture(t); await connect(f);
  f.gateway.connect(other); await new Promise(resolve => setImmediate(resolve));
  assert.equal((await f.gateway.status(other)).status, 'AccountInUse');
  f.gateway.disconnect(owner); assert.equal(f.store.read(owner).credentials, null);
  f.gateway.connect(other); await new Promise(resolve => setImmediate(resolve));
  assert.equal((await f.gateway.status(other)).status, 'Connected');
});
test('disconnect while QR login is pending fences late credentials and stale API', async t => {
  const f = fixture(t); let callback, finish; let stopped = false;
  f.api.listener.stop = () => { stopped = true; };
  const gateway = new Gateway(f.store, () => ({ loginQR: (_, cb) => { callback = cb; return new Promise(resolve => { finish = resolve; }); } }));
  gateway.connect(owner); gateway.disconnect(owner);
  callback({ type: 4, data: { cookie: 'STALE_SECRET' } }); finish(f.api);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.store.read(owner).credentials, null); assert.equal((await gateway.status(owner)).status, 'Disconnected'); assert.equal(stopped, true);
});
