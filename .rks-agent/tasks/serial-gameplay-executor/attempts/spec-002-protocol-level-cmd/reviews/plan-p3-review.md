---
kind: rks-review
verdict: approved
---

# Plan P3 有界独立评审

## 身份、目标与准则

- Reviewer：fresh independent `b_plan_p3_review`，独立于 author `b_roster_research`、challenge generator `b_plan_p3_challenges` 和 controller；本 reviewer 未再委派。
- 精确目标：`../plan.md` P3，实际核对 SHA-256 `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519`。
- 配套解释／授权：`../verification/plan-p3-amendment.md`，实际核对 SHA-256 `36f8f21fbada4c6e2a04a1a4a0d86f56486f62962f4e280a736664c939531e05`。
- Fresh immutable 挑战：`plan-p3-challenges.md`，实际核对 SHA-256 `62c0ca15bcf9639b03c9053a53060a79f65ad2666634752fd8693e5eb2da1034`；其中 1 low／0 high 是输入，不作为裁定。
- Governing：`../TaskContract.md` r1，SHA-256 `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；`../spec.md` B2，SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`，重点 RQ-003～009/014/015、AC-001～005/009、原行为保留；匹配的 `spec-b2-review.md` approved（SHA-256 `2803cb6d90907c6cadb36510155d2d678d3ad4f409a9426a585e23dc867e7bfb`）已读取。
- 当前路由：`../routing-decision.md`，SHA-256 `8423e6e6bfb2d8f65285891ae968d20e3da49fc529432f120d0abfcd497fd786`。实际 diff 只有 P3 说明及两个 Plan override 路径变更，两个 Spec B2 override、controller/profile/gate 强度、baseline 保持；不使用 amendment 中的旧 routing SHA 代替当前身份。
- 工作区：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；实际只读 git 核对 HEAD `0d5df6a4288a1b34751054d59a527eaa55ca8b74`。无 `.codegraph/`，因此用 rg／定点文件读取；未运行 native/build/test 或进行 git mutation。
- Loaded criteria：`/Users/puxianguo/.agents/skills/reviewing-work/SKILL.md` 与 `references/plan.md`，按 **Spec-level high-level / mode: task-units** 判断总路线、共享契约、责任／依赖、风险检查及下一 useful unit，不要求预写 Task15 API／fixture／所有未来 Task。读取 `using-rks-agent/SKILL.md` 的 bounded-subtask 规则，不重新路由。读取本工作区实际 `AGENTS.md` 涉及分层、显式工作单元／请求、Main／账本／发件箱／失败保护，以及 `Docs/Agent/ArchitecturePhilosophy.md`、`RefactorPreferences.md`。

本范围只审 P3 commit-only 已确认回滚结果、四 opening 迁移路线和相应取证／推进边界；不重开 P2 不变的设计、已批准 Task01–14 或 Spec B2，不批准 Task15 实现或结果 B 完成。已读目标全文及相对 HEAD 的实际 diff，未发现 P3 越界改动其余工作包或最终接受线。

## 裁定

**approved。** 当前高层 Plan 已解决推进所需的共享决定，无未决 blocking planning defect。最小 commit 结果入口可以弥补默认 sticky runner 与旧四 opening 外层失败响应之间的结构差异；它没有把完整 RunAsync、任意 action、编码登记或清理错误变成恢复区。

