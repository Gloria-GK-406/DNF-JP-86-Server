# TASK-003 — 合并为单一实体：组件、4 个接口、调用方切换

## Task Unit Contract

- Revision: 1
- Outcome and references（Plan T-003；Spec RQ-001、RQ-005 读路径、RQ-006、RQ-011，AC-001、AC-002、AC-004（I5 判据）、AC-005、AC-006、AC-011）：
  - 婚姻业务由一个 `MarriageDomainService` 实现对外 4 个接口，内部由职责组件构成；
  - 全部调用方切换到新接口；
  - 删除原 8 个实现类和其中 7 个旧接口（`IMarriageQuestDomainService` 保留签名）；
  - 加入架构判据。
- Owned boundary：
  - `Server/src/Dfo.Server.Abstractions/Marriage/`：新接口与契约；删除旧接口；按需拆分 store 接口；
  - `Server/src/Dfo.Server.Data/Marriage/`：`MarriageView` 等新契约所需的数据类型；
  - `Server/src/Dfo.Server.Application/Marriage/`：组件、实体、薄订阅类；`ApplicationModule` 注册；
  - `Server/src/Dfo.Server.Persistence/`：store 接口拆分后的实现与注册；删除 `MarriedAsync` / `IsMarriedAsync`；
  - 婚姻服务的调用方：
    - `Dfo.Server.Protocol/Game/MarriageProposal/*`、`TownProjection/TownProjectionSlice.cs`、`QuestAcceptance/AcceptQuestCommand.cs`；
    - Application 中的 `SelectCharacterPreparationService`、`ReplayStateDomainService`、`QuestCompletion` / `QuestFacts` / `QuestOfferRefresh` 三个服务；
    - `ProtocolModule` 中与此相关的注册；
  - 测试：
    - 现有婚姻测试与 TASK-001 / TASK-002 新增测试的**接线**：服务取得方式、类型名、方法名；
    - `MarriageHarness`；
    - 新增架构判据与组件测试。
- Required checks：
  - 解决方案构建 0 警告；
  - **全量测试**通过（已知偶发项按 `AGENTS.md` 判别）；
  - 婚姻相关测试除接线外断言零改动，`git diff` 中只能看到类型名、方法名、取服务方式的变化（例外见 Detailed Plan 第 7 点）；
  - AC-001、AC-002 的搜索结果为零；
  - 新架构判据及其反例用例通过；
  - `CommandHandlerArchitectureTests` 通过。
- Dependencies：TASK-002（关系记录、账本、阶段规则、回填）已完成。

## Detailed Plan

### 1. 对外接口（`Dfo.Server.Abstractions/Marriage/MarriageDomainContracts.cs`，名字可调整）

按设计稿 §4 划分 4 个面，旧方法到新方法的对应关系见设计稿 §4.1。下列两处偏离设计稿，实施时以此为准，并在结果的 Why 中写明：

- **接受预约保持两步**：`IMarriageCoupleDomainService.RespondReservationAsync` 只负责应答（接受时预约转 Reserved、阶段转 Preparing）；另设 `OpenWeddingAsync(MarriageCeremony)`，承接原 `Wedding.PrepareAsync`（分配教堂、两人入场、扣费、建婚礼）。命令处理器在同一事务里依次调用这两个方法，这和现状一致。
  - 原因：现有表征测试会单独调用应答，并断言「未扣费」「已预约」。如果合成一步，就得改断言，违反 AC-009。整个过程在一个事务内完成，原子性不受影响。
- **调度器读取接口保留它需要的信息**：`ActiveWeddingsAsync` 可以返回调度器实际用到的形状（现状只用 ceremony id）。
- **`IMarriageQueryDomainService.ViewAsync`**：返回带「有效阶段」的 `MarriageView`（RQ-005 读路径）。Proposed / Requested 到期但还没落库时，按到期后的阶段呈现，不写库。到期条件和写路径相同：超时，或任一方会话不再有效。
- `IMarriageQuestDomainService` 签名不变，由同一实例实现。

### 2. store 接口按组件拆分（只拆接口，不动表）

