# TASK-25 — T-006c：「发送即记录」替身改为经真实登记适配器与发件箱观察，通知总线每收件人一条批次（F-102）

基准提交 `1ee1c91e`（TASK-24／T-006b 已提交，结果审查 approved）。范围取自：
- `tasks/TASK-23.md` 文末「TASK-25 — T-006c」边界提纲整节（替身改写、人群项出站实测、F-61、`t006-closure.md`）与「T-006 完成的条件」七条；
- `verification/follow-ups.md` 中归属 TASK-25 或 T-006 的未关闭条目：F-21、F-61、F-101、F-102、F-104、F-108（检索「TASK-25」「T-006」逐条收齐的结果见 §13）；
- `verification/t006-test-dispositions.md` TASK-24 §5：18 条「KE 保留 → F 类」过渡期用例的驱动替换；
- `reviews/task-23-result-review.md`（F2 → F-102；非阻断残余 F-101、F-104）与 `reviews/task-24-result-review.md`（发现 3、4 只作记录）的残余；
- `plan.md` 共享决定「测试」第 2、3 条，「发件箱的归属」；`spec.md` RQ-030、RQ-031(d)、AC-011、AC-020。

**本计划把 T-006 的剩余工作拆成两个单元**（理由见 §0，待裁定第 1 条）：
- **TASK-25（本单元，T-006c）**：替身改写、18 条 F 类的驱动替换、F-101、F-102（生产文件 `NotificationBus.cs`）、F-104；
- **TASK-26（T-006d，收口）**：人群项出站实测、F-61、F-21、F-108、新建 `verification/t006-closure.md` 与 M-2 进入条件。边界提纲在文末，不在本单元执行。

上面列出的每一项来源，都在本单元的 Outcome 或 TASK-26 提纲里有归属，对照见 §13 表。

输入还有：
- TASK-23 的替身清点 `logs/task-23-doubles.txt`（基准 `a4422480`：95 个类、69 个文件）与脚本 scratchpad `t23/doubles.py`；
- TASK-23、TASK-24 的证据夹具：`tests/Support/XoDelivery.cs`（真实登记适配器、真实发件箱与有序组、真实 `OutboundSender`、`XoTransport`、`FaultingEncoder`、`NameEncoder`、`XoSubmissions`）、`Application.Tests/Notifications/NotificationWorld.cs`（真实总线经真实适配器登记）；
- TASK-24 的可达性规则（Detailed Plan §2：D／KE／KB，「注入点对应不再抛出一栏」即删除）与 `logs/task-24-sites.txt`；
- 写断言、取证汇与 `verification/probe-tools/violations.py`（TASK-17、TASK-18、TASK-24）；表征宿主（TASK-01）。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **替身清点与分类**（Plan「测试」第 3 条；TASK-23 提纲「范围」第 1 条）：在基准上重跑 `t23/doubles.py`，把每个替身类归入 §2 的一类，落盘 `logs/task-25-doubles.txt`（「文件:行 → 类 → 接口 → 类别 → 处置 → 依赖它的用例数 → 处置记录行」）。未分类 0。
  2. **共用观察支撑**（§3）：在 `tests/Support/` 建一份「真实登记 ＋ 记录提交」的支撑，只有一份实现，链接进需要它的测试项目。它由真实 `OutboxNetworkMessageSender`、`OutboxConnectionCloser`、`GameMessageSender` 与发件箱组成；在发件箱交给发送端的那一层记录条目；按旧替身的观察 API（`Messages`、`Batches`、`Sent`、`Closed` 等）给出门面，使断言行文字不变；失败注入一律放在登记点的编码接缝（`FaultingEncoder`），回调一律放在登记点（编码之前）。
  3. **改写 C1、C2 两类替身**（§4、§5）：断言行多重集合逐文件不变；必须变的，只能属于 §9 列出的三类，并逐条记入处置记录。被改写的用例全部经执行器运行（作业内发送），正式全量的取证中不出现新的执行器外记录。
  4. **18 条 F 类过渡期用例的驱动替换**（TASK-24 §5，§6）：抛异常的替身改为在登记点注入，异常类型与消息沿用原替身，「对 CMD 反应」的断言不变；读替身记录的部分改读门面。`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 的断言行多重集合与方法体中的断言逐字不变（AC-009）。
  5. **18 条之外的失败注入逐处判定可达性**（§6.2，待裁定第 4 条）：替身里的每个 `throw`、`Fail*` 开关、返回 `Failed` 的分支，按 TASK-24 §2 的规则归为 KE、KB 或「不再抛出」，落盘 `logs/task-25-injections.txt`。KE、KB 改为登记点注入或真实组装失败；「不再抛出」的删除并写承接者。
  6. **F-101 复核并关闭**（§7）：`EffectOperationReplayTests` 的 `Later_readonly_transactions_do_not_replace_the_primary_footprint_or_commit_state`、`Second_primary_command_cannot_pay_or_overwrite_the_first_committed_entry`，以及同文件的 F 类 `A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request`：回调改挂登记点，断言不变。
  7. **F-102**（§8；TASK-23 第 1 轮评审 F2；RQ-031(d)）：`NotificationBus` 每收件人只解析一次连接上下文，把该收件人的全部消息整批编码后登记一条批次；补「多条消息中最后一条不可编码，则该收件人一条都不登记」的用例；`NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 中钉住旧缺陷的那一条断言按新语义改写并单列记录；改写后的替身同步。
  8. **F-104 复核**（§10）：「`DomainEventDispatcher.DrainOnceAsync()` 之后立刻读帧」的用例逐个判定宿主是否开着派发组循环；开着的，按 TASK-23 S14 的写法加执行器屏障，断言不变；没开的写明理由。关闭 F-104，或把仍未判定的列名留在 F-104。
  9. **一次正式全量**：带取证汇，反例类与对照以外为 0；表征测试 10 通过、0 跳过，`Characterization/` 与金样本目录不改；变异核对（§12）。
- Owned boundary：
  - **生产代码**：
    - `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`（§8）；
    - `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs` 中描述「逐条登记、前一条已登记」的注释句（若有），只改这些句子；
    - 编译迫使的改动（`NotificationBus` 的注册所在文件），逐个列名，只限 `Dfo.Server.Application/Notifications/`。
  - **测试**：
    - `logs/task-25-doubles.txt` 中处置为「改写」的替身所在文件；`Application.Tests/Inventory/NpcShopPurchaseTests.cs` 中的 `ShopFixture`、`ShopSender`（被约 106 个文件、约 856 个用例方法使用；这些使用方文件本身预计不改）；
    - §6 的 18 条与 §6.2 判定后需要改或删的用例；§7、§8、§10 点名的文件；
    - `tests/Support/` 的新支撑文件与 `XoDelivery.cs`（扩展或拆出共用部分，仍只有一份实现）；需要链接它的测试项目 csproj；
    - 逐文件列入 `verification/t006-test-dispositions.md` 的 TASK-25 一节。
  - **文档与证据**：`verification/t006-test-dispositions.md`（TASK-25 一节）；`verification/follow-ups.md`；`verification/probe-tools/violations.py` 的期望表（只在 §11 的预期变化时改）；`verification/logs/task-25-*.txt`；一次性脚本放 scratchpad，全文附在对应日志末尾。
  - **不改**：
    - 除上面三项以外的全部生产代码，包括 `OutboxAdapters.cs`、`GameplayOutbox.cs`、`OutboundSender.cs`、`EventService` 与各组装器；
    - `spec.md`、`plan.md`、`AGENTS.md`、`Docs/`；`Characterization/` 目录与金样本文件；
    - 26 个投影替身（`ProjectionEventService` 子类 21 个与其他 `IEventService` 实现 5 个），按待裁定第 2 条只分类、不改写；
    - TASK-26 的范围：人群项实测、F-61 的取证接线与识别名单、`t006-closure.md`。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-25-build-test.txt`。
     - **总数**：基准为 8231（TASK-24 正式全量 8229 ＋ 评审后 Theory 2 行，TASK-24 Result §7）。`--list-tests` 合并显示不可序列化的 Theory 行（TASK-24 Why 9），加法按实际运行数写。本单元为 8231 ＋ N － D，逐项目写加法，N、D 逐条列名（预期见 §11）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动（`git status` 为证）。
     - **日志信号**：与 TASK-24 Checks 2 同口径、同脚本（scratchpad `t19/analyze.py`）。应为 0 的七项仍为 0；`Gameplay bus:… failed`、`inbound:… failed`、`gm:… failed`、`Outbound connection … closed`、`Notification for character … failed` 逐条与 TASK-24 对照，增减逐条归到本单元的改写或注入。
     - 耗时与内存只作信息（RQ-091 用户修订：单条 ≤ 2 s 即可接受，性能不作停止条件）。F-69：与 TASK-24 的正式全量逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  3. **零违规取证**（`logs/task-25-violations.txt`）：`violations.py` 的命令与 TASK-24 Checks 3 相同。预期先写进日志开头再跑：40 条，按 kind `bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37，与 TASK-24 相同；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。出现新的执行器外记录，说明某个改写后的用例在作业外发送，按 §5.3 修用例，不得把它加进期望表。
  4. **替身清单终态**（`logs/task-25-doubles.txt` 末尾）：同一脚本在工作区重跑；处置为「改写」的类不再实现 `INetworkMessageSender`、`IGameMessageSender`、`INetworkConnectionCloser`（门面类若仍实现接口，必须是转发给真实适配器的包装，§3.3，逐个列名）；「不改」与「交 T-008」的逐个附理由；加法：改写 ＋ 不改 ＋ 交 T-008 ＋ 投影替身（待裁定第 2 条）＝ 基准类数。
  5. **测试改动核对**（`logs/task-25-diff-check.txt`）：`diffcheck7.py 1ee1c91e --lines`（scratchpad `t07/diffcheck7.py`）加 `t21/methods.py`：
     - 方法集合只少了 D 中列出的、只多了 N 中列出的；
     - 断言行多重集合有变化的文件，逐处对到 `t006-test-dispositions.md` 的一行，且变化属于 §9 的三类之一；
     - `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 的断言行多重集合不变，方法体逐字相同，单列一行（AC-009）；
     - 替身类内部的 `Assert`（例如 `ShopSender.SendBatchAsync` 里的端点与帧长断言）被删除时，单列为 §9 第 2 类，写明由哪段真实编码承接。
  6. **可达性清单**（`logs/task-25-injections.txt`，§6.2）：每个注入点「文件:行 → 注入在哪个调用、对哪个收件人、抛什么 → TASK-24 §2 表的来源 → KE／KB／不再抛出 → 处置 → 用例名」。未分类 0。
  7. **新增与改写测试 ×10**（`logs/task-25-new-x10.txt`）：本单元新增的用例与 18 条 F 类、F-101 两条、F-102 相关用例隔离连续 10 次；其余改写过的测试类（按类过滤）隔离连续 3 次。全部通过。耗时只作信息。
  8. **变异核对**（`logs/task-25-mutations.txt`；程序与 TASK-24 Checks 9 相同：每项 scratchpad 备份，逐字节还原后 `cmp`，前后 `git diff -- Server/src` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§12 的每一项都要有指定的用例变红；标为「对照」的是把测试文件临时换回基准版本，预期保持绿，如实记录。
  9. **生产改动边界**（`logs/task-25-boundary.txt`）：`git diff --stat 1ee1c91e -- Server/src` 只含 Owned boundary 列出的文件；`Server/src` 下没有未跟踪文件；`spec.md`、`plan.md` 无改动。
  10. **隔离复跑**（`logs/task-25-isolated.txt`）：Application、Protocol、Networking、Host、Persistence 各一次，都带取证目录，记录各自的反例类与对照记录数。
  11. **F-102 与 F-101 的证据**（写进处置记录 TASK-25 一节）：F-102 新用例名、改写的那一条断言「旧 → 新 → 依据 RQ-031(d)」；F-101 三条用例的回调挂点（登记点）与断言不变的核对行。
  12. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出。改动的已有文件行尾与基准一致；基准上带 CRLF 的候选文件规划时已查到三个：`Host.Tests/GmEventsHttpTests.cs`（118 行）、`Application.Tests/Sessions/HousingNetworkSettlementTests.cs`、`Application.Tests/Achievements/AchievementTitleBookTests.cs`，改它们时按字节编辑，逐个核对（TASK-23 F4 的教训）。结束时执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-24（`1ee1c91e`）：`tests/Support/XoDelivery.cs`（含 `FaultingEncoder`、`ClosedAsync`）、18 条 F 类清单（`t006-test-dispositions.md` TASK-24 §5）、可达性规则与 `logs/task-24-sites.txt`、取证源 `GameplayAssertionDiagnostics`。
  - TASK-23（`50d7bce4`）：登记适配器、作业结束刷出、工作单元钩子、替身清点 `logs/task-23-doubles.txt`、`NotificationWorld` 与 `NotificationDeliveryTests`。
  - TASK-17、TASK-18：写断言、取证汇、`verification/probe-tools/violations.py`；`tests/Support/TestGameplay.cs`。
  - 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/methods.py`、`t23/doubles.py`，规划时核对存在；执行开始时再核对，缺失时从 TASK-23、TASK-24 日志末尾附的全文恢复。
