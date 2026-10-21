# 基线测量 — b2（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1536 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 321 s。
- 稳态窗实际包速率：57.5 包/秒（6897 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 4158 | 60.29% |
| SET_USER_POSITION | 2402 | 34.83% |
| STEP_FENCE | 200 | 2.90% |
| SET_USER_AREA | 35 | 0.51% |
| FINISH_LOADING | 18 | 0.26% |
| SELECT_CHARACTER | 16 | 0.23% |
| RETURN_SELECT_CHARACTER | 12 | 0.17% |
| EPLP | 9 | 0.13% |
| RESPONSE_PEER | 8 | 0.12% |
| ENTER_SELECT_DUNGEON | 8 | 0.12% |
| SELECT_DUNGEON | 8 | 0.12% |
| LOGIN | 7 | 0.10% |
| REQUEST_PEER | 7 | 0.10% |
| LEAVE_PARTY | 6 | 0.09% |
| CREATE_CHARACTER | 3 | 0.04% |

## 端到端延迟（稳态窗）

口径：客户端写出请求帧之前取时间戳 → 该请求第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到时取时间戳。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。没有回包的请求不计入分布，单列「无回包」。

| 请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 无回包 |
|---|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 4158 | 0.22 | 0.45 | 4.85 | 7.84 | 9.50 | 0 |
| SET_USER_POSITION | 2402 | 0.30 | 0.54 | 6.22 | 31.41 | 46.41 | 0 |
| SET_USER_AREA | 35 | 10.00 | 52.47 | 181.5 | 181.5 | 181.5 | 0 |
| FINISH_LOADING | 18 | 0.17 | 32.90 | 32.90 | 32.90 | 32.90 | 0 |
| SELECT_CHARACTER | 16 | 312.9 | 31856.5 | 31856.5 | 31856.5 | 31856.5 | 0 |
| RETURN_SELECT_CHARACTER | 12 | 28617.6 | 30044.2 | 30044.2 | 30044.2 | 30044.2 | 0 |
| EPLP | 9 | 26213.1 | 26611.4 | 26611.4 | 26611.4 | 26611.4 | 0 |
| RESPONSE_PEER | 8 | 0.75 | 123.5 | 123.5 | 123.5 | 123.5 | 0 |
| ENTER_SELECT_DUNGEON | 8 | 24.42 | 32.45 | 32.45 | 32.45 | 32.45 | 0 |
| SELECT_DUNGEON | 8 | 15232.0 | 26560.3 | 26560.3 | 26560.3 | 26560.3 | 0 |
| LOGIN | 7 | 54.59 | 138.2 | 138.2 | 138.2 | 138.2 | 0 |
| REQUEST_PEER | 0 | – | – | – | – | – | 7 |
| LEAVE_PARTY | 6 | 0.95 | 17.60 | 17.60 | 17.60 | 17.60 | 0 |
| CREATE_CHARACTER | 3 | 30.60 | 204.8 | 204.8 | 204.8 | 204.8 | 0 |
| **全部有回包请求** | 6690 | 0.25 | 0.61 | 29.62 | 29868.1 | 31856.5 | 7 |


## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| LeaveDungeonCommand | Direct | 12 | 26204.1 | 88641.2 | 88641.2 | 88641.2 | 88641.2 | 281.8 | 0 |
| EnterDungeonCommand | Transactional | 15 | 19764.9 | 75413.0 | 75413.0 | 75413.0 | 75413.0 | 281.8 | 2 |
| ExpireCharacterNameTagCommand | Transactional | 58 | 0.07 | 30116.7 | 64857.7 | 64857.7 | 64857.7 | 281.9 | 35 |
| PrepareSelectedCharacterSessionCommand | Transactional | 88 | 76.16 | 60111.8 | 60778.3 | 60778.3 | 60778.3 | 281.8 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 38 | 26523.3 | 30043.8 | 60213.9 | 60213.9 | 60213.9 | 281.8 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 115 | 837.5 | 60002.7 | 60003.1 | 60003.5 | 60003.5 | 281.2 | 12 |
| AdvanceWeaknessRecoveryCommand | Transactional | 5 | 8946.6 | 41910.5 | 41910.5 | 41910.5 | 41910.5 | 281.9 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 70 | 311.7 | 30230.6 | 30244.9 | 30244.9 | 30244.9 | 35.4 | 1 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 72 | 93.39 | 30080.7 | 30233.3 | 30233.3 | 30233.3 | 35.3 | 2 |
| PrepareSelectedCharacterStateCommand | Transactional | 75 | 71.33 | 30039.7 | 30211.2 | 30211.2 | 30211.2 | 127.7 | 2 |
| RefillOnlineSouthernTicketCommand | Transactional | 69 | 272.7 | 30142.8 | 30209.6 | 30209.6 | 30209.6 | 127.7 | 4 |
| RefillOnlineBossTowerTicketCommand | Transactional | 65 | 275.0 | 30173.0 | 30207.3 | 30207.3 | 30207.3 | 127.7 | 0 |
| AdvanceCreatureClockCommand | Transactional | 38 | 29.95 | 30141.2 | 30195.4 | 30195.4 | 30195.4 | 127.7 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 63 | 30.22 | 796.0 | 30155.4 | 30155.4 | 30155.4 | 97.1 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 65 | 100.8 | 850.5 | 30155.3 | 30155.3 | 30155.3 | 97.1 | 1 |
| CleanupExpiredCharacterItemsCommand | Transactional | 86 | 29.30 | 975.1 | 30141.2 | 30141.2 | 30141.2 | 319.6 | 1 |
| InitializeHousingCommand | Transactional | 76 | 12.11 | 631.3 | 30125.0 | 30125.0 | 30125.0 | 188.9 | 0 |
| RedeemGameLoginCommand | Transactional | 103 | 334.0 | 921.2 | 1138.4 | 30060.9 | 30060.9 | 65.9 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 82 | 33.09 | 1203.6 | 30059.0 | 30059.0 | 30059.0 | 65.9 | 6 |
| RegisterAccountCommand | Transactional | 81 | 639.5 | 29475.6 | 29730.2 | 29730.2 | 29730.2 | 221.7 | 0 |
| CreateCharacterCommand | Transactional | 27 | 79.80 | 201.1 | 688.0 | 688.0 | 688.0 | 189.6 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 38 | 0.28 | 459.2 | 602.6 | 602.6 | 602.6 | 36.0 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 89 | 0.05 | 42.84 | 43.33 | 43.33 | 43.33 | 4.2 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 70 | 0.02 | 0.20 | 7.38 | 7.38 | 7.38 | 286.2 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 51 | 0.04 | 2.56 | 7.14 | 7.14 | 7.14 | 66.8 | 0 |
| GetAccountCharacterRosterCommand | Direct | 27 | 0.25 | 2.92 | 6.56 | 6.56 | 6.56 | 2.1 | 0 |
| AcceptPartyInviteCommand | Direct | 15 | 0.06 | 6.27 | 6.27 | 6.27 | 6.27 | 40.8 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 168 | 0.01 | 6.06 | 6.09 | 6.09 | 6.09 | 2.1 | 0 |
| RequestPartyInviteCommand | Direct | 15 | 1.98 | 4.01 | 4.01 | 4.01 | 4.01 | 40.8 | 0 |
| CompensateSelectedCharacterSessionCommand | Workflow | 20 | 0.10 | 1.01 | 3.39 | 3.39 | 3.39 | 35.2 | 9 |
| LeavePartyCommand | Direct | 10 | 0.04 | 1.35 | 1.35 | 1.35 | 1.35 | 102.6 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 26 | 0.02 | 0.29 | 0.65 | 0.65 | 0.65 | 66.1 | 0 |

命令日志行 1832 条（其中失败 75）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组），按发出时刻所在的秒聚合。最长单次停顿 = 窗口内客户端看到的最大单请求延迟。

