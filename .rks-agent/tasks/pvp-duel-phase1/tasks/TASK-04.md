# TASK-04 — 房间领域核心（等待阶段、邀请、开局、互斥、启动校验）

## Task Unit Contract

- Revision: 1
- Outcome and references: `PvpRoomDomainService` 成为事务化的房间 owner，交付等待阶段全部操作、决斗邀请、开局（冻结参战名单进入 Loading）、与队伍互斥、进退房的城镇位置变化；权益改按频道身份取并加显式启动校验（Plan A1）；截止时间唤醒接口与调度器骨架。Plan T-004 与 A1；Spec RQ-010–015、RQ-020–021、RQ-050（启动校验部分）、RQ-071（房间部分）、RQ-074；AC-010–013、AC-020（领域部分）、AC-050（启动部分）、AC-070（普查部分）。
- Owned boundary: `Abstractions/Pvp/**`、`Application/Pvp/**`、`Abstractions/World/ITownAreaDomainService.cs`（`TownAreaDepartureReason` 加成员及全部 switch）、`Application/Sessions/SelectCharacterLifecycleServices.cs` 与 `SessionAdjacentEntryDomainService.cs` 中读取房间处（随新模型迁移）、`Data/**` 中随之调整的入场/同步状态记录、宿主启动校验挂接点（Host 或 GameServer 中现有启动前检查位置）；测试 `Application.Tests/Pvp/**`、写入口表、状态普查、受影响的 Sessions 测试。**不做**：对局阶段推进（加载完成之后）、结算、离场清理接入、停服、任何协议切片（TASK-05、TASK-06）。
- Required checks: 构建；`Application.Tests` 的 Pvp、Sessions、Execution（写入口表、状态普查、写断言）、Commands 架构；Host.Tests 架构与启动类测试；新增领域测试覆盖 AC-010–013、AC-020 领域部分、事务回滚（房间变更随事务回滚而不生效）、启动校验失败反例；与基线对比无新增失败。
- Dependencies: TASK-02、TASK-03
- Result review: required — 房间模型与结果类型是 TASK-05、TASK-06 的共享契约；事务参与者写错会造成写库失败后房间仍前进，只有专门的回滚测试能暴露。

## Detailed Plan

### 共享模型（`Abstractions/Pvp/`，TASK-05/06 依赖，语义固定，命名可贴合风格）
- 身份：`PvpRoomId(Guid)`、`PvpMatchId(Guid)`；线上房间号 `ushort Number` 由 owner 在同监听端口内分配（取最小未用正整数），只是显示编号。
- `PvpRoomPhase { Waiting, Loading, PeerLinking, Fighting, AwaitRank, AwaitResult }`（本单元只产生 Waiting 与 Loading）。
- 座位 8 个：每个座位为 空 / 关闭 / 占用（角色 ID、精确会话 `SelectedSessionIdentity`、队伍 0/1/2、是否准备）。
- 不可变快照 `PvpRoomSnapshot`：Id、Number、ListenerPort、频道身份、名称（nameType + bytes）、是否有密码（密码字节只在 owner 内部比对，不出现在快照）、MapIndex、Mode（`PvpBattleMode`）、房主座位、座位、Phase、`Version`（每次提交 +1）、`Origin`（一期只有 PlayerCreated）、`Match`（开局后：MatchId、冻结名单（座位、角色、会话、队伍）、阶段截止时间）。为 TASK-05 预留：`Match` 内加 `ModeState` 槽（一期为空），存活/击杀/逃离/终局结果由 TASK-05 扩展。
- 结果：封闭层级 `PvpRoomOutcome`（`Refused(PvpRoomRefusal reason)` / `Changed(PvpRoomSnapshot before?, PvpRoomSnapshot after?, ...)`），以及需要协议投影的附带信息（被移出座位的角色、城镇离开/返回结果）。`PvpRoomRefusal` 为领域枚举（WrongPassword、Full、NotWaiting、AlreadySeated、InParty、NotOwner、UnsupportedMode、InvalidMap、NotAllReady、UnbalancedTeams、TooFewMembers、InviteExpired、Blacklisted 等），线上错误码映射留给 Protocol（TASK-06）。
- 删除旧 `PvpRoom` 记录与 `Offer`/`Withdraw`/`OfferedOn`；读面改为 `IPvpRoomQueryDomainService`：`RoomsOn(listenerPort)`（只返回已公布房间）、`Find(PvpRoomId)`、`FindSeated(CharacterId)`、`IsSeated(CharacterId)`。`PlanPvpAsync`、`SessionAdjacentEntryDomainService` 改用快照（房间快照记录 `PvpRoomSnapshotState`/`PvpRoomState` 字段随之调整，线上字节布局 TASK-06 再定，本单元保证信息齐备）。

