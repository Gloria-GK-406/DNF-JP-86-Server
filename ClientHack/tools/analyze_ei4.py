"""What is .eI4, and who reads it?

.eI4 (VA 0x04E5D000, 0x10C000 bytes, entropy ~7.96) is the only protector
data section that is still opaque: the mutated CRT in .-wI reads it at
runtime (dropping it crashes startup at a precise address), it has zero
control flow, and nothing on disk explains it -- the shipped exe carries raw
size 0 for it, so the unpacker materialises it at startup.

This tool answers the cheap questions first:

  * is the content boot-stable? (compare two dumps' .eI4, page by page)
  * what is its internal shape? (per-page entropy, byte histogram,
    repeated 16-byte blocks -- ECB-style repetition shows up immediately,
    4-byte value histogram, zero runs)
  * who points at it? (every dword at every alignment, image-wide, that
    lands in .eI4 -- the boundary-1 "method B"; computed references will
    not show up, stored tables will)

Usage:
  python analyze_ei4.py --dump <dumpdir> [--dump2 <other-dumpdir>]
"""
import argparse
import math
import struct
import sys
from collections import Counter
from pathlib import Path

import capstone

BASE = 0x400000
EI4_VA = 0x04E5D000
EI4_END = 0x04F69000


def entropy(b):
    if not b:
        return 0.0
    c = Counter(b)
    n = len(b)
    return -sum((v / n) * math.log2(v / n) for v in c.values())


def structure(img, name):
    lo, hi = EI4_VA - BASE, EI4_END - BASE
    blob = img[lo:hi]
    print("[%s] .eI4 bytes=%d" % (name, len(blob)))
    print("  overall entropy: %.4f  nonzero: %.1f%%"
          % (entropy(blob), 100 * sum(1 for x in blob if x) / len(blob)))
    pages = [blob[i:i + 0x1000] for i in range(0, len(blob), 0x1000)]
    ents = [entropy(p) for p in pages]
    lo_e, hi_e = min(ents), max(ents)
    flat = sum(1 for e in ents if e > 7.5)
    print("  per-page entropy: min=%.3f max=%.3f  pages>7.5: %d/%d"
          % (lo_e, hi_e, flat, len(pages)))
    low_pages = [(i, e) for i, e in enumerate(ents) if e < 6.5]
    if low_pages:
        print("  low-entropy pages (<6.5): %d -- first 10: %s"
              % (len(low_pages),
                 ", ".join("%d(pageVA %08X)=%.2f"
                           % (i, EI4_VA + i * 0x1000, e)
                           for i, e in low_pages[:10])))
    # repeated 16-byte blocks (ECB / key reuse fingerprint)
    blocks16 = Counter(blob[i:i + 16] for i in range(0, len(blob) - 16, 16))
    rep = [(b, c) for b, c in blocks16.items() if c > 1]
    print("  repeated aligned 16B blocks: %d distinct-with-dups "
          "(top counts: %s)"
          % (len(rep), sorted((c for _b, c in rep), reverse=True)[:8] or "none"))
    dups = Counter(blob[i:i + 4] for i in range(0, len(blob) - 4, 4))
    top4 = dups.most_common(3)
    print("  top 4B values: %s (of %d slots)"
          % ([("%s x%d" % (b.hex(), c)) for b, c in top4], len(blob) // 4))
    print("  first 32 bytes: %s" % blob[:32].hex(" "))
    print("  last  32 bytes: %s" % blob[-32:].hex(" "))
    return blob


def differential(a, b):
    n = min(len(a), len(b))
    diff = [i for i in range(n) if a[i] != b[i]]
    print("[diff] %d/%d bytes differ (%.2f%%)"
          % (len(diff), n, 100 * len(diff) / n))
    if not diff:
        print("[diff] IDENTICAL across boots -> boot-stable payload")
        return
    # page-level diff map
    pages = Counter((EI4_VA + (i & ~0xFFF)) for i in diff)
    total_pages = n // 0x1000
    print("[diff] pages touched: %d/%d" % (len(pages), total_pages))
    for va, c in sorted(pages.items())[:12]:
        print("    page %08X: %d differing bytes" % (va, c))
    if len(diff) <= 8:
        for i in diff:
            print("    off +%06X: %02x -> %02x" % (i, a[i], b[i]))


def reference_census(img):
    """Every dword at every alignment image-wide that lands inside .eI4."""
    hits = Counter()
    sites = []
    for off in range(0, len(img) - 4, 4):
        v = struct.unpack_from("<I", img, off)[0]
        if EI4_VA <= v < EI4_END:
            hits[v & ~0xFFF] += 1
            if len(sites) < 400000:
                sites.append((BASE + off, v))
    print("[refs] dword pointers into .eI4: %d (distinct target pages %d)"
          % (len(sites), len(hits)))
    for va, c in hits.most_common(10):
        print("    -> page %08X : %d refs" % (va, c))
    by_region = Counter()
    for src, _t in sites:
        rva = src - BASE
        if rva < 0x2C28000:
            by_region[".text"] += 1
        elif rva < 0x395E000:
            by_region[".code"] += 1
        elif rva < 0x4E2A000:
            by_region[".data"] += 1
        elif rva < 0x4E5D000:
            by_region[".rdata"] += 1
        elif rva < 0x4F69000:
            by_region[".eI4"] += 1
        elif rva < 0x598B000:
            by_region[".rsrc/.tls/.vmp0/.vmp1"] += 1
        elif rva < 0x5D8C000:
            by_region[".bss/.mackt/.-wI"] += 1
        else:
            by_region['."2A/.#|U/.rsrc2'] += 1
    for k, v in by_region.most_common():
        print("    from %-24s %d" % (k, v))
    return sites


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True)
    ap.add_argument("--dump2", help="second dump for the boot-stability diff")
    a = ap.parse_args()
    img = (Path(a.dump) / "DNF-memory-rva.bin").read_bytes()
    blob1 = structure(img, "dump1")
    if a.dump2:
        img2 = (Path(a.dump2) / "DNF-memory-rva.bin").read_bytes()
        blob2 = structure(img2, "dump2")
        differential(blob1, blob2)
    reference_census(img)


if __name__ == "__main__":
    sys.exit(main())
