---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 「会话仍有效」「角色在线」收成具名判定

## Planning Choice

选 direct。这次改动是一组接口，加上大量调用点替换，全部落在同一个编译边界内。拆成 Task Unit 的话，中间状态不是编译不过，就是只能靠全量测试判断，没有一个单元能单独验收。

工作分两步：
- 第一步：换成具名判定，归级沿用原口径。
- 第二步：收紧口径。

两步之间跑一次全量测试作为停靠点。全部完成后只做一次实现提交。

## Approach and Shared Decisions

**DP-001 判定的实现**

判定在 `Users/OnlineStateRegistry.cs` 的 `sync` 内实现，`SelectedSessionDomainService` 只做门面。

- `IsSelected` 的实现是 `FindExact(session) is { } s && !IsRevoked(s)`。
- `IsInWorld` 改用今天 `IsActiveExact` 的方法体，抽成私有方法 `InWorldExact(selection)`。
- `TryGetInWorld` 有三个重载，都在同一次加锁内按键找到 `Selection`，再用 `InWorldExact` 判断。
- `UserDomainServices` 删除 `IsActiveExact`，`IAuthenticatedGamePresenceQuery` 只保留 `SnapshotAccounts`。

**DP-002 第一步：换名不收紧**

1. 用 `rewrite_currency.py` 改写「key 取自会话自身」的「查再比」写法，替换成 `IsSelected` / `!IsSelected`。试运行约 267 行。
2. 脚本列为手工处理的约 53 行逐处判断，规则按 RQ-005。
3. 清理路径上的「查再比」改成 `StandingOf(session) is not null`。这是原语义，不能用 `IsSelected`。
4. 在线用户组合和 `IsActiveExact` 改成 `IsInWorld`；被包含的判定、纯当前性 helper 一并删除（RQ-007～RQ-009）。

`IsSelected` 比「记录在」多了「连接未撤销」一条。这一步里，只有连接已撤销的会话会受影响，属于 RQ-011 授权的收紧。如果停靠点测试暴露出这类差异，逐条记录。

**DP-003 测试替身**

- `ISelectedSessionDomainService` 的替身按自己持有的状态实现新成员：
  - `IsSelected` 用替身自己的 `Find(session.AccountId) == session`；
  - `IsInWorld` 沿用替身已有的在世界状态；
  - `TryGetInWorld` 用替身自己的 `FindBy…` 加 `IsInWorld`。
- `IAuthenticatedGamePresenceQuery` 的替身删除 `IsActiveExact`。

**DP-004 第二步：收紧**

1. 非选角工作流路径上，所有关于会话的 `IsSelected` 改为 `IsInWorld`。
2. 「查不到就当不在线」的查找改为 `TryGetInWorld`（RQ-006）。涉及旁人、原来会抛异常的，改为跳过（RQ-014a）。
3. 之后出现的失败逐个判断：
   - 测试只登记了选中、没有进入世界，属于夹具问题，改用 `TestSessions.InWorld` / `EnsureInWorld`；
   - 生产路径确实在会话尚未入世时调用，说明它属于选角工作流路径，补进清单并写明原因。

**DP-005 投递**

`NotificationAudienceResolver` 与 `NotificationBus.IsStillHere` 按 RQ-015 修改；投影里「取出再比较」的写法按 RQ-016 修改。

**执行偏差（T-005 停靠点之后）**

第一步跑完全量测试，排除基线的 2 个后还有 46 个失败。抽查结果是测试夹具凭空 `Select` 会话、没有登记它的连接；生产中不存在这种选中记录，`IsSelected` 会把它当作连接已撤销。第二步收紧之后，这些夹具还要再补「进入世界」，所以改为先完成第二步的生产改动，再对两步的失败统一分诊，避免同一批夹具改两遍。

翻转的依据：同一条连接的入站报文按顺序处理，选角 handler 执行期间，这条连接上的下一条报文不会被处理。所以请求方自己连接上的玩法 handler 看不到 `Selected` 阶段，收紧只影响跨账号目标、非命令路径和连接已撤销的窗口。

## 路径清单（初始，执行中补全）

| 类别 | 位置 | 判定 |
|---|---|---|
| 选角工作流 | `Protocol/Game/SelectCharacter/SelectCharacterHandler.cs`（`EnsureCurrent` 改为 `IsSelected`）、`SelectCharacterPhaseCommands.cs`、`EnsureDailyContractPuppetsCommand.cs`；`MarriageHousing/HousingSlice.cs` 中 `InitializeHousingCommand` 的处理器；各阶段命令调用的领域服务方法 | 本会话用 `IsSelected`；查别人的地方用 `TryGetInWorld` |
| 选角工作流中已是在世界口径的 | 三种门票补充命令（`TauKingdom`、`SouthernDale`、`BossTower` 的 `RefillOnline…Ticket`） | `IsInWorld` |
| 清理 | `Application/Sessions/SelectCharacterLifecycleServices.cs`（拆除部分）、补偿处理器、`Protocol/Game/OnlineUserConnectionObserver.cs`、`ReturnToSelection/ReturnToSelectionCommand.cs` | `StandingOf` / `ReleaseExact`；保留的查找 (b) |
| 跨账号目标与非命令路径（RQ-014） | 见下表 | `IsInWorld` / `TryGetInWorld` |

