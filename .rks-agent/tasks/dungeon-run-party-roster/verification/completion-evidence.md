---
kind: rks-verification
decision: pass
---

# 完成验证：普通地下城副本只挂在队伍上（#202）

## 声明

分支 `task/dungeon-participants-20261027` 在 `4dc69b65` 满足 TaskContract（revision 1）的 SC-1～SC-8 与 Spec 的 AC-014。

- Spec 基准为 `1cff7218`。
- 整体审查覆盖 `1cff7218..99e7ebc7`，结论 approved（`../reviews/change-review.md`）。其后只有 `4dc69b65` 一次提交：给一个依赖 PVF、本机此前一直跳过的测试补上入场前组队，断言未改。
- 本声明只包括自动化证据，不包括 A21 客户端实机验收；后者不在契约成功条件内。

## 条件与证据

| 条件 | 结论 | 决定性证据 |
|---|---|---|
| SC-1 单一成员来源，无第二份名单，无回退路径 | 满足 | 整体审查第 1、2 项及 TASK-005 Review Round 1 的代码检查：`ActiveParticipants`、`runsByCharacter` 已删除，只剩 `runsById` 与 `runsByParty`；成员只由 `Participants`（队伍当前成员 ∩ 入场名单）得出；`Released`、`HoldsRunState` 只表达本局状态清理；唯一构造器要求队伍服务。行为测试：TASK-001 转让队长用例、TASK-005 新增 `PartyOwnedRuns` 用例。 |
| SC-2 经所属队伍找副本，离队后找不到 | 满足 | TASK-005 新增 AC-004 用例（真实队伍、虚拟队伍、退队后、开新局后），以及 `PartyServiceTests` 的 3 个 `KeyOf` 用例（TASK-004）。全部在下方完整运行中通过。 |
| SC-3 四条个人离开路径 | 满足 | 领域层：TASK-001 的 `Departing_member_...`（退队、被踢、队长离开）。协议层：TASK-002 的放弃副本、断线、退队、被踢用例，钉住帧序、收件人、投递登记、队伍与副本状态。重构后这些用例断言零改动且通过（整体审查第 3 项）。 |
| SC-4 整局结束路径保持队伍不变 | 满足 | TASK-001 的清场回城、全灭、入场中止、开新局；TASK-002/003 的整队退出与队长选图回城（含逐人生命周期、占用释放、失败虚弱）；TASK-005 的 AC-008（非队长回城不结束）。 |
| SC-5 运行中队伍不能增员，图外角色无资格 | 满足 | TASK-006 的 `PartyCommandHandlerTests.RunningParty`（3 个 Theory，含对照组）与 `DungeonRunDomainServiceTests.OutOfRoster`。守护经变异检查证实有效；`Application/Parties` 不引用副本服务。 |
| SC-6 存续副本恰一名队长，不抛未处理异常 | 满足 | TASK-005 新增 AC-010 用例，覆盖 8 条路径在清理过程中与清理后的视图读取。 |
| SC-7 注释与文档不再称名单冻结 | 满足 | TASK-007 加补救提交 `99e7ebc7`：`grep -rn -i 'frozen participant' Server/src` 无结果；`Document/ReactiveDungeonCellRuntime.md` 的“副本成员”一节为唯一归属，整体审查第 6 项确认与代码一致。 |
| SC-8 / AC-014 完整自动测试 | 满足（附归因） | 见下节。 |

## 完整测试（`4dc69b65`，工作区只有本验证产物未跟踪）

**不带 PVF**：`logs/full-test-no-pvf.log`

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Pvf | 73 | 9 | 0 |
| HttpApi | 59 | 0 | 0 |
| Infrastructure | 655 | 348 | 0 |
| Protocol | 1739 | 60 | 0 |
| Networking | 189 | 20 | 0 |
| Persistence | 484 | 0 | 0 |
| Application | 5497 | 493 | 0 |
| Host | 199 | 12 | 1 |

Host 的 1 个失败是 `LauncherHttpHostStartupTests.DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener`：`TaskCanceledException`，栈根为 `WaitForTcpListenerAsync`，在满载全量运行中耗时 20 秒。归入 `Document/BuildAndTest.md` 的“进程与 socket 超时”模式，依据如下：
- 按该模式要求单独运行 Host.Tests，200 通过、12 跳过、0 失败（`logs/host-tests-isolated.log`）；
- 该用例只验证宿主 HTTP 组件关闭时的监听，与本次改动的副本、队伍、协议命令没有交集。

**带真实 PVF**：`artifacts/refactor-session-20261021-closeout/preserved/Script.pvf`，即上一轮大重构收尾使用的档案；日志 `logs/full-test-pvf.log`。

除 Application 外各项目全部通过，统计如下：

| 项目 | 测试总数 | 通过 | 跳过 |
|---|---|---|---|
| Pvf | 82 | 82 | 0 |
| HttpApi | 59 | 59 | 0 |
| Protocol | 1804 | 1804 | 0 |
| Persistence | 484 | 484 | 0 |
| Infrastructure | 1003 | 1001 | 2 |
| Networking | 210 | 210 | 0 |
| Host | 212 | 212 | 0 |

Application 共 6094 个：6091 通过、3 失败。

- 2 个 `DimensionDungeonTests`（`Fusion_real_content_...`、`All_six_real_cells_...`）：`Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'`。用同一 PVF 在基准 `1cff7218` 的临时工作树上单独运行，同样以相同消息失败（`logs/baseline-1cff7218-dimension-pvf.log`），属于 PR #211 已记录的既有内容缺陷，不是本次引入。
- 1 个 `IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter`：`InvalidDataException`，消息含 `[minimum level]` 与 `required field is missing`，栈根为 `PvfEquipmentDefinitionSource.RequiredInt`。用例名、异常、栈根三项都与 `Document/BuildAndTest.md` 的“PVF 随机奖励内容缺失”一致，单独复跑 5 次全部通过（`logs/illusion-rerun-5x.log`）。按该文档，这仍是生产健壮性缺口，但不是本次引入。
- 之前在 PVF 下跳过的 `AltarLifecycleTests` 共 11 个用例全部通过，其中包括 TASK-004 调整了退队顺序的 `Member_departure_...`。

`4dc69b65` 的来由：在 `99e7ebc7` 上第一次带 PVF 运行时，`DungeonVisitDomainServiceTests.Real_consciousness_route_...` 因多人入场前未组队，抛出 “The entry roster no longer matches the party”。这是 TASK-004 迁移构造时，PVF 用例在本机被跳过而漏改的夹具。补上组队后该用例通过，并在本次完整运行中通过。

## 未纳入本验证

- A21 客户端实机行为（不是契约成功条件）。
- 整体审查的非阻断建议 S1～S5、残余风险 R-a～R-e，以及 R4（开新局不跨队伍拆离）的“接受并记录”决定，在 `spec-result.md` 中列出，交由用户决定后续处理。
