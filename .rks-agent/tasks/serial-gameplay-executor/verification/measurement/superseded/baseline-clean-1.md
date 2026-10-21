# 基线测量 — c1（合成负载）

> **合成负载**：用户没有提供 A21 客户端的真实游玩录制，负载由本工具按 TASK-01 表征序列合成（RQ-090 第②项）。

## 环境

- 机器：Apple M5，10 逻辑核，内存 32 GiB，macOS 27.0.1
- 磁盘（数据库所在卷 `/`，临时目录 `/var/folders/qc/sl_p_wxs2qvg3pn_zknp1ppw0000gn/T/`）：设备 /dev/disk3s5，文件系统 APFS，Internal，Apple Fabric，Solid State: Yes（`diskutil info /`：Solid State:               Yes）
- .NET 运行时（工具进程）：.NET 10.0.10
- Host：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`，构建配置 Release，Host.dll 修改时间 2026-10-21T12:33:02Z
- Host 进程：`dotnet Dfo.Server.Host.dll`，工作站/服务器 GC 取 Host 自身默认（csproj 未设置），日志 Default=Information（appsettings.json），额外设置 `Logging__Console__FormatterOptions__SingleLine=true`
- SQLite：版本 3.53.4；`PRAGMA journal_mode` = **wal**（对 Host 运行中的数据库文件以只读连接读取，文件级属性）；`PRAGMA synchronous` = **2**（FULL；连接级，取值见下）
  - synchronous 的取法：服务端源码不设置 synchronous（`Server/src` 中检索 `synchronous` 无 PRAGMA 命中；唯一的 PRAGMA 写入是 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` 的 `PRAGMA journal_mode=WAL;`），连接串只有 `Data Source`（`SqliteDatabase.cs:41` 等）。所以服务端每条连接取原生库的编译期默认值。上面的值来自本工具以同样形式的连接串、对同一数据库、先读 journal_mode（WAL 下 SQLite 按 WAL 默认调整）后读取的结果，用的是与 Host 相同的原生库：
  - Host 原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Server/src/Dfo.Server.Host/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 工具原生库 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/Tool/GameplayLoad/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib` SHA-256 2B27F45233818AE1…
  - 相关编译选项：DEFAULT_SYNCHRONOUS=2, DEFAULT_WAL_AUTOCHECKPOINT=1000, DEFAULT_WAL_SYNCHRONOUS=2, THREADSAFE=1

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1498 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：399.9 包/秒（47982 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26838 | 55.93% |
| SET_USER_POSITION | 20292 | 42.29% |
| STEP_FENCE | 568 | 1.18% |
| FINISH_LOADING | 70 | 0.15% |
| LEAVE_PARTY | 37 | 0.08% |
| REQUEST_PEER | 36 | 0.08% |
| RESPONSE_PEER | 36 | 0.08% |
| EPLP | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

口径：客户端写出请求帧之前取时间戳 → 该请求第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到时取时间戳。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。没有回包的请求不计入分布，单列「无回包」。

| 请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 无回包 |
|---|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26838 | 0.18 | 0.38 | 0.72 | 3.37 | 10.14 | 0 |
| SET_USER_POSITION | 20292 | 0.20 | 0.53 | 1.28 | 26.99 | 38.02 | 0 |
| FINISH_LOADING | 70 | 0.13 | 0.21 | 0.77 | 0.77 | 0.77 | 0 |
| LEAVE_PARTY | 37 | 0.84 | 1.41 | 1.47 | 1.47 | 1.47 | 0 |
| REQUEST_PEER | 0 | – | – | – | – | – | 36 |
| RESPONSE_PEER | 36 | 0.38 | 0.69 | 0.70 | 0.70 | 0.70 | 0 |
| EPLP | 35 | 18.68 | 23.07 | 24.09 | 24.09 | 24.09 | 0 |
| ENTER_SELECT_DUNGEON | 35 | 32.59 | 39.43 | 47.25 | 47.25 | 47.25 | 0 |
| SELECT_DUNGEON | 35 | 15.52 | 287.3 | 518.1 | 518.1 | 518.1 | 0 |
| **全部有回包请求** | 47378 | 0.19 | 0.46 | 1.03 | 27.89 | 518.1 | 36 |


## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RegisterAccountCommand | Transactional | 81 | 279.8 | 659.6 | 671.5 | 671.5 | 671.5 | 192.7 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81 | 635.6 | 639.5 | 640.2 | 640.2 | 640.2 | 223.1 | 0 |
| EnterDungeonCommand | Transactional | 80 | 9.00 | 73.98 | 639.4 | 639.4 | 639.4 | 223.0 | 0 |
| RedeemGameLoginCommand | Transactional | 81 | 189.5 | 571.4 | 575.7 | 575.7 | 575.7 | 223.3 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 21 | 3.37 | 19.32 | 497.2 | 497.2 | 497.2 | 0.8 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337 | 0.16 | 0.76 | 41.16 | 348.6 | 348.6 | 203.3 | 0 |
| CreateCharacterCommand | Transactional | 21 | 1.33 | 2.18 | 73.94 | 73.94 | 73.94 | 0.3 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21 | 21.81 | 36.17 | 44.31 | 44.31 | 44.31 | 2.6 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21 | 0.49 | 2.15 | 40.63 | 40.63 | 40.63 | 0.3 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21 | 18.54 | 39.11 | 40.63 | 40.63 | 40.63 | 2.2 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21 | 15.24 | 30.99 | 39.12 | 39.12 | 39.12 | 1.9 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21 | 14.90 | 34.89 | 36.92 | 36.92 | 36.92 | 1.4 | 0 |
| LeaveDungeonCommand | Direct | 78 | 1.78 | 2.86 | 21.65 | 21.65 | 21.65 | 18.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105 | 0.03 | 0.27 | 1.45 | 9.46 | 9.46 | 129.8 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21 | 0.49 | 3.48 | 7.15 | 7.15 | 7.15 | 0.3 | 0 |
| AcceptPartyInviteCommand | Direct | 82 | 0.04 | 0.41 | 5.81 | 5.81 | 5.81 | 9.6 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21 | 0.30 | 0.57 | 5.79 | 5.79 | 5.79 | 0.3 | 0 |
| LeavePartyCommand | Direct | 78 | 0.05 | 0.12 | 5.22 | 5.22 | 5.22 | 24.5 | 0 |
| RequestPartyInviteCommand | Direct | 82 | 1.99 | 3.03 | 5.17 | 5.17 | 5.17 | 228.4 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102 | 0.01 | 0.06 | 0.15 | 5.16 | 5.16 | 0.2 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21 | 0.04 | 0.25 | 4.08 | 4.08 | 4.08 | 0.9 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21 | 0.42 | 1.99 | 3.74 | 3.74 | 3.74 | 0.3 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21 | 0.73 | 2.88 | 3.52 | 3.52 | 3.52 | 3.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42 | 0.05 | 0.11 | 3.42 | 3.42 | 3.42 | 1.0 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4 | 0.17 | 2.91 | 2.91 | 2.91 | 2.91 | 60.0 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21 | 0.59 | 1.51 | 2.05 | 2.05 | 2.05 | 5.2 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84 | 0.06 | 0.16 | 0.82 | 0.82 | 0.82 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21 | 0.32 | 0.47 | 0.74 | 0.74 | 0.74 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160 | 0.01 | 0.02 | 0.28 | 0.65 | 0.65 | 14.0 | 0 |

命令日志行 1771 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组），按发出时刻所在的秒聚合。最长单次停顿 = 窗口内客户端看到的最大单请求延迟。

最长单次停顿：**573.3 ms**（SELECT_CHARACTER，t=0.28 s，solo00）。冷启动窗整体：22239 | 0.18 | 0.46 | 3.39 | 24.74 | 573.3（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 4 | 573.3 | 573.3 | SELECT_CHARACTER |
| 1 | 58 | 9.16 | 9.16 | SELECT_CHARACTER |
| 2 | 132 | 7.53 | 7.69 | CHECK_USER_CONNECTION |
| 3 | 172 | 10.35 | 27.93 | SELECT_CHARACTER |
| 4 | 204 | 15.11 | 19.86 | SET_USER_AREA |
| 5 | 359 | 25.24 | 34.65 | SET_USER_AREA |
| 6 | 400 | 0.24 | 0.26 | SET_USER_POSITION |
| 7 | 400 | 0.92 | 3.01 | SET_USER_POSITION |
| 8 | 400 | 0.87 | 1.66 | CHECK_USER_CONNECTION |
| 9 | 387 | 2.28 | 12.13 | RESPONSE_PEER |
| 10 | 388 | 0.77 | 1.10 | RESPONSE_PEER |
| 11 | 395 | 0.94 | 1.02 | SET_USER_POSITION |
| 12 | 400 | 1.10 | 1.31 | SET_USER_POSITION |
| 13 | 396 | 16.66 | 94.30 | SELECT_DUNGEON |
| 14 | 386 | 12.21 | 22.49 | ENTER_SELECT_DUNGEON |
| 15 | 376 | 12.66 | 21.38 | ENTER_SELECT_DUNGEON |
| 16 | 394 | 0.65 | 0.83 | CHECK_USER_CONNECTION |
| 17 | 400 | 0.76 | 1.06 | CHECK_USER_CONNECTION |
| 18 | 398 | 0.96 | 47.89 | EPLP |
| 19 | 398 | 0.89 | 22.10 | EPLP |
| 20 | 392 | 22.54 | 32.18 | SET_USER_POSITION |
| 21 | 400 | 0.82 | 2.94 | SET_USER_POSITION |
| 22 | 400 | 0.82 | 1.40 | SET_USER_POSITION |
| 23 | 400 | 0.57 | 0.83 | SET_USER_POSITION |
| 24 | 394 | 4.10 | 17.87 | LEAVE_PARTY |
| 25 | 398 | 0.68 | 1.10 | CHECK_USER_CONNECTION |
| 26 | 396 | 0.89 | 0.96 | CHECK_USER_CONNECTION |
| 27 | 400 | 0.78 | 3.33 | SET_USER_POSITION |
| 28 | 394 | 9.25 | 9.95 | CHECK_USER_CONNECTION |
| 29 | 386 | 0.73 | 0.95 | CHECK_USER_CONNECTION |
| 30 | 397 | 0.64 | 0.77 | CHECK_USER_CONNECTION |
| 31 | 393 | 0.80 | 1.04 | SET_USER_POSITION |
| 32 | 400 | 0.64 | 1.46 | SET_USER_POSITION |
| 33 | 376 | 24.74 | 39.03 | ENTER_SELECT_DUNGEON |
| 34 | 392 | 1.39 | 28.95 | ENTER_SELECT_DUNGEON |
| 35 | 392 | 0.69 | 19.69 | SELECT_DUNGEON |
| 36 | 392 | 0.53 | 22.44 | SELECT_DUNGEON |
| 37 | 400 | 0.50 | 0.59 | CHECK_USER_CONNECTION |
| 38 | 394 | 11.23 | 17.76 | EPLP |
| 39 | 398 | 0.54 | 17.90 | EPLP |
| 40 | 396 | 9.34 | 32.59 | SET_USER_POSITION |
| 41 | 400 | 0.65 | 1.28 | CHECK_USER_CONNECTION |
| 42 | 398 | 1.07 | 1.57 | LEAVE_PARTY |
| 43 | 396 | 1.26 | 1.92 | SET_USER_POSITION |
| 44 | 398 | 0.63 | 0.89 | SET_USER_POSITION |
| 45 | 398 | 0.70 | 2.96 | SET_USER_POSITION |
| 46 | 398 | 0.52 | 0.78 | SET_USER_POSITION |
| 47 | 390 | 1.13 | 1.21 | SET_USER_POSITION |
| 48 | 400 | 0.58 | 0.74 | SET_USER_POSITION |
| 49 | 390 | 0.54 | 0.80 | SET_USER_POSITION |
| 50 | 400 | 0.59 | 2.10 | SET_USER_POSITION |
| 51 | 392 | 13.61 | 30.80 | ENTER_SELECT_DUNGEON |
| 52 | 390 | 0.52 | 0.72 | SET_USER_POSITION |
| 53 | 392 | 12.78 | 32.35 | ENTER_SELECT_DUNGEON |
| 54 | 384 | 13.56 | 29.87 | ENTER_SELECT_DUNGEON |
| 55 | 400 | 0.54 | 1.37 | SET_USER_POSITION |
| 56 | 398 | 2.09 | 18.98 | EPLP |
| 57 | 384 | 17.23 | 27.03 | ENTER_SELECT_DUNGEON |
| 58 | 397 | 1.15 | 20.50 | EPLP |
| 59 | 397 | 0.80 | 21.90 | EPLP |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47378 | 0.19 | 0.46 | 1.03 | 27.89 | 518.1 |
| 10 并发登录的轮内 | 3063 | 0.09 | 0.15 | 0.20 | 12.46 | 643.6 |
| 登录阶段整体（182–233 s） | 20242 | 0.17 | 0.39 | 0.75 | 29.21 | 643.6 |

每轮时长（开始 → 10 个 LOGIN 都应答）：1314, 1313, 1307, 1304, 1286, 1297 ms。

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60 | 354.3 | 666.7 | 671.9 | 671.9 | 671.9 |
| 启动器登录（HTTP，含校验） | 60 | 637.2 | 639.9 | 640.5 | 640.5 | 640.5 |
| Game LOGIN 0x0001 | 60 | 258.3 | 573.1 | 575.9 | 575.9 | 575.9 |

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 196996 包/秒（5909887 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47378 | 0.19 | 0.46 | 1.03 | 27.89 | 518.1 |
| 其余连接，灌包期间 | 11850 | 0.07 | 0.14 | 0.57 | 18.04 | 33.58 |
| 灌包连接自身（含其窗口内排队） | 5909918 | 0.06 | 0.11 | 0.14 | 1.11 | 17.57 |

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39291 | first selection | 0.3 |
| EPLP | 同伴 | 78 | 3646 | leave dungeon | 125.0 |
| EPLP | 请求者 | 78 | 3550 | leave dungeon | 125.0 |
| SET_USER_AREA | 同伴 | 6 | 3084 | town | 5.7 |
| SET_USER_AREA | 请求者 | 12 | 2905 | town | 5.7 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 13.9 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 13.9 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 707 | party dungeon entry | 33.6 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 707 | party dungeon entry | 33.6 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 82 | 238 | party | 9.6 |
| RESPONSE_PEER | 请求者 | 82 | 236 | party | 9.6 |
| LEAVE_PARTY | 同伴 | 78 | 211 | leave party | 24.0 |
| LEAVE_PARTY | 请求者 | 78 | 172 | leave party | 24.0 |
| CREATE_CHARACTER | 请求者 | 21 | 137 | create | 0.2 |
| REQUEST_PEER | 同伴 | 82 | 124 | party | 9.6 |
| REQUEST_PEER | 请求者 | 82 | 96 | party | 9.6 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 14.0 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 14.0 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 1.03 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 0.20 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 0.57 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 6 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 573.3 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `RegisterAccountCommand`：样本 81，p99.9 671.5 ms，max 671.5 ms（出现于 t=192.7 s；冷启动窗内 max 75.94 ms，之后 max 671.5 ms）
- `AuthenticateAndIssueTicketCommand`：样本 81，p99.9 640.2 ms，max 640.2 ms（出现于 t=223.1 s；冷启动窗内 max 70.95 ms，之后 max 640.2 ms）
- `EnterDungeonCommand`：样本 80，p99.9 639.4 ms，max 639.4 ms（出现于 t=223.0 s；冷启动窗内 max 73.98 ms，之后 max 639.4 ms）
- `RedeemGameLoginCommand`：样本 81，p99.9 575.7 ms，max 575.7 ms（出现于 t=223.3 s；冷启动窗内 max 1.81 ms，之后 max 575.7 ms）
- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 497.2 ms，max 497.2 ms（出现于 t=0.8 s；冷启动窗内 max 497.2 ms，之后 max – ms）
- `AdvanceCreatureClockCommand`：样本 337，p99.9 348.6 ms，max 348.6 ms（出现于 t=203.3 s；冷启动窗内 max 65.79 ms，之后 max 348.6 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

端到端 > 250 ms 的请求（setup 与 background，含无回包按等到关闭/收口计）：5 个。
- SELECT_DUNGEON：4 个，最大 644 ms，发出时刻 96.0, 117.0, 202.1, 222.4 s
- SELECT_CHARACTER：1 个，最大 573 ms，发出时刻 0.3 s

失败的命令 0 条：

