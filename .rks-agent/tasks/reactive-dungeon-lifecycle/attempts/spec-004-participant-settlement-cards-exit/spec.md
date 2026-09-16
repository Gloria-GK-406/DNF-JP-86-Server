---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: complete
---

# Specification: 参与者结算与共享翻牌状态机

| ID | Requirement |
|---|---|
| RQ-001 | `TryClaimSettlement` 仅允许 active 且 `ClearOpen` 的参与者；结算仍按参与者独立 exactly-once。 |
| RQ-002 | DungeonRun aggregate 持有共享 `DungeonCardSelection`；同面同列全局唯一，每名参与者同一面最多一个选择。 |
| RQ-003 | 卡牌持久化奖励提交后产生 `DungeonCardSelectionChange`，包含 revision、actor、完整选择快照和语义 recipients。 |
| RQ-004 | 牌面投影必须过滤 frozen generation、replacement connection、离场和非当前 run；共享状态发给所有有效 recipients，私有库存/金币只发给 actor。 |
| RQ-005 | presentation coordinator 以 2 秒 layout 和 4 秒 auto-free 推进；所有入口复用同一 open/select 路径，手动/自动竞态由领域约束决定唯一赢家。 |
| RQ-006 | timer 不是奖励真源；回调必须重新校验 exact session、run id、active participant 和阶段。 |
| RQ-007 | 任一参与者离场只收缩自己，不能结束共享 run 或取消其他参与者的 settlement/card flow。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 未清关结算拒绝；清关后两人各一次，重复各自拒绝。 |
| AC-002 | RQ-002, RQ-003 | 两人不能占同一 free 列，也不能各自重复 free；不同列提交后 change 含两项 seat。 |
| AC-003 | RQ-004 | 两端看到相同完整快照；actor 独享 inventory/gold；旧 generation 与离场者不收。 |
| AC-004 | RQ-005, RQ-006 | 可控时间推进验证 2 秒 layout、再 4 秒 auto；手动抢先和 auto 抢先都只领取一次。 |
| AC-005 | RQ-007 | 先离场成员无后续通知，留下成员仍可翻牌和独立离场。 |
| AC-006 | all | 单人剧情、ordinary、Release build 与完整测试通过。 |
