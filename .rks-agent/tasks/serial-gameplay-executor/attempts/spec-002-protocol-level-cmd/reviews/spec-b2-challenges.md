# Spec B2 有界 adversarial challenges

## 目标、权威与攻击范围

- 角色：fresh independent adversarial generator `b_spec_b2_challenges`；非 producer、非 reviewer，未委派。采用 `generating-adversarial-challenges/SKILL.md`，本记录保持 immutable。
- 目标：`../spec.md`，B2，SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`；实际只读核对工作区 HEAD `2d8081ae2a3ef687e88ec284df9e97588cc625a7`。配套 `../verification/spec-b2-amendment.md` SHA-256 `ca46e1cef8daf0e5d3c717986e2c2818264b8c60a82a471e18371e0b365bab87`。
- Governing sources：子 `../TaskContract.md` r1 的 Goal／SC-8B／SC-13；根 `../../../TaskContract.md` r4 的迁移保留、行为与保护等价；根 `../../../spec.md` RQ-023(c) 的原样保留／位置等价、RQ-024 和 RQ-031(c)。`effect-registration-boundary-review.md` 与上述 amendment 是局部冲突及裁定输入，源码与原测试独立核对，不将 producer 或旧 reviewer 的判断当作本次结论。
- 攻击范围仅为 B2 的 RQ-006「即时登记失败的等价边界」、AC-003 与 AC-004/005、VC 表相应修改：UseStackable 主事务／Settled 后 RequiredExplicit、committed replay 普通 Write 后 CurrentStateBatch 两个出口的即时实际 encoder 故障。考察许可伪造／串用、编码出处与业务失败混淆、额外独立错误被 raw 保留遮盖、重放二次结算和事务／source-close 次序失真。不开启已批准 B1 的其他议题，不改 A sealed，不要求 Spec 提前给出局部 API 或尚未存在的 Task 实现证明。

## 保留的挑战

本轮保留 **0 条**：high-risk 0，low-risk 0。这是有界挑战结果，不是 review verdict、完整性证明或产品完成声明。

以下攻击路径已核对，未形成需要此时修改 Spec 的可信缺陷：

- **将相同 IOException 或整个登记阶段一概豁免。** 真实 `OutboxAdapters.cs:88–138` 区分 required direct 的抛出与 best-effort 编码失败后的逐目标继续；`MonsterDeathSlice.cs:455–491` 的原 protected 发布段又明确包装 typed failure。B2 `spec.md:69–82` 把两个具名旧出口、真实 adapter 的本请求／精确物理目标／本次 encoder 出处和最终错误条件同时设为门槛，拒绝按异常类型、文字、调用方自报或任意 Func 扩张；AC-003 保留非许可及原 typed 发布的对照。没有从本修订推导出可批准的全局豁免。
- **原编码失败发生后，cleanup 再失败却仍保 raw。** 当前 `CommandBus.cs:90–109` 的确会出现 body failure 与 scope dispose 独立 failure；`CommandWorkUnits.cs:90–104` 的默认策略先做 typed 包装及全参与者围栏。B2 要求 drain、所有工作和 scope cleanup 结束后的最终 failure 仍为单一原 cause，额外独立错误必须撤销保留并保留 cause 引用；AC-003/005 有对应可观察结果。当前产品不能原样承接这一要求，但这是已识别的后续实现输入，并非 B2 WHAT 的遗漏或不可实现要求。
- **将旧重放测试改到物理发送端以掩盖新增围栏／二次扣减。** 独立读到 `EffectOperationReplayTests.cs:97–126`、`:219–227` 的一次扣减及后来当前数量断言；`NpcShopPurchaseTests.cs:574–594`、`RecordedOutbound.cs:326–369` 和 `OutboundRegistrationParts.cs:82–101` 确认注错穿过真实登记 adapter，在实际 encoder 调用中抛出。B2 明禁移动注错点；AC-003 另要求原 cause 同对象、Committed、无新增 participant fence 和原 source-close 顺序，不靠 fixture 可再 dispatch 推论真实 socket 可重发。
- **把 replay 改只读、把登记放回事务内，或以重发原 ACK 代替当前投影。** `ReplayStateProjectionDelivery.cs:36–61` 的既有顺序是 snapshot 命令返回后 MirrorMerge／Create／batch／Town；`EffectOperationLedger.cs:57–74` 对 Committed 直接重放，finally 仅拒绝仍 Pending 的条目。B2 明确普通 Write 完成后保持原序，只保留 batch 的实际编码失败，不许可其前后业务失败，且不启用 Compose／延迟编码。AC-003/004 要求当前投影、不重复结算／原 ACK，足以供后继 Plan/Task 设计验证。
- **业务 catch 或泄漏句柄借许可继续写／成功返回。** B2 同时要求许可不可跨请求／目标／结束后复用，业务 catch 不能恢复结算权、继续写入或成功结束；AC-002/003 的生命周期与连续请求区分覆盖这一结果。具体来源证明与最终收口结构留给 Plan/Task，不把缺少接口预设计本身当作 Spec 风险。

## 证据边界与移交

仅静态读取治理制品、源码及原测试；未运行测试、native 程序或 git mutation，唯一写入为本新记录。Task10 已批准及新 effect/Main 仍 disabled 的阶段边界按本次输入与当前代码保持，不将现有实现误称已符合 B2。后继独立 reviewer 仍须自行判断目标与 governing outcome 的一致性；B2 gate 及后继 P2 gate 的批准权不属于本 generator。
