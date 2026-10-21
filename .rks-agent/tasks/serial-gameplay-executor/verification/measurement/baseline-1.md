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

- Host 冷启动：进程启动 → 启动器 HTTP 与游戏端口就绪 1409 ms

## 负载

- 连接：单人 8，双人组 6（12 条），合计 20 条背景连接；另有 1 条灌包连接（平时每 2 s 一个栅栏，不计入背景）。
- 目标速率：每连接 20 包/秒，合计 400 包/秒；种子 1；双人组每个状态停留 4–6 s。
- 进入序列并发上限（登录→建角→选角，含重连）：不限（Host 就绪即全部同时进入）。
- 单人动作权重：栅栏 0.47，移动 0.45，跨区 0.068，回选角+再选角 0.01，回选角+建角+选新角 0.002。
- 时间线（从 Host 就绪起）：冷启动窗 [0, 60) s；稳态窗 [60, 180) s；之后 6 轮 × 10 个并发登录（每 10 s 一轮）；然后灌包 30 s（窗口 32）。总时长 291 s。
- 稳态窗实际包速率：4.7 包/秒（559 包 / 120 s，含步骤栅栏）。

稳态窗的负载构成（按入站包计；STEP_FENCE 是工具为收口回包窗口在请求后紧跟的栅栏，客户端本来也会周期性发这个包）：

| 请求 | 包数 | 占比 |
|---|---:|---:|
| SET_USER_POSITION | 217 | 38.82% |
| CHECK_USER_CONNECTION | 212 | 37.92% |
| STEP_FENCE | 65 | 11.63% |
| SET_USER_AREA | 29 | 5.19% |
| SELECT_CHARACTER | 16 | 2.86% |
| RETURN_SELECT_CHARACTER | 9 | 1.61% |
| LOGIN | 8 | 1.43% |
| CREATE_CHARACTER | 3 | 0.54% |

## 端到端延迟（稳态窗）

判定口径：该请求的**计划发出时刻**（agent 节拍表上的时刻，落后时不重置）→ 第一条回包（按请求类型的回包签名识别，见 README）被接收线程读到。参考口径：实际写出请求帧的时刻 → 第一条回包。超时或连接被关闭、没等到回包的请求作为截尾样本计入（取到超时/关闭为止，是下界）；协议上本来没有回包的请求（如 REQUEST_PEER）不计入，单列「协议无回包」。基线上多段请求的首条回包可能早于整段结束；改造后按 VC-1 会推迟到 CMD 结束。

| 请求 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 协议无回包 | 参考（实际发出）p99 | 参考 p99.9 | 参考 max |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| （计划但未发出） | 19200（截尾 19200） | 170961.2 | 224961.2 | 229761.2 | 230860.7 | ≥230961.2 | 0 | – | – | – |
| SELECT_CHARACTER | 16** | 30664.1 | 32366.4 | 32366.4 | 32366.4 | 32366.4 | 0 | 32366.4 | 32366.4 | 32366.4 |
| LOGIN | 8** | 127.9 | 30297.2 | 30297.2 | 30297.2 | 30297.2 | 0 | 30297.2 | 30297.2 | 30297.2 |
| CREATE_CHARACTER | 3** | 266.8 | 30222.8 | 30222.8 | 30222.8 | 30222.8 | 0 | 30222.8 | 30222.8 | 30222.8 |
| CHECK_USER_CONNECTION | 2** | 0.11 | 0.15 | 0.15 | 0.15 | 0.15 | 0 | 0.15 | 0.15 | 0.15 |
| SET_USER_POSITION | 1** | 0.16 | 0.16 | 0.16 | 0.16 | 0.16 | 0 | 0.16 | 0.16 | 0.16 |
| **全部计入的请求** | 19230（截尾 19200） | 170864.1 | 224960.8 | 229761.2 | 230860.7 | ≥230961.2 | 0 | 32366.4 | 32366.4 | 32366.4 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

