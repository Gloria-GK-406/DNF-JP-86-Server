# 基线测量 — m3afd（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3683 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 256）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48002 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25609 | 53.35% |
| SET_USER_POSITION | 18393 | 38.32% |
| STEP_FENCE | 2143 | 4.46% |
| SET_USER_AREA | 1142 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |
| EPLP | 34 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25610 | 0.74 | 61.78 | 103.3 | 146.7 | 185.0 | 0 | 99.10 | 145.0 | 184.8 |
| SET_USER_POSITION | 18393 | 0.73 | 61.06 | 103.3 | 146.3 | 184.8 | 0 | 99.15 | 145.1 | 184.8 |
| SET_USER_AREA | 1142 | 28.72 | 81.53 | 125.7 | 145.3 | 154.7 | 0 | 123.8 | 144.3 | 154.4 |
| RETURN_SELECT_CHARACTER | 197* | 1.77 | 65.82 | 100.9 | 116.8 | 116.8 | 0 | 100.9 | 116.8 | 116.8 |
| SELECT_CHARACTER | 197* | 69.35 | 132.4 | 184.6 | 194.6 | 194.6 | 0 | 184.6 | 194.6 | 194.6 |
| FINISH_LOADING | 72** | 0.17 | 1.03 | 10.43 | 10.43 | 10.43 | 0 | 10.43 | 10.43 | 10.43 |
| LEAVE_PARTY | 36** | 0.40 | 17.35 | 50.66 | 50.66 | 50.66 | 0 | 50.66 | 50.66 | 50.66 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.28 | 4.01 | 7.46 | 7.46 | 7.46 | 0 | 7.46 | 7.46 | 7.46 |
| ENTER_SELECT_DUNGEON | 36** | 27.94 | 99.99 | 101.4 | 101.4 | 101.4 | 0 | 98.36 | 98.36 | 98.36 |
| SELECT_DUNGEON | 36** | 10.56 | 17.11 | 21.85 | 21.85 | 21.85 | 0 | 21.85 | 21.85 | 21.85 |
| CREATE_CHARACTER | 35** | 3.39 | 17.88 | 66.92 | 66.92 | 66.92 | 0 | 66.92 | 66.92 | 66.92 |
| EPLP | 34** | 51.31 | 133.9 | 135.1 | 135.1 | 135.1 | 0 | 133.6 | 133.6 | 133.6 |
| **全部计入的请求** | 45824 | 0.83 | 64.40 | 106.5 | 147.6 | 194.6 | 36 | 103.0 | 146.3 | 194.6 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45860 | 0.06 | 10.67 | 52.72 | 96.49 | 135.0（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.84 | 8.99 | 12.72 | 104.3 | 104.3 | 0.6 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 15.01 | 40.30 | 59.61 | 64.36 | 64.36 | 83.7 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 15.29 | 45.67 | 60.37 | 63.06 | 63.06 | 266.8 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.40 | 53.25 | 60.47 | 62.48 | 62.48 | 86.3 | 0 |
| EnterDungeonCommand | Transactional | 79** | 6.35 | 8.65 | 60.01 | 60.01 | 60.01 | 11.9 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 13.25 | 29.58 | 51.10 | 57.04 | 57.04 | 151.3 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.22 | 3.69 | 24.60 | 24.60 | 24.60 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 78** | 0.94 | 2.45 | 8.42 | 8.42 | 8.42 | 16.8 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.04 | 0.07 | 0.11 | 7.50 | 7.50 | 16.3 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.18 | 0.75 | 1.04 | 6.74 | 6.74 | 2.0 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.05 | 0.17 | 6.43 | 6.43 | 6.43 | 0.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.09 | 0.30 | 5.79 | 5.79 | 5.79 | 0.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.30 | 0.74 | 5.73 | 5.73 | 5.73 | 0.4 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.06 | 3.65 | 3.65 | 0.6 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.01 | 0.14 | 3.48 | 3.48 | 3.48 | 7.1 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.06 | 0.16 | 0.24 | 3.44 | 3.44 | 0.4 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.04 | 0.10 | 0.26 | 3.42 | 3.42 | 0.4 | 0 |
| AdvanceCreatureClockCommand | Transactional | 773* | 0.03 | 0.12 | 0.25 | 3.31 | 3.31 | 1.1 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 3.07 | 3.07 | 3.07 | 0.4 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 0.44 | 1.31 | 2.87 | 2.87 | 2.87 | 50.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.10 | 2.80 | 2.80 | 1.4 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.19 | 0.56 | 1.27 | 2.58 | 2.58 | 154.6 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.02 | 0.03 | 1.81 | 1.81 | 0.4 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.12 | 0.21 | 1.79 | 1.79 | 0.4 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.04 | 0.05 | 0.07 | 1.62 | 1.62 | 0.7 | 0 |
| LeavePartyCommand | Direct | 78** | 0.01 | 0.04 | 1.08 | 1.08 | 1.08 | 22.1 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 1.02 | 1.02 | 1.02 | 1.02 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.05 | 0.09 | 0.76 | 0.76 | 0.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.02 | 0.28 | 0.61 | 0.61 | 11.9 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.04 | 0.13 | 0.43 | 0.43 | 12.8 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.07 | 0.34 | 0.34 | 0.34 | 60.0 | 0 |

