# 基线测量 — c2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1429 ms

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
| CHECK_USER_CONNECTION | 26882 | 56.00% |
| SET_USER_POSITION | 20266 | 42.22% |
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
| CHECK_USER_CONNECTION | 26882 | 0.43 | 1.75 | 2.47 | 4.21 | 36.30 | 0 | 0.28 | 2.07 | 5.85 |
| SET_USER_POSITION | 20266 | 0.43 | 1.87 | 2.83 | 18.95 | 35.80 | 0 | 0.63 | 18.29 | 34.63 |
| FINISH_LOADING | 70** | 0.14 | 0.20 | 0.27 | 0.27 | 0.27 | 0 | 0.27 | 0.27 | 0.27 |
| EPLP | 36** | 17.82 | 20.96 | 22.57 | 22.57 | 22.57 | 0 | 20.81 | 20.81 | 20.81 |
| LEAVE_PARTY | 36** | 0.72 | 1.24 | 1.31 | 1.31 | 1.31 | 0 | 1.31 | 1.31 | 1.31 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.35 | 0.66 | 2.62 | 2.62 | 2.62 | 0 | 2.62 | 2.62 | 2.62 |
| ENTER_SELECT_DUNGEON | 35** | 29.78 | 39.47 | 41.45 | 41.45 | 41.45 | 0 | 40.54 | 40.54 | 40.54 |
| SELECT_DUNGEON | 35** | 16.27 | 20.31 | 21.42 | 21.42 | 21.42 | 0 | 21.42 | 21.42 | 21.42 |
| **全部计入的请求** | 47396 | 0.43 | 1.82 | 2.73 | 20.58 | 41.45 | 36 | 0.56 | 19.90 | 40.54 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47432 | 0.25 | 1.58 | 2.25 | 3.62 | 35.09（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| EnterDungeonCommand | Transactional | 78** | 9.72 | 434.3 | 1103.9 | 1103.9 | 1103.9 | 15.2 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.72 | 3.96 | 697.2 | 697.2 | 697.2 | 203.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 282.8 | 659.2 | 668.0 | 668.0 | 668.0 | 192.7 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 625.3 | 630.1 | 631.7 | 631.7 | 631.7 | 182.7 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.03 | 0.29 | 563.8 | 613.0 | 613.0 | 192.7 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 187.4 | 561.3 | 567.2 | 567.2 | 567.2 | 223.3 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 3.13 | 3.98 | 520.2 | 520.2 | 520.2 | 0.8 | 0 |
| AdvanceCreatureClockCommand | Transactional | 333* | 0.14 | 0.49 | 22.19 | 280.2 | 280.2 | 15.2 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.22 | 5.43 | 68.57 | 68.57 | 68.57 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.47 | 0.98 | 42.49 | 42.49 | 42.49 | 0.3 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 20.33 | 37.18 | 41.91 | 41.91 | 41.91 | 2.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 21.32 | 39.24 | 41.71 | 41.71 | 41.71 | 2.2 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.53 | 34.32 | 36.46 | 36.46 | 36.46 | 1.4 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.84 | 29.72 | 35.89 | 35.89 | 35.89 | 1.9 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.47 | 0.60 | 6.56 | 6.56 | 6.56 | 0.3 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.32 | 0.75 | 6.11 | 6.11 | 6.11 | 0.3 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.04 | 0.13 | 4.99 | 4.99 | 0.2 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.04 | 0.22 | 4.57 | 4.57 | 4.57 | 9.3 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 2.28 | 2.72 | 4.46 | 4.46 | 4.46 | 9.3 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.26 | 3.88 | 3.88 | 3.88 | 0.9 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.34 | 0.57 | 3.57 | 3.57 | 3.57 | 0.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.06 | 0.10 | 3.20 | 3.20 | 3.20 | 1.0 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.06 | 3.09 | 3.09 | 3.09 | 3.09 | 60.0 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.65 | 0.84 | 2.53 | 2.53 | 2.53 | 1.0 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.54 | 1.00 | 1.45 | 1.45 | 1.45 | 1.0 | 0 |
| LeavePartyCommand | Direct | 78** | 0.05 | 0.13 | 1.15 | 1.15 | 1.15 | 24.5 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.05 | 0.11 | 0.89 | 0.89 | 0.89 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.28 | 0.45 | 0.73 | 0.73 | 0.73 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 156* | 0.01 | 0.02 | 0.32 | 0.67 | 0.67 | 14.2 | 0 |

