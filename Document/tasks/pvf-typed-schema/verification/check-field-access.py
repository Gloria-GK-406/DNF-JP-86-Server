"""Check fixed PVF field access; dynamic probes and field value enums stay valid."""
from pathlib import Path
import re
import sys
root = Path(__file__).resolve().parents[4]
patterns = [
    re.compile(r'GetTag\s*\(\s*"'),
    re.compile(r'(?:section\.Name|fieldName|tag|section)\s*(?:==|!=)\s*"\['),
    re.compile(r'(?:OneInt|RequiredInt|FirstReal|ReadSectionValues|RequireTag|Offset)\([^\n]*"\['),
]
paths = sorted(root.joinpath("Server/src").rglob("*.cs")) + sorted(root.joinpath("Tool").rglob("*.cs"))
findings = []
scanned = 0
for path in paths:
    if any(part in {"obj", "bin", "out"} for part in path.parts):
        continue
    scanned += 1
    for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if any(pattern.search(line) for pattern in patterns):
            findings.append(f"{path.relative_to(root)}:{number}: {line.strip()}")
if findings:
    print("\n".join(findings))
    sys.exit(1)
print(f"PASS: checked {scanned} source files for fixed string tag lookup/comparison/helper access.")
print("Dynamic Probe values/where queries and field-value enums are excluded by syntax, not file exemptions.")
