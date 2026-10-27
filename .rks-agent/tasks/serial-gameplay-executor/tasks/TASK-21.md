# TASK-21 — T-005a：删除 Protocol 层的门、`LeaseSet` 与重试循环，落实 VC-7

基准提交 `b1959f3e`（TASK-20／M-1 通过并提交；`git diff 0d8d4fad b1959f3e -- Server` 为空，被测代码就是 TASK-19 结束时的代码）。范围取自 `plan.md` T-005 行（第 96 行）与共享决定、过渡期规则 2，`spec.md` RQ-060、附录 F「删除」栏、附录 G VC-7、AC-017。

T-005 拆成两个单元，本单元是第一个；第二个（TASK-22）只写边界提纲，见文末。拆分的理由与顺序见 Detailed Plan §0，需要主控方确认（待裁定第 1 条）。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **删除 `Dfo.Server.Protocol` 中的全部门与取门**（RQ-060；附录 F「删除」栏中属于 Protocol 的部分；Plan T-005 行）：
     - Protocol 自己定义的门，连同类型、注册与断言挂点一起删除：`TownAreaProjectionGate`、`DungeonProjectionGate`、`BossTowerProjectionGate`、`BossTowerEntryGate`、`IllusionTowerProjectionGate`、`IllusionTowerEntryGate`、`AchievementTitleProjectionGate`（64 路信号量）、`WeaknessProjectionDelivery` 的 `deliveryGate`；
     - Protocol 对 Application 门的全部使用：`SelectedSessionTransitionGate`（`AcquireAsync`、`BorrowOrAcquire*Async`、`AcquireManyExclusiveAsync`）与 `OnlineUserExecutionGate`。删除构造参数、字段与取门语句；门的类型本身留在 Application，由 TASK-22 删除；
     - 私有的 `LeaseSet`、`TransitionLeaseSet`、`ProjectionLeaseSet`（7 处），以及只为取门存在的包装（`PartyTransitionExecution.RunAsync`、`WeddingProposalExecution.RunAsync`）。
  2. **删除 Protocol 中全部「锁内复查、重试」循环**（附录 F「删除」栏「全部『锁内复查、重试』循环」），共 7 个，见 §2.7。
  3. **落实 VC-7**（附录 G）：放弃副本、离队、换队长（以及踢出队员，待裁定第 4 条）在重试用尽时「不回包、只记日志或回拒绝」的分支随循环消失。证据见 §7。
  4. **删干净的判据**（AC-017 的 Protocol 一半；RQ-072 的写法）：新增 `LockRemovalSourceTests`，含源码名称扫描与 `SemaphoreSlim` 白名单两支，每支配反例（§5）。TASK-22 与 T-006 在同一个判据上扩大名单。
  5. **门断言随门删除**（Plan 过渡期规则 2）：删掉的门，它的断言挂点与反例一起删除；`GameplayGateAssertion` 类本身保留，因为队伍的两把投递门要到 T-006 才删除（待裁定第 2 条）。RQ-061 写断言、泄漏流断言与取证源不动。
  6. **测试侧**：本单元的生产改动迫使改动的测试，按 §6 的规则逐条处置并记录；`ProtocolGateAssertionTests` 中针对已删门的 10 个用例删除；本单元使其「空转」的测门用例列名单交 TASK-22（§6.4）。
  7. **一次正式全量**：带取证汇，反例类与对照以外的违规仍为 0；表征测试与金样本不变；变异核对（§8）。
- Owned boundary：
  - **生产代码**：只限 `Server/src/Dfo.Server.Protocol/**`。基准上命中的 63 个文件见 §2 与 `logs/task-21-sites.txt`（执行开始时用 §2.0 的检索式重新生成）。`ProtocolModule.cs` 只删门的注册行（`:214`、`:483`、`:661`、`:908`、`:909`、`:927`、`:928`），命令注册中的 `UserAccessMode` 不动（TASK-22）。
  - **测试**：
    - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/LockRemovalSourceTests.cs`；
    - `Server/tests/Dfo.Server.Protocol.Tests/Execution/ProtocolGateAssertionTests.cs`：删除 10 个用例，类注释里的记录数改为 4；
    - 被本单元生产改动迫使改动的测试文件（构造参数、注册、已删类型），以及 §7、§2.7 新增的少量用例。逐文件列入 `verification/t005-test-dispositions.md`。
  - **文档与证据**：
    - 新建 `verification/t005-test-dispositions.md`（TASK-21 一节；TASK-22 续写）；
    - `verification/follow-ups.md`；
    - `verification/logs/task-21-*.txt`；一次性脚本放 scratchpad，全文附在对应日志末尾。
  - **不改**：
    - `Dfo.Server.Application`、`Dfo.Server.Abstractions`、`Dfo.Server.Networking` 及其余生产项目（包括 `GameplayAssertions.cs`、管线、`SelectedSessionSafety`、门的类型定义）；
    - 队伍的两把投递门（`PartyProjectionStreamRegistry.EnterParticipantsAsync` 的按角色信号量、`PartyDeliveryStream.Gate`）、`PartyExactNotificationDelivery.PublishWithRetryAsync`、`NotificationRetryQueue`／`NotificationRetryDrainService`（都归 T-006）；
    - 命令注册中的访问方式、`IMultiAccountCommand`、`IMultiSelectedSessionCommand`（TASK-22）；
    - `spec.md`、`plan.md`、`AGENTS.md`、`Docs/`；表征测试与金样本；`task-07-conflicts.md`（只读引用）。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-21-build-test.txt`。
     - **总数**：TASK-19 为 54 ＋ 80 ＋ 1704 ＋ 477 ＋ 970 ＋ 175 ＋ 4573 ＋ 191 ＝ 8224（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host；TASK-20 未改 `Server`）。本单元 ＝ 8224 ＋ N － D，逐项目写加法；N、D 逐条列名（预期见 §6.5）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）；跳过 20；挂起转储 0。
     - **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改（`git status` 为证）。
     - **日志信号**：与 TASK-19 Checks 2 同口径、同脚本（`analyze.py`）。应为 0 的各项仍为 0；`GameplayGateWait`、`GameplayLeakedFlow` 各 1，只在 `HostOutputViolationTests` 的显示名里；`Gameplay bus:… failed` 8、`inbound:… failed` 3、`gm:… failed` 2 逐条与 TASK-19 对照，不同的逐条说明。
     - 耗时与内存只作信息（RQ-091 用户修订：单条指令 ≤ 2 s 即可接受，性能不作停止条件）。F-69：与 TASK-19 那次全量逐条对比耗时，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-21-violations.txt`）：`violations.py` 的命令与 TASK-19 Checks 3 相同，只把 `ProtocolGateAssertionTests` 的期望改为 4。预期先写进日志开头再跑（§8.1）：合计 61；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  4. **判据输出**（`logs/task-21-lock-scan.txt`）：两支判据各自扫描的文件数、每条名称规则的命中（应为 0）、`SemaphoreSlim` 白名单逐项（文件、理由、去向）及其实际命中；判据用例与全部反例的通过记录。
  5. **取门点清单**（`logs/task-21-sites.txt`）：§2.0 检索式在基准上的全部命中，逐行写「文件:行 → 处置规则（§3 的 R1～R8）→ 保留下来的非锁语句」。执行结束时同一检索式在 Protocol 中的命中只剩 §2.0 列出的例外。
  6. **循环表**（`logs/task-21-loops.txt`）：§2.7 的 7 个循环，每个 `continue`／`reroute` 触发条件逐条判为「串行下恒不成立」或「确定性条件」，写明依据（两次读取之间有哪些语句、它们读不读写这份状态）；确定性条件改写后的结果与原循环用尽时的结果逐字相同。
  7. **升级拒绝核对**（`logs/task-21-upgrade.txt`）：§2.0 中每处 `BorrowOrAcquireExclusiveAsync`，判定它能否在某条命令的 45 号行为（共享租约帧）之内被调用。今天那种调用会抛「共享租约不能升级」；删除后不再抛，这是行为差异。预期 0 处；不为 0 按停止条件 2 处理。
  8. **测试改动核对**（`logs/task-21-diff-check.txt`）：`diffcheck7.py b1959f3e --lines`（scratchpad `t07/diffcheck7.py`），证明已有测试的方法集合只少了 D 中列出的方法、只多了 N 中列出的方法，断言行多重集合的变化逐处对应 `t005-test-dispositions.md` 的一行。
  9. **新增测试 ×10**（`logs/task-21-new-x10.txt`）：本单元新增的全部用例隔离连续 10 次，全部通过。
  10. **变异核对**（`logs/task-21-mutations.txt`；程序与 TASK-19 Checks 7 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：S1～S8 见 §8.2，每一项都要有指定的用例变红。
  11. **生产改动边界**（`logs/task-21-boundary.txt`）：`git diff --stat b1959f3e -- Server/src` 只含 `Dfo.Server.Protocol/` 下的文件；逐文件对到 `task-21-sites.txt` 或 `task-21-loops.txt` 的行。
  12. **隔离复跑**（`logs/task-21-isolated.txt`）：Application、Protocol 各一次，都带取证目录，记录各自的正向对照与反例类记录数（Application 49；Protocol 4 ＋ 2 ＋ 1 ＝ 7）。
  13. **空转名单**（`logs/task-21-vacuous.txt`）：§6.4。
  14. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。
- Dependencies：
  - TASK-19（`0d8d4fad`）：T-004 收口（`verification/t004-closure.md` §8：T-004 完成；门断言与 RQ-061 判据在 TASK-18、TASK-19 两次正式全量中零违规），即 T-005 进入条件中属于 T-004 的一半；
  - TASK-20（`b1959f3e`）：M-1 通过（`verification/measurement/m1-summary.md` §1），即进入条件的另一半；
  - TASK-17、TASK-18 的门断言、取证汇与 `violations.py`；`task-07-conflicts.md` §14～§15 的处置口径。
- Result review：required。
  - 本单元一次改 63 个生产文件、删 7 个重试循环，涉及选角、组队、进出副本、两塔、祭坛、比武、婚姻、城镇投影等几乎所有多人路径。
  - 主要风险不是「删多了门」，而是**把非锁职责当成锁的一部分删掉**：取门前后的当前性复查（`IsInWorld`、`StandingOf`、`DemandCurrent`）、参与者集合、关连接排在租约释放之后的次序、循环里的确定性拒绝。删错一处，全量多半照样是绿的：现有用例大多按「顺利路径」写，而且门在串行下本来就不被争用。
  - 循环的「串行下恒不成立」判定、`continue` 条件的分类、VC-7 的解读与「空转」名单，都是判断性产出，变异只能证明判据有区分力，证明不了每一处改写都对。
  - TASK-22 在本单元的结果上删除门的类型与管线；本单元若漏删或错删，会被带进后续单元。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 某处取门之后的复查既不属于「串行下恒不成立」，也不属于「确定性条件」，或删除取门会改变某个可达的结果（§3 R4 无法归类）；
    2. Required check 7 找到在命令的 45 号行为之内调用的 `BorrowOrAcquireExclusiveAsync`；
    3. 需要改 Owned boundary 以外的生产文件（包括 Application、Abstractions），或需要改 `spec.md`、`plan.md`；
    4. 正式全量出现 §6 规则覆盖不了的新失败；取证出现反例类与对照以外的记录；
    5. 表征测试或金样本需要改动；
    6. 某个测门或测交错用例的保护意图既找不到承接者，也说不出它为什么已被规格消除；
    7. 判据在扫描中找到本单元范围以外、附录 F 没有列出的并发原语（按附录 F 末句应补入附录 F，属于改 Spec）。
  - 变异核对期间允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 拆分与顺序

**为什么拆**：T-005 在基准上要改的生产文件约 85 个，分两层：

| 层 | 内容 | 规模（基准 `b1959f3e`） |
|---|---|---|
| Protocol | 取门点、Protocol 自有的门、`LeaseSet`、重试循环 | 63 个文件；`SelectedSessionTransitionGate` 在 Protocol 中 79 处引用、约 90 个取门点；7 个循环 |
| Application 与命令契约 | 管线 45／50／55／57 的取锁部分、门的类型、`TransactionalExecutionGate` 的 5 个领域使用者、组门、并发槽、效果账本信号量、Joust 信号量、访问方式与 `IMultiAccountCommand` | 约 20 个 Application 文件；`ProtocolModule.cs` 中 118 处 `UserAccessMode`；15 个命令记录 |

两层的风险性质不同：Protocol 层的风险是逐处改写中错删非锁职责（判断量大、机械量也大）；Application 层改的是命令契约（`AddCommandHandler` 的签名、`CommandExecutionContext` 的形状、管线层数），所有命令注册都受影响。放在一个单元里，结果审查要同时核对两类问题，证据也会混在一起（`RefactorPreferences` §3.2）。

**为什么 Protocol 在前**：项目引用是 Protocol → Application。门的类型定义在 Application，使用者大多在 Protocol。先清掉使用者，TASK-22 删类型时就只剩 Application 内部的引用；反过来先删类型，会让 Protocol 无法编译，两个单元不可能各自是绿的。Protocol 的取门点删除后，管线 45 号行为在没有借用帧时自己取共享租约，执行器上没有任何争用，所以本单元结束时系统照常运行。

**执行顺序**：
1. 用 §2.0 的检索式生成取门点清单（Required check 5 的前半）与升级拒绝核对（Required check 7）。
2. 先写判据与反例（§5），看它在基准上红（名称命中、白名单外的 `SemaphoreSlim`）。
3. 按簇改生产代码（§2.1～§2.8），每簇改完构建一次，跑该簇的测试项目：
   1. 单账号「读 → 发送」类（`BorrowOrAcquireExclusiveAsync` 与单个 `AcquireAsync`，约 30 个文件）；
   2. 每日配额刷新、到期物品、疲劳、配额投影等后台投影；
   3. 观察者、回选角、选角；
   4. 称号、虚弱；
   5. 队伍命令与婚姻（包装函数）；
   6. 副本进出、清算、死亡、复活、祭坛、比武、房间机制（`DungeonProjectionGate`、`AcquireManyExclusiveAsync`、`TransitionLeaseSet`）；
   7. 城镇投影（`TownAreaProjectionGate`、`ProjectionLeaseSet`）；
   8. 两塔（投影、入场门、协调器、`LeaseSet`、两个循环）。
4. 循环（§2.7）与 VC-7 证据（§7）。
5. 测试侧处置（§6），构建、正式全量与取证（Required checks 1～3），隔离复跑，新增用例 ×10。
6. 变异核对（§8.2）。
7. 清单、循环表、边界、diff 核对与 `follow-ups.md`。

### 1. 范围的几条边界

- **为什么删门不改行为**：
  - 门断言在执行器外取门即抛，在执行器上需要等待也抛（TASK-17）。TASK-18、TASK-19 两次正式全量中，这两类违规在反例类以外都是 0（`t004-closure.md` §5）。
  - 生产中取门的只有作业，执行器同一时刻只执行一个作业（RQ-001）。所以执行器上每一次取门都立即成功，门不再决定任何先后。
  - 删掉一次「立即成功的取门」，只剩下三种可能的差异，§3 逐一处理：
    - 取门顺带做的取消检查（R6）；
    - 借用帧拒绝「共享升级为独占」时抛出的异常（Required check 7）；
    - 取门前后两次读取的比较（R4）。
- **投递归 T-006**（待裁定第 2 条）：队伍的按角色信号量与 `PartyDeliveryStream.Gate` 守的是队伍投影的代际与 revision 水位线，而水位线只在送达后推进（附录 E DR-5、DR-17）。它们与水位线一起在 T-006 改写，本单元不碰。同理不碰 `PublishWithRetryAsync` 与通知重试队列。
- **称号门与虚弱门归本单元**（待裁定第 3 条）：两者都不依赖送达结果推进任何状态。过渡期作业上的直发是被 await 的（2 秒期限），所以一个作业结束时，它的发送已经完成；下一个作业的投影自然排在后面。DR-16「称号投影不插进进入序列」在过渡期由此成立，T-006 再按 RQ-032 在发件箱上复核。
- **管线不动**：45／50／55／57 号行为、`SelectedSessionSafety` 的借用、门的类型都留给 TASK-22。本单元结束时，Application 的门只剩管线与 `SelectedSessionSafety` 两类使用者。
- **注释**：按 AGENTS.md「记什么，不记什么」，只删除或改写描述门、租约、借用帧、锁序的注释；不新增「此处曾有门」一类的过程注释。门删除后仍有意义的不变量（例如「关连接排在本作业的发送之后」）保留并改写成不提门的说法。

### 2. 生产侧删除清单

#### 2.0 检索式与例外

清单由下面的检索式在基准上生成，写入 `logs/task-21-sites.txt`：

```
grep -rnE --include='*.cs' \
  "SelectedSessionTransitionGate|OnlineUserExecutionGate|UserExecutionGate<|TownAreaProjectionGate|DungeonProjectionGate|BossTower(Projection|Entry)Gate|IllusionTower(Projection|Entry)Gate|AchievementTitleProjectionGate|BorrowOrAcquire(Shared|Exclusive)Async|AcquireManyExclusiveAsync|LeaseSet\b|deliveryGate|UserAccessMode|PartyTransitionExecution|WeddingProposalExecution|RetryLimit|reroute" \
  Server/src/Dfo.Server.Protocol
