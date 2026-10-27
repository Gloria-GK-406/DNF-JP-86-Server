---
kind: rks-review
verdict: approved
---

# Plan P4 独立审查：CollectionBox 第三即时登记出口

## 身份、目标与判定层级

本审查由 fresh 独立 reviewer 完成；本人不是 P4 作者、Spec 作者或匹配挑战的生成者，未继续委派。应用 `reviewing-work/SKILL.md` 与 `references/plan.md`，按 `mode: task-units` 的 **Spec 级高层 Plan** 判据审查：总路线、责任、共享契约、真实依赖、验证方法和下一有用单元应当可执行；不要求未来 Task 的完整局部实现或尚未产出的产品结果。

工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；分支 `task/refactor-session-20261021`；实际源码 HEAD `dba3ede68e15f892afd35534db9fed7ed00d04a8`。以下 B 指 `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，源码路径相对工作区根。

| 实际核对输入 | bytes | SHA-256 |
|---|---:|---|
| 唯一目标 `B/plan.md`（P4） | 61551 | `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7` |
| `B/verification/plan-p4-amendment.md` | 16801 | `a3683f7449fc4bf9414c06f30d2447c4731bcaa64aea4a7c8830f199d70101bf` |
| `B/verification/evidence/plan-p4-amendment/plan-p3-before.md` | 52018 | `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519` |
| 匹配挑战 `B/reviews/plan-p4-challenges.md` | 8036 | `7b23d10a1cd1eee526449351fb89e281645793b752b0de4423874d8a1c98644d` |
| governing `B/spec.md`（B3） | 32948 | `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| B3 批准 `B/reviews/spec-b3-review.md` | 8194 | `42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52` |
| `B/reviews/spec-b3-challenges.md` | 7006 | `d61c61fa5f8aad20a3518b45619878b38927027a9e280d3da881f5750cf80fac` |
| `B/verification/spec-b3-amendment.md` | 10325 | `8cdcbb991a4ccbe202a965bcac8433e6ee816804a8ba9a3b4519c007c2cb89b2` |
| `B/verification/evidence/spec-b3-amendment/spec-b2-before.md` | 29265 | `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228` |

P3 快照和 B2 快照分别与 `git show HEAD:<B>/plan.md`、`git show HEAD:<B>/spec.md` 逐字节相等；直接审阅 P3→P4 实际 diff 和当前目标，未仅依作者摘要或挑战计数。实读根/子 TaskContract 的 Goal、Scope B、SC-13、约束与授权，根 map 的 A seal、B 激活及持续约束，父 Spec RQ-023(c)/024，当前 AGENTS 的分层、显式请求/单元、注入、足迹、失败与测试身份规则，以及 ArchitecturePhilosophy/RefactorPreferences 相关结构约束。工作区没有 `.codegraph/`，依仓库规则跳过 CodeGraph。

范围仅为已批准 B3 增加的 CollectionBox 首次成功 Main 结束后原一次 exact physical batch 的 HOW 及直接回归；没有泛重审 P3 无关路线、旧 TASK-01～19、A 或 Spec 终局。

## 结论与决定性证据

**approved：本次受影响 Plan 范围内没有未解决的阻断缺陷。** 方法能交付 B3 批准的有界行为保留，且将仍待验证的风险放在产品交付前的同一后继单元内辨明。此批准仅通过当前 P4 的受影响 Plan 门，不证明第三出口已实现、测试通过、Spec 完成或下一 Task 已获主控指定。

1. **阶段、实际调用与来源形成完整路径。** 现 `CommandRegistrationAttempt.cs:7–38` 只有原两个 exit，`ExplicitEffectRequest.cs:54–59` 将非 required 一律视作 replay，`OutboxAdapters.cs:159–173` 的 batch 又硬绑 replay。P4 `plan.md:69–83` 对这三个真实冲突均有方法决定：新增机制层 `SettledInitialBatch`，状态穷尽 switch，batch 仅接受 replay/initial，required 单独固定，未知阶段/调用 fail closed；保 owner request/job、健康/closed、single-call、原精确物理对象和已结束单元检查。它不是只加 enum 或 metadata。现 `IGameCommandOutbox.cs:28–31` 与 `ICommandMessageOutbox.cs:12–15` 已有 opaque 参数通道，原对象透传可行，不需 Abstractions 引用 Application enum、公开 phase 参数或另一凭据框架。

