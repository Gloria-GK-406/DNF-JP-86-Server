# Spec B4 有界对抗性挑战

## 目标、来源与范围

- Generator：fresh independent `b_spec_b4_challenges`；与 Spec producer 和后继 reviewer 分离。应用 `generating-adversarial-challenges`，本记录提出待独立裁定的挑战，不给 review verdict。
- 唯一工作目录：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；HEAD `f42efb55bedad71dc1196100e39255bc483ed72c`。
- 精确目标：`../spec.md` B4 ready，40180B，SHA `ca7f64fd4dfcb23a32ffd731d196dea9e9a83ef092e00991fde89393ea2a06c4`。
- 匹配 amendment：`../verification/spec-b4-amendment.md`，10185B，SHA `2b668e65a0b8ca7f38c190f9b4e7967c3d13f2610c452b20ba1ff89fa0fdfd37`；前版 B3 快照32948B，SHA `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`。
- Governing sources：根 Contract r4 的 Goal、Constraints「可见行为变化须列明」及 SC-8B；子 Contract r1 Goal/Scope；已提交 TASK-28 Result、Independent Quick Review、Controller Consumption 和正式取证报告；B4 RQ-006/007、Cera 专节、VC-B4、AC-003～005/011。父 Plan P4 四 opening 恢复限制只作既有边界，不在本次改写。
- Attack scope：仅 Cera 的可恢复拒绝与默认不可恢复失败、精确参与者及可见结果。实际读旧 Cera handler/fine/domain、现有显式工作单元／footprint／FailAsync／SelectedSessionSafety、CommandBus scope收口、执行器和 outbox 失败条目顺序。没有重审全 Spec，没有要求在 Spec 预写 API、Task 或未来动态结果。

## CH-1：足迹所需 actor 会自然进入默认围栏，但 VC-B4 未明确纳入这项差异

- Risk: high-risk
- Failure and impact: 迁移者沿当前既有机制为 Cera 的真实领域写入提供显式 NonRequest 足迹，就必须把精确购买 actor 放入工作单元参与者；一旦发生 B4 点名的 C1/C2 不确定结果或 C3 已提交后 scope 故障，默认最终收口会封锁这个 actor。TASK-28 的对应旧路径却没有选中会话围栏。B4 专节和 VC-B4 只明确拒绝 ACK／来源关闭变化，同时反复要求不增减原精确参与者范围、也不承诺新增围栏；尤其 AC-003 的“不以该变化扩大或缩减原精确参与者范围”可能被解释为必须保留旧 IAccount-only 的空保护集合。这样 P5 要么按现有 footprint/默认保护实现却引入未明列的新围栏与 DiscardExact，要么为了保空集合绕过足迹或削弱默认保护。两者影响 RQ-006/009/014、根保护意图及可见变化验收。普通后继测试只能暴露这项冲突，不能代替本次 WHAT 层决定“实际要封锁谁”；若留到产品才决定，将迫使后继暗改可见边界或重新过 Spec gate，故应在本轮独立评审裁定。
- Evidence:
  - `spec.md:98` 要求“精确参与者仍按原业务事实和工作边界确定”“不擅自增减参与者集合，也不承诺新增选中会话围栏”；`spec.md:158` 的 VC-B4 仍是“不擅自增减参与者或新增围栏”，`spec.md:170` 的 AC-003进一步写“不以该变化扩大或缩减原精确参与者范围”。表中的明列变化是普通拒绝改最终失败／关闭来源，没有将旧无围栏到新默认 actor 围栏列为必然或允许的可见结果。
  - `Server/src/Dfo.Server.Application/Inventory/CeraShopDomainService.cs:33,101–140` 在入口与真实写前多次调用 `footprint.DemandEffects(...)`；不能直接用没有足迹的普通 Write 迁移这些调用。`Commands/ICommandWorkUnits.cs:19–25` 在未声明 footprint 时取 `unit.Footprint` 会抛；`Commands/CommandContracts.cs:69–87` 又禁止 explicit 请求使用 LegacyFootprint 桥。
  - `Transactions/CommandWorkUnits.cs:63–81` 的现有 ProtocolNonEffect 足迹仅由 `CommandFootprintMode.NonRequest` 创建，要求 participants 恰好一个、连接对象就是物理来源。`Effects/ExplicitNonRequestFootprint.cs:15–36` 还对该 actor执行真实 current／owner校验。Cera 没有 Main／效果账本，不能改用 Main 绕开。
  - `Transactions/CommandWorkUnits.cs:148–151` 会把 Committed／Uncertain 单元的 participants 记录到本请求；`CommandRequestContext.cs:169–173` 保持该集合；`Transactions/CommandWorkUnits.cs:183–197` 的默认 FailAsync 对这些人逐个 `BlockAsync`。`Commands/SelectedSessionSafety.cs:61–73` 实际 Fence、封锁精确连接、DiscardExact 并请求关闭。因此这里不是仅词语差别，也不是“声明 actor 但没有行为”。
  - 旧 `CeraShopPurchaseCommand.cs:10–21` 只有 IAccountCommand；`SelectedSessionExecutionBehavior.cs:14` 未入 selected scope即next。已提交 `verification/evidence/task-28-inspection/report.md` 的 C1/C2/C3及对应正式 JSON直接记录 selectedScope=null、Standing.Fenced=false、IsBlocked=false、无close。这是本轮对照的已证旧结果，而非把新 actor 围栏当原行为。
- Minimum correction: 在本 Spec 的 Cera 结果／VC-B4及对应验收中，明确现有精确购买 actor 足迹与默认参与者保护的预期关系，说明相对旧无围栏结果允许或要求发生的具体变化。若选择沿当前机制封锁该 actor，应把它作为 VC-B4 明列差异并保足迹／默认保护；若主控坚持旧无围栏，则须先说明这是另一个受影响决策并判断与既有不变量相容性，不能留给 P5 当作无语义影响的本地接法。无需在 Spec 指定新的数据结构、工厂或恢复 API，也不据此扩大到其他协议或任意参与者。

## 其余攻击结果与限制

未保留其它独立挑战：B4 已区分原正常拒绝的空提交、真实确认回滚与 Uncertain，明确 NonEffect 无 Main／同序号幂等；scope结束前可能已登记消息及最终来源关闭置后均与现有 outbox 行为相容，不能用零帧统一断言；三个 raw 出口、KnownStorage／sticky／cleanup保护和后续独立 Spec/Plan gate 也未被本轮文字放宽。对这些已明确边界不再重复制造低价值问题。

本次仅静态源码与已提交证据读取，没有 native、产品修改、重新生成证据或对未来实现的通过主张。保留挑战数为 high-risk 1、low-risk 0；独立 reviewer 应核实本条的实际严重度及最小充分修正。本记录在落稿后保持 immutable，不是 Spec 或 Plan 批准。
