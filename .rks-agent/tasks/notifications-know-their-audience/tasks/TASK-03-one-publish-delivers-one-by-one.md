# TASK-03 — 一次发布逐个投递，一个失败不影响其余

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 总线的发布方法、投递实现（逐个收件人、全局一致的超时、失败隔离、两类失败分流）、以及与投递同时产出的摘要。
- Referenced spec requirements: RQ-008, RQ-009, RQ-012
- Referenced plan sections: T-003, BD-005, BD-006, BD-010
- Owned boundary: `Server/src/Dfo.Server.Abstractions/Notifications/`（总线接口与摘要类型）、`Server/src/Dfo.Server.Application/Notifications/`（总线实现）、`Server/tests/`
- Permitted files or interfaces: 上述路径；ApplicationModule 接线；既有的 `ExplicitMessageDelivery` 只读使用，不改
- Dependencies: TASK-02
- Required dependency outputs: 通知标记、处理器接口、注册表
- Acceptance conditions:
  - 摘要的归类恰好四种：成功、弃权、人不在了、失败
  - 「解析之后投递之前离线」归「人不在了」；投递失败、超时、处理器抛异常归「失败」
  - 一个收件人失败，其余各自仍收到自己的消息
  - 不做并发，不做节流，超时值全局一致且不可按通知覆盖
  - 全量判据通过
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-010, CK-011, CK-012, CK-013, CK-014
- Required evidence: 摘要的形状；失败隔离、两类失败分流、弃权归类三条判据；全量判据计数

## Task Unit Contract Amendment — Revision 2

- Contract revision: 2
- 来源：TASK-02 的修正案把 CK-006 移到本任务，但当时**只在 TASK-02 里写了一句「移到 TASK-03」，没有落到本
  文件**——TASK-02 的独立快审把这条记为阻断发现 F1（「检查被丢掉了，不是被移走了」）。这份修正案是补记。

新增受理条件：

> - 发布调用点的类型安全有具体的编译器拒绝记录：一次故意让通知类型实参与实参不符的 `PublishAsync`
>   调用，必须编译失败，且要录下**错误码与原文**，不是「看起来不行」。

新增检查：

| ID | Check | Expected result |
|---|---|---|
| CK-006 | Non-command：对 `PublishAsync` 的一次故意不匹配的调用尝试 | 不能通过编译；记录编译器拒绝的错误码与原文 |

背景（TASK-02 已验，可直接引用而不必重做）：注册调用点 `AddNotificationHandler<TNotification, THandler>`
的同类探针已在 TASK-02 做过，两种写法都得 `CS0311`。本条要的是**发布**调用点的那一份。

## Execution Result — Round 1

### 交付

- 新增 `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs`：
  `NotificationOutcome`（Delivered / Declined / Gone / Failed）、
  `NotificationAttempt(CharacterId, NotificationOutcome)`、
  `NotificationDelivery(IReadOnlyList<NotificationAttempt>)` 带 `With(outcome)`、
  `INotificationBus.PublishAsync<TNotification>(audience, notification, ct)`
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`：
  `NotificationDeliveryOptions(TimeSpan PerRecipientTimeout)`（默认 2 秒）与 `NotificationBus`
- `NotificationDispatchingExtensions` 加两行接线（options 与 bus）
- 新增 `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs`（8 条）

### 两类失败怎么分流——这是本任务唯一有真实选择的地方

「人不在了」与「投递失败」必须分开，因为前者不重试、后者要重试。有两种判法：

1. 按异常判：`TouchSocketConnectionRegistry.Get` 在连接没了时抛
   `InvalidOperationException("Connection '…' is not active.")`。**否决**——靠异常消息文本分类，
   改一个字就静默失效。
2. **投递前复问一次在场**：`sessions.FindByCharacter(角色)` 的连接是否仍是解析时那个。不是 → `Gone`。

选 2。它还有个副作用是对的：已经走了的人不用再麻烦处理器。

复问之后仍是竞态（下一瞬间就可能走），所以**发送抛出仍然是 `Failed`**，会进重试；重试时复问会答
`Gone` 而被丢弃。两条路合起来是收敛的。

### 摘要覆盖谁——一处刻意的取舍

摘要里的 attempt 是**人群解析出来的收件人**，不含「人群本想告诉、但当时不在线」的人。
原因不是省事：人群不总是一份名单（将来的 `InArea` 形态没有调用方可见的名字列表），所以
「你本想告诉谁而没告诉到」没有通用答案。这一条写进了 `NotificationDelivery` 的 `<remarks>`，
RQ-012 的字面（「覆盖了哪些收件人」）也正是这个口径。

### 超时

`NotificationDeliveryOptions` 单例，默认每收件人 2 秒，**不提供按通知覆盖**。
投递是顺序的，所以这个数就是「一个不读的 socket 能拖住队伍多久」的上界；按通知可覆盖会让最坏情况
取决于当时恰好在发哪个通知。用链接 CTS 实现，外层 ct 被取消时**重抛**而不是记成该收件人失败。

### 检查

| ID | 命令 / 方式 | 结果 |
|---|---|---|
| CK-010 | `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，0 个警告，0 个错误 |
| CK-006 | 发布调用点的编译探针，见下 | 两个探针都被拒；正对照编译通过 |
| CK-011 | `A_recipient_whose_delivery_fails_does_not_stop_the_others` | 通过。三人中间一人失败，另两人各自收到自己的帧，且 Sent 里只有那两个连接 |
| CK-012 | `Someone_who_left_is_told_apart_from_someone_whose_delivery_failed` | 通过。同一次发布里 First=Delivered / Second=Gone / Third=Failed |
| CK-013 | `A_recipient_the_handler_declines_hears_nothing_and_is_not_a_failure` | 通过。该收件人无消息、归 Declined、Failed 桶为空 |
| CK-014 | 全量判据 | 见下 |

### CK-006 的原始输出

```
探针 A  bus.PublishAsync<GateClosed>(who, new BellRang("closing time"), CancellationToken.None);
_ProbePublish.cs(17,43): error CS1503: 参数 2: 无法从"BellRang"转换为"GateClosed"

探针 B  bus.PublishAsync<INotification<BellRang>>(who, new BellRang("x"), CancellationToken.None);
_ProbePublish.cs(22,13): error CS0311: 类型"INotification<BellRang>"不能用作泛型类型或方法
"INotificationBus.PublishAsync<TNotification>(NotificationAudience, TNotification, CancellationToken)"
中的类型参数"TNotification"。没有从"INotification<BellRang>"到
"INotification<INotification<BellRang>>"的隐式引用转换。

正对照  bus.PublishAsync(who, new BellRang("closing time"), CancellationToken.None);  → 无错误
```

探针文件建后即删，目录清点确认无残留（只剩三个正式判据文件）。
**两个探针的拒绝理由不同**，值得记：A 是实参转换失败（显式指定别的通知类型），
B 是自指约束不满足（加宽到契约接口）。只有 B 是自指泛型带来的；A 靠的是 `TNotification` 同时出现在
类型实参与形参位置。

### Executor Self-check — Round 1

