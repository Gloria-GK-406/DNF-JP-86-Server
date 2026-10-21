# 基线测量 — c1（合成负载）

> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。

## 环境

- 机器：Apple M5，10 逻辑核，内存 32 GiB，macOS 27.0.1
- 磁盘（数据库所在卷 `/`，临时目录 `/var/folders/qc/sl_p_wxs2qvg3pn_zknp1ppw0000gn/T/`）：设备 /dev/disk3s5，文件系统 APFS，Internal，Apple Fabric，Solid State: Yes（`diskutil info /`：Solid State:               Yes）
- .NET 运行时（工具进程）：.NET 10.0.10
- Host：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，构建配置 Release，Host.dll 修改时间 2026-10-21T12:33:02Z
- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），额外设置 `Logging__Console__FormatterOptions__SingleLine=true`
- SQLite：版本 3.53.4；`PRAGMA journal_mode` = **wal**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；`PRAGMA synchronous` = **2**（FULL；连接级，取值见下）
  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：
  - Host 原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1397 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48000 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26921 | 56.09% |
| SET_USER_POSITION | 20215 | 42.11% |
| STEP_FENCE | 576 | 1.20% |
| FINISH_LOADING | 72 | 0.15% |
| EPLP | 36 | 0.07% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26921 | 0.24 | 1.68 | 2.49 | 6.46 | 42.79 | 0 | 0.27 | 2.79 | 30.82 |
| SET_USER_POSITION | 20215 | 0.30 | 1.92 | 2.89 | 25.93 | 55.98 | 0 | 0.58 | 25.64 | 44.16 |
| FINISH_LOADING | 72** | 0.13 | 0.22 | 0.27 | 0.27 | 0.27 | 0 | 0.27 | 0.27 | 0.27 |
| EPLP | 36** | 17.18 | 21.02 | 22.02 | 22.02 | 22.02 | 0 | 22.02 | 22.02 | 22.02 |
| LEAVE_PARTY | 36** | 0.71 | 1.19 | 3.38 | 3.38 | 3.38 | 0 | 3.38 | 3.38 | 3.38 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.31 | 0.56 | 0.72 | 0.72 | 0.72 | 0 | 0.72 | 0.72 | 0.72 |
| ENTER_SELECT_DUNGEON | 36** | 24.86 | 40.54 | 42.56 | 42.56 | 42.56 | 0 | 42.56 | 42.56 | 42.56 |
| SELECT_DUNGEON | 36** | 16.68 | 20.49 | 267.5 | 267.5 | 267.5 | 0 | 267.5 | 267.5 | 267.5 |
| **全部计入的请求** | 47388 | 0.27 | 1.81 | 2.79 | 24.51 | 267.5 | 36 | 0.48 | 24.11 | 267.5 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47424 | 0.05 | 1.55 | 2.34 | 5.72 | 17.80（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| LeaveDungeonCommand | Direct | 78** | 1.68 | 19.35 | 1019.0 | 1019.0 | 1019.0 | 223.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 279.6 | 657.9 | 665.7 | 665.7 | 665.7 | 232.7 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 628.1 | 635.6 | 637.5 | 637.5 | 637.5 | 233.0 | 0 |
| EnterDungeonCommand | Transactional | 79** | 9.64 | 262.1 | 633.1 | 633.1 | 633.1 | 232.9 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 187.8 | 562.3 | 570.2 | 570.2 | 570.2 | 233.3 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 3.03 | 3.82 | 502.8 | 502.8 | 502.8 | 0.8 | 0 |
| AdvanceCreatureClockCommand | Transactional | 335* | 0.13 | 0.44 | 46.25 | 346.0 | 346.0 | 233.3 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.14 | 2.13 | 66.95 | 66.95 | 66.95 | 0.2 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 19.35 | 32.13 | 41.68 | 41.68 | 41.68 | 2.5 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.60 | 22.92 | 41.26 | 41.26 | 41.26 | 0.3 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 20.14 | 39.39 | 39.64 | 39.64 | 39.64 | 3.2 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.64 | 34.11 | 36.19 | 36.19 | 36.19 | 1.4 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.29 | 29.40 | 35.12 | 35.12 | 35.12 | 1.8 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.52 | 1.39 | 7.66 | 7.66 | 7.66 | 5.3 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.36 | 0.87 | 6.01 | 6.01 | 6.01 | 0.3 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.27 | 0.46 | 6.00 | 6.00 | 6.00 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 2.19 | 2.76 | 5.95 | 5.95 | 5.95 | 107.2 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.03 | 0.22 | 5.55 | 5.55 | 5.55 | 9.1 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.08 | 0.11 | 4.86 | 4.86 | 0.2 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.25 | 3.88 | 3.88 | 3.88 | 0.9 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.06 | 0.11 | 3.15 | 3.15 | 3.15 | 0.9 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.09 | 2.80 | 2.80 | 2.80 | 2.80 | 60.0 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.49 | 1.12 | 2.73 | 2.73 | 2.73 | 0.3 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.65 | 2.47 | 2.53 | 2.53 | 2.53 | 4.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 84** | 0.04 | 0.33 | 1.56 | 1.56 | 1.56 | 53.5 | 0 |
| LeavePartyCommand | Direct | 78** | 0.05 | 0.08 | 1.22 | 1.22 | 1.22 | 22.9 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.08 | 0.23 | 0.84 | 0.84 | 0.84 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.32 | 0.47 | 0.69 | 0.69 | 0.69 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.02 | 0.28 | 0.61 | 0.61 | 13.7 | 0 |

