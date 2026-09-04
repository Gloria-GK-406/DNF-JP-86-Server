---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 通知知道自己要告诉谁

## Delivered

服务器有了一个统一的方式回答「把这件事告诉这群人」。调用方说出人群与通知，剩下的事由一个地方负责：
把人群解析成收件人、逐个取得该收件人的消息、投递、隔离失败、交回一份可核对的摘要。

- **人群是一个值，以角色表达**。`NotificationAudience` 是封闭层级（第一版只有 `TheseCharacters`）；
  「这个角色此刻在哪个连接」只由通知服务回答。
- **通知类型自带它的处理器契约**。`INotification<TSelf>` 自指，所以发布调用点的类型由实参定死。
- **处理器只读**，按收件人产出消息，返回空即弃权；机检守着注入面。
- **注册 + 启动校验**：漏注册在开始接受连接之前让服务器起不来。
- **逐个投递、每收件人期限、失败隔离**，摘要四种归类（成功 / 弃权 / 人不在了 / 失败）。
- **失败重试**：内存队列，重试**重新解析、重新问处理器**，上限按通知在注册时声明。
- **两处手写扇出改经此服务**：PvP 大厅公示、会话清理的「尽力送旁人」段。
  **两处所在的领域服务都不再为投递查连接。**
- **仓库约定**记下 `CMD ⇒ Notify`、没做的部分、以及判据偶发失败的判别规则。

判据：基线 2069 通过 / 2 跳过 / 0 失败 → **2126 通过 / 2 跳过 / 0 失败**，构建 0 警告 0 错误。
净 +57 全部为新增判据，**既有断言无一被削弱或删除**（曾有一条被删，已补回且更强）。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | 验证者自建探针工程：正对照通过；`PublishAsync<INotification<Foo>>` → CS0311、`AddNotificationHandler<Foo, BarHandler>` → CS0311、`PublishAsync<Foo>(…, new Bar())` → CS1503 |
| SC-2 | 全仓 `FindByCharacter` 生产命中 7 行逐处判定：为投递取连接的只有通知服务两处；另两处只取会话标识当运行态目录的键、从不读 `ConnectionId` |
| SC-3 | `NotificationDeliveryTests` 的按人取内容与弃权两条 |
| SC-4 | `NotificationHandlerArchitectureTests`（14 条），规则比命令处理器那张表更严（不含 `DomainStore`，豁免字典为空） |
| SC-5 | `A_recipient_whose_delivery_fails_does_not_stop_the_others` |
| SC-6 | `Someone_who_left_is_told_apart_from_someone_whose_delivery_failed` |
| SC-7 | `A_retry_asks_the_handler_again_rather_than_resending_what_it_had`（CK-019 变异证明：改成重发缓存帧时恰好红它，Expected `["second answer"]` / Actual `["first answer"]`）、`A_retry_goes_to_the_connection_they_are_on_now`、`A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit` |
| SC-8 | `NotificationDelivery` 四种归类 + `The_summary_accounts_for_every_recipient` |
| SC-9 | 迁移了**两处**扇出；`SelectCharacterSessionEntryService` 与 `SelectedSessionCleanupService` 的投递用连接查找均归零 |
| SC-10 | **revision 2 口径**：帧逐值不变（两处迁移追到同一构造表达式，`ToMessage` 逐字符相同）；收件人集合与投递语义共 13 条差异，验证者逐条回到声称位置核过 |
| SC-11 | `CommandHandlerArchitectureTests.cs` 对基线 **blob hash 相同**（`411de89a…`）；该类 12 条全绿 |
| SC-12 | `AGENTS.md` 新增两节，每个数字验证者实测复现（37 / 7、六个投递点四个在 catch、三条栈根、14 条） |

## Repository Result

- Changed paths：已跟踪 15 个文件（+388 / −111），另新增
  `Server/src/Dfo.Server.Abstractions/Notifications/`、`Server/src/Dfo.Server.Application/Notifications/`、
  `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/PvpParticipantPublishedNotification.cs`、
  `Server/tests/Dfo.Server.Application.Tests/Notifications/`、
  `Server/tests/Dfo.Server.Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs`
- Baseline：`b05d47e4e587d6ab85312ef7f5a3802716805e3c`
- Parent rollback required：no（standalone Spec）

## Successor Guidance

**必须由所有者裁定、本成果不代裁的四件：**

1. **TASK-06 的 `Permitted files` 事后扩张仍未获认可。** 它改的是**实现**
   （`SessionEntryModels.cs` 删 `SelectedSessionCleanupNotificationTarget`、
   `BestEffortTargets` → `PeerAudience` + `PeerNotices`；`ProtocolModule.cs` 加接线），
   **不在所有者那条「跨界修记载」规矩的射程内**。重裁时请看实际 diff，
   不要看我当初的提问——那次提问把它错描述成「一个错数被抄进四个文件」。
