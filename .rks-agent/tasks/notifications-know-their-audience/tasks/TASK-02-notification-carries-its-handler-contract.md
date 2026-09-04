# TASK-02 — 通知类型自带它的处理器契约

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 通知标记类型、处理器接口（按收件人产出该收件人的消息，产出空表示弃权，只读）、处理器注册表与启动校验。
- Referenced spec requirements: RQ-001, RQ-002, RQ-005, RQ-006, RQ-007
- Referenced plan sections: T-002, BD-003, BD-004, BD-010
- Owned boundary: `Server/src/Dfo.Server.Abstractions/Notifications/`、`Server/src/Dfo.Server.Application/Notifications/`、`Server/tests/`
- Permitted files or interfaces: 上述路径；ApplicationModule 的接线
- Dependencies: TASK-01
- Required dependency outputs: 人群类型与解析器
- Acceptance conditions:
  - 调用点指定一个与实参不符的通知类型**不能通过编译**，且这条要有具体的编译器拒绝记录（错误码 + 消息），不是「看起来不行」
  - 未注册处理器的通知类型在**开始接受连接之前**被拒绝；「之前」这一条要有可核对的依据（注册顺序或等价证明），不得只凭断言
  - 处理器接口写明只读；本成果新增的处理器不写任何状态
  - 全量判据通过
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-005, CK-006, CK-007, CK-008, CK-009
- Required evidence: 总线与注册的签名；一次故意不匹配的编译尝试及其原始编译器输出；启动校验位置的依据；处理器注入面的检视

## Task Unit Contract Amendment — Revision 2

- Contract revision: 2
- 改动范围：Required checks 里的两条检查改挂到能做它们的任务上。受理条件与边界不变。

**CK-006（一次故意不匹配的调用尝试不能通过编译）移到 TASK-03。** 它检的是**发布调用点**，而发布方法
`PublishAsync` 属于 BD-005，计划把 BD-005 划给 T-003。在 TASK-02 里没有调用点可探，只能改探一个同约束
的替身泛型方法——那证明的是「约束成立」而不是「调用点选不错」，比要求的弱。移到 TASK-03 做，探针探真的
`PublishAsync`。

**CK-008（检视处理器实现不写状态）在 TASK-02 只做接口部分。** 本任务不新增任何处理器实现，第一个生产
处理器要到 TASK-05 才出现。机检 `NotificationHandlerArchitectureTests` 一并放到 TASK-05——在 TASK-02
写它会得到一个空扫描，而空序列里没有违规。**这一条要写进 TASK-05 的契约，不得遗漏。**

## Execution Result — Round 1

### 交付

- 新增 `Server/src/Dfo.Server.Abstractions/Notifications/INotificationHandling.cs`：
  `INotification<TSelf> where TSelf : INotification<TSelf>`、
  `INotificationHandler<TNotification>.ComposeAsync(recipient, notification, ct)
   → ValueTask<IReadOnlyList<IServerNetworkMessage>>`
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationHandlerRegistry.cs`：
  `NotificationHandlerRegistration(NotificationType, HandlerType, ComposerType, RetryLimit)`、
  `NotificationCatalogRegistration(Assembly)`、`NotificationHandlerRegistry`（重复注册即抛；
  已登记目录的程序集里每个通知类型都必须有处理器，缺的**一次全报**）
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationComposer.cs`：
  `INotificationComposer`（internal，类型擦除）与 `NotificationComposer<TNotification>`
- `NotificationDispatchingExtensions` 扩充：`AddNotificationCatalog(Assembly)`、
  `AddNotificationHandler<TNotification, THandler>(int retryLimit)`、注册表与
  `NotificationHandlerRegistryValidationService`（`IHostedService`）
- 新增 `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationRegistrationTests.cs`（6 条）

### 两个设计选择，及其理由

**1. 自指泛型 `INotification<TSelf>`，不是光秃秃的标记接口。**

若用 `where TNotification : class, INotification`，则 `PublishAsync<INotification>(audience, x, ct)`
**能编译**——接口类型满足 `class` 约束，且 `INotification` 实现 `INotification`。查处理器会在运行时落空。
自指约束下 `INotification<X>` 不满足 `INotification<INotification<X>>`，这条路编译不过。
（探针留给 TASK-03，那里才有 `PublishAsync`。）

**2. 通知的完备性检查存在，而命令的没有——这不是不一致。**

`CommandHandlerRegistry` 的注释写明它故意不做完备性检查，理由是命令只因代码构造而存在，未注册是
「派发时的问题」。**这条理由在通知上不成立**：命令没找到处理器会抛进那个正在等结果的请求里，有人会看见；
而通知是尽力而为的，没人在等，未注册的通知与「今晚没人在线」长得一模一样。所以必须在启动时发现。
这个差别已写进注册表的 `<remarks>`。

目录按**程序集显式登记**而不是扫描全部已加载程序集：后者会把测试里随手写的通知类型变成服务器的启动失败。

### 检查

