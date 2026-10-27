# 基线测量 — m2bd2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3762 ms

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
| CHECK_USER_CONNECTION | 26742 | 55.71% |
| SET_USER_POSITION | 20409 | 42.52% |
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
| CHECK_USER_CONNECTION | 26742 | 0.37 | 1.97 | 16.04 | 38.94 | 51.21 | 0 | 15.74 | 38.94 | 51.21 |
| SET_USER_POSITION | 20409 | 0.44 | 2.10 | 15.70 | 39.34 | 53.62 | 0 | 15.12 | 39.34 | 53.62 |
| FINISH_LOADING | 70** | 0.18 | 0.33 | 1.80 | 1.80 | 1.80 | 0 | 1.80 | 1.80 | 1.80 |
| LEAVE_PARTY | 36** | 0.85 | 1.12 | 1.46 | 1.46 | 1.46 | 0 | 1.46 | 1.46 | 1.46 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.46 | 0.67 | 17.32 | 17.32 | 17.32 | 0 | 17.32 | 17.32 | 17.32 |
| EPLP | 35** | 39.63 | 57.57 | 59.81 | 59.81 | 59.81 | 0 | 58.97 | 58.97 | 58.97 |
| ENTER_SELECT_DUNGEON | 35** | 28.48 | 38.08 | 38.14 | 38.14 | 38.14 | 0 | 37.83 | 37.83 | 37.83 |
| SELECT_DUNGEON | 35** | 15.10 | 19.79 | 20.21 | 20.21 | 20.21 | 0 | 20.21 | 20.21 | 20.21 |
| **全部计入的请求** | 47398 | 0.41 | 2.07 | 18.55 | 40.84 | 59.81 | 36 | 18.27 | 40.46 | 58.97 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47434 | 0.14 | 1.54 | 2.09 | 2.48 | 10.83（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.80 | 11.00 | 99.58 | 99.58 | 99.58 | 0.4 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 15.28 | 58.90 | 59.87 | 59.87 | 59.87 | 3.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 18.51 | 51.16 | 56.23 | 56.23 | 56.23 | 4.8 | 0 |
| EnterDungeonCommand | Transactional | 79** | 9.19 | 12.34 | 55.00 | 55.00 | 55.00 | 12.8 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.93 | 22.38 | 25.63 | 25.63 | 25.63 | 3.2 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.17 | 2.16 | 23.15 | 23.15 | 23.15 | 0.3 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.34 | 20.83 | 21.14 | 21.14 | 21.14 | 3.1 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.59 | 3.11 | 17.01 | 17.01 | 17.01 | 17.8 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.26 | 3.98 | 6.98 | 6.98 | 6.98 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 84** | 0.03 | 0.25 | 5.98 | 5.98 | 5.98 | 8.1 | 0 |
| RequestPartyInviteCommand | Direct | 84** | 1.92 | 2.80 | 5.09 | 5.09 | 5.09 | 50.0 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.10 | 0.47 | 4.46 | 4.46 | 4.46 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.91 | 3.80 | 3.80 | 3.80 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.02 | 0.24 | 3.61 | 3.61 | 3.61 | 0.5 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.26 | 3.40 | 3.40 | 3.40 | 0.3 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.11 | 3.36 | 3.36 | 3.36 | 0.3 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.08 | 3.14 | 3.14 | 3.14 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 335* | 0.12 | 0.37 | 0.41 | 3.05 | 3.05 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.10 | 2.90 | 2.90 | 2.90 | 0.5 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.05 | 2.76 | 2.76 | 2.76 | 2.76 | 60.0 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.13 | 2.31 | 2.31 | 2.31 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.20 | 1.61 | 1.61 | 1.61 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.58 | 1.58 | 1.58 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.11 | 1.49 | 1.49 | 1.49 | 0.5 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.00 | 0.02 | 0.10 | 1.46 | 1.46 | 0.2 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.03 | 0.22 | 0.32 | 1.42 | 1.42 | 32.7 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.08 | 1.27 | 1.27 | 1.27 | 22.0 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.05 | 0.14 | 0.80 | 0.80 | 0.80 | 60.0 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.03 | 0.30 | 0.59 | 0.59 | 12.8 | 0 |