- Result review：required。
  - 改动面大：约 60 个测试文件直接改写，`ShopFixture` 一处改动影响约 106 个文件、约 856 个用例方法；外加一个生产文件。判错的方向是「门面让断言照旧成立，却没有真的经过发件箱」：全量会是绿的，测试却仍然证明不了发件箱语义。所以证据要靠逐文件的断言多重集合核对（Required check 5）、替身清单终态（check 4）和「换回旧驱动方式即不再变红」的对照变异（§12）。
  - F-102 改变一个生产文件的登记粒度，并改写一条已有断言；它是 RQ-031(d) 字面与实现之间的最后一处差异。
  - 本单元的处置记录、替身清单与正式全量是 TASK-26 收口与 M-2 的输入。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 试点（§14 第 2 步）之后，门面无法让试点文件的断言行保持不变，且变化不属于 §9 的三类。
    2. F-102 实施后，因「总线也登记批次」而改义的 `Batches` 计数或批次内容断言超过 10 处（待裁定第 3 条）。
    3. 某个注入点的可达性判不清，或判为「不再抛出」却找不到承接者，也说不出为什么已被规格消除（§6.2）。
    4. `MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 需要改任何一行断言（AC-009）。
    5. 需要改 Owned boundary 以外的生产文件；需要改 `spec.md`、`plan.md`。
    6. 正式全量出现 §9 规则覆盖不了的新失败；取证出现反例类与对照以外的记录且无法通过把发送放进作业解决（即生产路径本身在作业外发送）。
    7. 表征测试或任何金样本需要改动。
    8. 规划时预计「交 T-008」的类超过 15 个（§2），或某个类需要交 T-008 却说不出 §2 的理由。
    9. V12 发现有通知组装器能产出超过 `DnfFrameProtocol.MaximumFrameLength` 的帧（F-102 换成整批编码后，它会由「登记后交付」变为「登记点失败」，是本计划没有授权的行为变化）。
  - 变异核对期间，允许临时改动 `Server/src` 与临时把单个测试文件换回基准版本，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 范围、与提纲的差异、为什么拆分

**提纲与基准的差异**：
- 提纲写「约 57 个文件、69 个替身类」。69 是 TASK-23 在基准 `a4422480` 上直接实现三个接口（`INetworkConnectionCloser`、`INetworkMessageSender`、`IGameMessageSender`）的类数；同一份清点把投影替身也算上是 95 个类、69 个文件。
- 规划时在 `1ee1c91e` 上用同一脚本重跑：94 个类、68 个文件。直接实现三个接口的 68 个类，分布在 60 个文件；投影替身 26 个。比 TASK-23 少的 1 个是 `GameLoginIntegrationTests.FailFirstLoginDelivery`（TASK-24 W1 改写）；`XoProjections` 随 `XoDelivery` 移到 `tests/Support/`。执行时以 V1 为准。
- 提纲没有点出的规模：`ShopSender` 在清点里只算「1 个用例方法」，因为脚本只数点名的方法。实际上它是 `ShopFixture` 的发送器，被约 106 个文件使用；这些文件里 `Sender.Messages` 被读 544 次，`Sender.Batches` 56 次，`BeforeSend` 47 次，`FailBatch` 16 次，`FailExplicit` 5 次，`BeforeExplicit` 5 次，`Attempts` 4 次（规划时 `grep` 计数，执行时 V3 重数）。所以本单元的主要工作量不是 68 个类各改一遍，而是把这一个夹具改对。
- F-102 与替身改写是绑在一起的：`INetworkMessageSender.SendBatchAsync` 的默认实现抛 `NotSupportedException`，只实现了 `SendAsync` 的容器级替身一旦遇到「总线整批登记」，就会让总线记 `Failed`。TASK-23 Why 7 正是因此退回了逐条登记。所以 F-102 必须在容器级替身改写之后做。

**拆分**（待裁定第 1 条）：TASK-25 做替身改写与 F-102；收口单列为 TASK-26。理由：
- **证据要分开**：本单元要证明「只换了驱动方式，断言不变」（diffcheck），只有 F-102 一处有意的语义改变。收口要新增判据用例（F-61）、临时探针（人群项实测），还要写判断性的对账。放在一起，`diffcheck` 与边界核对都要再扣掉这些，审查时分不清。
- **收口要以评审过的结果为依据**：`t006-closure.md` 是 M-2 的依据，它引用的替身清单终态、处置记录与正式全量应当是 TASK-25 经结果审查之后的版本。在同一单元里，收口只能引用自己尚未评审的结果。
- **规模**：本单元改约 60 个测试文件，外加一个影响约 856 个用例方法的夹具；TASK-23 是 38 个，TASK-24 是 28 个。收口再加进来，一次结果审查要同时核对机械改写与 M-2 判断，两类问题的审查方式不同。
- **代价**：多一次正式全量（约 4 分钟）与一次结果审查。性能不作停止条件，可以接受。
- 若主控方不同意拆分，文末 TASK-26 提纲整节并入本单元，作为最后一步，Required checks 加上提纲里的检查项；其余各节不变。

**本单元不做的**（都在 TASK-26 提纲里）：人群项出站实测、F-61、F-21、F-108、`t006-closure.md`、M-2 进入条件。

### 1. 基准核实（执行时重跑并落盘 `logs/task-25-verify.txt`）

| # | 问题 | 检索 | 规划时在 `1ee1c91e` 上的结果 |
|---|---|---|---|
| V1 | 替身清点 | `python3 -I scratchpad/t23/doubles.py <工作区> 1ee1c91e` | 94 个类、68 个文件；直接实现三个接口的 68 个类、60 个文件（Application 27、Protocol 39、Host 1、Persistence 1）；投影替身 26（`ProjectionEventService` 子类 21 ＋ 其他 `IEventService` 实现 5）。依赖用例数合计 319（只数点名的方法） |
| V2 | 每个替身怎么接进被测对象 | 每个类名在同文件中的用法：DI 注册（`Add*<…Sender／Closer>`）还是构造参数（`new XxxHandler(…, sender, …)`） | 规划时用启发式脚本粗分（scratchpad `t25-howused.py`）：DI 约 15、构造约 49、两者都不是 4。执行时逐个读，结果进 `task-25-doubles.txt` |
| V3 | `ShopFixture`／`ShopSender` 的使用面 | `grep -rl ShopFixture`；`grep -rho "Sender\.\(Messages\|Batches\|FailExplicit\|FailBatch\|BeforeSend\|BeforeExplicit\|Attempts\)"` | 106 个文件、约 856 个用例方法；计数见 §0 |
| V4 | 注入失败的替身成员 | 替身类体内的 `throw`、`Fail*`／`Failing*` 开关、返回 `NotificationOutcome.Failed` 的分支；以及 `ShopSender` 的 `FailBatch`／`FailExplicit` 的使用点 | 执行时生成，进 `task-25-injections.txt`。已知其中 18 条用例是 TASK-24 §5 的 F 类 |
| V5 | `NotificationBus` 今天的登记方式与依赖它逐条登记的断言 | 读 `NotificationBus.cs`；`grep -rn "first part\|unencodable" Server/tests` | 逐条 `sender.SendAsync`（`NotificationBus.cs:93-97`）；`NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 断言第一位收件人的 `"first part"` 已登记（`:294-297`），这就是 F-102 的缺陷本身 |
| V6 | `SendBatchAsync` 的语义 | 读 `OutboxAdapters.cs:56-77` 与 `INetworkServices.cs:38-41` | 对给定上下文整批编码、登记一条（默认 Required）批次；连接是否仍是登记时的那一个由发送端交付前判定（`Stale`，RQ-035）；接口默认实现抛 `NotSupportedException` |
| V7 | 容器里没有联网时适配器的行为 | 读 `OutboxNetworkMessageSender` 的两个构造器、`GameplayOutboxes` 的两个构造器 | 没有上下文表与编码器时每次发送都静默丢弃；没有发送端时刷出的条目按过期丢弃。所以「不注册联网」的测试容器今天看不到任何登记，改写时必须由支撑提供上下文表、编码器与记录发送端 |
| V8 | 测试项目能看到哪些 internal | 各 csproj 的 `ProjectReference` 与 `InternalsVisibleTo` | Application 的 internal 对 Application、Protocol、Host、Infrastructure、Persistence 五个测试项目可见；Networking 的 internal 只对 Networking、Application、Protocol 三个测试项目可见；`GameMessageSender` 是 Protocol 的 internal，`XoDelivery` 已按名字创建 |
| V9 | `DrainOnceAsync()` 之后读帧的用例（F-104） | `grep -rl DrainOnceAsync Server/tests`，逐个读 `DrainOnceAsync` 之后几行是否立刻读帧，以及宿主是否启动派发组循环（`DomainEventDispatcher` 的托管服务或 `RunAsync`） | 19 个文件用到 `DrainOnceAsync`（Application 17、Host 2）；F-104 记的是 Application 11 个与 `GmOfficialMailHttpTests`，执行时按「之后立刻读帧」重新筛 |
| V10 | 作业外调用 handler 或发送的改写对象 | 每个改写文件中，handler／投递方法的调用是否已在 `TestGameplay.Run*`、`XoDelivery.Run*` 或经真实 dispatcher 运行 | 执行时逐文件确认，结果进处置记录 |
| V11 | CRLF 文件 | `grep -c $'\r'` | 候选文件中只有 Required check 12 列的三个 |
| V12 | 整批编码与逐条编码的差别（F-102 换路径后会不会多出拒绝） | 读 `Networking/Outbound/OutboundEncoder.cs` 的 `EncodeMessage` 与 `EncodeBatch` | 帧字节相同（逐条编码后拼接）；两处不同：`EncodeBatch` 对每帧检查 `DnfFrameProtocol.MaximumFrameLength`（`:59`、`:68`），`EncodeMessage` 不查；端点不符的异常文字不同。执行时确认没有任何通知组装器能产出超过帧长上限的帧（读各组装器产出的消息类型与编码器的长度上界）；有的话，F-102 会让它由「登记后交付」变为「登记点失败」，按停止条件 9 停下回报 |

V1～V12 与本表不一致时：V1 的类数差 ±3 以内照常进行并记录；更多，或 V5、V6、V7、V12 的读码结论不同，停下回报。

### 2. 分类规则

