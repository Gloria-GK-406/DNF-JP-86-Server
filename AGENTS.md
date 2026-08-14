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
Tool/        PvfLib（游戏向 PVF 工具库）、PvfProxy（TCP 中转/启动器）
Docs/        帧协议基线、客户端逆向系列文档、业务接入规范
```

## Server 解决方案结构

解决方案 `Server/86JP.Server.slnx`，共享配置 `Server/Directory.Build.props`
（net10.0 / Nullable / **TreatWarningsAsErrors** / Deterministic），中央包管理
（TouchSocket.Hosting 4.3.1、Microsoft.Data.Sqlite、xunit）。

| 项目 | 依赖 | 职责 |
|---|---|---|
| `Dfo.Pvf` | — | 只读 PVF 档案库（cipher/zlib/索引/token），无游戏概念 |
| `Dfo.Server.Data` | — | 纯模型：领域记录 + 网络消息 DTO（`Networking/NetworkMessages.cs`） |
| `Dfo.Server.Abstractions` | Data | 接缝层：`IServerModule` + 各领域服务/仓储/内容源/网络契约 |
| `Dfo.Server.Application` | Abstractions, Data | **领域编排核心**：命令总线、消息分发、handler、领域服务 |
| `Dfo.Server.Infrastructure` | Dfo.Pvf, Abstractions, Data | SQLite 仓储 + PVF 内容源 + 频道脚本 |
| `Dfo.Server.Networking` | Abstractions, Data | TouchSocket 传输：帧适配/解析、codec、TCP 服务、插件、发送器 |
| `Dfo.Server.Host` | 全部 | 组合根：模块注册顺序 + appsettings + channel_info.etc |
| `Dfo.Server.Utilities` | — | 空占位 |

**分层由项目引用图在编译期强制**（Application 不引用 Infrastructure/Networking；
Networking 不引用 Application——它只能通过 Abstractions 的
`INetworkMessageDispatcher` 间接到达 Application）。注意：测试项目的 csproj 注释
声称"有分层断言测试"，实际不存在；`Dfo.Server.Application.Tests` 引用
Infrastructure 是刻意的测试例外（entry 规则要跑真实 PVF）。

## 架构设计

一句话：**领域服务承载业务能力，命令执行总线执行领域命令，
socket 层的 handler 驱动领域命令完成任务。**

```
TCP 字节
  → TouchSocket 适配器（Networking/Framing，13B Game 头 / 11B Channel 头）
  → NetworkPlugin.OnTcpReceived（Runtime/NetworkPlugins.cs）
      帧解析 + payload codec → 强类型 IClientNetworkMessage
  → INetworkMessageDispatcher（Abstractions 接缝，实现在 Application）
  → NetworkMessageDispatcher：按 ClientMessageType 查注册表 → 每 message 新 DI scope
  → ClientMessageHandler<TMessage>（Application/Game|Channel）
      鉴权门（IOnlineUserQueryService）→ commandBus.SendAsync(...) → 发响应
  → CommandBus 管线 → ICommandHandler → 领域服务 → 仓储/内容源
  → INetworkMessageSender（TouchSocketNetworkMessageSender）编码回帧
