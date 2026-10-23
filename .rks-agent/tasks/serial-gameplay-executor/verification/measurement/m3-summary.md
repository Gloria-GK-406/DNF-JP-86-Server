# M-3 测量汇总（TASK-32，合成负载）

> **合成负载**：没有 A21 的真实录制，负载由 `Tool/GameplayLoad` 按 TASK-01 的线上序列合成，口径与命令行见 [README](README.md)。
> **用途**：结果 A 的最终测量（Plan T-009「最终测量」，RQ-090、SC-9）。M-3 是硬停闸；它判定 AC-005 的测量一半、AC-021（A 一半）与 F-02。

- 测量日期：2026-10-23（UTC 19:44～21:40，即本地 2026-10-24 03:44～05:40；18 次运行，没有重跑，没有条件运行）
- 被测服务端：代码冻结点 `2bdb1afd`（TASK-31 提交），Release 构建，`Dfo.Server.Host.dll` SHA-256 `79dbbf54…`（`../logs/task-32-build.txt`）。相对 M-2 的被测代码 `f7bf0aa3`，`Server/src` 有 45 个文件改动（＋735／－156 行），即 T-007（TASK-29）、TASK-30、TASK-31。执行开始与结束时 `git diff 2bdb1afd -- Server Tool` 都为空
- 工具：除三次控制运行外全部用 G1（`deb158a5…`），控制运行用 G0（`b3d6c286…`）；源码与两个 dll 的哈希见 `raw/m3-tool-version.txt`
- 判定口径的出处：
  - `spec.md` RQ-091 的「用户修订（2026-10-22，执行中）」：单条 CMD ≤ 2 s 即接受，只有数量级问题才回到用户；
  - TASK-20 的主控方裁定（S3 按 2 s 口径读；S4 无停止效力；(c)(e) 只有达到 2 s 线才停止）；
  - TASK-28「主控方裁定（执行前）」1～6、「主控方裁定（回报后）」（F-119）与「主控方处置（第 1 轮评审后）」；TASK-30「主控方裁定（执行前）」第 6 条；
  - **S5 第 5 项**：`../t009-logs-and-cases.md` §4 的五条，出处是 `../../tasks/TASK-31.md`「主控方裁定（执行前）」第 2 条，以及同文件「主控方处置（评审后，提交前）」第 1 条对它的修订；原样采用，判定日志开头逐字抄录；
  - `../../tasks/TASK-32.md`「主控方裁定（执行前）」1～7。
- 规则、§4 五条与 §4.4 的六条预期在第一次运行之前（19:43Z）写进 `../logs/task-32-judgement.txt` 开头；规则的回归核对与反例自检也在第一次运行之前完成（§4.3）。第一次运行开始于 19:44:52Z
- 本页的数字表全部由脚本输出生成：判定 scratchpad `t32/judge.py`，发送端层的行的归类 `t32/classify.py`，延迟构成 `t32/composition.py`，冷启动停顿对应的请求 `t32/coldmax.py`。全文与原始输出在 `../logs/task-32-judgement.txt`，不手抄

## 1. 结论

**M-3 通过。** S1～S5 在 18 次运行中都不命中，任何样本（干净或受污染）中都没有越线，所以没有重跑。

- **S1 不命中**：
  - 单条作业执行时长 max **269.3 ms**（A-dbg-1，`inbound:GameSelectCharacter`，冷启动窗内 t＝19.04 s；M-2 239.1 ms），是 2 s 线的 0.13 倍（269.3 ÷ 2000 ＝ 0.135）。这一次与机器上一次 1～2 s 的外部 CPU 尖峰同一秒（见 §2），是读日志与采样的推断；
  - 单条作业执行时长 **p99.9**（全部作业类型合计）：构成 A 26.76／27.53 ms（窗口 256 那次 26.91 ms），构成 B 0.29／0.28／0.32 ms，ramp-A-2s 94.99 ms（只算 inbound 100.6 ms）；
  - 冷进程首次选角 230.3～237.1 ms；内层命令 max 134.2 ms（A-dbg-1，`RefillOnlineSouthernTicketCommand`，就是上面那次选角里的一条）；ramp-A-2s 的作业 max 233.0 ms。
- **S2 不命中**：
  - (f) 最长单次停顿 max 1231.1 ms（A-def-f256，判定与参考序列相同），是 2 s 线的 0.62 倍（1231.1 ÷ 2000 ＝ 0.616）、目标 250 ms 的 4.9 倍（1231.1 ÷ 250 ＝ 4.92）；
  - 入队等待 max 872.5 ms（A-dbg-2 冷启动窗）；
  - 稳态、(d)、(e) 三个窗口（窗口 32 与 256）两个序列都 ≥ 2 s 的端到端样本 0 个，只在判定序列上 ≥ 2 s 的也是 0 个。
- **S3 不命中**：ramp-A-2s 可持续吞吐 1163 包/秒，不低于 400；ramp-B 16 档全部通过，各档 max ≤ 66.61 ms，错误 0，2 s 口径成立，不需要 ramp-B-2s。
- **S4（只记录）**：(d) 入队等待 p99，构成 A 136.9／103.3 ms（窗口 256 那次 103.8 ms），未达 50 ms；构成 B 17.32／2.42 ms 达标，窗口 256 那次 53.83 ms，未达。
- **S5 不命中**：
  - 18 次运行的错误、失败命令、失败作业都是 0，不设任何时间豁免；必须为 0 的 15 种信号都是 0；`OverLimit` 0；`Game handler failed` 0；
  - 发送端层的行共 106 ＋ 20 ＝ 126 行，按 §4.2 的规则归类：T（收尾，不计）106 行、P（就绪探测，不计）20 行、D 0 行、计入 0 行（§4.2）；
  - F-126 形态（工具先断开的连接上、Warning 的 `Failed`、`SocketException`）0 行。
- **相对 M-2 显著变差**（TASK-32 §1.5 的判据：两次同类运行都比 M-2 较差的那次差 2 倍以上，且超出 M-2 两次的差距）：**0 项**。脚本逐项检验了 17 个量 × 2 种构成，比值最大的是 (e) 入队等待 max 1.15 倍（189.9 ÷ 165.5）与选角作业 max 1.13 倍（269.3 ÷ 239.1），都远低于 2 倍（§5.2 表）。
- **基线重测**：未触发。构成 B 默认级别的稳态端到端 p99 为 19.22／19.84 ms，不到 M-2（20.52～26.07 ms）的 3 倍（61.56～78.20 ms）；S1～S3 也都不命中。
- **控制运行**：**未证明等价**（12 项里 2 项在区间内，§5.3）。三次 G0 落在两次 G1 的两侧，方向不一致；两次 G1 之间的差距很小（0.45～1.66 ms），所以区间很窄。(e) 的 G0 口径：三次为 9.84／14.84／8.47 ms，中位数 9.84 ms，约是 M-1 G0 的 2.0～2.2 倍，都低于 M-2 那一次 G0 的 17.84 ms（§5.3）。

### AC 关联与 F-02（TASK-32 主控方裁定第 5 条，「M-3 通过」的记法）

- **AC-005 的测量一半：M-3 满足**（修订后的 RQ-091 接受线，证据 `m3-summary.md` §1）。AC-005 的另一半（口令哈希在执行器外、并发有上限）不由本单元判定。
- **AC-021：A 一半满足**（测量记录有完整口径与数字，满足接受线）。
- 两条的最终状态由 TASK-34 的 AC 状态表引用本节，`spec.md` 不改。
- **F-02 已在 TASK-32 关闭**（`../follow-ups.md`）。

### 下一步

- TASK-33 照常开始；TASK-34 的 AC 状态表与 A 最终汇报引用本页。
- 未达目标值的项（F-77～F-81、F-120）仍归后续性能优化（交主控方立项），由 TASK-34 列入 A 的遗留问题；新增 F-127（收尾窗口内 `Writing is not allowed after writer was completed` 形态的 Warning `Failed`，§4.2）。

## 2. 环境

- 机器与软件：Apple M5（10 核），32 GiB，macOS 27.0.1，内置 SSD（APFS），接电源；SQLite 与构建配置由工具写进每份运行记录（`m3-*.md` 的环境节）；GC 为 Host 默认，环境中无 `DOTNET_` 变量。
- 内存与后台负载：swap 已用 9.5～10.0 GB；各次运行每一秒的 pageouts 都是 0；同机还有其他应用与 agent 会话。
- 开跑前没有孤儿测试 Host（`../logs/task-32-build.txt` 末尾）。静默检查 18 次全部通过（等待 0～240 s）。
- **周期性的外部 CPU 尖峰**：每隔约 10 分钟（UTC 的 x4:08～x4:10），一个名字以 `Microsoft` 开头的进程占 258%～488% CPU，持续 1～2 s。它从没有连续 3 s 超过 200%，按 P1 的阈值不构成污染。M-3 单条作业的 max（A-dbg-1 的 269.3 ms 选角，结束于 20:14:08.811Z）正落在这样一秒里（其他进程 360%，其中该进程 325%），是读采样的推断，数值照常使用。
- **测量期间没有运行任何分析脚本**：后处理在矩阵结束（21:40:38Z）之后才开始。
- 逐次的静默检查、前后快照与每秒采样汇总见 `../logs/task-32-env.txt`。

**干净／受污染**（TASK-28 §3.3，逐窗口）：

| 运行 | 判定 |
|---|---:|
| B-def-1 | cold 干净；steady 干净；d 干净；e 干净 |
| B-def-g0-2 | cold 干净；steady 受污染；d 干净；e 干净 |
| A-def-1 | cold 干净；steady 干净；d 干净；e 干净 |
| B-dbg-1 | cold 干净；steady 干净；d 干净；e 干净 |
| A-dbg-1 | cold 干净；steady 干净；d 干净；e 干净 |
| ramp-B | 整段：受污染 |
| ramp-A-2s | 整段：干净 |
| B-def-2 | cold 干净；steady 受污染；d 干净；e 干净 |
| B-def-g0-3 | cold 干净；steady 干净；d 干净；e 干净 |
| A-def-2 | cold 干净；steady 干净；d 干净；e 干净 |
| B-dbg-2 | cold 干净；steady 干净；d 干净；e 干净 |
| A-dbg-2 | cold 干净；steady 干净；d 干净；e 干净 |
| ramp-A | 整段：干净 |
| B-def-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| A-def-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| B-dbg-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| A-dbg-f256 | cold 干净；steady 干净；d 干净；e 干净 |
| B-def-g0 | cold 干净；steady 干净；d 干净；e 干净 |

- 受污染的 3 处全部是 P3 换页尖峰：B-def-g0-2 的稳态窗（1 s，1212 页/秒）、B-def-2 的稳态窗（1 s，1898 页/秒）、ramp-B 整段（2 s，最高 3856 页/秒）；1 ＋ 1 ＋ 1 ＝ 3。没有 P1、P2、P4。
- 构成 A 各窗口的发出滞后 p99 很大，同窗口参考序列 p99 也在 5 ms 以上，是闭环积压，不计为 P2（同 M-1、M-2）。
- S1～S5 在任何样本中都没有越线，所以没有因污染重跑。

## 3. 运行矩阵

- 命令行与 M-2 相同（`../logs/task-32-runs.txt` 有每次的完整命令行），只换了输出目录与 tag，并在次序 2、9 插入两次 G0 控制运行（B-def-g0-2、B-def-g0-3）；B-def-g0 仍在最后。去掉这两次，次序与 M-2 的 1～16 一一对应。
- 收尾窗口起点同 M-2：`run` 为「logins done, flooding」那一行 ＋ 30 s ＋ 2 s；`ramp` 为最后一行「ramp N: …」。
- 就绪锚点（§4.2 规则 P3）是 host.log 第一条 `Request finished … GET …/api/launcher/v1/bootstrap - 200`。
- 记录：
  - 默认级别：`m3-A-1.md`、`m3-A-2.md`、`m3-B-1.md`、`m3-B-2.md`；
  - debug 级：`m3-A-debug-1.md`、`m3-A-debug-2.md`、`m3-B-debug-1.md`、`m3-B-debug-2.md`；
  - 窗口 256：`m3-A-f256.md`、`m3-B-f256.md`、`m3-A-f256-debug.md`、`m3-B-f256-debug.md`；
  - 控制运行：`m3-B-g0.md`、`m3-B-g0-2.md`、`m3-B-g0-3.md`；
  - 爬坡：`m3-ramp-full-2s.md`（判 S3）、`m3-ramp-full.md`、`m3-ramp-clean.md`；
  - 4 ＋ 4 ＋ 4 ＋ 3 ＋ 3 ＝ 18 份；聚合结果 `raw/m3-*.summary.json`（15 份，爬坡不生成），工具版本 `raw/m3-tool-version.txt`。

| 运行 | 工具 | dll SHA-256 | 开始 (UTC) | 结束 (UTC) | 退出码 | Host 日志行数 | 收尾窗口起点 (UTC) | 就绪锚点（bootstrap 200 行号与时刻，UTC） | 静默检查 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| B-def-1 | `run` 构成 B；G1；tag `m3b1` | deb158a5… | 19:44:52 | 19:49:34 | 0 | 3501 | 19:49:31.038 | #98 19:44:57.036 | 通过 |
| B-def-g0-2 | `run` 构成 B；G0（基线 dll）；tag `m3bg02` | b3d6c286… | 19:53:06 | 19:57:47 | 0 | 3492 | 19:57:44.206 | #98 19:53:10.204 | 通过 |
| A-def-1 | `run` 构成 A；G1；tag `m3a1` | deb158a5… | 20:01:19 | 20:06:00 | 0 | 14261 | 20:05:57.598 | #98 20:01:23.594 | 通过 |
| B-dbg-1 | `run` 构成 B + debug；G1；tag `m3bd1` | deb158a5… | 20:07:31 | 20:12:14 | 0 | 2003260 | 20:12:09.572 | #103 20:07:35.562 | 通过 |
| A-dbg-1 | `run` 构成 A + debug；G1；tag `m3ad1` | deb158a5… | 20:13:46 | 20:18:28 | 0 | 1488221 | 20:18:23.777 | #103 20:13:49.776 | 通过 |
| ramp-B | `ramp` 构成 B；G1；tag `m3r2` | deb158a5… | 20:21:29 | 20:27:25 | 0 | 3095 | 20:27:24.326 | #98 20:21:32.912 | 通过 |
| ramp-A-2s | `ramp` 构成 A + debug + `max:2000`；G1；tag `m3r1s` | deb158a5… | 20:29:26 | 20:31:31 | 0 | 110593 | 20:31:31.512 | #103 20:29:30.306 | 通过 |
| B-def-2 | `run` 构成 B；G1；tag `m3b2` | deb158a5… | 20:32:32 | 20:37:14 | 0 | 3500 | 20:37:11.068 | #98 20:32:37.065 | 通过 |
| B-def-g0-3 | `run` 构成 B；G0（基线 dll）；tag `m3bg03` | b3d6c286… | 20:39:46 | 20:44:27 | 0 | 3524 | 20:44:24.152 | #98 20:39:50.143 | 通过 |
| A-def-2 | `run` 构成 A；G1；tag `m3a2` | deb158a5… | 20:46:28 | 20:51:09 | 0 | 14230 | 20:51:06.631 | #98 20:46:32.628 | 通过 |
| B-dbg-2 | `run` 构成 B + debug；G1；tag `m3bd2` | deb158a5… | 20:52:40 | 20:57:23 | 0 | 2000551 | 20:57:18.595 | #103 20:52:44.592 | 通过 |
| A-dbg-2 | `run` 构成 A + debug；G1；tag `m3ad2` | deb158a5… | 21:01:25 | 21:06:06 | 0 | 1461800 | 21:06:03.083 | #103 21:01:29.081 | 通过 |
| ramp-A | `ramp` 构成 A；G1；tag `m3r1` | deb158a5… | 21:08:08 | 21:09:02 | 0 | 3202 | 21:09:02.569 | #98 21:08:12.541 | 通过 |
| B-def-f256 | `run` 构成 B + 窗口 256；G1；tag `m3bf` | deb158a5… | 21:09:03 | 21:13:45 | 0 | 3508 | 21:13:41.463 | #98 21:09:07.461 | 通过 |
| A-def-f256 | `run` 构成 A + 窗口 256；G1；tag `m3af` | deb158a5… | 21:15:46 | 21:20:27 | 0 | 14273 | 21:20:24.523 | #98 21:15:50.520 | 通过 |
| B-dbg-f256 | `run` 构成 B + debug + 窗口 256；G1；tag `m3bfd` | deb158a5… | 21:21:29 | 21:26:11 | 0 | 1985808 | 21:26:06.894 | #103 21:21:32.893 | 通过 |
| A-dbg-f256 | `run` 构成 A + debug + 窗口 256；G1；tag `m3afd` | deb158a5… | 21:28:43 | 21:33:25 | 0 | 1463166 | 21:33:21.214 | #103 21:28:47.205 | 通过 |
| B-def-g0 | `run` 构成 B；G0（基线 dll）；tag `m3bg0` | b3d6c286… | 21:35:56 | 21:40:38 | 0 | 3499 | 21:40:34.708 | #99 21:36:00.706 | 通过 |

- 18 次的退出码都是 0。
- 解析自检：6 次 debug `run` 的工具解析行数与 `grep` 计数逐次相等，数值字段解析失败 0（`../logs/task-32-tool.txt` §4）。失败行自 TASK-31 多了 `connection {SourceConnection}` 字段，本次没有失败行，完成行模板未变。ramp-A-2s 不生成 summary.json，没有自检。

## 4. 判定表

越线项：

（无越线项）

逐条：

| 规则 | 运行 | 值 | 越线 | 样本 |
|---|---:|---:|---:|---:|
| S1 inner command | B-def-1 | 100.7 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-def-g0-2 | 104.1 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command | A-def-1 | 107.8 ms RefillOnlineSouthernTicketCommand | 否 | 干净 |
| S1 inner command | B-dbg-1 | 102.1 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | B-dbg-1 | 232.9 ms inbound:GameSelectCharacter at t=0.53 s | 否 | 干净 |
| S1 inner command | A-dbg-1 | 134.2 ms RefillOnlineSouthernTicketCommand | 否 | 干净 |
| S1 executor job | A-dbg-1 | 269.3 ms inbound:GameSelectCharacter at t=19.04 s | 否 | 干净 |
| S1 inner command (whole log) | ramp-B | 104.0 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command (whole log) | ramp-A-2s | 101.4 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job (after readiness) | ramp-A-2s | 233.0 ms inbound:GameSelectCharacter (99741 job lines) | 否 | 干净 |
| S1 inner command | B-def-2 | 103.3 ms PrepareSelectedCharacterStateCommand | 否 | 受污染 |
| S1 inner command | B-def-g0-3 | 100.5 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | A-def-2 | 101.3 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-dbg-2 | 99.03 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | B-dbg-2 | 230.3 ms inbound:GameSelectCharacter at t=0.53 s | 否 | 干净 |
| S1 inner command | A-dbg-2 | 105.2 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | A-dbg-2 | 234.0 ms inbound:GameSelectCharacter at t=0.67 s | 否 | 干净 |
| S1 inner command (whole log) | ramp-A | 100.7 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-def-f256 | 99.29 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | A-def-f256 | 103.9 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 inner command | B-dbg-f256 | 103.5 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | B-dbg-f256 | 234.8 ms inbound:GameSelectCharacter at t=0.53 s | 否 | 干净 |
| S1 inner command | A-dbg-f256 | 104.3 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S1 executor job | A-dbg-f256 | 237.1 ms inbound:GameSelectCharacter at t=0.67 s | 否 | 干净 |
| S1 inner command | B-def-g0 | 104.4 ms PrepareSelectedCharacterStateCommand | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-1 | 231.9 ; 231.9 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-g0-2 | 235.2 ; 235.2 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-g0-2 | 0 (judged-only 0) | 否 | 受污染 |
| S2 e2e >= 2 s on both series (d) | B-def-g0-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-g0-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-1 | 1150.5 ; 1150.5 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-1 | 234.3 ; 234.3 ms | 否 | 干净 |
| S2 queue wait max (cold) | B-dbg-1 | 233.8 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (steady) | B-dbg-1 | 61.22 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-1 | 89.91 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-1 | 39.80 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-1 | 944.8 ; 126.1 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-1 | 664.5 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-1 | 190.8 ms inbound:GameSetUserPosition | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-1 | 153.0 ms event:southern-dale.daily-refill-notifications | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-1 | 182.2 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-1 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-2 | 232.4 ; 232.4 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-2 | 0 (judged-only 0) | 否 | 受污染 |
| S2 e2e >= 2 s on both series (d) | B-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-g0-3 | 234.0 ; 234.0 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-g0-3 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-g0-3 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-g0-3 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-2 | 954.3 ; 59.97 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-2 | 235.9 ; 235.9 ms | 否 | 干净 |
| S2 queue wait max (cold) | B-dbg-2 | 234.9 ms event:achievement.inventory | 否 | 干净 |
| S2 queue wait max (steady) | B-dbg-2 | 60.45 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-2 | 10.89 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-2 | 39.90 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-2 | 888.4 ; 60.59 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-2 | 872.5 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-2 | 170.8 ms inbound:GameSetUserPosition | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-2 | 132.0 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-2 | 189.9 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-2 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-f256 | 232.6 ; 232.6 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-def-f256 | 1231.1 ; 1231.1 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-def-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-dbg-f256 | 236.1 ; 236.1 ms | 否 | 干净 |
| S2 queue wait max (cold) | B-dbg-f256 | 235.7 ms event:quest.inventory | 否 | 干净 |
| S2 queue wait max (steady) | B-dbg-f256 | 65.40 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (burst) | B-dbg-f256 | 62.61 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 queue wait max (flood) | B-dbg-f256 | 38.94 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | A-dbg-f256 | 801.7 ; 66.77 ms | 否 | 干净 |
| S2 queue wait max (cold) | A-dbg-f256 | 704.0 ms http:POST /api/launcher/v1/register | 否 | 干净 |
| S2 queue wait max (steady) | A-dbg-f256 | 190.4 ms timer:TournamentRewardSweepService | 否 | 干净 |
| S2 queue wait max (burst) | A-dbg-f256 | 112.9 ms event:quest.inventory | 否 | 干净 |
| S2 queue wait max (flood) | A-dbg-f256 | 142.5 ms inbound:GameConnectionCheck | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | A-dbg-f256 | 0 (judged-only 0) | 否 | 干净 |
| S2 (f) longest stall (judged; reference) | B-def-g0 | 233.1 ; 233.1 ms | 否 | 干净 |
| S2 e2e >= 2 s on both series (steady) | B-def-g0 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (d) | B-def-g0 | 0 (judged-only 0) | 否 | 干净 |
| S2 e2e >= 2 s on both series (e) | B-def-g0 | 0 (judged-only 0) | 否 | 干净 |
| S3 (p99 50 ms ramp; 2 s side read from its steps) | ramp-B | 24999.0 packets/s, stop: 全部档位通过（未触顶，最高档即下限） | 否 | 受污染 |
| S3 (2 s criterion) | ramp-A-2s | 1163.0 packets/s, stop: 判定 max ≥ 2000 ms | 否 | 干净 |
| S5 | B-def-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-def-g0-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 受污染 |
| S5 | A-def-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-dbg-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | A-dbg-1 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 28 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 9, 'Game handler failed' 0 | 否 | 干净 |
| S5 | ramp-B | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 22 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 9, 'Game handler failed' 0 | 否 | 受污染 |
| S5 | ramp-A-2s | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 22 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 9, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-def-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 受污染 |
| S5 | B-def-g0-3 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | A-def-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-dbg-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | A-dbg-2 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | ramp-A | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-def-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | A-def-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 34 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 9, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-dbg-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | A-dbg-f256 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 0 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |
| S5 | B-def-g0 | errors 0, failed commands 0, failed jobs 0, zero-signals 0, OverLimit 0, sender-layer lines T 0 / P 2 / D 0 / counted 0, S5-hit lines 0, F-126 shape 0, 'Writing is not allowed' 0, 'Game handler failed' 0 | 否 | 干净 |

