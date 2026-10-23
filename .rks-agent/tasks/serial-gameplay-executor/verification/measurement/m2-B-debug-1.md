# 基线测量 — m2bd1（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 4067 ms

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
| CHECK_USER_CONNECTION | 26801 | 55.84% |
| SET_USER_POSITION | 20338 | 42.37% |
| STEP_FENCE | 574 | 1.20% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |
| EPLP | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26801 | 0.33 | 2.01 | 15.07 | 46.08 | 58.51 | 0 | 14.77 | 46.08 | 58.51 |
| SET_USER_POSITION | 20338 | 0.45 | 2.15 | 11.83 | 49.10 | 55.30 | 0 | 11.70 | 49.10 | 55.26 |
| FINISH_LOADING | 72** | 0.18 | 0.33 | 10.18 | 10.18 | 10.18 | 0 | 10.18 | 10.18 | 10.18 |
| LEAVE_PARTY | 36** | 0.74 | 3.70 | 3.74 | 3.74 | 3.74 | 0 | 3.74 | 3.74 | 3.74 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.49 | 0.79 | 4.85 | 4.85 | 4.85 | 0 | 4.85 | 4.85 | 4.85 |
| ENTER_SELECT_DUNGEON | 36** | 24.32 | 37.51 | 40.61 | 40.61 | 40.61 | 0 | 37.51 | 37.51 | 37.51 |
| SELECT_DUNGEON | 36** | 15.37 | 21.16 | 21.87 | 21.87 | 21.87 | 0 | 21.87 | 21.87 | 21.87 |
| EPLP | 35** | 37.85 | 58.02 | 58.20 | 58.20 | 58.20 | 0 | 58.20 | 58.20 | 58.20 |
| **全部计入的请求** | 47390 | 0.39 | 2.09 | 16.33 | 49.40 | 58.51 | 36 | 16.09 | 49.40 | 58.51 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47426 | 0.05 | 1.58 | 2.12 | 3.37 | 25.03（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.91 | 16.29 | 98.56 | 98.56 | 98.56 | 0.4 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.91 | 56.78 | 60.07 | 60.07 | 60.07 | 4.5 | 0 |
| EnterDungeonCommand | Transactional | 80** | 8.59 | 11.59 | 55.55 | 55.55 | 55.55 | 13.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.91 | 35.07 | 36.99 | 36.99 | 36.99 | 2.7 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.81 | 21.19 | 35.26 | 35.26 | 35.26 | 2.7 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 14.72 | 22.72 | 27.39 | 27.39 | 27.39 | 0.5 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.24 | 2.75 | 23.78 | 23.78 | 23.78 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.57 | 3.38 | 16.91 | 16.91 | 16.91 | 18.7 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.03 | 0.27 | 5.98 | 5.98 | 5.98 | 8.7 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.24 | 1.85 | 5.59 | 5.59 | 5.59 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337* | 0.09 | 0.45 | 0.58 | 5.23 | 5.23 | 1.0 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.14 | 0.40 | 4.65 | 4.65 | 4.65 | 0.2 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 1.66 | 2.95 | 4.57 | 4.57 | 4.57 | 8.7 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.26 | 3.73 | 3.73 | 3.73 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.19 | 3.57 | 3.57 | 3.57 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.44 | 3.35 | 3.35 | 3.35 | 0.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.10 | 2.81 | 2.81 | 2.81 | 0.5 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 2.49 | 2.62 | 2.62 | 2.62 | 1.0 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.16 | 2.40 | 2.40 | 2.40 | 0.2 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 88** | 0.02 | 0.14 | 2.09 | 2.09 | 2.09 | 121.2 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.04 | 0.14 | 1.92 | 1.92 | 1.92 | 60.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.63 | 1.63 | 1.63 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.33 | 1.60 | 1.60 | 1.60 | 0.3 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.06 | 0.11 | 1.57 | 1.57 | 1.57 | 0.5 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.02 | 1.57 | 1.57 | 1.57 | 1.57 | 60.0 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.26 | 1.45 | 1.45 | 0.3 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.07 | 1.26 | 1.26 | 1.26 | 23.8 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.17 | 0.70 | 0.70 | 0.70 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.02 | 0.29 | 0.65 | 0.65 | 13.6 | 0 |

