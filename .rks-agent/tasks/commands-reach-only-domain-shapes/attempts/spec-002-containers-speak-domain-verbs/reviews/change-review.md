---
kind: rks-review
verdict: approved
---

# Change Review: 内存容器改说领域话

评审基线 `70a3e54e`，未提交工作树。`git diff -M 70a3e54e --stat` = 59 个文件、305 增、362 删
（`.rks-agent/` 下的 SDD 产物按指示不计入产品判断）。所有结论由本评审自行取证，未采信
`implementation-evidence.md` 的任何自述。

**两轮**：第一轮判定 `changes_required`，两条阻断均在 `AGENTS.md`（一条与代码矛盾的类别记载、
一处把后缀表截断的段落插入）。整改后第二轮复核关闭，见「整改复核（第二轮）」——
整改是纯文档的，`Server/` 的 diff 与第一轮逐字节相同，故第一轮的判据结果继续成立。

判据（本评审自跑，非引用）：

- `dotnet build Server/86JP.Server.slnx --nologo` → `0 个警告 0 个错误`
- `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` → 全绿，逐程序集
  52 + 488 + 60 + 255 + 6 + 221 + 925 = **2007 通过 / 2 跳过 / 0 失败**，与基线一致。
  已知偶发的 `OutsideTransactionTests` 本次未出现。

---

## Success Conditions

### C-1 四个接缝的名字说出它们是领域服务，且各自的内存容器不出现在接口上 —— 通过

四个接口在 `Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs`，逐成员核对：

| 接缝 | 行 | 成员 | 判断 |
|---|---|---|---|
| `ISessionPresenceDomainService` | 42–68 | `Admit` / `Withdraw` / `WithdrawExact` / `Find(CharacterId)` / `InArea(town, area, excluding, listenerPort?)` | 全是领域动词与领域提问。`InArea` 是按区域与监听端口过滤的提问，不是整表列举 |
| `ISelectedSessionDomainService` | 71–88 | `Select` / `ReleaseExact` / `Find(AccountId)` / `FindByCharacter` / `FindByWireUserId` / `FindByConnection` | 两个动词 + 四个单值提问，无列举 |
| `IExpertStoreDomainService` | 136–150 | `Open(CharacterId)` / `Open(session)` / `Close` / `CloseExact` / `HasOpenStore` | 全是摆摊语义的领域动词 |
| `IPvpLobbyDomainService` | 187–208 | `MarkReady` / `ClearReady` / `ClearReadyExact` / `Offer` / `Withdraw(roomId)` / `ReadyOn(port, excluding)` / `OfferedOn(port)` | `ReadyOn` / `OfferedOn` 按监听端口过滤，是提问不是列举 |

四个实现里的字典均为私有字段（`SelectedSessionDomainService.cs:10-11`、
`SessionPresenceDomainService.cs:14`、`ExpertStoreDomainService.cs:11`、
`PvpLobbyDomainService.cs:11-12`），接口上不出现容器本身。

`Current()` 确已删净——全仓 `grep -rn "\bCurrent()" --include=*.cs Server/` **零命中**
（接口 1 处、实现 1 处、15 个测试替身、1 条断言）。四个接口上无其他整表列举或容器操作残留。

旧标识符全仓零命中：`ISelectedSessionDirectory` / `SelectedSessionDirectory` /
`ISessionPresenceRegistry` / `SessionPresenceRegistry` / `IExpertStoreRegistry` /
`ExpertStoreRegistry` / `IPvpLobbyRegistry` / `PvpLobbyRegistry` / `OnlineStateStore` /
`RegisterReplacing` / `TryRemoveExact`，`Server/**/*.cs` 下一处不剩。

### C-2 命令处理器直接向会话领域服务提问；透传成员消失 —— 通过

`ISelectCharacterSessionEntryDomainService` 成员对照
（`Server/src/Dfo.Server.Abstractions/Sessions/ISelectCharacterLifecycleServices.cs`）：

- 改动前 8 个：`PlanPvp` / `PlanPvpPublication` / `Publish` / `ReplaceRuntimeExact` /
  `MarkPvpReady` + `Find(AccountId)` / `RegisterReplacing` / `ReleaseExact`
- 改动后 5 个（14–19 行）：后三个连同实现
  （`SelectCharacterLifecycleServices.cs` 原 119–128 行的三个一行转发）一并删除

两个命令处理器的构造器已改为直接注入
（`Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs`）：

- `PrepareSelectedCharacterSessionCommandHandler`:34 —— `ISelectedSessionDomainService sessions`
- `CompensateSelectedCharacterSessionCommandHandler`:303 —— `ISelectedSessionDomainService sessions`

