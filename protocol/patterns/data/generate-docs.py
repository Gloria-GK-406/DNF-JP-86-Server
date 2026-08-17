#!/usr/bin/env python3
"""Generate the per-protocol document scaffold and its index.

Each document has a machine zone, delimited by markers, and everything outside it is
human territory. Regeneration rewrites only what is between the markers, so refinement
added later survives the next run. That is the whole point of the split: these documents
are drafts meant to grow into the authoritative record as the game is walked forward,
and a generator that clobbers hand-written evidence would make that impossible.

Field meanings are an *input*, not an edit. They live in `meanings/*.tsv`, keyed by
family, type and offset, and are merged in at generation time. Enrichment therefore never
writes to a document, which keeps parallel enrichment free of shared-file contention and
keeps a meaning from being silently lost on the next regeneration.

Documents already written by hand are never touched. Their content is better than
anything derivable here and the index marks them as authoritative instead.

usage: generate-docs.py <inventory.tsv> <layouts.json> <protocol-root>
"""
import sys
import os
import json
import glob
import collections

INVENTORY, LAYOUTS, ROOT = sys.argv[1], sys.argv[2], sys.argv[3]
BEGIN = "<!-- rks:machine:begin -->"
END = "<!-- rks:machine:end -->"

# A CMD's request and response share one type and one exchange, so one C->S document owns
# both. One response was documented separately before that rule existed; rather than move
# hand-verified content, the generated request document links to it.
LEGACY_RESPONSE_DOCS = {
    ("CMD", "0x0001"): ("../server-to-client/CMD-0x0001.md",
                        "LOGIN 响应已有独立的手写记录（含实机样本）"),
}

# Protocols already covered by a hand-written family document. Those documents carry
# measured results this generator cannot derive -- buffer sizes recovered from adjacent
# offsets, reachability scans with positive controls, cross-checks against upstream. A
# reader who lands on the generated draft and never sees them would redo work that is
# already done, so the draft points at the family document.
FAMILY_DOCS = {
    ("CMD", "0x00C5"): "game-settings.md", ("CMD", "0x00C6"): "game-settings.md",
    ("CMD", "0x0170"): "game-settings.md", ("CMD", "0x01C0"): "game-settings.md",
    ("NOTI", "0x00AD"): "game-settings.md", ("NOTI", "0x01C7"): "game-settings.md",
    ("NOTI", "0x0187"): "game-settings.md",
}
FAMILY_TITLES = {"game-settings.md": "设置族：客户端设置的上传与下发"}

GEN_NOTE = (
    "> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**\n"
    "> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。\n"
    "> 本区之外的一切内容属于人工区，重新生成不会触碰。\n"
)

STATE_TEXT = {
    "closed": ("布局已闭合", "字段序列与宽度由构包/接收指令逐条读出，无分支、无变长原语。"),
    "closed-parametric": ("布局已闭合（含计数重复组）",
                          "存在按计数重复的字段组，长度为 `固定部分 + n×组宽`。"),
    "partial": ("布局部分可得", "已读出部分字段，但存在下列因素使其不完整。"),
    "structure-unknown": ("结构未知", "静态分析无法得出字段边界。"),
}

