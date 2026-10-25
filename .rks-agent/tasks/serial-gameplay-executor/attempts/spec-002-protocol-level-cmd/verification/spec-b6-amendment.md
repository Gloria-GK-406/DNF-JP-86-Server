# Spec B6 amendment：剩余 opening 与 Rental 的主结算及失败边界

## 权威与前版

这是主控授权的有界 WHAT 修订，使用 writing-specs；唯一工作区 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`，起点 `17d924a06b1237ca2b4f5997983c0a4923dfc050`。该提交已封存 TASK-32 的旧行为调查。作者按 [主控后继裁定](spec-b6-controller-decision.md) 落稿，未自行选择恢复 API／实现路线，不以 TASK-32 的独立 approved 冒本次 Spec gate。

根 TaskContract r4、child r1 的 Goal 允许“经审查的 Spec 逐条列出的有意变化”，根的“可见变化须列明”和“纯搬迁与语义改动分开提交”保持。用户把执行中裁定交主控并明确继续 Spec2；不是用户追加协议要求，也不解除独立 gate／Astra／收尾限制。

| 写前实读身份 | bytes | SHA256 |
|---|---:|---|
| 根 `../../../TaskContract.md` r4 | 32812 | `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4` |
| child `../TaskContract.md` r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| B5 原 Spec，[逐 bytes 快照](evidence/spec-b6-amendment/spec-b5-before.md) | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| 未修改 Plan P6 ready R2 `../plan.md` | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| committed done [TASK-32](../tasks/TASK-32.md)，包含主控消费 | 47491 | `f236bcf4bc8d805768e835b4ee8a501496a63f2eb8d3349b81640b6c99a9190e` |
| TASK-32 [报告](evidence/task-32-inspection/report.md) | 14407 | `150de8be4ff2a58896ee3697e32d7f9f10fa5f4771d61985f55b8c3613fff4b0` |
| 主控 [B6 WHAT 裁定](spec-b6-controller-decision.md) | 6900 | `7fab8c2ace4ffb0f58038690c52f8e1c6e15c14cb3974084e07ed91a7e4947da` |

B baseline 仍为 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`，A seal 和 Task01～32 保留。前版直接复制，不重排历史 B1～B5记录；当前只改 Spec 的相关要求／验收与新增专节、VC-B6／修订记录。没有另复制大量源码或 raw。

## 已证冲突与最小裁定

旧证据是 TASK-32 build-09 与 matrix-04 同 freeze `ef32d638a547f104919459f2a72456558dfe2f1a65cbdfe4c74efb74e248d74d` 的52场、原 [case-manifest](evidence/task-32-inspection/case-manifest.json)／[raw](evidence/task-32-inspection/matrix-04/) 和 checks-final-02；注册 decoder→同 provider dispatcher／legacy fine→原 domain／SQLite→生产 outbox／OutboundSender 加受控 physical transport。这里只消费已提交事实，作者未启动 native。

| 旧边界／来源 | B6 的关系 |
|---|---|
| 六条效果 Main 各 Transactional；Lottery mode1 premium 另 ReadOnly，Rental refresh 另 Transactional | 保提交点与顺序，不合并 Main／刷新，不把后者凭名字改只读。真实注册 `ProtocolModule.cs:261/:292` 与各 slice 对应；后继 HOW／对照表归 Plan |
| 四 opening action SQL19/1811 和实测 Compound／Lottery1 native Commit19/787 健康确认回滚都回原拒绝；Rental 两路相同阶段异常不恢复，无 ACK／实际来源关闭 | 保各自不同出口。原四 opening catch 见 CompoundItemSlice.cs:92～102、LotteryOpeningSlice.cs:94～95、VendingMachineSlice.cs:97～102、EpicCraftSlice.cs:69～79；RentalSlice 没有对应普通 catch。native 不以 action SQL／controlled Prepare 代替 |
| Rent 真满32设备格，stars 暂存100→97，InventoryFull 正常返回并 RequireRollback；110表644列＋实际 rowid 回滚，但 Frozen=false／Projection=null 后 Settled 抛错，Blocked／Fence／物理 Block，零帧／实际关闭1 | VC-B6 有意改为健康 NoRoom 正常 Result2、ConfirmedRollback／原 entry 和 Ordinal 合法重试、无新增保护／关闭／成功后刷新。既有 RentalSlice.cs:53～61 编码已有 `01 02 00 00 00`，不是 Error:2，不改字段布局。领域 `EquipmentRentalDomainService.cs:75～77` 正常拒绝与结算层 `EffectOperationContext.cs:102～107` 的旧冲突有实际证据；本节不扩修全部旧 rollback-only Main |
| 四 opening 真实 fine scope 提交后清理错误被旧 catch 映失败 ACK、ledger仍Committed／来源健康；Rental fine scope 已提交后失败为无 ACK／关闭、无新增 Fence／Block | VC-B6 明列唯一 whole scope 的完整默认失败和故障点后移：body／投影后可能已有原 success／normal refusal 帧，按真实 unit 存活状态／顺序保留并置后关闭，不补失败 ACK，不承诺零帧。已提交参与者符合原默认条件时须受精确保护，不绕过它保旧遗漏 |
| Lottery1 premium、Rental Panel/read 在 Main 提交后故障，第二单元确认回滚；Rental refresh scope 又有第二单元已提交后清理失败，旧均无 ACK／来源关闭但无新增 Fence／Block | VC-B6 另明列 Main 已提交的默认 typed／参与者保护增强，第二单元事实和原 Main durable 保留，不能把两段混叫一个回滚。不新增普通异常恢复 |
| C1 已 durable 仍 Uncertain；C2 实际 SQL 与 Rollback 第二 cause 都可达 typed 图，实际 SQLite 回滚仍 Uncertain | 原结果／精确保护保持。仅 ConfirmedRollback 后另有普通错误不伪称 Uncertain／强加围栏；独立同对象错误保独立发生，正常传播不冒第二次 |

