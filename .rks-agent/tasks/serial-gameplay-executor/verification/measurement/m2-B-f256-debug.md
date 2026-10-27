# 基线测量 — m2bfd（合成负载）

> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。

## 环境

- 机器：Apple M5，10 逻辑核，内存 32 GiB，macOS 27.0.1
- 磁盘（数据库所在卷 `/`，临时目录 `/var/folders/qc/sl_p_wxs2qvg3pn_zknp1ppw0000gn/T/`）：设备 /dev/disk3s5，文件系统 APFS，Internal，Apple Fabric，Solid State: Yes（`diskutil info /`：Solid State:               Yes）
- .NET 运行时（工具进程）：.NET 10.0.10
- Host：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，构建配置 Release，Host.dll 修改时间 2026-10-23T11:20:51Z
- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），额外设置 `Logging__Console__FormatterOptions__SingleLine=true`
- SQLite：版本 3.53.4；`PRAGMA journal_mode` = **wal**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；`PRAGMA synchronous` = **2**（FULL；连接级，取值见下）
  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：
  - Host 原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 4081 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 256）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48000 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26773 | 55.78% |
| SET_USER_POSITION | 20378 | 42.45% |
| STEP_FENCE | 566 | 1.18% |
| FINISH_LOADING | 70 | 0.15% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| EPLP | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26773 | 0.41 | 2.07 | 12.21 | 41.31 | 62.60 | 0 | 11.54 | 41.03 | 62.60 |
| SET_USER_POSITION | 20378 | 0.49 | 2.20 | 12.75 | 46.03 | 64.32 | 0 | 11.44 | 45.95 | 62.61 |
| FINISH_LOADING | 70** | 0.18 | 0.37 | 11.37 | 11.37 | 11.37 | 0 | 11.37 | 11.37 | 11.37 |
| LEAVE_PARTY | 36** | 0.80 | 1.30 | 3.58 | 3.58 | 3.58 | 0 | 3.58 | 3.58 | 3.58 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.48 | 0.94 | 2.43 | 2.43 | 2.43 | 0 | 2.43 | 2.43 | 2.43 |
| EPLP | 35** | 39.54 | 58.31 | 67.51 | 67.51 | 67.51 | 0 | 67.35 | 67.35 | 67.35 |
| ENTER_SELECT_DUNGEON | 35** | 19.48 | 36.38 | 37.28 | 37.28 | 37.28 | 0 | 37.28 | 37.28 | 37.28 |
| SELECT_DUNGEON | 35** | 15.26 | 23.38 | 26.73 | 26.73 | 26.73 | 0 | 26.73 | 26.73 | 26.73 |
| **全部计入的请求** | 47398 | 0.45 | 2.16 | 14.96 | 46.63 | 67.51 | 36 | 14.49 | 46.14 | 67.35 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47434 | 0.16 | 1.67 | 2.20 | 4.97 | 26.98（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.82 | 22.08 | 101.6 | 101.6 | 101.6 | 0.4 | 0 |
| EnterDungeonCommand | Transactional | 78** | 8.52 | 13.58 | 55.97 | 55.97 | 55.97 | 13.5 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.12 | 32.94 | 54.99 | 54.99 | 54.99 | 3.1 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.71 | 39.95 | 43.93 | 43.93 | 43.93 | 4.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 15.13 | 23.30 | 27.32 | 27.32 | 27.32 | 0.5 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.42 | 21.79 | 23.35 | 23.35 | 23.35 | 3.1 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.14 | 2.09 | 22.88 | 22.88 | 22.88 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.61 | 2.75 | 16.84 | 16.84 | 16.84 | 18.6 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.23 | 0.59 | 4.96 | 4.96 | 4.96 | 0.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.13 | 0.45 | 4.47 | 4.47 | 4.47 | 0.2 | 0 |
| AcceptPartyInviteCommand | Direct | 81** | 0.03 | 0.23 | 3.88 | 3.88 | 3.88 | 8.6 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.12 | 3.88 | 3.88 | 3.88 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.02 | 0.26 | 3.60 | 3.60 | 3.60 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.29 | 3.41 | 3.41 | 3.41 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 81** | 1.66 | 2.73 | 3.33 | 3.33 | 3.33 | 8.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.16 | 3.32 | 3.32 | 3.32 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 333* | 0.09 | 0.35 | 0.41 | 3.16 | 3.16 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.17 | 2.86 | 2.86 | 2.86 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.13 | 2.25 | 2.25 | 2.25 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.72 | 1.61 | 1.61 | 1.61 | 0.3 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.05 | 1.60 | 1.60 | 1.60 | 1.60 | 60.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.59 | 1.59 | 1.59 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.08 | 1.53 | 1.53 | 1.53 | 0.5 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.04 | 1.47 | 1.47 | 0.2 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.09 | 0.38 | 1.30 | 1.30 | 16.3 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.06 | 1.18 | 1.18 | 1.18 | 23.7 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.46 | 0.70 | 0.70 | 0.70 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 156* | 0.01 | 0.03 | 0.30 | 0.61 | 0.61 | 13.6 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.14 | 0.57 | 0.57 | 0.57 | 60.0 | 0 |

