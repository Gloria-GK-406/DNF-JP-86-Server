# TASK-24 — T-006b：附录 E 逐行承接——补经执行器与发件箱的证据，删除只剩传输失败才进入的分支，删除队伍两把投递门与门断言

基准提交 `50d7bce4`（TASK-23／T-006a 投递翻转已提交，第 2 轮结果审查 approved）。范围取自：
- `tasks/TASK-23.md` 文末「TASK-24 — T-006b」边界提纲整节，含第 1 轮评审后补入的「两塔进入时一名成员交付失败，只关闭该成员，其余成员与请求者照常进入」发送端用例；
- `tasks/TASK-23.md` Result 的 Remaining「交 TASK-24」：29 条过渡期用例（按 DR 行），以及 F-25、F-84、附录 F 的队伍两把投递门、§9 表「TASK-24 要做的」一栏；
- TASK-23 Detailed Plan §9 的附录 E 逐行状态表；
- `spec.md` 附录 E（DR-1～DR-17）、附录 G（VC-3 已扩展到两塔进入、VC-4、VC-6）、AC-011、AC-014、AC-017，以及 AC-009 中 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 断言不变的要求；
- `plan.md` 共享决定「Spec 复审残余的澄清」（R-1、R-5：DR-7、DR-8、VC-5、VC-6 只针对传输层交付失败；GM Cera 组装失败仍返回 unconfirmed、房间机制组装失败仍抛出等重试、队伍投影在 CMD 内再试一次组装，「第 5 步对这几处各补一条单元测试」）与「测试」第 2、3 条；
- `verification/follow-ups.md` 中归属 TASK-24 的 F-25、F-84（检索「TASK-24」「T-006」逐条收齐的结果见 §14）；
- `reviews/task-23-result-review.md` 的残余（F-97、F-98、F-100、F-101、F-102、F-104 去向都已登记在别的单元，本单元只在触及时遵守，见 §14）。

输入还有：
- TASK-23 的产物：`Application/Execution/Outbox/OutboxAdapters.cs`（登记适配器）、`GameplayOutbox`、`OutboundSender`、`IOutboundMessageEncoder`、`IOutboundOrdering`；七个 XO 产出点的有序组；`Protocol.Tests/Outbox/XoDelivery.cs`（真实登记适配器、真实发件箱与有序组、真实 `OutboundSender`，接一个可以逐连接放慢、卡住或失败的测试传输）；`Networking.Tests/Integration/OutboxWireTests.cs`；
- `verification/t006-test-dispositions.md` 的 TASK-23 一节（分类口径沿用，TASK-24 一节续写）；
- TASK-21、TASK-22 的判据 `LockRemovalSourceTests`（J1 名称、J2 信号量名单）；TASK-17、TASK-18 的写断言、取证汇与 `verification/probe-tools/violations.py`；TASK-01 的表征宿主 `Networking.Tests/Characterization/CharacterizationHost.cs`。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **可达性判定，逐处证明**（RQ-038、附录 E；本单元的核心约束）：
     - 先按 §2 列出翻转之后发送、关连接、即时投影三类调用还能在调用点抛出的全部来源；
     - 再把附录 E 各行涉及的每一处 `catch`／失败分支逐处归类：**D**（只剩传输失败或已不存在的来源能进入，删除）、**KE**（登记点的编码、端点或参数校验失败仍能进入，保留）、**KB**（CMD 内业务失败、组装失败 `Failed`、提交不确定仍能进入，保留）。
     - 每处的归类都写出「被 try 的调用 → 翻转后它能抛出什么 → 结论」，落盘 `logs/task-24-sites.txt`。删除的每一处都要证明「翻转后传输失败不会再抛到这里」，而且没有任何非传输来源能进入；只要还有一个非传输来源能进入，就保留。
  2. **删除 D 类分支**（附录 E DR-1～DR-3、DR-4、DR-9、DR-11 涉及的部分，§4）：规划时读码预计只有一类——包在关连接请求外面、「关闭失败就记 Warning」的 `try/catch`，基准上 10 处。`OutboxConnectionCloser` 翻转后只会在取消或执行器外抛出，这些位置都传 `CancellationToken.None`，执行器外是必须暴露的程序错误。调用本身保留，只删吞咽。
  3. **KE、KB 分支保留**，并为每一处保留的分支补一条**可达性证据**：经真实登记适配器与发件箱，在编码接缝上注入失败（§10 的 `FaultingEncoder`），或经真实通知总线让组装失败，证明分支今天仍然会被走到、反应与今天相同（§3、§9）。注释里把「发送失败」写成触发源而今天已不成立的，改成现在的触发源，只改这些句子。
  4. **附录 E 每一行一条经执行器与发件箱驱动的证据**（AC-014）：传输失败一律在测试传输上制造（`XoTransport.Fail`／`Stall`），断言「只有失败的连接被发送端关闭，CMD 与其他参与者不受影响」，再引用或补一条「该连接断开后由生命周期 CMD 清理」的用例（§3 表）。其中包括：
     - VC-3：组队进本，以及领主之塔、幻影塔进入时一名成员交付失败，只关闭该成员，其余成员与请求者照常进入（提纲补入的发送端用例）；
     - VC-4：怪物死亡、祭坛、清算在提交后发生传输层交付失败时，只关闭失败的连接，不封锁同局其他会话；
     - VC-6：GM Cera 提醒的结果（§7，取值按待裁定第 2 条）；
     - Plan R-5 的三条组装失败单元测试：GM Cera 组装失败返回 unconfirmed、房间机制组装失败抛出等重试、队伍投影在 CMD 内再试一次组装。
  5. **AC-011 其余几条**（§8）：
     - 祭坛提交后某个收件人组装失败（按待裁定第 1 条的结论写断言）；
     - 线级：怪物死亡在提交后失败时，每个参与者既被封锁、连接也被关闭（点名测试之外另写一条，AC-009 的点名测试断言不变）；
     - 线级：选角发布后失败走补偿时，同伴收到清理通告（b2 的同伴金样本不覆盖，§1 V9）。
  6. **删除队伍两把投递门**（附录 F「删除」栏投递三项的最后一项；DR-5、DR-17；§5）：`PartyProjectionStreamRegistry` 的按角色 `SemaphoreSlim`、`EnterParticipantsAsync`、`PartyParticipantLease`，`PartyDeliveryStream.Gate` 及其全部取门与 `Release`。流状态（`LatestRevision`、`CurrentPartyId`、成员集合与墓碑）保留：DR-5 要求水位线「刷出时推进」，它仍是投影的依据。
  7. **删除门断言，迁移取证源**（Plan 过渡期规则 2 的最后残余；F-84；§6）：删除 `GameplayGateAssertion` 的门成员（`Entering`、`WouldWait`、`Enter`、`OffExecutorKind`、`WaitKind`）与 `GameplayGateWaitException`；取证源（`DiagnosticSourceName`、`ViolationEvent`、`Report`）挪到一个只做取证的类型，写断言、泄漏流断言与 RQ-003 检查共用，取证源名称的字符串值不变。各程序集的正向对照 `GameplayAssertionRecorderControlTests` 改用写断言。
  8. **判据**（§11）：
     - J1 加入本单元删到零的名称；J2 名单删去 `PartyNotificationSlice.cs`、`GameplayAssertions.cs` 两行；类注释改为「附录 F 删除栏全部点名」；
     - AC-017 的「检索为零」一半：附录 F「删除」栏投递三项（`NotificationRetryQueue`、`NotificationRetryDrainService`、队伍按角色的 `SemaphoreSlim` 与 `PartyDeliveryStream.Gate`）在 `Server/src` 检索为零，检索式见 Required check 4。
  9. **过渡期用例逐条复核**（TASK-23 Remaining 的 29 条，§9）：逐条判定为「KE 改写」（换入真实适配器、作为可达性证据）、「KE 保留」（注入点仍对应真实的登记点异常，改记为 F 类，驱动方式交 TASK-25）或「删除」（注入点在真实适配器下已不可能抛出，写承接者），处置按待裁定第 6 条。
  10. **一次正式全量**：带取证汇，反例类与对照以外为 0；表征测试 10 通过、0 跳过，`Characterization/` 与金样本目录不改；变异核对（§13）。
- Owned boundary：
  - **生产代码**：
    - `Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs`（§6）；
    - `Server/src/Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs`、`Party/PartyTownProjectionDelivery.cs`（§5）；
    - D 类的 10 处所在文件（§4 表）：`Protocol/Game/BossTowerPartyCoordination.cs`、`IllusionTowerPartyCoordination.cs`、`BossTowerProjectionDelivery.cs`、`IllusionTowerProjectionDelivery.cs`、`DungeonEntry/DungeonEntryProjectionDelivery.cs`、`ReturnToSelection/ReturnToSelectionHandler.cs`、`SelectCharacter/SelectCharacterHandler.cs`、`Creatures/CreatureRuntimeDelivery.cs`、`Login/GameLoginHandler.cs`，以及 `Application/Commands/SelectedSessionSafety.cs`；
    - KE／KB 分支所在文件中「触发源已不成立」的注释句（§3），文件以 `logs/task-24-sites.txt` 的命中为准，不改可执行语句；
    - `Server/src/Dfo.Server.Application/Administration/GmCeraDomainService.cs`（§7，`GmCeraReminderDomainService` 的结果映射）；
    - 祭坛（`Protocol/Game/Altar/AltarProjection.cs`）只在待裁定第 1 条选备选 B 时改；
    - 编译迫使的改动（引用被删成员的生产文件），逐个列名。
    - 基准命中清单由 §1、§4、§5、§6 的检索式生成，写进 `logs/task-24-sites.txt`。
  - **Spec**：默认不改。只有待裁定第 1 条选备选 A 时改 `spec.md` AC-011 第 4 条与 RQ-031(d) 末句这两处措辞（事实性更正，按裁定给出的文字），改动全文写进 `logs/task-24-boundary.txt`。
  - **测试**：
    - §9、§10 列出的处置、改写与新增，涉及 Application、Protocol、Networking、Host 四个测试项目与 `tests/Support`；
    - 删除 `Protocol.Tests/Execution/ProtocolGateAssertionTests.cs`（4 条）与 `Protocol.Tests/Game/Party/PartyTownProjectionDeliveryTests.cs` 中的 2 条门反例（K 类，规格消除）；
    - `tests/Support/GameplayAssertionRecorder.cs`、`GameplayAssertionRecorderControlTests.cs`，`Application.Tests/Execution/ViolationCapture.cs`、`LockRemovalSourceTests.cs`，`Networking.Tests/Outbound/OutboxRegistrationTests.cs`，`Host.Tests/HostOutputViolationTests.cs`（§6 的编译与名称后果）；
    - `Protocol.Tests/Outbox/XoDelivery.cs` 按 §10 扩展，或移到 `tests/Support` 并链接进需要它的项目（只有一份实现）；
    - 逐文件列入 `verification/t006-test-dispositions.md` 的 TASK-24 一节。
  - **文档与证据**：`verification/t006-test-dispositions.md`（TASK-24 一节，含 §3 的附录 E 逐行证据表）；`verification/follow-ups.md`；`verification/probe-tools/violations.py` 的期望表（§12）；`verification/logs/task-24-*.txt`；一次性脚本放 scratchpad，全文附在对应日志末尾。
  - **不改**：
    - KE、KB 分支的可执行语句（附录 E 各行中 CMD 内命令失败、组装失败、提交不确定的处理，RQ-023(c)、RQ-031(d)、Plan R-5），包括 `IsolatePartialCohortAsync` 与 `RollbackEntry` 的调用、两塔 `SendToExactAsync` 的失败集合与 `CloseProjectionFailuresAsync`、两塔进入的 `deliveryFailure`、`SelectCharacterHandler` 的补偿、`PartyExactNotificationDelivery.PublishWithRetryAsync`；
    - `Application/Notifications/NotificationBus.cs`（F-102 归 TASK-25）；`Networking` 全部生产代码；`Dfo.Server.Persistence`、`Infrastructure`、`HttpApi`、`Host` 的生产代码；`Dfo.Server.GmWeb` 的生产代码（`GmCeraEndpoints` 的 HTTP 层 unconfirmed 保留，除非待裁定第 2 条另有结论）；`Web/gm-admin`；
    - 除 §9 点名的以外，注入「发送即记录」替身的用例（TASK-25）；
    - `plan.md`、`AGENTS.md`、`Docs/`；`Characterization/` 目录与金样本文件；`task-07-conflicts.md`、`entry-reconciliation.md`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-24-build-test.txt`。
     - **总数**：以执行开始时在基准上 `--list-tests` 的计数为准，预期 8191（TASK-23 正式全量）。本单元为 8191 ＋ N － D，逐项目写加法，N、D 逐条列名（预期见 §12）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动（`git status` 为证）。
     - **日志信号**：与 TASK-23 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。应为 0 的七项仍为 0；`GameplayGateWait` 由 1 变为 0（唯一来源 `HostOutputViolationTests` 的那一行随 §6 删除）；`Outbound connection … closed`、`Gameplay bus:… failed`、`inbound:… failed`、`gm:… failed` 逐条与 TASK-23 对照，新增的逐条归到本单元的用例。
     - 耗时与内存只作信息（RQ-091 用户修订：单条 ≤ 2 s 即可接受，性能不作停止条件）。F-69：与 TASK-23 的正式全量逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-24-violations.txt`）：`violations.py` 的命令与 TASK-23 Checks 3 相同，期望表按 §12 改；预期先写进日志开头再跑：反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0；kind 中不再出现 `gate-off-executor`、`gate-wait`。
  4. **判据与检索**（`logs/task-24-lock-scan.txt`）：
     - `LockRemovalSourceTests` 三支各扫描的文件数，J1 新名称与 J2 改动先 RED（生产代码放回基准）后 GREEN 的两次输出；
     - AC-017 检索式在结束时为 0 行：
       ```
       grep -rnE --include='*.cs' "NotificationRetryQueue|NotificationRetryDrainService|GameplayGateAssertion|GameplayGateWaitException|EnterParticipantsAsync|PartyParticipantLease|participantGates|\.Gate\b" Server/src
       grep -rn --include='*.cs' "SemaphoreSlim" Server/src/Dfo.Server.Protocol/Game/Party
       ```
       第一条中 `\.Gate\b` 若在 Party 目录之外命中名字相同、含义无关的成员，逐个列名说明；
     - 附录 F「删除」栏其余各项沿用 TASK-22、TASK-23 的检索结果，重跑一次确认仍为 0。
  5. **可达性清单**（`logs/task-24-sites.txt`）：§1 V2 检索出的每一处，逐行写「文件:行 → 被 try 的调用 → 翻转后可抛出的来源（引 §2 表的行）→ D／KE／KB → 处置 → 证据用例名」。未分类 0。结束时 D 类全部删除、KE／KB 全部仍在（同一检索式重跑）。
  6. **基准核实**（`logs/task-24-verify.txt`）：§1 的检索命令与输出全文，开始时在基准上跑一次，结束时在工作区再跑一次。基准上的结果与 §1 不一致时，按停止条件 2 处理。
  7. **测试改动核对**（`logs/task-24-diff-check.txt`）：`diffcheck7.py 50d7bce4 --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`，证明已有测试的方法集合只少了 D 中列出的、只多了 N 中列出的，断言行多重集合的变化逐处对应 `t006-test-dispositions.md` 的一行；`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 的断言行多重集合不变（AC-009），单列一行。
  8. **新增与改写测试 ×10**（`logs/task-24-new-x10.txt`）：本单元新增与改写的全部用例隔离连续 10 次，全部通过。线级用例另在 Networking 隔离下 ×10。
  9. **变异核对**（`logs/task-24-mutations.txt`；程序与 TASK-23 Checks 10 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§13 的每一项都要有指定的用例变红；标为「抽查」的如实记录。
  10. **生产改动边界**（`logs/task-24-boundary.txt`）：`git diff --stat 50d7bce4 -- Server/src` 只含 Owned boundary 列出的文件与编译迫使的文件，逐文件对到 `task-24-sites.txt` 的行；`Server/src` 下没有未跟踪的文件，本单元新建的除外，逐个列名。`git diff 50d7bce4 -- .rks-agent/tasks/serial-gameplay-executor/spec.md` 为空，或只有待裁定第 1 条授权的两处，全文附在日志里。
  11. **隔离复跑**（`logs/task-24-isolated.txt`）：Application、Protocol、Networking、Host 各一次，都带取证目录，记录各自的反例类与对照记录数；Networking 单独跑的理由同 TASK-23 Checks 12。
  12. **附录 E 与附录 G 逐行证据表**（写进 `t006-test-dispositions.md` TASK-24 一节，`logs/task-24-dr-evidence.txt` 附检索结果）：DR-1～DR-17 每行至少一条经执行器与发件箱驱动的用例名（可检索），VC-3、VC-4、VC-6 各自的用例名；TASK-23 已给出的行（DR-10、DR-13、DR-14、DR-15）照录并复核仍在。这张表是 TASK-25 收口时 AC-014、AC-017 对账的输入。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出；改动的已有文件行尾与基准一致（TASK-23 F4 的教训：带 CRLF 的文件逐个核对）。结束时执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-23（`50d7bce4`）：登记适配器、作业结束刷出、工作单元钩子、有序组、`OutboundSender` 接线，XO 证据夹具 `XoDelivery`，`OutboxWireTests`（含 N9：发送端超时关闭后被关连接的 `lifecycle:disconnected` 在执行器上运行），`t006-test-dispositions.md` TASK-23 一节，`logs/task-23-doubles.txt`。
  - TASK-21、TASK-22：`LockRemovalSourceTests`（J1、J2）。
  - TASK-17、TASK-18：写断言、取证汇、`verification/probe-tools/violations.py`。
  - TASK-01：表征宿主与 b 场景的故障注入写法（`CharacterizationOptions.Decorate`）。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/methods.py`，规划时核对存在；执行开始时再核对，缺失时从 TASK-23 日志末尾附的全文恢复。