| ID | 命令 / 方式 | 结果 |
|---|---|---|
| CK-005 | `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，0 个警告，0 个错误 |
| CK-006 | 见修订 2：移到 TASK-03 | — |
| CK-007 | `A_catalogued_notification_with_no_handler_is_refused` + `The_registry_is_asked_for_before_the_listeners_are_registered` | 通过。后者走 `ApplicationModule` → `NetworkingModule` 两次 `ConfigureServices` 之后的实际描述符序列，断言校验服务的下标小于 networking 段里**每一个** `IHostedService` 的下标（不点名 TouchSocket 任何类型） |
| CK-008 | 接口部分：`INotificationHandler<T>` 只有一个返回消息的方法，无写入语义；`<remarks>` 写明只读及其三条理由。**实现部分本轮无对象**（零生产处理器），机检移交 TASK-05 | 见修订 2 |
| CK-009 | 全量判据 | 见下 |

`dotnet test ... --filter "FullyQualifiedName~Notifications"` → 14 通过 / 0 失败（TASK-01 的 6 条 + 本轮 6 条 + 2 条
其他命名空间同名匹配项，见下方自检）。

### Executor Self-check — Round 1

- 边界内：Notifications 两个目录下的四个文件、一个新判据文件。`ApplicationModule.cs` 本轮**零改动**
  （TASK-01 已加过那一行）。未改任何既有断言。
- **一处我没验的**：上面 `--filter "FullyQualifiedName~Notifications"` 报 14 条，而 TASK-01 是 6 条、
  本轮我写了 6 条 = 12。差的 2 条是过滤器匹配到了别处含 "Notifications" 的用例，我**没有**逐条核对是哪两条。
  以全量判据的净增数为准。
- 未做的：`AddNotificationCatalog` 目前只有判据在用，`ProtocolModule` 的登记要到 TASK-05
  （第一个通知类型出现时）。**若 TASK-05 忘了登记，RQ-002 就对生产代码不生效**——TASK-05 必须补一条判据
  断言 Protocol 程序集已被登记。
- 处理器与 composer 注册为 Scoped，与命令处理器一致；总线从 scope 里解析，重试时自建 scope。

### 补上自检里那处未验的（同一轮内查清）

`--filter "FullyQualifiedName~Notifications"` 那 14 条，逐条列出后，多出的 2 条是既有用例，
名字里含 "Notifications" 被子串匹配到，与本成果无关：

```
Dfo.Server.Application.Tests.OnlineUsers.UserServiceTests
    .DisconnectSelectedSession_AttemptsResolvedNotificationsWhenCleanupFails
Dfo.Server.Application.Tests.Sessions.SessionGenerationOwnershipTests
    .CleanupFailure_PreservesAlreadyResolvedOwnerAndPeerNotifications
```

`Notifications` 命名空间下的实际是 12 条 = TASK-01 的 6 + 本轮的 6。

### CK-009 全量判据

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

510 + 52 + 60 + 6 + 256 + 223（跳过 2）+ 974 = **2081 通过 / 2 跳过 / 0 失败**。
TASK-01 收尾时为 2075，净 +6，与本轮新增的 6 条判据数相符。未遇既知偶发失败。

## Independent Quick Review — Round 1

- Verdict: **changes_required**
- 审的契约：revision 1 + Contract Amendment Revision 2
- Owned diff：基线 `b05d47e4`，未提交。五个文件——
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationHandling.cs`（新）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationHandlerRegistry.cs`（新）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationComposer.cs`（新）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs`（扩充）、
  `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationRegistrationTests.cs`（新）。
  `git status` 复核：本轮唯一的已跟踪改动是 `ApplicationModule.cs`（+2），内容为 TASK-01 的 using
  与 `AddNotificationDispatching()` 那一行——与自检「本轮零改动」一致。边界内，无越界文件。
- 检视的命令与证据：全量判据一次独立复跑；四个 scratchpad 编译/运行探针（工程建在 scratchpad，
  验完已删；`git status` 复核工作区与审前逐字一致，未留任何改动）。

### 先说复核通过的部分

**计数（第 5 项）——七个分项与合计逐项一致。** 自己跑
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`：
Protocol 510、Networking 52、Pvf 60、Host 6、Persistence 256、Infrastructure 223（跳过 2）、
Application **974** = **2081 通过 / 2 跳过 / 0 失败**，exit 0。对照 TASK-01 记录的
`…+968 = 2075`：净 +6 全部落在 `Application.Tests`，其余六项一字未动，与本轮 6 条新判据吻合。
未出现既知偶发失败（无 `ObjectDisposedException: 'SQLitePCL.sqlite3'`）。

**第 1 项「光秃秃标记接口能编译、自指约束不能」——两半都成立，执行方这句是真的。**
(a) 独立 scratch 工程，`public interface INotification;` 加
`PublishAsync<T>(T) where T : class, INotification`，
调用 `Bus.PublishAsync<INotification>(anything)` → **已成功生成，0 个警告，0 个错误**。
(b) 引用工作区已编译的 `Dfo.Server.Abstractions.dll`（即本轮真实产出，不是誊抄一份接口），
`PublishAsync<T>(T) where T : class, INotification<T>`，调用 `PublishAsync<INotification<Thing>>(...)` →

```
error CS0311: 类型"Dfo.Server.Abstractions.Notifications.INotification<ProbeB.Thing>"不能用作泛型类型
或方法"Bus.PublishAsync<TNotification>(TNotification)"中的类型参数"TNotification"。没有从
"…INotification<ProbeB.Thing>"到"…INotification<…INotification<ProbeB.Thing>>"的隐式引用转换。
```

