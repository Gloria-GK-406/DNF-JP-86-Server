---
kind: rks-review
target: git diff 7adec858..b6c17eb9（task/dungeon-owner-routing-20261028）
verdict: approved
reviewer: independent subagent (reviewing-work complete-result, code)
---

# 完整结果审查：副本归属命令先定位、再单次路由

## 范围

- 目标：`git diff 7adec858..b6c17eb9`，即分支 `task/dungeon-owner-routing-20261028` 的 HEAD `b6c17eb9`。审查开始时工作区干净。
- 共 7 个提交：契约、规格、计划提交 `aee80420`，以及 TASK-001～006。生产改动集中在 `Server/src/Dfo.Server.Protocol/Game/` 的 20 个文件，另改 2 个文档；测试改动见 diff stat。
- 采用的准则：`reviewing-work` 的 Code 准则（完整代码变更）、Deliverable 准则（文档部分）、仓库 `Document/ArchitectureReview.md`，并参照 `Architecture.md`、`RefactorPreferences.md`、`ProtocolCommands.md`。
- 不在本审查范围：SC-7 的 issue 评论、SC-8 的 PR/CI/Codex 状态。这些属于审查之后的步骤，目前尚未发生。

## 依据

- `TaskContract.md` revision 2：SC-1～SC-8、Constraints。
- `spec.md`：RQ-001～RQ-008、AC-001～AC-007、“位置 → 通道对照”、“陈旧帧行为变化清单”。
- `plan.md`：BD-001～BD-006。
- `evidence/background-7adec858.md`。
- 6 个 Task 文件只作线索使用，结论均以本审查的独立核对为准。

## 独立验证

- 构建：`dotnet build Server/86JP.Server.slnx -warnaserror`，0 警告 0 错误。
- 完整测试：`dotnet test Server/86JP.Server.slnx`，8 个程序集全部通过，失败 0，通过 9162，跳过 942。跳过项为缺 PVF 的既有条件跳过，与 `logs/task-006-full-test.log` 的分程序集计数一致。
- 审查者自拟变异：每项先备份、施加、只跑 `Game.Dungeons` 测试，再用备份还原并 touch。还原后重建，`Game.Dungeons` 254/254 通过，`git status` 干净。

  | # | 变异 | 结果 |
  |---|---|---|
  | R1 | `DungeonLocator.Locate` 幻影塔分支去掉 `run.RunId == runId` | `DungeonLocatorTests.Tower_run_other_than_the_registered_one_is_ordinary_or_none(IllusionTower)` 失败 |
  | R2 | 0x0029 路由把判陈旧挪到 `FindByConnection` 之后（行为测试无法区分） | 结构守护 `Dungeon_owner_routing_reads_staleness_and_ownership_only_where_listed` 失败 |
  | R3 | `LocateParty` 在同时有幻影塔候选时放弃 Boss 优先 | `DungeonLocatorTests.Party_prefers_the_boss_tower_over_the_illusion_tower` 与表征 `A_boss_seat_in_the_party_wins_over_an_illusion_seat` 失败 |
  | R4 | 0x0029 在幻影塔改为回拒绝帧 | 表征 `Revival_coin_is_refused_only_by_a_boss_tower(Illusion)` 失败 |

## 逐条核对

### SC-1 / RQ-002：入口判一次陈旧，通道不判

生产代码中，除定义外，`GameRequestFreshness.IsStale` 共有 13 处调用：

- 11 个路由方法覆盖 13 个命令，每个方法各一次，且都在会话查询与定位之前：
  - 0x002B：`GroundPickup/GameGetItemCommandHandler.cs:91`
  - 0x0028：`CharacterDeath/GameCharacterDeathCommandHandler.cs:75`
  - 0x0027：`GameMonsterDeathCommandHandler.cs:97`
  - 0x0025：`GameMapLoadingCommandHandler.cs:89`
  - 0x002D：`GameMapLoadingCommandHandler.cs:112`
  - 0x0048：`GamePartyTransitionCommandHandler.cs:118`
  - 0x002A：`GamePartyTransitionCommandHandler.cs:320`
  - 0x0084：`DungeonSelection/DungeonSelectionCommandPhases.cs:31`
  - 0x000F：`DungeonSelection/DungeonSelectionCommandPhases.cs:191`
  - 0x0029：`CharacterDeath/GameUseRevivalCoinCommandHandler.cs:29`
  - 0x000D/0x000E/0x0079：`GamePartyTransitionCommandHandler.Stages.cs:719`。此处排在 `request.DemandCurrent()` 之后，这是既有的请求当前性检查，不是会话查询，文档已写明。
- 0x009F：`TowerLoadingCommandPhases.cs:108`。
- 0x0010：`DungeonEntry/SelectDungeonCommandPhases.cs:50`。

