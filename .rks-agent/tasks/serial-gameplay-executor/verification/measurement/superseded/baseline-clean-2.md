# 基线测量 — c2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1480 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：1。
- 单人动作权重：栅栏 0.5，移动 0.5，跨区 0，回选角+再选角 0，回选角+建角+选新角 0。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 274 s。
- 稳态窗实际包速率：399.9 包/秒（47994 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 26952 | 56.16% |
| SET_USER_POSITION | 20196 | 42.08% |
| STEP_FENCE | 564 | 1.18% |
| FINISH_LOADING | 70 | 0.15% |
| EPLP | 36 | 0.08% |
| LEAVE_PARTY | 36 | 0.08% |
| REQUEST_PEER | 35 | 0.07% |
| RESPONSE_PEER | 35 | 0.07% |
| ENTER_SELECT_DUNGEON | 35 | 0.07% |
| SELECT_DUNGEON | 35 | 0.07% |

## 端到端延迟（稳态窗）

口径：客户端写出请求帧之前取时间戳 → 该请求第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到时取时间戳。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。没有回包的请求不计入分布，单列「无回包」。

| 请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 无回包 |
|---|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 26952 | 0.19 | 0.38 | 0.76 | 3.89 | 19.11 | 0 |
| SET_USER_POSITION | 20196 | 0.21 | 0.57 | 1.25 | 28.79 | 45.05 | 0 |
| FINISH_LOADING | 70 | 0.14 | 0.24 | 0.81 | 0.81 | 0.81 | 0 |
| EPLP | 36 | 18.46 | 28.03 | 28.04 | 28.04 | 28.04 | 0 |
| LEAVE_PARTY | 36 | 0.75 | 1.17 | 1.76 | 1.76 | 1.76 | 0 |
| REQUEST_PEER | 0 | – | – | – | – | – | 35 |
| RESPONSE_PEER | 35 | 0.35 | 0.67 | 0.77 | 0.77 | 0.77 | 0 |
| ENTER_SELECT_DUNGEON | 35 | 31.59 | 40.75 | 41.25 | 41.25 | 41.25 | 0 |
| SELECT_DUNGEON | 35 | 15.77 | 21.32 | 26.71 | 26.71 | 26.71 | 0 |
| **全部有回包请求** | 47395 | 0.20 | 0.49 | 1.04 | 27.39 | 45.05 | 35 |


## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RegisterAccountCommand | Transactional | 81 | 270.0 | 653.3 | 670.7 | 670.7 | 670.7 | 212.7 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 81 | 638.0 | 640.9 | 642.4 | 642.4 | 642.4 | 182.7 | 0 |
| RedeemGameLoginCommand | Transactional | 81 | 190.5 | 572.7 | 574.5 | 574.5 | 574.5 | 223.3 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 21 | 3.59 | 25.61 | 492.9 | 492.9 | 492.9 | 0.8 | 0 |
| AdvanceCreatureClockCommand | Transactional | 337 | 0.15 | 0.84 | 55.50 | 330.0 | 330.0 | 223.3 | 0 |
| EnterDungeonCommand | Transactional | 80 | 8.91 | 71.31 | 277.3 | 277.3 | 277.3 | 36.8 | 0 |
| CreateCharacterCommand | Transactional | 21 | 1.30 | 2.13 | 70.75 | 70.75 | 70.75 | 0.3 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 21 | 21.47 | 35.76 | 45.42 | 45.42 | 45.42 | 2.6 | 0 |
| RefillOnlineBossTowerTicketCommand | Transactional | 21 | 18.88 | 40.12 | 40.74 | 40.74 | 40.74 | 4.0 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 21 | 0.50 | 0.76 | 39.32 | 39.32 | 39.32 | 0.3 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 21 | 15.04 | 35.16 | 36.34 | 36.34 | 36.34 | 1.4 | 0 |
| RefillOnlineSouthernTicketCommand | Transactional | 21 | 15.42 | 30.61 | 36.18 | 36.18 | 36.18 | 1.9 | 0 |
| LeaveDungeonCommand | Direct | 78 | 1.81 | 4.30 | 18.47 | 18.47 | 18.47 | 19.0 | 0 |
| CleanupExpiredCharacterItemsCommand | Transactional | 21 | 0.50 | 1.31 | 6.77 | 6.77 | 6.77 | 0.3 | 0 |
| RequestPartyInviteCommand | Direct | 81 | 2.38 | 3.56 | 6.62 | 6.62 | 6.62 | 193.6 | 0 |
| GetAccountCharacterRosterCommand | Direct | 21 | 0.33 | 0.58 | 6.42 | 6.42 | 6.42 | 0.3 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 105 | 0.02 | 0.11 | 0.98 | 5.74 | 5.74 | 29.8 | 0 |
| AcceptPartyInviteCommand | Direct | 81 | 0.04 | 0.23 | 5.52 | 5.52 | 5.52 | 9.7 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 102 | 0.01 | 0.04 | 0.18 | 5.06 | 5.06 | 0.2 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 21 | 0.04 | 0.46 | 4.10 | 4.10 | 4.10 | 0.9 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4 | 0.03 | 3.60 | 3.60 | 3.60 | 3.60 | 60.0 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 21 | 0.44 | 0.66 | 3.55 | 3.55 | 3.55 | 0.3 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 42 | 0.06 | 0.13 | 3.53 | 3.53 | 3.53 | 1.0 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 21 | 0.70 | 0.99 | 2.78 | 2.78 | 2.78 | 1.0 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 21 | 0.55 | 0.65 | 1.52 | 1.52 | 1.52 | 1.0 | 0 |
| LeavePartyCommand | Direct | 78 | 0.05 | 0.18 | 1.50 | 1.50 | 1.50 | 24.0 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 84 | 0.07 | 0.30 | 1.09 | 1.09 | 1.09 | 60.0 | 0 |
| InitializeHousingCommand | Transactional | 21 | 0.33 | 0.48 | 0.73 | 0.73 | 0.73 | 0.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 160 | 0.01 | 0.03 | 0.52 | 0.66 | 0.66 | 14.1 | 0 |

命令日志行 1769 条（其中失败 0）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组），按发出时刻所在的秒聚合。最长单次停顿 = 窗口内客户端看到的最大单请求延迟。