1. **旧边界和拟迁边界有实际源码支撑。** 四旧 message handler 的 catch 在 `await commands.SendAsync` 外，确实覆盖 fine 的 action 和后续事务提交，响应登记在其后。仅在 action 中 RequireRollback 不足以保留旧 Prepare／fallible Commit 失败响应；默认 runner 则在逃逸错误后记录 request.Failed。因此新增 runner 独占构造的已完成结果有明确消费者，非无人消费的核心空壳。
2. **回滚事实与继续执行权分开。** Returned 包含正常 RequireRollback 拒绝，CommitRolledBack 只表示窄 CommitAsync 调用抛错后机制确认完整回滚，不声称提交；guard、当前性、已有 sticky、取消／typed uncertainty、非 fallible 违约和 Committed 完成状态不获得该结果。unit／lease 清理完成及 tracked operation 完成在返回之前，scope 独立失败仍由 bus 最终裁决，结果值不能清除它。现有 Completion 和 typed 路径提供可实现的分界，IsSettled 确实不能替代它。
3. **Main 和重试没有新增授权。** ConfirmedRollback 不调用 Settled、不清 main claim；Freeze 后 projection 保留真实事实，只有下一次 ledger admission 将原 entry 重置 Pending／清 projection，原 Ordinal 不变。其角色足以保留一次 Main、原窗口与同请求重试，不需新 ambient／全局 tracker／任意 raw 函子。
4. **责任和实施顺序适合当前分辨率。** 原 domain／wire-slot／clock 的异常映射归各 whole，Application 只拥有事务结果，outbox／Project／encoder 不进入该 action try。T-004 用 Task14 起点先取得旧 route 的真实协议 oracle，再同 provider 迁四协议与最小共享能力；T-005 后继不会借此预授权 Mailbox 自 Rollback。原全路线、最终八项目／取证／测量和结果 required 独立快审保持。
5. **实证缺口被明确留在实施前置步骤，没有冒称已通过。** provider 探针足以支持 deferred FK 在实际 provider 的 Commit 阶段注错这一候选方法，但不能证明框架／opening 的结果。Task15 先旧后新和方法不可行时报告／换有证据方法的路线，让该剩余实证可在产品迁移前廉价完成；未提前运行这些测试不是高层 Plan defect。API 名、局部类型和精确测试白名单可留 Task15。

## 决定性证据

以下源码均为本次实际读取，路径相对上述工作区；未将作者报告或旧测试名当成执行结果。

| 证据 | 结论 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/PackageOpening/PackageOpeningSlice.cs:65–104`；AvatarPackageOpening `:50–90`；MagicBoxSingleOpening `:42–80`；MagicBoxBatchOpening `:46–84` | 原 fine 只做 domain／wire／clock；四外 catch 按 OpeningRejected 或普通非取消、非 uncertainty 映射，日志和失败 ACK 在 fine 完全结束之后；成功仍各自原投影。 |
| `Application/Commands/Pipeline/UnitOfWorkBehavior.cs:51–70`；`SelectedSessionSettlementBehavior.cs:18–36`；`Application/Effects/EffectOperationContext.cs:88–113`（均在 `Server/src/Dfo.Server.*`） | Freeze／Commit 在 fine 返回之前，普通确认回滚及 typed uncertain 分路；Freeze projection 不因失败自动清空。 |
| `Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:97–129,139–187,212–259`；`DomainTransactionScope.cs:81–92` | settled 在 Prepare 前已置位；Prepare／唯一 fallible commit 失败完整反向回滚后才 ConfirmedRollback。非 fallible 违约和 rollback 失败为 typed Uncertain；Committed 后组装失败不是回滚。lease Dispose 不会重新证明 settled 事务结果。 |
| `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:127–150,165–233` | Prepare 先校验 facet，再 flush；实际 pending.CommitAsync 与资源清理不同阶段。清理失败保 operation／cleanup 因并 typed；成功清理才原样重抛 commit 因。 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:46–126`；`Commands/ICommandWorkUnits.cs:28–34`；`Commands/CommandRequestContext.cs:42–79,93–124`；`Commands/CommandBus.cs:90–128` | 默认 guard／action／commit 逃逸和 lease cleanup 都可能使请求失败；RequireRollback 仍需当前请求，operation drain 在 Close／scope 释放前，scope 独立失败仍收口。新结果不能通过外层 catch 清这些事实。 |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs:20–51`；`EffectOperationLedger.cs:36–67` | Main claim 一次，Freeze／Committed／ConfirmedRollback 区分；重试复用原 entry，仅置 Pending／清 projection，无 Ordinal 延长。 |
| `Server/src/Dfo.Server.Application/Inventory/PackageOpeningDomainServices.cs:14–103`；`OpeningInventoryDomainService.cs:51–127`；`OpeningEffectPlanRuleService.cs:100–107`；`Effects/OpeningEffectHandlers.cs:96–113,138–145`；`Inventory/ItemDeliveryDomainService.cs:172–175`；`ItemGrantDomainService.cs:21–37` | 四 opening 所用链没有自行提交／回滚；物理奖励固定 InventoryOnly，虚拟奖励用 ApplyPlanned，不走 ItemGrantDelivery 的自行 Rollback 路径。Mailbox 不是本次需要泛化的理由。 |
| `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:20–24`；`Server/src/Dfo.Server.Abstractions/Inventory/ItemLifecycleScope.cs:65–73`；`Application/Inventory/CharacterContainerDomainService.cs:117–155`；`InventoryContentsEventDomainService.cs:22–28`；`Application/DomainEvents/EventService.cs:83–101`；`Application/Effects/ItemEffectHandlers.cs:49–65` | 实际参与者随路径 enlist；custody／绑定 Prepare 守卫、事件回滚和可选 cooldown 的非 fallible commit 都须保留，不能拿某 fixture 名单概括全集。 |
| `Server/tests/Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs:42–115,159–248,265–317`；`DomainTransactionTests.cs:88–123` | 现有源码有正常拒绝、默认异常 sticky、Prepare／fallible recording、未 await drain、提交后／非 fallible／rollback cleanup 的有区分断言；它们不等于 native SQLite／opening 协议 oracle。 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/PackageOpeningTests.cs:200–215,401–411`；`DarkRepulsorPackageTests.cs:53–69`；`Server/tests/Dfo.Server.Networking.Tests/Integration/PackageOpeningIntegrationTests.cs:18–57` | 已有真实 trigger 是 action SQL 失败，主要直接 fine；现有四 opcode 线级例是无 selection／截断。均不足以证明真实 Commit 故障下的 ACK／无 fence／same-entry retry。 |
| `../verification/evidence/plan-p3-provider-probe/{Program.cs,probe.csproj,observation.json,stdout.log}` | 已读工具转录：实际 Persistence transitive provider、`:memory:`、FK=1、INSERT 成功、SqliteTransaction.Commit 栈 19/787、Rollback 成功。未捕 start/end/resources，不提升为完整协议／DomainTransaction 成功证明。 |

