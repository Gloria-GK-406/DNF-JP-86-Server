# 基线测量汇总（TASK-03，合成负载）

> **合成负载**：没有 A21 真实录制，负载由 `Tool/GameplayLoad` 按 TASK-01 的线上序列合成。口径与命令行见 [README](README.md)。

测量日期 2026-10-21；HEAD `53c72543`（`Server/src` 与基线 `a02bce46` 无差异）；Release 构建；
Apple M5（10 核）、32 GiB、macOS 27.0.1、内置 SSD（APFS）；SQLite 3.53.4，`journal_mode=wal`，
`synchronous=2`（FULL，取法见 README）。工具与 Host 同机。

## 两种负载构成

基线在 RQ-090 要求的完整构成下出现了一个**既有缺陷**（见下文「SQLite 30 秒停顿」），整个稳态窗几乎都卡在停顿里，
实际包速率只有目标的约 14%。所以这里给两种构成，**改造后的 M-1/M-2 两种都要重跑**：

| 构成 | 命令行差异 | 稳态窗里有什么 | 用途 |
|---|---|---|---|
| **A 完整**（`baseline-1.md`、`baseline-2.md`） | 默认 | 栅栏、移动、跨区 0x0024、回选角+再选角、回选角+建角+选新角、双人组组队/进本/离本/离队；20 个 agent 在 Host 就绪时同时建号选角 | RQ-090 要求的构成（含选角、建角、进本） |
| **B 无门外写触发**（`baseline-clean-1.md`、`baseline-clean-2.md`） | `--weights 0.5,0.5,0,0,0 --entry-concurrency 1` | 栅栏、移动、双人组组队/进本/离本/离队；建号选角只在开头，逐个进行 | 绕开该缺陷后的基线对照数字；选角与建角只出现在冷启动窗 |

B 不满足「稳态窗必含选角、建角」；它存在的理由只是让 (b)(c)(d)(e) 在基线上有一个不被缺陷吞掉的对照值。

## 主要数字（两次运行并列）

分布格式：样本 / p50 / p99 / p99.9 / max，单位 ms，端到端（发出 → 第一条回包），只计有回包的请求。

| 量 | A-1 | A-2 | B-1 | B-2 |
|---|---:|---:|---:|---:|
| 稳态窗实际包速率（包/秒，目标 400） | 53.38 | 57.48 | 399.9 | 399.9 |
| 稳态窗全部有回包请求 | 6189 / 0.21 / 32.78 / 30270.2 / 31064.3 | 6690 / 0.25 / 29.62 / 29868.1 / 31856.5 | 47378 / 0.19 / 1.03 / 27.89 / 518.1 | 47395 / 0.20 / 1.04 / 27.39 / 45.05 |
| 稳态 CHECK_USER_CONNECTION | 3729 / 0.19 / 3.51 / 7.20 / 7.74 | 4158 / 0.22 / 4.85 / 7.84 / 9.50 | 26838 / 0.18 / 0.72 / 3.37 / 10.14 | 26952 / 0.19 / 0.76 / 3.89 / 19.11 |
| 稳态 SET_USER_POSITION | 2311 / 0.23 / 11.90 / 20.39 / 20.54 | 2402 / 0.30 / 6.22 / 31.41 / 46.41 | 20292 / 0.20 / 1.28 / 26.99 / 38.02 | 20196 / 0.21 / 1.25 / 28.79 / 45.05 |
| 稳态 SELECT_CHARACTER | 19 / 30262.1 / 31064.3 / 31064.3 / 31064.3 | 16 / 312.9 / 31856.5 / 31856.5 / 31856.5 | 无样本 | 无样本 |
| 稳态 SELECT_DUNGEON | 7 / 17592.6 / 20946.2 / 20946.2 / 20946.2 | 8 / 15232.0 / 26560.3 / 26560.3 / 26560.3 | 35 / 15.52 / 518.1 / 518.1 / 518.1 | 35 / 15.77 / 26.71 / 26.71 / 26.71 |
| (f) 冷启动前 60 s 最长单次停顿 | 30574.5 | 30944.4 | 573.3 | 567.5 |
| (f) 冷启动窗整体 | 1859 / 0.18 / 765.8 / 30494.8 / 30574.5 | 2052 / 0.38 / 668.1 / 30807.1 / 30944.4 | 22239 / 0.18 / 3.39 / 24.74 / 573.3 | 22247 / 0.20 / 3.56 / 28.77 / 567.5 |
| (d) 10 并发登录轮内的背景请求 | 3131 / 0.20 / 895.4 / 28826.4 / 56854.1 | 2958 / 0.20 / 825.1 / 29385.7 / 55451.9 | 3063 / 0.09 / 0.20 / 12.46 / 643.6 | 3108 / 0.09 / 0.20 / 1.58 / 217.6 |
| (e) 灌包期间其余连接 | 1757 / 0.08 / 30.79 / 29047.7 / 29368.8 | 1206 / 0.09 / 88.05 / 29378.0 / 29877.4 | 11850 / 0.07 / 0.57 / 18.04 / 33.58 | 11857 / 0.08 / 0.57 / 15.34 / 29.28 |
| (e) 灌包连接自身应答速率（包/秒） | 199444 | 200348 | 196996 | 197656 |
| 命令超阈值项（p99.9 > 100 或 max > 250 ms） | 23 | 22 | 6 | 6 |
| 失败的命令 | 83 | 75 | 0 | 0 |
| 端到端 > 250 ms 的请求 | 175 | 159 | 5 | 2 |
| 错误（断线/超时） | 37 | 36 | 0 | 0 |
| 双人组进本次数 | 12 | 13 | 80 | 80 |

