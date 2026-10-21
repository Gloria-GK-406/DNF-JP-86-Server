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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1389 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 321 s。
- 稳态窗实际包速率：102.4 包/秒（12290 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| CHECK_USER_CONNECTION | 8851 | 72.02% |
| SET_USER_POSITION | 3171 | 25.80% |
| STEP_FENCE | 155 | 1.26% |
| SET_USER_AREA | 31 | 0.25% |
| SELECT_CHARACTER | 17 | 0.14% |
| LOGIN | 12 | 0.10% |
| FINISH_LOADING | 12 | 0.10% |
| RETURN_SELECT_CHARACTER | 7 | 0.06% |
| REQUEST_PEER | 6 | 0.05% |
| RESPONSE_PEER | 6 | 0.05% |
| ENTER_SELECT_DUNGEON | 6 | 0.05% |
| SELECT_DUNGEON | 6 | 0.05% |
| EPLP | 6 | 0.05% |
| LEAVE_PARTY | 3 | 0.02% |
| CREATE_CHARACTER | 1 | 0.01% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| （计划但未发出） | 19200（截尾 19200） | 200911.3 | 254911.3 | 259711.3 | 260810.5 | ≥260911.3 | 0 | – | – | – |
| CHECK_USER_CONNECTION | 9604 | 6961.8 | 48657.0 | 54950.4 | 56404.7 | 56753.7 | 0 | 0.30 | 6.61 | 14.08 |
| SET_USER_POSITION | 3947 | 9327.6 | 53205.1 | 55773.1 | 56703.8 | 56828.4 | 0 | 0.88 | 17.51 | 54.10 |
| SELECT_CHARACTER | 17**（截尾 1） | 30640.6 | 60002.0 | 60002.0 | 60002.0 | ≥60002.0 | 0 | 60002.0 | 60002.0 | ≥60002.0 |
| LOGIN | 12** | 127.1 | 329.7 | 329.7 | 329.7 | 329.7 | 0 | 329.7 | 329.7 | 329.7 |
| FINISH_LOADING | 12** | 0.17 | 7.46 | 7.46 | 7.46 | 7.46 | 0 | 7.46 | 7.46 | 7.46 |
| REQUEST_PEER | 0 | – | – | – | – | – | 6 | – | – | – |
| RESPONSE_PEER | 6** | 0.63 | 13.55 | 13.55 | 13.55 | 13.55 | 0 | 13.55 | 13.55 | 13.55 |
| ENTER_SELECT_DUNGEON | 6** | 18.98 | 46.44 | 46.44 | 46.44 | 46.44 | 0 | 44.48 | 44.48 | 44.48 |
| SELECT_DUNGEON | 6** | 16471.4 | 21347.2 | 21347.2 | 21347.2 | 21347.2 | 0 | 21347.2 | 21347.2 | 21347.2 |
| EPLP | 6** | 26161.3 | 56877.0 | 56877.0 | 56877.0 | 56877.0 | 0 | 56877.0 | 56877.0 | 56877.0 |
| LEAVE_PARTY | 3** | 3.60 | 18.92 | 18.92 | 18.92 | 18.92 | 0 | 17.16 | 17.16 | 17.16 |
| CREATE_CHARACTER | 1** | 159.6 | 159.6 | 159.6 | 159.6 | 159.6 | 0 | 159.6 | 159.6 | 159.6 |
| **全部计入的请求** | 32820（截尾 19201） | 158361.3 | 250661.3 | 258861.3 | 260711.3 | ≥260911.3 | 6 | 2.26 | 26159.5 | ≥60002.0 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：13626 | 7260.5 | 50863.6 | 55384.4 | 56504.4 | 56807.1（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ExpireCharacterNameTagCommand | Transactional | 54** | 2.66 | 30152.2 | 61303.9 | 61303.9 | 61303.9 | 158.1 | 30 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 79** | 125.9 | 30249.5 | 60265.6 | 60265.6 | 60265.6 | 218.9 | 1 |
| PrepareSelectedCharacterSessionCommand | Transactional | 86** | 66.42 | 30194.4 | 60265.5 | 60265.5 | 60265.5 | 218.9 | 3 |
| RefillOnlineBossTowerTicketCommand | Transactional | 68** | 188.4 | 840.4 | 60242.9 | 60242.9 | 60242.9 | 218.8 | 5 |
| RefillOnlineSouthernTicketCommand | Transactional | 74** | 286.6 | 30719.2 | 60221.6 | 60221.6 | 60221.6 | 218.8 | 6 |
| LeaveDungeonCommand | Direct | 9** | 29339.6 | 56839.3 | 56839.3 | 56839.3 | 56839.3 | 220.1 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 5** | 15596.0 | 38904.2 | 38904.2 | 38904.2 | 38904.2 | 218.9 | 0 |
| RegisterAccountCommand | Transactional | 81** | 452.0 | 37246.8 | 37495.9 | 37495.9 | 37495.9 | 219.5 | 0 |
| AuthenticateAndIssueTicketCommand | Transactional | 124* | 946.6 | 30716.2 | 30791.5 | 31551.7 | 31551.7 | 219.6 | 0 |
| CreateCharacterCommand | Transactional | 25** | 96.30 | 30667.3 | 30902.7 | 30902.7 | 30902.7 | 219.6 | 2 |
| PrepareSelectedCharacterStateCommand | Transactional | 82** | 102.9 | 30181.0 | 30793.0 | 30793.0 | 30793.0 | 288.8 | 2 |
| InitializeHousingCommand | Transactional | 82** | 65.99 | 804.0 | 30791.7 | 30791.7 | 30791.7 | 288.8 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 83** | 62.07 | 30098.5 | 30791.7 | 30791.7 | 30791.7 | 288.8 | 1 |
| RedeemGameLoginCommand | Transactional | 124* | 342.0 | 30307.7 | 30619.4 | 30681.6 | 30681.6 | 255.5 | 0 |
| RefillOnlineTauTicketCommand | Transactional | 77** | 280.5 | 30237.5 | 30246.8 | 30246.8 | 30246.8 | 65.2 | 3 |
| ReviveSelectedCharacterPetCommand | Transactional | 61** | 81.88 | 30148.6 | 30207.4 | 30207.4 | 30207.4 | 127.3 | 4 |
| PrepareReturnToSelectionCommand | Transactional | 24** | 20534.9 | 30159.9 | 30164.5 | 30164.5 | 30164.5 | 188.7 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 63** | 112.2 | 30073.1 | 30152.3 | 30152.3 | 30152.3 | 319.7 | 2 |
| CleanupExpiredCharacterItemsCommand | Transactional | 83** | 22.80 | 30073.4 | 30122.6 | 30122.6 | 30122.6 | 157.7 | 0 |
| EnterDungeonCommand | Transactional | 12** | 19722.6 | 21267.9 | 21267.9 | 21267.9 | 21267.9 | 255.5 | 0 |
| AdvanceCreatureClockCommand | Transactional | 11** | 147.9 | 945.8 | 945.8 | 945.8 | 945.8 | 256.5 | 0 |
| CompensateSelectedCharacterSessionCommand | Workflow | 40** | 0.07 | 453.9 | 608.4 | 608.4 | 608.4 | 96.7 | 16 |
| CompleteReturnToSelectionCommand | Workflow | 24** | 0.25 | 458.3 | 608.3 | 608.3 | 608.3 | 257.1 | 0 |
| ReadItemUsageSnapshotCommand | ReadOnly | 63** | 0.06 | 32.90 | 33.12 | 33.12 | 33.12 | 3.9 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 187* | 0.01 | 5.89 | 5.93 | 9.19 | 9.19 | 158.1 | 0 |
| AcceptPartyInviteCommand | Direct | 12** | 0.05 | 6.43 | 6.43 | 6.43 | 6.43 | 70.1 | 0 |
| GetAccountCharacterRosterCommand | Direct | 23** | 0.36 | 2.48 | 6.00 | 6.00 | 6.00 | 2.0 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 77** | 0.02 | 0.08 | 5.35 | 5.35 | 5.35 | 2.7 | 0 |
| RequestPartyInviteCommand | Direct | 12** | 1.53 | 3.65 | 3.65 | 3.65 | 3.65 | 70.0 | 0 |
| LeavePartyCommand | Direct | 9** | 0.05 | 1.16 | 1.16 | 1.16 | 1.16 | 131.8 | 0 |
| MarkDungeonParticipantReadyCommand | Direct | 24** | 0.02 | 0.30 | 0.63 | 0.63 | 0.63 | 96.2 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 27** | 0.04 | 0.10 | 0.41 | 0.41 | 0.41 | 65.8 | 0 |

