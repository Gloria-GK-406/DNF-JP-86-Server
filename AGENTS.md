# AGENTS.md — 86JP 服务端

DNF 日服 Act.86 客户端的服务端仿真。协议逆向结论的权威存放处是顶层
[`protocol/`](protocol/) 知识库（目录、模板、证据等级见
[protocol/README.md](protocol/README.md)）；帧级基线在
[Docs/NetworkingFrameProtocol.md](Docs/NetworkingFrameProtocol.md)。
旧单体实现保留在 `E:\Project\86JP` 仅供行为对照，**不要**参考其结构。

## 仓库布局

```
Server/      .NET 10 服务端解决方案（本文档主角，见下）
protocol/    协议知识库：按 endpoint/direction/type 组织的逆向记录
ClientHack/  客户端补丁宿主（纯 C17/x86，CMake，Az.dll + 插件体系）
Tool/        PvfProbe（PVF 归档探针）、PvfProxy（TCP 中转/启动器）
Docs/        帧协议基线、业务接入规范、客户端逆向系列文档
             （引擎侧：脚本运行时与行为数据模型见 Docs/ClientEngine/）
```

## Server 解决方案结构

解决方案 `Server/86JP.Server.slnx`，共享配置 `Server/Directory.Build.props`
（net10.0 / Nullable / **TreatWarningsAsErrors** / Deterministic），中央包管理
（TouchSocket.Hosting 4.3.1、Microsoft.Data.Sqlite、xunit）。

| 项目 | 依赖 | 职责 |
|---|---|---|
| `Dfo.Pvf` | — | 只读 PVF 档案库（cipher/zlib/索引/token），无游戏概念 |
| `Dfo.Server.Data` | — | 纯模型：领域记录 + 协议身份契约（`Networking/NetworkMessages.cs` 只留两个枚举、两个消息接口、两个帧元数据 record） |
| `Dfo.Server.Abstractions` | Data | 接缝层：`IServerModule` + 各领域服务/仓储/内容源/网络契约 + 通用只读服务器配置契约 |
| `Dfo.Server.Application` | Abstractions, Data | **领域编排核心**：命令总线与管线、消息分发机制、领域服务 |
| `Dfo.Server.Infrastructure` | Dfo.Pvf, Abstractions, Data | PVF 内容源 + 频道脚本 + 启动配置内存快照 |
| `Dfo.Server.Persistence` | Abstractions, Data | SQLite 仓储与存储 + `Schema/` 唯一建表归属者 + `Database/` 事务参与者。**全部类型 `internal`**，对外只经 Abstractions 契约 |
| `Dfo.Server.Networking` | Abstractions, Data | TouchSocket **传输**：帧适配/解析、TCP 服务、插件、发送器、注册表。收发路径上不含任何单个协议的知识；显式多收件人投递也只表达必达顺序和逐目标失败隔离 |
| `Dfo.Server.Protocol` | Abstractions, Application, Data, Networking | **协议切片**：一个协议一个目录，含其消息、解包器、编码器、消息 handler、命令与命令 handler；`ProtocolModule` 是唯一接线点 |
| `Dfo.Server.Host` | 全部 | 组合根：模块注册顺序 + appsettings + channel_info.etc |
| `Dfo.Server.Utilities` | — | 空占位 |

**分层由项目引用图在编译期强制**：

- `Data` 不引用任何项目；`Abstractions` 只引用 `Data`。
- `Networking` 只引用 `Abstractions` 与 `Data`——它**不引用 `Application`，也不引用
  `Protocol`**。传输不知道有哪些协议，只知道注册表里有什么。
- `Protocol` 引用其余四个。方向单向：`Protocol → Application`，因此
  **`Application` 永远不能引用 `Protocol`**。任何要同时看到"某个具体消息"和
  "某个领域服务"的代码，只能落在 `Protocol` 里。
- `Application` 不引用 `Infrastructure`/`Persistence`/`Networking`。
- `Persistence` 与 `Infrastructure` **互不引用**，且 `Persistence` 不引用 `Dfo.Pvf`。
  持久化要的三处内容侧事实（地下城经验策略、技能点表、物品到达规则）是 `Abstractions`
  里的接口，由组合根注入实现。这是两者能各自成项目的原因。
- 但 `PersistenceModule` 与 `InfrastructureModule` **必须同时注册**：前者有三处注册
  要后者提供的内容源，只装其一的容器解析不出可用服务。二者是并列不是可选。

注意：测试项目的 csproj 注释声称"有分层断言测试"，实际不存在；
`Dfo.Server.Application.Tests` 引用 Infrastructure 与 Persistence 是刻意的测试例外
（entry 规则要跑真实 PVF、载体等价性要跑真实 SQLite），它同时引用 `Protocol`
是因为测试仍按功能组织，尚未跟随源码切片重排。
`Dfo.Server.Persistence.Tests` 与 `Dfo.Server.Infrastructure.Tests` 按同一条线切开：
前者是 SQLite 与建表，后者是 PVF 内容、频道脚本与配置。

## 架构设计

一句话：**领域服务承载业务能力，命令执行总线执行领域命令，
socket 层的 handler 驱动领域命令完成任务。**

```
TCP 字节
  → TouchSocket 适配器（Networking/Framing，入站 14B Game 头 / 11B Channel 头；
     出站 Game 15B。入站长度是 A21 客户端的形状，A12 为 13B）
  → NetworkPlugin.OnTcpReceived（Networking/Runtime/NetworkPlugins.cs）
      按线级键查解包器注册表；查不到即丢弃。传输侧到此为止，不认识任何具体协议
  → IGameMessageDecoder / IChannelMessageDecoder
      （Protocol/<Endpoint>/<Protocol>/*MessageDecoder.cs）
      调本切片的 payload codec → 强类型 IClientNetworkMessage
  → INetworkMessageDispatcher（Abstractions 接缝，实现在 Application）
  → NetworkMessageDispatcher：按 ClientMessageType 查注册表 → 每 message 新 DI scope
  → ClientMessageHandler<TMessage>（Protocol/<Endpoint>/<Protocol>/*Handler.cs）
      鉴权门（IOnlineUserQueryDomainService）→ commandBus.SendAsync(...) → 发响应
  → CommandBus 管线（机制在 Application）→ ICommandHandler（在切片内）
      → 领域服务（Application）→ 仓储与存储（Persistence）
                              → 规则服务（Application）→ 内容源（Infrastructure）
  → Game 由 IGameMessageSender 约束 CMD/NOTI，Channel 直接用 INetworkMessageSender；
      最终均委托 TouchSocketNetworkMessageSender 按消息 CLR 类型查编码器注册表
      → IServerMessageEncoder（Protocol 切片内）产出完整帧 → 出站
```

### 1) 领域服务编排业务（Application）

领域按目录组织：`Accounts/`（账号 entry 状态）、`Channel/`（入场策略）、
`Characters/`（花名册/entry 状态/入场后修正）、`Inventory/`（入场租约、每日补货）、
`Quests/`（按角色取得的任务日志：在做的、已通关的、追踪选择）、
`OnlineUsers/`（在线聚合 + 执行门）、`Sessions/`（在场、选中会话、PvP 大厅、专家商店
四个领域服务，各自把运行态留在私有容器里）、`World/`（出生点策略、世界内容）、`Users/`
（`OnlineStateRegistry` 持三张字典与唯一那把锁，`AccountDomainService`、
`OnlineUserDomainService`、`ActiveCharacterDomainService` 三个门面各取其一）。
服务只做业务；`Persistence` 提供仓储与存储实现，`Infrastructure` 提供 PVF 内容源实现。

**三层是上下关系，不是三选一。**

```
CMD handler          驱动用例，不承载业务语义
  → 业务服务         （领域服务）读写世界状态，编排用例
    → 规则服务       内容 + 传入的 facts + 显式种子 → 值/决定，无状态
      → 纯规则       静态类，纯函数，不碰 I/O
```

每一层只调它下面那层。业务服务问规则服务「规则怎么说」，规则服务读内容源并调静态规则类算出
答案；业务服务不自己读内容源，handler 也不自己问规则服务——端点解析（`IChannelEntryPolicy`）
是唯一的例外，它列在下面「handler 业务语义规则」允许的四类里。

**规则输入只经规则服务进入本服务器。** 规则输入指游戏规则依赖的、来自运行中世界之外的值：
PVF 内容源、游戏规则配置、以及表达归档或游戏语义的常量。它们的读取点只有一个，就是拥有该
主题的那个规则服务；`Application` 的业务服务、`Protocol` 的 handler 与命令处理器都不读。

反过来的一半早就写在 `Docs/领域结构重构设计.md` §6 里：规则服务不许自己查世界状态，只能接收
facts。两半合起来才是一条线——一边不许往上摸世界，一边不许绕过它去够内容。

### 命令处理器只够得到领域形状

命令处理器的构造器里只允许出现两种东西：

| 形状 | 是什么 | 怎么认 |
|---|---|---|
| 领域服务 | 编排一项业务能力；规则判断由它在内部向规则服务索取 | `Dfo.Server.Abstractions` 里以 `DomainService` 结尾的接口 |
| 领域 store | 领域状态的事务结算真源，被要一个会话、由事务收口 | 同一程序集里以 `DomainStore` 结尾的接口 |

**实例与快照不在这张表上，因为它们不是被注入的**：实例由领域服务交出来
（`characters.Get(id)`、`logs.LogOf(...)`、`containers.FindContainer(...)`），快照作为命令
自己的字段送进来。

除此之外的一切都不许注入——行网关（`*Table`）、持久化接缝（`*Persistence`）、运行时容器
（`*Directory` / `*Registry`）、内容源、规则服务、命令总线。要用它们，就把用法收进拥有那件事
的领域服务里。

唯一的例外是一张具名豁免清单，目前只有 `TimeProvider`（环境时钟，不是这个游戏知道的东西）。
清单与它每一项的理由都写在守它的判据旁边。

**这条由判据守着**：`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`
反射遍历全部 `ICommandHandler<,>` 实现的构造器参数。它按类型判定而不是按一张类型名单——
名单要人手维护，而没人维护的名单等于没有规则。同一个判据另有一条用例专门证明它会拒绝
（喂给它行网关、持久化接缝、运行时容器与命令总线，断言全部被拒），否则一条什么都放行的规则
和一个没有违规的仓库看起来一模一样。

### 角色自己的数，问角色

等级、经验、剩余 SP/TP 这三个数属于角色自己，**从角色实例问，不从会话快照读**。

剩余点数尤其如此：数据库里没有这一列，它每次都要用等级去档案累加总量、加上库里的加成、
再减去遍历已学技能算出的花费。入场时算过一次并不代表以后还对——升级会让三项里的第一项变，
而没有任何东西会去追那份算好的值。这条规则就是从那个缺陷来的：玩家升级之后客户端看到的
技能点不变，要重登才对。

算术只有一处实现，在拥有档案答案的那个规则服务上；入场用手边的数据供料，角色实例用从表读到
的数据供料。**两个供料方，一个算法**——两份实现迟早分叉。

**现在做到了哪一步，如实说**：剩余 SP/TP 已经全部经角色实例回答，四个发经验帧的地方一处不剩；
道具的等级判定也改成了问实例。等级与经验则仍有第二条路——发帧时用的是授予动作自己的返回值，
那不是第二个来源而是那次写入的回值；会话里那份副本在其余地方仍被读作回落。把它们也收干净，
是要连着会话快照其余部分一起拆的下一件事，不在这一轮里。

会话快照里仍然留着坐标、在场、世界内容、背包投影与账号侧状态。**那不是说聚合已经拆完了**，
只是说剩余点数不在里面了。

**这几样状态各自归谁——以及哪几样只做了一半。** 下面这张表按「谁是主人」记，不按「计划要做什么」
记。**先看最后一列**：有两样今天一个生产调用方都没有，那是刻意的，不是漏接线。

| 状态 | 主人 | 入场序列之外今天谁在问它 |
|---|---|---|
| 身份（槽位、名字、职业、成长型） | 角色实例 `ICharacter.IdentityAsync` | 道具可用性判定（`PlayingCharacter.CanUseAsync`）。实例从 `characters` 行现读，不再向会话要 |
| 坐标 | `ICharacterPositionDomainService` | 6 个生产注入点：`TownProjectionSlice.cs:216`/`:335`、`DungeonSelectionSlice.cs:173`、`LeaveDungeonSlice.cs:91`、`SelectCharacterLifecycleServices.cs:33`/`:132` |
| 任务 | 既有的任务簿实例 `ICharacterQuestLogDomainService` | 进本时收集迷宫筛选用的 facts（`DungeonEntrySlice.cs:477-483`） |
| 设置（热键、选项位图、可见位） | `ICharacterSettingsDomainService` | **零个。刻意的，见下。** |
| 暗骑连招页 | `IDarkKnightComboDomainStore` + `IDarkKnightComboBook` | **零个。刻意的，见下。** |
| 疲劳 | `ICharacterFatigueDomainService` | 进本处报告消耗（`DungeonEntrySlice.cs:509-511`）。**接缝不记账，见下。** |

