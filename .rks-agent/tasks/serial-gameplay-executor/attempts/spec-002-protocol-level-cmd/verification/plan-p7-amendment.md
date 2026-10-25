# Plan P7 amendment：NoRoom独立记账修复与六消息whole闭合

## 只读前版与本次授权

writer按writing-plans及references/task-units.md形成高层HOW，唯一workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`，起点HEAD `17d924a06b1237ca2b4f5997983c0a4923dfc050`。Spec B6 fresh独立approved已由主控消费，本稿不批准自己，不创建Task/实施产品或修改routing。根/child授权和主控HOW先后裁定见 [主控消费](spec-b6-plan-p7-controller-consumption.md)。

| 写前实读输入 | bytes | SHA256 |
|---|---:|---|
| 原P6 ready R2 `../plan.md`，逐bytes保存于 [plan-p6-before.md](evidence/plan-p7-amendment/plan-p6-before.md) | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| 已approved B6 `../spec.md` | 66834 | `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd` |
| immutable [B6挑战](../reviews/spec-b6-challenges.md)，high0/low0 | 5660 | `f1458e00c7f0e6df37c7b8f0e0bbd996d8c6f66d40f563d7b04e6d1e57eea6d3` |
| 独立 [B6 review](../reviews/spec-b6-review.md)，approved | 7889 | `4d4c7a69676a1860962b495a0b5a03adbf352dcf172764a68f10500606d60cbb` |
| 主控B6/P7消费，当前仅Spec门及P7生产授权 | 4229 | `9facfd245338036c1f464c31f5daa8be07a4d8b0a4f386d5dde2f1321ff5fdd7` |
| 根TaskContract r4 | 32812 | `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4` |
| child TaskContract r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |

baseline仍为 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`，A seal/已提交Task01～32及P1～P6历史记录不改。原P6复制而非重建，旧末尾「本地修订说明」到其末行逐bytes保留；当前Plan只新增P7说明，不追改已接受历史及原raw。

## 依真实源选择的最小HOW

1. **先独立修NoRoom记账，实质分提交。** `EquipmentRentalDomainService.cs:75–77`已有正常InventoryFull返回/RequireRollback；`UnitOfWorkBehavior.cs:55–60`实际Dispose后记RollbackConfirmed，`SelectedSessionSettlementBehavior.cs:19`随后无条件Settled；`EffectOperationContext.cs:102–107`未识别该正常回滚而要求Frozen/Projection。最小前置在旧本次Main正常返回收口识别已有真实确认事实，仍要求事务确已开始，无确认时保原缺投影错误；不改原Rental mapper/encoder、注册/handler/领域规则或显式runner。`RentalSlice.cs:52–56/:118–126`已编码Result2 body `01 02 00 00 00`。该独立语义单元不夹whole/纯移动，不硬编码Rental或新增authority。

2. **受影响旧Main范围可核。** 全RequireRollback直接正常return的其它源为 `World/BossTower/BossTowerDomainService.cs:111/113`、`World/DungeonAdmissionDomainService.cs:243–247`；`Game/BossTowerCommands.cs:9–14`和`Game/DungeonEntry/EnterDungeonCommand.cs:8–12`的fine仅IMultiSelectedSessionCommand，不是IRequestSettlementCommand。原For/ForSettlement只处理后者，正式`EffectSettlementRegistrationTests.cs:34–41/:68–71`同样区分；当前直接可证的旧Main正常返回影响为Rent NoRoom。Task仍核调用点和强回归，不把静态分类冒所有caller动态全故障。显式`CommandWorkUnits.cs:281–290/:312–319`已正常回滚→Main.Failed且只在Committed才Settled，`ExplicitEffectRequest.cs:43–51`允许ConfirmedRollback，故不为本前置更改显式core或另造恢复机制。

3. **四opening按既有能力闭合。** Compound/Lottery/Vending/Epic各自原fine的领域入口已经显式接收recorder，whole仅传当前unit.Footprint及保持原参数/clock/槽位，Main用现WriteResultAsync。窄动作异常→RequireRollback(cause)→Returned拒绝，与真实KnownStorage/FallibleCommit同cause确认回滚→CommitRolledBack区分，mapper在单元完成后应用；guard/登记/cleanup及取消/typed等无恢复资格。现十consumer保持、四具名新增后十四，不改runner或来源事实。Lottery1原premium在Main catch外，迁成第二ReadOnly直接领域查询；fine若还有其它caller不提前删，实际最后caller迁完才清零。成功投影与Failed隔离沿原composer/窄登记，不保Whole→旧Delivery→bus转发。

4. **Rental独立且不复制opening恢复。** 两原Main各用现WriteAsync/单精确selected；Rent原fine的DemandActive及TouchedSlots记录直接用unit.Footprint，Charge沿最低Inventory|Rental投影和原领域，不增加无意义recorder接口。正常空提交拒绝仍Committed；NoRoom正常rollback-only不走异常恢复。动作SQL/native Commit异常仍noACK/source-close。成功后原ReadRentalRefresh依Panel→角色库存→账号库存→clock顺序成为另一个ordinary Write，不是只读或第二Main。保原ACK/库存/panel/外观顺序和LuckyStarChanged/Panel/replay等共享caller；不顺便改领域规则、事件或其它来源。

