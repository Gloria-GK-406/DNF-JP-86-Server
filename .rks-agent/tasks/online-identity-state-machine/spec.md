---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: 在线身份一条记录、一把锁；在线角色状态一个无锁运行时

## Why and Outcome

调用方今天要回答“这个会话还算不算数”，得分别问三四个组件再拼起来，几份存储的写入、拆除顺序又各不相同，所以到处是“三连查”。“连接已撤销、待清理”这种状态，也只能从存储之间的不一致里间接看出来。位置、血蓝、冷却、灵药四份在线状态各自为政，代数保护的程度也不一样。

交付后，“他是谁”由在线身份一个组件在一把锁下回答，“他怎么样”由在线角色运行时一个服务回答。游戏协议行为不变；只在“旧会话迟到读写”这一类交错上，有意收紧为精确隔离（见 RQ-012）。

路径相对 `Server/src/`。

## Definitions

- **选中会话记录**：在线身份为每个账号至多保存一条记录，内容是 `SelectedSessionIdentity`，加上阶段（`Selected` 或 `InWorld`）和隔离标记。
- **在线用户**：在线身份为每条已登记的连接保存一条记录，内容是连接、已认证账号和代数（`Generation`）。
- **连接已撤销、待清理**：选中会话记录仍在，但它的 `ConnectionId` 在在线身份里已经没有在线用户登记了。
- **精确**：参数里的会话必须与记录中的 `SelectedSessionIdentity` 值相等，不能只是同一个账号或同一个角色。

## Scope

包含 TaskContract Scope In 的全部内容，具体由下面的需求展开。

明确不包含：
- `SelectedSessionSafety` 按连接对象记录的连接级栅栏；
- `LoginSessionRegistry`（登录票据与连接是否被顶替），保持原样；
- 其余内联“当前”判定的收敛（#185）；
- 城镇区域里的第二份位置（#208）。

`OnlineUserSnapshot.CurrentCharacterId` 与 `State` 仍然对外提供，但改为从在线身份投影出来；读取它们的调用方不需要改。

## Requirements

### 在线身份

| ID | Requirement |
|---|---|
| RQ-001 | `OnlineStateRegistry` 成为唯一的在线身份组件，在一把锁内持有在线用户记录、选中会话记录和端点。删除 `SelectedSessionDomainService` 自有的字典与锁、`SelectedSessionRuntimeDirectory`（类与 `ISelectedSessionRuntimeDirectory` 接口）、`activeByAccount`、`OnlineUser.CurrentCharacterId` 字段、`SelectedSessionSafety.blocked`、`ActiveCharacterDomainService` 与 `IActiveCharacterDomainService`。 |
| RQ-002 | `ISelectedSessionDomainService` 保留，但由一个直接操作在线身份状态的实现提供。它的读取方法 `Find`、`FindByCharacter`、`FindByWireUserId`、`FindByConnection`，以及 `ISelectedSessionSnapshotQuery.Snapshot`，返回任意阶段、带或不带隔离标记、连接是否已撤销的选中会话，与今天选中会话目录的语义相同。 |
| RQ-003 | `ISelectedSessionDomainService` 新增两个查询：精确阶段查询 `StandingOf(session)`，返回阶段、是否隔离、连接是否已撤销，会话不是当前的则返回 null；以及在此之上的 `IsInWorld(session)`，它取代 `IsPublishedExact`。 |
| RQ-004 | `ISelectedSessionDomainService` 上的状态转移：`Select`（语义不变：同一角色或同一 `WireUserId` 已被别的账号占用时抛出，否则替换并返回被顶替的会话）；`EnterWorld(session)`（取代 `IActiveCharacterDomainService.Publish` 与运行时目录的发布）；`Fence(session)`；`ReleaseExact(session)`（同时完成今天 `ClearExact` 与 `ReleaseExact` 的效果）。 |
| RQ-005 | `EnterWorld` 的前置条件和副作用与今天 `OnlineStateRegistry.Publish` 相同：会话必须是当前会话；必须存在一个账号、连接、代数都与会话一致的在线用户，否则抛出。成功后在线用户代数加一，记录进入 `InWorld`，并记下所属在线用户。 |
| RQ-006 | `ReleaseExact` 只移除与参数精确相等的记录。被移除的记录如果处于 `InWorld`，并且所属在线用户仍登记在同一连接上，该在线用户代数加一，状态回到 `Authenticated`，与今天的 `UnbindCharacter` 相同。 |
| RQ-007 | `OnlineUserSnapshot.CurrentCharacterId` 与 `State == CharacterActive` 从选中会话记录投影出来：只有记录处于 `InWorld` 且所属在线用户就是这个在线用户时，才带出该角色。认证事务参与者对“尚未绑定角色”的检查也使用同一投影。 |
| RQ-008 | `IAuthenticatedGamePresenceQuery.IsActiveExact(session)` 改用合并后的记录，接受范围与今天相同：精确、`InWorld`、所属在线用户仍登记在会话连接上、该连接是游戏端点、账号一致。 |
| RQ-009 | 断开在线用户（`TryDisconnect`、`TryDisconnectConnection`）不再另外删除活跃登记，选中会话记录保留。此时 `StandingOf` 报告连接已撤销，`IsInWorld` 的结果不变（与今天运行时目录仍在一致），`IsActiveExact` 为假。这条记录在拆除流程调用 `ReleaseExact` 后消失。 |
| RQ-010 | `SelectedSessionSafety` 判断会话是否被隔离时改问在线身份。`BlockAsync` 通过 `Fence` 标记会话；`DemandCurrent` 的接受范围不变：不是当前或已隔离都拒绝。连接级栅栏保持原样。 |

