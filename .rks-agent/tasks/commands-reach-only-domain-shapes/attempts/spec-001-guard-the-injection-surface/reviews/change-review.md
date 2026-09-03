---
kind: rks-review
verdict: approved
---

# 变更评审：守住注入面（spec-001）

评审基线 `9c0de6c6`，工作区未提交。评审者独立复核，不采信 `implementation-evidence.md` 与整改
自述的任何结论；下文每条判定均附本次自己跑出的证据。

本文件覆盖两轮：第一轮判 `changes_required`（两条阻断），第二轮复核整改，判 `approved`。

## 判据复跑（第二轮，整改后）

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
→ Protocol 488 / Networking 52 / Pvf 60 / Persistence 255 / Host 6 / Infrastructure 221(skip 2) / Application 925
→ 合计 通过 2007 / 跳过 2 / 失败 0
```

跳过仍为 2（两条缺档案用例），说明 `PVF_ARCHIVE_PATH` 生效。第一轮同样跑绿（2004/2/0），两轮
都未遇到基线那个偶发 SQLite 失败。

## 独立取证：命令处理器注入面全枚举

不复用仓库里的判据，另建一个 scratchpad 反射探针，直接加载
`Dfo.Server.Application.Tests/bin/Debug/net10.0/Dfo.Server.Protocol.dll`，用与
`CommandHandlerArchitectureTests` 相同的谓词独立枚举（整改后重跑一次，结果不变）：

```
HANDLER_COUNT=27
...(55 行逐参数输出)...
VIOLATIONS=0
```

27 个处理器（`AcknowledgeGameExitCommandHandler` 零依赖），其余 54 个构造器参数全部是
`Dfo.Server.Abstractions` 里以 `DomainService` / `DomainStore` 结尾的接口；唯一非领域形状是
`SelectCardCommandHandler(TimeProvider clock)`，命中豁免。枚举中不含 `ICharacterTable`、
`ISelectedSessionPositionPersistence`、`ISelectedSessionDirectory`、`ICommandBus`、任何
`*RuleService` / `*DefinitionSource` / `*Repository` / `*Registry`。

这同时证明判据**不是恒真**：同一套遍历在生产程序集上确实找到 27 个类型、55 个参数并逐个求值。

## 成功条件逐条判定

| ID | 判定 | 证据 |
|---|---|---|
| C-1 | 满足 | 上述反射枚举，55 个参数逐项归类；豁免仅 `TimeProvider`，理由写在 `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs:36-37` |
| C-2 | 满足 | 枚举中 `ICharacterTable` 与 `ISelectedSessionPositionPersistence` 均不出现。`TutorialLevelUpSlice.cs:113` 现为 `GrantTutorialLevelCommandHandler(ICharacterExperienceDomainService grant)`；`SelectCharacterPhaseCommands.cs:31-36` 已无持久化接缝 |
| C-3 | 满足 | `CommandHandlerArchitectureTests.cs` 的 `IsDomainShape` 是对任意 `Type` 求值的谓词（接口 + 程序集 + 后缀），不含类型名单；`Exempt`（:33-38）是唯一具名例外 |
| C-4 | 满足（本轮已实机变异证实） | 见「第二轮复核 · BF-2」的三次变异；另有 `ATypeOutsideTheVocabularyIsRefused` 覆盖谓词侧，整改新增的 `TheSweepFindsTheCommandHandlers` 覆盖遍历非空侧 |
| C-5 | 满足 | 全仓 `Server/src` 含 `Store` 的标识符逐个列出：领域 store 家族 `I/Sqlite*DomainStore(Session)` + `IDomainStore(Session)`；摆摊家族 `IExpertStoreRegistry`/`ExpertStoreRegistry`/`ExpertStore*`/`StoreOwner`/`HasOpenStore`/`OwnsOpenStore`/`PrivateStore`/`EncodeExpertStoreClose` **一字未动**（diff 中仅有的 `ExpertStoreClosed` 增删是 `SelectedSessionCleanupNoticeKind` 枚举整体从 `Application` 搬到 `Data`，值 1/2 未变）；内存容器 `OnlineStateStore`、PVF `ChunkStore`、被存记录 `Stored*` 均未动 |
| C-6 | 满足 | `grep` 全仓：`IActiveCharacterAccessDomainService` / `IActiveCharacterWriteScope` / `IAccountCargoGoldService` / `IEquipmentEligibilityCharacterReader` / `EquipmentEligibilityCharacterFacts` / `TryApplyCharacters` / `ReadCharacters` / `GetRequiredCharacter` 全部零命中，接口、实现、注册、测试同步移除，无半删。`ActiveCharacterRegistration` 现为 `(OnlineUserId, Session?)`（`OnlineStateStore.cs:182-184`），两个字段均有存活读点（`ClearExact` :112-118、`TryDisconnectConnection` :128-130） |
| C-7 | 满足（第一轮不满足，已整改） | 行为逐值不变见下节 1-5；断言证据缺口（原 BF-2）已由新增用例补上，并经我自己的三次变异实证 |
| C-8 | 满足（第一轮不满足，已整改） | `AGENTS.md:194-198` 四个已删类型名已改为改名后的名字；我对 `AGENTS.md` 全文的反扫只剩一个 baseline 就存在的失效名（见非阻断观察 1） |

### C-7 的行为逐值不变部分

第二轮已确认整改**未改动任何生产代码**（我把两轮的 `git diff 9c0de6c6 -- Server/src` 全文逐字符
比对，结果 `identical: True`），所以第一轮下列结论原样成立：

1. **用例数对账精确闭合。** 逐文件比对基线与现状的 `[Fact]`+`[InlineData]` 用例数，只有 5 个文件
   的用例数发生变化：
   - `Users/ActiveCharacterAccessTests.cs` **−17**、`Inventory/AccountCargoGoldServiceTests.cs` **−3**、
     `Characters/EquipmentEligibilityCharacterFactsTests.cs` **−1**（三个文件整删，随死件退役）
   - `Commands/CommandHandlerArchitectureTests.cs` 1 → 12 = **+11**（白名单 10 + 整改新增的遍历非空 1）
   - `Characters/CharacterEntryInventoryTests.cs` 13 → 15 = **+2**（整改新增的 `[Theory]` 两个分支）
   - 其余 30 个被改动的测试文件用例数**逐个不变**；`Dfo.Server.Application.Tests.csproj` 的改动只是
     链接编译项随文件改名。
   - 2015 − 21 + 11 + 2 = **2007**，与实跑一致（488+52+60+255+6+221+925=2007），无未交代的减少。
     协调方给的「2015 − 21 + 10 + 3 = 2007」是同一笔账的另一种分组，成立。
2. **改名未夹带行为改动。** 我自己对全量 `git diff -U0` 施加改名映射后逐文件对消：25 个文件残差为
   零（纯改名），其余文件的残差全部对应已知的结构改动（删死件、搬 record、折三步、判据新增），
   没有一处是期望值/断言的悄改。
3. **「三步折成一步」的次序变化不可观察。** `PrepareSelectedCharacterSessionCommandHandler` 里落库
   从「租约准备之后」移到「之前」。核对：
   - 该命令是 `CommandExecutionKind.Transactional`（`ProtocolModule.cs:138-140`），两处副作用同一事务收口；
   - `SelectedSessionPositionPersistence.PersistAsync` 在有环境事务时只 `SessionIn(ambient).WritePosition`
     （`CharacterStateAccess.cs:114-133`），是暂存写；
   - `PrepareLeaseAsync`（`SelectCharacterEntryInventoryDomainService.cs:32-40`）只经
     `ICharacterInventoryEntryDomainService.LoadAsync` → `ICharacterInventoryEntryRepository.LoadCharacterAsync`
     读库存，**不读也不写角色位置**，所以先写位置不会污染它的读；
   - 参数逐值相同：原 `positions.PersistAsync(command.AccountId, session.CharacterId, …)`，新
     `positions.PersistAsync(session.AccountId, session.CharacterId, …)`，而 `session` 由
     `SelectedSessionIdentity.Create(command.AccountId, selected.Identity.CharacterId, …)` 构造，二者恒等。
4. **在线表注册项收窄不丢可观察事实。** `Publish(SelectedSessionIdentity, ActiveCharacterState)` 收窄为
   `Publish(SelectedSessionIdentity)`；被删掉的 `Publish(AccountId,…)`、`GetRequired(AccountId)`、
   `ReadCharacter(s)`、`TryApplyCharacters` 在基线上就**没有生产调用方**（`git grep` 基线确认，唯一命中是
   `UserDomainServices.cs` 的门面自身）。`owner.BindCharacter(session.CharacterId)` 与原
   `character.Identity.CharacterId` 恒等（见第 3 条的构造式）。
5. **教学落库等价。** `TrySetProgressAsync` 实现为 `SetProgressAsync(...) == 1`
   （`CharacterExperienceDomainService.cs:27-30`），处理器的 `!= 1 → NotPersisted` 语义原样保留。

## 可观察要求逐条判定

| ID | 判定 | 证据 |
|---|---|---|
| RQ-001 | 满足 | 同 C-1 的 55 项枚举 |
| RQ-002 | 满足 | `IsDomainShape` 按 `Type` 求值；新增一个 `Abstractions` 内 `*DomainService` 依赖无需改判据（`ADomainShapeOrANamedExemptionIsPermitted` 已按类型验证该分支） |
| RQ-003 | 满足 | 枚举中无单表网关、无持久化接缝、无 `*RuleService`、无 `*DefinitionSource`/`*ContentSource`、无 `*Directory`/`*Registry` |
| RQ-004 | 满足 | 同 C-5。领域 store 唯一判别子是 `DomainStore` 后缀，摆摊/内存容器/被存记录三类都不带该后缀，不会混淆 |
| RQ-005 | 满足 | 同 C-4 |
| RQ-006 | 满足 | 同 C-6。另核：本次新增的五个 `Abstractions` 接缝（`ISelectCharacterPreparationDomainService`、`ISelectedSessionCleanupDomainService`、`ISelectCharacterSessionEntryDomainService`、`ISelectCharacterQuestEntryDomainService`、`ISelectCharacterPetEntryDomainService`）成员均有生产消费方 |
| RQ-007 | 满足 | 见上节 1-5 |
| RQ-008 | 满足 | `AGENTS.md` 反扫见非阻断观察 1；新增的「命令处理器只够得到领域形状」一节、后缀表、五个内部服务的裁决、分层判据自陈四处我逐条对过代码，均属实 |

## 第二轮复核：整改验证

### BF-1（`AGENTS.md` 的已删类型名）—— 已解决，且我做了全文反扫

`AGENTS.md:194-198` 现为：

```
- 载体协议（`Abstractions/Transactions/DomainStores.cs`）：`IDomainStore<TSession>`
  给出载体身份、是否持久、以事务换取会话；`IDomainStoreSession<TPlace,TKey,TValue>` 是
  「按位置读、按位置写、按位置清空」的共享词汇。`IItemDomainStoreSession` 直接就是它的
  一个具名实例；`ICharacterDomainStoreSession` 与 `IQuestDomainStoreSession` 不是——……
