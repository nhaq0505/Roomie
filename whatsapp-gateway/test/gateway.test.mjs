import { test } from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { randomBytes, randomUUID } from 'node:crypto';
import { Gateway, isTransientRestoreError } from '../src/gateway.mjs';
import { EncryptedStore } from '../src/store.mjs';
import { createBaileysClient, reconnectDelayMs } from '../src/whatsapp.mjs';

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


test('Baileys adapter handles CJS-ESM namespace export and 515 restartRequired after QR scan', async () => {
  const sockets = [];
  const makeWASocket = () => {
    const ev = new EventEmitter();
    const sock = {
      ev,
      user: { id: '84901234567:2@s.whatsapp.net' },
      onWhatsApp: async (jid) => [{ exists: true, jid }],
      sendMessage: async () => ({ key: { id: 'wa-msg-2' } }),
      end: () => {}
    };
    sockets.push(sock);
    return sock;
  };
  const cjsNamespaceBaileys = {
    default: {
      __esModule: true,
      default: makeWASocket,
      makeWASocket,
      initAuthCreds: () => ({}),
      BufferJSON: { replacer: (_, v) => v, reviver: (_, v) => v },
      DisconnectReason: { loggedOut: 401, restartRequired: 515 }
    }
  };
  const cjsNamespaceQr = {
    default: {
      toDataURL: async (qr) => `data:image/png;base64,${Buffer.from(qr).toString('base64')}`
    }
  };
  const client = createBaileysClient(cjsNamespaceBaileys, cjsNamespaceQr);
  const events = [];
  const loginPromise = client.loginQR({}, e => events.push(e));
  assert.equal(sockets.length, 1);

  sockets[0].ev.emit('connection.update', { qr: 'PAIR_QR_1' });
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(events.some(e => e.type === 0 && e.data.image === Buffer.from('PAIR_QR_1').toString('base64')), true);

  // Simulate user scanning QR on phone: pair-success updates creds, sets isNewLogin, and WA closes stream with 515.
  sockets[0].ev.emit('creds.update', { me: { id: '84901234567:2@s.whatsapp.net' } });
  sockets[0].ev.emit('connection.update', { isNewLogin: true });
  sockets[0].ev.emit('connection.update', {
    connection: 'close',
    lastDisconnect: { error: { output: { statusCode: 515 } } }
  });

  assert.equal(events.some(e => e.type === 2), true);
  assert.equal(sockets.length, 2);

  sockets[1].ev.emit('connection.update', { connection: 'open' });
  const api = await loginPromise;
  assert.equal(api.getOwnId(), '84901234567@s.whatsapp.net');
  const intlUser = await api.findUser('+821012345678');
  assert.deepEqual(intlUser, { uid: '821012345678@s.whatsapp.net' });
});

test('gateway send accepts international E.164 phone numbers', async t => {
  const f = fixture(t);
  await connect(f);
  const res = await f.gateway.send(owner, { ...f.body(), phone: '+821012345678' });
  assert.equal(res.status, 'Sent');
  assert.equal(f.sends(), 1);
});

test('transient restore failure keeps credentials and retries with backoff; other failures clear them', async t => {
  const f = fixture(t); await connect(f);
  let mode = 'network', calls = 0;
  const flaky = () => ({
    login: async () => {
      calls++;
      if (mode === 'network') throw Object.assign(new TypeError('fetch failed'), { cause: { code: 'ENOTFOUND' } });
      throw new Error('fixture invalid session');
    },
    loginQR: f.factory().loginQR
  });
  let now = Date.parse('2026-10-20T01:00:00Z');
  const restarted = new Gateway(f.store, flaky, () => now);
  const tick = () => new Promise(resolve => setImmediate(resolve));
  assert.equal((await restarted.status(owner)).status, 'Restoring'); await tick();
  assert.equal(calls, 1);
  assert.notEqual(f.store.read(owner).credentials, null, 'network failure must not clear saved credentials');
  assert.equal((await restarted.status(owner)).status, 'Restoring'); await tick();
  assert.equal(calls, 1, 'no retry before the backoff elapses');
  now += 5000; await restarted.status(owner); await tick();
  assert.equal(calls, 2, 'retries after the first backoff');
  now += 9999; await restarted.status(owner); await tick();
  assert.equal(calls, 2, 'backoff doubles after each transient failure');
  mode = 'auth'; now += 1; await restarted.status(owner); await tick();
  assert.equal(calls, 3);
  assert.equal(f.store.read(owner).credentials, null, 'a non-transient failure clears the session');
  assert.equal((await restarted.status(owner)).status, 'Disconnected');
});

