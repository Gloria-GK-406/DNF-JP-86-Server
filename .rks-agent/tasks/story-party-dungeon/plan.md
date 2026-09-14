---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 固定队伍共享剧情地下城首发入场

## Planning Choice

采用 direct 详细计划。共享局、入场投递、死亡/掉落和结算都围绕同一个运行态契约修改，后续步骤的接口已经能从当前代码确定，不需要把细节延迟到独立 Task Unit；将细节保持在一个计划中也能避免两套并行的副本身份模型。

## Approach and Shared Decisions

- `IDungeonRunDomainService` 继续是唯一副本运行态写入口，但内部从“角色 → 独立记录”提升为“角色索引 → 一个共享聚合”。对外查询返回调用角色的参与者视图，共享字段始终来自同一聚合版本。
- 新增不含连接信息的运行标识、冻结参与者与入场请求模型。参与者只持有 AccountId、CharacterId、WireUserId、槽位和队长标志；连接代际由 Protocol 层独立的投递注册表冻结。
- `IDungeonVisitDomainService` 一次解析队长的布局，再原子创建全队共享局；单人 `EnterAsync` 复用同一路径的一人参与者特例。
- 共享状态变更在聚合锁内完成并以运行标识/房间版本防陈旧写。结算、翻牌等角色状态保存在聚合内的逐参与者槽位，不能写进全队共享字段。
- 首版以队长为移动权威。死亡和拾取允许任一参与者发起，但其共享声明只成功一次。
- 协议层冻结每名参与者的 `SelectedSessionIdentity`，投递前核对它仍是当前 generation；它不进入领域模型。队员首发进入使用现有 A21 `ENTER_SELECT_DUNGEON` 前奏加 `SELECT_DUNGEON` 成功投影，不涉及 0x02C2/0x02C3。
- 一次死亡的共享事实先声明，识别与掉落只执行一次；经验和 Hunt Trigger 按冻结参与者逐个执行并分别投递。失败边界沿用现有逐角色事务和共享死亡 claim，不把 Session 泄露进命令。

## Work and Dependencies

### T-001 以失败测试定义共享运行态（RQ-001、RQ-003、RQ-005、RQ-006、RQ-008、RQ-009）

- 在 `Dfo.Server.Application.Tests/World` 扩展 DungeonRun 测试：双成员 Begin、相同 RunId/布局/房间版本、队员移动拒绝、队长移动同步、共享死亡/掉落/拾取幂等、逐成员 settlement/cards、成员 End 后其余成员继续、最后成员 End 后释放。
- 保留并运行既有单人 DungeonRun/CardReward/MonsterDrop/GroundPickup 测试，确保新增接口可以向后兼容或同步更新测试桩。
- 预期首先得到明确编译或断言失败，随后才实现 T-002。

### T-002 实现共享局与参与者领域模型（依赖 T-001）

- 在 `Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` 增加运行标识、房间版本、冻结参与者、参与者身份和全队 Begin 契约；保留单人 Begin 作为一人特例。
- 重写 `Dfo.Server.Application/World/DungeonRunDomainService.cs` 的存储和并发边界：原子登记所有角色索引、共享移动/死亡/掉落/拾取，逐角色 settle/cards，逐参与者 End，清理精确的 run claim。
- 更新 `DungeonVisitDomainService`，新增一次布局解析后创建多参与者局的入口，并使 `DescribeAsync` 对同一共享房间给出稳定投影。
- 让经验、掉落和卡片规则从参与者视图取得冻结 `PartyMemberCount`，移除生产调用处的单人硬编码。

### T-003 以失败测试定义队伍入场和连接代际投递（RQ-001..RQ-005、RQ-010）

- 扩展 `SelectDungeonHandlerTests` 覆盖：队长双人成功、非队长 `0x08` 拒绝、成员 session/runtime 缺失整体拒绝、队长任务决定布局、无任务队员进入、普通模式隔离。
- 新增协议层固定参与者投递注册表测试：只认入场 generation；replacement connection、队伍后续变化和未冻结角色均不接收当前局通知。
- 扩展 MoveMap handler 测试：队长移动向冻结双端发同一 StartMap，队员/陈旧请求不推进。