- 从 `IMarriageWeddingDomainStore` 拆出宾客相关的 `InvitationsAsync` / `InviteAsync` / `PayAsync`，组成 `IMarriageWeddingGuestDomainStore`。Persistence 的同一个类实现两个接口，分别注册。
- 删除 `MarriedAsync` / `IsMarriedAsync`（`Server/src` 已经没有调用点）。
- 拆分后，每个 store 接口只由一个组件注入。

### 3. 组件（`Dfo.Server.Application/Marriage/`，都是 internal sealed 具体类型，以 singleton 注册）

| 组件 | 职责 | 唯一注入的 store |
|---|---|---|
| `CoupleLedger`（扩展现有） | 关系与求婚记录的读写；阶段 CAS；心意点；异常计数；收编原 Proposal store 的全部使用 | 关系 store、求婚 store |
| `MarriageEligibility` | 会话是否当前、同城同区、等级与异性配对、两项婚前任务已完成且绑定、金币检查。只读 | — |
| `ReservationDesk` | 预约的创建、应答、到期、按对方查找 | 预约 store |
| `ChurchRooms` | 房间分配、入场、新人 / 宾客站位持久化、快照（bride / groom）、跨区判定的房间部分 | 教堂 store |
| `WeddingSettlement` | 婚礼行的创建与扣费、到点完成 / 取消结算、退款、回礼、离场坐标、清理队列与重试 | 婚礼 store |
| `GuestList` | 请柬、礼金、宾客资格、剩余额度、已邀请名单、相关账号 | 宾客 store |
| `Guestbook` | 祝词的读、写、删 | 祝词 store |
| `TrialQuests` | 任务绑定、试炼计数、`DungeonMapCleared` 的处理逻辑 | 任务 store |
| `LovePoints` | 心意券的使用规则与效果结算；点数写入经由实体交给 Ledger | — |

另有 `MarriageStageRules`（纯函数，已有）。

- **组件之间不互相依赖**：跨组件的流程由实体编排，组件只接收值。例如推进婚礼时，实体先从 ChurchRooms 取房间和在场情况，从 GuestList 取宾客，再交给 WeddingSettlement 结算，然后由 Ledger 提交阶段。
- **规则服务照旧由组件调用**。

### 4. 实体

- `MarriageDomainService`（internal sealed partial），按接口拆成 `.Query.cs`、`.Couple.cs`、`.Venue.cs`、`.Quest.cs` 四个文件。
- 构造器只接 9 个组件和 `TimeProvider`，不接任何 store、`*Persistence`，也不接其它领域服务。
- 每个写操作加载一次关系、校验一次、提交一次。
- 身份判定**保持现状的逐处口径**，统一的工作在 T-004 做。可以把这六处判定集中写成实体里的私有方法，但行为不能变。

### 5. DI 注册

- 删除旧的 8 个注册，按设计稿 §5.5 注册：组件，实体，4 个接口都转发到同一个实例。
- `DungeonMapCleared` 改由薄订阅类 `MarriageTrialClearSubscriber` 转交给 `TrialQuests`，订阅 id 和投递策略沿用现状。
- `MarriageRelationAudit` 保留。

### 6. 调用方切换

- 逐个替换旧接口的注入和调用，各调用方只注入它用到的面，参照设计稿 §4.2。
- `MarriageCeremonyCommandHandler` 改为依次调用 `RespondReservationAsync` 和 `OpenWeddingAsync`；它根据本次应答结果的 state 决定是否开婚礼，这个逻辑保留，因为读的是本次结果，不是从存储推导阶段。
- `EnterMarriageChurchCommandHandler` 改为调用 `EnterChurchAsync`（flag 0 是新人，flag 1 是宾客）。

### 7. 测试接线

- 修改 `MarriageHarness` 的取服务方法；6 个现有婚姻测试文件和 TASK-001 / TASK-002 新增的测试改为取新接口，方法名跟着改。
- `TrialClears` 改为解析薄订阅类。
- **例外**：`MarriageChurchTests.cs` 第 353、354、511 行原本调用婚礼 store 的 `MarriedAsync` / `IsMarriedAsync` 做断言。改为经由 Query 接口（或 Ledger）读取同样的事实：是否已婚、心意点为 999。断言的语义值保持不变，在结果中逐条列出。