命令日志行 1805 条（其中失败 75）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**≥310106.5 ms**（（计划但未发出），截尾：超时或连接被关闭，计划发出 t=10.84 s，solo01；参考口径 – ms）。冷启动窗整体：8895（截尾 7018） | 277111.3 | 304206.9 | 308411.3 | 309806.5 | ≥310106.5（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 0 | – | – | – |
| 1 | 30 | 887.2 | 887.2 | LOGIN |
| 2 | 15 | 625.5 | 625.5 | SELECT_CHARACTER |
| 3 | 16 | 789.7 | 789.7 | RETURN_SELECT_CHARACTER |
| 4 | 165 | 250887.4 | 250931.0 | CHECK_USER_CONNECTION |
| 5 | 146 | 250537.8 | 250587.7 | SET_USER_POSITION |
| 6 | 146 | 249491.6 | 249540.4 | CHECK_USER_CONNECTION |
| 7 | 138 | 248542.5 | 248592.5 | SET_USER_POSITION |
| 8 | 152 | 248684.8 | 248734.6 | SET_USER_POSITION |
| 9 | 147 | 248289.4 | 248339.3 | SET_USER_POSITION |
| 10 | 141 | 310056.5 | 310106.5 | （计划但未发出） |
| 11 | 152 | 309856.5 | 309906.5 | （计划但未发出） |
| 12 | 155 | 308911.3 | 308911.3 | （计划但未发出） |
| 13 | 156 | 307911.3 | 307911.3 | （计划但未发出） |
| 14 | 156 | 306911.3 | 306911.3 | （计划但未发出） |
| 15 | 158 | 305911.3 | 305911.3 | （计划但未发出） |
| 16 | 160 | 304911.3 | 304911.3 | （计划但未发出） |
| 17 | 155 | 303911.3 | 303911.3 | （计划但未发出） |
| 18 | 157 | 302911.3 | 302911.3 | （计划但未发出） |
| 19 | 156 | 301911.3 | 301911.3 | （计划但未发出） |
| 20 | 156 | 300911.3 | 300911.3 | （计划但未发出） |
| 21 | 156 | 299911.3 | 299911.3 | （计划但未发出） |
| 22 | 158 | 298911.3 | 298911.3 | （计划但未发出） |
| 23 | 159 | 297911.3 | 297911.3 | （计划但未发出） |
| 24 | 156 | 296911.3 | 296911.3 | （计划但未发出） |
| 25 | 159 | 295911.3 | 295911.3 | （计划但未发出） |
| 26 | 160 | 294911.3 | 294911.3 | （计划但未发出） |
| 27 | 160 | 293911.3 | 293911.3 | （计划但未发出） |
| 28 | 160 | 292911.3 | 292911.3 | （计划但未发出） |
| 29 | 160 | 291911.3 | 291911.3 | （计划但未发出） |
| 30 | 160 | 290911.3 | 290911.3 | （计划但未发出） |
| 31 | 160 | 289911.3 | 289911.3 | （计划但未发出） |
| 32 | 160 | 288911.3 | 288911.3 | （计划但未发出） |
| 33 | 160 | 287911.3 | 287911.3 | （计划但未发出） |
| 34 | 170 | 286911.3 | 286911.3 | （计划但未发出） |
| 35 | 160 | 285911.3 | 285911.3 | （计划但未发出） |
| 36 | 160 | 284911.3 | 284911.3 | （计划但未发出） |
| 37 | 160 | 283911.3 | 283911.3 | （计划但未发出） |
| 38 | 160 | 282911.3 | 282911.3 | （计划但未发出） |
| 39 | 160 | 281911.3 | 281911.3 | （计划但未发出） |
| 40 | 160 | 280911.3 | 280911.3 | （计划但未发出） |
| 41 | 160 | 279911.3 | 279911.3 | （计划但未发出） |
| 42 | 160 | 278911.3 | 278911.3 | （计划但未发出） |
| 43 | 160 | 277911.3 | 277911.3 | （计划但未发出） |
| 44 | 160 | 276911.3 | 276911.3 | （计划但未发出） |
| 45 | 160 | 275911.3 | 275911.3 | （计划但未发出） |
| 46 | 160 | 274911.3 | 274911.3 | （计划但未发出） |
| 47 | 160 | 273911.3 | 273911.3 | （计划但未发出） |
| 48 | 160 | 272911.3 | 272911.3 | （计划但未发出） |
| 49 | 160 | 271911.3 | 271911.3 | （计划但未发出） |
| 50 | 160 | 270911.3 | 270911.3 | （计划但未发出） |
| 51 | 160 | 269911.3 | 269911.3 | （计划但未发出） |
| 52 | 160 | 268911.3 | 268911.3 | （计划但未发出） |
| 53 | 160 | 267911.3 | 267911.3 | （计划但未发出） |
| 54 | 160 | 266911.3 | 266911.3 | （计划但未发出） |
| 55 | 160 | 265911.3 | 265911.3 | （计划但未发出） |
| 56 | 160 | 264911.3 | 264911.3 | （计划但未发出） |
| 57 | 160 | 263911.3 | 263911.3 | （计划但未发出） |
| 58 | 160 | 262911.3 | 262911.3 | （计划但未发出） |
| 59 | 160 | 261911.3 | 261911.3 | （计划但未发出） |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 32820（截尾 19201） | 158361.3 | 250661.3 | 258861.3 | 260711.3 | ≥260911.3 |
| 10 并发登录的轮内 | 29926（截尾 17113） | 45410.5 | 129561.3 | 137060.5 | 138756.5 | ≥138911.3 |
| （参考：实际发出口径）稳态窗 | 13620（截尾 1） | 0.06 | 0.21 | 2.26 | 26159.5 | ≥60002.0 |
| （参考：实际发出口径）轮内 | 12814（截尾 1） | 0.07 | 0.20 | 2.40 | 26610.8 | 33202.2 |
| 登录阶段整体（182–289 s） | 29926（截尾 17113） | 45410.5 | 129561.3 | 137060.5 | 138756.5 | ≥138911.3 |