REASON_TEXT = {
    "": "",
    "empty-body": "构包器写完 header 即收尾，一个字段都没写——**body 长度为 0**。"
                  "这是已知的布局，不是未知：实现时不要为它构造任何载荷。",
    "counted-repeat-group": "字段组按运行时计数重复，重复次数需由样本或计数字段确定。",
    "polymorphic-tail": "构包路径上存在经虚函数表的间接调用，载荷由运行时类型决定，"
                        "静态不可展开。需先建立「kind → 载荷布局」表。",
    "polymorphic-payload": "整个载荷经虚函数写出，静态无任何字段边界可读。",
    "variable-width-primitive": "使用了宽度不由调用点决定的原语（变长字符串或长度前缀字节串），"
                                "该字段宽度需由样本确定。",
    "conditional-fields": "存在条件分支，部分字段仅在特定路径写出/读出。",
    "unfollowable-call-in-builder": "构包路径上有跟不下去的调用（进入打包区、超出跟进深度或递归），"
                                    "被调函数可能还写了字段。因此本表**可能不完整**——"
                                    "不能据此断言「字段就这些」。",
    "builder-sites-disagree": "同一 type 的多个构包点写出的字段形状不一致，"
                              "说明存在按调用路径分化的载荷。",
    "reads-inside-packed-region": "handler 的取字段调用进入了被打包/混淆的代码区，静态跟不下去。",
    "no-field-access-found": "走遍可达调用图未发现任何取字段原语调用。"
                             "**这是否证，不是证明**——只能说明静态没找到，不能断言不存在。",
    "unfollowable-call-in-handler": "handler 的调用图上有跟不下去的地方——间接调用（经函数指针"
                                    "或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到"
                                    "的那一侧，因此本表**可能不完整**。"
                                    "**这不等于「载荷是多态的」**——它只说明静态走到此为止。",
    "no-builder-site-found": "在 `.text` 中未找到该 type 的构包点。"
                             "客户端注册了它的响应 handler，说明该请求确实存在，"
                             "其构包器很可能位于被混淆的第二代码区。",
    "no-handler-found": "未找到该 type 的接收 handler。",
}

# The same reason code describes different code on each side, and the generic text would
# assert the wrong thing. A C->S document analyses a *builder*; saying "this handler
# probably does not read the body" there is a claim about code that was never examined.
REASON_TEXT_BY_DIRECTION = {
    ("C2S", "no-field-access-found"):
        "构包器在 `MakeHeader` 与 `Finalize` 之间未调用任何写入原语，但也没有正常收尾。"
        "**这是否证，不是证明**——可能是走到了跟不下去的分支，不能据此断言 body 为空。",
    ("S2C", "no-field-access-found"):
        "走遍可达调用图未发现任何取字段原语调用；该 handler 很可能不读 body（纯触发型）。"
        "**这是否证，不是证明**——需要正对照与盲区清单才能支撑该结论，"
        "参见 [game-settings 的否证方法](../../patterns/game-settings.md)。",
}


def canonical_type(raw):
    """Normalise a hand-written type to the `0xABCD` form used everywhere else.

    Shards are hand-written, so `0x02bc`, `0X02BC` and `0x2BC` all occur. Uppercasing the
    whole string mangles the `0x` prefix, so both sides of the join are parsed as numbers
    and re-formatted instead."""
    try:
        return "0x%04X" % int(raw.strip(), 16)
    except ValueError:
        return None


def load_meanings():
    """meanings/*.tsv -> {(family, type, offset): (meaning, confidence, source)}"""
    out = {}
    for path in sorted(glob.glob(os.path.join(os.path.dirname(LAYOUTS), "meanings", "*.tsv"))):
        with open(path, encoding="utf-8") as f:
            header = f.readline()
            if "family" not in header:
                continue
            for line in f:
                p = line.rstrip("\n").split("\t")
                if len(p) < 6 or not p[0].strip():
                    continue
                fam, ty, off, meaning, conf, src = p[0], p[1], p[2], p[3], p[4], p[5]
                canon = canonical_type(ty)
                if canon is None:
                    continue
                # `12` is a top-level offset; `g1:0` is offset 0 inside repeat group 1.
                scope, _, raw_off = off.strip().rpartition(":")
                try:
                    key = (fam.strip(), canon, scope, int(raw_off))
                except ValueError:
                    continue
                if meaning.strip():
                    out[key] = (meaning.strip(), conf.strip() or "Hypothesis",
                                src.strip() or os.path.basename(path))
    return out


