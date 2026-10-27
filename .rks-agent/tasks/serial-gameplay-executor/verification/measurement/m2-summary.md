# M-2 测量汇总（TASK-28，合成负载）

> **合成负载**：没有 A21 的真实录制，负载由 `Tool/GameplayLoad` 按 TASK-01 的线上序列合成，口径与命令行见 [README](README.md)。
> **用途**：M-2 是 T-005（删锁）与 T-006（投递换成发件箱与发送端）之后的硬停闸，也给出 (a)～(f) 的正式数字。最终判定归 T-009。

- 测量日期：2026-10-23（UTC 11:24～12:58，16 次运行，没有重跑）
- 被测服务端：HEAD `f7bf0aa3`（T-006 全部完成），Release 构建，`Dfo.Server.Host.dll` SHA-256 `4ccb0f2e…`（`../logs/task-28-build.txt`）。相对 M-1 的被测代码 `5f7dba11`，`Server/src` 有 186 个文件改动
- 判定口径：`spec.md` RQ-091 的用户修订（`c3115f9a`）；TASK-20 的主控方裁定（S3 按 2 s 口径读；S4 无停止效力；(c)(e) 只有达到 2 s 线才停止）；TASK-28「主控方裁定（执行前）」1～6（S2 覆盖 (e) 窗口与窗口 256；S5 去掉「预期，不计」豁免；S5 加发件箱与发送端信号，发送端层告警只在收尾窗口内不计；`OverLimit` 不设时间豁免）。规则与 §4.4 的六条预期在第一次运行之前写进了 `../logs/task-28-judgement.txt` 开头
- **回报后的裁定**：`../../tasks/TASK-28.md`「主控方裁定（回报后）」把工具就绪探测连接上的 16 行发送端告警归为「工具主动断开」，不计入 S5（F-119）。判定脚本据此在 `ATTRIBUTED` 中登记这 16 行后重算，没有重跑测量
- 本页的数字表全部由脚本输出生成：判定脚本 scratchpad `t28/judge.py`，延迟构成 `t28/composition.py`，冷启动停顿对应的请求 `t28/coldmax.py`。全文与原始输出在 `../logs/task-28-judgement.txt`，不手抄

## 1. 结论

**M-2 通过。** S1～S5 在 16 次运行中都不命中。依据是主控方回报后的裁定（下面「F-119 的裁定」）。

判定脚本的第一版按字面给出过「M-2 停止（命中 S5）」：收尾窗口之外有发送端层告警，S5 第 5 项。那次判定的现象、归因与理由原样保留在下面一小节；被改判的数字与那次的输出见 `../logs/task-28-judgement.txt` 的「First judgement (record)」一节。

### 按字面曾判「停止」的那一条：工具就绪探测连接上的两行发送端告警（记录）

- **现象**：构成 B 的 8 次运行（B-def-1／-2、B-dbg-1／-2、B-def-f256、B-dbg-f256、ramp-B、B-def-g0）各有两行 Warning，都在就绪后约 0.1 s：
  - `Outbound entry for Game:01-00-00-00: Failed`，异常是 TouchSocket 的 `ClientNotConnectedException`；
  - `Outbound connection Game:01-00-00-00 closed: Failed`。

  构成 A 的 8 次运行没有这两行。
- **是哪条连接**：`Game:01-00-00-00` 是进程的第一条游戏连接，即工具的就绪探测。`HostProcess.cs` 的 `WaitForBootstrapAsync` 在 `/bootstrap` 返回 200 后，用 `using var probe = new TcpClient()` 连一下游戏端口就关闭，随后取就绪时刻。
  - host.log 里这条连接只有这两行；
  - debug 运行中，这两行夹在它的 `lifecycle:connected` 作业（Host 的初始连接通知登记进发件箱）与 `lifecycle:disconnected` 作业之间。
- **机制（读代码推断）**：T-006 之后，初始连接通知经发件箱交给发送端。交付时工具已经关闭了探测连接，发送端按 RQ-036 以 `Failed` 结束该连接的队列，并记这两行（`OutboundSender.cs:250`、`:279`）。
  - 没有作业失败，没有影响其他连接，错误 0；
  - M-1 的日志里这条连接一行都没有（当时初始通知在观察者里直接发送），所以这两行是 T-006 的发送端带来的，是 RQ-036 设计的正常出口。
  - 只出现在构成 B（第 1 轮评审后改正）：两种构成里，探测连接的生命周期作业都在就绪后约 0.1 s 才进入，M-1 也一样，这是原来就有的首连延迟，不是 B 特有的。
    - 例：M-2 的 B-dbg-1 为 0.107 s，A-dbg-1 为 0.118 s；M-1 的 B-debug-1 为 0.100 s，A-debug-1 为 0.116 s。
    - 只有 B 出现告警，原因是发送端交付与断开处理在毫秒级上谁先谁后（B-dbg-1 里 `connected` 到 `disconnected` 隔 6 ms，A-dbg-1 隔 14 ms），成因未测。
    - 第一版写成「A 运行里只晚几毫秒」，那是把入队等待当成了距就绪的时间，是错的。
- **为什么按规则算命中**：§1.3 第 5 项只豁免「(d) 轮里工具在 LOGIN 应答后主动断开的登录连接」，并写明「否则命中」。就绪探测不属于这一种。B-def-1、B-dbg-1、B-dbg-f256 四个窗口都干净，所以是干净样本中的越线。8 次构成 B 运行全部出现，按 §3.4 不需要重跑确认。
- 本单元当时不放宽豁免：放宽就是改已定口径，属于停止条件 4。所以第一次判定的结论是「M-2 停止（命中 S5）」，交主控方决定，登记为 F-119。

### F-119 的裁定（主控方，回报后）

- **结论**：这 16 行（构成 B 每次 2 行，8 次）归为「工具主动断开」，不计入 S5，M-2 结论改为「通过」。原文见 `../../tasks/TASK-28.md`「主控方裁定（回报后）」。
- **依据**：
  - 这条连接是测量工具的就绪探测连接，连上游戏端口后立即由工具关闭。交付时客户端已经断开，发送端按 RQ-036 以 `Failed` 结束该连接的队列，正是 RQ-036 设计的正常出口。作业失败 0，错误 0，其他连接不受影响。
  - 与收尾窗口豁免的理由相同，都是客户端（工具）先断开，只是发生在测量开始之前。§1.3 第 5 项的豁免清单漏列了这种连接，属于判定规则的遗漏，不是服务端缺陷。
  - 只在构成 B 出现、成因未测的时序竞态，不影响这一归类：无论哪种时序，交付对象都是一条已被工具关闭的连接。
- **处理**：
  - `judge.py` 的 `ATTRIBUTED` 逐行登记了这 16 行（运行、host.log 行号、类别与依据），并断言它们都是 `Game:01-00-00-00` 上的 `Failed` 行；重算的最后一行是「M-2 通过」。测量没有重跑。
  - F-119 的状态改为「已裁定，不计」。客户端先断开时发送端按 Warning 记 `Failed`，这个日志级别是否合适，交 T-009 复核（F-121）。

### 其余各条

- **S1 不命中**：
  - 单条作业执行时长 max 239.1 ms（A-dbg-2，`inbound:GameSelectCharacter`，稳态窗 t＝154.8 s）；
  - 冷进程首次选角 226.8～229.9 ms；
  - 内层命令 max 110.0 ms（B-def-2，`PrepareSelectedCharacterStateCommand`）；
  - ramp-A-2s 的作业 max 230.2 ms。
- **S2 不命中**：
  - (f) 最长单次停顿 max 1367.6 ms（A-dbg-2，判定与参考序列相同），是 2 s 线的 0.68 倍；
  - 入队等待 max 1366.4 ms（A-dbg-2 冷启动窗）；
  - 稳态、(d)、(e) 三个窗口（窗口 32 与 256）没有两个序列都 ≥ 2 s 的端到端样本，只在判定序列上 ≥ 2 s 的也是 0。
- **S3 不命中**：ramp-A-2s 可持续吞吐 1169 包/秒，不低于 400；ramp-B 16 档全部通过，各档 max ≤ 70.01 ms，错误 0，2 s 口径成立。
- **S4（只记录）**：(d) 入队等待 p99，构成 A 100.0／180.9 ms（窗口 256 那次 90.52 ms），未达 50 ms；构成 B 1.00／25.92／1.82 ms，达标。
- **S5 的其余各项**：
  - 16 次运行的错误、失败命令、失败作业都是 0，不设任何时间豁免；
  - 必须为 0 的日志信号都是 0，包括 `GameplaySelfWait`、发件箱四种告警、`database is locked`、`direct send deadline exceeded`；
  - `OverLimit` 在任何时刻都是 0；
  - 收尾窗口内的发送端告警按裁定第 2 条不计：ramp-A-2s 12＋12 行，A-dbg-f256 6＋6 行，都是 `closed: Failed`／`entry Failed`；
  - 收尾窗口外的发送端告警 16 行，全部是上面 F-119 的就绪探测连接，按回报后的裁定不计；其余 0 行。
  - **M-1 在 ramp-A-2s 收尾时的 8 个失败作业（F-83）在 M-2 是 0**：同一个断线竞态现在在发送端结束，不回到作业。

### 是否触发 §3.5 的基线重测

未触发：
- 第一条不成立：S1～S3 都不命中；S5 的成因已经查明，不需要回答「是改造带来的还是环境变了」。
- 第二条不成立：构成 B 默认级别的稳态端到端 p99 为 26.07／20.52 ms，不到 M-1（14.7～18.4 ms）的 3 倍。

### 相对 M-1 变差的项（都在 2 s 接受线以内，只记录、不阻断）

下面四项比 M-1 差，都远低于 2 s，不触及任何停止线，归入 F-77～F-81 与 F-120，不阻断 T-007、T-008：

| 项 | M-2 | M-1 | 离 2 s 线 | F 编号 |
|---|---|---|---|---|
| (d) 构成 A 轮内背景请求的端到端 p99 | 138.8／205.8 ms（max 180.8／237.2 ms）；同轮入队等待 p99 100.0／180.9 ms | 71.5／133.4 ms；入队等待 p99 74.1／100.8 ms | max 237.2 ms，是 2 s 线的 0.12 倍 | F-79 |
| (e) 构成 B 灌包期间其余连接的端到端 p99 | G1：9.77／11.20 ms（max 41.80／47.42 ms），约翻倍；G0：17.84 ms（max 45.72 ms，单次） | 4.94／4.42 ms（G0） | max 47.42 ms，是 2 s 线的 0.02 倍；p99 也在 50 ms 以内 | F-120 |
| 灌包连接自身的应答速率 | 构成 B 7.8 万包/秒，构成 A 5.7～6.0 万包/秒，约减半 | 15.1～15.3 万；9.0～9.2 万 | 这是单个连接能抢走多少执行器，不是延迟，不涉及 2 s 线 | F-120 |
| ramp-A-2s 的 1200 档判定 p99 | 1011.7 ms（max 1266.0 ms），约翻倍 | 462.5 ms（max 668.5 ms） | max 1266.0 ms，是 2 s 线的 0.63 倍；该档按 2 s 口径通过 | F-77 |

其余变差的量（构成 A 的 (c) 与 (f)、构成 B 的稳态与 (f)）同样在 2 s 以内，数值见 §5、§7。

### AC 关联（TASK-28 主控方裁定第 5 条，「M-2 通过」的记法）

- AC-005 的测量一半：**M-2 满足**（按修订后的 RQ-091 接受线，证据 `m2-summary.md`），由 T-009 最终测量复核并关闭。
- AC-021：**M-2 有完整口径与数字，且满足接受线**，由 T-009 最终测量关闭。
- 两条都不在 M-2 关闭：最终交付的是 T-008 之后的代码，只有 T-009 的测量能代表它。
- F-02 保持未关闭，写入 M-2 结论与证据位置，归属由「M-2 与 T-009」改为「T-009 最终判定」。

### 下一步

- 按 Plan，M-2 通过后 T-007 可以开始，T-008 的进入条件里 M-2 这一项成立。
- F-120 与 F-77～F-81 归后续性能优化（交主控方立项），F-121 交 T-009 复核，F-82 覆盖缺口原样保留，都不阻断 T-007、T-008。

## 2. 环境

- 机器与软件：
  - Apple M5（10 核），32 GiB，macOS 27.0.1，内置 SSD（APFS），接电源；
  - SQLite 与构建配置由工具写进每份运行记录（`m2-*.md` 的环境节）；
  - GC 为 Host 默认，环境中无 `DOTNET_` 变量。
- 内存与后台负载：
  - swap 已用 8.6～9.0 GB／9 GB，B-def-g0 中途降到 6.9 GB；各次运行的 pageouts 均为 0；
  - 同机还有其他应用与其他 agent 会话在跑（采样里有 `git`、`codex`、`Python`）。
- 开跑前：两个本工作区遗留的孤儿测试 Host（8 h 44 min 前启动，ppid 1）会让静默检查失败，已结束，记在 `../logs/task-28-build.txt` 末尾。
- 静默检查：16 次全部通过（等待 0～150 s）。逐次结果、前后快照与每秒采样汇总见 `../logs/task-28-env.txt`。
- **测量期间没有运行任何分析脚本**：后处理在矩阵结束（12:58Z）之后才开始。M-1 曾因判定脚本与运行重叠而污染一个窗口，这次没有。

**干净／受污染**（§3.3，逐窗口）：

| 运行 | 判定 |
|---|---:|
| B-def-1 | cold 干净；steady 干净；d 干净；e 干净 |
| A-def-1 | cold 干净；steady 干净；d 干净；e 干净 |
| B-dbg-1 | cold 干净；steady 干净；d 干净；e 干净 |
| A-dbg-1 | cold 干净；steady 干净；d 干净；e 干净 |
| ramp-B | 整段：受污染 |
| ramp-A-2s | 整段：受污染 |
| B-def-2 | cold 受污染；steady 干净；d 干净；e 干净 |
| A-def-2 | cold 干净；steady 干净；d 干净；e 干净 |
| B-dbg-2 | cold 受污染；steady 干净；d 干净；e 干净 |
| A-dbg-2 | cold 干净；steady 干净；d 干净；e 干净 |
| ramp-A | 整段：干净 |
| B-def-f256 | cold 干净；steady 受污染；d 干净；e 干净 |
| A-def-f256 | cold 受污染；steady 受污染；d 干净；e 干净 |
| B-dbg-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| A-dbg-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| B-def-g0 | cold 受污染；steady 受污染；d 干净；e 干净 |

- 受污染的窗口全部来自 P3 换页尖峰（每秒数千到 3.9 万页换入），只有 B-def-f256 的稳态窗是 P1（其他进程 > 200% 持续 9 s）。逐秒的进程名见 `../logs/task-28-env.txt`。
- 构成 A 各窗口的发出滞后 p99 为 40～179 ms，同窗口参考序列 p99 为 88～108 ms，是闭环积压，不计为 P2（同 M-1）。
- S1～S3 在任何样本（干净或受污染）中都没有越线，所以没有因污染重跑。S5 那 16 行（第一次判定按字面计为越线，3 次干净样本中都有）成因与环境无关，回报后裁定不计。

## 3. 运行矩阵

- 命令行与 M-1 相同（`../logs/task-28-runs.txt` 有每次的完整命令行），只有三处差别：
  - 默认级别组与 p99 ≤ 50 ms 爬坡改用 M-1 的最终 dll G1（`deb158a5…`）；
  - 加跑窗口 256 的四次；
  - B-def-g0 是用基线 dll G0（`b3d6c286…`）的控制运行。
- 工具输出逐行加了 UTC 毫秒时间戳（`ts.py`），用来定收尾窗口起点：
  - `run`：「logins done, flooding」那一行加 30 s 灌包，再加工具在灌包后等待的 2 s（`Scenarios.cs` 的 `Task.Delay(2 s)`，之后才 `stop.Cancel()`）。计划写的是「加 `--flood` 秒数」，漏了这 2 s；这里取较晚的点，落进窗口的行只会更少；
  - `ramp`：最后一行「ramp N: …」的时刻。
- 记录：
  - 默认级别：`m2-A-1.md`、`m2-A-2.md`、`m2-B-1.md`、`m2-B-2.md`；
  - debug 级：`m2-A-debug-1.md`、`m2-A-debug-2.md`、`m2-B-debug-1.md`、`m2-B-debug-2.md`；
  - 窗口 256：`m2-A-f256.md`、`m2-B-f256.md`、`m2-A-f256-debug.md`、`m2-B-f256-debug.md`；
  - 控制运行：`m2-B-g0.md`；
  - 爬坡：`m2-ramp-full-2s.md`（判 S3）、`m2-ramp-full.md`、`m2-ramp-clean.md`；
  - 聚合结果 `raw/m2-*.summary.json`，工具版本 `raw/m2-tool-version.txt`。

| 运行 | 工具 | dll SHA-256 | 开始 (UTC) | 结束 (UTC) | 退出码 | Host 日志行数 | 收尾窗口起点 (UTC) | 静默检查 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| B-def-1 | `run` 构成 B；G1；tag `m2b1` | deb158a5… | 11:24:58 | 11:29:41 | 0 | 3509 | 11:29:37.641 | 通过 |
| A-def-1 | `run` 构成 A；G1；tag `m2a1` | deb158a5… | 11:31:42 | 11:36:23 | 0 | 14261 | 11:36:20.740 | 通过 |
| B-dbg-1 | `run` 构成 B + debug；G1；tag `m2bd1` | deb158a5… | 11:38:54 | 11:43:37 | 0 | 2040776 | 11:43:32.820 | 通过 |
| A-dbg-1 | `run` 构成 A + debug；G1；tag `m2ad1` | deb158a5… | 11:46:08 | 11:50:50 | 0 | 1467706 | 11:50:46.643 | 通过 |
| ramp-B | `ramp` 构成 B；G1；tag `m2r2` | deb158a5… | 11:52:21 | 11:58:18 | 0 | 3093 | 11:58:16.940 | 通过 |
| ramp-A-2s | `ramp` 构成 A + debug + `max:2000`；G1；tag `m2r1s` | deb158a5… | 12:00:19 | 12:02:26 | 0 | 111296 | 12:02:26.816 | 通过 |
| B-def-2 | `run` 构成 B；G1；tag `m2b2` | deb158a5… | 12:02:28 | 12:07:09 | 0 | 3506 | 12:07:06.048 | 通过 |
| A-def-2 | `run` 构成 A；G1；tag `m2a2` | deb158a5… | 12:09:10 | 12:13:51 | 0 | 14229 | 12:13:48.965 | 通过 |
| B-dbg-2 | `run` 构成 B + debug；G1；tag `m2bd2` | deb158a5… | 12:15:23 | 12:20:06 | 0 | 2053900 | 12:20:01.313 | 通过 |
| A-dbg-2 | `run` 构成 A + debug；G1；tag `m2ad2` | deb158a5… | 12:21:37 | 12:26:19 | 0 | 1526637 | 12:26:15.249 | 通过 |
| ramp-A | `ramp` 构成 A；G1；tag `m2r1` | deb158a5… | 12:27:20 | 12:28:14 | 0 | 3204 | 12:28:14.472 | 通过 |
| B-def-f256 | `run` 构成 B + 窗口 256；G1；tag `m2bf` | deb158a5… | 12:28:15 | 12:32:56 | 0 | 3484 | 12:32:53.303 | 通过 |
| A-def-f256 | `run` 构成 A + 窗口 256；G1；tag `m2af` | deb158a5… | 12:34:57 | 12:39:38 | 0 | 14223 | 12:39:35.584 | 通过 |
| B-dbg-f256 | `run` 构成 B + debug + 窗口 256；G1；tag `m2bfd` | deb158a5… | 12:40:39 | 12:45:22 | 0 | 2011952 | 12:45:17.721 | 通过 |
| A-dbg-f256 | `run` 构成 A + debug + 窗口 256；G1；tag `m2afd` | deb158a5… | 12:47:23 | 12:52:05 | 0 | 1521090 | 12:52:01.419 | 通过 |
| B-def-g0 | `run` 构成 B；G0（基线 dll）；tag `m2bg0` | b3d6c286… | 12:53:36 | 12:58:17 | 0 | 3492 | 12:58:14.247 | 通过 |

