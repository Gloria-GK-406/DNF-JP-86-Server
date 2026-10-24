---
kind: rks-review
verdict: approved
---

# Spec B2 有界独立评审

## 目标、身份与准则

- Reviewer：fresh independent `b_spec_b2_review`，非 producer `b_roster`、非 challenge generator `b_spec_b2_challenges`；未再委派。
- 精确目标：本子目录 `../spec.md` B2，实际读取并核对 SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`。
- 配套 amendment：`../verification/spec-b2-amendment.md`，实际核对 SHA-256 `ca46e1cef8daf0e5d3c717986e2c2818264b8c60a82a471e18371e0b365bab87`。
- 匹配挑战：`spec-b2-challenges.md`，所记目标及 amendment SHA 与实际目标一致；其 high-risk 0／low-risk 0 仅是挑战结果，不作为批准依据。
- 工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`。产品检查点 `2d8081ae2a3ef687e88ec284df9e97588cc625a7` 由主控指定；本轮依授权未运行 git，自行读取工作区实际源码。
- 应用准则：`reviewing-work/SKILL.md`、`references/spec.md` 的结果／范围／约束／验收一致性、WHAT 与内部设计的区分、实际 material defect 门槛及最小充分修正。`using-rks-agent` 的 bounded-subtask 规则适用，不重新路由父任务。
- Governing：子 `../TaskContract.md` r1 的 Goal、SC-8B、SC-13；根 `../../../TaskContract.md` r4 的结果 B、保护迁移及行为等价；根 `../../../spec.md` RQ-023 开头及 (b)/(c)、RQ-024、RQ-025、RQ-031(c)、AC-009 的保护意图。`effect-registration-boundary-review.md` 是已发现局部冲突与主控处置的输入，判断仍以本次实际读取为依据。

范围只覆盖 B2 对 RQ-006 即时登记失败边界、AC-003、AC-004/005 交叉约束、VC 表保留及 amendment/gate 的变更。不重开 B1 不受影响的要求，不审尚未形成的 Plan P2 或 Task11 实现，不批准产品完成。

## 裁定

**approved。** B2 明确区分原受保护提交后失败与原 fine CMD 之外的两个即时编码登记出口，足以供下一阶段形成局部设计及可判定验收。未发现本范围尚需阻断下一阶段的具体需求缺陷。

1. **结果符合权威兼容目标。** 父 RQ-023 的“原样保留、位置等价”同时约束原 typed 保护和原 raw 出口。B2 只允许 UseStackable 已提交／Freeze／Settled 之后 RequiredExplicit、Committed replay 普通 Write 之后 CurrentStateBatch 两个具名出口；没有用新协议 CMD 的语法范围扩张 fence，也没有按 codec/IOException 类别逆向取消旧 typed 保护。此项是原行为保留，不新增 VC，不要求修改根／子契约或重开 sealed A。
2. **默认保护与例外的判定闭合。** 必须同时具备具名许可和真实 adapter 的本请求／精确物理目标／本次 encoder 来源证明；所有工作、drain、scope cleanup 之后最终仍是同一原 cause，且没有 typed 不确定性或额外独立错误，才保 raw、Committed、无新增 participant fence。业务、非许可登记、未知来源、原 typed 发布及额外独立 cleanup 仍全参与者保护。跨请求／目标／结束后／泄漏复用及 catch 后继续写或成功结束均有明确拒绝结果。AC-003/005 给出可观察区分，既不依赖异常文字，也不授权任意 Func 忽略区。
3. **事务与重放结果保持。** replay 必须承接旧 Transactional 为普通 Write，在单元内直接读领域当前状态，结束后保持 MirrorMerge → Create → batch → Town 原序；不是 ReadOnly、nested bus、事务内登记或重发原 ACK。UseStackable 主事务与登记仍分段。AC-003/004 明确不重复结算、读取当前投影、原 cause 对象及 source-close 顺序；fixture 同连接再次 dispatch 不被扩大为真实关闭 socket 可重发。
4. **规格分辨率合适。** 最终结果和保护条件有风险依据，具体来源证明、attempt/receipt、接口与注册策略留在 Plan/Task；没有把内部 API 方案变成新的 Spec 先决。RQ-008/F-132 的采用条件未被本保留绕过，不启用 Compose 或延迟组装／编码，也不声称 F-132 已修。
5. **推进边界清楚。** B2 先匹配独立 gate，之后才制作并审查 P2，再 amendment-only checkpoint，随后才进行新 effect/Main 产品写入。新 Task11 的实现证明不是本次 Spec gate 前提。现有新 effect/Main 禁用的实际代码与该顺序一致，本裁定不否定或扩张前十个 Task 的已批准范围。

