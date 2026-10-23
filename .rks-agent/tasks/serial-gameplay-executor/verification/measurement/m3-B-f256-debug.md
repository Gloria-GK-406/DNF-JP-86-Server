# 基线测量 — m3bfd（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3717 ms

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
| CHECK_USER_CONNECTION | 26844 | 55.92% |
| SET_USER_POSITION | 20289 | 42.27% |
| STEP_FENCE | 578 | 1.20% |
| FINISH_LOADING | 72 | 0.15% |
| EPLP | 37 | 0.08% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26844 | 0.31 | 1.83 | 16.83 | 46.51 | 65.68 | 0 | 15.37 | 46.50 | 65.68 |
| SET_USER_POSITION | 20289 | 0.41 | 1.95 | 16.40 | 47.63 | 67.48 | 0 | 15.69 | 47.53 | 65.66 |
| FINISH_LOADING | 72** | 0.19 | 0.29 | 10.96 | 10.96 | 10.96 | 0 | 10.96 | 10.96 | 10.96 |
| EPLP | 37** | 45.93 | 63.88 | 70.74 | 70.74 | 70.74 | 0 | 70.74 | 70.74 | 70.74 |
| LEAVE_PARTY | 36** | 0.75 | 1.24 | 1.27 | 1.27 | 1.27 | 0 | 1.27 | 1.27 | 1.27 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.49 | 0.74 | 0.96 | 0.96 | 0.96 | 0 | 0.96 | 0.96 | 0.96 |
| ENTER_SELECT_DUNGEON | 36** | 30.32 | 40.14 | 41.28 | 41.28 | 41.28 | 0 | 41.28 | 41.28 | 41.28 |
| SELECT_DUNGEON | 36** | 15.23 | 18.79 | 19.47 | 19.47 | 19.47 | 0 | 19.47 | 19.47 | 19.47 |
| **全部计入的请求** | 47386 | 0.35 | 1.92 | 19.47 | 48.43 | 70.74 | 36 | 18.53 | 48.21 | 70.74 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47422 | 0.00 | 1.29 | 1.95 | 6.76 | 38.45（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.85 | 10.55 | 103.5 | 103.5 | 103.5 | 0.4 | 0 |
| EnterDungeonCommand | Transactional | 80** | 8.54 | 12.09 | 58.40 | 58.40 | 58.40 | 13.4 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 14.74 | 33.59 | 56.83 | 56.83 | 56.83 | 3.2 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 15.27 | 49.79 | 50.57 | 50.57 | 50.57 | 0.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 15.75 | 22.43 | 28.15 | 28.15 | 28.15 | 0.5 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 15.42 | 23.31 | 24.24 | 24.24 | 24.24 | 1.0 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.21 | 2.07 | 24.14 | 24.14 | 24.14 | 0.3 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.28 | 5.58 | 20.94 | 20.94 | 20.94 | 0.9 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.52 | 2.81 | 13.60 | 13.60 | 13.60 | 18.5 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 1.75 | 2.85 | 7.72 | 7.72 | 7.72 | 8.7 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.03 | 0.20 | 5.69 | 5.69 | 5.69 | 8.7 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.14 | 0.46 | 4.57 | 4.57 | 4.57 | 0.2 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.07 | 0.13 | 4.40 | 4.40 | 4.40 | 0.5 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337* | 0.10 | 0.36 | 0.47 | 3.77 | 3.77 | 95.0 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.03 | 0.24 | 3.77 | 3.77 | 3.77 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.07 | 3.48 | 3.48 | 3.48 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.19 | 3.37 | 3.37 | 3.37 | 0.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.06 | 0.08 | 2.78 | 2.78 | 2.78 | 0.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.04 | 0.13 | 2.47 | 2.47 | 2.47 | 0.2 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.10 | 0.96 | 2.34 | 2.34 | 27.1 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.06 | 1.57 | 1.67 | 1.67 | 1.67 | 3.8 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.38 | 1.62 | 1.62 | 1.62 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 1.61 | 1.61 | 1.61 | 0.2 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.02 | 0.06 | 1.53 | 1.53 | 0.2 | 0 |
| LeavePartyCommand | Direct | 78** | 0.03 | 0.08 | 1.42 | 1.42 | 1.42 | 23.6 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.05 | 1.25 | 1.25 | 1.25 | 1.25 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.05 | 0.71 | 0.71 | 0.71 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.02 | 0.27 | 0.61 | 0.61 | 13.4 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.13 | 0.53 | 0.53 | 0.53 | 60.0 | 0 |