发出滞后（实际发出 − 计划发出）：30** | 0.00 | 0.00 | 0.00 | 0.00 | 0.00（样本 | p50 | p95 | p99 | p99.9 | max，ms）。没有服务端停顿时，这就是工具自身定时器的迟到，会让判定口径略偏大（偏保守）；有停顿时它包含该连接在停顿后积压待发的时间。

## 服务端命令执行时长（`CommandLoggingBehavior` 的 `ElapsedMilliseconds`）

范围：Host 就绪之后整次运行的全部命令（含冷启动窗与 (d)(e) 阶段）。读到日志行的时刻用于判断落在哪个窗口。

| 命令 | 执行方式 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) | max 出现于 (s) | 失败 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ExpireCharacterNameTagCommand | Transactional | 54** | 0.01 | 30266.7 | 61339.3 | 61339.3 | 61339.3 | 127.2 | 45 |
| PrepareReturnToSelectionCommand | Transactional | 28** | 29830.5 | 30234.0 | 60266.2 | 60266.2 | 60266.2 | 252.0 | 0 |
| PrepareSelectedCharacterSessionCommand | Transactional | 94** | 96.27 | 30294.7 | 60266.1 | 60266.1 | 60266.1 | 252.0 | 0 |
| ApplySelectedCharacterDailyRefillCommand | Transactional | 80** | 154.4 | 30169.2 | 60265.4 | 60265.4 | 60265.4 | 252.0 | 1 |
| RefillOnlineSouthernTicketCommand | Transactional | 73** | 320.6 | 30519.5 | 60263.0 | 60263.0 | 60263.0 | 251.9 | 8 |
| InitializeHousingCommand | Transactional | 87** | 65.95 | 796.7 | 60261.4 | 60261.4 | 60261.4 | 251.9 | 0 |
| SynchronizeSelectedCharacterQuestsCommand | Transactional | 59** | 229.5 | 30222.7 | 60250.4 | 60250.4 | 60250.4 | 251.9 | 7 |
| ReviveSelectedCharacterPetCommand | Transactional | 52** | 84.53 | 30131.6 | 60250.3 | 60250.3 | 60250.3 | 251.9 | 2 |
| RegisterAccountCommand | Transactional | 81** | 681.5 | 60002.1 | 60047.7 | 60047.7 | 60047.7 | 252.1 | 10 |
| AuthenticateAndIssueTicketCommand | Transactional | 119* | 811.3 | 2581.4 | 30261.7 | 32555.4 | 32555.4 | 252.1 | 1 |
| RefillOnlineBossTowerTicketCommand | Transactional | 65** | 290.7 | 30524.7 | 30541.1 | 30541.1 | 30541.1 | 127.3 | 6 |
| RedeemGameLoginCommand | Transactional | 118* | 379.0 | 1248.2 | 30234.7 | 30296.9 | 30296.9 | 127.4 | 0 |
| EnsureDailyContractPuppetsCommand | Transactional | 89** | 77.37 | 30138.9 | 30266.8 | 30266.8 | 30266.8 | 189.2 | 2 |
| RefillOnlineTauTicketCommand | Transactional | 79** | 169.0 | 30223.9 | 30234.9 | 30234.9 | 30234.9 | 96.7 | 2 |
| CleanupExpiredCharacterItemsCommand | Transactional | 89** | 74.51 | 30186.0 | 30224.8 | 30224.8 | 30224.8 | 189.1 | 0 |
| CreateCharacterCommand | Transactional | 25** | 82.44 | 30115.3 | 30221.4 | 30221.4 | 30221.4 | 189.3 | 0 |
| PrepareSelectedCharacterStateCommand | Transactional | 85** | 136.3 | 30109.7 | 30200.3 | 30200.3 | 30200.3 | 221.8 | 5 |
| AdvanceCreatureClockCommand | Transactional | 11** | 153.1 | 30186.0 | 30186.0 | 30186.0 | 30186.0 | 65.2 | 0 |
| AdvanceWeaknessRecoveryCommand | Transactional | 4** | 7477.8 | 12279.7 | 12279.7 | 12279.7 | 12279.7 | 252.2 | 0 |
| CompleteReturnToSelectionCommand | Workflow | 28** | 0.22 | 310.7 | 458.2 | 458.2 | 458.2 | 4.8 | 0 |
| CompensateSelectedCharacterSessionCommand | Workflow | 40** | 0.07 | 6.38 | 307.6 | 307.6 | 307.6 | 66.2 | 24 |
| ReadItemUsageSnapshotCommand | ReadOnly | 47** | 0.12 | 42.23 | 42.27 | 42.27 | 42.27 | 3.9 | 0 |
| GetAccountCharacterRosterCommand | Direct | 25** | 0.23 | 2.60 | 6.25 | 6.25 | 6.25 | 2.0 | 0 |
| ActivateClientHotkeyProfileCommand | Direct | 177* | 0.01 | 5.95 | 5.99 | 5.99 | 5.99 | 1.9 | 0 |
| PublishSelectedCharacterSessionCommand | Workflow | 79** | 0.02 | 0.08 | 3.97 | 3.97 | 3.97 | 2.7 | 0 |
| ReadPremiumSnapshotCommand | ReadOnly | 3** | 0.10 | 0.40 | 0.40 | 0.40 | 0.40 | 66.6 | 0 |