### 4.1 判定规则（摘要；全文在判定日志开头）

- S1～S4 与 M-2 相同；S5 的第 1～4 项与 M-2 相同，第 5 项改按 `../t009-logs-and-cases.md` §4 的五条执行，见下一节。
- 「相对 M-2 显著变差」只记录，不停止；基线重测的触发条件改为「超过 M-2 的 3 倍」（TASK-32 主控方裁定第 6 条）。

### 4.2 发送端层的行：归类规则与结果

§4 的五条在判定脚本里的执行方式（`classify.py`，规则全文在判定日志开头，这里只列要点）：

- 归类的行：Warning 及以上的 `Outbound connection … closed: TimedOut|Failed`、`Outbound entry for …: TimedOut|Failed|OverLimit`、`Closing an outbound connection failed`、`An outbound delivery observer failed`、通知总线告警；以及带后缀 `; the connection was already gone` 的两种 Information 行（M-2 只扫 Warning 及以上，本单元加扫这两种 Information 行，否则 §4 第 3 条会变成看不见的口径变化）。
- 先满足的为准：
  0. `OverLimit` 任何时刻都计入（§4 第 2 条）；
  1. **T 收尾**：行的时刻 ≥ 收尾窗口起点，不计；
  2. **P 就绪探测**：该行所属连接同时满足 P1 编号最小、P2 没有任何非发送端层的行提到它、P3 每一行都在 bootstrap 200 之后 5 s 之内、P4 只有 `Failed`。带后缀的 Information 不计；Warning `Failed` 且是 `SocketException` 时按 F-126 记录、不计；其余 Warning 即 S5 命中（规则不预先放宽）；
  3. **D (d) 轮的登录连接**：不是 P，只被发送端层的行提到，每一行都在某一轮的时间窗 `[该轮第一个 LOGIN 发出, 该轮最后一个 LOGIN 应答 ＋ 1 s]` 内，且这类连接数不超过 (d) 登录数，不计；
  4. **计入**：其余全部，任何一行即 S5 命中。
- (d) 轮豁免与 M-2 原文的差别（主控方裁定第 4 条）：原文是「该连接断开后 1 s 内」；默认级别下服务端连接号与工具的登录号对不上，逐连接的断开时刻取不到，所以改按轮时间窗判。轮时间窗在 LOGIN 应答之前那一段比原文宽；应答之前的发送失败会让工具收不到应答，记为错误或超时，由 S5 第 1 项抓住。

**回归核对与反例**（第一次正式运行之前，输出全文在判定日志）：
- **回归核对（M-2 的 16 份 host.log）**：通过。归为 P 的恰好是 M-2 `ATTRIBUTED` 登记的 16 行（同样的 8 次运行、同样的行号，全部是 `Game:01-00-00-00` 上的 `Failed`）；归为「计入」的 0 行；归为 T 的 ramp-A-2s 24 行、A-dbg-f256 12 行、其余 0 行，24 ＋ 12 ＝ 36，与 M-2 的「收尾，不计」相同。那 16 行在 M-2 是 Warning（`ClientNotConnectedException`），按本规则的级别分支会是 S5 命中；这一点与回归核对无关，只说明 M-2 的现象若在 M-3 重现会被抓住。
- **反例与正例**（合成的 host.log 与 `samples.csv`）：20 项全部符合。R1（agent 连接，被 `Set user area` 提到）、R2（编号最小但在 bootstrap 之后 6 s）、R3（探测形态但结局 `TimedOut`）、R4（编号不是最小）、R5（某轮最后一个 LOGIN 应答之后 1.5 s）、R6（收尾窗口起点前 3 s 的 agent 连接 Information 行）、R7（收尾窗口内的 `OverLimit`）都归为「计入」；探测连接上带后缀的 Information（P）、(d) 轮窗内 `SocketException (32): Broken pipe` 的 Warning `Failed`（D，F-126）、收尾窗口内的 Warning `Failed`（T）都不计；另两项检验 P 的级别分支：探测连接上的 Warning `ClientNotConnectedException` 为 S5 命中，`SocketException (54): Connection reset by peer` 为 F-126、不计。

**M-3 的结果**（每次运行；P 一栏分级别）：

| 运行 | 收尾窗口起点 (UTC) | bootstrap 200 (UTC) | T 收尾，不计（按原因与级别） | P 就绪探测（按级别） | D (d) 登录连接 | 计入 | S5 命中行 | F-126 形态 | OverLimit（任何时刻） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| B-def-1 | 19:49:31.038 | 19:44:57.036 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| B-def-g0-2 | 19:57:44.206 | 19:53:10.204 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-def-1 | 20:05:57.598 | 20:01:23.594 | 0（0） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-dbg-1 | 20:12:09.572 | 20:07:35.562 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-dbg-1 | 20:18:23.777 | 20:13:49.776 | 28（connection closed Failed info suffix 5；connection closed Failed warn 9；entry outcome Failed info suffix 5；entry outcome Failed warn 9） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| ramp-B | 20:27:24.326 | 20:21:32.912 | 22（connection closed Failed info suffix 2；connection closed Failed warn 9；entry outcome Failed info suffix 2；entry outcome Failed warn 9） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| ramp-A-2s | 20:31:31.512 | 20:29:30.306 | 22（connection closed Failed info suffix 2；connection closed Failed warn 9；entry outcome Failed info suffix 2；entry outcome Failed warn 9） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-def-2 | 20:37:11.068 | 20:32:37.065 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| B-def-g0-3 | 20:44:24.152 | 20:39:50.143 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-def-2 | 20:51:06.631 | 20:46:32.628 | 0（0） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-dbg-2 | 20:57:18.595 | 20:52:44.592 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-dbg-2 | 21:06:03.083 | 21:01:29.081 | 0（0） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| ramp-A | 21:09:02.569 | 21:08:12.541 | 0（0） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-def-f256 | 21:13:41.463 | 21:09:07.461 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-def-f256 | 21:20:24.523 | 21:15:50.520 | 34（connection closed Failed info suffix 8；connection closed Failed warn 9；entry outcome Failed info suffix 8；entry outcome Failed warn 9） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-dbg-f256 | 21:26:06.894 | 21:21:32.893 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |
| A-dbg-f256 | 21:33:21.214 | 21:28:47.205 | 0（0） | 0（本次无级别证据） | 0 | 0 | 0 | 0 | 0 |
| B-def-g0 | 21:40:34.708 | 21:36:00.706 | 0（0） | 2（info+后缀 2，warn 0，其他 0） | 0 | 0 | 0 | 0 | 0 |

- **T 收尾，不计**：4 次运行有，A-dbg-1 28 行、ramp-B 22 行、ramp-A-2s 22 行、A-def-f256 34 行，28 ＋ 22 ＋ 22 ＋ 34 ＝ 106。其中 Warning 72 行（每次 9 对 entry／connection，18 × 4 ＝ 72），全是 `System.InvalidOperationException: Writing is not allowed after writer was completed`；带后缀的 Information 34 行（10 ＋ 4 ＋ 4 ＋ 16 ＝ 34），异常是 `System.IO.IOException`。全部在收尾窗口起点之后 0.007～0.231 s 内，起点前 1 s 内 0 行。
- **P 就绪探测**：构成 B 的 10 次运行（含三次控制运行与 ramp-B）每次恰好 2 行带后缀的 Information、0 行 Warning，10 × 2 ＝ 20；构成 A 的 8 次运行探测连接上 0 行，记「本次无级别证据」（§4 第 4 条），不算命中。**这就是级别生效的证据**：F-121 的降级覆盖了真实进程里的这条路径。
- **D** 0 行：(d) 轮的登录连接在 host.log 中一行都没有（M-2 同样如此）。
- **计入** 0 行，**F-126 形态** 0 行。
- `Writing is not allowed after writer was completed` 的计数口径不变（§4 第 5 条）：只出现在上述 4 次运行的收尾 Warning 的异常文本里，每次 9 行；失败作业 0，所以不涉及 S5 第 2 项。
- **F-121 在真实进程中的表现**（全部运行合计，`Failed` 结局）：带后缀的 Information 54 行（P 20 ＋ T 34），Warning 72 行（全在 T），20 ＋ 34 ＋ 72 ＝ 126。连接已不在时按 Information 记，生效；收尾时 Warning 的那 72 行是另一种形态（读码推断：发送端判定时连接仍可解析，`OutboundSender.cs` 的 `IsGone` 为假，而 TouchSocket 的写端已经完成），F-121 的判定不把它当作「连接已不在」，登记为 F-127（§8）。M-2 收尾窗口里的 36 行 Warning 也是这种异常。

日志开销（TASK-28 §4.3）：debug 组与默认级别组的端到端 p99 逐窗口对照，没有一个窗口超过 2 倍（最接近的是 A-1 冷启动窗，246.8 ÷ 127.9 ＝ 1.93 倍）。debug 组的入队等待都按正常数据使用。

| 运行对 | 窗口 | 默认级别 | debug | 差 | debug > 2 × 默认 |
|---|---:|---:|---:|---:|---:|
| B-dbg-1 / B-def-1 | cold | 40.31 | 42.86 | 2.55 | 否 |
| B-dbg-1 / B-def-1 | steady | 19.22 | 21.48 | 2.25 | 否 |
| B-dbg-1 / B-def-1 | (d) | 91.26 | 98.46 | 7.20 | 否 |
| B-dbg-1 / B-def-1 | (e) | 9.96 | 8.98 | -0.98 | 否 |
| A-dbg-1 / A-def-1 | cold | 127.9 | 246.8 | 119.0 | 否 |
| A-dbg-1 / A-def-1 | steady | 99.03 | 101.8 | 2.73 | 否 |
| A-dbg-1 / A-def-1 | (d) | 160.9 | 164.6 | 3.65 | 否 |
| A-dbg-1 / A-def-1 | (e) | 96.13 | 108.8 | 12.63 | 否 |
| B-dbg-2 / B-def-2 | cold | 42.90 | 39.86 | -3.03 | 否 |
| B-dbg-2 / B-def-2 | steady | 19.84 | 15.59 | -4.25 | 否 |
| B-dbg-2 / B-def-2 | (d) | 92.92 | 102.0 | 9.05 | 否 |
| B-dbg-2 / B-def-2 | (e) | 10.41 | 8.94 | -1.46 | 否 |
| A-dbg-2 / A-def-2 | cold | 171.6 | 218.1 | 46.50 | 否 |
| A-dbg-2 / A-def-2 | steady | 99.84 | 98.94 | -0.90 | 否 |
| A-dbg-2 / A-def-2 | (d) | 144.4 | 151.0 | 6.67 | 否 |
| A-dbg-2 / A-def-2 | (e) | 111.6 | 122.8 | 11.15 | 否 |
| B-dbg-f256 / B-def-f256 | cold | 41.18 | 38.72 | -2.46 | 否 |
| B-dbg-f256 / B-def-f256 | steady | 19.62 | 19.47 | -0.14 | 否 |
| B-dbg-f256 / B-def-f256 | (d) | 93.88 | 89.38 | -4.49 | 否 |
| B-dbg-f256 / B-def-f256 | (e) | 8.30 | 7.63 | -0.67 | 否 |
| A-dbg-f256 / A-def-f256 | cold | 140.7 | 139.4 | -1.34 | 否 |
| A-dbg-f256 / A-def-f256 | steady | 107.9 | 106.5 | -1.43 | 否 |
| A-dbg-f256 / A-def-f256 | (d) | 112.8 | 150.9 | 38.13 | 否 |
| A-dbg-f256 / A-def-f256 | (e) | 116.7 | 109.2 | -7.51 | 否 |

## 5. 与基线、M-1、M-2 对照

### 5.1 主要数字（四方）

- 列的来源：前三列是 TASK-03 的基线（`raw/baseline-1.summary.json`、`raw/clean-1.summary.json`、`raw/clean-2.summary.json`）；其后四列是 M-1 默认级别组（`raw/m1-*.summary.json`），再四列是 M-2 默认级别组（`raw/m2-*.summary.json`）；再后是 M-3 的默认级别组、debug 组、窗口 256 的四次与三次控制运行。
- 读法：判定序列，「样本 / p50 / p99 / p99.9 / max」，单位 ms。带 * 的 n < 1000（p99.9 即 max），带 ** 的 n < 100（p99 也即 max）。窗口 256 只填 (e) 相关行。