### T-004 实现首发入场与换图扇出（依赖 T-002、T-003）

- 在 `SelectDungeonHandler` 注入 PartyService 和 session/runtime 查询，先解析并冻结 PartySnapshot，再对全部成员完成纯读取预检；非队长或任一成员不可附着时发送既有拒绝且不调用共享 Begin。
- 对队长只发送现有 SELECT 成功序列；对未主动打开选择页的队员发送现有 ENTER 前奏与 SELECT 成功序列，所有布局/StartMap 来自同一共享局。
- 新增 Protocol 层进程内投递注册表保存 RunId → 冻结 `SelectedSessionIdentity`，并在副本结束时精确撤销参与者。
- 修改 `MoveMapHandler`：只允许冻结队长推进共享局，按投递注册表向所有当前仍匹配的连接广播同一 StartMap。

### T-005 以失败测试定义共享击杀、拾取和逐角色收益（RQ-006、RQ-007）

- 扩展 MonsterDeath handler/command 测试：两端重复同一 actor 只分配一次掉落，双方各得一次 PartyMemberCount=2 的经验和各自 Hunt Trigger；无匹配任务的成员不推进。
- 扩展 GroundPickup handler 测试：一个共享编号只能由一名参与者获得，并向其他冻结成员投递拾取结果而不重复修改背包。

### T-006 实现共享击杀与拾取投递（依赖 T-004、T-005）

- `MonsterDeathHandler` 在共享 claim 成功后识别一次怪物、为每个冻结参与者执行现有逐角色事务并构建各自经验帧；死亡/掉落/开门通知向冻结成员广播，重复上报无第二次收益。
- `GrantKillExperienceCommand` 使用运行态冻结的 PartyMemberCount；任务推进仍查询参与者自己的日志。
- `GetItemHandler` 保留拾取者背包事务，成功后把同一拾取 NOTI 投递给其余冻结成员；共享运行态负责竞争拒绝。

### T-007 以失败测试定义并实现逐成员结算、翻牌和离场（RQ-008、RQ-010）

- 扩展 SetPlayResult、CardReward、Leave/GiveUp/BackToVillage 测试，覆盖两名成员各自 settle/cards、重复拒绝、第一人离场不影响第二人、最后离场释放以及 generation 精确清理。
- 现有处理器继续以当前连接对应角色执行逐成员结算；运行态把 `IsSettled` 和卡片集合投影为该角色自己的状态。
- 离场处理同时精确移除该参与者的投递绑定；不触发重连或中途加入状态机。

### T-008 集成回归、协议记录与测试部署（依赖 T-001..T-007）

- 运行受影响项目测试，再运行 `dotnet build Server/86JP.Server.slnx` 和解决方案完整测试；修复所有接口桩、架构测试和警告即错误问题。
- 在协议人工区或 `Docs` 下记录本次首发组队入场的实现假设、实机待确认点、双端测试步骤和预期日志；不把未实测发送顺序标成 Confirmed。
- 完整审查和新鲜验证通过后生成一次 accepted-result 提交。
- 停止当前主干运行的本地 Host，使用任务工作区已接受提交的 Host 产物启动，确认 7001/10010/10011 监听、启动日志无异常，并向用户交付双客户端实测动作。

## Verification

- `Dfo.Server.Application.Tests`：共享聚合、并发幂等、逐参与者状态和单人回归。
- `Dfo.Server.Protocol.Tests`：双 session 入场/移动/死亡/拾取/结算/离场投递与连接 generation 隔离。
- 相关目标测试全部通过后运行解决方案 build 与完整 test；任何失败的具体 `UnitTestResult` 均视为未通过，不能只看命令退出汇总。
- 最终部署核对进程可执行路径来自任务工作区、HEAD 为 accepted-result 提交、端口监听齐全；实机结果只在用户完成双端测试后升级为客户端验证结论。