```

### 1) 领域服务编排业务（Application）

领域按目录组织：`Accounts/`（账号 entry 状态）、`Channel/`（入场策略 + 密钥）、
`Characters/`（花名册/entry 状态/入场后修正）、`Inventory/`（入场租约、每日补货）、
`OnlineUsers/`（在线聚合 + 执行门）、`Sessions/`（在场注册表、选中会话目录、
PvP 大厅、副本重进、专家商店）、`World/`（出生点策略、世界内容）、`Users/`
（`UserService` 一个类实现 4 个账号/在线接口，带 generation 计数）。
服务只做业务；`Infrastructure` 提供仓储与 PVF 内容源实现。

### 2) 命令执行总线（Application/Commands）

- 契约（`CommandContracts.cs`）：`ICommand<TResult>`（CLR 类型即身份），
  `IOnlineUserCommand`/`IAccountCommand` 携带作用域用户 id；
  `CommandExecutionKind` = Direct / Transactional / Workflow；
  `UserAccessMode` = Unscoped / Shared / Exclusive。
- `CommandBus`：每次 `SendAsync` 开新 DI scope → 查 `CommandHandlerRegistry` →
  按 `Order` 排序管线行为、从外到内包装 → 执行。
  **禁止嵌套 dispatch**（handler 里不能再发命令；共享逻辑抽成应用服务）。
- 管线（`Pipeline/`，Order 从小到大为外到内）：
  1. `CommandLoggingBehavior`（0）：计时 + 成功/失败日志
  2. `OnlineUserExecutionBehavior`（50）：按 OnlineUserId 取共享/独占租约
  3. `AccountExecutionBehavior`（55）：按 AccountId 取租约
  4. `CommandExecutionPolicyBehavior`（100）：Direct/Workflow 放行；
     **Transactional 一律抛异常**（设计占位，未接 unit-of-work；原子性目前
     由各 SQLite 仓储自己的事务负责，无生产命令使用它）
- 执行门（`OnlineUsers/UserExecutionGate.cs`）：按 key 的读写锁 + FIFO 等待队列；
  三个实例：OnlineUserExecutionGate、AccountExecutionGate、
  SelectedSessionTransitionGate（第三个由 SelectCharacter 命令手工获取，
  串行化会话切换/拆除）。
- 错误语义：异常直接上抛，**总线级无回滚**；补偿由 handler 自理
  （见 SelectCharacter 的失败路径）。
- 注册（`ApplicationModule.cs`）：现有命令
  LoginGameUser = Direct+OnlineUser Exclusive；GetAccountCharacterRoster =
  Direct+Account Shared；SelectCharacter = Workflow+Account Exclusive；
  Channel 系五个 = Direct。
- 命令注册表**没有**启动完备性检查（未知命令 dispatch 时才报错）——与消息
  注册表相反。

### 3) Socket 层：handler 驱动领域命令

- Networking 只做传输：`ChannelNetworkPlugin`/`GameNetworkPlugin` 把帧变成
  类型化消息（Game 侧按 type 走 payload codec：Login 双 D-string、
  SelectCharacter u16 槽位、GetUserInfo mode 字节、Exit 空；Channel 侧**不解码
  body**，消息号即触发器），然后丢给 dispatcher。未知 type 静默丢弃。
- 消息注册表（`MessageHandlerRegistry`）**有**启动完备性检查：
  `ClientMessageType` 每个 enum 成员必须有 handler，否则启动即抛
  ——消息来自网络，不允许存在"没人接"的类型。
- handler 统一模式：解析连接身份 →（可选）`IChannelEntryPolicy` 解析频道 →
  发命令 → 用 `INetworkMessageSender` 回响应。handler **不碰仓储**。
- 发送器按端点校验（Channel 消息只能发往 Channel 连接），Channel 载荷走
  AES-128-ECB + zlib + 当日密钥；Game 出站 15 字节头。
- 连接生命周期：`OnlineUserConnectionObserver` 在 TCP 连接/断开时直接调
  在线服务/清理服务/发送器（**不经过命令总线**——连接事件不建模为命令）。

### 端到端示例：GameLogin

1. 13B 头帧 → `GameClientFrameDataHandlingAdapter` → `GameNetworkPlugin`
   识别 cmd 0x01/type 0x0001，`GameLoginPayloadCodec` 解出 loginId + 密码哈希
2. dispatcher → `GameLoginHandler`：要求该连接已有匿名在线注册
   （TCP 连上时由 `OnlineUserConnectionObserver` 创建并推送初始连接通知）
3. `commandBus.SendAsync(LoginGameUserCommand)` → 管线：日志 →
   OnlineUser 独占租约 → Direct 放行 → `LoginGameUserCommandHandler` →
   `IAccountService.LoginAsync` → SQLite `GetOrCreate` → `OnlineUser.Authenticate`
   （generation +1）
4. handler 发 `GameLoginResponse(true)` → 编码成功体（服务器 IP、环境字节、
   LoginChannelPort 10128、UnknownPort 17200）→ 15B 头出站

集成测试 `Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs`
用真实 TouchSocket host + SQLite 断言逐字节行为。

### SelectCharacter：最大的例外（要牢记）

`SelectCharacterHandler` **不发响应**——`SelectCharacterCommandHandler`
（约 200 行编排器）拥有全部次序：generation 防陈旧检查、手工
SelectedSessionTransitionGate 独占、入场前置（频道拒绝 → 发拒绝投影再抛）、
旧会话租约保存与精确清理、`PrepareAsync`（700 行准备服务：背包/技能/世界
内容/出生点/PvP 合并…）、59 帧静态 G6 流 + 补充投影的**有序投递**、
每日补货、在场发布、PvP 计划、副本重进邀请；失败路径执行补偿清理后重抛。
这里"编排者"是**命令**而非服务——服务只准备状态，命令拥有次序、投递与回滚。

### 其他与主设计的偏差（诚实清单）

- `CLIENT_SPEC_STATISTIC`(0x00AB) 的 handler 直接打语义化日志，**不经过命令总线**
  （纯遥测、无领域变更、无响应；见 protocol 记录的 Implementation mapping）。
  存在水命令：`AcknowledgeGameExitCommand` 返回常量；Channel 系命令是单行
  转发——为的是统一日志/作用域，不是业务复杂度。
- 登录失败目前**无法编码**（sender 对失败体直接抛异常），失败以服务器内部
  异常形态存在，客户端看不到错误。
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

## 快速开发：新增一个协议的固定动作

1. `protocol/<endpoint>/<direction>/<type>-<name>/README.md` 按模板建记录，
   登记 `protocol/catalog.md`（逆向证据先行，布局确认后才可具名字段）。
2. Data：消息 DTO + `ClientMessageType`/`ServerMessageType` 枚举
   （新枚举成员必须同时注册 handler，否则启动失败——这是刻意的）。
3. Networking：入站 codec（`Framing/`）+ 出站编码（sender 模式匹配处）。
4. Application：handler（`Game/` 或 `Channel/`）→ 需要业务就定义命令 +
   handler + `ApplicationModule` 注册（选好 ExecutionKind 和 AccessMode）→
   领域服务承载逻辑。
5. 测试三件套：codec 向量、handler/命令单测、Networking.Tests 线级集成测试。

## 约定

- `ValueTask` 全覆盖；sealed handler + 主构造器 DI；命令与 handler 同文件共置。
- 逆向出来的 legacy 行为在代码里用 XML doc remarks 说明来源，不凭记忆。
- 协议结论变更必须同步 `protocol/` 记录与 catalog 状态；推翻旧结论要保留
  "为什么被推翻"（见 `protocol/README.md` 安全边界一节）。
- 分层问题看项目引用图，别找"分层测试"（不存在）。