```

不满足于只看这一处：我把 `AGENTS.md` 里全部反引号标识符抽出来（140 个），逐个在 `Server/` 下
反查，只有 **1 个**在代码里不存在：

```
MISSING: PersistOnTeardownAsync      （AGENTS.md:212）
```

而它在基线 `9c0de6c6` 上就已经失效（`git grep PersistOnTeardownAsync 9c0de6c6 -- Server/` 零命中，
只有 `9c0de6c6:AGENTS.md:186` 命中自己）。**不是本次造成的**，不在 C-8「本成果触及的文档冲突」
范围内，记为非阻断（见观察 1）。

### 关于 `Docs/` 三处旧名 —— 裁决：**同意不改**

我看过三处的上下文，同意协调方的判断：

- `Docs/ProtocolScaffoldDissonances.md:224` 落在 `> **2026-08-27 已收口。**` 这个带日期的引用块里，
  是「当时是什么样」的记载；
- `Docs/Refactor/TableOwnershipRuling.md` 自己在开头就写明「The shape being ruled on is the one the
  database had when this was written … as of commit `9c535a9`. That is a starting point, not a
  description of the database today … Read the counts as history」——文档自陈为存档。

另外契约与 Spec 的口径也支持不改：C-8 与 RQ-008 说的是「仓库约定」，指名的是 `AGENTS.md`（含
`AGENTS.md:403` 的自陈），不含 `Docs/` 的历史裁决存档。**唯一值得留个尾巴**的是
`TableOwnershipRuling.md:1262/1264`——那两行不是叙述而是「怎么复核」的路径指引
（`… the accessor's return type in …/*Tables.cs`、`SqliteCharacterStore.cs` 和 `SqliteItemStore.cs`），
照着走会找不到文件。但该文档的存档声明已经覆盖它，且改动会波及一份已完成裁决，**不阻断**，
建议作为后续文档整理的候选（见观察 2）。