B 的 (f) 最长停顿都是 t≈0.3 s 的第一个 SELECT_CHARACTER（冷进程第一次选角）。
B-1 稳态 max 518.1 ms 是一次 SELECT_DUNGEON；B-2 没有这样的离群值。
(e) 的灌包走的是最轻的路径（0x04DD 不经命令总线、无扇出），每秒约 20 万个应答，所以它衡量的是「一个连接能抢走多少」，
不是命令吞吐。

### 工具自身的抖动

同一构成、同一种子连续两次（B 是不受缺陷干扰的那组，用它判断工具的抖动）：

| 量 | B-1 | B-2 | 差 |
|---|---:|---:|---:|
| 稳态 p50 / p99 / p99.9 | 0.19 / 1.03 / 27.89 | 0.20 / 1.04 / 27.39 | ≤ 0.01 / 0.01 / 0.50 ms |
| 稳态 CHECK_USER_CONNECTION p99 | 0.72 | 0.76 | 0.04 ms |
| (f) 最长停顿 | 573.3 | 567.5 | 5.8 ms（1%） |
| (d) 轮内 p99 | 0.20 | 0.20 | 0 |
| (e) 其余连接 p99 | 0.57 | 0.57 | 0 |
| 爬坡每档实际速率 / 目标 | 见 `ramp-clean.md`、`ramp-clean-high.md` | — | 400–6000 档 ≥ 99.2%；续跑到 25000 档 ≥ 96.7% |

p50/p99 的两次差在 0.05 ms 以内，p99.9 差约 2%，单点 max 不稳定（518.1 对 45.05、643.6 对 217.6），
所以**比较时以 p99 和 p99.9 为准，max 只作离群提示**。A 的两次差得多（稳态 p99 32.8 对 29.6、(e) p99 30.8 对 88.1），
那是缺陷造成的停顿落在哪一秒不同，不是工具抖动。

## 可持续吞吐（b）

| 构成 | 结果 | 记录 |
|---|---|---|
| A 完整 | **没有通过档**：400 包/秒这一档就因一次 30 s 停顿导致的断线而停止，该档实际 4 包/秒 | `ramp-full.md` |
| B | 400 → 6000 包/秒全部通过；续跑 6000 → 25000 也全部通过，**≥ 24177 包/秒，未触顶**（p99 ≤ 1.41 ms） | `ramp-clean.md`、`ramp-clean-high.md` |

B 的上限没有测到：每条连接闭环，20 条连接在 25000 包/秒时每条每 0.8 ms 一个包，再往上主要受工具一侧限制。
而且高档位时 B 的构成 99.9% 以上是轻量包（双人组的进本循环按停留时间推进，不随速率增加），
**这个数衡量的是基线的多线程并行吞吐，不是长命令的吞吐**。改造后执行器是单线程，(b) 的达标判断要用同一构成重跑后看实测值，
不能拿基线的 24177 当作对照的「应达到」值。

## 单请求出站字节（RQ-036 下限依据）

精确值（`outbound.md`：空闲 Host、只有涉及的连接、TASK-01 的序列）：

| 请求 | 请求者 | 同伴 |
|---|---:|---:|
| SELECT_CHARACTER 首次选角 | 39285 B（195 帧） | — |
| SELECT_CHARACTER 回选角后再选 | 39227 B（193 帧） | 0（同伴在 1:1，选角者落在城门区） |
| SELECT_CHARACTER 第二个角色首次选角 | **39376 B**（195 帧） | — |
| RETURN_SELECT_CHARACTER（组队中） | 159 B | 176 B |
| SELECT_DUNGEON 144（组队进本） | 1367 B（10 帧） | 1367 B（10 帧） |
| ENTER_SELECT_DUNGEON | 608 B | 608 B |
| EPLP 离本 | 521 B | 521 B |
| RESPONSE_PEER | 212 B | 214 B |
| FINISH_LOADING | 36 B | 0 |