**会话运行态聚合仍然存在，而且几乎原样存在。** `SelectCharacterResult`
（`Dfo.Server.Data/Game/SelectCharacterModels.cs:35`）九个成员一个没少，
`ISelectedSessionRuntimeDirectory` 照旧。这一轮动的主要是**谁来回答**，不是**快照里有什么**：
取数点从快照挪到各自的主人。快照本身只少了一个字段——`ActiveCharacterState.DarkKnightCombo`
连同 `DarkKnightComboState` 记录一起退役了，理由是它此前**每个构造点写的都是 `null`、
全仓零个读点**，删它逐值不变。除此之外一个字段没删、没改。**拆整个聚合不在这一轮里**，
要等入场投影的公开态/进度/特性三块内容查清之后再定。

**`ActiveCharacterState.Position` 已经成了死字段——它还在，但没有人维护它，也没有人读它。**
`Server/src` 里写它的只有一处：`SelectCharacterPreparationService.cs:54`，入场落位时用频道出生点
`with` 出来的。读它的只有两处，都在入场序列之内（`SelectCharacterPreparationService.cs:149`
解析频道落点、`SelectCharacterLifecycleServices.cs:188` 把它交给坐标服务）。入场之后再没有任何
东西写它——`Server/src` 全量搜 `Position =` 与 `Position:`，除上面那一处只剩坐标服务对自己私有
`Standing` 记录的写；也再没有任何东西读它——`Server/src/Dfo.Server.Protocol` 下 `.Position`
成员访问 **0 处**。而 `ReplaceExact` 有 6 个生产调用点
（`SelectCharacterLifecycleServices.cs:195`、`DungeonClearSlice.cs:752`、`MonsterDeathSlice.cs:490`、
`AcceptQuestHandler.cs:106`、`FinishQuestHandler.cs:144`、`TutorialLevelUpSlice.cs:241`），
没有一处在交回去之前动过 `Character.Position`，于是每一次都把入场那一刻的坐标原样搬回。
**所以快照里的坐标自入场起永久陈旧。** 摘掉这个字段要动 `Dfo.Server.Data` 与五个切片文件，
不在这一轮里；在那之前，**任何从它取坐标的新代码都是错的**——坐标问
`ICharacterPositionDomainService`。

**疲劳有接缝，但接缝不记账。** 唯一注册的实现是
`UnaccountedCharacterFatigueDomainService`：它答一对定值，对报给它的东西一律丢弃。进本处
（`SelectDungeonHandler`）确实调 `SpendAsync` 且数目被判据钉住，但那是**报告**不是**闸门**——
没有余额可以不足，`SelectDungeonRefusal.InsufficientFatigue` 至今无人问。
`CharacterRosterService.cs:147` 那个写死的 `new CharacterFatigueState(188, 0)` 仍是发出去的唯一数字。
**报的那个 `1` 是占位值，且与档案已知冲突**：`SelectDungeonHandler.FatigueAnEntryCosts` 无条件
报 1，而副本自带 `[no fatigue]` 标记（`DungeonDefinition.ConsumesFatigue`，全仓唯一读者是
`PvfDungeonDefinitionSourceTests.cs:32` 那条钉解析的判据，它断言教程本 10000 为 `false`）——
按写下的规则那本进本该扣 0。今天不记账所以无可观察后果，但**真开始记账时，调用点必须先问档案
这本扣不扣**。规则与出处见 `Docs/DungeonEntryNotes.md` 第八节。

**设置与连招今天刻意没有消费方，别当死代码删掉。** 两者都有真实的表、真实的 SQL，判据对着真库
校验它们答得对；缺的是调用方，而调用方要等改键协议与连招编辑协议做进来。
证据：`character_hotkey_slots` 在 `Server/src` 里只有 `CREATE TABLE` 与一条 `SELECT`；
`character_settings` 只有 `CREATE TABLE`、一条 `SELECT` 与建号时的 `INSERT … DO NOTHING`；
全仓 `UPDATE character_settings` / `UPDATE character_hotkey` 零命中；唯一携带选项位图的入站协议
`SaveGameOption1Handler` 自己的摘要写着 "Nothing is persisted yet"。连招 store 的写路径
（`INSERT … ON CONFLICT DO UPDATE`、`DELETE`）已经建好，同样零调用方；入场读走的仍是
`ICharacterComboPageTable`。**没有任何架构判据会注意到它们长期无人调用**，所以这一段就是那个提醒。

**宿主部署配置不是规则输入。** `IServerConfigService` 与它的三个消费方按下面「接口后缀标明
类别」那节记的例外办，不受这条约束。同样不算规则输入的还有两类，各自在代码里写明了理由：
纯测量开关（`ExperienceDiagnostics`）与协议投递行为（`SelectCharacterDeliveryOptions`）。

**在线状态那把锁不能拆成三把。** 三张字典是同一个不变量的三种读法——一个账号至多一个活跃
角色，且该角色的 session 属于活着的连接。断线要同时删连接和该账号的活跃角色，发布要先读在线
用户再写活跃角色表；各自一把锁会开出"账号绑在一个已经没了的连接上"的窗口。所以责任拆成三个
服务类型，它们共享的状态留在 `OnlineStateRegistry` 的单锁后面。

### 2) 命令执行总线（Application/Commands）

- 契约（`CommandContracts.cs`）：`ICommand<TResult>`（CLR 类型即身份），
  `IOnlineUserCommand`/`IAccountCommand` 携带作用域用户 id；
  `CommandExecutionKind` = Direct / Transactional / Workflow；
  `UserAccessMode` = Unscoped / Shared / Exclusive。
- `CommandBus`：每次 `SendAsync` 开新 DI scope → 查 `CommandHandlerRegistry` →
  按 `Order` 排序管线行为、从外到内包装 → 执行。
  协议 handler 可以按顺序调用多次 `SendAsync`；**命令处理器禁止嵌套
  dispatch**，不得在 `ICommandHandler` 内递归或间接调用 `ICommandBus`。
  共享业务逻辑抽成应用服务，由命令处理器直接调用。
- 管线（`Pipeline/`，Order 从小到大为外到内）：
  1. `CommandLoggingBehavior`（0）：计时 + 成功/失败日志
  2. `OnlineUserExecutionBehavior`（50）：按 OnlineUserId 取共享/独占租约
  3. `AccountExecutionBehavior`（55）：按 AccountId 取租约
  4. `UnitOfWorkBehavior`（60）：Transactional 命令在此开领域事务，命令正常返回
     后提交，任何其他出路回滚；其余执行方式原样放行。**Order 必须严格落在
     55 与 100 之间**，事务才整个待在账号租约之内（对**声明了**账号访问方式的
     命令而言——未声明的命令 `AccountExecutionBehavior` 直接放行，不取租约，
     道具移动与道具使用两条即是如此）、又整个包住 handler
  5. `CommandExecutionPolicyBehavior`（100）：Direct/Workflow/Transactional 均放行；
     未知执行方式抛异常
- 执行门（`OnlineUsers/UserExecutionGate.cs`）：按 key 的读写锁 + FIFO 等待队列；
  三个实例：OnlineUserExecutionGate、AccountExecutionGate、
  SelectedSessionTransitionGate（第三个由 `SelectCharacterHandler` 手工获取，
  串行化会话切换/拆除）。
- 错误语义：异常直接上抛。Direct/Workflow **总线级无回滚**；Transactional 由
  `UnitOfWorkBehavior` 回滚本次登记的全部参与者，但只回滚参与者，不回滚
  参与者之外的副作用。跨阶段补偿仍由协议 handler 派发专用补偿命令，
  命令 handler 只执行单一领域动作。
- 领域事务（`Abstractions/Transactions/`、`Application/Transactions/`）：参与者声明
  `CommitCanFail`；提交分三段——全体 `PrepareAsync` → 唯一可失败者 `CommitAsync`
  → 其余 `CommitAsync`。**一次事务只收一个 `CommitCanFail` 参与者**，第二个在
  `Enlist` 时抛；这是「不做两阶段提交」的代价与前提。登记按 identity 去重，
  而 SQLite 侧的 identity 是**连接字符串**，即**一个数据库一个参与者**——物品、
  人物、任务与暗骑连招页是同一个参与者的**四**个面（`ISqliteStoreFacet`），不是四个参与者。
  这是被上一条逼出来的：若各自入伙，第二个可失败参与者会被拒，一次用例就永远无法同时改物品与
  人物。同一数据库在一次用例里因此只参与一次，其读能看见自己未提交的写。
  环境事务经 `IDomainTransactionAccessor` 暴露（`AsyncLocal`），因为存储是单例、
  容器按角色持有，两者都拿不到注入。
- 载体协议（`Abstractions/Transactions/DomainStores.cs`）：`IDomainStore<TSession>`
  给出载体身份、是否持久、以事务换取会话；`IDomainStoreSession<TPlace,TKey,TValue>` 是
  「按位置读、按位置写、按位置清空」的共享词汇。今天有**四**个 session：
  `IItemDomainStoreSession` 与 `IDarkKnightComboDomainStoreSession` 直接就是它的具名实例
  （后者是 `IDomainStoreSession<OwnedCharacterId, byte, DarkKnightComboEntryState>`，一张按页号
  寻址的齐次表）；`ICharacterDomainStoreSession` 与 `IQuestDomainStoreSession` 不是——人物和任务都不像
  容器那样是一张齐次的表，所以各是若干具名分区的组合：人物那边 `Creatures` 是钉死类型参数的
  `IDomainStoreSession`，一角色一行的位置分区另给三个成员；任务那边 `Carried`、`Cleared`、
  `Tracked` 三个分区都是。两种形状共享同一套词汇，
  类型一个不丢。业务代码经
  `AmbientStoreAccess` 触达：边界内用环境事务，边界外读放行、持久写拒绝、易失写
  自开一次性边界并当场落定。
- 注册（`ProtocolModule.cs`，与协议接线同处）：**凡能触达持久写的命令一律
  `Transactional`**——只有它会让 `UnitOfWorkBehavior` 开领域事务，没有那个事务，
  持久化就没有边界可加入，写会各自落定且无人能收回。据此：LoginGameUser、
  CreateCharacter、DeleteCharacter，以及 SelectCharacter 的 Prepare、DailyRefill、
  Quests、Pet，加上 MoveItemSpace、UseStackable、GrantTutorialReward，均为
  **Transactional**。
  仍为 Workflow 的只有两条只动内存注册表的（Publish、PvpReady）与 **Compensate**；
  Compensate 是刻意的——补偿逐个收集失败并继续，最后把收集到的抛出，边界会把此前
  每一处成功一并撤销，正是它的反面。它唯一的持久写（离场位置）经
  `PersistOnTeardownAsync` 自开边界当场落定。
  GetAccountCharacterRoster 与 CheckCharacterName = Direct+Account Shared；
  AcknowledgeGameExit 与 Channel 的 ScriptVersion、Script、Info = Direct。
  `ApplicationModule` 只留 `AddCommandDispatching()`：机制在 Application，
  具体命令在切片。
- 命令注册表**没有**启动完备性检查（未知命令 dispatch 时才报错）——与消息
  注册表相反。

### 3) Socket 层：handler 驱动领域命令

- Networking 只做传输：`ChannelNetworkPlugin`/`GameNetworkPlugin` 把帧交给
  注册表查出的解包器，拿到类型化消息后丢给 dispatcher。**传输侧没有任何
  `switch`，也不认识任何具体协议**：线级键（Game 是 selector+type，Channel 是
  消息号）查不到就是未注册，帧被静默丢弃。
- 解包与编码都由 DI 注册表驱动，注册表在 `ProtocolModule` 里被填满：
  入站按线级键注册并**声明该解包器能产出哪些目录成员**，出站按消息 CLR 类型
  注册并声明允许的端点。
- 消息注册表（`MessageHandlerRegistry`）**没有**枚举完备性检查——目录是服务器
  自己的协议总账，可以远大于已接线的集合。它检查的是**闭合性**：任何已注册
  解包器声明能产出的消息类型，必须有已注册的 handler，否则启动即抛。
  解包成功却没人接的消息才是接线错误。
- handler 统一模式：解析连接身份 →（可选）`IChannelEntryPolicy` 解析频道 →
  发命令 → 用端点对应 sender 回响应。没有领域状态或领域决策的纯协议交互
  可以直接发送，例如 Channel Connect 直接返回 Protocol 所有的当日密钥。
  **handler 不得承载业务语义**：见下面的"handler 业务语义规则"。
