# 基线测量 — m3ad2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3795 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48003 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25757 | 53.66% |
| SET_USER_POSITION | 18242 | 38.00% |
| STEP_FENCE | 2146 | 4.47% |
| SET_USER_AREA | 1141 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |
| EPLP | 35 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25757 | 1.62 | 55.37 | 97.46 | 141.9 | 171.0 | 0 | 93.19 | 139.0 | 171.0 |
| SET_USER_POSITION | 18242 | 1.63 | 55.20 | 94.51 | 136.5 | 171.1 | 0 | 89.13 | 136.3 | 171.1 |
| SET_USER_AREA | 1141 | 27.96 | 81.83 | 116.8 | 139.6 | 170.9 | 0 | 113.9 | 139.0 | 170.9 |
| RETURN_SELECT_CHARACTER | 197* | 1.95 | 46.78 | 94.90 | 124.8 | 124.8 | 0 | 93.42 | 123.9 | 123.9 |
| SELECT_CHARACTER | 197* | 67.05 | 119.5 | 170.0 | 172.2 | 172.2 | 0 | 170.0 | 172.2 | 172.2 |
| FINISH_LOADING | 72** | 0.16 | 40.37 | 66.99 | 66.99 | 66.99 | 0 | 66.99 | 66.99 | 66.99 |
| LEAVE_PARTY | 37** | 0.37 | 37.28 | 88.70 | 88.70 | 88.70 | 0 | 88.70 | 88.70 | 88.70 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.27 | 0.60 | 0.64 | 0.64 | 0.64 | 0 | 0.64 | 0.64 | 0.64 |
| ENTER_SELECT_DUNGEON | 36** | 32.89 | 88.09 | 89.67 | 89.67 | 89.67 | 0 | 67.76 | 67.76 | 67.76 |
| SELECT_DUNGEON | 36** | 10.33 | 26.76 | 66.42 | 66.42 | 66.42 | 0 | 66.42 | 66.42 | 66.42 |
| EPLP | 35** | 48.68 | 159.4 | 181.0 | 181.0 | 181.0 | 0 | 160.5 | 160.5 | 160.5 |
| CREATE_CHARACTER | 35** | 3.44 | 20.81 | 63.10 | 63.10 | 63.10 | 0 | 63.10 | 63.10 | 63.10 |
| **全部计入的请求** | 45821 | 1.68 | 58.31 | 98.94 | 141.9 | 181.0 | 36 | 93.64 | 140.4 | 172.2 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45857 | 0.72 | 4.97 | 45.26 | 90.84 | 120.9（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.79 | 9.69 | 12.83 | 105.2 | 105.2 | 0.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 15.11 | 38.35 | 56.25 | 63.54 | 63.54 | 262.0 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.39 | 55.26 | 59.45 | 61.90 | 61.90 | 17.5 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 12.68 | 25.85 | 44.63 | 61.89 | 61.89 | 270.0 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.52 | 47.73 | 57.63 | 60.45 | 60.45 | 256.9 | 0 |
| EnterDungeonCommand | Transactional | 79** | 6.11 | 8.04 | 59.11 | 59.11 | 59.11 | 11.3 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.26 | 4.66 | 32.88 | 32.88 | 32.88 | 2.0 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.03 | 2.02 | 11.34 | 11.34 | 11.34 | 16.2 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.10 | 0.30 | 6.58 | 6.58 | 6.58 | 0.2 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.18 | 0.77 | 1.17 | 6.22 | 6.22 | 2.0 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.37 | 1.03 | 5.74 | 5.74 | 5.74 | 0.4 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.07 | 0.15 | 0.31 | 5.68 | 5.68 | 256.9 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.01 | 0.15 | 3.92 | 3.92 | 3.92 | 6.8 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.04 | 0.07 | 0.10 | 3.88 | 3.88 | 0.7 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.10 | 3.62 | 3.62 | 0.6 | 0 |
| AdvanceCreatureClockCommand | Transactional | 769* | 0.03 | 0.12 | 0.24 | 3.34 | 3.34 | 1.3 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.04 | 0.08 | 0.15 | 3.26 | 3.26 | 0.4 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.08 | 2.90 | 2.90 | 0.7 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.16 | 2.83 | 2.83 | 2.83 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 0.39 | 0.90 | 2.81 | 2.81 | 2.81 | 6.8 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.19 | 0.57 | 1.15 | 2.78 | 2.78 | 198.3 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.04 | 0.06 | 0.09 | 1.82 | 1.82 | 0.7 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.12 | 0.22 | 1.79 | 1.79 | 55.1 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.04 | 1.69 | 1.69 | 1.69 | 0.3 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.02 | 0.02 | 1.57 | 1.57 | 0.3 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 1.05 | 1.05 | 1.05 | 1.05 | 60.0 | 0 |
| LeavePartyCommand | Direct | 78** | 0.01 | 0.03 | 0.96 | 0.96 | 0.96 | 22.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.05 | 0.08 | 0.68 | 0.68 | 0.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.02 | 0.28 | 0.62 | 0.62 | 11.3 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.03 | 0.08 | 0.49 | 0.49 | 30.9 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.03 | 0.41 | 0.41 | 0.41 | 60.0 | 0 |

