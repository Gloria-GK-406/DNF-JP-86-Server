# 架构与分层

修改项目依赖、领域职责或接口形状前阅读。协议用例的具体约束见
[协议 CMD 开发指南](ProtocolCommands.md)，重构判断见 [RefactorPreferences.md](RefactorPreferences.md)。

## 项目与依赖

解决方案 `Server/86JP.Server.slnx`，共享配置 `Server/Directory.Build.props`
（net10.0 / Nullable / **TreatWarningsAsErrors** / Deterministic），中央包管理
（TouchSocket.Hosting 4.3.1、Microsoft.Data.Sqlite、xunit）。

| 项目 | 依赖 | 职责 |
|---|---|---|
| `Dfo.Pvf` | — | 只读 PVF 档案库（cipher/zlib/索引/token），无游戏概念 |
| `Dfo.Server.Data` | — | 纯模型：领域记录 + 协议身份契约（`Networking/NetworkMessages.cs` 只留两个枚举、两个消息接口、两个帧元数据 record） |
| `Dfo.Server.Abstractions` | Data | 接缝层：`IServerModule` + 各领域服务/仓储/内容源/网络契约 + 通用只读服务器配置契约 |
| `Dfo.Server.Application` | Abstractions, Data | **领域编排核心**：玩法执行器与发件箱、命令总线与显式工作单元、消息分发机制、领域服务 |
| `Dfo.Server.Infrastructure` | Dfo.Pvf, Abstractions, Data | PVF 内容源 + 频道脚本 + 启动配置内存快照 |
| `Dfo.Server.Persistence` | Abstractions, Data | SQLite 仓储与存储 + `Schema/` 唯一建表归属者（只支持当前结构版本，旧库由 `Tool/DbMigrate` 迁移，见 [数据库结构版本与迁移](DatabaseSchemaMigration.md)）+ `Database/` 事务参与者。**全部类型 `internal`**，对外只经 Abstractions 契约 |
| `Dfo.Server.Networking` | Abstractions, Data | TouchSocket **传输**：帧适配/解析、TCP 服务、插件、出站编码器、发送端（每连接发送队列、交付期限、队列上限、失败即关连接）、注册表。收发路径上不含任何单个协议的知识；发送端也只表达必达/尽力、跨连接的交付顺序和逐目标失败隔离 |
| `Dfo.Server.Protocol` | Abstractions, Application, Data, Networking | **协议切片**：一个协议一个目录，含其消息、解包器、编码器、消息 handler、命令与命令 handler，以及登记它们的 `<切片>Registration`；`ProtocolModule` 按序调用各切片 |
| `Dfo.Server.HttpApi` | Abstractions, Application, Data | HTTP 组件：Kestrel 装配、路由、中间件、HTTP 就绪；保留 LauncherAuth 配置键 |
| `Dfo.Server.GmWeb` | HttpApi, Abstractions, Application, Data | GM HTTP 功能模块：管理员访问边界、查询/发放端点及 `/gm` 静态页面；前端源码在 `Web/gm-admin` |
| `Dfo.Server.GameServer` | Abstractions, Infrastructure, Networking, Protocol | 游戏组件：装配网络/协议，入口启动前预热内容，提供游戏就绪状态 |
| `Dfo.Server.Host` | 公共模块、HttpApi、GmWeb、GameServer | 唯一组合根：共享配置和服务注册；不直接引用 Networking/Protocol |
| `Dfo.Server.Utilities` | — | 空占位 |

**分层由项目引用图在编译期强制**：

- Host、HttpApi、GmWeb、GameServer 仍在同一进程、同一根 DI 中。组件不能自行 Build 第二个 Host/ServiceProvider。启动顺序与开关见 [Document/host-component-composition.md](host-component-composition.md)。

- `Data` 不引用任何项目；`Abstractions` 只引用 `Data`。
- `Networking` 只引用 `Abstractions` 与 `Data`——它**不引用 `Application`，也不引用
  `Protocol`**。传输不知道有哪些协议，只知道注册表里有什么。
- `Protocol` 引用 Abstractions、Application、Data、Networking。方向单向：`Protocol → Application`，因此
  **`Application` 永远不能引用 `Protocol`**。任何要同时看到"某个具体消息"和
  "某个领域服务"的代码，只能落在 `Protocol` 里。
- `Application` 不引用 `Infrastructure`/`Persistence`/`Networking`。
- `Persistence` 与 `Infrastructure` **互不引用**，且 `Persistence` 不引用 `Dfo.Pvf`。
  持久化要的三处内容侧事实（地下城经验策略、技能点表、物品到达规则）是 `Abstractions`
  里的接口，由组合根注入实现。这是两者能各自成项目的原因。
- 但 `PersistenceModule` 与 `InfrastructureModule` **必须同时注册**：前者有三处注册
  要后者提供的内容源，只装其一的容器解析不出可用服务。二者是并列不是可选。