命令日志行 1759 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**229.2 ms**（SELECT_CHARACTER，计划发出 t=0.29 s，solo04；参考口径 229.2 ms）。冷启动窗整体：22553 | 0.45 | 2.42 | 36.64 | 84.56 | 229.2（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 25 | 229.2 | 229.2 | SELECT_CHARACTER |
| 1 | 115 | 85.20 | 94.28 | SELECT_CHARACTER |
| 2 | 172 | 107.3 | 112.8 | SELECT_CHARACTER |
| 3 | 191 | 121.4 | 121.4 | SELECT_CHARACTER |
| 4 | 336 | 90.98 | 96.22 | SELECT_CHARACTER |
| 5 | 400 | 2.31 | 2.50 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.25 | 3.38 | SET_USER_POSITION |
| 7 | 400 | 2.46 | 3.58 | SET_USER_POSITION |
| 8 | 394 | 8.50 | 12.32 | RESPONSE_PEER |
| 9 | 382 | 2.46 | 3.78 | RESPONSE_PEER |
| 10 | 394 | 4.70 | 7.41 | SET_USER_POSITION |
| 11 | 400 | 2.37 | 3.61 | SET_USER_POSITION |
| 12 | 400 | 2.49 | 3.75 | CHECK_USER_CONNECTION |
| 13 | 386 | 73.16 | 83.36 | SELECT_DUNGEON |
| 14 | 390 | 2.57 | 7.17 | SET_USER_POSITION |
| 15 | 380 | 22.52 | 28.45 | ENTER_SELECT_DUNGEON |
| 16 | 396 | 2.75 | 2.77 | CHECK_USER_CONNECTION |
| 17 | 400 | 2.43 | 4.39 | CHECK_USER_CONNECTION |
| 18 | 396 | 38.77 | 50.72 | EPLP |
| 19 | 398 | 31.68 | 37.58 | EPLP |
| 20 | 398 | 33.95 | 47.73 | EPLP |
| 21 | 396 | 47.20 | 53.09 | EPLP |
| 22 | 400 | 4.74 | 7.16 | CHECK_USER_CONNECTION |
| 23 | 396 | 5.58 | 10.19 | LEAVE_PARTY |
| 24 | 398 | 2.36 | 4.77 | SET_USER_POSITION |
| 25 | 396 | 3.02 | 3.31 | CHECK_USER_CONNECTION |
| 26 | 398 | 2.55 | 5.71 | CHECK_USER_CONNECTION |
| 27 | 400 | 3.00 | 5.35 | CHECK_USER_CONNECTION |
| 28 | 390 | 2.61 | 4.34 | SET_USER_POSITION |
| 29 | 395 | 2.59 | 3.44 | CHECK_USER_CONNECTION |
| 30 | 392 | 2.44 | 6.25 | CHECK_USER_CONNECTION |
| 31 | 393 | 2.75 | 5.02 | CHECK_USER_CONNECTION |
| 32 | 400 | 2.49 | 4.13 | SET_USER_POSITION |
| 33 | 384 | 24.04 | 32.82 | ENTER_SELECT_DUNGEON |
| 34 | 392 | 19.32 | 30.80 | ENTER_SELECT_DUNGEON |
| 35 | 392 | 12.59 | 15.80 | ENTER_SELECT_DUNGEON |
| 36 | 384 | 16.42 | 23.49 | ENTER_SELECT_DUNGEON |
| 37 | 400 | 2.30 | 2.52 | SET_USER_POSITION |
| 38 | 394 | 33.24 | 39.13 | EPLP |
| 39 | 400 | 2.22 | 2.67 | CHECK_USER_CONNECTION |
| 40 | 398 | 30.74 | 44.19 | EPLP |
| 41 | 396 | 47.30 | 53.18 | EPLP |
| 42 | 396 | 2.56 | 4.10 | CHECK_USER_CONNECTION |
| 43 | 400 | 2.49 | 2.69 | SET_USER_POSITION |
| 44 | 398 | 2.54 | 2.84 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.51 | 4.10 | SET_USER_POSITION |
| 46 | 396 | 2.61 | 8.25 | SET_USER_POSITION |
| 47 | 390 | 2.51 | 2.66 | SET_USER_POSITION |
| 48 | 400 | 2.27 | 2.50 | SET_USER_POSITION |
| 49 | 400 | 2.51 | 3.82 | CHECK_USER_CONNECTION |
| 50 | 395 | 2.60 | 4.38 | SET_USER_POSITION |
| 51 | 385 | 3.84 | 3.99 | SET_USER_POSITION |
| 52 | 400 | 2.43 | 2.59 | SET_USER_POSITION |
| 53 | 384 | 37.70 | 39.50 | CHECK_USER_CONNECTION |
| 54 | 400 | 2.58 | 3.78 | SET_USER_POSITION |
| 55 | 384 | 24.69 | 30.61 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 9.76 | 20.00 | ENTER_SELECT_DUNGEON |
| 57 | 382 | 15.76 | 22.67 | SELECT_DUNGEON |
| 58 | 400 | 2.26 | 6.53 | SET_USER_POSITION |
| 59 | 398 | 20.80 | 32.33 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47398 | 0.45 | 2.16 | 14.96 | 46.63 | 67.51 |
| 10 并发登录的轮内 | 474* | 27.16 | 86.32 | 98.04 | 100.3 | 100.3 |
| （参考：实际发出口径）稳态窗 | 47398 | 0.21 | 0.53 | 14.49 | 46.14 | 67.35 |
| （参考：实际发出口径）轮内 | 474* | 3.57 | 80.69 | 92.17 | 92.34 | 92.34 |
| 登录阶段整体（182–232 s） | 19841 | 0.46 | 2.53 | 43.04 | 87.05 | 100.3 |

