---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/spec.md
target_state: ready
contract_revision: 2
verdict: changes_required
remediation: bounded
remediation_target: spec
reviewer: independent subagent (fresh context; reviewing-work Spec reference + writing-specs readiness + sdd-governed Adversarial Review Transitions)
---

# Spec 审查：serial-gameplay-executor（contract revision 2）

## 范围

- 目标：`.rks-agent/tasks/serial-gameplay-executor/spec.md`，`state: ready`，`contract.revision: 2`；作者已按挑战集做过一次针对性修正（新增 RQ-008、RQ-009、RQ-025、附录 H 等）。审查的是修正后的当前文本。
- 内容：契约覆盖、挑战 CH-1～CH-7 的裁定、需求可判定性与 WHAT/HOW 边界、契约复审遗留风险的承接。
- 只审查，不修改 Spec、契约或代码。不评审 Plan（尚不存在）。

## 依据

- 准则：`reviewing-work` 的 Spec 参考；`writing-specs` 的 Ready 条件；`sdd-governed`「Adversarial Review Transitions」。
- 治理来源：`TaskContract.md`（r2）、`reviews/task-contract-review-r2.md`（approved，9 条非阻断）、`reviews/spec-challenges.md`（CH-1～CH-7）、`research/01`–`05`、`AGENTS.md`、`Docs/Agent/RefactorPreferences.md` §3。
- 代码基准：worktree HEAD `a02bce46`。下文路径相对 `Server/`。
- 亲自核对过的代码事实：
  - `src/Dfo.Server.Application/Effects/EffectOperationContext.cs`：`BeginMain` 无帧时返回空 scope；`EnterWithoutSelectedSession` 是独立的「无选中会话」帧。
  - `src/Dfo.Server.Application/Effects/EffectOperationLedger.cs`：只有 `IEffectSettlementRequest` 才进账本，且「无选中会话」时走拒绝帧；`finally` 把 Pending 记成 Rejected。
  - `src/Dfo.Server.Application/Commands/NetworkCommandAuthorization.cs:23`：帧为 null 时放行。
  - `src/Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs`：入队前做 `IsBlocked`、`IsSuperseded`、`CaptureAuthority`。
  - `src/Dfo.Server.Application/Commands/SelectedSessionSafety.cs:57-72`：`BlockAsync` 除了 Fence，还对**每个**接收者 `RequestCloseExactAsync`。
  - `src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:440-497`：两处 catch 封锁全部 captured 参与者后**重新抛出**。
  - `src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs:150-273`：准入拒绝先发 x08 两条消息、再抛出；补偿后先向同伴投递清理通告、再关连接，最后 `failure?.Throw()`。
  - `tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs:425-437`：`AdmissionRefusal_SendsOrderedX08...` 断言先发 x08，再抛出。
  - `tests/Dfo.Server.Protocol.Tests/Game/MonsterDeath/MonsterDeathHandlerTests.cs:927-952`：只断言 `IsBlocked` 与 `Fenced`，**不断言连接被关闭**。
  - `src/Dfo.Server.Abstractions/DomainEvents/IDomainEventPersistence.cs`：`DomainEventDeliveryState { Pending, Completed, Terminal }`；`CompleteAsync(..., reason)`；`FailAsync(..., terminal)`。RQ-053(b) 不必改表即可实现。
  - `src/Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:178-187`：零处理时按 `NextDueAsync` 等待，到期即零延迟。CH-4 的自旋判断属实。
  - `src/Dfo.Server.Protocol/Game/UnansweredReports/UnansweredReportSlices.cs:274-289`：只记 Debug 日志。
  - `src/Dfo.Server.Protocol/Game/Tournament/HerasQuotaDailyRefreshService.cs`：「本期已送」是进程内字典。DR-6 的「刷出时记录」不涉及写库。
  - RQ-071 列出的 7 个判据类都存在。

## 契约覆盖对照

