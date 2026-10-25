# Plan P6 独立对抗挑战

## 精确目标、依据与攻击范围

本记录由 Plan 作者及后续 reviewer 之外的 fresh challenge generator 按 `generating-adversarial-challenges` 生成，只提出应由独立 reviewer 裁决的问题，不给 Plan verdict。唯一工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`，只读核验 HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`。本记录创建后保持 immutable。

以下路径相对本子 Spec 目录：

| 制品 | bytes | SHA256 |
|---|---:|---|
| 目标 `plan.md` P6 | 89736 | `eaba6ae255cbcb8120148644744e06b6097b2ea717e143f24c01b7a96b48e043` |
| `verification/plan-p6-amendment.md` | 6993 | `f6920be220905ba1951a41b9a78a62f3bb04f9bab426c581206b1fca0eb96a47` |
| 原 P5 快照 `verification/evidence/plan-p6-amendment/plan-p5-before.md` | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| 权威 `spec.md` B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| `reviews/spec-b5-review.md` approved | 9675 | `e85be4d072f30e8c3b24c1965a5e6d90902d10559c6d812e9505fb1dc1a80e66` |

读取 child TaskContract、根 Goal／约束、B5、匹配 B5 review、P6／amendment、Task30 Result／review／Controller Consumption R1 完整末节，核实际 Transactions、CommandWorkUnits、CommandRequestContext、CommandBus、registration、Mailbox Domain／SendDomain／Delivery／fine／六 decoder、outbox／adapter、四份原 fine caller 测试与共享调用方。读取 Task30 final-03 十四场的原 Summary、阶段与相关源码，区分旧自结算、native Commit、C1／C2／C3及其未测范围；旧证据不是新机制的运行证明。工作区没有 `.codegraph/`，按 AGENTS 跳过 CodeGraph。

攻击聚焦 P6 所选 HOW：真实 action-abort fact 的来源／阶段／消费、直接领域 await 与 pending handoff 的先后、现有真实 outbox 的 admission 生命周期、同 cause 的独立二次发生、不可恢复失败的来源关闭、原五 consumer／三个 raw／共享即时原子性，以及六消息与八 fresh full／DFO 数量账。具体 Task31 白名单、局部方法拼写、结果类型细节不作为 material 缺陷。

保留集：**high-risk 1／low-risk 0**。

## CH-1: pending 在领域 await 失败返回后才建立，真实自回滚与 observer 之间仍能登记或提交第二事务

- Risk: high-risk
- Failure and impact: P6 第77行规定真实 AbortAction 完成原事务 rollback／Completion 后签 fact，第79～83行再由直接领域 await 的 observer catch 发交接并进入 pending。按当前真实 unit 生命周期，原 rollback 在该 await 尚未向 observer 传播时就已把 owner.open 清空。此时下层领域调用的同容器代理／finally 可以捕获原 SQL 或 MailException，调用原 outbox 的普通 Register，或通过原 DomainTransactionScope.Begin 开第二事务并完成持久写，再原样重抛最初 cause。whole 交给 Invoke 的 delegate 自身仍可以只有一次直接 await，没有任何 catch、登记或业务；故第79行的 delegate 约束并没有排除此下层窗口。observer 随后看到的原 fact 依然是同 Tx、同 call、同原 cause、ConfirmedRollback，原 lease 的 cleanup 仍可成功；若 barrier 此时才开始，其 guard 从未发生，runner 就可能恢复 ActionRolledBack／普通拒绝并保持健康。先前登记的消息已经是单元外存活条目，第二事务甚至已 durable，完整清理无法收回它们。这违反 B5 RQ-007／AC-004 的单个原边界／不得第二事务，以及 AC-005 的不可恢复失败不得正常完成；也使 P6 自称的真实登记／新事务屏障在关键时段不成立。必须在实施前确定来源发布与 admission 封闭的先后，否则仅测试 signal 已发出之后的 catch 逃逸会漏掉该条路径；届时修正涉及已选 Tx／unit／observer 协议，不能靠末尾增加一次 cause 比对补救。
- Evidence: `plan.md:77–85` 将 fact 签发、直接 await 失败观察、pending／barrier建立分处前后阶段；`plan.md:242` 要求真实 Register／BeginUnit 拒绝，但没有要求在 observer 尚未接到错误的窗口触发。实际 `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:233–249` 完成 rollback 后调用 EndUnit；`:226–230` 调 unit.Rollback。`Server/src/Dfo.Server.Application/Execution/Outbox/GameplayOutbox.cs:364–368、391–395` 的 Rollback→End 调 owner.Close，`:203–207` 立即将 open 清空；`:127–142` 的 BeginUnit／Register 因而恢复普通 admission，非 close 条目的 Slot.Unit 会是 null。`DomainTransactionScope.cs:37–51` 则把已 settled 事务视为 Current=null，并允许新 Begin；该新 Begin 正是经相同 outbox.BeginUnit 的真实路径。下层失败传播中存在 managed hook 并非假设的任意 .NET getter：Task30 已接受的 `verification/evidence/task-30-inspection/freeze-07/Observation.cs:152–162、191–208` 是同 provider 的实际 ObservedProxy，它在 inner ValueTask 失败之后、向调用者重抛之前执行 Failed／Observer；原 `MailboxSendDomainService.cs:56` 也把 mailbox await 包在直接领域 Send await 之内。可复现 probe 是在新 Task 自有的同 provider 领域代理 catch 内，确认 inner 已自回滚且 fact 已发布、outer observer 尚未建立 pending，分别尝试真实普通 Register 和 Begin→真实 SQLite 写→Commit，再重抛同一 cause。应要求两条尝试在最早 admission 就拒绝、保独立 guard，最终不得 ActionRolledBack／普通 ACK；不能只在 whole 捕获交接后注入。此 probe 本轮未执行，所述是由实际源码与 P6 明示顺序推出的攻击路径，不报告已发生的新产品 bug。
- Minimum correction: 在高层 HOW 明确：一旦本次受观察的真实 action 自回滚进入可发布来源事实的阶段，原真实 unit／owner admission 必须同步进入受保护状态，不能等最外层 await 的 catch 才启用；在释放 open unit 与向下层 caller 暴露回滚完成之间不得存在普通 Register／BeginUnit 放行窗口。observer 随后只消费／交接该状态，runner 在全部 cleanup／健康检查后才解除；守卫发生仍由显式 unit 交回 request，无需让 Tx／outbox 保存 request/job/session。补上述“下层代理已回滚、observer 尚未接错”的真实拒绝反例。局部 API 和实现仍留 Task31；不要求改 A 发送设计、原五 consumer 或共享 standalone／ambient 的既有即时结算。

