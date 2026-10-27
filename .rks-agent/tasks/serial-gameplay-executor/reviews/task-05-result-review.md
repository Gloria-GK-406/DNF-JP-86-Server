---
target: tasks/TASK-05.md
verdict: approved
---

> 文件级 verdict 是最新一轮（Round 2）的结论。第 1 轮的 `changes_required` 保留在下文作为记录。

# TASK-05 Result 独立快速审查

```yaml
kind: rks-review
verdict: changes_required
```

- 审查对象：`tasks/TASK-05.md`（Contract Revision 1；Result 第 1 轮，未提交）；`git status --short` 中全部未跟踪文件：`Abstractions/Execution/GameplayJobSlot.cs`、`GameplayOutbound.cs`，`Application/Execution/Outbox/GameplayOutbox.cs`，`Networking/Outbound/` 4 个文件，两个测试目录（5 个文件），`verification/logs/task-05-*` 4 份日志，`verification/measurement/selection-outbound-bound.{md,tsv}`。HEAD `5a52a13a`。
- 被审文件 sha1：`OutboundSender.cs` = `898bbe52…`，`GameplayOutbox.cs` = `8a86390e…`，`OutboundTransport.cs` = `2c281e5c…`，`OutboundLimits.cs` = `27371eb9…`，`OutboundEncoder.cs` = `7268d544…`；`git status --short | md5` = `99aa2ea6…`。
- 采用的准则：Task Unit 快速审查，加上 Code 准则。依据：`spec.md` Definitions（交付）、RQ-012、RQ-030～RQ-038、附录 D 及其口径、附录 E、附录 G VC-8；`plan.md` 共享决定（发件箱归属、组装点、R-1 澄清、过渡期规则 3）；TASK-01 Remaining 第 2 条（根因）；TASK-03 末尾主控方处置；TASK-04 Remaining 与主控方补充；今天的语义参照 `TouchSocketNetworkMessageSender.cs`、`GameMessageSemantics.cs` 中的 `GameExplicitMessageDelivery`、`SupplementalSynchronizationMessages.cs` 中的 `CleanupNotificationDelivery`、`NotificationBus.cs`。

## 复测与实验

- **定向测试**：`Networking.Tests` 的 `~.Outbound.` 27/27 通过；`Application.Tests` 的 `~.Execution.Outbox.` 12/12 通过。所有实验恢复之后又各跑了一次，结果相同。
- **日志核对**：
  - `task-05-build-test.txt`：0 警告 0 错误。
  - 各项目总数：50＋1694＋80＋475＋(967＋2)＋(132＋2)＋(4395＋2＋16)＋165 ＝ 7980；通过数 7958。
  - 失败只有两条 `DimensionDungeonTests`；表征测试跳过的两条是 b1-strict 与 b2-strict。
  - `task-05-realsocket-load.txt` 20 次全部 6/6；`task-05-new-tests-x20.txt` 中 27/27 与 12/12 各出现 20 次。
  - 以上都与 Result 一致。
- **不接线**：在 `Server/src` 中检索新类型与新命名空间，只命中新目录里的 7 个文件。
- **临时实验**，全部已恢复：
  - 恢复后 sha1 与上面逐一相同，`git status` 的 md5 不变；两个测试项目已用恢复后的源码重建。
  - 探针测试 `ReviewProbeTemp.cs` 跑完已删除。
  - 仓库外的 socket 探针在 scratchpad `lingerprobe/` 中。
- **变异结果**：

| 变异 | 结果 |
|---|---|
| 关连接请求随所在单元登记 | `A_close_request_inside_a_unit_survives_its_rollback` 变红 |
| 队列上限判断从 `>` 改为 `>=` | `Up_to_the_limit_is_admitted` 变红 |
| 去掉 `PredecessorsDeliveredAsync` 中对 `before.Settled` 的等待 | 27/27 仍绿。原因：`BreaksGroup` 对未完成任务取 `.Result` 时会同步阻塞，外部行为近似不变。这也说明没有用例覆盖「组内中间条目提前完成」的形状，见 B-1 |

## 阻断项

