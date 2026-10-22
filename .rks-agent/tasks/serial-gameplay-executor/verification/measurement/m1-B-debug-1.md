# 基线测量 — m1bd1（合成负载）

> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。

## 环境

- 机器：Apple M5，10 逻辑核，内存 32 GiB，macOS 27.0.1
- 磁盘（数据库所在卷 `/`，临时目录 `/var/folders/qc/sl_p_wxs2qvg3pn_zknp1ppw0000gn/T/`）：设备 /dev/disk3s5，文件系统 APFS，Internal，Apple Fabric，Solid State: Yes（`diskutil info /`：Solid State:               Yes）
- .NET 运行时（工具进程）：.NET 10.0.10
- Host：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，构建配置 Release，Host.dll 修改时间 2026-10-22T18:50:41Z
- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），额外设置 `Logging__Console__FormatterOptions__SingleLine=true`
- SQLite：版本 3.53.4；`PRAGMA journal_mode` = **wal**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；`PRAGMA synchronous` = **2**（FULL；连接级，取值见下）
  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：
  - Host 原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3865 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（47999 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26798 | 55.83% |
| SET_USER_POSITION | 20343 | 42.38% |
| STEP_FENCE | 572 | 1.19% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 36 | 0.08% |
| REQUEST_PEER | 36 | 0.08% |
| RESPONSE_PEER | 36 | 0.08% |
| ENTER_SELECT_DUNGEON | 36 | 0.08% |
| SELECT_DUNGEON | 36 | 0.08% |
| EPLP | 34 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26798 | 0.37 | 2.15 | 14.40 | 42.31 | 58.67 | 0 | 14.03 | 42.07 | 58.20 |
| SET_USER_POSITION | 20343 | 0.39 | 2.11 | 10.22 | 33.70 | 58.20 | 0 | 9.50 | 33.24 | 58.20 |
| FINISH_LOADING | 72** | 0.17 | 0.30 | 0.97 | 0.97 | 0.97 | 0 | 0.97 | 0.97 | 0.97 |
| LEAVE_PARTY | 36** | 0.68 | 1.63 | 1.79 | 1.79 | 1.79 | 0 | 1.79 | 1.79 | 1.79 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.33 | 0.51 | 0.59 | 0.59 | 0.59 | 0 | 0.59 | 0.59 | 0.59 |
| ENTER_SELECT_DUNGEON | 36** | 25.28 | 38.80 | 38.84 | 38.84 | 38.84 | 0 | 37.97 | 37.97 | 37.97 |
| SELECT_DUNGEON | 36** | 14.82 | 20.80 | 22.92 | 22.92 | 22.92 | 0 | 22.92 | 22.92 | 22.92 |
| EPLP | 34** | 16.79 | 21.96 | 25.64 | 25.64 | 25.64 | 0 | 23.01 | 23.01 | 23.01 |
| **全部计入的请求** | 47391 | 0.38 | 2.16 | 14.65 | 40.46 | 58.67 | 36 | 14.16 | 40.03 | 58.20 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47427 | 0.12 | 1.65 | 2.25 | 2.68 | 8.68（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.93 | 6.62 | 102.1 | 102.1 | 102.1 | 0.4 | 0 |
| EnterDungeonCommand | Transactional | 80** | 8.76 | 11.84 | 55.47 | 55.47 | 55.47 | 13.2 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.25 | 21.17 | 53.35 | 53.35 | 53.35 | 4.2 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 15.64 | 47.91 | 51.25 | 51.25 | 51.25 | 2.5 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 15.62 | 22.94 | 29.21 | 29.21 | 29.21 | 0.5 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 19.35 | 25.12 | 27.27 | 27.27 | 27.27 | 2.7 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.14 | 1.90 | 23.68 | 23.68 | 23.68 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.55 | 2.30 | 16.40 | 16.40 | 16.40 | 18.2 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.12 | 0.62 | 6.33 | 6.33 | 6.33 | 0.2 | 0 |
| RequestPartyInviteCommand | Direct | 84** | 1.69 | 2.81 | 6.04 | 6.04 | 6.04 | 50.8 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.28 | 0.46 | 5.80 | 5.80 | 5.80 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 84** | 0.03 | 0.27 | 5.11 | 5.11 | 5.11 | 8.5 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.03 | 0.06 | 4.65 | 4.65 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.08 | 0.13 | 3.81 | 3.81 | 3.81 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.43 | 3.61 | 3.61 | 3.61 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.16 | 3.46 | 3.46 | 3.46 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.28 | 3.43 | 3.43 | 3.43 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 335* | 0.10 | 0.36 | 0.40 | 3.15 | 3.15 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.03 | 0.08 | 2.92 | 2.92 | 2.92 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.13 | 1.92 | 1.92 | 1.92 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.06 | 0.28 | 1.65 | 1.65 | 1.65 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.05 | 1.61 | 1.61 | 1.61 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.06 | 0.09 | 1.51 | 1.51 | 1.51 | 0.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 84** | 0.02 | 0.07 | 1.42 | 1.42 | 1.42 | 42.1 | 0 |
| LeavePartyCommand | Direct | 78** | 0.04 | 0.11 | 1.21 | 1.21 | 1.21 | 22.4 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.03 | 1.20 | 1.20 | 1.20 | 1.20 | 60.0 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.02 | 0.59 | 0.82 | 0.82 | 75.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.04 | 0.05 | 0.72 | 0.72 | 0.72 | 0.3 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.16 | 0.44 | 0.44 | 0.44 | 60.0 | 0 |

