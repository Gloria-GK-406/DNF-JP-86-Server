# TASK-44 — 加载完成、换房与塔阶段归并

本 Task 承接 Plan P8 T-007，迁移 FinishLoading（65573）、MoveMap（65581）、IllusionTowerStage（65695）与六条失去消费者的旧命令，共九行台账。TASK-43 已经独立批准并本地提交 `7f29f204`／`1515397c`，保留三种入场的事务、投影与精确清理边界。目标未偏离；塔加载／开始／换层是这些消息的实际分支，Boss Start 的离场适配器仍有真实消费者，继续保留，不提前宣称 T-008 完成。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-43；覆盖 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 三消息各唯一绑定完整协议 CMD，handler 只交输入与精确来源。删除 `Game.Dungeons.MarkDungeonParticipantReadyCommand`、`Game.LoadBossTowerCommand`／`AdvanceBossTowerCommand`、`Game.LoadIllusionTowerCommand`／`StartIllusionTowerCommand`／`AdvanceIllusionTowerCommand`，核完实际消费者才闭行。
- Result review: required；执行完停止写入，由主控派唯一fresh Astra快审。
- Boundary: 本次普通／两塔加载和换房编排、实际共用算法、原 registry 接缝、测试与九行台账。无新 Main／raw／Self／footprint许可，不改共享执行器或关闭契约；有证据的默认失败差异追加 VC 随 Task 审。一个 sol6.1 执行、一个 fresh Astra 快审，不设挑战或只取证 Task。

## Detailed Plan

1. **先跑原行为。** 以实际 `1515397c` 路由运行 PartyMoveMapHandlerTests、DungeonLoadingProjectionDeliveryTests、FinishLoading／TowerStage codec、两塔 public flow／线级及 DungeonVisitDomainServiceTests。补最小正式旧新 oracle：真实事务／SQLite疲劳／actual encoder／生产 sender，区分普通 Direct ready、普通换房自开事务、Boss全员与Illusion空保护、原 fine Dispose 和新 whole Dispose、第一单元已提交后第二单元失败。精确来源、同cause／chain、物理替换与实际成功帧有直接断言；受控 driver 不冒生产链。
2. **普通加载保持 Direct。** 精确session、原冻结registry前后门、run/version/Ready判定不变；城镇无run只释放NOTI001E，无CMD ACK。原ready内存动作不加虚构事务；保 release→mechanism→适用quota（Mirror／Altar／Tournament／Heras3900）→仅首次入场weakness顺序。
3. **普通换房保原事务。** 生产DI是 `TransactionalDungeonVisitDomainService`，接口default TryMoveAsync经MoveAsync进入其真实边界，并非缺事务；迁移为一次ordinary Write复用它，保原空selected及无footprint。MoveCommit与每成员首次房间疲劳原子结算；重访／免疲劳不重复扣。保MayLeave、current/admission、房间准备与版本、真实拒绝、当前冻结受众、requester缺席时首个有效成员必达、StartMap／Altar／Hell帧与其后疲劳投影；不直接绕到inner改变原拒绝reason。
4. **两塔阶段保持各自时序。** 保原stale在session/run之前的短路、Boss优先与Exact门。Boss Load与Ready后以leader执行Start仍是两次ordinary Write，原全员保护，最后加载者可为peer；Illusion Load只到Ready并通知，Stage mode1才Start，mode2报告不回复不换层。两塔Advance独立事务，Boss目标坐标与原全员、Illusion原空保护保持。guard／encoder／scope失败按真实原后果比较，不为了保raw扩大机制许可。
5. **只收真实调用链。** 新CMD不注入旧Delivery／Coordinator或嵌套bus；需要共用时由CMD拥有同算法，旧真实调用点用窄适配。普通registry可窄扩Find但必须映原单例。`BossTowerPartyCommandStages.StartBossPartyTowerAsync`仍由未迁离场路径使用 `StartBossTowerCommand`，本轮不删、不闭该行；也不搬Kill／Pickup／Die／Leave或Timer。所有关闭保原当前性与contexts精确准入，不关闭replacement，不新增关闭已移出contexts旧peer权限。
6. **冻结核验。** Release solution build，相关聚焦及App／Protocol／Networking完整；涉及实际Host注册或用例再跑Host完整。正式dotnet首次require_escalated，原类并行／项目串行／5m／detailed与独立DFO/TRX；逐display/status/重数对Task43最终库存，只容原两Dimension与原skip。核九目标之外和全部original／行序不变，strict／八坏例／实际注册／含新文件的diff-check。Result一页写交付、非常规选择、结果及限制；停写交fresh快审，主控消费后本地提交继续T-007。可分纯准备单独提交，不可分生命周期变化说明原因，不做外部操作。

## Result