最长单次停顿：**30944.4 ms**（SELECT_CHARACTER，t=35.37 s，solo03）。冷启动窗整体：2052 | 0.38 | 13.50 | 668.1 | 30807.1 | 30944.4（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 0 | – | – | – |
| 1 | 14 | 941.1 | 941.1 | LOGIN |
| 2 | 31 | 668.1 | 668.1 | SELECT_CHARACTER |
| 3 | 0 | – | – | – |
| 4 | 93 | 814.3 | 814.3 | RETURN_SELECT_CHARACTER |
| 5 | 87 | 30807.1 | 30807.1 | SELECT_CHARACTER |
| 6 | 77 | 9.79 | 9.79 | SET_USER_AREA |
| 7 | 72 | 17.41 | 17.41 | SET_USER_AREA |
| 8 | 71 | 26524.0 | 26524.0 | RETURN_SELECT_CHARACTER |
| 9 | 56 | 17.90 | 17.90 | SET_USER_AREA |
| 10 | 39 | 25428.5 | 25428.5 | RETURN_SELECT_CHARACTER |
| 11 | 16 | 18.09 | 18.09 | SET_USER_AREA |
| 12 | 19 | 8.04 | 8.04 | SET_USER_AREA |
| 13 | 20 | 0.63 | 0.63 | CHECK_USER_CONNECTION |
| 14 | 18 | 20482.4 | 20482.4 | RETURN_SELECT_CHARACTER |
| 15 | 0 | – | – | – |
| 16 | 0 | – | – | – |
| 17 | 0 | – | – | – |
| 18 | 0 | – | – | – |
| 19 | 0 | – | – | – |
| 20 | 0 | – | – | – |
| 21 | 0 | – | – | – |
| 22 | 0 | – | – | – |
| 23 | 0 | – | – | – |
| 24 | 0 | – | – | – |
| 25 | 0 | – | – | – |
| 26 | 0 | – | – | – |
| 27 | 0 | – | – | – |
| 28 | 0 | – | – | – |
| 29 | 0 | – | – | – |
| 30 | 0 | – | – | – |
| 31 | 0 | – | – | – |
| 32 | 0 | – | – | – |
| 33 | 0 | – | – | – |
| 34 | 0 | – | – | – |
| 35 | 30 | 30944.4 | 30944.4 | SELECT_CHARACTER |
| 36 | 178 | 17.42 | 29.43 | SET_USER_AREA |
| 37 | 176 | 28.46 | 30.22 | SET_USER_AREA |
| 38 | 171 | 1.10 | 27432.2 | RETURN_SELECT_CHARACTER |
| 39 | 157 | 1.08 | 1.09 | SET_USER_POSITION |
| 40 | 111 | 1.76 | 13.19 | RESPONSE_PEER |
| 41 | 114 | 1.63 | 2.65 | RESPONSE_PEER |
| 42 | 120 | 1.03 | 1.12 | SET_USER_POSITION |
| 43 | 120 | 1.08 | 3.94 | SET_USER_POSITION |
| 44 | 120 | 1.04 | 1.14 | SET_USER_POSITION |
| 45 | 112 | 48.27 | 20501.9 | SELECT_DUNGEON |
| 46 | 30 | 19850.5 | 19850.5 | SELECT_DUNGEON |
| 47 | 0 | – | – | – |
| 48 | 0 | – | – | – |
| 49 | 0 | – | – | – |
| 50 | 0 | – | – | – |
| 51 | 0 | – | – | – |
| 52 | 0 | – | – | – |
| 53 | 0 | – | – | – |
| 54 | 0 | – | – | – |
| 55 | 0 | – | – | – |
| 56 | 0 | – | – | – |
| 57 | 0 | – | – | – |
| 58 | 0 | – | – | – |
| 59 | 0 | – | – | – |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 6690 | 0.25 | 0.61 | 29.62 | 29868.1 | 31856.5 |
| 10 并发登录的轮内 | 2958 | 0.20 | 1.89 | 825.1 | 29385.7 | 55451.9 |
| 登录阶段整体（182–289 s） | 3479 | 0.19 | 4.32 | 761.8 | 29395.9 | 55451.9 |

