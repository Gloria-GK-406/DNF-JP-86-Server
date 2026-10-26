# AGENTS.md — 86JP 服务端

DNF 日服 Act.86 客户端的服务端仿真。协议逆向结论的权威存放处是顶层
[`protocol/`](protocol/) 知识库（目录、模板、证据等级见
[protocol/README.md](protocol/README.md)）；帧级基线在
[Docs/NetworkingFrameProtocol.md](Docs/NetworkingFrameProtocol.md)。
旧单体实现保留在 `E:\Project\86JP` 仅供行为对照，**不要**参考其结构。

## 开发与发布约定

- 开发提交只推功能分支，经 PR 合并到 `main`，不直接向主干推送代码。
- 服务端版本统一由 `Server/Directory.Build.props` 的 `VersionPrefix` 管理，初始基线为 `0.1.0`。用户没有另行指定时，每个 PR 相对目标 main 的版本递增一次 PATCH（如 `0.1.1`、`0.1.2`）；同一 PR 的追加提交不重复递增。合并前若 main 已前进，重新核对版本，避免并行 PR 使用同一版本。本次建立版本基线的 PR 使用 `0.1.0`。
- 递增版本不创建或推送 tag，不自动发布 Release；打 tag 仍须用户明确要求。
- 用户要求“分支收尾”时的标准顺序：更新实现/协议文档和验收边界 → 推送功能分支并创建 PR → CI 通过后合并 → 删除已合并的远端、本地任务分支及可清理的任务工作区 → 最后更新关联 issue（只关闭已完成范围）。主仓库不删除，切回 main；保留其他任务工作区、有效取证资料和运行中的服务/数据库。此类明确收尾授权覆盖上述流程，不逐步重复询问；不自动打 tag。
- 版本 tag 只从主干创建；Release 工作流检查 tag 指向 main 历史上的提交。
- PR 和主干合并运行 CI；版本 tag 重新测试后发布 Windows x64 / Linux x64 自包含包。
- 当前不依赖付费分支保护，遵守上述约定。操作与验收边界见 `Document/server-ci-release.md`。

## 仓库布局

```
Server/      .NET 10 服务端解决方案（本文档主角，见下）
protocol/    协议知识库：按 endpoint/direction/type 组织的逆向记录
Tool/        PvfProbe（PVF 归档探针）、WireSmoke（打线）、
             CrapMeter（方法级 CRAP 变更风险采集，结果写 .crap/<时间戳>/，不进版本库）
Docs/        四类东西混在一个平面上，读之前先认清是哪一类，分类见 README.md：
             本仓的设计与裁定 / 客户端逆向 / 参照服务端 ServerS4A21 的资料 / 协议失谐记录
```

**`Docs/` 里有一整类文档描述的是参照服务端 `ServerS4A21`，不是本仓**，它们开头有醒目的
免责块。当业务语义的参照读，**不要拿它当本仓事实去取证**——这已经出过事。

## Server 解决方案结构

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
| `Dfo.Server.Persistence` | Abstractions, Data | SQLite 仓储与存储 + `Schema/` 唯一建表归属者 + `Database/` 事务参与者。**全部类型 `internal`**，对外只经 Abstractions 契约 |
| `Dfo.Server.Networking` | Abstractions, Data | TouchSocket **传输**：帧适配/解析、TCP 服务、插件、出站编码器、发送端（每连接发送队列、交付期限、队列上限、失败即关连接）、注册表。收发路径上不含任何单个协议的知识；发送端也只表达必达/尽力、跨连接的交付顺序和逐目标失败隔离 |
| `Dfo.Server.Protocol` | Abstractions, Application, Data, Networking | **协议切片**：一个协议一个目录，含其消息、解包器、编码器、消息 handler、命令与命令 handler；`ProtocolModule` 是唯一接线点 |
| `Dfo.Server.HttpApi` | Abstractions, Application, Data | HTTP 组件：Kestrel 装配、路由、中间件、HTTP 就绪；保留 LauncherAuth 配置键 |
| `Dfo.Server.GmWeb` | HttpApi, Abstractions, Application, Data | GM HTTP 功能模块：管理员访问边界、查询/发放端点及 `/gm` 静态页面；前端源码在 `Web/gm-admin` |
| `Dfo.Server.GameServer` | Abstractions, Infrastructure, Networking, Protocol | 游戏组件：装配网络/协议，入口启动前预热内容，提供游戏就绪状态 |
| `Dfo.Server.Host` | 公共模块、HttpApi、GmWeb、GameServer | 唯一组合根：共享配置和服务注册；不直接引用 Networking/Protocol |
| `Dfo.Server.Utilities` | — | 空占位 |

**分层由项目引用图在编译期强制**：

- Host、HttpApi、GmWeb、GameServer 仍在同一进程、同一根 DI 中。组件不能自行 Build 第二个 Host/ServiceProvider。启动顺序与开关见 [Document/host-component-composition.md](Document/host-component-composition.md)。

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
当前模型及新增协议示例见 [协议 CMD 开发指南](Document/ProtocolCommands.md)。

### 设计哲学与重构判断

上面是请求的走向；**为什么这样切、什么时候该重构**另有两份，给做设计和改结构的 agent 读：

- [Docs/Agent/ArchitecturePhilosophy.md](Docs/Agent/ArchitecturePhilosophy.md)——总架构
  `handler → CMD → 领域服务 → 事件服务 → 通知总线` 的意图、领域模块按功能内聚、不变规则的两层、现存偏离。
  **新建领域模块、决定一段逻辑放在哪一层、评审结构时先读。**
- [Docs/Agent/RefactorPreferences.md](Docs/Agent/RefactorPreferences.md)——用户的重构触发信号、手法与被推翻过的做法。
  **修 bug 时犹豫「打补丁还是重构」、发现同一事实或判定出现第二处、准备动结构之前先读。**

两份是归纳而来，与本文冲突时以本文为准，并修正那两份。

### 1) 领域服务编排业务（Application）

领域按目录组织：`Accounts/`（账号 entry 状态）、`Channel/`（入场策略）、
`Characters/`（花名册/entry 状态/入场后修正）、`Inventory/`（入场读取、物品生命周期、每日补货）、
`Quests/`（按角色取得的任务日志：在做的、已通关的、追踪选择）、
`Sessions/`（在场、选中会话、PvP 大厅、专家商店
四个领域服务，各自把运行态留在私有容器里；选中会话服务本身不存状态，读写在线身份）、
`World/`（出生点策略、世界内容）、`Users/`（`OnlineStateRegistry` 是在线身份：在线用户、
认证账号、选中会话及其阶段同在唯一那把锁后，`OnlineUserDomainService`、
`GameSessionEndpointDomainService` 与 `Sessions/SelectedSessionDomainService` 是读写它的门面）。
角色在线时“怎么样”（位置、血蓝百分比、物品冷却、灵药加成）归 `Characters/` 下的
`IOnlineCharacterRuntimeDomainService`：按会话代数归属、无互斥锁，不并入角色领域服务。
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

反过来的一半早就写在 `Docs/领域结构重构设计.md` §6 里：规则服务不许自己查世界状态，只能接收
facts。两半合起来才是一条线——一边不许往上摸世界，一边不许绕过它去够内容。

### 命令处理器的注入与结构

业务依赖只允许 `Abstractions` 中的 `*DomainService`、`*DomainStore`，以及既有具名
`IPartyService`。角色／任务簿／容器实例从领域服务取得，事实值从命令输入传入；不注入行网关、
持久化接缝、运行时容器、内容源、规则服务、总线、队列、执行器、传输 sender／closer 或 service locator。

协议编排另有封闭的机制能力：显式工作单元、当前作业发件箱／关闭登记／即时投影、出站顺序、
固定槽位换算、时钟和日志。选角、翻牌、祭坛、Joust、HTTP 审计、生命周期、事件消费和调度的少量
特殊能力必须有具名理由；精确类型与理由只维护在
`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`，
不能凭相似名称、后缀或转发门面扩权。规则服务仍不得直接注入 CMD。

协议 CMD 不继承共用 CMD 基类，每个 CMD 直接显露自身用例顺序。可复用的协议阶段按明确职责组织，
共享业务归真正的领域 owner；不能把巨型基类换成依赖袋。服务依赖必须非空且完整，不用 `null!`、
可空构造参数、缺省服务或 `Func` 能力为不同调用者填空。业务可空值、纯值函数及显式单元 action 合法，
不构成服务定位或任意业务回调的许可。判据须检查真实生产类型，并有会拒绝违规形状的反例。

### 角色自己的数，问角色

身份、等级、经验、当前基础属性、已学技能和 SP/TP 账本属于角色，从 `ICharacter` 按 ID 现查。
经验与等级的唯一修改入口是 `ICharacter.AddExpAsync` / `SetExpAsync` / `SetLevelAsync`。
`ProgressAsync` 从累计经验和 `IExperienceRuleService.CharacterScale()` 推导等级；86 级经验上限取
PVF 表索引 84。数据库 `level` 仅为 SQL 兼容投影，启动时归一旧数据并装载上限与投影触发器。
`ExperienceAward.Total` 是封顶存储结果，`NotificationTotal` 是该次回包累计值，不可持久化。
满级溢出由角色实例调用账号胶囊领域服务，与角色更新同事务。详见 [经验规则](Document/CharacterExperience.md)。
这些查询不要求角色在线。角色实例只保留 ID 和取数服务，不缓存等级、技能组合或查询结果；
数据库读取复用当前命令的 SQLite 事务，提交、回滚之后再问仍从当前事实作答。

