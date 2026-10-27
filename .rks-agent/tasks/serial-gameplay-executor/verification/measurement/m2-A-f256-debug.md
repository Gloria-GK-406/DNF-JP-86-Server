# 基线测量 — m2afd（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 3962 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 256）。总时长 274 s。
- 稳态窗实际包速率：400.0 包/秒（48000 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25516 | 53.16% |
| SET_USER_POSITION | 18488 | 38.52% |
| STEP_FENCE | 2140 | 4.46% |
| SET_USER_AREA | 1143 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 70 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| EPLP | 35 | 0.07% |
| CREATE_CHARACTER | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25516 | 0.58 | 53.71 | 89.22 | 130.9 | 158.2 | 0 | 83.11 | 130.9 | 157.9 |
| SET_USER_POSITION | 18488 | 0.65 | 54.49 | 90.70 | 128.7 | 159.3 | 0 | 87.02 | 122.9 | 157.9 |
| SET_USER_AREA | 1143 | 29.06 | 74.64 | 115.7 | 149.5 | 164.5 | 0 | 113.8 | 149.4 | 163.9 |
| RETURN_SELECT_CHARACTER | 197* | 2.11 | 67.29 | 122.0 | 128.7 | 128.7 | 0 | 102.0 | 128.6 | 128.6 |
| SELECT_CHARACTER | 197* | 67.35 | 121.4 | 134.2 | 164.8 | 164.8 | 0 | 134.2 | 164.8 | 164.8 |
| FINISH_LOADING | 70** | 0.14 | 0.42 | 17.70 | 17.70 | 17.70 | 0 | 17.70 | 17.70 | 17.70 |
| LEAVE_PARTY | 37** | 0.41 | 64.68 | 67.27 | 67.27 | 67.27 | 0 | 67.27 | 67.27 | 67.27 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.25 | 27.62 | 61.58 | 61.58 | 61.58 | 0 | 61.58 | 61.58 | 61.58 |
| EPLP | 35** | 49.87 | 81.35 | 85.05 | 85.05 | 85.05 | 0 | 77.76 | 77.76 | 77.76 |
| CREATE_CHARACTER | 35** | 3.29 | 6.95 | 68.87 | 68.87 | 68.87 | 0 | 68.87 | 68.87 | 68.87 |
| ENTER_SELECT_DUNGEON | 35** | 28.07 | 86.34 | 95.16 | 95.16 | 95.16 | 0 | 58.03 | 58.03 | 58.03 |
| SELECT_DUNGEON | 35** | 10.16 | 73.79 | 77.75 | 77.75 | 77.75 | 0 | 77.75 | 77.75 | 77.75 |
| **全部计入的请求** | 45824 | 0.66 | 56.97 | 92.83 | 131.0 | 164.8 | 36 | 87.77 | 130.9 | 164.8 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45860 | 0.00 | 4.10 | 39.92 | 79.76 | 109.3（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 484* | 3.61 | 9.34 | 16.83 | 99.02 | 99.02 | 0.5 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 484* | 12.83 | 23.83 | 57.27 | 67.34 | 67.34 | 2.3 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 484* | 16.55 | 55.17 | 58.26 | 61.19 | 61.19 | 154.1 | 0 |
| EnterDungeonCommand | Transactional | 80** | 6.02 | 10.98 | 61.01 | 61.01 | 61.01 | 12.7 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 484* | 14.29 | 30.36 | 55.89 | 59.27 | 59.27 | 48.5 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 484* | 14.84 | 39.94 | 50.72 | 58.87 | 58.87 | 0.8 | 0 |
| CreateCharacterCommand | Transactional | 94** | 1.24 | 2.90 | 24.45 | 24.45 | 24.45 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 79** | 0.99 | 2.17 | 13.88 | 13.88 | 13.88 | 16.8 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.10 | 0.25 | 9.81 | 9.81 | 9.81 | 0.2 | 0 |
| GetAccountCharacterRosterCommand | Direct | 94** | 0.34 | 0.99 | 8.11 | 8.11 | 8.11 | 0.4 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 463* | 0.16 | 0.72 | 0.93 | 6.46 | 6.46 | 1.9 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 484* | 0.04 | 0.07 | 0.09 | 3.85 | 3.85 | 0.6 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 484* | 0.01 | 0.03 | 0.06 | 3.57 | 3.57 | 0.6 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 484* | 0.03 | 0.08 | 0.13 | 3.51 | 3.51 | 0.4 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 484* | 0.07 | 0.15 | 0.20 | 3.44 | 3.44 | 0.4 | 0 |
| AdvanceCreatureClockCommand | Transactional | 773* | 0.03 | 0.10 | 0.28 | 3.28 | 3.28 | 1.4 | 0 |
| AcceptPartyInviteCommand | Direct | 80** | 0.01 | 0.11 | 3.11 | 3.11 | 3.11 | 7.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 529* | 0.04 | 0.06 | 0.07 | 2.82 | 2.82 | 0.7 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 463* | 0.18 | 0.60 | 1.65 | 2.78 | 2.78 | 253.9 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.03 | 0.16 | 2.63 | 2.63 | 2.63 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 81** | 0.40 | 1.43 | 2.41 | 2.41 | 2.41 | 7.0 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 565* | 0.01 | 0.01 | 0.02 | 1.70 | 1.70 | 0.3 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.02 | 1.67 | 1.67 | 1.67 | 1.67 | 60.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.03 | 1.63 | 1.63 | 1.63 | 0.3 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 484* | 0.05 | 0.12 | 0.28 | 1.63 | 1.63 | 0.4 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 484* | 0.04 | 0.05 | 0.07 | 1.54 | 1.54 | 0.6 | 0 |
| LeavePartyCommand | Direct | 77** | 0.01 | 0.03 | 0.77 | 0.77 | 0.77 | 22.1 | 0 |
| InitializeHousingCommand | Transactional | 484* | 0.02 | 0.05 | 0.08 | 0.70 | 0.70 | 0.4 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.02 | 0.27 | 0.63 | 0.63 | 12.8 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.03 | 0.47 | 0.47 | 0.47 | 60.0 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.03 | 0.15 | 0.45 | 0.45 | 32.6 | 0 |

