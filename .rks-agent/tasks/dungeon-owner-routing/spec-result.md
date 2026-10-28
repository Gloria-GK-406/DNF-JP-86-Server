---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 副本归属命令先定位、再单次路由，陈旧帧在入口统一丢弃

## Delivered

- 13 个命令都按同一结构处理：入口判一次陈旧、定位一次、只进一个通道（Boss 塔、幻影塔或普通）。
  - 涵盖的命令：0x002B 拾取、0x0028 角色死亡、0x0027 怪物死亡与物件破坏、0x0025 加载完成、0x002D 过图、0x0048 Eplp、0x0084 回城、0x000F 进入选图、0x0029 复活币、0x002A 放弃、0x000D/0x000E/0x0079 组队变更。
  - 0x000F 是唯一的具名例外：Boss 塔释放成功后，直接接城镇选图。
- 定位能力 `DungeonLocator` 以副本占用登记为依据，只询问登记指向的那座塔，并核对会话与 RunId；组队类命令按整队位置定位，Boss 塔优先。两塔通道收到定位结果后，不再自己查归属。
- 陈旧判定改为中性的 `GameRequestFreshness.IsStale`，`TowerProtocol` 已删除。
- 授权的行为变化：13 个命令的陈旧帧在任何位置都被丢弃。可观察的变化有：
  - 0x000F 在普通副本、幻影塔、城镇；
  - 0x0029 在各位置，包括 Boss 塔不再回拒绝帧；
  - 0x0027 物件破坏在普通副本、幻影塔、城镇。
- 结构守护 `DungeonOwnerRoutingSourceTests` 固定了陈旧判定、塔与登记查询、定位调用允许出现的位置，并附带反例。
- `Document/ProtocolCommands.md` 新增“副本归属分发与陈旧帧”一节，是这条规则的唯一归属。
- 迁移后失效的 `*PartyAttempt.Handled` 字段已删除，两塔重复的组队候选收集合并为一处。

## Verification

- `verification/completion-evidence.md`（decision: pass）
- 整体审查：`reviews/change-review.md`（approved）

## Repository Result

- Changed paths：
  - 生产代码：`Server/src/Dfo.Server.Protocol/Game/` 下的 7 个处理器类及其分部文件、`Dungeons/DungeonLocator.cs`、`GameRequestFreshness.cs`、两个 `*PartyCoordination.cs`；
  - 测试：`Server/tests/` 下新增表征、矩阵、定位、守护测试与共享登记替身，适配若干夹具；
  - 文档：`Document/ProtocolCommands.md`、`Document/LordTower-Design-20261019.md`；
  - 另有本任务目录下的过程产物。
- Spec baseline: `7adec85831f3b5e31358ee61568a845555480618`
- Rollback: not applicable
- Parent rollback required: no

## Successor Guidance

以下都不阻断本次完成：

- **#200 遗留的测试夹具**：`AvatarCompoundTests.Rejections_leave_all_inputs_unchanged(reason: "expire")` 只在有 PVF 时运行。它给装扮设置核心字段 `ExpireTime`，而 #200 之后装扮期限只读 `AvatarDetail.ExpireDate`，导致在真实 PVF 下失败，基准上同样失败。应改为设置装扮详情的到期日，不需要改生产代码。
- **范围外的疑似业务缺口**：结构守护的白名单固定了 6 个其他功能成员的现状，它们只按幻影塔判断，不判断 Boss 塔：
  - 整理仓库 `GameSortItemCommand.cs`、恢复疲劳 `RecoverStaminaSlice.cs`：只在普通副本与幻影塔中拒绝；
  - 宠物通知收件人、任务完成外观：只按幻影塔同局成员扩展收件人。

  是否为 Boss 塔接入时的遗漏，需要对照客户端行为确认。若要统一，建议把“是否在副本”收进领域查询，避免第二种口径。
- **测试夹具的盲区**：`DerivedDungeonParticipation` 按塔状态推出登记，因此看不到“塔里有座位、登记却缺失”的回退；目前由领域事务不变量、结构守护与真实依赖注入的集成测试兜住。建议为两塔各补一条走真实登记服务的端到端用例，并把测试驱动里复刻的路由改为经公开入口驱动。
- **A21 实机确认**：陈旧帧统一属于行为变化，只有自动测试证据。
- #187 保持开放：本次只覆盖其第 2 条中按当前位置分发的塔路由链；0x0010 入场按目标分发与其余条目仍由 #187 负责。