静态规则由共享规则服务持有。`ICharacterStatRuleService` 准备职业与 1—86 级的不可变基础属性，
`ICharacterSkillRuleService` 返回按职业/技能共享的不可变规则，显式接收职业、等级和已学事实。
规则可以持有不可变静态声明与预计算表，但不能查询角色存储或缓存角色状态。职业拆装集中在
`CharacterCareer`，支持集合由 PVF 声明决定。属性与技能算法依据分别见
[基础属性规则](Document/CharacterBaseStatRules.md) 和 [技能规则](Document/CharacterSkillRules.md)。

`SkillPointLedgerAsync` 保留四个 long 余额及未知技能诊断，`RemainingSkillPointsAsync` 仅为旧协议
夹取到 ushort 的兼容读法。未知已学技能仍保留，并以零投入兼容处理、标记不完整和记录警告。
已学事实只含页号、技能 ID 和等级；slot、extra_values、`SkillTreeIndex` 属于动态配置/表现边界，
沿用现有存储与技能页投影。普通页账本不代表自由决斗频道的无限点数策略。

选角基础属性以及副本选择界面的基础属性/身份/进度/技能/余额投影由领域服务刷新。
`CharacterDerivedStatsStorageRecord` 的历史基础列不是当前基础属性真源，Progress1/2、百分比、
宠物等动态或未建模字段仍保留；装备和 Buff 对战斗属性的合成未实现。副本进出判定、接任务、
卡牌及无经验授予结果的发帧回落读取当前角色；非空授予结果继续代表该次写入的回值。
教程命令在现有事务中检查当前等级，不能凭旧会话的 1 级把已升级角色改回 2 级。

`SelectCharacterResult` 只在选角响应组包期间存在，不再存入会话运行时目录。后续投影通过
`ISelectedCharacterCurrentViewDomainService` 在精确会话校验前后重建，物品、角色、任务、账号等
可变内容分别从所属领域读取。`ProtocolMirror` 是协议投影，不是当前点数真源。

**这几样状态各自归谁——以及哪几样只做了一半。** 下面这张表按「谁是主人」记，不按「计划要做什么」
记。**先看最后一列**：有两样今天一个生产调用方都没有，那是刻意的，不是漏接线。

| 状态 | 主人 | 入场序列之外今天谁在问它 |
|---|---|---|
| 身份（槽位、名字、职业、成长型） | 角色实例 `ICharacter.IdentityAsync` | 道具可用性判定（`PlayingCharacter.CanUseAsync`）。实例从 `characters` 行现读，不再向会话要 |
| 坐标 | `IOnlineCharacterRuntimeDomainService`（与血蓝、冷却、灵药同一条运行时记录） | 城镇投影、选图、离开副本、两塔、婚礼、传送道具、GM 运行时查询、拆除时落库；持有自己会话的调用方按会话精确读取 |
| 任务 | 既有的任务簿实例 `ICharacterQuestLogDomainService` | 进本时收集迷宫筛选用的 facts（`DungeonEntrySlice.cs:477-483`） |
| 设置（热键、选项位图、可见位） | `ICharacterSettingsDomainService` | **零个。刻意的，见下。** |
| 暗骑连招页 | `IDarkKnightComboDomainStore` + `IDarkKnightComboBook` | **零个。刻意的，见下。** |
| 疲劳 | `ICharacterFatigueDomainService` | 进本处报告消耗（`DungeonEntrySlice.cs:509-511`）。**接缝不记账，见下。** |

会话是否已发布（进入世界）是在线身份上选中会话记录的阶段，用 `ISelectedSessionDomainService.IsInWorld`
精确查询（见下文「会话还有没有效」）；是否落库位置不存储，拆除时由会话频道（`!IsTransient`）推导。
`SelectCharacterResult` DTO 本身仍完整，作为一次性组包结果使用，不是运行期角色聚合。
入场后的当前视图走只读重建，不重复执行选角恢复、技能和解锁等写入。

坐标运行期由 `IOnlineCharacterRuntimeDomainService` 持有；当前视图通过独立的
`ICharacterCurrentStateQueryDomainService` 读取当前位置并组装投影，不调用登录准备或出生点计算。
登录落点只在选角准备中计算，普通频道缺少到达门时回退到配置的初始城镇到达门。
选角入口结果中的坐标只用于当次入场，不得在之后作为真源复用。

**物品移动、修理和耐久消耗直接落在物品领域。** 要发外观帧或进本投影时再读取当前库存；
不存在会话物品副本和 `ReplaceExact` 回写。入场时的物品租约缓存及退场保存链路已移除。
会话与当前视图的边界见 [选角结果与运行期当前视图](Document/SelectedSessionCurrentView.md)。

**疲劳已按日记账。** `CharacterFatigueDomainService` 将配置总量与账号黑钻加成合并，
按 `IDailyResetRuleService` 的当前周期读取已用量；`SpendAsync`/`RefundAsync` 在命令事务内
通过条件写入更新，退款不降低当日累计消耗。`DungeonVisitDomainService` 先检查
`ConsumesFatigue`，由 `DungeonAdmissionDomainService` 检查需要疲劳的副本拒绝零剩余角色，
Visit 在创建出生房时扣除 1 点；入场凭证不含疲劳费用，避免重复扣除。后续首次进入新房间扣 1 点，重访不扣；
不消耗疲劳的副本跳过这两步。当前实现和自动测试不等同于 A21 实机验收。

**客户端设置按blob保存。** C5主设置、170两个快捷聊天bank由`IClientSettingsDomainService`按账号独立更新；
C6完整文本按账号＋profile存入`account_hotkey_options`。登录使用普通profile，选角职业10使用缔造者profile1，
其余使用0；精确连接上下文归属上传，未下发／无法归属的profile不猜写。旧`character_hotkey_slots`和账号
二进制槽列只保留兼容，不用于A21的1C7文本回放。C0由`ICharacterOptionsDomainService`保存角色516B完整包，
AD／1C7／187在已有登录与选角序列回放。内部字段语义、其他玩家可见性联动未纳入blob能力。
协议依据和实机边界见`protocol/patterns/game-settings.md`。连招store与入场读取仍保留原有边界，不因本能力移除。

**宿主部署配置不是规则输入。** `IServerConfigService` 的实际消费方包括启动服务、连接观察者、每日重置／疲劳／黑钻服务，
以及 Infrastructure 的 `ChannelLoginDeploymentSource`。它们沿用下面「接口后缀标明类别」的部署配置例外。
LOGIN 的真实 `IChannelEntryDomainService` 通过 typed 来源取得事实，不直接 `GetConfig`；来源持同一只读快照，
仅在成功响应阶段按需读取。同样不算规则输入的还有两类，各自在代码里写明了理由：
纯测量开关（`ExperienceDiagnostics`）与协议投递行为（`SelectCharacterDeliveryOptions`）。

**在线身份只有一条记录、一把锁。** “这个账号/角色/连接现在是哪个会话、走到哪一阶段”只在
`OnlineStateRegistry` 回答：每个账号至多一条选中会话记录（`Selected` → `InWorld`，可带隔离标记），
在线用户上的当前角色由这条记录投影出来，不另存。连接被撤销（协议拒绝、被顶替）时在线用户立即删除、
选中会话记录保留，`StandingOf` 报告 `ConnectionRevoked`，直到拆除流程 `ReleaseExact`。不要再在旁边
加平行的会话目录、活跃角色表或隔离集合（#186）。

**「会话还有没有效」「那个角色在不在线」只问具名判定。**
- `IsSelected(session)`：当前选中且连接仍归它，任何阶段都算。只用于选角工作流。
- `IsInWorld(session)`：有效且已从它自己的游戏连接进入世界。其余玩法、投影、投递都用它。
- `TryGetInWorld(角色 / 线上号 / 连接)`：手里只有键、要找在世界的会话时用。

拆除、补偿、断线清理处理的是连接已撤销的残留记录，问 `StandingOf` / `ReleaseExact`。三个判定都不看隔离标记，隔离由 CMD 入口与显式工作单元的精确保护检查负责。

不再手写「`Find…` 出来再比较」「查不到就当不在线」，也不再拼在线用户的 `CharacterActive` 标志（#185）。
`Find…` 只剩下面几种用法：
- 请求方按自己这条连接解析会话；
- 清理路径取出要释放的会话；
- 查到之后必须经过一处具名判定；
- 只用于加锁，或只做身份一致性快照，不用它决定对方在不在线；
- 不以在线为前提的业务动作（如比武淘汰），发送前仍以 `IsInWorld` 把关；
- 客户端设置确认「这条连接上还没有选中会话」。

### 玩法执行器、发件箱与发送端

