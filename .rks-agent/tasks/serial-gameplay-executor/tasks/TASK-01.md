# TASK-01 — 基线重跑与 RQ-080 表征测试

## Task Unit Contract

- Revision: 1
- Outcome and references: 在基线代码上（HEAD `b76504bb`，产品代码与 `a02bce46` 相同）重跑完整构建与全量测试并落盘；为 RQ-080 (a)～(g) 七个工作流写出经真实 TCP 连接、断言完整帧字节与帧序的表征测试，并在基线上通过。覆盖 Plan T-001 的「基线」与「表征」两部分，对应 RQ-080、AC-019（基线一半）、EV-2。T-001 余下的附录 A/D 统计与测量由后续单元承担。
- Owned boundary:
  - 新增 `Server/tests/Dfo.Server.Networking.Tests/Characterization/`（测试代码与金样本数据文件），必要时调整该项目 csproj 以复制数据文件；
  - 新增 `.rks-agent/tasks/serial-gameplay-executor/verification/`（基线说明 `baseline.md` 与 `logs/`）；
  - 不改任何产品代码（`Server/src`）和既有测试文件。
- Required checks:
  1. 未加新测试前：`dotnet build Server/86JP.Server.slnx -c Release` 0 警告 0 错误；`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"` 完整输出落盘为 `verification/logs/baseline-build-test.txt`，失败只允许是 `DimensionDungeonTests` 的 2 条，以及 AGENTS.md 登记的偶发项（偶发项必须按「三样一起看」取得身份并隔离复跑）。
  2. 加入表征测试后：构建仍 0 警告 0 错误；`Dfo.Server.Networking.Tests` 隔离运行全绿，且七组表征测试都**真的执行了**（不是被 Skip）；表征测试连续运行 3 次全绿（防时序抖动），日志落盘为 `verification/logs/characterization-baseline.txt`。
  3. 每条表征测试的「可变字段」掩码逐项列出，并附理由（见 Detailed Plan §3）。
- Dependencies: 无
- Result review: required — 后续每个 Task 都以这批测试作为「行为不变」的主要证据。如果它们断言太松（只断言顺序、掩码过宽、靠可删除的接缝准备数据），或者带着将被删除的接缝，那么后面的搬迁、删锁、换投递即使改坏了行为，它们也照样是绿的；而这种缺陷只靠测试本身变绿是暴露不出来的。

## Detailed Plan

### 1. 先跑基线，再写测试

在写任何新文件之前完成 Required check 1，并按上一任务的格式写 `verification/baseline.md`：各项目的通过、失败、跳过、总计，以及加法。这次运行的结果就是后续 Task「与基线对比」的基准。

### 2. 表征测试的形态

放在 `Dfo.Server.Networking.Tests/Characterization/`，每个工作流一个文件，共享一个驱动件。要求如下：

- **黑盒驱动，只依赖改造后仍然存在的东西**：
  - 主机按 `Integration/TestInfrastructure` 的方式装配真实模块、真实 SQLite 和真实 PVF（用 `PvfFact`）；
  - 状态准备只经线上协议完成，例如经 TCP 登录、建角、选角、组队、进本；
  - **不得**使用 `ICommandBus`、具体命令类型、消息 handler、门、`INotificationBus`、`IEventService`，也不得替换 `INetworkMessageDispatcher` 或 sender，因为这些都会在后续 Task 中改形或删除。
  - 如果某一步实在无法经线上完成（例如账号需要预置），只允许直接写 SQLite 表，或者经 HTTP 启动器注册。两者都要在测试里注明原因。
- **故障注入**（用于 (b)，以及其他需要的地方）只允许两种接缝：
  - 在 DI 里用装饰器替换某个 `Dfo.Server.Abstractions` 里以 `DomainService` 结尾的接口，让它在指定调用上抛出；
  - 在传输层由客户端一侧主动断开 socket。
  
  选取的接缝，必须是附录 F 和附录 H 都没有计划删除或改形的接口。
- **断言完整帧**：
  - 每个连接按到达顺序收集全部出站帧（15 字节 Game 头加载荷），与金样本逐帧逐字节比对。金样本用十六进制文本文件保存，一帧一行，并带注释行说明这一帧是哪条消息。
  - 断言帧数和帧序。不得只断言「包含某帧」。
  - 跨连接的顺序要求（XO-1 跟随者先于请求者、XO-2 同伴先于领队 ACK），另外用到达的时间先后来断言。做法是：在请求者收到它的那一帧之前，确认跟随者的对应帧已经读到。
