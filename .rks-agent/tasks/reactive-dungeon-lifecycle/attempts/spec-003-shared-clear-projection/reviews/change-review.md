---
kind: rks-review
verdict: approved
reviewed_at: 2026-09-16
---

# Change Review

独立审查首轮要求修正两项阻断：权威 `ClearOpen` 不能依赖死亡/拾取帧发送成功；Boss-dead-then-final-task-item 必须用真实 SQLite 与 CommandBus 证明。

复审确认均已关闭：

- GroundPickup、solo/party MonsterDeath 在首个网络写入前提交可选 `DungeonClearChange`，随后仍保持“死亡/拾取帧 → 清关帧”的 wire 顺序。
- 拾取和死亡发送异常测试证明 `ClearOpen` 已提交且不能重复开启。
- `QuestClearVerticalTests` 使用 production pickup、Seeking、Settlement、Clear 与真实 SQLite，证明 Trigger 1→0、背包 14→15、唯一 clear 向两名当前成员各一次。
- frozen leader、离场/replacement generation、单人和 ordinary 边界无新增问题。

复审相关 Protocol Tests 26/26、Application Tests 40/40 通过；Release build 0 warnings / 0 errors；`git diff --check` passed。