- 发送器按端点校验（Channel 消息只能发往 Channel 连接），Channel 载荷走
  AES-128-ECB + zlib + 当日密钥；Game 出站 15 字节头。
- 连接生命周期：`OnlineUserConnectionObserver`（`Protocol/Game/`）在 TCP
  连接/断开时直接调在线服务/清理服务/发送器（**不经过命令总线**——连接事件
  不建模为命令）。它落在 Protocol 而非 Application，是因为连接建立时要发
  `GameInitialConnectionNotice`，而消息 DTO 在切片里。

### 切片边界规则

一个协议 = `Dfo.Server.Protocol/<Endpoint>/<Protocol>/` 一个目录，最多六类制品：

| 制品 | 文件 | 何时可省 |
|---|---|---|
| 消息 record | `Messages.cs` | 从不 |
| payload codec | `*PayloadCodec.cs` | body 无需解码时（Channel 系） |
| 入站解包器 | `*MessageDecoder.cs` | 纯出站协议 |
| 出站编码器 | `*ResponseEncoder.cs` / `*NoticeEncoder.cs` | 纯入站协议（遥测类） |
| 消息 handler | `*Handler.cs` | 纯出站协议 |
| 命令 + 命令 handler | `<Verb>Command.cs` | handler 只读或只记日志时 |

规则：

- **切片内不跨协议引用**。要共用就上提到 endpoint 层
  （`Channel/ChannelProtocol.cs`、`Game/GameProtocol.cs`）或领域服务。
- 目录名不得与任何被引用类型同名——C# 名称查找里外层命名空间优先于
  using 导入的类型，同名会得到 CS0118。`AccountRoster` 之所以不叫
  `AccountCharacterRoster` 就是因为这个。
- 新协议只改一处接线：`ProtocolModule`。`ApplicationModule` 不再承载任何
  单个协议的注册。

### handler 业务语义规则

**handler 内部不得操作任何业务语义。** 带业务含义的行为必须经命令或领域服务，
不许在 handler 里就地完成。

理由是防一类隐性 bug：handler 直接改了状态，而领域服务层对此一无所知，
于是并发门、执行次序、补偿与日志全部绕过。

普通 handler 里允许出现的只有四类：

1. 从 `NetworkDispatchContext` 解出连接身份，取不到就抛；
2. 只读查询与端点解析（`IOnlineUserQueryDomainService`、`IChannelEntryPolicy`）；
3. 不含领域状态或领域决策的确定性协议能力，例如 Channel Connect 读取
   Protocol 所有的当日密钥，或 LOGIN 从 `IServerConfigService` 读取端点配置并构造响应；
4. `commandBus.SendAsync(...)`、sender 的单连接或显式收件人发送方法、
   `INotificationBus.PublishAsync(...)`，以及把两者按顺序打包好的投递服务
   （如 `ICleanupNotificationDelivery.DeliverAsync`）——见下面的「CMD ⇒ Notify」。
   发布一个通知是投递，不是业务：谁该收到由前面的命令决定，通知只负责把它送出去。
   **一份计划要分两条路送出去时，把顺序封进一个投递服务，不要在每个调用点各写一遍**——
   清理通告有六个投递点，**其中四个在 catch 里**（`OnlineUserConnectionObserver.cs:102`、
   `SelectCharacterHandler.cs:136` / `:156` / `:162`），`:156` 那个还在补偿命令之后。

Game 出站还必须遵守一条协议边界：`0x01` 是 CMD，`0x00` 是 NOTI，二者由客户端的
两张独立注册表接收。这个语义只属于 Protocol。每个 Game 出站 message 必须且只能实现
`IGameCmdMessage` 或 `IGameNotiMessage` 之一；编码器分别继承对应的固定类别基类，handler
分别调用 `IGameMessageSender.SendCmdAsync` / `SendNotiAsync`。批量或显式收件人发送使用
`GameExplicitMessageDelivery`，其中每条消息仍携带自身类别，调用方不能覆盖首字节。
Networking 只把该字节当作不透明 `Selector` 做拆帧、查表与写帧，不解释 CMD/NOTI。

`SelectCharacterHandler` 是批准的多阶段协议工作流例外：除上述四类外，它可以获取并
释放 `SelectedSessionTransitionGate`、用只读目录/在线查询检查 session 仍为 current、
维护待关闭连接集合，并在失败时派发专用补偿命令；实际关闭必须在 gate lease 释放后
执行。这个例外只授权生命周期**协调**，不授权 handler 直接修改领域状态；所有准备、
补货、发布、PVP、任务、宠物与补偿动作仍分别由阶段命令处理器完成。

**禁止命令处理器嵌套 dispatch**：协议 handler 可以为一个入站工作流按顺序派发
多条命令；但 `ICommandHandler` 内部不得通过 `ICommandBus` 递归或间接派发另一条
命令。命令之间需要共享的业务逻辑应抽成应用服务，由各命令处理器直接调用。
纯遥测协议（无领域变更、无响应）可以只打日志不发命令，这是有意的例外，
必须在协议记录的 Implementation mapping 里写明。

### 端到端示例：GameLogin

1. 14B 头帧 → `GameClientFrameDataHandlingAdapter` → `GameNetworkPlugin`
   按不透明 selector 0x01/type 0x0001 查表；Protocol 将所有 C→S Game 注册固定为 CMD，
   `GameLoginPayloadCodec` 解出 loginId + 密码哈希
2. dispatcher → `GameLoginHandler`：要求该连接已有匿名在线注册
   （TCP 连上时由 `OnlineUserConnectionObserver` 创建并推送初始连接通知）
3. `commandBus.SendAsync(LoginGameUserCommand)` → 管线：日志 →
   OnlineUser 独占租约 → Direct 放行 → `LoginGameUserCommandHandler` →
   `IAccountDomainService.LoginAsync` → SQLite `GetOrCreate` → `OnlineUser.Authenticate`
   （generation +1）
4. handler 从通用 `IServerConfigService` 的内存快照读取环境、公开 host 与三个端口，
   构造完整 `GameLoginResponse` 后通过 `SendCmdAsync` 发出；CMD 编码器只按消息字段编码，
   固定首字节 0x01，再由 15B Game 头出站

集成测试 `Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs`
用真实 TouchSocket host + SQLite 断言逐字节行为。

### SelectCharacter：接受与状态同步工作流

`SelectCharacterHandler` 是协议工作流协调者：它持有
`SelectedSessionTransitionGate`，在同一 lease 内顺序派发准备、每日补货、发布、
PVP、任务、宠物与补偿等顶层命令，并在阶段之间发送具体状态同步消息。
`01/0004` 只表示接受进入，后续消息是可复用的生命周期状态同步。发送前会先验证
已准备的全部必达接受/前置状态可编码；失败时派发 exact-session 补偿命令，lease
释放后再尽力关闭连接。每个命令 handler 只执行单一领域动作，禁止持有
`ICommandBus` 递归编排。同步消息按自身协议身份分类，不按工作流位置猜测：进入接受、
PremiumService、CloneTitle 是 CMD，其余当前状态推送是 NOTI；拒绝保持相邻的 CMD
`01/0004` 与 NOTI `00/01F7` 两条消息。Networking 只执行通用传输与显式收件人失败隔离。

### CMD ⇒ Notify：把一件事告诉一群人

**做完一件事和告诉别人这件事，是两步。** 命令改状态并提交；通知把结果送给一群人。
它们之间没有隐式联系——协议 handler 先派发命令，再发布通知，顺序写在那里看得见。

```csharp
var result = await commandBus.SendAsync(new MarkSelectedCharacterPvpReadyCommand(...), ct);
if (result.Publication.Audience.Count > 0)
    await notifications.PublishAsync(
        new NotificationAudience.TheseCharacters(result.Publication.Audience),
        new PvpParticipantPublished(result.Publication.Newcomer),
        ct);
```

要点，按重要性：

- **人群以角色表达，不是连接。** `NotificationAudience` 是一个封闭层级的值；
  「这个角色此刻在哪个连接上」只由通知服务回答（`NotificationAudienceResolver` 解析一次，
  `NotificationBus` 在每个收件人轮到时再问一次）。**领域服务不得为投递去查连接**——
  这条正是通知服务存在的理由，在它之前有两个领域服务因为要算「通知谁」而知道了 socket 的存在。
  **规则的边界说准**：禁的是「**拿一个角色去查它在哪个连接、为的是给他发东西**」。
  两个限定缺一不可——`FindByCharacter` 本身不是禁令的对象：
  `SelectCharacterLifecycleServices.cs:154` 与 `ExperienceBonusDomainService.cs:31` 今天仍在调它，
  取的是**会话标识**当运行态目录的键，**从不读 `ConnectionId`**，两处都合规。
  按「见到 `FindByCharacter` 就是违规」去 grep 会把它们误判。
  不禁的是「这次操作本身就发生在其上的那个连接」：清理计划带着离开者自己的
  `session.ConnectionId`（`SelectCharacterLifecycleServices.cs:120`），必送给本人的那一半直接走它，
  不是扇出。
  **别把这条豁免读宽**：`SelectedSessionIdentity` 里带着 `ConnectionId`，而它被传进十来个领域服务，
  照字面它们都「手上有连接」。豁免的是**这次操作的主体自己的**那一个，不是任何拿得到的连接；
  凡是要决定「还该告诉谁」的，一律走人群。
- **通知类型自带它的处理器契约。** `INotification<TSelf>` 是自指的，所以发布调用点的类型由实参定死，
  加宽到契约接口本身编译不过（`CS0311`）。处理器按**收件人**产出该收件人的消息，返回空表示弃权。
- **处理器只读。** 写属于已经提交完的那条命令。`NotificationHandlerArchitectureTests` 守着注入面：
  只允许 `Dfo.Server.Abstractions` 里以 `DomainService` 结尾的接口。
- **注册时声明重试上限**，每个通知自己决定。0 表示只发一次。
  **失败不只是抛异常**——每收件人有一个全局一致的投递期限，到点也算失败，而那时帧可能已经在传输里，
  所以重试可能是重复而不是补送。「到两次会出问题的通知应当声明 0」。
- **通知类型必须在 `ProtocolModule` 里注册处理器**，且该程序集要 `AddNotificationCatalog`。
  漏注册会在**开始接受连接之前**让服务器起不来（宿主先实例化 hosted service 再启动）。

**这一轮做到哪一步，以及没做什么：**

- 迁移了两处扇出：PvP 大厅公示、会话清理的「尽力送旁人」段。第三个候选
  （`SessionAdjacentEntryDomainService` 里的 `presence.InArea`）经检查**不是扇出**——
  它给入场者本人算「我周围有谁」，收件人只有一个。
- **周期触发不在这一轮里。** 没有调度器、没有时刻表。唯一的循环是重试队列的排空
  （`NotificationRetryDrainService`），它不驱动任何业务。
- **没有发布订阅。** 业务用例直接调用通知服务；不存在「发布一件事、由订阅者各自决定反应」的间接层。
- **其余「回给请求方」的发送一处没动。** 本轮收尾时实测 37 处 `SendExplicitAsync` 与 7 处
  `SendNotiAsync`（命令：`grep -rn "SendExplicitAsync" Server/src --include=*.cs` 去掉
  `INetworkServices.cs` / `GameMessageSemantics.cs` / `TouchSocketNetworkMessageSender.cs` 三个定义处）。
  它们绝大多数没有旁人收件人，套上人群抽象等于给占绝对多数的情形加一层间接。
  （数会变，看的是它旁边那句理由，不是这个数。）
- **频道、全服、组队三种人群没有实现**，因为本仓今天没有它们的真实调用方（队伍这个概念还不存在）。
  人群是封闭层级，但**它不是真正的联合类型**：C# 要求未密封 record 的复制构造函数至少 protected
  （`CS8878`），所以外部 record 借它派生得出来。真正的保护是解析器最后一个分支会**指名**没教过的形态。
- **被迁移的两处扇出，帧逐值不变，收件人集合在竞态窗口里与迁移前不同**——
  会话被顶替或重连时，旧代码寻址的是「当时记下的会话」，新代码寻址的是「这个角色此刻在哪」。
  完整差异清单在 `.rks-agent/tasks/notifications-know-their-audience/tasks/TASK-05-*.md` 与 `TASK-06-*.md`。

**投递失败会被说出来，但不是靠摘要。** 总线把每个收件人的结果放进一份摘要
（成功 / 弃权 / 人不在了 / 失败），而**读摘要是可选的，今天没有一个调用方读它**。
所以总线自己记日志，在还拿得到原因的地方记：

- 投递抛异常 → `Warning` + 异常本身；
- 到了每收件人期限 → `Warning`，与抛异常**分开说**（超时也是从 catch 里出来的，异常就在手上；
  不记它是因为那是取消令牌，不是原因）；