## 其他已尝试攻击与未保留候选

- action SQL／Claim GoldLimit 与 native Commit19/787 混用：P6 已将独立 self 结果、签发点、原 cause／phase／occurrence、runner 自己 Abort 之前的事实及 KnownStorage/FallibleCommit 区分写明；没有另立同义 CH。CH-1 是事实发布到 observer 之间的可写窗口，不是要求业务自报更多证明。
- 已进入 pending 后 catch 交接再吞错／重抛旧 cause，以及 unit／lease／scope 再抛同对象：P6 已要求真实 guard occurrence／sticky、完整 cleanup 后才给结果，并保 CommandBus drain→Close→scope→一次 FailAsync；这些要求可在现有显式 request 处实现。CH-1 修正的是这些保护何时开始，不重复指摘已经列明的后续责任。
- 自授或跨协议借 SelfSettledAction：Plan 限真实 registration→bus→request、ordinary／ProtocolNonEffect／空 participants／None，并要求六消息五 CLR 唯一 owner 与拒绝反例；没有把公开业务声明当签发依据，也没有新增后簇 consumer。具体 metadata 成员留 Task 阶段不构成独立缺陷。
- C2 双因与共享 atomicity：实际 RunAsync 原无 cause Rollback 与 DomainTransaction 的 aggregate 分支解释旧缺口；P6 明确只补原 cause、保 Uncertain，保 ItemDelivery／QuestHelperPuppet 的 IsSettled 和 caught refusal 后 Commit 必 throw。GM 多目标 prepared／receipt／audit、Housing／Wedding及 LiveOnly 的强验证责任已列明，未把静态影响算端到端通过。
- 六路阶段／精确身份／scope 已登记消息：Plan 保 Send/Claim Prepare/gold 原事务内、各原 ACK／投影顺序、空 selected 与额外精确 current 门；C1/C2/C3保默认来源关闭／chain及 replacement 物理隔离，正常 disconnect 清理不误算新增结算围栏。未从旧 C3 slots=[] 推 whole 零帧。
- 删 fine 的四个测试 caller 及数量闭合：实际 Host 的两份 GM HTTP、Application 的 MailboxProtocolIntegration／ItemDelivery 仍强读 UID/core／资产／编码／stale 归属；P6 已分别要求真实 driver 和不可 wire 表达输入的 test-only 真实领域＋原事务边界，不以换 provider／预置 SQL 求绿。八 fresh full、9029历史库存、DFO 67＋7＝74、原五 consumer 与三个 raw 完整回归责任均明确，不因高层未预写逐命令另立 finding。

## 限制、交接与停止

只做只读源码／Markdown／原 JSON 核查和上述可复现 probe 的设计，没有 build、testhost、probe、native 或新运行证据。仅新建本 challenge 文件；未改 Plan／amendment／snapshot／Spec／Contracts／routing／Task30／A／产品／测试／ledger，无 git mutation，不创建或执行 Task31。独立 reviewer 应对 CH-1 的时序与最小纠正作自己的判断；本记录不自批 Plan、不批准实现，也不关闭 T-005／Spec002／分支。writer 已停止。
