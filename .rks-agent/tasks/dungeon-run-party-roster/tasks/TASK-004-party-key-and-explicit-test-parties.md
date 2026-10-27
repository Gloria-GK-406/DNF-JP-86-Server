# TASK-004 — 队伍键查询、测试显式组队、删除无队伍回退

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付 Plan T-002，覆盖 Spec RQ-002、RQ-003、AC-003、AC-011。
  - `IPartyService` 提供 `KeyOf(CharacterId)`，语义见 Plan BD-001。
  - 所有构造副本服务的测试都显式提供队伍服务，多人入场前已组队。
  - 副本服务删除不带队伍服务的构造路径，以及 `parties is null` 时退回入场名单的分支。
  - 副本内部仍保留 `ActiveParticipants` 与 `runsByCharacter`，留待 T-003 处理。
- Owned boundary:
  - 生产代码：`Server/src/Dfo.Server.Abstractions/Parties/IPartyService.cs`（或 `IPartyService` 所在文件）、`Server/src/Dfo.Server.Application/Parties/PartyService.cs`、`Server/src/Dfo.Server.Protocol/Game/Party/PartyClientProjectionService.cs`、`Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs`。在副本服务中只允许删除构造路径和回退分支，并把 `parties` 字段改为非空。
  - 测试：`Server/tests/` 下所有测试文件，以及测试项目 `.csproj` 的链接项。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 无错误；
  - Application.Tests 与 Protocol.Tests 全部测试通过（缺 PVF 的按规则跳过）；
  - TASK-001～TASK-003 的表征测试断言未改（只允许改构造与夹具），用 `git diff` 检查这三份测试文件的断言行；
  - 新增 `KeyOf` 的单元测试，覆盖 AC-003 的三种情形。
- Dependencies: TASK-003
- Result review: required — 改动横跨生产接口与数百个测试构造。若迁移时改坏了某个测试的断言，或者组队方式与测试原意不符，后面 T-003 的行为判据就会失效，而全绿的测试暴露不了这种问题。

## Detailed Plan

**对 Plan BD-002 的细化**：测试改用**真实的 `PartyService`** 组队，不再新写一个实现 `IPartyService` 全部成员的替身。理由：
- 真实服务就是副本服务在生产中读取的队伍语义，替身容易与之偏离；
- 组队可以复用 TASK-001 已经验证过的“邀请 + 接受”写法；
- 构造态里的“图外角色”（AC-009）留到 T-004 再按需加专门夹具。

BD-002 的意图不变，即测试要显式表达队伍。

步骤：
1. **`KeyOf`**：在 `IPartyService` 加 `PartyKey KeyOf(CharacterId characterId)`，不给默认实现。`PartyService` 在 `lock (sync)` 下查 `membership`，有就返回队伍键，否则返回 `PartyKey.Virtual(characterId)`。协议层装饰器直接转发。所有测试替身补实现：能直接返回语义值的就返回，其余抛 `NotSupportedException`。在 Application.Tests 的队伍测试中新增 AC-003 的三个用例：在真实队伍中、不在队伍中、退队之后。
2. **测试组队辅助**：在 `Server/tests/Support/` 新增共享辅助（例如 `TestParties`），由两个测试项目链接引用。提供：
   - 创建真实 `PartyService`，所需依赖用测试桩；
   - 按入场名单组队：首位队长依次邀请其余成员，接受后核对队长与成员；
   - 一个副本服务工厂，返回带该队伍服务的 `DungeonRunDomainService`，并保留按需传入虚弱、宠物、生命周期、占用登记等可选依赖的能力。
   - 现有 Application.Tests 中的 `StubPartyMemberStatusDomainService` 与 `EmptyPartyMemberSessionEndpointSource`，按需移到共享目录或在 Protocol.Tests 中链接。