- 边界内：Abstractions 一个新文件、Application 一个新文件、扩展方法两行、一个新判据文件。
  未改任何既有断言。
- **重试尚未接线**：`Failed` 目前只是被归类，没有进队列——那是 TASK-04（BD-007）。
  也就是说本轮 `NotificationDeliveryOptions` 之外没有任何「第二次机会」，这是计划里的顺序，不是遗漏。
- **一处我没验的**：`RecordingSender.StallFor` 用 `Task.Delay(Timeout.Infinite, ct)` 模拟不读的 socket，
  这检的是「链接 CTS 到期会让那次发送抛 OperationCanceledException 并被归为 Failed」。它**不**证明
  真实 TouchSocket 发送会响应取消令牌。真实行为未验。
- `sessions` 注入进总线是有意的：总线是被允许知道连接存在的那个地方。

### CK-014 全量判据

`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

510 + 60 + 52 + 256 + 6 + 223（跳过 2）+ 983 = **2090 通过 / 2 跳过 / 0 失败**。
TASK-02 收尾为 2082，净 +8，与本轮新增的 8 条判据数相符。未遇既知偶发失败。

## Independent Quick Review — Round 1

- Verdict: **changes_required**
- 评审的契约：Task Unit Contract revision 1 + Contract Amendment Revision 2（两份合起来读）
- Owned diff identity（基线 `b05d47e4`，未提交）：新增 `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs`、
  新增 `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`、
  改动 `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs`（第 16–17 行两行接线）、
  新增 `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs`（8 条）。
  `git status` 另有 `ApplicationModule.cs` 与 `Dfo.Server.Application.Tests.csproj` 两处改动，逐行看过，
  分别是 TASK-01/02 的 `AddNotificationDispatching()` 接线与 `Microsoft.Extensions.Hosting` 包引用，**不属本轮**。
  边界之外无改动，无无关变更。
- 评审后工作区已复原：`NotificationBus.cs` 的 md5 与变异前一致（`5c40f06dc049cc0192c690903d8bc88b`），
  临时判据文件已删，`git status` 与评审开始时逐行相同。编译探针建在 scratchpad 的独立工程里，已删。

### 复核过的命令与证据

| # | 做法 | 结果 |
|---|---|---|
| R-1 | CK-006 两个探针独立重做（scratchpad 工程，只引用已构建的 `Dfo.Server.Abstractions.dll` / `Dfo.Server.Data.dll`） | 与文件所录**逐字相符**：A `CS1503 参数 2: 无法从"BellRang"转换为"GateClosed"`；B `CS0311 …没有从"INotification<BellRang>"到"INotification<INotification<BellRang>>"的隐式引用转换`；正对照 0 警告 0 错误 |
| R-2 | 反事实对照：把约束换成**非自指**的普通标记接口，重跑两个探针的等价写法 | A 的等价写法仍 `CS1503`；B 的等价写法**编译通过**。执行方那句「只有 B 是自指泛型带来的；A 靠的是 `TNotification` 同时出现在类型实参与形参位置」**成立** |
| R-3 | 变异 M-A：在通用 `catch` 前插一条 `catch (OperationCanceledException) { return Declined; }` | 只有 `A_recipient_who_runs_out_of_time_is_a_failure` 变红（7 通过 / 1 失败）。超时判据**不是平凡通过**，它钉住的正是「超时→Failed」这条映射 |
| R-4 | 变异 M-B：删掉 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }` | 只有 `The_callers_own_cancellation_is_not_reported_as_a_failed_recipient` 变红，报 `Assert.ThrowsAny() Failure: No exception was thrown`——没有这条 catch，OCE 被吞成 `Failed` 且发布正常返回。该判据的异常**只能**来自那条重抛 |
| R-5 | 变异 M-D：`IsStillHere` 改为恒真 | 只有 `Someone_who_left_is_told_apart_from_someone_whose_delivery_failed` 变红。三种归类是按角色 id 断言的，不是靠排列巧合 |
| R-6 | 探针：解析后、投递前**同一角色换连接**（重登），观察归类 | 输出 `RECONNECTED RECIPIENT OUTCOME = Gone; connections sent to = [Game:A]`——归 `Gone`，新连接一个字节也没收到。见 F-1 |
| R-7 | CK-014 自行全量重跑：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 510 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 983 = **2090 通过 / 2 跳过 / 0 失败**，与执行方所录**完全一致**；新增文件 8 条 `[Fact]`，与净 +8 相符。构建 0 警告 0 错误（`TreatWarningsAsErrors` 下即 CK-010 的再确认），未遇既知偶发失败 |
| R-8 | 超时判据的抖动分析（读代码，非命令） | 每个收件人各自新建链接 CTS 并各自 `CancelAfter`；未 stall 的收件人整条路径同步完成（`ValueTask.FromResult` + 同步假发送），不经线程池跳转，慢机器/并行只会推迟 stall 者的恢复而不会翻转断言；若 deadline 在 `Task.Delay` 之前就到期，得到的仍是 OCE，结论不变。基线过滤跑 8 条 115–126 ms。**未发现偶发失败路径** |
| R-9 | 摘要口径对 RQ-012 的字面核对 | `spec.md` 术语表把「收件人」定义为「人群解析之后得到的一个具体目标」，故 RQ-012 的「覆盖了哪些收件人」字面就是**解析出来的**那些人；`The_summary_accounts_for_every_recipient`（第三人未上线故不在 `Attempts` 里）钉的正是这个口径。**一致** |
| R-10 | `AttemptAsync` 可见性核对 | 全仓 grep：唯一调用点是同文件同类里的 `PublishAsync`，`Server/tests` 无任何引用。见 F-2 |

### 必须修（Required）

**F-1 「解析后重登」这一类既不在契约的枚举里，也不在执行方的收敛论证里，而交付物里的说明对它是假的**

`NotificationBus.IsStillHere` 的判据是「`FindByCharacter(角色)` 的连接是否**仍是解析时那个**」。
同一角色换了连接（重登），`FindByCharacter` 返回的是**新**会话，连接不等 → 归 `Gone`。R-6 实测确认，
且新连接没有收到任何东西。

问题不在于选了哪一边，而在于：

1. 契约受理条件只枚举了两类——「解析之后投递之前**离线**」归 `Gone`，「投递失败/超时/处理器抛异常」归
   `Failed`。重登两类都不是。
2. `INotificationBus.cs` 里 `Gone` 的注释写的是 *"They were gone by the time it was their turn."*
   ——对一个**一直在线、只是换了连接**的人来说，这句话是假的。
3. Execution Result 的收敛论证（「复问之后仍是竞态……发送抛出仍是 `Failed`，重试时复问会答 `Gone` 而被丢弃，
   两条路合起来是收敛的」）覆盖的是「第一轮 Failed → 重试时 Gone」这条路，**没有**覆盖「第一轮复问就答 Gone」。
4. 后果要等 T-004 才显形，但那时已经晚了：按计划 T-004 第 2 步「把角色重新解析成**当时**的连接」，
   若此人归 `Failed`，重试会重新解析到新连接并送达；归 `Gone` 则不进队列，**一个全程在线的玩家被静默丢弃**。

