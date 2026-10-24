# Spec B4／Plan P5主控消费

## 独立门与当前身份

唯一worktree `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`；本amendment检查点父HEAD为Task28 `f42efb55bedad71dc1196100e39255bc483ed72c`。主控已完整读取B4初稿diff/amendment、immutable CH-1、R2相对R1的全部纠正与current Spec review，以及P5受影响diff／验证段、amendment、fresh挑战和独立Plan review；所有producer／generator／reviewer均分离且停止writer/native。两独立review对当前exacttarget均approved、无remediation或未解决blocking。

| 制品 | bytes / SHA-256 |
|---|---|
| `spec.md` | 42632 / `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| `plan.md` | 71863 / `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| `verification/spec-b4-amendment.md` | 13392 / `a73b9317a2be57767a73cf4937b4553fc80abb81f526a62b2f6ded1c2ca07d8b` |
| `verification/plan-p5-amendment.md` | 12843 / `5d2a9c1aa0fff1877b9b75b6f904293f75f5bdf40b8e6dfe404c2af98c4553ba` |
| `reviews/spec-b4-challenges.md` | 6366 / `f8859fbc613f6f199f62fb6398de3b9c486accb09ba00b8a739b1c93b0a7ea86` |
| `reviews/spec-b4-review.md` | 7624 / `19d7a2d0a5075f89858ab1c32b70d575fbabe350a4e62f45d8f7528eeb659848` |
| `reviews/plan-p5-challenges.md` | 6021 / `2b9a0dc65666e57f1d327a65c51cf58ac51f4c8849de437b36cb2548ce250b1e` |
| `reviews/plan-p5-review.md` | 8426 / `c355eabec0d2607b6a39ef0ee84c829f281bcd6be8503decc3ee02d5e31a7244` |
| `verification/evidence/spec-b4-amendment/spec-b3-before.md` | 32948 / `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05` |
| `verification/evidence/spec-b4-amendment/spec-b4-ready-r1-before.md` | 40180 / `ca7f64fd4dfcb23a32ffd731d196dea9e9a83ef092e00991fde89393ea2a06c4` |
| `verification/evidence/spec-b4-amendment/spec-b4-amendment-r1-before.md` | 10185 / `2b668e65a0b8ca7f38c190f9b4e7967c3d13f2610c452b20ba1ff89fa0fdfd37` |
| `verification/evidence/plan-p5-amendment/plan-p4-before.md` | 61551 / `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7` |
| 更新后 routing-decision.md | 5507 / `eafec301c273372456d4ff522804e73dda61095171db565591c59bc249d2d03d` |

主控逐项复算上述identity；B3和P4快照等父HEAD原文，R1 Spec/amendment与immutable CH-1保留，不倒写旧事实。当前四个gate override已切B4/P5，旧消费段作为历史保留。子Contract r1、根Contract r4、Spec baseline `10a7218a779493494b138bd6b43cc0fc1a84aa6a`、A seal、controller/profile/task-units/on-drift及auto门强度不改。根／子Goal允许经审查Spec逐项有意变化，根约束和SC-8B要求明列，故本有界裁定沿现有授权，无新增HITL。

## 消费结果与原保护

Task28是独立approved的实际旧decoder调查：A/B普通action SQL19/1811与真实SqliteTransaction.Commit19/787确认完整回滚且原拒绝ACK；C1非fallible违约、C2 SQL加Rollback二因和C3 fine scope提交后清理故障均旧拒绝ACK／无围栏关闭，真实资产可能已提交。它们是实际／受控before，不是生产事故、TCP／A21或迁移后通过。

B4初稿漏明足迹所需actor的自然新增保护，fresh CH-1的high-risk成立；主控实读Cera DemandEffects、NonRequest单actor、FailAsync/BlockAsync后明确选择现有精确买家足迹与默认保护，不为保旧空selected集合削弱它。R2将该新增Fence／精确物理连接阻断／DiscardExact／关闭列入VC-B4；fresh reviewer独立确认CH-1在R1成立、R2充分解决。此为同风险基础下review前一次最小纠正，原challenge不重生；Task28先前表述原样保留，由本轮显式裁定澄清后继范围。

P5只增加Cera一个具名消费者：原一次Transactional对应Ordinary write+NonRequest单精确actor和显式physical/metadata，复用当前WriteResult/KnownStorage commit-only结果；窄clock/domain action映射以RequireRollback(cause)保因、unit完全结束后回原拒绝，正常返回拒绝保原空提交。typed/guard/unknown/sticky/独立cleanup不能恢复；精确买家Committed/Uncertain进入默认失败保护时才Fence/Block/DiscardExact/关闭，不误伤替代身份和其他物理连接，健康成功／拒绝／确认回滚无新增保护。C3映新whole真实DI scope，已登记normal条目按原unit事实／顺序处理后关闭，不统一零帧、不撤条目或提前flush／补StorageFailureACK。三个raw出口、defaultWrite/ReadOnly、KnownStorage/guard/cleanup及Compose禁用保持；没有新core/API、ambient请求或Mailbox恢复授权。

后继按P5直接对照真正decoder same-provider/domain/SQLite/outbox/productionSender＋受控transport，保完整NULL/BLOB资产／事件、native阶段、同cause及第二cause、精确actor保护／不误伤／实际帧与close/chain；旧direct-fine强意图和Host真HTTP grant/shop竞争保持，wire不可表达的领域事实按真实domain＋原事务test seam分层，不能造字段或删强断言。按实际影响检查，触core/API须返主控并扩大八freshfull；最终T012八full/DFO74+7/Golden15/Characterization10/单CMD≤2秒线不减。

## 完整性与下一步

本轮仅14个规格／计划／审查／快照／routing／本消费文档路径；无产品、Server/tests、ledger或历史Task改动，无native／新full／capture通过。原9014＝8994pass＋原exact2Dimensionfail＋18skip只是Task27已接受库存；Task28无新增xUnit。主控先核精确NUL集合、hash、metadata whitespace、parent/branch/index，再本地amendment-only检查点；提交后核diff-tree精确14路径、clean和Co-Authored-By footer，才指定唯一TASK-29进行详细规划/readiness/生产。此文件不是Task29/实现或Spec002完成声明。

中文、不push/PR/merge/远端/版本/tag/部署/游戏/cleanup；Astra通过且用户明确分支收尾的外部门继续保留。
