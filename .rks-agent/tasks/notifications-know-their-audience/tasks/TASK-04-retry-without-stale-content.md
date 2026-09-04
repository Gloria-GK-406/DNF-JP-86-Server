# TASK-04 — 失败的收件人有第二次机会，但不带着旧内容

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 内存重试队列与其排空。重试项记（通知, 角色, 已用次数）——记角色不记连接。排空时重新解析连接、重新向处理器取内容。上限来自注册声明，达上限即丢弃。
- Referenced spec requirements: RQ-010, RQ-011
- Referenced plan sections: T-004, BD-007, BD-010
- Owned boundary: `Server/src/Dfo.Server.Application/Notifications/`、`Server/tests/`
- Permitted files or interfaces: 上述路径；ApplicationModule 接线；TASK-03 的失败归类可扩展
- Dependencies: TASK-03
- Required dependency outputs: 失败归类
- Acceptance conditions:
  - 重试**不缓存任何东西**：既不缓存连接也不缓存编码好的帧或处理器产出的消息
  - 重试之前内容变了，送出的是变化后的值
  - 重试之前角色换了连接，送到新连接
  - 达注册声明的上限后不再重试，该项被丢弃
  - 不做去重；队列在内存里不落库
  - 全量判据通过
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: test-driven-development
- Required checks: CK-015, CK-016, CK-017, CK-018, CK-019, CK-020
- Required evidence: 三条重试判据；CK-019 的变异必须**指名失败用例的全限定名与 Expected/Actual 原文**，并确认还原后恢复绿

## Execution Result — Round 1

### 交付

- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationRetryQueue.cs`：
  `NotificationRetry(Registration, Notification, CharacterId, Attempts)` 与 `NotificationRetryQueue`
  （`ConcurrentQueue`，`Add` / `TryTake` / `Count`）
- 新增 `Server/src/Dfo.Server.Application/Notifications/NotificationRetryDrainService.cs`：
  `IHostedService`，`PeriodicTimer` 按 `options.RetryInterval` 排空，单次失败记日志后继续
- `NotificationBus` 增 `INotificationRetryDrain`（`DrainAsync`）与 `RetryAsync`；
  `PublishAsync` 在 `Failed` 且 `RetryLimit > 0` 时入队
- `NotificationDeliveryOptions` 增 `RetryInterval`（默认 1 秒）
- `NotificationDispatchingExtensions` 接线队列、总线的两个门面、排空服务
- 新增 `Server/tests/.../Notifications/NotificationRetryTests.cs`（6 条）
- **提取** `Server/tests/.../Notifications/NotificationWorld.cs`：把 TASK-03 判据里的替身世界抽成共享件

### 一处必须先解决的张力（TASK-03 留下的）

TASK-03 判定**发布期间**的重登归 `Gone`（不发）。而重试是按角色重新解析的，重登者会拿到新连接并**收到**。
这两条看起来矛盾，必须给出理由否则就是不一致：

> **重试是对一个人的一次全新求值，不是那次发布的尾巴。** 发布期间，会话是在**这次发布正跑着**的时候
> 被替换的，而手上那条消息是对着刚消失的那个会话算出来的。重试时**手上什么都没有**：问题从
> 「这个角色是谁、他在哪」重新开始，答案是他当前的会话，处理器对着它现算内容。
> 队列里存的是一个人，不是一个会话——这就是那句话的含义。

写进了 `RetryAsync` 的 `<remarks>`，并在 `A_retry_goes_to_the_connection_they_are_on_now` 的判据注释里
交叉引用，免得后来的读者只看到一边。

### 排空的边界

`DrainAsync` 只处理**这一趟开始时**在队列里的那些。失败者被放到队尾，若一直排空下去会在同一瞬间
把一个通知的全部重试额度烧光——连接根本来不及恢复。`A_pass_works_through_what_was_waiting_and_no_further`
钉住这一条。

`RetryLimit == 0` 时**根本不入队**，而不是入队后下一趟丢弃：后者是同一个结果绕远路，而且会让队列深度
不再说明任何事情。

### 检查

| ID | 命令 / 方式 | 结果 |
|---|---|---|
| CK-015 | `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，0 个警告，0 个错误 |
| CK-016 | `A_retry_asks_the_handler_again_rather_than_resending_what_it_had` | 通过 |
| CK-017 | `A_retry_goes_to_the_connection_they_are_on_now` | 通过 |
| CK-018 | `A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` | 通过。limit=2：发布后待重试 1；三趟排空后待重试 0，共 3 次发起 |
| CK-019 | 变异，见下 | 恰好 CK-016 变红，Expected/Actual 已录 |
| CK-020 | 全量判据 | 见下 |

### CK-019 变异的原始输出

变异内容（模拟「把重试改成重发缓存的帧」这个自然而错误的写法）：`NotificationRetry` 增
`Frames` 字段；`AttemptAsync` 改为连同composed messages一起返回，**投递失败时也把它们带回**；
`PublishAsync` 入队时存下；`RetryAsync` 若 `Frames` 非空则直接重发而不问处理器。

```
失败 A_retry_asks_the_handler_again_rather_than_resending_what_it_had
  Assert.Equal() Failure: Collections differ
                                            ↓ (pos 0)
  Expected: ["second answer"]
  Actual:   ["first answer"]
                                            ↑ (pos 0)
  NotificationRetryTests.cs(40,0)
```

**6 条里恰好红这一条**，其余 5 条全绿——说明它钉住的正是「重新问处理器」这件事，而不是别的。
失败的样子本身就是要防的那个后果：**旧值盖住了新值**。

还原后 6 条全绿，两个源文件 md5 与变异前逐字一致
（`NotificationBus.cs` = `15822745d6cef7020018f20f4c6c98ea`，
`NotificationRetryQueue.cs` = `eebb71c492bb6b5181ed6f1e8a085f8e`）。

### Executor Self-check — Round 1

- **动了 TASK-03 已通过评审的判据文件**：把 `World` 及其替身从 `NotificationDeliveryTests.cs` 提取到
  新文件 `NotificationWorld.cs`，改名 `NotificationWorld`，可见性由 private nested 改 internal。
  这是机械提取，**判据的内容与断言一字未改**；提取后 `Application.Tests.Notifications` 24 条仍全绿。
  另给世界加了三样重试判据需要的东西：构造参数 `retryLimit`、`Waiting`、`DrainAsync`，
  以及 `RecordingSender.StopFailing()`。理由：复制 120 行替身世界更糟——第二份会漂移。
- **未验的**：排空服务的 `PeriodicTimer` 循环**没有判据**。所有重试判据都手动调 `DrainAsync`
  （世界把 `RetryInterval` 设为 `Timeout.InfiniteTimeSpan`）。也就是说「循环真的会按 1 秒周期跑」
  这件事本轮无证据，只有 `Host.Tests` 起真实宿主时它会真的跑起来（队列为空、无副作用）。
- 排空服务注册在校验服务**之后**；顺序对保证无影响（宿主先实例化再启动），但读起来更顺。

### CK-020 全量判据

52 + 60 + 510 + 256 + 6 + 992 + 223（跳过 2）= **2099 通过 / 2 跳过 / 0 失败**。
TASK-03 收尾为 2093，净 +6，与本轮新增的 6 条判据数相符。
`Host.Tests` 6 条仍全绿——排空服务在真实宿主里起来了、队列为空、无副作用。未遇既知偶发失败。

