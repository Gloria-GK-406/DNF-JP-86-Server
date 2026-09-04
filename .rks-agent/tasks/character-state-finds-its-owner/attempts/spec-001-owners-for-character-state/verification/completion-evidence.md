---
kind: rks-verification
decision: pass
---

# Completion Evidence: 角色状态各自找到主人（spec-001）

## 被评估的确切主张

> 「spec-001『角色状态各自找到主人』已完成，其契约的成功条件 C-1..C-11 全部满足。」

独立验证者，全新上下文，未参与实现，未参与九轮 Task 快审，未参与完整变更评审。
下面每一条的证据都是**本轮自己跑的命令或自己读的当前产物**。九轮快审的 `passed`
与 `reviews/change-review.md` 的 `approved` 只作为存在性事实记录，其结论**未被采信**。

验证对象：工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/character-state-finds-its-owner`，
分支 `character-state-finds-its-owner`，基线 `427331bc`，未提交（已跟踪改动 35 个文件 + 18 个未跟踪新代码文件）。

## 判据结果（自跑）

| 命令 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | **0 警告 / 0 错误**，退出码 0 |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | **2069 通过 / 2 跳过 / 0 失败**，退出码 0 |
| `dotnet test … --filter "FullyQualifiedName~CommandHandlerArchitectureTests"` | **12 通过 / 0 失败**，退出码 0 |

全量分项：Protocol 510、Networking 52、Pvf 60、Persistence 256、Host 6、
Infrastructure 223(+2 skip)、Application 962。合计 2069/2/0，与预期数字**逐项吻合**
（基线 427331bc 为 2024/2/0，净增 45 条）。

那 2 个 skip 是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
——档案已加载的标志，与契约 Assumptions 一致。

**本轮未遇到既知偶发**：没有出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
也没有出现 `Dfo.Server.Networking.Tests` 的未记名并行失败。全量一次跑绿，无需复跑。

一处**我自己的命令造成的**噪声，如实记录：C-7 的变异 A 我误未带 `PVF_ARCHIVE_PATH`
单跑 Protocol 项目，`ItemPathLiveTests.AnItemDraggedOverASocketLandsInTheDatabaseAndIsAcknowledgedByteForByte`
随之失败；带上环境变量重跑即通过（510/0）。是环境变量缺失，不是产品缺陷，也不是偶发。

## 成功条件到证据的逐条映射

| ID | 自跑的命令 / 自己的观察 | 结果 |
|---|---|---|
| **C-1** | 读 `ICharacter.cs`：新增 `ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken)`；读 `PlayingCharacter.cs`：实现体每次调 `characters.FindIdentityAsync(...)`，**无任何字段缓存**，构造参数中 `ISelectedSessionDomainService`/`ISelectedSessionRuntimeDirectory` 已移除，私有 `State()` 已删除。`grep -rn "\.Identity\b" Server/src` 枚举全部身份读取点：`SelectCharacterPreparationService`(16 处)、`SelectCharacterEntryInventoryDomainService:92`、`SelectedSessionRuntimeDirectory:16`、`SelectCharacterPhaseCommands`(2)、`SelectedCharacterStateMessageFactory`(5)、`StateSynchronizationPayloadCodec.Entry/.Inventory`、`SupplementalSynchronizationCodec` ——**逐个核对后全部落在入场序列/入场投影编码之内**；唯一入场之后的读点 `PlayingCharacter.CanUseAsync` 已改经实例（`identity.Job`）。判据 `CharacterIdentityTests.An_identity_the_store_moved_is_what_the_next_question_gets` 钉住「现读」，`The_job_a_use_is_judged_against_is_the_one_the_row_holds_now` 钉住判定点 | **满足** |
| **C-2** | 读 `ICharacterPositionDomainService`：`Place` / `Find` / `CrossInto` / `StepTo` / `ForgetExact`。读 `ICharacter.cs` 全部成员（`IdentityAsync`/`ProgressAsync`/`GoldAsync`/`AddGoldAsync`/`TrySpendGoldAsync`/`RuleForAsync`/`CooldownKeyFor`/`Judge`/`CanUseAsync`/`Id`）——**无任何回答坐标的成员**。`grep -rn "\.Position" Server/src/Dfo.Server.Protocol` = **0 处**。6 处读取点去向：`BackToVillageHandler`→`positions.Find`、`SetUserAreaHandler` 拒绝分支与日志(4 处)→`standing`、`TownReturnProjection`→改为**入参**、清理路径→`positions.Find`。2 处整体写回去向：`SetUserAreaHandler` 的 `ReplaceExact(… Position = moved)` → `positions.CrossInto`；`SetUserPositionHandler` 的同形写回 → `positions.StepTo`。生产注入点 6 个，与 AGENTS.md 所载行号**逐一吻合** | **满足** |
| **C-3** | 读 `ICharacterSettingsDomainService`：`ValueTask<CharacterSettingsEntryState?> FindAsync(CharacterId, CancellationToken)`；读实现 `CharacterSettingsDomainService`：从 `ICharacterSettingsTable` + `ICharacterHotkeyTable` 两张表现读，**不跨调用留存**。设置的 2 处原读取点仍在入场装配器内（入场序列的正当用法，Spec 明写不动它）。判据 `CharacterSettingsTests` 4 条对真实行校验 | **满足** |
| **C-4** | `IDarkKnightComboDomainStore : IDomainStore<IDarkKnightComboDomainStoreSession>`；`IDarkKnightComboBook`：`PagesAsync`/`FindPageAsync`/`SetPageAsync`/`ClearPageAsync`，经 `ICharacterDarkKnightComboDomainService.BookOf(accountId, characterId)` 交出。`grep -rn "DarkKnightCombo\b" Server/src Server/tests \| wc -l` = **0**（残留字段标识符全仓计数为 0）；`DarkKnightComboState` 记录已从 `CharacterModels.cs` 删除，余下 4 处命中全是**同名前缀的另一个协议消息** `DarkKnightComboStateMessage`。`grep "CREATE TABLE" SqliteDarkKnightComboDomainStore.cs` 无命中——建在既有行网关 `ICharacterComboPageTable` 之上，未新建表。判据 `DarkKnightComboBookTests` 15 条 | **满足** |
| **C-5** | 读 `DungeonEntrySlice.cs` diff：`var quests = result.CharacterEntry.Quests;` 已替换为 `questLogs.LogOf(session.AccountId, session.CharacterId)` 后 `CarriedAsync` / `ClearedAsync`。`ICharacterQuestLogDomainService` **不在**本轮 18 个未跟踪新文件之列（既有通路）。本成果新增的任务相关类型清单：**空**。判据 `SelectDungeonHandlerTests` 中 `Entry_asks_the_quest_log_which_quests_are_carried` / `..._cleared` / `An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy` | **满足** |
| **C-6** | 读 `ICharacterFatigueDomainService`：`BalanceOfAsync` / `SpendAsync` / `RefundAsync`，值类型 `CharacterFatigueBalance(Total, Used)` 带 `Remaining`。读 `UnaccountedCharacterFatigueDomainService`：`SpendAsync` 与 `RefundAsync` 均 `=> ValueTask.CompletedTask`，`BalanceOfAsync` 恒返 `(188, 0)`——**「不记账」从实现体一眼可读，无需从行为反推**，且类名与 `<remarks>` 都写明。进本处 `SelectDungeonHandler` 在 `entered` 成功之后调 `fatigue.SpendAsync(session.CharacterId, FatigueAnEntryCosts /* =1 */, ct)`。`ApplicationModule` 已注册该唯一实现 | **满足** |
| **C-7** | **我自己做的变异验证**（下节详列）：变异 A（把 `FatigueAnEntryCosts` 由 1 改为 2）→ `SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam` **FAIL**（`Assert.Equal() Failure: Values differ`，`SelectDungeonHandlerTests.cs:142`）。变异 B（把进本处的 `SpendAsync` 换成 `RefundAsync`，即消耗报告消失）→ 同一条用例连同另 4 条 **FAIL**（5 失败 / 505 通过）。**还原后 510 通过 / 0 失败**，文件 SHA-256 与变异前完全一致 | **满足** |
| **C-8** | 全量 2069/2/0 通过（上表）。`git diff -- Server/tests` 逐条审：`git diff \| grep "^-" \| grep -i assert` 仅命中 **3 行文档注释**，**零条 `Assert.` 语句被删除**；`grep "^-.*\[Fact\|Theory\]"` **零命中**（无用例被删）。改动全部可归因：①构造签名更新（`PlayingCharacter` 去掉两个会话参数、`SelectedSessionCleanupService` 增加 `ICharacterPositionDomainService`）；②`ActiveCharacterState` 退役 `DarkKnightCombo` 后删掉的 `null` 实参（`CharacterEntryInventoryTests`、`SelectCharacterPayloadCodecTests`、`ItemUseFixtures`）；③随之失效的 `FixedRuntimeSessions` 夹具与 `SessionLevel` 陷阱常量删除（会话已不再注入角色实例，该陷阱结构上不可达）。**无一处期望值被改写**。协议编解码器文件（`StateSynchronizationPayloadCodec*`、`StateSynchronizationMessages.cs`）**未出现在改动清单中**，帧与字节布局未动 | **满足** |
| **C-9** | 自跑 `--filter FullyQualifiedName~CommandHandlerArchitectureTests` = **12 通过 / 0 失败**。`git diff --stat -- Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` **输出为空**——判定谓词 `IsPermitted` / `IsDomainShape` 与具名豁免字典 `Exempt` 所在的唯一文件**未被改动**，因此既未放宽也未扩充。本轮三个新注入的服务是靠满足既有领域形状谓词通过的，不是靠加豁免 | **满足** |
| **C-10** | `grep -rhoE "\b[A-Za-z]*(Domain)?Store\b" Abstractions Application \| sort -u` → 领域 store 类型为 `IDomainStore`、`ICharacterDomainStore`、`IItemDomainStore`、`IQuestDomainStore`、`IDarkKnightComboDomainStore`。**本成果新增的唯一 store 类型** `IDarkKnightComboDomainStore` 直接派生自 `IDomainStore<IDarkKnightComboDomainStoreSession>`，其 session 是 `IDomainStoreSession<OwnedCharacterId, byte, DarkKnightComboEntryState>` 的具名实例——与既有三个同一种读法：事务结算的真源。判据 `DarkKnightComboBookTests.TheComboStoreJoinsTheParticipantTheOtherStoresAlreadyJoined` 与 `OneDatabaseOneParticipantTests` 钉住「一个数据库一个参与者、四个面」 | **满足** |
| **C-11** | 读 `git diff AGENTS.md`（+54/−0 与两处订正段）：新增归属表（身份/坐标/任务/设置/连招/疲劳各自的主人与今天的消费方）。**记载与代码逐条核对**：①「`SelectCharacterResult` 九个成员一个没少」——读 `SelectCharacterModels.cs`，确为 9 个成员，且 `git status` 显示该文件**未被改动**；②「坐标 6 个生产注入点」+ 所引全部行号（`TownProjectionSlice.cs:216/:335`、`DungeonSelectionSlice.cs:173`、`LeaveDungeonSlice.cs:91`、`SelectCharacterLifecycleServices.cs:33/:132`）——grep 结果**行号逐一吻合**；③「`ReplaceExact` 6 个生产调用点」——grep 计数 = **6**；④「Protocol 下 `.Position` 成员访问 0 处」——grep 计数 = **0**；⑤「48 个 `ClientMessageHandler<>` 子类，其中 19 个类名在 `Server/tests` 全域零出现」——自跑枚举 = **48 / 19**，精确吻合。**并且如实写下了两件事**：「会话运行态聚合仍然存在，而且几乎原样存在」「拆整个聚合不在这一轮里」；「疲劳有接缝，但接缝不记账」「报的那个 `1` 是占位值，且与档案已知冲突」。`Docs/DungeonEntryNotes.md` 第八节同步记下接缝、总量出处与该冲突 | **满足** |

## C-7 变异验证记录（本验证者自跑）

被变异的位置：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs`。
变异前后均以 SHA-256 固定：`50e3a5bc4fc4775fa72796fd08439745e8f35858d33b6696c29f152215399b7d`。

