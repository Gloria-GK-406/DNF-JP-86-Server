# 基线测量 — m3bd1（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3732 ms

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
| CHECK_USER_CONNECTION | 26860 | 55.96% |
| SET_USER_POSITION | 20297 | 42.29% |
| STEP_FENCE | 562 | 1.17% |
| FINISH_LOADING | 68 | 0.14% |
| EPLP | 37 | 0.08% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 34 | 0.07% |
| SELECT_DUNGEON | 34 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26860 | 0.41 | 2.22 | 20.96 | 51.08 | 75.39 | 0 | 19.02 | 46.42 | 61.85 |
| SET_USER_POSITION | 20297 | 0.54 | 2.29 | 16.19 | 49.39 | 72.53 | 0 | 14.29 | 48.15 | 60.18 |
| FINISH_LOADING | 68** | 0.19 | 0.30 | 8.53 | 8.53 | 8.53 | 0 | 8.53 | 8.53 | 8.53 |
| EPLP | 37** | 43.54 | 61.61 | 77.80 | 77.80 | 77.80 | 0 | 61.61 | 61.61 | 61.61 |
| LEAVE_PARTY | 36** | 0.89 | 5.78 | 14.69 | 14.69 | 14.69 | 0 | 14.69 | 14.69 | 14.69 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.52 | 0.75 | 0.99 | 0.99 | 0.99 | 0 | 0.99 | 0.99 | 0.99 |
| ENTER_SELECT_DUNGEON | 34** | 26.97 | 39.40 | 39.43 | 39.43 | 39.43 | 0 | 39.40 | 39.40 | 39.40 |
| SELECT_DUNGEON | 34** | 14.95 | 20.68 | 22.05 | 22.05 | 22.05 | 0 | 22.05 | 22.05 | 22.05 |
| **全部计入的请求** | 47402 | 0.47 | 2.28 | 21.48 | 51.37 | 77.80 | 36 | 19.85 | 48.65 | 61.85 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47438 | 0.12 | 1.68 | 2.10 | 12.89 | 74.25（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 3.05 | 24.51 | 102.1 | 102.1 | 102.1 | 0.4 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 15.16 | 36.56 | 59.01 | 59.01 | 59.01 | 3.2 | 0 |
| EnterDungeonCommand | Transactional | 79** | 8.49 | 10.87 | 57.26 | 57.26 | 57.26 | 13.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 15.59 | 41.06 | 53.76 | 53.76 | 53.76 | 4.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 15.88 | 28.54 | 42.27 | 42.27 | 42.27 | 4.2 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.22 | 24.74 | 36.38 | 36.38 | 36.38 | 2.5 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.24 | 4.49 | 24.00 | 24.00 | 24.00 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.69 | 4.06 | 19.15 | 19.15 | 19.15 | 18.7 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.03 | 0.19 | 5.64 | 5.64 | 5.64 | 8.7 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 1.93 | 3.28 | 5.47 | 5.47 | 5.47 | 151.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.30 | 0.63 | 5.12 | 5.12 | 5.12 | 0.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.15 | 0.34 | 4.54 | 4.54 | 4.54 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.13 | 4.36 | 4.36 | 4.36 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.31 | 3.74 | 3.74 | 3.74 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.26 | 3.43 | 3.43 | 3.43 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 335* | 0.12 | 0.59 | 0.98 | 3.37 | 3.37 | 158.0 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.50 | 3.27 | 3.27 | 3.27 | 0.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.08 | 2.89 | 2.89 | 2.89 | 0.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.10 | 0.27 | 2.55 | 2.55 | 24.4 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.06 | 0.13 | 2.34 | 2.34 | 2.34 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.06 | 0.42 | 1.61 | 1.61 | 1.61 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.58 | 1.58 | 1.58 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.06 | 0.09 | 1.55 | 1.55 | 1.55 | 0.5 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.05 | 1.44 | 1.44 | 0.2 | 0 |
| LeavePartyCommand | Direct | 77** | 0.03 | 0.07 | 1.30 | 1.30 | 1.30 | 23.9 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.04 | 1.26 | 1.26 | 1.26 | 1.26 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.18 | 0.70 | 0.70 | 0.70 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.03 | 0.33 | 0.60 | 0.60 | 13.6 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.13 | 0.48 | 0.48 | 0.48 | 60.0 | 0 |

