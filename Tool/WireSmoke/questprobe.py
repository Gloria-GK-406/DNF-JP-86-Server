"""Accept a quest on the wire and check the answer against the reference server's.

The unit tests prove the codec and the archive reading. This proves the pipeline: that the
type is dispatched, that a character is found for the connection, that the row is written,
and that the two frames the reference sends come back in its order.

The bodies are the ones this server logged on 2026-08-27 while answering none of them.

usage: questprobe.py --port <port> [--index 0]
Set SMOKE_MID and SMOKE_TICKET from a fresh launcher login.
"""
import argparse
import struct
import sys

from entryprobe import Conn, login_payload, require_success

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port', type=int, required=True)
parser.add_argument('--index', type=int, default=0)
args = parser.parse_args()
payload = login_payload()

# What the reference server answered for quest 1016: accepted, trigger 1, no items.
REFERENCE_ACK = bytes.fromhex("01F8030100000000")

LIVE_BODIES = [
    ("1F00F803", 1016),
    ("1F006500", 101),
]


def drain(conn, seconds=8):
    frames = []
    while True:
        frame = conn.recv_frame(timeout=seconds)
        if frame is None:
            return frames
        frames.append(frame)
        seconds = 2


c = Conn("127.0.0.1", args.port)
c.recv_frame()
c.send(0x01, 0x0001, payload)
require_success(drain(c, 4), 0x0001)

c.send(0x01, 0x0004, struct.pack("<H", args.index))
entry = drain(c, 40)
require_success(entry, 0x0004)
print("entry batch: %d frames" % len(entry))

failed = False
for body, questId in LIVE_BODIES:
    c.send(0x01, 0x001F, bytes.fromhex(body))
    answered = drain(c, 4)
    print("\naccept %d  ->  %d frame(s)" % (questId, len(answered)))
    for selector, type_, payload in answered:
        print("   %s 0x%04X (%dB) %s"
              % ("CMD " if selector == 0x01 else "NOTI", type_, len(payload), payload.hex(" ")))

    ack = next((f for f in answered if f[0] == 0x01 and f[1] == 0x001F), None)
    if ack is None:
        print("   *** no acknowledgement"); failed = True
    elif questId == 1016 and ack[2] != REFERENCE_ACK:
        print("   *** differs from the reference: %s" % REFERENCE_ACK.hex(" ")); failed = True
    elif not ack[2] or ack[2][0] != 0x01:
        print("   *** refused, reason %d" % (ack[2][1] if len(ack[2]) > 1 else -1)); failed = True

    if not any(f[0] == 0x00 and f[1] == 0x0015 for f in answered):
        print("   *** the offer list did not follow"); failed = True

# The same quest again: the reference refuses an active quest with 18, and so must this.
c.send(0x01, 0x001F, bytes.fromhex(LIVE_BODIES[0][0]))
again = drain(c, 4)
repeat = next((f for f in again if f[0] == 0x01 and f[1] == 0x001F), None)
print("\naccept 1016 a second time  ->  %s"
      % ("nothing" if repeat is None else repeat[2].hex(" ")))
if repeat is None or repeat[2] != bytes([0x00, 18]):
    print("   *** expected a refusal of 00 12"); failed = True

c.close()
print("\nRESULT: %s" % ("FAILED" if failed else "matches the reference"))
sys.exit(1 if failed else 0)