## 决定性证据（实际读取）

以下路径相对上述工作区；源码与原测试直接检查，未以 producer 报告代替：

| 证据 | 对本裁定的意义 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:346–376,399–402`；`Application/Commands/Pipeline/SelectedSessionSettlementBehavior.cs:18–24`；`Application/Effects/EffectOperationContext.cs:88–107` | 旧主命令返回前完成 Freeze/Settled/Committed；之后 handler 组装并 RequiredExplicit 登记。即时编码失败在原 fine 主结算边界之外。前置业务组装仍与实际编码出口分开。 |
| `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:57–74` | Committed 直接进入当前投影重放；finally 只将仍 Pending 的条目置 Rejected，未把后续 raw 登记异常改为 Blocked。 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs:97–126,219–227` | 第一条故障后只扣一次，同 raw 请求重放当前库存且不含 UseStackableResponse；第二条 replay batch 故障后仍可重放，后来库存 7 的实际帧断言得到 7。 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs:574–594`；`Server/tests/Support/RecordedOutbound.cs:317–369`；`Server/tests/Support/OutboundRegistrationParts.cs:82–101` | FailExplicit/FailBatch 经真实 OutboxNetworkMessageSender，in-flight 标记仅用于故障定位，实际异常由 encoder Check 抛出；不是 physical transport 注错。 |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:61–79,88–153` | batch 在登记前即时 EncodeBatch；required direct 即时 EncodeEach 失败上抛，best-effort 编码失败逐目标跳过。B2 没有授予整个登记阶段或所有目标通用豁免。 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs:788–789`；`Application/Effects/ReplayStateDomainService.cs:22–46`；`Protocol/Game/ReplayStateProjectionDelivery.cs:36–61` | ReadReplayState 原注册 Transactional，handler 直接领域读取；之后才 MirrorMerge/Create/batch/Town。普通 Write 而后事务外 sink 是旧边界的等价承接。 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:90–104`；`Application/Commands/CommandBus.cs:90–109` | 当前默认 HasCommittedUnit 失败会包装 typed 并 fence；scope dispose 可追加独立错误。这解释原冲突并支持 B2 的最终 cause 条件，不能误称当前产品已经实现 B2。 |
| `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:455–491`；`Server/tests/Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs:265–317` | MonsterDeath 原发布段包 typed 并遍历 captured 全体；普通 postcommit IOException 保原 inner cause 且全员 fence，rollback cleanup 双故障保两 cause 且全员 fence。B2 明确保留这些负对照。 |
| `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs:294–301`；`Application/Execution/Outbox/GameplayOutbox.cs:213–240` | 作业结束处理 outbox；有效已提交条目按原序，失败时最后追加 source close。source close 与 participant fence 不是同一个结果。 |
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs:126–127`；`Commands/CommandRequestContext.cs:97–99`；`Transactions/CommandWorkUnits.cs:41–44` | 新 explicit effect 仍拒绝、effect 状态无工厂、非 Ordinary/Main 未启用；后续 gate 顺序不依赖尚不存在的 Task11 交付。 |

## 挑战裁定与移交

匹配挑战集保留 0 条，故无逐项 CH 待裁定；reviewer 独立复核了其中记录的攻击路径：

- 相同 IOException／整个登记阶段获得豁免：**rejected**，RQ-006 与 AC-003 的具名许可、真实 encoder 出处及默认保护负对照已排除。
- 独立 cleanup 错误被 raw 资格遮盖：**rejected**，最终单 cause 条件及额外错误撤销保留明确，不依赖提前判断。
- 移动物理故障注入或放宽旧重放断言：**rejected**，规格明确禁止，实际旧注错及断言已核对。
- replay 改 ReadOnly／事务内 sink／重发原 ACK：**rejected**，普通 Write、原序、当前投影和不重复结算要求明确。
- catch、跨请求／目标或 closed/leaked 状态恢复执行权：**rejected**，显式禁止且 AC-002/003 可验收。

当前实现默认收口尚未满足新保留边界是下一阶段的已知设计输入，不是 Spec 遗漏，也不把提前实现作为批准条件。Plan P2 及后继实施须保留真实同容器登记路径、Required/BestEffort、原事务和 source-close 顺序，并通过匹配的受影响独立 gate；本 review 不替其批准。

本轮仅静态检查，未运行产品测试、native 程序或 git；唯一写入为本新 review。没有未决 blocking finding 或缺失的 Spec-stage prerequisite。controller 负责消费本批准、后续 gate、检查点和启用，不由 reviewer 封存 Spec 或启动产品工作。
