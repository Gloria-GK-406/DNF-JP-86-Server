"""Regenerate only the abyss-related machine projections, preserving human sections.

Run from the task repository root. Full scaffolding goes to ignored artifacts; only
three corrected machine zones and seven evidence-backed index rows are copied back.
The generator and original inputs remain the repository's authoritative mechanism.
"""
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[3]
data = root / "protocol/patterns/data"
output = root / "artifacts/abyss-protocol-regeneration"
output.mkdir(parents=True, exist_ok=True)
subprocess.run(
    [sys.executable, str(data / "generate-docs.py"),
     str(data / "protocol-inventory.tsv"), str(data / "protocol-layouts.json"),
     str(output)], check=True)
begin, end = "<!-- rks:machine:begin -->", "<!-- rks:machine:end -->"
for relative in ("game/server-to-client/NOTI-0x02A6.md", "game/server-to-client/NOTI-0x02A7.md", "game/client-to-server/0x00EB.md"):
    target = root / "protocol" / relative
    existing = target.read_text(encoding="utf-8")
    generated = (output / relative).read_text(encoding="utf-8")
    prefix, rest = existing.split(begin, 1)
    _, suffix = rest.split(end, 1)
    zone = generated.split(begin, 1)[1].split(end, 1)[0]
    target.write_text(prefix + begin + zone + end + suffix, encoding="utf-8", newline="\n")

target = root / "protocol/game/index.md"
existing = target.read_text(encoding="utf-8")
generated = (output / "game/index.md").read_text(encoding="utf-8")
identities = {
    "client-to-server/0x0413.md", "client-to-server/0x04C3.md",
    "server-to-client/NOTI-0x00CF.md", "server-to-client/NOTI-0x02A7.md",
    "server-to-client/NOTI-0x030E.md", "server-to-client/NOTI-0x0431.md",
    "server-to-client/NOTI-0x049C.md",
}
for identity in sorted(identities):
    old = [line for line in existing.splitlines() if f"]({identity})" in line]
    new = [line for line in generated.splitlines() if f"]({identity})" in line]
    if len(old) != 1 or len(new) != 1:
        raise RuntimeError(f"Expected one index row for {identity}")
    existing = existing.replace(old[0], new[0])
target.write_text(existing, encoding="utf-8", newline="\n")
print("Updated three machine zones and seven index rows; human content preserved.")