- 重试额度用尽、彻底放弃某个收件人 → `Warning`；声明了 0 次重试的通知不记这一条，
  因为它从来就没有第二次机会可放弃，失败那一条已经说过了。

**弃权与「人不在了」不记**——它们不是出了问题。

### 物品：有身份的东西，和它在哪，是两张表

**`items` 是世界表，`item_slots` 是位置表。** 世界表说「这件东西是什么、它自己带着哪些可变状态」；
位置表说「哪个容器的哪一格放着什么、几个、那 82 字节住在世界表的哪一行」。
移动一件东西不再销毁重建它的世界行——`item_uid` 跨越任何位置变动与任何写入都不变。

`items` 今天 **19 列**（`Schema/CharacterDatabaseSchema.cs` 的 `CREATE TABLE items`，逐列数过）：

| 组 | 列数 | 内容 |
|---|---|---|
| 身份与 core | 2 | `item_uid`（`INTEGER PRIMARY KEY AUTOINCREMENT`，带一条 CHECK，见下）、`item_core`（`CHECK (length(item_core) = 82)`） |
| 归属与时间 | 3 | `cargo_character_id`、`created_at`、`updated_at` |
| 时装 | 6 | `avatar_expire_date`、`_clear_id`、`_jewel_socket`、`_color1`、`_color2`、`_delete_date` |
| 契约兽 | 8 | `creature_field04`、`_mode_flag`、`_progress_value`、`_mode1_field0a`、`_mode1_field0b`、`_field_after_value`、`_text`、`_tail_flag` |

**「这一行有没有时装细节」由细节列自己回答，读侧只有一处。** 判定处是
`InventoryItemTables.cs` 的 `SqliteCharacterItemTable.Avatar(SqliteDataReader, int)`：
六列的**第一列** `avatar_expire_date` 是不是 NULL，就是这一行是不是时装的全部依据
（`SqliteWorldItemTable.Read` 也调它，不另判一次）。契约兽那八列同理，第一列是
`creature_field04`。`items` 上**没有** `avatar_uid`，也**没有** `idx_items_avatar_uid`。

「同一个时装 UID 全库至多一行带着它的细节」这条规则**还在**，但它不再是表的键，而是**三条写入
语句自己的条件，分别是**：`SqliteCharacterItemTable.UpdateOwnAsync` 的 `NOT EXISTS(…)`、
`SqliteCharacterItemTable.InsertOwnAsync` 的 `INSERT … SELECT … WHERE NOT EXISTS(…)`
（后者取代了原先的 `ON CONFLICT(avatar_uid) … DO NOTHING`）、以及
`SqliteItemDomainStore.FillAsync` 的 `INSERT … SELECT … WHERE @expire IS NULL OR NOT EXISTS(…)`。
前两条共用同一段条件文本 `SqliteCharacterItemTable.CarriesTheAvatar`；第三条**没有**复用它，
因为它比对的是**待插入的 core** 而不是一个 `@avatarKey` 参数，所以那段文本在它里面是内联的。
三条都从 **core 里**读 UID（`substr(item_core, 6, 4)`，即 core 偏移 5 起的 4 字节小端），
不再读一份列副本。**该谓词文本在 `Server/src` 今天出现在 4 处 SQL 里，按成员名分别是**
`SqliteCharacterItemTable.UpdateOwnAsync` 与 `SqliteCharacterItemTable.InsertOwnAsync`
（同文件 `InventoryItemTables.cs`，这两处是常量 `SqliteCharacterItemTable.CarriesTheAvatar` 的两个使用点）、
`SqliteItemDomainStoreSession.CarriedAvatarsAsync` 的检索键（写作 `AND substr(item_core, 6, 4) = @uid`）、
`SqliteItemDomainStoreSession.FillAsync` 的守卫
（写作 `AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4)`），后两处在 `SqliteItemDomainStore.cs`。
计数口径：在 `Server/src` 下按字符串 `item_core, 6, 4` 检索，命中 3 行；其中
`InventoryItemTables.cs` 那一行是常量声明，被上面点名的两个成员各引用一次，故算 4 处 SQL。
**这一句原先用行号索引，那是错的**；订正与来龙去脉见下面「这一轮补记的四条修正案」其一。

`item_slots` **8 列**：`owner_scope`（`character`/`account`）、`owner_id`、`list_type`、`slot_index`、
`kind`（`world`/`template`）、`id`、`core_uid`、`count`。
`UNIQUE(owner_scope, owner_id, list_type, slot_index)` 是「一格只放一样东西」；
三条具名 CHECK 分别是 `count > 0`、世界槽 `count = 1`、世界槽 `core_uid = id`。

**`core_uid` 是这个形状的关键，一个列解掉了两笔债。** 任何一种槽行都指名它那 82 字节住在哪一行
——堆叠物也有世界行，因为它的 core **不可约到「模板号 + 数量」**
（`StackableItemCore.Create` 写死 `Marker16 = -1` 与 `RandomOptionChangedIndex = 255`，
而 `GameInventoryDelivery` 对 `ItemKind == 2` 把 `-1` **原样上线不钳**；
见 `Docs/ItemWorldModel.md` 的「更正 C-1」）。于是两件事同时成立：孤儿查询收成单条
`NOT EXISTS (… holder.core_uid = items.item_uid)`，不再需要靠位置列把堆叠物挑出去；
上一版为躲唯一键冲突而设的「抬起到哨兵槽再落下」整套机制**摘掉**
（全仓 `LiftAsync` / `NoContainer` **零命中**）。

**分类由档案回答，不由存储里那个种类字节回答。** 唯一实现是
`ItemPlacementRuleService.ClassifyAsync`，签名**只收模板号**（`int itemId`），
答 `ItemWorldClass` 四值之一（`Interchangeable` / `Equipment` / `Avatar` / `Creature`）。
生产侧 6 个调用点（`InventoryEntryAssembly` 两处、`ItemContainers` 两处、
`CharacterRosterService`、`DailyRefillService`），覆盖每一条写入路径。
存储里的行说自己是别的种类时，**以档案为准并拒绝**，而不是照存。

#### 本轮明确没有做的（逐条，不是「大概没做」）

- **不做定时清理器。** 无人引用的世界行只要求**可查询**（`ReadUnreferencedAsync`），
  没有任何东西清扫它们。
- **不启用「存在但不在任何槽里」的用法**：地上掉落、交易托管、拍卖挂单、邮件附件，一条都没接。
- **不做契约兽领域本身**：实例动词、喂食、经验、等级、饱食度记账，一个都没有。
- **不裁契约兽槽位是 24 还是 25。** 沿用今天在用的 24；悬案原样留在
  `Docs/ProtocolScaffoldDissonances.md` 第九节。
- **不动 `character_condition` 的五个含义未查清的列。**
- **旧库作废重建。** 没有迁移机制，也没有为旧形状保留兼容读路径。

#### 被授权的两处行为变化

只有两处，其余一律逐值字节不变。完整清单（每条的连带后果与代码位置）在
`.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`。

1. **契约兽取下之后能装回去。** 全部起因是 `PvfEquipmentSlots.ByName` 补了一个键
   `["[creature]"] = 24`（该表今天 **16 个键**）。连带：契约兽瞄准非契约兽槽位时，
   **服务器内部**的拒绝理由从 `NotWearable` 变成 `ItemCannotOccupySlot`——
   `ErrorCodeFor` 把两者都映射到同一个错误码，**客户端字节不变**，但内部语义确实变了。
2. **契约兽的种类判定取档案的解释：1 → 5。** 起因是
   `PvfEntryLifecycleContentSource.WornPieceMetadata` 对 `ItemWorldClass.Creature`
   给 `CreatureItemKind = 5` 而非 `WornPieceItemKind = 1`。连带：
   **契约兽不再能被拖进快捷栏 3..8**（`ItemSlotRuleService.AllowsQuickSlot` 把 5 排除在外）
   ——这是第二处真实的可观察行为变化，只是没有既有判据覆盖它。
   **它有一个点名的可达实例**：交任务奖励里有契约兽模板 id **63465**
   （`equipment/creature/WolfLee.equ`，`[equipment type]` 原文 `[creature]`），
   而 `FinishQuestCommandHandler` 走的正是 `IItemContainer.AddAsync`。**不是理论上可达。**

#### 三处「结论对，但撑着它的不是代码」

**不要把它们读成「有代码守着」。**

- **`items` 上那条 `world_identity_fits_the_wire` CHECK（`item_uid BETWEEN 1 AND 2147483647`）
  只对新建库无条件成立。** 发布版的 `items` 表没有它，而 schema 调和只会
  `ALTER TABLE … ADD COLUMN`（`CharacterDatabaseSchema.cs` 一处），**补不了 CHECK**。
  既存库上溢出会落到编码时的 `checked((int))`，抛 `OverflowException`。
  这与「旧库作废重建」不冲突，**但说法不得省略这个限定**。
- **「今天不存在能销毁一件有身份物品的生产通路」成立，但其中一条依据不是代码给的。**
  `ItemContainers.ConsumeAsync` 只有一个生产调用方（`ItemUseEffectDomainService`），
  而它是否被调用只看 `rule.IsConsumedOnUse`——那是**档案数据**
  （`PvfItemUseRuleDefinitionSource`）保证的；`ConsumeAsync` 自己**没有任何守卫**
  拦住一件有身份的东西。
- **「清空先于填入」这条不变量已经不再承重，且没有任何判据钉住它。**
  `SqliteItemDomainStore` 的注释**四处**说它（类型级 `<remarks>` 一处，加 `ApplyDecidedWritesAsync`、`FlushAsync`、
  `EmptyAsync` 各一处 `<remarks>`），但变异实测：逐槽交替 empty/fill、字面违反该注释，
  **`Dfo.Server.Persistence.Tests` 今天的 316 条仍然全绿**（TASK-13 重跑过这次变异；
  它原先记的是 315，本成果后来新增一条判据，**结论未变**）。真正扛住换位的是 `spared`——幸存者按名字免删，
  落点是 `EmptyAsync` 里 `DELETE FROM items` 上追加的 `AND item_uid NOT IN (<幸存者>)`。
  这是一条**只写在注释里的口头不变量**。

#### 已知缺陷与覆盖缺口（只记，本成果不修）

- **时装与契约兽宝珠同样「取得下、装不上」。** `PvfEquipmentSlots.ByName` 的 16 个键里
  **没有任何 avatar 键**（文件里 `avatar` 三处出现全在注释里），而槽 0..11 正是时装带；
  `[artifact red/blue/green]`、`[charm]`、`[flag]`、`[name tag]` 同样不在表里。
  症状与契约兽本轮之前完全一致。**这是同一处不对称的其余部分。**
- **入场存盘的时装守卫，与发放路径的写入方是矛盾的（潜伏，今天不可达）。**
  `InventoryEntryAssembly.SaveAsync` 拒绝「档案说是时装、行却不是 kind 8」的行；
  而 `ItemContainers.AddAsync` 把 `placement.ItemKind` 原样写进 core，
  `PvfEntryLifecycleContentSource.WornPieceMetadata` 对非契约兽**一律给 1**。
  **即：一件时装模板一旦经发放路径入库，下一次存盘必被自己这道守卫拒绝并回滚。**
  今天走不到——五条发放通路（卡片奖励、地面拾取、接任务给的道具、交任务奖励、教程奖励）
  都调 `IItemContainer.AddAsync`，逐条对真实档案穷尽后**时装 0 个**；
  决定性的一条是**66328 个时装模板里声明 `[creation rate]` 的是 0 个**，
  而装备掉落池的唯一闸门正是它（`PvfDropItemPoolSource.ReadCreatable`），
  另有 `DropRuleService` 对 `DropItemKind.Avatar` 槽**显式 `return null`**。
  穷尽过程记在 `behavior-change-list.md`。**根因是下面那条 8／1 不一致；修了它这条自然消失。**
  **档案内容或发放表一变，它就会从「不可达」变成「玩家存盘失败」。**
- **时装的种类不一致仍在**：存储记 8、档案给 1。本轮只对齐了契约兽那一对；动时装属
  未授权的行为变化。**四处生产注释**点着它——数的是**点名 8／1 这一对**的生产注释：
  `ItemPlacementContent` 的 `ItemWorldClass` 类型级 `<remarks>`、
  `IItemPlacementRuleService.ClassifyAsync`、`ItemPlacementRuleService.ClassifyAsync`、
  `PvfEntryLifecycleContentSource.WornPieceMetadata`。
  **不计入**：`SqliteItemDomainStore.CarriedAvatarsAsync`、`AvatarKind` 常量、
  `InventoryEntryAssembly.WithAvatarDetail` 都只说「时装是 kind 8」而**不点名那对分歧**；
  判据侧 `ItemClassificationTests` 点名，但那不是生产注释。
