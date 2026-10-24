# Plan P5 独立评审

```yaml
kind: rks-review
verdict: approved
```

## 目标、独立性和规划层级

Reviewer 为 fresh independent `b_plan_p5_review`，与 P5 producer、challenge generator、B4 producer／reviewer及主控分离；应用 `reviewing-work` 的 Plan 判据。目标是 `mode: task-units` 的高层受影响方法计划，审核 Cera 这一临近消费者所需的共享接缝、责任、依赖和验证；不要求预写全部 Task／fixture细节，也不以尚未运行的未来产品结果代替计划判断。

唯一工作目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，HEAD `f42efb55bedad71dc1196100e39255bc483ed72c`。

| 精确输入 | bytes / SHA-256 |
|---|---|
| [Plan P5 ready](../plan.md) | 71863 / `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| [P5 amendment](../verification/plan-p5-amendment.md) | 12843 / `5d2a9c1aa0fff1877b9b75b6f904293f75f5bdf40b8e6dfe404c2af98c4553ba` |
| [P4 原文快照](../verification/evidence/plan-p5-amendment/plan-p4-before.md) | 61551 / `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`；独立核其 bytes 等于 HEAD 原 Plan |
| [P5 immutable challenge](plan-p5-challenges.md) | 6021 / `2b9a0dc65666e57f1d327a65c51cf58ac51f4c8849de437b36cb2548ce250b1e`；high-risk0／low-risk0 |
| [已批准 Spec B4 ready R2](../spec.md) | 42632 / `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| [B4 fresh review](spec-b4-review.md) | 7624 / `19d7a2d0a5075f89858ab1c32b70d575fbabe350a4e62f45d8f7528eeb659848`；approved，CH-1在R1成立而R2已解决 |

已读根／子 Contract 的目标、保护及可见变化约束、current B4 的 Cera 专节／VC-B4／相关 AC、P5受影响段落和总体路线、amendment、matching challenge；亦读适用 AGENTS、架构哲学及重构偏好。实际检查 Cera 旧 handler／fine／domain／通知／注册、原强测试和 Host 竞争用例，现有 WriteResult／NonRequest／RequireRollback／CommandBus／SelectedSessionSafety／outbox，以及 TASK-28 正式七例的完成事实、cause图和 A/B 完整资产对照。生产报告只作为取证入口，没有代替源码检查。

## 独立判断

1. **下一有用单元和共享能力相容。** Cera 原 fine 只有一次 Transactional，领域入口及多处写前必须 DemandEffects；现有 `CommandWorkUnits` 已能对 ProtocolNonEffect 建立一次 Ordinary write＋单精确物理 actor 的 NonRequest 足迹。P5 显式保本次 session、physical connection和 Game metadata，不用空参与者、Main、LegacyFootprint或隐式request。复用当前 WriteResultAsync 即可消费封闭结果，无需先交接口空壳或修改 runner。薄 handler、唯一 whole、原领域交易所有者及旧 fine／生产转发删除的责任明确，符合协议级 CMD 目标。
2. **动作拒绝和提交恢复没有混成宽 catch。** 实读 runner 的 BusinessRollback 与实际 CommitAsync 窄span：RequireRollback(cause)能将普通 action cause 送入真实 Abort，typed／取消会拒绝并保持失败；CommitRolledBack只在 KnownStorage原来源、同cause、ConfirmedRollback、健康request／current且无guard／uncertain等条件下产生，finally仍完成unit／lease清理及tracked operation。P5把actor／footprint／runner守卫留在业务try外，只包原clock／领域调用，单元结束后映原拒绝；没有调用方自报已回滚、clearFailure或普通异常恢复API。正常返回拒绝仍保原空提交，不一律RequireRollback。C2的SQL加Rollback第二cause不能因先捕到SQL而被恢复，当前方法能保留typed及两因。
3. **VC-B4的精确保护被落实，而非重新选择。** NonRequest要求一个精确actor；Committed／Uncertain的该actor由runner登记后，只有进入默认失败收口才经FailAsync／BlockAsync执行Fence、精确连接阻断、DiscardExact及关闭。P5与已批准B4一致，把旧IAccount-only空selected保护集合到新精确买家保护的变化列明，不为保旧无围栏而削弱足迹；健康成功、正常拒绝和健康确认回滚不调用失败保护。对替换身份／其他物理连接要求实际状态或受控真实替换证明，未以“Exact”名称当成动态通过，也未把关闭生命周期的正常投影误称误伤。
4. **C3 scope与出站阶段正确。** 当前 CommandBus 依次 drain、结束request、清理真实scope、最终FailAsync；GameplayOutbox.Complete保留已提交单元及单元外条目，按登记顺序处理，并把失败来源关闭置后。P5将旧fine scope故障映到新whole实际DI scope解析的受控资源，不用body手抛／手动Dispose冒替。body结束前可能已登记正常消息的事实被保留，不追加StorageFailureACK、撤消息或提前flush，不统一要求零帧。该方法与现有实现相容，原cause／独立清理事实及提交事实都有明确后继观察。
5. **业务语义和测试意图有具体承接。** 旧cart／receipt／refusal日志、缺session／online分支、错误码4／11／1、两个Project的受众与先后、扩容快照→产品ACK→wallet→gold→inventory→pet→restriction→premium/state均有原源码对应。原PVF、余额／gold、UID／宠物、权益／名字牌／容量、晚期回滚、旧selection／持久owner、receipt不投替换身份等强意图要求保留。旧direct-fine测试按实际可表达范围转真whole／decoder或真domain＋原显式事务test seam，不能新增虚构wire字段、删除NoSession／回滚断言或留下legacy转发。Host真实HTTP grant与三次真实购买、逐receipt及最终97810／三operation明确保留，未被SQL预置或假receipt替代。局部fixture和结果形状能在临近Task以现有强断言低成本收敛，不构成当前高层阻塞。