- Result review：required。
  - 本单元的主要判断是「哪段代码只为传输失败存在」。判错有两个方向：把仍可由编码失败、业务失败进入的分支当死代码删掉，全量多半仍是绿的（今天守这些分支的用例大多注入自己的替身）；把只剩传输失败的分支留下，则 AC-014 与提纲的「死代码清理」名不副实。所以证据是逐处的可达性记录（Required check 5）加上变异：删掉每一处保留的分支，对应的可达性证据必须变红。
  - 改动跨十几个协议切片与 Abstractions 的取证源。取证源是三条判据与七个程序集正向对照共用的契约，改错会让「零违规」失去意义而不变红，所以正向对照的记录数与 kind 是必查项。
  - 附录 E 逐行证据表、VC-3／VC-4／VC-6 的证据与 AC-017 的「检索为零」是 TASK-25 收口和 M-2 的依据。
  - VC-6 改变 GM HTTP 接口 `reminder` 字段的取值（待裁定第 2 条）。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 某一处分支的可达性判不清：被 try 的调用经由本计划 §2 之外的实现（另一个发送器、装饰器、生产中的另一条注册），或某个 `catch` 只能由传输失败进入、却不在 §4 的 D 类形状里（例如需要改可执行语句才能删、或删除会改变 CMD 的结果）。
    2. §1 的核实结果与本计划不一致：§2 表多出一个能在调用点抛出的来源；D 类候选的令牌不是 `CancellationToken.None`、调用前也没有处理取消；队伍门的取门点多于 §5 列出的；V7 读出的祭坛行为与待裁定第 1 条的描述不同。
    3. 需要改 Owned boundary 以外的生产文件，或需要改 `spec.md`（待裁定第 1 条授权的两处除外）、`plan.md`。
    4. 正式全量出现 §9、§10 规则覆盖不了的新失败；取证出现反例类与对照以外的记录。
    5. 表征测试或任何金样本需要改动。
    6. 某条要删除的用例，保护意图既找不到承接者，也说不出为什么已被规格消除；`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 需要改任何一行断言（AC-009）。
    7. 写某一行的传输失败证据时，发现今天的生产行为不等于附录 E「新的承接方式」或附录 G 已授权的变化（即 TASK-23 §9「目标已满足」的判断有误）。这属于附录 E／G 的缺口，按附录 E 末句交主控方决定是否补表。
    8. AC-011 的两条线级回归需要在生产代码里加注入点才能构造，且待裁定第 3 条没有授权降级。
  - 变异核对期间，允许临时改动 `Server/src`，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 范围、与提纲的差异、为什么不拆

**提纲与本单元约束的差异**（待裁定第 4 条）：
- 提纲（TASK-23 文末）写「删除 DR-1～DR-3 等行只被传输失败触发的分支」，点名了 `IsolatePartialCohortAsync` 与传输失败触发的 `RollbackEntry`、两塔 `SendToExactAsync` 的失败集合与 `CloseQuietlyAsync`、`SelectCharacterHandler` 的发送失败补偿、`LeaveDungeonSlice` 的同伴发送 `catch`。
- 规划时读码（§2）：翻转之后，这些 `catch` 包着的发送调用在登记点仍会因编码、端点或参数校验失败而抛出（裁定第 1 条备选 B：编码在登记点完整完成，失败在调用点抛出）。它们都是不带类型过滤的 `catch`，没有一处只接传输异常。所以按本单元的约束，它们是 KE 分支，**保留**；可执行语句不动。
- 真正只剩传输失败（以及已不存在的「连接已断开即抛出」）能进入的，是包在关连接请求外面的吞咽分支：今天它们接住的是 TouchSocket 关闭失败，翻转后 `OutboxConnectionCloser` 只登记，不碰传输（§4）。
- 同理，TASK-23 Result 把 29 条过渡期用例记为「只证明死代码」。按本单元的判定，它们中的大多数证明的是保留下来的 KE 分支；逐条重新归类（§9）。

**本单元做的五件事**，按依赖排序：
1. 可达性判定与 D 类删除（§2、§4）——它决定其余各节「保留什么、给什么补证据」；
2. 附录 E 逐行的传输失败证据与保留分支的可达性证据（§3、§7、§8）；
3. 过渡期用例逐条复核（§9）；
4. 队伍两把投递门（§5）；
5. 门断言与取证源（§6），以及判据（§11）。
第 4、5 件与前三件互不依赖，可以先做，见下文执行顺序。

**为什么不拆**：
- 生产改动不大：D 类 10 处只删 `try/catch` 外壳；队伍门 2 个文件；取证源 1 个文件；GM Cera 1 个文件。没有语义翻转。
- 判断量集中在可达性清单与证据表，它们是同一张表的两栏（§3），拆开会让「这一处为什么留」和「它的证据是哪条」分在两个单元里，审查时对不上。
- 测试量（预计新增 25～32 条、改写约 10 条、删除约 10 条，§12）小于 TASK-23（新增 40、删除 43、改写 30 个文件）。
- 若 §1 V2 的检索结果比本计划多出很多 D 类候选（例如超过 20 处），或 KE 分支的可达性证据需要大量夹具改造，在执行开始时就停下回报，由主控方决定是否按「24a：§5、§6、§11 / 24b：其余」拆开；执行者不自行拆分。

**执行顺序**：
1. 在基准上跑 §1 的核实（Required check 6）与 §1 V2 的检索，生成 `task-24-sites.txt` 的初稿（Required check 5 的前半）；先按 §2 把每处分类写完，再动代码。
2. 先写判据（§11 的 J1 新名称、J2 删两行），看它在基准上变红。
3. 队伍门（§5）→ 取证源（§6）：每步构建一次，跑 Protocol 与 Application。
4. D 类删除（§4）：构建，跑相关项目。
5. GM Cera（§7）。
6. 测试支撑（§10）→ 附录 E 逐行证据（§3）→ AC-011（§8）→ 过渡期用例（§9）。
7. 构建、正式全量与取证（Required checks 1～3），隔离复跑，新增与改写 ×10。
8. 变异（§13）、清单、边界、diff 核对、证据表、`follow-ups.md`。

### 1. 基准核实（执行时按 Required check 6 重跑并落盘）

| # | 问题 | 检索 | 基准上已知的结果（`50d7bce4`，规划时读码） |
|---|---|---|---|
| V1 | 翻转后三类调用在调用点能抛出什么 | 读 `OutboxAdapters.cs`（`OutboxNetworkMessageSender`、`OutboxConnectionCloser`）、`GameplayOutbox.Current`、`NotificationBus.PublishAsync`、`EventService.ProjectAsync`、Protocol `GameMessageSender` | 见 §2 表 |
| V2 | 附录 E 各行涉及的 `catch`／失败分支 | 在 `Server/src/Dfo.Server.Protocol`、`Dfo.Server.Application` 中，找出 try 体（同一方法内）调用 `INetworkMessageSender`、`IGameMessageSender`、`INetworkConnectionCloser`、`IEventService.ProjectAsync`、`ICleanupNotificationDelivery.DeliverAsync`、`PartyExactNotificationDelivery.PublishWithRetryAsync` 或各 `*ProjectionDelivery`／`*Delivery` 发送方法的每个 `catch`，以及读 `NotificationOutcome.Failed`／`Gone`、`HasFailures` 的每个分支。脚本放 scratchpad，全文附日志 | 规划时按点名文件读过：选角、组队进本、两塔进入与协调、怪物死亡、清算、祭坛、队伍、配额刷新、GM Cera、房间机制、生物、回选角、初始连接、离开副本、登录。执行时以脚本结果为准，逐行进 `task-24-sites.txt` |
| V3 | 有没有只接传输异常的 `catch` | `grep -rnE "catch \((IOException\|SocketException\|ObjectDisposedException\|TimeoutException)\|when \(.*(IOException\|SocketException\|ObjectDisposed\|TimeoutException)" Server/src/Dfo.Server.Protocol Server/src/Dfo.Server.Application` | 只有 `GameplayExecutor.cs:111` 的 `TimeoutException`（执行器自己的停止等待，与发送无关）。没有按传输异常过滤的发送 `catch` |
| V4 | D 类候选：包在关连接请求外面的吞咽 | `grep -rn -B3 "RequestClose\(Exact\)\?Async" Server/src`，逐个读令牌与 `catch` | 10 处，见 §4 表；令牌都是 `CancellationToken.None` |
| V5 | 队伍门的取门点 | `grep -rnE "EnterParticipantsAsync\|\.Gate\b\|GameplayGateAssertion" Server/src/Dfo.Server.Protocol` | `PartyNotificationSlice.cs`：`EnterParticipantsAsync` 定义（`:468`）与调用（`:660`），`stream.Gate` 取门 `:663`、`:742`，释放 `:724`、`:755`，按角色门 `:483`；`PartyTownProjectionDelivery.cs`：`EnterParticipantsAsync` 调用 `:61`、`:176`，取门 `:66`、`:198`，释放 `:146`、`:224` |
| V6 | `GameplayGateAssertion` 与 `GameplayGateWaitException` 的引用 | `grep -rn "GameplayGateAssertion\|GameplayGateWaitException\|gate-off-executor\|gate-wait" Server/src Server/tests --include='*.cs'`（排除 `TestResults`） | 生产：`GameplayAssertions.cs`、两个队伍文件。测试：`ProtocolGateAssertionTests`、`PartyTownProjectionDeliveryTests`（2 条门反例）、`GameplayAssertionRecorder.cs`（订阅取证源；exe 输出识别名单含 `GameplayGateWaitException`，`:57`）、`GameplayAssertionRecorderControlTests`、`ViolationCapture.cs`、`OutboxRegistrationTests.cs:206`、`LockRemovalSourceTests.cs:60`、`HostOutputViolationTests.cs:13`（一行 InlineData 以它为显示名） |
| V7 | 祭坛提交后组装失败时谁被关闭（待裁定第 1 条） | 读 `AltarProjection.cs:87-112` 与调用方 `AltarHandlers.cs:54-70`、`AltarScheduler.cs:71`，以及 `SelectedSessionSafety.BlockAsync` | 规划时读码：`BlockAsync(new(recipient, null), …)` 传的连接为 `null`，所以只封锁会话、**不登记任何关闭**；随后 `throw uncertain`。入站路径上，作业体异常按 RQ-031(c) 关闭来源连接，即只有请求者被关闭；定时路径（`AltarScheduler`）没有来源，没有人被关闭。被封锁的其余参与者，下一次请求在 `DemandCurrent` 处失败，按 DR-15 断线。怪物死亡不同：它先 `safety.Capture(x)` 取得每个参与者的连接（`MonsterDeathSlice.cs:423`），`BlockAsync` 会逐个登记精确关闭 |
| V8 | GM Cera 今天的结果与消费方 | 读 `GmCeraDomainService.cs:14-28`、`GmWeb/GmCeraEndpoints.cs:41-55`；`grep -rn "reminder" Web/gm-admin/src` | `RemindAsync`：没有会话的账号跳过（结果仍是 `attempted`）；有会话的投影后，任一 `Failed` 或 `Gone` 即 `unconfirmed`。HTTP 层：`RemindAsync` 抛出（含 5 秒超时）即 `unconfirmed`。`gm-admin` 只在测试桩里构造 `reminder`，页面不读它 |
| V9 | b2 的同伴金样本是否覆盖「同伴收到清理通告」 | 读 `SelectCharacterCompensationCharacterizationTests.cs` 类注释与 `Golden/b-select-quest-failure.peer.hex` | 不覆盖：请求者落在不共享在场的城镇门口，类注释写明「基线上没有其他玩家被告知任何事」 |
| V10 | AC-011 两条线级回归在现有宿主上的可构造性（待裁定第 3 条） | 读 `CharacterizationHost`、`CharacterizationSteps`（组队、进 144）、`CharacterizationOptions.Decorate`；`IOutboundMessageEncoder` 能否在宿主 DI 里装饰 | 规划时读码：组队进本 144 的步骤现成（表征 c）；`Decorate` 可以装饰任何已注册服务，`IOutboundMessageEncoder` 在 `NetworkingModule` 注册，可装饰为「对指定连接的指定消息类型抛出」。击杀怪物的入站帧与选角时让请求者落在共享在场区域的方法未读到，执行时确认 |
| V11 | DR-6 七个配额刷新是否共用一段「只有 Delivered 才记本期已送」的代码 | 读 `StateSynchronization/UsageQuotaProjection.cs` 与七个 `*QuotaDailyRefreshService.cs` | 未读完，执行时确认。共用时证据挂在共用的判定上，七个刷新各列名引用；不共用时每个一条 |
| V12 | DR-16 称号投影的路径 | 读选角 CMD 之后的称号事件订阅与它的投影（`AchievementTitle*`），以及 TASK-21 删除 `AchievementTitleProjectionGate` 时的承接用例 | 执行时确认，结果写进 §3 表的 DR-16 行 |
| V13 | 注入「会抛异常的关连接器」的用例 | 在实现 `INetworkConnectionCloser` 的测试替身里找 `throw` | 规划时检索命中 `CommonExperienceEffectTests.CallbackCloser`：它的 `RequestCloseAsync` 抛出，意图是「这里禁止按 ID 关闭」，不是模拟关闭失败。执行时逐个判定，D 类删除之后这类用例的断言是否变化 |
| V14 | 29 条过渡期用例的注入方式 | 逐条读：注入的是哪个替身、在哪个调用点、对哪个收件人、抛什么类型 | 见 §9 表的预判，执行时逐条确认后再处置 |

### 2. 可达性判定：翻转之后三类调用还能抛出什么

**来源表**（V1；本节是 `task-24-sites.txt` 每一行「可抛出的来源」一栏的引用对象）：

| 调用 | 翻转后在调用点仍会抛出 | 翻转后不再抛出 |
|---|---|---|
| `INetworkMessageSender.SendAsync`（`IGameMessageSender.SendCmdAsync`／`SendNotiAsync` 转给它） | 参数为空；调用方取消（`ThrowIfCancellationRequested`）；编码失败、端点不符、没有编码器（`encoder.EncodeMessage`）；执行器外（`outboxes.Current`，程序错误） | 传输写失败、交付超时、队列超限（都在发送端，按 RQ-036 关该连接）；连接已断开（F-83，静默丢弃） |
| `SendBatchAsync` | 同上（整批编码） | 同上；上下文已被替换（交付前 `Stale`） |
| `SendExplicitAsync` | 参数校验（`InvalidDataException`）；取消；**直接收件人**那一批的编码失败；执行器外 | 同上；**目标收件人**的编码失败（适配器记 Warning 后跳过该目标，不抛出） |
| `INetworkConnectionCloser.RequestCloseAsync`／`RequestCloseExactAsync` | 参数为空；取消；执行器外 | 传输关闭失败；连接已不在或已被替换（静默丢弃） |
| `IEventService.ProjectAsync` → `NotificationBus.PublishAsync` | 在可写事务内调用（守卫）；执行器外 | 逐收件人的组装或编码失败不抛出，记为 `Failed`；传输失败不再产生 `Failed` |
| `GameMessageSender`（Protocol） | CMD／NOTI 类别不符（程序错误） | — |

**分类规则**：
- **D（删除）**：把这处 `catch` 能接到的来源逐个对表，全部落在「不再抛出」一栏，或者只剩「执行器外」这一项（执行器外是 RQ-061 必须暴露的程序错误，吞掉它只会让取证之外的代码继续跑）。取消的情形：调用传的是 `CancellationToken.None`，或 `catch` 之前已经有 `when (token.IsCancellationRequested) throw` 一类的处理。
- **KE（保留）**：仍有「编码失败」「端点不符」「参数校验」中的任何一项能进入。例：组队进本 XO-1 循环里每个成员都是 `SendExplicitAsync` 的**直接**收件人，他那一批编码失败仍会在调用点抛出，进入隔离与回滚。
- **KB（保留）**：`catch` 的 try 体里除了发送，还有 CMD 内的业务步骤（读当前视图、评估、认领、组帧、`commandBus.SendAsync`），或分支读的是 `Failed`（翻转后只剩组装或编码失败，Plan R-5）。
- **特殊：注入点对应「不再抛出」一栏的用例**。例如替身在某个 `SendExplicitAsync` 的**目标**收件人上抛出：真实适配器在那里不会抛，用例证明的是一条生产中已走不到的路径（§9 的「删除」）。

**KE／KB 的反应与今天相同，这是保留它们的依据**：编码在登记点完成（裁定第 1 条），抛出的时刻与今天的「发送抛出」相同；此前登记的条目按 RQ-031(c) 照常刷出，关连接条目排在该连接已登记的条目之后。所以「已送达者先收到、再被隔离关闭」与今天的线上结果一致（例如组队进本：先登记的跟随者批次先交付，再执行 `IsolatePartialCohortAsync` 登记的关闭）。这一点由 §3 每个 KE 行的可达性证据钉住。

### 3. 附录 E 逐行承接表

「分支」一栏是 TASK-24 执行前的预判，以 `task-24-sites.txt` 为准；「证据」一栏中 T 开头的是传输失败证据（新增，经 §10 的夹具：真实登记适配器、真实发件箱、真实 `OutboundSender` 接 `XoTransport`），R 开头的是保留分支的可达性证据（改写或新增，经 `FaultingEncoder` 或真实通知总线的组装失败）。

| 行 | 今天的分支位置 | 分支预判 | 本单元的生产处置 | 证据 |
|---|---|---|---|---|
| DR-1 选角 | `SelectCharacterHandler` 的工作流 `catch` → 补偿；拒绝对外的 `catch (deliveryFailure)`；`pendingCloses` 关闭循环 | 工作流 `catch`：KB（命令失败）＋KE；拒绝对 `catch`：KE；关闭循环的 `catch`：D | 删关闭循环的吞咽（§4） | T1：接受进入的批次交付失败 → 只有该连接被发送端关闭（`Failed`），不派发补偿命令；引用断线拆除用例（W5／W6 或执行时检索到的同类）。R1：接受进入批次中一条消息编码失败 → 补偿、关闭来源（`SelectCharacterLifecycleTests.RequiredOwnerFailure_…` 改写，§9） |
| DR-2 进本 | `DungeonEntryProjectionDelivery` XO-1 循环的 `catch` → `IsolatePartialCohortAsync` ＋ `RollbackEntry`；`IsolatePartialCohortAsync` 内的关闭吞咽 | 循环 `catch`：KE；关闭吞咽：D | 删关闭吞咽；`IsolatePartialCohortAsync` 留成一个只登记关闭的循环；循环 `catch` 上方「TCP cannot retract…」一段注释改为现在的触发源（登记点编码失败） | T2（VC-3）：跟随者交付失败（`Fail`，不是超时）→ 只关他，请求者与其余成员交付，副本局没有回滚（`TryAbortEntry` 未发生、run 仍在）。R2：跟随者那一批编码失败 → 已登记者照常刷出后被关闭、进本回滚、来源关闭（`PartySelectDungeonHandlerTests.A_leader_delivery_failure_isolates_the_member_that_already_received_start_map` 或同行另一条改写） |
| DR-3 两塔 | 进入：`deliveryFailure` `when (committed)` → 全体隔离成员 `CloseQuietlyAsync` 再抛出；其余投影：`SendToExactAsync` 的失败集合 → `CloseProjectionFailuresAsync`；`CloseQuietlyAsync` 内的关闭吞咽；协调器 `CloseExactAsync` 的关闭吞咽 | 进入 `catch`：KB＋KE；失败集合：KE；两处关闭吞咽：D | 删两塔投影与两塔协调器共 4 处关闭吞咽；失败集合与进入 `catch` 不动 | T3、T4（VC-3，提纲补入）：领主之塔、幻影塔进入时一名成员交付失败 → 只关该成员，其余成员与请求者照常进入，CMD 不抛出、没有其他关闭条目。R3：进入时一名成员那一批编码失败 → 全体隔离成员与请求者被关闭（今天的行为）；`SendToExactAsync` 一名成员编码失败 → 只关他 |
| DR-4 怪物死亡、祭坛、清算 | 怪物死亡提交后 `catch` → 全体 `BlockAsync`（含精确关闭）；清算 `PublicationFailedAsync`；祭坛 `Failed` → 全体 `BlockAsync(…, null)` 再抛出；`BlockAsync` 内的关闭吞咽 | 三处主分支：KB（组帧、评估、认领、组装）＋KE；`BlockAsync` 关闭吞咽：D | 删 `SelectedSessionSafety.BlockAsync` 的关闭吞咽 | T5、T6、T7（VC-4）：怪物死亡、祭坛、清算各一条：一名参与者交付失败 → 只关他，其他会话 `StandingOf(...).Fenced` 为假，CMD 成功。R4：AC-011 的两条（§8：祭坛组装失败；怪物死亡线级） |
| DR-5 队伍水位线 | `Delivered` 才推进 `LatestRevision`；城镇只对 `Delivered` 补发 | KB（`Failed` 只剩组装失败）| 删两把门（§5），水位线逻辑不动 | T8：一次队伍投影登记即推进水位线；一名成员交付失败 → 他被关闭，水位线不回退；引用「重连时选角下发完整队伍状态」的已有用例（执行时检索列名）。R5（Plan R-5）：一名收件人组装失败 → `PublishWithRetryAsync` 在 CMD 内再组装一次，仍失败则 `HasFailures`、不推进后续阶段 |
| DR-6 配额刷新 | 七个刷新只有 `Delivered` 才记「本期已送」 | KB | 不改 | T9：交付失败（传输）不清「本期已送」，该连接被关闭（刷出时记录）。R6：组装失败 → 不记，下一刻再试（`UsageQuotaProjectionTests` 的三条之一改写，§9）。V11 决定是一条还是七条 |
| DR-7 GM Cera | `Failed`／`Gone` → unconfirmed | KB | 按 §7 改结果映射 | T10：在线账号交付失败（传输）→ 仍为「已提交发送」；R7（Plan R-5）：组装失败 → unconfirmed；另加「不在线」一条（取值按待裁定第 2 条）|
| DR-8 房间机制 | `Failed` → 抛 `IOException` 等重试 | KB | 不改 | T11：传输失败不抛出，事件 delivery 完成，该连接被关闭。R8（Plan R-5）：组装失败 → 抛 `IOException`，delivery 记一次失败等重试 |
| DR-9 生物重命名 | 逐收件人 `catch` → 关该收件人，所有者失败返回假；关闭吞咽 | 逐收件人 `catch`：KE；关闭吞咽：D | 删 `CreatureRuntimeDelivery` 的关闭吞咽 | T12：外观与 ACK 依次登记在所有者连接上；所有者交付失败 → 连接被关闭，ACK 随之丢弃，不先于外观到达。R9：`CreatureProjectionDeliveryTests` 两条之一改写（§9） |
| DR-10 清理通告 | `CleanupNotificationDelivery` 所有者必达 | — | — | TASK-23 N8 XO-5 两条，复核仍在 |
| DR-11 回选角 | `ReturnToSelectionHandler` 的 `catch` → 关闭；关闭吞咽 | `catch`：KB＋KE；关闭吞咽：D | 删关闭吞咽 | T13：回选角回包交付失败 → 只被发送端关闭，拆除不重复执行。R10：回包编码失败 → 关闭来源、失败照常上抛 |
| DR-12 初始 NOTI | `OnlineUserConnectionObserver` 的 `catch` → `TryDisconnect` 并抛出 | KE | 不改 | T14：初始通知交付失败 → 发送端关闭，被关连接的 `lifecycle:disconnected` 移除在线身份（N9 的同形；能在 `LifecycleWireHost` 上做就做线级）。R11：`OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity` 改写（§9） |
| DR-13 钱包、PvP 重试 | — | — | — | TASK-23 N14 与 §6 删除，复核 |
| DR-14 期限、重试队列、排空服务 | — | — | — | TASK-23 删除与 `OutboundSenderTests` 期限用例，复核 |
| DR-15 handler 抛异常 | — | — | — | TASK-23 N6、b1、b2、W7，复核 |
| DR-16 称号投影 | 选角压住实时称号投影 | — | 不改 | T15：选角 CMD 之后执行的称号事件 CMD，其条目在同一连接上排在选角条目之后（V12 定路径） |
| DR-17 队伍流门 | 按代际的流门与按角色门 | — | 删两把门（§5） | T16：两条先后完成的队伍 CMD，投影按完成顺序交付，后一条的修订号不低于前一条；无门之后旧修订的「墓碑」规则仍成立（`PartyTownProjectionDeliveryTests.ACommittedRevisionStillRefreshesANewObserverButAnOlderSnapshotDoesNot` 仍过） |

附录 G：VC-3 由 T2、T3、T4；VC-4 由 T5、T6、T7；VC-6 由 T10、R7 与 §7 的「不在线」用例；VC-1、VC-2、VC-5、VC-8 在 TASK-23 已落实，本单元只复核证据名仍可检索。

若写某一行的 T 证据时发现今天的行为不是「新的承接方式」所写的，按停止条件 7 处理。

### 4. D 类：关连接请求外面的吞咽分支

| # | 位置（基准行号，执行时核对） | 今天接住的 | 翻转后能进入的来源 | 处置 |
|---|---|---|---|---|
| D1 | `BossTowerPartyCoordination.cs:326-335` `CloseExactAsync` | TouchSocket 关闭失败 | 只有执行器外（令牌 `None`） | 删 `try/catch`，调用保留 |
| D2 | `IllusionTowerPartyCoordination.cs:326` 起，同 D1 | 同上 | 同上 | 同上 |
| D3 | `BossTowerProjectionDelivery.cs:811` `CloseQuietlyAsync` | 同上 | 同上 | 删 `try/catch`；`IsInWorld` 判定保留 |
| D4 | `IllusionTowerProjectionDelivery.cs:762` `CloseQuietlyAsync` | 同上 | 同上 | 同上 |
| D5 | `DungeonEntryProjectionDelivery.cs:488` `IsolatePartialCohortAsync` | 同上 | 同上 | 删 `try/catch` |
| D6 | `ReturnToSelectionHandler.cs:51` | 同上 | 同上 | 删 `try/catch`，`failure.Throw()` 不变 |
| D7 | `SelectCharacterHandler.cs:247-257` `pendingCloses` 循环 | 同上 | 同上 | 删 `try/catch` |
| D8 | `CreatureRuntimeDelivery.cs:78-90` 收件人关闭 | 同上 | 同上 | 删内层 `try/catch`，外层逐收件人 `catch`（KE）不变 |
| D9 | `GameLoginHandler.cs:150-157` `RejectAsync` | 同上 | 同上 | 删 `try/catch`；`close_failed` 日志分类随之消失，在 Result 中登记 |
| D10 | `SelectedSessionSafety.cs:68` `BlockAsync` | 同上 | 同上 | 删 `try/catch` |

- 证明写法（每行进 `task-24-sites.txt`）：引 §2 表「`RequestClose*Async`」一行，读出令牌是 `CancellationToken.None`，`catch` 只能接到执行器外的 `GameplayOffExecutorException`；该异常抛出前已经写了 `write-off-executor` 取证，而全量取证中反例类之外为 0，所以生产与测试中都没有走到这些 `catch`。
- 不在本表的 `RequestClose*Async` 调用（`CreatureRuntimeDelivery.cs:185` 带 `stoppingToken`、没有 `catch`；`LoginSessionRegistry.cs:103`）不动。
- D 类删除之后，注入「会抛异常的关连接器」的用例（V13）逐个判定：替身抛出的意图若是「此处不应按 ID 关闭」，断言照旧成立（异常不再被吞，但它本来就不该被调用）；若是「模拟关闭失败」，按 §9 规则删除并写承接者。

### 5. 队伍两把投递门（DR-5、DR-17）

- 删除：
  - `PartyProjectionStreamRegistry.participantGates`、`EnterParticipantsAsync`；`PartyParticipantLease` 整个类；
  - `PartyDeliveryStream.Gate`；
  - `PartyNotificationSlice.cs` 与 `PartyTownProjectionDelivery.cs` 中每一处 `using var participantLease = await streams.EnterParticipantsAsync(…)`、`GameplayGateAssertion.Enter(stream.Gate, …)`，以及只为 `stream.Gate.Release()` 存在的 `try/finally`（`try` 体原样保留，`continue`／`return` 语义不变）；
  - 只为取门而存在的辅助方法（例如只喂给 `EnterParticipantsAsync` 的 `PlanParticipants`），以执行时「无其他调用方」为准。
- 保留：`PartyProjectionStreamRegistry` 本身与它的按代际字典，`PartyDeliveryStream` 的 `LatestRevision`、`CurrentPartyId`、`CurrentMembers`、`FullStateMembers` 与墓碑语义；`PublishWithRetryAsync`（Plan R-5）。
- 注释：讲「命令先释放门再做传输」「一个流让四阶段投影连续」的句子，改为现在的理由（同一条 CMD 内顺序登记，CMD 之间按完成顺序交付，RQ-032；水位线仍挡住旧修订），只改这些句子。
- 类名 `PartyDeliveryStream` 不改，J1 不加这个类名（待裁定第 5 条）；门成员由 J2 与 AC-017 检索式守着。
- 测试：
  - 删除 `PartyTownProjectionDeliveryTests` 的 `A_read_repair_finding_its_stream_gate_taken_fails_the_job_at_once`、`A_moved_party_refresh_finding_its_stream_gate_taken_fails_the_job_at_once`（K 类：被测的门删除，规格消除，附录 F）；
  - 删除 `ProtocolGateAssertionTests` 整个文件 4 条（K 类）。其中两条「不能在执行器外取门」的保护意图（队伍投递只在执行器上发生）由 RQ-061 发件箱一半承接：`NotificationDeliveryTests.A_projection_off_the_executor_is_a_program_error_reported_as_evidence`、`OutboxRegistrationTests.A_send_off_the_executor_is_a_program_error_reported_as_evidence`；另两条「门要等就失败」的意图随门一起被规格消除（单执行器下没有可等的门）；
  - 其余队伍用例预计不改即过。

### 6. 门断言与取证源（F-84）

- `GameplayAssertions.cs`：
  - 新的取证类型，例如 `public static class GameplayAssertionDiagnostics`：`DiagnosticSourceName`（值仍为 `"Dfo.Server.Gameplay.Assertions"`）、`ViolationEvent`（值仍为 `"Violation"`）、`internal static void Report(string kind, string operation)`；名字由执行者定，约束是不含 Gate；
  - `GameplayWriteAssertion`、`GameplayNetworkWaitAssertion`、`GameplayLeakAssertion` 改调新类型的 `Report`；
  - 删除 `GameplayGateAssertion` 整个类与 `GameplayGateWaitException`；`GameplayAssertionViolation` 的文档注释改指向新类型。
- 测试侧（E 类，断言不变）：
  - `GameplayAssertionRecorder.cs`、`ViolationCapture.cs`、`OutboxRegistrationTests.cs` 改引用新类型的 `DiagnosticSourceName`；
  - `GameplayAssertionRecorder.cs` 的 exe 输出识别名单去掉 `GameplayGateWaitException`；
  - `HostOutputViolationTests` 以 `GameplayGateWaitException` 为显示名的那一行 InlineData 删除（D 1 行；它测的是「识别名单里的类型名会被记为 exe 记录」，名单其余各行仍在，承接者为同一 Theory 的其余行）。
- 正向对照：`GameplayAssertionRecorderControlTests` 改为 `GameplayWriteAssertion.Writing("recorder-control")`，断言不变（仍是 `GameplayOffExecutorException`，消息含 `recorder-control`）；七个程序集各一条记录，kind 由 `gate-off-executor` 变为 `write-off-executor`。
- `violations.py`：期望表去掉 `ProtocolGateAssertionTests` 与 `PartyTownProjectionDeliveryTests` 两个反例类，正向对照的 kind 改为 `write-off-executor`；若脚本按 kind 白名单判断，删去 `gate-off-executor`、`gate-wait`（§12）。

### 7. GM Cera（VC-6，F-25）

- 今天（V8）：`attempted`／`unconfirmed`。`Gone` 与 `Failed` 都给 `unconfirmed`，没有会话的账号不影响结果。
- 改为（取值与合并规则按待裁定第 2 条；下面是建议 A）：
  - `unconfirmed`：本批任一账号的投影组装失败（`Failed`，Plan R-5），或 HTTP 层 `RemindAsync` 抛出（`GmCeraEndpoints`，不改）；
  - `offline`：否则，本批至少一个目标账号没有在世界中的会话（`Find` 为空，或投影结果为 `Gone`）；
  - `attempted`：其余，即全部目标账号的提醒都已登记、提交发送。按裁定第 4 条「改义不改名」，字符串不改，含义改为「已提交发送」。
- 只改 `GmCeraReminderDomainService`；`IGmCeraReminderDomainService` 签名不变。
- 证据（Host 或 Application，按现有 Cera 夹具所在）：在线 → `attempted`；一个在线、一个不在线 → `offline`；组装失败 → `unconfirmed`；在线但交付失败（传输）→ `attempted`（T10）。`GmCeraHttpTests` 里 `FailingReminder` 那条（HTTP 层 `unconfirmed`）不改。
- 关闭 F-25。

### 8. AC-011 其余几条

1. **祭坛提交后组装失败**（R4 之一，按待裁定第 1 条）：
   - 夹具：真实通知总线，让 `AltarStateChanged` 的处理器对一名参与者抛出；经真实登记适配器与发件箱在作业里运行。
   - 建议 A 的断言（今天的行为）：全部参与者 `Fenced`；`ProjectAsync` 之后抛出 `TransactionOutcomeUncertainException`；入站路径上，请求者的来源连接在它此前登记的条目之后被关闭（RQ-031(c)）；其余参与者没有关闭条目。
   - 若选备选 B：断言全部参与者都有精确关闭条目，生产改 `AltarProjection` 传入 `Capture` 得到的连接。
2. **怪物死亡线级**（R4 之二，点名测试之外另写）：
   - 在表征宿主（只读复用，不在 `Characterization/` 目录里加用例）上两人组队进 144、击杀一只怪物；用 `Decorate` 把 `IOutboundMessageEncoder` 装饰成「对报告者那一批中的 `MonsterDeathNotification` 抛出」——这是报告者的直接批次，真实适配器在调用点抛出，走提交后 `catch`。
   - 断言：两个连接都在读到此前登记的帧之后收到 FIN；服务端两个会话都 `Fenced`；之后同局没有任何玩法 CMD 继续处理这两个连接的请求。
   - 新文件放 `Networking.Tests/Integration/`。
3. **选角发布后补偿、同伴收到清理通告**（线级）：
   - 同一宿主：同伴先在一个共享在场的城镇区域里；请求者选角时让它落在同一区域（V10 确认手段：预置位置或宿主选项），在发布之后的阶段注入失败（沿用 b2 的 `FailingQuestEntry` 写法）。
   - 断言：同伴收到请求者的离开通告（清理通告），请求者在补偿后被关闭。
- 若 2、3 在现有宿主上必须改生产代码才能构造，按待裁定第 3 条处理。
- AC-011 第 6 条（判据阻止 CMD 直接调用发送器）由 `CommandHandlerArchitectureTests` 已有，留给 TASK-25 收口时对账，本单元不动。

### 9. 过渡期用例逐条复核（TASK-23 Remaining，29 条）

**处置规则**（待裁定第 6 条）：
- **KE 改写**：每个保留分支至少一条可达性证据经真实适配器。优先改写本行过渡期用例中最容易换入真实适配器的那一条：把抛异常的替身换成 §10 的夹具，异常改由 `FaultingEncoder` 在编码接缝上抛出，类型与消息沿用原替身（例如原来抛 `IOException` 就仍抛 `IOException`），断言中「对 CMD 反应」的部分不变；断言里读替身记录的部分（`sender.Sent`、`closer.Closed`）改读发件箱条目，逐处写进处置记录。换不进的，新增一条 R 用例代替改写，原用例按下一条处理。
- **KE 保留**：注入点仍对应真实的登记点异常（直接收件人的发送、`SendAsync`、`SendBatchAsync`），用例照旧有效。不改，在 `t006-test-dispositions.md` 里改记为「F 类：证明保留的 KE 分支，驱动方式交 TASK-25」，并写明它对应 §2 表的哪一个来源。
- **删除**：注入点对应 §2 表「不再抛出」一栏（`SendExplicitAsync` 的目标收件人、关连接器的关闭失败、连接已断开即抛出），用例只证明生产中走不到的路径。删除，承接者为 §3 的 T 证据。
- AC-009 点名的 `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`：只能是「KE 保留」或「KE 改写且断言不变」。若它的注入点在目标收件人上（真实适配器不抛），按停止条件 6 停下。

| DR | 用例 | 预判 | 说明（执行时按 V14 逐条确认） |
|---|---|---|---|
| DR-1／11 | `SelectCharacterLifecycleTests.RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection` | KE 改写（R1）或 KE 保留 | 账本式夹具；换不进真实适配器时新增 R1，本条保留 |
| | `…PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt` | KE 保留 | PvP 直接收件人 |
| | `…PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks` | 待确认 | 若注入在目标收件人上则删除，承接 T1 与 TASK-23 的 VC-5 证据 |
| DR-2 | `PartySelectDungeonHandlerTests.A_projection_failure_cannot_split_party_members_between_town_and_run` | KE 保留 | 成员是直接收件人 |
| | `…A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town` | KE 保留 | 同上 |
| | `…A_leader_delivery_failure_isolates_the_member_that_already_received_start_map` | KE 改写（R2） | 断言「已收到 START_MAP 的成员被隔离」改读关闭条目排在他的进入批次之后 |
| | `PartyMoveMapHandlerTests.Publication_failure_logs_the_committed_room_and_preserves_the_exception` | KE 保留 | — |
| DR-3 | `BossTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session` | KE 保留 | 保护意图是精确会话（被替换的会话不被关），与触发源无关 |
| | `IllusionTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session` | KE 保留 | 同上 |
| DR-4 | `MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact` | KE 保留 | — |
| | `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` | KE 保留（AC-009） | 见上方规则 |
| | `GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact` | KE 保留 | — |
| | `IllusionTowerRewardProjectionTests.Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | KB；改写或保留待确认 | 「两次尝试」若依据 `Failed`，翻转后只剩组装失败，改写为组装失败（真实总线）；若依据发送抛出，KE 保留 |
| DR-6 | `UsageQuotaProjectionTests.Tournament_online_refresh_follows_daily_reset_and_retries_failed_delivery` | KE 改写（R6）或保留 | 三条中挑一条改为组装失败 |
| | `…Mirror_online_worker_refreshes_at_local_midnight_and_retries_failed_delivery` | KB 保留 | — |
| | `…Gold_silver_horn_online_reset_refresh_retries_failures_without_sending_every_tick` | KB 保留 | — |
| | `UsageQuotaTests.Quota_refresh_is_postcommit_coalesced_and_a_failed_projection_never_rolls_back_business_counts` | KB 保留 | — |
| DR-9 | `CreatureProjectionDeliveryTests.Peer_send_failure_closes_peer_and_continues_without_closing_owner` | KE 改写（R9） | `SendNotiAsync` 逐收件人，真实适配器会抛；`sender.Sent`、`closer.Closed` 改读条目 |
| | `…Owner_send_failure_closes_owner_and_reports_failure` | KE 保留 | — |
| DR-12 | `OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity` | KE 改写（R11）或保留 | — |
| 离开副本 | `LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit` | KE 保留 | 同伴发送是直接收件人 |
| 登录 | `GameLoginTests.MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity` | KE 保留 | — |
| | `GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect` | KE 改写 | 线级：装饰器从 `IGameMessageSender` 移到 `IOutboundMessageEncoder`（首个 `GameLoginResponse` 编码抛 `IOException`），断言不变；它同时是 D9 之后登录拒绝路径的线级证据 |
| 提交后不回滚 | `SkillLearningCommandTests.A_send_failure_leaves_the_committed_learning_for_relogin` | KE 保留 | — |
| | `CeraShopPurchaseTests.Send_failure_does_not_repeat_or_undo_the_committed_purchase` | KE 保留 | — |
| | `NpcShopPurchaseTests.Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit` | KE 保留 | — |
| | `ItemSaleTests.Failed_delivery_does_not_retry_or_reverse_the_sale` | KE 保留 | — |
| | `EffectOperationReplayTests.A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request` | KE 保留 | F-101 同文件，TASK-25 一并改驱动 |
| | `MailboxCoreIntegrationTests.Rolled_back_or_failed_mail_never_notifies_and_delivery_errors_do_not_undo_commit` | KE 保留 | — |

