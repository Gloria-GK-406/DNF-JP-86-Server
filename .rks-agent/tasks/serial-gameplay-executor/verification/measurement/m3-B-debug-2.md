# 基线测量 — m3bd2（合成负载）

> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。

## 环境

- 机器：Apple M5，10 逻辑核，内存 32 GiB，macOS 27.0.1
- 磁盘（数据库所在卷 `/`，临时目录 `/var/folders/qc/sl_p_wxs2qvg3pn_zknp1ppw0000gn/T/`）：设备 /dev/disk3s5，文件系统 APFS，Internal，Apple Fabric，Solid State: Yes（`diskutil info /`：Solid State:               Yes）
- .NET 运行时（工具进程）：.NET 10.0.10
- Host：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，构建配置 Release，Host.dll 修改时间 2026-10-23T19:33:50Z
- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），额外设置 `Logging__Console__FormatterOptions__SingleLine=true`
- SQLite：版本 3.53.4；`PRAGMA journal_mode` = **wal**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；`PRAGMA synchronous` = **2**（FULL；连接级，取值见下）
  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：
  - Host 原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3821 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48002 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26834 | 55.90% |
| SET_USER_POSITION | 20324 | 42.34% |
| STEP_FENCE | 562 | 1.17% |
| FINISH_LOADING | 68 | 0.14% |
| EPLP | 37 | 0.08% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 34 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26834 | 0.42 | 2.02 | 13.28 | 45.06 | 60.88 | 0 | 12.20 | 44.52 | 60.72 |
| SET_USER_POSITION | 20324 | 0.51 | 2.11 | 11.19 | 42.65 | 59.03 | 0 | 9.56 | 42.65 | 58.35 |
| FINISH_LOADING | 68** | 0.20 | 1.20 | 9.38 | 9.38 | 9.38 | 0 | 9.38 | 9.38 | 9.38 |
| EPLP | 37** | 38.42 | 59.83 | 61.04 | 61.04 | 61.04 | 0 | 60.89 | 60.89 | 60.89 |
| LEAVE_PARTY | 36** | 0.75 | 1.38 | 2.50 | 2.50 | 2.50 | 0 | 2.50 | 2.50 | 2.50 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.46 | 0.94 | 0.97 | 0.97 | 0.97 | 0 | 0.97 | 0.97 | 0.97 |
| ENTER_SELECT_DUNGEON | 35** | 25.74 | 39.40 | 41.20 | 41.20 | 41.20 | 0 | 40.56 | 40.56 | 40.56 |
| SELECT_DUNGEON | 34** | 15.49 | 22.98 | 27.81 | 27.81 | 27.81 | 0 | 27.81 | 27.81 | 27.81 |
| **全部计入的请求** | 47404 | 0.47 | 2.10 | 15.59 | 45.16 | 61.04 | 36 | 14.74 | 44.92 | 60.89 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47440 | 0.11 | 1.41 | 2.17 | 4.74 | 28.57（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.76 | 10.06 | 99.03 | 99.03 | 99.03 | 0.4 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 18.14 | 51.52 | 60.08 | 60.08 | 60.08 | 2.5 | 0 |
| EnterDungeonCommand | Transactional | 80** | 8.68 | 12.14 | 57.83 | 57.83 | 57.83 | 13.6 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.36 | 30.21 | 55.65 | 55.65 | 55.65 | 4.5 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.79 | 50.50 | 55.14 | 55.14 | 55.14 | 1.0 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.28 | 24.42 | 24.68 | 24.68 | 24.68 | 3.8 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.23 | 7.37 | 23.78 | 23.78 | 23.78 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.64 | 2.63 | 17.09 | 17.09 | 17.09 | 18.9 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.27 | 0.54 | 5.66 | 5.66 | 5.66 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 1.60 | 2.99 | 5.05 | 5.05 | 5.05 | 73.1 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.15 | 0.53 | 4.52 | 4.52 | 4.52 | 212.1 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337* | 0.08 | 0.38 | 0.49 | 3.81 | 3.81 | 186.0 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.07 | 3.71 | 3.71 | 3.71 | 0.3 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.26 | 3.61 | 3.61 | 3.61 | 0.5 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.17 | 3.35 | 3.35 | 3.35 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.03 | 0.17 | 3.12 | 3.12 | 3.12 | 8.7 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.07 | 2.89 | 2.89 | 2.89 | 0.5 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.10 | 2.57 | 2.57 | 2.57 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.06 | 0.17 | 2.29 | 2.29 | 2.29 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.08 | 1.86 | 1.86 | 1.86 | 0.3 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.07 | 1.83 | 1.83 | 1.83 | 0.5 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.59 | 1.59 | 1.59 | 0.2 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.03 | 0.06 | 1.46 | 1.46 | 0.2 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.04 | 1.31 | 1.31 | 1.31 | 1.31 | 60.0 | 0 |
| LeavePartyCommand | Direct | 77** | 0.03 | 0.08 | 1.27 | 1.27 | 1.27 | 23.8 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.04 | 0.71 | 0.71 | 0.71 | 0.3 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.14 | 0.68 | 0.68 | 0.68 | 60.0 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.07 | 0.24 | 0.65 | 0.65 | 15.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.03 | 0.29 | 0.62 | 0.62 | 13.6 | 0 |