- Outcome: done（执行轮R1，最终freeze r4；未提交，待唯一fresh Astra快审与主控接受）。
- Changed: FinishLoading／MoveMap／IllusionTowerStage各唯一whole，record归本slice、共用算法归Game endpoint；删除六fine和失去消费者的旧加载编排，保StartBoss departure真实消费者。普通ready Direct、Move原真实SQLite事务、Boss两unit／全员与Illusion空保护保持；registry同单例、recipient共用原算法。只闭九台账行、AGENTS增一清单。
- Why: VC-B15明列fine→whole生命周期默认失败与已登记帧差异，第二实际Start返回事务内Fighting候选后失败保首Committed／次ConfirmedRollback／外读Ready；VC-B16仅本次cleanup捕获physical并保contexts准入。机制纯快照直接投已有通知、重新查current selection，不冒旧Direct subscriber scope相同。产品scope迁移与默认故障不可分；纯准备三测试文件可分，最终旧HEAD已编译20/20。
- Checks: 旧Protocol24/24、App旧基线29/29；最终同20例旧新各20/20（15生产链＋5机制方法级）。最终r4 Release build零警告错误，App5529＝5511pass＋2原Dimension＋16skip，Protocol1781／Net203／Host203全过，共7716；App N20/D0，其余0/0。DFO71＝原67反例＋4control，outside/exe/orphan0；runtime三唯一route／三个单binding／六fine0、StartBoss1；九行之外／original／行序、strict／八坏例selftest／含新文件diff-check通过。轮次、命令、失败身份与限制见[核验摘要](../verification/task44-verification.md)。
- Remaining: 原Dimension内容缺口未修；受控physical与通知bus不冒A21实机。其它塔／T-007／T-008／Spec B均未闭合；原始日志本机保留，无index／commit／推送／外部操作。已停止产品和测试写入，记录核完后交fresh审。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

仅批准 TASK-44 合同 r1／执行 R1 最终 r4 的本地结果；未发现未解决的阻断缺陷。对象为 `task/refactor-session-20261021`、`1515397cb49279ebfe7a02bf600ab6d3ae32b7a2` 上本次未提交改动。按 `reviewing-work` 的 Task Unit 与 Code 判据，读取流程重置、实际 AGENTS／架构约定、B6 与 VC-B15／B16、P8 T-007／T-008 边界，独立检查实际新旧源码、测试断言和原始记录；没有另派挑战或 reviewer。

- 三个消息均转薄 handler→本切片 whole record，共用 endpoint 算法；六 fine 已删除，真实 departure 消费的 StartBoss 保留。普通 ready 的冻结 registry／session 前后门、Direct 无事务和 release→mechanism→quota→首次 weakness 保持；Move 仍调用原 TransactionalDungeonVisitDomainService，以一 ordinary 空 selected 单元复用原事务，受众及房间→疲劳顺序保持。
- Boss Load／Start 是两次独立写、原全员保护；Illusion 空 selected、Load 只 Ready、Stage1 才 Start、Stage2 不推进。实际 Start 后抛错的 oracle 确认 Fighting 候选曾产生，再核首 Committed／次 ConfirmedRollback／外读 Ready。VC-B15 对 scope 时点、已有帧及默认 typed 保护的差异有直接断言；没有把整体数据库称为不确定。VC-B16 的 query-only 与 contexts 双替换分别检查四个 physical，保当前 contexts 准入。逐成员 encoder 收集后正常返回的旧语义保持。
- 机制恢复原 subscriber 只有当前 selection 查询与相同通知投影，Failed 同 IOException；新入口复用该通知。五例明确只是方法级受控通知总线，不冒生产编码或同 scope 生命周期，Timer 原 Direct 路径保持。
- 独立解析 `task44-old-final/Application/results.trx` 为 20/20；`task44-final-r4` 四 TRX 为 Application 5511 通过／2 失败／16 skip、Protocol 1781、Networking 203、Host 203 全过。与 TASK-43 指定最终 TRX 逐 display／status／重数比较仅 App N20/D0，其他 N0/D0；两失败仍为 Dimension 的 7100／maze0／(1,0)／Left 内容缺口。核查 run.json 的工作区、HEAD、分支、命令、起止与退出值及 DFO 分类 62/1/7/1，未出现范围外／exe／orphan 记录。
- 独立比较台账对象确认仅约定九行变化、全部 original 与顺序不变；实际注册 diff 与 runtime capture 相符。三份纯准备测试已有旧产品绿证据，可由主控分段提交；产品归并与 whole 默认失败边界不可分的理由成立。

边界：本评审未重跑全量、未主张 A21 实机验收，也不闭合 T-007、T-008 或 Spec B。没有修改产品、测试、台账或 git index；只追加本节，写入至此停止，交主控消费。

## Controller Consumption R1

主控接受上述 approved，仅关闭 TASK-44。已独立核读实际变更与评审，解析最终四份原始 TRX／run.json，并按 TASK-43 指定库存逐 display／status／重数比较：7716＝7698通过＋2原 Dimension失败＋16原skip，只有App新增20例；独立重跑四项目DFO parser为71条原反例／control，范围外、exe、orphan均0。再次核 runtime 三唯一消息／三个单binding whole／六fine0／StartBoss1，以及九目标之外、original和行序不变；strict及八坏例全部通过。

纯准备补丁对当前HEAD的cached apply检查通过，三测试文件先独立本地提交，其余归并及VC-B15／B16后提交；产品scope与默认失败不可分理由接受。无新Main／footprint／raw／Self或关闭机制许可，无外部操作。继续P8 T-007的击杀与物件破坏路径；T-007／T-008／Spec B和最终封存门仍未完成。
