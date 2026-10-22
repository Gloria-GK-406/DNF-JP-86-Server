# 测量工具与口径（TASK-03）

> **合成负载。** 用户没有提供 A21 客户端的真实游玩录制，RQ-090 第①项不可用；这里用第②项：
> 由测量工具按 TASK-01 表征测试已验证的线上序列合成负载。所有数字都只代表这份合成负载。

基准：分支 `task/refactor-session-20261021`，HEAD `53c72543`。`git diff --stat a02bce46 53c72543 -- Server/src`
为空，所以被测的 Host 与基线 `a02bce46` 的服务端源码相同。

## 文件

| 文件 | 内容 |
|---|---|
| `README.md` | 本文：命令行、驱动方式、负载构成、口径、局限 |
| `baseline-summary.md` | **先读这份**：两种构成、新口径下的主要数字、工具抖动、吞吐、出站字节、SQLite 30 秒停顿、RQ-091 对照与 M-1 预警、与第 1 轮口径的差异、缺口 |
| `baseline-1.md` | 构成 A（完整）一次运行的记录（工具生成的 `summary.md` 原样保存） |
| `baseline-clean-1.md`、`baseline-clean-2.md` | 构成 B（绕开基线停顿缺陷）连续两次运行的记录 |
| `ramp-full.md`、`ramp-clean.md` | 吞吐爬坡：构成 A；构成 B（400 → 25000 包/秒） |
| `outbound.md`、`outbound-frames.txt` | 空闲 Host 上单条请求对单个连接的出站字节（新建角色上的实测值），以及逐帧清单（也是回包签名的核对依据） |
| `raw/` | 各次完整运行的 `summary.json`（聚合结果）。逐请求的 `samples.csv`（每份约 350 MB，主要是灌包样本）与 Host 日志只留在运行目录，不入库 |
| `superseded/` | 第 1 轮的记录（闭环不补发、超时剔除的旧口径），只作对照，原因见其中的 README |
| `m1-summary.md` | **M-1 先读这份**（TASK-20，T-004 之后）：判定、与基线的逐项对照、作业层执行时长与入队等待、(c)(e) 过渡态参考、后续性能优化事项 |
| `m1-A-1.md`、`m1-A-2.md`、`m1-B-1.md`、`m1-B-2.md` | M-1 默认日志级别的运行（与基线同一个工具 dll），工具生成的 `summary.md` 原样保存 |
| `m1-A-debug-1.md`、`m1-A-debug-2.md`、`m1-B-debug-1.md`、`m1-B-debug-2.md` | M-1 `--executor-log debug` 的运行（改动后的工具），末尾多一节「执行器作业」 |
| `m1-ramp-full-2s.md` | M-1 构成 A 的 2 s 口径爬坡（`--ramp-criterion max:2000`），S3 按它判定 |
| `m1-ramp-full.md`、`m1-ramp-full-2.md`、`m1-ramp-clean.md` | M-1 吞吐爬坡（p99 ≤ 50 ms 口径）：构成 A 两次，只作目标对照；构成 B（400 → 25000 包/秒） |
| `raw/m1-*.summary.json`、`raw/m1-tool-version.txt` | M-1 各次运行的聚合结果；两组运行各用哪个工具 dll 及新旧源码的 SHA-256 |

## 命令行