test('restore error classification honours explicit flags and network codes', () => {
  assert.equal(isTransientRestoreError(Object.assign(new Error('x'), { transient: true })), true);
  assert.equal(isTransientRestoreError(Object.assign(new Error('x'), { transient: false, code: 'ECONNRESET' })), false);
  assert.equal(isTransientRestoreError(Object.assign(new Error('x'), { code: 'ETIMEDOUT' })), true);
  assert.equal(isTransientRestoreError(Object.assign(new Error('x'), { name: 'AbortError' })), true);
  assert.equal(isTransientRestoreError(new Error('invalid cookie')), false);
  assert.equal(isTransientRestoreError(null), false);
});

test('an unreadable session file does not crash startup or affect other owners', async t => {
  const f = fixture(t); await connect(f);
  t.mock.method(console, 'error', () => {});
  const garbage = Buffer.from('not-an-encrypted-session-file-0123456789abcdef');
  fs.writeFileSync(f.store.file(other), garbage);
  const restarted = new Gateway(f.store, f.factory);
  assert.equal((await restarted.status(other)).status, 'Disconnected');
  assert.deepEqual(fs.readFileSync(f.store.file(other)), garbage, 'the unreadable file is left untouched');
  assert.equal((await restarted.status(owner)).status, 'Restoring');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal((await restarted.status(owner)).status, 'Connected');
});

function fakeBaileys() {
  const sockets = [];
  const makeWASocket = (options) => {
    const ev = new EventEmitter();
    const sock = { ev, auth: options.auth, user: { id: '84901234567:2@s.whatsapp.net' },
      onWhatsApp: async (jid) => [{ exists: true, jid }], sendMessage: async () => ({ key: { id: 'm' } }), end: () => {} };
    sockets.push(sock);
    return sock;
  };
  const baileys = { makeWASocket, initAuthCreds: () => ({}), BufferJSON: { replacer: (_, v) => v, reviver: (_, v) => v },
    DisconnectReason: { loggedOut: 401, restartRequired: 515 } };
  const qr = { toDataURL: async () => 'data:image/png;base64,AA==' };
  return { sockets, client: createBaileysClient(baileys, qr) };
}
const savedSession = { creds: { me: { id: '84901234567:2@s.whatsapp.net' } }, keys: {} };
const closeWith = (sock, statusCode) => sock.ev.emit('connection.update', { connection: 'close', lastDisconnect: { error: { output: { statusCode } } } });

test('Baileys restore rejects as transient on network close and as terminal on logout', async () => {
  const a = fakeBaileys();
  const transient = a.client.login(savedSession);
  closeWith(a.sockets[0], 408);
  await assert.rejects(transient, e => e.transient === true);
  const b = fakeBaileys();
  const terminal = b.client.login(savedSession);
  closeWith(b.sockets[0], 401);
  await assert.rejects(terminal, e => e.transient === false);
  const c = fakeBaileys();
  const banned = c.client.login(savedSession);
  closeWith(c.sockets[0], 403);
  await assert.rejects(banned, e => e.transient === false);
});

test('Baileys signal key updates are persisted (debounced) and terminal codes close the session', async () => {
  const { sockets, client } = fakeBaileys();
  const loginPromise = client.login(savedSession);
  sockets[0].ev.emit('connection.update', { connection: 'open' });
  const api = await loginPromise;
  const saved = [];
  api.listener.on('credentials', snap => saved.push(snap));
  await sockets[0].auth.keys.set({ session: { 'peer.0': { record: 'R1' } } });
  await sockets[0].auth.keys.set({ 'pre-key': { 7: { k: 'P7' } } });
  assert.equal(saved.length, 0, 'key writes are debounced');
  await new Promise(resolve => setTimeout(resolve, 1100));
  assert.equal(saved.length, 1, 'a burst of key writes produces one persisted snapshot');
  assert.deepEqual(saved[0].keys.session['peer.0'], { record: 'R1' });
  assert.deepEqual(saved[0].keys['pre-key']['7'], { k: 'P7' });
  let closed = false;
  api.listener.on('closed', () => { closed = true; });
  closeWith(sockets[0], 500);
  assert.equal(closed, true, 'badSession (500) is terminal');
  api.listener.stop();
});

test('reconnect delay grows exponentially and is capped', () => {
  assert.equal(reconnectDelayMs(0), 2000);
  assert.equal(reconnectDelayMs(1), 4000);
  assert.equal(reconnectDelayMs(3), 16000);
  assert.equal(reconnectDelayMs(50), 5 * 60 * 1000);
});
