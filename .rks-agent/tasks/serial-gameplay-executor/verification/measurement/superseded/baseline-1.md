# 基线测量 — b1（合成负载）

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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1407 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 321 s。
- 稳态窗实际包速率：53.4 包/秒（6406 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 3729 | 58.21% |
| SET_USER_POSITION | 2311 | 36.08% |
| STEP_FENCE | 209 | 3.26% |
| SET_USER_AREA | 56 | 0.87% |
| SELECT_CHARACTER | 20 | 0.31% |
| RETURN_SELECT_CHARACTER | 15 | 0.23% |
| FINISH_LOADING | 14 | 0.22% |
| LOGIN | 10 | 0.16% |
| REQUEST_PEER | 7 | 0.11% |
| RESPONSE_PEER | 7 | 0.11% |
| ENTER_SELECT_DUNGEON | 7 | 0.11% |
| SELECT_DUNGEON | 7 | 0.11% |
| EPLP | 7 | 0.11% |
| LEAVE_PARTY | 5 | 0.08% |
| CREATE_CHARACTER | 2 | 0.03% |

## 端到端延迟（稳态窗）

口径：客户端写出请求帧之前取时间戳 → 该请求第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到时取时间戳。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。没有回包的请求不计入分布，单列「无回包」。

| 请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | 无回包 |
|---|---:|---:|---:|---:|---:|---:|---:|
| CHECK_USER_CONNECTION | 3729 | 0.19 | 0.41 | 3.51 | 7.20 | 7.74 | 0 |
| SET_USER_POSITION | 2311 | 0.23 | 0.64 | 11.90 | 20.39 | 20.54 | 0 |
| SET_USER_AREA | 56 | 12.21 | 34.68 | 160.9 | 160.9 | 160.9 | 0 |
| SELECT_CHARACTER | 19 | 30262.1 | 31064.3 | 31064.3 | 31064.3 | 31064.3 | 1 |
| RETURN_SELECT_CHARACTER | 15 | 28464.3 | 29951.3 | 29951.3 | 29951.3 | 29951.3 | 0 |
| FINISH_LOADING | 14 | 0.22 | 10.95 | 10.95 | 10.95 | 10.95 | 0 |
| LOGIN | 10 | 129.3 | 364.9 | 364.9 | 364.9 | 364.9 | 0 |
| REQUEST_PEER | 0 | – | – | – | – | – | 7 |
| RESPONSE_PEER | 7 | 0.53 | 1.35 | 1.35 | 1.35 | 1.35 | 0 |
| ENTER_SELECT_DUNGEON | 7 | 24.03 | 34.14 | 34.14 | 34.14 | 34.14 | 0 |
| SELECT_DUNGEON | 7 | 17592.6 | 20946.2 | 20946.2 | 20946.2 | 20946.2 | 0 |
| EPLP | 7 | 26045.3 | 27653.8 | 27653.8 | 27653.8 | 27653.8 | 0 |
| LEAVE_PARTY | 5 | 1.08 | 15.11 | 15.11 | 15.11 | 15.11 | 0 |
| CREATE_CHARACTER | 2 | 41.29 | 43.58 | 43.58 | 43.58 | 43.58 | 0 |
| **全部有回包请求** | 6189 | 0.21 | 1.15 | 32.78 | 30270.2 | 31064.3 | 8 |


## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| LeaveDungeonCommand | Direct | 11 | 26640.9 | 87890.0 | 87890.0 | 87890.0 | 87890.0 | 282.5 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 80 | 61.81 | 30070.8 | 60932.0 | 60932.0 | 60932.0 | 282.5 | 5 |
| PrepareSelectedCharacterSessionCommand | Transactional | 93 | 66.42 | 60270.4 | 60931.9 | 60931.9 | 60931.9 | 282.5 | 0 |
| CreateCharacterCommand | Transactional | 26 | 76.57 | 844.5 | 60124.3 | 60124.3 | 60124.3 | 282.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 117 | 887.4 | 60001.7 | 60002.3 | 60002.4 | 60002.4 | 281.6 | 12 |
| EnterDungeonCommand | Transactional | 14 | 19867.9 | 50220.0 | 50220.0 | 50220.0 | 50220.0 | 96.0 | 2 |
| AdvanceWeaknessRecoveryCommand | Transactional | 5 | 19763.3 | 42512.9 | 42512.9 | 42512.9 | 42512.9 | 282.5 | 0 |
| ExpireCharacterNameTagCommand | Transactional | 74 | 0.07 | 30077.5 | 39133.6 | 39133.6 | 39133.6 | 283.5 | 39 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 75 | 147.9 | 30179.9 | 30838.7 | 30838.7 | 30838.7 | 96.3 | 1 |
| RefillOnlineTauTicketCommand | Transactional | 74 | 213.7 | 30138.4 | 30820.2 | 30820.2 | 30820.2 | 96.2 | 3 |
| RefillOnlineSouthernTicketCommand | Transactional | 69 | 202.1 | 885.8 | 30792.4 | 30792.4 | 30792.4 | 96.3 | 4 |
| RedeemGameLoginCommand | Transactional | 105 | 321.5 | 959.6 | 1024.7 | 30727.8 | 30727.8 | 96.3 | 0 |
| RegisterAccountCommand | Transactional | 81 | 638.9 | 29930.7 | 30184.3 | 30184.3 | 30184.3 | 222.2 | 0 |
| PrepareReturnToSelectionCommand | Transactional | 40 | 26549.5 | 29799.4 | 30140.1 | 30140.1 | 30140.1 | 34.9 | 0 |
| ReviveSelectedCharacterPetCommand | Transactional | 63 | 17.65 | 92.42 | 30113.3 | 30113.3 | 30113.3 | 65.4 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 84 | 36.26 | 30077.3 | 30077.9 | 30077.9 | 30077.9 | 158.0 | 4 |
| CleanupExpiredCharacterItemsCommand | Transactional | 93 | 34.18 | 1055.2 | 30077.5 | 30077.5 | 30077.5 | 158.0 | 2 |
| RefillOnlineBossTowerTicketCommand | Transactional | 64 | 152.5 | 880.8 | 30059.6 | 30059.6 | 30059.6 | 127.8 | 0 |
| AdvanceCreatureClockCommand | Transactional | 28 | 33.38 | 30045.4 | 30059.0 | 30059.0 | 30059.0 | 319.7 | 0 |
| InitializeHousingCommand | Transactional | 80 | 14.83 | 637.9 | 30055.2 | 30055.2 | 30055.2 | 221.5 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 63 | 70.77 | 783.6 | 845.3 | 845.3 | 845.3 | 289.2 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 40 | 0.31 | 152.5 | 458.3 | 458.3 | 458.3 | 4.7 | 0 |
| CompensateSelectedCharacterSessionCommand | Workflow | 21 | 0.05 | 0.78 | 151.7 | 151.7 | 151.7 | 128.0 | 11 |
| ReadItemUsageSnapshotCommand | ReadOnly | 82 | 0.05 | 32.56 | 33.17 | 33.17 | 33.17 | 3.9 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 74 | 0.02 | 0.08 | 12.06 | 12.06 | 12.06 | 2.7 | 0 |
| AcceptPartyInviteCommand | Direct | 15 | 0.05 | 7.17 | 7.17 | 7.17 | 7.17 | 40.3 | 0 |
| GetAccountCharacterRosterCommand | Direct | 26 | 0.23 | 3.28 | 6.87 | 6.87 | 6.87 | 2.0 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 169 | 0.01 | 5.96 | 5.97 | 6.00 | 6.00 | 1.9 | 0 |
| RequestPartyInviteCommand | Direct | 15 | 2.02 | 3.95 | 3.95 | 3.95 | 3.95 | 40.3 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 24 | 0.02 | 0.65 | 0.72 | 0.72 | 0.72 | 65.6 | 0 |
| LeavePartyCommand | Direct | 9 | 0.03 | 0.50 | 0.50 | 0.50 | 0.50 | 101.1 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 38 | 0.04 | 0.14 | 0.36 | 0.36 | 0.36 | 65.6 | 0 |