| 类别 | 是什么 | 处置 |
|---|---|---|
| **C1 容器级替身** | 以 DI 注册覆盖 `INetworkMessageSender`／`IGameMessageSender`／`INetworkConnectionCloser` 的替身，被测对象经容器拿到它（`ShopSender`、`LearningSender`、`ReusingConnectionSender`、`ReplayBatchSender`、`GmEventsHttpTests.CaptureSender`、两个婚礼 `RecordingSender`、幻影塔的 `FailingSender`／`CleanupCloser`／`RecordingCloser`／`NoopConnectionCloser`、`PartySelectDungeonHandlerTests.RecordingCloser`、`CardRewardTests.RecordingSender`、`JoustQueryHandlerTests.Sender`、`LoginSessionReconnectTests.RecordingCloser` 等，以 V2 为准） | 去掉覆盖，容器用真实登记适配器；由 §3 的支撑提供上下文表、编码器与记录发送端；断言读门面 |
| **C2 构造注入替身** | 测试 `new` 出 handler、投递服务或协调器时直接传入的替身 | 换成支撑提供的真实 `GameMessageSender`／适配器／关连接器；被测调用放进作业；断言读门面 |
| **C3 不改** | 被测对象就是这个接口的契约本身（例如 `ExplicitMessageDeliveryContractTests.RecordingSender` 测的是 `INetworkMessageSender` 默认方法的行为）；或类没有任何用例经过它 | 不改，逐个写理由 |
| **T8 交 T-008** | 用例测的是 handler 内部编排（「假总线 ＋ 直接 new handler」），换成真实适配器后必须改断言文字才能表达同一件事，而该 handler 会在 T-008 归并成协议 CMD 时整体重写（Plan「测试」第 2 条，RQ-021） | 不改，逐个写「为什么换不了、交给 T-008 哪个功能簇」。预计 0～10 个，超过 15 个按停止条件 8 |
| **P 投影替身** | `ProjectionEventService` 子类与直接实现 `IEventService.ProjectAsync` 的类 | 按待裁定第 2 条：只分类（记录型／失败注入型／阻塞型），不改写；失败注入型在 §6.2 里与其他注入一起判定可达性 |

判定顺序：先看 C3，再看能否 C1／C2，最后才是 T8。T8 的每一个都要在试点之后决定，不在规划阶段预判。

### 3. 共用观察支撑

**位置与名字**：`tests/Support/` 下新文件（名字由执行者定，例如 `RecordedOutbound.cs`），与 `XoDelivery.cs` 共用 `FaultingEncoder`、`XoConnections`、`XoSubmissions`。仍然只有一份实现：要么把共用部分从 `XoDelivery.cs` 拆到新文件，要么让新支撑引用 `XoDelivery` 里的类型；改名或挪位置时 XO 用例只改类型名，记 §9 第 3 类。Host.Tests、Persistence.Tests 看不到 Networking 的 internal（V8），新支撑只依赖 Abstractions 的 `IOutboundSender`／`OutboundEntry` 与 Application 的适配器，不依赖 Networking 的类型；需要真实 `OutboundSender` 的场景只在 Application、Protocol、Networking 三个项目里用。

**3.1 组成**：
- 真实 `GameplayOutboxes`、`OutboxNetworkMessageSender`、`OutboxConnectionCloser`、`GameplayOutboxOrdering`（经 `AddGameplayExecution`），以及按名字创建的 `GameMessageSender`。
- **上下文表**：支持 Game 与 Channel 两种端点；测试显式登记连接，或开启「首次见到即登记」以保持旧替身对任何连接都接收的行为。用后者时，F-83 的「连接已不在即丢弃」在该测试里不会发生，写进处置记录。
- **编码器**：容器里注册了 Protocol 编码器的（C1 大多如此），用按 `ServerMessageEncoderRegistration` 查真实编码器的那一种（与今天 `ShopSender.SendBatchAsync` 的编码方式相同），帧断言因此照旧成立；否则用 `NameEncoder`。两种都包在 `FaultingEncoder` 里。
- **记录发送端**：实现 `IOutboundSender`，按提交顺序记录条目；需要交付结果的，转给真实 `OutboundSender` 加 `XoTransport`（只在能看到 Networking 的三个项目里）。

**3.2 门面**（让断言行文字不变）：
- `Messages`：已提交条目中的消息，按提交顺序展开，不含关连接条目。作业内读取时，再加上当前作业发件箱里已登记、尚未刷出的条目（经 `GameplayOutboxes.CurrentOrNull`），与旧替身「调用即可见」的时机一致。**区分力说明**：回滚丢弃（RQ-031(b)）与作业结束刷出（RQ-031(a)）只有在作业结束之后读取时才被观察到；处置记录里标出哪些用例是作业内读取。
- `Batches`：经 `SendBatchAsync` 进入的条目（含 `Connection`、`Messages`、`Frames`）。用一个转发包装记录调用种类，再按消息引用把调用对回提交的条目；被回滚丢弃的调用不出现。
- `Sent`／`Closed`／`Closes` 等各替身原有的成员名：按原替身的含义从条目算出（`IsClose` 条目即登记了关闭；需要「发送端执行了关闭」的读 `XoTransport.IsClosed`／`ClosedAsync`，两者的区别写进处置记录，同 TASK-24 §10）。
- `Clear()`：清空已记录的条目与调用记录，供 `f.Sender.Messages.Clear()` 一类的写法照旧使用。
- `Attempts`：转发包装记录的 `SendAsync` 调用次数。

**3.3 失败与回调的挂点**：
- **失败**一律在 `FaultingEncoder` 上注入（「指定连接 ＋ 指定消息类型或谓词」），异常类型与消息沿用原替身。这样调用在登记点抛出、本次调用的条目一条都不登记，与生产中编码、端点失败的形状相同（TASK-24 §2 的 KE）。
- **回调**（`BeforeSend`、`BeforeExplicit`、F-101 的 `f.Sender` 回调）挂在转发包装上、调用真实适配器之前。它运行的时刻就是生产中的登记点：仍在同一个作业里、发送调用的位置上（F-101）。
- `ShopSender` 的 `FailExplicit`／`FailBatch` 保留开关名，内部改为对下一次相应调用的直接收件人注入编码失败。旧 `FailBatch` 的语义是「先记录批次、再抛出」，改写后批次不再被记录（登记点失败时本次调用什么都不登记）；读到这个失败批次的断言按 §9 第 1 类单列，或按 §6.2 判定后删除。

**3.4 仍然是包装、不是替身的判据**：门面类若实现 `INetworkMessageSender`，它的三个方法都必须转发给真实适配器，自身不记录消息、不抛异常（失败只来自 `FaultingEncoder`）。替身清单终态（Required check 4）逐个核对这一点。

### 4. C1 改写（容器级）

执行顺序（试点在前，见 §14）：
1. **`ShopFixture`／`ShopSender`**（`NpcShopPurchaseTests.cs`）：去掉 `services.AddSingleton<INetworkMessageSender>(Sender)`，改为注册支撑的上下文表（夹具的 `connection` 预先登记）、真实编码器、记录发送端；`Sender` 属性返回门面，成员名不变。`ShopSender.SendBatchAsync` 内的两条 `Assert`（端点相符、帧长在 15～1,048,576 之间）删除，由真实编码器与发送端承接（端点不符在登记点抛出；帧长上限由 `OutboundSender` 的队列字节上限承接），记 §9 第 2 类。
2. **其余 C1**：`SkillLearningCommandTests`、`CollectionBoxNetworkSettlementTests`、`ReplayStateSnapshotTests`、`JoustIntegrationTests`（`:1062` 注册的 `Sender`）、婚礼两个、幻影塔三个、`PartySelectDungeonHandlerTests`、`CardRewardTests`、`JoustQueryHandlerTests`、`LoginSessionReconnectTests`（Persistence）、`GmEventsHttpTests`（Host，CRLF）。各自去掉覆盖、接支撑。
3. **Host 的 `GmEventsHttpTests.CaptureSender`**：`GmTestHost` 已注册真实适配器与联网模块；改写为替换 `IOutboundSender` 为记录发送端（或在真实发送端前加记录），读门面。文件带 CRLF，按字节编辑。

### 5. C2 改写（构造注入）

**5.1 写法**：每个测试类持有一份支撑实例（或用已有的 `XoDelivery`）；`new XxxHandler(..., support.Game, support.Closer, ...)`；被测调用放进作业（`support.RunAsync`、`RunFromAsync`，或现有的 `TestGameplay.Run*`：`GameplayOutboxes` 按作业令牌取发件箱，任何执行器的作业都能登记，刷出时交给支撑容器里的记录发送端）。

**5.2 分批**（每批构建一次，跑该批所在项目的过滤测试）：
1. Application：`GameLoginTests`、`ExitFlushHandlerTests`、`GameExitMessageHandlerTests`、`MoveItemSpaceHandlerTests`、`UseStackableHandlerTests`、`SelectCharacterLifecycleTests`、`OnlineUserServiceTests`、`SessionGenerationOwnershipTests`、两个 Channel handler 测试、`CommonExperienceEffectTests`、登录会话两个；
2. Protocol 副本与地下城：`SelectDungeonHandlerTests`、`PartyMoveMapHandlerTests`、`LeaveDungeonTests`、`ResultScreenTests`、`DungeonClearProjectionDeliveryTests`、`DungeonLoadingProjectionDeliveryTests`、`FinishLoadingCodecTests`、卡牌三个；
3. Protocol 两塔、怪物死亡、拾取、生物：`BossTowerProjectionConcurrencyTests`、`IllusionTowerProjectionConcurrencyTests`、`MonsterDeathHandlerTests`（AC-009 用例所在，断言一行不动）、`GetItemHandlerTests`、`QuestClearVerticalTests`、`CreatureProjectionDeliveryTests`、`HatchCreatureHandlerTests`、`CharacterDeathHandlerTests`；
4. Protocol 其余：城镇两个、队伍、物品移动三个与 `ExperienceFrameStandIns`、编解码器类四个、`ItemUsageProjectionTests`、`WeaknessProjectionTests`、`SharedEffectProjectionTests`、`TitleBookMutationHandlerTests`、`ReturnToSelectionTests`。
（以 V2 的清单为准；批内顺序不限。）

**5.3 作业外调用**：改写前在作业外调用 handler 的用例，改写后会在登记点得到 `GameplayOffExecutorException` 并写一条 `write-off-executor` 取证。处置是把调用放进作业（测试体的改动，不是断言的改动），逐个记入处置记录；不得把这类记录加进 `violations.py` 的期望表。

**5.4 作业内读取**：用例在被测调用之后、同一个作业里读替身记录的，门面按 §3.2 读当前发件箱；读取放在作业之后也能表达同一意图的，优先移到作业之后（断言文字不变，只是位置变化），记入处置记录。

### 6. 失败注入

**6.1 18 条 F 类**（TASK-24 §5，驱动替换；注入点都是直接收件人，真实适配器在登记点仍会抛出）：

| DR | 用例 | 替身 → 新注入点 |
|---|---|---|
| DR-1 | `SelectCharacterLifecycleTests.RequiredOwnerFailure_CompensatesExactAttemptAndClosesOnlyItsConnection`、`PvpDirectFailure_DoesNotMarkReadyAndRollsBackPublishedAttempt` | 尾段 `SendExplicitAsync` 的直接收件人 → `FaultingEncoder` 对该连接的对应消息 |
| DR-2 | `PartySelectDungeonHandlerTests.A_secondary_recipient_delivery_failure_aborts_entry_before_anyone_leaves_town`；`PartyMoveMapHandlerTests.Publication_failure_logs_the_committed_room_and_preserves_the_exception` | 成员的直接发送 → 同上 |
| DR-3 | `BossTowerProjectionConcurrencyTests.Projection_failure_does_not_close_a_replacement_selected_session`、`IllusionTowerProjectionConcurrencyTests` 同名用例 | `SendToExactAsync` 内对该成员的发送 → 同上；被替换会话不被关闭的断言读关闭条目 |
| DR-4 | `MonsterDeathHandlerTests.Death_send_failure_cannot_erase_the_committed_shared_clear_fact`；`GetItemHandlerTests.Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact`；`IllusionTowerRewardProjectionTests.Persistent_delivery_failure_closes_after_two_attempts_and_real_cleanup_removes_receipt` | 报告者／拾取者的直接发送 → 同上；幻影塔奖励的「两次尝试」由同一注入连续命中两次表达 |
| DR-9 | `CreatureProjectionDeliveryTests.Owner_send_failure_closes_owner_and_reports_failure` | 所有者的 `SendNotiAsync` → 同上 |
| DR-12 | `OnlineUserServiceTests.FailedInitialNotice_RemovesExactRegisteredIdentity` | 初始通知 → 同上 |
| 离开副本 | `LeaveDungeonTests.Failed_exit_delivery_releases_the_transition_without_repeating_the_exit` | 同伴与本人的直接发送 → 同上 |
| 登录 | `GameLoginTests.MessageHandler_SendFailureConsumesTheSuccessButRemovesItsPublishedIdentity` | `SendCmdAsync` → 同上 |
| 提交后不回滚 | `SkillLearningCommandTests.A_send_failure_leaves_the_committed_learning_for_relogin`、`CeraShopPurchaseTests.Send_failure_does_not_repeat_or_undo_the_committed_purchase`、`NpcShopPurchaseTests.Delivery_failure_neither_repeats_the_purchase_nor_rolls_back_its_commit`、`ItemSaleTests.Failed_delivery_does_not_retry_or_reverse_the_sale`、`EffectOperationReplayTests.A_use_already_durable_before_sender_failure_cannot_consume_twice_on_the_same_raw_request` | `ShopSender`／`LearningSender` 的失败开关 → 门面开关，内部为登记点注入（§3.3） |