**一条队列。** 进程内只有一个 `GameplayExecutor`，FIFO、单消费者，一个作业结束才取下一个。
生产入口通过 `CommandBus.SendAsync`（协议）或 `SendInternalAsync`（具名内部来源）提交 typed CMD；
底层 `IGameplayQueue` 由机制使用，不把任意业务委托当作新的内部命令输入。协议作业以 CMD 类型命名，
内部来源明确给出 timer／event／lifecycle／http／gm／startup 标签与调用链。

**生产者只组织输入、提交、等待。** 薄 handler、定时源、事件组循环、HTTP／GM 路由与启动服务不在队列外
读写玩法世界、开写事务或发包。HTTP 的解析、身份输入、密码计算与响应映射属于外侧边界，42 个路由操作
由同一种 `HttpRouteCommand` 按纯数据输入执行；不能传入业务 `Func`。定时源按原规则合并 hosted tick，
两塔与每日刷新跟踪完整扫描／逐项轮次，手工已接受轮次仍须排空；同一轮的不同 CMD 之间允许正常 FIFO 插入。
每日刷新在真实 outbox 登记接受后才推进当前周期和精确会话水位，不把“尝试过”当成接受。

纯遥测经 `RegisterTelemetryMessageHandler` 注册，只注入日志，留在收包回调中。普通消息的双向注册绑定
消息 CLR、handler 与 CMD；有限请求家族明确列成员，拒绝未知派生。

**玩法路径上不加门、信号量与「锁内复查、重试」循环**，串行只靠这条队列（`LockRemovalSourceTests` 按名字与文件守着）。
短 `lock(sync)`、PVF 内容源与规则初始化的锁保留；执行器外的口令哈希限流保留它的信号量。

**作业里不等网络，也不入队后等待。** 在执行器上调用入队点并等待是自我等待，以 `GameplaySelfWaitException`
作为程序错误失败；作业直接写或关传输连接同样失败。从作业里分出去的工作在 `ExecutionContext.SuppressFlow`
下启动，传输回调入口经 `GameplayJob.RunDetachedAsync` 清除作业身份；两者能出现在哪些文件由 `DetachmentSiteTests` 列名单。

**运行期判据没有开关。** 执行器外开启写工作单元、登记发件箱、调用玩法状态的写入口，立即抛出
`GameplayOffExecutorException`。玩法状态名单的唯一出处是 `GameplayWriteEntryTableTests.Types`（挂点）与
`GameplayStateCensusTests.Exemptions`（豁免）；新增持有进程内可变状态的单例或静态字段必须进其中之一，
普查判据会拒绝漏登记的。测试在执行器外写玩法状态要经 `Server/tests/Support/TestGameplay.cs`。
只有 `GameplayExecutor` 能进出作业（`GameplayJobHostStructureTests`）。
口令哈希只经 `PasswordComputationLimiter`，在执行器外计算，结果作为作业的输入；在执行器上调用即失败。

**请求上下文显式传递。** 每个 CMD 的 `CommandExecutionContext.Request` 只属于当前作业、当前调用；
结束后关闭，不能由 DI、静态字段、job slot 或 AsyncLocal 获取“当前请求”。ProtocolNonEffect、
ProtocolEffectRejected、ProtocolEffectSelected、TrustedInternal 是封闭状态，不以 null 会话推断内部权限。
仅执行器当前 job 和领域事务保留各自必要的 ambient；它们都不是请求权限的替代。

**调用链。** 每个作业带根、父、深度、来源种类与来源连接。事件作业是发布它的那个作业的子；作业登记的关连接
引起的 `lifecycle:disconnected` 是登记它的那个作业的子；入站、定时、GM、HTTP、启动恢复与传输层自发的关闭是新根。
深度上限 `GameplayCallDepth.Limit`（32）：超限的作业不入队，执行器记一条带完整链的 `Error`，根来源是客户端连接的，
关闭该连接；超限的事件 delivery 由系统作业落定，超限的拆除改作新根执行。

**失败与日志。** 作业失败由执行器记一条带调用链与来源连接的 Error。生产者用 `GameplayJob.EndedAJob`
识别已经报告的失败，同一传播链降为 Debug 或带额外事实且不附异常的 Warning；独立业务／清理错误仍按真实
发生报告。定时源只隔离已经执行的项失败，拒绝入队不伪装作已执行成功；原重试、取消与停机规则保持。
同一 cause 的独立清理失败也是另一发生，不能用异常引用相同来抹掉。

**发件箱。** CMD 通过具名 outbox／projection／closure 接缝登记，登记时立即编码，不等网络。
单元内条目随真实完成状态保留或丢弃；单元外条目及关闭请求按原规则保留，作业结束后按登记顺序交发送端。
生产禁止调用事务内延迟 `ComposeAsync`；其存在与机制测试不代表已启用或问题已修。
`CommandJobEntry.Begin` 在身份检查、CMD scope 建立前初始化发件箱；即使没有任何消息条目，失败仍可关闭
精确来源。不存在要求各 dispatcher 或调用者顺手读取 Current 的过渡约定。

提交后的编码失败、scope 释放失败、未请求取消及不确定结果走默认失败：保留完整原因，保护已声明的精确
参与者，关闭实际来源，不补失败 ACK。正常拒绝仍按各协议原语义处理。已批准的三类即时登记 raw 出口与
有限健康回滚结果另有真实阶段证明，不能从异常类型或“可重放”推导许可。通知总线原逐收件人 Failed 隔离
保持；它不代表其它调用者可以清除 request failure。

**发送端** `OutboundSender`（`Networking/Outbound/`）：每连接一个发送队列；条目分必达与尽力；跨连接的有序组
按登记顺序交付，组内一条必达条目没交付成功，其后的条目一律跳过（关连接请求除外）。交付超过
`OutboundLimits.DeliveryTimeout`（2 秒）、等前序有序条目超过 `OutboundLimits.OrderedWaitLimit`（2 秒）、
或队列超过 `OutboundLimits.QueueByteLimit`（1 MiB，按编码后的字节计）时，关闭该连接并丢弃它余下的条目；
连接已不在登记表里时条目直接丢弃；没有重试。执行器从不等待发送。
日志：普通交付失败、超时、超限记 `Warning`；交付失败时传输层已无法解析精确连接、连接关闭 token 已取消，
或真实发送命中当前 .NET 的已完成 PipeWriter 抛错点时，记 `Information` 并带后缀
`; the connection was already gone`。这覆盖 TouchSocket 写端先完成、连接仍 Online/已登记的窗口；
不按任意同类或同文异常降级，超时仍记 `Warning`。失败结果、关闭及 observer 通告、后继丢弃规则保持。

**停机。** 宿主先停止接受连接与 HTTP、停掉各后台服务；`GameplayExecutorLifetime` 再依次排空生产者
（传输排空会拆除仍登记的连接），最后排空并停止执行器。在线玩家的拆除落库因此在执行器停止之前完成。

### 2) 命令执行总线（Application/Commands）

只保留 `ICommand<TResult>`、`ICommandHandler<TCommand,TResult>` 与一个 `CommandBus`。注册类别只有
Protocol／Internal，没有细粒度内联路由、ExecutionKind、标记接口管线或逐命令自动事务。
协议 handler 提交一个 `IProtocolCommand` 并等待；内部来源显式提供 `InternalCommandOrigin`。
CMD 内禁止直接或间接再次 dispatch，执行器内入队等待、泄漏续体或已关闭请求都会失败。

总线初始化发件箱，创建 CMD scope 和显式 request，调用 handler；随后排空已开始的操作、关闭 request、
释放 scope、检查作业健康并裁决默认失败。未 await 的单元不会因 handler 提前返回就越过收口。
注册检查消息／CMD 类型绑定、来源、有限能力和重复注册；未知内部命令仍在 dispatch 时拒绝，
没有按所有 `ICommand` 类型枚举补注册的规则。

**一条 CMD 可以有多段事务。** `ICommandWorkUnits.WriteAsync`、`ReadOnlyAsync` 接受当前 execution、
具名 declaration 和 action。单元各自拥有实际事务，不把整个协议或选角工作流合成一个事务。
参与者保护声明与事务参与者不同，空 selected 列表不表示没有数据库或内存参与者。
`RequireRollback(cause)` 可表达正常业务拒绝；Committed、ConfirmedRollback、Uncertain 是不同事实，
不能拿 `IsSettled` 推断成功。补偿保留各阶段的原独立落定与收集顺序。

**结算权限不靠会话存在推断。** 普通 NonEffect 没有 Main／操作账本／raw 权限；效果消息经过真实 admission，
唯一 Main 绑定原精确物理连接账本与 entry。同帧确认回滚重试复用 entry，不能延长原 Ordinal 窗口。
已提交重放读当前投影，不重做资产写；Pending 收尾为拒绝。Footprint 必须显式传入，绑定 request、job、
owner、活跃 action 与事务。NonRequest／DeclaredNonRequest owner 声明不扩 Main 或恢复权限，
保护目标仅取本次真正声明的精确 session／physical 引用；早期 null 不遮掉后来声明的 physical。

**有限结果入口不是通用恢复。** `WriteResultAsync` 仅在获准的真实阶段、健康确认回滚和完整清理后返回
`CommitRolledBack`；Mailbox 自结算另用唯一具名事实。选角精确 cleanup、HTTP 固定失败审计、BodyOnly
取消观察及调度完成桥各有固定 owner／来源／发生阶段，不能对外暴露任意回调、跳过默认保护或清 sticky。
独立 guard、rollback、lease／scope 清理错误都会否定单一健康恢复依据。实现与边界见
[协议 CMD 开发指南](Document/ProtocolCommands.md)。

