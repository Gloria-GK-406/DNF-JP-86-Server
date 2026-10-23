# 基线测量 — m3ad1（合成负载）

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
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：399.9 包/秒（47994 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 25551 | 53.24% |
| SET_USER_POSITION | 18471 | 38.49% |
| STEP_FENCE | 2125 | 4.43% |
| SET_USER_AREA | 1140 | 2.38% |
| RETURN_SELECT_CHARACTER | 197 | 0.41% |
| SELECT_CHARACTER | 197 | 0.41% |
| FINISH_LOADING | 68 | 0.14% |
| LEAVE_PARTY | 37 | 0.08% |
| REQUEST_PEER | 36 | 0.08% |
| RESPONSE_PEER | 36 | 0.08% |
| CREATE_CHARACTER | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 34 | 0.07% |
| SELECT_DUNGEON | 34 | 0.07% |
| EPLP | 33 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 25555 | 1.45 | 58.55 | 97.69 | 137.2 | 199.0 | 0 | 94.26 | 129.5 | 197.9 |
| SET_USER_POSITION | 18472 | 1.42 | 59.06 | 98.96 | 143.7 | 199.2 | 0 | 95.28 | 135.9 | 197.9 |
| SET_USER_AREA | 1140 | 28.34 | 81.72 | 119.0 | 141.5 | 194.6 | 0 | 118.5 | 140.6 | 193.8 |
| RETURN_SELECT_CHARACTER | 197* | 1.48 | 55.16 | 107.1 | 152.2 | 152.2 | 0 | 106.7 | 152.2 | 152.2 |
| SELECT_CHARACTER | 197* | 69.48 | 123.3 | 172.5 | 179.4 | 179.4 | 0 | 172.5 | 179.4 | 179.4 |
| FINISH_LOADING | 68** | 0.18 | 0.28 | 0.39 | 0.39 | 0.39 | 0 | 0.39 | 0.39 | 0.39 |
| LEAVE_PARTY | 37** | 0.46 | 47.28 | 71.95 | 71.95 | 71.95 | 0 | 71.95 | 71.95 | 71.95 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.34 | 10.88 | 20.31 | 20.31 | 20.31 | 0 | 20.31 | 20.31 | 20.31 |
| CREATE_CHARACTER | 35** | 3.29 | 18.65 | 126.0 | 126.0 | 126.0 | 0 | 126.0 | 126.0 | 126.0 |
| ENTER_SELECT_DUNGEON | 34** | 26.93 | 105.4 | 113.5 | 113.5 | 113.5 | 0 | 113.5 | 113.5 | 113.5 |
| SELECT_DUNGEON | 34** | 10.48 | 117.7 | 120.7 | 120.7 | 120.7 | 0 | 120.7 | 120.7 | 120.7 |
| EPLP | 33** | 48.67 | 136.1 | 160.8 | 160.8 | 160.8 | 0 | 135.8 | 135.8 | 135.8 |
| **全部计入的请求** | 45838 | 1.51 | 61.15 | 101.8 | 146.5 | 199.2 | 36 | 97.57 | 137.2 | 197.9 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：45874 | 0.48 | 8.21 | 47.76 | 96.53 | 149.2（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RefillOnlineSouthernTicketCommand | Transactional | 485* | 14.65 | 53.31 | 60.51 | 134.2 | 134.2 | 19.0 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 485* | 3.69 | 9.42 | 13.47 | 101.8 | 101.8 | 0.5 | 0 |
| EnterDungeonCommand | Transactional | 80** | 6.19 | 8.71 | 69.53 | 69.53 | 69.53 | 10.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 485* | 12.81 | 26.75 | 56.98 | 63.11 | 63.11 | 2.0 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 485* | 17.52 | 50.82 | 60.66 | 62.73 | 62.73 | 204.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 485* | 15.13 | 46.73 | 60.38 | 62.29 | 62.29 | 266.3 | 0 |
| CreateCharacterCommand | Transactional | 95** | 1.21 | 2.79 | 31.63 | 31.63 | 31.63 | 0.4 | 0 |
| LeaveDungeonCommand | Direct | 79** | 0.90 | 2.15 | 13.96 | 13.96 | 13.96 | 15.8 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.08 | 0.34 | 8.17 | 8.17 | 8.17 | 0.2 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 566* | 0.01 | 0.02 | 0.03 | 7.85 | 7.85 | 235.2 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.04 | 0.17 | 7.82 | 7.82 | 7.82 | 0.3 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 464* | 0.17 | 0.78 | 1.12 | 6.49 | 6.49 | 2.1 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 464* | 0.19 | 0.56 | 1.40 | 5.99 | 5.99 | 198.4 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 485* | 0.04 | 0.06 | 0.08 | 5.62 | 5.62 | 237.0 | 0 |
| GetAccountCharacterRosterCommand | Direct | 95** | 0.29 | 1.04 | 5.44 | 5.44 | 5.44 | 0.4 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 485* | 0.04 | 0.07 | 0.11 | 3.93 | 3.93 | 0.6 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 485* | 0.01 | 0.03 | 0.05 | 3.78 | 3.78 | 0.6 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 485* | 0.04 | 0.09 | 0.14 | 3.72 | 3.72 | 0.4 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 485* | 0.06 | 0.15 | 0.23 | 3.60 | 3.60 | 0.4 | 0 |
| AdvanceCreatureClockCommand | Transactional | 772* | 0.03 | 0.11 | 0.24 | 3.55 | 3.55 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 530* | 0.04 | 0.06 | 0.09 | 2.99 | 2.99 | 0.7 | 0 |
| RequestPartyInviteCommand | Direct | 80** | 0.41 | 1.18 | 2.80 | 2.80 | 2.80 | 48.9 | 0 |
| AcceptPartyInviteCommand | Direct | 80** | 0.01 | 0.13 | 2.55 | 2.55 | 2.55 | 6.5 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 485* | 0.05 | 0.13 | 0.23 | 1.93 | 1.93 | 0.4 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.01 | 0.03 | 1.64 | 1.64 | 1.64 | 0.3 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.01 | 0.05 | 0.99 | 1.25 | 1.25 | 130.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160* | 0.01 | 0.02 | 0.62 | 1.08 | 1.08 | 266.1 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.01 | 1.04 | 1.04 | 1.04 | 1.04 | 60.0 | 0 |
| LeavePartyCommand | Direct | 76** | 0.01 | 0.03 | 1.02 | 1.02 | 1.02 | 20.8 | 0 |
| InitializeHousingCommand | Transactional | 485* | 0.02 | 0.06 | 0.09 | 0.72 | 0.72 | 0.4 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.02 | 0.03 | 0.34 | 0.34 | 0.34 | 60.0 | 0 |

