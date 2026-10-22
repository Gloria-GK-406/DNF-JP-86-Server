# 基线测量 — m1bd2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 4102 ms

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
| CHECK_USER_CONNECTION | 26808 | 55.85% |
| SET_USER_POSITION | 20325 | 42.34% |
| STEP_FENCE | 578 | 1.20% |
| FINISH_LOADING | 72 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| EPLP | 36 | 0.07% |
| REQUEST_PEER | 36 | 0.07% |
| RESPONSE_PEER | 36 | 0.07% |
| ENTER_SELECT_DUNGEON | 36 | 0.07% |
| SELECT_DUNGEON | 36 | 0.07% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26808 | 0.28 | 1.97 | 15.19 | 41.74 | 55.51 | 0 | 14.58 | 41.58 | 55.11 |
| SET_USER_POSITION | 20325 | 0.32 | 1.97 | 10.89 | 43.71 | 55.89 | 0 | 10.66 | 43.71 | 55.17 |
| FINISH_LOADING | 72** | 0.15 | 0.29 | 0.35 | 0.35 | 0.35 | 0 | 0.35 | 0.35 | 0.35 |
| LEAVE_PARTY | 37** | 0.69 | 1.06 | 1.18 | 1.18 | 1.18 | 0 | 1.18 | 1.18 | 1.18 |
| EPLP | 36** | 16.74 | 20.35 | 22.94 | 22.94 | 22.94 | 0 | 21.19 | 21.19 | 21.19 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 | – | – | – |
| RESPONSE_PEER | 36** | 0.30 | 0.53 | 0.64 | 0.64 | 0.64 | 0 | 0.64 | 0.64 | 0.64 |
| ENTER_SELECT_DUNGEON | 36** | 25.06 | 37.33 | 37.57 | 37.57 | 37.57 | 0 | 37.23 | 37.23 | 37.23 |
| SELECT_DUNGEON | 36** | 14.80 | 20.52 | 21.23 | 21.23 | 21.23 | 0 | 21.23 | 21.23 | 21.23 |
| **全部计入的请求** | 47386 | 0.30 | 2.01 | 15.76 | 42.06 | 55.89 | 36 | 15.60 | 42.01 | 55.17 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：47422 | 0.03 | 1.55 | 2.26 | 3.38 | 12.80（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| PrepareSelectedCharacterStateCommand | Transactional | 21** | 2.66 | 18.97 | 103.6 | 103.6 | 103.6 | 0.4 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21** | 15.65 | 50.53 | 63.88 | 63.88 | 63.88 | 0.8 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21** | 20.14 | 27.55 | 59.15 | 59.15 | 59.15 | 3.8 | 0 |
| EnterDungeonCommand | Transactional | 82** | 8.34 | 11.15 | 57.05 | 57.05 | 57.05 | 13.6 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21** | 14.30 | 54.37 | 54.62 | 54.62 | 54.62 | 3.2 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21** | 13.77 | 24.43 | 29.80 | 29.80 | 29.80 | 2.5 | 0 |
| CreateCharacterCommand | Transactional | 21** | 1.15 | 2.03 | 23.72 | 23.72 | 23.72 | 0.3 | 0 |
| LeaveDungeonCommand | Direct | 80** | 1.57 | 2.33 | 14.61 | 14.61 | 14.61 | 18.7 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21** | 0.28 | 0.40 | 6.69 | 6.69 | 6.69 | 0.3 | 0 |
| RegisterAccountCommand | Transactional | 81** | 0.15 | 0.52 | 6.37 | 6.37 | 6.37 | 0.2 | 0 |
| AcceptPartyInviteCommand | Direct | 83** | 0.03 | 0.22 | 5.60 | 5.60 | 5.60 | 8.7 | 0 |
| RequestPartyInviteCommand | Direct | 83** | 1.86 | 2.72 | 5.37 | 5.37 | 5.37 | 31.6 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102* | 0.01 | 0.03 | 0.06 | 4.91 | 4.91 | 0.3 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21** | 0.08 | 0.14 | 3.85 | 3.85 | 3.85 | 0.5 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21** | 0.04 | 0.26 | 3.77 | 3.77 | 3.77 | 0.5 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21** | 0.05 | 0.08 | 3.45 | 3.45 | 3.45 | 0.3 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21** | 0.07 | 0.17 | 3.37 | 3.37 | 3.37 | 0.3 | 0 |
| AdvanceCreatureClockCommand | Transactional | 345* | 0.09 | 0.34 | 0.38 | 3.33 | 3.33 | 1.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42** | 0.05 | 0.08 | 2.89 | 2.89 | 2.89 | 0.5 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 0.03 | 2.31 | 2.31 | 2.31 | 2.31 | 60.0 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81** | 0.04 | 0.15 | 1.89 | 1.89 | 1.89 | 0.2 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21** | 0.05 | 0.30 | 1.66 | 1.66 | 1.66 | 0.3 | 0 |
| RedeemGameLoginCommand | Transactional | 81** | 0.02 | 0.05 | 1.62 | 1.62 | 1.62 | 0.2 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21** | 0.05 | 0.10 | 1.57 | 1.57 | 1.57 | 0.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105* | 0.02 | 0.10 | 0.35 | 1.52 | 1.52 | 11.4 | 0 |
| LeavePartyCommand | Direct | 78** | 0.04 | 0.08 | 1.28 | 1.28 | 1.28 | 23.9 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84** | 0.06 | 0.25 | 0.96 | 0.96 | 0.96 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21** | 0.03 | 0.10 | 0.72 | 0.72 | 0.72 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 164* | 0.01 | 0.02 | 0.29 | 0.64 | 0.64 | 13.6 | 0 |

