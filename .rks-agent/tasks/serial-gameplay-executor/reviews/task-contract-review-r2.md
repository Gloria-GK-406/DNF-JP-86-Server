---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/TaskContract.md
target_revision: 2
verdict: approved
reviewer: independent subagent (fresh context; reviewing-work deliverable + modeling-task-contracts outcome/mechanism, steps 5-10, readiness)
---

# TaskContract 复审：serial-gameplay-executor（revision 2）

## 范围

- 目标：`.rks-agent/tasks/serial-gameplay-executor/TaskContract.md`，frontmatter `revision: 2`、`state: ready`。
- 重点：
  1. r1 阻断项（SC-8「线级集成测试原样通过」与删净冲突）是否消除；
  2. r2 吸收的非阻断修改是否引入新的歧义、冲突、过度约束或越权；
  3. 完整重读后的 ready 条件。
- 只审查，不修改契约或代码。不审查 Spec、Plan（尚不存在）。

## 依据

- `reviewing-work` 的 Deliverable 参考；`modeling-task-contracts` 的「Separate Outcomes from Mechanisms」、Create or Revise 第 5–10 步、「Readiness」。
- 上一轮：`reviews/task-contract-review.md`（r1，changes_required）。
- 证据：`research/01`–`05`；`AGENTS.md`；`Docs/Agent/RefactorPreferences.md` §3。
- 代码基准：worktree HEAD `a02bce46`。下文 `src/`、`tests/` 均相对 `Server/`。
- 用户原话按契约 Source Request 与 Authorization 的记录采信，审查者看不到会话原文。

## 抽查核实

| 契约里的说法 | 核实结果 |
|---|---|
| 口令哈希 PBKDF2、22 万次迭代 | 属实：`src/Dfo.Server.Application/Authentication/PasswordCredentialService.cs:16` 为 `220000` |
| 重计算只出现在 HTTP 侧 | 属实：`IPasswordCredentialRuleService` 只被 `AuthenticationDomainService` 与 `GmAccessDomainService` 注入；`GameLoginHandler` 走票据，不做哈希。所以「重计算在执行器外完成」与「handler 只解包入队」不冲突 |
| 跨连接顺序：离开副本同伴先于领队 ACK；进本跟随者先于领队 | 属实：`LeaveDungeonSlice.cs:283` 注释；`DungeonEntryProjectionDelivery.cs` 的 `IsolatePartialCohortAsync` |
| 选角金样本不经工作流 | 属实：`SelectCharacterAcceptedTranscriptTests.cs:454` 只经 `SelectedCharacterStateMessageFactory.Create` 与 `ProtocolModule` 注册的编码器 |
| RI 位置引用（Scope In 第 1 项、Scope Out 第 2/3/6 项） | 与正文序号一致 |

## r1 阻断项处置：已消除

r2 的 SC-8 几乎逐字采用了 r1 给出的最小改法：

- 线级集成测试只要求「对线上字节、帧序和连接开关的断言不变」；
- 「只因准备数据或测试接缝被删除而改动的文件，逐个列出并说明断言未改」；
- 「以被替换的发送或分发语义为测试对象的用例」并入「测试数量与断言变化逐条说明」。

对照 r1 列出的三类必改文件：