调用点 53 / 67 / 91 / 102 / 139 / 320 全部改为 `sessions.*`，语义与旧透传逐点对应
（`Find`→`Find`，`RegisterReplacing`→`Select`，`ReleaseExact`→`ReleaseExact`）。

**没有在别处新长出等价的壳**：`git status --short` 只有 `M` 与 `RM`，无新增产品文件；
按重命名映射做的多重集残差比对（见 C-5）在全部 55 个 `Server/` 文件里没有出现任何新的转发成员。
`ISelectedSessionDomainService` 的实现全仓只有 `SelectedSessionDomainService` 一个（其余命中均为
测试替身），没有第二层包装。

### C-3 判据仍通过，且不是靠放宽规则或扩充豁免 —— 通过

`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` 的
全文 diff **只有一行**：

```
-    [InlineData(typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionDirectory))]
+    [InlineData(typeof(Dfo.Server.Abstractions.Sessions.ISelectedSessionRuntimeDirectory))]
```

`Exempt`（33–38 行，仍只有 `TimeProvider`）、`IsPermitted`（128 行）、`IsDomainShape`
（139–143 行：接口 + `Assembly == Abstractions` + 后缀 `DomainService`/`DomainStore`）
一个字未动。反例条目数 5 未变。

**反例强度**：`ISelectedSessionRuntimeDirectory`（`ISessionPresence.cs:90-100`）在同一
`Dfo.Server.Abstractions.Sessions` 命名空间，成员为 `Publish` / `ReplaceExact` /
`GetRequired` / `TryGet` / `RemoveExact` —— 纯粹的取出/塞回容器动词，比旧反例
（它还带四个 `Find*` 领域提问）更彻底地代表「reaching a container」那一类，强度不降反升。

**本评审自做两次变异（均已还原并校验）**：

1. 给 `CompensateSelectedCharacterSessionCommandHandler` 加一个真实使用的
   `ISelectedSessionRuntimeDirectory` 参数 →
   `ProductionCommandHandlers_OnlyTakeDomainShapes` **FAIL**
   （`Assert.Empty() Failure: Collection was not empty ["CompensateSelectedCharacterSessionCommandHandler(I"…]`）
2. 在 `IsDomainShape` 后缀表里加 `|| type.Name.EndsWith("Directory")`（模拟「白名单被壳满足」）→
   `ATypeOutsideTheVocabularyIsRefused(type: typeof(ISelectedSessionRuntimeDirectory))` **FAIL**

还原后三个被动过的文件 md5 与变异前备份逐字节相同，`git diff -M 70a3e54e --stat` 回到
`59 files changed, 303 insertions(+), 360 deletions(-)`。

### C-4 领域层里 `store` 只剩一种读法 —— 通过

全仓声明中含 `Store` 的类型逐个分类：

- **事务结算真源**：`IDomainStore` / `IDomainStoreSession` / `ICharacterDomainStore` /
  `IItemDomainStore` / `IQuestDomainStore`（+ `*Session`）/ `Sqlite*DomainStore*` /
  `VolatileItemDomainStore*` / `MarkDomainStore` / `RecordingCharacterDomainStore` /
  `StoreUnderTest` / `StoreBackedItemContainer` / `AmbientStoreAccess`（`Application`，指领域 store 的取用）
- **玩家摆的摊**：`IExpertStoreDomainService` / `ExpertStoreDomainService` /
  `ExpertStoreEntryState` / `ExpertStoreClosedMessage` / `StoreOwner`（`ExpertStoreDomainService.cs` 私有）
  —— `Expert` 前缀使语义仍可仅凭名字读出
- **不在领域层**：`ChunkStore`（`Dfo.Pvf/Chunks/`，归档解析）、`ISqliteStoreFacet`
  （`Persistence` 内部，spec-001 已记为范围外）
- `Stored*` 一族是过去分词的存储行记录，不是「store」这个名词

原先的第三种读法 `OnlineStateStore` 已消失（→ `OnlineStateRegistry`）。**归类恰当**：它是
`Application` 内部 `internal sealed class`、无接口、只被 `ApplicationModule` 与三个门面
（`AccountDomainService` / `OnlineUserDomainService` / `ActiveCharacterDomainService`）触碰，
不跨层、不可能被命令处理器注入；`AGENTS.md:420` 明确后缀表的范围是 `Dfo.Server.Abstractions`，
所以一个 `Application` 内部运行时内存表叫 `*Registry` 与后缀表不冲突。

