# 203 背景调查：协议层“先塔后普通”分发、陈旧帧与地下城参与登记

基线：`main` 7adec858（工作区 `task/dungeon-owner-routing-20261028` 与之相同）。只读调查。
路径前缀省略：`P/` = `Server/src/Dfo.Server.Protocol/Game/`，`A/` = `Server/src/Dfo.Server.Application/`，`T/` = `Server/tests/`。

## 0. 公共事实

| 事实 | 证据 |
|---|---|
| 陈旧判定 = `InboundOrdinal is > 0 && !SequenceIsFresh`；`InboundOrdinal` 为 null（测试常用 `GameMessageMetadata.Empty`）时永远不算陈旧 | `P/TowerProtocol.cs:5`；`Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs:512-518`；传输层赋值 `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs:253` |
| `SequenceIsFresh` 默认 false，所以只设置 `InboundOrdinal` 的测试帧就是陈旧帧 | `NetworkMessages.cs:518` |
| `TowerProtocol.IsStale` 在 `Server/src` 共 19 处 | 见 §1 各行 |
| CMD handler 是 Scoped（每个 CMD 一个实例），`closeTargets` 这类字段状态只在本 CMD 内有效 | `A/Commands/CommandDispatchingExtensions.cs:77`；`P/Party/ExactSessionClosure.cs:49-60` |
| handler 体在 `WriteAsync/ReadOnlyAsync` 之外**没有**环境事务（事务由工作单元各自 `transactions.Begin`），所以 handler 体里调用 `bossTowers.Find` 不会触发 Boss 塔的 `Pending.AuthenticatedSessions/Observe` 副作用 | `A/Transactions/CommandWorkUnits.cs:51,289`；`Document/GameplayExecution.md:57-63`；Boss 副作用条件 `A/World/BossTower/BossTowerDomainService.cs:51-52`（推断：未逐行追踪总线入口） |
| 消息 handler 丢弃 CMD 的 bool 返回值（`_ = await commands.SendAsync(...)`） | `P/GroundPickup/GroundPickupSlice.cs:294`、`P/Party/PartyCommandSlice.cs:387`、`P/CharacterDeath/CharacterDeathSlice.cs:72`、`P/CharacterDeath/UseRevivalCoinSlice.cs:53`、`P/DungeonClear/LeaveDungeonSlice.cs:97`、`P/DungeonClear/DungeonClearSlice.cs:158`、`P/FinishLoading/FinishLoadingSlice.cs:106`、`P/DungeonEntry/DungeonEntrySlice.cs:493`。CommandInvoker 是否使用返回值：**未确认** |
| `*Exact`：`IsInWorld(session)` + 再次 `Find(session)` + RunId + Room.Version 相同 | `P/Party/BossTowerActionProjection.cs:207-210`；`P/Party/IllusionTowerActionProjection.cs:207-210`。紧接在 `Find(session)` 之后调用时，实际只多出 `IsInWorld` 检查 |

## 1. 分发点完整清单

### 1.1 `git grep` 命中的 31 处、13 个文件，按性质归类

| 文件:行 | 所在方法 | 性质 |
|---|---|---|
| `P/CharacterDeath/GameCharacterDeathCommandHandler.cs:107,121` | DieBossAsync / DieIllusionAsync | **命令分发**（0x0028） |
| `P/GroundPickup/GameGetItemCommandHandler.cs:140,181` | PickupBossAsync / PickupIllusionAsync | **命令分发**（0x002B） |
| `P/GameMonsterDeathCommandHandler.cs:104` | RunAsync | **命令分发**（0x0027）外层 Boss 判定 |
| `P/MonsterTowerCommandPhases.cs:31,50` | KillBossAsync / KillIllusionAsync | **命令分发**（0x0027） |
| `P/TowerLoadingCommandPhases.cs:53,79,108,126,138` | CompleteBossLoading / AdvanceBoss / CompleteIllusionLoading / StartIllusion / AdvanceIllusion | **命令分发**（0x0025/0x002D/0x009F） |
| `P/GamePartyTransitionCommandHandler.Exit.cs:50,80` | SelectBossReturnAsync / SelectIllusionReturnAsync | **命令分发**（0x0048） |
| `P/DungeonSelection/GameDungeonSelectionCommands.cs:128,145,161` | SelectionPrepareBoss / SelectionLeaveBoss / SelectionLeaveIllusion | **命令分发**（0x000F/0x0084） |
| `P/GamePartyTransitionCommandHandler.Stages.cs:452,733` | IllusionTowerExactSessions / BossTowerExactSessions | 组队协调内部的精确性校验（属于 0x000D/0x000E/0x0079/0x002A 的塔分支） |
| `P/DungeonEntry/BossEntryCommandPhases.cs:194` | BossEntryCurrent | 入场后精确性校验，不是分发 |
| `P/DungeonEntry/IllusionEntryCommandPhases.cs:197` | IllusionEntryCurrent | 同上 |
| `P/Party/BossTowerActionProjection.cs:102,209,215,225` | SendCommittedBatch 重试 / BossTowerExact / ExactMembers / CurrentRun | **投影内部读塔状态**，不是命令分发 |
| `P/Party/IllusionTowerActionProjection.cs:100,209,215,225` | 同上（幻影塔） | **投影内部**，不是命令分发 |
| `P/TowerReturnCommandHandler.Stages.cs:124,143` | ReturnBossTowerRunAsync / ReturnIllusionTowerRunAsync | **计时器驱动**的单一塔通道（`FindReturnsDue` → 冻结 context，`P/TowerReturnCommands.cs:130-143`），无陈旧帧，不是客户端分发 |

### 1.2 grep 未命中、但同样按“先塔后普通”或按塔归属分发的入口

