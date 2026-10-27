---
kind: rks-review
target: git 13c9e2d6..e337660b
verdict: approved
reviewer: independent subagent (reviewing-work, code)
---

# 完整改动审查：在线身份状态机与在线角色运行时

## 审查范围

- 目标：实现提交 `e337660b`（`git diff 13c9e2d6 e337660b`，177 个文件，+3390/−1860），基线 `7fc8acdc`；工作区无未提交改动。
- 依据：`TaskContract.md`（revision 2）、`spec.md`（RQ-001～RQ-018、AC-001～AC-012、调用点映射表）、`plan.md`（direct）、`verification/baseline.md`。
- 判据：reviewing-work 的 Code 参考（完整改动），文档部分按 Deliverable 判据核对与实现是否一致。
- 未提供挑战集，本轮不做挑战裁定。

## 关键证据

1. 构建：在 `Server/` 下 `dotnet build 86JP.Server.slnx`，0 个错误。
2. 测试：工作区根目录有 `Script.pvf`，`dotnet test 86JP.Server.slnx --no-build` 全量结果见“测试复核”一节。
3. AC-001 检索：在 `Server/src` 中检索 `SelectedSessionRuntimeDirectory|IActiveCharacterDomainService|activeByAccount|ICharacterPositionDomainService|ICharacterVitalRuntimeDomainService|ICharacterItemCooldownDomainService|ISpiritPotionDomainService|SpiritPotionDomainService|CharacterPositionDomainService|CharacterItemCooldownDomainService|CharacterVitalRuntimeDomainService|TryGetPositionPersistence|IsPublishedExact|ActiveCharacterDomainService`，零命中；`SelectedSessionSafety` 只剩连接级 `ConditionalWeakTable`；`[Obsolete]` 在基线和目标中都是 0。
4. AC-008 检索：`OnlineCharacterRuntimeDomainService.cs` 中没有 `lock`、`Monitor`、`SemaphoreSlim`、`Lock`、`Mutex`。
5. 逐段对照了基线 `OnlineStateRegistry`、`SelectedSessionDomainService`、`SelectedSessionRuntimeDirectory`、四个旧运行时服务、`SelectedSessionSafety`、拆除/发布服务，与目标实现逐一比较语义。

## 逐项判断

### 在线身份（RQ-001～RQ-010）

- RQ-001/002/016：`OnlineStateRegistry` 在同一个 `sync` 下持有 `byId`、`byConnection`、`selections`、`endpoints`；`SelectedSessionDomainService(OnlineStateRegistry)` 是无状态门面，同时实现 `ISelectedSessionDomainService` 与 `ISelectedSessionSnapshotQuery`；DI 注册已同步（`ApplicationModule.cs`）。`Find*`/`Snapshot` 返回任意阶段的记录，语义与旧选中会话目录一致。
- RQ-003：`StandingOf` 按账号取记录再要求值相等（`FindExact`），对其他代数返回 null；`IsInWorld` 是精确且 `InWorld`。
- RQ-004 `Select`（`OnlineStateRegistry.cs:139`）：跨账号冲突检查与旧实现相同；同一会话重复选中时保持原记录（含阶段与隔离标记）。旧实现中阶段存在运行时目录里，重复选中同样不影响阶段，返回值也同为“被顶替者 = 自身”，所以等价。
- RQ-005 `EnterWorld`（`:159`）：要求当前精确选中，再按账号+连接+代数找在线用户，找不到就抛出；成功后记录 `InWorld`、记下所属在线用户、在线用户代数加一。与旧 `Publish`→`BindCharacter` 的效果一致；旧实现不检查“是否当前选中”，但唯一调用点随后的 `sessionEntry.Publish` 会因不是当前选中而抛出，新实现只是把拒绝提前且没有副作用，符合 RQ-005。另有“重复进入同一会话视为无操作”的分支，见非阻断风险 R-1。
- RQ-006 `ReleaseExact`（`:198`）：只删除精确记录；`InWorld` 且所属在线用户仍登记在同一连接上时代数加一。旧流程是 `ClearExact`（解绑并加一）再 `ReleaseExact`（不加一），新流程只调一次，代数增量相同。调用点（`SelectCharacterPhaseCommands.cs:65/99/136/360`、拆除服务 `:193`）都经过拆除服务，没有出现“重复加一”。
- RQ-007 投影：`SnapshotOf`（`:334`）只在记录是 `InWorld` 且 `Owner == user.Id` 时带出角色并报告 `CharacterActive`；`OnlineUser.State` 只剩 Connected/Authenticated，解绑时自然回到 Authenticated。认证参与者的两处检查改用 `BoundCharacter`，当 `AccountId` 为空时它恒为 null，与旧的 `CurrentCharacterId is null` 等价。
- RQ-008 `IsActiveExact`（`:256`）：精确、`InWorld`、所属在线用户仍在、连接一致、游戏端点、账号一致、`byConnection` 映射回所属在线用户。旧实现的 `CurrentCharacterId == session.CharacterId` 由“所属在线用户”关系替代，等价。
- RQ-009：`TryDisconnect`/`TryDisconnectConnection` 统一走 `Remove`（`:305`），不再动选中记录；`IsRevoked`（`:319`）在连接没有登记、账号不同或所属在线用户不同时报告撤销。`IsInWorld` 在断线后仍为真，与旧运行时目录在断线后仍保留一致。
- RQ-010：`DemandCurrent`（`SelectedSessionSafety.cs:44`）改为 `StandingOf(session) is not { Fenced: false }` 时拒绝，即“不是当前”或“已隔离”都拒绝；服务未注册时仍然拒绝，与旧逻辑一致。`BlockAsync` 先 `Fence` 再加连接栅栏，连接级栅栏未改动。

