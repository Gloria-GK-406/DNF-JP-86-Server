---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: 玩法执行改为单执行器、协议级 CMD 与提交后发件箱

## Why and Outcome

今天每条连接各自串行，跨连接并发，串行单位是「一条命令」。可一次请求是「复查 → 命令 → 再读 → 投递」，有时还包含好几条命令；再加上定时器、事件订阅、断线清理从侧面插进来，结果协议层手写了约 105 处取门，管线里叠了 5 道门，还写了 5 份「锁内复查、重试」循环，并留下 #204 那样的死锁陷阱（`research/01`–`04`）。

交付后，进程内只有一条玩法队列，串行单位是一条 CMD。CMD 覆盖一个入站协议的全部处理，或者一次定时触发、一次事件消费、一次连接生命周期事件、一次 GM/HTTP 写操作。CMD 产生的出站消息在它完成后才发出，执行器从不等待网络。玩法路径不再需要锁，「谁先谁后」由队列的结构来保证。

路径相对 `Server/src/`。基线 `a02bce46`。

## Definitions

- **执行器**：进程内唯一的玩法执行单元。先进先出，单消费者；同一时刻只执行一条 CMD，执行到完成才取下一条。
- **CMD**：执行器上的一个执行单位，即一条命令。它不是 Game 出站首字节 `0x01` 的 CMD 类别。分两类：
  - **协议 CMD**：一个入站协议消息类型对应一个；
  - **内部 CMD**：定时触发、事件消费、连接生命周期、GM/HTTP 写操作等非协议来源。
- **入队点**：把 CMD 交给执行器的唯一 API。
- **工作单元**：CMD 内一次 SQLite 事务（今天的 `DomainTransaction`），要么整体提交，要么整体回滚。一条 CMD 可以有零到多个工作单元，按顺序执行。
- **出站消息**：一切发往客户端的消息，包括 `0x01`、`0x00` 与 Channel 端点的消息。
- **发件箱**：CMD 执行期间登记出站消息与关连接请求的地方。CMD 完成后，按登记顺序交给发送端。
- **交付**：发送端把一条出站消息交给传输层（今天 `ISender.SendAsync` 返回即算）。交付不代表客户端已收到。
- **调用链**：每条 CMD 带根 ID、父 ID、深度和来源种类。来源种类有：连接、定时、事件、生命周期、GM、HTTP、启动恢复。
- **玩法状态**：领域服务持有的内存运行态与持久数据，例如在线身份、城镇区域、副本局、塔局、队伍、生物运行时、登录会话、事件投递状态。

## Scope

**In**：执行器与入队点；全部入口改为 CMD；协议 CMD 吸收 handler 的工作；工作单元与发件箱；发送端（队列、顺序、失败策略）；调用链与深度上限；删除玩法路径的锁；判据；测试；测量；文档。

**Out**：按频道分流（只预留分流键）；删除短 `lock(sync)` 与 PVF 缓存锁；持久化模型与表结构；协议字段与编码；#209、#210；推送、PR、远端 issue。

## Requirements

### A. 执行器与入队点

| ID | Requirement |
|---|---|
| RQ-001 | 进程内恰有一个玩法执行器。它先进先出、单消费者，一条 CMD 执行到完成（包括其中对本地 SQLite 的等待）后才开始下一条。 |
| RQ-002 | 入队点按调用方提供的分流键选择执行器。分流键函数今天对任何输入都返回同一个执行器。 |
| RQ-003 | CMD 内不得等待网络发送完成，也不得向执行器入队后等待那条 CMD 完成。违反时视为程序错误：记带调用链的 `Error`，该 CMD 以失败结束。 |
| RQ-004 | 一条 CMD 失败（未处理异常）不影响执行器继续执行后续 CMD。 |
| RQ-005 | **入站背压**：同一连接的下一条入站消息，要等上一条消息的 CMD 完成后才入队。连接内的顺序与今天一致，单个连接在队列里同时至多有一条待执行的入站 CMD。这里的「完成」指执行器执行完这条 CMD（发件箱已交给发送端），不包括交付。 |
| RQ-008 | **等待关系**：执行器从不等待关连接、交付或传输层回调；收包回调与 HTTP 请求所等待的东西里，也不包含关连接与交付。TouchSocket 关闭连接时会等当前收包回调结束，上面这条保证这种等待不会成环。 |
| RQ-009 | **停机顺序**：先停止接受新连接与 HTTP 请求；再把已经入队的 CMD，包括断开引起的生命周期 CMD（例如拆除时落库位置），全部执行完；最后停止执行器。 |
| RQ-006 | **定时源合并**：同一个定时或唤醒源，在它上一次入队的 CMD 完成之前，不会再入队新的 CMD。 |
| RQ-007 | **重计算**：口令哈希这类 CPU 密集计算不在执行器上做。计算在执行器外完成，并发有上限，超出时排队而不是拒绝；结果再作为 CMD，或作为 CMD 的输入进入执行器。 |

### B. 入口

| ID | Requirement |
|---|---|
| RQ-010 | 会改玩法状态、写 SQLite 或产生出站消息的工作，只在执行器上的 CMD 内进行。入口清单见附录 A，每一项都对应一种 CMD 来源。 |
| RQ-011 | 允许留在执行器外的只有附录 B 所列的部分，每一项附安全理由。之后若再加执行器外的部分，必须同步更新附录 B 和判据（RQ-061）。 |
| RQ-012 | **连接生命周期**：连接建立、断开，以及关连接引起的清理，都以生命周期 CMD 执行。传输线程不直接改玩法状态。由 CMD 发起的关连接请求，要等该 CMD 发件箱里此前登记的消息都交付之后，才执行关闭。 |
| RQ-013 | **定时与延时**：后台服务、调度任务、延时计时器（例如卡牌布局与自动翻牌）只负责在到期时入队 CMD，不直接改状态，也不直接发包。调度任务不再用 `Task.Run` 并发执行。 |
| RQ-014 | **事件消费**：Reliable、LiveOnly 订阅与失败重试，都以事件 CMD 执行，语义见 RQ-040～RQ-042。`DirectAsync` 订阅仍在调用方 CMD 内同步执行。 |
| RQ-015 | **GM 与 HTTP**：写操作，以及读取内存玩法状态的读操作，都以 CMD 执行。HTTP 请求在执行器外等待 CMD 完成，再返回结果。 |

### C. 协议 CMD

| ID | Requirement |
|---|---|
| RQ-020 | 每个已注册的入站消息号（`ClientMessageType`）恰好映射到一个协议 CMD 类型；一个 CMD 类型可以服务多个消息号。handler 只做两件事：把解包后的消息与连接身份交给入队点，等待 CMD 完成以实现 RQ-005。handler 不注入领域服务、发送器、门或关连接器。例外：只记日志、不改状态、不写库、不发包的纯遥测 handler，可以留在执行器外（附录 B）；它们只能注入日志。 |
| RQ-025 | **请求级上下文是 CMD 的显式数据**，在执行器上、CMD 开始时建立，不依赖 AsyncLocal 跨入队点传递。它包括：物理连接上下文、捕获的登录权威、效果账本条目、入站元数据、选中会话接收者。dispatcher 今天在入队前做的检查（会话封锁 `IsBlocked`、登录顶替 `IsSuperseded`、`CaptureAuthority`），在 CMD 开始时、在执行器上重做。请求级上下文有三种合法状态，与今天对应：
|  | (1) 非效果消息：没有账本条目，这是合法的； |
|  | (2) 没有选中会话的效果请求：可以回拒绝，但不能结算（今天的 `EnterWithoutSelectedSession`）； |
|  | (3) 有选中会话的效果请求：有账本条目，序号必须前进。 |
|  | 只有当效果结算请求的协议 CMD 缺少它应有的状态 (2) 或 (3) 时，才视为程序错误；不再「没有帧就放行」。今天由管线 45、58、60 号行为承担的非锁职责，按附录 H 逐项迁移。 |
| RQ-021 | 今天 handler、`*Delivery`、`*Coordinator` / `*Coordination` 里的具名判定、领域调用、多段提交、投递、补偿、回滚，都并入对应的 CMD。多个 CMD 共享的逻辑放在领域服务或应用服务里，CMD 不调用另一个 CMD。 |
| RQ-022 | 今天的细粒度命令处理器，并入协议 CMD 或内部 CMD、改成应用服务，或删除，不保留转发层。 |
| RQ-023 | 以下保护原样保留，在 CMD 内的位置等价： |
|  | (a) #185 的具名判定（`IsSelected`、`IsInWorld`、`TryGetInWorld`、`StandingOf`、`ReleaseExact`）； |
|  | (b) 效果账本「一次网络效果请求只有一个主结算」与已提交请求的重放； |
|  | (c) 事务结果不确定时的会话隔离（`TransactionOutcomeUncertainException` → `SelectedSessionSafety.BlockAsync`）。CMD 带有参与者集合时（今天的 `IMultiSelectedSessionCommand.Sessions`），封锁集合中的**每个**会话；提交之后、CMD 之内发生的非传输失败（组帧、运行时认领、评估等），保持今天的处理方式，例如怪物死亡会封锁全部参与者（`MonsterDeathSlice.cs:455-497`）。 |
|  | (d) 登录被顶替的权威检查（`LoginSessionRegistry.IsSuperseded`、`CaptureAuthority`）； |
|  | (e) 领域服务只接收业务槽位（`BusinessSlot`），协议槽位只在协议边界内出现。 |
| RQ-024 | **工作单元**：今天按「一命令一事务」提交的每一段，在 CMD 内仍是独立的工作单元，提交点不合并、不拆分，除非附录 C 明确列出并说明理由。只读命令今天用只读事务的，仍用只读工作单元。 |