命令日志行 1789 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**136.8 ms**（SELECT_CHARACTER，计划发出 t=0.30 s，solo00；参考口径 136.8 ms）。冷启动窗整体：22520 | 0.30 | 2.41 | 33.39 | 84.53 | 136.8（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 23 | 136.8 | 136.8 | SELECT_CHARACTER |
| 1 | 111 | 72.42 | 76.45 | SET_USER_POSITION |
| 2 | 171 | 106.9 | 109.7 | CHECK_USER_CONNECTION |
| 3 | 183 | 106.8 | 116.2 | CHECK_USER_CONNECTION |
| 4 | 322 | 97.49 | 110.3 | CHECK_USER_CONNECTION |
| 5 | 400 | 1.66 | 2.38 | SET_USER_POSITION |
| 6 | 400 | 3.49 | 5.64 | CHECK_USER_CONNECTION |
| 7 | 400 | 2.33 | 2.54 | CHECK_USER_CONNECTION |
| 8 | 395 | 17.31 | 17.42 | SET_USER_POSITION |
| 9 | 384 | 3.72 | 5.93 | CHECK_USER_CONNECTION |
| 10 | 391 | 2.26 | 3.47 | CHECK_USER_CONNECTION |
| 11 | 400 | 4.41 | 7.51 | SET_USER_POSITION |
| 12 | 400 | 2.65 | 2.76 | SET_USER_POSITION |
| 13 | 384 | 76.57 | 88.94 | ENTER_SELECT_DUNGEON |
| 14 | 392 | 9.63 | 12.64 | SELECT_DUNGEON |
| 15 | 382 | 20.12 | 25.57 | ENTER_SELECT_DUNGEON |
| 16 | 394 | 2.34 | 2.50 | CHECK_USER_CONNECTION |
| 17 | 400 | 2.64 | 3.04 | SET_USER_POSITION |
| 18 | 398 | 41.98 | 47.50 | CHECK_USER_CONNECTION |
| 19 | 396 | 16.73 | 36.19 | CHECK_USER_CONNECTION |
| 20 | 398 | 41.22 | 44.27 | CHECK_USER_CONNECTION |
| 21 | 396 | 53.27 | 53.99 | CHECK_USER_CONNECTION |
| 22 | 400 | 2.41 | 2.82 | SET_USER_POSITION |
| 23 | 398 | 6.38 | 18.31 | SET_USER_POSITION |
| 24 | 396 | 2.23 | 3.61 | SET_USER_POSITION |
| 25 | 396 | 2.65 | 2.73 | CHECK_USER_CONNECTION |
| 26 | 398 | 2.30 | 2.80 | CHECK_USER_CONNECTION |
| 27 | 400 | 2.24 | 2.65 | CHECK_USER_CONNECTION |
| 28 | 395 | 2.71 | 5.40 | SET_USER_POSITION |
| 29 | 394 | 2.88 | 3.31 | CHECK_USER_CONNECTION |
| 30 | 391 | 2.74 | 4.25 | CHECK_USER_CONNECTION |
| 31 | 390 | 2.62 | 4.92 | CHECK_USER_CONNECTION |
| 32 | 400 | 2.23 | 2.69 | SET_USER_POSITION |
| 33 | 392 | 33.50 | 36.02 | ENTER_SELECT_DUNGEON |
| 34 | 388 | 21.88 | 29.79 | ENTER_SELECT_DUNGEON |
| 35 | 388 | 18.40 | 25.06 | ENTER_SELECT_DUNGEON |
| 36 | 384 | 11.76 | 13.98 | ENTER_SELECT_DUNGEON |
| 37 | 400 | 2.26 | 2.72 | SET_USER_POSITION |
| 38 | 396 | 26.57 | 27.84 | CHECK_USER_CONNECTION |
| 39 | 398 | 2.08 | 18.19 | CHECK_USER_CONNECTION |
| 40 | 398 | 32.19 | 34.29 | CHECK_USER_CONNECTION |
| 41 | 396 | 39.08 | 44.37 | SET_USER_POSITION |
| 42 | 398 | 2.58 | 2.76 | CHECK_USER_CONNECTION |
| 43 | 396 | 2.32 | 2.63 | SET_USER_POSITION |
| 44 | 400 | 2.58 | 2.74 | CHECK_USER_CONNECTION |
| 45 | 398 | 2.73 | 3.10 | CHECK_USER_CONNECTION |
| 46 | 396 | 2.50 | 2.60 | SET_USER_POSITION |
| 47 | 390 | 2.53 | 2.60 | SET_USER_POSITION |
| 48 | 395 | 2.35 | 2.63 | SET_USER_POSITION |
| 49 | 399 | 2.63 | 2.69 | CHECK_USER_CONNECTION |
| 50 | 396 | 2.80 | 3.26 | CHECK_USER_CONNECTION |
| 51 | 391 | 2.59 | 4.89 | CHECK_USER_CONNECTION |
| 52 | 383 | 26.90 | 38.67 | CHECK_USER_CONNECTION |
| 53 | 392 | 16.18 | 19.76 | ENTER_SELECT_DUNGEON |
| 54 | 400 | 2.40 | 2.91 | CHECK_USER_CONNECTION |
| 55 | 384 | 16.22 | 22.73 | ENTER_SELECT_DUNGEON |
| 56 | 400 | 2.55 | 2.90 | CHECK_USER_CONNECTION |
| 57 | 388 | 22.90 | 27.62 | CHECK_USER_CONNECTION |
| 58 | 398 | 2.61 | 3.10 | SET_USER_POSITION |
| 59 | 398 | 33.24 | 42.63 | SET_USER_POSITION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47386 | 0.30 | 2.01 | 15.76 | 42.06 | 55.89 |
| 10 并发登录的轮内 | 478* | 27.00 | 78.17 | 84.82 | 100.7 | 100.7 |
| （参考：实际发出口径）稳态窗 | 47386 | 0.18 | 0.38 | 15.60 | 42.01 | 55.17 |
| （参考：实际发出口径）轮内 | 478* | 7.41 | 75.05 | 81.45 | 88.97 | 88.97 |
| 登录阶段整体（182–232 s） | 19808 | 0.30 | 2.51 | 44.31 | 78.85 | 100.7 |