加法：DR-1 2 ＋ DR-2 2 ＋ DR-3 2 ＋ DR-4 3 ＋ DR-9 1 ＋ DR-12 1 ＋ 离开副本 1 ＋ 登录 1 ＋ 提交后不回滚 5 ＝ 18。

每条在处置记录里写：注入点「旧 → 新」、异常类型与消息、断言行变化（预期无；读替身记录改读门面而断言文字相同的，记「无变化」）。

**6.2 其余注入**（V4；待裁定第 4 条）：按 TASK-24 Detailed Plan §2 的来源表逐处归类：
- **KE**（直接收件人、`SendAsync`、`SendBatchAsync` 的编码或端点失败）：改为登记点注入，断言不变；
- **KB**（替身返回 `Failed`、组装抛出、投影抛出）：P 类替身里的保持不改（待裁定第 2 条），C1／C2 里的改为经真实总线让组装失败，或保留为 KB 注入并写明；
- **不再抛出**（`SendExplicitAsync` 的目标收件人、关连接失败、「传输失败」只能在交付时发生的）：删除，承接者引 TASK-24 §6 证据表中对应 DR 行的 T 用例；找不到承接者按停止条件 3。旧 `FailBatch`「先记录、再抛出」读到失败批次的断言，属于这一类还是 §9 第 1 类，按它的保护意图判定，逐条写明。

### 7. F-101

- 三条用例都经 `ShopFixture`，回调写在 `f.Sender.BeforeSend`／`BeforeExplicit` 上。改写后回调由转发包装在调用真实适配器之前执行（§3.3），仍在操作的作业里、主命令之后；断言不变即成立。
- 处置记录写明：回调挂点（登记点）、它所在的作业（入站作业）、断言行多重集合不变。
- 关闭 F-101。

### 8. F-102

**8.1 生产改动**（只改 `NotificationBus.cs`）：
- 构造器增加 `INetworkConnectionContexts`，沿用登记适配器的两构造器写法：没有上下文表（容器没有联网）时视为连接都不在，按今天经 `SendAsync` 的结果处理（静默丢弃，结果 `Delivered`，F-83）。
- `AttemptAsync`：`IsStillHere` → 组装 → 空则 `Declined` → **只解析一次** `contexts.TryGet(recipient.ConnectionId, …)`：
  - 取不到：与今天相同，丢弃、`Delivered`（`INotificationBus.Delivered` 的注释已写「或连接已不在而被丢弃（F-83）」）；
  - 取到：`sender.SendBatchAsync(context, messages, …)`，整批编码、登记一条批次。任一条不可编码即在登记点抛出，本收件人一条都不登记，结果 `Failed`、记一条 `Warning`，与其他收件人隔离（RQ-031(d)「只丢弃该收件人的这批条目」）。
- 交付等级与今天相同（`SendAsync` 与 `SendBatchAsync` 都登记 Required 批次）；同一连接上的顺序不变（RQ-032）；有序组里一个收件人由多条条目变为一条，组内相对顺序不变。
- 编码由 `EncodeMessage` 换成 `EncodeBatch`：帧字节相同，多一项帧长上限检查（V12）。V12 确认没有通知能触到这个上限，所以可观察的差别只有「不可编码时一条都不登记」。
- 类注释的 `<remarks>`（「前一条已登记」一段）改为现在的语义；`INotificationBus.cs` 若有同义句子，只改这些句子。
- 取消：`ThrowIfCancellationRequested` 仍在组装之前与登记之前各一次。

