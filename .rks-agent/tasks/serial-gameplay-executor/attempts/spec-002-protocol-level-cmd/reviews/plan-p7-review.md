# Plan P7 ready R1 — 独立审核 R1（2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

## 目标、分辨率与独立性

本 fresh reviewer 独立于 Plan producer、挑战生成者、Spec producer/reviewer和主控。已读 `reviewing-work/SKILL.md` 与 `references/plan.md`；对象是 `mode: task-units` 的高层 Plan P7 ready R1，判断其路线、共享契约、依赖、提交分离、验证及下一有用单元能否交付 approved B6，不是 Task-plan、代码结果或整个 Spec 最终批准。唯一工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，实际HEAD `17d924a06b1237ca2b4f5997983c0a4923dfc050`；无 `.codegraph/`，直接检索当前源。

| 实际目标／输入 | bytes | SHA256 |
|---|---:|---|
| `../plan.md`，P7 ready R1 | 108854 | `651119f9995057344f3dc8d064c901eb90d081a00f9999d662343ce380fbe8d4` |
| `../verification/plan-p7-amendment.md` | 9588 | `7b5d2b819bbe444a753faa0ce49c447b3a05801ad7171bfd07c1b2652d8a9c75` |
| immutable `plan-p7-challenges.md` | 7179 | `5d9351b429a8c9c8960c8d4442fdb5ba7156746776fa1fe5f70882f7ab837127` |
| P6 before snapshot | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| approved B6 `../spec.md` | 66834 | `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd` |
| 独立 `spec-b6-review.md`，approved | 7889 | `4d4c7a69676a1860962b495a0b5a03adbf352dcf172764a68f10500606d60cbb` |
| 主控 B6/P7 消费，当前为Spec门及P7生产授权 | 4229 | `9facfd245338036c1f464c31f5daa8be07a4d8b0a4f386d5dde2f1321ff5fdd7` |
| 根 TaskContract r4 | 32812 | `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4` |
| child TaskContract r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |

实际读 Plan 本次diff、共享契约及全部T-001～012/交接覆盖、P7验证和NextBoundary、配套amendment、匹配挑战、approved B6专节/相关验收及审核、主控顺序与根/child约束，并直接核当前生产与正式判据。P6 snapshot与 `HEAD:plan.md` 逐bytes相等，原P1～P6「本地修订说明」完整保留；17 RQ/11 AC及12条粗包身份未丢失。未把TASK-32调查approved当Plan或产品批准。

## 决定性核查

**独立NoRoom语义前置可执行且范围有依据。** 当前 `EquipmentRentalDomainService.cs:75–77` 正常返回InventoryFull并RequireRollback；旧UOW `UnitOfWorkBehavior.cs:55–60`实际Dispose成功后才记RollbackConfirmed，`SelectedSessionSettlementBehavior.cs:19–24`随后无条件Settled，而 `EffectOperationContext.cs:102–107`要求未发生的Frozen/Projection。原 `RentalSlice.cs:52–56/:120–128`已有Result2 mapper及 `01 02 00 00 00`，无需改编码或领域。独立抽核Task32 matrix-04/rent-noroom原raw：before与initial结束完整110表（含实际rowid资料）逐值相等，真实入站decoder/body/ordinal和typed错误可见，physical实际0帧/Close1，符合待修冲突。

P7的前置仅在本次已开始事务的Main正常返回收口识别真实确认回滚，保无确认时缺事务/Freeze/Projection程序错误，不授异常/取消/不确定或第二错误恢复；不改原mapper/encoder、注册/handler、领域或显式runner，不在core硬编码Rental。独立枚举当前RequireRollback源点，除Rental外直接正常return的BossTower和DungeonAdmission路径，其fine仅IMultiSelectedSessionCommand，接口不继承IRequestSettlementCommand，For/ForSettlement确实不处理它们；正式 `EffectSettlementRegistrationTests.cs:34–41/:68–71`列20旧Main及这两个non-Main，与Plan分类一致。其它被检索源点为重抛或已有explicit unit，未发现本前置暗扩另一路旧Main的直接normal-return源点。该核查是静态影响分类，不是共享caller全故障通过。

**新增分支有能失败的验证，且无需先预写Task。** P7要求先在原registered decoder/dispatcher/fine满32格路径取得真实RED，再最小patch GREEN；同run全110表/644列含NULL/BLOB与实际item_slots rowid、暂存扣星和回滚、Result2完整帧、ConfirmedRollback/no refresh/无新增保护关闭直接核。去满包后同raw frame保entry/原Ordinal且只结算那次，正常空提交Committed及非法/缺事务/缺Freeze/第二错误对照分开。当前 `CommandWorkUnits.cs:281–290/:312–319`已正确normal rollback-only→Main.Failed、只在Committed才Settled，`ExplicitEffectRequest.cs:43–51`接受ConfirmedRollback，故不需要另改该core或制造新authority。现有真实事务/库存强断言与Main/non-Main判据继续保留，具体driver和过滤留Task不妨碍本高层路线ready。

