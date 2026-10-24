# Plan P3 独立挑战记录

- Target: `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/plan.md`，P3。
- Target SHA-256: `778a28e67377d099d0d0cc43739e5c3083566c5895cb3292f2b47e2625909519`。
- 解释及授权: `verification/plan-p3-amendment.md`，SHA-256 `36f8f21fbada4c6e2a04a1a4a0d86f56486f62962f4e280a736664c939531e05`。
- Governing sources: 本目录 `TaskContract.md` revision 1（SHA-256 `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`）；`spec.md` B2，特别是 RQ-004～008、AC-002～005（SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`）；当前 `routing-decision.md` 的 long-running/governed 及 P3 gate 路径（SHA-256 `8423e6e6bfb2d8f65285891ae968d20e3da49fc529432f120d0abfcd497fd786`）。amendment 内旧 routing 表是其写入时的历史快照，不替代当前路由。
- Source checkout: `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；源代码检查点 `0d5df6a4288a1b34751054d59a527eaa55ca8b74`。
- Freshness / role: 独立于 P3 author 和后继 Plan reviewer；只生成挑战，不作 review verdict。
- Attack scope: P3 新增的 commit-only 已确认回滚结果及四 opening 的旧失败 ACK/重试保持；围绕真实 runner、事务、SQLite、四旧 handler、opening 领域及已有测试，检查它是否会错误恢复程序守卫、丢失恢复响应或误改 Main/Freeze/Ordinal。默认 Write/ReadOnly、B2 两个 raw 出口及其它保护均按既有要求保持。本记录不要求门前执行 Task15 的协议 oracle，不将 provider 可行性探针当作协议通过。
- 操作边界: 仅只读源码/制品检查；未执行 native/build/test，未修改目标、产品、旧制品或 git。

保留 1 条 low-risk 挑战；没有满足“目标缺陷→错误实现→重大伤害且临近 Task 无法廉价控制”因果标准的 high-risk 挑战。下列问题可由临近 Task 的局部设计和针对性检查控制，不要求仅为展开 API/fixture 细节重写高层 Plan。

## CH-1: 真实 Prepare 不变量守卫尚未记录 sticky，不能以“无已记失败”替代排除守卫

- Risk: low-risk
- Failure and impact: 若新 runner 只实现 plan.md:61 的 CommitAsync span、ConfirmedRollback、非取消/非 uncertainty、健康/无已记 sticky 和清理条件，真实 Prepare 不变量违例仍可能被包装成 CommitRolledBack。以悬空 detached item 为例：Prepare 抛普通 InvalidOperationException，协调器完整回滚后 Completion 是 ConfirmedRollback，且该守卫未写入 request/job 的失败状态；四 opening 随后便会映射 StorageFailure 并正常结束，使程序错误被当成可恢复的存储故障。资产已真回滚，因此这里没有证明重复发奖或持久损坏；但会削弱 P3 明确保留的程序错误出口。Plan/amendment 已要求这些真实守卫不降级，风险在于临近实现和测试把“已有 sticky guard”当成了全部守卫。可在 Task15 廉价控制，属非阻塞提醒。
- Evidence: `Server/src/Dfo.Server.Abstractions/Inventory/ItemLifecycleScope.cs:65–70` 的 ValidateForCommit/Prepare 直接抛普通 InvalidOperationException；`Server/src/Dfo.Server.Application/Inventory/CharacterContainerDomainService.cs:134–142` 的绑定版本守卫也是如此，均不记录 request/job sticky。`Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:127–135` 还会在 Prepare 再做 facet 校验。`Server/src/Dfo.Server.Application/Transactions/DomainTransaction.cs:111–129,212–228` 对这些 Prepare 异常和普通存储 Prepare 异常走同样的反向回滚，成功后均可得到 ConfirmedRollback；实际 Completion 证明回滚，不证明 cause 是可恢复业务/存储故障。`plan.md:61,188` 主要展开已有 sticky/runner guard 的保护，而 `verification/plan-p3-amendment.md:46` 又明确列出 custody/绑定守卫并要求不降级，应一起落实。
- Minimum correction: 临近 Task 明确如何识别或保留这些真实、尚未 sticky 的不变量守卫，并至少用一个真实参与者的 Prepare 守卫反例区分于正常可恢复 Prepare/SQLite 故障：前者不能得到恢复结果/失败 ACK 后正常结束，后者仍保留原恢复响应。不得依赖异常文字或把全部 InvalidOperationException 一概归类；具体类型/来源标记及测试 fixture 留 Task 选择即可。本挑战不要求扩大恢复 span 或引入新 ambient/tracker。