各塔通道中原有的 `TowerProtocol.IsStale` 已全部删除，例如 `MonsterTowerCommandPhases.cs`、`TowerLoadingCommandPhases.cs`、`GameDungeonSelectionCommands.cs` 与 `.Exit.cs`。结构守护按成员固定出现次数，R2 证明顺序回退会被拒绝。**达成。**

### SC-2 / RQ-003 / RQ-004 / RQ-005：定位一次，路由到单通道

- 单人命令：每个路由方法调用 `DungeonLocator.Locate` 一次，再 `switch` 到强类型通道。证据见上列各文件的下一行至数行，例如 `GameGetItemCommandHandler.cs:98`、`GameUseRevivalCoinCommandHandler.cs:32`。
- 组队命令：`LocatePartyOf`（`GamePartyTransitionCommandHandler.Stages.cs:688-712`）一次收集候选，交给 `LocateParty`。两塔原先各自的 `BossTowerCapture` / `IllusionTowerCapture` 已删除，候选收集只剩一处。
- 通道签名改为接收 `DungeonPlace.InBossTower` / `InIllusionTower` / `DungeonPartyPlace.*`，体内不再有 `bossTowers.Find(session)` 之类的路由查询。保留的 `BossTowerExact` / `IllusionTowerExact` 是 RQ-005 允许的精确会话校验。
- 0x000F 例外（`DungeonSelectionCommandPhases.cs:191-200`）：只有定位为 Boss 塔时才进 Boss 通道。`SelectionPrepareBossAsync` 返回 `released.Changed`（`GameDungeonSelectionCommands.cs:139`）：
  - 已释放：同一方法直接调用 `EnterOrdinarySelectionAsync`，不重新定位；
  - 未释放或校验失败：结束。

  与基准 `return !released.Changed` 的语义等价。`EnterSelectionBossHandOverTests` 与表征 `Enter_selection_continues_into_the_ordinary_selection_once_the_boss_run_is_released` 覆盖这两条路径。
- `Locate`（`Dungeons/DungeonLocator.cs:48-65`）按 Spec“位置”定义实现：先查登记，只问登记的那座塔，再核对 RunId。

**达成。**

### SC-3 / AC-002：陈旧帧在各位置丢弃

- `InRunCommandStaleFrameTests`：6 类请求（含物件破坏）× 5 个位置。
- `ExitAndSelectionStaleFrameTests`：4 个命令 × 5 个位置。
- `PartyCommandStaleFrameTests`：组队变更 × 两塔，0x002A × 城镇/普通/两塔。
- TASK-001 的 `.Stale.cs` 继续覆盖“保持丢弃”的格。

断言为“无塔动作、无工作单元、无回包、世界快照不变”。全部通过。**达成。**

### SC-4 / AC-004：新鲜帧行为不变

- TASK-001 新增的 3 个表征测试文件，自 `68c34f8e` 至 HEAD 零改动（`git log 68c34f8e..b6c17eb9` 对这 3 个文件无提交）。
- 只有夹具 `DungeonOwnerRoutingWorld.cs` 在 TASK-002 中有改动：把本地 `Registrations` 换成等价的共享 `DerivedDungeonParticipation`，塔替身改为实现 `ICharacterTowerSeats`。登记推导规则逐字相同，不改断言。
- 表征断言包括塔动作、工作单元声明名（如 `pick-up-boss-tower-item`、`selection-release-boss-tower`）、回包与收件人，改动后全部通过。

抽查四个命令，与基准逐格对照：

- **0x002B**：基准 Boss 分支先查归属、后判陈旧，幻影分支先判陈旧，普通路径只靠这两处顺带。新代码三条通道的体完全搬移，工作单元名与回包未变。
- **0x0027**：Boss 塔遇物件破坏仍直接 return，不回包；幻影塔遇物件破坏，因 `case InIllusionTower when ObjectCode is null` 落入 default，进普通流程。与基准一致。
- **0x0029**：基准只在 `bossTower.Find` 非空时回拒绝帧，其余走普通。新代码 Boss 塔回拒绝帧，其余（含幻影塔）走 `ReviveOrdinaryAsync`。一致，R4 证明有测试守住。
- **0x000E**：幻影塔失败时发 `RemovePartyMemberRefusal`，Boss 塔失败不发，这一差异原样保留在 `HandleMutationAsync` 的 switch 中（`Stages.cs:723-735`）。

**达成。**

### SC-5：已知窗口、整队归属与 0x000F

- 会话代次窗口：单人命令 22 格，组队命令 16 格，表征测试均通过。
- 残留普通登记：`An_ordinary_registration_without_a_findable_run_is_answered_by_the_ordinary_flow`。
- 整队归属：`.Party.cs` 覆盖“调用者在城镇、队员在塔”与 Boss 优先。
- 0x000F 释放后续选图：见 SC-2。