## Independent Quick Review — Round 1

- Verdict: **changes_required**
- 评审的契约：Task Unit Contract revision 1（无修正案）
- Owned diff identity（未提交的未跟踪新文件，基线 `b05d47e4`）——开审即录、收审复核，两次逐字相同：

| 文件 | md5 |
|---|---|
| `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` | `15822745d6cef7020018f20f4c6c98ea` |
| `Server/src/Dfo.Server.Application/Notifications/NotificationRetryQueue.cs` | `eebb71c492bb6b5181ed6f1e8a085f8e` |
| `Server/src/Dfo.Server.Application/Notifications/NotificationRetryDrainService.cs` | `8ebe948a2de8d5cade5b81b848bcf9cf` |
| `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs` | `7e8e8000afec95818653de3820d50a8b` |
| `Server/tests/.../Notifications/NotificationRetryTests.cs` | `d15017ed06eb3acfb1a64feaf4d15547` |
| `Server/tests/.../Notifications/NotificationWorld.cs` | `1943efb779e99331a5a7736d0e63764e` |
| `Server/tests/.../Notifications/NotificationDeliveryTests.cs` | `be1a453658659fa5d7bd1f380961c9d9` |

  跟踪文件的改动只有两处，均属既有轮次遗留而非本轮新增：`ApplicationModule.cs`（+2 行，`AddNotificationDispatching()` 接线）
  与 `Application.Tests.csproj`（+3 行，`Microsoft.Extensions.Hosting`）。**边界外无改动。**
- 评审后工作区已复原：四次变异（全部在 `NotificationBus.cs` / `NotificationRetryQueue.cs`）后逐字还原，
  上表七个 md5 与开审时**完全一致**；`dotnet build` 0 警告 0 错误；Notifications 30 条全绿。

### 独立复核