- 解析自检：debug 运行（不含 ramp-A-2s，爬坡不生成 summary.json）的工具解析行数与 `grep` 计数逐次相等，数值字段解析失败 0（`../logs/task-28-tool.txt` §4）。

## 4. 判定表

越线项（按回报后的裁定重算；第一次判定的 8 行 S5 越线见 `../logs/task-28-judgement.txt`「First judgement (record)」）：

（无越线项）

逐条：

| 规则 | 运行 | 值 | 越线 | 样本 |
|---|---:|---:|---:|---:|
| S1 inner command | B-def-1 | 102.1 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | A-def-1 | 106.4 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-dbg-1 | 98.56 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | B-dbg-1 | 227.5 ms inbound:GameSelectCharacter at t=0.53 s | 否 | 干净 |
| S1 inner command | A-dbg-1 | 102.0 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | A-dbg-1 | 229.6 ms inbound:GameSelectCharacter at t=0.63 s | 否 | 干净 |
| S1 inner command (whole log) | ramp-B | 100.2 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command (whole log) | ramp-A-2s | 100.9 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 executor job (after readiness) | ramp-A-2s | 230.2 ms inbound:GameSelectCharacter (100389 job lines) | 否 | 受污染 |
| S1 inner command | B-def-2 | 110.0 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command | A-def-2 | 102.9 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-dbg-2 | 99.58 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 executor job | B-dbg-2 | 229.9 ms inbound:GameSelectCharacter at t=0.52 s | 否 | 受污染 |
| S1 inner command | A-dbg-2 | 102.8 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | A-dbg-2 | 239.1 ms inbound:GameSelectCharacter at t=154.8 s | 否 | 干净 |
| S1 inner command (whole log) | ramp-A | 100.5 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-def-f256 | 100.6 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command | A-def-f256 | 101.0 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command | B-dbg-f256 | 101.6 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | B-dbg-f256 | 227.8 ms inbound:GameSelectCharacter at t=0.52 s | 否 | 干净 |
| S1 inner command | A-dbg-f256 | 99.02 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | A-dbg-f256 | 226.8 ms inbound:GameSelectCharacter at t=0.66 s | 否 | 干净 |
| S1 inner command | B-def-g0 | 102.7 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S2 (f) longest stall (judged; reference) | B-def-1 | 236.3 ; 236.3 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-1 | 968.5 ; 968.5 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-1 | 232.4 ; 232.4 ms | 否 | 干净 |
| S2 queue wait max (cold) | B-dbg-1 | 231.8 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (steady) | B-dbg-1 | 57.91 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-1 | 3.50 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-1 | 38.13 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-1 | 1020.2 ; 115.1 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-1 | 567.3 ms lifecycle:connected | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-1 | 166.9 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-1 | 111.4 ms inbound:GameLogin | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-1 | 144.4 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-2 | 247.4 ; 247.4 ms | 否 | 受污染 |
| S2 e2e >= 2 s on both series (steady) | B-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-2 | 1053.1 ; 212.2 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-2 | 230.9 ; 230.9 ms | 否 | 受污染 |
| S2 queue wait max (cold) | B-dbg-2 | 231.1 ms event:achievement.inventory | 否 | 受污染 |
| S2 queue wait max (steady) | B-dbg-2 | 53.48 ms inbound:GameSetUserPosition | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-2 | 88.17 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-2 | 37.65 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-2 | 1367.6 ; 1367.6 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-2 | 1366.4 ms event:quest.inventory | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-2 | 240.0 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-2 | 187.1 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-2 | 165.5 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-f256 | 231.0 ; 231.0 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-f256 | 0 (judged-only 0) | 否 | 受污染 |
| S2 e2e >= 2 s on both series (d) | B-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-f256 | 887.8 ; 887.8 ms | 否 | 受污染 |
| S2 e2e >= 2 s on both series (steady) | A-def-f256 | 0 (judged-only 0) | 否 | 受污染 |
| S2 e2e >= 2 s on both series (d) | A-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-f256 | 229.2 ; 229.2 ms | 否 | 干净 |
| S2 queue wait max (cold) | B-dbg-f256 | 228.7 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (steady) | B-dbg-f256 | 62.37 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-f256 | 100.3 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-f256 | 41.44 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-f256 | 921.3 ; 921.3 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-f256 | 921.7 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-f256 | 158.8 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-f256 | 100.5 ms event:quest.mirror-leave | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-f256 | 181.2 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-g0 | 229.8 ; 229.8 ms | 否 | 受污染 |
| S2 e2e >= 2 s on both series (steady) | B-def-g0 | 0 (judged-only 0) | 否 | 受污染 |
| S2 e2e >= 2 s on both series (d) | B-def-g0 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-g0 | 0 (judged-only 0) | 否 | 干净 |
| S3 (p99 50 ms ramp; 2 s side read from its steps) | ramp-B | 25005.0 packets/s, stop: 全部档位通过（未触顶，最高档即下限） | 否 | 受污染 |
| S3 (2 s criterion) | ramp-A-2s | 1169.0 packets/s, stop: 判定 max ≥ 2000 ms | 否 | 受污染 |
| S5 | B-def-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | A-def-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | B-dbg-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | A-dbg-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | ramp-B | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |
| S5 | ramp-A-2s | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 24, 'Writing is not allowed' 12 | 否 | 受污染 |
| S5 | B-def-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |
| S5 | A-def-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | B-dbg-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |
| S5 | A-dbg-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | ramp-A | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | B-def-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |
| S5 | A-def-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |
| S5 | B-dbg-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 干净 |
| S5 | A-dbg-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 0), inside teardown (收尾，不计) 12, 'Writing is not allowed' 6 | 否 | 干净 |
| S5 | B-def-g0 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender warnings outside teardown 0 (attributed to tool disconnect 2), inside teardown (收尾，不计) 0, 'Writing is not allowed' 0 | 否 | 受污染 |

收尾窗口（§1.3）。窗口内的发送端层告警按裁定第 2 条「收尾，不计」；窗口外的计入 S5：

| 运行 | 收尾窗口起点 (UTC) | 窗口内（收尾，不计） | 窗口外（计入 S5） | 窗口外但归为工具主动断开 | OverLimit（任何时刻） |
|---|---:|---:|---:|---:|---:|
| B-def-1 | 11:29:37.641 | 0 | 0 | 2 | 0 |
| A-def-1 | 11:36:20.740 | 0 | 0 | 0 | 0 |
| B-dbg-1 | 11:43:32.820 | 0 | 0 | 2 | 0 |
| A-dbg-1 | 11:50:46.643 | 0 | 0 | 0 | 0 |
| ramp-B | 11:58:16.940 | 0 | 0 | 2 | 0 |
| ramp-A-2s | 12:02:26.816 | connection closed Failed 12；entry outcome Failed 12 | 0 | 0 | 0 |
| B-def-2 | 12:07:06.048 | 0 | 0 | 2 | 0 |
| A-def-2 | 12:13:48.965 | 0 | 0 | 0 | 0 |
| B-dbg-2 | 12:20:01.313 | 0 | 0 | 2 | 0 |
| A-dbg-2 | 12:26:15.249 | 0 | 0 | 0 | 0 |
| ramp-A | 12:28:14.472 | 0 | 0 | 0 | 0 |
| B-def-f256 | 12:32:53.303 | 0 | 0 | 2 | 0 |
| A-def-f256 | 12:39:35.584 | 0 | 0 | 0 | 0 |
| B-dbg-f256 | 12:45:17.721 | 0 | 0 | 2 | 0 |
| A-dbg-f256 | 12:52:01.419 | connection closed Failed 6；entry outcome Failed 6 | 0 | 0 | 0 |
| B-def-g0 | 12:58:14.247 | 0 | 0 | 2 | 0 |

日志开销（§4.3）：debug 组与默认级别组的端到端 p99 逐窗口对照，没有一个窗口超过 2 倍。debug 组的入队等待都按正常数据使用。

| 运行对 | 窗口 | 默认级别 | debug | 差 | debug > 2 × 默认 |
|---|---:|---:|---:|---:|---:|
| B-dbg-1 / B-def-1 | cold | 40.92 | 40.28 | -0.65 | 否 |
| B-dbg-1 / B-def-1 | steady | 26.07 | 16.33 | -9.73 | 否 |
| B-dbg-1 / B-def-1 | (d) | 84.27 | 90.32 | 6.05 | 否 |
| B-dbg-1 / B-def-1 | (e) | 9.77 | 7.45 | -2.33 | 否 |
| A-dbg-1 / A-def-1 | cold | 151.1 | 286.9 | 135.8 | 否 |
| A-dbg-1 / A-def-1 | steady | 103.9 | 103.9 | 0.06 | 否 |
| A-dbg-1 / A-def-1 | (d) | 138.8 | 114.2 | -24.66 | 否 |
| A-dbg-1 / A-def-1 | (e) | 112.8 | 110.7 | -2.04 | 否 |
| B-dbg-2 / B-def-2 | cold | 39.67 | 41.81 | 2.14 | 否 |
| B-dbg-2 / B-def-2 | steady | 20.52 | 18.55 | -1.97 | 否 |
| B-dbg-2 / B-def-2 | (d) | 100.1 | 96.90 | -3.21 | 否 |
| B-dbg-2 / B-def-2 | (e) | 11.20 | 8.42 | -2.78 | 否 |
| A-dbg-2 / A-def-2 | cold | 248.6 | 167.3 | -81.31 | 否 |
| A-dbg-2 / A-def-2 | steady | 97.49 | 114.5 | 17.02 | 否 |
| A-dbg-2 / A-def-2 | (d) | 205.8 | 212.2 | 6.40 | 否 |
| A-dbg-2 / A-def-2 | (e) | 106.6 | 107.1 | 0.59 | 否 |
| B-dbg-f256 / B-def-f256 | cold | 41.33 | 36.64 | -4.69 | 否 |
| B-dbg-f256 / B-def-f256 | steady | 25.30 | 14.96 | -10.34 | 否 |
| B-dbg-f256 / B-def-f256 | (d) | 95.51 | 98.04 | 2.53 | 否 |
| B-dbg-f256 / B-def-f256 | (e) | 12.09 | 8.76 | -3.33 | 否 |
| A-dbg-f256 / A-def-f256 | cold | 234.7 | 138.2 | -96.53 | 否 |
| A-dbg-f256 / A-def-f256 | steady | 98.32 | 92.83 | -5.49 | 否 |
| A-dbg-f256 / A-def-f256 | (d) | 123.4 | 94.61 | -28.79 | 否 |
| A-dbg-f256 / A-def-f256 | (e) | 114.7 | 97.84 | -16.83 | 否 |

## 5. 与基线、M-1 对照

### 5.1 主要数字（三方）

- 列的来源：
  - 前三列是 TASK-03 的基线（`raw/baseline-1.summary.json`、`raw/clean-1.summary.json`、`raw/clean-2.summary.json`）；
  - 其后四列是 M-1 默认级别组（`raw/m1-*.summary.json`）；
  - 再后是 M-2 的默认级别组、debug 组、窗口 256 的四次与控制运行。
- 读法：判定序列，「样本 / p50 / p99 / p99.9 / max」，单位 ms。带 * 的 n < 1000（p99.9 即 max），带 ** 的 n < 100（p99 也即 max）。窗口 256 只填 (e) 相关行。

