# TASK-20 — 测量闸 M-1：T-004 之后按 RQ-090 预测量，按修订后的 RQ-091 判定

基准提交 `5f7dba11`（TASK-19 与 Plan T-004 行修正之后；`git diff 0d8d4fad 5f7dba11 -- Server/src` 为空，被测服务端就是 TASK-19 结束时的代码）。范围取自 `plan.md`「测量闸 M-1」行与 T-005 的依赖；判定口径以 `spec.md` RQ-091 的「用户修订（2026-10-22，执行中）」（提交 `c3115f9a`）为准。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **M-1 测量**（Plan「测量闸 M-1」行；RQ-090；SC-9）：在当前 HEAD 的 Release Host 上，用 TASK-03 的 `Tool/GameplayLoad` 与同一组命令行，测出：
     - (a) 各类 CMD 的执行时长 p50／p95／p99／p99.9／max，分两层：执行器作业（协议级 CMD，过渡态下即 `inbound:*`、`timer:*`、`event:*`、`http:*` 等一个作业）与 `CommandLoggingBehavior` 记录的内层命令；
     - (b) 可持续吞吐（爬坡，构成 A 与 B）；
     - (d) 10 个并发登录轮内的入队等待与端到端延迟；
     - (f) 冷启动前 60 s 的最长单次停顿与入队等待；
     - (c) 稳态入队等待 p99、(e) 灌包期间的入队等待与其他连接端到端延迟，**只作过渡态参考**（Plan：此时仍混着直接发送的开销）。
  2. **判定**：按 §1 的判定规则给出「M-1 通过」或「M-1 停止（命中 Sx）」。M-1 是硬停闸：停止时由主控方回到用户，本单元不做任何性能优化。
  3. **与基线对照**：与 `verification/measurement/baseline-summary.md`（TASK-03，基线 `a02bce46`，同一工具 dll）逐项对照，包括其中「M-1 预警」表的 8 行。基线记录沿用、不重测，重测的触发条件见 §3.5。
  4. **工具补一项解析**（`verification/measurement/README.md`「入队等待」节写明的前置条件）：解析执行器 Debug 级的作业完成／失败日志，得到每个作业的执行时长与入队等待，按窗口聚合。只做加法，不改既有口径（§2）。
  5. **记录**：`verification/measurement/m1-*.md`、`raw/m1-*`、`verification/logs/task-20-*.txt`；`measurement/README.md` 的文件表与入队等待一节更新。
  6. **`follow-ups.md`**：F-02～F-10（`t004-closure.md` §7 交 M-1 的 9 项）逐条更新状态与去向；未达原目标值的项、合成负载覆盖不到的项登记为新编号（从 F-77 起），归「后续性能优化（交主控方立项）」。
  7. **AC 关联**：AC-005 的测量一半、AC-021 的过渡态证据（是否据此关闭见待裁定第 4 条）。
- Owned boundary：
  - **工具**（只限三个文件）：`Tool/GameplayLoad/Program.cs`（新选项与文件头用法注释）、`Tool/GameplayLoad/Scenarios.cs`（`RunOptions` 新字段、聚合与 `summary.md`／`summary.json` 的新增段落）、`Tool/GameplayLoad/HostProcess.cs`（新正则、新记录类型、按选项追加一个日志级别环境变量）。
  - **文档与证据**：
    - 新建 `verification/measurement/m1-summary.md`（先读这份）、`m1-{A,B}-{1,2}.md`、`m1-{A,B}-debug-{1,2}.md`、`m1-ramp-full.md`、`m1-ramp-clean.md`（工具生成的 `summary.md` 原样保存）；
    - `verification/measurement/raw/m1-*.summary.json`、`raw/m1-tool-version.txt`；
    - 修改 `verification/measurement/README.md`（文件表、新选项、「入队等待」节）；
    - 修改 `verification/follow-ups.md`；
    - `verification/logs/task-20-{env,build,tool,runs,judgement,boundary}.txt`。
  - **scratchpad**：运行输出目录（`samples.csv`、`host.log`）、环境采样脚本、判定脚本。两个脚本的全文附在对应日志末尾（与 TASK-18、TASK-19 的做法相同）。
  - **工具二进制**：`Tool/GameplayLoad/bin/` 下的两个目录（被 `.gitignore` 的 `[Bb]in/` 覆盖，不入库）：`bin/m1-base/`（改动前 dll 的原样副本）与默认的 `bin/Release/net10.0/`（改动后重新构建）。
  - **不改**：`Server/` 下的一切（包括临时探针）；`spec.md`、`plan.md`、`t004-closure.md`、`baseline*.md`、`task-15-coldstart.md` 等既有测量记录；AGENTS.md 与 `Docs/`。