命令日志行 9784 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**801.7 ms**（CHECK_USER_CONNECTION，计划发出 t=2.40 s，solo03；参考口径 66.77 ms）。冷启动窗整体：21931 | 0.99 | 66.19 | 139.4 | 605.7 | 801.7（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 36 | 677.0 | 677.0 | SELECT_CHARACTER |
| 1 | 41 | 704.2 | 704.2 | SELECT_CHARACTER |
| 2 | 202 | 753.3 | 801.7 | CHECK_USER_CONNECTION |
| 3 | 365 | 265.6 | 315.0 | CHECK_USER_CONNECTION |
| 4 | 392 | 37.90 | 39.15 | SET_USER_AREA |
| 5 | 380 | 107.7 | 109.8 | SELECT_CHARACTER |
| 6 | 389 | 58.81 | 73.67 | SET_USER_AREA |
| 7 | 373 | 58.55 | 70.50 | SELECT_CHARACTER |
| 8 | 365 | 106.3 | 120.9 | SET_USER_AREA |
| 9 | 383 | 117.2 | 120.4 | SET_USER_POSITION |
| 10 | 382 | 113.4 | 136.0 | SET_USER_AREA |
| 11 | 384 | 118.2 | 119.5 | SET_USER_POSITION |
| 12 | 365 | 81.21 | 107.7 | ENTER_SELECT_DUNGEON |
| 13 | 377 | 68.82 | 80.83 | SELECT_CHARACTER |
| 14 | 380 | 93.75 | 131.7 | SELECT_CHARACTER |
| 15 | 383 | 90.03 | 94.72 | SET_USER_AREA |
| 16 | 382 | 122.5 | 128.8 | SET_USER_AREA |
| 17 | 382 | 100.4 | 119.5 | SELECT_CHARACTER |
| 18 | 384 | 79.10 | 80.70 | SELECT_CHARACTER |
| 19 | 385 | 72.45 | 77.25 | SELECT_CHARACTER |
| 20 | 384 | 111.4 | 112.8 | SELECT_CHARACTER |
| 21 | 388 | 83.51 | 85.36 | SELECT_CHARACTER |
| 22 | 388 | 45.83 | 58.98 | SET_USER_POSITION |
| 23 | 384 | 53.51 | 65.00 | SELECT_CHARACTER |
| 24 | 381 | 60.91 | 68.48 | SELECT_CHARACTER |
| 25 | 377 | 75.48 | 89.07 | SET_USER_AREA |
| 26 | 386 | 62.11 | 62.76 | SELECT_CHARACTER |
| 27 | 382 | 51.03 | 51.21 | SET_USER_POSITION |
| 28 | 377 | 73.13 | 84.86 | SET_USER_POSITION |
| 29 | 382 | 105.2 | 107.4 | SET_USER_POSITION |
| 30 | 388 | 57.41 | 68.85 | SET_USER_AREA |
| 31 | 375 | 61.06 | 69.79 | SET_USER_AREA |
| 32 | 367 | 109.4 | 129.6 | CHECK_USER_CONNECTION |
| 33 | 369 | 47.40 | 60.54 | SET_USER_AREA |
| 34 | 384 | 107.9 | 108.7 | SELECT_CHARACTER |
| 35 | 376 | 60.29 | 70.40 | SELECT_CHARACTER |
| 36 | 381 | 28.17 | 65.69 | SELECT_CHARACTER |
| 37 | 386 | 109.2 | 110.6 | SELECT_CHARACTER |
| 38 | 382 | 70.55 | 88.57 | SELECT_CHARACTER |
| 39 | 375 | 94.45 | 95.68 | SET_USER_POSITION |
| 40 | 382 | 54.49 | 64.40 | SELECT_CHARACTER |
| 41 | 384 | 92.03 | 104.0 | SELECT_CHARACTER |
| 42 | 383 | 68.98 | 106.4 | SELECT_CHARACTER |
| 43 | 382 | 103.8 | 116.1 | CHECK_USER_CONNECTION |
| 44 | 372 | 81.90 | 103.0 | SELECT_CHARACTER |
| 45 | 386 | 56.35 | 58.18 | SELECT_CHARACTER |
| 46 | 375 | 90.45 | 101.4 | SELECT_CHARACTER |
| 47 | 373 | 90.81 | 112.2 | SELECT_CHARACTER |
| 48 | 387 | 69.44 | 76.66 | SELECT_CHARACTER |
| 49 | 373 | 127.7 | 145.0 | SET_USER_AREA |
| 50 | 378 | 82.33 | 97.26 | CHECK_USER_CONNECTION |
| 51 | 382 | 72.93 | 91.50 | SELECT_CHARACTER |
| 52 | 380 | 103.1 | 115.2 | SELECT_CHARACTER |
| 53 | 380 | 58.97 | 93.64 | ENTER_SELECT_DUNGEON |
| 54 | 370 | 48.88 | 68.33 | SELECT_CHARACTER |
| 55 | 375 | 61.28 | 63.37 | FINISH_LOADING |
| 56 | 374 | 81.33 | 108.1 | SELECT_CHARACTER |
| 57 | 391 | 105.1 | 107.1 | SELECT_CHARACTER |
| 58 | 385 | 61.10 | 74.13 | SELECT_CHARACTER |
| 59 | 377 | 67.11 | 77.95 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45824 | 0.83 | 64.40 | 106.5 | 147.6 | 194.6 |
| 10 并发登录的轮内 | 599* | 33.27 | 135.6 | 150.9 | 168.6 | 168.6 |
| （参考：实际发出口径）稳态窗 | 45824 | 0.23 | 57.37 | 103.0 | 146.3 | 194.6 |
| （参考：实际发出口径）轮内 | 599* | 4.29 | 97.28 | 142.6 | 142.7 | 142.7 |
| 登录阶段整体（182–232 s） | 19216 | 0.94 | 69.60 | 112.6 | 144.9 | 168.6 |

