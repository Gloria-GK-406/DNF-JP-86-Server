"""A minimal wire client for this server's Game endpoint.

Exists to set up state without driving the real client's UI: the character the
entry sequence needs has to be made by the server itself, because creation
writes six tables and a hand-written characters row is rejected at select with
"incomplete required profile state".

Speaks the A21 framing: 14 bytes outbound, 15 inbound.
"""
import socket, struct, sys, time

OUT_HEADER = 14   # what this client sends
IN_HEADER = 15    # what the server sends

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
                    f = self.buf[:total]; self.buf = self.buf[total:]
                    return f[0], struct.unpack_from('<H', f, 1)[0], f[IN_HEADER:]
            try:
                chunk = self.s.recv(65536)
            except socket.timeout:
                return None
            if not chunk:
                return None
            self.buf += chunk
    def close(self):
        try: self.s.close()
        except Exception: pass

def main():
    host, port = '127.0.0.1', int(sys.argv[1]) if len(sys.argv) > 1 else 10011
    account = sys.argv[2] if len(sys.argv) > 2 else '10038'
    pwhash  = sys.argv[3] if len(sys.argv) > 3 else 'de509f65e9ccaae621cb7278fc2b8e6c'
    name    = sys.argv[4] if len(sys.argv) > 4 else 'Probe'
    job     = int(sys.argv[5]) if len(sys.argv) > 5 else 0

    c = Conn(host, port)
    got = c.recv_frame()
    print('notice: cls=0x%02X type=0x%04X body=%d' % (got[0], got[1], len(got[2])) if got else 'notice: none')

    c.send(0x01, 0x0001, dstr(account) + dstr(pwhash))
    r = c.recv_frame()
    print('login answer: cls=0x%02X type=0x%04X body=%d' % (r[0], r[1], len(r[2])) if r else 'login: no answer')

    body = bytes([job]) + dstr(name) + b'\x00\x00\x00\x00'
    c.send(0x01, 0x0005, body)
    print('create sent: job=%d name=%s body=%dB' % (job, name, len(body)))

    deadline = time.time() + 8
    while time.time() < deadline:
        r = c.recv_frame(timeout=3)
        if not r: break
        cls, typ, b = r
        print('  <- cls=0x%02X type=0x%04X body=%d  %s' % (cls, typ, len(b), b[:24].hex(' ')))
        if typ == 0x0005:
            print('  CREATE_CHARACTER answered')
            break
    c.close()

main()