def field_rows(entry, meanings, fam, ty):
    """Render fields in wire order, with repeat groups shown inline where they occur.

    Two things this must not get wrong.

    Order. Groups are emitted at the instruction address where the loop actually sits,
    not appended after the fixed fields. A group in the middle of a body would otherwise
    be printed at the end and every offset around it would read as authoritative when the
    real order differs.

    Offsets after a group are unknowable, because the repeat count is a runtime value.
    They are printed as `?` rather than as a number that happens to assume one entry --
    a plausible wrong offset is what makes a reader mis-parse a real frame.

    Meanings are keyed by scope as well as offset. Group offsets restart at zero, so a
    flat key would let one meaning land on both a top-level field and a group field."""
    canon = canonical_type(ty)
    default_src = "构包指令" if entry["direction"] == "C2S" else "接收指令"
    rows = []

    nominal_used = [False]

    def emit(f, scope, off, indent, exact=True):
        w = f["len"] if f["width"] is None else f["width"]
        m = meanings.get((fam, canon, scope, off)) if off is not None else None
        meaning = m[0] if m else "unknown"
        conf = m[1] if m else "—"
        src = m[2] if m else default_src
        if f.get("src"):
            src = "%s，取自 `%s`" % (src, f["src"])
        if off is None:
            shown = "?"
        elif exact:
            shown = str(off)
        else:
            shown = "%d\\*" % off
            nominal_used[0] = True
            if m:
                # The grade describes the field's identity, which upstream code can
                # establish outright. Its position is a separate claim and a weaker one,
                # so say so on the row rather than silently letting a Confirmed grade
                # appear to vouch for an offset the repeat count can still move.
                src += "。**位置为名义值**（按 n=0 排布）"
        rows.append("| %s%s | %s | `%s` | %s | %s | %s |" % (
            indent, shown, "?" if w is None else w,
            f["enc"], meaning, conf, src))
        return None if (w is None or off is None) else off + w

    groups = entry.get("groups", [])
    items = [("f", f, f["at"]) for f in entry.get("fields", [])]
    items += [("g", (i, g), min([x["at"] for x in g["fields"]], default=0))
              for i, g in enumerate(groups)]
    items.sort(key=lambda x: x[2])

    # Offsets after a repeat group depend on a runtime count, so they are marked nominal
    # rather than dropped. Dropping them would also drop every field meaning attached to
    # them -- the position is uncertain, the identity of the field is not.
    offset, exact = 0, True
    for kind, payload, _at in items:
        if kind == "f":
            offset = emit(payload, "", offset, "", exact)
        else:
            i, g = payload
            tag = "g%d" % (i + 1)
            rows.append("| **重复组 %s ×n** |  |  | *以下字段按运行时计数重复；"
                        "其后字段的偏移随重复次数变化* |  |  |" % tag)
            goff = 0
            for f in g["fields"]:
                goff = emit(f, tag, goff, "&nbsp;&nbsp;↳&nbsp;")
            exact = False
    if nominal_used[0]:
        rows.append("")
        rows.append("带 `*` 的偏移是**名义值**：偏移累加时**没有计入其前重复组的任何一次重复**，"
                    "即按重复次数 **n = 0** 排布。真实位置 = 名义值 + Σ(该组宽度 × 实际重复次数)。"
                    "实现时必须按计数字段累加，不能照抄这个数字。")
    return rows