每轮时长（开始 → 10 个 LOGIN 都应答）：8287, 89735, 2290, 1281, 1896, 1888 ms。

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 50 | 636.3 | 7397.2 | 7524.5 | 7524.5 | 7524.5 |
| 启动器登录（HTTP，含校验） | 50 | 799.6 | 1203.3 | 1203.5 | 1203.5 | 1203.5 |
| Game LOGIN 0x0001 | 50 | 337.4 | 714.4 | 839.8 | 839.8 | 839.8 |

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 200348 包/秒（6010440 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 6690 | 0.25 | 0.61 | 29.62 | 29868.1 | 31856.5 |
| 其余连接，灌包期间 | 1206 | 0.09 | 4.69 | 88.05 | 29378.0 | 29877.4 |
| 灌包连接自身（含其窗口内排队） | 6010471 | 0.06 | 0.11 | 0.14 | 1.22 | 9.71 |

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 61 | 39427 | selection after create | 189.6 |
| RETURN_SELECT_CHARACTER | 请求者 | 37 | 36530 | return to selection | 191.4 |
| REQUEST_PEER | 同伴 | 15 | 9090 | party | 39.9 |
| REQUEST_PEER | 请求者 | 15 | 9062 | party | 39.9 |
| SET_USER_AREA | 同伴 | 8 | 3572 | town | 35.9 |
| SET_USER_AREA | 请求者 | 202 | 2898 | town | 195.3 |
| EPLP | 请求者 | 10 | 2005 | leave dungeon | 72.0 |
| EPLP | 同伴 | 10 | 2005 | leave dungeon | 72.0 |
| SELECT_DUNGEON | 请求者 | 13 | 1367 | party dungeon entry | 46.3 |
| SELECT_DUNGEON | 同伴 | 13 | 1367 | party dungeon entry | 46.3 |
| LEAVE_PARTY | 同伴 | 10 | 663 | leave party | 103.0 |
| ENTER_SELECT_DUNGEON | 请求者 | 15 | 632 | party dungeon entry | 113.0 |
| ENTER_SELECT_DUNGEON | 同伴 | 15 | 632 | party dungeon entry | 113.0 |
| LEAVE_PARTY | 请求者 | 10 | 624 | leave party | 103.0 |
| LOGIN | 请求者 | 53 | 339 | login | 1.5 |
| CREATE_CHARACTER | 请求者 | 27 | 313 | create | 188.9 |
| RESPONSE_PEER | 同伴 | 15 | 238 | party | 40.8 |
| RESPONSE_PEER | 请求者 | 15 | 236 | party | 40.8 |
| FINISH_LOADING | 请求者 | 26 | 36 | party dungeon entry | 66.1 |
| FINISH_LOADING | 同伴 | 26 | 0 | party dungeon entry | 66.1 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 29.62 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 825.1 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 88.05 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 22 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 30944.4 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `LeaveDungeonCommand`：样本 12，p99.9 88641.2 ms，max 88641.2 ms（出现于 t=281.8 s；冷启动窗内 max – ms，之后 max 88641.2 ms）
- `EnterDungeonCommand`：样本 15，p99.9 75413.0 ms，max 75413.0 ms（出现于 t=281.8 s；冷启动窗内 max – ms，之后 max 75413.0 ms）
- `ExpireCharacterNameTagCommand`：样本 58，p99.9 64857.7 ms，max 64857.7 ms（出现于 t=281.9 s；冷启动窗内 max 0.33 ms，之后 max 64857.7 ms）
- `PrepareSelectedCharacterSessionCommand`：样本 88，p99.9 60778.3 ms，max 60778.3 ms（出现于 t=281.8 s；冷启动窗内 max 30217.2 ms，之后 max 60778.3 ms）
- `PrepareReturnToSelectionCommand`：样本 38，p99.9 60213.9 ms，max 60213.9 ms（出现于 t=281.8 s；冷启动窗内 max 26523.3 ms，之后 max 60213.9 ms）
- `AuthenticateAndIssueTicketCommand`：样本 115，p99.9 60003.5 ms，max 60003.5 ms（出现于 t=281.2 s；冷启动窗内 max 1009.1 ms，之后 max 60003.5 ms）
- `AdvanceWeaknessRecoveryCommand`：样本 5，p99.9 41910.5 ms，max 41910.5 ms（出现于 t=281.9 s；冷启动窗内 max – ms，之后 max 41910.5 ms）
- `RefillOnlineTauTicketCommand`：样本 70，p99.9 30244.9 ms，max 30244.9 ms（出现于 t=35.4 s；冷启动窗内 max 30244.9 ms，之后 max 30145.6 ms）
- `ApplySelectedCharacterDailyRefillCommand`：样本 72，p99.9 30233.3 ms，max 30233.3 ms（出现于 t=35.3 s；冷启动窗内 max 30233.3 ms，之后 max 30185.0 ms）
- `PrepareSelectedCharacterStateCommand`：样本 75，p99.9 30211.2 ms，max 30211.2 ms（出现于 t=127.7 s；冷启动窗内 max 577.6 ms，之后 max 30211.2 ms）
- `RefillOnlineSouthernTicketCommand`：样本 69，p99.9 30209.6 ms，max 30209.6 ms（出现于 t=127.7 s；冷启动窗内 max 354.1 ms，之后 max 30209.6 ms）
- `RefillOnlineBossTowerTicketCommand`：样本 65，p99.9 30207.3 ms，max 30207.3 ms（出现于 t=127.7 s；冷启动窗内 max 333.8 ms，之后 max 30207.3 ms）
- `AdvanceCreatureClockCommand`：样本 38，p99.9 30195.4 ms，max 30195.4 ms（出现于 t=127.7 s；冷启动窗内 max 401.7 ms，之后 max 30195.4 ms）
- `ReviveSelectedCharacterPetCommand`：样本 63，p99.9 30155.4 ms，max 30155.4 ms（出现于 t=97.1 s；冷启动窗内 max 66.96 ms，之后 max 30155.4 ms）
- `SynchronizeSelectedCharacterQuestsCommand`：样本 65，p99.9 30155.3 ms，max 30155.3 ms（出现于 t=97.1 s；冷启动窗内 max 284.1 ms，之后 max 30155.3 ms）
- `CleanupExpiredCharacterItemsCommand`：样本 86，p99.9 30141.2 ms，max 30141.2 ms（出现于 t=319.6 s；冷启动窗内 max 274.2 ms，之后 max 30141.2 ms）
- `InitializeHousingCommand`：样本 76，p99.9 30125.0 ms，max 30125.0 ms（出现于 t=188.9 s；冷启动窗内 max 128.9 ms，之后 max 30125.0 ms）
- `RedeemGameLoginCommand`：样本 103，p99.9 30060.9 ms，max 30060.9 ms（出现于 t=65.9 s；冷启动窗内 max 921.2 ms，之后 max 30060.9 ms）
- `EnsureDailyContractPuppetsCommand`：样本 82，p99.9 30059.0 ms，max 30059.0 ms（出现于 t=65.9 s；冷启动窗内 max 126.0 ms，之后 max 30059.0 ms）
- `RegisterAccountCommand`：样本 81，p99.9 29730.2 ms，max 29730.2 ms（出现于 t=221.7 s；冷启动窗内 max 1007.0 ms，之后 max 29730.2 ms）
- `CreateCharacterCommand`：样本 27，p99.9 688.0 ms，max 688.0 ms（出现于 t=189.6 s；冷启动窗内 max 115.2 ms，之后 max 688.0 ms）
- `CompleteReturnToSelectionCommand`：样本 38，p99.9 602.6 ms，max 602.6 ms（出现于 t=36.0 s；冷启动窗内 max 602.6 ms，之后 max 304.4 ms）