**达成。**

### 授权变化格

逐一对照“陈旧帧行为变化清单”：

- 0x000F 在普通副本、幻影塔、城镇改为丢弃：基准只有 Boss 塔分支判陈旧，现在入口统一判。
- 0x0029：基准从不判陈旧，现在全部位置丢弃，Boss 塔不再回拒绝帧。
- 0x0027 物件破坏：基准中 Boss 分支不判陈旧，直接忽略；`KillIllusionAsync` 只在 `ObjectCode is null` 时调用，所以物件帧不经陈旧判定，在普通、幻影、城镇进普通流程。现在全部丢弃。
- 无会话时，0x002D、0x0048、0x0084、0x000F、0x0027、0x0028、0x0029 改为丢弃。

其余格的基准本来就由塔分支顺带或入口判陈旧丢弃。没有发现清单以外的行为变化。

需要说明的一处：新鲜帧的定位从“依次查塔 `Find`”收窄为“登记与塔一致”。两者只在“塔认这个会话、登记却不指向该塔该局”时不同。核对结果：

- Boss 塔：登记与释放都与塔成员变更处于同一 `Execute`／事务中（`BossTowerDomainService.cs:114, 251, 399`）。
- 幻影塔：同上（`IllusionTowerDomainService.cs:116, 329`）。
- `TryClaim` 拒绝跨模式覆盖。
- 执行器串行。

因此生产中不存在这种状态，与 Constraints 的“对应服务认可才指向该副本”一致。

### SC-6 / RQ-001：中性陈旧定义

- `Game/GameRequestFreshness.cs:12` 的判定式与被删除的 `TowerProtocol.cs` 逐字一致。
- `git grep TowerProtocol -- Server` 无结果。protocol 目录中的命中是上游 `DeathTowerProtocolSelfTest` 的文件名，无关。
- 0x009F 与 0x0010 已改用新定义。
- `EffectOperationLedger` 中的 `SequenceIsFresh` 按 Scope Out 保留。

**达成。**

### RQ-007 / AC-005：结构守护

`DungeonOwnerRoutingSourceTests` 扫描 `Dfo.Server.Protocol` 的全部生产源码，扫描前先清掉注释，并按成员固定命中次数。四类规则如下：

- 陈旧判定；
- 塔 `Find` / `IsInTower`；
- 登记 `Find`；
- `Locate` / `LocateParty` / `LocatePartyOf`。

路由方法另有“判陈旧先于会话或定位”的顺序检查。测试附 6 个反例用例，以及“注释与其他查询不算路由”的负例。

逐项核对了白名单，没有混入 13 个命令的路由用法：

- 精确会话校验；
- 投影收件人与重读；
- 整队精确校验；
- 离开投影（3 处既有重复）；
- 入场精确性校验；
- 计时器回城；
- 0x009F；
- 生物通知、任务外观、整理与疲劳恢复。

后两类是其他命令自己的“是否在塔”规则，不选择副本通道。

R2 证明，行为测试拒绝不了的顺序回退会被守护拒绝。TASK-006 记录的 7 项生产文件变异，与本审查的 R1～R4，共同说明守护和测试能拒绝回退。**达成。**

### RQ-008 / AC-006：文档

- `Document/ProtocolCommands.md:41` 新增“副本归属分发与陈旧帧”一节，与实现逐点一致：
  - 入口判陈旧，组队类的 `DemandCurrent` 在前；
  - 定位规则与三类“普通或不在副本”；
  - 整队位置；
  - 单通道与保留的差异；
  - 0x000F 例外：释放与登记撤销在同一写入中完成，已对照 `ReleaseForSelectionAsync` 核实；
  - 0x009F 与 0x0010 的位置；
  - 守护的四条规则。
- `LordTower-Design-20261019.md:63` 只加了链接，没有复写规则。
- AC-006 点名的三处顺序差异注释已删除：
  - `GameGetItemCommandHandler` 的 “Boss lookup precedes stale…”；
  - `MonsterTowerCommandPhases` 的 “The stale gate precedes…”；
  - `GameDungeonSelectionCommands` 的 “Selection checks for an attached run before consuming a stale request…”。
- 生产代码中残留的 “stale” 注释都是新的“通道已由调用方判陈旧”说明，与实现一致。
- protocol 知识库各命令页没有关于陈旧顺序的旧结论。

**达成。**

### AC-007 / SC-8（测试部分）

完整测试已通过，见“独立验证”。PR、CI 与 Codex 审核部分不在本审查范围。

## 发现

### 阻断

无。

### 非阻断