### B-1　有序组的中间条目提前完成时，等待上限失效：后继无限期等前序，慢的前序不会被关闭

**原因**：后继条目先 `await before.Settled.WaitAsync(ending)`，这一步不限时（`OutboundSender.cs:189-190`）；2 秒上限只加在紧邻的前驱上（`:193`）。这个设计依赖一个前提：每个中间条目都亲自对它的前驱做过限时等待。但中间条目有几条路径会不经 `ProcessAsync` 就完成：

- 所在队列已经结束，入队时直接判 Discarded（`:390-391`）；
- 入队即超限，判 OverLimit（`:392-393`）；
- 还在排队时，被队列的 `End` 一并判为 Discarded（`:429-430`）。

这时没有任何一方对更早的未完成条目施加上限。后继只能等那个条目按它自己连接的队列自然完成，而那个队列最多积压 1 MiB，其中每条各有 2 秒期限。

**实测**：临时探针，使用生产默认值 `OutboundSendOptions.Default`。

- 组 A(c1) → B(c2) → C(c3)。c1 上 A 之前排着 3 条积压，每次发送 1.5 秒；c2 已先被关闭。
  - C 在 **6010 ms** 后才交付；A 判 Delivered，没有任何连接被判失败。
- 对照组：c2 保持在线，其余相同。
  - C 在 **2010 ms** 后交付；A 判 TimedOut，c1 被关闭，失败回调 1 次。
- 把等待上限缩到 300 ms、A 慢 3 秒时，C 等了 3008 ms，是上限的 10 倍。

**后果**：违反 RQ-033「等待前序条目的时间有上限……超时后判定前序条目的目标连接失败」，以及 AC-012「前序连接超过等待上限时，它被关闭，后续条目照常交付」。

- 到了 T-006，最典型的是 XO-5 清理通告：所有者 → 城镇同伴 → 队伍投影。清理场景里同伴的连接已经关闭或失败是常态。
- 一旦出现，队伍投影就要等到所有者的整个积压排空，而慢的所有者不会被断开。用户关心的「该断的不断」正是这一形态。
- 现有表征测试抓不到它。

**最小修正**：

- 把上限施加到「最早的未完成前序」，而不只是紧邻的前驱。例如沿链从早到晚逐个等待未完成的条目，每个条目各用 `OrderedWaitLimit` 限时，超时就 `End(..., TimedOut, close: true)`。或者等价地，给 `before.Settled` 的等待也加上限，超时后对链上第一个未完成的条目判 TimedOut。
- 配一条用例：组内中间条目所在的连接已关闭，或入队即超限，断言后继在约一个等待上限内交付，并且慢前序被判 TimedOut、连接被关闭。

## 非阻断项

### N-1　中止式关闭的残余风险（Remaining 第 2 条）说重了，应按证据改写

**探针**：.NET 10，macOS 回环，仓库外的独立程序。

- 服务端做法与 Result 读出的 TouchSocket 关闭顺序相同：发送返回后立即 `Shutdown(Both)` ＋ `Close(0)`。客户端接收缓冲 4 KiB，300 ms 后才开始读。
- 64 KiB、512 KiB、2 MiB 三种大小，客户端都**完整收到，并以 FIN 结束**。
- 对照一：只做 `Close(0)`，不先 `Shutdown`。客户端以 RST 结束并丢数据，512 KiB 只收到 310,568 B，2 MiB 只收到 1,894,512 B。
- 对照二：显式设置 linger 0 再关闭。结果与对照一相同。

**结论**：TouchSocket 在 `Close(0)` 之前先做 `Shutdown(Both)`，至少在 macOS 上，这让已进入内核发送缓冲的数据照常发出，而不是随 RST 丢弃。Result 写的「内核发送缓冲里尚未发出的字节可能随 RST 一起丢弃」在本机不成立；Linux 与 Windows 未验证。

**建议**：

- 把 Remaining 第 2 条改为「macOS 上实测不丢；Linux（CI）与 Windows（发布目标）未验证」。
- 让 T-006 在 Linux 上用「帧大于 socket 缓冲、客户端延迟读」的形状复验一次，例如把真实 socket 用例的第二帧放大到 512 KiB 以上。
- 在这个前提下，b1-strict 与 b2-strict 的稳定性不受这条残余拖累。