| ID | 做了什么 | 结果 |
|---|---|---|
| A-1 | CK-020 自行全量重跑：`PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | Protocol 510 / Networking 52 / Pvf 60 / Persistence 256 / Host 6 / Infrastructure 223（跳过 2）/ Application 992 = **2099 通过 / 2 跳过 / 0 失败**，与执行方所录**七个分项与合计逐项一致**。未遇既知偶发失败 |
| A-2 | CK-015 自行重跑 `dotnet build Server/86JP.Server.slnx --nologo` | 已成功生成，**0 个警告，0 个错误**（`TreatWarningsAsErrors` 下即再确认） |
| A-3 | 分项来源核对：Notifications 目录 `[Fact]` 计数 | Audience 6 / Delivery **11** / Registration 7 / Retry 6 = 30。TASK-03 收尾为 6/11/7；净 +6 **全部**来自 `NotificationRetryTests.cs`。`NotificationWorld.cs` 0 条 `[Fact]`——提取件本身不带判据。Application.Tests 986 → 992 与之相符 |
| A-4 | **CK-019 变异独立重做**（不照执行方写法，自行实现「重试改成重发缓存的帧」）：`NotificationRetry` 增 `Frames`；`AttemptAsync` 改返回 `(Outcome, Frames)` 且**失败时把 composed 带回**；`PublishAsync` 入队时存下；`RetryAsync` 若 `Frames` 非空走新增的 `ResendAsync`（仍然重新解析连接，只是不再问处理器） | **30 条里恰好红 `A_retry_asks_the_handler_again_rather_than_resending_what_it_had` 一条**（29 通过 / 1 失败）。报文 `Assert.Equal() Failure: Collections differ … Expected: ["second answer"] / Actual: ["first answer"]`，落点 `NotificationRetryTests.cs:line 40`——与执行方所录**逐字相符**（含行号 40）。其余 5 条重试判据与 11 条投递判据全绿，说明 CK-016 钉住的确实是「重新问处理器」而不是别的 |
| A-5 | A-4 还原后核 md5 | `NotificationBus.cs` = `15822745d6cef7020018f20f4c6c98ea`、`NotificationRetryQueue.cs` = `eebb71c492bb6b5181ed6f1e8a085f8e`，与执行方给的还原后哈希**完全一致** |
| A-6 | **排空边界变异**：`DrainAsync` 的 `for (var pending = retries.Count; …)` 换成 `while (retries.TryTake(out var entry))` | `A_pass_works_through_what_was_waiting_and_no_further` **变红**，报 `Expected: 4 / Actual: 12`——正是「一趟把 5 次额度全烧光」那个后果（2 人 × 5 次 + 首发 2 次）。这条判据**确实钉住了「只处理这一趟开始时排着的那些」**。同时 `A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` 也红（Expected 1 / Actual 0），属预期连带 |
| A-7 | **上限守卫变异**：删掉 `if (entry.Attempts >= entry.Registration.RetryLimit) return;` | **恰好** `A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` 变红（Expected 0 / Actual 1）。**这个守卫不是死代码**，见 A-8 |
| A-8 | 重试计数语义对 RQ-011 的逐条核对（读代码 + A-7 佐证） | `RetryLimit: 2` 的实际轨迹：首发失败入队 `Attempts=0`；第 1 趟守卫 `0>=2` 不成立 → 重试失败 → 回队 `Attempts=1`；第 2 趟 → 回队 `Attempts=2`；第 3 趟守卫 `2>=2` 成立 → 丢弃。**共 3 次发起（1 首发 + 2 重试），与执行方所述一致**，且被 `Assert.Equal(3, world.Sender.Attempted.Count)` 钉住。与 RQ-011 三句逐条对应：上限来自 `AddNotificationHandler` 的 `retryLimit`（注册时声明）✓；`NotificationRetry.Attempts` 记自己已用次数 ✓；达上限丢弃 ✓。**守卫在 `RetryLimit == 0` 不入队的前提下仍然是活代码**——它管的是 `RetryLimit >= 1` 时链条的终止，A-7 证明删掉它 CK-018 立刻红 |
| A-9 | **提取是否机械**：`NotificationDeliveryTests.cs` 11 条逐条与 TASK-03 三轮记录对照（CK-011/012/013 的结果描述、S-3 的重登判据三条断言、G-1 的上界 `< 1s`、F-6(b)/S-5 的 `Attempted` 断言、G-2 的 `A_presence_lookup_that_throws…` 及其 `OnSend` 触发写法、G-3 改后的 remarks 原文） | **逐条对上，无一条断言被削弱或删除，11 条判据仍在**（29 个 `Assert.`）。G-3 的 remarks 原文「What it bounds is what this code enforces on the token it hands the transport -- see NotificationDeliveryOptions for where that stops」与 TASK-03 T-13 所录**逐字相同** |
| A-10 | **提取的强证据**：重做 TASK-03 快审的变异 M-1（`IsStillHere` 去掉连接比较，只判 `FindByCharacter(…) is not null`） | **恰好** `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed` 变红，报 `Expected: [7002] / Actual: []`——与 TASK-03 快审 Round 2 的 S-3 所录**逐字相同**。提取之后这条判据的牙齿**一颗没掉**，这比逐行读断言更硬 |
| A-11 | 提取件的行为中性核对（读代码） | `NotificationWorld` 构造参数 `retryLimit` **默认 0**，投递判据一律 `new NotificationWorld()`，故 `PublishAsync` 的入队分支（`RetryLimit > 0`）对它们**一次也不进**——投递语义与 TASK-03 逐值不变。`ComposeRule` 捕获的是 `recipient => Compose(recipient)`（读属性而非取值），所以 CK-016 中途换 `Compose` 才生效；`Sender` 属性初始化早于构造器体，注入的是同一实例。**未发现提取带来的行为差异** |
| A-12 | `DrainAsync` 的并发性判断（读代码） | `ConcurrentQueue` FIFO + `Add` 追加到队尾，而循环从队头至多取 `pending` 个，故**并发 `PublishAsync` 入队不会污染这一趟**（新项在队尾，取不到），失败回队项也一样。唯一的危险是**两个排空同时跑**：生产里只有 `NotificationRetryDrainService` 一个循环，且 `INotificationRetryDrain` 是 `internal`，没有第二个生产调用方能起第二趟。**今天无并发缺陷**。一处小瑕疵见 F-4 |
| A-13 | `Host.Tests` 隐患判断（读 `ChannelHostStartupTests.cs`） | **不会挂住**：`StopAsync` 先 `stopping.CancelAsync()` 再 `draining.WaitAsync(ct)`；`WaitForNextTickAsync(ct)` 抛 OCE 被 `DrainUntilStoppedAsync` 吞掉，循环即止；排空中途关停时 `AttemptAsync` 的 `catch (OCE) when (ct.IsCancellationRequested) { throw; }` 会把取消一路抛回，不会等满 2 秒超时。**但见 F-2**——`Host.Tests` 根本没走到这条路上 |

### 发现

- **F-1（阻断）本轮的交付把 TASK-03 给 `Gone` 写下的那条理由变成了假话，而「张力」小节没有注意到。**

  执行方给的理由本身，我判定**成立**：`RetryAsync` 的 `<remarks>` 说的「队列里存的是一个人，不是一个会话」
  是这两边真正的差别——发布期间手上握着一个**在 T0 解析出来的 `NotificationRecipient`（角色 + 旧连接）**，
  而队列项 `NotificationRetry` 里**根本没有连接**。这不是给不一致找借口，这是一句对的话。
  「人群在一个瞬间解析一次」也确实是 RQ-012 摘要口径的前提（`The_summary_accounts_for_every_recipient`
  钉的就是「解析出来的那些人」），发布中途对个别人重新解析会让「这次覆盖了谁」不再有单一答案。

  **问题不在这条理由，在它没去管的另一边。** `NotificationDeliveryTests.cs:230-233`
  （TASK-03 已过评审、本轮原样搬过来的那段 remarks）为 `Gone` 给出的理由是：

  > calling it a failure would put a live player into the retry queue, where re-resolving would find
  > the new connection and **deliver the older answer to it**.

  这句话的后半截**已经被本轮的交付推翻**：重试不再送「older answer」，它重新问处理器——A-4 的变异正是
  拿这件事当靶子打的（把旧帧留下来，CK-016 立刻红）。也就是说，TASK-03 当初否掉「归 Failed」用的
  那个后果，在 TASK-04 落地之后**不再存在**。

  于是记录变成两处互相拆台：`RetryAsync` 的 remarks 说「重试是全新求值，所以送新连接没问题」，
  而投递判据的 remarks 仍说「归 Failed 不行，因为重试会送旧答案」。执行方自称「必须给出理由否则就是
  不一致」，给了理由，却**没有回去看被这个理由作废的那句话**——这正是本仓被点名过的那类问题：
  一句没验过的论断，只不过这次它是从上一轮继承下来的。

  **哪边更该改：`Gone` 那边的理由，不是任何一边的行为。** 行为上两边都站得住（理由见上），
  RQ-009「收件人已不在」的分流与 RQ-010「重试重新解析」各自有效；但重登者归 `Gone` 意味着他**永远收不到**
  这条通知（`Gone` 不入队），而同一个人只要首发是 `Failed` 就还有第二次机会——同一件真实事件、相反的结果。
  这个不对称可以接受（Spec Definitions 里「尽力而为」写了通知送不到不是错误），但它**必须靠一条还成立的理由**
  撑着，而现在撑着它的那条已经倒了。

  **要求的整改（局部、在边界内）**：把 `NotificationDeliveryTests.cs` 那段 remarks 里
  「re-resolving would … deliver the older answer to it」换成本轮之后仍然为真的理由
  （可用的一条：发布是**一个瞬间的一次解析**，中途对个别人重新解析会让摘要的「覆盖了谁」失去单一答案；
  而队列项不带解析结果，所以重试没有这个问题），并在 Execution Result 的「张力」小节里如实记下
  「本轮作废了 TASK-03 的原理由」。**不要求改任何行为，也不要求改判据的断言。**

  附带（**属 TASK-03 的既有代码，不在本轮 owned diff，仅记录**）：`NotificationBus.IsStillHere` 的
  `<remarks>` 写「the message in hand was composed against the session that replaced it」，
  而 `IsStillHere` 在 `AttemptAsync` 里是**先于 `ComposeAsync`** 调的——返回 `Gone` 的那一刻手上
  **没有任何 message**，陈旧的是解析出来的连接。这句话两处都不准（「composed」和「the session that
  replaced it」），留给最终评审。

- **F-2（阻断，小）自检里那句「`Host.Tests` 起真实宿主时它会真的跑起来（队列为空、无副作用）」是没验过的推断，而且推不出来。**

  `Host.Tests` 的收尾是 `process.Kill(entireProcessTree: true)`（`ChannelHostStartupTests.cs:73-77`），
  **它从不优雅关停宿主**，所以 `StopAsync` 一次也没被执行过。更要紧的是：`StartAsync` 里
  `draining = DrainUntilStoppedAsync(stopping.Token)` 是个 async 方法，**连 `new PeriodicTimer(...)`
  抛出的异常也只会落进 `draining` 这个 Task**，而 `draining` 只在 `StopAsync` 里被 await——
  于是在 `Host.Tests` 的路径上，循环里**任何**故障都是不可见的。那 6 条绿只证明「注册它没有搞坏 DI 与启动」，
  证明不了「起来了」，更证明不了「无副作用」。

  这与 A-13 不矛盾：关停路径**读代码看是干净的**（不会挂住），但**本轮没有任何东西执行过它**。

  **要求的整改**：把自检里那句话改成它实际能支撑的范围。另外**建议**（不作为阻断条件）补上最小判据——
  Spec 的 Non-Goals 明确把这个循环划进范围（「重试队列的排空循环除外——那是本服务自己的一部分」），
  BD-007 的产出也写着「队列**与其排空**」，而 Required methods 含 TDD；两个类型都是 `internal` 且
  `Application.csproj` 对测试程序集开了 `InternalsVisibleTo`，代价很小。最小可行的形状：

  > 一个只做计数并 `TrySetResult` 的假 `INotificationRetryDrain`，`NotificationDeliveryOptions`
  > 的 `RetryInterval` 设成十几毫秒，`StartAsync` → 等那个 TCS（带宽裕超时）→ 断言至少被调过两次
  > （**两次**才说明它是循环而不是一次性）→ `StopAsync` 应迅速返回 → 记下调用次数，再等一小会儿
  > 断言次数不再增长。第二条可选：让假件第一次抛异常，断言循环没死、后续仍在调。
  >
  > 注意这条判据**不该断言墙钟周期**（那会在忙机器上假红），只断言「是循环、可停、单次失败不致命」。

- **F-3（非阻断）「`RetryLimit == 0` 根本不入队」的理由，在同一个文件里被另一条路径打脸。**

  Execution Result 写：「后者是同一个结果绕远路，而且会让队列深度不再说明任何事情。」
  可是**额度用尽的那一项走的正是「入队后下一趟丢弃」**：`RetryAsync` 的守卫在**取出之后**才判
  `Attempts >= RetryLimit`，所以 `Attempts == RetryLimit` 的项要在队里**整整躺一个周期**才被丢。
  CK-018 自己把这件事写在注释里（"The third pass finds the allowance spent and lets them go."——
  2 次重试用掉 3 趟），A-8 的轨迹也印证。于是 `Waiting` 在那一个周期里**数着一具尸体**，
  正是那句话要避免的情形。

  不阻断，因为 RQ-011「达上限后被丢弃」在行为上满足（只是晚一趟），且这是内部计数不是可观察行为。
  两条出路任选：把回队条件改成 `entry.Attempts + 1 < entry.Registration.RetryLimit`（守卫随之变成
  真正的兜底），或者把那句理由改成只说 `RetryLimit == 0` 这一种情形。**建议顺手做，别留着。**

- **F-4（非阻断，小）`DrainAsync` 取消时会静悄悄吃掉一项。**

  `cancellationToken.ThrowIfCancellationRequested()` 排在 `retries.TryTake(out var entry)` **之后**，
  所以一趟被取消时，刚出队的那一项既没被重试也没回队，直接丢了。关停时无害（队列本来就随进程消失），
  但把检查挪到循环条件里就没有这回事。记录，不要求改。

### 受理条件逐条核对

| 受理条件 | 覆盖它的判据 / 证据 | 判定 |
|---|---|---|
| 重试不缓存任何东西（连接、编码好的帧、处理器产出） | `NotificationRetry` 只有 (Registration, Notification, CharacterId, Attempts)——读代码即证；A-4 的变异证明「一旦缓存帧就红」 | 覆盖 |
| 重试之前内容变了，送出的是变化后的值 | CK-016，A-4 独立变异确认非平凡通过 | 覆盖 |
| 重试之前角色换了连接，送到新连接 | CK-017 | 覆盖 |
| 达注册声明的上限后不再重试，该项被丢弃 | CK-018；A-7 变异确认守卫是活代码；A-8 逐条对上 RQ-011 三句 | 覆盖 |
| 不做去重；队列在内存里不落库 | `ConcurrentQueue` 单字段，`Add` 无查重；无任何持久化引用 | 覆盖 |
| 全量判据通过 | A-1 独立全量 2099/2/0，七个分项逐项相符 | 覆盖 |
| Required checks CK-015..CK-020 | CK-015 = A-2；CK-016/017/018 = 判据全绿并经变异；CK-019 = A-4 + A-5；CK-020 = A-1 | 覆盖 |
| Required evidence：CK-019 变异指名全限定名与 Expected/Actual 原文并确认还原后恢复绿 | A-4 逐字相符（含 `line 40`）、A-5 md5 逐字相符、还原后 30 条全绿 | 覆盖 |
| Owned boundary / 不建检查点不提交 | 跟踪文件只 +5 行且属既有轮次；`git log` HEAD 仍是 `b05d47e4`，无新提交 | 覆盖 |

**没有发现未被判据覆盖的受理条件。** 唯一在契约受理条件之外、却被 Spec Non-Goals 明确划进范围的
是排空循环本身（F-2）。

### 结论

CK-015..CK-020 全部独立复核通过：全量 2099/2/0 七个分项逐项相符、构建 0/0、CK-019 的变异独立重做后
**恰好红 CK-016 那一条且 Expected/Actual 与所录逐字相同**、还原后两个 md5 与所录逐字相同。
排空边界与上限守卫各经一次变异证明**不是平凡通过**。提取经 TASK-03 变异 M-1 重做确认
**判据的牙齿一颗没掉**，11 条判据仍在，无一条断言被削弱。并发无缺陷，关停读代码无挂住风险。

阻断的两条都不是编码错误，都是**记录与事实对不上**：F-1 是本轮把上一轮的理由变成了假话而没人回头看；
F-2 是拿一组根本走不到那条路的判据去支撑一句「它真的跑起来了」。两条都在本 Task 的边界内、都是局部可改的。

## Controller Disposition — after Quick Review Round 1

`task-rework`。两条阻断都成立，两条非阻断一并处理。

F-1 是本轮最该记的一条，而且不是「一句没验过的论断」的老毛病，是它的变体：
**我在这一轮亲手推翻了自己上一轮写下的理由，却没有回头看那句话还在不在。**

## Execution Result — Round 2

### F-1：两份记录互相拆台

TASK-03 给 `Gone` 写的理由里有一句：归 `Failed` 不行，因为「re-resolving would **deliver the older
answer to it**」。而本轮 CK-019 的变异**证明的正是相反的事**——重试重新问处理器，送的是新答案。
那句话在写下的当天是猜的，本轮把它变成了假的，而我没回头改。

按快审的判定，**该改的是理由不是行为**。三处措辞：

| 位置 | 改成 |
|---|---|
| `NotificationDeliveryTests` 里那条重登判据的 remarks | 归 `Failed` 的问题不是「会送旧答案」，而是**什么都没失败**：会报出一次并不存在的投递失败，并据此把一个在线玩家放进重试队列。摘要要能读，而一个同时装着「socket 死了」和「他重新登录了」的桶不能读 |
| `NotificationOutcome.Gone` 的 `<remarks>` | 原文说「a message composed against the session that has just been replaced is stale by construction」。**那一刻手上根本没有 message**——在场复问在 `ComposeAsync` **之前**。改成：这次发布在一个瞬间解析出它的收件人，那次解析就是它要做的事；送给替代者等于把一个「早于那个会话存在」的帧插进它自己的入场序列中间 |
| `IsStillHere` 的 `<remarks>` | 同一处错误（「the message in hand was composed against…」）。改成：两者都不是这次发布解析出的那个收件人——这才是它们同解的原因；而**先于组装**去问，是为什么两者都不花一次处理器调用 |

### F-2：`Host.Tests` 那句自检推不出来

自检写「排空服务在真实宿主里起来了、队列为空、无副作用」。快审指出：`Host.Tests` 用
`process.Kill(entireProcessTree: true)` 收尾，**`StopAsync` 一次都没跑过**；而循环里的异常只落进
`draining` 这个从不被 await 的 Task。那 6 条绿**只证明「注册它没搞坏 DI 与启动」**。

两件事一起做：

1. **改口径**（见下方自检）。
2. **补最小判据** `NotificationRetryDrainServiceTests`（3 条）：假的 `INotificationRetryDrain` 记次数，
   interval 5ms，断言**「会再来一趟」「停得下来」「单趟抛异常不致命」**。
   **不断言周期**——那是在断言机器的心情。等待用的是 `TaskCompletionSource`，慢机器只是慢，不会假红。
3. 顺带修一处快审点出的真实隐患：`PeriodicTimer` 原本在 async 方法里 new，**间隔配错时抛的异常落进那个
   没人 await 的 Task**，服务器会安静地永不重试。改到 `StartAsync` 里同步构造——配错就在宿主启动时炸，
   有人看得见。

### F-3：入队条件

原理由（「入队后下一趟丢弃会让队列深度不再说明任何事情」）被同一个文件的另一条路径打脸——
额度用尽的项**正是**入队后躺满一个周期才被守卫丢掉，`Waiting` 那一拍数着一具尸体。

改的是代码不是话：新增 `Requeue(...)`，**入队时**就判 `used < RetryLimit`，
所以队列里永远不会有「没人会再试」的项。`RetryAsync` 开头那个 `Attempts >= RetryLimit` 守卫
随之删除——保留它会变成一个删掉也不会有判据变红的分支（TASK-02 的 N2 是同一类问题）。

`A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` 相应更新：limit=2 时
**两趟**排空之后 `Waiting` 就是 0（原来要三趟），发起总数仍是 3（1 次首发 + 2 次重试），
并新增一句「再来一趟不会有第四次尝试」。

### F-4：取消的位置

`ThrowIfCancellationRequested()` 原本排在 `TryTake` **之后**，取消一趟会把刚出队的那一项静悄悄吃掉。
改成先问再取。

### Executor Self-check — Round 2

- 生产改动：`NotificationBus.cs`（`Requeue`、删守卫、取消位置、两处注释）、
  `NotificationRetryDrainService.cs`（`PeriodicTimer` 移到 `StartAsync`）、
  `INotificationBus.cs` 的 `Gone` 注释。
- 判据改动：新增 `NotificationRetryDrainServiceTests`（3 条）；
  `A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` 按新入队规则更新；两处 remarks。
- **关于排空循环，现在能说的与不能说的**：能说「它会再来一趟、停得下来、单趟失败不致命」——有判据。
  **不能说**「它在真实宿主里按 1 秒周期跑过」——`Host.Tests` 杀进程收尾，`StopAsync` 从未执行，
  那 6 条只证明注册它没搞坏启动。这一条本轮仍无证据，如实记在这里。

### Round 2 全量判据

60 + 52 + 510 + 256 + 6 + 223（跳过 2）+ 995 = **2102 通过 / 2 跳过 / 0 失败**。
Round 1 为 2099，净 +3（排空服务的 3 条）。构建 0 警告 0 错误。未遇既知偶发失败。

## Independent Quick Review — Round 2

- Verdict: **passed**
- 评审的契约：Task Unit Contract revision 1（无修正案）。**只审 Round 2 的整改**，Round 1 已复核过的部分不重复。
- Owned diff identity（未提交的未跟踪新文件，基线 `b05d47e4`）——开审即录、七次变异后收审复核，两次逐字相同：

| 文件 | md5 | 与 Round 1 相比 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/Notifications/INotificationBus.cs` | `978fe6e65efff956fbf5f88aeb1b93e7` | 变（`Gone` 的 remarks） |
| `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs` | `f7a419d207af49c927f8a65c7beee815` | 变 |
| `Server/src/Dfo.Server.Application/Notifications/NotificationRetryDrainService.cs` | `fb46d763246e0aa59d5f271078bce77e` | 变 |
| `Server/src/Dfo.Server.Application/Notifications/NotificationRetryQueue.cs` | `eebb71c492bb6b5181ed6f1e8a085f8e` | **未变**（与 Round 1 逐字相同） |
| `Server/src/Dfo.Server.Application/Notifications/NotificationDispatchingExtensions.cs` | `7e8e8000afec95818653de3820d50a8b` | **未变** |
| `Server/tests/.../Notifications/NotificationRetryTests.cs` | `f2c32cc56abba7c66d929f727fd636b4` | 变（CK-018 更新） |
| `Server/tests/.../Notifications/NotificationRetryDrainServiceTests.cs` | `affbd31c8e30120c3c39067d23cfb868` | **新增** |
| `Server/tests/.../Notifications/NotificationDeliveryTests.cs` | `f834296e977204cb1dfd6c9b34726e53` | 变（仅 remarks） |
| `Server/tests/.../Notifications/NotificationWorld.cs` | `1943efb779e99331a5a7736d0e63764e` | **未变** |

  跟踪文件的改动仍只有 `ApplicationModule.cs`（+2）与 `Application.Tests.csproj`（+3），与 Round 1 逐字相同（`git diff` 全文已核）。
  `git log` HEAD 仍是 `b05d47e4`，无新提交、无检查点。**边界外无改动。**