正对照（`PublishAsync<Thing>(thing)` 与类型推断两种写法）均成功生成。**自指泛型这个设计的理由成立。**

**第 2 项「开始接受连接之前」——判据确实覆盖了它，而且真实保证比判据声称的更强。**
逐条核了三个环节：

1. 生产环境的模块顺序：`Server/src/Dfo.Server.Host/Program.cs:31-32` 是 `ApplicationModule()` 在
   `NetworkingModule()` 之前，`ServerModuleRegistrationExtensions.AddServerModules` 按数组顺序
   `foreach` 调 `ConfigureServices`。判据里那两次调用与生产同序。
2. **networking 段的 `IHostedService` 确实是开监听口的那个**（这是原问题的关键）。scratch 运行探针：
   `services.AddTcpService<ITcpService, TcpService>(...)` 恰好注册 **1 个** `IHostedService`（工厂式，
   运行时类型 `TouchSocket.Hosting.HostedServices.ServiceHost<ITcpService>`）；`StartAsync` 之前
   `ServerState=None`、端口未监听，`StartAsync` 之后 `ServerState=Running`、端口已监听。
   `Networking:Enabled=true` 下 `NetworkingModule` 调两次 `AddTcpService`（Channel 与 Game），
   所以判据里的 `networkingHosted` 就是那两个开口者本身，`Assert.NotEmpty` 也挡住了空集合放行。
3. `IHostedService` 的启动顺序确实由注册顺序决定（默认 `ServicesStartConcurrently=false`，顺序启动）。

补充一条比判据更强的事实：scratch 运行探针复刻本轮结构（构造器注入一个会抛的单例，再后注册一个
"监听者"），`host.StartAsync()` 抛在
`Host.StartAsync → GetRequiredService<IEnumerable<IHostedService>>` 里，**两个 hosted service 的
`StartAsync` 一个都没跑**。即 `IEnumerable<IHostedService>` 是先整体解析再逐个启动的，而校验服务
用构造器注入注册表，所以抛点必在任何 hosted service 启动之前，**与注册顺序无关**。
判据成立，只是它钉的是一条更弱、且对模块顺序敏感的性质（见 F6）。

**第 3 项 `CommandHandlerRegistry` 的对比——如实转述，结论不过强。** 原文是
「There is no completeness check: unlike a client message, which arrives off the wire and must
always find a handler, a command only exists because code constructed it, so an unregistered one
is a problem at dispatch rather than at startup.」执行方与 `<remarks>` 的转述与之相符。
执行方新引入的区分轴（失败可见 vs 静默）不在原注释里，但被明确写成「那条理由在通知上不成立」，
既没有冒充原注释的话，也没有推出比「通知必须在启动时查」更远的结论。这一条我判**站得住**。

**第 4 项 `A_catalogued_notification_with_no_handler_is_refused` 的稳定性——不会因别处新增通知而
失效或误报。** 目录是测试程序集，`registrations` 传 `[]`，所以 `UnhandledProbe` 恒在缺口清单里；
将来在该程序集新增通知类型只会让消息变长，`Assert.Contains` 那一条依旧成立；反向误报要求消息里
凭别的原因出现 `UnhandledProbe` 这个串，不现实。用 `Contains` 而非精确相等的理由（目录是整个
程序集）与机制一致。残留风险只有 `Assembly.GetTypes()` 遇到加载不了的类型会抛
`ReflectionTypeLoadException`，届时是换一种红，不是变绿。**可以接受。**

**第 6 项 可证伪性，挑两条自己判：**

- `A_catalogued_notification_with_no_handler_is_refused`：把 `NotificationHandlerRegistry` 里
  `gaps.Length > 0` 那段删掉即变红，且它是 RQ-002 唯一的直接守卫。**可证伪。**
- `The_registry_is_asked_for_before_the_listeners_are_registered`：把
  `AddNotificationDispatching()` 从 `ApplicationModule` 挪到 networking 之后即变红；
  `Assert.NotEmpty(networkingHosted)` 排除了「networking 一个 hosted service 都没有」时的空转通过。
  **可证伪。**（第三条 `An_assembly_that_declares_no_notification_leaves_nothing_to_handle` 不好，见 F5。）

### 需要修的

**F1（阻断） Amendment Revision 2 把两条检查「移走」了，但没有任何任务接住它们。**
逐字核对下游两个契约文件：

- `tasks/TASK-03-one-publish-delivers-one-by-one.md` 仍是 `Contract revision: 1`，
  `Required checks: CK-010, CK-011, CK-012, CK-013, CK-014` —— **没有 CK-006**。
- `tasks/TASK-05-pvp-publication-goes-through-notifications.md` 仍是 `Contract revision: 1`，
  `Required checks: CK-021, CK-022, CK-023, CK-024, CK-025` —— **没有 CK-008 的机检部分，
  也没有「Protocol 程序集已被登记」那条**。

修订 2 自己写着「**这一条要写进 TASK-05 的契约，不得遗漏**」，而这件事到本轮结束仍未发生；
执行方自检里点名的第三条前向义务（TASK-05 必须断言 Protocol 程序集已被 `AddNotificationCatalog`
登记，否则 RQ-002 对生产代码不生效）同样没有落到任何契约里。当前状态不是「检查移到了别处」，
是「检查被摘掉了」。这正是修订 2 该不该成立的分水岭：**理由可以成立，但只有在落点真的写下来之后。**