### 事务化
- 照 `IllusionTowerDomainService.Changes`：`Pending => accessor.Current ?? throw` 后 `Enlist(this, () => new Changes(this))`；读在有事务时看本事务暂存；`Prepare` 乐观校验 before 未变、占座会话仍是 before 中记录的会话；`Commit` 应用并维护 角色→房间 索引与房间号分配，然后对有截止时间变化的房间调用 `IPvpDeadlineWake.Wake()`；`Rollback` 丢弃；`CommitCanFail => false`；无事务即抛。写方法 `GameplayWriteAssertion.Writing`；进写入口表与状态普查。

### 操作（写面 `IPvpRoomDomainService`）
- `Create`、`Enter`（含密码；邀请接受也走同一入房逻辑，但不产生 CMD 成功体标记）、`SetSeat`（换队 / 踢人 / 退房 / 关开座位；只剩一名玩家时保留至少一个可加入空位；房主退房由下一位成员继任，无人则销毁）、`SetMode`（只接受能力表 `Supported`，按 `TeamAssignment` 重新分队）、`SetMap`（规则服务校验下标）、`SetReady`（房主准备时检查成员≥2、非房主全部准备、`RequiresBalancedTeams` 时两队人数相等；通过则生成 MatchId、冻结名单、进入 Loading 并设加载截止 60s）。
- 进房/建房：校验不在队（读 `IPartyService`）、不在其他房间；经 `ITownTransitDomainService.LeaveTown(..., TownAreaDepartureReason.PvpRoom)` 离城，结果放入结果层级供协议投影。退房/被踢/销毁：经 `ReturnToTownAsync` 回到原落点，结果同样附带。若该调用要求异步，写方法改为 `ValueTask` 返回。
- 邀请：`Invite(inviter session, target session)` 记录（邀请人房间、双方精确会话、过期时间 30s、token）；`AcceptInvite` 重新校验房间仍 Waiting、未满、未过期、双方会话未变，再按入房逻辑；过期邀请惰性清理。黑名单检查读现有关系服务（`ICharacterRelation…DomainService` 的查询面，按代码实际名称）。
- 互斥的另一侧（在房不能接受组队邀请）放在组队邀请 CMD 中检查 `IPvpRoomQueryDomainService.IsSeated` 属于协议层，留给 TASK-06；本单元只提供查询。

### 权益与启动校验（Plan A1）
- `IPvpSettlementRuleService.EntitlementsOf` 输入改为 `ChannelEntryIdentity`：`Kind == FreeDuel` 一律不记录；`Kind == Pvp` 按 `LoginEnvironment`（8 不记录、24 记录 50/10），其他值抛。更新 TASK-02 的测试。
- 显式启动校验：找到宿主"开始接受连接前"的现有检查/就绪机制（例如 GameServer 的启动前预热或 hosted service 启动顺序），新增一个检查：遍历频道目录中被判为决斗频道的频道调用 `EntitlementsOf`，并强制加载两份 PvP 内容源；失败抛出使启动失败。写一个反例测试（未知环境 / 缺段档案 → 启动失败）。若找不到合适挂接点，写 Blocker 说明而不是自造机制。

### 调度骨架
- `Abstractions/Pvp`：`IPvpDeadlineWake { void Wake(); }`。`Application/Pvp/PvpDeadlineScheduler : TypedGameplayTimerSource, IPvpDeadlineWake`，照 `DungeonDeadlineScheduler` 的拉取+唤醒写法，来源集合 `IEnumerable<PvpDeadlineSource>` 本单元为空（TASK-05 增加）；登记状态普查 E1。

### 测试
AC-010–013 的领域用例；邀请（成功、过期、房满、已开局、会话已替换、在队、黑名单）；事务回滚后房间不变、房间号不泄漏；Prepare 冲突；开局冻结名单与 Loading 截止；启动校验反例；写入口表与普查。

## Result

