# 基线测量 — m2ad1（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3659 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48000 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25510 | 53.15% |
| SET_USER_POSITION | 18477 | 38.49% |
| STEP_FENCE | 2151 | 4.48% |
| SET_USER_AREA | 1144 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| EPLP | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25510 | 1.11 | 60.23 | 102.9 | 142.4 | 167.5 | 0 | 98.07 | 140.6 | 167.0 |
| SET_USER_POSITION | 18477 | 1.16 | 61.27 | 100.1 | 142.4 | 167.3 | 0 | 96.61 | 139.0 | 166.8 |
| SET_USER_AREA | 1144 | 28.51 | 84.94 | 129.2 | 166.1 | 169.2 | 0 | 129.2 | 166.1 | 167.9 |
| RETURN_SELECT_CHARACTER | 197* | 2.24 | 48.60 | 96.17 | 104.1 | 104.1 | 0 | 95.71 | 104.1 | 104.1 |
| SELECT_CHARACTER | 197* | 68.92 | 120.4 | 167.3 | 171.2 | 171.2 | 0 | 167.3 | 171.2 | 171.2 |
| FINISH_LOADING | 72** | 0.17 | 9.32 | 112.4 | 112.4 | 112.4 | 0 | 112.4 | 112.4 | 112.4 |
| LEAVE_PARTY | 37** | 0.40 | 58.71 | 159.6 | 159.6 | 159.6 | 0 | 159.6 | 159.6 | 159.6 |
| EPLP | 36** | 46.76 | 107.5 | 126.6 | 126.6 | 126.6 | 0 | 125.6 | 125.6 | 125.6 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.26 | 1.46 | 2.40 | 2.40 | 2.40 | 0 | 2.40 | 2.40 | 2.40 |
| ENTER_SELECT_DUNGEON | 36** | 30.32 | 71.35 | 78.94 | 78.94 | 78.94 | 0 | 78.94 | 78.94 | 78.94 |
| SELECT_DUNGEON | 36** | 9.83 | 72.86 | 75.87 | 75.87 | 75.87 | 0 | 75.87 | 75.87 | 75.87 |
| CREATE_CHARACTER | 35** | 3.52 | 24.45 | 67.90 | 67.90 | 67.90 | 0 | 67.90 | 67.90 | 67.90 |
| **全部计入的请求** | 45813 | 1.22 | 62.90 | 103.9 | 143.3 | 171.2 | 36 | 100.2 | 141.8 | 171.2 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45849 | 0.20 | 10.07 | 51.97 | 92.37 | 161.4（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.71 | 9.82 | 12.82 | 102.0 | 102.0 | 0.5 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.21 | 55.64 | 61.06 | 62.18 | 62.18 | 148.1 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.99 | 40.74 | 55.89 | 61.60 | 61.60 | 121.7 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 15.17 | 48.57 | 58.42 | 61.11 | 61.11 | 247.3 | 0 |
| EnterDungeonCommand | Transactional | 82** | 6.03 | 8.69 | 61.03 | 61.03 | 61.03 | 11.6 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 12.68 | 25.13 | 51.70 | 58.38 | 58.38 | 177.3 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.17 | 3.42 | 30.40 | 30.40 | 30.40 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 78** | 1.06 | 2.33 | 12.40 | 12.40 | 12.40 | 15.8 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.10 | 0.29 | 9.29 | 9.29 | 9.29 | 0.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.32 | 1.68 | 9.00 | 9.00 | 9.00 | 198.8 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.17 | 0.76 | 1.23 | 6.82 | 6.82 | 1.4 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.19 | 0.73 | 1.59 | 5.92 | 5.92 | 230.4 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.04 | 5.50 | 5.50 | 5.50 | 222.3 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.04 | 0.07 | 0.10 | 4.00 | 4.00 | 0.6 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.06 | 3.70 | 3.70 | 0.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.07 | 0.16 | 0.27 | 3.41 | 3.41 | 0.4 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.04 | 0.09 | 0.15 | 3.41 | 3.41 | 0.4 | 0 |
| AdvanceCreatureClockCommand | Transactional | 774* | 0.03 | 0.11 | 0.31 | 3.09 | 3.09 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.07 | 2.81 | 2.81 | 0.6 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.01 | 0.16 | 2.65 | 2.65 | 2.65 | 7.2 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 0.38 | 0.85 | 2.57 | 2.57 | 2.57 | 51.9 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.16 | 2.53 | 2.53 | 2.53 | 0.3 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.13 | 0.40 | 1.83 | 1.83 | 13.3 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.02 | 0.03 | 1.55 | 1.55 | 0.3 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.04 | 0.05 | 0.06 | 1.52 | 1.52 | 0.6 | 0 |
| LeavePartyCommand | Direct | 78** | 0.01 | 0.03 | 1.11 | 1.11 | 1.11 | 20.0 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.02 | 0.94 | 0.94 | 0.94 | 0.94 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.06 | 0.10 | 0.71 | 0.71 | 0.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 164* | 0.01 | 0.02 | 0.27 | 0.61 | 0.61 | 11.6 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 84** | 0.01 | 0.04 | 0.53 | 0.53 | 0.53 | 47.4 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.06 | 0.36 | 0.36 | 0.36 | 60.0 | 0 |