- 评审后工作区已复原：七次变异（`NotificationRetryDrainService.cs` 四次、`NotificationBus.cs` 三次）与一个临时探针判据文件
  （`ZzReviewProbe.cs`，已删）之后逐字还原，上表九个 md5 与开审时**完全一致**，Notifications 目录的文件数与文件名亦一致；
  `dotnet build Server/86JP.Server.slnx --nologo` 已成功生成、**0 个警告 0 个错误**；Notifications 35 条全绿。

### 独立复核

| ID | 做了什么 | 结果 |
|---|---|---|
| B-1 | **F-1(a)** 全仓搜被证伪的那句话：`grep -rn "older answer\|composed against\|stale by construction" Server/` | 通知相关代码**一处不剩**。唯一命中是 `SelectCharacterHandler.cs:267`（讲刷新后 `prepared` 被替换，与本议题无关） |
| B-2 | **F-1(b)** 到 `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/` 核实「入场序列会从头重建状态」 | **成立，且比措辞更强**。`SelectCharacterPhaseCommands.cs:91` 的 `sessions.Select(session)` 在 `PrepareSelectedCharacterSessionCommand` 里执行，**先于任何入场帧**——所以新会话一被 `FindByCharacter` 看见，它的入场序列才刚开始；随后 `SelectedCharacterStateMessageFactory.Create` 按 A21 抓包逐帧重建（角色记录、装备、技能、任务、成就、库存、快捷键……约 59 帧），`SelectCharacterHandler` 再发完成标记与其后的尾部。「builds its state from the ground up」属实；「is the authority on it」有 `SelectCharacterDeliveryOptions` 自己的 remarks 佐证（尾部被扣住时客户端会死，因为 subtype-0x02 记录是它建自己城镇形象的依据） |
| B-3 | **F-1(b)** 换上的理由是否**在本轮之后仍为真** | 是。它讲的是**发布中途**那一刻，与「重试重新问处理器」不冲突——本轮的变异（B-19）打的是重试路径，打不到这句。且「a publish that began before that session existed」是构造性为真：人群解析与 `IsStillHere` 读的是同一个 `ISelectedSessionDomainService`，连接不同即意味着会话是在 T0 之后被换的 |
| B-4 | **F-1(c)** 有没有换上过强表述 | 有两处轻微，均不阻断，见 N-1、N-2 |
| B-5 | **F-2(a)** 变异一：`DrainUntilStoppedAsync` 的 `while (await interval.WaitForNextTickAsync(...))` 改成 `if (...)`（只跑一趟） | **恰好红两条**：`NotificationRetryDrainServiceTests.The_loop_comes_round_again_rather_than_draining_once` 与 `.A_pass_that_throws_does_not_end_the_loop`，均报 `System.TimeoutException : The operation has timed out.`，落点 `NotificationRetryDrainServiceTests.cs:line 87`。`Stopping_ends_the_loop` 仍绿（正确——只跑一趟也停得下来）。33 通过 / 2 失败 |
| B-6 | **F-2(a)** 变异二：`DrainOnceAsync` 的 `catch (Exception error)` 里加 `throw;`（异常终止循环；直接删 catch 会因 CS9113「参数 logger 未读」编译失败，故改成记完日志再抛，效果等价） | **恰好红一条**：`.A_pass_that_throws_does_not_end_the_loop`，`System.TimeoutException`。34 通过 / 1 失败 |
| B-7 | **F-2(a)** 变异三（自补，为验第三条判据）：`StopAsync` 改成 `=> Task.CompletedTask`（不取消、不等待） | **恰好红一条**：`.Stopping_ends_the_loop`，`Assert.Equal() Failure: Values differ / Expected: 1 / Actual: 8`。三条判据各钉一件事，无一条平凡通过 |
| B-8 | **F-2(b)** 满载并行下的稳定性（读代码 + 上述多次运行的实测） | **判定稳定**。理由：一，等待用 `TaskCompletionSource` 加 10 秒超时，5ms 周期在慢机器上只是慢；二，`wanted` 在 `StartAsync` 之后才写，若此时循环已跑过若干趟，`ReachedAsync` 的 `if (Passes >= passes) return;` 直接返回；三，即便 `wanted` 与 `Passes` 无同步导致一次唤醒丢失，**每一趟都会重新判 `Passes >= wanted`**，最多推迟一个 5ms 周期，设计是自愈的；四，`Stopping_ends_the_loop` 的读数发生在 `StopAsync` 之后，而 `StopAsync` 会 `await draining`，所以计数**已定**，`Task.Delay(50)` 之后的比较是确定性的——机器再忙也只会假绿，不会假红 |
| B-9 | **F-2(c)** 自检口径与事实是否相符 | **相符**。`ChannelHostStartupTests.cs:75` 与 `:145` 均为 `process.Kill(entireProcessTree: true)`，`StopAsync` 确实一次未执行；「不能说它在真实宿主里按 1 秒周期跑过」这句是准确的 |
| B-10 | **F-2(d)** `PeriodicTimer` 移到 `StartAsync` 之后，配错间隔是否真在宿主启动时抛出——**自己验一次**（临时判据文件 `ZzReviewProbe.cs`，验完即删） | **是**。`RetryInterval = TimeSpan.Zero` 与 `TimeSpan.FromMilliseconds(-5)` 都从 `NotificationRetryDrainService.StartAsync` **同步抛** `ArgumentOutOfRangeException`；用 `Host.CreateApplicationBuilder()` 起真实宿主、`RetryInterval = TimeSpan.Zero` 时，`host.StartAsync()` 也抛出同一个异常。（`TimeSpan.FromMilliseconds(-1)` 不抛——那是 `Timeout.InfiniteTimeSpan`，是合法值，判据世界正是用它把循环关掉的，不算配错。）**反证**：把 `PeriodicTimer` 挪回 async 方法里（Round 1 的形状），同样三个探针全部变成「无异常」——服务器会安安静静地起来并永不重试。这处修的是真隐患 |
| B-11 | **F-3(a)** 删掉 `Attempts >= RetryLimit` 守卫后，是否还有路径能让额度用尽的项被再试（推演加全仓核） | **没有**。`retries.Add(...)` 全仓**只有一个调用点**（`NotificationBus.cs:114`，在 `Requeue` 里），`NotificationRetryQueue` 是 `internal` 且只注入给 `NotificationBus`；`Requeue` 只有两个调用方（`PublishAsync` 传 `used: 0`，`RetryAsync` 传 `entry.Attempts + 1`）。`RetryLimit` 来自单例 `NotificationHandlerRegistration`，注册时 `ThrowIfNegative` 且此后不变。故队列里的项恒满足 `Attempts < RetryLimit`，取出后那一次重试是第 `Attempts+1` 次、恒不超过 `RetryLimit`。守卫确属冗余，删得对 |
| B-12 | **F-3(a)** 上限变异：`Requeue` 的 `used < registration.RetryLimit` 改成 `used <= registration.RetryLimit` | **红两条**，且都是该红的：`A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit`（`Expected: 0 / Actual: 1`，即第 88 行「两趟之后 Waiting 为 0」）与 `A_notification_that_declares_no_retries_queues_nobody`（`Expected: 0 / Actual: 1`）。上限这条线**被两条判据从两侧钉住**，差一个都会红 |
| B-13 | **F-3(b)** 更新后的 CK-018 与 Spec RQ-011 / AC-011 逐句对照 | **一致**。limit=2 的实际轨迹：首发失败到 `Requeue(used:0)`，`0<2` 入队；第 1 趟重试一失败到 `Requeue(used:1)`，`1<2` 回队（判据断言 `Waiting == 1`）；第 2 趟重试二失败到 `Requeue(used:2)`，`2<2` 不成立即丢弃（断言 `Waiting == 0`）；发起总数「1 首发 + 2 重试 = 3」（断言 `Attempted.Count == 3`）；再来一趟不增加（断言仍为 3）。对 RQ-011 三句：上限来自 `AddNotificationHandler` 的 `retryLimit`（注册声明）成立；`NotificationRetry.Attempts` 记自己已用的重试次数成立；达上限即丢弃成立。比 Round 1 的三趟更紧——队列里不再躺尸体，Round 1 的 F-3 是被行为消除而不是被措辞掩盖 |
| B-14 | **F-4** 取消的位置 | `DrainAsync` 现为 `for (pending = Count; ...) { ThrowIfCancellationRequested(); if (!TryTake) break; await RetryAsync(...); }`。**先问再取**属实。**没有引入新问题**：取消时抛在出队之前，队列原样保留，没有任何一项被吃掉；被中止的那一趟里剩下的项留在队里等下一趟（进程真要停时队列本就随之消失）。唯一无害的不对称是队列为空时 `pending == 0`、循环体不执行，于是一次已取消的排空会「正常返回」 |
| B-15 | **计数核对**：自行全量 `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo`，跑了 **4 次** | Pvf 60 / Networking 52 / Protocol 510 / Persistence 256 / Host 6 / Infrastructure 223（跳过 2）/ Application 995 = **2102 通过 / 2 跳过 / 0 失败**，与执行方所录**七个分项与合计逐项一致**；较 Round 1 的 2099 净 +3，且 +3 全在 Application.Tests（992 到 995），恰为 `NotificationRetryDrainServiceTests` 的 3 条 `[Fact]`；其余六个分项与 Round 1 逐项相同。4 次里 3 次全绿；1 次见 N-6 |
| B-16 | **回归**：`INotificationBus.cs` 是否只动了注释 | **是**。剥掉全部 `///` 与 `//` 之后的代码骨架为：`NotificationOutcome` 四个成员 `Delivered / Declined / Gone / Failed`（顺序未变）、`NotificationAttempt` 记录、`NotificationDelivery` 及其 `With`、`INotificationBus.PublishAsync` 的签名与 `where TNotification : class, INotification<TNotification>` 约束——与 TASK-03 快审 T-14 所录的声明面**逐项相同**。改动只在 `Gone` 的 `<remarks>` |
| B-17 | **回归**：`NotificationDeliveryTests.cs` 是否只动了注释 | **是**。11 条 `[Fact]`、29 个 `Assert.`，与 Round 1 快审 A-9 所录**逐字相同**；11 个用例名一字未改；29 行断言逐行读过，与 TASK-03 三轮记录（G-1 的 `< 1s` 上界、S-3 的重登三断言、F-6(b)/S-5 的 `Attempted`、G-2 的 `A_presence_lookup_that_throws…`、`The_summary_accounts_for_every_recipient`）逐条对上，无一条被削弱或删除。改动只在 `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed` 的 `<remarks>` |
| B-18 | **回归的强证据**：重做 TASK-03 快审的变异 M-1（`IsStillHere` 去掉连接比较，只判 `FindByCharacter(…) is not null`） | **恰好**红 `Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed`，报 `Assert.Equal() Failure: Collections differ / Expected: [7002] / Actual: []`——与 TASK-03 快审 Round 2 的 S-3、TASK-04 快审 Round 1 的 A-10 所录**逐字相同**。本轮改注释之后这条判据的牙齿**一颗没掉** |
| B-19 | **CK-019 在 Round 2 的实现上是否仍咬得住**（Round 2 动了重试路径却未重跑 CK-019，故自行重做）：在 `AttemptAsync` 里按角色缓存处理器产出并在重试时复用——即「把处理器产出的消息留下来」这个自然而错误的写法 | **恰好红一条**：`Dfo.Server.Application.Tests.Notifications.NotificationRetryTests.A_retry_asks_the_handler_again_rather_than_resending_what_it_had`，`Assert.Equal() Failure: Collections differ / Expected: ["second answer"] / Actual: ["first answer"]`，落点 `NotificationRetryTests.cs:line 40`——与 Round 1 执行方所录、Round 1 快审 A-4 所复现的**逐字相同（含 line 40）**。其余 34 条全绿。Required evidence 在新实现上依然成立 |