每轮时长（开始 → 10 个 LOGIN 都应答）：239, 240, 281, 282, 254, 259 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17632 | 0.54 | 66.82 | 111.0 | 145.5 | 194.6 | 107.8 |
| 单人 | 轮内 | 237* | 40.60 | 140.6 | 152.5 | 168.6 | 168.6 | 142.6 |
| 双人组领队 | 稳态窗 | 14078 | 1.30 | 62.57 | 104.8 | 149.5 | 185.0 | 101.5 |
| 双人组领队 | 轮内 | 177* | 31.91 | 134.2 | 150.8 | 152.1 | 152.1 | 142.6 |
| 双人组队员 | 稳态窗 | 14114 | 0.79 | 61.84 | 104.5 | 148.5 | 184.8 | 100.6 |
| 双人组队员 | 轮内 | 185* | 33.25 | 135.6 | 150.9 | 151.8 | 151.8 | 142.6 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 123.4 | 150.8 | 208.3 | 208.3 | 208.3 |
| 启动器登录（HTTP，含校验） | 60** | 89.57 | 129.7 | 167.6 | 167.6 | 167.6 |
| Game LOGIN 0x0001 | 60** | 0.27 | 0.93 | 24.99 | 24.99 | 24.99 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 256 持续发 CHECK_USER_CONNECTION；实际应答速率 44256 包/秒（1327817 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45824 | 0.83 | 64.40 | 106.5 | 147.6 | 194.6 |
| 其余连接，灌包期间 | 11472 | 0.98 | 66.14 | 109.2 | 120.7 | 153.3 |
| （参考：实际发出口径）其余连接，灌包期间 | 11472 | 0.21 | 62.98 | 108.8 | 120.4 | 142.7 |
| 灌包连接自身（含其窗口内排队） | 1328072 | 3.07 | 7.12 | 75.78 | 235.8 | 407.8 |