只保留三类已批准即时编码 raw 出口：UseStackable 的 SettledRequiredExplicit、具名 committed replay 的
CommittedReplayCurrentStateBatch、CollectionBox 的 SettledInitialBatch。它们须贯通本次 actual encoder
的 opaque attempt，匹配精确目标、原 cause 与完整健康清理；业务、lookup、登记守卫和额外失败仍默认处理。
重放 owner 的许可不延伸到首次成功。不得为新的协议凭异常类别加入第四出口。

**领域事务／载体。** 参与者按 identity 去重，全部 Prepare → 唯一可失败者 Commit → 其余
Commit；一次事务只允许一个 `CommitCanFail`，第二个 Enlist 即拒，回滚只涵盖已登记参与者。SQLite identity
是连接字符串，一个数据库一个参与者，物品／人物／任务／暗骑页是同一个参与者的四个 `ISqliteStoreFacet`，
可读本次未提交写。`IDomainTransactionAccessor` 用 AsyncLocal 暴露事务，不能用它取得当前请求。
`IDomainStore<TSession>` 给出载体身份、持久性与事务会话；`IDomainStoreSession<TPlace,TKey,TValue>` 共享
按位置读／写／清空的类型词汇：Item 与 DarkKnightCombo 是具名齐次 session；Character 的 Creatures 与其余
具名分区、Quest 的 Carried／Cleared／Tracked 分区组合成各自 session。`AmbientStoreAccess` 边界内用环境事务，
边界外读放行、持久写拒绝、易失写自开一次性边界；实例／快照不因共享词汇丢失类型。

### 3) Socket 层：薄 handler 提交协议 CMD

Networking 只做帧适配、线级注册查找与传输。未注册线级键静默丢弃；已注册 decoder 产出的目录成员必须
都有 handler，启动检查闭合性而不要求目录枚举完备。普通 handler 在执行器外将原消息与精确连接上下文
交给一个协议 CMD，不先建立 inbound 作业，不查世界、不自行回包。

CMD 拥有身份解析、业务阶段、显式单元与出站登记。Game 的 CMD／NOTI 类别只在 Protocol 解释；
Channel 经通用 `ICommandMessageOutbox` 登记，保持原 AES-128-ECB、zlib 和当日密钥规则。
连接建立／断开由 typed Internal CMD 驱动固定生命周期 handler；`OnlineUserConnectionObserver`
拥有 Game 初始消息等协议知识，在作业内调用领域能力与精确清理，传输 observer 只提交并等待。

### 切片边界规则

一个协议 = `Dfo.Server.Protocol/<Endpoint>/<Protocol>/` 一个目录，最多六类制品：

| 制品 | 文件 | 何时可省 |
|---|---|---|
| 消息 record | `Messages.cs` | 从不 |
| payload codec | `*PayloadCodec.cs` | body 无需解码时（Channel 系） |
| 入站解包器 | `*MessageDecoder.cs` | 纯出站协议 |
| 出站编码器 | `*ResponseEncoder.cs` / `*NoticeEncoder.cs` | 纯入站协议（遥测类） |
| 消息 handler | `*Handler.cs` | 纯出站协议 |
| 命令 + 命令 handler | `<Verb>Command.cs` | 仅纯遥测无命令；只读或纯协议响应仍有 CMD |

规则：

- **切片内不跨协议引用**。要共用就上提到 endpoint 层
  （`Channel/ChannelProtocol.cs`、`Game/GameProtocol.cs`）或领域服务。
- 目录名不得与任何被引用类型同名——C# 名称查找里外层命名空间优先于
  using 导入的类型，同名会得到 CS0118。`AccountRoster` 之所以不叫
  `AccountCharacterRoster` 就是因为这个。
- 新协议只改一处接线：`ProtocolModule`。`ApplicationModule` 不再承载任何
  单个协议的注册。

### handler 与 CMD 的边界

普通消息 handler 只注入 `ICommandBus` 和可选日志，将原消息、dispatch 原样交给注册的 CMD 并等待。
它不读在线身份、不派多条细命令、不登记出站、不执行补偿。纯遥测仅日志的例外由注册表检查。
选角也遵守这条规则：原多阶段编排已经属于 `GameSelectCharacterCommandHandler`。

CMD 可以按用例顺序调用领域服务、开多个具名单元、登记即时投影及精确关闭；不直接读内容／规则、
不发送 socket、不调用另一个 CMD，不绕过明确的事务／保护声明。共享领域事实由实际变更的领域服务
记录，CMD 不凭投影发布持久事实。协议共用的纯帧能力留在 Protocol，不污染 Application。

Game `0x01` 是 CMD、`0x00` 是 NOTI；每个出站消息必须且只能实现 `IGameCmdMessage` 或
`IGameNotiMessage` 之一，编码器继承固定类别基类。CMD 用 `IGameCommandOutbox.RegisterCmdAsync`／
`RegisterNotiAsync`；批量和显式收件人登记保每条消息自己的类别，不让调用者覆盖首字节。
Networking 仅把此字节当不透明 Selector。

### 端到端示例：GameLogin

1. 14B 头帧 → `GameClientFrameDataHandlingAdapter` → `GameNetworkPlugin` 按 selector 0x01/type 0x0001 查表；
   `GameLoginPayloadCodec` 解出两个 int32-LE 长度前缀 ASCII 字符串：MId 和 Ticket。
   它们来自 launcher HTTP 登录结果，不是账号名／密码，也不是角色 ID。MId 为当前签发的六位随机字符串，
   Ticket 为16随机字节的32位十六进制文本；示例只取 HTTP 实际返回值，不能硬编码。
2. dispatcher 按 `ProtocolCommand` 注册调用薄 `GameLoginHandler` → 同一总线将 `GameLoginCommand` 入队一次，
   全程保原 request 和物理连接 context；不先建立旧 `inbound:GameLogin` 作业。
   匿名在线注册由 TCP connected 生命周期先建立；匿名协议请求不因此变为内部请求。
3. Protocol CMD 的唯一普通 Write 调真实认证领域服务：正常兑换只登记 LoginSessionRegistry 和
   OnlineStateRegistry 两个内存参与者，提交发布身份后执行既有 afterCommit 顶替清理；selected-session 声明列表为空。
   该兑换路径不加入 SQLite；注册和 launcher 发票的 SQLite 事务是另一个用例边界。
4. 提交返回后重查精确身份，再读取账号设置；`IChannelEntryDomainService.ResolveLoginAsync` 先映射 listener，
   按频道 environment 短路选择回退，再由 typed 部署来源读取连接端口、host、PortA、PortB。
5. `IGameCommandOutbox` 登记成功 CMD 和原六 NOTI。hotkeys 登记后、auction0 前直接激活 profile0；
   设置读取、激活和登记均在兑换事务之后。拒绝保 Warning 类别、移除在线身份并登记原完整连接关闭。
   编码器保持固定 CMD/NOTI 与 15B Game 头；作业结束后由发送端交付。

集成测试 `Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs`
用真实 TouchSocket host + SQLite 断言逐字节行为。

### SelectCharacter：接受与状态同步工作流

薄 `SelectCharacterHandler` 只提交 `GameSelectCharacterCommand`。其 CMD 顺序执行准备、补货、发布、
PVP、任务、宠物和同步投影，保留每段原事务及当前性检查，不嵌套派发。`01/0004` 是进入接受，
发送前完整预校验必达接受／前置状态；失败走具名精确 cleanup 与原补偿顺序，再登记连接关闭。
清理自己的独立失败不能被原失败遮盖，也不能借 cleanup 重新开启普通业务。

进入接受、PremiumService、CloneTitle 为 CMD，其余当前状态推送为 NOTI；拒绝仍为相邻的
`01/0004` 与 `00/01F7`。关闭登记排在此前消息之后，真正发送和关闭由作业外发送端执行。
入场 DTO 仅供当次组包，后续投影从当前领域事实重建。

### 领域事件与即时投影

领域服务经 `IEventService` 发布事实，订阅者决定如何响应；协议 CMD 经 `ICommandProjections` 登记即时投影。通知只是消费者之一。
组件不得直接依赖 `INotificationBus`：只有 `NotificationProjectionSubscriber<T>` 适配器、
通知运输机制与组合注册接触该接口。协议编码和 Socket 发送仍由 Protocol/Networking 拥有。

- `EnqueueAsync<TEvent>`：当前可写业务事务内记录强类型事实与匹配订阅的 delivery；
  与业务使用同一个 SQLite 事务。提交后只向容量 1 的 Channel TryWrite 唤醒，回滚不投递。
  每事务一个 BatchId，每事实一个 EventId，payload 不带服务、连接、协议消息或可变聚合。
- `DirectAsync<TEvent>`：不入队、直接依序等待运行时订阅者；不自行开启事务，继承调用方当前事务，
  可在事务内外调用，失败与取消向上传播。次数写入订阅要求当前可写业务事务，扣次事实不注册为队列订阅；
  提交后的界面刷新另由持久化变化事件进入 ProjectAsync。马战维护仍在事务外发布阶段变化，
  查询不发布；启动恢复静默、变化去重、失败不重复公告。