### 发现

两条阻断（F-1、F-2）与两条非阻断（F-3、F-4）**全部整改到位**，且都经变异确认不是平凡整改。以下六条均**不阻断**，记录备最终评审。

- **N-1（非阻断）同一处措辞漏扫了第四个位置。**

  执行方 Round 2 的表格写「`IsStillHere` 的 `<remarks>`：**同一处错误**（「the message in hand was composed against…」）」并改掉了它，也改掉了 `Gone` 与重登判据。但 `NotificationRetryTests.cs:52-53` 里那句交叉引用**还是老说法**：

  > mid-publish the session was replaced underneath **a message already worked out for the old one**

  `IsStillHere` 在 `AttemptAsync` 里是**先于 `ComposeAsync`** 调的（`NotificationBus.cs:176` 对 `:179`），返回 `Gone` 的那一刻**没有任何 message 被组装出来**——被「worked out for the old one」的是解析出来的收件人（角色加旧连接），不是消息。同一文件所引的 `RetryAsync` 自己已经改准了（「**what is in hand** was worked out against the session that just went away」），只有这条判据的注释还停在旧稿。

  不阻断：两处**权威**理由（`NotificationOutcome.Gone` 与 `IsStillHere`）现在都是对的，这只是一句转述，且不承载任何判定；行为与断言均无关。建议顺手把「a message already worked out for the old one」换成与 `RetryAsync` 一致的说法（「what was in hand -- the recipient this publish resolved -- was worked out against the session that just went away」）。