| 量 | base-A | base-B-1 | base-B-2 | M1-A-def-1 | M1-A-def-2 | M1-B-def-1 | M1-B-def-2 | M2-A-def-1 | M2-A-def-2 | M2-B-def-1 | M2-B-def-2 | M3-A-def-1 | M3-A-def-2 | M3-B-def-1 | M3-B-def-2 | M3-A-dbg-1 | M3-A-dbg-2 | M3-B-dbg-1 | M3-B-dbg-2 | M3-A-def-f256 | M3-B-def-f256 | M3-A-dbg-f256 | M3-B-dbg-f256 | M3-B-def-g0-2 | M3-B-def-g0-3 | M3-B-def-g0 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 稳态窗实际包速率（包/秒，目标 400） | 4.7 | 400.0 | 400.0 | 400.2 | 400.1 | 400.0 | 400.0 | 400.1 | 400.0 | 400.0 | 400.1 | 400.0 | 400.0 | 400.0 | 400.0 | 399.9 | 400.0 | 400.0 | 400.0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 400.0 | 400.0 | 400.0 |
| 发出滞后（实际 − 计划） | 30** / 0.00 / 0.00 / 0.00 / 0.00 | 47424 / 0.05 / 2.34 / 5.72 / 17.80 | 47432 / 0.25 / 2.25 / 3.62 / 35.09 | 45854 / 0.95 / 40.48 / 74.28 / 136.7 | 45882 / 0.38 / 43.40 / 89.37 / 124.8 | 47434 / 0.20 / 2.22 / 2.74 / 13.45 | 47430 / 0.07 / 2.18 / 3.26 / 39.20 | 45846 / 0.28 / 52.03 / 126.4 / 187.0 | 45860 / 0.24 / 44.40 / 86.25 / 200.6 | 47430 / 0.00 / 2.22 / 2.91 / 39.42 | 47446 / 0.08 / 2.13 / 2.90 / 23.18 | 45857 / 0.15 / 46.68 / 80.98 / 112.9 | 45870 / 0.00 / 45.47 / 87.89 / 145.6 | 47426 / 0.15 / 2.20 / 2.88 / 32.18 | 47426 / 0.06 / 1.97 / 4.45 / 36.82 | 45874 / 0.48 / 47.76 / 96.53 / 149.2 | 45857 / 0.72 / 45.26 / 90.84 / 120.9 | 47438 / 0.12 / 2.10 / 12.89 / 74.25 | 47440 / 0.11 / 2.17 / 4.74 / 28.57 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47430 / 0.14 / 2.35 / 2.74 / 11.47 | 47422 / 0.00 / 2.27 / 3.09 / 38.62 | 47432 / 0.08 / 2.17 / 3.02 / 46.99 |
| 稳态窗全部计入的请求 | 19230（截尾 19200） / 170864.1 / 229761.2 / 230860.7 / ≥230961.2 | 47388 / 0.27 / 2.79 / 24.51 / 267.5 | 47396 / 0.43 / 2.73 / 20.58 / 41.45 | 45818 / 1.80 / 91.65 / 124.2 / 165.2 | 45846 / 1.45 / 94.56 / 136.7 / 174.9 | 47398 / 0.43 / 16.58 / 40.91 / 60.37 | 47394 / 0.32 / 18.38 / 47.71 / 89.19 | 45810 / 1.75 / 103.9 / 177.3 / 237.0 | 45824 / 1.20 / 97.49 / 136.3 / 223.4 | 47394 / 0.35 / 26.07 / 48.18 / 72.02 | 47410 / 0.39 / 20.52 / 47.10 / 58.54 | 45821 / 1.19 / 99.03 / 134.6 / 175.7 | 45835 / 0.92 / 99.84 / 137.2 / 187.4 | 47390 / 0.47 / 19.22 / 46.47 / 61.41 | 47390 / 0.39 / 19.84 / 51.75 / 60.86 | 45838 / 1.51 / 101.8 / 146.5 / 199.2 | 45821 / 1.68 / 98.94 / 141.9 / 181.0 | 47402 / 0.47 / 21.48 / 51.37 / 77.80 | 47404 / 0.47 / 15.59 / 45.16 / 61.04 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47394 / 0.45 / 19.32 / 47.94 / 64.94 | 47386 / 0.33 / 17.92 / 46.05 / 60.80 | 47396 / 0.45 / 17.40 / 45.97 / 59.63 |
| 参考：稳态窗 | 30** / 498.5 / 32366.4 / 32366.4 / 32366.4 | 47388 / 0.18 / 0.48 / 24.11 / 267.5 | 47396 / 0.18 / 0.56 / 19.90 / 40.54 | 45818 / 0.27 / 88.41 / 123.4 / 165.2 | 45846 / 0.22 / 89.24 / 125.3 / 174.9 | 47398 / 0.19 / 16.09 / 40.90 / 60.06 | 47394 / 0.18 / 17.69 / 47.49 / 89.01 | 45810 / 0.25 / 99.52 / 144.7 / 220.3 | 45824 / 0.24 / 93.01 / 135.7 / 169.8 | 47394 / 0.21 / 25.63 / 47.91 / 58.68 | 47410 / 0.20 / 20.28 / 46.81 / 57.68 | 45821 / 0.28 / 96.49 / 127.3 / 175.7 | 45835 / 0.28 / 94.69 / 134.1 / 187.2 | 47390 / 0.22 / 18.81 / 45.91 / 61.16 | 47390 / 0.21 / 19.30 / 51.38 / 60.59 | 45838 / 0.29 / 97.57 / 137.2 / 197.9 | 45821 / 0.34 / 93.64 / 140.4 / 172.2 | 47402 / 0.23 / 19.85 / 48.65 / 61.85 | 47404 / 0.23 / 14.74 / 44.92 / 60.89 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 47394 / 0.22 / 18.59 / 47.60 / 63.85 | 47386 / 0.21 / 17.56 / 46.05 / 60.08 | 47396 / 0.23 / 16.92 / 45.28 / 59.07 |
| 稳态 CHECK_USER_CONNECTION | 2** / 0.11 / 0.15 / 0.15 / 0.15 | 26921 / 0.24 / 2.49 / 6.46 / 42.79 | 26882 / 0.43 / 2.47 / 4.21 / 36.30 | 25583 / 1.77 / 91.19 / 123.8 / 164.1 | 25436 / 1.38 / 94.67 / 142.2 / 174.9 | 26768 / 0.43 / 15.78 / 40.93 / 60.37 | 26872 / 0.30 / 17.41 / 45.15 / 89.19 | 25512 / 1.59 / 102.7 / 171.0 / 236.3 | 25490 / 1.07 / 95.33 / 137.2 / 200.5 | 26794 / 0.31 / 21.44 / 45.30 / 54.99 | 26791 / 0.34 / 17.87 / 42.60 / 55.48 | 25497 / 1.10 / 96.48 / 131.6 / 162.9 | 25527 / 0.80 / 95.08 / 136.3 / 187.2 | 26940 / 0.44 / 17.48 / 43.00 / 58.67 | 26771 / 0.33 / 18.08 / 50.38 / 60.86 | 25555 / 1.45 / 97.69 / 137.2 / 199.0 | 25757 / 1.62 / 97.46 / 141.9 / 171.0 | 26860 / 0.41 / 20.96 / 51.08 / 75.39 | 26834 / 0.42 / 13.28 / 45.06 / 60.88 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 26713 / 0.43 / 15.38 / 45.37 / 60.14 | 26941 / 0.29 / 15.49 / 44.78 / 60.80 | 26783 / 0.41 / 16.46 / 42.28 / 56.20 |
| 稳态 SET_USER_POSITION | 1** / 0.16 / 0.16 / 0.16 / 0.16 | 20215 / 0.30 / 2.89 / 25.93 / 55.98 | 20266 / 0.43 / 2.83 / 18.95 / 35.80 | 18413 / 1.72 / 90.28 / 131.6 / 165.2 | 18598 / 1.35 / 93.12 / 123.6 / 161.1 | 20383 / 0.43 / 12.75 / 40.90 / 56.47 | 20273 / 0.35 / 16.01 / 52.57 / 87.84 | 18470 / 1.74 / 102.1 / 180.1 / 237.0 | 18513 / 1.13 / 94.53 / 129.7 / 223.4 | 20351 / 0.39 / 23.72 / 47.64 / 57.48 | 20375 / 0.44 / 18.47 / 46.59 / 58.54 | 18501 / 1.12 / 98.41 / 129.0 / 160.7 | 18493 / 0.86 / 97.29 / 135.3 / 187.1 | 20199 / 0.51 / 15.26 / 42.86 / 61.41 | 20368 / 0.46 / 14.29 / 50.72 / 58.43 | 18472 / 1.42 / 98.96 / 143.7 / 199.2 | 18242 / 1.63 / 94.51 / 136.5 / 171.1 | 20297 / 0.54 / 16.19 / 49.39 / 72.53 | 20324 / 0.51 / 11.19 / 42.65 / 59.03 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 20432 / 0.46 / 18.00 / 46.62 / 59.99 | 20192 / 0.36 / 13.74 / 43.63 / 58.22 | 20365 / 0.48 / 12.77 / 44.58 / 58.08 |
| 稳态 SET_USER_AREA | 0 | 0 | 0 | 1142 / 21.15 / 106.2 / 132.1 / 146.4 | 1142 / 22.57 / 108.3 / 138.9 / 143.3 | 0 | 0 | 1145 / 29.67 / 117.5 / 185.2 / 235.2 | 1141 / 27.83 / 110.1 / 140.5 / 146.6 | 0 | 0 | 1144 / 28.36 / 117.0 / 148.5 / 168.0 | 1142 / 29.17 / 112.5 / 162.0 / 187.4 | 0 | 0 | 1140 / 28.34 / 119.0 / 141.5 / 194.6 | 1141 / 27.96 / 116.8 / 139.6 / 170.9 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 | 0 | 0 |
| 稳态 SELECT_DUNGEON | 0 | 36** / 16.68 / 267.5 / 267.5 / 267.5 | 35** / 16.27 / 21.42 / 21.42 / 21.42 | 36** / 10.11 / 74.58 / 74.58 / 74.58 | 34** / 10.52 / 77.47 / 77.47 / 77.47 | 35** / 16.21 / 23.61 / 23.61 / 23.61 | 35** / 15.87 / 22.47 / 22.47 / 22.47 | 36** / 10.48 / 74.80 / 74.80 / 74.80 | 36** / 9.96 / 67.81 / 67.81 / 67.81 | 35** / 15.13 / 28.89 / 28.89 / 28.89 | 34** / 14.81 / 21.06 / 21.06 / 21.06 | 36** / 9.92 / 68.58 / 68.58 / 68.58 | 35** / 9.96 / 68.89 / 68.89 / 68.89 | 36** / 15.71 / 25.27 / 25.27 / 25.27 | 36** / 16.15 / 26.11 / 26.11 / 26.11 | 34** / 10.48 / 120.7 / 120.7 / 120.7 | 36** / 10.33 / 66.42 / 66.42 / 66.42 | 34** / 14.95 / 22.05 / 22.05 / 22.05 | 34** / 15.49 / 27.81 / 27.81 / 27.81 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 35** / 15.58 / 23.99 / 23.99 / 23.99 | 36** / 14.96 / 23.18 / 23.18 / 23.18 | 35** / 16.26 / 21.73 / 21.73 / 21.73 |
| 稳态 SELECT_CHARACTER | 16** / 30664.1 / 32366.4 / 32366.4 / 32366.4 | 0 | 0 | 197* / 4.27 / 76.39 / 83.46 / 83.46 | 197* / 4.18 / 70.26 / 107.3 / 107.3 | 0 | 0 | 197* / 67.92 / 176.6 / 220.3 / 220.3 | 197* / 68.11 / 164.7 / 169.8 / 169.8 | 0 | 0 | 197* / 70.73 / 138.4 / 175.7 / 175.7 | 197* / 67.93 / 173.2 / 173.5 / 173.5 | 0 | 0 | 197* / 69.48 / 172.5 / 179.4 / 179.4 | 197* / 67.05 / 170.0 / 172.2 / 172.2 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 | 0 | 0 |
| 稳态 CREATE_CHARACTER | 3** / 266.8 / 30222.8 / 30222.8 / 30222.8 | 0 | 0 | 35** / 3.40 / 93.05 / 93.05 / 93.05 | 35** / 3.22 / 111.5 / 111.5 / 111.5 | 0 | 0 | 35** / 3.41 / 108.4 / 108.4 / 108.4 | 35** / 3.40 / 92.27 / 92.27 / 92.27 | 0 | 0 | 35** / 3.51 / 11.68 / 11.68 / 11.68 | 35** / 3.20 / 67.36 / 67.36 / 67.36 | 0 | 0 | 35** / 3.29 / 126.0 / 126.0 / 126.0 | 35** / 3.44 / 63.10 / 63.10 / 63.10 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 | 0 | 0 |
| (f) 最长单次停顿（判定；参考） | ≥284314.1；– | 575.9；575.9 | 1118.5；1118.5 | 921.2；139.2 | 1189.6；1189.6 | 133.0；133.0 | 132.4；132.4 | 968.5；968.5 | 1053.1；212.2 | 236.3；236.3 | 247.4；247.4 | 1150.5；1150.5 | 954.3；59.97 | 231.9；231.9 | 232.4；232.4 | 944.8；126.1 | 888.4；60.59 | 234.3；234.3 | 235.9；235.9 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 235.2；235.2 | 234.0；234.0 | 233.1；233.1 |
| (f) 冷启动窗整体 | 8880（截尾 6834） / 249010.8 / 282660.7 / 284010.7 / ≥284314.1 | 22347 / 0.27 / 4.07 / 26.46 / 575.9 | 22265 / 0.42 / 4.20 / 66.45 / 1118.5 | 22019 / 1.81 / 217.7 / 818.2 / 921.2 | 21917 / 1.51 / 177.3 / 612.9 / 1189.6 | 22559 / 0.45 / 31.25 / 80.94 / 133.0 | 22533 / 0.33 / 30.71 / 79.91 / 132.4 | 21925 / 1.83 / 151.1 / 530.2 / 968.5 | 21957 / 1.39 / 248.6 / 889.2 / 1053.1 | 22513 / 0.38 / 40.92 / 105.4 / 236.3 | 22455 / 0.39 / 39.67 / 108.2 / 247.4 | 21905 / 1.35 / 127.9 / 523.2 / 1150.5 | 21967 / 1.20 / 171.6 / 678.3 / 954.3 | 22497 / 0.50 / 40.31 / 104.1 / 231.9 | 22522 / 0.41 / 42.90 / 100.8 / 232.4 | 21978 / 1.58 / 246.8 / 780.7 / 944.8 | 21928 / 1.73 / 218.1 / 829.2 / 888.4 | 22518 / 0.47 / 42.86 / 97.41 / 234.3 | 22518 / 0.47 / 39.86 / 101.0 / 235.9 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 22544 / 0.50 / 41.77 / 84.78 / 235.2 | 22473 / 0.32 / 39.18 / 102.9 / 234.0 | 22535 / 0.45 / 37.74 / 96.73 / 233.1 |
| (d) 10 并发登录轮内的背景请求 | 12167（截尾 12144） / 69864.1 / 108164.1 / 108861.2 / ≥108914.1 | 3081 / 0.17 / 208.5 / 871.0 / 1025.1 | 3072 / 0.28 / 2.95 / 566.5 / 705.0 | 540* / 15.40 / 71.46 / 89.35 / 89.35 | 531* / 39.33 / 133.4 / 133.7 / 133.7 | 477* / 32.88 / 96.49 / 101.3 / 101.3 | 473* / 31.40 / 98.29 / 99.83 / 99.83 | 519* / 33.35 / 138.8 / 180.8 / 180.8 | 595* / 37.56 / 205.8 / 237.2 / 237.2 | 474* / 6.90 / 84.27 / 85.01 / 85.01 | 504* / 9.06 / 100.1 / 102.4 / 102.4 | 554* / 45.31 / 160.9 / 164.0 / 164.0 | 568* / 32.50 / 144.4 / 146.5 / 146.5 | 477* / 27.07 / 91.26 / 100.9 / 100.9 | 478* / 29.93 / 92.92 / 96.84 / 96.84 | 539* / 38.59 / 164.6 / 174.8 / 174.8 | 606* / 23.66 / 151.0 / 158.5 / 158.5 | 483* / 13.82 / 98.46 / 103.0 / 103.0 | 474* / 39.51 / 102.0 / 104.1 / 104.1 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 460* / 14.46 / 96.60 / 115.0 / 115.0 | 449* / 5.07 / 80.54 / 82.74 / 82.74 | 462* / 15.64 / 86.32 / 93.63 / 93.63 |
| 参考：(d) 轮内 | 23** / 1186.5 / 32304.4 / 32304.4 / 32304.4 | 3081 / 0.08 / 0.19 / 605.5 / 1025.1 | 3072 / 0.09 / 0.19 / 13.83 / 703.2 | 540* / 9.91 / 69.83 / 87.34 / 87.34 | 531* / 14.55 / 124.8 / 124.9 / 124.9 | 477* / 18.82 / 89.30 / 96.42 / 96.42 | 473* / 3.13 / 87.85 / 97.43 / 97.43 | 519* / 25.66 / 133.1 / 133.2 / 133.2 | 595* / 11.66 / 185.4 / 204.8 / 204.8 | 474* / 1.10 / 80.55 / 82.34 / 82.34 | 504* / 3.49 / 90.77 / 102.4 / 102.4 | 554* / 25.22 / 107.4 / 161.6 / 161.6 | 568* / 6.48 / 91.90 / 110.0 / 110.0 | 477* / 6.72 / 89.06 / 90.29 / 90.29 | 478* / 15.06 / 86.44 / 86.91 / 86.91 | 539* / 14.29 / 155.0 / 155.2 / 155.2 | 606* / 6.62 / 106.8 / 132.2 / 132.2 | 483* / 2.99 / 89.95 / 99.26 / 99.26 | 474* / 4.87 / 92.26 / 92.56 / 92.56 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 460* / 5.82 / 92.56 / 110.1 / 110.1 | 449* / 1.75 / 80.35 / 80.55 / 80.55 | 462* / 4.61 / 83.83 / 90.99 / 90.99 |
| (d) 领队连接轮内（判定） | 0 | 915* / 0.17 / 621.4 / 1025.1 / 1025.1 | 912* / 0.47 / 316.8 / 705.0 / 705.0 | 168* / 13.81 / 71.46 / 80.78 / 80.78 | 166* / 47.07 / 133.7 / 133.7 / 133.7 | 147* / 32.02 / 93.10 / 101.3 / 101.3 | 144* / 29.85 / 98.32 / 98.37 / 98.37 | 168* / 33.03 / 169.8 / 180.8 / 180.8 | 184* / 40.94 / 227.4 / 237.2 / 237.2 | 139* / 8.33 / 84.82 / 85.01 / 85.01 | 151* / 8.15 / 99.42 / 100.5 / 100.5 | 171* / 45.58 / 163.3 / 164.0 / 164.0 | 175* / 33.07 / 144.5 / 144.5 / 144.5 | 140* / 24.11 / 86.10 / 88.12 / 88.12 | 136* / 23.10 / 69.13 / 70.84 / 70.84 | 166* / 39.95 / 158.2 / 167.0 / 167.0 | 185* / 24.37 / 154.5 / 158.5 / 158.5 | 144* / 12.14 / 82.03 / 82.91 / 82.91 | 141* / 39.57 / 103.8 / 104.1 / 104.1 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 137* / 14.63 / 96.60 / 115.0 / 115.0 | 132* / 4.95 / 80.55 / 82.73 / 82.73 | 138* / 15.64 / 91.73 / 93.59 / 93.59 |
| (e) 灌包期间其余连接 | 4805（截尾 4800） / 16964.1 / 31664.1 / 31961.2 / ≥31964.1 | 11860 / 0.17 / 2.71 / 13.41 / 26.66 | 11875 / 0.30 / 2.71 / 12.09 / 28.08 | 11483 / 1.55 / 89.65 / 140.8 / 185.7 | 11497 / 1.21 / 86.34 / 113.8 / 124.9 | 11862 / 0.37 / 4.94 / 23.07 / 29.74 | 11861 / 0.18 / 4.42 / 24.95 / 29.54 | 11472 / 1.88 / 112.8 / 178.9 / 201.7 | 11470 / 1.29 / 106.6 / 145.8 / 227.3 | 11858 / 0.42 / 9.77 / 34.20 / 41.80 | 11869 / 0.59 / 11.20 / 31.55 / 47.42 | 11491 / 1.27 / 96.13 / 118.8 / 139.8 | 11485 / 1.22 / 111.6 / 148.3 / 215.8 | 11868 / 0.63 / 9.96 / 32.36 / 47.18 | 11871 / 0.54 / 10.41 / 33.72 / 46.04 | 11544 / 1.48 / 108.8 / 167.8 / 185.8 | 11479 / 1.73 / 122.8 / 174.7 / 190.9 | 11862 / 0.60 / 8.98 / 31.42 / 41.15 | 11859 / 0.51 / 8.94 / 34.85 / 43.13 | 11491 / 2.11 / 116.7 / 158.2 / 205.9 | 11862 / 0.58 / 8.30 / 36.88 / 46.71 | 11472 / 0.98 / 109.2 / 120.7 / 153.3 | 11867 / 0.39 / 7.63 / 30.83 / 41.40 | 11858 / 0.70 / 9.84 / 31.26 / 46.98 | 11862 / 0.50 / 14.84 / 35.86 / 46.31 | 11858 / 0.51 / 8.47 / 30.06 / 44.19 |
| 参考：(e) | 5** / 2.12 / 30119.3 / 30119.3 / 30119.3 | 11860 / 0.07 / 0.50 / 11.39 / 26.66 | 11875 / 0.07 / 0.43 / 11.42 / 27.14 | 11483 / 0.22 / 83.76 / 139.9 / 185.7 | 11497 / 0.18 / 84.05 / 113.3 / 120.8 | 11862 / 0.08 / 4.58 / 21.29 / 29.24 | 11861 / 0.08 / 4.08 / 24.87 / 29.12 | 11472 / 0.28 / 104.3 / 177.3 / 201.7 | 11470 / 0.20 / 91.10 / 115.0 / 124.9 | 11858 / 0.10 / 9.77 / 33.55 / 40.03 | 11869 / 0.10 / 10.59 / 30.31 / 46.38 | 11491 / 0.27 / 93.72 / 110.6 / 139.8 | 11485 / 0.25 / 103.4 / 120.6 / 147.9 | 11868 / 0.10 / 8.90 / 32.36 / 45.18 | 11871 / 0.10 / 10.41 / 32.17 / 45.44 | 11544 / 0.23 / 105.9 / 166.2 / 185.8 | 11479 / 0.29 / 114.2 / 168.7 / 190.1 | 11862 / 0.10 / 8.52 / 31.10 / 40.23 | 11859 / 0.11 / 8.60 / 34.51 / 40.29 | 11491 / 0.38 / 110.8 / 131.6 / 148.7 | 11862 / 0.10 / 7.96 / 36.88 / 44.76 | 11472 / 0.21 / 108.8 / 120.4 / 142.7 | 11867 / 0.10 / 6.94 / 30.46 / 41.40 | 11858 / 0.09 / 9.12 / 31.26 / 45.75 | 11862 / 0.10 / 12.74 / 34.64 / 44.92 | 11858 / 0.10 / 8.11 / 30.06 / 43.73 |
| (e) 灌包连接自身应答速率（包/秒） | 199107 | 196658 | 195702 | 90443 | 92402 | 151416 | 152916 | 57043 | 59856 | 77863 | 77852 | 57157 | 57567 | 77178 | 77196 | 44806 | 44217 | 62697 | 62607 | 58302 | 77228 | 44256 | 62088 | 77749 | 76733 | 77335 |
| 命令超阈值项（p99.9 > 100 或 max > 250 ms） | 21 | 7 | 8 | 1 | 1 | 1 | 0 | 1 | 1 | 1 | 1 | 2 | 1 | 1 | 1 | 2 | 1 | 1 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 1 | 1 | 1 |
| 失败的命令 | 113 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 | 0 | 0 |
| 判定口径 > 250 ms 的请求 | 45372 | 27 | 27 | 213 | 168 | 0 | 0 | 157 | 219 | 0 | 0 | 149 | 177 | 0 | 0 | 209 | 203 | 0 | 0 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 0 | 0 | 0 |
| 错误（断线、超时） | 51 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| 双人组进本次数 | 0 | 79 | 78 | 80 | 78 | 79 | 78 | 80 | 80 | 81 | 79 | 82 | 79 | 79 | 78 | 80 | 79 | 79 | 80 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 同窗口 32 的运行 | 79 | 80 | 80 |
| Host 日志行数 | 3016 | 3498 | 3554 | 14363 | 14397 | 3595 | 3604 | 14261 | 14229 | 3509 | 3506 | 14261 | 14230 | 3501 | 3500 | 1488221 | 1461800 | 2003260 | 2000551 | 14273 | 3508 | 1463166 | 1985808 | 3492 | 3524 | 3499 |

### 5.2 M-2 → M-3 的变化

| 量 | M-1 | M-2 | M-3 | M-3 窗口 256 | M-2 → M-3（较小值之差～较大值之差） |
|---|---:|---:|---:|---:|---:|
| 稳态端到端 p99（构成 A） | 91.65／94.56 | 103.9／97.49 | 99.03／99.84 | 107.9 | 1.54～-4.02 |
| 稳态端到端 p99（构成 B） | 16.58／18.38 | 26.07／20.52 | 19.22／19.84 | 19.62 | -1.30～-6.23 |
| (d) 背景请求端到端 p99（构成 A） | 71.46／133.4 | 138.8／205.8 | 160.9／144.4 | 112.8 | 5.55～-44.92 |
| (d) 背景请求端到端 p99（构成 B） | 96.49／98.29 | 84.27／100.1 | 91.26／92.92 | 93.88 | 6.99～-7.18 |
| (e) 其余连接端到端 p99（构成 A） | 89.65／86.34 | 112.8／106.6 | 96.13／111.6 | 116.7 | -10.43～-1.17 |
| (e) 其余连接端到端 p99（构成 B） | 4.94／4.42 | 9.77／11.20 | 9.96／10.41 | 8.30 | 0.19～-0.79 |
| (f) 最长单次停顿（构成 A） | 921.2／1189.6 | 968.5／1053.1 | 1150.5／954.3 | 1231.1 | -14.23～97.39 |
| (f) 最长单次停顿（构成 B） | 133.0／132.4 | 236.3／247.4 | 231.9／232.4 | 232.6 | -4.37～-15.00 |
| (e) 灌包连接自身应答速率（构成 A） | 90443.1／92402.2 | 57043.2／59856.2 | 57157.1／57567.0 | 58302.2 | 113.9～-2289.2 |
| (e) 灌包连接自身应答速率（构成 B） | 151416.5／152915.9 | 77862.9／77852.5 | 77178.2／77195.8 | 77228.1 | -674.3～-667.1 |
| 稳态入队等待 p99（debug）（构成 A） | 86.10／86.49 | 94.31／99.75 | 93.98／89.78 | 97.53 | -4.53～-5.77 |
| 稳态入队等待 p99（debug）（构成 B） | 16.22／17.14 | 16.56／19.01 | 20.97／14.78 | 19.81 | -1.78～1.95 |
| (d) 入队等待 p99（debug）（构成 A） | 74.09／100.8 | 100.0／180.9 | 136.9／103.3 | 103.8 | 3.29～-44.02 |
| (d) 入队等待 p99（debug）（构成 B） | 11.64／17.16 | 1.00／25.92 | 17.32／2.42 | 53.83 | 1.43～-8.60 |
| (e) 入队等待 max（debug）（构成 A） | 190.5／127.9 | 144.4／165.5 | 182.2／189.9 | 142.5 | 37.89～24.37 |
| (e) 入队等待 max（debug）（构成 B） | 29.24／30.25 | 38.13／37.65 | 39.80／39.90 | 38.94 | 2.16～1.77 |
| 选角作业 p99（debug）（构成 A） | 132.8／130.7 | 130.0／142.6 | 137.0／128.8 | 140.8 | -1.18～-5.58 |
| 选角作业 p99（debug）（构成 B） | 232.8／233.7 | 227.5／229.9 | 232.9／230.3 | 234.8 | 2.80～2.99 |
| 选角作业 max（debug）（构成 A） | 236.1／229.5 | 229.6／239.1 | 269.3／234.0 | 237.1 | 4.45～30.26 |
| 选角作业 max（debug）（构成 B） | 232.8／233.7 | 227.5／229.9 | 232.9／230.3 | 234.8 | 2.80～2.99 |

「相对 M-2 显著变差」的逐项检验（TASK-32 §1.5；两次都 > 2 倍且超出差距才算）：