| 契约项 | Spec 承接 | 判断 |
|---|---|---|
| Goal：唯一执行器、全部入口为 CMD、提交后发件箱、玩法路径无锁、除列明变化外行为不变 | RQ-001～RQ-015、RQ-030～RQ-037、RQ-060、附录 G | 覆盖。但「行为不变」被 RQ-031(c) 部分破坏，见阻断项 B-1 |
| Scope In：执行器与入队点、分流键 | RQ-001、RQ-002 | ✓ |
| Scope In：重计算与背压 | RQ-007、RQ-005、AC-003、RQ-090(d)(e) | ✓ |
| Scope In：全部入口改为 CMD；启动期例外逐项列明 | RQ-010～RQ-015、附录 A、附录 B | ✓ |
| Scope In：协议级 CMD | RQ-020～RQ-025 | ✓（纯遥测例外见非阻断 2） |
| Scope In：发件箱、每连接发送队列、批次与必达、逐目标隔离、跨连接顺序 | RQ-030～RQ-037、附录 D | ✓ |
| Scope In：依赖投递结果或同步发送异常的行为 | RQ-038、附录 E | 有承接；失败路径上的清理通告与关连接被 RQ-031(c) 吞掉，见 B-1 |
| Scope In：调用链与深度上限 | RQ-050～RQ-054 | ✓ |
| Scope In：删除玩法路径上的锁 | RQ-060、附录 F | ✓（「其他并发原语」颗粒度见非阻断 6） |
| Scope In：保留并迁移（SQLite 事务、具名判定、会话隔离、顶替检查、一请求一主结算、事件组件） | RQ-023、RQ-024、RQ-025、RQ-040～RQ-043、附录 H | 隔离的「关连接」一半与 RQ-031(c) 冲突，见 B-1 |
| Scope In：判据 + 反例 | RQ-061、RQ-070～RQ-072 | ✓ |
| Scope In：测试（含选角端到端） | RQ-080～RQ-083 | ✓ |
| Scope In：测量 | RQ-090、RQ-091 | ✓ |
| Scope In：文档 | RQ-100 | ✓ |
| Scope Out 1–6 | Spec Scope Out；RQ-051、RQ-053(b) 均不改表 | ✓ 无越界 |
| Constraint：顺序保证无例外、机器判据 | RQ-003、RQ-010、RQ-061、AC-002、AC-017 | ✓ |
| Constraint：保住保护意图，不得静默消失 | RQ-023、RQ-038、附录 E | ✗ 部分：其他参与者的关连接、同伴清理通告会静默消失（B-1） |
| Constraint：可见行为变化须列明 | 附录 G | ✗ 部分：VC-2 的理由与覆盖面都不对（B-1） |
| Constraint：纯搬迁与语义改动分开提交 | 没有写进 Spec（属 Plan）；RQ-080 提供能失败的证据 | 可接受，由 Plan 承接 |
| Constraint：一次到位、删净 | RQ-022、RQ-060、AC-017 | ✓ |
| Constraint：单数据库写者 | RQ-061、附录 B | 终态 ✓；「迁移期间」由 Plan 承接 |
| SC-1 | RQ-010、RQ-011、RQ-061、附录 A/B、AC-006 | ✓ |
| SC-2 | RQ-020、RQ-070、AC-008 | ✓，但纯遥测例外偏离字面，见非阻断 2 |
| SC-3 | RQ-030～RQ-037、AC-011～AC-013 | ✓（RQ-033 已写明「不拖累」的边界） |
| SC-4 | RQ-060、附录 F、AC-017 | ✓ |
| SC-5 | RQ-040～RQ-043、AC-015 | ✓ |
| SC-6 | RQ-050～RQ-054、AC-016 | ✓ |
| SC-7 | RQ-038、附录 E、附录 G、AC-014 | 部分，见 B-1 |
| SC-8 | RQ-080～RQ-083、AC-019、AC-020 | ✓（受影响线级用例没有点名，见非阻断 5） |
| SC-9 | RQ-090、RQ-091、AC-005、AC-021 | ✓ |
| SC-10 | RQ-100、AC-022 | ✓ |
| SC-11 | Spec 没有对应条目 | 这是汇报义务，由控制者承接，见非阻断 11 |

每条 RQ 都至少被一条 AC 覆盖（逐条核对：RQ-001～009 → AC-001/002/003/004/005/007；RQ-010～015 → AC-006/007；RQ-020～025 → AC-008/009/010；RQ-030～038 → AC-011～014；RQ-040～043 → AC-015；RQ-050～054 → AC-016；RQ-060/061 → AC-017；RQ-070～072 → AC-008/018；RQ-080～083 → AC-019/020；RQ-090/091 → AC-005/021；RQ-100 → AC-022）。引用的文件、类型与测试名都可以解析。

## 挑战裁定