- **新形状引入一个旧形状表达不出的失败模式：同一件东西被两个位置同时指名。**
  保存侧已加拒绝（`InventoryEntryAssembly.SaveAsync` 落位前比身份集合的大小），
  **装载侧没有对称的拒绝**——入场读只校验 (容器, 槽) 不重复，不校验 `core_uid` 不重复。
- **「非时装不得带细节」与三条取值校验全仓无断言。** `GameInventoryDelivery` 的
  `"Non-avatar inventory item carries avatar detail."` 与
  `"Avatar jewel socket must contain exactly 30 bytes."`、以及 `InventoryEntryAssembly` 里
  「珠宝槽恰好 30 字节 ＋ 两个颜色落在 0..65535」那一条，**在 `Server/tests` 全域零命中**；
  删掉它们全量仍会绿。（`avatar_jewel_socket` 另有 schema CHECK 兜底，其余没有。）
- **契约兽的「从槽 24 取下、目标背包格里已有另一只 → 互换」没有判据。**
- **「同一个时装 UID 全库至多一行」这条规则今天覆盖三条写入语句，还剩一个写入方不被覆盖。**
  规则的三处落点见上面的世界表小节。入场存盘走前两条，各有判据钉住，见
  `SqliteEntryInventoryMutationRepositoryTests` 的
  `DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与
  `DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`；
  第三条 `SqliteItemDomainStore.FillAsync` 由
  `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 钉住。
  **被拒时的表现与旧索引只差异常类型**：旧索引抛 `SqliteException`，今天 `FillAsync` 的
  `INSERT … SELECT` 落空、`RETURNING` 无行、`ExecuteScalarAsync` 返回 `null`，紧随其后那句
  `allocated is null` 抛 `InvalidOperationException`（消息含 `named no identity`），
  **同样回滚，不会静默通过**。
  **今天仍不被覆盖的写入方只剩一个**：`SqliteWorldItemTable`（`InsertAsync`／`UpdateAsync`），
  它写 `items` 时装列却没有自己的条件，退役前靠 `idx_items_avatar_uid` 抛错。
  它**生产里零调用方、零 DI 注册**（`PersistenceModule` 注册的是 `ICharacterItemTable`、
  `IAccountCargoItemTable`、`SqliteItemDomainStore`／`IItemDomainStore`，**没有** `IWorldItemTable`），
  全仓只有 `WorldAndSlotTableTests` 与 `CreatureIdentityFitsTheWireTests` 把它 new 出来。
  **这个缺口今天到不了生产，是有意留下的**——去补它等于在一个生产无人调用的类上引入新行为。
- **两条判据的名字不再描述它证明的东西**：
  `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`（现在靠「行没被删」通过，
  它 `<remarks>` 里说的那套搬运 `CarriedAvatarsAsync` 近乎走不到——**这一点仍然为真**。
  但**不要再照抄旧的那句「全仓零覆盖」**：让 `CarriedAvatarsAsync` 恒返回空表，
  今天 `Dfo.Server.Persistence.Tests` 会**红 1 条**——
  `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`，因为空表使 `@expire` 为 NULL、
  守卫短路放行、该判据等的那次抛错不再发生。**它钉住的是守卫对这次检索的依赖，
  不是那六个值被正确搬到新行上**；后者今天**仍然零覆盖**）、
  `EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt`
  （正向断言现在指向新增的 `ON item_slots(core_uid)`）。

#### 三条已被证伪的记载，订正

一度记着「孤儿的答案跨存盘易逝——`DeleteOwnExceptAsync` 删的恰好就是孤儿查询会报的那类行」。
**这句在位置列退役之前为真，今天不成立**：该删除现在以
`SELECT core_uid FROM item_slots WHERE owner_scope='character' AND owner_id=@cid …`
圈定对象，**一条没有任何槽行指名的世界行根本不在它的范围内**。
所以孤儿今天**不会**被入场对账顺手清掉，它会一直留着，直到有人写清理器。
「孤儿在今天的生产上不会产生」这一条不受影响。

其二，本文件一度把「**`avatar_uid` 这个半身份还在**」列在「已知缺陷与覆盖缺口」里，原话是：
「物品拿到了真身份 `item_uid`，但「这一行有没有时装细节」今天仍由 `avatar_uid` 是否为 NULL
决定（`items` 上另有 `idx_items_avatar_uid` 唯一部分索引）。它的退役曾被指给「退役旧形状」
那一步，而交付的形状仍是 `avatar_*7`——**那条派工没有落地。**」
**这句在写下时为真，今天不成立**：列与索引都已退役，判定处见上面的世界表小节。
那条缺陷条目**因此整条删除**，不是改写。

其三，同一次退役让 `Docs/Refactor/TableOwnershipRuling.md` 的 **D-15**（「时装细节表的主键靠
`items.avatar_uid` 上的部分唯一索引复原」）与该文档 `character_avatar_detail.item_uid`
→ `items.avatar_uid` 那一行**不再描述今天的库**。那是一份历史裁定，按本仓规矩**不改它的正文**，
本节即是它的修正案：它所保的那条规则仍在，位置换成了**三条**写入语句自己的条件。

#### 「服务端同意时装是什么」这一轮：做到哪一步

**这一轮的名字是 `server-agrees-what-an-avatar-is`。** 逐条的行为变化、覆盖读数与字节归因在
`.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（下称「清单」）。
这里只写后来的读者与所有者要拿去做决定的部分。

**读法提醒**：本文件上面几节里的「本轮」指的是上一个成果 `items-own-their-identity`
（那份清单在 `.rks-agent/tasks/items-own-their-identity/behavior-change-list.md`，
「被授权的两处行为变化」一节点着它）；**本节起的「本轮／这一轮」指
`server-agrees-what-an-avatar-is`**。

**做成了四件事**（第 1–3 件是本轮原本就要做的，第 4 件是被独立评审拦下来才补做的）。

1. **时装的种类只剩一个说法。** 上面「已知缺陷与覆盖缺口」里那条「存储记 8、档案给 1」不再成立——
   `PvfEntryLifecycleContentSource.WornPieceMetadata` 现在对 `ItemWorldClass.Avatar`
   答 `AvatarItemKind`（8），此前落 `WornPieceItemKind`（1）。
   观测：`GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar`
   对模板 `101550507` 断言档案与存储答同一个数；把档案那一支改回 1，该判据变红（清单第四节 MU-1）。
2. **发放通路能造出一件完整的时装：一个身份，加六列事实。**
   `ItemContainers.ArrivingAvatar` 与 `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支
   在落地时把 core 的 `Value` 换成 `ArrivingAvatar.NewIdentity()` 抽出来的数，并把六项事实随写入交出去。
   **六项分别是**：`avatar_expire_date` ＝ `int.MaxValue`、`avatar_clear_id` ＝ 0、
   `avatar_jewel_socket` ＝ 30 个零字节、`avatar_color1` ＝ 0、`avatar_color2` ＝ 0、
   `avatar_delete_date` ＝ 0——正是上面世界表小节里「时装 6 列」那一组。
   观测：`GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`
   与 `…Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`；MU-4、MU-5 各让它们变红。
3. **「同一个时装 UID 全库至多一行」那条守卫，对新入库的行开始生效。**
   上面世界表小节记的第三条落点 `SqliteItemDomainStore.FillAsync` 的
   `WHERE @expire IS NULL OR NOT EXISTS(…)`：此前一次发放来的写入把 `@expire` 绑成 `DBNull`，
   左侧取真、守卫短路；现在写入自带 `int.MaxValue`，左侧不再取真。
   观测：`TwoAvatarsDoNotShareOneIdentityTests.TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts`
   连同同类的另两条（`…TwoAvatarsPutDownInSeparateSettlementsStandUnderIdentitiesOfTheirOwn`、
   `…AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack`）；MU-6 让它们变红。

**第四件，是被独立评审拦下来才做成的：从地上捡起来的时装也是完整的。**

上面第 2 条说的「发放通路能造出一件完整的时装」，**在一段时间里只对 `AddAsync` 那一道门成立**。
**地面拾取那道门上不成立**，而那正是一条发放通路。

- **制造者是谁**：`PickUpGroundDropCommandHandler.CarryOverAsync`。它在
  `container.AddAsync(...)` 把时装落地（六列齐备）之后，对**同一格**再写一次
  `landed.Core with { Value = checked((int)found.Instance), Durability = found.Durability }`，
  走的是 `IItemContainer.PutAsync`，而 `StoreBackedItemContainer.PutAsync` 转调
  `WriteOneAsync(slotIndex, core, **null**, …)`——**不带六项事实**。
  那句无条件换 `Value` 的写法**在本轮基线 `565d33fe` 上就有**，本轮没有动过它。
  **就这一条路而言**（这句只说地面拾取，不说别的路）：本轮之前它写下的时装行带 kind 1，
  `WithAvatarDetail` 在 `item.Core.ItemKind != 8` 上早返、走不到那条拒绝；
  本轮的第 1、2 条（档案改答 kind 8、落地带六项事实）之后它带 kind 8，才走得到——
  **两者相遇，才有了这条缺陷。**
- **后果是什么**：换掉 `Core.Value` 之后 `SqliteItemDomainStoreSession.Survivors` 三趟都不认
  （`SameContent` 因 core 字节变了不成立，`SameThing` 因它比 `core.Value` 而这一位刚被换掉也不成立），
  该行被删掉重建；`FillAsync` 算六列时写入不带事实，落到 `CarriedAvatarsAsync` 回捞，
  而检索键取的是**新**的 `Core.Value`，回捞落空 → **六列全 NULL**。
  下一次入场装载在 `InventoryEntryAssembly.WithAvatarDetail` 抛
  `InvalidDataException: Avatar detail {value} is unavailable.`，
  该异常落进 `SelectCharacterHandler.HandleAsync` 的 `catch (Exception error)`，
  连接被关掉——**所有者看到的是「这个角色选完进不去」**。
- **由谁发现**：**完整变更集独立评审**（`.rks-agent/tasks/server-agrees-what-an-avatar-is/reviews/change-review.md`
  的阻断发现 BF-1）。**不是执行方自查，也不是任务级快审。**
  它此前没有被记下，两条可核的观测：评审自己写的是「这条新增的制造者
  不在 3.1–3.4 的 34 行里、不在 3.6 的两条里、不在正向扫的 B-01..B-05 里，
  `AGENTS.md` 也没有点到它」（逐字抄）；本轮开工时另在清单**第二版**上按
  「CarryOverAsync／地面拾取／GroundPickup／PickedUpAvatar」四个词跑过一次只读检索，
  **命中 2 处，逐处读过，两处都与这条门无关**（一处是起因 E 计数口径里排除
  `GroundPickupSlice` 那句，一处是第九节讲别的容器通路上非堆移动未测）。
  评审的凭据是它自己临时加的一条探针
  （用完删除、按 `sha256sum` 还原），读数 `Avatar detail 123456 is unavailable.`。
- **怎么修的**：TASK-09 在 `CarryOverAsync` 里按 `landed.WorldClass` 分岔——
  `is ItemWorldClass.Avatar` 时只写 `landed.Core with { Durability = found.Durability }`，**不碰 `Value`**；
  其余仍写 `Value` ＋ `Durability`。分岔用 `ContainerSlotItem.WorldClass`（向归档问来的那个答案，
  与 `IsPile` 同一个来源），不用调用方递进来的 kind 字节。
  理由不是「给时装开特例」：`FloorPickup.Instance` 的 `<param>` 把那个值定义成
  「把这件**装备**与另一件区分开的值」，而时装核心里那一位是身份、也是查六列用的键
  ——**这条通路本来就把两种东西写进了同一位**。
- **修后有无覆盖**：**有。** 新判据 `PickedUpAvatarStaysWholeTests` 三条（真 SQLite 库、
  操作者自己的 `Script.pvf`、地板由 `DungeonRunDomainService.AllocateMonsterDrops` 摆出、
  拾取走生产命令处理器）。变异实测 MU-09-1 把那一岔改回原样，读数 `失败: 2，通过: 1`：
  `An_avatar_taken_off_the_floor_is_served_again_with_its_six_facts` 以
  `Avatar detail 1075541543 is unavailable.` 红，
  `Two_avatars_off_one_kill_keep_two_identities_of_their_own` 以
  `Assert.NotEqual() Failure: Values are equal` 红。
- **一处不足以确定，明写**：**一件时装今天能不能真的出现在地板上，至今未确立。**
  上面那三条判据是用 `AllocateMonsterDrops` 直接把时装放上地板做出来的——
  **那是「GM 直接发放／掉落」那个动作的形状**，不是对 `DropRuleService` 今天会不会掉时装的测定。

