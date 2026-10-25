import { test } from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { Gateway } from '../src/gateway.mjs';
import { EncryptedStore } from '../src/store.mjs';
import { createBaileysClient } from '../src/whatsapp.mjs';

const owner = '11111111-1111-1111-1111-111111111111';
const other = '22222222-2222-2222-2222-222222222222';
function fixture(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'roomie-whatsapp-gateway-'));
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  const key = randomBytes(32).toString('base64');
  const store = new EncryptedStore(dir, key);
  const listener = new EventEmitter();
  listener.start = () => listener.emit('connected'); listener.stop = () => {};
  let sends = 0, fail = false, findFail = false;
  const api = { listener, getOwnId: () => '84912345678@s.whatsapp.net', findUser: async () => { if (findFail) throw Error('fixture'); return { uid: '84912345678@s.whatsapp.net' }; },
    sendMessage: async (message, uid, type) => { sends++; if (fail) throw Error('fixture timeout');
      assert.equal(uid, '84912345678@s.whatsapp.net'); assert.equal(type, 0); assert.equal(message.attachments, undefined);
      assert.ok(message.msg.includes('https://roomie.example/t/fixture'));
      return { message: { msgId: 'fixture-wa-message' }, attachment: [] }; } };

  let now = Date.parse('2026-10-20T01:00:00Z');
  const factory = () => ({ login: async () => api, loginQR: async (_, callback) => {
    callback({ type: 0, data: { image: 'fixture-base64' }, actions: { abort() {} } });
    callback({ type: 4, data: { creds: { me: { id: '84912345678:1@s.whatsapp.net' } }, keys: { preKeys: { 1: 'SECRET_KEY' } } } });
    return api;
  } });
  const gateway = new Gateway(store, factory, () => now);
  const body = () => ({ attemptId: randomUUID(), phone: '0912345678', message: 'Fixture invoice https://roomie.example/t/fixture' });
  return { dir, key, store, api, gateway, factory, body, advance: ms => { now += ms; }, set: value => { now = Date.parse(value); }, sends: () => sends, fail: () => { fail = true; }, findFail: () => { findFail = true; } };
}
async function connect(f) { f.gateway.connect(owner); await new Promise(resolve => setImmediate(resolve)); assert.equal((await f.gateway.status(owner)).status, 'Connected'); }

test('saved session is encrypted and authentication failures do not reveal credentials', t => {
  const f = fixture(t); f.store.write(owner, { creds: 'WA_SECRET_KEY' });
  assert.deepEqual(f.store.owners(), [owner]);
  assert.deepEqual(f.store.read(owner), { creds: 'WA_SECRET_KEY' });
  assert.equal(fs.readFileSync(f.store.file(owner)).includes('WA_SECRET_KEY'), false);
  assert.throws(() => new EncryptedStore(f.dir, randomBytes(32).toString('base64')).read(owner));
  assert.throws(() => f.store.read('../../secret'));
  const raw = Buffer.from(fs.readFileSync(f.store.file(owner)));
  raw[raw.length - 1] ^= 0xff;
  fs.writeFileSync(f.store.file(owner), raw);
  assert.throws(() => f.store.read(owner));
});

test('QR login persists credentials and a restart restores the real session', async t => {
  const f = fixture(t); await connect(f);
  const restarted = new Gateway(f.store, f.factory);
  assert.equal((await restarted.status(owner)).status, 'Restoring');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await restarted.status(owner)).status, 'Connected');
});