| 量 | base-A | base-B-1 | base-B-2 | M1-A-def-1 | M1-A-def-2 | M1-B-def-1 | M1-B-def-2 | M2-A-def-1 | M2-A-def-2 | M2-B-def-1 | M2-B-def-2 | M2-A-dbg-1 | M2-A-dbg-2 | M2-B-dbg-1 | M2-B-dbg-2 | M2-A-def-f256 | M2-B-def-f256 | M2-A-dbg-f256 | M2-B-dbg-f256 | M2-B-def-g0 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 稳态窗实际包速率（包/秒，目标 400） | 4.7 | 400.0 | 400.0 | 400.2 | 400.1 | 400.0 | 400.0 | 400.1 | 400.0 | 400.0 | 400.1 | 400.0 | 399.9 | 400.0 | 400.0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 400.0 |
| 发出滞后（实际 − 计划） | 30** / 0.00 / 0.00 / 0.00 / 0.00 | 47424 / 0.05 / 2.34 / 5.72 / 17.80 | 47432 / 0.25 / 2.25 / 3.62 / 35.09 | 45854 / 0.95 / 40.48 / 74.28 / 136.7 | 45882 / 0.38 / 43.40 / 89.37 / 124.8 | 47434 / 0.20 / 2.22 / 2.74 / 13.45 | 47430 / 0.07 / 2.18 / 3.26 / 39.20 | 45846 / 0.28 / 52.03 / 126.4 / 187.0 | 45860 / 0.24 / 44.40 / 86.25 / 200.6 | 47430 / 0.00 / 2.22 / 2.91 / 39.42 | 47446 / 0.08 / 2.13 / 2.90 / 23.18 | 45849 / 0.20 / 51.97 / 92.37 / 161.4 | 45858 / 0.78 / 62.01 / 126.4 / 185.4 | 47426 / 0.05 / 2.12 / 3.37 / 25.03 | 47434 / 0.14 / 2.09 / 2.48 / 10.83 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47432 / 0.07 / 2.30 / 46.98 / 123.3 |
| 稳态窗全部计入的请求 | 19230（截尾 19200） / 170864.1 / 229761.2 / 230860.7 / ≥230961.2 | 47388 / 0.27 / 2.79 / 24.51 / 267.5 | 47396 / 0.43 / 2.73 / 20.58 / 41.45 | 45818 / 1.80 / 91.65 / 124.2 / 165.2 | 45846 / 1.45 / 94.56 / 136.7 / 174.9 | 47398 / 0.43 / 16.58 / 40.91 / 60.37 | 47394 / 0.32 / 18.38 / 47.71 / 89.19 | 45810 / 1.75 / 103.9 / 177.3 / 237.0 | 45824 / 1.20 / 97.49 / 136.3 / 223.4 | 47394 / 0.35 / 26.07 / 48.18 / 72.02 | 47410 / 0.39 / 20.52 / 47.10 / 58.54 | 45813 / 1.22 / 103.9 / 143.3 / 171.2 | 45822 / 1.52 / 114.5 / 177.9 / 240.3 | 47390 / 0.39 / 16.33 / 49.40 / 58.51 | 47398 / 0.41 / 18.55 / 40.84 / 59.81 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47396 / 0.47 / 22.28 / 84.19 / 164.4 |
| 参考：稳态窗 | 30** / 498.5 / 32366.4 / 32366.4 / 32366.4 | 47388 / 0.18 / 0.48 / 24.11 / 267.5 | 47396 / 0.18 / 0.56 / 19.90 / 40.54 | 45818 / 0.27 / 88.41 / 123.4 / 165.2 | 45846 / 0.22 / 89.24 / 125.3 / 174.9 | 47398 / 0.19 / 16.09 / 40.90 / 60.06 | 47394 / 0.18 / 17.69 / 47.49 / 89.01 | 45810 / 0.25 / 99.52 / 144.7 / 220.3 | 45824 / 0.24 / 93.01 / 135.7 / 169.8 | 47394 / 0.21 / 25.63 / 47.91 / 58.68 | 47410 / 0.20 / 20.28 / 46.81 / 57.68 | 45813 / 0.24 / 100.2 / 141.8 / 171.2 | 45822 / 0.27 / 105.5 / 159.4 / 240.3 | 47390 / 0.22 / 16.09 / 49.40 / 58.51 | 47398 / 0.20 / 18.27 / 40.46 / 58.97 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47396 / 0.25 / 17.97 / 52.28 / 134.2 |
| 稳态 CHECK_USER_CONNECTION | 2** / 0.11 / 0.15 / 0.15 / 0.15 | 26921 / 0.24 / 2.49 / 6.46 / 42.79 | 26882 / 0.43 / 2.47 / 4.21 / 36.30 | 25583 / 1.77 / 91.19 / 123.8 / 164.1 | 25436 / 1.38 / 94.67 / 142.2 / 174.9 | 26768 / 0.43 / 15.78 / 40.93 / 60.37 | 26872 / 0.30 / 17.41 / 45.15 / 89.19 | 25512 / 1.59 / 102.7 / 171.0 / 236.3 | 25490 / 1.07 / 95.33 / 137.2 / 200.5 | 26794 / 0.31 / 21.44 / 45.30 / 54.99 | 26791 / 0.34 / 17.87 / 42.60 / 55.48 | 25510 / 1.11 / 102.9 / 142.4 / 167.5 | 25556 / 1.45 / 118.0 / 177.9 / 234.7 | 26801 / 0.33 / 15.07 / 46.08 / 58.51 | 26742 / 0.37 / 16.04 / 38.94 / 51.21 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 26810 / 0.45 / 19.25 / 74.84 / 164.4 |
| 稳态 SET_USER_POSITION | 1** / 0.16 / 0.16 / 0.16 / 0.16 | 20215 / 0.30 / 2.89 / 25.93 / 55.98 | 20266 / 0.43 / 2.83 / 18.95 / 35.80 | 18413 / 1.72 / 90.28 / 131.6 / 165.2 | 18598 / 1.35 / 93.12 / 123.6 / 161.1 | 20383 / 0.43 / 12.75 / 40.90 / 56.47 | 20273 / 0.35 / 16.01 / 52.57 / 87.84 | 18470 / 1.74 / 102.1 / 180.1 / 237.0 | 18513 / 1.13 / 94.53 / 129.7 / 223.4 | 20351 / 0.39 / 23.72 / 47.64 / 57.48 | 20375 / 0.44 / 18.47 / 46.59 / 58.54 | 18477 / 1.16 / 100.1 / 142.4 / 167.3 | 18444 / 1.49 / 105.7 / 160.6 / 234.0 | 20338 / 0.45 / 11.83 / 49.10 / 55.30 | 20409 / 0.44 / 15.70 / 39.34 / 53.62 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 20338 / 0.50 / 20.61 / 103.2 / 164.3 |
| 稳态 SET_USER_AREA | 0 | 0 | 0 | 1142 / 21.15 / 106.2 / 132.1 / 146.4 | 1142 / 22.57 / 108.3 / 138.9 / 143.3 | 0 | 0 | 1145 / 29.67 / 117.5 / 185.2 / 235.2 | 1141 / 27.83 / 110.1 / 140.5 / 146.6 | 0 | 0 | 1144 / 28.51 / 129.2 / 166.1 / 169.2 | 1143 / 28.73 / 127.0 / 176.5 / 182.9 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 |
| 稳态 SELECT_DUNGEON | 0 | 36** / 16.68 / 267.5 / 267.5 / 267.5 | 35** / 16.27 / 21.42 / 21.42 / 21.42 | 36** / 10.11 / 74.58 / 74.58 / 74.58 | 34** / 10.52 / 77.47 / 77.47 / 77.47 | 35** / 16.21 / 23.61 / 23.61 / 23.61 | 35** / 15.87 / 22.47 / 22.47 / 22.47 | 36** / 10.48 / 74.80 / 74.80 / 74.80 | 36** / 9.96 / 67.81 / 67.81 / 67.81 | 35** / 15.13 / 28.89 / 28.89 / 28.89 | 34** / 14.81 / 21.06 / 21.06 / 21.06 | 36** / 9.83 / 75.87 / 75.87 / 75.87 | 36** / 10.55 / 71.46 / 71.46 / 71.46 | 36** / 15.37 / 21.87 / 21.87 / 21.87 | 35** / 15.10 / 20.21 / 20.21 / 20.21 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 35** / 15.50 / 23.38 / 23.38 / 23.38 |
| 稳态 SELECT_CHARACTER | 16** / 30664.1 / 32366.4 / 32366.4 / 32366.4 | 0 | 0 | 197* / 4.27 / 76.39 / 83.46 / 83.46 | 197* / 4.18 / 70.26 / 107.3 / 107.3 | 0 | 0 | 197* / 67.92 / 176.6 / 220.3 / 220.3 | 197* / 68.11 / 164.7 / 169.8 / 169.8 | 0 | 0 | 197* / 68.92 / 167.3 / 171.2 / 171.2 | 197* / 72.06 / 187.6 / 240.3 / 240.3 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 |
| 稳态 CREATE_CHARACTER | 3** / 266.8 / 30222.8 / 30222.8 / 30222.8 | 0 | 0 | 35** / 3.40 / 93.05 / 93.05 / 93.05 | 35** / 3.22 / 111.5 / 111.5 / 111.5 | 0 | 0 | 35** / 3.41 / 108.4 / 108.4 / 108.4 | 35** / 3.40 / 92.27 / 92.27 / 92.27 | 0 | 0 | 35** / 3.52 / 67.90 / 67.90 / 67.90 | 35** / 3.16 / 112.5 / 112.5 / 112.5 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 |
| (f) 最长单次停顿（判定；参考） | ≥284314.1；– | 575.9；575.9 | 1118.5；1118.5 | 921.2；139.2 | 1189.6；1189.6 | 133.0；133.0 | 132.4；132.4 | 968.5；968.5 | 1053.1；212.2 | 236.3；236.3 | 247.4；247.4 | 1020.2；115.1 | 1367.6；1367.6 | 232.4；232.4 | 230.9；230.9 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 229.8；229.8 |
| (f) 冷启动窗整体 | 8880（截尾 6834） / 249010.8 / 282660.7 / 284010.7 / ≥284314.1 | 22347 / 0.27 / 4.07 / 26.46 / 575.9 | 22265 / 0.42 / 4.20 / 66.45 / 1118.5 | 22019 / 1.81 / 217.7 / 818.2 / 921.2 | 21917 / 1.51 / 177.3 / 612.9 / 1189.6 | 22559 / 0.45 / 31.25 / 80.94 / 133.0 | 22533 / 0.33 / 30.71 / 79.91 / 132.4 | 21925 / 1.83 / 151.1 / 530.2 / 968.5 | 21957 / 1.39 / 248.6 / 889.2 / 1053.1 | 22513 / 0.38 / 40.92 / 105.4 / 236.3 | 22455 / 0.39 / 39.67 / 108.2 / 247.4 | 22003 / 1.29 / 286.9 / 950.3 / 1020.2 | 21916 / 1.52 / 167.3 / 654.2 / 1367.6 | 22521 / 0.39 / 40.28 / 88.92 / 232.4 | 22539 / 0.42 / 41.81 / 102.1 / 230.9 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 22544 / 0.79 / 38.14 / 104.1 / 229.8 |
| (d) 10 并发登录轮内的背景请求 | 12167（截尾 12144） / 69864.1 / 108164.1 / 108861.2 / ≥108914.1 | 3081 / 0.17 / 208.5 / 871.0 / 1025.1 | 3072 / 0.28 / 2.95 / 566.5 / 705.0 | 540* / 15.40 / 71.46 / 89.35 / 89.35 | 531* / 39.33 / 133.4 / 133.7 / 133.7 | 477* / 32.88 / 96.49 / 101.3 / 101.3 | 473* / 31.40 / 98.29 / 99.83 / 99.83 | 519* / 33.35 / 138.8 / 180.8 / 180.8 | 595* / 37.56 / 205.8 / 237.2 / 237.2 | 474* / 6.90 / 84.27 / 85.01 / 85.01 | 504* / 9.06 / 100.1 / 102.4 / 102.4 | 592* / 27.26 / 114.2 / 122.0 / 122.0 | 527* / 37.90 / 212.2 / 218.1 / 218.1 | 470* / 15.11 / 90.32 / 96.89 / 96.89 | 488* / 24.99 / 96.90 / 108.8 / 108.8 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 509* / 7.13 / 99.71 / 101.9 / 101.9 |
| 参考：(d) 轮内 | 23** / 1186.5 / 32304.4 / 32304.4 / 32304.4 | 3081 / 0.08 / 0.19 / 605.5 / 1025.1 | 3072 / 0.09 / 0.19 / 13.83 / 703.2 | 540* / 9.91 / 69.83 / 87.34 / 87.34 | 531* / 14.55 / 124.8 / 124.9 / 124.9 | 477* / 18.82 / 89.30 / 96.42 / 96.42 | 473* / 3.13 / 87.85 / 97.43 / 97.43 | 519* / 25.66 / 133.1 / 133.2 / 133.2 | 595* / 11.66 / 185.4 / 204.8 / 204.8 | 474* / 1.10 / 80.55 / 82.34 / 82.34 | 504* / 3.49 / 90.77 / 102.4 / 102.4 | 592* / 15.45 / 111.8 / 121.0 / 121.0 | 527* / 21.62 / 90.26 / 100.4 / 100.4 | 470* / 5.18 / 87.52 / 89.09 / 89.09 | 488* / 10.43 / 89.97 / 101.8 / 101.8 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 509* / 2.39 / 90.09 / 90.29 / 90.29 |
| (d) 领队连接轮内（判定） | 0 | 915* / 0.17 / 621.4 / 1025.1 / 1025.1 | 912* / 0.47 / 316.8 / 705.0 / 705.0 | 168* / 13.81 / 71.46 / 80.78 / 80.78 | 166* / 47.07 / 133.7 / 133.7 / 133.7 | 147* / 32.02 / 93.10 / 101.3 / 101.3 | 144* / 29.85 / 98.32 / 98.37 / 98.37 | 168* / 33.03 / 169.8 / 180.8 / 180.8 | 184* / 40.94 / 227.4 / 237.2 / 237.2 | 139* / 8.33 / 84.82 / 85.01 / 85.01 | 151* / 8.15 / 99.42 / 100.5 / 100.5 | 182* / 27.09 / 112.8 / 114.1 / 114.1 | 166* / 32.00 / 194.2 / 194.7 / 194.7 | 138* / 15.86 / 84.51 / 86.65 / 86.65 | 145* / 24.99 / 95.35 / 106.9 / 106.9 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 151* / 7.40 / 99.71 / 101.7 / 101.7 |
| (e) 灌包期间其余连接 | 4805（截尾 4800） / 16964.1 / 31664.1 / 31961.2 / ≥31964.1 | 11860 / 0.17 / 2.71 / 13.41 / 26.66 | 11875 / 0.30 / 2.71 / 12.09 / 28.08 | 11483 / 1.55 / 89.65 / 140.8 / 185.7 | 11497 / 1.21 / 86.34 / 113.8 / 124.9 | 11862 / 0.37 / 4.94 / 23.07 / 29.74 | 11861 / 0.18 / 4.42 / 24.95 / 29.54 | 11472 / 1.88 / 112.8 / 178.9 / 201.7 | 11470 / 1.29 / 106.6 / 145.8 / 227.3 | 11858 / 0.42 / 9.77 / 34.20 / 41.80 | 11869 / 0.59 / 11.20 / 31.55 / 47.42 | 11466 / 1.38 / 110.7 / 138.5 / 151.1 | 11476 / 1.48 / 107.1 / 159.4 / 188.6 | 11861 / 0.47 / 7.45 / 30.88 / 39.62 | 11878 / 0.54 / 8.42 / 28.50 / 40.12 | 11490 / 1.22 / 114.7 / 164.2 / 191.8 | 11864 / 0.44 / 12.09 / 35.96 / 47.05 | 11478 / 0.86 / 97.84 / 135.8 / 181.3 | 11862 / 0.51 / 8.76 / 29.85 / 42.24 | 11869 / 0.94 / 17.84 / 37.63 / 45.72 |
| 参考：(e) | 5** / 2.12 / 30119.3 / 30119.3 / 30119.3 | 11860 / 0.07 / 0.50 / 11.39 / 26.66 | 11875 / 0.07 / 0.43 / 11.42 / 27.14 | 11483 / 0.22 / 83.76 / 139.9 / 185.7 | 11497 / 0.18 / 84.05 / 113.3 / 120.8 | 11862 / 0.08 / 4.58 / 21.29 / 29.24 | 11861 / 0.08 / 4.08 / 24.87 / 29.12 | 11472 / 0.28 / 104.3 / 177.3 / 201.7 | 11470 / 0.20 / 91.10 / 115.0 / 124.9 | 11858 / 0.10 / 9.77 / 33.55 / 40.03 | 11869 / 0.10 / 10.59 / 30.31 / 46.38 | 11466 / 0.21 / 104.5 / 138.3 / 148.9 | 11476 / 0.23 / 102.3 / 157.5 / 188.6 | 11861 / 0.10 / 7.00 / 30.60 / 38.32 | 11878 / 0.10 / 7.50 / 28.49 / 37.91 | 11490 / 0.18 / 102.1 / 162.7 / 191.8 | 11864 / 0.10 / 12.09 / 35.70 / 46.87 | 11478 / 0.26 / 91.11 / 135.7 / 181.3 | 11862 / 0.10 / 8.01 / 29.61 / 41.62 | 11869 / 0.11 / 16.49 / 35.30 / 42.65 |
| (e) 灌包连接自身应答速率（包/秒） | 199107 | 196658 | 195702 | 90443 | 92402 | 151416 | 152916 | 57043 | 59856 | 77863 | 77852 | 44414 | 46384 | 63942 | 64377 | 60165 | 77670 | 46169 | 62974 | 77055 |
| 命令超阈值项（p99.9 > 100 或 max > 250 ms） | 21 | 7 | 8 | 1 | 1 | 1 | 0 | 1 | 1 | 1 | 1 | 1 | 1 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 1 |
| 失败的命令 | 113 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 |
| 判定口径 > 250 ms 的请求 | 45372 | 27 | 27 | 213 | 168 | 0 | 0 | 157 | 219 | 0 | 0 | 231 | 163 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 |
| 错误（断线、超时） | 51 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 双人组进本次数 | 0 | 79 | 78 | 80 | 78 | 79 | 78 | 80 | 80 | 81 | 79 | 82 | 79 | 80 | 79 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 78 |
| Host 日志行数 | 3016 | 3498 | 3554 | 14363 | 14397 | 3595 | 3604 | 14261 | 14229 | 3509 | 3506 | 1467706 | 1526637 | 2040776 | 2053900 | 14223 | 3484 | 1521090 | 2011952 | 3492 |

读法：
- **构成 A**：
  - 稳态端到端 p99 从 M-1 的 91.7／94.6 ms 变为 97.5／103.9 ms（debug 103.9／114.5 ms）；
  - (e) 其余连接从 86.3／89.7 ms 变为 106.6／112.8 ms；
  - (f) 与 M-1 同量级。
- **构成 B**：
  - 稳态端到端 p99 从 16.6／18.4 ms 变为 20.5／26.1 ms（debug 16.3／18.6 ms，与 M-1 debug 14.7／15.8 ms 接近）；
  - (e) 其余连接从 4.4／4.9 ms 变为 9.8／11.2 ms（G1）。同一口径对照（控制运行用 G0，与 M-1 默认级别组、基线同一个 dll）：M-1 4.4～4.9 ms → M-2 17.84 ms（单次运行），约 3.6～4 倍（17.84 ÷ 4.94 ＝ 3.61，17.84 ÷ 4.42 ＝ 4.04）；相对基线 2.71 ms 约 6.6 倍（17.84 ÷ 2.71 ＝ 6.58）。G0 只跑了 1 次，可能含噪声。都远低于 2 s 与 50 ms，不构成停止；
  - (f) 从 132.4／133.0 ms 变为 236.3／247.4 ms，原因见 §6 第 3 条。
- **选角的端到端**：构成 A 稳态 SELECT_CHARACTER 的 p50 从 4.27／4.18 ms 变为 67.9／68.1 ms，SET_USER_AREA 的 p50 从 21.2／22.6 ms 变为 29.7／27.8 ms。这是 VC-1 的口径：首条回包推迟到作业结束（README「端到端延迟」节），不是退化。
- **灌包连接自身的应答速率**：
  - 构成 B 默认级别从 15.1～15.3 万包/秒降到 7.8 万包/秒，构成 A 从 9.0～9.2 万降到 5.7～6.0 万；
  - debug 级的降幅较小，B 8.5 万 → 6.4 万；
  - 只作信息，登记为 F-120。

### 5.2 控制运行（基线 dll 与 M-1 最终 dll）

| 量 | B-def-1（G1） | B-def-2（G1） | B-def-g0（G0） |
|---|---:|---:|---:|
| 稳态窗全部计入的请求 | 47394 / 0.35 / 26.07 / 48.18 / 72.02 | 47410 / 0.39 / 20.52 / 47.10 / 58.54 | 47396 / 0.47 / 22.28 / 84.19 / 164.4 |
| 参考：稳态窗 | 47394 / 0.21 / 25.63 / 47.91 / 58.68 | 47410 / 0.20 / 20.28 / 46.81 / 57.68 | 47396 / 0.25 / 17.97 / 52.28 / 134.2 |
| (f) 最长单次停顿（判定；参考） | 236.3；236.3 | 247.4；247.4 | 229.8；229.8 |
| (d) 背景请求 | 474* / 6.90 / 84.27 / 85.01 / 85.01 | 504* / 9.06 / 100.1 / 102.4 / 102.4 | 509* / 7.13 / 99.71 / 101.9 / 101.9 |
| (e) 其余连接 | 11858 / 0.42 / 9.77 / 34.20 / 41.80 | 11869 / 0.59 / 11.20 / 31.55 / 47.42 | 11869 / 0.94 / 17.84 / 37.63 / 45.72 |
| (e) 灌包连接自身应答速率 | 77863 | 77852 | 77055 |
| PrepareSelectedCharacterStateCommand max | 102.1 | 110.0 | 102.7 |
| EnterDungeonCommand p50 / max | 8.57 / 57.06 | 8.36 / 78.57 | 8.82 / 55.24 |
| 错误／失败命令 | 0／0 | 0／0 | 0／0 |
steady p99: G1 runs 26.07 / 20.52 (gap 5.55 ms); G0 22.28 ms; G0 within [min-gap, max+gap]: True
(d) background p99: G1 runs 84.27 / 100.1 (gap 15.84 ms); G0 99.71 ms; G0 within [min-gap, max+gap]: True
(f) longest stall: G1 runs 236.3 / 247.4 (gap 11.14 ms); G0 229.8 ms; G0 within [min-gap, max+gap]: True
(e) others p99: G1 runs 9.77 / 11.20 (gap 1.43 ms); G0 17.84 ms; G0 within [min-gap, max+gap]: False

- **判据**（§4.1）：控制运行与两次 G1 运行的差异，要落在两次 G1 运行之间的差距之内（`[较小值 − 差距, 较大值 ＋ 差距]`）。判定脚本逐项打印，见上表之后的四行。
- **稳态、(d)、(f) 落在差距之内**：
  - 稳态端到端 p99：G0 22.28 ms，G1 为 26.07／20.52 ms，差距 5.55 ms；
  - (d) 背景请求 p99：G0 99.71 ms，G1 为 84.27／100.1 ms，差距 15.84 ms；
  - (f) 最长停顿：G0 229.8 ms，G1 为 236.3／247.4 ms，差距 11.14 ms。
- **(e) 不在差距之内**：其余连接的 p99，G0 17.84 ms，G1 为 9.77／11.20 ms，差距只有 1.43 ms。
- **结论**：**没有证明两个 dll 在默认级别下等价**（按裁定第 3 条，只有差异落在两次 G1 运行的差距之内才算等价）。
  - (e) 的差异方向是 G0 较高；G0 只跑了 1 次，可能含噪声，不能据此判断哪个 dll 更慢。
  - 因此 (e) 与基线、M-1 的对照同时写两种口径：G1（本单元的默认级别组）与 G0（与 M-1 默认级别组、基线同一个 dll）。见 §5.1 与 F-120。
  - 控制运行的冷启动窗与稳态窗受 P3 换页污染，(d)、(e) 窗干净。

### 5.3 执行器作业（基线不存在）

各类作业的执行时长（M-2 六次 debug 运行，「样本 / p50 / p99 / p99.9 / max；首次；冷启动窗 max／之后 max」），最后一列是 M-1 四次的 p50 / max：

