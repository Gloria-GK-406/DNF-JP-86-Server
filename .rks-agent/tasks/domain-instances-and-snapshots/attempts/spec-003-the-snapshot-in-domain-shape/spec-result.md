---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: The snapshot in domain shape

## Delivered

- 以协议段命名的记录在领域里**不再存在**。两族都删掉了：存储侧的
  `CharacterSubtype0/1StorageRecord`（曾由装配器从四张与两张表按段序拍平），
  以及入场侧的 `CharacterSubtype0/1EntryState`（曾由一段 45 字段的按位复制从前者转来）。
- 取而代之的是一族概念记录——运营态、设置、PvP、专家职业、派生数值、公会站位，加一个
  `CharacterEntryExtrasState`——**存储侧与入场侧共用同一族**，从装配器一路原样传到编解码器。
- 段的次序与位宽收窄现在发生在 `writer.Write(...)` 那一行。`CharacterProfileEntryProjection`
  由 45 字段的按位复制变成一个传递。
- 装载时对「值超出该字段宽度」的拒绝保留在原处，并首次获得用例。
- 下发帧一字节不变。

## Success Evidence

见 `verification/completion-evidence.md` 的逐条表。

## Repository Result

- Changed paths：19，全部属于本尝试。
- Baseline：`078d762`，尝试期间未变。
- Parent rollback required：no。

## Successor Guidance

- **给「没做的事」写的理由，是一个关于代码的断言，和代码一样可以被实验推翻。** 本成果发生过一次：
  为四处未下沉的收窄写的理由，四处里三处是假的，评审真改真跑当场证伪。写进设计文档的理由会被
  当成已经查过的事实——要么查过再写，要么写明没查。
- **改变一个东西的形状，会改变它能出什么错，而守住旧形状的用例不会自动守住新形状。**
  投影从按位复制变成传递之后，唯一剩下的失效方式是整块丢掉或替换一个概念，而那一种此前无人守。
- **加宽类型会改变负值的可表达性。** `uint`→`long` 之后负值成为可表达的，必须验证基线的拦截点仍在
  同一处生效。本成果由第三轮评审专门构造探针确认过。
- 剩余同类工作：技能、任务、进度、特性四个入场投影仍在应用层收窄（27 处）。

## Artifacts

- `spec.md`、`plan.md`
- `reviews/change-review.md`（三轮：`changes_required`、`changes_required`、`passed`）
- `verification/completion-evidence.md`
- `Docs/Refactor/DomainInstanceDesign.md`（「Settled by the snapshot-side implementation」一节）
