# 基线测量 — m1ad2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3956 ms

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
| CHECK_USER_CONNECTION | 25507 | 53.14% |
| SET_USER_POSITION | 18494 | 38.53% |
| STEP_FENCE | 2142 | 4.46% |
| SET_USER_AREA | 1143 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 70 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| EPLP | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25507 | 1.67 | 50.56 | 89.96 | 119.1 | 147.6 | 0 | 86.79 | 118.5 | 139.3 |
| SET_USER_POSITION | 18494 | 1.68 | 52.55 | 93.81 | 116.5 | 137.1 | 0 | 90.85 | 113.6 | 136.4 |
| SET_USER_AREA | 1143 | 22.82 | 65.62 | 104.3 | 136.9 | 154.9 | 0 | 95.05 | 136.1 | 154.7 |
| RETURN_SELECT_CHARACTER | 197* | 2.28 | 57.88 | 73.71 | 100.5 | 100.5 | 0 | 73.71 | 99.03 | 99.03 |
| SELECT_CHARACTER | 197* | 4.34 | 20.52 | 64.98 | 74.03 | 74.03 | 0 | 64.98 | 74.03 | 74.03 |
| FINISH_LOADING | 70** | 0.14 | 0.27 | 58.73 | 58.73 | 58.73 | 0 | 58.73 | 58.73 | 58.73 |
| LEAVE_PARTY | 37** | 0.37 | 55.99 | 56.06 | 56.06 | 56.06 | 0 | 56.06 | 56.06 | 56.06 |
| EPLP | 36** | 12.49 | 87.01 | 87.49 | 87.49 | 87.49 | 0 | 85.92 | 85.92 | 85.92 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.20 | 0.39 | 66.67 | 66.67 | 66.67 | 0 | 66.67 | 66.67 | 66.67 |
| CREATE_CHARACTER | 35** | 3.18 | 18.23 | 19.35 | 19.35 | 19.35 | 0 | 19.35 | 19.35 | 19.35 |
| ENTER_SELECT_DUNGEON | 35** | 28.88 | 111.7 | 147.9 | 147.9 | 147.9 | 0 | 131.3 | 131.3 | 131.3 |
| SELECT_DUNGEON | 35** | 10.29 | 115.4 | 116.9 | 116.9 | 116.9 | 0 | 116.9 | 116.9 | 116.9 |
| **全部计入的请求** | 45822 | 1.72 | 52.11 | 92.31 | 119.1 | 154.9 | 36 | 87.85 | 118.5 | 154.7 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45858 | 1.04 | 2.64 | 42.24 | 68.58 | 90.73（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.74 | 8.20 | 12.10 | 98.27 | 98.27 | 0.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 14.65 | 33.02 | 51.73 | 69.09 | 69.09 | 0.8 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.66 | 49.85 | 56.12 | 62.96 | 62.96 | 172.8 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.18 | 54.17 | 58.24 | 60.23 | 60.23 | 62.8 | 0 |
| EnterDungeonCommand | Transactional | 79** | 6.01 | 11.50 | 59.54 | 59.54 | 59.54 | 12.6 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 12.68 | 25.56 | 48.16 | 57.13 | 57.13 | 2.1 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.18 | 3.14 | 23.61 | 23.61 | 23.61 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 79** | 0.97 | 3.06 | 13.55 | 13.55 | 13.55 | 17.5 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.09 | 0.36 | 11.13 | 11.13 | 11.13 | 0.2 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.16 | 0.73 | 1.12 | 6.49 | 6.49 | 1.8 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.33 | 0.99 | 5.06 | 5.06 | 5.06 | 0.4 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.03 | 0.04 | 5.05 | 5.05 | 0.4 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.10 | 4.84 | 4.84 | 4.84 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.01 | 0.08 | 3.87 | 3.87 | 3.87 | 6.9 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.05 | 0.07 | 0.11 | 3.86 | 3.86 | 0.7 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.07 | 3.76 | 3.76 | 0.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.06 | 0.15 | 0.23 | 3.38 | 3.38 | 0.5 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.04 | 3.31 | 3.31 | 3.31 | 0.4 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.03 | 0.08 | 0.14 | 3.30 | 3.30 | 0.5 | 0 |
| AdvanceCreatureClockCommand | Transactional | 776* | 0.03 | 0.12 | 0.32 | 3.07 | 3.07 | 1.7 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.07 | 2.99 | 2.99 | 0.7 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 0.42 | 2.25 | 2.49 | 2.49 | 2.49 | 209.6 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.18 | 0.60 | 1.35 | 2.43 | 2.43 | 254.3 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.12 | 0.23 | 1.61 | 1.61 | 0.5 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.03 | 0.05 | 0.07 | 1.53 | 1.53 | 0.7 | 0 |
| LeavePartyCommand | Direct | 77** | 0.01 | 0.05 | 1.30 | 1.30 | 1.30 | 22.0 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 1.17 | 1.17 | 1.17 | 1.17 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.05 | 0.09 | 0.71 | 0.71 | 0.5 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.02 | 0.28 | 0.66 | 0.66 | 12.6 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.03 | 0.07 | 0.48 | 0.48 | 18.8 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.03 | 0.40 | 0.40 | 0.40 | 60.0 | 0 |

