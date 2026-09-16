---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: 剧情任务物品与 seeking 派生进度

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | `QuestDefinition` 必须无损携带合法 `[monster reward item]` 七元组；不完整或非法组不得被解释为可发放规则。 |
| RQ-002 | 任务掉落规则必须同时匹配 QuestLinked Active、冻结 QuestId、队长仍携带任务、monster code、dungeon 和 difficulty，并以当前背包数量及 maxStack/seeking required cap 限制掉落。 |
| RQ-003 | 任务掉落必须携带领域 OwnerCharacterId；MonsterDeath wire 的每个 owned drop record 必须由 CharacterId 稳定映射 owner，不能因 delivery registry 缺项退化为 reporter；普通无 owner 掉落继续使用 reporter fallback。 |
| RQ-004 | GroundPickup 必须在占用前拒绝非 owner，且拒绝不能消耗地面物；owner 成功入包后运行 seeking 派生，并将共享拾取变更延迟到 SQLite 成功提交之后。 |
| RQ-005 | seeking 派生服务必须从当前 carried quests、内容定义和 Main container 的 staged `ReadAllAsync` 计算剩余数，以 CAS 更新变化项并返回完整 carried progress；金币与失败拾取不调用。 |
| RQ-006 | 当前有效连接收到背包拾取通知及更新后的 `AcceptedQuestProgressMessage`；旧连接和非当前 generation 不得收到。 |
| RQ-007 | 普通地下城不读取 QuestLinked 任务来源，不产生剧情任务物品，也不继承剧情完成门槛。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 真实 PVF 的 1779 精确投影两个声明，字段分别等于 65601/65305、145、-1、10149163、1、100、15。 |
| AC-002 | RQ-002, RQ-007 | 匹配 QuestLinked kill 产生有 owner 的任务物品；ordinary、wrong monster/dungeon/difficulty、not carried 均不产生。 |
| AC-003 | RQ-003, RQ-004 | 两人 Run 中每个 wire drop owner 正确；leader 从 delivery registry 离开后 owned drop 仍编码为 leader；helper 拾取被拒且 floor 仍在，leader 领取一次。 |
| AC-004 | RQ-004, RQ-005 | 背包从 0、1、15 个目标物时派生剩余 15、14、0；真实 CommandBus + SQLite CAS prepare 失败时 Trigger、背包与 DungeonPickupChange 均不提交，floor 可重试。 |
| AC-005 | RQ-006 | picker 收到完整 carried quest 进度；replacement picker 测试无发送。 |
| AC-006 | RQ-001-RQ-007 | 受影响测试、Release 构建与完整测试基线比较通过。 |