- **可变字段掩码**：时间戳、随机种子、自增 UID、密钥等每次运行都会变的字节，在比对前按「帧类型 + 偏移 + 长度」掩掉。掩码表写在驱动件里，每一项附一句说明：值为什么会变、取自哪个字段。能通过固定 `TimeProvider`、固定种子等手段让它变得确定的，优先固定，而不是掩掉。掩码不得覆盖整帧。
- **金样本的产生**：在基线上跑一次，把实际帧录下来作为金样本。逐帧对照协议记录（`protocol/`）或者现有 codec 测试，确认录到的是预期消息，不是错误路径的产物（例如拒绝包）。这次对照的结论写进 Result。

### 3. 七个工作流（RQ-080）

| 项 | 场景 | 关键断言 |
|---|---|---|
| (a) | 单人登录 → 建角 → 选角成功 | 选角全序列帧，与 `SelectCharacterAcceptedTranscriptTests` 的 62 帧在类型序列上一致 |
| (b) | 选角在某个后段阶段失败 → 补偿 | 失败时请求者收到的帧（含相邻的 `01/0004` 拒绝与 `00/01F7`），以及断开；若有同伴在线，同伴收到的清理通告 |
| (c) | 两人组队，领队请求进本 | 两个连接的完整帧；XO-1 |
| (d) | 组队在副本内，离开副本 | 两个连接的完整帧；XO-2 |
| (e) | 两人组队进塔（死亡塔或幻影塔，挑一个有组队路径的） | 两个连接的完整帧 |
| (f) | 组队后一人离队 | 两个连接的完整帧 |
| (g) | 两人同城镇，一人断开 TCP | 另一人收到的清理通告帧 |

场景细节（选哪个副本、哪个阶段失败）由执行者按现有集成测试与 `Tool/WireSmoke` 的已知可行路径选定，在 Result 里写明。如果某一项在基线上根本走不通（例如组队进塔没有可用的线上路径），不得用一个更弱的替代场景冒充，而要按 executing-tasks 写 Blocker，交给控制方处置。

### 4. 执行顺序

基线运行 → 驱动件（连接、收帧、掩码、金样本读写）→ (a) → (g) → (f) → (c) → (d) → (e) → (b) → 3 次连续运行 → 全量构建与 Networking.Tests 隔离运行 → 写 Result。

## Result

（第 4 版，覆盖前三版。按主控方历次返工裁定：(b1)(b2) 各自拆成常绿表征加跳过的严格版；补充根因定位与负载循环结果；结果审查 B-1 按方案 (ii) 处理，撤回 XO-1/XO-2 的断言；并写入 N-1～N-6。）

### Outcome

done。

- **基线**：已重跑并落盘。
- **表征测试**：RQ-080 (a)～(g) 共 10 个用例。其中 8 个执行；另 2 个严格版（b1-strict、b2-strict）按裁定跳过，由 T-006 启用。8 个执行用例都经真实 TCP 在基线上确定通过，并按完整帧字节与金样本比对。
- **跨连接顺序 XO-1/XO-2**：没有断言，已撤回，改由 T-003/T-006 的 AC-012 测试承接。原因是基线本身不保证客户端看到的到达先后，见 Checks 的 XO 测量和 Remaining 第 1 条。用于检测的代码已删除。
- **Blocker**：无。主控方已预先裁定，XO 的降级可以接受。

### Changed

**修改**：`Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj`

- 引用 `Dfo.Server.GameServer` 与 `Dfo.Server.HttpApi`，按 `Dfo.Server.Host/Program.cs` 的组合方式在进程内组装宿主，包括启动器 HTTP。
- 链接 `tests/Support/PvfAttributes.cs`。
- 把 `Characterization/Golden/*.hex` 复制到输出目录。

**新增**：`Server/tests/Dfo.Server.Networking.Tests/Characterization/`

驱动件：

- `CharacterizationHost.cs`：进程内宿主。
  - 使用真实模块、每个用例一个临时 SQLite、真实 Script.pvf，并固定 `TimeProvider`。
  - 经 `/api/launcher/v1/register` 与 `/api/launcher/v1/login` 签发票据。
  - 设置 `DFO_CHARACTERIZATION_DEBUG=1` 时，额外输出发送器逐帧的 `S->C` 调试日志。