```bash
# 1. 基线 Release 构建（工具不进解决方案，单独构建）
dotnet build Server/86JP.Server.slnx -c Release
dotnet build Tool/GameplayLoad -c Release

# 2. 完整运行：(f) 冷启动窗 → 稳态窗 → (d) 并发登录 → (e) 灌包
G=Tool/GameplayLoad/bin/Release/net10.0/GameplayLoad.dll
B="--weights 0.5,0.5,0,0,0 --entry-concurrency 1"          # 构成 B
dotnet $G run --out <dir>/clean-1 --tag c1 --seed 1 $B      # 构成 B，连续两次
dotnet $G run --out <dir>/clean-2 --tag c2 --seed 1 $B
dotnet $G run --out <dir>/baseline-1 --tag b1 --seed 1      # 构成 A（默认）

# 3. 吞吐爬坡
dotnet $G ramp --out <dir>/ramp-full --tag r1 --seed 1
dotnet $G ramp --out <dir>/ramp-clean --tag r2 --seed 1 $B \
    --rates 400,800,1200,1600,2000,2400,3000,4000,5000,6000,8000,10000,12000,15000,20000,25000

# 4. 单请求出站字节精确值
dotnet Tool/GameplayLoad/bin/Release/net10.0/GameplayLoad.dll outbound --out <dir>/outbound

# 5. 作业层执行时长与入队等待（M-1 起）
dotnet $G run --out <dir>/B-debug-1 --tag m1bd1 --seed 1 $B --executor-log debug

# 6. 2 s 口径的爬坡（M-1 起，S3 按它判定）：逐档要求判定 max < 2000 ms、作业执行时长 max < 2000 ms、错误 0、实际速率 ≥ 目标 95%
dotnet $G ramp --out <dir>/ramp-full-2s --tag m1r1s --seed 1 --executor-log debug --ramp-criterion max:2000 \
    --rates 400,800,1200,1600,2000,2400,3000,4000,5000,6000,8000,10000,12000,15000,20000,25000
```

默认参数：`--host Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，`--pvf` 取
`PVF_ARCHIVE_PATH` 或仓库根的 `Script.pvf`；`--solos 8 --pairs 6 --rate 20`（每连接包/秒）；
`--cold 60 --steady 120 --bursts 6 --burst-size 10 --burst-every 10 --flood 30 --flood-window 32 --dwell 4`；
`--weights 0.47,0.45,0.068,0.01,0.002`（单人：栅栏、移动、跨区、回选角+再选角、回选角+建角+选新角）；
爬坡 `--rates 400,800,1200,1600,2000,2400,3000,4000,5000,6000 --step 20 --warm 30`；
`--entry-concurrency 0`（不限）；`--executor-log off`；爬坡 `--ramp-criterion p99:50`（下面「可持续吞吐」一节的判据；`max:<ms>` 见命令 6）。改造后的 M-1/M-2 用**同一命令行**重跑端到端与命令时长，只换 `--host`（或重新构建）；
作业层执行时长与入队等待另加 `--executor-log debug` 运行，见「入队等待」一节。

## 驱动方式

- **Host 是子进程**，做法同 `Server/tests/Dfo.Server.Host.Tests/EndToEndHostHarness.cs`：每次运行一个新的临时目录和
  SQLite 文件，`dotnet Dfo.Server.Host.dll`，配置全部走环境变量（`Networking__Enabled=true`、
  `Networking__UdpDiscoveryEnabled=false`、三个游戏端口与频道端口改到空闲端口、`Channel__UseScriptCatalog=false`、
  `LauncherAuth__Enabled=true`、`GmWeb__Enabled=false`、`Pvf__ArchivePath`、`Database__Path`）。负载打在
  `#ch.11`（appsettings 的 `Channels[1]`），与 WireSmoke 相同。就绪 = 启动器 `/api/launcher/v1/bootstrap`
  返回 200 且游戏端口可连。**(f) 因此是真正的冷进程**：Host 就绪的那一刻负载开始，什么都不预热。
- **日志**：`appsettings.json` 本来就是 `Logging:LogLevel:Default=Information`，`CommandLoggingBehavior` 的成功行就是
  Information，所以**不需要**另开 Information。唯一的日志改动是 `Logging__Console__FormatterOptions__SingleLine=true`，
  让每条命令日志单独成行便于解析；产品代码未改。**日志配置是环境项**：基线一次完整运行约 1 万条命令日志；改造后每个入站包
  都成为 CMD 并写开始/结束日志（RQ-054），Information 级控制台日志经管道输出可能本身成为瓶颈。改造后复测时把实际的日志级别
  与每次运行的 Host 日志行数（`summary.json` 的 `hostLogLines`）一并记录，必要时加跑一次降低日志级别的对照。
- **账号**经启动器 HTTP 注册（`/api/launcher/v1/register`）并取票（`/api/launcher/v1/login`），两者都在服务端做口令哈希；
  之后全部走 TCP。