命令日志行 1852 条（其中失败 83）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组），按发出时刻所在的秒聚合。最长单次停顿 = 窗口内客户端看到的最大单请求延迟。

最长单次停顿：**30574.5 ms**（SELECT_CHARACTER，t=4.72 s，solo06）。冷启动窗整体：1859 | 0.18 | 20.26 | 765.8 | 30494.8 | 30574.5（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 0 | – | – | – |
| 1 | 30 | 884.4 | 884.4 | LOGIN |
| 2 | 15 | 628.6 | 628.6 | SELECT_CHARACTER |
| 3 | 22 | 788.4 | 788.4 | RETURN_SELECT_CHARACTER |
| 4 | 109 | 30494.8 | 30574.5 | SELECT_CHARACTER |
| 5 | 75 | 29098.9 | 29098.9 | SET_USER_AREA |
| 6 | 57 | 7.30 | 7.30 | SET_USER_AREA |
| 7 | 54 | 20.48 | 20.48 | SET_USER_AREA |
| 8 | 46 | 26410.4 | 26410.4 | RETURN_SELECT_CHARACTER |
| 9 | 37 | 25008.9 | 25008.9 | RETURN_SELECT_CHARACTER |
| 10 | 6 | 24698.3 | 24698.3 | RETURN_SELECT_CHARACTER |
| 11 | 0 | – | – | – |
| 12 | 0 | – | – | – |
| 13 | 0 | – | – | – |
| 14 | 0 | – | – | – |
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
| 34 | 4 | 30461.2 | 30461.2 | SELECT_CHARACTER |
| 35 | 121 | 20.32 | 30119.8 | SET_USER_AREA |
| 36 | 155 | 26.27 | 27.10 | SET_USER_AREA |
| 37 | 157 | 26.29 | 27585.0 | RETURN_SELECT_CHARACTER |
| 38 | 139 | 17.76 | 29.24 | SET_USER_AREA |
| 39 | 137 | 18.29 | 23.12 | SET_USER_AREA |
| 40 | 128 | 14.02 | 24.52 | SET_USER_AREA |
| 41 | 135 | 0.34 | 1.71 | RESPONSE_PEER |
| 42 | 140 | 0.24 | 0.25 | SET_USER_POSITION |
| 43 | 132 | 0.30 | 22135.7 | RETURN_SELECT_CHARACTER |
| 44 | 101 | 40.02 | 21086.1 | SELECT_DUNGEON |
| 45 | 59 | 19935.8 | 19935.8 | SELECT_DUNGEON |
| 46 | 0 | – | – | – |
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
| 稳态窗（对照） | 6189 | 0.21 | 1.15 | 32.78 | 30270.2 | 31064.3 |
| 10 并发登录的轮内 | 3131 | 0.20 | 1.61 | 895.4 | 28826.4 | 56854.1 |
| 登录阶段整体（182–289 s） | 3505 | 0.19 | 4.03 | 846.7 | 29565.7 | 56854.1 |

