# 基线测量 — m2bg0（合成负载）

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
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/m1-base/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3777 ms

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
| CHECK_USER_CONNECTION | 26810 | 55.85% |
| SET_USER_POSITION | 20338 | 42.37% |
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
| CHECK_USER_CONNECTION | 26810 | 0.45 | 2.07 | 19.25 | 74.84 | 164.4 | 0 | 15.91 | 50.18 | 134.2 |
| SET_USER_POSITION | 20338 | 0.50 | 2.10 | 20.61 | 103.2 | 164.3 | 0 | 14.20 | 54.87 | 134.0 |
| FINISH_LOADING | 70** | 0.19 | 0.37 | 0.43 | 0.43 | 0.43 | 0 | 0.43 | 0.43 | 0.43 |
| EPLP | 36** | 36.99 | 64.16 | 76.45 | 76.45 | 76.45 | 0 | 76.34 | 76.34 | 76.34 |
| LEAVE_PARTY | 36** | 0.79 | 2.97 | 4.45 | 4.45 | 4.45 | 0 | 4.45 | 4.45 | 4.45 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.52 | 0.68 | 0.74 | 0.74 | 0.74 | 0 | 0.74 | 0.74 | 0.74 |
| ENTER_SELECT_DUNGEON | 35** | 26.39 | 45.54 | 45.93 | 45.93 | 45.93 | 0 | 44.64 | 44.64 | 44.64 |
| SELECT_DUNGEON | 35** | 15.50 | 23.00 | 23.38 | 23.38 | 23.38 | 0 | 23.38 | 23.38 | 23.38 |
| **全部计入的请求** | 47396 | 0.47 | 2.11 | 22.28 | 84.19 | 164.4 | 36 | 17.97 | 52.28 | 134.2 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47432 | 0.07 | 1.46 | 2.30 | 46.98 | 123.3（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.71 | 23.41 | 102.7 | 102.7 | 102.7 | 0.4 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 18.19 | 50.43 | 65.36 | 65.36 | 65.36 | 3.3 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.82 | 56.75 | 58.05 | 58.05 | 58.05 | 2.2 | 0 |
| EnterDungeonCommand | Transactional | 78** | 8.82 | 12.73 | 55.24 | 55.24 | 55.24 | 13.3 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 14.89 | 16.19 | 39.73 | 39.73 | 39.73 | 3.8 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 13.82 | 22.46 | 35.60 | 35.60 | 35.60 | 3.8 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.11 | 1.96 | 22.80 | 22.80 | 22.80 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.52 | 2.69 | 15.35 | 15.35 | 15.35 | 18.6 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.23 | 0.42 | 6.63 | 6.63 | 6.63 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 80** | 0.03 | 0.18 | 5.89 | 5.89 | 5.89 | 8.7 | 0 |
| RequestPartyInviteCommand | Direct | 80** | 1.45 | 2.75 | 4.81 | 4.81 | 4.81 | 29.9 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.15 | 0.39 | 4.28 | 4.28 | 4.28 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.09 | 3.73 | 3.73 | 3.73 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.21 | 3.68 | 3.68 | 3.68 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.14 | 3.51 | 3.51 | 3.51 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.23 | 3.28 | 3.28 | 3.28 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 333* | 0.07 | 0.29 | 0.42 | 3.15 | 3.15 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.09 | 2.89 | 2.89 | 2.89 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.06 | 0.15 | 2.31 | 2.31 | 2.31 | 0.2 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.08 | 2.24 | 2.24 | 2.24 | 165.6 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.07 | 2.03 | 2.03 | 2.03 | 2.03 | 60.0 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.45 | 1.60 | 1.60 | 1.60 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.58 | 1.58 | 1.58 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.08 | 1.54 | 1.54 | 1.54 | 0.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.12 | 0.31 | 1.53 | 1.53 | 19.8 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.20 | 1.41 | 1.41 | 0.2 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.05 | 0.12 | 1.03 | 1.03 | 1.03 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.16 | 0.70 | 0.70 | 0.70 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 156* | 0.01 | 0.03 | 0.28 | 0.60 | 0.60 | 13.3 | 0 |

