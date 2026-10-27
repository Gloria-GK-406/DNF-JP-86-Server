# Plan P6 amendment：Mailbox self结算来源、whole与共享双因

## 来源与精确目标

本writer按using-rks-agent受限子任务及writing-plans/task-units高层参考受命只写HOW。唯一workdir为`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支`task/refactor-session-20261021`，HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`。主控已完整消费fresh approved B5，无新增HITL；本P6仍须独立Plan gate，ready不是approval。

| 只读依据/目标 | bytes | SHA256 |
|---|---:|---|
| child TaskContract r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| Spec B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| Spec B5 amendment | 11251 | `70d86067ba8ebdba9e5df0fe1e0efb118d5c57ba4d03919ec6d2920863eb20ec` |
| immutable B5 challenge，0/0 | 6854 | `5d25a35f5bfa08e074ed7b20d268c3804246273ffb80e0c26df4a66bf99fa701` |
| fresh B5 review，approved | 9675 | `e85be4d072f30e8c3b24c1965a5e6d90902d10559c6d812e9505fb1dc1a80e66` |
| 原P5，现逐bytes快照[evidence/plan-p6-amendment/plan-p5-before.md](evidence/plan-p6-amendment/plan-p5-before.md) | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| 已提交TASK-30 | 28640 | `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c` |
| 本P6 plan.md | 89736 | `eaba6ae255cbcb8120148644744e06b6097b2ea717e143f24c01b7a96b48e043` |

B baseline仍`10a7218a779493494b138bd6b43cc0fc1a84aa6a`。读取B5/amend/review、P5、Task30计划/Result/review/Controller Consumption末节及report，核实际Mailbox RunAsync、DomainTransaction/Scope、CommandWorkUnits/Request/Bus/registry、six decoder/Delivery/fine、outbox终局及shared callers/原fine测试调用。worktree无.codegraph，按AGENTS跳过CodeGraph；架构哲学/重构约束沿真实层边界，不参照旧单体结构。

## 冲突和选定HOW

P5恢复span仅CommitAsync；Mailbox action内实际自Rollback已使Currentnull/事务settled。把它改成RequireRollback或据IsSettled直接恢复都会放宽共享保护。P6选择独立WriteSelfSettledResult入口和封闭Returned/ActionRolledBack/CommitRolledBack，注册能力只Mailbox六logical/五CLR族；原Write/ReadOnly/WriteResult五consumer与三个raw不动。

来源不是business自报：internal真实Tx AbortActionAsync在原Open事务所有参与者回滚/Completion完成后才签不可伪造fact，含自身Tx/调用identity/occurrence/阶段/原cause/输出/完成。显式unit的直接领域await观察器将本次fact单次交回拥有的runner；fact必须在runner自己的失败Abort前存在，后来Abort普通throw不能补来源。Tx只留自身操作事实，无request/job/session；runner显式绑定本次request/job/lease。公开机制仅当前显式单元，不做业务mapper、不新ambient/容器/Host/outbox。机制交接signal和pending只阻止catch后继续，不授业务成功权；普通outbox没有request参数，因此登记/第二事务的阻挡明确由已有真实outbox unit的窄admission barrier实现，只存unit/pending/自身guard occurrence，不持request或callback；runner显式读取guard合入sticky，不新增jobslot/第二outbox，不改变原unit状态/Complete/flush。完整unit/lease cleanup后才能给单元结果，scope/drain/Close后才可给bus成功。

同cause仅是必要引用条件。fact后真实unit/request/current/registration guard的独立occurrence及sticky/GuardFailure，和独立lease/scope再次抛同对象，均撤销候选并保两次发生；普通传播不误算第二次。没有fact、错误阶段/lease/request/job、取消/typed/未知完成/已有sticky/guard/未完成清理均不恢复。普通非self动作拒绝只在原Open事务窄catch RequireRollback；正常返回拒绝/Querynull保原提交事实，不统一回滚。

共享RunAsync只把原catch cause传入真实Abort，利用原typedRollbackFailure聚合保原SQL和Rollback第二cause，不改即时自结算/IsSettled分支/caught Commit必throw。Mailbox六路唯一whole、五handler薄入队；每原fine路由一次ordinary写、空participants/None，保Send/Claim资产Prepare/gold在事务内及原单元后投影顺序。whole恢复映ACK前重核精确session/IsInWorld/OnlineUserId/物理身份，机制不硬编码Mailbox也不套Cera actor。

VC-B5 C1/C2/C3等不可恢复走原默认失败/精确source-close/chain，无新增selected结算Fence/DiscardExact；正常disconnect/teardown照旧。scope前可能已有success/normalrefusal帧，按实际unit状态存活后置后关闭，不由旧slots[]推零帧、不撤消息/earlyflush/补StorageFailureACK。B5权威勘误single3帧/multi4帧已入验证，历史report/review/Task不覆盖。

## 受影响位置、责任和限制

修改Planning Choice、§4共享contract/Mailbox阶段、§5现有ICommandWorkUnits能力说明、T-004/T-005状态与依赖、P6验证责任、最终VC列表、NextBoundary及P6修订记录。P5 Cera要求标为已由Task29交付的历史起点；P3/P4历史原要求保持。T-004仍有具名8消息7fine残余；T-005拍卖只有outboundB7无入站，不凭包名造Task，租赁2Main/独立Tx refresh仍pending；T-006～012不减。仅Mailbox是下一UsefulUnit，机制/六consumer/删除/有价值验证同交，不先空壳。

Task31门后承担真实decoder/同provider/Domain/SQLite/outbox/production sender对照Task30十四场/59focused、来源/cleanup有行为价值mutation、nativeCommit与self区分、C1durable/C2双因/C3whole scope、actualsourceclose/chain/替换物理隔离及原强driver迁移。共享ItemDelivery/GM整批prepared-audit/QuestHelperPuppet/Housing/Wedding原子性、即时结算和双因按所触真实用例直接核并披露未测，不静态冒端到端。same-run全部110表/列NULL/BLOB原样回滚，跨run仅已证具名SQL时间/HousingUTC/GUID图窄关系；完整NUL nonowned与canonical/manifest/行尾不变。

预计改共享core，临近实施要求fresh Release solution --no-incremental0/0＋八项目full；原类并行/5m/Skip/detailed/DFO不改。当前9029=9009pass+原exactDimension2fail+18skip只历史引用，DFO九expected67＋七actualcontrols7=74、Golden15/Char10、注册/runtime/purefamily/3raw全owners不放宽。2秒性能及T-012最终fresh核验仍后继。细source白名单、逐命令、driver/非null结果留Task31，不在高层Plan预写。

本轮只作者静态制品/身份检查：快照直接复制原bytes，写后15725个nonowned tracked文件SHA未变；未运行build/probe/testhost/native/新测试，无产品/测试/ledger/A/Contract/routing/Task改动，无git mutation。只允许plan.md、本amendment、新原P5snapshot三路径。writer落稿停止，交fresh challenge及另一fresh独立Plan review，主控消费及amendment-only检查点后才细化Task31；本记录不批准自身、不激活产品、不关闭T-004/T-005/Spec002或解除Astra/用户收尾限制。
