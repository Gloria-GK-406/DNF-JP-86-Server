---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 普通地下城副本只挂在队伍上，人员只问队伍

## Delivered

- 普通地下城副本只与一支队伍关联（真实队伍，或单人的虚拟队伍）。副本成员是队伍当前成员中属于入场名单的角色，槽位与队长取队伍的值。副本不再持有 `ActiveParticipants` 和 `runsByCharacter`。
- `IPartyService.KeyOf` 回答角色属于哪支队伍。副本查找走“角色 → 所属队伍 → 该队伍的副本”。
- 个人离开（放弃副本、断线、退队、被踢）一律先退队，再清理本局状态；`EndAsync` 拒绝“仍在队伍中且队伍还有其他成员”的角色。新增 `EndRunAsync` 承担整队退出与队长选图回城的整局结束；非队长选图回城不再结束副本。各路径的逐人副作用与基准一致。
- 发起邀请、接受邀请的协议命令在任一方处于副本时拒绝，运行中的队伍不能增员；队伍服务不读取副本。
- 行为表征测试（领域层与协议层）在结构改动之前提交，结构改动之后断言零改动且仍通过。
- 接口注释与 `Document/ReactiveDungeonCellRuntime.md` 的“副本成员”一节描述新模型。

## Verification

- `verification/completion-evidence.md`（decision: pass）
- 整体审查：`reviews/change-review.md`（approved）

## Repository Result

- Changed paths：相对基准，`Server/src` 与 `Document` 共 18 个文件（+366/−229），`Server/tests` 共 72 个文件（+1884/−222），另有本任务目录下的过程产物。分支 `task/dungeon-participants-20261027`，未推送。
- Spec baseline: `1cff7218ddff1570dcd48eb07b779283ee7c6542`
- Rollback: not applicable
- Parent rollback required: no

## Successor Guidance

以下各项均未阻断完成，留给用户决定是否处理：

- **R4，开新局只结束同一队伍的旧副本**：基准做法是把入场者从任何副本中拆离。按 Spec 可达性说明，合法操作下入场者不会残留在别的队伍的副本里。整体审查建议接受现状，不加跨队伍扫描，因为那会把按角色定位副本的旁路加回来。如果将来需要防护，应在入场时拒绝并记日志。
- **整体审查的非阻断建议**：
  - S1：同步 `End(CharacterId)` 的线性回退查找，应限定为“已离队后的清理”用途，或收窄为测试辅助；
  - S2：在协议层为放弃副本和断线补上占用、生命周期、虚弱的断言；
  - S3：把 `PreparePartyEntry` 中的局部变量 `frozen` 改名；
  - S4：在“副本成员”一节补充 R4 的前提说明；
  - S5：更新 `Document/PartyKey-Dungeon-Membership-A21-Acceptance.md` 中“新加入队伍的 C”这一场景的期望。
- **残余风险**：
  - 断线时如果队伍清理本身失败，该角色的占用登记会保留到整局结束（已写入文档）；
  - 在不可达的中间态下，各结束路径记录生命周期的口径不一致；
  - 独自在副本中的角色新建真实队伍会让原副本变得不可达（该路径需要角色在城镇，所以不可达，与基准相同）。
- **既有问题（非本次引入）**：
  - 真实 PVF 下 `DimensionDungeonTests` 2 个用例因 7100 号地下城内容缺陷失败；
  - `IllusionTowerPublicFlowTests` 偶发“随机奖励装备缺少 `[minimum level]`”。
- **未做 A21 客户端实机验收**：之前 PartyKey 任务的实机验收表 `Document/PartyKey-Dungeon-Membership-A21-Acceptance.md` 仍然适用。

## Artifacts

- 表征与新增测试：`DungeonRunDomainServiceTests.Departures.cs`、`DungeonDepartureProtocolCharacterizationTests.cs`、`DungeonRunDomainServiceTests.PartyOwnedRuns.cs`、`DungeonPartyOwnedRunProtocolTests.cs`、`DungeonRunDomainServiceTests.OutOfRoster.cs`、`PartyCommandHandlerTests.RunningParty.cs`。
- 共享测试辅助：`Server/tests/Support/TestParties.cs`。