### 8. 新增测试

- **架构判据**（`Application.Tests/Marriage/MarriageArchitectureTests.cs`），反射遍历 Application 程序集：
  - 只有 `CoupleLedger` 的构造器接受关系 store；
  - 每个婚姻 store 接口最多被一个组件的构造器接受；
  - 9 个组件的构造器参数里没有其它组件类型；
  - `MarriageDomainService` 的构造器只含组件类型和 `TimeProvider`。
  - 另写一条反例用例：用测试内定义的违规类型喂给同一判定函数，断言被拒绝。
- **账本**：「子记录已迁移、关系 CAS 失败 → 抛异常、整笔回滚」（TASK-002 的遗留）。
- **ViewAsync**：Proposed / Requested 超时、或一方会话失效后，返回到期后的阶段，并且数据库里的阶段和 revision 都不变（AC-005）。
- **同一实例**：DI 解析出的 4 个接口是同一个对象（AC-001）。

### 9. 执行顺序

1. 拆分 store 接口；
2. 拆出组件（先从现有服务中提炼，行为不变）；
3. 写实体与接口；
4. DI 注册；
5. 切换调用方；
6. 删除旧代码；
7. 测试接线，随后构建、跑婚姻过滤集；
8. 新增测试；
9. 跑全量测试。

中途可以随时跑婚姻过滤集定位问题。

## Result

### Changed

**新增（生产代码）**

| 文件 | 要点 |
|---|---|
| `Abstractions/Marriage/MarriageDomainContracts.cs` | 4 个对外接口：`IMarriageQueryDomainService`、`IMarriageCoupleDomainService`、`IMarriageVenueDomainService`，以及从 `MarriageProposalContracts.cs` 原样搬来的 `IMarriageQuestDomainService`（签名未变） |
| `Data/Marriage/MarriageRelationModels.cs`（追加） | `MarriageView(RelationId, Stage, Revision, Self, Partner, CeremonyId)`，另有 `MarriedAt`、`LovePoints`、`TrialsCleared`；`Stage` 是有效阶段 |
| `Application/Marriage/MarriageDomainService.cs` + `.Query.cs` / `.Couple.cs` / `.Venue.cs` / `.Quest.cs` | 实体（internal sealed partial），一个类实现 4 个接口。构造器只接 9 个组件加 `TimeProvider`。主文件放六处身份判定中的三处（`ReservationPairAsync` = 原 Ceremony 口径、`ReservedAsync` = 原 Church 口径、`QuestRelationAsync` = 原 Quest 口径），另外三处（Engagement、LovePoint、Guestbook）在各自方法里，逐处照搬原逻辑；两处惰性到期（`ActiveProposalAsync`、`ActiveReservationAsync`）也在主文件 |
| `Application/Marriage/MarriageEligibility.cs`、`ReservationDesk.cs`、`ChurchRooms.cs`、`WeddingSettlement.cs`、`GuestList.cs`、`Guestbook.cs`、`TrialQuests.cs`、`LovePoints.cs` | 8 个新组件（`CoupleLedger` 是扩展的第 9 个），见下表 |
| `Application/Marriage/MarriageTrialClearSubscriber.cs` | 薄订阅类，`DungeonMapCleared` 交给实体的 `RecordTrialClearAsync` |

**修改（生产代码）**