2. **许可不会代替实际 encoder 来源，默认策略保留。** 实读 `OutboxAdapters.cs:66–85,175–191`：target、取消、batch 拷贝、outbox.Current、实际 EncodeBatch 和最终 Register 是不同位置；required 的 buffer 写也不等同于 encoder span。P4 仅允许 actual adapter 窄 encoder catch 记 cause，typed 转换、callback、业务 Full/clock、current/lookup/guard/Register 均不能盖章。Application 只知注册与机制阶段，CollectionBox whole/message 绑定留 ProtocolModule；普通无 token 重载不变。真实注册→签发→typed Game→opaque 接缝→actual adapter→encoder→最终收口的责任归属与项目引用方向一致，未增加 request ambient、DI current、job slot、事务取请求或第二 Host。

3. **CollectionBox 迁移能保持原用例与事务分段。** 实读 `CollectionBoxSlice.cs:18–38,50–58,103–151`、`ProtocolModule.cs:326–333`、`SelectedSessionSettlementBehavior.cs:18–25` 与 `EffectOperationContext.cs:102–107`：原 fine 是一个 Transactional Main，主结算返回后 handler 才组帧并一次 batch。P4 `:81` 明定一次 Main Write 内 `DemandActive → ExecuteAsync → RecordInventorySlots`，Select 最低 CollectionBox、其它 action 最低 Inventory|CollectionBox；保原四类 catch、NoRoom 过滤、拒绝码、空 actor、前后 current、循环 mapping 与逐条 clock。正常拒绝回滚在 action 内以既有 RequireRollback(cause) 完成，其 guard 不吞；一般 SQL 原未 catch，继续外抛，不借 P3 commit-result 强恢复。只有真实非空成功结果、Main 及清理结束、原库存→Full/selected重复→ACK 组帧完成后才签 initial 并对原 physical 一次 batch；拒绝无 permit。whole 的非空结果类型及详细 driver 留临近 Task，符合本高层 Plan 的合法分辨率。

4. **最终失败资格仍在全部清理之后决定。** 实读 `CommandBus.cs:86–128`、`CommandRequestContext.cs:73–162`、`CommandWorkUnits.cs:160–197`，当前已有 scope 仍活着时 drain、Close、scope cleanup、一次 FailAsync 的收口。P4 `:85–87,202` 保持它，未退回提前按 MainCommitted 决定 raw/typed。raw 仍须最终只剩同一 encoder cause、无 typed/额外独立失败；独立 unit/scope cleanup 即使再次抛同一对象也撤销资格，保发生事实、各因和全部精确参与者围栏。P3 commit-only/来源 GuardFailure/RequireRollback 两因保留、默认 Write/ReadOnly sticky、旧两许可及 Compose 禁用均未被本次扩展替换。

5. **先旧后新的直接证据与共享核心验收足够具体。** `CollectionBoxNetworkSettlementTests.cs:31–50,118–145` 确实手造 request 直达 dispatcher、只断言 IOException 类型；`:66–109` 的 replacement hook 没有证明替换发生。`RecordedOutbound.cs:328–391`、`OutboundRegistrationParts.cs:88–101` 与 `NpcShopPurchaseTests.cs:575–595` 确认注错可沿同 provider wrapper 的 inFlight 到 actual registration encoder，而 `RecordedOutbound.cs:423–427` 不物理关闭 socket。P4 `:198–206` 要求先真正旧 decoder Select/Put 固定 cause，再新 decoder/whole 同 provider 对应复跑，并直接观察真实 entry/原 Ordinal、Committed、失败整 batch 未登记、无新增 fence、source-close/current replay及不二结算/不原 ACK；另补真实 replacement 触发事实。有效 provenance mutation、默认/错 phase-call-kind/跨 request-job-target/closed-leaked-reuse/catch 后保护及独立同对象 cleanup 都明确。共享核心后新鲜 build 0/0、八 full、DFO74＋七 actual controls、Golden15/Characterization10、逐 case 数量账没有被三 full＋四 controls 替代；8828 及另1800 Task15 库存也未冒称 P4 新跑。

## 六条攻击路径的独立裁定

匹配挑战集没有保留 CH。我仍按实际源码和当前 P4 逐项复核其六条路径；以下不以“0 high/0 low”代替批准，也不取消门后验证。