加法：DR-1／11 3 ＋ DR-2 4 ＋ DR-3 2 ＋ DR-4 4 ＋ DR-6 4 ＋ DR-9 2 ＋ DR-12 1 ＋ 离开副本 1 ＋ 登录 2 ＋ 提交后不回滚 6 ＝ 29。

- 改写或新增 R 用例时，若用例在 `DomainEventDispatcher.DrainOnceAsync()` 之后立即读帧，按 F-104 的做法加执行器屏障，并在处置记录里写明。
- 若某条 R 用例要让同一收件人的第二条消息编码失败，注意 F-102（`NotificationBus` 逐条登记，前一条已登记）；不在本单元修，断言只写今天的行为。

### 10. 测试支撑

- 以 `Protocol.Tests/Outbox/XoDelivery.cs` 为唯一实现扩展（需要在 Application.Tests 里用时，移到 `tests/Support/` 并链接进 Application.Tests 与 Protocol.Tests；两者都能看到 Networking 的 internal 并引用 Protocol。是否改名由执行者定，改名时 XO 用例只改类型名，记 E 类）：
  - `FaultingEncoder`：包住现有的 `NameEncoder`，对「指定连接 ＋ 指定消息类型（或谓词）」抛出给定的异常；其余照常编码。
  - 传输失败沿用 `XoTransport.Fail`／`Stall`／`Slow`；结果从 `XoObserver` 与 `Recorder.Entries` 读。
  - 关闭条目：从 `Recorder.Entries` 中 `IsClose` 的条目读，或按 `XoTransport.IsClosed` 读交付后的结果；两者的区别在处置记录里写清楚（前者是「登记了关闭」，后者是「发送端执行了关闭」）。