命令日志行 1691 条（其中失败 113）。

## (f) 冷启动：Host 就绪后前 60 秒

包含建号与选角在内的全部请求（setup 与 background 组，截尾样本计入），按计划发出时刻所在的秒聚合。最长单次停顿 = 窗口内的最大单请求延迟（判定口径：计划发出 → 第一条回包；截尾样本取到超时/关闭，是下界）。

最长单次停顿：**≥284314.1 ms**（（计划但未发出），截尾：超时或连接被关闭，计划发出 t=6.65 s，solo06；参考口径 – ms）。冷启动窗整体：8880（截尾 6834） | 249010.8 | 277060.7 | 282660.7 | 284010.7 | ≥284314.1（样本 | p50 | p95 | p99 | p99.9 | max）。

<details><summary>逐秒</summary>

| 秒 | 样本 | p99 (ms) | max (ms) | max 的请求 |
|---|---:|---:|---:|---:|
| 0 | 0 | – | – | – |
| 1 | 30 | 887.6 | 887.6 | LOGIN |
| 2 | 15 | 630.5 | 630.5 | SELECT_CHARACTER |
| 3 | 12 | 788.7 | 788.7 | RETURN_SELECT_CHARACTER |
| 4 | 169 | 254684.5 | 254734.5 | CHECK_USER_CONNECTION |
| 5 | 150 | 254285.2 | 254335.1 | CHECK_USER_CONNECTION |
| 6 | 152 | 284264.1 | 284314.1 | （计划但未发出） |
| 7 | 148 | 283914.1 | 283960.7 | （计划但未发出） |
| 8 | 155 | 282914.1 | 282960.7 | （计划但未发出） |
| 9 | 152 | 281914.1 | 281960.7 | （计划但未发出） |
| 10 | 141 | 280914.1 | 280960.7 | （计划但未发出） |
| 11 | 154 | 279914.1 | 279960.7 | （计划但未发出） |
| 12 | 149 | 278914.1 | 278960.7 | （计划但未发出） |
| 13 | 158 | 277960.7 | 277960.8 | （计划但未发出） |
| 14 | 150 | 276960.7 | 276960.8 | （计划但未发出） |
| 15 | 155 | 275960.7 | 275960.8 | （计划但未发出） |
| 16 | 154 | 274960.7 | 274960.8 | （计划但未发出） |
| 17 | 154 | 273960.7 | 273960.8 | （计划但未发出） |
| 18 | 155 | 272960.7 | 272960.8 | （计划但未发出） |
| 19 | 157 | 271960.7 | 271960.8 | （计划但未发出） |
| 20 | 157 | 270960.7 | 270960.8 | （计划但未发出） |
| 21 | 158 | 269960.7 | 269960.8 | （计划但未发出） |
| 22 | 158 | 268960.7 | 268960.8 | （计划但未发出） |
| 23 | 159 | 267960.7 | 267960.8 | （计划但未发出） |
| 24 | 160 | 266960.7 | 266960.8 | （计划但未发出） |
| 25 | 158 | 265960.7 | 265960.8 | （计划但未发出） |
| 26 | 158 | 264960.7 | 264960.8 | （计划但未发出） |
| 27 | 157 | 263960.7 | 263960.8 | （计划但未发出） |
| 28 | 154 | 262960.7 | 262960.8 | （计划但未发出） |
| 29 | 160 | 261960.7 | 261960.8 | （计划但未发出） |
| 30 | 160 | 260960.7 | 260960.8 | （计划但未发出） |
| 31 | 159 | 259960.7 | 259960.8 | （计划但未发出） |
| 32 | 158 | 258960.7 | 258960.8 | （计划但未发出） |
| 33 | 160 | 257960.7 | 257960.8 | （计划但未发出） |
| 34 | 163 | 256960.7 | 256960.8 | （计划但未发出） |
| 35 | 158 | 255960.7 | 255960.8 | （计划但未发出） |
| 36 | 159 | 254960.7 | 254960.8 | （计划但未发出） |
| 37 | 158 | 253960.7 | 253960.8 | （计划但未发出） |
| 38 | 158 | 252960.7 | 252960.8 | （计划但未发出） |
| 39 | 158 | 251960.7 | 251960.8 | （计划但未发出） |
| 40 | 160 | 250960.7 | 250960.8 | （计划但未发出） |
| 41 | 160 | 249960.8 | 249961.2 | （计划但未发出） |
| 42 | 160 | 248960.8 | 248961.2 | （计划但未发出） |
| 43 | 160 | 247960.8 | 247961.2 | （计划但未发出） |
| 44 | 160 | 246960.8 | 246961.2 | （计划但未发出） |
| 45 | 160 | 245960.8 | 245961.2 | （计划但未发出） |
| 46 | 160 | 244960.8 | 244961.2 | （计划但未发出） |
| 47 | 160 | 243960.8 | 243961.2 | （计划但未发出） |
| 48 | 160 | 242960.8 | 242961.2 | （计划但未发出） |
| 49 | 160 | 241960.8 | 241961.2 | （计划但未发出） |
| 50 | 160 | 240960.8 | 240961.2 | （计划但未发出） |
| 51 | 160 | 239960.8 | 239961.2 | （计划但未发出） |
| 52 | 160 | 238960.8 | 238961.2 | （计划但未发出） |
| 53 | 160 | 237960.8 | 237961.2 | （计划但未发出） |
| 54 | 160 | 236960.8 | 236961.2 | （计划但未发出） |
| 55 | 160 | 235960.8 | 235961.2 | （计划但未发出） |
| 56 | 160 | 234960.8 | 234961.2 | （计划但未发出） |
| 57 | 160 | 233960.8 | 233961.2 | （计划但未发出） |
| 58 | 160 | 232960.8 | 232961.2 | （计划但未发出） |
| 59 | 160 | 231960.8 | 231961.2 | （计划但未发出） |

