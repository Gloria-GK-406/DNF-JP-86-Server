#!/usr/bin/env python3
"""Cut the upstream-referenced types into independent enrichment work packets.

Enrichment is the one part of this scaffold that needs judgement rather than
disassembly, so it is the part that fans out. Batching is by upstream module because
that is what makes the packets independent: one reader, one subsystem, no shared file.
Each packet owns exactly one output shard, so parallel writers cannot collide.

`GameProtocolHandler.cs` is excluded from grouping. It is the dispatch table and mentions
almost every type, so grouping by it would produce one useless 189-type packet; it is
still listed inside each packet as the place to find a type's entry point.

Each packet carries the extracted layout for its types, so the reader compares upstream
parsing order against real field widths instead of inventing offsets.

usage: make-batches.py <inventory.tsv> <layouts.json> <upstream-map.tsv> <outdir>
"""
import sys
import os
import json
import collections

INVENTORY, LAYOUTS, UPMAP, OUTDIR = sys.argv[1:5]
DISPATCH = "Network/Protocol/GameProtocolHandler.cs"
MAX_PER_BATCH = 26

inv = {}
with open(INVENTORY, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        r = dict(zip(header, line.rstrip("\n").split("\t")))
        inv[(r["family"], r["type"])] = r

layouts = json.load(open(LAYOUTS, encoding="utf-8"))

entries = []
with open(UPMAP, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        r = dict(zip(header, line.rstrip("\n").split("\t")))
        files = [x for x in r["files"].split(",") if x]
        specific = [x for x in files if x != DISPATCH]
        r["_files"] = files
        r["_key"] = specific[0] if specific else DISPATCH
        entries.append(r)

groups = collections.defaultdict(list)
for r in entries:
    groups[r["_key"]].append(r)

# Modules large enough to stand alone become their own packet; everything else is packed
# into mixed packets. A packet of two types wastes a reader's startup cost on almost no
# work, and 37 near-empty packets cost far more than they return.
MIN_STANDALONE = 6

standalone, tail = {}, []
for key, rows in groups.items():
    if len(rows) >= MIN_STANDALONE:
        standalone[key] = rows
    else:
        tail.extend(rows)

batches = []
for key in sorted(standalone):
    rows = sorted(standalone[key], key=lambda r: (r["family"], r["type"]))
    for i in range(0, len(rows), MAX_PER_BATCH):
        batches.append((key, rows[i:i + MAX_PER_BATCH]))

tail.sort(key=lambda r: (r["_key"], r["family"], r["type"]))
for i in range(0, len(tail), MAX_PER_BATCH):
    chunk = tail[i:i + MAX_PER_BATCH]
    batches.append(("mixed: " + ", ".join(sorted({r["_key"] for r in chunk})), chunk))

os.makedirs(OUTDIR, exist_ok=True)
os.makedirs(os.path.join(os.path.dirname(UPMAP), "meanings"), exist_ok=True)

names = []
for n, (key, rows) in enumerate(batches, 1):
    slug = ("mixed" if key.startswith("mixed:") else
            key.replace("Network/", "").replace("Handlers/", "")
               .replace("Builders/", "").replace("Parsers/", "")
               .replace(".cs", "").replace("/*", "-any").lower())
    slug = "".join(c if (c.isalnum() or c == "-") else "-" for c in slug).strip("-")
    while "--" in slug:
        slug = slug.replace("--", "-")
    slug = "%02d-%s" % (n, slug or "misc")
    names.append((slug, key, rows))

    L = ["# 富化工作包 %s" % slug, "",
         "**上游主模块**：`%s`" % key, "",
         "**输出分片**：`protocol/patterns/data/meanings/%s.tsv`（本包独占，勿写其他文件）" % slug,
         "", "本包 %d 条协议。下表给出静态提取到的字段布局；你的任务是尽可能为每个字段"
         "填写有证据的可能含义。" % len(rows), ""]
    for r in rows:
        key2 = (r["family"], r["type"])
        e = layouts.get("%s:%s" % key2)
        row = inv.get(key2)
        if e is None or row is None:
            continue
        L.append("## %s `%s` `%s`" % (r["family"], r["type"], r["name"]))
        L.append("")
        L.append("- 文档：`protocol/%s`" % row["doc"])
        L.append("- 上游文件：%s" % ", ".join("`%s`" % x for x in r["_files"]))
        L.append("- 提取状态：`%s`（%s），BodyLength = `%s`"
                 % (e["state"], e.get("reason") or "—", e.get("rule") or "未知"))
        if e["state"] == "structure-unknown":
            L.append("- **无字段表**：结构未知，本包不要为它编造字段。"
                     "若上游代码明确给出了 body 结构，只在下方备注里说明，不要写进 TSV。")
            L.append("")
            continue
        L.append("")
        L.append("| offset | size | enc | 当前含义 |")
        L.append("|---:|---:|---|---|")
        off = 0
        for f in e.get("fields", []):
            w = f["len"] if f["width"] is None else f["width"]
            L.append("| %s | %s | %s | unknown |" % (off if off is not None else "?",
                                                     w if w is not None else "?", f["enc"]))
            off = None if (off is None or w is None) else off + w
        for gi, g in enumerate(e.get("groups", [])):
            L.append("| *重复组 g%d ×n* | | | *offset 列用 `g%d:偏移` 寻址* |"
                     % (gi + 1, gi + 1))
            goff = 0
            for f in g["fields"]:
                w = f["len"] if f["width"] is None else f["width"]
                L.append("| g%d:%s | %s | %s | unknown |"
                         % (gi + 1, goff if goff is not None else "?",
                            w if w is not None else "?", f["enc"]))
                goff = None if (goff is None or w is None) else goff + w
        L.append("")
    open(os.path.join(OUTDIR, slug + ".md"), "w", encoding="utf-8",
         newline="\n").write("\n".join(L) + "\n")

print("batches: %d covering %d protocols"
      % (len(batches), sum(len(r) for _, r in batches)))
for slug, key, rows in names:
    print("  %-44s %2d  <- %s" % (slug, len(rows), key))