命令日志行 1765 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**234.3 ms**（SELECT_CHARACTER，计划发出 t=0.29 s，solo03；参考口径 234.3 ms）。冷启动窗整体：22518 | 0.47 | 2.45 | 42.86 | 97.41 | 234.3（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 24 | 234.3 | 234.3 | SELECT_CHARACTER |
| 1 | 112 | 97.41 | 98.51 | CHECK_USER_CONNECTION |
| 2 | 167 | 106.7 | 116.1 | SELECT_CHARACTER |
| 3 | 183 | 112.1 | 117.9 | SELECT_CHARACTER |
| 4 | 320 | 113.9 | 115.7 | SET_USER_POSITION |
| 5 | 400 | 2.22 | 3.05 | SET_USER_POSITION |
| 6 | 400 | 2.21 | 2.38 | SET_USER_POSITION |
| 7 | 400 | 2.70 | 4.35 | SET_USER_POSITION |
| 8 | 395 | 16.84 | 17.61 | RESPONSE_PEER |
| 9 | 384 | 3.40 | 4.99 | SET_USER_POSITION |
| 10 | 391 | 2.75 | 6.90 | CHECK_USER_CONNECTION |
| 11 | 400 | 2.38 | 2.67 | SET_USER_POSITION |
| 12 | 400 | 2.38 | 3.67 | CHECK_USER_CONNECTION |
| 13 | 384 | 68.08 | 88.92 | ENTER_SELECT_DUNGEON |
| 14 | 392 | 8.43 | 12.80 | SELECT_DUNGEON |
| 15 | 382 | 19.48 | 30.22 | ENTER_SELECT_DUNGEON |
| 16 | 394 | 2.18 | 2.22 | SET_USER_POSITION |
| 17 | 400 | 2.44 | 2.78 | CHECK_USER_CONNECTION |
| 18 | 398 | 47.28 | 55.36 | EPLP |
| 19 | 396 | 35.59 | 46.48 | EPLP |
| 20 | 398 | 41.72 | 49.74 | EPLP |
| 21 | 396 | 48.56 | 57.67 | CHECK_USER_CONNECTION |
| 22 | 400 | 2.97 | 3.56 | CHECK_USER_CONNECTION |
| 23 | 399 | 6.64 | 15.32 | LEAVE_PARTY |
| 24 | 395 | 9.66 | 11.07 | CHECK_USER_CONNECTION |
| 25 | 396 | 4.83 | 5.47 | SET_USER_POSITION |
| 26 | 398 | 2.48 | 3.95 | SET_USER_POSITION |
| 27 | 400 | 3.40 | 4.69 | CHECK_USER_CONNECTION |
| 28 | 395 | 2.80 | 4.54 | SET_USER_POSITION |
| 29 | 395 | 3.63 | 5.22 | SET_USER_POSITION |
| 30 | 390 | 5.03 | 7.93 | SET_USER_POSITION |
| 31 | 390 | 2.59 | 2.82 | CHECK_USER_CONNECTION |
| 32 | 400 | 2.40 | 3.70 | CHECK_USER_CONNECTION |
| 33 | 392 | 32.17 | 41.10 | ENTER_SELECT_DUNGEON |
| 34 | 388 | 22.17 | 31.82 | ENTER_SELECT_DUNGEON |
| 35 | 388 | 14.38 | 22.39 | ENTER_SELECT_DUNGEON |
| 36 | 384 | 15.21 | 17.59 | ENTER_SELECT_DUNGEON |
| 37 | 400 | 2.79 | 3.70 | CHECK_USER_CONNECTION |
| 38 | 396 | 24.24 | 42.05 | EPLP |
| 39 | 398 | 28.75 | 32.88 | SET_USER_POSITION |
| 40 | 398 | 37.99 | 44.15 | EPLP |
| 41 | 396 | 69.97 | 72.08 | EPLP |
| 42 | 398 | 3.79 | 6.81 | CHECK_USER_CONNECTION |
| 43 | 396 | 2.81 | 3.96 | CHECK_USER_CONNECTION |
| 44 | 400 | 2.32 | 2.51 | SET_USER_POSITION |
| 45 | 398 | 2.35 | 5.36 | SET_USER_POSITION |
| 46 | 396 | 2.41 | 2.47 | SET_USER_POSITION |
| 47 | 390 | 2.59 | 4.80 | CHECK_USER_CONNECTION |
| 48 | 395 | 3.23 | 4.09 | SET_USER_POSITION |
| 49 | 400 | 2.35 | 3.99 | SET_USER_POSITION |
| 50 | 400 | 4.43 | 7.20 | CHECK_USER_CONNECTION |
| 51 | 390 | 2.57 | 2.74 | SET_USER_POSITION |
| 52 | 387 | 12.60 | 23.77 | ENTER_SELECT_DUNGEON |
| 53 | 384 | 39.73 | 43.44 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 2.43 | 3.06 | CHECK_USER_CONNECTION |
| 55 | 392 | 23.26 | 29.53 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.35 | 3.03 | CHECK_USER_CONNECTION |
| 57 | 380 | 32.30 | 42.47 | EPLP |
| 58 | 400 | 2.27 | 2.68 | SET_USER_POSITION |
| 59 | 398 | 39.79 | 45.36 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47402 | 0.47 | 2.28 | 21.48 | 51.37 | 77.80 |
| 10 并发登录的轮内 | 483* | 13.82 | 74.71 | 98.46 | 103.0 | 103.0 |
| （参考：实际发出口径）稳态窗 | 47402 | 0.23 | 0.69 | 19.85 | 48.65 | 61.85 |
| （参考：实际发出口径）轮内 | 483* | 2.99 | 71.06 | 89.95 | 99.26 | 99.26 |
| 登录阶段整体（182–232 s） | 19832 | 0.51 | 2.84 | 41.70 | 79.60 | 103.0 |