| 入口 | 证据 | 备注 |
|---|---|---|
| `GameUseRevivalCoinCommandHandler.RunAsync`（0x0029） | `P/CharacterDeath/GameUseRevivalCoinCommandHandler.cs:26-33` | 字段名是 `bossTower`（单数），grep 漏掉；只有 Boss → 普通，没有幻影分支 |
| `TryGiveUpTowersAsync` → `CoordinateBossTowerPartyAsync` → `CoordinateIllusionTowerPartyAsync` | `P/GamePartyTransitionCommandHandler.Exit.cs:102-106` | 0x002A |
| `HandleMutationAsync` → Coordinate Boss → Illusion → 普通 | `P/GamePartyTransitionCommandHandler.Stages.cs:755-773` | 0x000D/0x000E/0x0079 |
| `Coordinate*PartyAsync` 的归属判定：`*Capture` 对**调用者、目标及所有在线队员**都执行 `Find`，任何一人在塔就由塔分支处理 | `Stages.cs:686-715`（Boss）、`Stages.cs:405-434`（幻影）；`Runs.Count==0` → NotHandled `Stages.cs:503-504`/`222-223` | 归属按整队判定，不是只看调用者 |
| `GameMapLoadingCommandHandler.HandleAsync(FinishLoading)` | `P/GameMapLoadingCommandHandler.cs:85-97` | 0x0025 |
| `MoveOrdinaryAsync` | `P/OrdinaryLoadingCommandPhases.cs:92-124` | 0x002D |
| `HandleAsync(IllusionTowerStage)` | `P/GameMapLoadingCommandHandler.cs:102-108` | 0x009F，只有幻影一个通道 |
| `EnterSelectDungeonAsync` | `P/DungeonSelection/DungeonSelectionCommandPhases.cs:170-177` | 0x000F，只有 Boss 分支 |
| `BackToVillageAsync` | `P/DungeonSelection/DungeonSelectionCommandPhases.cs:26-35` | 0x0084 |
| `SelectDungeonAsync`（0x0010） | `P/DungeonEntry/SelectDungeonCommandPhases.cs:38-56,115-140` | 按**目标地下城**是否属于塔分发（`Supports`），不按当前归属；陈旧帧已在入口统一判一次 |
| 会话清理（断线/回选角） | `A/Sessions/SelectCharacterLifecycleServices.cs:129-155`（**幻影 → Boss → 普通**）；`A/Commands/CommandSelectedSessionCleanup.cs:37-38` | 生命周期而不是客户端命令；顺序与命令侧相反 |
| 只读归属读取（不是命令分发） | `A/Characters/CharacterVisibleSessionDomainService.cs:18-29`（Boss→幻影→普通，可见集合投影）；`P/Creatures/CreatureRuntimeDelivery.cs:172-190`、`P/Creatures/RenameCreatureHandler.cs:81-91`（只看幻影）；`P/QuestCompletion/GameFinishQuestCommand.cs:254-260`（只看幻影）；`A/Inventory/NpcShopDomainService.cs:34-36`（Boss 用 `CanShop`，否则看 participation）；`A/Inventory/ItemUseEffectDomainService.cs:42`（participation==BossTower）；`P/InventorySort/GameSortItemCommand.cs:47`、`P/RecoverStamina/RecoverStaminaSlice.cs:150`、`A/Inventory/CargoTransferDomainService.cs:24`、`A/Inventory/InventoryExpansionDomainService.cs:154`（`IsInTower`）；`A/Characters/PlayingCharacter.cs:168,195`（runs/IsInTower/participation 三者并查）；`A/World/DungeonAdmissionDomainService.cs:49` | 门禁或投影受众，不属于本次路由范围 |

### 1.3 客户端命令逐条分析（13 个按归属分发 + 0x009F 单通道 + 0x0010 按目标分发）

列说明：“陈旧位置”= 该分支里 `IsStale` 相对归属查询的位置；返回值 true = 已处理（停止），false = 继续下一分支。