- **线上序列**取自 TASK-01：`Server/tests/Dfo.Server.Networking.Tests/Characterization/WireClient.cs` 与
  `CharacterizationSteps.cs`。**做法是复制后改写，不链接源文件，也不引用测试项目**——改写是为了计时：
  接收改为每连接一条专用线程阻塞在 `Socket.Receive`，读返回的那一刻用 `Stopwatch.GetTimestamp()` 打戳，
  到达时间不含工具自身线程池的排队。请求体逐字节沿用：LOGIN、CREATE_CHARACTER（job 0）、SELECT_CHARACTER、
  SET_USER_AREA（1:1）、REQUEST_PEER/RESPONSE_PEER（type 0）、ENTER_SELECT_DUNGEON（`00000000`）、
  SELECT_DUNGEON 144、FINISH_LOADING、EPLP（`0101`）、LEAVE_PARTY；另加 SET_USER_POSITION（0x0023，7 字节，
  `TownProjectionSlice.SetUserPositionDecoder`）与 RETURN_SELECT_CHARACTER（0x0007，空 body）。
- TASK-01 的两点发现照办：双人场景先各发 0x0024 进 1:1 再组队；死亡之塔需要 50 级和道具 4183，线上做不到，
  **不纳入负载**（TASK-01 (e) 用的是直接写库，负载工具不写库）。

## 负载构成（合成）

20 条背景连接 = 8 个单人 + 6 个双人组（12 条），每连接目标 20 包/秒，合计 400 包/秒；另 1 条灌包连接平时每 2 s
发一个栅栏（`idle` 组，不计入背景）。依据：RQ-091 的私服规模 20 人 × 20 包/秒。

| agent | 每个节拍做什么 |
|---|---|
| 单人 | 按权重抽一个动作：栅栏 0x04DD（0.47）、城镇内移动 0x0023（0.45）、跨区 0x0024 在 1:0 与 1:1 之间来回（0.068）、回选角 0x0007 + 再选角 0x0004（0.01）、回选角 + 建角 0x0005 + 选新角（0.002；满 32 个角色后改为再选角） |
| 双人组 | 两条连接各自在城镇里发栅栏/移动（副本里只发栅栏）；每个状态停留 4–6 s 后推进一步：组队（0x000A → 0x000B）→ 进本（0x000F → 0x0010 → 双方 0x0025）→ 领队离本（0x0048 `0101`）→ 队员离队（0x000D）→ 回到组队前 |

- 起始序列（建号 → LOGIN → CREATE → SELECT，双人组再各进 1:1）在 Host 就绪后**所有 agent 同时开始**
  （`--entry-concurrency 0`），算在冷启动窗里。
- 上表是**构成 A**（默认，RQ-090 要求的完整构成）。**构成 B** = `--weights 0.5,0.5,0,0,0 --entry-concurrency 1`：
  单人只发栅栏与移动，起始序列逐个进行，稳态窗里没有跨区、回选角、建角；双人组不变。B 是因为 A 在基线上触发了一个
  既有的 SQLite 30 秒停顿缺陷才加的（见 `baseline-summary.md`），用来给 (b)(c)(d)(e) 一个不被缺陷吞掉的基线对照值。
- **节奏（第 2 轮起；第 3 轮起每条连接一张节拍表）**：每条连接有自己的节拍表，按「本动作发出的包数 / 目标速率」排下一个节拍，**节拍表从不重置**。
  双人组的领队与队员是两个玩家，各自一个循环、各自一张节拍表：领队一步在服务端等待时，队员照常发自己的轻量包，
  不会因此欠下节拍（第 2 轮两条连接共用一张表，把领队的等待算到了队员头上，审查 R2-B1）。组队、进本、离本、离队由领队的循环
  驱动，按动作归属记在领队连接上（动作的第一个请求带领队的节拍）；其中队员的那部分（队员自己的请求、队员的收口栅栏）在队员
  连接的互斥下发出，不带节拍、以实际发出时刻为计划时刻（它等领队的时间不算它的），包数计入队员的节拍表。
  进出副本期间队员只发栅栏（SET_USER_POSITION 只在城镇区域有回包）。队员的轻量包与这些步骤交错，所以负载下双人组同伴的
  出站字节窗口里会混入队员自己的轻量回包，是上界。
  每条连接仍是闭环（上一个请求收口前不发下一个，因为服务端对同一连接本来就按顺序处理），但落后时不再从当前时刻重排，
  而是背靠背地把欠下的节拍发完，直到追上节拍表。每个动作的第一个请求带上它的**计划发出时刻**（节拍时刻）；
  同一动作里必须等前一个回包的后续请求（如回选角之后的选角）以实际发出时刻为计划时刻。
