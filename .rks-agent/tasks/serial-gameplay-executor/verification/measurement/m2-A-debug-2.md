# 基线测量 — m2ad2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3931 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：399.9 包/秒（47990 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25546 | 53.23% |
| SET_USER_POSITION | 18442 | 38.43% |
| STEP_FENCE | 2144 | 4.47% |
| SET_USER_AREA | 1143 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 36 | 0.08% |
| REQUEST_PEER | 36 | 0.08% |
| RESPONSE_PEER | 36 | 0.08% |
| ENTER_SELECT_DUNGEON | 36 | 0.08% |
| SELECT_DUNGEON | 36 | 0.08% |
| CREATE_CHARACTER | 35 | 0.07% |
| EPLP | 34 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25556 | 1.45 | 64.05 | 118.0 | 177.9 | 234.7 | 0 | 104.8 | 159.6 | 232.9 |
| SET_USER_POSITION | 18444 | 1.49 | 60.76 | 105.7 | 160.6 | 234.0 | 0 | 99.96 | 152.6 | 232.8 |
| SET_USER_AREA | 1143 | 28.73 | 79.04 | 127.0 | 176.5 | 182.9 | 0 | 118.0 | 149.3 | 149.9 |
| RETURN_SELECT_CHARACTER | 197* | 1.95 | 54.64 | 94.40 | 97.28 | 97.28 | 0 | 92.67 | 96.36 | 96.36 |
| SELECT_CHARACTER | 197* | 72.06 | 140.3 | 187.6 | 240.3 | 240.3 | 0 | 187.6 | 240.3 | 240.3 |
| FINISH_LOADING | 72** | 0.17 | 0.33 | 19.10 | 19.10 | 19.10 | 0 | 19.10 | 19.10 | 19.10 |
| LEAVE_PARTY | 36** | 0.60 | 56.43 | 99.73 | 99.73 | 99.73 | 0 | 99.73 | 99.73 | 99.73 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.27 | 0.54 | 9.45 | 9.45 | 9.45 | 0 | 9.45 | 9.45 | 9.45 |
| ENTER_SELECT_DUNGEON | 36** | 27.04 | 123.3 | 218.2 | 218.2 | 218.2 | 0 | 121.9 | 121.9 | 121.9 |
| SELECT_DUNGEON | 36** | 10.55 | 16.33 | 71.46 | 71.46 | 71.46 | 0 | 71.46 | 71.46 | 71.46 |
| CREATE_CHARACTER | 35** | 3.16 | 64.43 | 112.5 | 112.5 | 112.5 | 0 | 112.5 | 112.5 | 112.5 |
| EPLP | 34** | 58.13 | 139.2 | 165.8 | 165.8 | 165.8 | 0 | 137.2 | 137.2 | 137.2 |
| **全部计入的请求** | 45822 | 1.52 | 65.14 | 114.5 | 177.9 | 240.3 | 36 | 105.5 | 159.4 | 240.3 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45858 | 0.78 | 11.61 | 62.01 | 126.4 | 185.4（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.52 | 8.95 | 12.63 | 102.8 | 102.8 | 0.6 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.95 | 34.52 | 60.31 | 80.86 | 80.86 | 154.7 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 15.20 | 57.55 | 61.29 | 71.26 | 71.26 | 232.1 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 17.49 | 55.60 | 61.74 | 66.37 | 66.37 | 241.9 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 13.03 | 33.68 | 51.75 | 62.81 | 62.81 | 21.0 | 0 |
| EnterDungeonCommand | Transactional | 79** | 6.12 | 10.42 | 58.40 | 58.40 | 58.40 | 12.7 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.21 | 2.67 | 23.24 | 23.24 | 23.24 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 78** | 0.95 | 2.21 | 18.72 | 18.72 | 18.72 | 17.6 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.16 | 0.77 | 1.06 | 7.12 | 7.12 | 1.9 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.09 | 0.39 | 6.75 | 6.75 | 6.75 | 0.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.30 | 0.89 | 5.75 | 5.75 | 5.75 | 0.4 | 0 |
| AdvanceCreatureClockCommand | Transactional | 776* | 0.03 | 0.12 | 0.30 | 5.48 | 5.48 | 61.0 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.16 | 4.98 | 4.98 | 4.98 | 0.3 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.18 | 0.61 | 1.70 | 4.94 | 4.94 | 227.5 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.04 | 0.07 | 0.09 | 3.94 | 3.94 | 0.7 | 0 |
| RequestPartyInviteCommand | Direct | 82** | 0.40 | 2.40 | 3.79 | 3.79 | 3.79 | 189.9 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.06 | 3.71 | 3.71 | 0.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.06 | 0.16 | 0.23 | 3.36 | 3.36 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.03 | 0.09 | 0.13 | 3.26 | 3.26 | 0.5 | 0 |
| AcceptPartyInviteCommand | Direct | 82** | 0.01 | 0.07 | 3.06 | 3.06 | 3.06 | 7.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.07 | 3.01 | 3.01 | 0.7 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.04 | 0.05 | 0.07 | 2.55 | 2.55 | 65.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.03 | 1.70 | 1.70 | 1.70 | 0.4 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.13 | 0.24 | 1.59 | 1.59 | 0.5 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.01 | 0.02 | 1.53 | 1.53 | 0.4 | 0 |
| LeavePartyCommand | Direct | 78** | 0.01 | 0.03 | 1.16 | 1.16 | 1.16 | 22.0 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 0.83 | 0.83 | 0.83 | 0.83 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.06 | 0.11 | 0.68 | 0.68 | 0.5 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 158* | 0.01 | 0.02 | 0.30 | 0.62 | 0.62 | 12.7 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.02 | 0.08 | 0.40 | 0.40 | 18.8 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.03 | 0.33 | 0.33 | 0.33 | 60.0 | 0 |