- 线级（§8）用 `CharacterizationHost` 的 `Decorate` 装饰 `IOutboundMessageEncoder`，不新建宿主类型。
- 证据口径同 TASK-23 §10.6：本单元的 T、R 证据一律经真实执行器、真实登记适配器与发件箱；需要传输的，用真实 `OutboundSender` 加测试传输或真实 socket。

### 11. 判据

- **J1**（`LockRemovalSourceTests.Names`）：加入「T-006 投递（TASK-24）」一组：`GameplayGateAssertion`、`GameplayGateWaitException`、`EnterParticipantsAsync`、`PartyParticipantLease`，以及执行时删到零的其他标识符（例如 `participantGates`）。`PartyDeliveryStream` 不加（待裁定第 5 条）。
- **J2**（`Semaphores`）：删去 `PartyNotificationSlice.cs`、`GameplayAssertions.cs` 两行。删去之后这两个文件出现任何 `SemaphoreSlim` 即违规。
- 类注释：「Of appendix F's delete column only TASK-24's one item is not named here yet…」改为附录 F「删除」栏全部点名。
- 反例：J1、J2 沿用已有的通用反例，不另加。
- 先 RED（只改测试、生产放回基准：J1 命中 `GameplayGateAssertion` 等，J2 报两个文件的信号量数不符）后 GREEN，两次输出进 `logs/task-24-lock-scan.txt`。