- `CoupleLedger.cs`：构造器加入求婚 store 和 `IMarriageProposalRuleService`；新增 `FindProposalAsync`、`ReceivedProposalAsync`、`ProposeAsync`（T1：建求婚行和关系行，顺序与原来相同）、`TransitionProposalAsync`；删除 `CreateAsync(Guid)`（已并入 `ProposeAsync`）。原有的 `FindAsync` / `ReadAsync` / `ReadForAsync` / `CommitAsync` / `MustCommitAsync` / `AddLovePointsAsync` / `CountUnsettledReservationsAsync` 签名不变。
- `Abstractions/Marriage/*Contracts.cs`：删除 7 个旧接口（`IMarriageProposal/Ceremony/Church/Engagement/Guestbook/LovePoint/WeddingDomainService`）；`IMarriageWeddingDomainStore` 删除 `MarriedAsync` / `IsMarriedAsync`，宾客三方法拆到新的 `IMarriageWeddingGuestDomainStore`。
- `Persistence/Marriage/SqliteMarriageWeddingDomainStore.cs`：同一个类实现两个接口，删除两个推导方法的实现；`PersistenceModule` 先注册具体类，两个接口都转发到同一实例。
- `Application/ApplicationModule.cs`：删除 8 个旧注册和旧订阅；注册 8 个组件、实体、4 个接口转发；`AddDomainSubscriber<DungeonMapCleared, MarriageTrialClearSubscriber>`，订阅 id `quest.wedding-trial`、`Reliable` 不变；`MarriageRelationAudit` 保留。`LovePoints` 用工厂注册，见 Why「DI 环」。
- `MarriageGuestbookRuleService`、`MarriageLovePointRuleService` 原来与旧服务同文件，随文件删除，分别搬到 `Guestbook.cs`、`LovePoints.cs`，内容未改。

**删除**：`MarriageProposal/Ceremony/Church/Engagement/Guestbook/LovePoint/Quest/WeddingDomainService.cs` 共 8 个文件。`Server/src` 的 `git diff --stat`：28 个文件，+104 / −1099（不含新文件，含 TASK-002 已有的改动）。新组件、实体和订阅类共 15 个文件，1374 行。

**组件与各自注入的 store**

| 组件 | 婚姻 store | 主要其它依赖 |
|---|---|---|
| `CoupleLedger` | 关系 store、求婚 store | 求婚规则（Lifetime）、时钟 |
| `MarriageEligibility` | — | 会话、TownArea、角色、求婚规则、预约规则、任务日志、入场库存（03F3 对方装备） |
| `ReservationDesk` | 预约 store | 预约规则、时钟 |
| `ChurchRooms` | 教堂 store | 教堂规则、求婚规则（性别）、会话、在线快照、TownArea、角色、位置、位置持久化、时钟 |
| `WeddingSettlement` | 婚礼 store | 教堂规则、求婚规则、会话、角色、位置、位置持久化、邮件、物品投递、`ICharacterTable`、`ICharacterDomainStore`（离线存档坐标，非婚姻 store）、事务访问器 |
| `GuestList` | 宾客 store（新拆） | 教堂规则、在线快照、TownArea、角色、`ICharacterTable` |
| `Guestbook` | 祝词 store | 祝词规则、角色、时钟 |
| `TrialQuests` | 任务 store | 求婚规则、任务内容、任务日志、角色、任务可用性、婚姻地下城内容、事件服务、时钟 |
| `LovePoints` | — | 心意券规则、背包槽、道具使用规则、角色、`Func<IApplyEffectsDomainService>`、时钟 |

**4 个接口的方法**

- Query：`ViewAsync`、`SnapshotAsync`、`ReceivedProposalAsync`、`ReceivedReservationAsync`、`IsMarriedAsync(relation)`、`WeddingPhaseAsync(ceremony)`、`AppearanceAsync`、`CanCrossAsync`、`CanLeaveAsync`、`RemainingInvitationsAsync`、`InvitedAsync`、`CoupleAccountsAsync`、`WeddingAccountsAsync`、`ActiveWeddingsAsync`（返回 ceremony id 列表）。
- Couple：`ProposeAsync`、`RespondProposalAsync`、`RequestReservationAsync`、`RespondReservationAsync`、`OpenWeddingAsync(MarriageCeremony)`、`UseLovePointAsync`。
- Venue：`PrepareChurchAsync`、`EnterChurchAsync(actor, key, area, flag)`、`InviteAsync`、`GiftAsync`、`AdvanceAsync`、`CompleteCleanupAsync`、`ReadGuestbookAsync`、`PostGuestbookAsync`、`DeleteGuestbookAsync`。
- Quest：`EligibleQuestIdsAsync`、`CanAcceptAsync`、`BindAsync`、`CanCompleteAsync`（不变）。