命令日志行 9778 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**888.4 ms**（CHECK_USER_CONNECTION，计划发出 t=2.28 s，solo05；参考口径 60.59 ms）。冷启动窗整体：21928 | 1.73 | 64.03 | 218.1 | 829.2 | 888.4（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 34 | 872.1 | 872.1 | SELECT_CHARACTER |
| 1 | 80 | 625.2 | 625.2 | CHECK_USER_CONNECTION |
| 2 | 168 | 888.0 | 888.4 | CHECK_USER_CONNECTION |
| 3 | 360 | 287.5 | 312.2 | CHECK_USER_CONNECTION |
| 4 | 390 | 62.60 | 63.26 | CHECK_USER_CONNECTION |
| 5 | 383 | 90.66 | 107.5 | SELECT_CHARACTER |
| 6 | 384 | 113.2 | 113.6 | CHECK_USER_CONNECTION |
| 7 | 373 | 59.87 | 63.85 | SELECT_CHARACTER |
| 8 | 368 | 107.9 | 109.1 | SELECT_CHARACTER |
| 9 | 383 | 61.11 | 73.05 | SELECT_CHARACTER |
| 10 | 381 | 117.8 | 122.5 | SET_USER_AREA |
| 11 | 383 | 105.5 | 115.1 | SELECT_CHARACTER |
| 12 | 373 | 78.23 | 92.35 | CHECK_USER_CONNECTION |
| 13 | 365 | 120.2 | 121.0 | CHECK_USER_CONNECTION |
| 14 | 383 | 92.80 | 103.8 | SELECT_CHARACTER |
| 15 | 384 | 56.73 | 65.80 | SELECT_CHARACTER |
| 16 | 385 | 61.34 | 79.66 | SELECT_CHARACTER |
| 17 | 385 | 119.1 | 127.9 | SELECT_CHARACTER |
| 18 | 379 | 73.24 | 86.34 | SELECT_CHARACTER |
| 19 | 387 | 115.8 | 123.5 | SELECT_CHARACTER |
| 20 | 388 | 88.19 | 110.4 | SELECT_CHARACTER |
| 21 | 386 | 58.90 | 71.00 | SELECT_CHARACTER |
| 22 | 384 | 108.0 | 108.4 | SET_USER_AREA |
| 23 | 381 | 92.99 | 114.7 | SELECT_CHARACTER |
| 24 | 385 | 76.58 | 96.23 | SELECT_CHARACTER |
| 25 | 385 | 54.24 | 70.65 | SELECT_CHARACTER |
| 26 | 378 | 54.34 | 58.94 | CHECK_USER_CONNECTION |
| 27 | 378 | 62.91 | 68.67 | SET_USER_POSITION |
| 28 | 377 | 85.35 | 93.09 | SELECT_CHARACTER |
| 29 | 389 | 67.29 | 68.31 | SELECT_CHARACTER |
| 30 | 384 | 47.92 | 48.47 | SET_USER_POSITION |
| 31 | 377 | 58.84 | 77.11 | SELECT_CHARACTER |
| 32 | 360 | 99.25 | 110.4 | SELECT_CHARACTER |
| 33 | 377 | 55.05 | 65.58 | SELECT_CHARACTER |
| 34 | 375 | 104.0 | 114.0 | SELECT_CHARACTER |
| 35 | 383 | 71.89 | 74.96 | SELECT_CHARACTER |
| 36 | 375 | 46.53 | 56.01 | SELECT_CHARACTER |
| 37 | 387 | 51.53 | 61.45 | SELECT_CHARACTER |
| 38 | 384 | 52.84 | 70.83 | SELECT_CHARACTER |
| 39 | 380 | 160.5 | 167.3 | SELECT_CHARACTER |
| 40 | 378 | 90.32 | 103.8 | SELECT_CHARACTER |
| 41 | 377 | 84.34 | 87.40 | CHECK_USER_CONNECTION |
| 42 | 387 | 55.35 | 59.41 | SELECT_CHARACTER |
| 43 | 381 | 106.1 | 119.0 | SET_USER_AREA |
| 44 | 377 | 70.53 | 76.79 | SET_USER_AREA |
| 45 | 383 | 50.38 | 63.84 | SELECT_CHARACTER |
| 46 | 362 | 88.90 | 99.94 | SELECT_CHARACTER |
| 47 | 379 | 77.54 | 100.5 | SET_USER_POSITION |
| 48 | 384 | 94.88 | 106.6 | SELECT_CHARACTER |
| 49 | 386 | 62.31 | 83.69 | SELECT_CHARACTER |
| 50 | 385 | 89.29 | 110.4 | SELECT_CHARACTER |
| 51 | 373 | 60.69 | 70.78 | SELECT_CHARACTER |
| 52 | 356 | 82.78 | 83.97 | CHECK_USER_CONNECTION |
| 53 | 383 | 35.32 | 70.46 | ENTER_SELECT_DUNGEON |
| 54 | 387 | 74.20 | 84.12 | SELECT_CHARACTER |
| 55 | 377 | 102.7 | 113.9 | SET_USER_POSITION |
| 56 | 379 | 59.45 | 62.26 | SELECT_CHARACTER |
| 57 | 385 | 94.22 | 115.5 | SELECT_CHARACTER |
| 58 | 384 | 60.42 | 79.56 | SELECT_CHARACTER |
| 59 | 384 | 78.80 | 79.01 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45821 | 1.68 | 58.31 | 98.94 | 141.9 | 181.0 |
| 10 并发登录的轮内 | 606* | 23.66 | 114.1 | 151.0 | 158.5 | 158.5 |
| （参考：实际发出口径）稳态窗 | 45821 | 0.34 | 51.99 | 93.64 | 140.4 | 172.2 |
| （参考：实际发出口径）轮内 | 606* | 6.62 | 81.66 | 106.8 | 132.2 | 132.2 |
| 登录阶段整体（182–232 s） | 19201 | 2.00 | 67.93 | 108.4 | 154.4 | 180.3 |