| 作业 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-1 p50 / max（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| `inbound:GameSelectCharacter` | 484* / 68.33 / 130.0 / 229.6 / 229.6；首次 229.6；冷 229.6／后 131.6 | 484* / 69.37 / 142.6 / 239.1 / 239.1；首次 227.7；冷 227.7／后 239.1 | 21** / 75.77 / 227.5 / 227.5 / 227.5；首次 227.5；冷 227.5／后 – | 21** / 69.45 / 229.9 / 229.9 / 229.9；首次 229.9；冷 229.9／后 – | 484* / 66.59 / 133.3 / 226.8 / 226.8；首次 226.8；冷 226.8／后 135.6 | 21** / 69.64 / 227.8 / 227.8 / 227.8；首次 227.8；冷 227.8／后 – | 67.79 / 236.1；64.99 / 229.5；70.49 / 232.8；70.80 / 233.7 |
| `inbound:GameSetUserArea` | 2658 / 23.24 / 52.52 / 64.15 / 75.64；首次 10.69；冷 57.80／后 75.64 | 2655 / 22.56 / 52.97 / 76.37 / 117.2；首次 10.04；冷 58.60／后 117.2 | 12** / 14.17 / 36.86 / 36.86 / 36.86；首次 15.96；冷 36.86／后 – | 12** / 14.46 / 24.14 / 24.14 / 24.14；首次 10.44；冷 24.14／后 – | 2658 / 23.07 / 53.54 / 60.74 / 63.85；首次 11.17；冷 58.44／后 63.85 | 12** / 13.73 / 39.83 / 39.83 / 39.83；首次 10.78；冷 39.83／后 – | 23.18 / 73.85；23.11 / 101.7；11.77 / 32.87；12.79 / 20.25 |
| `inbound:GameEplpCommand` | 78** / 45.37 / 77.21 / 77.21 / 77.21；首次 40.92；冷 58.08／后 77.21 | 78** / 40.77 / 96.86 / 96.86 / 96.86；首次 50.58；冷 50.58／后 96.86 | 78** / 34.56 / 59.52 / 59.52 / 59.52；首次 48.89；冷 59.25／后 59.52 | 78** / 37.62 / 58.64 / 58.64 / 58.64；首次 48.19；冷 57.56／后 58.64 | 79** / 45.13 / 109.2 / 109.2 / 109.2；首次 59.98；冷 59.98／后 109.2 | 78** / 33.97 / 63.66 / 63.66 / 63.66；首次 49.56；冷 52.28／后 63.66 | 45.62 / 97.99；45.85 / 88.55；33.22 / 59.31；38.76 / 59.06 |
| `inbound:GameSelectDungeon` | 82** / 9.93 / 77.26 / 77.26 / 77.26；首次 77.26；冷 77.26／后 22.42 | 79** / 10.23 / 73.22 / 73.22 / 73.22；首次 73.22；冷 73.22／后 22.70 | 80** / 14.59 / 72.64 / 72.64 / 72.64；首次 72.64；冷 72.64／后 21.72 | 79** / 14.90 / 73.48 / 73.48 / 73.48；首次 73.48；冷 73.48／后 19.97 | 80** / 10.00 / 75.78 / 75.78 / 75.78；首次 75.78；冷 75.78／后 27.35 | 78** / 14.46 / 72.84 / 72.84 / 72.84；首次 72.84；冷 72.84／后 23.25 | 9.64 / 71.06；10.02 / 77.57；14.65 / 74.75；14.31 / 76.81 |
| `inbound:GameEnterSelectDungeon` | 82** / 21.20 / 45.90 / 45.90 / 45.90；首次 32.07；冷 40.07／后 45.90 | 79** / 20.28 / 56.15 / 56.15 / 56.15；首次 28.42；冷 29.14／后 56.15 | 80** / 24.01 / 62.25 / 62.25 / 62.25；首次 62.25；冷 62.25／后 46.40 | 79** / 25.85 / 53.37 / 53.37 / 53.37；首次 53.37；冷 53.37／后 53.21 | 80** / 21.24 / 57.51 / 57.51 / 57.51；首次 25.42；冷 25.76／后 57.51 | 78** / 18.66 / 62.39 / 62.39 / 62.39；首次 62.39；冷 62.39／后 38.81 | 22.57 / 44.13；23.75 / 46.57；23.75 / 46.56；22.46 / 62.75 |
| `inbound:GameCreateCharacter` | 94** / 1.66 / 41.56 / 41.56 / 41.56；首次 41.56；冷 41.56／后 10.18 | 94** / 1.79 / 31.09 / 31.09 / 31.09；首次 31.09；冷 31.09／后 4.38 | 21** / 1.69 / 31.45 / 31.45 / 31.45；首次 31.45；冷 31.45／后 – | 21** / 1.61 / 32.06 / 32.06 / 32.06；首次 32.06；冷 32.06／后 – | 94** / 1.75 / 35.19 / 35.19 / 35.19；首次 35.19；冷 35.19／后 5.46 | 21** / 1.54 / 29.81 / 29.81 / 29.81；首次 29.81；冷 29.81／后 – | 1.70 / 31.59；1.78 / 30.81；1.55 / 31.58；1.55 / 32.57 |
| `inbound:GameSetUserPosition` | 41887 / 0.01 / 0.12 / 1.84 / 18.88；首次 1.80；冷 4.22／后 18.88 | 41738 / 0.01 / 0.11 / 2.58 / 16.67；首次 1.80；冷 16.67／后 15.61 | 45986 / 0.03 / 0.14 / 2.94 / 22.74；首次 1.90；冷 4.51／后 22.74 | 46151 / 0.02 / 0.09 / 3.16 / 5.96；首次 1.76；冷 4.38／后 5.96 | 41901 / 0.01 / 0.12 / 2.00 / 20.50；首次 1.87；冷 20.50／后 9.35 | 46069 / 0.02 / 0.10 / 3.15 / 6.95；首次 1.78；冷 6.95／后 6.14 | 0.02 / 13.97；0.02 / 5.57；0.02 / 6.12；0.02 / 4.67 |
| `event:southern-dale.daily-refill-notifications` | 484* / 0.41 / 6.03 / 8.56 / 8.56；首次 5.14；冷 5.14／后 8.56 | 484* / 0.42 / 6.21 / 18.49 / 18.49；首次 5.26；冷 18.49／后 11.24 | 21** / 0.75 / 5.44 / 5.44 / 5.44；首次 5.34；冷 5.44／后 – | 21** / 0.75 / 5.32 / 5.32 / 5.32；首次 5.02；冷 5.32／后 – | 484* / 0.41 / 4.56 / 5.89 / 5.89；首次 5.12；冷 5.53／后 5.89 | 21** / 0.79 / 7.97 / 7.97 / 7.97；首次 5.27；冷 7.97／后 – | 0.41 / 6.61；0.38 / 7.67；0.74 / 5.27；0.66 / 6.58 |
| `inbound:GameResponsePeer` | 82** / 0.18 / 8.43 / 8.43 / 8.43；首次 8.43；冷 8.43／后 2.26 | 82** / 0.16 / 9.28 / 9.28 / 9.28；首次 9.28；冷 9.28／后 1.39 | 82** / 0.38 / 17.11 / 17.11 / 17.11；首次 17.11；冷 17.11／后 0.73 | 84** / 0.35 / 17.24 / 17.24 / 17.24；首次 17.24；冷 17.24／后 0.52 | 80** / 0.16 / 9.45 / 9.45 / 9.45；首次 9.45；冷 9.45／后 2.75 | 81** / 0.35 / 12.03 / 12.03 / 12.03；首次 12.03；冷 12.03／后 2.33 | 0.19 / 8.21；0.23 / 11.68；0.41 / 15.01；0.39 / 16.63 |
| `inbound:GameRequestPeer` | 82** / 0.47 / 4.25 / 4.25 / 4.25；首次 4.25；冷 4.25／后 1.25 | 82** / 0.51 / 4.74 / 4.74 / 4.74；首次 4.74；冷 4.74／后 3.91 | 82** / 2.06 / 16.53 / 16.53 / 16.53；首次 16.53；冷 16.53／后 3.83 | 84** / 2.30 / 16.52 / 16.52 / 16.52；首次 16.52；冷 16.52／后 3.23 | 81** / 0.49 / 6.86 / 6.86 / 6.86；首次 6.86；冷 6.86／后 2.65 | 81** / 2.22 / 11.30 / 11.30 / 11.30；首次 11.30；冷 11.30／后 3.14 | 0.48 / 4.43；0.52 / 7.28；2.01 / 17.36；2.29 / 17.48 |
| `lifecycle:disconnected` | 81** / 0.01 / 1.84 / 1.84 / 1.84；首次 1.78；冷 1.78／后 1.84 | 81** / 0.01 / 2.25 / 2.25 / 2.25；首次 2.25；冷 2.25／后 1.96 | 81** / 0.01 / 14.62 / 14.62 / 14.62；首次 1.73；冷 1.73／后 14.62 | 81** / 0.01 / 16.50 / 16.50 / 16.50；首次 1.83；冷 1.83／后 16.50 | 81** / 0.01 / 3.49 / 3.49 / 3.49；首次 3.28；冷 3.28／后 3.49 | 81** / 0.01 / 14.38 / 14.38 / 14.38；首次 1.78；冷 1.78／后 14.38 | 0.01 / 6.68；0.01 / 10.06；0.01 / 8.22；0.01 / 14.21 |
| `inbound:GameConnectionCheck` | 1395238 / 0.00 / 0.01 / 0.04 / 10.67；首次 0.11；冷 3.41／后 10.67 | 1454387 / 0.00 / 0.01 / 0.04 / 6.88；首次 0.13；冷 1.42／后 6.88 | 1980459 / 0.00 / 0.02 / 0.04 / 8.76；首次 0.13；冷 3.48／后 8.76 | 1993412 / 0.00 / 0.01 / 0.03 / 7.45；首次 0.14；冷 4.33／后 7.45 | 1448632 / 0.00 / 0.01 / 0.03 / 9.18；首次 0.11；冷 9.18／后 8.23 | 1951583 / 0.00 / 0.01 / 0.03 / 15.55；首次 0.13；冷 3.85／后 15.55 | 0.00 / 7.19；0.00 / 6.89；0.00 / 3.83；0.00 / 3.43 |
| `http:POST /api/launcher/v1/register` | 81** / 0.11 / 14.28 / 14.28 / 14.28；首次 14.28；冷 14.28／后 0.46 | 81** / 0.09 / 11.99 / 11.99 / 11.99；首次 11.99；冷 11.99／后 0.48 | 81** / 0.15 / 7.23 / 7.23 / 7.23；首次 7.23；冷 7.23／后 0.87 | 81** / 0.12 / 6.98 / 6.98 / 6.98；首次 6.98；冷 6.98／后 0.89 | 81** / 0.11 / 15.00 / 15.00 / 15.00；首次 15.00；冷 15.00／后 0.47 | 81** / 0.14 / 6.93 / 6.93 / 6.93；首次 6.93；冷 6.93／后 0.87 | 0.10 / 17.35；0.11 / 13.84；0.14 / 8.87；0.17 / 8.92 |
| `inbound:GameReturnToSelection` | 463* / 0.46 / 3.18 / 11.09 / 11.09；首次 11.09；冷 11.09／后 6.34 | 463* / 0.44 / 3.24 / 12.02 / 12.02；首次 12.02；冷 12.02／后 5.84 | – | – | 463* / 0.45 / 2.98 / 10.26 / 10.26；首次 10.26；冷 10.26／后 3.64 | – | 0.42 / 12.40；0.45 / 10.30；–；– |
| `timer:PremiumDailyRefreshService` | 4** / 0.86 / 4.68 / 4.68 / 4.68；首次 4.68；冷 4.68／后 1.59 | 4** / 0.55 / 4.26 / 4.26 / 4.26；首次 4.26；冷 4.26／后 0.73 | 4** / 1.19 / 11.65 / 11.65 / 11.65；首次 11.65；冷 11.65／后 2.32 | 4** / 1.54 / 9.24 / 9.24 / 9.24；首次 9.24；冷 9.24／后 3.33 | 4** / 0.63 / 5.91 / 5.91 / 5.91；首次 5.91；冷 5.91／后 0.84 | 4** / 2.39 / 7.07 / 7.07 / 7.07；首次 7.07；冷 7.07／后 2.94 | 0.74 / 6.68；0.52 / 6.74；3.01 / 6.20；2.36 / 12.27 |
| `inbound:GameLeaveParty` | 78** / 0.19 / 8.85 / 8.85 / 8.85；首次 8.85；冷 8.85／后 1.09 | 78** / 0.18 / 10.06 / 10.06 / 10.06；首次 10.06；冷 10.06／后 1.23 | 78** / 0.55 / 11.43 / 11.43 / 11.43；首次 11.43；冷 11.43／后 3.57 | 78** / 0.55 / 10.42 / 10.42 / 10.42；首次 10.42；冷 10.42／后 1.25 | 77** / 0.21 / 6.43 / 6.43 / 6.43；首次 6.43；冷 6.43／后 0.84 | 78** / 0.57 / 9.84 / 9.84 / 9.84；首次 9.84；冷 9.84／后 1.07 | 0.22 / 9.98；0.21 / 14.82；0.62 / 15.17；0.65 / 15.80 |
| `inbound:GameLogin` | 81** / 0.10 / 9.45 / 9.45 / 9.45；首次 9.45；冷 9.45／后 5.59 | 81** / 0.10 / 9.67 / 9.67 / 9.67；首次 9.67；冷 9.67／后 0.29 | 81** / 0.14 / 9.32 / 9.32 / 9.32；首次 9.32；冷 9.32／后 7.72 | 81** / 0.12 / 9.00 / 9.00 / 9.00；首次 9.00；冷 9.00／后 0.30 | 81** / 0.11 / 10.35 / 10.35 / 10.35；首次 10.35；冷 10.35／后 0.22 | 81** / 0.14 / 9.07 / 9.07 / 9.07；首次 9.07；冷 9.07／后 0.33 | 0.10 / 13.46；0.11 / 18.38；0.15 / 12.32；0.14 / 12.75 |
| `timer:CreatureClockService` | 276* / 0.14 / 0.96 / 6.05 / 6.05；首次 6.05；冷 6.05／后 2.15 | 276* / 0.15 / 2.75 / 6.46 / 6.46；首次 6.46；冷 6.46／后 5.63 | 277* / 0.02 / 1.39 / 8.43 / 8.43；首次 8.43；冷 8.43／后 1.70 | 277* / 0.03 / 1.19 / 5.94 / 5.94；首次 5.94；冷 5.94／后 1.28 | 276* / 0.13 / 0.77 / 6.49 / 6.49；首次 6.49；冷 6.49／后 1.08 | 277* / 0.02 / 1.23 / 6.16 / 6.16；首次 6.16；冷 6.16／后 1.26 | 0.16 / 7.11；0.15 / 7.34；0.02 / 6.82；0.02 / 7.09 |
| `event:achievement.inventory` | 940* / 0.26 / 2.94 / 7.61 / 7.61；首次 7.06；冷 7.06／后 7.61 | 940* / 0.25 / 4.84 / 7.35 / 7.35；首次 7.35；冷 7.35／后 5.97 | 210* / 0.33 / 3.35 / 7.15 / 7.15；首次 7.15；冷 7.15／后 – | 210* / 0.32 / 4.07 / 8.12 / 8.12；首次 7.05；冷 8.12／后 – | 940* / 0.25 / 2.13 / 7.07 / 7.07；首次 7.07；冷 7.07／后 5.72 | 210* / 0.34 / 2.92 / 7.88 / 7.88；首次 7.17；冷 7.88／后 – | 0.24 / 11.75；0.24 / 7.12；0.32 / 7.67；0.31 / 7.04 |
| `event:quest.inventory` | 940* / 0.10 / 0.79 / 4.76 / 4.76；首次 4.76；冷 4.76／后 1.30 | 940* / 0.10 / 0.55 / 5.01 / 5.01；首次 5.01；冷 5.01／后 4.96 | 210* / 0.11 / 3.22 / 4.61 / 4.61；首次 4.61；冷 4.61／后 – | 210* / 0.10 / 1.21 / 4.67 / 4.67；首次 4.67；冷 4.67／后 – | 940* / 0.10 / 0.97 / 6.72 / 6.72；首次 5.07；冷 6.72／后 2.60 | 210* / 0.11 / 1.55 / 4.71 / 4.71；首次 4.71；冷 4.71／后 – | 0.09 / 1.70；0.09 / 5.13；0.11 / 7.59；0.10 / 5.02 |
| `timer:ScheduledTaskHostedService/character.name-tag.expiration` | 4** / 0.33 / 2.49 / 2.49 / 2.49；首次 2.49；冷 2.49／后 0.80 | 5** / 0.46 / 1.99 / 1.99 / 1.99；首次 1.99；冷 1.99／后 0.51 | 5** / 1.62 / 2.88 / 2.88 / 2.88；首次 2.00；冷 2.00／后 2.88 | 5** / 2.01 / 6.65 / 6.65 / 6.65；首次 6.65；冷 6.65／后 2.32 | 5** / 0.57 / 2.21 / 2.21 / 2.21；首次 2.21；冷 2.21／后 0.82 | 5** / 1.81 / 5.62 / 5.62 / 5.62；首次 5.62；冷 5.62／后 2.00 | 0.49 / 2.03；0.40 / 2.23；0.31 / 7.00；0.90 / 7.23 |
| `event:altar.departure` | 164* / 0.12 / 0.82 / 1.87 / 1.87；首次 1.87；冷 1.87／后 0.82 | 158* / 0.11 / 0.80 / 1.19 / 1.19；首次 0.46；冷 0.46／后 1.19 | 160* / 0.15 / 0.64 / 0.92 / 0.92；首次 0.58；冷 0.92／后 0.64 | 158* / 0.14 / 0.41 / 0.59 / 0.59；首次 0.59；冷 0.59／后 0.41 | 160* / 0.11 / 1.84 / 1.96 / 1.96；首次 1.96；冷 1.96／后 1.84 | 156* / 0.14 / 0.66 / 6.05 / 6.05；首次 0.53；冷 0.53／后 6.05 | 0.10 / 2.09；0.11 / 1.84；0.13 / 0.58；0.13 / 2.03 |
| `http:POST /api/launcher/v1/login` | 81** / 0.04 / 2.86 / 2.86 / 2.86；首次 2.86；冷 2.86／后 0.37 | 81** / 0.05 / 5.69 / 5.69 / 5.69；首次 5.69；冷 5.69／后 0.18 | 81** / 0.07 / 2.77 / 2.77 / 2.77；首次 2.77；冷 2.77／后 0.25 | 81** / 0.07 / 2.62 / 2.62 / 2.62；首次 2.62；冷 2.62／后 0.27 | 81** / 0.04 / 2.99 / 2.99 / 2.99；首次 2.99；冷 2.99／后 0.27 | 81** / 0.07 / 2.54 / 2.54 / 2.54；首次 2.54；冷 2.54／后 0.26 | 0.04 / 2.95；0.05 / 5.56；0.06 / 2.21；0.05 / 2.20 |
| `inbound:GameFinishLoading` | 164* / 0.10 / 1.96 / 3.27 / 3.27；首次 3.27；冷 3.27／后 0.19 | 158* / 0.08 / 3.32 / 5.65 / 5.65；首次 3.32；冷 5.65／后 0.24 | 160* / 0.10 / 1.48 / 3.40 / 3.40；首次 3.40；冷 3.40／后 0.94 | 158* / 0.10 / 2.10 / 3.01 / 3.01；首次 3.01；冷 3.01／后 1.68 | 160* / 0.08 / 1.93 / 3.10 / 3.10；首次 3.10；冷 3.10／后 0.21 | 156* / 0.10 / 1.81 / 3.24 / 3.24；首次 3.24；冷 3.24／后 0.34 | 0.08 / 4.01；0.08 / 4.46；0.09 / 3.97；0.09 / 4.04 |
| `timer:MirrorQuotaDailyRefreshService` | 4** / 0.31 / 1.03 / 1.03 / 1.03；首次 1.03；冷 1.03／后 0.35 | 4** / 0.09 / 5.60 / 5.60 / 5.60；首次 1.02；冷 1.02／后 5.60 | 4** / 0.03 / 2.31 / 2.31 / 2.31；首次 2.31；冷 2.31／后 0.44 | 4** / 0.14 / 2.33 / 2.33 / 2.33；首次 2.33；冷 2.33／后 0.19 | 4** / 0.07 / 1.43 / 1.43 / 1.43；首次 1.43；冷 1.43／后 0.32 | 4** / 0.05 / 1.97 / 1.97 / 1.97；首次 1.97；冷 1.97／后 0.35 | 0.10 / 1.10；0.07 / 1.19；0.05 / 1.33；0.03 / 2.68 |
| `timer:HerasQuotaDailyRefreshService` | 4** / 0.08 / 0.49 / 0.49 / 0.49；首次 0.49；冷 0.49／后 0.26 | 4** / 0.07 / 5.13 / 5.13 / 5.13；首次 0.51；冷 0.51／后 5.13 | 4** / 0.01 / 3.17 / 3.17 / 3.17；首次 3.17；冷 3.17／后 0.02 | 4** / 0.02 / 4.65 / 4.65 / 4.65；首次 4.65；冷 4.65／后 0.03 | 4** / 0.08 / 0.71 / 0.71 / 0.71；首次 0.71；冷 0.71／后 0.08 | 4** / 0.02 / 0.88 / 0.88 / 0.88；首次 0.88；冷 0.88／后 0.02 | 0.07 / 0.71；0.07 / 0.66；0.01 / 0.79；0.03 / 1.12 |
| `timer:JoustMaintenanceService` | 276* / 0.07 / 0.42 / 1.27 / 1.27；首次 0.12；冷 0.38／后 1.27 | 276* / 0.07 / 0.30 / 0.93 / 0.93；首次 0.11；冷 0.30／后 0.93 | 277* / 0.23 / 0.57 / 4.30 / 4.30；首次 0.17；冷 4.30／后 0.60 | 277* / 0.27 / 0.45 / 0.70 / 0.70；首次 0.15；冷 0.70／后 0.42 | 276* / 0.05 / 0.34 / 2.31 / 2.31；首次 0.11；冷 0.25／后 2.31 | 277* / 0.27 / 0.43 / 0.64 / 0.64；首次 0.13；冷 0.64／后 0.40 | 0.06 / 0.59；0.05 / 0.53；0.31 / 3.68；0.30 / 1.92 |
| `lifecycle:connected` | 82** / 0.01 / 4.29 / 4.29 / 4.29；首次 4.29；冷 4.29／后 0.07 | 82** / 0.01 / 3.95 / 3.95 / 3.95；首次 3.95；冷 3.95／后 0.14 | 82** / 0.01 / 4.17 / 4.17 / 4.17；首次 4.17；冷 4.17／后 0.04 | 82** / 0.01 / 3.99 / 3.99 / 3.99；首次 3.99；冷 3.99／后 0.05 | 82** / 0.01 / 3.96 / 3.96 / 3.96；首次 3.96；冷 3.96／后 0.05 | 82** / 0.02 / 3.97 / 3.97 / 3.97；首次 3.97；冷 3.97／后 0.06 | 0.01 / 4.23；0.01 / 4.22；0.01 / 4.16；0.01 / 4.22 |
| `timer:WeaknessRecoveryService` | 4** / 0.38 / 1.53 / 1.53 / 1.53；首次 1.49；冷 1.49／后 1.53 | 4** / 0.31 / 1.72 / 1.72 / 1.72；首次 1.34；冷 1.34／后 1.72 | 4** / 0.86 / 3.65 / 3.65 / 3.65；首次 2.62；冷 2.62／后 3.65 | 4** / 0.46 / 4.15 / 4.15 / 4.15；首次 4.15；冷 4.15／后 3.50 | 4** / 0.68 / 2.91 / 2.91 / 2.91；首次 2.91；冷 2.91／后 1.76 | 4** / 0.78 / 3.59 / 3.59 / 3.59；首次 2.77；冷 2.77／后 3.59 | 0.48 / 2.39；0.37 / 2.23；1.07 / 5.11；1.52 / 5.23 |
| `timer:DungeonPartyWipeService` | 1105 / 0.01 / 0.04 / 1.03 / 2.60；首次 2.60；冷 2.60／后 1.03 | 1104 / 0.01 / 0.04 / 0.27 / 2.55；首次 2.55；冷 2.55／后 0.08 | 1108 / 0.01 / 0.07 / 0.40 / 1.38；首次 1.38；冷 1.38／后 0.35 | 1108 / 0.01 / 0.06 / 1.31 / 4.14；首次 1.31；冷 1.31／后 4.14 | 1105 / 0.01 / 0.03 / 0.91 / 2.66；首次 2.66；冷 2.66／后 0.91 | 1108 / 0.01 / 0.06 / 0.59 / 1.25；首次 1.25；冷 1.25／后 0.06 | 0.01 / 1.32；0.01 / 7.01；0.01 / 1.24；0.01 / 1.24 |
| `timer:ItemUsageDailyRefreshService` | 4** / 0.21 / 0.67 / 0.67 / 0.67；首次 0.67；冷 0.67／后 0.51 | 4** / 0.16 / 2.03 / 2.03 / 2.03；首次 2.03；冷 2.03／后 0.16 | 4** / 0.02 / 2.22 / 2.22 / 2.22；首次 2.22；冷 2.22／后 0.54 | 4** / 0.01 / 3.63 / 3.63 / 3.63；首次 3.63；冷 3.63／后 0.05 | 4** / 0.20 / 0.84 / 0.84 / 0.84；首次 0.84；冷 0.84／后 0.20 | 4** / 0.04 / 3.55 / 3.55 / 3.55；首次 3.55；冷 3.55／后 0.59 | 0.18 / 0.70；0.18 / 1.58；0.01 / 0.98；0.01 / 3.61 |
| `timer:MarriageWeddingScheduler` | 276* / 0.03 / 0.18 / 1.75 / 1.75；首次 1.75；冷 1.75／后 0.20 | 276* / 0.02 / 0.13 / 1.78 / 1.78；首次 1.78；冷 1.78／后 0.14 | 277* / 0.10 / 0.32 / 3.55 / 3.55；首次 1.81；冷 3.55／后 0.32 | 277* / 0.10 / 0.19 / 1.86 / 1.86；首次 1.86；冷 1.86／后 0.19 | 276* / 0.02 / 0.24 / 1.81 / 1.81；首次 1.81；冷 1.81／后 0.31 | 277* / 0.10 / 0.19 / 1.86 / 1.86；首次 1.86；冷 1.86／后 0.18 | 0.02 / 2.03；0.02 / 1.94；0.11 / 2.03；0.11 / 2.09 |
| `event:quest.mirror-leave` | 164* / 0.12 / 0.77 / 1.49 / 1.49；首次 0.77；冷 0.77／后 1.49 | 158* / 0.11 / 1.51 / 2.33 / 2.33；首次 2.33；冷 2.33／后 1.51 | 160* / 0.15 / 0.68 / 3.09 / 3.09；首次 3.09；冷 3.09／后 0.44 | 158* / 0.15 / 1.57 / 2.71 / 2.71；首次 2.71；冷 2.71／后 1.57 | 160* / 0.11 / 0.71 / 0.82 / 0.82；首次 0.82；冷 0.82／后 0.44 | 156* / 0.14 / 0.63 / 3.07 / 3.07；首次 3.07；冷 3.07／后 0.63 | 0.11 / 0.86；0.11 / 1.18；0.14 / 2.73；0.14 / 0.84 |
| `timer:FatigueDailyRefreshService` | 4** / 0.61 / 1.08 / 1.08 / 1.08；首次 1.08；冷 1.08／后 0.75 | 4** / 0.15 / 0.95 / 0.95 / 0.95；首次 0.95；冷 0.95／后 0.46 | 4** / 0.42 / 2.90 / 2.90 / 2.90；首次 2.90；冷 2.90／后 2.29 | 4** / 0.50 / 2.54 / 2.54 / 2.54；首次 2.54；冷 2.54／后 1.15 | 4** / 0.18 / 1.46 / 1.46 / 1.46；首次 1.46；冷 1.46／后 0.50 | 4** / 1.50 / 2.74 / 2.74 / 2.74；首次 2.15；冷 2.15／后 2.74 | 0.28 / 1.19；0.26 / 1.30；0.56 / 1.50；0.55 / 2.37 |
| `timer:ReviveCoinQuotaDailyRefreshService` | 4** / 0.10 / 0.65 / 0.65 / 0.65；首次 0.65；冷 0.65／后 0.30 | 4** / 0.09 / 0.53 / 0.53 / 0.53；首次 0.53；冷 0.53／后 0.10 | 4** / 0.01 / 1.37 / 1.37 / 1.37；首次 1.37；冷 1.37／后 0.02 | 4** / 0.28 / 1.93 / 1.93 / 1.93；首次 1.93；冷 1.93／后 0.33 | 4** / 0.10 / 0.87 / 0.87 / 0.87；首次 0.87；冷 0.87／后 0.11 | 4** / 0.01 / 0.88 / 0.88 / 0.88；首次 0.88；冷 0.88／后 0.03 | 0.10 / 1.51；0.10 / 0.69；0.01 / 0.89；0.02 / 1.19 |
| `timer:GoldSilverHornQuotaDailyRefreshService` | 4** / 0.06 / 0.49 / 0.49 / 0.49；首次 0.49；冷 0.49／后 0.21 | 4** / 0.10 / 1.16 / 1.16 / 1.16；首次 1.16；冷 1.16／后 0.43 | 4** / 0.01 / 1.35 / 1.35 / 1.35；首次 1.35；冷 1.35／后 0.01 | 4** / 0.02 / 1.38 / 1.38 / 1.38；首次 1.38；冷 1.38／后 0.03 | 4** / 0.09 / 1.84 / 1.84 / 1.84；首次 1.84；冷 1.84／后 0.50 | 4** / 0.01 / 0.77 / 0.77 / 0.77；首次 0.77；冷 0.77／后 0.05 | 0.06 / 0.57；0.05 / 0.71；0.02 / 0.71；0.11 / 1.36 |
| `timer:BossTowerReturnSweepService` | 553* / 0.00 / 0.02 / 1.57 / 1.57；首次 1.02；冷 1.57／后 0.02 | 553* / 0.01 / 0.02 / 1.01 / 1.01；首次 1.01；冷 1.01／后 0.02 | 554* / 0.01 / 0.04 / 1.09 / 1.09；首次 1.09；冷 1.09／后 0.14 | 554* / 0.01 / 0.02 / 0.76 / 0.76；首次 0.76；冷 0.76／后 0.02 | 553* / 0.00 / 0.02 / 1.04 / 1.04；首次 1.04；冷 1.04／后 0.02 | 554* / 0.01 / 0.03 / 0.79 / 0.79；首次 0.79；冷 0.79／后 0.04 | 0.00 / 1.03；0.00 / 0.75；0.01 / 0.92；0.01 / 0.77 |
| `timer:TournamentQuotaDailyRefreshService` | 4** / 0.06 / 1.56 / 1.56 / 1.56；首次 1.56；冷 1.56／后 0.21 | 4** / 0.06 / 0.47 / 0.47 / 0.47；首次 0.47；冷 0.47／后 0.06 | 4** / 0.01 / 0.77 / 0.77 / 0.77；首次 0.77；冷 0.77／后 0.01 | 4** / 0.02 / 1.21 / 1.21 / 1.21；首次 1.21；冷 1.21／后 0.03 | 4** / 0.06 / 0.63 / 0.63 / 0.63；首次 0.63；冷 0.63／后 0.06 | 4** / 0.04 / 0.76 / 0.76 / 0.76；首次 0.76；冷 0.76／后 0.04 | 0.06 / 0.56；0.05 / 0.58；0.01 / 0.69；0.01 / 1.44 |
| `event:tournament.departure` | 164* / 0.10 / 0.25 / 0.33 / 0.33；首次 0.16；冷 0.16／后 0.33 | 158* / 0.09 / 0.19 / 0.29 / 0.29；首次 0.16；冷 0.16／后 0.29 | 160* / 0.12 / 0.29 / 0.97 / 0.97；首次 0.22；冷 0.22／后 0.97 | 158* / 0.13 / 0.61 / 1.06 / 1.06；首次 0.21；冷 1.06／后 0.61 | 160* / 0.10 / 1.55 / 1.55 / 1.55；首次 0.25；冷 0.80／后 1.55 | 156* / 0.12 / 0.23 / 0.69 / 0.69；首次 0.19；冷 0.69／后 0.23 | 0.09 / 0.34；0.09 / 0.19；0.11 / 0.60；0.12 / 0.27 |
| `timer:AltarQuotaDailyRefreshService` | 4** / 0.35 / 0.53 / 0.53 / 0.53；首次 0.53；冷 0.53／后 0.35 | 4** / 0.07 / 0.53 / 0.53 / 0.53；首次 0.53；冷 0.53／后 0.08 | 4** / 0.01 / 1.45 / 1.45 / 1.45；首次 1.45；冷 1.45／后 0.04 | 4** / 0.02 / 1.29 / 1.29 / 1.29；首次 1.29；冷 1.29／后 0.03 | 4** / 0.09 / 0.81 / 0.81 / 0.81；首次 0.81；冷 0.81／后 0.09 | 4** / 0.02 / 0.99 / 0.99 / 0.99；首次 0.99；冷 0.99／后 0.03 | 0.10 / 2.60；0.09 / 0.76；0.05 / 0.94；0.01 / 1.53 |
| `timer:DimensionQuotaDailyRefreshService` | 4** / 0.05 / 0.43 / 0.43 / 0.43；首次 0.43；冷 0.43／后 0.15 | 4** / 0.05 / 0.42 / 0.42 / 0.42；首次 0.42；冷 0.42／后 0.05 | 4** / 0.02 / 0.68 / 0.68 / 0.68；首次 0.68；冷 0.68／后 0.04 | 4** / 0.01 / 1.38 / 1.38 / 1.38；首次 1.38；冷 1.38／后 0.02 | 4** / 0.05 / 0.70 / 0.70 / 0.70；首次 0.70；冷 0.70／后 0.06 | 4** / 0.02 / 0.61 / 0.61 / 0.61；首次 0.61；冷 0.61／后 0.02 | 0.05 / 0.53；0.05 / 0.58；0.12 / 1.89；0.01 / 4.51 |
| `timer:TournamentRewardSweepService` | 5463 / 0.00 / 0.01 / 0.03 / 0.57；首次 0.57；冷 0.57／后 0.08 | 5434 / 0.00 / 0.01 / 0.02 / 0.51；首次 0.51；冷 0.51／后 0.02 | 5537 / 0.01 / 0.02 / 0.06 / 0.58；首次 0.58；冷 0.58／后 0.10 | 5536 / 0.00 / 0.01 / 0.02 / 0.59；首次 0.59；冷 0.59／后 0.03 | 5452 / 0.00 / 0.01 / 0.02 / 1.05；首次 0.55；冷 0.55／后 1.05 | 5539 / 0.00 / 0.01 / 0.02 / 0.55；首次 0.55；冷 0.55／后 0.03 | 0.00 / 0.67；0.00 / 0.87；0.00 / 0.48；0.00 / 0.53 |
| `timer:IllusionTowerReturnSweepService` | 553* / 0.00 / 0.02 / 0.80 / 0.80；首次 0.80；冷 0.80／后 0.02 | 553* / 0.00 / 0.02 / 0.79 / 0.79；首次 0.79；冷 0.79／后 0.02 | 554* / 0.01 / 0.04 / 0.87 / 0.87；首次 0.87；冷 0.87／后 0.09 | 554* / 0.01 / 0.03 / 0.92 / 0.92；首次 0.92；冷 0.92／后 0.04 | 553* / 0.00 / 0.02 / 0.80 / 0.80；首次 0.80；冷 0.80／后 0.50 | 554* / 0.01 / 0.03 / 1.05 / 1.05；首次 1.05；冷 1.05／后 0.02 | 0.00 / 0.78；0.00 / 0.94；0.01 / 0.77；0.01 / 0.92 |
| `timer:LoginSessionMaintenanceService` | 9** / 0.01 / 0.12 / 0.12 / 0.12；首次 0.12；冷 0.12／后 0.02 | 9** / 0.01 / 0.15 / 0.15 / 0.15；首次 0.15；冷 0.15／后 0.01 | 9** / 0.02 / 0.25 / 0.25 / 0.25；首次 0.25；冷 0.25／后 0.05 | 9** / 0.02 / 0.36 / 0.36 / 0.36；首次 0.36；冷 0.36／后 0.03 | 9** / 0.01 / 0.16 / 0.16 / 0.16；首次 0.16；冷 0.16／后 0.01 | 9** / 0.02 / 0.17 / 0.17 / 0.17；首次 0.17；冷 0.17／后 0.03 | 0.01 / 0.10；0.01 / 0.10；0.02 / 0.18；0.02 / 0.17 |
| `timer:RoomMechanismMaintenanceService` | 162* / 0.00 / 0.04 / 0.06 / 0.06；首次 0.04；冷 0.06／后 0.02 | 158* / 0.00 / 0.03 / 0.05 / 0.05；首次 0.05；冷 0.05／后 0.02 | 160* / 0.00 / 0.05 / 0.05 / 0.05；首次 0.05；冷 0.05／后 0.05 | 158* / 0.00 / 0.04 / 0.05 / 0.05；首次 0.04；冷 0.05／后 0.02 | 160* / 0.00 / 0.01 / 0.04 / 0.04；首次 0.04；冷 0.04／后 0.01 | 156* / 0.00 / 0.02 / 0.05 / 0.05；首次 0.05；冷 0.05／后 0.02 | 0.00 / 0.05；0.00 / 0.05；0.00 / 0.04；0.00 / 0.06 |
| `timer:NotificationRetryDrainService` | – | – | – | – | – | – | 0.00 / 0.17；0.00 / 0.16；0.00 / 0.22；0.00 / 0.22 |

