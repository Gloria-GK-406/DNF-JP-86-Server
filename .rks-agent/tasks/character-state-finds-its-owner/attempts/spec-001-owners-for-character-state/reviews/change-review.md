---
kind: rks-review
verdict: approved
---

# Change Review: 角色状态各自找到主人（spec-001 完整变更）

独立评审，全新上下文，未参与实现。评审对象是 `character-state-finds-its-owner` 分支相对基线
`427331bc` 的**完整未提交变更**：已跟踪 35 个文件（+776/−268）与 18 个未跟踪新代码文件。
`.rks-agent/` 下的内容按流程产物处理，不计入被审代码。

九个 Task 各自的快审记录只作为支撑证据读取，未采信其结论；下面每一条都对着代码与自跑判据独立核过。

## 判据（本评审自行执行）

| 命令 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，**0 警告 0 错误** |
| `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | **2069 通过 / 2 跳过 / 0 失败**（连跑两次，两次均如此，未见既知偶发） |
| `dotnet test … --filter "FullyQualifiedName~CommandHandlerArchitectureTests"` | 12 通过 / 0 失败 |

分项：Protocol 510、Application 962、Persistence 256、Infrastructure 223+2skip、Pvf 60、
Networking 52、Host 6 = 2069 通过 + 2 跳过。2 个 skip 是档案已加载的标志，与基线一致。

净增判据数自行核对：新增测试文件 44 个 `[Fact]`（4+8+4+15+1+8+2+2），已跟踪测试文件新增
`[Fact]` 1 个，合计 **+45**，与 2024 → 2069 精确吻合。

工作区状态：`git log -1` = `427331bc`，`git stash list` 为空，未建检查点，未提交。

## 一、成功条件 C-1..C-11 逐条核验

**C-1 身份 — 达成。** `ICharacter.IdentityAsync`（`Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs:59`）
由 `PlayingCharacter.IdentityAsync`（`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:46-55`）
实现，每次调用走 `characters.FindIdentityAsync`，无任何字段级缓存。
读取点全仓枚举：`Server/src` 里 `.Identity.` 的成员访问全部落在入场序列内——
`SelectCharacterPreparationService.cs`（入场装配）、`SelectedCharacterStateMessageFactory.cs`
（入场消息工厂，即契约点名的那 5 处）、`StateSynchronizationPayloadCodec.Entry.cs` /
`SupplementalSynchronizationCodec.cs`（入场编解码器）、以及
`SelectedSessionRuntimeDirectory.cs:16` 与 `SelectCharacterEntryInventoryDomainService.cs:92`
两处发布期一致性守卫。`SelectedCharacterStateMessageFactory` 的唯一调用方是
`SelectCharacterHandler`，确认没有入场之后的再投影通路。唯一的入场后违规者
`PlayingCharacter.State()` 已退役，随之 `ISelectedSessionDomainService` 与
`ISelectedSessionRuntimeDirectory` 两个主构造器参数被一并摘掉（`PlayingCharacter.cs:23-30` 前后）。
**AC-002「没有第三种」成立。**

契约 Assumptions 要求 Spec 明写身份归位走多远。Spec 的 *External Behavior and Data Rules*
明确选了「只在实例上暴露成员、不动入场编解码器」并给了理由（同步纯函数变世界查询会更差），
实现与之一致，未默默选一种。

**C-2 坐标 — 达成。** `ICharacterPositionDomainService`
（`Server/src/Dfo.Server.Abstractions/Characters/ICharacterPositionDomainService.cs`）持有活坐标，
实现在 `Server/src/Dfo.Server.Application/Characters/CharacterPositionDomainService.cs`。
`ICharacter` 成员清单（`ICharacter.cs`：`Id`/`CanUseAsync`/`IdentityAsync`/`ProgressAsync`/
`RemainingSkillPointsAsync`/`GoldAsync`/`AddGoldAsync`/`TrySpendGoldAsync`）**没有回答坐标的成员**。
8 处去向逐个核过：`TownProjectionSlice.cs`（`SetUserAreaHandler` 读+`CrossInto`、
`SetUserPositionHandler` `StepTo`）、`DungeonSelectionSlice.cs:173`（`BackToVillageHandler`）、
`LeaveDungeonSlice.cs:91`（`EplpCommandHandler`）、
`SelectCharacterLifecycleServices.cs:33/132`（清理与发布）。
两处整体写回（`runtimeSessions.ReplaceExact(… Position = moved …)`）已改为向服务报告移动。
全仓 `.Character.Position` 生产读取点收敛到 **1 处**：
`SelectCharacterLifecycleServices.cs:188` 的入场移交；`Dfo.Server.Protocol` 下 `.Position`
成员访问 **0 处**。**RQ-004 成立。**

**C-3 设置 — 达成。** `ICharacterSettingsDomainService` +
`Server/src/Dfo.Server.Application/Characters/CharacterSettingsDomainService.cs`，
从 `ICharacterSettingsTable` 与 `ICharacterHotkeyTable` 两表现读，不缓存。
校验条件与入场装配逐字一致——`CharacterSettingsDomainService.cs:45-49` 对
`CharacterEntryAssembly.cs:177-181`，含 `> 256` 上限与相同异常文本。
`CharacterSettingsTests` 对着真 `SqliteDatabase` 校验四种情形，含
「无 settings 行 → null」与「有行无 hotkey → 空列表」的区分。**AC-005 成立。**

**C-4 连招 — 达成。** `IDarkKnightComboDomainStore` /
`IDarkKnightComboDomainStoreSession` /（实例）`IDarkKnightComboBook` +
`ICharacterDarkKnightComboDomainService`，实现在
`Server/src/Dfo.Server.Persistence/Characters/SqliteDarkKnightComboDomainStore.cs` 与
`Server/src/Dfo.Server.Application/Characters/DarkKnightComboBook.cs`。
建在既有行网关 `ICharacterComboPageTable` 之上，不新建表（schema 未动）。
页号上界 `HighestPage = 1` 与入场装配的 `page.PageIndex is < 0 or > 1`
（`CharacterEntryAssembly.cs:232`）精确对齐。
残留字段核对：全仓 `DarkKnightComboState` 记录与 `ActiveCharacterState.DarkKnightCombo` 字段
**计数 0**；剩下的 `DarkKnightComboStateMessage` 是无关的协议消息类型。**AC-007 成立。**

**C-5 任务读点 — 达成。** `DungeonEntrySlice.cs:477-483` 改为
`questLogs.LogOf(…).CarriedAsync/ClearedAsync`，不再读 `result.CharacterEntry.Quests`。
本成果新增的任务相关类型清单为 **空**（18 个新文件里无任务类型，diff 中亦无）。
取不到时的语义保持：日志对无行角色答空列表而非 null，原路径亦然。**AC-008 成立。**

**C-6 疲劳接缝 — 达成。** `ICharacterFatigueDomainService` 回答总量/已用量/剩余量
（`Remaining` 有下溢保护，`ICharacterFatigueDomainService.cs:29-30`），接受 `SpendAsync` /
`RefundAsync` 两向报告；实现 `UnaccountedCharacterFatigueDomainService` **类型名与文档直说不记账**，
两个动词均 `return ValueTask.CompletedTask`，`BalanceOfAsync` 恒答 `(188, 0)`。
进本处 `DungeonEntrySlice.cs:509-511` 真实调用，且**位置在 `visits.EnterAsync` 之后**——
被拒的选择不报告。不建 store 不建表，符合 Scope Out。**AC-009/AC-010 成立。**

**C-7 变异验证 — 本评审独立复现，达成。** 三次变异，每次都还原：

| 变异 | 结果 |
|---|---|
| 删除 `SpendAsync` 调用 | 构建失败：`DungeonEntrySlice.cs(426,36): error CS9113: 参数"fatigue"未读`。仓库把未读主构造器参数当错误，调用点无法被静默删掉 |
| `FatigueAnEntryCosts` 由 1 改 2 | `SelectDungeonHandlerTests.Entry_reports_what_it_costs_to_the_fatigue_seam` 失败（`Assert.Equal() Failure: Values differ`），510 中 1 红 |
| 把调用移到 `visits.EnterAsync` 之前 | `SelectDungeonHandlerTests.A_refused_selection_reports_nothing_to_the_fatigue_seam` 失败（`Assert.Empty() Failure: Collection was not empty`），510 中 1 红 |
| 还原 | `git diff --shortstat` 回到 35 files / +776 / −268；Protocol.Tests 510 全绿；全量复跑 2069/2/0 |

判据钉的数是**写死的 1** 而非引用被测常量，因此不是空转判据。**AC-011 成立。**

**C-8 逐值不变 — 达成，且「既有断言一字未改」经机检确认。**
对 `Server/tests` 全量 diff 取所有 `-` 行做断言匹配，命中的三行**全部是注释文字**
（`…is asserted.` / `…the case asserts it.` / `…every assertion about where…`），
**没有任何一行被删除或修改的断言语句**。已跟踪测试文件中新增 `[Fact]` 仅 1 条，
删除的 `public void Publish/ReplaceExact` 来自退役的 `FixedRuntimeSessions` 替身，不是用例。
两处夹具值改动经逐条归因，**均为收紧而非迁就**：
- `CharacterItemUseTests.cs:301` 未发布用例的行由 `CharacterRow.None` 改为 `At(level: 50)`
  ——原来行也缺，判据对着两个原因都能过；现在只剩在场记录这一个原因。
- `LeaveDungeonTests.cs:289` 把 `Entry()` 快照里的坐标故意改成 `(9,8,77,66,4)`，
  真值移到 `Standing` 由坐标服务替身提供——读错来源现在必红。断言仍用原常量，未改。

行为等价性另做了三处独立核查（这是「逐值不变」真正的风险面）：
1. `PlayingCharacter.CanUseAsync` 的「有没有在玩」判据由「会话+运行态」换成
   `presence.Find(characterId)`。全仓 `presence.Admit` 只有
   `SelectCharacterLifecycleServices.cs:190`（`Publish` 内，与 `runtimes.Publish` 之间无 await），
   `presence.WithdrawExact` 只有同文件 `:86`（与 `runtimes.RemoveExact` 同一次同步运行）。
   两者**充要等价**，拒绝集合不变。
2. 职业由会话快照改读 `characters` 行。今日无任何写职业的通路，两者同源同值；
   新增用例 `A_character_marked_deleted_under_the_player_is_still_judged` 对着真库钉住
   `FindIdentityAsync` **不过滤 `delete_flag`**（与 `FindProgressAsync` 一致，SQL 已核对），
   堵住了「加一个软删过滤就凭空多出一种拒绝」这条回归。
3. 任务来源改动**在线上可观察结果为零**：`DungeonEntryFacts` 只流向
   `DungeonLayoutRuleService.ResolveAsync`，而 `SelectMaze`
   （`DungeonLayoutRuleService.cs:73-76`）**刻意不读 facts**，仅用 seed。
   坐标方面，`BackToVillage`/`Eplp` 原先读的 `result.Character.Position` 在改动前是被
   移动处理器 `ReplaceExact` 维护着的活值，现在读服务持有的活值，同值。

**C-9 注入面判据 — 达成。** `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`
**在 diff 中零改动**（不在 35 个已改文件之列）。判定谓词 `IsDomainShape`
（同文件 `:139-143`：接口 + 属于 Abstractions 程序集 + 名字以 `DomainService`/`DomainStore` 结尾）
与具名豁免清单（`:33-38`，仍**只有 `TimeProvider` 一条**）均未放宽或扩充。12/12 通过。
新增的四个 `…DomainService` 与一个 `…DomainStore` 都在 Abstractions 且符合后缀规则，
是靠合规通过而非靠放宽。

**C-10 `store` 一词 — 达成。** Abstractions 中含 `Store` 的类型：
`ICharacterDomainStore(Session)`、`IItemDomainStore(Session)`、`IQuestDomainStore(Session)`、
新增 `IDarkKnightComboDomainStore(Session)`、以及词汇本体 `IDomainStore`/`IDomainStoreSession`
——全部是事务结算的真源。实例叫 `IDarkKnightComboBook`，**没有叫 store**。
`IExpertStoreDomainService` 是游戏里的「摆摊」，是既有类型，本轮未新增同类歧义。
`AGENTS.md` 相应把 session 数从三个改记为四个，说法与代码一致。

**C-11 仓库记载 — 达成。** `AGENTS.md` 新增的归属表、
「会话运行态聚合仍然存在」段、「坐标已成死字段」段、「疲劳有接缝但不记账」段、
「设置与连招刻意无消费方」段，以及 `Docs/DungeonEntryNotes.md` 第八节的重写，
逐条抽查行号锚点**全部命中**：`TownProjectionSlice.cs:216/:335`、`DungeonSelectionSlice.cs:173`、
`LeaveDungeonSlice.cs:91`、`SelectCharacterLifecycleServices.cs:33/:132/:188/:195`、
`DungeonEntrySlice.cs:477-483/:509-511`、`CharacterRosterService.cs:147`、
`SelectCharacterPreparationService.cs:54/:149`、`PvfDungeonDefinitionSourceTests.cs:32`。
数量型断言亦复核：`SelectCharacterResult` 仍是 **9 个成员**；
`ClientMessageHandler<>` 子类 **48 个**；`ReplaceExact` 生产调用点 **6 处**；
`Dfo.Server.Protocol` 下 `.Position` 成员访问 **0 处**；
`SelectDungeonRefusal.InsufficientFatigue` 生产引用 **0 处**；
`ConsumesFatigue` 全仓唯一读者确为 `PvfDungeonDefinitionSourceTests.cs:32`。
**记载没有一句比代码强**，明确写了聚合仍在、疲劳未记账、两样归位无消费方、
以及占位值 `1` 与 `[no fatigue]` 的已知冲突。

## 二、缺失与多余行为

**没有缺失。** Scope In 的七项全部落地，无「只做了一半」或「做成了另一件事」的项。

**没有多余。** 逐个文件核过是否夹带：
- `CharacterEntryTables.cs` 的 `NameOf(reader)` → `NameOf(reader, ordinal)` 是新读取所必需
  （新语句里 name 不在第 2 列），不是顺手重构。
- `IDungeonVisitDomainService.cs`、`ISessionPresence.cs`、`TownProjectionSlice.cs` 的文档改动
  都是被 T-002/T-003 的事实变化逼出来的，未改任何契约。
- `OneDatabaseOneParticipantTests.cs` 只改注释（三个 store 改记为四个）。
- 未收敛职业的其余可读形状（角色名单摘要、技能态），与 Spec 的 Non-Goals 一致。
- 未新增协议帧，未改任何帧的字节布局：Protocol 侧改动仅落在 5 个 slice 的 handler 与
  `TownReturnProjection` 的内部签名上，**没有任何 codec / 消息记录被改**
  （`SelectCharacterPayloadCodecTests.cs` 的改动只是去掉退役字段的构造实参）。
- 分层未破：新 Abstractions 文件只依赖 `Dfo.Server.Data.*` 与同层；
  store 实现在 Persistence；服务实现在 Application；组合根接线在
  `ApplicationModule.cs` / `PersistenceModule.cs`。
- 切片架构：`SelectDungeonHandler` 是 `ClientMessageHandler` 而非命令处理器，无嵌套 dispatch；
  它新增的两次读经 `AmbientStoreAccess.ReadAsync`（`CharacterStateAccess.cs:28-48`），
  **无持久写**，因此不触发「凡能触达持久写的命令一律 Transactional」这条。
- 事务结算顺序：facet 按首次使用顺序登记（`SqliteDatabaseParticipant.cs:59-61`）；
  连招 store 今日零生产调用方，从不加入任何既有用例，**别的 store 结算顺序未变**（CK-021 成立）。

## 三、关于「设置与连招零生产调用方」的独立复核

**同意不推迟，独立结论与 TASK-04 快审一致但理由自行成立。** 依据三条：
契约 Scope In 与 C-3/C-4 把两者写成本成果的成功条件而非可选项，推迟即是对着明文契约做范围收窄；
两者都不是空接口——有真表、真 SQL、真行，`CharacterSettingsTests` 与 `DarkKnightComboBookTests`
（后者 15 条，覆盖写入结算、回滚、越界拒绝、事务参与者归并）**都对着真 SQLite 库验证**；
且 CK-016 本就要求如实报数而非要求非零。
死代码风险由 `AGENTS.md` 里那段「别当死代码删掉」的显式提醒承接，并写明了缺的是哪两个协议。

## 四、非阻断观察

以下均不构成阻断，记录供后续成果参考。

1. **`ICharacterPositionDomainService.Find` 按角色寻址，而所有改动方法按会话精确寻址。**
   （`ICharacterPositionDomainService.cs:36` vs `Place`/`CrossInto`/`StepTo`/`ForgetExact`）
   清理路径新增的合取式 `positions.Find(session.CharacterId) is { } standing`
   （`SelectCharacterLifecycleServices.cs:72-74`）与三个协议守卫，靠的是
   「发布与清理成对」这个**不变量**而不是检查本身。今日不可达错误——运行态目录按账号键且会话精确，
   被顶替的会话在 `TryGet` 就被挡住——但一个会话精确的 `Find` 会让守卫自明而非靠论证。

2. **`SetUserPositionHandler` 没有跟随另外三个 handler 加上坐标合取式。**
   （`TownProjectionSlice.cs:345-355`）它直接调 `StepTo`，无记录时会抛而非记日志忽略。
   `Publish` 里 `runtimes.Publish` 与 `positions.Place` 是两条独立语句
   （`SelectCharacterLifecycleServices.cs:185-186`），理论上存在极窄的落入窗口。
   窗口极小且客户端此时尚未被告知落位，但四个 handler 的写法不齐是可见的不对称。

3. **`SelectDungeonHandler` 每次进本新增两次数据库往返**（原先零次）。
   用例外读会各自开一个边界再回滚（`CharacterStateAccess.cs:39-47`）。不是行为改变，是开销改变。

4. **Spec 的一句前提与代码不符，实现是对的、Spec 是错的。**
   Spec *Why and Outcome* 称「任务状态……被各自改变它们的路径写回，不会变陈」；
   实测 `QuestOfferRefreshDomainService.RefreshAsync`（`QuestOfferRefreshDomainService.cs:59-72`）
   只更新 `World.Subject` 与 `World.Content`，`AcceptQuestHandler.cs:106` 与
   `FinishQuestHandler.cs:144` 也都不动 `CharacterEntry.Quests`——那份拷贝**自入场起就是陈的**。
   `DungeonEntrySlice.cs:471-476` 的注释说对了这一点。
   因此这处重指**实质是一个潜在缺陷的修复**，而非纯粹的换源；它今天逐值不变，
   **仅仅因为 `DungeonLayoutRuleService.SelectMaze` 刻意不读 facts**。
   一旦那条规则开始读 facts（其注释已把这列为待办），本次改动就会变成可观察的行为差异。
   建议在后续成果里把这层依赖记下来。

5. **连招 store 的写路径今日零调用方，其 facet 从未在生产中入伙任何事务。**
   CK-021 的「结算顺序未变」今日成立，但第一次真实调用才是这个 facet 首次参与结算，
   届时需要重新确认顺序假设。

6. **TASK-09 自检里的 `lf_lines` 186→189 是 `split("\n")` 计数。**
   实测 `AGENTS.md` 现有 **530 个换行符 / 531 个 split 分片**，确认是同一个 off-by-one 模式，
   按换行符计应为 185→188。不影响任何算术与结论，按非阻断处置，无需返工。

7. **疲劳占位值 `1` 与 `DungeonDefinition.ConsumesFatigue` 的冲突**已在
   `AGENTS.md`、`Docs/DungeonEntryNotes.md` 与 `SelectDungeonHandler.FatigueAnEntryCosts`
   的 `<remarks>` 三处互指着写明，并已移交 spec-002。本成果不记账，线上无后果，符合契约。

## 五、结论

契约 C-1..C-11 全部达成；无缺失、无多余；核心不变量「服务器对客户端逐值不变」
经机检确认既有断言一字未改，并经三处独立等价性核查支撑；
硬约束（无新帧、无字节布局改动、分层、切片、`store` 单一读法、未提交未 stash）全部满足。

`verdict: approved`。

本评审是完整变更的门禁结论，**不替代随后的独立新鲜验证**。