2. 那条规矩自陈的代价：**边界对「修记载」这类改动不再有硬约束力**，补记修正案是剩下的全部约束。
3. **SC-10 今天真正靠的是独立评审，不是任何自动化判据。** 它的必需证据是一份由被判定方自产的清单，
   而该清单在五轮里分别缺 3 / 2 / 1 / 1 / 0 条，每次都是换一个独立上下文重读代码才发现的。
4. **通知投递失败在生产里没有出口。** 范围比「新增退化」窄——旧路径旁人失败本来就是
   `catch { break; }` 无日志，属平移；真正新增的无声只有旁人半边的映射异常（C-D8），
   而它今天不可达且有判据钉着。未被抵消的是 `Failed` / `Gone` / 达上限丢弃在生产里无人读。
   **修还是记债，由所有者定。**

**后继者必须保留的事实：**

- **人群与连接互斥**：要让「收件人集合逐值不变」成立，人群就必须携带会话；而 RQ-004 要的正是
  把连接从人群里拿掉。这一对互斥是 SC-10 被下调的全部原因，不是实现没做好。
- **`retryLimit` 按通知声明，且失败不只是抛异常**——每收件人期限到点也算失败，而那时帧可能已在传输里，
  所以重试可能是重复而不是补送。PvP 公示声明 1、清理通告声明 0，各自的理由在注释里。
- **判据偶发失败的判别规则已写进 `AGENTS.md`，但它至今零现场检验**：本成果最后七轮全量判据全绿。
  三条栈根的表、「用例名不是判别依据」「取不到身份一律是取证缺口」三句都还没被现场用过。
- **周期任务是下一个成果**，本成果不含任何调度器。唯一的循环是重试队列排空，它不驱动业务。

## Artifacts

- 评审：`reviews/change-review.md`（verdict `approved`）
- 验证：`verification/completion-evidence.md`（decision `pass`）
- 七个 Task 与 17 轮独立快审：`tasks/TASK-01..07-*.md`
- Spec 的三条更正：`spec.md` 的 Corrections（C-1 / C-2 / C-3）
- 契约修正案：`TaskContract.md` 的 Amendments（revision 2）
- 路由决策的事后批注：`routing-decision.md` 的 Post-hoc Annotation

## Post-verification Change — 所有者裁定后加的日志

验证通过（`pass`）之后，所有者对交回的两件作了裁定：

1. **TASK-06 的 `Permitted files` 事后扩张 —— 认可。** 悬置解除。
2. **可观测性 —— 现在就修：给总线加日志。**

### 第 2 件的落地

`NotificationBus` 注入 `ILogger<NotificationBus>`，在**还拿得到原因的地方**记：

- 投递抛异常 → `Warning` + 异常本身（记在 `AttemptAsync` 的 catch 里，因为往外走的只剩一个词）；
- 到了每收件人期限 → `Warning`，**与抛异常分开说**（那时手上没有异常）；
- 重试额度用尽、彻底放弃某收件人 → `Warning`（`Requeue`）。

**弃权与「人不在了」不记**——它们不是出了问题。

**一处实现里被判据当场纠正的**：初版对 `retryLimit: 0` 的通知也记「gave up after 0 of 0」，
于是一次失败被记了两条。`A_failed_delivery_is_said_out_loud` 报
`Assert.Single() Failure: The collection contained 2 matching items`——**是实现的问题不是判据的**。
改成只在 `RetryLimit > 0` 时记：声明了 0 次重试的通知从来就没有第二次机会可放弃，
失败那一条已经说过了。

新增判据 3 条（失败被说出来、超时与失败分开说、放弃被说出来），
判据 **2126 → 2129 通过 / 2 跳过 / 0 失败**。

`AGENTS.md` 里那条「已知限制：通知投递失败今天是无声的」**已经变成假的**，随之改写为实际行为。

**这三项改动发生在评审与验证通过之后**，须重跑评审与验证。

### 增量复评之后又修的一处真缺陷（N-11）

增量复评给了 `approved`，但记了一条 non-blocking：超时与异常的分叉用的是
`deadline.IsCancellationRequested`——**只看时钟**。于是一个**忽略取消令牌、过了期限才抛真异常**的
处理器会被记成「超时」，而**异常被丢掉**。

窄，但它丢的是排查时唯一有用的那样东西。已改为
`error is OperationCanceledException && deadline.IsCancellationRequested`，
两个条件缺一不可，并在注释里写明为什么。

新增判据 `Something_thrown_after_the_deadline_is_still_reported_as_what_it_was`：
处理器 `Thread.Sleep(80)` 之后抛 `InvalidOperationException`，期限 30ms，
断言记下来的 `Error` 是那个异常而不是 null。变异复核：