- `WireClient.cs`：A21 帧格式的客户端。
  - 入站 sequence 逐帧递增。
  - 按大块读取 socket 数据。
  - 用 `CHECK_USER_CONNECTION`（CMD 0x04DD）作为步骤栅栏。
  - 记录步骤标记和全部出站帧，以及连接关闭的方式（`CloseKind`）。
- `CharacterizationTranscript.cs`：掩码表、浮动帧规则、金样本的读写与比对。可选参数 `lastStepMayEndEarly` 允许观测转写在最后一步内提前结束，见下文。
- `CharacterizationSteps.cs`、`DeterministicLayouts.cs`、`FrameNames.cs`、`CharacterizationCollection.cs`：共用步骤、固定布局种子、帧名注释、让用例串行执行（每个用例 120 s）。

场景：

- `SelectCharacterSuccessCharacterizationTests.cs`：(a)
- `SelectCharacterCompensationCharacterizationTests.cs`：(b1) 常绿、(b1-strict)、(b2) 常绿、(b2-strict)
- `PartyCharacterizationTests.cs`：(c)(d)(f)(g)
- `PartyTowerCharacterizationTests.cs`：(e)

金样本：`Golden/*.hex`，15 个文件，每个场景的每条连接一个。常绿版与严格版共用同一组金样本：(b1) 两者用 `b-select-cleanup-failure.*`，(b2) 两者用 `b-select-quest-failure.*`。

**新增**：`.rks-agent/tasks/serial-gameplay-executor/verification/baseline.md`、`verification/logs/`

**拆分后的用例清单**（10 个）

| 项 | 用例 | 状态 |
|---|---|---|
| (a) | `SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript` | 执行 |
| (b1) 常绿 | `SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_refuses_the_entry_and_closes_the_connection` | 执行 |
| (b1-strict) | `SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close` | `[PvfFact(Skip = "Baseline defect: close can overtake the refusal pair under load; enabled by T-006 (RQ-031(c), AC-011).")]` |
| (b2) 常绿 | `SelectCharacterCompensationCharacterizationTests.A_failed_quest_synchronization_after_publication_compensates_and_closes_the_connection` | 执行 |
| (b2-strict) | `SelectCharacterCompensationCharacterizationTests.A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close` | `[PvfFact(Skip = "Baseline defect: close can overtake the tail of the entry batch under load; enabled by T-006 (RQ-031(c), AC-007).")]` |
| (c) | `PartyCharacterizationTests.A_party_entering_a_dungeon_produces_the_recorded_transcripts` | 执行 |
| (d) | `PartyCharacterizationTests.Leaving_a_dungeon_produces_the_recorded_transcripts` | 执行 |
| (e) | `PartyTowerCharacterizationTests.A_party_entering_the_tower_of_death_receives_the_recorded_transcripts` | 执行 |
| (f) | `PartyCharacterizationTests.Leaving_a_party_projects_to_both_players` | 执行 |
| (g) | `PartyCharacterizationTests.A_disconnect_in_town_announces_the_departure_to_the_other_player` | 执行 |

**各场景的具体路径**

公共准备只走线上协议：

1. 经启动器 HTTP 注册并签发票据；
2. LOGIN `0x0001`；
3. CREATE_CHARACTER `0x0005`（job 0，与 wireclient.py 的 body 相同）；
4. SELECT_CHARACTER `0x0004`（槽 0）。

双人场景再做两步：

1. 两人各发 SET_USER_AREA `0x0024` 进入 1:1。出生的城门区域不共享在场状态，在那里组队会被静默拒绝。
2. 领队发 REQUEST_PEER `0x000A`（type 0），队员回 RESPONSE_PEER `0x000B`（type 0）。

