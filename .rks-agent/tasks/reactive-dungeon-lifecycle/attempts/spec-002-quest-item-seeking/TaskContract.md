---
kind: rks-task-contract
revision: 2
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-6
  - SC-7
depends_on:
  - ../spec-001-reactive-pickup-projection/spec-result.md
---

# TaskContract: 剧情任务物品与 seeking 派生进度

## Context

任务 1779 是剧情地下城 145 的 `seeking` 目标；真实 A21 PVF 声明怪物 65601/65305 各以 100% 概率掉落物品 10149163，需求和持有上限均为 15。当前服务端既没有投影 `[monster reward item]`，也只在角色进入时根据背包重算 seeking Trigger，运行期拾取不能解除剧情清关门槛。

## Goal

让 QuestLinked 运行按其冻结任务声明产生有归属的任务物品；物品成功进入权威背包后，在同一命令事务内按背包现状派生、持久化并投影 seeking Trigger，使任务 1779 在取得 15 个目标物时归零，同时显式普通地下城保持不注入剧情任务掉落。

## Scope

### In

- 投影 PVF `[monster reward item]` 七元组并钉住任务 1779 的实际数据。
- 仅对 `QuestLinked + Active + exact QuestId` 的运行解析目标怪、地下城和难度。
- 任务物品归属于冻结队长；非归属角色不能领取该地面物。
- 在背包成功变化后重算所有 carried seeking 任务，CAS 持久化并向当前连接投影完整 carried quest 进度。
- 让 MonsterDeath 每个掉落记录可投影自己的 owner，同时保留普通掉落的既有 reporter owner。

### Out

- 不处理 `[enemy reward item]`、捕获物、清关任务物品或全部任务掉落词汇。
- 不实现 Boss 清关、结算、翻牌与回城状态机；本结果只解除其任务前置缺口。
- 不修改 UDP/P2P。

## Constraints

- 普通运行即使角色持有任务 1779，也不得产生其剧情任务物品。
- 不匹配怪物、地下城、难度、任务状态或未携带冻结任务时不得产生任务物品。
- 背包写入与 seeking Trigger 写入必须处于同一 CMD 事务；任一步失败不得提交共享拾取变更。
- 共享 `DungeonPickupChange` 必须作为 commit-infallible transaction participant 延迟到 SQLite 成功提交之后；SQLite prepare/CAS 失败必须释放占用并保留地面物。
- Trigger 是背包事实的派生值，不以客户端上报为真源；最多写入九位通道范围。
- 任务物品只有归属角色可以领取；其他成员不得消耗地面占用。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C2-SC-1 | 真实 PVF 中任务 1779 投影两个怪物来源与 item/count/rate/maxStack 原值 | 真实档案内容测试 |
| C2-SC-2 | QuestLinked 1779 的匹配目标怪产生归属队长的 10149163；普通运行或不匹配范围不产生 | 规则与 MonsterDropService 测试 |
| C2-SC-3 | 非归属成员不能拾取任务物品；归属成员领取一次且共享地面状态只提交一次 | GroundPickup 命令测试 |
| C2-SC-4 | 成功拾取后 Trigger 按背包现值 15→14→0 派生并持久化；SQLite CAS/commit 失败、命令异常或金币路径不推进且不消耗地面物 | seeking 服务、真实 CommandBus + SQLite 事务测试 |
| C2-SC-5 | 当前连接收到更新后的完整 carried quest 进度，replacement connection 不收到旧结果 | Protocol 投影测试 |
| C2-SC-6 | Release 构建、受影响定向测试和无新增完整测试失败 | 构建与测试证据 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根任务授权：“其余的可以按你说的处理。你来进行修改重构吧” | 在隔离工作区实现、验证并提交本子结果；不含合并与部署 |