NoRoom 的具体响应、账本和原异常出口、whole scope／独立刷新终局只在 Spec 专节表定义；本记录解释修订依据，不是另一套实现合同。 已成功 Settled 的 ledger Committed 在后续 scope／refresh 默认失败时仍保留（`ExplicitEffectRequest.cs:43～46`），`CommandWorkUnits.cs:375～378` 依 HasCommittedUnit 加 typed／精确参与者保护；不能由外层 typed 名称倒推 ledger 都 Blocked，或把纯 ConfirmedRollback 后普通清理错误强改 Uncertain。当前显式 runner 在 `CommandWorkUnits.cs:280～319` 已将正常 rollback-only 对应 main.Failed，只在 Committed 时 Settled，说明不能为了迁移把健康 NoRoom 伪装异常恢复或扩大共享机制。具体最小前置修正／结构准备和提交分离留 Plan 从真实依赖决定；不能把混合提交改名“语义提交”规避根约束。

## 原保护与有限 owner

既有三个 raw 出口类型、其他 owner、默认 sticky／来源事实／guard、Compose 禁用和 F-132 条件保持。六新 whole 仅在逐具名先旧 decoder／原实际 encoder 固定 cause oracle、后同容器新路径与默认／独立清理反例证明后，取得现有 CommittedReplayCurrentStateBatch family 的有限 owner；不满足便不授 owner、返受影响 gate。TASK-32 没有这项 encoder 注错，不能称它已证明原 cause 同对象／原 ledger entry／Ordinal／无围栏／实际关闭等 raw 全条件。首次成功、普通拒绝、RequiredExplicit 或 CollectionBox initial batch 不借 replay 获权；不新增第四类 raw。

现十个实际 WriteResult 消费方为 Warehouse、ResetRandomOption、ResetItemQuality、PackageOpening、MagicBoxSingleOpening、MagicBoxBatchOpening、ItemSockets、CeraShopPurchase、AvatarPackageOpening、AvatarCompound；Task31 审核已把早期“五consumer”解释为原四 opening＋Cera子集，完整十个 current／HEAD 原样保持。B6 只纠正当前 AC-005 完整计数，历史记录不追改。四 remaining opening 以后可具名消费，Rental 不随之获普通异常恢复；Mailbox SelfSettledAction 六 logical／五 CLR 的唯一 whole 权限不扩，不把本次与自结算混并。Spec 不选择新 API 或自建证明框架。

## 验收与不代表的结论

Spec 的 RQ-006／007、专节、VC-B6 及 AC-003～005／011交叉闭合，17 RQ／11 AC 的 IDs不变，VC-B1～B5和其余要求保持。迁后须直接对照：六路健康成功／拒绝、真实 NoRoom 与不同异常出口、原提交点／完整资产含NULL／BLOB／rowid、scope／独立刷新的真实 Completion／cause／条目、实际发送→关闭与原链；精确 actor Fence／物理 Block／DiscardExact不误伤真实替换 session／不同物理连接和无关玩家。只有真实 Committed／Uncertain 默认条件才围栏，健康 NoRoom、成功、普通拒绝与确认回滚分别作对照。scope 证据必须实际 resolve 并 Dispose 资源，不以 body 手抛冒清理；具体选例及方法留 P7／临近 Task。

TASK-32 的52场与142 focused是历史调查：Vending／Epic未逐路 native，opening C1／C2只Compound代表，dispatcher只Compound／Rent／Charge代表，其他是源码共享推论。无新DFO74/7／八full／Golden15／表征10／2秒／真实 disconnect child／TCP或A21结论；受控scope／participant／read fault不是自然事故，观察查询提前enlist同SQLite participant的影响已有披露。后继不得把这些限制藏掉或冒所有 caller 故障全覆盖；本轮 metadata-only 无产品／正式测试结果和 canonical 闭行。

## 下一 gate

本 B6 `state: ready` 只表示可以审，未 approved。依次 fresh 有界 Spec challenge、另一 fresh 匹配本稿的独立 Spec review；通过后才授权受影响 Plan P7，另经 fresh challenge／独立 Plan review、主控完整消费和 amendment-only 本地检查点，再详细规划新 Task。既有 B5／P6／Task32 批准不替代本轮门。当前 Plan／routing、Task、canonical及A保持原 bytes；未选新 API／具体 Task 拆分，不启动 native／挑战／review／产品或 Git mutation。writer 完成限定制品后停止；原中文／本地工作区／不pushPR／版本tag／部署／游戏／清理及Astra／用户收尾限制保持。