测试的依赖例外：
`Dfo.Server.Application.Tests` 引用 Infrastructure 与 Persistence 是刻意的测试例外
（entry 规则要跑真实 PVF、载体等价性要跑真实 SQLite），它同时引用 `Protocol`
是因为测试按功能组织。
`Dfo.Server.Persistence.Tests` 与 `Dfo.Server.Infrastructure.Tests` 按同一条线切开：
前者是 SQLite 与建表，后者是 PVF 内容、频道脚本与配置。

## 请求主线

一句话：**薄消息 handler 提交一个协议 CMD；CMD 在唯一玩法执行器中完成整个用例，
显式划出各段事务、调用领域服务并登记投影；发件箱在作业结束后交给发送端。**

```
TCP 字节 → Networking 帧适配（A21 入站 Game 14B／Channel 11B，出站 Game 15B）
  → 按线级键查 Protocol 解包器 → 本切片 payload codec → 强类型消息
  → NetworkMessageDispatcher 按消息注册调用薄 handler（执行器外）
  → handler 将原消息和精确 NetworkDispatchContext 交给 CommandBus.SendAsync
  → 唯一 GameplayExecutor FIFO 作业
  → 初始化发件箱 → 新 CMD scope → 显式 CommandExecutionContext / CommandRequestContext
  → ICommandHandler：身份／业务阶段／显式工作单元／补偿／投影顺序
      → 领域服务 → 仓储与存储；领域服务 → 规则服务 → 内容源
  → IGameCommandOutbox / ICommandMessageOutbox / ICommandProjections 即时编码并登记
  → 收口工作单元、request、scope 与失败 → 作业发件箱交给 OutboundSender
  → 每连接发送队列 → 网络
```

纯遥测只在收包回调中记日志，不进入 CMD；其它已注册消息均绑定协议 CMD。
Networking 不认识单个协议；传输等待本条处理完成后才读该连接下一帧。
当前模型及新增协议示例见 [协议 CMD 开发指南](ProtocolCommands.md)。

## 领域与规则

领域按目录组织：`Accounts/`（账号 entry 状态）、`Channel/`（入场策略）、
`Characters/`（花名册/entry 状态/入场后修正）、`Inventory/`（入场读取、物品生命周期、每日补货）、
`Quests/`（按角色取得的任务日志：在做的、已通关的、追踪选择）、
`Sessions/`（在场、选中会话、PvP 大厅、专家商店
四个领域服务，各自把运行态留在私有容器里；选中会话服务本身不存状态，读写在线身份）、
`World/`（出生点策略、世界内容）、`Users/`（`OnlineStateRegistry` 是在线身份：在线用户、
认证账号、选中会话及其阶段同在唯一那把锁后，`OnlineUserDomainService`、
`GameSessionEndpointDomainService` 与 `Sessions/SelectedSessionDomainService` 是读写它的门面）。
角色在线时“怎么样”（位置、血蓝百分比、物品冷却、灵药加成）统一经 `Characters/` 下的
`IOnlineCharacterRuntimeDomainService` 读写：按会话代数归属，不并入角色领域服务。位置由
`World/OnlineCharacterLocationDomainService` 唯一保存：站在哪个城镇区域与坐标是同一事实，区域广播要在同一把锁下
读到全员坐标；离城（选图、副本中）时记录不删，保留离开时的落点供回城和下线写库。它的写方法只供运行时调用，
其他代码经运行时改位置，经只读的 `ITownAreaDomainService` 查区域成员。位置记录不依赖任何服务：
组队服务按需读取队员所在区域（队伍列在队长所在的共享区），不保留位置副本，也不接收推送。
服务只做业务；`Persistence` 提供仓储与存储实现，`Infrastructure` 提供 PVF 内容源实现。

**三层是上下关系，不是三选一。**

```
协议 CMD handler     拥有用例阶段、事务与投影顺序，调用领域业务能力
  → 业务服务         （领域服务）读写世界状态，编排用例
    → 规则服务       内容 + 传入的 facts + 显式种子 → 不可变规则实例/值/决定，无角色状态
      → 纯规则       静态类，纯函数，不碰 I/O
```

每一层只调它下面那层。业务服务问规则服务「规则怎么说」，规则服务读内容源并调静态规则类算出
答案；业务服务不自己读内容源，CMD 也不自己问规则服务。频道准入与登录端点经
`IChannelEntryDomainService` 取得具名结果，规则和部署配置仍由各自所有者读取。

**规则输入只经规则服务进入本服务器。** 规则输入指游戏规则依赖的、来自运行中世界之外的值：
PVF 内容源、游戏规则配置、以及表达归档或游戏语义的常量。它们的读取点只有一个，就是拥有该
主题的那个规则服务；`Application` 的业务服务、`Protocol` 的 handler 与命令处理器都不读。

规则服务不查世界状态，只接收调用方传入的 facts；业务服务不能绕过它读取规则输入。

领域按功能内聚：`Abstractions/<领域>/` 放契约，`Application/<领域>/` 放业务、规则、订阅与投影，
`Persistence/<领域>/` 放存储，PVF 内容源在 `Infrastructure/Content`。同一事实只由一个 owner 保存。
CMD 编排多个领域能力，但不接管领域规则；领域服务不认识具体协议的字节布局。
需要同时看到具体消息与领域结果的逻辑归 Protocol。

