import { EventEmitter } from 'node:events';

const FALLBACK_WA_VERSION = [2, 3000, 1051697602];
const noop = () => {};
const silentLogger = {
  level: 'silent',
  info: noop,
  debug: noop,
  trace: noop,
  warn: noop,
  error: noop,
  fatal: noop,
  child: () => silentLogger
};

let cachedVersion = null;
let cachedVersionAt = 0;
let inflightVersion = null;

export function createBaileysClient(baileys, qrcode) {
  if (!baileys || !qrcode) throw new Error('@whiskeysockets/baileys and qrcode are required');
  const root = (baileys?.default && typeof baileys.default === 'object') ? baileys.default : baileys;
  const makeWASocket = typeof baileys?.makeWASocket === 'function'
    ? baileys.makeWASocket
    : typeof root?.makeWASocket === 'function'
      ? root.makeWASocket
      : typeof root?.default === 'function'
        ? root.default
        : typeof baileys?.default === 'function'
          ? baileys.default
          : null;
  const initAuthCreds = baileys?.initAuthCreds ?? root?.initAuthCreds;
  const BufferJSON = baileys?.BufferJSON ?? root?.BufferJSON;
  const DisconnectReason = baileys?.DisconnectReason ?? root?.DisconnectReason;
  const fetchLatestWaWebVersion = baileys?.fetchLatestWaWebVersion ?? root?.fetchLatestWaWebVersion;
  const fetchLatestBaileysVersion = baileys?.fetchLatestBaileysVersion ?? root?.fetchLatestBaileysVersion;
  const toDataURL = typeof qrcode?.toDataURL === 'function'
    ? qrcode.toDataURL.bind(qrcode)
    : typeof qrcode?.default?.toDataURL === 'function'
      ? qrcode.default.toDataURL.bind(qrcode.default)
      : null;

  if (typeof makeWASocket !== 'function' || typeof initAuthCreds !== 'function' || !BufferJSON || typeof toDataURL !== 'function') {
    throw new Error('@whiskeysockets/baileys and qrcode are required');
  }

  const hasRemoteVersionFetcher = typeof fetchLatestWaWebVersion === 'function' || typeof fetchLatestBaileysVersion === 'function';

  async function resolveVersion() {
    if (!hasRemoteVersionFetcher) return FALLBACK_WA_VERSION;
    if (cachedVersion && Date.now() - cachedVersionAt < 6 * 3600 * 1000) return cachedVersion;
    if (inflightVersion) return inflightVersion;
    inflightVersion = (async () => {
      if (typeof fetchLatestWaWebVersion === 'function') {
        try {
          const wa = await fetchLatestWaWebVersion({ timeout: 3500 });
          if (wa?.isLatest && Array.isArray(wa.version)) {
            cachedVersion = wa.version;
            cachedVersionAt = Date.now();
            return cachedVersion;
          }
        } catch { }
      }
      if (typeof fetchLatestBaileysVersion === 'function') {
        try {
          const b = await fetchLatestBaileysVersion({ timeout: 3500 });
          if (b?.isLatest && Array.isArray(b.version)) {
            cachedVersion = b.version;
            cachedVersionAt = Date.now();
            return cachedVersion;
          }
        } catch { }
      }
      return cachedVersion ?? FALLBACK_WA_VERSION;
    })().finally(() => { inflightVersion = null; });
    return inflightVersion;
  }

  if (hasRemoteVersionFetcher && !cachedVersion && !inflightVersion) {
    void resolveVersion();
  }

  function createAuthState(initial) {
    const parsed = initial ? JSON.parse(JSON.stringify(initial), BufferJSON.reviver) : null;
    const creds = parsed?.creds ?? initAuthCreds();
    const keys = parsed?.keys ?? {};
    return {
      state: {
        creds,
        keys: {
          get: async (type, ids) => {
            const bucket = keys[type] ?? {};
            return Object.fromEntries(ids.map(id => [id, bucket[id] ?? null]));
          },
          set: async (data) => {
            for (const [category, entries] of Object.entries(data)) {
              keys[category] ??= {};
              for (const [id, value] of Object.entries(entries ?? {})) {
                if (value) keys[category][id] = value;
                else delete keys[category][id];
              }
            }
          }
        }
      },
      snapshot: () => JSON.parse(JSON.stringify({ creds, keys }, BufferJSON.replacer))
    };
  }

  function normalizeOwnId(rawId) {
    return String(rawId ?? '').replace(/:\d+@/, '@');
  }

  function toWhatsAppJid(phone) {
    const digits = String(phone ?? '').replace(/\D/g, '');
    const intl = digits.startsWith('0') ? `84${digits.slice(1)}` : digits;
    return `${intl}@s.whatsapp.net`;
  }

  function wrapSocket(getSocket, listener, state) {
    return {
      listener,
      getOwnId: () => normalizeOwnId(getSocket()?.user?.id ?? state?.creds?.me?.id),
      findUser: async (phone) => {
        const sock = getSocket();
        if (!sock) return null;
        const jid = toWhatsAppJid(phone);
        const matches = await sock.onWhatsApp(jid);
        const first = Array.isArray(matches) ? matches[0] : null;
        return first?.exists && first?.jid ? { uid: first.jid } : null;
      },
      sendMessage: async (payload, uid) => {
        const sock = getSocket();
        if (!sock) throw new Error('WhatsApp socket is not connected');
        const sent = await sock.sendMessage(uid, { text: payload.msg });
        return { message: { msgId: sent?.key?.id ?? null } };
      }
    };
  }

  return {
    login: (savedCredentials) => new Promise((resolve, reject) => {
      const { state, snapshot } = createAuthState(savedCredentials);
      const listener = new EventEmitter();
      let settled = false;
      let aborted = false;
      let currentSock = null;
      let reconnectTimer = null;

      const stop = () => {
        aborted = true;
        if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
        try { currentSock?.end(); } catch { }
      };
      listener.start = () => listener.emit('connected');
      listener.stop = stop;

      const openSocket = (version) => {
        if (aborted) return;
        const sock = makeWASocket({
          ...(version ? { version } : {}),
          auth: state,
          printQRInTerminal: false,
          logger: silentLogger
        });
        currentSock = sock;
        sock.ev.on('creds.update', (update) => {
          if (aborted) return;
          if (update && typeof update === 'object') Object.assign(state.creds, update);
          if (settled) listener.emit('credentials', snapshot());
        });
        sock.ev.on('connection.update', (update) => {
          if (aborted || currentSock !== sock) return;
          if (update.connection === 'open') {
            if (!settled) { settled = true; resolve(wrapSocket(() => currentSock, listener, state)); }
            else { listener.emit('credentials', snapshot()); listener.emit('connected'); }
          } else if (update.connection === 'close') {
            const code = update.lastDisconnect?.error?.output?.statusCode;
            const loggedOut = code === (DisconnectReason?.loggedOut ?? 401) || code === 401;
            const restartRequired = code === (DisconnectReason?.restartRequired ?? 515) || code === 515;
            if (!settled) {
              if (!loggedOut && restartRequired) {
                try { sock.end(); } catch { }
                openSocket(version);
                return;
              }
              settled = true;
              reject(new Error('Session restore failed'));
              return;
            }
            if (loggedOut) {
              listener.emit('closed');
            } else {
              listener.emit('disconnected');
              if (hasRemoteVersionFetcher && !aborted) {
                if (reconnectTimer) clearTimeout(reconnectTimer);
                reconnectTimer = setTimeout(() => {
                  reconnectTimer = null;
                  openSocket(version);
                }, 2000);
              }
            }
          }
        });
      };

      if (!hasRemoteVersionFetcher || cachedVersion) {
        openSocket(cachedVersion ?? undefined);
      } else {
        resolveVersion().then(version => openSocket(version)).catch(() => openSocket(FALLBACK_WA_VERSION));
      }
    }),
    loginQR: (_, callback) => new Promise((resolve, reject) => {
      const { state, snapshot } = createAuthState(null);
      const listener = new EventEmitter();
      let settled = false;
      let aborted = false;
      let paired = false;
      let currentSock = null;
      let reconnectTimer = null;

      const abort = () => {
        aborted = true;
        if (reconnectTimer) { clearTimeout(reconnectTimer); reconnectTimer = null; }
        try { currentSock?.end(); } catch { }
      };
      listener.start = () => listener.emit('connected');
      listener.stop = abort;

      const openSocket = (version) => {
        if (aborted) return;
        const sock = makeWASocket({
          ...(version ? { version } : {}),
          auth: state,
          printQRInTerminal: false,
          logger: silentLogger
        });
        currentSock = sock;
        sock.ev.on('creds.update', (update) => {
          if (aborted) return;
          if (update && typeof update === 'object') Object.assign(state.creds, update);
          if (state.creds?.me?.id) paired = true;
          const snap = snapshot();
          callback({ type: 4, data: snap, actions: { abort } });
          if (settled) listener.emit('credentials', snap);
        });
        sock.ev.on('connection.update', async (update) => {
          if (aborted || currentSock !== sock) return;
          if (update.isNewLogin) {
            paired = true;
            callback({ type: 4, data: snapshot(), actions: { abort } });
            callback({ type: 2, actions: { abort } });
          }
          if (update.qr) {
            try {
              const dataUrl = await toDataURL(update.qr);
              if (aborted || currentSock !== sock) return;
              const image = dataUrl.replace(/^data:image\/png;base64,/, '');
              callback({ type: 0, data: { image }, actions: { abort } });
            } catch {
              if (!aborted && currentSock === sock) callback({ type: 3, actions: { abort } });
            }
          }
          if (update.connection === 'open') {
            paired = true;
            const snap = snapshot();
            callback({ type: 4, data: snap, actions: { abort } });
            if (!settled) {
              settled = true;
              resolve(wrapSocket(() => currentSock, listener, state));
            } else {
              listener.emit('credentials', snap);
              listener.emit('connected');
            }
          } else if (update.connection === 'close') {
            const code = update.lastDisconnect?.error?.output?.statusCode;
            const loggedOut = code === (DisconnectReason?.loggedOut ?? 401) || code === 401;
            const restartRequired = code === (DisconnectReason?.restartRequired ?? 515) || code === 515;
            if (!settled) {
              if (!loggedOut && (restartRequired || paired || Boolean(state.creds?.me?.id))) {
                try { sock.end(); } catch { }
                openSocket(version);
                return;
              }
              settled = true;
              callback({ type: 1, actions: { abort } });
              reject(new Error('QR login closed'));
              return;
            }
            if (loggedOut) {
              listener.emit('closed');
            } else {
              listener.emit('disconnected');
              if (hasRemoteVersionFetcher && !aborted) {
                if (reconnectTimer) clearTimeout(reconnectTimer);
                reconnectTimer = setTimeout(() => {
                  reconnectTimer = null;
                  openSocket(version);
                }, 2000);
              }
            }
          }
        });
      };

      if (!hasRemoteVersionFetcher || cachedVersion) {
        openSocket(cachedVersion ?? undefined);
      } else {
        resolveVersion().then(version => openSocket(version)).catch(() => openSocket(FALLBACK_WA_VERSION));
      }
    })
  };
}
