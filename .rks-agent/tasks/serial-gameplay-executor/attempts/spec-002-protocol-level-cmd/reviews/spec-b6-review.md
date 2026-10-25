# Spec B6 ready R1 — 独立审核 R1（2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

## 目标、判据与独立性

本 fresh reviewer 独立于 B6 producer、challenge generator 和主控，使用 `reviewing-work/SKILL.md` 与 `references/spec.md`，只判断本次受影响 WHAT 是否符合上游、清楚可实施且可判定。不是 Task-plan／代码结果／完整 Spec 最终审核，也不替代后续新鲜验证。实际工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，HEAD `17d924a06b1237ca2b4f5997983c0a4923dfc050`；无 `.codegraph/`，因此直接检索当前源。

| 本轮实际目标／输入 | bytes | SHA256 |
|---|---:|---|
| `../spec.md`，B6 ready R1 | 66834 | `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd` |
| `../verification/spec-b6-amendment.md` | 9696 | `3fd97177c73e11bc687931ab19d3c90a68c2e49de1493744a0d3f0ffdcec9ebb` |
| immutable `spec-b6-challenges.md` | 5660 | `f1458e00c7f0e6df37c7b8f0e0bbd996d8c6f66d40f563d7b04e6d1e57eea6d3` |
| 根 TaskContract r4 | 32812 | `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4` |
| child TaskContract r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| `../verification/spec-b6-controller-decision.md` | 6900 | `7fab8c2ace4ffb0f58038690c52f8e1c6e15c14cb3974084e07ed91a7e4947da` |

读当前 Spec 及配套 amendment、根 Goal／Scope B／Constraints／授权、child、主控后继裁定、TASK-32 的已提交 report／case manifest／相关 raw、B5 before 及当前生产机制／六路协议。独立核 B5 before 与 `HEAD:spec.md` 逐 bytes 相同，B1～B5历史段保留；17 RQ／11 AC IDs完整唯一，未重写根／child或A。零挑战本身不是批准依据。

## 决定性核查

**原行为与新 WHAT 的界线成立。** 根 Goal 和 child Goal 允许经审查逐条列出的有意变化，根 Constraints要求显式说明可见差异并保留保护意图。B6仅把四 remaining opening、Rental六消息的 NoRoom／scope／独立刷新列为VC-B6，在RQ-006/007、专节、AC-003～005/011对应；其余要求和既有VC保持。它没有把旧调查 approved当新语义／实现批准，也未重开A或改变执行器、发送端、数据库模型、字段／编码。

**NoRoom是健康正常回滚，不能冒已提交。** 直接核 `EquipmentRentalDomainService.cs:75–77`：真实NoRoom设置RequireRollback并正常返回InventoryFull；旧 `UnitOfWorkBehavior.cs:55–60` 完成Dispose后记RollbackConfirmed再返回，`SelectedSessionSettlementBehavior.cs:19–24`却无条件Settled，后者在`EffectOperationContext.cs:102–107`要求Frozen／Projection。原`RentalSlice.cs:52–56/:118–126`已有Result2的`01 02 00 00 00`，无需新协议。抽读matrix-04 `rent-noroom.json`：实际ConfirmedRollback／RollbackOnly、Frozen=false、ledgerBlocked、全生产表及rowid before=after、零帧／actualClose1，与被修缺口一致。B6要求真实满32格、Result2、ConfirmedRollback、健康无新增Fence／Block／DiscardExact／关闭及无额外refresh；同帧retry保原entry／Ordinal，不混已提交replay。现`CommandWorkUnits.cs:281–290/:312–319`及`ExplicitEffectRequest.cs:43–51`已支持健康rollback-only和只有Committed才Settled，Spec未强迫新恢复框架或扩大其它旧Main。接口图和正式`EffectSettlementRegistrationTests`也区分BossTower／Dungeon的non-Main，不借此修所有旧rollback-only。