**这条缺陷暴露的方法缺口，比缺陷本身更该记下。** 此前的反向扫扫的是**判定点**
（一条拒绝在哪、有没有判据会红），**没有扫写入方**（谁能把系统送进那条拒绝）。
于是清单把「装载时六列取不到被拒」那条记作「有覆盖」，读起来像已经照看过了，
**而它当时只覆盖了「背包内拖动」那一个制造者，「地面拾取」这个制造者从头到尾没有被枚举。**
清单第三版为此新增了 **3.7 节「制造者扫」**：按一条可复核的枚举口径把 **13 个写入方**逐个判过，
问的是同一个问题：**它能不能造出一行「kind 8 但六列为空」的时装，有没有判据覆盖。**

**那 13 行的口径覆盖到哪、排除了什么，一并记在这里**（第二轮补记，由 TASK-10 的独立评审指出）：
能往世界表 `items` 写那六列的成员，在 `Server/src` 下今天读到**三族**——
**容器一族**经 `StoreBackedItemContainer.WriteOneAsync`（清单表 #1–#11）、
**入场存盘一族**经 `InsertOwnAsync`／`UpdateOwnAsync`（#12–#13），
以及 **`SqliteWorldItemTable.InsertAsync`／`UpdateAsync`**——
**第三族被明写排除，不在那 13 行里**。排除的理由只有一条：**它今天在 `Server/src` 里
零调用方、零 DI 注册**（`grep -rn "IWorldItemTable" Server/src --include=*.cs` 只读出接口声明
与实现的类头两行；`grep -rn "SqliteWorldItemTable" Server/` 在 `Server/src` 下只有它自己，
把它 new 出来的是 `WorldAndSlotTableTests` 与 `CreatureIdentityFitsTheWireTests` 两个测试文件；
`PersistenceModule` 注册的是 `ICharacterItemTable`、`IAccountCargoItemTable`、
`SqliteItemDomainStore`／`IItemDomainStore`）。
**这一族不是本轮新发现的**：本文件上面「已知缺陷与覆盖缺口」一节早已记着它，
那是**上一个成果 `items-own-their-identity`** 写下的，处置是「有意留下的守卫缺口」；
上面三条 grep 说明**它今天仍是那个状态**，本轮既未改它，也未把它列为新缺陷。
**它写得出那个形状**（`BindAvatarColumns` 在 `avatar is null` 时把六列绑成 NULL），
所以**谁第一次给它接上生产调用方，那份 13 行的清单当天就变成 15 行**，
而进来的那两行没有闸门也没有判据——这正是上面那条已知缺陷说的「谁接线谁补守卫」。

那一节的结论是：**在那 13 行之内，除已修掉的两个（背包内拖动、地面拾取）之外，
没有找到第三个既同型又今天能被时装走到的写入方**；该节同时逐条写明了这个枚举口径挡不住什么。
被排除的那一族今天走不到，**加不进那三条小结的任何一个集合，结论不因它而变**。

**没做的那一件：时装仍然穿不上。**

不是没写代码，是**槽号一格都没有出处**。`PvfEquipmentSlots.ByName` 的 16 个键里，
以 `avatar]` 结尾的是 **0 个**（口径：把上面「已知缺陷与覆盖缺口」第一条里点名的那 16 个键逐个取末尾七个字符比对；
`[title name]`、`[support weapon]`、`[creature]` 这些都不以 `avatar]` 结尾）。
于是 `PvfEquipmentSlotDefinitionSource.GetAsync` 对一件时装答 `null`，
`MoveItemSpaceCommandHandler.PutOnAsync` 拿到 `null` 就 `Refused(NotWearable, piece.ItemId)`。
**本轮没有给这张表补任何键**（该文件不在本轮的改动清单里，`git status --porcelain` 里不出现）——
本轮一个槽号也没量出来，补键就等于写下一个没有出处的数。

**本轮留下的是量具，不是修法。** `MoveItemSpaceHandler.ResolveAsync` 的拒绝记录现在写作
`item {ItemId} {From}->{To} refused as {Status}`：`{ItemId}` 是被拒的模板号，
`{To}` 是**客户端自己写下的**目标槽号。观测：`RefusedEquipIsRecordedWithItsTemplateTests` 的三条整句比对判据。
一次实机拖拽就能从这一行读出「哪个模板 → 哪个槽」。

**槽号出处：本轮找过三处，逐处写结论。**

1. **档案里时装名后面那个数不是槽号。** `[equipment type]` 的形状是「一个名字，后面一个数」
   （`[shoulder] 13`）。本轮把 `equipment/equipment.lst` 的 **118,479** 个模板逐个打开、
   读各自文件里第一处 `[equipment type]`：**11 个以 `avatar]` 结尾的名字，后面那个数的取值集合是 `{0}`**，
   样本是这 11 个名字下的 66,328 个模板，逐个读过（其中后面不跟整数的模板 0 个）。
   同一次扫描里 `[weapon]` 后面那个数取到 10 个不同值、`[coat]` 10 个、`[ring]` 7 个。
   **一个取值集合只有 `{0}` 的数，读不出 0..11 这十二格里的哪一格。**
2. **`protocol/` 里，本轮没有找到一份说「量到了一件穿在身上的时装」的记载。**
   检索口径：在 `protocol/` 下按「时装」二字检索，命中 **19 个文件**，逐个读过；
   其中四条记载明写「没量到」，分处三个文件，逐字抄下：
   - `protocol/game/server-to-client/NOTI-0x0002.md`：「到这一天为止，本仓的角色从来没有穿戴任何东西，
     `equipCount` 恒为 0，这条路径因此从未上过线。」
   - 同文件稍后：「**注意**：A21 参照里槽位 11 没有物品，这一条来自上游
     `EquipmentTypeInfo.IsCostumeBarSlot`，不是量出来的。」
   - `protocol/game/server-to-client/NOTI-0x000D.md`：「A21 的时装条目未被观察到，
     其珠宝槽块与这段零的先后是从 A12 沿用的，不是量出来的。」
   - `protocol/patterns/character-lifecycle.md` 的「未解问题」表里有一行：
     「有外观条目的角色如何编码 | 中：我们恒发 0 条 | 给已建角色穿一件时装」。
   那 19 个文件里，写出角色身上有哪几格的是
   `protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md`，
   它列的三格是 12（武器）、14（上衣）、16（下装），三格都不在 0..11 内。
   **本轮没有逐文件穷尽 `protocol/` 的 1,819 个文件**
   （口径：`find protocol -type f | wc -l`），所以这里只写上面这几条记载的读数。
3. **上游转写只到「带宽十二格」与「槽 11 叫什么」，而且自陈不是量出来的。**
   `NOTI-0x0002.md` 写「**时装槽的边界是 11，不是 10。** A21 在武器之前插了 `AuraSkinAvatar`（槽位 11）」，
   紧接着就是上面抄的那句「不是量出来的」。
   **它给了 0..11 这十二格的边界，没有给「11 个类型名 → 12 个槽号」的对照。**

**`PvfEquipmentSlots.ByName` 今天认不出的类型名：17 个，覆盖 66,596 个模板。**

计数口径：把 `equipment/equipment.lst` 读成「id → 相对路径」（本轮实测 118,479 行、
118,479 个互不相同的 id、118,479 条互不相同的路径），逐个打开 `equipment/<路径>`，
取文件里第一处 `[equipment type]` 后面那个字符串，按 `OrdinalIgnoreCase` 拿去 `ByName` 查，
查不到的按名字归并计数。这一遍里打不开的文件 0 个、读不到类型名的模板 0 个，
读到类型名的模板 118,479 个。
**测法**：把 `Server/src/Dfo.Pvf` 的 11 个源文件原样复制到工作区之外，写一个只调
`PvfArchive.Open`／`FindFile`／`OpenScript`／`GetTag`／`ResolveString` 的一次性程序跑一遍；
取标签与取值那两步与 `PvfEquipmentSlotDefinitionSource.Read` 逐句相同
（同一个 `[equipment type]`、同一组 `TryMovePast` ＋ `TryReadSectionValue`）。
**工作区里的文件一个都没有被改动**，这一段的数与判据无关，重跑要另写程序。

| 认不出的名字 | 模板数 | 是不是时装名 |
|---|---:|---|
| `[coat avatar]` | 8,391 | 是 |
| `[hat avatar]` | 8,325 | 是 |
| `[pants avatar]` | 8,194 | 是 |
| `[shoes avatar]` | 8,070 | 是 |
| `[hair avatar]` | 7,929 | 是 |
| `[breast avatar]` | 7,565 | 是 |
| `[face avatar]` | 7,028 | 是 |
| `[waist avatar]` | 7,000 | 是 |
| `[skin avatar]` | 2,954 | 是 |
| `[weapon avatar]` | 449 | 是 |
| `[aurora avatar]` | 423 | 是 |
| `[artifact red]` | 65 | 否 |
| `[charm]` | 50 | 否 |
| `[artifact blue]` | 47 | 否 |
| `[flag]` | 47 | 否 |
| `[artifact green]` | 31 | 否 |
| `[name tag]` | 28 | 否 |

复算：11 个时装名相加 **66,328**；6 个非时装名相加 **268**；两数相加 **66,596**；行数 11 ＋ 6 ＝ **17**。
加法的每一项都在上面那张 17 行表里，可当场重算。

**这里曾经差 5，来历已查明：那是一次加法错误，不是 5 个模板行。**
本 Spec 的中间取证文件 `.rks-agent/tasks/server-agrees-what-an-avatar-is/findings-archive-measurement.md`
把这两个数写作 66,323 与 66,591，各比这里少 5。错在哪已经落到句子上：
**同一份文件的表与它自己的结论句对不上**——该文件的探针输出原文逐名列的 11 个数是
423／7565／8391／7028／7929／8325／8194／8070／2954／7000／449，**这 11 个数相加是 66328**，
与本轮按上面口径测的两遍、以及 QR-07-1 评审方另写探针跑的第三遍**逐个相同**；
而同一个文件的结论句写成「这 11 个名字覆盖 66,323 个模板」。**表是对的，小计算错了。**
计数口径与旁证：非时装六项 65＋47＋31＋50＋47＋28 ＝ **268**，两版相同，
**5 的差额整个落在时装小计这一项**（66,591 − 66,323 ＝ 268 ＝ 66,596 − 66,328）；
另与本文件上面「已知缺陷与覆盖缺口」里早已记着的「66328 个时装模板」相符。
所以按上面那一遍 118,479 个模板的口径，**档案里并没有另外 5 个时装模板可数**——
差的是一个写错的小计，不是 5 个能被打开的模板文件。
错值后来被下游照抄，本轮点到的落点有两处：`TaskContract.md` 与 TASK-07 的验收条件第 4 条
（本轮没有逐文件穷尽本 Spec 的中间记载，所以这里只写点到的这两处）。
**订正已经落好**：契约原写的 66,591 已由 **Contract Amendment C-3** 订正为 **66,596**（其中时装 **66,328**），
`findings-archive-measurement.md` 已补记 **修正案 F-1**；两处都保留原文、另起一段订正。

**这一处留下的教训比那个数本身有用。** 那份取证文件记的是档案侧那一遍穷尽扫描
（118,479 个模板逐个读过），它同时给了表和小计；
**而上面点到的两处下游（`TaskContract.md`、本任务验收条件第 4 条）照抄的都是它的结论句，不是它的表**
——读的人看不见加法，也就没有机会发现小计错了。所以：
**一个由多项相加得出的数，要么当场把加法写出来（各项、项数、和，三样齐），要么就不要写这个小计。**
上面那张 17 行表加紧跟其后的那行复算，就是按这条写的。
按 D-07 的处置意见，这个错能被逮住，是因为上一轮没有照抄契约的 66,591，而是把三个数并列写了出来。

**`ByName` 的 16 个键里，有两个在这次扫描中一次都没被用到**：`[title]` 与 `[earring]`——
118,479 个模板读到的类型名里，按 `OrdinalIgnoreCase` 与这两个键相等的是 **0 个**。
（称号在档案里走的是 `[title name]`，1,796 个模板；24 号槽走的是 `[support weapon]`，26 个模板。
补一个数：`PvfEquipmentSlots.ByName` 里槽号写作 **24** 的键有三个——`[earring]`、`[support weapon]`、
`[creature]`，其中 `[creature]` 的模板数是 **707**。这 707 的出处是 QR-07-1 评审方那一遍探针，
与 `findings-archive-measurement.md` 的类型名表、以及上一个成果 `items-own-their-identity` 记下的
`Avatar=66328 Creature=707 Equipment=51444 sum=118479` 三处相同；
本轮执行者那一遍只统计了 `ByName` **认不出**的名字，没有单独复算它。
所以「`[earring]` 一次都没被用到」说的只是这一个键，不是 24 号槽在档案里没有东西。）
**这不是删它们的理由**：`PvfInitialEquipmentDefinitionSource` 也读这张表，
它读的是创建角色那张表里写的名字，不是 `[equipment type]`——本轮没有测过那张表里出现哪些名字。

