"""Turn a PageGuardTrace log into an answer about the protector's sections.

Each record is the FIRST touch of one page of a watched section: the faulting
address, the instruction that faulted, and the top of the stack. Two questions
matter for the clean-exe programme:

  coverage -- what fraction of each section is live at all? A section with 3% of
      its pages touched is mostly padding or dead payload; one at 60% is working
      code.

  provenance -- who reaches into it? A page reached only from another protector
      section is internal machinery. A page reached from .text or .code is a
      real client dependency, and its caller address is the thing to look at.

Usage:
    analyze_pageguard.py <clienthack-pageguard.log> [--image <DNF.exe>]
"""
import argparse
import json
import struct
import sys
from collections import Counter, defaultdict
from pathlib import Path

PAGE = 4096
ACCESS = {0: "read", 1: "write", 8: "execute"}
# The client's own sections; a touch whose EIP lands here is a real dependency.
CLIENT_SECTIONS = {0, 1, 2, 3, 12, 14}


def load_sections(image_path):
    data = Path(image_path).read_bytes()
    e = struct.unpack_from("<I", data, 0x3C)[0]
    fh = e + 4
    count = struct.unpack_from("<H", data, fh + 2)[0]
    opt_size = struct.unpack_from("<H", data, fh + 16)[0]
    base = struct.unpack_from("<I", data, fh + 20 + 28)[0]
    table = fh + 20 + opt_size
    out = []
    for i in range(count):
        h = table + i * 40
        name = data[h:h + 8].rstrip(b"\0").decode("latin1")
        vsize, vaddr, raw, _ptr = struct.unpack_from("<IIII", data, h + 8)
        span = max(vsize, raw)
        out.append({"index": i, "name": name, "start": base + vaddr,
                    "end": base + vaddr + span})
    return out


def locate(sections, address):
    for s in sections:
        if s["start"] <= address < s["end"]:
            return s
    return None


def label(section):
    return "??" if section is None else "%02d:%s" % (section["index"], section["name"])


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("log", type=Path)
    ap.add_argument("--image", type=Path,
                    default=Path(r"D:\project\DNF\DNFClient-debug\DNF.exe"),
                    help="the packed exe, read only for its section table")
    ap.add_argument("--callers", type=int, default=12,
                    help="client call sites to list per section")
    args = ap.parse_args()

    sections = load_sections(args.image)
    header = None
    records = []
    for line in args.log.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line.startswith("{"):
            continue
        try:
            obj = json.loads(line)
        except ValueError:
            continue
        if obj.get("event") == "arm":
            header = obj
        else:
            records.append(obj)

    if header is None:
        sys.exit("no arm record in the log: the run never armed")

    watched = {w["index"]: {"name": w["name"],
                            "start": int(w["start"], 16),
                            "end": int(w["end"], 16)}
               for w in header["sections"]}
    print("armed %s  base %s  %d page(s) over %d section(s)"
          % (header["time"], header["base"], header["pages"], len(watched)))
    print("records: %d" % len(records))
    print()

    touched = defaultdict(set)          # section index -> set of page numbers
    callers = defaultdict(Counter)      # section index -> Counter of client EIPs
    origin = defaultdict(Counter)       # section index -> Counter of EIP section
    access_mix = defaultdict(Counter)
    unattributed = 0

    for r in records:
        addr = int(r["addr"], 16)
        eip = int(r["eip"], 16)
        target = None
        for index, w in watched.items():
            if w["start"] <= addr < w["end"]:
                target = index
                break
        if target is None:
            unattributed += 1
            continue
        touched[target].add(addr // PAGE)
        access_mix[target][ACCESS.get(r.get("access"), str(r.get("access")))] += 1
        source = locate(sections, eip)
        origin[target][label(source)] += 1
        if source is not None and source["index"] in CLIENT_SECTIONS:
            callers[target][eip] += 1
        elif r.get("access") == 8:
            # An execute fault reports Eip inside the guarded page, so the only
            # candidate for the caller is the return address on the stack.
            stack = r.get("stack") or []
            if stack:
                ret = int(stack[0], 16)
                ret_section = locate(sections, ret)
                if ret_section is not None and ret_section["index"] in CLIENT_SECTIONS:
                    callers[target][ret] += 1

    print("%-12s %10s %10s %8s   %s"
          % ("section", "pages", "touched", "live%", "access mix"))
    print("-" * 78)
    for index in sorted(watched):
        w = watched[index]
        total = (w["end"] - w["start"] + PAGE - 1) // PAGE
        hit = len(touched[index])
        mix = ", ".join("%s=%d" % (k, v) for k, v in access_mix[index].most_common())
        print("%-12s %10d %10d %7.1f%%   %s"
              % ("%02d:%s" % (index, w["name"]), total, hit,
                 100.0 * hit / total if total else 0.0, mix or "-"))
    if unattributed:
        print("\n%d record(s) outside every watched range" % unattributed)

    print()
    print("who reaches each section (by the section the faulting EIP is in):")
    for index in sorted(watched):
        if not origin[index]:
            continue
        top = ", ".join("%s=%d" % (k, v) for k, v in origin[index].most_common(6))
        print("  %02d:%-8s  %s" % (index, watched[index]["name"], top))

    print()
    print("client call sites into each section (.text/.code/.data/.rdata/.lA0/.rsrc):")
    for index in sorted(watched):
        hits = callers[index]
        if not hits:
            print("  %02d:%-8s  none" % (index, watched[index]["name"]))
            continue
        print("  %02d:%-8s  %d distinct" % (index, watched[index]["name"], len(hits)))
        for addr, count in hits.most_common(args.callers):
            print("       %08X [%s] x%d" % (addr, label(locate(sections, addr)), count))


if __name__ == "__main__":
    main()
