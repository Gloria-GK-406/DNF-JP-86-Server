# Spec B4 ready R2 独立评审

```yaml
kind: rks-review
verdict: approved
```

## 目标、独立性与判据

Reviewer 为 fresh independent `b_spec_b4_review`，与 B4 producer、CH-1 generator 和主控分离；应用 `reviewing-work` 的 Spec 判据。唯一工作目录为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，HEAD `f42efb55bedad71dc1196100e39255bc483ed72c`。

| 当前评审输入 | 精确身份 |
|---|---|
| [Spec B4 ready R2](../spec.md) | 42632B，SHA `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| [B4 amendment R2](../verification/spec-b4-amendment.md) | 13392B，SHA `a73b9317a2be57767a73cf4937b4553fc80abb81f526a62b2f6ded1c2ca07d8b` |
| [immutable CH-1](spec-b4-challenges.md) | 6366B，SHA `f8859fbc613f6f199f62fb6398de3b9c486accb09ba00b8a739b1c93b0a7ea86`；目标为 R1，当前 R2 是其 review 前一次最小纠正 |
| [R1 Spec 快照](../verification/evidence/spec-b4-amendment/spec-b4-ready-r1-before.md) | 40180B，SHA `ca7f64fd4dfcb23a32ffd731d196dea9e9a83ef092e00991fde89393ea2a06c4` |
| [R1 amendment 快照](../verification/evidence/spec-b4-amendment/spec-b4-amendment-r1-before.md) | 10185B，SHA `2b668e65a0b8ca7f38c190f9b4e7967c3d13f2610c452b20ba1ff89fa0fdfd37` |
| [B3 快照](../verification/evidence/spec-b4-amendment/spec-b3-before.md) | 32948B，SHA `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`；独立核其 bytes 等于 HEAD 原 spec |

范围仅为本轮 Cera WHAT、VC-B4 和相关 RQ／AC，以及 CH-1 的当前处置。已读根 Contract r4 Goal、Constraints 与 SC-8B、子 Contract r1、current Spec／amendment、R1 对照、CH-1、TASK-28 Result／独立快审／主控消费、正式七例 JSON及相关现有源码。适用仓库 AGENTS、架构哲学与重构偏好亦已读取。没有把本轮变成整份 Spec 重审、Plan HOW 预审或未来产品验收。

## CH-1 独立裁定

**Disposition：confirmed in R1；当前 R2 已充分解决，无未解决阻塞。** 原 high-risk 等级成立：这不是名称差异，旧路径没有选中保护集合，而现有显式足迹必须取得精确 actor；若不在 WHAT 明列，后继只能暗增可见围栏或削弱足迹／默认保护。该选择不能交给普通局部接法。

独立读取实际调用链确认：

- `CeraShopDomainService.PurchaseAsync` 在入口及实际写前调用 `DemandEffects`，不能沿无足迹单元合法执行这些业务。
- `CommandWorkUnits.RunAsync` 对 NonRequest 要求 Ordinary write、ProtocolNonEffect、恰一个精确物理 actor；`ExplicitNonRequestFootprint` 校验 actor、owner 和 current。explicit 请求不能借 LegacyFootprint 绕过。
- 单元 Committed／Uncertain 会记录该参与者，`FailAsync` 对默认保护集合逐项 `BlockAsync`。后者实际 Fence、精确连接阻断、DiscardExact 和精确关闭；`OnlineStateRegistry.Fence` 经 FindExact，`CreatureRuntimeDomainService.DiscardExact` 仅移除该 session 身份，支持保护替换会话的要求。
- 旧 Cera fine 只实现 IAccountCommand，旧外层 handler 捕获非取消异常；TASK-28 正式 C1/C2/C3 的 selectedScope=null、无 Fence／Blocked／close 与此相符。

R2 的 RQ-006、Cera 专节、VC-B4、AC-003/005 已明确：本次精确买家进入显式足迹／结算参与者，只有 Committed／Uncertain 事实触发默认失败保护时才 Fence、阻断该精确连接、DiscardExact 和关闭来源；这是相对旧空集合新增的有意变化。范围不扩到替换身份或其他玩家／协议，不能为保旧空集合削弱保护。正常成功、正常拒绝和健康清理完成的确认回滚仍无新增围栏／关闭，不把普通 Commit 成功当成围栏触发条件。

该纠正解决的是同一个参与者／围栏风险，未引入新的中心机制；沿原 immutable CH-1 评审当前 R2 合理，不需要重生挑战。R1 原稿及 amendment、TASK-28 先前裁定均保留原文，后继显式裁定没有倒写旧事实。

## 当前规格的可实现性与验收

1. **原结果与有意变化分明。** 当前专节和 AC-004 保原成功、正常拒绝、普通动作失败／真实提交阶段确认回滚的拒绝响应，且要求真实 cause、健康请求、完整清理及实际完成事实。正常拒绝的原空提交不被统一改成回滚。typed 不确定、unknown、sticky／guard、取消及独立清理不落入恢复行，没有按异常文字／IsSettled 自报回滚的宽口子。
2. **旧证据足以支撑本次 WHAT 决定。** 独立读七个正式 JSON：A 是原生 SQL19/1811、未返回 receipt、ConfirmedRollback；B 是真实 SqliteTransaction.Commit19/787、先 receipt／FK1／child1、KnownStorage／FallibleCommit、完整回滚；两者旧拒绝 ACK／无围栏关闭。C1 资产已提交且 Uncertain，C2 双 cause／Uncertain 虽资产回滚，C3 scope故障时 Committed，三者旧均正常返回拒绝 ACK／无围栏关闭。它们是已接受旧边界的实际或受控观察，不是迁移后通过；内存传输、观察查询可能提前 enlist SQLite、受控故障等范围均披露。
3. **不确定和已登记消息不混同。** 当前 AC-005要求保 cause 引用、实际资产与完成事实，并直接核精确保护、来源关闭／链和条目次序。已读 CommandBus 的 drain→request结束→scope清理→最终 FailAsync，及 GameplayOutbox.Complete 的已提交／单元外条目存活、登记次序和失败来源关闭置后。故 R2 明确 scope失败前可能已登记正常消息是必要且与现机制相容；不要求统一零帧，也不新增撤回／提前flush／因cleanup追加 StorageFailure ACK。新保护对其他身份的限定指精确围栏和清理目标，不否认既有关闭生命周期的正常投影。
4. **WHAT 与 HOW 分层充分。** Spec 决定保何种响应、何时失败、保护哪个精确买家及哪些事实须验证；它没有指定新的工厂、数据结构或恢复 API。P4 当前仍限定四 opening 的恢复消费，不能直接实施 Cera；B4 明确待本门通过后另做受影响 P5、fresh challenge／review及 amendment-only 检查点，再详细规划产品。这是正确的后继门，不是当前 Spec 缺失实现。
5. **授权与范围相容。** 根／子 Goal允许经审查逐项列出的有意变化，根 Constraints要求回包／关闭等变化列明，SC-8B亦保此例外。B4按这一权限明确新增 VC-B4，不需要改 Contract 或新增 HITL；执行器、出站、事务机制、A封存和原单执行器不变量不被重新设计。Cera仍是NonEffect，不获得Main、效果账本或同序号幂等承诺。

## 不变项与批准边界

独立复算目标／amendment／CH-1和快照 SHA；17个RQ、11个AC稳定。B3的三个具名raw出口整节与VC-B1／B2／B3逐bytes保持；child Contract、Plan P4、routing和TASK-28仍等原身份／HEAD，index空。本 reviewer没有修改这些输入。

本轮无未解决 blocking finding。批准只表示当前 B4 R2 的受影响WHAT可供后继Plan落实；不批准尚未存在的P5、恢复接口、产品实现、未来动态证据或Spec002整体完成。后继必须落实当前AC的普通回滚对照、真实nativeCommit、typed／双因／scope故障、精确买家保护及不误伤、真实登记和关闭次序；原全量／DFO／Golden／性能接受线与Astra收尾门仍在原阶段验收，不在本次静态review冒称通过。

本 reviewer仅新增本文件，未运行native、改产品或旧证据、stage／commit／remote。完成后停止写入，交主控消费与推进。