### D. 发件箱与发送端

| ID | Requirement |
|---|---|
| RQ-030 | CMD 内的出站消息与关连接请求，只能登记进本 CMD 的发件箱，不能直接调用发送器。 |
| RQ-031 | **组装与刷出规则**： |
|  | (0) **组装点**：人群解析与逐收件人组装（今天 `ProjectAsync` 的 `ComposeAsync`）都在执行器上的 CMD 内完成。在某个工作单元内登记的条目，在该工作单元提交完成的那一刻组装，然后控制权才回到 CMD 代码；不在工作单元内登记的条目，在登记时组装。发送端只接收组装好的帧。这样，组装看到的是已提交的状态，并且不会看到后续阶段的改动； |
|  | (a) CMD 正常完成后，发件箱按登记顺序交给发送端； |
|  | (b) 在某个工作单元内登记的条目，若该工作单元回滚，则丢弃； |
|  | (c) CMD 以未处理异常结束时，发件箱按与 (a)(b) 相同的提交规则处理：已提交工作单元内登记的条目和工作单元外登记的条目，照常按登记顺序刷出；在回滚或结果不确定的工作单元内登记的条目，丢弃；关连接请求一律执行。最后，在这些条目交付之后，关闭来源连接（若有）。这样既保留了今天「handler 抛异常即断线」的语义，也保留了今天「先发出拒绝或清理通告、先关闭其他参与者，再抛出」的路径； |
|  | (d) **组装失败按收件人记录，与今天一致**：为某个收件人组装失败时，只丢弃该收件人的这批条目，记一条 `Warning`，不关闭他的连接，也不让整条 CMD 失败。这对应今天 `NotificationBus` 逐收件人记 Failed 的做法。组装发生在工作单元提交时、控制权回到 CMD 之前（(0)），所以 CMD 能看到逐收件人的组装结果。今天依据 Failed 做业务决定的 CMD 照原样处理，例如祭坛遇到任一 Failed 就转为提交后发布失败，按 RQ-023(c) 封锁全部参与者，入站时来源连接关闭（`AltarProjection.cs:94-108`）。 |
| RQ-032 | **单连接顺序**：同一连接上的交付顺序，等于这些条目在各自 CMD 中登记的先后，CMD 之间则按完成顺序。 |
| RQ-033 | **跨连接顺序**：附录 D 列出的跨连接顺序要求，用「有序条目」表达。一个有序条目交付之前，它前面的有序条目必须都已交付或已判定失败。等待前序条目的时间有上限，沿用 2 秒；超时后，按 RQ-036 判定前序条目的目标连接失败，后续条目继续交付。所以「慢连接不拖累其他连接」只对没有顺序依赖的连接成立；有顺序依赖的一组连接，最多被拖慢一个等待上限。 |
| RQ-034 | **批次与必达**：今天 `GameExplicitMessageDelivery` 的 Required / BestEffort 语义、`SendBatchAsync` 的整批编码与传输身份校验、`CleanupNotificationDelivery` 的「必达失败即不再继续」，在发送端保持。 |
| RQ-035 | **传输身份**：每个条目都带登记时的连接上下文。交付前如果发现连接已被替换，或已经关闭，则丢弃该条目，不交付给新连接。 |
| RQ-036 | **失败策略**：对某个连接交付失败、交付超时（沿用今天每收件人 2 秒），或该连接的发送队列超过上限时，关闭这个连接，丢弃它余下的条目；其他连接不受影响。之后的清理由 RQ-012 的生命周期 CMD 完成。发送队列上限按字节计。它的下限不得小于单条 CMD 对单个连接登记的最大发件箱的 2 倍；这个最大值在实施初期实测（选角是已知的最大者），并由一条判据测试钉住。 |
| RQ-037 | 执行器不等待交付。慢连接只影响它自己的发送队列。 |
| RQ-038 | 今天依赖投递结果或同步发送异常的每一项行为，按附录 E 的对照表承接，保住各自的保护意图。 |

### E. 事件

| ID | Requirement |
|---|---|
| RQ-040 | 事件组件的事实记录、订阅登记、批次与事件 ID、启动扫描遗留 delivery，保持不变。 |
| RQ-041 | Reliable：一条 delivery 的消费是一条事件 CMD。完成标记与业务写入在同一工作单元提交；失败时回滚，另起工作单元记录重试，退避语义不变。 |
| RQ-042 | LiveOnly：保持「先提交消费决定，再投影；离线、旧进程或过期提示时跳过；失败不补播」。投影进入事件 CMD 的发件箱。 |
| RQ-043 | 每个订阅组仍串行，「失败退避期间后续到期事实可先处理」的语义不变。订阅组之间不再并发，因为它们都进同一个执行器。 |

### F. 调用链与深度

| ID | Requirement |
|---|---|
| RQ-050 | 每条 CMD 带调用链：根 ID、父 ID、深度、来源种类，以及来源连接（若有）。由 CMD 引起的 CMD 深度为父深度加 1，根 ID 继承父 CMD，包括：事件 CMD、关连接引起的生命周期 CMD、同一 delivery 的失败重试（继承首次消费时的链）。定时、入站、GM、HTTP、启动恢复的 CMD 是新根，深度为 0。「关连接引起的生命周期 CMD」只指 CMD 登记进发件箱的关连接请求；传输层自发的关闭（交付失败、交付超时、队列超限，RQ-036；客户端断开）引起的生命周期 CMD 也是新根，来源种类为「生命周期」。 |
| RQ-051 | 跨持久化边界的传递：事件 delivery 的调用链只保存在进程内存中，按 delivery 记录，表结构不变。进程重启后，遗留 delivery 的消费作为新根，来源种类为「启动恢复」。 |
| RQ-052 | 深度上限是一个具名常量，取值至少为最长合法链长度的 2 倍，且不小于 8。最长合法链的统计方法：用订阅注册（事件 → 订阅）与各订阅处理中 `EnqueueAsync` 的事件类型（订阅 → 事件）构成事件图，再加上「入站或定时 → 首个事件」与「关连接 → 生命周期」两类边，取最长路径。统计结果作为表格写入附录 A；一条判据测试钉住常量不小于这个长度的 2 倍。 |
| RQ-053 | **超限**：不入队这条 CMD，记一条 `Error` 日志，内容包含完整调用链（根到当前的来源种类与 CMD 类型序列）。此外： |
|  | (a) 根来源是客户端连接的，关闭该连接。这次关闭由深度检查发起，不是由 CMD 登记，所以它引起的生命周期 CMD 是新根（RQ-050），不会因为继承超限的链而被拒绝执行； |
|  | (b) 超限的 CMD 对应一行事件 delivery 的，**必须落定这一行**：Reliable 写为终态（`DomainEventDeliveryState.Terminal`），LiveOnly 以「深度超限」为理由写为完成。这次落定在执行器上的一个工作单元里进行，是一条不继承调用链、豁免深度检查的系统 CMD，它只做落定，不执行订阅处理。这样不会反复读到同一行到期的 delivery； |
|  | (c) 其余情况只记日志。 |
| RQ-054 | CMD 执行的开始、结束、失败日志都带根 ID、父 ID、深度与 CMD 类型。 |

### G. 锁的删除

| ID | Requirement |
|---|---|
| RQ-060 | 附录 F「删除」一栏的门、信号量、管线行为、循环与声明，从生产代码中删除，不留类型、注册或转发。「保留」与「改为执行器内访问」两栏按附录 F 处理。 |
| RQ-061 | 运行期判据：写 SQLite 的工作单元开启、发件箱登记、附录 F 指定的玩法状态写入口，若在执行器之外被调用（附录 B 的启动期例外除外），立即抛出程序错误。 |

### H. 判据