### 在线角色运行时（RQ-011～RQ-014）

- RQ-011：新接口与实现到位，四个旧服务与接口已删除，调用方按映射表迁移。持有自己会话的调用点改为 `PositionOf(session)`；两塔、离开副本、回城、踢出队员等读同伴位置的调用点，传入的会话都刚由 `FindByCharacter` 取得或已验证是当前会话，精确读取与按角色读取结果相同。
- RQ-012/013 CAS 实现：`Entry` 是 sealed record，自定义 `Equals(Entry?)` 为 `ReferenceEquals`、`GetHashCode` 为 `RuntimeHelpers.GetHashCode`。`ConcurrentDictionary.TryUpdate(key, next, current)` 与 `TryRemove(KeyValuePair)` 都用 `EqualityComparer<Entry>.Default` 比较，因此比较的是引用：只有读到的那一份才会被替换或移除，`with` 产生的新实例不会被误判为相等。`TryChange` 在归属会话不符或记录不存在时返回 null，`Move` 抛出、冷却与灵药忽略；只有 `Begin` 会插入，所以 `EndExact` 之后迟到的写入不会重建记录。`Begin(new)` 与旧会话 `EndExact`/`TryChange` 交错时，比较失败后重读，会发现会话不符而退出，不会误删或改到新会话。不丢更新：同一记录上的并发 `StepTo`/`StartCooldown` 通过重试循环串行化。
- 效果处理器：`StartItemCooldownEffectHandler`、`StartSpiritPotionEffectHandler` 仍在提交参与者中写入，`ValidateAsync` 保证 `context.Session` 不为空，`context.Session!` 安全。旧实现“提交时查当前选中”改为“提交时检查运行时记录的归属”，在发布之后两者等价，符合 RQ-012 的收紧。
- RQ-014（`SelectCharacterLifecycleServices.cs:102-117`）：只在 `runtime.PositionOf(session)` 有值时才解析频道；解析通过 `RunAsync` 执行，抛错记入 `failures`，`persists` 保持 false，从而跳过落库，其余拆除步骤照常执行，最后抛出 `SelectedSessionCleanupFailure`。频道来自 `IChannelEntryPolicy.ResolveAsync(session.ListenerPort)`，与选角入口用的是同一来源（`SelectCharacterHandler.cs:97`），`!IsTransient` 与入场时的 `PersistsPosition` 定义一致（`EntryPlacementRuleService.cs:46`）。
- RQ-015：发布顺序是 `EnterWorld`（命令处理器）→ `creatures.Enter` → `runtime.Begin` → `townAreas.Enter`。拆除时 `EndExact` 一次清掉四类状态，容器清理仍以 `FindByCharacter is null` 为条件。旧实现的冷却清理以“角色未被重新选中”为条件；但在生产选角流程中，旧会话总是先清理并释放、再 `Select`，`Select` 返回非空时直接抛出，所以“拆除时同角色已有新会话”的情形不会出现，新实现中 `Begin` 清空冷却与灵药，与旧行为一致。