命令日志行 9782 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**921.3 ms**（SELECT_CHARACTER，计划发出 t=0.43 s，solo03；参考口径 921.3 ms）。冷启动窗整体：21984 | 0.85 | 65.26 | 138.2 | 535.5 | 921.3（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 35 | 921.3 | 921.3 | SELECT_CHARACTER |
| 1 | 95 | 426.7 | 426.7 | SELECT_CHARACTER |
| 2 | 187 | 572.6 | 572.7 | SET_USER_POSITION |
| 3 | 378 | 93.23 | 110.1 | SET_USER_POSITION |
| 4 | 387 | 65.19 | 110.7 | SELECT_CHARACTER |
| 5 | 384 | 56.50 | 58.86 | SELECT_CHARACTER |
| 6 | 386 | 35.72 | 59.95 | SELECT_CHARACTER |
| 7 | 371 | 74.36 | 119.7 | SELECT_CHARACTER |
| 8 | 367 | 102.8 | 102.9 | CHECK_USER_CONNECTION |
| 9 | 382 | 100.5 | 111.9 | SELECT_CHARACTER |
| 10 | 385 | 74.80 | 80.15 | SET_USER_AREA |
| 11 | 383 | 106.3 | 107.1 | SELECT_CHARACTER |
| 12 | 372 | 75.26 | 85.84 | SELECT_DUNGEON |
| 13 | 373 | 81.11 | 111.5 | SELECT_CHARACTER |
| 14 | 376 | 37.31 | 83.16 | SELECT_CHARACTER |
| 15 | 383 | 61.49 | 77.43 | SELECT_CHARACTER |
| 16 | 385 | 138.7 | 184.2 | SELECT_CHARACTER |
| 17 | 381 | 65.90 | 88.20 | SELECT_CHARACTER |
| 18 | 387 | 87.04 | 87.21 | SET_USER_POSITION |
| 19 | 381 | 75.78 | 91.76 | SET_USER_POSITION |
| 20 | 386 | 84.33 | 111.3 | SELECT_CHARACTER |
| 21 | 390 | 57.06 | 87.84 | SELECT_CHARACTER |
| 22 | 385 | 42.36 | 58.86 | SELECT_CHARACTER |
| 23 | 384 | 104.4 | 107.0 | SELECT_CHARACTER |
| 24 | 381 | 44.72 | 66.79 | SELECT_CHARACTER |
| 25 | 381 | 72.58 | 76.64 | SELECT_CHARACTER |
| 26 | 387 | 49.10 | 60.18 | SELECT_CHARACTER |
| 27 | 381 | 104.4 | 135.4 | SELECT_CHARACTER |
| 28 | 379 | 44.16 | 64.56 | SELECT_CHARACTER |
| 29 | 383 | 67.46 | 68.17 | SET_USER_POSITION |
| 30 | 376 | 52.14 | 63.28 | CHECK_USER_CONNECTION |
| 31 | 383 | 73.49 | 115.3 | SELECT_CHARACTER |
| 32 | 371 | 72.55 | 100.9 | CHECK_USER_CONNECTION |
| 33 | 387 | 77.07 | 110.6 | ENTER_SELECT_DUNGEON |
| 34 | 357 | 60.60 | 88.11 | SELECT_CHARACTER |
| 35 | 373 | 138.5 | 139.5 | SELECT_CHARACTER |
| 36 | 384 | 88.66 | 103.1 | SELECT_CHARACTER |
| 37 | 387 | 67.43 | 129.4 | SET_USER_POSITION |
| 38 | 383 | 96.24 | 102.5 | SET_USER_AREA |
| 39 | 376 | 133.4 | 180.6 | SELECT_CHARACTER |
| 40 | 380 | 97.95 | 114.3 | SET_USER_POSITION |
| 41 | 380 | 85.28 | 136.9 | SET_USER_POSITION |
| 42 | 388 | 110.6 | 110.7 | CHECK_USER_CONNECTION |
| 43 | 380 | 95.14 | 106.6 | SELECT_CHARACTER |
| 44 | 375 | 59.26 | 80.57 | SET_USER_POSITION |
| 45 | 381 | 87.87 | 104.7 | SET_USER_POSITION |
| 46 | 370 | 138.8 | 138.9 | CHECK_USER_CONNECTION |
| 47 | 383 | 58.96 | 69.87 | SELECT_CHARACTER |
| 48 | 380 | 103.9 | 107.4 | SELECT_CHARACTER |
| 49 | 387 | 66.29 | 109.5 | SELECT_CHARACTER |
| 50 | 378 | 46.48 | 86.51 | SELECT_CHARACTER |
| 51 | 373 | 65.22 | 69.58 | SELECT_CHARACTER |
| 52 | 369 | 62.75 | 62.94 | SELECT_CHARACTER |
| 53 | 384 | 46.34 | 76.11 | SET_USER_AREA |
| 54 | 383 | 69.11 | 116.4 | SELECT_CHARACTER |
| 55 | 384 | 76.36 | 108.0 | SELECT_CHARACTER |
| 56 | 358 | 99.25 | 99.96 | SELECT_CHARACTER |
| 57 | 394 | 83.92 | 88.08 | SELECT_CHARACTER |
| 58 | 384 | 103.2 | 133.4 | SELECT_CHARACTER |
| 59 | 381 | 44.51 | 60.47 | SET_USER_AREA |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45824 | 0.66 | 56.97 | 92.83 | 131.0 | 164.8 |
| 10 并发登录的轮内 | 516* | 29.96 | 84.01 | 94.61 | 143.1 | 143.1 |
| （参考：实际发出口径）稳态窗 | 45824 | 0.30 | 51.98 | 87.77 | 130.9 | 164.8 |
| （参考：实际发出口径）轮内 | 516* | 19.54 | 76.32 | 92.74 | 109.9 | 109.9 |
| 登录阶段整体（182–232 s） | 19206 | 0.88 | 59.08 | 96.48 | 129.2 | 163.1 |