每轮时长（开始 → 10 个 LOGIN 都应答）：204, 214, 194, 203, 203, 198 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.64 | 2.28 | 20.53 | 49.69 | 75.39 | 18.19 |
| 单人 | 轮内 | 197* | 15.43 | 84.50 | 101.5 | 103.0 | 103.0 | 99.26 |
| 双人组领队 | 稳态窗 | 14083 | 0.41 | 2.34 | 25.39 | 55.18 | 77.80 | 24.48 |
| 双人组领队 | 轮内 | 144* | 12.14 | 70.52 | 82.03 | 82.91 | 82.91 | 80.17 |
| 双人组队员 | 稳态窗 | 14119 | 0.41 | 2.22 | 19.03 | 49.52 | 74.03 | 16.47 |
| 双人组队员 | 轮内 | 142* | 13.52 | 70.49 | 81.93 | 82.84 | 82.84 | 80.33 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 100.9 | 113.6 | 132.1 | 132.1 | 132.1 |
| 启动器登录（HTTP，含校验） | 60** | 87.33 | 99.94 | 104.9 | 104.9 | 104.9 |
| Game LOGIN 0x0001 | 60** | 0.22 | 10.59 | 10.67 | 10.67 | 10.67 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 62697 包/秒（1880947 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47402 | 0.47 | 2.28 | 21.48 | 51.37 | 77.80 |
| 其余连接，灌包期间 | 11862 | 0.60 | 4.70 | 8.98 | 31.42 | 41.15 |
| （参考：实际发出口径）其余连接，灌包期间 | 11862 | 0.10 | 4.16 | 8.52 | 31.10 | 40.23 |
| 灌包连接自身（含其窗口内排队） | 1880978 | 0.36 | 0.47 | 5.29 | 7.45 | 48.16 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 4717 | leave dungeon | 41.2 |
| EPLP | 请求者 | 78 | 4669 | leave dungeon | 41.2 |
| SET_USER_AREA | 同伴 | 6 | 3093 | town | 5.0 |
| SET_USER_AREA | 请求者 | 12 | 2914 | town | 5.0 |
| REQUEST_PEER | 同伴 | 82 | 1950 | party | 167.2 |
| REQUEST_PEER | 请求者 | 82 | 1762 | party | 167.2 |
| LEAVE_PARTY | 同伴 | 77 | 1733 | leave party | 163.3 |
| LEAVE_PARTY | 请求者 | 77 | 1646 | leave party | 163.3 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 13.5 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 13.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 711 | party dungeon entry | 53.2 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 711 | party dungeon entry | 53.2 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 82 | 262 | party | 9.2 |
| RESPONSE_PEER | 请求者 | 82 | 260 | party | 9.2 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.2 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 13.6 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 13.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 21.48 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 98.46 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 8.98 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 234.3 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 102.1 ms，max 102.1 ms（出现于 t=0.4 s；冷启动窗内 max 102.1 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1999752 行（Host 就绪之后 1999747 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 72.61 | 117.3 | 232.9 | 232.9 | 232.9 | 232.9 | 232.9 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 79** | 14.09 | 20.15 | 75.26 | 75.26 | 75.26 | 75.26 | 75.26 | 21.88 | 13.6 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 25.69 | 39.97 | 64.18 | 64.18 | 64.18 | 64.18 | 64.18 | 40.18 | 13.5 | 0 |
| inbound:GameEplpCommand | 78** | 41.27 | 60.23 | 61.12 | 61.12 | 61.12 | 52.16 | 54.99 | 61.12 | 124.5 | 0 |
| inbound:GameCreateCharacter | 21** | 1.75 | 5.07 | 31.09 | 31.09 | 31.09 | 31.09 | 31.09 | – | 0.3 | 0 |
| inbound:GameSetUserArea | 12** | 13.38 | 19.46 | 19.46 | 19.46 | 19.46 | 10.92 | 19.46 | – | 5.0 | 0 |
| inbound:GameRequestPeer | 82** | 2.61 | 4.01 | 17.33 | 17.33 | 17.33 | 17.33 | 17.33 | 5.82 | 8.7 | 0 |
| inbound:GameResponsePeer | 82** | 0.40 | 2.25 | 17.13 | 17.13 | 17.13 | 17.13 | 17.13 | 3.21 | 8.7 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.47 | 17.10 | 17.10 | 17.10 | 1.73 | 1.73 | 17.10 | 274.0 | 0 |
| inbound:GameLeaveParty | 77** | 0.59 | 1.72 | 14.67 | 14.67 | 14.67 | 14.67 | 14.67 | 4.16 | 23.9 | 0 |
| inbound:GameLogin | 81** | 0.13 | 0.35 | 9.22 | 9.22 | 9.22 | 9.22 | 9.22 | 0.40 | 0.2 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.71 | 8.47 | 8.47 | 8.47 | 8.47 | 8.47 | 8.47 | 1.10 | 24.5 | 0 |
| timer:CreatureClockService | 277* | 0.03 | 1.26 | 1.77 | 7.51 | 7.51 | 7.51 | 7.51 | 3.82 | 1.0 | 0 |
| inbound:GameConnectionCheck | 1942947 | 0.00 | 0.00 | 0.02 | 0.04 | 7.29 | 0.13 | 2.59 | 7.29 | 269.8 | 0 |
| timer:JoustMaintenanceService | 277* | 0.24 | 0.43 | 0.46 | 7.25 | 7.25 | 0.15 | 0.46 | 7.25 | 276.9 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.16 | 0.60 | 7.00 | 7.00 | 7.00 | 7.00 | 7.00 | 0.76 | 0.2 | 0 |
| event:achievement.inventory | 210* | 0.34 | 0.84 | 3.16 | 6.92 | 6.92 | 6.92 | 6.92 | – | 0.3 | 0 |
| inbound:GameSetUserPosition | 45992 | 0.03 | 0.10 | 0.15 | 2.78 | 6.61 | 1.70 | 4.27 | 6.61 | 272.1 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.80 | 5.15 | 6.49 | 6.49 | 6.49 | 5.15 | 6.49 | – | 0.8 | 0 |
| timer:PremiumDailyRefreshService | 4** | 2.16 | 6.19 | 6.19 | 6.19 | 6.19 | 6.19 | 6.19 | 2.57 | 60.0 | 0 |
| event:quest.inventory | 210* | 0.12 | 0.47 | 1.77 | 4.91 | 4.91 | 4.91 | 4.91 | – | 0.3 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.77 | 4.31 | 4.31 | 4.31 | 4.31 | 2.04 | 2.04 | 4.31 | 180.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.05 | 4.08 | 4.08 | 4.08 | 4.08 | 4.08 | 0.08 | 0.1 | 0 |
| inbound:GameFinishLoading | 158* | 0.11 | 0.22 | 1.60 | 3.40 | 3.40 | 3.40 | 3.40 | 1.40 | 13.6 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.11 | 0.23 | 0.44 | 3.05 | 3.05 | 1.97 | 3.05 | 0.44 | 32.0 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.08 | 0.31 | 2.66 | 2.66 | 2.66 | 2.66 | 2.66 | 2.42 | 0.2 | 0 |
| event:altar.departure | 158* | 0.14 | 0.26 | 0.64 | 2.22 | 2.22 | 2.22 | 2.22 | 0.64 | 18.7 | 0 |
| timer:FatigueDailyRefreshService | 4** | 1.48 | 2.14 | 2.14 | 2.14 | 2.14 | 2.14 | 2.14 | 2.02 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.33 | 1.91 | 1.91 | 1.91 | 1.91 | 1.01 | 1.01 | 1.91 | 120.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.05 | 1.83 | 1.83 | 1.83 | 1.83 | 1.83 | 1.83 | 0.05 | 60.0 | 0 |
| event:quest.mirror-leave | 158* | 0.14 | 0.35 | 1.71 | 1.80 | 1.80 | 0.98 | 0.98 | 1.80 | 119.6 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.18 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 0.40 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.05 | 0.08 | 0.52 | 1.35 | 1.35 | 1.35 | 0.17 | 0.2 | 0 |
| event:tournament.departure | 158* | 0.12 | 0.20 | 0.23 | 1.17 | 1.17 | 0.21 | 0.21 | 1.17 | 123.8 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.03 | 0.07 | 1.10 | 1.10 | 1.10 | 1.10 | 0.10 | 0.5 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.02 | 0.86 | 0.86 | 0.86 | 0.86 | 0.86 | 0.86 | 0.04 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.02 | 0.83 | 0.83 | 0.83 | 0.83 | 0.83 | 0.83 | 0.13 | 60.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.02 | 0.82 | 0.82 | 0.82 | 0.82 | 0.82 | 0.82 | 0.03 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.05 | 0.78 | 0.78 | 0.78 | 0.78 | 0.26 | 0.5 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.02 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.12 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.02 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.05 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5535 | 0.01 | 0.01 | 0.03 | 0.11 | 0.46 | 0.46 | 0.46 | 0.26 | 0.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.42 | 0.42 | 0.42 | 0.42 | 0.42 | 0.42 | 0.04 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 158* | 0.00 | 0.02 | 0.04 | 0.06 | 0.06 | 0.06 | 0.06 | 0.04 | 13.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 21.48 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25413 | 0.01 | 0.84 | 41.72 | 97.07 | 233.8 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22915 | 0.01 | 0.27 | 33.19 | 87.15 | 115.5 |  |  |
| 稳态窗 | 全部作业 | 52101 | 0.01 | 0.22 | 20.97 | 46.48 | 61.22 | inbound:GameConnectionCheck | 124.5 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.19 | 16.47 | 46.62 | 61.22 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 872* | 0.04 | 1.86 | 17.32 | 89.91 | 89.91 | timer:TournamentRewardSweepService | 212.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 613* | 0.06 | 0.92 | 17.33 | 17.35 | 17.35 |  |  |
| (e) 灌包期间 | 全部作业 | 1893976 | 0.00 | 0.00 | 0.01 | 0.05 | 39.80 | inbound:GameConnectionCheck | 269.5 |
| (e) 灌包期间 | 只算 inbound: | 1892981 | 0.00 | 0.00 | 0.01 | 0.05 | 39.80 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 117 | 233.5 | 233.8 | event:achievement.inventory | 232.9 |
| 1 | 273 | 86.66 | 94.84 | inbound:GameConnectionCheck | 95.15 |
| 2 | 329 | 94.95 | 115.5 | event:quest.inventory | 115.0 |
| 3 | 328 | 105.6 | 117.8 | event:quest.inventory | 117.3 |
| 4 | 497 | 113.7 | 115.8 | event:quest.inventory | 115.2 |
| 5 | 431 | 0.09 | 0.16 | timer:IllusionTowerReturnSweepService | 0.09 |
| 6 | 432 | 0.34 | 0.77 | inbound:GameConnectionCheck | 0.74 |
| 7 | 431 | 0.30 | 0.39 | timer:IllusionTowerReturnSweepService | 2.42 |
| 8 | 432 | 15.14 | 16.64 | inbound:GameSetUserPosition | 17.33 |
| 9 | 435 | 2.81 | 4.80 | inbound:GameSetUserPosition | 7.27 |
| 10 | 428 | 0.21 | 0.29 | inbound:GameSetUserPosition | 4.27 |
| 11 | 431 | 0.24 | 1.86 | inbound:GameConnectionCheck | 1.84 |
| 12 | 432 | 0.34 | 3.46 | inbound:GameConnectionCheck | 3.43 |
| 13 | 433 | 63.41 | 74.44 | inbound:GameSetUserPosition | 75.26 |
| 14 | 433 | 2.54 | 8.29 | inbound:GameSetUserPosition | 12.65 |
| 15 | 440 | 18.49 | 27.30 | inbound:GameConnectionCheck | 29.87 |
| 16 | 426 | 0.30 | 0.34 | inbound:GameSetUserPosition | 0.39 |
| 17 | 431 | 0.18 | 1.05 | inbound:GameConnectionCheck | 1.00 |
| 18 | 430 | 43.41 | 50.52 | inbound:GameConnectionCheck | 52.16 |
| 19 | 454 | 35.07 | 37.60 | timer:DungeonPartyWipeService | 45.58 |
| 20 | 439 | 39.61 | 41.95 | inbound:GameSetUserPosition | 49.40 |
| 21 | 445 | 45.29 | 54.85 | inbound:GameConnectionCheck | 51.76 |
| 22 | 432 | 0.28 | 0.34 | inbound:GameConnectionCheck | 1.88 |
| 23 | 434 | 0.37 | 7.74 | timer:TournamentRewardSweepService | 14.67 |
| 24 | 430 | 1.28 | 7.89 | inbound:GameConnectionCheck | 8.47 |
| 25 | 431 | 0.28 | 1.72 | inbound:GameSetUserPosition | 1.60 |
| 26 | 432 | 0.31 | 3.54 | inbound:GameSetUserPosition | 1.53 |
| 27 | 431 | 0.32 | 3.28 | inbound:GameConnectionCheck | 3.15 |
| 28 | 432 | 0.54 | 2.66 | inbound:GameSetUserPosition | 3.23 |
| 29 | 437 | 1.48 | 3.24 | inbound:GameConnectionCheck | 3.23 |
| 30 | 432 | 0.29 | 1.24 | inbound:GameSetUserPosition | 1.12 |
| 31 | 426 | 0.26 | 3.23 | timer:TournamentRewardSweepService | 3.05 |
| 32 | 432 | 0.25 | 0.54 | timer:TournamentRewardSweepService | 1.56 |
| 33 | 432 | 31.57 | 38.20 | inbound:GameSetUserPosition | 40.21 |
| 34 | 438 | 20.43 | 21.89 | timer:TournamentRewardSweepService | 31.57 |
| 35 | 428 | 10.64 | 21.17 | inbound:GameConnectionCheck | 22.15 |
| 36 | 434 | 11.98 | 17.29 | inbound:GameConnectionCheck | 17.31 |
| 37 | 431 | 0.19 | 0.40 | inbound:GameSetUserPosition | 0.37 |
| 38 | 444 | 16.94 | 23.26 | inbound:GameConnectionCheck | 24.40 |
| 39 | 440 | 31.04 | 40.44 | inbound:GameConnectionCheck | 41.76 |
| 40 | 439 | 35.21 | 37.70 | inbound:GameConnectionCheck | 43.70 |
| 41 | 445 | 70.41 | 71.74 | inbound:GameConnectionCheck | 54.99 |
| 42 | 434 | 0.21 | 6.55 | inbound:GameConnectionCheck | 1.21 |
| 43 | 429 | 0.28 | 0.36 | inbound:GameSetUserPosition | 1.67 |
| 44 | 432 | 0.25 | 0.90 | inbound:GameConnectionCheck | 0.89 |
| 45 | 431 | 0.20 | 0.43 | inbound:GameConnectionCheck | 3.52 |
| 46 | 432 | 0.23 | 0.29 | inbound:GameConnectionCheck | 1.11 |
| 47 | 431 | 1.62 | 3.22 | inbound:GameConnectionCheck | 3.10 |
| 48 | 432 | 0.31 | 2.94 | inbound:GameSetUserPosition | 0.98 |
| 49 | 431 | 0.29 | 0.63 | inbound:GameSetUserPosition | 2.53 |
| 50 | 432 | 0.21 | 0.35 | inbound:GameSetUserPosition | 0.95 |
| 51 | 431 | 0.28 | 2.12 | inbound:GameSetUserPosition | 3.08 |
| 52 | 433 | 10.55 | 14.32 | inbound:GameConnectionCheck | 23.49 |
| 53 | 433 | 33.23 | 42.03 | inbound:GameConnectionCheck | 33.54 |
| 54 | 432 | 0.33 | 0.97 | inbound:GameConnectionCheck | 0.94 |
| 55 | 432 | 20.67 | 23.05 | inbound:GameConnectionCheck | 29.24 |
| 56 | 432 | 0.28 | 0.60 | timer:TournamentRewardSweepService | 0.38 |
| 57 | 447 | 29.10 | 33.78 | timer:TournamentRewardSweepService | 42.00 |
| 58 | 432 | 0.19 | 0.25 | inbound:GameConnectionCheck | 0.18 |
| 59 | 450 | 36.94 | 41.86 | inbound:GameConnectionCheck | 43.12 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25413 | 4435 | 7.4% | inbound:GameSelectCharacter 1877 ms／21／max 232.9；inbound:GameEplpCommand 586 ms／15／max 54.99；inbound:GameSetUserPosition 418 ms／9612／max 4.27；inbound:GameEnterSelectDungeon 399 ms／18／max 64.18；inbound:GameSelectDungeon 312 ms／18／max 75.26；inbound:GameSetUserArea 164 ms／12／max 19.46；inbound:GameConnectionCheck 139 ms／13093／max 2.59；event:achievement.inventory 96 ms／210／max 6.92 |
| 稳态窗 | 120.0 | 52101 | 4519 | 3.8% | inbound:GameEplpCommand 1528 ms／37／max 61.12；inbound:GameSetUserPosition 964 ms／20297／max 4.45；inbound:GameEnterSelectDungeon 862 ms／34／max 38.65；inbound:GameSelectDungeon 507 ms／34／max 21.88；inbound:GameConnectionCheck 350 ms／27482／max 3.72；inbound:GameRequestPeer 89 ms／36／max 5.82；timer:CreatureClockService 40 ms／120／max 3.82；timer:JoustMaintenanceService 31 ms／120／max 0.46 |
| (d) 10 并发登录的轮内 | 1.2 | 872 | 58 | 4.8% | inbound:GameEnterSelectDungeon 17 ms／1／max 16.92；http:POST /api/launcher/v1/register 11 ms／60／max 0.76；inbound:GameSelectDungeon 10 ms／1／max 10.34；inbound:GameLogin 8 ms／60／max 0.40；http:POST /api/launcher/v1/login 7 ms／60／max 2.42；inbound:GameSetUserPosition 3 ms／243／max 0.08；inbound:GameConnectionCheck 1 ms／308／max 0.12；lifecycle:connected 1 ms／60／max 0.08 |
| (e) 灌包期间 | 30.0 | 1893976 | 8636 | 28.8% | inbound:GameConnectionCheck 8074 ms／1887719／max 7.29；inbound:GameEplpCommand 247 ms／8／max 39.79；inbound:GameSetUserPosition 110 ms／5197／max 5.17；inbound:GameEnterSelectDungeon 95 ms／7／max 17.89；inbound:GameSelectDungeon 78 ms／7／max 13.79；inbound:GameRequestPeer 6 ms／9／max 0.89；inbound:GameLeaveParty 4 ms／11／max 2.46；timer:JoustMaintenanceService 3 ms／30／max 0.15 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.8，`event:` 1.9。

