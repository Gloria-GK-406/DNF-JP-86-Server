"""输出婚房 manager / 树对象等辅助函数（只读）。用法：python -I probe_helpers.py <out-name> RVA[:title] ..."""
from __future__ import annotations
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import a21lib as L

def main() -> None:
    name = sys.argv[1]
    out = L.header_lines()
    for arg in sys.argv[2:]:
        rva_s, _, title = arg.partition(":")
        rva = int(rva_s, 16)
        out += L.dump_function(rva, title or "helper")
        cs = L.callers(rva)
        out.append("direct callers: " + (", ".join(f"{c:08X}(fn {L.find_entry(c):08X})" for c in cs[:40]) or "none"))
        out.append("")
    Path(__file__).with_name(name).write_text("\n".join(out) + "\n", encoding="utf-8")
    print("ok")

if __name__ == "__main__":
    main()