| # | 命令 / opcode / 入口 | 分支顺序 | Boss 分支：陈旧位置→返回 | 幻影分支：陈旧位置→返回 | 普通路径自己判陈旧？ | 查归属前的副作用 | 查询方式 / Exact |
|---|---|---|---|---|---|---|---|
| 1 | GET_ITEM **0x002B**，`GameGetItemCommandHandler.HandleAsync` `GroundPickup/GameGetItemCommandHandler.cs:87-117` | Boss→幻影→普通 | **查后判**（`:140-142`）：不在 Boss 返回 false；在 Boss 且陈旧返回 true | **先判后查**（`:180-182`）：陈旧直接 true（不论在不在塔、有没有会话） | 否（`:92-100`） | 无 | `FindByConnection` + `Find(session)`；之后 `BossTowerExact` `:143` / `IllusionTowerExact` `:183`（失败 → true 吞掉）。普通用 `runs.Find(characterId)` `:98` |
| 2 | CHARACTER_DEATH **0x0028**，`GameCharacterDeathCommandHandler.HandleAsync` `CharacterDeath/GameCharacterDeathCommandHandler.cs:70-91` | 会话为空直接 true（`:73`）→Boss→幻影→普通 | **先判后查**（`:106-108`）→ 陈旧一律 true | 先判后查（`:120-122`），实际到不了陈旧（Boss 已吞） | 否（`:76-79`） | `FindByConnection` | `Find(session)` + Exact `:109`/`:123`；普通 `runs.Find(characterId)` + `registry.Find(runId)` 精确会话 `:76-77` |
| 3 | MONSTER_DEATH **0x0027**，`GameMonsterDeathCommandHandler.RunAsync` `GameMonsterDeathCommandHandler.cs:93-120` | 外层 `bossTowers.Find(reporter)`（`:104`）→ 幻影（仅 `ObjectCode is null`，`:110-112`）→ 普通 | 外层**查后**进入；`ObjectCode==null` 时 KillBoss **先判后查**（`MonsterTowerCommandPhases.cs:30-32`）→ true；`ObjectCode!=null` 在 Boss 中直接 return（`:106-108`，陈旧/新鲜同样忽略） | `ObjectCode==null`：**先判后查**（`MonsterTowerCommandPhases.cs:48-51`，注释称保留原 helper 行为）→ 陈旧一律 true；`ObjectCode!=null`：**不进入幻影分支** | 否 | Debug 日志 `:95-97`；reporter 为空 Debug 日志 `:98-103` | 外层 `Find(reporter)`，KillBoss 内再 `FindByConnection`+`Find(session)`+Exact `MonsterTowerCommandPhases.cs:31-33`；幻影同理 `:50-52` |
| 4 | FINISH_LOADING **0x0025**，`GameMapLoadingCommandHandler.HandleAsync` `GameMapLoadingCommandHandler.cs:85-97` | Boss→幻影→普通（普通也处理城镇加载 `OrdinaryLoadingCommandPhases.cs:36-45`） | **先判后查**（`TowerLoadingCommandPhases.cs:51-54`）→ 陈旧一律 true | 先判后查（`:106-109`），到不了 | 否 | 无 | `Find(session)`；Boss 先 `CaptureLoadingConnections` 再 Exact（`:55-57`），幻影同（`:110-112`）；普通 `runs.Find(characterId)` + 精确会话 `:36-52` |
| 5 | MOVE_MAP **0x002D**，`MoveOrdinaryAsync` `OrdinaryLoadingCommandPhases.cs:92-124` | 会话为空 → Debug 返回（`:101-108`）→ Boss→幻影→普通 | **先判后查**（`TowerLoadingCommandPhases.cs:72-77`）：陈旧时写 **Warning** “Move-map ignored … reason=StaleSequence” 并 true，外层再写 Debug “route=BossTower”（`OrdinaryLoadingCommandPhases.cs:110-115`） | 先判后查（`TowerLoadingCommandPhases.cs:136-139`），到不了 | 否 | Debug 日志 `:97-100` | `Find(session)`；Boss 查后先 Info 日志 `:81-83`、Capture `:84`，再 Exact `:86-91`（失败有 Warning）；普通 `runs.Find(characterId)` `:124` |
| 6 | ILLUSION_TOWER_STAGE **0x009F**，`GameMapLoadingCommandHandler.cs:102-108` | 仅 `Mode==1` 时进入幻影；无 Boss、无普通 | — | **先判后查**（`TowerLoadingCommandPhases.cs:124-127`），不在塔返回 false 后被忽略 | — | 无 | `Find(session)` + Exact `:128` |
| 7 | EPLP_COMMAND **0x0048**，`EplpAsync` `GamePartyTransitionCommandHandler.cs:110-117` | 会话非空：Boss→幻影→普通；会话为空：直接普通（`:116-117` 有 `session is not null` 前置） | **查后判**（`Exit.cs:50-53`），且查到后先 `closeTargets.Capture`（`:52`）再判陈旧 → true | **先判后查**（`Exit.cs:79-81`）→ 陈旧一律 true（不论在不在塔） | 否（`:119-288`） | `FindByConnection` | `Find(session)`；Boss Exact `:54`；幻影 `state!=LeaveState` 早退 `:83` 后 Exact `:84`；普通 `runs.Find(characterId)` `:119` |
| 8 | GIVEUP_GAME **0x002A**，`GiveUpAsync` `GamePartyTransitionCommandHandler.cs:294-310` | **入口统一判陈旧**（`:299-300`）→ 会话 → Boss 协调 → 幻影协调 → 普通 | 入口已判 | 入口已判 | 入口已判 | 无（陈旧判定在任何查询之前） | 协调按**整队**判定（`Stages.cs:686-715` / `405-434`），`allowVirtual=true`（`Exit.cs:104-105`），之后 `*ExactSessions`（`Stages.cs:717-736` / `436-455`） |
| 9-11 | LEAVE_PARTY **0x000D** / REMOVE_PARTY_MEMBER **0x000E** / CHANGE_PARTY_LEADER **0x0079**，`HandleMutationAsync` `Stages.cs:755-812` | `request.DemandCurrent()` → **入口统一判陈旧**（返回 **false**）→ Boss 协调 → 幻影协调 → 普通 | 入口已判 | 入口已判 | 入口已判 | `DemandCurrent()`（`:759`） | 同上，`allowVirtual=false`（`:764,766`）；幻影分支 Remove 失败额外发 `RemovePartyMemberRefusal`（`:769-770`），Boss 分支不发（`:765`） |
| 12 | ENTER_SELECT_DUNGEON **0x000F**，`EnterSelectDungeonAsync` `DungeonSelectionCommandPhases.cs:170-177` | 仅 Boss → 普通（**无幻影分支**） | **查后判**（`GameDungeonSelectionCommands.cs:127-131`，注释说明“选图先查归属，回城相反”）→ 陈旧 true。另外：释放成功时 `return !released.Changed` = **false**，继续执行普通选图（`:135-137`） | — | 否 | 无 | `Find(session)` + Exact + 队长校验 `:132` |
| 13 | BACK_TO_VILLAGE **0x0084**，`BackToVillageAsync` `DungeonSelectionCommandPhases.cs:26-35` | 会话非空：Boss→幻影→普通；会话为空：直接普通 | **先判后查**（`GameDungeonSelectionCommands.cs:143-146`）→ 陈旧一律 true | 先判后查（`:159-162`），到不了 | 否 | `FindByConnection` | `Find(session)` + Exact（`:147`/`:163`），Exact 之后才 Capture |
| 14 | USE_REVIVAL_COIN **0x0029**，`GameUseRevivalCoinCommandHandler.RunAsync` `:23-62` | Boss → 普通（**无幻影分支**） | **完全不判陈旧**；在 Boss 即发拒绝 CMD（`:28-32`） | — | 否 | `FindByConnection` | `bossTower.Find(payer)`，无 Exact；普通 `runs.Find(characterId)` + registry 精确会话 `:33-35` |
| 15 | SELECT_DUNGEON **0x0010**，`SelectDungeonAsync` `DungeonEntry/SelectDungeonCommandPhases.cs:32-140` | 先 `TryLoadAsync`，再**入口统一判陈旧**（`:50-56`，Warning），再按**目标**分 Boss/幻影/普通（`:115-140`） | 入口已判 | 入口已判 | 入口已判 | `currentViews.TryLoadAsync`（`:39-40`） | 不按当前归属分发（参考形态，已经是“统一判一次”） |

