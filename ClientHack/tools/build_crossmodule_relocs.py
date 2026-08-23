"""Build a client's cross-module relocation table from a CROSS-REBOOT dump pair.

The rebuilt PE is a memory-dump replay, so it bakes the dumping boot's module
bases. Pointers into other modules must be re-based at startup by the entry
stub: value += current_base(M) - dump_base(M). This tool produces the (site,
module) table the stub walks.

Why the pair must straddle a reboot: within one boot only per-process-randomised
DLLs move. Every KnownDLL -- kernel32, user32, gdi32, ntdll -- keeps its base
until the machine restarts, so a same-boot pair shows a zero delta for exactly
the modules that matter and every site pointing at one of them is invisible.

Scanning is byte-granular, not DWORD-aligned: most wrapper sites are immediates
embedded in instructions and are not 4-aligned.

A value need NOT lie inside its module's range. Themida's wrappers store
api_address + K for a per-site constant K; addition is transparent to
subtraction, so the stored value still moves by exactly the base delta.
Requiring the value to be in range rejects every transformed site.

Sites another writer already owns are excluded by ADDRESS, not by section: the
client IAT (filled by the loader, because the rebuilt PE declares those slots as
real imports) and any orphan slots the stub resolves itself would otherwise get
a base delta applied on top of an already-correct value. On S4A12 that is what
made an early table crash: kernel32!TlsGetValue/TlsSetValue/TlsFree were stub-
resolved and then had kernel32's delta added again.

Two modules that happen to share a delta in ONE pair are indistinguishable in
that pair. Picking one silently is a latent cross-boot fault: it stays correct
until a later boot splits the group. On S4A12 exactly that happened: ntdll and
version.dll shared a delta in the pair the table was built from, and a later
boot separated them, leaving 9 sites re-based by the wrong module.

--refine settles those without waiting for another reboot, because a SAME-BOOT
pair splits groups in the complementary way. Within one boot every KnownDLL
keeps its base while the app-local DLLs are re-randomised per process, so a
group like d3dx9_43|dbghelp|fmodex|msacm32|msvcp60|msvcr100|tbbmalloc -- welded
together across a reboot -- comes apart completely, each member moving by a
different amount. Candidates are intersected across every supplied pair, so a
module survives only if it explains the observed change in ALL of them.
"""
import argparse
import csv
import re
import struct
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np


def load_modules(path):
    out = {}
    for row in csv.DictReader(open(path, encoding="utf-8-sig")):
        name = (row.get("name") or "").strip().lower()
        if not name:
            continue
        try:
            out[name] = (int(row["base_va"], 16), int(row["end_va"], 16))
        except (KeyError, ValueError):
            pass
    return out


def load_sections(exe):
    d = Path(exe).read_bytes()
    e = struct.unpack_from("<I", d, 0x3C)[0]
    fh = e + 4
    n = struct.unpack_from("<H", d, fh + 2)[0]
    osz = struct.unpack_from("<H", d, fh + 16)[0]
    t = fh + 20 + osz
    out = []
    for i in range(n):
        h = t + i * 40
        name = d[h:h + 8].rstrip(b"\0").decode("latin1")
        vs, va, raw, _ = struct.unpack_from("<IIII", d, h + 8)
        out.append((i, name, va, va + max(vs, raw)))
    return out


