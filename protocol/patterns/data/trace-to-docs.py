#!/usr/bin/env python3
"""Write measured field tables into protocol documents from a runtime read trace.

Why this exists
---------------
The static extractor answers "what does the handler read" by walking the call
graph, and on S->C it loses that argument wherever the graph enters the packed
region -- which is most notification handlers. Those documents carry a Body
section marked "布局部分可得" that is a lower bound at best, and for polymorphic
bodies it is worse than a lower bound: the 0x0002 table describes subtype 0 and
silently reads as though it described the whole protocol.

The read trace measures the same question at runtime instead. Every S->C field
extraction goes through four primitives, so hooking them yields the real field
sequence -- offsets, widths, order, values, and the RVA that asked. Packed code
cannot hide from it. See patterns/game-receive.md.

So this tool takes measurement as the authority and writes it into the human
zone, above whatever the static walk produced. It does not touch the machine
zone: that stays as the extractor left it, and the section written here says
plainly which of the two to believe.

What it cannot do
-----------------
Give meanings. A measured table is boundaries and observed values; the `含义`
column is left for a person, and the cheapest way to fill it is a differential
against a live client now that the offsets are known.

Family
------
The trace records no CMD/NOTI selector -- the primitives see a stream, not a
message -- and the same type number in the two families is two unrelated
protocols, so guessing would write CMD data into NOTI documents. The data
settles it: a CMD body opens with the result envelope, read by the receive layer
itself, so a frame whose first read comes from ENVELOPE_READ_RVA is CMD and
every other frame is NOTI. Checked against the server's own per-frame send log
on 2026-08-17: the eight envelope-first frames were exactly the six types it
had sent with selector=0x01.

usage: trace-to-docs.py <read-trace.log> <protocol-root> [--force] [--dry-run]
"""
import collections
import json
import os
import re
import sys

# Return address of the `ok` ReadU8 inside the receive function; see
# patterns/game-receive.md, "CMD body 的两字节结果信封".
ENVELOPE_READ_RVA = 0x00D89B25

SECTION_TITLE = "## 实测字段表（运行时读取跟踪）"


def encoding_of(width, is_bytes):
    if is_bytes:
        return "`bytes`(%d)" % width
    return {1: "`u8`", 2: "`u16`", 4: "`u32`"}.get(width, "`bytes`(%d)" % width)


def load_frames(path):
    frames = []
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            line = line.strip()
            if not line:
                continue
            try:
                frames.append(json.loads(line))
            except ValueError:
                continue
    return frames


def signature(frame):
    """Frames with the same read sequence describe the same shape.

    Grouping on this rather than on body length keeps a polymorphic protocol's
    subtypes apart (0x0002 reads three different sequences) while collapsing the
    repeats of a variable-length one that happen to read identically.
    """
    return tuple((r["off"], r["width"] if "width" in r else r["w"],
                  bool(r.get("peek") is not None)) for r in frame["fields"])