| 项 | 路径 |
|---|---|
| (a) | 单人完成公共准备。除了整份金样本，还断言选角批次（`01/0004`…`00/0016`）的类型序列：去掉内容条件帧后，要与 `SelectCharacterAcceptedTranscriptTests.AcceptedStaticIdentities` 的 62 帧一致，但其中两帧按条件不出现——job 12 专属的 `0x0245`，以及没有宠物时的 `0x0077`。被去掉的内容条件帧是活动块 `0x042D/0x033F/0x04B8/0x04BA/0x04B9` 和收藏箱、胶囊 `0x03D0/0x03EB/0x03DF`。 |
| (b1) 常绿 | 同伴先在 1:1 待命。请求者选角时，`IExpiredCharacterItemCleanupDomainService.CleanupAsync` 对 character 2 抛出一次异常；这一步在准备之后、尚未发出任何入场状态。基线上服务端先发相邻的 CMD `01/0004`（body `0002`）与 NOTI `00/01F7`，再关闭连接。断言如下：<br>• 选角之前的全部帧与金样本完全一致。<br>• 选角这一步读到的帧必须逐字节等于金样本中这一步的前缀；0、1、2 帧都可以接受。<br>• 之后连接必须被关闭（`IsClosed`）。<br>• 同伴侧金样本严格比对：除栅栏外不收到任何帧。<br>每次运行把实际读到的帧数写进测试输出。<br>**(b) 不覆盖同伴清理通告。** 请求者落在城门区域，基线上不共享在场状态，同伴看不到请求者，所以同伴本来就什么都收不到。清理通告由 (g) 覆盖。 |
| (b1-strict) | 与常绿版走同一条路径，断言整对拒绝帧都先于关闭到达，然后严格比对整份金样本。这条要求属于 RQ-031(c)/AC-011，在基线上不可靠，因此跳过，留给 T-006 启用。 |
| (b2) 常绿 | `ISelectCharacterQuestEntryDomainService.SynchronizeAsync` 对 character 2 抛出一次异常，时点在会话发布之后。基线上，请求者先收到成功的入场批次和尾部（到 `0x00FB`），然后连接被关闭，不发拒绝帧。断言如下：<br>• 选角之前的帧严格比对；<br>• 选角这一步读到的帧，必须是金样本这一步逐字节的前缀；<br>• 这一步不得出现 `01F7`；<br>• 之后连接被关闭；<br>• 同伴侧严格比对，除栅栏外不收到任何帧。<br>每次运行在测试输出中记录读到的帧数。 |
| (b2-strict) | 同一路径。断言整份入场批次和尾部都先于关闭到达，然后严格比对整份金样本。这属于 RQ-031(c)/AC-007 的要求，基线上不可靠，因此跳过，由 T-006 启用。 |
| (c) | 组队后，领队发 ENTER_SELECT_DUNGEON `0x000F`（body `00000000`），再发 SELECT_DUNGEON `0x0010` 进入副本 144（剧情模式，1 级可进）。<br>只逐连接比对完整转写；XO-1 不做断言，见 Remaining 第 1 条。 |
| (d) | 在 (c) 之后，两人各发 FINISH_LOADING `0x0025`，然后领队发 EPLP `0x0048`（body `0101`）。<br>只逐连接比对完整转写；XO-2 不做断言，见 Remaining 第 1 条。 |
| (e) | 组队进入死亡之塔 11000（`0x000F` 加 `0x0010`）。入塔要求 50 级并持有道具 4183，线上协议无法把新角色升到 2 级以上，也无法发放 4183。因此在建角之后、选角之前直接写 SQLite，测试代码里写明了原因：<br>• `characters.exp` 写成等级投影触发器里 50 级的阈值；<br>• 在 Main/177 放入一个 4183。<br>之后依次收到 CMD `0x0012`、`0x000E`、`0x0002`×2、`0x000F`、`0x0003`、`0x001A`、`0x019F`、`0x008E`、`0x008F`。 |
| (f) | 组队后队员发 LEAVE_PARTY `0x000D`。 |
| (g) | 两人同在 1:1，领队客户端关闭 socket。队员收到 NOTI `0x0006`（body `0100`）。 |

### Why