两种归类都讲得通（重登后客户端本就会重新拉状态，`Gone` 有其道理），所以这里要的不是改行为，是**把选择记下来**：
要么改判为 `Failed`（让重试按角色重新解析），要么保留 `Gone` 并且 (a) 改掉 `Gone` 那句已被证伪的注释、
(b) 把这一类补进 Execution Result 的分流小节。**无论选哪边，都补一条把它钉住的判据**——
这一类今天没有任何判据覆盖。

**F-2（原文对照）超时那句「上界」是被自己的 self-check 否掉的**

Execution Result 的「超时」小节把它当事实写：「投递是顺序的，所以这个数就是『一个不读的 socket 能拖住队伍多久』
的上界」。而 Self-check 又写：「它**不**证明真实 TouchSocket 发送会响应取消令牌。真实行为未验」。
这两句直接矛盾——若 `((ISender)connection.Session).SendAsync(…, cancellationToken)` 不理会令牌，
那个「上界」就不成立，而**上界正是这个超时存在的全部理由**。

我核过仓里没有现成证据：`Server/tests/Dfo.Server.Networking.Tests/Integration/OutboundSendFailureTests.cs`
只覆盖端点不匹配与缺编码器两类拒绝，没有任何一条检「被取消的出站发送」。

**本轮不要求补真实 socket 判据**——那需要一个停止读取的真 socket 把内核缓冲填满，而 `AGENTS.md` 已明确
「拉真实进程或真实 socket 的用例在满载全量下会假红」，代价与本 Task Unit 不相称。要求的是**把话说回到已证的范围**：
这个 deadline 界的是**本代码的 await**，是否界得住真实发送取决于发送方是否响应令牌，且该前提**未验**。
Self-check 的自认方向是对的，但它没有回头改掉正文里那句断言，于是文件里同时留着一真一假两句话。

### 建议修（Recommended，不阻断）

- **F-3 `AttemptAsync` 的 `internal` 是无谓放宽。** R-10 确认唯一调用点是同类里的 `PublishAsync`，本轮
  没有任何判据用它。`Dfo.Server.Application.csproj` 对四个测试程序集开了 `InternalsVisibleTo`，
  所以 `internal` 是**跨程序集**的实际放宽，而不只是同类内的写法差别。`private` 足够。
  （同类：`NotificationDeliveryOptions` 是 `public`，而它今天的消费者只有 Application 实现与
  Application.Tests，后者看得见 internal。）
- **F-4 `IsStillHere(recipient)` 调用在 `try` 之外**（`NotificationBus.cs:74`），任何来自会话查找的异常
  会越过 `AttemptAsync` 的隔离、掀掉整次发布——而「一个收件人失败，其余各自仍收到自己的消息」正是契约受理条件。
  `SelectedSessionDomainService.FindByCharacter` 用 `SingleOrDefault`，同一 `CharacterId` 出现两次即抛
  `InvalidOperationException`；`Select` 今天挡住了这种情形，所以是**潜在**而非可达。一行的事：把复问挪进 `try`，
  或就地包一层。
- **F-5 注释漂移一处。** `NotificationAudienceResolver` 的 remarks 仍自称
  *"The one place that answers 'which connection is this character on'"*，而 `IsStillHere` 现在问的正是同一个问题。
- **F-6 两条判据可以更钉一点（可选）。** (a) 超时判据换成任何有限 deadline 都会绿，它没有钉住
  「用的是 `options.PerRecipientTimeout` 这个值」；(b) 取消判据只断言了异常类型，没有断言
  `Sender.Sent` 只有一条——即它自己 remarks 里说的「不会继续走完人群」这半句尚未被断言覆盖。

### 结论

CK-006（含那句关于两个探针拒绝理由不同的论断）、CK-011、CK-012、CK-013、CK-014 与计数全部独立复核通过，
四次变异证明三条关键判据都不是平凡通过，全量 2090/2/0 与所录一致。阻断项只有两条，且都是本仓反复出现的那一类
——**一句没验过的论断**：F-1 是一个既没被枚举、也没被判据覆盖、而注释还把它说反了的归类分支；
F-2 是正文断言与自己 self-check 的直接矛盾。两条都在本 Task Unit 内可就地修完。

## Controller Disposition — after Quick Review Round 1

`task-rework`。两条阻断都成立，且都不改批准的边界。四条建议项一并处理——它们与阻断项是同一类问题
（一句比代码强的话或一条钉不住的判据），本成果已因这类问题返工三次。

## Execution Result — Round 2

### F-1：解析后重登这一类，我漏了

快审实测（`RECONNECTED RECIPIENT OUTCOME = Gone; connections sent to = [Game:A]`）：同一角色换连接会归
`Gone`，新连接一字节没收到。这**不在契约的两类枚举里**，也**不在我的收敛论证里**——那段只覆盖
「第一轮 Failed → 重试时 Gone」。而 `Gone` 原来的注释 "They were gone by the time it was their turn"
对一个全程在线的人是**假的**。

**行为保留，理由补齐，判据钉住。** 保留的理由不是省事：

> 重登后的新会话**自己会跑完整的入场序列**从头建立状态。为「刚刚被替换掉的那个会话」算出来的消息，
> 按构造就是陈的——发过去等于让一个旧答案去和新会话自己的入场序列赛跑。他拿到的应该是入场给他的那份。

另一条读法（归 `Failed`）也讲得通，而且**正因为讲得通才危险**：它会把一个在线玩家放进重试队列，
重试时按角色重新解析会找到新连接，然后把那个旧答案送过去。所以这个选择必须写下来而不是留给后来人猜。

- `NotificationOutcome.Gone` 的注释重写：明写两条到达路径（登出 / 换连接回来），并写明为什么两者同解。
- `IsStillHere` 加 `<summary>`，说明两个半边各自问的是什么。
- 新增判据 `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`：
  断言归 `Gone`、`Failed` 桶为空、且**新连接上没有任何发送**。判据注释里写下了被否掉的那条读法及其后果。

### F-2：超时那句「上界」被我自己的自检否掉了

正文当事实写「这个数就是一个不读的 socket 能拖住队伍多久的上界」，自检又写「不证明真实 TouchSocket
发送会响应取消令牌」。两句不能同时成立。已把正文改回已证范围：

> 它约束的是**发送路径对它的观察**——取消交给传输的令牌。一个卡住的 socket 多快响应那个令牌是传输的事，
> 这里没有测过。读作「这段代码强制的截止时间」，不是「每个收件人墙钟时间的已证上限」。

快审明确**不要求**补真实 socket 判据（AGENTS.md 写明真 socket 用例满载下会假红，代价不相称）。

### 四条建议项