每轮时长（开始 → 10 个 LOGIN 都应答）：38790, 1281, 1672, 31720, 1913, 31567 ms。

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 60** | 497.6 | 37309.3 | 37497.1 | 37497.1 | 37497.1 |
| 启动器登录（HTTP，含校验） | 60** | 803.8 | 30716.6 | 30791.7 | 30791.7 | 30791.7 |
| Game LOGIN 0x0001 | 60** | 458.0 | 30495.2 | 30682.9 | 30682.9 | 30682.9 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 196371 包/秒（5891136 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 32820（截尾 19201） | 158361.3 | 250661.3 | 258861.3 | 260711.3 | ≥260911.3 |
| 其余连接，灌包期间 | 9522（截尾 4801） | 10656.5 | 29011.3 | 31406.5 | 31911.3 | ≥31961.3 |
| （参考：实际发出口径）其余连接，灌包期间 | 4722（截尾 1） | 0.06 | 0.10 | 0.84 | 13995.8 | 30740.2 |
| 灌包连接自身（含其窗口内排队） | 5891167 | 0.06 | 0.11 | 0.15 | 1.95 | 18.25 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 41 | 39467 | selection after create | 219.6 |
| RETURN_SELECT_CHARACTER | 请求者 | 24 | 13829 | return to selection | 225.5 |
| LEAVE_PARTY | 同伴 | 9 | 13568 | leave party | 293.6 |
| LEAVE_PARTY | 请求者 | 9 | 13529 | leave party | 293.6 |
| SET_USER_AREA | 请求者 | 137 | 2576 | town | 127.5 |
| SET_USER_AREA | 同伴 | 4 | 2398 | town | 289.7 |
| EPLP | 同伴 | 9 | 1981 | leave dungeon | 102.2 |
| EPLP | 请求者 | 9 | 1724 | leave dungeon | 102.2 |
| SELECT_DUNGEON | 请求者 | 12 | 1681 | party dungeon entry | 298.7 |
| SELECT_DUNGEON | 同伴 | 12 | 1392 | party dungeon entry | 235.1 |
| ENTER_SELECT_DUNGEON | 请求者 | 12 | 632 | party dungeon entry | 75.1 |
| ENTER_SELECT_DUNGEON | 同伴 | 12 | 632 | party dungeon entry | 75.1 |
| LOGIN | 请求者 | 64 | 339 | login | 1.4 |
| CREATE_CHARACTER | 请求者 | 23 | 313 | create | 188.7 |
| RESPONSE_PEER | 同伴 | 12 | 214 | party | 70.0 |
| RESPONSE_PEER | 请求者 | 12 | 212 | party | 70.0 |
| REQUEST_PEER | 同伴 | 12 | 52 | party | 70.0 |
| FINISH_LOADING | 请求者 | 24 | 36 | party dungeon entry | 96.2 |
| REQUEST_PEER | 请求者 | 12 | 24 | party | 70.0 |
| FINISH_LOADING | 同伴 | 24 | 0 | party dungeon entry | 96.2 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 258861.3 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 137060.5 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 31406.5 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 23 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 310106.5 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `ExpireCharacterNameTagCommand`：样本 54，p99.9 61303.9 ms，max 61303.9 ms（出现于 t=158.1 s；冷启动窗内 max – ms，之后 max 61303.9 ms）
- `ApplySelectedCharacterDailyRefillCommand`：样本 79，p99.9 60265.6 ms，max 60265.6 ms（出现于 t=218.9 s；冷启动窗内 max 336.1 ms，之后 max 60265.6 ms）
- `PrepareSelectedCharacterSessionCommand`：样本 86，p99.9 60265.5 ms，max 60265.5 ms（出现于 t=218.9 s；冷启动窗内 max 30069.8 ms，之后 max 60265.5 ms）
- `RefillOnlineBossTowerTicketCommand`：样本 68，p99.9 60242.9 ms，max 60242.9 ms（出现于 t=218.8 s；冷启动窗内 max 323.3 ms，之后 max 60242.9 ms）
- `RefillOnlineSouthernTicketCommand`：样本 74，p99.9 60221.6 ms，max 60221.6 ms（出现于 t=218.8 s；冷启动窗内 max 322.7 ms，之后 max 60221.6 ms）
- `LeaveDungeonCommand`：样本 9，p99.9 56839.3 ms，max 56839.3 ms（出现于 t=220.1 s；冷启动窗内 max – ms，之后 max 56839.3 ms）
- `AdvanceWeaknessRecoveryCommand`：样本 5，p99.9 38904.2 ms，max 38904.2 ms（出现于 t=218.9 s；冷启动窗内 max – ms，之后 max 38904.2 ms）
- `RegisterAccountCommand`：样本 81，p99.9 37495.9 ms，max 37495.9 ms（出现于 t=219.5 s；冷启动窗内 max 950.5 ms，之后 max 37495.9 ms）
- `AuthenticateAndIssueTicketCommand`：样本 124，p99.9 31551.7 ms，max 31551.7 ms（出现于 t=219.6 s；冷启动窗内 max 949.5 ms，之后 max 31551.7 ms）
- `CreateCharacterCommand`：样本 25，p99.9 30902.7 ms，max 30902.7 ms（出现于 t=219.6 s；冷启动窗内 max 30077.2 ms，之后 max 30902.7 ms）
- `PrepareSelectedCharacterStateCommand`：样本 82，p99.9 30793.0 ms，max 30793.0 ms（出现于 t=288.8 s；冷启动窗内 max 548.2 ms，之后 max 30793.0 ms）
- `InitializeHousingCommand`：样本 82，p99.9 30791.7 ms，max 30791.7 ms（出现于 t=288.8 s；冷启动窗内 max 21.03 ms，之后 max 30791.7 ms）
- `EnsureDailyContractPuppetsCommand`：样本 83，p99.9 30791.7 ms，max 30791.7 ms（出现于 t=288.8 s；冷启动窗内 max 5.21 ms，之后 max 30791.7 ms）
- `RedeemGameLoginCommand`：样本 124，p99.9 30681.6 ms，max 30681.6 ms（出现于 t=255.5 s；冷启动窗内 max 868.0 ms，之后 max 30681.6 ms）
- `RefillOnlineTauTicketCommand`：样本 77，p99.9 30246.8 ms，max 30246.8 ms（出现于 t=65.2 s；冷启动窗内 max 341.0 ms，之后 max 30246.8 ms）
- `ReviveSelectedCharacterPetCommand`：样本 61，p99.9 30207.4 ms，max 30207.4 ms（出现于 t=127.3 s；冷启动窗内 max 3.95 ms，之后 max 30207.4 ms）
- `PrepareReturnToSelectionCommand`：样本 24，p99.9 30164.5 ms，max 30164.5 ms（出现于 t=188.7 s；冷启动窗内 max 29971.1 ms，之后 max 30164.5 ms）
- `SynchronizeSelectedCharacterQuestsCommand`：样本 63，p99.9 30152.3 ms，max 30152.3 ms（出现于 t=319.7 s；冷启动窗内 max 263.6 ms，之后 max 30152.3 ms）
- `CleanupExpiredCharacterItemsCommand`：样本 83，p99.9 30122.6 ms，max 30122.6 ms（出现于 t=157.7 s；冷启动窗内 max 3.79 ms，之后 max 30122.6 ms）
- `EnterDungeonCommand`：样本 12，p99.9 21267.9 ms，max 21267.9 ms（出现于 t=255.5 s；冷启动窗内 max – ms，之后 max 21267.9 ms）
- `AdvanceCreatureClockCommand`：样本 11，p99.9 945.8 ms，max 945.8 ms（出现于 t=256.5 s；冷启动窗内 max 377.6 ms，之后 max 945.8 ms）
- `CompensateSelectedCharacterSessionCommand`：样本 40，p99.9 608.4 ms，max 608.4 ms（出现于 t=96.7 s；冷启动窗内 max – ms，之后 max 608.4 ms）
- `CompleteReturnToSelectionCommand`：样本 24，p99.9 608.3 ms，max 608.3 ms（出现于 t=257.1 s；冷启动窗内 max 458.3 ms，之后 max 608.3 ms）