**8.2 用例**：
- 新增 `NotificationDeliveryTests` 一条（名字由执行者定，意思是「多条消息中最后一条不可编码，则该收件人一条都不登记，其他收件人照常」）：断言该收件人 `Failed`、没有任何条目、`Warning` 一条且异常类型为编码失败、连接没有被关闭、其他收件人的批次已登记。
- 新增一条「每收件人只解析一次上下文」：`NotificationWorld` 的上下文表计数，两条消息的收件人只被 `TryGet` 一次。若执行者判断这一条只是在钉实现细节、不值得单独成例，可以并进上一条，在 Result 中说明。
- 改写 `A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 中钉住旧缺陷的那一条断言：期望的登记列表由 `[(First, "first part"), (Second, …)]` 改为 `[(Second, …)]`。这是本单元唯一有意的断言改义，在处置记录里单列「旧 → 新 → 依据 RQ-031(d)、F-102」。
- 改写后的容器级替身同步：`ShopSender` 门面的 `Batches` 由于总线现在也走 `SendBatchAsync`，会出现总线登记的批次。受影响的断言按待裁定第 3 条处理，逐条列名。

**8.3 顺序**：F-102 在全部 C1 改写完成、全量 Application 与 Protocol 过滤运行变绿之后再做（§14 第 6 步），这样它引起的断言变化可以与替身改写分开计数。

### 9. 允许的断言变化（diffcheck 的三类）

1. **登记点语义的有意差异**：F-102 的那一条；`Batches` 因总线批次而改义的（待裁定第 3 条，上限 10 处）；旧替身「先记录、再抛出」改为「登记点失败、不登记」后，读失败批次的断言（§6.2 判定为保留的）。每条写「旧 → 新 → 依据」。
2. **替身内部断言由真实组件承接**：例如 `ShopSender.SendBatchAsync` 的端点与帧长断言；写明承接它的生产代码位置。
3. **类型名或成员名的机械替换**：支撑改名、`XoDelivery` 拆分引起的类型名变化，以及门面无法保留原成员名的极少数情况；断言的比较对象与期望值不变。

其余任何断言变化都按停止条件 1 或 6 处理。断言行的**位置**变化（例如从作业内移到作业之后，§5.4）不算断言变化，但逐条记入处置记录。

### 10. F-104

- V9 逐个判定：用例在 `DrainOnceAsync()` 之后是否立刻读帧（读门面、读 socket、读 `Sender.Messages`）；宿主是否开着派发组循环。
- 开着组循环的：按 TASK-23 S14 的写法，在每处「`DrainOnceAsync()` 之后读帧」之前加一次执行器屏障（入队一个空作业并等它完成），断言不变；没开的：写明「`DrainOnceAsync` 是唯一的排空者，返回时投递作业已完成并刷出」，不改。
- 改写替身之后，`DrainOnceAsync` 之后读到的是刷出后的条目；作业完成信号在刷出之后（RQ-005），所以只要排空者是测试自己，就不会读早。
- `AchievementTitleBookTests.cs` 带 CRLF，按字节编辑。
- 全部判定完关闭 F-104；仍有判不清的，列名留在 F-104。

### 11. 数量与取证预期

**数量**（逐项目写加法，执行时按实际例数写）：
- **N**：F-102 的 1～2 条；其余预计 0（改写不增加方法）。
- **D**：§6.2 判为「不再抛出」的注入用例，预计 0～3 条；每条带承接者。
- **改写**：C1 约 15 个类、C2 约 49 个类所在的约 60 个文件，加上 `ShopFixture` 一处；方法数不变。
- 预期总数 8231 ＋ N － D，预计 8231～8233。

**取证**（先写进 `logs/task-25-violations.txt` 再跑）：40 条，与 TASK-24 相同：`bus-leaked-flow` 2 ＋ `network-wait` 1 ＋ `write-off-executor` 37。反例类：Application `GameplayWriteAssertionTests` 24、`CommandBusExecutorTests` 1、`EffectOperationReplayTests` 1、`GameplayOutboxTests` 3、`NotificationDeliveryTests` 1；Networking `OutboxRegistrationTests` 2、`OutboundRealSocketTests` 1；七个程序集各一条对照；24 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 2 ＋ 1 ＋ 7 ＝ 40。改写后的用例都在作业内发送，不预计产生记录；若 §6.2 删除了反例类里的用例，按实际改期望表并逐条说明。

### 12. 变异（Required check 8）

| 变异 | 应当变红 | 对照（测试文件换回基准版本，同一变异下） |
|---|---|---|
| M1 `NotificationBus` 恢复逐条 `SendAsync` | §8.2 新用例；改写的那一条断言 | — |
| M2 `NotificationBus` 每条消息解析一次上下文 | §8.2 的「只解析一次」用例（若并入上一条则抽查） | — |
| M3 `GameplayOutboxes` 刷出时丢弃全部条目（作业结束不交给发送端） | C1 抽一个 `ShopFixture` 用例文件（例如 `NpcShopPurchaseTests`）、C1 抽 `SkillLearningCommandTests`、C2 抽一个 Application 文件、C2 抽一个 Protocol 文件、Host 抽 `GmEventsHttpTests` | 同一批文件的基准版本保持绿：旧替身在调用点记录，看不到刷出 |
| M4 `OutboxConnectionCloser` 不登记关闭 | 关连接替身改写后的一个文件（例如 `CreatureProjectionDeliveryTests` 或两塔之一） | 基准版本保持绿 |
| M5 工作单元回滚时不丢弃单元内登记的条目（RQ-031(b)） | 抽一个在回滚路径上断言「没有发出」的改写用例（执行时检索；找不到就如实记录「改写后的用例里没有在作业后读取回滚路径的」） | 基准版本保持绿 |
| M6 删除 18 条 F 类中三条所守的反应分支（登录失败后移除在线身份；提交后发送失败不回滚的购买；生物所有者失败即关闭） | 对应的 F 类用例 | — |
| M7（抽查）`FaultingEncoder` 不抛（测试侧临时改动，代表「注入没有生效」） | 18 条 F 类全部变红 | — |

每一类替身至少一个文件进入 M3 或 M4，即提纲要求的「每类替身改写挑一个文件，回退驱动方式应当变红或明示不变的理由」：变红的是改写后的版本，对照是回退到旧驱动方式的版本。

### 13. 范围来源的去向与 `follow-ups.md`

| 来源 | 本单元 | TASK-26 |
|---|---|---|
| TASK-23 提纲：替身改写、`diffcheck` 证明断言不变 | §2～§6、Required check 5 | 引用替身清单终态 |
| TASK-23 提纲：人群项出站实测 | — | 提纲第 1 项 |
| TASK-23 提纲：F-61 | — | 提纲第 2 项 |
| TASK-23 提纲：`t006-closure.md`（Plan T-006 行、AC-007、AC-017、附录 D／E／G、RQ-008、RQ-012、RQ-036、RQ-061 发件箱一半、VC-8、M-2 进入条件） | — | 提纲第 5 项 |
| TASK-23 提纲：「T-006 完成的条件」七条 | 提供第 6、7 条的本单元部分（正式全量与取证） | 逐条对账 |
| F-102 | §8，关闭 | — |
| 18 条 F 类 | §6.1 | — |
| F-21 | — | 提纲第 3 项 |
| F-101 | §7，关闭 | — |
| F-104 | §10，关闭或更新 | — |
| F-108 | — | 提纲第 4 项（待裁定第 5 条） |
| F-61 | — | 提纲第 2 项（待裁定第 7 条） |
| F-69 | 更新（本单元的耗时对比） | 更新 |
| F-87 | 改写中发现名字提到已删机制的用例，列名追加；改名不做（T-008） | — |
| TASK-23 评审：F2 | 即 F-102 | — |
| TASK-23 评审：残余 F-101、F-104 | 同上 | — |
| TASK-23 评审：残余 F-97、F-98、F-100 | 不涉及（T-009、T-008、信息） | 列入移交表 |
| TASK-24 评审：发现 3（D6、D7 关闭异常可能取代原始失败）、发现 4（T15 的证明边界） | 不涉及 | 写入 `t006-closure.md` 的「已接受的差异／证明边界」 |
| F-107（GM Cera 对外字段变化） | 不涉及 | 写入收口，交外部审核 |

`follow-ups.md`：关闭 F-101、F-102，F-104 关闭或更新；更新 F-69、F-87；新增从 F-111 起编号；文首补一行 TASK-25 的摘要（仿 TASK-24 那一行）。

`t006-test-dispositions.md` TASK-25 一节：开头写日期、基准与分类（沿用 TASK-23、TASK-24 的 E、R、S、W、F、K、KE，加 **C1、C2、C3、T8、P** 五类替身处置与 §9 的三类断言变化）；数量与加法；替身逐类处置表（与 `task-25-doubles.txt` 一致）；18 条 F 类；§6.2 的其余注入；删除（每条带承接者）；改写（旧 → 新 → 保护意图，只列断言有变化或位置有变化的）；新增；F-101、F-102、F-104 的小节。

### 14. 执行顺序

1. 在基准上跑 §1 的核实（V1～V11），生成 `task-25-doubles.txt` 与 `task-25-injections.txt` 的初稿；先按 §2、§6.2 把每个类、每个注入点分类写完，再动代码。
2. **试点**：写 §3 的支撑；改 `ShopFixture`（C1）、`GameLoginTests`（C2，含一条 F 类与一个关连接替身）、`CreatureProjectionDeliveryTests`（C2，Protocol，含 F 类与关连接替身）；构建，跑这三处的过滤测试与 `diffcheck`。断言变化超出 §9 → 停止条件 1；`ShopFixture` 牵动的 106 个文件中出现大量失败 → 先看是否都是 §5.3 的作业外调用，是就按 §5.3 处理，不是就停下回报。
3. C1 其余（§4）。
4. C2 分批（§5.2），每批构建并跑所在项目的过滤测试。
5. 18 条 F 类与 §6.2（可以穿插在第 3、4 步各自所在的文件里做，但处置记录按 §6 组织）；F-101（§7）。
6. 全量 Application 与 Protocol 过滤运行变绿之后，做 F-102（§8），记下它引起的断言变化（待裁定第 3 条的计数）。
7. F-104（§10）。
8. 构建、正式全量与取证（Required checks 1～3），隔离复跑，×10／×3。
9. 变异（§12）、替身清单终态、边界、diff 核对、处置记录、`follow-ups.md`。

### 15. 表征测试与金样本

- 表征测试 10 条与全部金样本不变：本单元只改测试的驱动方式，以及 `NotificationBus` 的登记粒度。表征场景中，总线登记的每个收件人多为单条消息；多条消息的收件人由逐条变为整批，同一连接上的帧序列逐字节不变（编码相同，`EncodeBatch` 是逐条编码的拼接）。
- 有一条表征或金样本需要改，按停止条件 7 处理。

### 16. 生产行为为什么只按 F-102 变化

- **替身改写**只动测试。
- **F-102**：可编码时，同一收件人的消息由 N 条批次变为 1 条批次，帧序列与交付等级相同，单连接顺序（RQ-032）与有序组内的相对顺序不变；不可编码时，由「前面的已登记、后面的没有」变为「一条都不登记」，即 RQ-031(d) 的字面。结果值（`Delivered`／`Failed`／`Gone`／`Declined`）的含义不变。
- **证据**：表征测试与金样本不变；正式全量与 TASK-24 相比只有 §11 记录的增删与 §9 记录的断言变化；取证 40 条、反例类与对照以外为 0；变异 M1～M7。

## 后续单元边界提纲（TASK-26 — T-006d：T-006 收口，不在本单元执行）

- **为什么单列**：见 §0。收口是 M-2 的依据，要以 TASK-25 经结果审查并提交之后的结果为准。
- **进入条件**：TASK-25 已提交。
- **范围**：
  1. **人群项出站实测**（`measurement/selection-outbound-bound.md`「未计入」一节；RQ-036、AC-013）：
     - 测什么：一次选角 CMD（以及城镇进入、PvP 大厅进入这类会给本人下发人群快照的 CMD）对请求者单个连接登记的最大字节数中，人群项的部分：城镇在场投影（约 582 B／人）、PvP 频道大厅快照、选角后放闸的成就／称号投影。
     - 怎么测：沿用 TASK-23 `t23/probe.py` 的临时探针（`GameplayOutbox` 按登记写一行，用后逐字节还原、`cmp`、sha1），在表征宿主上用多人场景实测每人字节数；再按代码中的容量上限（一个城镇区域、一个大厅最多多少人；`PlanTownPublicationAsync` 的前置条件）推出上界。上限在代码里找不到的，写「不足以确定」，不挑一个数。
     - 判据：`SelectionOutboundBound` ＋ 人群项上界 ≤ `QueueByteLimit` ÷ 2（1,048,576 ÷ 2 ＝ 524,288；余量 524,288 － 454,235 ＝ 70,053 B，约 120 人 × 582 B）。不满足或不足以确定，按待裁定第 6 条。
     - 落盘：在 `selection-outbound-bound.md` 末尾补一节（带日期与基准提交），逐项写口径、实测、上界与加法。
  2. **F-61**（Host 取证收口复核，待裁定第 7 条）：
     - 核对三个打包进程夹具（`EndToEndHostHarness`、`LauncherHttpHostStartupTests`、`ChannelHostStartupTests`）仍把每行输出交给 `GameplayAssertionRecorder.ObserveHostOutput`；
     - 识别名单（`HostViolationTypes`）今天只有 `GameplayOffExecutorException`、`GameplayLeakedFlowException`；TASK-23 开启的 RQ-003 网络等待检查抛的是 `GameplaySelfWaitException`，不在名单上。按裁定补名单与 `HostOutputViolationTests` 的一行；
     - 补一条源码扫描判据：凡启动打包进程（`ProcessStartInfo`）的测试夹具，都把输出交给 `ObserveHostOutput`，配一条反例；先 RED 后 GREEN；
     - 「子进程中被吞掉、又没打印的违规」仍看不见，作为已知残余记入收口。
  3. **F-21**：核对停机排空的兜底拆除与 `TryRemove` 仲裁仍在（`TouchSocketTransportDrain`），停机测试仍通过（AC-007 第 4 句的用例名，执行时检索）；停服之外的关闭回调由 TASK-23 N9 覆盖。关闭 F-21。
  4. **F-108**：AC-011「线级」第 3 条的证据层级偏离，按待裁定第 5 条记入收口。
  5. **新建 `verification/t006-closure.md`**，仿 `t005-closure.md`（只写「交付 → 哪个 TASK → 证据位置 → 状态」，未关闭事项只引 F 编号）：
     - §1 Plan T-006 行逐项：执行器上的发送器、`ProjectAsync`、关连接改为登记发件箱；提交钩子与组装点；RQ-031(c) 刷出规则与 (d) 按收件人隔离；接上发送端；附录 E DR-1～DR-17；删除重试队列与排空服务；改写「发送即记录」替身（TASK-25 的替身清单终态）；开启 RQ-003「等待网络」检查；
     - §2 附录 F「删除」栏投递三项与 AC-017（检索为零，检索式重跑；RQ-061 两半：写断言自 TASK-18、发件箱一半自 TASK-23，各列反例用例）；
     - §3 附录 E 十七行逐行对账（以 TASK-24 §6 证据表为输入，重跑 `dr_evidence` 检索确认用例名在 TASK-25 之后仍可检索），AC-014；
     - §4 附录 G：VC-1～VC-6、VC-8 逐行（VC-3 已扩展到两塔进入，VC-6 的取值与 F-107），VC-7 引 `t005-closure.md`；
     - §5 附录 D：XO-1～XO-7 逐项的有序组与证据，AC-012；
     - §6 AC-007（断线、关连接、被顶替三种情形的生命周期 CMD；由 CMD 发起的关闭在此前登记的帧之后，严格形式；自关来源不自锁；停机落库）、AC-011（含 F-108）、AC-013（含人群项实测）、RQ-008（执行器不等待关连接、交付或传输回调：RQ-003 网络等待检查与 N9）、RQ-012、RQ-036（传输写入点判据、期限、队列上限与 2 倍余量）、RQ-061 发件箱一半、VC-8；
     - §7 「T-006 完成的条件」七条逐条：投递三项与删除名称检索为零；`ExecutorSendDeadline` 为零、2 秒只在 `OutboundLimits` 两个约定位置；作业内没有直接传输写入；附录 E 每行有经执行器与发件箱的证据；b1、b2 严格形式开启并通过；每个单元一次正式全量、取证反例类外为 0；
     - §8 移交清单（只列 F 编号与归属：T-007、T-008、T-009、M-2 与之后、外部审核）；
     - §9 结论与 **M-2 进入条件**：Plan Work 表中 M-2 依赖 T-006；逐条写成立与否；不成立的写缺什么、归谁；
     - §10 已接受的差异与证明边界：VC-3 扩展到两塔（TASK-23 F1）、GM Cera 对外字段（F-107）、F-102 的批次化、AC-011 线级第 3 条（F-108）、TASK-24 评审发现 3 与 4。
  6. `follow-ups.md`：关闭 F-21，F-61 按裁定关闭或改为信息；更新 F-69；文首补 TASK-26 一行。
- **检查**：一次正式全量加取证，反例类与对照以外为 0；表征 10 通过、0 跳过，金样本不变；F-61 判据先 RED 后 GREEN 与反例；人群项探针的还原证明；AC-017 检索式重跑；附录 E 证据用例名检索全部找到；`cr-at-eol` 口径 `diff --check`。
- **停止条件要点**：人群项上界突破 2 倍余量或不足以确定（不在收口单元改 `OutboundLimits`）；附录 E 某行的证据在 TASK-25 之后找不到；M-2 进入条件有一条不成立且不能在收口内补齐。
- **Result review**：required，因为收口是 M-2 的依据。

## 待裁定

1. **拆分为 TASK-25（替身改写与 F-102）与 TASK-26（T-006 收口）**（§0）。
   - **建议接受**。理由：本单元的证据是「只换驱动、断言不变」，收口要新增判据、临时探针与判断性对账，混在一起 `diffcheck` 与边界核对都要再扣除；收口作为 M-2 依据，应引用经结果审查之后的 TASK-25；本单元规模已大于 TASK-23、TASK-24。代价是多一次正式全量与结果审查。
   - 备选：不拆，TASK-26 提纲并入本单元作为最后一步。
   - 不改 `plan.md`：T-006 行只说交付什么，不规定单元数；主控方若要在 `plan.md` 里写明「T-006 实际为 TASK-23～TASK-26」，由主控方改。
2. **26 个投影替身（`ProjectionEventService` 子类 21、其他 `IEventService` 实现 5）不在本单元改写**（§2 P 类）。
   - 它们替代的是 handler 与事件服务之间的投影契约，断言的是「向哪群人投影了什么」；投影之后的组装与登记由 `NotificationDeliveryTests`、`NotificationWorld` 与 XO 用例经真实总线与发件箱证明，它们本身不在发送那一层记录。提纲的「69 个」也只指三个发送／关连接接口的直接实现。
   - **建议**：只分类（记录型／失败注入型／阻塞型）写进替身清单；失败注入型按 §6.2 判定可达性（返回 `Failed` 代表组装失败，仍可达，KB）；阻塞型逐个确认在单执行器下是否空转，空转的列入 F-87 一类，交 T-008 随功能簇改写。
   - 备选：全部换成真实 `EventService` ＋ `NotificationBus` ＋ 组装器。改动量约再加 20 个文件，且要为每个用例准备组装器所需的领域状态，与 T-008 的功能簇重写重叠。
3. **F-102 的登记路径，以及因此改义的 `Batches` 断言**（§8.2、停止条件 2）。
   - **建议 A**：总线经公开接缝 `INetworkMessageSender.SendBatchAsync` 登记（它本来就是「整批编码、登记一条」，登记点只此一处）。代价：`ShopFixture` 等门面的 `Batches` 会出现总线的批次，计数类断言（例如 `Assert.Single(f.Sender.Batches)`，规划时在效果结算类用例中见到十余处）若所在流程里有总线投影，就会改义。逐条列名，记 §9 第 1 类；超过 10 处停下回报。
   - 备选 B：给登记适配器加一个 internal 登记方法，总线直接用它登记；门面的 `Batches` 只记经 `SendBatchAsync` 的调用，断言不变。代价是生产中多一条登记路径，与 `OutboxAdapters.cs` 类注释「唯一的发送器」相悖，要一并改注释。
   - 不改 Spec，也不改已定共享契约：`INotificationBus` 的结果值与含义不变。
4. **18 条之外的失败注入，按 TASK-24 的可达性规则处置，预先授权删除「不再抛出」类**（§6.2）。
   - 规划时可见的有 `ShopSender.FailBatch`（16 处使用）、`FailExplicit`（5 处），以及 V4 会找出的其他替身内 `throw`。它们不在 TASK-23 交给 TASK-24 的 29 条名单里，TASK-24 没有判定过。
   - **建议**：同 TASK-24 主控方裁定第 6 条的口径：KE、KB 改为登记点注入或真实组装失败，断言不变；「不再抛出」的删除并写承接者（TASK-24 §6 证据表中对应 DR 行的 T 用例）；找不到承接者即停下（停止条件 3）。
   - 备选：不授权删除，遇到「不再抛出」类一律停下回报。
5. **AC-011「线级」第 3 条的证据层级偏离（F-108）在收口中的记法**（TASK-26 提纲第 4 项；会影响 Spec 验收是否记为满足）。
   - TASK-24 主控方裁定第 3 条已预先授权降级到「执行器 ＋ 真实适配器 ＋ 真实发送端」层，证据为 `SelectCharacterLifecycleTests.A_selection_compensated_after_publication_registers_the_peers_cleanup_notice_before_the_close`。
   - **建议**：不改 Spec；`t006-closure.md` 把 AC-011 记为「满足，其中线级第 3 条按裁定以发送端层证据代替，原因是普通频道的登录落点规则使同伴无法在场（F-108）」，交外部审核时说明。
   - 备选：改 `spec.md` AC-011 第 3 条线级回归的措辞为「发送端层回归」，使收口可以不带条件地记为满足。
6. **人群项上界突破 2 倍余量或不足以确定时怎么办**（TASK-26 提纲第 1 项；涉及 RQ-036、AC-013 与共享常量 `OutboundLimits.QueueByteLimit`）。
   - **建议**：TASK-26 不改 `OutboundLimits`，停下回报，由主控方在「调大队列上限（并重算判据）」「限制人群快照的单 CMD 字节数」「接受并在 AC-013 的对账中写明适用范围」之间决定。若实测与上界都在余量内，照常收口。
   - 备选：预先授权 TASK-26 在突破时把 `QueueByteLimit` 调到「新上界 × 2」并改 `OutboundQueueLimitTests` 的判据。
7. **F-61 的收口方式**（TASK-26 提纲第 2 项）。
   - **建议**：识别名单补 `GameplaySelfWaitException`（RQ-003 网络等待与自我等待都抛它，T-006 之后它是新的一类可能出现在子进程输出里的程序错误）；补源码扫描判据守住「打包进程夹具把输出交给 `ObserveHostOutput`」；「被吞掉又没打印的违规看不见」作为已知残余写进收口，F-61 改为「已复核，残余为信息」后关闭。
   - 备选：保持 F-61 未关闭，转 T-009 与 RQ-061 运行期判据全开一并处理。

## 主控方裁定（执行前）

1. **拆为 TASK-25（替身与驱动替换、F-102）与 TASK-26（收口），接受。**
2. **26 个投影替身只分类、不改写，接受。** 阻塞型逐个确认是否空转，空转的登记交 T-008。
3. **F-102 采用 A：走公开接缝 `SendBatchAsync`。** 不给适配器开第二条 internal 登记路径。`Batches` 计数断言因此改义的，逐条记录；超过 10 处时停下回报。`NotificationDeliveryTests.A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 中断言「first part 已登记」的那一处，是本单元唯一一处有意改义，单独记录。
4. **18 条之外的失败注入按 TASK-24 的可达性规则处置，接受；预先授权删除「不再抛出」类。** 每条删除都必须写明承接者，找不到承接者就停下。
5. **AC-011 线级第 3 条（F-108）不改 Spec，接受。** `t006-closure.md` 记为「满足，其中一条已裁定降级」，交外部审核时说明。
6. **人群项上界突破 2 倍余量或不足以确定时停下回报，接受。** TASK-26 不改 `OutboundLimits`。
7. **F-61 按建议收口，接受。** 识别名单补上 `GameplaySelfWaitException`，补源码扫描判据（打包进程夹具把输出交给 `ObserveHostOutput`）并配反例，「被吞掉又没打印的违规」作为已知残余写进收口，然后关闭。这部分在 TASK-26 执行。