| 量 | M-2（两次） | M-2 差距 | M-3（两次） | M-3 ÷ M-2 较差值 | 显著变差（两次都 > 2 倍且超出差距） |
|---|---:|---:|---:|---:|---:|
| 稳态端到端 p99（构成 A） | 103.9／97.49 | 6.37 | 99.03／99.84 | 0.95／0.96 | 否 |
| 稳态端到端 p99（构成 B） | 26.07／20.52 | 5.55 | 19.22／19.84 | 0.74／0.76 | 否 |
| (d) 背景请求端到端 p99（构成 A） | 138.8／205.8 | 67.02 | 160.9／144.4 | 0.78／0.70 | 否 |
| (d) 背景请求端到端 p99（构成 B） | 84.27／100.1 | 15.84 | 91.26／92.92 | 0.91／0.93 | 否 |
| (e) 其余连接端到端 p99（构成 A） | 112.8／106.6 | 6.22 | 96.13／111.6 | 0.85／0.99 | 否 |
| (e) 其余连接端到端 p99（构成 B） | 9.77／11.20 | 1.43 | 9.96／10.41 | 0.89／0.93 | 否 |
| (f) 最长单次停顿（构成 A） | 968.5／1053.1 | 84.54 | 1150.5／954.3 | 1.09／0.91 | 否 |
| (f) 最长单次停顿（构成 B） | 236.3／247.4 | 11.14 | 231.9／232.4 | 0.94／0.94 | 否 |
| (e) 灌包连接自身应答速率（构成 A） | 57043.2／59856.2 | 2812.9 | 57157.1／57567.0 | 1.00／1.01 | 否 |
| (e) 灌包连接自身应答速率（构成 B） | 77862.9／77852.5 | 10.45 | 77178.2／77195.8 | 0.99／0.99 | 否 |
| 稳态端到端 p99（debug）（构成 A） | 103.9／114.5 | 10.60 | 101.8／98.94 | 0.89／0.86 | 否 |
| 稳态端到端 p99（debug）（构成 B） | 16.33／18.55 | 2.21 | 21.48／15.59 | 1.16／0.84 | 否 |
| (e) 其余连接端到端 p99（debug）（构成 A） | 110.7／107.1 | 3.59 | 108.8／122.8 | 0.98／1.11 | 否 |
| (e) 其余连接端到端 p99（debug）（构成 B） | 7.45／8.42 | 0.98 | 8.98／8.94 | 1.07／1.06 | 否 |
| (f) 最长单次停顿（debug）（构成 A） | 1020.2／1367.6 | 347.4 | 944.8／888.4 | 0.69／0.65 | 否 |
| (f) 最长单次停顿（debug）（构成 B） | 232.4／230.9 | 1.45 | 234.3／235.9 | 1.01／1.02 | 否 |
| 稳态入队等待 p99（构成 A） | 94.31／99.75 | 5.45 | 93.98／89.78 | 0.94／0.90 | 否 |
| 稳态入队等待 p99（构成 B） | 16.56／19.01 | 2.45 | 20.97／14.78 | 1.10／0.78 | 否 |
| (d) 入队等待 p99（构成 A） | 100.0／180.9 | 80.90 | 136.9／103.3 | 0.76／0.57 | 否 |
| (d) 入队等待 p99（构成 B） | 1.00／25.92 | 24.92 | 17.32／2.42 | 0.67／0.09 | 否 |
| (e) 入队等待 max（构成 A） | 144.4／165.5 | 21.19 | 182.2／189.9 | 1.10／1.15 | 否 |
| (e) 入队等待 max（构成 B） | 38.13／37.65 | 0.48 | 39.80／39.90 | 1.04／1.05 | 否 |
| 冷启动窗入队等待 max（构成 A） | 567.3／1366.4 | 799.1 | 664.5／872.5 | 0.49／0.64 | 否 |
| 冷启动窗入队等待 max（构成 B） | 231.8／231.1 | 0.71 | 233.8／234.9 | 1.01／1.01 | 否 |
| 选角作业 p99（构成 A） | 130.0／142.6 | 12.58 | 137.0／128.8 | 0.96／0.90 | 否 |
| 选角作业 p99（构成 B） | 227.5／229.9 | 2.40 | 232.9／230.3 | 1.01／1.00 | 否 |
| 选角作业 max（构成 A） | 229.6／239.1 | 9.47 | 269.3／234.0 | 1.13／0.98 | 否 |
| 选角作业 max（构成 B） | 227.5／229.9 | 2.40 | 232.9／230.3 | 1.01／1.00 | 否 |
| 换区作业 p50（构成 A） | 23.24／22.56 | 0.68 | 23.51／23.79 | 1.01／1.02 | 否 |
| 换区作业 p50（构成 B） | 14.17／14.46 | 0.29 | 13.38／10.71 | 0.93／0.74 | 否 |
| 位置包作业 p50（构成 A） | 0.01／0.01 | 0.00 | 0.01／0.01 | 0.96／0.96 | 否 |
| 位置包作业 p50（构成 B） | 0.03／0.02 | 0.01 | 0.03／0.03 | 0.91／0.85 | 否 |
| 连接检查作业 p50（构成 A） | 0.00／0.00 | 0.00 | 0.00／0.00 | 1.00／1.00 | 否 |
| 连接检查作业 p50（构成 B） | 0.00／0.00 | 0.00 | 0.00／0.00 | 1.04／1.00 | 否 |

读法：
- 所有量都与 M-2 同量级。默认级别构成 B 的稳态端到端 p99 从 20.5／26.1 ms 降到 19.2／19.8 ms，构成 A 从 97.5／103.9 ms 变为 99.0／99.8 ms；(e) 其余连接构成 B 9.96／10.41 ms（M-2 9.77／11.20 ms），构成 A 96.1／111.6 ms（M-2 106.6／112.8 ms）；(f) 构成 B 231.9／232.4 ms（M-2 236.3／247.4 ms）。
- 变大的量都在一次运行里出现，另一次落在 M-2 的范围内：选角作业 max（A-dbg-1 269.3 ms，A-dbg-2 234.0 ms）；构成 A 的 (f)（A-def-1 1150.5 ms，A-def-2 954.3 ms）；构成 B 的 (d) 入队等待 p99（B-dbg-1 17.32 ms，B-dbg-2 2.42 ms）。
- 构成 A 的 (e) 入队等待 max 两次都比 M-2 大（182.2／189.9 ms，M-2 144.4／165.5 ms），约 1.1 倍，远不到 2 倍。

### 5.3 控制运行（基线 dll 与 M-1 最终 dll）

| 量 | B-def-1（G1） | B-def-2（G1） | B-def-g0-2（G0） | B-def-g0-3（G0） | B-def-g0（G0） |
|---|---:|---:|---:|---:|---:|
| 稳态窗全部计入的请求 | 47390 / 0.47 / 19.22 / 46.47 / 61.41 | 47390 / 0.39 / 19.84 / 51.75 / 60.86 | 47394 / 0.45 / 19.32 / 47.94 / 64.94 | 47386 / 0.33 / 17.92 / 46.05 / 60.80 | 47396 / 0.45 / 17.40 / 45.97 / 59.63 |
| 参考：稳态窗 | 47390 / 0.22 / 18.81 / 45.91 / 61.16 | 47390 / 0.21 / 19.30 / 51.38 / 60.59 | 47394 / 0.22 / 18.59 / 47.60 / 63.85 | 47386 / 0.21 / 17.56 / 46.05 / 60.08 | 47396 / 0.23 / 16.92 / 45.28 / 59.07 |
| (f) 最长单次停顿（判定；参考） | 231.9；231.9 | 232.4；232.4 | 235.2；235.2 | 234.0；234.0 | 233.1；233.1 |
| (d) 背景请求 | 477* / 27.07 / 91.26 / 100.9 / 100.9 | 478* / 29.93 / 92.92 / 96.84 / 96.84 | 460* / 14.46 / 96.60 / 115.0 / 115.0 | 449* / 5.07 / 80.54 / 82.74 / 82.74 | 462* / 15.64 / 86.32 / 93.63 / 93.63 |
| (e) 其余连接 | 11868 / 0.63 / 9.96 / 32.36 / 47.18 | 11871 / 0.54 / 10.41 / 33.72 / 46.04 | 11858 / 0.70 / 9.84 / 31.26 / 46.98 | 11862 / 0.50 / 14.84 / 35.86 / 46.31 | 11858 / 0.51 / 8.47 / 30.06 / 44.19 |
| (e) 灌包连接自身应答速率 | 77178 | 77196 | 77749 | 76733 | 77335 |
| PrepareSelectedCharacterStateCommand max | 100.7 | 103.3 | 104.1 | 100.5 | 104.4 |
| EnterDungeonCommand p50 / max | 8.88 / 56.34 | 9.37 / 55.98 | 8.58 / 56.52 | 8.64 / 56.02 | 8.88 / 55.34 |
| 错误／失败命令 | 0／0 | 0／0 | 0／0 | 0／0 | 0／0 |

逐项检验（TASK-32 §5.2，主控方裁定第 2 条）：

| 量 | G1（B-def-1／B-def-2） | 差距 | 区间 | G0 运行 | G0 值 | 是否在区间内 |
|---|---:|---:|---:|---:|---:|---:|
| 稳态端到端 p99 | 19.22／19.84 | 0.61 | [18.61, 20.45] | B-def-g0-2 | 19.32 | 在 |
| 稳态端到端 p99 | 19.22／19.84 | 0.61 | [18.61, 20.45] | B-def-g0-3 | 17.92 | **不在** |
| 稳态端到端 p99 | 19.22／19.84 | 0.61 | [18.61, 20.45] | B-def-g0 | 17.40 | **不在** |
| (d) 背景请求 p99 | 91.26／92.92 | 1.66 | [89.60, 94.59] | B-def-g0-2 | 96.60 | **不在** |
| (d) 背景请求 p99 | 91.26／92.92 | 1.66 | [89.60, 94.59] | B-def-g0-3 | 80.54 | **不在** |
| (d) 背景请求 p99 | 91.26／92.92 | 1.66 | [89.60, 94.59] | B-def-g0 | 86.32 | **不在** |
| (f) 最长单次停顿 | 231.9／232.4 | 0.51 | [231.4, 232.9] | B-def-g0-2 | 235.2 | **不在** |
| (f) 最长单次停顿 | 231.9／232.4 | 0.51 | [231.4, 232.9] | B-def-g0-3 | 234.0 | **不在** |
| (f) 最长单次停顿 | 231.9／232.4 | 0.51 | [231.4, 232.9] | B-def-g0 | 233.1 | **不在** |
| (e) 其余连接 p99 | 9.96／10.41 | 0.45 | [9.51, 10.86] | B-def-g0-2 | 9.84 | 在 |
| (e) 其余连接 p99 | 9.96／10.41 | 0.45 | [9.51, 10.86] | B-def-g0-3 | 14.84 | **不在** |
| (e) 其余连接 p99 | 9.96／10.41 | 0.45 | [9.51, 10.86] | B-def-g0 | 8.47 | **不在** |

2/12 inside -> 未证明等价（不在区间内：B-def-g0-3 的稳态端到端 p99、B-def-g0 的稳态端到端 p99、B-def-g0-2 的(d) 背景请求 p99、B-def-g0-3 的(d) 背景请求 p99、B-def-g0 的(d) 背景请求 p99、B-def-g0-2 的(f) 最长单次停顿、B-def-g0-3 的(f) 最长单次停顿、B-def-g0 的(f) 最长单次停顿、B-def-g0-3 的(e) 其余连接 p99、B-def-g0 的(e) 其余连接 p99）
range only (no effect on the test): 稳态端到端 p99 G0 17.40..19.32 ms, G1 19.22..19.84 ms
range only (no effect on the test): (d) 背景请求 p99 G0 80.54..96.60 ms, G1 91.26..92.92 ms
range only (no effect on the test): (f) 最长单次停顿 G0 233.1..235.2 ms, G1 231.9..232.4 ms
range only (no effect on the test): (e) 其余连接 p99 G0 8.47..14.84 ms, G1 9.96..10.41 ms

- **结论：未证明等价**。12 项里只有 2 项（B-def-g0-2 的稳态与 (e)）落在区间内。
- 三次 G0 落在两次 G1 的两侧：稳态 G0 17.40～19.32 ms 对 G1 19.22～19.84 ms；(d) 80.54～96.60 ms 对 91.26～92.92 ms；(e) 8.47～14.84 ms 对 9.96～10.41 ms。只有 (f) 三次都偏高（233.1～235.2 ms 对 231.9～232.4 ms，高 0.7～3.3 ms）。两次 G1 运行之间的差距只有 0.45～1.66 ms，比三次 G0 彼此之间的差距（稳态 1.92 ms、(d) 16.06 ms、(e) 6.37 ms）小得多，区间因此很窄。这只是说明，不改判据。
- 所以与基线对照时，(e) 仍同时给 G1 与 G0 两种口径。

**(e) 的 G0 口径**（三次 G0 对 M-1 的默认级别组、M-2 的一次 G0 与基线）：

| G0 运行 | (e) 其余连接 p99 (ms) | ÷ M-1 G0（4.42～4.94） | ÷ M-2 G0（17.84） | ÷ 基线（2.71～2.71） |
|---|---:|---:|---:|---:|
| B-def-g0-2 | 9.84 | 1.99～2.23 | 0.55 | 3.64～3.64 |
| B-def-g0-3 | 14.84 | 3.00～3.36 | 0.83 | 5.48～5.48 |
| B-def-g0 | 8.47 | 1.71～1.92 | 0.47 | 3.13～3.13 |
| 三次中位数 | 9.84 | 1.99～2.23 | 0.55 | 3.64～3.64 |

G0 (e) others p99: 9.84, 14.84, 8.47 ms; range 8.47..14.84; median 9.84; G1 M-3 B-def-1/-2: 9.96 / 10.41 ms

- 三次都不在 M-1 的 4.4～4.9 ms 附近，也不是都在 15 ms 以上（§4.4 第 5 条的两种情形都不是）：G0 口径下构成 B 的 (e) 约为 M-1 的 2 倍（中位数 9.84 ÷ 4.94 ＝ 1.99，9.84 ÷ 4.42 ＝ 2.23），与 G1 口径的「约翻倍」一致。
- M-2 那一次 G0 的 17.84 ms 比 M-3 三次都高，多半含噪声（推断）。F-120 按此改写。

### 5.4 执行器作业（基线不存在）

各类作业的执行时长（M-3 六次 debug 运行），最后一列是 M-2 四次的 p50 / max：