**F2（阻断） 修订 2 给 CK-006 的理由——「在 TASK-02 里没有调用点可探」——是假的，我探到了。**
`AddNotificationHandler<TNotification, THandler>` 就是本轮交付里一个**真实的生产调用点**，
两个类型实参必须相互吻合，它自己的 XML doc 还写着「the two type arguments must agree: the handler
must handle this notification, or the call does not compile」。引用工作区已编译的
`Dfo.Server.Application.dll`，喂一对不吻合的实参：

```
error CS0311: 类型"ProbeC.HandlerOfOther"不能用作泛型类型或方法
"NotificationDispatchingExtensions.AddNotificationHandler<TNotification, THandler>(IServiceCollection, int)"
中的类型参数"THandler"。没有从"ProbeC.HandlerOfOther"到
"Dfo.Server.Abstractions.Notifications.INotificationHandler<ProbeC.Thing>"的隐式引用转换。
```

（正对照 `AddNotificationHandler<Other, HandlerOfOther>` 成功生成，0 警告。）
这不是「同约束的替身泛型方法」，是本任务自己交付的那个方法。另外：修订 2 明写「受理条件与边界
不变」，而受理条件第一条要求「具体的编译器拒绝记录（错误码 加 消息）」——现在这条受理条件在本轮
没有任何证据支撑，而唯一该产出它的检查被移走了。**契约里留了一条无证据的受理条件，这是自相矛盾。**
顺带：`AddNotificationHandler` doc 里那句编译期断言，本轮同样没验过，上面这段就是它的证据。

**F3（阻断） `INotificationHandling.cs:33` 用现在时引用了一个不存在的判据。**
原文：「`NotificationHandlerArchitectureTests` holds the line by refusing anything but a domain
service on the injection surface.」全仓 grep `NotificationHandlerArchitectureTests` 只有这一行注释
命中，判据本身按修订 2 已推迟到 TASK-05。`AGENTS.md` 恰好把这个反模式记成教训：
「一句『不跨层』的记载不会阻止跨层，只会让下一个读它的人以为没有跨层。所以命令处理器那条注入规则
现在由判据守，而不是由这张表守。」对照同目录 TASK-01 的 `INotificationServices.cs`——它把做不到
的地方逐条写明「it is less than 'closed' -- so here is exactly where it stops」——本文件的写法是
反方向的。

### 记录但不阻断

**F4 `INotification<TSelf>` 的 `<remarks>` 有一处比 C# 能给的强。** 原文说它
「makes the type at a publish call the concrete one and nothing else」。探针结果：
widening 到**契约接口**确被挡（上文 CS0311，核心论断为真），但 widening 到一个非 sealed 的
**基 record** 能编译——

```csharp
public record OpenBase(int Value) : INotification<OpenBase>;
public sealed record Narrower(int Value, string Extra) : OpenBase(Value);
Bus.PublishAsync<OpenBase>(narrower);   // 已成功生成
```

实践上通知都会是 sealed record，所以这不是功能缺陷。但 spec 的 Corrections C-1/C-2 两条正是为
这一类「类型系统承诺说过头」而追加的，同一把尺子下建议补一句限定（挡的是 widening 到契约接口；
基类那条靠通知都是 sealed）。

**F5 `An_assembly_that_declares_no_notification_leaves_nothing_to_handle` 的理由与机制对不上。**
它的 `<remarks>` 说「The contract assembly declares the notification interface itself…If the sweep
counted that, wiring the catalogue would fail」。反射探针实测：`Dfo.Server.Abstractions` 共 505 个
类型，**加不加 `IsAbstract` / `IsInterface` 两个过滤器，命中集合都是空**——因为 `INotification<TSelf>`
不出现在它自己的 `GetInterfaces()` 里。也就是说它描述的那个变异不会让这条判据变红。判据本身
（空目录不得报缺口）不算错，只是弱，且理由写偏了。建议要么改掉理由，要么换成一个真能红的边界
（例如放一个实现了 `INotification<>` 的 abstract 类进去，断言它不算缺口）。

**F6 顺序判据把它要证明的前提写死在自己里了。**
`The_registry_is_asked_for_before_the_listeners_are_registered` 的 `<remarks>` 说
「the application module is applied before the networking one」，但这一点是判据**自己按那个顺序
调两次 `ConfigureServices` 假定的**，不是它证明的；生产顺序只写在 `Program.cs:31-32`，全仓没有
任何判据钉住它（`grep AddServerModules Server/tests/` 无命中）。真要钉，得有一条判据断言组合根的
模块顺序。不阻断的原因是：上面已验明真实保证（`IEnumerable<IHostedService>` 先整体解析）与顺序
无关，所以模块顺序被改动时是这条判据误报变红，而不是保护落空。

### 需要的补救（对应 F1 / F2 / F3）

1. 把 CK-006 写进 TASK-03 的 Required checks；把 CK-008 的机检
   （`NotificationHandlerArchitectureTests`）与「Protocol 程序集已被 `AddNotificationCatalog` 登记」
   一并写进 TASK-05 的 Required checks，各升一次 contract revision。
2. 在本轮交付里补上受理条件一要求的编译器拒绝记录（F2 里的 CS0311 原始输出可直接采用），
   或修订该受理条件并写明为何本轮不需要它——两样都不做不行。
