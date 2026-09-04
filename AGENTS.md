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
4. `commandBus.SendAsync(...)` 与 sender 的单连接或显式收件人发送方法。

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