（散文里还留着一处把它叫 `store` 的话，见非阻断观察 O-3；C-4 的证据类是「名字含 `Store` 的
**类型**逐个列出」，故不计入本条判定。）

### C-5 行为逐值不变 —— 通过

用例数 2007，与 spec-001 后完全一致（逐程序集加总见开头）。

行为保持由**独立的机械核对**支撑：本评审对 `git diff -M -U0 70a3e54e -- Server/` 的每个文件，
把删除行套用重命名映射（含 `RegisterReplacing→Select`、`TryRemoveExact→ReleaseExact`、
局部变量 `directory→sessions`/`store→state`）后与新增行做多重集对消。55 个文件中 **34 个残差为空**，
其余 21 个的残差**全部落在四类之内，无第五类**：

1. `Current()` 的纯删除（13 个文件，0 新增行）
2. 拆壳（`ISelectCharacterLifecycleServices.cs`、`SelectCharacterLifecycleServices.cs`、
   `SelectCharacterPhaseCommands.cs`、`SelectCharacterLifecycleTests.cs`）
3. 类别措辞与 XML 文档搬移（`ISessionPresence.cs`、`SessionPresenceDomainService.cs`、
   `Support/UserDomainServices.cs`）
4. 判据反例替换（1 行）

无一处是期望值或断言的改写。逐点复核了最容易夹带行为的两处：

- `SelectedSessionDomainService.Select` / `ReleaseExact` 的方法体与旧
  `RegisterReplacing` / `TryRemoveExact` **逐行相同**（仅方法名变）
- `SelectCharacterLifecycleTests.cs:536,548` 把 `sessionEntry` 换成 `directory` 时，
  旧测试替身 `SessionEntry` 的三个成员本来就是直接转发同一个 `directory` 实例、不写 ledger，
  因此处理器拿到的是同一个对象、同一条路径，无断言变化

`ReleaseExact` 上那段解释「为什么是 Exact 而不是按账号」的 XML 文档从
`ISelectCharacterLifecycleServices.cs` 搬到 `ISessionPresence.cs:75-82`，八行逐字符相同
（残差比对中删除行与新增行完全对消）。

改动文件无 CRLF。

### C-6 仓库约定里关于这几个接缝类别的记载与代码一致 —— 通过（第一轮曾不通过，整改后复核关闭）

第一轮在此发现两条阻断，均在 `AGENTS.md`，整改后由本评审独立复扫关闭，详见「整改复核（第二轮）」。

被改动的记载逐条核对：

- `AGENTS.md:92-93` 现为「`Sessions/`（在场、选中会话、PvP 大厅、专家商店四个领域服务，
  各自把运行态留在私有容器里）」，与四个接口的名字、类别及各自的私有字段（见 C-1）一致，
  也不再与同文件后缀表的「`*Directory` 不得注入命令处理器」相冲突
- `AGENTS.md:151`、`:94` 的 `OnlineStateRegistry` 与代码一致
- `Docs/ProtocolSliceArchitectureNotes.md:162` 的 `ISelectedSessionDomainService` 与
  `SelectCharacterHandler.cs:58` 一致
- `Docs/领域结构重构设计.md:101` 的 `Select` 与 `ISessionPresence.cs:73` 一致
- `Docs/领域结构重构设计.md:252`（§12「第 0 步已完成（2026-08-18）」）与 `Docs/Refactor/` 下的
  存档保留旧符号 —— **这个取舍成立**：两处都是带日期或带来源的历史记载，
  `Docs/Refactor/SelectCharacterMigrationMatrix.md:148` 说的还是**旧系统**的
  `CharacterSessionLifecycleCoordinator` / `SessionDirectory`，本来就不该跟着改
- `ISessionPresence.cs:17,60,132` 三处 "the legacy directory / registry" 保留未改，
  指的是旧运行时，改了才是错

---

## Observable Requirements

| RQ | 判定 | 独立证据 |
|---|---|---|
| RQ-001 | 通过 | 见 C-1：四个接口逐成员核对，无容器操作、无整表列举；旧标识符全仓零命中 |
| RQ-002 | 通过 | 见 C-2：三个透传成员连同实现删除；两个处理器构造器直接注入 `ISelectedSessionDomainService`；无新壳。唯一保留的逐字转发 `ReplaceRuntimeExact` 早于 spec-001 就存在（`git show 9c0de6c6:…/SelectCharacterPhaseCommands.cs` 已有同名调用），不是为通过判据而设，且它指向 Spec 明列的 Non-Goal，见 O-1 |
| RQ-003 | 通过 | 见 C-3：判据 diff 只有一行反例；谓词与豁免清单未动；两次自建变异分别被两条不同用例抓住 |
| RQ-004 | 通过 | 见 C-4：类型名层面只剩「事务结算真源」一种读法，摆摊由 `Expert` 前缀分辨 |
| RQ-005 | 通过 | 见 C-5：2007/2/0；55 文件残差机械对消，无期望值或断言改写 |
| RQ-006 | 通过（整改后） | 见 C-6 与「整改复核」：两条与代码矛盾/破坏记载的缺陷已消除，`AGENTS.md` 四张表结构完整，其余记载逐条对上 |