命令日志行 1747 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**575.9 ms**（SELECT_CHARACTER，计划发出 t=0.26 s，solo05；参考口径 575.9 ms）。冷启动窗整体：22347 | 0.27 | 1.94 | 4.07 | 26.46 | 575.9（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 4 | 575.9 | 575.9 | SELECT_CHARACTER |
| 1 | 75 | 8.91 | 8.91 | SET_USER_POSITION |
| 2 | 151 | 9.11 | 28.80 | SELECT_CHARACTER |
| 3 | 172 | 7.07 | 7.75 | SET_USER_POSITION |
| 4 | 241 | 20.10 | 30.74 | SET_USER_AREA |
| 5 | 390 | 23.51 | 28.18 | SET_USER_AREA |
| 6 | 400 | 2.28 | 2.71 | SET_USER_POSITION |
| 7 | 400 | 2.53 | 5.85 | SET_USER_POSITION |
| 8 | 400 | 2.20 | 2.78 | SET_USER_POSITION |
| 9 | 385 | 2.51 | 12.28 | RESPONSE_PEER |
| 10 | 390 | 2.49 | 2.88 | SET_USER_POSITION |
| 11 | 395 | 2.44 | 2.56 | CHECK_USER_CONNECTION |
| 12 | 400 | 2.34 | 2.71 | CHECK_USER_CONNECTION |
| 13 | 392 | 4.00 | 88.00 | SELECT_DUNGEON |
| 14 | 384 | 16.31 | 40.46 | ENTER_SELECT_DUNGEON |
| 15 | 403 | 2.85 | 27.40 | ENTER_SELECT_DUNGEON |
| 16 | 373 | 2.32 | 13.13 | SELECT_DUNGEON |
| 17 | 400 | 2.43 | 2.62 | CHECK_USER_CONNECTION |
| 18 | 398 | 2.83 | 48.89 | EPLP |
| 19 | 396 | 10.54 | 14.84 | EPLP |
| 20 | 400 | 2.51 | 2.75 | SET_USER_POSITION |
| 21 | 394 | 37.08 | 40.85 | SET_USER_POSITION |
| 22 | 400 | 2.69 | 17.26 | LEAVE_PARTY |
| 23 | 398 | 2.31 | 2.61 | SET_USER_POSITION |
| 24 | 398 | 2.49 | 2.90 | SET_USER_POSITION |
| 25 | 398 | 2.63 | 4.94 | SET_USER_POSITION |
| 26 | 396 | 2.70 | 4.72 | CHECK_USER_CONNECTION |
| 27 | 393 | 5.94 | 13.32 | SET_USER_POSITION |
| 28 | 395 | 2.57 | 4.06 | SET_USER_POSITION |
| 29 | 400 | 2.36 | 2.64 | SET_USER_POSITION |
| 30 | 389 | 2.75 | 2.90 | CHECK_USER_CONNECTION |
| 31 | 396 | 2.26 | 2.77 | CHECK_USER_CONNECTION |
| 32 | 387 | 15.59 | 38.33 | ENTER_SELECT_DUNGEON |
| 33 | 400 | 2.47 | 2.93 | SET_USER_POSITION |
| 34 | 392 | 2.76 | 32.68 | ENTER_SELECT_DUNGEON |
| 35 | 396 | 4.26 | 30.21 | ENTER_SELECT_DUNGEON |
| 36 | 376 | 15.89 | 24.56 | ENTER_SELECT_DUNGEON |
| 37 | 396 | 12.08 | 18.61 | SET_USER_POSITION |
| 38 | 396 | 5.34 | 18.30 | SET_USER_POSITION |
| 39 | 400 | 2.30 | 2.72 | SET_USER_POSITION |
| 40 | 394 | 21.03 | 26.46 | SET_USER_POSITION |
| 41 | 398 | 11.79 | 32.40 | SET_USER_POSITION |
| 42 | 398 | 2.58 | 4.74 | CHECK_USER_CONNECTION |
| 43 | 398 | 2.64 | 2.91 | SET_USER_POSITION |
| 44 | 400 | 2.25 | 2.96 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.44 | 3.98 | SET_USER_POSITION |
| 46 | 395 | 2.59 | 2.93 | CHECK_USER_CONNECTION |
| 47 | 399 | 3.25 | 7.93 | CHECK_USER_CONNECTION |
| 48 | 390 | 2.54 | 2.61 | CHECK_USER_CONNECTION |
| 49 | 400 | 2.31 | 2.93 | SET_USER_POSITION |
| 50 | 395 | 2.39 | 2.75 | SET_USER_POSITION |
| 51 | 390 | 2.58 | 2.83 | CHECK_USER_CONNECTION |
| 52 | 395 | 2.62 | 6.59 | SET_USER_POSITION |
| 53 | 384 | 14.66 | 37.75 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 2.42 | 2.70 | CHECK_USER_CONNECTION |
| 55 | 397 | 13.77 | 31.59 | ENTER_SELECT_DUNGEON |
| 56 | 387 | 2.48 | 2.62 | CHECK_USER_CONNECTION |
| 57 | 380 | 15.60 | 23.24 | ENTER_SELECT_DUNGEON |
| 58 | 400 | 2.34 | 2.49 | SET_USER_POSITION |
| 59 | 400 | 2.73 | 3.48 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47388 | 0.27 | 1.81 | 2.79 | 24.51 | 267.5 |
| 10 并发登录的轮内 | 3081 | 0.17 | 1.94 | 208.5 | 871.0 | 1025.1 |
| （参考：实际发出口径）稳态窗 | 47388 | 0.18 | 0.27 | 0.48 | 24.11 | 267.5 |
| （参考：实际发出口径）轮内 | 3081 | 0.08 | 0.14 | 0.19 | 605.5 | 1025.1 |
| 登录阶段整体（182–233 s） | 20265 | 0.25 | 1.80 | 2.77 | 298.5 | 1025.1 |