**四opening与Rental没有被错误统一恢复或提交。** 当前Compound/Lottery/Vending/Epic原catch确实包旧fine的SendAsync，窄业务映射与原响应可直接复用；P7只给这四路增加现WriteResult的具名consumer，保原十个、KnownStorage/FallibleCommit同cause/真实完成/cleanup门和Write/ReadOnly默认sticky，不改runner或来源事实。受管理guard即使随domain await进入catch也不得据RequireRollback恢复；投影/encoder/cleanup不纳窄动作try。Rental无对应普通catch，P7用现WriteAsync保其异常确认回滚noACK/source-close，健康NoRoom只是正常rollback-only，不冒异常恢复。原Rent DemandActive/TouchedSlots改传本次unit.Footprint、Charge最低Inventory|Rental不造额外规则。Lottery mode1 premium在原catch外并另ReadOnly，Rental成功后Panel→角色库存→账号库存→clock另ordinary Write，ProtocolModule真实注册分别为ReadOnly/Transactional；Plan保持这些点，不合Main、不递归bus。

当前ReadPremiumSnapshot另有usage report/daily refresh caller，ReadRentalRefresh由租/充共用；P7明确剩余真实caller未迁不提前删除fine，最后caller完成才清零。原共享OpeningInventory/ApplyEffects/ItemDelivery、LuckyStar/Panel/通知路径及强断言仍有责任，不留whole转旧bus或顺便改变规则/事件策略。

**whole失败按真实最终边界，帧与保护条件均可核。** `CommandBus.cs:87–128`在body之后drain→Close request→真实DI scope cleanup→一次FailAsync，旧fine的`:214–255`在返回协议handler之前结束scope；P7明确该固有位置变化属于VC-B6。真实scope资源Dispose注错、故障前已登记success/normal-refusal帧、真实unit状态、实际Send→Close/原chain都有验证责任，不拿body手抛冒scope、不补失败ACK/撤帧/提前flush/承诺零帧。Main后的独立premium/refresh不能撤Main durable，第二单元完成与清理事实分开。

`FailAsync:370–384`依HasCommittedUnit/CommittedParticipants收口，`ExplicitEffectRequest.Failed:43–46`保已Committed ledger。P7对应保Committed ledger/禁止重结算，真实Committed/Uncertain默认参与者保护与纯ConfirmedRollback后普通第二错误不伪Uncertain/一概围栏分开；独立同对象错误保发生事实。精确旧actor与真实旧physical、replacement及无关玩家必须直接对照，不以ConnectionId或“Exact”名称代隔离。没有扩大默认保护到无事实支持的所有scope。

**有限replay owner仍有真实切换前置。** 当前legacy `Game/LegacyReplayStateProjectionDelivery.cs:18–24`在原普通ReadReplayState写单元后传null attempt，whole `CommandBus.cs:181–192`已按真实registration签现CommittedReplayCurrentStateBatch；`Game/ReplayStateProjectionDelivery.cs:33–46/:47–62`区分实际batch和后续Town，`ExplicitEffectRequest.cs:54–65`核Committed/replay/replayCommitted/精确physical，注册判据也已有有限effect绑定条件。P7不改这套框架；六路逐具名旧decoder/实际encoder fixed-cause oracle在切换前完成，再有限注册和新same-provider同oracle及默认/cleanup反例。TASK-32的52场没有encoder fault，Plan明确其缺证，不能凭静态共享或调查approved先授owner。已实际Close的transport不假接收，账本/current投影和真实sender关闭按各自范围证明。首次成功/普通拒绝、其他两类许可及Mailbox self不获新权，既有source/phase/owner强例及可编译mutation按实际触及复用，不造无价值笛卡尔积。

## 挑战裁决、分提交与后继责任

immutable挑战保留集high0/low0，无待裁CH项；零挑战不作为自批理由。独立核查未见本Plan门必须纠正的material defect，判 `approved`，不附remediation字段。

根纯搬迁/语义分提交仍生效：P7先把有独立用户价值的NoRoom记账修复单独交付，不与whole/注册迁移混合；能独立保持行为的纯移动/抽取如确有真实依赖则另作等价准备提交，不先造无consumer空壳。whole不可分的请求/DI/编排边界变化依approved VC-B6验收，不冒方法体纯搬迁，也不以提交标签豁免根约束。该分离在本高层已可执行，Task执行仍须核实际owned diff，不能把本批准当混合无关改动的许可。

T-004其余Rename/Hatch/SharedEffect/GenCeraTicket、T-005实际Rental、T-006～012及全部未闭来源/过渡机制保持责任，T-011在真实consumer闭合后删除、T-012仍需八fresh full/完整构建/Golden/表征/同工具2秒测量和文档。每Task required结果快审、风险相关full及最终全量处于正确层级；partial或零DFO不冒七程序集74/7，原断言/并行/5m/Skip及失败身份规则保持。具体Task命令/fixture/case后定不是本Plan缺陷，所有待做证据也未被计成新通过。

本review仅批准当前高层Plan可供主控消费，不创建Task或声明实施/Spec完成。NoRoom新原decoder RED/GREEN、六路old encoder oracle/新whole证据及实际scope/刷新保护仍待各后继Task。主控完成本门消费与amendment-only本地检查点后才能详细规划/实施Task33；旧P6/B6/Task32批准不代本Plan门。A/root/child/baseline、Astra和用户收尾、不pushPR/远端/版本tag/部署/游戏/cleanup限制保持。

写前与写后重核目标/上游身份一致；本reviewer只新增此文件，无native/构建/test/探针、产品/正式测试/canonical/其他artifact修改或Git mutation。writer停止，交主控。