5. **whole完整生命周期是明列变化。** 旧fine scope在 `CommandBus.cs:214`起内联路径，返回协议handler之前清理；whole `:87–128`在body/投影后清理，再 `CommandWorkUnits.FailAsync:370–384`依实际CommittedParticipants默认typed/精确保护。Plan不造第二scope、临时标记、伪Uncertain或撤帧来模拟旧点。独立NoRoom前置和能独立保持原行为的纯结构准备必须分提交；whole边界/显式请求/原编排吸收不可分的改变依VC-B6验收，不把它冒纯搬迁，也不夹能独立提交的纯移动/无关整理。旧fine/转发随对应whole删除，局部Task保独立结果快审。

6. **replay只沿现family且先补旧证据。** legacy `LegacyReplayStateProjectionDelivery.cs:18–24`在普通ReadReplayState事务后传null attempt；whole `CommandBus.cs:181–192`已可按实际注册签同一CommittedReplayCurrentStateBatch；sink `ReplayStateProjectionDelivery.cs:33–46/:47–62`的batch与后续Town分段不改。每条切换前取得真实old decoder/actualencoder固定cause oracle，再有限注册并新same-provider对照；Task32无encoder fault，不能据52场/静态共享提前授owner。只有限owner，不加第四类型、initial/RequiredExplicit新许可或Mailbox self。真实已Close不假装还能接包，账本重试与实际sender关闭分清证明范围。

## 验证与覆盖责任

Plan新增P7验证段明确：NoRoom先原decoder满32格真实RED→最小patch GREEN，全110表644列/NULL/BLOB/实际rowid回滚、Result2真实帧/无refresh、账本ConfirmedRollback/无FenceBlockDiscardClose、去阻塞后原entry/Ordinal只该次结算；普通空提交Committed和缺事务/缺Freeze/unknown/第二错误对照。异常输入的原cause/typed/source-close保持，不把正常返回NoRoom伪装异常恢复。

四opening/Rental迁后按原事务边界、实际domain/SQLite/outbox/production sender对照，保健康/拒绝/早门/真实SQL及native来源，Vending/Epic缺逐路native等已有局限不冒动态全覆盖。whole真实DI资源scope故障前可已有success/refusal帧，按真实unit存活顺序Send→actualClose/chain；Main后独立refresh不撤Main、ledgerCommitted仍不能重结算。Committed/Uncertain默认精确actor保护与纯ConfirmedRollback后第二普通error分开；C1/C2/独立同cause错误保事实。真实替换session/不同physical/无关玩家不误伤，受控故障/提前enlist同SQLite/非TCP-A21局限如实记。

有限replay owner每条先旧actualencoder再新oracle，验证cause引用/entry/Ordinal/Committed/失败batch未登记/noFence/sourceClose/current投影不二结算，并保clock/业务组帧/非许可登记和额外cleanup默认反例；优先复用现phase/source/owner/cleanup强例和可编译mutation，不做无价值笛卡尔积。正式注册判据、原领域/codec/wire/共享ApplyEffects/ItemDelivery/LuckyStar强断言及真正剩余caller驱动按受影响范围更新，不能删难以wire表达的拒绝断言或用SQL预置替代真实业务。

每Task选择实际受影响相关full和聚焦保护；共享runner/来源/事务core若需改变先返主控并扩大八freshfull。原5m/类并行/Skip/DFOexpected/actualcontrols、Detailed原日志/命令/UTC/exit/自然结束、sourcefreeze与加载DLL身份、逐case增减账保持；partial/零DFO不冒七程序集74/7。Task31 9075/DFO74+7仅历史库存、Task32仅52/142调查，本稿无新通过。最终T-012八freshfull/Golden/Char/2秒同工具测量/文档仍完整有责。

本次受影响Plan header、§4/§5、T-004/005、Verification/NextBoundary及P7修订说明；T-001～012路线/17RQ/11AC覆盖保持，VC列表仅跟approved B6增加交叉引用。T-004其余四消息、T-006～012/未闭来源及过渡不因Mailbox/调查done自动done。NoRoom前置不闭canonical行，whole对应实际consumer完成才精确闭行；严格工具及八坏例/NUL nonowned/owned manifest/行尾/四类whitespace和结果required审核随Task执行。

## 门及停止

本P7 state:ready仅高层可审，无执行状态。writer仅写 `plan.md`、本amendment和原P6快照；未改Spec/gates/root/child/A/Task/routing/canonical/产品/正式测试，未运行native/构建/test或Gitmutation。下一步main指定fresh immutable P7挑战与另一fresh独立Plan review；两门及main消费、amendment-only本地检查点之后才详细规划Task33。旧P6/B6/Task32批准不能代此门，Astra/用户收尾及不pushPR/远端/版本tag/部署/游戏/cleanup保持。没有需改变Spec或等待用户的material未决项；本稿不自批，写毕停止writer。