### 1.4 由此推出的当前陈旧帧实际行为

“城镇”= 有会话但三种副本都不在；“无会话”= `FindByConnection` 为 null。

| 命令 | 普通副本 | Boss 塔 | 幻影塔 | 城镇 | 无会话 |
|---|---|---|---|---|---|
| 0x002B 拾取 | 丢弃（由幻影分支先判吞掉） | 丢弃（Boss 分支） | 丢弃 | 丢弃 | 丢弃（幻影先判；新鲜帧会写 Warning `:95`，陈旧帧不写） |
| 0x0028 角色死亡 | 丢弃（Boss 分支先判） | 丢弃 | 丢弃 | 丢弃 | 入口直接 true（`:73`） |
| 0x0027 怪物死亡 `ObjectCode==null` | 丢弃（幻影先判） | 丢弃（KillBoss 先判） | 丢弃 | 丢弃 | 仅 Debug 日志 |
| 0x0027 怪物死亡 `ObjectCode!=null`（物件破坏） | **照常处理**（普通路径无陈旧判定，`DestroyObjectAsync` `:131-138`） | 忽略（`:106-108`，新鲜帧也忽略） | **进入普通路径**：`runs.Find` 为 null，回 `MonsterDeathNotification.Empty`（`:113-119`，新鲜帧同样如此） | 回 Empty（同左） | 仅 Debug 日志 |
| 0x0025 加载完成 | 丢弃（Boss 先判） | 丢弃 | 丢弃 | 丢弃（**城镇加载也不回 001E**） | 丢弃（新鲜帧此时写 Warning `:92`） |
| 0x002D 过图 | 丢弃，写 Warning 和 “route=BossTower” Debug | 同左 | 同左 | 同左 | Debug 后返回（不判陈旧） |
| 0x009F 幻影开战 | 不在塔，忽略 | 忽略 | 丢弃 | 忽略 | 忽略 |
| 0x0048 EPLP | 丢弃（幻影先判） | 丢弃（Boss 查后判，判前已 Capture） | 丢弃 | 丢弃 | **普通路径处理**：`TryLoadAsync` 为 null，写 Warning 后返回 |
| 0x002A 放弃 | 入口丢弃 | 入口丢弃 | 入口丢弃 | 入口丢弃 | 入口丢弃（早于会话查询） |
| 0x000D/0x000E/0x0079 组队变更 | 入口丢弃（在 `DemandCurrent` 之后），无拒绝帧、无日志 | 同左 | 同左 | 同左 | 同左 |
| 0x000F 进入选图 | **照常处理**（普通路径无判定） | 丢弃 | **照常处理**（无幻影分支） | **照常处理** | 普通路径：Warning 后返回 |
| 0x0084 回城 | 丢弃（Boss 先判） | 丢弃 | 丢弃 | 丢弃 | 普通路径：Warning 后返回 |
| 0x0029 复活币 | **照常处理** | **照常回拒绝帧** | **静默返回**（`runs.Find` 为 null） | 静默返回 | 静默返回 |
| 0x0010 选择地下城 | 入口丢弃（Warning） | 同左 | 同左 | 同左 | 无会话时先 Warning 返回 |

结论：
- 目前真正“在入口统一判一次”的只有 0x002A、0x000D/0x000E/0x0079 和 0x0010。
- 对 0x002B、0x0028、0x0025、0x002D、0x0084、0x0048，以及 `ObjectCode==null` 的 0x0027，结果都是“三种副本加城镇全部丢弃”。差别只在丢弃发生在哪个分支，以及日志。
- 0x000F、0x0029，以及 `ObjectCode!=null` 的 0x0027，在普通副本和城镇中**不丢弃**陈旧帧；Boss 塔里的结果则因命令而异。

### 1.5 行为不一致点（零行为变化必须逐条保留，或者明确接受变化）