命令日志行 9785 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**1367.6 ms**（SELECT_CHARACTER，计划发出 t=0.46 s，pair02L；参考口径 1367.6 ms）。冷启动窗整体：21916 | 1.52 | 77.89 | 167.3 | 654.2 | 1367.6（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 45 | 1367.6 | 1367.6 | SELECT_CHARACTER |
| 1 | 23 | 107.5 | 107.5 | SELECT_CHARACTER |
| 2 | 183 | 702.3 | 703.3 | CHECK_USER_CONNECTION |
| 3 | 378 | 333.8 | 334.9 | CHECK_USER_CONNECTION |
| 4 | 392 | 43.54 | 63.86 | SELECT_CHARACTER |
| 5 | 380 | 98.99 | 108.9 | SET_USER_POSITION |
| 6 | 388 | 88.70 | 110.7 | SELECT_CHARACTER |
| 7 | 371 | 52.39 | 74.10 | SELECT_CHARACTER |
| 8 | 367 | 102.6 | 117.3 | SET_USER_AREA |
| 9 | 380 | 112.6 | 118.0 | SELECT_CHARACTER |
| 10 | 385 | 103.6 | 109.0 | SELECT_CHARACTER |
| 11 | 389 | 113.3 | 113.4 | SET_USER_POSITION |
| 12 | 364 | 112.7 | 128.7 | SELECT_CHARACTER |
| 13 | 375 | 73.22 | 94.69 | SELECT_CHARACTER |
| 14 | 382 | 76.25 | 77.16 | SELECT_CHARACTER |
| 15 | 379 | 78.49 | 79.98 | SELECT_CHARACTER |
| 16 | 384 | 180.8 | 199.4 | SELECT_CHARACTER |
| 17 | 381 | 63.39 | 74.95 | SELECT_CHARACTER |
| 18 | 384 | 88.53 | 111.1 | SELECT_CHARACTER |
| 19 | 383 | 92.30 | 114.2 | SELECT_CHARACTER |
| 20 | 386 | 102.3 | 122.7 | SELECT_CHARACTER |
| 21 | 387 | 92.65 | 92.73 | SET_USER_POSITION |
| 22 | 386 | 44.20 | 53.23 | SET_USER_AREA |
| 23 | 388 | 92.01 | 113.1 | SELECT_CHARACTER |
| 24 | 377 | 80.46 | 92.99 | SELECT_CHARACTER |
| 25 | 382 | 74.31 | 74.42 | CHECK_USER_CONNECTION |
| 26 | 382 | 49.76 | 71.24 | SELECT_CHARACTER |
| 27 | 383 | 44.36 | 50.16 | SET_USER_AREA |
| 28 | 377 | 105.6 | 121.3 | SET_USER_POSITION |
| 29 | 379 | 111.9 | 126.2 | SET_USER_POSITION |
| 30 | 386 | 57.19 | 69.59 | SET_USER_AREA |
| 31 | 378 | 61.97 | 74.35 | SELECT_CHARACTER |
| 32 | 370 | 103.9 | 119.8 | SELECT_CHARACTER |
| 33 | 377 | 93.77 | 116.3 | SELECT_CHARACTER |
| 34 | 369 | 105.3 | 122.6 | SELECT_CHARACTER |
| 35 | 374 | 75.41 | 76.18 | SELECT_CHARACTER |
| 36 | 388 | 67.70 | 89.61 | SELECT_CHARACTER |
| 37 | 387 | 67.36 | 75.71 | SELECT_CHARACTER |
| 38 | 384 | 94.63 | 114.1 | SELECT_CHARACTER |
| 39 | 372 | 170.6 | 171.7 | SELECT_CHARACTER |
| 40 | 381 | 102.8 | 107.2 | SELECT_CHARACTER |
| 41 | 382 | 42.44 | 57.66 | SET_USER_AREA |
| 42 | 386 | 112.5 | 113.6 | CHECK_USER_CONNECTION |
| 43 | 384 | 95.28 | 112.9 | SELECT_CHARACTER |
| 44 | 371 | 115.3 | 165.1 | CHECK_USER_CONNECTION |
| 45 | 377 | 46.73 | 63.50 | SELECT_CHARACTER |
| 46 | 378 | 109.3 | 114.9 | CHECK_USER_CONNECTION |
| 47 | 376 | 106.6 | 113.2 | RESPONSE_PEER |
| 48 | 389 | 92.96 | 103.4 | SELECT_CHARACTER |
| 49 | 377 | 189.6 | 189.8 | SET_USER_POSITION |
| 50 | 376 | 89.32 | 107.8 | SELECT_CHARACTER |
| 51 | 375 | 60.48 | 62.68 | SELECT_CHARACTER |
| 52 | 375 | 56.24 | 72.28 | SELECT_CHARACTER |
| 53 | 381 | 58.97 | 66.00 | ENTER_SELECT_DUNGEON |
| 54 | 381 | 83.36 | 107.0 | SELECT_CHARACTER |
| 55 | 374 | 111.9 | 117.5 | SELECT_CHARACTER |
| 56 | 373 | 85.08 | 85.35 | CHECK_USER_CONNECTION |
| 57 | 392 | 76.11 | 76.82 | CHECK_USER_CONNECTION |
| 58 | 382 | 122.3 | 140.0 | SELECT_CHARACTER |
| 59 | 381 | 70.95 | 90.68 | SELECT_CHARACTER |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45822 | 1.52 | 65.14 | 114.5 | 177.9 | 240.3 |
| 10 并发登录的轮内 | 527* | 37.90 | 160.5 | 212.2 | 218.1 | 218.1 |
| （参考：实际发出口径）稳态窗 | 45822 | 0.27 | 55.61 | 105.5 | 159.4 | 240.3 |
| （参考：实际发出口径）轮内 | 527* | 21.62 | 75.35 | 90.26 | 100.4 | 100.4 |
| 登录阶段整体（182–232 s） | 19224 | 1.67 | 70.50 | 113.2 | 194.1 | 240.3 |