3. 改掉 `INotificationHandling.cs:33` 那句现在时的判据引用，或本轮就把该判据补上。

以上三条都是本地可改的。实现代码本身、判据计数、以及自指泛型与启动校验这两个核心设计，复核后
未发现缺陷。

## Controller Disposition — after Quick Review Round 1

`task-rework`。三条阻断都成立，且都不需要改批准的边界或中心做法。

F1 是这一轮最该记的一条：**我在修正案里写了「这一条要写进 TASK-05 的契约，不得遗漏」，然后没写。**
检查因此是被丢掉了而不是被移走了。已按快审的措辞补记为 TASK-03 修订 2 与 TASK-05 修订 2（见那两个文件）。

F4/F5/F6 三条非阻断项本轮一并处理：F4 与 F5 改了（理由是它们与 F3 同类——一句比代码强的话，
本成果已经因为这类问题返工两次）；F6 被 F6 自己给出的更强事实取代（见下）。

## Task Unit Contract Amendment — Revision 3

- Contract revision: 3
- 撤回修订 2 中「CK-006 移到 TASK-03」的**理由**，保留其**结论**，并收紧受理条件的证据要求。

修订 2 写的是「在 TASK-02 里没有调用点可探」。**这句是错的**，快审 F2 证明了：
`AddNotificationHandler<TNotification, THandler>` 就是本轮交付的生产调用点，而且它的 XML 文档
自己就断言了一条编译期保证。所以受理条件第 1 条（具体的编译器拒绝记录）本轮**能做也必须做**，
不得挂空。已做，见下方 Round 2。

CK-006 仍留在 TASK-03，但理由改为：它检的是**发布**调用点，那个方法要到 TASK-03 才存在。
两处调用点各要各的记录，不是一处代另一处。

## Execution Result — Round 2

### F2：注册调用点的编译探针（本轮补做，原始输出）

两个探针，一次构建：

```
探针 A  services.AddNotificationHandler<ProbeThing, ProbeOtherHandler>(retryLimit: 0);
        （处理器处理的是 ProbeOther，不是 ProbeThing）
探针 B  services.AddNotificationHandler<INotification<ProbeOther>, ProbeOtherHandler>(retryLimit: 0);
        （通知类型实参写成契约接口本身）
```

编译器原文（已去掉项目路径后缀）：

```
_ProbeMismatch.cs(22,18): error CS0311: 类型"ProbeOtherHandler"不能用作泛型类型或方法
"NotificationDispatchingExtensions.AddNotificationHandler<TNotification, THandler>(IServiceCollection, int)"
中的类型参数"THandler"。没有从"ProbeOtherHandler"到
"Dfo.Server.Abstractions.Notifications.INotificationHandler<ProbeThing>"的隐式引用转换。

_ProbeMismatch.cs(26,18): error CS0311: 类型"INotification<ProbeOther>"不能用作泛型类型或方法
"...AddNotificationHandler<TNotification, THandler>..."中的类型参数"TNotification"。
没有从"INotification<ProbeOther>"到"INotification<INotification<ProbeOther>>"的隐式引用转换。

_ProbeMismatch.cs(26,18): error CS0311: ...THandler... 没有从"ProbeOtherHandler"到
"INotificationHandler<INotification<ProbeOther>>"的隐式引用转换。
```

探针文件建后即删，工作区无残留。**探针 B 正是自指约束的收益**：光秃秃的标记接口下这一行会编译通过。

### F3：注释里那句「`NotificationHandlerArchitectureTests` 守着」

该类当时**不存在**（全仓 grep 只命中这句注释本身）。这正是 AGENTS.md 记过的那个反面教训，而且同成果的
`INotificationServices.cs` 做的恰好相反（明说保证到哪里为止）。已删掉这句对不存在之物的引用，改成直接
说规则本身：处理器拿到的是领域服务，拿不到任何结算写入的东西。机检由 TASK-05 修订 2 承接。

### F4：`INotification<TSelf>` 的 `<remarks>` 过强

原文说自指约束「makes the type at a publish call the concrete one and nothing else」。快审探针证明：
建在**非密封基类**上的通知仍可被加宽到那个基类。已改写为如实的两段：自指约束关掉的是「加宽到契约接口
本身」这一条（那条**与通知怎么声明无关，永远开着**）；真正让「没有基类可加宽」成立的是每个通知都是
sealed record。

### F5：那条正例判据的注释描述了一个并不存在的保护

原注释说「若扫描把接口本身也算进去，加目录那天就会失败」。实际上 `INotification<TSelf>` 不在它自己的
`GetInterfaces()` 里，所以那个变异根本不会让它变红——`Dfo.Server.Abstractions` 的 505 个类型里命中集恒为空。

处置：注释改成如实的「这条只说明『没有通知的程序集不是缺口』，它不是关于过滤器的证据」；
并**另加**一个真能证伪过滤器的目标——`private abstract record ShapeNoOneCanSend : INotification<ShapeNoOneCanSend>`，
在缺口判据里断言消息含 `UnhandledProbe` 且**不含** `ShapeNoOneCanSend`。去掉 `IsAbstract` 过滤即变红。

### F6：改用它自己给出的那个更强事实