每轮时长（开始 → 10 个 LOGIN 都应答）：189, 218, 195, 205, 196, 195 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19200 | 0.36 | 2.30 | 14.27 | 39.52 | 55.86 | 13.37 |
| 单人 | 轮内 | 193* | 29.96 | 80.31 | 97.58 | 100.7 | 100.7 | 88.87 |
| 双人组领队 | 稳态窗 | 14075 | 0.26 | 1.77 | 18.19 | 42.23 | 55.89 | 18.08 |
| 双人组领队 | 轮内 | 143* | 26.02 | 76.00 | 83.24 | 84.36 | 84.36 | 78.71 |
| 双人组队员 | 稳态窗 | 14111 | 0.29 | 1.71 | 13.43 | 43.63 | 55.51 | 12.75 |
| 双人组队员 | 轮内 | 142* | 26.02 | 75.61 | 82.85 | 84.38 | 84.38 | 78.74 |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 102.1 | 109.8 | 111.9 | 111.9 | 111.9 |
| 启动器登录（HTTP，含校验） | 60** | 86.08 | 104.5 | 107.4 | 107.4 | 107.4 |
| Game LOGIN 0x0001 | 60** | 0.23 | 1.03 | 10.70 | 10.70 | 10.70 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 84567 包/秒（2537204 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47386 | 0.30 | 2.01 | 15.76 | 42.06 | 55.89 |
| 其余连接，灌包期间 | 11836 | 0.17 | 1.84 | 6.53 | 26.64 | 28.98 |
| （参考：实际发出口径）其余连接，灌包期间 | 11836 | 0.07 | 0.17 | 6.53 | 26.64 | 28.98 |
| 灌包连接自身（含其窗口内排队） | 2537235 | 0.34 | 0.41 | 0.93 | 3.37 | 31.44 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39297 | first selection | 0.3 |
| EPLP | 同伴 | 80 | 4669 | leave dungeon | 41.1 |
| EPLP | 请求者 | 80 | 4621 | leave dungeon | 41.1 |
| SET_USER_AREA | 同伴 | 6 | 3117 | town | 5.0 |
| SET_USER_AREA | 请求者 | 12 | 3010 | town | 5.0 |
| LEAVE_PARTY | 同伴 | 78 | 1877 | leave party | 82.3 |
| SELECT_DUNGEON | 请求者 | 82 | 1367 | party dungeon entry | 13.5 |
| SELECT_DUNGEON | 同伴 | 82 | 1367 | party dungeon entry | 13.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 82 | 656 | party dungeon entry | 34.4 |
| ENTER_SELECT_DUNGEON | 同伴 | 82 | 656 | party dungeon entry | 34.4 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 83 | 262 | party | 234.7 |
| RESPONSE_PEER | 请求者 | 83 | 260 | party | 234.7 |
| LEAVE_PARTY | 请求者 | 78 | 220 | leave party | 82.3 |
| REQUEST_PEER | 同伴 | 83 | 172 | party | 8.7 |
| REQUEST_PEER | 请求者 | 83 | 144 | party | 8.7 |
| CREATE_CHARACTER | 请求者 | 21 | 140 | create | 0.3 |
| FINISH_LOADING | 请求者 | 164 | 36 | party dungeon entry | 13.6 |
| FINISH_LOADING | 同伴 | 164 | 0 | party dungeon entry | 13.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 15.76 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 84.82 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 6.53 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 1 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 136.8 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 103.6 ms，max 103.6 ms（出现于 t=0.4 s；冷启动窗内 max 103.6 ms，之后 max – ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 82 次；服务端关闭连接或超时的请求 0 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：0 个；截尾样本（超时或连接被关闭）0 个，协议无回包 83 个。