| 项 | 处置 |
|---|---|
| `AttemptAsync` 的 `internal` 是跨程序集的无谓放宽（`Application.csproj` 对四个测试程序集开了 `InternalsVisibleTo`，而没有任何判据引用它） | 改 `private` |
| `IsStillHere` 在 `try` 之外，会话查找若抛会掀掉整次发布、破坏隔离（`FindByCharacter` 用 `SingleOrDefault`，今天被 `Select` 的唯一性检查挡住，属潜在） | 移进 `try`，并在注释里写明为什么在里面 |
| `NotificationAudienceResolver` 的 remarks 仍自称「唯一回答角色在哪个连接的地方」，已漂移（总线现在也问） | 改为如实：这个问题留在通知服务内部——解析器一次，总线在每个收件人轮到时再问一次看答案是否还成立 |
| 超时判据换任何有限 deadline 都会绿；取消判据只断言异常类型 | 各补一条。见下 |

### 两条判据的加强

- 新增 `The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`：两人都卡住，读秒。
  断言 `elapsed >= 100ms`（两个 50ms 截止都真的生效）且 `< 5s`（不是默认的那个值）。
  硬编码任何别的常数都会让其中一条红。
- `The_callers_own_cancellation_is_not_reported_as_a_failed_recipient` 加断言：给 `RecordingSender`
  加 `Attempted`（记录每一次**发起**的发送，不论结果），断言 `Attempted == [First 的连接]`
  且 `Sent` 为空。**第一版我写成断言 `Sent == [First]`，跑出来是红的**——因为取消发生在
  `SendAsync` 记录之前。改对之后这条判据说的是「第二个收件人根本没被发起」，正是要证明的那件事。

### Executor Self-check — Round 2

- 生产改动：`INotificationBus.cs` 的 `NotificationOutcome` 注释（三个成员）、`NotificationBus.cs` 的
  `AttemptAsync` 可见性 + `IsStillHere` 位置与注释 + options 注释、`INotificationServices.cs` 的解析器 remarks。
  **`IsStillHere` 移进 `try` 是本轮唯一的行为相关改动**——它把「会话查找抛异常」从「掀掉整次发布」
  变成「该收件人记 Failed」。这是修隔离，不是改语义。
- 判据改动：新增 2 条（重登、截止时间），加强 1 条（取消），新增 `RecordingSender.Attempted`。
- 未做：真实 TouchSocket 发送响应取消令牌这件事仍未验，按快审意见不在本轮补。

### Round 2 全量判据

52 + 510 + 60 + 6 + 256 + 223（跳过 2）+ 985 = **2092 通过 / 2 跳过 / 0 失败**。
Round 1 为 2090，净 +2（重登、截止时间两条新判据）。构建 0 警告 0 错误。未遇既知偶发失败。

## Independent Quick Review — Round 2

- Verdict: **changes_required**
- 评审的契约：Task Unit Contract revision 1 + Contract Amendment Revision 2（本轮未再变更）
- 审的范围：Controller Disposition 之后的 Execution Result — Round 2 与 Executor Self-check — Round 2。
  Round 1 已判通过的部分（CK-006 两个探针、CK-011/012/013、摘要口径）不重开。
- Owned diff identity（基线 `b05d47e4`，未提交）：
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs`（md5 `549ea955…`）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`（md5 `f875c235…`）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs`（md5 `8beffea5…`）、
  `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs`（md5 `c778ffb1…`，10 条 `[Fact]`）、
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs`（md5 `2109a1aa…`，TASK-01 产物）。
  tracked 的两处改动 `ApplicationModule.cs` / `Dfo.Server.Application.Tests.csproj` 已 `git diff` 逐行核对，
  与 Round 1 所录**逐字相同**（一行 `AddNotificationDispatching()` + 一个 `Microsoft.Extensions.Hosting` 包引用），
  本轮未再动。边界外无新增改动。
- 评审后工作区已复原：三次变异后 `NotificationBus.cs` 的 md5 回到 `f875c23545390152aa4dc38f293b417b`
  （与开审时一致），另外四个文件全程未动；`git status` 与开审时逐行相同；计时探针工程建在 scratchpad，未进工作区。
  复原后重新 `dotnet build Server/86JP.Server.slnx --nologo` → 已成功生成，0 个警告，0 个错误。

### 复核过的命令与证据

| # | 做法 | 结果 |
|---|---|---|
| S-1 | 全量判据自行重跑：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | Networking 52 / Pvf 60 / Protocol 510 / Host 6 / Persistence 256 / Infrastructure 223（跳过 2）/ Application 985 = **2092 通过 / 2 跳过 / 0 失败**，与 Round 2 所录**完全一致**。Round 1 为 2090，净 +2；`NotificationDeliveryTests.cs` 由 8 条增至 10 条 `[Fact]`，`NotificationAudienceTests`(6) 与 `NotificationRegistrationTests`(7) 未变，+2 全部来自本轮新判据。未遇既知偶发失败 |
| S-2 | `dotnet build Server/86JP.Server.slnx --nologo`（`TreatWarningsAsErrors` 下即 CK-010 再确认） | 已成功生成，0 个警告，0 个错误 |
| S-3 | 变异 M-1：`IsStillHere` 去掉连接比较，只判 `FindByCharacter(...) is not null` | **只有** `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed` 变红（9 通过 / 1 失败），报 `Assert.Equal() Failure: Collections differ … Expected [7002] / Actual []`。新判据**确实钉住了连接比较那一半**，且不与「离线归 Gone」那条重叠 |
| S-4 | 变异 M-2：`deadline.CancelAfter(options.PerRecipientTimeout)` 换成硬编码 `TimeSpan.FromSeconds(2)`（加 `_ = options;` 避开 CS9113） | **10 条全绿**，`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` 没红（该文件跑时长由 271 ms 变 6 s）。见 G-1 |
| S-5 | 变异 M-3：把 `catch (OperationCanceledException) when (…) { throw; }` 改成 `return Failed`，并在 `PublishAsync` 的 foreach 之后补 `cancellationToken.ThrowIfCancellationRequested()`——即「异常照抛，但人群继续走完」 | **只有** `The_callers_own_cancellation_is_not_reported_as_a_failed_recipient` 变红，且 `Assert.ThrowsAny` 仍满足、报错来自集合比较。新加的 `Attempted` 断言**确实**单独钉住了「第二个收件人根本没被发起」，不是靠异常类型顺带过 |
| S-6 | `Gone` 注释里那句关于**别处代码**的断言（「重登后的新会话自己会跑完整的入场序列从头建立状态」）核实 | 属实。`SelectedSessionDomainService.Select` 全仓唯一生产调用点是 `SelectCharacterPhaseCommands.cs:91`，即同一角色换连接必经 SelectCharacter 路径；`SelectCharacterHandler.ExecuteAcceptedWorkflowAsync` 由 `stateMessages.CreateValidated(prepared)` 起，按新取的 `prepared` 重发整批入场帧、pre-publication 四帧、refill 增量与 publication。**不是过强表述**。一处口径见 G-4 |
| S-7 | `NotificationDeliveryOptions` 改后注释与 self-check 的一致性 | 正文已退回已证范围（"Read this as the deadline this code enforces, not as a proven ceiling on wall-clock time per recipient"），与「真实发送是否响应令牌未验」不再打架，也没有换一句新的过强表述。**F-2 在这个文件里已解决**；同类的一句仍留在别处，见 G-3 |
| S-8 | `AttemptAsync` 改 `private` 后的引用核对 | 全仓 grep：`NotificationBus.cs` 内唯一调用点是同类的 `PublishAsync`；`Server/tests` 下同名符号只有 `SqliteEntryLifecycleRecoveryTests` 里一个无关的本地方法。无判据依赖它，构建 0/0（S-2）。**F-3 已解决** |
| S-9 | `INotificationAudienceResolver` 的 remarks 改写与代码对照 | 新说法「the question "which connection is this character on" stays inside the notification service, here and in the bus, which asks it again at each recipient's turn to see whether the answer still holds」与 `NotificationBus.IsStillHere` 每收件人一次 `FindByCharacter` 完全对上；`NotificationAudienceResolver.cs` 自身的 remarks 里没有残留的「唯一」式说法。**F-5 已解决** |
| S-10 | TASK-01 产物回归：`INotificationServices.cs` 本轮只动了注释吗 | 是。声明面与 TASK-01 Execution Result 所录逐项相同（`NotificationAudience` 抽象 record + 私有无参构造 + 嵌套 `TheseCharacters(IReadOnlyList<CharacterId>)`、`NotificationRecipient(CharacterId, NetworkConnectionId)`、`INotificationAudienceResolver.Resolve`）；TASK-01 快审 Round 2 逐句核过的那几句（"shapes below are siblings…"、"the only constructor that makes one out of nothing is private"、"it is less than closed -- so here is exactly where it stops"、反例代码与 "That compiles, and both halves read back"、"make anything else a deliberate act"）**全部原封不动**在文件里。改的只有 `INotificationAudienceResolver` 那段 remarks 的末句。`NotificationAudienceTests` 6 条全绿。**无回归** |
| S-11 | 100 ms 下界的抖动风险实测（scratchpad 独立探针，300 轮，每轮两次 50 ms 链接 CTS + `Task.Delay(Infinite)`） | `min=114.77ms, under100ms=0`。定时器在本机不会提前触发，下界有约 15% 余量；负载只会把 elapsed 推大，故 `>= 100ms` 侧**风险低**。上界 5 s 侧的问题不是抖动，是太松，见 G-1 |