命令日志行 9797 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**944.8 ms**（SET_USER_POSITION，计划发出 t=2.21 s，solo02；参考口径 126.1 ms）。冷启动窗整体：21978 | 1.58 | 81.43 | 246.8 | 780.7 | 944.8（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 27 | 577.5 | 577.5 | CREATE_CHARACTER |
| 1 | 88 | 748.0 | 748.0 | SET_USER_POSITION |
| 2 | 204 | 917.3 | 944.8 | SET_USER_POSITION |
| 3 | 371 | 219.1 | 268.6 | CHECK_USER_CONNECTION |
| 4 | 392 | 66.58 | 93.94 | SET_USER_AREA |
| 5 | 381 | 110.1 | 123.9 | SELECT_CHARACTER |
| 6 | 383 | 60.65 | 69.18 | SET_USER_AREA |
| 7 | 375 | 93.26 | 113.5 | SELECT_CHARACTER |
| 8 | 372 | 108.7 | 121.3 | SELECT_CHARACTER |
| 9 | 377 | 79.15 | 79.79 | SET_USER_AREA |
| 10 | 379 | 115.6 | 143.5 | SELECT_DUNGEON |
| 11 | 382 | 116.9 | 131.0 | SET_USER_AREA |
| 12 | 362 | 36.79 | 65.21 | SELECT_CHARACTER |
| 13 | 389 | 106.2 | 134.4 | SELECT_CHARACTER |
| 14 | 376 | 82.35 | 93.59 | SELECT_CHARACTER |
| 15 | 384 | 105.8 | 110.7 | SELECT_CHARACTER |
| 16 | 384 | 86.56 | 86.96 | CHECK_USER_CONNECTION |
| 17 | 382 | 55.25 | 72.12 | SELECT_CHARACTER |
| 18 | 380 | 246.3 | 273.2 | SELECT_CHARACTER |
| 19 | 387 | 144.7 | 154.1 | SET_USER_AREA |
| 20 | 386 | 113.4 | 125.2 | SELECT_CHARACTER |
| 21 | 388 | 87.74 | 90.18 | CHECK_USER_CONNECTION |
| 22 | 387 | 49.27 | 59.63 | SELECT_CHARACTER |
| 23 | 384 | 94.11 | 107.4 | SELECT_CHARACTER |
| 24 | 378 | 56.10 | 64.98 | SELECT_CHARACTER |
| 25 | 382 | 95.54 | 109.5 | SET_USER_POSITION |
| 26 | 384 | 66.04 | 75.57 | CHECK_USER_CONNECTION |
| 27 | 388 | 46.90 | 47.40 | SET_USER_AREA |
| 28 | 368 | 72.04 | 85.33 | SELECT_CHARACTER |
| 29 | 381 | 58.86 | 77.48 | SET_USER_AREA |
| 30 | 389 | 58.54 | 72.32 | SET_USER_POSITION |
| 31 | 373 | 104.7 | 121.0 | SET_USER_AREA |
| 32 | 379 | 64.10 | 78.49 | SELECT_CHARACTER |
| 33 | 368 | 77.93 | 102.7 | SELECT_CHARACTER |
| 34 | 364 | 102.5 | 104.1 | FINISH_LOADING |
| 35 | 380 | 125.8 | 136.0 | SELECT_CHARACTER |
| 36 | 392 | 27.48 | 34.99 | EPLP |
| 37 | 380 | 81.31 | 82.58 | CHECK_USER_CONNECTION |
| 38 | 388 | 39.81 | 60.57 | EPLP |
| 39 | 370 | 142.7 | 153.5 | SET_USER_AREA |
| 40 | 383 | 73.25 | 73.58 | SET_USER_POSITION |
| 41 | 385 | 88.64 | 102.6 | SET_USER_AREA |
| 42 | 385 | 82.43 | 85.72 | SELECT_CHARACTER |
| 43 | 383 | 116.5 | 124.9 | SET_USER_AREA |
| 44 | 371 | 76.75 | 79.84 | CHECK_USER_CONNECTION |
| 45 | 383 | 103.9 | 114.9 | SET_USER_AREA |
| 46 | 372 | 167.2 | 167.7 | SET_USER_POSITION |
| 47 | 379 | 63.22 | 63.34 | CHECK_USER_CONNECTION |
| 48 | 387 | 81.84 | 110.7 | SELECT_CHARACTER |
| 49 | 372 | 150.9 | 170.6 | SELECT_CHARACTER |
| 50 | 385 | 67.81 | 73.30 | SELECT_CHARACTER |
| 51 | 382 | 78.94 | 94.29 | SET_USER_AREA |
| 52 | 369 | 86.60 | 105.5 | SELECT_CHARACTER |
| 53 | 370 | 50.54 | 52.59 | ENTER_SELECT_DUNGEON |
| 54 | 375 | 86.08 | 112.0 | SELECT_CHARACTER |
| 55 | 383 | 57.65 | 71.61 | SET_USER_AREA |
| 56 | 385 | 113.1 | 115.1 | SELECT_CHARACTER |
| 57 | 393 | 96.14 | 108.8 | SELECT_CHARACTER |
| 58 | 367 | 110.6 | 135.4 | SELECT_CHARACTER |
| 59 | 385 | 60.31 | 62.82 | SELECT_CHARACTER |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 45838 | 1.51 | 61.15 | 101.8 | 146.5 | 199.2 |
| 10 并发登录的轮内 | 539* | 38.59 | 138.7 | 164.6 | 174.8 | 174.8 |
| （参考：实际发出口径）稳态窗 | 45838 | 0.29 | 54.55 | 97.57 | 137.2 | 197.9 |
| （参考：实际发出口径）轮内 | 539* | 14.29 | 93.74 | 155.0 | 155.2 | 155.2 |
| 登录阶段整体（182–232 s） | 19212 | 1.72 | 73.41 | 119.7 | 161.5 | 210.7 |