</details>

## (d) 10 个并发登录

每轮 10 个并发：启动器 HTTP 注册（口令哈希）→ 启动器 HTTP 登录（口令校验）→ TCP 连接 → LOGIN；LOGIN 应答后断开。「轮内」= 从该轮开始到 10 个都完成的区间；比较轮内发出的背景请求与稳态窗的端到端分布。

| 背景请求 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 稳态窗（对照） | 19230（截尾 19200） | 170864.1 | 224960.8 | 229761.2 | 230860.7 | ≥230961.2 |
| 10 并发登录的轮内 | 12167（截尾 12144） | 69864.1 | 105114.1 | 108164.1 | 108861.2 | ≥108914.1 |
| （参考：实际发出口径）稳态窗 | 30** | 498.5 | 32364.8 | 32366.4 | 32366.4 | 32366.4 |
| （参考：实际发出口径）轮内 | 23** | 1186.5 | 3589.9 | 32304.4 | 32304.4 | 32304.4 |
| 登录阶段整体（182–259 s） | 12337（截尾 12314） | 70364.1 | 105064.1 | 108164.1 | 108861.2 | ≥108914.1 |

每轮时长（开始 → 10 个 LOGIN 都应答）：8984, 60004, 2166, 1300, 1768, 1720 ms。

