# T-009 收口对账（TASK-34：T-009e，spec-001 收口）

- 日期 2026-10-24。冻结点 `2bdb1afd`；Spec 基准 `a02bce46`；规划检查点 `b76504bb`（测试数量基线 7872，`baseline.md`）。本单元开始时 HEAD 为 `51c294fa`。`git diff 2bdb1afd -- Server Tool` 在开始、正式全量前与结束时都为空（`logs/task-34-verify.txt`、`logs/task-34-build-test.txt` part 0、`logs/task-34-boundary.txt`）。
- 用途：spec-001 封存与完整结果评审的输入。要点是：Plan T-009 逐项对到证据；A 封存状态的最终全量；从基线到 A 封存的测试数量加法；测试文件改动清单；表征与金样本；移交清单；交主控方的封存数据。
- 写法：仿 `t006-closure.md`，只写「要求 → 单元 → 证据 → 状态」。未关闭事项只引 F 编号，内容在 `follow-ups.md`；推导过程在 `tasks/TASK-30.md`～`tasks/TASK-34.md`。
- 文件:行 指冻结点；路径相对 `Server/`，另有注明的除外。日志路径相对 `verification/`。

## 1. Plan T-009 逐项

依据是 `plan.md` R3 中 T-009 修订后的内容。

| 要求 | 单元 | 证据 | 状态 |
|---|---|---|---|
| RQ-061 运行期判据的最终核对：不设启动期例外（F-16），原行括注按 F-60 改正 | TASK-30 | `t009-criteria.md` §1、§2；本单元正式全量取证 73 条，反例类与对照以外 0、`exe:` 0、没有测试帧 0（`logs/task-34-violations.txt`） | 满足 |
| A 的判据加固与反例对账（F-76；RQ-072 的 A 一半） | TASK-30 | `t009-criteria.md` §4（34 条判据，每条有反例或变异）、§5 | 满足 |
| A 期间改写的判据逐条对账（RQ-071、AC-018 的 A 一半） | TASK-30 | `t009-criteria.md` §3（11 条：旧断言 → 新断言 → 保护意图 → 反例） | 满足 |
| AC-009 的 A 一半 | TASK-31、TASK-33、本单元 | ①②④ `t009-docs-and-appendices.md` §10、§7；③ `t009-logs-and-cases.md` §5；①的「通过」：本单元正式全量中 193 个方法全部通过，共 332 行（§3.3） | 满足 |
| RQ-024 的 A 一半：A 期间提交点有没有合并或拆分 | TASK-33 | `t009-docs-and-appendices.md` §9 | 满足 |
| 附录 A、B、E、F 的最终对账；附录 H 中属于 A 的各行（F-62、F-75、F-85、F-97、F-99） | TASK-30、TASK-33 | `t009-criteria.md` §7；`t009-docs-and-appendices.md` §3～§7 | 满足 |
| 文档：RQ-100 的 A 一半（F-35、F-36、F-89、F-92、F-105、F-118），F-110、F-122 的 A 一半 | TASK-33、本单元 | `t009-docs-and-appendices.md` §1、§13；本单元改了 `AGENTS.md`，2 个 hunk、3 处改动：过渡态 T3 一条补上四个标记接口（`transition-mechanisms.md` §4）；偶发栈根表的标题改为「四条」，并追加第四个栈根（§2.2） | 满足 |
| 过渡机制清单（根 SC-12）与交给子 Spec B 的移交清单 | 本单元 | `transition-mechanisms.md`；`logs/task-34-transition.txt`；移交清单在本文 §6 | 满足 |
| 最终测量（RQ-090、RQ-091；关闭 AC-005 的测量一半、AC-021、F-02） | TASK-32 | `measurement/m3-summary.md` §1：「M-3 通过」 | 满足 |
| 最终构建与全量测试，测试数量加法（RQ-081～RQ-083） | 本单元 | §2、§3、§4 | 满足 |
| 改道后留在 A、归 T-009 的 follow-ups：F-30、F-31、F-32、F-33、F-87（改名一半）、F-98、F-111、F-121 | TASK-31、TASK-33 | `t009-logs-and-cases.md` §1～§3、§5～§7；F-98 由 VC-10 承接（`t009-docs-and-appendices.md` §8）。F-30 的命令层一半是过渡机制 T5，F-33 的临时文件偏离列为外部审核时说明的已接受偏离 | 满足 |

覆盖栏逐条：
- RQ-061：TASK-30（`t009-criteria.md` §1）；
- RQ-023 的 A 一半：保护在今天的位置原样保留，由 AC-009 的既有测试证明（§3.3）；
- RQ-024 的 A 一半：`t009-docs-and-appendices.md` §9；
- RQ-071、RQ-072 的 A 一半：`t009-criteria.md` §3、§4；
- RQ-100 的 A 一半：`t009-docs-and-appendices.md` §1；
- RQ-080：§5；
- RQ-081～RQ-083：§2～§5；
- RQ-090～RQ-091：`measurement/m3-summary.md`；
- 附录 A、B、E、F：`t009-docs-and-appendices.md` §3～§6；附录 H 中属于 A 的各行：同文件 §7；
- 根 SC-10：`t009-docs-and-appendices.md` §1；根 SC-11：`a-final-report.md`；根 SC-12：`transition-mechanisms.md`。

## 2. 最终构建与正式全量（AC-020、RQ-083、子契约 SC-8）

### 2.1 结果