test('text message response is success and duplicate attempt never resends', async t => {
  const f = fixture(t); await connect(f); const body = f.body();
  assert.equal((await f.gateway.send(owner, body)).status, 'Sent');
  assert.equal((await f.gateway.send(owner, body)).status, 'Sent'); assert.equal(f.sends(), 1);
  assert.equal((await f.gateway.send(owner, { ...body, message: body.message + ' changed' })).status, 'Unknown'); assert.equal(f.sends(), 1);
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

test('crash after reserving submission returns unknown without contacting WhatsApp', async t => {
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
  const f = fixture(t); await connect(f); f.api.listener.emit('closed', 401, 'fixture signout');
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
  callback({ type: 4, data: { creds: 'STALE_SECRET' } }); finish(f.api);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(f.store.read(owner).credentials, null); assert.equal((await gateway.status(owner)).status, 'Disconnected'); assert.equal(stopped, true);
});

test('QR intermediate events, expiry abort, listener reconnect and send input validation edge cases', async t => {
  const f = fixture(t);
  let qrCallback;
  let aborted = 0;
  const qrGateway = new Gateway(f.store, () => ({
    loginQR: (_, cb) => {
      qrCallback = cb;
      return new Promise(() => {});
    }
  }));
  qrGateway.connect(owner);
  qrCallback({ type: 0, data: { image: 'QUJD' }, actions: { abort: () => { aborted++; } } });
  assert.deepEqual(await qrGateway.status(owner), { status: 'QrReady', qrImage: 'data:image/png;base64,QUJD' });
  qrCallback({ type: 2, actions: { abort: () => { aborted++; } } });
  assert.deepEqual(await qrGateway.status(owner), { status: 'AwaitingConfirmation', qrImage: null });
  qrCallback({ type: 1, actions: { abort: () => { aborted++; } } });
  assert.deepEqual(await qrGateway.status(owner), { status: 'Expired', qrImage: null });
  assert.equal(aborted, 1);

  await connect(f);
  f.api.listener.emit('disconnected');
  assert.equal((await f.gateway.status(owner)).status, 'Restoring');
  f.api.listener.emit('connected');
  assert.equal((await f.gateway.status(owner)).status, 'Connected');
  f.api.listener.emit('error', new Error('socket glitch'));
  assert.equal((await f.gateway.status(owner)).status, 'Restoring');
  f.api.listener.emit('connected');

  const valid = f.body();
  await assert.rejects(() => f.gateway.send(owner, { ...valid, attemptId: 'not-a-uuid' }), /Invalid message/);
  await assert.rejects(() => f.gateway.send(owner, { ...valid, phone: '0123456789' }), /Invalid message/);
  await assert.rejects(() => f.gateway.send(owner, { ...valid, phone: '091234567' }), /Invalid message/);
  await assert.rejects(() => f.gateway.send(owner, { ...valid, message: 'x'.repeat(6001) }), /Invalid message/);
  assert.throws(() => new EncryptedStore(f.dir, 'short-key'), /32 bytes/);
});

test('Baileys adapter normalizes Vietnam phone to JID, emits QR and sends text message', async () => {
  const ev = new EventEmitter();
  let lastSent;
  const mockBaileys = {
    default: () => ({
      ev,
      user: { id: '84987654321:14@s.whatsapp.net' },
      onWhatsApp: async (jid) => [{ exists: jid === '84912345678@s.whatsapp.net', jid }],
      sendMessage: async (jid, payload) => { lastSent = { jid, payload }; return { key: { id: 'wa-msg-1' } }; },
      end: () => {}
    }),
    initAuthCreds: () => ({ me: { id: '84987654321:14@s.whatsapp.net' } }),
    BufferJSON: { replacer: (_, v) => v, reviver: (_, v) => v },
    DisconnectReason: { loggedOut: 401 }
  };
  const mockQr = { toDataURL: async (qr) => `data:image/png;base64,${Buffer.from(qr).toString('base64')}` };
  const client = createBaileysClient(mockBaileys, mockQr);
  const events = [];
  const loginPromise = client.loginQR({}, e => events.push(e));
  ev.emit('connection.update', { qr: 'MOCK_QR' });
  await new Promise(resolve => setImmediate(resolve));
  ev.emit('connection.update', { connection: 'open' });
  const api = await loginPromise;
  assert.equal(api.getOwnId(), '84987654321@s.whatsapp.net');
  assert.equal(events.some(e => e.type === 0 && e.data.image === Buffer.from('MOCK_QR').toString('base64')), true);
  assert.equal(events.some(e => e.type === 4 && e.data?.creds), true);
  const found = await api.findUser('0912345678');
  assert.deepEqual(found, { uid: '84912345678@s.whatsapp.net' });
  const missing = await api.findUser('0999999999');
  assert.equal(missing, null);
  const sent = await api.sendMessage({ msg: 'Hello' }, found.uid);
  assert.equal(sent.message.msgId, 'wa-msg-1');
  assert.deepEqual(lastSent, { jid: '84912345678@s.whatsapp.net', payload: { text: 'Hello' } });
});