命令日志行 9787 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**1181.5 ms**（SELECT_CHARACTER，计划发出 t=0.47 s，pair01L；参考口径 1181.5 ms）。冷启动窗整体：21942 | 1.73 | 63.70 | 114.0 | 630.3 | 1181.5（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 44 | 1181.5 | 1181.5 | SELECT_CHARACTER |
| 1 | 52 | 366.8 | 366.8 | SELECT_CHARACTER |
| 2 | 171 | 648.4 | 667.0 | SET_USER_AREA |
| 3 | 389 | 194.2 | 224.1 | SET_USER_POSITION |
| 4 | 391 | 44.04 | 62.18 | SET_USER_POSITION |
| 5 | 378 | 97.14 | 130.2 | SET_USER_AREA |
| 6 | 389 | 94.77 | 113.2 | SET_USER_POSITION |
| 7 | 369 | 41.13 | 57.03 | CHECK_USER_CONNECTION |
| 8 | 367 | 96.77 | 105.1 | CHECK_USER_CONNECTION |
| 9 | 382 | 84.61 | 102.9 | CHECK_USER_CONNECTION |
| 10 | 387 | 79.21 | 97.34 | SET_USER_POSITION |
| 11 | 387 | 97.24 | 111.8 | CHECK_USER_CONNECTION |
| 12 | 364 | 88.17 | 90.05 | SET_USER_AREA |
| 13 | 372 | 99.38 | 103.4 | CHECK_USER_CONNECTION |
| 14 | 380 | 76.17 | 76.28 | CHECK_USER_CONNECTION |
| 15 | 386 | 60.04 | 60.14 | CHECK_USER_CONNECTION |
| 16 | 384 | 92.93 | 95.27 | CHECK_USER_CONNECTION |
| 17 | 381 | 59.17 | 73.67 | SET_USER_AREA |
| 18 | 380 | 86.45 | 104.9 | CHECK_USER_CONNECTION |
| 19 | 388 | 58.59 | 60.82 | CHECK_USER_CONNECTION |
| 20 | 382 | 97.78 | 112.7 | CHECK_USER_CONNECTION |
| 21 | 390 | 52.62 | 53.61 | SET_USER_POSITION |
| 22 | 384 | 40.46 | 43.13 | CHECK_USER_CONNECTION |
| 23 | 388 | 62.80 | 64.42 | CHECK_USER_CONNECTION |
| 24 | 379 | 102.3 | 111.4 | SET_USER_AREA |
| 25 | 381 | 66.61 | 83.33 | SET_USER_POSITION |
| 26 | 377 | 78.68 | 93.92 | SET_USER_POSITION |
| 27 | 388 | 38.45 | 42.87 | SET_USER_AREA |
| 28 | 373 | 93.58 | 103.5 | SET_USER_POSITION |
| 29 | 387 | 58.74 | 62.48 | SET_USER_AREA |
| 30 | 382 | 47.12 | 62.47 | SET_USER_POSITION |
| 31 | 367 | 57.64 | 72.01 | CHECK_USER_CONNECTION |
| 32 | 381 | 82.03 | 100.4 | CHECK_USER_CONNECTION |
| 33 | 369 | 36.77 | 55.20 | CHECK_USER_CONNECTION |
| 34 | 377 | 84.41 | 100.7 | SET_USER_POSITION |
| 35 | 374 | 40.54 | 66.73 | SET_USER_POSITION |
| 36 | 389 | 105.4 | 124.8 | CHECK_USER_CONNECTION |
| 37 | 385 | 73.40 | 81.46 | SET_USER_AREA |
| 38 | 382 | 103.7 | 103.9 | CHECK_USER_CONNECTION |
| 39 | 375 | 87.43 | 101.6 | SET_USER_POSITION |
| 40 | 383 | 54.28 | 66.00 | CHECK_USER_CONNECTION |
| 41 | 381 | 40.13 | 54.54 | SET_USER_POSITION |
| 42 | 382 | 95.79 | 110.9 | CHECK_USER_CONNECTION |
| 43 | 383 | 68.34 | 83.16 | CHECK_USER_CONNECTION |
| 44 | 378 | 63.27 | 81.63 | SET_USER_POSITION |
| 45 | 377 | 71.71 | 100.1 | SET_USER_POSITION |
| 46 | 378 | 87.37 | 102.5 | CHECK_USER_CONNECTION |
| 47 | 370 | 58.98 | 88.53 | CHECK_USER_CONNECTION |
| 48 | 388 | 160.5 | 166.9 | SET_USER_POSITION |
| 49 | 385 | 88.42 | 117.7 | SET_USER_AREA |
| 50 | 379 | 118.3 | 124.6 | CHECK_USER_CONNECTION |
| 51 | 376 | 58.72 | 73.26 | SET_USER_AREA |
| 52 | 359 | 67.53 | 80.58 | CHECK_USER_CONNECTION |
| 53 | 390 | 21.66 | 33.57 | CHECK_USER_CONNECTION |
| 54 | 386 | 81.67 | 99.98 | CHECK_USER_CONNECTION |
| 55 | 365 | 46.04 | 64.14 | CHECK_USER_CONNECTION |
| 56 | 383 | 125.0 | 133.8 | SET_USER_POSITION |
| 57 | 382 | 98.68 | 113.0 | CHECK_USER_CONNECTION |
| 58 | 385 | 90.74 | 90.91 | CHECK_USER_CONNECTION |
| 59 | 381 | 78.38 | 96.82 | SET_USER_POSITION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45822 | 1.72 | 52.11 | 92.31 | 119.1 | 154.9 |
| 10 并发登录的轮内 | 546* | 34.97 | 96.50 | 114.5 | 133.0 | 133.0 |
| （参考：实际发出口径）稳态窗 | 45822 | 0.27 | 46.61 | 87.85 | 118.5 | 154.7 |
| （参考：实际发出口径）轮内 | 546* | 21.77 | 88.78 | 94.03 | 113.3 | 113.3 |
| 登录阶段整体（182–232 s） | 19225 | 1.81 | 61.89 | 101.7 | 148.4 | 193.0 |