命令日志行 1770 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**235.9 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo01；参考口径 235.9 ms）。冷启动窗整体：22518 | 0.47 | 2.50 | 39.86 | 101.0 | 235.9（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 24 | 235.9 | 235.9 | SELECT_CHARACTER |
| 1 | 112 | 70.41 | 70.88 | SELECT_CHARACTER |
| 2 | 171 | 130.5 | 132.6 | SELECT_CHARACTER |
| 3 | 178 | 117.0 | 117.4 | CHECK_USER_CONNECTION |
| 4 | 317 | 86.72 | 116.2 | SELECT_CHARACTER |
| 5 | 400 | 6.49 | 8.84 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.30 | 2.72 | SET_USER_POSITION |
| 7 | 400 | 2.54 | 2.99 | CHECK_USER_CONNECTION |
| 8 | 395 | 9.86 | 9.92 | SET_USER_POSITION |
| 9 | 385 | 3.36 | 4.53 | CHECK_USER_CONNECTION |
| 10 | 390 | 3.47 | 4.47 | CHECK_USER_CONNECTION |
| 11 | 400 | 3.33 | 3.46 | CHECK_USER_CONNECTION |
| 12 | 400 | 2.65 | 3.28 | CHECK_USER_CONNECTION |
| 13 | 384 | 64.82 | 75.98 | SELECT_DUNGEON |
| 14 | 392 | 3.26 | 12.00 | ENTER_SELECT_DUNGEON |
| 15 | 378 | 27.72 | 27.84 | CHECK_USER_CONNECTION |
| 16 | 398 | 2.30 | 2.70 | SET_USER_POSITION |
| 17 | 400 | 2.48 | 3.97 | CHECK_USER_CONNECTION |
| 18 | 399 | 50.36 | 53.64 | EPLP |
| 19 | 395 | 25.07 | 28.63 | EPLP |
| 20 | 398 | 28.96 | 36.53 | EPLP |
| 21 | 396 | 41.93 | 52.44 | EPLP |
| 22 | 400 | 2.92 | 4.07 | SET_USER_POSITION |
| 23 | 398 | 12.37 | 14.25 | LEAVE_PARTY |
| 24 | 396 | 2.31 | 3.37 | CHECK_USER_CONNECTION |
| 25 | 397 | 2.64 | 6.74 | SET_USER_POSITION |
| 26 | 397 | 3.77 | 5.99 | SET_USER_POSITION |
| 27 | 400 | 2.34 | 3.69 | CHECK_USER_CONNECTION |
| 28 | 390 | 2.48 | 3.06 | SET_USER_POSITION |
| 29 | 395 | 4.99 | 5.47 | CHECK_USER_CONNECTION |
| 30 | 395 | 2.50 | 2.71 | SET_USER_POSITION |
| 31 | 390 | 2.74 | 3.29 | SET_USER_POSITION |
| 32 | 400 | 2.57 | 3.16 | CHECK_USER_CONNECTION |
| 33 | 392 | 22.38 | 35.76 | ENTER_SELECT_DUNGEON |
| 34 | 376 | 29.39 | 30.37 | ENTER_SELECT_DUNGEON |
| 35 | 402 | 17.06 | 24.30 | ENTER_SELECT_DUNGEON |
| 36 | 382 | 9.15 | 26.88 | SELECT_DUNGEON |
| 37 | 400 | 2.07 | 3.05 | SET_USER_POSITION |
| 38 | 397 | 29.69 | 33.52 | EPLP |
| 39 | 397 | 41.96 | 43.24 | EPLP |
| 40 | 396 | 28.38 | 57.13 | SET_USER_POSITION |
| 41 | 398 | 52.26 | 59.08 | EPLP |
| 42 | 398 | 4.37 | 5.01 | CHECK_USER_CONNECTION |
| 43 | 400 | 3.18 | 3.32 | CHECK_USER_CONNECTION |
| 44 | 396 | 2.69 | 3.00 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.72 | 3.18 | SET_USER_POSITION |
| 46 | 396 | 6.48 | 10.64 | SET_USER_POSITION |
| 47 | 400 | 2.21 | 3.45 | CHECK_USER_CONNECTION |
| 48 | 395 | 2.74 | 5.79 | SET_USER_POSITION |
| 49 | 395 | 2.83 | 3.17 | SET_USER_POSITION |
| 50 | 389 | 3.03 | 4.28 | CHECK_USER_CONNECTION |
| 51 | 396 | 5.50 | 5.61 | CHECK_USER_CONNECTION |
| 52 | 395 | 2.66 | 2.93 | CHECK_USER_CONNECTION |
| 53 | 392 | 10.51 | 17.21 | SET_USER_POSITION |
| 54 | 401 | 29.33 | 36.80 | ENTER_SELECT_DUNGEON |
| 55 | 377 | 21.53 | 23.73 | ENTER_SELECT_DUNGEON |
| 56 | 390 | 3.01 | 5.13 | CHECK_USER_CONNECTION |
| 57 | 393 | 13.23 | 24.32 | EPLP |
| 58 | 397 | 2.90 | 3.30 | SET_USER_POSITION |
| 59 | 400 | 12.96 | 19.13 | SET_USER_POSITION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47404 | 0.47 | 2.10 | 15.59 | 45.16 | 61.04 |
| 10 并发登录的轮内 | 474* | 39.51 | 95.60 | 102.0 | 104.1 | 104.1 |
| （参考：实际发出口径）稳态窗 | 47404 | 0.23 | 0.68 | 14.74 | 44.92 | 60.89 |
| （参考：实际发出口径）轮内 | 474* | 4.87 | 90.76 | 92.26 | 92.56 | 92.56 |
| 登录阶段整体（182–232 s） | 19836 | 0.48 | 2.98 | 50.58 | 96.91 | 104.1 |