- `ProjectAsync<TNotification>`：在当前作业里组装即时协议投影并登记进发件箱，原样返回 NotificationDelivery；
  不持久化 Session/CMD 回复，也不能在未提交的可写事务内调用。登录、队伍、库存等流程
  保持批次、精确会话与 ACK 顺序；CMD 使用具名登记接缝，不能注入传输 sender。

事件稳定名称/版本与 subscriber ID 在组合根注册。每个订阅组一个生产者循环：在执行器外读出到期的
delivery，把每次消费通过 typed Internal CMD 入队为 `event:<订阅 ID>` 作业并等它完成，再处理下一条；闲时 await
唤醒或最近重试时间，启动扫描遗留工作，停服取消未完成消费。Reliable 订阅在作业内的新 scope
与新事务中处理，业务更新与完成标记同事务；失败回滚后另事务记录重试。
Reliable 的注册分发在当前 CMD 内完成，不嵌套 dispatch。组内串行，失败退避期间后续到期事实可先处理，不保证严格 FIFO；
订阅组之间不并发，因为都在同一个执行器上。当前只注册每个稳定名称的一个版本；未知版本保留
诊断，不自动 upcast；新订阅不回填历史 delivery，完成记录暂不自动清理。

LiveOnly 只处理本进程的新鲜事件：发送前提交一次消费决定，离线、旧进程或过期提示跳过，
失败不补播；崩溃窗口允许漏提示。邮件每封记录 MailCreated，提醒按 BatchId/角色合并，
不以合并提醒丢失事实。GM 活动配置在领域事务记录变化，HTTP 不负责广播。

协议即时投影在 CMD 内按原阶段登记。领域动作完成及所属事务落定之后，CMD 将具名结果交给
`ICommandProjections.ProjectAsync`，最后由作业发件箱统一交付；不在薄 handler 中再次调用总线或 sender。
选角的 PVP 发布、精确清理等流程保自己的受众和顺序，不能为了统一写法移动事务点。

要点，按重要性：

- **人群以角色表达，不是连接。** `NotificationAudience` 是一个封闭层级的值；
  「这个角色此刻在哪个连接上」只由通知服务回答（`NotificationAudienceResolver` 解析一次，
  `NotificationBus` 在每个收件人轮到时再问一次）。**领域服务不得为投递去查连接**——
  这条正是通知服务存在的理由，在它之前有两个领域服务因为要算「通知谁」而知道了 socket 的存在。
  **规则的边界说准**：禁的是「**拿一个角色去查它在哪个连接、为的是给他发东西**」。
  两个限定缺一不可——按角色取会话（`TryGetInWorld`）本身不是禁令的对象：
  `ExperienceBonusDomainService` 这类调用取的是**会话标识**当运行态目录的键，**从不读 `ConnectionId`**，合规。
  按「见到按角色取会话就是违规」去 grep 会把它们误判。
  不禁的是「这次操作本身就发生在其上的那个连接」：清理计划带着离开者自己的
  `session.ConnectionId`（`SelectCharacterLifecycleServices.cs:204`），必送给本人的那一半直接走它，
  不是扇出。
  **别把这条豁免读宽**：`SelectedSessionIdentity` 里带着 `ConnectionId`，而它被传进十来个领域服务，
  照字面它们都「手上有连接」。豁免的是**这次操作的主体自己的**那一个，不是任何拿得到的连接；
  凡是要决定「还该告诉谁」的，一律走人群。
- **通知类型自带它的处理器契约。** `INotification<TSelf>` 是自指的，所以发布调用点的类型由实参定死，
  加宽到契约接口本身编译不过（`CS0311`）。处理器按**收件人**产出该收件人的消息，返回空表示弃权。
- **处理器只读。** 写属于已经提交完的那条命令。`NotificationHandlerArchitectureTests` 守着注入面：
  只允许 `Dfo.Server.Abstractions` 里以 `DomainService` 结尾的接口。
- **通知没有重试，也没有每收件人期限。** 组装或编码失败的收件人记为 `Failed`，只丢他的条目；
  交付失败由发送端关闭该连接，之后的清理是那条连接的 `lifecycle:disconnected` 作业。
  `NotificationOutcome.Registered` 保留原摘要口径，连接已不在而丢弃时也可能为此值。
  真正 outbox 接受由 `NotificationAttempt.RegistrationAccepted` 单独表示；周期水位只能依据这个事实推进。
  两者均不证明已落线或客户端收到，不支持接受结果的旧 sender 默认 false。
- **通知类型必须在 `ProtocolModule` 里注册处理器**，且该程序集要 `AddNotificationCatalog`。
  漏注册会在**开始接受连接之前**让服务器起不来（宿主先实例化 hosted service 再启动）。

**当前消费边界：**

- 人群通知用于 PvP 大厅公示、会话清理以及队伍城镇投影等路径；
  `PartyTownProjectionDelivery` 使用 `TheseSessions` 指定精确会话。
- `SessionAdjacentEntryDomainService` 给入场者本人计算周围角色，不因此成为多人扇出。
- 事件服务拥有领域订阅与分发；通知总线是低层投递机制，只组装并登记进发件箱，不处理可靠领域事实。
- 回给请求方的消息仍可直接投递，不必套上人群抽象。
- 没有专用的频道、全服或组队 Audience 类型；队伍业务已存在，使用现有精确会话人群。
  人群 record 层级不能完全阻止外部派生，解析器会明确拒绝未知形态。

**投递结果既有摘要，也有日志。** 总线返回成功（已登记）、弃权、离线与失败结果；
`PartyTownProjectionDelivery` 已读取成功和失败集合，用于维护实际送达的受众与后续处理。
读取摘要仍是可选的，不能依赖每个调用方都读取它来保证可观测性。
所以在还拿得到原因的地方记日志：

- 组装或编码抛异常 → 总线（或发件箱的逐收件人组装）记 `Warning` + 异常本身，只丢该收件人的条目；
- 交付失败、超时、超过队列上限 → 发送端按连接记，级别见「玩法执行器、发件箱与发送端」。

**弃权与「人不在了」不记**——它们不是出了问题。

### 物品：统一属性、容器内引用与取出记录

**业务属性只定义在 `ItemSnapshot`。** 模板号、数量、`ItemTraits Type`、PVF 身份分类形成的
`IsPile`、独立物品 `ItemUid`、全部兼容字段及 Avatar/Creature 细节都在这份数据中。
`Count` 是数量；`InstanceValue` 是旧编码里的实例值，不能用它代替独立物品 UID。
`InventoryItemCoreState` 和 `InventoryItemState` 只用于既有数据库、报文和入场投影边界。

容器的读取、移动、修改与销毁有不同入口：

- `Get/GetAll` 返回 `ItemReference`，绑定事务、真实槽位和占用代次；它不能直接被 `Put`。
- `Take(reference, count?)` 返回 detached `ItemRecord`；整件取出腾空槽位，部分取出减少原堆。
  记录携带完整属性，但没有容器位置。
- `Put(slot, record)` 接收一次记录；兼容材料可合堆。新物品通过生命周期服务 `Create` 创建。
- `Update(reference, change)` 对当前占用的最新属性应用变更，不移动物品，不替换 UID。
  属性修改不使同一占用的其他引用失效；整件 Take 后即使同 UID 放回，旧引用仍失效。
- 销毁先 `Take`，再 `IItemLifecycleDomainService.DeleteItem(record)`；取出本身不销毁。
  装备与宠物实例绑定这类引用，移位后须重新取得行为接口，不再按 UID 跟随旧句柄。

所有写入处于同一领域事务。`ItemLifecycleScope` 保证 detached 记录只被接收或删除一次，
未安置记录不能提交；中途读取/Flush 不结束其生命周期。回滚恢复物品、数量、状态和位置。
邮件附件由同一 item session 接收/取出记录，作为持久托管；同事务材料可在 SQLite 与易失后端间
转移，独立物品不跨后端。装备交换先取出双方，再分别放入。

`items` 继续保存 82 字节兼容 core 及时装六项、宠物八项扩展状态，`item_slots` 保存归属和位置。
整件 Take 只删位置，Put 保留原 UID，Update 更新原行，显式 Delete 才结束物品行。
材料的内部 backing 行键不充当业务 UID。`InventoryEntryAssembly` 读取完整引用并投影入场状态，
已移除旧 EntryLease/整份 Save、StoredItem、ContainerSlotItem、InventoryStorageItem、
InventorySlotContent、InventoryItemCreationFacts、PutNew/PutTransferred 通路。

新建和落位根据 PVF 校验类型、是否堆叠和目标规则；既存行的身份形态以实际 `item_slots.kind`
为准。容器 flag 准入统一使用 `ItemAdmissionRule` 的 Allowed/All/Any 匹配，语义见
[物品准入规则](Document/ItemAdmissionRules.md)。容量、固定槽、堆叠上限及同账号归属仍由对应业务边界检查。容量缩小后的旧堆可取出或减量，
不能借 Update 增长或改写其他状态。普通补货沿用补货规则决定的数量，与普通发放的堆叠规划不同。
账号仓库无时装细节的历史行仅保留读取兼容，不放宽新建/重新落位的完整性要求。
角色 Main/1 的零复活币逻辑数量为 0，位置表的物理 count 保持正数。