命令日志行 1752 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**134.1 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo05；参考口径 134.1 ms）。冷启动窗整体：22551 | 0.39 | 2.50 | 31.62 | 79.28 | 134.1（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 24 | 134.1 | 134.1 | SELECT_CHARACTER |
| 1 | 115 | 63.31 | 75.49 | CHECK_USER_CONNECTION |
| 2 | 172 | 107.1 | 113.8 | SET_USER_POSITION |
| 3 | 191 | 92.97 | 96.73 | SET_USER_POSITION |
| 4 | 338 | 84.27 | 97.64 | SET_USER_POSITION |
| 5 | 400 | 2.82 | 3.47 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.67 | 3.01 | CHECK_USER_CONNECTION |
| 7 | 400 | 2.60 | 3.01 | SET_USER_POSITION |
| 8 | 390 | 15.77 | 16.07 | SET_USER_POSITION |
| 9 | 387 | 2.64 | 8.90 | CHECK_USER_CONNECTION |
| 10 | 393 | 2.63 | 6.85 | SET_USER_POSITION |
| 11 | 400 | 2.08 | 2.88 | SET_USER_POSITION |
| 12 | 400 | 3.78 | 5.11 | CHECK_USER_CONNECTION |
| 13 | 384 | 72.87 | 74.52 | SELECT_DUNGEON |
| 14 | 392 | 10.52 | 12.48 | SELECT_DUNGEON |
| 15 | 376 | 14.91 | 27.31 | ENTER_SELECT_DUNGEON |
| 16 | 400 | 2.17 | 2.53 | CHECK_USER_CONNECTION |
| 17 | 400 | 2.43 | 2.81 | SET_USER_POSITION |
| 18 | 396 | 48.01 | 48.75 | CHECK_USER_CONNECTION |
| 19 | 398 | 15.39 | 23.55 | CHECK_USER_CONNECTION |
| 20 | 398 | 24.00 | 32.03 | CHECK_USER_CONNECTION |
| 21 | 396 | 29.38 | 35.80 | CHECK_USER_CONNECTION |
| 22 | 398 | 12.87 | 15.32 | LEAVE_PARTY |
| 23 | 400 | 2.55 | 2.69 | CHECK_USER_CONNECTION |
| 24 | 396 | 2.54 | 3.85 | LEAVE_PARTY |
| 25 | 396 | 5.45 | 5.87 | SET_USER_POSITION |
| 26 | 393 | 2.62 | 3.16 | CHECK_USER_CONNECTION |
| 27 | 400 | 2.42 | 3.94 | SET_USER_POSITION |
| 28 | 395 | 2.73 | 3.05 | SET_USER_POSITION |
| 29 | 395 | 2.52 | 2.86 | SET_USER_POSITION |
| 30 | 392 | 6.06 | 6.60 | SET_USER_POSITION |
| 31 | 385 | 34.07 | 39.24 | ENTER_SELECT_DUNGEON |
| 32 | 400 | 2.54 | 2.86 | CHECK_USER_CONNECTION |
| 33 | 401 | 19.31 | 31.17 | ENTER_SELECT_DUNGEON |
| 34 | 391 | 2.59 | 2.86 | CHECK_USER_CONNECTION |
| 35 | 382 | 16.55 | 25.76 | ENTER_SELECT_DUNGEON |
| 36 | 386 | 13.83 | 22.98 | SELECT_DUNGEON |
| 37 | 398 | 22.01 | 26.79 | CHECK_USER_CONNECTION |
| 38 | 398 | 10.44 | 16.98 | CHECK_USER_CONNECTION |
| 39 | 397 | 41.06 | 45.44 | SET_USER_POSITION |
| 40 | 399 | 2.48 | 2.94 | CHECK_USER_CONNECTION |
| 41 | 396 | 46.52 | 51.97 | SET_USER_POSITION |
| 42 | 398 | 7.40 | 8.04 | SET_USER_POSITION |
| 43 | 398 | 2.50 | 2.84 | SET_USER_POSITION |
| 44 | 398 | 2.48 | 3.27 | SET_USER_POSITION |
| 45 | 398 | 2.82 | 3.45 | CHECK_USER_CONNECTION |
| 46 | 396 | 6.92 | 9.68 | CHECK_USER_CONNECTION |
| 47 | 400 | 2.52 | 4.13 | SET_USER_POSITION |
| 48 | 390 | 2.71 | 2.83 | SET_USER_POSITION |
| 49 | 395 | 2.65 | 2.78 | CHECK_USER_CONNECTION |
| 50 | 396 | 2.62 | 4.31 | SET_USER_POSITION |
| 51 | 389 | 2.59 | 3.05 | SET_USER_POSITION |
| 52 | 392 | 27.62 | 32.31 | ENTER_SELECT_DUNGEON |
| 53 | 392 | 17.53 | 28.65 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 19.27 | 30.16 | ENTER_SELECT_DUNGEON |
| 55 | 384 | 9.25 | 18.04 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 3.83 | 4.40 | SET_USER_POSITION |
| 57 | 380 | 17.28 | 18.67 | SET_USER_POSITION |
| 58 | 400 | 2.48 | 3.97 | CHECK_USER_CONNECTION |
| 59 | 397 | 32.87 | 41.35 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47391 | 0.38 | 2.16 | 14.65 | 40.46 | 58.67 |
| 10 并发登录的轮内 | 460* | 27.83 | 74.65 | 84.95 | 88.20 | 88.20 |
| （参考：实际发出口径）稳态窗 | 47391 | 0.19 | 0.46 | 14.16 | 40.03 | 58.20 |
| （参考：实际发出口径）轮内 | 460* | 9.61 | 72.17 | 82.75 | 85.72 | 85.72 |
| 登录阶段整体（182–232 s） | 19835 | 0.36 | 2.44 | 37.24 | 74.94 | 88.20 |

