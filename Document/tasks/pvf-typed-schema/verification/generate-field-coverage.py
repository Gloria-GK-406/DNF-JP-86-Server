"""Refresh declared-field coverage from the final source, retaining boundary notes."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[4]
target = root / "Document/tasks/pvf-typed-schema/content-coverage.md"
assert target.resolve().is_relative_to(root.resolve())
old = target.read_text(encoding="utf-8")
tail = old[old.index("## 无标签或按位置的固定序列"):]
tail = re.sub(r"统计：[^\n]+", "", tail)
pattern = re.compile(r'static readonly PvfField<([^\n]+?)>\s+(\w+)\s*=\s*PvfField\.(\w+)(?:<[^;\n]+>)?\s*\(\s*"([^"]+)"')
sections = []
count = 0
for path in sorted((root / "Server/src/Dfo.Server.Infrastructure/Content").glob("*.cs")):
    fields = pattern.findall(path.read_text(encoding="utf-8-sig"))
    if not fields:
        continue
    count += len(fields)
    table = [f"## {path.name}", "", "| 声明 | PVF 名称 | 结果类型 / 形状 |", "|---|---|---|"]
    table.extend(f"| `{name}` | `{tag}` | `{kind}` / `{factory}` |" for kind, name, factory, tag in fields)
    sections.append("\n".join(table))
header = "# PVF 固定读取覆盖清单\n\n由 verification/generate-field-coverage.py 从当前源码生成。复合 Tokens 字段由各主题的专业解析器完整验证；字段族和动态边界见下方说明。\n\n"
header += f"统计：{len(sections)} 个内容主题，{count} 个具名字段声明（不含 ProbeFields.Name 和 PvfFieldFamily）。\n\n"
target.write_text(header + "\n\n".join(sections) + "\n\n" + tail, encoding="utf-8", newline="\n")
print(f"Generated {len(sections)} themes / {count} named fields.")