灌包窗口 256 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.7 |
| SET_USER_AREA | 请求者 | 2655 | 6802 | town | 185.0 |
| EPLP | 同伴 | 78 | 6708 | leave dungeon | 40.7 |
| EPLP | 请求者 | 78 | 5714 | leave dungeon | 247.2 |
| SET_USER_AREA | 同伴 | 6 | 5710 | town | 2.8 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 3739 | return to selection | 263.8 |
| LEAVE_PARTY | 同伴 | 78 | 2743 | leave party | 220.6 |
| REQUEST_PEER | 同伴 | 83 | 2474 | party | 86.6 |
| RESPONSE_PEER | 同伴 | 83 | 2255 | party | 212.3 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 1861 | party dungeon entry | 210.8 |
| LEAVE_PARTY | 请求者 | 78 | 1694 | leave party | 220.6 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 1562 | party dungeon entry | 210.8 |
| REQUEST_PEER | 请求者 | 83 | 1415 | party | 86.6 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 11.8 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 11.8 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.7 |
| RESPONSE_PEER | 请求者 | 83 | 884 | party | 8.2 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 11.9 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 11.9 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 106.5 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 150.9 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 109.2 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 801.7 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 484，p99.9 104.3 ms，max 104.3 ms（出现于 t=0.6 s；冷启动窗内 max 104.3 ms，之后 max 14.68 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 1 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：166 个；截尾样本（超时或连接被关闭）1 个，协议无回包 83 个。
- CHECK_USER_CONNECTION：72 个（其中截尾 0），最大 802 ms，计划发出时刻 1.2, 1.2, 1.3, 1.3, 1.4, 1.5, 1.6, 1.8, 1.9, 2.4, 2.4, 2.4 … s
- SET_USER_POSITION：66 个（其中截尾 0），最大 785 ms，计划发出时刻 1.4, 1.5, 1.6, 1.8, 1.9, 2.0, 2.0, 2.1, 2.1, 2.4, 2.4, 2.4 … s
- SELECT_CHARACTER：12 个（其中截尾 0），最大 704 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 0.4, 1.2, 1.2, 1.2, 1.2, 1.2, 1.2, 2.4 s
- CREATE_CHARACTER：8 个（其中截尾 0），最大 615 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4 s
- SET_USER_AREA：5 个（其中截尾 0），最大 488 ms，计划发出时刻 1.7, 2.6, 2.9, 2.9, 2.8 s
- RETURN_SELECT_CHARACTER：3 个（其中截尾 0），最大 454 ms，计划发出时刻 2.4, 2.5, 2.5 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1448922 行（Host 就绪之后 1448917 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 68.62 | 120.9 | 140.8 | 237.1 | 237.1 | 237.1 | 237.1 | 144.8 | 0.7 | 0 |
| inbound:GameSetUserArea | 2655 | 23.34 | 41.66 | 56.30 | 74.62 | 94.01 | 9.90 | 56.69 | 94.01 | 212.2 | 0 |
| inbound:GameEplpCommand | 78** | 43.55 | 76.84 | 92.68 | 92.68 | 92.68 | 33.26 | 47.90 | 92.68 | 198.7 | 0 |
| inbound:GameSelectDungeon | 79** | 10.07 | 16.99 | 75.29 | 75.29 | 75.29 | 75.29 | 75.29 | 21.71 | 11.9 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 23.11 | 37.71 | 45.17 | 45.17 | 45.17 | 31.26 | 45.17 | 40.02 | 53.2 | 0 |
| inbound:GameCreateCharacter | 94** | 1.66 | 5.02 | 32.82 | 32.82 | 32.82 | 32.82 | 32.82 | 6.30 | 0.4 | 0 |
| inbound:GameLogin | 81** | 0.12 | 0.77 | 12.84 | 12.84 | 12.84 | 12.84 | 12.84 | 1.56 | 0.4 | 0 |
| event:achievement.inventory | 940* | 0.25 | 0.54 | 1.86 | 12.22 | 12.22 | 12.22 | 12.22 | 5.91 | 0.4 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.88 | 11.95 | 11.95 | 11.95 | 11.95 | 11.95 | – | 11.95 | 60.0 | 0 |
| inbound:GameSetUserPosition | 41888 | 0.01 | 0.06 | 0.13 | 2.54 | 11.18 | 1.72 | 11.18 | 8.21 | 57.5 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.10 | 0.42 | 10.99 | 10.99 | 10.99 | 10.99 | 10.99 | 2.08 | 0.2 | 0 |
| inbound:GameReturnToSelection | 463* | 0.46 | 1.53 | 3.35 | 10.46 | 10.46 | 10.46 | 10.46 | 4.67 | 2.0 | 0 |
| inbound:GameResponsePeer | 83** | 0.18 | 0.99 | 10.38 | 10.38 | 10.38 | 10.38 | 10.38 | 1.30 | 7.1 | 0 |
| inbound:GameLeaveParty | 78** | 0.22 | 1.27 | 9.51 | 9.51 | 9.51 | 9.51 | 9.51 | 4.80 | 22.1 | 0 |
| inbound:GameConnectionCheck | 1390771 | 0.00 | 0.00 | 0.01 | 0.05 | 9.27 | 0.15 | 1.59 | 9.27 | 92.8 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.42 | 3.12 | 5.89 | 8.01 | 8.01 | 5.60 | 5.60 | 8.01 | 66.9 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.06 | 0.27 | 7.15 | 7.15 | 7.15 | 7.15 | 7.15 | 0.27 | 0.3 | 0 |
| timer:CreatureClockService | 276* | 0.17 | 0.52 | 1.20 | 6.58 | 6.58 | 6.58 | 6.58 | 5.33 | 1.1 | 0 |
| inbound:GameRequestPeer | 83** | 0.53 | 2.42 | 5.69 | 5.69 | 5.69 | 5.69 | 5.69 | 2.42 | 7.1 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.07 | 4.22 | 4.22 | 4.22 | 4.22 | 4.22 | 0.20 | 0.1 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.63 | 3.75 | 3.75 | 3.75 | 3.75 | 3.75 | 1.54 | 0.1 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.50 | 3.38 | 3.38 | 3.38 | 3.38 | 0.95 | 0.95 | 3.38 | 120.0 | 0 |
| inbound:GameFinishLoading | 158* | 0.09 | 0.19 | 1.70 | 3.23 | 3.23 | 3.23 | 3.23 | 0.21 | 11.9 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.54 | 2.22 | 2.22 | 2.22 | 2.22 | 2.22 | 2.22 | 0.75 | 12.8 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.71 | 1.87 | 1.87 | 1.87 | 1.87 | 1.84 | – | 1.87 | 180.0 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.08 | 0.14 | 1.80 | 1.80 | 1.80 | 1.80 | 0.22 | 1.1 | 0 |
| event:altar.departure | 158* | 0.12 | 0.32 | 1.42 | 1.76 | 1.76 | 1.76 | 1.76 | 1.42 | 16.8 | 0 |
| timer:JoustMaintenanceService | 276* | 0.08 | 0.22 | 0.42 | 1.72 | 1.72 | 0.12 | 0.30 | 1.72 | 78.9 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.09 | 1.49 | 1.49 | 1.49 | 1.49 | 1.49 | 1.49 | 0.23 | 60.0 | 0 |
| event:quest.inventory | 940* | 0.10 | 0.30 | 0.72 | 1.35 | 1.35 | 0.62 | 1.20 | 1.35 | 112.1 | 0 |
| timer:DungeonPartyWipeService | 1104 | 0.01 | 0.03 | 0.05 | 0.39 | 1.32 | 1.32 | 1.32 | 0.18 | 0.2 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.03 | 1.16 | 1.16 | 1.16 | 1.16 | 0.75 | 1.1 | 0 |
| timer:RoomMechanismMaintenanceService | 158* | 0.00 | 0.01 | 0.04 | 1.11 | 1.11 | 0.04 | 0.04 | 1.11 | 249.5 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.08 | 1.01 | 1.01 | 1.01 | 1.01 | 1.01 | 1.01 | 0.71 | 60.0 | 0 |
| event:quest.mirror-leave | 158* | 0.12 | 0.31 | 0.80 | 0.96 | 0.96 | 0.77 | 0.77 | 0.96 | 182.1 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.03 | 0.80 | 0.80 | 0.80 | 0.80 | 0.03 | 1.1 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.08 | 0.80 | 0.80 | 0.80 | 0.80 | 0.47 | 0.47 | 0.80 | 120.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.19 | 0.73 | 0.73 | 0.73 | 0.73 | 0.73 | – | 0.73 | 60.0 | 0 |
| event:tournament.departure | 158* | 0.09 | 0.18 | 0.35 | 0.60 | 0.60 | 0.21 | 0.21 | 0.60 | 117.8 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 0.59 | 0.59 | 0.59 | 0.59 | 0.59 | 0.59 | 0.31 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.08 | 0.55 | 0.55 | 0.55 | 0.55 | 0.55 | – | 0.55 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5426 | 0.00 | 0.01 | 0.01 | 0.03 | 0.51 | 0.51 | 0.51 | 0.04 | 0.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.08 | 0.48 | 0.48 | 0.48 | 0.48 | 0.48 | 0.48 | 0.25 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.46 | 0.46 | 0.46 | 0.46 | 0.46 | 0.46 | 0.16 | 60.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.15 | 0.15 | 0.15 | 0.15 | 0.15 | 0.15 | 0.03 | 29.9 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 106.5 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26100 | 0.04 | 55.61 | 101.7 | 592.5 | 704.0 | http:POST /api/launcher/v1/register | 1.9 |
| 冷启动窗 | 只算 inbound: | 23171 | 0.03 | 53.14 | 100.0 | 349.0 | 703.7 |  |  |
| 稳态窗 | 全部作业 | 52940 | 0.03 | 52.29 | 97.53 | 142.1 | 190.4 | timer:TournamentRewardSweepService | 86.4 |
| 稳态窗 | 只算 inbound: | 48060 | 0.03 | 49.79 | 96.25 | 142.1 | 184.7 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1094 | 0.33 | 53.53 | 103.8 | 112.9 | 112.9 | event:quest.inventory | 212.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 794* | 0.24 | 59.50 | 103.8 | 111.2 | 111.2 |  |  |
| (e) 灌包期间 | 全部作业 | 1341234 | 0.00 | 0.00 | 0.01 | 37.84 | 142.5 | inbound:GameConnectionCheck | 246.0 |
| (e) 灌包期间 | 只算 inbound: | 1340082 | 0.00 | 0.00 | 0.01 | 35.67 | 142.5 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 108 | 349.0 | 451.4 | inbound:GameSelectCharacter | 237.1 |
| 1 | 114 | 703.9 | 704.0 | http:POST /api/launcher/v1/register | 127.8 |
| 2 | 249 | 282.4 | 305.2 | inbound:GameSetUserPosition | 114.2 |
| 3 | 903 | 136.1 | 200.2 | inbound:GameConnectionCheck | 105.3 |
| 4 | 431 | 33.51 | 38.91 | inbound:GameConnectionCheck | 38.82 |
| 5 | 454 | 106.2 | 107.7 | inbound:GameConnectionCheck | 109.6 |
| 6 | 434 | 58.10 | 66.68 | timer:TournamentRewardSweepService | 58.67 |
| 7 | 428 | 40.76 | 53.44 | inbound:GameConnectionCheck | 29.27 |
| 8 | 440 | 104.0 | 114.2 | timer:TournamentRewardSweepService | 106.1 |
| 9 | 432 | 113.5 | 113.6 | inbound:GameSetUserPosition | 88.95 |
| 10 | 430 | 112.7 | 131.3 | timer:TournamentRewardSweepService | 113.3 |
| 11 | 443 | 116.0 | 116.1 | inbound:GameConnectionCheck | 102.1 |
| 12 | 467 | 78.91 | 81.06 | inbound:GameSetUserPosition | 81.69 |
| 13 | 441 | 68.62 | 80.52 | event:quest.inventory | 79.91 |
| 14 | 430 | 20.16 | 22.64 | inbound:GameSetUserPosition | 22.54 |
| 15 | 432 | 94.03 | 127.0 | timer:TournamentRewardSweepService | 131.5 |
| 16 | 440 | 121.9 | 128.2 | inbound:GameSetUserPosition | 124.5 |
| 17 | 413 | 92.29 | 114.7 | timer:TournamentRewardSweepService | 119.4 |
| 18 | 474 | 79.02 | 100.0 | inbound:GameConnectionCheck | 80.54 |
| 19 | 440 | 70.84 | 71.85 | timer:TournamentRewardSweepService | 77.09 |
| 20 | 440 | 110.7 | 110.8 | inbound:GameConnectionCheck | 112.6 |
| 21 | 433 | 82.31 | 84.66 | inbound:GameSetUserPosition | 85.24 |
| 22 | 436 | 41.33 | 58.47 | inbound:GameSetUserPosition | 41.32 |
| 23 | 429 | 47.54 | 64.37 | inbound:GameConnectionCheck | 64.84 |
| 24 | 473 | 63.59 | 68.25 | event:quest.inventory | 67.75 |
| 25 | 455 | 72.00 | 82.30 | timer:TournamentRewardSweepService | 61.63 |
| 26 | 420 | 32.77 | 39.48 | inbound:GameSetUserPosition | 37.63 |
| 27 | 443 | 59.44 | 62.05 | inbound:GameConnectionCheck | 62.66 |
| 28 | 433 | 71.58 | 84.59 | inbound:GameSetUserPosition | 63.69 |
| 29 | 434 | 102.2 | 102.3 | inbound:GameSetUserPosition | 63.40 |
| 30 | 433 | 54.32 | 64.55 | timer:TournamentRewardSweepService | 45.05 |
| 31 | 452 | 59.01 | 68.81 | event:quest.inventory | 68.80 |
| 32 | 467 | 104.7 | 108.8 | inbound:GameConnectionCheck | 102.6 |
| 33 | 422 | 45.17 | 53.26 | timer:TournamentRewardSweepService | 58.49 |
| 34 | 436 | 100.1 | 108.0 | inbound:GameSetUserPosition | 108.6 |
| 35 | 435 | 58.49 | 65.87 | timer:TournamentRewardSweepService | 70.24 |
| 36 | 432 | 26.66 | 59.80 | timer:TournamentRewardSweepService | 65.58 |
| 37 | 463 | 108.4 | 110.2 | event:quest.inventory | 109.9 |
| 38 | 464 | 70.17 | 80.85 | timer:TournamentRewardSweepService | 88.42 |
| 39 | 482 | 88.60 | 88.67 | inbound:GameSetUserPosition | 66.60 |
| 40 | 440 | 49.10 | 63.72 | inbound:GameConnectionCheck | 64.29 |
| 41 | 443 | 90.42 | 101.7 | timer:TournamentRewardSweepService | 103.9 |
| 42 | 426 | 61.64 | 61.65 | inbound:GameSetUserPosition | 61.56 |
| 43 | 437 | 96.88 | 115.6 | inbound:GameConnectionCheck | 106.2 |
| 44 | 452 | 68.42 | 96.61 | inbound:GameConnectionCheck | 102.9 |
| 45 | 436 | 55.90 | 57.30 | inbound:GameSetUserPosition | 58.07 |
| 46 | 455 | 88.88 | 90.08 | inbound:GameConnectionCheck | 65.91 |
| 47 | 431 | 85.32 | 90.34 | inbound:GameConnectionCheck | 101.3 |
| 48 | 428 | 72.29 | 107.0 | timer:TournamentRewardSweepService | 112.1 |
| 49 | 440 | 106.2 | 112.2 | inbound:GameSetUserPosition | 100.5 |
| 50 | 454 | 80.67 | 89.96 | inbound:GameResponsePeer | 89.56 |
| 51 | 453 | 68.27 | 90.94 | event:quest.inventory | 90.60 |
| 52 | 434 | 98.98 | 109.3 | timer:TournamentRewardSweepService | 115.0 |
| 53 | 444 | 46.48 | 58.89 | inbound:GameConnectionCheck | 59.59 |
| 54 | 422 | 48.63 | 63.28 | timer:TournamentRewardSweepService | 68.18 |
| 55 | 447 | 58.57 | 63.18 | inbound:GameFinishLoading | 62.79 |
| 56 | 451 | 77.69 | 80.20 | inbound:GameConnectionCheck | 108.0 |
| 57 | 431 | 93.93 | 105.1 | inbound:GameSetUserPosition | 107.0 |
| 58 | 441 | 42.91 | 69.84 | timer:TournamentRewardSweepService | 73.98 |
| 59 | 450 | 59.43 | 71.40 | inbound:GameConnectionCheck | 59.85 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26100 | 25565 | 42.6% | inbound:GameSetUserArea 13076 ms／625／max 56.69；inbound:GameSelectCharacter 10260 ms／132／max 237.1；inbound:GameEplpCommand 592 ms／17／max 47.90；inbound:GameEnterSelectDungeon 350 ms／18／max 45.17；inbound:GameSelectDungeon 274 ms／18／max 75.29；inbound:GameSetUserPosition 265 ms／8650／max 11.18；event:achievement.inventory 133 ms／390／max 12.22；inbound:GameCreateCharacter 123 ms／39／max 32.82 |
| 稳态窗 | 120.0 | 52940 | 46865 | 39.1% | inbound:GameSetUserArea 27039 ms／1142／max 74.62；inbound:GameSelectCharacter 15893 ms／196／max 144.8；inbound:GameEplpCommand 1325 ms／34／max 60.77；inbound:GameEnterSelectDungeon 797 ms／36／max 40.02；inbound:GameSetUserPosition 531 ms／18394／max 8.21；inbound:GameSelectDungeon 401 ms／36／max 21.71；event:southern-dale.daily-refill-notifications 173 ms／197／max 8.01；inbound:GameConnectionCheck 160 ms／27810／max 9.27 |
| (d) 10 并发登录的轮内 | 1.6 | 1094 | 991 | 63.7% | inbound:GameSetUserArea 563 ms／16／max 94.01；inbound:GameSelectCharacter 287 ms／3／max 112.7；inbound:GameEplpCommand 101 ms／2／max 53.24；http:POST /api/launcher/v1/register 10 ms／60／max 2.08；inbound:GameLogin 8 ms／60／max 1.56；inbound:GameSetUserPosition 4 ms／265／max 0.14；http:POST /api/launcher/v1/login 4 ms／60／max 0.27；event:achievement.inventory 3 ms／9／max 0.40 |
| (e) 灌包期间 | 30.0 | 1341234 | 17093 | 57.0% | inbound:GameSetUserArea 7968 ms／275／max 68.19；inbound:GameConnectionCheck 4073 ms／1334835／max 5.68；inbound:GameSelectCharacter 3966 ms／50／max 122.3；inbound:GameEplpCommand 495 ms／9／max 74.24；inbound:GameEnterSelectDungeon 237 ms／8／max 34.40；inbound:GameSetUserPosition 145 ms／4798／max 5.99；inbound:GameSelectDungeon 80 ms／8／max 11.67；event:southern-dale.daily-refill-notifications 34 ms／50／max 6.24 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 31.5，`event:` 9.2。