每轮时长（开始 → 10 个 LOGIN 都应答）：186, 195, 213, 187, 195, 196 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.38 | 2.14 | 13.44 | 35.54 | 57.51 | 13.31 |
| 单人 | 轮内 | 191* | 26.64 | 75.83 | 86.66 | 88.20 | 88.20 | 84.26 |
| 双人组领队 | 稳态窗 | 14078 | 0.40 | 2.26 | 16.99 | 40.96 | 58.20 | 16.73 |
| 双人组领队 | 轮内 | 135* | 27.90 | 72.13 | 83.38 | 84.95 | 84.95 | 82.56 |
| 双人组队员 | 稳态窗 | 14113 | 0.36 | 2.10 | 12.62 | 46.51 | 58.67 | 12.37 |
| 双人组队员 | 轮内 | 134* | 27.87 | 72.21 | 83.40 | 85.63 | 85.63 | 82.61 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 97.83 | 120.4 | 129.4 | 129.4 | 129.4 |
| 启动器登录（HTTP，含校验） | 60** | 84.88 | 92.54 | 95.49 | 95.49 | 95.49 |
| Game LOGIN 0x0001 | 60** | 0.20 | 0.71 | 2.07 | 2.07 | 2.07 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 85472 包/秒（2564185 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47391 | 0.38 | 2.16 | 14.65 | 40.46 | 58.67 |
| 其余连接，灌包期间 | 11856 | 0.24 | 1.99 | 3.89 | 15.38 | 29.57 |
| （参考：实际发出口径）其余连接，灌包期间 | 11856 | 0.08 | 0.19 | 2.27 | 14.81 | 29.31 |
| 灌包连接自身（含其窗口内排队） | 2564216 | 0.34 | 0.41 | 0.93 | 3.42 | 30.35 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3695 | leave dungeon | 142.9 |
| EPLP | 请求者 | 78 | 3647 | leave dungeon | 123.0 |
| SET_USER_AREA | 同伴 | 6 | 3045 | town | 4.8 |
| SET_USER_AREA | 请求者 | 12 | 2938 | town | 4.8 |
| LEAVE_PARTY | 同伴 | 78 | 1565 | leave party | 61.8 |
| LEAVE_PARTY | 请求者 | 78 | 1526 | leave party | 61.8 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.1 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 727 | party dungeon entry | 132.6 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 727 | party dungeon entry | 132.6 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 84 | 262 | party | 51.7 |
| RESPONSE_PEER | 请求者 | 84 | 260 | party | 51.7 |
| REQUEST_PEER | 同伴 | 84 | 195 | party | 71.2 |
| REQUEST_PEER | 请求者 | 84 | 143 | party | 71.2 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.2 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 13.2 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 13.2 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 14.65 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 84.95 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 3.89 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 134.1 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 102.1 ms，max 102.1 ms（出现于 t=0.4 s；冷启动窗内 max 102.1 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 84 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 2683329 行（Host 就绪之后 2683324 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 70.49 | 113.3 | 232.8 | 232.8 | 232.8 | 232.8 | 232.8 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 80** | 14.65 | 19.92 | 74.75 | 74.75 | 74.75 | 74.75 | 74.75 | 21.32 | 13.2 | 0 |
| inbound:GameEplpCommand | 78** | 33.22 | 52.65 | 59.31 | 59.31 | 59.31 | 52.65 | 52.65 | 59.31 | 143.0 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 23.75 | 37.22 | 46.56 | 46.56 | 46.56 | 46.56 | 46.56 | 37.87 | 13.1 | 0 |
| inbound:GameSetUserArea | 12** | 11.77 | 32.87 | 32.87 | 32.87 | 32.87 | 14.62 | 32.87 | – | 4.7 | 0 |
| inbound:GameCreateCharacter | 21** | 1.55 | 4.91 | 31.58 | 31.58 | 31.58 | 31.58 | 31.58 | – | 0.3 | 0 |
| inbound:GameRequestPeer | 84** | 2.01 | 5.12 | 17.36 | 17.36 | 17.36 | 17.36 | 17.36 | 6.17 | 8.5 | 0 |
| inbound:GameLeaveParty | 78** | 0.62 | 2.48 | 15.17 | 15.17 | 15.17 | 15.17 | 15.17 | 4.36 | 22.4 | 0 |
| inbound:GameResponsePeer | 84** | 0.41 | 0.82 | 15.01 | 15.01 | 15.01 | 15.01 | 15.01 | 2.19 | 8.5 | 0 |
| inbound:GameLogin | 81** | 0.15 | 0.40 | 12.32 | 12.32 | 12.32 | 12.32 | 12.32 | 0.41 | 0.2 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.14 | 0.69 | 8.87 | 8.87 | 8.87 | 8.87 | 8.87 | 0.85 | 0.2 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.92 | 8.22 | 8.22 | 8.22 | 3.26 | 3.26 | 8.22 | 274.0 | 0 |
| event:achievement.inventory | 210* | 0.32 | 1.09 | 2.96 | 7.67 | 7.67 | 7.67 | 7.67 | – | 0.3 | 0 |
| event:quest.inventory | 210* | 0.11 | 0.33 | 0.72 | 7.59 | 7.59 | 5.25 | 7.59 | – | 4.0 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 4** | 0.31 | 7.00 | 7.00 | 7.00 | 7.00 | 7.00 | 7.00 | 1.78 | 42.1 | 0 |
| timer:CreatureClockService | 277* | 0.02 | 0.91 | 1.53 | 6.82 | 6.82 | 6.82 | 6.82 | 1.68 | 1.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 3.01 | 6.20 | 6.20 | 6.20 | 6.20 | 6.20 | 6.20 | 3.15 | 60.0 | 0 |
| inbound:GameSetUserPosition | 46168 | 0.02 | 0.09 | 0.11 | 1.14 | 6.12 | 4.53 | 6.12 | 4.02 | 30.8 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.74 | 3.97 | 5.27 | 5.27 | 5.27 | 5.27 | 5.27 | – | 0.5 | 0 |
| timer:WeaknessRecoveryService | 4** | 1.07 | 5.11 | 5.11 | 5.11 | 5.11 | 2.11 | 2.11 | 5.11 | 180.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 4.16 | 4.16 | 4.16 | 4.16 | 4.16 | 0.04 | 0.1 | 0 |
| inbound:GameFinishLoading | 160* | 0.09 | 0.20 | 2.65 | 3.97 | 3.97 | 3.97 | 3.97 | 0.91 | 13.2 | 0 |
| inbound:GameConnectionCheck | 2626040 | 0.00 | 0.00 | 0.01 | 0.02 | 3.83 | 0.12 | 3.83 | 3.36 | 58.4 | 0 |
| timer:JoustMaintenanceService | 277* | 0.31 | 0.43 | 0.47 | 3.68 | 3.68 | 0.14 | 0.48 | 3.68 | 113.9 | 0 |
| event:quest.mirror-leave | 160* | 0.14 | 0.28 | 0.83 | 2.73 | 2.73 | 2.73 | 2.73 | 0.48 | 18.2 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.06 | 0.27 | 2.21 | 2.21 | 2.21 | 2.21 | 2.21 | 0.29 | 0.2 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.11 | 0.17 | 0.19 | 2.03 | 2.03 | 2.03 | 2.03 | 0.19 | 1.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.12 | 1.89 | 1.89 | 1.89 | 1.89 | 1.89 | 1.89 | 1.29 | 60.0 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.56 | 1.50 | 1.50 | 1.50 | 1.50 | 1.50 | 1.50 | 0.92 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.05 | 1.33 | 1.33 | 1.33 | 1.33 | 1.33 | 1.33 | 0.36 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1110 | 0.01 | 0.04 | 0.05 | 0.28 | 1.24 | 1.24 | 1.24 | 0.07 | 0.2 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.01 | 0.98 | 0.98 | 0.98 | 0.98 | 0.98 | 0.98 | 0.02 | 60.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.05 | 0.94 | 0.94 | 0.94 | 0.94 | 0.94 | 0.94 | 0.06 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 555* | 0.01 | 0.02 | 0.03 | 0.92 | 0.92 | 0.92 | 0.92 | 0.02 | 0.5 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.01 | 0.89 | 0.89 | 0.89 | 0.89 | 0.89 | 0.89 | 0.03 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.01 | 0.79 | 0.79 | 0.79 | 0.79 | 0.79 | 0.79 | 0.03 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 555* | 0.01 | 0.02 | 0.03 | 0.77 | 0.77 | 0.77 | 0.77 | 0.02 | 0.5 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.02 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.03 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.01 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.03 | 60.0 | 0 |
| event:tournament.departure | 160* | 0.11 | 0.18 | 0.54 | 0.60 | 0.60 | 0.22 | 0.22 | 0.60 | 259.2 | 0 |
| event:altar.departure | 160* | 0.13 | 0.23 | 0.33 | 0.58 | 0.58 | 0.58 | 0.58 | 0.33 | 18.2 | 0 |
| timer:TournamentRewardSweepService | 5546 | 0.00 | 0.01 | 0.01 | 0.02 | 0.48 | 0.48 | 0.48 | 0.02 | 0.0 | 0 |
| timer:NotificationRetryDrainService | 277* | 0.00 | 0.01 | 0.01 | 0.22 | 0.22 | 0.22 | 0.22 | 0.01 | 1.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.18 | 0.18 | 0.18 | 0.18 | 0.18 | 0.18 | 0.04 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.01 | 0.03 | 0.04 | 0.04 | 0.04 | 0.04 | 0.02 | 13.2 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 14.65 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25516 | 0.01 | 0.51 | 33.21 | 82.91 | 233.7 | event:quest.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22950 | 0.01 | 0.24 | 25.84 | 76.39 | 113.7 |  |  |
| 稳态窗 | 全部作业 | 52201 | 0.01 | 0.20 | 16.22 | 41.96 | 58.08 | inbound:GameConnectionCheck | 143.0 |
| 稳态窗 | 只算 inbound: | 48059 | 0.01 | 0.17 | 11.88 | 39.30 | 58.08 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 842* | 0.06 | 0.46 | 11.64 | 20.33 | 20.33 | http:POST /api/launcher/v1/register | 202.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 578* | 0.08 | 0.46 | 1.78 | 11.64 | 11.64 |  |  |
| (e) 灌包期间 | 全部作业 | 2577240 | 0.00 | 0.00 | 0.01 | 0.05 | 29.24 | inbound:GameConnectionCheck | 263.2 |
| (e) 灌包期间 | 只算 inbound: | 2576216 | 0.00 | 0.00 | 0.01 | 0.05 | 29.24 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 116 | 233.3 | 233.7 | event:quest.inventory | 232.8 |
| 1 | 268 | 73.88 | 77.11 | event:quest.inventory | 76.69 |
| 2 | 347 | 102.4 | 113.7 | inbound:GameSetUserPosition | 113.3 |
| 3 | 363 | 88.83 | 99.35 | event:quest.inventory | 99.05 |
| 4 | 490 | 82.91 | 109.0 | event:quest.inventory | 108.5 |
| 5 | 432 | 0.10 | 1.45 | inbound:GameSetUserPosition | 1.41 |
| 6 | 433 | 0.41 | 0.54 | inbound:GameSetUserPosition | 0.47 |
| 7 | 432 | 0.32 | 0.60 | inbound:GameSetUserPosition | 0.80 |
| 8 | 433 | 15.57 | 15.68 | inbound:GameSetUserPosition | 17.36 |
| 9 | 434 | 0.75 | 8.75 | inbound:GameConnectionCheck | 8.75 |
| 10 | 431 | 0.21 | 1.88 | inbound:GameConnectionCheck | 1.83 |
| 11 | 432 | 0.26 | 0.43 | inbound:GameConnectionCheck | 0.13 |
| 12 | 433 | 0.31 | 3.57 | inbound:GameConnectionCheck | 3.34 |
| 13 | 434 | 66.54 | 74.05 | inbound:GameConnectionCheck | 74.75 |
| 14 | 434 | 7.46 | 10.98 | inbound:GameConnectionCheck | 12.40 |
| 15 | 435 | 16.06 | 19.39 | timer:BossTowerReturnSweepService | 26.80 |
| 16 | 433 | 0.10 | 0.23 | timer:NotificationRetryDrainService | 0.22 |
| 17 | 432 | 0.11 | 0.38 | timer:NotificationRetryDrainService | 0.38 |
| 18 | 447 | 47.87 | 47.88 | inbound:GameConnectionCheck | 52.65 |
| 19 | 439 | 15.32 | 22.76 | inbound:GameConnectionCheck | 26.51 |
| 20 | 440 | 22.97 | 31.94 | inbound:GameConnectionCheck | 33.00 |
| 21 | 446 | 35.52 | 40.73 | timer:TournamentRewardSweepService | 51.41 |
| 22 | 433 | 12.71 | 12.76 | inbound:GameSetUserPosition | 15.17 |
| 23 | 432 | 0.29 | 0.44 | inbound:GameConnectionCheck | 0.38 |
| 24 | 433 | 0.31 | 0.43 | inbound:GameConnectionCheck | 3.95 |
| 25 | 432 | 0.27 | 0.42 | timer:NotificationRetryDrainService | 0.39 |
| 26 | 433 | 0.36 | 2.95 | inbound:GameConnectionCheck | 2.84 |
| 27 | 432 | 0.27 | 3.66 | inbound:GameSetUserPosition | 0.25 |
| 28 | 433 | 0.23 | 2.89 | inbound:GameSetUserPosition | 2.88 |
| 29 | 433 | 0.25 | 0.33 | inbound:GameConnectionCheck | 2.73 |
| 30 | 435 | 0.24 | 6.16 | inbound:GameSetUserPosition | 6.12 |
| 31 | 431 | 33.23 | 34.45 | inbound:GameSetUserPosition | 37.29 |
| 32 | 433 | 0.23 | 0.42 | timer:NotificationRetryDrainService | 2.74 |
| 33 | 430 | 18.60 | 29.92 | inbound:GameConnectionCheck | 31.15 |
| 34 | 436 | 5.03 | 14.58 | inbound:GameConnectionCheck | 15.72 |
| 35 | 441 | 11.68 | 16.31 | inbound:GameConnectionCheck | 25.48 |
| 36 | 428 | 9.71 | 22.66 | timer:TournamentRewardSweepService | 22.95 |
| 37 | 439 | 21.81 | 25.56 | inbound:GameConnectionCheck | 25.53 |
| 38 | 440 | 11.31 | 16.57 | inbound:GameConnectionCheck | 21.44 |
| 39 | 447 | 37.88 | 45.26 | inbound:GameSetUserPosition | 46.29 |
| 40 | 432 | 0.22 | 0.45 | timer:NotificationRetryDrainService | 0.43 |
| 41 | 446 | 46.26 | 51.16 | inbound:GameSetUserPosition | 51.08 |
| 42 | 436 | 6.65 | 6.85 | inbound:GameSetUserPosition | 7.00 |
| 43 | 430 | 0.42 | 0.63 | inbound:GameConnectionCheck | 0.46 |
| 44 | 433 | 0.26 | 0.33 | inbound:GameSetUserPosition | 0.96 |
| 45 | 434 | 0.25 | 3.28 | inbound:GameConnectionCheck | 3.24 |
| 46 | 431 | 0.22 | 0.28 | inbound:GameSetUserPosition | 0.84 |
| 47 | 432 | 0.30 | 3.77 | inbound:GameSetUserPosition | 3.45 |
| 48 | 433 | 0.28 | 0.64 | inbound:GameConnectionCheck | 0.71 |
| 49 | 432 | 0.24 | 1.32 | inbound:GameSetUserPosition | 2.14 |
| 50 | 434 | 0.30 | 3.93 | inbound:GameSetUserPosition | 6.35 |
| 51 | 431 | 0.26 | 0.43 | timer:NotificationRetryDrainService | 2.84 |
| 52 | 434 | 27.33 | 27.50 | inbound:GameSetUserPosition | 30.95 |
| 53 | 433 | 13.61 | 23.72 | inbound:GameSetUserPosition | 28.66 |
| 54 | 442 | 17.59 | 27.63 | inbound:GameConnectionCheck | 30.05 |
| 55 | 425 | 5.49 | 12.91 | inbound:GameSetUserPosition | 18.01 |
| 56 | 433 | 0.37 | 3.63 | inbound:GameConnectionCheck | 0.43 |
| 57 | 448 | 15.94 | 18.14 | inbound:GameConnectionCheck | 22.97 |
| 58 | 433 | 0.27 | 0.31 | inbound:GameConnectionCheck | 3.83 |
| 59 | 459 | 32.54 | 41.25 | inbound:GameConnectionCheck | 43.63 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25516 | 4233 | 7.1% | inbound:GameSelectCharacter 1762 ms／21／max 232.8；inbound:GameEplpCommand 575 ms／16／max 52.65；inbound:GameEnterSelectDungeon 400 ms／18／max 46.56；inbound:GameSetUserPosition 378 ms／9624／max 6.12；inbound:GameSelectDungeon 338 ms／18／max 74.75；inbound:GameSetUserArea 166 ms／12／max 32.87；event:achievement.inventory 100 ms／210／max 7.67；inbound:GameConnectionCheck 92 ms／13115／max 3.83 |
| 稳态窗 | 120.0 | 52201 | 3969 | 3.3% | inbound:GameEplpCommand 1299 ms／34／max 59.31；inbound:GameEnterSelectDungeon 844 ms／36／max 37.87；inbound:GameSetUserPosition 808 ms／20343／max 4.02；inbound:GameSelectDungeon 533 ms／36／max 20.73；inbound:GameConnectionCheck 199 ms／27430／max 3.36；inbound:GameRequestPeer 87 ms／36／max 6.17；timer:JoustMaintenanceService 39 ms／120／max 3.68；inbound:GameLeaveParty 27 ms／36／max 1.77 |
| (d) 10 并发登录的轮内 | 1.2 | 842 | 51 | 4.4% | inbound:GameEplpCommand 20 ms／1／max 20.38；http:POST /api/launcher/v1/register 11 ms／60／max 0.85；inbound:GameLogin 9 ms／60／max 0.41；http:POST /api/launcher/v1/login 4 ms／60／max 0.29；inbound:GameSetUserPosition 4 ms／178／max 1.71；lifecycle:connected 1 ms／60／max 0.04；inbound:GameConnectionCheck 1 ms／339／max 0.03；lifecycle:disconnected 1 ms／54／max 0.09 |
| (e) 灌包期间 | 30.0 | 2577240 | 6178 | 20.6% | inbound:GameConnectionCheck 5726 ms／2570805／max 0.94；inbound:GameEplpCommand 178 ms／8／max 29.21；inbound:GameSetUserPosition 105 ms／5344／max 0.48；inbound:GameEnterSelectDungeon 75 ms／7／max 14.60；inbound:GameSelectDungeon 66 ms／7／max 10.50；inbound:GameRequestPeer 6 ms／10／max 0.85；timer:JoustMaintenanceService 3 ms／30／max 0.13；inbound:GameLeaveParty 3 ms／11／max 0.29 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 32.8，`event:` 1.7。