### 在线角色运行时

| ID | Requirement |
|---|---|
| RQ-011 | 新增 `IOnlineCharacterRuntimeDomainService`（Abstractions）及其实现（Application）。它是独立服务，不并入角色领域服务。它持有每个在线角色的一条运行时记录：所属会话、位置、血蓝百分比、物品冷却、灵药加成。删除 `ICharacterPositionDomainService`、`ICharacterVitalRuntimeDomainService`、`ICharacterItemCooldownDomainService`、`ISpiritPotionDomainService` 及其实现，所有调用方迁到新服务。 |
| RQ-012 | 写入都是精确的：`Begin(session, position, hpPercent)`（发布时调用，替换该角色之前的任何记录，冷却和灵药从空开始）、`EndExact(session)`、`CrossInto`/`StepTo(session, …)`（不是所属会话就抛出，与今天的位置服务相同）、`StartCooldown(session, …)`、`ApplySpiritPotion(session, …)`（不是所属会话就不生效）。读取：调用方持有自己的会话时按会话精确读取（不是所属会话返回 null 或空）；只持有别的角色的 ID 时，读取该角色当前记录。今天对冷却、灵药不做的代数隔离，以及旧会话对自身状态的迟到读取，在这里有意收紧为精确隔离。 |
| RQ-013 | 运行时实现不使用 `lock`、`Monitor`、`SemaphoreSlim` 或其他互斥原语，只用并发集合的原子操作加不可变记录。同一记录的并发更新不得丢失。`EndExact` 之后，旧会话迟到的任何写入都不得重新建出记录。 |
| RQ-014 | 拆除时不再读取存储的“是否存位置”，而是在这一会话的运行时记录存在时，用 `IChannelEntryPolicy.ResolveAsync(session.ListenerPort)` 得到 `!channel.IsTransient` 来判断要不要落库。解析失败要像其他拆除步骤一样记入清理失败，并跳过落库，其余清理照常进行。 |

### 流程与整体