## Blocker

执行轮次 1（2026-10-23，基准 `1ee1c91e`，未提交）。触发 **停止条件 2**（主控方裁定第 3 条）：F-102 按 A 方案（通知总线走公开接缝 `SendBatchAsync`）实施后，因「总线也登记批次」而改义的断言超过 10 处。按条件停下，F-102 的改动已从工作区还原，本单元其余未完成的步骤没有继续。

**不能继续的是什么**
- Outcome 7（F-102）。实施本身成立：两条新增用例先 RED 后 GREEN，`NotificationDeliveryTests` 16 条全绿；但 Application.Tests 全量随之出现 30 条非基线失败。
- 逐条取每个失败用例行的第一处失败断言：`Batches` 计数／内容断言 19 处 ＋ `Attempts` 断言 3 处 ＋ 由 `Attempts` 改义引起的 `Messages` 计数 1 处 ＝ 23 处 > 10。19 处 `Batches` 全部是 `ShopFixture` 系列（装备强化、附魔、锻造、外域净化、增幅、抽奖、自动售货、海拉斯券、魔法封印、装备升级券）里的 `Assert.Single/Empty(...Sender.Batches)`：这些流程的通知投影原先经 `SendAsync` 登记、不算批次，F-102 之后算。3 处 `Attempts` 是 18 条 F 类中「提交后不回滚」的三条（`NpcShopPurchaseTests`、`ItemSaleTests`、`CeraShopPurchaseTests`）：`Attempts` 只数单条 `SendAsync`，原先那一次就是总线的发送。这些只是每个用例的第一处失败，后面的断言没有展开。
- 受影响的验收：Outcome 7、RQ-031(d) 字面与实现的最后一处差异、Required checks 5（断言多重集合）、8（M1、M2）、11（F-102 证据）；F-102 之后才能做的 Outcome 8（F-104）、9（正式全量、取证、变异）也因此没有开始。

**直接证据**
- `verification/logs/task-25-f102-stop.txt`：RED／GREEN 命令与关键输出、全量失败行、23 处断言逐行（文件:行、命中次数、断言原文、类别）与加法。
- F-102 的完整改动（`NotificationBus.cs` 改为每收件人解析一次上下文、经 `SendBatchAsync` 整批登记；`NotificationWorld` 加上下文查询记录；新增 `A_recipient_whose_last_message_cannot_be_encoded_registers_none_of_its_messages`、`A_recipient_with_several_messages_is_resolved_once_and_registered_as_one_batch`；`A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered` 的登记列表断言由 `[(First, "first part"), (Second, …)]` 改为 `[(Second, …)]`）保存在 scratchpad `t25/f102.patch`（`git diff` 输出，204 行），`git apply` 即可恢复。
- V12 已核对：全部 Game 编码器经 `GameServerFrameEncoder.Encode`，它对超过 `DnfFrameProtocol.MaximumFrameLength` 的帧先抛出；所以 `EncodeBatch` 多出的帧长检查对通知不可达，停止条件 9 不成立。

**需要主控方决定的**（任选其一，或另给方案）
1. 接受这 23 处按 §9 第 1 类逐条改写（例如 `Assert.Single(...Batches)` 改为只看本次回放批次，`Attempts` 改为同时计批次调用），上限从 10 处放宽；
2. 改用裁定第 3 条的备选 B（适配器开 internal 登记方法，总线直接用它，门面 `Batches` 不变），代价是生产里多一条登记路径并改 `OutboxAdapters.cs` 注释（超出本单元 Owned boundary，需授权）；
3. 门面 `Batches`／`Attempts` 不计来自通知总线的调用（只在测试侧改；代价是门面要识别调用方，`Batches` 的含义变成「handler 自己登记的批次」，需在处置记录里写明）。

**本轮已完成、仍在工作区的部分**（供恢复时接着做）
- 共用支撑：`tests/Support/RecordedOutbound.cs`（真实 `GameplayOutboxes`、`OutboxNetworkMessageSender`、`OutboxConnectionCloser` 之上的记录与门面；登记点回调；`FaultingEncoder` 注入；按协议注册的 `RegistrationEncoder`）、`RecordedOutbound.Game.cs`（只链接到能引用 Protocol 的项目）、`OutboundRegistrationParts.cs`（从 `XoDelivery.cs` 拆出 `XoSubmissions`、`XoConnections`、`FaultingEncoder`、`NameEncoder`，命名空间不变，只有一份实现；`XoConnections` 加首次见到即登记，`FaultingEncoder` 加内层编码器参数与按连接判定的注入）；四个测试项目 csproj 加链接。
- 替身改写：基准上的 C1、C2 全部改完（`ShopFixture`／`ShopSender`、`JoustIntegrationTests` 夹具，以及 64 个文件中的其余类）；留作 C3 的 3 个：`ExplicitMessageDeliveryContractTests.RecordingSender`（测的就是接口默认方法）、`SessionGenerationOwnershipTests.NoopSender`／`RecordingSender`（没有任何用例使用）。改写后仍实现发送／关闭接口的类，都是把三个方法转给真实适配器的包装（§3.4），清单与逐类核对没有落盘。
- 18 条 F 类的注入全部改到登记点（`FaultingEncoder`），异常类型与消息沿用原替身。
- 作业外调用放进作业（§5.3）：`HerasEntryCouponTests.Malformed_requests_finish_with_refusal_without_touching_the_bag`（2 行）、`ChannelConnectHandlerTests` 1 条、`ChannelControlMessageHandlerTests` 3 条、`GameExitMessageHandlerTests` 1 条、`ExitFlushHandlerTests` 3 条、`SelectDungeonHandlerTests.Refusals_preserve_the_actual_reason_and_allow_retry`（15 行）、`ReturnToSelectionTests.The_return_batch_uses_the_reference_order_and_cmd_envelope`（2 行）。
- 删除 1 条：`CollectionBoxNetworkSettlementTests.Refusal_is_not_delivered_to_a_connection_replaced_before_transport_lookup`。它断言调用点抛 `IOException`，这个异常只来自替身对物理连接被替换的模拟；登记后由发送端判 `Stale`（RQ-035），登记点不再抛出。承接：同文件改写后的 `Ack_cannot_follow_a_reused_connection_id_after_the_inventory_batch`（同一个 `SendBatchAsync(context.Connection)` 发送点的精确连接绑定），以及 `OutboundSenderTests.An_entry_for_a_replaced_connection_is_dropped_and_never_reaches_the_new_one`、`OutboundRealSocketTests.An_entry_for_an_equal_but_not_identical_context_is_stale`。
- 当前状态（F-102 已还原）：`dotnet build Server/86JP.Server.slnx -c Release` 0 个警告、0 个错误；Application 4534 条中失败 2（`DimensionDungeonTests` 基线 2 条）、跳过 16（4535 － 1 条删除 ＝ 4534）；Protocol 1746 全部通过；Persistence 477 全部通过；Host 只跑了 `GmEventsHttpTests` 11 条，全部通过；`git -c core.whitespace=cr-at-eol diff --check` 无输出；`Server/src` 无改动。`GmEventsHttpTests.cs` 是混合行尾（118 行 CRLF），改动处原为 LF，改后 CRLF 仍为 118 行。
- `diffcheck7.py 1ee1c91e`：方法 707 → 706（即上面那 1 条删除）；断言行集合不等的 7 个文件中，`NpcShopPurchaseTests` 是 `ShopSender.SendBatchAsync` 内两条替身断言（§9 第 2 类，由真实编码与端点检查承接），`CollectionBoxNetworkSettlementTests` 是删除，其余 5 个（`CommonExperienceEffectTests`、`CharacterDeathHandlerTests`、`DungeonLoadingProjectionDeliveryTests`、`ItemUsageProjectionTests`、`WeaknessProjectionTests`）是替身内的断言表达式不变、所在行的其他记录语句被改写，逐条处置还没写。