**调用方注入**

| 调用方 | 注入 |
|---|---|
| `MarriageProposalCommandHandler`、`MarriageCeremonyCommandHandler`、`WeddingLovePointCommandHandler` | Couple |
| `PrepareMarriageChurchCommandHandler`、`EnterMarriageChurchCommandHandler`、`WeddingCommentCommandHandler`、`AdvanceMarriageWeddingCommandHandler`、`CompleteWeddingCleanupCommandHandler`、`WeddingLetterCommandHandler`、`WeddingGiftCommandHandler` | Venue |
| `WeddingResponseHandler`（原注入 Proposal、Engagement、Ceremony、Wedding 四个）、`WeddingPartnerHandler`、`WeddingLovePointHandler`、`MarriageWeddingScheduler`、`SetUserAreaHandler`（原 Church + Wedding 两个可空参数合并为一个） | Query |
| `SelectCharacterPreparationService` | Query（原 Engagement）+ Quest（不变） |
| `ReplayStateDomainService` | Query（`SnapshotAsync`，原 Engagement） |
| `QuestCompletion` / `QuestFacts` / `QuestOfferRefresh`、`AcceptQuestCommandHandler` | Quest，无需改动 |

`MarriageCeremonyCommandHandler` 依次调 `RespondReservationAsync`（或 `RequestReservationAsync`），再按本次结果 `State == Reserved` 调 `OpenWeddingAsync`，逻辑照旧。`EnterMarriageChurchCommandHandler` 只调 `EnterChurchAsync`，flag 分派移进实体，flag ≥ 2 仍回 -10。

**测试**

- 接线：`MarriageHarness`（取服务方法改为 `Query` / `Couple` / `Venue` / `WeddingQuests` / `TrialClears`）、5 个已跟踪的婚姻测试文件、TASK-001 / TASK-002 新增的 6 个文件。`EffectOperationReplayTests` 只用 store，无需改。
- 新增 `MarriageArchitectureTests.cs`（2 条）和 `MarriageEntityTests.cs`（4 条），见 Checks。

`MarriageChurchTests.cs` 的三处例外（Detailed Plan 第 7 点）：

| 原行 | 原断言 | 新断言 |
|---|---|---|
| 353 | `Assert.Equal(both, await restored…GetRequiredService<IMarriageWeddingDomainStore>().IsMarriedAsync(ceremony.RelationId, default))` | `Assert.Equal(both, await restored…GetRequiredService<IMarriageQueryDomainService>().IsMarriedAsync(ceremony.RelationId, default))` |
| 354 | `Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (await restored…<IMarriageWeddingDomainStore>().MarriedAsync(ceremony.RelationId, default))!.CompletedAt!.Value.ToUnixTimeMilliseconds())` | `Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (await restored…<CoupleLedger>().ReadAsync(ceremony.RelationId, default))!.MarriedAt!.Value.ToUnixTimeMilliseconds())` |
| 511 | `Assert.Equal(999, (await restored…<IMarriageWeddingDomainStore>().MarriedAsync(ceremony.RelationId, default))!.LovePoints)` | `Assert.Equal(999, (await restored…<CoupleLedger>().ReadAsync(ceremony.RelationId, default))!.LovePoints)` |

期望值（`both`、`clock.Now` 毫秒、999）都没变，读的是同一事实：已婚、结婚时刻、心意点，都按 relation id 读。354、511 一开始写成 `Query.ViewAsync(f.Session)`，失败了：重开的 fixture 没有测试用的任务内容，而 `ViewAsync` 计算 `TrialsCleared` 时要读任务规则，于是抛 `Script.pvf was not found`。后来按第 7 点允许的「或 Ledger」改为读账本。

### Why

**对设计稿的偏离**

