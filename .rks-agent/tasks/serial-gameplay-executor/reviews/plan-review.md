---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/plan.md
target_revision: worktree HEAD a02bce46（plan.md 当前文本：mode task-units，state ready，含挑战后的一次针对性修正）
spec_state: ready
contract_revision: 2
verdict: changes_required
remediation: bounded
remediation_target: plan
reviewer: 独立子代理（全新上下文；未参与 Plan 编写与挑战生成；只读；依据 reviewing-work Plan 参考、writing-plans task-units、sdd-governed Adversarial Review Transitions）
---

# Plan 审查：serial-gameplay-executor

## 范围

- 目标：`plan.md`（high-level Plan，`mode: task-units`），基线 `a02bce46`。代码路径相对 `Server/src/`，测试路径相对 `Server/tests/`。
- 判断五件事：Spec 覆盖与依赖、CH-1～CH-6 的回应、共享决定的分辨率、task-units 的 ready 条件（含 Spec 残余 R-1/R-5/R-6）、仓库约束（纯搬迁与语义分开、能失败的证据、删净）是否被切分支持。
- 本轮是该目标的第一次 Plan 审查。

## 依据

- 准则：`reviewing-work` 的 Plan 参考（task-units 一行：路线、全覆盖、责任边界、共享契约、依赖、集成检查、下一单元；未来 Task 的细节不是前提）；`writing-plans/references/task-units.md`；`sdd-governed`「Adversarial Review Transitions」。
- 治理来源：`spec.md`（ready，r3 approved）、`TaskContract.md` r2、`routing-decision.md`（long-running、attention_review on-drift）、`research/01`–`05`、`AGENTS.md`、`Docs/Agent/RefactorPreferences.md` §3。
- 挑战集：`reviews/plan-challenges.md`（CH-1～CH-6）。
- 亲自核对的代码与测试：
  - `Protocol/Game/Tournament/TournamentRewardSweepService.cs:38-47`
  - `Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:26`
  - `Networking/Runtime/TouchSocketNetworkConnectionCloser.cs:18-44`
  - `Protocol/Game/ReplayStateProjectionDelivery.cs:36-46`
  - `Application/Commands/Pipeline/UnitOfWorkBehavior.cs`
  - `Application/Transactions/DomainTransactionScope.cs:32-43`
  - 生产代码里 `Task.WhenAll` / `Task.Run` 的检索结果
  - `tests/Support/TestSessions.cs`
  - `Protocol.Tests/Game/TownProjection/TownProjectionHandlerTests.cs`
  - 测试中 `.Begin()` 与直接构造门的计数

## 覆盖映射

| Spec 条目 | 落点 | 判断 |
|---|---|---|
| RQ-001～004 | T-002（机制）；T-004 接线 | ✓ |
| RQ-005、RQ-007～009 | T-004；共享决定「重计算」「停机与等待关系」 | ✓ |
| RQ-006 | T-002（机制）；落地只写在共享决定「定时源」里，T-004 的覆盖栏没有列 | ✓（见残余 R-d） |
| RQ-010～015 | T-004；附录 A/B 的对账在 T-004，最终对账在 T-009 | ✓ |
| RQ-020～022、RQ-070、RQ-071 | T-008；判据在 T-009 全开 | ✓ |
| RQ-023 | 过渡期由原管线保持，T-008 迁移 | ✓ |
| RQ-024 / 附录 C | 过渡期 UoW 一命令一事务，T-008 填附录 C | ✓ |
| RQ-025 / 附录 H | 过渡期帧移进作业（T-004）；显式数据与 H 迁移在 T-008 | ✓ |
| RQ-030～038 / 附录 E / VC-1～6、8 | T-003（机制）、T-006（接线与逐项改写） | ✓ |
| RQ-036 的下限实测 | T-001 | ✓ |
| RQ-040～043 | T-004（执行位置）；组门在 T-005 删除 | ✓ |
| RQ-050～054 | T-002（机制）、T-007（贯通）；RQ-052 的取值在 T-001 | ✓ |
| RQ-060 / 附录 F / VC-7 | T-005；投递类在 T-006；最终对账在 T-009 | ✓ |
| RQ-061 | 部分在 T-004 开启，发件箱登记在 T-006，全开在 T-009 | ✓，但与测试驱动方式冲突，见阻断项 B-1 |
| RQ-072 | T-002，以及 T-009 收口 | ✓ |
| RQ-080 / 附录 D | T-001；附录 D 补全在 T-001，有序条目在 T-003 和 T-006 | ✓ |
| RQ-081～083 | 每个 Task 的 Verification，以及 T-009 | ✓ |
| RQ-090、RQ-091 | T-001 基线、M-1 预判、M-2 完整测量、T-009 最终测量；预热在 T-004 | ✓（M-1、M-2 不在依赖链上，见残余 R-a） |
| RQ-100 | T-009 | ✓ |
| AC-001～022 | 全部可经上表追到某个 T-xxx 或测量闸；AC-007 停机测试和 AC-015 在 T-004，AC-021 在 M-2 和 T-009 | ✓ |