命令日志行 1757 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**229.8 ms**（SELECT_CHARACTER，计划发出 t=0.29 s，solo04；参考口径 229.8 ms）。冷启动窗整体：22544 | 0.79 | 2.72 | 38.14 | 104.1 | 229.8（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 25 | 229.8 | 229.8 | SELECT_CHARACTER |
| 1 | 116 | 72.29 | 72.77 | SET_USER_POSITION |
| 2 | 172 | 113.7 | 119.8 | SELECT_CHARACTER |
| 3 | 183 | 132.7 | 135.8 | SELECT_CHARACTER |
| 4 | 334 | 104.8 | 105.4 | SET_USER_POSITION |
| 5 | 400 | 2.57 | 2.80 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.78 | 4.62 | SET_USER_POSITION |
| 7 | 400 | 2.74 | 2.90 | CHECK_USER_CONNECTION |
| 8 | 390 | 13.54 | 17.67 | RESPONSE_PEER |
| 9 | 389 | 2.81 | 5.53 | CHECK_USER_CONNECTION |
| 10 | 391 | 5.20 | 5.78 | SET_USER_POSITION |
| 11 | 400 | 2.82 | 2.92 | SET_USER_POSITION |
| 12 | 400 | 2.93 | 3.84 | CHECK_USER_CONNECTION |
| 13 | 387 | 58.11 | 74.67 | SELECT_DUNGEON |
| 14 | 389 | 2.40 | 2.46 | CHECK_USER_CONNECTION |
| 15 | 380 | 19.11 | 26.12 | ENTER_SELECT_DUNGEON |
| 16 | 396 | 3.69 | 3.70 | CHECK_USER_CONNECTION |
| 17 | 400 | 2.68 | 2.72 | SET_USER_POSITION |
| 18 | 395 | 30.41 | 46.66 | EPLP |
| 19 | 399 | 2.79 | 3.58 | CHECK_USER_CONNECTION |
| 20 | 398 | 33.48 | 44.42 | EPLP |
| 21 | 396 | 45.63 | 53.13 | CHECK_USER_CONNECTION |
| 22 | 400 | 2.89 | 4.48 | SET_USER_POSITION |
| 23 | 397 | 4.82 | 11.33 | LEAVE_PARTY |
| 24 | 397 | 5.98 | 6.70 | SET_USER_POSITION |
| 25 | 396 | 2.90 | 5.45 | SET_USER_POSITION |
| 26 | 398 | 2.81 | 4.18 | SET_USER_POSITION |
| 27 | 395 | 2.83 | 4.21 | SET_USER_POSITION |
| 28 | 395 | 3.05 | 6.40 | CHECK_USER_CONNECTION |
| 29 | 397 | 2.74 | 2.94 | CHECK_USER_CONNECTION |
| 30 | 392 | 2.88 | 4.76 | SET_USER_POSITION |
| 31 | 391 | 2.69 | 2.88 | CHECK_USER_CONNECTION |
| 32 | 401 | 27.90 | 37.89 | ENTER_SELECT_DUNGEON |
| 33 | 383 | 15.27 | 36.93 | ENTER_SELECT_DUNGEON |
| 34 | 392 | 18.98 | 30.84 | ENTER_SELECT_DUNGEON |
| 35 | 392 | 21.63 | 27.53 | SET_USER_POSITION |
| 36 | 384 | 15.29 | 17.18 | ENTER_SELECT_DUNGEON |
| 37 | 400 | 2.47 | 2.87 | SET_USER_POSITION |
| 38 | 394 | 24.46 | 32.88 | EPLP |
| 39 | 400 | 2.65 | 3.58 | CHECK_USER_CONNECTION |
| 40 | 398 | 42.38 | 46.90 | CHECK_USER_CONNECTION |
| 41 | 396 | 47.49 | 55.23 | EPLP |
| 42 | 396 | 2.78 | 6.39 | SET_USER_POSITION |
| 43 | 400 | 2.91 | 3.81 | SET_USER_POSITION |
| 44 | 398 | 2.88 | 3.04 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.79 | 2.96 | SET_USER_POSITION |
| 46 | 396 | 3.17 | 3.59 | CHECK_USER_CONNECTION |
| 47 | 390 | 4.71 | 5.31 | SET_USER_POSITION |
| 48 | 400 | 2.80 | 2.91 | CHECK_USER_CONNECTION |
| 49 | 395 | 2.88 | 4.07 | SET_USER_POSITION |
| 50 | 400 | 4.71 | 5.35 | SET_USER_POSITION |
| 51 | 385 | 4.19 | 4.75 | CHECK_USER_CONNECTION |
| 52 | 400 | 2.77 | 5.32 | CHECK_USER_CONNECTION |
| 53 | 384 | 19.44 | 33.17 | ENTER_SELECT_DUNGEON |
| 54 | 398 | 14.31 | 20.90 | ENTER_SELECT_DUNGEON |
| 55 | 386 | 15.96 | 21.97 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.55 | 2.72 | SET_USER_POSITION |
| 57 | 382 | 23.74 | 24.42 | SET_USER_POSITION |
| 58 | 400 | 2.51 | 2.74 | CHECK_USER_CONNECTION |
| 59 | 398 | 24.87 | 29.90 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47396 | 0.47 | 2.11 | 22.28 | 84.19 | 164.4 |
| 10 并发登录的轮内 | 509* | 7.13 | 83.27 | 99.71 | 101.9 | 101.9 |
| （参考：实际发出口径）稳态窗 | 47396 | 0.25 | 0.75 | 17.97 | 52.28 | 134.2 |
| （参考：实际发出口径）轮内 | 509* | 2.39 | 79.60 | 90.09 | 90.29 | 90.29 |
| 登录阶段整体（182–232 s） | 19852 | 0.45 | 1.92 | 35.74 | 84.59 | 101.9 |