1. 接受预约仍然是两步：`RespondReservationAsync` 加 `OpenWeddingAsync`。这是 Task 预定的偏离。
2. `ActiveWeddingsAsync` 只返回 ceremony id，调度器只用这个。这也是 Task 预定的。
3. **`Church.PrepareAsync` 放在 Venue（`PrepareChurchAsync`），没有做成只读的 `Query.ChurchAsync`。** 原方法在缺房时会分配教堂，是写操作。生产路径上缺房的分支到不了（T6 同事务已分配），但现有测试多处在只接受预约、没开婚礼时直接调用它，并断言分到了房间（如 `Rolled_back_preparation_and_entry_leave_no_room_or_access_grant`、`Two_reserved_couples_get_distinct_rooms…`）。改成只读就要改这些断言，所以保留写语义，放到写接口上。
4. **Query 保留 `IsMarriedAsync(relation)` 和 `WeddingPhaseAsync(ceremony)`。** 设计稿写的是改读 `View.Stage`。但 `TownProjectionSlice` 读的是外观所在婚礼的 phase，宾客没有对应的 View；另外现有测试（`MarriageChurchTests:447`、`MarriageRequestCharacterizationTests:168`）直接断言这两个方法。前者读阶段，后者读婚礼明细，都不是从婚礼或预约推导关系阶段。
5. **订阅类转交实体（`RecordTrialClearAsync`），不直接转交 `TrialQuests`。** 计数前要先按 Quest 口径找到这一对的关系，而组件不能调账本。所以实体做过滤和关系判定，`TrialQuests` 只负责逐人记录。
6. **03F3 对方资料（装备 13 格）由 `MarriageEligibility.ProfileAsync` 构造。** 它需要角色和入场库存这两个只读依赖，归入「只读的人物事实」组件最近。
7. **DI 环（新发现）。** 合并后出现构造期依赖环：`LovePoints → IApplyEffectsDomainService → DungeonEntryAllowanceEffectHandler → … → QuestFactsDomainService → IMarriageQuestDomainService（即实体本身）`。旧结构没有这个环，因为原心意点服务不被任何任务服务依赖。症状是任何解析婚姻服务的用例都挂起（`--blame-hang-timeout` 取到 hang dump）。临时写了一个反射探针沿构造器做 DFS 定位到这条路径，确认后删除。修法沿用仓库已有的做法（`WeaknessDomainService`、`IndependentDropRuleService` 用 `Func<T>` 在使用时解析）：`LovePoints` 改为接 `Func<IApplyEffectsDomainService>`，在 `ApplicationModule` 用工厂注册，类上写了一行原因。
8. **「每个写操作加载一次关系、校验一次、提交一次」没有完全做到。** 写路径保留了 TASK-002 的读法。例如申请预约先经 `ReservationPairAsync` 读两次（按两个角色），提交前再 `ReadForAsync` 重读，因为惰性到期可能刚推进过 revision。合并读取会改变 `Stale` 与 `Ineligible` 的先后，和 T-004 的身份统一纠缠在一起，所以本单元不动，只把这些读收拢到实体。

**为保持行为所做的取舍**

- 各流程逐行沿用原服务中副作用的顺序。到点结算的写入顺序没变：每位新人依次站位、回礼、礼金分成，然后宾客回礼；取消时先预约过期，再退婚礼费、退宾客礼金，再改写离场坐标。
- 原 Ceremony 资格检查对每人每个任务交替判断「已完成」「已绑定」，现在拆成 `eligibility.WeddingQuestsClearedAsync && trials.BoundToAsync`。都是只读，布尔结果相同。`BoundToAsync` 用的任务号来自求婚规则，原来来自预约规则；`MarriageCeremonyRuleService` 的这两个值就是转发求婚规则，值相同。
- 原 Church 服务的可空 store 参数（`store is null` 分支）在 DI 中总是有值，合并后不再保留。
- 测试中直接调用 `Church.EnterAsync(…, flag: 1)` 的两处（`MarriageChurchTests:190`、`MarriageChurchCharacterizationTests` 中 flag 1 那一行），接线后走 `EnterChurchAsync` 的宾客分支。这与生产命令处理器的分派一致。两处的 actor 都没有请柬，结果仍是 -10，断言不变。

