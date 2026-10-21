---
kind: rks-verification
decision: pass
---

# 完成验证：「会话仍有效」「角色在线」收成具名判定

**验证对象**：分支 `task/refactor-session-20261021`，HEAD `b01f6841`。依次包含实现提交 `97c5af5d`、第一轮审查修复 `7b55d4bf`、第二轮审查修复 `a29d7586`，以及测试断言收紧 `b01f6841`。Spec 基线为 `ea6a1544`。

## AC 与证据

| AC | 结果 | 证据 |
|---|---|---|
| AC-001 检索 | 见下文 | `logs/ac001-scan.txt`（在 HEAD 上运行） |
| AC-002 判定在各状态下的答案 | 通过 | `Sessions/SessionCurrencyChecksTests`（8 条）：选中、在世界、隔离、连接撤销（入世前和入世后）、被替换、已释放、其他代数，覆盖三个重载 |
| AC-003 路径清单与流程测试 | 通过 | `plan.md`「执行后的定稿」：工作流 5 处、清理 3 处、RQ-014 收紧行为表。选角、顶替、断线、拆除、补偿的既有测试全部通过 |
| AC-004 收紧回归 | 通过 | 玩法写操作：`NpcShopPurchaseTests.A_purchase_after_the_connection_is_revoked_is_refused_before_teardown`。投影与投递：`NotificationInWorldTests`。跨账号目标：`OtherCharacterDetailQueryDomainServiceTests.ATargetStillEnteringTheWorldIsNotCurrent` / `ATargetWhoseConnectionWasRevokedIsNotCurrent`。选角工作流：`SelectionWorkflowCurrencyTests`（2 条）。清理路径不收紧：`CreatureRuntimeDomainServiceTests.Leaving_after_the_connection_is_revoked_still_settles_the_stomach_and_queued_experience` |
| AC-005 投递 | 通过 | `NotificationInWorldTests`（5 条）：精确会话人群撤销后不投递，解析后撤销计为 Gone；角色人群在尚未入世、撤销时不投递，连接不变时照常投递 |
| AC-006 构建与测试 | 通过 | `logs/final-build-test.txt`（HEAD b01f6841）：构建 0 警告 0 错误。Application.Tests 只有基线已有的 2 个 `DimensionDungeonTests` 失败，与 `baseline.md` 一致。全量运行中另有 Networking.Tests 2 个、Host.Tests 1 个失败，都是 Connection refused 或端口类问题；单独复跑两轮全部通过（`logs/final-isolated-reruns.txt`），在 a29d7586 的全量运行中也全部通过，判定为满载下的假红。测试数相对基线：Application.Tests 从 4323 增至 4342（+19），分别是 SessionCurrencyChecks 8、NotificationInWorld 5、NpcShop 撤销 1、OtherCharacterDetail 2、SelectionWorkflow 2、宠物撤销后离场 1；其余项目的测试数不变 |
| AC-007 文档 | 通过 | `AGENTS.md`：「会话还有没有效只问具名判定」一段、`Find…` 的剩余用法清单、通知人群一节中的示例 |
| AC-008 issue | 通过 | https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/209 |

## AC-001 检索结果

以下各类都在 `Server/src` 中检索，结果如下：

- 会话 `Find…(…) ==/!=` 比较：0 处。
- `IsActiveExact`：0 处。
- `OnlineUserState.CharacterActive` 读取：1 处，是 `OnlineStateRegistry` 本身把选中记录投影到在线用户快照，不是当前性用途。
- 纯当前性 helper：0 处。剩下的 9 个 helper 都把当前性与其他事实组合在一起，Spec RQ-009 允许保留：
  - `ClientSettings.IsCurrent`：连接代数、尚无会话；
  - `ChurchRooms.IsPresent`、`HousingState.IsPresent`：区域租约；
  - `MarriageEligibility.IsCurrent`：架构判据规定的转交点；
  - `NotificationBus.IsStillHere`：人群形态；
  - 两塔的 `Current`：塔局代数；
  - `DungeonCardPresentationCoordinator.IsCurrent`：副本 RunId；
  - `DungeonLoadingProjectionDelivery.IsCurrentSession`：连接比较。