### N-2　组外的 Required 与 BestEffort 没有区别，与今天的显式投递有一处差异，需要在 T-006 交接里写明

- **今天**：`DeliverExplicitAsync`（`TouchSocketNetworkMessageSender.cs:124-149`）先发 Required 直发消息，失败时异常上抛，**所有 BestEffort 目标都不再发送**。BestEffort 目标逐个隔离，某个目标失败只中止该目标余下的消息。
- **新组件**：组外的 Required 失败只关闭该连接，其他目标照常交付。BestEffort 的「每目标中止」由「一个目标一个条目、失败即关闭」承接，这一点比今天更强，符合 RQ-036。
- **影响**：要保持 RQ-034 中「Required 失败后其余不再发送」，T-006 必须把请求者的 Required 条目排在同一个有序组里 BestEffort 条目的前面，代价是一次组内等待。否则这就是一处未列入附录 G 的行为变化。
- Remaining 第 4 条只给出了 XO 的映射建议。应补上普通 `SendExplicitAsync` 的映射建议，或者注明这项差异由 T-006 裁定。
- `CleanupNotificationDelivery` 的映射（所有者 Required，同伴与队伍 BestEffort，同组）与今天一致：同伴的投递失败不会阻止队伍投影，所有者失败则两者都不再发送。用例 `A_required_failure_stops_its_group…` 覆盖了这一点。

### N-3　编码失败的处理与今天的通知路径不同

- 新发送端遇到编码失败会关闭连接（`EncodingFailed`）。
- 今天 `NotificationBus.AttemptAsync` 里，编码异常发生在 `SendAsync` 内部，只判该收件人 Failed、记 Warning，不断开连接。
- 直发路径上，今天的异常会经 handler 导致断线（DR-15），这一点一致。
- 这属于程序错误路径，例如帧超长或编码器缺失。建议 T-006 把它补入附录 E 或附录 G，作为一条明确记录的差异。

### N-4　RQ-031(c) 中来源连接的关闭只排在来源连接自己的条目之后

- `Complete` 把 `Close(source)` 追加在列表末尾。发送端按连接 FIFO，所以来源连接上的帧一定先于关闭；但其他连接上的条目可能在来源连接关闭之后才交付。
- 实际影响很小：清理 CMD 的消息按 RQ-032 排在后面。
- 但 Remaining 第 5 条只就 RQ-012 讨论了跨连接时机。建议把 RQ-031(c) 的这一解读也写进去：按连接排序即足够。

### N-5　刷出时 `Submit` 抛出异常，会连带丢掉来源连接的关闭

- CMD 失败时，`RunAsync` 记下刷出异常并吞掉（`GameplayOutbox.cs:37-45`），追加的 `Close(source)` 也一起丢失，违背 DR-15「抛异常即断线」。
- CMD 正常结束时，整个发件箱都丢失。
- 可能的触发：同一组对象跨 CMD 复用导致序号不递增，或者停机时发送端已释放。
- 两者都是程序错误。可以把序号校验前移到登记时，或者在刷出失败后单独提交 `Close(source)`。

### N-6　有序组对象必须每条 CMD 新建；需要写成约束

- `LastSubmitted` 会跨 `Submit` 保留，所以复用组对象并让序号重新从 1 开始，会直接抛异常。
- 反过来，如果某个组长期存活、序号一直递增，`LastAdmitted` 会经 `Predecessor` 链挂住组里每个条目，连同各自的 `Frames`，永远不释放，形成泄漏。
- 建议在 `OutboundOrderGroup` 的注释里写明「一条 CMD 一个组」。也可以在条目落定之后清掉 `Predecessor` 与 `Frames`。

### N-7　刷出做法的依据与代价：结论可用，有一处交互要交给 T-006

