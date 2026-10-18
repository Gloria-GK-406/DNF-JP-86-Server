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
| `Dfo.Server.Application` | Abstractions, Data | **领域编排核心**：命令总线与管线、消息分发机制、领域服务 |
| `Dfo.Server.Infrastructure` | Dfo.Pvf, Abstractions, Data | PVF 内容源 + 频道脚本 + 启动配置内存快照 |
| `Dfo.Server.Persistence` | Abstractions, Data | SQLite 仓储与存储 + `Schema/` 唯一建表归属者 + `Database/` 事务参与者。**全部类型 `internal`**，对外只经 Abstractions 契约 |
| `Dfo.Server.Networking` | Abstractions, Data | TouchSocket **传输**：帧适配/解析、TCP 服务、插件、发送器、注册表。收发路径上不含任何单个协议的知识；显式多收件人投递也只表达必达顺序和逐目标失败隔离 |
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
`Characters/`（花名册/entry 状态/入场后修正）、`Inventory/`（入场读取、物品生命周期、每日补货）、
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
    → 规则服务       内容 + 传入的 facts + 显式种子 → 不可变规则实例/值/决定，无角色状态
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

例外必须进入具名豁免清单并写明理由。目前包括 `TimeProvider`（环境时钟，不是这个游戏知道的
东西），以及用户明确采用领域名称的 `IPartyService`（队伍聚合与目录的权威服务边界，不追加
`Domain` 后缀）。清单与它每一项的理由都写在守它的判据旁边；其它接口不得借此绕过后缀规则。

**这条由判据守着**：`Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`
反射遍历全部 `ICommandHandler<,>` 实现的构造器参数。它按类型判定而不是按一张类型名单——
名单要人手维护，而没人维护的名单等于没有规则。同一个判据另有一条用例专门证明它会拒绝
（喂给它行网关、持久化接缝、运行时容器与命令总线，断言全部被拒），否则一条什么都放行的规则
和一个没有违规的仓库看起来一模一样。

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
| 坐标 | `ICharacterPositionDomainService` | 6 个生产注入点：`TownProjectionSlice.cs:216`/`:335`、`DungeonSelectionSlice.cs:173`、`LeaveDungeonSlice.cs:91`、`SelectCharacterLifecycleServices.cs:33`/`:132` |
| 任务 | 既有的任务簿实例 `ICharacterQuestLogDomainService` | 进本时收集迷宫筛选用的 facts（`DungeonEntrySlice.cs:477-483`） |
| 设置（热键、选项位图、可见位） | `ICharacterSettingsDomainService` | **零个。刻意的，见下。** |
| 暗骑连招页 | `IDarkKnightComboDomainStore` + `IDarkKnightComboBook` | **零个。刻意的，见下。** |
| 疲劳 | `ICharacterFatigueDomainService` | 进本处报告消耗（`DungeonEntrySlice.cs:509-511`）。**接缝不记账，见下。** |

`ISelectedSessionRuntimeDirectory` 只保留精确连接身份的发布标记和位置持久化策略；
`SelectCharacterResult` DTO 本身仍完整，作为一次性组包结果使用，不是运行期角色聚合。
入场后的当前视图走只读重建，不重复执行选角恢复、技能和解锁等写入。

坐标运行期由 `ICharacterPositionDomainService` 持有；当前视图通过独立的
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
     后提交；显式标记 `RequireRollback()` 的正常拒绝结果也先回滚再返回，其他失败出路回滚；其余执行方式原样放行。**Order 必须严格落在
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
   `IEventService.ProjectAsync(...)`，以及把两者按顺序打包好的投递服务
   （如 `ICleanupNotificationDelivery.DeliverAsync`）——见下面的「领域事件与即时投影」。
   即时投影是投递，不是业务：谁该收到由前面的命令决定，投影只负责按原顺序送出。
   handler 不发布领域事实；事实由实际变更所属的领域服务在事务中记录。
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

### 领域事件与即时投影

组件统一依赖 `IEventService`；领域服务发布事实，订阅者自行决定如何响应。通知只是消费者之一。
组件不得直接依赖 `INotificationBus`：只有 `NotificationProjectionSubscriber<T>` 适配器、
通知运输机制与组合注册接触该接口。协议编码和 Socket 发送仍由 Protocol/Networking 拥有。

- `EnqueueAsync<TEvent>`：当前可写业务事务内记录强类型事实与匹配订阅的 delivery；
  与业务使用同一个 SQLite 事务。提交后只向容量 1 的 Channel TryWrite 唤醒，回滚不投递。
  每事务一个 BatchId，每事实一个 EventId，payload 不带服务、连接、协议消息或可变聚合。
- `DirectAsync<TEvent>`：不入队、直接依序等待运行时订阅者；不自行开启事务，继承调用方当前事务，
  可在事务内外调用，失败与取消向上传播。次数写入订阅要求当前可写业务事务，扣次事实不注册为队列订阅；
  提交后的界面刷新另由持久化变化事件进入 ProjectAsync。马战维护仍在事务外发布阶段变化，
  查询不发布；启动恢复静默、变化去重、失败不重复公告。