- 命令：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`；`DFO_GAMEPLAY_ASSERTIONS=<证据目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`。脚本 scratchpad `t34/formal.sh`。
- 原始输出：`logs/task-34-build-test-raw.txt`（TASK-34 主控方裁定第 1 条）。摘要与判定：`logs/task-34-build-test.txt` 文首。
- 构建：0 警告、0 错误。
- 运行：2026-10-23T23:43:47Z～23:46:48Z（UTC），181 s。开始时 swap 已用 9273.5／10240 MB，最大 RSS 11235803136 B。只作记录（F-69）。
- 总数 8315 ＝ HttpApi 57 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 188 ＋ Application 4594 ＋ Host 197，逐项目与 TASK-31 相同。逐步：57 ＋ 80 ＝ 137；＋ 1752 ＝ 1889；＋ 477 ＝ 2366；＋ 970 ＝ 3336；＋ 188 ＝ 3524；＋ 4594 ＝ 8118；＋ 197 ＝ 8315。
- 通过 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 188 ＋ 4575 ＋ 197 ＝ 8294；失败 3；跳过 2 ＋ 16 ＝ 18；8294 ＋ 3 ＋ 18 ＝ 8315。
- 失败 3 ＝ 两条基线 `DimensionDungeonTests`（`baseline.md`）＋ `AchievementTitleBookTests.Real_title_replaces_mirror_in_one_transaction_and_refusal_or_rollback_preserves_old_wear(mode: 0)` 1 条。后者经主控方处置（`tasks/TASK-34.md`「主控方处置（Blocker 之后）」）后登记为 SQLite 并行释放偶发的第四个栈根（§2.2），属 RQ-083 允许的「AGENTS.md 已登记的偶发项」。正式全量不重跑。
- 表征测试 10 通过、0 跳过；挂起转储 0。
- 取证 73 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 69，各预期类相符；反例类与对照以外 0，`exe:` 0，没有测试帧 0（`logs/task-34-violations.txt`）。
- 日志信号：「必须为 0」的七项与执行器失败的逐类型计数，都与 TASK-31 相同。与 TASK-31 不同的两项已逐条归因（`logs/task-34-build-test.txt`「Attribution」一节）：
  - 发送端成对行 8 对：五次重置 5 对（4 对为带后缀的 Information，1 对为 Warning 的 `Failed`，异常是 `SocketException (32) Broken pipe`，属 F-126 的形态，不是 F-127 的「Writing is not allowed after writer was completed」）、慢读者 TimedOut 1 对、进程内宿主在发送途中或停止后的 2 对（都是带后缀的 Information）；
  - 「the drain tears it down」3 行：进程内宿主停止时仍有连接登记，属 RQ-009 的兜底拆除。历次分别是 TASK-29 2 行、TASK-30 5 行、TASK-31 1 行、本单元 3 行，单独串行运行 Networking 时 0 行，随负载变化。
- F-69（只作信息）：与 TASK-31 的正式全量相比，多出 ≥ 9 秒的用例 5 条（＋9～＋18 秒；Host 4 条、Application 1 条）。单独运行两次，每次 1～4 秒，是满载拉长，不是排空（`logs/task-34-build-test.txt` 末尾「F-69」一节）。

### 2.2 新栈根的不归因依据（主控方处置第 1、2 条）

- 失败的身份：`ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`，抛在 `sqlite3_prepare_v2`；栈根 `AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery`（`tests/Dfo.Server.Application.Tests/Achievements/AchievementTitleBookTests.cs:446`，由 Fixture 构造器在 `:437` 调用）。原始输出第 6081～6107 行。
- 结论：`TestGameplay` 包装与失败路径无交集，复现 0 次。这是登记的依据；它不等于「与 A 的改动无交集」，见下面第三、四点。
- 读码（`logs/task-34-isolated.txt` part 2）：
  - `Fixture.Sql` 在一次同步调用里新建、打开、使用并释放一个局部连接，不保存，也不交出去。数据库文件按 Fixture 用新的 GUID 命名（`:425`）。`Sql`、`Scalar`、`ItemUids`、`ItemsAndPositions` 四个直连方法在 `a02bce46` → `2bdb1afd` 之间都没改；A 只改了 `WriteAsync` 一对，让它们的事务在作业里运行；
  - 用例体经 `TestGameplay.RunAsync` 包成一个作业，在一个新建的执行器上运行。测试线程在作业结束前只等待，不做别的事（`tests/Support/TestGameplay.cs:116-131`）；嵌套的 `WriteAsync` 在作业内内联（`:110`）。Fixture 的构造、使用与释放都在同一个作业里按程序次序发生；
  - 结论：包装把用例体挪到了执行器的作业上，但没有改变这个 Fixture 任何一条连接的归属与次序。没有一条连接被两个流同时使用，也没有一条连接在自己释放之后还被使用。
- 能释放「刚从池里打开的连接」的原生句柄的，在测试进程内只有进程级的 `SqliteConnection.ClearAllPools()`：
  - xUnit 在同一程序集内并行运行测试类，而按 GUID 命名的文件隔离不了进程级清池；
  - `Application.Tests` 冻结点上有 25 个文件调用 `ClearAllPools`，基准上 22 个。A 新增的 3 个是 `Execution/GameplayExecutorLifetimeTests.cs`、`Execution/TimerSourceHost.cs` 与 `OnlineUsers/SerialDisconnectTests.cs`；
  - 已登记的三个栈根指向的是同一种机制。本仓库以往的一份记录也写过这个假设，并注明「无法证明竞争交错」（`.rks-agent/tasks/change-risk-is-measured-repeatably/verification/sqlite-diagnostic.md`）；
  - 这一点是读码推断，没有观测到：日志里没有句柄身份，也没有清池线程；
  - **与 A 的改动有交集，只是不在失败路径上**：机制在 A 之前就有，但 A 新增的 3 个调用点给它多加了触发源，可能提高了这条偶发的出现概率（推断，未观测）。这件事交主控方另行立项（F-130，测试基础设施）：这 3 处可改为按路径 `ClearPool`，或放进不与其他类并行的同一个 collection。
- 复现尝试，0 次复现（`logs/task-34-isolated.txt` part 3）：
  - `Application.Tests` 整个项目跑 3 次：每次 4594 ＝ 4576 ＋ 2 ＋ 16，失败的只有两条基线。**有判别力的是这 3 次**：只有整项目运行时，调用 `ClearAllPools` 的那些类才会与这个类并行；
  - 该类单独连续跑 30 次，同时开 8 个限时 `yes` 负载：30 次都是 66／66 通过，`SQLitePCL.sqlite3` 与栈根都没有出现。这 30 次在构造上不具判别力：这个类的 4 个 partial 文件都不调用 `ClearAllPools`，按上面的推断，单独跑它本来就不会复现；
  - 结束后 `pgrep -x yes` 为 0。
- 按处置第 2 条，栈根已追加进 `AGENTS.md`「判据偶发失败」一节的栈根表（现为四条）。这是本单元唯一一处在过渡态段落之外改动 `AGENTS.md`，授权出处是主控方处置第 2 条。

## 3. 测试数量加法（AC-020、RQ-083、R3-3「A 从基线加到 A 封存」）

### 3.1 逐段

口径：按实际运行的例数计，Theory 按行计。N 是新增例数，D 是删除例数。改名不计入 N、D（TASK-31 裁定第 4 条 (b)）。每段的终点取该单元正式全量日志中 8 个项目总数之和（脚本 scratchpad `t34/counts.py`，`logs/task-34-test-counts.txt`）；没有自己正式全量的段写「未实测」，并注明出处。

| 区间 | 起点 | N | D | 终点 | 加法成立 | 日志中的逐项目总数 | 出处 |
|---|---:|---:|---:|---:|---|---|---|
| TASK-01 | 7872 | 10 | 0 | 7882 | 是 | 未实测（见出处） | `tasks/TASK-01.md:212`：实测 7881 = 7872 + 9（第 2 版代码，b2 未拆）；之后只把 b2 拆成两条、加 1 条跳过用例，按裁定未重跑，推算 7882；TASK-04 以 7882 为起点（`tasks/TASK-04.md:15`） |
| TASK-02 | 7882 | 0 | 0 | 7882 | 是 | 未实测（见出处） | 只写 Spec 附录与验证记录，不改测试 |
| TASK-03 | 7882 | 0 | 0 | 7882 | 是 | 未实测（见出处） | 测量工具与基线测量（Tool/），不改测试 |
| TASK-04 | 7882 | 59 | 0 | 7941 | 是 | `logs/task-04-build-test.txt` 第 1～8 个总数行：1694 + 50 + 80 + 475 + 969 + 107 + 4401 + 165 | 只有总数差 +59（`tasks/TASK-04.md:199`：7941 = 7882 + 59），处置记录未分列 D |
| TASK-05 | 7941 | 45 | 0 | 7986 | 是 | `logs/task-05-build-test.txt` 第 1～8 个总数行：50 + 1694 + 80 + 475 + 969 + 138 + 165 + 4415 | 只有总数差 +45（`tasks/TASK-05.md:286`：Networking 107 + 31、Application 4401 + 14），未分列 D |
| TASK-06 | 7986 | 13 | 0 | 7999 | 是 | `logs/task-06-build-test.txt` 第 1～8 个总数行：50 + 80 + 1694 + 475 + 138 + 969 + 4428 + 165 | `tasks/TASK-06.md:397`：N = 13（TestGameplayTests），迁移部分 0 变化 |
| TASK-07 | 7999 | 0 | 0 | 7999 | 是 | `logs/task-07-build-test.txt` 第 1～8 个总数行：50 + 1694 + 80 + 475 + 969 + 138 + 4428 + 165 | `tasks/TASK-07.md:276`：不新增也不删除 |
| TASK-08 | 7999 | 14 | 0 | 8013 | 是 | `logs/task-08-build-test.txt` 第 1～8 个总数行：50 + 80 + 1694 + 475 + 969 + 149 + 4431 + 165 | `tasks/TASK-08.md:371`：7999 + 14 - 0 |
| TASK-09 | 8013 | 14 | 1 | 8026 | 是 | `logs/task-09-build-test.txt` 第 1～8 个总数行：50 + 80 + 1694 + 475 + 969 + 155 + 4438 + 165 | `tasks/TASK-09.md:385`：8013 + 14 - 1 |
| TASK-10 | 8026 | 34 | 0 | 8060 | 是 | `logs/task-10-build-test.txt` 第 1～8 个总数行：50 + 80 + 1694 + 475 + 969 + 173 + 4453 + 166 | `tasks/TASK-10.md:538`：8026 + 34 - 0 |
| TASK-11 | 8060 | 41 | 0 | 8101 | 是 | `logs/task-11-build-test.txt` 第 1～8 个总数行：50 + 80 + 1701 + 475 + 969 + 173 + 4487 + 166 | `tasks/TASK-11.md:691`：8060 + 41 - 0 |
| TASK-12 | 8101 | 9 | 0 | 8110 | 是 | `logs/task-12-build-test.txt` 第 1～8 个总数行：50 + 80 + 1701 + 475 + 969 + 173 + 4496 + 166 | `tasks/TASK-12.md:489`：8101 + 9 - 0 |
| TASK-12 评审后 | 8110 | 1 | 0 | 8111 | 是 | 未实测（见出处） | `tasks/TASK-12.md:591`：Application 第 2 轮 +1，N = 9 + 1 = 10；正式全量未重跑 |
| TASK-13 | 8111 | 0 | 0 | 8111 | 是 | 未实测（见出处） | 只做隔离运行，测试数不变（`tasks/TASK-14.md:854`「TASK-13 未改变计数」） |
| TASK-14 | 8111 | 19 | 0 | 8130 | 是 | `logs/task-14-build-test.txt` 第 1～8 个总数行：53 + 80 + 1701 + 476 + 969 + 173 + 4502 + 176 | `tasks/TASK-14.md:854`：8111 + 19 - 0 |
| TASK-15 | 8130 | 7 | 0 | 8137 | 是 | `logs/task-15-build-test.txt` 第 1～8 个总数行：53 + 80 + 1701 + 476 + 969 + 173 + 4502 + 183 | `tasks/TASK-15.md:784`、`:859`：N = 7（Host 176 → 183） |
| TASK-16 | 8137 | 10 | 0 | 8147 | 是 | `logs/task-16-build-test.txt` 第 1～8 个总数行：53 + 80 + 1703 + 476 + 969 + 173 + 4510 + 183 | `tasks/TASK-16.md:431`：8137 + 10 - 0 |
| TASK-17 | 8147 | 59 | 44 | 8162 | 是 | `logs/task-17-build-test.txt` 第 1～8 个总数行：53 + 80 + 1703 + 476 + 969 + 173 + 4525 + 183 | `tasks/TASK-17.md:382`：8147 + 59 - 44（处置见 task-07-conflicts.md §14） |
| TASK-18 | 8162 | 52 | 1 | 8213 | 是 | `logs/task-18-build-test.txt` 第 1～8 个总数行：54 + 80 + 1704 + 477 + 970 + 174 + 4563 + 191 | `tasks/TASK-18.md:508`：8162 + 52 - 1 |
| TASK-19 | 8213 | 11 | 0 | 8224 | 是 | `logs/task-19-build-test.txt` 第 1～8 个总数行：54 + 80 + 1704 + 477 + 970 + 175 + 4573 + 191 | `tasks/TASK-19.md:496`：8213 + 11 - 0 |
| TASK-20 | 8224 | 0 | 0 | 8224 | 是 | 未实测（见出处） | 测量闸 M-1，不改测试 |
| TASK-21 | 8224 | 15 | 11 | 8228 | 是 | `logs/task-21-build-test.txt` 第 2～9 个总数行：54 + 80 + 1699 + 477 + 970 + 175 + 4582 + 191 | `tasks/TASK-21.md:522`：8224 + 15 - 11（t005-test-dispositions.md TASK-21） |
| TASK-22 | 8228 | 9 | 45 | 8192 | 是 | `logs/task-22-build-test.txt` 第 7～14 个总数行：54 + 80 + 1699 + 477 + 970 + 175 + 4546 + 191 | `tasks/TASK-22.md:631`：8228 + 9 - 45（t005-test-dispositions.md TASK-22） |
| TASK-22 评审后 | 8192 | 2 | 0 | 8194 | 是 | 未实测（见出处） | `tasks/TASK-22.md:694`：Application +2（4546 → 4548），正式全量未重跑 |
| TASK-23 | 8194 | 40 | 43 | 8191 | 是 | `logs/task-23-build-test.txt` 第 45～52 个总数行：54 + 80 + 1709 + 477 + 970 + 178 + 4532 + 191 | `tasks/TASK-23.md:861`：8194 + 40 - 43（t006-test-dispositions.md TASK-23 §1） |
| TASK-24 | 8191 | 45 | 7 | 8229 | 是 | `logs/task-24-build-test.txt` 第 9～16 个总数行：54 + 80 + 1744 + 477 + 970 + 179 + 4535 + 190 | `tasks/TASK-24.md:524`：8191 + 45 - 7 |
| TASK-24 评审后 | 8229 | 2 | 0 | 8231 | 是 | 未实测（见出处） | `tasks/TASK-24.md:585`：Protocol +2，N 45 + 2 = 47；正式全量未重跑 |
| TASK-25 | 8231 | 2 | 1 | 8232 | 是 | `logs/task-25-build-test.txt` 第 1～8 个总数行：54 + 80 + 1746 + 477 + 970 + 179 + 4536 + 190 | `tasks/TASK-25.md:488`：8231 + 2 - 1 |
| TASK-26 | 8232 | 6 | 0 | 8238 | 是 | `logs/task-26-build-test.txt` 第 1～8 个总数行：54 + 80 + 1746 + 477 + 970 + 179 + 4538 + 194 | `tasks/TASK-26.md:378`：8232 + 6 - 0 |
| TASK-26 评审后 | 8238 | 1 | 0 | 8239 | 是 | 未实测（见出处） | `t006-closure.md:144`：Host +1（HostOutputWiringSourceTests），正式全量未重跑 |
| TASK-27 | 8239 | 8 | 0 | 8247 | 是 | `logs/task-27-build-test.txt` 第 1～8 个总数行：54 + 80 + 1752 + 477 + 970 + 181 + 4538 + 195 | `tasks/TASK-27.md:376`：8239 + 8 - 0 |
| TASK-28 | 8247 | 0 | 0 | 8247 | 是 | 未实测（见出处） | 测量闸 M-2，不改测试 |
| TASK-29 | 8247 | 15 | 0 | 8262 | 是 | `logs/task-29-build-test.txt` 第 1～8 个总数行：54 + 80 + 1752 + 477 + 970 + 181 + 4553 + 195 | `tasks/TASK-29.md:385`：8247 + 15 - 0（t007-closure.md §5.1） |
| TASK-30 | 8262 | 39 | 0 | 8301 | 是 | `logs/task-30-build-test.txt` 第 1～8 个总数行：54 + 80 + 1752 + 477 + 970 + 183 + 4589 + 196 | `tasks/TASK-30.md:438`：8262 + 39 - 0 |
| TASK-30 评审后 | 8301 | 1 | 0 | 8302 | 是 | 未实测（见出处） | `tasks/TASK-30.md:488`：Application +1，N = 40（t009-criteria.md §8）；正式全量未重跑 |
| TASK-31 | 8302 | 13 | 0 | 8315 | 是 | `logs/task-31-build-test.txt` 第 1～8 个总数行：57 + 80 + 1752 + 477 + 970 + 188 + 4594 + 197 | `tasks/TASK-31.md:503`：8302 + 13 - 0；R = 26 不计入（t009-logs-and-cases.md §7.4） |
| TASK-32 | 8315 | 0 | 0 | 8315 | 是 | 未实测（见出处） | 测量闸 M-3，不改测试 |
| TASK-33 | 8315 | 0 | 0 | 8315 | 是 | 未实测（见出处） | 文档与附录对账，不跑测试套件 |
| TASK-34 | 8315 | 0 | 0 | 8315 | 是 | `logs/task-34-build-test-raw.txt` 第 1～8 个总数行：57 + 80 + 1752 + 477 + 970 + 188 + 4594 + 197 | 本单元正式全量，不改测试 |

### 3.2 合计

- ΣN ＝ 10 ＋ 59 ＋ 45 ＋ 13 ＋ 14 ＋ 14 ＋ 34 ＋ 41 ＋ 9 ＋ 1 ＋ 19 ＋ 7 ＋ 10 ＋ 59 ＋ 52 ＋ 11 ＋ 15 ＋ 9 ＋ 2 ＋ 40 ＋ 45 ＋ 2 ＋ 2 ＋ 6 ＋ 1 ＋ 8 ＋ 15 ＋ 39 ＋ 1 ＋ 13 ＝ 596（30 项）。
- ΣD ＝ 1 ＋ 44 ＋ 1 ＋ 11 ＋ 45 ＋ 43 ＋ 7 ＋ 1 ＝ 153（8 项）。
- 7872 ＋ 596 － 153 ＝ 8315，等于本单元正式全量的总数。
- ΣR：只有 TASK-31 单列了改名（R ＝ 26 个方法、27 例，不计入 N、D）；更早的单元没有单列 R。
- 出处的缺口：TASK-04（＋59）与 TASK-05（＋45）只有总数差，处置记录没有分列 D，这里按各自 Result 的「7882 ＋ 59」「7941 ＋ 45」写作 N、D 0。TASK-01 的终点 7882 是它 Result 里的推算值（实测 7881 是在拆分 b2 之前），只能由 TASK-04 以它为起点间接印证。
- 逐项目，基线 → A 封存：
  - HttpApi 50 → 57（＋7）、Pvf 80 → 80（＋0）、Protocol 1694 → 1752（＋58）、Persistence 475 → 477（＋2）、Infrastructure 969 → 970（＋1）、Networking 97 → 188（＋91）、Application 4342 → 4594（＋252）、Host 165 → 197（＋32）；
  - 7 ＋ 0 ＋ 58 ＋ 2 ＋ 1 ＋ 91 ＋ 252 ＋ 32 ＝ 443 ＝ 8315 － 7872 ＝ 596 － 153。

### 3.3 AC-009①：193 个方法、332 行（订正 TASK-33 的「331 行」）

- `t34/protection.py` 就是 `t33/protection.py`，只把「通过」的来源换成本次原始输出。它选出 195 个方法：冻结点上同名 190 个、改名 3 个、删除 2 个；断言行多重集合相同 181 个、不同 12 个、删除 2 个。这些数都与 TASK-33 相同（`logs/task-34-protection-tests.txt`）。
- 193 个方法全部通过。脚本报 192 个通过、1 个找不到。找不到的那条是 `ItemPathFrameTests.AMoveOfARealRowAcknowledgesTheSameBytesAsBefore`：它的「已通过」与一个日志前缀挤在同一行（原始输出第 3932 行），脚本按行首匹配，所以看不到。
- 行数是 332。两次运行脚本都读出 331，但两次各有恰好一行被挤掉：
  - 本次挤掉的是上面那条（1 行）；
  - TASK-31 挤掉的是 `ReplayStateProjectionTests.Tower_replay_uses_current_memory_slots_and_late_replay_preserves_restored_slots(inTower: True)`（scratchpad `t31/formal-raw.txt:1700`）；
  - 所以两次实际通过的行数都是 331 ＋ 1 ＝ 332。
- 订正：TASK-33 记的「193 个方法、331 行」（`logs/task-33-protection-tests.txt`、`t009-docs-and-appendices.md` §10）少算了这一行挤掉的结果，应为 332 行。已提交的 TASK-33 文件不改（主控方处置第 3 条）。

## 4. 测试文件改动清单（RQ-081、RQ-083、子契约 SC-8）

### 4.1 总览

- `git diff --name-status a02bce46 2bdb1afd -- Server/tests`：新增 112，删除 7，修改 363（其中 `.cs` 356、`.csproj` 7）。全文在 `logs/task-34-test-files.txt`。
- 脚本 scratchpad `t34/diffcheck34.py`：就是 `t07/diffcheck7.py`，只加了一处「跳过基准之后删除的文件」，否则原脚本打不开这 7 个文件。它在冻结点工作树上比对测试方法多重集合、断言行多重集合与白名单之外的残余片段。分类脚本 `t34/testfiles.py`。
- 356 个修改的 `.cs` 文件：
  - a1：方法、断言都相同，残余只有白名单形状，218 个（名单在日志里）；
  - a2：方法、断言都相同，残余超出白名单，即准备数据、驱动方式或测试接缝的改动，67 个（§4.3）；
  - b：方法多重集合有变化（增、删或改名），41 个；
  - c：方法相同、断言多重集合有变化，30 个；
  - 218 ＋ 67 ＋ 41 ＋ 30 ＝ 356。
- b 与 c 的 71 个文件，每个都有处置记录点名它的类，指不到的为 0（§4.2）。指向的粒度是文件：表中给出点名该类的第一行，逐条断言的变化在那份记录里。以被替换的发送或分发语义为测试对象的文件，例如 `PreparedBatchTests`、`NotificationDeliveryTests`、`CleanupNotificationDeliveryTests`，替代它们的用例写在所指记录的同一行里（SC-8 末句）。
- 删除的 7 个文件：

| 文件 | 删除单元 | 处置 |
|---|---|---|
| `Application.Tests/Commands/OnlineUserExecutionGateTests.cs` | TASK-22 | `t005-test-dispositions.md` TASK-22 §2 D3（规格消除，门删除） |
| `Application.Tests/Commands/SelectedSessionTransitionGateTests.cs` | TASK-22 | 同上 D4 |
| `Application.Tests/Commands/MultiAccountExecutionTests.cs` | TASK-22 | 同上 D5 |
| `Application.Tests/Notifications/NotificationRetryTests.cs` | TASK-23 | `t006-test-dispositions.md` TASK-23 §2 R1（DR-13、DR-14，不再重试；VC-5） |
| `Application.Tests/Notifications/NotificationRetryDrainServiceTests.cs` | TASK-23 | 同上 R2（排空服务随附录 F 删除） |
| `Networking.Tests/Runtime/ExactConnectionCloseTests.cs` | TASK-23 | 同上 R11（被测的直接关连接器删除；承接用例在同一行） |
| `Networking.Tests/Integration/TouchSocketNetworkMessageSenderExplicitDeliveryTests.cs` | TASK-23 | 同上 §2「—」一行与 §3 S6：文件改名为 `OutboxExplicitDeliveryTests.cs`，3 条计入 D 3 ＋ N 3；方法名在 TASK-31 改（`t009-logs-and-cases.md` §7.3 第 20～22 行） |

### 4.2 断言或方法有变化的文件（b 41、c 30）

| 类 | 文件（相对 `Server/tests/`） | 方法 基准→冻结点 | 断言行 基准→冻结点（只在基准／只在冻结点） | 处置记录（点名该类的第一行；括号内为同一文件中的其余行数） |
|---|---|---|---|---|
| b | `Dfo.Server.Application.Tests/Commands/CommandBusTests.cs` | 13→8 | 23→15（−8／＋0） | `t005-test-dispositions.md:136(+2)`；`task-07-conflicts.md:98(+5)`；`t009-criteria.md:63(+3)` |
| b | `Dfo.Server.Application.Tests/Commands/TransactionalCommandSerializationTests.cs` | 3→2 | 5→4（−1／＋0） | `t005-test-dispositions.md:205`；`task-07-conflicts.md:93(+5)` |
| b | `Dfo.Server.Application.Tests/DomainEvents/DomainEventDispatchTests.cs` | 17→19 | 72→85（−1／＋14） | `t005-test-dispositions.md:132(+5)`；`t006-test-dispositions.md:415`；`task-07-conflicts.md:120(+2)` |
| b | `Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` | 26→24 | 128→124（−20／＋16） | `t005-test-dispositions.md:51(+4)`；`t006-test-dispositions.md:161(+7)`；`task-07-conflicts.md:27(+7)` |
| b | `Dfo.Server.Application.Tests/Inventory/EquipmentEnchantHandlerTests.cs` | 12→12 | 49→51（−5／＋7） | `t006-test-dispositions.md:325(+6)`；`t009-logs-and-cases.md:160` |
| b | `Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs` | 16→16 | 43→64（−15／＋36） | `t006-test-dispositions.md:28(+10)`；`task-07-conflicts.md:138(+1)`；`t009-criteria.md:99` |
| b | `Dfo.Server.Application.Tests/Notifications/NotificationHandlerArchitectureTests.cs` | 9→8 | 13→10（−3／＋0） | `t006-test-dispositions.md:32`；`t009-criteria.md:70(+1)` |
| b | `Dfo.Server.Application.Tests/Notifications/NotificationRegistrationTests.cs` | 7→7 | 16→15（−1／＋0） | `t006-test-dispositions.md:32(+1)`；`t009-criteria.md:69`；`t009-logs-and-cases.md:157` |
| b | `Dfo.Server.Application.Tests/OnlineUsers/OnlineUserServiceTests.cs` | 21→18 | 90→76（−14／＋0） | `t005-test-dispositions.md:50(+3)`；`t006-test-dispositions.md:218(+1)`；`task-07-conflicts.md:31(+3)` |
| b | `Dfo.Server.Application.Tests/Scheduling/ScheduledTaskTests.cs` | 12→12 | 31→32（−5／＋6） | `t005-test-dispositions.md:138(+4)`；`task-07-conflicts.md:240(+1)`；`t009-criteria.md:71(+1)` |
| b | `Dfo.Server.Application.Tests/Sessions/CollectionBoxNetworkSettlementTests.cs` | 7→6 | 34→31（−3／＋0） | `t006-test-dispositions.md:331` |
| b | `Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs` | 20→19 | 78→73（−6／＋1） | `t005-test-dispositions.md:174`；`t006-test-dispositions.md:88(+3)`；`task-07-conflicts.md:35(+8)` |
| b | `Dfo.Server.Application.Tests/Sessions/ReplayStateSnapshotTests.cs` | 11→11 | 59→52（−9／＋2） | `t005-test-dispositions.md:42(+3)`；`task-07-conflicts.md:34(+14)`；`t009-logs-and-cases.md:150(+1)` |
| b | `Dfo.Server.Application.Tests/Transactions/UnitOfWorkBehaviorTests.cs` | 8→8 | 25→25（−0／＋0） | `t005-test-dispositions.md:149`；`t009-criteria.md:66`；`t009-logs-and-cases.md:135` |
| b | `Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | 14→14 | 75→74（−3／＋2） | `t005-test-dispositions.md:107(+2)`；`t006-test-dispositions.md:147(+2)`；`task-07-conflicts.md:113(+6)` |
| b | `Dfo.Server.Application.Tests/World/DungeonObjectQueueTests.cs` | 3→2 | 22→19（−3／＋0） | `task-07-conflicts.md:118(+2)`；`task-07-diff-check.md:124` |
| b | `Dfo.Server.Application.Tests/World/IllusionTowerPublicFlowTests.cs` | 2→2 | 53→51（−8／＋6） | `t006-test-dispositions.md:292`；`task-07-conflicts.md:42(+2)`；`t009-criteria.md:175` |
| b | `Dfo.Server.Application.Tests/World/IllusionTowerRewardProjectionTests.cs` | 3→3 | 34→34（−0／＋0） | `t006-test-dispositions.md:211(+3)`；`t009-logs-and-cases.md:159` |
| b | `Dfo.Server.HttpApi.Tests/HttpApiPipelineTests.cs` | 13→14 | 52→56（−0／＋4） | `tasks/TASK-14.md:80(+6)` |
| b（线级） | `Dfo.Server.Networking.Tests/Integration/HandlerFailureConnectionIntegrationTests.cs` | 2→3 | 4→15（−0／＋11） | `t006-test-dispositions.md:86`；`task-07-conflicts.md:130`；`task-06-conflicts.md:89` |
| b | `Dfo.Server.Persistence.Tests/Accounts/AuthenticationConsumptionTests.cs` | 2→2 | 13→12（−1／＋0） | `task-07-conflicts.md:56(+3)`；`task-06-conflicts.md:18`；`task-06-diff-check.md:105` |
| b | `Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerProjectionConcurrencyTests.cs` | 11→16 | 77→101（−0／＋24） | `t005-test-dispositions.md:66(+5)`；`t006-test-dispositions.md:103(+3)`；`task-07-conflicts.md:60(+2)` |
| b | `Dfo.Server.Protocol.Tests/Game/CardReward/DungeonCardPresentationCoordinatorTests.cs` | 10→10 | 43→44（−1／＋2） | `t006-test-dispositions.md:293`；`task-07-conflicts.md:20(+6)`；`task-06-conflicts.md:42(+1)` |
| b | `Dfo.Server.Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs` | 6→8 | 20→27（−10／＋17） | `t006-test-dispositions.md:51(+2)`；`task-07-conflicts.md:130(+2)`；`task-06-conflicts.md:86` |
| b | `Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 6→6 | 13→11（−2／＋0） | `t005-test-dispositions.md:150`；`t009-criteria.md:67`；`t009-logs-and-cases.md:148` |
| b | `Dfo.Server.Protocol.Tests/Game/Creatures/CreatureProjectionDeliveryTests.cs` | 6→7 | 14→19（−2／＋7） | `t006-test-dispositions.md:154(+4)`；`t006-closure.md:63` |
| b | `Dfo.Server.Protocol.Tests/Game/DungeonClear/LeaveDungeonTests.cs` | 25→22 | 80→70（−10／＋0） | `t005-test-dispositions.md:41(+7)`；`t006-test-dispositions.md:219(+1)`；`task-07-conflicts.md:63(+3)` |
| b | `Dfo.Server.Protocol.Tests/Game/DungeonClear/SetPlayResultHandlerTests.cs` | 26→27 | 86→92（−0／＋6） | `t006-test-dispositions.md:179(+1)`；`t006-closure.md:58` |
| b | `Dfo.Server.Protocol.Tests/Game/DungeonEntry/PartySelectDungeonHandlerTests.cs` | 18→20 | 154→160（−16／＋22） | `t005-test-dispositions.md:64(+1)`；`t006-test-dispositions.md:78(+6)`；`task-07-conflicts.md:65(+1)` |
| b | `Dfo.Server.Protocol.Tests/Game/DungeonEntry/SelectDungeonHandlerTests.cs` | 22→21 | 83→79（−4／＋0） | `t005-test-dispositions.md:63`；`t006-test-dispositions.md:385`；`task-07-conflicts.md:64(+1)` |
| b | `Dfo.Server.Protocol.Tests/Game/Dungeons/DungeonLoadingProjectionDeliveryTests.cs` | 7→5 | 32→26（−7／＋1） | `t005-test-dispositions.md:61`；`t006-test-dispositions.md:381`；`task-07-conflicts.md:59(+1)` |
| b | `Dfo.Server.Protocol.Tests/Game/IllusionTower/IllusionTowerProjectionConcurrencyTests.cs` | 11→16 | 78→108（−0／＋30） | `t005-test-dispositions.md:67(+1)`；`t006-test-dispositions.md:103(+3)`；`task-07-conflicts.md:61(+2)` |
| b | `Dfo.Server.Protocol.Tests/Game/MonsterDeath/MonsterDeathHandlerTests.cs` | 25→26 | 125→134（−0／＋9） | `t005-test-dispositions.md:199`；`t006-test-dispositions.md:176(+5)`；`t006-closure.md:58` |
| b | `Dfo.Server.Protocol.Tests/Game/Party/PartyCommandCodecTests.cs` | 18→20 | 76→79（−2／＋5） | `t005-test-dispositions.md:151`；`t009-criteria.md:68`；`t006-closure.md:231` |
| b | `Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs` | 20→16 | 55→51（−4／＋0） | `t005-test-dispositions.md:68(+3)`；`task-07-conflicts.md:62(+1)`；`task-06-conflicts.md:54(+4)` |
| b | `Dfo.Server.Protocol.Tests/Game/Party/PartyNotificationTests.cs` | 33→39 | 131→157（−3／＋29） | `t006-test-dispositions.md:78(+5)`；`task-07-conflicts.md:69(+1)`；`task-06-conflicts.md:70` |
| b | `Dfo.Server.Protocol.Tests/Game/Party/PartyTownProjectionDeliveryTests.cs` | 7→5 | 33→25（−8／＋0） | `t005-test-dispositions.md:207`；`t006-test-dispositions.md:144(+2)`；`task-07-conflicts.md:57(+1)` |
| b | `Dfo.Server.Protocol.Tests/Game/TownProjection/TownAreaProjectionDeliveryTests.cs` | 8→9 | 41→43（−0／＋2） | `t005-test-dispositions.md:56`；`t006-test-dispositions.md:103`；`task-06-conflicts.md:79` |
| b | `Dfo.Server.Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs` | 7→6 | 72→69（−3／＋0） | `t005-test-dispositions.md:57`；`task-07-conflicts.md:58(+1)`；`task-06-conflicts.md:37` |
| b | `Dfo.Server.Protocol.Tests/Game/UsageQuotaProjectionTests.cs` | 12→14 | 65→71（−0／＋6） | `t005-test-dispositions.md:55`；`t006-test-dispositions.md:184(+2)`；`task-06-conflicts.md:102(+2)` |
| b | `Dfo.Server.Protocol.Tests/MarriageProposalCodecTests.cs` | 15→14 | 51→49（−2／＋0） | `t005-test-dispositions.md:33`；`task-07-conflicts.md:68(+1)`；`task-06-conflicts.md:69(+1)` |
| c | `Dfo.Server.Application.Tests/Authentication/LoginSessionCommitRaceTests.cs` | 2→2 | 8→8（−1／＋1） | `t006-test-dispositions.md:58`；`task-07-conflicts.md:162`；`t009-docs-and-appendices.md:135` |
| c | `Dfo.Server.Application.Tests/Events/JoustIntegrationTests.cs` | 47→47 | 320→319（−1／＋0） | `t006-test-dispositions.md:322`；`task-07-conflicts.md:121(+1)`；`task-07-diff-check.md:82` |
| c | `Dfo.Server.Application.Tests/Inventory/CeraShopPurchaseTests.cs` | 30→30 | 194→195（−0／＋1） | `t006-test-dispositions.md:223(+4)` |
| c | `Dfo.Server.Application.Tests/Inventory/EquipmentAmplificationTests.cs` | 8→8 | 40→41（−1／＋2） | `t006-test-dispositions.md:341` |
| c | `Dfo.Server.Application.Tests/Inventory/EquipmentForgingHandlerTests.cs` | 4→4 | 31→32（−1／＋2） | `t006-test-dispositions.md:352` |
| c | `Dfo.Server.Application.Tests/Inventory/EquipmentOutworldHandlerTests.cs` | 9→9 | 72→73（−1／＋2） | `t006-test-dispositions.md:342` |
| c | `Dfo.Server.Application.Tests/Inventory/EquipmentReinforcementHandlerTests.cs` | 16→16 | 150→152（−4／＋6） | `t006-test-dispositions.md:356(+4)` |
| c | `Dfo.Server.Application.Tests/Inventory/EquippedUpgradeTicketTests.cs` | 5→5 | 43→44（−1／＋2） | `t006-test-dispositions.md:355` |
| c | `Dfo.Server.Application.Tests/Inventory/HerasEntryCouponTests.cs` | 8→8 | 40→41（−1／＋2） | `t006-test-dispositions.md:353(+1)` |
| c | `Dfo.Server.Application.Tests/Inventory/ItemSaleTests.cs` | 21→21 | 75→76（−0／＋1） | `t006-test-dispositions.md:225(+5)` |
| c | `Dfo.Server.Application.Tests/Inventory/LotteryOpeningTests.cs` | 19→19 | 97→101（−4／＋8） | `t006-test-dispositions.md:343(+3)` |
| c | `Dfo.Server.Application.Tests/Inventory/MagicSealHandlerTests.cs` | 8→8 | 37→37（−1／＋1） | `t006-test-dispositions.md:354` |
| c | `Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs` | 15→15 | 87→86（−2／＋1） | `t006-test-dispositions.md:224(+6)` |
| c | `Dfo.Server.Application.Tests/Inventory/OnlineExpiredItemCleanupTests.cs` | 7→7 | 35→34（−1／＋0） | `t005-test-dispositions.md:173`；`task-07-conflicts.md:112(+2)`；`t009-docs-and-appendices.md:138` |
| c | `Dfo.Server.Application.Tests/Inventory/VendingMachineTests.cs` | 10→10 | 42→44（−2／＋4） | `t006-test-dispositions.md:347(+1)` |
| c | `Dfo.Server.Application.Tests/Notifications/NotificationWorld.cs` | 0→0 | 2→2（−1／＋1） | `t006-test-dispositions.md:48`；`task-07-conflicts.md:142(+1)` |
| c（线级） | `Dfo.Server.Host.Tests/EndToEndHostHarness.cs` | 0→0 | 48→49（−0／＋1） | `t006-closure.md:218` |
| c | `Dfo.Server.Host.Tests/GmCeraHttpTests.cs` | 13→13 | 80→85（−3／＋8） | `t006-test-dispositions.md:156(+2)`；`task-07-conflicts.md:293`；`t006-closure.md:61` |
| c | `Dfo.Server.Host.Tests/GmInitializationHttpTests.cs` | 5→5 | 48→48（−3／＋3） | `tasks/TASK-14.md:532(+4)`；`tasks/TASK-16.md:447` |
| c（线级） | `Dfo.Server.Networking.Tests/Runtime/PreparedBatchTests.cs` | 5→5 | 13→11（−12／＋10） | `t006-test-dispositions.md:37(+1)`；`task-07-conflicts.md:130`；`task-06-conflicts.md:87` |
| c | `Dfo.Server.Persistence.Tests/Accounts/AuthenticationFailureTests.cs` | 3→3 | 15→15（−1／＋1） | `tasks/TASK-06.md:187`；`tasks/TASK-14.md:529(+5)` |
| c | `Dfo.Server.Persistence.Tests/Accounts/AuthenticationMigrationTests.cs` | 3→3 | 21→21（−1／＋1） | `tasks/TASK-06.md:189(+1)`；`tasks/TASK-14.md:529(+4)` |
| c | `Dfo.Server.Persistence.Tests/Accounts/AuthenticationRegistrationTests.cs` | 1→1 | 10→10（−1／＋1） | `task-06-conflicts.md:22` |
| c | `Dfo.Server.Persistence.Tests/Accounts/LoginSessionQueuedCommandTests.cs` | 1→1 | 4→3（−1／＋0） | `t005-test-dispositions.md:173(+1)`；`task-07-conflicts.md:55(+3)`；`task-06-conflicts.md:17` |
| c | `Dfo.Server.Persistence.Tests/Accounts/LoginSessionReconnectTests.cs` | 5→5 | 17→16（−2／＋1） | `t006-test-dispositions.md:59`；`t009-docs-and-appendices.md:136` |
| c | `Dfo.Server.Protocol.Tests/Game/CharacterDeath/CharacterDeathHandlerTests.cs` | 1→1 | 10→10（−1／＋1） | `t005-test-dispositions.md:62`；`t006-test-dispositions.md:380` |
| c | `Dfo.Server.Protocol.Tests/Game/ItemUsageProjectionTests.cs` | 3→3 | 19→19（−1／＋1） | `t005-test-dispositions.md:54`；`t006-test-dispositions.md:381`；`task-06-conflicts.md:101` |
| c | `Dfo.Server.Protocol.Tests/Game/StateSynchronization/TitleBookMutationHandlerTests.cs` | 1→1 | 10→9（−1／＋0） | `t005-test-dispositions.md:43(+1)`；`task-07-conflicts.md:67(+1)`；`task-06-conflicts.md:68(+1)` |
| c | `Dfo.Server.Protocol.Tests/Game/StateSynchronization/TitleBookProtocolTests.cs` | 9→9 | 49→46（−3／＋0） | `t005-test-dispositions.md:60`；`task-07-conflicts.md:66(+1)`；`task-06-conflicts.md:65(+3)` |
| c | `Dfo.Server.Protocol.Tests/Game/Weakness/WeaknessProjectionTests.cs` | 2→2 | 6→6（−1／＋1） | `t006-test-dispositions.md:381` |

### 4.3 断言未改、准备数据或驱动有改动的文件（a2，67 个；SC-8「逐个列出并说明断言未改」）

这些文件的测试方法多重集合与断言行多重集合两侧相同，改动只在准备数据、驱动方式或测试接缝上。「改动它的提交」一栏说明了是哪一类改动：
- `c19b3b7b`、`422ec64d`：TASK-06、TASK-07 的测试迁移，经 `TestGameplay` 驱动；
- `a4422480`、`09e4983c`：TASK-21、TASK-22 删门，删掉的是门的实参或声明；
- `50d7bce4`、`1ee1c91e`、`a861c4de`：TASK-23～TASK-25，投递改为经发件箱观察；
- `122576d5`：TASK-30 的包装；
- `0c9c59c8`、`6a3bff70`、`bfbcf012`、`6331b23a`、`f13e612a`、`648fbc60`、`8c531c9b`、`2bdb1afd`：各单元的注册、宿主或日志接缝。

其中 4 个文件没有处置记录点名它的类：`IllusionTowerDomainServiceTests`、`EitherEndMayBeHoldingItTests`、`RefusedEquipIsRecordedWithItsTemplateTests`、`TheCharacterChangesAsYouDressItTests`。它们的断言两侧相同，只是被上面的迁移、包装或替身提交改过。

| 文件（相对 `Server/tests/`） | 白名单外片段 | 改动它的提交 |
|---|---:|---|
| `Dfo.Server.Application.Tests/Authentication/GameLoginCommandTransactionTests.cs` | 10 | `422ec64d`、`bfbcf012` |
| `Dfo.Server.Application.Tests/Channel/ChannelConnectHandlerTests.cs` | 11 | `a861c4de` |
| `Dfo.Server.Application.Tests/Channel/ChannelControlMessageHandlerTests.cs` | 11 | `a861c4de` |
| `Dfo.Server.Application.Tests/Characters/SkillLearningCommandTests.cs` | 21 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` | 2 | `8c531c9b` |
| `Dfo.Server.Application.Tests/Dispatching/NetworkMessageDispatcherTests.cs` | 10 | `422ec64d`、`6a3bff70` |
| `Dfo.Server.Application.Tests/Game/ExitFlushHandlerTests.cs` | 13 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Application.Tests/Game/GameExitMessageHandlerTests.cs` | 13 | `a861c4de` |
| `Dfo.Server.Application.Tests/Game/GameLoginTests.cs` | 58 | `422ec64d`、`a4422480`、`a861c4de` |
| `Dfo.Server.Application.Tests/Game/MoveItemSpaceHandlerTests.cs` | 27 | `422ec64d`、`6a3bff70`、`a4422480`、`a861c4de` |
| `Dfo.Server.Application.Tests/Game/SelectCharacterReturnTests.cs` | 1 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs` | 14 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Application.Tests/Inventory/EffectRequestTransport.cs` | 1 | `a861c4de` |
| `Dfo.Server.Application.Tests/Inventory/WarehouseMoveSynchronizationTests.cs` | 1 | `422ec64d` |
| `Dfo.Server.Application.Tests/Marriage/MarriageCeremonyHandlerTests.cs` | 16 | `422ec64d`、`a4422480`、`a861c4de` |
| `Dfo.Server.Application.Tests/Marriage/MarriageCeremonyTests.cs` | 1 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/Marriage/MarriageChurchTests.cs` | 1 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/Marriage/MarriageEntityTests.cs` | 13 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Application.Tests/Marriage/MarriageProposalTests.cs` | 1 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/Notifications/NotificationInWorldTests.cs` | 2 | `422ec64d`、`50d7bce4` |
| `Dfo.Server.Application.Tests/Parties/PartySettingsDomainTests.cs` | 2 | `422ec64d` |
| `Dfo.Server.Application.Tests/Sessions/OnlineIdentityTests.cs` | 2 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/Sessions/SessionGenerationOwnershipTests.cs` | 2 | `422ec64d`、`2bdb1afd` |
| `Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.cs` | 1 | `422ec64d` |
| `Dfo.Server.Application.Tests/World/DungeonVisitDomainServiceTests.cs` | 1 | `422ec64d`、`a4422480` |
| `Dfo.Server.Application.Tests/World/IllusionTowerDomainServiceTests.cs` | 31 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Application.Tests/World/TournamentRewardSweepTests.cs` | 1 | `422ec64d`、`648fbc60` |
| `Dfo.Server.Host.Tests/ChannelHostStartupTests.cs`（线级） | 1 | `0c9c59c8` |
| `Dfo.Server.Host.Tests/ComponentLifecycleTests.cs`（线级） | 1 | `6331b23a` |
| `Dfo.Server.Host.Tests/GmEventsHttpTests.cs` | 21 | `a4422480`、`50d7bce4`、`a861c4de` |
| `Dfo.Server.Host.Tests/GmOfficialMailHttpTests.cs` | 2 | `422ec64d`、`a861c4de` |
| `Dfo.Server.Host.Tests/GmPlayerDirectoryHttpTests.cs` | 6 | `c19b3b7b`、`bfbcf012`、`50d7bce4` |
| `Dfo.Server.Host.Tests/GmPlayerSamplingHttpTests.cs` | 12 | `bfbcf012` |
| `Dfo.Server.Host.Tests/GmTestHost.cs` | 1 | `6331b23a` |
| `Dfo.Server.Host.Tests/LauncherHttpHostStartupTests.cs`（线级） | 3 | `f13e612a`、`0c9c59c8` |
| `Dfo.Server.Networking.Tests/Integration/BossTowerFrameIntegrationTests.cs`（线级） | 8 | `c19b3b7b`、`bfbcf012` |
| `Dfo.Server.Networking.Tests/Integration/EplpRechallengeFrameTests.cs`（线级） | 1 | `50d7bce4` |
| `Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs`（线级） | 51 | `422ec64d`、`bfbcf012`、`1ee1c91e` |
| `Dfo.Server.Networking.Tests/Integration/IllusionTowerStageFrameIntegrationTests.cs`（线级） | 10 | `c19b3b7b`、`bfbcf012`、`122576d5` |
| `Dfo.Server.Networking.Tests/Integration/InventorySlotMappingFailureIntegrationTests.cs`（线级） | 1 | `2bdb1afd` |
| `Dfo.Server.Networking.Tests/Integration/OutboundSendFailureTests.cs`（线级） | 1 | `122576d5` |
| `Dfo.Server.Networking.Tests/Integration/UnknownInboundFrameIntegrationTests.cs`（线级） | 5 | `50d7bce4` |
| `Dfo.Server.Persistence.Tests/Accounts/AuthenticationConcurrencyTests.cs` | 13 | `bfbcf012` |
| `Dfo.Server.Persistence.Tests/Accounts/AuthenticationFixture.cs` | 1 | `c19b3b7b`、`bfbcf012` |
| `Dfo.Server.Persistence.Tests/Accounts/AuthenticationTicketTests.cs` | 17 | `c19b3b7b`、`bfbcf012` |
| `Dfo.Server.Protocol.Tests/Game/AccountRoster/OtherCharacterDetailFlowTests.cs` | 3 | `c19b3b7b`、`09e4983c`、`a4422480` |
| `Dfo.Server.Protocol.Tests/Game/BossTower/BossTowerPublicHandlerTests.cs` | 1 | `c19b3b7b`、`09e4983c` |
| `Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs` | 25 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/CardReward/DungeonCardSelectionProjectionDeliveryTests.cs` | 17 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/Creatures/HatchCreatureHandlerTests.cs` | 19 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/DeleteItem/DeleteItemCodecTests.cs` | 13 | `c19b3b7b`、`422ec64d`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/DungeonClear/DungeonClearProjectionDeliveryTests.cs` | 15 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs` | 15 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/DungeonEntry/PartyMoveMapHandlerTests.cs` | 14 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/ExperienceFrameStandIns.cs` | 19 | `a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/FinishLoading/FinishLoadingCodecTests.cs` | 20 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/GroundPickup/GetItemHandlerTests.cs` | 22 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/GroundPickup/QuestClearVerticalTests.cs` | 15 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/Joust/JoustQueryHandlerTests.cs` | 25 | `c19b3b7b`、`a4422480`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/LevelUpTicket/LevelUpTicketCodecTests.cs` | 19 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/MoveItemSpace/EitherEndMayBeHoldingItTests.cs` | 14 | `c19b3b7b`、`a861c4de`、`122576d5` |
| `Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs` | 14 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheCharacterChangesAsYouDressItTests.cs` | 15 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/QuestClearCoupon/QuestClearCouponCodecTests.cs` | 13 | `c19b3b7b`、`a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/ReturnToSelection/ReturnToSelectionTests.cs` | 22 | `a861c4de` |
| `Dfo.Server.Protocol.Tests/Game/TownProjection/SharedTownMovementDomainService.cs` | 1 | `a4422480` |
| `Dfo.Server.Protocol.Tests/SharedEffectProjectionTests.cs` | 17 | `c19b3b7b`、`09e4983c`、`a4422480`、`a861c4de` |

### 4.4 线级测试文件（RQ-081、RQ-082）

范围是 `Dfo.Server.Networking.Tests` 整个项目，加上 `Dfo.Server.Host.Tests` 中用到 `TcpClient`、`Socket`、`EndToEndHostHarness` 或 `ProcessStartInfo` 的文件。

| 文件 | 类 | 断言变化 | 处置或 VC |
|---|---|---|---|
| `Networking.Tests/Integration/HandlerFailureConnectionIntegrationTests.cs` | b | 新增 1 个方法（F-32），原方法的断言不变（−0／＋11） | `t009-logs-and-cases.md` §5、§7.1。线上行为与基准相同：基准上线上断言全过，只有日志断言失败。不是可见变化 |
| `Networking.Tests/Runtime/PreparedBatchTests.cs` | c | 被测的直发发送器删除，断言对象由「传输上发了什么」改为「发件箱登记了什么」（−12／＋10） | `t006-test-dispositions.md` TASK-23 §3 S5。以被替换的发送语义为测试对象，按 SC-8 处理；不是客户端可见变化 |
| `Host.Tests/EndToEndHostHarness.cs` | c | 新增 1 行断言（初始通知的类型），并接入宿主输出的违规识别 | `t006-closure.md` §11（F-61）。不是可见变化 |
| `Networking.Tests/Integration/` 下其余 8 个文件，`Host.Tests` 的 `ChannelHostStartupTests.cs`、`ComponentLifecycleTests.cs`、`LauncherHttpHostStartupTests.cs` | a1 1 个、a2 10 个 | 断言不变 | 见 §4.3 |
| 删除的 2 个 Networking 文件 | D | — | §4.1 删除表 |

- 修改过的线级文件里，没有一处断言变化表示客户端可见的变化。
- 客户端可见的变化，由 A 新增的线级与发送端层用例证明，逐条对到附录 G：VC-1～VC-8 见 `t006-closure.md` §4，VC-9 见 `t006-closure.md` §12，VC-10 见 `t009-docs-and-appendices.md` §8。逐条的证据用例见 `a-final-report.md` §3。
- 对不上附录 G 的线级断言变化：0。

## 5. 表征测试与金样本（AC-019、RQ-080、RQ-081）

- 表征测试的引入点是 `f01dc226`，基线运行日志 `logs/full-with-characterization.txt`。
  - `git diff f01dc226 2bdb1afd -- tests/Dfo.Server.Networking.Tests/Characterization` 只有两行：`SelectCharacterCompensationCharacterizationTests.cs` 的 b1、b2 去掉 `Skip`（TASK-23，`50d7bce4`）。断言文本自引入点起没有改；`Golden/` 下 15 个文件的 diff 为空（`logs/task-34-test-files.txt` 末段）。
  - 本次正式全量中 10 通过、0 跳过。
  - b1、b2 在基线上是带着「基线缺陷」的理由跳过的：它们表达 RQ-031(c) 的严格形式，基线做不到，由 T-006 开启。AC-019「在基线上通过」对这两条读作「在基线上记录为已知缺陷，断言不改」（TASK-34 主控方裁定第 6 条；`a-final-report.md` §4 的解读表）。
  - 附录 G 的差异逐条对应：表征的断言没有因任何一条 VC 而改动。VC-1 的证据是金样本 a 不变（`t006-closure.md` §4）。
- 选角金样本 `tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`：`a02bce46` → `2bdb1afd` 的 diff 为 0 行，本次通过。
- codec 向量（`research/05-tests.md` 第 33 行的口径；按文件名 `*Codec*Tests.cs`、`*Payload*Tests.cs`、`*Frame*Tests.cs`、`*Transcript*Tests.cs` 选取，脚本 `t34/codec.py`）：
  - 基准上 77 个文件：Protocol 63、Networking 13、Application 1。research 05 写的是「82 个文件」，用的是另一种选法，没有留下名单。差的 5 个按文件名选不出来，本单元不补；
  - 冻结点上未改 62 个；修改 15 个，其中 a1 5、a2 8、b 2（`PartyCommandCodecTests`：TASK-27 新增 VC-9 的边界用例，并删去访问方式断言，`t009-criteria.md` §3 第 7 行；`MarriageProposalCodecTests`：删 1 个以门为测试对象的方法，`t005-test-dispositions.md:33`）；c 0；
  - 调用 `ComposeAsync` 的 codec 文件，冻结点上有 7 个，都是 a1 或 a2，断言不变。research 05 写的是 9 个，另外 2 个的文件名不在上面的模式内。

## 6. 移交清单（只列 F 编号与去向）

以 `follow-ups.md` 终态为准。每条未关闭条目的「归属」末尾都有「〔A 封存：→ 去向〕」。核对见 `logs/task-34-followups.txt`。

| 去向 | 条目 | 数 |
|---|---|---:|
| 子 Spec B | F-07、F-12（附录 C 一半）、F-34、F-58（复核一半）、F-69（续写）、F-86、F-88、F-93、F-109、F-110（结构保证，过渡机制 T6）、F-112、F-122（改名）、F-124、F-127、F-129（新增） | 15 |
| 子 Spec B ＋ 交主控方另行立项 | F-128（① 子 Spec B；②③ 主控方） | 1 |
| 后续性能优化（交主控方立项） | F-77、F-78、F-79、F-80、F-81、F-82、F-120 | 7 |
| 交主控方另行立项 | F-01、F-113、F-114、F-116 (a)(b)、F-125（生产缺口）；测试基础设施与取证缺口 F-37、F-38、F-39、F-45、F-55、F-71、F-103、F-130（新增） | 13 |
| 用户 | F-117 | 1 |
| 外部审核（交审时说明的已接受偏离） | F-107、F-108 | 2 |
| 信息（不需要动作） | F-40、F-41、F-42、F-44、F-46、F-70、F-95、F-96、F-100；条件触发的 F-06、F-43 | 11 |

- 合计：15 ＋ 1 ＋ 7 ＋ 13 ＋ 1 ＋ 2 ＋ 11 ＝ 50 条未关闭（含部分关闭）。
- 清单共 130 行 ＝ 已关闭 80 ＋ 未关闭 36 ＋ 信息 11 ＋ 部分关闭 3。80 ＋ 36 ＋ 11 ＋ 3 ＝ 130；36 ＋ 11 ＋ 3 ＝ 50。
- 本单元关闭 F-05、F-10（各自的另一半已由 F-82 承接）与 F-69 的 A 一半；新增 F-129、F-130（F-130 是第 1 轮评审后按主控方处置第 4 条新增的）。
- 另有三件没有 F 编号的移交：
  - 「扇出变长字段的上限约定没有判据守着（AGENTS.md 已写明），是否设判据交子 Spec B」。F-118 已在 TASK-33 关闭，按主控方处置第 4 条写成遗留问题，不另开编号；
  - F-33 的临时文件偏离：F-33 已关闭，与 F-107、F-108 同列，交审时说明；
  - 过渡机制清单本身（`transition-mechanisms.md`），根 SC-13 要求它在 B 封存时检索为零。

## 7. 交主控方的封存数据（只准备，不改 `multi-spec-map.md`）

### 7.1 `Success Condition Progress` 的建议值（TASK-34 主控方裁定第 7 条；最终值由主控方在封存步骤决定）

Constraint 一栏逐条对照根契约 Constraints 的七条：顺序保证没有例外、保住保护意图、可见行为变化须列明、纯搬迁与语义改动分开提交、一次到位删净、单数据库写者、先 A 后 B。各 SC 一行只写结论，七条的依据见本节末的表。

| SC | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|
| SC-1 | satisfied | `a-final-report.md` §9（AC-001、AC-003、AC-004、AC-006）；`t009-docs-and-appendices.md` §3、§4；`t009-criteria.md` §1、§2 | none | aligned |
| SC-3 | satisfied | `a-final-report.md` §9（AC-007、AC-011、AC-012、AC-013，VC-10）；`t006-closure.md` §5、§6 | 附条件，都已有去向：AC-007、RQ-012「此前登记的消息」按连接计（外部审核时说明）；AC-011 线级第 3 条降到发送端层（F-108）；AC-012 的 XO-2～XO-7 由发送端通用用例承担、XO-6 是近似（VC-10）；AC-013 人群项的适用范围（F-113、F-114）；VC-10 第 3 句的衔接是读码 | aligned |
| SC-4 | satisfied | `a-final-report.md` §9（AC-017）；`t005-closure.md` §2、§3；`t009-docs-and-appendices.md` §6；`transition-mechanisms.md` §3 S2、S3 | none。短 `lock` 与 PVF 内容缓存锁按根 Scope Out 第 2 项保留；A 在 Networking 新增的执行器外短锁已在 `transition-mechanisms.md` §3 列出 | aligned |
| SC-5 | satisfied | `a-final-report.md` §9（AC-015）；`t004-closure.md` §3；`t005-closure.md` §5；`t007-closure.md` | none | aligned |
| SC-6 | satisfied | `a-final-report.md` §9（AC-016，SC-6 记「满足，附条件」）；`t007-closure.md` §1 | 附条件：RQ-050 末句的实现精度。CMD 已登记关闭、发送端还没执行时，传输层先断开，这次断开仍按继承处理，与 RQ-050 末句的字面不符（TASK-29 主控方裁定第 5 条；`t007-closure.md` §3 第 5 条、§4 第 2 条）。去向：随子 Spec B 复核，或在主控方立项的发送端精度改进中处理；外部审核时说明 | aligned |
| SC-7 | satisfied | `a-final-report.md` §3、§9（AC-014，VC-1～VC-10）；`t006-closure.md` §3、§4 | 附条件，已有去向：AC-014 的 DR-16 证据用占位作业代表进入序列；VC-10 的组合证据 | aligned |
| SC-8 | satisfied | 本文 §2～§5；`a-final-report.md` §6；选角端到端证据 `SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`（金样本 a） | none。新栈根已按主控方处置登记为偶发项（§2.2） | aligned |
| SC-9 | satisfied | `measurement/m3-summary.md` §1；`a-final-report.md` §6 | 未达原目标值的项是后续性能优化（F-77～F-82、F-120），不阻断 | aligned |
| SC-10 | satisfied | `t009-docs-and-appendices.md` §1、§11；`transition-mechanisms.md` §4 | F-128 ②③：RQ-100 点名范围之外的两份文档，交主控方 | aligned |
| SC-11 | satisfied | `a-final-report.md`（§2 是三项默示决定的复述） | 外部审核在 B 完成后进行（根 RI-16），审核结论不是封存的前提 | aligned |
| SC-12 | satisfied | `transition-mechanisms.md`；`logs/task-34-transition.txt` | none | aligned |

七条约束各自的依据（建议值，最终由主控方定）：

| 根契约约束 | 依据 | 建议 |
|---|---|---|
| 顺序保证没有例外 | 执行器外开写工作单元、登记发件箱、调用玩法写入口都立即抛出，没有开关（RQ-061，`t009-criteria.md` §1）；作业内入队后等待与等待网络都是程序错误（AC-002）；本次正式全量取证 73 条，全在反例类与对照里，`exe:` 0（§2.1） | aligned |
| 保住保护意图 | 附录 E 十七行逐行有承接与测试（`t006-closure.md` §3）；AC-009 的保护测试断言不变并通过（§3.3）；附条件只有 AC-014 的 DR-16，已写明 | aligned |
| 可见行为变化须列明 | 附录 G 的 VC-1～VC-10 逐条有证据用例与授权（`a-final-report.md` §3）；修改过的线级文件里没有未列明的可见变化（§4.4） | aligned |
| 纯搬迁与语义改动分开提交 | T-004 拆成测试迁移、入口逐类迁入、语义翻转三段，翻转单独成单元（TASK-17）；删锁（T-005）、投递翻转（T-006）、调用链（T-007）各自成单元，每个单元都有能失败的证据（表征测试、变异系列，见各单元日志）。R3 之后保留的过渡机制是有意留下、逐项列明的，不是把搬迁与语义混在一起（`plan.md` R3、`transition-mechanisms.md`） | aligned |
| 一次到位、删净 | 附录 F「删除」栏在 `Server/src` 检索为零（AC-017）；A 删除的 49 个名字只剩同名无关的命中（`transition-mechanisms.md` §3 S2）；A 封存时仍在的旧机制全部列入过渡机制清单 6 项，由 B 删除（SC-12） | aligned |
| 单数据库写者 | 执行器外写 SQLite 由运行期判据拒绝（RQ-061）；留在执行器外的部分逐项写明为什么安全（附录 B，`t009-docs-and-appendices.md` §4），开始监听前的启动期工作不设例外（F-16） | aligned |
| 先 A 后 B | A 的全部证据都在冻结点 `2bdb1afd` 上，不依赖 B 的任何工作（`spec.md` R3-1）；A 封存后不重开，审核意见走修正子 Spec 或并进 B（根 RI-16） | aligned |

### 7.2 spec-001 一行

- Result：`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`，由主控方在封存步骤写。
- `spec-result.md` 应引用两份文件（TASK-30 主控方裁定第 5 条）：`verification/transition-mechanisms.md`（过渡机制清单）与 `verification/a-final-report.md`（A 的最终汇报）。
- Git result：封存提交，由主控方填写。

### 7.3 封存提交前要再核的事项

- 冻结核对：`git diff 2bdb1afd -- Server Tool` 为空。
- 工作区：`git status --short` 只有本单元 Owned boundary 的文件（`logs/task-34-boundary.txt` 末尾），加上主控方自己写的封存文件。
- 本单元没有提交。提交时要把 `logs/task-34-build-test-raw.txt`（约 3.9 MB）一并纳入（主控方裁定第 1 条）。
