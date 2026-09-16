---
kind: rks-review
verdict: approved
reviewed_at: 2026-09-16
---

# Change Review

独立审查经过三轮后通过，无剩余阻断项。

首轮指出：翻牌共享状态必须晚于持久化奖励提交；双人同时自动翻牌会竞争同一列；prepared reservation 在离场时可能被替换并由旧事务误删；基于墙钟的计时测试存在负载相关失败。修正后：

- `DungeonCardSelectionCommit` 作为延迟回执，只有 durable participant 成功后才发布共享选牌状态；失败会释放精确 token 并允许重试。
- prepared claim 在离场和 run 清理期间保留；reserve/release/commit 均校验 prepared 状态与原 token。
- 2 秒 layout 与 4 秒 auto-free 通过可控 delay 测试，不再依赖真实墙钟。
- auto-free 维护本轮已尝试列；瞬态 reservation 冲突而共享快照未前进时直接尝试下一列，最多执行 `Slots.Count` 次。
- 双人并发自动翻牌最终占用两个不同列，各发一次奖励；replacement、离场和旧 run 回调均被过滤。

复审执行 Coordinator/Projection 8/8、DungeonRun 相关 53/53；Release build 0 warnings / 0 errors；`git diff --check` passed。