3. **迁移构造**：
   - 把 `new DungeonRunDomainService()`、`new DungeonRunDomainService(transactions)`、`new DungeonRunDomainService(null!, null!, ...)` 等不带队伍的构造，改为经工厂得到。
   - 多人入场前组队。测试专用扩展 `DungeonRunTestEntryExtensions.PreparePartyEntry/BeginParty` 可以在副本服务来自工厂时，按名单自动组队（尚未成队才组），以减少逐处改动；直接调用 6 参数实例方法的地方则显式组队。
   - 盘点结果中列出的“只结束部分成员”的测试，改为先让该成员在队伍中退队，再结束。
   - 只改构造与夹具，不改断言；确需改断言的，在 Result 中逐条列出并说明原因。
4. **删除回退**：删除 `DungeonRunDomainService()`、`DungeonRunDomainService(IDomainTransactionAccessor)`、`internal DungeonRunDomainService(IPartyService)` 这几个构造器，以及聚合中 `parties is null` 的分支；`parties` 字段与参数改为非空。`PreparePartyEntry` 中对 `this.parties is null` 的处理一并删除。
5. **检查**：完整运行 Application.Tests 与 Protocol.Tests。用 `git diff` 过滤三份表征测试文件，确认没有改动断言行。

## Result

- Outcome: 完成。`IPartyService.KeyOf` 已加入并实现；所有测试中的副本服务都经真实 `PartyService` 构造，多人入场前已组队；副本服务的无队伍构造路径与 `parties is null` 回退已删除。`ActiveParticipants`、`runsByCharacter` 与各离开路径未动，留给 T-003。
- Round 1，改动文件分类：
  - 生产代码（4 个）：`Abstractions/Parties/IPartyService.cs`（新增 `KeyOf`）、`Application/Parties/PartyService.cs`（在 `lock (sync)` 下查 `membership`）、`Protocol/Game/Party/PartyClientProjectionService.cs`（转发）、`Application/World/DungeonRunDomainService.cs`（删除 3 个构造器与 2 处回退分支，`parties` 字段、聚合字段和参数改为非空）。
  - 共享测试辅助（新增 1 个）：`Server/tests/Support/TestParties.cs`。
  - 测试项目链接（3 个 csproj）：Application.Tests、Protocol.Tests、Networking.Tests 链接 `TestParties.cs`；Protocol.Tests 与 Networking.Tests 另外链接 `StubPartyMemberStatusDomainService.cs`、`EmptyPartyMemberSessionEndpointSource.cs`。Networking.Tests 本来就链接 `DungeonRunTestEntryExtensions.cs`，因此需要一起改。
  - 测试替身补 `KeyOf`（10 个文件，12 个替身）：能直接给出语义值的返回 `PartyKey.Virtual` 或转发给内部服务（`PassiveParties`、`NoPartyService`、`VirtualOnlyPartyService`、`PartyMutationProbe`、`RecordingPartyMutations`、`DungeonProjectionParty`），其余抛 `NotSupportedException`。
  - 构造迁移（约 45 个测试文件）：`new DungeonRunDomainService()`、`(scope)`、`(parties)`、`(null!, null!, …)` 和目标类型 `new()` 一律改为 `TestParties.Runs(...)`。两个 DI 容器（`DungeonPickupTransactionTests`、`QuestClearVerticalTests`）原先靠 DI 选中单参构造器，现改为注册 `IPartyService`。
  - 显式组队（直接调用 6 参入场或经 Visit／Hell／Sao 入场的多人用例）：DungeonRunDomainServiceTests（`BeginPartyWithCell`）、DungeonCellFactoryTests、MonsterDeathTransactionTests、SaoTowerDomainTests（`Claim`）、DungeonVisitDomainServiceTests（2 处）、HellDungeonEntryTests（2 处）、DungeonRoomMechanismTests、DungeonEntryMaterialTests；Protocol 的 AdvanceHuntEnemyQuestCommandTests、AltarProjectionFailureTests、FinishQuestHandlerTests、GroundPickupTests（`BeginWithItems`）、MonsterDeathHandlerTests、QuestClearVerticalTests。
  - “先退队再结束”：DungeonRunDomainServiceTests 中盘点列出的 8 个用例，加上 `A_participant_replaced_by_a_new_run_cannot_ready_the_old_instance`（成员需先退队，才能单人开新局）；DungeonRoomMechanismTests 的 Loading_barrier…；SaoTowerDomainTests 的 First_party…（队长先退队，两人结束后最后一人也退队，晚到的入场由此组成自己的队伍）；AltarLifecycleTests 的 Member_departure…（`LeaveParty` 移到 `EndAsync` 之前）；QuestClearVerticalTests 的 PickerDeparted 分支；DungeonClearProjectionDeliveryTests 的 Participant_who_left_the_run_is_not_notified；DungeonCardSelectionProjectionDeliveryTests 的 Departed_and_replacement_generations…
  - 写入分类表：`GameplayWriteEntryTableTests` 把 `PartyService.KeyOf(CharacterId)` 登记为读取（R）。
  - 新增 AC-003 测试：`PartyServiceTests` 中的 `Key_of_a_real_party_member_is_that_party_key`、`Key_of_a_character_outside_any_real_party_is_its_virtual_key`、`Key_of_a_member_who_left_is_its_virtual_key_while_the_rest_keep_the_party_key`。