每轮时长（开始 → 10 个 LOGIN 都应答）：8516, 90188, 2353, 1280, 1652, 1864 ms。

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 50 | 639.1 | 7617.1 | 7743.5 | 7743.5 | 7743.5 |
| 启动器登录（HTTP，含校验） | 50 | 756.5 | 1027.0 | 1027.6 | 1027.6 | 1027.6 |
| Game LOGIN 0x0001 | 50 | 365.8 | 684.6 | 811.4 | 811.4 | 811.4 |

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 199443 包/秒（5983318 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 6189 | 0.21 | 1.15 | 32.78 | 30270.2 | 31064.3 |
| 其余连接，灌包期间 | 1757 | 0.08 | 5.66 | 30.79 | 29047.7 | 29368.8 |
| 灌包连接自身（含其窗口内排队） | 5983349 | 0.06 | 0.11 | 0.15 | 0.86 | 18.55 |

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 62 | 39427 | selection after create | 189.8 |
| RETURN_SELECT_CHARACTER | 请求者 | 40 | 33244 | return to selection | 290.3 |
| REQUEST_PEER | 同伴 | 15 | 19310 | party | 294.1 |
| REQUEST_PEER | 请求者 | 15 | 17202 | party | 294.1 |
| SET_USER_AREA | 请求者 | 245 | 4407 | town | 5.9 |
| SET_USER_AREA | 同伴 | 6 | 2309 | town | 191.5 |
| SELECT_DUNGEON | 请求者 | 12 | 1681 | party dungeon entry | 44.5 |
| SELECT_DUNGEON | 同伴 | 12 | 1367 | party dungeon entry | 44.5 |
| EPLP | 请求者 | 9 | 1134 | leave dungeon | 70.5 |
| EPLP | 同伴 | 9 | 1134 | leave dungeon | 70.5 |
| ENTER_SELECT_DUNGEON | 请求者 | 14 | 656 | party dungeon entry | 44.5 |
| ENTER_SELECT_DUNGEON | 同伴 | 14 | 656 | party dungeon entry | 44.5 |
| LOGIN | 请求者 | 55 | 339 | login | 1.1 |
| CREATE_CHARACTER | 请求者 | 25 | 313 | create | 188.9 |
| RESPONSE_PEER | 同伴 | 15 | 214 | party | 40.3 |
| RESPONSE_PEER | 请求者 | 15 | 212 | party | 40.3 |
| LEAVE_PARTY | 同伴 | 9 | 163 | leave party | 101.1 |
| LEAVE_PARTY | 请求者 | 9 | 124 | leave party | 101.1 |
| FINISH_LOADING | 请求者 | 24 | 36 | party dungeon entry | 65.6 |
| FINISH_LOADING | 同伴 | 24 | 0 | party dungeon entry | 65.6 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 32.78 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 895.4 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 30.79 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 23 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 30574.5 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `LeaveDungeonCommand`：样本 11，p99.9 87890.0 ms，max 87890.0 ms（出现于 t=282.5 s；冷启动窗内 max – ms，之后 max 87890.0 ms）
- `PrepareSelectedCharacterStateCommand`：样本 80，p99.9 60932.0 ms，max 60932.0 ms（出现于 t=282.5 s；冷启动窗内 max 30185.5 ms，之后 max 60932.0 ms）
- `PrepareSelectedCharacterSessionCommand`：样本 93，p99.9 60931.9 ms，max 60931.9 ms（出现于 t=282.5 s；冷启动窗内 max 30185.5 ms，之后 max 60931.9 ms）
- `CreateCharacterCommand`：样本 26，p99.9 60124.3 ms，max 60124.3 ms（出现于 t=282.5 s；冷启动窗内 max 102.5 ms，之后 max 60124.3 ms）
- `AuthenticateAndIssueTicketCommand`：样本 117，p99.9 60002.4 ms，max 60002.4 ms（出现于 t=281.6 s；冷启动窗内 max 949.4 ms，之后 max 60002.4 ms）
- `EnterDungeonCommand`：样本 14，p99.9 50220.0 ms，max 50220.0 ms（出现于 t=96.0 s；冷启动窗内 max – ms，之后 max 50220.0 ms）
- `AdvanceWeaknessRecoveryCommand`：样本 5，p99.9 42512.9 ms，max 42512.9 ms（出现于 t=282.5 s；冷启动窗内 max – ms，之后 max 42512.9 ms）
- `ExpireCharacterNameTagCommand`：样本 74，p99.9 39133.6 ms，max 39133.6 ms（出现于 t=283.5 s；冷启动窗内 max 30298.2 ms，之后 max 39133.6 ms）
- `ApplySelectedCharacterDailyRefillCommand`：样本 75，p99.9 30838.7 ms，max 30838.7 ms（出现于 t=96.3 s；冷启动窗内 max 30198.0 ms，之后 max 30838.7 ms）
- `RefillOnlineTauTicketCommand`：样本 74，p99.9 30820.2 ms，max 30820.2 ms（出现于 t=96.2 s；冷启动窗内 max 30218.8 ms，之后 max 30820.2 ms）
- `RefillOnlineSouthernTicketCommand`：样本 69，p99.9 30792.4 ms，max 30792.4 ms（出现于 t=96.3 s；冷启动窗内 max 322.6 ms，之后 max 30792.4 ms）
- `RedeemGameLoginCommand`：样本 105，p99.9 30727.8 ms，max 30727.8 ms（出现于 t=96.3 s；冷启动窗内 max 865.4 ms，之后 max 30727.8 ms）
- `RegisterAccountCommand`：样本 81，p99.9 30184.3 ms，max 30184.3 ms（出现于 t=222.2 s；冷启动窗内 max 948.7 ms，之后 max 30184.3 ms）
- `PrepareReturnToSelectionCommand`：样本 40，p99.9 30140.1 ms，max 30140.1 ms（出现于 t=34.9 s；冷启动窗内 max 30140.1 ms，之后 max 29799.6 ms）
- `ReviveSelectedCharacterPetCommand`：样本 63，p99.9 30113.3 ms，max 30113.3 ms（出现于 t=65.4 s；冷启动窗内 max 22.72 ms，之后 max 30113.3 ms）
- `EnsureDailyContractPuppetsCommand`：样本 84，p99.9 30077.9 ms，max 30077.9 ms（出现于 t=158.0 s；冷启动窗内 max 112.6 ms，之后 max 30077.9 ms）
- `CleanupExpiredCharacterItemsCommand`：样本 93，p99.9 30077.5 ms，max 30077.5 ms（出现于 t=158.0 s；冷启动窗内 max 128.9 ms，之后 max 30077.5 ms）
- `RefillOnlineBossTowerTicketCommand`：样本 64，p99.9 30059.6 ms，max 30059.6 ms（出现于 t=127.8 s；冷启动窗内 max 309.4 ms，之后 max 30059.6 ms）
- `AdvanceCreatureClockCommand`：样本 28，p99.9 30059.0 ms，max 30059.0 ms（出现于 t=319.7 s；冷启动窗内 max 378.5 ms，之后 max 30059.0 ms）
- `InitializeHousingCommand`：样本 80，p99.9 30055.2 ms，max 30055.2 ms（出现于 t=221.5 s；冷启动窗内 max 128.6 ms，之后 max 30055.2 ms）
- `SynchronizeSelectedCharacterQuestsCommand`：样本 63，p99.9 845.3 ms，max 845.3 ms（出现于 t=289.2 s；冷启动窗内 max 261.6 ms，之后 max 845.3 ms）
- `CompleteReturnToSelectionCommand`：样本 40，p99.9 458.3 ms，max 458.3 ms（出现于 t=4.7 s；冷启动窗内 max 458.3 ms，之后 max 152.5 ms）
- `CompensateSelectedCharacterSessionCommand`：样本 21，p99.9 151.7 ms，max 151.7 ms（出现于 t=128.0 s；冷启动窗内 max 0.78 ms，之后 max 151.7 ms）