命令日志行 1761 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**1118.5 ms**（SELECT_DUNGEON，计划发出 t=14.12 s，pair00L；参考口径 1118.5 ms）。冷启动窗整体：22265 | 0.42 | 1.88 | 4.20 | 66.45 | 1118.5（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 4 | 597.3 | 597.3 | SELECT_CHARACTER |
| 1 | 55 | 13.87 | 13.87 | CHECK_USER_CONNECTION |
| 2 | 117 | 6.96 | 7.31 | CHECK_USER_CONNECTION |
| 3 | 172 | 7.32 | 7.35 | SET_USER_POSITION |
| 4 | 225 | 9.96 | 18.07 | SET_USER_AREA |
| 5 | 378 | 15.43 | 20.40 | SET_USER_AREA |
| 6 | 400 | 2.53 | 2.57 | SET_USER_POSITION |
| 7 | 400 | 2.02 | 2.44 | CHECK_USER_CONNECTION |
| 8 | 400 | 2.48 | 2.77 | SET_USER_POSITION |
| 9 | 390 | 2.56 | 10.02 | RESPONSE_PEER |
| 10 | 381 | 1.98 | 2.64 | CHECK_USER_CONNECTION |
| 11 | 399 | 2.08 | 2.63 | CHECK_USER_CONNECTION |
| 12 | 400 | 2.38 | 2.81 | SET_USER_POSITION |
| 13 | 400 | 2.03 | 4.33 | SET_USER_POSITION |
| 14 | 382 | 739.4 | 1118.5 | SELECT_DUNGEON |
| 15 | 396 | 90.09 | 190.0 | CHECK_USER_CONNECTION |
| 16 | 374 | 5.96 | 14.64 | SELECT_DUNGEON |
| 17 | 400 | 2.23 | 2.64 | CHECK_USER_CONNECTION |
| 18 | 400 | 2.51 | 3.03 | CHECK_USER_CONNECTION |
| 19 | 398 | 2.83 | 48.01 | EPLP |
| 20 | 396 | 2.40 | 19.61 | EPLP |
| 21 | 396 | 16.86 | 29.52 | SET_USER_POSITION |
| 22 | 398 | 15.35 | 31.08 | SET_USER_POSITION |
| 23 | 400 | 2.62 | 3.03 | SET_USER_POSITION |
| 24 | 398 | 2.38 | 16.11 | LEAVE_PARTY |
| 25 | 396 | 2.94 | 4.24 | LEAVE_PARTY |
| 26 | 395 | 2.54 | 2.68 | CHECK_USER_CONNECTION |
| 27 | 399 | 5.10 | 5.75 | CHECK_USER_CONNECTION |
| 28 | 400 | 2.43 | 2.91 | SET_USER_POSITION |
| 29 | 395 | 2.48 | 2.53 | SET_USER_POSITION |
| 30 | 390 | 2.08 | 2.74 | CHECK_USER_CONNECTION |
| 31 | 390 | 2.00 | 2.07 | SET_USER_POSITION |
| 32 | 395 | 2.78 | 3.01 | CHECK_USER_CONNECTION |
| 33 | 400 | 2.51 | 2.82 | CHECK_USER_CONNECTION |
| 34 | 399 | 20.19 | 38.84 | ENTER_SELECT_DUNGEON |
| 35 | 385 | 14.95 | 38.91 | ENTER_SELECT_DUNGEON |
| 36 | 372 | 15.14 | 22.37 | SELECT_DUNGEON |
| 37 | 396 | 2.37 | 2.81 | CHECK_USER_CONNECTION |
| 38 | 400 | 2.10 | 2.41 | CHECK_USER_CONNECTION |
| 39 | 396 | 2.67 | 20.35 | EPLP |
| 40 | 396 | 2.81 | 20.92 | EPLP |
| 41 | 396 | 16.48 | 31.38 | SET_USER_POSITION |
| 42 | 400 | 2.51 | 2.89 | SET_USER_POSITION |
| 43 | 398 | 2.23 | 2.42 | SET_USER_POSITION |
| 44 | 396 | 2.67 | 4.73 | SET_USER_POSITION |
| 45 | 400 | 2.59 | 4.65 | CHECK_USER_CONNECTION |
| 46 | 396 | 2.40 | 2.71 | SET_USER_POSITION |
| 47 | 398 | 2.10 | 2.47 | SET_USER_POSITION |
| 48 | 393 | 2.31 | 2.53 | CHECK_USER_CONNECTION |
| 49 | 392 | 2.60 | 3.05 | SET_USER_POSITION |
| 50 | 400 | 2.24 | 4.80 | CHECK_USER_CONNECTION |
| 51 | 390 | 2.25 | 2.58 | CHECK_USER_CONNECTION |
| 52 | 397 | 2.89 | 40.24 | ENTER_SELECT_DUNGEON |
| 53 | 390 | 4.17 | 4.87 | CHECK_USER_CONNECTION |
| 54 | 386 | 17.21 | 33.80 | ENTER_SELECT_DUNGEON |
| 55 | 390 | 2.57 | 26.45 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.19 | 3.09 | SET_USER_POSITION |
| 57 | 390 | 12.25 | 25.08 | SELECT_DUNGEON |
| 58 | 390 | 3.64 | 22.62 | ENTER_SELECT_DUNGEON |
| 59 | 400 | 2.14 | 5.06 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47396 | 0.43 | 1.82 | 2.73 | 20.58 | 41.45 |
| 10 并发登录的轮内 | 3072 | 0.28 | 1.75 | 2.95 | 566.5 | 705.0 |
| （参考：实际发出口径）稳态窗 | 47396 | 0.18 | 0.28 | 0.56 | 19.90 | 40.54 |
| （参考：实际发出口径）轮内 | 3072 | 0.09 | 0.14 | 0.19 | 13.83 | 703.2 |
| 登录阶段整体（182–233 s） | 20283 | 0.39 | 1.78 | 2.77 | 55.76 | 705.0 |