def render(entry, row, meanings):
    fam, ty = row["family"], row["type"]
    name = row["name"] or "（无枚举名）"
    direction = "Client → Server" if entry["direction"] == "C2S" else "Server → Client"
    state_title, state_note = STATE_TEXT[entry["state"]]

    L = []
    L.append(BEGIN)
    L.append("")
    L.append(GEN_NOTE)
    L.append("## 身份")
    L.append("")
    L.append("| 项 | 值 | 来源 |")
    L.append("|---|---|---|")
    L.append("| Endpoint | Game | 帧协议基线 |")
    L.append("| Direction | %s | %s |" % (direction,
             "构包点普查" if entry["direction"] == "C2S" else "接收注册表普查"))
    L.append("| Command 族 | `%s`（%s） | 客户端两张注册表的分表证据 |"
             % ("0x01" if fam == "CMD" else "0x00", fam))
    L.append("| Type | `%s` | 同上 |" % ty)
    L.append("| 名称 | `%s` | %s |" % (name, row["name_source"]))
    L.append("| 身份说明 | %s | 普查 |" % row["identity_note"])
    L.append("")

    fam_doc = FAMILY_DOCS.get((fam, ty))
    if fam_doc:
        L.append("> **本协议属于一个已有手写族文档的协议族：**"
                 "[%s](../../patterns/%s)。"
                 % (FAMILY_TITLES.get(fam_doc, fam_doc), fam_doc))
        L.append("> 那里有本页无法推导的实测结论（缓冲区尺寸、可达性扫描、上游交叉核对）。"
                 "**先读它。**")
        L.append("")

    L.append("## Body")
    L.append("")
    L.append("**状态：%s** — %s" % (state_title, state_note))
    code = entry.get("reason", "")
    reason = REASON_TEXT_BY_DIRECTION.get((entry["direction"], code)) \
        or REASON_TEXT.get(code, code)
    if reason:
        L.append("")
        L.append("原因：%s" % reason)
    L.append("")

    if entry["state"] == "structure-unknown":
        L.append("本协议**没有可用的字段表**。上面的原因即其不可知的成因；")
        L.append("要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。")
        if entry.get("opaque"):
            L.append("")
            L.append("静态跟不下去的调用目标：%s"
                     % "、".join("`%s`" % t for t in entry["opaque"]))
    else:
        rule = entry.get("rule") or "未知"
        L.append("```text")
        if entry["state"].startswith("closed"):
            L.append("BodyLength = %s" % rule)
        else:
            # A partial extraction knows some fields are missing, so the sum of the ones
            # it saw is a lower bound, never the length. Printing `BodyLength = 7` here
            # is exactly how a reader ends up writing a decoder against a number the
            # extractor never claimed.
            L.append("BodyLength = 未知（字段集合已知不完整）")
            L.append("已读出字段合计 = %s   ← 下界，不是总长" % rule)
        L.append("```")
        L.append("")
        L.append("| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |")
        L.append("|---:|---:|---|---|---|---|")
        L.extend(field_rows(entry, meanings, fam, ty))
        L.append("")
        L.append("`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——"
                 "这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。")
    L.append("")

    L.append("## 定位")
    L.append("")
    if entry["direction"] == "C2S":
        sites = row["builder_sites"]
        L.append("| 项 | 值 |")
        L.append("|---|---|")
        L.append("| 构包点 | %s |" % (", ".join("`%s`" % s for s in sites.split(",")[:12])
                                     if sites else "**未找到**（见上方原因）"))
        if sites and len(sites.split(",")) > 12:
            L.append("| | …共 %d 个构包点 |" % len(sites.split(",")))
        L.append("| 构包点所在区 | %s |" % (row["builder_region"] or "—"))
        L.append("| 多构包点一致性 | %s |" % entry.get("site_agreement", "n/a"))
        if row["handler_rva"]:
            L.append("| 响应 handler（客户端） | `%s` |" % row["handler_rva"])
            L.append("| 响应 handler 注册路径 | %s |" % (row["handler_path"] or "—"))
        L.append("")
        legacy = LEGACY_RESPONSE_DOCS.get((fam, ty))
        if legacy:
            L.append("**响应侧另有文档**：%s → [%s](%s)"
                     % (legacy[1], os.path.basename(legacy[0]), legacy[0]))
            L.append("")
        if row["handler_rva"]:
            L.append("客户端为本 type 注册了 CMD 表 handler，"
                     "说明服务端**会**对它回一帧同 type 的 CMD 响应。")
        else:
            L.append("客户端**未**为本 type 注册 CMD 表 handler，"
                     "因此它很可能是 fire-and-forget 的上报，服务端不必回帧。")
    else:
        L.append("| 项 | 值 |")
        L.append("|---|---|")
        L.append("| handler 入口 | `%s` |" % row["handler_rva"])
        L.append("| handler 所在区 | %s |" % (row["handler_region"] or "—"))
        L.append("| 注册路径 | %s |" % (row["handler_path"] or "—"))
        L.append("")
    L.append("")
    L.append("支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`")
    L.append("")
    L.append(END)
    return "\n".join(L)


