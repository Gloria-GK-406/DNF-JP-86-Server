"""A minimal wire client for this server's Game endpoint.

Exists to set up state without driving the real client's UI: the character the
entry sequence needs has to be made by the server itself, because creation
writes six tables and a hand-written characters row is rejected at select with
"incomplete required profile state".

Speaks the A21 framing: 14 bytes outbound, 15 inbound.
"""
import argparse, time
from entryprobe import Conn, dstr, login_payload, require_success

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, required=True)
    parser.add_argument('--name', default='Probe')
    parser.add_argument('--job', type=int, default=0)
    args = parser.parse_args()
    payload = login_payload()
    host, port, name, job = '127.0.0.1', args.port, args.name, args.job

    c = Conn(host, port)
    got = c.recv_frame()
    print('notice: cls=0x%02X type=0x%04X body=%d' % (got[0], got[1], len(got[2])) if got else 'notice: none')

    c.send(0x01, 0x0001, payload)
    r = c.recv_frame()
    print('login answer: cls=0x%02X type=0x%04X body=%d' % (r[0], r[1], len(r[2])) if r else 'login: no answer')

    require_success([r] if r else [], 0x0001)
    created = False
    body = bytes([job]) + dstr(name) + b'\x00\x00\x00\x00'
    c.send(0x01, 0x0005, body)
    print('create sent: job=%d name=%s body=%dB' % (job, name, len(body)))

    deadline = time.time() + 8
    while time.time() < deadline:
        r = c.recv_frame(timeout=3)
        if not r: break
        cls, typ, b = r
        print('  <- cls=0x%02X type=0x%04X body=%d  %s' % (cls, typ, len(b), b[:24].hex(' ')))
        if cls == 0x01 and typ == 0x0005:
            require_success([r], 0x0005)
            created = True
            print('  CREATE_CHARACTER answered')
            break
    c.close()
    if not created:
        raise RuntimeError("CREATE_CHARACTER did not succeed")

if __name__ == "__main__":
    main()
