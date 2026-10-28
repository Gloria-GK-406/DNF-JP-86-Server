---
kind: rks-review
target: .rks-agent/tasks/dungeon-owner-routing/TaskContract.md
target_revision: 2
verdict: approved
reviewer: independent subagent (reviewing-work deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：dungeon-owner-routing（revision 1）

## 范围

- 审查对象：`TaskContract.md` revision 1（state: ready），代码基准 `7adec858`（main，当前分支 `task/dungeon-owner-routing-20261028` 与之相同）。
- 审查内容：契约作为非代码交付物的完整性、正确性、来源忠实度、可用性、范围与无依据断言；以及 modeling-task-contracts 的 ready 条件。本审查只针对契约，不评价后续 Spec/实现。
- 背景证据 `evidence/background-7adec858.md` 只当线索使用，关键结论已对照代码抽查。

## 依据

- `reviewing-work` SKILL 与 `references/deliverable.md`；`modeling-task-contracts` SKILL 中「Separate Outcomes from Mechanisms」「Create or Revise」第 5–7 步、「Readiness」。
- 来源：issue #203、#187、#188（均 OPEN，已用 `gh issue view` 读取正文）；契约 Authorization 与 Source Request 记录的用户原话（按契约记录采信）。
- 仓库规则：`AGENTS.md`、`Document/RefactorPreferences.md`、`Document/ProtocolCommands.md`、`Document/server-ci-release.md`、`Document/FeatureDevelopment.md`（确认本任务不属于新功能流程）。
- 代码：`Server/src/Dfo.Server.Protocol/Game/` 下相关处理器、`TowerProtocol.cs`、`IDungeonParticipationDomainService` 及其实现与注册。

## 关键证据（抽查结论）

以下路径省略前缀 `Server/src/Dfo.Server.Protocol/Game/`，另行注明的除外。

| 契约陈述 | 核对结果 | 证据 |
|---|---|---|
| 13 个命令、7 个处理器类 | 属实。GetItem、CharacterDeath、UseRevivalCoin、MonsterDeath、MapLoading（0x0025/0x002D/0x009F）、PartyTransition（0x0048/0x002A/0x000D/0x000E/0x0079）、DungeonSelection（0x000F/0x0084） | `GamePartyTransitionCommandHandler.cs:48-50`；`GameMapLoadingCommandHandler.cs:52-53`；`DungeonSelection/GameDungeonSelectionCommands.cs:63` |
| 陈旧判定 = `InboundOrdinal is > 0 && !SequenceIsFresh`，19 处调用 | 属实（19 处调用 + 1 处定义） | `TowerProtocol.cs:5`；`git grep TowerProtocol.IsStale` |
| 各分支先后顺序不一 | 属实。拾取 Boss“查后判” `GroundPickup/GameGetItemCommandHandler.cs:140-142`，幻影“先判后查” `:180-182`；EPLP Boss 先 Capture 再判 `GamePartyTransitionCommandHandler.Exit.cs:50-53`；0x000F Boss“查后判” `DungeonSelection/GameDungeonSelectionCommands.cs:128-131` | 同左 |
| 0x000F、0x0029、0x0027 物件破坏在普通副本和城镇照常处理陈旧帧 | 属实，但不完整（见 N1） | `DungeonSelection/DungeonSelectionCommandPhases.cs:170-177`；`CharacterDeath/GameUseRevivalCoinCommandHandler.cs:25-33`；`GameMonsterDeathCommandHandler.cs:104-139` |
| 只有 0x002A、组队变更、0x0010 在入口统一判陈旧 | 属实 | `GamePartyTransitionCommandHandler.cs:299`；`GamePartyTransitionCommandHandler.Stages.cs:759-760`；`DungeonEntry/SelectDungeonCommandPhases.cs:50` |
| 组队类按整队归属 | 属实。调用者、目标及所有在线队员逐个 `Find` | `GamePartyTransitionCommandHandler.Stages.cs:686-715` |
| 0x000F Boss 释放后同一命令继续普通选图 | 属实。`return !released.Changed`，释放成功返回 false 后落入普通选图 | `DungeonSelection/GameDungeonSelectionCommands.cs:135-137`；`DungeonSelection/DungeonSelectionCommandPhases.cs:174-177` |
| 占用登记给出 普通/幻影塔/Boss 塔 + RunId，三种副本入场登记，生产必然注册 | 属实。单例注册；三个领域服务以可选参数接收，入场 `TryClaim` | `Server/src/Dfo.Server.Abstractions/World/IDungeonParticipationDomainService.cs:4-14`；`Server/src/Dfo.Server.Application/ApplicationModule.cs:325`；`Application/World/DungeonRunDomainService.cs:127`、`BossTower/BossTowerDomainService.cs:114`、`IllusionTower/IllusionTowerDomainService.cs:116` |
| 没有架构或源码测试约束这类分发写法 | 属实。测试中 `TowerProtocol.IsStale` 只出现在复制写法的驱动里 | `Server/tests/Support/TowerAlgorithmDrivers.cs:100,151` |

背景报告的 §0、§1.1–§1.5、§2.1 抽查均与代码一致，没有发现事实错误。

## 发现

### 阻断项

**B1. Goal/SC-2 的“唯一通道”与 Constraints/SC-5 的 0x000F 交接相互矛盾**

- 证据：Goal（`TaskContract.md:31`）要求“最后只路由到一个通道……各通道只处理自己那种副本的逻辑”，SC-2（`:82`）要求“路由到唯一通道”，并用含反例的结构测试守护。Constraints（`:73`）和 SC-5（`:85`）又要求 0x000F 在 Boss 塔释放后，由同一命令继续执行普通（城镇）选图。现行代码确实是两个通道接力（`GameDungeonSelectionCommands.cs:137` → `DungeonSelectionCommandPhases.cs:174-177`）。
- 后果：两类条件无法同时按字面满足。实现者只能二选一：一是让 Boss 通道执行城镇选图逻辑，违反“通道只处理自己那种副本”；二是由分发处在 Boss 通道之后再进入第二个通道，违反“唯一通道”。SC-2 的结构测试要么拒绝这种交接，要么只能临时放宽。验收口径因此在 Spec 阶段就会分叉。
- 最小修改：在 Goal 或 Constraints 中按结果层面写明 0x000F 的处置。例如把它列为“唯一通道”的具名例外：Boss 通道只负责释放，释放成功后同一命令继续执行城镇选图步骤，不重新定位，也不再判陈旧。SC-2 也要相应写明，只允许这一个具名例外，其他交接一律拒绝。不需要在契约里规定具体的实现机制。

**B2. Scope Out 与 RI-6（#187 第 2 条）交叉，Root Intent Coverage 没有显式处置**

- 证据：#187 第 2 条“塔路由链以可选参数……散在 10 多个普通 handler”列出了 `DungeonEntry:448`。按 #187 的基准 `a88fceaf` 核对，该位置是 0x0010 `SelectDungeonHandler` 的可选塔参数构造器（`git show a88fceaf:Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs` 第 447-449 行）。契约 Scope Out（`:48`）排除了 0x0010，而 RI-6（`:63`）却把 #187 第 2 条整体标为 `in_scope`。当前 0x0010 仍在普通 handler 里按目标地下城依次分到 Boss / 幻影 / 普通（`DungeonEntry/SelectDungeonCommandPhases.cs:115-140`）。
- 后果：这违反了 readiness 中“显式处理每一个 Scope Out 交叉”的条件。RI-6 把只部分覆盖的来源意图标成完整 in_scope，SC-7 给 #187 的“本次覆盖条目”评论因此可能夸大覆盖范围。这与用户“别让 issue 和修正范围不一致”的要求（RI-5）直接相关。
- 最小修改：拆分 RI-6，或在其 Rationale 中给 0x0010 部分单独处置。可选两种：一是 `not_applicable`，附证据：已无可选参数，按目标而非当前归属分发，已在入口判陈旧；二是 `preserved_upstream` 到 #187。同时让 SC-7 的 #187 评论如实说明 0x0010 未改动及其原因。

### 非阻断项

- **N1. 行为变化清单不完整（Current Situation `:23`）。** RI-4 的“任何位置”授权覆盖了全部变化，但契约只写了“普通副本和城镇”，以下几种也会变化：
  - 0x0029 的陈旧帧在 Boss 塔里目前会回拒绝帧（`GameUseRevivalCoinCommandHandler.cs:28-31`）；
  - 0x000F 在幻影塔里照常处理陈旧帧，因为它没有幻影分支（`DungeonSelectionCommandPhases.cs:174-176`）；
  - 0x0027 物件破坏帧在幻影塔和城镇里回 `MonsterDeathNotification.Empty`（`GameMonsterDeathCommandHandler.cs:110-119`）；
  - 无会话时 0x0048、0x0084、0x000F 写 Warning，0x002D 写 Debug，仅日志不同。

  建议补全这份清单，供 SC-3 测试矩阵和 SC-7 的 #203 评论直接使用。
- **N2. 已知的第二个不一致窗口未列出（`:25`、Assumptions `:102`）。** 背景 §2.3 的 W2 指出：普通运行按当前队伍键查找（`Application/World/DungeonRunDomainService.cs:1406-1419` 一带），可能出现占用登记仍为 Ordinary、而 `runs.Find` 为 null 的情况；该状态能否跨命令残留未确认。按登记定位时，这类请求会进“普通”通道，现行代码则走城镇路径。Assumption 称“依据背景证据”只有三种例外，与背景证据不符。Constraints `:70` 的总原则虽能兜底，仍建议把 W1 的规则泛化为“登记与运行时成员不一致时，按现行运行时查询结果路由”，或把 W2 显式列入 SC-5。
- **N3. Scope In `:38` 的“以副本占用登记为依据”属于机制选择，契约没有注明依据。** 仓库中确有支持：`Document/LordTower-Design-20261019.md:60`（跨模式共用事务性参与占用）、#187 第 4 条（“是否在副本中”有多套 membership）、`Document/RefactorPreferences.md:10`。建议在 Rationale 中引用这些依据，或把数据源选择留给 Spec。依据已存在，因此不阻断。
- **N4. SC-6 与 Scope Out `:48` 的边界可以写得更清楚。** 0x009F（`TowerLoadingCommandPhases.cs:124`）和 0x0010（`SelectDungeonCommandPhases.cs:50`）同样调用 `TowerProtocol.IsStale`。按 SC-6，它们应改用中性定义，只是分发保持不变。建议在 Scope Out 中注明“仅分发不改”。另外，`Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:42` 用 `SequenceIsFresh` 判断重放，概念不同，建议注明不在 SC-6 范围内。
- **N5. 日志是否属于受保护行为未明说。** Constraints `:70` 列举了通道、协议帧、收件人和领域结果，背景 §6.2 也提出了这个问题。0x002D 的陈旧帧目前固定写带 Boss 标签的 Warning（`TowerLoadingCommandPhases.cs:72-76`）。建议明确写出日志文本和级别不属于受保护的可观察行为。
- **N6. RI-7 与 Scope Out `:51` 的“等”太含糊。** #187 第 3 条（MonsterDeath 硬编码清场）、第 4 条（多套 membership）、`*EntranceEventDomainService`、`Status == "active"` 都没有逐条列出。第 4 条会被本次的定位能力部分触及，建议逐条列出，保证 #187 评论准确。
- **N7. SC-1/SC-2 的证据形式写死为“源码结构测试”。** `RefactorPreferences.md:12,35` 同样认可类型、编译期或注册检查加反例。建议改写为“结构守护（类型、编译期或源码扫描）且含能被拒绝的反例”，不限定具体测试手法。
- **N8. RI-3 与 Scope Out `:49` 的交叉未在 RI 表中说明。** 用户的决定是基于“13 个文件、31 处”做出的（`:13`），其中约 13 处属于投影内部读取、入场精确性校验、计时器回城和 0x009F，契约把它们排除在外。Scope Out 给了理由，但 RI-3 没有交叉说明。建议在 RI-3 的 Rationale 中注明，并在 SC-7 的 #203 评论里写清“改动 13 个命令，以下非分发读取未改”。
- **N9. 合并后的 issue 处理未写明。** `server-ci-release.md:43-49` 要求合并后“更新关联 issue，只关闭已完成的范围”。建议在 Requires Confirmation 中写明合并后关闭 #203、#187 保持开放。
- **N10. 测试现状描述略有偏差（`:26`）。** 组队变更已有消息 handler 层的陈旧帧测试（`Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs:393,418`，使用 `RecordingCommandBus`），缺的是 CMD 层覆盖。“只覆盖”的说法不够准确。
- **N11. #187 评论的授权属于推断。** 用户原话是“在issue里说明修正的扩张范围”，没有点名 #187。结合 RI-6 来看合理，记录在此供控制方知悉。

## 其他 readiness 检查（通过项）

- 不存在 Open Questions 段，也没有 `unresolved` 行；所有处置都使用允许的取值。
- `preserved_upstream` 的 RI-7、RI-8 分别指向开放中的 #187、#188，是具体、独立且仍在办的 owner，没有借此推掉本契约自身的结果义务。
- Authorization 只有一行 task 授权，逐字记录了用户原话，范围覆盖实施、PR、issue 评论和 Codex 闭环。合并放在 Requires Confirmation 中，符合 `server-ci-release.md:55`：只有用户说“分支收尾”才授权合并。
- 版本号不改，符合 `server-ci-release.md:20-22`。表征测试先行、结构与行为变化分开验证，依据为 #203 的“③先钉现有行为”和 `RefactorPreferences.md:25-26`。
- 本任务是 bugfix 加重构，不适用新功能用户测试流程（`FeatureDevelopment.md:3-5`）。A21 实机确认写在 Assumptions 中，处理恰当。

## 结论

`changes_required`。契约对现状的事实陈述基本准确，背景证据经抽查可靠，授权和大部分 readiness 条件均满足。两处需要修改：B1 是 Goal/SC-2 与 0x000F 交接约束之间的内在矛盾；B2 是 Scope Out 与 #187 第 2 条的交叉没有在 Root Intent Coverage 中显式处置。两项都只需修改契约文字，不必回到意图建模。修订时 revision 递增到 2，再交独立复审。非阻断项建议一并处理，尤其是 N1、N2，它们直接影响 SC-3/SC-5 的测试矩阵和 SC-7 的 issue 评论是否准确。

## Revision 2 复审

### 范围与方法

- 对象：`TaskContract.md` revision 2（state: ready）。逐条核对 revision 1 的 B1、B2 与 N1–N11 是否已处理、修改有没有引入新问题；新增的事实陈述按代码与 issue 原文抽查。未改动的证据沿用上文。
- 结论：B1、B2 已解决，没有新的阻断项。

### 阻断项处理

| 项 | 结论 | 依据 |
|---|---|---|
| B1 | 已解决 | Goal 改为三步结构（`TaskContract.md:41-44`），并把 0x000F 写为唯一具名例外（`:46`）；SC-2 只允许这一个例外（`:109`）；Constraints 引用该例外（`:100`），SC-5 也覆盖它（`:112`）。例外的边界与代码一致：释放失败时停止，释放成功后继续选图（`DungeonSelection/GameDungeonSelectionCommands.cs:135-137`、`DungeonSelection/DungeonSelectionCommandPhases.cs:174-177`）。“唯一通道”与 0x000F 交接不再矛盾。 |
| B2 | 已解决 | 原 RI-6 拆成 RI-6（按当前位置分发，in_scope，`:89`）和 RI-7（0x0010 按目标分发的可选塔参数，preserved_upstream 到开放的 #187，`:90`）。Scope Out 的 #187 逐条列表包含 0x0010（`:76`），SC-7 要求 #187 评论说明覆盖与未覆盖部分（`:114`）。与 Scope Out 的交叉已在 Root Intent Coverage 中显式处置。 |

### 非阻断项处理

N1–N11 均已处理，位置分别是：
- N1：`:24-28`
- N2：`:33-35`、`:98`、`:129`
- N3：`:32`
- N4：`:54`、`:66`、`:113`
- N5：`:97`
- N6：`:68-76`
- N7：`:56`、`:108-109`
- N8：`:22`、`:86`
- N9：`:125`
- N10：`:36`
- N11：`:88`

revision 已递增为 2。没有 Open Questions，也没有 `unresolved`。`preserved_upstream` 行（RI-7、RI-8、RI-9）都指向开放中的 #187、#188。

### 剩余非阻断问题（不影响 ready，建议在 Spec 或 issue 评论中顺手修正）

- **R1. 无会话情形的描述不准确（`:28`）。** 契约写的是“无会话时 0x0048、0x0084 不判陈旧，其他命令判陈旧”。实际上，无会话时 0x002D（`OrdinaryLoadingCommandPhases.cs:101-108`）、0x000F（`GameDungeonSelectionCommands.cs:127-129` 返回 false 后由普通路径写 Warning）、0x0027（`GameMonsterDeathCommandHandler.cs:98-103`）、0x0028（`CharacterDeath/GameCharacterDeathCommandHandler.cs:73`）和 0x0029（`CharacterDeath/GameUseRevivalCoinCommandHandler.cs:26`）也都在判陈旧之前就返回了；真正在无会话时判陈旧的只有 0x002B、0x0025、0x002A 和组队变更。无会话时这些命令都不产生领域写入或回包，差别只在日志，而 Constraints 已把日志排除在保护范围外，所以不影响 SC-3/SC-4。但这段文字会进入 #203 评论，建议更正。
- **R2. “约 13 处非分发命中”的组成与 31 处命中不完全对应（`:22`）。** 31 处命中里被排除的实际是：投影内部读取 8 处、入场精确性校验 2 处、计时器回城 2 处，以及 0x009F 的 1 处（只换定义）。“会话生命周期清理”位于 Application 层，并不在这 31 处命中里。数量大致吻合，建议 #203 评论按实际组成列出。
- **R3. #187 逐条列表还漏了两个子项（`:68-76`）。** 一是活动开放判断使用 GM 展示字符串 `Status == "active"`（第 5 条），二是副本收件人的五种口径（第 7 条）。RI-8 的“其余条目”在语义上已涵盖，但 SC-7 要求 #187 评论说明未覆盖部分，建议补上。
- **R4. “位置”与“通道”不一一对应，建议在 Spec 中写明。** 现行代码中有几个命令在某些位置没有专属分支，例如 0x000F 和 0x0029 在幻影塔里走普通路径，0x0027 物件破坏帧在幻影塔里也走普通路径，在 Boss 塔里则被忽略。按 Constraints“每个命令由哪个通道处理保持现状”，这些位置应路由到现行承接它们的那个通道，这与 Goal 的“只路由到一个通道”并不冲突。建议 Spec 为每个命令给出“位置 → 通道”对照表，避免实现时误以为每个位置都必须有一个同名通道。

### Revision 2 结论

`approved`。B1、B2 已消除，revision 2 满足 modeling-task-contracts 的 readiness 条件。剩余 R1–R4 都是非阻断项。本结论只针对契约这一道审查关，不代表后续 Spec、实现或完成验证已经通过。