1. **陈旧判定与归属查询的先后顺序不一**。“查后判”：GetItem Boss（`GameGetItemCommandHandler.cs:139-142`）、EPLP Boss（`Exit.cs:50-53`）、进入选图 Boss（`GameDungeonSelectionCommands.cs:127-131`）。其余塔分支都是“先判后查”。代码注释已明确记录这种不一致是刻意保留的原 helper 行为：`GameGetItemCommandHandler.cs:139`、`MonsterTowerCommandPhases.cs:48`、`GameDungeonSelectionCommands.cs:130`。
2. **普通路径从不自己判陈旧**，依赖前面某个塔分支“顺带”丢弃。因此只有 Boss 分支的命令（0x000F、0x0029），普通路径会处理陈旧帧。
3. **0x0027 物件破坏**（`ObjectCode!=null`）完全绕过幻影分支：普通路径处理陈旧帧；幻影塔中回 Empty 通知。
4. **0x002D 陈旧帧**在任何模式下都写 Boss 标签日志（`TowerLoadingCommandPhases.cs:74`、`OrdinaryLoadingCommandPhases.cs:113`）。
5. **0x0048 Boss 分支**在判陈旧前先 `closeTargets.Capture`（`Exit.cs:52`）。handler 是 Scoped，陈旧返回后不再使用，**推断**没有外部可观察差异。
6. **0x0029** 只有 Boss 分支，且不判陈旧；幻影塔成员静默返回，不发拒绝帧。
7. **0x000F** 只有 Boss 分支，而且 Boss 分支释放成功后返回 false，**由同一命令继续执行普通选图**（`GameDungeonSelectionCommands.cs:137`）。这是“塔通道交接给普通通道”，不是单通道。
8. **0x009F** 只有幻影通道，没有普通回落。
9. **组队类命令（0x002A、0x000D/0x000E/0x0079）按整队归属判定**：调用者、目标或任一在线队员在塔，就进入塔分支（`Stages.cs:696-713`、`415-432`）。`Runs.Count>1` 时拒绝，原因 PartyGenerationChanged（`Stages.cs:505-506`）。
10. **0x000E 幻影分支失败时发拒绝帧，Boss 分支不发**（`Stages.cs:765` 对比 `:769-770`）；幻影分支写组队日志，Boss 分支在此处不写（`:771`）。
11. **会话为空时**：0x0048、0x0084 跳过两塔直接进普通路径（Warning）；0x002B 由幻影分支先判陈旧，陈旧帧被静默丢弃；0x0025 由 Boss 分支先判陈旧，陈旧帧被丢弃；0x0028 在入口 true。
12. **组队变更陈旧时返回 false，其余返回 true**（`Stages.cs:760`）。返回值被消息 handler 丢弃，**推断**没有外部可观察差异。
13. **会话清理顺序是幻影 → Boss**（`SelectCharacterLifecycleServices.cs:131,147`），命令侧是 Boss → 幻影；可见会话解析是 Boss → 幻影 → 普通（`CharacterVisibleSessionDomainService.cs:18-29`）。

## 2. `IDungeonParticipationDomainService` 语义

### 2.1 实现要点（`A/World/DungeonParticipationDomainService.cs`）

| 项 | 行为 | 证据 |
|---|---|---|
| 数据 | 进程内 `CharacterId → (Mode, RunId)`，**按角色，不含会话代次** | `:10`；接口 `Server/src/Dfo.Server.Abstractions/World/IDungeonParticipationDomainService.cs:4-14` |
| Find | 在可写事务中返回本事务的待定视图（并 **enlist** 事务参与者，测试记为 ConditionalEnlist），否则读已提交 | `:14-18`、`:12-13`；`T/Dfo.Server.Application.Tests/Execution/GameplayWriteEntryTableTests.cs:252` |
| TryClaim | 写断言；任一角色已有不同 owner 时失败，**例外是 Ordinary→Ordinary 允许覆盖**（RunId 不同也允许）；没有事务会抛异常 | `:19-26`、`:12` |
| Release | 有事务时写待定（仅 `Find==expected`），无事务时转 `ReleaseCommitted` | `:27-32` |
| ReleaseCommitted | 立即修改已提交表（仅 owner 相同） | `:33-37` |
| 事务可见（transaction-visible） | 待定变更只对本事务的 Find 可见；Prepare 校验 Before 未变并加预留，防止交错事务抢占；Commit 应用；Rollback 解除预留 | `:38-77`；测试 `T/Dfo.Server.Application.Tests/World/DungeonParticipationDomainServiceTests.cs:13,38` |
| 依赖注入 | 生产注册单例；三个领域服务都把它作为**可空**可选参数注入（null 时不登记） | `A/ApplicationModule.cs:325`；`A/World/DungeonRunDomainService.cs:24,59`；`A/World/BossTower/BossTowerDomainService.cs:31`；`A/World/IllusionTower/IllusionTowerDomainService.cs:24` |

### 2.2 各模式何时登记、何时释放

| 模式 | 登记（TryClaim） | 释放 | 对应运行状态变化 |
|---|---|---|---|
| 普通（含镜像、祭坛、Sao、绝望塔、Tournament 等，都走 `DungeonRunDomainService`） | 入场：`DungeonRunDomainService.cs:127-129`，在事务内；运行在 `EntryCommit.CommitAsync` 发布（`DungeonRunDomainService.Transitions.cs:23-36`；无事务分支 `:141-145` 在 participation 已注入时会因 TryClaim 抛异常而到不了） | **立即释放** `ReleaseCommitted`，位于 `Release(aggregate, id)`（`DungeonRunDomainService.cs:1448-1451`）。调用方有：`EndRun`（整局结束、回城、团灭、清场返回、替换旧局，`:1476-1483`）、`Depart`（个人离开，`:1406-1419`） | `runs.Find(char)` 经**当前队伍键**及成员资格（`:406-412`、`:1486-1490`、`:1822-1834`、`:1884-1885`） |
| Boss 塔 | `EnterAsync` 事务内 `:114-117`，与 `Pending.Add(run)` 同一事务 | `ReleaseForSelectionAsync` 对全员 Release 并 Leave（`:236-256`）；`RewardingMutation` 中离开成员 Release（`:398-399`）。后者覆盖 LeaveAsync（主动离开、组队离开、断线 `disconnected=true`）、计时器回城（`TowerReturnCommandHandler.Stages.cs:132-133`）、加载超时。都与运行变更在同一事务 | 成员从 `Members` 移除（`BossTowerRun.cs:138-149`）；Commit 更新 membership，最后一人离开时删除运行（`BossTowerDomainService.cs:459-479`） |
| 幻影塔 | `EnterAsync` 事务内 `:116-117`（失败时返回 Refuse，不抛异常） | 仅 `RewardingMutation` 中离开成员（`:328-329`），覆盖 LeaveAsync、断线、计时器回城（`TowerReturnCommandHandler.Stages.cs:150-151`） | Commit `:380-398` |
| 结算后等待回城（两塔） | 成员仍在 run 中，participation 仍登记 | 由计时器离开时释放 | **一致** |

