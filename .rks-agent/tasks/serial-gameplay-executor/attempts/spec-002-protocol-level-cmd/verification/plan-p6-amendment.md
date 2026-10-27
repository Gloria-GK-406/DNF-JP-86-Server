# Plan P6 ready R2 amendment：Mailbox self结算来源、whole与共享双因

## 来源与精确目标

本writer按using-rks-agent受限子任务及writing-plans/task-units高层参考受命只写HOW。唯一workdir为`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支`task/refactor-session-20261021`，HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`。主控已完整消费fresh approved B5，无新增HITL；本P6仍须独立Plan gate，ready不是approval。

| 只读依据/目标 | bytes | SHA256 |
|---|---:|---|
| child TaskContract r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| Spec B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| Spec B5 amendment | 11251 | `70d86067ba8ebdba9e5df0fe1e0efb118d5c57ba4d03919ec6d2920863eb20ec` |
| immutable B5 challenge，0/0 | 6854 | `5d25a35f5bfa08e074ed7b20d268c3804246273ffb80e0c26df4a66bf99fa701` |
| fresh B5 review，approved | 9675 | `e85be4d072f30e8c3b24c1965a5e6d90902d10559c6d812e9505fb1dc1a80e66` |
| immutable P6 R1 challenge，high1/low0 | 9554 | `d0d165bede921fb70a156ed945aa31f6e3f2268612841ea0e318fb53fde3f04e` |
| R1 Plan逐bytes快照[evidence/plan-p6-amendment/plan-p6-ready-r1-before.md](evidence/plan-p6-amendment/plan-p6-ready-r1-before.md) | 89736 | `eaba6ae255cbcb8120148644744e06b6097b2ea717e143f24c01b7a96b48e043` |
| R1 amendment逐bytes快照[evidence/plan-p6-amendment/plan-p6-amendment-r1-before.md](evidence/plan-p6-amendment/plan-p6-amendment-r1-before.md) | 6993 | `f6920be220905ba1951a41b9a78a62f3bb04f9bab426c581206b1fca0eb96a47` |
| 原P5，现逐bytes快照[evidence/plan-p6-amendment/plan-p5-before.md](evidence/plan-p6-amendment/plan-p5-before.md) | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| 已提交TASK-30 | 28640 | `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c` |
| 本P6 ready R2 plan.md | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |

B baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。读取B5/amend/review、P5、Task30计划/Result/review/Controller Consumption末节及report，核实际Mailbox RunAsync、DomainTransaction/Scope、CommandWorkUnits/Request/Bus/registry、six decoder/Delivery/fine、outbox终局及shared callers/原fine测试调用。worktree无.codegraph，按AGENTS跳过CodeGraph；架构哲学/重构约束沿真实层边界，不参照旧单体结构。

## 冲突和选定HOW

P5恢复span仅CommitAsync；Mailbox action内实际自Rollback已使Currentnull/事务settled。把它改成RequireRollback或据IsSettled直接恢复都会放宽共享保护。P6选择独立WriteSelfSettledResult入口和封闭Returned/ActionRolledBack/CommitRolledBack，注册能力只Mailbox六logical/五CLR族；原Write/ReadOnly/WriteResult五consumer与三个raw不动。

来源不是business自报：internal真实Tx AbortActionAsync仅对本次显式观察，在入口、原Tx置settled/任何rollback await/回调前同步pin原unit/owner admission，owner.Close不得清pin，向下层暴露完成前无Register/BeginUnit窗口；所有参与者真实回滚/Completion完成后才签不可伪造fact，含自身Tx/调用identity/occurrence/阶段/原cause/输出/完成。显式unit的直接领域await观察器仅消费既有pin/fact并单次交回拥有的runner，不负责首次加barrier；fact必须在runner自己的失败Abort前存在，后来Abort普通throw不能补来源。Tx只留自身操作事实，无request/job/session；runner显式绑定本次request/job/lease。公开机制仅当前显式单元，不做业务mapper、不新ambient/容器/Host/outbox。机制交接signal和pending只阻止catch后继续，不授业务成功权；普通outbox没有request参数，因此登记/第二事务的阻挡明确由已有真实outbox unit的窄admission barrier实现，只存unit/pending/自身guard occurrence，不持request或callback；runner显式读取guard合入sticky，不新增jobslot/第二outbox，不改变原unit状态/Complete/flush。runner完成全部unit/lease cleanup与显式健康检查、无barrier guard后才解除健康候选并给单元结果，独立guard occurrence不能被后续fact/原cause传播清除，scope/drain/Close后才可给bus成功。

同cause仅是必要引用条件。fact后真实unit/request/current/registration guard的独立occurrence及sticky/GuardFailure，和独立lease/scope再次抛同对象，均撤销候选并保两次发生；普通传播不误算第二次。没有fact、错误阶段/lease/request/job、取消/typed/未知完成/已有sticky/guard/未完成清理均不恢复。普通非self动作拒绝只在原Open事务窄catch RequireRollback；正常返回拒绝/Querynull保原提交事实，不统一回滚。