没有无处落地的要求。

依赖顺序基本真实，有两点：
- T-005 先于 T-006 可行：串行加上「在执行器上直接 await 发送」已经保住 DR-16、DR-17 的顺序意图，所以删掉称号门与队伍流门后，到 T-006 之前不会出现窗口。
- T-007 对 T-006 的依赖偏保守，但单工作区本来就要顺序执行，不构成问题。

## 挑战裁定

| 挑战 | 独立评估的严重度 | 修正 | 裁定 |
|---|---|---|---|
| CH-1 持门生产者让执行器自锁 | **high**，已核实。`TournamentRewardSweepService.cs:38-47` 先取会话门 Exclusive，再 `SendAsync`；管线 45 只沿当前流的帧借用（`SelectedSessionExecutionBehavior.cs:26`） | 过渡期规则 1：作业包住整段，翻转放在最后。规则 2：门断言 | **confirmed → 已充分回应**。修正与最小建议一一对应，没有过度设计 |
| CH-2 判据晚于删锁 | **high**，成立 | T-004 开启 RQ-061 中不依赖发件箱的两部分，T-005 的进入条件改为零违规 | **confirmed → 回应方向正确**。但这个修正与 Plan 自己的测试决定产生了新矛盾，见 B-1 |
| CH-3 分离任务继承「在执行器上」 | **high**，已核实：`TouchSocketNetworkConnectionCloser.cs:18/26` 用 `_ = CloseDetachedAsync`，`:33` 是 `Task.Yield` | 规则 3：AsyncLocal 作业令牌，作业结束即失效；`SuppressFlow` 下分出的工作不继承；`RunContinuationsAsynchronously`；T-002 补两条反例 | **confirmed → 已充分回应**。挑战建议的第二条反例「关连接后生命周期 CMD 正常完成」由 AC-007 的线级用例承接（RQ-012 在 T-004），可以接受。残余见 R-c |
| CH-4 执行器生命周期与测试改写 | **high**，成立 | 首次入队即启动，不留旁路；会挂起的测试在第 3 步改写；假总线用例不作证据；附录 E 的证据必须经执行器和发件箱取得 | **confirmed → 已回应**。原先「统一经 dispatcher」与「第 7 步才改写」之间的矛盾已消除。但「假总线 / 直接 new handler 用例保留到 T-008」与 T-004 新开的判据冲突，并入 B-1 |
| CH-5 过渡期直接发送无期限 | 同意 **low**：分支收口前不部署，有用户停点 | 规则 4（2 秒期限后关连接）；M-1 的 (c)(e) 只作参考；新增 M-2；T-001 记录 handler 时长 | **confirmed → 已回应，力度相称**。规则 4 在「纯搬迁」一步里带进了一处可见变化，见 R-b |
| CH-6 发件箱归属与提交钩子 | 同意 **low**，本属局部设计 | 「发件箱的归属」一节：按作业为单位，钩子挂在 `DomainTransaction` | **confirmed → 已回应**。写在 Plan 层不算过度：它是 T-006 和 T-008 共用的边界 |