### 测试迁移

- 新增测试 `OnlineIdentityTests`（10 个）覆盖 AC-002～AC-006，`OnlineCharacterRuntimeTests`（8 个）覆盖 AC-007/AC-008；`SessionGenerationOwnershipTests` 新增的两个测试与改写后的 Theory 覆盖 AC-009 的四种情形；`OnlineUserServiceTests.A_rejected_connection_is_cleaned_up_by_its_disconnect_and_leaves_nothing` 覆盖 AC-010 新增的流程。
- 有意改动 (a)：`Disconnect_OnlyAcceptedOwnerRemovalClearsActiveCharacter`。按 RQ-009，断线后选中记录要保留到拆除；`ClearExact` 在测试夹具里已映射到 `ReleaseExact`，若保留原断言就等于要求断线时删除选中记录，与 Spec 相冲突。改为断言 `IsActiveExact` 为假且报告 `ConnectionRevoked`，仍然验证了原测试的核心：断线后会话不再算活跃。成立。
- 有意改动 (b)：`Queued_cleanup_revalidates_published_session_after_account_gate`。旧测试依赖“运行时目录已删、活跃登记仍在”这一状态，这只是旧拆除过程中的一瞬间，新模型里不存在。改为“释放后重新选中（Selected 阶段）”，排队的命令在管线 `Capture` 处因在线用户没有绑定该角色而被拒绝。物品数与待投递事件的断言保持不变。按任务的结构变化，这一改动站得住。生产路径中 `OnlineExpiredItemCleanupJob` 遇到 Selected 阶段会话时被拒绝的行为，与基线相同（基线中 Selected 阶段的 `CurrentCharacterId` 同样为空）。
- 测试替身的默认值“当前选中即在世界”：只在替身自身判定为当前选中时才报告 `InWorld`，不改变对非当前会话的拒绝。被替换的旧替身原本要么是始终返回 true 的 `PublishedSessions`，要么配合真实运行时目录 `Publish`，所以新默认值与原测试的前提相同。需要区分“未发布”的测试（`ExperienceBonusDomainServiceTests` 的 `published` 参数、`CharacterSkillLearningTests`、`TauKingdomTests`、`MoveItemSpaceHandlerTests` 的 `NoPublishedRuntime`）都保留了未发布分支。没有发现它掩盖生产路径问题，但见非阻断项 N-1。

### 文档（RQ-018）

`AGENTS.md`（`Users/`、`Sessions/` 段落、状态主人表、“在线身份只有一条记录、一把锁”）、`Document/SelectedSessionCurrentView.md`、`Document/成长契约与精灵秘药经验.md`、`Docs/CharacterEntryFacesInventory.md`（补注）都与实现一致。描述也准确：撤销后记录保留、`StandingOf` 报告 `ConnectionRevoked`、拆除时由频道推导是否落库、秘药随会话记录清除。

## 发现

无阻断发现。

## 非阻断风险与建议