命令日志行 1770 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**230.9 ms**（SELECT_CHARACTER，计划发出 t=0.29 s，solo06；参考口径 230.9 ms）。冷启动窗整体：22539 | 0.42 | 2.47 | 41.81 | 102.1 | 230.9（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 25 | 230.9 | 230.9 | SELECT_CHARACTER |
| 1 | 115 | 76.41 | 76.88 | SELECT_CHARACTER |
| 2 | 172 | 122.3 | 123.5 | SELECT_CHARACTER |
| 3 | 183 | 112.6 | 112.7 | SELECT_CHARACTER |
| 4 | 330 | 105.5 | 107.0 | SELECT_CHARACTER |
| 5 | 400 | 2.08 | 2.35 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.49 | 2.97 | SET_USER_POSITION |
| 7 | 400 | 2.19 | 3.12 | SET_USER_POSITION |
| 8 | 390 | 12.71 | 17.64 | RESPONSE_PEER |
| 9 | 391 | 2.57 | 3.94 | RESPONSE_PEER |
| 10 | 389 | 5.33 | 7.42 | SET_USER_POSITION |
| 11 | 400 | 2.20 | 2.77 | SET_USER_POSITION |
| 12 | 394 | 62.87 | 73.99 | SELECT_DUNGEON |
| 13 | 390 | 24.02 | 36.31 | ENTER_SELECT_DUNGEON |
| 14 | 392 | 28.92 | 31.13 | ENTER_SELECT_DUNGEON |
| 15 | 395 | 17.41 | 22.24 | ENTER_SELECT_DUNGEON |
| 16 | 381 | 2.45 | 4.94 | CHECK_USER_CONNECTION |
| 17 | 398 | 40.01 | 49.07 | EPLP |
| 18 | 398 | 27.47 | 39.79 | EPLP |
| 19 | 398 | 43.66 | 45.66 | EPLP |
| 20 | 398 | 69.89 | 75.85 | EPLP |
| 21 | 396 | 29.72 | 45.55 | EPLP |
| 22 | 398 | 3.22 | 6.29 | SET_USER_POSITION |
| 23 | 400 | 2.32 | 3.38 | CHECK_USER_CONNECTION |
| 24 | 396 | 2.46 | 3.24 | SET_USER_POSITION |
| 25 | 398 | 2.72 | 3.18 | SET_USER_POSITION |
| 26 | 391 | 2.50 | 2.76 | SET_USER_POSITION |
| 27 | 400 | 2.37 | 2.58 | SET_USER_POSITION |
| 28 | 400 | 2.28 | 2.86 | CHECK_USER_CONNECTION |
| 29 | 385 | 2.51 | 3.75 | CHECK_USER_CONNECTION |
| 30 | 400 | 2.50 | 2.68 | CHECK_USER_CONNECTION |
| 31 | 382 | 20.24 | 30.37 | ENTER_SELECT_DUNGEON |
| 32 | 400 | 2.02 | 2.27 | SET_USER_POSITION |
| 33 | 400 | 2.24 | 2.71 | SET_USER_POSITION |
| 34 | 392 | 12.67 | 15.17 | ENTER_SELECT_DUNGEON |
| 35 | 384 | 28.07 | 32.77 | ENTER_SELECT_DUNGEON |
| 36 | 384 | 14.66 | 21.07 | ENTER_SELECT_DUNGEON |
| 37 | 398 | 11.71 | 22.65 | EPLP |
| 38 | 398 | 15.52 | 17.97 | EPLP |
| 39 | 396 | 31.90 | 36.57 | EPLP |
| 40 | 398 | 47.39 | 53.63 | EPLP |
| 41 | 396 | 42.49 | 58.97 | EPLP |
| 42 | 400 | 2.71 | 3.41 | CHECK_USER_CONNECTION |
| 43 | 400 | 2.62 | 3.59 | SET_USER_POSITION |
| 44 | 394 | 2.72 | 3.66 | SET_USER_POSITION |
| 45 | 400 | 2.52 | 2.86 | SET_USER_POSITION |
| 46 | 396 | 3.19 | 10.97 | SET_USER_POSITION |
| 47 | 395 | 2.23 | 2.63 | SET_USER_POSITION |
| 48 | 395 | 2.51 | 3.58 | SET_USER_POSITION |
| 49 | 395 | 3.62 | 3.72 | CHECK_USER_CONNECTION |
| 50 | 395 | 2.51 | 3.35 | SET_USER_POSITION |
| 51 | 395 | 2.38 | 2.69 | CHECK_USER_CONNECTION |
| 52 | 387 | 28.18 | 38.26 | ENTER_SELECT_DUNGEON |
| 53 | 402 | 34.41 | 35.40 | CHECK_USER_CONNECTION |
| 54 | 390 | 15.96 | 28.19 | ENTER_SELECT_DUNGEON |
| 55 | 382 | 15.79 | 22.20 | ENTER_SELECT_DUNGEON |
| 56 | 392 | 22.94 | 33.01 | EPLP |
| 57 | 392 | 10.18 | 23.76 | ENTER_SELECT_DUNGEON |
| 58 | 398 | 31.30 | 33.42 | EPLP |
| 59 | 400 | 24.03 | 30.07 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47398 | 0.41 | 2.07 | 18.55 | 40.84 | 59.81 |
| 10 并发登录的轮内 | 488* | 24.99 | 81.01 | 96.90 | 108.8 | 108.8 |
| （参考：实际发出口径）稳态窗 | 47398 | 0.20 | 0.46 | 18.27 | 40.46 | 58.97 |
| （参考：实际发出口径）轮内 | 488* | 10.43 | 79.00 | 89.97 | 101.8 | 101.8 |
| 登录阶段整体（182–232 s） | 19840 | 0.43 | 2.57 | 41.45 | 88.35 | 108.8 |