失败的命令 0 条：

## 执行器作业（`--executor-log debug` 时）

本次 `--executor-log debug`。解析执行器的作业完成行（Debug）与失败行（Error）：共 2656327 行（Host 就绪之后 2656322 行，失败 0）；形似作业行但未匹配 0 行，数值解析失败 0 行。执行时长 = 作业体开始 → 结束（不含入队等待；过渡态下含作业内的直接发送）；入队等待 = 入队 → 开始执行。按工具读到日志行的时刻归窗。

### 各类作业的执行时长（Host 就绪之后）

| 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 首次 (ms) | 冷启动窗 max | 之后 max | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| inbound:GameSelectCharacter | 21** | 70.80 | 117.1 | 233.7 | 233.7 | 233.7 | 233.7 | 233.7 | – | 0.5 | 0 |
| inbound:GameSelectDungeon | 82** | 14.31 | 19.21 | 76.81 | 76.81 | 76.81 | 76.81 | 76.81 | 21.13 | 13.6 | 0 |
| inbound:GameEnterSelectDungeon | 82** | 22.46 | 36.89 | 62.75 | 62.75 | 62.75 | 62.75 | 62.75 | 37.18 | 13.5 | 0 |
| inbound:GameEplpCommand | 80** | 38.76 | 56.23 | 59.06 | 59.06 | 59.06 | 47.10 | 59.06 | 57.48 | 21.5 | 0 |
| inbound:GameCreateCharacter | 21** | 1.55 | 5.13 | 32.57 | 32.57 | 32.57 | 32.57 | 32.57 | – | 0.3 | 0 |
| inbound:GameSetUserArea | 12** | 12.79 | 20.25 | 20.25 | 20.25 | 20.25 | 11.17 | 20.25 | – | 5.0 | 0 |
| inbound:GameRequestPeer | 83** | 2.29 | 4.33 | 17.48 | 17.48 | 17.48 | 17.48 | 17.48 | 4.33 | 8.7 | 0 |
| inbound:GameResponsePeer | 83** | 0.39 | 1.77 | 16.63 | 16.63 | 16.63 | 16.63 | 16.63 | 1.77 | 8.7 | 0 |
| inbound:GameLeaveParty | 78** | 0.65 | 1.41 | 15.80 | 15.80 | 15.80 | 15.80 | 15.80 | 4.32 | 23.9 | 0 |
| lifecycle:disconnected | 81** | 0.01 | 2.08 | 14.21 | 14.21 | 14.21 | 3.25 | 3.25 | 14.21 | 274.0 | 0 |
| inbound:GameLogin | 81** | 0.14 | 0.40 | 12.75 | 12.75 | 12.75 | 12.75 | 12.75 | 0.41 | 0.3 | 0 |
| timer:PremiumDailyRefreshService | 4** | 2.36 | 12.27 | 12.27 | 12.27 | 12.27 | 12.27 | 12.27 | 3.69 | 60.0 | 0 |
| http:POST /api/launcher/v1/register | 81** | 0.17 | 0.68 | 8.92 | 8.92 | 8.92 | 8.92 | 8.92 | 0.88 | 0.2 | 0 |
| timer:ScheduledTaskHostedService/character.name-tag.expiration | 5** | 0.90 | 7.23 | 7.23 | 7.23 | 7.23 | 7.23 | 7.23 | 1.92 | 11.4 | 0 |
| timer:CreatureClockService | 277* | 0.02 | 0.88 | 1.20 | 7.09 | 7.09 | 7.09 | 7.09 | 1.20 | 1.0 | 0 |
| event:achievement.inventory | 210* | 0.31 | 0.71 | 2.88 | 7.04 | 7.04 | 7.04 | 7.04 | – | 0.3 | 0 |
| event:southern-dale.daily-refill-notifications | 21** | 0.66 | 5.23 | 6.58 | 6.58 | 6.58 | 5.23 | 6.58 | – | 0.8 | 0 |
| timer:WeaknessRecoveryService | 4** | 1.52 | 5.23 | 5.23 | 5.23 | 5.23 | 4.44 | 4.44 | 5.23 | 180.0 | 0 |
| event:quest.inventory | 210* | 0.10 | 0.33 | 2.56 | 5.02 | 5.02 | 5.02 | 5.02 | – | 0.3 | 0 |
| inbound:GameSetUserPosition | 45961 | 0.02 | 0.08 | 0.11 | 1.41 | 4.67 | 4.67 | 4.67 | 3.57 | 0.7 | 0 |
| timer:DimensionQuotaDailyRefreshService | 4** | 0.01 | 4.51 | 4.51 | 4.51 | 4.51 | 4.51 | 4.51 | 0.02 | 60.0 | 0 |
| lifecycle:connected | 82** | 0.01 | 0.06 | 4.22 | 4.22 | 4.22 | 4.22 | 4.22 | 0.10 | 0.1 | 0 |
| inbound:GameFinishLoading | 164* | 0.09 | 0.24 | 1.91 | 4.04 | 4.04 | 4.04 | 4.04 | 1.13 | 13.6 | 0 |
| timer:ItemUsageDailyRefreshService | 4** | 0.01 | 3.61 | 3.61 | 3.61 | 3.61 | 3.61 | 3.61 | 0.02 | 60.0 | 0 |
| inbound:GameConnectionCheck | 2599219 | 0.00 | 0.00 | 0.01 | 0.02 | 3.43 | 0.13 | 3.12 | 3.43 | 100.7 | 0 |
| timer:MirrorQuotaDailyRefreshService | 4** | 0.03 | 2.68 | 2.68 | 2.68 | 2.68 | 2.68 | 2.68 | 0.32 | 60.0 | 0 |
| timer:FatigueDailyRefreshService | 4** | 0.55 | 2.37 | 2.37 | 2.37 | 2.37 | 2.37 | 2.37 | 1.02 | 60.0 | 0 |
| http:POST /api/launcher/v1/login | 81** | 0.05 | 0.25 | 2.20 | 2.20 | 2.20 | 2.20 | 2.20 | 0.25 | 0.2 | 0 |
| timer:MarriageWeddingScheduler | 277* | 0.11 | 0.17 | 0.20 | 2.09 | 2.09 | 2.09 | 2.09 | 0.20 | 1.0 | 0 |
| event:altar.departure | 164* | 0.13 | 0.27 | 0.86 | 2.03 | 2.03 | 2.03 | 2.03 | 0.86 | 18.7 | 0 |
| timer:JoustMaintenanceService | 277* | 0.30 | 0.42 | 0.48 | 1.92 | 1.92 | 0.15 | 0.45 | 1.92 | 221.9 | 0 |
| timer:AltarQuotaDailyRefreshService | 4** | 0.01 | 1.53 | 1.53 | 1.53 | 1.53 | 1.53 | 1.53 | 0.03 | 60.0 | 0 |
| timer:TournamentQuotaDailyRefreshService | 4** | 0.01 | 1.44 | 1.44 | 1.44 | 1.44 | 1.44 | 1.44 | 0.06 | 60.0 | 0 |
| timer:GoldSilverHornQuotaDailyRefreshService | 4** | 0.11 | 1.36 | 1.36 | 1.36 | 1.36 | 1.15 | 1.15 | 1.36 | 120.0 | 0 |
| timer:DungeonPartyWipeService | 1110 | 0.01 | 0.04 | 0.05 | 0.34 | 1.24 | 1.24 | 1.24 | 0.07 | 0.2 | 0 |
| timer:ReviveCoinQuotaDailyRefreshService | 4** | 0.02 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 1.19 | 0.02 | 60.0 | 0 |
| timer:HerasQuotaDailyRefreshService | 4** | 0.03 | 1.12 | 1.12 | 1.12 | 1.12 | 1.12 | 1.12 | 0.05 | 60.0 | 0 |
| timer:IllusionTowerReturnSweepService | 555* | 0.01 | 0.02 | 0.03 | 0.92 | 0.92 | 0.92 | 0.92 | 0.03 | 0.5 | 0 |
| event:quest.mirror-leave | 164* | 0.14 | 0.28 | 0.64 | 0.84 | 0.84 | 0.84 | 0.84 | 0.64 | 18.7 | 0 |
| timer:BossTowerReturnSweepService | 555* | 0.01 | 0.02 | 0.03 | 0.77 | 0.77 | 0.77 | 0.77 | 0.02 | 0.5 | 0 |
| timer:TournamentRewardSweepService | 5547 | 0.00 | 0.01 | 0.01 | 0.02 | 0.53 | 0.53 | 0.53 | 0.03 | 0.0 | 0 |
| event:tournament.departure | 164* | 0.12 | 0.20 | 0.24 | 0.27 | 0.27 | 0.19 | 0.27 | 0.24 | 57.1 | 0 |
| timer:NotificationRetryDrainService | 277* | 0.00 | 0.01 | 0.01 | 0.22 | 0.22 | 0.22 | 0.22 | 0.01 | 1.0 | 0 |
| timer:LoginSessionMaintenanceService | 9** | 0.02 | 0.17 | 0.17 | 0.17 | 0.17 | 0.17 | 0.17 | 0.03 | 29.9 | 0 |
| timer:RoomMechanismMaintenanceService | 164* | 0.00 | 0.02 | 0.03 | 0.06 | 0.06 | 0.06 | 0.06 | 0.03 | 13.6 | 0 |