按连接角色拆分（每条连接有自己的节拍表，一个角色的数字只含它自己的请求；双人组的组队、进本、离本由领队发起，记在领队连接上）：

| 连接角色 | 窗口 | 判定：样本 | p50 | p95 | p99 | p99.9 | max (ms) | 参考 p99 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 单人 | 稳态窗 | 19230（截尾 19200） | 170864.1 | 224960.8 | 229761.2 | 230860.7 | ≥230961.2 | 32366.4 |
| 单人 | 轮内 | 12167（截尾 12144） | 69864.1 | 105114.1 | 108164.1 | 108861.2 | ≥108914.1 | 32304.4 |
| 双人组领队 | 稳态窗 | 0 | – | – | – | – | – | – |
| 双人组领队 | 轮内 | 0 | – | – | – | – | – | – |
| 双人组队员 | 稳态窗 | 0 | – | – | – | – | – | – |
| 双人组队员 | 轮内 | 0 | – | – | – | – | – | – |

| 登录者自身 | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 启动器注册（HTTP，含哈希） | 50** | 681.7 | 7833.9 | 7961.3 | 7961.3 | 7961.3 |
| 启动器登录（HTTP，含校验） | 50** | 801.4 | 1010.5 | 1013.2 | 1013.2 | 1013.2 |
| Game LOGIN 0x0001 | 50** | 329.0 | 645.6 | 755.3 | 755.3 | 755.3 |

样本数带 * 的行 n < 1000，p99.9 即 max；带 ** 的行 n < 100，p99 也即 max（nearest-rank）。「截尾」= 超时或连接被关闭、没等到回包的请求，取「计划发出 → 超时/关闭」计入分布，是下界；max 前的 ≥ 表示最大值就是截尾样本。

## (e) 单连接灌包

灌包连接在城门区（不共享在场，无扇出）以窗口 32 持续发 CHECK_USER_CONNECTION；实际应答速率 199107 包/秒（5973232 个）。

|  | 样本 | p50 | p95 | p99 | p99.9 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|
| 其余连接，稳态窗（对照） | 19230（截尾 19200） | 170864.1 | 224960.8 | 229761.2 | 230860.7 | ≥230961.2 |
| 其余连接，灌包期间 | 4805（截尾 4800） | 16964.1 | 30464.1 | 31664.1 | 31961.2 | ≥31964.1 |
| （参考：实际发出口径）其余连接，灌包期间 | 5** | 2.12 | 30119.3 | 30119.3 | 30119.3 | 30119.3 |
| 灌包连接自身（含其窗口内排队） | 5973263 | 0.06 | 0.10 | 0.13 | 1.00 | 16.48 |

灌包窗口 32 决定灌包连接最多有多少个请求同时在服务端排队；改造后它在单执行器队列里最多占这么多项，(e) 的结果直接取决于这个窗口（见 README）。

## 入队等待

基线不存在：基线没有执行器，也就没有「入队 → 开始执行」这个量（RQ-090）。上面的 (d)(e)(f) 用端到端延迟作对照口径，不拿别的量代替入队等待。

## 负载下的单请求出站字节（每类请求、每个收件连接的最大值）

口径：带步骤栅栏的请求，请求者从发出到栅栏应答之间收到的字节（不含栅栏应答）；双人组的同伴从请求发出到它自己的收口栅栏应答之间收到的字节。负载下其他玩家的城镇投影也会落进窗口，所以这里是「含他人投影」的上界；精确值见 outbound 运行。

