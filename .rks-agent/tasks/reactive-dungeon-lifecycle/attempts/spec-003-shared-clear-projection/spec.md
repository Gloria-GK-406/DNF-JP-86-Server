---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 共享清关变更

| ID | Requirement |
|---|---|
| RQ-001 | DungeonRun aggregate 必须持有 ClearOpen，第一次开启递增 RuntimeRevision 并返回 `DungeonClearChange`；重复开启无变化。 |
| RQ-002 | `IDungeonClearDomainService` 必须只从 run、房间规则、冻结 leader 与 settlement rule 评估，不读取连接/session。 |
| RQ-003 | MonsterDeath 和 commit 后 GroundPickup 必须复用同一领域评估；不得各自复制清关条件。 |
| RQ-004 | 协议投影只向 change recipients 中仍属于该 run frozen delivery 且 generation 当前的连接发送清关帧。 |
| RQ-005 | QuestLinked 使用冻结 leader 条件；ordinary 不读取其 carried quest，单人使用自身。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 两次评估只有第一次产生 change，revision 单调且 recipients 为当前 active participants。 |
| AC-002 | RQ-002, RQ-003 | Boss 先死、后拾取最后任务物品时，死亡不产生 change，拾取事务提交后产生 change。 |
| AC-003 | RQ-004 | 两名当前成员各收一次；离场成员、旧 generation 与 replacement connection 不收。 |
| AC-004 | RQ-005 | ordinary、单人 QuestLinked、双人 QuestLinked 条件分别正确。 |