### 必须修（Required）

**G-1 加强后的截止时间判据仍然放过了最该排除的那个常数，而 Execution Result 说它「任何别的常数都会红」**

S-4：把 `deadline.CancelAfter(options.PerRecipientTimeout)` 原地换成硬编码 `TimeSpan.FromSeconds(2)`，
`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` **仍然是绿的**，全文件 10 条全绿。
原因是算术：两个收件人 × 2 s = 4 s，正好落在断言的 `>= 100ms` 与 `< 5s` 之间。

而 2 秒不是随便挑的常数——它就是 `NotificationDeliveryOptions.Default`，也是
`NotificationDispatchingExtensions` 里 `TryAddSingleton(NotificationDeliveryOptions.Default)`
注册给生产的那个值。也就是说：**这条判据分不出「读了注入的 options」与「写死了生产默认值」**，
而这正是 Round 1 F-6(a) 要它分出来的那件事（原文：「它没有钉住『用的是 `options.PerRecipientTimeout` 这个值』」）。

据此，本轮 Execution Result「两条判据的加强」小节里的这句是**假的**：

> 硬编码任何别的常数都会让其中一条红。

判据自己的 remarks 也跟着假：「a bound generous enough that a loaded machine does not turn it red while
still being **far below what the other configured value would cost**」——若「the other configured value」
指 `Default` 的 2 s，两个收件人只要 4 s，`< 5s` 并不 "far below"，反而正好放过它。

这是本成果被点名过三轮的同一类问题：**一句比代码强的话**，而这次那句话说的正是这条判据存在的理由。
就地可修，二选一（或都做）：

1. 把上界收到能排除 2 s 的位置（例如 `< 1s`：S-11 实测 min 114.77 ms，约 9 倍余量；M-2 下 elapsed ≈ 4 s 必红）；
2. 把话说回到已证范围——写明它排除的是「大于 X 的任何常数」，而不是「任何别的常数」。

只做 2 不做 1 的话，这条判据对 F-6(a) 的原始诉求就没有实质增益，请一并说明为什么可以接受。
（附带一处易读错：`World` 未指定超时时用的正是 5 s，与断言上界同值，读起来容易让人以为上界排除的是「另一个配置值」。）

### 建议修（Recommended，不阻断）

- **G-2 本轮唯一的行为改动没有判据。** Self-check 自认「`IsStillHere` 移进 `try` 是本轮唯一的行为相关改动」，
  这个描述**准确**：`IsStillHere` 现在位于 `try` 内，会话查找抛出会落到通用 `catch` 归 `Failed`，
  不再掀掉整次发布。但**没有任何判据钉住它**——`World` 注入的是真的 `SelectedSessionDomainService`，
  `FindByCharacter` 的 `SingleOrDefault` 又被 `Select` 的唯一性检查挡住，经现有测试替身触发不到。
  同时新写的注释把这件事当结论陈述（"a session lookup that threw would otherwise take down the whole
  publish"）。Required methods 里有 `test-driven-development`，一个会抛的 `ISelectedSessionDomainService`
  假件加一条判据即可；不补的话，至少让注释说明这条路径未被判据覆盖。
- **G-3 F-2 退回去的那句话，在一个文件之外还留着原版。** `NotificationDeliveryOptions` 已改成
  「不是每收件人墙钟时间的已证上限」，但 `A_recipient_who_runs_out_of_time_is_a_failure` 的 remarks 仍写
  "Delivery is sequential, so this bound is what stops one stalled socket from holding the audience"
  ——同一件未验的事，一处已退、一处未退。建议让两处口径一致。
- **G-4 `Gone` 注释末句的适用范围比它写的窄。** S-6 证实前半（新会话跑完整入场序列重建状态）为真，
  但末句 "What they get instead is the correct one, from their own entry" 只对**状态形态**的通知成立；
  对事件形态的通知（本文件判据自己用的就是 `TownBellRang`）入场序列并不会补发，重登者是**什么都没有**，
  而不是「拿到正确的那份」。选择本身仍然站得住（旧答案确实是陈的），建议把末句限定到状态类通知。

### 结论

Round 1 的两条阻断，F-1 的整改**实质到位**：新判据经 M-1 变异确认钉住了连接比较那一半，
`Gone` 注释里那句关于 SelectCharacter 的断言经代码核实属实（S-6），归类选择与被否掉的读法都写进了判据注释；
F-2 在 `NotificationDeliveryOptions` 上已改回已证范围，正文与 self-check 不再打架，也没有换一句新的过强表述。
四条建议项中 F-3（`private`）、F-5（解析器 remarks）、F-6(b)（`Attempted`，经 M-3 变异确认单独有效）均已解决；
F-4 行为已修但无判据（G-2）。全量 2092/2/0 与所录逐项相符，构建 0 警告 0 错误，
TASK-01 产物只动注释、声明面与已核过的措辞无回归。