| 攻击路径 | 裁定 | 依据与处理 |
|---|---|---|
| 只加 enum，adapter 仍硬绑 replay；未知分支误放行 | rejected | `:71–79` 明确穷尽 state 与真实 batch/required 调用矩阵，实际硬绑点被点名替换；`:202` 有双向错用反例。局部内部方法拼写 deferred 给详细 Task。 |
| initial 借给拒绝/业务/Register/其它协议，或绕 wrapper 自报 encoder | rejected | `:81–83,104–105,114` 将真实成功组帧与签发/窄来源分开，保持同 Registering/actual adapter/outboxes；`:198–202` 要真实触发、默认对照和有效 mutation。没有许可扩大决定。 |
| old/foreign attempt 只污染 owner，新 caller catch 后仍写或正常 return | deferred | 实际 `Enter:24–30` 只 Failed owner，`DemandRegistrationCurrent:47–54` closed/job 拒绝直接 throw；现 `ExplicitEffectCommandTests:357–412` 不足以证明新 caller catch 后禁写。P4 `:204` 要在同 provider 真实新请求内先 catch 再 actual write/return，同时观察两端；若可继续则在现显式边界最小修正并 RED→GREEN，若已有守卫阻止则保区分证据。这是实施前可便宜辨明的局部安全义务，未把假设写成已证缺陷，也未许可带缺口交付。 |
| 提前 raw/typed，独立 cleanup 同对象被传播去重掩盖 | rejected | `:85–87,202` 保完整 drain/Close/cleanup 后一次终判，明确独立发生不按引用去重。当前实现已有对应收口与独立清理记录，扩展无须改变中心方案。 |
| whole 吞普通 SQL、扩大 catch，破坏 Main/footprint/帧序或把拒绝当成功 | rejected | 原 action、最低 needs、四 named catch/NoRoom、ordinary SQL、空 actor 与真实 result 分支均由 `:81,149,198–202` 约束；未新增 StorageFailure 或 commit-result 恢复。 |
| 旧间证/未触发 hook/三 full 库存被当全验收，成功 Withdraw 或 socket 被外推 | rejected；成功 Withdraw 为 residual-risk | `:198–206` 明确证据层级、旧 decoder 先行、实际 hook/encoder、可运行 mutation、新鲜八 full 与数量身份；Discard 不真关 socket。`CollectionBoxDomainService.cs:106–118` 支持 Withdraw 静态共用成功出口，但未动态注错须继续披露；不能外推已测。 |

对第三条的安全后置判断是本次审查的有意决定：P4 已固定要求和失败后的纠正边界，详细 Task 可以在新出口交付前以真实持久写探针分辨；尚未运行 probe 本身不是高层 Plan 缺陷。它也不是允许无条件保留现代码。若探针显示最小显式边界无法满足 B3，或修正需要突破请求/机制约束，须按 `:236` 返回主控重审，不能添加隐式 current 通道或暗扩范围。本批准不预判探针结果。

## 控制、保留与交接

P4 的 diff 限定 Planning Choice、受影响共享方法/能力、T-004、验证、Next Planning Boundary 与修订说明；未重写已接受 Task 或 A。计划为 task-units，T-004 合并真实消费者与最小共享 phase/adapter 以防空壳；其它工作包与最终路线保持。当前下一 useful unit 为 CollectionBox，而不是旧 P3 的四 opening；历史库存/旧下一 Task 的文字已明确标为历史。详细结果形状、driver、精确技术白名单与方法名可在主控指定的临近 Task 安全收敛，不构成额外前置门。

实读 routing 的 `plan-review: auto` 与 `implementation: auto`、继承授权及 Artifact overrides 仍指旧门，P4 `:13,234–236` 正确要求由主控完整消费 B3/P4 匹配 gates、更新 routing、完成 amendment-only 本地检查点后，再指定下一 Task 详细规划/产品。作者 amendment 的 `decision: manual_required` 自查标签已由其正文限定为交付核对，不是该 routing 的 workflow gate，也没有权力新增用户 HITL；本次未发现它造成实质冲突，无须为格式另增停点。主控仍须更新匹配 gate 路径，不能沿用 P3 批准直接实施。

本轮只做静态源码/文档/diff、只读 Git 和 bytes/SHA 核对；没有 native、probe、build、test、产品/Task20/routing、stage/commit 操作。唯一写入是新 `B/reviews/plan-p4-review.md`。受审目标、匹配挑战、原材料保持原 bytes/SHA，HEAD 保持上述值，index 为空；Server/Tool 无本轮差异。批准不覆盖尚未运行的旧/新 oracle、guard 探针、mutation、八 full 或成功 Withdraw 动态结果。交付本记录后停止全部共享写入，由主控消费。