## 挑战与验证范围

匹配P5挑战没有保留编号问题（high-risk0／low-risk0）；独立核其五条攻击路径及不保留理由，与上述源码和已批准B4相符，没有发现需上游返回或补充的独立material HOW缺陷。此处不是把“0挑战”直接当批准。

直接读TASK-28七例：success和正常拒绝为Committed，A为未receipt的真实action19/1811／ConfirmedRollback，B为先receipt且KnownStorage／FallibleCommit的真实SQLite提交19/787／ConfirmedRollback；A/B的全部before／after资产相等。C1资产已提交而Uncertain，C2两cause／Uncertain但资产实际回滚，C3scope故障时Committed；旧均正常返回拒绝ACK且无围栏／close。P5正确把这些作为已接受before及受控故障输入，没有冒称迁移后已通过、生产事故或TCP／A21验收，也没有把NonEffect的新合法购买冒Main／同Ordinal幂等。

P5对新单元要求同provider真decoder、原领域／SQLite、原adapter／outbox、真实sender＋受控transport，全列NULL／BLOB和事件、cause引用、native阶段、实际帧／close／chain及source/assembly freeze对应。C1/C2/C3、正常对照及精确actor不误伤有明确动态要求。按实际影响至少App／Protocol／Networking full，Host受改则完整Host；若触共享core/API必须返主控并扩大八freshfull，未削弱最终T-012八full／DFO74+7／Golden15／Characterization10／单条2秒测量。逐case/status/multiplicity和混用库存的原bytes／SHA provenance条件明确，9014仅引用Task27，不冒本轮新全量。未来局部结果仍required fresh快审。

## 不变项和批准边界

独立复算全部目标／gate身份；P4快照逐bytes等HEAD。T-005～012表行、P1～P4修订段与原P4逐bytes保持，既有后续路线和责任未被本次覆盖。child Contract、routing和TASK-28当前bytes仍等HEAD／已知身份，index空；当前tracked变化仅Spec与Plan。本轮只消费已批准B4，不修改WHAT、三个raw出口、默认Write／ReadOnly、KnownStorage／guard／cleanup或Compose禁用，也未重开A。

`approved`仅覆盖当前P5高层受影响方法可供后继详细Task落实，无未解决blocking finding。主控仍须完整消费B4/P5门、更新routing并完成amendment-only本地检查点，然后才指定Task路径、做readiness和生产；本review不创建Task29、不设置Task完成或证明产品／Spec002完成。

Reviewer仅新增本文件，不运行native、不改target／产品／历史证据、不stage／commit／remote；此记录完成后停止写入。