| 请求 | 角色 | 窗口数 | 最大字节 | 出自场景 | 时刻 (s) |
|---|---:|---:|---:|---:|---:|
| SELECT_CHARACTER | 请求者 | 43 | 39379 | selection after create | 158.9 |
| RETURN_SELECT_CHARACTER | 请求者 | 27 | 6715 | return to selection | 4.8 |
| SET_USER_AREA | 请求者 | 128 | 5770 | town | 5.6 |
| LOGIN | 请求者 | 68 | 339 | login | 1.7 |
| CREATE_CHARACTER | 请求者 | 25 | 313 | create | 259.5 |

## 与 RQ-091 阈值的对照（基线预判）

| 项 | 阈值 | 基线（本次） | 说明 |
|---|---:|---:|---:|
| (b) 可持续吞吐 | ≥ 2000 CMD/秒 | 见 ramp 运行 | 本运行只跑 400 包/秒 |
| (c) 400/秒下等待 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 229761.2 ms |
| (d) 10 并发登录下 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：轮内 p99 108164.1 ms |
| (e) 灌包下其他连接 p99 | ≤ 50 ms | 基线不存在 | 端到端对照：p99 31664.1 ms |
| 单条命令 p99.9 / max | ≤ 100 / ≤ 250 ms | 21 个命令超阈值 | 见下 |
| (f) 最长单次停顿 | ≤ 250 ms | 284314.1 ms | 冷启动窗，端到端 |

M-1 预警（命令执行时长 p99.9 > 100 ms 或 max > 250 ms）：

- `ExpireCharacterNameTagCommand`：样本 54，p99.9 61339.3 ms，max 61339.3 ms（出现于 t=127.2 s；冷启动窗内 max 0.19 ms，之后 max 61339.3 ms）
- `PrepareReturnToSelectionCommand`：样本 28，p99.9 60266.2 ms，max 60266.2 ms（出现于 t=252.0 s；冷启动窗内 max 30129.6 ms，之后 max 60266.2 ms）
- `PrepareSelectedCharacterSessionCommand`：样本 94，p99.9 60266.1 ms，max 60266.1 ms（出现于 t=252.0 s；冷启动窗内 max 30157.1 ms，之后 max 60266.1 ms）
- `ApplySelectedCharacterDailyRefillCommand`：样本 80，p99.9 60265.4 ms，max 60265.4 ms（出现于 t=252.0 s；冷启动窗内 max 30191.1 ms，之后 max 60265.4 ms）
- `RefillOnlineSouthernTicketCommand`：样本 73，p99.9 60263.0 ms，max 60263.0 ms（出现于 t=251.9 s；冷启动窗内 max 321.1 ms，之后 max 60263.0 ms）
- `InitializeHousingCommand`：样本 87，p99.9 60261.4 ms，max 60261.4 ms（出现于 t=251.9 s；冷启动窗内 max 6.10 ms，之后 max 60261.4 ms）
- `SynchronizeSelectedCharacterQuestsCommand`：样本 59，p99.9 60250.4 ms，max 60250.4 ms（出现于 t=251.9 s；冷启动窗内 max 265.9 ms，之后 max 60250.4 ms）
- `ReviveSelectedCharacterPetCommand`：样本 52，p99.9 60250.3 ms，max 60250.3 ms（出现于 t=251.9 s；冷启动窗内 max 2.75 ms，之后 max 60250.3 ms）
- `RegisterAccountCommand`：样本 81，p99.9 60047.7 ms，max 60047.7 ms（出现于 t=252.1 s；冷启动窗内 max 956.0 ms，之后 max 60047.7 ms）
- `AuthenticateAndIssueTicketCommand`：样本 119，p99.9 32555.4 ms，max 32555.4 ms（出现于 t=252.1 s；冷启动窗内 max 952.5 ms，之后 max 32555.4 ms）
- `RefillOnlineBossTowerTicketCommand`：样本 65，p99.9 30541.1 ms，max 30541.1 ms（出现于 t=127.3 s；冷启动窗内 max 322.5 ms，之后 max 30541.1 ms）
- `RedeemGameLoginCommand`：样本 118，p99.9 30296.9 ms，max 30296.9 ms（出现于 t=127.4 s；冷启动窗内 max 868.7 ms，之后 max 30296.9 ms）
- `EnsureDailyContractPuppetsCommand`：样本 89，p99.9 30266.8 ms，max 30266.8 ms（出现于 t=189.2 s；冷启动窗内 max 5.08 ms，之后 max 30266.8 ms）
- `RefillOnlineTauTicketCommand`：样本 79，p99.9 30234.9 ms，max 30234.9 ms（出现于 t=96.7 s；冷启动窗内 max 320.6 ms，之后 max 30234.9 ms）
- `CleanupExpiredCharacterItemsCommand`：样本 89，p99.9 30224.8 ms，max 30224.8 ms（出现于 t=189.1 s；冷启动窗内 max 119.2 ms，之后 max 30224.8 ms）
- `CreateCharacterCommand`：样本 25，p99.9 30221.4 ms，max 30221.4 ms（出现于 t=189.3 s；冷启动窗内 max 102.8 ms，之后 max 30221.4 ms）
- `PrepareSelectedCharacterStateCommand`：样本 85，p99.9 30200.3 ms，max 30200.3 ms（出现于 t=221.8 s；冷启动窗内 max 548.6 ms，之后 max 30200.3 ms）
- `AdvanceCreatureClockCommand`：样本 11，p99.9 30186.0 ms，max 30186.0 ms（出现于 t=65.2 s；冷启动窗内 max 30157.2 ms，之后 max 30186.0 ms）
- `AdvanceWeaknessRecoveryCommand`：样本 4，p99.9 12279.7 ms，max 12279.7 ms（出现于 t=252.2 s；冷启动窗内 max – ms，之后 max 12279.7 ms）
- `CompleteReturnToSelectionCommand`：样本 28，p99.9 458.2 ms，max 458.2 ms（出现于 t=4.8 s；冷启动窗内 max 458.2 ms，之后 max 310.7 ms）
- `CompensateSelectedCharacterSessionCommand`：样本 40，p99.9 307.6 ms，max 307.6 ms（出现于 t=66.2 s；冷启动窗内 max 0.87 ms，之后 max 307.6 ms）