- N-1（测试失去意义，建议修正）：`Server/tests/Dfo.Server.Application.Tests/Inventory/ApplyEffectsTests.cs:120`、`:145`、`:173` 三个测试构造了新的 `OnlineCharacterRuntimeDomainService`，但从未 `Begin` 运行时记录。于是 `StartCooldown`/`ApplySpiritPotion` 无论事务回滚、提交失败还是会话被替换都会被忽略，“没有冷却/秘药”的断言恒为真，测试不再能区分“回滚不授予效果”与“效果从未生效”。同一性质的保护在别处仍有：`SpecialConsumableEffectTests` 的 `Assert.Equal(!fail, IsCoolingDown(...))` 和 `UseStackableHandlerTests` 中秘药提交前为 0、提交后为 1、回滚、释放、替换这几组测试，所以不构成阻断。最小修正：在这三个测试里先对 `sessions.Current` 执行 `Begin`（替换测试中对替换后的会话 `Begin`，或断言原记录未被写入），并补一条成功提交后确实记录冷却的正向对照。
- R-1：`EnterWorld` 对“已在世界、所属在线用户仍在同一连接”的同一会话直接返回（`OnlineStateRegistry.cs:166-170`）。基线中第二次 `Publish` 会因代数已推进而抛出。生产中每个会话只有 `PublishSelectedCharacterSessionCommandHandler` 一个调用点，且只调用一次，所以当前不可达；但如果将来出现重复发布，随后的 `runtime.Begin` 会清空冷却与灵药。建议删除这个分支，按 RQ-005 抛出；或者说明它为何必要。
- R-2：会话隔离标记随记录在 `ReleaseExact` 时一起消失，而基线 `blocked` 集合是按值永久保留的（同时也是一处无界增长）。只有“同一会话值在释放后被原样重新选中”时两者行为才不同，这要求同一连接、同一代数（Selected 阶段释放不推进代数）。`BlockAsync` 带连接时会立起连接栅栏并请求关闭连接；`SelectedSessionExecutionBehavior.cs:36` 以 `new(target, null)` 调用时没有连接栅栏，但这类会话已处于 `InWorld`，释放时代数会推进。目前没有找到可达路径，记为残余风险。
- R-3：`Docs/领域结构重构设计.md:111` 仍以现在时引用 `activeByAccount`；该文档开头声明“不要把本文的形状当成今天的代码”。`Docs/Refactor/SelectCharacterMigrationMatrix.md`、`Document/outputs/...`、`Document/Verification/...` 中的旧类型名属于历史记录。`OnlineUserConnectionObserver.cs:79-81` 的注释仍写“selected directory”。可以顺手更新，不影响 AC-012 的实质。
- R-4：`OnlineStateRegistry.Select` 在“出现被顶替者”时已经覆盖了旧记录，随后选角处理器才抛出“在生命周期门之外被改动”（`SelectCharacterPhaseCommands.cs:106-108`）。在这种不变量被破坏的路径上，被覆盖会话的所属在线用户不会再推进代数，而基线会在它的 `ClearExact` 时推进。该路径受账号生命周期门保护，属于防御分支。
- 验证证据（EV-001～EV-005）尚未写入 `verification/`，属于后续完成验证阶段的事项，不由本审查替代。

## 测试复核

审查者在 `e337660b` 上独立运行 `dotnet test 86JP.Server.slnx --no-build`，工作区根目录有 `Script.pvf`：

| 测试项目 | 通过 | 失败 | 跳过 | 总计 | 与基线比较 |
|---|---|---|---|---|---|
| Dfo.Pvf.Tests | 80 | 0 | 0 | 80 | 相同 |
| Dfo.Server.HttpApi.Tests | 50 | 0 | 0 | 50 | 相同 |
| Dfo.Server.Persistence.Tests | 475 | 0 | 0 | 475 | 相同 |
| Dfo.Server.Protocol.Tests | 1694 | 0 | 0 | 1694 | 相同 |
| Dfo.Server.Networking.Tests | 97 | 0 | 0 | 97 | 相同 |
| Dfo.Server.Infrastructure.Tests | 967 | 0 | 2 | 969 | 相同 |
| Dfo.Server.Application.Tests | 4304 | 2 | 16 | 4322 | +21 |
| Dfo.Server.Host.Tests | 165 | 0 | 0 | 165 | 相同 |

失败的只有基线已有的 2 个 `DimensionDungeonTests`（`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`）。Application.Tests 增加 21 个，与作者自报的新增测试数（10 + 8 + 2 + 1）一致。

## 复审（e337660b..e9498353）