最长单次停顿：**567.5 ms**（SELECT_CHARACTER，t=0.27 s，flood）。冷启动窗整体：22247 | 0.20 | 0.75 | 3.56 | 28.77 | 567.5（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 3 | 567.5 | 567.5 | SELECT_CHARACTER |
| 1 | 50 | 29.14 | 29.14 | SELECT_CHARACTER |
| 2 | 133 | 6.16 | 6.17 | SELECT_CHARACTER |
| 3 | 175 | 6.71 | 8.59 | SET_USER_POSITION |
| 4 | 207 | 14.66 | 17.12 | SET_USER_AREA |
| 5 | 367 | 25.09 | 32.78 | SET_USER_AREA |
| 6 | 399 | 0.28 | 0.36 | SET_USER_POSITION |
| 7 | 400 | 1.08 | 3.46 | SET_USER_POSITION |
| 8 | 399 | 1.41 | 1.51 | SET_USER_POSITION |
| 9 | 385 | 2.21 | 15.26 | RESPONSE_PEER |
| 10 | 391 | 1.15 | 1.40 | SET_USER_POSITION |
| 11 | 395 | 1.38 | 1.68 | SET_USER_POSITION |
| 12 | 400 | 1.27 | 1.45 | SET_USER_POSITION |
| 13 | 399 | 14.05 | 100.7 | SELECT_DUNGEON |
| 14 | 382 | 12.55 | 77.82 | SELECT_DUNGEON |
| 15 | 380 | 7.83 | 24.53 | SELECT_DUNGEON |
| 16 | 391 | 0.80 | 1.06 | CHECK_USER_CONNECTION |
| 17 | 400 | 0.86 | 0.97 | CHECK_USER_CONNECTION |
| 18 | 399 | 0.78 | 45.97 | EPLP |
| 19 | 397 | 3.84 | 22.90 | EPLP |
| 20 | 392 | 13.84 | 34.81 | EPLP |
| 21 | 400 | 0.98 | 1.34 | SET_USER_POSITION |
| 22 | 400 | 0.98 | 1.14 | CHECK_USER_CONNECTION |
| 23 | 400 | 0.99 | 1.21 | SET_USER_POSITION |
| 24 | 398 | 1.79 | 15.70 | LEAVE_PARTY |
| 25 | 394 | 1.17 | 4.40 | LEAVE_PARTY |
| 26 | 396 | 1.09 | 1.11 | SET_USER_POSITION |
| 27 | 400 | 1.09 | 1.46 | SET_USER_POSITION |
| 28 | 395 | 0.92 | 1.02 | SET_USER_POSITION |
| 29 | 390 | 1.08 | 1.35 | SET_USER_POSITION |
| 30 | 390 | 1.19 | 1.33 | SET_USER_POSITION |
| 31 | 395 | 1.41 | 1.55 | SET_USER_POSITION |
| 32 | 400 | 1.38 | 4.33 | SET_USER_POSITION |
| 33 | 392 | 13.50 | 35.48 | ENTER_SELECT_DUNGEON |
| 34 | 392 | 9.97 | 34.67 | ENTER_SELECT_DUNGEON |
| 35 | 391 | 6.84 | 32.70 | ENTER_SELECT_DUNGEON |
| 36 | 375 | 17.74 | 284.6 | SELECT_DUNGEON |
| 37 | 402 | 0.57 | 0.64 | SET_USER_POSITION |
| 38 | 398 | 0.83 | 18.93 | EPLP |
| 39 | 398 | 0.91 | 18.61 | EPLP |
| 40 | 396 | 16.51 | 21.35 | EPLP |
| 41 | 395 | 25.59 | 31.99 | SET_USER_POSITION |
| 42 | 397 | 28.65 | 28.77 | SET_USER_POSITION |
| 43 | 397 | 0.87 | 1.28 | SET_USER_POSITION |
| 44 | 400 | 0.92 | 1.01 | SET_USER_POSITION |
| 45 | 398 | 0.94 | 3.03 | SET_USER_POSITION |
| 46 | 396 | 1.54 | 1.58 | SET_USER_POSITION |
| 47 | 398 | 1.03 | 1.53 | LEAVE_PARTY |
| 48 | 390 | 2.83 | 2.88 | SET_USER_POSITION |
| 49 | 400 | 0.99 | 1.10 | SET_USER_POSITION |
| 50 | 396 | 1.08 | 1.40 | SET_USER_POSITION |
| 51 | 384 | 1.08 | 3.39 | SET_USER_POSITION |
| 52 | 389 | 15.37 | 38.50 | ENTER_SELECT_DUNGEON |
| 53 | 403 | 0.89 | 1.02 | SET_USER_POSITION |
| 54 | 392 | 2.27 | 37.37 | ENTER_SELECT_DUNGEON |
| 55 | 398 | 0.88 | 3.20 | SET_USER_POSITION |
| 56 | 395 | 16.01 | 34.59 | ENTER_SELECT_DUNGEON |
| 57 | 373 | 18.06 | 32.38 | SELECT_DUNGEON |
| 58 | 400 | 0.76 | 1.52 | CHECK_USER_CONNECTION |
| 59 | 400 | 3.67 | 6.08 | CHECK_USER_CONNECTION |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 47395 | 0.20 | 0.49 | 1.04 | 27.39 | 45.05 |
| 10 并发登录的轮内 | 3108 | 0.09 | 0.15 | 0.20 | 1.58 | 217.6 |
| 登录阶段整体（182–233 s） | 20266 | 0.18 | 0.44 | 0.96 | 33.78 | 217.6 |