- **依据**：`pause = resume = 1` 时，只要管道里有未消费的字节，写端就暂停，直到读端 `AdvanceTo` 越过全部字节才恢复。`RunSend` 在 `TcpCore.SendAsync` 完成之后才 `AdvanceTo`，所以「发送返回」就等于「内核已接收」。这与 Result 的变异实验（改用默认管道后 5/5 丢帧）互相印证。我没有独立复读 IL。
- **代价**：
  - 暂停按「未消费字节数」判定，不是逐字节暂停。一批消息合成一个 `byte[]`、一次发送，所以每个条目只多一次线程池往返。
  - 管道里不再缓存数据，内存反而下降。
  - 大批次不会被管道拖慢，速度只受 socket 接收快慢约束。
- **要交给 T-006 的交互**：2 秒交付期限按**条目**计。
  - 如果把选角的 454 KB 作为一个条目，那么在低带宽或高 RTT 的链路上，减去内核缓冲后，要在 2 秒内送完。今天直发没有期限，通知是按每收件人计的小消息。
  - 建议 T-006 选定选角条目的切分粒度，或者实测一次，并对照 VC-8 判断这是不是可接受的变化。
- **「未配置管道即判 Failed」**：后果是第一次交付就让全部连接断开，任何真实连接的测试都会立刻变红，属于响亮的快速失败，不会静默出错。Remaining 第 1 条已写明，这一配置需要与「执行器上的发送改为登记发件箱」同时生效。

## 已核对、无问题的部分

- **RQ-031**：
  - (0) 单元内登记的待组装条目在 `CommitAsync` 时组装，单元外登记的立即组装；有用例演示组装读到的是提交时的状态。
  - (a)(b)(c) 刷出规则实现正确：关连接请求不属于任何单元（R-1），回滚和不确定单元中的条目丢弃，失败结束时最后追加来源连接的关闭。
  - (d) 组装失败按收件人隔离、记 Warning、不关连接，逐收件人结果可见。
  - `MarkUncertain` 与作业结束时仍开着的单元，都按「不确定」丢弃其条目。
- **RQ-035**：按连接上下文的引用比较。值相等但引用不同的上下文判 Stale，经真实 socket 证明；变异「改为值相等」能让用例变红。
- **RQ-036**：交付超时、发送方忽略取消时的双重限时、超限即关闭、余下条目丢弃，都有用例和变异证据。关闭在分离任务中进行，只关闭精确身份对应的连接。
- **RQ-037 与 RQ-008**：
  - `Submit` 只在锁内校验并写入无界通道，不阻塞。
  - 三类循环都在 `SuppressFlow` 下启动，有用例证明在作业内提交时，交付不在执行器上。
  - 关连接在每连接的排空循环里 await，执行器从不等待它。
- **执行器关系**：发件箱只能在作业内取得和登记，作业外、作业结束后、`RunDetached` 内都抛程序错误。`GameplayJobSlot` 用 `ConditionalWeakTable` 挂在令牌上，作业结束后 `Active` 为空，再读即抛，不会串到后续作业。
- **有序组无死锁**：按提交序归纳，最早的未完成条目一定在它所在队列的队首，而且它的前驱都已完成，所以总能推进。B-1 是等待上限的问题，不是死锁。
- **上限推算**，抽查四项：
  - 0x000D：5＋101×363、5＋131×210、5＋101×200、3＋101×240、9＋101×120、3，合计 120,763；
  - 0x00AC：1＋33＋8×8,334 ＝ 66,706，共 34 帧；
  - 0x0166：175＋26×500 ＝ 13,175；
  - 0x0061：6＋255×166＋20×563 ＝ 53,596。
  - 四项都与 tsv 一致。tsv 69 行的四列合计为 36,360、450,680、195、237，与 md 及常量一致：454,235 ＝ 450,680 ＋ 237×15；2 × 454,235 ＝ 908,470 ≤ 1 MiB。
  - 「未计入」一节如实列出了可能吃掉 140,106 B 余量的项（0x01C0 是刻意没有生产写入方的连招页）。
- **真实 socket 测试改用 `GameTcpService`**：这是生产实际使用的服务类型，带两处已知的 TouchSocket 规避（accept 时序、TcpCore 回收）。用裸 `TcpService` 测的是生产不会出现的配置，所以改用它是对的。失败根因「未单独证实」，Result 已如实写明，看不出掩盖了新组件的缺陷。
- **契约边界**：没有改已跟踪文件，没有新增 `InternalsVisibleTo`。Result 中的测试数与加法（12＋0＋16＋5＋6 ＝ 39）与实测一致。

