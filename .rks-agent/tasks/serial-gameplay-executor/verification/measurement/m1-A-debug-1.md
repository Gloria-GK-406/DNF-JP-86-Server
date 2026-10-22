# 基线测量 — m1ad1（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3912 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48001 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25505 | 53.13% |
| SET_USER_POSITION | 18499 | 38.54% |
| STEP_FENCE | 2141 | 4.46% |
| SET_USER_AREA | 1143 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 70 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| EPLP | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25505 | 1.18 | 55.25 | 94.66 | 214.5 | 312.7 | 0 | 86.96 | 138.1 | 234.4 |
| SET_USER_POSITION | 18499 | 1.19 | 55.32 | 95.61 | 234.7 | 328.5 | 0 | 88.44 | 122.3 | 234.3 |
| SET_USER_AREA | 1143 | 21.69 | 75.96 | 105.9 | 190.7 | 220.8 | 0 | 102.4 | 139.7 | 155.2 |
| RETURN_SELECT_CHARACTER | 197* | 1.76 | 58.37 | 114.4 | 259.4 | 259.4 | 0 | 114.2 | 126.5 | 126.5 |
| SELECT_CHARACTER | 197* | 4.27 | 15.90 | 74.18 | 92.67 | 92.67 | 0 | 74.18 | 92.67 | 92.67 |
| FINISH_LOADING | 70** | 0.14 | 0.25 | 0.55 | 0.55 | 0.55 | 0 | 0.55 | 0.55 | 0.55 |
| LEAVE_PARTY | 37** | 0.37 | 60.07 | 107.9 | 107.9 | 107.9 | 0 | 107.9 | 107.9 | 107.9 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.17 | 0.31 | 0.32 | 0.32 | 0.32 | 0 | 0.32 | 0.32 | 0.32 |
| EPLP | 35** | 10.99 | 76.83 | 150.3 | 150.3 | 150.3 | 0 | 148.0 | 148.0 | 148.0 |
| ENTER_SELECT_DUNGEON | 35** | 28.76 | 96.96 | 123.2 | 123.2 | 123.2 | 0 | 77.35 | 77.35 | 77.35 |
| SELECT_DUNGEON | 35** | 9.74 | 13.02 | 70.58 | 70.58 | 70.58 | 0 | 70.58 | 70.58 | 70.58 |
| CREATE_CHARACTER | 35** | 3.34 | 57.12 | 90.60 | 90.60 | 90.60 | 0 | 90.60 | 90.60 | 90.60 |
| **全部计入的请求** | 45824 | 1.26 | 55.97 | 95.53 | 221.2 | 328.5 | 36 | 88.37 | 131.0 | 234.4 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45860 | 0.41 | 5.23 | 45.32 | 172.4 | 315.4（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.67 | 8.97 | 15.40 | 103.8 | 103.8 | 0.6 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 12.79 | 24.22 | 48.21 | 67.03 | 67.03 | 165.0 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 14.86 | 39.46 | 52.10 | 61.32 | 61.32 | 0.8 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.39 | 35.75 | 56.10 | 59.81 | 59.81 | 118.4 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.10 | 55.07 | 57.48 | 59.34 | 59.34 | 74.7 | 0 |
| EnterDungeonCommand | Transactional | 81** | 5.99 | 7.57 | 54.08 | 54.08 | 54.08 | 12.5 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.08 | 2.84 | 23.64 | 23.64 | 23.64 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 80** | 1.01 | 2.60 | 17.15 | 17.15 | 17.15 | 17.4 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.09 | 0.29 | 9.72 | 9.72 | 9.72 | 0.2 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.15 | 0.71 | 1.09 | 7.14 | 7.14 | 1.9 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.37 | 1.01 | 5.74 | 5.74 | 5.74 | 0.4 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.03 | 0.03 | 5.03 | 5.03 | 0.4 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.05 | 0.08 | 0.11 | 3.91 | 3.91 | 0.7 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.06 | 3.79 | 3.79 | 0.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.06 | 0.15 | 0.22 | 3.38 | 3.38 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.03 | 0.08 | 0.12 | 3.37 | 3.37 | 0.5 | 0 |
| AdvanceCreatureClockCommand | Transactional | 785* | 0.03 | 0.13 | 0.32 | 3.22 | 3.22 | 1.6 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.03 | 0.06 | 0.07 | 2.82 | 2.82 | 0.7 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.11 | 0.21 | 2.59 | 2.59 | 19.6 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.01 | 0.13 | 2.58 | 2.58 | 2.58 | 6.8 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 0.38 | 1.10 | 2.34 | 2.34 | 2.34 | 132.9 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.14 | 2.29 | 2.29 | 2.29 | 0.3 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.18 | 0.54 | 1.40 | 1.89 | 1.89 | 239.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.03 | 1.71 | 1.71 | 1.71 | 0.4 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.03 | 0.05 | 0.07 | 1.54 | 1.54 | 0.7 | 0 |
| LeavePartyCommand | Direct | 77** | 0.01 | 0.05 | 0.96 | 0.96 | 0.96 | 21.8 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 0.90 | 0.90 | 0.90 | 0.90 | 60.0 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.04 | 0.78 | 0.78 | 0.78 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.05 | 0.08 | 0.72 | 0.72 | 0.5 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 162* | 0.01 | 0.02 | 0.29 | 0.62 | 0.62 | 12.6 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.03 | 0.10 | 0.40 | 0.40 | 19.7 | 0 |