## 接口类别与代码形状

`Abstractions` 里的接口后缀就是它的类别，不需要打开实现才知道调用它意味着什么：

| 后缀 | 含义 | 调用它意味着 |
|---|---|---|
| `*DomainService` | 领域服务 | 读写世界状态，**次序与并发有意义** |
| `*RuleService` | 规则服务 | 内容 + 传入的 facts + 显式种子 → 不可变规则实例/值/决定，无角色状态。**内容源与规则配置的唯一读取点**；由业务服务调用，不由 handler 调用 |
| `*Projection` | 投影 | 单个 `Build(存储记录) → 领域值`，无依赖、无 I/O |
| `*Rule` | 规则实例 | 由规则服务返回并共享的不可变静态规则，只接受显式 facts，不读取世界或角色存储，不缓存角色状态 |
| `*Rules`（静态类） | 纯规则 | 纯函数，不碰 I/O。**只由规则服务调用**，是规则服务的下一层而不是它的替代 |
| `*Repository` / `*Registry` / `*Directory` / `*Persistence` | 持久化与运行时注册表 | 各自后缀已表明。**不得注入命令处理器** |
| `*DomainStore` | 领域 store | 领域状态的事务结算真源，被要一个会话、由事务收口。CMD 的业务依赖之一，机制例外见协议指南 |
| `*DefinitionSource` / `*ContentSource` / `*Catalog` | 内容源 | 读归档，确定性、可缓存。**只被规则服务注入** |
| `*Policy` | 策略 | 规则服务的一种，回答"允不允许"；频道规则由领域服务内部调用 |
| `*Query` / `*Reader` | 只读投影/查询 | 只读，不改世界状态 |
| `*Decoder` / `*Encoder` / `*Sender` / `*Dispatcher` / `*Handler` / `*Observer` / `*Closer` | 传输与协议 | 各自后缀已表明 |
| `*Module` | 组合根接线 | `IServerModule`，只注册不执行业务 |
| `IGameplayQueue`（按名字认，不按后缀） | 执行器机制 | 由 `CommandBus` 等机制将 typed CMD 变成作业；生产者经总线提交，不注入 CMD handler。在执行器上调用并等待即自我等待，以程序错误失败 |

判断一个进程内容器属于哪一边，看**接口说的是领域动作还是容器动作**，而不是看它内部有没有用字典。会话、在场、专家商店、PvP 大厅四处都在内存里保存运行态，但它们对外只回答领域问题（谁在这个区域、这个角色有没有摊开着、哪些房间已公告），所以它们是领域服务；字典是实现细节。反过来，一个对外暴露「取出来、改一改、塞回去」的类型不会因为改个名字就变成领域服务。

判据：**这块状态参不参与别的聚合的不变量**——参与就放进那个聚合，不参与才自己一个服务。
规则服务**不许自己查世界状态**，只能接收 facts；一旦它自己去查，它就成了世界的第二个所有者。

`IServerConfigService`（`T GetConfig<T>(string key)`）是宿主配置访问，保留该名称。
启动服务、连接观察者、每日重置／疲劳／黑钻服务及 `ChannelLoginDeploymentSource` 使用部署配置例外。
LOGIN 经 `IChannelEntryDomainService` 与 typed 来源读取同一配置快照，只在成功响应阶段按需取值，
不直接 `GetConfig`。纯测量开关 `ExperienceDiagnostics` 与协议投递选项 `SelectCharacterDeliveryOptions`
也不属于游戏规则输入；理由由各自实现说明，不能扩大为任意配置读取许可。

- 使用 `ValueTask`；handler 为 sealed，使用主构造器 DI；命令与命令 handler 同文件共置。
- 依赖经构造器显式注入。构造参数不用产出接口的 `Func`、`Lazy<>` 或 `IServiceProvider` 推迟依赖：出现依赖环时
  按职责拆读写接口、抽出上层服务，或让调用方把世界状态作为 facts 传入。按注册类型在作业或事件 scope 内解析处理器的
  分发机制是具名例外。`DependencyGraphArchitectureTests` 扫描真实生产类型，并在生产模块组合出的容器上检查依赖环。
- 依赖没有可选的：容器构造的类型，构造参数里的服务不可空、不带默认值，注册的实现只有一个公开构造器；缺了就该启动失败，
  不能传 null 后跳过一段或在罕见路径上才抛错。可执行程序经 `ServerComposition` 组合，任何环境都开启 `ValidateOnBuild`／`ValidateScopes`；
  `RequiredDependencyArchitectureTests` 在同一组合上检查，并带拒绝违规形状的反例。服务自己组装的对象（容器、会话、上下文）不在此列，由持有依赖的 owner 负责。
- 分层先看项目引用图；CMD 注入与结构由 `CommandHandlerArchitectureTests` 的真实类型判据守护。
  消息／CMD 双向绑定、薄 handler 和纯遥测由注册检查及对应结构测试约束。
- 类型后缀与文档不能替代判据；新增机制能力须有具名理由及会拒绝违规形状的反例。
