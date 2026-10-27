# Spec B2 amendment：具名即时编码登记失败兼容边界

## 身份与授权

- 目标：[Spec B2](../spec.md)；承接B1，子[TaskContract](../TaskContract.md) revision 1、根[TaskContract](../../../TaskContract.md) revision 4均不改。
- Spec baseline保持`10a7218a779493494b138bd6b43cc0fc1a84aa6a`；本次落稿的产品检查点为TASK-10已独立approved的`2d8081ae2a3ef687e88ec284df9e97588cc625a7`。
- 直接输入：[独立边界复核](../reviews/effect-registration-boundary-review.md)，结论为`changes_required / material / spec`，限首次新effect admission/Main启用前的共享失败边界，不是完整Plan重审或Task结果否定。
- 主控明确裁定：沿用户已授权的保A行为目标处理此有界amendment，不新增VC、不改根契约或terminal状态；先B2及受影响独立challenge/review，再Plan P2及其受影响审查，再amendment-only本地检查点，最后才授权后续产品单元。此次writer仅改Spec及本记录，不修改Plan、契约、A、旧Tasks/reviews或产品，不启动Task11。

## 原冲突及证据

权威要求同时成立：根契约的Goal/结果B迁移保留项要求协议行为及原保护等价，子契约第42行要求相对A行为不变；父[Root Spec](../../../spec.md) RQ-023开头要求“以下保护原样保留，在CMD内的位置等价”，其(c)要求提交后非传输失败“保持今天的处理方式”，以MonsterDeath原全参与者围栏为例。故不能把搬入协议CMD后的语法范围当成新的失败语义，也不能反向推出所有codec故障都不围栏。根与A资料保持封存。

以下源码定位以当前Task10检查点为依据；独立review先前检查HEAD为Task09，其边界结论与此处一致。本轮只读复核及文档落稿，没有执行产品测试。