命令日志行 1754 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**232.4 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo00；参考口径 232.4 ms）。冷启动窗整体：22521 | 0.39 | 2.33 | 40.28 | 88.92 | 232.4（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 24 | 232.4 | 232.4 | SELECT_CHARACTER |
| 1 | 113 | 80.87 | 90.36 | SELECT_CHARACTER |
| 2 | 167 | 101.6 | 113.2 | SELECT_CHARACTER |
| 3 | 183 | 112.4 | 113.4 | CHECK_USER_CONNECTION |
| 4 | 324 | 104.1 | 110.6 | SET_USER_POSITION |
| 5 | 400 | 2.06 | 2.52 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.33 | 2.78 | CHECK_USER_CONNECTION |
| 7 | 400 | 2.30 | 2.73 | CHECK_USER_CONNECTION |
| 8 | 395 | 13.12 | 17.50 | RESPONSE_PEER |
| 9 | 384 | 3.99 | 6.21 | CHECK_USER_CONNECTION |
| 10 | 391 | 2.35 | 2.86 | CHECK_USER_CONNECTION |
| 11 | 400 | 2.17 | 2.98 | SET_USER_POSITION |
| 12 | 400 | 3.16 | 3.62 | SET_USER_POSITION |
| 13 | 385 | 69.96 | 88.92 | SELECT_DUNGEON |
| 14 | 391 | 2.32 | 2.62 | CHECK_USER_CONNECTION |
| 15 | 382 | 19.36 | 24.92 | ENTER_SELECT_DUNGEON |
| 16 | 394 | 2.13 | 2.37 | SET_USER_POSITION |
| 17 | 400 | 2.17 | 2.78 | SET_USER_POSITION |
| 18 | 397 | 43.85 | 50.60 | EPLP |
| 19 | 397 | 22.35 | 26.92 | EPLP |
| 20 | 398 | 48.57 | 50.49 | EPLP |
| 21 | 396 | 54.19 | 59.73 | EPLP |
| 22 | 400 | 2.15 | 2.65 | CHECK_USER_CONNECTION |
| 23 | 397 | 8.93 | 11.94 | LEAVE_PARTY |
| 24 | 397 | 2.15 | 2.48 | CHECK_USER_CONNECTION |
| 25 | 396 | 2.73 | 3.55 | SET_USER_POSITION |
| 26 | 398 | 2.37 | 4.75 | CHECK_USER_CONNECTION |
| 27 | 400 | 2.15 | 2.42 | SET_USER_POSITION |
| 28 | 390 | 2.91 | 3.83 | SET_USER_POSITION |
| 29 | 399 | 2.18 | 2.49 | CHECK_USER_CONNECTION |
| 30 | 391 | 2.11 | 2.21 | CHECK_USER_CONNECTION |
| 31 | 390 | 2.55 | 6.01 | SET_USER_POSITION |
| 32 | 400 | 2.17 | 3.04 | SET_USER_POSITION |
| 33 | 384 | 33.15 | 36.46 | ENTER_SELECT_DUNGEON |
| 34 | 392 | 27.14 | 32.59 | ENTER_SELECT_DUNGEON |
| 35 | 392 | 16.94 | 28.09 | ENTER_SELECT_DUNGEON |
| 36 | 384 | 10.71 | 13.95 | SELECT_DUNGEON |
| 37 | 400 | 2.48 | 4.02 | CHECK_USER_CONNECTION |
| 38 | 395 | 37.92 | 39.23 | EPLP |
| 39 | 399 | 2.68 | 5.74 | SET_USER_POSITION |
| 40 | 398 | 42.29 | 45.76 | EPLP |
| 41 | 396 | 51.94 | 54.51 | CHECK_USER_CONNECTION |
| 42 | 398 | 2.68 | 3.63 | SET_USER_POSITION |
| 43 | 398 | 2.18 | 3.08 | CHECK_USER_CONNECTION |
| 44 | 398 | 4.68 | 5.93 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.42 | 3.53 | SET_USER_POSITION |
| 46 | 396 | 2.90 | 13.76 | SET_USER_POSITION |
| 47 | 390 | 2.41 | 3.86 | CHECK_USER_CONNECTION |
| 48 | 400 | 2.07 | 2.67 | SET_USER_POSITION |
| 49 | 393 | 2.71 | 3.34 | CHECK_USER_CONNECTION |
| 50 | 397 | 2.54 | 3.90 | CHECK_USER_CONNECTION |
| 51 | 391 | 2.45 | 2.63 | CHECK_USER_CONNECTION |
| 52 | 391 | 33.63 | 38.68 | ENTER_SELECT_DUNGEON |
| 53 | 390 | 23.60 | 28.28 | ENTER_SELECT_DUNGEON |
| 54 | 394 | 2.30 | 2.66 | CHECK_USER_CONNECTION |
| 55 | 384 | 19.33 | 27.10 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.32 | 7.19 | SET_USER_POSITION |
| 57 | 390 | 23.82 | 33.62 | EPLP |
| 58 | 396 | 20.04 | 24.59 | EPLP |
| 59 | 398 | 39.64 | 47.62 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47390 | 0.39 | 2.09 | 16.33 | 49.40 | 58.51 |
| 10 并发登录的轮内 | 470* | 15.11 | 77.84 | 90.32 | 96.89 | 96.89 |
| （参考：实际发出口径）稳态窗 | 47390 | 0.22 | 0.54 | 16.09 | 49.40 | 58.51 |
| （参考：实际发出口径）轮内 | 470* | 5.18 | 74.86 | 87.52 | 89.09 | 89.09 |
| 登录阶段整体（182–232 s） | 19847 | 0.40 | 2.49 | 38.98 | 79.80 | 96.89 |