**给所有者的实机须知，三条。**

1. **看到 `item 0` 不是缺陷。** 时装拖拽这一帧，客户端要在「源列号／目标列号」两处写数。
   若它把目标列写成 `Avatar`（＝1）而不是 `Equipment`（＝3），请求就到不了
   `MoveItemSpaceCommandHandler.PutOnAsync`，而是更早地在 `HandleAsync` 的
   `if (command.SourceListType != MainListType || command.DestinationListType != MainListType)`
   这一句上被拒。那条路走的是**单参**的 `ItemMoveResult.Refused`，`ItemId` 位置是 0，
   于是记录读作 `item 0 …->N refused as ListNotServed`——**槽号仍在，模板号丢失**。
   **要量的东西是槽号**；一次只拖一件时，模板号由操作上下文可知，所以这行记录仍然管用。
   **证据类型：代码路径读出。真实客户端在这一帧里写的是哪个列号，本轮没有观测过**，
   这正是实机要回答的问题之一。
2. **「取下」那条路径不会说出模板号。** `MoveItemSpaceCommandHandler.TakeOffAsync` 的三个拒绝返回
   （`MainSlotAdmission.ItemUnknown` → `Refused(NotWearable)`、
   `MainSlotAdmission.SlotNotAllowed` → `Refused(ItemCannotOccupySlot)`、
   `displacedSlot != command.SourceSlotIndex` → `Refused(DisplacedCannotOccupySlot)`）
   写的都是单参重载，那时 `{ItemId}` 是 0；同方法更早的 `NoBoundContainer` 与 `BothSlotsEmpty`
   两个返回也是单参。**要量的那一次是「拖上去」，不是「取下来」。** 证据类型：代码级读出。
3. **一件时装的「剩余秒数」那一格应当画出约 11.4 年，不是约 68 年。**
   `GameInventoryDelivery.WriteItem` 对一行 kind 8 写的第三个字段是
   `Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`
   ——**是差，不是 `ExpireDate` 本身**。本轮实算一遍（2026-09-06T09:32:11Z，`now` ＝ 1788687136）：
   `2147483647 − 1788687136 = 358796511` 秒；`358796511 ÷ 86400 = 4152.7` 天；
   `358796511 ÷ 31556952 = 11.37` 年（分母取儒略年 365.2425 天）＝ **约 11.4 年**，并且每过一秒少一。
   **本仓对这个数只用「约 11.4 年」一个说法**（＝ `11.37` 取三位有效数字）；
   **不要写成「十一年半」**——那是 `11.5`，与算式差约 0.13 年（约 47 天）。
   **看到十一年多，是这个占位在正常工作，不是缺陷**；看到约 68 年、或一个不合理的日期、
   或客户端拒绝渲染，那才是要追的。
   **这个数此前在本仓被写错过**（写成「约 2.1×10⁹／two billion」）。
   「约 68 年」对应的是 **`int.MaxValue` 本身**：`2147483647 ÷ 31556952 = 68.05` 年；
   而写下的那两位有效数字 `2.1×10⁹` 本身是 `2.1×10⁹ ÷ 31556952 = 66.55` 年。
   两者相差的倍数 `2147483647 ÷ 358796511 = 5.99`，**将近六倍**。
   订正见下面的修正案其五。证据类型：代码级读出 ＋ 一次当场的减法，读者可拿自己的钟重做。

**这一轮的影响面超出了时装：普通装备在背包里的拖动行为也变了，而且是变好。**

事实链，逐环点名（证据类型：**代码级读出**）：

1. `CharacterRosterService.EquipmentCore` 给起始装备写的是
   `Value: Random.Shared.Next(1, int.MaxValue)`；`GroundPickupSlice.CarryOverAsync` 在一件装备
   落进背包后，把该格写成 `Value = checked((int)found.Instance)`。
   **这两句写进 `Value` 位的都是一个身份，不是「有几个」**——起始装备走前一句，
   从地上捡起来的装备走后一句。
2. 在本轮**之前**，`ContainerSlotItem.Count` 写的是 `=> Core.Value`，无条件读同一位。
   于是这样一件装备在背包内被拖动时，`DecideMoveAsync` 走哪一支，取决于请求里的 `MoveCount`
   与那个身份相不相等（`MoveItemSpaceDecoder` 从 `body[7..]` 读那四个字节；`MoveItemSpaceSlice`
   自己 `<remarks>` 抄下的那次实机请求在这四个字节上是 `01 00 00 00`，即 1）。两者不等时：
   目标格空 → `StoreBackedItemContainer.DecideMoveAsync` 选 `Split`（把一件装备拆开）；
   目标格有东西 → 返回 `PartialSwapNotAllowed`（对调被拒）。
3. 本轮为修时装而把它改成 `Count => this.IsPile ? Core.Value : 1`
   （`IsPile => this.WorldClass is ItemWorldClass.Interchangeable`），
   **顺带把普通装备的这条毛病一起消掉了**：现在整件移动、整件对调。

**要说清三件事：**

- **这是变好。** 同一次拖动，此前落在「拆分」或「对调被拒」那一支，现在落在「整件移动／整件对调」那一支。
- **它不在本 Spec 原本列出的行为变化里**，是同一处修复强制带出的。要保留旧行为，
  反而得给时装开一个特例，那会让普通装备继续坏着。
- **证据类型：代码级读出，未实测，也未实机。** 上面三步都是从工作区文件的语句文本读出的；
  本轮没有一条判据把一件带着抽出来的 `Value` 的普通装备送进 `MoveToAsync`
  （清单 3.2 的 RV-23 与 3.3 的 RP-08 都记作「无覆盖」，凭据是 MU-20 的 3 条红与 MU-21 的 22 条红
  逐条列出之后没有这样一条）。
  **所以：所有者实机测普通装备时若看到与线上老服不同的拖动行为，那不是本轮新引入的缺陷。**

**这一条是怎么被发现的，要一并记下：五份记载都没发现它。**
清单第一版、QR-06-1、TASK-08 的执行结果、QR-08-1、控制器处置 D-08——这五份都把它当成时装专属的问题。
它是 TASK-06 第二轮重跑反向扫时**从代码里扫出来的**，不是从上面点名的那五份记载里读出来的。
**它本身就是「反向扫为什么必须存在」的例证。**

#### 这一轮补记的五条修正案

按本仓规矩：**改已被证伪的记载要补记修正案，不是抹去原文。**
（前四条随本轮主体一起写下；**其五**是完整变更集独立评审指出后补记的。）

**其一——`AGENTS.md` 自己用行号索引，那是错的。** 上面世界表小节那句
「该谓词文本在 `Server/src` 今天出现在 4 处 SQL 里」，**原文用的是行号**，写作
`InventoryItemTables.cs:229`、`:264`、`SqliteItemDomainStore.cs:414`、`:544`。
前两个数至今仍对；后两个**在本轮基线 `565d33fe` 上就已经不对**——基线实测是 **438** 与 **568**
（口径：`git show 565d33fe:Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs`
逐行检索 `substr(item_core, 6, 4)` 与 `substr(standing.item_core, 6, 4)`，各命中一行）。
这一轮里它们又动过两次：TASK-01 落地后快审 R-01 测得 **453** 与 **595**，
交付态本节再测是 **468** 与 **642**。**同一对数在一轮之内漂了三次。**
**这正是本仓「按成员名索引、不按行号」那条规矩自己被违反的实例**，也是那条规矩最便宜的例证。
上面那句已改成按成员名索引。**这是基线就有的缺陷，不是本轮弄坏又修好的**——
本轮开工之前它就已经指错了行。

**其二——时装的 8／1 不一致，已经不在了。** 上面「已知缺陷与覆盖缺口」里那条
「**时装的种类不一致仍在**：存储记 8、档案给 1……**四处生产注释**点着它」，
**在写下时为真，今天不成立**：`WornPieceMetadata` 已改答 8（见上面「做成了三件事」第 1 条）。
原文不删。它点名的那四处生产注释本轮各自补记了修正案、也各自保留了原文，分别在
`ItemPlacementContent` 的 `ItemWorldClass`、`IItemPlacementRuleService.ClassifyAsync`、
`ItemPlacementRuleService.ClassifyAsync`、`PvfEntryLifecycleContentSource.WornPieceMetadata`。

**其三——那条潜伏的存盘矛盾，随根因一起消失了。** 同一节里
「**入场存盘的时装守卫，与发放路径的写入方是矛盾的（潜伏，今天不可达）**」那条，
原文自己写着「根因是下面那条 8／1 不一致；修了它这条自然消失」。根因已修：
`ItemContainers` 这条路现在写 8，`InventoryEntryAssembly.SaveAsync` 那道
「档案说是时装、行却不是 kind 8」的拒绝对发放来的行不再命中。
观测：`GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`
走的正是「发放 → 存盘 → 再载入」这一趟。原文不删。

**其四——一条无出处的译名。** `PvfEquipmentSlots` 的类型级 `<remarks>` 写着
「A21 inserted the aurora-illusion avatar ahead of the weapon」。
**`aurora-illusion avatar` 这个说法在本仓找不到出处**，它把三样东西拧成了一样：

| 说法 | 出处 | 本轮实测 |
|---|---|---|
| `AuraSkinAvatar` | `protocol/game/server-to-client/NOTI-0x0002.md` 与 `protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md`，两处都写这个名字：前者紧接着自陈「不是量出来的」，后者写「槽号出自上游 `EquipmentType`」 | 底层记载说的是这个名字 |
| `aura skin avatar` | `ClientHack/tools/literals-a21.csv` 第 27941 行，客户端二进制里的一条字面量 | 档案里以它作 `[equipment type]` 的模板 **0 个** |
| `[aurora avatar]` | 档案 `equipment/*.equ` 的 `[equipment type]` | **423 个模板**，后面那个数取值集合 `{0}` |

（这三行的计数口径与上面那张十七行表相同，是同一次扫描的读数。）
**三个是三样东西**：一个是上游的枚举名，一个是客户端二进制里的字面量，一个是档案里的类型名；
`aurora-illusion avatar` 哪一个都不是。**按仓规不改那段注释的正文**，本条即是它的修正案。
它同时说明了槽号为什么补不上：**这 11 个时装名里哪一个坐在槽 11，本轮未确立。**

**其五——「剩余秒数约二十一亿」是错的，一次减法就能证伪。**
**原文两处，逐字抄下，不删**：

