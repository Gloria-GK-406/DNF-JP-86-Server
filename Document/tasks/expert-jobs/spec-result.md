---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# JP86 四副职业服务端实施结果

## 交付

- 按角色读取、加入和放弃职业，复用职业状态和配方两张角色表。正确职业的专属服务可用。
- 分解师、附魔师、炼金术师和控偶师已接入任务、物品和城镇摊位的正常入口。双人交易在同一事务中更新两方资产及职业状态。
- 记录业务实现和相关协议证据，提供 A21 实机验收顺序。

## 验证

[服务端交付验证](verification/completion-evidence.md) · [独立交付评审](reviews/change-review.md)

## 仓库结果

- 功能分支为 `feature/expert-jobs`，基线为 `cada01a0ec33ad9e3ec34fcad1ce2ca475244fd4`。
- 实现和测试在 `Server/`，协议登记在 `protocol/`，任务证据在本目录。
- A21 实机验收在后续阶段，功能分支及工作树保留供验收。 [A21 验收顺序](A21验收顺序.md)
