---
kind: rks-review
verdict: approved
---

# Plan P2 有界独立评审

## 目标、身份与准则

Reviewer：fresh independent `b_plan_p2_review`，非 producer `b_roster`、非 generator `b_plan_p2_challenges`；未委派。工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，本轮一次只读 HEAD 核对得到 `2d8081ae2a3ef687e88ec284df9e97588cc625a7`。

实际读取并在落笔前再次核对的三个目标：

| 制品 | SHA-256 |
|---|---|
| `../plan.md` P2 | `0dd2dc61f5a6fa606e0fc308d2339cd578b7f543c87de9d55bc3ec9d29dc3212` |
| `../routing-decision.md` | `54a0dcf923faa180316ddda5e31d18052c937f709c24a2cdac677c4ebb817f33` |
| `../verification/plan-p2-amendment.md` | `14a1cb4dca0d0dda5c00b533d18403b1048d61c414ffe96285861f2eee11625e` |

应用 `reviewing-work/SKILL.md` 与 `references/plan.md` 的高层 `mode: task-units` 准则：判断共享契约、责任边界、依赖、集成检查和下一有用单元能否交付批准结果，不要求远期 Task 详案或提前实现。使用 `using-rks-agent` 的 bounded-subtask 规则，不重新路由父任务。已读本工作区 AGENTS 及两份架构／重构偏好资料；工作区根无 `.codegraph/`，按仓库规则直接只读源码。

Governing 为子 `../TaskContract.md` r1 Goal、SC-8B、SC-13，根 `../../../TaskContract.md` r4，父 `../../../spec.md` RQ-023 开头及 (b)/(c)、RQ-024、RQ-031(c)、AC-009，以及已独立批准的子 Spec B2 RQ-004～009、AC-002～005。实际核对 B2 SHA `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`、B2 amendment `ca46e1cef8daf0e5d3c717986e2c2818264b8c60a82a471e18371e0b365bab87`、B2 challenge `abf3aa29bfe319e44daa7b36266473b52af22168a7dc0d2954106bf2af71e990` 及 independent approved review `2803cb6d90907c6cadb36510155d2d678d3ad4f409a9426a585e23dc867e7bfb`，仍匹配。原 `effect-registration-boundary-review.md` 与 `spec-b2-review.md` 只作为冲突／已批准边界输入，以下结论回到当前源码独立判断。

范围限 P2 新增 §3／4／5、T-004、对应 Verification／Next Planning Boundary 与四个 gate override；不重开 P1 其他路线或已批准 B2 WHAT，不重新验收前十 Task，不改 sealed A。

## 裁定

**approved。** 本精确范围没有尚需阻断高层 Plan gate 的实际缺陷。P2 已解决下一效果单元依赖的共享设计决策，其具体 API 与局部测试编排可以在主控授权的临近 Task 中落实。

1. **来源证明可沿现有分层落地。** 请求在健康、精确 job／物理目标、注册具名出口及完成阶段下签发一次调用 handle，经 typed Game adapter、Abstractions opaque 契约直达实际 Application adapter；内部 sealed 实现及无公开自报入口，足以将“持有许可”和“实际 encoder 出错”分开。Application 仍不认识 Protocol CLR，句柄只作方法参数，不添加 CMD 注入 factory。当前 encoder 调用点与校验、lookup、buffer、callback、outbox.Register 可明确分离，窄 catch 有实际落点；没有要求通用 receipt 框架、第二发件箱或 ambient 请求。
2. **最终收口次序处理了真实冲突。** 现有两次 FailAsync 会提前 typed／fence，P2 明确替换这一时机：先记录 sticky 失败，scope 存活时 drain 所有已启动单元，再按原生命周期 Close／dispose，收齐独立失败后一次裁决，最后由既有 job end 处理 outbox。独立 unit／dispose 阶段即使抛同一个 Exception 对象也撤销 raw，不以 Includes 或引用相同去重放行。原 typed 不确定性继续保留；普通 postcommit、未知来源、守卫和额外失败仍全参与者围栏。catch 不能恢复写权或成功结束。
3. **账本及 replay 保持位置等价。** 抽出的 admission 只返回原 entry／具名决定，新旧共用原唯一 CWT，不经旧 Dispatch 执行业务；ConfirmedRollback 保原 Ordinal，Pending 结束拒绝及 128 窗口不变。新 ledger／reader／sink 包括 reader 内部 Capture 都显式携带 metadata／recipient，旧 LegacyReplay 仅作有删除账的旧 route 入口。Replay 普通 Write 内直接读领域快照，之后事务外 MirrorMerge → Create → batch → Town；不 nested bus、不重开 Main、不重复原业务／ACK、不启用 Compose 或延迟编码。
4. **具名兼容出口没有扩张为所有登记失败豁免。** 只有 UseStackable postSettled RequiredExplicit 和 CommittedReplay postWrite CurrentStateBatch，且真实 encoder 出处与最终单一原 cause 条件全部成立才保 raw、Committed、无新增 participant fence。前后业务组装、Town、callback／转换／lookup／Register 错误都不取得该证明，Required／BestEffort 原规则保持。MonsterDeath、祭坛及普通 postcommit 的默认 typed 全员保护明确保留。
5. **验证及下一单元边界有用且可执行。** 原两条 replay 测试的持久数量、帧字节和不重 ACK 断言保留，真实 encoder 注错位置保持；补 cause 身份、Committed、无新 fence、source-close 顺序、同对象独立第二故障、跨 request／job／target／closed／leaked、catch 后禁止写和可编译来源 mutant。RecordedOutbound 的新旧接口必须 alias 同一 Registering／provider／outboxes，保 inFlight／callback／FaultingEncoder，而不是绕 wrapper 获得假绿。T-004 消费 Task09／10 已验骨干、按实际调用者留账；没有将详细 Task11 或未实现 Main 当本轮缺失前提。