| 作业 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-2 p50 / max（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| `inbound:GameSelectCharacter` | 485* / 68.95 / 137.0 / 269.3 / 269.3；首次 235.9；冷 269.3／后 151.7 | 484* / 68.17 / 128.8 / 234.0 / 234.0；首次 234.0；冷 234.0／后 135.7 | 21** / 72.61 / 232.9 / 232.9 / 232.9；首次 232.9；冷 232.9／后 – | 21** / 70.22 / 230.3 / 230.3 / 230.3；首次 230.3；冷 230.3／后 – | 484* / 68.62 / 140.8 / 237.1 / 237.1；首次 237.1；冷 237.1／后 144.8 | 21** / 72.93 / 234.8 / 234.8 / 234.8；首次 234.8；冷 234.8／后 – | 68.33 / 229.6；69.37 / 239.1；75.77 / 227.5；69.45 / 229.9 |
| `inbound:GameEplpCommand` | 79** / 45.66 / 91.70 / 91.70 / 91.70；首次 46.95；冷 76.62／后 91.70 | 78** / 44.59 / 157.3 / 157.3 / 157.3；首次 44.94；冷 49.40／后 157.3 | 78** / 41.27 / 61.12 / 61.12 / 61.12；首次 52.16；冷 54.99／后 61.12 | 78** / 37.79 / 60.42 / 60.42 / 60.42；首次 51.71；冷 57.80／后 60.42 | 78** / 43.55 / 92.68 / 92.68 / 92.68；首次 33.26；冷 47.90／后 92.68 | 78** / 41.10 / 88.77 / 88.77 / 88.77；首次 40.07；冷 53.51／后 88.77 | 45.37 / 77.21；40.77 / 96.86；34.56 / 59.52；37.62 / 58.64 |
| `inbound:GameSetUserArea` | 2659 / 23.51 / 54.18 / 87.27 / 111.4；首次 13.28；冷 53.94／后 111.4 | 2656 / 23.79 / 57.00 / 74.86 / 76.36；首次 10.81；冷 76.06／后 76.36 | 12** / 13.38 / 19.46 / 19.46 / 19.46；首次 10.92；冷 19.46／后 – | 12** / 10.71 / 39.39 / 39.39 / 39.39；首次 10.71；冷 39.39／后 – | 2655 / 23.34 / 56.30 / 74.62 / 94.01；首次 9.90；冷 56.69／后 94.01 | 12** / 14.41 / 36.37 / 36.37 / 36.37；首次 10.78；冷 36.37／后 – | 23.24 / 75.64；22.56 / 117.2；14.17 / 36.86；14.46 / 24.14 |
| `inbound:GameSelectDungeon` | 80** / 10.28 / 85.66 / 85.66 / 85.66；首次 85.66；冷 85.66／后 19.07 | 79** / 9.77 / 74.13 / 74.13 / 74.13；首次 74.13；冷 74.13／后 13.59 | 79** / 14.09 / 75.26 / 75.26 / 75.26；首次 75.26；冷 75.26／后 21.88 | 80** / 14.46 / 75.56 / 75.56 / 75.56；首次 75.56；冷 75.56／后 22.80 | 79** / 10.07 / 75.29 / 75.29 / 75.29；首次 75.29；冷 75.29／后 21.71 | 80** / 14.27 / 76.56 / 76.56 / 76.56；首次 76.56；冷 76.56／后 19.26 | 9.93 / 77.26；10.23 / 73.22；14.59 / 72.64；14.90 / 73.48 |
| `inbound:GameEnterSelectDungeon` | 80** / 22.53 / 42.72 / 42.72 / 42.72；首次 34.24；冷 34.24／后 42.72 | 79** / 22.56 / 49.05 / 49.05 / 49.05；首次 27.59；冷 27.59／后 49.05 | 79** / 25.69 / 64.18 / 64.18 / 64.18；首次 64.18；冷 64.18／后 40.18 | 80** / 21.54 / 63.93 / 63.93 / 63.93；首次 63.93；冷 63.93／后 46.26 | 79** / 23.11 / 45.17 / 45.17 / 45.17；首次 31.26；冷 45.17／后 40.02 | 80** / 24.00 / 60.67 / 60.67 / 60.67；首次 60.67；冷 60.67／后 45.79 | 21.20 / 45.90；20.28 / 56.15；24.01 / 62.25；25.85 / 53.37 |
| `inbound:GameCreateCharacter` | 95** / 1.78 / 39.24 / 39.24 / 39.24；首次 39.24；冷 39.24／后 4.32 | 94** / 1.90 / 35.35 / 35.35 / 35.35；首次 35.35；冷 35.35／后 6.76 | 21** / 1.75 / 31.09 / 31.09 / 31.09；首次 31.09；冷 31.09／后 – | 21** / 1.63 / 31.36 / 31.36 / 31.36；首次 31.36；冷 31.36／后 – | 94** / 1.66 / 32.82 / 32.82 / 32.82；首次 32.82；冷 32.82／后 6.30 | 21** / 1.54 / 31.68 / 31.68 / 31.68；首次 31.68；冷 31.68／后 – | 1.66 / 41.56；1.79 / 31.09；1.69 / 31.45；1.61 / 32.06 |
| `inbound:GameConnectionCheck` | 1415894 / 0.00 / 0.01 / 0.04 / 26.31；首次 0.10；冷 26.31／后 6.04 | 1389511 / 0.00 / 0.01 / 0.04 / 7.43；首次 0.13；冷 2.52／后 7.43 | 1942947 / 0.00 / 0.02 / 0.04 / 7.29；首次 0.13；冷 2.59／后 7.29 | 1940288 / 0.00 / 0.01 / 0.04 / 7.28；首次 0.12；冷 3.75／后 7.28 | 1390771 / 0.00 / 0.01 / 0.05 / 9.27；首次 0.15；冷 1.59／后 9.27 | 1925401 / 0.00 / 0.02 / 0.03 / 18.44；首次 0.14；冷 7.49／后 18.44 | 0.00 / 10.67；0.00 / 6.88；0.00 / 8.76；0.00 / 7.45 |
| `inbound:GameSetUserPosition` | 41751 / 0.01 / 0.12 / 2.17 / 22.88；首次 1.84；冷 22.88／后 8.01 | 41774 / 0.01 / 0.12 / 2.52 / 21.38；首次 1.78；冷 8.74／后 21.38 | 45992 / 0.03 / 0.15 / 2.78 / 6.61；首次 1.70；冷 4.27／后 6.61 | 45923 / 0.03 / 0.13 / 2.64 / 7.83；首次 1.78；冷 3.43／后 7.83 | 41888 / 0.01 / 0.13 / 2.54 / 11.18；首次 1.72；冷 11.18／后 8.21 | 46052 / 0.03 / 0.12 / 2.65 / 6.42；首次 1.76；冷 5.44／后 6.42 | 0.01 / 18.88；0.01 / 16.67；0.03 / 22.74；0.02 / 5.96 |
| `inbound:GameRequestPeer` | 80** / 0.50 / 4.73 / 4.73 / 4.73；首次 4.19；冷 4.73／后 3.19 | 82** / 0.49 / 7.37 / 7.37 / 7.37；首次 7.37；冷 7.37／后 2.64 | 82** / 2.61 / 17.33 / 17.33 / 17.33；首次 17.33；冷 17.33／后 5.82 | 82** / 1.99 / 7.76 / 7.76 / 7.76；首次 7.76；冷 7.76／后 5.47 | 83** / 0.53 / 5.69 / 5.69 / 5.69；首次 5.69；冷 5.69／后 2.42 | 83** / 2.37 / 20.17 / 20.17 / 20.17；首次 20.17；冷 20.17／后 5.94 | 0.47 / 4.25；0.51 / 4.74；2.06 / 16.53；2.30 / 16.52 |
| `inbound:GameResponsePeer` | 80** / 0.19 / 8.41 / 8.41 / 8.41；首次 8.41；冷 8.41／后 0.80 | 82** / 0.18 / 12.03 / 12.03 / 12.03；首次 12.03；冷 12.03／后 0.52 | 82** / 0.40 / 17.13 / 17.13 / 17.13；首次 17.13；冷 17.13／后 3.21 | 82** / 0.37 / 9.63 / 9.63 / 9.63；首次 9.63；冷 9.63／后 0.86 | 83** / 0.18 / 10.38 / 10.38 / 10.38；首次 10.38；冷 10.38／后 1.30 | 83** / 0.37 / 16.90 / 16.90 / 16.90；首次 16.90；冷 16.90／后 0.63 | 0.18 / 8.43；0.16 / 9.28；0.38 / 17.11；0.35 / 17.24 |
| `lifecycle:disconnected` | 81** / 0.01 / 1.78 / 1.78 / 1.78；首次 1.78；冷 1.78／后 1.52 | 81** / 0.01 / 4.71 / 4.71 / 4.71；首次 4.71；冷 4.71／后 2.71 | 81** / 0.01 / 17.10 / 17.10 / 17.10；首次 1.73；冷 1.73／后 17.10 | 81** / 0.01 / 15.07 / 15.07 / 15.07；首次 1.73；冷 1.73／后 15.07 | 81** / 0.01 / 3.75 / 3.75 / 3.75；首次 3.75；冷 3.75／后 1.54 | 81** / 0.01 / 15.56 / 15.56 / 15.56；首次 1.82；冷 1.82／后 15.56 | 0.01 / 1.84；0.01 / 2.25；0.01 / 14.62；0.01 / 16.50 |
| `inbound:GameLeaveParty` | 76** / 0.20 / 8.54 / 8.54 / 8.54；首次 8.54；冷 8.54／后 3.21 | 78** / 0.20 / 8.59 / 8.59 / 8.59；首次 8.59；冷 8.59／后 1.79 | 77** / 0.59 / 14.67 / 14.67 / 14.67；首次 14.67；冷 14.67／后 4.16 | 77** / 0.54 / 13.82 / 13.82 / 13.82；首次 13.82；冷 13.82／后 7.19 | 78** / 0.22 / 9.51 / 9.51 / 9.51；首次 9.51；冷 9.51／后 4.80 | 78** / 0.53 / 12.70 / 12.70 / 12.70；首次 12.70；冷 12.70／后 1.08 | 0.19 / 8.85；0.18 / 10.06；0.55 / 11.43；0.55 / 10.42 |
| `http:POST /api/launcher/v1/register` | 81** / 0.10 / 13.29 / 13.29 / 13.29；首次 13.29；冷 13.29／后 0.43 | 81** / 0.11 / 10.60 / 10.60 / 10.60；首次 10.60；冷 10.60／后 0.35 | 81** / 0.16 / 7.00 / 7.00 / 7.00；首次 7.00；冷 7.00／后 0.76 | 81** / 0.16 / 7.02 / 7.02 / 7.02；首次 7.02；冷 7.02／后 4.58 | 81** / 0.10 / 10.99 / 10.99 / 10.99；首次 10.99；冷 10.99／后 2.08 | 81** / 0.15 / 7.12 / 7.12 / 7.12；首次 7.12；冷 7.12／后 1.87 | 0.11 / 14.28；0.09 / 11.99；0.15 / 7.23；0.12 / 6.98 |
| `inbound:GameLogin` | 81** / 0.11 / 9.67 / 9.67 / 9.67；首次 9.67；冷 9.67／后 0.28 | 81** / 0.11 / 10.04 / 10.04 / 10.04；首次 10.04；冷 10.04／后 0.22 | 81** / 0.13 / 9.22 / 9.22 / 9.22；首次 9.22；冷 9.22／后 0.40 | 81** / 0.14 / 9.17 / 9.17 / 9.17；首次 9.17；冷 9.17／后 0.42 | 81** / 0.12 / 12.84 / 12.84 / 12.84；首次 12.84；冷 12.84／后 1.56 | 81** / 0.13 / 9.34 / 9.34 / 9.34；首次 9.34；冷 9.34／后 0.31 | 0.10 / 9.45；0.10 / 9.67；0.14 / 9.32；0.12 / 9.00 |
| `event:achievement.inventory` | 950* / 0.26 / 2.00 / 8.20 / 8.20；首次 7.85；冷 7.85／后 8.20 | 940* / 0.26 / 2.60 / 8.87 / 8.87；首次 7.19；冷 7.19／后 8.87 | 210* / 0.34 / 3.16 / 6.92 / 6.92；首次 6.92；冷 6.92／后 – | 210* / 0.32 / 7.46 / 7.93 / 7.93；首次 7.46；冷 7.93／后 – | 940* / 0.25 / 1.86 / 12.22 / 12.22；首次 12.22；冷 12.22／后 5.91 | 210* / 0.33 / 3.19 / 11.42 / 11.42；首次 11.42；冷 11.42／后 – | 0.26 / 7.61；0.25 / 7.35；0.33 / 7.15；0.32 / 8.12 |
| `timer:PremiumDailyRefreshService` | 4** / 0.52 / 4.76 / 4.76 / 4.76；首次 4.76；冷 4.76／后 0.74 | 4** / 0.60 / 5.49 / 5.49 / 5.49；首次 5.49；冷 5.49／后 0.81 | 4** / 2.16 / 6.19 / 6.19 / 6.19；首次 6.19；冷 6.19／后 2.57 | 4** / 2.32 / 7.42 / 7.42 / 7.42；首次 7.42；冷 7.42／后 2.39 | 4** / 0.88 / 11.95 / 11.95 / 11.95；首次 11.95；冷 –／后 11.95 | 4** / 2.18 / 6.05 / 6.05 / 6.05；首次 6.05；冷 6.05／后 2.48 | 0.86 / 4.68；0.55 / 4.26；1.19 / 11.65；1.54 / 9.24 |
| `inbound:GameReturnToSelection` | 464* / 0.47 / 2.76 / 9.97 / 9.97；首次 9.97；冷 9.97／后 6.25 | 463* / 0.46 / 2.99 / 9.95 / 9.95；首次 9.95；冷 9.95／后 3.23 | – | – | 463* / 0.46 / 3.35 / 10.46 / 10.46；首次 10.46；冷 10.46／后 4.67 | – | 0.46 / 11.09；0.44 / 12.02；–；– |
| `http:POST /api/launcher/v1/login` | 81** / 0.06 / 9.46 / 9.46 / 9.46；首次 9.46；冷 9.46／后 0.23 | 81** / 0.04 / 3.22 / 3.22 / 3.22；首次 3.22；冷 3.22／后 0.19 | 81** / 0.08 / 2.66 / 2.66 / 2.66；首次 2.66；冷 2.66／后 2.42 | 81** / 0.08 / 2.60 / 2.60 / 2.60；首次 2.60；冷 2.60／后 0.28 | 81** / 0.06 / 7.15 / 7.15 / 7.15；首次 7.15；冷 7.15／后 0.27 | 81** / 0.05 / 2.83 / 2.83 / 2.83；首次 2.83；冷 2.83／后 0.26 | 0.04 / 2.86；0.05 / 5.69；0.07 / 2.77；0.07 / 2.62 |
| `timer:ScheduledTaskHostedService/character.name-tag.expiration` | 5** / 1.10 / 4.76 / 4.76 / 4.76；首次 4.76；冷 4.76／后 1.91 | 5** / 0.40 / 2.47 / 2.47 / 2.47；首次 2.47；冷 2.47／后 0.67 | 5** / 0.71 / 8.47 / 8.47 / 8.47；首次 8.47；冷 8.47／后 1.10 | 5** / 1.21 / 2.97 / 2.97 / 2.97；首次 2.97；冷 2.97／后 1.24 | 5** / 0.54 / 2.22 / 2.22 / 2.22；首次 2.22；冷 2.22／后 0.75 | 5** / 1.65 / 6.80 / 6.80 / 6.80；首次 6.80；冷 6.80／后 3.08 | 0.33 / 2.49；0.46 / 1.99；1.62 / 2.88；2.01 / 6.65 |
| `event:southern-dale.daily-refill-notifications` | 485* / 0.43 / 5.92 / 6.01 / 6.01；首次 5.49；冷 5.49／后 6.01 | 484* / 0.41 / 7.30 / 8.39 / 8.39；首次 8.09；冷 8.39／后 7.96 | 21** / 0.80 / 6.49 / 6.49 / 6.49；首次 5.15；冷 6.49／后 – | 21** / 0.75 / 5.20 / 5.20 / 5.20；首次 5.20；冷 5.20／后 – | 484* / 0.42 / 5.89 / 8.01 / 8.01；首次 5.60；冷 5.60／后 8.01 | 21** / 0.77 / 7.00 / 7.00 / 7.00；首次 5.32；冷 7.00／后 – | 0.41 / 8.56；0.42 / 18.49；0.75 / 5.44；0.75 / 5.32 |
| `timer:CreatureClockService` | 276* / 0.14 / 0.77 / 6.76 / 6.76；首次 6.76；冷 6.76／后 1.61 | 276* / 0.16 / 3.08 / 6.81 / 6.81；首次 6.81；冷 6.81／后 6.27 | 277* / 0.03 / 1.77 / 7.51 / 7.51；首次 7.51；冷 7.51／后 3.82 | 277* / 0.02 / 1.86 / 5.98 / 5.98；首次 5.98；冷 5.98／后 4.09 | 276* / 0.17 / 1.20 / 6.58 / 6.58；首次 6.58；冷 6.58／后 5.33 | 277* / 0.03 / 4.45 / 7.73 / 7.73；首次 7.73；冷 7.73／后 4.57 | 0.14 / 6.05；0.15 / 6.46；0.02 / 8.43；0.03 / 5.94 |
| `timer:JoustMaintenanceService` | 276* / 0.07 / 0.52 / 0.66 / 0.66；首次 0.12；冷 0.66／后 0.52 | 276* / 0.08 / 0.22 / 1.20 / 1.20；首次 0.14；冷 0.21／后 1.20 | 277* / 0.24 / 0.46 / 7.25 / 7.25；首次 0.15；冷 0.46／后 7.25 | 277* / 0.29 / 0.82 / 0.97 / 0.97；首次 0.15；冷 0.57／后 0.97 | 276* / 0.08 / 0.42 / 1.72 / 1.72；首次 0.12；冷 0.30／后 1.72 | 277* / 0.28 / 0.63 / 0.76 / 0.76；首次 0.15；冷 0.54／后 0.76 | 0.07 / 1.27；0.07 / 0.93；0.23 / 4.30；0.27 / 0.70 |
| `lifecycle:connected` | 82** / 0.01 / 5.82 / 5.82 / 5.82；首次 5.82；冷 5.82／后 0.03 | 82** / 0.01 / 6.20 / 6.20 / 6.20；首次 6.20；冷 6.20／后 0.05 | 82** / 0.01 / 4.08 / 4.08 / 4.08；首次 4.08；冷 4.08／后 0.08 | 82** / 0.02 / 4.02 / 4.02 / 4.02；首次 4.02；冷 4.02／后 0.04 | 82** / 0.01 / 4.22 / 4.22 / 4.22；首次 4.22；冷 4.22／后 0.20 | 82** / 0.01 / 4.03 / 4.03 / 4.03；首次 4.03；冷 4.03／后 0.03 | 0.01 / 4.29；0.01 / 3.95；0.01 / 4.17；0.01 / 3.99 |
| `event:tournament.departure` | 160* / 0.10 / 1.68 / 6.17 / 6.17；首次 0.18；冷 1.38／后 6.17 | 158* / 0.10 / 0.27 / 0.29 / 0.29；首次 0.19；冷 0.19／后 0.29 | 158* / 0.12 / 0.23 / 1.17 / 1.17；首次 0.21；冷 0.21／后 1.17 | 160* / 0.12 / 0.27 / 0.59 / 0.59；首次 0.20；冷 0.22／后 0.59 | 158* / 0.09 / 0.35 / 0.60 / 0.60；首次 0.21；冷 0.21／后 0.60 | 160* / 0.13 / 0.57 / 0.61 / 0.61；首次 0.19；冷 0.26／后 0.61 | 0.10 / 0.33；0.09 / 0.29；0.12 / 0.97；0.13 / 1.06 |
| `timer:WeaknessRecoveryService` | 4** / 0.32 / 1.78 / 1.78 / 1.78；首次 1.78；冷 1.78／后 1.57 | 4** / 0.31 / 1.87 / 1.87 / 1.87；首次 1.69；冷 1.69／后 1.87 | 4** / 0.77 / 4.31 / 4.31 / 4.31；首次 2.04；冷 2.04／后 4.31 | 4** / 0.79 / 4.07 / 4.07 / 4.07；首次 2.07；冷 2.07／后 4.07 | 4** / 0.71 / 1.87 / 1.87 / 1.87；首次 1.84；冷 –／后 1.87 | 4** / 1.19 / 6.14 / 6.14 / 6.14；首次 2.05；冷 2.05／后 6.14 | 0.38 / 1.53；0.31 / 1.72；0.86 / 3.65；0.46 / 4.15 |
| `event:quest.inventory` | 950* / 0.10 / 0.77 / 6.13 / 6.13；首次 6.13；冷 6.13／后 2.62 | 940* / 0.10 / 1.00 / 5.77 / 5.77；首次 5.77；冷 5.77／后 2.85 | 210* / 0.12 / 1.77 / 4.91 / 4.91；首次 4.91；冷 4.91／后 – | 210* / 0.11 / 0.58 / 5.27 / 5.27；首次 5.27；冷 5.27／后 – | 940* / 0.10 / 0.72 / 1.35 / 1.35；首次 0.62；冷 1.20／后 1.35 | 210* / 0.11 / 0.80 / 2.79 / 2.79；首次 0.58；冷 2.79／后 – | 0.10 / 4.76；0.10 / 5.01；0.11 / 4.61；0.10 / 4.67 |
| `inbound:GameFinishLoading` | 160* / 0.11 / 1.69 / 3.55 / 3.55；首次 3.55；冷 3.55／后 1.16 | 158* / 0.10 / 1.81 / 3.23 / 3.23；首次 3.23；冷 3.23／后 0.23 | 158* / 0.11 / 1.60 / 3.40 / 3.40；首次 3.40；冷 3.40／后 1.40 | 160* / 0.11 / 2.00 / 3.25 / 3.25；首次 3.25；冷 3.25／后 1.13 | 158* / 0.09 / 1.70 / 3.23 / 3.23；首次 3.23；冷 3.23／后 0.21 | 160* / 0.11 / 1.68 / 3.09 / 3.09；首次 3.09；冷 3.09／后 0.25 | 0.10 / 3.27；0.08 / 5.65；0.10 / 3.40；0.10 / 3.01 |
| `timer:FatigueDailyRefreshService` | 4** / 0.15 / 1.12 / 1.12 / 1.12；首次 1.12；冷 1.12／后 0.39 | 4** / 0.18 / 1.24 / 1.24 / 1.24；首次 1.24；冷 1.24／后 0.56 | 4** / 1.48 / 2.14 / 2.14 / 2.14；首次 2.14；冷 2.14／后 2.02 | 4** / 1.42 / 2.54 / 2.54 / 2.54；首次 1.42；冷 1.42／后 2.54 | 4** / 0.50 / 3.38 / 3.38 / 3.38；首次 0.95；冷 0.95／后 3.38 | 4** / 1.57 / 2.96 / 2.96 / 2.96；首次 1.57；冷 1.57／后 2.96 | 0.61 / 1.08；0.15 / 0.95；0.42 / 2.90；0.50 / 2.54 |
| `timer:MarriageWeddingScheduler` | 276* / 0.02 / 0.17 / 1.86 / 1.86；首次 1.86；冷 1.86／后 0.18 | 276* / 0.02 / 0.14 / 1.83 / 1.83；首次 1.83；冷 1.83／后 0.17 | 277* / 0.11 / 0.44 / 3.05 / 3.05；首次 1.97；冷 3.05／后 0.44 | 277* / 0.11 / 0.24 / 2.13 / 2.13；首次 2.13；冷 2.13／后 0.33 | 276* / 0.02 / 0.14 / 1.80 / 1.80；首次 1.80；冷 1.80／后 0.22 | 277* / 0.13 / 0.25 / 1.94 / 1.94；首次 1.94；冷 1.94／后 0.45 | 0.03 / 1.75；0.02 / 1.78；0.10 / 3.55；0.10 / 1.86 |
| `timer:DungeonPartyWipeService` | 1105 / 0.01 / 0.05 / 0.43 / 1.30；首次 1.30；冷 1.30／后 0.06 | 1104 / 0.01 / 0.05 / 0.66 / 2.53；首次 2.53；冷 2.53／后 0.07 | 1108 / 0.01 / 0.08 / 0.52 / 1.35；首次 1.35；冷 1.35／后 0.17 | 1108 / 0.01 / 0.06 / 0.53 / 1.25；首次 1.25；冷 1.25／后 0.08 | 1104 / 0.01 / 0.05 / 0.39 / 1.32；首次 1.32；冷 1.32／后 0.18 | 1108 / 0.01 / 0.06 / 0.41 / 1.36；首次 1.36；冷 1.36／后 0.10 | 0.01 / 2.60；0.01 / 2.55；0.01 / 1.38；0.01 / 4.14 |
| `timer:AltarQuotaDailyRefreshService` | 4** / 0.07 / 0.56 / 0.56 / 0.56；首次 0.56；冷 0.56／后 0.08 | 4** / 0.25 / 0.69 / 0.69 / 0.69；首次 0.69；冷 0.69／后 0.32 | 4** / 0.02 / 0.82 / 0.82 / 0.82；首次 0.82；冷 0.82／后 0.03 | 4** / 0.02 / 2.36 / 2.36 / 2.36；首次 2.36；冷 2.36／后 0.02 | 4** / 0.09 / 1.49 / 1.49 / 1.49；首次 1.49；冷 1.49／后 0.23 | 4** / 0.02 / 0.73 / 0.73 / 0.73；首次 0.73；冷 0.73／后 0.03 | 0.35 / 0.53；0.07 / 0.53；0.01 / 1.45；0.02 / 1.29 |
| `event:altar.departure` | 160* / 0.13 / 1.42 / 1.91 / 1.91；首次 1.91；冷 1.91／后 1.42 | 158* / 0.11 / 1.31 / 1.83 / 1.83；首次 1.83；冷 1.83／后 1.31 | 158* / 0.14 / 0.64 / 2.22 / 2.22；首次 2.22；冷 2.22／后 0.64 | 160* / 0.15 / 0.55 / 2.28 / 2.28；首次 2.28；冷 2.28／后 0.55 | 158* / 0.12 / 1.42 / 1.76 / 1.76；首次 1.76；冷 1.76／后 1.42 | 160* / 0.14 / 0.65 / 2.11 / 2.11；首次 2.11；冷 2.11／后 0.65 | 0.12 / 1.87；0.11 / 1.19；0.15 / 0.92；0.14 / 0.59 |
| `timer:GoldSilverHornQuotaDailyRefreshService` | 4** / 0.11 / 0.54 / 0.54 / 0.54；首次 0.54；冷 0.54／后 0.49 | 4** / 0.05 / 0.59 / 0.59 / 0.59；首次 0.59；冷 0.59／后 0.05 | 4** / 0.02 / 0.83 / 0.83 / 0.83；首次 0.83；冷 0.83／后 0.13 | 4** / 0.02 / 0.63 / 0.63 / 0.63；首次 0.63；冷 0.63／后 0.03 | 4** / 0.08 / 0.55 / 0.55 / 0.55；首次 0.55；冷 –／后 0.55 | 4** / 0.01 / 2.26 / 2.26 / 2.26；首次 2.26；冷 2.26／后 0.02 | 0.06 / 0.49；0.10 / 1.16；0.01 / 1.35；0.02 / 1.38 |
| `timer:ItemUsageDailyRefreshService` | 4** / 0.16 / 0.73 / 0.73 / 0.73；首次 0.73；冷 0.73／后 0.17 | 4** / 0.18 / 0.65 / 0.65 / 0.65；首次 0.65；冷 0.65／后 0.20 | 4** / 0.33 / 1.91 / 1.91 / 1.91；首次 1.01；冷 1.01／后 1.91 | 4** / 0.02 / 0.85 / 0.85 / 0.85；首次 0.85；冷 0.85／后 0.02 | 4** / 0.19 / 0.73 / 0.73 / 0.73；首次 0.73；冷 –／后 0.73 | 4** / 0.02 / 1.29 / 1.29 / 1.29；首次 1.29；冷 1.29／后 0.02 | 0.21 / 0.67；0.16 / 2.03；0.02 / 2.22；0.01 / 3.63 |
| `timer:ReviveCoinQuotaDailyRefreshService` | 4** / 0.08 / 0.62 / 0.62 / 0.62；首次 0.62；冷 0.62／后 0.09 | 4** / 0.10 / 0.64 / 0.64 / 0.64；首次 0.64；冷 0.64／后 0.10 | 4** / 0.05 / 1.83 / 1.83 / 1.83；首次 1.83；冷 1.83／后 0.05 | 4** / 0.01 / 0.95 / 0.95 / 0.95；首次 0.95；冷 0.95／后 0.81 | 4** / 0.10 / 0.59 / 0.59 / 0.59；首次 0.59；冷 0.59／后 0.31 | 4** / 0.04 / 1.89 / 1.89 / 1.89；首次 1.89；冷 1.89／后 0.06 | 0.10 / 0.65；0.09 / 0.53；0.01 / 1.37；0.28 / 1.93 |
| `timer:MirrorQuotaDailyRefreshService` | 4** / 0.08 / 1.04 / 1.04 / 1.04；首次 1.04；冷 1.04／后 0.27 | 4** / 0.07 / 1.13 / 1.13 / 1.13；首次 1.13；冷 1.13／后 0.57 | 4** / 0.18 / 1.51 / 1.51 / 1.51；首次 1.51；冷 1.51／后 0.40 | 4** / 0.06 / 1.80 / 1.80 / 1.80；首次 1.80；冷 1.80／后 0.33 | 4** / 0.08 / 1.01 / 1.01 / 1.01；首次 1.01；冷 1.01／后 0.71 | 4** / 0.06 / 1.43 / 1.43 / 1.43；首次 1.43；冷 1.43／后 0.40 | 0.31 / 1.03；0.09 / 5.60；0.03 / 2.31；0.14 / 2.33 |
| `event:quest.mirror-leave` | 160* / 0.11 / 0.89 / 1.80 / 1.80；首次 0.76；冷 0.89／后 1.80 | 158* / 0.12 / 1.25 / 1.41 / 1.41；首次 0.73；冷 0.73／后 1.41 | 158* / 0.14 / 1.71 / 1.80 / 1.80；首次 0.98；冷 0.98／后 1.80 | 160* / 0.15 / 1.01 / 1.25 / 1.25；首次 1.01；冷 1.01／后 1.25 | 158* / 0.12 / 0.80 / 0.96 / 0.96；首次 0.77；冷 0.77／后 0.96 | 160* / 0.14 / 1.44 / 1.50 / 1.50；首次 0.87；冷 0.87／后 1.50 | 0.12 / 1.49；0.11 / 2.33；0.15 / 3.09；0.15 / 2.71 |
| `timer:HerasQuotaDailyRefreshService` | 4** / 0.06 / 0.63 / 0.63 / 0.63；首次 0.63；冷 0.63／后 0.07 | 4** / 0.08 / 1.66 / 1.66 / 1.66；首次 1.66；冷 1.66／后 0.08 | 4** / 0.02 / 0.86 / 0.86 / 0.86；首次 0.86；冷 0.86／后 0.04 | 4** / 0.04 / 0.69 / 0.69 / 0.69；首次 0.69；冷 0.69／后 0.06 | 4** / 0.08 / 0.48 / 0.48 / 0.48；首次 0.48；冷 0.48／后 0.25 | 4** / 0.03 / 0.66 / 0.66 / 0.66；首次 0.66；冷 0.66／后 0.03 | 0.08 / 0.49；0.07 / 5.13；0.01 / 3.17；0.02 / 4.65 |
| `timer:TournamentQuotaDailyRefreshService` | 4** / 0.05 / 1.51 / 1.51 / 1.51；首次 1.51；冷 1.51／后 0.06 | 4** / 0.05 / 0.58 / 0.58 / 0.58；首次 0.58；冷 0.58／后 0.06 | 4** / 0.02 / 0.76 / 0.76 / 0.76；首次 0.76；冷 0.76／后 0.12 | 4** / 0.03 / 0.83 / 0.83 / 0.83；首次 0.83；冷 0.83／后 0.03 | 4** / 0.08 / 0.80 / 0.80 / 0.80；首次 0.47；冷 0.47／后 0.80 | 4** / 0.05 / 0.93 / 0.93 / 0.93；首次 0.67；冷 0.67／后 0.93 | 0.06 / 1.56；0.06 / 0.47；0.01 / 0.77；0.02 / 1.21 |
| `timer:BossTowerReturnSweepService` | 553* / 0.00 / 0.02 / 1.04 / 1.04；首次 1.04；冷 1.04／后 0.02 | 553* / 0.00 / 0.03 / 1.19 / 1.19；首次 1.19；冷 1.19／后 0.03 | 554* / 0.01 / 0.05 / 0.78 / 0.78；首次 0.78；冷 0.78／后 0.26 | 554* / 0.01 / 0.03 / 0.78 / 0.78；首次 0.78；冷 0.78／后 0.03 | 553* / 0.00 / 0.03 / 0.80 / 0.80；首次 0.80；冷 0.80／后 0.03 | 554* / 0.01 / 0.03 / 0.77 / 0.77；首次 0.77；冷 0.77／后 0.03 | 0.00 / 1.57；0.01 / 1.01；0.01 / 1.09；0.01 / 0.76 |
| `timer:IllusionTowerReturnSweepService` | 553* / 0.00 / 0.02 / 0.78 / 0.78；首次 0.78；冷 0.78／后 0.03 | 553* / 0.00 / 0.02 / 0.91 / 0.91；首次 0.91；冷 0.91／后 0.03 | 554* / 0.01 / 0.07 / 1.10 / 1.10；首次 1.10；冷 1.10／后 0.10 | 554* / 0.01 / 0.05 / 1.15 / 1.15；首次 1.15；冷 1.15／后 0.08 | 553* / 0.00 / 0.03 / 1.16 / 1.16；首次 1.16；冷 1.16／后 0.75 | 554* / 0.01 / 0.03 / 0.93 / 0.93；首次 0.93；冷 0.93／后 0.05 | 0.00 / 0.80；0.00 / 0.79；0.01 / 0.87；0.01 / 0.92 |
| `timer:RoomMechanismMaintenanceService` | 160* / 0.00 / 0.03 / 0.04 / 0.04；首次 0.04；冷 0.04／后 0.03 | 158* / 0.00 / 0.02 / 0.05 / 0.05；首次 0.05；冷 0.05／后 0.02 | 158* / 0.00 / 0.04 / 0.06 / 0.06；首次 0.06；冷 0.06／后 0.04 | 160* / 0.00 / 0.03 / 0.05 / 0.05；首次 0.05；冷 0.05／后 0.02 | 158* / 0.00 / 0.04 / 1.11 / 1.11；首次 0.04；冷 0.04／后 1.11 | 160* / 0.00 / 0.03 / 0.04 / 0.04；首次 0.04；冷 0.04／后 0.03 | 0.00 / 0.06；0.00 / 0.05；0.00 / 0.05；0.00 / 0.05 |
| `timer:DimensionQuotaDailyRefreshService` | 4** / 0.04 / 0.47 / 0.47 / 0.47；首次 0.47；冷 0.47／后 0.04 | 4** / 0.05 / 0.53 / 0.53 / 0.53；首次 0.53；冷 0.53／后 0.07 | 4** / 0.02 / 0.66 / 0.66 / 0.66；首次 0.66；冷 0.66／后 0.05 | 4** / 0.02 / 0.61 / 0.61 / 0.61；首次 0.61；冷 0.61／后 0.06 | 4** / 0.05 / 0.46 / 0.46 / 0.46；首次 0.46；冷 0.46／后 0.16 | 4** / 0.02 / 0.63 / 0.63 / 0.63；首次 0.63；冷 0.63／后 0.05 | 0.05 / 0.43；0.05 / 0.42；0.02 / 0.68；0.01 / 1.38 |
| `timer:TournamentRewardSweepService` | 5416 / 0.00 / 0.01 / 0.03 / 0.05；首次 0.02；冷 0.05／后 0.05 | 5442 / 0.00 / 0.01 / 0.02 / 0.52；首次 0.52；冷 0.52／后 0.03 | 5535 / 0.01 / 0.03 / 0.11 / 0.46；首次 0.46；冷 0.46／后 0.26 | 5532 / 0.01 / 0.02 / 0.04 / 0.48；首次 0.48；冷 0.48／后 0.13 | 5426 / 0.00 / 0.01 / 0.03 / 0.51；首次 0.51；冷 0.51／后 0.04 | 5538 / 0.01 / 0.02 / 0.04 / 0.51；首次 0.51；冷 0.51／后 0.05 | 0.00 / 0.57；0.00 / 0.51；0.01 / 0.58；0.00 / 0.59 |
| `timer:LoginSessionMaintenanceService` | 9** / 0.01 / 0.16 / 0.16 / 0.16；首次 0.16；冷 0.16／后 0.02 | 9** / 0.01 / 0.16 / 0.16 / 0.16；首次 0.16；冷 0.16／后 0.02 | 9** / 0.02 / 0.42 / 0.42 / 0.42；首次 0.42；冷 0.42／后 0.04 | 9** / 0.03 / 0.14 / 0.14 / 0.14；首次 0.14；冷 0.14／后 0.08 | 9** / 0.01 / 0.15 / 0.15 / 0.15；首次 0.15；冷 0.15／后 0.03 | 9** / 0.03 / 0.47 / 0.47 / 0.47；首次 0.47；冷 0.47／后 0.04 | 0.01 / 0.12；0.01 / 0.15；0.02 / 0.25；0.02 / 0.36 |