每轮时长（开始 → 10 个 LOGIN 都应答）：201, 193, 202, 210, 184, 199 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.54 | 2.24 | 11.03 | 44.55 | 60.88 | 10.16 |
| 单人 | 轮内 | 189* | 39.34 | 94.50 | 101.2 | 102.0 | 102.0 | 91.99 |
| 双人组领队 | 稳态窗 | 14084 | 0.45 | 1.95 | 24.41 | 48.36 | 61.04 | 23.44 |
| 双人组领队 | 轮内 | 141* | 39.57 | 93.72 | 103.8 | 104.1 | 104.1 | 92.46 |
| 双人组队员 | 稳态窗 | 14120 | 0.43 | 1.72 | 13.53 | 42.90 | 57.09 | 12.79 |
| 双人组队员 | 轮内 | 144* | 39.50 | 96.91 | 104.0 | 104.0 | 104.0 | 92.39 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 102.9 | 111.1 | 114.8 | 114.8 | 114.8 |
| 启动器登录（HTTP，含校验） | 60** | 87.04 | 92.95 | 99.11 | 99.11 | 99.11 |
| Game LOGIN 0x0001 | 60** | 0.23 | 1.00 | 1.04 | 1.04 | 1.04 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 62607 包/秒（1878225 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47404 | 0.47 | 2.10 | 15.59 | 45.16 | 61.04 |
| 其余连接，灌包期间 | 11859 | 0.51 | 4.61 | 8.94 | 34.85 | 43.13 |
| （参考：实际发出口径）其余连接，灌包期间 | 11859 | 0.11 | 4.19 | 8.60 | 34.51 | 40.29 |
| 灌包连接自身（含其窗口内排队） | 1878256 | 0.36 | 0.47 | 5.26 | 7.44 | 48.59 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3881 | leave dungeon | 123.5 |
| EPLP | 请求者 | 78 | 3671 | leave dungeon | 63.2 |
| SET_USER_AREA | 同伴 | 6 | 3165 | town | 5.0 |
| SET_USER_AREA | 请求者 | 12 | 2986 | town | 5.0 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.5 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 679 | party dungeon entry | 96.8 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 679 | party dungeon entry | 96.8 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 82 | 286 | party | 8.7 |
| RESPONSE_PEER | 请求者 | 82 | 284 | party | 8.7 |
| REQUEST_PEER | 同伴 | 82 | 267 | party | 272.7 |
| LEAVE_PARTY | 同伴 | 77 | 235 | leave party | 231.6 |
| REQUEST_PEER | 请求者 | 82 | 167 | party | 272.7 |
| LEAVE_PARTY | 请求者 | 77 | 148 | leave party | 24.2 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.3 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 13.6 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 13.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 15.59 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 102.0 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 8.94 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 0 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 235.9 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- 无。

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1997033 行（Host 就绪之后 1997028 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 70.22 | 131.9 | 230.3 | 230.3 | 230.3 | 230.3 | 230.3 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 80** | 14.46 | 22.42 | 75.56 | 75.56 | 75.56 | 75.56 | 75.56 | 22.80 | 13.6 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 21.54 | 38.64 | 63.93 | 63.93 | 63.93 | 63.93 | 63.93 | 46.26 | 13.5 | 0 |
| inbound:GameEplpCommand | 78** | 37.79 | 59.10 | 60.42 | 60.42 | 60.42 | 51.71 | 57.80 | 60.42 | 144.9 | 0 |
| inbound:GameSetUserArea | 12** | 10.71 | 39.39 | 39.39 | 39.39 | 39.39 | 10.71 | 39.39 | – | 5.0 | 0 |
| inbound:GameCreateCharacter | 21** | 1.63 | 7.99 | 31.36 | 31.36 | 31.36 | 31.36 | 31.36 | – | 0.3 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.62 | 15.07 | 15.07 | 15.07 | 1.73 | 1.73 | 15.07 | 274.0 | 0 |
| inbound:GameLeaveParty | 77** | 0.54 | 5.36 | 13.82 | 13.82 | 13.82 | 13.82 | 13.82 | 7.19 | 23.8 | 0 |
| inbound:GameResponsePeer | 82** | 0.37 | 0.86 | 9.63 | 9.63 | 9.63 | 9.63 | 9.63 | 0.86 | 8.7 | 0 |
| inbound:GameLogin | 81** | 0.14 | 0.39 | 9.17 | 9.17 | 9.17 | 9.17 | 9.17 | 0.42 | 0.2 | 0 |
| event:achievement.inventory | 210* | 0.32 | 0.75 | 7.46 | 7.93 | 7.93 | 7.46 | 7.93 | – | 4.1 | 0 |
| inbound:GameSetUserPosition | 45923 | 0.03 | 0.09 | 0.13 | 2.64 | 7.83 | 1.78 | 3.43 | 7.83 | 272.4 | 0 |
| inbound:GameRequestPeer | 82** | 1.99 | 5.31 | 7.76 | 7.76 | 7.76 | 7.76 | 7.76 | 5.47 | 8.7 | 0 |
| timer:PremiumDailyRefreshService | 4** | 2.32 | 7.42 | 7.42 | 7.42 | 7.42 | 7.42 | 7.42 | 2.39 | 60.0 | 0 |
| inbound:GameConnectionCheck | 1940288 | 0.00 | 0.00 | 0.01 | 0.04 | 7.28 | 0.12 | 3.75 | 7.28 | 271.6 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.16 | 1.04 | 7.02 | 7.02 | 7.02 | 7.02 | 7.02 | 4.58 | 0.2 | 0 |
| timer:CreatureClockService | 277* | 0.02 | 0.84 | 1.86 | 5.98 | 5.98 | 5.98 | 5.98 | 4.09 | 1.0 | 0 |
| event:quest.inventory | 210* | 0.11 | 0.33 | 0.58 | 5.27 | 5.27 | 5.27 | 5.27 | – | 0.3 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.75 | 3.80 | 5.20 | 5.20 | 5.20 | 5.20 | 5.20 | – | 0.5 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.79 | 4.07 | 4.07 | 4.07 | 4.07 | 2.07 | 2.07 | 4.07 | 180.0 | 0 |
| lifecycle:connected | 82** | 0.02 | 0.05 | 4.02 | 4.02 | 4.02 | 4.02 | 4.02 | 0.04 | 0.1 | 0 |
| inbound:GameFinishLoading | 160* | 0.11 | 0.20 | 2.00 | 3.25 | 3.25 | 3.25 | 3.25 | 1.13 | 13.6 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 1.21 | 2.97 | 2.97 | 2.97 | 2.97 | 2.97 | 2.97 | 1.24 | 15.4 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.08 | 0.28 | 2.60 | 2.60 | 2.60 | 2.60 | 2.60 | 0.28 | 0.2 | 0 |
| timer:FatigueDailyRefreshService | 4** | 1.42 | 2.54 | 2.54 | 2.54 | 2.54 | 1.42 | 1.42 | 2.54 | 120.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.02 | 2.36 | 2.36 | 2.36 | 2.36 | 2.36 | 2.36 | 0.02 | 60.0 | 0 |
| event:altar.departure | 160* | 0.15 | 0.35 | 0.55 | 2.28 | 2.28 | 2.28 | 2.28 | 0.55 | 18.9 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.11 | 0.21 | 0.24 | 2.13 | 2.13 | 2.13 | 2.13 | 0.33 | 1.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.06 | 1.80 | 1.80 | 1.80 | 1.80 | 1.80 | 1.80 | 0.33 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.04 | 0.06 | 0.53 | 1.25 | 1.25 | 1.25 | 0.08 | 0.2 | 0 |
| event:quest.mirror-leave | 160* | 0.15 | 0.38 | 1.01 | 1.25 | 1.25 | 1.01 | 1.01 | 1.25 | 60.8 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.05 | 1.15 | 1.15 | 1.15 | 1.15 | 0.08 | 0.5 | 0 |
| timer:JoustMaintenanceService | 277* | 0.29 | 0.53 | 0.82 | 0.97 | 0.97 | 0.15 | 0.57 | 0.97 | 165.9 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.01 | 0.95 | 0.95 | 0.95 | 0.95 | 0.95 | 0.95 | 0.81 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.02 | 0.85 | 0.85 | 0.85 | 0.85 | 0.85 | 0.85 | 0.02 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.03 | 0.83 | 0.83 | 0.83 | 0.83 | 0.83 | 0.83 | 0.03 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 0.78 | 0.78 | 0.78 | 0.78 | 0.03 | 0.5 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.04 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.06 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.02 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.03 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.02 | 0.61 | 0.61 | 0.61 | 0.61 | 0.61 | 0.61 | 0.06 | 60.0 | 0 |
| event:tournament.departure | 160* | 0.12 | 0.21 | 0.27 | 0.59 | 0.59 | 0.20 | 0.22 | 0.59 | 256.2 | 0 |
| timer:TournamentRewardSweepService | 5532 | 0.01 | 0.01 | 0.02 | 0.04 | 0.48 | 0.48 | 0.48 | 0.13 | 0.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.03 | 0.14 | 0.14 | 0.14 | 0.14 | 0.14 | 0.14 | 0.08 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.02 | 0.03 | 0.05 | 0.05 | 0.05 | 0.05 | 0.02 | 13.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 15.59 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25393 | 0.02 | 0.48 | 39.80 | 108.0 | 234.9 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22911 | 0.02 | 0.29 | 27.72 | 86.18 | 126.2 |  |  |
| 稳态窗 | 全部作业 | 52088 | 0.02 | 0.23 | 14.78 | 42.93 | 60.45 | inbound:GameConnectionCheck | 144.9 |
| 稳态窗 | 只算 inbound: | 48054 | 0.02 | 0.21 | 9.85 | 42.87 | 60.45 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 867* | 0.07 | 1.03 | 2.42 | 10.89 | 10.89 | timer:TournamentRewardSweepService | 212.0 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 607* | 0.09 | 1.05 | 2.42 | 2.49 | 2.49 |  |  |
| (e) 灌包期间 | 全部作业 | 1891261 | 0.00 | 0.00 | 0.01 | 0.06 | 39.90 | inbound:GameConnectionCheck | 271.2 |
| (e) 灌包期间 | 只算 inbound: | 1890259 | 0.00 | 0.00 | 0.01 | 0.06 | 39.90 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 105 | 227.5 | 234.9 | event:achievement.inventory | 230.3 |
| 1 | 283 | 126.7 | 127.3 | event:quest.inventory | 126.6 |
| 2 | 332 | 126.2 | 132.3 | event:achievement.inventory | 131.9 |
| 3 | 320 | 116.0 | 118.4 | event:quest.inventory | 116.6 |
| 4 | 484 | 85.61 | 115.9 | event:quest.inventory | 115.7 |
| 5 | 445 | 8.21 | 15.42 | event:quest.inventory | 15.11 |
| 6 | 432 | 0.20 | 0.57 | inbound:GameConnectionCheck | 0.56 |
| 7 | 431 | 0.43 | 1.68 | inbound:GameConnectionCheck | 1.64 |
| 8 | 432 | 6.84 | 8.65 | inbound:GameSetUserPosition | 9.63 |
| 9 | 436 | 3.14 | 4.41 | inbound:GameConnectionCheck | 5.57 |
| 10 | 427 | 0.31 | 3.26 | inbound:GameSetUserPosition | 3.07 |
| 11 | 431 | 0.33 | 3.20 | inbound:GameConnectionCheck | 3.17 |
| 12 | 432 | 0.48 | 0.62 | inbound:GameConnectionCheck | 0.16 |
| 13 | 433 | 62.33 | 63.59 | inbound:GameConnectionCheck | 75.56 |
| 14 | 433 | 0.86 | 0.96 | inbound:GameConnectionCheck | 11.82 |
| 15 | 437 | 17.40 | 27.61 | inbound:GameConnectionCheck | 27.48 |
| 16 | 430 | 0.30 | 0.35 | inbound:GameConnectionCheck | 0.51 |
| 17 | 431 | 0.23 | 0.29 | inbound:GameConnectionCheck | 3.75 |
| 18 | 440 | 48.90 | 51.48 | timer:TournamentRewardSweepService | 51.71 |
| 19 | 444 | 24.16 | 26.71 | inbound:GameConnectionCheck | 27.95 |
| 20 | 439 | 27.17 | 34.93 | inbound:GameSetUserPosition | 36.18 |
| 21 | 445 | 38.84 | 50.98 | inbound:GameSetUserPosition | 52.13 |
| 22 | 432 | 0.36 | 0.56 | inbound:GameSetUserPosition | 0.43 |
| 23 | 431 | 12.13 | 13.41 | inbound:GameConnectionCheck | 13.82 |
| 24 | 432 | 0.57 | 1.14 | inbound:GameConnectionCheck | 2.16 |
| 25 | 432 | 0.32 | 0.47 | inbound:GameSetUserPosition | 5.36 |
| 26 | 431 | 0.41 | 3.49 | inbound:GameConnectionCheck | 3.43 |
| 27 | 431 | 0.29 | 3.50 | inbound:GameConnectionCheck | 3.41 |
| 28 | 432 | 0.27 | 1.47 | inbound:GameSetUserPosition | 2.75 |
| 29 | 432 | 2.94 | 3.53 | inbound:GameConnectionCheck | 3.31 |
| 30 | 432 | 0.41 | 1.17 | inbound:GameConnectionCheck | 1.17 |
| 31 | 431 | 0.45 | 1.69 | inbound:GameConnectionCheck | 2.05 |
| 32 | 432 | 0.39 | 2.76 | inbound:GameConnectionCheck | 2.60 |
| 33 | 441 | 11.38 | 22.18 | inbound:GameConnectionCheck | 34.64 |
| 34 | 426 | 29.14 | 29.18 | inbound:GameSetUserPosition | 30.10 |
| 35 | 442 | 15.23 | 20.19 | inbound:GameSetUserPosition | 22.80 |
| 36 | 423 | 6.42 | 12.63 | inbound:GameSetUserPosition | 26.71 |
| 37 | 431 | 0.17 | 0.21 | inbound:GameConnectionCheck | 0.43 |
| 38 | 447 | 28.96 | 31.48 | inbound:GameSetUserPosition | 32.59 |
| 39 | 437 | 39.38 | 41.67 | inbound:GameConnectionCheck | 42.74 |
| 40 | 446 | 36.27 | 56.00 | inbound:GameSetUserPosition | 55.95 |
| 41 | 438 | 49.28 | 54.64 | inbound:GameConnectionCheck | 57.80 |
| 42 | 432 | 0.40 | 4.24 | inbound:GameConnectionCheck | 0.40 |
| 43 | 431 | 0.25 | 2.74 | inbound:GameSetUserPosition | 2.64 |
| 44 | 432 | 0.28 | 0.39 | inbound:GameConnectionCheck | 1.13 |
| 45 | 431 | 0.34 | 0.44 | inbound:GameConnectionCheck | 1.85 |
| 46 | 432 | 0.30 | 3.66 | inbound:GameSetUserPosition | 3.55 |
| 47 | 436 | 0.32 | 0.40 | inbound:GameSetUserPosition | 2.15 |
| 48 | 427 | 0.36 | 3.43 | inbound:GameSetUserPosition | 3.37 |
| 49 | 431 | 0.44 | 2.18 | inbound:GameConnectionCheck | 3.28 |
| 50 | 436 | 2.55 | 3.40 | inbound:GameConnectionCheck | 3.35 |
| 51 | 427 | 0.33 | 0.40 | timer:TournamentRewardSweepService | 1.41 |
| 52 | 432 | 0.32 | 0.99 | inbound:GameConnectionCheck | 0.95 |
| 53 | 432 | 0.32 | 16.14 | inbound:GameConnectionCheck | 16.13 |
| 54 | 431 | 27.72 | 33.96 | inbound:GameConnectionCheck | 36.17 |
| 55 | 446 | 20.48 | 20.90 | timer:TournamentRewardSweepService | 22.62 |
| 56 | 422 | 0.23 | 1.14 | inbound:GameSetUserPosition | 0.24 |
| 57 | 439 | 8.37 | 13.78 | timer:TournamentRewardSweepService | 22.85 |
| 58 | 432 | 0.18 | 0.32 | inbound:GameConnectionCheck | 0.32 |
| 59 | 443 | 16.43 | 18.98 | inbound:GameSetUserPosition | 7.42 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25393 | 4327 | 7.2% | inbound:GameSelectCharacter 1887 ms／21／max 230.3；inbound:GameEplpCommand 494 ms／13／max 57.80；inbound:GameEnterSelectDungeon 402 ms／18／max 63.93；inbound:GameSetUserPosition 395 ms／9550／max 3.43；inbound:GameSelectDungeon 344 ms／18／max 75.56；inbound:GameSetUserArea 163 ms／12／max 39.39；inbound:GameConnectionCheck 140 ms／13153／max 3.75；event:achievement.inventory 102 ms／210／max 7.93 |
| 稳态窗 | 120.0 | 52088 | 4241 | 3.5% | inbound:GameEplpCommand 1362 ms／36／max 60.42；inbound:GameSetUserPosition 894 ms／20322／max 4.45；inbound:GameEnterSelectDungeon 834 ms／35／max 39.93；inbound:GameSelectDungeon 532 ms／34／max 22.80；inbound:GameConnectionCheck 326 ms／27451／max 4.61；inbound:GameRequestPeer 81 ms／36／max 5.47；timer:JoustMaintenanceService 41 ms／120／max 0.97；timer:CreatureClockService 22 ms／120／max 1.86 |
| (d) 10 并发登录的轮内 | 1.2 | 867 | 35 | 2.9% | http:POST /api/launcher/v1/register 16 ms／60／max 4.58；inbound:GameLogin 9 ms／60／max 0.42；http:POST /api/launcher/v1/login 5 ms／60／max 0.28；inbound:GameSetUserPosition 2 ms／221／max 0.13；inbound:GameConnectionCheck 1 ms／325／max 0.03；lifecycle:connected 1 ms／60／max 0.04；lifecycle:disconnected 0 ms／55／max 0.08；inbound:GameLeaveParty 0 ms／1／max 0.13 |
| (e) 灌包期间 | 30.0 | 1891261 | 8631 | 28.8% | inbound:GameConnectionCheck 7994 ms／1885059／max 7.28；inbound:GameEplpCommand 284 ms／9／max 39.89；inbound:GameSetUserPosition 131 ms／5134／max 6.10；inbound:GameEnterSelectDungeon 113 ms／7／max 18.30；inbound:GameSelectDungeon 77 ms／7／max 13.40；inbound:GameRequestPeer 7 ms／9／max 0.86；timer:JoustMaintenanceService 4 ms／30／max 0.16；event:altar.departure 3 ms／18／max 0.32 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.4，`timer:` 31.8，`event:` 1.8。

