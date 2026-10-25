import fs from 'node:fs';
import path from 'node:path';
import { randomBytes, createCipheriv, createDecipheriv } from 'node:crypto';
export class EncryptedStore {
  constructor(directory, key) {
    this.directory = directory;
    this.key = Buffer.from(key ?? '', 'base64');
    if (this.key.length !== 32) throw new Error('WHATSAPP_SESSION_KEY must be 32 bytes encoded as base64');
    fs.mkdirSync(directory, { recursive: true, mode: 0o700 });
  }
  file(owner) {
    if (!/^[a-f0-9-]{36}$/i.test(owner)) throw new Error('Invalid owner');
    return path.join(this.directory, `${owner}.enc`);
  }
  owners() { return fs.readdirSync(this.directory).filter(n => /^[a-f0-9-]{36}\.enc$/i.test(n)).map(n => n.slice(0, -4)); }
  read(owner) {
    const file = this.file(owner);
    if (!fs.existsSync(file)) return null;
    const data = fs.readFileSync(file);
    const cipher = createDecipheriv('aes-256-gcm', this.key, data.subarray(0, 12));
    cipher.setAuthTag(data.subarray(12, 28));
    return JSON.parse(Buffer.concat([cipher.update(data.subarray(28)), cipher.final()]).toString());
  }
  write(owner, value) {
    const file = this.file(owner);
    const iv = randomBytes(12);
    const cipher = createCipheriv('aes-256-gcm', this.key, iv);
    const body = Buffer.concat([cipher.update(JSON.stringify(value)), cipher.final()]);
    fs.writeFileSync(file + '.tmp', Buffer.concat([iv, cipher.getAuthTag(), body]), { mode: 0o600 });
    // Flush before returning: a crash must not turn a submitted message back into an unsent attempt.
    const fd = fs.openSync(file + '.tmp', 'r+');
    try { fs.fsyncSync(fd); } finally { fs.closeSync(fd); }
    fs.renameSync(file + '.tmp', file);
    if (process.platform !== 'win32') {
      const dir = fs.openSync(this.directory, 'r');
      try { fs.fsyncSync(dir); } finally { fs.closeSync(dir); }
    }
  }
}