负载下按「请求类型 × 角色」的最大窗口（含他人投影的上界）：SELECT_CHARACTER 请求者 39427 B（A）/ 39291 B（B）；
EPLP 3646 B（B，同伴），SET_USER_AREA 4407 B（A，请求者）——这两项随 1:1 区域里的人数增长（回城与跨区会下发区域内玩家列表），
是该命令自己的发件箱，不是混入的他人投影。A 里 RETURN_SELECT_CHARACTER 33244 / 36530 B、REQUEST_PEER 19310 B 这几项
出自持续 30 s 的停顿窗口，窗口里收到的是别的玩家的城镇投影，不是这条命令的发件箱。

**结论**：单条 CMD 对单个连接的最大出站字节是选角全序列，精确值 **39376 B**，负载下观察到的最大 39427 B。
RQ-036 的下限按 2 倍取不小于 **78854 B**（按负载观察值）。组队进本最大的是 SELECT_DUNGEON，请求者与同伴各 1367 B。
选角首个角色的 NOTI 0x0015 浮动帧由事件循环发出（TASK-01），不一定落在窗口内，这个上限未必包含它（25 B 级别）。

## SQLite 30 秒停顿（既有缺陷，A 构成的主导现象）

现象（两次 A 都出现，B 两次都没有）：

1. 某个 Transactional 命令在第一次写入时得到 `SQLite Error 5: 'database is locked'`，**等满约 30 s 才失败**——
   `Microsoft.Data.Sqlite` 默认 `Default Timeout` 30 s 内重试 BUSY（AGENTS.md「构建与运行」已记录这一形状）。
   失败的命令以选角各阶段（`PrepareSelectedCharacterState`、`ApplySelectedCharacterDailyRefill`）、各 `RefillOnline*Ticket`、
   `ExpireCharacterNameTag` 为主。
2. 它在等待期间持有 `TransactionalExecutionGate`（`Server/src/Dfo.Server.Application/Commands/Pipeline/TransactionalExecutionBehavior.cs`，
   全进程只有一个许可的信号量）。于是**所有 Transactional 命令**（选角、建角、登录取票、注册、进本，以及在
   `DungeonLifecycleDomainService` 里取这把门的 `LeaveDungeonCommand`）一起排队 30 s，排队者自己再撞上 BUSY 时变成 60 s 以上。
   选角失败会补偿并断线，agent 重连又是一次选角，于是停顿一轮接一轮。
3. 不经事务门的轻量请求不受影响：A 稳态 CHECK_USER_CONNECTION p99 3.5 ms、SET_USER_POSITION p99 11.9 ms。

成因（**代码读出，未逐一用实验隔离**）：事务门只管经命令管线的 Transactional 命令，门外还有直接开事务写 SQLite 的入口。
它们在门内事务「先读后写」之间提交，后者的写入就会得到 BUSY。读到的门外写入口：

- `SetUserAreaHandler`（`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownProjectionSlice.cs:222`，handler 直接处理，不是命令）
  每次跨区调用 `ISelectedSessionPositionPersistence.PersistAsync`，没有环境事务时走
  `Server/src/Dfo.Server.Application/Characters/CharacterStateAccess.cs:159` 的 `AloneAsync`，自开 `DomainTransaction` 并提交，不取门；
- 会话拆除（回选角的 `CompleteReturnToSelectionCommand` 是 Workflow、断线清理不经命令总线）落位置走同一个 `AloneAsync`
  （`Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:109-115`）；
- `Server/src/Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs:274` 在没有环境事务时同样自开事务提交。

对照实验（各一次，短时长，未入库）：只关跨区（`--weights 0.5,0.488,0,0.01,0.002`）仍有停顿；关回选角、保留跨区并逐个进入
（`--weights 0.47,0.45,0.08,0,0 --entry-concurrency 1`）仍有停顿；两者都关并逐个进入（即 B）没有。
所以**门外写入口不止一个**，上面列出的是读到的，不保证完整。

对本重构的含义：单执行器之后这些写都在执行器上串行，按设计这个停顿应当消失；M-1/M-2 用 A 构成重跑时，
「失败的命令」「错误」「端到端 > 250 ms 的请求」三项应当接近 B 的水平。若仍出现 30 s 级停顿，说明还有执行器之外的写入口
（RQ-061 的判据应当先抓到它）。

## 与 RQ-091 的对照（基线预判）