### 入队等待

(e) 窗的作业行不带连接号，灌包连接自己的作业分不出来；按 RQ-005 它在队列里至多 1 条，FIFO 下它的入队等待与别人的同分布，「全部作业」可作代理。端到端对照：稳态窗 p99 15.76 ms。

| 窗口 | 作业 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 的作业 | max 出现于 (s) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 冷启动窗 | 全部作业 | 25486 | 0.01 | 0.50 | 37.46 | 97.92 | 234.9 | event:achievement.inventory | 0.5 |
| 冷启动窗 | 只算 inbound: | 22919 | 0.01 | 0.20 | 28.01 | 82.25 | 116.1 |  |  |
| 稳态窗 | 全部作业 | 52216 | 0.01 | 0.15 | 17.14 | 41.81 | 55.00 | inbound:GameSetUserPosition | 61.9 |
| 稳态窗 | 只算 inbound: | 48060 | 0.01 | 0.13 | 12.61 | 41.81 | 55.00 |  |  |
| (d) 10 并发登录的轮内 | 全部作业 | 860* | 0.06 | 2.09 | 17.16 | 17.18 | 17.18 | inbound:GameConnectionCheck | 192.2 |
| (d) 10 并发登录的轮内 | 只算 inbound: | 606* | 0.08 | 0.52 | 17.17 | 17.18 | 17.18 |  |  |
| (e) 灌包期间 | 全部作业 | 2550256 | 0.00 | 0.00 | 0.01 | 0.04 | 30.25 | inbound:GameConnectionCheck | 254.5 |
| (e) 灌包期间 | 只算 inbound: | 2549235 | 0.00 | 0.00 | 0.01 | 0.04 | 30.25 |  |  |