单条作业执行时长，按全部作业类型合计（含 ramp-A-2s）：

| 运行 | 作业数 | p99 | p99.9 | max | max 的作业 | inbound 作业数 | inbound p99 | inbound p99.9 | inbound max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A-dbg-1 | 1453489 | 0.03 | 26.89 | 229.6 | inbound:GameSelectCharacter | 1441553 | 0.02 | 27.01 | 229.6 |
| A-dbg-2 | 1512423 | 0.03 | 25.56 | 239.1 | inbound:GameSelectCharacter | 1500538 | 0.02 | 25.64 | 239.1 |
| B-dbg-1 | 2037318 | 0.04 | 0.29 | 227.5 | inbound:GameSelectCharacter | 2027220 | 0.04 | 0.14 | 227.5 |
| B-dbg-2 | 2050426 | 0.03 | 0.27 | 229.9 | inbound:GameSelectCharacter | 2040338 | 0.03 | 0.10 | 229.9 |
| A-dbg-f256 | 1506869 | 0.03 | 26.23 | 226.8 | inbound:GameSelectCharacter | 1494950 | 0.02 | 26.35 | 226.8 |
| B-dbg-f256 | 2008502 | 0.03 | 0.26 | 227.8 | inbound:GameSelectCharacter | 1998417 | 0.03 | 0.10 | 227.8 |
| ramp-A-2s | 100389 | 24.85 | 98.84 | 230.2 | inbound:GameSelectCharacter | 94135 | 25.38 | 102.0 | 230.2 |
(host.log stamps are millisecond wall clock; readiness from summary.json originUtc (ramps: first ramp line - 50 s); M-1 values: m1-summary.md §5.3 A 24.96/24.98 p99.9, 236.1/229.5 max; B 0.12/0.12, 232.8/233.7)

