---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Game 协议初期文档骨架

## Delivered

- Game 端点静态可见的 **1410 条协议身份**（C→S CMD 682 + S→C NOTI 728）各有一篇文档，
  外加 1 条规格声明的既有例外（LOGIN 响应独立成篇），共 1411 篇，按方向分层。
- 每篇记录 command 族、type、方向、官方名与名称来源、body 状态（有布局 / 结构未知二选一）、
  字段偏移宽度编码与可能含义、以及构包点或 handler 入口。
- **1408 / 1410（99.9%）取得官方名**，取自上游 `CmdPacketType` / `NotiPacketType`；
  未命名的两个是各族 `0x0000`，非合法 wire type。
- **870 条有证据的字段含义**（Confirmed 564 / High 286 / Hypothesis 47），
  每条给出上游 `file:line`；其余 2577 条字段诚实保留 `unknown`。
- 可重跑的静态分析工具链（14 个脚本）与 5 道门禁，`rebuild.py` 一次跑完全流水线。
- 覆盖全部文档的[全量索引](../../../protocol/game/index.md)。
- 既有 15 篇手写文档并入新命名方案并完成 7 处官方名增补，证据零删除。
- [失谐记录](../../../Docs/ProtocolScaffoldDissonances.md)：六类客户端与上游的布局分歧，
  只记录不弥合。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `check-acceptance.py` A-1：缺失 0、未声明的多余 0、声明的例外 1 |
| SC-2 | A-2：1396 篇生成文档五项身份要素齐全；名称与上游枚举一致 |
| SC-3 | A-3：逐篇恰好命中一个 body 状态，无第三态；结构未知者全部带成因 |
| SC-4 | `check-meanings.py` 897 行 0 错误；A-8：870 条含义无一只引用默认文本，全部带等级；三位独立复核者各自做了全量核查并逐条打开上游源码核对行号 |
| SC-5 | `check-controls.py` 14/14；`check-controls-mutation.py` 9 组双向变异全部被抓出 |
| SC-6 | 逐篇 `git diff`：删除行仅为已失效的旧命名注记，证据表、样本、逆向日志零改动；重命名为真 `git mv` |
| SC-7 | `check-regeneration-safety.py` 0 失败，还原经实测确认；`rebuild.py` 幂等，1480 文件逐字节不变 |
| SC-8 | A-6：索引 1411 条，与文档集合精确一致 |

## Repository Result

- Changed paths: 22 个既有文件（增补/改名/删除被超集覆盖的旧普查产物）+ 1419 个新增文件。
- Baseline: `ac52cda`；结果提交在分支 `protocol-doc-scaffold`。
- Parent rollback required: no

## Successor Guidance

- **这批文档是初稿，不是定稿。** 走到某条协议时先开它的文档，多半已有官方名、构包点/handler
  入口与字段骨架；要做的是把 `unknown` 换成有证据的结论并确认响应流程。
- **改字段含义请改 `patterns/data/meanings/*.tsv`**，那是生成器的输入；直接改机器区会在下次
  重新生成时丢失。机器区之外的一切是人工领地，重新生成不触碰。
- **「结构未知」意味着静态分析已到头**，不要再往那个方向投入，直接上运行时手段。
  其成因分六类并逐篇标注。
- **重复组之后的偏移是名义值**（带 `*`，按 n=0 排布），实现时必须按计数字段累加。
- **枚举名给"叫什么"，分发表给"做什么"，两者可以不一致**——多个协议的名称与其实际 handler
  完全对不上。实现前必须跟进分发目标。
- 客户端与上游存在多处布局分歧，判定它们**需要实机样本**，见失谐记录。
- **流程教训**：三轮复核中有两轮记录到"复核期间工作树在动"，导致其门禁结论无法绑定。
  口头冻结不是可绑定的对象——**先提交，用提交哈希作为冻结声明**。

## Artifacts

- 协议文档与索引：`protocol/game/`
- 工具链与数据：`protocol/patterns/data/`
- 失谐记录：`Docs/ProtocolScaffoldDissonances.md`
- 复核：`.rks-agent/tasks/protocol-doc-scaffold/reviews/change-review.md`
- 验证证据：`.rks-agent/tasks/protocol-doc-scaffold/verification/completion-evidence.md`
