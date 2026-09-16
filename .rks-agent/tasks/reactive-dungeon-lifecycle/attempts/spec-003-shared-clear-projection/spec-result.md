---
kind: rks-spec-result
state: accepted
contract:
  path: ./TaskContract.md
  revision: 1
review: ./reviews/change-review.md
evidence: ./verification/completion-evidence.md
---

# Spec Result: 共享 Boss 清关变更与投影

Boss 清场已经从 MonsterDeath 的协议特判收回到共享 `DungeonRun` 实例：房间清空与冻结剧情条件共同决定一次性的 `DungeonClearChange`，死亡和任务物品拾取都只触发同一个领域入口。投影边界按 frozen participant、当前 generation、仍在同一 run 三项过滤。

本结果只负责“开启结算入口”。每名参与者各自提交成绩、翻牌与独立回城仍属于下一 Spec。

独立审查首轮发现 ClearOpen 受首个网络写入成败影响，以及关键顺序测试没有真实穿过 SQLite/CommandBus。修正后，领域清关事实先提交再按既有 wire 顺序投影；真实纵向测试证明 Boss 已死、最后任务物品入包后才产生一次共享清关并投影给两名成员。复审通过。