<details><summary>冷启动窗逐秒入队等待</summary>

| 秒 | 作业数 | 入队等待 p99 (ms) | 入队等待 max (ms) | 等待 max 的作业 | 执行时长 max (ms) |
|---|---:|---:|---:|---:|---:|
| 0 | 106 | 234.4 | 234.9 | event:achievement.inventory | 233.7 |
| 1 | 285 | 79.44 | 90.91 | event:achievement.inventory | 89.53 |
| 2 | 333 | 103.9 | 114.7 | event:quest.inventory | 114.4 |
| 3 | 329 | 112.8 | 117.4 | event:quest.inventory | 117.1 |
| 4 | 502 | 102.1 | 112.3 | event:quest.inventory | 111.9 |
| 5 | 432 | 0.07 | 0.11 | timer:NotificationRetryDrainService | 0.15 |
| 6 | 433 | 0.30 | 0.44 | inbound:GameSetUserPosition | 0.43 |
| 7 | 432 | 0.26 | 0.44 | timer:NotificationRetryDrainService | 0.42 |
| 8 | 433 | 14.70 | 17.20 | inbound:GameSetUserPosition | 17.48 |
| 9 | 436 | 3.09 | 5.80 | inbound:GameConnectionCheck | 6.19 |
| 10 | 429 | 0.33 | 1.20 | inbound:GameSetUserPosition | 2.88 |
| 11 | 433 | 2.82 | 5.53 | inbound:GameConnectionCheck | 7.23 |
| 12 | 433 | 0.27 | 0.44 | timer:NotificationRetryDrainService | 0.42 |
| 13 | 434 | 71.60 | 84.65 | inbound:GameConnectionCheck | 76.81 |
| 14 | 434 | 7.68 | 12.01 | inbound:GameConnectionCheck | 12.60 |
| 15 | 441 | 12.96 | 21.84 | inbound:GameSetUserPosition | 25.48 |
| 16 | 427 | 0.11 | 0.16 | timer:NotificationRetryDrainService | 0.17 |
| 17 | 432 | 0.09 | 0.14 | timer:TournamentRewardSweepService | 2.01 |
| 18 | 449 | 41.65 | 46.71 | inbound:GameConnectionCheck | 47.10 |
| 19 | 437 | 22.86 | 35.14 | inbound:GameConnectionCheck | 40.08 |
| 20 | 440 | 38.60 | 43.64 | inbound:GameConnectionCheck | 44.80 |
| 21 | 446 | 49.43 | 53.17 | inbound:GameConnectionCheck | 59.06 |
| 22 | 433 | 0.26 | 2.68 | inbound:GameSetUserPosition | 2.65 |
| 23 | 434 | 0.20 | 12.87 | inbound:GameConnectionCheck | 15.80 |
| 24 | 431 | 0.25 | 0.41 | timer:NotificationRetryDrainService | 1.65 |
| 25 | 432 | 0.21 | 0.40 | timer:NotificationRetryDrainService | 0.83 |
| 26 | 433 | 0.22 | 0.41 | timer:NotificationRetryDrainService | 3.92 |
| 27 | 432 | 0.20 | 0.38 | timer:NotificationRetryDrainService | 0.35 |
| 28 | 433 | 0.42 | 4.10 | inbound:GameSetUserPosition | 4.46 |
| 29 | 437 | 0.25 | 3.11 | inbound:GameConnectionCheck | 3.06 |
| 30 | 434 | 0.27 | 0.42 | timer:NotificationRetryDrainService | 2.38 |
| 31 | 427 | 0.30 | 4.76 | inbound:GameConnectionCheck | 5.67 |
| 32 | 433 | 0.19 | 0.22 | inbound:GameSetUserPosition | 0.19 |
| 33 | 433 | 24.65 | 34.65 | inbound:GameConnectionCheck | 35.63 |
| 34 | 439 | 21.40 | 23.48 | inbound:GameConnectionCheck | 29.53 |
| 35 | 429 | 8.27 | 17.52 | inbound:GameConnectionCheck | 21.23 |
| 36 | 435 | 9.90 | 11.36 | inbound:GameSetUserPosition | 13.80 |
| 37 | 432 | 0.10 | 0.43 | inbound:GameConnectionCheck | 0.42 |
| 38 | 445 | 22.76 | 27.73 | inbound:GameConnectionCheck | 31.91 |
| 39 | 441 | 17.15 | 24.11 | inbound:GameConnectionCheck | 27.74 |
| 40 | 440 | 30.78 | 32.05 | inbound:GameConnectionCheck | 32.02 |
| 41 | 446 | 37.74 | 44.28 | inbound:GameSetUserPosition | 45.43 |
| 42 | 435 | 0.24 | 0.36 | inbound:GameSetUserPosition | 2.42 |
| 43 | 430 | 0.25 | 0.40 | timer:NotificationRetryDrainService | 0.94 |
| 44 | 433 | 0.21 | 0.26 | timer:TournamentRewardSweepService | 1.41 |
| 45 | 432 | 0.23 | 0.41 | timer:NotificationRetryDrainService | 0.81 |
| 46 | 433 | 0.24 | 0.81 | inbound:GameConnectionCheck | 2.30 |
| 47 | 432 | 0.29 | 2.34 | inbound:GameConnectionCheck | 3.24 |
| 48 | 433 | 0.20 | 0.26 | timer:NotificationRetryDrainService | 1.87 |
| 49 | 436 | 0.24 | 0.28 | inbound:GameSetUserPosition | 2.47 |
| 50 | 429 | 0.28 | 0.42 | inbound:GameSetUserPosition | 0.18 |
| 51 | 433 | 1.41 | 3.03 | inbound:GameConnectionCheck | 2.87 |
| 52 | 434 | 20.86 | 36.83 | inbound:GameConnectionCheck | 36.82 |
| 53 | 433 | 9.78 | 17.29 | inbound:GameSetUserPosition | 18.35 |
| 54 | 433 | 0.26 | 0.42 | timer:NotificationRetryDrainService | 0.41 |
| 55 | 434 | 10.43 | 20.31 | inbound:GameSetUserPosition | 22.68 |
| 56 | 433 | 0.24 | 0.45 | timer:NotificationRetryDrainService | 0.43 |
| 57 | 456 | 19.56 | 27.06 | inbound:GameConnectionCheck | 30.68 |
| 58 | 431 | 0.19 | 0.44 | timer:NotificationRetryDrainService | 0.42 |
| 59 | 451 | 34.65 | 42.49 | inbound:GameSetUserPosition | 44.90 |

