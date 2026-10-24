# 架构哲学：一个请求怎么走、一个领域怎么切

> 2026-10-20，基准提交 `d29033a0`。来源：用户口述的总架构，加对代码的核实。
> 本文讲**设计意图**和它在代码里的样子；具体约束（命令执行方式、handler 四类操作、事件三入口的使用规则）
> 以 [AGENTS.md「架构设计」](../../AGENTS.md#架构设计) 为准，这里只引不抄。
> 标注：**已核实**＝读过代码；**推断**＝归纳，未经用户确认。路径相对 `Server/src/`。
> §1、§5 中执行与锁的部分于 2026-10-24 按基准 `2bdb1afd` 改写；其余各节仍是 `d29033a0` 时的记录。

## 1. 总架构

用户口述：`PacketHandler → CMD → 领域服务 → NotiBus（或 EventBus → NotiBus）`。
代码里的对应（已核实）：

```
GameplayExecutor 上的作业（inbound:<ClientMessageType>）   唯一的玩法执行器，一次一个作业
Protocol/Game/<协议>/ 切片
  PacketHandler            只做身份解析、发命令、映射错误码、登记回包
    └─ commands.SendAsync(XxxCommand)           CMD：sealed record : ICommand<TResult>；在作业里内联执行（过渡形态）
         ICommandHandler<TCmd,TResult>          只能注入 *DomainService / *DomainStore
           └─ I*DomainService                   Application/<领域>/
                ├─ I*RuleService / static *Rules   不变规则
                ├─ I*Repository                    Persistence/<领域>/
                └─ IEventService                   口述的 "EventBus"
                     EnqueueAsync / DirectAsync / ProjectAsync
                       └─ NotificationProjectionSubscriber<T>   唯一的桥
                            └─ INotificationBus.PublishAsync    口述的 "NotiBus"
                                 受众(NotificationAudience) → INotificationHandler 组包 → 登记进作业的发件箱
  作业结束 → 发件箱按登记顺序交给 OutboundSender（Networking/Outbound）→ 每连接发送队列 → 出站
```

- 命令契约 `Dfo.Server.Application/Commands/CommandContracts.cs`；总线为自研 `CommandBus`，不是 MediatR。
- 事件服务接口 `Dfo.Server.Abstractions/DomainEvents/DomainEvents.cs`，实现 `Application/DomainEvents/EventService.cs`；
  其中 `NotificationProjectionSubscriber<T>` 是全仓唯一调用 `INotificationBus.PublishAsync` 的地方。
- 典型链：`Protocol/Game/BuyItem/BuyItemHandler.cs` → `GameBuyItemCommandHandler` → `INpcShopDomainService.PurchaseAsync`
  → `ICommandProjections.ProjectAsync(NpcShopPurchaseCompleted)`；普通购买用显式 ordinary Write，Housing 用 Main，薄 handler 不再承载 NPC 分支。邮件 `MailboxDomainService` `EnqueueAsync(MailCreated)`
  → `MailCreatedNotificationSubscriber` → `ProjectAsync`。

**口述没覆盖的第二条出站路（已核实）**：CMD 回包和一部分 NOTI 由 handler 直接经 `IGameMessageSender` 登记进发件箱，
不经事件服务。领域结果的扇出才走 `ProjectAsync → NotificationBus`。两条路最后都在作业结束后由发送端交付。

## 2. 领域模块按功能内聚

用户口述："以功能内聚的部分组成一个相对独立的领域模块。"

- `Abstractions/<领域>/` 放 public 契约（`I*DomainService`、`I*RuleService`、`I*Repository`、事件与通知 record），
  `Application/<领域>/` 放 internal 实现、订阅者、投影，`Persistence/<领域>/` 放仓储，PVF 内容源在 `Infrastructure/Content`。
  两侧目录名对称。
- 类别由后缀表达，见 AGENTS.md「接口后缀标明类别」。
- 领域不认识连接、Session 和协议字段：人群用角色表达（`NotificationAudience`），协议转换放在 Protocol 边缘，
  快照和记录用领域形状命名，不用协议段命名。
- 内聚判据（推断，取自 `31e7a91a` 等）：一个概念的状态只有一个实体持有；几个服务互相读写同一份存储、
  或同一个问题有多种答法，就说明边界切错了，应合成一个模块。

## 3. 不变规则做成内置规则层

用户口述："如果是不变的规则，则内置规则功能模块。"代码里是两层（已核实）：

| 层 | 形态 | 例子 |
|---|---|---|
| 规则服务 `*RuleService` | 读内容源，接收调用方给的 facts，返回不可变规则对象；不自己查世界状态 | `Application/Inventory/EquipmentRepairRuleService.cs` |
| 纯规则 `*Rules` | `internal static` 纯函数 | `Application/Inventory/EquipmentReinforcementRules.cs` |

- 内容（PVF 等）只经规则服务进入服务端（`8abf77eb`）。
- 只有领域服务调用规则。命令 handler 构造参数由
  `tests/Dfo.Server.Application.Tests/Commands/CommandHandlerArchitectureTests.cs` 按后缀判定，规则服务进不来。
- 判断一段逻辑该放哪（推断）：只依赖内容和传入事实、不改状态 → 规则；要读写世界状态或编排多步 → 领域服务；
  只和字节布局有关 → Protocol 切片。

## 4. 分层是项目引用图

- Application 只引用 Abstractions、Data，不得引用 Infrastructure / Protocol；Protocol 引用 Application 和 Networking；
  Host 是组合根，单进程装各组件（`038dabb0`："不拆进程，组件化"）。项目内类型默认 internal。
- `ProtocolModule` 是协议唯一接线点；解码出的消息没有 handler 就启动失败。
- 架构约束优先做成**机器判据**（项目引用、类型绑定、启动闭包检查、反射测试），不靠名单和审查。

## 5. 现存偏离（基准提交时）

改到这些地方时按上面的意图收口；它们是否为有意例外尚未经用户确认。

- handler 直接改领域状态、不经 CMD：`Protocol/Game/TownProjection/TownProjectionSlice.cs`（SetUserArea/Position）、
  `MarriageProposal/MarriageChurchSlice.cs`、`MarriageProposal/MarriageWeddingSlice.cs`。
- handler 里有业务判断：`CharacterDeath/CharacterDeathSlice.cs` 自己算多人扇出，没用 `NotificationAudience`。
  BuyItem 的 `NpcId == 20011` 分支现已移入唯一 whole CMD，普通／Housing 精确消息族共用薄 handler。
- 通知 handler 直接 `new InventorySlotMappingRuleService()`（`BuyItem/NpcShopPurchaseCompleted.cs`）。
- handler 在入站作业内编排多条细粒度命令，每条命令一个事务（过渡形态，见 AGENTS.md「命令执行总线」）。
- 命令总线的执行方式（含 `ReadOnly`）与管线的五层以 AGENTS.md「命令执行总线」为准。