| # | 变异 | 失败的判据 | 结果 |
|---|---|---|---|
| A | `public const ushort FatigueAnEntryCosts = 1;` → `= 2`（**报错数**） | `Dfo.Server.Protocol.Tests.Game.DungeonEntry.SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam`，`Assert.Equal() Failure: Values differ`，`SelectDungeonHandlerTests.cs:142` | 红 |
| B | `.SpendAsync(session.CharacterId, FatigueAnEntryCosts, ct)` → `.RefundAsync(…)`（**消耗报告被移除**） | 同上一条，另加 `Entry_asks_the_quest_log_which_quests_are_carried` / `..._cleared` / `The_answer_to_an_entry_is_what_it_was` / `An_empty_log_enters_with_no_quests_rather_than_the_sessions_copy`；合计 5 失败 / 505 通过 | 红 |
| — | 还原 | `Dfo.Server.Protocol.Tests` **510 通过 / 0 失败 / 0 跳过** | 绿 |

变异体经原文件副本按字节还原，还原后 SHA-256 与变异前一致；工作区未留下任何残余。

## 契约 Scope Out 的专项核实

**「会话运行态聚合与 `ISelectedSessionRuntimeDirectory` 本轮结束时仍然存在」，记载没有把本轮写成「拆掉了大聚合」。**