## Acceptance Scenarios

- **AC-001** 通过 —— C-1 的逐成员表即是「逐个读名字与成员」的结果
- **AC-002** 通过 —— C-2 的成员对照与两个构造器
- **AC-003** 通过 —— C-3；判据跑绿，谓词/豁免清单 diff 为空，变异证明它仍会红
- **AC-004** 通过 —— C-4 的类型清单
- **AC-005** 通过 —— C-5；2007 不变，测试 diff 无期望值改写
- **AC-006** 通过（整改后）—— 逐条对照后，`AGENTS.md` 与两份 `Docs` 中已无与代码矛盾的陈述

## Non-Goals 核查（范围）—— 通过

- `ISelectedSessionRuntimeDirectory` 接口体（`ISessionPresence.cs:90-100`）与
  `SelectedSessionRuntimeDirectory.cs` 一字未动；它只是被引用进判据反例
- `IEntryInventoryLeaseRegistry` 与入场背包租约：`git diff` 中零改动
- 普通 socket 消息处理器的依赖面：`SelectCharacterHandler.cs:58` 等处只有类型名随重命名更新，
  参数个数与来源不变；判据覆盖面未扩张
- 可观察行为：见 C-5

---

## 整改复核（第二轮）

两条阻断均已修复，本评审独立复扫确认关闭。整改是**纯文档**的：
`git diff -M -U0 70a3e54e -- Server/` 的输出与第一轮保存的副本 **md5 逐字节相同**
（`f1ffe32ed3dec97dedcabf93c134c0c8`），故产品代码与测试零改动，第一轮的构建与
2007 / 2 / 0 判据结果继续成立，无需重跑。

`AGENTS.md` 的 `--numstat` 为 `6 4`，与三处改动逐条闭合：布局句 −3/+3、
`OnlineStateRegistry` −1/+1、判别式段落 +2/−0。**没有夹带第四处改动。**
文件行数 437 → 439，标题层级与基线逐行相同。

### B-1（`AGENTS.md:92` 把两个接缝叫回「注册表 / 目录」）—— 已关闭

现读作：

```
92:`OnlineUsers/`（在线聚合 + 执行门）、`Sessions/`（在场、选中会话、PvP 大厅、专家商店
93:四个领域服务，各自把运行态留在私有容器里）、`World/`（出生点策略、世界内容）、`Users/`
```

两个过时类别词消失，且新说法本身经核对属实——四个确为领域服务（C-1 的成员表），
四个的容器确为私有字段（`SelectedSessionDomainService.cs:10-11`、
`SessionPresenceDomainService.cs:14`、`ExpertStoreDomainService.cs:11`、
`PvpLobbyDomainService.cs:11-12`）。与 `AGENTS.md:404` 那条「`*Directory` 不得注入命令处理器」
的冲突随之消失。

### B-2（判别式段落截断后缀表）—— 已关闭

段落已整体移到表后（现 `AGENTS.md:412`），表格恢复连续：`AGENTS.md:398-410` 共 13 行不间断，
= 表头 + 分隔行 + **11 个类别行**，从 `*DomainService` 到 `*Module` 无空行、无插入。
`*DomainStore` 行（`:405`）重新落在表体内。段落文本 519 字节，内容未改。

### 按要求自行复扫全文，不止指出的那一处

同类错误（内容对、位置错 / 结构被破坏）我对三份被改文档做了机械复扫，未发现第二处：

- 对每一段连续的 `|` 行做「第二行是否为合法分隔行」的判定：
  `AGENTS.md` 四段（`26-37`、`122-125`、`248-255`、`398-410`）全部 **OK**；
  `Docs/领域结构重构设计.md` 七段（`11-16`、`25-31`、`47-55`、`116-120`、`138-142`、
  `229-234`、`269-277`）全部 **OK**；`Docs/ProtocolSliceArchitectureNotes.md` 无表格
- `AGENTS.md` 的围栏代码块计数为 6（3 个块，配对平衡）
- `AGENTS.md` 全部标题行与基线 `diff` 为空——没有段落被挪进错误的小节

