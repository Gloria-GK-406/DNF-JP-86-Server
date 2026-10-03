"""Drive login + SELECT_CHARACTER over the wire and dump every frame the server sends.

The real client is needed to judge whether a batch is ACCEPTED, but not to see what
the batch IS. This records our side of the entry sequence with no GUI at all, so the
byte-level comparison against the reference capture can proceed while the client is
unavailable.

usage: entryprobe.py --port <port> --out <capture.txt> [--index 0]
Set SMOKE_MID and SMOKE_TICKET from the launcher login response.
"""
import argparse, os, re, socket, struct, sys, time

OUT_HEADER = 14
IN_HEADER = 15


def frame(selector, type_, body=b''):
    total = OUT_HEADER + len(body)
    return (bytes([selector]) + struct.pack('<H', type_) + struct.pack('<I', total)
            + b'\x00\x00\x00\x00' + b'\x00' + b'\x00\x00' + body)


def dstr(s):
    b = s.encode('ascii') if isinstance(s, str) else s
    return struct.pack('<i', len(b)) + b


class Conn:
    def __init__(self, host, port):
        self.s = socket.create_connection((host, port), timeout=15)
        self.buf = b''

    def send(self, selector, type_, body=b''):
        self.s.sendall(frame(selector, type_, body))

    def recv_frame(self, timeout=10):
        self.s.settimeout(timeout)
        while True:
            if len(self.buf) >= 7:
                total = struct.unpack_from('<I', self.buf, 3)[0]
                if not IN_HEADER <= total <= 1024 * 1024:
                    raise ValueError(f'invalid server frame length: {total}')
                if len(self.buf) >= total:
                    f = self.buf[:total]
                    self.buf = self.buf[total:]
                    return f[0], struct.unpack_from('<H', f, 1)[0], f[IN_HEADER:]
            try:
                chunk = self.s.recv(65536)
            except socket.timeout:
                return None
            if not chunk:
                return None
            self.buf += chunk

    def close(self):
        self.s.close()


def login_payload():
    mid, ticket = os.environ.get('SMOKE_MID', ''), os.environ.get('SMOKE_TICKET', '')
    if not re.fullmatch(r'[0-9]{6}', mid) or not re.fullmatch(r'[0-9A-F]{32}', ticket):
        raise ValueError('Set SMOKE_MID (6 digits) and SMOKE_TICKET (32 uppercase hex) from a fresh launcher login.')
    return dstr(mid) + dstr(ticket)


def require_success(frames, opcode):
    answer = next((f for f in frames if f[0] == 1 and f[1] == opcode), None)
    if answer is None or not answer[2] or answer[2][0] != 1:
        raise RuntimeError(f'CMD 0x{opcode:04X} did not succeed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--index', type=int, default=0)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    payload = login_payload()
    out = open(args.out, 'w', encoding='utf-8')

    n = [0]

    def log(d, cls, typ, body):
        fam = 'CMD' if cls == 0x01 else ('NOTI' if cls == 0x00 else 'cls%02X' % cls)
        out.write('#%-4d %s %-4s 0x%04X (%dB)  %s\n'
                  % (n[0], d, fam, typ, len(body), body.hex(' ')))
        n[0] += 1

    c = Conn('127.0.0.1', args.port)
    g = c.recv_frame()
    if g:
        log('S->C', *g)

    c.send(0x01, 0x0001, payload)
    # Ticket credentials must not enter the archived frame dump.
    responses = []
    deadline = time.time() + 6
    while time.time() < deadline:
        r = c.recv_frame(timeout=2)
        if not r:
            break
        responses.append(r)
        log('S->C', *r)

    require_success(responses, 0x0001)
    responses = []
    body = struct.pack('<H', args.index)
    c.send(0x01, 0x0004, body)
    log('C->S', 0x01, 0x0004, body)

    # The first prepare after a cold start reads the PVF archive and has been seen to take
    # four seconds. A short timeout here closes the socket mid-batch, and the server then
    # fails the whole select with "writing is not allowed after writer was completed" --
    # which looks like a server defect and is not one.
    quiet = time.time() + 40
    while time.time() < quiet:
        r = c.recv_frame(timeout=30)
        if not r:
            break
        responses.append(r)
        log('S->C', *r)
        quiet = time.time() + 8

    require_success(responses, 0x0004)
    out.close()
    c.close()
    print('frames: %d' % n[0])


if __name__ == "__main__":
    main()