每轮时长（开始 → 10 个 LOGIN 都应答）：200, 205, 297, 212, 192, 297 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17630 | 1.49 | 62.56 | 111.6 | 176.8 | 240.3 | 106.1 |
| 单人 | 轮内 | 203* | 49.67 | 166.5 | 217.0 | 218.1 | 218.1 | 95.96 |
| 双人组领队 | 稳态窗 | 14078 | 1.46 | 69.06 | 120.1 | 183.0 | 234.7 | 105.7 |
| 双人组领队 | 轮内 | 166* | 32.00 | 145.3 | 194.2 | 194.7 | 194.7 | 80.21 |
| 双人组队员 | 稳态窗 | 14114 | 1.57 | 65.88 | 114.4 | 176.3 | 227.8 | 102.7 |
| 双人组队员 | 轮内 | 158* | 32.05 | 162.5 | 212.2 | 212.3 | 212.3 | 76.05 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 111.6 | 173.6 | 174.5 | 174.5 | 174.5 |
| 启动器登录（HTTP，含校验） | 60** | 85.48 | 121.6 | 148.6 | 148.6 | 148.6 |
| Game LOGIN 0x0001 | 60** | 0.21 | 21.52 | 84.60 | 84.60 | 84.60 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 46384 包/秒（1391527 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45822 | 1.52 | 65.14 | 114.5 | 177.9 | 240.3 |
| 其余连接，灌包期间 | 11476 | 1.48 | 66.17 | 107.1 | 159.4 | 188.6 |
| （参考：实际发出口径）其余连接，灌包期间 | 11476 | 0.23 | 61.26 | 102.3 | 157.5 | 188.6 |
| 灌包连接自身（含其窗口内排队） | 1391558 | 0.35 | 0.52 | 2.47 | 75.54 | 386.2 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.7 |
| SET_USER_AREA | 请求者 | 2655 | 6919 | town | 129.0 |
| SET_USER_AREA | 同伴 | 6 | 6454 | town | 2.7 |
| EPLP | 同伴 | 78 | 6388 | leave dungeon | 39.4 |
| EPLP | 请求者 | 78 | 5545 | leave dungeon | 60.7 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 2983 | return to selection | 259.5 |
| REQUEST_PEER | 同伴 | 82 | 2400 | party | 190.1 |
| LEAVE_PARTY | 同伴 | 78 | 2351 | leave party | 44.6 |
| LEAVE_PARTY | 请求者 | 78 | 2221 | leave party | 44.6 |
| REQUEST_PEER | 请求者 | 82 | 2188 | party | 190.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 79 | 1511 | party dungeon entry | 91.8 |
| ENTER_SELECT_DUNGEON | 同伴 | 79 | 1511 | party dungeon entry | 91.8 |
| SELECT_DUNGEON | 请求者 | 79 | 1367 | party dungeon entry | 12.6 |
| SELECT_DUNGEON | 同伴 | 79 | 1367 | party dungeon entry | 12.6 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.7 |
| RESPONSE_PEER | 同伴 | 82 | 1309 | party | 47.7 |
| RESPONSE_PEER | 请求者 | 82 | 428 | party | 50.5 |
| LOGIN | 请求者 | 21 | 339 | login | 0.4 |
| FINISH_LOADING | 请求者 | 158 | 36 | party dungeon entry | 12.7 |
| FINISH_LOADING | 同伴 | 158 | 0 | party dungeon entry | 12.7 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 114.5 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 212.2 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 107.1 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 1367.6 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 484，p99.9 102.8 ms，max 102.8 ms（出现于 t=0.6 s；冷启动窗内 max 102.8 ms，之后 max 14.33 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 79 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：163 个；截尾样本（超时或连接被关闭）0 个，协议无回包 82 个。
- CHECK_USER_CONNECTION：67 个（其中截尾 0），最大 703 ms，计划发出时刻 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2, 2.2, 2.2, 2.2, 2.2, 2.3 … s
- SET_USER_POSITION：67 个（其中截尾 0），最大 702 ms，计划发出时刻 2.1, 2.2, 2.2, 2.2, 2.2, 2.2, 2.2, 2.2, 2.3, 2.3, 2.3, 2.3 … s
- SELECT_CHARACTER：18 个（其中截尾 0），最大 1368 ms，计划发出时刻 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5 … s
- SET_USER_AREA：8 个（其中截尾 0），最大 686 ms，计划发出时刻 2.1, 2.2, 2.3, 2.3, 2.5, 2.8, 2.9, 3.1 s
- RETURN_SELECT_CHARACTER：3 个（其中截尾 0），最大 620 ms，计划发出时刻 2.3, 2.4, 2.5 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1512394 行（Host 就绪之后 1512389 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 69.37 | 121.5 | 142.6 | 239.1 | 239.1 | 227.7 | 227.7 | 239.1 | 154.8 | 0 |
| inbound:GameSetUserArea | 2655 | 22.56 | 40.74 | 52.97 | 76.37 | 117.2 | 10.04 | 58.60 | 117.2 | 154.4 | 0 |
| inbound:GameEplpCommand | 78** | 40.77 | 69.51 | 96.86 | 96.86 | 96.86 | 50.58 | 50.58 | 96.86 | 264.9 | 0 |
| inbound:GameSelectDungeon | 79** | 10.23 | 15.18 | 73.22 | 73.22 | 73.22 | 73.22 | 73.22 | 22.70 | 12.7 | 0 |
| inbound:GameEnterSelectDungeon | 79** | 20.28 | 39.86 | 56.15 | 56.15 | 56.15 | 28.42 | 29.14 | 56.15 | 153.3 | 0 |
| inbound:GameCreateCharacter | 94** | 1.79 | 3.84 | 31.09 | 31.09 | 31.09 | 31.09 | 31.09 | 4.38 | 0.4 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.42 | 5.20 | 6.21 | 18.49 | 18.49 | 5.26 | 18.49 | 11.24 | 1.9 | 0 |
| inbound:GameSetUserPosition | 41738 | 0.01 | 0.05 | 0.11 | 2.58 | 16.67 | 1.80 | 16.67 | 15.61 | 48.2 | 0 |
| inbound:GameReturnToSelection | 463* | 0.44 | 2.01 | 3.24 | 12.02 | 12.02 | 12.02 | 12.02 | 5.84 | 1.9 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.09 | 0.45 | 11.99 | 11.99 | 11.99 | 11.99 | 11.99 | 0.48 | 0.2 | 0 |
| inbound:GameLeaveParty | 78** | 0.18 | 0.94 | 10.06 | 10.06 | 10.06 | 10.06 | 10.06 | 1.23 | 22.1 | 0 |
| inbound:GameLogin | 81** | 0.10 | 0.29 | 9.67 | 9.67 | 9.67 | 9.67 | 9.67 | 0.29 | 0.4 | 0 |
| inbound:GameResponsePeer | 82** | 0.16 | 0.62 | 9.28 | 9.28 | 9.28 | 9.28 | 9.28 | 1.39 | 7.0 | 0 |
| event:achievement.inventory | 940* | 0.25 | 0.71 | 4.84 | 7.35 | 7.35 | 7.35 | 7.35 | 5.97 | 0.5 | 0 |
| inbound:GameConnectionCheck | 1454387 | 0.00 | 0.00 | 0.01 | 0.04 | 6.88 | 0.13 | 1.42 | 6.88 | 134.1 | 0 |
| timer:CreatureClockService | 276* | 0.15 | 0.55 | 2.75 | 6.46 | 6.46 | 6.46 | 6.46 | 5.63 | 1.8 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.05 | 0.23 | 5.69 | 5.69 | 5.69 | 5.69 | 5.69 | 0.18 | 0.3 | 0 |
| inbound:GameFinishLoading | 158* | 0.08 | 0.21 | 3.32 | 5.65 | 5.65 | 3.32 | 5.65 | 0.24 | 15.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.09 | 5.60 | 5.60 | 5.60 | 5.60 | 1.02 | 1.02 | 5.60 | 120.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.07 | 5.13 | 5.13 | 5.13 | 5.13 | 0.51 | 0.51 | 5.13 | 180.1 | 0 |
| event:quest.inventory | 940* | 0.10 | 0.31 | 0.55 | 5.01 | 5.01 | 5.01 | 5.01 | 4.96 | 0.5 | 0 |
| inbound:GameRequestPeer | 82** | 0.51 | 2.91 | 4.74 | 4.74 | 4.74 | 4.74 | 4.74 | 3.91 | 7.0 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.55 | 4.26 | 4.26 | 4.26 | 4.26 | 4.26 | 4.26 | 0.73 | 60.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.03 | 3.95 | 3.95 | 3.95 | 3.95 | 3.95 | 0.14 | 0.1 | 0 |
| timer:DungeonPartyWipeService | 1104 | 0.01 | 0.03 | 0.04 | 0.27 | 2.55 | 2.55 | 2.55 | 0.08 | 0.2 | 0 |
| event:quest.mirror-leave | 158* | 0.11 | 0.28 | 1.51 | 2.33 | 2.33 | 2.33 | 2.33 | 1.51 | 17.6 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.65 | 2.25 | 2.25 | 2.25 | 2.25 | 2.25 | 1.96 | 0.1 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.16 | 2.03 | 2.03 | 2.03 | 2.03 | 2.03 | 2.03 | 0.16 | 60.0 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.46 | 1.99 | 1.99 | 1.99 | 1.99 | 1.99 | 1.99 | 0.51 | 18.8 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.08 | 0.13 | 1.78 | 1.78 | 1.78 | 1.78 | 0.14 | 1.8 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.31 | 1.72 | 1.72 | 1.72 | 1.72 | 1.34 | 1.34 | 1.72 | 180.1 | 0 |
| event:altar.departure | 158* | 0.11 | 0.23 | 0.80 | 1.19 | 1.19 | 0.46 | 0.46 | 1.19 | 179.1 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.10 | 1.16 | 1.16 | 1.16 | 1.16 | 1.16 | 1.16 | 0.43 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.01 | 0.02 | 0.02 | 1.01 | 1.01 | 1.01 | 1.01 | 0.02 | 0.5 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.15 | 0.95 | 0.95 | 0.95 | 0.95 | 0.95 | 0.95 | 0.46 | 60.0 | 0 |
| timer:JoustMaintenanceService | 276* | 0.07 | 0.18 | 0.30 | 0.93 | 0.93 | 0.11 | 0.30 | 0.93 | 267.9 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.79 | 0.79 | 0.79 | 0.79 | 0.02 | 0.5 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.07 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.08 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.09 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.10 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5434 | 0.00 | 0.01 | 0.01 | 0.02 | 0.51 | 0.51 | 0.51 | 0.02 | 0.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.06 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.06 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.42 | 0.42 | 0.42 | 0.42 | 0.42 | 0.42 | 0.05 | 60.0 | 0 |
| event:tournament.departure | 158* | 0.09 | 0.17 | 0.19 | 0.29 | 0.29 | 0.16 | 0.16 | 0.29 | 123.3 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.15 | 0.15 | 0.15 | 0.15 | 0.15 | 0.15 | 0.01 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 158* | 0.00 | 0.01 | 0.03 | 0.05 | 0.05 | 0.05 | 0.05 | 0.02 | 12.7 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 114.5 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26100 | 0.05 | 57.91 | 105.0 | 633.7 | 1366.4 | event:quest.inventory | 1.8 |
| 冷启动窗 | 只算 inbound: | 23169 | 0.05 | 55.74 | 103.8 | 510.4 | 1304.4 |  |  |
| 稳态窗 | 全部作业 | 52902 | 0.04 | 51.46 | 99.75 | 157.3 | 240.0 | event:achievement.inventory | 154.8 |
| 稳态窗 | 只算 inbound: | 48030 | 0.05 | 50.32 | 99.61 | 157.3 | 225.4 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 998* | 0.13 | 69.60 | 180.9 | 187.1 | 187.1 | inbound:GameConnectionCheck | 232.2 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 713* | 0.17 | 69.60 | 181.0 | 187.1 | 187.1 |  |  |
| (e) 灌包期间 | 全部作业 | 1404744 | 0.00 | 0.00 | 0.01 | 37.30 | 165.5 | inbound:GameConnectionCheck | 267.5 |
| (e) 灌包期间 | 只算 inbound: | 1403590 | 0.00 | 0.00 | 0.01 | 34.82 | 165.5 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 130 | 296.0 | 418.6 | inbound:GameSelectCharacter | 227.7 |
| 1 | 280 | 1318.9 | 1366.4 | event:quest.inventory | 115.2 |
| 2 | 215 | 523.8 | 523.9 | inbound:GameConnectionCheck | 121.2 |
| 3 | 728 | 169.6 | 204.5 | timer:TournamentRewardSweepService | 113.9 |
| 4 | 420 | 27.52 | 33.11 | inbound:GameSetUserPosition | 33.01 |
| 5 | 455 | 92.55 | 104.7 | inbound:GameSetUserPosition | 92.01 |
| 6 | 447 | 87.24 | 87.47 | inbound:GameSetUserPosition | 110.5 |
| 7 | 437 | 45.74 | 45.78 | inbound:GameConnectionCheck | 73.97 |
| 8 | 392 | 100.4 | 101.4 | event:southern-dale.daily-refill-notifications | 105.6 |
| 9 | 415 | 60.52 | 74.88 | inbound:GameSetUserPosition | 62.30 |
| 10 | 487 | 112.3 | 112.5 | inbound:GameSetUserPosition | 117.8 |
| 11 | 417 | 113.1 | 113.2 | inbound:GameSetUserPosition | 113.1 |
| 12 | 475 | 108.6 | 128.2 | event:quest.inventory | 127.7 |
| 13 | 446 | 71.18 | 71.30 | inbound:GameConnectionCheck | 94.57 |
| 14 | 453 | 75.39 | 76.64 | event:quest.inventory | 76.26 |
| 15 | 424 | 77.80 | 77.83 | inbound:GameConnectionCheck | 79.83 |
| 16 | 431 | 172.3 | 172.4 | inbound:GameConnectionCheck | 120.5 |
| 17 | 447 | 46.42 | 60.10 | inbound:GameConnectionCheck | 74.79 |
| 18 | 448 | 87.94 | 88.05 | inbound:GameConnectionCheck | 111.0 |
| 19 | 449 | 87.01 | 87.18 | inbound:GameConnectionCheck | 114.0 |
| 20 | 421 | 42.36 | 42.52 | inbound:GameConnectionCheck | 69.14 |
| 21 | 445 | 101.1 | 101.1 | inbound:GameConnectionCheck | 122.4 |
| 22 | 431 | 43.59 | 43.62 | inbound:GameSetUserPosition | 43.54 |
| 23 | 434 | 89.38 | 91.03 | inbound:GameSetUserPosition | 113.0 |
| 24 | 474 | 79.05 | 92.77 | event:quest.inventory | 92.49 |
| 25 | 453 | 69.22 | 69.25 | inbound:GameSetUserPosition | 64.35 |
| 26 | 417 | 43.17 | 43.21 | inbound:GameSetUserPosition | 71.08 |
| 27 | 447 | 40.30 | 49.87 | inbound:GameConnectionCheck | 43.52 |
| 28 | 433 | 102.7 | 119.9 | inbound:GameSetUserPosition | 109.3 |
| 29 | 420 | 107.9 | 123.8 | inbound:GameConnectionCheck | 111.0 |
| 30 | 445 | 55.77 | 55.83 | inbound:GameSetUserPosition | 31.73 |
| 31 | 452 | 55.89 | 74.18 | event:quest.inventory | 73.75 |
| 32 | 458 | 102.5 | 119.4 | event:quest.inventory | 119.1 |
| 33 | 442 | 92.56 | 92.65 | inbound:GameConnectionCheck | 116.2 |
| 34 | 417 | 103.9 | 104.7 | inbound:GameConnectionCheck | 115.6 |
| 35 | 447 | 101.2 | 101.3 | inbound:GameConnectionCheck | 122.5 |
| 36 | 438 | 62.11 | 62.22 | inbound:GameConnectionCheck | 89.50 |
| 37 | 425 | 64.78 | 64.91 | inbound:GameConnectionCheck | 75.58 |
| 38 | 489 | 94.30 | 113.9 | event:quest.inventory | 113.6 |
| 39 | 485 | 170.2 | 171.7 | event:achievement.inventory | 114.5 |
| 40 | 439 | 102.0 | 102.2 | inbound:GameSetUserPosition | 107.0 |
| 41 | 441 | 47.13 | 47.30 | inbound:GameConnectionCheck | 61.32 |
| 42 | 397 | 111.1 | 111.3 | inbound:GameConnectionCheck | 107.8 |
| 43 | 437 | 62.91 | 80.20 | inbound:GameSetUserPosition | 88.65 |
| 44 | 490 | 108.6 | 114.7 | inbound:GameConnectionCheck | 112.4 |
| 45 | 432 | 46.31 | 46.34 | inbound:GameSetUserPosition | 63.40 |
| 46 | 401 | 65.75 | 108.6 | inbound:GameSetUserPosition | 108.5 |
| 47 | 494 | 104.3 | 112.9 | inbound:GameResponsePeer | 112.9 |
| 48 | 409 | 42.42 | 43.61 | inbound:GameConnectionCheck | 68.16 |
| 49 | 450 | 176.7 | 188.8 | inbound:GameSetUserPosition | 113.2 |
| 50 | 455 | 84.93 | 107.5 | event:quest.inventory | 107.2 |
| 51 | 453 | 58.74 | 62.59 | event:quest.inventory | 62.26 |
| 52 | 436 | 54.61 | 54.65 | inbound:GameConnectionCheck | 72.16 |
| 53 | 432 | 54.15 | 54.23 | inbound:GameSetUserPosition | 64.85 |
| 54 | 421 | 82.06 | 82.19 | inbound:GameConnectionCheck | 106.8 |
| 55 | 456 | 63.60 | 70.76 | event:quest.inventory | 70.24 |
| 56 | 453 | 111.2 | 111.3 | inbound:GameConnectionCheck | 117.4 |
| 57 | 433 | 74.91 | 75.02 | inbound:GameConnectionCheck | 76.18 |
| 58 | 411 | 114.8 | 114.9 | inbound:GameConnectionCheck | 139.9 |
| 59 | 491 | 84.25 | 84.51 | inbound:GameSetUserPosition | 91.11 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26100 | 25367 | 42.3% | inbound:GameSetUserArea 12147 ms／626／max 58.60；inbound:GameSelectCharacter 11130 ms／132／max 227.7；inbound:GameEplpCommand 503 ms／16／max 50.58；inbound:GameEnterSelectDungeon 307 ms／18／max 29.14；inbound:GameSelectDungeon 263 ms／18／max 73.22；inbound:GameSetUserPosition 239 ms／8623／max 16.67；event:achievement.inventory 170 ms／390／max 7.35；inbound:GameCreateCharacter 112 ms／39／max 31.09 |
| 稳态窗 | 120.0 | 52902 | 47107 | 39.3% | inbound:GameSetUserArea 26882 ms／1142／max 117.2；inbound:GameSelectCharacter 16123 ms／196／max 239.1；inbound:GameEplpCommand 1515 ms／34／max 94.27；inbound:GameEnterSelectDungeon 907 ms／36／max 56.15；inbound:GameSetUserPosition 474 ms／18437／max 15.61；inbound:GameSelectDungeon 392 ms／36／max 16.15；inbound:GameConnectionCheck 158 ms／27737／max 6.88；event:southern-dale.daily-refill-notifications 148 ms／196／max 11.24 |
| (d) 10 并发登录的轮内 | 1.4 | 998 | 700 | 49.9% | inbound:GameSelectCharacter 376 ms／3／max 203.0；inbound:GameSetUserArea 291 ms／11／max 38.14；http:POST /api/launcher/v1/register 7 ms／60／max 0.48；inbound:GameLogin 6 ms／60／max 0.29；inbound:GameSetUserPosition 6 ms／237／max 2.43；inbound:GameConnectionCheck 4 ms／399／max 2.44；http:POST /api/launcher/v1/login 3 ms／60／max 0.18；event:achievement.inventory 2 ms／9／max 0.46 |
| (e) 灌包期间 | 30.0 | 1404744 | 16638 | 55.5% | inbound:GameSetUserArea 7566 ms／278／max 58.80；inbound:GameConnectionCheck 4096 ms／1398339／max 5.42；inbound:GameSelectCharacter 3852 ms／50／max 122.0；inbound:GameEplpCommand 618 ms／10／max 96.86；inbound:GameEnterSelectDungeon 179 ms／7／max 35.17；inbound:GameSetUserPosition 107 ms／4801／max 2.54；inbound:GameSelectDungeon 71 ms／7／max 13.86；event:southern-dale.daily-refill-notifications 51 ms／50／max 6.61 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.2，`timer:` 31.4，`event:` 9.2。