## 错误与无回包

错误 38 条；背景 agent 重连 38 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 12 次；服务端关闭连接或超时的请求 48816 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：73176 个；截尾样本（超时或连接被关闭）48786 个，协议无回包 12 个。
- （计划但未发出）：48728 个（其中截尾 48728），最大 310106 ms，计划发出时刻 11.6, 11.7, 11.7, 11.8, 11.8, 11.9, 11.9, 12.0, 12.0, 12.1, 12.1, 12.2 … s
- CHECK_USER_CONNECTION：16395 个（其中截尾 0），最大 309902 ms，计划发出时刻 75.4, 75.4, 75.5, 75.5, 75.5, 75.5, 75.6, 75.6, 75.6, 75.6, 75.7, 75.7 … s
- SET_USER_POSITION：7803 个（其中截尾 0），最大 309852 ms，计划发出时刻 4.4, 4.1, 4.2, 4.8, 4.3, 4.9, 4.4, 5.0, 4.5, 4.6, 4.6, 5.2 … s
- SET_USER_AREA：92 个（其中截尾 0），最大 309567 ms，计划发出时刻 4.3, 4.4, 5.0, 6.2, 5.1, 5.4, 5.7, 6.0, 6.3, 10.2, 10.4, 10.6 … s
- SELECT_CHARACTER：67 个（其中截尾 4），最大 60002 ms，计划发出时刻 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 … s
- LOGIN：37 个（其中截尾 0），最大 30157 ms，计划发出时刻 1.1, 1.1, 1.2, 1.2, 1.3, 1.4, 1.4, 1.5, 1.5, 1.6, 1.7, 4.4 … s
- RETURN_SELECT_CHARACTER：23 个（其中截尾 0），最大 308939 ms，计划发出时刻 4.0, 4.4, 4.5, 8.6, 10.0, 10.2, 14.2, 7.0, 5.3, 6.8, 17.5, 16.8 … s
- SELECT_DUNGEON：12 个（其中截尾 0），最大 21347 ms，计划发出时刻 75.2, 75.2, 76.4, 141.4, 141.5, 142.3, 234.3, 235.1, 235.2, 298.7, 304.1, 305.8 s
- CREATE_CHARACTER：9 个（其中截尾 2），最大 30913 ms，计划发出时刻 4.7, 4.7, 4.7, 4.7, 4.7, 4.7, 188.7, 257.0, 257.4 s
- EPLP：9 个（其中截尾 0），最大 56877 ms，计划发出时刻 101.3, 102.0, 102.2, 163.2, 163.6, 163.8, 259.6, 259.7, 262.4 s
- LEAVE_PARTY：1 个（其中截尾 0），最大 383 ms，计划发出时刻 293.6 s