## CH-1 disposition

**confirmed（源码前提）／deferred（临近 Task 的非阻塞落实）；不确认当前高层 Plan 存在需重写的 material defect。**

`ItemLifecycleScope.ValidateForCommit/Prepare` 和 `CharacterContainerDomainService.Changes.PrepareAsync` 的确直接抛普通 InvalidOperationException，没有先记录 request/job sticky；SQLite Prepare 还调用 facet.ValidateForCommit。DomainTransaction 对它们同样可能完成回滚，所以 **“CommitAsync span＋ConfirmedRollback＋无已记 failure”本身不足以判断可恢复**。此处不能把挑战整体 rejected，也不能假定现有 sticky 守卫已覆盖所有真实 Prepare 不变量。

但 amendment「真实旧行为与缺口」已明确列出 custody／绑定守卫并要求不降级，Plan §4／Verification 也要求 guard 保留；高层路线没有授权忽略这些约束。识别真实不变量来源、保原 cause 的局部实现，以及至少一个真实参与者 Prepare 守卫反例，可在 Task15 详细规划及实现中完成，不需要改变 commit-only 中心方案或提前编写该 Task。Task15 必须区分该守卫与可恢复的 Prepare／native SQLite 故障；前者不得得到恢复结果／失败 ACK 后正常结束，后者按旧 oracle 保留。不得按异常文字或将所有 InvalidOperationException 粗分。该 deferred 项由 controller 交给临近 Task 并在 required 结果快审检查，不是产品已满足的结论。

## 推进边界

本批准只通过当前 P3 高层 Plan gate。controller 仍须完成 amendment-only 检查点，随后给 Task15 精确路径／详细规划授权；Task15 先取得旧 route 的 action SQL 及真实 native Commit 协议证据，再实施新结果／四协议并做同 provider 对照、共享 core 回归及 required 独立结果快审。正常拒绝或捕获 action 因转 RequireRollback 后，如后续回滚／清理另失败，仍须按既有“保留业务／清理各因引用”要求收口，不得让正常回值遮掉独立失败。

本轮仅新建此 review；未创建 Task15，未改 Plan／amendment／challenge／routing／Spec／Contract／旧制品或产品，未运行测试／native／构建。当前无缺失的 Plan-stage prerequisite，无 blocking finding；Spec／根完成、Astra、push／PR／merge／版本／部署／清理均不在本批准范围。