共享RunAsync只把原catch cause传入真实Abort，利用原typedRollbackFailure聚合保原SQL和Rollback第二cause，不改即时自结算/IsSettled分支/caught Commit必throw。Mailbox六路唯一whole、五handler薄入队；每原fine路由一次ordinary写、空participants/None，保Send/Claim资产Prepare/gold在事务内及原单元后投影顺序。whole恢复映ACK前重核精确session/IsInWorld/OnlineUserId/物理身份，机制不硬编码Mailbox也不套Cera actor。

VC-B5 C1/C2/C3等不可恢复走原默认失败/精确source-close/chain，无新增selected结算Fence/DiscardExact；正常disconnect/teardown照旧。scope前可能已有success/normalrefusal帧，按实际unit状态存活后置后关闭，不由旧slots[]推零帧、不撤消息/earlyflush/补StorageFailureACK。B5权威勘误single3帧/multi4帧已入验证，历史report/review/Task不覆盖。

## 受影响位置、责任和限制

修改Planning Choice、§4共享contract/Mailbox阶段、§5现有ICommandWorkUnits能力说明、T-004/T-005状态与依赖、P6验证责任、最终VC列表、NextBoundary及P6修订记录。P5 Cera要求标为已由Task29交付的历史起点；P3/P4历史原要求保持。T-004仍有具名8消息7fine残余；T-005拍卖只有outboundB7无入站，不凭包名造Task，租赁2Main/独立Tx refresh仍pending；T-006～012不减。仅Mailbox是下一UsefulUnit，机制/六consumer/删除/有价值验证同交，不先空壳。

Task31门后承担真实decoder/同provider/Domain/SQLite/outbox/production sender对照Task30十四场/59focused、来源/cleanup有行为价值mutation、nativeCommit与self区分、C1durable/C2双因/C3whole scope、actualsourceclose/chain/替换物理隔离及原强driver迁移。共享ItemDelivery/GM整批prepared-audit/QuestHelperPuppet/Housing/Wedding原子性、即时结算和双因按所触真实用例直接核并披露未测，不静态冒端到端。same-run全部110表/列NULL/BLOB原样回滚，跨run仅已证具名SQL时间/HousingUTC/GUID图窄关系；完整NUL nonowned与canonical/manifest/行尾不变。

预计改共享core，临近实施要求fresh Release solution --no-incremental0/0＋八项目full；原类并行/5m/Skip/detailed/DFO不改。当前9029=9009pass+原exactDimension2fail+18skip只历史引用，DFO九expected67＋七actualcontrols7=74、Golden15/Char10、注册/runtime/purefamily/3raw全owners不放宽。2秒性能及T-012最终fresh核验仍后继。细source白名单、逐命令、driver/非null结果留Task31，不在高层Plan预写。

R1作者阶段只写Plan/amendment/原P5snapshot，完成时15725个nonowned tracked SHA未变，无native/产品结论；其精确原稿见两份新增R1逐bytes快照。本次R2只允许current Plan、本amendment和两份R1snapshot，不改原P5snapshot、immutable P6 CH-1、B5或其它文件，无build/probe/testhost/native/产品/测试/ledger/A/Contract/routing/Task31/git mutation。writer落稿停止，交另一fresh独立Plan reviewer裁决R1 immutable挑战与本R2；不重生CH、不自审或自批，主控消费及amendment-only检查点后才细化Task31，不解除Astra/用户收尾限制。


## R2：immutable CH-1后的最小纠正与门

主控完整消费R1 immutable挑战（9554B，SHA如上），接受high-risk CH-1；真实unit.End→owner.Close(open=null)、Scope.Currentnull以及下层ObservedProxy在inner错误后、outerobserver接错前的catch窗口成立。此处不是新WHAT、产品bug运行报告或独立Plan approval。按挑战后review前一次最小高风险纠正，保持原高层路线/接口/consumer，只修§4 barrier首次pin、owner.Close保pin、fact真实完成后签发、observer只消费及runner最终解除的时序。

Verification补两场真正下层同provider proxy窗口反例：inner已真实回滚、outer尚未接错，分别普通Register与Begin→SQLitewrite→Commit，在最早admission拒绝，后者SQL/Commit不得到达；proxy捕获guard后重抛旧cause，最终仍保独立guard、不得ActionRolledBack/普通ACK恢复。要求可编译行为mutation将pin延后outerobserver或Close清pin而真实越过admission使断言RED，恢复入口pin/保pinGREEN，不只测whole catch signal之后。无观察的standalone/ambient、原五consumer、Tx只存自身操作fact、barrier只存unit/pending/自身guard、显式runner绑定、原A Complete/flush均不改。

R2没有新测试运行或pass，没有new challenge/review；ready只代表作者可交审。R1 identities保留，不覆盖immutable挑战；下一步由另一fresh reviewer裁决CH-1与R2，之后仍需主控消费及amendment-only checkpoint。