- **账号与票据经启动器 HTTP**：Game 协议做不到注册账号和签发票据（票据只在内存里）。按 Host 的组合根原样组装宿主。为装配真实组合，必须引用 `Dfo.Server.GameServer` 与 `Dfo.Server.HttpApi` 两个项目，主控方已接受（N-5）；没有使用 `ICommandBus`、具体命令类型、消息 handler、门、`INotificationBus`、`IEventService`，也没有替换 dispatcher 或 sender。
- **步骤边界用栅栏**：每一步先给发起方发栅栏，再给其他连接发，栅栏的应答也写进金样本。单步预算 20 s，超时时报出最后几帧。
- **故障注入**：两个接缝都是 Abstractions 中以 `DomainService` 结尾的单成员接口，都不在附录 F 或 H 中。只对 character 2 触发一次。`DomainServiceDecoration.Decorate` 在运行期检查接口后缀和所在程序集。
- **能固定的先固定**：
  - 时钟固定在 2026-08-13T02:30:00Z。
  - 每个用例使用全新数据库，建角顺序固定，因此角色 id、wire id、物品 uid、队伍 id 都是确定的。
  - 副本 144 的布局种子由 `DeterministicLayouts` 固定。它装饰 `IDungeonStructureRuleService`，只替换 `seed` 参数，用于确定性，不是故障注入。
  - `created_at` 与 3 处 InstanceValue 用掩码处理，没有改成直接写库固定（N-4，主控方已接受）。直接写库会让表征测试依赖更多表结构细节，例如 `characters.created_at` 的列和 `items.item_core` 的字节布局。掩码每项只有 4 字节，范围更窄。
- **浮动帧**：第二份 NOTI `0x0015` 由南部溪谷补给事件 `SouthernDaleDailyRefillCompleted` 的 LiveOnly 订阅者在事件循环里发出，在基线上的位置不固定。比对时把它从有序序列中取出单独列，字节与条数照比，并要求它出现在 CMD `0x0239` 之后。对 (b1)/(b2) 的请求者设为 optional（不比对）。
- **(b1)/(b2) 常绿版的前缀比对**（`lastStepMayEndEarly`）有三个条件，缺一不可：
  1. 观测必须包含金样本中的全部步骤行；
  2. 已读到的每一行都与金样本相同；
  3. 只允许缺少最后一步末尾的若干帧。

  因此登录、建角、步骤结构，以及读到的每个字节，仍然被严格钉住；被放宽的只有基线不保证送达的那一段尾部。这类金样本须在空闲机器上录制。

#### 掩码表

偏移从 15 字节帧头的第一个字节算起。

| 帧 | 偏移 | 长度 | 条件 | 理由 |
|---|---:|---:|---|---|
| CMD `0x0004` | 20（body 5） | 4 | body 长度 > 2 | 角色 `created_at` 的 unix 秒，由 SQLite `unixepoch()` 在 CREATE_CHARACTER 时写入，取的是墙钟（`CharacterEntryTables.cs`）。 |
| NOTI `0x0002` subtype 1 | 138（body 123） | 4 | body 长 519 且 body[0]=1 | 第 1 件起始装备的 `core.Value`（InstanceValue），来自 `EquipmentQualitySeeds.NewRandom()`，即 `Random.Shared`，无法注入。 |
| 同上 | 198（body 183） | 4 | 同上 | 第 2 件，两件之间相隔 60 字节（`WriteNoti2EquippedItem`）。 |
| 同上 | 258（body 243） | 4 | 同上 | 第 3 件。 |
| NOTI `0x008F`（仅 (e)） | 17（body 2） | 4 | — | `room.Seed`，由 `IllusionTowerProjectionDelivery` 用 `Random.Shared.Next()` 生成后直接交给命令。若要固定它，需要装饰有 21 个成员的塔领域服务，而那正是本次重构要改形的代码。 |

没有任何掩码覆盖帧头或整帧（`Hex()` 在运行期检查）。掩码位置的确定方法：录制 4～5 轮，对 15 个文件逐字节求差，变化的只有上表这些位置。加上掩码并固定种子后，连续比对 10 轮无差异。

#### 金样本对照结论

逐段核对，录到的都是预期消息，不是拒绝包或错误路径的产物：

- **登录**：
  - 初始连接通告 NOTI `0x0001` 与 `GameLoginIntegrationTests.BuildExpectedInitialConnectionNotice` 逐字段一致。
  - LOGIN 应答 CMD `0x0001` 与 `BuildExpectedLoginResponse` 一致。
  - 账号状态帧顺序与 `GameLoginIntegrationTests` 一致。