每轮时长（开始 → 10 个 LOGIN 都应答）：228, 205, 301, 254, 205, 253 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 17630 | 0.84 | 61.45 | 103.2 | 143.3 | 196.5 | 102.2 |
| 单人 | 轮内 | 208* | 35.14 | 135.3 | 156.7 | 168.2 | 168.2 | 128.7 |
| 双人组领队 | 稳态窗 | 14086 | 1.68 | 60.78 | 100.3 | 149.1 | 199.2 | 95.77 |
| 双人组领队 | 轮内 | 166* | 39.95 | 140.8 | 158.2 | 167.0 | 167.0 | 155.1 |
| 双人组队员 | 稳态窗 | 14122 | 1.67 | 60.68 | 100.1 | 148.5 | 199.0 | 95.31 |
| 双人组队员 | 轮内 | 165* | 40.78 | 139.2 | 170.2 | 174.8 | 174.8 | 128.7 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 113.8 | 170.2 | 180.1 | 180.1 | 180.1 |
| 启动器登录（HTTP，含校验） | 60** | 92.05 | 130.6 | 131.0 | 131.0 | 131.0 |
| Game LOGIN 0x0001 | 60** | 0.39 | 29.00 | 109.8 | 109.8 | 109.8 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 44806 包/秒（1352905 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 45838 | 1.51 | 61.15 | 101.8 | 146.5 | 199.2 |
| 其余连接，灌包期间 | 11544 | 1.48 | 69.18 | 108.8 | 167.8 | 185.8 |
| （参考：实际发出口径）其余连接，灌包期间 | 11544 | 0.23 | 59.02 | 105.9 | 166.2 | 185.8 |
| 灌包连接自身（含其窗口内排队） | 1352936 | 0.36 | 0.55 | 2.20 | 73.15 | 480.6 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 484 | 40480 | selection after create | 234.6 |
| EPLP | 同伴 | 79 | 7518 | leave dungeon | 194.0 |
| SET_USER_AREA | 请求者 | 2658 | 6802 | town | 30.8 |
| EPLP | 请求者 | 79 | 5810 | leave dungeon | 228.6 |
| SET_USER_AREA | 同伴 | 6 | 4798 | town | 3.0 |
| LEAVE_PARTY | 同伴 | 76 | 3266 | leave party | 124.9 |
| LEAVE_PARTY | 请求者 | 76 | 3099 | leave party | 124.9 |
| REQUEST_PEER | 同伴 | 80 | 3076 | party | 110.7 |
| RETURN_SELECT_CHARACTER | 请求者 | 464 | 2399 | return to selection | 185.9 |
| REQUEST_PEER | 请求者 | 80 | 1937 | party | 110.7 |
| RESPONSE_PEER | 同伴 | 80 | 1555 | party | 233.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 1522 | party dungeon entry | 202.0 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 1522 | party dungeon entry | 202.0 |
| RESPONSE_PEER | 请求者 | 80 | 1498 | party | 197.3 |
| CREATE_CHARACTER | 请求者 | 95 | 1414 | create | 274.1 |
| SELECT_DUNGEON | 请求者 | 80 | 1392 | party dungeon entry | 119.9 |
| SELECT_DUNGEON | 同伴 | 80 | 1392 | party dungeon entry | 119.9 |
| LOGIN | 请求者 | 21 | 339 | login | 0.3 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 10.9 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 10.9 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 101.8 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 164.6 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 108.8 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 2 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 944.8 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `RefillOnlineSouthernTicketCommand`：样本 485，p99.9 134.2 ms，max 134.2 ms（出现于 t=19.0 s；冷启动窗内 max 134.2 ms，之后 max 62.97 ms）
- `PrepareSelectedCharacterStateCommand`：样本 485，p99.9 101.8 ms，max 101.8 ms（出现于 t=0.5 s；冷启动窗内 max 101.8 ms，之后 max 30.80 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 23 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：209 个；截尾样本（超时或连接被关闭）23 个，协议无回包 80 个。
- SET_USER_POSITION：99 个（其中截尾 0），最大 945 ms，计划发出时刻 1.2, 1.2, 1.2, 1.3, 1.3, 1.3, 1.3, 1.4, 1.4, 1.5, 1.6, 1.6 … s
- CHECK_USER_CONNECTION：86 个（其中截尾 0），最大 943 ms，计划发出时刻 1.2, 1.2, 1.2, 1.3, 1.3, 1.4, 1.4, 1.4, 1.5, 1.5, 1.5, 1.6 … s
- SELECT_CHARACTER：11 个（其中截尾 0），最大 666 ms，计划发出时刻 0.4, 0.4, 0.4, 0.4, 1.1, 1.1, 1.1, 1.2, 1.2, 1.2, 18.8 s
- SET_USER_AREA：9 个（其中截尾 0），最大 917 ms，计划发出时刻 1.4, 1.5, 1.6, 1.8, 2.7, 2.7, 2.1, 2.8, 2.7 s
- RETURN_SELECT_CHARACTER：3 个（其中截尾 0），最大 569 ms，计划发出时刻 1.7, 2.3, 2.4 s
- CREATE_CHARACTER：1 个（其中截尾 0），最大 578 ms，计划发出时刻 0.4 s

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 1473933 行（Host 就绪之后 1473927 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 485* | 68.95 | 121.9 | 137.0 | 269.3 | 269.3 | 235.9 | 269.3 | 151.7 | 19.0 | 0 |
| inbound:GameSetUserArea | 2659 | 23.51 | 40.81 | 54.18 | 87.27 | 111.4 | 13.28 | 53.94 | 111.4 | 232.1 | 0 |
| inbound:GameEplpCommand | 79** | 45.66 | 72.64 | 91.70 | 91.70 | 91.70 | 46.95 | 76.62 | 91.70 | 155.1 | 0 |
| inbound:GameSelectDungeon | 80** | 10.28 | 15.66 | 85.66 | 85.66 | 85.66 | 85.66 | 85.66 | 19.07 | 10.9 | 0 |
| inbound:GameEnterSelectDungeon | 80** | 22.53 | 34.56 | 42.72 | 42.72 | 42.72 | 34.24 | 34.24 | 42.72 | 181.8 | 0 |
| inbound:GameCreateCharacter | 95** | 1.78 | 4.25 | 39.24 | 39.24 | 39.24 | 39.24 | 39.24 | 4.32 | 0.4 | 0 |
| inbound:GameConnectionCheck | 1415894 | 0.00 | 0.00 | 0.01 | 0.04 | 26.31 | 0.10 | 26.31 | 6.04 | 19.7 | 0 |
| inbound:GameSetUserPosition | 41751 | 0.01 | 0.06 | 0.12 | 2.17 | 22.88 | 1.84 | 22.88 | 8.01 | 50.0 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.10 | 0.55 | 13.29 | 13.29 | 13.29 | 13.29 | 13.29 | 0.43 | 0.2 | 0 |
| inbound:GameReturnToSelection | 464* | 0.47 | 1.60 | 2.76 | 9.97 | 9.97 | 9.97 | 9.97 | 6.25 | 2.1 | 0 |
| inbound:GameLogin | 81** | 0.11 | 0.29 | 9.67 | 9.67 | 9.67 | 9.67 | 9.67 | 0.28 | 0.3 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.06 | 0.25 | 9.46 | 9.46 | 9.46 | 9.46 | 9.46 | 0.23 | 0.3 | 0 |
| inbound:GameLeaveParty | 76** | 0.20 | 0.79 | 8.54 | 8.54 | 8.54 | 8.54 | 8.54 | 3.21 | 20.8 | 0 |
| inbound:GameResponsePeer | 80** | 0.19 | 0.62 | 8.41 | 8.41 | 8.41 | 8.41 | 8.41 | 0.80 | 6.5 | 0 |
| event:achievement.inventory | 950* | 0.26 | 0.67 | 2.00 | 8.20 | 8.20 | 7.85 | 7.85 | 8.20 | 228.9 | 0 |
| timer:CreatureClockService | 276* | 0.14 | 0.40 | 0.77 | 6.76 | 6.76 | 6.76 | 6.76 | 1.61 | 1.0 | 0 |
| event:tournament.departure | 160* | 0.10 | 0.18 | 1.68 | 6.17 | 6.17 | 0.18 | 1.38 | 6.17 | 264.0 | 0 |
| event:quest.inventory | 950* | 0.10 | 0.31 | 0.77 | 6.13 | 6.13 | 6.13 | 6.13 | 2.62 | 0.4 | 0 |
| event:southern-dale.daily-refill-notifications | 485* | 0.43 | 2.39 | 5.92 | 6.01 | 6.01 | 5.49 | 5.49 | 6.01 | 258.6 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.03 | 5.82 | 5.82 | 5.82 | 5.82 | 5.82 | 0.03 | 0.1 | 0 |
| timer:PremiumDailyRefreshService | 4** | 0.52 | 4.76 | 4.76 | 4.76 | 4.76 | 4.76 | 4.76 | 0.74 | 60.0 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 1.10 | 4.76 | 4.76 | 4.76 | 4.76 | 4.76 | 4.76 | 1.91 | 10.2 | 0 |
| inbound:GameRequestPeer | 80** | 0.50 | 1.54 | 4.73 | 4.73 | 4.73 | 4.19 | 4.73 | 3.19 | 7.3 | 0 |
| inbound:GameFinishLoading | 160* | 0.11 | 0.20 | 1.69 | 3.55 | 3.55 | 3.55 | 3.55 | 1.16 | 10.9 | 0 |
| event:altar.departure | 160* | 0.13 | 0.29 | 1.42 | 1.91 | 1.91 | 1.91 | 1.91 | 1.42 | 15.9 | 0 |
| timer:MarriageWeddingScheduler | 276* | 0.02 | 0.06 | 0.17 | 1.86 | 1.86 | 1.86 | 1.86 | 0.18 | 1.0 | 0 |
| event:quest.mirror-leave | 160* | 0.11 | 0.22 | 0.89 | 1.80 | 1.80 | 0.76 | 0.89 | 1.80 | 162.4 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 0.28 | 1.78 | 1.78 | 1.78 | 1.78 | 1.78 | 1.52 | 0.1 | 0 |
| timer:WeaknessRecoveryService | 4** | 0.32 | 1.78 | 1.78 | 1.78 | 1.78 | 1.78 | 1.78 | 1.57 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.05 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 1.51 | 0.06 | 60.0 | 0 |
| timer:DungeonPartyWipeService | 1105 | 0.01 | 0.03 | 0.05 | 0.43 | 1.30 | 1.30 | 1.30 | 0.06 | 0.2 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.15 | 1.12 | 1.12 | 1.12 | 1.12 | 1.12 | 1.12 | 0.39 | 60.0 | 0 |
| timer:BossTowerReturnSweepService | 553* | 0.00 | 0.02 | 0.02 | 1.04 | 1.04 | 1.04 | 1.04 | 0.02 | 1.0 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.08 | 1.04 | 1.04 | 1.04 | 1.04 | 1.04 | 1.04 | 0.27 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 553* | 0.00 | 0.01 | 0.02 | 0.78 | 0.78 | 0.78 | 0.78 | 0.03 | 1.0 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.16 | 0.73 | 0.73 | 0.73 | 0.73 | 0.73 | 0.73 | 0.17 | 60.0 | 0 |
| timer:JoustMaintenanceService | 276* | 0.07 | 0.20 | 0.52 | 0.66 | 0.66 | 0.12 | 0.66 | 0.52 | 23.9 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.06 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.63 | 0.07 | 60.0 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.08 | 0.62 | 0.62 | 0.62 | 0.62 | 0.62 | 0.62 | 0.09 | 60.0 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.07 | 0.56 | 0.56 | 0.56 | 0.56 | 0.56 | 0.56 | 0.08 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.11 | 0.54 | 0.54 | 0.54 | 0.54 | 0.54 | 0.54 | 0.49 | 60.0 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.04 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.47 | 0.04 | 60.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.01 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0.02 | 29.9 | 0 |
| timer:TournamentRewardSweepService | 5416 | 0.00 | 0.01 | 0.01 | 0.03 | 0.05 | 0.02 | 0.05 | 0.05 | 167.5 | 0 |
| timer:RoomMechanismMaintenanceService | 160* | 0.00 | 0.01 | 0.03 | 0.04 | 0.04 | 0.04 | 0.04 | 0.03 | 10.9 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 101.8 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 26147 | 0.07 | 60.61 | 112.0 | 522.0 | 664.5 | event:achievement.inventory | 1.8 |
| 冷启动窗 | 只算 inbound: | 23230 | 0.06 | 56.83 | 107.2 | 348.5 | 603.4 |  |  |
| 稳态窗 | 全部作业 | 52893 | 0.05 | 50.62 | 93.98 | 135.4 | 190.8 | inbound:GameSetUserPosition | 175.2 |
| 稳态窗 | 只算 inbound: | 48038 | 0.05 | 47.65 | 92.49 | 128.6 | 190.8 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 1080 | 0.53 | 106.1 | 136.9 | 152.0 | 153.0 | event:southern-dale.daily-refill-notifications | 232.1 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 772* | 0.57 | 107.0 | 120.7 | 139.2 | 139.2 |  |  |
| (e) 灌包期间 | 全部作业 | 1366177 | 0.00 | 0.00 | 0.01 | 37.01 | 182.2 | inbound:GameConnectionCheck | 259.0 |
| (e) 灌包期间 | 只算 inbound: | 1365015 | 0.00 | 0.00 | 0.01 | 32.16 | 182.2 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 99 | 505.5 | 505.5 | inbound:GameSelectCharacter | 235.9 |
| 1 | 150 | 663.9 | 664.5 | event:achievement.inventory | 118.0 |
| 2 | 230 | 295.0 | 295.0 | inbound:GameConnectionCheck | 121.5 |
| 3 | 949 | 72.88 | 126.2 | inbound:GameConnectionCheck | 61.61 |
| 4 | 406 | 36.02 | 46.63 | inbound:GameSetUserPosition | 35.86 |
| 5 | 455 | 66.03 | 88.54 | timer:TournamentRewardSweepService | 65.80 |
| 6 | 457 | 123.0 | 123.1 | timer:IllusionTowerReturnSweepService | 123.8 |
| 7 | 439 | 86.65 | 110.9 | timer:TournamentRewardSweepService | 113.4 |
| 8 | 384 | 50.45 | 50.47 | inbound:GameSetUserPosition | 64.74 |
| 9 | 478 | 107.2 | 107.3 | inbound:GameConnectionCheck | 121.1 |
| 10 | 436 | 113.3 | 142.5 | inbound:GameConnectionCheck | 85.66 |
| 11 | 444 | 111.0 | 111.0 | inbound:GameConnectionCheck | 110.8 |
| 12 | 447 | 30.63 | 61.93 | timer:TournamentRewardSweepService | 65.05 |
| 13 | 460 | 104.7 | 129.3 | timer:TournamentRewardSweepService | 134.3 |
| 14 | 425 | 81.24 | 81.25 | inbound:GameConnectionCheck | 93.44 |
| 15 | 433 | 59.20 | 62.32 | inbound:GameReturnToSelection | 78.30 |
| 16 | 439 | 101.0 | 101.6 | inbound:GameConnectionCheck | 110.6 |
| 17 | 442 | 52.50 | 69.91 | timer:TournamentRewardSweepService | 71.98 |
| 18 | 354 | 68.75 | 74.92 | inbound:GameSetUserPosition | 79.85 |
| 19 | 535 | 237.6 | 266.5 | timer:TournamentRewardSweepService | 269.3 |
| 20 | 427 | 105.9 | 106.0 | inbound:GameSetUserPosition | 125.1 |
| 21 | 439 | 95.57 | 106.5 | inbound:GameConnectionCheck | 107.7 |
| 22 | 430 | 46.73 | 54.98 | timer:TournamentRewardSweepService | 59.49 |
| 23 | 445 | 86.99 | 87.05 | inbound:GameSetUserPosition | 107.3 |
| 24 | 459 | 50.82 | 64.74 | event:quest.inventory | 64.45 |
| 25 | 458 | 93.95 | 108.8 | inbound:GameSetUserPosition | 107.4 |
| 26 | 408 | 65.55 | 69.16 | inbound:GameConnectionCheck | 63.71 |
| 27 | 456 | 46.01 | 57.32 | inbound:GameConnectionCheck | 47.20 |
| 28 | 434 | 67.42 | 79.57 | event:southern-dale.daily-refill-notifications | 84.95 |
| 29 | 419 | 57.00 | 76.01 | inbound:GameSetUserPosition | 69.66 |
| 30 | 441 | 56.68 | 71.59 | inbound:GameSetUserPosition | 42.64 |
| 31 | 425 | 104.4 | 117.1 | timer:TournamentRewardSweepService | 107.5 |
| 32 | 487 | 62.61 | 69.21 | timer:TournamentRewardSweepService | 65.72 |
| 33 | 435 | 75.66 | 102.5 | inbound:GameConnectionCheck | 102.5 |
| 34 | 411 | 101.8 | 104.0 | inbound:GameFinishLoading | 104.0 |
| 35 | 460 | 125.5 | 125.9 | timer:TournamentRewardSweepService | 135.8 |
| 36 | 437 | 26.69 | 32.12 | inbound:GameConnectionCheck | 33.06 |
| 37 | 475 | 75.07 | 81.88 | inbound:GameConnectionCheck | 80.82 |
| 38 | 433 | 32.10 | 32.12 | inbound:GameSetUserPosition | 35.66 |
| 39 | 486 | 133.5 | 141.0 | timer:BossTowerReturnSweepService | 110.7 |
| 40 | 444 | 71.21 | 71.39 | inbound:GameSetUserPosition | 69.94 |
| 41 | 438 | 84.02 | 84.06 | inbound:GameConnectionCheck | 69.33 |
| 42 | 412 | 70.52 | 70.53 | inbound:GameSetUserPosition | 85.60 |
| 43 | 415 | 68.42 | 73.69 | inbound:GameSetUserPosition | 55.67 |
| 44 | 495 | 108.6 | 122.3 | timer:TournamentRewardSweepService | 110.1 |
| 45 | 434 | 89.84 | 111.4 | timer:TournamentRewardSweepService | 103.7 |
| 46 | 443 | 161.5 | 161.6 | inbound:GameConnectionCheck | 103.1 |
| 47 | 445 | 57.51 | 81.16 | inbound:GameSetUserPosition | 60.02 |
| 48 | 412 | 79.74 | 109.8 | inbound:GameConnectionCheck | 110.5 |
| 49 | 451 | 149.3 | 170.0 | timer:TournamentRewardSweepService | 113.6 |
| 50 | 454 | 64.40 | 72.47 | event:quest.inventory | 72.15 |
| 51 | 438 | 71.80 | 89.73 | timer:TournamentRewardSweepService | 78.38 |
| 52 | 449 | 86.04 | 103.2 | inbound:GameConnectionCheck | 103.9 |
| 53 | 436 | 28.13 | 51.48 | inbound:GameSetUserPosition | 42.93 |
| 54 | 455 | 83.73 | 111.8 | event:quest.inventory | 111.4 |
| 55 | 410 | 28.10 | 31.78 | inbound:GameSetUserArea | 28.95 |
| 56 | 452 | 113.0 | 113.0 | inbound:GameConnectionCheck | 114.9 |
| 57 | 434 | 94.54 | 96.04 | inbound:GameConnectionCheck | 108.7 |
| 58 | 427 | 59.37 | 65.77 | inbound:GameConnectionCheck | 72.11 |
| 59 | 476 | 109.2 | 134.2 | timer:TournamentRewardSweepService | 104.3 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 26147 | 25904 | 43.2% | inbound:GameSetUserArea 12874 ms／630／max 53.94；inbound:GameSelectCharacter 10768 ms／132／max 269.3；inbound:GameEplpCommand 633 ms／16／max 76.62；inbound:GameEnterSelectDungeon 310 ms／18／max 34.24；inbound:GameSelectDungeon 284 ms／18／max 85.66；inbound:GameSetUserPosition 239 ms／8644／max 22.88；event:achievement.inventory 147 ms／390／max 7.85；inbound:GameCreateCharacter 121 ms／39／max 39.24 |
| 稳态窗 | 120.0 | 52893 | 46847 | 39.0% | inbound:GameSetUserArea 27804 ms／1140／max 87.27；inbound:GameSelectCharacter 15181 ms／196／max 137.0；inbound:GameEplpCommand 1542 ms／33／max 91.70；inbound:GameEnterSelectDungeon 774 ms／34／max 34.67；inbound:GameSetUserPosition 437 ms／18465／max 8.01；inbound:GameSelectDungeon 358 ms／34／max 16.22；inbound:GameConnectionCheck 146 ms／27727／max 2.28；inbound:GameReturnToSelection 131 ms／197／max 2.94 |
| (d) 10 并发登录的轮内 | 1.4 | 1080 | 1171 | 81.0% | inbound:GameSetUserArea 675 ms／15／max 111.4；inbound:GameSelectCharacter 358 ms／3／max 151.7；inbound:GameEplpCommand 63 ms／1／max 62.80；inbound:GameEnterSelectDungeon 26 ms／1／max 26.26；inbound:GameSelectDungeon 14 ms／1／max 14.19；inbound:GameSetUserPosition 7 ms／272／max 2.20；http:POST /api/launcher/v1/register 7 ms／60／max 0.43；inbound:GameLogin 6 ms／60／max 0.28 |
| (e) 灌包期间 | 30.2 | 1366177 | 16874 | 55.9% | inbound:GameSetUserArea 7488 ms／276／max 55.73；inbound:GameSelectCharacter 4178 ms／51／max 124.8；inbound:GameConnectionCheck 4080 ms／1360018／max 6.04；inbound:GameEplpCommand 550 ms／11／max 66.17；inbound:GameEnterSelectDungeon 226 ms／9／max 31.15；inbound:GameSetUserPosition 125 ms／4542／max 6.38；inbound:GameSelectDungeon 89 ms／9／max 10.86；event:southern-dale.daily-refill-notifications 41 ms／51／max 6.01 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.3，`timer:` 31.3，`event:` 9.1。