### 2.3 登记与 Find 可能不一致的时间窗口

| # | 窗口 | 状态 | 证据 / 置信度 |
|---|---|---|---|
| W1 | **会话代次**：塔的 `Find(session)` 要求 `run.Members` 中有**完全相同**的 `SelectedSessionIdentity`（含 ConnectionId、ConnectionGeneration、WireUserId），participation 只看 CharacterId。同一角色的新会话已发布、旧会话塔成员资格尚未清理时：participation=BossTower/IllusionTower(runId)，`Find(newSession)`=null，`IsInTower(char)`=true | 现有代码把新会话的命令交给普通路径（`runs.Find` 通常为 null）。定位函数若按 participation 得到“塔”再核对 RunId，**会落到不同分支** | 身份字段 `Server/src/Dfo.Server.Data/World/SelectedSessionModels.cs:8-16`；Find `BossTowerDomainService.cs:48-54`、`IllusionTowerDomainService.cs:39-43`；清理注释说明存在“目录竞争中已落败的旧代次仍需释放” `A/Sessions/SelectCharacterLifecycleServices.cs:129-130`。窗口确实存在；**其他命令能否在这段时间插入执行，未确认** |
| W2 | **普通运行由队伍键查找**：`runs.Find(char)` 需要角色仍在当前队伍且属于该局成员。角色离队但尚未 `Depart` 时，`runs.Find` 为 null，participation 仍为 Ordinary | `MutatePartyStateAsync` 先 `parties.LeaveParty`，再 `runs.EndAsync`（`Stages.cs:161-178`），发生在同一 CMD 内。断线清理同样先清理队伍，再结束运行（`SelectCharacterLifecycleServices.cs:157-170`）。只有 1 人的真实队伍离队后队伍被删除，键变成 Virtual（`A/Parties/PartyService.cs:457-473`） | 只在同一 CMD 内可见；若 `EndAsync` 未成功或未调用，残留 HoldsRunState（`DungeonRunDomainService.cs:1888-1889`）。**跨 CMD 的残留是否存在，未确认** |
| W3 | **普通入场事务中**：TryClaim 后、提交前，可写事务内 `participation.Find`=Ordinary(new)，`runs.Find` 仍为 null 或旧局（runs 不感知事务） | 只在入场工作单元内部可见；handler 体在事务外读取时两者一致 | `DungeonRunDomainService.cs:127-139` |
| W4 | **替换旧普通局**：Ordinary→Ordinary 覆盖。EntryCommit 内 `EndRun(previous)` 会对旧 owner 调 `ReleaseCommitted`，因 owner 不同而成为空操作（取决于事务参与者的提交顺序） | 提交后一致 | `DungeonParticipationDomainService.cs:22-23,36`；`Transitions.cs:27-28` |
| W5 | **一次提交中多个参与者的提交顺序**：participation 与 Boss、幻影、普通各自独立加锁提交，执行器外的读者可能看到中间状态 | 执行器串行；`GameplayWriteAssertionTests.cs:296` 名称写明 participation “reads anywhere”。执行器外读者（如 GM HTTP）是否依赖此一致性：**未确认** | `DungeonParticipationDomainService.cs:63-71` |
| W6 | **participation 为 null**（测试夹具手工构造服务，或替身塔服务）：塔或普通 run 存在但 participation 未登记 | 生产注入不受影响；会改变大量测试夹具的行为 | 替身：`T/Dfo.Server.Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs:144`、`T/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerProjectionConcurrencyTests.cs:780`、`T/Dfo.Server.Protocol.Tests/Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs:784`、`T/Dfo.Server.Protocol.Tests/Support/TowerDomainStub.cs:7`、`T/Dfo.Server.Application.Tests/Support/TowerPresenceProxy.cs:7-22`（`IsInTower`=true 但 `Find`=null）、`T/Support/TowerAlgorithmDrivers.cs:56`（`ProtocolAlgorithmFixture.Default`）；手工构造 `T/Support/TestParties.cs:44-47`、`T/Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.Departures.cs:217,349` |
| W7 | **组队类命令按整队归属**：调用者本人不在塔（participation 为 null 或 Ordinary），但队员在塔时，现有代码走塔分支 | 仅凭调用者的定位结果无法复现 | `Stages.cs:686-715`、`405-434` |
| W8 | **0x000F 的 Boss 释放后同一 CMD 交给普通流程**：释放后 participation 待定为 null（事务提交后 null），普通选图在同一命令内继续 | 定位函数在释放前取得的“种类”会过期 | `GameDungeonSelectionCommands.cs:135-137`；`BossTowerDomainService.cs:248-251` |

未发现“participation=null 但塔 `Find(session)` 非 null”的生产窗口：两塔的登记与运行变更都在同一事务内（W5 除外）。普通运行方面，也未发现“`runs.Find` 非 null 但 participation 为 null”的生产窗口：`Release` 只发生在离开或结束时，此时 `runs.Find` 已为 null（W6 除外）。

### 2.4 `Find(session)` 与 `Find(characterId)` 的差异

| | 塔 `Find(SelectedSessionIdentity)` | 塔 `IsInTower(CharacterId)` | 普通 `runs.Find(CharacterId)` | `participation.Find(CharacterId)` |
|---|---|---|---|---|
| 键 | 角色 → run，**再核对完整会话身份** | 角色 → run | 当前队伍键 → run，再核对成员资格 | 角色 |
| 旧代次会话 | null | true | 不涉及会话 | 已登记 |
| 事务内 | Boss：仅可写事务看待定，并有 Authenticated/Observe 副作用（`BossTowerDomainService.cs:43-54`）；幻影：只要有事务就看待定（`IllusionTowerDomainService.cs:34-43`） | 同左 Lookup | 不感知事务 | 可写事务看待定，并 enlist |