每轮时长（开始 → 10 个 LOGIN 都应答）：199, 210, 191, 193, 190, 193 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.47 | 2.37 | 13.36 | 38.48 | 64.32 | 12.65 |
| 单人 | 轮内 | 193* | 27.66 | 84.45 | 96.99 | 100.3 | 100.3 | 92.13 |
| 双人组领队 | 稳态窗 | 14081 | 0.45 | 1.67 | 20.09 | 50.29 | 67.51 | 19.69 |
| 双人组领队 | 轮内 | 141* | 24.82 | 86.32 | 98.10 | 98.87 | 98.87 | 92.34 |
| 双人组队员 | 稳态窗 | 14117 | 0.43 | 1.55 | 11.51 | 47.01 | 61.58 | 10.31 |
| 双人组队员 | 轮内 | 140* | 22.63 | 86.33 | 98.04 | 98.81 | 98.81 | 92.30 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 99.39 | 111.4 | 123.7 | 123.7 | 123.7 |
| 启动器登录（HTTP，含校验） | 60** | 86.39 | 94.79 | 99.05 | 99.05 | 99.05 |
| Game LOGIN 0x0001 | 60** | 0.22 | 0.49 | 0.78 | 0.78 | 0.78 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 256 持续发 CHECK_USER_CONNECTION；实际应答速率 62974 包/秒（1889397 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47398 | 0.45 | 2.16 | 14.96 | 46.63 | 67.51 |
| 其余连接，灌包期间 | 11862 | 0.51 | 4.63 | 8.76 | 29.85 | 42.24 |
| （参考：实际发出口径）其余连接，灌包期间 | 11862 | 0.10 | 4.25 | 8.01 | 29.61 | 41.62 |
| 灌包连接自身（含其窗口内排队） | 1889652 | 3.11 | 8.62 | 9.73 | 32.13 | 46.08 |