**与计划或 V1 不一致之处（恢复时须一并处理）**
- V1：`t23/doubles.py` 只认基类列表与声明同行的类，主构造器参数换行的 6 个类漏数（`SelectCharacterLifecycleTests.RecordingSender`、`RecordingNotifications`，`OnlineUserServiceTests.RecordingInitialSender`，两塔与 `PartySelectDungeonHandlerTests` 的 `Sender`）。其中 5 个是发送替身，且 DR-1、DR-2、DR-3、DR-12 的 F 类正经过它们，所以一并改写。补了扫描脚本 scratchpad `t25/doubles2.py`：基准上 100 个类、68 个文件（94 ＋ 6）。
- 部分门面在登记点保留了「调用级」记录，供原断言读取：`IllusionTowerRewardProjectionTests.FailingSender.Attempts`（重试复用同一个投递对象，`Assert.Same` 要求）、两塔 `Sender.Attempts`（连接号）、`DungeonCardPresentationCoordinatorTests.Seen`、`SelectCharacterLifecycleTests` 的 ledger 与 `StateMessageCount`。它们记的是「到达登记点的调用」，不是「发出了什么」；处置记录里要逐个写明。
- `CommonExperienceEffectTests.CallbackCloser.RequestCloseAsync` 保留抛 `InvalidOperationException("ID-only close is forbidden here.")`：它是「此处禁止按 ID 关闭」的守卫，TASK-24 V13 已判定它不被调用；严格按 §3.4「包装不抛异常」需要主控方确认是否接受。
- `EquipmentEnchantHandlerTests.A_partial_notification_failure_can_be_replayed_without_a_second_debit` 的 `BeforeSend` 回调按 `Attempts == 2` 抛异常，属于 §6.2 的其余注入，还没改到 `FaultingEncoder`。

**尚未开始**：§6.2 可达性清单（`logs/task-25-injections.txt`）、`logs/task-25-doubles.txt` 与 `task-25-verify.txt` 的落盘、F-101 的处置记录行（三条用例已随 `ShopFixture` 改到登记点回调，全量通过）、F-104、正式全量与取证、×10／×3、变异、边界、`t006-test-dispositions.md` TASK-25 一节、`follow-ups.md`。

**下一步可执行的动作**：主控方就上面三个选项定一个；按选择恢复 `t25/f102.patch`（或按 B 改适配器），完成 F-102 的断言处置，然后接着做「尚未开始」各项。

## 主控方处置（Blocker 之后）

处置为 `task-rework`，合同边界不变；只把裁定第 3 条的停止上限从 10 处提高到 30 处。

1. **采用选项 1：恢复 F-102 补丁，把这 23 处按 §9 第 1 类逐条改写。**
   - 不采用选项 2：它会在生产中多出第二条登记路径，正是裁定第 3 条要避免的。
   - 不采用选项 3：门面要「认出总线的调用」，等于在测试支撑里再造一层调用方识别，`Batches` 的含义会变得隐晦。
   - 改写规则：
     - 每处改写都保住原断言的区分力。例如把 `Assert.Single(Sender.Batches)` 改为对 handler 自己那一批的断言（按消息类型或收件人筛选），并在合适处另加一条断言，说明通知投影作为整批登记。
     - `Attempts` 一类的计数改义，逐条写明新旧计数口径（逐条登记的调用次数改为每收件人一批）和期望值的推导。
     - 逐条记入 `t006-test-dispositions.md`。
   - 若某一处无法在保住区分力的前提下改写，或总数超过 30，停下回报。
2. **`CommonExperienceEffectTests.CallbackCloser` 保留按 ID 关闭时抛出的守卫，接受**，前提是它是测试自身的不变量检查（关闭了不该关的连接就让用例失败），不是失败注入。在处置记录中写明。
3. **`EquipmentEnchantHandlerTests` 中按次数抛异常的回调**，按 §6.2 改到编码器注入，并随第 1 条的 `Attempts` 改义一起处理。
4. 之后照计划完成尚未开始的各项（§6.2 可达性清单、替身清单与核实日志、F-101 处置与关闭、F-104、正式全量与取证、×10／×3、变异、边界核对、处置记录、`follow-ups.md`），Required checks 全部重跑。构建以 `--no-incremental` 为准。

## 主控方处置之后的执行（第 2 轮）

上面的 `## Blocker` 已由「主控方处置（Blocker 之后）」处置为 `task-rework`：恢复 F-102 补丁（`git apply` scratchpad `t25/f102.patch`），按 §9 第 1 类逐条改写，上限 30 处。Blocker 一节保留作为第 1 轮的记录，下面的 Result 是处置之后的结果。

## Result

- **Outcome**：done（执行两轮：第 1 轮在 F-102 处触发停止条件 2，见上面的 Blocker；第 2 轮按主控方处置完成）。
- **Changed**：
  1. **共用观察支撑**（`Server/tests/Support/`，只有一份实现）：新建 `RecordedOutbound.cs`、`RecordedOutbound.Game.cs`、`OutboundRegistrationParts.cs`。后者是从 `XoDelivery.cs` 拆出的 `XoSubmissions`、`XoConnections`、`FaultingEncoder`、`NameEncoder`，命名空间不变，XO 用例一字未改；`XoConnections` 加「首次见到即登记」，`FaultingEncoder` 加内层编码器参数与按连接判定的注入。Application、Protocol、Host、Persistence 四个测试项目的 csproj 加链接（Persistence 不引用 Protocol，所以只链接前两个文件）。
  2. **替身改写**：基准 100 个类（`t23/doubles.py` 的 94 ＋ 漏数的 6）＝ C1 16 ＋ C2 54 ＋ C3 3 ＋ P 27；T8 0。改写的 70 个终态：转给真实适配器的包装 65、只读门面 4、删除 1。`ShopFixture`／`ShopSender`（106 个文件使用）与 Joust 夹具改为 `RecordedOutbound.AddTo`。逐类见 `logs/task-25-doubles.txt`。
  3. **18 条 F 类**改为登记点（`FaultingEncoder`）注入，异常类型与消息沿用原替身；§6.2 其余 15 条注入同为 KE，保留；守卫 61 处（第 1 轮评审后）。`EquipmentEnchantHandlerTests` 按次数抛异常的回调改为编码器注入（主控方处置第 3 条）。
  4. **F-102**：`Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` 每收件人只查一次上下文，经 `SendBatchAsync` 整批登记；新增 2 条用例，改写 1 条计划内断言；由此改义的 24 处断言按 §9 第 1 类改写（19 处 `Batches` 限定为重放批，并在 15 处另加「投影作为整批登记」的断言；3 处 `Attempts` 口径改义；2 处部分通知失败），共用判定 `EffectRequestTransport.IsStateReplay`。
  5. **删除 1 条**：`CollectionBoxNetworkSettlementTests.Refusal_is_not_delivered_to_a_connection_replaced_before_transport_lookup`（不再抛出，承接者见处置记录 §5）。
  6. **作业外调用放进作业**：11 个方法、27 个运行例。
  7. **F-104**：`GmEventsHttpTests`（CRLF 混合文件，改动处原为 LF）补两处执行器屏障，`GmOfficialMailHttpTests` 加一处屏障与 `Settled` 辅助方法。
  8. **文档与证据**：`verification/t006-test-dispositions.md` 新增 TASK-25 一节；`verification/follow-ups.md` 关闭 F-101、F-102、F-104，更新 F-69、F-87，新增 F-111；`verification/logs/task-25-{verify,doubles,injections,build-test,violations,diff-check,new-x10,mutations,boundary,isolated,f102-stop}.txt`。
- **Why**：
  1. **V1 少数 6 个类，一并改写**：`t23/doubles.py` 只认基类列表与声明同行的类，漏了主构造器参数换行的 6 个类（5 个发送替身、1 个投影替身）。DR-1、DR-2、DR-3、DR-12 的 F 类必须经过这 5 个发送替身，所以一并改写，并补了扫描脚本 `t25/doubles2.py`。V1 本身与规划一致（94／68），差的是脚本盲区，不是基准变了。
  2. **门面多数仍实现接口**：C2 的被测对象多数用构造参数拿发送器，让原类名变成「三个方法都转给真实适配器」的包装（§3.4），用例体只改构造实参，断言行不动。C2 包装各自持有一份独立的 `RecordedOutbound`（作业令牌决定发件箱，任何执行器的作业都能登记）；C1 用容器里 `AddTo` 换上的那一份。同一 C2 用例里发送器与关闭器各有一份发件箱，这些用例原本就分别读取两者，不读二者之间的顺序；代价是 C2 包装的登记不在被测容器的工作单元里，回滚丢弃对它们不生效（处置记录 §6.4，旧替身同样看不到）。
  3. **作业内读取包含未刷出的条目**：作业内读取时补上当前发件箱里尚未刷出的条目（用反射读 `GameplayOutbox` 的私有槽位，回滚与不确定的单元不计），时机与旧替身「调用即可见」一致。这类读取证明不了作业结束才刷出；作业之后读取的用例由 M3 证明确实经过刷出（下面 Checks 8）。
  4. **F-102 改写的口径**：`Batches` 在这些用例里原本就等于「重放批」（投影原来逐条 `SendAsync`，不算批次）。所以按内容（库存快照）筛出重放批，保住原断言的区分力；投影改为整批之后，再加一条断言把这个整批钉住。M1 下这 15 处加的断言全部变红，说明它们守着 F-102。
  5. **`Attempts` 改为单条发送与整批登记一起计**：这样「失败不重试」在 F-102 之后仍由同一行断言 `Assert.Equal(1, …)` 表达，期望值的推导写在处置记录 §6.1。
  6. **删除而不是改写 CollectionBox 那一条**：它断言的 `IOException` 只来自替身对传输的模拟，登记点在生产中不会抛；属于「不再抛出」，依裁定第 4 条删除并写承接者。
  7. **`CallbackCloser` 保留守卫**：它是测试自身的不变量检查，不是失败注入（主控方处置第 2 条）。