**六路恢复与提交点没有被统一。** 实际ProtocolModule注册六个Main为Transactional，premium为ReadOnly、Rental refresh为另一个Transactional；LotterySlice中premium在Main catch外，Rental成功后才refresh，拒绝直接返回。四opening普通catch覆盖旧fine SendAsync，Rental无同样catch。B6保四opening健康action／真实native确认回滚的原拒绝，而Rental同类异常仍无ACK／关闭；正常返回空提交拒绝仍按Committed，与NoRoom分开。范围没有用异常类／文字或IsSettled授恢复，guard／sticky／取消／未知／独立cleanup都仍须按实际失败处理。C1 durable Uncertain／C2双因原结果保持，不据SQL回滚快照改称健康确认回滚。

**scope与刷新差异明列且可验收。** 旧fine scope是`CommandBus.cs:214–255`内的真实DI边界，Dispose早于返回协议handler；whole的`:87–128`覆盖body／投影后清理再FailAsync。matrix-04的`compound-fine.json`确为Main/ledgerCommitted、原catch失败ACK、source健康；`rent-refresh.json`／`lottery1-refresh.json`是MainCommitted＋第二单元ConfirmedRollback，`rent-refreshscope.json`两个单元Committed，旧均无新增Fence。B6明确新whole完整失败边界、Main后独立单元不合并／不能回滚Main；scope前可有原success／normal refusal条目，按真实状态和顺序存活后实际关闭，不补失败ACK、不承诺零帧、不撤消息／提前flush。`FailAsync:370–384`按HasCommittedUnit／精确参与者保护，`ExplicitEffectRequest.Failed`不改已Committed ledger，B6都作了正确区分；仅ConfirmedRollback后普通第二错误不被强记Uncertain／一概围栏。AC-005要求真实scope资源、cause／独立发生、实际Send→Close／原链与真实替换身份及无关连接对照，不拿名称Exact代隔离。

**有限replay owner没有提前获得证据或扩大类别。** 直接核legacy `LegacyReplayStateProjectionDelivery.cs:18–24`的原普通写后null attempt，whole `CommandBus.cs:181–192`按实际注册签同family attempt；`ReplayStateProjectionDelivery.cs:33–46`为CurrentStateBatch、后续Town另段，`ExplicitEffectRequest.cs:54–65`核Committed／replay／真实精确物理目标。TASK-32实际52场fault集合没有encoder注错，B6正文及AC-003明确逐具名旧decoder／实际encoder fixed-cause oracle在切换前补，再同容器新路径和来源／默认／独立cleanup反例对照，未满足不授owner并回受影响gate。这是后继实现的真实前置义务，不是当前已通过事实。只沿现有CommittedReplayCurrentStateBatch family的六有限owner，不增加第四raw类型、首次成功raw、RequiredExplicit／CollectionBox initial权限或Mailbox SelfSettledAction。当前源实际十个WriteResult消费者与amendment名单吻合，Mailbox只一个具名whole的自结算注册；B6当前计数纠正保历史，未改其来源／默认保护。

## 挑战裁决、分提交与后继边界

匹配immutable挑战保留集high0／low0，没有待裁CH项。独立核查未发现本轮Spec门必须纠正的material defect，判`approved`，不附remediation字段。

根「纯搬迁与语义改动分开提交」没有被本审核豁免。B6明确独立NoRoom修正与可独立纯结构准备分开，禁止仅把混合提交改名“语义提交”；whole生命周期变化则作为VC-B6如实记录。后继Plan必须从真实依赖落实可执行分离，不能把此处没有预写详细Task／API当成自由混合许可。本轮只批准WHAT足够明确，不批准尚未产出的P7或具体分解。六路encoder旧oracle、迁后raw/default保护、NoRoom／scope／refresh及精确来源证据仍待后继Task交付；Task32的52场／142focused仅旧调查，未测Vending/Epic逐路native等限制保持，不能冒八full／DFO74+7／性能2秒／TCP/A21或所有caller故障全覆盖。

目标精确身份在写前及写后重核不变。本reviewer只新建此review文件，无native／构建／测试／探针，没有改目标、Plan、routing、Task、canonical、产品／正式测试／raw、git或远端。writer停止，交主控消费本门；后继受影响P7须另经fresh challenge／独立Plan review、主控消费及amendment-only本地检查点，之后才可详细规划／执行新Task。Astra与用户收尾及不pushPR等门保持。