## 阻断项

### B-1：过渡期判据以「不在执行器上」为准，Plan 却让大量测试在执行器外驱动这些路径。T-004 的完成条件做不到，最可能的补法会让判据失效

**问题**

T-004 同时开启三类以「当前不在执行器上」为准的判据：
- 门断言（规则 2）；
- 写 SQLite 的工作单元开启；
- 附录 F 的玩法状态写入口。

T-004 的完成条件是「门断言和 RQ-061 判据在一次全量运行中零违规」。

与此同时，Plan 的「测试」一节允许「假总线 + 直接 new handler」的用例保留到 T-008，却没有规定：测试代码在执行器外开写事务、写在线身份、取真实门、直接调用 handler 时，这些判据怎么对待。

**证据**（基线 `a02bce46`）

- 测试代码直接调用非只读的 `.Begin()`：Application.Tests 604 处、127 个文件；Persistence.Tests 60 处、27 个文件；Protocol.Tests 30 处、8 个文件。`DomainTransactionScope.Begin` 直接在调用流上建环境事务（`DomainTransactionScope.cs:32-43`），所以这些都是「执行器外开写工作单元」。
- `tests/Support/TestSessions.cs` 在测试流上直接调用 `OnlineStateRegistry.RegisterConnection`、`Authenticate`、`Select`、`EnterWorld`，共 32 个文件使用。在线身份属于附录 F「改为执行器内访问」。
- 23 个测试文件直接构造真实门，其中 16 个在 Protocol.Tests，多为直接 new handler 的用例。
- 例：`TownProjectionHandlerTests` 直接 new `SetUserAreaHandler`，配真实的城镇区域与位置服务，从测试流调用 `HandleAsync`。这条路径会走到 `TownProjectionSlice.cs:368` 的 `CrossInto`。

**后果**

- 照现在的写法，T-004 打开判据后，上述用例会成批抛出，「全绿 + 零违规」无法达成。
- 在全绿压力下，最可能的局部补法有两种：
  - 判据只在「执行器已启动或已注册」时生效；
  - 加一个全局测试开关。
- 两种补法都正好让判据抓不到 CH-2 要抓的「漏迁的生产入口」。漏迁的入口在测试里同样跑在没有执行器作业的流上。这样 T-005 的进入条件失去意义，删锁之后的真并发要到很晚才暴露。这也违背 Plan 自己写的「不允许旁路」。
- 这个选择从 T-004 一直用到 T-009 的全开判据，以及 RQ-072 的反例，属于后续单元都要依赖、事后改代价高的共享边界，应当在 Plan 层定下。

**最小修改**

在共享决定的「测试」一节补一条，写明测试驱动与过渡期判据的关系。只需定以下三点，具体 API 留给 T-004：

1. 判据按**调用位置**判定，即作业令牌，不按「执行器是否启动或注册」放行。生产代码里不存在关闭判据的开关。
2. 测试代码要在执行器外开写工作单元、写附录 F 入口、取门、直接调用 handler 时，统一采用一种方式，并写明是哪一种：
   - 经测试辅助，把测试体作为一个执行器作业运行（推荐，判据保持完整）；
   - 或者用一个**只在测试程序集可见**的具名豁免作用域。选这种方式时，要配一条判据，钉住 `Server/src` 不引用它，并配反例。
   - 需要说明 Persistence.Tests 这类不装配执行器的纯持久化用例是否适用、为什么适用。
3. 「假总线 / 直接 new handler 可保留到 T-008」改为：保留的前提是按第 2 点运行。T-004 的工作量与测试数量加法要计入这批改动，必要时把 T-004 拆成多个 Task Unit，翻转仍然是最后一步。

这是一处 bounded 修正：路线、分层与其余共享决定都不变，不需要新的对抗挑战轮次。

## 非阻断残余风险