### BF-2（出生点落位/落库的生产实现零覆盖）—— 已解决，我自己做了三次变异

新用例 `CharacterEntryInventoryTests.cs:583-622` 经 `EntryServices.Create(..., positions:)` 装配的是
**真实**的 `SelectCharacterPreparationService`（`EntryServices.Create` 返回值就是该具体类型），
翻转 `PersistsPosition` 后直接调 `PlaceAtSpawnAsync`。

我没有采信协调方的变异结果，自己独立做了三次（每次先 `sha256sum` 备份、跑完还原、还原后
`sha256sum -c` 校验通过，`Server/src` 与整改后状态逐字符一致）：

| 变异 | 注入的缺陷 | 结果 |
|---|---|---|
| M1 | `if (placed.World.Channel.PersistsPosition)` → `if (true)` | **失败 1 / 通过 1**。`persists: False` 分支报 `Expected: null / Actual: CharacterPositionState { TownId = 1, AreaId = 0, X = 474, Y = 234, Direction = 5 }`（`CharacterEntryInventoryTests.cs:621`） |
| M2 | 去掉盖章：`var placed = prepared with { Character = … Position = resolved };` → `var placed = prepared;` | **失败 2 / 通过 0**。两个分支都报 `Expected: {1,0,474,234,5} / Actual: {1,1,10,20,0}`（`:620`） |
| M3 | 字段级错位：`spawn.Direction` → `0` | **失败 2 / 通过 0** |