- `ProjectAsync<TNotification>`：等待即时协议投影，原样返回 NotificationDelivery；
  不持久化 Session/CMD 回复，也不能在未提交的可写事务内调用。登录、队伍、库存等流程
  保持批次、精确会话与 ACK 顺序。单连接直接 sender 和明确协议投递工作流仍可使用。

事件稳定名称/版本与 subscriber ID 在组合根注册。每订阅组独立异步循环，闲时 await
唤醒或最近重试时间，启动扫描遗留工作，停服取消未完成消费。Reliable 订阅在新 scope、
数据库准入门及新事务中处理，业务更新与完成标记同事务；失败回滚后另事务记录重试。
Reliable 不能重入命令管线，也不能在该门内再获取外层账户/会话锁。组内串行，失败退避期间
后续到期事实可先处理，不保证严格 FIFO。当前只注册每个稳定名称的一个版本；未知版本保留
诊断，不自动 upcast；新订阅不回填历史 delivery，完成记录暂不自动清理。

LiveOnly 只处理本进程的新鲜事件：发送前提交一次消费决定，离线、旧进程或过期提示跳过，
失败不补播；崩溃窗口允许漏提示。邮件每封记录 MailCreated，提醒按 BatchId/角色合并，
不以合并提醒丢失事实。GM 活动配置在领域事务记录变化，HTTP 不负责广播。

协议即时投影仍是明确的“命令完成，然后发送结果”：

```csharp
var result = await commandBus.SendAsync(new MarkSelectedCharacterPvpReadyCommand(...), ct);
if (result.Publication.Audience.Count > 0)
    await eventService.ProjectAsync(
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

**当前消费边界：**

- 人群通知用于 PvP 大厅公示、会话清理以及队伍城镇投影等路径；
  `PartyTownProjectionDelivery` 使用 `TheseSessions` 指定精确会话。
- `SessionAdjacentEntryDomainService` 给入场者本人计算周围角色，不因此成为多人扇出。
- 事件服务拥有领域订阅与分发；通知总线是低层投递机制，`NotificationRetryDrainService`
  只排空运输层的既有重试队列，不处理可靠领域事实。
- 回给请求方的消息仍可直接投递，不必套上人群抽象。
- 没有专用的频道、全服或组队 Audience 类型；队伍业务已存在，使用现有精确会话人群。
  人群 record 层级不能完全阻止外部派生，解析器会明确拒绝未知形态。

**投递结果既有摘要，也有日志。** 总线返回成功、弃权、离线与失败结果；
`PartyTownProjectionDelivery` 已读取成功和失败集合，用于维护实际送达的受众与后续处理。
读取摘要仍是可选的，不能依赖每个调用方都读取它来保证可观测性。
所以总线自己记日志，在还拿得到原因的地方记：

- 投递抛异常 → `Warning` + 异常本身；
- 到了每收件人期限 → `Warning`，与抛异常**分开说**（超时也是从 catch 里出来的，异常就在手上；
  不记它是因为那是取消令牌，不是原因）；
- 重试额度用尽、彻底放弃某个收件人 → `Warning`；声明了 0 次重试的通知不记这一条，
  因为它从来就没有第二次机会可放弃，失败那一条已经说过了。

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

**留下的是量具，不是修法。** `MoveItemSpaceHandler.ResolveAsync` 的拒绝记录写作
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
  EXIT 与 Channel Connect 均为纯协议直接响应，不修改领域状态。
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
| `*RuleService` | 规则服务 | 内容 + 传入的 facts + 显式种子 → 不可变规则实例/值/决定，无角色状态。**内容源与规则配置的唯一读取点**；由业务服务调用，不由 handler 调用 |
| `*Projection` | 投影 | 单个 `Build(存储记录) → 领域值`，无依赖、无 I/O |
| `*Rule` | 规则实例 | 由规则服务返回并共享的不可变静态规则，只接受显式 facts，不读取世界或角色存储，不缓存角色状态 |
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

设计依据与推导过程见 `Docs/领域结构重构设计.md`（**那是一份意图书，其中 §2／§6 已落地、
§10／§14 没有**，它自己的开头有落地状态）。

- `ValueTask` 全覆盖；sealed handler + 主构造器 DI；命令与 handler 同文件共置。
- 逆向出来的 legacy 行为在代码里用 XML doc remarks 说明来源，不凭记忆。
- 协议结论变更必须同步 `protocol/` 记录与 catalog 状态；推翻旧结论要保留
  "为什么被推翻"（见 `protocol/README.md` 安全边界一节）。
- 分层问题主要看项目引用图。唯一的分层判据是
  `Server/tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs`，
  它只覆盖**命令处理器**的构造器注入面；普通消息 handler 的能力清单仍然只写在本文档里，
  没有判据守着。

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