```yaml
kind: rks-review
target: git e337660b..e9498353
verdict: approved
reviewer: independent subagent (reviewing-work, code)
```

### 范围与依据

- 目标：修复提交 `e9498353`（15 个文件，+77/−30）。针对首轮的 N-1、R-1、R-3 复审，并检查这些修复是否引入新的回归。R-2、R-4 按协调者的处置继续作为残余风险，不再复审。
- 依据与首轮相同：Spec 中的 RQ-005、RQ-012、RQ-013、AC-010，以及 reviewing-work 的 Code 判据。

### 关键证据

- 构建：在 `e9498353` 上 `dotnet build 86JP.Server.slnx`，0 个警告，0 个错误。
- 全量测试：工作区根目录有 `Script.pvf`，`dotnet test --no-build` 的结果是 Application.Tests 4305 通过、2 失败、16 跳过，共 4323；失败的仍只有基线已有的 2 个 `DimensionDungeonTests`。比首轮多 1 个，即新增的正向对照。其余 7 个测试项目的数量与基线一致，全部通过。

### 逐项判断

- **N-1：已解决。**
  - `ApplyEffectsTests.cs` 中的 `Late_effect_failure…` 和 `Failed_transaction_commit…` 现在先对 `sessions.Current` 执行 `Begin`，冷却与灵药用同一个运行时实例。这样“回滚或提交失败后不授予效果”的断言重新有了区分力。
  - `Replaced_session_does_not_receive_an_old_cooldown_commit` 中，旧会话先有自己的记录；替换会话在旧提交落地前 `Begin` 了自己的记录。断言替换者没有冷却，正好对应 RQ-012/AC-007 在效果提交层面的精确隔离。
  - 新增的正向对照 `Committed_consumption_records_the_cooldown_and_potion_for_its_session` 断言：提交前没有冷却；提交后消耗了物品、开始冷却、灵药倍率为 1。这证明前面几个“无效果”断言不是因为效果从未生效而恒为真。
- **R-1：已解决。**
  - `OnlineStateRegistry.EnterWorld`（`:159-176`）删除了“已在世界即无操作”的分支。
  - 对已进入世界的同一会话再次调用时，所属在线用户的代数已经推进，按账号+连接+代数查找会失败并抛出，与基线 `Publish` 第二次调用的行为一致，符合 RQ-005。
  - 首轮也确认过，生产中每个会话只有一个调用点，且只调用一次，所以这次收紧不影响生产路径。
- **R-1 的测试侧改动：`EnsureInWorld`（`tests/Support/TestSessions.cs`）。**
  - 它只在 `!IsInWorld(session)` 时调用真实的 `EnterWorld`，所以进入时的全部校验（当前选中、在线用户、代数）都照常执行，只跳过“夹具已经进入”这种重复调用。
  - 18 处替换的都是迁移过程中新写的测试准备步骤，没有一处断言依赖“第二次进入”这件事本身。
  - `GmPlayerDirectoryHttpTests` 中“进入后代数推进”的断言仍然成立：该会话此前只做过 `Select`，`EnsureInWorld` 会真正执行一次进入。
  - `OnlineIdentityTests` 与 Networking 集成测试仍直接调用 `EnterWorld`，状态转移的覆盖没有减弱。
- **R-3：已处理。**
  - `OnlineUserConnectionObserver.cs:79-81` 的注释改为“已选中的会话仍报告为已撤销，其余资源需要完整清理”，与 RQ-009 的语义一致。
  - `Docs/领域结构重构设计.md` 是开头已声明不代表现状的历史设计文档，保持不动，可以接受。

### 发现

无阻断发现。

### 残余风险

- R-2（隔离标记随释放一起消失）与 R-4（`Select` 在防御分支中覆盖了被顶替者的记录）仍按首轮的描述保留。
- 小建议，不构成阻断：可以在 `OnlineIdentityTests` 中补一条“同一会话第二次 `EnterWorld` 抛出，且在线用户代数不变”的断言，把 R-1 恢复后的严格语义固定下来。
- 验证证据 EV-001～EV-005 仍待在完成验证阶段写入 `verification/`。