失败的命令 75 条：CreateCharacterCommand ×2（最长 30667 ms）；ApplySelectedCharacterDailyRefillCommand ×1（最长 30174 ms）；RefillOnlineBossTowerTicketCommand ×5（最长 1208 ms）；CompensateSelectedCharacterSessionCommand ×16（最长 0 ms）；ExpireCharacterNameTagCommand ×30（最长 61304 ms）；PrepareSelectedCharacterStateCommand ×2（最长 30149 ms）；ReviveSelectedCharacterPetCommand ×4（最长 515 ms）；RefillOnlineSouthernTicketCommand ×6（最长 60222 ms）；PrepareSelectedCharacterSessionCommand ×3（最长 676 ms）；SynchronizeSelectedCharacterQuestsCommand ×2（最长 1227 ms）；RefillOnlineTauTicketCommand ×3（最长 1201 ms）；EnsureDailyContractPuppetsCommand ×1（最长 717 ms）
- t=34.8s pair03: pair03M: connection closed during CREATE_CHARACTER after 30070 ms (first reply none).
- t=64.7s solo05: solo05: SELECT_CHARACTER timed out.
- t=64.8s solo06: solo06: SELECT_CHARACTER timed out.
- t=64.8s solo00: solo00: SELECT_CHARACTER timed out.
- t=65.0s pair00: pair00M: connection closed during SELECT_CHARACTER after 30214 ms (first reply CMD  0x0004 (270B) 01000000).
- t=94.8s pair04: pair04M: SELECT_CHARACTER timed out.
- t=94.8s solo04: solo04: SELECT_CHARACTER timed out.
- t=94.8s solo07: solo07: SELECT_CHARACTER timed out.
- t=94.8s solo01: solo01: SELECT_CHARACTER timed out.
- t=94.8s solo02: solo02: SELECT_CHARACTER timed out.
- t=94.9s solo03: solo03: SELECT_CHARACTER timed out.
- t=125.9s pair00: pair00L: SELECT_CHARACTER timed out.
- t=127.3s solo07: solo07: connection closed during SELECT_CHARACTER after 30704 ms (first reply CMD  0x0004 (270B) 01000000).
- t=127.5s pair03: pair03M: connection closed during SELECT_CHARACTER after 160 ms (first reply none).
- t=156.6s solo01: solo01: SELECT_CHARACTER timed out.
- t=156.6s solo02: solo02: SELECT_CHARACTER timed out.
- t=156.6s solo03: solo03: SELECT_CHARACTER timed out.
- t=187.5s pair00: pair00L: SELECT_CHARACTER timed out.
- t=188.7s pair04: pair04M: connection closed during SELECT_CHARACTER after 30596 ms (first reply CMD  0x0004 (270B) 01000000).
- t=218.1s pair03: pair03L: SELECT_CHARACTER timed out.
- t=218.1s solo01: solo01: SELECT_CHARACTER timed out.
- t=218.1s solo07: solo07: SELECT_CHARACTER timed out.
- t=218.1s solo02: solo02: SELECT_CHARACTER timed out.
- t=218.1s solo03: solo03: SELECT_CHARACTER timed out.
- t=218.2s solo04: solo04: SELECT_CHARACTER timed out.
- t=218.6s solo05: solo05: SELECT_CHARACTER timed out.
- t=255.4s solo03: solo03: connection closed during SELECT_CHARACTER after 34647 ms (first reply CMD  0x0004 (270B) 01000000).
- t=257.4s pair03: pair03M: connection closed during SELECT_CHARACTER after 677 ms (first reply none).
- t=285.3s solo00: solo00: SELECT_CHARACTER timed out.
- t=288.1s solo05: solo05: connection closed during CREATE_CHARACTER after 30670 ms (first reply none).