def orphan_slots(rebuilder, image_base):
    """RVAs the entry stub resolves via LoadLibrary/GetProcAddress."""
    if not rebuilder or not Path(rebuilder).exists():
        return set()
    src = Path(rebuilder).read_text(encoding="utf-8", errors="surrogateescape")
    m = re.search(r"ORPHAN_SLOTS = \[(.*?)^\]", src, re.S | re.M)
    if not m:
        return set()
    pat = r"\(\s*(0x[0-9A-Fa-f]+),\s*\"([^\"]+)\",\s*\"([^\"]*)\""
    return {int(va, 16) - image_base
            for va, _dll, _fn in re.findall(pat, m.group(1))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--old", required=True, help="dump from boot A")
    ap.add_argument("--new", required=True, help="dump from boot B")
    ap.add_argument("--exe", required=True, help="packed DNF.exe (section names)")
    ap.add_argument("--rebuilder", help="rebuild_runnable_pe.py, for ORPHAN_SLOTS")
    ap.add_argument("--image-base", default="0x00400000")
    ap.add_argument("--refine", nargs=2, action="append", metavar=("OLD", "NEW"),
                    default=[], help="extra dump pair used only to break "
                                     "same-delta ties; repeatable")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    old, new = Path(a.old), Path(a.new)
    image_base = int(a.image_base, 16)

    mo = load_modules(old / "DNF-modules.csv")
    mn = load_modules(new / "DNF-modules.csv")
    shared = set(mo) & set(mn)
    delta = {m: (mn[m][0] - mo[m][0]) & 0xFFFFFFFF for m in shared}
    by_delta = defaultdict(list)
    for m, d in delta.items():
        if d:
            by_delta[d].append(m)
    stuck = sorted(m for m in shared if not delta[m])
    print("modules in both dumps: %d, moved: %d"
          % (len(shared), sum(1 for m in shared if delta[m])))
    print("distinct non-zero deltas: %d" % len(by_delta))
    groups = {d: ms for d, ms in by_delta.items() if len(ms) > 1}
    if groups:
        print("deltas shared by >1 module (ambiguity groups): %d" % len(groups))
        for d, ms in sorted(groups.items(), key=lambda kv: -len(kv[1]))[:8]:
            print("   %08X <- %s" % (d, ", ".join(sorted(ms))))
    if stuck:
        print("did NOT move (invisible in this pair): %s" % ", ".join(stuck))

    A = (old / "DNF-memory-rva.bin").read_bytes()
    B = (new / "DNF-memory-rva.bin").read_bytes()
    n = min(len(A), len(B))
    wanted = np.array(sorted(by_delta), dtype=np.uint32)

    found = {}
    for off in range(4):                       # byte-granular
        count = (n - off) // 4
        x = np.frombuffer(A, dtype="<u4", count=count, offset=off)
        y = np.frombuffer(B, dtype="<u4", count=count, offset=off)
        diff = (y - x).astype(np.uint32)
        for k in np.nonzero(np.isin(diff, wanted))[0]:
            found[off + int(k) * 4] = (int(diff[k]), int(x[k]))

    owned = {}
    for row in csv.DictReader(open(new / "DNF-iat.csv", encoding="utf-8-sig")):
        owned[int(row["slot_rva"], 16)] = "client-IAT"
    for rva in orphan_slots(a.rebuilder, image_base):
        owned[rva] = "orphan-slot"

    secs = load_sections(a.exe)

    def sect(rva):
        for i, name, s, e in secs:
            if s <= rva < e:
                return "%02d:%s" % (i, name)
        return None

    rows, skipped = [], Counter()
    for rva in sorted(found):
        d, value = found[rva]
        label = sect(rva)
        if label is None:
            skipped["outside-any-section"] += 1
            continue
        if rva in owned:
            skipped[owned[rva]] += 1
            continue
        owners = sorted(by_delta[d])
        if len(owners) == 1:
            cands, how = owners, "unique-delta"
        else:
            inrange = [m for m in owners if mo[m][0] <= value < mo[m][1]]
            if len(inrange) == 1:
                cands, how = inrange, "range-tiebreak"
            else:
                cands, how = owners, "same-delta"
        rows.append([rva, label, cands, how])

    # --- break the remaining ties with the extra pairs ---------------------
    for pi, (p_old, p_new) in enumerate(a.refine, 1):
        ties = [r for r in rows if len(r[2]) > 1]
        if not ties:
            break
        p_old, p_new = Path(p_old), Path(p_new)
        d_old = load_modules(p_old / "DNF-modules.csv")
        d_new = load_modules(p_new / "DNF-modules.csv")
        d2 = {m: (d_new[m][0] - d_old[m][0]) & 0xFFFFFFFF
              for m in set(d_old) & set(d_new)}
        P = (p_old / "DNF-memory-rva.bin").read_bytes()
        Q = (p_new / "DNF-memory-rva.bin").read_bytes()
        moved2 = sum(1 for v in d2.values() if v)
        before = sum(len(r[2]) for r in ties)
        settled = 0
        for r in ties:
            rva = r[0]
            try:
                vp = struct.unpack_from("<I", P, rva)[0]
                vq = struct.unpack_from("<I", Q, rva)[0]
            except struct.error:
                continue
            obs = (vq - vp) & 0xFFFFFFFF
            # a module explains this pair only if its own delta matches
            keep = [m for m in r[2] if d2.get(m) == obs]
            if keep and len(keep) < len(r[2]):
                r[2] = keep
                if len(keep) == 1:
                    r[3] = "refined-pair%d" % pi
                    settled += 1
        print("refine pair %d: %d modules moved, %d tied rows, %d fully settled "
              "(candidates %d -> %d)"
              % (pi, moved2, len(ties), settled, before,
                 sum(len(r[2]) for r in ties)))

    with open(a.out, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["site_rva", "section", "module", "resolution"])
        for rva, label, cands, how in rows:
            res = how if len(cands) == 1 else how + ":" + "|".join(cands)
            w.writerow(["0x%08X" % rva, label, cands[0], res])
    rows = [(r[0], r[1], r[2][0], r[3] if len(r[2]) == 1
             else r[3] + ":" + "|".join(r[2])) for r in rows]

    print()
    print("sites found (byte-granular): %d" % len(found))
    print("excluded: %s"
          % (", ".join("%s=%d" % kv for kv in skipped.most_common()) or "none"))
    print("written : %d" % len(rows))
    for k, v in sorted(Counter(r[1] for r in rows).items()):
        print("   %-14s %5d" % (k, v))
    res = Counter(r[3].split(":")[0] for r in rows)
    print("resolution: %s" % ", ".join("%s=%d" % kv for kv in res.most_common()))
    amb = sum(1 for r in rows if r[3].startswith("same-delta"))
    if amb:
        print("STILL AMBIGUOUS: %d rows -- correct for THIS pair, but a later boot"
              % amb)
        print("  that splits the group breaks them. Settle with a second pair.")
    print("modules covered: %d" % len({r[2] for r in rows}))
    print("   " + ", ".join("%s=%d" % kv
                            for kv in Counter(r[2] for r in rows).most_common(8)))
    print("wrote %s" % a.out)


if __name__ == "__main__":
    main()
