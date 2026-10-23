# TASK-32 — T-009c：最终测量 M-3，在 A 封存代码上按修订后的 RQ-091 判定

基准提交 `2bdb1afd`（TASK-31／T-009b 已提交），即代码冻结点（`t009-logs-and-cases.md` §8）。被测服务端就是冻结点的代码。相对 M-2 的被测代码（`f7bf0aa3`），中间是 T-007（TASK-29：调用链贯通与深度上限）、TASK-30（RQ-061 名单补全与写断言挂点）与 TASK-31（日志面与保护用例）：`git diff --stat f7bf0aa3 2bdb1afd -- Server/src` 为 45 个文件，＋735／－156 行。

本单元是 `tasks/TASK-30.md` §0 拆分中的第三个单元；边界提纲见 TASK-30 §0.2 的 TASK-32 一行。依据：
- 判定口径：`spec.md` RQ-091 的「用户修订（2026-10-22，执行中）」；TASK-20 的主控方裁定（S3 按 2 s 口径读；S4 无停止效力；(c)(e) 只有达到 2 s 线才停止）；`tasks/TASK-28.md` 的「主控方裁定（执行前）」1～6、「主控方裁定（回报后）」（F-119）与「主控方处置（第 1 轮评审后）」；`tasks/TASK-30.md`「主控方裁定（执行前）」第 6 条；
- S5 第 5 项的口径：`verification/t009-logs-and-cases.md` §4 的五条。出处是 `tasks/TASK-31.md`「主控方裁定（执行前）」第 2 条，以及同一文件「主控方处置（评审后，提交前）」第 1 条对它的修订（恢复 M-2 对 (d) 轮登录连接 Warning 行的豁免；F-126 的 `SocketException` 例外；探测连接没有行时记「本次无级别证据」）。原样采用，判定日志开头注明这两处出处；
- 做法：沿用 `tasks/TASK-28.md`（M-2）的全部做法，以及 `reviews/task-28-result-review.md` 两轮的意见。本文件只写与 M-2 不同的部分，相同的部分按节号引用 TASK-28。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **M-3 测量**（Plan T-009「最终测量」；RQ-090；SC-9）：在冻结点的 Release Host 上，用 M-2 的工具、命令行与 G1 dll，跑与 M-2 相同的 16 次运行，另加 2 次 G0 控制运行（共 18 次，§4.1），完整测出 (a)～(f)，口径同 TASK-28 Outcome 第 1 条。
  2. **判定**：按 §1 给出「M-3 通过」或「M-3 停止（命中 Sx）」。M-3 是硬停闸：停止时由主控方回到用户，本单元不做任何优化。
  3. **四方对照**：与基线（`baseline-summary.md`）、M-1（`m1-summary.md`）、M-2（`m2-summary.md`）逐项对照，写明 T-007、TASK-30、TASK-31 带来的变化（§4.4 的预期逐条对照）。
  4. **控制运行等价性**（§5.2）：按 `reviews/task-28-result-review.md` M2 的口径，稳态、(d)、(f)、(e) 四项，逐项检验每次 G0 是否落在两次 G1 运行的差距之内；(e) 同时给出 G0 口径的对照。
  5. **就绪探测连接的豁免写成规则**（§1.3、§2.3）：按连接行为识别，不按 host.log 行号登记；用 M-2 的 16 份 host.log 回归核对，并用合成反例证明规则会拒绝。
  6. **记录**：`verification/measurement/m3-*.md`、`raw/m3-*`、`verification/logs/task-32-*.txt`；`measurement/README.md` 的文件表与现状说明更新。
  7. **关闭与状态**（§6.3）：判定 AC-005 的测量一半、AC-021 与 F-02（F-02 归属「T-009 最终判定」）；更新 F-77～F-83、F-119～F-121、F-126 的 M-3 值与状态；新增编号从 F-127 起。
- Owned boundary：
  - **文档与证据**：
    - 新建 `verification/measurement/m3-summary.md`（先读这份）；
    - 新建 18 份运行记录（工具生成的 `summary.md`／`ramp.md` 原样保存，§4.1 的「记录」列）：`m3-{A,B}-{1,2}.md`、`m3-{A,B}-debug-{1,2}.md`、`m3-{A,B}-f256.md`、`m3-{A,B}-f256-debug.md`、`m3-B-g0.md`、`m3-B-g0-2.md`、`m3-B-g0-3.md`、`m3-ramp-full-2s.md`、`m3-ramp-full.md`、`m3-ramp-clean.md`，4 ＋ 4 ＋ 4 ＋ 3 ＋ 3 ＝ 18；
    - 新建 `verification/measurement/raw/m3-*.summary.json`（15 份，爬坡不生成）、`raw/m3-tool-version.txt`；
    - 条件运行（§4.2）的记录名属于上面两类的 `m3-*` 命名；
    - 修改 `verification/measurement/README.md`（§6.2）；
    - 修改 `verification/follow-ups.md`（§6.3）；
    - 新建 `verification/logs/task-32-{env,build,tool,runs,judgement,boundary}.txt`。
  - **scratchpad**：`t32/` 下的运行输出（`m3/`、`out/`、`samples/`、`signals/`、`env/`）与全部脚本。脚本从 `t28/` 复制后改写（§2.3），全文附在对应日志末尾。`t28/m2/` 留作 §2.4 的回归核对输入，不删、不改。
  - **工具**：只读。`Tool/GameplayLoad/*.cs` 不改；`bin/Release/net10.0/GameplayLoad.dll`（G1）与 `bin/m1-base/GameplayLoad.dll`（G0）原样使用。只有 Required check 2 发现 dll 与记录不符时，按 TASK-28 §2.2 重建（`bin/` 不入库）。
  - **不改**：`Server/` 下的一切（包括日志级别与临时探针）；`Tool/` 下的一切源码；`spec.md`、`plan.md`；`t009-criteria.md`、`t009-logs-and-cases.md` 与以往的 closure；`baseline*.md`、`m1-*`、`m2-*` 与 `raw/m1-*`、`raw/m2-*`；`AGENTS.md`、`Docs/`、`protocol/`。