- Changed：生产代码共 +24／−27 行；测试共 63 个文件 +342／−207 行，另有新增文件 `TestParties.cs`。
- Why：
  - 测试使用真实 `PartyService`，不另写 `IPartyService` 替身，原因见 Detailed Plan。`TestParties.Runs` 用 `ConditionalWeakTable` 记住“副本服务 → 队伍服务”，供 `EnsureEntryParty`、`Leave` 和 `Of` 查询；不认识的副本服务（如 DI 构造的、`DepartureProbe` 的）不做自动组队。
  - 自动组队规则（`EnsureEntryParty`）：只对 `TestParties.Runs` 构造的服务生效；名单只有 1 人时不组队（走虚拟队伍）；队长已在真实队伍中时不动（交给测试自己和入场校验）；否则由名单中 `IsLeader` 的成员按槽位升序依次邀请、对方接受，组成后核对队长与成员集合。队伍按加入顺序分配槽位，所以“队长槽 0、其余顺序编号”的名单槽位不变；现有用例都符合这一形状。
  - `DungeonRunTestEntryExtensions.PreparePartyEntry`（以及经由它的 `BeginParty`、`Begin`）调用 `EnsureEntryParty`，减少逐处改动。直接调用 6 参实例方法的地方显式调用 `EnsureEntryParty` 或 `Form`。
  - 副本服务构造器中加入 `parties ?? throw new ArgumentNullException`：字段已非空，`null!` 传入时应立即失败，不能在运行中退化成另一条路径（RQ-002）。这超出“只删除”的字面范围，属于“字段改非空”的配套，请评审确认。
  - `transactions` 字段仍可为空（测试工厂对未传事务的情况传 `null!`，保留立即应用路径），不在本任务范围内。
- Checks：
  - `dotnet build Server/86JP.Server.slnx`：0 警告，0 错误。
  - `dotnet test tests/Dfo.Server.Application.Tests`：通过 5477，跳过 493（缺 PVF），失败 0。
  - `dotnet test tests/Dfo.Server.Protocol.Tests`：通过 1733，跳过 60，失败 0。
  - `dotnet test Server/86JP.Server.slnx` 全量，失败均为 0：Pvf 73/9 跳过；Infrastructure 655/348 跳过；HttpApi 59；Protocol 1733/60 跳过；Networking 189/20 跳过；Persistence 484；Host 200/12 跳过；Application 5477/493 跳过。
  - 生产 DI：`ApplicationModule` 注册了 `IPartyService`（协议层替换为装饰器），删除构造器后 DI 选中的仍是完整构造器。DungeonDepartureProtocolCharacterizationTests 等用生产注册解析副本服务的测试全部通过。
  - 表征测试 `git diff -U0`：`DungeonRunDomainServiceTests.Departures.cs` 与 `DungeonDepartureProtocolCharacterizationTests.cs` 无改动；`PartyCommandHandlerTests.cs` 只有 3 行改动：第 45 行和第 84 行把 `new DungeonRunDomainService(parties)` 换成 `TestParties.Runs(parties: parties)`，第 973 行为替身 `RecordingParties` 补 `KeyOf`（抛 `NotSupportedException`）。均不是断言。
  - 全部测试 diff 中删除的行只涉及构造或名单提取。唯一一行带 `Assert` 的删除是 `Assert.Null(new DungeonRunDomainService().TrySettle(Character))` 改为 `Assert.Null(TestParties.Runs().TrySettle(Character))`，断言本身不变。AltarLifecycleTests 中 `await runs.EndAsync(new(72), …)` 与 `Assert.True(parties.LeaveParty(new(72)).Succeeded)` 交换了顺序（断言行内容不变，只是改为先退队）。没有修改任何既有断言。