| 挑战 | 原始严重度（独立评估） | 状态 | 理由 |
|---|---|---|---|
| CH-1 请求级保护靠 AsyncLocal，跨入队点后静默失效 | high，confirmed | 已解决（有残余澄清） | RQ-025 规定请求级上下文是 CMD 的显式数据，dispatcher 的检查改在执行器上、CMD 开始时重做；附录 H 逐项列出 0/45/58/60/100 号行为的非锁职责去向；AC-009 增加经真实入站路径的重放反例。修正相称。残余：「缺少上下文即程序错误」没有区分合法的「无账本条目」（非效果消息）与「无选中会话的效果请求」两种状态，附录 H 也漏了 `EnterWithoutSelectedSession` 与「序号必须前进」，见非阻断 1 |
| CH-2 多参与者隔离的载体被当锁删掉；提交后失败混同投递失败 | high，confirmed | **部分解决** | 参与者集合已移入「保留」，RQ-023(c) 写明封锁每个会话，DR-4/VC-4 已拆成两种情形，并点名测试保持断言。但 `BlockAsync` 的另一半（对每个接收者关连接）在新模型里会作为关连接请求登记进发件箱；怪物死亡封锁后会重新抛出，RQ-031(c) 随即「丢弃发件箱中全部未刷出的条目」，其他参与者的关连接被丢掉。点名的测试只断言 Fence，不断言关闭，拦不住。并入阻断项 B-1 |
| CH-3 投影的组装时刻与依据的状态不明 | high，confirmed | 已解决 | RQ-031(0) 定下组装点：工作单元内登记的条目在该单元提交完成那一刻组装，工作单元外的在登记时组装；发送端只收组装好的帧。RQ-080 改为断言完整帧字节。修正相称。残余：组装失败的语义（今天是逐收件人 Failed，新模型下会使整条 CMD 失败），以及 (0) 本身没有直接的验收场景，见非阻断 3、8 |
| CH-4 深度超限后 delivery 未落定，导致自旋活锁 | high，confirmed（已核实分发循环零延迟重读） | 已解决 | RQ-053(b) 要求用豁免深度检查的系统 CMD 落定那一行（Reliable 写 Terminal，LiveOnly 带理由写完成；现有接口直接支持，不改表）；RQ-050 写明重试与关连接引起的 CMD 继承调用链；RQ-052 给出统计方法；AC-016 断言分发循环进入等待而不是空转。修正相称 |
| CH-5 RQ-091 的口径可能虚假达标 | high（这是契约假设唯一的验证关口），confirmed | 已解决 | RQ-090/091 要求真实录制的负载构成、长 CMD、基线对照、SQLite 设置与磁盘类型、冷启动、p99.9/max 上限、`Lazy` 预热清单。分量偏重，但这是契约 Assumptions 的验证门，相称。残余：真实录制从哪里来，以及基线上没有「入队等待」这个量，见非阻断 7 |
| CH-6 等待关系与停机顺序未约束 | low，confirmed | 已解决 | RQ-005 定义「完成」不含交付；RQ-008 写明等待关系；RQ-009 写明停机顺序；AC-007 有自锁线级测试与停机落库测试。修正相称 |
| CH-7 「一一对应」的计数单位有歧义；纯遥测进全局队列 | low，confirmed | 已解决（引入一处偏离） | RQ-020 改为「每个 `ClientMessageType` 恰好映射一个 CMD 类型」并检查全函数；纯遥测 handler 列入附录 B。已核实 `UnansweredReportHandler` 只记日志。残余：这一例外偏离了 SC-2 的字面，见非阻断 2 |

挑战集以外的发现：B-1 的后两种后果（准入拒绝 x08 丢失、补偿清理通告丢失），挑战集没有提到，是本审查从代码核实出来的。

## 阻断项

### B-1：RQ-031(c) 在 CMD 失败时丢弃全部条目，吞掉了其他连接的关闭与已提交结果的通知，与 RQ-023(c)、契约「保住保护意图」冲突

**问题。** Definitions 把发件箱定义为「登记出站消息**与关连接请求**的地方」，RQ-030 要求 CMD 内的关连接请求只能登记进发件箱。RQ-031(c) 又规定，CMD 以未处理异常结束时，「丢弃发件箱中全部未刷出的条目，并关闭来源连接」。VC-2 给的理由是「反正都会断线」。

可是今天有几条路径会先**有意**发出消息或关闭**其他**连接，然后再抛出。照 Spec 字面迁移（CMD 照旧在末尾抛出，以保住 DR-15「异常即断线」），这些动作会被一并丢掉：

