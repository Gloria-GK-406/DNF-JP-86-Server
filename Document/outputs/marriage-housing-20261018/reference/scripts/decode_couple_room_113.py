"""Decode the ServerS4A21 113-byte COUPLE_ROOM (NOTI 0x03D8) replay constant.

Source: E:/Project/DNF/ServerS4A21 @ c20dba5f, Server/DfoServer/Network/Builders/Init/WeddingReplayBodyBuilders.cs:46-47
Shape (A21 ClientStatic): u32 count + count*(u16 slot + u32 itemId) + u32 + u32 + u32 + i8
"""
import datetime
import struct

HEX = ("1000000000006EA19800010071A19800190072A19800140025A198000D0005A1980007001BA1980004000AA1980003005CA19800130004A1980009001DA1980010000FA19800160010A19800060062A198000A0065A19800150004A19800060021A1980003000000EE3005691E00000001")
b = bytes.fromhex(HEX)
print("length", len(b))
n = struct.unpack_from("<I", b, 0)[0]
print("count", n)
off = 4
for i in range(n):
    slot, item = struct.unpack_from("<HI", b, off)
    print(f"entry {i:2d} off {off:3d} raw {b[off:off+6].hex()} slot {slot:2d} item {item}")
    off += 6
t1, t2, t3, t4 = struct.unpack_from("<IIIb", b, off)
print(f"tail off {off} raw {b[off:].hex()}")
print(f"  u32 {t1}; u32 {t2} (0x{t2:08X}, as unix time UTC {datetime.datetime.fromtimestamp(t2, datetime.timezone.utc)}); u32 {t3}; i8 {t4}")
assert off + 13 == len(b)