命令日志行 9803 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**1077.2 ms**（CREATE_CHARACTER，计划发出 t=0.46 s，solo00；参考口径 1077.2 ms）。冷启动窗整体：21951 | 1.27 | 65.47 | 131.8 | 551.7 | 1077.2（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 41 | 1077.2 | 1077.2 | CREATE_CHARACTER |
| 1 | 30 | 287.8 | 287.8 | SELECT_CHARACTER |
| 2 | 214 | 584.4 | 606.6 | SET_USER_POSITION |
| 3 | 381 | 236.9 | 264.2 | SET_USER_AREA |
| 4 | 392 | 53.10 | 65.55 | CHECK_USER_CONNECTION |
| 5 | 379 | 47.43 | 53.41 | CHECK_USER_CONNECTION |
| 6 | 383 | 82.69 | 83.93 | CHECK_USER_CONNECTION |
| 7 | 370 | 63.15 | 66.28 | SET_USER_POSITION |
| 8 | 376 | 91.94 | 93.21 | SET_USER_AREA |
| 9 | 379 | 95.67 | 108.4 | SET_USER_POSITION |
| 10 | 384 | 77.05 | 83.07 | CHECK_USER_CONNECTION |
| 11 | 388 | 47.18 | 50.54 | SET_USER_AREA |
| 12 | 353 | 109.9 | 128.8 | ENTER_SELECT_DUNGEON |
| 13 | 379 | 74.46 | 74.62 | CHECK_USER_CONNECTION |
| 14 | 389 | 115.3 | 121.3 | SET_USER_POSITION |
| 15 | 379 | 72.26 | 85.02 | CHECK_USER_CONNECTION |
| 16 | 385 | 98.22 | 104.2 | SET_USER_POSITION |
| 17 | 381 | 57.87 | 64.70 | CHECK_USER_CONNECTION |
| 18 | 381 | 77.47 | 90.00 | CHECK_USER_CONNECTION |
| 19 | 387 | 63.11 | 69.24 | SET_USER_POSITION |
| 20 | 384 | 78.23 | 84.30 | CHECK_USER_CONNECTION |
| 21 | 386 | 39.32 | 48.38 | SET_USER_AREA |
| 22 | 386 | 41.88 | 59.98 | SET_USER_AREA |
| 23 | 386 | 51.50 | 63.51 | SET_USER_POSITION |
| 24 | 382 | 101.4 | 101.5 | CHECK_USER_CONNECTION |
| 25 | 381 | 57.48 | 60.37 | SET_USER_POSITION |
| 26 | 375 | 59.10 | 77.89 | CHECK_USER_CONNECTION |
| 27 | 382 | 31.07 | 31.80 | CHECK_USER_CONNECTION |
| 28 | 380 | 101.9 | 104.8 | SET_USER_POSITION |
| 29 | 387 | 42.87 | 65.76 | SET_USER_AREA |
| 30 | 381 | 45.13 | 46.67 | SET_USER_AREA |
| 31 | 370 | 80.96 | 100.0 | SET_USER_POSITION |
| 32 | 365 | 97.59 | 103.6 | CHECK_USER_CONNECTION |
| 33 | 376 | 56.90 | 63.00 | SET_USER_POSITION |
| 34 | 384 | 119.0 | 132.1 | CHECK_USER_CONNECTION |
| 35 | 382 | 97.19 | 109.3 | CHECK_USER_CONNECTION |
| 36 | 375 | 80.17 | 87.58 | EPLP |
| 37 | 390 | 46.78 | 59.35 | CHECK_USER_CONNECTION |
| 38 | 382 | 82.22 | 93.81 | CHECK_USER_CONNECTION |
| 39 | 376 | 72.35 | 73.21 | SET_USER_AREA |
| 40 | 383 | 84.60 | 97.11 | CHECK_USER_CONNECTION |
| 41 | 376 | 74.52 | 95.38 | CHECK_USER_CONNECTION |
| 42 | 383 | 56.78 | 62.98 | SET_USER_AREA |
| 43 | 389 | 93.99 | 100.0 | CHECK_USER_CONNECTION |
| 44 | 368 | 81.06 | 85.98 | SET_USER_POSITION |
| 45 | 380 | 36.43 | 41.30 | SET_USER_AREA |
| 46 | 366 | 96.18 | 108.5 | CHECK_USER_CONNECTION |
| 47 | 382 | 106.1 | 124.1 | SET_USER_AREA |
| 48 | 387 | 130.6 | 130.9 | SELECT_DUNGEON |
| 49 | 375 | 131.8 | 224.3 | CHECK_USER_CONNECTION |
| 50 | 374 | 96.95 | 122.9 | CHECK_USER_CONNECTION |
| 51 | 367 | 96.10 | 102.2 | SET_USER_POSITION |
| 52 | 383 | 55.78 | 64.20 | CHECK_USER_CONNECTION |
| 53 | 388 | 27.35 | 27.56 | SET_USER_POSITION |
| 54 | 381 | 44.32 | 49.99 | SET_USER_POSITION |
| 55 | 371 | 126.2 | 126.5 | CHECK_USER_CONNECTION |
| 56 | 379 | 58.45 | 58.85 | SET_USER_POSITION |
| 57 | 390 | 90.53 | 103.0 | CHECK_USER_CONNECTION |
| 58 | 386 | 96.34 | 113.1 | SET_USER_POSITION |
| 59 | 382 | 122.4 | 122.8 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45824 | 1.26 | 55.97 | 95.53 | 221.2 | 328.5 |
| 10 并发登录的轮内 | 541* | 25.75 | 79.21 | 96.49 | 110.3 | 110.3 |
| （参考：实际发出口径）稳态窗 | 45824 | 0.24 | 48.56 | 88.37 | 131.0 | 234.4 |
| （参考：实际发出口径）轮内 | 541* | 11.22 | 67.27 | 85.19 | 108.4 | 108.4 |
| 登录阶段整体（182–232 s） | 19185 | 1.52 | 58.13 | 89.74 | 129.6 | 183.5 |