| ID | Requirement |
|---|---|
| RQ-015 | 选角准备、发布、拆除、断线、协议拒绝、顶替、隔离各路径都改用上面的转移。发布顺序保持今天的：先 `EnterWorld`，再 `Begin`，再进入城镇区域。拆除时由 `EndExact` 一次清掉位置、血蓝、冷却、灵药。容器清理仍然只在该角色已经没有选中会话时进行。 |
| RQ-016 | 生产代码里不留旧类型、转发层或 `[Obsolete]`。保留的接口（`ISelectedSessionDomainService`、`ISelectedSessionSnapshotQuery`、`IAuthenticatedGamePresenceQuery` 等）由新组件直接实现，DI 注册相应更新。 |
| RQ-017 | 游戏协议回包与持久化结果不变。完整解决方案构建通过；除基线已有的 2 个 `DimensionDungeonTests` 失败外，所有测试通过；测试数的增减要能说明。 |
| RQ-018 | 描述这些结构的文档与新模型一致：`AGENTS.md` 中讲 `Users/`、`Sessions/`、在线状态那把锁、运行时目录的段落；`Docs/CharacterEntryFacesInventory.md` 中提到被删类型的地方；以及代码检索时发现的其他 Docs 描述。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-011, RQ-016 | 在 `Server/src` 中检索 `SelectedSessionRuntimeDirectory`、`IActiveCharacterDomainService`、`activeByAccount`、`ICharacterPositionDomainService`、`ICharacterVitalRuntimeDomainService`、`ICharacterItemCooldownDomainService`、`ISpiritPotionDomainService`、`SpiritPotionDomainService`、`CharacterPositionDomainService`、`CharacterItemCooldownDomainService`、`CharacterVitalRuntimeDomainService`、`TryGetPositionPersistence`、`IsPublishedExact`，结果都为零；`SelectedSessionSafety` 中不再有会话集合；`[Obsolete]` 的数量不增加。 |
| AC-002 | RQ-002, RQ-003 | 给定账号 A 以会话 S 选中，并依次经过 Selected、InWorld、Fenced、连接撤销各状态：`Find(A)`、`FindByCharacter`、`FindByConnection`、`FindByWireUserId` 始终返回 S；`StandingOf(S)` 依次报告对应的阶段和标记；对另一个同角色但不同代数的会话 S'，`StandingOf(S')` 为 null。 |
| AC-003 | RQ-004, RQ-005 | 连接未登记、代数不符或会话不是当前时，`EnterWorld` 抛出，记录保持 Selected，在线用户代数不变；成功时在线用户代数加一，快照的 `CurrentCharacterId` 为该角色，`State` 为 `CharacterActive`。 |
| AC-004 | RQ-006, RQ-007 | 对 InWorld 的 S 调用 `ReleaseExact(S)` 后，`Find` 为 null，在线用户代数加一，快照 `CurrentCharacterId` 为 null，`State` 为 `Authenticated`；对一个已被替换的旧会话调用 `ReleaseExact`，返回 false，且不改动替换者。 |
| AC-005 | RQ-008, RQ-009 | S 处于 InWorld 时 `IsActiveExact(S)` 为真。`TryDisconnectConnection` 之后：`IsActiveExact(S)` 为假，`IsInWorld(S)` 为真，`StandingOf(S)` 报告连接已撤销，`FindByConnection` 仍返回 S；`ReleaseExact(S)` 之后，这些查询都为空或假。 |
| AC-006 | RQ-010 | 对当前会话 `BlockAsync` 之后，`StandingOf` 报告已隔离，`DemandCurrent(S)` 抛出；同角色的新会话不受影响。连接级栅栏原有测试不改动仍通过。 |
| AC-007 | RQ-012 | 给定旧会话 S1 已被同角色的新会话 S2 `Begin`：S1 的 `StepTo` 抛出；S1 的 `StartCooldown`、`ApplySpiritPotion` 不改变 S2 的记录；按 S1 精确读取位置返回 null；按角色读取返回 S2 的状态；`EndExact(S1)` 返回 false，S2 的记录保留。 |
| AC-008 | RQ-013 | 实现文件中没有 `lock`、`Monitor`、`SemaphoreSlim`、`Lock`、`Mutex`。并发测试中，多个线程对同一会话交替执行 `StepTo` 与 `StartCooldown`，结束后两类更新都在。`EndExact` 与迟到写入并发时，结束后记录不存在。 |
| AC-009 | RQ-014 | 拆除一个持久频道上已发布的会话时，按运行时位置落库；在临时频道上不落库；从未发布（没有运行时记录）的会话不落库；频道解析抛错时，清理报告该失败，其余清理照常完成。 |
| AC-010 | RQ-015 | 现有选角、顶替、拆除、断线的流程测试（Protocol、Application、Networking 集成测试）不改断言即可通过，只因类型变化调整构造方式。新增的流程测试覆盖：协议拒绝后由断线回调完成清理，清理后选中会话记录与运行时记录都不存在。 |
| AC-011 | RQ-017 | `dotnet build Server/86JP.Server.slnx` 0 错误；`dotnet test` 在放有 `Script.pvf` 时，除基线的 2 个失败外全部通过；每个测试项目相对基线的数量变化都写明原因。 |
| AC-012 | RQ-018 | 文档差异覆盖 RQ-018 列出的位置，文中不再出现被删类型作为现行结构的描述。 |

## Success Evidence

- EV-001（AC-001）：`grep` 检索输出，附在验证证据中。
- EV-002（AC-002～AC-006、AC-009）：新增或改写的 Application 单元测试，按 AC 编号命名或注释。
- EV-003（AC-007、AC-008）：运行时服务的单元测试与并发测试，加上实现文件检索。
- EV-004（AC-010、AC-011）：完整构建与测试输出，并与 `verification/baseline.md` 对照。
- EV-005（AC-012）：文档差异。

## Call-Site Mapping

迁移按下表进行；表中没有、但被编译器发现的调用点，按同一原则处理：接受范围不变，持有自己的会话时按会话精确读写。

| 今天 | 交付后 | 接受范围 |
|---|---|---|
| `published.IsPublishedExact(s)`：`TauKingdomDomainService`、`MonsterDeathDomainService`、`ExperienceBonusDomainService`、`SouthernDaleDomainService`、`BossTowerTicketDomainService`、`SelectedCharacterCurrentViewDomainService`、`OnlineExpiredItemCleanup`、`CharacterNameTagDomainService`、`GmPlayerRuntimeQuery`、`TownProjectionSlice` | `sessions.IsInWorld(s)` | 不变（InWorld，连接撤销与否都算） |
| `online.IsActiveExact(s)` | 不变（实现改为读合并后的记录） | 不变 |
| `activeCharacters.Publish(s)`（`PublishSelectedCharacterSessionCommandHandler`） | `sessions.EnterWorld(s)` | 不变 |
| 拆除中 `positions.ForgetExact`、`vitals.RemoveExact`、`runtimes.RemoveExact`、`activeCharacters.ClearExact`、`sessions.ReleaseExact`、`cooldowns.Clear`、`spiritPotion.ClearIfUnselected` | `runtime.EndExact(s)`、`sessions.ReleaseExact(s)` | 精确 |
| 发布中 `spiritPotion.Clear`、`runtimes.Publish`、`positions.Place`、`vitals.Publish` | `sessions.EnterWorld(s)`（在命令处理器里）、`runtime.Begin(s, …)` | 不变 |
| `positions.Find(s.CharacterId)`，调用方持有自己的会话 s（Replay、LeaveDungeon、DungeonClear、DungeonSelection、PartyWipe、TownProjection、TeleportConsumable、拆除与发布计划、GM 运行时查询、CurrentStateQuery、TownPositionEffect、ChurchRooms、WeddingSettlement 等） | `runtime.PositionOf(s)` | 收紧为精确 |
| `positions.Find(peer.CharacterId)`，读别的角色（两塔成员、副本参与者、队伍成员等） | `runtime.PositionOf(characterId)` | 不变 |
| `positions.CrossInto/StepTo(s, …)` | `runtime.CrossInto/StepTo(s, …)` | 不变 |
| `vitals.HpPercentOf(characterId)` | `runtime.HpPercentOf(characterId)` | 不变 |
| `cooldowns.IsCoolingDown(characterId, key)`（`PlayingCharacter`） | `runtime.IsCoolingDown(characterId, key)` | 不变 |
| `cooldowns.Start(characterId, …)`（提交时先查当前会话） | `runtime.StartCooldown(s, …)` | 精确 |
| `potion.ApplyForSession(s, sessions, …)` | `runtime.ApplySpiritPotion(s, …)` | 精确 |
| `spiritPotions.GetBonusRate(characterId)` | `runtime.SpiritPotionBonusRateOf(characterId)` | 不变 |
| `SelectedSessionSafety.IsBlocked(s)`、`blocked.Add(s)` | `sessions.StandingOf(s)?.Fenced`、`sessions.Fence(s)` | 不变 |