命令日志行 1773 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**236.1 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo01；参考口径 236.1 ms）。冷启动窗整体：22521 | 0.34 | 2.35 | 38.72 | 88.22 | 236.1（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 24 | 236.1 | 236.1 | SELECT_CHARACTER |
| 1 | 111 | 84.58 | 84.86 | SELECT_CHARACTER |
| 2 | 167 | 103.6 | 117.4 | SELECT_CHARACTER |
| 3 | 183 | 114.1 | 114.2 | SELECT_CHARACTER |
| 4 | 324 | 94.02 | 99.23 | SELECT_CHARACTER |
| 5 | 400 | 2.02 | 2.73 | CHECK_USER_CONNECTION |
| 6 | 400 | 2.16 | 2.83 | CHECK_USER_CONNECTION |
| 7 | 400 | 2.36 | 4.10 | CHECK_USER_CONNECTION |
| 8 | 390 | 15.99 | 19.54 | CHECK_USER_CONNECTION |
| 9 | 394 | 2.17 | 4.75 | RESPONSE_PEER |
| 10 | 386 | 3.99 | 5.37 | SET_USER_POSITION |
| 11 | 400 | 2.52 | 3.08 | CHECK_USER_CONNECTION |
| 12 | 400 | 2.46 | 2.58 | CHECK_USER_CONNECTION |
| 13 | 387 | 62.46 | 77.10 | SELECT_DUNGEON |
| 14 | 389 | 2.35 | 3.01 | SET_USER_POSITION |
| 15 | 382 | 22.67 | 29.39 | ENTER_SELECT_DUNGEON |
| 16 | 394 | 2.01 | 2.46 | SET_USER_POSITION |
| 17 | 400 | 2.20 | 2.42 | CHECK_USER_CONNECTION |
| 18 | 396 | 37.29 | 50.31 | EPLP |
| 19 | 398 | 1.97 | 2.52 | CHECK_USER_CONNECTION |
| 20 | 398 | 28.02 | 48.78 | EPLP |
| 21 | 396 | 50.98 | 53.06 | EPLP |
| 22 | 400 | 2.58 | 2.97 | CHECK_USER_CONNECTION |
| 23 | 398 | 8.40 | 13.44 | LEAVE_PARTY |
| 24 | 396 | 2.72 | 3.99 | SET_USER_POSITION |
| 25 | 396 | 2.67 | 3.70 | SET_USER_POSITION |
| 26 | 398 | 2.13 | 3.11 | CHECK_USER_CONNECTION |
| 27 | 400 | 2.18 | 7.51 | SET_USER_POSITION |
| 28 | 396 | 2.68 | 3.13 | SET_USER_POSITION |
| 29 | 391 | 2.39 | 6.09 | SET_USER_POSITION |
| 30 | 393 | 3.02 | 6.08 | SET_USER_POSITION |
| 31 | 390 | 2.52 | 2.96 | SET_USER_POSITION |
| 32 | 400 | 2.07 | 3.01 | SET_USER_POSITION |
| 33 | 400 | 2.91 | 3.10 | CHECK_USER_CONNECTION |
| 34 | 376 | 34.07 | 39.91 | ENTER_SELECT_DUNGEON |
| 35 | 396 | 23.16 | 25.61 | ENTER_SELECT_DUNGEON |
| 36 | 380 | 17.67 | 20.74 | ENTER_SELECT_DUNGEON |
| 37 | 400 | 2.41 | 2.78 | CHECK_USER_CONNECTION |
| 38 | 394 | 20.89 | 33.48 | EPLP |
| 39 | 400 | 2.35 | 3.27 | SET_USER_POSITION |
| 40 | 398 | 30.07 | 50.89 | EPLP |
| 41 | 396 | 47.87 | 53.86 | EPLP |
| 42 | 398 | 2.94 | 3.23 | CHECK_USER_CONNECTION |
| 43 | 396 | 2.26 | 2.97 | CHECK_USER_CONNECTION |
| 44 | 400 | 2.30 | 3.70 | CHECK_USER_CONNECTION |
| 45 | 398 | 7.42 | 12.20 | CHECK_USER_CONNECTION |
| 46 | 396 | 2.53 | 4.29 | SET_USER_POSITION |
| 47 | 389 | 2.31 | 3.09 | CHECK_USER_CONNECTION |
| 48 | 396 | 2.32 | 3.07 | CHECK_USER_CONNECTION |
| 49 | 400 | 4.76 | 5.40 | CHECK_USER_CONNECTION |
| 50 | 400 | 4.12 | 6.17 | CHECK_USER_CONNECTION |
| 51 | 390 | 2.68 | 4.18 | CHECK_USER_CONNECTION |
| 52 | 387 | 33.39 | 39.18 | ENTER_SELECT_DUNGEON |
| 53 | 384 | 31.26 | 35.64 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 2.05 | 3.05 | SET_USER_POSITION |
| 55 | 392 | 20.04 | 28.31 | ENTER_SELECT_DUNGEON |
| 56 | 398 | 15.47 | 21.33 | EPLP |
| 57 | 382 | 27.75 | 32.00 | ENTER_SELECT_DUNGEON |
| 58 | 400 | 2.16 | 2.63 | CHECK_USER_CONNECTION |
| 59 | 398 | 29.19 | 34.27 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47386 | 0.35 | 1.92 | 19.47 | 48.43 | 70.74 |
| 10 并发登录的轮内 | 481* | 28.67 | 82.20 | 89.38 | 91.74 | 91.74 |
| （参考：实际发出口径）稳态窗 | 47386 | 0.22 | 0.58 | 18.53 | 48.21 | 70.74 |
| （参考：实际发出口径）轮内 | 481* | 12.96 | 81.13 | 89.38 | 89.89 | 89.89 |
| 登录阶段整体（182–232 s） | 19836 | 0.34 | 2.37 | 47.46 | 85.70 | 91.74 |

