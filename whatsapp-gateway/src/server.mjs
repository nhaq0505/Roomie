import http from 'node:http';
import { timingSafeEqual } from 'node:crypto';
import { Gateway } from './gateway.mjs';
import { EncryptedStore } from './store.mjs';
import { createBaileysClient } from './whatsapp.mjs';

const token = process.env.WHATSAPP_GATEWAY_TOKEN;
if (!token || token.length < 32) throw new Error('WHATSAPP_GATEWAY_TOKEN must contain at least 32 characters');
const store = new EncryptedStore(process.env.WHATSAPP_DATA_DIR ?? '.local-data', process.env.WHATSAPP_SESSION_KEY);

let clientPromise = null;
function getClient() {
  if (!clientPromise) {
    clientPromise = (async () => {
      const [baileys, qrcode] = await Promise.all([
        import('@whiskeysockets/baileys').catch(err => {
          if (err?.code === 'ERR_MODULE_NOT_FOUND') return null;
          throw err;
        }),
        import('qrcode').catch(err => {
          if (err?.code === 'ERR_MODULE_NOT_FOUND') return null;
          throw err;
        })
      ]);
      return createBaileysClient(baileys, qrcode);
    })();
    clientPromise.catch(() => {});
  }
  return clientPromise;
}

const gateway = new Gateway(store, () => ({
  login: async (savedCredentials) => (await getClient()).login(savedCredentials),
  loginQR: async (opts, callback) => (await getClient()).loginQR(opts, callback)
}));

const server = http.createServer(async (req, res) => {
  res.setHeader('Content-Type', 'application/json'); res.setHeader('Cache-Control', 'no-store');
  const reply = (status, value) => { res.writeHead(status); res.end(JSON.stringify(value)); };
  const expected = Buffer.from(`Bearer ${token}`); const actual = Buffer.from(req.headers.authorization ?? '');
  if (expected.length !== actual.length || !timingSafeEqual(expected, actual)) return reply(401, { message: 'Unauthorized' });
  const match = /^\/owners\/([a-f0-9-]{36})\/(status|connect|disconnect|send)$/i.exec(req.url ?? '');
  if (!match) return reply(404, { message: 'Not found' });
  const [, owner, action] = match;
  if (req.method !== (action === 'status' ? 'GET' : 'POST')) return reply(405, { message: 'Method not allowed' });
  try {
    if (action === 'status') return reply(200, await gateway.status(owner));
    if (action === 'connect') {
      gateway.connect(owner);
      const deadline = Date.now() + 5000;
      while (Date.now() < deadline) {
        const current = await gateway.status(owner);
        if (current.status !== 'Connecting') return reply(200, current);
        await new Promise(r => setTimeout(r, 50));
      }
      return reply(200, await gateway.status(owner));
    }
    if (action === 'disconnect') return reply(200, gateway.disconnect(owner));
    let body = ''; for await (const chunk of req) { body += chunk; if (Buffer.byteLength(body) > 16000) return reply(413, { message: 'Too large' }); }
    return reply(200, await gateway.send(owner, JSON.parse(body)));
  } catch { return reply(503, { message: 'Gateway operation failed' }); }
});
server.requestTimeout = 45000;
server.listen(Number(process.env.PORT ?? 5091), process.env.HOST ?? '127.0.0.1');
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => { server.close(); process.exit(0); });