每轮时长（开始 → 10 个 LOGIN 都应答）：258, 203, 315, 218, 195, 253 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17630 | 1.52 | 55.43 | 95.22 | 220.8 | 309.4 | 88.55 |
| 单人 | 轮内 | 207* | 26.72 | 81.02 | 99.63 | 110.3 | 110.3 | 98.31 |
| 双人组领队 | 稳态窗 | 14079 | 1.66 | 57.15 | 97.09 | 227.2 | 328.5 | 88.23 |
| 双人组领队 | 轮内 | 166* | 22.86 | 77.71 | 90.31 | 93.41 | 93.41 | 73.60 |
| 双人组队员 | 稳态窗 | 14115 | 0.67 | 56.46 | 96.49 | 214.5 | 312.8 | 88.32 |
| 双人组队员 | 轮内 | 168* | 21.61 | 76.30 | 90.33 | 93.44 | 93.44 | 73.60 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 93.05 | 177.9 | 178.5 | 178.5 | 178.5 |
| 启动器登录（HTTP，含校验） | 60** | 102.7 | 136.0 | 136.5 | 136.5 | 136.5 |
| Game LOGIN 0x0001 | 60** | 0.32 | 0.61 | 30.69 | 30.69 | 30.69 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 51778 包/秒（1554804 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45824 | 1.26 | 55.97 | 95.53 | 221.2 | 328.5 |
| 其余连接，灌包期间 | 11463 | 1.24 | 59.18 | 114.7 | 191.7 | 217.5 |
| （参考：实际发出口径）其余连接，灌包期间 | 11463 | 0.19 | 49.98 | 88.72 | 180.0 | 190.6 |
| 灌包连接自身（含其窗口内排队） | 1554835 | 0.33 | 0.41 | 1.86 | 69.30 | 395.5 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.6 |
| EPLP | 同伴 | 80 | 7878 | leave dungeon | 121.4 |
| SET_USER_AREA | 请求者 | 2655 | 6754 | town | 188.3 |
| EPLP | 请求者 | 80 | 6687 | leave dungeon | 121.4 |
| SET_USER_AREA | 同伴 | 6 | 5730 | town | 2.6 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 3155 | return to selection | 55.9 |
| REQUEST_PEER | 同伴 | 83 | 2453 | party | 106.3 |
| LEAVE_PARTY | 同伴 | 77 | 2221 | leave party | 86.6 |
| LEAVE_PARTY | 请求者 | 77 | 2158 | leave party | 86.6 |
| ENTER_SELECT_DUNGEON | 请求者 | 81 | 2110 | party dungeon entry | 148.2 |
| ENTER_SELECT_DUNGEON | 同伴 | 81 | 2110 | party dungeon entry | 148.2 |
| RESPONSE_PEER | 同伴 | 83 | 1991 | party | 49.2 |
| RESPONSE_PEER | 请求者 | 83 | 1949 | party | 49.2 |
| REQUEST_PEER | 请求者 | 83 | 1905 | party | 106.3 |
| SELECT_DUNGEON | 请求者 | 81 | 1367 | party dungeon entry | 12.5 |
| SELECT_DUNGEON | 同伴 | 81 | 1367 | party dungeon entry | 12.5 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.6 |
| LOGIN | 请求者 | 21 | 339 | login | 0.4 |
| FINISH_LOADING | 请求者 | 162 | 36 | party dungeon entry | 12.6 |
| FINISH_LOADING | 同伴 | 162 | 0 | party dungeon entry | 12.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 95.53 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 96.49 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 114.7 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 1077.2 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 484，p99.9 103.8 ms，max 103.8 ms（出现于 t=0.6 s；冷启动窗内 max 103.8 ms，之后 max 24.45 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 81 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：178 个；截尾样本（超时或连接被关闭）0 个，协议无回包 83 个。
- SET_USER_POSITION：78 个（其中截尾 0），最大 607 ms，计划发出时刻 1.6, 1.6, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2, 2.2, 2.2 … s
- CHECK_USER_CONNECTION：73 个（其中截尾 0），最大 582 ms，计划发出时刻 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2, 2.2, 2.2 … s
- SELECT_CHARACTER：12 个（其中截尾 0），最大 980 ms，计划发出时刻 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 1.6, 2.1, 2.1 s
- SET_USER_AREA：9 个（其中截尾 0），最大 539 ms，计划发出时刻 2.1, 2.2, 2.2, 2.2, 2.3, 2.6, 2.7, 2.8, 3.0 s
- RETURN_SELECT_CHARACTER：4 个（其中截尾 0），最大 451 ms，计划发出时刻 2.3, 2.4, 2.4, 112.9 s
- CREATE_CHARACTER：2 个（其中截尾 0），最大 1077 ms，计划发出时刻 0.5, 0.5 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1676037 行（Host 就绪之后 1676032 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 67.79 | 113.1 | 132.8 | 236.1 | 236.1 | 236.1 | 236.1 | 136.0 | 0.7 | 0 |
| inbound:GameEplpCommand | 80** | 45.62 | 69.72 | 97.99 | 97.99 | 97.99 | 54.60 | 54.60 | 97.99 | 113.0 | 0 |
| inbound:GameSetUserArea | 2655 | 23.18 | 42.37 | 54.63 | 68.41 | 73.85 | 11.09 | 62.24 | 73.85 | 112.7 | 0 |
| inbound:GameSelectDungeon | 81** | 9.64 | 12.26 | 71.06 | 71.06 | 71.06 | 71.06 | 71.06 | 12.94 | 12.5 | 0 |
| inbound:GameEnterSelectDungeon | 81** | 22.57 | 33.99 | 44.13 | 44.13 | 44.13 | 27.89 | 30.86 | 44.13 | 157.5 | 0 |
| inbound:GameCreateCharacter | 94** | 1.70 | 3.72 | 31.59 | 31.59 | 31.59 | 31.59 | 31.59 | 5.87 | 0.4 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.10 | 0.45 | 17.35 | 17.35 | 17.35 | 17.35 | 17.35 | 2.42 | 0.2 | 0 |
| inbound:GameSetUserPosition | 41826 | 0.02 | 0.06 | 0.11 | 1.07 | 13.97 | 4.73 | 8.35 | 13.97 | 100.9 | 0 |
| inbound:GameLogin | 81** | 0.10 | 0.95 | 13.46 | 13.46 | 13.46 | 13.46 | 13.46 | 0.95 | 0.4 | 0 |
| inbound:GameReturnToSelection | 463* | 0.42 | 1.49 | 2.81 | 12.40 | 12.40 | 12.40 | 12.40 | 4.86 | 1.9 | 0 |
| event:achievement.inventory | 940* | 0.24 | 0.57 | 1.44 | 11.75 | 11.75 | 11.75 | 11.75 | 2.96 | 0.5 | 0 |
| inbound:GameLeaveParty | 77** | 0.22 | 0.90 | 9.98 | 9.98 | 9.98 | 9.98 | 9.98 | 2.68 | 21.8 | 0 |
| inbound:GameResponsePeer | 83** | 0.19 | 0.70 | 8.21 | 8.21 | 8.21 | 8.21 | 8.21 | 1.29 | 6.8 | 0 |
| inbound:GameConnectionCheck | 1617608 | 0.00 | 0.00 | 0.01 | 0.02 | 7.19 | 0.22 | 1.50 | 7.19 | 252.5 | 0 |
| timer:CreatureClockService | 276* | 0.16 | 0.48 | 1.08 | 7.11 | 7.11 | 7.11 | 7.11 | 1.14 | 1.6 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.74 | 6.68 | 6.68 | 6.68 | 6.68 | 6.68 | 6.68 | 0.81 | 60.0 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 1.16 | 6.68 | 6.68 | 6.68 | 6.68 | 6.68 | 4.63 | 0.1 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.41 | 2.91 | 3.22 | 6.61 | 6.61 | 5.63 | 6.61 | 5.54 | 33.4 | 0 |
| inbound:GameRequestPeer | 83** | 0.48 | 1.76 | 4.43 | 4.43 | 4.43 | 4.43 | 4.43 | 2.74 | 6.8 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 4.23 | 4.23 | 4.23 | 4.23 | 4.23 | 0.05 | 0.1 | 0 |
| inbound:GameFinishLoading | 162* | 0.08 | 0.19 | 2.37 | 4.01 | 4.01 | 4.01 | 4.01 | 0.21 | 12.6 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.04 | 0.18 | 2.95 | 2.95 | 2.95 | 2.67 | 2.95 | 0.16 | 0.3 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.10 | 2.60 | 2.60 | 2.60 | 2.60 | 0.60 | 0.60 | 2.60 | 120.0 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.48 | 2.39 | 2.39 | 2.39 | 2.39 | 1.56 | 1.56 | 2.39 | 180.0 | 0 |
| event:altar.departure | 162* | 0.10 | 0.21 | 1.38 | 2.09 | 2.09 | 2.09 | 2.09 | 1.38 | 17.4 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.49 | 2.03 | 2.03 | 2.03 | 2.03 | 2.03 | 2.03 | 0.73 | 19.7 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.06 | 0.11 | 2.03 | 2.03 | 2.03 | 2.03 | 0.10 | 1.5 | 0 |
| event:quest.inventory | 940* | 0.09 | 0.29 | 0.58 | 1.70 | 1.70 | 0.57 | 1.63 | 1.70 | 217.6 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 0.11 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1105 | 0.01 | 0.03 | 0.04 | 0.40 | 1.32 | 1.32 | 1.32 | 0.04 | 0.2 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.28 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 0.62 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.10 | 1.10 | 1.10 | 1.10 | 1.10 | 1.10 | 1.10 | 0.28 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.02 | 1.03 | 1.03 | 1.03 | 1.03 | 0.03 | 0.5 | 0 |
| event:quest.mirror-leave | 162* | 0.11 | 0.26 | 0.82 | 0.86 | 0.86 | 0.86 | 0.86 | 0.82 | 17.4 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.78 | 0.78 | 0.78 | 0.78 | 0.02 | 0.5 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.07 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.08 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.18 | 0.70 | 0.70 | 0.70 | 0.70 | 0.70 | 0.70 | 0.36 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5464 | 0.00 | 0.01 | 0.01 | 0.02 | 0.67 | 0.67 | 0.67 | 0.02 | 0.0 | 0 |
| timer:JoustMaintenanceService | 276* | 0.06 | 0.19 | 0.29 | 0.59 | 0.59 | 0.11 | 0.29 | 0.59 | 152.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.06 | 0.57 | 0.57 | 0.57 | 0.57 | 0.57 | 0.57 | 0.10 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.06 | 0.56 | 0.56 | 0.56 | 0.56 | 0.56 | 0.56 | 0.07 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.53 | 0.07 | 60.0 | 0 |
| event:tournament.departure | 162* | 0.09 | 0.14 | 0.19 | 0.34 | 0.34 | 0.19 | 0.19 | 0.34 | 112.5 | 0 |
| timer:NotificationRetryDrainService | 276* | 0.00 | 0.00 | 0.01 | 0.17 | 0.17 | 0.17 | 0.17 | 0.01 | 1.5 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.10 | 0.10 | 0.10 | 0.10 | 0.10 | 0.10 | 0.02 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 162* | 0.00 | 0.01 | 0.03 | 0.05 | 0.05 | 0.05 | 0.05 | 0.03 | 12.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 95.53 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26196 | 0.06 | 51.79 | 99.49 | 508.2 | 1074.5 | inbound:GameCreateCharacter | 1.5 |
| 冷启动窗 | 只算 inbound: | 23201 | 0.05 | 50.05 | 95.30 | 383.3 | 1074.5 |  |  |
| 稳态窗 | 全部作业 | 53101 | 0.05 | 47.93 | 86.10 | 125.8 | 223.4 | inbound:GameConnectionCheck | 113.0 |
| 稳态窗 | 只算 inbound: | 48065 | 0.05 | 45.74 | 85.26 | 125.3 | 223.4 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1047 | 0.23 | 54.77 | 74.09 | 83.44 | 98.54 | timer:TournamentRewardSweepService | 232.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 751* | 0.24 | 49.49 | 66.03 | 83.44 | 83.44 |  |  |
| (e) 灌包期间 | 全部作业 | 1568045 | 0.00 | 0.00 | 0.01 | 28.74 | 190.5 | inbound:GameConnectionCheck | 251.5 |
| (e) 灌包期间 | 只算 inbound: | 1566860 | 0.00 | 0.00 | 0.01 | 27.45 | 190.5 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 122 | 352.6 | 437.7 | inbound:GameSelectCharacter | 236.1 |
| 1 | 87 | 1074.5 | 1074.5 | inbound:GameCreateCharacter | 113.5 |
| 2 | 252 | 407.1 | 410.1 | timer:TournamentRewardSweepService | 113.1 |
| 3 | 938 | 186.3 | 212.4 | inbound:GameSetUserPosition | 114.7 |
| 4 | 444 | 50.14 | 66.86 | event:quest.inventory | 66.56 |
| 5 | 444 | 51.80 | 65.07 | event:quest.inventory | 64.79 |
| 6 | 434 | 82.01 | 85.17 | timer:TournamentRewardSweepService | 108.0 |
| 7 | 434 | 47.28 | 66.09 | inbound:GameSetUserPosition | 66.04 |
| 8 | 435 | 76.23 | 86.62 | inbound:GameSetUserPosition | 73.26 |
| 9 | 435 | 89.78 | 108.3 | inbound:GameSetUserPosition | 111.1 |
| 10 | 434 | 75.57 | 85.82 | timer:TournamentRewardSweepService | 108.0 |
| 11 | 436 | 46.96 | 47.65 | timer:TournamentRewardSweepService | 74.90 |
| 12 | 464 | 109.7 | 110.1 | event:quest.inventory | 109.6 |
| 13 | 433 | 73.08 | 73.08 | inbound:GameConnectionCheck | 68.68 |
| 14 | 446 | 120.1 | 139.0 | event:quest.inventory | 138.7 |
| 15 | 439 | 69.74 | 84.82 | inbound:GameConnectionCheck | 87.31 |
| 16 | 434 | 96.45 | 100.8 | inbound:GameSetUserPosition | 110.0 |
| 17 | 443 | 57.18 | 64.36 | inbound:GameConnectionCheck | 67.78 |
| 18 | 462 | 71.76 | 87.89 | inbound:GameConnectionCheck | 95.52 |
| 19 | 434 | 62.30 | 71.61 | timer:TournamentRewardSweepService | 89.15 |
| 20 | 436 | 75.58 | 86.41 | timer:TournamentRewardSweepService | 101.0 |
| 21 | 440 | 38.42 | 45.04 | inbound:GameConnectionCheck | 63.05 |
| 22 | 428 | 36.14 | 41.79 | inbound:GameSetUserArea | 41.05 |
| 23 | 419 | 46.81 | 63.42 | inbound:GameSetUserPosition | 64.25 |
| 24 | 494 | 98.35 | 113.7 | event:quest.inventory | 113.3 |
| 25 | 461 | 56.79 | 65.46 | event:quest.inventory | 65.16 |
| 26 | 427 | 56.14 | 77.69 | inbound:GameConnectionCheck | 75.92 |
| 27 | 435 | 28.84 | 31.43 | inbound:GameConnectionCheck | 36.34 |
| 28 | 432 | 97.30 | 104.2 | inbound:GameSetUserPosition | 106.6 |
| 29 | 435 | 42.76 | 51.16 | timer:BossTowerReturnSweepService | 59.99 |
| 30 | 432 | 44.06 | 45.71 | inbound:GameConnectionCheck | 35.82 |
| 31 | 444 | 80.84 | 103.1 | event:quest.inventory | 102.7 |
| 32 | 465 | 93.71 | 101.8 | inbound:GameConnectionCheck | 98.15 |
| 33 | 440 | 56.43 | 64.94 | timer:TournamentRewardSweepService | 67.58 |
| 34 | 435 | 117.3 | 131.9 | inbound:GameConnectionCheck | 134.4 |
| 35 | 439 | 97.07 | 108.1 | inbound:GameConnectionCheck | 112.2 |
| 36 | 459 | 68.66 | 79.34 | inbound:GameConnectionCheck | 87.32 |
| 37 | 439 | 40.67 | 58.75 | inbound:GameConnectionCheck | 61.29 |
| 38 | 475 | 84.05 | 104.9 | event:quest.inventory | 104.7 |
| 39 | 478 | 64.91 | 67.80 | inbound:GameConnectionCheck | 64.24 |
| 40 | 433 | 82.44 | 95.30 | inbound:GameConnectionCheck | 101.0 |
| 41 | 438 | 71.20 | 94.72 | inbound:GameConnectionCheck | 76.86 |
| 42 | 440 | 48.00 | 61.03 | inbound:GameConnectionCheck | 65.22 |
| 43 | 434 | 92.54 | 101.8 | timer:TournamentRewardSweepService | 105.5 |
| 44 | 450 | 73.50 | 84.13 | event:quest.inventory | 83.79 |
| 45 | 418 | 27.88 | 36.04 | inbound:GameConnectionCheck | 56.02 |
| 46 | 449 | 95.12 | 109.0 | event:quest.inventory | 108.7 |
| 47 | 457 | 105.0 | 116.3 | inbound:GameSetUserPosition | 102.1 |
| 48 | 398 | 102.7 | 102.7 | inbound:GameConnectionCheck | 78.42 |
| 49 | 454 | 130.5 | 130.6 | inbound:GameConnectionCheck | 98.99 |
| 50 | 472 | 94.69 | 116.1 | inbound:GameConnectionCheck | 94.45 |
| 51 | 457 | 99.49 | 120.7 | event:quest.inventory | 120.2 |
| 52 | 420 | 43.78 | 64.04 | inbound:GameConnectionCheck | 57.64 |
| 53 | 450 | 40.62 | 61.52 | inbound:GameSetUserPosition | 62.72 |
| 54 | 445 | 40.59 | 52.58 | timer:TournamentRewardSweepService | 59.18 |
| 55 | 472 | 106.7 | 126.3 | inbound:GameConnectionCheck | 105.1 |
| 56 | 419 | 54.36 | 58.31 | inbound:GameConnectionCheck | 83.82 |
| 57 | 450 | 89.57 | 95.87 | inbound:GameConnectionCheck | 107.7 |
| 58 | 436 | 92.08 | 111.7 | inbound:GameSetUserPosition | 107.9 |
| 59 | 441 | 104.5 | 122.4 | inbound:GameSetUserPosition | 106.4 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26196 | 25272 | 42.1% | inbound:GameSetUserArea 12780 ms／625／max 62.24；inbound:GameSelectCharacter 10390 ms／132／max 236.1；inbound:GameEplpCommand 596 ms／16／max 54.60；inbound:GameEnterSelectDungeon 301 ms／18／max 30.86；inbound:GameSelectDungeon 242 ms／18／max 71.06；inbound:GameSetUserPosition 233 ms／8677／max 8.35；event:achievement.inventory 134 ms／390／max 11.75；event:southern-dale.daily-refill-notifications 113 ms／132／max 6.61 |
| 稳态窗 | 120.0 | 53101 | 46800 | 39.0% | inbound:GameSetUserArea 27894 ms／1144／max 73.85；inbound:GameSelectCharacter 14958 ms／196／max 136.0；inbound:GameEplpCommand 1633 ms／36／max 97.99；inbound:GameEnterSelectDungeon 814 ms／35／max 44.13；inbound:GameSetUserPosition 470 ms／18502／max 13.97；inbound:GameSelectDungeon 343 ms／35／max 12.94；event:southern-dale.daily-refill-notifications 115 ms／196／max 5.54；inbound:GameReturnToSelection 115 ms／197／max 4.86 |
| (d) 10 并发登录的轮内 | 1.4 | 1047 | 945 | 65.5% | inbound:GameSetUserArea 461 ms／14／max 51.00；inbound:GameSelectCharacter 331 ms／4／max 107.0；inbound:GameEplpCommand 122 ms／2／max 66.45；http:POST /api/launcher/v1/register 10 ms／60／max 2.42；inbound:GameLogin 6 ms／60／max 0.95；inbound:GameSetUserPosition 4 ms／216／max 0.13；event:southern-dale.daily-refill-notifications 3 ms／4／max 1.65；http:POST /api/launcher/v1/login 2 ms／60／max 0.16 |
| (e) 灌包期间 | 30.0 | 1568045 | 15653 | 52.1% | inbound:GameSetUserArea 7312 ms／277／max 56.59；inbound:GameSelectCharacter 3682 ms／51／max 107.9；inbound:GameConnectionCheck 3534 ms／1561831／max 7.19；inbound:GameEplpCommand 512 ms／9／max 87.71；inbound:GameEnterSelectDungeon 289 ms／10／max 37.33；inbound:GameSetUserPosition 113 ms／4570／max 2.55；inbound:GameSelectDungeon 96 ms／10／max 11.16；inbound:GameReturnToSelection 29 ms／51／max 1.08 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 32.7，`event:` 9.3。