- **N-2（非阻断）`Gone` 的新理由里有一句说得比能证的更满。**

  > Delivering to the replacement instead would put a frame ... **into the middle of its entry sequence**

  B-2 查明新会话是在 `PrepareSelectedCharacterSessionCommand` 里、**入场序列开始之前**就被 `sessions.Select` 登记的，所以「撞进入场序列中间」在绝大多数情况下成立；但它是**竞态而非必然**——一次很长的发布（每人 2 秒超时乘以多人）完全可能在替代者跑完入场之后才轮到那个人。同一段落往下两句其实已经把话说圆了（「racing a fresh entry sequence with an older frame is the worse of the two failures」——用的是 racing），所以这只是同一段里前紧后松，不是没验过的论断。记录，不要求改。

- **N-3（非阻断）本轮唯一改变生产行为的那处修复，自己没有判据钉住。**

  `PeriodicTimer` 从 async 方法挪到 `StartAsync`，是 Round 2 对真实隐患的修复（B-10 证明它是真的：挪回去，配错间隔就一声不吭）。但 B-10 的探针是我临时加的、验完即删；**留在仓里的三条排空判据在挪回去之后全部照绿**。也就是说这处修复目前只靠那段注释守着。不阻断——Round 1 并未要求这处修改，而且改对了；但记录下来，免得日后有人「顺手」把 `new PeriodicTimer` 移回循环里而没有任何东西变红。