**这正面回答了「是否只补了一半」**：不是。M2 是关键——`Actual` 停在种子位置 `{1,1,10,20,0}`
而不是出生点 `{1,0,474,234,5}`，说明 `PrepareAsync` **并没有**提前把位置盖成出生点，因此
`Assert.Equal(expected, placed.Character.Position)` 对「盖到角色身上」这一维是真的在守，不是恒真。
M3 进一步说明字段映射也被守住（测试端的 `expected` 是在测试文件里独立按字段拼出来的，不是从
生产代码借来的）。M1 守住频道判定这一维。三维齐全，我第一轮点名的那个真空已经补满。

还原核验：

```
$ sha256sum -c prep.sha
Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs: OK
$ 全量判据（还原后）→ 通过 2007 / 跳过 2 / 失败 0
```

### 非阻断建议的采纳

`TheSweepFindsTheCommandHandlers`（`Assert.NotEmpty(ProductionCommandHandlers())`）已加入，
观察 2（第一轮）随之关闭。

### 整改的范围核验

把两轮的 `git diff 9c0de6c6` 全文比对，整改只动了三个文件、且**生产代码零改动**：

- `AGENTS.md`：4 行改名
- `Server/tests/.../Characters/CharacterEntryInventoryTests.cs`：新增 `[Theory]` + `RecordingPositions` 替身 + 2 个 `using`
- `Server/tests/.../Commands/CommandHandlerArchitectureTests.cs`：新增 `TheSweepFindsTheCommandHandlers`