| ID | Requirement |
|---|---|
| RQ-070 | 结构判据（反射测试）： |
|  | (a) handler 的构造参数只能是入队点与日志； |
|  | (b) CMD 处理器的注入面只能是：`Dfo.Server.Abstractions` 中以 `DomainService` / `DomainStore` 结尾的接口、今天豁免清单里的 `TimeProvider` 与 `IPartyService`，以及具名的协议边界能力（发件箱、关连接、协议槽位映射、日志）。具名清单写在判据旁，每项附理由；不得出现入队点、执行器、门、发送器； |
|  | (c) 每个已注册的入站协议消息类型恰有一个协议 CMD。 |
| RQ-071 | 今天会冲突的判据都要重写，并保留各自的保护意图，包括：`CommandHandlerArchitectureTests`、`CommandRegistrationTests`、`CommandBusTests`（嵌套 dispatch）、`EffectSettlementRegistrationTests`、`InventorySlotBoundaryArchitectureTests`、`NotificationHandlerArchitectureTests`、`EventNotificationBoundaryTests`。 |
| RQ-072 | RQ-061 与 RQ-070 的每一条判据，都配一条证明它会拒绝的反例用例。 |

### I. 行为不变的证据

| ID | Requirement |
|---|---|
| RQ-080 | **先钉行为**：在结构改动之前，基于基线为以下工作流补上经真实连接、断言完整帧序的表征测试，并在基线上通过： |
|  | (a) 选角成功全序列； |
|  | (b) 选角某阶段失败、走补偿； |
|  | (c) 组队进本（跟随者先于领队）； |
|  | (d) 离开副本（同伴先于领队 ACK）； |
|  | (e) 组队进塔； |
|  | (f) 离队； |
|  | (g) 断线清理通告。 |
|  | 这些测试断言完整的帧字节与帧序，不只断言顺序，所以组装内容的漂移也会被发现。改造后它们的断言不变，且仍然通过；附录 G 允许的变化除外，按该附录处理。 |
| RQ-081 | 线级集成测试对线上字节、帧序、连接开关的断言不变，附录 G 允许的变化除外，那些按 RQ-082 处理。codec 向量与选角金样本原样通过。这里的「codec 向量」指纯编解码测试；调用通知 handler `ComposeAsync` 的 9 个文件，若通知 handler 形态改变，它们的改动按 RQ-083 逐条说明。只因准备数据或测试接缝被删除而改动的测试文件，逐个列出并说明断言未改。 |
| RQ-082 | 可见行为变化只允许附录 G 所列各项，每项都有测试证据。 |
| RQ-083 | 构建 0 警告 0 错误。全量测试除基线已有失败（`DimensionDungeonTests` 2 条）和 AGENTS.md 已登记的偶发项外全部通过。测试数量的增减与断言变化逐条说明，并写出加法。 |

### J. 测量

| ID | Requirement |
|---|---|
| RQ-090 | 用同一套测量工具，分别在基线 `a02bce46` 和改造后测量，并记录：机器、磁盘类型、构建配置、SQLite 设置（`journal_mode`、`synchronous`）、负载构成、持续时间、样本量。负载构成按以下优先级选取，并写明各类 CMD 的比例，其中必须包括选角、建角、进本这些长 CMD：①用 A21 客户端真实游玩的一段录制（需要用户提供）；②没有真实录制时，用合成负载：以 `Tool/WireSmoke` 的场景脚本为基础，缺少的场景（例如进本）由测量工具按线级集成测试的驱动方式补齐；在测量记录与汇报中标明「合成负载」这一局限。基线上没有执行器，不存在「入队等待」这个量，所以对照口径用端到端延迟：入站帧到达到第一条回包离开。记录时注明两点：多段 CMD 的首条回包按 VC-1 会推迟到 CMD 结束；没有回包的 CMD 不计入这个量。至少测量： |
|  | (a) 各类 CMD 的执行时长 p50 / p95 / p99 / p99.9 / max； |
|  | (b) 该负载下执行器的可持续吞吐（CMD/秒）； |
|  | (c) 按该负载构成、400 CMD/秒时，入队到开始执行的等待时间 p99； |
|  | (d) 10 个并发登录（含口令哈希）对 (c) 的影响； |
|  | (e) 单个连接持续灌包时，对其他连接的 (c) 的影响； |
|  | (f) 冷启动：进程启动后不预热游玩路径，直接施加负载，记录前 60 秒的 (c) 与最长单次停顿。 |
| RQ-091 | **达标标准**（依据：私服规模按 20 人、每人每秒 20 个包，即 400 包/秒，取 5 倍余量；单次停顿以人可察觉的卡顿为界）： |
|  | (b) ≥ 2000 CMD/秒； |
|  | (c)、(d)、(e) 下的 p99 等待 ≤ 50 毫秒； |
|  | 玩法 CMD 的单条执行时长 p99.9 ≤ 100 毫秒、max ≤ 250 毫秒（启动前的工作不计）； |
|  | (f) 的最长单次停顿 ≤ 250 毫秒。 |
|  | 执行器上会触达的全部 `Lazy` 内容源（例如成就目录、称号目录）进入启动预热清单，并列入附录 B。不达标时停止实施，回到用户，并给出与基线的对照。 |
|  | **用户修订（2026-10-22，执行中）**：用户的原话是「不需要太追求基线……以完整重构为主目标，性能能接受就行。后续我们再专门优化就好了」，以及「这次重构阈值可以高一点，就以2s为基线。只要指令执行时间不超过2s就都能算接受」。本次重构的达标判定据此改为两条：①单条 CMD 的执行时长（max）≤ 2 秒，即为接受；②上面 (b)、(c)、(d)、(e)、(f) 和单条 p99.9 / max 的原阈值，改作记录与对照用的目标值，不再阻断实施。只有在出现数量级问题时才停止实施并回到用户，例如单条 CMD 超过 2 秒，或吞吐、等待比目标差了几倍。测试套件的耗时与内存不作达标依据。没有达到原目标值的项，在测量记录中逐项列为后续性能优化事项。 |

### K. 文档