### 执行后的定稿

**选角工作流中保留 `IsSelected` 的调用点（5 处）**

| 位置 | 说明 |
|---|---|
| `SelectCharacterHandler.EnsureCurrent` | 每一步之间检查；口径与原「当前选中且连接仍属于该账号」一致或更严 |
| `EnsureDailyContractPuppetsCommand` | 发布前阶段 |
| `InitializeHousingCommandHandler` | 发布前阶段 |
| `SelectCharacterSessionEntryService.Publish` | 发布本身 |
| `ClientSettingsDomainService.IsCurrent` | 热键激活和设置上传共用；另外比对在线用户代数，并允许尚无会话 |

**清理路径改用 `StandingOf` 的调用点**

- `SelectCharacterLifecycleServices` 拆除中的 `ownsSelectedCharacter`
- 断线观察者 `CleanupSelectedHeldAsync`
- `CompleteReturnToSelectionCommandHandler`

**收紧后的行为（RQ-014）**

以下各项都有共同的收紧：尚未入世，或连接已撤销的角色，不再算在线。

| 路径 | 收紧后的行为 |
|---|---|
| 组队邀请、回应邀请、踢人、移交队长的目标（`PartyCommandSlice`） | 目标不在世界时，按「目标不在线」处理 |
| 查看他人详情（`OtherCharacterDetailQueryDomainService`）、称号簿主人、求婚对象与申请人 | 返回目标不在当前 |
| 入场时同城旁人、PvP 大厅成员（`SelectCharacterLifecycleServices`） | 跳过这个人，不再抛异常（RQ-014a） |
| 拆除通告与队伍目录的同伴收件人 | 不投递给不在世界的同伴；离开者本人仍按自己的连接直接发送 |
| 通知人群解析与投递（`NotificationAudienceResolver`、`NotificationBus`） | 不解析、不投递 |
| 定时任务：虚弱恢复、比武奖励扫描、各类配额日刷新、在线过期物品清理、三种门票补充 | 跳过。名牌过期、在线过期物品清理、门票补充的 `catch` 过滤改为 `!IsInWorld` 之后，会话连接撤销时出现的失败也按「会话已离开」静默跳过 |
| 婚礼参与者与来宾的投影（`MarriageEligibility.InWorldSessionOf`）、婚礼结算的两位新人 | 不投影；新人缺席时婚礼结算照旧失败 |
| 副本准入时情侣副本或镜像副本的队员、循环副本的请求方、单人比武参与者 | 判为不满足前置条件 |
| 宠物离场结算（`CreatureRuntimeDomainService.LeaveAsync`，含饥饿与排队的房间经验） | **不收紧**：属于清理路径，改问 `StandingOf`，连接撤销后照常结算 |
| 两塔提交前对旁人的在世界检查（`BossTowerDomainService`、`IllusionTowerDomainService`） | 旁人的连接恰好在变更与提交之间被撤销时，这次提交失败；离开者的离塔会在拆除中失败。窗口很窄，接受，不另行处理 |
| 事件订阅者与投影：外观设置、名牌过期、过期物品、钱包变化、两塔、祭坛、镜像、各副本投影 | 不投影 |

**没有找到合法调用方会因此改变行为的路径。** 依据是：同一条连接的报文按顺序处理，请求方自己的玩法 handler 在选角执行期间不会被处理。

## Work and Dependencies

| ID | Work | Covers | Depends on | Check |
|---|---|---|---|---|
| T-001 | 先写判定的单元测试，覆盖 AC-002（`tests/Dfo.Server.Application.Tests/Sessions/SessionCurrencyChecksTests.cs`），确认为红 | RQ-001～RQ-004 | — | 编译不过或失败 |
| T-002 | 实现 DP-001，更新接口与门面，删除 `IsActiveExact` | RQ-001～RQ-004 | T-001 | T-001 转绿 |
| T-003 | 按 DP-002 处理生产代码 | RQ-005、RQ-007～RQ-009、RQ-012 | T-002 | 生产代码编译通过 |
| T-004 | 按 DP-003 处理测试替身与测试代码 | RQ-010 | T-003 | 全部测试项目编译通过 |
| T-005 | 第一步停靠点：完整构建 + 全量测试 | — | T-004 | 与基线一致，差异逐条说明 |
| T-006 | 按 DP-004 收紧，同时补全路径清单与 RQ-014 清单 | RQ-006、RQ-011、RQ-013、RQ-014、RQ-014a | T-005 | 全量测试只剩基线失败 |
| T-007 | 按 DP-005 处理投递，编写 AC-005 测试 | RQ-015、RQ-016 | T-006 | 新测试通过 |
| T-008 | 编写 AC-004 的收紧回归测试 | RQ-011 | T-006 | 新测试通过 |
| T-009 | 更新文档，创建 issue | RQ-017、RQ-018 | T-006 | 文档差异、issue 链接 |
| T-010 | 运行 AC-001 检索并做保留查找的归类表；完整构建与全量测试；写入证据 | AC-001、AC-006 | T-007～T-009 | 检索结果符合 AC-001，测试只剩基线失败 |

## Verification

- 基线（`ea6a1544`）：`verification/baseline.md`。
- 完成时：按 AC-001～AC-008 逐条取证，写入 `verification/completion-evidence.md`。