入队等待（四个窗口；全部作业，括号内为只算 `inbound:` 的 p99 / max），最后一列是 M-1 的 p99：

| 窗口 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-1 p99（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 (f) | 26197 / 0.04 / 103.1 / 400.5 / 567.3（inbound 100.1 / 567.2）；max：lifecycle:connected | 26100 / 0.05 / 105.0 / 633.7 / 1366.4（inbound 103.8 / 1304.4）；max：event:quest.inventory | 25426 / 0.01 / 40.52 / 87.88 / 231.8（inbound 31.26 / 111.7）；max：event:achievement.inventory | 25427 / 0.01 / 36.49 / 105.0 / 231.1（inbound 29.56 / 120.8）；max：event:achievement.inventory | 26164 / 0.07 / 102.7 / 413.1 / 921.7（inbound 100.0 / 921.7）；max：inbound:GameConnectionCheck | 25440 / 0.01 / 36.40 / 85.14 / 228.7（inbound 29.05 / 121.2）；max：event:achievement.inventory | 99.49；93.18；33.21；37.46 |
| 稳态窗 (c) | 52993 / 0.04 / 94.31 / 135.4 / 166.9（inbound 94.08 / 166.9）；max：inbound:GameConnectionCheck | 52902 / 0.04 / 99.75 / 157.3 / 240.0（inbound 99.61 / 225.4）；max：event:achievement.inventory | 52089 / 0.01 / 16.56 / 45.61 / 57.91（inbound 12.74 / 57.91）；max：inbound:GameConnectionCheck | 52088 / 0.01 / 19.01 / 39.92 / 53.48（inbound 15.00 / 53.48）；max：inbound:GameSetUserPosition | 52955 / 0.05 / 83.37 / 129.5 / 158.8（inbound 81.46 / 157.7）；max：timer:TournamentRewardSweepService | 52088 / 0.01 / 14.42 / 43.09 / 62.37（inbound 10.99 / 62.37）；max：inbound:GameConnectionCheck | 86.10；86.49；16.22；17.14 |
| (d) 轮内 | 1001 / 0.21 / 100.0 / 111.4 / 111.4（inbound 94.32 / 111.4）；max：inbound:GameLogin | 998* / 0.13 / 180.9 / 187.1 / 187.1（inbound 181.0 / 187.1）；max：inbound:GameConnectionCheck | 858* / 0.03 / 1.00 / 3.50 / 3.50（inbound 1.00 / 1.00）；max：timer:TournamentRewardSweepService | 891* / 0.06 / 25.92 / 88.17 / 88.17（inbound 17.88 / 28.91）；max：timer:TournamentRewardSweepService | 980* / 0.22 / 90.52 / 100.5 / 100.5（inbound 90.52 / 90.54）；max：event:quest.mirror-leave | 851* / 0.06 / 1.82 / 100.3 / 100.3（inbound 1.82 / 1.84）；max：timer:TournamentRewardSweepService | 74.09；100.8；11.64；17.16 |
| (e) 灌包期间 | 1345625 / 0.00 / 0.01 / 40.22 / 144.4（inbound 0.01 / 144.4）；max：inbound:GameConnectionCheck | 1404744 / 0.00 / 0.01 / 37.30 / 165.5（inbound 0.01 / 165.5）；max：inbound:GameConnectionCheck | 1931495 / 0.00 / 0.01 / 0.05 / 38.13（inbound 0.01 / 38.13）；max：inbound:GameConnectionCheck | 1944591 / 0.00 / 0.01 / 0.04 / 37.65（inbound 0.01 / 37.65）；max：inbound:GameConnectionCheck | 1399059 / 0.00 / 0.01 / 33.91 / 181.2（inbound 0.01 / 181.2）；max：inbound:GameConnectionCheck | 1902667 / 0.00 / 0.01 / 0.05 / 41.44（inbound 0.01 / 41.44）；max：inbound:GameConnectionCheck | 0.01；0.01；0.01；0.01 |

执行器占用与每秒作业数：

| 窗口 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-1（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| cold | 41.9%（26197 个） | 42.3%（26100 个） | 7.5%（25426 个） | 7.2%（25427 个） | 42.2%（26164 个） | 7.0%（25440 个） | 42.1%；41.6%；7.1%；7.1% |
| steady | 38.9%（52993 个） | 39.3%（52902 个） | 3.6%（52089 个） | 3.4%（52088 个） | 38.6%（52955 个） | 3.2%（52088 个） | 39.0%；38.7%；3.3%；3.4% |
| burst | 56.2%（1001 个） | 49.9%（998 个） | 2.9%（858 个） | 7.3%（891 个） | 54.3%（980 个） | 2.4%（851 个） | 65.5%；49.6%；4.4%；4.7% |
| flood | 56.5%（1345625 个） | 55.5%（1404744 个） | 28.7%（1931495 个） | 28.3%（1944591 个） | 54.6%（1399059 个） | 28.1%（1902667 个） | 52.1%；51.7%；20.6%；21.0% |
| 稳态每秒作业数 | inbound: 400.6，timer: 31.7，event: 9.3 | inbound: 400.2，timer: 31.4，event: 9.2 | inbound: 400.5，timer: 31.8，event: 1.8 | inbound: 400.5，timer: 31.8，event: 1.8 | inbound: 400.4，timer: 31.7，event: 9.2 | inbound: 400.5，timer: 31.8，event: 1.8 | inbound: 400.5，timer: 32.7，event: 9.3；inbound: 400.5，timer: 32.7，event: 9.3；inbound: 400.5，timer: 32.8，event: 1.7；inbound: 400.5，timer: 32.8，event: 1.8 |

### 5.4 内层命令

`baseline-summary.md`「M-1 预警」表的 8 行（max，ms）：

| 命令 | 基线 B-1 / B-2 | M-1（8 次） | A-def-1 | A-def-2 | B-def-1 | B-def-2 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `EnterDungeonCommand` | 633.1 / 1103.9 | 54.08～61.60 | 60.26（n 80） | 55.64（n 80） | 57.06（n 81） | 78.57（n 79） | 61.03（n 82） | 58.40（n 79） | 55.55（n 80） | 55.00（n 79） |
| `LeaveDungeonCommand` | 1019.0 / 697.2 | 13.55～17.15 | 14.81（n 79） | 8.74（n 78） | 17.81（n 78） | 10.89（n 78） | 12.40（n 78） | 18.72（n 78） | 16.91（n 78） | 17.01（n 78） |
| `RegisterAccountCommand` | 665.7 / 668.0 | 6.15～12.65 | 10.07（n 81） | 12.07（n 81） | 4.72（n 81） | 7.72（n 81） | 9.29（n 81） | 6.75（n 81） | 4.65（n 81） | 4.46（n 81） |
| `AuthenticateAndIssueTicketCommand` | 637.5 / 631.7 | 1.89～4.84 | 7.32（n 81） | 2.59（n 81） | 2.34（n 81） | 2.38（n 81） | 2.53（n 81） | 4.98（n 81） | 2.40（n 81） | 2.31（n 81） |
| `RedeemGameLoginCommand` | 570.2 / 567.2 | 1.61～3.52 | 1.72（n 81） | 2.30（n 81） | 1.63（n 81） | 1.72（n 81） | 5.50（n 81） | 1.70（n 81） | 1.63（n 81） | 1.58（n 81） |
| `PrepareSelectedCharacterStateCommand` | 502.8 / 520.2 | 98.27～103.8 | 106.4（n 484） | 102.9（n 484） | 102.1（n 21） | 110.0（n 21） | 102.0（n 484） | 102.8（n 484） | 98.56（n 21） | 99.58（n 21） |
| `AdvanceCreatureClockCommand` | 346.0 / 280.2 | 3.07～3.33 | 3.37（n 776） | 3.19（n 766） | 4.92（n 339） | 3.47（n 335） | 3.09（n 774） | 5.48（n 776） | 5.23（n 337） | 3.05（n 335） |
| `ExpireCharacterNameTagCommand` | 1.56 / 613.0 | 0.40～1.80 | 0.65（n 105） | 0.47（n 84） | 5.51（n 84） | 1.09（n 105） | 0.53（n 84） | 0.40（n 105） | 2.09（n 88） | 1.42（n 105） |

选角作业内的命令（构成 A），最后一列是 M-1 四次的 p50：

| 命令 | A-def-1 | A-def-2 | A-dbg-1 | A-dbg-2 | M-1 p50（4 次） |
|---|---:|---:|---:|---:|---:|
| `PrepareSelectedCharacterStateCommand` | 484* / 3.70 / 13.30 / 106.4 / 106.4 | 484* / 3.68 / 13.36 / 102.9 / 102.9 | 484* / 3.71 / 12.82 / 102.0 / 102.0 | 484* / 3.52 / 12.63 / 102.8 / 102.8 | 3.75；3.55；3.67；3.74 |
| `ApplySelectedCharacterDailyRefillCommand` | 484* / 12.69 / 41.54 / 57.11 / 57.11 | 484* / 12.57 / 50.69 / 59.02 / 59.02 | 484* / 12.68 / 51.70 / 58.38 / 58.38 | 484* / 13.03 / 51.75 / 62.81 / 62.81 | 12.79；12.64；12.79；12.68 |
| `RefillOnlineSouthernTicketCommand` | 484* / 14.58 / 59.88 / 63.05 / 63.05 | 484* / 14.35 / 54.66 / 58.65 / 58.65 | 484* / 14.99 / 55.89 / 61.60 / 61.60 | 484* / 14.95 / 60.31 / 80.86 / 80.86 | 14.61；14.34；14.39；14.18 |
| `RefillOnlineBossTowerTicketCommand` | 484* / 15.14 / 47.00 / 53.06 / 53.06 | 484* / 14.79 / 45.70 / 55.11 / 55.11 | 484* / 15.17 / 58.42 / 61.11 / 61.11 | 484* / 15.20 / 61.29 / 71.26 / 71.26 | 14.78；14.86；14.86；14.65 |
| `RefillOnlineTauTicketCommand` | 484* / 16.21 / 57.82 / 72.05 / 72.05 | 484* / 16.49 / 57.99 / 72.82 / 72.82 | 484* / 16.21 / 61.06 / 62.18 / 62.18 | 484* / 17.49 / 61.74 / 66.37 / 66.37 | 16.13；16.14；16.10；16.66 |

内层命令 max 前 15：

| 命令 | A-def-1 | A-def-2 | B-def-1 | B-def-2 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-def-f256 | B-def-f256 | A-dbg-f256 | B-dbg-f256 | B-def-g0 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `PrepareSelectedCharacterStateCommand` | 106.4 | 102.9 | 102.1 | 110.0 | 102.0 | 102.8 | 98.56 | 99.58 | 101.0 | 100.6 | 99.02 | 101.6 | 102.7 |
| `RefillOnlineSouthernTicketCommand` | 63.05 | 58.65 | 57.49 | 55.89 | 61.60 | 80.86 | 35.26 | 21.14 | 59.52 | 46.49 | 59.27 | 54.99 | 35.60 |
| `EnterDungeonCommand` | 60.26 | 55.64 | 57.06 | 78.57 | 61.03 | 58.40 | 55.55 | 55.00 | 59.99 | 54.62 | 61.01 | 55.97 | 55.24 |
| `RefillOnlineTauTicketCommand` | 72.05 | 72.82 | 58.27 | 56.15 | 62.18 | 66.37 | 60.07 | 59.87 | 70.31 | 57.27 | 61.19 | 23.35 | 58.05 |
| `RefillOnlineBossTowerTicketCommand` | 53.06 | 55.11 | 40.23 | 47.94 | 61.11 | 71.26 | 36.99 | 25.63 | 60.68 | 50.91 | 58.87 | 43.93 | 39.73 |
| `ApplySelectedCharacterDailyRefillCommand` | 57.11 | 59.02 | 51.14 | 28.67 | 58.38 | 62.81 | 27.39 | 56.23 | 60.36 | 51.29 | 67.34 | 27.32 | 65.36 |
| `GetAccountCharacterRosterCommand` | 6.03 | 5.48 | 5.63 | 5.31 | 9.00 | 5.75 | 5.59 | 6.98 | 5.78 | 32.51 | 8.11 | 4.96 | 6.63 |
| `CreateCharacterCommand` | 24.27 | 32.42 | 23.94 | 24.84 | 30.40 | 23.24 | 23.78 | 23.15 | 23.98 | 23.51 | 24.45 | 22.88 | 22.80 |
| `LeaveDungeonCommand` | 14.81 | 8.74 | 17.81 | 10.89 | 12.40 | 18.72 | 16.91 | 17.01 | 8.41 | 17.16 | 13.88 | 16.84 | 15.35 |
| `RegisterAccountCommand` | 10.07 | 12.07 | 4.72 | 7.72 | 9.29 | 6.75 | 4.65 | 4.46 | 4.74 | 4.52 | 9.81 | 4.47 | 4.28 |
| `AuthenticateAndIssueTicketCommand` | 7.32 | 2.59 | 2.34 | 2.38 | 2.53 | 4.98 | 2.40 | 2.31 | 2.60 | 2.35 | 2.63 | 2.25 | 2.31 |
| `CompleteReturnToSelectionCommand` | 6.98 | 6.26 | – | – | 6.82 | 7.12 | – | – | 6.99 | – | 6.46 | – | – |
| `PublishSelectedCharacterSessionCommand` | 3.81 | 3.67 | 7.05 | 3.84 | 3.70 | 3.71 | 3.73 | 3.61 | 3.63 | 3.60 | 3.57 | 3.60 | 3.68 |
| `PrepareReturnToSelectionCommand` | 6.05 | 2.06 | – | – | 5.92 | 4.94 | – | – | 3.30 | – | 2.78 | – | – |
| `AcceptPartyInviteCommand` | 3.02 | 3.89 | 3.16 | 3.05 | 2.65 | 3.06 | 5.98 | 5.98 | 3.49 | 4.06 | 3.11 | 3.88 | 5.89 |

### 5.5 吞吐爬坡（b）

- 第一张是判 S3 的 2 s 口径爬坡（`m2-ramp-full-2s.md`），其后是构成 B 与 p99 ≤ 50 ms 口径的构成 A。
- 每份爬坡后附逐档的「档末 5 秒每秒发出滞后 max」与执行器占用（§1.4 的稳定通过档）。「趋势」一列是机械判据：5 个值中后一秒大于前一秒的至少 3 次，且末值大于首值，记为「上升」。

**ramp-B**（`ramp-clean`）：可持续吞吐 25005.0；停止原因 全部档位通过（未触顶，最高档即下限）；Host 日志中内层命令 max 100.2 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 判定（p99 ≤ 50 ms 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 7115 | 0.44 | 2.44 | 21.40 | 42.80 | 47.74 | 19.87 | 6（0.083%） | 19.02 | 19.02 | 0 | 通过 |
| 800 | 800 | 14320 | 0.28 | 1.56 | 22.88 | 47.10 | 57.74 | 21.53 | 6（0.042%） | 21.31 | 21.31 | 0 | 通过 |
| 1200 | 1200 | 21509 | 0.26 | 1.30 | 21.44 | 45.22 | 56.60 | 14.91 | 6（0.028%） | 20.20 | 20.20 | 0 | 通过 |
| 1600 | 1599 | 28703 | 0.22 | 1.21 | 22.90 | 46.91 | 59.34 | 12.97 | 6（0.021%） | 16.95 | 16.95 | 0 | 通过 |
| 2000 | 2000 | 35906 | 0.18 | 1.12 | 16.97 | 48.98 | 58.47 | 4.28 | 6（0.017%） | 17.67 | 17.67 | 0 | 通过 |
| 2400 | 2400 | 43105 | 0.19 | 1.17 | 20.73 | 48.43 | 58.93 | 3.10 | 6（0.014%） | 19.41 | 19.41 | 0 | 通过 |
| 3000 | 3000 | 53902 | 0.19 | 1.03 | 20.95 | 50.36 | 70.01 | 3.32 | 6（0.011%） | 15.12 | 15.12 | 0 | 通过 |
| 4000 | 4000 | 71905 | 0.20 | 1.28 | 19.62 | 43.91 | 53.82 | 3.76 | 5（0.007%） | 17.04 | 17.04 | 0 | 通过 |
| 5000 | 5000 | 89908 | 0.22 | 1.57 | 21.35 | 40.58 | 47.95 | 3.72 | 5（0.006%） | 16.86 | 16.86 | 0 | 通过 |
| 6000 | 6000 | 107906 | 0.26 | 2.06 | 18.39 | 37.26 | 48.44 | 4.17 | 6（0.006%） | 13.78 | 13.78 | 0 | 通过 |
| 8000 | 7999 | 143901 | 0.29 | 2.74 | 18.41 | 35.43 | 46.30 | 4.40 | 6（0.004%） | 14.75 | 14.75 | 0 | 通过 |
| 10000 | 9999 | 179910 | 0.32 | 3.49 | 17.29 | 34.64 | 41.27 | 4.98 | 5（0.003%） | 14.85 | 14.85 | 0 | 通过 |
| 12000 | 12000 | 215910 | 0.28 | 4.00 | 18.68 | 36.84 | 44.54 | 5.12 | 5（0.002%） | 14.13 | 14.13 | 0 | 通过 |
| 15000 | 15000 | 269918 | 0.26 | 4.49 | 20.47 | 35.53 | 44.05 | 5.24 | 5（0.002%） | 14.83 | 14.83 | 0 | 通过 |
| 20000 | 19995 | 359895 | 0.25 | 4.80 | 22.68 | 34.94 | 40.23 | 5.14 | 5（0.001%） | 14.32 | 14.32 | 0 | 通过 |
| 25000 | 25005 | 449924 | 0.34 | 6.54 | 30.45 | 40.30 | 48.41 | 6.18 | 5（0.001%） | 16.33 | 16.33 | 0 | 通过 |

