# 基线测量 — m3bg0（合成负载）

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
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/m1-base/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3800 ms

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
| CHECK_USER_CONNECTION | 26783 | 55.80% |
| SET_USER_POSITION | 20365 | 42.43% |
| STEP_FENCE | 568 | 1.18% |
| FINISH_LOADING | 70 | 0.15% |
| EPLP | 36 | 0.07% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26783 | 0.41 | 2.19 | 16.46 | 42.28 | 56.20 | 0 | 15.90 | 41.35 | 56.17 |
| SET_USER_POSITION | 20365 | 0.48 | 2.22 | 12.77 | 44.58 | 58.08 | 0 | 12.60 | 43.03 | 57.55 |
| FINISH_LOADING | 70** | 0.19 | 0.31 | 0.36 | 0.36 | 0.36 | 0 | 0.36 | 0.36 | 0.36 |
| EPLP | 36** | 41.34 | 58.42 | 59.63 | 59.63 | 59.63 | 0 | 59.07 | 59.07 | 59.07 |
| LEAVE_PARTY | 36** | 0.92 | 1.50 | 46.58 | 46.58 | 46.58 | 0 | 46.58 | 46.58 | 46.58 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.50 | 16.71 | 56.09 | 56.09 | 56.09 | 0 | 56.09 | 56.09 | 56.09 |
| ENTER_SELECT_DUNGEON | 35** | 27.12 | 39.34 | 40.01 | 40.01 | 40.01 | 0 | 39.81 | 39.81 | 39.81 |
| SELECT_DUNGEON | 35** | 16.26 | 20.06 | 21.73 | 21.73 | 21.73 | 0 | 21.73 | 21.73 | 21.73 |
| **全部计入的请求** | 47396 | 0.45 | 2.22 | 17.40 | 45.97 | 59.63 | 36 | 16.92 | 45.28 | 59.07 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47432 | 0.08 | 1.72 | 2.17 | 3.02 | 46.99（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.91 | 15.40 | 104.4 | 104.4 | 104.4 | 0.4 | 0 |
| EnterDungeonCommand | Transactional | 80** | 8.88 | 11.59 | 55.34 | 55.34 | 55.34 | 13.2 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.95 | 33.67 | 49.80 | 49.80 | 49.80 | 3.2 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 15.05 | 29.97 | 47.91 | 47.91 | 47.91 | 3.8 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.59 | 39.80 | 45.40 | 45.40 | 45.40 | 4.9 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 14.53 | 26.71 | 28.39 | 28.39 | 28.39 | 0.5 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.16 | 1.92 | 24.36 | 24.36 | 24.36 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.68 | 2.67 | 17.20 | 17.20 | 17.20 | 18.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.32 | 0.77 | 6.78 | 6.78 | 6.78 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.03 | 0.18 | 6.10 | 6.10 | 6.10 | 8.6 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 1.75 | 3.10 | 5.36 | 5.36 | 5.36 | 153.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.03 | 0.59 | 5.10 | 5.10 | 259.9 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.16 | 0.47 | 4.58 | 4.58 | 4.58 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.12 | 3.98 | 3.98 | 3.98 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.20 | 3.67 | 3.67 | 3.67 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.15 | 3.47 | 3.47 | 3.47 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.30 | 3.31 | 3.31 | 3.31 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337* | 0.08 | 0.33 | 0.43 | 3.14 | 3.14 | 1.0 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 84** | 0.02 | 0.09 | 2.94 | 2.94 | 2.94 | 59.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.10 | 2.79 | 2.79 | 2.79 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.15 | 2.38 | 2.38 | 2.38 | 0.2 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.03 | 1.68 | 1.68 | 1.68 | 1.68 | 60.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.63 | 1.63 | 1.63 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.33 | 1.62 | 1.62 | 1.62 | 0.3 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.06 | 1.51 | 1.51 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.10 | 1.48 | 1.48 | 1.48 | 0.5 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.07 | 1.35 | 1.35 | 1.35 | 22.4 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.05 | 0.12 | 0.92 | 0.92 | 0.92 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.15 | 0.70 | 0.70 | 0.70 | 0.3 | 0 |