`git diff 9c0de6c6 -- Server/src` 两轮逐字符相同（`identical: True`），所以第一轮关于行为逐值不变的
全部结论不需要重新推导。

## 非阻断观察

1. **`AGENTS.md:212` 的 `PersistOnTeardownAsync` 在基线上就已失效。** 全仓零命中，基线亦然。
   不属本成果，建议顺手在下一次改 `AGENTS.md` 时一并核实那句话现在的正确写法。
2. **`Docs/Refactor/TableOwnershipRuling.md:1262/1264` 的两条「怎么复核」路径指向已不存在的文件。**
   文档开头的存档声明覆盖了它，本轮不阻断；若哪天有人真按那两行去复核会扑空，记为文档整理候选。
3. **T-005 的「计划修订」正当，但代价要记明，且我同意它属于后续成果而非本轮阻断。**
   把 `ISelectedSessionDirectory` 从两个命令处理器上摘掉是 RQ-001/RQ-003 强制的（它在
   `Abstractions` 里以 `Directory` 结尾，白名单必拒），Spec 的 Non-Goal「内存容器改说领域动作、
   把容器语义收进服务内部」与根契约 SC-5 指的是那三个注册表的私有化，与此不同——容器没有被
   私有化，也没有改说领域话。**代价**是 `ISelectCharacterSessionEntryDomainService` 现在多了三个
   逐字透传的容器动词（`SelectCharacterLifecycleServices.cs:123-129`：`Find` → `sessions.Find`、
   `RegisterReplacing` → `sessions.RegisterReplacing`、`ReleaseExact` → `sessions.TryRemoveExact`），
   处理器做的还是同样三件容器事，只隔了一层——**这条白名单可以被一个「改名壳」满足**。
   `ISelectedSessionDirectory` 在 `Protocol` 里仍有 25 处注入（全是消息 handler，属明列 Non-Goal）。
   **明确裁决：不构成本轮阻断**，但请后续成果（三个注册表转领域服务 / 拆大聚合）把这三个透传
   一并收口，别让它作为「已解决」被静默丢掉。
4. **`OnlineStateStore.Publish` 里那条运行时校验消失了。** 基线
   `if (character.Identity.CharacterId != session.CharacterId) throw new InvalidDataException(...)`
   随参数收窄而不复存在。我核过唯一生产调用点：`session` 由 `selected.Identity.CharacterId` 构造，
   `command.State.Character` 也源自同一个 `selected`，两者恒等，该分支在生产上不可达；基线也没有
   任何用例覆盖它。属「运行时检查变成结构上不可能」，不是行为改动。
5. **`OnlineUserServiceTests` 的断言迁移整体成立，但有一处只减不补。**
   `Assert.Same(character, service.GetRequired(accountId))` 这一族随「注册项不再存副本」而必然消失，
   改为经 `OnlineUserSnapshot.CurrentCharacterId` 与 `ClearExact` 的真假观察，强度基本等价
   （`Assert.False(ClearExact(旧 session))` + `Assert.True(ClearExact(新 session))` 同时守住了
   「注册项确实搬走了」）。唯一净减少：`:294-300` 那个用例删掉了 `ClearExact` 之后
   `Assert.Throws<KeyNotFoundException>(GetRequired(accountId))`，没有补等价的「注册项已不在」断言
   （`Assert.True(ClearExact(replacement))` 间接覆盖，但弱一点）。
6. **`SelectCharacterLifecycleTests` 的 `Assert.Throws<KeyNotFoundException>` → `Assert.Null(CurrentCharacterId)`
   换了观察维度。** 旧断言看的是「账号的活动登记被移除」，新断言看的是「在线用户的角色绑定被解除」，
   二者都由 `ClearExact` 一次完成，但新断言抓不住「忘了 `RemoveActive`」。该维度的覆盖迁到了
   `OnlineUserServiceTests` 的 `Assert.False/True(ClearExact(...))`，聚合起来没有真空，只是不再同处一文件。