- **判定序列 = 计划发出 → 第一条回包**（协调遗漏校正，coordinated omission）。理由：闭环客户端在服务端停顿时少发包，
  如果从实际发出时刻算，停顿期间本该发出、也会被拖慢的那些请求根本没有样本，p99 系统性偏低（审查 B1）。
  从计划时刻算，每个被耽误的节拍都留下一个包含等待时间的样本。**实际发出 → 第一条回包**的序列保留为参考，两者都写在记录里。
  没有改成开环发包，是因为服务端对一个连接本来就串行处理，开环只会把请求堆在 socket 里，排队时间与计划口径等价，
  而闭环加计划时刻还能让出站字节窗口保持可归属。
- **到停止时还没发出的节拍**（agent 落后太多，运行结束前没追上）也计入：每个欠下的节拍记一个「（计划但未发出）」截尾样本，
  取「计划时刻 → 停止时刻」。停止时仍在途的请求同样记为截尾样本。计数方式：按每个包间隔记 1 个，而多包动作实际只会产生
  1–2 个判定样本，所以积压很大时这类样本略多；只影响已经严重不达标的情形。
- **补发会改变服务端负载的形状**：落后后背靠背补发，会按原比例重放欠下的动作，包括长动作（回选角、建角），所以停顿之后的
  若干秒里长 CMD 的密度高于名义比例；节拍落后还会跨窗口延续（A 从冷启动起就没追上，它的「稳态窗」量的是冷启动的积压）。
  **判读任何窗口之前先看该记录的「发出滞后」**：滞后大，说明窗口不独立，数字反映的是之前的积压。
- 爬坡收口时，主线程读取各连接节拍表的「下一个未发节拍」与 agent 线程的写入没有协调（节拍表内部加锁，但读到的可能是写入前一刻的值），
  最多偏一个节拍，只影响「是否已收口」的判断，属良性竞态。
- 没有服务端停顿时，判定序列比参考序列多出的是工具自身定时器的迟到（`Task.Delay` 的粒度，B 下 p99 约 2.3 ms），
  会让判定口径略偏大；每份记录都给出「发出滞后（实际发出 − 计划发出）」的分布。
- **步骤栅栏**：除栅栏和移动外，每个请求后面同一次写入紧跟一个 0x04DD，用它的应答收口这个请求的回包窗口；
  双人组的同伴在请求者收口后再发一个栅栏收口自己的窗口。这些栅栏计为 `STEP_FENCE` 包，计入速率与构成。
- 实际构成按**稳态窗内实际发出的入站包**统计，写在每份 baseline 记录里。构成 A 的稳态窗含 SELECT_CHARACTER、
  CREATE_CHARACTER、SELECT_DUNGEON 等长请求；构成 B 的稳态窗只有 SELECT_DUNGEON 一类长请求（选角、建角只在冷启动窗）。
- 随机数按 `--seed` 固定（每个 agent 一个种子），两次运行的动作序列相同；服务端的反应（停顿、失败）不固定，
  所以两次的实际构成会有小差别。
- 失败（服务端关闭连接、请求 60 s 无应答）记一条错误，agent 重新取票、LOGIN、选第 0 个角色后继续；
  双人组任何一方失败则两条一起重连并回到组队前。

## 口径

### 端到端延迟

- **起点（判定）**：该请求的计划发出时刻（见上「节奏」）。**起点（参考）**：工具写出请求帧之前的 `Stopwatch` 时间戳。
  **终点**：该请求**第一条回包**被接收线程读到的时间戳。各窗口按计划发出时刻归窗。