| 项 | 阈值 | A（两次） | B（两次） |
|---|---|---|---|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 无通过档 | ≥ 24177 包/秒（未触顶，几乎全是轻量包） |
| (c) 400/秒下入队等待 p99 | ≤ 50 ms | 基线不存在；端到端 p99 32.78 / 29.62 ms（实际只有 53 / 57 包/秒） | 基线不存在；端到端 p99 1.03 / 1.04 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在；端到端 895.4 / 825.1 ms | 基线不存在；端到端 0.20 / 0.20 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在；端到端 30.79 / 88.05 ms | 基线不存在；端到端 0.57 / 0.57 ms |
| 单条命令 p99.9 ≤ 100、max ≤ 250 ms | — | 23 / 22 项超出（30–89 s 级） | 6 / 6 项超出（见下） |
| (f) 最长单次停顿 | ≤ 250 ms | 30574.5 / 30944.4 ms | 573.3 / 567.5 ms |

### M-1 预警清单（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）

**B 构成（两次都出现，不受停顿缺陷干扰）**，数字为 B-1 / B-2 的 max（这些命令样本少，p99.9 即 max）：

| 命令 | max (ms) | 出现在 | 说明 |
|---|---:|---|---|
| `RegisterAccountCommand` | 671.5 / 670.7 | (d) 登录轮 | 口令哈希在命令里（`AuthenticationDomainService.cs:21`），在事务门内串行；单个约 64 ms，10 个并发排成约 640 ms。HTTP CMD，RQ-007 要求哈希移到执行器外 |
| `AuthenticateAndIssueTicketCommand` | 640.2 / 642.4 | (d) 登录轮 | 同上（`AuthenticationDomainService.cs:37` 校验） |
| `RedeemGameLoginCommand` | 575.7 / 574.5 | (d) 登录轮 | 本身不哈希，是排在门后等前面的哈希 |
| `EnterDungeonCommand` | 639.4 / 277.3 | (d) 登录轮 | 同上，排在门后 |
| `PrepareSelectedCharacterStateCommand` | 497.2 / 492.9 | t≈0.8 s，冷进程第一次选角 | 首次触达的内容加载；冷启动窗之后 B 里没有再出现 |
| `AdvanceCreatureClockCommand` | 348.6 / 330.0 | (d) 登录轮 | 后台定时命令，排在门后 |

即：B 下超阈值的只有两类——**登录哈希占住全局事务门**（RQ-007 的问题）和**冷进程首次选角**（RQ-091 的启动预热问题）。
冷启动窗之后、登录轮之外，B 没有超阈值的命令。

**A 构成**：上表 6 项在 A 里同样超阈值，但 max 变成 30–75 s（停顿）；此外还有 A-1 17 项、A-2 16 项只在 A 出现：
`PrepareSelectedCharacterSession`、`ApplySelectedCharacterDailyRefill`、`CleanupExpiredCharacterItems`、`EnsureDailyContractPuppets`、
`InitializeHousing`、`ReviveSelectedCharacterPet`、`SynchronizeSelectedCharacterQuests`、`RefillOnlineBossTower/Southern/TauTicket`（3 项）、
`CreateCharacter`、`PrepareReturnToSelection`、`ExpireCharacterNameTag`、`AdvanceWeaknessRecovery`、
`LeaveDungeon`（Direct，max 88 s，在 `DungeonLifecycleDomainService` 里取同一把门）——以上 15 项 max 都在 30 s 以上，
例外是 A-1 的 `SynchronizeSelectedCharacterQuests`（845 ms）与 A-2 的 `CreateCharacter`（688 ms）；以及 `CompleteReturnToSelection`（458 / 603 ms）和 `CompensateSelectedCharacterSession`
（152 ms，仅 A-1）。15 + 2 = 17（A-1）；A-2 没有 `CompensateSelectedCharacterSession`，为 16。逐项数字见两份 baseline 记录。

## 缺口

1. **A 构成在基线上测不出有意义的 (b)(c)(d)(e)**：既有的 30 s 停顿让实际速率只有 53–57 包/秒，爬坡第一档即停止。
   这里用 B 补了一个对照值，但 B 的稳态窗没有选角和建角。
2. **(b) 在 B 上未触顶**：≥ 24177 包/秒受工具闭环限制，且几乎全是轻量包。
3. **入队等待**在基线上不存在（RQ-090），没有替代数字。
4. 基线上 0x0023、0x0024、0x04DD 等由 handler 直接处理，没有命令日志，只有端到端数字。
5. `synchronous` 取自与服务端连接同条件的新连接（同一原生库，SHA-256 一致），不是从 Host 进程内部读出的。
6. 死亡之塔不在负载里（需要 50 级与道具，线上做不到，TASK-01 已说明）。