### 12. 数量与取证预期

**数量**（逐项目写加法，执行时按实际例数写）：
- **D**：`ProtocolGateAssertionTests` 4 ＋ `PartyTownProjectionDeliveryTests` 门反例 2 ＋ `HostOutputViolationTests` 1 行 ＋ §9 的「删除」（预计 0～2）＋ V13 判定后的关闭失败用例（预计 0～1）。预计 7～10。
- **N**：T1～T16（T8～T11 中 Plan R-5 与 VC-6 可能各多一两条）＋ 新增的 R 用例（换不进真实适配器时，预计 0～4）＋ §7 的 Cera 用例 3～4 ＋ §8 的 3 条。预计 25～32。
- **改写**（方法不增不减）：§9 的「KE 改写」约 6～8 条，§6 的 E 类若干。

**取证**（先写进 `logs/task-24-violations.txt` 再跑）：
- 基准：TASK-23 的 46 条 ＝ `bus-leaked-flow` 2 ＋ `gate-off-executor` 9 ＋ `gate-wait` 4 ＋ `network-wait` 1 ＋ `write-off-executor` 30。其中 `gate-off-executor` 9 ＝ `ProtocolGateAssertionTests` 2 ＋ 正向对照 7；`gate-wait` 4 ＝ `ProtocolGateAssertionTests` 2 ＋ `PartyTownProjectionDeliveryTests` 2。
- 本单元：删去门反例 6 条；正向对照 7 条改为 `write-off-executor`；新增用例不预计产生记录（T、R 都在作业内运行）。
- 预期：46 － 6 ＝ 40 ＝ `bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37（30 ＋ 7）；`gate-*` 0。执行前把精确数字与按类、按 kind 的拆分写进日志开头。

### 13. 变异（Required check 9）

| 变异 | 应当变红 |
|---|---|
| M1 在 `PartyNotificationSlice.cs` 加回一个 `SemaphoreSlim` 字段 | J2 |
| M2 在任一生产文件加回名为 `GameplayGateAssertion` 的类型 | J1 |
| M3 组队进本 XO-1 `catch` 删去 `IsolatePartialCohortAsync` 调用 | R2 |
| M4 同一 `catch` 删去 `RollbackEntry` | R2 |
| M5 两塔进入 `deliveryFailure` 分支删去关闭循环 | R3（进入） |
| M6 两塔 `CloseProjectionFailuresAsync` 不关闭 | R3（`SendToExactAsync`） |
| M7 XO-3 组内交付等级改为 Required | T3 |
| M8 XO-4 组内交付等级改为 Required | T4 |
| M9 怪物死亡提交后 `catch` 不调用 `BlockAsync` | §8 第 2 条线级用例；AC-009 点名用例 |
| M10 祭坛 `Failed` 不转为提交后失败 | §8 第 1 条 |
| M11 清算 `PublicationFailedAsync` 改为直接重抛 | 清算的 KE 证据（执行时指定） |
| M12 `PublishWithRetryAsync` 去掉 CMD 内的第二次组装 | R5 |
| M13 Cera 结果映射把 `Gone` 改回 `unconfirmed` | §7「不在线」用例 |
| M14 Cera 结果映射把 `Failed` 改为 `attempted` | R7 |
| M15 房间机制 `Failed` 不抛 `IOException` | R8 |
| M16 生物逐收件人 `catch` 不关闭失败的收件人 | R9 |
| M17 初始 NOTI `catch` 不 `TryDisconnect` | R11 |
| M18 选角补偿后不投递清理通告 | §8 第 3 条线级用例 |
| M19 配额刷新在 `Failed` 时也记「本期已送」 | R6 |
| M20 正向对照改回不写取证的判定（例如直接 `throw new GameplayOffExecutorException`） | `violations.py` 报某程序集缺正向对照（抽查一个程序集） |
| M21（抽查）D 类任一处恢复 `try/catch` 吞咽 | 预计无用例变红：D 类本来就走不到。如实记录，作为「删除不改变行为」的旁证，不是缺陷 |

每一行至少一项「删掉保留下来的非传输失败处理，对应用例变红」，即提纲的要求；M3～M6、M9～M19 覆盖 KE／KB 的每一类。

### 14. `follow-ups.md` 与处置记录

- **关闭**：F-25（§7）、F-84（§6）。
- **检索「TASK-24」「T-006」的其余结果**（规划时逐条看过）：
  - F-21（T-006，停机排空的兜底拆除保留）：本单元不涉及，留给 TASK-25 的 T-006 收口一并关闭；
  - F-14 的另一半、F-20、F-22～F-24、F-63～F-65、F-83：TASK-23 已关闭；
  - F-61、F-101、F-102、F-104（TASK-25），F-98（T-008），F-97、F-99、F-105（T-009）：不在本单元。触及时遵守：F-102 不修（§9）；F-104 的屏障写法照用（§9）；F-101 所在文件的 `A_use_already_durable…` 一条按 KE 保留。
- **更新**：F-69（本单元的耗时对比）；F-87（本单元之后名字里仍提到门或旧机制的用例，例如 `…_stream_gate_…` 已删，其余列名）。
- **新增**：执行中发现的事项，从 F-106 起编号；待裁定第 1 条若选 A，记一项「AC-011 第 4 条与 RQ-031(d) 措辞已按今天的行为更正」。
- **`t006-test-dispositions.md` TASK-24 一节**：开头写日期、基准与分类（沿用 TASK-23 的 E、R、S、W、F，加 K「门反例，规格消除」与 KE「保留分支的可达性证据」）；数量与加法；删除（每条带承接者或规格消除理由）；改写（旧断言 → 新断言 → 保护意图）；保留（含 §9「KE 保留」改记为 F 类的条目及其对应的 §2 来源）；新增；§3 的附录 E 与附录 G 逐行证据表（Required check 12）。

### 15. 表征测试与金样本

- 表征测试 10 条与全部金样本不变：
  - 本单元删除的 D 类分支在生产中走不到，队伍门在单执行器下从不等待（TASK-21、TASK-22 的门断言已证明），取证源迁移只换类型；
  - VC-6 不在表征范围内；
  - §8 的两条线级用例放在 `Integration/`，只读复用表征宿主，不改 `Characterization/` 目录。
- 有一条表征或金样本需要改，按停止条件 5 处理。

### 16. 生产行为为什么只按附录 G 变化

- **D 类删除**：每一处的 `catch` 只能接到执行器外的程序错误（§4），生产中为 0；删除之后，程序错误不再被吞，其余路径逐字节相同。
- **KE／KB 分支**：可执行语句不动。
- **队伍门**：在单执行器下从不争用；删掉之后，同一条 CMD 内按顺序登记，CMD 之间按完成顺序交付（RQ-032），水位线规则不变（DR-5、DR-17）。
- **取证源迁移**：取证源名称与事件名的字符串值不变，记录的 kind 只在正向对照上由 `gate-off-executor` 变为 `write-off-executor`。
- **GM Cera**：VC-6 授权的变化，取值按待裁定第 2 条。
- **证据**：表征测试与金样本不变；正式全量与 TASK-23 相比只有 §12 记录的增删与改写；取证中反例类与对照以外为 0；变异 M1～M21。

## 待裁定

1. **祭坛：AC-011 第 4 条写「封锁并关闭全部参与者（今天的行为）」，与今天的代码不符**（§1 V7、§8 第 1 条）。
   - 今天：`AltarProjection` 调 `BlockAsync(new(recipient, null), …)`，连接为 `null`，只封锁会话、不登记关闭；入站路径上只有请求者的来源连接按 RQ-031(c) 被关闭，定时路径上没有人被关闭；其余参与者在下一次请求时因封锁而失败、按 DR-15 断线。RQ-031(d) 末句也写了「封锁并关闭全部参与者（`AltarProjection.cs:94-108`）」。怪物死亡才是「封锁并关闭全部参与者」。
   - **建议 A**：证据按今天的行为写（全部封锁；入站时来源连接在其已登记条目之后关闭），把 AC-011 第 4 条与 RQ-031(d) 末句改为「封锁全部参与者，入站时来源连接随 RQ-031(c) 关闭」。这是对 Spec 措辞的事实性更正，DR-4 的保护意图「不让会话继续」仍由封锁满足。生产不改。
   - 备选 B：让祭坛像怪物死亡一样先 `Capture` 参与者连接，`BlockAsync` 逐个登记精确关闭。行为与 Spec 字面一致，但这是对今天行为的改变，Spec 里没有授权它的附录 G 条目，需要同时补一条。
   - 改 Spec 或改行为都要裁定。
2. **GM Cera（VC-6）的取值与合并规则**（§7）。`reminder` 是 GM HTTP 接口的响应字段，取值集合是对外契约（`gm-admin` 页面不读它，只有测试桩构造，检索为证）。
   - **建议 A**：`attempted` 保留字符串、含义改为「已提交发送」（同裁定第 4 条改义不改名）；新增 `offline`：本批至少一个目标账号没有在世界中的会话（含 `Gone`）；`unconfirmed` 只剩组装失败（Plan R-5 已定）与 HTTP 层提醒异常（`GmCeraEndpoints` 不改）；优先级 `unconfirmed` ＞ `offline` ＞ `attempted`。注意：今天「部分账号不在线」给 `attempted`，按 A 会变成 `offline`，这正是 VC-6 允许的变化。
   - 备选 B：只有「本批没有任何账号登记到提醒」时才给 `offline`，部分不在线仍为 `attempted`。对现有结果改动更小，但「不在线」只在全员离线时才说出来。
   - 备选 C：`attempted` 改名为 `submitted`。与 VC-6 的中文措辞对得更直，代价是改一个对外字符串，且与裁定第 4 条的处理方式不一致。
3. **AC-011 两条线级回归的降级授权**（§8 第 2、3 条）。
   - 计划在表征宿主上构造：怪物死亡用编码接缝注入（不改生产）；选角补偿要让请求者落在与同伴共享在场的区域，规划时尚未确认现有宿主能不能不改生产代码做到（§1 V10）。
   - **建议**：预先授权：若任一条在现有宿主上必须改生产代码才能构造，改为「执行器 ＋ 真实登记适配器 ＋ 真实 `OutboundSender`（测试传输）」的发送端层证据，断言同样的封锁、关闭与清理通告条目，并在 Result 与收口中登记为 AC-011「线级」一词的偏离。
   - 备选：不授权，遇到即按停止条件 8 停下回报。
4. **提纲中「删除 DR-1～DR-3 的发送失败分支」与本单元约束冲突**（§0、§2）。
   - 那些分支翻转后仍可由登记点的编码失败进入（裁定第 1 条备选 B 保留了「编码失败在调用点抛出」），按「仍可能由非传输失败触发的分支一律保留」不删；本单元只删关连接失败的吞咽（§4 的 10 处）。TASK-23 Result 把 29 条过渡期用例记为「只证明死代码」，本单元按 §9 改记。
   - **建议接受**。不改 Spec：附录 E「新的承接方式」说的是传输失败怎样承接，没有要求删除编码失败的处理；这是对提纲的修正，在 Result 中登记。
5. **`PartyDeliveryStream` 类名保留，J1 不加它**（§5、§11）。
   - 提纲写「J1 加入 `PartyDeliveryStream`、`PartyParticipantGate`、`GameplayGateAssertion`」。删门之后这个类仍承载水位线、当前队伍 ID、成员集合与墓碑（DR-5 要求水位线「刷出时推进」），类名删不到零；`PartyParticipantGate` 只是一个字符串参数，随取门调用一起消失。附录 F 删除的是「`PartyDeliveryStream.Gate`」这个成员，不是这个类。
   - **建议**：J1 加 `GameplayGateAssertion`、`GameplayGateWaitException`、`EnterParticipantsAsync`、`PartyParticipantLease`（及执行时删到零的其他名字）；门成员由 J2（两文件 `SemaphoreSlim` 为 0）与 AC-017 检索式（`\.Gate\b`、`participantGates`）守。
   - 备选：把这个类改名（例如 `PartyProjectionWatermark`）再把旧名加入 J1。名字更贴切，代价是在删门的同一处做一次跨 3～4 个文件的机械改名。
6. **过渡期用例的驱动改写放在哪个单元**（§9）。
   - **建议**：本单元只对每个保留分支改写（或新增）一条经真实适配器的可达性证据；其余注入抛异常替身、仍对应真实登记点异常的用例不改，改记为 F 类，驱动方式随 TASK-25 的替身改写一起换。这样 TASK-25 的输入清单（`logs/task-23-doubles.txt`）不变，只是多了一栏说明。
   - 备选：29 条在本单元全部换入真实适配器。证据更整齐，代价是改动量约翻倍，并与 TASK-25 在同一批文件上重叠，`diffcheck` 难以分清「语义承接」与「驱动替换」。

## 主控方裁定（执行前）

0. **不拆分，接受**；按计划，若开始时 D 类候选超过 20 处或夹具改造量明显超出，停下回报。
1. **祭坛与 AC-011 第 4 条：采用 A。** Spec 对「今天的行为」描述有误：代码只封锁、不关闭。AC-011 第 4 条与 RQ-031(d) 末句改为「封锁全部参与者，入站时来源连接关闭」，属事实性更正，生产不改。Owned boundary 增加 `spec.md` 这两处。
2. **GM Cera（VC-6）：按 VC-6 字面取值，不保留「未确认」。**
   - 结果只有两种：`attempted`（保留字符串，含义改为「已提交发送」）与 `offline`（本批任一账号不在线即返回）。
   - 组装失败与 HTTP 层异常不再折成 `unconfirmed`，而是作为错误照常上抛。它们是程序错误，如实报错比返回一个模糊的提醒值更准确。
   - `gm-admin` 页面不读这个字段；对外 HTTP 字段的取值变化在 Result 中登记，交外部审核时说明。若读码发现有别的消费者依赖 `unconfirmed`，停下回报。
3. **AC-011 两条线级回归的降级，预先授权，接受。** 若在表征宿主上必须改生产代码才能构造，降为「执行器 ＋ 真实适配器 ＋ 真实发送端（测试传输）」层的证据，在 Result 中登记为偏离。
4. **DR-1～DR-3 的发送失败分支保留，只删 10 处关闭吞咽，接受。** 依据：这些分支仍可由编码、端点或参数校验失败进入。TASK-23 Result 中「只证明死代码」的标注由本单元在 `t006-test-dispositions.md` 与 `follow-ups.md` 中更正，不改已提交的 TASK-23 Result。
5. **`PartyDeliveryStream` 类名不进 J1，接受。** 门成员由 J2 与 AC-017 检索式守住。
6. **过渡期用例：每个保留分支只改写或新增一条可达性证据，其余改记为 F 类，驱动替换交 TASK-25，接受。**

## Result（第 1 轮，合同 Revision 1，2026-10-23，基准 `50d7bce4`，未提交）

### Outcome

done（待独立结果审查，Result review：required）。

- 可达性判定：§1 V2 的检索在基准上找到 137 处（`catch` 106 ＋ 结果读取 31），逐处归类为 D 10 ＋ KB 39 ＋ KB+KE 35 ＋ KE 53 ＝ 137，未分类 0（`logs/task-24-sites.txt`）。
- D 类 10 处关连接吞咽全部删除，调用保留；KE／KB 的可执行语句不动，只改两句触发源已不成立的注释。
- 队伍两把投递门、`GameplayGateAssertion` 与 `GameplayGateWaitException` 删除；取证源挪到 `GameplayAssertionDiagnostics`，字符串值不变。
- GM Cera 按裁定第 2 条只返回 `attempted`／`offline`，组装失败与 HTTP 层提醒异常照常上抛。
- `spec.md` 按裁定第 1 条改 AC-011 第 4 条与 RQ-031(d) 末句两处。
- 附录 E DR-1～DR-17 每行都有经执行器与发件箱驱动的证据，VC-3、VC-4、VC-6 各有用例（`t006-test-dispositions.md` TASK-24 §6）。
- 正式全量 8229 条（＝ 8191 ＋ 45 － 7），失败 3 ＝ 那 2 条 `DimensionDungeonTests` ＋ 1 条打包进程用例在满载下的就绪等待超时（已取身份，隔离全过，Checks 2）；跳过 18；表征 10 通过、0 跳过；取证 40 条，反例类与对照以外 0。
- 停止条件都没有触发；与计划或裁定不同之处见 Why 2～9。

### Changed

- **生产代码**（15 个文件，增 191 行、删 389 行；Abstractions 1 ＋ Application 2 ＋ GmWeb 1 ＋ Protocol 11 ＝ 15，`logs/task-24-boundary.txt`）：
  - **D 类删除**（§4 D1～D10）：`BossTowerPartyCoordination.cs`、`IllusionTowerPartyCoordination.cs`、`BossTowerProjectionDelivery.cs`、`IllusionTowerProjectionDelivery.cs`、`DungeonEntryProjectionDelivery.cs`、`ReturnToSelectionHandler.cs`、`SelectCharacterHandler.cs`、`CreatureRuntimeDelivery.cs`、`GameLoginHandler.cs`（`close_failed` 日志分类随之消失）、`Application/Commands/SelectedSessionSafety.cs`。每处只删 `try/catch` 外壳。
  - **注释**（§3）：`DungeonEntryProjectionDelivery` XO-1 循环 `catch` 上方「TCP cannot retract…」改为登记点失败；`ReturnToSelectionHandler` 的「partial-delivery failure」改为「a reply that could not be registered」。
  - **队伍两把门**（§5）：`PartyNotificationSlice.cs` 删 `participantGates`、`EnterParticipantsAsync`、`PartyParticipantLease`、`PartyDeliveryStream.Gate`、两处取门与 `try/finally`、只喂给取门的 `PlanParticipants`；`PartyTownProjectionDelivery.cs` 删两处取门、两处流门与 `try/finally`，以及只为取门计算的观察者集合。「一个流让四阶段投影连续」的注释改为 RQ-032 的理由。水位线、当前队伍 ID、成员集合与墓碑不动。
  - **取证源**（§6）：`Abstractions/Execution/GameplayAssertions.cs` 新类型 `GameplayAssertionDiagnostics`（`DiagnosticSourceName`、`ViolationEvent`、internal `Report`），三个断言改调它；删 `GameplayGateAssertion` 与 `GameplayGateWaitException`。
  - **GM Cera**（§7，裁定第 2 条）：`GmCeraReminderDomainService` 没有会话、`Gone` 或 `Declined` 记 `offline`，任一 `Failed` 在其余账号提醒完之后抛出；`GmWeb/GmCeraEndpoints.cs` 把提醒挪到发放的 `try/catch` 之外，异常照常上抛。
- **Spec**：`spec.md` RQ-031(d) 末句与 AC-011 第 4 条（全文在 `logs/task-24-boundary.txt`）。
- **测试**（`t006-test-dispositions.md` TASK-24 一节）：
  - 变动的已有文件 20 个（Application 3 ＋ Host 2 ＋ Networking 2 ＋ Protocol 11 ＋ `tests/Support` 2），其中修改 18、删除 2（`Protocol.Tests/Execution/ProtocolGateAssertionTests.cs`；`Protocol.Tests/Outbox/XoDelivery.cs` 移到 `tests/Support/XoDelivery.cs`）；新建 8 个（Networking `Integration/PostCommitFailureWireTests.cs`；Protocol `Game/Altar/AltarProjectionFailureTests.cs`、`Game/GmCeraReminderTests.cs`、`Game/InitialConnectionDeliveryFailureTests.cs`、`Game/ReturnToSelection/ReturnToSelectionDeliveryFailureTests.cs`、`Game/RoomMechanismProjectionFailureTests.cs`、`Game/TitleProjectionOrderTests.cs`；`tests/Support/XoDelivery.cs`）；两个 csproj 各加一行链接。
  - N 45、D 7、改写 4（R2、R9、W1、V6）、E 类若干。
- **文档与证据**：`verification/t006-test-dispositions.md` TASK-24 一节；`verification/follow-ups.md`（关闭 F-25、F-84；更新 F-69、F-87；新增 F-106～F-110）；`verification/probe-tools/violations.py` 的用法示例（类名已过期）；`verification/logs/task-24-*.txt` 11 份（verify、sites、lock-scan、build-test、violations、diff-check、new-x10、mutations、boundary、isolated、dr-evidence）。

### Why

1. **停止条件**：
   - 1：V1 在基准上每个接缝只有一个生产实现（`OutboxNetworkMessageSender`、`OutboxConnectionCloser`、`GameMessageSender`、`EventService`、`NotificationBus`，`logs/task-24-verify.txt`），137 处的被 try 调用都经由它们，没有 §2 之外的发送器、装饰器或注册。10 处 D 类都在 §4 的形状里：删 `try/catch` 外壳、调用保留，不改 CMD 的结果。
   - 2：V2～V14 与计划一致（V4 10 处，令牌全是 `CancellationToken.None`；V5 取门点与计划逐行相同；V7 祭坛只封锁、不登记关闭；V11 七个配额刷新各自判定 `Delivered`、不共用代码；V12 见 Why 7）。有一处计划 §2 表没写到的来源：`GameplayOutbox.EnsureWritable` 对「已经刷出的发件箱」抛 `InvalidOperationException`。它和执行器外一样是程序错误（只有在作业结束钩子之后还登记才会出现），不改变任何一处的归类，所以没有按停止条件 2 停下；请结果审查确认这个判断。
   - 3：生产改动只在 Owned boundary 列出的文件里；`GmWeb/GmCeraEndpoints.cs` 的改动由裁定第 2 条授权（Owned boundary「除非待裁定第 2 条另有结论」）；`spec.md` 只有裁定第 1 条授权的两处；`plan.md` 未改。
   - 4、5：第一次正式全量没有新失败；记录的那次多出 1 条 `LauncherHttpHostStartupTests.DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener`，是 AGENTS.md 记的打包进程用例在满载下的就绪等待超时，取了身份、隔离全过（Checks 2），本单元没有碰它的路径，所以没有按停止条件 4 停下。取证没有名单外记录；表征测试与金样本未改。
   - 6：29 条过渡期用例没有删除；AC-009 点名用例的注入点是运行时认领发布被拒（KB），断言行多重集合不变（`logs/task-24-diff-check.txt`）。
   - 7：附录 E 每一行写传输失败证据时，今天的行为都等于「新的承接方式」或附录 G 已授权的变化。
   - 8：AC-011 线级第 1 条在表征宿主上不改生产代码即可构造；第 3 条需要生产改动，按裁定第 3 条降级（Why 4）。
2. **GM Cera 的取值与上抛方式**（裁定第 2 条）：
   - `Declined` 也记为 `offline`：钱包组装器只在「该账号此刻没有在世界中的当前会话」时弃权（会话不匹配、被替换、不在世界中），这正是「不在线」；不这样记，就会对没有发出任何东西的账号说「已提交发送」。
   - 组装失败在其余账号提醒完之后再抛：一个账号组装失败不应让同批其他在线账号收不到提醒。
   - 提醒挪到发放的 `try/catch` 之外：留在里面，提醒异常会被当成发放失败，记一条 `RolledBack` 审计并返回「发放已回滚」，而发放其实已经提交。挪出之后，提醒异常经 GM CMD 照常失败，HTTP 500；审计只有 `Succeeded`，同编号重放不重复入账（`GmCeraHttpTests` V6）。对外字段 `reminder` 的取值变化登记 F-107。
3. **R1、R11 是新增而不是改写**：`SelectCharacterLifecycleTests` 与 `OnlineUserServiceTests` 在 Application.Tests 里，原用例的注入点都是直接收件人（真实适配器在登记点仍会抛），按 §9 规则改记 F 类；R1 用夹具新增的 `gameSender`／`connectionCloser` 参数接到 `XoDelivery`，R11 放在 Protocol.Tests（初始通知的观察者在 Protocol，Application.Tests 够不到它的 internal 发送器）。
4. **AC-011 线级第 3 条降到发送端层**（裁定第 3 条）：在表征宿主上，请求者第一次选角、走进共享区域 1:1、回选角再选角，第二次仍落在不共享在场的城镇门口（实测：同伴在第二次选角期间只收到栅栏帧），这是普通频道的登录落点规则，不改生产代码就无法让同伴在场。降级后的用例经真实执行器、真实登记适配器、真实 `CleanupNotificationDelivery`（按名字创建，它是 Protocol 的 internal）与发送端，断言同伴的清理通告已登记并交付、请求者在其后被关闭；登记 F-108，交 TASK-25 收口时作为 AC-011「线级」一词的偏离。
5. **AC-011 线级第 1 条的构造**：144 的起始房间没有怪物（实测 START_MAP 86 字节，按序号 1～255 报告击杀全部只得到空确认）。用例借表征宿主的 `Decorate` 动作做了两件事，都不在 `Characterization/` 目录里、也不改生产代码：把 `IOutboundMessageEncoder` 换成「对第一条有主人的死亡通知抛出」的包装（计划 §8 第 2 条、§10 指定的做法；表征宿主自带的 `Decorate<TService>` 助手只接受 `*DomainService`，所以这里直接替换注册）；把 `IDungeonRunDomainService` 原样留下、只记住实例，以便测试线程读出相邻房间里活着的怪物序号，再经 MOVE_MAP 走过去击杀。会话被封锁由记录 `Fence` 调用的直通代理观察（`ISelectedSessionDomainService` 以工厂注册，代理不另造实例）。
6. **`XoDelivery` 移到 `tests/Support/`**（计划 §10 允许）：Application.Tests 里的 R1、T1 与 AC-011 第 3 条要用它。`GameMessageSender` 是 Protocol 的 internal，改为按名字创建，两个项目用同一份实现。`RunFromAsync` 在作业开始时先打开发件箱，与两个 dispatcher 相同：第一次写 R11 时没有这一步，失败作业的来源没有被关闭——执行器只在发件箱挂上结束钩子时才登记来源关闭。生产中带来源的作业都由 dispatcher 产生，所以成立，但这条不变量只靠约定，登记 F-110。
7. **DR-16 的路径**（V12）：实时称号投影由 `AchievementEventSubscriber` 经 `ProjectAsync` 发出，是 LiveOnly 订阅，派发时是执行器上单独的一个作业。T15 用一个分两段登记、中间等待的作业代表进入序列，在它运行时把称号事件排进执行器，断言称号消息排在整个进入序列之后交付。进入序列本身不是选角处理器，这是对计划「选角 CMD」的简化：要证明的是 RQ-032 的「CMD 之间按完成顺序」，与是哪条 CMD 无关。
8. **第一轮 ×10 暴露的竞争**：发送端先报告失败条目的结果、再在分离的任务里关闭连接，用例在 `SettledAsync` 之后立刻读 `XoTransport.IsClosed` 会偶尔读到假（Protocol 4／10 轮、共 5 次：`InitialConnectionDeliveryFailureTests` 的 T14 两次、`RoomMechanismProjectionFailureTests` 的 T11 两次、`UsageQuotaProjectionTests` T9 的 Altar 行一次）。`XoDelivery` 加 `ClosedAsync` 等待关闭，本单元自己的 24 处正向关闭断言改用它；TASK-23 的用例（例如 `A_follower_over_the_limit_is_closed_and_the_requesting_leader_is_still_entered`）读的是超时关闭，没有改。改后重跑 ×10、正式全量与隔离。
9. **其余与计划不同之处**：
   - `PartyTownProjectionDelivery.RefreshMovedPartyAsync` 删门之后，`candidate` 与 `current` 是紧挨着的两次 `FindParty`；去掉一次属于重构，本单元不做。`RefreshObservedPartiesAsync` 的 `representedSessions` 参数只剩空值校验，接口不改，登记 F-109。
   - D 类删除之后，执行器外的程序错误不再被这些 `catch` 吞掉；D9 的 `close_failed` 日志分类随之消失。
   - 29 条过渡期用例删除 0 条（计划预计 0～2）：`PvpPeerFailure_IsolatedAfterReadyAndDoesNotPreventTrailingLocks` 的「目标失败」注入在替身里是空操作，用例实测的是成功路径与 PvP 公示人群，保留，名字登记 F-87。
   - `--list-tests` 在基准上列出 8128 条（Theory 的不可序列化数据行合并显示），TASK-23 记的 8191 是实际运行数；本单元的加法按实际运行数写。
   - 日志信号 `GameplayGateWait` 为 1，不是 0：唯一一行是判据 J1 的输出「rule GameplayGateWaitException scope 'Server/src': 0 hits」，不是违规。
10. **TDD 的口径**：判据（J1 新名称、J2 删两行）走了 RED → GREEN（`logs/task-24-lock-scan.txt`）。其余新用例是在生产改动之后写出的证据，区分力由变异 M3～M19 证明，不是 RED。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 警告、0 错误（两次正式全量之前各一次，`logs/task-24-build-test.txt`；第二次在变异系列之后，即从还原后的源码整体重建）。
2. **正式全量**（`logs/task-24-build-test.txt`）。一共跑了两次：第一次之后，×10 暴露了 Why 8 的竞争、改了 24 处断言，所以记第二次，即最终代码上的那次（2026-10-23T05:28:21Z～05:31:21Z，测试部分 174 秒，开始时 swap 已用 10717 MB；只作信息）。
   - **总数**：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1744 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 179 ＋ Application 4535 ＋ Host 190 ＝ 8229 ＝ 8191 ＋ 45 － 7。
   - **N ＝ 45**：Application 3 ＋ Networking 1 ＋ Protocol 41；**D ＝ 7**：Protocol 6 ＋ Host 1。逐条名单与加法见 `t006-test-dispositions.md` TASK-24 §1、§2、§4。
   - **逐项目**：Application 4532 ＋ 3 ＝ 4535；Networking 178 ＋ 1 ＝ 179；Protocol 1709 ＋ 41 － 6 ＝ 1744；Host 191 － 1 ＝ 190；其余四个项目不变。
   - **结果**：通过 54 ＋ 80 ＋ 1744 ＋ 477 ＋ 968 ＋ 179 ＋ 4517 ＋ 189 ＝ 8208；失败 3 ＝ 那 2 条 `DimensionDungeonTests` ＋ `Host.Tests/LauncherHttpHostStartupTests.DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener` 1；跳过 2（Infrastructure）＋ 16（Application）＝ 18，不变；8208 ＋ 3 ＋ 18 ＝ 8229；挂起转储 0。
   - **Host 那 1 条的身份**：`TaskCanceledException`，栈顶是 `WaitForTcpListenerAsync`（打包进程的监听就绪等待，20 秒）；就是 AGENTS.md 记的「拉真实进程的用例在满载全量下会假红」的形状。单独跑 5 次全过（各 4 秒）、整类 15 条跑 3 次全过，第一次正式全量与 Host 隔离复跑也都通过（`logs/task-24-build-test.txt` 文首）；本单元没有改 Host、HttpApi 与这条用例的任何路径。所以没有按停止条件 4 停下。
   - 第一次正式全量：8229 条，失败只有那 2 条 `DimensionDungeonTests`，跳过 18。
   - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本无改动（`logs/task-24-boundary.txt`）。
   - **日志信号**（`t19/analyze.py`）：应为 0 的七项仍为 0；`direct send deadline exceeded` 0；`GameplayGateWait` 1（只是判据 J1 的输出行，Why 9）；`GameplayLeakedFlow` 1（`HostOutputViolationTests` 的显示名）。
     - `Gameplay bus:… failed` 8，与 TASK-23 逐项相同；
     - `inbound:… failed` 8 ＝ TASK-23 的 7 ＋ `GameMonsterDeath` 1（AC-011 线级用例 W2 注入的失败）；
     - `gm:… failed` 4 ＝ TASK-23 的 2 ＋ `POST /api/gm/v1/cera/grant` 2（改写的 V6：提醒抛出两次，各一次 GM CMD 失败）；
     - `Outbound connection … closed` 7 ＝ `Failed` 6 ＋ `TimedOut` 1，与 TASK-23 相同（本单元的发送端证据用 `XoDelivery`，没有这条日志）；
     - 「the drain tears it down」1，与 TASK-23 相同（第一次正式全量为 2）：信息级，停机时排空兜底拆除仍登记着的连接（F-21 保留的行为）。
   - **F-69**：与 TASK-23 的正式全量相比，多出 ≥ 9 秒的 6 条（+9～+15 秒；Host 3 条、Application 3 条；开跑时 swap 已用 10717 MB）。隔离下：`LauncherHttpHostStartupTests.DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener` 4 秒（全量中 20 秒是它撞上就绪等待预算而失败，见 `logs/task-24-build-test.txt` 文首）、`GmCeraHttpTests.BoundariesMissingFieldsUnknownTypesAndCorruptWalletNeverCredit` 4 秒、`GmCeraHttpTests.ConcurrentDuplicateBatchSurvivesRestartAndReplaysAfterAccountDeletion` 4 秒、`DespairTowerTests.Daily_reset_reopens_free_entry_and_preserves_the_next_floor` 1 秒、`SaoTowerDomainTests.Real_entry_resolves_shared_floor_charges_one_ticket_and_rollback_preserves_inventory` 3 秒、`SealedTreasureTests.Production_factory_materializes_all_25_maps_and_both_platinum_packages` 11 秒（TASK-23 全量 9 秒，本来就慢）；判读同 TASK-22（满载拉长，不是排空）。
3. **零违规取证**（`logs/task-24-violations.txt`，预期在运行前写在日志开头）：40 ＝ TASK-23 的 46 － 6（`ProtocolGateAssertionTests` 4 ＋ `PartyTownProjectionDeliveryTests` 2）；按 kind：`bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37 ＝ 40（37 ＝ 30 ＋ 7 个改了 kind 的正向对照）；`gate-off-executor`、`gate-wait` 0。反例类与对照以外 0，没有测试帧 0，`exe:` 0，七个程序集各一条对照，退出码 0；两次正式全量相同。
4. **判据与检索**（`logs/task-24-lock-scan.txt`）：
   - RED（测试已改、生产在基准）：J1 有 20 个命中 ＝ `GameplayGateAssertion` 10 ＋ `GameplayGateWaitException` 2 ＋ `EnterParticipantsAsync` 4 ＋ `PartyParticipantLease` 2 ＋ `participantGates` 2；J2 报 `GameplayAssertions.cs`、`PartyNotificationSlice.cs` 两个文件的信号量不在名单上；三支各扫描 1565 个文件。
   - GREEN：14 条全过，三支各扫描 1565 个文件。
   - AC-017 检索式一：`Server/src` 只有 8 行，都是名字相同、含义无关的 `.Gate` 成员（`TownAreaKind.Gate` 枚举成员 3 行、`DropRules.Gate(...)` 3 行、城镇定义的到达门属性 `.Gate` 2 行），没有一行在 `Protocol/Game/Party` 下；检索式二：0 行。附录 F「删除」栏其余各项：TASK-23 的检索式 0 行，TASK-21、TASK-22 的名字由 J1 守着（GREEN）。