| 证据 | 实际事实与冲突 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/UseStackable/UseStackableSlice.cs:346–376`、`:399–402`；`Application/Commands/Pipeline/SelectedSessionSettlementBehavior.cs:18–24`；`Application/Effects/EffectOperationContext.cs:102–107` | fine主命令完成事务及Settled之后返回，handler随后即时组装并RequiredExplicit登记。该登记的raw故障不再经过旧主结算管线 |
| `Server/src/Dfo.Server.Application/Effects/EffectOperationLedger.cs:57–74` | Committed请求进入当前状态重放；finally只将仍Pending的条目改Rejected，没有将后续raw登记故障改Blocked |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs:97–126` | 首次实际登记IOException后仅扣一次；同fixture、同物理connection后续同raw请求重放当前库存、不重发原ACK |
| 同文件`:219–227` | committed replay的batch登记IOException后仍可重放；后来库存改7，投影读到7，未再次执行业务 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs:574–594`；`Server/tests/Support/RecordedOutbound.cs:326–369`；`Server/tests/Support/OutboundRegistrationParts.cs:82–101` | FailExplicit/FailBatch经同一Registering的in-flight标记进入实际FaultingEncoder；这是登记编码点，错误文字含transport不改变来源。不能搬走注错点或改变原业务断言 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs:788–789`；`Application/Effects/ReplayStateDomainService.cs:22–33`；`Protocol/Game/ReplayStateProjectionDelivery.cs:36–61` | ReadReplayState原注册Transactional。其返回后才MirrorMerge→Create→batch→Town。后继应普通Write内直接领域读取，随后事务外原序；不能ReadOnly或nested bus |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:90–104`；`Application/Commands/CommandBus.cs:90–109` | 当前FailAsync对所有HasCommittedUnit的非typed错误包装PostCommitPublicationFailure并围栏；scope dispose另有故障会合并再收口。原样用于新Main/replay会破坏上述旧边界 |
| `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:455–491`；`Server/tests/Dfo.Server.Application.Tests/Transactions/CommandWorkUnitsTests.cs:265–317` | 原受保护发布仍typed并围栏所有捕获参与者；普通提交后IOException及独立cleanup错误已有默认保护对照，不得被新保留吞掉 |

旧fixture同连接再次dispatch只证明账本/围栏/重放语义，不证明真实socket被关闭后仍可接收请求。原source close仍由A的作业发件箱顺序承担，与selected participant fence分开判断。

## 兼容裁定与本稿变化

只允许两个已证具名出口：UseStackable已Settled后的RequiredExplicit；Committed replay普通Write后的CurrentStateBatch。具名许可与真实适配器证明的本次实际encoder出处须同时成立，绑定本请求、精确物理目标及本次调用。drain、所有工作及scope cleanup结束后，最终失败仍须为同一原始cause对象，并且没有typed不确定性或额外独立错误；才保raw对象、Committed、不新增participant fence、原source close与后续合法同序号当前投影重放/不重结算。

其他提交后业务组装、账本/原typed发布、来源不明、登记守卫及额外独立dispose/cleanup失败仍按原范围保护全部精确参与者，MonsterDeath/祭坛不变。无IOException或错误文字全局豁免、无调用方自报或任意Func忽略区。保留资格不可跨请求/目标、closed/leaked复用，业务catch也不能继续写或成功结束。未来出口逐项取证/审查，不预授权。

Spec只改RQ-006及其等价边界说明、AC-003、AC-004/005交叉引用、VC“其余”保留与B2修订记录；17RQ/11AC、VC-B1/B2/B3、所有其他要求不变。RQ-008及F-132原条件不变，不启用Compose/deferred composition/encoding，不称F-132已修。具体attempt/receipt、接口/注册/异常收口实现不写入Spec，由Plan/临近Task以最小机制决定；不改路由或协议CMD中心方案。

## 当前及后继gate

1. 本稿是producer交付，Spec保持`state: ready`不等于approved。主控须对B2取得新鲜有界adversarial challenge，再由独立reviewer审核匹配版本；不能把本记录或旧review当B2批准。新material-risk basis是许可伪造/串用、把业务错误误认编码来源、把额外独立错误掩盖为raw。
2. B2受影响gate通过后，才正式制作Plan P2：对齐§4异常/工作单元边界、必要的§5窄登记能力与T-004共享回归输入，不重写其余路线；进行受影响的独立challenge/review。当前P1未由本writer更改，不将其已有批准当成新边界已批准。
3. B2/P2及匹配gate通过后，由主控做amendment-only本地检查点；此检查点不包含产品迁移、不宣告Spec完成。然后才授权临近Task及产品写入，不提前要求尚不存在的Task11提供实现结果。
4. 后继效果单元保留上述两条原注错与业务断言，补raw cause同对象/Committed/无新增fence/source-close对照、相同IOException来自业务/非许可/原typed发布仍全员fence、登记加独立dispose双故障撤销保留、closed/跨请求拒绝、catch后不能继续写及有价值mutant。actual同容器encoder路径、Required/BestEffort和原事务/帧序不改。具体计划/证据届时落盘，不把本清单计为已跑结果。

## Task01–10为何仍有效

本发现局部约束后续首次新effect admission/Main启用，不追溯改变既有Task结果。当前`Server/src/Dfo.Server.Application/Commands/CommandBus.cs:126–127`仍拒绝显式effect CMD；`CommandRequestContext.cs:97–99`的两个effect状态仍无工厂，`CommandWorkUnits.cs`也未启用MainSettlement。UseStackable/replay仍走已证legacy路径，Task04的普通提交后业务失败默认围栏保持正确。

Task10只完成mandatory EffectExecutionContext/ApplyEffects及真实上游参数与捕获足迹；其Result/独立approved及主控检查点未启用新Main、不关闭Spec或来源pending。Task10正式02原证据为8523＝8503pass＋2准确原Dimensionfail＋18skip，8499＋N24－D0；这是已接受Task记录的数量，不是本轮重新运行结果。本稿不修改Task01–10的产品、断言、Result、review或证据，也不把既有结果扩大为新边界实现已证明。

后继修改共享失败机制时，应按受影响保护补证，继续保留既有普通postcommit/typed/全参与者、事务回滚、source-close及生命周期守护；无需为此改根契约、重开sealed A或把B判terminal。最终Spec/根完成、Astra及用户收尾授权保持原归属；无推送、PR、合并、部署或清理。
