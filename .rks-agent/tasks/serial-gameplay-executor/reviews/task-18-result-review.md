---
kind: rks-review
target: tasks/TASK-18.md
verdict: approved
---

# TASK-18 结果快速审查（独立审查者）

- 审查对象：`tasks/TASK-18.md`，合同 Revision 1，含「主控方裁定（执行前）」1～6 与第 1 轮 Result；改动为基准 `6a3bff70` 之上的未提交差异。评审者按调用方要求只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 快速审查，加 Code 标准。
- 评审者自跑：Application.Tests 中 `GameplayWriteEntryTableTests`、`GameplayWriteAssertionTests`、`CommandBusExecutorTests` 与承接用例，共 47 条，全部通过。

## 核心结论：读写分类表

63 个「读」成员逐个读了方法体，没有一个应改为「写」。

| 类型 | 表中读 | 确认为读 | 应改为写 |
|---|---:|---:|---|
| `OnlineStateRegistry` | 15 | 15 | 无 |
| `LoginSessionRegistry` | 3 | 3 | 无（`get_CloseRequests` 见 N2） |
| `DungeonRunDomainService` | 8 | 8 | 无 |
| `CreatureRuntimeDomainService` | 3 | 3 | 无 |
| `PartyService` | 3 | 3 | 无 |
| `TownAreaDomainService` | 3 | 3 | 无 |
| `DungeonParticipationDomainService` | 1 | 1 | 无 |
| `DungeonCardPresentationCoordinator` | 0（豁免 2） | — | 无（豁免见 N3） |
| `BossTowerDomainService` | 5 | 5 | 无（`Find`、`CanShop` 见 N1） |
| `IllusionTowerDomainService` | 4 | 4 | 无 |
| `TournamentDungeonDomainService` | 6 | 6 | 无 |
| `AltarDungeonEngine` | 5 | 5 | 无 |
| `AltarStateStore` | 2 | 2 | 无 |
| `OnlineCharacterRuntimeDomainService` | 5 | 5 | 无 |
| **合计** | **63** | **63** | **0** |

## 发现（均为 P3，不阻断）

- **N1**：两塔 `Find`／`CanShop` 的分类理由写作「登记空参与者」，对 6 个成员中的 4 个成立；`BossTowerDomainService.Find`（`:51-52`）与 `CanShop`（`:263-264`）登记的是在 `Changes.PrepareAsync`（`:430-455`）中校验的读集。归类仍成立：读集只在提交时生效，而执行器外唯一的可写环境事务 `BeginSnapshot` 从不提交；改判为写会误伤执行器外合法的无事务读。修法：改措辞，并注明是对计划 §2.3 规则的有意偏离。
- **N2**：`LoginSessionRegistry.get_CloseRequests`（`:36`）的备注引用附录 B 生产者一行，但归类为「读」；它交出的 reader 被 `LoginSessionMaintenanceService.CloseSupersededAsync`（`LoginSessionRuntime.cs:62`）在执行器外出队。实际影响为零（玩法动作在 `lifecycle:close-superseded` CMD 内）。修法：改备注或改为豁免，并登记补名。
- **N3**：卡牌协调器 `Dispose`（`:401`）只调 `Stop()`，即 `DrainAsync` 的第一步，只改协调器自身计时标志；认可它被附录 B 生产者一行覆盖，不触发停止条件 3。建议 T-009 改附录 B 时把 `Dispose` 写进该行。
- **N4**：Host 方案 A 的接线（夹具 `Append` → `ObserveHostOutput` → 写 `exe:` 记录）没有判据；「控制台打印异常类型名」的实跑证据用的是替身异常，属间接证据。修法：补接线用例，或并入 F-61 残余。

## 已核实无问题

- 登记参与者在执行器外不产生状态变化，按读处理符合 RQ-061 字面与 RQ-010。
- 成员枚举覆盖属性、事件、索引器、静态成员与显式接口实现；`AltarStateStore` 补入成立，无同类遗漏（`TowerInventoryDomainService` 不在附录 F 名单，已在 F-62 登记）。
- 入口挂点：`Begin` 写断言在「已有事务」检查之前；`BeginSnapshot` 与 `Begin` 逐语句相同只少断言；7 处 `new DomainTransaction(`、`BeginSnapshot` 唯一调用方与「快照不提交」都由源码判据钉住且各有反例；`ChangeAsync`、`AloneAsync`、`WithinAsync` 回退分支的断言位置正确。
- 泄漏流：判定顺序为空命令、`DispatchDepth`、泄漏、执行器，均以失败的 `ValueTask` 返回；承接用例完整承接被删用例的意图。
- 表达式体改块体：两塔 ValueTask 成员的生产调用方都直接返回或直接 `await`，同步与异步抛出的差别只出现在执行器外的误用路径。
- 冲突用例：`AuthenticationConsumptionTests` 删除的断言所守的「两个未提交绑定重叠」在单执行器下结构上不可能；`Leaving_the_room…` 删除理由与后继成立；X1、X2 有变异证据；§15.4 加法复算无误。
- 数字：8213 ＝ 通过 8191 ＋ 失败 2 ＋ 跳过 20；71 条取证按类、按 kind、按程序集三种加法都对，正向对照 7 个程序集各 1 条。
- 边界：生产差异 20 个文件都在 Owned boundary 内；非冲突测试改动都在白名单形状内。

## 主控方处置

见 `tasks/TASK-18.md`「主控方处置（评审后，提交前）」：N1、N2 改措辞（分类不变），N2、N3 登记到 F-62，N4 并入 F-61；改后 `GameplayWriteEntryTableTests` 9／9 通过。