每轮时长（开始 → 10 个 LOGIN 都应答）：218, 213, 300, 221, 210, 292 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17628 | 1.74 | 54.45 | 92.10 | 119.1 | 154.9 | 87.61 |
| 单人 | 轮内 | 213* | 32.92 | 87.08 | 109.3 | 133.0 | 133.0 | 96.32 |
| 双人组领队 | 稳态窗 | 14079 | 1.91 | 50.51 | 92.90 | 115.4 | 147.9 | 87.85 |
| 双人组领队 | 轮内 | 167* | 35.25 | 96.61 | 114.6 | 114.6 | 114.6 | 94.03 |
| 双人组队员 | 稳态窗 | 14115 | 1.49 | 50.22 | 92.25 | 111.9 | 147.6 | 87.91 |
| 双人组队员 | 轮内 | 166* | 40.57 | 96.24 | 114.2 | 114.2 | 114.2 | 94.03 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 111.9 | 192.1 | 234.7 | 234.7 | 234.7 |
| 启动器登录（HTTP，含校验） | 60** | 87.85 | 126.0 | 142.8 | 142.8 | 142.8 |
| Game LOGIN 0x0001 | 60** | 0.20 | 0.54 | 0.66 | 0.66 | 0.66 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 51673 包/秒（1550980 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45822 | 1.72 | 52.11 | 92.31 | 119.1 | 154.9 |
| 其余连接，灌包期间 | 11463 | 1.58 | 53.88 | 89.42 | 103.3 | 118.3 |
| （参考：实际发出口径）其余连接，灌包期间 | 11463 | 0.21 | 48.11 | 88.05 | 102.8 | 118.3 |
| 灌包连接自身（含其窗口内排队） | 1551011 | 0.34 | 0.42 | 1.88 | 65.60 | 292.7 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.5 |
| EPLP | 同伴 | 79 | 6384 | leave dungeon | 159.9 |
| SET_USER_AREA | 请求者 | 2655 | 6361 | town | 51.5 |
| EPLP | 请求者 | 79 | 6168 | leave dungeon | 159.9 |
| SET_USER_AREA | 同伴 | 6 | 6083 | town | 2.7 |
| REQUEST_PEER | 同伴 | 83 | 3049 | party | 109.5 |
| LEAVE_PARTY | 同伴 | 77 | 2721 | leave party | 224.6 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 2679 | return to selection | 251.3 |
| REQUEST_PEER | 请求者 | 83 | 2613 | party | 109.5 |
| LEAVE_PARTY | 请求者 | 77 | 1786 | leave party | 224.6 |
| RESPONSE_PEER | 同伴 | 83 | 1635 | party | 231.2 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 1626 | party dungeon entry | 185.3 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 1626 | party dungeon entry | 185.3 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 12.5 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 12.5 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.5 |
| RESPONSE_PEER | 请求者 | 83 | 615 | party | 231.2 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 12.6 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 12.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 92.31 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 114.5 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 89.42 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 0 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 1181.5 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- 无。

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：152 个；截尾样本（超时或连接被关闭）0 个，协议无回包 83 个。
- SET_USER_POSITION：64 个（其中截尾 0），最大 648 ms，计划发出时刻 2.0, 2.0, 2.0, 2.0, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2 … s
- CHECK_USER_CONNECTION：62 个（其中截尾 0），最大 648 ms，计划发出时刻 2.0, 2.0, 2.0, 2.0, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2 … s
- SELECT_CHARACTER：15 个（其中截尾 0），最大 1181 ms，计划发出时刻 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 … s
- SET_USER_AREA：7 个（其中截尾 0），最大 667 ms，计划发出时刻 2.1, 2.1, 2.1, 2.3, 2.5, 2.7, 2.7 s
- RETURN_SELECT_CHARACTER：3 个（其中截尾 0），最大 596 ms，计划发出时刻 2.2, 2.3, 2.4 s
- CREATE_CHARACTER：1 个（其中截尾 0），最大 1045 ms，计划发出时刻 0.5 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1672170 行（Host 就绪之后 1672165 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 64.99 | 115.3 | 130.7 | 229.5 | 229.5 | 229.5 | 229.5 | 139.3 | 0.7 | 0 |
| inbound:GameSetUserArea | 2655 | 23.11 | 41.31 | 55.01 | 68.15 | 101.7 | 10.59 | 78.24 | 101.7 | 202.1 | 0 |
| inbound:GameEplpCommand | 79** | 45.85 | 79.20 | 88.55 | 88.55 | 88.55 | 48.01 | 48.01 | 88.55 | 240.3 | 0 |
| inbound:GameSelectDungeon | 79** | 10.02 | 15.56 | 77.57 | 77.57 | 77.57 | 77.57 | 77.57 | 16.52 | 12.6 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 23.75 | 41.42 | 46.57 | 46.57 | 46.57 | 34.58 | 46.57 | 44.97 | 52.5 | 0 |
| inbound:GameCreateCharacter | 94** | 1.78 | 4.89 | 30.81 | 30.81 | 30.81 | 30.81 | 30.81 | 5.87 | 0.4 | 0 |
| inbound:GameLogin | 81** | 0.11 | 0.30 | 18.38 | 18.38 | 18.38 | 18.38 | 18.38 | 0.27 | 0.4 | 0 |
| inbound:GameLeaveParty | 77** | 0.21 | 0.90 | 14.82 | 14.82 | 14.82 | 14.82 | 14.82 | 0.90 | 22.0 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.11 | 0.39 | 13.84 | 13.84 | 13.84 | 13.84 | 13.84 | 1.01 | 0.2 | 0 |
| inbound:GameResponsePeer | 83** | 0.23 | 0.48 | 11.68 | 11.68 | 11.68 | 11.68 | 11.68 | 0.48 | 6.9 | 0 |
| inbound:GameReturnToSelection | 463* | 0.45 | 1.68 | 2.67 | 10.30 | 10.30 | 10.30 | 10.30 | 3.19 | 1.9 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.17 | 10.06 | 10.06 | 10.06 | 10.06 | 10.06 | 6.41 | 0.2 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.38 | 1.81 | 2.95 | 7.67 | 7.67 | 5.22 | 5.22 | 7.67 | 199.5 | 0 |
| timer:CreatureClockService | 276* | 0.15 | 0.43 | 0.96 | 7.34 | 7.34 | 7.34 | 7.34 | 1.11 | 1.7 | 0 |
| inbound:GameRequestPeer | 83** | 0.52 | 2.84 | 7.28 | 7.28 | 7.28 | 7.28 | 7.28 | 3.18 | 6.9 | 0 |
| event:achievement.inventory | 940* | 0.24 | 0.58 | 1.99 | 7.12 | 7.12 | 7.12 | 7.12 | 2.99 | 0.5 | 0 |
| timer:DungeonPartyWipeService | 1104 | 0.01 | 0.03 | 0.05 | 1.83 | 7.01 | 7.01 | 7.01 | 1.83 | 0.2 | 0 |
| inbound:GameConnectionCheck | 1613864 | 0.00 | 0.00 | 0.01 | 0.02 | 6.89 | 0.12 | 0.59 | 6.89 | 258.8 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.52 | 6.74 | 6.74 | 6.74 | 6.74 | 6.74 | 6.74 | 0.74 | 60.0 | 0 |
| inbound:GameSetUserPosition | 41740 | 0.02 | 0.07 | 0.12 | 1.45 | 5.57 | 4.73 | 4.73 | 5.57 | 88.9 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.05 | 0.26 | 5.56 | 5.56 | 5.56 | 5.56 | 5.56 | 0.13 | 0.3 | 0 |
| event:quest.inventory | 940* | 0.09 | 0.27 | 0.69 | 5.13 | 5.13 | 5.13 | 5.13 | 1.51 | 0.5 | 0 |
| inbound:GameFinishLoading | 158* | 0.08 | 0.21 | 2.16 | 4.46 | 4.46 | 4.46 | 4.46 | 0.23 | 12.6 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.03 | 4.22 | 4.22 | 4.22 | 4.22 | 4.22 | 0.03 | 0.1 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.40 | 2.23 | 2.23 | 2.23 | 2.23 | 2.23 | 2.23 | 0.45 | 18.8 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.37 | 2.23 | 2.23 | 2.23 | 2.23 | 2.23 | 2.23 | 2.02 | 60.0 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.12 | 0.17 | 1.94 | 1.94 | 1.94 | 1.94 | 0.60 | 1.7 | 0 |
| event:altar.departure | 158* | 0.11 | 0.25 | 0.97 | 1.84 | 1.84 | 1.84 | 1.84 | 0.97 | 17.5 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.18 | 1.58 | 1.58 | 1.58 | 1.58 | 1.58 | 1.58 | 0.38 | 60.0 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.26 | 1.30 | 1.30 | 1.30 | 1.30 | 1.30 | 1.30 | 0.59 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.07 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 0.26 | 60.0 | 0 |
| event:quest.mirror-leave | 158* | 0.11 | 0.17 | 0.82 | 1.18 | 1.18 | 0.75 | 0.75 | 1.18 | 197.1 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.94 | 0.94 | 0.94 | 0.94 | 0.02 | 0.5 | 0 |
| timer:TournamentRewardSweepService | 5454 | 0.00 | 0.01 | 0.01 | 0.02 | 0.87 | 0.53 | 0.53 | 0.87 | 73.7 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.09 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.76 | 0.10 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.02 | 0.75 | 0.75 | 0.75 | 0.75 | 0.02 | 0.5 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.05 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.07 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.69 | 0.10 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.07 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.66 | 0.08 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.06 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.05 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.58 | 0.08 | 60.0 | 0 |
| timer:JoustMaintenanceService | 276* | 0.05 | 0.24 | 0.41 | 0.53 | 0.53 | 0.12 | 0.41 | 0.53 | 259.0 | 0 |
| event:tournament.departure | 158* | 0.09 | 0.14 | 0.17 | 0.19 | 0.19 | 0.19 | 0.19 | 0.17 | 17.5 | 0 |
| timer:NotificationRetryDrainService | 276* | 0.00 | 0.00 | 0.01 | 0.16 | 0.16 | 0.16 | 0.16 | 0.00 | 1.7 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.10 | 0.10 | 0.10 | 0.10 | 0.10 | 0.10 | 0.01 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 157* | 0.00 | 0.01 | 0.03 | 0.05 | 0.05 | 0.05 | 0.05 | 0.03 | 12.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 92.31 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26185 | 0.07 | 50.92 | 93.18 | 576.4 | 1245.8 | timer:TournamentRewardSweepService | 1.7 |
| 冷启动窗 | 只算 inbound: | 23190 | 0.07 | 49.78 | 90.99 | 513.3 | 1178.3 |  |  |
| 稳态窗 | 全部作业 | 53095 | 0.07 | 46.47 | 86.49 | 116.4 | 139.2 | inbound:GameConnectionCheck | 85.0 |
| 稳态窗 | 只算 inbound: | 48058 | 0.07 | 43.98 | 84.86 | 114.7 | 139.2 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1022 | 0.16 | 59.33 | 100.8 | 105.8 | 112.1 | timer:TournamentRewardSweepService | 212.0 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 727* | 0.19 | 59.33 | 78.72 | 101.7 | 101.7 |  |  |
| (e) 灌包期间 | 全部作业 | 1564196 | 0.00 | 0.00 | 0.01 | 29.71 | 127.9 | timer:TournamentRewardSweepService | 268.9 |
| (e) 灌包期间 | 只算 inbound: | 1563021 | 0.00 | 0.00 | 0.01 | 27.16 | 118.2 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 126 | 354.2 | 434.5 | inbound:GameSelectCharacter | 229.5 |
| 1 | 168 | 1244.8 | 1245.8 | timer:TournamentRewardSweepService | 101.3 |
| 2 | 151 | 550.2 | 550.2 | event:quest.inventory | 116.9 |
| 3 | 939 | 77.07 | 128.3 | inbound:GameConnectionCheck | 106.5 |
| 4 | 425 | 34.54 | 60.76 | event:quest.inventory | 60.43 |
| 5 | 468 | 95.41 | 114.2 | inbound:GameSetUserPosition | 107.3 |
| 6 | 429 | 93.17 | 112.1 | inbound:GameSetUserPosition | 126.2 |
| 7 | 440 | 40.69 | 56.65 | inbound:GameConnectionCheck | 59.86 |
| 8 | 435 | 75.86 | 104.1 | inbound:GameConnectionCheck | 108.1 |
| 9 | 432 | 82.74 | 97.67 | inbound:GameConnectionCheck | 107.8 |
| 10 | 430 | 76.20 | 88.15 | timer:DungeonPartyWipeService | 111.6 |
| 11 | 440 | 75.66 | 111.7 | inbound:GameConnectionCheck | 114.3 |
| 12 | 473 | 86.21 | 86.33 | inbound:GameConnectionCheck | 77.57 |
| 13 | 426 | 98.16 | 105.0 | timer:TournamentRewardSweepService | 115.3 |
| 14 | 453 | 74.55 | 85.55 | event:quest.inventory | 85.10 |
| 15 | 428 | 52.87 | 64.24 | timer:TournamentRewardSweepService | 76.65 |
| 16 | 437 | 90.86 | 97.58 | timer:TournamentRewardSweepService | 111.0 |
| 17 | 448 | 53.23 | 63.96 | inbound:GameSetUserArea | 68.15 |
| 18 | 449 | 86.22 | 103.1 | inbound:GameConnectionCheck | 119.4 |
| 19 | 437 | 44.39 | 60.20 | inbound:GameConnectionCheck | 72.34 |
| 20 | 443 | 76.71 | 103.3 | inbound:GameConnectionCheck | 117.1 |
| 21 | 440 | 52.49 | 55.82 | timer:TournamentRewardSweepService | 62.30 |
| 22 | 429 | 35.70 | 42.50 | inbound:GameSetUserPosition | 42.49 |
| 23 | 435 | 47.18 | 64.19 | inbound:GameConnectionCheck | 67.54 |
| 24 | 474 | 106.3 | 113.0 | event:quest.inventory | 112.7 |
| 25 | 455 | 63.99 | 76.58 | inbound:GameSetUserPosition | 68.05 |
| 26 | 435 | 66.70 | 93.03 | inbound:GameSetUserPosition | 73.14 |
| 27 | 435 | 25.87 | 38.98 | inbound:GameSetUserPosition | 42.99 |
| 28 | 423 | 91.87 | 98.59 | timer:DungeonPartyWipeService | 117.1 |
| 29 | 442 | 50.58 | 55.82 | timer:TournamentRewardSweepService | 60.41 |
| 30 | 426 | 40.85 | 45.82 | inbound:GameSetUserPosition | 38.49 |
| 31 | 464 | 63.02 | 64.21 | inbound:GameConnectionCheck | 62.86 |
| 32 | 453 | 81.54 | 97.33 | inbound:GameConnectionCheck | 102.8 |
| 33 | 436 | 29.31 | 54.13 | inbound:GameConnectionCheck | 68.58 |
| 34 | 410 | 84.29 | 99.05 | inbound:GameSetUserPosition | 114.2 |
| 35 | 463 | 61.30 | 72.34 | timer:NotificationRetryDrainService | 76.90 |
| 36 | 437 | 103.7 | 124.7 | inbound:GameConnectionCheck | 135.5 |
| 37 | 417 | 72.73 | 83.92 | event:quest.inventory | 83.28 |
| 38 | 507 | 98.56 | 114.9 | event:quest.inventory | 114.5 |
| 39 | 500 | 87.31 | 101.5 | inbound:GameSetUserPosition | 104.8 |
| 40 | 415 | 55.08 | 65.31 | inbound:GameConnectionCheck | 68.07 |
| 41 | 452 | 37.30 | 54.28 | inbound:GameSetUserPosition | 34.64 |
| 42 | 433 | 89.83 | 110.8 | inbound:GameConnectionCheck | 113.5 |
| 43 | 409 | 42.66 | 58.56 | event:achievement.inventory | 62.46 |
| 44 | 477 | 75.43 | 80.06 | event:southern-dale.daily-refill-notifications | 62.63 |
| 45 | 434 | 64.74 | 97.42 | inbound:GameSetUserPosition | 103.1 |
| 46 | 459 | 84.87 | 102.2 | inbound:GameConnectionCheck | 106.7 |
| 47 | 439 | 57.57 | 88.09 | inbound:GameSetUserPosition | 72.49 |
| 48 | 383 | 74.44 | 78.25 | inbound:GameConnectionCheck | 102.4 |
| 49 | 448 | 153.1 | 160.4 | inbound:GameSetUserArea | 61.12 |
| 50 | 490 | 105.6 | 124.1 | inbound:GameConnectionCheck | 126.1 |
| 51 | 454 | 56.13 | 62.87 | event:quest.inventory | 62.58 |
| 52 | 406 | 37.07 | 63.71 | inbound:GameConnectionCheck | 77.32 |
| 53 | 464 | 67.79 | 79.97 | inbound:GameConnectionCheck | 71.89 |
| 54 | 430 | 83.62 | 91.17 | timer:DungeonPartyWipeService | 113.4 |
| 55 | 461 | 59.85 | 63.07 | event:quest.inventory | 62.78 |
| 56 | 459 | 111.4 | 123.4 | inbound:GameConnectionCheck | 122.9 |
| 57 | 429 | 83.62 | 112.3 | inbound:GameConnectionCheck | 107.3 |
| 58 | 433 | 88.52 | 94.93 | timer:TournamentRewardSweepService | 103.1 |
| 59 | 452 | 77.69 | 96.53 | inbound:GameSetUserPosition | 100.3 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26185 | 24990 | 41.6% | inbound:GameSetUserArea 12411 ms／626／max 78.24；inbound:GameSelectCharacter 10479 ms／132／max 229.5；inbound:GameEplpCommand 512 ms／16／max 48.01；inbound:GameEnterSelectDungeon 370 ms／18／max 46.57；inbound:GameSelectDungeon 267 ms／18／max 77.57；inbound:GameSetUserPosition 231 ms／8637／max 4.73；event:achievement.inventory 126 ms／390／max 7.12；inbound:GameCreateCharacter 122 ms／39／max 30.81 |
| 稳态窗 | 120.0 | 53095 | 46397 | 38.7% | inbound:GameSetUserArea 27704 ms／1143／max 66.46；inbound:GameSelectCharacter 14610 ms／197／max 132.4；inbound:GameEplpCommand 1711 ms／36／max 81.33；inbound:GameEnterSelectDungeon 802 ms／35／max 37.55；inbound:GameSetUserPosition 510 ms／18495／max 5.57；inbound:GameSelectDungeon 374 ms／35／max 16.52；inbound:GameReturnToSelection 118 ms／197／max 2.67；event:southern-dale.daily-refill-notifications 108 ms／197／max 5.64 |
| (d) 10 并发登录的轮内 | 1.5 | 1022 | 722 | 49.6% | inbound:GameSetUserArea 545 ms／15／max 101.7；inbound:GameSelectCharacter 96 ms／1／max 95.63；inbound:GameEplpCommand 47 ms／1／max 46.53；http:POST /api/launcher/v1/register 9 ms／60／max 1.01；inbound:GameLogin 7 ms／60／max 0.27；inbound:GameSetUserPosition 6 ms／284／max 2.13；http:POST /api/launcher/v1/login 3 ms／60／max 0.13；event:southern-dale.daily-refill-notifications 3 ms／2／max 2.09 |
| (e) 灌包期间 | 30.0 | 1564196 | 15533 | 51.7% | inbound:GameSetUserArea 7374 ms／278／max 50.20；inbound:GameSelectCharacter 3716 ms／51／max 112.1；inbound:GameConnectionCheck 3487 ms／1557854／max 6.89；inbound:GameEplpCommand 421 ms／8／max 70.67；inbound:GameEnterSelectDungeon 215 ms／8／max 34.99；inbound:GameSetUserPosition 124 ms／4710／max 2.54；inbound:GameSelectDungeon 74 ms／8／max 10.03；inbound:GameReturnToSelection 35 ms／51／max 2.66 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 32.7，`event:` 9.3。

