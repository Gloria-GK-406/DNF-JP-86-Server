"""Drive login + SELECT_CHARACTER over the wire and dump every frame the server sends.

The real client is needed to judge whether a batch is ACCEPTED, but not to see what
the batch IS. This records our side of the entry sequence with no GUI at all, so the
byte-level comparison against the reference capture can proceed while the client is
unavailable.

usage: entryprobe.py [port] [account] [pwhash] [rosterIndex] [out.txt]
"""
import socket, struct, sys, time

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


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 20011
    account = sys.argv[2] if len(sys.argv) > 2 else '10038'
    pwhash = sys.argv[3] if len(sys.argv) > 3 else 'de509f65e9ccaae621cb7278fc2b8e6c'
    index = int(sys.argv[4]) if len(sys.argv) > 4 else 0
    out = open(sys.argv[5] if len(sys.argv) > 5 else 'entryprobe.txt', 'w', encoding='utf-8')

    n = [0]

    def log(d, cls, typ, body):
        fam = 'CMD' if cls == 0x01 else ('NOTI' if cls == 0x00 else 'cls%02X' % cls)
        out.write('#%-4d %s %-4s 0x%04X (%dB)  %s\n'
                  % (n[0], d, fam, typ, len(body), body.hex(' ')))
        n[0] += 1

    c = Conn('127.0.0.1', port)
    g = c.recv_frame()
    if g:
        log('S->C', *g)

    c.send(0x01, 0x0001, dstr(account) + dstr(pwhash))
    log('C->S', 0x01, 0x0001, dstr(account) + dstr(pwhash))
    deadline = time.time() + 6
    while time.time() < deadline:
        r = c.recv_frame(timeout=2)
        if not r:
            break
        log('S->C', *r)

    body = struct.pack('<H', index)
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
        log('S->C', *r)
        quiet = time.time() + 8

    out.close()
    c.close()
    print('frames: %d' % n[0])


if __name__ == "__main__":
    main()