1. 用细粒度命令准备数据的 4 个文件（`GameLoginIntegrationTests`、`BossTowerFrameIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`CardRewardReconnectIntegrationTests`），以及打桩领域服务的 `BossTowerFrameIntegrationTests`、替换 dispatcher 的 `UnknownInboundFrameIntegrationTests`，都落在第二条。
2. `TouchSocketNetworkMessageSenderExplicitDeliveryTests`、`OutboundSendFailureTests` 测的正是要换掉的发送语义，落在第三条。
3. `HandlerFailureConnectionIntegrationTests` 替换 dispatcher，断言「handler 抛异常即关连接」，落在第二、三条。

合规实现不再必然违反 SC-8，证据（逐文件清单、断言对照、数量加法）可观察、可达成。阻断项消除。

## 对 r2 新增与改写内容的检查

| 改动 | 判断 |
|---|---|
| 术语澄清（CMD 是执行单位，不是 `0x01`；「出站消息」覆盖 `0x01`、`0x00`、Channel） | 方向正确，SC-1、SC-3 已改用「出站消息」。但 Goal、Constraint 1、Scope In「判据」三处仍写「发 NOTI」，见非阻断 1 |
| Goal 改为「除经审查的 Spec 逐条列出的有意变化外」 | 与 Constraint 3 一致，r1 非阻断 3 已解决。但 SC-8 没有跟着加这个例外，见非阻断 2 |
| 重计算与背压 | 「CPU 密集计算在执行器外完成，结果再作为 CMD 进入」是机制，但依据是实质风险（未认证、无限流的启动器端点可拖慢全服），几乎是唯一自然的做法，成本低，可以留在契约层。背压写成「等价的背压或上限」，是结果层表述，合适 |
| 选角端到端证据（Scope In 测试项、SC-8 末句） | 可观察。时序见非阻断 4 |
| SC-1 改写（列出执行器外部分） | 解决了 r1 非阻断 4。列表用「包括」，不是穷举；「重计算」也在执行器外但没列入，属于 Scope In 已说明的部分，不构成冲突 |
| SC-3 改写（按结果验收，覆盖三类出站消息） | 解决了 r1 非阻断 2、5。「慢连接不拖累」与「跨连接顺序」之间有张力，见非阻断 3 |
| SC-4 改写（其余并发原语逐项去留） | 解决了 r1 非阻断 10。「其余并发原语」的颗粒度见非阻断 5 |
| SC-5 改写（LiveOnly 离线、旧进程、过期时跳过） | 与 AGENTS.md 一致，r1 非阻断 9 已解决 |
| SC-11 在规划停点列明三项默示决定与可见变化 | 与 RI-4、RI-8、RI-9 一一对应，r1 非阻断 1 已解决；证据可观察（规划停点汇报）；不新增授权 |
| RI-9 补原话 | Source Request 已录入用户问 MySQL 的原句，RI-9 的引文与之一致 |
| 授权与停点 | 未改动，仍准确；SC-11 和 Assumption「不达标先回到用户」都是向用户汇报，不是越权 |

## 新发现

### 阻断

无。

### 非阻断（交给 Spec / Plan，或在下一次修订契约时顺手改）

1. **术语澄清只做了一半。** Goal（「会改游戏状态、写库或发出 NOTI 的工作」）、Constraint 1（「任何会改游戏状态、写库或发 NOTI 的路径」）、Scope In 判据（「改状态、写库、发 NOTI 只在执行器上进行」）仍写 NOTI。按 AGENTS.md，NOTI 只指 `0x00`，而术语注释只重定义了 CMD，没有重定义 NOTI。好在 SC-1、SC-3 按「出站消息」验收，覆盖更宽，验收上不会漏。Spec 写执行器判据时应按 SC-1 覆盖全部出站消息；下次修订时把这三处改成「出站消息」。

2. **SC-8 没有承接 Goal 的「Spec 列出的有意变化」例外。** Goal 和 Constraint 3 预见到落线顺序、断线时机可能有意改变，而 SC-8 第二句对线级测试的帧序、连接开关断言写的是绝对「不变」。第三句只豁免「以被替换的发送或分发语义为测试对象」的用例。如果 Spec 列出的某项有意变化恰好被一个业务线级用例断言（例如 `GameLoginIntegrationTests.LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect` 依赖同步投递失败后的清理与断线，它可以归入第三句，但别的用例未必能），SC-8 就会与 Goal 冲突。本审查的读法：SC-8 的「不变」服从 Goal 的例外条款，经审查的 Spec 逐条列出的有意变化，在线级用例上的断言改动归入「测试数量与断言变化逐条说明」。Spec 一旦列出这类变化，应在 Spec 里点名受影响的线级用例；下次修订契约时把例外写进 SC-8。

3. **SC-3 里「慢连接不拖累其他连接」与「跨连接顺序成立」有张力。** 今天「跟随者先于领队」靠的是对跟随者的发送 `await` 返回（交给管道）后再发领队。每连接独立队列下要等价保证，领队的帧就得等跟随者队列推进到那一条，跟随者慢，领队就被拖。合理的达成方式是：「不拖累」指无顺序依赖的连接；有顺序依赖的一组连接设等待上限，超时按今天的失败处理（隔离或关连接）。Spec 应写明这条边界和上限，否则 SC-3 按字面无法同时满足。

4. **选角端到端证据要在动结构之前钉住。** 要证明「行为不变」，这份证据必须先在基线上建立并通过，再拿去对比改造后的结果（`RefactorPreferences.md` §3.4「风险大的先钉行为」）。改造后才写的端到端用例只能证明「现在这样」。契约没有规定顺序，Plan 应把它排在选角迁移之前，并覆盖失败补偿路径。

5. **SC-4「其余并发原语逐项给出去留」的颗粒度。** 调研 04 的「其他并发原语」表还包括 Interlocked、volatile、ConcurrentDictionary、Channel 等按类计数的条目；连同 Scope Out 保留的 190 处短锁，若字面逐个对账成本很高、价值很低。Spec 可按「类型 × 所有者」分组给去留；括号里点名的几把信号量逐个给。

6. **「codec 向量原样通过」的边界。** 调研 05 把 9 个调用通知 handler `ComposeAsync` 的文件算在 codec 测试里（如 `tests/Dfo.Server.Protocol.Tests/Game/BuyItem/BuyItemCodecTests.cs:30`）。如果 Spec 改掉通知 handler 的组装接口，它们就要改，而 SC-8 要求 codec 向量原样通过。这不是必然冲突（组装接口可以保留在 CMD 内部使用），但 Spec 应明确：「codec 向量」指纯编解码向量；调用 `ComposeAsync` 的用例若改动，按「逐条说明」处理。

7. **Current Situation 里有一句残留的过度断言。** 「线级集成测试、codec 向量、选角 62 帧金样本可以原样作为『行为不变』的证据」与 r2 自己的 SC-8、调研 05 的例外清单、r1 非阻断 6 都矛盾。它只是现状描述，验收以 SC-8 为准，但 Spec 作者可能被误导。下次修订时改成与 SC-8 一致。

8. **背压没有直接的验收测试。** Scope In 要求「等价的背压或上限」，SC-9 只要求灌包情况下的测量数字。建议 Spec 在 SC-3 或 SC-9 的证据里加一条用例：单连接灌包时队列有界，其他连接仍得到服务。

9. **Context 里的修订过程段。** 「revision 2 吸收了独立审查……」一段属于过程记录，`modeling-task-contracts` 要求契约只记要达成什么、不记工作流。不影响执行，下次修订时可删。

## Readiness

- Goal 与 Scope 与对齐的执行模型一致，无歧义到影响方向的程度。
- 一行 `task` 授权，原话完整，覆盖调研、契约、路由、Spec、Plan 与规划检查点提交；实施等用户 compact 后明确开始；不含合并、推送、PR、远端 issue。
- 每个 SC 都有可观察证据；SC-8 已可达成。
- Root Intent Coverage 每条意图出现一次，处置值合法，没有 `unresolved`；Scope Out 的每项交叉都有处置（Out 1↔RI-4，Out 2↔RI-8，Out 3↔RI-9，Out 6↔RI-12；Out 4、Out 5 不是来源意图）。
- 没有 Open Questions；假设可见，且有「不达标先回到用户」的出口。
- 新增的机制约束（重计算放在执行器外、选角端到端证据）都有实质风险或仓库规范作为依据。
- `revision` 已从 1 递增到 2，r1 审查随之失效，本轮即为对 r2 的独立复审。

## 结论

`approved`。

r1 的唯一阻断项已按最小改法消除。r2 吸收的非阻断修改没有引入越权，也没有让任何 SC 变得不可达成。剩余 9 条非阻断风险中，第 2、3 条最值得在 Spec 阶段先处理：SC-8 要承接 Goal 的有意变化例外，SC-3 要界定「不拖累」与跨连接顺序的边界。其余可在 Spec / Plan 中处理，或在下次修订契约时顺手改正。本批准只针对 TaskContract 门，不代表完成验证。
