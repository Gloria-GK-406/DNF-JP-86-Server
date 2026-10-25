# TASK-48 独立规划评审 R1（2026-10-25）

**结论：Spec B8 与 Plan P10／TASK-48 revision 1 均 approved；本次未发现阻断实施规划的缺陷。** 仅在主控消费本评审、完成 metadata-only 规划检查点后，才能实施产品、接口及判据增量。最终 Result 尚未评审。

评审基准为工作区 `refactor-session-20261021` 的 `d0dd43d260a7fc1ed22d4cb2196a8861f15dbdc4`，目标是当前待提交的 B8 ready R1、P10 ready R1 和 TASK-48 revision 1。按 `using-rks-agent` 有界委派与 `reviewing-work` 的 Spec／Plan 判据，读取实际 AGENTS、根 r4／子 r1 对应边界和 process-reset §3～6；未重路由、生成挑战或运行产品测试，也未把旧测试或执行者报告当作新实现通过。

## Spec B8 verdict

```yaml
kind: rks-review
verdict: approved
```

RQ-009、注入说明、AC-001 与 B8 修订记录相互闭合。新增的是原翻牌生产者的固定阶段同步控制：输入限精确 session／run，禁止任意延时、回调、请求、世界查询、发送及当前作业内入队等待；真正到期仍从作业外进入 typed 内部 CMD。它没有把 B7 的纯 Wake 偷换成通用调度，也不扩大其它技术注入类别，符合根／子要求的单执行器、薄 handler、显式上下文与保留原阶段位置，无须改变根／子契约或中心方案。

实际必要性来自旧代码：`DungeonClearSlice.cs:1055–1060` 成功登记后调度；`CardRewardHandlers.cs:149–155` 手动 Free 成功后、投影前取消；`DungeonCardPresentationCoordinator.cs` 的 `OpenLayoutAsync` 在布局登记及 `ScheduleClearReturn` 后替换自动确认待办，`RunAutoFlipAsync` 仍调用旧 SelectCard fine。B8 正面覆盖这一依赖，并以同状态所有者、token 身份替换、已入队取消和 drain 作为接受条件。

## Plan P10／TASK-48 verdict

```yaml
kind: rks-review
verdict: approved
```

在上述 Spec 批准基础上，P10 的封闭接口和 Task 的八行范围可执行。`IDungeonCardPresentationControl` 同 singleton alias、三固定动作、剥离原 producer 的世界／投影业务，能保原调用时点而不把 producer 或规则服务伪装成 DomainService。真正共用的布局／选择算法有合法归属；单消费编排归自己的 CMD，禁止 CMD 互调。Grant 与 SelectCard 在 `ProtocolModule.cs:545`／`:772` 均为 Transactional，计划分别对应 actor＋NonRequest 和空 selected＋None 的一次 ordinary Write，没有扩大 card fixing／claim／发布的事务边界。

延时迁移的抑制 flow、先 yield、Timer 新根、无 source、None 入队而 body 观察 token、同 token Complete 与 inflight/drain 均对应旧生产者实际机制；现 `CommandBus.cs:56–71` 的 Internal 入口支持该来源和取消分离，未要求改 runner／bus。未迁 GiveUp／Eplp／Wipe 继续同 owner Cancel，不误关其迁移账。

需由最终 Result 兑现的既有计划义务：先以旧实现的真实 scope／SQLite／claim oracle 证实 Grant 的 durable 提交后、`durableCommitted=true` 前清理失败结果（`DungeonClearSlice.cs:927–945,1062–1072`）；任何默认 whole 生命周期可见差异列 VC，不预判其为纯搬迁。F-86 须由真实 reservation／单执行器证明，现 fake bus 的 AlreadyFlipped→成功用例不能证明生产冲突。保原强断言并补真实 typed 来源、根链、取消／替换／drain、共享翻面、一次结算及精确关闭证据，随后按 Task 完成 fresh 八项目全量与逐用例核对。这些是已明确的实施验收工作，不是要求计划阶段预先完成产品。

主控拥有后续门消费及生命周期决定；本文件只批准当前增量规划，不关闭任何迁移行、不批准最终结果或外部收尾。同一 reviewer 保持可用于 Result／有界返修复核。