每轮时长（开始 → 10 个 LOGIN 都应答）：200, 233, 190, 187, 199, 205 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.41 | 2.16 | 15.91 | 44.54 | 67.48 | 14.50 |
| 单人 | 轮内 | 188* | 29.12 | 81.25 | 86.80 | 91.74 | 91.74 | 85.64 |
| 双人组领队 | 稳态窗 | 14075 | 0.34 | 1.34 | 26.98 | 52.63 | 70.74 | 26.50 |
| 双人组领队 | 轮内 | 143* | 28.55 | 85.25 | 89.38 | 89.89 | 89.89 | 89.38 |
| 双人组队员 | 稳态窗 | 14111 | 0.34 | 1.28 | 17.70 | 47.53 | 65.68 | 17.02 |
| 双人组队员 | 轮内 | 150* | 28.45 | 85.12 | 89.45 | 89.82 | 89.82 | 89.45 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 93.80 | 127.4 | 129.2 | 129.2 | 129.2 |
| 启动器登录（HTTP，含校验） | 60** | 89.46 | 104.8 | 107.9 | 107.9 | 107.9 |
| Game LOGIN 0x0001 | 60** | 0.23 | 0.76 | 11.45 | 11.45 | 11.45 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 256 持续发 CHECK_USER_CONNECTION；实际应答速率 62088 包/秒（1863234 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47386 | 0.35 | 1.92 | 19.47 | 48.43 | 70.74 |
| 其余连接，灌包期间 | 11867 | 0.39 | 4.67 | 7.63 | 30.83 | 41.40 |
| （参考：实际发出口径）其余连接，灌包期间 | 11867 | 0.10 | 4.33 | 6.94 | 30.46 | 41.40 |
| 灌包连接自身（含其窗口内排队） | 1863489 | 3.14 | 8.63 | 10.14 | 33.30 | 53.33 |