F6 说顺序判据把它要证明的模块顺序硬编码进了自己，而生产顺序（`Program.cs:31-32`）没有判据钉着。
快审同时给出了更强的事实：宿主是**先把 hosted service 都实例化出来、再逐个启动**，所以构造函数抛出的时机
在任何一个 `StartAsync` 之前，与注册顺序无关。

据此新增 `The_registry_is_asked_for_before_the_first_hosted_service_starts`：用真实宿主，把探针 listener
注册在**前面**、通知校验注册在后面，`host.StartAsync()` 抛出，并断言 `probe.Started == false`。
这条判据不依赖模块顺序，所以 F6 指出的缺口不再是缺口。原来那条顺序判据保留——它记的是模块接线这件事本身。

为此给 `Dfo.Server.Application.Tests` 加了 `Microsoft.Extensions.Hosting` 包引用（三个兄弟测试项目已有此依赖）。

### Executor Self-check — Round 2

- 改动：`INotificationHandling.cs` 两处 `<remarks>`（F3/F4）、`NotificationRegistrationTests.cs`
  （F5 的探针类型与断言、F6 的新判据、两处注释）、测试项目 csproj 加一个包引用、
  TASK-03 与 TASK-05 两个契约的修订 2、本文件。
- **生产逻辑零改动**：`NotificationHandlerRegistry.cs`、`NotificationComposer.cs`、
  `NotificationDispatchingExtensions.cs`、`NotificationAudienceResolver.cs`、`ApplicationModule.cs` 本轮均未改。
  F3/F4 改的是 `INotificationHandling.cs` 的注释，接口签名一字未动。
- `Notifications` 命名空间下判据 13 条（TASK-01 的 6 + 本轮的 7），逐条列名核对过，全绿。

### Round 2 全量判据

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

52 + 510 + 60 + 256 + 6 + 223（跳过 2）+ 975 = **2082 通过 / 2 跳过 / 0 失败**。
Round 1 为 2081，净 +1，来自 F6 新增的那条宿主启动判据。构建 0 警告 0 错误。未遇既知偶发失败。

## Independent Quick Review — Round 2

- Verdict: **passed**
- 审的契约：revision 1 + Amendment Revision 2 + Amendment Revision 3（本轮只审 Round 2 的整改，Round 1
  已复核通过的部分不重开）
- Owned diff：基线 `b05d47e4`，未提交。`git status --untracked-files=all` 逐个文件核对，与 Round 1
  记录的集合一致，无新增越界文件：已跟踪改动两个——`ApplicationModule.cs`（+2，TASK-01 的接线）与
  `Dfo.Server.Application.Tests.csproj`（+3，一个包引用加两行注释）；未跟踪为 Notifications 三个目录下的
  六个源文件（其中两个属 TASK-01）与 `.rks-agent/tasks/…`。
- 检视的命令与证据：全量判据一次独立复跑；scratchpad 编译探针工程一套（`AddNotificationHandler` 的
  正/负对照 + 加宽到非密封基类）；工作区内两次变异复跑（F5 的过滤器、F6 的 `probe.Started`），
  变异前备份、变异后按 sha256 逐字还原。审后 `git status` 与审前逐字一致，工作区无残留。

### F1 — 已解决

`tasks/TASK-03-one-publish-delivers-one-by-one.md` 现为 `Contract revision: 2`，新增受理条件一条
（发布调用点的编译器拒绝记录，要错误码与原文）与检查表一行 CK-006，落点是 `PublishAsync`——与修订 3
改后的理由一致，不是照抄修订 2 那句已被证伪的话。

`tasks/TASK-05-pvp-publication-goes-through-notifications.md` 现为 `Contract revision: 2`，两条前向义务
**都**落地了：`NotificationHandlerArchitectureTests`（CK-021a，写明按 `CommandHandlerArchitectureTests`
的形状、含三条自检）、`ProtocolModule` 登记通知目录（CK-021c）。另外多带一条 CK-021b（
`CommandHandlerArchitectureTests` 的 diff 必须为空），比快审要求的更紧，方向正确。

(c) 第三条前向义务的核查：把 TASK-02 全文按 `TASK-0[0-9]` 逐处扫过，指向下游的义务只有三条——
CK-006、CK-008 的机检、Protocol 程序集登记——均已落到上面两个文件。**没有第四条被漏在 TASK-02 里。**

### F2 — 已解决，探针独立重做，与执行方所录一致

我在 scratchpad 另建工程，引用工作区已编译的 `Dfo.Server.Application.dll` / `Dfo.Server.Abstractions.dll`
（真实产出，不是誊抄接口），照执行方的两个探针原样写：

```
Mismatch.cs(11,18): error CS0311: 类型"ProbeReg.ProbeOtherHandler"不能用作泛型类型或方法
"NotificationDispatchingExtensions.AddNotificationHandler<TNotification, THandler>(IServiceCollection, int)"
中的类型参数"THandler"。没有从"ProbeReg.ProbeOtherHandler"到
"Dfo.Server.Abstractions.Notifications.INotificationHandler<ProbeReg.ProbeThing>"的隐式引用转换。

Mismatch.cs(15,18): error CS0311: …"INotification<ProbeReg.ProbeOther>"…类型参数"TNotification"。
没有从"…INotification<ProbeReg.ProbeOther>"到"…INotification<…INotification<ProbeReg.ProbeOther>>"的隐式引用转换。

Mismatch.cs(15,18): error CS0311: …"ProbeReg.ProbeOtherHandler"…类型参数"THandler"。没有从
"ProbeReg.ProbeOtherHandler"到"…INotificationHandler<…INotification<ProbeReg.ProbeOther>>"的隐式引用转换。
```

