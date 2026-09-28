"""Print reader call positions, stack destinations, and later indexed setters."""
import re
from pathlib import Path

lines = Path(__file__).with_name("noti0023-reader.asm.txt").read_text(encoding="utf-8-sig").splitlines()
starts = [i for i, line in enumerate(lines) if "call     0x23c5310" in line]
for n, i in enumerate(starts[:27]):
    back = lines[max(0, i - 8):i]
    slot = next((re.search(r"\[ebp - (0x[0-9a-f]+)\]", line)
                 for line in reversed(back) if " lea " in line), None)
    addr = re.search(r"RVA ([0-9A-F]+)", lines[i])
    label = f"head{n}" if n < 4 else f"slot{n-4:02d}"
    print(f"{label} read={addr.group(1)} local={slot.group(1) if slot else '?'}")