1. **其他参与者的隔离只剩一半。** 怪物死亡在提交之后失败，或提交结果不确定时，会对全部参与者调用 `BlockAsync`，然后重新抛出（`MonsterDeathSlice.cs:440-444`、`:487-497`）。`BlockAsync` 包括 Fence 和对每个接收者 `RequestCloseExactAsync`（`SelectedSessionSafety.cs:57-72`）。在新模型里，后一半是登记在发件箱里的关连接请求，会被 RQ-031(c) 丢弃。结果是：同伴的会话已被封锁，但连接没有关闭。此后它发来的每个包都在 `IsBlocked` 处被静默丢弃，客户端卡死，直到玩家自己退出。这直接违反 RQ-023(c)「处理方式不变」，也违反契约 Constraint「失败时隔离或关连接……不得静默消失」。AC-009 点名的 `Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 只断言 `IsBlocked` 与 `Fenced`（`MonsterDeathHandlerTests.cs:945-948`），不断言关闭，这个回退不会被发现。进本、离开副本、祭坛、两塔、婚礼这些带参与者集合的 CMD 同理。
2. **同伴收不到补偿通知。** 选角在 Publish 之后失败时，补偿完成后会先向同伴投递清理通告（`SelectCharacterHandler.cs:211-215`、`:171-183`），再关连接并抛出（`:273`）。在新模型里，这些登记给**其他连接**的通告会被丢弃。同伴已经看到角色进入城镇，却永远收不到离开，留下幽灵角色。VC-2 只说「客户端收不到」，没有申报对其他收件人的影响；DR-1、DR-10 的保护意图也就落空了。
3. **准入拒绝的提示丢失。** 准入被拒时，今天先按序发出 CMD `01/0004` 与 NOTI `00/01F7`（x08）两条消息，再抛出（`SelectCharacterHandler.cs:150-169`；测试 `AdmissionRefusal_SendsOrderedX08AndHasZeroLifecycleSideEffects`）。在新模型里，客户端只会被断开，看不到拒绝原因。这两条消息正是要在断开**之前**让客户端看到的，所以 VC-2「反正都会断线」的理由在这里不成立。

**后果。** 实施者照字面执行，编译和现有点名测试都能通过，但会交付一个在失败路径上丢失保护的系统：同伴卡死、幽灵角色、拒绝原因消失。这些都是契约明令不得静默消失的保护。

**要求的最小修改**（bounded，目标 spec，不改契约边界）：

1. 改写 RQ-031(c)。CMD 以未处理异常结束时，已登记条目按 (a)(b) 同一套提交规则处理：
   - 已提交工作单元内登记的条目，以及工作单元外登记的条目，照常按序刷出；
   - 回滚或结果不确定的工作单元内登记的条目，丢弃；
   - 已登记的关连接请求**一律执行**，不因 CMD 失败而丢弃；
   - 最后关闭来源连接，关闭发生在此前条目交付之后（RQ-012）。

   这与今天「发出去的已经发出、然后断线」的语义一致，也不违反「只刷出已提交的结果」。如果作者选择另一种规则，至少要保证：关连接请求不被丢弃；发给非来源连接、反映已提交状态的条目不被丢弃；有意先回复再失败的路径（准入拒绝）照旧送达。
2. 相应删除或收窄 VC-2：只保留「回滚或不确定工作单元内登记的消息不再发出」这类真实的变化，并改写理由。
3. 在 AC-011 或 AC-009 中补三条可失败的验收：
   - 怪物死亡提交后的非传输失败，会关闭**每个**参与者的连接，而不只是封锁；
   - 选角准入拒绝，在断开前按序送达 x08 两条消息；
   - 选角在 Publish 之后失败、走补偿时，同伴收到清理通告。

   RQ-080(b) 的表征测试应覆盖第三条（有同伴观察者、失败点在 Publish 之后）。

## 非阻断残余风险

以下问题不会单独导致错误交付，建议在修正 B-1 时顺手处理，或交给 Plan：

1. **RQ-025「缺少请求级上下文即程序错误」需要分情形。** 今天只有 `IEffectSettlementRequest` 消息才有账本帧，「无选中会话」的效果请求走 `EnterWithoutSelectedSession`，只能做纯拒绝回包、不得结算。建议写明：
   - 对已选中会话的效果请求，缺少账本条目才是程序错误；
   - 无选中会话的效果请求，保留「可回拒绝、不可结算」这一状态；
   - 非效果消息本来就没有账本条目，效果足迹记录照旧为空操作。

   附录 H 补上 `EnterWithoutSelectedSession` 与「传输序号必须前进」两项。否则实施者可能把合法的无会话拒绝变成断线。
2. **纯遥测 handler 留在执行器外，偏离 SC-2 字面**（「每个入站协议对应一条协议级 CMD；handler 只解包入队」）。这个例外有 Goal 的限定语和 SC-1「执行器外部分逐项列明」作为依据，判据也守得住，所以不阻断。但它是 Spec 对契约的解释，应在 SC-11 的规划停点向用户说明。另一个做法是让它们入队，并把代价计入 RQ-090。
3. **组装失败的语义没有写明。** 今天某个收件人的 composer 抛出，只记该收件人 Failed、进重试；按 RQ-031(0)，它会成为整条 CMD 的异常，按 RQ-031(c) 断开**来源**连接。建议明确：组装异常按收件人隔离（关闭该收件人连接或记日志弃权），或者作为可见变化列入附录 G。
4. **发送队列上限的单位与下限没有写。** RQ-036 用「超过上限」触发关连接。上限必须容得下单条 CMD 最大的发件箱（例如选角整批），否则每次选角都会断线。表征测试会发现这个问题，但建议写明单位（条数或字节）和下限。
5. **附录 G 没有点名受影响的线级用例**（契约复审第 2 条）。至少包括：`GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect`、`HandlerFailureConnectionIntegrationTests`、`OutboundSendFailureTests`、`TouchSocketNetworkMessageSenderExplicitDeliveryTests`。Plan 应逐个对应到 VC 或 RQ-081 的说明清单。
6. **附录 F 没有处理「其他并发原语」**（Interlocked、volatile、ConcurrentDictionary、ConcurrentQueue、`Channel<T>`）。现在只有一句兜底。AC-017 对账时建议按「类型 × 所有者」分组给出去留（契约复审第 5 条）。
7. **RQ-090 的前提。** 本仓的 `Tool/WireSmoke` 只录过建角、选角、接任务三段，没有一段持续游玩的真实录制，可能需要用户用 A21 客户端提供。另外，基线上没有「入队等待」这个量，基线对照应改用端到端响应延迟。Plan 要先确认录制来源和对照口径，不能自造负载比例。
8. **RQ-031(0) 没有直接的验收场景。** 现在只靠 RQ-080 的完整字节间接覆盖。建议在 AC-011 加一条：在第 N 个工作单元登记的投影，反映第 N 个单元提交后、第 N+1 个单元开始前的状态。
9. **RQ-061「附录 F 指定的玩法状态写入口」在附录 F 里并没有逐个指定**，Plan 需要给出清单。另外，`AmbientStoreAccess.ReadSnapshotAsync` 开的是非只读事务，执行器外的只读查询如果走它，可能误触发守卫，需要实施时核对。
10. **RQ-071 没有列 `MarriageArchitectureTests`**（调研 02 指出它可能冲突）。「包括」足以覆盖，但建议点名。
11. **SC-11 是控制者的汇报义务。** 规划停点应列出：三项默示决定、附录 G 全部变化（含按 B-1 修正后的 VC-2）、纯遥测例外，以及 RQ-091 不达标即回到用户的出口。
12. RQ-007 的哈希等待队列无界，与今天等价（今天 HTTP 也没有上限），执行器队列受哈希并发上限约束。不构成新风险。

## 结论

`changes_required`，`remediation: bounded`，`remediation_target: spec`。

Spec 整体完整：覆盖了契约的 Goal、Scope、Constraints 与 SC-1～SC-10；契约复审中与 Spec 相关的风险（SC-8 例外、慢连接与跨连接顺序、选角先钉行为、codec 范围、背压验收）都有承接；CH-1、CH-3～CH-7 的修正充分且相称。

唯一的阻断项是 B-1。它是 CH-2 修正不完整的延续：RQ-031(c) 的「失败即全部丢弃」与 RQ-023(c)「处理方式不变」、DR-1/DR-10 的保护意图、AGENTS.md 的相邻拒绝消息约定相互冲突。照字面实施，会在失败路径上静默丢掉其他参与者的关闭、同伴的清理通告和准入拒绝提示，而现有点名测试拦不住。修正只需改写 RQ-031(c) 与 VC-2，并补三条验收，不改变批准的结果边界与风险依据。按 Adversarial Review Transitions，这是对该目标的第一次 bounded 退回。

修正后的复审只需核对 B-1 及其直接回归。非阻断 1、3、4 建议在同一次修正中顺手澄清，其余可交给 Plan。