## 3. 现有测试覆盖（陈旧帧）

### 3.1 有陈旧帧用例的

| 命令 | 模式 | 测试 | 覆盖程度 |
|---|---|---|---|
| 0x002A 放弃 | 与模式无关（依赖全部为 null） | `T/Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs:532` `Stale_give_up_stops_before_any_current_run_or_party_capture`（经 `T/Dfo.Server.Protocol.Tests/Support/DungeonExitDrivers.cs:59-62` 调用真实 handler） | 入口统一判定 |
| 0x0010 选择地下城 | 普通或无运行 | `T/Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs:364` `Stale_frame_is_ignored_before_the_entry_intent_is_consumed` | 入口统一判定 |
| 0x000F 进入选图 | **Boss 塔**（结算后） | `T/Dfo.Server.Application.Tests/World/DungeonSelectionTowerBoundaryTests.cs:16` `Task42_real_boss_selection_release_preserves_choice_and_stale_guards_and_uses_an_ordinary_protected_unit(stale)` | Boss“查后判”路径；陈旧时不释放，0 个单元 |
| 0x000D、0x000E、0x0079 | — | `T/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs:393` `A_stale_leave_frame_cannot_bind_to_the_current_party`、`:418` `Stale_targeted_party_frames_stop_before_roster_or_session_capture` | **只验证消息 handler**：使用 `RecordingCommandBus`（`:815-834`），CMD handler 并未执行，**`HandleMutationAsync` 的陈旧判定（`Stages.cs:760`）实际未覆盖** |

### 3.2 完全没有陈旧帧测试的组合

| 命令 | 普通 | Boss 塔 | 幻影塔 | 城镇 / 无会话 |
|---|---|---|---|---|
| 0x002B 拾取 | 无 | 无 | 无 | 无 |
| 0x0028 角色死亡 | 无 | 无 | 无 | 无 |
| 0x0027 怪物死亡（两种 ObjectCode） | 无 | 无 | 无 | 无 |
| 0x0025 加载完成（含城镇加载） | 无 | 无 | 无 | 无 |
| 0x002D 过图 | 无 | 无 | 无 | 无 |
| 0x009F 幻影开战 | — | — | 无 | 无 |
| 0x0048 EPLP | 无 | 无 | 无 | 无 |
| 0x0084 回城 | 无 | 无 | 无 | 无 |
| 0x000F 进入选图 | 无 | 有（§3.1） | 无 | 无 |
| 0x0029 复活币 | 无 | 无 | 无 | 无 |
| 0x000D、0x000E、0x0079（CMD 层） | 无 | 无 | 无 | 无 |
| 0x002A | 有（与模式无关） | 同左 | 同左 | 同左 |

说明：塔相关测试普遍使用 `GameMessageMetadata.Empty`（`InboundOrdinal`=null，永远新鲜），例如 `T/Dfo.Server.Application.Tests/World/PartyTowerFootprintBoundaryTests.cs`（16 处）、`IllusionTowerDomainServiceTests.cs`（14 处）、`IllusionTowerPublicFlowTests.cs`（12 处）、`T/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs`（8 处）。真实 TCP 测试的序号都递增且新鲜：`T/Dfo.Server.Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs:100-120`、`IllusionTowerStageFrameIntegrationTests.cs:146-164`。普通副本线上辅助方法固定 `SequenceIsFresh = true`（`T/Dfo.Server.Application.Tests/World/DungeonExitLifecycleTests.cs:186`）。传输层计数器语义有单测：`T/Dfo.Server.Networking.Tests/Runtime/GameInboundFrameCounterTests.cs:8`。

### 3.3 participation 相关测试

`DungeonParticipationDomainServiceTests.cs:13,38`（事务预留）、`GameplayWriteAssertionTests.cs:296`（写断言）、`GameplayWriteEntryTableTests.cs:81,251-255`、`DungeonPartyOwnedRunProtocolTests.cs:38,58`（离开后为 null / 成员回城不结束时仍登记）、`DungeonDepartureProtocolCharacterizationTests.cs:317`、`DungeonRunDomainServiceTests.Departures.cs:203,291`（Ordinary 覆盖与释放次数）。未发现覆盖 W1、W2 窗口的测试。

### 3.4 会被重构波及的测试耦合

- 测试驱动**复制了**生产中“先判陈旧 → Find → Exact”的写法：`T/Support/TowerAlgorithmDrivers.cs:98-107`（Boss）、`:149-157`（幻影），供 `IllusionTowerDomainServiceTests.cs:1132` 使用。
- 测试直接调用 internal 分支方法：`CoordinateBossTowerPartyAsync` / `CoordinateIllusionTowerPartyAsync`（`T/Support/TowerAlgorithmDrivers.cs:120-122` 等）、`ReturnBoss/IllusionTowerRunAsync`、`CompleteBossLoadingAsync`（`T/Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs:155`）、`CompleteOrdinaryLoadingAsync`（`T/Dfo.Server.Protocol.Tests/Game/DungeonEntry/LoadingAlgorithmTestDriver.cs:66`）、`SelectDungeonAsync` / `EnterBossAsync` / `EnterIllusionAsync`（`EntryHandlerDriver.cs:48-50`）。

## 4. 架构测试与源码扫描