- **第一条回包按签名识别**，签名逐条对照 TASK-01 金样本，并在 `outbound-frames.txt` 里逐请求核对过
  「请求者窗口内的第一帧就是签名帧」：

  | 请求 | 第一条回包 |
  |---|---|
  | 0x0001 LOGIN / 0x0004 SELECT_CHARACTER / 0x0005 CREATE_CHARACTER / 0x0007 RETURN_SELECT_CHARACTER / 0x0048 EPLP / 0x04DD | 同 type 的 CMD |
  | 0x0023 SET_USER_POSITION | NOTI 0x0016，且开头 u16 为自己的 wire id |
  | 0x0024 SET_USER_AREA | NOTI 0x0017，且开头 u16 为自己的 wire id |
  | 0x000F / 0x0010 | NOTI 0x0002 且 body ≥ 500 字节（完整 USERINFO），或同 type 的 CMD（拒绝/应答） |
  | 0x000B RESPONSE_PEER、0x000D LEAVE_PARTY | NOTI 0x0009 |
  | 0x0025 FINISH_LOADING | NOTI 0x001E |
  | 0x000A REQUEST_PEER | 请求者没有回包 |

  城镇 NOTI（0x0016/0x0017）带 wire id 比对，所以别的玩家的城镇投影不会提前停掉本请求的时钟。
  0x000F/0x0010 的签名**不比对 wire id**：组队进本时请求者会先后收到两条 519 字节的 NOTI 0x0002，按 XO-1 第一条可能是
  队员的而不是自己的；两条通常在同一次读里到达，对计时影响很小，但「第一条回包是自己的 USERINFO」没有核实。
- **两类「没等到回包」分开处理**（审查 B2）：
  - **协议无回包**（如 REQUEST_PEER：步骤栅栏应答先到，签名始终没出现）：不是延迟样本，不计入分布，单列计数；
  - **超时或连接被关闭**（60 s 内没等到签名，或连接先断了）：作为**截尾样本**计入分布与 (f)，取值「计划发出 → 超时/关闭」，
    是**下界**。表里的样本数旁注明截尾个数，最大值是截尾样本时 max 前加「≥」。
- **样本量小的单类分布**：nearest-rank 下 n < 1000 时 p99.9 就是 max，n < 100 时 p99 也是 max；记录里这些行的样本数分别标 * 与 **。
  长 CMD（SELECT_DUNGEON、SELECT_CHARACTER 等）多数属于这种情况，工具抖动的结论只对聚合分布成立，不能推到单类长 CMD。
- RQ-090 的两条注明：**基线上多段请求（如选角）的首条回包可能早于整段结束，改造后按 VC-1 会推迟到 CMD 结束；
  没有回包的 CMD 不计入这个量。** 因此改造后的选角端到端数字预期变大，这是口径决定的，不是退化。
- 起点与终点都在客户端：包含两次本机回环 TCP 与工具接收线程的唤醒。工具与 Host 在同一台机器上，抢同一组 CPU。
  RQ-090 写的是服务端口径（入站帧到达 → 第一条回包离开），客户端口径只会更大。

### 服务端命令执行时长

解析 Host 标准输出里 `CommandLoggingBehavior` 的两种日志行（成功 `… handled by … in {ElapsedMilliseconds} ms`、
失败 `… failed in handler … after {ElapsedMilliseconds} ms`），按命令类型聚合 p50/p95/p99/p99.9/max。
范围是 Host 就绪之后整次运行（冷启动窗与 (d)(e) 都在内），每条命令按工具读到日志行的时刻归窗。
基线上大部分 handler 不是命令（如 0x0023、0x04DD、0x0024 由 handler 直接处理），它们只有端到端数字。

### 可持续吞吐（b）

`ramp`：先按 400 包/秒预热 30 s（不计），然后按目标总速率逐档提高，每档 20 s、每档前 2 s 不计。
**某档背景请求的端到端 p99（判定序列，截尾样本计入）> 50 ms，或该档出现错误（超时、断线、拒绝），或实际包速率 < 目标的 90%，
即停止；可持续吞吐取此前最后一个通过档的实际包速率。** 每档结束后，先等计划发出时刻落在本档、仍在途或还没发出的请求收口
（完成、超时或断线；agent 追上节拍表；最多 65 s）再统计，等待期间负载按本档速率继续；65 s 后仍未发出的节拍按截尾样本计入。
速率靠每连接的目标速率提高，连接数不变，但**构成比例不是不变的**：单人的动作按包抽取，长请求（选角、建角）随速率增加；
双人组的组队与进本按停留时间推进，不随速率增加。所以构成 B 每档的长请求（只有 SELECT_DUNGEON）都是 5–7 个，
占比随速率下降（400 档 0.069%，2000 档 0.014%，25000 档 0.001%）。每档的长请求包数与占比写在爬坡记录里。
「包」包括步骤栅栏；基线上一个入站包不一定对应一条命令，改造后每个入站包是一条协议 CMD，所以用包/秒作对照单位。