- Required checks：
  1. **冻结核对与被测构建**（`logs/task-32-build.txt`）：
     - 执行开始时：`git rev-parse HEAD` 为 `2bdb1afd…`；`git diff 2bdb1afd -- Server Tool` 为空；`git status --porcelain -- Server Tool` 为空；
     - `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误；记录 `Dfo.Server.Host.dll` 的 SHA-256 与修改时间、`dotnet --version`；
     - 不跑测试套件（本单元不改代码；全量测试归 TASK-34）。
  2. **工具**（`logs/task-32-tool.txt`，结果写进 `raw/m3-tool-version.txt`）：
     - 八个源文件的 SHA-256 与 `raw/m2-tool-version.txt` 逐个相同，`git diff b1959f3e -- Tool/GameplayLoad` 为空；
     - G1 的 SHA-256 为 `deb158a5a422ec2b9e32dc84aed934d8d1ff1d29bc061ea31d8f14782264cefa`，G0 为 `b3d6c286e61607baf20c532fe7880f0902af60d60e40f2fb33435b53be899753`（规划时 2026-10-24 已核对两者相符）；
     - **解析自检**：每次 debug `run` 之后，`executorLines.total` 等于 `head -n <logLinesAtSnapshot> host.log | grep -cE 'Gameplay .+ (completed|failed):'`，数值解析失败 0。执行器失败行自 TASK-31 多了 `connection {SourceConnection}` 字段（`GameplayExecutor.cs:313-316`），完成行模板未变；解析式 `.*?\b(?:in|after) … ms(?: after|,) … ms queued` 规划时读码判断不受影响，执行时以自检为准。
  3. **环境与噪声**（`logs/task-32-env.txt`）：同 TASK-28 Required check 3，按 §3。
  4. **运行记录**（`logs/task-32-runs.txt`）：同 TASK-28 Required check 4；另加每次运行就绪锚点（§1.3 规则 P 的 P3）的 host.log 行与时刻、(d) 各轮的时间窗（规则 D）。
  5. **判定**（`logs/task-32-judgement.txt`）：
     - 开头先写 §1 的规则、`t009-logs-and-cases.md` §4 五条的原文及其两处出处、§4.4 的预期，然后才开始第一次正式运行；
     - 接着写 §2.4 的回归核对与规则反例的输出（也在第一次正式运行之前完成）；
     - 判定脚本按 §1 逐条输出「规则 → 各次运行的值 → 是否越线 → 样本是否干净」，发送端层的每一行输出归类（探测／(d) 登录／收尾／计入）与依据，最后一行是结论；
     - `m3-summary.md` 的数字表一律由脚本输出生成，不手抄。
  6. **记录引用核对**：`m3-summary.md` 中出现的每个文件路径存在；F 编号在 `follow-ups.md` 中存在且状态与表述一致（`t28/refs.py` 复制改写，口径同 TASK-28 Required check 6）。结果附在 `logs/task-32-judgement.txt` 末尾。
  7. **边界**（`logs/task-32-boundary.txt`）：
     - 执行结束时：`git diff 2bdb1afd -- Server Tool` 为空，`git status --porcelain -- Server Tool` 为空；
     - `git status --porcelain` 只含 Owned boundary 的路径与本任务文件；
     - `git -c core.whitespace=cr-at-eol diff --check` 无输出，新文件行尾 LF、无行尾空白；
     - 没有残留的 Host、testhost、GameplayLoad 与采样进程；已执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-31（`2bdb1afd`，代码冻结点；S5 第 5 项口径 `t009-logs-and-cases.md` §4；F-121 的降级、F-126）；
  - TASK-30（主控方裁定第 6 条：矩阵与判定口径）；
  - TASK-28（M-2 的工具版本、命令行、脚本 `t28/`、全部裁定与评审；`t28/m2/` 的 16 份 host.log 作回归核对输入）；
  - TASK-20（M-1 记录与裁定）、TASK-03（基线记录）；
  - **执行前提**：工作区除本任务文件外干净；§7 待裁定各条有裁定。
- Result review：required。
  - M-3 是结果 A 封存前唯一的测量证据：AC-005 的测量一半、AC-021 与 F-02 由它判定，TASK-34 的 A 最终汇报与 AC 状态表直接引用它的结论。判定若把一次越线归入「工具主动断开」「收尾，不计」或「受污染」而放过，A 就带着一个未被发现的数量级问题封存；反过来把环境污染或工具迟到判成越线，则无谓地停下封存。两种错都不会让任何检查变红。
  - 本单元第一次用「按行为识别」的规则代替逐行登记；规则写宽会放过真正的发送失败，写窄会把就绪探测判成命中。§2.4 的回归核对与反例能暴露明显的错，但条件本身是否恰当只能靠复核。
  - 产出以判断为主：干净／污染的分类、发送端层行的归类、控制运行等价性的结论、F 项的去向与 AC 的记法。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. §1 的停止线 S1、S2、S3、S5 在干净样本中被越过（确认程序见 §3）。这是本单元的预期产出之一，不是执行失败：照常完成其余运行与记录，结论写「M-3 停止」；
    2. 在 3 次尝试内取不到某类运行的干净样本，且值落在停止线 0.5～2 倍之间，无法判定；
    3. 被测 Host 无法在负载下完成一次运行（启动失败、进程崩溃、工具因 Host 行为异常退出）；
    4. 需要改 `Server/` 或 `Tool/` 下的任何文件（包括为测量加探针、改日志级别、解析自检不相等时修正则），需要改既有口径、Spec 验收或 `t009-logs-and-cases.md` §4 的规则；
    5. 冻结核对（Required check 1 或 7）不为空。
  - 归因只允许读日志、读代码、读已有记录，不做性能剖析与代码试改；需要更深的归因时，在回报中写明建议的手段，由主控方决定。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 定位与执行顺序

**M-3 回答两个问题**：
1. 结果 A 封存时的代码（冻结点）有没有数量级的性能问题或行为异常，使 A 不能按修订后的 RQ-091 接受；
2. 封存状态下 (a)～(f) 的正式数字是多少，与基线、M-1、M-2 比变了什么。

用户立场不变：性能能接受就行；单条指令 ≤ 2 s 都算接受；只有数量级问题才回到用户（RQ-091 用户修订）。停止线因此与 M-2 相同，未达原目标值的项只记录，列为后续性能优化事项。

**与后续步骤的关系**：
- **M-3 通过**：AC-005 的测量一半、AC-021（A 一半）满足，F-02 关闭（§6.3）；TASK-33 照常开始。TASK-34 的 AC 状态表与 A 最终汇报引用 `m3-summary.md`。
- **M-3 停止**：TASK-33、TASK-34 是否继续、A 是否照常封存，由主控方带着本单元的停止记录回到用户决定。本单元不提出路线修改，只在 `m3-summary.md` 的「建议的下一步」里写可选手段。
- 子 Spec B 封存后还要再测一次（根 SC-9B，`spec.md` 修订记录 R3-1）。本单元在 §6.1 第 9 节写给它的交代。

**执行顺序**：
1. 冻结核对与被测构建（Required check 1）；
2. 工具核对（Required check 2）；
3. 从 `t28/` 复制并改写脚本（§2.3）；把 §1 的规则、§4 五条原文与 §4.4 的预期写进 `logs/task-32-judgement.txt` 开头；
4. 回归核对与规则反例（§2.4），结果写进判定日志；不通过即停在这里修脚本（脚本在 scratchpad，不属停止条件），直到通过才开始正式运行；
5. 按 §4.1 的矩阵依次运行，每次之前做静默检查；
6. 判定脚本出结论，必要时按 §3 重跑；
7. 写 `m3-summary.md`，更新 README 与 `follow-ups.md`；
8. 引用核对与边界核对（含结束时的冻结核对）。

### 1. 判定规则

#### 1.1 停止线

任一条在干净样本中成立，M-3 即停止。S1～S4 与 M-2 完全相同（`tasks/TASK-28.md` §1.1、§1.5 的原文，含 S2 覆盖 (e) 窗口与窗口 256、S3 按 ramp-A-2s 的 2 s 口径与构成 B 的读法），这里只列要点：

| 编号 | 规则 |
|---|---|
| S1 | 单条 CMD：Host 就绪之后，任何执行器作业的执行时长 > 2000 ms，或任何 `CommandLoggingBehavior` 内层命令 > 2000 ms。启动前的工作不计 |
| S2 | 秒级停顿：(f) 冷启动前 60 s 的最长单次停顿 ≥ 2000 ms；或任一窗口的入队等待 max ≥ 2000 ms；或稳态、(d)、(e)（窗口 32 与 256）窗口内其余连接的端到端样本在判定序列与参考序列上都 ≥ 2000 ms |
| S3 | 吞吐：构成 A 在 2 s 口径爬坡（ramp-A-2s）下的可持续吞吐 < 400 包/秒；构成 B 按 TASK-28 §1.1 的读法 |
| S4 | 只记录，无停止效力：(d) 轮内的入队等待 p99，对照目标 50 ms |
| S5 | 正确性，见 §1.2、§1.3 |

#### 1.2 S5 的各项（正确性项全部保留）

1. 任一次运行 `summary.json` 的 `errors` 非 0；
2. 任何失败命令或失败作业（`Command … failed in handler`、`Gameplay … failed`），不设时间豁免（TASK-28 裁定第 2 条）；
3. `host.log` 中出现必须为 0 的信号：M-2 的 15 项（`t28/signals.py` 的 `ZERO`，含 `Admitting an outbound entry failed`），原样沿用。规划时在冻结点上逐项检索消息模板：`database is locked` 是 SQLite 的消息，`direct send deadline exceeded` 的产生代码已在 TASK-23 删除，其余 13 项的模板都还在 `Server/src` 中；`GameplayOffExecutorException` 的消息就是其中的 `must happen inside a gameplay job`。停机阶段以外的 `GameplayExecutorStoppedException` 照旧报告；
4. `Outbound connection … closed: OverLimit`，任何时刻出现即命中（`t009-logs-and-cases.md` §4 第 2 条）；
5. **发送端层的行**，按 §1.3 归类：归类为「计入」的任何一行即命中。

#### 1.3 发送端层的行：`t009-logs-and-cases.md` §4 五条的执行方式

判定日志开头逐字抄录 §4 的五条，并写明出处（`tasks/TASK-31.md`「主控方裁定（执行前）」第 2 条，同文件「主控方处置（评审后，提交前）」第 1 条修订）。下面是它们在判定脚本里的执行方式，不改变规则本身。

**要归类的行**：
- Warning 及以上：`Outbound connection … closed: TimedOut|Failed`、`Outbound entry for …: TimedOut|Failed|OverLimit`、`Closing an outbound connection failed`、`An outbound delivery observer failed`、通知总线告警（`Notification for character … failed.`）；
- Information：带后缀 `; the connection was already gone` 的两种行（`OutboundSender.cs:257`、`:293`）。M-2 的 `signals.py` 只扫 `warn:`／`fail:`／`crit:` 行，本单元必须改成也扫 `info:` 行里带后缀的这两种（§2.3），否则 §4 第 3 条会变成看不见的口径变化（TASK-31 §4.4 的说明）。

**四种归类**，按下列顺序判定，先满足的为准：

- **T 收尾**：行的时刻 ≥ 收尾窗口起点。起点取法同 M-2 实际做法（`tasks/TASK-28.md` Result「Why」第 1 条）：`run` 为「logins done, flooding」那一行的时刻 ＋ 30 s ＋ 2 s；`ramp` 为最后一行「ramp N: …」的时刻。Warning 行与 Information 行都「收尾，不计」，按原因与级别计数。起点前 1 s 内的行单列，逐条说明（§4 第 1、3 条）。
- **P 就绪探测连接**：行所属的连接 C 同时满足 P1～P4（§4 第 3 条「按连接行为识别」的执行方式；待裁定第 3 条）：
  - **P1 首条**：C 的编号是本次 host.log 中出现的全部 `Game:` 连接编号里最小的（编号 `b0-b1-b2-b3` 按小端整数解析）。依据：工具只在就绪之前开这一条游戏连接（`Tool/GameplayLoad/HostProcess.cs:227-234`，连上即随 `using` 关闭，不写任何字节）；agent 与 (d) 登录连接都在就绪之后才开（`Scenarios.cs`）。M-2 的 16 次运行里它都是 `Game:01-00-00-00`；
  - **P2 无入站行为**：host.log 中提到 C 的行只有发送端层的行。入站帧的 handler 信息行带连接号（例如 `Create character Game:…`、`Set user area Game:…`），执行器失败行自 TASK-31 带 `connection Game:…`；M-2 的 B-1 中 21 条 agent 连接都被 handler 行提到过。「没发过入站帧」本身来自工具源码，P2 是 host.log 上可以检验的必要条件，记录中写明这一证据边界；
  - **P3 时刻**：C 的每一行都在 host.log 第一条 `Request finished … GET …/api/launcher/v1/bootstrap - 200` 之后 5 s 之内。工具拿到这个 200 之后才连游戏端口（`HostProcess.cs:207-234`）；M-2 实测约 0.1 s；5 s 远早于 (d) 窗（就绪后 180 s 起）。锚点取自 host.log 本身，`run` 与 `ramp` 同样适用，不依赖爬坡缺少的 `originUtc`；
  - **P4 结局**：C 的行只有 `Failed` 结局（entry 与 connection closed）。`TimedOut`、`OverLimit` 及其他行不归此类。
  - 满足 P 时：带后缀的 Information 行「工具主动断开（就绪探测），不计」（§4 第 3 条）；Warning 的 `Failed` 行按 §4 第 4 条——异常是 `SocketException`（Broken pipe 或 Connection reset）时按 F-126 记录并回报、不计；其余异常（例如 M-2 那 16 行的 `ClientNotConnectedException`）即 S5 命中，说明 F-121 的判定没有覆盖真实进程里的这条路径。
- **D (d) 轮的登录连接**：行所属的连接 C 不满足 P，满足 P2 与 P4 以外的下列条件（§4 第 1、3 条；待裁定第 4 条）：
  - C 只被发送端层的行提到（同 P2）；
  - C 的每一行都落在某一轮 b 的时间窗内：`[该轮第一个 LOGIN 发出, 该轮最后一个 LOGIN 应答结束 ＋ 1 s]`。时刻取自 `samples.csv` 中 `group = burst` 的 LOGIN 行（`t_send_ms ＋ latency_ms`，相对 `originUtc`），换算到 host.log 的墙上时钟；
  - 归为 D 的连接数不超过本次运行的 (d) 登录数（6 轮 × 10）。
  - 满足 D 时：Warning 行与 Information 行都「工具主动断开（(d) 登录连接），不计」；其中 Warning 的 `Failed` 且异常是 `SocketException` 的，另按 F-126 计数。
- **计入**：不属于 T、P、D 的全部行。任何一行即 S5 命中（§4 第 1 条的 Warning 行、第 3 条「归因不上的计入 S5」的 Information 行）。

**级别生效的证据**（§4 第 4 条）：每次运行单列就绪探测连接上的行数，按级别与是否带后缀分开。构成 B 预期每次 2 行带后缀的 Information、0 行 Warning；某次运行探测连接上一行都没有，记「本次无级别证据」，不算命中。构成 A 在 M-2 中探测连接没有行，M-3 同样可能如此。

**`Writing is not allowed after writer was completed`**：计数口径不变（§4 第 5 条），出现在失败作业里时已由第 2 项命中。

#### 1.4 目标值（只记录、作对照）

同 TASK-28 §1.4：(b) ≥ 2000 CMD/秒；(c)(d)(e) 入队等待 p99 ≤ 50 ms；玩法 CMD 单条 p99.9 ≤ 100 ms、max ≤ 250 ms；(f) 最长单次停顿 ≤ 250 ms。(b) 照旧写可持续吞吐与稳定通过档两个数（F-77 的评审补记：不得把可持续吞吐当作余量依据）。每个未达项在 `m3-summary.md` 的「后续性能优化事项」表中列一行。

#### 1.5 相对 M-2 显著变差（只记录，无停止效力；待裁定第 6 条）

M-3 与 M-2 之间只有 45 个文件的改动，预期数字同量级（§4.4 第 1 条）。某个量在两次同类运行中都比 M-2 的对应范围差 2 倍以上、且超出 M-2 两次运行的差距时，在 `m3-summary.md` §1 单列「相对 M-2 显著变差」，按 §5.3 的顺序只读归因。它不是停止线；达到 S1～S3 才停止。TASK-28 §3.5 的基线重测触发条件中「超过 M-1 的 3 倍」改为「超过 M-2 的 3 倍」（构成 B 默认级别稳态端到端 p99，M-2 为 20.52～26.07 ms）。

### 2. 工具与脚本

#### 2.1 工具不改

- 除控制运行外全部用 G1（`bin/Release/net10.0/`），控制运行用 G0（`bin/m1-base/`）；依据 TASK-28 裁定第 3 条与 `m2-summary.md` §9。
- 发送端的交付时刻仍是 Trace 级，不打开（改日志级别在边界外）；延迟构成照旧按分布相减估计（TASK-28 §5.3）。

#### 2.2 dll 与记录不符时

同 TASK-28 §2.2。

#### 2.3 脚本（scratchpad `t32/`）

从 `t28/` 复制后改写，改动处在脚本头部注释列出，全文附进日志：

| 脚本 | 改写 |
|---|---|
| `matrix.sh` | 输出目录 `t32/m3`；运行名单换成 §4.1 的 18 次；tag 由 `m2*` 换成 `m3*`；新增 `B-def-g0-2`、`B-def-g0-3` 两个名字（G0，输出 `$O/B-g0-2`、`$O/B-g0-3`）；静默检查、环境快照、时间戳过滤 `ts.py` 不变 |
| `ts.py`、`sampler.py`、`sampsum.py`、`teardown.py` | 不改口径，只改路径 |
| `signals.py` | `ZERO` 不变（§1.2 第 3 项）；发送端层的行除 `warn:`／`fail:`／`crit:` 外，也识别 `info:` 行中带 `; the connection was already gone` 后缀的两种；每行输出连接号、级别、结局、是否带后缀、异常类型（从行内文本取，`SocketException` 另取 `(32)`、`(54)` 等错误号与 Broken pipe／Connection reset 字样）；另输出：host.log 中每个 `Game:` 连接号被哪些非发送端层的行提到（按 logger 计数）、第一条 bootstrap 200 行的行号与时刻、`Game handler failed` 计数（F-30 之后应为 0，只作信息） |
| `classify.py`（新） | §1.3 的 T／P／D／计入归类，输入 `signals.py` 的输出、`samples.csv` 与收尾窗口起点，逐行输出归类与依据（满足了哪几条、不满足哪一条）。`judge.py` 只读它的结果，不另写一套归类 |
| `judge.py` | 删去 `ATTRIBUTED` 与按行号的断言，S5 第 5 项改读 `classify.py` 的结果；新增 M-2 列（读 `raw/m2-*.summary.json`），三方对照改为四方；控制运行一节改为三次 G0（§5.2）；「按字面不计裁定时」的那一行读法删去（M-3 没有逐行裁定） |
| `rules_selftest.py`（新） | §2.4 的合成反例与正例 |
| `composition.py`、`coldmax.py`、`jobtypes.py`、`floodjob.py`、`fill.py`、`refs.py` | 换路径、模板与运行名；`refs.py` 的核对范围改为 `m3-summary.md` |
| `logs.sh`、`judgelog.sh`、`toollog.sh`、`buildlog.sh`、`boundary.sh`、`post.sh` | 换路径与日志名；`buildlog.sh` 与 `boundary.sh` 加 `git diff 2bdb1afd -- Server Tool` 与 `git status --porcelain -- Server Tool` |

#### 2.4 规则的回归核对与反例（正式运行之前）

1. **回归核对（M-2 数据）**：对 `t28/m2/` 的 16 份 host.log 跑 `signals.py` ＋ `classify.py`（收尾窗口起点取 `logs/task-28-runs.txt` 记录的值）。通过的条件，三条同时成立：
   - 归为 P 的恰好是 M-2 `judge.py` 的 `ATTRIBUTED` 登记的 16 行：同样的 8 次运行、同样的行号，全部是 `Game:01-00-00-00` 上的 `Failed`；
   - 归为「计入」的行为 0；
   - 归为 T 的计数与 M-2 的「收尾，不计」相同（ramp-A-2s 24 行、A-dbg-f256 12 行，其余为 0）。

   这证明新规则在 M-2 上与逐行裁定逐行相同。M-2 那 16 行是 Warning；新规则在 M-3 中对它们的处置按 §1.3 P 的级别分支，与回归核对无关。
2. **反例**（`rules_selftest.py`，合成的 host.log 片段与 `samples.csv`），下列每一例都必须归为「计入」：
   - R1：agent 连接上的 `Failed` 行，该连接另被一行 `Set user area Game:…` 提到（P2、D 都不满足）；
   - R2：编号最小、只有发送端层行的连接，但行在 bootstrap 200 之后 6 s（P3 不满足；也不在任何 (d) 轮窗内）；
   - R3：探测形态的连接，结局是 `TimedOut`（P4 不满足）；
   - R4：只有发送端层行、在 bootstrap 之后 0.1 s，但编号不是最小的（P1 不满足）；
   - R5：只有发送端层行、编号较大的连接，行在某一轮最后一个 LOGIN 应答之后 1.5 s（D 不满足）；
   - R6：收尾窗口起点之前 3 s、agent 连接上的带后缀 Information 行（第 3 条「归因不上」）；
   - R7：`OverLimit` 出现在收尾窗口内（第 2 条，任何时刻都命中）。
3. **正例**，必须归为不计：探测连接上带后缀的 Information 两行（P）；(d) 轮窗内 `SocketException (32): Broken pipe` 的 Warning `Failed`（D，另记 F-126）；收尾窗口内的 Warning `Failed`（T）。
4. 回归核对与反例的输出全文写进 `logs/task-32-judgement.txt`；任何一条不符，先改脚本再重跑本节，不开始正式运行。

### 3. 测量环境与噪声控制

同 TASK-28 §3.1～§3.5，逐条沿用（环境取证、静默检查、每秒采样与 P1～P4 污染判定、每类 2 次并按「越线只出现在受污染样本中则同类重跑，最多再 2 次」确认、基线与 M-1、M-2 记录沿用不重测）。差别只有两处：
- §3.5 的基线重测触发条件按 §1.5 改为对照 M-2；
- 磁盘：规划时（2026-10-24）数据卷可用 588 GiB；M-2 的 `t28/m2/` 为 4.6 GB，M-3 的 18 次运行预计 5～6 GB。`t20/`、`t28/` 不删。

开跑前按 TASK-28 的经验先查本工作区遗留的孤儿测试 Host（ppid 1 的 `Dfo.Server.Host`），有则结束并记在 `logs/task-32-build.txt` 末尾。

### 4. 运行矩阵与命令行

#### 4.1 矩阵

```bash
G1=Tool/GameplayLoad/bin/Release/net10.0/GameplayLoad.dll  # deb158a5…，除控制运行外全部用它
G0=Tool/GameplayLoad/bin/m1-base/GameplayLoad.dll          # b3d6c286…，只用于三次控制运行
B="--weights 0.5,0.5,0,0,0 --entry-concurrency 1"
RATES=400,800,1200,1600,2000,2400,3000,4000,5000,6000,8000,10000,12000,15000,20000,25000
O=<scratchpad>/t32/m3
```

M-2 的 16 次保持原次序与命令行（`logs/task-28-runs.txt`），只换输出目录与 tag；新增的两次 G0 插在两次 G1 构成 B 默认级别运行之后（待裁定第 1 条）：

| 次序 | 名称 | 命令 | 记录 |
|---|---|---|---|
| 1 | B-def-1 | `dotnet $G1 run --out $O/B-1 --tag m3b1 --seed 1 $B` | `m3-B-1.md` |
| 2 | B-def-g0-2（控制） | `dotnet $G0 run --out $O/B-g0-2 --tag m3bg02 --seed 1 $B` | `m3-B-g0-2.md` |
| 3 | A-def-1 | `dotnet $G1 run --out $O/A-1 --tag m3a1 --seed 1` | `m3-A-1.md` |
| 4 | B-dbg-1 | `dotnet $G1 run --out $O/B-debug-1 --tag m3bd1 --seed 1 $B --executor-log debug` | `m3-B-debug-1.md` |
| 5 | A-dbg-1 | `dotnet $G1 run --out $O/A-debug-1 --tag m3ad1 --seed 1 --executor-log debug` | `m3-A-debug-1.md` |
| 6 | ramp-B | `dotnet $G1 ramp --out $O/ramp-clean --tag m3r2 --seed 1 $B --rates $RATES` | `m3-ramp-clean.md` |
| 7 | ramp-A-2s | `dotnet $G1 ramp --out $O/ramp-A-2s --tag m3r1s --seed 1 --executor-log debug --ramp-criterion max:2000 --rates $RATES` | `m3-ramp-full-2s.md` |
| 8 | B-def-2 | 同 1，`$O/B-2`，tag `m3b2` | `m3-B-2.md` |
| 9 | B-def-g0-3（控制） | 同 2，`$O/B-g0-3`，tag `m3bg03` | `m3-B-g0-3.md` |
| 10～12 | A-def-2、B-dbg-2、A-dbg-2 | 同 3～5，`-2` 后缀，tag `m3a2`、`m3bd2`、`m3ad2` | `m3-*-2.md` |
| 13 | ramp-A | `dotnet $G1 ramp --out $O/ramp-full --tag m3r1 --seed 1` | `m3-ramp-full.md` |
| 14 | B-def-f256 | 同 1，加 `--flood-window 256`，`$O/B-f256`，tag `m3bf` | `m3-B-f256.md` |
| 15 | A-def-f256 | 同 3，加 `--flood-window 256`，`$O/A-f256`，tag `m3af` | `m3-A-f256.md` |
| 16 | B-dbg-f256 | 同 4，加 `--flood-window 256`，`$O/B-f256-debug`，tag `m3bfd` | `m3-B-f256-debug.md` |
| 17 | A-dbg-f256 | 同 5，加 `--flood-window 256`，`$O/A-f256-debug`，tag `m3afd` | `m3-A-f256-debug.md` |
| 18 | B-def-g0（控制） | `dotnet $G0 run --out $O/B-g0 --tag m3bg0 --seed 1 $B` | `m3-B-g0.md` |

- 去掉第 2、9 两行，次序与 M-2 的 1～16 一一对应。
- 每次完成后，把 `summary.md`（爬坡是 `ramp.md`）原样复制为记录名，`summary.json` 复制为 `raw/m3-<名称>.summary.json`；`samples.csv`、`host.log`、`.out` 留在 scratchpad。
- 预计耗时：一次 `run` 约 5 分钟，ramp-B 约 6 分钟，ramp-A 与 ramp-A-2s 各 1～3 分钟；18 次合计约 1.7 小时，加静默检查约 2.2 小时，不含重跑。

#### 4.2 条件运行

同 TASK-28 §4.2：ramp-B 有一档停止时加跑 ramp-B-2s（记录 `m3-ramp-clean-2s.md`）；确认重跑加 `-r1`、`-r2` 后缀。

#### 4.3 两组的用途与日志开销

同 TASK-28 §4.3。默认级别组为次序 1、2、3、8、9、10、14、15、18（其中 2、9、18 是控制运行），debug 组为 4、5、11、12、16、17 与 ramp-A-2s。窗口 256 按 14 对 16、15 对 17 配对。

#### 4.4 预期（运行之前写进 `logs/task-32-judgement.txt`）

只用来事后对照，不影响判定：
1. **数字与 M-2 同量级**：M-2 → M-3 的生产改动对每个作业只多出常数级的检查——入队时的泄漏判定（`GameplayExecutor.cs:42-45`）与调用链传递（TASK-29），每个挂点写成员一次 `GameplayJob.Active` 判定（`GameplayAssertions.cs:41-48`，TASK-30），日志级别与失败模板（TASK-31）。预期 (a)～(f) 各项与 M-2 的差在两次运行的差距量级之内；轻量作业 p50 持平。
2. **就绪探测的级别**：构成 B 每次运行探测连接上 2 行带后缀的 Information、0 行 Warning。依据（读码，`reviews/task-31-result-review.md` 第 1 条）：TouchSocket 只在 `Online` 为 false 时抛 `ClientNotConnectedException`，`Resolve`（`OutboundTransport.cs:64-72`）读同一个字段，关闭后不会恢复。构成 A 预期 0 行。
3. **收尾窗口内的行**：大部分由 Warning 变为带后缀的 Information；剩下的 Warning 是 `TimedOut`，或对端重置时 `SocketException` 形态的 `Failed`（F-126）。
4. **正确性**：错误、失败命令、失败作业都是 0；必须为 0 的 15 项都是 0；`OverLimit` 0；归为「计入」的发送端层行 0；`Game handler failed` 0（F-30 之后生产者那条降为 Debug）。
5. **控制运行**：三次 G0 的 (e) 其余连接 p99，若落在 M-1 的 4.4～4.9 ms 附近，M-2 那一次 17.84 ms 多半是噪声；若三次都在 15 ms 以上，G0 口径下 (e) 确实比 M-1 大约 3～4 倍，F-120 按 G0 口径改写。
6. **解析自检相等**，数值解析失败 0。

### 5. 各量的取法与对照

#### 5.1 (a)～(f) 与四方对照

取法同 TASK-28 §5.1～§5.5：(a) 作业层取 debug 组与 ramp-A-2s，内层命令取全部运行；(b) ramp-A-2s 判 S3，ramp-A 作目标对照，ramp-B 判构成 B，并写稳定通过档；(c)(d)(e) 的入队等待、端到端与延迟构成估计（分布相减）；(f) 冷启动与两个倍数（2 s 线与目标 250 ms）；C 类、D 类首次加载与覆盖缺口（F-82 原样，负载仍是 `GmWeb__Enabled=false`、5 分钟不跨日切、动作集不变）。

四方对照表沿用 `m2-summary.md` §5.1 的行，列为：base-A、base-B-1、base-B-2（`raw/baseline-1`、`clean-1`、`clean-2`）、M-1 四列、M-2 的默认级别四列、M-3 的默认级别四列与 debug 四列、M-3 窗口 256 四列、M-3 三次控制运行。数值全部由脚本从 `raw/*.summary.json` 读出。M-1 → M-2 → M-3 的变化逐项写一列「M-2 → M-3」。

#### 5.2 控制运行的等价性

- **判据**（`reviews/task-28-result-review.md` M2；TASK-28 裁定第 3 条）：对每次 G0（B-def-g0、B-def-g0-2、B-def-g0-3）与四项中的每一项（稳态端到端 p99、(d) 背景请求 p99、(f) 最长单次停顿、(e) 其余连接 p99），检验 G0 的值是否落在 `[两次 G1 的较小值 − 差距, 较大值 ＋ 差距]` 之内，G1 为 B-def-1、B-def-2，差距为两者之差。3 × 4 ＝ 12 项逐项打印。
- **结论的写法**（待裁定第 2 条）：12 项全部在区间内，写「默认级别下两个 dll 等价（三次控制运行）」，基线对照可直接用 G1；有任何一项不在，写「未证明等价」，列出不在区间内的项。三次 G0 的范围与两次 G1 的范围另列一行，只作说明。
- **(e) 的 G0 口径对照**：无论等价与否都写。三次 G0 的 (e) 其余连接 p99（逐次值、范围、中位数）对 M-1 默认级别组（G0，4.42～4.94 ms）、M-2 的一次 G0（17.84 ms）与基线（2.71 ms），写出倍数与算式。三次 G0 的结论改写 F-120 中「G0 只跑了 1 次，可能含噪声」那一句（§4.4 第 5 条）。

#### 5.3 归因（未达目标值时，或 §1.5 单列的项）

同 TASK-28 §5.3 末段的五步（日志开销、执行时长之和最大的作业类型及其占比、与 M-2 的分解对比、作业结束之后一段的估计、结论分类）。M-3 新出现的成因登记新编号（§6.3）。

#### 5.4 日志信号

同 TASK-28 §5.7，另加：
- 发送端层的每一行：连接号、级别、结局、后缀、异常类型、§1.3 的归类及依据；
- 就绪探测连接上的行数（按级别与后缀），作 §4 第 4 条的级别证据；
- F-126 形态（工具先断开的连接上、Warning 的 `Failed`、`SocketException`）的逐条清单；
- F-121 在真实进程中的表现：全部运行合计，`Failed` 结局按「Warning／带后缀的 Information」与归类分开计数。

### 6. 记录与 `follow-ups.md`

#### 6.1 `m3-summary.md` 的结构

开头写日期、基准提交 `2bdb1afd`（代码冻结点）、「合成负载」声明、用途（结果 A 的最终测量，关闭 AC-005 的测量一半、AC-021 与 F-02）、判定口径的出处（§1 的各条，含 §4 五条的两处出处）。

1. **结论**：M-3 通过／停止（命中哪条、在哪次运行、确认过程）；AC 关联与 F-02（§6.3）；相对 M-2 显著变差的项（§1.5）；
2. **环境**：同 `m2-summary.md` §2 的写法，逐窗口干净／污染；
3. **运行矩阵**：每次的命令、dll、起止、退出码、`hostLogLines`、收尾窗口起点、就绪锚点；
4. **判定表**：S1～S5 逐条（脚本输出生成）；发送端层行的归类表（每次运行：T、P、D、计入四栏，P 栏分级别；F-126 形态单列）；规则的回归核对与反例结果的摘要；
5. **四方对照**（§5.1）与控制运行（§5.2）、作业层与内层命令、吞吐爬坡、稳态端到端；
6. **T-007、TASK-30、TASK-31 带来的变化**：§4.4 六条预期逐条「符合／不符合／无法判断」与依据；延迟构成估计；
7. **(c)(e) 正式数字与归因**；
8. **后续性能优化事项**：每个未达目标值的项一行，对应的 F 编号；
9. **对子 Spec B 最终测量的交代**：沿用的命令行与 dll；§1.3 的归类规则与 `classify.py` 的位置（规则以本记录与 `t009-logs-and-cases.md` §4 为准，脚本在 scratchpad，全文在 `logs/task-32-judgement.txt`）；控制运行的结论；本单元的覆盖缺口；
10. **缺口与局限**：沿用 `m2-summary.md` §10 的各条，按 M-3 的实际改写；加上 P2 只是「没发过入站帧」的必要条件、(d) 轮按轮时间窗而不是按单条连接的断开时刻判定（服务端连接号与工具的登录号在默认级别下对不上）。

#### 6.2 README 更新

README 是现状说明，按 AGENTS.md「过期的记载怎么办」直接改：
- 文件表加 M-3 的各行（`m3-summary.md` 标「M-3 先读这份」）；
- 「入队等待」节：失败行模板多了 `connection {SourceConnection}` 字段；「工具版本」一条写 M-3 三次控制运行的结论；
- 发送端层告警的现状：「连接已不在」的 `Failed` 为带后缀的 Information（F-121），测量判定的归类规则见 `m3-summary.md`。

#### 6.3 `follow-ups.md`

文件头补一条 TASK-32 的说明（日期、基准 `2bdb1afd`、结论、更新与关闭了哪些编号、新增编号范围）。执行时再检索「M-3」「T-009 最终」「TASK-32」，确认没有遗漏。

| 项 | 今天的归属与状态 | M-3 通过时 | M-3 停止时 |
|---|---|---|---|
| F-02 RQ-091 达标判定 | T-009 最终判定；未关闭 | **已在 TASK-32 关闭**：写 M-3 结论与证据位置 `measurement/m3-summary.md` §1；AC-005 的测量一半记「M-3 满足（修订后的 RQ-091 接受线）」，AC-021 记「A 一半满足」，两条的最终状态由 TASK-34 的 AC 状态表引用（待裁定第 5 条） | 不关闭，写「M-3 停止」及证据位置，归属不变 |
| F-77～F-81 | 后续性能优化（交主控方立项）；未关闭 | 追加 M-3 值与 M-2 对照；达到原目标值才关闭，否则仍未关闭（待裁定第 6 条） | 同左 |
| F-82 覆盖缺口 | 后续性能优化；未关闭 | 注明 M-3 同样未覆盖；若出现新的 `event:` 类型，给出实测 | 同左 |
| F-83 断线竞态的作业失败 | 已在 TASK-23 关闭 | 不改状态；M-3 的作业失败计数写在 `m3-summary.md` | 同左 |
| F-119 就绪探测（M-2 按字面的 S5 命中） | 已裁定，不计 | **已在 TASK-32 关闭**：豁免已写成 §1.3 的规则 P，回归核对在 M-2 数据上逐行复现了 16 行；写 M-3 中探测连接的行数与级别（待裁定第 7 条） | 若停止与探测连接有关，保持原状态并写明；否则同左 |
| F-120 灌包与构成 B 的 (e) | 后续性能优化；未关闭 | 追加 M-3 值与三次 G0 的口径，改写「G0 只跑了 1 次」那一句 | 同左 |
| F-121 发送端日志级别 | 已在 TASK-31 关闭 | 不改状态；追加真实进程中的证据（§5.4 最后一条）。若级别没有生效，即 S5 命中，走右列 | 写明级别没有生效的那几行；另立新编号交主控方 |
| F-126 对端重置时的先后 | TASK-32；未关闭 | M-3 判读：F-126 形态的行数与所在连接；没有出现、或出现但都归为不计时，**已在 TASK-32 关闭**，并注明子 Spec B 的最终测量沿用同一判读（待裁定第 7 条） | 保持未关闭，回报 |
| F-69、F-125 | 测试套件相关 | 不更新（本单元不跑测试套件；F-69 由 TASK-34 续写） | 同左 |

新编号从 F-127 起，归属写明「后续性能优化（交主控方立项）」「覆盖缺口」「子 Spec B」或「主控方裁定」，状态「未关闭」。

### 7. 命中停止线时

1. 照常完成矩阵里其余的运行，除非 Host 已无法运行（停止条件 3）；
2. 按 §3 确认，按 §5.3 只读归因（日志、代码、已有记录）；
3. 触发 TASK-28 §3.5（按 §1.5 改过的条件）时重测基线构成 B 一次，记作 `m3-baseline-recheck.md`（在 Result 中说明）；
4. `m3-summary.md` 结论写「M-3 停止」，附：命中的规则与数值、确认过程、归因（标明哪些是推断）、四方对照、建议的下一步（更深的归因手段、可选的优化方向、对 A 封存的影响）。不提出、也不实施任何优化代码；
5. `follow-ups.md` 按 §6.3 的「M-3 停止时」一列处理；
6. Result 中写明停止，由主控方回到用户。

## 待裁定

1. **两次新增 G0 运行的构成与位置**。
   - **建议**：都用构成 B 默认级别，命名 `B-def-g0-2`、`B-def-g0-3`，分别紧接在 B-def-1、B-def-2 之后（§4.1 次序 2、9）；M-2 原有的 B-def-g0 留在最后（次序 18）。理由：M-2 未证明等价的是构成 B 的 (e)；与 G1 相邻运行，两者之间的环境漂移最小；M-2 的 16 次保持原次序，M-2 与 M-3 之间可以逐次对照。
2. **三次 G0 下等价结论的写法**。
   - **建议**：12 项（3 次 × 4 项）逐项检验，全部在区间内才写「等价」，否则写「未证明等价」并列出不在区间内的项；三次 G0 的范围只作说明，不改判据。(e) 的 G0 口径对照无论结论如何都写（§5.2）。理由：沿用已批准的单次判据，只是多做两次，不引入新的统计口径。
3. **就绪探测连接的识别条件 P1～P4 与 5 s 时窗**（§1.3）。
   - **建议**：接受。P1～P4 都能从 host.log 本身检验；锚点用 bootstrap 200 行，`run` 与 `ramp` 同样适用；回归核对要求新规则在 M-2 的 16 份 host.log 上逐行复现 `ATTRIBUTED`，反例 R1～R4 证明四条各自会拒绝。
   - 需要知道的后果：按 §4 第 4 条，探测连接上若出现非 `SocketException` 的 Warning `Failed`（如 M-2 的 `ClientNotConnectedException`），即 S5 命中、M-3 停止。读码判断这不会发生（§4.4 第 2 条）；即便发生，它是日志级别问题而不是性能问题，但规则不预先放宽，由主控方回到用户时判断。
4. **(d) 轮豁免的执行方式**（§1.3 D）：「断开后 1 s 内」改按「该轮最后一个 LOGIN 应答之后 1 s 内」的轮时间窗判定，并要求该连接只被发送端层的行提到、归类数不超过登录数。
   - **建议**：接受。理由：默认级别下服务端的连接号与工具的登录号对不上，逐连接的断开时刻取不到；轮时间窗在应答之前的那一段比原文宽，但 (d) 连接在 LOGIN 应答之前若发生发送失败，工具收不到应答，会记为错误或超时，由 S5 第 1 项抓住，所以不会因此放过真正的失败。反例 R5 证明窗外的行会被计入。
5. **AC-005 的测量一半、AC-021 与 F-02 的记法**。
   - **建议**：M-3 通过时 F-02 关闭（「已在 TASK-32 关闭」）；AC-005 的测量一半记「M-3 满足（修订后的 RQ-091 接受线，证据 `measurement/m3-summary.md`）」，AC-021 记「A 一半满足」，写在 `m3-summary.md` §1 与 F-02 一行，由 TASK-34 的 AC 状态表引用；不改 `spec.md`。AC-005 的另一半（口令哈希在执行器外、并发有上限）不由本单元判定。M-3 停止时三者都不改。理由：Plan R3 把这三项的关闭交给 T-009 的最终测量；AC 状态表的唯一归处是 TASK-34（TASK-30 §0.2），本单元只给出判定与证据，避免同一状态写在两处。
6. **未达目标值的项与「相对 M-2 显著变差」**（§1.4、§1.5）。
   - **建议**：F-77～F-81、F-120 沿用 M-2 的做法：追加 M-3 值，达到原目标值才关闭，否则保持未关闭、归属「后续性能优化（交主控方立项）」，由 TASK-34 列入 A 的遗留问题。另设 §1.5 的「相对 M-2 显著变差」单列（2 倍且超出差距），只记录、只读归因、不停止；基线重测的触发条件改为对照 M-2。理由：M-3 是 A 的最后一次测量，与上一次测量的回退应当被看见，但用户口径下它不构成停止。
7. **F-119、F-126 在 M-3 之后的状态**。
   - **建议**：
     - F-119：规则化与回归核对完成后改为「已在 TASK-32 关闭」。理由：它在 A 里剩下的动作（把豁免写成规则，`m2-summary.md` §9）由本单元完成。
     - F-126：M-3 中没有 F-126 形态的行，或有但全部落在 T、P、D 的不计范围内时，改为「已在 TASK-32 关闭」，并注明子 Spec B 的最终测量沿用同一判读；出现在其他位置时保持未关闭并回报。理由：F-126 的归属动作只是「M-3 判读、出现即回报」，判定行为本身按事实问、不改（`follow-ups.md` F-126）。

## 主控方裁定（执行前）

依据是用户口径：单条指令 ≤ 2 s 都算接受，只有数量级问题才回到用户（RQ-091 用户修订）。M-3 是硬停闸。

1. **新增两次 G0：都用构成 B 默认级别**，命名 `B-def-g0-2`、`B-def-g0-3`，排在次序 2、9。原有的 `B-def-g0` 留在次序 18。接受。
2. **等价结论**：12 项全部落在区间内才写「等价」，否则写「未证明等价」；(e) 的 G0 口径对照照写。接受。
3. **探测连接识别条件 P1～P4 与 5 s 时窗**：接受。规则不预先放宽：探测连接上若出现非 `SocketException` 的 Warning `Failed`，按 S5 命中处理。
4. **(d) 轮豁免改按轮时间窗判定**：接受。应答前的发送失败由 S5 第 1 项抓住，反例 R5 证明窗外的行会被计入。在判定日志中写明这一执行方式，以及它与 M-2 原文的差别。
5. **AC-005 测量一半、AC-021、F-02 的记法**：按计划建议，接受。M-3 通过时 F-02 关闭，AC-005 测量一半记「M-3 满足」，AC-021 记「A 一半满足」，最终状态由 TASK-34 的 AC 状态表引用；M-3 停止时三者都不改。
6. **未达目标项、「相对 M-2 显著变差」单列、基线重测触发条件改为对照 M-2 的 3 倍**：接受。显著变差只记录、只读归因，不停止。
7. **F-119、F-126 的去向**：按计划建议，接受。

## Result

执行轮次：第 1 轮（2026-10-23 UTC，即本地 2026-10-24；基准 `2bdb1afd`，未提交）。18 次运行在 UTC 19:44:52～21:40:38 依次完成，退出码都是 0，没有重跑，也没有条件运行。拥有的改动见 Changed 与 `logs/task-32-boundary.txt`。

- **Outcome**：done。结论 **M-3 通过**：S1～S5 在 18 次运行中都不命中，干净或受污染的样本中都没有越线（`measurement/m3-summary.md` §1）。
  - **停止线逐条**：
    - S1 不命中：单条作业执行时长 max 269.3 ms（A-dbg-1，`inbound:GameSelectCharacter`，冷启动窗 t＝19.04 s；269.3 ÷ 2000 ＝ 0.13），**p99.9**（全部作业类型合计）构成 A 26.76／27.53 ms（窗口 256 26.91 ms）、构成 B 0.29／0.28／0.32 ms、ramp-A-2s 94.99 ms；内层命令 max 134.2 ms；ramp-A-2s 作业 max 233.0 ms。
    - S2 不命中：(f) 最长停顿 max 1231.1 ms（A-def-f256；1231.1 ÷ 2000 ＝ 0.62，÷ 250 ＝ 4.92）；入队等待 max 872.5 ms；三个窗口两序列都 ≥ 2 s 的样本 0。
    - S3 不命中：ramp-A-2s 1163 包/秒；ramp-B 16 档全过、各档 max ≤ 66.61 ms、错误 0。
    - S4（只记录）：(d) 入队等待 p99 构成 A 136.9／103.3 ms（窗口 256 103.8 ms），构成 B 17.32／2.42 ms（窗口 256 53.83 ms）。
    - S5 不命中：错误、失败命令、失败作业、15 项信号、`OverLimit`、`Game handler failed` 都是 0；发送端层的行 126 行归为 T 106、P 20、D 0、计入 0（106 ＋ 20 ＋ 0 ＋ 0 ＝ 126）；F-126 形态 0。
  - **相对 M-2 显著变差**：0 项（17 个量 × 2 种构成 ＝ 34 项检验；比值最大 1.15 倍，(e) 入队等待 max）。基线重测未触发（构成 B 默认级别稳态端到端 p99 19.22／19.84 ms，3 × M-2 为 61.56～78.20 ms）。
  - **控制运行**：**未证明等价**，12 项里 2 项在区间内；三次 G0 落在两次 G1 的两侧，两次 G1 的差距只有 0.45～1.66 ms。(e) 的 G0 口径为 9.84／14.84／8.47 ms，中位数 9.84 ms，约是 M-1 的 2 倍（9.84 ÷ 4.94 ＝ 1.99，9.84 ÷ 4.42 ＝ 2.23），三次都低于 M-2 那一次的 17.84 ms。§4.4 第 5 条的两种情形都不是。
  - **(a)～(f) 主要数字**（M-3 → 对照 M-2；基线、M-1 与目标见 `m3-summary.md` §5、§8）：
    - (a) 见 S1。选角作业构成 A p99 128.8～140.8 ms（M-2 130.0～142.6 ms），max 234.0～269.3 ms（M-2 226.8～239.1 ms）；目标 p99.9 ≤ 100 ms 未达；**max ≤ 250 ms 在 A-dbg-1 一次未达**（M-2 达到）。
    - (b) 2 s 口径构成 A 1163 包/秒（M-2 1169、M-1 1188），稳定通过档 800（800 档滞后 77.1～136.0 ms 无上升，占用 63.4%；1200 档 658.4 → 1234.4 ms 上升，占用 91.3%）；p99 ≤ 50 ms 口径无通过档（400 档 106.4 ms）；构成 B ≥ 25000；目标 2000 未达。
    - (c) 稳态入队等待 p99 构成 A 93.98／89.78 ms（M-2 94.31／99.75），构成 B 20.97／14.78 ms（M-2 16.56／19.01）；稳态端到端 p99 默认级别构成 A 99.03／99.84 ms（M-2 103.9／97.49），构成 B 19.22／19.84 ms（M-2 26.07／20.52；基线 2.73～2.79）。
    - (d) 背景请求端到端 p99 构成 A 160.9／144.4 ms（M-2 138.8／205.8），构成 B 91.26／92.92 ms（M-2 84.27／100.1）。
    - (e) 窗口 32 其余连接端到端 p99 构成 A 96.13／111.6 ms（M-2 112.8／106.6），构成 B 9.96／10.41 ms（M-2 9.77／11.20；M-1 4.94／4.42；基线 2.71）；入队等待 max 构成 A 182.2／189.9 ms、构成 B 39.80／39.90 ms；窗口 256 其余连接 p99 构成 A 116.7（默认）／109.2（debug）ms、构成 B 8.30／7.63 ms。
    - (f) 构成 A 1150.5／954.3 ms（默认）、944.8／888.4 ms（debug）、1231.1／801.7 ms（窗口 256），M-2 887.8～1367.6 ms；构成 B 231.9～236.1 ms（M-2 229.2～247.4 ms）。
  - **受污染样本**：3 处，全是 P3 换页尖峰（B-def-g0-2 稳态窗、B-def-2 稳态窗、ramp-B 整段，1 ＋ 1 ＋ 1 ＝ 3）；没有越线，所以不重跑，数值照常列出、判定表标「受污染」。另有一个每 10 分钟 1～2 s 的外部 CPU 尖峰（`Microsoft*` 进程 258%～488%），不满 P1 的 3 s，A-dbg-1 的 269.3 ms 作业落在其中一秒（推断）。
  - **AC 与 F 编号**（裁定第 5、7 条）：AC-005 的测量一半记「M-3 满足」，AC-021 记「A 一半满足」，最终状态由 TASK-34 的 AC 状态表引用 `m3-summary.md` §1；F-02、F-119、F-126 已在 TASK-32 关闭；F-77～F-82、F-120 追加 M-3 值，仍未关闭；F-121 追加真实进程的级别证据（状态不变）；F-83 不改；新增 F-127。
- **Changed**：
  - 新建 `verification/measurement/m3-summary.md`；18 份运行记录 `m3-{A,B}-{1,2}.md`、`m3-{A,B}-debug-{1,2}.md`、`m3-{A,B}-f256.md`、`m3-{A,B}-f256-debug.md`、`m3-B-g0.md`、`m3-B-g0-2.md`、`m3-B-g0-3.md`、`m3-ramp-full-2s.md`、`m3-ramp-full.md`、`m3-ramp-clean.md`（4 ＋ 4 ＋ 4 ＋ 3 ＋ 3 ＝ 18，工具生成的原样保存）；`raw/m3-*.summary.json` 15 份与 `raw/m3-tool-version.txt`。
  - 修改 `verification/measurement/README.md`（文件表加 7 行；失败行模板的 `connection` 字段；「工具版本」写三次控制运行的结论；新增「发送端层的告警」一节）与 `verification/follow-ups.md`（文件头加 TASK-32 一条；F-02、F-119、F-126 关闭；F-77～F-82、F-120、F-121 追加；F-120 的「G0 只跑了 1 次」一句按三次 G0 改写；新增 F-127）。
  - 日志 `verification/logs/task-32-{build,tool,env,runs,judgement,boundary}.txt`。
  - scratchpad `t32/`：脚本 `matrix.sh`、`ts.py`、`sampler.py`、`sampsum.py`、`teardown.py`（后四个与 `t28/` 同口径）、`signals.py`、`classify.py`（新）、`regress.py`（新）、`rules_selftest.py`（新）、`judge.py`、`composition.py`、`coldmax.py`、`jobtypes.py`、`floodjob.py`、`fill.py`、`refs.py`、`post.sh`、`logs.sh`、`judgelog.sh`、`toollog.sh`、`buildlog.sh`、`boundary.sh`、`prebuild.sh`、`prelog.sh`、`followups_edit.py`、`readme_edit.py`；运行输出 `m3/`（5.1 GB）、`out/`、`samples/`、`signals/`、`classify/`、`env/`、`regress/`、`selftest/`。`t28/m2/` 只读未改。
  - 未改：`Server/`、`Tool/`（源码与 dll）、`spec.md`、`plan.md`、`t009-*.md`、各份 closure、`baseline*`／`m1-*`／`m2-*` 记录与 `raw/`、`AGENTS.md`、`Docs/`、`protocol/`。
- **Why**：
  - **探测连接的 P1 取「本次 host.log 中出现的全部 `Game:` 编号里最小的」**，构成 A 中探测连接一行都没有，最小编号是 `Game:02-00-00-00`（第一条 agent），它被 handler 行提到，P2 不成立，不会误归 P。
  - **P 的级别分支里，Warning 的 `closed: Failed` 行自己不带异常**，取同一连接上它之前那条 Warning `entry … Failed` 的异常；找不到即按「不是 `SocketException`」处理（S5 命中，更严）。
  - **D 要求一条连接的全部行落在同一轮的时间窗内**（计划写「某一轮」），并把「D 连接数 ≤ (d) 登录数」实现为超出即全部不归 D。两处都只会让更少的行被豁免。本次 D 类 0 行。
  - **`OverLimit` 放在 T 之前判定**：§4 第 2 条要求任何时刻都计入；反例 R7 钉住这一点。
  - **控制运行未证明等价，照判据写**，不因三次 G0 分落两侧而放宽；范围与 G0 彼此之间的差距只作说明（裁定第 2 条）。
  - **F-127 新登记，而不是并入 F-121 或 F-126**：收尾窗口内的 72 行 Warning 异常是 `InvalidOperationException`（写端已完成），既不是 F-121 降级的「连接已不在」，也不是 F-126 的 `SocketException`；按规则属 T、不计，只是 §4.4 第 3 条的预期不成立，是否处理归主控方。用新脚本重读 M-2 的 host.log，M-2 收尾的 36 行 Warning 也是这一种。
  - **判定日志头部的时刻**：头部规则文本先写为「19:45Z」，在第一次运行之前改正为实际的「19:43Z」并重写了运行前部分（19:44:45Z）；运行前部分此后原样保留在 `logs/task-32-judgement.txt` 开头（逐字节比对相同）。
- **Checks**：
  1. 冻结核对与被测构建：开始时 HEAD `2bdb1afd…`，`git diff 2bdb1afd -- Server Tool` 与 `git status --porcelain -- Server Tool` 为空，工作区只有本任务文件；`dotnet build … -c Release --no-incremental` 0 警告 0 错误；`Dfo.Server.Host.dll` `79dbbf54…`；`dotnet --version` 10.0.302；没有孤儿 Host；不跑测试套件（`logs/task-32-build.txt`）。
  2. 工具：8 个源文件哈希与 `raw/m2-tool-version.txt` 逐个相同，`git diff b1959f3e -- Tool/GameplayLoad` 为空，G1、G0 哈希与合同相符，未重建；6 次 debug `run` 的解析自检全部相等，数值解析失败 0（`logs/task-32-tool.txt`、`raw/m3-tool-version.txt`）。
  3. 环境与噪声：18 次静默检查全部通过（等待 0～240 s）；每秒采样汇总、逐窗口干净／污染判定与说明见 `logs/task-32-env.txt`。
  4. 运行记录：每次的命令行、dll 与哈希、起止、退出码、`hostLogLines`、收尾窗口起点、就绪锚点（bootstrap 200 的行号与时刻）、(d) 各轮时间窗、日志信号与每条发送端层的行的归类（`logs/task-32-runs.txt`）。
  5. 判定：`logs/task-32-judgement.txt` 依次是运行前写入的规则、§4 五条原文（脚本比对与源文逐字相同）及两处出处、§4.4 预期；回归核对（16 份 M-2 host.log：P 16 行与 `ATTRIBUTED` 逐行相同，计入 0，T 24 ＋ 12 ＝ 36，PASS）与规则自检（R1～R7 计入、三类正例不计、两项级别分支检验，20／20 PASS），都在第一次运行之前完成；`judge.py` 的逐条输出（发送端层的每一行带归类与依据），最后一行「M-3 通过」；延迟构成、冷启动请求、作业类型、(e) 占用的输出与归因说明。`m3-summary.md` 的表由 `fill.py` 填入，占位缺失 0。
  6. 记录引用核对：`refs.py` 检查 41 个路径、16 个 F 编号、17 项状态与归属及文本，0 项未解决（`logs/task-32-judgement.txt` 末尾）。
  7. 边界：结束时 `git diff 2bdb1afd -- Server Tool` 与 `git status --porcelain -- Server Tool` 为空；`git status --porcelain` 只含 Owned boundary 的路径与本任务文件，另有一个不属于本单元的 `tasks/TASK-33.md`（矩阵期间由其他写入方创建，本单元未动）；`git -c core.whitespace=cr-at-eol diff --check` 无输出；新文件行尾空白与 CR 都是 0；没有残留的 Host、testhost、GameplayLoad 与采样进程；已执行 `dotnet build-server shutdown`（`logs/task-32-boundary.txt`）。未 commit，未用任何形式的 `git stash`，未 push。
- **Remaining**：
  - Result review 为 required，由主控方派发独立复核。
  - F-77～F-82、F-120 归后续性能优化，由 TASK-34 列入 A 的遗留问题；F-81 的 max 目标在 M-3 一次未达（269.3 ms），写在 F-81 一行。
  - F-127 交主控方裁定（收尾时写端已完成形态的 Warning `Failed` 是否也按「连接已不在」处理）。
  - 子 Spec B 的最终测量沿用本单元的命令行、dll 与 `classify.py` 的规则（`m3-summary.md` §9）。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-32-result-review.md`），「M-3 通过」成立。提交前处理如下：
- **L1**：Result 里凡说判定日志运行前部分「逐字节相同」之处，一律以本条为准，改读为「除末行时刻行在运行后补写说明外，逐字节相同」。三份输入的修改时刻都早于第一次运行，实质内容未改。
- **L2**：F-119 中「R1～R4 各自被拒」改为准确说法，并写明子 Spec B 复用规则前要补一例只违反 P2 的反例。
- **L3**：F-127 补记「与 F-126 同一机制」。去向：A 的代码已冻结，交子 Spec B 在首个单元顺带修，并列入 TASK-34 的遗留问题。
- **L4**：只作信息，不改。