- **R-a：测量闸不在依赖链上。** T-005 的依赖只写了 T-004，T-007 和 T-008 都没有依赖 M-2。M-1、M-2 的「停止、回到用户」有可能被跨 compact 的调度跳过。建议在依赖栏写上：T-005 依赖 M-1，T-007 与 T-008 依赖 M-2。可以与 B-1 同轮顺手改。
- **R-b：T-004 的「行为不变」与规则 4 有出入。** 2 秒期限加关连接，等于提前落实了 VC-8，是 T-004 里唯一的可见语义变化。建议两点：
  - T-004 的 Task Unit 把它声明为本步唯一的语义例外，并配一条测试；
  - 超时以发送失败（异常）的形式抛给调用方，这样附录 E 各条路径在过渡期仍走今天的补偿、隔离语义，而不是「已关连接但调用方以为发送成功」。
- **R-c：只靠逐个列出分离点，有遗漏风险。**
  - 如果某个传输回调在作业流上同步触发，例如 TouchSocket 在发送失败时内联触发 `OnTcpClosed`，它会带着作业令牌。生命周期入口会被判成自我等待，清理丢失，只留下一条 `Error`。
  - 门删除（T-005）以后，未列出的分离点继承令牌造成的作业内并发，就失去了门断言这个侧面检测。
  - 建议 T-004 把传输回调入口当作流边界，进入时清除令牌；T-002 可考虑给令牌加一个「同一作业同时至多一个活动的内联流」的廉价检测。属于局部设计。
- **R-d：** T-004 的覆盖栏补上 RQ-006（定时源合并的落地）。
- **R-e：** 执行器「首次入队即启动」时，测试容器释放时要能停掉消费循环，避免 7872 个用例累积后台循环。属于 T-002 的局部设计。

## Spec 残余 R-1 / R-5 / R-6

Plan 第 61–64 行已澄清：
- R-1：关连接请求不属于任何工作单元，一律执行；
- R-5：DR-7、DR-8、VC-5、VC-6 只针对传输失败，组装失败按 RQ-031(d) 原样处理，并点名 GM Cera、房间机制、队伍三处，T-006 各补一条单元测试；
- R-6：钱包与 PvP 的重试删除由 VC-5 覆盖，组装失败按 (d) 处理。

三条都与 r3 审查的建议一致，没有引入新矛盾。

## 仓库约束

| 约束 | 判断 |
|---|---|
| 纯搬迁与语义改动分开 | 支持，但有一处偏离。T-004 是搬迁，T-005 删锁，T-006 改投递语义，T-008 改结构，证据互不混淆。偏离是规则 4，见 R-b |
| 「行为不变」要有能失败的证据 | 支持。T-001 先在基线上通过完整帧字节的表征测试；门断言和 RQ-061 判据把「无人争用」变成会失败的断言；附录 E 的证据要求经执行器和发件箱取得。前提是 B-1 修正后，判据不会被放宽 |
| 删净 | 支持。门断言随门在 T-005 删除；过渡内联规则在 T-008 末尾删除；直接发送期限被 T-006 取代；T-009 按附录 F 做检索为零的对账 |

## 结论

`changes_required`，`remediation: bounded`，`remediation_target: plan`。

- 路线、分解、共享决定与覆盖整体可用；CH-1～CH-6 都得到了相称的回应；Spec 残余 R-1、R-5、R-6 已澄清。
- 唯一的阻断项是 B-1。它是 CH-2 与 CH-4 两处修正叠加后出现的新矛盾：过渡期判据以执行器归属为准，而 Plan 允许、而且大量现存测试在执行器外驱动同一批路径。如果不在 Plan 层定下测试驱动方式，T-004 要么完不成，要么被判据旁路换来全绿。
- 补上「测试与过渡期判据」这一条共享决定（顺带修正 R-a）之后，按 bounded 复审只需核对这一处与它的直接回归。
- 下一单元 T-001 不受 B-1 影响，它的输入已具备。但按 TaskContract，实施仍要等用户在 compact 后明确开始。
- 本审查不代表整体完成。