### (d) 10 个并发登录

稳态窗之后，每 10 s 一轮、共 6 轮，每轮 10 个并发：启动器注册（哈希）→ 启动器取票（校验）→ TCP → LOGIN，应答后断开。
「轮内」= 该轮开始到 10 个 LOGIN 都应答的区间。比较计划发出时刻落在轮内的背景请求与稳态窗的端到端分布。

### (e) 单连接灌包

一条已选角、站在城门区（不共享在场，没有扇出）的连接，保持 32 个 0x04DD 在途，持续 30 s。
比较灌包期间其余连接的端到端分布与稳态窗；另给灌包连接自己的应答速率。
**窗口 32 是结论的一部分**：基线上服务端按连接并行，窗口大小几乎不影响别的连接；改造后单执行器上，灌包连接在执行器队列里
最多同时占 32 项（RQ-005 实施后入站 CMD 每连接最多 1 条待执行，窗口其余部分停在传输层），(e) 的结果直接取决于这个数。
M-2 建议加跑一档更大的窗口（`--flood-window 256`）或开环灌包，并在记录里写明窗口。0x04DD 不经命令总线、无扇出，
是最轻的路径，灌包连接每秒约 20 万个应答衡量的是「一个连接能抢走多少」，不是命令吞吐。

### (f) 冷启动

Host 就绪即开始负载（起始序列也在内），前 60 s 按计划发出时刻逐秒给 p99 与 max；
**最长单次停顿 = 窗口内的最大单请求延迟**（判定序列；setup 与 background 两组；截尾样本计入，是下界）。

### 入队等待

**基线不存在。** 基线没有执行器，没有「入队 → 开始执行」这个量。(c)(d)(e)(f) 在基线上只给端到端对照，
不拿别的量凑数。

**改造后由执行器日志提供**（M-1 起）：执行器在 Debug 级为每个完成的作业记一行
`Gameplay {CommandType} completed: … in {ElapsedMilliseconds} ms after {QueueWaitMilliseconds} ms queued`，失败行是 Error 级
（`… after {ElapsedMilliseconds} ms, {QueueWaitMilliseconds} ms queued`，`Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs`）。
`--executor-log debug` 让工具给 Host 多设一个环境变量 `Logging__LogLevel__Dfo.Server.Application.Execution.GameplayExecutor=Debug`
（只抬这一个类别），并解析这两种行：执行时长 = 作业体开始 → 结束，不含入队等待；入队等待 = 入队 → 开始执行。
与命令日志一样按工具读到行的时刻归窗，`summary.md` 末尾多一节「执行器作业」：各类作业的执行时长分布（含首次值、冷启动窗
与之后的 max）、四个窗口（冷启动、稳态、(d) 轮内、(e) 灌包期间）的入队等待（全部作业与只算 `inbound:`）、冷启动窗逐秒的入队
等待、各窗口的执行器占用（执行时长之和 ÷ 窗口时长）、稳态窗按来源前缀的每秒作业数。`summary.json` 对应新增 `originUtc`
（就绪时刻的 UTC，供环境采样对齐）、`executorLog`、`executorLines`、`executorJobs`、`queueWait`、`queueWaitColdPerSecond`、
`executorBusy`、`jobRatePerSecond`；既有的段落、窗口与键不变。默认 `off` 时 Host 只输出失败行，新节的分布不代表全部作业。

- **解析自检**：`executorLines.total` 应等于 `head -n <executorLines.logLinesAtSnapshot> host.log | grep -cE 'Gameplay .+ (completed|failed):'`。
  只取前 N 行，是因为工具写完记录之后才结束 Host，`host.log` 在那之后还会收到几行定时作业。
