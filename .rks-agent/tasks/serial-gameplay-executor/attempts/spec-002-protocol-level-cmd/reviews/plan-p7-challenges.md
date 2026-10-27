# Plan P7 ready R1 — 独立有界挑战（2026-10-25）

## 目标、来源与独立性

本 fresh context 独立于 Plan producer、Spec producer/reviewer及后继 Plan reviewer，使用 `generating-adversarial-challenges`。仅生成不可变挑战，不作 review verdict、产品通过或实现/生命周期批准。工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；HEAD `17d924a06b1237ca2b4f5997983c0a4923dfc050`，无 `.codegraph/`，因此直接检索当前源码。

| 本次目标/输入 | bytes | SHA256 |
|---|---:|---|
| `../plan.md` P7 ready R1，mode task-units | 108854 | `651119f9995057344f3dc8d064c901eb90d081a00f9999d662343ce380fbe8d4` |
| `../verification/plan-p7-amendment.md` | 9588 | `7b5d2b819bbe444a753faa0ce49c447b3a05801ad7171bfd07c1b2652d8a9c75` |
| P6 before snapshot | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| approved Spec B6 `../spec.md` | 66834 | `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd` |

实际读本次 Plan diff/相关共享契约、全部 Work and Dependencies、P7 验证/NextBoundary/amendment、B6 专节及对应独立 approved review、主控 B6/P7 消费、根 r4 和 child r1 的目标/约束。另直接核 NoRoom 领域→旧 UOW/结算→legacy effect frame、显式 runner/Main/默认终判、旧与新 replay reader/sink 和注册判据。TASK-32 的报告及 case manifest 是旧证据输入，52场 fault 集合确无 encoder fault，不升级为本轮新运行。

攻击范围限本次 HOW：独立 NoRoom 正常确认回滚记账前置、原协议编码和事务/账本结果；四 opening 与 Rental 不同恢复出口及原 Main/独立刷新点；whole scope 与真实帧/关闭/精确参与者保护；六个现有 replay family owner 的先旧后新资格；原十 consumer/Mailbox/self/raw/default 契约及其余粗包覆盖、根分提交约束。高层 Plan 不预写临近 Task 的全部命令/fixture/case，本轮不将这些后定实现细节视为缺陷。

## 保留集

- high-risk：**0**。
- low-risk：**0**。

未发现需保留的、能从本目标缺陷连到错误/不可实施结果且后续不能便宜控制的挑战；不为数量制造 CH 项。这不是完整性证明或 `approved` verdict，另一 fresh reviewer 仍须独立判断当前目标。

## 决定性核查

1. **NoRoom 前置有独立价值且不靠伪造提交事实。** `EquipmentRentalDomainService.cs:75–77` 的正常 InventoryFull/RequireRollback、`UnitOfWorkBehavior.cs:55–60` 的真实 Dispose→RollbackConfirmed、`SelectedSessionSettlementBehavior.cs:19` 的正常返回 Settled，以及 `EffectOperationContext.cs:102–107` 的未 Freeze 拒绝构成实际冲突。P7 只让本次已开始事务的 Main 正常返回识别已有确认回滚；无真实确认仍保缺事务/Freeze/Projection错误，不授异常或不确定恢复。原 mapper/encoder、注册和领域规则不动，独立提交后才 whole。当前显式 `CommandWorkUnits.cs:281–290/:312–319` 已正确 Failed(rollback)/仅Committed才Settled，不必另改共享 core。其它直接正常 rollback-only 的 BossTower/Dungeon fine 不是 IRequestSettlementCommand，原 For/ForSettlement 及正式登记判据不把它们作 Main；Plan要求临近 Task 再核而未冒所有 caller 故障通过。

2. **最小验证能揭露旧缺口，也约束新增分支。** P7 不以 domain 直接返回替代入站：要求 old registered decoder/dispatcher/fine 满32格真实行为 RED→最小patch GREEN、同run全资产/NULL/BLOB/实际rowid回滚、Result2完整body、ConfirmedRollback/no refresh/no新增Fence/Block/Discard/Close及原entry/Ordinal合法retry；另保正常空提交Committed、非法无事务/无投影/第二错误/typed不确定对照。旧 `EffectOperationReplayTests`、`ExplicitEffectCommandTests` 的真实库存/事务强例和 Main/non-Main 判据继续保留。具体驱动/过滤在详细Task确定，不是本Plan层前置缺口。

3. **恢复消费者和事务点被明确分开。** 四opening用现 WriteResult、窄动作映射和原 KnownStorage/FallibleCommit 同cause/真实完成/cleanup 门，guard/登记/cleanup不借业务catch恢复，现十consumer不变、只四个具名接入。Rental保WriteAsync且异常回滚仍noACK/source-close，正常NoRoom不冒异常恢复；原Rent DemandActive/TouchedSlots走当前unit.Footprint，Charge保最低Inventory|Rental。Lottery mode1另ReadOnly、Rental成功后另ordinary Write依原Panel→角色库存→账号库存→clock，不合Main、不递归bus。fine仍有其它caller时不得提前删，最后真实caller迁完才删除；共享规则/事件不顺便改。

4. **whole失败使用真实最终边界。** 当前 `CommandBus.cs:87–128` 在body之后drain/Close request/DI scope cleanup再FailAsync，与旧fine scope在协议响应前不同；P7明确此固有生命周期变化属于VC-B6，要求真实resolved资源Dispose而非body手抛，保故障前已登记success/normal refusal按真实unit顺序发送后实际Close/原chain。不撤帧/补失败ACK/伪Uncertain求兼容。`FailAsync:370–384` 依HasCommittedUnit/精确CommittedParticipants，`ExplicitEffectRequest.cs:43–51` 保已Committed ledger；Plan对应区分Committed/Uncertain默认保护与纯ConfirmedRollback后的第二普通错误，要求真实replacement physical/session及无关对照，未将所有scope统一围栏或Blocked。

5. **六个有限 replay owner 的证据仍是前置。** legacy `LegacyReplayStateProjectionDelivery.cs:18–24` 在旧普通写后传null attempt；whole `CommandBus.cs:181–192` 按真实registration签现family attempt；`ReplayStateProjectionDelivery.cs:33–46` batch与`:47–62`后续Town分段保持。P7要求切换前逐具名旧decoder/actualencoder fixed-cause oracle，之后新same-provider同oracle，直接核cause/entry/原Ordinal/Committed/失败batch未登记/noFence/source-close/current投影不二结算；已Close不能假接收。TASK-32实际52场无encoder，Plan没有据此提前授owner；失败返受影响门，不增类型/首次成功raw/Self。非encoder/额外cleanup默认反例及现source/phase/owner强例、可编译mutation按实际触及保留，不要求无价值故障笛卡尔积。

6. **分提交和全局剩余责任没有丢失。** 独立NoRoom语义修复不夹whole/纯移动；可独立等价结构准备另提交，现机制已可用则不预造空壳。whole不可分的请求/DI/编排边界变化明列VC-B6，不冒方法体纯搬迁，也不能靠标签豁免根约束。T-004其余四消息、T-005 Rental、T-006～012及来源/过渡删除仍有责任；每Task required结果审核、受影响full/聚焦回归与最终八freshfull/2秒测量各在正确层级。P7明确未执行、部分DFO不冒全量和原断言/期限/并行/Skip保持，未沿旧P6或B6批准硬过Plan门。

以上为只读制品/源码核查，没有 native、构建、测试、探针或 Git mutation，也没有新字节/事务/性能/TCP-A21通过主张。仅写本不可变挑战文件；完成后停止 writer，交主控选择另一 fresh reviewer。