命令日志行 1750 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**233.1 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo04；参考口径 233.1 ms）。冷启动窗整体：22535 | 0.45 | 2.39 | 37.74 | 96.73 | 233.1（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 25 | 233.1 | 233.1 | SELECT_CHARACTER |
| 1 | 116 | 86.45 | 95.59 | SELECT_CHARACTER |
| 2 | 171 | 105.1 | 115.3 | SELECT_CHARACTER |
| 3 | 183 | 116.1 | 120.8 | SELECT_CHARACTER |
| 4 | 328 | 109.9 | 111.2 | SELECT_CHARACTER |
| 5 | 400 | 2.10 | 3.09 | SET_USER_POSITION |
| 6 | 400 | 2.24 | 2.57 | SET_USER_POSITION |
| 7 | 400 | 2.37 | 2.68 | SET_USER_POSITION |
| 8 | 390 | 13.15 | 20.18 | SET_USER_POSITION |
| 9 | 393 | 2.50 | 4.14 | RESPONSE_PEER |
| 10 | 387 | 3.18 | 5.12 | SET_USER_POSITION |
| 11 | 400 | 2.44 | 5.16 | SET_USER_POSITION |
| 12 | 400 | 2.68 | 5.32 | SET_USER_POSITION |
| 13 | 384 | 73.06 | 74.18 | SELECT_DUNGEON |
| 14 | 392 | 11.76 | 12.08 | SELECT_DUNGEON |
| 15 | 384 | 16.61 | 27.88 | ENTER_SELECT_DUNGEON |
| 16 | 392 | 3.11 | 11.92 | SELECT_DUNGEON |
| 17 | 400 | 2.31 | 3.15 | CHECK_USER_CONNECTION |
| 18 | 396 | 47.72 | 51.86 | EPLP |
| 19 | 398 | 2.82 | 3.13 | CHECK_USER_CONNECTION |
| 20 | 398 | 46.71 | 49.51 | CHECK_USER_CONNECTION |
| 21 | 396 | 41.25 | 52.92 | EPLP |
| 22 | 398 | 12.06 | 14.87 | LEAVE_PARTY |
| 23 | 400 | 2.63 | 5.04 | CHECK_USER_CONNECTION |
| 24 | 396 | 2.52 | 4.89 | LEAVE_PARTY |
| 25 | 396 | 2.49 | 2.65 | CHECK_USER_CONNECTION |
| 26 | 393 | 2.61 | 4.14 | CHECK_USER_CONNECTION |
| 27 | 400 | 2.20 | 3.60 | CHECK_USER_CONNECTION |
| 28 | 395 | 2.65 | 4.27 | SET_USER_POSITION |
| 29 | 395 | 2.36 | 2.41 | SET_USER_POSITION |
| 30 | 395 | 2.71 | 2.84 | SET_USER_POSITION |
| 31 | 382 | 36.26 | 40.03 | ENTER_SELECT_DUNGEON |
| 32 | 400 | 2.38 | 3.66 | SET_USER_POSITION |
| 33 | 392 | 23.22 | 36.02 | ENTER_SELECT_DUNGEON |
| 34 | 400 | 2.78 | 3.35 | SET_USER_POSITION |
| 35 | 386 | 17.04 | 26.33 | ENTER_SELECT_DUNGEON |
| 36 | 382 | 20.49 | 23.01 | ENTER_SELECT_DUNGEON |
| 37 | 398 | 21.04 | 24.78 | EPLP |
| 38 | 398 | 21.18 | 33.51 | EPLP |
| 39 | 398 | 33.70 | 43.06 | EPLP |
| 40 | 398 | 45.25 | 47.82 | EPLP |
| 41 | 396 | 39.56 | 45.42 | EPLP |
| 42 | 396 | 2.24 | 2.37 | SET_USER_POSITION |
| 43 | 400 | 2.40 | 2.86 | CHECK_USER_CONNECTION |
| 44 | 398 | 2.44 | 2.54 | SET_USER_POSITION |
| 45 | 398 | 4.31 | 5.21 | CHECK_USER_CONNECTION |
| 46 | 396 | 3.05 | 4.99 | CHECK_USER_CONNECTION |
| 47 | 395 | 2.61 | 5.57 | SET_USER_POSITION |
| 48 | 395 | 3.23 | 6.08 | SET_USER_POSITION |
| 49 | 396 | 2.55 | 2.91 | SET_USER_POSITION |
| 50 | 399 | 2.52 | 3.44 | SET_USER_POSITION |
| 51 | 386 | 3.00 | 3.68 | CHECK_USER_CONNECTION |
| 52 | 391 | 28.20 | 29.26 | ENTER_SELECT_DUNGEON |
| 53 | 392 | 23.06 | 35.84 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 2.16 | 3.16 | CHECK_USER_CONNECTION |
| 55 | 384 | 23.09 | 32.76 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.27 | 2.60 | SET_USER_POSITION |
| 57 | 382 | 19.92 | 25.08 | EPLP |
| 58 | 398 | 2.33 | 2.69 | CHECK_USER_CONNECTION |
| 59 | 398 | 28.85 | 43.15 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47396 | 0.45 | 2.22 | 17.40 | 45.97 | 59.63 |
| 10 并发登录的轮内 | 462* | 15.64 | 73.48 | 86.32 | 93.63 | 93.63 |
| （参考：实际发出口径）稳态窗 | 47396 | 0.23 | 0.69 | 16.92 | 45.28 | 59.07 |
| （参考：实际发出口径）轮内 | 462* | 4.61 | 69.70 | 83.83 | 90.99 | 90.99 |
| 登录阶段整体（182–232 s） | 19823 | 0.47 | 2.50 | 38.19 | 74.75 | 93.63 |