```

执行结束时，同一检索式在 Protocol 中只允许剩下：
- `ProtocolModule.cs` 的命令注册中的 `UserAccessMode`（TASK-22）；
- `PartyTransitionExecution.RunPartyAsync`，如果它在删掉取门后仍承担收件人捕获（§2.8）；
- 其余命中为 0。

#### 2.1 `SelectedSessionTransitionGate` 在 Protocol 中的使用

- **定义**：`Application/OnlineUsers/SelectedSessionTransitionGate.cs:8`（类型由 TASK-22 删除）。注册在 `CommandDispatchingExtensions.cs:27`。
- **今天保护什么**：同一账号的会话切换、拆除与「读 → 发送」不交错；`AcquireManyExclusiveAsync` 在流上装好借用帧，让后续命令的 45 号行为借用而不是再取。
- **删除后由什么保证**：执行器单消费者（RQ-001）；同一作业内的「读 → 发送」之间不会插入别的作业；断线、顶替与拆除都作为生命周期 CMD 执行（RQ-012），排在当前作业之后。
- **Protocol 中的取门点**（基准行号，粗检索；以 `task-21-sites.txt` 为准）：

| 形态 | 文件 |
|---|---|
| `BorrowOrAcquireExclusiveAsync`（单账号「读 → 发送」） | `ReplayStateProjectionDelivery.cs:36`、`Rental/RentalSlice.cs:115`／`:147`、`Joust/JoustSlice.cs:161`／`:184`、`Teleport/TeleportSlice.cs:94`、`StateSynchronization/ExpiredItemsProjection.cs:26`、`CollectionBox/CollectionBoxSlice.cs:106` |
| 单账号 `AcquireAsync`（handler 与投影） | `AccountRoster/OtherCharacterDetailSlice.cs:186`、`Fatigue/FatigueProjection.cs:62`、`LotteryOpening/LotteryOpeningSlice.cs:88`、`Mailbox/MailboxDelivery.cs:60`、`QuestClearCoupon/EntryAllowanceHandler.cs:55`、`QuestClearCoupon/FatiguePotionHandler.cs:49`、`RecoverStamina/RecoverStaminaSlice.cs:144`、`RepairItem/RepairItemHandler.cs:28`、`Creatures/CreatureRuntimeDelivery.cs:167`／`:181`、`VendingMachine/VendingMachineSlice.cs:88`、`StateSynchronization/ItemUsageProjection.cs:113`、`StateSynchronization/UsageQuotaProjection.cs:115`、`StateSynchronization/PremiumServiceUsageReport.cs:34`、`TownProjection/TownProjectionSlice.cs:254`／`:351`、`Weakness/WeaknessRecoveryService.cs:79` |
| 每日配额刷新（后台，逐会话取门） | `Altar/AltarQuotaDailyRefreshService.cs:34`、`StateSynchronization/DimensionQuotaDailyRefreshService.cs:34`、`GoldSilverHornQuotaDailyRefreshService.cs:34`、`PremiumDailyRefreshService.cs:59`、`ReviveCoinQuotaDailyRefreshService.cs:34`、`Tournament/HerasQuotaDailyRefreshService.cs:34`、`Tournament/TournamentQuotaDailyRefreshService.cs:34` |
| 生命周期与选角 | `OnlineUserConnectionObserver.cs:98`／`:126`、`ReturnToSelection/ReturnToSelectionHandler.cs:27`、`SelectCharacter/SelectCharacterHandler.cs:103` |
| 包装函数（§2.8） | `Party/PartyCommandSlice.cs:405`～`:473`（`RunAsync`、`RunPartyAsync`，调用点 `:629`、`:693`、`:771`、`:872`、`:995`、`:1081`（`RunAsync`）、`:1123`）；`MarriageProposal/MarriageProposalSlice.cs:258`～`:272`（`WeddingProposalExecution.RunAsync`，调用点在 `MarriageProposalSlice`、`MarriageWeddingSlice`、`MarriageChurchSlice`、`MarriageEngagementSlice`、`MarriageGuestbookSlice`、`MarriageLovePointSlice`、`UseStackable/SharedEffectSlice.cs:59`） |
| 多账号（与副本门同用） | `DungeonClear/DungeonClearSlice.cs:250`、`DungeonClear/LeaveDungeonSlice.cs:205`（`AcquireManyExclusiveAsync`）、`DungeonSelection/DungeonSelectionSlice.cs:305`～`:325`、`:614`、`DungeonEntry/DungeonEntrySlice.cs`、`DungeonEntry/DungeonEntryProjectionDelivery.cs:158`／`:486`～`:499`、`CharacterDeath/*`、`Altar/AltarHandlers.cs`、`Altar/AltarScheduler.cs`、`Tournament/TournamentSlice.cs`、`Tournament/TournamentRewardSweepService.cs`、`Dungeons/DungeonLoadingProjectionDelivery.cs`、两塔四个文件（§2.6） |

`DungeonSelectionSlice.cs:572`～`:575` 的可空参数与 `?? new()` 默认值一并删除。

#### 2.2 `OnlineUserExecutionGate` 的两处直接使用

- `OnlineUserConnectionObserver.cs:18`／`:101`、`ReturnToSelection/ReturnToSelectionHandler.cs:13`／`:29`。
- **今天保护什么**：同一在线用户的命令与断线清理互斥，并与过渡门保持「先过渡门、后在线门」的锁序。
- **删除后**：断线清理是生命周期 CMD（RQ-012），与该用户的入站 CMD 在同一个队列里先后执行；锁序问题随门消失。管线 50 号行为对命令的取门留给 TASK-22。

#### 2.3 投影门与入场门（Protocol 自有类型）

| 门 | 定义 | 注册 | 使用者 | 今天保护什么 |
|---|---|---|---|---|
| `TownAreaProjectionGate` | `TownProjection/TownAreaProjectionDelivery.cs:17` | `ProtocolModule.cs:483` | 同文件 `:55`、`:66`、`:78`、`:722`（`ProjectionLeaseSet`） | 同一城镇区域的投影顺序 |
| `DungeonProjectionGate` | `Dungeons/DungeonLoadingProjectionDelivery.cs:13` | `ProtocolModule.cs:214` | 约 13 个文件（`research/04` 门表；以 `task-21-sites.txt` 为准） | 同一副本局的投影顺序 |
| `BossTowerProjectionGate`／`BossTowerEntryGate` | `BossTowerProjectionDelivery.cs:46`／`:47` | `ProtocolModule.cs:908`／`:909` | `BossTowerProjectionDelivery.cs` 约 14 处、`BossTowerPartyCoordination.cs:86` | 塔局投影；同一角色的入场 |
| `IllusionTowerProjectionGate`／`IllusionTowerEntryGate` | `IllusionTowerProjectionDelivery.cs:40`／`:41` | `ProtocolModule.cs:927`／`:928` | `IllusionTowerProjectionDelivery.cs` 约 12 处、`IllusionTowerPartyCoordination.cs:85` | 同上 |

删除后由执行器单消费者保证：同一区域、同一副本局、同一塔局的投影都来自作业，作业不交错；同一角色的两次入场是两条先后执行的 CMD，后一条读到前一条提交后的状态。

#### 2.4 `AchievementTitleProjectionGate`

- 定义 `TitleBook/TitleBookSlice.cs:92`（64 路信号量，`:94`～`:99`），注册 `ProtocolModule.cs:661`。
- 使用者：`Achievements/AchievementSlice.cs:63`／`:69`、`:84`／`:88`（订阅者）；`TitleBook/TitleBookSlice.cs:109`／`:120`、`:184`／`:189`（订阅者）；`TitleBook/TitleAppearanceSlice.cs:50`／`:57`；`SelectCharacter/SelectCharacterHandler.cs:73`／`:143`～`:144`；`MoveItemSpace/MoveItemSpaceSlice.cs:849`／`:858`～`:859`。
- 今天保护：按角色给称号投影与请求 ACK 排序；选角持门直到进入批次送达（DR-16）。
- 删除后：称号事件 CMD 只能排在选角 CMD 之后执行；过渡期选角作业的发送在作业内 await 完成，所以投影不会插进进入序列（§1）。

#### 2.5 虚弱投递门

- `Weakness/WeaknessRecoveryService.cs:33`（字段）、`:37`（`GameplayGateAssertion.Enter`）、`:45` 的 `finally Release`。
- 今天保护：同一份虚弱投影的「读 → 发送」不交错。删除后由单消费者保证。`WeaknessProjectionDelivery` 类型保留。
- 同文件 `:51`／`:79` 的过渡门使用按 §2.1 删除（F-19 的 Weakness 一项）。

#### 2.6 `LeaseSet` 一族

| 类 | 位置 | 使用 |
|---|---|---|
| `LeaseSet` | `BossTowerPartyCoordination.cs:436` | `:102` |
| `LeaseSet` | `BossTowerProjectionDelivery.cs:932` | `:93`、`:210`、`:238`、`:265`、`:299`、`:325`、`:378`、`:404`、`:440`、`:460`、`:523`、`:562`、`:585`、`:615` |
| `LeaseSet` | `IllusionTowerProjectionDelivery.cs:874` | `:844` |
| `LeaseSet` | `IllusionTowerPartyCoordination.cs:455` | `:404` |
| `TransitionLeaseSet` | `DungeonEntry/DungeonEntryProjectionDelivery.cs:576` | `:499` |
| `TransitionLeaseSet` | `DungeonSelection/DungeonSelectionSlice.cs:382` | `:325` |
| `ProjectionLeaseSet` | `TownProjection/TownAreaProjectionDelivery.cs:747` | `:725` |

连同「按账号排序后逐个取门、失败时逆序释放」的收集代码一起删除。收集代码里顺带计算的东西（例如参与者账号集合）若在门之外还有读者，按 R5 保留计算本身。

#### 2.7 重试循环（7 个）

| # | 位置 | 请求 | 用尽时今天的结果 |
|---|---|---|---|
| L1 | `Party/PartyCommandSlice.cs:748`（`attempt >= 7` 在 `:792`） | 离队 `LeavePartyHandler` | 以 `CallerNotCurrent` 结束，不发通知，只记日志 |
| L2 | `Party/PartyCommandSlice.cs:833`（`:896`） | 换队长 `ChangePartyLeaderHandler` | 同上 |
| L3 | `Party/PartyCommandSlice.cs:947`（`:1019`） | 踢出队员 `RemovePartyMemberHandler` | 以 `CallerNotCurrent` 结束，回拒绝包 `RemovePartyMemberRefusal` |
| L4 | `DungeonClear/DungeonClearSlice.cs:192`（`:291`） | 放弃副本 `GiveUpGameHandler` | 直接 `return`：不回 `GiveUpGameResponse`、不进城 |
| L5 | `BossTowerPartyCoordination.cs:77`（`RetryLimit = 8`，`:40`） | 上面四个请求在首领塔中的分支 | `Rejected(null, CallerNotCurrent)`（`:244`） |
| L6 | `IllusionTowerPartyCoordination.cs:76`（`RetryLimit = 8`，`:39`） | 同上，幻影塔 | 同上（`:242`） |
| L7 | `OnlineUserConnectionObserver.cs:89`（`while (true)`） | 断线清理 | 无上限：直到绑定稳定 |

逐个触发条件的分类规则见 §3 R4。基准阅读的初判（执行时在 `task-21-loops.txt` 逐条确认）：
- L1～L4 的 `reroute` 只在「两塔协调器先答『不归我管』，取门后再问『有没有涉塔』却答『有』」，或「副本局 ID 在取门前后不同」时成立。两次询问之间只有取门与只读语句，串行下恒不成立。
- L5、L6 的第一处 `continue`（`:91`／`:90`）与第二处的 `SameCapture` 比较的都是取门前后的两次 `Capture`，串行下恒不成立；`ExactSessions` 为假是确定性条件（调用方或成员的会话已经不是当前的），原循环会白转 8 次后返回 `Rejected(null, CallerNotCurrent)`，改写后直接返回同一个结果。
- L7 的 `continue`（`selectedBefore is null && selected is not null`）比较的是取门前后两次查询，串行下恒不成立。
- 业务重试不在本单元：`DungeonCardPresentationCoordinator.cs:243` 的「预留冲突重试 3 次」不是锁内复查，记 F 项交 T-008 判断它在串行下是否还会发生；`ScheduledTaskHostedService` 的尝试次数、事件 delivery 的重试属于业务语义，保留。

#### 2.8 只为取门存在的包装

- `PartyTransitionExecution.RunAsync`：删掉取门后只剩 `return await action()`，删除包装，调用点直接调用动作。
- `PartyTransitionExecution.RunPartyAsync`：删掉取门后仍做 `PartyNotificationRecipientCapture.Capture`（非锁职责），保留为一个不带门参数的函数；名字不改，避免改动扩散到全部调用点（T-008 归并队伍簇时再定）。
- `WeddingProposalExecution.RunAsync`：删掉取门后在 `actor`／`peer` 为空与不为空两种情形下都只是 `await action()`，删除包装，调用点直接调用动作。

### 3. 逐处改写规则

| 规则 | 内容 |
|---|---|
| R1 | 删除取门语句与它的租约变量、`await using` 块；块内语句原位保留，次序不变 |
| R2 | 删除 `LeaseSet` 一族与收集代码（§2.6）；收集过程中计算、门外还在用的值保留 |
| R3 | 删除门的构造参数、字段、可空默认值与 DI 注册；删除 Protocol 自有门的类型声明 |
| R4 | 循环与「取门后复查」：每个比较条件归为两类之一。**(a) 串行下恒不成立**：两次读取之间只有取门与不写这份状态的语句。删除第二次读取、比较与循环，主体执行一次。**(b) 确定性条件**：同一份输入下每次都得到同一个答案（例如 `ExactSessions`）。保留检查，把原来的 `continue` 改成原循环用尽时的结果，逐字相同。归不进两类的，按停止条件 1 处理 |
| R5 | 当前性检查（`IsInWorld`、`StandingOf`、`TryGetInWorld`、`Capture`、`DemandCurrent`）、参与者集合、收件人捕获一律保留，即使它们紧跟在被删的取门之后。它们防的是「手里的会话是上一条 CMD 留下的」，与锁无关 |
| R6 | 取消：取门今天顺带检查一次取消令牌。删除后不补 `ThrowIfCancellationRequested`，因为停机按 RQ-009 要把已入队的 CMD 执行完，提前在取门处放弃不是要保护的语义。若某个用例或注释表明某处的取消检查是有意的，就在原位补一行显式检查，并在 `task-21-sites.txt` 中写明理由 |
| R7 | 次序：今天写在「租约释放之后」的动作（例如 `SelectCharacterHandler` 的待关闭连接、两塔协调器释放门后的投影）保持在原来的相对位置，只是不再有租约 |
| R8 | 注释：描述门、租约、借用帧、锁序的注释删除或改写（§1 末条）；描述门删除后仍成立的不变量的，改写为不提门的说法 |

### 4. 必须原样保留的非锁职责（本单元范围内）

- 每个 handler 与投影的当前性检查与参与者集合（R5）。
- `SelectCharacterHandler`：各阶段命令的派发顺序、失败补偿、待关闭连接在 `finally` 之后执行（AGENTS.md「SelectCharacter」一节的协调职责中，只有「获取并释放 `SelectedSessionTransitionGate`」随门删除）。
- 两塔协调器：取门之后的 `Validate`、`ExactSessions`（R4(b)）、投影失败后 `CloseExactAsync` 的隔离。
- `PartyTransitionExecution.RunPartyAsync` 的收件人捕获。
- `OnlineUserConnectionObserver`：精确清理（`StandingOf`）、`TryDisconnectConnection` 的代数与身份判断、`ForgetConnection` 的时机。
- 不在本单元：管线 45／58 号行为的非锁职责（附录 H）原样在 Application 中运行，本单元不碰。

### 5. 判据（`LockRemovalSourceTests`）

放在 `Server/tests/Dfo.Server.Application.Tests/Execution/LockRemovalSourceTests.cs`，沿用 `GameplayJobHostStructureTests` 的 `Repository()` 与「文件字典 → 违规列表」的写法：判据函数接收 `IReadOnlyDictionary<string, string>`（相对路径 → 文本），生产用例喂真实的 `Server/src`，反例喂合成字典。

**两支判据**：

1. **名称**：按整词（`\b…\b`）匹配，扫描原文本（包括注释，待裁定第 5 条）。每条规则写「名称、作用范围、例外文件、由哪个单元加入」：

   | 范围 | 名称 | 例外 | 加入 |
   |---|---|---|---|
   | 全部 `Server/src` | `TownAreaProjectionGate`、`DungeonProjectionGate`、`BossTowerProjectionGate`、`BossTowerEntryGate`、`IllusionTowerProjectionGate`、`IllusionTowerEntryGate`、`AchievementTitleProjectionGate`、`LeaseSet`、`TransitionLeaseSet`、`ProjectionLeaseSet`、`WeddingProposalExecution` | — | TASK-21 |
   | `Dfo.Server.Protocol/` | `SelectedSessionTransitionGate`、`OnlineUserExecutionGate`、`UserExecutionGate`、`BorrowOrAcquireSharedAsync`、`BorrowOrAcquireExclusiveAsync`、`AcquireManyExclusiveAsync`、`TransactionalExecutionGate` | — | TASK-21；TASK-22 改为全部 `Server/src` |
   | `Dfo.Server.Protocol/` | `UserAccessMode` | `ProtocolModule.cs` | TASK-21；TASK-22 去掉例外并改为全部 `Server/src` |

2. **`SemaphoreSlim` 白名单**：`Server/src` 中出现 `SemaphoreSlim` 的文件必须在白名单上，每项写理由与去向；白名单上的文件不再出现它，算陈旧条目，同样报违规。本单元结束时的白名单（基准扫描逐项核对）：

   | 文件 | 理由 | 去向 |
   |---|---|---|
   | `Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs` | 队伍投递门（DR-17） | T-006 删除 |
   | `Dfo.Server.Abstractions/Execution/GameplayAssertions.cs` | `GameplayGateAssertion.Enter` 的参数，服务于上一行 | T-006 删除 |
   | `Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs` | 组门 | TASK-22 删除 |
   | `Dfo.Server.Application/Scheduling/ScheduledTaskHostedService.cs` | 并发槽 | TASK-22 删除 |
   | `Dfo.Server.Application/Commands/Pipeline/TransactionalExecutionBehavior.cs` | `TransactionalExecutionGate` | TASK-22 删除 |
   | `Dfo.Server.Application/Events/JoustMaintenanceService.cs` | 维护节拍信号量 | TASK-22 删除 |
   | `Dfo.Server.Application/Effects/EffectOperationLedger.cs` | 每连接账本信号量 | TASK-22 删除 |
   | `Dfo.Server.Application/Events/JoustRuleService.cs` | 规则加载（附录 F「保留」：规则初始化） | 保留 |
   | `Dfo.Server.Application/Characters/CharacterStatRuleService.cs` | 规则初始化（同上） | 保留 |
   | `Dfo.Server.Application/Authentication/PasswordComputationLimiter.cs` | 口令哈希的并发上限，在执行器外（RQ-007） | 保留 |
   | `Dfo.Server.Infrastructure/Content/PvfItemCatalogSource.cs`、`PvfBossTowerContentSource.cs` | PVF 内容源初始化（附录 F「保留」） | 保留 |

   `PasswordComputationLimiter` 不在附录 F 的「保留」栏里，但它是 RQ-007 要求的并发上限，不是玩法门；在 Result 中登记，并在 `follow-ups.md` 记一项交 T-009 补进附录 F「保留」栏。如果扫描找到上表以外的文件，按停止条件 7 处理。

**用例**（N 的一部分）：

| 用例 | 内容 |
|---|---|
| J1 | 生产源码的名称扫描为 0；把扫描的文件数与每条规则的命中写进输出 |
| J2 | 生产源码的 `SemaphoreSlim` 都在白名单上，白名单没有陈旧条目 |
| C1 | 合成字典：全局名称出现在任意文件，被点名 |
| C2 | 合成字典：名称只出现在注释里，仍被点名 |
| C3 | 合成字典：Protocol 范围的名称出现在 Protocol 文件里被点名；同名出现在 Application 文件里不点名 |
| C4 | 合成字典：`UserAccessMode` 出现在 `ProtocolModule.cs` 不点名，出现在另一个 Protocol 文件被点名 |
| C5 | 合成字典：`SemaphoreSlim` 出现在白名单以外的文件，被点名 |
| C6 | 合成字典：白名单文件不再含 `SemaphoreSlim`，报陈旧条目 |
| C7 | 合成字典：名称作为更长标识符的一部分（例如 `TownAreaProjectionGateway`）不点名 |

### 6. 测试侧

#### 6.1 先跑后改

生产改动完成后先构建、跑全量，按失败形态分类。编译错误与测试失败都只按本节规则处理，不顺手改别的。

#### 6.2 处置分类

| 类 | 判定 | 处置 | 记录 |
|---|---|---|---|
| E 编译迫使 | 构造参数、注册、已删类型的引用；断言不受影响 | 删去实参或注册行，断言行不变 | `diffcheck7.py` 证明断言多重集合不变；文件名入 `t005-test-dispositions.md` |
| K 门的反例 | 用例的被测对象就是一把已删的门 | 删除。承接：保护意图「执行器上不等门」已无对象，由 RQ-001 单消费者与 TASK-17、18、19 的零违规全量消除（规格消除） | 逐条写进 `t005-test-dispositions.md` |
| G 门内次序 | 保护意图是门内或门间的次序本身（锁序、FIFO、门释放之后才发送） | 删除，理由写「机制随门删除」；若断言里另有业务结果，就把业务结果留下、只删门相关的断言行，按改写记录 | 同上，「旧断言 → 新断言 → 保护意图」 |
| B 交错下的业务结果 | 保护意图是「某个业务结果在交错下仍正确」 | 改写为两条先后执行的 CMD：先让干扰作为一条 CMD 执行完，再执行被测请求；断言不变 | 同上 |
| V 空转 | 用例仍通过，但它证明的「门被取／门被释放／不等门」已没有对象（例如持着门证明某路径不等它） | 本单元不改（门的类型还在，用例照样能编译、照样是绿的），列入 `logs/task-21-vacuous.txt` 交 TASK-22 | 名单 |

沿用 TASK-17 主控方裁定第 7 条：删除的每一条都写明承接它的后继用例（名字必须能检索到）；没有后继的，写明保护意图为什么已由规格消除。

#### 6.3 已知的成员（基准检索，执行时补全）

- **K**：`ProtocolGateAssertionTests` 中 `A_projection_or_entry_gate_cannot_be_taken_off_the_executor`（Theory 6 行）、`The_title_projection_gate_cannot_be_taken_off_the_executor`、`On_the_executor_the_title_projection_gate_is_taken_released_and_never_waited_on`、`The_weakness_delivery_gate_cannot_be_taken_off_the_executor`、`A_contended_weakness_delivery_gate_fails_the_job_at_once`。6 ＋ 4 ＝ 10 例。同文件的 4 条队伍用例保留（T-006）。
- **E**（预计，以编译结果为准）：直接构造 Protocol handler、投影、协调器并传门的用例。基准上传 `new SelectedSessionTransitionGate()`／`new OnlineUserExecutionGate()` 的测试文件 24 个，引用 Protocol 自有门类型的测试文件 12 个（含 `ProtocolGateAssertionTests`），例如 `BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests`、`TitleBookProtocolTests`、`PartyCommandHandlerTests`、`LeaveDungeonTests`、`SelectDungeonHandlerTests`、`DungeonLoadingProjectionDeliveryTests`、`CharacterDeathHandlerTests`、`TitleBookMutationHandlerTests`、`FinishQuestHandlerTests`、`SelectCharacterLifecycleTests`、`OnlineUserServiceTests`。
- **V 候选**（预计，执行时逐条确认）：
  - `PartyCommandHandlerTests` 的 `Successful_command_notifications_are_delivered_after_both_transition_gates_release`、`Set_party_info_delivers_its_immutable_result_after_the_transition_gate_releases`（`GateReentryNotificationBus` 在投递时重入门，handler 不再持门之后恒成立）、`A_target_that_was_missing_on_the_first_lookup_cannot_appear_behind_the_gate`；
  - `OnlineUserServiceTests.DisconnectWithoutSelectedSession_DoesNotWaitForAccountLifecycleGate`（测试持门证明观察者不等门，观察者不再取门之后恒成立；E 类的构造参数改动照做）；
  - `ReplayStateSnapshotTests` 中 `TrackedEntryCount == 0` 的门泄漏断言（`:48`、`:225`、`Replacement_while_waiting_for_lifecycle_lease_rejects_the_original_read`）：本单元之后 Protocol 不再借用该门，但管线 45 号行为仍然取它，断言照常有对象，是否空转逐条判定。
- **不改用名**：用例名里提到门的（例如 `…_after_gates_release`）本单元不改名，避免方法集合变化掩盖真实的删减；名单随 V 类交 TASK-22（与 T-008 按簇改写时一并决定）。
- **F-68**（跨容器 SQLite 竞争用例）：不经门，本单元不涉及，交 TASK-22 按提纲处置。

#### 6.4 空转名单

`logs/task-21-vacuous.txt` 逐条写：用例全名｜它原来证明的门行为｜本单元之后为什么恒成立｜TASK-22 的建议处置（删除或去掉门相关的行）。名单同时写入 `t005-test-dispositions.md` 的「交 TASK-22」小节。

#### 6.5 N 与 D 的预期

- D：K 类 10 例；G 类按实际（预计 0～少数）。
- N：判据 9 例（J1、J2、C1～C7）；§7 的 VC-7 证据用例（按缺口新增，至多 4 例）；§2.7 R4(b) 的确定性拒绝用例（两塔协调器各 1 例，若现有用例未覆盖「`ExactSessions` 为假时返回 `Rejected(null, CallerNotCurrent)`」）。
- 执行时按实际例数逐项目写加法。

### 7. VC-7 的落实与证据

**解读**：VC-7 说的是「重试用尽」这个分支消失。串行之后 L1～L4 的 `reroute` 已恒不成立（§2.7），所以这个分支自 T-004 起就不可达；本单元把循环与分支一起删除，请求在协调器答「不归我管」之后只走一次普通路径，并照常回包。L3（踢人）不在 VC-7 原文中（待裁定第 4 条）。

**证据**（写进 `t005-test-dispositions.md` 的 VC-7 小节）：

1. **静态**：`task-21-loops.txt` 的逐条分类；§2.0 检索式中 `reroute`、`RetryLimit` 在 Protocol 中为 0。
2. **行为**：四个请求各至少一条用例，在执行器上（`TestGameplay`）运行，断言请求被处理一次并回包（放弃副本：`GiveUpGameResponse` 与进城投影；离队、换队长：队伍通知送达；踢人：成功路径的通知，或拒绝路径的 `RemovePartyMemberRefusal`）。
   - 已有候选：`LeaveDungeonTests.GiveUp_sends_weakness_once_after_success_and_before_town`、`PartyCommandHandlerTests.Leaving_party_in_a_run_detaches_only_the_departing_character`、`Removing_a_member_in_a_run_detaches_only_the_removed_character`、`Party_handlers_dispatch_the_six_party_write_commands`。逐条核对它们是否断言了回包，以及是否装配了两塔协调器。
   - 缺口：现有用例若都没有在装配两塔协调器的情况下走普通路径，就在已有两塔协调器替身的测试文件里各补一条（协调器答「不归我管」→ 普通路径执行一次 → 回包）。
3. **区分力**：变异 S6、S7（§8.2）模拟「用尽后不回包」，第 2 条的用例必须变红。

`follow-ups.md`：在 VC-7 对应的条目（若无则新增）写「已在 TASK-21 落实」，并引用上面的证据位置。

### 8. 全量、取证与变异

#### 8.1 取证预期（先写进 `logs/task-21-violations.txt` 再跑）

本单元删除的反例只有 `ProtocolGateAssertionTests` 的 10 例，其中门外取门 8 条（投影与入场门 6、称号 1、虚弱 1）、执行器上等门 2 条（称号 1、虚弱 1）。新增的判据只读源码，不产生取证记录。

- 按类：`GameplayGateAssertionTests` 21、`ProtocolGateAssertionTests` 4、`PartyTownProjectionDeliveryTests` 2、`MultiAccountExecutionTests` 1、`GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1、正向对照 7。21 ＋ 4 ＋ 2 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 61。
- 按 kind：`gate-off-executor` 25 － 8 ＝ 17，`gate-wait` 20 － 2 ＝ 18，`write-off-executor` 24，`bus-leaked-flow` 2。17 ＋ 18 ＋ 24 ＋ 2 ＝ 61。
- 对照：TASK-19 的 71 － 10 ＝ 61。

#### 8.2 变异（Required check 10）

| 变异 | 应当变红 |
|---|---|
| S1 在 Protocol 任一文件加 `internal sealed class TownAreaProjectionGate;` | J1，点名该文件 |
| S2 给 `TeleportSlice` 的 handler 加回一个 `SelectedSessionTransitionGate` 构造参数（DI 仍能解析） | J1，Protocol 范围规则点名 |
| S3 在 Protocol 任一文件加 `private readonly SemaphoreSlim probe = new(1, 1);` | J2，点名该文件 |
| S4 在 Protocol 的某个 handler 文件加一处 `UserAccessMode.Shared` | J1，`UserAccessMode` 规则点名（例外只认 `ProtocolModule.cs`） |
| S5 两塔协调器之一把 `!ExactSessions(...)` 的结果改为继续执行 | §2.7 R4(b) 的确定性拒绝用例 |
| S6 `GiveUpGameHandler` 在普通路径的 `SendExplicitAsync` 之前 `return` | §7 放弃副本的证据用例 |
| S7 `LeavePartyHandler` 在 `RunPartyAsync` 之前 `return` | §7 离队的证据用例 |
| S8 删除某个已删门位置之后保留的 `IsInWorld` 复查（例如 `TeleportSlice` 中取门后的那一处），使其对已被顶替的会话继续执行 | 覆盖该复查的已有用例；若没有用例变红，如实记录，并在 Result 中列出 R5 保留下来、却没有用例守着的复查（交 T-008） |

S8 不是判据的区分力证明，而是抽查「R5 保留的复查是否有人守着」。没有用例变红不算失败，但必须如实记录。

#### 8.3 冲突用例复核

正式全量中，`task-07-conflicts.md` §15.1 交 T-005 的 3 条（`CommandBusTests` 租约释放）仍通过：它们测管线 50／55 号的租约，归 TASK-22。

### 9. 生产行为为什么不变（VC-7 除外）

- 门：§1 的论证。三类可能的差异分别由 R4（取门前后比较）、R6（取消检查）、Required check 7（升级拒绝）收住。
- 循环：L1～L4、L7 的触发条件串行下恒不成立；L5、L6 的确定性条件结果逐字保留；唯一消失的是「竞态下用尽」，即 VC-7。
- 投递：本单元不改任何发送调用。过渡期直发的 2 秒期限、`ExecutorSendDeadline` 不变。
- 证据：表征测试与金样本不变；正式全量与 TASK-19 相比只有 §6 记录的增删与改写；日志信号逐条对照。

### 10. `follow-ups.md`

- F-19：Weakness `deliveryGate` 一项在本单元关闭；其余（调度 `Task.Run` 与并发槽、组门与准入门、Joust `ticks`）仍归 TASK-22。
- 新增（从 F-84 起编号，以执行时的实际编号为准）：
  - `GameplayGateAssertion` 删除时，取证源（`DiagnosticSourceName`、`Report`）要挪给写断言与泄漏流断言，正向对照改用写断言 → T-006（待裁定第 2 条）；
  - `PasswordComputationLimiter` 的信号量补进附录 F「保留」栏 → T-009（§5）；
  - `DungeonCardPresentationCoordinator.cs:243` 的预留冲突重试在串行下是否还会发生 → T-008（§2.7）；
  - 用例名里仍提到门的 → 与空转名单一起交 TASK-22，改名与否随 T-008 按簇决定（§6.3）；
  - R5 保留下来、却没有用例守着的当前性复查（变异 S8 的结果）→ T-008；
  - AGENTS.md「SelectCharacter」一节与「命令执行总线」中关于 `SelectedSessionTransitionGate` 由 handler 手工获取的描述 → T-009（RQ-100）；
  - VC-7 措辞（待裁定第 4 条的结果）。

## 后续单元边界提纲（TASK-22 = T-005b，不在本单元执行）

**范围**：Application 层的门、管线取锁部分、命令契约中的访问方式与多账号声明，以及 T-005 的收口。基准为 TASK-21 提交之后。

- **生产（`Dfo.Server.Application` 为主，另有命令注册与命令记录）**：
  1. **管线**：删除 50 `OnlineUserExecutionBehavior`、55 `AccountExecutionBehavior`、57 `TransactionalExecutionBehavior` 三个文件及注册（`CommandDispatchingExtensions.cs:44`～`:49`）。45 `SelectedSessionExecutionBehavior` 只删租约列表与借用循环（`:22`～`:26`、`:39`～`:42`）和构造参数 `lifecycle`；附录 H 第 45 行列出的非锁职责全部保留：`BeginMain`、两次 `DemandRecipient`、`Capture`、账号与会话一致性、「参与者集合必须包含报告会话」、逐个 `DemandCurrent`、结果不确定时逐个 `BlockAsync`、`SelectedSessionCommandScope.Enter`。类名保留，T-008 按附录 H 迁移。58、60、100、0 号不动。
  2. **`SelectedSessionSafety`**：删除构造参数 `lifecycle` 与 `BlockAsync` 中的借用（`:64`），其余不动。
  3. **门的类型**：`SelectedSessionTransitionGate.cs`；`OnlineUserExecutionGate.cs`（`UserExecutionGate<T>`、`OnlineUserExecutionGate`、`AccountExecutionGate`）；`TransactionalExecutionGate`；注册 `CommandDispatchingExtensions.cs:26`～`:29`、`ApplicationModule.cs:278`～`:280`（没有使用者的 `UserExecutionGate<CharacterId>`）。
  4. **`TransactionalExecutionGate` 的领域使用者**：`World/DungeonLifecycleDomainService.cs:9`／`:19`、`World/TransactionalDungeonVisitDomainService.cs:12`／`:18`、`World/IllusionTower/IllusionTowerDomainService.cs:22`／`:49`、`World/BossTower/BossTowerDomainService.cs:29`／`:60`、`DomainEvents/DomainEventDispatcher.cs:39`／`:50`／`:123`／`:137`／`:153`。保护「同一时刻一个 SQLite 写者」改由执行器单消费者保证；`AcquireWriteAsync` 的写保留不动（附录 F「保留」）。
  5. **访问方式**：`UserAccessMode` 枚举、`CommandExecutionContext` 的两个字段、`CommandHandlerRegistration` 的两个字段、`AddCommandHandler` 的两个参数、`CommandBus.cs:63`～`:64`；调用点 `ProtocolModule.cs`（118 处）、`ApplicationModule.cs`（2 处）以及 5 个每日补给与到期清理的注册文件。
  6. **`IMultiAccountCommand`**：删除接口与 15 个实现上的声明。`AccountIds` 成员按读者处置：业务代码读它的（`HousingCommand`、`CapsuleCommand`、`WeddingLovePointCommand`、`BuyItemCommand`／`HousingBuyItemCommand` 等）作为 CMD 数据保留；只有 55 号行为读的计算属性一并删除。`IMultiSelectedSessionCommand` 与它的 `Sessions` 保留（附录 F「保留」）；`IAccountCommand` 保留（45 号的一致性检查在用）；`IOnlineUserCommand` 按待裁定第 6 条。
  7. **组门**：`DomainEventDispatcher.cs:46`、`:58`、`:75`～`:76`、`:94`。`GameplayJob.AssertNotOnExecutor("Draining domain event deliveries")` 保留，注释改为「在作业内排空等于入队后等待自己（RQ-003）」，反例 `Draining_from_inside_a_job_fails_at_once_as_a_program_error` 不变。组内串行按待裁定第 7 条。
  8. **调度器**：删除 `ScheduledTaskHostedService` 的并发槽（`:69`、`:101`、`:110`、`:124`、`:133`、`:139`～`:140`、`:167`）与 `Task.Run`（`:127`～`:129`）。`Start` 直接调用 `RunAsync`：它在第一次入队处就返回挂起的任务，计时循环不被同步前缀阻塞，因为回调本身在作业里执行。`ScheduledTaskSchedulerOptions` 与 `AddTaskScheduling(options)` 参数按待裁定第 8 条。
  9. **效果账本**：`EffectOperationLedger.cs:38`（取门）、`:78`（释放）、`:82`（字段）。由 RQ-005 替代（同一连接的下一条入站消息要等上一条 CMD 完成才入队）。
  10. **Joust**：`JoustMaintenanceService.cs:15`、`:29`～`:31`。由 RQ-006 定时源合并与单消费者替代。
  11. **`GameplayGateAssertion`**：类注释改为「随队伍投递门在 T-006 删除」；`Enter`／`Entering`／`WouldWait`、取证源 `DiagnosticSourceName`／`Report` 保留。
- **测试**（按 TASK-21 §6 的分类与记录）：
  - 删除：`GameplayGateAssertionTests`（21 条记录，其中门外 8、等门 13；它的门全部删除）；`OnlineUserExecutionGateTests`（1 例）；`SelectedSessionTransitionGateTests`（2 例）；`MultiAccountExecutionTests`（3 例、1 条记录）；`CommandBusTests` 的 3 条租约释放（F-67，承接者 `GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs`）与 `OnlineUserScopedRegistration_RequiresAnOnlineUserCommand`、`AccountScopedRegistration_RequiresAnAccountCommand`（规格消除：访问方式不存在了）。
  - 改写（RQ-071）：`CommandBusTests.Pipeline_ExecutesBehaviorsInOrderAroundTheTerminalHandler`／`EveryProductionBehaviourResolvesForACommandResultType`；`UnitOfWorkBehaviorTests` 中「事务在账号租约之内」的次序断言（`:123`～`:130`，意图随 55／57 消除，45 ＜ 58 ＜ 60 的次序保留）；`CommandRegistrationTests.GameTicketRedemptionIsTransactionalAndOnlineUserExclusive`（保留执行方式断言，去掉访问方式断言）；`PartyCommandCodecTests:306`～`:307`；`DomainEventDispatchTests` 在测试里持有准入门的两处（`:177`、`:460`）；`GameplayWriteAssertionTests` 用 `TransactionalExecutionGate` 的三处（`:338`、`:358`、`:484`）；`ReplayStateSnapshotTests` 的 `TrackedEntryCount` 断言（门泄漏断言随门删除）；N5 `A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off`（待裁定第 7 条）；`ScheduledTaskTests` 中测并发槽的用例（`Global_limit_queues_one_instance_and_coalesces_waiting_runs`、`Retry_delay_does_not_hold_the_global_execution_slot`、`Factory_uses_configured_singletons_and_explicit_options_override_defaults` 的选项一半、`Invalid_configuration_fails_before_callbacks` 中与并发有关的行），承接者 `ScheduledTaskExecutorTests.Each_attempt_is_its_own_timer_job_and_the_retry_delay_leaves_the_executor_free`；TASK-21 的空转名单。
  - 保留不改：`TransactionalCommandSerializationTests`（意图「事务命令的处理器不并发」由执行器承接，TASK-18 变异 X1 已证明它不依赖门）；F-68 的跨容器 SQLite 竞争用例（建议保留：它们测 SQLite 写保留与提交冲突，`AcquireWriteAsync` 属于附录 F「保留」，与门无关）。
  - 编译迫使：测试中的命令注册与 `CommandExecutionContext` 构造（`EffectOperationReplayTests`、`SelectCharacterLifecycleTests:880`、`SelectCharacterReturnTests:149`、`GmEventsHttpTests`、`LoginSessionQueuedCommandTests` 等）。
- **判据**：`LockRemovalSourceTests` 把 Protocol 范围的名称改为全部 `Server/src`，`UserAccessMode` 去掉例外，再加入 `OnlineUserExecutionBehavior`、`AccountExecutionBehavior`、`TransactionalExecutionBehavior`、`IMultiAccountCommand`、`OnlineUserAccessMode`、`AccountAccessMode`、`ScheduledTaskSchedulerOptions`（视待裁定第 8 条）、`groupGates`；白名单删去五个 TASK-22 项。附录 F「删除」栏中只剩 T-006 的项（队伍两把投递门、`NotificationRetryQueue`、`NotificationRetryDrainService`）不在名单上，在判据旁写明。
- **取证预期**：61 － 21 － 1 ＝ 39。按类：`ProtocolGateAssertionTests` 4、`PartyTownProjectionDeliveryTests` 2、`GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1、正向对照 7；4 ＋ 2 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 39。按 kind：`gate-off-executor` 17 － 8 ＝ 9，`gate-wait` 18 － 13 － 1 ＝ 4，`write-off-executor` 24，`bus-leaked-flow` 2；9 ＋ 4 ＋ 24 ＋ 2 ＝ 39。
- **变异**：判据的名称与白名单各一项；45 号非锁职责各抽一项（去掉「参与者集合必须包含报告会话」、去掉结果不确定时的逐个 `BlockAsync`），指定的已有用例必须变红（后者对应 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 一类）；调度器去掉 `Task.Run` 后计时循环不被阻塞（`ScheduledTaskTests` 中慢任务不阻塞其他任务的用例）。
- **T-005 收口**：新建 `verification/t005-closure.md`：
  - 附录 F「删除」栏逐项：删除于哪个单元 → 判据规则或检索证据；T-006 的三项写明去向；
  - 「保留」栏逐项确认仍在；
  - 附录 H 第 45、58 行的非锁职责逐项对账（AC-009 的这一半，最终对账归 T-008）；
  - VC-7 证据汇总；
  - `follow-ups.md`：F-17（删除一半）、F-19、F-67、F-68 关闭，新事项追加。
- **一次正式全量**：口径同 TASK-21 Required checks 2～3、12；表征与金样本不变；`diffcheck7.py`；F-69 耗时对比。
- **Result review**：required。改的是所有命令都经过的管线与注册契约；45 号行为错删一项非锁职责（例如结果不确定时的封锁），全量多半照样是绿的。

## 待裁定

1. **拆分与顺序**（§0）：T-005 拆成 TASK-21（Protocol 层）与 TASK-22（Application 层、命令契约与收口），Protocol 在前。
   - **建议接受**。理由：两层的风险性质与审查重点不同；项目引用方向决定了只有先清使用者、再删类型，两个单元才能各自是绿的。
   - 备选：不拆（一个单元约 85 个生产文件，审查与证据混在一起）；或按「门 / 管线」横切（会让同一个 Protocol 文件在两个单元里各改一次）。
2. **队伍两把投递门归 T-006，`GameplayGateAssertion` 因此保留到 T-006**（§1）。
   - 队伍的按角色信号量与 `PartyDeliveryStream.Gate` 守的是只在送达后推进的 revision 水位线（附录 E DR-5、DR-17），与水位线一起在 T-006 改写，符合 Plan T-005 行「投递以外」与主控方的提示。
   - 后果：Plan 过渡期规则 2 写「这个断言在第 4 步随门一起删除」，实际按门逐个执行：本单元与 TASK-22 删掉的门，断言挂点与反例一起删除；`GameplayGateAssertion` 类（`Enter`／`Entering`／`WouldWait`）、`ProtocolGateAssertionTests` 的 4 条队伍用例、`PartyTownProjectionDeliveryTests` 的 2 条反例保留到 T-006，与它们守的门一起删除。取证源 `DiagnosticSourceName`／`Report` 由 RQ-061 写断言与泄漏流断言共用，T-006 删除该类时要把它们挪到别处，正向对照 `GameplayAssertionRecorderControlTests` 也要改用写断言（记 F 项交 T-006）。
   - **建议接受**。
3. **称号投影门（DR-16）与虚弱投递门归本单元，不随投递推迟到 T-006**（§1、§2.4、§2.5）。
   - 理由：两者都不依赖送达结果推进状态；过渡期作业上的直发被 await，一个作业的发送在它结束前完成，下一个作业的投影自然排在后面，DR-16 在过渡期成立；T-006 再按 RQ-032 在发件箱上复核 DR-16。F-19 已把虚弱门归 T-005。
   - **建议接受**。备选：称号门随 DR-16 归 T-006，本单元保留它（那样 `SelectCharacterHandler` 要在本单元和 T-006 各改一次）。
4. **VC-7 的措辞补「踢出队员」**（§2.7 L3、§7）。
   - 踢人与离队、换队长共用同一种循环。今天它在重试用尽时回拒绝包 `RemovePartyMemberRefusal`；循环删除后它总是正常处理。这个分支自 T-004 起已不可达，本单元的做法不受影响；但 RQ-082 只允许附录 G 列出的可见变化，附录 G 不提踢人。
   - 两塔协调器 L5、L6 的用尽分支：确定性条件（`ExactSessions` 为假）下结果逐字保留；只有竞态触发的那部分消失，而协调器只服务这四个请求，属于 VC-7 的原意，不需要另列。
   - **建议**：主控方在 `spec.md` 附录 G 的 VC-7 中把「放弃副本、离队、换队长」改为「放弃副本、离队、换队长、踢出队员」。不改的话，本单元在 Result 中把踢人记为「随 VC-7 同类落实、附录 G 未列」。
5. **AC-017「检索为零」按原文本（包括注释）判定**（§5）。
   - **建议包括注释**。理由：AC-017 的字面就是「检索为零」；RQ-060 要求不留类型、注册或转发；留在注释里的旧门名属于 AGENTS.md 所说「漂了不会有人发现」的记载，而且会误导下一个用 grep 找门的人。代价是删门时要同时清掉提到它的注释（R8 本来就要求）。
   - 备选：沿用 `GameplayJobHostStructureTests` 的去注释匹配，只判代码。
6. **（TASK-22）`IOnlineUserCommand` 的去留**。它的唯一消费者是 50 号行为（检查「注册为在线用户作用域的命令必须实现它」）；50 号删除后它是一个没有读者的标记接口，59 处实现。附录 F 没有列它。
   - **建议 TASK-22 一并删除接口声明**（`OnlineUserId` 属性作为 CMD 数据保留）。理由：留下一个没有读者的作用域声明，会让读者以为还有按在线用户取租约的地方；AGENTS.md 中对它的描述由 T-009 一并改。
   - 备选：保留到 T-008 归并命令时处理。这属于共享命令契约的变化，请确认。
7. **（TASK-22）组门删除后，组内串行由什么保证**。生产中每个订阅组只有一个排空者（`RunGroupAsync` 的循环），每条 delivery 一条事件 CMD，等完成再取下一条，所以组内串行由「一个排空者 ＋ 单消费者」保证。可是 N5（`A_group_stays_serial_and_consumes_later_facts_while_a_failed_one_backs_off`）用 3 个并发的 `DrainOnceAsync` 证明组内串行（断言 `MaximumOutstanding == 1`），它依赖组门。
   - **建议**：不加替代守卫，N5 改为经 `RunAsync` 组循环驱动，断言不变；`DrainOnceAsync` 是 `internal`，生产中只有组循环调用，在判据旁写明「同一组只能有一个排空者」。
   - 备选：加一个「同一组并发排空即抛」的快速失败守卫（不等待，只报程序错误），N5 改为断言第二个并发排空被拒。
   - 这条涉及 AC-015「订阅组内顺序」的证据形态，请确认。
8. **（TASK-22）`ScheduledTaskSchedulerOptions` 与 `AddTaskScheduling(options)` 参数**。它们只有「并发上限」一个成员，生产中没有调用方传它。
   - **建议随并发槽删除**（RQ-060：不留转发）；`Abstractions/Scheduling/ScheduledTasks.cs:23` 的公开记录一并删除。
   - 备选：保留类型、不再读取（不建议：留下一个不起作用的配置项）。

## 主控方裁定（执行前）

1. **拆分为 TASK-21（Protocol 层）与 TASK-22（Application、命令契约与 T-005 收口），Protocol 在前，接受。**
2. **队伍两把投递门归 T-006，接受。** `GameplayGateAssertion` 类与 4＋2 条队伍反例保留到 T-006；过渡期规则 2 改为「按门逐个删除」，在 Result 中登记。T-006 删除该类时，取证源与正向对照改挂写断言，登记为新的 F 项。
3. **称号门（DR-16）与虚弱门在本单元删除，接受。**
4. **VC-7 补「踢出队员」，接受，按事实性补全处理。** 本单元在 `spec.md` 附录 G 的 VC-7 中补上这一请求（踢人与离队是同一种循环，用尽时今天同样回拒绝包），RQ／AC 正文不动；Owned boundary 增加 `spec.md` 附录 G 这一处。
5. **AC-017 的「检索为零」连注释一起判定，接受。** 注释里提到被删门的地方一并改写或删除；只描述历史、且对读代码的人仍有用的，改为不含被删类型名的说法。
6. **（TASK-22）删除 `IOnlineUserCommand` 接口声明，`OnlineUserId` 留作数据，原则上接受。** 这是 T-005 删除访问方式的直接后果，仍在 Spec 范围内；TASK-22 规划时核实没有其它读者。
7. **（TASK-22）组门删除后不加替代守卫，原则上接受。** 依据：组排空在执行器上串行执行，同组并发排空在结构上不可能。TASK-22 规划时核实组循环确实只经执行器驱动，并把 N5 改为经 `RunAsync` 组循环驱动、断言不变；核实不成立时转回主控方。
8. **（TASK-22）`ScheduledTaskSchedulerOptions` 与 `AddTaskScheduling(options)` 参数随并发槽一起删除，原则上接受。**
9. **执行时留意的两项**：`PasswordComputationLimiter` 的信号量进白名单，登记 F 项交 T-009 补进附录 F「保留」栏；`DungeonCardPresentationCoordinator.cs:243` 的预留冲突重试登记 F 项交 T-008。

## Result（第 1 轮，合同 Revision 1，2026-10-23，基准 `b1959f3e`，未提交）

### Outcome

done（待独立结果审查，Result review：required）。

- Protocol 层的门、取门、`LeaseSet` 一族、只为取门存在的包装和 7 个「锁内复查、重试」循环全部删除；§2.0 检索式在 Protocol 中只剩计划允许的两类：`ProtocolModule.cs` 命令注册里的 118 处 `UserAccessMode`、`PartyTransitionExecution.RunPartyAsync`（6 个调用点和类声明，只做收件人捕获）。
- VC-7 落实：四个请求在重试用尽时不回包、只记日志或回拒绝包的分支随循环消失；`spec.md` 附录 G VC-7 按裁定第 4 条补了「踢出队员」。
- 判据 `LockRemovalSourceTests` 两支（名称、`SemaphoreSlim` 名单）先 RED 后 GREEN；正式全量 8228 条，失败只有那 2 条 `DimensionDungeonTests`；取证 61 条，反例类与对照以外 0。
- 停止条件一条都没有触发（理由见 Why 1～3）。

### Changed

- **生产代码**：64 个文件，全在 `Server/src/Dfo.Server.Protocol/`（1663 行增、2837 行删；`logs/task-21-boundary.txt`）。63 个是 §2.0 检索式在基准上命中的文件，第 64 个是 `SelectCharacter/SelectCharacterPhaseCommands.cs`（只改了描述生命周期门的注释与两条异常消息）。
  - Protocol 自有的门删除 8 把：7 个门类型（`TownAreaProjectionGate`、`DungeonProjectionGate`、两塔的投影门与入场门共 4 个、`AchievementTitleProjectionGate`），加上 `WeaknessProjectionDelivery` 的 `deliveryGate` 字段；`ProtocolModule.cs` 删 7 行注册。
  - 取门、借用、`AcquireManyExclusiveAsync`、`OnlineUserExecutionGate` 的使用全部删除（R1）；`LeaseSet` 7 处与收集代码删除（R2）；构造参数、字段、可空默认值删除（R3）。逐行处置：`logs/task-21-sites.txt`（基准命中 418 行 ＝ 分类 300 行 ＋ 保留的 118 行 `UserAccessMode`）。
  - 包装（§2.8）：`PartyTransitionExecution.RunAsync` 删除，唯一调用点内联；`RunPartyAsync` 只剩收件人捕获，参数改为 `(sessions, townAreas, action)`；`WeddingProposalExecution` 删除，10 个调用点内联（每个都是所在方法的最后一句，所以 lambda 里的 `return;` 仍然离开方法）。
  - 循环（§2.7，`logs/task-21-loops.txt`）：L1～L4、L7 按 R4(a) 删除；L5、L6 的 `ExactSessions` 按 R4(b) 改为直接返回 `Rejected(null, PartyFailureReason.CallerNotCurrent)`。
  - 循环之外的取门后复查按 R4(a) 删除，末表 12 行、13 处（幻影塔一行含 `ReprojectCompletionAsync` 与 `SweepOneAsync` 两处）（比较对象是取门之前读到的值，中间只有取门和只读语句），清单在 `logs/task-21-loops.txt` 末表；R5 的当前性检查（`IsInWorld`、`StandingOf`、`Capture`、`DemandCurrent`、参与者集合）一律保留。
  - 只服务于被删门或被删复查的私有成员一并删除：两塔协调器的 `HasInvolvedTower`、`SameCapture`、`SameParty`、`OnceAsyncDisposable`，幻影塔的 `AcquireAccountsAsync`，两塔投影的 `AcquireTransitionsAsync`，城镇投影的 `AcquireAreasAsync`／`KeyOrder`，`GetOtherCharacterDetailHandler.ExecuteExactAsync`。`AltarProtocolCoordinator` 的 `Settle…UnderGatesAsync` 两个方法改名为 `SettleBeforeExitAsync`、`SettleGiveUpAsync`。
  - 注释按 R8 与裁定第 5 条改写或删除，清单在 `logs/task-21-sites.txt` 附录 D。
- **测试**（`verification/t005-test-dispositions.md`）：
  - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/LockRemovalSourceTests.cs`（9 条）。
  - `BossTowerProjectionConcurrencyTests` 加 5 条（R4(b) 1 条、VC-7 4 条）。
  - `IllusionTowerProjectionConcurrencyTests` 加 1 条（R4(b)）。
  - 删除 11 例：`ProtocolGateAssertionTests` 10 例（K）、`MarriageProposalCodecTests` 1 例（G）。
  - 改写 3 处（G）：`LeaveDungeonTests`、`ReplayStateSnapshotTests`、`TitleBookMutationHandlerTests`。
  - 编译迫使（E）19 个文件。
- **文档与证据**：
  - `spec.md` 附录 G VC-7 一行（裁定第 4 条）；
  - 新建 `verification/t005-test-dispositions.md`；
  - `verification/follow-ups.md`：改 F-19，新增 F-84～F-91；
  - 日志 `verification/logs/task-21-{build-test,violations,lock-scan,sites,loops,upgrade,diff-check,new-x10,mutations,boundary,isolated,vacuous}.txt`，一次性脚本全文附在各自日志末尾。

### Why

1. **停止条件 1（复查归不进两类）没有触发。** 删取门时遇到的每个比较都归了类：
   - 比较的是取门前后两次读取、中间没有写的，归 (a)；
   - `ExactSessions` 归 (b)；
   - 中间有写的（`AltarScheduler.AdvanceAsync`、`TournamentRewardSweepService.SweepAsync` 的候选与当前、两塔离场投影在发送前后重读 `CurrentRun`）不是锁内复查，保留；
   - 谓词式的当前性检查按 R5 保留，即使它紧跟在被删的取门之后，所以 `BackToVillage` 的 `IsInWorld` 过滤现在连续出现两次。
2. **停止条件 2 没有触发**：8 处 `BorrowOrAcquireExclusiveAsync` 都不在任何命令的 45 号行为之内（`logs/task-21-upgrade.txt`，0 处）。今天唯一能走到「共享租约不能升级」的是一条测试自己持共享租约的断言，按 G 删除（G2）。
3. **停止条件 7 没有触发**：`SemaphoreSlim` 扫描只找到计划 §5 名单上的 12 个文件。`PasswordComputationLimiter` 按裁定第 9 条进名单，并登记 F-85。
4. **R4(a) 的范围比计划点名的 7 个循环大**：计划 R4 的对象是「循环与取门后复查」。除 7 个循环外，我按同一规则删了非循环的取门后比较，共 13 处（末表 12 行）。若审查认为非循环的复查应保留，回退只涉及 `task-21-loops.txt` 末表的 12 行，每行都是独立的条件删除。
5. **R6（取消）**：没有补显式检查。取消发生的位置有三处变化，写在 `logs/task-21-sites.txt` 附录 B：`ReturnToSelectionHandler` 被取消时现在会关连接，`SelectCharacterHandler` 会记失败日志，断线清理不再因取消而跳过清理。正式全量没有用例走这三条路。
6. **VC-7 用例的队伍服务**：用的是生产的 `PartyClientProjectionService`，不是裸的 `PartyService`。裸服务产出的离队、踢人计划没有 `ClearPartyId`，过不了 `PartyNotificationDelivery.ValidatePlanShape`；生产中由投影服务补上。
7. **N10～N15 在基准代码上也通过**：它们钉的是保留下来的行为，所以没有 RED。区分力由变异 S5～S7 证明。只有判据（J1、J2）走了 RED → GREEN，红的原因与预期相符：J1 在基准上有 281 个命中，J2 列出两个名单外的文件。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误。正式全量前做过一次（`logs/task-21-build-test.txt` 第 1 部分），变异之后又做过一次（`logs/task-21-mutations.txt`「final rebuild」）。
2. **正式全量**（`logs/task-21-build-test.txt`，2026-10-22T21:45:47Z～21:48:27Z，测试部分 154 秒，开始时 swap 已用 7183 MB；只作信息）：
   - **总数**：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1699 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 175 ＋ Application 4582 ＋ Host 191 ＝ 8228 ＝ 8224 ＋ 15 － 11。
   - **N ＝ 15**：
     - Application 9，即 `LockRemovalSourceTests` 全部（J1、J2、C1～C7）；
     - Protocol 6：`BossTowerProjectionConcurrencyTests` 中 `A_caller_whose_session_is_not_current_is_refused_at_once_without_a_tower_or_party_write`、`Leave_party_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change`、`Change_leader_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change`、`Remove_member_outside_a_tower_runs_the_ordinary_path_once_and_delivers_the_party_change`、`Give_up_outside_a_tower_runs_the_ordinary_path_once_and_answers_before_the_town`，加上 `IllusionTowerProjectionConcurrencyTests.A_caller_whose_session_is_not_current_is_refused_at_once_without_a_tower_or_party_write`；
     - 9 ＋ 6 ＝ 15。
   - **D ＝ 11**：
     - `ProtocolGateAssertionTests` 10 例：`A_projection_or_entry_gate_cannot_be_taken_off_the_executor`（6 行）、`The_title_projection_gate_cannot_be_taken_off_the_executor`、`On_the_executor_the_title_projection_gate_is_taken_released_and_never_waited_on`、`The_weakness_delivery_gate_cannot_be_taken_off_the_executor`、`A_contended_weakness_delivery_gate_fails_the_job_at_once`；
     - `MarriageProposalCodecTests.Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences` 1 例；
     - 6 ＋ 4 ＋ 1 ＝ 11。
   - **逐项目**：Protocol 1704 ＋ 6 － 11 ＝ 1699；Application 4573 ＋ 9 ＝ 4582；其余六个项目不变。
   - **结果**：
     - 通过 54 ＋ 80 ＋ 1699 ＋ 477 ＋ 968 ＋ 173 ＋ 4564 ＋ 191 ＝ 8206；
     - 失败 2，就是那 2 条 `DimensionDungeonTests`；
     - 跳过 2 ＋ 2 ＋ 16 ＝ 20；
     - 8206 ＋ 2 ＋ 20 ＝ 8228；挂起转储 0。
   - **表征测试**：8 通过、2 跳过，`Characterization/` 与金样本未改（`git status`）。
   - **日志信号**（`analyze.py`，与 TASK-19 同一脚本）：
     - 为 0 的七项仍为 0；
     - `GameplayGateWait`、`GameplayLeakedFlow` 各 1 次，都只出现在 `HostOutputViolationTests` 的显示名里；
     - `Gameplay bus:… failed` 8 条（`Act` 2 ＋ `CreateCharacterCommand` 1 ＋ `RegisterAccountCommand` 1 ＋ `SendGmMailCommand` 4）、`inbound:… failed` 3 条（`GameSelectCharacter` 2 ＋ `GameSellItem` 1）、`gm:… failed` 2 条（`auth/login` 1 ＋ `auth/logout` 1），三类都与 TASK-19 逐条相同；
     - 只计数的「the drain tears it down」本次 0、TASK-17～19 各 1。它是 Networking 真实套接字用例里的一条 info 日志，取决于时序：之后隔离连跑三次 Networking，分别是 0、1、0。
   - **§8.3**：交 T-005 的 3 条 `CommandBusTests` 租约用例通过，归 TASK-22。
   - **F-69**：与 TASK-19 相比，多出 ≥ 9 秒的有 3 条，都是 Host.Tests 中拉起打包进程的用例（＋17、＋14、＋13 秒）。隔离下分别是 3、4、4 秒，属于满载拉长，不是排空的特征。
3. **零违规取证**（`logs/task-21-violations.txt`，预期在运行前已写进日志开头）：
   - 记录 61 条：21 ＋ 4 ＋ 2 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＋ 7 ＝ 61。
   - 按 kind：`gate-off-executor` 17 ＋ `gate-wait` 18 ＋ `write-off-executor` 24 ＋ `bus-leaked-flow` 2 ＝ 61。
   - 与 TASK-19 对照：71 － 10 ＝ 61，逐类相同（`ProtocolGateAssertionTests` 从 14 条降到 4 条）。
   - 反例类与对照以外 0，没有测试帧 0，`exe:` 0，退出码 0。
4. **判据输出**（`logs/task-21-lock-scan.txt`）：
   - 两支各扫 1574 个文件；19 条名称规则的命中都是 0。
   - 名单 12 个文件，每项都有理由与去向，每个文件仍在使用 `SemaphoreSlim`。
   - RED（只改了测试、还没改生产代码时）：J1 有 281 个不同的命中：105 ＋ 74 ＋ 21 ＋ 17 ＋ 11 ＋ 10 ＋ 8 ＋ 6 ＋ 5 ＋ 4 ＋ 4 ＋ 4 ＋ 3 ＋ 3 ＋ 2 ＋ 2 ＋ 2 ＝ 281，按名称的拆分见日志；J2 报 `TitleBookSlice.cs` 与 `WeaknessRecoveryService.cs`。C1～C7 通过。
   - GREEN：9 条全部通过。
5. **取门点清单**（`logs/task-21-sites.txt`）：
   - 基准命中 418 ＝ 分类 300 ＋ `ProtocolModule.cs` 命令注册中的 `UserAccessMode` 118。
   - 分类计数：R1 73 ＋ R1 续行 46 ＋ R2 20 ＋ R3 默认值 2 ＋ R3 字段 1 ＋ R3 参数 99 ＋ R3 注册 7 ＋ R3／R2 类型 16 ＋ R4 循环 16 ＋ R8 注释 2 ＋ §2.8 包装签名 1 ＋ §2.8 `RunPartyAsync` 调用 6 ＋ §2.8 内联 11 ＝ 300。
   - 每个文件写了保留下来的非锁语句；附录 A～D 分别列出：检索式不命中的取门（称号门 7 处）、R6、R7、R8。
   - 结束时同一检索式只剩 118 ＋ 7 ＝ 125 行，都在 §2.0 允许的例外内。
6. **循环表**（`logs/task-21-loops.txt`）：
   - 7 个循环的每个条件都写了两次读取之间有哪些语句；
   - L5、L6 用尽时的结果与新的直接返回逐字相同，都是 `Rejected(null, PartyFailureReason.CallerNotCurrent)`；
   - 另列非循环复查 12 行、13 处。
7. **升级拒绝核对**（`logs/task-21-upgrade.txt`）：0 / 8。
8. **测试改动核对**（`logs/task-21-diff-check.txt`，`diffcheck7.py b1959f3e --lines` 加 `methods.py`）：
   - 方法只少了 D 的 6 个方法（11 例），只多了 N 的 15 个；
   - 断言行多重集合有 6 个文件变化，每处都对到 `t005-test-dispositions.md` 的一行（K1～K5、G1、G2、G4、N10～N15）；
   - 其余文件的断言多重集合不变。
9. **新增测试 ×10**（`logs/task-21-new-x10.txt`）：15 × 10 ＝ 150 次通过，0 失败。
10. **变异核对**（`logs/task-21-mutations.txt`）：
    - 每项都先备份，用后逐字节还原并 `cmp`（退出码都是 0）；`git diff -- Server/src` 的 sha1 前后都是 `700685cb…`；最后做了 `--no-incremental` 重建。
    - 第 1 轮的 S2、S6、S7 变异体编译不过（未读参数、不可达代码，TreatWarningsAsErrors），第 2 轮改写后重跑。
    - S1、S2、S4：J1 变红，点名 `TeleportSlice.cs`；S2 下 Protocol 照常能编译。
    - S3：J2 变红。
    - S5：只有首领塔那条 R4(b) 用例变红。
    - S6：放弃副本的 VC-7 用例变红，另有 `LeaveDungeonTests` 两条也变红。
    - S7：离队的 VC-7 用例变红，另有 `Party_handlers_dispatch_the_six_party_write_commands` 也变红。
    - S8 抽查两处 R5 检查（S8a `RecoverStaminaHandler` 的 `IsInWorld`、S8b `TeleportHandler` 的 `Owns`）：都没有用例变红，如实记录，登记 F-88。
11. **生产改动边界**（`logs/task-21-boundary.txt`）：64 个文件都在 `Dfo.Server.Protocol/` 下，逐文件对到 sites 或 loops；`Server/src` 下没有未跟踪文件。
12. **隔离复跑**（`logs/task-21-isolated.txt`）：
    - Application 4582 条（失败只有那 2 条已知的，跳过 16），取证 49 ＝ 21 ＋ 1 ＋ 24 ＋ 1 ＋ 1 ＋ 对照 1；
    - Protocol 1699 条全部通过，取证 7 ＝ 4 ＋ 2 ＋ 1。
13. **空转名单**（`logs/task-21-vacuous.txt`）：
    - 空转 7 条（V1～V7，V7 只有 isAltar＝false 那一行空转）；
    - 判为不空转、仍经管线取门的 4 组；
    - 名字里提到门的 5 条；
    - 全部写进 `t005-test-dispositions.md`「交 TASK-22」。
14. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。
- 其余：`pgrep -x yes` 为 0（本单元没有加负载）；结束时执行了 `dotnet build-server shutdown`。

### Remaining

- **交 TASK-22**：
  - 空转用例 V1～V7 与名单外的门泄漏断言（`t005-test-dispositions.md` §7）；
  - `AltarProjection.cs` 那行无用的 `using`，以及两处描述管线租约的注释（F-91）。
- **交 T-006**：`GameplayGateAssertion` 删除时，取证源与正向对照要改挂写断言（F-84）。
- **交 T-008**：
  - R5 检查有没有用例守着：抽查的两处都没有（F-88）；
  - 卡牌预留冲突的重试（F-86）；
  - 用例改名（F-87）。
- **交 T-009**：
  - 附录 F「保留」栏补 `PasswordComputationLimiter`（F-85）；
  - AGENTS.md 中由 handler 手工取门的描述（F-89）。
- **需要主控方确认**：Why 4 中非循环复查的删除范围。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-21-result-review.md`），4 条低严重度发现都不阻断。13 处取门后复查逐处判定全部成立，删除范围维持。提交前处理：

1. **L1**：`spec.md` 附录 G 的 VC-7 补一句：不含两塔协调器 `ExactSessions` 为假的确定性拒绝（结果与基准逐字相同，不属于重试）。属事实性收窄，RQ／AC 正文不动。
2. **L2**：Result 中「12 处」改为「12 行、13 处」。
3. **L3**：`logs/task-21-sites.txt` 附录 B 的结论按评审口径理解为「删除取门不引入基准不可达的取消结果，三处只是举例」；日志原文保留，以本条为准。
4. **L4**：`sites.py` 中 `TownAreaProjectionDelivery` 的备注方法名应为 `ForcePositionAsync`（行号 `:234-236` 无误）；只影响备注，以本条为准。
5. 剩余风险（R5 保留的当前性检查覆盖未逐一确认）沿用 F-88，交 T-008。
