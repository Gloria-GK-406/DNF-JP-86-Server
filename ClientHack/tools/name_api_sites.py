"""Name the cross-module API pointers a disassembler otherwise shows as raw data.

The rebuilt PE declares the client's own IAT as real imports, so a disassembler
names those call sites already. It cannot name the pointers OUTSIDE that table:
the relocation table knows which module each one points into, but a module name
is not a symbol, and `dd 0x76F2A1B0` stays unreadable.

A site is nameable *from the data alone* exactly when Themida's per-site
constant K is zero -- the stored value then IS the API address, and
`value - module_base` is an RVA the module's export table names. When K != 0 the
value lands far outside the module (measured: every K distinct, magnitude ~1-2
GB) and only the mutated code that consumes it knows how to undo the transform.

Those are no longer anonymous: `resolve_wrappers.py` evaluates that consuming
code instead of trying to invert K, and names 749 of them. Prefer it. This tool
remains useful for the sites in plain sections, which no wrapper reaches.

On S4A21 that split is 80 / 452, and the nameable 80 sit almost entirely in
plain sections (."2A, .code, .tls) rather than in the mutated region -- which is
what makes naming them worth doing: the surrounding code is readable.

Emits a CSV and an IDA script. Run against the dump the exe was built from, so
the addresses match the binary being analysed.
"""
import argparse
import csv
import struct
from collections import Counter
from pathlib import Path

IMAGE_BASE = 0x00400000


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


def export_map(path):
    """{function_rva: name} for one PE's named exports."""
    d = Path(path).read_bytes()
    if d[:2] != b"MZ":
        return {}
    e = struct.unpack_from("<I", d, 0x3C)[0]
    if d[e:e + 4] != b"PE\0\0":
        return {}
    fh = e + 4
    opt = fh + 20
    magic = struct.unpack_from("<H", d, opt)[0]
    exp_rva = struct.unpack_from("<I", d, opt + (96 if magic == 0x10B else 112))[0]
    if not exp_rva:
        return {}
    nsec = struct.unpack_from("<H", d, fh + 2)[0]
    osz = struct.unpack_from("<H", d, fh + 16)[0]
    st = fh + 20 + osz
    secs = []
    for i in range(nsec):
        h = st + i * 40
        vs, va, raw, ptr = struct.unpack_from("<IIII", d, h + 8)
        secs.append((va, max(vs, raw), ptr))

    def off(rva):
        for va, size, ptr in secs:
            if va <= rva < va + size:
                return ptr + (rva - va)
        return None

    base = off(exp_rva)
    if base is None:
        return {}
    n_names = struct.unpack_from("<I", d, base + 24)[0]
    of_funcs = off(struct.unpack_from("<I", d, base + 28)[0])
    of_names = off(struct.unpack_from("<I", d, base + 32)[0])
    of_ords = off(struct.unpack_from("<I", d, base + 36)[0])
    if None in (of_funcs, of_names, of_ords):
        return {}
    out = {}
    for i in range(n_names):
        no = off(struct.unpack_from("<I", d, of_names + i * 4)[0])
        if no is None:
            continue
        nm = d[no:d.index(b"\0", no)].decode("latin1")
        ordinal = struct.unpack_from("<H", d, of_ords + i * 2)[0]
        out.setdefault(struct.unpack_from("<I", d, of_funcs + ordinal * 4)[0], nm)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dump", required=True, help="the dump the exe was built from")
    ap.add_argument("--table", required=True, help="crossmodule-relocs-*.csv")
    ap.add_argument("--dll-dir", action="append", default=[],
                    help="where to look for the DLLs; repeatable "
                         "(default: SysWOW64 plus the client folder)")
    ap.add_argument("--out-csv", required=True)
    ap.add_argument("--out-idc", help="also write an IDA python script")
    a = ap.parse_args()

    search = [Path(p) for p in a.dll_dir] or [Path(r"C:\Windows\SysWOW64")]
    dump = Path(a.dump)
    mods = load_modules(dump / "DNF-modules.csv")
    img = (dump / "DNF-memory-rva.bin").read_bytes()

    exports = {}
    named, transformed, unnamed = [], 0, 0
    for row in csv.DictReader(open(a.table, encoding="utf-8-sig")):
        rva = int(row["site_rva"], 16)
        module = row["module"].strip().lower()
        rng = mods.get(module)
        if rng is None:
            continue
        value = struct.unpack_from("<I", img, rva)[0]
        if not (rng[0] <= value < rng[1]):
            transformed += 1                 # api + K, K != 0
            continue
        if module not in exports:
            found = next((d / module for d in search if (d / module).exists()), None)
            exports[module] = export_map(found) if found else {}
        name = exports[module].get(value - rng[0])
        if name:
            named.append((rva, row["section"], module, name))
        else:
            unnamed += 1

    with open(a.out_csv, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["site_rva", "site_va", "section", "module", "function"])
        for rva, section, module, name in sorted(named):
            w.writerow(["0x%08X" % rva, "0x%08X" % (IMAGE_BASE + rva),
                        section, module, name])

    if a.out_idc:
        with open(a.out_idc, "w", encoding="utf-8") as fh:
            fh.write("# Generated by name_api_sites.py -- run in IDA.\n")
            fh.write("# Marks each site as a DWORD and names it after the API it\n")
            fh.write("# points at. These are pointers the import table does not\n")
            fh.write("# cover, so IDA has no way to know what they are.\n")
            fh.write("import ida_bytes, ida_name\n\n")
            for rva, _section, module, name in sorted(named):
                label = "p_%s_%s" % (module.replace(".dll", ""), name)
                label = "".join(c if (c.isalnum() or c == "_") else "_"
                                for c in label)[:200]
                fh.write("ida_bytes.create_data(0x%08X, ida_bytes.FF_DWORD, 4, "
                         "ida_idaapi.BADADDR)\n" % (IMAGE_BASE + rva))
                fh.write("ida_name.set_name(0x%08X, %r, ida_name.SN_FORCE)\n"
                         % (IMAGE_BASE + rva, label))

    print("named            : %d" % len(named))
    print("K != 0 (opaque)  : %d" % transformed)
    print("in-module, no export entry: %d" % unnamed)
    print("by section: %s"
          % ", ".join("%s=%d" % kv
                      for kv in Counter(s for _r, s, _m, _n in named).most_common()))
    print("distinct APIs: %d" % len({(m, n) for _r, _s, m, n in named}))
    print("wrote %s" % a.out_csv)
    if a.out_idc:
        print("wrote %s" % a.out_idc)


if __name__ == "__main__":
    main()
