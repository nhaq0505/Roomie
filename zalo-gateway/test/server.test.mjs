import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import net from 'node:net';
import { setTimeout as delay } from 'node:timers/promises';

test('real HTTP gateway protects session endpoints and exposes no credentials', async t => {
  const reservation = net.createServer(); await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
  const port = reservation.address().port; await new Promise(resolve => reservation.close(resolve));
  const dir = mkdtempSync(path.join(tmpdir(), 'roomie-zalo-http-'));
  const token = randomBytes(32).toString('hex');
  const process = spawn(globalThis.process.execPath, ['src/server.mjs'], { stdio: 'ignore', env: { ...globalThis.process.env,
    PORT: String(port), HOST: '127.0.0.1', ZALO_DATA_DIR: dir, ZALO_GATEWAY_TOKEN: token, ZALO_SESSION_KEY: randomBytes(32).toString('base64') } });
  t.after(async () => { if (process.exitCode === null) { process.kill(); await new Promise(resolve => process.once('exit', resolve)); } rmSync(dir, { recursive: true, force: true }); });
  const url = `http://127.0.0.1:${port}/owners/11111111-1111-1111-1111-111111111111/status`;
  let response;
  for (let i = 0; i < 50; i++) { try { response = await fetch(url); break; } catch { await delay(100); } }
  assert.equal(response?.status, 401);
  assert.equal((await fetch(url, { headers: { authorization: 'Bearer wrong' } })).status, 401);
  const headers = { authorization: `Bearer ${token}` };
  response = await fetch(url, { headers });
  assert.equal(response.status, 200); assert.deepEqual(await response.json(), { status: 'Disconnected', qrImage: null });
  assert.match(response.headers.get('cache-control'), /no-store/);
  assert.equal((await fetch(url, { method: 'POST', headers })).status, 405);
  assert.equal((await fetch(url.replace('/status', '/unknown'), { headers })).status, 404);
  const disconnectRes = await fetch(url.replace('/status', '/disconnect'), { method: 'POST', headers });
  assert.equal(disconnectRes.status, 200);
  assert.deepEqual(await disconnectRes.json(), { status: 'Disconnected', qrImage: null });
  const sendUrl = url.replace('/status', '/send');
  assert.equal((await fetch(sendUrl, { method: 'POST', headers, body: JSON.stringify({ attemptId: 'bad', phone: 'bad', message: 'hi' }) })).status, 503);
  assert.equal((await fetch(sendUrl, { method: 'POST', headers, body: 'x'.repeat(16005) })).status, 413);
});