每轮时长（开始 → 10 个 LOGIN 都应答）：209, 206, 209, 180, 208, 196 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.62 | 2.29 | 13.55 | 37.21 | 46.28 | 13.08 |
| 单人 | 轮内 | 194* | 12.85 | 80.36 | 96.49 | 96.89 | 96.89 | 89.08 |
| 双人组领队 | 稳态窗 | 14077 | 0.32 | 1.64 | 23.59 | 52.67 | 58.20 | 23.42 |
| 双人组领队 | 轮内 | 138* | 15.86 | 77.84 | 84.51 | 86.65 | 86.65 | 78.56 |
| 双人组队员 | 稳态窗 | 14113 | 0.32 | 1.52 | 14.33 | 50.91 | 58.51 | 14.19 |
| 双人组队员 | 轮内 | 138* | 15.11 | 77.85 | 84.51 | 86.51 | 86.51 | 78.58 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 100.1 | 110.9 | 119.8 | 119.8 | 119.8 |
| 启动器登录（HTTP，含校验） | 60** | 88.00 | 97.35 | 99.74 | 99.74 | 99.74 |
| Game LOGIN 0x0001 | 60** | 0.19 | 0.74 | 7.97 | 7.97 | 7.97 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 63942 包/秒（1918455 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47390 | 0.39 | 2.09 | 16.33 | 49.40 | 58.51 |
| 其余连接，灌包期间 | 11861 | 0.47 | 4.56 | 7.45 | 30.88 | 39.62 |
| （参考：实际发出口径）其余连接，灌包期间 | 11861 | 0.10 | 4.12 | 7.00 | 30.60 | 38.32 |
| 灌包连接自身（含其窗口内排队） | 1918486 | 0.35 | 0.46 | 5.26 | 7.30 | 45.36 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 4693 | leave dungeon | 41.1 |
| EPLP | 请求者 | 78 | 3695 | leave dungeon | 21.5 |
| SET_USER_AREA | 同伴 | 6 | 3189 | town | 5.0 |
| SET_USER_AREA | 请求者 | 12 | 2962 | town | 5.0 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 2034 | party dungeon entry | 197.7 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 2034 | party dungeon entry | 197.7 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.5 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.5 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 82 | 294 | party | 206.6 |
| RESPONSE_PEER | 请求者 | 82 | 292 | party | 206.6 |
| LEAVE_PARTY | 同伴 | 78 | 235 | leave party | 23.8 |
| REQUEST_PEER | 同伴 | 82 | 187 | party | 173.3 |
| LEAVE_PARTY | 请求者 | 78 | 172 | leave party | 65.7 |
| REQUEST_PEER | 请求者 | 82 | 159 | party | 173.3 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.3 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 13.6 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 13.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 16.33 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 90.32 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 7.45 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 0 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 232.4 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- 无。

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 2037273 行（Host 就绪之后 2037268 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 75.77 | 112.6 | 227.5 | 227.5 | 227.5 | 227.5 | 227.5 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 80** | 14.59 | 19.14 | 72.64 | 72.64 | 72.64 | 72.64 | 72.64 | 21.72 | 13.6 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 24.01 | 37.29 | 62.25 | 62.25 | 62.25 | 62.25 | 62.25 | 46.40 | 13.5 | 0 |
| inbound:GameEplpCommand | 78** | 34.56 | 57.53 | 59.52 | 59.52 | 59.52 | 48.89 | 59.25 | 59.52 | 204.5 | 0 |
| inbound:GameSetUserArea | 12** | 14.17 | 36.86 | 36.86 | 36.86 | 36.86 | 15.96 | 36.86 | – | 5.0 | 0 |
| inbound:GameCreateCharacter | 21** | 1.69 | 4.71 | 31.45 | 31.45 | 31.45 | 31.45 | 31.45 | – | 0.3 | 0 |
| inbound:GameSetUserPosition | 45986 | 0.03 | 0.10 | 0.14 | 2.94 | 22.74 | 1.90 | 4.51 | 22.74 | 273.0 | 0 |
| inbound:GameResponsePeer | 82** | 0.38 | 0.91 | 17.11 | 17.11 | 17.11 | 17.11 | 17.11 | 0.73 | 8.7 | 0 |
| inbound:GameRequestPeer | 82** | 2.06 | 3.82 | 16.53 | 16.53 | 16.53 | 16.53 | 16.53 | 3.83 | 8.7 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.73 | 14.62 | 14.62 | 14.62 | 1.73 | 1.73 | 14.62 | 274.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 1.19 | 11.65 | 11.65 | 11.65 | 11.65 | 11.65 | 11.65 | 2.32 | 60.0 | 0 |
| inbound:GameLeaveParty | 78** | 0.55 | 1.99 | 11.43 | 11.43 | 11.43 | 11.43 | 11.43 | 3.57 | 23.8 | 0 |
| inbound:GameLogin | 81** | 0.14 | 0.48 | 9.32 | 9.32 | 9.32 | 9.32 | 9.32 | 7.72 | 0.3 | 0 |
| inbound:GameConnectionCheck | 1980459 | 0.00 | 0.00 | 0.02 | 0.04 | 8.76 | 0.13 | 3.48 | 8.76 | 273.7 | 0 |
| timer:CreatureClockService | 277* | 0.02 | 1.05 | 1.39 | 8.43 | 8.43 | 8.43 | 8.43 | 1.70 | 1.0 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.15 | 0.53 | 7.23 | 7.23 | 7.23 | 7.23 | 7.23 | 0.87 | 0.2 | 0 |
| event:achievement.inventory | 210* | 0.33 | 1.08 | 3.35 | 7.15 | 7.15 | 7.15 | 7.15 | – | 0.3 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.75 | 5.41 | 5.44 | 5.44 | 5.44 | 5.34 | 5.44 | – | 0.8 | 0 |
| event:quest.inventory | 210* | 0.11 | 0.57 | 3.22 | 4.61 | 4.61 | 4.61 | 4.61 | – | 0.3 | 0 |
| timer:JoustMaintenanceService | 277* | 0.23 | 0.42 | 0.57 | 4.30 | 4.30 | 0.17 | 4.30 | 0.60 | 5.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 4.17 | 4.17 | 4.17 | 4.17 | 4.17 | 0.04 | 0.1 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.86 | 3.65 | 3.65 | 3.65 | 3.65 | 2.62 | 2.62 | 3.65 | 180.0 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.10 | 0.22 | 0.32 | 3.55 | 3.55 | 1.81 | 3.55 | 0.32 | 44.0 | 0 |
| inbound:GameFinishLoading | 160* | 0.10 | 0.24 | 1.48 | 3.40 | 3.40 | 3.40 | 3.40 | 0.94 | 13.6 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.01 | 3.17 | 3.17 | 3.17 | 3.17 | 3.17 | 3.17 | 0.02 | 60.0 | 0 |
| event:quest.mirror-leave | 160* | 0.15 | 0.32 | 0.68 | 3.09 | 3.09 | 3.09 | 3.09 | 0.44 | 18.7 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.42 | 2.90 | 2.90 | 2.90 | 2.90 | 2.90 | 2.90 | 2.29 | 60.0 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 1.62 | 2.88 | 2.88 | 2.88 | 2.88 | 2.00 | 2.00 | 2.88 | 121.2 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.07 | 0.25 | 2.77 | 2.77 | 2.77 | 2.77 | 2.77 | 0.25 | 0.2 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.03 | 2.31 | 2.31 | 2.31 | 2.31 | 2.31 | 2.31 | 0.44 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.02 | 2.22 | 2.22 | 2.22 | 2.22 | 2.22 | 2.22 | 0.54 | 60.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.01 | 1.45 | 1.45 | 1.45 | 1.45 | 1.45 | 1.45 | 0.04 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.05 | 0.07 | 0.40 | 1.38 | 1.38 | 1.38 | 0.35 | 0.2 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.01 | 1.37 | 1.37 | 1.37 | 1.37 | 1.37 | 1.37 | 0.02 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.01 | 1.35 | 1.35 | 1.35 | 1.35 | 1.35 | 1.35 | 0.01 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.04 | 1.09 | 1.09 | 1.09 | 1.09 | 0.14 | 0.5 | 0 |
| event:tournament.departure | 160* | 0.12 | 0.22 | 0.29 | 0.97 | 0.97 | 0.22 | 0.22 | 0.97 | 158.3 | 0 |
| event:altar.departure | 160* | 0.15 | 0.33 | 0.64 | 0.92 | 0.92 | 0.58 | 0.92 | 0.64 | 39.0 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.04 | 0.87 | 0.87 | 0.87 | 0.87 | 0.09 | 0.5 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.01 | 0.77 | 0.77 | 0.77 | 0.77 | 0.77 | 0.77 | 0.01 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.02 | 0.68 | 0.68 | 0.68 | 0.68 | 0.68 | 0.68 | 0.04 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5537 | 0.01 | 0.01 | 0.02 | 0.06 | 0.58 | 0.58 | 0.58 | 0.10 | 0.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.25 | 0.25 | 0.25 | 0.25 | 0.25 | 0.25 | 0.05 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.02 | 0.05 | 0.05 | 0.05 | 0.05 | 0.05 | 0.05 | 100.9 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 16.33 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25426 | 0.01 | 0.61 | 40.52 | 87.88 | 231.8 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22920 | 0.01 | 0.20 | 31.26 | 79.24 | 111.7 |  |  |
| 稳态窗 | 全部作业 | 52089 | 0.01 | 0.15 | 16.56 | 45.61 | 57.91 | inbound:GameConnectionCheck | 141.9 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.14 | 12.74 | 46.06 | 57.91 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 858* | 0.03 | 0.52 | 1.00 | 3.50 | 3.50 | timer:TournamentRewardSweepService | 202.0 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 594* | 0.05 | 0.55 | 1.00 | 1.00 | 1.00 |  |  |
| (e) 灌包期间 | 全部作业 | 1931495 | 0.00 | 0.00 | 0.01 | 0.05 | 38.13 | inbound:GameConnectionCheck | 242.0 |
| (e) 灌包期间 | 只算 inbound: | 1930494 | 0.00 | 0.00 | 0.01 | 0.05 | 38.13 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 115 | 229.6 | 231.8 | event:achievement.inventory | 227.5 |
| 1 | 280 | 79.10 | 87.16 | event:quest.inventory | 85.12 |
| 2 | 327 | 92.11 | 113.1 | event:quest.inventory | 112.6 |
| 3 | 323 | 100.6 | 111.9 | event:quest.inventory | 111.5 |
| 4 | 506 | 97.21 | 110.3 | inbound:GameSetUserPosition | 108.7 |
| 5 | 432 | 0.09 | 0.10 | inbound:GameConnectionCheck | 0.92 |
| 6 | 431 | 0.21 | 0.37 | inbound:GameSetUserPosition | 1.72 |
| 7 | 431 | 0.18 | 0.21 | inbound:GameConnectionCheck | 0.30 |
| 8 | 432 | 12.58 | 15.06 | inbound:GameSetUserPosition | 17.11 |
| 9 | 435 | 0.29 | 6.03 | inbound:GameConnectionCheck | 7.24 |
| 10 | 428 | 0.26 | 1.06 | inbound:GameSetUserPosition | 2.93 |
| 11 | 432 | 0.22 | 0.38 | timer:IllusionTowerReturnSweepService | 1.99 |
| 12 | 432 | 0.37 | 1.42 | inbound:GameConnectionCheck | 1.39 |
| 13 | 434 | 69.81 | 83.80 | inbound:GameConnectionCheck | 72.64 |
| 14 | 431 | 0.23 | 6.24 | timer:TournamentRewardSweepService | 12.84 |
| 15 | 441 | 12.97 | 23.69 | inbound:GameConnectionCheck | 24.69 |
| 16 | 426 | 0.10 | 1.16 | inbound:GameConnectionCheck | 0.14 |
| 17 | 431 | 0.09 | 0.09 | inbound:GameConnectionCheck | 0.10 |
| 18 | 446 | 42.45 | 43.42 | inbound:GameConnectionCheck | 48.89 |
| 19 | 438 | 16.92 | 24.18 | inbound:GameConnectionCheck | 26.69 |
| 20 | 439 | 45.73 | 48.21 | inbound:GameConnectionCheck | 49.49 |
| 21 | 445 | 50.86 | 54.51 | inbound:GameConnectionCheck | 59.25 |
| 22 | 432 | 0.19 | 0.36 | inbound:GameConnectionCheck | 0.44 |
| 23 | 432 | 1.62 | 9.97 | inbound:GameConnectionCheck | 11.43 |
| 24 | 431 | 0.18 | 0.89 | inbound:GameSetUserPosition | 1.99 |
| 25 | 430 | 0.17 | 0.41 | inbound:GameConnectionCheck | 3.21 |
| 26 | 433 | 0.26 | 4.55 | inbound:GameConnectionCheck | 4.51 |
| 27 | 430 | 0.19 | 0.28 | timer:IllusionTowerReturnSweepService | 0.34 |
| 28 | 433 | 0.30 | 1.32 | inbound:GameSetUserPosition | 2.99 |
| 29 | 435 | 0.29 | 1.02 | inbound:GameConnectionCheck | 2.19 |
| 30 | 434 | 0.23 | 0.55 | inbound:GameSetUserPosition | 2.89 |
| 31 | 426 | 0.26 | 3.11 | inbound:GameSetUserPosition | 2.97 |
| 32 | 431 | 0.20 | 0.30 | inbound:GameSetUserPosition | 2.78 |
| 33 | 433 | 26.62 | 34.09 | inbound:GameSetUserPosition | 36.26 |
| 34 | 434 | 25.22 | 31.27 | inbound:GameSetUserPosition | 32.26 |
| 35 | 431 | 14.29 | 25.29 | inbound:GameConnectionCheck | 26.33 |
| 36 | 434 | 9.29 | 12.26 | inbound:GameSetUserPosition | 13.82 |
| 37 | 432 | 0.12 | 0.20 | timer:TournamentRewardSweepService | 0.28 |
| 38 | 453 | 35.02 | 37.67 | inbound:GameConnectionCheck | 38.92 |
| 39 | 431 | 0.31 | 3.98 | inbound:GameConnectionCheck | 3.91 |
| 40 | 438 | 40.68 | 44.46 | inbound:GameSetUserPosition | 45.47 |
| 41 | 446 | 38.14 | 53.99 | inbound:GameConnectionCheck | 53.94 |
| 42 | 433 | 0.26 | 0.41 | inbound:GameSetUserPosition | 1.56 |
| 43 | 429 | 0.26 | 3.71 | timer:TournamentRewardSweepService | 3.55 |
| 44 | 433 | 0.27 | 3.69 | inbound:GameSetUserPosition | 3.66 |
| 45 | 430 | 0.19 | 2.51 | inbound:GameSetUserPosition | 2.29 |
| 46 | 433 | 0.22 | 1.24 | inbound:GameSetUserPosition | 1.42 |
| 47 | 431 | 0.21 | 2.62 | inbound:GameConnectionCheck | 2.64 |
| 48 | 431 | 0.20 | 1.81 | inbound:GameConnectionCheck | 1.78 |
| 49 | 434 | 0.29 | 2.14 | inbound:GameSetUserPosition | 3.02 |
| 50 | 430 | 0.26 | 0.45 | inbound:GameConnectionCheck | 3.01 |
| 51 | 431 | 0.19 | 0.25 | inbound:GameSetUserPosition | 2.21 |
| 52 | 433 | 31.96 | 37.01 | inbound:GameConnectionCheck | 37.83 |
| 53 | 439 | 17.81 | 25.32 | inbound:GameConnectionCheck | 27.89 |
| 54 | 425 | 0.16 | 0.24 | timer:BossTowerReturnSweepService | 0.26 |
| 55 | 434 | 13.69 | 24.31 | inbound:GameConnectionCheck | 26.84 |
| 56 | 431 | 0.14 | 0.34 | timer:CreatureClockService | 0.42 |
| 57 | 448 | 21.01 | 26.69 | inbound:GameConnectionCheck | 32.31 |
| 58 | 437 | 17.81 | 22.06 | inbound:GameConnectionCheck | 24.43 |
| 59 | 450 | 33.42 | 44.70 | inbound:GameConnectionCheck | 47.19 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25426 | 4473 | 7.5% | inbound:GameSelectCharacter 1815 ms／21／max 227.5；inbound:GameEplpCommand 588 ms／16／max 59.25；inbound:GameSetUserPosition 437 ms／9590／max 4.51；inbound:GameEnterSelectDungeon 409 ms／18／max 62.25；inbound:GameSelectDungeon 329 ms／18／max 72.64；inbound:GameSetUserArea 189 ms／12／max 36.86；inbound:GameConnectionCheck 143 ms／13119／max 3.48；event:achievement.inventory 104 ms／210／max 7.15 |
| 稳态窗 | 120.0 | 52089 | 4291 | 3.6% | inbound:GameEplpCommand 1321 ms／35／max 57.68；inbound:GameSetUserPosition 967 ms／20338／max 5.12；inbound:GameEnterSelectDungeon 828 ms／36／max 37.29；inbound:GameSelectDungeon 545 ms／36／max 21.72；inbound:GameConnectionCheck 340 ms／27435／max 5.49；inbound:GameRequestPeer 78 ms／36／max 3.83；timer:JoustMaintenanceService 31 ms／120／max 0.60；timer:CreatureClockService 29 ms／120／max 1.70 |
| (d) 10 并发登录的轮内 | 1.2 | 858 | 35 | 2.9% | inbound:GameLogin 16 ms／60／max 7.72；http:POST /api/launcher/v1/register 10 ms／60／max 0.87；http:POST /api/launcher/v1/login 4 ms／60／max 0.25；inbound:GameSetUserPosition 2 ms／202／max 0.07；inbound:GameConnectionCheck 1 ms／331／max 0.08；lifecycle:connected 1 ms／60／max 0.04；lifecycle:disconnected 0 ms／54／max 0.09；inbound:GameLeaveParty 0 ms／1／max 0.26 |
| (e) 灌包期间 | 30.0 | 1931495 | 8619 | 28.7% | inbound:GameConnectionCheck 8061 ms／1925170／max 7.57；inbound:GameEplpCommand 250 ms／9／max 38.08；inbound:GameSetUserPosition 133 ms／5259／max 6.43；inbound:GameSelectDungeon 78 ms／6／max 13.99；inbound:GameEnterSelectDungeon 67 ms／6／max 16.86；inbound:GameRequestPeer 7 ms／10／max 0.92；timer:JoustMaintenanceService 3 ms／30／max 0.16；event:altar.departure 3 ms／18／max 0.64 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.8，`event:` 1.8。