每轮时长（开始 → 10 个 LOGIN 都应答）：1308, 1294, 1309, 1308, 1294, 1311 ms。

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60 | 352.0 | 663.7 | 671.1 | 671.1 | 671.1 |
| 启动器登录（HTTP，含校验） | 60 | 638.7 | 641.3 | 642.7 | 642.7 | 642.7 |
| Game LOGIN 0x0001 | 60 | 255.4 | 573.9 | 574.7 | 574.7 | 574.7 |

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 197656 包/秒（5929693 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 47395 | 0.20 | 0.49 | 1.04 | 27.39 | 45.05 |
| 其余连接，灌包期间 | 11857 | 0.08 | 0.15 | 0.57 | 15.34 | 29.28 |
| 灌包连接自身（含其窗口内排队） | 5929724 | 0.06 | 0.11 | 0.14 | 1.14 | 6.04 |

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 21 | 39291 | first selection | 1.3 |
| EPLP | 同伴 | 78 | 3614 | leave dungeon | 83.6 |
| EPLP | 请求者 | 78 | 3574 | leave dungeon | 127.5 |
| SET_USER_AREA | 同伴 | 6 | 3060 | town | 5.6 |
| SET_USER_AREA | 请求者 | 12 | 2905 | town | 5.6 |
| SELECT_DUNGEON | 请求者 | 80 | 1367 | party dungeon entry | 14.0 |
| SELECT_DUNGEON | 同伴 | 80 | 1367 | party dungeon entry | 14.0 |
| ENTER_SELECT_DUNGEON | 请求者 | 80 | 707 | party dungeon entry | 241.3 |
| ENTER_SELECT_DUNGEON | 同伴 | 80 | 707 | party dungeon entry | 241.3 |
| LOGIN | 请求者 | 21 | 339 | login | 0.2 |
| RESPONSE_PEER | 同伴 | 81 | 318 | party | 9.7 |
| RESPONSE_PEER | 请求者 | 81 | 316 | party | 9.7 |
| LEAVE_PARTY | 同伴 | 78 | 187 | leave party | 25.3 |
| LEAVE_PARTY | 请求者 | 78 | 148 | leave party | 25.3 |
| CREATE_CHARACTER | 请求者 | 21 | 137 | create | 1.1 |
| REQUEST_PEER | 同伴 | 81 | 100 | party | 48.5 |
| REQUEST_PEER | 请求者 | 81 | 72 | party | 48.5 |
| FINISH_LOADING | 请求者 | 160 | 36 | party dungeon entry | 14.1 |
| FINISH_LOADING | 同伴 | 160 | 0 | party dungeon entry | 14.1 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 1.04 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 0.20 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 0.57 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 6 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 567.5 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `RegisterAccountCommand`：样本 81，p99.9 670.7 ms，max 670.7 ms（出现于 t=212.7 s；冷启动窗内 max 75.82 ms，之后 max 670.7 ms）
- `AuthenticateAndIssueTicketCommand`：样本 81，p99.9 642.4 ms，max 642.4 ms（出现于 t=182.7 s；冷启动窗内 max 70.53 ms，之后 max 642.4 ms）
- `RedeemGameLoginCommand`：样本 81，p99.9 574.5 ms，max 574.5 ms（出现于 t=223.3 s；冷启动窗内 max 1.76 ms，之后 max 574.5 ms）
- `PrepareSelectedCharacterStateCommand`：样本 21，p99.9 492.9 ms，max 492.9 ms（出现于 t=0.8 s；冷启动窗内 max 492.9 ms，之后 max – ms）
- `AdvanceCreatureClockCommand`：样本 337，p99.9 330.0 ms，max 330.0 ms（出现于 t=223.3 s；冷启动窗内 max 65.28 ms，之后 max 330.0 ms）
- `EnterDungeonCommand`：样本 80，p99.9 277.3 ms，max 277.3 ms（出现于 t=36.8 s；冷启动窗内 max 277.3 ms，之后 max 214.3 ms）

## 错误与无回包

错误 0 条；背景 agent 重连 0 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 80 次；服务端关闭连接或超时的请求 0 个。

端到端 > 250 ms 的请求（setup 与 background，含无回包按等到关闭/收口计）：2 个。
- SELECT_CHARACTER：1 个，最大 568 ms，发出时刻 0.3 s
- SELECT_DUNGEON：1 个，最大 285 ms，发出时刻 36.5 s

失败的命令 0 条：

