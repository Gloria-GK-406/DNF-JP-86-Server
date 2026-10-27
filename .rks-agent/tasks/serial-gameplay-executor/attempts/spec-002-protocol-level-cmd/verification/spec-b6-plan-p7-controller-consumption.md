# Spec B6 / Plan P7 受影响 gate 主控消费（2026-10-25）

## Spec B6 ready R1

主控已完整消费B6新增WHAT及受影响RQ/AC/VC、amendment、fresh immutable challenge与另一fresh independent review。当前HEAD为TASK-32检查点 `17d924a06b1237ca2b4f5997983c0a4923dfc050`；Task31/32及其原审核、raw不改。以下身份已逐bytes重核：

| 目标 | bytes | SHA256 |
|---|---:|---|
| `spec.md` | 66834 | `d20f90fb095438d5408b9f7bb54e69af3224c41e1f749d14a475a8104b9956fd` |
| `verification/spec-b6-amendment.md` | 9696 | `3fd97177c73e11bc687931ab19d3c90a68c2e49de1493744a0d3f0ffdcec9ebb` |
| `reviews/spec-b6-challenges.md` | 5660 | `f1458e00c7f0e6df37c7b8f0e0bbd996d8c6f66d40f563d7b04e6d1e57eea6d3` |
| `reviews/spec-b6-review.md` | 7889 | `4d4c7a69676a1860962b495a0b5a03adbf352dcf172764a68f10500606d60cbb` |

挑战0 high/0 low；独立review verdict `approved`，无未解决material finding或remediation。主控不以零挑战自批：已核原NoRoom领域正常return/RequireRollback→UOW确认回滚→legacy Settled要求Frozen的冲突、显式runner正常rollback路径、FailAsync的真实CommittedParticipants条件、旧fine与whole scope位置及replay接线，与review和Task32原事实一致。准许进入受影响Plan P7 HOW修订，尚不创建/实施Task，不把此Spec门当Plan/产品批准。

本轮仅六消息具名VC-B6：NoRoom健康正常拒绝/ConfirmedRollback修复；whole scope和Main后独立premium/refresh的默认保护变化明列；四opening原健康确认回滚拒绝和Rental异常不恢复分别保留。Completion/ledger/error不同，已Committed ledger不改Blocked、不重结算，纯ConfirmedRollback后的第二普通error不伪Uncertain或一概围栏。存活条目按真实unit状态/原顺序后实际关闭；不承诺零帧。六新whole仅待逐具名旧encoder oracle和新same-provider反证后取得现有replay family有限owner，不授权首次successraw、新raw类别或Mailbox self能力。旧实际十WriteResult、其他raw/defaultsticky、Compose禁用/F-132保持。

## P7 生产授权与最小先后裁定

P7仍task-units/on-drift并覆盖原17RQ/11AC、T-001～012全部未完成责任。主控选择下一有用前置是独立NoRoom正常确认回滚记账修复（不与whole/注册迁移混合）；之后再按真实共享依赖闭合四remainingopening和Rental whole。产品改动前仍需本轮Plan fresh challenge/review、本文件Plan消费和amendment-only检查点。详细Task稳定ID此时不创建，门后从TASK-33递增分配。

该前置有真实用户价值，不是新恢复框架：原Rental mapper/encoder已支持Result2；现explicit runner无需改。只读全RequireRollback callsite分类表明直接正常return的旧源点有Rental、BossTower、DungeonAdmission，后二fine不是IRequestSettlementCommand，For/ForSettlement不处理；当前oldMain受影响正常返回只有Rent NoRoom。Plan须把真实rollback/normalreturn账本与非法缺Freeze/未开始/不确定/第二错误区分，并由原decoder及现强断言证明，不在core硬编码Rental类型、不发新authority。该源码分类不冒各caller动态全故障通过。

根纯搬迁/语义分提交不豁免。独立NoRoom修复和可独立纯结构准备应分开；协议whole重写所必需的完整生命周期差异是已明列VC-B6，但不能据此夹带能独立保持原行为的纯移动、无关重构，或仅改提交标签规避。Plan落实最小可执行边界，不造第二scope、临时授权、撤帧/伪Uncertain求兼容。现B6裁定6900B原文保留；本节将其分离约束具体化，不改WHOTo其他协议。

## 当前非结论与后继

当前Plan P6 R2尚未修订/批准P7，routing仍指B5/P6旧gate；该历史批准不代本门。下一production只写PlanP7/amendment/旧P6快照，停止后fresh immutable挑战和另一freshreview。两门均approved且主控完整消费，才更新四artifact overrides并amendment-only本地检查点，再Task33。无native/新full/性能/实机结论、无canonical闭行或产品/正式测试变化。A/root/child/baseline/controller/profile/gate强度、不pushPR/远端/版本tag/部署/游戏/cleanup及Astra/用户收尾限制保持。


## Plan P7 ready R1及联合gate消费

主控完整消费当前P7全部新增/受影响段、amendment、immutable fresh挑战和另一fresh independent review，逐bytes重核：

| 目标 | bytes | SHA256 |
|---|---:|---|
| `plan.md` | 108854 | `651119f9995057344f3dc8d064c901eb90d081a00f9999d662343ce380fbe8d4` |
| `verification/plan-p7-amendment.md` | 9588 | `7b5d2b819bbe444a753faa0ce49c447b3a05801ad7171bfd07c1b2652d8a9c75` |
| `reviews/plan-p7-challenges.md` | 7179 | `5d9351b429a8c9c8960c8d4442fdb5ba7156746776fa1fe5f70882f7ab837127` |
| `reviews/plan-p7-review.md` | 10227 | `3c85f4523fc5eb6603c3e3b4d364d1a96914026e61b57cc2a761b5e44bd9f118` |

P7 challenge high0/low0；独立review `approved`，无material finding/未决remediation。主控已按NoRoom实际旧链/显式runner、恢复与提交点、真实scope/FailAsync/replay源和根分提交要求核其可执行边界；这不是主控替代独立判断。P7与已approved B6一致，旧P6 snapshot等于HEAD原稿，P1～P6历史原文保留，T-001～012及17RQ/11AC、T-004剩余四消息、T-006～012/来源/过渡及最终八full/测量责任不丢失。

准许固定B6/P7这组受影响门，routing只替换四个artifact override及本轮说明，不变controller/profile/auto门/baseline/契约/parent。只有amendment-only本地检查点完成且parent/footer/exact path/clean核实后，才详细规划TASK-33（独立NoRoom正常确认回滚记账语义修复）；不在此创建Task或提前实施。该单元不改注册/handler/mapper/encoder/领域/explicit runner、canonical迁移行和十consumer/六self/其他raw owner。随后四opening和Rental各自whole与切换前old encoder oracle仍全部待交，后继每Task按required结果门推进。

当前两门批准是WHAT/HOW可用，不代表产品/Spec完成。当前仅本轮文档制品，未运行native或新full/DFO/Golden/Char/2秒/实机，不把Task31/32旧结果计新。全部writer已停止；精确owned路径/四类whitespace、Git parent和提交后实际diff-tree由本轮检查点外报，检查点不包含产品/测试/Task/canonical或上游A/root/child。所有既有远端/收尾限制保持。上文“当前P6尚未修订”是此前Spec门时点原记录，由本节当前P7消费更新，不追改历史。