- Required checks：
  1. **被测构建**（`logs/task-20-build.txt`）：
     - `git rev-parse HEAD` 为 `5f7dba11…`，`git status --porcelain -- Server` 为空；
     - `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误；
     - 记录 `Dfo.Server.Host.dll` 的 SHA-256 与修改时间、`dotnet --version`。
     - 不跑测试套件：本单元不改产品代码，测试套件的耗时与内存也不作判定依据（RQ-091 用户修订）。
  2. **工具**（`logs/task-20-tool.txt`）：
     - 改动前把 `Tool/GameplayLoad/bin/Release/net10.0/` 整个复制到 `bin/m1-base/`，`GameplayLoad.dll` 的 SHA-256 必须是 `b3d6c286…53be899753`（与 `raw/tool-version.txt` 相同）；
     - 改动后 `dotnet build Tool/GameplayLoad -c Release`，0 个警告，0 个错误（项目开着 `TreatWarningsAsErrors`）；
     - `git diff -- Tool/GameplayLoad` 全文，逐段说明「只做加法」：既有正则、窗口、口径、表格、json 键的代码不变，`OnLine` 的控制流改动单独说明；
     - **解析自检**：每次 debug 运行后，工具解析出的执行器记录数等于 `grep -cE 'Gameplay .+ (completed|failed):' host.log`；数值字段解析失败数为 0；
     - 新旧源码与两个 dll 的 SHA-256 写进 `raw/m1-tool-version.txt`。
  3. **环境与噪声**（`logs/task-20-env.txt`，§3）：每次运行前的静默检查结果、运行期间每秒采样的汇总、每次运行的「干净／受污染」判定及依据。
  4. **运行记录**（`logs/task-20-runs.txt`）：每次运行的完整命令行、使用的工具 dll 及其 SHA-256、开始与结束时刻（UTC）、退出码、`hostLogLines`、输出目录、运行后从 `host.log` 检索的日志信号计数（§5.6）。
  5. **判定**（`logs/task-20-judgement.txt`）：判定脚本读取各次 `summary.json`、`samples.csv`、`host.log` 计数与采样文件，按 §1 逐条输出「规则 → 各次运行的值 → 是否越线 → 样本是否干净」，最后一行是结论。`m1-summary.md` 中的数字表一律由这个脚本的输出生成，不手抄。
  6. **记录引用核对**：`m1-summary.md` 中出现的每个文件路径存在；出现的 F 编号在 `follow-ups.md` 中存在且状态与表述一致（沿用 TASK-19 的引用核对脚本，口径同 TASK-19 第 2 轮：归属写了动作的条目不得是「信息」）。结果附在 `logs/task-20-judgement.txt` 末尾。
  7. **边界**（`logs/task-20-boundary.txt`）：`git status --porcelain` 只含 Owned boundary 列出的路径；`git diff --stat -- Server` 为空；`git -c core.whitespace=cr-at-eol diff --check` 无输出。
- Dependencies：TASK-19（`0d8d4fad`）与 Plan 修正（`5f7dba11`），即 T-004 全部完成；TASK-03（测量工具与基线记录）；TASK-15（预热与留给 M-1 的 C 类、D 类清单，`verification/measurement/task-15-coldstart.md`）。
- Result review：required。
  - M-1 是 T-005 的进入条件之一，也是整条路线上第一个硬停闸。判定若把一次越线归为「受污染」而放过，或把一次被外部负载污染的样本判成越线，前者让路线在错误的吞吐假设上继续删锁和归并，后者无谓地把实施停下来回到用户。两种错都不会让任何检查变红。
  - 产出以判断为主：干净／污染的分类、(c)(e) 越线时的归因、F 项的去向，都只能靠独立复核。
  - 工具改动会影响之后 M-2、T-009 的全部数字；「只做加法」需要有人对着 diff 核一遍。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. §1 的停止线 S1～S5 在干净样本中被越过（确认程序见 §3.4）。这是本单元的预期产出之一，不是执行失败：照常完成其余运行与记录，结论写「M-1 停止」。
    2. 在 3 次尝试内取不到某一类运行的干净样本（§3.4），且该类运行的值落在停止线附近、无法判定。
    3. 被测 Host 无法在负载下完成一次运行（启动失败、进程崩溃、工具因 Host 行为异常退出）。
    4. 需要改 `Server/` 下的任何文件（包括为测量加探针或日志）、改工具的既有口径，或改 Spec 验收与已定口径。
  - 归因只允许读日志、读代码、读已有记录，不做性能剖析与代码试改；需要更深的归因时，在回报中写明建议的手段，由主控方决定。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. 定位与执行顺序

**M-1 回答一个问题**：T-004 把全部入口迁上单执行器之后，有没有出现数量级的性能问题，使后面的删锁与归并建立在错误的假设上。它不追求基线，也不追求原 RQ-091 阈值。用户立场（RQ-091 用户修订原文）：「不需要太追求基线……以完整重构为主目标，性能能接受就行。后续我们再专门优化就好了」；「只要指令执行时间不超过2s就都能算接受」。

**执行顺序**：
1. 被测构建（Required check 1）；
2. 归档旧工具 dll，改工具，构建，做一次短的自检运行（§2.4）；
3. 环境采样脚本与判定脚本写好，先把 §1 的规则和 §4 的预期写进 `logs/task-20-judgement.txt` 开头，再开始正式运行；
4. 按 §4 的运行矩阵依次运行，每次之前做静默检查（§3.2）；
5. 判定脚本出结论，必要时按 §3.4 重跑；
6. 写 `m1-summary.md`、更新 README 与 `follow-ups.md`；
7. 引用核对与边界核对。

### 1. 判定规则

**来源**：RQ-091 用户修订：「①单条 CMD 的执行时长（max）≤ 2 秒，即为接受；②……原阈值，改作记录与对照用的目标值，不再阻断实施。只有在出现数量级问题时才停止实施并回到用户，例如单条 CMD 超过 2 秒，或吞吐、等待比目标差了几倍。」下面把「几倍」与「秒级停顿」落成数（待裁定第 1 条）。

**停止线**（任一条在干净样本中成立，M-1 即停止）：

| 编号 | 规则 | 数据来源 |
|---|---|---|
| S1 | **单条 CMD**：Host 就绪之后，任何执行器作业的执行时长 > 2000 ms（全部来源：入站、定时、事件、HTTP、生命周期），或任何 `CommandLoggingBehavior` 内层命令 > 2000 ms。启动前的工作不计（RQ-091 原文「启动前的工作不计」） | debug 运行的执行器记录；全部运行的命令日志 |
| S2 | **秒级停顿**：(f) 冷启动前 60 s 的最长单次停顿 ≥ 2000 ms；或任一窗口的入队等待 max ≥ 2000 ms；或稳态、(d) 窗口内有端到端样本在判定序列与参考序列（实际发出 → 第一条回包）上都 ≥ 2000 ms。只在判定序列上越线、参考序列不越线的，是工具自身迟到，按 §3.3 处理 | `summary.json`、`samples.csv`、执行器记录 |
| S3 | **吞吐**：(b) 可持续吞吐 < 667 包/秒，即低于目标 2000 的三分之一。构成 A、B 各自判定，任一越线即命中 | 爬坡记录 |
| S4 | **(d) 等待**：10 并发登录轮内，执行器作业入队等待 p99 > 150 ms（目标 50 ms 的 3 倍） | debug 运行的执行器记录 |
| S5 | **正确性**：任一次运行出现构成 B 基线没有的错误（断线、超时），或出现不能归因为协议预期拒绝的失败命令／失败作业；或 `host.log` 中出现 `direct send deadline exceeded`、`GameplaySelfWait`、`must happen inside a gameplay job`，或停机阶段以外的 `GameplayExecutorStoppedException` | `summary.json` 的 `errors`、`failedCommands`；`host.log` 检索 |

S5 不是性能规则：M-1 是 T-004 之后第一次在真实进程上跑持续负载，这里出现的行为异常同样要回到主控方，不能在测量记录里一笔带过。

**过渡态参考**：(c) 稳态、(e) 灌包期间的入队等待 p99 > 150 ms，不单独触发停止；在 `m1-summary.md` 单列，并按 §5.3 归因。归因不是「作业上的直接发送」（即 T-006 换成发件箱后不会消失）时，按停止处理（待裁定第 3 条）。

**目标值（只记录、作对照）**：(b) ≥ 2000 CMD/秒；(c)(d)(e) 入队等待 p99 ≤ 50 ms；玩法 CMD 单条 p99.9 ≤ 100 ms、max ≤ 250 ms；(f) 最长单次停顿 ≤ 250 ms。每个未达项在 `m1-summary.md` 的「后续性能优化事项」表中列一行（量、值、目标、出处、已知或推断的成因），并在 `follow-ups.md` 登记（§6.3）。

**「单条 CMD」的对象**（待裁定第 2 条）：过渡态下，一个协议级 CMD 就是执行器上的一个作业，它的执行时长取执行器日志里的 `in {ElapsedMilliseconds} ms`（作业体开始到结束，不含入队等待）。作业内的直接发送算在里面，这与 T-006 之后不同，记录中注明。内层命令是作业的一部分，单列只为与基线的命令级数字对照。

### 2. 工具改动：入队等待与作业时长

#### 2.1 为什么要改、为什么这样改

- 执行器已经在 Debug 级记下每个作业的执行时长与入队等待（`Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs` 的 `Job<T>.RunAsync`：`Gameplay {CommandType} completed: … in {ElapsedMilliseconds} ms after {QueueWaitMilliseconds} ms queued`；失败行是 Error 级，`… after {ElapsedMilliseconds} ms, {QueueWaitMilliseconds} ms queued`）。日志类别是 `Dfo.Server.Application.Execution.GameplayExecutor`，默认 Information 下不输出完成行。
- 工具今天只解析 `CommandLoggingBehavior`，而 0x04DD、0x0023、0x0024 这类入站包没有内层命令，只有作业层能给出它们的执行时长；(c)(d)(e)(f) 的入队等待也只在作业层。
- **不采用「事后解析 `host.log`」**：`host.log` 的时间戳是墙上时钟，工具的窗口用的是 `Stopwatch`，按 (d) 每轮约 1 秒的窗口对齐，误差不可控。在工具里按与命令日志相同的「读到行的时刻」归窗，口径一致。

#### 2.2 改法（只做加法）

- `Program.cs`：新选项 `--executor-log <off|debug>`，缺省 `off`；文件头用法注释加一行。
- `Scenarios.cs`：`RunOptions` 加字段 `ExecutorLog`；`run` 的 `summary.md` 末尾追加一节「执行器作业（`--executor-log debug` 时）」，`summary.json` 追加键。既有段落、窗口定义与 json 键一个不动。
- `HostProcess.cs`：
  - `--executor-log debug` 时，环境变量多一项 `Logging__LogLevel__Dfo.Server.Application.Execution.GameplayExecutor=Debug`；
  - 新正则：`Gameplay (?<type>.+?) (?:completed|failed): .*?(?:in|after) (?<ms>[0-9.Ee+-]+) ms(?: after|,) (?<q>[0-9.Ee+-]+) ms queued`。`type` 用非贪婪匹配，因为 HTTP 作业名带空格（`http:POST /api/launcher/v1/register`）；数值允许科学计数法。这是起点写法：执行时对照 §2.4 自检运行的真实行核对（失败行的 `chain {Chain}` 与单行格式下附在行尾的异常文本都不能干扰匹配），以 Required check 2 的计数相等为准；
  - 新记录 `ExecutorRecord(ReadTimestamp, CommandType, ElapsedMs, QueueWaitMs, Failed)`，与 `CommandRecord` 用同一个读行时刻；
  - `OnLine` 现在在命令正则不匹配时提前返回，改为两个正则依次尝试。这是唯一触及既有控制流的地方，在 `logs/task-20-tool.txt` 中单独说明。
- `summary.json` 另加 `originUtc`（就绪时刻的 UTC），供环境采样按秒对齐（§3.3）。

#### 2.3 新增的聚合（全部按 Host 就绪之后、读行时刻归窗）

| 键 | 内容 |
|---|---|
| `executorLines` | 解析到的完成行与失败行数（含就绪之前，供 Required check 2 自检）；就绪之后的数另列 |
| `executorJobs` | 按作业类型：执行时长分布（样本、p50、p95、p99、p99.9、max）、首次出现的值、冷启动窗 max 与之后的 max、失败数 |
| `queueWait` | 冷启动窗、稳态窗、(d) 轮内、(e) 灌包窗四个窗口，各给「全部作业」与「只算 `inbound:`」两个分布 |
| `queueWaitColdPerSecond` | 冷启动窗逐秒的入队等待 p99 与 max |
| `jobRatePerSecond` | 稳态窗内按来源前缀（`inbound:`、`timer:`、`event:`、`http:`、其他）的每秒作业数（F-08、F-10） |

- **(e) 的局限**：日志行不带连接号，灌包连接自己的作业分不出来。按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，所以 (e) 窗的「全部作业」分布可作代理；在记录中写明。
- `summary.md` 的新节写明本次 `--executor-log` 的取值与解析到的行数。

#### 2.4 自检运行

改完后跑一次短的 debug 运行（`--cold 10 --steady 20 --bursts 1 --flood 5`，构成 B），只用于 Required check 2 的解析自检与新节格式检查，不进入判定，输出不入库。

#### 2.5 与「同一套测量工具」的关系

- **默认日志级别的运行（§4 的 def 组）一律用 `bin/m1-base/` 里的原 dll**（SHA-256 `b3d6c286…`），与 TASK-03 基线逐字节同一个工具，可比性不靠「只做加法」的论证。
- **debug 组用改动后的 dll**；它的作业层数字基线上本来就不存在（README「入队等待」：基线没有执行器）。
- `raw/m1-tool-version.txt` 写明两组各用哪个 dll。`bin/m1-base/` 不入库；它可以按 `raw/tool-version.txt` 的源码 SHA 从提交 `5075a283` 确定性重建，README 中写明这一点，供 M-2 复用。

### 3. 测量环境与噪声控制

#### 3.1 环境取证（每次运行前后各一次，写进 `logs/task-20-env.txt`）

- 机器与系统：`sysctl -n machdep.cpu.brand_string hw.ncpu hw.memsize`、`sw_vers`、`uptime`；
- 内存与交换：`sysctl vm.swapusage`、`vm_stat`（记 pageins、pageouts、swapins、swapouts 的累计值）、`memory_pressure | tail -1`；
- 电源与温度：`pmset -g batt`（是否接电源）、`pmset -g therm`；
- 磁盘：数据库所在卷的 `df -h` 与可用空间（每次运行 `samples.csv` 约 350 MB，debug 组的 `host.log` 可能数百 MB）；
- 构建配置与 SQLite 设置：由工具的 `EnvironmentProbe` 写进每份记录（Release、`journal_mode`、`synchronous`、SQLite 版本与 dylib 哈希），不另取；
- GC：Host 用默认 GC 设置（csproj 未设），运行环境中没有 `DOTNET_gcServer` 等变量，记 `env | grep -i '^DOTNET_'` 的输出；
- 其他进程：`ps -Ao pid,pcpu,rss,comm -r | head -15`。

**已知背景**：规划时（2026-10-23）swap 已用 7.6 GB／9 GB；TASK-15 测量时约 6.8 GB／8 GB，TASK-19 全量时 8.0 GB。swap 偏高是这台机器的常态，不作为拒绝运行的理由，但它是 §3.3 中「换页」一类污染的来源。

#### 3.2 每次运行前的静默检查

1. `dotnet build-server shutdown`，结束构建服务节点；
2. 检查没有其他负载进程：`pgrep -fl 'testhost|vstest|dotnet test|Dfo.Server.Host|GameplayLoad'` 为空（其他工作区或会话可能正在跑全量测试）；
3. 1 分钟 loadavg < 3（10 核）；
4. 不满足时每 30 s 复查，最多等 10 分钟；仍不满足就照常运行，但把这次运行整体标为「环境未静」，按受污染处理。

检查结果逐次写进 `logs/task-20-env.txt`。

#### 3.3 运行期间的采样与污染判定

**采样脚本**（scratchpad，后台运行，每秒一行 TSV，运行结束后停止）：UTC 时刻、1 分钟 loadavg、`vm_stat` 的 pageins／pageouts 增量、swap 已用、Host 与工具两个进程的 %CPU 与 RSS、其他进程 %CPU 之和与最高的 3 个进程名。时刻按 `summary.json` 的 `originUtc` 换算成相对就绪的秒数，与工具的窗口对齐。

**一次运行或一个窗口判为受污染**，满足任一条：
- **P1 外部 CPU**：窗口内有连续 ≥ 3 秒，Host 与工具之外的进程 %CPU 之和 > 200%（两个核）；
- **P2 工具迟到**：窗口的发出滞后 p99 > 5 ms（基线 B 为 2.25～2.34 ms，`baseline-summary.md`）；
- **P3 换页**：窗口内 pageins 或 pageouts 的每秒增量出现明显尖峰（> 该次运行中位数的 10 倍且 > 1000 页/秒）；
- **P4 环境未静**（§3.2）。

**单个慢样本的归因**（S1、S2 越线时必做）：取该样本或该作业发生的那一秒及前后各 1 秒的采样行。若同时有 P1 或 P3 尖峰，这个样本判为受污染；若判定序列越线而参考序列没有，判为工具迟到（P2）。归因写进 `logs/task-20-judgement.txt`。

#### 3.4 重复与确认

- 运行矩阵中每类运行各 2 次（与基线 B 的两次相同），两次都记录，不取平均（README：(d)(f) 由单个慢事件决定，只能看量级和成因）。
- **越线的确认**：
  - 越线出现在干净样本中 → 命中停止线；
  - 越线只出现在受污染样本中 → 同一类运行重跑，最多再 2 次；重跑在干净样本中复现 → 命中；不复现 → 不命中，原样本与重跑都写进记录；
  - 3 次都取不到干净样本，且值落在停止线附近（停止线的 0.5～2 倍之间）→ 停止条件 2。
- **未越线时**不因污染重跑：污染只会让数字变坏，未越线的结论不受影响。受污染的未越线样本在记录中标注即可。

#### 3.5 基线记录沿用，不重测

- 依据：RQ-090 要求「用同一套测量工具，分别在基线和改造后测量」。基线测量已在 TASK-03 完成（同一台机器、同一个工具 dll、同一组命令行，记录在 `baseline-summary.md`）；M-1 的 def 组用同一个 dll，口径逐字节相同。
- RQ-091 修订后的判定是绝对线（2 s、目标的几倍），不依赖与基线的差值，基线只作对照。
- **触发重测基线的条件**：S1～S4 命中，且需要回答「是改造带来的还是环境变了」；或 def 组 B 的稳态端到端 p99 超过基线（2.73～2.79 ms）的 3 倍，而同次运行的作业执行时长与入队等待都很小（说明差在环境或工具侧）。触发时，用 `git archive a02bce46 Server` 导出到工作区外构建（TASK-15 P0 的做法），按构成 B 跑 1 次，记作 `m1-baseline-recheck.md`。

### 4. 运行矩阵与命令行

```bash
G0=Tool/GameplayLoad/bin/m1-base/GameplayLoad.dll          # 原 dll（def 组、爬坡）
G1=Tool/GameplayLoad/bin/Release/net10.0/GameplayLoad.dll  # 改动后（debug 组）
B="--weights 0.5,0.5,0,0,0 --entry-concurrency 1"          # 构成 B
O=<scratchpad>/m1
```

按下面的次序交错运行，把环境漂移分散到各组：

| 次序 | 名称 | 命令 | 记录 |
|---|---|---|---|
| 1 | B-def-1 | `dotnet $G0 run --out $O/B-1 --tag m1b1 --seed 1 $B` | `m1-B-1.md` |
| 2 | A-def-1 | `dotnet $G0 run --out $O/A-1 --tag m1a1 --seed 1` | `m1-A-1.md` |
| 3 | B-dbg-1 | `dotnet $G1 run --out $O/B-debug-1 --tag m1bd1 --seed 1 $B --executor-log debug` | `m1-B-debug-1.md` |
| 4 | A-dbg-1 | `dotnet $G1 run --out $O/A-debug-1 --tag m1ad1 --seed 1 --executor-log debug` | `m1-A-debug-1.md` |
| 5 | ramp-B | `dotnet $G0 ramp --out $O/ramp-clean --tag m1r2 --seed 1 $B --rates 400,800,1200,1600,2000,2400,3000,4000,5000,6000,8000,10000,12000,15000,20000,25000` | `m1-ramp-clean.md` |
| 6 | ramp-A | `dotnet $G0 ramp --out $O/ramp-full --tag m1r1 --seed 1` | `m1-ramp-full.md` |
| 7～10 | B-def-2、A-def-2、B-dbg-2、A-dbg-2 | 同 1～4，`-2` 后缀，tag 换成 `m1b2` 等 | `m1-*-2.md` |

- 命令行与 TASK-03 README 完全相同，只多了 debug 组的 `--executor-log debug`；`--host` 用默认（当前工作区的 Release Host）。
- 每次完成后，把 `summary.md` 原样复制为表中的记录名，`summary.json` 复制为 `raw/m1-<名称>.summary.json`；`samples.csv`、`host.log` 留在 scratchpad。
- 预计耗时：一次 `run` 约 5 分钟，两次爬坡各 5～8 分钟，总计约 1～1.5 小时，不含重跑。
- **两组的用途**：
  - **def 组**（默认日志级别，与基线同一个 dll）：端到端 (c)(d)(e)(f)、内层命令时长、错误与失败数，用于与基线逐项对照与 S2、S5；
  - **debug 组**：作业层执行时长与入队等待，用于 (a) 的作业层、S1、S4 与 (c)(e) 的参考。
- **日志开销的控制**：.NET 控制台日志的队列满时会让写日志的线程等待，debug 组在 (e) 灌包阶段每个包都写一行，可能把执行器拖慢。所以：
  - 用 def 组与 debug 组的端到端分布对照，逐窗口写出差值；
  - 某窗口 debug 组的端到端 p99 超过 def 组的 2 倍，就把该窗口 debug 组的入队等待标为「日志开销污染，只作参考」；
  - debug 组的作业时长若越过 S1，先看同一作业类型在 def 组的内层命令时长与端到端；def 组干净且远低于 2 s 时，归因为日志开销，记录不命中，并写明依据。
- **爬坡的「包」与「CMD」**：T-004 之后每个入站包都是执行器上的一个作业（负载不发遥测包），再加上约每秒 30 个空闲定时作业（F-08）。爬坡记录的包/秒即入站 CMD/秒，在 `m1-summary.md` 中写明这一换算。

### 5. 各量的取法与对照

#### 5.1 (a) CMD 执行时长

- 作业层：debug 两次的 `executorJobs`，按类型列分布、首次值、冷启动窗 max 与之后 max；
- 内层命令：def 组与 debug 组的 `commands` 与 `commandWarnings`；
- 与基线对照：基线只有内层命令。对 `baseline-summary.md`「M-1 预警」表的 8 行，逐行写出 M-1 的值与成因变化（例如口令哈希已移到执行器外，`RegisterAccountCommand` 不再含哈希）；
- 小样本按 README 的约定标 * 与 **。

#### 5.2 (b) 吞吐

- 两份爬坡记录的「可持续吞吐」与停止原因；与基线（A 无通过档、B ≥ 25000 未触顶）对照。
- B 的长请求只有 SELECT_DUNGEON，占比随速率下降（README），所以 B 的数衡量的是轻量包吞吐；A 含选角与建角，才是 RQ-090「该负载下」的口径。两者都判 S3。

#### 5.3 (c)(d)(e) 入队等待与端到端

- (d)：debug 组 `queueWait` 的 (d) 轮内分布判 S4；def 组 (d) 端到端按连接角色拆分，与基线 `baseline-summary.md` 的 (d) 拆分表对照（基线的领队连接 621.4／316.8 ms 来自登录哈希在全局门内串行，`PasswordComputationLimiter` 把它移出执行器后应当消失，M-1 写出实测）。
- (c)(e)：只作过渡态参考。越过 150 ms 时，按下面的顺序归因，写进 `m1-summary.md`：
  1. 是否日志开销污染（§4）；
  2. 同窗口内执行时长最大的作业类型，以及它们的执行时长之和占窗口时长的比例；
  3. 这些作业里是否含直接发送：读该作业类型的处理路径，看是否在作业内 await `TouchSocketNetworkMessageSender`（`t004-closure.md` §2 的来源证据表可作起点）；
  4. 结论分「直接发送主导（T-006 预期消除）」与「其他」，后者按 §1 处理。
- (e) 的灌包窗口沿用 32（与基线相同）；README 建议的更大窗口归 M-2，本单元不加。

#### 5.4 (f) 冷启动

- 最长单次停顿、出在哪个请求、它的参考序列值；冷启动窗逐秒入队等待；
- 与 `task-15-coldstart.md` 的 P1 第 3 轮（138.2／140.5 ms，都是首次选角）及基线 B（575.9／1118.5 ms）对照；
- F-04：首次 `inbound:GameSelectCharacter` 作业与首次 `PrepareSelectedCharacterStateCommand` 的时长，与之后各次对照。

#### 5.5 留给 M-1 的 C 类、D 类首次加载（F-03、F-05）

本单元不加探针（停止条件 4），用间接证据：
- C 类：首次 `inbound:GameSelectDungeon`、`inbound:GameSelectCharacter` 与之后各次的作业时长差；
- D 类（周签到、超额技能契约、每日补给规则）：「几乎每条玩法 CMD 都读」的代价摊在各作业的常态时长里，看这些作业的 p99、p99.9 是否在目标内；
- 结论写成「≤ 2 s，接受」或「超出目标值，登记后续优化，成因为推断」，不写成已证实的归因；
- 合成负载走不到的 CMD（装备强化、NPC 商店、邮件等）上的首次加载与读档，本单元测不到，登记为覆盖缺口（§6.3）。

#### 5.6 日志信号（每次运行后从 `host.log` 检索，写进 `logs/task-20-runs.txt`）

- 应为 0：`direct send deadline exceeded`、`GameplaySelfWait`、`must happen inside a gameplay job`、`Nested command dispatch`、`did not drain within`；`GameplayExecutorStoppedException` 只允许出现在停机之后；
- 计数并归类：`Gameplay … failed`（按作业类型）、`Command … failed in handler`（按命令类型）。基线 B 的失败命令为 0，A 为 113（SQLite 30 秒停顿，`baseline-summary.md`）；
- **构成 A 的 SQLite 30 秒停顿**：基线上它来自门外写入口。T-004 之后附录 F 的写入口都在执行器上（`logs/task-18-violations.txt`、`logs/task-19-violations.txt` 零违规），预期不再出现。若仍出现，它会以 S1／S2 越线的形式命中停止线，归因中引用 `baseline-summary.md`「SQLite 30 秒停顿」一节，并检索 `database is locked`。

### 6. 记录与 `follow-ups.md`

#### 6.1 `m1-summary.md` 的结构

开头写日期、基准提交 `5f7dba11`、「合成负载」声明、用途（M-1 预判；最终判定归 M-2 与 T-009）。

1. **结论**：M-1 通过／停止（命中哪条、在哪次运行、确认过程）；
2. **环境**：机器、系统、构建配置、SQLite 设置、swap 与负载背景、各次运行的干净／污染判定；
3. **运行矩阵**：每次运行的命令、dll、时长、样本量、`hostLogLines`；
4. **判定表**：§1 的 S1～S5 逐条，各次运行的值，是否越线（由判定脚本输出生成）；
5. **与基线对照**：沿用 `baseline-summary.md`「主要数字」的行，加 M-1 的四列（A-1、A-2、B-1、B-2），再加作业层与入队等待的新行；基线没有的量写「基线不存在」；
6. **(c)(e) 过渡态参考**与归因；
7. **后续性能优化事项**：每个未达目标值的项一行，对应的 F 编号；
8. **对 M-2 的交代**：工具已能解析入队等待；M-2 建议加跑更大的灌包窗口（README）；本次的覆盖缺口；
9. **缺口与局限**：合成负载、工具与 Host 同机、(e) 分不出灌包连接自己的作业、debug 组的日志开销、出站字节不在本单元范围（归 T-003／T-006）。

#### 6.2 README 更新

- 文件表加 `m1-summary.md` 等新文件；
- 命令行一节加 `--executor-log debug` 一行；
- 「入队等待」一节中「改造后工具也测不到它……在那之前，改造后的 (c)(d)(e) 只能给端到端数字」改为现状：执行器在 Debug 级记录入队等待，工具用 `--executor-log debug` 解析；写明日志开销的控制办法与 `bin/m1-base/` 的重建方法。README 是现状说明，按 AGENTS.md「过期的记载怎么办」直接改，不另起修正段。

#### 6.3 `follow-ups.md`

文件头补一条 TASK-20 的说明（日期、基准、本次更新了哪些编号、新增编号范围）。各项的预期去向，执行时按证据确认：

| 项 | 预期处理 |
|---|---|
| F-02 RQ-091 达标判定 | M-1 预判完成，结论与证据位置写进该行；状态保持未关闭，归属改为「M-2 与 T-009 最终判定」 |
| F-03 周签到等 D 类读档 | 按 §5.5 给出间接证据；≤ 2 s 即在 M-1 关闭，未达目标值的部分另立新编号 |
| F-04 首次选角约 100 ms | 按 §5.4 给出首次值；同上 |
| F-05 C 类首次加载 | 负载覆盖到的部分同上；覆盖不到的 CMD 转入新的覆盖缺口编号 |
| F-06 预热 W2 识别边界 | M-1 不能观测豁免项（`PvfItemTooltipSource` 只被 GM tooltip 路由读，而负载关闭了 GmWeb）；归属改为「人工把关」，状态保持未关闭 |
| F-07 日切长作业 | 5 分钟的运行通常碰不到日切，预期观测不到；见待裁定第 5 条 |
| F-08 空闲作业约每秒 30 个 | 用 `jobRatePerSecond` 与定时作业的执行时长、入队等待给出实测，在 M-1 关闭 |
| F-09 GM 查询与玩法串行 | 负载 `GmWeb__Enabled=false`，测不到；见待裁定第 5 条 |
| F-10 慢订阅 | 观测到 `event:` 作业时给出实测并关闭；没有观测到就按覆盖缺口处理，见待裁定第 5 条 |

新编号从 F-77 起，归属写明「后续性能优化（交主控方立项）」或「覆盖缺口」，状态「未关闭」。`t004-closure.md` 是带日期的对账记录，不改；它写的「M-1 尚未执行」由 `m1-summary.md` 接续。

### 7. 命中停止线时

1. 照常完成矩阵里其余的运行（数据越全，用户越好决定），除非 Host 已无法运行（停止条件 3）；
2. 按 §3.4 确认，按 §3.3、§5.3 归因，只读日志与代码；
3. 触发 §3.5 时重测基线 B 一次；
4. `m1-summary.md` 结论写「M-1 停止」，附：命中的规则与数值、确认过程、归因（标明哪些是推断）、与基线的对照、建议的下一步（例如先做的优化或更深的归因手段）。不提出、也不实施任何优化代码；
5. Result 中写明停止，由主控方回到用户。

## 待裁定

1. **「几倍」与「秒级停顿」的取数**。RQ-091 修订只写了「单条 CMD 超过 2 秒，或吞吐、等待比目标差了几倍」。
   - **建议**：「几倍」取 3 倍，即 (b) < 667 包/秒、(d) 入队等待 p99 > 150 ms；「秒级停顿」取与单条 CMD 相同的 2 s 线（S2）。理由：3 倍以上才算「数量级」的一侧，又不至于大到让 400 包/秒的实际规模都撑不住（667 仍高于 20 人 × 20 包/秒）；停顿与单条 CMD 同线，用户只需记一个数。
   - 若主控方认为 2～3 倍也应停止，只需改 §1 表中的两个数，其余计划不变。
2. **「单条 CMD」指哪一层**。过渡态下一个协议级 CMD 是一个执行器作业，内含若干内层命令与直接发送。
   - **建议**：S1 以作业层执行时长为准（不含入队等待），内层命令同时检查（内层超 2 s 必然意味着作业超 2 s）。入队等待单独由 S2、S4 管。作业内直接发送的时间算在作业里，T-006 之后它会从执行时长中移出，M-2 的数字因此可能更小。
3. **(c)(e) 越线时是否停止**。Plan 说 (c)(e) 在 M-1「只作过渡态参考」，而用户修订说「等待比目标差了几倍」就停。
   - **建议**：(c)(e) 越过 150 ms 不自动停止，但必须单列并归因（§5.3）；归因不是直接发送时按停止处理，因为那部分 T-006 不会改善，等到 M-2 再发现只会更晚。
4. **AC-005 测量一半与 AC-021 是否在 M-1 关闭**。AC-005 要求「测量记录满足 RQ-091」，AC-021 要求「测量记录包含口径与数字，满足达标标准」；T-005～T-008 之后代码还会变。
   - **建议**：M-1 只提供过渡态证据，不关闭两条 AC。AC-005 的测量一半记为「M-1 预判满足（证据位置），由 T-009 最终测量复核」；AC-021 按 Plan 归 M-2 与 T-009。F-02 相应保持未关闭（§6.3）。
5. **合成负载覆盖不到的三项**：F-07（日切长作业）、F-09（GM 查询）、F-10 中没观测到的慢订阅。
   - **建议**：登记为一个新的覆盖缺口编号，归「后续性能优化（交主控方立项）」，不阻断 T-005 与 M-2。理由：用户立场是「性能能接受就行，后续再专门优化」；要覆盖它们，需要让负载开 GmWeb 并发 GM 查询、或在测量中跨过日切，这都是扩展负载工具的工作，不属于本闸。F-07 原有的「T-008 拆分」归属不变。
6. **工具改动与「同一套测量工具」**。RQ-090 要求基线与改造后用同一套测量工具；本单元给工具加了一项解析。
   - **建议**：接受。def 组与爬坡仍用原 dll（SHA-256 与基线相同）；新解析只产生基线上本来不存在的作业层与入队等待数字（README 已预见这一步）。M-2 起统一用新 dll，默认模式下只多一次正则匹配，README 中写明。

## 主控方裁定（执行前）

依据：用户口径「性能能接受就行；单条指令执行时间不超过 2 s 都算接受；只有数量级问题才回到用户」（spec.md RQ-091 用户修订，c3115f9a）。停止线只保留真正的「不可接受」，其余一律记录为目标对照与后续优化事项。

1. **停止线收窄**：
   - S1、S2、S5 照计划。
   - **S3 改为 (b) 可持续吞吐 < 400 包/秒**（RQ-091 的实际规模 20 人 × 20 包/秒；低于它即实际规模下不可用，属数量级问题）。400～2000 之间记为未达目标、登记后续优化，不停止。
   - **S4 取消停止效力**：(d) 入队等待 p99 只记录并对照目标 50 ms；它若达到 2 s 已由 S2 覆盖。
2. **「单条 CMD」以执行器作业层执行时长为准**（不含入队等待），同时检查内层命令，接受计划建议。
3. **(c)(e) 越线不自动停止**：必须归因并单列；不再以「归因不是直接发送」作为停止条件，只有达到 S1／S2 的 2 s 线才停止。
4. **AC-005 测量一半与 AC-021 不在 M-1 关闭**，接受计划建议：AC-005 记「M-1 预判满足，由 T-009 复核」，AC-021 归 M-2 与 T-009，F-02 保持未关闭、归属改为 M-2 与 T-009。
5. **合成负载覆盖不到的三项**（F-07、F-09、F-10 中的未观测部分）合并登记为一个覆盖缺口，归后续性能优化立项，不阻断 T-005 与 M-2，接受。
6. **工具改动接受**：默认日志的运行与爬坡用归档原 dll；新解析只产生基线没有的作业层与入队等待数字；M-2 起统一用新 dll。


## Result

执行轮次：第 1 轮，含执行中按主控方 S3 裁定所做的补测（2026-10-23，基准 `5f7dba11`，未提交）。拥有的改动见 Changed 与 `logs/task-20-boundary.txt`。

- **Outcome**：done。结论 **M-1 通过**：S1～S5 在全部 12 次运行中都不命中。依据是下文「主控方裁定（补测回报后）」与 Why 第 1 条的两条主控方裁定。
  - **S3 不命中**：2 s 口径爬坡 ramp-A-2s（`measurement/m1-ramp-full-2s.md`）。400、800、1200 三档通过，1600 档停止（实际 1245 包/秒，判定 max 8039.2 ms）。构成 A 的可持续吞吐 1188 包/秒，不低于 400。
    - 各档判定 p99：124.5／127.3／462.5／7691.5 ms；
    - 各档判定 max：163.3／219.8／668.5／8039.2 ms；
    - 各档作业执行时长 max：125.6／119.7／110.8／190.5 ms；
    - 各档错误都为 0。采样干净，不重跑。
    - p99 ≤ 50 ms 口径的 ramp-A／ramp-A-2（400 档 p99 112.0／109.0 ms，「无通过档」）原样记为 F-77 的目标对照。
  - **S5 不命中**（8 个收尾失败作业经主控方裁定不计入，F-83 归 T-006）：ramp-A-2s 收尾时有 8 个失败作业（6 个 `inbound:GameConnectionCheck`、2 个 `inbound:GameSetUserPosition`）。
    - 异常是 `Writing is not allowed after writer was completed`；全部在 Host 本地时间 04:19:55.908～.926 的 18 ms 内，即工具在最后一个过载档后关闭 20 条连接的时刻。
    - 读日志与代码的推断：排队作业向刚关闭的连接回包而失败。各档统计期间没有失败；其余 11 次运行收尾时队列为空，这类失败为 0。
    - 它不是协议拒绝，按 S5 字面原本算越线。主控方裁定它属于断线竞态的预期失败，与基线同类，不计入；判定脚本按裁定把它单列为「预期，不计」。
  - **S1 未越线**：执行器单作业 max 236.1 ms（首次选角）；内层命令 max 103.8 ms。
  - **S2 未越线**：(f) max 1189.6 ms；入队等待 max 1245.8 ms；稳态与 (d) 窗两个序列都 ≥ 2 s 的样本 0 个。
  - **S4 只记录**：(d) 入队等待 p99 构成 A 74.1／100.8 ms 未达 50 ms 目标；构成 B 11.6／17.2 ms 达标。
  - **(c)(e)**：没有超过 150 ms 的项。
- **Changed**：
  - 工具（两次加法改动）：`Tool/GameplayLoad/HostProcess.cs`、`Program.cs`、`Scenarios.cs`。
    - 第 1 次加 `--executor-log off|debug`：解析执行器作业行，`summary.md` 末尾追加「执行器作业」一节，`summary.json` 追加 8 个键。
    - 第 2 次按主控方裁定加 `--ramp-criterion p99:50|max:<ms>`：默认 `p99:50` 保持原判据；`max:<ms>` 逐档判 max、作业 max、错误与 95% 速率。
    - 删除的 12 行（6 ＋ 6）逐行说明见 `logs/task-20-tool.txt`。
    - 改动前的 dll 归档在 `bin/m1-base/`（`b3d6c286…`，与基线相同）。debug 运行用的是 `b47cc49a…`，2 s 爬坡用的是 `deb158a5…`。第 2 次改动不触及 `run` 路径，所以 debug 运行的数字不受影响。
  - 记录：
    - `verification/measurement/m1-summary.md`（先读）；
    - `m1-{A,B}-{1,2}.md`、`m1-{A,B}-debug-{1,2}.md`；
    - `m1-ramp-full-2s.md`（判 S3）、`m1-ramp-full.md`、`m1-ramp-full-2.md`（目标对照）、`m1-ramp-clean.md`；
    - `raw/m1-*.summary.json`（8 份）、`raw/m1-tool-version.txt`；
    - `measurement/README.md`：文件表、命令行 5／6、默认值、「入队等待」一节改为现状。
  - `verification/follow-ups.md`：
    - 文件头加 TASK-20 一条；
    - F-02～F-10 逐条更新：F-03、F-04、F-08 关闭；F-05、F-10 关闭覆盖到的一半；F-09 转入 F-82；F-02 改归 M-2 与 T-009；F-06 归人工把关；F-07 拆分仍归 T-008；
    - 新增 F-77～F-83，共 7 项：F-77～F-81 归后续性能优化（F-77 记 2 s 口径 1188 包/秒与 50 ms 口径的目标对照）；F-82 是覆盖缺口；F-83 是收尾断线时的失败作业，经主控方裁定不计入 S5，归 T-006。
  - 日志：`verification/logs/task-20-{build,tool,env,runs,judgement,boundary}.txt`。
  - 未改：`Server/`、`spec.md`、`plan.md`、`t004-closure.md`、既有测量记录。
- **Why**：
  - **主控方执行中裁定（S3 的读法）**：S3 不按工具爬坡判据「p99 ≤ 50 ms」读。那个 50 ms 是 RQ-091 的原目标，用户修订后只作记录；接受线是「单条 ≤ 2 s」。所以 S3 问的是：在 2 s 接受线下，构成 A 的可持续吞吐是否 < 400 包/秒。
    - 为此按裁定补跑了 2 s 口径爬坡：实际速率 ≥ 95%、错误 0、判定 max < 2000 ms、作业 max < 2000 ms；档位从 400 起，到 25000 或第一档不通过为止。
    - 工具原本没有判据参数，所以按「只做加法、默认保持 50 ms」新增了 `--ramp-criterion`。
    - 第 1 轮原来的结论「M-1 停止（命中 S3）」因此改判。原文的依据，即 p99 ≤ 50 ms 口径的两次爬坡，保留为目标对照。
  - **主控方补测回报后裁定（F-83）**：
    - 2 s 爬坡新出现 8 个收尾失败作业，主控方给的「两种情况」没有覆盖这一项。本单元当时按 S5 字面回报「停止」，并给出归因。
    - 主控方裁定（原文见本文件末尾「主控方裁定（补测回报后）」）：这 8 个属于断线竞态的预期失败，与基线同类，不计入 S5，M-1 结论改为「通过」。依据：对已关闭会话的写入，基线与今天都由 `session.SendAsync` 抛出，发送器自 `a02bce46` 以来只加了 2 s 期限的包装；执行器下更常见，只是因为积压时排队的作业更多。
    - F-83 归 T-006：发件箱的交付对象是已关闭的连接时静默丢弃，作业不因此失败，由用例钉住。
    - 据此，`judge.py` 把带 `Writing is not allowed after writer was completed` 的作业失败行单列为「预期，不计」，最后一行改为「M-1 通过」。只改文档与判定脚本，没有重跑测量。
  - **加跑 ramp-A-2**（在裁定之前）：当时 S3 由第一档单个 p99 决定，加跑约 1 分钟只为复现，不改变判定。
  - **判定脚本对 P2 的处理**：同窗口参考序列 p99 > 5 ms 时，把发出滞后归为闭环积压，而不是工具迟到。只影响「干净／受污染」的标注。
  - **工具上的两处取舍**：`jobRatePerSecond` 按实际前缀分组，是计划五组的超集；另加 `executorBusy`（执行器占用）键，供归因使用。
  - **解析自检取前 N 行**：工具写完记录之后才结束 Host，所以只数前 `logLinesAtSnapshot` 行。
- **Checks**：
  1. 被测构建：HEAD `5f7dba11`，`Server` 无改动；Release `--no-incremental` 构建 0 警告 0 错误；Host.dll `37d60ea9…`（`logs/task-20-build.txt`）。
  2. 工具：
     - 归档 dll 的哈希与 `raw/tool-version.txt` 一致；两次改动后的构建都是 0 警告 0 错误；
     - diff 全文与逐段说明在 `logs/task-20-tool.txt`；
     - 4 次 debug 运行解析自检全部相等（A-dbg-1 1676037、A-dbg-2 1672170、B-dbg-1 2683329、B-dbg-2 2656327），未匹配 0，数值失败 0。
  3. 环境：12 次运行（10 次矩阵 ＋ ramp-A-2 ＋ ramp-A-2s）的静默检查全部通过。只有 B-dbg-1 的稳态窗与 (e) 窗受污染（P3，与本单元自己的 `judge.py` 读盘重合），没有越线，只标注（`logs/task-20-env.txt`）。
  4. 运行记录：12 次运行退出码全为 0（`logs/task-20-runs.txt`）。
  5. 判定：`judge.py` 逐条输出，按两条主控方裁定后最后一行为「M-1 通过」。`m1-summary.md` 的表由 `fill.py` 从判定输出填入。主控方裁定、规则、归因与脚本全文在 `logs/task-20-judgement.txt`。
  6. 引用核对：`refs.py` 核对 28 个路径、16 个 F 编号、16 条状态；按所有者／状态规则核对 83 行；未解决 0。
  7. 边界：status 只含 Owned boundary 的路径与本任务文件；`Server` 无 diff；`diff --check` 无输出；新文件无行尾空白；没有残留进程，已执行 `dotnet build-server shutdown`（`logs/task-20-boundary.txt`）。
  - 稳态对照（`m1-summary.md` §1、§5.6）：
    - 构成 A 稳态端到端 p99 91.7～95.5 ms，为基线 2.73～2.79 ms（基线为构成 B）的约 33～35 倍；构成 B 当前 14.7～18.4 ms，约 5～7 倍。
    - 推断的份额：A 稳态执行时长之和里，`GameSetUserArea` 占 59.6～59.7%，选角占 31.5～32.0%。选角内四条每日补给命令 p50 之和是选角 p50 的 85.8～89.5%，即约 28% 的执行时长花在每日补给命令上（32.0% × 85.8% ＝ 27.5%；31.5% × 89.5% ＝ 28.2%）。
- **Remaining**：
  - Result review 为 required。F-83 交 T-006 用用例钉住「向已关闭连接交付时静默丢弃」。
  - 更深的归因（未做，需主控方决定）：对 `SetUserAreaHandler` 与选角作业做 dotnet-trace 剖析或加临时计时探针。
  - scratchpad `t20/m1/` 留有各次的 `samples.csv` 与 `host.log`（约 4 GB），不入库，复核后可删。

## 主控方裁定（补测回报后）

- **S3**：按 2 s 口径读（补测前裁定，见 Result Why）。构成 A 的可持续吞吐为 1188 包/秒，≥ 400，不命中。
- **F-83 的 8 个收尾作业失败归为「断线竞态的预期失败」，不计 S5，M-1 结论改为「通过」。**
  - **依据**：这 8 个作业都是在连接关闭之前已经入队，轮到执行时向已关闭的会话回包，抛出 `Writing is not allowed after writer was completed`。时间全部落在工具关闭连接那 18 ms 内，各档统计期间为 0。
  - **与基线同类**：发送器对已关闭会话的写入，基线与今天都由 `session.SendAsync` 抛出；`TouchSocketNetworkMessageSender` 自 `a02bce46` 以来只加了 2 s 期限的包装，没有改变这一行为。基线里正在发送的 handler 撞上断线，同样会失败。执行器下这种情况更常见，只是因为有积压时排队的作业更多（这一档是在过载之后关闭连接）。
  - **后续**：F-83 归 T-006。发件箱的交付对象是已关闭的连接时，应当静默丢弃，不让作业失败；T-006 要用一条用例钉住这一点。

## 主控方处置（评审后，提交前）

独立快审 verdict `approved`（`reviews/task-20-result-review.md`），发现都不阻断。提交前处理如下，结论不变：

1. **L1**：F-77 补记：1188 包/秒只是 18 秒档长下的上限（1200 档执行器占用约 89%，档末发出滞后仍在上升）；稳定通过的档是 800（实际约 770～800 包/秒）。M-2 与 T-009 不得把 1188 当作余量依据。
2. **L2**：`judge.py` 的「预期，不计」规则没有时间窗。F-83 补记：M-2 复用脚本时去掉这条豁免（T-006 之后这类失败应为 0），或把它限定在工具关闭连接之后的窗口内。
3. **L3**：F-83 补记：「与基线同类」是读代码得出的推断，不是基线上的观测（基线构成 A 爬坡第一档即被 SQLite 停顿卡住，构成 B 收尾时队列为空，观测不到）。依据三条：`TouchSocketNetworkMessageSender` 自 `a02bce46` 起只多了期限包装、异常原样上抛；TouchSocket 版本未变（4.3.1）；连接关闭不等接收回调结束。评审另核实失败限于单个作业、无黏滞程序错误、两类作业不涉及事务。
4. **I1**：(f) 构成 A 冷启动最长停顿 1189.6 ms 是目标 250 ms 的 4.8 倍，向用户汇报时一并说明（F-80 已登记）。
5. **I2**：Checks 6 的路径数改为 28；收尾的通知总线 `Connection … is not active` 警告与 A-def-2 窗口外的 3 行 timed_out 只作记录。