```
变异  分叉退回 if (deadline.IsCancellationRequested)
→ 失败 Something_thrown_after_the_deadline_is_still_reported_as_what_it_was（54 条中恰好红这一条）
还原后 → 54 条全绿；NotificationBus.cs md5 39b28a62... 与变异前一致
```

**遮住这个缺陷的是我写的一句话**（复评的 N-10）：`AGENTS.md` 里我写超时那条「那时手上没有异常」——
可 `error` 就在作用域里，代码是**选择**不记它。那句话让人以为分叉是被异常类型决定的，
而实际上当时只看时钟。**一句不准的解释，遮住了它解释的那段代码的缺陷。** 措辞已改。

另按复评 N-13：给 TASK-06 的 C-D8 加了**前向注记**（不回改历史轮次记录）——
那条里「总线没有 logger」「没有任何地方会说出来」今天已是假的。

判据 **2129 → 2130 通过 / 2 跳过 / 0 失败**（逐项目相加，不用会数错行的 `grep -c`）。

### 验证发现的三件，及其处置（N-15 / N-16 / N-17）

**N-16 —— 那条新规则的第一次现场检验，而且它按设计工作了。**

验证者跑了六趟全量，红了一趟：`The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`，
两个 50ms 期限跑了 **1045ms**，只超我设的 1 秒上界 4.5%。

它按 `AGENTS.md` 那张表判了归属：**异常类型不匹配**（xunit 断言失败，不是
`ObjectDisposedException: 'SQLitePCL.sqlite3'`）、**栈根不在表内**。所以**拒绝**把它塞进已知那一条，
判为「本成果自己的一条此前未记录的时钟偶发」。

**这是那条规则至今唯一的一次现场检验，结果是它挡住了一次错误归类。**

处置：上界 1 秒 → **2.5 秒**，并把「先前那个数是怎么测错的」写进注释
（首测 197–315ms、看起来是三倍余量，实际不是）。

**这次放宽是真的削弱，必须说清楚**：这条判据要挡的是「把生产默认值抄成常数」——
两个收件人 × 默认 2 秒 = 4 秒，2.5 秒的上界仍然挡得住。但硬编码常数的存活窗口
**从每收件人 [50ms, 500ms) 放宽到了 [50ms, 1.25s)**。换来的是它不再假红。

**N-15 —— 分叉的时钟那一半没有判据咬住。** 验证者变异证明：只留异常类型、丢掉时钟，
**56 条全绿**。补 `A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason`：
处理器在期限还很宽裕时自己抛 `OperationCanceledException`，断言它被记成**带原因的失败**
而不是「超时」。变异复核：

```
变异  if (error is OperationCanceledException)   // 丢掉时钟
→ 失败 A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason（55 条中恰好红这一条）
还原后 → 55 条全绿；NotificationBus.cs md5 39b28a62... 与变异前一致
```

**两个条件现在各有一条判据夹住。**

**N-17 —— 前向注记没覆盖全。** TASK-06 里另有五处写着「无声」。已在文末加一节**统一前向注记**，
并在三处显眼位置就地标「已过时」，**不回改上文**——那些句子当时是对的，而且正是它们促成了这次修复。

判据 **2130 → 2131 通过 / 2 跳过 / 0 失败**，连续三趟同值、全绿。
生产代码在这三件处置里**逐字节未改**（`NotificationBus.cs` md5 与 N-11 修复后相同）。

### 收口验证的三条，及其处置（N-18 / N-19 / N-20）

**N-18 —— 一个反讽值得记下来。**

我先前学到的教训是「**列行号而不是列个数**」（行号可以当场核）。于是统一前向注记里我列了行号——
可 TASK-06 是个**只会追加的文件**，我自己在上面插了一行，清单里三个行号就全差了 1，还漏掉三处。

**在会被插入行的文件里，行号比个数更脆。** 已改为**按内容索引**：
「本文件里每一处『无声』及其同义说法，一处不例外」，并把这个反讽本身写进注记。

**N-19 / N-20 —— 那条挂钟判据的走廊已经很窄，把账写在判据自己身上。**

收口验证实测：本机上限约 1 秒，新上界 2.5 秒，必须挡住的生产默认落点 4 秒——
**余量约 2.4 倍，离失去鉴别力只有 1.6 倍**。而且它第 1 趟（冷启动）就报了 1 秒，
**在旧的 1 秒上界下本来就会再红**——这独立坐实了放宽是必要的，不是我在给自己找台阶。

已把完整的账写进判据自己的 `<remarks>`（此前只写在 `spec-result.md` 里）：
放宽的代价（存活窗口 500ms → 1.25s/收件人）、走廊有多窄、以及**下一步该怎么办**：

> 再红一次的答案不是更大的上界，是**别再读挂钟**，换一种方式断言那个期限。

判据 **2131 通过 / 2 跳过 / 0 失败**，本轮未增删判据条数。生产代码逐字节未改。
