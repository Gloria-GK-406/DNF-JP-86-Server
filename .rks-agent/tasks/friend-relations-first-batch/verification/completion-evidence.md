---
kind: rks-verification
decision: pass
---

# 完成验证：角色关系系统与好友第一批

**声明**：TaskContract rev 1（`../TaskContract.md`）的 SC-1 – SC-10，在分支 `feature/60-friend-relations` 的 HEAD `733aed4b1` 上全部满足。

**适用范围**：
- 全量测试跑在 `5436e0dfc` 的干净构建上（0 警告 0 错误）。`5436e0dfc..733aed4b1` 只改了 `SqliteCharacterIdentityRepository.cs` 的一段注释（2 行），不改变行为。
- 测试日志在 `artifacts/friend-relations/verify/rem-*.log`，不入库。`rem-build.log` 是修补测试构造参数之前那次失败的构建；修补后的构建在会话中直接确认为 0 警告 0 错误，测试即在其产物上运行。
- 全部带 `PVF_ARCHIVE_PATH`，指向主干 `artifacts/pvf-merged-20261111/Script.pvf`。

## 自动检查

| 项目 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告，0 错误 |
| Dfo.Server.Application.Tests | 6701 通过，0 失败，0 跳过 |
| Dfo.Server.Protocol.Tests | 2282 通过，0 失败，0 跳过 |
| Dfo.Server.Persistence.Tests | 466 通过，0 失败，0 跳过 |
| Dfo.Server.Host.Tests | 243 通过，0 失败，1 跳过（基线已有的跳过） |
| Dfo.Server.Networking.Tests | 218 通过，0 失败，0 跳过（含 golden 选角特征测试） |
| Tool/DbMigrate.Tests | 27 通过，0 失败，0 跳过 |

## 完整结果审查

`../reviews/change-review.md`：
- Round 1 结论 `changes_required`，阻塞项 B1 为按名查找有两套实现；
- 修正提交 `5436e0dfc`；
- Round 2 结论 `approved`；
- 新提的 N9（注释已说反）已在 `733aed4b1` 修正。

## 用户实机

- **测试环境**：测试服跑本分支，库为聊天测试库的 v5 副本；客户端为 `DNFClient-A21-protocol` 与 `protocol2`；测试流程见 `Document/tasks/friend-relations/用户测试流程.md`，共 16 步。
- **用户反馈**（2026-11-12）：「看起来没有问题，就是频道跟随还没做？其他的好像都没问题」。频道跟随 `0x0323` 本来就在 Scope Out（RQ-13，由 #60 承接），不算失败。
- **流程中写明的已知限制**，用户已看过、未提异议：
  - 同频道好友也显示频道号（AM-2）；
  - 加自己时客户端没有提示（AM-1，客户端对 0x01 静默）。

## 逐条结论

| SC | 结论 | 决定性证据 |
|---|---|---|
| SC-1 加好友（在线/离线、单向、错误、去重） | 满足 | `Application.Tests/FriendCommands/FriendCommandTests`、`Relations/CharacterRelationDomainServiceTests`、`Protocol.Tests/Game/UnitedFriend/FriendCodecTests`；用户实机。加自己时客户端无提示，属于已知限制，AM-1 已记录 |
| SC-2 删除 | 满足 | 同上（删除、反向保留、失败回 0x15）；用户实机 |
| SC-3 登录列表与三种在线状态 | 满足 | `SelectCharacterLifecycleTests.Friends`、`FriendEntrySequenceTests`、`FriendListBuilderTests`（ch 按 AM-2）；用户实机 |
| SC-4 上下线、升级、转职推送，只给观察者 | 满足 | `FriendPresencePushTests`、`SelectedSessionPartyCleanupTests`、`CleanupNotificationDeliveryTests`、`ReturnToSelectionTests`、`OrdinaryQuestEffectTests`（转职、觉醒事件）；用户实机 |
| SC-5 重登、重启后保持 | 满足 | `SqliteCharacterRelationDomainStoreTests`（重开库一致）；用户实机重登 |
| SC-6 黑名单增、删、列表、错误码 | 满足 | `Application.Tests/BlacklistCommands/BlacklistCommandTests`、`Protocol.Tests/Game/Blacklist/*`；用户实机 |
| SC-7 黑名单拦聊天与 1:1（77），解除后恢复 | 满足 | `Chat/SendChatCommandTests.Blacklist`（六类频道）、`OneToOneChat/ConversationCodecTests`；用户实机 |
| SC-8 单一 owner、只存 ID、被动、删角色清理 | 满足 | `Relations/RelationPassivityArchitectureTests`（带反例）、结构 v5 无名字列、`CharacterRosterCommandFlowTests`（`purge:True`）、`FriendPresencePushTests` 的删除用例；完整审查 Round 1 的约束核对 |
| SC-9 构建、测试、迁移、协议文档 | 满足 | 上方自动检查表；`DbMigrate.Tests` 4→5 与结构一致性；`protocol/` 七页 + catalog（证据标签见 `research/client-evidence.md`） |
| SC-10 测试服与编号流程，用户通过前不建 PR | 满足 | `tasks/TASK-07.md` 的 Result（端口、库、备份、status 200）；测试流程文档；用户反馈见上；尚未创建 PR |