- Outcome: done
- Changed:
  - 共享模型 `Abstractions/Pvp/PvpRooms.cs`（重写）：`PvpRoomId`/`PvpMatchId`(Guid)、`PvpRoomPhase`、`PvpRoomOrigin`、`PvpTeam{Individual=0,First=1,Second=2}`、封闭 `PvpSeat{Open,Closed,Taken(Session,Team,Ready)}`、`PvpFighter`、`PvpMatch(Id,Fighters,Map,StartedAt,PhaseDeadline,ModeState?)`、空基类 `PvpMatchModeState`、`PvpRoomSnapshot`、`PvpRoomRefusal`、`PvpRoomChangeKind`、`PvpSeatRelease`、`IPvpTownMoves`、封闭 `PvpRoomOutcome{Refused,Changed}`、`PvpInvitation`、封闭 `PvpInviteOutcome{Refused,Sent}`、`PvpRoomCreation`、`PvpSeatAction`、读面 `IPvpRoomQueryDomainService`、写面 `IPvpRoomDomainService`、`IPvpDeadlineWake`、`IPvpStartupVerification`。旧 `PvpRoom`/`Offer`/`Withdraw`/`OfferedOn` 删除。
  - `Abstractions/Pvp/PvpMaps.cs`：`IPvpRoomRuleService` 增加分队、开局判定、关座、继任、密码、加载/邀请时长；`PvpRoomMemberFacts`。`PvpSettlement.cs`：`EntitlementsOf(ChannelEntryIdentity)`。
  - `Application/Pvp/`：`PvpRoomDomainService`（事务参与者 `Changes`）、`PvpRoomRules`（静态，60s/30s/≤8 字节/≥2 人等）、`PvpRoomRuleService` 委托、`PvpSettlementRuleService` 按频道身份取权益、`PvpStartupVerification`、`PvpDeadlineScheduler`（+`PvpDeadlineSource`/`PvpDeadlineRound`/`AddPvpDeadlineSource<T>`，来源本单元为空）、`PvpRoomLobbyView`（快照→Data 记录）、`PvpRegistration` 两行。
  - `Abstractions/World/ITownAreaDomainService.cs`：`TownAreaDepartureReason.PvpRoom`（代码库中无此枚举的 switch）。
  - `Data`：`PvpRoomSeatState(Occupancy,Team,UserId,Ready)`+`PvpRoomSeatOccupancy`、`PvpRoomSnapshotState(RoomNumber,NameType,Name,Waiting,OwnerSeat,MapIndex,BattleMode,Seats,HasPassword)`；删 `PvpRoomState`，`PvpLobbyEntryState.Rooms` 改为 `PvpRoomSnapshotState`。
  - 读取方：`SessionAdjacentEntryDomainService`、`SelectCharacterSessionEntryService.PlanPvpAsync` 改读 `RoomsOn`。
  - 启动挂接：`GameServer/GameServerStartupService.StartingAsync`（PVF 存在分支内、内容预热之前）调用 `IPvpStartupVerification.VerifyAsync`，异常即启动失败。
  - Protocol：`SupplementalSynchronizationCodec` 把领域形状映射为 0x0029 字节（房间 1 等待/2 战斗；座位 空 0xFF、关 0xFE、占用=队伍号；末尾座位字节与匹配类型暂写 0）。
  - 测试：新增 `Application.Tests/Pvp/PvpRoomFixture.cs`、`PvpRoomDomainServiceTests.cs`（29 例）、`PvpStartupVerificationTests.cs`、`Host.Tests/PvpStartupVerificationHostTests.cs`；改 `PvpSettlementRuleServiceTests`、`PvpLobbyEntryTests`、`SessionAdjacentEntryTests`、`TownPresencePublicationTests`、`GameplayWriteAssertionTests`、`GameplayWriteEntryTableTests`（12 行）、`GameplayStateCensusTests`（调度器 E1）、`TimerSourceJobFailureTests`（调度器豁免）、`RegistryExistenceTests`、`Protocol.Tests/SupplementalSynchronizationTests`、`Networking.Tests/SelectionLifecycleWholeCommandTests`。
