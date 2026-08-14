#!/usr/bin/env python3
"""Strip an Authenticode signature from a PE so editbin /REBASE will accept it.

Zeroes the Security data directory and truncates the appended certificate (which
sits at the end of the file). editbin refuses to rebase a signed image
(LNK1282); this removes the signature in place. Use only on lab-local DLL copies.
"""
import struct
import sys


def strip(path):
    d = bytearray(open(path, "rb").read())
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    opt = pe + 24
    if struct.unpack_from("<I", d, opt + 92)[0] < 5:
        print(f"{path}: no security directory")
        return
    off = opt + 96 + 4 * 8
    cert_off, cert_sz = struct.unpack_from("<II", d, off)
    if not (cert_off and cert_sz):
        print(f"{path}: already unsigned")
        return
    struct.pack_into("<II", d, off, 0, 0)
    if cert_off + cert_sz == len(d):
        d = d[:cert_off]
        note = f"truncated cert -> {len(d):#x}"
    else:
        note = "cert not at EOF; only zeroed directory"
    open(path, "wb").write(d)
    print(f"{path}: signature stripped ({note})")


if __name__ == "__main__":
    for p in sys.argv[1:]:
        strip(p)