### Checks

所有命令都在 worktree 的 `Server/` 下实际运行。

- **构建**：`dotnet build 86JP.Server.slnx` → 0 个警告，0 个错误。
- **全量测试**：`dotnet test 86JP.Server.slnx --no-build --logger "console;verbosity=detailed"`，日志在会话 scratchpad 的 `mda-task003-full.log`。所有测试项目都显示「测试运行成功」，失败 0；日志中 `ObjectDisposedException` 出现 0 次。

  | 项目 | 总数 | 通过 | 跳过 | 失败 |
  |---|---:|---:|---:|---:|
  | Application | 4012 | 3586 | 426 | 0 |
  | Protocol | 1581 | 1521 | 60 | 0 |
  | Persistence | 470 | 470 | 0 | 0 |
  | Infrastructure | 933 | 596 | 337 | 0 |
  | Host | 159 | 149 | 10 | 0 |
  | Networking | 92 | 92 | 0 | 0 |
  | HttpApi | 50 | 50 | 0 | 0 |
  | Pvf | 80 | 71 | 9 | 0 |

  Application 4012 = TASK-002 复核时的 4006 + 本单元新增 6 条。其余项目的数字与 TASK-002 复核时相同。跳过的都是 Skippable（本机无 PVF 或客户端档案）。
- **婚姻过滤集**（取自同一份全量日志）：Application 婚姻用例通过 94、跳过 3，即 TASK-002 的 91 条加新增 6 条；Protocol 婚姻用例 36 条通过；Persistence 的 `Marriage*MigrationTests` 2 条通过；`CommandHandlerArchitectureTests` 13 条通过、0 失败。
- **AC-001**，在 `Server/src` 下排除 `obj/`：
  - `grep -rnE "\bMarriage(Proposal|Ceremony|Church|Engagement|Guestbook|LovePoint|Quest|Wedding)DomainService\b"` → 0 行（旧 8 个实现类）；
  - `grep -rnE "\bIMarriage(Proposal|Ceremony|Church|Engagement|Guestbook|LovePoint|Wedding)DomainService\b"` → 0 行（7 个旧接口）；
  - 同一实例由 `The_four_faces_resolve_to_one_instance` 证明（`Assert.Same` 三次）。
- **AC-002**：`grep -rnE "\.(IsReservedAsync|MarriedAsync)\(|\bMarriedAsync\b|IsReservedAsync" Server/src` → 0 行。对 Application 和 Protocol 搜 `MarriageWeddingPhase.*` / `MarriageCeremonyState.*` / `MarriageProposalState.*`，剩下的都属于以下几类：
  - 子记录自身的明细：求婚 / 预约的待答幂等、CAS 与到期；单场婚礼的 phase，用于请柬、礼金、最后一分钟锁、外观、清理、宾客跨区、祝词簿排除已取消；
  - Protocol 读本次命令的结果（`MarriageCeremonyCommand.cs:25` 等）。

  没有用它们推导关系是否「已订婚 / 已预约 / 已婚」的地方。
- **架构判据**：
  - `MarriageArchitectureTests.The_entity_is_composed_of_independent_components_and_each_store_has_one_owner` 通过。它反射遍历 Application 程序集，检查四点：实体构造器只有 `TimeProvider` 和具体类；9 个组件互不出现在彼此的构造器里；8 个婚姻 store 接口各自恰好被一个类型的构造器接受；关系 store 只被 `CoupleLedger` 接受。
  - 反例 `…refuses_a_store_in_the_entity_a_component_taking_another_and_a_second_relation_writer` 通过。它把测试内定义的违规类型喂给同一个判定函数：实体接 store 和领域服务接口、组件接组件、两个类型都接关系 store。断言 6 条违规全部被报出。
