"""只读解码 A21 dstr.dat（FILE/102/1042）并导出婚房/树/胶囊相关字符串（复用仓内解码器）。"""
from __future__ import annotations
import hashlib, importlib.util, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
DSTR = ROOT.parent / "DNFClient-A21-protocol" / "dstr.dat"
spec = importlib.util.spec_from_file_location("read_dstr", ROOT / "Tool/ClientStringPatch/patch_sao_strings.py")
dec = importlib.util.module_from_spec(spec); spec.loader.exec_module(dec)

def table() -> dict[int, str]:
    text = dec.decode(dec.resources(DSTR)[("FILE", 102, 1042)]).decode("utf-8")
    return {int(k): v for line in text.splitlines() if ">" in line for k, v in [line.split(">", 1)] if k.isdigit()}

def main() -> None:
    t = table()
    ids = [int(a, 16) for a in sys.argv[1:]]
    out = [f"dstr={DSTR}", f"dstr_sha256={hashlib.sha256(DSTR.read_bytes()).hexdigest()}", "range 0x11C30..0x11D60 (婚姻段) + 关键词命中 + 指定 ID", ""]
    for k in sorted(t):
        if 0x11C30 <= k <= 0x11D60 or k in ids or re.search("婚房|家具|情侣|爱情树|浇水|收获|胶囊|仓库|种子|树苗|小屋", t[k]):
            out.append(f"{k:08X} {t[k]}")
    Path(__file__).with_name("dstr-housing.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    print(len(out))

if __name__ == "__main__":
    main()