## 错误与无回包

错误 51 条；背景 agent 重连 41 次（失败后重新取票、LOGIN、选角，双人组两条一起重连）；双人组进本 0 次；服务端关闭连接或超时的请求 43839 个。

判定口径端到端 > 250 ms 的请求（setup 与 background，含截尾样本）：45372 个；截尾样本（超时或连接被关闭）43806 个，协议无回包 0 个。
- （计划但未发出）：43745 个（其中截尾 43745），最大 284314 ms，计划发出时刻 7.1, 7.1, 7.2, 7.2, 7.3, 7.3, 7.4, 7.4, 7.5, 7.5, 7.6, 7.6 … s
- CHECK_USER_CONNECTION：714 个（其中截尾 0），最大 269491 ms，计划发出时刻 4.6, 4.6, 5.7, 5.8, 6.1, 6.1, 6.6, 6.7, 6.9, 7.0, 7.0, 7.1 … s
- SET_USER_POSITION：663 个（其中截尾 0），最大 269591 ms，计划发出时刻 4.4, 4.5, 5.7, 5.9, 6.0, 6.0, 6.2, 6.2, 6.3, 6.3, 6.4, 6.4 … s
- SET_USER_AREA：96 个（其中截尾 0），最大 269691 ms，计划发出时刻 4.3, 4.4, 5.6, 5.8, 6.7, 7.2, 7.5, 7.7, 4.9, 10.4, 15.1, 10.6 … s
- SELECT_CHARACTER：75 个（其中截尾 7），最大 60002 ms，计划发出时刻 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0, 2.0 … s
- LOGIN：47 个（其中截尾 0），最大 30297 ms，计划发出时刻 1.1, 1.1, 1.2, 1.2, 1.3, 1.4, 1.4, 1.5, 1.6, 1.6, 1.7, 4.4 … s
- RETURN_SELECT_CHARACTER：29 个（其中截尾 2），最大 282786 ms，计划发出时刻 4.0, 4.4, 4.6, 4.5, 10.0, 10.3, 14.3, 8.6, 40.1, 17.7, 11.9, 15.5 … s
- CREATE_CHARACTER：3 个（其中截尾 0），最大 30223 ms，计划发出时刻 158.6, 159.1, 259.5 s