| 出处 | 原文 |
|---|---|
| `behavior-change-list.md` 3.3 的 RP-06 | 「第三个字段是 `Math.Max(0, int.MaxValue - now)`（**约 2.1×10⁹**）而不是落地计数」 |
| 生产注释 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` 的 `ArrivingAvatar.NeverExpires` 的 `<remarks>` | "Nobody has yet watched a client draw an avatar whose remaining seconds are **two billion**" |

**错在哪**：两处都把 `int.MaxValue` 本身当成了上线的数，而写上去的是
`ExpireDate - now`——**是差，不是被减数**。
**复算（本轮当场做的一遍，读者可自行重做）**：

```text
int.MaxValue         = 2147483647
now（unix 秒）        = 1788687136     ← 2026-09-06T09:32:11Z，`date +%s`
差                    = 358796511 秒
358796511 ÷ 86400    = 4152.7 天
358796511 ÷ 31556952 = 11.37 年       ← 分母取儒略年 365.2425 天
```

即 **约 3.59×10⁸ 秒 ≈ 11.4 年**（＝ 上面算式末行 `11.37` 取三位有效数字）。
作为对照，**「约 68 年」对应的是 `int.MaxValue` 本身**：`2147483647 ÷ 31556952 = 68.05` 年；
写下的那两位有效数字 `2.1×10⁹` 本身是 `2.1×10⁹ ÷ 31556952 = 66.55` 年。
两者相差的倍数：`2147483647 ÷ 358796511 = 5.99`，**将近六倍**。
这个数每过一秒少一，所以它不是常量，上面写的是**一次带时刻的减法**。

**指出者**：完整变更集独立评审的非阻断观察（`reviews/change-review.md` 第七节）。
**订正落在两处**：那段生产注释保留原句、另起一段写明它错在哪并附同一条算式
（**只改注释措辞，没有改任何可执行代码**）；清单在 RP-06 那一栏就地标注，并把修正案全文
记在它第五节的 **NB-1b**。上面「给所有者的实机须知」第 3 条写的就是订正后的数。

**这个错与其一那条「差 5」同类，共因也一样**：
**一个读者一次算术就能证伪的数，被写下时没有把算式一并写出来。**
本仓那条规矩（「一个由多项相加得出的数，要么当场把加法写出来，要么就不要写这个小计」）
在这里推广一次：**由算术得出的数，都要把算式一并写出来。**

**这条修正案第一次交付时，自己犯了它要修的那件事的小号版本。** 它修的是
「同一个数被写成两个说法」（`2.1×10⁹` 与 `3.6×10⁸`）；而它第一次写下时，
**把订正后的数也写成了两个说法**——本节与清单 NB-1b 写「约 **11.4** 年」，
上面「实机须知」第 3 条的标题与清单 3.8.2 的所有者段落却写「约**十一年半**」，
生产注释里是 "roughly eleven and a half years"。复算给的是 `11.37`，
而「十一年半」是 `11.5`，**差约 0.13 年（约 47 天）**。
由 TASK-10 的独立评审指出，第二轮已统一为「约 11.4 年」／"about 11.4 years"。
**这是本成果第五次栽在「表是对的、小结或复述算错了」上**，前四次已各自订正。
**由此多加一条**：**一个数被订正之后，它的每一处复述都要照着算式抄，
不要用口语约数替换**——「十一年半」「六十八年」这类写法，正是让原错传开的那种写法。

### 判据偶发失败：先取身份，再谈别的

有一条已知的 SQLite 并行释放偶发。**判别依据是三样一起看：**

1. 异常类型 `ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`
2. 复跑即绿（**要真的跑**——某些机器上三次全量能红两次）
3. **栈根在下面这份清单内**

已记录的栈根（三条）：

| 栈根 | 见于 |
|---|---|
| `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar` | `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`、`DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`、`DarkKnightComboBookTests.TwoBooksAskedForTheSameCharacterAreTheSameBook` |
| `ItemPathFrameTests.Storage.ReadAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |
| `OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |

**用例名不是判别依据**——第一条栈根已经在三个不同用例名上出现过。
**栈根是**：清单外的新栈根**不得直接归入**，要另给不归因的明证（改动与该路径无交集等），
确认之后把新栈根追加进这张表。

**取不到身份的失败一律是取证缺口，不是结论。** 所以跑全量判据时把完整输出落盘
（`--logger "console;verbosity=detailed"`），不要用会滤掉明细的过滤器——
这条规则是因为同一个成果里连续四次遇到失败、四次都没取到身份才写下来的。
已知的取证缺口两条：`Dfo.Server.Networking.Tests` 的一次未记名失败、
`DarkKnightComboBookTests.ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead`。

### 其他与主设计的偏差（诚实清单）

- `CLIENT_SPEC_STATISTIC`(0x00AB) 的 handler 直接打语义化日志，**不经过命令总线**
  （纯遥测、无领域变更、无响应；见 protocol 记录的 Implementation mapping）。
  存在水命令：`AcknowledgeGameExitCommand` 返回常量；Channel Connect 已改为纯协议
  直接响应。ScriptVersion、Script、Info 三个命令曾直接读基础设施内容源，现已改为经
  `IChannelDirectoryDomainService` → `IChannelContentRuleService` 取得，目录与脚本同一个所有者。
- 登录失败目前**无法编码**（sender 对失败体直接抛异常），失败以服务器内部
  异常形态存在，客户端看不到错误。
- **协议 handler 层普遍没有判据守着。这是个模式，不是个例。** `Dfo.Server.Protocol` 里有 48 个
  `ClientMessageHandler<>` 子类，其中 **19 个**的类名在 `Server/tests` 全域一次都不出现：
  `AcceptQuestHandler`、`CheckCharacterNameHandler`、`ConnectionCheckHandler`、
  `ContentsPlayInfoHandler`、`CreateCharacterHandler`、`DeleteCharacterHandler`、
  `EnterSelectDungeonHandler`、`GetAccountCharacterRosterHandler`、`GetItemHandler`、
  `GiveUpGameHandler`、`HiddenCharacterInfoHandler`、`MapEntryReportHandler`、
  `MercenaryInfoHandler`、`MoveMapHandler`、`SaveGameOption1Handler`、`SecurityStatusHandler`、
  `SetQuestTriggerHandler`、`StoryPauseHandler`、`UnansweredReportHandler`。
  缺口长的样子是固定的：**把一个 handler 整段退回读陈值，全量判据仍然全绿**。
  已经实测撞到过三处，每一处都是先发现缺口、补上一条会红的判据之后才有的红：
  `MonsterDeathHandler`（`MonsterDeathHandlerTests.cs`）、`SetUserAreaHandler` 与
  `SetUserPositionHandler`（`TownProjectionHandlerTests.cs`）、`SelectDungeonHandler`
  （`SelectDungeonHandlerTests.cs`）——后三个 handler 在这些用例之前从未被任何用例构造过。
  **所以动 handler 之前先查它有没有判据；多半没有，那就先补一条会红的再动它。**
  「快速开发」那节写的「测试三件套」是规定，不是现状。
- `Dfo.Server.Utilities` 是空项目占位。

## 构建与运行

- SDK：`global.json` 锁 10.0.302。**本机只装 10.0.201 时**，从仓库外目录构建
  可绕过（global.json 按 cwd 向上查找）：
  `cd D:\project\DNF && dotnet build DNF-JP-86-Server/Server/86JP.Server.slnx`
- 运行 Host（content root = exe 目录）：
  ```bash
  cd Server/src/Dfo.Server.Host/bin/Debug/net10.0
  NETWORKING__ENABLED=true ./Dfo.Server.Host.exe
  ```
  `Networking:Enabled` 出厂 false（只注册 DI 接缝不开监听）；打开后监听
  `ChannelPort` 7001 + `GamePorts` [10011]。其余关键键：
  `GameLogin:*`（字节级嵌入登录成功体）、`Database:Path`（SQLite 自动建表）、
  `Pvf:ArchivePath`（Script.pvf **懒打开**，缺失也能启动）、
  `ChannelEntry:*`（受限频道/等级门槛等 legacy 默认）。
- 测试：`dotnet test`；PVF 相关用 SkippableFact（无档案时跳过）；
  Host.Tests 会拉起打包后的 exe 做真实 TCP 验证。
- **拉真实进程或真实 socket 的用例在满载全量下会假红。** `Host.Tests` 与
  `Networking.Tests/Integration/` 各条都用 10 秒预算等进程退出或等连接就绪，
  机器被占满时会撞线，报的是 `TaskCanceledException`/`OperationCanceledException`
  而非断言不符。判据是单独跑该项目：隔离下 `Networking.Tests` 46 条 1 秒跑完，
  全量下同样 46 条要 10 秒。看到这个形状先隔离复跑，不要当缺陷查。
- **两个用例同时「先读后写」同一个库，第二个会等满 30 秒再失败。** 一次用例一条连接、
  一个 deferred 事务，这让用例内的多次读看到同一时刻，也让长读不挡写。代价是：
  用例 A 与 B 都先读、A 先提交之后，B 再写时它手里那份视图已经过期，SQLite 拒绝在其上写。
  等待帮不上忙——过期的是时刻不是锁——但从连接看这就是「库忙」，于是它按
  `Microsoft.Data.Sqlite` 的默认 `Default Timeout` 等满 30 秒才抛。
  今天没有任何地方设过这个超时，也没有任何地方在 BUSY 上重试。
  真正的解法是「整条用例重来」，属于派发用例的那一层，不属于数据库。
  单人调试撞不到；同账号有闸，跨账号（登录、建号这类先读后写的命令）能真实撞上。
  在此之前，第二条用例的调用方会先卡 30 秒再收到失败。

## 快速开发：新增一个协议的固定动作

1. `protocol/<endpoint>/<direction>/<type>-<name>/README.md` 按模板建记录，
   登记 `protocol/catalog.md`（逆向证据先行，布局确认后才可具名字段）。
2. `Dfo.Server.Data/Networking/NetworkMessages.cs`：只加
   `ClientMessageType`/`ServerMessageType` 的目录成员。**消息 record 不放这里**
   ——那里只留协议身份契约。目录可以先于接线存在，不会导致启动失败。
3. 新建切片目录 `Dfo.Server.Protocol/<Endpoint>/<Protocol>/`，按需放入六类制品
   （见"切片边界规则"）：`Messages.cs` → payload codec → 解包器 → 编码器 →
   消息 handler → 命令与命令 handler。业务逻辑留在领域服务里。
4. `ProtocolModule` 加四类注册行——入站解包器（线级键 + 声明可产出的目录成员）、
   出站编码器（消息 CLR 类型 + 允许端点）、命令（ExecutionKind + AccessMode）、
   消息 handler（目录成员）。**这是唯一需要改的接线点**；传输侧不动，
   `ApplicationModule` 不动。
5. 测试三件套：codec 向量、handler/命令单测、Networking.Tests 线级集成测试。
   注意测试仍按功能组织，尚未跟随切片重排。

## 约定

### 接口后缀标明类别

`Abstractions` 里的接口后缀就是它的类别，不需要打开实现才知道调用它意味着什么：

| 后缀 | 含义 | 调用它意味着 |
|---|---|---|
| `*DomainService` | 领域服务 | 读写世界状态，**次序与并发有意义** |
| `*RuleService` | 规则服务 | 内容 + 传入的 facts + 显式种子 → 值/决定，无状态。**内容源与规则配置的唯一读取点**；由业务服务调用，不由 handler 调用 |
| `*Projection` | 投影 | 单个 `Build(存储记录) → 领域值`，无依赖、无 I/O |
| `*Rules`（静态类） | 纯规则 | 纯函数，不碰 I/O。**只由规则服务调用**，是规则服务的下一层而不是它的替代 |
| `*Repository` / `*Registry` / `*Directory` / `*Persistence` | 持久化与运行时注册表 | 各自后缀已表明。**不得注入命令处理器** |
| `*DomainStore` | 领域 store | 领域状态的事务结算真源，被要一个会话、由事务收口。是命令处理器可注入的两种形状之一 |
| `*DefinitionSource` / `*ContentSource` / `*Catalog` | 内容源 | 读归档，确定性、可缓存。**只被规则服务注入** |
| `*Policy` | 策略 | 规则服务的一种，回答"允不允许"；`IChannelEntryPolicy` 即此类 |
| `*Query` / `*Reader` | 只读投影/查询 | 只读，不改世界状态 |
| `*Decoder` / `*Encoder` / `*Sender` / `*Dispatcher` / `*Handler` / `*Observer` / `*Closer` | 传输与协议 | 各自后缀已表明 |
| `*Module` | 组合根接线 | `IServerModule`，只注册不执行业务 |

判断一个进程内容器属于哪一边，看**接口说的是领域动作还是容器动作**，而不是看它内部有没有用字典。会话、在场、摆摊、PvP 大厅四处都在内存里保存运行态，但它们对外只回答领域问题（谁在这个区域、这个角色有没有摊开着、哪些房间已公告），所以它们是领域服务；字典是实现细节。反过来，一个对外暴露「取出来、改一改、塞回去」的类型不会因为改个名字就变成领域服务。

判据：**这块状态参不参与别的聚合的不变量**——参与就放进那个聚合，不参与才自己一个服务。
规则服务**不许自己查世界状态**，只能接收 facts；一旦它自己去查，它就成了世界的第二个所有者。

唯一的例外是 `IServerConfigService`（`T GetConfig<T>(string key)`）：它是宿主配置访问，
三类都不属于，**刻意保留原名**。

**本约定的范围是 `Dfo.Server.Abstractions`。** 这里曾经记着五个 `Application` 内部自用的
`I*Service`「不跨层暴露、尚未纳入分类法」。那句话与代码不符：`Protocol` 的命令处理器一直在跨层
注入它们。它们现在是 `Abstractions` 里带 `DomainService` 后缀的正经接缝
（`ISelectedSessionCleanupDomainService`、`ISelectCharacterSessionEntryDomainService`、
`ISelectCharacterPreparationDomainService`、`ISelectCharacterQuestEntryDomainService`、
`ISelectCharacterPetEntryDomainService`），实现仍留在 `Application`。

*教训*：一句「不跨层」的记载不会阻止跨层，只会让下一个读它的人以为没有跨层。所以命令处理器
那条注入规则现在由判据守，而不是由这张表守。

设计依据与推导过程见 `Docs/领域结构重构设计.md`。

- `ValueTask` 全覆盖；sealed handler + 主构造器 DI；命令与 handler 同文件共置。
- 逆向出来的 legacy 行为在代码里用 XML doc remarks 说明来源，不凭记忆。
- 协议结论变更必须同步 `protocol/` 记录与 catalog 状态；推翻旧结论要保留
  "为什么被推翻"（见 `protocol/README.md` 安全边界一节）。
- 分层问题主要看项目引用图。唯一的分层判据是
  `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`，
  它只覆盖**命令处理器**的构造器注入面；普通消息 handler 的能力清单仍然只写在本文档里，
  没有判据守着。