| ID | Requirement |
|---|---|
| RQ-100 | 更新 `AGENTS.md` 的执行模型相关内容：架构总览与请求走向、命令执行总线（含管线实际层数）、handler 业务语义规则、SelectCharacter、领域事件与即时投影、构建与测试中关于并发假红的段落、`PersistOnTeardownAsync` 等已过时的名称。同时更新 `Docs/Agent/ArchitecturePhilosophy.md` §1 与 §5。文档里不再出现已删除的门与管线行为（作为被推翻做法的记录除外）。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001、RQ-002、RQ-004 | 单元测试：多个生产者并发入队的 N 条 CMD，执行顺序等于入队顺序，任意两条 CMD 的执行区间不重叠；一条抛异常后，后续 CMD 照常执行；分流键函数对任意输入都返回同一个执行器。 |
| AC-002 | RQ-003 | Given 一条 CMD 等待网络发送，或在 CMD 内向执行器入队后等待，When 执行，Then 抛出程序错误并记录调用链（反例用例）。 |
| AC-003 | RQ-005 | 线级测试： |
|  |  | - 同一连接连续发两个包，第二个包的 CMD 在第一个完成后才开始执行； |
|  |  | - 两个连接交替发包，各自的连接内顺序不变； |
|  |  | - 一个连接持续灌包时，队列里属于它的待执行入站 CMD 始终不超过 1 条，另一个连接的入站 CMD 照常执行。 |
| AC-004 | RQ-006 | Given 一个定时源上一次的 CMD 仍在队列中，When 再次到期，Then 不入队新的 CMD；上一条完成后才恢复入队。 |
| AC-005 | RQ-007、RQ-090(d)、RQ-091 | 口令哈希在执行器外执行（判据或测试断言不在执行器上），并发有上限；测量记录满足 RQ-091。 |
| AC-006 | RQ-010、RQ-011、RQ-013～RQ-015 | 附录 A 每一项都有「来源 → CMD 类型 → 测试或检索证据」的对账行；附录 B 每一项都有安全理由；代码中检索不到在执行器外改状态、写库或发包的路径（RQ-061 判据通过）。 |
| AC-007 | RQ-012、RQ-008、RQ-009 | 断线、关连接、被顶替三种情形下，清理都作为生命周期 CMD 执行。由 CMD 发起的关连接，发生在该 CMD 此前登记的消息交付之后（线级测试断言帧在断开之前到达）。CMD 关闭自己的来源连接（例如登录被拒）不会自锁（线级测试）。停机测试：在线玩家的拆除落库在执行器停止之前完成，位置不丢失。 |
| AC-008 | RQ-020～RQ-022、RQ-070 | 反射判据：从全部已注册的入站消息号到协议 CMD 类型的映射是全函数，附录 B 列出的纯遥测 handler 除外，handler 与 CMD 处理器的注入面合规，判据都有反例用例；生产代码中没有 CMD 调用另一个 CMD。 |
| AC-009 | RQ-023、RQ-025 | 具名判定、效果账本、会话隔离、登录顶替、槽位边界的既有测试，在新结构下断言不变并通过；改写的测试逐条说明。`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 的断言不变。经真实入站路径的反例用例：重发一个已提交的效果请求，得到重放而不是断线；效果结算请求的协议 CMD 缺少 RQ-025 的状态 (2) 或 (3) 时抛出程序错误，非效果消息没有账本条目时照常执行。附录 H 逐项对账。 |
| AC-010 | RQ-024 | 附录 C 对照表：今天的每个事务边界都对应新的工作单元，提交点的合并或拆分各自附理由；持久化相关测试通过。 |
| AC-011 | RQ-030、RQ-031 | 单元测试： |
|  |  | - 一条 CMD 在两个工作单元里各登记一条消息，第二个工作单元回滚，只刷出第一条； |
|  |  | - CMD 先在已提交的单元内、再在单元外登记消息，然后以未处理异常结束：这些消息按序刷出，在它们之后来源连接被关闭；回滚单元内的消息不发出； |
|  |  | - 某个收件人组装失败：只丢弃他的条目、记 `Warning`，他的连接不被关闭，其他收件人照常收到，CMD 本身成功； |
|  |  | - 祭坛提交后，某个收件人组装失败：按 RQ-023(c) 封锁全部参与者，入站时来源连接关闭（今天的行为，补一条之前没有的测试）； |
|  |  | - 工作单元提交之后，组装读到的是已提交的状态，看不到 CMD 后续阶段的改动； |
|  |  | - 判据阻止 CMD 直接调用发送器。 |
|  |  | 线级回归： |
|  |  | - 怪物死亡在提交后失败时，每个参与者既被封锁，连接也被关闭（在点名测试之外补充「关闭」的断言）； |
|  |  | - 选角准入被拒时，`01/0004` 与 `00/01F7` 两条拒绝消息按序送达，之后才断开； |
|  |  | - 选角发布后失败走补偿时，同伴收到清理通告。 |
| AC-012 | RQ-032、RQ-033 | 单连接顺序测试；附录 D 每一项跨连接顺序都有测试，并在前序连接人为变慢（但未超过等待上限）时仍然成立；前序连接超过等待上限时，它被关闭，后续条目照常交付。 |
| AC-013 | RQ-034～RQ-037 | 发送端测试：Required 失败中止该目标、BestEffort 按目标隔离、整批编码与身份校验、连接被替换时丢弃、超时或队列超限时关闭该连接；一个被卡住的连接不影响其他连接的交付与执行器吞吐；判据测试钉住发送队列上限不小于实测最大单 CMD 发件箱的 2 倍，选角全序列不会触发超限。 |
| AC-014 | RQ-038 | 附录 E 每一行都有对应测试，证明保护意图仍然成立。 |
| AC-015 | RQ-040～RQ-043 | 事件测试：Reliable 完成标记与业务写入同提交、失败回滚后记录重试、LiveOnly 跳过与不补播、启动扫描遗留 delivery、订阅组内顺序与失败退避后的继续处理。 |
| AC-016 | RQ-050～RQ-054 | Given 一条事件链超过深度上限，Then 不入队、`Error` 日志包含完整链、根连接被关闭；超限的 Reliable delivery 状态为 Terminal、LiveOnly delivery 已完成，二者都不会再被分发循环取出（测试断言分发循环之后进入等待，而不是空转）；无连接链只记日志；失败重试继承原链；正常 CMD 日志带根、父、深度；判据测试钉住深度常量与附录 A 的最长链。 |
| AC-017 | RQ-060、RQ-061 | 附录 F 逐项对账：「删除」栏在 `Server/src` 中检索为零；运行期判据在执行器外调用时抛出（反例用例）。 |
| AC-018 | RQ-071、RQ-072 | 被重写的判据逐条说明「旧断言 → 新断言 → 保护意图」，每条都有反例用例。 |
| AC-019 | RQ-080 | 表征测试在基线提交上通过（记录运行日志），改造后断言不变并通过；附录 G 允许的差异逐条对应。 |
| AC-020 | RQ-081～RQ-083 | 构建与全量测试输出（detailed logger 落盘）；测试数量加法；改动的线级测试文件清单与说明。 |
| AC-021 | RQ-090、RQ-091 | 测量记录包含口径与数字，满足达标标准。 |
| AC-022 | RQ-100 | 文档差异，检索已删除的类型名只出现在「被推翻的做法」一类的记录中。 |

## Appendix A：入口与 CMD 来源

依据 `research/01-entry-points.md`。实施时逐项对账（AC-006）。

| 来源 | 今天的位置 | 新的 CMD |
|---|---|---|
| Game 与 Channel 入站协议 | `NetworkPlugins.cs` 中 Game 与 Channel 插件的 `OnTcpReceived`（`:62`、`:144`）→ `NetworkMessageDispatcher` → 173 条消息注册（173 个消息类型，141 个 handler 类型；基准 `c3115f9a`，计数方法见 `verification/entry-reconciliation.md` §2），其中附录 B 的 14 个遥测消息类型（2 个 handler 类型）留在执行器外，其余 159 条（139 个 handler 类型）在执行器上 | 每个消息类型一个协议 CMD（`inbound:<ClientMessageType>`） |
| 效果账本重放 | `EffectOperationLedger.DispatchAsync`（入站 CMD 内的效果结算调用它） | 随所属协议 CMD 执行 |
| 连接建立、断开、关连接后的清理、孤儿清理 | `OnlineUserConnectionObserver`、`TouchSocketNetworkConnectionCloser`，经 `NetworkConnectionLifecycleDispatcher` 入队；孤儿清理与传输排空（`TouchSocketTransportDrain`）的兜底拆除归断开 | 生命周期 CMD（`lifecycle:connected`、`lifecycle:disconnected`） |
| 登录会话的连接观察与维护扫描 | `LoginSessionRuntime.cs` 的 `LoginSessionConnectionObserver`（`:11`）与 `LoginSessionMaintenanceService`（`:28`） | 生命周期 CMD（连接观察随 `lifecycle:disconnected`）与定时 CMD（`timer:LoginSessionMaintenanceService`）；顶替后的关闭由顶替发生的那条 CMD 登记进它的发件箱（T-006，F-65），被顶替连接的清理随它的 `lifecycle:disconnected` |
| 22 个后台服务（其中 `ScheduledTaskHostedService` 承载 5 个调度任务） | `research/01` §3；归属表见 `tasks/TASK-11.md` §1。`research/01` 的 33 个托管服务：本行 22，第 4 行 1，第 8 行 1，第 11 行与附录 B「开始监听前的启动期工作」4，附录 B 中不碰玩法状态的 5（`HttpApiStartupService`、3 个 `*RegistryValidationService`、UDP 发现），22 ＋ 1 ＋ 1 ＋ 4 ＋ 5 ＝ 33。基准 `c3115f9a` 上托管服务注册共 34 个，多出的 1 个是执行器自身的 `GameplayExecutorLifetime` | 各自对应定时 CMD（`timer:<类型名>`；调度任务为 `timer:ScheduledTaskHostedService/<任务 ID>`） |
| 延时计时器（卡牌布局、自动翻牌） | `DungeonCardPresentationCoordinator` 的 `ScheduleAfterSettlement`（布局）与 `OpenLayoutAsync` 末尾（自动翻牌） | 到期时入队的定时 CMD（`timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip`）；协调器排空之后到达的调度请求不启动计时器（TASK-16） |
| 两塔离场队列 | `ConcurrentQueue` ＋ `BossTowerReturnSweepService`、`IllusionTowerReturnSweepService` | 定时 CMD（`timer:BossTowerReturnSweepService`、`timer:IllusionTowerReturnSweepService`），在 tick 作业里消费队列。不采用「离场时直接入队」：写队列的 sink 在拆除 CMD 里被调用，从作业里入队并等待就是自我等待（TASK-11） |
| 52 个事件订阅（Reliable 17、LiveOnly 22、Direct 13；TASK-16 在基准 `c3115f9a` 复核） | `DomainEventDispatcher` | Reliable 与 LiveOnly 为事件 CMD（`event:<订阅 ID>`）；Direct 在调用方 CMD 内 |
| GM 路由 51 条：40 条为 GM CMD（含 Cera 提醒、读取内存玩法状态的 GM 查询与 GM 登录），11 条豁免列在附录 B。原文「GM 写操作 34 条」按代码数不出这个口径，改按路由计 | `GmWeb/*Endpoints.cs`；路由组挂 `HttpGameplay.RunsOnGameplay` | GM CMD（`gm:<METHOD> <路由模板>`） |
| 启动器路由 3 条：注册与登录 2 条为 HTTP CMD，`bootstrap` 1 条豁免（附录 B） | `HttpApiApplicationExtensions.cs` | HTTP CMD（`http:POST /api/launcher/v1/register`、`http:POST /api/launcher/v1/login`；口令哈希按 RQ-007 在外部完成） |
| 启动恢复（Joust 静默恢复、遗留 delivery 扫描）与开始监听前的启动作业（`EpicBook` 迁移、经验存储策略安装、GM bootstrap） | `JoustMaintenanceService`、`DomainEventDispatcher`、`GameServerStartupService`、`GmBootstrapService` | 启动恢复 CMD（`startup:JoustMaintenanceService`、`startup:event/<订阅 ID>`、`startup:GameServerStartupService/epic-book`、`startup:GameServerStartupService/experience-storage`、`startup:GmBootstrapService`） |

逐行对账（来源 → CMD 类型 → 经真实入口驱动的测试 → 探针族终值 → 留给后续的部分）见 `verification/entry-reconciliation.md` §2（TASK-16，基准 `c3115f9a`）。

最长合法链按 RQ-052 的方法在实施初期统计，表格补在这里，用于确定深度常量。

**统计结果**（TASK-02；方法、52 个订阅的逐项证据与加法见 `verification/event-chain.md`）：

- 类型图上有一个环：成就完成 → 称号解锁 → 称号簿物品写入 → 物品成就 → 成就完成。数据层面，物品成就的容器过滤切断了这个环，但没有测试守着这一点。所以计长度时取类型图上的最长**简单路径**，不使用数据层面的切断。
- 「关连接 → 生命周期」这条边的口径写在 RQ-050 里：只有 CMD 登记的关连接请求，才继承登记它的那条 CMD 的链；传输层自发的关闭是新根。这样一来，「交付失败 → 关同伴 → 同伴的清理通告再失败」这种级联，就不会让链越过深度上限，进而误关根连接。

| 跳 | CMD | 来源种类 | 位置 |
|---|---|---|---|
| 1 | 入站怪物死亡 → MonsterDefeated | 入站 | `MonsterKillRewardsDomainService.cs:44` |
| 2 | achievement.monster → AchievementCompleted | 事件 | `AchievementDomainService.cs:204` |
| 3 | title.unlock → 称号簿写入 → InventoryContentsChanged | 事件 | `TitleBookDomainService.cs:123` |
| 4 | achievement.inventory（不考虑过滤）→ AchievementProgressChanged | 事件 | `AchievementDomainService.cs:171` |
| 5 | achievement.noti | 事件 | `ProtocolModule.cs:661` |
| 6 | CMD 登记的关连接 → 生命周期 CMD：两塔断线离开，剩余成员全灭 → 结算升级（CharacterLevelChanged）并放入物品 | 生命周期 | `SelectCharacterLifecycleServices.cs:149-163`；`BossTowerDomainService.cs:349-351`；`BossTowerRewardDomainService.cs:127` |
| 7 | achievement.level → AchievementCompleted | 事件 | `AchievementRegistration.cs:39` |
| 8 | title.unlock → InventoryContentsChanged | 事件 | `AchievementRegistration.cs:40` |
| 9 | achievement.inventory（不考虑过滤） | 事件 | `AchievementRegistration.cs:36` |
| 10 | achievement.noti | 事件 | `ProtocolModule.cs:661` |

生命周期 CMD 能发出的事件种类和入站 CMD 一样，所以以它为起点的尾巴最长也是 5 跳（第 6～10 跳）。L ＝ 5 ＋ 5 ＝ 10，深度常量下限为 max(2 × 10, 8) ＝ 20。常量取 **32**，判据钉住「常量 ≥ 2 × 10」。这样，日后增加订阅时有余量；同时，真正的无限链在 32 层内仍会被截断。

## Appendix B：留在执行器外的部分

| 部分 | 为什么安全 |
|---|---|
| 解包与帧适配、入站帧计数（`GameInboundFrameCounter`） | 只处理字节，不碰玩法状态 |
| 发送端：编码、发送队列、交付、超时 | 只读取发件箱交来的不可变消息；关连接引起的清理由生命周期 CMD 执行 |
| 传输层的连接注册（`TouchSocketConnectionRegistry`） | 传输层自身的状态。**补齐（TASK-16，基准 `c3115f9a`）**：停机时的 `TouchSocketTransportDrain`（对仍登记的连接请求关闭，从注册表 `TryRemove` 仲裁之后分发 `lifecycle:disconnected` 并等待；拆除本身是生命周期 CMD，排空只读写传输注册表）；`NetworkPlugins` 四个回调外层的 `GameplayJob.RunDetachedAsync` 流边界（判据 `DetachmentSiteTests`）；TouchSocket 自己的 TCP 服务宿主 |
| UDP 发现（`A21UdpDiscovery`） | 只回 UDP 包，不碰玩法状态 |
| PVF 内容源与缓存、规则服务的不可变规则 | 只读，或延迟加载的不可变内容，已有锁保护。**清单（TASK-14，基准 `2d8cec4d`）**：GM 路由 `GET /api/gm/v1/server-info`（配置与档案摘要）、`GET /api/gm/v1/items/{itemId:int}/tooltip`（物品说明）；与本表其余 GM／HTTP 路由一样以路由元数据 `OutsideGameplay(理由)` 标注，豁免集合由 `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption` 钉住，加一条豁免必须同时改这个用例与本表（RQ-011） |
| GM 自身的内存状态（`GmLoginLimiter`、`GmIconPackStore`） | 不属于玩法状态。**清单（TASK-14，基准 `2d8cec4d`）**：`GmLoginLimiter`（GM 登录在口令校验之前计数，只计数、不是等待原语）；`GmIconPackStore` 及其两条路由 `GET /api/gm/v1/items/icon-pack`、`GET /api/gm/v1/items/{itemId:int}/icon` |
| 只读 SQLite 的查询（不读内存玩法状态，例如 GM 邮件物品查询、身份识别） | WAL 下读取快照，不写库。**清单（TASK-14，基准 `2d8cec4d`）**：`GmAccessBoundary` 的身份识别；`GET /api/gm/v1/auth/status`；`GET /api/gm/v1/mail/items`、`GET /api/gm/v1/mail/items/{itemId:int}`；启动器登录第一段的账号查询（`LauncherCredentialReader`）；GM 登录第一段的管理员查询（`GmCredentialReader`）；`MarriageRelationAudit` 的启动期计数；**补齐（TASK-16）**：事件分发 `DomainEventDispatcher.DrainOnceAsync` 在执行器外读取到期 delivery 的 `ReadDueAsync`（只读，读到的条目作为事件 CMD 的输入，消费与完成标记在 CMD 内，TASK-12 裁定第 3 条），以及组循环 `RunGroupAsync` 在两次排空之间读取下一个到期时间的 `NextDueAsync`（只读，TASK-16 第 2 轮补）。第一段读到的记录只是校验的依据，第二段的 CMD 在写意向下重读并比对，记录变了就不签发（启动器重做第一段一次，GM 直接拒绝并审计）。**经总线的只读查询不在本行**：GM 的查询命令一律作为 GM CMD 进执行器（TASK-14 §1.1） |
| 口令哈希等纯计算 | 没有共享状态（RQ-007）。**清单（TASK-14，基准 `2d8cec4d`）**：`PasswordComputationLimiter` 是注册哈希、启动器登录校验与哑校验、旧 MD5 迁移与重哈希、GM 登录校验与哑校验、GM bootstrap 哈希的唯一入口；上限等于处理器数，超出时按到达顺序排队、不拒绝；在执行器上调用即以 `GameplaySelfWaitException` 失败并标为程序错误。结果作为 CMD 的输入（`PreparedRegistration`、`VerifiedLogin`、`GmCredentialCheck`、bootstrap 口令记录）；领域服务与命令处理器不注入哈希器与限流器。判据：`PasswordComputationLimiterTests`（H2～H5）、`CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` |
| 纯遥测 handler（只记日志，例如隐藏角色信息报告与未应答类报告，即 `HiddenCharacterInfoHandler`、`UnansweredReportHandler` 一类） | 不改状态、不写库、不发包；仍在收包回调里按连接顺序执行（RQ-020 例外）。实施时逐个列出。**清单（TASK-09，基准 `690b9926`）**：2 个 handler、14 个消息类型（1 ＋ 13 ＝ 14）——`HiddenCharacterInfoHandler`：`GameHiddenCharacterInfo`；`UnansweredReportHandler`：`GameUseSkill`、`GameSyncItemSpace`、`GameOverflowInfo`、`GameLogDomainConnect`、`GameLogConnectProcess`、`GameCharacterStatistic`、`GameRequestBlacklist`、`GameFarmEventAction`、`GamePi`、`GameRequestIgaVersion`、`GameFpsPerformanceStatistic`、`GameFrameLagPerDungeon`、`GameLagStatistics`。安全理由由启动检查强制：经 `RegisterTelemetryMessageHandler` 注册的 handler，构造器参数只能是 `ILogger`／`ILogger<T>`，否则 `MessageHandlerRegistry` 构造即抛。执行器外照旧先做只读的 `IsBlocked`、`IsSuperseded` 检查（主控方裁定第 1 条）。**注**：名字像遥测、但构造器还注入了别的，所以进执行器、不在本清单：`ClientSpecStatisticHandler`、`ClientSpecProfileHandler`、`ClientHardwareDetailHandler`、`SessionMetricsHandler`、`MapEntryReportHandler`（注入 `IOnlineUserQueryDomainService`）；`CreatureResponseReportHandler`、`CreatureScriptMessageReportHandler`（注入 `ISelectedSessionDomainService`）；`SecurityStatusHandler`、`ContentsPlayInfoHandler`、`MercenaryInfoHandler`、`GenCeraTicketHandler`（注入发送器）。第一列原先举例的「客户端规格、会话指标」按此判据不属于本行，TASK-16 已把举例改为清单内的类型 |
| 启动预热的 `Lazy` 内容源（含成就目录、称号目录） | 在开始监听前加载完毕，执行器上不会触发首次加载（RQ-091）。实施时逐个列出。**清单（TASK-15，基准 `bfbcf012`，第 3 轮）**。**机制**：参与者实现 `Dfo.Server.Abstractions.Content.IContentWarmup`（`Name`，`WarmAsync` 返回条目数），以 `AddContentWarmup<T>()` 注册为同一个单例。`GameServerStartupService.StartingAsync` 在 `archive is not null` 分支末尾、两个启动作业之后调用 `ContentWarmup.RunAsync`，在执行器外逐项执行，不入队、不取门、不开事务。某项失败时记 Warning 后继续（`Lazy` 缓存该异常，首次索引下次请求重试，请求方看到的结果与今天一致）；取消照常抛出。服务被替身占位时记 Warning「Content warmup skipped」。汇总行写作「Content warmup finished: N items, F failed」，N 包含失败项与被跳过的项。**开关** `ContentWarmup:Enabled` 缺省为 true，生产不设这个键。它只控制本单元的统一预热；已有各项与启动作业不受影响。开关关闭时记一条「Content warmup skipped by configuration」，执行器上的首次加载与 TASK-15 之前相同。测试中，只有进程内的 `GmTestHost`、`ComponentLifecycleTests` 设为 false；S1（W1）、全部表征测试与停机 S2（`CharacterizationHost`），以及拉起真实打包进程的用例，都保持生产默认。**已有项**（TASK-14 之前就有，原样保留）：装备索引、领主塔、独立掉落、额外掉落、怪物声明掉落、世界掉落、次元副本、角色基础属性、自动售货机、彩票（同时建好礼包的 stackable 路径表）、经验刻度；`epic-book` 启动作业只加载史诗书目录。**本单元 71 个参与者**，名单的唯一出处是 `ContentWarmupTests.Participants`，这里不逐个重抄。- 31 个来自 §2.3 与探针第 1 轮：成就、称号簿、技能（`CharacterSkillRuleWarmup`，覆盖 `SupportedCareers` 的全部职业）、金币上限、会员服务、任务助手人偶、经验规则、掉落规则（全部 `DropItemFamily`）、金银号角、地狱奖励、守望之塔、婚姻 4 项、祭坛、Joust、比武、初始装备与技能、物品落位、绝望之塔、城镇、每日活动、收集箱、任务目录、副本／地图／房间两类路径索引、史诗书配方。- 40 个是第 3 轮按静态可达性补入的整索引（判定表见 TASK-15 Result）：宠物 3 项、循环副本、镜像副本、幻影塔、地狱副本、任务参数、教程奖励、背包扩展、物品目录路径表、掉落率与金币表、结算掉率表、时装合成、仓库物品路径、商城（含能力表）、分解、附魔、魔法封印及其等级、品级、强化增幅、修理、穿戴等级、重封、锻造、异界、装备槽位、经验道具、排序键、出售、镶嵌、道具用途与使用规则、邮件物品、NPC 商店、配方、可堆叠动作、传送道具、幸运星。**证据**：W1 是 `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs` 中的 `ContentWarmupTests.AssertWarmedAsync`，依次检查：参与者名集合与 `ContentWarmupTests.Participants` 相等；宿主日志恰有一条「Content warmup finished: 71 items, 0 failed」，并且没有 failed 或 skipped 行；在调用任何 `WarmAsync` 之前，持有者的 `Lazy`／`??=` 字段都已加载，按职业、按族的键覆盖全部键；最后每项再调用一次，返回大于 0。变异「删掉 `StartingAsync` 中的预热调用」会让 W1 变红。W2 是 `ContentWarmupTests` 的结构判据及反例。探针第 2 轮见 `verification/logs/task-15-firstload-r2.txt`，清单内项在执行器上为 0。**不预热**：(1) C 类按键缓存保持懒加载，首次加载发生在执行器上，交 M-1（单键 p99／max；单个 CMD 内首次加载的最大读次数）：副本定义 0.48／10.7 ms（GameSelectDungeon 内 1 次），地图 0.03／2.4 ms（GameSelectDungeon 内最多 52 次读），房间角色 0.05／7.1 ms（1 次），房间掉落（没测到，`verification/measurement/task-15-coldstart.md`；GameSelectDungeon 内最多 6 次读），物品落位 0.03／33.2 ms（GameSelectCharacter 内 21 次读），物品目录 0.01／21.3 ms；另外，各内容源按物品、按 ID 的单键缓存，以及物品目录供 GM 搜索的全量数组 `searchable`（与物品目录同一族），都归此类。(2) D 类不缓存，耗时算在各自的 CMD 里，交 M-1：周签到（几乎每条玩法 CMD 都读，单个作业最多 74 次读，**M-1 性能关注点**）、超额技能契约、每日补给规则。(3) 豁免 4 项：`PvfItemTooltipSource` 只被 GM tooltip 路由读取，那条路由在执行器外（本表「PVF 内容源与缓存」行）；`PvfArchiveProvider` 是档案本身；`InventoryEffectPreflightDomainService+Standing` 是单次调用内的值；`PvfEntryLifecycleContentSource.slotMappings` 是不读档案的规则对象。**约束**：新增 `Lazy` 或 `??=` 首次索引的持有者，必须同时改 `ContentWarmupTests.Holders`（作为参与者或豁免）和本行，由 W2 守着（RQ-011 式）。**W2 的识别边界**：它认得 `Lazy<>`、`ConcurrentDictionary<,Lazy<>>` 实例字段，以及同文件类型上的 `??=` 字段；认不出 `if (x is null)`、手写字典、`is {}` 这几种写法的首次索引。这几种写法由人工检索和参与者名单覆盖，新增时须人工核对是否可达。**TASK-12 Result 第 5 条**（事件作业可能早于预热运行）关闭：预热在 `StartingAsync` 中完成，先于任何 `StartAsync`，因此也先于事件组循环。探针第 2 轮里 `event:` 类型下已经没有成就或称号目录的加载。**留给 M-1**：首次选角 101.7／104.3 ms 中约 100 ms 不是内容加载（推断为 JIT 与 SQLite 冷页；首次选角里 D 类读档那几条代码路径的首次 JIT 也算在内）；C 类与 D 类的耗时；RQ-091 的达标判定。合成负载覆盖不到的 CMD（装备强化、NPC 商店、邮件等）走到上述 C 类、D 类时，首次加载或读档仍发生在执行器上，M-1 的负载同样覆盖不到。整索引已按静态可达性全部纳入预热，不在此列。 |
| 开始监听前的启动期工作：建表与迁移、`EpicBook` 迁移、经验存储策略安装、GM bootstrap | 发生在开始接受连接与 HTTP 之前，此时执行器还没有其他工作；作为 RQ-061 的启动期例外。**清单（TASK-14，基准 `2d8cec4d`）**：`EpicBook` 迁移、经验存储策略安装、GM bootstrap 已作为执行器上的启动作业运行，来源 `StartupRecovery`，CMD 类型分别为 `startup:GameServerStartupService/epic-book`、`startup:GameServerStartupService/experience-storage`、`startup:GmBootstrapService`（判据 `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`）；bootstrap 的口令哈希在作业之前、执行器外完成。仍在执行器外的：建表与迁移（Persistence 单例构造时直连 SQLite，不经工作单元入口）、频道脚本加载（`ChannelScriptStartupService`）、`GameServerStartupService.StartingAsync` 中的 PVF 内容初始化、`MarriageRelationAudit` 的只读计数（见「只读 SQLite 的查询」行）。**补齐（TASK-16，基准 `c3115f9a`）**：`HttpApiStartupService`（只设置 HTTP 就绪标志）、`CommandHandlerRegistryValidationService`、`MessageHandlerRegistryValidationService`、`NotificationHandlerRegistryValidationService`（启动时检查注册表的闭合性）——不读写玩法状态、不写库、不发包。TASK-18（基准 `6a3bff70`）：不需要。RQ-061 的判据只挂在工作单元入口与附录 F 的写入口上，仍在执行器外的建表与迁移不经这两类入口。 |
| 不碰玩法状态的 HTTP 路由（TASK-14 按 RQ-011 新增）：GM `GET /api/gm/v1/health`、`GET /gm/{**path}`、`GET /api/gm/v1/auth/csrf`、`GET /api/gm/v1/auth/session`，启动器 `GET /api/launcher/v1/bootstrap` | 只返回常量、静态文件、防伪令牌、GM 边界已识别并放进请求的身份或就绪标志，不读写玩法状态、不写库、不发包；以路由元数据 `OutsideGameplay(理由)` 标注，豁免集合由 `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption` 钉住。**补齐（TASK-16）**：GM 路由组过滤器（`HttpGameplay.RunsOnGameplay`）在入队之前、执行器外把 JSON 请求体读入内存（上限 32 KiB）；它只读 HTTP 请求本身，读体异常原样交给 CMD 内的委托（判据 `GmGameplayRouteTests.A_slowly_sent_body_is_read_off_the_executor_before_its_cmd_is_queued`、`GmGameplayRouteTests.Refused_bodies_keep_their_status_and_message`） |
| 生产者的入队侧（TASK-16 按 RQ-011 新增）：`NetworkMessageDispatcher` 与 `NetworkConnectionLifecycleDispatcher` 的入队并等待；各定时源的计时循环、`CoalescingGameplaySource` 与 `GameplayTimerWork`，`ScheduledTaskHostedService` 的计时循环与并发槽（T-005 删除）；卡牌协调器在延时上的等待与 `DrainAsync`；`DomainEventDispatcher` 的组循环与组门（T-005 删除）；`HttpGameplay.RunAsync` 与两个凭据 reader 返回之后在请求线程上的等待及 `IResult` 的写出；`GameServerStartupService`、`GmBootstrapService` 对启动作业的等待；`GameplayExecutorLifetime` 依次排空生产者再停止执行器 | 只组织作业、入队、等待作业完成，或写出作业已经算好的不可变结果；不读写玩法状态、不取玩法门、不开工作单元。玩法部分整个在作业里（拆分原则第 2 条）。等待时不持玩法门（会话门、账号门、在线用户门、准入门、投影门），所以「执行器外一边持门一边等已迁入的入口」不成立（拆分原则第 3 条，各单元 Required check 8 逐个列过）；例外是事件组门与调度任务并发槽，它们在持有期间等待作业，但作业从不获取它们，不成环，二者在 T-005 删除（门断言若覆盖它们会在执行器外报错，见 `verification/t004c-entry.md` 第 1 项）。依据：探针 P1／P2／P3 在执行器外对玩法状态的访问，3.1～3.9 族只剩 TASK-16 允许的残留（`verification/entry-reconciliation.md` §1）；生产代码中持有 `IGameplayQueue` 的位置以基准 `c3115f9a` 的 37 行检索为准，新增持有者要同时改本行与该检索；`GameplaySelfWait` 在正式全量中为 0 |

## Appendix C：工作单元对照（实施时填写）

实施时为每个改造的 CMD 列出：今天的命令与事务边界（Transactional / ReadOnly / Direct / Workflow），新 CMD 中对应的工作单元，以及合并或拆分的理由。默认一对一。

## Appendix D：跨连接顺序要求

| ID | 要求 | 今天的位置 |
|---|---|---|
| XO-1 | 组队进本：跟随者的进入消息先于请求者 | `DungeonEntryProjectionDelivery.cs:353-376` |
| XO-2 | 离开副本：同伴看到决定先于领队的 ACK | `LeaveDungeonSlice.cs:283-309` |
| XO-3 | 领主之塔进入：各成员批次先于请求者 | `BossTowerProjectionDelivery.cs:175` |
| XO-4 | 幻影塔进入：各成员批次先于请求者 | `IllusionTowerProjectionDelivery.cs:171` |
| XO-5 | 清理通告：所有者 → 城镇同伴 → 队伍投影（与 DR-10 互引） | `SupplementalSynchronizationMessages.cs:222-276` |
| XO-6 | 队伍状态按阶段跨收件人推进，每一阶段全员交付后才进入下一阶段 | `PartyNotificationSlice.cs:798-842` |
| XO-7 | 婚礼：先向双方广播两人位置，再让任何一端开始过场 | `MarriageWeddingSlice.cs:220-222` |

检索方法、命中清单、加法，以及今天各项由哪些测试守着，见 `verification/cross-connection-order.md` 与 `verification/xo-dispositions.md`。XO-6、XO-7 的源码没有说清楚为什么需要跨连接等待，按「行为不变」的原则保守列入；实施时只有拿到证据才能移出。

**改造后允许乱序的写法**：「先发给操作者本人、再发给其他人」（约 5 处，见 `xo-dispositions.md` 第 1 节）；城镇到达时「先向同伴发到达、再向本人发同伴」（`TownAreaProjectionDelivery.cs:204-210`、`:575-588`）；请求者 Required 先发、其余 BestEffort 后发（`DeliverExplicitAsync` 的实现顺序）。这三种写法都没有注释或测试要求先后，所以不列入本表。T-006 可以不保持它们的跨连接先后，单连接顺序仍按 RQ-032 保持。

**口径**：顺序指「交付」的先后，即交给传输层的先后（Definitions）。TASK-01 实测，在基线上，两个连接在客户端一侧的到达先后并不确定（`verification/logs/xo-arrival-measurement.txt`），所以 AC-012 的测试在发送端这一层断言交付顺序，不在线上的到达层断言。

## Appendix E：依赖投递结果或同步发送异常的行为

| ID | 今天 | 保护意图 | 新的承接方式 |
|---|---|---|---|
| DR-1 | 选角任一发送抛出 → 派发补偿命令，释放门后关连接 | 失败的会话不留在半入场状态 | 选角 CMD 内的命令失败照旧走补偿。发送失败发生在 CMD 之后：按 RQ-036 关连接，再由生命周期 CMD 拆除会话 |
| DR-2 | 进本部分 cohort 发送失败 → 隔离已送达者与当前目标，`RollbackEntry` | 不让副本局里留下没收到进入消息的成员 | 失败的连接按 RQ-036 关闭，由生命周期 CMD 把它移出副本局；其他成员正常进入（VC-3） |
| DR-3 | 两塔 `SendToExactAsync` 失败 → `CloseQuietly`；协调投影抛出 → `CloseExact` | 失败者不留在塔局里 | RQ-036 关连接，生命周期 CMD 清理 |
| DR-4 | 怪物死亡、祭坛、清算：提交之后，在投递或投递前的工作中失败 → `BlockAsync` / `PublicationFailed` | 状态已提交而客户端不知道时，不让会话继续 | 分成两种情形。**传输层交付失败**发生在 CMD 之后，按 RQ-036 关闭失败的连接（VC-4）。**提交之后、CMD 之内的非传输失败**（组帧、运行时认领、评估），以及提交本身不确定的情形，仍按 RQ-023(c) 封锁参与者集合中的全部会话，处理方式不变 |
| DR-5 | 队伍 revision 水位线只在送达后推进；城镇只对 Delivered 补发队伍刷新 | 不让成员错过队伍状态 | 刷出时推进。失败的连接会被关闭，重连时选角工作流会下发完整状态 |
| DR-6 | 7 个配额刷新只有 Delivered 才记「本期已送」 | 不漏发配额状态 | 刷出时记录。失败连接被关闭，重连时选角会下发 |
| DR-7 | GM Cera 提醒 Failed / Gone 返回 unconfirmed | 让 GM 知道提醒可能没到 | 返回「已提交发送」或「不在线」（VC-6） |
| DR-8 | 房间机制通知失败 → 抛 IOException 等重试 | 机制状态一定送达 | 失败连接关闭；重连后的进本或回城投影下发当前状态 |
| DR-9 | 生物重命名：所有者没送到就不发 ACK | ACK 不先于外观 | 外观与 ACK 依次登记进同一个发件箱；所有者失败时连接被关闭 |
| DR-10 | 清理通告：所有者必达失败 → 不再发同伴和队伍通知 | 顺序与必达 | RQ-034 |
| DR-11 | 回选角部分失败 → 关连接 | 失败的会话不留在半退出状态 | RQ-036 |
| DR-12 | 连接建立时初始 NOTI 失败 → 断开并抛出 | 连接不处于未初始化状态 | RQ-036 |
| DR-13 | 钱包变化、PvP 参与者通知各重试 1 次 | 偶发失败时补送 | 不再重试；失败的连接关闭（VC-5） |
| DR-14 | 每收件人 2 秒期限 + 重试队列 + `NotificationRetryDrainService` | 慢连接不拖累发送方 | RQ-036 / RQ-037；重试队列与排空服务删除 |
| DR-15 | handler 抛异常 → 断线 | 未知错误不让会话继续 | RQ-031(c) |
| DR-16 | 选角持有称号投影门，压住实时称号投影，直到整个进入批次送达 | 称号投影不插进进入序列 | 称号事件 CMD 在选角 CMD 完成之后才执行；按 RQ-032，它的消息排在选角消息之后交付 |
| DR-17 | 队伍按代际的流门与 revision 水位线，原因是「命令先释放门再做传输」 | 队伍投影不乱序、不回退 | 队伍命令与它的投影在同一条 CMD 内；按 RQ-032，不同 CMD 的投影按完成顺序交付 |

实施时如果发现本表没有覆盖的依赖，补入本表并配测试。

## Appendix F：锁与并发原语的去留

依据 `research/04-locks-and-persistence.md`。

| 去留 | 项 |
|---|---|
| 删除 | `SelectedSessionTransitionGate`（含借用帧、`AcquireManyExclusiveAsync`）；`TownAreaProjectionGate`；`DungeonProjectionGate`；`BossTowerProjectionGate` / `BossTowerEntryGate`；`IllusionTowerProjectionGate` / `IllusionTowerEntryGate`；`AchievementTitleProjectionGate`（64 路信号量）；队伍按角色的 SemaphoreSlim 与 `PartyDeliveryStream.Gate`；`OnlineUserExecutionGate`、`AccountExecutionGate`、`UserExecutionGate<T>` 基类，以及 `ApplicationModule.cs:280` 没有使用者的 `UserExecutionGate<CharacterId>` 注册；`TransactionalExecutionGate`；管线行为 `SelectedSessionExecutionBehavior`（45）、`OnlineUserExecutionBehavior`（50）、`AccountExecutionBehavior`（55）、`TransactionalExecutionBehavior`（57）中的取锁部分，它们的非锁职责按附录 H 迁移；`IMultiAccountCommand`，以及命令注册中的 OnlineUser / Account 访问方式；`IMultiSelectedSessionCommand` 的取锁用途（参与者集合本身保留，见「保留」栏）；全部「锁内复查、重试」循环与私有 `LeaseSet` 类；`EffectOperationLedger` 的每连接 SemaphoreSlim（由 RQ-005 替代）；`DomainEventDispatcher` 的 groupGates（由执行器替代）；Weakness、Joust 维护服务的 SemaphoreSlim；`ScheduledTaskHostedService` 的并发槽与 `Task.Run`；`NotificationRetryQueue` 与 `NotificationRetryDrainService` |
| 保留 | 参与者集合（今天的 `IMultiSelectedSessionCommand.Sessions`）作为 CMD 数据，用于逐个复查当前性和结果不确定时逐个封锁（RQ-023(c)）；短 `lock(sync)`（应用层 190 处、协议层 11 处、持久化层 3 处）；PVF 内容源与规则初始化用的锁与信号量；`GameInboundFrameCounter`、`TouchSocketConnectionRegistry`、GmWeb 两个类的同步；`AcquireWriteAsync` 的写保留（单写者下无害，之后另议） |
| 改为执行器内访问 | 附录 A 所列由后台流、传输回调、事件循环访问的玩法状态：`OnlineStateRegistry`、`LoginSessionRegistry`、`DungeonRunDomainService`、生物运行时、两塔、比武、祭坛、队伍、城镇区域、副本参与、卡牌展示协调。它们的短锁按「保留」一栏处理 |

实施时如果发现本表以外的并发原语，补入本表。

## Appendix G：允许的可见行为变化

| ID | 变化 | 理由 |
|---|---|---|
| VC-1 | 多段 CMD（例如选角）的出站消息在 CMD 完成后统一落线，而不是在各段之间陆续落线。消息内容与相对顺序不变 | 发件箱只刷出已提交的结果 |
| VC-2 | CMD 因未处理异常结束时，在回滚或结果不确定的工作单元内登记的消息不会发出。今天的投影本来就在命令提交之后才发生，所以预期客户端可见的差异仅限于理论情形；实施中如果发现有测试受影响，补入本条并配证据 | RQ-031(c) |
| VC-3 | 组队进本或两塔进入时某个成员发送失败：只断开该成员，其他成员与请求者正常进入（今天组队进本会隔离已送达的成员并回滚整次进本；两塔进入会中止其余成员的发送、关闭全部隔离成员与请求者） | DR-2、DR-3 |
| VC-4 | 怪物死亡、祭坛、清算在提交后发生**传输层交付失败**时，只关闭失败的连接，不再隔离同局的其他会话。CMD 内的非传输失败仍封锁全部参与者，不在本条范围内 | DR-4 |
| VC-5 | 钱包变化、PvP 参与者通知不再重试一次；失败时断开该连接 | DR-13 |
| VC-6 | GM Cera 提醒的结果改为「已提交发送」或「不在线」，不再有「未确认」 | DR-7 |
| VC-7 | 放弃副本、离队、换队长、踢出队员在重试用尽时不回包、只记日志或回拒绝包的分支消失；这类请求总会得到正常处理和回包。不含两塔协调器对「调用方、队员或塔局成员中有会话不再是当前会话」（`ExactSessions` 为假）的确定性拒绝：那条路径的结果与基准逐字相同，不属于重试 | 串行下不再需要重试 |
| VC-8 | 慢客户端在发送超时或发送队列超限时被断开（今天是标记投递失败、进入重试） | RQ-036 |
| VC-9 | `SET_PARTY_INFO`（0x000C）的队伍标题超过 256 B 时请求被拒绝（解码失败），发送者连接被关闭；今天这样的标题被接受，并扇出给队员与区域内的人。256 B 是服务端策略，没有客户端上限证据 | F-115；不让一条客户端请求占满第三方的发送队列（RQ-036） |

## Appendix H：管线行为的非锁职责去向

依据：`Commands/Pipeline/*.cs`、`Effects/EffectOperationContext.cs`、`Effects/EffectOperationLedger.cs`、`Commands/NetworkCommandAuthorization.cs`、`Dispatching/NetworkMessageDispatcher.cs`。实施时逐项对账（AC-009），新发现的职责补入本表。

| 今天的位置 | 职责 | 去向 |
|---|---|---|
| dispatcher | 会话封锁 `IsBlocked`、登录顶替 `IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter` | 协议 CMD 开始时，在执行器上执行（RQ-025） |
| `EffectOperationLedger` | 按连接与序号登记账本条目，并检查序号必须前进；重放已提交请求；把 Pending 结束记为 Rejected；没有选中会话时进入「可拒绝、不可结算」帧（`EnterWithoutSelectedSession`） | 作为协议 CMD 的请求级上下文（RQ-025 的三种状态），在 CMD 开始时登记，结束时落定；重放由该 CMD 执行 |
| 45 `SelectedSessionExecutionBehavior` | `BeginMain`（一请求一主结算）、`DemandRecipient`、`Capture` 接收者、账号与会话一致性检查、参与者集合必须包含报告会话、逐个 `DemandCurrent`、结果不确定时逐个 `BlockAsync`、设置 `SelectedSessionCommandScope` | 取锁部分删除；其余在 CMD 内主结算所在的工作单元前后执行。主结算的识别方式由今天的「命令类型实现 `IRequestSettlementCommand`」改为「CMD 声明自己的主结算工作单元」，`EffectSettlementRegistrationTests` 按此重写 |
| 58 `SelectedSessionSettlementBehavior` | `authorization.DemandCurrent`、接收者 `DemandCurrent`、`Settled` / `Failed` 记录、结果不确定时 `BlockAsync` | 在主结算工作单元提交前后执行，语义不变 |
| 60 `UnitOfWorkBehavior` | 开启与提交事务、`RequireRollback`、`TransactionStarted` / `RollbackConfirmed` / `Freeze` | 由工作单元作用域承担，语义不变 |
| 100 `CommandExecutionPolicyBehavior` | 授权 `DemandCurrent`、拒绝未知执行方式 | 授权检查并入 RQ-025；执行方式随工作单元模型改写 |
| 0 `CommandLoggingBehavior` | 计时与成功或失败日志 | 由执行器对每条 CMD 记录，并带调用链（RQ-054） |

## Success Evidence

- EV-1：执行器、发件箱、发送端、调用链、事件 CMD 的单元测试与反例用例（AC-001～AC-004、AC-011、AC-013、AC-015～AC-017）。
- EV-2：基线与改造后的表征测试运行日志（AC-019）。
- EV-3：全量构建与测试日志，以及测试数量加法（AC-020）。
- EV-4：附录 A、C、E、F 的对账记录（AC-006、AC-010、AC-014、AC-017）。
- EV-5：测量记录（AC-021）。
- EV-6：文档差异（AC-022）。