- 代码事实：`SelectCharacterResult` 仍是 9 成员 record，其定义文件 `SelectCharacterModels.cs`
  **完全未被改动**；`ISelectedSessionRuntimeDirectory` 的成员（`Publish`/`ReplaceExact`/`GetRequired`/`TryGet`/`RemoveExact`）
  在 `ISessionPresence.cs` 的 diff 中**一个未动**（该文件改动仅是邻居接口
  `ISelectedSessionPositionPersistence` 的文档注释）。`ReplaceExact` 仍有 6 个生产调用点。
- 记载事实：`AGENTS.md` 明写「**会话运行态聚合仍然存在，而且几乎原样存在**」「这一轮动的主要是
  **谁来回答**，不是**快照里有什么**」「**拆整个聚合不在这一轮里**」；快照只少了
  `ActiveCharacterState.DarkKnightCombo` 一个字段，且写明了删它的理由（此前每个构造点都写 `null`、
  全仓零读点）。`spec.md` 的 Non-Goals 同样明写「**会话运行态聚合本身不退役**」。
- **未发现任何产物把本轮写成「拆掉了大聚合」。** 相反，`AGENTS.md` 还主动记下了一个**更弱**的事实：
  `ActiveCharacterState.Position` 已成死字段（有人写、无人读、自入场起永久陈旧），并明确警告
  「在那之前，任何从它取坐标的新代码都是错的」。这条记载我逐项核过：Protocol 下 `.Position` 访问 0 处，
  6 处 `ReplaceExact` 无一在交回前动过 `Character.Position`——属实。