每轮时长（开始 → 10 个 LOGIN 都应答）：1294, 1298, 1293, 1291, 1287, 1301 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.31 | 2.18 | 2.77 | 8.22 | 47.37 | 0.30 |
| 单人 | 轮内 | 1246 | 0.27 | 2.18 | 2.67 | 2.75 | 2.90 | 0.17 |
| 双人组领队 | 稳态窗 | 14076 | 0.26 | 1.37 | 11.25 | 33.49 | 267.5 | 10.98 |
| 双人组领队 | 轮内 | 915* | 0.17 | 74.96 | 621.4 | 1025.1 | 1025.1 | 12.81 |
| 双人组队员 | 稳态窗 | 14112 | 0.25 | 1.29 | 2.03 | 22.83 | 42.79 | 0.55 |
| 双人组队员 | 轮内 | 920* | 0.13 | 1.04 | 1.66 | 23.09 | 23.09 | 0.18 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 349.5 | 659.5 | 666.0 | 666.0 | 666.0 |
| 启动器登录（HTTP，含校验） | 60** | 629.5 | 636.6 | 637.7 | 637.7 | 637.7 |
| Game LOGIN 0x0001 | 60** | 256.8 | 562.9 | 570.4 | 570.4 | 570.4 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 196658 包/秒（5899753 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47388 | 0.27 | 1.81 | 2.79 | 24.51 | 267.5 |
| 其余连接，灌包期间 | 11860 | 0.17 | 1.81 | 2.71 | 13.41 | 26.66 |
| （参考：实际发出口径）其余连接，灌包期间 | 11860 | 0.07 | 0.14 | 0.50 | 11.39 | 26.66 |
| 灌包连接自身（含其窗口内排队） | 5899784 | 0.06 | 0.10 | 0.13 | 0.99 | 16.59 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39291 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3638 | leave dungeon | 21.7 |
| EPLP | 请求者 | 78 | 3590 | leave dungeon | 21.7 |
| SET_USER_AREA | 同伴 | 6 | 3036 | town | 5.4 |
| SET_USER_AREA | 请求者 | 12 | 2857 | town | 5.4 |
| LEAVE_PARTY | 同伴 | 78 | 1799 | leave party | 102.8 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 13.6 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 13.6 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 1252 | party dungeon entry | 137.0 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 1252 | party dungeon entry | 137.0 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 83 | 262 | party | 70.7 |
| RESPONSE_PEER | 请求者 | 83 | 260 | party | 70.7 |
| LEAVE_PARTY | 请求者 | 78 | 196 | leave party | 22.9 |
| CREATE_CHARACTER | 请求者 | 21 | 137 | create | 0.2 |
| REQUEST_PEER | 同伴 | 83 | 100 | party | 9.0 |
| REQUEST_PEER | 请求者 | 83 | 72 | party | 9.0 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 13.7 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 13.7 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 2.79 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 208.5 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 2.71 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 7 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 575.9 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `LeaveDungeonCommand`：样本 78，p99.9 1019.0 ms，max 1019.0 ms（出现于 t=223.3 s；冷启动窗内 max 19.35 ms，之后 max 1019.0 ms）
- `RegisterAccountCommand`：样本 81，p99.9 665.7 ms，max 665.7 ms（出现于 t=232.7 s；冷启动窗内 max 71.47 ms，之后 max 665.7 ms）
- `AuthenticateAndIssueTicketCommand`：样本 81，p99.9 637.5 ms，max 637.5 ms（出现于 t=233.0 s；冷启动窗内 max 65.82 ms，之后 max 637.5 ms）
- `EnterDungeonCommand`：样本 79，p99.9 633.1 ms，max 633.1 ms（出现于 t=232.9 s；冷启动窗内 max 67.52 ms，之后 max 633.1 ms）
- `RedeemGameLoginCommand`：样本 81，p99.9 570.2 ms，max 570.2 ms（出现于 t=233.3 s；冷启动窗内 max 1.59 ms，之后 max 570.2 ms）
- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 502.8 ms，max 502.8 ms（出现于 t=0.8 s；冷启动窗内 max 502.8 ms，之后 max – ms）
- `AdvanceCreatureClockCommand`：样本 335，p99.9 346.0 ms，max 346.0 ms（出现于 t=233.3 s；冷启动窗内 max 57.83 ms，之后 max 346.0 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：27 个；截尾样本（超时或连接被关闭）0 个，协议无回包 83 个。
- SET_USER_POSITION：11 个（其中截尾 0），最大 971 ms，计划发出时刻 203.0, 203.1, 222.4, 222.4, 222.5, 222.6, 222.6, 222.7, 222.9, 222.9, 223.0 s
- CHECK_USER_CONNECTION：9 个（其中截尾 0），最大 871 ms，计划发出时刻 192.8, 212.8, 222.5, 222.7, 222.8, 222.8, 223.0, 223.1, 232.6 s
- SELECT_DUNGEON：4 个（其中截尾 0），最大 636 ms，计划发出时刻 91.8, 192.4, 212.5, 232.3 s
- EPLP：2 个（其中截尾 0），最大 1025 ms，计划发出时刻 202.9, 222.3 s
- SELECT_CHARACTER：1 个（其中截尾 0），最大 576 ms，计划发出时刻 0.3 s

失败的命令 0 条：