1. **测试夹具的登记由塔状态推出，无法发现“塔有座位、登记缺失”这类回退。**
   - 现状：`ProtocolAlgorithmFixture`（`Server/tests/Support/ProtocolAlgorithmFixture.cs:33-42`）在测试未提供登记时，默认注入 `DerivedDungeonParticipation`。各驱动（`TowerAlgorithmDrivers`、`DungeonSelectionTestDriver`、`CharacterDeathHandlerTests`、`LeaveDungeonTests`）也用它。它按塔的 `Find` 推出登记，与塔状态在构造上一致。
   - 后果：经这些夹具的既有塔测试，无法发现“生产登记与塔不一致”或“绕过定位直接查塔”的回退。
   - 为什么不阻断：
     - 绕过定位的回退由结构守护与使用 `ICharacterTowerSeats` 的代次窗口表征拒绝；
     - 登记与塔的一致性是领域不变量，由塔服务在同一事务内登记与释放保证；
     - Networking 集成测试用真实 DI 时必须显式 `TryClaim`，说明真实登记确实在被读取；
     - Application 层经 DI 解析的 handler 也走真实登记。
   - 建议：后续为两塔各补一条经真实 `DungeonParticipationDomainService` 的端到端路由用例。
2. **几个测试驱动在测试里复刻了路由。**
   - 具体位置：
     - `BossTowerPublicHandlerTests.LoadingFlow.CompleteLoadingAsync` 在测试内调 `Locate` 再进通道；
     - `TowerAlgorithmDrivers` 的 `BossTowerPartyDriver` / `IllusionTowerPartyDriver.CoordinateAsync` 复刻组队路由；
     - 两个 `LeaveAndReturnAsync` 仍保留旧形状“判陈旧 + 塔 `Find`”，这是既有的测试专用实现，本次只改名。
   - 它们不在守护扫描范围内，文档与守护注释已说明。生产路由由表征测试经公开 `HandleAsync` 覆盖，所以不掩盖生产问题。
   - 建议：后续整理时，改为经公开入口驱动。
3. **AC-004 中“普通”一列以城镇代表。** 表征的 `FreshPlaces` 没有 `RoutingPlace.Ordinary`，普通副本内的结果依赖各命令既有的 handler 测试。陈旧帧矩阵含 Ordinary。普通通道的代码是整体搬移，审查逐行核对无改动，风险低。
4. **定位能力放在协议层静态类型。** “哪个副本拥有这个会话”由登记加塔的精确会话共同判定，带有领域判断色彩。当前只有协议层路由使用，又与 `BossTowerActionProjection`、`DungeonPartyRecipients` 等同层静态辅助一致，所以可接受。若以后其他调用方需要同一判定，例如 `GameSortItemCommand`、`RecoverStaminaSlice` 的“是否在副本”，应考虑收进领域查询，避免出现第二种口径。
5. **`PartyCommandHandlerTests` 的两处断言改动**（`IPartyService` 只读 `query` 次数 2→1、3→2）是合并候选收集的直接结果。写操作的种类与顺序、拒绝帧、命令序列都未变，属于契约 Scope In“两塔的候选收集合并为一处”的授权范围。`Handled` 字段删除导致的 `Assert.True(x.Handled)` → `Assert.NotNull(x)` 同样等价：驱动用 null 表示原先的 `Handled == false`。
6. **证据存放位置与 Spec 不一致。** Spec 的 Success Evidence 写“完整测试输出存放在 `verification/`”，实际在 `logs/`。不影响证据效力，完成验证时注明即可。

## 架构漂移结论

**通过：附建议。**

- **架构对应**：
  - 陈旧判定是协议层的入站帧属性，中性定义 `GameRequestFreshness` 放在协议层，合适。
  - 定位复用既有的领域 owner `IDungeonParticipationDomainService` 与两塔服务的只读查询，没有新增状态、快照或同步义务。
  - 路由与各通道仍在原 CMD handler 内，按分部文件拆分（`*.Towers.cs`），属于同一 owner 内的组织调整。
  - 处理器新增注入的依赖符合 `*DomainService` 后缀，`CommandHandlerArchitectureTests` 通过。
- **阻断形状**：无。
  - 没有第二真源：登记仍只由塔服务与普通副本服务写入。
  - 没有另写一套同义判定：塔归属的旧算法“依次 `Find`”已删除，只剩定位器一处。
  - 规则没有绕过规则服务，协议知识没有进入领域，共享机制没有增加专项分支。
- **非阻断**：上文第 4 项，定位判定的未来归属。
- **周边检查**：
  - 协议层所有 `bossTowers` / `illusionTowers` 的 `Find`、`IsInTower` 调用都已由守护枚举，并逐项核对理由。
  - 离开投影在 3 个文件中的重复是基准既有，本次未加剧。

## 结论

`approved`：在本审查范围内，没有未解决的阻断问题。完成之前仍需：

- 按 SC-7 在 #203、#187 发表范围说明；
- 按 SC-8 创建 PR，等当前提交 CI 通过且 Codex 审核通过；
- 由用户做 A21 实机确认。
