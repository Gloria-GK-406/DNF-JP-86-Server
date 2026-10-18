"""Extract couple/housing-related CREATE TABLE blocks from the 1995chen-dnf init_sql dump.

Usage: python -P extract_70_db_schema.py <dir with extracted init_sql.tgz>
Searched tables: any table whose name or columns match housing|couple|marri|wedding|spouse|lover|tree|decoration.
Binary '\0' default runs are collapsed to <NUL*n>.
"""
import pathlib, re, sys

root = pathlib.Path(sys.argv[1])
pat = re.compile(r"(?i)housing|couple|marri|wedding|spouse|lover|_tree|tree_|decoration|furni|capsule")
for f in sorted(root.glob("*.sql")):
    text = f.read_text(encoding="utf-8", errors="replace")
    for m in re.finditer(r"CREATE TABLE `([^`]+)`\s*\((.*?)\)\s*ENGINE=[^;]*;", text, re.S):
        name, body = m.group(1), m.group(0)
        if pat.search(name) or pat.search(m.group(2)):
            line = text.count("\n", 0, m.start()) + 1
            body = re.sub(r"(\\0){2,}", lambda x: f"<NUL*{len(x.group(0)) // 2}>", body)
            print(f"## {f.name}:{line} `{name}`\n{body}\n")