HUMAN_STUB = """
<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
"""


def write_doc(path, body, name, ty, fam):
    title = "# `%s` `%s`" % (ty, name) if name else "# `%s`（无枚举名）" % ty
    if fam == "NOTI":
        title = "# NOTI `%s` `%s`" % (ty, name) if name else "# NOTI `%s`（无枚举名）" % ty
    if os.path.exists(path):
        old = open(path, encoding="utf-8").read()
        if BEGIN in old and END in old:
            head, rest = old.split(BEGIN, 1)
            _, tail = rest.split(END, 1)
            new = head + body + tail
        else:
            return "skipped-handwritten"
    else:
        new = title + "\n\n" + body + "\n" + HUMAN_STUB
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w", encoding="utf-8", newline="\n").write(new)
    return "written"


# ------------------------------------------------------------------------- run
rows = []
with open(INVENTORY, encoding="utf-8") as f:
    header = f.readline().rstrip("\n").split("\t")
    for line in f:
        rows.append(dict(zip(header, line.rstrip("\n").split("\t"))))

layouts = json.load(open(LAYOUTS, encoding="utf-8"))
meanings = load_meanings()
stats = collections.Counter()
index = {"C2S": [], "S2C": []}

for row in rows:
    key = "%s:%s" % (row["family"], row["type"])
    entry = layouts[key]
    path = os.path.join(ROOT, row["doc"].replace("/", os.sep))
    status = write_doc(path, render(entry, row, meanings),
                       row["name"], row["type"], row["family"])
    stats[status] += 1
    stats["state:" + entry["state"]] += 1
    index[row["direction"]].append((row, entry, status))

# ------------------------------------------------------------------------- index
def index_table(items, direction):
    L = ["| Type | 名称 | Body 状态 | 长度规则 | 文档 |",
         "|---|---|---|---|---|"]
    for row, entry, status in items:
        if status == "skipped-handwritten":
            # Never print the machine verdict for a hand-written document. Those carry
            # capture-confirmed conclusions this extractor cannot reach, and showing
            # "结构未知" next to a document that states a Confirmed empty body would make
            # the index contradict the very record it links to.
            L.append("| `%s` | `%s` | **手写记录** | 见文档 | [%s](%s) **(手写)** |"
                     % (row["type"], row["name"] or "—",
                        os.path.basename(row["doc"]),
                        row["doc"].split("game/", 1)[1]))
            continue
        rule = entry.get("rule") or "—"
        if entry["state"] == "structure-unknown":
            # No field table means no length. `length_rule()` sums an empty field list to
            # "0", and printing that asserts an empty body -- which is exactly what these
            # documents forbid ("这是否证，不是证明"). The index must not claim what the
            # document it links to refuses to claim.
            rule = "—"
        elif not entry["state"].startswith("closed"):
            rule = "≥ %s" % rule if rule != "—" else "—"
        L.append("| `%s` | `%s` | %s | `%s` | [%s](%s) |"
                 % (row["type"], row["name"] or "—",
                    STATE_TEXT[entry["state"]][0], rule,
                    os.path.basename(row["doc"]),
                    row["doc"].split("game/", 1)[1]))
    return L