灌包窗口 256 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 请求者 | 78 | 3791 | leave dungeon | 204.9 |
| EPLP | 同伴 | 78 | 3791 | leave dungeon | 204.9 |
| SET_USER_AREA | 同伴 | 6 | 3093 | town | 4.9 |
| SET_USER_AREA | 请求者 | 12 | 2890 | town | 4.9 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.3 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.3 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 783 | party dungeon entry | 192.1 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 783 | party dungeon entry | 192.1 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 83 | 334 | party | 232.1 |
| LEAVE_PARTY | 同伴 | 78 | 307 | leave party | 23.6 |
| RESPONSE_PEER | 请求者 | 83 | 260 | party | 145.9 |
| REQUEST_PEER | 同伴 | 83 | 220 | party | 232.0 |
| LEAVE_PARTY | 请求者 | 78 | 196 | leave party | 23.6 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.3 |
| REQUEST_PEER | 请求者 | 83 | 96 | party | 65.5 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 13.4 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 13.4 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 19.47 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 89.38 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 7.63 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 236.1 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 103.5 ms，max 103.5 ms（出现于 t=0.4 s；冷启动窗内 max 103.5 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 83 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1982284 行（Host 就绪之后 1982279 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 72.93 | 116.9 | 234.8 | 234.8 | 234.8 | 234.8 | 234.8 | – | 0.5 | 0 |
| inbound:GameEplpCommand | 78** | 41.10 | 61.45 | 88.77 | 88.77 | 88.77 | 40.07 | 53.51 | 88.77 | 204.9 | 0 |
| inbound:GameSelectDungeon | 80** | 14.27 | 18.92 | 76.56 | 76.56 | 76.56 | 76.56 | 76.56 | 19.26 | 13.4 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 24.00 | 39.61 | 60.67 | 60.67 | 60.67 | 60.67 | 60.67 | 45.79 | 13.3 | 0 |
| inbound:GameSetUserArea | 12** | 14.41 | 36.37 | 36.37 | 36.37 | 36.37 | 10.78 | 36.37 | – | 4.5 | 0 |
| inbound:GameCreateCharacter | 21** | 1.54 | 25.08 | 31.68 | 31.68 | 31.68 | 31.68 | 31.68 | – | 0.3 | 0 |
| inbound:GameRequestPeer | 83** | 2.37 | 3.43 | 20.17 | 20.17 | 20.17 | 20.17 | 20.17 | 5.94 | 8.7 | 0 |
| inbound:GameConnectionCheck | 1925401 | 0.00 | 0.00 | 0.02 | 0.03 | 18.44 | 0.14 | 7.49 | 18.44 | 273.5 | 0 |
| inbound:GameResponsePeer | 83** | 0.37 | 0.83 | 16.90 | 16.90 | 16.90 | 16.90 | 16.90 | 0.63 | 8.7 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.48 | 15.56 | 15.56 | 15.56 | 1.82 | 1.82 | 15.56 | 274.0 | 0 |
| inbound:GameLeaveParty | 78** | 0.53 | 1.13 | 12.70 | 12.70 | 12.70 | 12.70 | 12.70 | 1.08 | 23.6 | 0 |
| event:achievement.inventory | 210* | 0.33 | 0.89 | 3.19 | 11.42 | 11.42 | 11.42 | 11.42 | – | 0.3 | 0 |
| inbound:GameLogin | 81** | 0.13 | 0.31 | 9.34 | 9.34 | 9.34 | 9.34 | 9.34 | 0.31 | 0.2 | 0 |
| timer:CreatureClockService | 277* | 0.03 | 0.91 | 4.45 | 7.73 | 7.73 | 7.73 | 7.73 | 4.57 | 1.0 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.15 | 0.60 | 7.12 | 7.12 | 7.12 | 7.12 | 7.12 | 1.87 | 0.2 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.77 | 5.32 | 7.00 | 7.00 | 7.00 | 5.32 | 7.00 | – | 0.8 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 1.65 | 6.80 | 6.80 | 6.80 | 6.80 | 6.80 | 6.80 | 3.08 | 27.1 | 0 |
| inbound:GameSetUserPosition | 46052 | 0.03 | 0.09 | 0.12 | 2.65 | 6.42 | 1.76 | 5.44 | 6.42 | 272.0 | 0 |
| timer:WeaknessRecoveryService | 4** | 1.19 | 6.14 | 6.14 | 6.14 | 6.14 | 2.05 | 2.05 | 6.14 | 180.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 2.18 | 6.05 | 6.05 | 6.05 | 6.05 | 6.05 | 6.05 | 2.48 | 60.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 4.03 | 4.03 | 4.03 | 4.03 | 4.03 | 0.03 | 0.1 | 0 |
| inbound:GameFinishLoading | 160* | 0.11 | 0.22 | 1.68 | 3.09 | 3.09 | 3.09 | 3.09 | 0.25 | 13.4 | 0 |
| timer:FatigueDailyRefreshService | 4** | 1.57 | 2.96 | 2.96 | 2.96 | 2.96 | 1.57 | 1.57 | 2.96 | 120.0 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.05 | 0.26 | 2.83 | 2.83 | 2.83 | 2.83 | 2.83 | 0.26 | 0.2 | 0 |
| event:quest.inventory | 210* | 0.11 | 0.38 | 0.80 | 2.79 | 2.79 | 0.58 | 2.79 | – | 4.5 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.01 | 2.26 | 2.26 | 2.26 | 2.26 | 2.26 | 2.26 | 0.02 | 60.0 | 0 |
| event:altar.departure | 160* | 0.14 | 0.29 | 0.65 | 2.11 | 2.11 | 2.11 | 2.11 | 0.65 | 18.5 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.13 | 0.21 | 0.25 | 1.94 | 1.94 | 1.94 | 1.94 | 0.45 | 1.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.04 | 1.89 | 1.89 | 1.89 | 1.89 | 1.89 | 1.89 | 0.06 | 60.0 | 0 |
| event:quest.mirror-leave | 160* | 0.14 | 0.33 | 1.44 | 1.50 | 1.50 | 0.87 | 0.87 | 1.50 | 157.5 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.06 | 1.43 | 1.43 | 1.43 | 1.43 | 1.43 | 1.43 | 0.40 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1108 | 0.01 | 0.04 | 0.06 | 0.41 | 1.36 | 1.36 | 1.36 | 0.10 | 0.2 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.02 | 1.29 | 1.29 | 1.29 | 1.29 | 1.29 | 1.29 | 0.02 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.05 | 0.93 | 0.93 | 0.93 | 0.93 | 0.67 | 0.67 | 0.93 | 120.0 | 0 |
| timer:IllusionTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 0.93 | 0.93 | 0.93 | 0.93 | 0.05 | 0.5 | 0 |
| timer:BossTowerReturnSweepService | 554* | 0.01 | 0.02 | 0.03 | 0.77 | 0.77 | 0.77 | 0.77 | 0.03 | 0.5 | 0 |
| timer:JoustMaintenanceService | 277* | 0.28 | 0.49 | 0.63 | 0.76 | 0.76 | 0.15 | 0.54 | 0.76 | 209.9 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.02 | 0.73 | 0.73 | 0.73 | 0.73 | 0.73 | 0.73 | 0.03 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.03 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.03 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.02 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.05 | 60.0 | 0 |
| event:tournament.departure | 160* | 0.13 | 0.20 | 0.57 | 0.61 | 0.61 | 0.19 | 0.26 | 0.61 | 256.1 | 0 |
| timer:TournamentRewardSweepService | 5538 | 0.01 | 0.01 | 0.02 | 0.04 | 0.51 | 0.51 | 0.51 | 0.05 | 0.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.03 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.04 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.01 | 0.03 | 0.04 | 0.04 | 0.04 | 0.04 | 0.03 | 13.4 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 19.47 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25417 | 0.01 | 0.62 | 37.40 | 91.51 | 235.7 | event:quest.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22918 | 0.01 | 0.26 | 30.41 | 84.38 | 114.0 |  |  |
| 稳态窗 | 全部作业 | 52103 | 0.01 | 0.18 | 19.81 | 47.11 | 65.40 | inbound:GameConnectionCheck | 104.9 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.16 | 14.75 | 46.15 | 65.40 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 917* | 0.10 | 19.92 | 53.83 | 62.61 | 62.61 | inbound:GameConnectionCheck | 182.0 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 644* | 0.13 | 24.46 | 53.83 | 62.61 | 62.61 |  |  |
| (e) 灌包期间 | 全部作业 | 1876498 | 0.00 | 0.00 | 0.01 | 0.05 | 38.94 | inbound:GameConnectionCheck | 265.8 |
| (e) 灌包期间 | 只算 inbound: | 1875502 | 0.00 | 0.00 | 0.01 | 0.05 | 38.94 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 108 | 235.2 | 235.7 | event:quest.inventory | 234.8 |
| 1 | 283 | 83.09 | 85.19 | event:quest.inventory | 84.80 |
| 2 | 327 | 97.14 | 117.5 | event:quest.inventory | 116.9 |
| 3 | 324 | 110.2 | 114.0 | inbound:GameSetUserPosition | 113.5 |
| 4 | 506 | 91.75 | 98.97 | event:achievement.inventory | 98.60 |
| 5 | 431 | 0.13 | 0.72 | inbound:GameSetUserPosition | 0.70 |
| 6 | 432 | 0.21 | 0.43 | inbound:GameConnectionCheck | 0.47 |
| 7 | 431 | 0.32 | 1.02 | inbound:GameSetUserPosition | 0.20 |
| 8 | 432 | 11.59 | 19.29 | inbound:GameConnectionCheck | 20.17 |
| 9 | 440 | 0.21 | 0.79 | inbound:GameConnectionCheck | 7.96 |
| 10 | 423 | 2.08 | 3.54 | inbound:GameSetUserPosition | 3.45 |
| 11 | 431 | 0.24 | 2.80 | inbound:GameConnectionCheck | 2.79 |
| 12 | 432 | 0.18 | 0.24 | inbound:GameConnectionCheck | 1.44 |
| 13 | 445 | 55.19 | 73.44 | inbound:GameConnectionCheck | 76.56 |
| 14 | 421 | 0.19 | 1.20 | inbound:GameSetUserPosition | 0.82 |
| 15 | 440 | 22.21 | 23.59 | inbound:GameConnectionCheck | 23.55 |
| 16 | 426 | 0.10 | 0.36 | timer:CreatureClockService | 1.76 |
| 17 | 431 | 0.13 | 0.23 | timer:CreatureClockService | 0.20 |
| 18 | 455 | 36.90 | 47.36 | event:altar.departure | 40.07 |
| 19 | 429 | 0.13 | 0.18 | timer:MarriageWeddingScheduler | 0.49 |
| 20 | 439 | 32.64 | 45.37 | inbound:GameConnectionCheck | 47.51 |
| 21 | 445 | 46.99 | 51.85 | inbound:GameConnectionCheck | 52.76 |
| 22 | 432 | 0.27 | 0.35 | inbound:GameConnectionCheck | 0.41 |
| 23 | 435 | 4.34 | 8.09 | inbound:GameConnectionCheck | 12.70 |
| 24 | 428 | 0.30 | 3.73 | inbound:GameSetUserPosition | 3.70 |
| 25 | 431 | 0.26 | 0.33 | timer:TournamentRewardSweepService | 0.26 |
| 26 | 432 | 0.22 | 0.24 | inbound:GameSetUserPosition | 2.57 |
| 27 | 432 | 0.22 | 2.03 | inbound:GameConnectionCheck | 6.80 |
| 28 | 433 | 0.29 | 0.33 | inbound:GameSetUserPosition | 2.79 |
| 29 | 433 | 0.69 | 5.63 | inbound:GameSetUserPosition | 5.61 |
| 30 | 435 | 0.90 | 3.66 | inbound:GameSetUserPosition | 3.58 |
| 31 | 426 | 0.37 | 1.41 | inbound:GameConnectionCheck | 2.56 |
| 32 | 432 | 0.25 | 0.34 | timer:IllusionTowerReturnSweepService | 0.42 |
| 33 | 431 | 0.38 | 2.75 | inbound:GameConnectionCheck | 2.62 |
| 34 | 435 | 29.20 | 33.77 | inbound:GameConnectionCheck | 39.61 |
| 35 | 436 | 17.85 | 25.38 | inbound:GameConnectionCheck | 25.36 |
| 36 | 430 | 10.94 | 16.77 | inbound:GameConnectionCheck | 20.48 |
| 37 | 431 | 0.19 | 0.34 | timer:CreatureClockService | 0.42 |
| 38 | 453 | 20.26 | 21.90 | event:altar.departure | 32.37 |
| 39 | 431 | 0.16 | 0.17 | inbound:GameSetUserPosition | 1.01 |
| 40 | 439 | 31.62 | 47.71 | inbound:GameSetUserPosition | 49.99 |
| 41 | 445 | 43.80 | 50.06 | inbound:GameSetUserPosition | 53.51 |
| 42 | 432 | 0.31 | 3.08 | inbound:GameConnectionCheck | 0.88 |
| 43 | 431 | 0.20 | 0.34 | inbound:GameConnectionCheck | 1.02 |
| 44 | 432 | 0.24 | 3.43 | inbound:GameConnectionCheck | 3.19 |
| 45 | 431 | 0.22 | 0.25 | timer:TournamentRewardSweepService | 2.85 |
| 46 | 432 | 0.25 | 0.33 | timer:BossTowerReturnSweepService | 0.98 |
| 47 | 435 | 0.29 | 2.81 | inbound:GameConnectionCheck | 2.62 |
| 48 | 428 | 0.24 | 0.37 | inbound:GameSetUserPosition | 0.42 |
| 49 | 431 | 0.30 | 3.46 | inbound:GameConnectionCheck | 3.50 |
| 50 | 432 | 0.33 | 3.90 | inbound:GameSetUserPosition | 3.68 |
| 51 | 431 | 0.29 | 1.50 | inbound:GameSetUserPosition | 3.65 |
| 52 | 433 | 32.47 | 37.29 | inbound:GameSetUserPosition | 38.23 |
| 53 | 433 | 27.25 | 32.23 | inbound:GameSetUserPosition | 34.53 |
| 54 | 432 | 0.19 | 0.39 | inbound:GameConnectionCheck | 0.42 |
| 55 | 432 | 13.30 | 25.64 | inbound:GameConnectionCheck | 27.94 |
| 56 | 439 | 14.55 | 17.81 | inbound:GameConnectionCheck | 21.14 |
| 57 | 440 | 22.45 | 28.12 | timer:TournamentRewardSweepService | 28.32 |
| 58 | 432 | 0.15 | 0.17 | inbound:GameConnectionCheck | 0.14 |
| 59 | 450 | 24.66 | 28.95 | inbound:GameSetUserPosition | 34.00 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25417 | 4422 | 7.4% | inbound:GameSelectCharacter 1816 ms／21／max 234.8；inbound:GameEplpCommand 546 ms／15／max 53.51；inbound:GameEnterSelectDungeon 423 ms／18／max 60.67；inbound:GameSetUserPosition 409 ms／9621／max 5.44；inbound:GameSelectDungeon 331 ms／18／max 76.56；inbound:GameSetUserArea 206 ms／12／max 36.37；inbound:GameConnectionCheck 157 ms／13087／max 7.49；event:achievement.inventory 102 ms／210／max 11.42 |
| 稳态窗 | 120.0 | 52103 | 4582 | 3.8% | inbound:GameEplpCommand 1577 ms／37／max 70.31；inbound:GameEnterSelectDungeon 940 ms／36／max 40.90；inbound:GameSetUserPosition 905 ms／20289／max 4.79；inbound:GameSelectDungeon 520 ms／36／max 19.26；inbound:GameConnectionCheck 342 ms／27482／max 4.43；inbound:GameRequestPeer 75 ms／36／max 5.94；timer:JoustMaintenanceService 38 ms／120／max 0.63；timer:CreatureClockService 34 ms／120／max 4.57 |
| (d) 10 并发登录的轮内 | 1.2 | 917 | 255 | 21.0% | inbound:GameEplpCommand 119 ms／2／max 64.96；inbound:GameEnterSelectDungeon 74 ms／3／max 45.79；inbound:GameSelectDungeon 31 ms／3／max 11.36；http:POST /api/launcher/v1/register 12 ms／60／max 1.87；inbound:GameLogin 7 ms／60／max 0.31；http:POST /api/launcher/v1/login 3 ms／60／max 0.26；inbound:GameSetUserPosition 2 ms／204／max 0.07；inbound:GameRequestPeer 1 ms／1／max 1.11 |
| (e) 灌包期间 | 30.0 | 1876498 | 8681 | 28.9% | inbound:GameConnectionCheck 8092 ms／1870111／max 6.80；inbound:GameEplpCommand 234 ms／8／max 38.85；inbound:GameSetUserPosition 136 ms／5323／max 6.42；inbound:GameEnterSelectDungeon 100 ms／7／max 24.14；inbound:GameSelectDungeon 89 ms／7／max 13.79；inbound:GameRequestPeer 7 ms／10／max 0.86；timer:JoustMaintenanceService 3 ms／30／max 0.18；timer:CreatureClockService 3 ms／30／max 0.31 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.8，`event:` 1.9。