ramp-B 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 通过 |  | 2.6 / 2.9 / 2.5 / 2.5 / 3.1 | 上升 |
| 800 | 800 | 通过 |  | 1.8 / 5.2 / 13.2 / 4.7 / 3.8 | 无上升趋势 |
| 1200 | 1200 | 通过 |  | 1.7 / 2.9 / 1.8 / 16.9 / 3.6 | 无上升趋势 |
| 1600 | 1599 | 通过 |  | 1.3 / 2.3 / 13.4 / 1.3 / 1.9 | 上升 |
| 2000 | 2000 | 通过 |  | 3.7 / 1.7 / 1.9 / 20.1 / 1.7 | 无上升趋势 |
| 2400 | 2400 | 通过 |  | 7.2 / 1.2 / 3.4 / 30.6 / 1.5 | 无上升趋势 |
| 3000 | 3000 | 通过 |  | 1.1 / 2.2 / 31.3 / 2.7 / 2.7 | 上升 |
| 4000 | 4000 | 通过 |  | 48.8 / 1.3 / 33.9 / 2.6 / 4.3 | 无上升趋势 |
| 5000 | 5000 | 通过 |  | 1.3 / 42.0 / 4.9 / 1.4 / 1.8 | 无上升趋势 |
| 6000 | 6000 | 通过 |  | 44.6 / 2.4 / 29.5 / 3.8 / 2.6 | 无上升趋势 |
| 8000 | 7999 | 通过 |  | 34.6 / 36.5 / 3.8 / 5.7 / 4.1 | 无上升趋势 |
| 10000 | 9999 | 通过 |  | 3.9 / 21.5 / 38.0 / 4.1 / 4.1 | 上升 |
| 12000 | 12000 | 通过 |  | 5.3 / 4.8 / 42.3 / 28.2 / 5.0 | 无上升趋势 |
| 15000 | 15000 | 通过 |  | 5.7 / 5.1 / 4.2 / 42.5 / 4.7 | 无上升趋势 |
| 20000 | 19995 | 通过 |  | 5.1 / 6.1 / 38.1 / 5.4 / 30.9 | 上升 |
| 25000 | 25005 | 通过 |  | 6.5 / 47.7 / 7.5 / 41.2 / 6.7 | 无上升趋势 |

**ramp-A-2s**（`ramp-A-2s`）：可持续吞吐 1169.0；停止原因 判定 max ≥ 2000 ms；Host 日志中内层命令 max 100.9 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 作业数 | 作业执行时长 max (ms) | max 的作业 | 入队等待 max (ms) | 判定（2 s 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 6824 | 1.85 | 64.90 | 100.9 | 169.7 | 177.0 | 97.24 | 51（0.708%） | 177.0 | 177.0 | 0 | 7958 | 134.2 | inbound:GameSelectCharacter | 172.0 | 通过 |
| 800 | 799 | 13836 | 12.17 | 92.34 | 118.3 | 167.5 | 176.8 | 103.7 | 75（0.521%） | 176.3 | 176.3 | 0 | 15255 | 108.3 | inbound:GameSelectCharacter | 175.8 | 通过 |
| 1200 | 1169 | 20801 | 222.1 | 792.9 | 1011.7 | 1169.5 | 1266.0 | 123.6 | 99（0.470%） | 232.6 | 232.6 | 0 | 23231 | 116.2 | inbound:GameSelectCharacter | 264.4 | 通过 |
| 1600 | 1144 | 27664 | 6131.1 | 10327.1 | 11094.3 | 11749.9 | 11881.5 | 125.1 | 99（0.481%） | 328.9 | 328.9 | 0 | 34252 | 119.8 | inbound:GameSelectCharacter | 303.7 | 停止 |

ramp-A-2s 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 通过 | 41.4% | 19.8 / 51.2 / 8.3 / 67.6 / 120.3 | 上升 |
| 800 | 799 | 通过 | 62.8% | 86.9 / 140.4 / 78.1 / 101.5 / 95.4 | 无上升趋势 |
| 1200 | 1169 | 通过 | 91.6% | 738.0 / 842.3 / 910.8 / 933.5 / 1210.0 | 上升 |
| 1600 | 1144 | 停止 | 98.7% | 9264.2 / 9608.2 / 10020.7 / 10947.6 / 11443.2 | 上升 |

**ramp-A**（`ramp-full`）：可持续吞吐 无通过档；停止原因 p99 > 50 ms；Host 日志中内层命令 max 100.5 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 判定（p99 ≤ 50 ms 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 6816 | 1.48 | 62.41 | 100.7 | 140.0 | 175.2 | 98.81 | 55（0.764%） | 138.8 | 138.8 | 0 | 停止 |

ramp-A 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 停止 |  | 32.7 / 156.7 / 125.2 / 50.0 / 76.2 | 无上升趋势 |

- **(b) 的两个数（§1.4）**：
  - 2 s 口径下构成 A 的可持续吞吐是 **1169 包/秒**（1200 档的实际速率；M-1 1188）。
  - **稳定通过档是 800**（实际 799 包/秒）：800 档档末 5 秒滞后 78～140 ms，无上升趋势，执行器占用 62.8%；1200 档滞后 738 → 1210 ms 持续上升，占用 91.6%。
  - 1169 包/秒与 M-1 的 1188 一样，只是 18 秒档长下的上限，不能当作余量依据。
- **构成 A 的过载点比 M-1 低一些**：
  - 1200 档的判定 p99 为 1011.7 ms，max 1266.0 ms（M-1：462.5／668.5 ms）；
  - 1600 档的实际速率只到 1144 包/秒（M-1：1245 包/秒）。
- **构成 B**：16 档全部通过，≥ 25000 包/秒，未触顶，与基线、M-1 相同。档末滞后在每一档都 ≤ 48.8 ms，没有积压；机械判据标的几处「上升」是几毫秒量级的抖动，所以构成 B 的稳定通过档也是 25000。
- **p99 ≤ 50 ms 口径**：构成 A 在 400 档停止（判定 p99 100.7 ms；M-1 112.0／109.0 ms），只作目标对照（F-77）。

### 5.6 稳态端到端 p99 与基线、M-1 的对照

基线（构成 B，TASK-03 clean-1／clean-2）稳态 p99：2.73～2.79 ms；基线构成 A 的稳态 p99 为 229761.2 ms（SQLite 30 秒停顿主导，不可比）

| 运行 | 稳态端到端 p99 | 相对基线 B | M-1 同名运行 | 同次稳态入队等待 p99 |
|---|---:|---:|---:|---:|
| A-def-1 | 103.9 | 37.3～38.0 倍 | 91.65 | – |
| A-def-2 | 97.49 | 35.0～35.7 倍 | 94.56 | – |
| B-def-1 | 26.07 | 9.4～9.5 倍 | 16.58 | – |
| B-def-2 | 20.52 | 7.4～7.5 倍 | 18.38 | – |
| A-dbg-1 | 103.9 | 37.3～38.0 倍 | 95.53 | 94.31 |
| A-dbg-2 | 114.5 | 41.1～41.9 倍 | 92.31 | 99.75 |
| B-dbg-1 | 16.33 | 5.9～6.0 倍 | 14.65 | 16.56 |
| B-dbg-2 | 18.55 | 6.7～6.8 倍 | 15.76 | 19.01 |
| A-def-f256 | 98.32 | 35.3～36.0 倍 | – | – |
| B-def-f256 | 25.30 | 9.1～9.3 倍 | – | – |
| A-dbg-f256 | 92.83 | 33.3～34.0 倍 | – | 83.37 |
| B-dbg-f256 | 14.96 | 5.4～5.5 倍 | – | 14.42 |
| B-def-g0 | 22.28 | 8.0～8.2 倍 | – | –（默认级别无作业层） |

构成 A 稳态窗的执行器占用按作业类型分解（M-2 三次与 M-1 两次）：

| 运行 | 执行时长之和／窗口（占用） | 前 6 类（占执行时长之和的比例） |
|---|---:|---:|
| A-dbg-1 | 46.7 s／120 s（38.9%） | inbound:GameSetUserArea 27.3 s（58.4%）；inbound:GameSelectCharacter 15.4 s（33.0%）；inbound:GameEplpCommand 1.5 s（3.2%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.4 s（0.9%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| A-dbg-2 | 47.1 s／120 s（39.3%） | inbound:GameSetUserArea 26.9 s（57.1%）；inbound:GameSelectCharacter 16.1 s（34.2%）；inbound:GameEplpCommand 1.5 s（3.2%）；inbound:GameEnterSelectDungeon 0.9 s（1.9%）；inbound:GameSetUserPosition 0.5 s（1.0%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| A-dbg-f256 | 46.3 s／120 s（38.6%） | inbound:GameSetUserArea 27.5 s（59.4%）；inbound:GameSelectCharacter 14.8 s（32.0%）；inbound:GameEplpCommand 1.5 s（3.3%）；inbound:GameEnterSelectDungeon 0.9 s（2.0%）；inbound:GameSetUserPosition 0.4 s（0.9%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| M1-A-dbg-1 | 46.8 s／120 s（39.0%） | inbound:GameSetUserArea 27.9 s（59.6%）；inbound:GameSelectCharacter 15.0 s（32.0%）；inbound:GameEplpCommand 1.6 s（3.5%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.5 s（1.0%）；inbound:GameSelectDungeon 0.3 s（0.7%） |
| M1-A-dbg-2 | 46.4 s／120 s（38.7%） | inbound:GameSetUserArea 27.7 s（59.7%）；inbound:GameSelectCharacter 14.6 s（31.5%）；inbound:GameEplpCommand 1.7 s（3.7%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.5 s（1.1%）；inbound:GameSelectDungeon 0.4 s（0.8%） |

A-dbg-1：一次选角里四条每日补给命令 p50 之和 59.06 ms，选角作业 p50 68.33 ms，比例 86.4%（各自 p50 相加，只是量级）

A-dbg-2：一次选角里四条每日补给命令 p50 之和 60.66 ms，选角作业 p50 69.37 ms，比例 87.5%（各自 p50 相加，只是量级）

A-dbg-f256：一次选角里四条每日补给命令 p50 之和 58.51 ms，选角作业 p50 66.59 ms，比例 87.9%（各自 p50 相加，只是量级）

## 6. T-005、T-006 带来的变化

### 6.1 §4.4 六条预期逐条对照

作业执行时长 M-1 → M-2（p50 与 p99，debug 运行逐对）：

| 作业 | A-1（M-1 → M-2） | A-2 | B-1 | B-2 |
|---|---:|---:|---:|---:|
| `inbound:GameSetUserArea` | p50 23.181 → 23.244（0.063）；p99 54.63 → 52.52 | p50 23.112 → 22.561（-0.551）；p99 55.01 → 52.97 | p50 11.765 → 14.169（2.404）；p99 32.87 → 36.86 | p50 12.790 → 14.458（1.668）；p99 20.25 → 24.14 |
| `inbound:GameSelectCharacter` | p50 67.787 → 68.334（0.547）；p99 132.8 → 130.0 | p50 64.992 → 69.368（4.375）；p99 130.7 → 142.6 | p50 70.486 → 75.774（5.288）；p99 232.8 → 227.5 | p50 70.805 → 69.451（-1.354）；p99 233.7 → 229.9 |
| `inbound:GameEplpCommand` | p50 45.623 → 45.375（-0.248）；p99 97.99 → 77.21 | p50 45.855 → 40.769（-5.086）；p99 88.55 → 96.86 | p50 33.224 → 34.562（1.338）；p99 59.31 → 59.52 | p50 38.758 → 37.619（-1.139）；p99 59.06 → 58.64 |
| `inbound:GameSelectDungeon` | p50 9.635 → 9.927（0.292）；p99 71.06 → 77.26 | p50 10.020 → 10.232（0.212）；p99 77.57 → 73.22 | p50 14.645 → 14.585（-0.060）；p99 74.75 → 72.64 | p50 14.313 → 14.899（0.585）；p99 76.81 → 73.48 |
| `inbound:GameEnterSelectDungeon` | p50 22.569 → 21.202（-1.367）；p99 44.13 → 45.90 | p50 23.751 → 20.277（-3.473）；p99 46.57 → 56.15 | p50 23.745 → 24.007（0.262）；p99 46.56 → 62.25 | p50 22.457 → 25.852（3.395）；p99 62.75 → 53.37 |
| `inbound:GameSetUserPosition` | p50 0.017 → 0.013（-0.005）；p99 0.11 → 0.12 | p50 0.018 → 0.011（-0.007）；p99 0.12 → 0.11 | p50 0.023 → 0.030（0.007）；p99 0.11 → 0.14 | p50 0.024 → 0.021（-0.003）；p99 0.11 → 0.09 |
| `inbound:GameConnectionCheck` | p50 0.002 → 0.003（0.001）；p99 0.01 → 0.01 | p50 0.002 → 0.002（0.000）；p99 0.01 → 0.01 | p50 0.002 → 0.002（0.000）；p99 0.01 → 0.02 | p50 0.002 → 0.003（0.000）；p99 0.01 → 0.01 |

定时作业逐类计数（每次运行）：

| 定时作业 | A-1（M-1 → M-2） | A-2 | B-1 | B-2 |
|---|---:|---:|---:|---:|
| `timer:AltarQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:BossTowerReturnSweepService` | 553 → 553 | 553 → 553 | 555 → 554 | 555 → 554 |
| `timer:CreatureClockService` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:DimensionQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:DungeonPartyWipeService` | 1105 → 1105 | 1104 → 1104 | 1110 → 1108 | 1110 → 1108 |
| `timer:FatigueDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:GoldSilverHornQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:HerasQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:IllusionTowerReturnSweepService` | 553 → 553 | 553 → 553 | 555 → 554 | 555 → 554 |
| `timer:ItemUsageDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:JoustMaintenanceService` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:LoginSessionMaintenanceService` | 9 → 9 | 9 → 9 | 9 → 9 | 9 → 9 |
| `timer:MarriageWeddingScheduler` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:MirrorQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:NotificationRetryDrainService` | 276 → 0 | 276 → 0 | 277 → 0 | 277 → 0 |
| `timer:PremiumDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:ReviveCoinQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:RoomMechanismMaintenanceService` | 162 → 162 | 157 → 158 | 160 → 160 | 164 → 158 |
| `timer:ScheduledTaskHostedService/character.name-tag.expiration` | 5 → 4 | 5 → 5 | 4 → 5 | 5 → 5 |
| `timer:TournamentQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:TournamentRewardSweepService` | 5464 → 5463 | 5454 → 5434 | 5546 → 5537 | 5547 → 5536 |
| `timer:WeaknessRecoveryService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |

| # | 预期 | 实测 | 判断 |
|---|---|---|---|
| 1 | 含区域投影与多段回包的五类作业执行时长比 M-1 小 | 五类作业的 p50 都与 M-1 持平，差在 ±5 ms 内，有升有降。构成 A 的 `GameSetUserArea` 为 23.2／22.6 ms（M-1 23.2／23.1 ms），`GameSelectCharacter` 为 68.3／69.4 ms（M-1 67.8／65.0 ms）。构成 A 稳态窗的执行时长之和为 46.3～47.1 s，M-1 为 46.4～46.8 s | **不符合**。网络等待不是成因；作业内与发送相关的工作（M-1 是直接发送，M-2 是登记、编码与冲刷）总量没有变化，这部分的份额没有单独测出 |
| 2 | 构成 A 稳态入队等待 p99（M-1 86.1／86.5 ms）下降 | 94.3／99.8 ms（窗口 256 那次 83.4 ms），没有下降；执行器占用 38.6%～39.3%，与 M-1 的 38.7%～39.0% 相同 | **不符合**。按计划的读法，这间接印证了 F-78、F-81 的推断：排队来自直接发送之外的工作，即 `GameSetUserArea`（占执行时长之和 57%～59%）与 `GameSelectCharacter`（32%～34%，其中四条每日补给命令约占选角作业 p50 的 86%～88%） |
| 3 | 首条回包推迟到作业结束；选角端到端可能变大；轻量包多出发送端一段 | SELECT_CHARACTER 稳态 p50 由 4.2～4.3 ms 变为 67.9～72.1 ms，与选角作业 p50（66.6～69.4 ms）吻合；构成 B 的 (f) 由约 133 ms 变为 229～247 ms，即首次选角作业的长度（226.8～229.9 ms）。轻量包作业之后一段的 p50 由 0.16～0.19 ms 变为 0.17～0.25 ms（§6.2） | **符合** |
| 4 | 稳态 `timer:` 每秒作业数比 M-1 少 `NotificationRetryDrainService` 那一份 | `timer:NotificationRetryDrainService` 由每次 276～277 个变为 0；稳态 `timer:` 由 32.7／32.8 个/秒变为 31.4～31.8 个/秒，差约 1.0 个/秒；其余定时作业的计数不变（差 ≤ 20 个，来自运行时长的毫秒差） | **符合** |
| 5 | S5 信号：作业失败 0；`GameplaySelfWait` 0；`OverLimit` 0；收尾窗口外的发送端告警 0 | 前三项都是 0；收尾窗口外的发送端告警在构成 B 的每次运行中有 2 行（§1，F-119），按回报后的裁定归为「工具主动断开」、不计 | **字面上部分不符合**（那 2 行是预期之外的现象；按裁定不计入 S5） |
| 6 | 轻量作业 p50 持平或略降 | `GameSetUserPosition` p50 由 0.017～0.024 ms 变为 0.011～0.030 ms，`GameConnectionCheck` 由 0.002 ms 变为 0.002～0.003 ms | **符合**（持平） |

### 6.2 端到端延迟的构成（估计）

- 算法：同窗口、同请求类型的分布相减，`作业之后一段 ≈ 参考端到端 − 入队等待 − 执行时长`，p50 与 p99 各算一次。**这不是逐样本的拆分**，p99 一列只作量级参考。
- 「作业之后一段」的含义：M-2 是发件箱交给发送端、发送端交付、本机回环与工具接收；M-1 只有「作业结束到工具读到」，因为回包在作业内直接写出。
- (e) 窗里灌包连接的栅栏也是 CHECK_USER_CONNECTION，那一行不可用。