仍需返工的只有一条：**G-1**——F-6(a) 的整改没有达到它自己宣称的效果，而宣称本身（「硬编码任何别的常数都会红」）
已被 M-2 直接证伪，且放过的恰是生产在用的那个默认值。改一个上界数字与一句话即可，仍在本 Task Unit 内。

## Controller Disposition — after Quick Review Round 2

`task-rework`。G-1 成立，且它证伪的正是我 Round 2 自己写下的那句话
（「硬编码任何别的常数都会让其中一条红」）。三条建议项一并处理。

## Execution Result — Round 3

### G-1：加强后的截止时间判据仍分不出「读了 options」与「写死了生产默认值」

快审的算术是对的：2 个收件人 × 硬编码 2 秒 = 4 秒，正落在我写的 `>= 100ms` 且 `< 5s` 之间。
而 **2 秒恰是 `NotificationDeliveryOptions.Default`，也是注册给生产的那个值**——所以那条判据挡不住
「把生产默认值抄成常数」这一种，而这正是 Round 1 F-6(a) 要它挡的。

上界收到 **1 秒**。实测两次 50ms 截止约 115ms，余量约 9 倍；变异复核：

```
变异  deadline.CancelAfter(TimeSpan.FromSeconds(2));   // 硬编码生产默认值
→ 失败 The_deadline_a_stalled_recipient_runs_into_is_the_configured_one [4 s]
   Two 50ms deadlines took 4022ms, so some other value was applied --
   the production default of two seconds would land near four.
还原后 → 已通过 (159 ms)
```

判据的 remarks 也改了：明写「上界是做功的那一半，必须低于生产默认值在这里的开销——两人 × 2 秒 = 4 秒，
所以 5 秒的上界什么都证明不了」。

### G-2：`IsStillHere` 移进 `try` 是行为改动，却没有判据钉着

补 `A_presence_lookup_that_throws_costs_only_that_recipient`。

**第一版写错了，值得记**：我直接 `Sessions.ThrowFor(First)`，跑出来是红的——
因为**人群解析走的是同一个会话服务**，从一开始就拒答会让 `audiences.Resolve` 先抛，
整次发布在进入任何 attempt 之前就掀了。那是另一个时刻、另一个答案，测的根本不是这条。
改成由第一次投递**触发**拒答（`Sender.OnSend = _ => Sessions.ThrowFor(Second)`），
才落在「解析之后、该收件人轮到时」这个要测的时刻。这条理由写进了判据注释。

替身 `AwkwardSessions` 包住真的 `SelectedSessionDomainService`，只对指定角色抛。
用替身而非真实故障，是因为真实故障今天造不出来：`FindByCharacter` 用 `SingleOrDefault`，
而目录自己的准入检查让「一个角色两个会话」不可能。**今天不可能不等于永远不可能**，
这条判据说的是万一发生时代码往哪边倒。

变异复核：

```
变异  把 IsStillHere(recipient) 移回 try 之外（即 Round 1 的写法）
→ 失败 A_presence_lookup_that_throws_costs_only_that_recipient
   （11 条中恰好红这一条）
还原后 → 11 条全绿；NotificationBus.cs 的 md5 与变异前一致 f875c235...
```

### G-3 / G-4

- G-3：`A_recipient_who_runs_out_of_time_is_a_failure` 的 remarks 里还留着 F-2 已经退掉的那句「上界」。
  已改，并指向 `NotificationDeliveryOptions` 说明它到哪儿为止。
- G-4：`Gone` 注释末句「What they get instead is the correct one, from their own entry」只对**状态形态**的
  通知成立；**事件形态**（判据自己用的 `TownBellRang` 就是）的重登者是什么都没有。已补一段分开写这两种，
  并明说事件形态那部分是**这个选择的代价**而不是反对它的论据。

### Executor Self-check — Round 3

- 生产改动：`INotificationBus.cs` 的 `Gone` 注释补一段（G-4）。**其余生产文件本轮零改动**——
  G-1/G-2/G-3 全在判据与判据注释里。
- 判据改动：新增 1 条（G-2），改上界与 remarks 1 条（G-1），改 remarks 1 条（G-3），
  新增替身 `AwkwardSessions`。
- 两次变异均已逐字还原并核对 md5。

### Round 3 全量判据

52 + 510 + 60 + 256 + 6 + 223（跳过 2）+ 986 = **2093 通过 / 2 跳过 / 0 失败**。
Round 2 为 2092，净 +1（G-2 的那条新判据）。构建 0 警告 0 错误。未遇既知偶发失败。

## Independent Quick Review — Round 3

- Verdict: **passed**
- 评审的契约：Task Unit Contract revision 1 + Contract Amendment Revision 2（本轮未再变更）
- 审的范围：Controller Disposition — after Quick Review Round 2 之后的 Execution Result — Round 3 与
  Executor Self-check — Round 3，即 G-1/G-2/G-3/G-4 四条的整改。Round 1/Round 2 已判通过的部分
  （CK-006 两个探针、CK-011/012/013、摘要口径、F-1/F-2/F-3/F-5/F-6(b) 的整改）不重开。
