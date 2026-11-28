---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# 加百利秘密商店实现结果

普通地下城结算会按 PVF 选择商人并生成角色货单。A21 可消费专用商品列表和购买协议，金币或道具扣除、入包和货单库存由同一个工作单元提交。上述行为已通过自动验证。A21 用户已确认赫顿玛尔旧街区的单人金币路径符合预期，实机范围见[本轮实机结果](user-test.md#本轮实机结果)。

## 交付

- [测试步骤和固定刷出配置](user-test.md)
- [自动验证](verification/completion-evidence.md)
- [独立审查](reviews/change-review.md)
- [协议清单](../../../protocol/catalog.md)

## Git

- 实机候选原功能分支 `feature/gabriel-secret-shop`，基线 `fe6c55321045f7099f14821759ac1ee625b8b43f`。
- 规划 `3808c76a4`，实现 `5a9143e1c`，实机资料修订 `94757a1e1`。
