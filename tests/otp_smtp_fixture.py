"""Loopback-only SMTP sink for disposable OTP QA; never relays email.

Usage: python3 tests/otp_smtp_fixture.py --outbox /tmp/rehouse-otp-qa.X/outbox --port 5098
Create an empty <outbox>/.fail file to reject delivery in a failure-path test.
"""
import argparse
import email
from email import policy
import json
import os
from pathlib import Path
import re
import socketserver
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--outbox', required=True)
    parser.add_argument('--port', type=int, default=5098)
    args = parser.parse_args()
    outbox = Path(args.outbox).resolve()
    if not str(outbox).startswith(str(Path('/tmp').resolve()) + os.sep):
        parser.error('outbox must be a disposable directory under /tmp')
    outbox.mkdir(mode=0o700, parents=True, exist_ok=True)

    class Handler(socketserver.StreamRequestHandler):
        def reply(self, value):
            self.wfile.write(value.encode('ascii') + b'\r\n')
            self.wfile.flush()

        def handle(self):
            self.reply('220 localhost disposable SMTP sink')
            recipient = ''
            while True:
                line = self.rfile.readline(16384)
                if not line:
                    return
                command = line.decode('ascii', errors='replace').strip()
                verb = command.split(' ', 1)[0].upper()
                if verb in ('EHLO', 'HELO'):
                    self.reply('250-localhost')
                    self.reply('250 8BITMIME')
                elif verb == 'RCPT':
                    recipient = command.split('<', 1)[-1].split('>', 1)[0]
                    self.reply('250 OK')
                elif verb == 'DATA':
                    if (outbox / '.fail').exists():
                        self.reply('451 Synthetic delivery failure')
                        continue
                    self.reply('354 End with a single dot')
                    lines = []
                    while True:
                        part = self.rfile.readline(65536)
                        if not part:
                            return
                        if part in (b'.\r\n', b'.\n'):
                            break
                        lines.append(part[1:] if part.startswith(b'..') else part)
                    message = email.message_from_bytes(b''.join(lines), policy=policy.default)
                    body = message.get_body(preferencelist=('plain',)).get_content()
                    match = re.search(r'\b([0-9]{6})\b', body)
                    notification = str(message['Subject']) == 'Mật khẩu Roomie đã được thay đổi'
                    if not match and not notification:
                        self.reply('554 Missing synthetic OTP')
                        continue
                    path = outbox / (uuid.uuid4().hex + '.json')
                    temporary = path.with_suffix('.tmp')
                    with temporary.open('x') as target:
                        os.chmod(temporary, 0o600)
                        json.dump({'email': recipient, 'code': match.group(1) if match else None, 'subject': str(message['Subject'])}, target)
                    temporary.replace(path)
                    self.reply('250 Accepted by local fixture')
                elif verb == 'QUIT':
                    self.reply('221 Bye')
                    return
                elif verb in ('MAIL', 'RSET', 'NOOP'):
                    self.reply('250 OK')
                else:
                    self.reply('502 Unsupported fixture command')

    class Server(socketserver.ThreadingTCPServer):
        allow_reuse_address = True
        daemon_threads = True

    with Server(('127.0.0.1', args.port), Handler) as server:
        print('Disposable SMTP sink ready on loopback', flush=True)
        server.serve_forever()


if __name__ == '__main__':
    main()