每轮时长（开始 → 10 个 LOGIN 都应答）：188, 204, 232, 212, 191, 204 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.43 | 2.32 | 18.46 | 39.57 | 53.62 | 18.24 |
| 单人 | 轮内 | 199* | 24.19 | 79.32 | 97.06 | 108.8 | 108.8 | 89.90 |
| 双人组领队 | 稳态窗 | 14081 | 0.40 | 1.76 | 21.76 | 45.13 | 59.81 | 21.65 |
| 双人组领队 | 轮内 | 145* | 24.99 | 88.35 | 95.35 | 106.9 | 106.9 | 89.97 |
| 双人组队员 | 稳态窗 | 14117 | 0.39 | 1.71 | 15.04 | 38.22 | 51.05 | 14.47 |
| 双人组队员 | 轮内 | 144* | 26.88 | 89.30 | 95.34 | 102.8 | 102.8 | 90.04 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 102.6 | 131.9 | 133.8 | 133.8 | 133.8 |
| 启动器登录（HTTP，含校验） | 60** | 88.55 | 95.42 | 98.12 | 98.12 | 98.12 |
| Game LOGIN 0x0001 | 60** | 0.21 | 0.53 | 7.58 | 7.58 | 7.58 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 64377 包/秒（1931556 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47398 | 0.41 | 2.07 | 18.55 | 40.84 | 59.81 |
| 其余连接，灌包期间 | 11878 | 0.54 | 4.63 | 8.42 | 28.50 | 40.12 |
| （参考：实际发出口径）其余连接，灌包期间 | 11878 | 0.10 | 4.21 | 7.50 | 28.49 | 37.91 |
| 灌包连接自身（含其窗口内排队） | 1931587 | 0.35 | 0.45 | 5.16 | 7.14 | 43.82 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 请求者 | 78 | 3761 | leave dungeon | 20.9 |
| EPLP | 同伴 | 78 | 3761 | leave dungeon | 20.9 |
| SET_USER_AREA | 同伴 | 6 | 3069 | town | 4.9 |
| SET_USER_AREA | 请求者 | 12 | 2914 | town | 4.9 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 12.7 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 12.7 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 632 | party dungeon entry | 53.9 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 632 | party dungeon entry | 53.9 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 84 | 262 | party | 48.5 |
| RESPONSE_PEER | 请求者 | 84 | 260 | party | 48.5 |
| LEAVE_PARTY | 同伴 | 78 | 211 | leave party | 44.2 |
| LEAVE_PARTY | 请求者 | 78 | 148 | leave party | 44.2 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.2 |
| REQUEST_PEER | 同伴 | 84 | 124 | party | 113.0 |
| REQUEST_PEER | 请求者 | 84 | 96 | party | 9.5 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 12.8 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 12.8 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 18.55 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 96.90 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 8.42 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 0 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 230.9 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- 无。

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 1 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）1 个，协议无回包 84 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 2050382 行（Host 就绪之后 2050377 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 69.45 | 122.7 | 229.9 | 229.9 | 229.9 | 229.9 | 229.9 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 79** | 14.90 | 19.92 | 73.48 | 73.48 | 73.48 | 73.48 | 73.48 | 19.97 | 12.8 | 0 |
| inbound:GameEplpCommand | 78** | 37.62 | 57.18 | 58.64 | 58.64 | 58.64 | 48.19 | 57.56 | 58.64 | 63.5 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 25.85 | 37.62 | 53.37 | 53.37 | 53.37 | 53.37 | 53.37 | 53.21 | 12.7 | 0 |
| inbound:GameCreateCharacter | 21** | 1.61 | 5.02 | 32.06 | 32.06 | 32.06 | 32.06 | 32.06 | – | 0.3 | 0 |
| inbound:GameSetUserArea | 12** | 14.46 | 24.14 | 24.14 | 24.14 | 24.14 | 10.44 | 24.14 | – | 4.3 | 0 |
| inbound:GameResponsePeer | 84** | 0.35 | 0.75 | 17.24 | 17.24 | 17.24 | 17.24 | 17.24 | 0.52 | 8.2 | 0 |
| inbound:GameRequestPeer | 84** | 2.30 | 3.23 | 16.52 | 16.52 | 16.52 | 16.52 | 16.52 | 3.23 | 8.1 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.28 | 16.50 | 16.50 | 16.50 | 1.83 | 1.83 | 16.50 | 274.0 | 0 |
| inbound:GameLeaveParty | 78** | 0.55 | 2.07 | 10.42 | 10.42 | 10.42 | 10.42 | 10.42 | 1.25 | 22.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 1.54 | 9.24 | 9.24 | 9.24 | 9.24 | 9.24 | 9.24 | 3.33 | 60.0 | 0 |
| inbound:GameLogin | 81** | 0.12 | 0.30 | 9.00 | 9.00 | 9.00 | 9.00 | 9.00 | 0.30 | 0.2 | 0 |
| event:achievement.inventory | 210* | 0.32 | 1.10 | 4.07 | 8.12 | 8.12 | 7.05 | 8.12 | – | 3.4 | 0 |
| inbound:GameConnectionCheck | 1993412 | 0.00 | 0.00 | 0.01 | 0.03 | 7.45 | 0.14 | 4.33 | 7.45 | 271.7 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.12 | 0.60 | 6.98 | 6.98 | 6.98 | 6.98 | 6.98 | 0.89 | 0.2 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 2.01 | 6.65 | 6.65 | 6.65 | 6.65 | 6.65 | 6.65 | 2.32 | 32.7 | 0 |
| inbound:GameSetUserPosition | 46151 | 0.02 | 0.07 | 0.09 | 3.16 | 5.96 | 1.76 | 4.38 | 5.96 | 270.5 | 0 |
| timer:CreatureClockService | 277* | 0.03 | 0.76 | 1.19 | 5.94 | 5.94 | 5.94 | 5.94 | 1.28 | 1.0 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.75 | 5.02 | 5.32 | 5.32 | 5.32 | 5.02 | 5.32 | – | 0.7 | 0 |
| event:quest.inventory | 210* | 0.10 | 0.41 | 1.21 | 4.67 | 4.67 | 4.67 | 4.67 | – | 0.3 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.02 | 4.65 | 4.65 | 4.65 | 4.65 | 4.65 | 4.65 | 0.03 | 60.0 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.46 | 4.15 | 4.15 | 4.15 | 4.15 | 4.15 | 4.15 | 3.50 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.04 | 0.06 | 1.31 | 4.14 | 1.31 | 1.31 | 4.14 | 99.5 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 3.99 | 3.99 | 3.99 | 3.99 | 3.99 | 0.05 | 0.1 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.01 | 3.63 | 3.63 | 3.63 | 3.63 | 3.63 | 3.63 | 0.05 | 60.0 | 0 |
| inbound:GameFinishLoading | 158* | 0.10 | 0.24 | 2.10 | 3.01 | 3.01 | 3.01 | 3.01 | 1.68 | 12.8 | 0 |
| event:quest.mirror-leave | 158* | 0.15 | 0.33 | 1.57 | 2.71 | 2.71 | 2.71 | 2.71 | 1.57 | 17.8 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.07 | 0.25 | 2.62 | 2.62 | 2.62 | 2.62 | 2.62 | 0.27 | 0.2 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.50 | 2.54 | 2.54 | 2.54 | 2.54 | 2.54 | 2.54 | 1.15 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.14 | 2.33 | 2.33 | 2.33 | 2.33 | 2.33 | 2.33 | 0.19 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.28 | 1.93 | 1.93 | 1.93 | 1.93 | 1.93 | 1.93 | 0.33 | 60.0 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.10 | 0.16 | 0.19 | 1.86 | 1.86 | 1.86 | 1.86 | 0.19 | 1.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.02 | 1.38 | 1.38 | 1.38 | 1.38 | 1.38 | 1.38 | 0.03 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.01 | 1.38 | 1.38 | 1.38 | 1.38 | 1.38 | 1.38 | 0.02 | 60.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.02 | 1.29 | 1.29 | 1.29 | 1.29 | 1.29 | 1.29 | 0.03 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.02 | 1.21 | 1.21 | 1.21 | 1.21 | 1.21 | 1.21 | 0.03 | 60.0 | 0 |
| event:tournament.departure | 158* | 0.13 | 0.20 | 0.61 | 1.06 | 1.06 | 0.21 | 1.06 | 0.61 | 39.5 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 0.92 | 0.92 | 0.92 | 0.92 | 0.04 | 0.5 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.02 | 0.76 | 0.76 | 0.76 | 0.76 | 0.02 | 0.5 | 0 |
| timer:JoustMaintenanceService | 277* | 0.27 | 0.41 | 0.45 | 0.70 | 0.70 | 0.15 | 0.70 | 0.42 | 33.9 | 0 |
| timer:TournamentRewardSweepService | 5536 | 0.00 | 0.01 | 0.01 | 0.02 | 0.59 | 0.59 | 0.59 | 0.03 | 0.0 | 0 |
| event:altar.departure | 158* | 0.14 | 0.29 | 0.41 | 0.59 | 0.59 | 0.59 | 0.59 | 0.41 | 17.8 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.36 | 0.36 | 0.36 | 0.36 | 0.36 | 0.36 | 0.03 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 158* | 0.00 | 0.02 | 0.04 | 0.05 | 0.05 | 0.04 | 0.05 | 0.02 | 56.7 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 18.55 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25427 | 0.01 | 0.62 | 36.49 | 105.0 | 231.1 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22934 | 0.01 | 0.18 | 29.56 | 98.75 | 120.8 |  |  |
| 稳态窗 | 全部作业 | 52088 | 0.01 | 0.12 | 19.01 | 39.92 | 53.48 | inbound:GameSetUserPosition | 165.5 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.11 | 15.00 | 38.59 | 53.48 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 891* | 0.06 | 14.10 | 25.92 | 88.17 | 88.17 | timer:TournamentRewardSweepService | 232.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 618* | 0.08 | 11.33 | 17.88 | 28.91 | 28.91 |  |  |
| (e) 灌包期间 | 全部作业 | 1944591 | 0.00 | 0.00 | 0.01 | 0.04 | 37.65 | inbound:GameConnectionCheck | 263.5 |
| (e) 灌包期间 | 只算 inbound: | 1943590 | 0.00 | 0.00 | 0.01 | 0.04 | 37.65 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 115 | 230.5 | 231.1 | event:achievement.inventory | 229.9 |
| 1 | 277 | 76.19 | 76.25 | event:quest.inventory | 75.81 |
| 2 | 336 | 120.0 | 123.1 | event:quest.inventory | 122.7 |
| 3 | 324 | 110.0 | 112.7 | event:quest.inventory | 112.3 |
| 4 | 513 | 101.3 | 106.6 | event:quest.inventory | 106.3 |
| 5 | 431 | 0.08 | 0.78 | inbound:GameConnectionCheck | 0.78 |
| 6 | 432 | 0.16 | 1.35 | inbound:GameConnectionCheck | 1.31 |
| 7 | 431 | 0.29 | 0.71 | inbound:GameSetUserPosition | 0.43 |
| 8 | 432 | 9.40 | 15.62 | inbound:GameConnectionCheck | 17.24 |
| 9 | 432 | 0.86 | 3.37 | inbound:GameSetUserPosition | 3.97 |
| 10 | 431 | 0.24 | 0.52 | inbound:GameSetUserPosition | 3.77 |
| 11 | 431 | 0.22 | 1.83 | inbound:GameConnectionCheck | 1.80 |
| 12 | 435 | 60.25 | 67.73 | inbound:GameSetUserPosition | 73.48 |
| 13 | 430 | 23.59 | 28.60 | inbound:GameSetUserPosition | 35.06 |
| 14 | 433 | 26.75 | 27.99 | inbound:GameConnectionCheck | 30.91 |
| 15 | 453 | 12.14 | 17.25 | inbound:GameConnectionCheck | 20.47 |
| 16 | 413 | 0.11 | 1.08 | timer:BossTowerReturnSweepService | 0.77 |
| 17 | 438 | 32.32 | 44.16 | inbound:GameConnectionCheck | 48.19 |
| 18 | 439 | 25.77 | 32.02 | inbound:GameSetUserPosition | 38.12 |
| 19 | 438 | 41.78 | 44.36 | inbound:GameConnectionCheck | 45.35 |
| 20 | 448 | 64.03 | 70.97 | inbound:GameConnectionCheck | 48.57 |
| 21 | 438 | 33.73 | 34.37 | event:altar.departure | 44.99 |
| 22 | 430 | 0.24 | 4.39 | inbound:GameSetUserPosition | 4.38 |
| 23 | 431 | 0.15 | 2.37 | inbound:GameConnectionCheck | 2.29 |
| 24 | 432 | 0.18 | 0.35 | inbound:GameConnectionCheck | 2.45 |
| 25 | 431 | 0.19 | 2.94 | inbound:GameSetUserPosition | 2.92 |
| 26 | 432 | 0.13 | 0.22 | inbound:GameConnectionCheck | 2.75 |
| 27 | 431 | 0.20 | 0.24 | timer:BossTowerReturnSweepService | 0.40 |
| 28 | 432 | 0.19 | 0.24 | timer:CreatureClockService | 0.42 |
| 29 | 432 | 0.30 | 1.67 | inbound:GameSetUserPosition | 2.78 |
| 30 | 432 | 0.20 | 1.47 | inbound:GameSetUserPosition | 0.57 |
| 31 | 432 | 14.40 | 27.01 | inbound:GameSetUserPosition | 29.14 |
| 32 | 433 | 0.18 | 0.24 | timer:TournamentRewardSweepService | 6.65 |
| 33 | 431 | 0.14 | 0.30 | timer:IllusionTowerReturnSweepService | 1.02 |
| 34 | 433 | 4.98 | 12.51 | inbound:GameSetUserPosition | 15.02 |
| 35 | 433 | 23.58 | 27.33 | inbound:GameConnectionCheck | 30.94 |
| 36 | 434 | 13.16 | 16.93 | timer:TournamentRewardSweepService | 20.44 |
| 37 | 438 | 7.95 | 16.45 | inbound:GameConnectionCheck | 22.45 |
| 38 | 439 | 11.67 | 15.34 | inbound:GameSetUserPosition | 17.82 |
| 39 | 445 | 27.82 | 31.60 | inbound:GameConnectionCheck | 36.40 |
| 40 | 439 | 40.86 | 49.68 | inbound:GameConnectionCheck | 51.97 |
| 41 | 438 | 40.85 | 46.06 | timer:TournamentRewardSweepService | 57.56 |
| 42 | 432 | 0.22 | 0.43 | inbound:GameConnectionCheck | 0.13 |
| 43 | 431 | 0.15 | 0.20 | inbound:GameConnectionCheck | 0.19 |
| 44 | 432 | 0.16 | 0.25 | inbound:GameSetUserPosition | 3.18 |
| 45 | 431 | 0.23 | 2.11 | inbound:GameSetUserPosition | 2.08 |
| 46 | 432 | 0.21 | 0.49 | inbound:GameSetUserPosition | 0.71 |
| 47 | 431 | 0.16 | 0.19 | inbound:GameConnectionCheck | 3.19 |
| 48 | 432 | 0.24 | 2.77 | inbound:GameSetUserPosition | 2.70 |
| 49 | 436 | 0.96 | 3.31 | inbound:GameConnectionCheck | 5.81 |
| 50 | 427 | 0.16 | 0.21 | inbound:GameSetUserPosition | 0.15 |
| 51 | 431 | 0.17 | 0.39 | inbound:GameSetUserPosition | 2.77 |
| 52 | 433 | 21.81 | 34.16 | inbound:GameSetUserPosition | 37.89 |
| 53 | 442 | 30.47 | 34.19 | inbound:GameConnectionCheck | 33.99 |
| 54 | 431 | 13.08 | 20.65 | inbound:GameSetUserPosition | 26.84 |
| 55 | 431 | 13.06 | 18.85 | inbound:GameConnectionCheck | 21.34 |
| 56 | 433 | 18.45 | 27.78 | inbound:GameConnectionCheck | 32.66 |
| 57 | 432 | 8.28 | 15.78 | inbound:GameSetUserPosition | 22.79 |
| 58 | 439 | 27.92 | 28.01 | inbound:GameConnectionCheck | 32.80 |
| 59 | 443 | 24.21 | 29.32 | inbound:GameConnectionCheck | 9.24 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25427 | 4305 | 7.2% | inbound:GameSelectCharacter 1842 ms／21／max 229.9；inbound:GameEplpCommand 541 ms／14／max 57.56；inbound:GameEnterSelectDungeon 444 ms／18／max 53.37；inbound:GameSelectDungeon 334 ms／18／max 73.48；inbound:GameSetUserPosition 320 ms／9623／max 4.38；inbound:GameSetUserArea 176 ms／12／max 24.14；inbound:GameConnectionCheck 119 ms／13102／max 4.33；event:achievement.inventory 103 ms／210／max 8.12 |
| 稳态窗 | 120.0 | 52088 | 4130 | 3.4% | inbound:GameEplpCommand 1346 ms／35／max 58.64；inbound:GameEnterSelectDungeon 946 ms／35／max 37.62；inbound:GameSetUserPosition 778 ms／20409／max 5.65；inbound:GameSelectDungeon 542 ms／35／max 19.92；inbound:GameConnectionCheck 254 ms／27368／max 3.85；inbound:GameRequestPeer 75 ms／36／max 3.20；timer:JoustMaintenanceService 34 ms／120／max 0.40；timer:CreatureClockService 28 ms／120／max 1.19 |
| (d) 10 并发登录的轮内 | 1.2 | 891 | 89 | 7.3% | inbound:GameEplpCommand 30 ms／1／max 29.84；inbound:GameSelectDungeon 18 ms／1／max 17.76；inbound:GameEnterSelectDungeon 15 ms／1／max 15.40；http:POST /api/launcher/v1/register 10 ms／60／max 0.89；inbound:GameLogin 7 ms／60／max 0.30；http:POST /api/launcher/v1/login 4 ms／60／max 0.27；inbound:GameSetUserPosition 2 ms／199／max 0.08；inbound:GameConnectionCheck 1 ms／354／max 0.03 |
| (e) 灌包期间 | 30.0 | 1944591 | 8507 | 28.3% | inbound:GameConnectionCheck 7991 ms／1938220／max 7.45；inbound:GameEplpCommand 237 ms／9／max 37.64；inbound:GameSetUserPosition 109 ms／5311／max 5.96；inbound:GameEnterSelectDungeon 75 ms／6／max 17.90；inbound:GameSelectDungeon 68 ms／6／max 14.18；inbound:GameRequestPeer 8 ms／7／max 3.23；timer:JoustMaintenanceService 3 ms／30／max 0.13；inbound:GameLeaveParty 2 ms／12／max 0.27 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.8，`event:` 1.8。