- **日志开销**：Debug 级下每个作业一行，(e) 灌包期间每秒几十万行；控制台日志队列满时写日志的线程会等，可能拖慢执行器。
  所以作业层数字只从 debug 运行取，端到端、内层命令与错误仍以默认级别的运行为准；同一窗口 debug 运行的端到端 p99 超过默认
  运行的 2 倍时，该窗口的入队等待只作参考（M-1 的做法，`m1-summary.md`）。
- **(e) 的局限**：作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人
  同分布，(e) 窗的「全部作业」可作代理。
- **工具版本**：M-1 的默认级别运行与爬坡用改动前的 dll（`Tool/GameplayLoad/bin/m1-base/`，SHA-256 与 `raw/tool-version.txt`
  相同，口径与基线逐字节一致）；debug 运行用改动后的 dll。`bin/` 不入库：改动前的 dll 可从提交 `5075a283` 的源码确定性
  重建（源码 SHA-256 见 `raw/tool-version.txt`），改动后的见 `raw/m1-tool-version.txt`。M-2 起统一用改动后的 dll，默认
  级别下它只对每条非命令日志行多做一次子串检查与正则匹配。

### 单请求出站字节

「这次请求开始到下一次请求之前」在工具里落为：请求者从请求发出到步骤栅栏应答之间收到的全部帧字节
（含 15 字节帧头，不含栅栏应答本身）；同伴从请求者发出请求到同伴自己的收口栅栏应答之间收到的字节。
- `outbound`：空闲 Host、只有涉及的连接，按 TASK-01 的选角序列与组队进本序列逐步测，得到**新建角色上的精确值**。
  **它不是上界**：选角序列里多帧随角色数据增长，例如 ITEM_LIST（NOTI 0x000D）为「5 B ＋ 条目数 × 101 B」
  （`protocol/game/server-to-client/NOTI-0x000D.md`；本次 1520 B = 5 + 15 × 101，只有 15 个起始物品），背包、装扮栏、任务
  都随游玩增长。按它取 2 倍只是 RQ-036 下限的下限；T-003 要按容器容量上界推算，或用容器装满的角色实测（主控方处置）。
- 负载运行里同样按窗口记录，按请求类型 × 角色取最大值；负载下别的玩家的城镇投影也会落进窗口，所以是「含他人投影」的上界。

### SQLite 设置

- `journal_mode`：运行中对 Host 的数据库文件开只读连接执行 `PRAGMA journal_mode;`。WAL 是文件级属性，读到的就是
  Host 的实际值。
- `synchronous`：连接级设置。服务端源码里没有任何 `PRAGMA synchronous`（`Server/src` 唯一的 PRAGMA 写入是
  `Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`
  （如 `Dfo.Server.Persistence/Database/SqliteDatabase.cs:41`），所以服务端每条连接都是原生库的编译期默认值。
  记录里的值来自工具以同样形式的连接串、对同一个数据库、先读 journal_mode 再读 synchronous 得到的结果
  （WAL 下 SQLite 按 `DEFAULT_WAL_SYNCHRONOUS` 调整），工具与 Host 用的是同一个原生库——两份
  `libe_sqlite3.dylib` 的 SHA-256 写在记录里；另列出 `PRAGMA compile_options` 中的相关项。
  **取的是「与服务端连接同条件的新连接」的值，不是从服务端进程内部读出的。**

## 局限

1. 合成负载，不是 A21 录制。动作比例是为了让长请求有足够样本而定的，选角频率（约每秒一次以上）远高于真实玩家。
2. 工具与 Host 同机运行；客户端每连接闭环，服务端停顿时实际速率低于目标（记录给实际速率），被耽误的节拍由计划时刻口径
   与「（计划但未发出）」截尾样本补上。
3. 端到端终点是工具读到帧的时刻；TASK-01 已查明 `SendAsync` 返回时帧尚未落线，所以服务端「发出」与客户端「读到」之间
   还有 TouchSocket 发送循环的时间，这部分算在端到端里。
4. 负载下的出站字节含他人投影；精确值以 `outbound.md` 为准。
5. 选角首个账号的南部溪谷补给浮动帧 NOTI 0x0015 由事件循环发出（TASK-01），可能落在窗口之外；精确值里不保证计入。
6. 出站字节只在新建角色上测过，见上「单请求出站字节」。