剩余的会话 `Find…` 调用按以下类别保留：

- **(a) 请求方按自己这条入站连接解析会话**：131 处，启发式归类。
- **(b) 选角工作流和清理路径**：8 处。
- **E3 其余 18 处**，逐条如下：

| 位置 | 类别 | 理由 |
|---|---|---|
| `ClientSettingsDomainService.cs:94` | (c) | 判定「这条连接上还没有选中会话」 |
| `GmCeraDomainService.cs:21` | (d) | 经通知人群投递，投递层判定 `IsInWorld` |
| `CreatureRuntimeDomainService.cs:27` | (d) | 实例运行时在使用时由 `EnsureActive` 判定；离场时按 `StandingOf` |
| `CycleDungeonEntryDomainService.cs:50` | (d) | 紧接着判定 `IsInWorld` |
| `DungeonAdmissionDomainService.cs:54`、`:59` | (d) | 紧接着判定 `IsInWorld` |
| `AccountWalletChangedHandler.cs:14` | (d) | 读库之后复查 `IsInWorld` |
| `NameTagExpiredSubscriber.cs:15`、`ExpiredItemsProjection.cs:27` | (d) | 与事件身份逐字段比对后，判定 `IsInWorld` |
| `RentalSlice.cs:164` | (d) | 紧接着判定 `IsInWorld` |
| `MoveItemSpaceSlice.cs:401` | (d) | 请求方自己；下游 `AdvanceAsync` 判定 `IsInWorld` |
| `BossTowerPartyCoordination.cs:190`、`IllusionTowerPartyCoordination.cs:188` | (e) | 被影响的会话集合，只用于隔离与关闭；关闭前过滤 `IsInWorld` |
| `BossTowerPartyCoordination.cs:353`、`IllusionTowerPartyCoordination.cs:351` | (e) | 队伍身份一致性快照，只比对账号与线上号 |
| `MarriageLovePointSlice.cs:62` | (e) | 只用于加伴侣账号锁 |
| `TownAreaProjectionDelivery.cs:429` | (e) | 只用于计算要锁的旧区域 |
| `TournamentSlice.cs:173` | (f) | 比武淘汰不以在线为前提；发送前判定 `IsInWorld` |

## 有意的行为与断言调整

- **收紧**：玩法、投影、投递不再接受尚未入世或连接已撤销的会话；选角工作流不再接受连接已撤销的会话。逐条见 `plan.md` 的 RQ-014 表。其中队长踢人、移交队长的目标连接已撤销时，操作会被拒绝。
- **断言改动 1 处**：`OnlineIdentityTests.A_revoked_connection_leaves_its_session_waiting_for_cleanup`。原断言是「撤销后 `IsInWorld` 仍为真」，现改为断言「`StandingOf` 不为 null」；`IsInWorld` 为假与 `ConnectionRevoked` 为真两条断言保持不变。
- **测试夹具**：约 30 个测试类原先凭空 `Select` 会话，现改为按真实流程走「登记连接 → 认证 → 选中 → 进入世界」，断言不变。

## 限制

- 自动化测试不能替代 A21 实机验收。
- 收紧的测试证据是按类别取的样本；跨账号目标、定时任务、事件订阅者只有清单，没有逐项测试。
- 宿主启动类测试在满载时会因连接被拒而偶发失败，单独复跑可以通过（见 AGENTS.md「拉真实进程……会假红」）。
- 留给后续处理的问题：#209（写操作按数据库再查归属）；城镇区域租约只按「角色 + 连接代数」区分，不同连接上的同一角色会拿到相同租约，这是本次之前就存在的设计问题，未处理。
