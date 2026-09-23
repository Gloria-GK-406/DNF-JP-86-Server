# `10006007` 任务完成券全链独立审查

审查日期：2026-09-23。`kind: rks-review`；结论：**approved（代码正确性层面，无阻断项）**。审查对象为当前工作树中的 `0x0204` 19B 券分支、PVF 筛选、任务标记与进度、背包结算、提交后通知及相关测试。结论只涵盖本服选定的保守业务策略：`Epic/Normal` 且最低等级不高于角色等级，补齐安全前置；它不是未获证实的官服筛选算法或客户端实机验收。

## 审查结论与依据

| 关注点 | 结论与证据 |
|---|---|
| 入口与协议 | `QuestClearCouponSlice.cs:23-59` 仅接受实帧对应的 19B、主背包、槽 69 所在的合法非负槽、动作 77 和零保留域；其他形状在调用会话/业务前拒绝。`QuestClearCouponDomainService.cs:22-39` 又以实时槽物品 ID `10006007` 验证，不能用报文动作值冒充物品。成功/失败分别有独立响应，`OpeningDelivery.cs:16-34` 按 ACK、库存绝对变化、任务投影排序，并核对当前会话。A21 成功响应只有客户端静态最小读取证据，尚未有成功实帧，故这里认可的是“与现有静态证据相容”，不是 `ClientVerified`。 |
| 任务选择与职业安全 | `QuestClearCouponRuleService.cs:30-92` 只以 `Epic/Normal` 为根，按 `RequiredLevel.Minimum <= level` 追溯历史主线；`Side/CommonUnique` 仅可能作为安全前置。每个根在独立候选集上求前置闭包，OR 组只取一组；缺定义、环、不安全组均不泄漏部分标记。逐任务排除转职标识、成长/觉醒等非普通物品奖励、特殊永久状态、问答未决、碰撞、事件/隐藏、职业家族与成长类型不匹配。已清的特殊前置只作为已满足条件使用，不改其原标记。规则测试覆盖这些边界。`Normal` 在当前 A21 PVF 中为零项，“普通任务”覆盖范围仍是产品/实机证据缺口。 |
| 原子性与无奖励 | `ProtocolModule.cs:324-325` 把命令注册为事务执行并持有用户/账户独占门；`UnitOfWorkBehavior.cs:27-37` 在处理成功返回后提交，异常时释放回滚。`QuestClearCouponDomainService.cs:45-65` 在同一命令中依次标记、报告依赖进度、消耗一张券，`SettleAsync` 再校验槽物品/数量/期限；失败不会留下“券没扣但任务已清”或反向状态。`QuestLog.cs:232-264` 清除相应已接/追踪行、写非零已清标志、保留原有非零标志且只返回新增 ID。结算给空奖励表，不调用通常的任务领奖/经验/职业变更路径。 |
| 批量进度与旧行为 | `QuestProgressDomainService.cs:34-91,259-296` 先聚合本次报告的所有 `QuestCleared` ID，在第一个同类事实处对当前已接任务及完整已清快照重算“清任务”目标；重复 ID 或同一个被依赖任务只写一次。单个 `QuestCleared` 结果与原流程等价。混合 `QuestCleared`/`QuestHoldingsChanged` 的现有交付路径也维持顺序：前者只改“清任务”通道，后者再按物品同步；这些事实处理器不在报告中改变已清标志，因此提前聚合后者的已清 ID 不改变最终状态。既有单步和新增批量测试均通过。 |
| 提交后可见性 | `QuestClearCouponSlice.cs:164-206` 在事务提交后读取已清/已接快照，发全量 `0x0164`、已接任务进度以及刷新后的可接列表；其 bitmap 格式与选角初始化一致（`CharacterEntryStateServices.cs:67-70`），不会仅更新背包。`OpeningCompleted` 只向仍匹配的会话发出。 |

## 验证与剩余风险

- 本人运行 `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~QuestClearCoupon|FullyQualifiedName~QuestProgressDomainServiceTests|FullyQualifiedName~QuestLogTests" --verbosity quiet`：66/66 通过。
- 本人运行 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~QuestClearCoupon" --verbosity quiet`：13/13 通过。第一次尝试恰好撞上并行编辑中的测试文件，短暂因缺 `using` 编译失败；该编辑随后补齐，复测通过。未替根任务重复完整方案构建。
- 提交后任务日志或 offer 投影读取若失败，处理器会记录错误，仍发送成功 ACK 与库存变化，任务 UI 可能暂时沿用旧缓存，需重登恢复。这是失败路径的可用性风险，不影响事务状态；可以后续补强重试/重同步。本轮未发现能从现有材料证明的正常路径阻断问题。
- ACK 的确切字节、客户端看到任务列表的效果、`Normal`/其他任务类别与“普通任务”的对应关系仍须真实 A21 成功样本验证。70 参考端没有这个批量动作，不以其算法推断 A21 行为。