def render(family, type_id, groups):
    """One section: a table per observed shape, newest evidence first."""
    out = [SECTION_TITLE, ""]
    out.append(
        "客户端**实际**读取这条协议的方式，由运行时读取跟踪测得（做法见 "
        "[game-receive](../../patterns/game-receive.md#四个原语是唯一出口)）。"
    )
    out.append("")
    out.append(
        "**本节优先于机器区的 Body 表。** 机器区那张表由静态走图产出，在 S→C 上普遍"
        "止步于打包区，给出的是下界；载荷多形时它只描述其中一形，却读起来像描述了整条协议。"
        "本节是实测，但**只给边界，不给含义**——`含义` 一列留给人填，"
        "偏移已确证之后用实机改值做差分是最便宜的填法。"
    )
    out.append("")
    if family == "CMD":
        out.append(
            "偏移 0 那次读取属于**接收层**而不是 handler：每条 CMD body 都以两字节结果信封"
            "开头，由分发前的接收函数读掉（见 "
            "[结果信封](../../patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)）。"
        )
        out.append("")

    for sig, frames in groups:
        lengths = sorted({f["body_len"] for f in frames})
        sample = frames[0]
        consumed = max((r["off"] + (r.get("width") or r["w"]))
                       for r in sample["fields"]) if sample["fields"] else 0
        tail = sample["body_len"] - consumed
        head = "### 形状 %s" % (
            "body %s 字节" % "/".join(str(n) for n in lengths))
        if len(frames) > 1:
            head += "（%d 帧共享）" % len(frames)
        out.append(head)
        out.append("")
        out.append("| Offset | Size | Enc | 读取者 RVA | 实测值 | 含义 |")
        out.append("|---:|---:|---|---|---|---|")
        prev_end = None
        for record in sample["fields"]:
            width = record.get("width") or record["w"]
            is_bytes = record.get("peek") is not None
            value = ("`%s`…" % record["peek"]) if is_bytes else str(record.get("v", ""))
            if prev_end is not None and record["off"] != prev_end:
                out.append("| *%d* | *%d* | — | — | *未被读取* | *空隙* |"
                           % (prev_end, record["off"] - prev_end))
            out.append("| %d | %d | %s | `0x%08X` | %s | — |"
                       % (record["off"], width, encoding_of(width, is_bytes),
                          record["ret"], value))
            prev_end = record["off"] + width
        out.append("")
        out.append("读取 %d 次，消费到偏移 **%d**。" % (len(sample["fields"]), consumed))
        if tail > 0:
            out.append("")
            out.append(
                "> **尾部 %d 字节客户端不读。** 服务端发 %d 字节，客户端停在 %d。"
                "未被读到的字节**不是「结构未知的字段」，是没有字段**——"
                "按[少读无害](../../patterns/game-receive.md#多发无害少发才致命)"
                "它无害，但不该记成待补全项。"
                % (tail, sample["body_len"], consumed)
            )
        elif tail < 0:
            out.append("")
            out.append(
                "> **客户端读超出了 body %d 字节。** 这会从下一帧取字节并使其后每帧错位，"
                "见[多发无害少发才致命]"
                "(../../patterns/game-receive.md#多发无害少发才致命)。"
                % -tail
            )
        out.append("")
    return "\n".join(out).rstrip() + "\n"


def document_path(root, family, type_id):
    if family == "CMD":
        return os.path.join(root, "game", "client-to-server", "0x%04X.md" % type_id)
    return os.path.join(root, "game", "server-to-client", "NOTI-0x%04X.md" % type_id)


def splice(text, section):
    """Insert or replace the measured section immediately after the machine zone.

    Placed there so a reader meets the measurement before the static table it
    supersedes, and outside the machine markers so regeneration cannot touch it.
    """
    marker = "<!-- rks:machine:end -->"
    existing = text.find(SECTION_TITLE)
    if existing >= 0:
        nxt = text.find("\n## ", existing + len(SECTION_TITLE))
        end = len(text) if nxt < 0 else nxt + 1
        return text[:existing] + section + "\n" + text[end:]
    at = text.find(marker)
    if at < 0:
        return text.rstrip() + "\n\n" + section
    at += len(marker)
    return text[:at] + "\n\n" + section + text[at:]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    force = "--force" in sys.argv
    dry = "--dry-run" in sys.argv
    if len(args) != 2:
        print(__doc__.strip().splitlines()[-1])
        return 2
    trace_path, root = args

    frames = load_frames(trace_path)
    buckets = collections.OrderedDict()
    for frame in frames:
        if not frame.get("fields"):
            continue
        first = frame["fields"][0]
        family = "CMD" if first["ret"] == ENVELOPE_READ_RVA else "NOTI"
        buckets.setdefault((family, frame["type"]), collections.OrderedDict()) \
            .setdefault(signature(frame), []).append(frame)

    written = skipped = missing = 0
    for (family, type_id), shapes in buckets.items():
        path = document_path(root, family, type_id)
        if not os.path.exists(path):
            print("  missing document: %s 0x%04X" % (family, type_id))
            missing += 1
            continue
        with open(path, encoding="utf-8", errors="replace") as handle:
            text = handle.read()
        if SECTION_TITLE in text and not force:
            skipped += 1
            continue
        section = render(family, type_id, list(shapes.items()))
        if not dry:
            with open(path, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(splice(text, section))
        written += 1
        print("  %s 0x%04X  %d shape(s)  %s" % (
            family, type_id, len(shapes), os.path.basename(path)))

    print("frames: %d   protocols: %d   written: %d   skipped(existing): %d   "
          "missing doc: %d" % (len(frames), len(buckets), written, skipped, missing))
    print("re-run with --force to replace sections already present")
    return 0


if __name__ == "__main__":
    sys.exit(main())