- **N-4（非阻断）F-4 的取消位置同样无判据。**

  把 `ThrowIfCancellationRequested()` 挪到 `TryTake` 之前是严格的改进（B-14），但挪回去不会有任何判据变红。Round 1 原本写的就是「记录，不要求改」，执行方改了却没补判据。同 N-3，记录即可。

- **N-5（非阻断）新判据的失败形状是超时而不是数值。**

  `The_loop_comes_round_again_rather_than_draining_once` 与 `A_pass_that_throws_does_not_end_the_loop` 坏掉时报的是 `System.TimeoutException`（各 10 秒），既不给出「跑了几趟」也不快。这是「不断言周期」换来的代价，判定**可接受**（B-8 已论证它只会假绿不会假红）；若想更好读，可在超时后断言 `Passes` 的实际值，让报文自己说「只跑了 1 趟」。

- **N-6（非阻断，如实记录）四次全量里有一次出现 1 条 Networking 偶发失败，其身份未被我捕获。**

  第 2 次全量运行报 `Dfo.Server.Networking.Tests` **51 通过 / 1 失败**（其余六个分项全绿）。它**不匹配**既知偶发失败的判别依据（那一条是 `ObjectDisposedException: 'SQLitePCL.sqlite3'`、栈根 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 到 `SqliteCommand.ExecuteScalar`，属 Persistence）。我的输出过滤把明细行滤掉了，**没能录下用例名与异常**——这是本轮取证的一个缺口，如实写在这里。此后单独重跑 `Dfo.Server.Networking.Tests` **8 次**、再跑 2 次全量（其中一次输出完整落盘）均未复现，`grep "\[FAIL\]"` 无命中。

  归因判断：`Dfo.Server.Networking` **不引用** `Dfo.Server.Application`，本 Task 的 owned diff 全在 Application 与 Abstractions.Notifications，改不到它；只在满载并行下出现一次，形态像时序偶发。**不归因于本轮交付**，但既然不属既知偶发的判别依据，留给最终评审决定要不要立案。

### 受理条件逐条核对（Round 2 后）