5. **可达性清单**（`logs/task-24-sites.txt`）：基准 137 行、未分类 0；加法在日志开头。结束时同一检索式 127 行 ＝ 137 － 10，D 类 10 处无一残留，KE／KB 全部仍在。
6. **基准核实**（`logs/task-24-verify.txt`）：开始时在基准、结束时在工作区各跑一次；与 §1 的差异只有 Why 1 第 2 点的「已刷出的发件箱」来源。
7. **测试改动核对**（`logs/task-24-diff-check.txt`）：方法只少了 6 个（D 中 K1 4 ＋ K2 2）、只多了 33 个（＝ N 45 例：31 个 Fact ＋ 2 个 Theory × 7 行；31 ＋ 14 ＝ 45），Host 的 D 1 是 InlineData 一行、方法不变；断言行多重集合有变化的 13 个文件逐个对到 `t006-test-dispositions.md` 的一行：13 ＝ 有新增方法的已有文件 9（其中 `PartySelectDungeonHandlerTests` 含改写 R2、`CreatureProjectionDeliveryTests` 含改写 R9）＋ 只有改写的 `GmCeraHttpTests`（V6）1 ＋ 删除 K1、K2 的两个文件 2 ＋ 正向对照 E3 1。`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`：断言行 8 条，多重集合不变，方法体逐字相同（AC-009）。
8. **新增与改写测试 ×10**（`logs/task-24-new-x10.txt`）：每轮 Application 18 ＋ Protocol 49 ＋ Networking 2 ＋ Host 7 ＝ 76 条，10 × 76 ＝ 760 次通过、0 失败、0 跳过（含线级 W2、W1，在 Networking 隔离下）。第一轮（改 `ClosedAsync` 之前）Protocol 4／10 轮共 5 次失败，见 Why 8。
9. **变异核对**（`logs/task-24-mutations.txt`）：M1～M21 每项都有指定用例变红（M20、M21 为抽查），逐项在日志开头。三轮共 27 次还原，`cmp` 全为 0，每轮前后 `git diff -- Server/src` 的 sha1 都是 `58db2f42…`；第二次正式全量的 `--no-incremental` 构建即变异之后的整体重建。要说明的五处：
   - M9（删怪物死亡提交后 `catch` 的封锁）只让线级 W2 变红，AC-009 点名用例保持绿：它注入的认领发布被拒让 `ProcessMonsterDeathCommand` 成为结果不确定的提交，走的是更早的那个分支（`MonsterDeathSlice.cs:440-445`）。补做 M9b（删那个分支的封锁），AC-009 用例变红；
   - M16 第一轮的写法（删关闭调用）编译不过（CS9113，关连接器参数不再被读），测试跑的是旧二进制；第二轮改为「关闭所有者而不是失败的收件人」，R9 变红；
   - M18 第一轮改的是 `SelectCharacterPhaseFailure` 分支，而本单元的降级用例走的是通用 `catch` 与补偿命令，所以保持绿；第二轮改补偿命令之后的清理投递，用例变红。属于选错了变异位置，不是缺口；
   - M20 第一轮的 lambda 有二义性、编译不过；第二轮正向对照不再写取证，`violations.py` 报 Protocol.Tests 没有取证文件、对照 0 条 [MISSING]，退出码 1；
   - M21（抽查）恢复 D10 的 `try/catch`：63 ＋ 1 条全过，没有用例变红，与「D 类本来就走不到」一致。
   - M3、M4 另让 F 类的 `A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town` 变红；M7、M8 另让 XO-3、XO-4 的慢成员用例变红。