单条作业执行时长，按全部作业类型合计（含 ramp-A-2s）：

| 运行 | 作业数 | p99 | p99.9 | max | max 的作业 | inbound 作业数 | inbound p99 | inbound p99.9 | inbound max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A-dbg-1 | 1473973 | 0.03 | 26.76 | 269.3 | inbound:GameSelectCharacter | 1462064 | 0.02 | 26.91 | 269.3 |
| A-dbg-2 | 1447593 | 0.03 | 27.53 | 234.0 | inbound:GameSelectCharacter | 1435699 | 0.02 | 27.63 | 234.0 |
| B-dbg-1 | 1999797 | 0.04 | 0.29 | 232.9 | inbound:GameSelectCharacter | 1989709 | 0.04 | 0.15 | 232.9 |
| B-dbg-2 | 1997078 | 0.04 | 0.28 | 230.3 | inbound:GameSelectCharacter | 1986985 | 0.04 | 0.13 | 230.3 |
| A-dbg-f256 | 1448961 | 0.04 | 26.91 | 237.1 | inbound:GameSelectCharacter | 1437074 | 0.03 | 26.97 | 237.1 |
| B-dbg-f256 | 1982329 | 0.04 | 0.32 | 234.8 | inbound:GameSelectCharacter | 1972230 | 0.04 | 0.13 | 234.8 |
| ramp-A-2s | 99741 | 24.43 | 94.99 | 233.0 | inbound:GameSelectCharacter | 93581 | 25.06 | 100.6 | 233.0 |
(host.log stamps are millisecond wall clock; readiness from summary.json originUtc (ramps: first ramp line - 50 s); M-2 values: m2-summary.md §5.3 / TASK-28 Result: p99.9 A 26.89/25.56 (f256 26.23), B 0.29/0.27/0.26, ramp-A-2s 98.84; max A 229.6/239.1/226.8, B 227.5/229.9/227.8, ramp-A-2s 230.2)

pooled max over all debug runs and ramp-A-2s: 269.3 ms (A-dbg-1, inbound:GameSelectCharacter); p99.9 range 0.28..94.99 ms

入队等待（四个窗口；全部作业，括号内为只算 `inbound:` 的 p99 / max），最后一列是 M-2 的 p99：

| 窗口 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-2 p99（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 (f) | 26147 / 0.07 / 112.0 / 522.0 / 664.5（inbound 107.2 / 603.4）；max：event:achievement.inventory | 26134 / 0.07 / 101.3 / 382.9 / 872.5（inbound 96.31 / 872.5）；max：inbound:GameConnectionCheck | 25413 / 0.01 / 41.72 / 97.07 / 233.8（inbound 33.19 / 115.5）；max：event:achievement.inventory | 25393 / 0.02 / 39.80 / 108.0 / 234.9（inbound 27.72 / 126.2）；max：event:achievement.inventory | 26100 / 0.04 / 101.7 / 592.5 / 704.0（inbound 100.0 / 703.7）；max：http:POST /api/launcher/v1/register | 25417 / 0.01 / 37.40 / 91.51 / 235.7（inbound 30.41 / 114.0）；max：event:quest.inventory | 103.1；105.0；40.52；36.49 |
| 稳态窗 (c) | 52893 / 0.05 / 93.98 / 135.4 / 190.8（inbound 92.49 / 190.8）；max：inbound:GameSetUserPosition | 52951 / 0.07 / 89.78 / 138.2 / 170.8（inbound 89.24 / 170.8）；max：inbound:GameSetUserPosition | 52101 / 0.01 / 20.97 / 46.48 / 61.22（inbound 16.47 / 61.22）；max：inbound:GameConnectionCheck | 52088 / 0.02 / 14.78 / 42.93 / 60.45（inbound 9.85 / 60.45）；max：inbound:GameConnectionCheck | 52940 / 0.03 / 97.53 / 142.1 / 190.4（inbound 96.25 / 184.7）；max：timer:TournamentRewardSweepService | 52103 / 0.01 / 19.81 / 47.11 / 65.40（inbound 14.75 / 65.40）；max：inbound:GameConnectionCheck | 94.31；99.75；16.56；19.01 |
| (d) 轮内 | 1080 / 0.53 / 136.9 / 152.0 / 153.0（inbound 120.7 / 139.2）；max：event:southern-dale.daily-refill-notifications | 1104 / 0.35 / 103.3 / 131.9 / 132.0（inbound 104.7 / 132.0）；max：inbound:GameConnectionCheck | 872* / 0.04 / 17.32 / 89.91 / 89.91（inbound 17.33 / 17.35）；max：timer:TournamentRewardSweepService | 867* / 0.07 / 2.42 / 10.89 / 10.89（inbound 2.42 / 2.49）；max：timer:TournamentRewardSweepService | 1094 / 0.33 / 103.8 / 112.9 / 112.9（inbound 103.8 / 111.2）；max：event:quest.inventory | 917* / 0.10 / 53.83 / 62.61 / 62.61（inbound 53.83 / 62.61）；max：inbound:GameConnectionCheck | 100.0；180.9；1.00；25.92 |
| (e) 灌包期间 | 1366177 / 0.00 / 0.01 / 37.01 / 182.2（inbound 0.01 / 182.2）；max：inbound:GameConnectionCheck | 1339826 / 0.00 / 0.01 / 36.16 / 189.9（inbound 0.01 / 189.9）；max：inbound:GameConnectionCheck | 1893976 / 0.00 / 0.01 / 0.05 / 39.80（inbound 0.01 / 39.80）；max：inbound:GameConnectionCheck | 1891261 / 0.00 / 0.01 / 0.06 / 39.90（inbound 0.01 / 39.90）；max：inbound:GameConnectionCheck | 1341234 / 0.00 / 0.01 / 37.84 / 142.5（inbound 0.01 / 142.5）；max：inbound:GameConnectionCheck | 1876498 / 0.00 / 0.01 / 0.05 / 38.94（inbound 0.01 / 38.94）；max：inbound:GameConnectionCheck | 0.01；0.01；0.01；0.01 |

执行器占用与每秒作业数：

| 窗口 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-dbg-f256 | B-dbg-f256 | M-2（A-dbg-1；A-dbg-2；B-dbg-1；B-dbg-2） |
|---|---:|---:|---:|---:|---:|---:|---:|
| cold | 43.2%（26147 个） | 42.7%（26134 个） | 7.4%（25413 个） | 7.2%（25393 个） | 42.6%（26100 个） | 7.4%（25417 个） | 41.9%；42.3%；7.5%；7.2% |
| steady | 39.0%（52893 个） | 39.0%（52951 个） | 3.8%（52101 个） | 3.5%（52088 个） | 39.1%（52940 个） | 3.8%（52103 个） | 38.9%；39.3%；3.6%；3.4% |
| burst | 81.0%（1080 个） | 68.5%（1104 个） | 4.8%（872 个） | 2.9%（867 个） | 63.7%（1094 个） | 21.0%（917 个） | 56.2%；49.9%；2.9%；7.3% |
| flood | 55.9%（1366177 个） | 56.9%（1339826 个） | 28.8%（1893976 个） | 28.8%（1891261 个） | 57.0%（1341234 个） | 28.9%（1876498 个） | 56.5%；55.5%；28.7%；28.3% |
| 稳态每秒作业数 | inbound: 400.3，timer: 31.3，event: 9.1 | inbound: 400.4，timer: 31.7，event: 9.2 | inbound: 400.5，timer: 31.8，event: 1.9 | inbound: 400.4，timer: 31.8，event: 1.8 | inbound: 400.5，timer: 31.5，event: 9.2 | inbound: 400.5，timer: 31.8，event: 1.9 | inbound: 400.6，timer: 31.7，event: 9.3；inbound: 400.2，timer: 31.4，event: 9.2；inbound: 400.5，timer: 31.8，event: 1.8；inbound: 400.5，timer: 31.8，event: 1.8 |

### 5.5 内层命令

`baseline-summary.md`「M-1 预警」表的 8 行（max，ms）：

| 命令 | 基线 B-1 / B-2 | M-1（8 次） | M-2（8 次） | A-def-1 | A-def-2 | B-def-1 | B-def-2 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `EnterDungeonCommand` | 633.1 / 1103.9 | 54.08～61.60 | 55.00～78.57 | 54.96（n 82） | 59.35（n 79） | 56.34（n 79） | 55.98（n 78） | 69.53（n 80） | 59.11（n 79） | 57.26（n 79） | 57.83（n 80） |
| `LeaveDungeonCommand` | 1019.0 / 697.2 | 13.55～17.15 | 8.74～18.72 | 9.42（n 78） | 10.56（n 78） | 16.54（n 78） | 15.26（n 78） | 13.96（n 79） | 11.34（n 78） | 19.15（n 78） | 17.09（n 78） |
| `RegisterAccountCommand` | 665.7 / 668.0 | 6.15～12.65 | 4.46～12.07 | 6.30（n 81） | 4.75（n 81） | 4.70（n 81） | 4.63（n 81） | 8.17（n 81） | 6.58（n 81） | 4.54（n 81） | 4.52（n 81） |
| `AuthenticateAndIssueTicketCommand` | 637.5 / 631.7 | 1.89～4.84 | 2.31～7.32 | 2.55（n 81） | 6.68（n 81） | 2.40（n 81） | 2.42（n 81） | 7.82（n 81） | 2.83（n 81） | 2.34（n 81） | 2.29（n 81） |
| `RedeemGameLoginCommand` | 570.2 / 567.2 | 1.61～3.52 | 1.58～5.50 | 1.67（n 81） | 1.66（n 81） | 1.61（n 81） | 1.62（n 81） | 1.64（n 81） | 1.69（n 81） | 1.58（n 81） | 1.59（n 81） |
| `PrepareSelectedCharacterStateCommand` | 502.8 / 520.2 | 98.27～103.8 | 98.56～110.0 | 105.4（n 484） | 101.3（n 484） | 100.7（n 21） | 103.3（n 21） | 101.8（n 485） | 105.2（n 484） | 102.1（n 21） | 99.03（n 21） |
| `AdvanceCreatureClockCommand` | 346.0 / 280.2 | 3.07～3.33 | 3.05～5.48 | 3.40（n 782） | 21.40（n 773） | 5.31（n 335） | 3.21（n 333） | 3.55（n 772） | 3.34（n 769） | 3.37（n 335） | 3.81（n 337） |
| `ExpireCharacterNameTagCommand` | 1.56 / 613.0 | 0.40～1.80 | 0.40～5.51 | 0.53（n 84） | 0.60（n 104） | 0.42（n 96） | 1.50（n 105） | 1.25（n 105） | 0.49（n 105） | 2.55（n 105） | 0.65（n 105） |

选角作业内的命令（构成 A），最后一列是 M-2 四次的 p50：

| 命令 | A-def-1 | A-def-2 | A-dbg-1 | A-dbg-2 | M-2 p50（4 次） |
|---|---:|---:|---:|---:|---:|
| `PrepareSelectedCharacterStateCommand` | 484* / 3.79 / 14.46 / 105.4 / 105.4 | 484* / 3.87 / 15.52 / 101.3 / 101.3 | 485* / 3.69 / 13.47 / 101.8 / 101.8 | 484* / 3.79 / 12.83 / 105.2 / 105.2 | 3.70；3.68；3.71；3.52 |
| `ApplySelectedCharacterDailyRefillCommand` | 484* / 12.77 / 48.94 / 62.83 / 62.83 | 484* / 12.93 / 50.22 / 52.76 / 52.76 | 485* / 12.81 / 56.98 / 63.11 / 63.11 | 484* / 12.68 / 44.63 / 61.89 / 61.89 | 12.69；12.57；12.68；13.03 |
| `RefillOnlineSouthernTicketCommand` | 484* / 14.60 / 60.05 / 107.8 / 107.8 | 484* / 14.53 / 56.33 / 59.64 / 59.64 | 485* / 14.65 / 60.51 / 134.2 / 134.2 | 484* / 14.52 / 57.63 / 60.45 / 60.45 | 14.58；14.35；14.99；14.95 |
| `RefillOnlineBossTowerTicketCommand` | 484* / 15.14 / 52.87 / 55.85 / 55.85 | 484* / 15.04 / 50.46 / 52.92 / 52.92 | 485* / 15.13 / 60.38 / 62.29 / 62.29 | 484* / 15.11 / 56.25 / 63.54 / 63.54 | 15.14；14.79；15.17；15.20 |
| `RefillOnlineTauTicketCommand` | 484* / 16.54 / 60.77 / 64.13 / 64.13 | 484* / 16.16 / 59.75 / 64.21 / 64.21 | 485* / 17.52 / 60.66 / 62.73 / 62.73 | 484* / 16.39 / 59.45 / 61.90 / 61.90 | 16.21；16.49；16.21；17.49 |

内层命令 max 前 15：