- **没有**测试约束 `TowerProtocol`、`IsStale` 或 `bossTowers.Find` 的写法。`git grep` 在 `Server/tests` 中只在测试驱动 `T/Support/TowerAlgorithmDrivers.cs:100,151` 命中 `TowerProtocol.IsStale`，那是复制的写法，不是约束。
- `T/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs:35-60` 按类型后缀约束 CMD handler 的依赖；`IDungeonParticipationDomainService` 以 `DomainService` 结尾，**可以注入**协议 handler。新增的定位类型若不叫 `*DomainService` / `*RuleService`，需要列入豁免或另起名称（豁免判定规则见同文件说明 `:9-22`）。
- `T/Dfo.Server.Application.Tests/Execution/LockRemovalSourceTests.cs:26-27` 是源码扫描，但只针对 `SemaphoreSlim` gate 名单，与本题无关。
- `TowerDepartureSourceCharacterizationTests` / `TowerReturnSourceCharacterizationTests` 名为 Source，实际是计时器源的行为测试，不扫描源码。

## 5. 文档

| 位置 | 要点 |
|---|---|
| `Document/ProtocolCommands.md`、`GameplayExecution.md`、`Architecture.md`、`RefactorPreferences.md`、`ArchitectureReview.md` | **没有**描述塔、普通的分发顺序或陈旧帧规则。`GameplayExecution.md:66-67` 只讲效果请求的同帧重放（Ordinal 保持） |
| `Document/tasks/illusion-tower/tasks/TASK-04.md:18` | 真实网络中 `InboundOrdinal>0 && !SequenceIsFresh` 的请求，应在**绑定当前挑战或消费意图之前**丢弃，防止旧连接帧绑定到重入后的新 run；`Empty` 仅供测试；不能宣称能识别伪造的新鲜序号 |
| `Document/tasks/illusion-tower/tasks/TASK-06.md:13` | 塔相关入口**先丢弃旧帧，再识别涉及的塔**，并捕获精确 session、RunId/RoomVersion 和 party 身份 |
| `Document/tasks/illusion-tower/verification/TASK-06-verification.md:60` | Leave、Remove、ChangeHost、GiveUp 在任何当前 run、party 捕获之前丢弃旧帧 |
| `Document/LordTower-Design-20261019.md:60` | 普通副本、死亡/迷妄塔与领主塔共用**事务性参与占用**，建立与释放绑定 run/session |
| `Document/LordTower-Design-20261019.md:63` | 既有 0010/0025/0027/002D/0048 “按当前玩法分流” |
| `Document/LordTower-Implementation-20261019.md:11-19` | 各入口在 Boss 塔中的当前行为表（0029 明确拒绝；000F 合法释放后进入已有选图投影）。该文引用的 `BossTowerCommands.cs`、`BossTowerProjectionDelivery.cs` 在当前树中**不存在**（文档漂移） |
| `Document/tasks/item-effects-unification/verification/preparatory-findings.md:14` | `SequenceIsFresh` 只认首帧或上一个新鲜 ushort+1，支持回绕 |
| 代码注释（事实上的“规则”） | `GameGetItemCommandHandler.cs:139`、`MonsterTowerCommandPhases.cs:48`、`GameDungeonSelectionCommands.cs:130` 记录了顺序差异是刻意保留 |

## 6. 阻碍“零行为变化统一路由”的风险

1. **入口统一判陈旧会改变行为**的命令：0x000F（普通、幻影、城镇目前**处理**陈旧帧）、0x0029（全部模式都处理）、`ObjectCode!=null` 的 0x0027（普通处理；幻影、城镇回 Empty）。另外 0x0048、0x0084、0x002D 在**无会话**时目前不判陈旧，0x0025、0x002B 在无会话时判。
2. **日志行为**：0x002D 陈旧帧固定写 Boss 标签的 Warning 和 Debug；0x0025、0x002B 无会话时，新鲜和陈旧帧日志不同。是否把日志算作“行为”需要先定。
3. **组队类命令按整队归属**（W7），无法由“调用者 → participation”的单角色定位替代；整队捕获、`Runs.Count>1` 拒绝、ExactSessions 全员校验都属于分支语义。
4. **0x000F 的 Boss → 普通交接**（W8）：同一命令内先走塔通道再走普通通道，违背“只路由到一个通道”。
5. **会话代次窗口**（W1）：participation 按角色、塔 Find 按精确会话。旧代次未清理时，定位结果为“塔”但 RunId 核对失败。必须定义这种情况落到哪里，现状是普通路径。
6. **普通运行查找依赖当前队伍**（W2），participation 与 `runs.Find` 在同一 CMD 内可能短暂不一致；若定位改用 participation，普通快照仍要用 `runs.Find(char)` 并定义不一致时的去向。
7. **participation 依赖可空注入**（W6）：大量测试替身、手工构造的服务没有 participation。改用它定位会让这些测试改走普通路径；按 Mode 取塔快照也需要它们补充 participation 替身。`PlayingCharacter.cs:195` 同时检查三者，也说明现有代码并不单独信任 participation。
8. **各分支私有差异**需要逐一搬进独立通道：0x000E 幻影失败发拒绝帧而 Boss 不发；0x0048 Boss 先 Capture；0x0029 只有 Boss 拒绝、幻影静默；0x009F 无普通回落；会话为空时不同命令的进入路径不同；Exact 失败统一吞掉（true）。
9. **测试网缺口**：除 0x002A、0x0010 和 Boss 下的 0x000F 外，陈旧帧在三种副本中**基本没有** CMD 层测试，组队类命令的 CMD 层陈旧判定也未覆盖。零行为变化需要先补特征测试，否则无法证明。
10. **测试耦合**：测试驱动复制了分支写法（`TowerAlgorithmDrivers.cs:98-107,149-157`），并直接调用 internal 分支方法（§3.4），重命名或合并会引起连带修改。
11. **执行器外读者**（W5）：多参与者提交不是原子的；若定位函数会被执行器外调用，需要确认可见性。**未确认**是否存在这类调用。