## 错误与无回包

错误 37 条；背景 agent 重连 27 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 12 次；服务端关闭连接或超时的请求 26 个。

端到端 > 250 ms 的请求（setup 与 background，含无回包按等到关闭/收口计）：175 个。
- SELECT_CHARACTER：73 个，最大 60002 ms，发出时刻 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 … s
- RETURN_SELECT_CHARACTER：38 个，最大 30149 ms，发出时刻 3.9, 4.3, 4.5, 4.8, 8.5, 9.9, 10.3, 38.0, 43.6, 98.2, 98.4, 99.3 … s
- LOGIN：30 个，最大 30728 ms，发出时刻 1.1, 1.1, 1.2, 1.2, 1.3, 1.4, 1.4, 1.5, 1.5, 1.6, 1.7, 4.3 … s
- SELECT_DUNGEON：14 个，最大 50234 ms，发出时刻 44.5, 45.7, 45.8, 75.9, 110.3, 113.4, 137.1, 143.6, 172.2, 175.1, 202.5, 205.1 … s
- EPLP：11 个，最大 60001 ms，发出时刻 70.5, 70.6, 101.2, 133.5, 133.6, 162.1, 163.4, 194.6, 194.7, 226.7, 227.3 s
- SET_USER_AREA：5 个，最大 30120 ms，发出时刻 4.3, 4.3, 5.9, 35.3, 289.3 s
- CREATE_CHARACTER：3 个，最大 60001 ms，发出时刻 188.9, 188.9, 222.3 s
- REQUEST_PEER：1 个，最大 25657 ms，发出时刻 294.1 s