idx = ["# Game 协议全量索引", "",
       "本页由 [`patterns/data/generate-docs.py`](../patterns/data/generate-docs.py) 生成，"
       "覆盖静态普查可见的全部协议身份。**重新运行会整体覆盖本文件。**", "",
       "协议身份 = endpoint + direction + command 族 + type。同一 type 在 CMD 与 NOTI 下是"
       "两条毫不相干的协议——`0x0289` 在 CMD 是 `HARDCORE_MURDERER`、在 NOTI 是"
       " `HONOR_LEVEL_INFO`，两族各有自己的文档，因此两族各自成表。", "",
       "标 **(手写)** 的条目是人工逆向并经实机验证的权威记录，生成器不会改写它们；"
       "其余为静态分析初稿，`unknown` 表示证据不足而非尚未查看。", "",
       "## Body 状态的含义", "",
       "| 状态 | 含义 | 下一步该做什么 |", "|---|---|---|",
       "| 布局已闭合 | 字段序列与宽度由指令逐条读出，无分支、无变长原语 | 可直接据以实现；用真实样本复核总长 |",
       "| 布局已闭合（含计数重复组） | 同上，但含按计数重复的字段组 | 需从样本或计数字段确定重复次数 |",
       "| 布局部分可得 | 读出了部分字段，但存在分支、变长原语或多态尾部 | 补一个真实样本，或对分支点下断点 |",
       "| 结构未知 | 字段边界无法由静态分析确定 | 只能靠运行时手段；静态再投入无收益 |", "",
       "## Client → Server（全部为 CMD 族）", "",
       "客户端的出站帧构造点把 command 字节写死为 `0x01`，因此**客户端只发 CMD、从不发 NOTI**。"
       "一条 CMD 的请求与其响应共用同一个 type、同属一次交换，记在同一篇文档内。", ""]
idx += index_table(index["C2S"], "C2S")
idx += ["", "## Server → Client（NOTI 族，服务端主动推送）", "",
        "NOTI 是单向推送通道，客户端从不发送。其 body 没有 CMD 的两字节结果信封。", ""]
idx += index_table(index["S2C"], "S2C")
idx += ["", "### S→C 的 CMD 响应", "",
        "CMD 的响应与其请求同 type、同属一次交换，因此记在该 type 的 **C→S 文档**内，"
        "上表不重复列出。下面是唯一的例外：一条在该约定确立之前就已单独成篇的手写记录，"
        "其请求文档反向链接到它。", "",
        "| Type | 名称 | 文档 |", "|---|---|---|",
        "| `0x0001` | `LOGIN` 响应 | "
        "[CMD-0x0001.md](server-to-client/CMD-0x0001.md) **(手写)** |"]
idx += ["", "## 覆盖与边界", "",
        "本索引的完整性受两处静态盲区限制，两者都已在各自文档中显式标注：", "",
        "- **C→S**：被混淆的第二代码区扫不到构包点。%d 条 CMD 只在接收侧留下响应 handler，"
        "其请求构包器不在 `.text` 中。" % sum(1 for r, e, s in index["C2S"]
                                              if not r["builder_sites"]),
        "- **S→C**：约四分之一的 handler 完全不读 body，无法从指令推出布局；"
        "另有一部分取字段调用进入打包区。", "",
        "方法与证据见 [game-frame](../patterns/game-frame.md) 与"
        " [game-receive](../patterns/game-receive.md)。"]

open(os.path.join(ROOT, "game", "index.md"), "w", encoding="utf-8",
     newline="\n").write("\n".join(idx) + "\n")

print("documents        : %d written, %d skipped (hand-written)"
      % (stats["written"], stats["skipped-handwritten"]))
for k in sorted(k for k in stats if k.startswith("state:")):
    print("  %-28s %5d" % (k[6:], stats[k]))
print("meanings merged  : %d field entries" % len(meanings))
print("index            : %s" % os.path.join(ROOT, "game", "index.md"))