## 非阻断观察

**O-1 `ISelectCharacterSessionEntryDomainService.ReplaceRuntimeExact` 仍是逐字转发。**
`SelectCharacterLifecycleServices.cs:174-175` 一行转发到 `runtimes.ReplaceExact`，被
`SynchronizeSelectedCharacterQuestsCommandHandler` 与 `ReviveSelectedCharacterPetCommandHandler`
调用（`SelectCharacterPhaseCommands.cs:257,285`）。形状上它与刚被拆掉的那层壳同类：
命令处理器隔着一层碰 `ISelectedSessionRuntimeDirectory`。
但它**早于 spec-001**（`git show 9c0de6c6:…` 已有同名调用与同名接口成员），不是为通过判据而设，
且它指向的容器是本 Spec 明列的 Non-Goal。记在这里，以免下一个成果拆运行态聚合时把它漏掉。

**O-2 判据反例锚在下一个成果打算拆掉的类型上。** `ISelectedSessionRuntimeDirectory` 正是
spec-003 的核心目标。它一改名或一拆解，`ATypeOutsideTheVocabularyIsRefused` 又要换反例。
本轮的处理无可指摘（当时没有更好的「货真价实的容器」可选），但下一轮应考虑让这条反例
指向一个不会被本系列改动的容器，或改为构造一个测试专用的反例类型。

**O-3 `ApplicationModule.cs:92` 仍把 `OnlineStateRegistry` 叫作 store。**

```
92:        // One store, three services. The store keeps the collections whose invariant spans them
93:        // behind a single lock; the services are the three responsibilities read off it.
94:        services.TryAddSingleton<OnlineStateRegistry>();
```

这正是 RQ-004 要消掉的第二种读法，就压在被改名的那行上面。类本身的文档
（`OnlineStateRegistry.cs:9-11`「one state, one lock」）已经是干净的，只有这条接线注释没跟上。
不计入 C-4 是因为该条的证据类明确限定为「名字含 `Store` 的**类型**」。

**O-4 两处代码注释仍把新领域服务称作 directory。**

- `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:84`
  —— `// After the directory entry is gone:`，指的是上一行的 `sessions.ReleaseExact(session)`
- `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs:182`
  —— `// exact directory removal in its finally block.`，指补偿处理器里的 `sessions.ReleaseExact`

执行者声明的措辞改动范围是「接口摘要与类注释」，这两处是方法体内注释，落在其自述范围之外，
故不计为食言；但既然本轮特意扫过这批词，一并收掉更干净。

**O-5 两处文档措辞尾巴。** `AGENTS.md:291`「用只读目录/在线查询检查 session 仍为 current」
与 `Docs/领域结构重构设计.md:103`「否则目录里会同时存在两个」，指的都是已改名的会话领域服务。
两句话陈述的事实仍然成立，只是类别词过时。

**O-6 保留 `FindByWireUserId` / `FindByCharacter` 两条断言的理由属实（已独立复核）。**
本评审 grep 了全仓这两个成员的每一处命中：除
`SelectedSessionDomainServiceTests.cs:46-47` 外，其余 16 处**全部**是测试替身里
`=> Session` / `=> identity` / `=> null` 形式的常量实现，对真实实现零覆盖。
另有四个测试文件构造真实 `SelectedSessionDomainService`
（`CarriedGoldCapTests.cs:154`、`SelectCharacterLifecycleTests.cs:520`、
`OnlineUserServiceTests.cs:385,412`、`SessionGenerationOwnershipTests.cs` 七处），
但它们断的是清理与世代语义，没有一条断言这两个查询的返回值。
所以「整条删除会让两个成员失去全部覆盖」的说法成立，只删 `Current()` 那一条断言是正确处置，
不是为凑用例数留测试。方法名从 `RecipientResolutionIsStableAndOnlyReturnsCurrentRegistrations`
改为 `WireIdAndCharacterLookupsEachFindTheirOwnSession` 也如实反映了它现在断的事。

**O-7 摆摊语义未被误伤（已复核）。** `IExpertStoreDomainService` 及其一族
（`ExpertStoreEntryState`、`ExpertStoreClosedMessage`、`StoreOwner`、`HasOpenStore`）
的 `Store` 一字未动；`ISessionPresence.cs:127-135` 里解释「为什么摆摊与在场分开」的注释
只把 "presence registry" 改成 "presence service"，"expert store" 与 "the legacy runtime keeps it
in its own registry" 都原样保留 —— 前者是语义、后者指旧系统，两处都改对了（即不改）。