10. **生产改动边界**（`logs/task-24-boundary.txt`）：15 个文件，全在 Owned boundary 列出的文件里（逐文件对到 `task-24-sites.txt` 的 D 行、§5／§6／§7 条目或 §3 的注释句）；`Server/src` 下没有未跟踪文件；`Networking`、`Persistence`、`Infrastructure`、`HttpApi`、`Host` 的生产代码、`NotificationBus.cs`、`Web/gm-admin`、`plan.md`、`AGENTS.md`、`Docs/` 无改动；`spec.md` 只有裁定第 1 条的两处，全文在日志里。
11. **隔离复跑**（`logs/task-24-isolated.txt`，最终代码）：Application 4535 条（失败只有那 2 条已知的，跳过 16），取证 31 ＝ 24 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 对照 1；Protocol 1744 条全过，取证只有对照 1（TASK-23 为 7，少的 6 条就是删掉的门反例）；Networking 179 条全过（隔离 43 秒），取证 4 ＝ 2 ＋ 1 ＋ 对照 1；Host 190 条全过，取证只有对照 1；四个项目反例类与对照以外都是 0。
12. **附录 E 与附录 G 逐行证据表**：写在 `t006-test-dispositions.md` TASK-24 §6；`logs/task-24-dr-evidence.txt` 检索了表中 51 个用例名与类名，全部找到（找不到 0）；TASK-23 给出的 DR-10、DR-13、DR-14、DR-15 的用例复核仍在。
13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动或删除的 40 个已跟踪文件在基准与工作区都没有 CRLF，行尾无变化（`logs/task-24-boundary.txt`）。本单元没有加负载，`pgrep -x yes` 为 0；结束时执行了 `dotnet build-server shutdown`。