`IsDefault` 标记默认绑定角色，每种类型至多一个且不可移除；`IsPersistent` 独立描述存储生命周期。
`GetContainer(..., isDefault: false)` 使用当前绑定，`true` 显式取默认容器；Find/Get 不补建缺失容器。
`ReplaceDefault` 事务性替换已有默认容器；离线操作显式使用 `GetPersistentStore` / `IItemGrantDomainService.GrantAllPersistentAsync`。
容器注册服务只管理绑定；整批物品赋予归属 `IItemGrantDomainService`。钱包、成长和容量的修改入口见
[资产赋予领域服务](Document/AssetGrantServices.md)。

账号注册与创角通过 `IInitializationDomainService` 在创建事务内应用当前配置；GM 中账号、角色各有一份独立版本的生效配置，见 [初始化配置](Document/InitializationSettings.md)。
每日补给通过配置 / 发放领域服务在角色入场时执行，每日尽可能补给一次，空间不足也记录完成；GM 区分 PVF 与自定义规则，见 [每日补给](Document/DailyRefillSettings.md)。
选角显式注册空的默认内存类型 29 容器。死亡塔入场换成本局六格，跨层保留，退塔/断线换回空容器；
Main 与 SharedMaterial 不切换。实现及实机边界见 `Document/TowerQuickSlots-20261005.md`。

边界限制：`core_uid` 没有数据库唯一约束，普通写入由记录接收机制限制；入场仍检查重复行身份。
`items.item_uid` 的 wire 范围 CHECK 不能靠给既存表 ADD COLUMN 补上，编码仍使用 checked 转换。
物品写入统一通过生命周期接口；数据库结构约束由直接 SQL 测试覆盖。

设计与回归证据见 `Document/tasks/item-container-lifecycle/`。以上是 CurrentImplementation，
不代替 A21 实机验收。以下时装小节保留原逆向及历史修复记录，旧调用链以本节为准。

#### 时装：种类已经对齐，穿不上的那一半已于 2026-09-08 修好

**种类对齐了。** `PvfEntryLifecycleContentSource.WornPieceMetadata` 对 `ItemWorldClass.Avatar`
答 `AvatarItemKind`（8），与存储的判定一致；发放通路落地时会抽一个身份并交出六项事实
（`ItemContainers.ArrivingAvatar`）。`avatar_expire_date` 写的是 `int.MaxValue`，
**这是占位裁定，不是量出来的**。

**穿不上的原因曾经不是没写代码，是槽号一格都没有出处。**
`PvfEquipmentSlots.ByName` 当时的 16 个键里以 `avatar]` 结尾的是 **0 个**，于是
`PvfEquipmentSlotDefinitionSource.GetAsync` 对一件时装答 `null`，
`MoveItemSpaceCommandHandler.PutOnAsync` 就 `Refused(NotWearable, piece.ItemId)`。
**（2026-09-08：11 个时装键已进表，表现在 27 个键，时装能穿上。
下面这一整节记的是当时的诊断，数字都是那时的；结论见本节末尾的补记。）**
同样在表外的还有 `[artifact red/blue/green]`、`[charm]`、`[flag]`、`[name tag]`。
**没有给这张表补键，因为补键就等于写下一个没有出处的数。**

**槽号出处找过三处，三处都不给。**

1. **档案里时装名后面那个数不是槽号。** `[equipment type]` 的形状是「一个名字，后面一个数」
   （`[shoulder] 13`）。把 `equipment/equipment.lst` 的 **118,479** 个模板逐个打开读第一处
   `[equipment type]`：**11 个以 `avatar]` 结尾的名字，后面那个数的取值集合是 `{0}`**
   （样本 66,328 个模板，后面不跟整数的 0 个）。同一次扫描里 `[weapon]` 取到 10 个不同值、
   `[coat]` 10 个、`[ring]` 7 个。**一个取值集合只有 `{0}` 的数，读不出 0..11 里的哪一格。**
2. **`protocol/` 里没有一份说「量到了一件穿在身上的时装」的记载。** 按「时装」二字检索命中
   19 个文件，逐个读过，其中四条明写「没量到」，分处三个文件——
   `NOTI-0x0002.md`（`equipCount` 恒为 0；槽位 11 那条来自上游 `EquipmentTypeInfo.IsCostumeBarSlot`，
   不是量出来的）、`NOTI-0x000D.md`（A21 的时装条目未被观察到）、
   `patterns/character-lifecycle.md` 的未解问题表。唯一列出角色身上有哪几格的是
   `patterns/data/traces/a21-starting-gear-and-skills-20260826.md`，
   它列的 12／14／16 三格都不在 0..11 内。**没有逐文件穷尽 `protocol/` 的 1,819 个文件。**
3. **上游转写只到「带宽十二格」与「槽 11 叫什么」，且自陈不是量出来的。**
   **它给了 0..11 的边界，没有给「11 个类型名 → 12 个槽号」的对照。**

**`ByName` 当时认不出的类型名：17 个，覆盖 66,596 个模板。**
**（2026-09-08 之后：11 个时装名已被认出，认不出的只剩 6 个、覆盖 268 个模板——
就是下表里标「否」的那六行。表本身不动，它记的是当时的扫描。）**
口径：把 `equipment/equipment.lst` 读成「id → 相对路径」（实测 118,479 行、互不相同的 id 与路径），
逐个打开取第一处 `[equipment type]` 后的字符串，按 `OrdinalIgnoreCase` 查 `ByName`，
查不到的按名字归并。这一遍打不开的文件 0 个、读不到类型名的模板 0 个。
**测法**：把 `Server/src/Dfo.Pvf` 的源文件复制到工作区之外另写一次性程序跑；
**这一段的数与判据无关，重跑要另写程序。**

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
加法的每一项都在表里，可当场重算。
**（这三个数是 2026-09-08 之前的。补键之后「认不出」的只剩那 6 行、268 个模板；
11 个时装名与它们的 66,328 个模板现在都认得出，槽号就是本节末尾那 11 个。）**