| 命令 | A-def-1 | A-def-2 | B-def-1 | B-def-2 | A-dbg-1 | A-dbg-2 | B-dbg-1 | B-dbg-2 | A-def-f256 | B-def-f256 | A-dbg-f256 | B-dbg-f256 | B-def-g0-2 | B-def-g0-3 | B-def-g0 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `RefillOnlineSouthernTicketCommand` | 107.8 | 59.64 | 60.19 | 61.71 | 134.2 | 60.45 | 36.38 | 24.68 | 59.94 | 59.11 | 64.36 | 56.83 | 51.32 | 38.94 | 49.80 |
| `PrepareSelectedCharacterStateCommand` | 105.4 | 101.3 | 100.7 | 103.3 | 101.8 | 105.2 | 102.1 | 99.03 | 103.9 | 99.29 | 104.3 | 103.5 | 104.1 | 100.5 | 104.4 |
| `RefillOnlineTauTicketCommand` | 64.13 | 64.21 | 54.86 | 28.87 | 62.73 | 61.90 | 59.01 | 55.65 | 87.44 | 32.25 | 62.48 | 24.24 | 48.96 | 58.37 | 47.91 |
| `EnterDungeonCommand` | 54.96 | 59.35 | 56.34 | 55.98 | 69.53 | 59.11 | 57.26 | 57.83 | 59.62 | 56.61 | 60.01 | 58.40 | 56.52 | 56.02 | 55.34 |
| `RefillOnlineBossTowerTicketCommand` | 55.85 | 52.92 | 44.04 | 65.15 | 62.29 | 63.54 | 53.76 | 55.14 | 61.87 | 38.93 | 63.06 | 50.57 | 47.72 | 38.52 | 45.40 |
| `ApplySelectedCharacterDailyRefillCommand` | 62.83 | 52.76 | 54.92 | 51.09 | 63.11 | 61.89 | 42.27 | 60.08 | 59.99 | 53.17 | 57.04 | 28.15 | 27.60 | 56.47 | 28.39 |
| `CreateCharacterCommand` | 24.16 | 37.47 | 24.11 | 24.11 | 31.63 | 32.88 | 24.00 | 23.78 | 25.30 | 24.13 | 24.60 | 24.14 | 24.07 | 23.75 | 24.36 |
| `AdvanceCreatureClockCommand` | 3.40 | 21.40 | 5.31 | 3.21 | 3.55 | 3.34 | 3.37 | 3.81 | 3.09 | 3.30 | 3.31 | 3.77 | 3.04 | 3.12 | 3.14 |
| `GetAccountCharacterRosterCommand` | 5.54 | 5.68 | 7.18 | 5.56 | 5.44 | 5.74 | 5.12 | 5.66 | 5.00 | 6.88 | 5.73 | 20.94 | 5.26 | 9.71 | 6.78 |
| `LeaveDungeonCommand` | 9.42 | 10.56 | 16.54 | 15.26 | 13.96 | 11.34 | 19.15 | 17.09 | 9.96 | 17.20 | 8.42 | 13.60 | 13.86 | 18.31 | 17.20 |
| `CompleteReturnToSelectionCommand` | 11.55 | 6.38 | – | – | 6.49 | 6.22 | – | – | 6.92 | – | 6.74 | – | – | – | – |
| `RegisterAccountCommand` | 6.30 | 4.75 | 4.70 | 4.63 | 8.17 | 6.58 | 4.54 | 4.52 | 7.06 | 4.69 | 5.79 | 4.57 | 4.33 | 4.55 | 4.58 |
| `PublishSelectedCharacterSessionCommand` | 3.68 | 7.96 | 3.73 | 3.76 | 3.78 | 3.62 | 3.74 | 3.61 | 7.18 | 3.65 | 3.65 | 3.77 | 3.79 | 3.61 | 3.67 |
| `ActivateClientHotkeyProfileCommand` | 1.63 | 3.03 | 1.62 | 1.48 | 7.85 | 1.57 | 1.44 | 1.46 | 2.90 | 1.50 | 1.81 | 1.53 | 1.42 | 1.46 | 1.51 |
| `AuthenticateAndIssueTicketCommand` | 2.55 | 6.68 | 2.40 | 2.42 | 7.82 | 2.83 | 2.34 | 2.29 | 2.64 | 2.84 | 6.43 | 2.47 | 2.35 | 2.40 | 2.38 |

### 5.6 吞吐爬坡（b）

- 第一张是构成 B（`m3-ramp-clean.md`），其后是判 S3 的 2 s 口径爬坡（`m3-ramp-full-2s.md`）与 p99 ≤ 50 ms 口径的构成 A（`m3-ramp-full.md`）。每份爬坡后附逐档的「档末 5 秒每秒发出滞后 max」与执行器占用。「趋势」一列是机械判据：5 个值中后一秒大于前一秒的至少 3 次，且末值大于首值，记为「上升」。

**ramp-B**（`ramp-clean`）：可持续吞吐 24999.0；停止原因 全部档位通过（未触顶，最高档即下限）；Host 日志中内层命令 max 104.0 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 判定（p99 ≤ 50 ms 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 7123 | 0.35 | 1.96 | 20.46 | 58.09 | 66.61 | 20.18 | 5（0.069%） | 26.06 | 26.06 | 0 | 通过 |
| 800 | 800 | 14315 | 0.28 | 1.61 | 16.91 | 43.42 | 55.10 | 14.86 | 5（0.035%） | 22.28 | 22.28 | 0 | 通过 |
| 1200 | 1200 | 21511 | 0.24 | 1.40 | 16.65 | 44.76 | 56.58 | 9.92 | 5（0.023%） | 17.63 | 17.63 | 0 | 通过 |
| 1600 | 1600 | 28711 | 0.21 | 1.12 | 22.20 | 46.87 | 57.86 | 8.70 | 5（0.017%） | 18.09 | 18.09 | 0 | 通过 |
| 2000 | 2000 | 35922 | 0.19 | 1.22 | 16.15 | 41.71 | 55.13 | 3.15 | 4（0.011%） | 16.12 | 16.12 | 0 | 通过 |
| 2400 | 2400 | 43132 | 0.18 | 1.09 | 22.01 | 48.42 | 59.38 | 2.59 | 2（0.005%） | 17.88 | 17.88 | 0 | 通过 |
| 3000 | 3000 | 53916 | 0.21 | 1.14 | 20.75 | 42.47 | 55.00 | 3.46 | 4（0.007%） | 18.98 | 18.98 | 0 | 通过 |
| 4000 | 4000 | 71896 | 0.18 | 1.06 | 17.67 | 45.85 | 58.19 | 3.37 | 6（0.008%） | 21.72 | 21.72 | 0 | 通过 |
| 5000 | 5000 | 89918 | 0.21 | 1.41 | 18.67 | 42.35 | 53.89 | 3.68 | 4（0.004%） | 21.06 | 21.06 | 0 | 通过 |
| 6000 | 6000 | 107911 | 0.27 | 2.00 | 16.12 | 43.55 | 53.06 | 4.19 | 5（0.005%） | 16.48 | 16.48 | 0 | 通过 |
| 8000 | 8000 | 143918 | 0.30 | 2.88 | 18.87 | 37.52 | 47.04 | 4.65 | 4（0.003%） | 13.15 | 13.15 | 0 | 通过 |
| 10000 | 9999 | 179906 | 0.29 | 3.49 | 19.72 | 38.55 | 45.67 | 4.87 | 5（0.003%） | 21.30 | 21.30 | 0 | 通过 |
| 12000 | 12000 | 215924 | 0.28 | 3.71 | 13.50 | 33.06 | 41.64 | 4.98 | 3（0.001%） | 14.90 | 14.90 | 0 | 通过 |
| 15000 | 15000 | 269917 | 0.26 | 4.53 | 18.52 | 34.65 | 42.17 | 5.25 | 5（0.002%） | 13.42 | 13.42 | 0 | 通过 |
| 20000 | 20001 | 359925 | 0.25 | 4.68 | 20.44 | 35.86 | 44.28 | 5.13 | 5（0.001%） | 15.25 | 15.25 | 0 | 通过 |
| 25000 | 24999 | 449916 | 0.32 | 6.72 | 31.08 | 44.02 | 49.90 | 6.42 | 5（0.001%） | 16.04 | 16.04 | 0 | 通过 |

ramp-B 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 通过 |  | 2.1 / 2.7 / 2.9 / 2.8 / 2.2 | 无上升趋势 |
| 800 | 800 | 通过 |  | 2.1 / 7.2 / 2.1 / 2.1 / 2.0 | 无上升趋势 |
| 1200 | 1200 | 通过 |  | 1.5 / 1.8 / 3.3 / 1.8 / 2.5 | 上升 |
| 1600 | 1600 | 通过 |  | 1.2 / 1.5 / 1.2 / 3.6 / 1.1 | 无上升趋势 |
| 2000 | 2000 | 通过 |  | 1.5 / 4.9 / 1.6 / 5.0 / 1.5 | 无上升趋势 |
| 2400 | 2400 | 通过 |  | 3.5 / 49.3 / 1.2 / 3.0 / 5.8 | 上升 |
| 3000 | 3000 | 通过 |  | 2.5 / 1.6 / 37.6 / 2.6 / 1.7 | 无上升趋势 |
| 4000 | 4000 | 通过 |  | 4.1 / 2.8 / 46.5 / 1.2 / 1.6 | 无上升趋势 |
| 5000 | 5000 | 通过 |  | 3.5 / 2.2 / 43.3 / 1.6 / 1.8 | 无上升趋势 |
| 6000 | 6000 | 通过 |  | 3.0 / 2.6 / 2.0 / 48.0 / 3.0 | 无上升趋势 |
| 8000 | 8000 | 通过 |  | 3.5 / 3.6 / 3.8 / 3.9 / 44.4 | 上升 |
| 10000 | 9999 | 通过 |  | 4.3 / 4.2 / 4.1 / 4.0 / 4.3 | 无上升趋势 |
| 12000 | 12000 | 通过 |  | 32.3 / 4.3 / 4.8 / 5.5 / 4.9 | 无上升趋势 |
| 15000 | 15000 | 通过 |  | 4.5 / 4.7 / 4.7 / 5.6 / 39.6 | 上升 |
| 20000 | 20001 | 通过 |  | 5.4 / 5.4 / 5.5 / 39.2 / 5.1 | 无上升趋势 |
| 25000 | 24999 | 通过 |  | 7.3 / 6.4 / 8.1 / 49.2 / 7.0 | 无上升趋势 |

**ramp-A-2s**（`ramp-A-2s`）：可持续吞吐 1163.0；停止原因 判定 max ≥ 2000 ms；Host 日志中内层命令 max 101.4 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 作业数 | 作业执行时长 max (ms) | max 的作业 | 入队等待 max (ms) | 判定（2 s 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 6819 | 1.18 | 57.12 | 92.60 | 137.1 | 169.8 | 87.08 | 53（0.737%） | 142.8 | 142.8 | 0 | 7998 | 113.1 | inbound:GameSelectCharacter | 148.6 | 通过 |
| 800 | 800 | 13840 | 10.42 | 96.25 | 141.5 | 176.7 | 261.3 | 102.3 | 74（0.514%） | 176.6 | 176.6 | 0 | 15210 | 113.1 | inbound:GameSelectCharacter | 179.2 | 通过 |
| 1200 | 1163 | 20813 | 160.2 | 687.7 | 982.9 | 1166.9 | 1251.1 | 117.9 | 100（0.478%） | 166.3 | 168.3 | 0 | 23078 | 114.5 | inbound:GameSelectCharacter | 168.3 | 通过 |
| 1600 | 1191 | 27666 | 4978.9 | 9080.5 | 9521.5 | 9759.7 | 10012.4 | 119.6 | 100（0.466%） | 166.9 | 193.7 | 0 | 33634 | 121.9 | inbound:GameSelectCharacter | 211.6 | 停止 |

ramp-A-2s 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 通过 | 43.1% | 18.3 / 32.2 / 15.9 / 33.9 / 33.7 | 无上升趋势 |
| 800 | 800 | 通过 | 63.4% | 92.6 / 136.0 / 102.6 / 103.5 / 77.1 | 无上升趋势 |
| 1200 | 1163 | 通过 | 91.3% | 658.4 / 802.8 / 817.5 / 920.3 / 1234.4 | 上升 |
| 1600 | 1191 | 停止 | 98.9% | 8319.4 / 8509.5 / 9046.2 / 9540.4 / 9783.0 | 上升 |

**ramp-A**（`ramp-full`）：可持续吞吐 无通过档；停止原因 p99 > 50 ms；Host 日志中内层命令 max 100.7 ms（PrepareSelectedCharacterStateCommand）

| 目标包/秒 | 实际包/秒 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 | 长请求包数（占比） | 长请求 p99 | 长请求 max | 错误 | 判定（p99 ≤ 50 ms 口径） |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 400 | 400 | 6811 | 1.80 | 67.37 | 106.4 | 150.3 | 177.5 | 106.1 | 56（0.778%） | 177.5 | 177.5 | 0 | 停止 |

ramp-A 逐档：档末 5 秒每秒发出滞后 max 与执行器占用（档内第 2 s 起 18 s；只有 debug 爬坡有作业层）

| 目标包/秒 | 实际包/秒 | 判定 | 执行器占用 | 档末 5 s 滞后 max (ms) | 趋势 |
|---|---:|---:|---:|---:|---:|
| 400 | 400 | 停止 |  | 65.8 / 68.2 / 90.8 / 101.9 / 32.8 | 无上升趋势 |

- **(b) 的两个数**（TASK-28 §1.4）：
  - 2 s 口径下构成 A 的可持续吞吐 **1163 包/秒**（1200 档的实际速率；M-2 1169，M-1 1188）；
  - **稳定通过档 800**（实际 800 包/秒）：800 档档末 5 秒滞后 77.1～136.0 ms，无上升趋势，执行器占用 63.4%；1200 档滞后 658.4 → 1234.4 ms 上升，占用 91.3%。1163 包/秒只是 18 秒档长下的上限，不能当作余量依据（F-77）。
  - 1200 档判定 p99 982.9 ms、max 1251.1 ms（2 s 线的 0.63 倍；M-2 1011.7／1266.0 ms）；1600 档实际只到 1191 包/秒、max 10012.4 ms，停止。
- **构成 B**：16 档全部通过，≥ 25000 包/秒，未触顶，与基线、M-1、M-2 相同；各档 max ≤ 66.61 ms。机械判据标的几处「上升」是一秒 40～50 ms 的单点，不是积压，所以构成 B 的稳定通过档也是 25000。
- **p99 ≤ 50 ms 口径**：构成 A 在 400 档停止（判定 p99 106.4 ms；M-2 100.7 ms），只作目标对照。

### 5.7 稳态端到端 p99 与基线、M-1、M-2 的对照

基线（构成 B，TASK-03 clean-1／clean-2）稳态 p99：2.73～2.79 ms；基线构成 A 的稳态 p99 为 229761.2 ms（SQLite 30 秒停顿主导，不可比）

| 运行 | 稳态端到端 p99 | 相对基线 B | M-1 同名运行 | M-2 同名运行 | 同次稳态入队等待 p99 |
|---|---:|---:|---:|---:|---:|
| A-def-1 | 99.03 | 35.5～36.2 倍 | 91.65 | 103.9 | – |
| A-def-2 | 99.84 | 35.8～36.5 倍 | 94.56 | 97.49 | – |
| B-def-1 | 19.22 | 6.9～7.0 倍 | 16.58 | 26.07 | – |
| B-def-2 | 19.84 | 7.1～7.3 倍 | 18.38 | 20.52 | – |
| A-dbg-1 | 101.8 | 36.5～37.2 倍 | 95.53 | 103.9 | 93.98 |
| A-dbg-2 | 98.94 | 35.5～36.2 倍 | 92.31 | 114.5 | 89.78 |
| B-dbg-1 | 21.48 | 7.7～7.9 倍 | 14.65 | 16.33 | 20.97 |
| B-dbg-2 | 15.59 | 5.6～5.7 倍 | 15.76 | 18.55 | 14.78 |
| A-def-f256 | 107.9 | 38.7～39.5 倍 | – | 98.32 | – |
| B-def-f256 | 19.62 | 7.0～7.2 倍 | – | 25.30 | – |
| A-dbg-f256 | 106.5 | 38.2～39.0 倍 | – | 92.83 | 97.53 |
| B-dbg-f256 | 19.47 | 7.0～7.1 倍 | – | 14.96 | 19.81 |
| B-def-g0-2 | 19.32 | 6.9～7.1 倍 | – | – | –（默认级别无作业层） |
| B-def-g0-3 | 17.92 | 6.4～6.6 倍 | – | – | –（默认级别无作业层） |
| B-def-g0 | 17.40 | 6.2～6.4 倍 | – | 22.28 | –（默认级别无作业层） |

构成 A 稳态窗的执行器占用按作业类型分解（M-3 三次与 M-2 两次）：

| 运行 | 执行时长之和／窗口（占用） | 前 6 类（占执行时长之和的比例） |
|---|---:|---:|
| A-dbg-1 | 46.8 s／120 s（39.0%） | inbound:GameSetUserArea 27.8 s（59.4%）；inbound:GameSelectCharacter 15.2 s（32.4%）；inbound:GameEplpCommand 1.5 s（3.3%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.4 s（0.9%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| A-dbg-2 | 46.8 s／120 s（39.0%） | inbound:GameSetUserArea 27.9 s（59.6%）；inbound:GameSelectCharacter 14.8 s（31.6%）；inbound:GameEplpCommand 1.7 s（3.6%）；inbound:GameEnterSelectDungeon 0.8 s（1.8%）；inbound:GameSetUserPosition 0.5 s（1.0%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| A-dbg-f256 | 46.9 s／120 s（39.1%） | inbound:GameSetUserArea 27.0 s（57.7%）；inbound:GameSelectCharacter 15.9 s（33.9%）；inbound:GameEplpCommand 1.3 s（2.8%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.5 s（1.1%）；inbound:GameSelectDungeon 0.4 s（0.9%） |
| M2-A-dbg-1 | 46.7 s／120 s（38.9%） | inbound:GameSetUserArea 27.3 s（58.4%）；inbound:GameSelectCharacter 15.4 s（33.0%）；inbound:GameEplpCommand 1.5 s（3.2%）；inbound:GameEnterSelectDungeon 0.8 s（1.7%）；inbound:GameSetUserPosition 0.4 s（0.9%）；inbound:GameSelectDungeon 0.4 s（0.8%） |
| M2-A-dbg-2 | 47.1 s／120 s（39.3%） | inbound:GameSetUserArea 26.9 s（57.1%）；inbound:GameSelectCharacter 16.1 s（34.2%）；inbound:GameEplpCommand 1.5 s（3.2%）；inbound:GameEnterSelectDungeon 0.9 s（1.9%）；inbound:GameSetUserPosition 0.5 s（1.0%）；inbound:GameSelectDungeon 0.4 s（0.8%） |

A-dbg-1：一次选角里四条每日补给命令 p50 之和 60.11 ms，选角作业 p50 68.95 ms，比例 87.2%（各自 p50 相加，只是量级）

A-dbg-2：一次选角里四条每日补给命令 p50 之和 58.70 ms，选角作业 p50 68.17 ms，比例 86.1%（各自 p50 相加，只是量级）

A-dbg-f256：一次选角里四条每日补给命令 p50 之和 59.95 ms，选角作业 p50 68.62 ms，比例 87.4%（各自 p50 相加，只是量级）

## 6. T-007、TASK-30、TASK-31 带来的变化

### 6.1 §4.4 六条预期逐条对照

作业执行时长 M-2 → M-3（p50 与 p99，debug 运行逐对）：

| 作业 | A-1（M-2 → M-3） | A-2 | B-1 | B-2 |
|---|---:|---:|---:|---:|
| `inbound:GameSetUserArea` | p50 23.244 → 23.514（0.270）；p99 52.52 → 54.18 | p50 22.561 → 23.785（1.224）；p99 52.97 → 57.00 | p50 14.169 → 13.379（-0.790）；p99 36.86 → 19.46 | p50 14.458 → 10.706（-3.751）；p99 24.14 → 39.39 |
| `inbound:GameSelectCharacter` | p50 68.334 → 68.950（0.617）；p99 130.0 → 137.0 | p50 69.368 → 68.172（-1.195）；p99 142.6 → 128.8 | p50 75.774 → 72.606（-3.168）；p99 227.5 → 232.9 | p50 69.451 → 70.219（0.768）；p99 229.9 → 230.3 |
| `inbound:GameEplpCommand` | p50 45.375 → 45.657（0.283）；p99 77.21 → 91.70 | p50 40.769 → 44.591（3.823）；p99 96.86 → 157.3 | p50 34.562 → 41.273（6.711）；p99 59.52 → 61.12 | p50 37.619 → 37.787（0.168）；p99 58.64 → 60.42 |
| `inbound:GameSelectDungeon` | p50 9.927 → 10.281（0.354）；p99 77.26 → 85.66 | p50 10.232 → 9.772（-0.460）；p99 73.22 → 74.13 | p50 14.585 → 14.089（-0.497）；p99 72.64 → 75.26 | p50 14.899 → 14.458（-0.441）；p99 73.48 → 75.56 |
| `inbound:GameEnterSelectDungeon` | p50 21.202 → 22.533（1.331）；p99 45.90 → 42.72 | p50 20.277 → 22.558（2.281）；p99 56.15 → 49.05 | p50 24.007 → 25.693（1.686）；p99 62.25 → 64.18 | p50 25.852 → 21.539（-4.313）；p99 53.37 → 63.93 |
| `inbound:GameSetUserPosition` | p50 0.013 → 0.012（-0.001）；p99 0.12 → 0.12 | p50 0.011 → 0.012（0.001）；p99 0.11 → 0.12 | p50 0.030 → 0.028（-0.003）；p99 0.14 → 0.15 | p50 0.021 → 0.026（0.005）；p99 0.09 → 0.13 |
| `inbound:GameConnectionCheck` | p50 0.003 → 0.003（0.000）；p99 0.01 → 0.01 | p50 0.002 → 0.003（0.000）；p99 0.01 → 0.01 | p50 0.002 → 0.003（0.000）；p99 0.02 → 0.02 | p50 0.003 → 0.003（0.000）；p99 0.01 → 0.01 |

定时作业逐类计数（每次运行）：