- Remaining（不阻断）：
  - `AltarLifecycleTests.Member_departure_leaves_the_remaining_party_running_without_repaying_the_departed_member` 依赖 PVF，本机跳过，“先退队”的调整没有实际跑过。
  - 构造态“图外角色”夹具（AC-009）按计划留到 T-004。
  - `EnsureEntryParty` 不处理“队长不在真实队伍、但某名成员已在别的队伍里”的情况（会因邀请失败抛异常）；当前没有用例走到这里，T-003 新增用例时如需要应显式组队。

## Review Round 1

```yaml
kind: rks-review
verdict: approved
```

- 类型：Independent Quick Review（任务单元结果快速审查），审查者独立于执行者。
- 对象：Result Round 1；合同 Revision 1，无修订；分支 `task/dungeon-participants-20261027`，HEAD `032eb7ff` 上的未提交工作区改动（4 个生产文件、67 个已跟踪测试文件／csproj、新增 `Server/tests/Support/TestParties.cs`）。
- 依据：reviewing-work 的 task-unit 与 code 准则；Spec RQ-002、RQ-003、AC-003、AC-011；Plan BD-001、BD-002、T-002、T-003；本文件 Detailed Plan。按要求未运行 build／test，测试结论采用执行者自报的全量结果，审查只读代码与 diff。

### 结论

1. **生产代码符合合同。**
   - `PartyService.KeyOf` 在 `lock (sync)` 下查 `membership`，命中返回 `party.Key`，否则返回 `PartyKey.Virtual(characterId)`。`LeaveLocked` 会从 `membership` 移除退队者，因此退队后返回虚拟键，符合 BD-001／AC-003。接口没有默认实现；装饰器 `PartyClientProjectionService.KeyOf` 直接转发给 `inner`。
   - `DungeonRunDomainService` 只做了这些改动：删除三个无队伍构造器；删除 `PreparePartyEntry` 与聚合 `Participants` 中 `parties is null` 的两处分支，非空分支的语义逐字保留；字段、聚合字段和参数改为非空。`ActiveParticipants`、`runsByCharacter` 与离开路径都没有改动。
   - `parties ?? throw new ArgumentNullException` 是字段改为非空的直接配套。没有这一行时，`null!` 传入后会在首次入场时才抛 NRE；有了它，构造时就会立即失败，直接落实 RQ-002 的“没有退回路径”。改动只有一行，不改变任何合法调用的行为，判定在合同允许范围内。
   - 生产 DI：`ApplicationModule` 第 807 行注册 `IPartyService`，`ProtocolModule` 第 218 行替换为装饰器。删除构造器后，`DungeonRunDomainService` 只剩一个公共构造器，`TryAddSingleton<IDungeonRunDomainService, DungeonRunDomainService>` 仍可构造。仓库内已不存在其他旧构造调用（`Tool/` 与 `src/` 中均未找到）。