每轮时长（开始 → 10 个 LOGIN 都应答）：213, 211, 297, 312, 309, 232 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17633 | 1.41 | 59.66 | 101.2 | 139.6 | 172.2 | 95.51 |
| 单人 | 轮内 | 233* | 23.49 | 129.8 | 146.4 | 154.0 | 154.0 | 106.7 |
| 双人组领队 | 稳态窗 | 14076 | 2.00 | 58.10 | 97.73 | 146.2 | 181.0 | 92.82 |
| 双人组领队 | 轮内 | 185* | 24.37 | 113.2 | 154.5 | 158.5 | 158.5 | 106.7 |
| 双人组队员 | 稳态窗 | 14112 | 1.66 | 57.28 | 97.09 | 141.8 | 171.1 | 91.89 |
| 双人组队员 | 轮内 | 188* | 23.62 | 113.7 | 154.5 | 154.8 | 154.8 | 132.1 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 101.9 | 190.0 | 234.9 | 234.9 | 234.9 |
| 启动器登录（HTTP，含校验） | 60** | 109.3 | 133.1 | 173.0 | 173.0 | 173.0 |
| Game LOGIN 0x0001 | 60** | 0.35 | 57.43 | 61.74 | 61.74 | 61.74 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 44217 包/秒（1326666 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45821 | 1.68 | 58.31 | 98.94 | 141.9 | 181.0 |
| 其余连接，灌包期间 | 11479 | 1.73 | 75.10 | 122.8 | 174.7 | 190.9 |
| （参考：实际发出口径）其余连接，灌包期间 | 11479 | 0.29 | 60.94 | 114.2 | 168.7 | 190.1 |
| 灌包连接自身（含其窗口内排队） | 1326697 | 0.36 | 0.58 | 2.16 | 78.04 | 440.7 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.7 |
| SET_USER_AREA | 请求者 | 2656 | 6626 | town | 141.5 |
| EPLP | 同伴 | 78 | 5841 | leave dungeon | 180.9 |
| EPLP | 请求者 | 78 | 5693 | leave dungeon | 254.5 |
| SET_USER_AREA | 同伴 | 6 | 5014 | town | 3.2 |
| LEAVE_PARTY | 同伴 | 78 | 3751 | leave party | 204.2 |
| REQUEST_PEER | 同伴 | 82 | 3188 | party | 229.7 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 2583 | return to selection | 201.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 2397 | party dungeon entry | 254.5 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 2397 | party dungeon entry | 254.5 |
| RESPONSE_PEER | 同伴 | 82 | 2228 | party | 229.9 |
| LEAVE_PARTY | 请求者 | 78 | 1954 | leave party | 204.2 |
| REQUEST_PEER | 请求者 | 82 | 1782 | party | 249.9 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 11.2 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 11.2 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.7 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| RESPONSE_PEER | 请求者 | 82 | 284 | party | 229.9 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 11.3 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 11.3 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 98.94 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 151.0 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 122.8 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 888.4 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 484，p99.9 105.2 ms，max 105.2 ms（出现于 t=0.6 s；冷启动窗内 max 105.2 ms，之后 max 18.78 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：203 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。
- SET_USER_POSITION：95 个（其中截尾 0），最大 880 ms，计划发出时刻 1.4, 1.5, 1.5, 1.6, 1.6, 1.6, 1.6, 1.6, 1.6, 1.7, 1.8, 1.8 … s
- CHECK_USER_CONNECTION：81 个（其中截尾 0），最大 888 ms，计划发出时刻 1.5, 1.5, 1.5, 1.5, 1.6, 1.7, 1.7, 1.7, 2.0, 1.8, 1.9, 2.1 … s
- SELECT_CHARACTER：13 个（其中截尾 0），最大 872 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 1.4, 1.4, 2.0, 2.0 … s
- SET_USER_AREA：10 个（其中截尾 0），最大 875 ms，计划发出时刻 1.5, 1.6, 1.7, 1.8, 2.1, 2.3, 2.4, 2.4, 2.4, 3.0 s
- RETURN_SELECT_CHARACTER：3 个（其中截尾 0），最大 705 ms，计划发出时刻 2.0, 2.0, 2.4 s
- LOGIN：1 个（其中截尾 0），最大 870 ms，计划发出时刻 0.4 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1447563 行（Host 就绪之后 1447558 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 68.17 | 120.2 | 128.8 | 234.0 | 234.0 | 234.0 | 234.0 | 135.7 | 0.7 | 0 |
| inbound:GameEplpCommand | 78** | 44.59 | 74.21 | 157.3 | 157.3 | 157.3 | 44.94 | 49.40 | 157.3 | 159.6 | 0 |
| inbound:GameSetUserArea | 2656 | 23.79 | 41.52 | 57.00 | 74.86 | 76.36 | 10.81 | 76.06 | 76.36 | 207.3 | 0 |
| inbound:GameSelectDungeon | 79** | 9.77 | 14.64 | 74.13 | 74.13 | 74.13 | 74.13 | 74.13 | 13.59 | 11.3 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 22.56 | 41.98 | 49.05 | 49.05 | 49.05 | 27.59 | 27.59 | 49.05 | 196.0 | 0 |
| inbound:GameCreateCharacter | 94** | 1.90 | 5.48 | 35.35 | 35.35 | 35.35 | 35.35 | 35.35 | 6.76 | 0.4 | 0 |
| inbound:GameSetUserPosition | 41774 | 0.01 | 0.05 | 0.12 | 2.52 | 21.38 | 1.78 | 8.74 | 21.38 | 229.1 | 0 |
| inbound:GameResponsePeer | 82** | 0.18 | 0.52 | 12.03 | 12.03 | 12.03 | 12.03 | 12.03 | 0.52 | 6.9 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.11 | 0.35 | 10.60 | 10.60 | 10.60 | 10.60 | 10.60 | 0.35 | 0.2 | 0 |
| inbound:GameLogin | 81** | 0.11 | 0.27 | 10.04 | 10.04 | 10.04 | 10.04 | 10.04 | 0.22 | 0.3 | 0 |
| inbound:GameReturnToSelection | 463* | 0.46 | 1.59 | 2.99 | 9.95 | 9.95 | 9.95 | 9.95 | 3.23 | 2.0 | 0 |
| event:achievement.inventory | 940* | 0.26 | 0.69 | 2.60 | 8.87 | 8.87 | 7.19 | 7.19 | 8.87 | 246.7 | 0 |
| inbound:GameLeaveParty | 78** | 0.20 | 1.14 | 8.59 | 8.59 | 8.59 | 8.59 | 8.59 | 1.79 | 22.0 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.41 | 2.27 | 7.30 | 8.39 | 8.39 | 8.09 | 8.39 | 7.96 | 38.4 | 0 |
| inbound:GameConnectionCheck | 1389511 | 0.00 | 0.00 | 0.01 | 0.04 | 7.43 | 0.13 | 2.52 | 7.43 | 106.9 | 0 |
| inbound:GameRequestPeer | 82** | 0.49 | 1.97 | 7.37 | 7.37 | 7.37 | 7.37 | 7.37 | 2.64 | 6.8 | 0 |
| timer:CreatureClockService | 276* | 0.16 | 0.47 | 3.08 | 6.81 | 6.81 | 6.81 | 6.81 | 6.27 | 1.3 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 6.20 | 6.20 | 6.20 | 6.20 | 6.20 | 0.05 | 0.1 | 0 |
| event:quest.inventory | 940* | 0.10 | 0.29 | 1.00 | 5.77 | 5.77 | 5.77 | 5.77 | 2.85 | 0.4 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.60 | 5.49 | 5.49 | 5.49 | 5.49 | 5.49 | 5.49 | 0.81 | 60.0 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.82 | 4.71 | 4.71 | 4.71 | 4.71 | 4.71 | 2.71 | 0.1 | 0 |
| inbound:GameFinishLoading | 158* | 0.10 | 0.21 | 1.81 | 3.23 | 3.23 | 3.23 | 3.23 | 0.23 | 11.3 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.04 | 0.21 | 3.22 | 3.22 | 3.22 | 3.22 | 3.22 | 0.19 | 0.3 | 0 |
| timer:DungeonPartyWipeService | 1104 | 0.01 | 0.03 | 0.05 | 0.66 | 2.53 | 2.53 | 2.53 | 0.07 | 0.2 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.40 | 2.47 | 2.47 | 2.47 | 2.47 | 2.47 | 2.47 | 0.67 | 30.9 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.31 | 1.87 | 1.87 | 1.87 | 1.87 | 1.69 | 1.69 | 1.87 | 180.0 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.08 | 0.14 | 1.83 | 1.83 | 1.83 | 1.83 | 0.17 | 1.3 | 0 |
| event:altar.departure | 158* | 0.11 | 0.31 | 1.31 | 1.83 | 1.83 | 1.83 | 1.83 | 1.31 | 16.2 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.08 | 1.66 | 1.66 | 1.66 | 1.66 | 1.66 | 1.66 | 0.08 | 60.0 | 0 |
| event:quest.mirror-leave | 158* | 0.12 | 0.29 | 1.25 | 1.41 | 1.41 | 0.73 | 0.73 | 1.41 | 239.1 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.18 | 1.24 | 1.24 | 1.24 | 1.24 | 1.24 | 1.24 | 0.56 | 60.0 | 0 |
| timer:JoustMaintenanceService | 276* | 0.08 | 0.17 | 0.22 | 1.20 | 1.20 | 0.14 | 0.21 | 1.20 | 126.9 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.03 | 1.19 | 1.19 | 1.19 | 1.19 | 0.03 | 1.3 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.07 | 1.13 | 1.13 | 1.13 | 1.13 | 1.13 | 1.13 | 0.57 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.91 | 0.91 | 0.91 | 0.91 | 0.03 | 1.3 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.25 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.32 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.18 | 0.65 | 0.65 | 0.65 | 0.65 | 0.65 | 0.65 | 0.20 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 0.64 | 0.64 | 0.64 | 0.64 | 0.64 | 0.64 | 0.10 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.05 | 0.59 | 0.59 | 0.59 | 0.59 | 0.59 | 0.59 | 0.05 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.05 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.06 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.07 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5442 | 0.00 | 0.01 | 0.01 | 0.02 | 0.52 | 0.52 | 0.52 | 0.03 | 0.0 | 0 |
| event:tournament.departure | 158* | 0.10 | 0.22 | 0.27 | 0.29 | 0.29 | 0.19 | 0.19 | 0.29 | 222.3 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.02 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 158* | 0.00 | 0.01 | 0.02 | 0.05 | 0.05 | 0.05 | 0.05 | 0.02 | 11.3 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 98.94 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26134 | 0.07 | 51.32 | 101.3 | 382.9 | 872.5 | inbound:GameConnectionCheck | 1.3 |
| 冷启动窗 | 只算 inbound: | 23186 | 0.07 | 49.24 | 96.31 | 324.3 | 872.5 |  |  |
| 稳态窗 | 全部作业 | 52951 | 0.07 | 48.47 | 89.78 | 138.2 | 170.8 | inbound:GameSetUserPosition | 121.5 |
| 稳态窗 | 只算 inbound: | 48045 | 0.07 | 47.67 | 89.24 | 135.9 | 170.8 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1104 | 0.35 | 83.87 | 103.3 | 131.9 | 132.0 | inbound:GameConnectionCheck | 222.2 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 811* | 0.41 | 88.84 | 104.7 | 132.0 | 132.0 |  |  |
| (e) 灌包期间 | 全部作业 | 1339826 | 0.00 | 0.00 | 0.01 | 36.16 | 189.9 | inbound:GameConnectionCheck | 257.1 |
| (e) 灌包期间 | 只算 inbound: | 1338697 | 0.00 | 0.00 | 0.01 | 33.41 | 189.9 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 110 | 305.6 | 434.4 | inbound:GameSelectCharacter | 234.0 |
| 1 | 175 | 872.4 | 872.5 | inbound:GameConnectionCheck | 123.3 |
| 2 | 134 | 391.6 | 392.2 | inbound:GameSetUserPosition | 117.2 |
| 3 | 957 | 218.3 | 248.5 | inbound:GameSetUserPosition | 85.51 |
| 4 | 433 | 56.00 | 60.77 | inbound:GameConnectionCheck | 60.93 |
| 5 | 452 | 89.51 | 107.5 | event:quest.inventory | 107.1 |
| 6 | 435 | 112.1 | 112.2 | inbound:GameConnectionCheck | 83.16 |
| 7 | 421 | 34.83 | 57.84 | inbound:GameSetUserPosition | 63.74 |
| 8 | 431 | 107.8 | 107.9 | inbound:GameConnectionCheck | 109.0 |
| 9 | 418 | 51.76 | 58.54 | timer:DungeonPartyWipeService | 58.67 |
| 10 | 464 | 107.4 | 118.1 | inbound:GameConnectionCheck | 117.6 |
| 11 | 414 | 85.94 | 85.96 | inbound:GameSetUserPosition | 82.68 |
| 12 | 493 | 111.8 | 114.8 | event:quest.inventory | 114.2 |
| 13 | 419 | 118.8 | 118.8 | inbound:GameConnectionCheck | 101.2 |
| 14 | 454 | 89.26 | 100.6 | inbound:GameConnectionCheck | 103.6 |
| 15 | 424 | 56.34 | 60.83 | inbound:GameSetUserPosition | 65.64 |
| 16 | 450 | 61.08 | 64.77 | timer:TournamentRewardSweepService | 79.49 |
| 17 | 443 | 104.1 | 126.4 | inbound:GameConnectionCheck | 127.7 |
| 18 | 451 | 57.31 | 72.03 | inbound:GameConnectionCheck | 86.20 |
| 19 | 426 | 113.6 | 113.6 | inbound:GameSetUserPosition | 61.79 |
| 20 | 449 | 81.03 | 95.61 | timer:TournamentRewardSweepService | 110.3 |
| 21 | 433 | 50.54 | 58.25 | inbound:GameSetUserPosition | 70.87 |
| 22 | 386 | 31.43 | 83.52 | inbound:GameConnectionCheck | 92.89 |
| 23 | 485 | 92.66 | 108.1 | inbound:GameConnectionCheck | 114.6 |
| 24 | 465 | 75.76 | 95.90 | event:quest.inventory | 95.38 |
| 25 | 456 | 51.33 | 70.39 | event:quest.inventory | 69.94 |
| 26 | 426 | 44.51 | 57.34 | inbound:GameConnectionCheck | 57.56 |
| 27 | 437 | 46.13 | 66.18 | inbound:GameSetUserPosition | 45.04 |
| 28 | 433 | 70.26 | 85.21 | inbound:GameSetUserPosition | 92.89 |
| 29 | 425 | 66.81 | 66.83 | inbound:GameConnectionCheck | 68.17 |
| 30 | 431 | 41.20 | 47.44 | inbound:GameConnectionCheck | 36.92 |
| 31 | 490 | 62.17 | 76.84 | event:quest.inventory | 73.83 |
| 32 | 419 | 83.87 | 107.7 | inbound:GameSetUserPosition | 110.3 |
| 33 | 447 | 45.82 | 60.58 | inbound:GameConnectionCheck | 65.45 |
| 34 | 437 | 103.6 | 103.8 | inbound:GameSetUserPosition | 113.9 |
| 35 | 414 | 43.96 | 57.26 | timer:TournamentRewardSweepService | 70.76 |
| 36 | 473 | 70.34 | 70.35 | inbound:GameConnectionCheck | 74.84 |
| 37 | 460 | 43.48 | 61.21 | event:quest.inventory | 60.95 |
| 38 | 459 | 49.67 | 70.57 | event:quest.inventory | 70.28 |
| 39 | 474 | 159.4 | 163.8 | inbound:GameConnectionCheck | 104.7 |
| 40 | 429 | 73.50 | 88.73 | inbound:GameConnectionCheck | 103.6 |
| 41 | 418 | 76.17 | 76.30 | inbound:GameConnectionCheck | 71.80 |
| 42 | 457 | 52.03 | 52.03 | inbound:GameSetUserPosition | 40.42 |
| 43 | 451 | 104.0 | 104.0 | inbound:GameConnectionCheck | 105.7 |
| 44 | 439 | 63.54 | 68.00 | inbound:GameConnectionCheck | 74.35 |
| 45 | 433 | 40.46 | 48.43 | inbound:GameConnectionCheck | 63.72 |
| 46 | 442 | 81.70 | 98.89 | inbound:GameConnectionCheck | 99.84 |
| 47 | 448 | 76.17 | 91.18 | inbound:GameConnectionCheck | 91.20 |
| 48 | 419 | 77.29 | 102.0 | inbound:GameSetUserPosition | 106.4 |
| 49 | 472 | 65.50 | 69.74 | timer:TournamentRewardSweepService | 83.56 |
| 50 | 452 | 87.31 | 96.83 | timer:TournamentRewardSweepService | 110.2 |
| 51 | 404 | 34.37 | 55.30 | inbound:GameSetUserPosition | 34.20 |
| 52 | 468 | 74.41 | 80.71 | inbound:GameConnectionCheck | 81.30 |
| 53 | 432 | 31.64 | 33.33 | inbound:GameConnectionCheck | 33.06 |
| 54 | 419 | 55.17 | 81.58 | inbound:GameSetUserPosition | 84.00 |
| 55 | 474 | 91.50 | 113.7 | inbound:GameSetUserPosition | 113.2 |
| 56 | 430 | 40.30 | 59.35 | inbound:GameConnectionCheck | 62.14 |
| 57 | 465 | 92.09 | 97.63 | timer:TournamentRewardSweepService | 115.3 |
| 58 | 432 | 54.51 | 66.94 | timer:TournamentRewardSweepService | 79.44 |
| 59 | 447 | 76.16 | 76.17 | inbound:GameConnectionCheck | 76.06 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26134 | 25597 | 42.7% | inbound:GameSetUserArea 12978 ms／628／max 76.06；inbound:GameSelectCharacter 10340 ms／132／max 234.0；inbound:GameEplpCommand 635 ms／17／max 49.40；inbound:GameEnterSelectDungeon 333 ms／18／max 27.59；inbound:GameSelectDungeon 261 ms／18／max 74.13；inbound:GameSetUserPosition 233 ms／8688／max 8.74；inbound:GameCreateCharacter 163 ms／39／max 35.35；event:achievement.inventory 143 ms／390／max 7.19 |
| 稳态窗 | 120.0 | 52951 | 46840 | 39.0% | inbound:GameSetUserArea 27938 ms／1141／max 66.91；inbound:GameSelectCharacter 14783 ms／196／max 135.7；inbound:GameEplpCommand 1663 ms／35／max 157.3；inbound:GameEnterSelectDungeon 829 ms／36／max 47.00；inbound:GameSetUserPosition 486 ms／18238／max 21.23；inbound:GameSelectDungeon 364 ms／36／max 13.14；inbound:GameConnectionCheck 157 ms／27950／max 7.43；inbound:GameReturnToSelection 127 ms／197／max 3.23 |
| (d) 10 并发登录的轮内 | 1.6 | 1104 | 1078 | 68.5% | inbound:GameSetUserArea 696 ms／18／max 70.17；inbound:GameSelectCharacter 280 ms／4／max 96.85；inbound:GameEplpCommand 56 ms／1／max 56.34；event:achievement.inventory 10 ms／10／max 7.58；http:POST /api/launcher/v1/register 8 ms／60／max 0.35；inbound:GameLogin 6 ms／60／max 0.22；inbound:GameSetUserPosition 5 ms／299／max 0.27；http:POST /api/launcher/v1/login 3 ms／60／max 0.19 |
| (e) 灌包期间 | 30.0 | 1339826 | 17068 | 56.9% | inbound:GameSetUserArea 7745 ms／275／max 53.13；inbound:GameSelectCharacter 4219 ms／51／max 124.4；inbound:GameConnectionCheck 4115 ms／1333435／max 5.31；inbound:GameEplpCommand 423 ms／8／max 74.21；inbound:GameEnterSelectDungeon 213 ms／7／max 41.98；inbound:GameSetUserPosition 120 ms／4815／max 5.73；inbound:GameSelectDungeon 66 ms／7／max 10.44；event:southern-dale.daily-refill-notifications 54 ms／51／max 6.74 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.4，`timer:` 31.7，`event:` 9.2。