失败的命令 113 条：ApplySelectedCharacterDailyRefillCommand ×1（最长 30083 ms）；ExpireCharacterNameTagCommand ×45（最长 61339 ms）；RefillOnlineSouthernTicketCommand ×8（最长 60263 ms）；PrepareSelectedCharacterStateCommand ×5（最长 30102 ms）；CompensateSelectedCharacterSessionCommand ×24（最长 0 ms）；ReviveSelectedCharacterPetCommand ×2（最长 1189 ms）；SynchronizeSelectedCharacterQuestsCommand ×7（最长 1235 ms）；RefillOnlineBossTowerTicketCommand ×6（最长 1149 ms）；RegisterAccountCommand ×10（最长 60048 ms）；RefillOnlineTauTicketCommand ×2（最长 1210 ms）；EnsureDailyContractPuppetsCommand ×2（最长 813 ms）；AuthenticateAndIssueTicketCommand ×1（最长 867 ms）
- t=34.8s pair04: pair04M: connection closed during SELECT_CHARACTER after 30135 ms (first reply CMD  0x0004 (270B) 01000000).
- t=64.7s pair03: pair03M: SELECT_CHARACTER timed out.
- t=64.7s pair05: pair05M: SELECT_CHARACTER timed out.
- t=64.7s pair01: pair01M: SELECT_CHARACTER timed out.
- t=64.7s pair00: pair00M: SELECT_CHARACTER timed out.
- t=64.7s pair02: pair02M: SELECT_CHARACTER timed out.
- t=64.7s solo00: solo00: SELECT_CHARACTER timed out.
- t=64.8s solo06: solo06: SELECT_CHARACTER timed out.
- t=64.9s solo05: solo05: SELECT_CHARACTER timed out.
- t=94.9s solo07: solo07: SELECT_CHARACTER timed out.
- t=95.1s solo01: solo01: SELECT_CHARACTER timed out.
- t=96.7s pair04: pair04L: connection closed during SELECT_CHARACTER after 30855 ms (first reply CMD  0x0004 (270B) 01000000).
- t=125.9s pair03: pair03L: SELECT_CHARACTER timed out.
- t=125.9s solo05: solo05: SELECT_CHARACTER timed out.
- t=125.9s solo06: solo06: SELECT_CHARACTER timed out.
- t=125.9s pair01: pair01L: SELECT_CHARACTER timed out.
- t=125.9s pair00: pair00L: SELECT_CHARACTER timed out.
- t=125.9s pair05: pair05L: SELECT_CHARACTER timed out.
- t=125.9s pair02: pair02L: SELECT_CHARACTER timed out.
- t=125.9s solo00: solo00: SELECT_CHARACTER timed out.
- t=158.6s pair01: pair01L: connection closed during SELECT_CHARACTER after 30650 ms (first reply none).
- t=187.9s pair03: pair03L: SELECT_CHARACTER timed out.
- t=187.9s solo05: solo05: SELECT_CHARACTER timed out.
- t=187.9s pair05: pair05L: SELECT_CHARACTER timed out.
- t=187.9s pair00: pair00L: SELECT_CHARACTER timed out.
- t=187.9s solo00: solo00: SELECT_CHARACTER timed out.
- t=187.9s pair02: pair02L: SELECT_CHARACTER timed out.
- t=219.1s solo07: solo07: SELECT_CHARACTER timed out.
- t=221.8s pair01: pair01L: connection closed during SELECT_CHARACTER after 31421 ms (first reply CMD  0x0004 (270B) 01000000).
- t=249.2s pair04: pair04M: SELECT_CHARACTER timed out.