### Remaining

- **需要结果审查确认**：Why 1 第 2 点（§2 表之外的「已刷出的发件箱」来源不改变归类）；Why 2（GM Cera 的 `Declined` 记 `offline`、提醒挪出发放的 `try/catch` 后 HTTP 500）；Why 4（AC-011 线级第 3 条降级）；Why 5（线级第 1 条借宿主 `Decorate` 动作替换编码器、记住运行服务）；Why 7（T15 的进入序列用占位作业代表）；Why 8（`ClosedAsync` 与改动的 24 处断言）。
- **交 TASK-25**：
  - 18 条 F 类过渡期用例的驱动替换（抛异常的替身 → 登记点注入），清单 `t006-test-dispositions.md` TASK-24 §5；替身清点 `logs/task-23-doubles.txt` 不变；
  - AC-011「线级」第 3 条的偏离（F-108）与附录 E／G 逐行证据表（TASK-24 §6）作为 AC-014、AC-017 收口对账的输入；
  - F-61、F-101、F-102、F-104 照旧。
- **交 T-008**：F-109（`representedSessions` 只剩校验）、F-110（失败作业关闭来源依赖作业里已打开发件箱）、F-87 的改名一半、F-98。
- **交外部审核**：F-107（GM Cera 接口 `reminder` 取值与提醒异常时的 HTTP 状态变化）。
- **测试基础设施**：打包进程用例在满载下的一次假红（本次是 `LauncherHttpHostStartupTests.DisabledHttpApi_AllowsTwoIsolatedHostsWithoutAHiddenKestrelListener`，同 F-55／F-103 的形状），没有新登记。
- **评审发现 3（只作记录）**：D6、D7 两处删掉吞咽分支之后，关闭请求若抛出，原始失败可能被关闭异常取代（日志里看到的是关闭异常而不是原始失败）。能走到这里的只有执行器外的程序错误（翻转之后关闭请求只登记进发件箱，见 Detailed Plan §2），所以不改。
- **评审发现 4（只作记录）**：T15（`TitleProjectionOrderTests.A_title_projection_queued_during_an_entry_is_delivered_after_the_whole_entry`）的证明边界：它用占位作业代表进入序列（Why 7），证明的是「进入作业期间入队的称号投影在整个进入之后送出」；**不证明**真实选角序列中称号事件在什么时候入队。

### 评审后修改（按文末主控方处置第 1～3 条，2026-10-23，未提交）

1. **发现 1**：`Server/src/Dfo.Server.GmWeb/GmCeraEndpoints.cs` 的 `Grant`，发放提交之后的提醒调用包进自己的 `try/catch`：提醒抛出时记 Error 日志（`Committed GM wallet reminder failed.`），返回 `Error(500, "点券已到账，提醒未能发出；勿新建编号重发。", "unknown")`，与本端点其余 500 同形。提醒仍在发放的失败处理之外，已提交的发放不会被记成回滚。
   - 用例：改写 V6 `Server/tests/Dfo.Server.Host.Tests/GmCeraHttpTests.cs` 的 `CommittedGrantSurvivesReminderExceptionAndProjectionReadsCurrentWallet`，断言 500、`message` 等于该文字、`outcome` 为 `unknown`、审计只有一条 `Succeeded`、`gm_cera_operations` 1 行、钱包 60；同编号重放（测试先把钱包直接改成 59）仍是同一个 500，钱包仍是 59、操作行仍是 1（只入账一次）。先红后绿：生产代码未改时红（实际得到外层的「服务器暂时无法处理请求。」），改后绿（`logs/task-24-review.txt` 第 1、2 部分）。
   - F-107 补写了响应体与前端行为（`verification/follow-ups.md`）。前端只按 `outcome` 与状态码判断、不显示响应体文字：保留待定记录与原编号，提示是前端自己的「结果暂未确认」；所以处置里「GM 能读到『已到账』」今天只在响应体里成立，界面上看不到。前端没有改。
2. **发现 2**：`Server/tests/Dfo.Server.Protocol.Tests/Game/GmCeraReminderTests.cs` 新增 Theory `An_account_whose_session_the_reminder_cannot_reach_makes_the_result_offline`，2 行：
   - `gone`：账号有选中会话但不在世界中（`ReminderWorld.SelectedOnly`，账号 8304），通知总线报 `Gone`；
   - `declined`：组装方没有可显示的钱包（`Wallets.Missing`，读到 null），通知总线报 `Declined`。
   - 两行都断言结果为 `offline`、在线账号仍收到 `CeraBalanceUpdateMessage`、够不着的那个连接上没有登记任何消息。
   - 与处置措辞的一处差异：代码里「会话已被替换（连接已换）」报的也是 `Gone`（`INotificationBus.cs` 的注释），`Declined` 是「组装方对这个收件人没有东西可发」。所以 `gone` 一行同时代表「不在世界中」与「已被替换」，`declined` 一行用钱包缺失制造。生产代码 `GmCeraDomainService.cs:31` 没有改。
   - 变异 M22（`t24/mutate.py` 第 4 轮）：删掉第 31 行这个分支（`Gone`／`Declined` 不再记 `offline`，结果保持 `attempted`）→ 6 条中通过 4、两行都红；还原后 `cmp` 为 0，`git diff -- Server/src` 的 sha1 前后都是 `c97f03aa…`（`logs/task-24-review.txt` 第 3 部分）。
3. **发现 3、4**：写入上面的 Remaining。

**数量变化**：Protocol 1744 ＋ 2 ＝ 1746，合计 8229 ＋ 2 ＝ 8231，N 45 ＋ 2 ＝ 47（`t006-test-dispositions.md` TASK-24 §7）。没有重跑正式全量；Checks 第 2 条记的是评审前的那一次。

**检查**（`logs/task-24-review.txt` 第 4 部分）：
- Release 构建 `--no-incremental`：0 警告、0 错误。
- GmWeb 没有自己的测试项目，GM Cera 端点用例在 Host.Tests：`FullyQualifiedName~GmCera` 18 条全过（`GmCeraHttpTests` 17 ＋ `GmCeraBrowserFixtureTests` 1）。
- Protocol.Tests `FullyQualifiedName~GmCera`：6 条全过（原 4 ＋ 新 Theory 2 行）。
- `git -c core.whitespace=cr-at-eol diff --check`：无输出，退出码 0；改动的文件都是 LF，没有 CRLF 引入。
- 本次没有加负载，`pgrep -x yes` 为 0；结束时执行了 `dotnet build-server shutdown`。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-24-result-review.md`），4 条发现都不阻断。提交前处理：

1. **发现 1（HTTP 500 的对外表现）：采用评审给的加固写法。** `GmCeraEndpoints` 在发放已提交之后，提醒若抛出：记日志，返回与本端点其余 500 同形的 JSON 错误体 `Error(500, "点券已到账，提醒未能发出；勿新建编号重发。", "unknown")`。前端仍按 `unknown` 处理，行为不变，GM 能读到「已到账」。这仍是报错、不折成 `unconfirmed`，与裁定 2 不冲突。同时在 F-107 中补写前端行为：500 且没有 `outcome` 时保留待定记录与原编号，原编号重放不会二次入账；提醒持续失败时重放仍是 500，需要走「查询」消掉待定记录。用例：改写或新增一条，断言提醒抛出时响应体含该文字、`outcome` 为 `unknown`，且只入账一次。
2. **发现 2**：补一条用例，覆盖「账号有选中会话但不在世界中（`Gone`）或会话已被替换（`Declined`）→ `offline`」，并补变异（把该分支改回 `attempted` 时用例变红）。
3. **发现 3、4**：只作记录。D6、D7 处原始失败可能被关闭异常取代，能走到这里的只有执行器外的程序错误；T15 的证明边界（不证明真实选角序列中称号事件的入队时机）。都写入 Result Remaining。

处理后重跑：Release 构建；GmWeb 与 Protocol 中 GmCera 相关测试的过滤运行；`cr-at-eol` 口径 `diff --check`。