每轮时长（开始 → 10 个 LOGIN 都应答）：1297, 1303, 1288, 1285, 1291, 1286 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.24 | 1.84 | 2.48 | 5.44 | 21.61 | 0.33 |
| 单人 | 轮内 | 1241 | 0.13 | 1.74 | 2.34 | 2.95 | 5.06 | 0.19 |
| 双人组领队 | 稳态窗 | 14080 | 0.63 | 1.83 | 10.38 | 34.56 | 41.45 | 9.26 |
| 双人组领队 | 轮内 | 912* | 0.47 | 1.91 | 316.8 | 705.0 | 705.0 | 0.82 |
| 双人组队员 | 稳态窗 | 14116 | 0.56 | 1.76 | 2.70 | 16.52 | 35.80 | 0.58 |
| 双人组队员 | 轮内 | 919* | 0.38 | 1.59 | 2.47 | 3.79 | 3.79 | 0.18 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 354.4 | 659.8 | 668.6 | 668.6 | 668.6 |
| 启动器登录（HTTP，含校验） | 60** | 626.7 | 630.7 | 632.1 | 632.1 | 632.1 |
| Game LOGIN 0x0001 | 60** | 255.5 | 561.5 | 567.4 | 567.4 | 567.4 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 195702 包/秒（5871065 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47396 | 0.43 | 1.82 | 2.73 | 20.58 | 41.45 |
| 其余连接，灌包期间 | 11875 | 0.30 | 1.67 | 2.71 | 12.09 | 28.08 |
| （参考：实际发出口径）其余连接，灌包期间 | 11875 | 0.07 | 0.13 | 0.43 | 11.42 | 27.14 |
| 灌包连接自身（含其窗口内排队） | 5871096 | 0.06 | 0.10 | 0.13 | 1.01 | 16.94 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39291 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3614 | leave dungeon | 185.7 |
| EPLP | 请求者 | 78 | 3542 | leave dungeon | 247.6 |
| SET_USER_AREA | 同伴 | 6 | 3012 | town | 5.5 |
| SET_USER_AREA | 请求者 | 12 | 2857 | town | 5.5 |
| SELECT_DUNGEON | 请求者 | 78 | 1367 | party dungeon entry | 14.1 |
| SELECT_DUNGEON | 同伴 | 78 | 1367 | party dungeon entry | 14.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 78 | 728 | party dungeon entry | 52.9 |
| ENTER_SELECT_DUNGEON | 同伴 | 78 | 728 | party dungeon entry | 152.3 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| LEAVE_PARTY | 同伴 | 78 | 331 | leave party | 24.4 |
| LEAVE_PARTY | 请求者 | 78 | 268 | leave party | 24.4 |
| RESPONSE_PEER | 同伴 | 82 | 262 | party | 9.3 |
| RESPONSE_PEER | 请求者 | 82 | 260 | party | 90.2 |
| CREATE_CHARACTER | 请求者 | 21 | 137 | create | 0.2 |
| REQUEST_PEER | 同伴 | 82 | 76 | party | 9.3 |
| REQUEST_PEER | 请求者 | 82 | 48 | party | 9.3 |
| FINISH_LOADING | 请求者 | 156 | 36 | party dungeon entry | 14.2 |
| FINISH_LOADING | 同伴 | 156 | 0 | party dungeon entry | 14.2 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 2.73 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 2.95 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 2.71 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 8 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 1118.5 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `EnterDungeonCommand`：样本 78，p99.9 1103.9 ms，max 1103.9 ms（出现于 t=15.2 s；冷启动窗内 max 1103.9 ms，之后 max 590.8 ms）
- `LeaveDungeonCommand`：样本 78，p99.9 697.2 ms，max 697.2 ms（出现于 t=203.3 s；冷启动窗内 max 19.14 ms，之后 max 697.2 ms）
- `RegisterAccountCommand`：样本 81，p99.9 668.0 ms，max 668.0 ms（出现于 t=192.7 s；冷启动窗内 max 76.23 ms，之后 max 668.0 ms）
- `AuthenticateAndIssueTicketCommand`：样本 81，p99.9 631.7 ms，max 631.7 ms（出现于 t=182.7 s；冷启动窗内 max 68.61 ms，之后 max 631.7 ms）
- `ExpireCharacterNameTagCommand`：样本 105，p99.9 613.0 ms，max 613.0 ms（出现于 t=192.7 s；冷启动窗内 max 1.54 ms，之后 max 613.0 ms）
- `RedeemGameLoginCommand`：样本 81，p99.9 567.2 ms，max 567.2 ms（出现于 t=223.3 s；冷启动窗内 max 1.70 ms，之后 max 567.2 ms）
- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 520.2 ms，max 520.2 ms（出现于 t=0.8 s；冷启动窗内 max 520.2 ms，之后 max – ms）
- `AdvanceCreatureClockCommand`：样本 333，p99.9 280.2 ms，max 280.2 ms（出现于 t=15.2 s；冷启动窗内 max 280.2 ms，之后 max 0.57 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 78 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：27 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。
- CHECK_USER_CONNECTION：17 个（其中截尾 0），最大 839 ms，计划发出时刻 14.4, 14.4, 14.5, 14.5, 14.6, 14.6, 14.7, 14.7, 14.8, 14.8, 14.9, 14.9 … s
- SELECT_DUNGEON：4 个（其中截尾 0），最大 1119 ms，计划发出时刻 14.1, 14.8, 192.9, 222.5 s
- SET_USER_POSITION：4 个（其中截尾 0），最大 566 ms，计划发出时刻 202.7, 202.8, 202.9, 203.0 s
- SELECT_CHARACTER：1 个（其中截尾 0），最大 597 ms，计划发出时刻 0.3 s
- EPLP：1 个（其中截尾 0），最大 705 ms，计划发出时刻 202.6 s

失败的命令 0 条：