每轮时长（开始 → 10 个 LOGIN 都应答）：208, 200, 299, 233, 223, 242 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17628 | 0.78 | 59.29 | 97.96 | 133.7 | 164.8 | 90.47 |
| 单人 | 轮内 | 195* | 31.88 | 81.62 | 92.97 | 100.5 | 100.5 | 92.88 |
| 双人组领队 | 稳态窗 | 14080 | 0.60 | 54.78 | 92.45 | 130.9 | 159.3 | 86.58 |
| 双人组领队 | 轮内 | 159* | 29.91 | 84.12 | 111.5 | 143.1 | 143.1 | 92.81 |
| 双人组队员 | 稳态窗 | 14116 | 0.57 | 54.11 | 89.52 | 130.9 | 159.3 | 86.09 |
| 双人组队员 | 轮内 | 162* | 28.82 | 83.77 | 94.63 | 95.13 | 95.13 | 92.27 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 89.85 | 141.7 | 169.4 | 169.4 | 169.4 |
| 启动器登录（HTTP，含校验） | 60** | 89.56 | 133.5 | 169.8 | 169.8 | 169.8 |
| Game LOGIN 0x0001 | 60** | 0.22 | 65.21 | 65.43 | 65.43 | 65.43 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 256 持续发 CHECK_USER_CONNECTION；实际应答速率 46169 包/秒（1385642 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45824 | 0.66 | 56.97 | 92.83 | 131.0 | 164.8 |
| 其余连接，灌包期间 | 11478 | 0.86 | 62.57 | 97.84 | 135.8 | 181.3 |
| （参考：实际发出口径）其余连接，灌包期间 | 11478 | 0.26 | 59.14 | 91.11 | 135.7 | 181.3 |
| 灌包连接自身（含其窗口内排队） | 1385897 | 3.09 | 6.49 | 66.54 | 253.1 | 362.7 |