- **Checks**（第 2 轮，日志都在 `verification/logs/`）：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 两次，正式全量前一次、变异系列后一次，都是 0 个警告、0 个错误（`task-25-build-test.txt` 文首、`task-25-mutations.txt` 末尾）。
  2. **正式全量**（`task-25-build-test.txt`，211 秒）：
     - 总数：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1746 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 179 ＋ Application 4536 ＋ Host 190 ＝ 8232 ＝ 8231 ＋ 2（N1、N2）－ 1（CollectionBox 那一条）。逐项目只有 Application 变化：4535 ＋ 2 － 1 ＝ 4536。
     - 结果：通过 8212；失败 2，都是 `DimensionDungeonTests` 基线；跳过 18，不变。8212 ＋ 2 ＋ 18 ＝ 8232。挂起转储 0。
     - 表征测试 10 通过、0 跳过；`Characterization/` 与 `Golden/` 无改动。
     - 日志信号：
       - 应为 0 的七项为 0；bus 8 项、inbound 8 项与 TASK-24 逐条相同；
       - gm 由 4 变为 2：少的两条 `cera/grant` 来自 TASK-24 评审后的改动（`1ee1c91e` 中提醒失败改由 GmWeb 记录），与本单元无关；
       - `Outbound connection … closed` 8 ＝ Failed 7 ＋ TimedOut 1：多出的 Failed 是 Networking 进程内宿主停机时「send pipe drained」的时序行；隔离运行是 6 ＝ Failed 5 ＋ TimedOut 1，TASK-24 的记录是 7。
     - F-69：多出 ≥ 9 秒的 15 条，隔离下 2～15 秒，是满载拉长。
  3. **取证**（`task-25-violations.txt`）：预期先写在日志开头。实际 40 条 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ write-off-executor 37；反例类与对照之外 0，没有测试帧 0，`exe:` 0，退出码 0。期望表未改，没有执行器外的新记录。
  4. **替身清单终态**（`task-25-doubles.txt`）：
     - 基准 100 个类 ＝ 改写 70（C1 16 ＋ C2 54）＋ 不改 3 ＋ 交 T-008 0 ＋ 投影 27；
     - 改写的 70 ＝ 包装 65 ＋ 只读门面 4 ＋ 删除 1；
     - 工作区 97 个类 ＝ 包装 65 ＋ C3 3 ＋ P 27 ＋ 支撑自身的转发包装 2。
  5. **测试改动核对**（`task-25-diff-check.txt`）：
     - 方法只少 D 的 1 条、只多 N 的 2 条。
     - 断言行多重集合不同的 20 个文件（第 1 轮评审后；之前 18）＝ §6.1 的 12 个 ＋ §8 的 1 个 ＋ §5 的 1 个 ＋ §9-2 的 1 个 ＋ 断言表达式不变、只改了所在行的 5 个，每个文件对到处置记录的一行。
     - `ShopSender` 内两条替身断言单列为 §9 第 2 类。
     - AC-009：方法体逐字相同，断言 8 行的多重集合相同。
  6. **可达性清单**（`task-25-injections.txt`）：
     - 注入点 79 ＝ KE 18 ＋ G 61（第 1 轮评审后；之前 78 ＝ 18 ＋ 60）；
     - 打开注入的用例 33 ＝ 18 条 F 类 ＋ 15 条其他，全部 KE；
     - 未分类 0；「不再抛出」0。
  7. **×10／×3**（`task-25-new-x10.txt`）：
     - ×10：Application 28 条 × 10、Protocol 9 条 × 10，全部通过。DR-12 漏在过滤器外（类名是 `UserServiceTests`，不是文件名），单独补跑 10 次，全过。合计 Application 29 ＋ Protocol 9 条。
     - ×3：Application 全项目 4536、Protocol 全项目 1746、Host 61、Persistence 8，三次结果相同（只有基线 2 条失败）。
  8. **变异**（`task-25-mutations.txt`）：
     - M1 下 N1、N2、改写的那条断言变红，另加的投影整批断言（锻造 1、抽奖 4）也变红；M2 下 N2 变红；M3 下抽查的五个文件都变红；M4 下关闭器改写过的三个文件共 8 条变红；M6a、M6b、M6c 下对应的 F 类变红；M7 下 18 条全红（第 3 轮 15 条，第 1 轮评审后补断言、第 4 轮重跑余下 3 条）；M5 下 `OutboxWorkUnitTests` 2 条变红（第 4 轮）。
     - 对照：M3 下 3 个基准文件保持绿；M4 下基准 `Owner_send_failure` 保持绿。
     - 与计划不同（Remaining 2）：M5 在改写后的用例里没有找到可变红的；`GmEventsHttpTests` 做不了对照。
     - 每一项都按字节还原并 `cmp`；`git diff` sha1 前后相同。前四项在第 1 轮编译失败，作废后重跑（日志开头写明）。
  9. **边界**（`task-25-boundary.txt`）：`Server/src` 只改 `NotificationBus.cs`；`Server/src` 下没有未跟踪文件；`spec.md`、`plan.md`、`AGENTS.md`、`Docs/` 无改动。
  10. **隔离复跑**（`task-25-isolated.txt`）：五个项目全部通过（Application 只有基线 2 条失败）。各自的取证：Application 31 ＝ 反例 30 ＋ 对照 1；Networking 4 ＝ 反例 3 ＋ 对照 1；Protocol、Host、Persistence 各只有对照 1；反例类之外都是 0。
  11. **F-102 与 F-101 的证据**：写在处置记录 TASK-25 §7～§9；RED／GREEN 的输出在 `task-25-f102-stop.txt`。
  12. **行尾与收尾**：`git -c core.whitespace=cr-at-eol diff --check` 无输出。逐个文件比较行尾种类，变化 0；`GmEventsHttpTests.cs` 的 CRLF 行仍是 118 行，另两个 CRLF 候选文件未改。`pgrep -x yes` 为 0，没有加负载。结束时执行了 `dotnet build-server shutdown`。
  13. **TDD**：F-102 的两条新用例先 RED（退出码 1，失败原因就是缺失的行为），后 GREEN（退出码 0）。命令与输出见 `task-25-f102-stop.txt` §1、§2。
- **Remaining**：
  1. **TASK-26 的范围不在本单元**：人群项出站实测、F-61、F-21、F-108、`t006-closure.md`、M-2 进入条件。
  2. **M5 没有找到可变红的改写用例**：改写后的用例里，没有一条在作业之后断言「回滚的路径没有发出」。RQ-031(b) 的证据仍是 `OutboxWorkUnitTests`（TASK-23 N5）。
  3. **（已在第 1 轮评审后处理）** 原写「M7 下有 3 条 F 类不变红，基准上也一样」。改正：「基准上也一样」只对 `ItemSaleTests` 成立；`NpcShopPurchaseTests` 与 `CeraShopPurchaseTests` 在基准上组装 3 条消息，注入失效时 `Attempts` 会是 3。三条都已补「注入点之后什么都没登记」的断言，M7 局部重跑 3 条全红，合计 18 条全红。
  4. **新增 F-111、F-112**：F-111 是 4 个没有任何用例使用的替身；F-112 是 C2 包装独立发件箱，不能作为 RQ-031(b) 的证据。都交 T-008。F-87 追加 1 个名字已过时的用例（`EquipmentEnchantHandlerTests.A_partial_notification_failure_can_be_replayed_without_a_second_debit`）。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，唯一的阻断项是发现 1。按 `task-rework` 处理，五条全部在本单元修：

1. **发现 1（阻断）**：`NpcShopPurchaseTests.cs:240`、`CeraShopPurchaseTests.cs:366`（`ItemSaleTests` 同样处理）补一条「该收件人在注入点之后什么都没登记」的断言（例如不含 `BuyItemResponse`，或该收件人的已登记消息为空），恢复原断言「失败发生在第一条、之后不再尝试」的区分力。对这三条局部重跑 M7，应达到 18 条全红。改正 `logs/task-25-mutations.txt` 表头、Result「Remaining 3」与处置记录 §6.1 #20～#22 的口径推导（「基准上也一样」只对 ItemSale 成立）。
2. **发现 2**：§6.1 #10、#17、#18 改为在被拒请求前后比较 `Batches.Count`，断言没有变化；`IsStateReplay` 改为按类型判定，不再按类型名字符串。
3. **发现 3**：在 `follow-ups.md` 新增一项：C2 包装各自持有独立发件箱，这些用例证明不了「事务回滚但消息已发」不会发生，不能作为 RQ-031(b) 的证据，交 T-008；TASK-26 收口时引用这一项。
4. **发现 4**：`GameLoginTests.RecordingSender` 补上 `outbound.RefuseExplicit(...)`，与同类文件一致。
5. **发现 5**：在 M5 下加跑 `OutboxWorkUnitTests` 并记录它变红。

处理后：Release `--no-incremental` 构建；受影响测试文件的过滤运行与 M7、M5 局部重跑；`cr-at-eol` 口径 `diff --check`；在 Result 中补一节「第 1 轮评审后修改」。只改测试与记录，不需要重跑正式全量，除非改动波及共用支撑文件（那时重跑 Application 与 Protocol 两个项目）。

## 第 1 轮评审后修改

按上面「主控方处置（第 1 轮评审后）」第 1～5 条修改，只改测试与记录，没有改生产代码和共用支撑文件（`tests/Support/` 未动）。

- **Changed**：
  1. **发现 1**：`NpcShopPurchaseTests.cs:241`、`ItemSaleTests.cs:245`、`CeraShopPurchaseTests.cs:367` 各加 `Assert.Empty(…Sender.Messages)`，钉住「该收件人在注入点之后什么都没登记」。处置记录 §6.1 #20～#22 的口径推导已改正，「基准上也一样」只对 ItemSale 成立；变异日志表头与 Remaining 3 同步改正。
  2. **发现 2**：§6.1 #10、#17、#18 改为在被拒请求前后比较 `Batches.Count`（`EquipmentEnchantHandlerTests.cs:146`、`EquipmentReinforcementHandlerTests.cs:172`、`:189`）。`EffectRequestTransport.IsStateReplay` 改为按类型判定：`InventorySnapshotMessage` 的 `Type` 按全名从 Protocol 程序集取一次（`throwOnError: true`），不再比较类型名字符串。
  3. **发现 3**：`follow-ups.md` 新增 F-112；处置记录 §6.4 引用它。
  4. **发现 4**：`GameLoginTests.RecordingSender` 补上 `outbound.RefuseExplicit(...)`，异常文字与旧替身相同。注入清单的守卫因此多 1 处，注入点 79 ＝ KE 18 ＋ G 61。
  5. **发现 5**：M5 下加跑 `OutboxWorkUnitTests`，2 条变红。
  6. 处置记录新增 TASK-25 §11。`logs/task-25-mutations.txt`（加第 4 轮）、`task-25-diff-check.txt`、`task-25-injections.txt`、`task-25-verify.txt` 已重新生成。
- **Checks**：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 两次（返工后一次、第 4 轮变异后一次），都是 0 个警告、0 个错误。
  2. **过滤运行**：`FullyQualifiedName~Dfo.Server.Application.Tests.Inventory|FullyQualifiedName~GameLoginTests` 共 1566 条，通过 1551、跳过 15、失败 0；第 4 轮变异后重建再跑一次，结果相同。Inventory 全部类都在内，因为 `IsStateReplay` 在 `EffectRequestTransport` 里，被这些类共用。
  3. **变异第 4 轮**：
     - M7r：CeraShop、ItemSale、NpcShop 三条全红，M7 合计 15 ＋ 3 ＝ 18 条全红；
     - M5r：`OutboxWorkUnitTests` 4 条中 2 条变红（`Of_two_writable_transactions_only_the_one_that_commits_is_flushed`、`Entries_of_a_transaction_whose_outcome_is_uncertain_are_dropped`）；
     - 每项都按字节还原、`cmp` 退出码 0；`git diff -- Server/src` 与 `-- Server/tests` 的 sha1 前后相同。
  4. **diffcheck**：方法只少 1 条、只多 2 条（不变）。断言行多重集合不同的文件 20 个，比之前多出的是 ItemSale 与 CeraShop 新加的那一行。
  5. **行尾**：`git -c core.whitespace=cr-at-eol diff --check` 无输出；行尾种类变化 0。
  6. **没有重跑正式全量与 Application、Protocol 两个全项目**：共用支撑文件 `tests/Support/` 未改动。
