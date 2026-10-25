import { EventEmitter } from 'node:events';

export function createBaileysClient(baileys, qrcode) {
  if (!baileys || !qrcode) throw new Error('@whiskeysockets/baileys and qrcode are required');
  const { default: makeWASocket, initAuthCreds, BufferJSON, DisconnectReason } = baileys;

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

  function wrapSocket(sock, listener) {
    return {
      listener,
      getOwnId: () => normalizeOwnId(sock.user?.id),
      findUser: async (phone) => {
        const jid = toWhatsAppJid(phone);
        const matches = await sock.onWhatsApp(jid);
        const first = Array.isArray(matches) ? matches[0] : null;
        return first?.exists && first?.jid ? { uid: first.jid } : null;
      },
      sendMessage: async (payload, uid) => {
        const sent = await sock.sendMessage(uid, { text: payload.msg });
        return { message: { msgId: sent?.key?.id ?? null } };
      }
    };
  }

  return {
    login: (savedCredentials) => new Promise((resolve, reject) => {
      const { state } = createAuthState(savedCredentials);
      const listener = new EventEmitter();
      let settled = false;
      const sock = makeWASocket({ auth: state, printQRInTerminal: false });
      listener.start = () => listener.emit('connected');
      listener.stop = () => { try { sock.end(); } catch { } };
      sock.ev.on('connection.update', (update) => {
        if (update.connection === 'open') {
          if (!settled) { settled = true; resolve(wrapSocket(sock, listener)); }
          else listener.emit('connected');
        } else if (update.connection === 'close') {
          const code = update.lastDisconnect?.error?.output?.statusCode;
          if (!settled) { settled = true; reject(new Error('Session restore failed')); return; }
          if (code === DisconnectReason?.loggedOut || code === 401) listener.emit('closed');
          else listener.emit('disconnected');
        }
      });
    }),
    loginQR: (_, callback) => new Promise((resolve, reject) => {
      const { state, snapshot } = createAuthState(null);
      const listener = new EventEmitter();
      let settled = false;
      const sock = makeWASocket({ auth: state, printQRInTerminal: false });
      const abort = () => { try { sock.end(); } catch { } };
      listener.start = () => listener.emit('connected');
      listener.stop = abort;
      sock.ev.on('creds.update', () => {
        callback({ type: 4, data: snapshot(), actions: { abort } });
      });
      sock.ev.on('connection.update', async (update) => {
        if (update.qr) {
          try {
            const dataUrl = await qrcode.toDataURL(update.qr);
            const image = dataUrl.replace(/^data:image\/png;base64,/, '');
            callback({ type: 0, data: { image }, actions: { abort } });
          } catch {
            callback({ type: 3, actions: { abort } });
          }
        }
        if (update.connection === 'open') {
          callback({ type: 4, data: snapshot(), actions: { abort } });
          if (!settled) { settled = true; resolve(wrapSocket(sock, listener)); }
          else listener.emit('connected');
        } else if (update.connection === 'close') {
          const code = update.lastDisconnect?.error?.output?.statusCode;
          if (!settled) {
            settled = true;
            callback({ type: 1, actions: { abort } });
            reject(new Error('QR login closed'));
            return;
          }
          if (code === DisconnectReason?.loggedOut || code === 401) listener.emit('closed');
          else listener.emit('disconnected');
        }
      });
    })
  };
}