- Owned diff identity（基线 `b05d47e4`，未提交）：
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs`（md5 `1be1aa7d33a8e1e23359d617b1075020`，
  较 Round 2 的 `549ea955…` 改变——`Gone` 注释补段，G-4）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`（md5 `f875c23545390152aa4dc38f293b417b`，
  **与 Round 2 所录逐字相同**）、
  `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs`（md5 `8beffea50b5a64880e7418da9ee00b6c`，未变）、
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs`（md5 `2109a1aad786de8e7dcda6e3ce1013cd`，未变）、
  `Server/tests/Dfo.Server.Application.Tests/Notifications/NotificationDeliveryTests.cs`（md5 `4075da3f858f474fca98ee199a8896d4`，11 条 `[Fact]`）。
  `git diff` 逐行核对：tracked 的两处（`ApplicationModule.cs` 一行 `AddNotificationDispatching()` +
  `Dfo.Server.Application.Tests.csproj` 一个 `Microsoft.Extensions.Hosting` 包引用）与 Round 1/2 所录**逐字相同**，
  本轮未再动。`--untracked-files=all` 全列：无新增生产文件，边界外无改动。
- 评审后工作区已复原：四次变异（三次生产文件、一次判据文件）后全部逐字还原，五个文件 md5 与开审时
  **逐项一致**，`git status` 与开审时逐行相同；trx logger 产生的 `Server/tests/*/TestResults/` 已清除；
  编译探针建在 scratchpad 独立工程，已删。复原后 `dotnet build Server/86JP.Server.slnx --nologo`
  → 已成功生成，**0 个警告，0 个错误**（`TreatWarningsAsErrors` 下即 CK-010 再确认）。

### 复核过的命令与证据

| # | 做法 | 结果 |
|---|---|---|
| T-1 | 全量判据自行重跑：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | Protocol 510 / Networking 52 / Pvf 60 / Persistence 256 / Host 6 / Infrastructure 223（跳过 2）/ Application 986 = **2093 通过 / 2 跳过 / 0 失败**，与 Round 3 所录**逐项一致**。未遇既知偶发失败 |
| T-2 | 复原后再跑一次全量（第二个独立样本） | 同上 2093/2/0，逐项一致。两次全绿，可复现 |
| T-3 | 分项来源核对：`NotificationDeliveryTests` 11 条、`NotificationAudienceTests` 6 条、`NotificationRegistrationTests` 7 条 `[Fact]` | Round 2 为 10/6/7；净 +1 **全部**来自 G-2 的新判据 `A_presence_lookup_that_throws_costs_only_that_recipient`。与所录相符 |
| T-4 | 变异 M-a（**重做执行方的那次**）：`deadline.CancelAfter(options.PerRecipientTimeout)` → 硬编码 `TimeSpan.FromSeconds(2)`（加 `_ = options;` 避开 CS9113） | **恰好** `The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` 变红（10 通过 / 1 失败），报 `Two 50ms deadlines took 4034ms, so some other value was applied -- the production default of two seconds would land near four.`。执行方所录 4022ms 属同一现象。**收到 1 秒确实挡住了生产默认值** |
| T-5 | 变异 M-b（**快审另加的一次**）：硬编码 `TimeSpan.FromMilliseconds(200)` | **11 条全绿**。这条判据**仍然挡不住小于 1 秒的硬编码常数**。见 H-1 |
| T-6 | 变异 M-c：硬编码 `TimeSpan.FromMilliseconds(20)`，探下界 | 同一条变红，报 `took 71ms, so they were not applied`。故硬编码常数的**存活窗口约为每收件人 [50ms, 500ms)**——两侧之外（含生产默认的 2 秒、`World` 兜底的 5 秒）都会红 |
| T-7 | 变异 M-d：把 `IsStillHere(recipient)` 移回 `try` 之外（即 Round 1 的写法） | **恰好** `A_presence_lookup_that_throws_costs_only_that_recipient` 变红（10 通过 / 1 失败），异常 `System.InvalidOperationException : Sequence contains more than one element.` **越过整次发布**抛到用例外。G-2 的新判据确实单独钉住了「复问进 try」这一件事 |
| T-8 | G-2 判据落点核实（读代码）：新判据是否真在「解析之后、该收件人轮到时」 | 是。`NotificationAudienceResolver.Reachable`（`NotificationAudienceResolver.cs:34-45`）对每个角色调 `sessions.FindByCharacter`，故预先 `ThrowFor` 会让 `audiences.Resolve`（`NotificationBus.cs:43`）先抛、整次发布在进入任何 attempt 之前掀掉。判据改用 `Sender.OnSend = _ => Sessions.ThrowFor(Second)`，由 First 的发送（在 First 的 `IsStillHere` 与 compose 之后）触发拒答，Second 轮到时才抛——**落点正确**，执行方记的「第一版写错了」的理由属实 |
| T-9 | 替身 `AwkwardSessions` 的诚实性与「真实故障今天造不出来」核实 | 属实。`SelectedSessionDomainService.FindByCharacter:48-51` 用 `SingleOrDefault(谓词)`；`Select:14-30` 拒绝「同 CharacterId 但不同 AccountId」的会话，而字典以 AccountId 为键、同账号只能有一份，故「一个角色两个会话」今天确实构造不出。替身包住真的服务、只对指定角色抛，且注释明写它是替身而非真实故障——**没有假装成别的东西**。一处措辞不精确见 H-3 |
| T-10 | 1 秒上界的抖动风险**实测**（把上界临时改成 1ms 逼它打印实测值，跑 4 次**全量并行**满载） | 实测 elapsed = **240 / 280 / 197 / 315 ms**，最差 315ms vs 上界 1000ms，余量约 **3.2 倍**。另：trx 里该用例 xUnit 报的 duration 曾达 **907ms**，但那含 `new World()` 的 DI 容器构建与 JIT（在 `Stopwatch` 起点之前），不进断言。**今天不构成偶发失败风险**（6 次全量运行该用例全绿），但余量比 remarks 自称的「九倍」小得多。见 H-2 |
| T-11 | G-4 的 `Gone` 注释：`<para>` 配平与 XML 合法性 | `INotificationBus.cs:28-45` 两段 `<para>` 各自闭合；对三个改动文件的**全部** `///` 块做 XML 解析（`INotificationBus.cs` 7 块、`NotificationBus.cs` 3 块、`NotificationDeliveryTests.cs` 17 块）→ **0 处非良构**。注：本仓未开 `GenerateDocumentationFile`，编译器不校验文档 XML，故此项为独立核对而非编译副产物 |
| T-12 | G-4 内容与代码一致性 | 新段把「状态形态」与「事件形态」分开写，并明说事件形态那部分是**这个选择的代价**而非反对它的论据——正是 G-4 要的限定。判据自己用的 `TownBellRang` 属事件形态，与新段自洽。**没有新的过强表述** |
| T-13 | G-3 核对 | `A_recipient_who_runs_out_of_time_is_a_failure` 的 remarks 已改为「What it bounds is what this code enforces on the token it hands the transport -- see NotificationDeliveryOptions for where that stops」，与 `NotificationDeliveryOptions` 已退回的口径一致，两处不再一真一假。**G-3 已解决** |
| T-14 | 回归：Round 3 是否只动了 `INotificationBus.cs` 的注释 | 是。`NotificationBus.cs` md5 `f875c235…` 与 Round 2 所录**逐字相同**（执行方「两次变异后已逐字还原」属实）；`NotificationDispatchingExtensions.cs`、`INotificationServices.cs` 亦未变；`INotificationBus.cs` 的改动仅为 `Gone` 的 remarks 补两段 `<para>`，声明面（四个枚举成员、`NotificationAttempt`、`NotificationDelivery.With`、`INotificationBus.PublishAsync` 签名与约束）**未动** |

### 四条整改的判定

- **G-1 已整改到快审 Round 2 指定的那一步。** 该条给的是二选一，执行方选了第 1 项「把上界收到能排除 2 s 的位置」，
  T-4 直接确认有效（4034ms 必红）。Round 2 证伪的那句「硬编码任何别的常数都会让其中一条红」**已从文中消失**，
  改后的 remarks 把话锁在生产默认值上（"it has to be below what the production default would cost here"），
  这是已证范围内的表述。残余口子见 H-1，**不阻断**——它不是 G-1 点名的那一类，也不是 F-6(a) 的原始诉求。
- **G-2 已解决。** 判据落点正确（T-8）、变异单独有效（T-7）、替身诚实且「真实故障今天造不出来」属实（T-9）。
- **G-3 已解决**（T-13）。**G-4 已解决**（T-11、T-12）。

### 整体收口判断（TASK-03 作为一个整体）

逐条对契约受理条件与 Amendment Revision 2：

| 受理条件 | 覆盖它的判据 / 证据 | 判定 |
|---|---|---|
| 摘要的归类恰好四种 | `NotificationOutcome` 四个成员，四种在本文件判据里各被断言至少一次 | 覆盖 |
| 「解析之后投递之前离线」归 `Gone` | `Someone_who_left_is_told_apart_from_someone_whose_delivery_failed` | 覆盖 |
| 投递失败归 `Failed` | `A_recipient_whose_delivery_fails_does_not_stop_the_others` | 覆盖 |
| 超时归 `Failed` | `A_recipient_who_runs_out_of_time_is_a_failure` | 覆盖 |
| 处理器抛异常归 `Failed` | `A_handler_that_throws_for_one_recipient_fails_only_that_one` | 覆盖 |
| 一个收件人失败，其余各自仍收到自己的消息 | 上述失败判据 + `A_presence_lookup_that_throws_costs_only_that_recipient`（Round 3 新增，把隔离从「发送/处理器」扩到「会话查找」） | 覆盖 |
| 不做并发 | 无直接判据，但被两条**间接**钉住：`The_callers_own_cancellation…` 断言 `Attempted == [First]`（并发则两人都会被发起）、`Someone_who_left…` 依赖「First 的发送使 Second 离场」这一时序 | 覆盖（间接，见 H-4） |
| 不做节流 | 代码中无节流；这是「不存在某物」，无可断言 | 不适用 |
| 超时值全局一致且不可按通知覆盖 | `PublishAsync` 签名无超时参数；`NotificationDeliveryOptions` 以 `TryAddSingleton` 单例注册；`The_deadline…is_the_configured_one` 钉住读的是注入值（口子见 H-1） | 覆盖 |
| 全量判据通过 | T-1/T-2 两次独立全量 2093/2/0 | 覆盖 |
| CK-006（Amendment Rev 2）：发布调用点的编译器拒绝记录 | Round 1 已录 `CS1503` / `CS0311` 原文，Round 1 快审 R-1/R-2 独立重做并逐字相符 | 覆盖 |

**没有发现未被判据覆盖的受理条件。** 两处仍然敞着但**不属本 Task Unit**、且已被如实记录的：
(1) 真实 TouchSocket 发送是否响应取消令牌——Round 2 快审明确不要求本轮补（`AGENTS.md` 写明真 socket 用例满载会假红），
文档口径已两处退回已证范围（`NotificationDeliveryOptions` + 超时判据 remarks），留给最终评审/后续任务；
(2) RQ-009 的「`Gone` 不进重试、`Failed` 进重试」的**重试**那一半按计划属 T-004（BD-007），
本任务只交分类，Self-check Round 1 已声明。两者都不是本单元的漏项。

### 建议修（Recommended，不阻断）

- **H-1 截止时间判据仍放过 [50ms, 500ms) 区间内的硬编码常数（T-5/T-6）。** 硬编码
  `TimeSpan.FromMilliseconds(200)` 时 11 条全绿。这不是 G-1 点名的那一类（G-1 说的是生产默认值 2 秒，已挡住），
  所以不阻断；但 remarks 里 "still far under any plausible hard-coded value"
  （`NotificationDeliveryTests.cs:166-167`）对 200ms 这个硬编码值是不成立的。建议把该从句收到已证范围，
  例如「排除任何每收件人 ≥500ms 的常数，生产默认的 2 秒在其中」——这也正是 Round 2 G-1 给的第 2 项，
  与已做的第 1 项并不冲突。
- **H-2 「roughly nine times」是空载数字（T-10）。** 满载全量并行下实测 197–315 ms，1 秒上界的真实余量约
  **3.2 倍**而非 9 倍。今天不假红（6 次全量该用例全绿，最差 315ms），但 `NotificationDeliveryTests.cs:165-167`
  这句把安全垫说宽了约三倍。建议改成满载实测值，或干脆不提倍数、只说它低于生产默认值在这里的开销。
- **H-3 `AwkwardSessions` 模仿的是另一个重载的消息（T-9 附带探针）。**
  `NotificationDeliveryTests.cs:323` 抛 `"Sequence contains more than one element."`，
  而 `FindByCharacter` 用的是**带谓词**的 `SingleOrDefault`，真实消息是
  `Sequence contains more than one matching element`（无句点）；`Sequence contains more than one element`
  是**无谓词**重载的消息，且真实值也没有末尾句点。异常**类型**是对的，消息从不被代码读取，故无行为后果，
  替身本身也明写自己是替身——只是模仿得不够准。顺带：那个字符串是 `$"…"` 但没有任何插值洞，`$` 可删。
- **H-4 「不做并发」只有间接判据。** 两条判据依赖顺序时序而成立（见上表），改成并发实现会让它们红，
  所以不是零覆盖；但没有一条判据以「顺序性」为**主题**。属可选加强，也可能更适合留给最终评审整体看。

### 结论

Round 2 唯一的阻断 G-1 **整改到位且经独立变异确认**：硬编码生产默认值 2 秒会让那条判据红（T-4，4034ms），
Round 2 被证伪的那句普遍性断言已被删除、换成锁在生产默认值上的说法。三条建议项 G-2/G-3/G-4 全部解决：
G-2 的新判据落点正确（T-8）、变异单独有效（T-7）、替身诚实（T-9）；G-3 两处口径已一致；
G-4 的新段 `<para>` 配平、XML 良构、内容与代码一致且限定得当。

计数 2093/2/0 经**两次**独立全量确认，分项与净 +1 的来源逐项对得上；构建 0 警告 0 错误；
回归面上 Round 3 除 `INotificationBus.cs` 的注释外零生产改动，`NotificationBus.cs` 的 md5 与 Round 2 逐字相同，
边界外无改动。契约受理条件与 CK-006 逐条有判据或证据支撑，**未发现未覆盖项**。

四条建议项都是「话比证据宽一点」的同类问题（H-1/H-2 是量化措辞、H-3 是替身措辞、H-4 是可选加强），
均不改变任何已证结论，也都不需要在本轮返工。本 Task Unit 可按执行方的受理逻辑继续。

## Controller Disposition — after Quick Review Round 3

`passed`。最新的执行结果、自检、快审裁定与 owned diff 指向同一轮。

四条建议项里的三条就地处理——H-1/H-2 与本成果已经返工四次的原因是同一类（一句比实测强的话）：

- **H-1**：remarks 里「far under any plausible hard-coded value」对 200ms 那类常数不成立。快审实测出
  硬编码常数的存活窗口约为每收件人 **[50ms, 500ms)**。改成只声称它实际决定的那一件事：
  「production default 不是跑的那个」，并明写它**不**决定每一个硬编码值。
- **H-2**：「roughly nine times」是空载数字。快审满载并行实测 **197–315 ms**，余量约 **3 倍**。
  改成实测数并注明「if this ever goes red at a plausible-looking number, it is the machine, not the deadline」。
- **H-3**：替身模仿的是 `SingleOrDefault` **无谓词重载**的消息；真实带谓词重载是
  `Sequence contains more than one matching element`（无句点）。已改，并去掉那个没有插值洞的 `$`。
- **H-4**（「不做并发」只有间接判据）不处理：并发与否是投递实现的形状，直接判据要靠时序断言，
  抖动代价高于收益。如实记在这里。

改动后 `NotificationDeliveryTests` 11 条仍全绿。**这三处改动发生在快审 passed 之后，须在最终完整变更评审里一并复核。**

登记表置 TASK-03 为 `done`，`tasks.active` 前移至 TASK-04。