**`ByName` 当时的 16 个键里有两个在这次扫描中一次都没被用到**：`[title]` 与 `[earring]`。
（称号走的是 `[title name]`，1,796 个模板；24 号槽走的是 `[support weapon]`，26 个模板。
槽号写作 24 的键有三个——`[earring]`、`[support weapon]`、`[creature]`，其中 `[creature]` 707 个模板。
所以「`[earring]` 没被用到」说的只是这一个键，不是 24 号槽在档案里没东西。）
**这不是删它们的理由**：`PvfInitialEquipmentDefinitionSource` 也读这张表，读的是创建角色那张表里
写的名字，不是 `[equipment type]`。
**「那张表里出现哪些名字没有测过」这句已作废（2026-09-08）**——判据
`PvfEquipmentSlotsTests.No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
在钉它：档案里 14 个职业的创建表**只出现 `[weapon]`、`[coat]`、`[pants]` 三个名字**。
（本轮补时装键之前又独立扫过一遍，逐职业零命中——这正是「补键会不会改变建号」的答案。）

#### 2026-09-08：那 11 个时装槽已经量完，表已填上

**下面这套量具是为量这张表造的，表量完了。** 11 件骑士时装逐件拖到装扮槽，
得到 `0..10` 十一个互不相同的槽号；`PvfEquipmentSlots.ByName` 已按此补上 11 个键
（`[hat avatar]`=0 … `[weapon avatar]`=10）。**时装现在能穿上。**
对照表与三来源互证记在 `.rks-agent/tasks/costumes-know-where-they-are-worn/TaskContract.md`。

**槽 11 没有键**：上游枚举叫它 `AuraSkinAvatar`，而**本档案没有任何模板声明它**
（上面那张表就是证据：11 个时装名，没有第 12 个）。

**量具本身留着**，它对「下一个槽号问题」仍然管用——下面三条注意事项也仍然成立，
只是第 1 条与第 3 条描述的现象，起因已经变了（时装现在有槽号了）。

**留下的是量具，不是修法。** 当前 `GameMoveItemSpaceCommandHandler.ResolveAsync` 的拒绝记录写作
`item {ItemId} from list {FromList} slot {From} to list {ToList} slot {To} refused as {Status}`：
`{ItemId}` 是被拒的模板号，`{To}` 是**客户端自己写下的**目标槽号
（判据 `RefusedEquipIsRecordedWithItsTemplateTests` 整句比对）。
**2026-09-08 补上了两个列表号**：在那之前这一行只记槽号，而同一对槽号属于好几条不同的路，
一次实机诊断为此多花了三个来回才把「装扮脱不下来」定位到跨列表移动上。
**但这一行记的是「请求写了哪两格」，不是「东西往哪边走」。** 一次请求不说哪一端拿着东西，
服务端按「哪一端有东西就动哪一端」办，而回执与这一行都照抄请求自己的两端（协议要求如此）。
所以一次反向的脱下会记成 `from list 0 slot 12 to list 3 slot 3`，东西其实是从 `3:3` 到 `0:12`。
**这一行判不出方向，`{ItemId}` 也判不出来（2026-09-08 实机更正）。** 当天所有者把同一件头发
装扮脱下再穿上，两次都记成

```
item 413560000 from list 1 slot 1 to list 3 slot 1
```

**逐字符相同**——两端与物品在两个方向上本来就是同一组数。原文写「要判方向就看 `{ItemId}`」，
那句作为语义描述不错（它命名的是当时拿着东西的那一端），**但当作判读方法不管用**。
真判方向只能靠这一行以外的东西：前后文里那两格各自的状态，或者库。
**一次实机拖拽就能从这一行读出「哪个模板 → 哪个槽」。** 用它之前要知道三件事，
**三条的证据类型都是代码路径读出，没有实机观测过**：

1. **`item 0` 这一条已作废，但作废的方式和第一次写的更正**（也是 2026-09-07，已改正）**不同。**
   原文写：目标列写成 `Avatar`(=1) 而非 `Equipment`(=3) 时请求会被拒为 `ListNotServed`、
   记录读作 `item 0 …->N`。装扮栏接进 `MoveItemSpace` 之后**不再有这一行拒绝**——
   但**不是因为它改走了 `PutOnAsync`**。`PutOnAsync` 的入口条件是**目标列 == 3**
   （`MoveItemSpaceSlice.cs` 的 `HandleAsync`）；目标列 == 1 走的是 `CarryAcrossAsync`，
   **一件时装会被真的搬进装扮栏，`Moved`，没有任何拒绝行**
   （判据 `A_costume_moves_from_the_bag_back_into_the_avatar_list`）。
   非时装或越界槽则得 `ItemCannotOccupySlot`，**那一行带模板号**。
   **对量槽位映射的人：那条路现在是哑的，别走它。**
   **实机观测到的目标列是 `Equipment`(=3)**（2026-09-07 量 11 件时装，全部如此），
   而那条路——`PutOnAsync` → `NotWearable` → 带模板号——**可执行语句逐字符未变**
   （注释改过），量具照旧管用。
2. **「取下」那条路现在说一部分模板号（2026-09-07 更正）。** 原文写 `TakeOffAsync` 的五个拒绝
   返回都是单参重载、`{ItemId}` 恒 0。现在其中**三个**（`NotWearable`、`ItemCannotOccupySlot`、
   `DisplacedCannotOccupySlot`）带模板号，另两个（`NoBoundContainer`、`BothSlotsEmpty`）
   仍不带——它们在读到物品之前就返回了。**要量的那一次仍然是「拖上去」**，这一条没变。
3. **「剩余秒数」那一格应当画出约 11.4 年，不是约 68 年。**
   `GameInventoryDelivery.WriteItem` 对 kind 8 写的是
   `Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`——**是差，不是
   `ExpireDate` 本身**。实算（2026-09-06T09:32:11Z，`now` ＝ 1788687136）：
   `2147483647 − 1788687136 = 358796511` 秒 ÷ `31556952` ＝ **11.37 年**，每过一秒少一。
   **本仓对这个数只用「约 11.4 年」一个说法**，不要写成「十一年半」。
   **约 68 年对应的是 `int.MaxValue` 本身**（`2147483647 ÷ 31556952 = 68.05`），
   看到它、或一个日期、或客户端拒绝渲染，才是要追的。

#### 普通装备数量与实例值

独立装备的 `ItemSnapshot.Count` 恒为 1；旧 core.Value 由 `InstanceValue` 保留。
起始装备和地面掉落的这个兼容值不再参与数量判断，也不等同于 `ItemUid`。
移动和对调通过 Take/Put 保留原 UID，耐久等属性通过 Update 修改。
这段描述是实现与自动化回归证据，不宣称新增 ClientVerified。

### 判据偶发失败：先取身份，再谈别的

有一条已知的 SQLite 并行释放偶发。**判别依据是三样一起看：**

1. 异常类型 `ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`
2. 复跑即绿（**要真的跑**——某些机器上三次全量能红两次）
3. **栈根在下面这份清单内**

已记录的栈根（五条）：

| 栈根 | 见于 |
|---|---|
| `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar` | `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`、`DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`、`DarkKnightComboBookTests.TwoBooksAskedForTheSameCharacterAreTheSameBook` |
| `ItemPathFrameTests.Storage.ReadAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |
| `OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → SqliteCommand.ExecuteReaderAsync` | 同名用例 |
| `AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery` | `AchievementTitleBookTests.Real_title_replaces_mirror_in_one_transaction_and_refusal_or_rollback_preserves_old_wear` |
| `SqliteDatabaseParticipant.CommandAsync → SqliteConnection.BeginTransaction → SqliteCommand.ExecuteNonQuery` | `JoustIntegrationTests.Statistics_failure_rolls_back_mail_completion_and_accounting_marker_then_retry_counts_once` |

**用例名不是判别依据**——第一条栈根已经在三个不同用例名上出现过。
**栈根是**：清单外的新栈根**不得直接归入**，要另给不归因的明证（改动与该路径无交集等），
确认之后把新栈根追加进这张表。

另有一条已登记的内容数据偶发，**身份三样一起看，用例名是其中一样**：

1. 用例 `IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter`；
2. 异常 `InvalidDataException`，消息含 `[minimum level]` 与 `required field is missing`；
3. 栈根是 `PvfEquipmentDefinitionSource` 缺必填字段时的那次抛出（`PvfEquipmentDefinitionSource.cs:160`）。

单独复跑该用例 5 次全过，才归入。用例名在这里算身份，与上一类相反：成因是随机奖励选中了 PVF 里缺
`[minimum level]` 字段的装备，只在这条随机奖励路径上出现；同一栈根出现在别的用例上，可能是另一处内容缺陷，
不归入。这同时是一处生产健壮性缺口：PVF 内容缺这个字段时，奖励结算会直接抛错。

**取不到身份的失败一律是取证缺口，不是结论。** 所以跑全量判据时把完整输出落盘
（`--logger "console;verbosity=detailed"`），不要用会滤掉明细的过滤器——
这条规则是因为同一个成果里连续四次遇到失败、四次都没取到身份才写下来的。
已知的取证缺口两条：`Dfo.Server.Networking.Tests` 的一次未记名失败、
`DarkKnightComboBookTests.ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead`。

### 其他与主设计的偏差（诚实清单）

- `CLIENT_SPEC_STATISTIC`(0x00AB) 经协议 CMD 查询在线归属并打语义化日志，无领域变更或响应；
  这一在线查询使它不属于纯遥测。
  纯遥测的判据是「构造器只注入日志」：经 `RegisterTelemetryMessageHandler` 注册的 handler 留在收包回调里，
  构造器注入了日志以外的东西时 `MessageHandlerRegistry` 构造即抛。
  EXIT 与 Channel Connect 均由协议 CMD 登记纯协议响应，不修改领域状态。
  ScriptVersion、Script、Info 三个命令曾直接读基础设施内容源，现已改为经
  `IChannelDirectoryDomainService` → `IChannelContentRuleService` 取得，目录与脚本同一个所有者。
- 登录失败目前**无法编码**（sender 对失败体直接抛异常），失败以服务器内部
  异常形态存在，客户端看不到错误。
- **修改协议 handler 前检查具体行为覆盖。** 历史的“48 个 handler、19 个无测试”清单已失效；
  `GetItemHandler`、`MoveMapHandler`、`GiveUpGameHandler` 等已有测试。
  类名搜索只能定位候选测试，不能证明覆盖充分。重点检查当前事实读取、事务结果、
  精确会话和发帧行为；有实际回归风险且现有验证不足时，再补能观察该结果的测试。
  `MonsterDeathHandlerTests`、`TownProjectionHandlerTests`、`SelectDungeonHandlerTests`
  可作为相应行为的测试入口。
- `Dfo.Server.Utilities` 是空项目占位。

## 构建与运行

- SDK：`global.json` 写的是 `10.0.100` ＋ `rollForward: latestMajor`，**不锁小版本**。
  （这里曾写作「锁 10.0.302」，与文件不符。）真要绕开 `global.json`，
  可从仓库外目录构建——它按 cwd 向上查找：
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
  而非断言不符。判据是单独跑该项目：`2bdb1afd` 上 Release，`Networking.Tests` 188 条单独跑
  52.8 秒，全量并行中 1.9 分钟（`dotnet test` 报告的「总时间」）。看到这个形状先隔离复跑，不要当缺陷查。
- **两个用例同时「先读后写」同一个库，第二个会等满 30 秒再失败。** 一次用例一条连接、
  一个 deferred 事务，这让用例内的多次读看到同一时刻，也让长读不挡写。代价是：
  用例 A 与 B 都先读、A 先提交之后，B 再写时它手里那份视图已经过期，SQLite 拒绝在其上写。
  等待帮不上忙——过期的是时刻不是锁——但从连接看这就是「库忙」，于是它按
  `Microsoft.Data.Sqlite` 的默认 `Default Timeout` 等满 30 秒才抛。
  今天没有任何地方设过这个超时，也没有任何地方在 BUSY 上重试。
  真正的解法是「整条用例重来」，属于派发用例的那一层，不属于数据库。
  生产中撞不上：写事务只在执行器上开启（执行器外开启即抛），两个写用例不会同时进行。
  直接操作 SQLite、并行运行的测试仍可能撞上，那时第二条用例的调用方会先卡 30 秒再收到失败。

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
   出站编码器（消息 CLR 类型 + 允许端点）、`AddProtocolCommandHandler`（CMD、handler、精确消息绑定）、
   `RegisterProtocolMessageHandler`（目录成员、消息 CLR、CMD）。**这是唯一需要改的接线点**；传输侧不动，
   `ApplicationModule` 不动。