每轮时长（开始 → 10 个 LOGIN 都应答）：219, 203, 231, 210, 192, 200 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.41 | 2.04 | 19.85 | 83.38 | 164.3 | 15.70 |
| 单人 | 轮内 | 208* | 6.62 | 83.31 | 100.1 | 101.9 | 101.9 | 90.04 |
| 双人组领队 | 稳态窗 | 14080 | 0.52 | 2.22 | 26.49 | 86.73 | 164.1 | 23.00 |
| 双人组领队 | 轮内 | 151* | 7.40 | 84.16 | 99.71 | 101.7 | 101.7 | 90.24 |
| 双人组队员 | 稳态窗 | 14116 | 0.51 | 2.12 | 19.95 | 86.59 | 164.4 | 15.43 |
| 双人组队员 | 轮内 | 150* | 7.36 | 78.31 | 90.57 | 101.6 | 101.6 | 90.23 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 105.1 | 118.5 | 145.4 | 145.4 | 145.4 |
| 启动器登录（HTTP，含校验） | 60** | 92.14 | 101.1 | 107.9 | 107.9 | 107.9 |
| Game LOGIN 0x0001 | 60** | 0.25 | 0.61 | 0.84 | 0.84 | 0.84 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 77055 包/秒（2311669 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47396 | 0.47 | 2.11 | 22.28 | 84.19 | 164.4 |
| 其余连接，灌包期间 | 11869 | 0.94 | 6.22 | 17.84 | 37.63 | 45.72 |
| （参考：实际发出口径）其余连接，灌包期间 | 11869 | 0.11 | 5.55 | 16.49 | 35.30 | 42.65 |
| 灌包连接自身（含其窗口内排队） | 2311700 | 0.28 | 0.33 | 5.42 | 8.12 | 50.70 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 4669 | leave dungeon | 82.2 |
| EPLP | 请求者 | 78 | 4573 | leave dungeon | 82.2 |
| SET_USER_AREA | 同伴 | 6 | 3021 | town | 4.9 |
| SET_USER_AREA | 请求者 | 12 | 2890 | town | 4.9 |
| SELECT_DUNGEON | 请求者 | 78 | 1367 | party dungeon entry | 13.3 |
| SELECT_DUNGEON | 同伴 | 78 | 1367 | party dungeon entry | 13.3 |
| ENTER_SELECT_DUNGEON | 请求者 | 78 | 656 | party dungeon entry | 256.3 |
| ENTER_SELECT_DUNGEON | 同伴 | 78 | 656 | party dungeon entry | 256.3 |
| RESPONSE_PEER | 同伴 | 80 | 390 | party | 8.7 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 请求者 | 80 | 308 | party | 8.7 |
| LEAVE_PARTY | 同伴 | 78 | 211 | leave party | 25.7 |
| REQUEST_PEER | 同伴 | 80 | 204 | party | 8.7 |
| REQUEST_PEER | 请求者 | 80 | 176 | party | 8.7 |
| LEAVE_PARTY | 请求者 | 78 | 148 | leave party | 144.9 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.2 |
| FINISH_LOADING | 请求者 | 156 | 36 | party dungeon entry | 13.3 |
| FINISH_LOADING | 同伴 | 156 | 0 | party dungeon entry | 13.3 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 22.28 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 99.71 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 17.84 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 229.8 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 102.7 ms，max 102.7 ms（出现于 t=0.4 s；冷启动窗内 max 102.7 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 78 次；服务端关闭连接或超时的请求 1 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）1 个，协议无回包 80 个。

失败的命令 0 条：