7. **`SelectCharacterLifecycleTests` 的 `Preparation` 替身仍保留着 `PlaceAtSpawnAsync` 的复制品**
   （`:669-687`）。现在生产实现另有真测试守着（BF-2 整改），这份复制品不再是覆盖真空，但它是一份
   会漂移的第二实现：将来改了生产逻辑而忘了改它，lifecycle 用例的 ledger 序列断言会失去意义。
   建议后续让它委托真实实现。
8. **`CharacterEntryInventoryTests` 里既有的 `PublishCount` / `GetRequired` 一族断言在结构上恒真。**
   `Support/EntryServices.cs:34` 声明了 `activeCharacters` 参数，但 `:47-66` 的构造调用根本没把它传下去
   ——基线 `9c0de6c6` 即如此（已核对基线同一文件）。本次新增的三条 `PublishCount == 0` 同属此列，
   不是强度下降，但也没有增加保护。注意**新增的 `PlaceAtSpawn` 用例不受此影响**：它走的是
   `positions:` 这个真的被传下去的参数，变异验证已证实其有效。建议把 `activeCharacters` 那层
   替身单独修掉（接线或删除）。
9. **`OnlineStateStore` 与 `ISqliteStoreFacet` 仍带歧义的 `Store` 一词。** 计划显式推迟，且
   `DomainStore` 后缀已是唯一判别子，C-5/RQ-004 不受影响；记为下一轮词汇收敛候选。
10. **`ICharacterExperienceDomainService.TrySetProgressAsync` 无直测。** 它是一行委托，且
    `GrantTutorialLevelCommandHandler` 在基线上本就没有任何用例（`git grep GrantTutorialLevel 9c0de6c6 -- Server/tests`
    零命中），不构成回归，但新增的生产成员确实没有判据看着。
11. **`IExpertStoreRegistry` 未被误伤，已逐个确认。** diff 里出现的 `ExpertStoreClosed` 增删来自
    `SelectedSessionCleanupNoticeKind` 从 `Application/Sessions` 整体搬到 `Data/World`，枚举值
    `ExpertStoreClosed = 1` / `TownDeparture = 2` 逐字保留。
12. **搬到 `Data` 的四个 record 与三个附带类型形状逐字未变**（`SelectCharacterQuestSynchronization`、
    `SelectCharacterPetRevival`、`PvpParticipantPublicationTarget`、`SelectedSessionCleanupNotificationPlan`
    及其 `Notice`/`NoticeKind`/`NotificationTarget`），`Empty(session)` 工厂也原样保留。
13. **范围核查通过。** 未触碰 Non-Goals：三个注册表未转领域服务、`SelectedSessionRuntimeDirectory`
    与大聚合未拆、8 个直调写操作的 socket handler 未改、普通消息 handler 的注入面未纳入判据
    （`AGENTS.md:434-437` 也明说了判据只覆盖命令处理器）。In Scope 项目全部落地。

## 结论

C-1..C-8 与 RQ-001..RQ-008 **全部满足**。

行为保持这一面做得扎实：用例数 2015 − 21 + 11 + 2 = 2007 精确闭合，改名的 25 个文件残差为零，
折三步为一步的次序变化经事务性与 `PrepareLeaseAsync` 不碰位置两条独立核实为不可观察，死件三个
接缝加只写不读副本删得干净无残留。

判据这一面真的守得住：按类型判定、遍历非空（现已有判据自守）、可证伪，且我用外部反射探针
独立复算过 27 handler / 55 参数 / 0 违规。

第一轮的两条阻断已整改并经我独立复核：`AGENTS.md` 现在全文只剩一个 baseline 就存在的失效名；
出生点落位/落库的三个维度（盖章、字段映射、频道判定）各被一次变异实证守住。整改未动任何生产
代码。

`Docs/` 三处旧名同意不改，理由是它们是带日期的历史记载/自陈存档，而契约点名的「仓库约定」是
`AGENTS.md`。「白名单可被改名壳满足」这条我维持第一轮的判断——它是真实的，但属于后续成果的
范围而非本轮阻断，已在观察 3 里写清楚以免被静默丢掉。

**verdict: approved。**