## `spec.md` 末尾订正的核实

订正声称：**`CharacterEntry.Quests` 自入场起就是陈的**，此前 Spec 正文那句「任务状态……被各自改变它们的路径写回，不会变陈」是错的。

**订正本身属实**，三条支撑逐条核过：

1. `QuestOfferRefreshDomainService`（实际路径 `Server/src/Dfo.Server.Application/Quests/`，
   非 Spec 所写的 `World/`——仅路径笔误，不影响结论）对 `CharacterEntry` 的引用计数
   `grep -c` = **0**。
2. `AcceptQuestHandler` 中 `CharacterEntry` 命中数 = **0**，根本不碰。
3. `FinishQuestHandler` 确实改 `CharacterEntry`，但 `:122-126` 改的是
   `Public.Extras.CharacterExperience`，**不是 `Quests`**。

我另外查到一处 Spec 未提及的 `CharacterEntry.Quests` 写入点：
`SelectCharacterPostEntryServices.cs:57-63`（`SelectCharacterQuestEntryService.SynchronizeAsync`）。
读其上下文后确认它是**入场序列内**的寻物任务触发值同步，发生在 publish 之前，
**不构成入场之后的刷新**——因此不推翻订正的结论「自入场起就是陈的」。

**订正没有改变 C-1..C-11 中任何一条的达成情况**：

- C-5 只要求「那一处改问已有的任务簿实例」且不新建任务类型，两者仍然成立且已达成。
- C-8（逐值不变）是唯一可能被波及的一条：读点由陈快照改为现读日志，若下游消费这些 facts，
  入场后接/交任务就会改变迷宫选择。我核实了下游——`DungeonLayoutRuleService.SelectMaze`
  对 `facts` **只做 `ArgumentNullException.ThrowIfNull`，全程不读**，其自身 `<remarks>`
  写明「the facts are deliberately unused」。故今天**确实逐值不变**，C-8 不受影响；
  订正对这一点的说明与代码一致。
- 订正把这件事的性质从「搬读点」改述为「实质修掉一个潜在缺陷」，属于评价而非成功条件，
  不影响任何一条的判定。

原文保留、订正另起一节的做法，本身与 `AGENTS.md` 中「注释/记载不得比代码强」的治理方向一致。

## 工作区最终状态

- `git log -1` = **`427331bc Record where the three batches left the root map`**（未提交、未建检查点）
- `git stash list` = **空**
- 已跟踪改动 35 个文件，与完整变更评审所载的 35 个一致；未跟踪新代码文件 18 个
- 变异验证所动的唯一文件已按字节还原（SHA-256 校验一致）
- 本验证者**未改动**任何生产代码、测试、`sdd-execution.yaml`、Task 文件或评审产物；
  仅新增本文件 `verification/completion-evidence.md`