| 定时作业 | A-1（M-2 → M-3） | A-2 | B-1 | B-2 |
|---|---:|---:|---:|---:|
| `timer:AltarQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:BossTowerReturnSweepService` | 553 → 553 | 553 → 553 | 554 → 554 | 554 → 554 |
| `timer:CreatureClockService` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:DimensionQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:DungeonPartyWipeService` | 1105 → 1105 | 1104 → 1104 | 1108 → 1108 | 1108 → 1108 |
| `timer:FatigueDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:GoldSilverHornQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:HerasQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:IllusionTowerReturnSweepService` | 553 → 553 | 553 → 553 | 554 → 554 | 554 → 554 |
| `timer:ItemUsageDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:JoustMaintenanceService` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:LoginSessionMaintenanceService` | 9 → 9 | 9 → 9 | 9 → 9 | 9 → 9 |
| `timer:MarriageWeddingScheduler` | 276 → 276 | 276 → 276 | 277 → 277 | 277 → 277 |
| `timer:MirrorQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:PremiumDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:ReviveCoinQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:RoomMechanismMaintenanceService` | 162 → 160 | 158 → 158 | 160 → 158 | 158 → 160 |
| `timer:ScheduledTaskHostedService/character.name-tag.expiration` | 4 → 5 | 5 → 5 | 5 → 5 | 5 → 5 |
| `timer:TournamentQuotaDailyRefreshService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |
| `timer:TournamentRewardSweepService` | 5463 → 5416 | 5434 → 5442 | 5537 → 5535 | 5536 → 5532 |
| `timer:WeaknessRecoveryService` | 4 → 4 | 4 → 4 | 4 → 4 | 4 → 4 |

| # | 预期 | 实测 | 判断 |
|---|---|---|---|
| 1 | 数字与 M-2 同量级；轻量作业 p50 持平 | 34 项检验中「相对 M-2 显著变差」0 项（§5.2）；五类长作业 p50 与 M-2 相差都在 ±7 ms 内，有升有降；`GameSetUserPosition` p50 0.012～0.028 ms（M-2 0.011～0.030 ms），`GameConnectionCheck` 0.003 ms（M-2 0.002～0.003 ms）；定时作业的计数逐类接近（差 ≤ 47 个，来自运行时长的毫秒差）；构成 A 稳态执行器占用 39.0%～39.1%（M-2 38.9%～39.3%） | **符合** |
| 2 | 构成 B 每次探测连接上 2 行带后缀的 Information、0 行 Warning；构成 A 0 行 | 构成 B 10 次运行每次 2 行带后缀的 Information、0 行 Warning；构成 A 8 次 0 行 | **符合** |
| 3 | 收尾窗口内的行大部分变为带后缀的 Information；剩下的 Warning 是 `TimedOut` 或 `SocketException` 形态的 `Failed` | 收尾 106 行中 Warning 72 行、Information 34 行，Warning 多于 Information；Warning 全是 `InvalidOperationException: Writing is not allowed after writer was completed`，没有 `TimedOut`，也没有 `SocketException` | **不符合**（全部在收尾窗口内，按规则不计；登记为 F-127） |
| 4 | 错误、失败命令、失败作业、15 项信号、`OverLimit`、「计入」的发送端层行、`Game handler failed` 都是 0 | 全部是 0 | **符合** |
| 5 | 三次 G0 的 (e) 若在 4.4～4.9 ms 附近，M-2 的 17.84 ms 多半是噪声；若三次都在 15 ms 以上，(e) 在 G0 口径下确实大 3～4 倍 | 三次为 9.84／14.84／8.47 ms，中位数 9.84 ms，约是 M-1 的 2 倍，两种情形都不是 | **都不是**：G0 口径下约翻倍，与 G1 口径一致；M-2 的 17.84 ms 多半含噪声（§5.3） |
| 6 | 解析自检相等，数值解析失败 0 | 6 次 debug `run` 全部相等，失败 0 | **符合** |

### 6.2 端到端延迟的构成（估计）

- 算法同 M-2：同窗口、同请求类型的分布相减，`作业之后一段 ≈ 参考端到端 − 入队等待 − 执行时长`，p50 与 p99 各算一次。**这不是逐样本的拆分**，p99 一列只作量级参考。(e) 窗里 CHECK_USER_CONNECTION 那一行被灌包连接的作业主导，不可用。

| 测量 | 运行 | 窗口 | 请求 ↔ 作业 | 参考端到端 n / p50 / p99 | 入队等待 n / p50 / p99 | 执行时长 p50 / p99 | 作业之后一段 p50 估计 | p99 估计（量级） |
|---|---|---|---|---:|---:|---:|---:|---:|
| M-3 | A-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25555 / 0.259 / 94.257 | 27727 / 0.051 / 91.419 | 0.002 / 0.053 | 0.206 | 2.785 |
| M-3 | A-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18472 / 0.296 / 95.283 | 18465 / 0.052 / 93.812 | 0.012 / 0.123 | 0.232 | 1.348 |
| M-3 | A-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6520 / 0.203 / 104.3 | 1359943 / 0.001 / 0.010 | 0.003 / 0.011 | 0.199 | 104.2 |
| M-3 | A-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4509 / 0.225 / 103.9 | 4509 / 0.055 / 101.0 | 0.015 / 0.120 | 0.154 | 2.851 |
| M-3 | A-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25757 / 0.304 / 93.195 | 27950 / 0.065 / 90.475 | 0.002 / 0.086 | 0.237 | 2.633 |
| M-3 | A-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18242 / 0.354 / 89.127 | 18238 / 0.072 / 86.808 | 0.012 / 0.123 | 0.270 | 2.196 |
| M-3 | A-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6226 / 0.261 / 107.1 | 1333399 / 0.001 / 0.009 | 0.003 / 0.010 | 0.257 | 107.1 |
| M-3 | A-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4815 / 0.282 / 108.9 | 4815 / 0.080 / 108.1 | 0.015 / 0.126 | 0.186 | 0.673 |
| M-3 | B-dbg-1 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26860 / 0.214 / 19.025 | 27479 / 0.014 / 18.253 | 0.009 / 0.046 | 0.192 | 0.726 |
| M-3 | B-dbg-1 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20297 / 0.266 / 14.289 | 20296 / 0.017 / 14.086 | 0.031 / 0.157 | 0.218 | 0.046 |
| M-3 | B-dbg-1 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6611 / 0.090 / 7.071 | 1887536 / 0.001 / 0.008 | 0.003 / 0.013 | 0.086 | 7.051 |
| M-3 | B-dbg-1 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5194 / 0.116 / 6.977 | 5194 / 0.009 / 4.797 | 0.015 / 0.054 | 0.093 | 2.126 |
| M-3 | B-dbg-2 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26834 / 0.206 / 12.202 | 27451 / 0.020 / 10.813 | 0.007 / 0.045 | 0.179 | 1.345 |
| M-3 | B-dbg-2 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20324 / 0.263 / 9.561 | 20322 / 0.028 / 8.236 | 0.030 / 0.135 | 0.205 | 1.190 |
| M-3 | B-dbg-2 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6669 / 0.096 / 7.011 | 1884886 / 0.001 / 0.008 | 0.003 / 0.012 | 0.092 | 6.991 |
| M-3 | B-dbg-2 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5133 / 0.123 / 6.958 | 5133 / 0.011 / 4.839 | 0.015 / 0.052 | 0.098 | 2.066 |
| M-3 | A-dbg-f256 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 25610 / 0.197 / 99.101 | 27805 / 0.030 / 95.690 | 0.002 / 0.079 | 0.165 | 3.332 |
| M-3 | A-dbg-f256 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 18393 / 0.241 / 99.147 | 18392 / 0.030 / 97.522 | 0.013 / 0.128 | 0.197 | 1.498 |
| M-3 | A-dbg-f256 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6240 / 0.178 / 99.051 | 1334518 / 0.001 / 0.009 | 0.003 / 0.009 | 0.174 | 99.032 |
| M-3 | A-dbg-f256 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 4793 / 0.208 / 110.9 | 4796 / 0.038 / 109.3 | 0.017 / 0.143 | 0.153 | 1.529 |
| M-3 | B-dbg-f256 | steady | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck` | 26844 / 0.200 / 15.375 | 27482 / 0.011 / 14.579 | 0.008 / 0.043 | 0.181 | 0.754 |
| M-3 | B-dbg-f256 | steady | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 20289 / 0.242 / 15.688 | 20288 / 0.010 / 15.192 | 0.032 / 0.124 | 0.200 | 0.372 |
| M-3 | B-dbg-f256 | e | CHECK_USER_CONNECTION ↔ `inbound:GameConnectionCheck`（灌包连接的作业占多数，不可用） | 6480 / 0.085 / 6.838 | 1869695 / 0.001 / 0.007 | 0.003 / 0.012 | 0.081 | 6.819 |
| M-3 | B-dbg-f256 | e | SET_USER_POSITION ↔ `inbound:GameSetUserPosition` | 5318 / 0.112 / 6.236 | 5318 / 0.007 / 0.528 | 0.015 / 0.052 | 0.090 | 5.656 |
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

- **p50**：作业之后一段，稳态窗 M-3 为 0.165～0.270 ms，M-2 为 0.171～0.248 ms，持平。
- **p99**：构成 A 稳态窗的这一段 1.35～3.33 ms，参考端到端 p99 为 89.1～99.1 ms，差不多全部是入队等待（同 M-2）。
- **构成 B 的 (e) 窗 SET_USER_POSITION**：这一段的 p99 估计窗口 32 为 2.07～2.13 ms（M-2 5.11～5.48 ms），窗口 256 为 5.66 ms（M-2 1.12 ms）。M-2 与 M-3 的量级互换，说明这个估计在两次运行之间不稳定，不能据此说发送端一段变短或变长。

### 6.3 冷启动停顿对应的请求

| 运行与请求 |
|---|
| A-1: CREATE_CHARACTER setup pair00L t=0.46 s judged 1150.5 ms reference 1150.470 ms |
| A-2: CHECK_USER_CONNECTION background solo00 t=2.17 s judged 954.3 ms reference 59.973 ms |
| A-debug-1: SET_USER_POSITION background solo02 t=2.21 s judged 944.8 ms reference 126.114 ms |
| A-debug-2: CHECK_USER_CONNECTION background solo05 t=2.28 s judged 888.4 ms reference 60.595 ms |
| A-f256: SELECT_CHARACTER setup solo02 t=0.45 s judged 1231.1 ms reference 1231.080 ms |
| A-f256-debug: CHECK_USER_CONNECTION background solo03 t=2.40 s judged 801.7 ms reference 66.774 ms |
| B-1: SELECT_CHARACTER setup solo04 t=0.30 s judged 231.9 ms reference 231.892 ms |
| B-2: SELECT_CHARACTER setup solo06 t=0.30 s judged 232.4 ms reference 232.403 ms |
| B-debug-1: SELECT_CHARACTER setup solo03 t=0.29 s judged 234.3 ms reference 234.283 ms |
| B-debug-2: SELECT_CHARACTER setup solo01 t=0.30 s judged 235.9 ms reference 235.931 ms |
| B-f256: SELECT_CHARACTER setup solo01 t=0.30 s judged 232.6 ms reference 232.620 ms |
| B-f256-debug: SELECT_CHARACTER setup solo01 t=0.30 s judged 236.1 ms reference 236.137 ms |
| B-g0: SELECT_CHARACTER setup solo04 t=0.30 s judged 233.1 ms reference 233.100 ms |
| B-g0-2: SELECT_CHARACTER setup solo04 t=0.29 s judged 235.2 ms reference 235.157 ms |
| B-g0-3: SELECT_CHARACTER setup solo04 t=0.29 s judged 234.0 ms reference 234.027 ms |

- 构成 B 的最长停顿都是首次 SELECT_CHARACTER（就绪后 0.29～0.30 s 计划发出），等于首次选角作业的长度（230.3～237.1 ms），同 M-2。
- 构成 A 是就绪后 0.4～2.4 s 内的建角、选角、位置包或栅栏，排在 20 个 agent 同时建号、选角的串行作业之后（同 M-1、M-2）。A-2、A-debug-1、A-debug-2、A-f256-debug 的最长值只在判定序列上（参考序列 60.0～126.1 ms）：那是排在冷启动积压后面的发出滞后。

## 7. (c)(e) 正式数字与归因

- **(c) 稳态入队等待 p99**：构成 A 93.98／89.78 ms（窗口 256 那次 97.53 ms），构成 B 20.97／14.78 ms（19.81 ms）；目标 50 ms，构成 A 未达，构成 B 达标。端到端 p99：构成 A 98.9～107.9 ms，构成 B 15.59～21.48 ms。
- **(e) 窗口 32**：入队等待 p99 都是 0.01 ms（代理值，被灌包连接的轻量作业主导，§10 第 3 条）；max 构成 A 182.2／189.9 ms，构成 B 39.80／39.90 ms。其余连接的端到端 p99：构成 A 96.1～122.8 ms，构成 B 8.94～10.41 ms。
- **(e) 窗口 256**：入队等待 max 构成 A 142.5 ms、构成 B 38.94 ms；其余连接的端到端 p99 构成 A 116.7／109.2 ms，构成 B 8.30／7.63 ms，与窗口 32 同量级。

构成 A 未达 50 ms 的归因（TASK-28 §5.3 的五步）：
1. 不是日志开销：没有窗口的 debug 端到端 p99 超过默认级别的 2 倍（§4.2 末）。
2. 稳态窗执行时长之和最大的是 `inbound:GameSetUserArea`（57.7%～59.6%）与 `inbound:GameSelectCharacter`（31.6%～33.9%），合计约 91%～92%；执行器占用 39.0%～39.1%。
3. 与 M-2 的同一分解相比，份额与占用都没有变（M-2：57.1%～58.4%／33.0%～34.2%，占用 38.9%～39.3%）。四条每日补给命令约占选角作业 p50 的 86%～87%（M-2 86%～88%）。
4. 作业结束之后那一段的 p99 只有 1.35～3.33 ms，约占参考端到端 p99 的 1%～4%（§6.2）。
5. 结论：**执行器上的长作业串行**，与 M-1、M-2 相同（F-78）。M-3 没有新出现的成因。

构成 B 的 (e)：其余连接的 p99 仍约 10 ms（M-1 约 4.5 ms），(e) 窗每个作业的平均占用 4.56～4.63 µs（M-2 4.37～4.46 µs，M-1 2.40～2.47 µs；`../logs/task-32-judgement.txt` 的 floodjob 一节），灌包连接自身速率 7.7 万包/秒（M-2 7.8 万），归 F-120，原样保留。

## 8. 后续性能优化事项

每个未达目标值的项一行，全部登记在 `../follow-ups.md`，状态「未关闭」。

| 量 | M-3 值 | M-2 值 | 目标 | 出处 | 已知或推断的成因 | F 编号 |
|---|---|---|---|---|---|---|
| (b) 构成 A 可持续吞吐 | 2 s 口径 1163 包/秒，稳定通过档 800；p99 ≤ 50 ms 口径无通过档（400 档 p99 106.4 ms） | 1169 包/秒，稳定通过档 800；无通过档（100.7 ms） | ≥ 2000 CMD/秒 | §5.6 | 执行器上的长作业串行（推断） | F-77 |
| (c)(e) 构成 A 稳态与灌包期间 | 入队等待 p99 93.98／89.78 ms；(e) 其余连接端到端 p99 96.1～122.8 ms | 94.3／99.8 ms；106.6～112.8 ms | 入队等待 p99 ≤ 50 ms | §7 | `GameSetUserArea` 与 `GameSelectCharacter` 串行，约占执行时长之和的 91%～92% | F-78 |
| (d) 10 并发登录 | 入队等待 p99 构成 A 136.9／103.3 ms，构成 B 17.32／2.42 ms（窗口 256 那次 53.83 ms）；背景端到端 p99 构成 A 160.9／144.4 ms，构成 B 91.26／92.92 ms | A 100.0／180.9 ms；B 1.0／25.9 ms；背景 A 138.8／205.8 ms、B 84.3／100.1 ms | p99 ≤ 50 ms | §5.1、§5.4 | 构成 A：同上；构成 B：双人组领队自身的离本、进本长作业落在轮内 | F-79 |
| (f) 冷启动 | 构成 A 801.7～1231.1 ms（2 s 线的 0.62 倍、目标的 4.9 倍）；构成 B 231.9～236.1 ms，达标但贴近目标 | A 887.8～1367.6 ms（0.68 倍、5.5 倍）；B 229.2～247.4 ms | ≤ 250 ms | §5.1、§6.3 | 构成 A：20 个 agent 同时建号、选角，串行积压；构成 B：首次选角的首条回包推迟到作业结束（VC-1） | F-80 |
| 玩法 CMD 单条 | `GameSelectCharacter` 构成 A p99 128.8～140.8 ms，p99.9（即 max）234.0～269.3 ms；**max 目标在 A-dbg-1 一次未达**（269.3 ms）；全部作业合计 p99.9 构成 A 26.8～27.5 ms | p99 130.0～142.6 ms，max 226.8～239.1 ms（max 目标达到） | p99.9 ≤ 100 ms、max ≤ 250 ms | §5.4 | 四条每日补给命令约占选角作业 p50 的 86%～87%；269.3 ms 那一次与一次外部 CPU 尖峰同秒（推断，§2） | F-81 |
| 合成负载覆盖不到的项 | M-3 同样未覆盖；`event:` 作业类型与 M-2 相同，各类 max ≤ 12.22 ms | 同 | — | §10 | 负载 `GmWeb__Enabled=false`、5 分钟不跨日切、动作集不变 | F-82 |
| 灌包连接自身速率与构成 B 的 (e) | 灌包连接自身速率：构成 B 7.7 万包/秒，构成 A 5.7～5.8 万；构成 B 其余连接 (e) 端到端 p99 G1 9.96／10.41 ms，G0 三次 8.47～14.84 ms（中位数 9.84 ms，约 M-1 的 2 倍） | 7.8 万；5.7～6.0 万；G1 9.77／11.20 ms，G0 一次 17.84 ms | (e) 入队等待 p99 ≤ 50 ms（已达） | §5.3、§7 | (e) 窗每个作业的平均占用 4.56～4.63 µs（M-1 2.40～2.47 µs）；推断成因同 M-2：每个作业结束时的发件箱冲刷与发送端交付，未剖析 | F-120 |
| 收尾窗口内 Warning 形态的 `Failed` | 4 次运行各 9 对，共 72 行，全是 `Writing is not allowed after writer was completed`；按 T 不计 | M-2 ramp-A-2s 12 对、A-dbg-f256 6 对，同一种异常（本单元回归核对时用新脚本重读 M-2 的 host.log 确认） | —（日志级别，不是性能） | §4.2 | 读码推断：发送端判定时连接仍可解析，而 TouchSocket 的写端已完成；F-121 的「连接已不在」判定不覆盖这种形态 | F-127（新） |

## 9. 对子 Spec B 最终测量的交代

- **命令行与 dll**：沿用本单元的命令行（`../logs/task-32-runs.txt`）与 G1 dll（`raw/m3-tool-version.txt`）。默认级别组、debug 组、窗口 256 与爬坡都用 G1。控制运行（三次 G0）**未证明**两个 dll 在默认级别下等价（§5.3）：三次 G0 落在两次 G1 的两侧，方向不一致。与基线对照时 (e) 同时给 G1 与 G0 两种口径；若要判定等价，两次 G1 的差距太窄是主要障碍，可考虑每种 dll 各跑三次以上再比范围（只是可选手段）。
- **S5 第 5 项的归类规则**：以本页 §4.2 与 `../t009-logs-and-cases.md` §4 为准；执行它的脚本 `classify.py` 在 scratchpad `t32/`，全文附在 `../logs/task-32-judgement.txt` 末尾。规则按连接行为识别，不依赖 host.log 行号，可以直接复用；复用前先在本单元的 18 份 host.log 上跑一次回归（预期：P 20 行、T 106 行、计入 0 行）。
- **F-126**：M-3 没有出现，已关闭；子 Spec B 的最终测量沿用同一判读（工具先断开的连接上出现 `SocketException` 形态的 Warning `Failed` 时记录并回报、不计）。
- **F-127**：收尾窗口内 `Writing is not allowed after writer was completed` 形态的 Warning `Failed` 按 T 不计；若它出现在收尾窗口之外，按规则计入 S5。
- **覆盖缺口**：F-82 原样保留，M-3 同样没有覆盖。

## 10. 缺口与局限

1. 合成负载，不是 A21 录制。选角频率（约每秒 1.6 次）远高于真实玩家，构成 A 的数字因此偏悲观。
2. 工具与 Host 同机运行，抢同一组 CPU；本机 swap 已用约 9.5～10 GB，同机还有其他应用与 agent 会话，并有一个每 10 分钟 1～2 s 的外部 CPU 尖峰（§2），P1 的阈值（连续 3 s）不会把它判为污染。
3. (e) 的作业行不带连接号，灌包连接自己的作业分不出来；(e) 窗的入队等待被大量空队列上的轻量作业主导，p99 不代表其他连接。
4. 作业层的窗口按工具读到日志行的时刻归窗；debug 级下日志行在高负载时可能晚到。本次对照未见 2 倍以上的差异。
5. 成因的份额（每日补给规则读取、事务提交、当前视图重建各占多少）没有测量，是读代码的推断。
6. 延迟构成只能分布相减估计：发送端的交付时刻是 Trace 级，不打开，工具也不解析（§6.2）。
7. 收尾窗口起点约有 1 s 的不确定性。本次收尾行都在起点之后 0.007～0.231 s，起点前 1 s 内 0 行。
8. 爬坡不生成 summary.json：就绪时刻取「第一行 ramp 输出 − 50 s」；ramp-A-2s 没有解析自检。
9. **P2 只是必要条件**：「探测连接没有发过入站帧」来自工具源码（`HostProcess.cs` 的 `WaitForBootstrapAsync`），host.log 上能检验的只是「没有任何非发送端层的行提到它」。
10. **(d) 轮按轮时间窗判定**，不是按单条连接的断开时刻：默认级别下服务端连接号与工具的登录号对不上（§4.2）。本次 D 类 0 行，这条局限没有实际影响。G0 运行没有 `originUtc`，轮时间窗的原点取工具「host ready」那一行的时刻，误差为几毫秒。
11. 合成负载不超过 21 个在线角色，观测不到人群项的上界问题（F-113、F-114、F-116、F-117 与 M-3 无关，不改）。