- **建角**：CMD `0x0005` 的 body 为 `01`。
- **选角**：CMD `0x0004` 的 body[0]=1，帧类型序列与 62 帧转写一致。
- **(b1)**：`0002` 和 `01F7` 的文本与 `SupplementalSynchronizationTests` 一致。
- **(c)**：两端的帧序与 `DungeonEntryProjectionDelivery.cs:205-283` 一致，没有出现 `0x0010` 拒绝。
- **(d)**：`001E`、`010101`，以及回城投影，与 `LeaveDungeonSlice.cs:283-309` 一致。
- **(e)**：
  - 缺少道具时收到 `001100`（MissingRequiredItem），说明 SQLite 准备是必要的；
  - 准备后 `0x0012` 扣除的是槽 177，`0x008F` 带 3 个怪物，与 `IllusionTowerProjectionDelivery.cs:63-170` 一致。
- **(f)**：`0007`、`0008` 与 `PartyNotificationTests` 的样本一致；清除帧 `0100 0100 03` 表示 partyId=1、type=3。
- **(g)**：`0006` 的 body 为离开者的 wire id `0100`。

### Checks

| 检查 | 命令 | 结果 | 日志 |
|---|---|---|---|
| 1 基线（未加测试） | `dotnet build Server/86JP.Server.slnx -c Release`；`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"` | 0 警告 0 错误。总计 7872：通过 7852，失败 2（`DimensionDungeonTests`），跳过 18。没有偶发项。 | `verification/logs/baseline-build-test.txt`、`verification/baseline.md` |
| 2a 表征连续 3 次 | `dotnet test Server/tests/Dfo.Server.Networking.Tests -c Release --filter "FullyQualifiedName~Characterization" --logger "console;verbosity=detailed"` ×3 | 每次 10 个：通过 8，跳过 2（仅 b1-strict 与 b2-strict）。其余 8 个都真实执行。 | `verification/logs/characterization-baseline.txt` |
| 2b 全量构建 | `dotnet build Server/86JP.Server.slnx -c Release` | 0 警告 0 错误 | `verification/logs/networking-isolated.txt` 开头、`full-with-characterization.txt` |
| 2c Networking.Tests 隔离运行 | `dotnet test Server/tests/Dfo.Server.Networking.Tests -c Release --no-build --logger "console;verbosity=detailed"` | 共 107 个（原 97 + 新 10）：通过 105，跳过 2 | `verification/logs/networking-isolated.txt` |
| 附加：全量测试（第 2 版代码，未拆分 b2） | 同检查 1 | 总计 7881 = 7872 + 9。通过 7860，失败 2（与基线同），跳过 19（基线 18 + b1-strict）。Networking.Tests 在全量并行下 105/106 通过，跳过 1。此后只改了测试代码：拆分 (b2)，新增一个跳过的用例。按裁定未重跑全量测试；按现在的代码推算，总计应为 7882，跳过 20。 | `verification/logs/full-with-characterization.txt` |
| 3 负载循环 | 运行期间 Application.Tests 与 Infrastructure.Tests 各自循环并行 | 见下表 | `verification/logs/characterization-load.txt` |
| 4 XO 到达先后测量（审查 B-1，方案 (i) 的前置） | 每个连接用专用线程阻塞在 `Socket.Receive`，收到的每个数据块用 `Stopwatch.GetTimestamp()` 打戳；比较请求者对应帧与跟随者对应帧的时间戳之差（lead）。产品代码未改，即基线。 | **XO-1**：空闲 10 次全部 lead > 0，但只有 3～45 µs；负载下 20 次中 6 次 ≤ 0。**XO-2**：空闲 10 次中 4 次 ≤ 0；负载下 20 次中 14 次 ≤ 0。基线下无法稳定为绿，也就做不出「反转后必红、恢复后必绿」的断言，因此改走方案 (ii)，反转实验没有进行。测量代码已删除。 | `verification/logs/xo-arrival-measurement.txt` |

负载循环结果：

