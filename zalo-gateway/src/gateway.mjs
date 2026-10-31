import { createHash } from 'node:crypto';

const TRANSIENT_CODES = new Set(['ECONNRESET', 'ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN', 'ETIMEDOUT', 'EPIPE', 'ENETUNREACH', 'EHOSTUNREACH',
  'UND_ERR_CONNECT_TIMEOUT', 'UND_ERR_SOCKET', 'UND_ERR_HEADERS_TIMEOUT', 'UND_ERR_BODY_TIMEOUT']);
const RESTORE_BACKOFF_BASE_MS = 5000;
const RESTORE_BACKOFF_MAX_MS = 5 * 60 * 1000;

// A restore failure caused by the network (DNS, reset, timeout) must not log the owner out:
// keep the saved credentials and retry with backoff. Only other failures clear the session.
export function isTransientRestoreError(error) {
  if (!error) return false;
  if (error.transient === true) return true;
  if (error.transient === false) return false;
  const codes = [error.code, error.cause?.code, error.errno].filter(Boolean).map(String);
  if (codes.some(code => TRANSIENT_CODES.has(code))) return true;
  if (error.name === 'AbortError' || error.name === 'TimeoutError') return true;
  return error.name === 'TypeError' && /fetch failed|network/i.test(String(error.message ?? ''));
}