## 错误与无回包

错误 36 条；背景 agent 重连 26 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 13 次；服务端关闭连接或超时的请求 25 个。

端到端 > 250 ms 的请求（setup 与 background，含无回包按等到关闭/收口计）：159 个。
- SELECT_CHARACTER：61 个，最大 60003 ms，发出时刻 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.1, 2.2, 2.2, 2.2, 2.2, 2.2 … s
- RETURN_SELECT_CHARACTER：36 个，最大 60002 ms，发出时刻 4.2, 4.6, 4.7, 4.8, 8.9, 10.2, 10.7, 14.9, 38.5, 67.7, 68.8, 74.3 … s
- LOGIN：28 个，最大 30061 ms，发出时刻 1.1, 1.2, 1.2, 1.3, 1.4, 1.4, 1.5, 1.6, 1.6, 1.7, 1.8, 4.7 … s
- SELECT_DUNGEON：15 个，最大 60001 ms，发出时刻 45.8, 46.3, 46.5, 70.7, 113.0, 114.1, 132.6, 138.4, 142.4, 173.7, 173.8, 203.7 … s
- EPLP：12 个，最大 60003 ms，发出时刻 71.0, 71.0, 72.0, 102.2, 131.8, 132.1, 163.0, 163.5, 164.1, 193.2, 193.9, 227.4 s
- SET_USER_AREA：4 个，最大 29809 ms，发出时刻 4.7, 4.9, 191.3, 288.9 s
- REQUEST_PEER：2 个，最大 26016 ms，发出时刻 39.9, 107.4 s
- CREATE_CHARACTER：1 个，最大 690 ms，发出时刻 188.9 s