| 系列 | 过滤 | 次数 | 结果 |
|---|---|---:|---|
| A（第 2 版代码，b2 尚为严格） | `Characterization` | 10 | (a)(b1 常绿)(c)(d)(e)(f)(g) 各 10/10。**(b2) 9/10**：有 1 次请求者的最后一帧 NOTI `0x00FB` 还没到，连接就已关闭。这一结果促成了 (b2) 的拆分。b1-strict 跳过。 |
| **A2（最终代码）** | `Characterization` | 10 | **8 个执行用例各 10/10**。b1-strict、b2-strict 各跳过 10 次。(b1) 常绿读到的拒绝帧：0 帧 2 次，2 帧 8 次。(b2) 常绿每次都读到选角这一步的 84 帧（与金样本 83 帧加 1 帧可选浮动帧一致），即这 10 次都未截断。 |
| C | `SelectCharacterCompensation`（b2 先运行，b1 在同一进程中随后运行，这是复现竞态的条件） | 20 | **(b1) 常绿 20/20**。实际读到的拒绝帧数：0 帧 3 次，1 帧 2 次，2 帧 15 次。说明前缀比对确实接住了截断；断言没有放宽到别处。(b2) 20/20。 |
| B | 只运行 (b1) 常绿（进程中没有更早的宿主） | 20 | 20/20，且 20 次都读到 2 帧。 |

(b1) 常绿版在负载下合计跑了 60 次（A 10 + A2 10 + C 20 + B 20），全部通过。(b2) 在 C 中按当时的严格断言 20/20 通过，常绿版在 A2 中 10/10 通过。

负载系列跑在撤回 XO 之前的代码上。那时 (c)(d) 还带着旧的非阻塞检查，该检查一直为绿，审查已经证明它无法失败。撤回之后对代码只做了两处改动：删除这段检查，重命名 (c)(d) 两个用例。帧读取方式和金样本比对都没有变，所以各系列结果对最终代码仍然有效。2a、2c 是用最终代码重跑的。2a 与 2c 的输出中，(b1) 常绿每次都读到 `2 of 2`，(b2) 常绿每次都读到 84 帧。

### Remaining

1. **XO-1/XO-2 撤回，改由 T-003/T-006 的 AC-012 测试承接**（审查 B-1，方案 (ii)）。
   - 基线本身不保证客户端看到的跨连接到达先后。服务端确实先调用跟随者的 `SendAsync`，但每条连接的 TouchSocket 发送循环各自刷写（见第 2 条）。两条连接到达时刻只差几微秒，先后两种情况都会出现（Checks 第 4 行）。
   - 所以在基线上做不出「能红也能绿」的表征断言。(c)(d) 只按连接逐帧比对完整转写，不约束跨连接顺序。测试里也没有留下无效断言。
   - XO-1/XO-2 的保证应当在 T-003 的发送端、T-006 的发件箱中，由 AC-012 的单元或集成测试证明：按登记顺序交付，并且跨连接的先后可以观测。不应依赖这批表征测试。
   - (e) 进塔本来就没有附录 D 的顺序要求，同样不断言。
2. **根因定位**：关连接会越过已经「发出」的帧。这是传输层的行为，供 T-003 设计「关连接排在已登记消息之后」时参考。
   - **发送返回时，帧还没有离开进程。**
     - `TouchSocketNetworkMessageSender.SendAsync`（`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs:59-103`）逐帧 `await ((ISender)connection.Session).SendAsync(...)`。
     - TouchSocket 4.3.1 的 TCP 会话经 `TcpTransport.Writer`（一个 PipeWriter）写入，再由独立的发送循环 `TcpTransport.RunSend` 写到 socket（`~/.nuget/packages/touchsocket/4.3.1/lib/net10.0/TouchSocket.xml`：`TransportOption.SendPipeOptions`、`TcpTransport.Writer`「用于写入数据的管道写入器」、`TcpTransport.RunSend`「运行发送循环」）。
     - 因此 `SendAsync` 返回只表示帧已进入发送管道。
   - **实验证据**：临时探针，已删除。
     - 包装 `IGameMessageSender` 后记录到：拒绝对的 `SendExplicitAsync` 正常返回、没有抛异常，然后才调用关连接；客户端却只读到 0 或 1 帧，随后是干净的 end of stream（FIN，不是 RST）。
     - 发送调试日志也显示，两帧都在关闭之前进入了发送器。
   - **关闭有两条路径，任意一条都足以越过管道。**
     - 第一条：handler 的分离关闭。`SelectCharacterHandler.cs:257-271` 在释放门之后调用 `RequestCloseExactAsync`，接着进入 `TouchSocketNetworkConnectionCloser.cs:30-44`，执行 `Task.Yield()` 之后再 `session.CloseAsync(...)`，以 fire-and-forget 方式运行，不是同步关闭。
     - 第二条：handler 重新抛出异常（`SelectCharacterHandler.cs:273` 的 `failure?.Throw()`），异常穿过 `GameNetworkPlugin.OnTcpReceived`（`NetworkPlugins.cs:150-165` 记录日志后 `throw`），由 TouchSocket 拆除会话。
     - 探针把关闭器换成空操作之后，连接照样被关闭；在满载下仍有 8/14 次丢帧（0 帧 6 次，1 帧 2 次）。所以仅靠第二条路径就足以越过管道。
     - 保留真实关闭器时为 3/10，延迟 500 ms 关闭时没有改善。
   - **复现条件**：满载，并且同一测试进程中更早运行过另一个宿主。
     - 探针中先跑一个宿主再测 (b1)：真实关闭器 3/10 丢帧；只跑 (b1)：0/10。负载系列 B 也是 0/20。
     - 推测是前一个宿主的拆除工作与新宿主的发送循环争用线程池，从而拉开了「写入管道」与「刷到 socket」之间的时间窗。这一点未证实。
   - **对 T-003 的含义**：
     - 「交付」不能以 `ISender.SendAsync` 返回为准。关连接必须等发送管道刷出之后才能执行，比如先完成写端并等待发送循环结束，再关闭。
     - handler 异常导致的拆除也必须排在已登记消息之后，否则 RQ-031(c)/AC-011 仍会被这条路径破坏。
     - b1-strict 和 b2-strict 是现成的回归判据。
     - (b2) 的现象是补偿后关闭越过入场尾帧（系列 A 中 `expected 99 lines, observed 98`，缺 NOTI `0x00FB`），与 (b1) 同根因。