错误码、拒绝理由、三条的分布（探针 A 一条、探针 B 两条）与文件里录的逐字相符，仅行号与项目路径不同。
正对照（`AddNotificationHandler<ProbeOther, ProbeOtherHandler>`）**已成功生成，0 个警告，0 个错误**。
受理条件第 1 条现在有证据了。探针工程验完即删；工作区里 `find -iname "*probe*"` 只命中 `ClientHack/`、
`Tool/`、`protocol/` 这些与本成果无关的既有路径，**没有残留探针文件**。

### F3 — 已解决

`grep -rn NotificationHandlerArchitectureTests Server/src Server/tests` 无命中（全仓除 `.rks-agent`
外也无命中）。改后那句是
「Where it shows is the injection surface: a handler is given domain services and nothing that settles
a write -- no store, no table, no command bus.」——直说规则本身，**没有再指认一个不存在的守卫**，
也没有换个名字重造空头承诺。`NotificationDispatchingExtensions.cs` 里那句
「see NotificationRegistrationTests for the check」指向的类确实存在且确实有那条判据。

### F5 — 已解决，正例判据现在真能证伪过滤器（我自己变异过）

变异一：把 `IsNotification` 的 `type is { IsAbstract: false, IsInterface: false }` 改成
`type is { IsInterface: false }`，`A_catalogued_notification_with_no_handler_is_refused` **变红**：

```
Assert.DoesNotContain() Failure: Sub-string found
String: ···"egistered handler: 'ShapeNoOneCanSend' (f"···
NotificationRegistrationTests.cs:line 42
```

`ShapeNoOneCanSend` 是真正的证伪目标，不是摆设。改后那条弱正例的注释（「它不是关于过滤器的证据」）
也与事实相符：变异二（只去掉 `IsInterface: false`）时七条全绿，说明 `Dfo.Server.Abstractions` 里确实
一个候选类型都没有，「nothing is filtered」这句是对的。两次变异后均按 sha256 还原
（`0c0b2a12…`），还原后 `Dfo.Server.Application.Tests` 975 条全绿。

### F6 — 已解决，`probe.Started == false` 不是平凡为真

按要求做了区分力变异：把
`The_registry_is_asked_for_before_the_first_hosted_service_starts` 里的
`AddNotificationCatalog(...)` 去掉（校验因此不抛），并把 `ThrowsAnyAsync` 换成直接 `await host.StartAsync()`，
其余不动。结果**该断言变红**：

```
错误消息: A hosted service started before the notification check ran.
NotificationRegistrationTests.cs:line 77
```

即：探针 listener 注册在前，正常启动时它**会**被启动；原判据里它没被启动，只因为构造期抛出发生在
任何 `StartAsync` 之前。这条判据证明了它自称的事，且不依赖模块顺序——F6 指出的缺口确实被堵上了。
还原后 sha256 与备份一致（`ca324b6d…`）。

包引用的影响：`Microsoft.Extensions.Hosting` 的版本早已由 `Server/Directory.Packages.props:11` 集中钉住
（10.0.10），本轮**没有新增 PackageVersion**，只是在测试项目里引一个已钉版本的包；
`Dfo.Server.Infrastructure.Tests` / `Networking.Tests` / `Protocol.Tests` 三个兄弟项目确实早有同一条引用。
它只进测试项目，生产项目 csproj 一个都没动（`git status` 里不存在）。旁证：其余六个测试程序集的计数
一条不差，只有 Application 从 974 变 975。

### 计数 — 独立复跑，与执行方所报一致

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ exit 0：

| 程序集 | 通过 | 跳过 |
|---|---|---|
| Protocol.Tests | 510 | 0 |
| Networking.Tests | 52 | 0 |
| Dfo.Pvf.Tests | 60 | 0 |
| Host.Tests | 6 | 0 |
| Persistence.Tests | 256 | 0 |
| Infrastructure.Tests | 223 | 2 |
| Application.Tests | 975 | 0 |
| **合计** | **2082** | **2** |

失败 0。对照 Round 1 的 2081：净 +1，全部落在 Application，其余六项一字未动，与「本轮只多一条宿主启动
判据」相符。未遇既知偶发失败（无 `ObjectDisposedException: 'SQLitePCL.sqlite3'`）。
`NotificationRegistrationTests` 单类 7 条（变异时按类过滤跑出的总数即 7），与自检「本轮 7 条」一致。

### 「生产逻辑零改动」— 属实

- `NotificationHandlerRegistry.cs`、`NotificationComposer.cs`、`NotificationDispatchingExtensions.cs`
  的 mtime 分别是 12:32:33 / 12:32:41 / 12:32:55，`NotificationAudienceResolver.cs` 是 12:03:52，
  `ApplicationModule.cs` 是 12:04:16——**全部早于 Round 2 的改动窗口**（`INotificationHandling.cs` 12:49:35、
  `NotificationRegistrationTests.cs` 12:50:35、csproj 12:50:08、TASK-03/05 12:51:22、本文件 12:52:25）。
- `ApplicationModule.cs` 的 `git diff` 就是 +2 行（`using` 与 `AddNotificationDispatching()`），
  与 Round 1 记录逐字相同。