命令日志行 9771 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**1020.2 ms**（SET_USER_POSITION，计划发出 t=2.02 s，solo00；参考口径 115.1 ms）。冷启动窗整体：22003 | 1.29 | 69.88 | 286.9 | 950.3 | 1020.2（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 35 | 566.9 | 566.9 | SELECT_CHARACTER |
| 1 | 107 | 978.4 | 978.4 | CHECK_USER_CONNECTION |
| 2 | 189 | 1020.2 | 1020.2 | SET_USER_POSITION |
| 3 | 386 | 432.6 | 461.2 | SET_USER_POSITION |
| 4 | 384 | 109.6 | 113.7 | SELECT_CHARACTER |
| 5 | 387 | 72.16 | 85.81 | SELECT_CHARACTER |
| 6 | 385 | 35.84 | 40.10 | SET_USER_AREA |
| 7 | 366 | 103.1 | 109.9 | SELECT_CHARACTER |
| 8 | 379 | 121.1 | 127.0 | SET_USER_AREA |
| 9 | 373 | 86.32 | 102.2 | SELECT_CHARACTER |
| 10 | 386 | 107.2 | 116.2 | SELECT_CHARACTER |
| 11 | 380 | 75.19 | 77.64 | SELECT_DUNGEON |
| 12 | 375 | 62.77 | 68.03 | SELECT_CHARACTER |
| 13 | 376 | 106.3 | 112.6 | SELECT_CHARACTER |
| 14 | 380 | 91.29 | 106.1 | SELECT_CHARACTER |
| 15 | 378 | 111.6 | 112.7 | CHECK_USER_CONNECTION |
| 16 | 383 | 54.43 | 74.04 | SELECT_CHARACTER |
| 17 | 383 | 59.68 | 69.03 | CHECK_USER_CONNECTION |
| 18 | 386 | 103.2 | 111.9 | SELECT_CHARACTER |
| 19 | 384 | 104.8 | 106.2 | SET_USER_AREA |
| 20 | 380 | 95.96 | 105.4 | SELECT_CHARACTER |
| 21 | 394 | 28.44 | 29.93 | SET_USER_POSITION |
| 22 | 382 | 91.26 | 100.8 | CHECK_USER_CONNECTION |
| 23 | 384 | 67.15 | 68.21 | SELECT_CHARACTER |
| 24 | 384 | 103.7 | 112.3 | CHECK_USER_CONNECTION |
| 25 | 377 | 96.55 | 106.5 | SET_USER_AREA |
| 26 | 378 | 49.67 | 59.21 | SELECT_CHARACTER |
| 27 | 381 | 96.49 | 107.6 | SET_USER_AREA |
| 28 | 386 | 67.30 | 74.86 | CHECK_USER_CONNECTION |
| 29 | 390 | 41.81 | 45.11 | SET_USER_POSITION |
| 30 | 377 | 54.17 | 59.90 | SELECT_CHARACTER |
| 31 | 368 | 91.02 | 94.68 | ENTER_SELECT_DUNGEON |
| 32 | 372 | 94.30 | 104.0 | SELECT_CHARACTER |
| 33 | 372 | 63.08 | 70.12 | SELECT_CHARACTER |
| 34 | 377 | 94.02 | 103.6 | SELECT_CHARACTER |
| 35 | 386 | 28.64 | 35.13 | EPLP |
| 36 | 381 | 67.04 | 72.59 | SET_USER_AREA |
| 37 | 384 | 102.1 | 113.9 | SELECT_CHARACTER |
| 38 | 374 | 116.3 | 121.6 | SELECT_CHARACTER |
| 39 | 381 | 137.9 | 148.3 | EPLP |
| 40 | 384 | 61.15 | 67.80 | SET_USER_AREA |
| 41 | 382 | 54.86 | 55.04 | CHECK_USER_CONNECTION |
| 42 | 379 | 62.39 | 69.32 | SELECT_CHARACTER |
| 43 | 382 | 100.1 | 106.2 | SELECT_CHARACTER |
| 44 | 375 | 54.43 | 58.83 | SELECT_CHARACTER |
| 45 | 380 | 116.0 | 120.5 | SET_USER_AREA |
| 46 | 374 | 83.27 | 92.99 | CHECK_USER_CONNECTION |
| 47 | 377 | 91.90 | 102.8 | SELECT_CHARACTER |
| 48 | 381 | 156.6 | 162.8 | SELECT_CHARACTER |
| 49 | 389 | 56.68 | 61.45 | SELECT_CHARACTER |
| 50 | 376 | 107.3 | 109.9 | SELECT_CHARACTER |
| 51 | 388 | 62.43 | 72.26 | SET_USER_POSITION |
| 52 | 365 | 69.45 | 69.48 | CHECK_USER_CONNECTION |
| 53 | 370 | 36.40 | 41.40 | ENTER_SELECT_DUNGEON |
| 54 | 380 | 87.31 | 107.2 | SELECT_CHARACTER |
| 55 | 383 | 84.33 | 91.27 | CHECK_USER_CONNECTION |
| 56 | 383 | 108.0 | 121.5 | SELECT_CHARACTER |
| 57 | 376 | 64.48 | 65.69 | CHECK_USER_CONNECTION |
| 58 | 389 | 92.25 | 101.7 | SELECT_CHARACTER |
| 59 | 380 | 90.10 | 91.75 | SET_USER_AREA |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45813 | 1.22 | 62.90 | 103.9 | 143.3 | 171.2 |
| 10 并发登录的轮内 | 592* | 27.26 | 94.97 | 114.2 | 122.0 | 122.0 |
| （参考：实际发出口径）稳态窗 | 45813 | 0.24 | 56.04 | 100.2 | 141.8 | 171.2 |
| （参考：实际发出口径）轮内 | 592* | 15.45 | 80.21 | 111.8 | 121.0 | 121.0 |
| 登录阶段整体（182–232 s） | 19183 | 1.58 | 73.31 | 109.4 | 144.4 | 162.2 |