灌包窗口 256 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.6 |
| EPLP | 同伴 | 79 | 6924 | leave dungeon | 228.0 |
| SET_USER_AREA | 请求者 | 2657 | 6898 | town | 30.4 |
| EPLP | 请求者 | 79 | 5829 | leave dungeon | 228.0 |
| SET_USER_AREA | 同伴 | 6 | 4552 | town | 3.0 |
| LEAVE_PARTY | 同伴 | 77 | 3210 | leave party | 41.6 |
| REQUEST_PEER | 同伴 | 80 | 3202 | party | 129.1 |
| LEAVE_PARTY | 请求者 | 77 | 2972 | leave party | 44.3 |
| RETURN_SELECT_CHARACTER | 请求者 | 463 | 2877 | return to selection | 168.1 |
| REQUEST_PEER | 请求者 | 80 | 1884 | party | 129.1 |
| RESPONSE_PEER | 同伴 | 80 | 1656 | party | 108.2 |
| RESPONSE_PEER | 请求者 | 80 | 1518 | party | 108.2 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 1391 | party dungeon entry | 212.4 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 1391 | party dungeon entry | 212.4 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 12.7 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 12.7 |
| CREATE_CHARACTER | 请求者 | 94 | 1323 | create | 234.6 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 12.8 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 12.8 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 92.83 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 94.61 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 97.84 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 0 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 921.3 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- 无。

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 10 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：142 个；截尾样本（超时或连接被关闭）10 个，协议无回包 80 个。
- SET_USER_POSITION：64 个（其中截尾 0），最大 573 ms，计划发出时刻 1.4, 1.4, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.6, 1.6, 1.6 … s
- CHECK_USER_CONNECTION：58 个（其中截尾 0），最大 573 ms，计划发出时刻 1.4, 1.4, 1.4, 1.4, 1.5, 1.5, 1.5, 1.5, 1.5, 1.6, 2.2, 2.2 … s
- SELECT_CHARACTER：13 个（其中截尾 0），最大 921 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 1.4, 1.4, 1.4 … s
- SET_USER_AREA：7 个（其中截尾 0），最大 572 ms，计划发出时刻 1.4, 1.5, 1.6, 2.6, 2.3, 2.4, 2.8 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1506833 行（Host 就绪之后 1506828 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 484* | 66.59 | 115.3 | 133.3 | 226.8 | 226.8 | 226.8 | 226.8 | 135.6 | 0.7 | 0 |
| inbound:GameEplpCommand | 79** | 45.13 | 74.43 | 109.2 | 109.2 | 109.2 | 59.98 | 59.98 | 109.2 | 202.2 | 0 |
| inbound:GameSelectDungeon | 80** | 10.00 | 16.63 | 75.78 | 75.78 | 75.78 | 75.78 | 75.78 | 27.35 | 12.7 | 0 |
| inbound:GameSetUserArea | 2658 | 23.07 | 41.41 | 53.54 | 60.74 | 63.85 | 11.17 | 58.44 | 63.85 | 224.5 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 21.24 | 46.92 | 57.51 | 57.51 | 57.51 | 25.42 | 25.76 | 57.51 | 163.0 | 0 |
| inbound:GameCreateCharacter | 94** | 1.75 | 3.73 | 35.19 | 35.19 | 35.19 | 35.19 | 35.19 | 5.46 | 0.4 | 0 |
| inbound:GameSetUserPosition | 41901 | 0.01 | 0.05 | 0.12 | 2.00 | 20.50 | 1.87 | 20.50 | 9.35 | 57.6 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.11 | 0.36 | 15.00 | 15.00 | 15.00 | 15.00 | 15.00 | 0.47 | 0.2 | 0 |
| inbound:GameLogin | 81** | 0.11 | 0.25 | 10.35 | 10.35 | 10.35 | 10.35 | 10.35 | 0.22 | 0.4 | 0 |
| inbound:GameReturnToSelection | 463* | 0.45 | 1.67 | 2.98 | 10.26 | 10.26 | 10.26 | 10.26 | 3.64 | 1.9 | 0 |
| inbound:GameResponsePeer | 80** | 0.16 | 0.54 | 9.45 | 9.45 | 9.45 | 9.45 | 9.45 | 2.75 | 7.0 | 0 |
| inbound:GameConnectionCheck | 1448632 | 0.00 | 0.00 | 0.01 | 0.03 | 9.18 | 0.11 | 9.18 | 8.23 | 14.6 | 0 |
| event:achievement.inventory | 940* | 0.25 | 0.52 | 2.13 | 7.07 | 7.07 | 7.07 | 7.07 | 5.72 | 0.4 | 0 |
| inbound:GameRequestPeer | 81** | 0.49 | 2.26 | 6.86 | 6.86 | 6.86 | 6.86 | 6.86 | 2.65 | 7.0 | 0 |
| event:quest.inventory | 940* | 0.10 | 0.31 | 0.97 | 6.72 | 6.72 | 5.07 | 6.72 | 2.60 | 14.0 | 0 |
| timer:CreatureClockService | 276* | 0.13 | 0.42 | 0.77 | 6.49 | 6.49 | 6.49 | 6.49 | 1.08 | 1.4 | 0 |
| inbound:GameLeaveParty | 77** | 0.21 | 0.82 | 6.43 | 6.43 | 6.43 | 6.43 | 6.43 | 0.84 | 22.1 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.63 | 5.91 | 5.91 | 5.91 | 5.91 | 5.91 | 5.91 | 0.84 | 60.0 | 0 |
| event:southern-dale.daily-refill-notifications | 484* | 0.41 | 2.13 | 4.56 | 5.89 | 5.89 | 5.12 | 5.53 | 5.89 | 134.1 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.04 | 3.96 | 3.96 | 3.96 | 3.96 | 3.96 | 0.05 | 0.1 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.60 | 3.49 | 3.49 | 3.49 | 3.28 | 3.28 | 3.49 | 274.0 | 0 |
| inbound:GameFinishLoading | 160* | 0.08 | 0.18 | 1.93 | 3.10 | 3.10 | 3.10 | 3.10 | 0.21 | 12.8 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.04 | 0.26 | 2.99 | 2.99 | 2.99 | 2.99 | 2.99 | 0.27 | 0.3 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.68 | 2.91 | 2.91 | 2.91 | 2.91 | 2.91 | 2.91 | 1.76 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1105 | 0.01 | 0.02 | 0.03 | 0.91 | 2.66 | 2.66 | 2.66 | 0.91 | 0.2 | 0 |
| timer:JoustMaintenanceService | 276* | 0.05 | 0.18 | 0.34 | 2.31 | 2.31 | 0.11 | 0.25 | 2.31 | 190.9 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.57 | 2.21 | 2.21 | 2.21 | 2.21 | 2.21 | 2.21 | 0.82 | 32.6 | 0 |
| event:altar.departure | 160* | 0.11 | 0.30 | 1.84 | 1.96 | 1.96 | 1.96 | 1.96 | 1.84 | 16.9 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.09 | 1.84 | 1.84 | 1.84 | 1.84 | 1.84 | 1.84 | 0.50 | 60.0 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.07 | 0.24 | 1.81 | 1.81 | 1.81 | 1.81 | 0.31 | 1.4 | 0 |
| event:tournament.departure | 160* | 0.10 | 0.25 | 1.55 | 1.55 | 1.55 | 0.25 | 0.80 | 1.55 | 258.3 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.18 | 1.46 | 1.46 | 1.46 | 1.46 | 1.46 | 1.46 | 0.50 | 60.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.07 | 1.43 | 1.43 | 1.43 | 1.43 | 1.43 | 1.43 | 0.32 | 60.0 | 0 |
| timer:TournamentRewardSweepService | 5452 | 0.00 | 0.01 | 0.01 | 0.02 | 1.05 | 0.55 | 0.55 | 1.05 | 99.1 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.02 | 1.04 | 1.04 | 1.04 | 1.04 | 0.02 | 1.4 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.10 | 0.87 | 0.87 | 0.87 | 0.87 | 0.87 | 0.87 | 0.11 | 60.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.20 | 0.84 | 0.84 | 0.84 | 0.84 | 0.84 | 0.84 | 0.20 | 60.0 | 0 |
| event:quest.mirror-leave | 160* | 0.11 | 0.21 | 0.71 | 0.82 | 0.82 | 0.82 | 0.82 | 0.44 | 16.9 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.09 | 0.81 | 0.81 | 0.81 | 0.81 | 0.81 | 0.81 | 0.09 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.80 | 0.80 | 0.80 | 0.80 | 0.50 | 1.4 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.08 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.71 | 0.08 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.05 | 0.70 | 0.70 | 0.70 | 0.70 | 0.70 | 0.70 | 0.06 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.06 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.06 | 60.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.01 | 30.0 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.01 | 0.01 | 0.04 | 0.04 | 0.04 | 0.04 | 0.01 | 12.8 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 92.83 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26164 | 0.07 | 55.33 | 102.7 | 413.1 | 921.7 | inbound:GameConnectionCheck | 1.4 |
| 冷启动窗 | 只算 inbound: | 23236 | 0.06 | 54.28 | 100.0 | 377.4 | 921.7 |  |  |
| 稳态窗 | 全部作业 | 52955 | 0.05 | 47.86 | 83.37 | 129.5 | 158.8 | timer:TournamentRewardSweepService | 140.8 |
| 稳态窗 | 只算 inbound: | 48044 | 0.05 | 46.49 | 81.46 | 129.5 | 157.7 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 980* | 0.22 | 59.29 | 90.52 | 100.5 | 100.5 | event:quest.mirror-leave | 202.2 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 693* | 0.33 | 60.58 | 90.52 | 90.54 | 90.54 |  |  |
| (e) 灌包期间 | 全部作业 | 1399059 | 0.00 | 0.00 | 0.01 | 33.91 | 181.2 | inbound:GameConnectionCheck | 267.0 |
| (e) 灌包期间 | 只算 inbound: | 1397909 | 0.00 | 0.00 | 0.01 | 31.85 | 181.2 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 113 | 416.1 | 488.2 | inbound:GameSelectCharacter | 226.8 |
| 1 | 195 | 921.7 | 921.7 | inbound:GameConnectionCheck | 113.0 |
| 2 | 212 | 274.0 | 281.8 | timer:TournamentRewardSweepService | 118.7 |
| 3 | 907 | 72.82 | 73.92 | inbound:GameSetUserPosition | 63.58 |
| 4 | 426 | 64.78 | 83.47 | timer:TournamentRewardSweepService | 110.6 |
| 5 | 462 | 60.57 | 77.42 | timer:TournamentRewardSweepService | 61.20 |
| 6 | 435 | 26.96 | 38.18 | timer:TournamentRewardSweepService | 59.83 |
| 7 | 429 | 76.63 | 97.41 | timer:BossTowerReturnSweepService | 119.6 |
| 8 | 443 | 100.1 | 100.2 | inbound:GameConnectionCheck | 86.59 |
| 9 | 430 | 98.89 | 101.7 | inbound:GameConnectionCheck | 111.7 |
| 10 | 433 | 64.74 | 73.02 | inbound:GameSetUserPosition | 72.56 |
| 11 | 457 | 99.97 | 100.0 | inbound:GameSetUserPosition | 106.8 |
| 12 | 409 | 63.47 | 75.80 | inbound:GameSelectDungeon | 75.78 |
| 13 | 482 | 79.14 | 111.2 | event:quest.inventory | 110.9 |
| 14 | 432 | 36.43 | 66.68 | timer:BossTowerReturnSweepService | 83.03 |
| 15 | 434 | 59.52 | 69.86 | timer:TournamentRewardSweepService | 77.30 |
| 16 | 438 | 138.3 | 168.5 | timer:DungeonPartyWipeService | 103.3 |
| 17 | 448 | 51.75 | 66.37 | timer:TournamentRewardSweepService | 88.08 |
| 18 | 424 | 59.90 | 83.94 | inbound:GameConnectionCheck | 84.40 |
| 19 | 463 | 86.81 | 91.35 | inbound:GameSetUserPosition | 58.96 |
| 20 | 434 | 83.16 | 92.78 | inbound:GameSetUserPosition | 111.2 |
| 21 | 433 | 56.68 | 75.73 | inbound:GameSetUserPosition | 87.73 |
| 22 | 424 | 39.18 | 40.22 | inbound:GameConnectionCheck | 58.75 |
| 23 | 463 | 102.9 | 106.8 | event:quest.inventory | 106.6 |
| 24 | 434 | 22.86 | 66.52 | event:quest.inventory | 66.22 |
| 25 | 466 | 70.38 | 72.81 | inbound:GameSetUserPosition | 76.50 |
| 26 | 437 | 48.22 | 56.65 | timer:DungeonPartyWipeService | 60.03 |
| 27 | 436 | 102.9 | 128.6 | timer:TournamentRewardSweepService | 99.57 |
| 28 | 426 | 43.56 | 64.33 | inbound:GameConnectionCheck | 64.45 |
| 29 | 436 | 51.43 | 63.63 | inbound:GameSetUserPosition | 59.86 |
| 30 | 412 | 43.54 | 62.61 | inbound:GameConnectionCheck | 35.26 |
| 31 | 495 | 73.09 | 114.8 | event:quest.inventory | 114.5 |
| 32 | 432 | 64.20 | 100.6 | inbound:GameConnectionCheck | 71.03 |
| 33 | 422 | 35.69 | 60.03 | timer:TournamentRewardSweepService | 61.07 |
| 34 | 458 | 99.33 | 101.3 | timer:DungeonPartyWipeService | 103.5 |
| 35 | 430 | 138.0 | 138.0 | inbound:GameConnectionCheck | 139.3 |
| 36 | 438 | 88.43 | 98.31 | timer:TournamentRewardSweepService | 103.0 |
| 37 | 472 | 47.17 | 68.13 | event:quest.inventory | 67.88 |
| 38 | 466 | 96.09 | 128.8 | inbound:GameSetUserPosition | 128.9 |
| 39 | 449 | 129.3 | 155.8 | timer:TournamentRewardSweepService | 113.2 |
| 40 | 437 | 74.34 | 113.5 | inbound:GameSetUserPosition | 58.59 |
| 41 | 455 | 74.33 | 84.17 | inbound:GameSetUserPosition | 55.39 |
| 42 | 393 | 107.8 | 110.5 | inbound:GameConnectionCheck | 74.09 |
| 43 | 494 | 76.81 | 94.61 | inbound:GameConnectionCheck | 106.4 |
| 44 | 434 | 54.17 | 58.18 | inbound:GameSetUserArea | 59.10 |
| 45 | 426 | 87.57 | 104.3 | inbound:GameConnectionCheck | 58.77 |
| 46 | 461 | 137.0 | 137.0 | inbound:GameConnectionCheck | 112.6 |
| 47 | 433 | 55.46 | 57.97 | inbound:GameSetUserPosition | 69.76 |
| 48 | 419 | 84.05 | 102.2 | inbound:GameSetUserPosition | 107.3 |
| 49 | 473 | 78.02 | 109.2 | event:quest.inventory | 108.9 |
| 50 | 451 | 55.37 | 69.21 | event:quest.inventory | 86.36 |
| 51 | 389 | 47.58 | 57.92 | inbound:GameSetUserPosition | 69.46 |
| 52 | 480 | 60.90 | 62.16 | inbound:GameConnectionCheck | 62.75 |
| 53 | 432 | 40.15 | 59.73 | inbound:GameSetUserArea | 43.86 |
| 54 | 433 | 68.69 | 93.83 | timer:TournamentRewardSweepService | 116.2 |
| 55 | 456 | 76.24 | 97.34 | timer:TournamentRewardSweepService | 108.0 |
| 56 | 448 | 81.65 | 99.26 | inbound:GameSetUserPosition | 99.82 |
| 57 | 426 | 82.14 | 82.14 | inbound:GameConnectionCheck | 87.95 |
| 58 | 415 | 102.7 | 102.7 | inbound:GameConnectionCheck | 103.8 |
| 59 | 474 | 116.4 | 130.7 | timer:JoustMaintenanceService | 133.3 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26164 | 25301 | 42.2% | inbound:GameSetUserArea 12814 ms／630／max 58.44；inbound:GameSelectCharacter 10405 ms／132／max 226.8；inbound:GameEplpCommand 517 ms／15／max 59.98；inbound:GameEnterSelectDungeon 301 ms／18／max 25.76；inbound:GameSelectDungeon 272 ms／18／max 75.78；inbound:GameSetUserPosition 239 ms／8756／max 20.50；event:achievement.inventory 146 ms／390／max 7.07；inbound:GameCreateCharacter 123 ms／39／max 35.19 |
| 稳态窗 | 120.0 | 52955 | 46339 | 38.6% | inbound:GameSetUserArea 27548 ms／1143／max 62.54；inbound:GameSelectCharacter 14831 ms／197／max 134.1；inbound:GameEplpCommand 1540 ms／35／max 77.35；inbound:GameEnterSelectDungeon 906 ms／35／max 57.51；inbound:GameSetUserPosition 437 ms／18484／max 9.35；inbound:GameSelectDungeon 362 ms／35／max 16.43；inbound:GameConnectionCheck 132 ms／27704／max 2.90；inbound:GameReturnToSelection 123 ms／197／max 3.64 |
| (d) 10 并发登录的轮内 | 1.4 | 980 | 763 | 54.3% | inbound:GameSetUserArea 478 ms／16／max 52.31；inbound:GameSelectCharacter 150 ms／2／max 85.14；inbound:GameEplpCommand 109 ms／1／max 109.2；http:POST /api/launcher/v1/register 7 ms／60／max 0.47；inbound:GameLogin 6 ms／60／max 0.22；http:POST /api/launcher/v1/login 3 ms／60／max 0.27；inbound:GameSetUserPosition 2 ms／204／max 0.05；event:achievement.inventory 2 ms／6／max 0.56 |
| (e) 灌包期间 | 30.0 | 1399059 | 16400 | 54.6% | inbound:GameSetUserArea 7169 ms／272／max 57.97；inbound:GameConnectionCheck 4208 ms／1392846／max 8.23；inbound:GameSelectCharacter 3950 ms／51／max 122.8；inbound:GameEplpCommand 559 ms／10／max 74.43；inbound:GameEnterSelectDungeon 208 ms／8／max 34.06；inbound:GameSetUserPosition 106 ms／4616／max 2.23；inbound:GameSelectDungeon 78 ms／8／max 10.47；inbound:GameReturnToSelection 33 ms／51／max 2.92 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.4，`timer:` 31.7，`event:` 9.2。