- **新增行为用例**，全部通过：
  - `A_relation_that_moved_after_its_child_record_changed_rolls_the_whole_use_case_back`：装饰关系 store，让 CAS 拿到的 revision 偏一位。接受求婚和申请预约都抛 `InvalidOperationException`；回滚后求婚仍是 Pending、关系仍是 stage 0 / revision 1，预约行数为 0、关系 revision 不变、`ceremony_id` 为 NULL。这是 TASK-002 的遗留。
  - `The_view_shows_a_lapsed_proposal_as_closed_and_leaves_it_stored_as_proposed`：超时、对方会话失效两种情况下，View 都返回 Closed，库里 stage 0 / revision 1、求婚 state 0 不变；账号不符时 View 返回 null。
  - `The_view_shows_a_lapsed_reservation_as_engaged_and_leaves_it_stored_as_requested`：未到期时 View 为 Requested，带 ceremony，`TrialsCleared = true`；超时或会话失效后返回 Engaged，`CeremonyId = null`，revision 不变；库里 stage 2、预约 state 0、`ceremony_id` 非空，都不变（AC-005）。
  - `The_four_faces_resolve_to_one_instance`（AC-001）。
- **测试 diff 只含接线**：
  - `git diff --stat -- Server/tests`：`MarriageCeremonyHandlerTests` 6、`MarriageCeremonyTests` 80、`MarriageChurchTests` 201、`MarriageEngagementTests` 28、`MarriageProposalTests` 20 行。`EffectOperationReplayTests`（+1）和 `SchemaOwnershipTests`（2）是 TASK-002 控制者已批准的改动，本单元没碰。
  - 逐文件核对：取 HEAD 版本，套用接线规则（scratchpad 的 `test_rewire_rules.py`，只包含服务取得方式、接口类型、方法名、宾客 flag 1 这几类改名），再与工作区 diff（`verify_wiring.py`）。4 个文件残差为 0。`MarriageChurchTests` 的残差只有五处：一行 `using Dfo.Server.Application.Marriage;`、新增的 `Ask`（Query）和 `Pair`（Couple）两个取服务 helper、上表 3 处例外，以及 TASK-002 已批准的种子行（508）。
  - TASK-001 / TASK-002 新增的 6 个文件未被 git 跟踪，无法用 git diff 比对。它们由同一份接线脚本（`test_rewire.py`）机械改写。除 `MarriageHarness` 外，行数与改写前一致（98 / 136 / 174 / 171 / 249 / 236）。`MarriageHarness` 从 199 行减到 195 行，是 10 行旧取服务方法换成了 6 行新的。

### Remaining

- 写路径仍有多次读取关系（Why 第 8 点）。「加载一次」是否在 T-004 统一身份判定时一并收拢，请控制者确认。
- 设计稿需要记录的偏差（Why 第 3–7 点，尤其是 DI 环及其修法）留给 T-005 写入设计稿。
- `ViewAsync` 计算 `TrialsCleared` 时依赖任务规则内容，没有内容（例如未装 PVF 的环境）时会抛异常。生产环境总有内容；目前没有生产调用方。如果后续让调度器或协议使用 View，需要考虑这一点。

## Controller Decision（round 1）

- **结果接受**。控制者重新跑了全量构建和测试：0 警告，8 个测试项目「测试运行成功」，失败 0。日志在会话 scratchpad 的 `mda-task003-verify.log`。AC-001 搜索剩下的命中都是刻意保留的 `IMarriageQuestDomainService`（revision 3）。
- **「每个写操作只加载一次关系」并入 TASK-004**：统一身份判定时，每个公开操作只经账本解析一次关系，然后往下传。由此带来的失败码先后顺序变化，在 TASK-004 里按刻意修正逐条列出，并配对照测试。
- **`ViewAsync` 读 `TrialsCleared` 时缺任务内容会抛异常**：交给 TASK-004 修正，缺内容时不抛异常、得到 false，并补一条用例。
- **偏离设计稿的第 2–6 点**：Venue 上的 `PrepareChurchAsync`、Query 上保留的 `IsMarriedAsync` / `WeddingPhaseAsync`、订阅类经实体转交、对方资料由 Eligibility 构造、`LovePoints` 以 `Func<>` 延迟解析来打破 DI 环，都留给 T-005 写进设计稿的落地状态。