每轮时长（开始 → 10 个 LOGIN 都应答）：295, 202, 277, 256, 295, 217 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17627 | 1.47 | 62.79 | 104.2 | 143.0 | 171.2 | 101.7 |
| 单人 | 轮内 | 230* | 27.91 | 104.9 | 115.7 | 122.0 | 122.0 | 111.8 |
| 双人组领队 | 稳态窗 | 14075 | 0.96 | 63.00 | 103.8 | 140.6 | 166.2 | 99.25 |
| 双人组领队 | 轮内 | 182* | 27.09 | 90.29 | 112.8 | 114.1 | 114.1 | 111.6 |
| 双人组队员 | 稳态窗 | 14111 | 1.21 | 62.88 | 103.9 | 147.9 | 167.5 | 99.48 |
| 双人组队员 | 轮内 | 180* | 27.09 | 86.78 | 112.7 | 112.7 | 112.7 | 111.8 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 102.1 | 153.1 | 157.6 | 157.6 | 157.6 |
| 启动器登录（HTTP，含校验） | 60** | 102.9 | 192.7 | 192.9 | 192.9 | 192.9 |
| Game LOGIN 0x0001 | 60** | 0.46 | 5.98 | 114.8 | 114.8 | 114.8 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 44414 包/秒（1332448 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45813 | 1.22 | 62.90 | 103.9 | 143.3 | 171.2 |
| 其余连接，灌包期间 | 11466 | 1.38 | 74.08 | 110.7 | 138.5 | 151.1 |
| （参考：实际发出口径）其余连接，灌包期间 | 11466 | 0.21 | 65.27 | 104.5 | 138.3 | 148.9 |
| 灌包连接自身（含其窗口内排队） | 1332479 | 0.36 | 0.50 | 2.22 | 89.92 | 424.8 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 233.8 |
| SET_USER_AREA | 请求者 | 2658 | 6706 | town | 30.3 |
| EPLP | 同伴 | 78 | 6135 | leave dungeon | 100.7 |
| SET_USER_AREA | 同伴 | 6 | 5944 | town | 2.9 |
| EPLP | 请求者 | 78 | 5689 | leave dungeon | 226.1 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 3669 | return to selection | 161.7 |
| REQUEST_PEER | 同伴 | 82 | 2929 | party | 144.4 |
| REQUEST_PEER | 请求者 | 82 | 2661 | party | 144.4 |
| LEAVE_PARTY | 同伴 | 78 | 2137 | leave party | 147.6 |
| LEAVE_PARTY | 请求者 | 78 | 1962 | leave party | 147.6 |
| ENTER_SELECT_DUNGEON | 请求者 | 82 | 1658 | party dungeon entry | 96.2 |
| ENTER_SELECT_DUNGEON | 同伴 | 82 | 1658 | party dungeon entry | 96.2 |
| SELECT_DUNGEON | 请求者 | 82 | 1367 | party dungeon entry | 11.5 |
| SELECT_DUNGEON | 同伴 | 82 | 1367 | party dungeon entry | 11.5 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 233.8 |
| RESPONSE_PEER | 同伴 | 82 | 550 | party | 50.1 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| RESPONSE_PEER | 请求者 | 82 | 308 | party | 7.2 |
| FINISH_LOADING | 请求者 | 164 | 36 | party dungeon entry | 11.6 |
| FINISH_LOADING | 同伴 | 164 | 0 | party dungeon entry | 11.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 103.9 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 114.2 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 110.7 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 1020.2 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 484，p99.9 102.0 ms，max 102.0 ms（出现于 t=0.5 s；冷启动窗内 max 102.0 ms，之后 max 14.31 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 82 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：231 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。
- SET_USER_POSITION：108 个（其中截尾 0），最大 1020 ms，计划发出时刻 1.2, 1.2, 1.4, 1.4, 1.2, 1.3, 1.3, 1.4, 1.5, 1.4, 1.4, 1.4 … s
- CHECK_USER_CONNECTION：92 个（其中截尾 0），最大 978 ms，计划发出时刻 1.1, 1.1, 1.1, 1.2, 1.3, 1.2, 1.3, 1.4, 1.3, 1.4, 1.4, 1.5 … s
- SET_USER_AREA：15 个（其中截尾 0），最大 1020 ms，计划发出时刻 1.2, 1.3, 1.3, 1.6, 2.5, 2.0, 2.2, 2.3, 2.5, 2.7, 2.7, 2.8 … s
- SELECT_CHARACTER：11 个（其中截尾 0），最大 567 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 1.1, 1.1, 1.4, 1.4, 1.4, 1.7, 2.5 s
- RETURN_SELECT_CHARACTER：5 个（其中截尾 0），最大 596 ms，计划发出时刻 1.1, 1.5, 1.7, 2.6, 2.6 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1453460 行（Host 就绪之后 1453455 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 68.33 | 117.8 | 130.0 | 229.6 | 229.6 | 229.6 | 229.6 | 131.6 | 0.6 | 0 |
| inbound:GameSelectDungeon | 82** | 9.93 | 15.04 | 77.26 | 77.26 | 77.26 | 77.26 | 77.26 | 22.42 | 11.6 | 0 |
| inbound:GameEplpCommand | 78** | 45.37 | 65.29 | 77.21 | 77.21 | 77.21 | 40.92 | 58.08 | 77.21 | 255.0 | 0 |
| inbound:GameSetUserArea | 2658 | 23.24 | 40.69 | 52.52 | 64.15 | 75.64 | 10.69 | 57.80 | 75.64 | 202.2 | 0 |
| inbound:GameEnterSelectDungeon | 82** | 21.20 | 35.65 | 45.90 | 45.90 | 45.90 | 32.07 | 40.07 | 45.90 | 131.3 | 0 |
| inbound:GameCreateCharacter | 94** | 1.66 | 5.10 | 41.56 | 41.56 | 41.56 | 41.56 | 41.56 | 10.18 | 0.4 | 0 |
| inbound:GameSetUserPosition | 41887 | 0.01 | 0.05 | 0.12 | 1.84 | 18.88 | 1.80 | 4.22 | 18.88 | 98.7 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.11 | 0.45 | 14.28 | 14.28 | 14.28 | 14.28 | 14.28 | 0.46 | 0.2 | 0 |
| inbound:GameReturnToSelection | 463* | 0.46 | 1.76 | 3.18 | 11.09 | 11.09 | 11.09 | 11.09 | 6.34 | 1.4 | 0 |
| inbound:GameConnectionCheck | 1395238 | 0.00 | 0.00 | 0.01 | 0.04 | 10.67 | 0.11 | 3.41 | 10.67 | 174.4 | 0 |
| inbound:GameLogin | 81** | 0.10 | 0.91 | 9.45 | 9.45 | 9.45 | 9.45 | 9.45 | 5.59 | 0.3 | 0 |
| inbound:GameLeaveParty | 78** | 0.19 | 1.09 | 8.85 | 8.85 | 8.85 | 8.85 | 8.85 | 1.09 | 20.0 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.41 | 2.98 | 6.03 | 8.56 | 8.56 | 5.14 | 5.14 | 8.56 | 195.2 | 0 |
| inbound:GameResponsePeer | 82** | 0.18 | 1.22 | 8.43 | 8.43 | 8.43 | 8.43 | 8.43 | 2.26 | 7.2 | 0 |
| event:achievement.inventory | 940* | 0.26 | 0.84 | 2.94 | 7.61 | 7.61 | 7.06 | 7.06 | 7.61 | 148.1 | 0 |
| timer:CreatureClockService | 276* | 0.14 | 0.41 | 0.96 | 6.05 | 6.05 | 6.05 | 6.05 | 2.15 | 1.0 | 0 |
| event:quest.inventory | 940* | 0.10 | 0.32 | 0.79 | 4.76 | 4.76 | 4.76 | 4.76 | 1.30 | 0.4 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.86 | 4.68 | 4.68 | 4.68 | 4.68 | 4.68 | 4.68 | 1.59 | 60.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 4.29 | 4.29 | 4.29 | 4.29 | 4.29 | 0.07 | 0.1 | 0 |
| inbound:GameRequestPeer | 82** | 0.47 | 1.54 | 4.25 | 4.25 | 4.25 | 4.25 | 4.25 | 1.25 | 7.2 | 0 |
| inbound:GameFinishLoading | 164* | 0.10 | 0.19 | 1.96 | 3.27 | 3.27 | 3.27 | 3.27 | 0.19 | 11.6 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.04 | 0.22 | 2.86 | 2.86 | 2.86 | 2.86 | 2.86 | 0.37 | 0.3 | 0 |
| timer:DungeonPartyWipeService | 1105 | 0.01 | 0.03 | 0.04 | 1.03 | 2.60 | 2.60 | 2.60 | 1.03 | 0.2 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 4** | 0.33 | 2.49 | 2.49 | 2.49 | 2.49 | 2.49 | 2.49 | 0.80 | 47.4 | 0 |
| event:altar.departure | 164* | 0.12 | 0.22 | 0.82 | 1.87 | 1.87 | 1.87 | 1.87 | 0.82 | 15.8 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.72 | 1.84 | 1.84 | 1.84 | 1.78 | 1.78 | 1.84 | 274.0 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.03 | 0.11 | 0.18 | 1.75 | 1.75 | 1.75 | 1.75 | 0.20 | 1.0 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.02 | 1.57 | 1.57 | 1.02 | 1.57 | 0.02 | 3.5 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.06 | 1.56 | 1.56 | 1.56 | 1.56 | 1.56 | 1.56 | 0.21 | 60.0 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.38 | 1.53 | 1.53 | 1.53 | 1.53 | 1.49 | 1.49 | 1.53 | 180.0 | 0 |
| event:quest.mirror-leave | 164* | 0.12 | 0.28 | 0.77 | 1.49 | 1.49 | 0.77 | 0.77 | 1.49 | 215.1 | 0 |
| timer:JoustMaintenanceService | 276* | 0.07 | 0.22 | 0.42 | 1.27 | 1.27 | 0.12 | 0.38 | 1.27 | 66.0 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.61 | 1.08 | 1.08 | 1.08 | 1.08 | 1.08 | 1.08 | 0.75 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.31 | 1.03 | 1.03 | 1.03 | 1.03 | 1.03 | 1.03 | 0.35 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.80 | 0.80 | 0.80 | 0.80 | 0.02 | 1.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.21 | 0.67 | 0.67 | 0.67 | 0.67 | 0.67 | 0.67 | 0.51 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 0.65 | 0.65 | 0.65 | 0.65 | 0.65 | 0.65 | 0.30 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5463 | 0.00 | 0.01 | 0.01 | 0.03 | 0.57 | 0.57 | 0.57 | 0.08 | 0.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.35 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.35 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.08 | 0.49 | 0.49 | 0.49 | 0.49 | 0.49 | 0.49 | 0.26 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.06 | 0.49 | 0.49 | 0.49 | 0.49 | 0.49 | 0.49 | 0.21 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.43 | 0.43 | 0.43 | 0.43 | 0.43 | 0.43 | 0.15 | 60.0 | 0 |
| event:tournament.departure | 164* | 0.10 | 0.16 | 0.25 | 0.33 | 0.33 | 0.16 | 0.16 | 0.33 | 274.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.12 | 0.12 | 0.12 | 0.12 | 0.12 | 0.12 | 0.02 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 162* | 0.00 | 0.01 | 0.04 | 0.06 | 0.06 | 0.04 | 0.06 | 0.02 | 56.3 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 103.9 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26197 | 0.04 | 56.39 | 103.1 | 400.5 | 567.3 | lifecycle:connected | 1.0 |
| 冷启动窗 | 只算 inbound: | 23247 | 0.04 | 54.79 | 100.1 | 287.2 | 567.2 |  |  |
| 稳态窗 | 全部作业 | 52993 | 0.04 | 51.00 | 94.31 | 135.4 | 166.9 | inbound:GameConnectionCheck | 135.9 |
| 稳态窗 | 只算 inbound: | 48076 | 0.03 | 50.68 | 94.08 | 135.4 | 166.9 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1001 | 0.21 | 68.36 | 100.0 | 111.4 | 111.4 | inbound:GameLogin | 222.3 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 734* | 0.21 | 68.36 | 94.32 | 111.4 | 111.4 |  |  |
| (e) 灌包期间 | 全部作业 | 1345625 | 0.00 | 0.00 | 0.01 | 40.22 | 144.4 | inbound:GameConnectionCheck | 246.6 |
| (e) 灌包期间 | 只算 inbound: | 1344485 | 0.00 | 0.00 | 0.01 | 36.80 | 144.4 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 120 | 567.2 | 567.3 | lifecycle:connected | 229.6 |
| 1 | 159 | 446.8 | 446.8 | lifecycle:connected | 116.6 |
| 2 | 209 | 292.0 | 292.3 | inbound:GameSetUserPosition | 109.9 |
| 3 | 969 | 113.5 | 115.2 | timer:TournamentRewardSweepService | 66.73 |
| 4 | 453 | 108.9 | 113.2 | event:quest.inventory | 112.8 |
| 5 | 435 | 71.01 | 72.89 | inbound:GameSetUserPosition | 85.66 |
| 6 | 432 | 35.49 | 39.42 | inbound:GameConnectionCheck | 23.24 |
| 7 | 434 | 101.7 | 104.1 | inbound:GameConnectionCheck | 109.8 |
| 8 | 420 | 117.6 | 121.6 | inbound:GameSetUserPosition | 108.6 |
| 9 | 449 | 84.74 | 93.77 | timer:DungeonPartyWipeService | 102.1 |
| 10 | 433 | 106.0 | 106.1 | inbound:GameConnectionCheck | 116.0 |
| 11 | 432 | 74.84 | 75.65 | inbound:GameConnectionCheck | 77.26 |
| 12 | 463 | 57.35 | 67.39 | event:quest.inventory | 67.02 |
| 13 | 425 | 104.5 | 104.6 | inbound:GameSetUserPosition | 112.5 |
| 14 | 464 | 92.21 | 105.8 | event:quest.inventory | 105.2 |
| 15 | 413 | 110.6 | 110.7 | inbound:GameConnectionCheck | 111.0 |
| 16 | 453 | 62.71 | 71.32 | inbound:GameConnectionCheck | 73.89 |
| 17 | 447 | 56.39 | 65.99 | inbound:GameConnectionCheck | 65.38 |
| 18 | 430 | 101.4 | 111.3 | inbound:GameConnectionCheck | 111.7 |
| 19 | 443 | 94.70 | 105.7 | inbound:GameSetUserPosition | 66.25 |
| 20 | 446 | 92.36 | 92.53 | inbound:GameConnectionCheck | 105.2 |
| 21 | 435 | 44.00 | 44.13 | inbound:GameConnectionCheck | 56.68 |
| 22 | 434 | 80.86 | 80.90 | inbound:GameConnectionCheck | 69.07 |
| 23 | 452 | 66.79 | 67.94 | event:quest.inventory | 67.67 |
| 24 | 434 | 102.5 | 109.6 | inbound:GameConnectionCheck | 110.2 |
| 25 | 466 | 92.90 | 94.19 | inbound:GameSetUserPosition | 81.45 |
| 26 | 428 | 48.69 | 49.02 | inbound:GameSetUserPosition | 59.10 |
| 27 | 419 | 55.60 | 55.75 | inbound:GameSetUserPosition | 65.98 |
| 28 | 458 | 102.9 | 103.0 | timer:IllusionTowerReturnSweepService | 103.6 |
| 29 | 427 | 40.44 | 44.59 | inbound:GameSetUserPosition | 41.65 |
| 30 | 450 | 47.86 | 50.74 | inbound:GameConnectionCheck | 31.31 |
| 31 | 416 | 52.42 | 59.50 | event:quest.inventory | 59.14 |
| 32 | 492 | 94.09 | 103.9 | event:quest.inventory | 103.6 |
| 33 | 426 | 51.55 | 60.78 | inbound:GameConnectionCheck | 69.99 |
| 34 | 431 | 91.39 | 102.6 | inbound:GameConnectionCheck | 103.4 |
| 35 | 444 | 27.34 | 31.14 | inbound:GameConnectionCheck | 34.80 |
| 36 | 455 | 66.92 | 67.07 | inbound:GameConnectionCheck | 67.27 |
| 37 | 402 | 39.74 | 57.15 | inbound:GameConnectionCheck | 58.08 |
| 38 | 546 | 115.9 | 121.4 | event:achievement.inventory | 116.1 |
| 39 | 438 | 120.9 | 133.6 | inbound:GameConnectionCheck | 107.7 |
| 40 | 429 | 56.15 | 60.91 | inbound:GameConnectionCheck | 61.36 |
| 41 | 436 | 53.60 | 54.79 | inbound:GameConnectionCheck | 58.82 |
| 42 | 441 | 60.45 | 61.67 | inbound:GameSetUserPosition | 69.16 |
| 43 | 455 | 99.77 | 105.7 | event:quest.inventory | 106.0 |
| 44 | 433 | 52.30 | 56.16 | inbound:GameConnectionCheck | 58.70 |
| 45 | 449 | 101.7 | 119.1 | inbound:GameConnectionCheck | 100.9 |
| 46 | 429 | 82.94 | 92.57 | inbound:GameConnectionCheck | 73.09 |
| 47 | 405 | 80.60 | 100.3 | inbound:GameSetUserPosition | 102.6 |
| 48 | 474 | 155.3 | 157.6 | inbound:GameSetUserPosition | 106.4 |
| 49 | 453 | 55.09 | 61.23 | event:quest.inventory | 60.89 |
| 50 | 448 | 99.87 | 109.6 | event:quest.inventory | 109.4 |
| 51 | 426 | 61.82 | 71.38 | inbound:GameSetUserPosition | 50.96 |
| 52 | 462 | 67.93 | 67.98 | inbound:GameSetUserPosition | 59.46 |
| 53 | 419 | 36.14 | 36.43 | inbound:GameConnectionCheck | 40.07 |
| 54 | 392 | 42.62 | 66.14 | inbound:GameConnectionCheck | 66.76 |
| 55 | 496 | 88.96 | 106.9 | event:quest.inventory | 106.6 |
| 56 | 451 | 106.0 | 112.9 | inbound:GameConnectionCheck | 98.21 |
| 57 | 438 | 53.55 | 65.49 | inbound:GameConnectionCheck | 64.36 |
| 58 | 433 | 91.90 | 92.05 | inbound:GameConnectionCheck | 101.6 |
| 59 | 446 | 82.74 | 82.88 | inbound:GameSetUserPosition | 62.85 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26197 | 25139 | 41.9% | inbound:GameSetUserArea 12792 ms／630／max 57.80；inbound:GameSelectCharacter 10105 ms／132／max 229.6；inbound:GameEplpCommand 659 ms／17／max 58.08；inbound:GameEnterSelectDungeon 365 ms／18／max 40.07；inbound:GameSelectDungeon 273 ms／18／max 77.26；inbound:GameSetUserPosition 213 ms／8714／max 4.22；event:achievement.inventory 138 ms／390／max 7.06；inbound:GameCreateCharacter 120 ms／39／max 41.56 |
| 稳态窗 | 120.0 | 52993 | 46677 | 38.9% | inbound:GameSetUserArea 27265 ms／1145／max 62.28；inbound:GameSelectCharacter 15424 ms／197／max 131.6；inbound:GameEplpCommand 1505 ms／36／max 70.10；inbound:GameEnterSelectDungeon 788 ms／36／max 45.90；inbound:GameSetUserPosition 440 ms／18485／max 18.88；inbound:GameSelectDungeon 378 ms／36／max 22.42；inbound:GameConnectionCheck 170 ms／27728／max 10.67；event:southern-dale.daily-refill-notifications 157 ms／197／max 7.64 |
| (d) 10 并发登录的轮内 | 1.5 | 1001 | 866 | 56.2% | inbound:GameSetUserArea 637 ms／18／max 75.64；inbound:GameSelectCharacter 197 ms／2／max 120.7；inbound:GameLogin 13 ms／60／max 5.59；http:POST /api/launcher/v1/register 8 ms／60／max 0.46；inbound:GameSetUserPosition 4 ms／214／max 0.47；http:POST /api/launcher/v1/login 3 ms／60／max 0.37；inbound:GameConnectionCheck 1 ms／438／max 0.11；inbound:GameReturnToSelection 1 ms／2／max 0.85 |
| (e) 灌包期间 | 30.0 | 1345625 | 16939 | 56.5% | inbound:GameSetUserArea 7766 ms／273／max 58.49；inbound:GameSelectCharacter 4054 ms／50／max 121.8；inbound:GameConnectionCheck 4008 ms／1339283／max 5.91；inbound:GameEplpCommand 509 ms／8／max 77.21；inbound:GameEnterSelectDungeon 259 ms／9／max 35.30；inbound:GameSetUserPosition 124 ms／4751／max 5.88；inbound:GameSelectDungeon 86 ms／9／max 10.18；event:southern-dale.daily-refill-notifications 41 ms／50／max 7.85 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.6，`timer:` 31.7，`event:` 9.3。