每轮时长（开始 → 10 个 LOGIN 都应答）：201, 224, 218, 193, 214, 181 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.40 | 1.94 | 16.70 | 41.64 | 58.08 | 16.24 |
| 单人 | 轮内 | 189* | 15.13 | 70.11 | 85.26 | 86.32 | 86.32 | 81.74 |
| 双人组领队 | 稳态窗 | 14080 | 0.48 | 2.38 | 22.10 | 48.43 | 59.63 | 21.53 |
| 双人组领队 | 轮内 | 138* | 15.64 | 76.66 | 91.73 | 93.59 | 93.59 | 90.89 |
| 双人组队员 | 稳态窗 | 14116 | 0.45 | 2.27 | 14.85 | 45.27 | 56.09 | 14.10 |
| 双人组队员 | 轮内 | 135* | 17.48 | 75.01 | 91.67 | 93.63 | 93.63 | 90.74 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 101.0 | 115.0 | 126.3 | 126.3 | 126.3 |
| 启动器登录（HTTP，含校验） | 60** | 91.99 | 113.4 | 115.6 | 115.6 | 115.6 |
| Game LOGIN 0x0001 | 60** | 0.29 | 11.55 | 30.61 | 30.61 | 30.61 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 77335 包/秒（2320481 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47396 | 0.45 | 2.22 | 17.40 | 45.97 | 59.63 |
| 其余连接，灌包期间 | 11858 | 0.51 | 5.49 | 8.47 | 30.06 | 44.19 |
| （参考：实际发出口径）其余连接，灌包期间 | 11858 | 0.10 | 5.06 | 8.11 | 30.06 | 43.73 |
| 灌包连接自身（含其窗口内排队） | 2320512 | 0.28 | 0.33 | 5.44 | 7.90 | 44.23 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3775 | leave dungeon | 62.1 |
| EPLP | 请求者 | 78 | 3703 | leave dungeon | 62.1 |
| SET_USER_AREA | 同伴 | 6 | 3141 | town | 4.9 |
| SET_USER_AREA | 请求者 | 12 | 2890 | town | 4.9 |
| RESPONSE_PEER | 请求者 | 82 | 2038 | party | 165.9 |
| LEAVE_PARTY | 同伴 | 78 | 1925 | leave party | 62.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 1890 | party dungeon entry | 197.5 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 1890 | party dungeon entry | 197.5 |
| RESPONSE_PEER | 同伴 | 82 | 1880 | party | 165.9 |
| LEAVE_PARTY | 请求者 | 78 | 1814 | leave party | 62.1 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.1 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.1 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| REQUEST_PEER | 同伴 | 82 | 227 | party | 133.1 |
| REQUEST_PEER | 请求者 | 82 | 191 | party | 92.1 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.3 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 13.2 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 13.2 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 17.40 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 86.32 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 8.47 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 233.1 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 104.4 ms，max 104.4 ms（出现于 t=0.4 s；冷启动窗内 max 104.4 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 1 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）1 个，协议无回包 82 个。

失败的命令 0 条：