5. 在 CMD 中显式定义每段 Write／ReadOnly 及保护参与者，保持正常拒绝与帧序；不要为整个 CMD 自动包事务。
   测试 codec 向量、薄 handler 的原样输入、真实 CMD／工作单元／精确会话与 Networking 线级行为；
   路由闭合和构造器判据必须包含该类型。完整示例见 [协议 CMD 开发指南](Document/ProtocolCommands.md)。
   注意测试仍按功能组织，尚未跟随切片重排。
6. 客户端提供、会扇出给其他连接（或累积进其他连接一次作业的出站）的变长字段，解码器或规则服务必须有上限，
   并在协议记录里写明上限的依据与依据等级。超过发送队列上限（`OutboundLimits.QueueByteLimit`）的出站会让
   收件的第三方连接被关闭。现有字段由具名 codec／领域规则及线级边界测试守护；尚无能从任意新字段
   自动证明第三方扇出字节上限的通用判据，不能以统一 string 长度检查替代协议分析。

## 约定

### 接口后缀标明类别

`Abstractions` 里的接口后缀就是它的类别，不需要打开实现才知道调用它意味着什么：

| 后缀 | 含义 | 调用它意味着 |
|---|---|---|
| `*DomainService` | 领域服务 | 读写世界状态，**次序与并发有意义** |
| `*RuleService` | 规则服务 | 内容 + 传入的 facts + 显式种子 → 不可变规则实例/值/决定，无角色状态。**内容源与规则配置的唯一读取点**；由业务服务调用，不由 handler 调用 |
| `*Projection` | 投影 | 单个 `Build(存储记录) → 领域值`，无依赖、无 I/O |
| `*Rule` | 规则实例 | 由规则服务返回并共享的不可变静态规则，只接受显式 facts，不读取世界或角色存储，不缓存角色状态 |
| `*Rules`（静态类） | 纯规则 | 纯函数，不碰 I/O。**只由规则服务调用**，是规则服务的下一层而不是它的替代 |
| `*Repository` / `*Registry` / `*Directory` / `*Persistence` | 持久化与运行时注册表 | 各自后缀已表明。**不得注入命令处理器** |
| `*DomainStore` | 领域 store | 领域状态的事务结算真源，被要一个会话、由事务收口。是命令处理器可注入的两种形状之一 |
| `*DefinitionSource` / `*ContentSource` / `*Catalog` | 内容源 | 读归档，确定性、可缓存。**只被规则服务注入** |
| `*Policy` | 策略 | 规则服务的一种，回答"允不允许"；频道规则由领域服务内部调用 |
| `*Query` / `*Reader` | 只读投影/查询 | 只读，不改世界状态 |
| `*Decoder` / `*Encoder` / `*Sender` / `*Dispatcher` / `*Handler` / `*Observer` / `*Closer` | 传输与协议 | 各自后缀已表明 |
| `*Module` | 组合根接线 | `IServerModule`，只注册不执行业务 |
| `IGameplayQueue`（按名字认，不按后缀） | 入队点 | 把一个作业交给执行器，作业执行完返回的任务才完成。只由生产者调用；在执行器上调用并等待即自我等待，以程序错误失败。命令处理器注入不了它：注入面判据按后缀判定，它不在放行之列 |

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

设计依据与推导过程见 `Docs/领域结构重构设计.md`（**那是一份意图书，其中 §2／§6 已落地、
§10／§14 没有**，它自己的开头有落地状态）。

- `ValueTask` 全覆盖；sealed handler + 主构造器 DI；命令与 handler 同文件共置。
- 逆向出来的 legacy 行为在代码里用 XML doc remarks 说明来源，不凭记忆。
- 协议结论变更必须同步 `protocol/` 记录与 catalog 状态；推翻旧结论要保留
  "为什么被推翻"（见 `protocol/README.md` 安全边界一节）。
- 分层问题主要看项目引用图。唯一的分层判据是
  `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`，
  它覆盖 CMD 构造器与结构的允许／拒绝形状；消息／CMD 双向绑定、薄 handler 和纯遥测注入由
  注册检查及对应结构测试共同约束。类型后缀和文档不能替代这些判据。

## 记什么，不记什么

**这个仓库的产出是代码。** 文档与注释是为它服务的，不是与它并列的第二件产出。
写之前先过一句：**有人会拿它做决定吗？** 落不到某个人某次要做的决定上，就不写。

写多了不是「更仔细」，是负资产：**没人读的记载照样会漂，而漂了不会有人发现**——
本仓已经有过好几次，一条记载漂到自己的反面还在原地躺着。

### 注释

**只说代码说不出的东西。** 够格的只有两种：

1. **代码表达不了**——这个值的出处、为什么不是另一种写法、一条只靠约定成立的不变量、
   一处刻意留下的缺口；
2. **扫代码代价很大**——这条路今天没有生产调用方、这个数是怎么量出来的、
   这个形状是从哪一版沿用的。

把代码已经说清楚的事再说一遍，一律不写。
**给个尺度**：`Server/src` 今天注释/代码 ＝ **0.46**（口径：非空行 52,793，
其中以 `///`、`//`、`*`、`/*` 开头的 16,548 行）。而成果 `fa10dfa5` 在 `Server/src/*.cs` 上
新增 **65 行代码、403 行注释**，比 **6.20**，是基线的十三倍——**那不是仔细，是本末倒置**。
一处改动的注释比它的代码还多时，先问是不是在替过程记账。

### 每种事实只有一个归处

抄到第二处就是给自己埋一个漂移点。**要引就引，不要抄。**

| 事实 | 归处 |
|---|---|
| 上游与线上协议的结论及其证据 | `protocol/`（规矩见 `protocol/README.md`） |
| 本仓的架构、约定、今天没有的东西、已知缺口 | **本文件** |
| 一次设计、调查或裁定的推导过程 | `Docs/`，**开头必须带日期与基准提交** |
| 只有读这一段代码时才需要知道的 | 就地注释 |
| 一次成果怎么做的、谁发现了什么、评审说了什么 | 那次成果自己的记载，**哪个仓库文档都不进** |

`Docs/` 里另有一类**描述参照服务端 `ServerS4A21`** 的资料，开头有免责块。
**当业务语义的参照读，不要拿它当本仓事实去取证**——已经出过事。

### 不写的

- **过程。** 谁做的、第几轮、由谁发现、评审怎么裁——读者要的是结论与它今天靠什么撑着。
- **给某个人的一次性交代。** 那是消息，不是仓库知识。
- **没有出处的数。** 写数就写口径；量不出来就写「不足以确定」，不要挑一个看着合理的填上。
- **「以后要做 X」。** 除非它已经是一条今天成立的约束（那就写成约束）。

### 过期的记载怎么办

**看这份文件的价值在不在「当时是怎么想的」。**

- **在**（带日期的裁定、调查、设计）：**保留原文**，在开头补一段落地状态，说明哪条今天
  不成立、今天的答案在哪。`Docs/Refactor/TableOwnershipRuling.md`、`Docs/ItemWorldModel.md`
  就是这么处理的。
- **不在**（本文件这样的现状图）：**直接改掉**，不要另起一段写修正案。
  *这条是有代价才写下的*：本文件一度在「已知缺陷」里写着「时装的种类不一致仍在」，
  而订正在 350 行之后——一个 grep 的读者先撞上假话。

**本文件因此还有三条**：不写「本轮／这一轮」；不指向 `.rks-agent/`
（高层图不该依赖单次任务的过程稿，一个事实重要到要写进这里，就把它自己说完整）；
不写给所有者看的一次性交代。

### 两条从踩坑里换来的记法

- **一个由多项相加得出的数，要么当场把加法写出来（各项、项数、和，三样齐），
  要么就不要写这个小计。** 曾有一份取证文件表是对的、小计算错了 5，下游照抄的都是结论句
  而不是它的表——读的人看不见加法，也就没机会发现小计错了。
- **反向扫要扫写入方，不能只扫判定点。** 「一条拒绝在哪、有没有判据会红」只答了一半；
  另一半是「谁能把系统送进那条拒绝」。曾有一条缺陷正是因为只扫了判定点而漏过：
  清单把它记作「有覆盖」，而实际只覆盖了两个制造者中的一个。

## 2026-09-26 仓库边界

本仓只承载服务端、服务端工具和协议知识库。客户端补丁/调试研究迁往相邻DNF-JP-86-ClientHack；WPF登录器及历史PvfProxy迁往DNF-JP-86-Launcher。Dfo.Server.LauncherApi是服务端HTTP认证端点，保留在本仓。协议消费证据与服务端验收记录保留，不是客户端运行实现。历史任务记录中的旧绝对路径仅作为来源。