- `INotificationHandling.cs` 本轮确实被改过，但接口签名与 Round 1 交付记录一致：
  `INotification<TSelf> where TSelf : INotification<TSelf>`、
  `INotificationHandler<TNotification>.ComposeAsync(NotificationRecipient, TNotification, CancellationToken)
  → ValueTask<IReadOnlyList<IServerNetworkMessage>>`。改的是两处 `<remarks>`。

### 记录但不阻断（不要求本轮再动）

**N1 F4 改后仍留了一句无依据的现在时断言。** 新 `<remarks>` 的核心更正是对的，而且我验过两半：
加宽到契约接口恒被挡（上文 CS0311）、加宽到非密封基类**能编译**（scratch：
`record OpenBase(int) : INotification<OpenBase>` / `sealed record Narrower : OpenBase`，
`PublishAsync<OpenBase>(narrower)` 已成功生成，0 警告）。剩下的是那句
「Every notification here is a sealed record, which is what actually leaves no base to widen to」：
本轮仓库里**零个生产通知类型**，而唯一的反例恰恰是本轮新加的 `ShapeNoOneCanSend`（abstract record，
实现 `INotification<ShapeNoOneCanSend>`）——按字面读，这句现在既无正例也有反例，是一句约定而不是事实，
且没有任何判据守着「通知必须 sealed」。之所以不阻断：这正是 Round 1 F4 亲口开的方子
（「基类那条靠通知都是 sealed」），执行方照办了，本轮再翻是重开已裁的问题。建议在 TASK-07（约定）
或 TASK-05 落一条判据/约定条目时顺手把它从「是」改成「本项目要求」，或给它一个守卫。

**N2 `IsInterface: false` 这半个过滤器仍无判据可证伪。** 变异二（只去掉它）七条全绿——目前被登记的两个
程序集里没有任何接口实现 `INotification<>`。`ShapeNoOneCanSend` 只钉住了 `IsAbstract` 那半。
判据注释写的是 "dropping the filter"（单数、笼统），与它实际钉住的范围略宽于事实。等 TASK-05 有生产
通知时再补一个接口形状的目标即可，本轮不值得为此返工。

**N3 旧的顺序判据 `The_registry_is_asked_for_before_the_listeners_are_registered` 的 `<remarks>` 未随之更新。**
它仍写「the application module is applied before the networking one … and this walks the collection those
two produce to say so」，而这一点依旧是它自己按那个顺序调两次 `ConfigureServices` 假定的，不是它证明的
（F6 的原始意见）。新判据已经把真正的保证钉死，所以这不再是保护缺口，只是这句话比它做的事说得满一点。
留着这条判据本身是对的——它记的是模块接线。

### 结论

三条阻断（F1/F2/F3）逐条独立复核，均已真正解决，且整改本身未引入新问题：生产逻辑零改动属实，
新增的两个判据目标（`ShapeNoOneCanSend`、`ProbeListener`）我都做过变异，各自都能红，不是装饰；
计数 2082/2/0 与所报一致；工作区无探针残留。verdict **passed**——按快审的定义，这只表示本 Task Unit
可以走执行方的受理逻辑，不是最终质量批准。

## Controller Disposition — after Quick Review Round 2

`passed`。最新的执行结果、自检、快审裁定与 owned diff 指向同一轮。

快审记的三条非阻断项 N1/N2/N3 **本轮就地处理，不带进下一个任务**——理由是它们与已经让本成果返工两次的
是同一类问题（一句比代码强的话），而且改动都很小。

### N1：「Every notification here is a sealed record」

本轮仓库里零个生产通知类型，而且本轮新加的 `ShapeNoOneCanSend` 就是个 abstract record 通知，
这句话被自己的判据反证。改为不作全称断言：「sealing the notification is what leaves no base to widen to,
and that is a choice each one makes for itself」。

### N2：`IsInterface: false` 这半个过滤器没有证伪目标

先验证前提再动手：临时判据断言 `typeof(IProbeShape).IsAbstract`、`typeof(INotification<>).IsAbstract`、
`typeof(IEnumerable).IsAbstract` 三条全真（通过，探针即删）。**接口恒为 abstract，所以这一半是死代码。**
删掉它，过滤器只剩 `!type.IsAbstract`，并加注释写明它同时覆盖接口。没有留下无法证伪的分支。

### N3：旧顺序判据的 `<remarks>` 未随 F6 更新

它仍在用「hosted services start in registration order」当作保证来源，而 F6 之后真正的保证是
「宿主先实例化再启动」那条。已改：明说这条判据记的是**接线**而不是保证，保证在上一条判据里、
且与顺序无关。

### 检查（N-fix 之后）

- `dotnet build Server/86JP.Server.slnx --nologo` → 0 警告 0 错误
- `Notifications` 命名空间 13 条判据全绿（条数未变）
- 全量判据见下

登记表置 TASK-02 为 `done`，`tasks.active` 前移至 TASK-03。
**这三处改动发生在快审 passed 之后，须在最终完整变更评审里一并复核。**

**N-fix 之后的全量判据**：510 + 60 + 52 + 256 + 6 + 223（跳过 2）+ 975 = **2082 通过 / 2 跳过 / 0 失败**，
与 Round 2 同值（未增删判据条数）。未遇既知偶发失败。