| 测量 | 运行 | 窗口 | 请求 ↔ 作业 | 参考端到端 n / p50 / p99 | 入队等待 n / p50 / p99 | 执行时长 p50 / p99 | 作业之后一段 p50 估计 | p99 估计（量级） |
|---|---|---|---|---:|---:|---:|---:|---:|
| M-2 | A-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25510 / 0.211 / 98.066 | 27728 / 0.035 / 94.078 | 0.002 / 0.078 | 0.174 | 3.909 |
| M-2 | A-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18477 / 0.250 / 96.607 | 18485 / 0.035 / 94.093 | 0.012 / 0.116 | 0.202 | 2.398 |
| M-2 | A-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6280 / 0.184 / 99.645 | 1339210 / 0.001 / 0.009 | 0.003 / 0.008 | 0.180 | 99.628 |
| M-2 | A-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4746 / 0.210 / 102.4 | 4751 / 0.045 / 102.0 | 0.015 / 0.123 | 0.149 | 0.227 |
| M-2 | A-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25556 / 0.242 / 104.8 | 27740 / 0.044 / 101.9 | 0.002 / 0.063 | 0.196 | 2.792 |
| M-2 | A-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18444 / 0.286 / 99.959 | 18440 / 0.049 / 98.324 | 0.011 / 0.108 | 0.226 | 1.527 |
| M-2 | A-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6243 / 0.202 / 93.173 | 1398248 / 0.001 / 0.009 | 0.002 / 0.007 | 0.198 | 93.158 |
| M-2 | A-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4794 / 0.235 / 103.0 | 4798 / 0.061 / 101.4 | 0.014 / 0.121 | 0.160 | 1.526 |
| M-2 | B-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26801 / 0.199 / 14.774 | 27435 / 0.011 / 14.351 | 0.008 / 0.041 | 0.180 | 0.382 |
| M-2 | B-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20338 / 0.252 / 11.695 | 20338 / 0.011 / 11.468 | 0.034 / 0.147 | 0.207 | 0.080 |
| M-2 | B-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6551 / 0.083 / 6.521 | 1925138 / 0.001 / 0.007 | 0.002 / 0.012 | 0.079 | 6.501 |
| M-2 | B-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5255 / 0.111 / 6.628 | 5258 / 0.007 / 1.095 | 0.015 / 0.054 | 0.089 | 5.479 |
| M-2 | B-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26742 / 0.189 / 15.745 | 27368 / 0.011 / 15.038 | 0.007 / 0.031 | 0.171 | 0.677 |
| M-2 | B-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20409 / 0.221 / 15.121 | 20409 / 0.011 / 15.004 | 0.022 / 0.096 | 0.188 | 0.021 |
| M-2 | B-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6514 / 0.083 / 7.474 | 1938024 / 0.001 / 0.007 | 0.002 / 0.012 | 0.079 | 7.455 |
| M-2 | B-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5309 / 0.109 / 6.203 | 5309 / 0.007 / 1.045 | 0.012 / 0.046 | 0.090 | 5.112 |
| M-2 | A-dbg-f256 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25516 / 0.271 / 83.108 | 27704 / 0.049 / 79.746 | 0.002 / 0.038 | 0.220 | 3.324 |
| M-2 | A-dbg-f256 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18488 / 0.313 / 87.021 | 18484 / 0.054 / 83.639 | 0.012 / 0.116 | 0.248 | 3.265 |
| M-2 | A-dbg-f256 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6427 / 0.236 / 87.246 | 1392588 / 0.001 / 0.009 | 0.003 / 0.008 | 0.232 | 87.229 |
| M-2 | A-dbg-f256 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4613 / 0.263 / 91.083 | 4613 / 0.062 / 89.839 | 0.014 / 0.121 | 0.187 | 1.123 |
| M-2 | B-dbg-f256 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26773 / 0.198 / 11.539 | 27399 / 0.011 / 10.715 | 0.007 / 0.031 | 0.180 | 0.793 |
| M-2 | B-dbg-f256 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20378 / 0.226 / 11.441 | 20378 / 0.010 / 11.118 | 0.023 / 0.106 | 0.193 | 0.217 |
| M-2 | B-dbg-f256 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6591 / 0.084 / 6.171 | 1895748 / 0.001 / 0.007 | 0.002 / 0.012 | 0.080 | 6.152 |
| M-2 | B-dbg-f256 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5208 / 0.106 / 7.812 | 5208 / 0.007 / 6.647 | 0.012 / 0.048 | 0.087 | 1.117 |
| M-1 | A-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25505 / 0.227 / 86.963 | 27706 / 0.049 / 84.338 | 0.002 / 0.036 | 0.177 | 2.589 |
| M-1 | A-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18499 / 0.226 / 88.436 | 18502 / 0.048 / 86.192 | 0.017 / 0.112 | 0.161 | 2.132 |
| M-1 | A-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6451 / 0.183 / 88.713 | 1561784 / 0.001 / 0.008 | 0.002 / 0.004 | 0.180 | 88.701 |
| M-1 | A-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4559 / 0.179 / 86.838 | 4559 / 0.060 / 86.716 | 0.018 / 0.112 | 0.101 | 0.010 |
| M-1 | A-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25507 / 0.263 / 86.794 | 27706 / 0.070 / 82.834 | 0.002 / 0.038 | 0.192 | 3.922 |
| M-1 | A-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18494 / 0.259 / 90.848 | 18495 / 0.070 / 88.697 | 0.018 / 0.117 | 0.171 | 2.034 |
| M-1 | A-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6313 / 0.197 / 88.096 | 1557808 / 0.001 / 0.007 | 0.002 / 0.004 | 0.194 | 88.085 |
| M-1 | A-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4705 / 0.203 / 87.689 | 4705 / 0.073 / 86.827 | 0.019 / 0.111 | 0.111 | 0.751 |
| M-1 | B-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26798 / 0.184 / 14.026 | 27430 / 0.011 / 13.618 | 0.006 / 0.024 | 0.167 | 0.384 |
| M-1 | B-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20343 / 0.198 / 9.502 | 20343 / 0.010 / 9.247 | 0.025 / 0.116 | 0.162 | 0.138 |
| M-1 | B-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6455 / 0.072 / 1.479 | 2570518 / 0.001 / 0.006 | 0.002 / 0.004 | 0.069 | 1.469 |
| M-1 | B-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5344 / 0.083 / 1.514 | 5344 / 0.006 / 0.249 | 0.016 / 0.056 | 0.060 | 1.208 |
| M-1 | B-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26808 / 0.176 / 14.579 | 27446 / 0.009 / 14.231 | 0.006 / 0.022 | 0.161 | 0.327 |
| M-1 | B-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20325 / 0.191 / 10.663 | 20325 / 0.009 / 10.521 | 0.025 / 0.110 | 0.158 | 0.033 |
| M-1 | B-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6674 / 0.069 / 4.416 | 2544063 / 0.001 / 0.006 | 0.002 / 0.004 | 0.066 | 4.406 |
| M-1 | B-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5095 / 0.078 / 1.806 | 5095 / 0.005 / 0.776 | 0.016 / 0.051 | 0.057 | 0.979 |

- **p50**：作业之后一段，稳态窗 M-2 为 0.17～0.25 ms，M-1 为 0.16～0.19 ms；(e) 窗 SET_USER_POSITION，M-2 为 0.09～0.19 ms，M-1 为 0.06～0.11 ms。发送端带来的这一段约多几十微秒，占端到端 p50（0.2～0.3 ms）的一小部分。
- **p99**：构成 A 稳态窗的这一段只有 1.5～3.9 ms；同一张表里参考端到端 p99 为 83.1～104.8 ms，入队等待 p99 为 79.7～101.9 ms，差不多全部是入队等待。
- **构成 B 的 (e) 窗（窗口 32）**：这一段的 p99 估计为 5.1～5.5 ms，M-1 为 1.0～1.2 ms。这与其余连接 (e) 端到端 p99 从约 4.5 ms 变为约 10 ms 相合，是 M-2 新出现的（F-120）。窗口 256 那次只有 1.1 ms，量级不稳定。

### 6.3 冷启动停顿对应的请求

| 运行与请求 |
|---|
| A-1: CREATE_CHARACTER setup pair02L t=0.46 s judged 968.5 ms reference 968.543 ms |
| A-2: SET_USER_POSITION background solo01 t=2.03 s judged 1053.1 ms reference 212.157 ms |
| B-1: SELECT_CHARACTER setup solo00 t=0.30 s judged 236.3 ms reference 236.262 ms |
| B-2: SELECT_CHARACTER setup solo02 t=0.30 s judged 247.4 ms reference 247.402 ms |
| A-debug-1: SET_USER_POSITION background solo00 t=2.02 s judged 1020.2 ms reference 115.150 ms |
| A-debug-2: SELECT_CHARACTER setup pair02L t=0.46 s judged 1367.6 ms reference 1367.573 ms |
| B-debug-1: SELECT_CHARACTER setup solo00 t=0.30 s judged 232.4 ms reference 232.363 ms |
| B-debug-2: SELECT_CHARACTER setup solo06 t=0.29 s judged 230.9 ms reference 230.914 ms |
| A-f256: SELECT_CHARACTER setup solo00 t=0.45 s judged 887.8 ms reference 887.754 ms |
| B-f256: SELECT_CHARACTER setup solo05 t=0.30 s judged 231.0 ms reference 231.022 ms |
| A-f256-debug: SELECT_CHARACTER setup solo03 t=0.43 s judged 921.3 ms reference 921.346 ms |
| B-f256-debug: SELECT_CHARACTER setup solo04 t=0.29 s judged 229.2 ms reference 229.152 ms |
| B-g0: SELECT_CHARACTER setup solo04 t=0.29 s judged 229.8 ms reference 229.815 ms |

- 构成 B 的最长停顿都是首次 SELECT_CHARACTER（就绪后 0.29～0.30 s 计划发出），等于首次选角作业的长度。
- 构成 A 是就绪后 0.4～2 s 内的建角、选角或位置包，排在 20 个 agent 同时建号、选角的串行作业之后（同 M-1）。
- A-2 与 A-debug-1 的最长值只在判定序列上（参考序列 212.2／115.1 ms）：那是排在冷启动积压后面的发出滞后。

## 7. (c)(e) 正式数字与归因

正式数字（T-006 之后作业内不再等待网络，这两项不再是过渡态）：

- **(c) 稳态入队等待 p99**：构成 A 94.3／99.8 ms（窗口 256 那次 83.4 ms），构成 B 16.6／19.0 ms（14.4 ms）；目标 50 ms，构成 A 未达，构成 B 达标。端到端 p99：构成 A 97.5～114.5 ms，构成 B 14.96～26.07 ms。
- **(e) 窗口 32**：
  - 入队等待 p99 都是 0.01 ms。这是代理值，被灌包连接每秒数万个排在空队列上的作业主导（§10 第 3 条）。
  - 入队等待 max：构成 A 144.4／165.5 ms，构成 B 38.1／37.7 ms。
  - 其余连接的端到端 p99：构成 A 106.6～112.8 ms，构成 B 7.5～11.2 ms。
- **(e) 窗口 256**：
  - 入队等待 p99 0.01 ms，max 构成 A 181.2 ms、构成 B 41.4 ms；
  - 其余连接的端到端 p99：构成 A 97.8／114.7 ms，构成 B 8.8／12.1 ms。
  - 与窗口 32 同量级：在途包更多时，其他连接没有因此变差。RQ-005 下灌包连接在执行器队列里至多 1 条，其余在途包停在传输层。

构成 A 未达 50 ms 的归因（§5.3 的顺序）：
1. 不是日志开销：没有窗口的 debug 端到端 p99 超过默认级别的 2 倍（§4）。
2. 稳态窗执行时长之和最大的是 `inbound:GameSetUserArea`（57.1%～59.4%）与 `inbound:GameSelectCharacter`（32.0%～34.2%），合计约 91%；执行器占用 38.6%～39.3%。
3. 与 M-1 的同一分解相比，份额与占用都没有变（M-1：59.6%／31.5%～32.0%，占用 38.7%～39.0%）。
4. 作业结束之后那一段的 p99 只有 1.5～3.9 ms，约占参考端到端 p99（83.1～104.8 ms）的 1.5%～4%（§6.2）。
5. 结论：**执行器上的长作业串行**，与 M-1 相同（F-78）。网络等待不是成因；作业内与发送相关的工作（M-1 是直接发送，M-2 是登记、编码与冲刷）总量没有变化（§6.1 第 1 条），这部分的份额没有单独测出。

构成 B 的 (e)：其余连接的 p99 从约 4.5 ms 翻到约 10 ms，其中作业之后一段的 p99 约 5 ms（§6.2）。归为「发送端交付」，是 M-2 新出现的，登记为 F-120。

## 8. 后续性能优化事项

每个未达目标值的项一行，全部登记在 `../follow-ups.md`，状态「未关闭」。最后一行 F-119 是第一次判定按字面计为 S5 命中的项，回报后已裁定为「工具主动断开」、不计；它带出的日志级别问题是 F-121。

| 量 | M-2 值 | M-1 值 | 目标 | 出处 | 已知或推断的成因 | F 编号 |
|---|---|---|---|---|---|---|
| (b) 构成 A 可持续吞吐 | 2 s 口径 1169 包/秒，稳定通过档 800；p99 ≤ 50 ms 口径无通过档（400 档 p99 100.7 ms） | 1188 包/秒，稳定通过档 800；无通过档（112.0／109.0 ms） | ≥ 2000 CMD/秒 | §5.5 | 执行器上的长作业串行（推断，同 F-78） | F-77 |
| (c)(e) 构成 A 稳态与灌包期间 | 入队等待 p99 94.3／99.8 ms；(e) 其余连接端到端 p99 106.6～112.8 ms | 86.1／86.5 ms；86.3～89.7 ms | 入队等待 p99 ≤ 50 ms | §7 | `GameSetUserArea` 与 `GameSelectCharacter` 串行，约占执行时长之和的 91%；网络等待不是成因，作业内与发送相关的工作（M-1 直接发送，M-2 登记、编码与冲刷）总量没有变化，份额没有单独测出（§6.1） | F-78 |
| (d) 10 并发登录 | 入队等待 p99 构成 A 100.0／180.9 ms，构成 B 1.0／25.9 ms 达标；背景端到端 p99 构成 A 138.8／205.8 ms，构成 B 84.3／100.1 ms | A 74.1／100.8 ms；B 11.6／17.2 ms；背景端到端 A 71.5／133.4 ms、B 96.5／98.3 ms | p99 ≤ 50 ms | §5.1、§5.3 | 构成 A：同上；构成 B：双人组领队自身的离本、进本长作业落在轮内 | F-79 |
| (f) 冷启动 | 构成 A 887.8～1367.6 ms（2 s 线的 0.68 倍、目标的 5.5 倍）；构成 B 229.2～247.4 ms，达标但贴近目标 | A 921.2～1189.6 ms（0.59 倍、4.8 倍）；B 132.4～136.8 ms | ≤ 250 ms | §5.1、§6.3 | 构成 A：20 个 agent 同时建号、选角，串行积压；构成 B：首次选角的首条回包推迟到作业结束（VC-1） | F-80 |
| 玩法 CMD 单条 | `GameSelectCharacter` p99 130.0～142.6 ms，p99.9（即 max）226.8～239.1 ms；全部作业合计 p99.9 构成 A 25.6～26.9 ms | p99 130.7～132.8 ms，max 229.5～236.1 ms | p99.9 ≤ 100 ms、max ≤ 250 ms | §5.3 | 四条每日补给命令约占选角作业 p50 的 86%～88%（D 类规则读取与各自的事务，推断） | F-81 |
| 合成负载覆盖不到的项 | M-2 同样未覆盖 | — | — | §10 | 负载 `GmWeb__Enabled=false`、5 分钟不跨日切、动作集不变 | F-82 |
| 灌包连接自身速率与构成 B 的 (e) | 灌包连接自身速率：构成 B 默认级别 7.8 万包/秒，构成 A 5.7～6.0 万；构成 B 其余连接 (e) 端到端 p99 9.8～11.2 ms（G1）、17.84 ms（G0，单次运行，可能含噪声） | 15.1～15.3 万；9.0～9.2 万；4.4～4.9 ms（G0；对 M-2 的 G0 值约 3.6～4 倍，对基线 2.71 ms 约 6.6 倍） | (e) 入队等待 p99 ≤ 50 ms（已达） | §5.1、§6.2 | 每个作业在 (e) 窗的平均占用由 2.4 µs 升到 4.4 µs（构成 B debug：6.18 s ÷ 2,577,240 个 → 8.62 s ÷ 1,931,495 个）；发送端一段的 p99 约 5 ms（推断：发件箱冲刷与发送端交付） | F-120（新） |
| 收尾窗口外的发送端告警（按字面曾判 S5 命中，已裁定不计） | 构成 B 每次 2 行，就绪探测连接 | 无（M-1 是直接发送） | 0 | §1 | T-006 的发送端向工具已关闭的探测连接交付初始连接通知 | F-119（新，已裁定，不计）；日志级别交 T-009 复核：F-121（新） |

## 9. 对 T-009 的交代

- **命令行与 dll**：最终测量沿用本单元的命令行（`../logs/task-28-runs.txt`）与 G1 dll（`raw/m2-tool-version.txt`）。默认级别组、debug 组、窗口 256 与爬坡都用 G1。控制运行**没有证明**两个 dll 在默认级别下等价：稳态、(d)、(f) 落在两次 G1 运行的差距之内，(e) 不在（§5.2）。T-009 若要用 G1 的 (e) 与基线对照，宜多跑几次 G0 控制运行，或 (e) 同时给 G0 口径。
- **F-119 已裁定**：就绪探测连接按「工具主动断开」处理。T-009 的判定脚本要把这条豁免写进规则（§1.3 第 5 项的清单漏列了它），不能只沿用本单元按行号登记的 `ATTRIBUTED`——行号每次运行都会变。F-121（发送端对客户端先断开记 Warning `Failed` 是否合适）由 T-009 复核。
- **S3 与 (b)**：继续按 2 s 口径爬坡（`--ramp-criterion max:2000 --executor-log debug`）判 S3，并同时写出稳定通过档。
- **覆盖缺口**：F-82 原样保留，M-2 同样没有覆盖。

## 10. 缺口与局限

1. 合成负载，不是 A21 录制。选角频率（约每秒 1.6 次）远高于真实玩家，构成 A 的数字因此偏悲观。
2. 工具与 Host 同机运行，抢同一组 CPU；本机 swap 已用约 9 GB，同机还有其他应用与 agent 会话，P3 换页尖峰比 M-1 多。
3. (e) 的作业行不带连接号，灌包连接自己的作业分不出来；(e) 窗的入队等待被大量空队列上的轻量作业主导，p99 不代表其他连接。
4. 作业层的窗口按工具读到日志行的时刻归窗；debug 级下日志行在高负载时可能晚到。本次对照未见 2 倍以上的差异。
5. 成因的份额（每日补给规则读取、事务提交、当前视图重建各占多少）没有测量，是读代码的推断。
6. **延迟构成只能分布相减估计**：发送端的交付时刻（`Outbound entry … Delivered`）是 Trace 级，不打开，工具也不解析（§6.2）。
7. **收尾窗口起点约有 1 s 的不确定性**（工具输出行与实际关闭之间的间隔）。本次窗口外的告警都在起点前 270 s 以上，窗口内的都在起点之后，没有落在不确定区间里的行。
8. 爬坡不生成 summary.json：就绪时刻取「第一行 ramp 输出 − 50 s」，误差是首档的收口时间（几十毫秒）；ramp-A-2s 没有解析自检。
9. 合成负载不超过 21 个在线角色，观测不到人群项的上界问题（F-113、F-114、F-116、F-117 与 M-2 无关，不改）。