## 结论

修正 B-1（含用例）之后复审。N-1 至 N-7 不阻断本 Task，但应在 Result 的 Remaining 中更正或补充，交给 T-006。

## Round 2

```yaml
kind: rks-review
verdict: approved
```

- 审查对象：`tasks/TASK-05.md`，Result 第 2 轮（见末尾「各轮变化」）。
- 被审文件 sha1：
  - `OutboundSender.cs` = `08ddc551…`；
  - `GameplayOutbox.cs` = `363d7eed…`；
  - `GameplayOutbound.cs` = `92552559…`；
  - 其余生产文件与第 1 轮相同（`OutboundTransport.cs` `2c281e5c…`、`OutboundLimits.cs` `27371eb9…`、`OutboundEncoder.cs` `7268d544…`）。
- 与第 1 轮源码备份逐行 diff，复审第 1 轮的未决项及其修正带来的直接回归。

### 复测

- **定向测试**：Networking `~.Outbound.` 31/31、Application `~.Execution.Outbox.` 14/14 通过。
- **数字核对**：
  - 新测试加法 14 ＋ 0 ＋ 19 ＋ 5 ＋ 7 ＝ 45。
  - `task-05-build-test.txt`（18:43:11Z）：0 警告 0 错误。各项目总数 50＋1694＋80＋475＋(967＋2)＋(136＋2)＋165＋(4397＋2＋16) ＝ 7986，通过 7964，失败只有两条 `DimensionDungeonTests`。
  - `task-05-new-tests-x20.txt` 中 31/31 与 14/14 各 20 次；`task-05-realsocket-load.txt` 中 7/7 共 20 次。
  - 都与 Result 一致。
- **临时探针**，`ReviewProbeTemp.cs` 跑完已删除，测试项目已重建，生产文件 sha1 不变：

| 探针 | 结果 |
|---|---|
| P1：第 1 轮的 B-1 形状，生产默认参数 | C 在 2003 ms 交付，A 判 TimedOut（第 1 轮为 6010 ms，A 判 Delivered） |
| P2：A(卡死) → B(已关) → D(已关) → E(卡死) → C，两个中间条目提前完成，两个前序卡死 | C 在 4010 ms 交付；A 与 E 都判 TimedOut，各自的连接被关闭，失败回调 2 次 |

### 第 1 轮各项的处置

**B-1：已解决。**

- 修正后，`PredecessorsDeliveredAsync` 沿链收集所有「未落定」的前序，从最早的开始逐个限时等待；已完成的跳过，超时的对其 `End(TimedOut, close: true)`。
- 逐种形状核对：
  - **多个中间条目提前完成**：它们的 `Done` 已完成，被跳过。P2 证明了这一点。
  - **落定顺序与链顺序不一致**：比如 B 先于 A 完成。收集在第一个 `Settled` 已完成的条目处停止，所以 B 会入栈；出栈时先等 A，再跳过 B。
  - **`Settled` 滞后**：`Settled` 由 `WhenAll` 经异步续延完成，可能比 `Done` 晚一跳。这只会让几个已完成的条目入栈，出栈时都被跳过，不影响正确性。
- **超时的链式累积**：
  - 每个前序的上限，从它前一个前序落定时开始计。所以后继的总等待上界是「链上卡住的前序数 × 2 秒」：P2 中两个卡死的前序得到 4 秒。
  - 只有一个慢前序时，后继自己的等待与组内中间条目的等待并行计时，谁先超时谁判定，总等待约等于一个上限（P1，2003 ms）。
  - 这与 RQ-033 中「有顺序依赖的一组连接，最多被拖慢一个等待上限」的关系是：只能按「每个慢前序最多拖慢一个上限」来读。否则就得把一个只是排在慢前序后面、本身并不慢的条目也判为超时，那又违背了「判定前序条目的目标连接失败」。
  - 今天 `NotificationBus` 按收件人依次投递，每人 2 秒，也会同样累积，所以这不是新组件引入的退化。
  - 见下面 R2-N1。