- Why:
  - 城镇移动在参与者 Commit 内执行，而不是写方法体内：位置服务不是事务化的（立即生效），在体内离城会在写库失败回滚后留下"人不在城镇、也不在房间"。结果经 `IPvpTownMoves` 在提交时填入（`Applied` 之前为空），协议在 `WriteAsync` 返回后读取。Commit 内移动失败只记 Error，不让 `CommitCanFail=false` 失真。写方法因此不必为城镇改为异步；建房（解析频道）、邀请（黑名单）本来就异步，为统一全部用 `ValueTask`。
  - `RoomsOn` 只返回已提交房间（=已公布）；`Find`/`FindSeated`/`IsSeated` 在可写事务内看本事务暂存。
  - 0x0029 的字节映射放进编解码器（RQ-073：领域无字节布局），因此动了 Protocol 编码器与其测试；`PlanPvpAsync` 改为直接读 owner（旧实现要求房间同时出现在入场状态与 owner 中，否则抛异常，房间真实存在后会让选角失败）。
  - 规则常量（60s、30s、密码 8 字节、≥2 人）与分队/平衡/继任放进 `PvpRoomRules`，经规则服务读取（架构规则：游戏语义常量只经规则服务）。分队：新人进人数少的队（平局 First）；换模式按座位顺序交替；继任为房主座位之后循环的下一个占用座。换模式/换队不清准备标记（Spec 未规定）。
  - 启动校验遍历"监听端口 ∪ 频道广告端口"并经 `IChannelEntryDomainService.ResolveAsync` 判类，不改频道服务；内容经两个规则服务强制加载（内容源只能注入规则服务）。挂在 PVF 存在的分支里，与现有"无 PVF 时照旧启动"一致。
  - 边界外的测试改动：`RegistryExistenceTests` 的生产图需要 Infrastructure+Persistence 模块（房间 owner 经城镇移动依赖内容与存储，模块都是惰性的）；`TimerSourceJobFailureTests` 列调度器为豁免（尚无来源，TASK-05 加来源后移入对账表）；`SelectionLifecycleWholeCommandTests` 的测试替身把频道判为 `Pvp` 却无环境——这种身份生产中不可能出现，启动校验正确拒绝，替身改为环境 8。
- Checks（`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，`-m:1` 单进程）：
  - `dotnet build Server/86JP.Server.slnx -m:1`：成功，无警告。
  - Application.Tests `Pvp|Sessions|Execution|Commands|DependencyGraph|RequiredDependency`：827 通过 0 失败。
  - Application.Tests `Architecture|Registr|Select|Census|Cycle`：735 通过，13 失败全部在 `baseline-failed.txt`（GuardianAltarLifecycleTests、DarkElfRuinsTests.Licensed_star…）。
  - Host.Tests `PvpStartupVerificationHostTests|Architecture|ContentWarmup|ChannelHostStartup|GmGameplayRouteTests.Startup|ComponentLifecycle|HostShutdownOrder`：40 通过、1 跳过（既有 Sigterm 用例）。
  - Protocol.Tests `SupplementalSynchronization|SelectCharacter`：75 通过；Networking.Tests `SelectionLifecycleWholeCommandTests`：2 通过。
  - 换行：`git diff --numstat` 与 `--ignore-cr-at-eol` 只在整文件重写的 `PvpRooms.cs` 差 1 行。
- Remaining:
  - 离场（Depart）、对局阶段推进、结算、停服、协议切片、组队邀请一侧的互斥检查均未做（TASK-05/06）。
  - 战斗中途的退房/踢人当前一律 `NotWaiting` 拒绝；战斗中的离开由 TASK-05 的 Depart 处理。
  - 邀请过期只惰性清理（任何提交时清过期与房间已不存在的邀请）；不由调度器推进。
  - 无 PVF 的部署不跑启动校验。

## Result Review

- Verdict: approved（独立快速审查）。审查者在当前源码上重建后跑 Application.Tests 过滤集 972 项全过；主控随后在当前源码上 `-m:1` 单线程复跑 Host（架构/启动/预热/Pvp）58 项、Protocol（补充同步/选角）75 项、Networking `SelectionLifecycleWholeCommandTests` 2 项，全过。
- 偏离 1（城镇移动在参与者 Commit 内）经审查确认符合架构，有先例 `Application/Effects/TownPositionEffectHandler.cs:39-48`，保留。
- 移交：回城落点若为家园城镇会在 Commit 期读存储（决斗频道基本不会），由 TASK-05/06 集成用例覆盖或注释前提；`PvpTeam` 枚举值直当线上值，TASK-06 收进 codec；换模式/换队不清准备标记，TASK-08 实机核对；`IPvpTownMoves` 只能在 `WriteAsync` 返回后读、`Find`/`FindSeated` 不要在编码或 Commit 回调里调用（TASK-06）。