| 受理条件 | 覆盖它的判据 / 证据 | 判定 |
|---|---|---|
| 重试**不缓存任何东西**（连接、编码好的帧、处理器产出） | `NotificationRetry` 仍只有 (Registration, Notification, CharacterId, Attempts)——`NotificationRetryQueue.cs` 与 Round 1 逐字相同；CK-016 加 **B-19 在新实现上重做的缓存变异**（缓存处理器产出立刻红，且 Expected/Actual 与所录逐字相同） | 覆盖 |
| 重试之前内容变了，送出的是变化后的值 | CK-016，B-19 确认非平凡通过 | 覆盖 |
| 重试之前角色换了连接，送到新连接 | CK-017（判据体本轮未改） | 覆盖 |
| 达注册声明的上限后不再重试，该项被丢弃 | 更新后的 CK-018（两趟后 `Waiting` 为 0、发起 3、再一趟不增）；B-12 两侧变异；B-11 证明删守卫后无旁路；B-13 对上 RQ-011/AC-011 | 覆盖 |
| 不做去重；队列在内存里不落库 | `ConcurrentQueue` 单字段，`Add` 无查重，无任何持久化引用（文件未变） | 覆盖 |
| 全量判据通过 | B-15 独立全量 4 次，2102/2/0，七个分项逐项相符 | 覆盖 |
| Required checks CK-015..CK-020 | CK-015 等于自行 build 0 警告 0 错误；CK-016/017/018 全绿并各经变异；CK-019 等于 **B-19 在 Round 2 实现上重做**；CK-020 等于 B-15 | 覆盖 |
| Required evidence：CK-019 变异指名全限定名与 Expected/Actual 原文并确认还原后恢复绿 | B-19 逐字相符（含 `line 40`）；还原后九个 md5 逐字相同、Notifications 35 条全绿 | 覆盖 |
| Owned boundary / 不建检查点不提交 | `git diff` 全文核过，跟踪文件仍只 +5 行且属既有轮次；HEAD 仍 `b05d47e4` | 覆盖 |

**没有发现未被判据覆盖的受理条件。** Round 1 点出的那个「在受理条件之外、却被 Spec Non-Goals 划进范围」的缺口——排空循环本身——本轮已由 `NotificationRetryDrainServiceTests` 三条补上，并经 B-5/B-6/B-7 三次变异确认各自钉住一件事。仍未被任何判据钉住的只剩两处**本轮新做的改进**（N-3 的 `PeriodicTimer` 位置、N-4 的取消位置），二者都不是受理条件，且都是把行为往对的方向改。

### 结论

两条阻断均**实质整改**，不是换个说法了事。

F-1 上，被证伪的那句话已全仓消失，换上的理由我到 `SelectCharacter` 亲自核过——新会话在入场帧发出**之前**就被登记，随后约 59 帧从头重建状态，所以「把一个早于那个会话存在的帧插进它的入场序列」这个理由**在本轮之后仍然为真**，而且比措辞更硬。只剩一句转述漏扫（N-1）与一处前紧后松（N-2），都不承载判定。

F-2 上，三条新判据各经一次变异证明不是平凡通过（只跑一趟红两条；异常终止循环红第三条；`StopAsync` 空转红「停得下来」），并发稳定性判定为只会假绿不会假红；改后的自检口径与 `process.Kill` 的事实相符；`PeriodicTimer` 的位置我正反各验一次——现在配错就在宿主启动时抛 `ArgumentOutOfRangeException`，挪回去则一声不吭。

F-3 改的是代码不是话：入队即判额度，队列里不再躺着没人会试的项，守卫删得干净（`retries.Add` 全仓一个调用点），上限被两条判据从两侧钉住。F-4 先问再取，无项被吃。

全量 2102/2/0 四次自跑、七个分项逐项相符；两处 TASK-03 产物的改动经剥注释比对与变异重做确认**只动了注释**，声明面与 29 条断言一字未改。

## Controller Disposition — after Quick Review Round 2

`passed`。六条非阻断里四条就地处理，两条如实留档。

### N-1：我上一轮只扫了三处，第四处漏了

同一句已被证伪的措辞（「a message already worked out for the old one」）还留在
`NotificationRetryTests.cs:52-53`。这就是「改了三处、以为改完了」——而本成果反复返工的原因正是
没有把一句话扫干净。已改，并复扫全仓确认零残留：

```
grep -rn "worked out for the old one\|composed against\|older answer\|into the middle of its entry" \
    Server/src Server/tests --include=*.cs   → 无输出
```

### N-3 / N-4：两处改了生产行为却没判据的地方

快审点名：`PeriodicTimer` 挪到 `StartAsync` 是**本轮唯一改变生产行为的修复，而它自己没有判据**——
挪回去三条排空判据全绿。取消位置同理。两条都补上：

- `An_interval_that_cannot_tick_is_refused_at_startup`（`[Theory]`，0ms 与 -5ms）：
  断言 `StartAsync` 抛 `ArgumentOutOfRangeException`。快审已验过挪回 async 方法则**一声不吭**，
  所以这条判据钉住的正是那个「服务器安静地永不重试」的隐患。
- `A_pass_called_off_leaves_what_it_had_not_reached`：取消一趟排空，断言待重试数仍为 1、
  发起次数仍为 1。取消排在 `TryTake` 之后时这一条会红。

### N-2：措辞再收一档

`Gone` 里「into the middle of its entry sequence」比能证的满一点——它是竞态。改成 `race … against`，
与同段下文一致。

### N-5：留档不处理

排空判据坏掉时报 10 秒 `TimeoutException` 而不是数值差异。可读性差一档，但改成轮询断言会引入抖动，
不划算。如实记在这里。

### N-6：一次没记下身份的 Networking 偶发——取证缺口，如实留档

快审 4 次全量里有 1 次 `Dfo.Server.Networking.Tests` 报 51/52。**它不匹配既知偶发的判别依据**
（那条是 `ObjectDisposedException: 'SQLitePCL.sqlite3'` + `CharacterDatabaseSchema.KeepWritersAndReadersApart`
栈根，属 Persistence/SQLite）。快审的输出过滤把明细滤掉了，**用例名没录下来**——这是取证缺口，不是结论。

此后 8 次单跑 + 2 次全量均未复现。不归因本轮交付的依据：`Dfo.Server.Networking` 不引用
`Dfo.Server.Application`，本成果的改动进不了那个程序集。

**这是本仓第二次遇到未记名的 Networking 并行偶发**（上一次记在 spec-001 的根图里）。
下次再遇到务必先记用例名与栈根，再谈别的。已列入 TASK-07 要写进仓库约定的事项。

## Execution Result — Round 3

见上。生产改动仅 `INotificationBus.cs` 一处措辞（N-2）；`NotificationRetryDrainService.cs` 与
`NotificationBus.cs` 本轮**零改动**——N-3/N-4 补的是判据，不是实现。
新增判据 3 条（`[Theory]` 两例 + 取消一条）。

### Round 3 全量判据

52 + 510 + 60 + 256 + 6 + 223（跳过 2）+ 998 = **2105 通过 / 2 跳过 / 0 失败**。
Round 2 为 2102，净 +3（`[Theory]` 两例 + 取消一条）。构建 0 警告 0 错误。
`Networking.Tests` 本次 52/52。未遇既知偶发失败。

登记表置 TASK-04 为 `done`，`tasks.active` 前移至 TASK-05。
**N-1/N-2/N-3/N-4 这四处改动发生在快审 passed 之后，须在最终完整变更评审里一并复核。**