- 新增的 Theory 覆盖三种「中间条目提前完成」的形状。执行者恢复第 1 轮逻辑的变异，三组全部变红，符合预期。

**N-5：已解决。**

- `Flush` 至多调用两次 `Submit`：第一次提交整批，被拒时第二次只提交去掉组的关连接条目，加上 `Close(source)`。没有重试，也没有递归，所以不会形成循环。
- **不会重复关闭**：`Submit` 先校验再 `TryWrite`，要么全收、要么全拒，被拒的那批一条都没进入发送端。即使同一连接出现两条关闭（例如用户请求的关闭加上 `Close(source)`），第二条也只会因队列已结束判 Discarded，没有副作用。
- 第二次提交不带有序组，序号校验不会再次失败。剩下唯一的失败途径是发送端已释放（停机），见 R2-N2。

**N-6：已解决。** 约束已写入 `OutboundOrderGroup` 的 XML doc。条目落定后释放已编码字节，计数改用 `Length`，字节上限的计算不受影响。

- **释放与发送的竞态**：
  - 每个连接只有自己的排空循环会发送，并且先取引用 `var frames = item.Frames`、再检查 `ending`。
  - 其他线程只能经 `End` 先 `Cancel`、再 `Complete`（释放）。`End` 能并发发生的只有两种，都带 `close: true`：后继等待超时，以及入队超限。
  - 释放只是把属性换成空数组，不改原数组的内容。所以无论读到哪个版本，最坏情况都只是向一个正在被关闭的连接多送一次原样的字节，或者送一个空缓冲。该条目的结局已由 `End` 定下，`Complete` 的 `TrySetResult` 不会覆盖它。
  - 在 ARM 内存序下，「先读到空数组、却没读到取消」理论上可能发生，后果同样只是一次空发送。属良性竞态，不影响正确性。

**N-1：已解决。** Remaining 第 2 条已按探针证据改写，新增真实 socket 用例。用例强度见 R2-N3。

**N-2、N-3、N-4、N-7：** 已分别作为约束或待决项写入 Remaining 第 9、10、11、13 条，表述与审查一致。

### 第 2 轮非阻断项

- **R2-N1**：建议在 Remaining 里补一句 RQ-033 的解读：「一组的总等待 ≤ 链上卡住的前序数 × 2 秒；每个慢前序最多拖慢一个上限」。
  - 还应提醒 T-006：线性链会把同一阶段的多名收件人（例如 XO-5 的城镇同伴）也串成先后，于是卡住的同伴会逐个累加等待。
  - 如果 T-006 想把同一阶段压到一个上限之内，需要另行设计「阶段」形状，比如同阶段的条目只等上一阶段落定。本 Task 不需要做。
- **R2-N2**：发送端已释放时（停机），失败路径上第二次 `Submit` 也会抛出 `ObjectDisposedException`。这个异常会从 `catch` 块里冒出，替换掉 CMD 原来的异常，与注释「原异常才是调用方看到的」不符。
  - 停机时连接本来就会关闭，实际影响很小。
  - 可以给第二次提交也套一层 try/log。
- **R2-N3**：新增的真实 socket 用例证明了「小接收窗口、慢读、900 KiB 单批次之后关闭，完整收到并以 FIN 结束」，形状正确。
  - 但它没有断言关闭发生在客户端开始读取之前：观察者的等待放在读完之后。
  - 在 macOS 上，内核会一次收下全部字节，关闭必然先于读取，所以覆盖的正是残余风险的形状。
  - 在内核缓冲较小的平台上，发送会等读端腾出窗口，关闭就会落在读取中途，形状随之改变；读得太慢时还可能撞上 2 秒交付期限，这其实正是 N-7 的信号。
  - 建议 T-006 在 Linux 上复验时，记下关闭时刻与读取开始时刻的先后。

### 结论

第 1 轮的阻断项 B-1 已修正，并有用例与独立探针证据。修正没有引入阻断级回归，本轮没有阻断项。R2-N1 至 R2-N3 不阻断本 Task，可以在提交前顺手写进 Remaining，也可以交给 T-006。