失败的命令 83 条：ApplySelectedCharacterDailyRefillCommand ×1（最长 30104 ms）；ExpireCharacterNameTagCommand ×39（最长 15327 ms）；PrepareSelectedCharacterStateCommand ×5（最长 60932 ms）；EnterDungeonCommand ×2（最长 50220 ms）；RefillOnlineTauTicketCommand ×3（最长 30820 ms）；CompensateSelectedCharacterSessionCommand ×11（最长 0 ms）；RefillOnlineSouthernTicketCommand ×4（最长 30792 ms）；AuthenticateAndIssueTicketCommand ×12（最长 60002 ms）；CleanupExpiredCharacterItemsCommand ×2（最长 1055 ms）；EnsureDailyContractPuppetsCommand ×4（最长 1027 ms）
- t=34.8s pair05: pair05M: connection closed during SELECT_CHARACTER after 30176 ms (first reply CMD  0x0004 (270B) 01000000).
- t=64.7s pair00: pair00M: SELECT_CHARACTER timed out.
- t=64.7s solo06: solo06: SELECT_CHARACTER timed out.
- t=64.8s solo05: solo05: SELECT_CHARACTER timed out.
- t=65.4s solo01: solo01: connection closed during SELECT_CHARACTER after 30430 ms (first reply none).
- t=94.9s solo04: solo04: SELECT_CHARACTER timed out.
- t=95.0s solo02: solo02: SELECT_CHARACTER timed out.
- t=95.0s solo07: solo07: SELECT_CHARACTER timed out.
- t=96.0s pair01: pair01L: connection closed during SELECT_DUNGEON after 50234 ms (first reply none).
- t=128.0s pair00: pair00M: connection closed during SELECT_CHARACTER after 30574 ms (first reply CMD  0x0004 (270B) 01000000).
- t=157.7s pair01: pair01M: SELECT_CHARACTER timed out.
- t=157.7s solo00: solo00: SELECT_CHARACTER timed out.
- t=188.9s pair00: pair00L: connection closed during SELECT_CHARACTER after 30854 ms (first reply CMD  0x0004 (270B) 01000000).
- t=221.5s pair00: pair00M: connection closed during SELECT_CHARACTER after 30103 ms (first reply none).
- t=251.5s solo01: solo01: SELECT_CHARACTER timed out.
- t=252.4s pair05: pair05L: connection closed during SELECT_DUNGEON after 46830 ms (first reply none).
- t=254.6s pair04: pair04L: EPLP timed out.
- t=254.7s pair02: pair02L: EPLP timed out.
- t=281.5s solo07: solo07: SELECT_CHARACTER timed out.
- t=281.6s b1-burst1-0: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.7s solo02: solo02: SELECT_CHARACTER timed out.
- t=281.7s b1-burst1-1: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.7s b1-burst1-2: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.8s b1-burst1-6: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.9s b1-burst1-4: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=281.9s b1-burst1-5: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=282.0s b1-burst1-3: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=282.0s pair00: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=282.1s b1-burst1-9: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.
- t=282.1s b1-burst1-8: The request was canceled due to the configured HttpClient.Timeout of 60 seconds elapsing.