3. **两条严格用例都由 T-006 启用**：
   - b1-strict 对应 RQ-031(c)、AC-011；
   - b2-strict 对应 RQ-031(c)、AC-007。

   启用办法：去掉 `Skip`。届时如果附录 G（VC-1/VC-2）允许出现差异，逐条对应说明（N-2）。例如 T-008 把选角归并为一条协议 CMD 之后，按 VC-2，未处理异常所在工作单元里登记的入场批次不会发出，b2-strict 的期望需要随之调整。常绿版届时保留或删除，由 T-006 决定。
4. **后续 Task 引用常绿版作证据时要看帧数**（N-1）。在 T-006 启用严格版之前，常绿版允许拒绝对读到 0 帧，所以「拒绝对不再发出」这类回归不会让它变红。每次隔离运行都要核对测试输出：
   - (b1) 应为 `refusal frames read before the close: 2 of 2`；
   - (b2) 应为 `selection frames read before the close: 84`。

   隔离运行中出现 0 帧或帧数变少，必须追查，不能只看绿灯。本次隔离运行中的数字都是 2 和 84。
5. **表征证据必须来自本地有 PVF 的运行，并核对执行数**（N-6）。全部用例都是 `[PvfFact]`，CI 没有 Script.pvf 时会全部跳过。引用「表征测试通过」时须核对：执行 8 个，跳过 2 个，且只有 b1-strict 与 b2-strict。
6. **`CharacterizationHost` 手工复刻了 `Dfo.Server.Host/Program.cs` 的组合**（N-5）。后续 Task 只要改动 `Program.cs`，就要同步核对 `CharacterizationHost`。尤其是 T-002/T-004 如果把执行器或停机顺序接在组合根上，表征宿主必须随之更新，否则会悄悄分叉。
7. (b2) 的故障接缝 `ISelectCharacterQuestEntryDomainService` 可能在 T-008 的选角功能簇中被调整。届时把装饰器换到等价的领域服务上，断言不动。
8. 金样本依赖当前内容和固定时刻。0x0002 subtype 1 的掩码只对 519 字节的帧生效，对应 job 0 加 3 件起始装备。
9. **重录方法**：设置 `DFO_CHARACTERIZATION_RECORD=1` 运行；`DFO_CHARACTERIZATION_RECORD_DIR=<目录>` 可以录到别处，用来做多轮比较。必须在空闲机器上录，否则 (b1)/(b2) 可能录成截断的样本。录完要重新构建，样本才会复制到输出目录。
10. 附录 D 其余跨连接顺序要求的检索、附录 A 的统计、测量，由 T-001 的后续单元承担。