失败的命令 75 条：ApplySelectedCharacterDailyRefillCommand ×2（最长 30186 ms）；ExpireCharacterNameTagCommand ×35（最长 237 ms）；PrepareSelectedCharacterStateCommand ×2（最长 30050 ms）；RefillOnlineTauTicketCommand ×1（最长 369 ms）；CompensateSelectedCharacterSessionCommand ×9（最长 0 ms）；RefillOnlineSouthernTicketCommand ×4（最长 30185 ms）；SynchronizeSelectedCharacterQuestsCommand ×1（最长 282 ms）；EnterDungeonCommand ×2（最长 75413 ms）；AuthenticateAndIssueTicketCommand ×12（最长 60004 ms）；CleanupExpiredCharacterItemsCommand ×1（最长 975 ms）；EnsureDailyContractPuppetsCommand ×6（最长 1204 ms）
- t=35.3s pair00: pair00M: connection closed during SELECT_CHARACTER after 30235 ms (first reply CMD  0x0004 (270B) 01000000).
- t=65.0s pair01: pair01M: SELECT_CHARACTER timed out.
- t=65.1s solo06: solo06: SELECT_CHARACTER timed out.
- t=65.2s solo02: solo02: SELECT_CHARACTER timed out.
- t=65.3s solo05: solo05: SELECT_CHARACTER timed out.
- t=96.0s solo07: solo07: SELECT_CHARACTER timed out.
- t=97.1s solo05: solo05: connection closed during SELECT_CHARACTER after 30749 ms (first reply CMD  0x0004 (270B) 01000000).
- t=127.6s pair00: pair00M: connection closed during SELECT_CHARACTER after 30250 ms (first reply CMD  0x0004 (270B) 01000000).
- t=157.5s solo01: solo01: SELECT_CHARACTER timed out.
- t=157.5s solo04: solo04: SELECT_CHARACTER timed out.
- t=188.8s pair00: pair00L: connection closed during SELECT_CHARACTER after 30350 ms (first reply CMD  0x0004 (270B) 01000000).
- t=221.1s pair00: pair00M: connection closed during SELECT_CHARACTER after 30089 ms (first reply none).
- t=251.8s pair01: pair01L: connection closed during SELECT_DUNGEON after 45800 ms (first reply none).
- t=253.2s pair03: pair03L: EPLP timed out.
- t=253.9s pair04: pair04L: EPLP timed out.
- t=266.4s pair02: pair02L: SELECT_DUNGEON timed out.
- t=281.1s solo02: solo02: SELECT_CHARACTER timed out.
- t=281.2s b2-burst1-2: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.2s b2-burst1-0: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.3s b2-burst1-1: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.4s b2-burst1-5: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.4s b2-burst1-3: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.5s b2-burst1-4: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.5s b2-burst1-7: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.6s pair00: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.6s b2-burst1-8: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.6s solo00: solo00: RETURN_SELECT_CHARACTER timed out.
- t=281.7s b2-burst1-6: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.7s solo03: solo03: SELECT_CHARACTER timed out.
- t=281.7s solo01: solo01: SELECT_CHARACTER timed out.

