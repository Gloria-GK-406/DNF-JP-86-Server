"""核对并输出婚房/树/胶囊相关 S2C handler（CMD 回包与 NOTI）及其注册点（只读）。

注册点取自 protocol/patterns/data/protocol-identity.tsv，并在此按指令重新核对：
注册现场应为 `push handlerVA ; push type ; ... call Register/forwarder`。
"""
from __future__ import annotations
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L

TSV = Path(__file__).resolve().parents[4] / "protocol" / "patterns" / "data" / "protocol-identity.tsv"
WANT_CMD = set(range(0x03F2, 0x0407))
WANT_NOTI = set(range(0x03D0, 0x03E4))

def main() -> None:
    rows = []
    for line in TSV.read_text(encoding="utf-8").splitlines()[1:]:
        f = line.split("\t")
        if f[0] != "S2C":
            continue
        t = int(f[2], 16)
        if (f[1] == "CMD" and t in WANT_CMD) or (f[1] == "NOTI" and t in WANT_NOTI):
            rows.append((f[1], t, int(f[4], 16), int(f[7].split(",")[0], 16)))
    out = L.header_lines()
    out.append("family type handlerRVA regSiteRVA verified")
    for fam, t, h, site in rows:
        txt = ""
        for back in range(0x16, 0x8, -1):
            ws = L.window(site - back, site + 5)
            if ws and ws[-1].address - L.BASE == site:
                txt = " | ".join(f"{i.mnemonic} {i.op_str}" for i in ws)
                if f"0x{h + L.BASE:x}" in txt:
                    break
        ok = f"0x{h + L.BASE:x}" in txt and f"0x{t:x}" in txt
        out.append(f"{fam:4} 0x{t:04X} {h:08X} {site:08X} {'OK' if ok else 'CHECK'} :: {txt}")
    out.append("")
    done = set()
    for fam, t, h, site in rows:
        if h in done:
            out.append(f"({fam} 0x{t:04X} shares handler {h:08X})")
            continue
        done.add(h)
        out += L.dump_function(h, f"S2C {fam} 0x{t:04X} handler")
    Path(__file__).with_name("handlers-disassembly.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    print("ok", len(rows))

if __name__ == "__main__":
    main()