2. **测试迁移没有改动断言。**`git diff -U0` 中测试的全部删除行只涉及构造器、名单提取或入场辅助的表达式体。唯一移动的断言行是 AltarLifecycleTests 中 `Assert.True(parties.LeaveParty(new(72)).Succeeded)`，从 `EndAsync` 之后移到之前，内容不变，属于 AC-011 允许的“让离开者在测试队伍中退队”。
   - 抽查“先退队再结束”的用例：DungeonRunDomainServiceTests 中的 9 处，SaoTowerDomainTests、DungeonRoomMechanismTests、QuestClearVerticalTests 的 PickerDeparted、两个协议投影投递用例。它们都在 `End` 前插入退队，原断言不变，测试原意（离开者不再收通知、剩余成员继续、旧局领取失效等）保持。SAO 用例补充的第二次退队是让晚到入场能重新组队，没有引入新断言。
   - 自动组队是否吞掉拒绝路径：测试中没有任何断言引用“roster no longer matches／entry party no longer exists”。`Assert.Throws<ArgumentException>` 的两处（第 411、418 行）直接调用 6 参实例方法，不经过 `EnsureEntryParty`，且是单人名单。`Failed_initial_cell_creation_does_not_publish_any_run` 未组队，但 `DungeonVisitDomainService` 先调用 `cells.CreateAsync`（第 102 行），再调用 `PreparePartyEntry`（第 117 行），所以异常仍来自造房。`DungeonEntryMaterialTests` 第 108 行是新夹具中的单人入场。三个原本就用真实队伍的用例（第 562、606、842 行），队长已在真实队伍中，`EnsureEntryParty` 不会介入，原有组队与后续入队、退队语义不变。未发现自动组队改变某个测试判据的情况。
3. **表征测试零改动。**`DungeonRunDomainServiceTests.Departures.cs` 与 `DungeonDepartureProtocolCharacterizationTests.cs` 在 `git status` 中无改动，前者的 `DepartureProbe` 原本就使用完整构造器。`PartyCommandHandlerTests.cs` 只改了第 45、84 行的构造，以及替身 `RecordingParties` 新增的 `KeyOf`，没有断言改动。
4. **`TestParties` 的形状可以承接 T-003。**它基于真实 `PartyService`。`Leave` 覆盖成员退队；`Of(runs)` 暴露真实队伍服务，被踢、转让队长都可以直接调用。由于增员守护放在协议 CMD（BD-006），T-004 的图外角色用真实服务的邀请加接受即可构造，无需替身。`ConditionalWeakTable` 按引用登记，不会阻止被 GC 回收，并发访问安全。
5. **新增的 AC-003 三个用例覆盖了在队伍中、不在队伍中、退队后三种情形**；写入分类表已把 `KeyOf` 登记为读取（R）。

### 非阻断建议

- `TestParties.Form` 只核对队长与成员集合，不核对槽位。副本成员视图取的是队伍分配的槽位，而不是名单槽位。T-003 若新增名单槽位不是从队长 0 号起连续编号的多人用例，槽位断言会在没有提示的情况下与名单不一致。建议在 `Form` 末尾增加一项检查：确认 `formed.Members` 的槽位与名单槽位一致，不一致时立即失败。
- AltarLifecycleTests 的 `Member_departure_…` 依赖 PVF，本机已跳过，调整顺序后没有实际执行过。T-003 改动离开顺序后，应在具备 PVF 的环境中补跑一次。
- SaoTowerDomainTests 第二次退队上方的注释写成 “The last member's party dissolves too”。实际情况是，该成员退队后，成为单人的真实队伍随之解散。措辞可以更准确，但这不影响结论。

### 审查后补充（提交前）

采纳 Review Round 1 的非阻断建议 1：`TestParties.Form` 组队后再核对每名成员的槽位与入场名单一致，不一致立即失败。复跑 Application.Tests（5477 通过 / 493 跳过）、Protocol.Tests（1733 / 60）、Networking.Tests（189 / 20），均 0 失败。建议 2（有 PVF 的环境补跑 `AltarLifecycleTests`）留到最终验证阶段处理。