## 决定性源码与现有证据

以下路径相对工作区，均实际读取；未将 producer 报告或 0CH 代替独立检查。

| 证据 | 对本裁定的意义 |
|---|---|
| `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs:9–18,47–62`；`Commands/CommandDispatchingExtensions.cs:65–88` | 已有注册身份、来源和 typed binding 冻结接点；具名机制许可可附于该接线而不让 Application 识别具体 Protocol 类型。当前仍拒绝 explicit effect。 |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:61–79,88–153` | EncodeBatch 与 required direct EncodeMessage 在登记点执行；buffer／Register 可在 catch 外，BestEffort 原 catch 逐目标继续。P2 的窄来源证明具有明确实现位置。 |
| `Server/src/Dfo.Server.Protocol/Game/IGameCommandOutbox.cs:6–23` | 现有 typed Game → Abstractions 适配链可显式透传 opaque 参数，保 CMD／NOTI 约束和依赖方向。 |
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs:90–109`；`Transactions/CommandWorkUnits.cs:66–104`；`Commands/CommandRequestContext.cs:52–71` | 当前提前 FailAsync 和 Includes 的真实风险；P2 指定先收齐阶段失败再裁决，不能原样沿用提前包装。现有 Track／Drain 提供复用起点，不需要全局追踪框架。 |
| `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:16–74` | 原唯一物理连接 CWT、LatestOrdinal／Window、Committed replay 和 ConfirmedRollback 不更新 Ordinal 的事实。纯 admission 抽取可以保原 identity／entry。 |
| `Server/src/Dfo.Server.Application/Commands/SelectedSessionSafety.cs:29–44`；`Effects/ReplayStateDomainService.cs:40–47` | reader 内部确实仍有 implicit Capture；P2 点名贯通此处显式输入，避免只改外层。 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs:788–789`；`Protocol/Game/ReplayStateProjectionDelivery.cs:30–61` | replay 原注册 Transactional，之后才 Merge／Create／batch／Town；普通 Write 后事务外 sink 是等价路线。 |
| `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:346–376,399–402`；`Application/Commands/Pipeline/SelectedSessionSettlementBehavior.cs:18–24`；`Application/Effects/EffectOperationContext.cs:88–107` | 原 Main 返回前 Freeze／Settled／Committed，其后才业务组装及 required direct 即时登记；许可不能覆盖此前业务组装。 |
| `Server/tests/Support/RecordedOutbound.cs:71–87,317–369`；`Support/OutboundRegistrationParts.cs:82–101` | Registering 包 actual adapter，inFlight 决定真实 FaultingEncoder 注错；当前新窄接口尚须同实例 alias，P2 已明确安排而未冒称实现完成。 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs:97–126,219–227` | 原扣一次、后续当前数量及实际帧字节、不重 ACK 是必须保留的真实断言。 |
| `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:455–491`；`Protocol/Game/Altar/AltarProjection.cs:93–107`；`Server/tests/Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs:265–317` | 原受保护发布及普通 postcommit／rollback cleanup 双故障全参与者保护，P2 的默认规则和负对照未削弱。 |
| `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs:294–301`；`Execution/Outbox/GameplayOutbox.cs:213–240` | 最终作业结果仍交原 end hook；已提交条目按序刷出，最后 source close，与 participant fence 分开。 |
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs:126–127`；`Commands/CommandRequestContext.cs:97–99`；`Transactions/CommandWorkUnits.cs:41–44`；`../tasks/TASK-10.md:221–250` | 新 effect／Main 仍 disabled，Task10 接受的是参数／来源骨干及其既有验证；本 Plan 不把前十 Task 扩大成新机制通过。 |

## 挑战处置、routing 与推进边界

匹配 `plan-p2-challenges.md` 的三个目标 SHA 与本次实际目标一致，保留 high-risk 0／low-risk 0，故没有待逐项裁定的 CH。对其已尝试的路径独立复核结论为：业务／callback 冒充 encoder、提前 raw 判决遮蔽独立清理失败、catch 恢复权威／重试延窗、reader 暗读旧请求、窄接口绕过注错 wrapper，均 **rejected（作为当前 Plan 缺陷）**；依据分别为上述 P2 共享约束及对应源码落点。这里不是宣称实施已排除这些风险，后继 Task 仍须按 Verification 给出证据。

主控提供 routing 唯一只读 diff：原 `Artifact path overrides: none` 一行被本次说明及四个路径替换。reviewer 将当前该区块替换回主控提供的原文，在内存中重算得到 `fdc76959d36082a8ef6d8663d14c1af1ebd08c86f074bf9362fe250605233bb2`，与 P2 amendment 所绑定原 routing SHA 完全一致。因此除该区块外字节保持；controller／profile／gate 强度、Contract、baseline、Bound methods 与其他默认路径未变。本轮四路径分别定位 B2 challenge／review 和 P2 challenge／review，没有新增框架或审批强度。

P2 approved 只允许主控消费本次受影响 gate 并进行 **amendment-only 本地检查点**；之后才由主控给出精确 Task 路径、授权临近详细规划和首次 Effect/Main 产品工作。前十 Task 与原 P1 审查保留，不回溯否定其批准范围；不提前迁 BuyItem 双 CLR 族、不封存 Spec、不重开 sealed A、不推送／PR／部署或启动产品。

本轮未运行 native、build 或测试，未执行 git mutation；有一次只读 `git rev-parse HEAD`，其余仅静态读取与内存 SHA／routing 还原核对。唯一写入为本新 review，写后停笔。无未决 blocking finding 或本阶段缺失先决。