export class Gateway {
  constructor(store, createZalo, now = Date.now) {
    this.store = store; this.createZalo = createZalo; this.now = now; this.sessions = new Map();
    for (const owner of store.owners()) this.session(owner);
  }
  session(owner) {
    if (!this.sessions.has(owner)) {
      let saved = null;
      let unreadable = false;
      try { saved = this.store.read(owner); }
      catch {
        // One corrupt file or a wrong key must not crash every other owner's session at startup.
        // Keep the file untouched until the owner explicitly reconnects; log only the owner id.
        unreadable = true;
        console.error(`Session file for owner ${owner} could not be read; starting disconnected.`);
      }
      saved ??= { credentials: null, uid: null, attempts: {}, lastSendAt: 0 };
      this.sessions.set(owner, { saved, status: 'Disconnected', api: null, qrImage: null, generation: 0, unreadable, restoreFailures: 0, restoreRetryAt: 0 });
    }
    return this.sessions.get(owner);
  }
  persist(owner, s) { this.store.write(owner, s.saved); }
  view(s) { return { status: s.status, qrImage: s.qrImage }; }
  async status(owner) {
    const s = this.session(owner);
    if (s.saved.credentials && !s.api && !s.login && this.now() >= (s.restoreRetryAt ?? 0)) {
      s.status = 'Restoring';
      const generation = s.generation;
      s.login = this.createZalo().login(s.saved.credentials).then(api => {
        if (generation !== s.generation) { api.listener.stop(); return; }
        s.restoreFailures = 0; s.restoreRetryAt = 0;
        this.attach(owner, s, api);
      }).catch(error => {
        if (generation !== s.generation) return;
        if (isTransientRestoreError(error)) {
          s.restoreFailures = (s.restoreFailures ?? 0) + 1;
          s.restoreRetryAt = this.now() + Math.min(RESTORE_BACKOFF_MAX_MS, RESTORE_BACKOFF_BASE_MS * 2 ** (s.restoreFailures - 1));
          s.status = 'Restoring';
          return;
        }
        s.restoreFailures = 0; s.restoreRetryAt = 0;
        s.saved.credentials = null; s.status = 'Disconnected'; this.persist(owner, s);
      }).finally(() => { if (generation === s.generation) s.login = null; });
    }
    return this.view(s);
  }
  attach(owner, s, api) {
    const uid = api.getOwnId();
    if ([...this.sessions.entries()].some(([id, other]) => id !== owner && other.saved.uid === uid)) {
      api.listener.stop(); s.saved.credentials = null; s.saved.uid = null; s.status = 'AccountInUse'; this.persist(owner, s); return;
    }
    s.api = api; s.saved.uid = uid; s.status = 'Connecting'; s.qrImage = null;
    this.persist(owner, s);
    api.listener.on('connected', () => { if (s.api === api) s.status = 'Connected'; });
    api.listener.on('disconnected', () => { if (s.api === api) s.status = 'Restoring'; });
    api.listener.on('error', () => { if (s.api === api) s.status = 'Restoring'; });
    api.listener.on('closed', () => {
      if (s.api !== api) return;
      s.api = null; s.saved.credentials = null; s.status = 'Disconnected'; this.persist(owner, s);
    });
    // Replies remain in the owner's normal Zalo app; no message content is stored here.
    api.listener.start({ retryOnClose: true });
  }
  connect(owner) {
    const s = this.session(owner);
    if (s.login || s.api) return this.view(s);
    const generation = ++s.generation;
    s.status = 'Connecting'; s.qrImage = null;
    const promise = this.createZalo().loginQR({}, event => {
      if (generation !== s.generation) { event.actions?.abort(); return; }
      if (event.type === 0) { s.status = 'QrReady'; s.qrImage = `data:image/png;base64,${event.data.image}`; s.abort = event.actions.abort; }
      if (event.type === 2) { s.status = 'AwaitingConfirmation'; s.qrImage = null; }
      if (event.type === 1 || event.type === 3) { s.status = 'Expired'; s.qrImage = null; event.actions.abort(); }
      if (event.type === 4) { s.saved.credentials = event.data; this.persist(owner, s); }
    });
    s.login = promise.then(api => {
      if (generation !== s.generation) { api.listener.stop(); return; }
      this.attach(owner, s, api);
    }).catch(() => { if (generation === s.generation && s.status !== 'Expired') { s.status = 'Disconnected'; s.qrImage = null; } })
      .finally(() => { if (generation === s.generation) { s.login = null; s.abort = null; } });
    return this.view(s);
  }
  disconnect(owner) {
    const s = this.session(owner);
    ++s.generation;
    const api = s.api; s.api = null; s.abort?.(); api?.listener.stop();
    s.saved.credentials = null; s.saved.uid = null; s.status = 'Disconnected'; s.qrImage = null; s.login = null; s.abort = null;
    this.persist(owner, s);
    return this.view(s);
  }
  async send(owner, body) {
    const s = this.session(owner);
    if (!/^[a-f0-9-]{36}$/i.test(body.attemptId) || !/^0[35789]\d{8}$/.test(body.phone) ||
        typeof body.message !== 'string' || body.message.length > 6000) throw new Error('Invalid message');
    const fingerprint = createHash('sha256').update(JSON.stringify(body)).digest('hex');
    const previous = s.saved.attempts[body.attemptId];
    if (previous) return { status: previous.fingerprint === fingerprint ? (previous.status === 'Sending' ? 'Unknown' : previous.status) : 'Unknown' };
    if (s.status !== 'Connected' || !s.api) return { status: 'Disconnected' };
    const hour = new Date(this.now() + 7 * 3600000).getUTCHours();
    if (s.busy || hour < 7 || hour >= 21 || this.now() - s.saved.lastSendAt < 5000) return { status: 'Deferred' };
    s.busy = true;
    try {
      let user;
      try { user = await s.api.findUser(body.phone); }
      catch { return { status: 'Failed' }; }
      if (!user?.uid) return { status: 'Failed' };
      if (s.status !== 'Connected' || !s.api) return { status: 'Disconnected' };
      const currentHour = new Date(this.now() + 7 * 3600000).getUTCHours();
      if (currentHour < 7 || currentHour >= 21) return { status: 'Deferred' };
      s.saved.attempts[body.attemptId] = { status: 'Sending', fingerprint, at: this.now() };
      s.saved.lastSendAt = this.now();
      this.persist(owner, s);
      try {
        const result = await s.api.sendMessage({ msg: body.message }, user.uid, 0);
        const delivered = result?.message?.msgId;
        s.saved.attempts[body.attemptId].status = delivered ? 'Sent' : 'Unknown';
      } catch { s.saved.attempts[body.attemptId].status = 'Unknown'; }
      this.persist(owner, s);
      return { status: s.saved.attempts[body.attemptId].status };
    } finally { s.busy = false; }
  }
}