</details>

### 各窗口的执行器占用（作业执行时长之和 ÷ 窗口时长）

| 窗口 | 时长 (s) | 作业数 | 执行时长之和 (ms) | 占用 | 按执行时长之和的前 8 类（和／个数／max） |
|---|---:|---:|---:|---:|---:|
| 冷启动窗 | 60.0 | 25486 | 4281 | 7.1% | inbound:GameSelectCharacter 1893 ms／21／max 233.7；inbound:GameEplpCommand 533 ms／16／max 59.06；inbound:GameEnterSelectDungeon 391 ms／18／max 62.75；inbound:GameSetUserPosition 377 ms／9624／max 4.67；inbound:GameSelectDungeon 319 ms／18／max 76.81；inbound:GameSetUserArea 150 ms／12／max 20.25；inbound:GameConnectionCheck 89 ms／13084／max 3.12；event:achievement.inventory 84 ms／210／max 7.04 |
| 稳态窗 | 120.0 | 52216 | 4049 | 3.4% | inbound:GameEplpCommand 1394 ms／36／max 57.48；inbound:GameEnterSelectDungeon 862 ms／36／max 37.18；inbound:GameSetUserPosition 771 ms／20325／max 3.57；inbound:GameSelectDungeon 551 ms／36／max 21.13；inbound:GameConnectionCheck 198 ms／27446／max 3.43；inbound:GameRequestPeer 77 ms／36／max 3.25；timer:JoustMaintenanceService 32 ms／120／max 0.58；inbound:GameLeaveParty 27 ms／37／max 1.29 |
| (d) 10 并发登录的轮内 | 1.2 | 860 | 56 | 4.7% | inbound:GameEnterSelectDungeon 17 ms／1／max 16.94；http:POST /api/launcher/v1/register 11 ms／60／max 0.88；inbound:GameSelectDungeon 11 ms／1／max 10.53；inbound:GameLogin 8 ms／60／max 0.41；http:POST /api/launcher/v1/login 4 ms／60／max 0.25；inbound:GameSetUserPosition 2 ms／193／max 0.07；inbound:GameConnectionCheck 1 ms／349／max 0.11；lifecycle:connected 1 ms／60／max 0.10 |
| (e) 灌包期间 | 30.0 | 2550256 | 6304 | 21.0% | inbound:GameConnectionCheck 5785 ms／2544063／max 0.71；inbound:GameEplpCommand 181 ms／7／max 30.24；inbound:GameEnterSelectDungeon 117 ms／10／max 14.44；inbound:GameSetUserPosition 98 ms／5095／max 0.58；inbound:GameSelectDungeon 95 ms／10／max 10.81；inbound:GameRequestPeer 7 ms／11／max 0.91；timer:JoustMaintenanceService 3 ms／30／max 0.13；timer:CreatureClockService 3 ms／30／max 0.34 |

稳态窗每秒作业数（按来源前缀）：`inbound:` 400.5，`timer:` 32.8，`event:` 1.8。