灌包窗口 256 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3767 | leave dungeon | 185.8 |
| EPLP | 请求者 | 78 | 3719 | leave dungeon | 185.8 |
| SET_USER_AREA | 同伴 | 6 | 3117 | town | 4.9 |
| SET_USER_AREA | 请求者 | 12 | 2890 | town | 4.9 |
| SELECT_DUNGEON | 请求者 | 78 | 1367 | party dungeon entry | 13.5 |
| SELECT_DUNGEON | 同伴 | 78 | 1367 | party dungeon entry | 13.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 78 | 799 | party dungeon entry | 53.1 |
| ENTER_SELECT_DUNGEON | 同伴 | 78 | 799 | party dungeon entry | 53.1 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 81 | 262 | party | 8.9 |
| RESPONSE_PEER | 请求者 | 81 | 260 | party | 8.9 |
| LEAVE_PARTY | 同伴 | 78 | 211 | leave party | 23.7 |
| LEAVE_PARTY | 请求者 | 78 | 148 | leave party | 23.7 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.2 |
| REQUEST_PEER | 同伴 | 81 | 132 | party | 250.8 |
| REQUEST_PEER | 请求者 | 81 | 104 | party | 250.8 |
| FINISH_LOADING | 请求者 | 156 | 36 | party dungeon entry | 13.6 |
| FINISH_LOADING | 同伴 | 156 | 0 | party dungeon entry | 13.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 14.96 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 98.04 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 8.76 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 229.2 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 101.6 ms，max 101.6 ms（出现于 t=0.4 s；冷启动窗内 max 101.6 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 78 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 81 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 2008456 行（Host 就绪之后 2008451 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 69.64 | 113.2 | 227.8 | 227.8 | 227.8 | 227.8 | 227.8 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 78** | 14.46 | 22.48 | 72.84 | 72.84 | 72.84 | 72.84 | 72.84 | 23.25 | 13.5 | 0 |
| inbound:GameEplpCommand | 78** | 33.97 | 57.25 | 63.66 | 63.66 | 63.66 | 49.56 | 52.28 | 63.66 | 206.1 | 0 |
| inbound:GameEnterSelectDungeon | 78** | 18.66 | 37.69 | 62.39 | 62.39 | 62.39 | 62.39 | 62.39 | 38.81 | 13.5 | 0 |
| inbound:GameSetUserArea | 12** | 13.73 | 39.83 | 39.83 | 39.83 | 39.83 | 10.78 | 39.83 | – | 4.4 | 0 |
| inbound:GameCreateCharacter | 21** | 1.54 | 4.17 | 29.81 | 29.81 | 29.81 | 29.81 | 29.81 | – | 0.3 | 0 |
| inbound:GameConnectionCheck | 1951583 | 0.00 | 0.00 | 0.01 | 0.03 | 15.55 | 0.13 | 3.85 | 15.55 | 273.9 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.92 | 14.38 | 14.38 | 14.38 | 1.78 | 1.78 | 14.38 | 274.0 | 0 |
| inbound:GameResponsePeer | 81** | 0.35 | 1.80 | 12.03 | 12.03 | 12.03 | 12.03 | 12.03 | 2.33 | 8.6 | 0 |
| inbound:GameRequestPeer | 81** | 2.22 | 3.16 | 11.30 | 11.30 | 11.30 | 11.30 | 11.30 | 3.14 | 8.6 | 0 |
| inbound:GameLeaveParty | 78** | 0.57 | 1.24 | 9.84 | 9.84 | 9.84 | 9.84 | 9.84 | 1.07 | 23.7 | 0 |
| inbound:GameLogin | 81** | 0.14 | 0.32 | 9.07 | 9.07 | 9.07 | 9.07 | 9.07 | 0.33 | 0.2 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.79 | 5.27 | 7.97 | 7.97 | 7.97 | 5.27 | 7.97 | – | 0.8 | 0 |
| event:achievement.inventory | 210* | 0.34 | 0.98 | 2.92 | 7.88 | 7.88 | 7.17 | 7.88 | – | 3.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 2.39 | 7.07 | 7.07 | 7.07 | 7.07 | 7.07 | 7.07 | 2.94 | 60.0 | 0 |
| inbound:GameSetUserPosition | 46069 | 0.02 | 0.07 | 0.10 | 3.15 | 6.95 | 1.78 | 6.95 | 6.14 | 14.1 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.14 | 0.61 | 6.93 | 6.93 | 6.93 | 6.93 | 6.93 | 0.87 | 0.2 | 0 |
| timer:CreatureClockService | 277* | 0.02 | 0.92 | 1.23 | 6.16 | 6.16 | 6.16 | 6.16 | 1.26 | 1.0 | 0 |
| event:altar.departure | 156* | 0.14 | 0.28 | 0.66 | 6.05 | 6.05 | 0.53 | 0.53 | 6.05 | 206.7 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 1.81 | 5.62 | 5.62 | 5.62 | 5.62 | 5.62 | 5.62 | 2.00 | 16.3 | 0 |
| event:quest.inventory | 210* | 0.11 | 0.32 | 1.55 | 4.71 | 4.71 | 4.71 | 4.71 | – | 0.3 | 0 |
| lifecycle:connected | 82** | 0.02 | 0.04 | 3.97 | 3.97 | 3.97 | 3.97 | 3.97 | 0.06 | 0.1 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.78 | 3.59 | 3.59 | 3.59 | 3.59 | 2.77 | 2.77 | 3.59 | 180.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.04 | 3.55 | 3.55 | 3.55 | 3.55 | 3.55 | 3.55 | 0.59 | 60.0 | 0 |
| inbound:GameFinishLoading | 156* | 0.10 | 0.24 | 1.81 | 3.24 | 3.24 | 3.24 | 3.24 | 0.34 | 13.6 | 0 |
| event:quest.mirror-leave | 156* | 0.14 | 0.27 | 0.63 | 3.07 | 3.07 | 3.07 | 3.07 | 0.63 | 18.6 | 0 |
| timer:FatigueDailyRefreshService | 4** | 1.50 | 2.74 | 2.74 | 2.74 | 2.74 | 2.15 | 2.15 | 2.74 | 120.0 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.07 | 0.26 | 2.54 | 2.54 | 2.54 | 2.54 | 2.54 | 0.26 | 0.2 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.05 | 1.97 | 1.97 | 1.97 | 1.97 | 1.97 | 1.97 | 0.35 | 60.0 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.10 | 0.17 | 0.19 | 1.86 | 1.86 | 1.86 | 1.86 | 0.18 | 1.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.04 | 0.06 | 0.59 | 1.25 | 1.25 | 1.25 | 0.06 | 0.2 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 1.05 | 1.05 | 1.05 | 1.05 | 0.02 | 0.5 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.02 | 0.99 | 0.99 | 0.99 | 0.99 | 0.99 | 0.99 | 0.03 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.01 | 0.88 | 0.88 | 0.88 | 0.88 | 0.88 | 0.88 | 0.03 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.02 | 0.88 | 0.88 | 0.88 | 0.88 | 0.88 | 0.88 | 0.02 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 0.79 | 0.79 | 0.79 | 0.79 | 0.04 | 0.5 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.01 | 0.77 | 0.77 | 0.77 | 0.77 | 0.77 | 0.77 | 0.05 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.04 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.04 | 60.0 | 0 |
| event:tournament.departure | 156* | 0.12 | 0.19 | 0.23 | 0.69 | 0.69 | 0.19 | 0.69 | 0.23 | 40.2 | 0 |
| timer:JoustMaintenanceService | 277* | 0.27 | 0.40 | 0.43 | 0.64 | 0.64 | 0.13 | 0.64 | 0.40 | 5.9 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.02 | 0.61 | 0.61 | 0.61 | 0.61 | 0.61 | 0.61 | 0.02 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5539 | 0.00 | 0.01 | 0.01 | 0.02 | 0.55 | 0.55 | 0.55 | 0.03 | 0.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.17 | 0.17 | 0.17 | 0.17 | 0.17 | 0.17 | 0.03 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 156* | 0.00 | 0.02 | 0.02 | 0.05 | 0.05 | 0.05 | 0.05 | 0.02 | 13.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 14.96 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25440 | 0.01 | 0.42 | 36.40 | 85.14 | 228.7 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22948 | 0.01 | 0.20 | 29.05 | 75.76 | 121.2 |  |  |
| 稳态窗 | 全部作业 | 52088 | 0.01 | 0.16 | 14.42 | 43.09 | 62.37 | inbound:GameConnectionCheck | 162.1 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.14 | 10.99 | 42.39 | 62.37 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 851* | 0.06 | 0.44 | 1.82 | 100.3 | 100.3 | timer:TournamentRewardSweepService | 192.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 594* | 0.09 | 0.79 | 1.82 | 1.84 | 1.84 |  |  |
| (e) 灌包期间 | 全部作业 | 1902667 | 0.00 | 0.00 | 0.01 | 0.05 | 41.44 | inbound:GameConnectionCheck | 268.1 |
| (e) 灌包期间 | 只算 inbound: | 1901657 | 0.00 | 0.00 | 0.01 | 0.04 | 41.44 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 136 | 228.5 | 228.7 | event:achievement.inventory | 227.8 |
| 1 | 246 | 84.27 | 92.13 | event:quest.inventory | 91.68 |
| 2 | 347 | 102.3 | 112.7 | event:quest.inventory | 112.2 |
| 3 | 361 | 113.6 | 121.2 | inbound:GameSetUserPosition | 113.2 |
| 4 | 488 | 88.79 | 96.02 | event:quest.inventory | 95.74 |
| 5 | 431 | 0.11 | 0.18 | inbound:GameConnectionCheck | 0.64 |
| 6 | 432 | 0.20 | 0.35 | timer:TournamentRewardSweepService | 2.58 |
| 7 | 431 | 0.16 | 0.34 | timer:IllusionTowerReturnSweepService | 3.19 |
| 8 | 436 | 4.35 | 10.17 | inbound:GameConnectionCheck | 12.03 |
| 9 | 428 | 0.23 | 2.37 | inbound:GameConnectionCheck | 5.80 |
| 10 | 431 | 0.26 | 3.88 | inbound:GameConnectionCheck | 3.85 |
| 11 | 431 | 0.19 | 1.77 | inbound:GameSetUserPosition | 1.75 |
| 12 | 432 | 0.24 | 0.30 | inbound:GameConnectionCheck | 0.19 |
| 13 | 444 | 69.64 | 74.65 | inbound:GameConnectionCheck | 72.84 |
| 14 | 422 | 0.07 | 0.12 | timer:TournamentRewardSweepService | 6.95 |
| 15 | 438 | 17.96 | 25.31 | inbound:GameSetUserPosition | 27.18 |
| 16 | 429 | 0.21 | 2.34 | inbound:GameConnectionCheck | 5.62 |
| 17 | 431 | 0.12 | 2.47 | inbound:GameConnectionCheck | 2.43 |
| 18 | 446 | 38.08 | 45.57 | inbound:GameConnectionCheck | 49.56 |
| 19 | 438 | 26.43 | 34.01 | inbound:GameConnectionCheck | 36.48 |
| 20 | 439 | 30.05 | 40.17 | inbound:GameSetUserPosition | 46.87 |
| 21 | 445 | 42.33 | 49.86 | inbound:GameConnectionCheck | 52.28 |
| 22 | 432 | 0.31 | 3.56 | inbound:GameConnectionCheck | 3.52 |
| 23 | 431 | 0.26 | 6.63 | inbound:GameConnectionCheck | 9.84 |
| 24 | 432 | 0.18 | 0.28 | inbound:GameSetUserPosition | 3.94 |
| 25 | 431 | 0.28 | 2.40 | inbound:GameConnectionCheck | 2.30 |
| 26 | 432 | 0.25 | 0.39 | inbound:GameConnectionCheck | 1.25 |
| 27 | 436 | 0.19 | 1.55 | inbound:GameSetUserPosition | 3.16 |
| 28 | 427 | 0.21 | 0.26 | inbound:GameConnectionCheck | 3.18 |
| 29 | 437 | 0.28 | 2.83 | inbound:GameConnectionCheck | 0.85 |
| 30 | 429 | 0.20 | 0.34 | inbound:GameConnectionCheck | 0.91 |
| 31 | 429 | 0.21 | 2.64 | inbound:GameConnectionCheck | 2.65 |
| 32 | 432 | 0.27 | 3.50 | inbound:GameConnectionCheck | 3.38 |
| 33 | 433 | 21.19 | 25.20 | inbound:GameConnectionCheck | 32.33 |
| 34 | 433 | 17.86 | 26.48 | inbound:GameSetUserPosition | 30.22 |
| 35 | 432 | 10.28 | 12.82 | inbound:GameSetUserPosition | 14.52 |
| 36 | 434 | 11.45 | 16.10 | inbound:GameSetUserPosition | 22.31 |
| 37 | 431 | 0.10 | 0.29 | inbound:GameSetUserPosition | 0.31 |
| 38 | 453 | 29.63 | 35.96 | inbound:GameConnectionCheck | 38.19 |
| 39 | 431 | 0.18 | 0.24 | inbound:GameConnectionCheck | 0.33 |
| 40 | 439 | 26.37 | 36.40 | inbound:GameConnectionCheck | 43.81 |
| 41 | 445 | 43.00 | 49.31 | inbound:GameSetUserPosition | 51.48 |
| 42 | 432 | 0.17 | 0.24 | timer:CreatureClockService | 1.24 |
| 43 | 431 | 0.18 | 0.27 | timer:CreatureClockService | 0.30 |
| 44 | 432 | 0.28 | 0.36 | inbound:GameSetUserPosition | 0.95 |
| 45 | 431 | 0.21 | 3.14 | inbound:GameSetUserPosition | 3.08 |
| 46 | 432 | 0.20 | 0.25 | inbound:GameSetUserPosition | 2.06 |
| 47 | 431 | 0.26 | 1.71 | inbound:GameConnectionCheck | 2.81 |
| 48 | 432 | 0.21 | 0.34 | inbound:GameConnectionCheck | 1.16 |
| 49 | 436 | 0.23 | 0.32 | inbound:GameSetUserPosition | 2.50 |
| 50 | 427 | 0.23 | 3.49 | inbound:GameConnectionCheck | 0.30 |
| 51 | 431 | 0.29 | 3.03 | inbound:GameSetUserPosition | 2.98 |
| 52 | 432 | 0.22 | 0.27 | inbound:GameConnectionCheck | 2.04 |
| 53 | 433 | 24.91 | 37.36 | inbound:GameSetUserPosition | 37.28 |
| 54 | 432 | 0.16 | 3.21 | inbound:GameSetUserPosition | 3.18 |
| 55 | 433 | 20.39 | 26.66 | inbound:GameSetUserPosition | 30.32 |
| 56 | 426 | 0.14 | 0.34 | timer:TournamentRewardSweepService | 0.30 |
| 57 | 446 | 12.73 | 16.35 | inbound:GameSetUserPosition | 22.48 |
| 58 | 432 | 0.28 | 0.30 | inbound:GameConnectionCheck | 0.31 |
| 59 | 450 | 20.75 | 27.72 | inbound:GameConnectionCheck | 31.80 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25440 | 4176 | 7.0% | inbound:GameSelectCharacter 1766 ms／21／max 227.8；inbound:GameEplpCommand 526 ms／14／max 52.28；inbound:GameEnterSelectDungeon 384 ms／18／max 62.39；inbound:GameSetUserPosition 355 ms／9608／max 6.95；inbound:GameSelectDungeon 318 ms／18／max 72.84；inbound:GameSetUserArea 210 ms／12／max 39.83；inbound:GameConnectionCheck 115 ms／13131／max 3.85；event:achievement.inventory 106 ms／210／max 7.88 |
| 稳态窗 | 120.0 | 52088 | 3852 | 3.2% | inbound:GameEplpCommand 1259 ms／35／max 57.25；inbound:GameSetUserPosition 821 ms／20378／max 5.03；inbound:GameEnterSelectDungeon 728 ms／35／max 36.93；inbound:GameSelectDungeon 537 ms／35／max 23.25；inbound:GameConnectionCheck 256 ms／27399／max 4.10；inbound:GameRequestPeer 77 ms／36／max 3.12；timer:JoustMaintenanceService 30 ms／120／max 0.40；timer:CreatureClockService 22 ms／120／max 1.26 |
| (d) 10 并发登录的轮内 | 1.2 | 851 | 28 | 2.4% | http:POST /api/launcher/v1/register 11 ms／60／max 0.87；inbound:GameLogin 8 ms／60／max 0.33；http:POST /api/launcher/v1/login 4 ms／60／max 0.26；inbound:GameSetUserPosition 2 ms／211／max 0.09；inbound:GameConnectionCheck 1 ms／323／max 0.02；lifecycle:connected 1 ms／60／max 0.06；lifecycle:disconnected 0 ms／55／max 0.09；timer:TournamentRewardSweepService 0 ms／21／max 0.01 |
| (e) 灌包期间 | 30.0 | 1902667 | 8433 | 28.1% | inbound:GameConnectionCheck 7838 ms／1896382／max 7.38；inbound:GameEplpCommand 274 ms／10／max 41.44；inbound:GameSetUserPosition 119 ms／5208／max 6.14；inbound:GameEnterSelectDungeon 92 ms／7／max 17.32；inbound:GameSelectDungeon 81 ms／7／max 14.39；inbound:GameRequestPeer 6 ms／9／max 0.84；event:altar.departure 4 ms／20／max 0.66；timer:JoustMaintenanceService 3 ms／30／max 0.16 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.8，`event:` 1.8。

