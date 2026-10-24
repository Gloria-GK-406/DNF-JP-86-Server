# Plan P2 amendment：具名即时编码登记失败与显式重放链

## 目标、已消费gate及授权

目标为[Plan P2](../plan.md)及[routing-decision](../routing-decision.md)四个受影响gate路径，配置仍为task-units、sdd-governed/long-running、attention_review on-drift及原门强度。子Contract r1、根Contract r4、Spec baseline `10a7218a779493494b138bd6b43cc0fc1a84aa6a`均不改变。主控指定当前产品检查点为Task10已独立approved的`2d8081ae2a3ef687e88ec284df9e97588cc625a7`；本writer未运行git或native/build/test，仅只读源码/治理资料及写获准三文件。

P2消费的精确Spec-stage输入如下，写前已核实际SHA及匹配目标：

| 制品 | SHA-256 / 结论 |
|---|---|
| [Spec B2](../spec.md) | `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`；17RQ/11AC、VC-B1/B2/B3保持 |
| [B2 amendment](spec-b2-amendment.md) | `ca46e1cef8daf0e5d3c717986e2c2818264b8c60a82a471e18371e0b365bab87`；本轮只读，不能随P2改写已绑定输入 |
| [B2 fresh challenge](../reviews/spec-b2-challenges.md) | `abf3aa29bfe319e44daa7b36266473b52af22168a7dc0d2954106bf2af71e990`；high-risk 0/low-risk 0，非批准替代 |
| [B2 independent review](../reviews/spec-b2-review.md) | `2803cb6d90907c6cadb36510155d2d678d3ad4f409a9426a585e23dc867e7bfb`；approved，精确绑定上述B2/amendment |

原Plan P1 SHA为`d00675fd2f129a7a8588a5390814355cc7deeabefc928b2c1d6130ccae1f109d`，原routing-decision SHA为`fdc76959d36082a8ef6d8663d14c1af1ebd08c86f074bf9362fe250605233bb2`。本轮主控授权只改这两文件并新建本记录，不改Spec/B2 amendment、root/契约/A、旧Task/Result、旧reviews/challenges或产品，不创建Task11、运行native或进行git操作。

## P2共享设计范围

- §3：新旧共用唯一CWT ledger，抽pure admission、不带CMD/delivery/legacy ambient执行；ConfirmedRollback保原entry.Ordinal不延长窗口。新Selected/Rejected/replay输入与recorder显式传递；新bus/ledger/reader/sink所有Capture均显式recipient/metadata/null，包括ReplayStateDomainService内部旧Capture的必需贯通。仅旧route消费的具名LegacyReplay适配临时留账，最后消费者迁完删、最迟T-011清零。
- §4：保Task04默认业务/未知/typed/guard/额外独立cleanup全参与者保护。仅注册元数据声明的UseStackable postSettled RequiredExplicit和CommittedReplay postWrite CurrentStateBatch，绑定本请求/job/阶段/精确物理target/调用，才能取得一次性handle。Application不识别具体Protocol CLR；不是任意新出口或构造器工厂许可。
- 最小来源证明：Application内部sealed实现、Abstractions仅不透明窄透传、Game typed adapter保CMD/NOTI、actual App outbox仅真实encoder窄catch内部记原cause并原样throw。无公开自报入口或额外receipt/result框架；普通Register默认不变。callback/校验/lookup/outbox.Register等非encoder错误没有凭据，Required与BestEffort原规则保持。无新ambient/jobslot/DI当前请求/事务查请求。
- 最终裁决：先sticky记录并drain所有unit，在scope存活期间等待未await工作；按原生命周期Close、dispose并收集独立失败后，才最终分类/fence，之后由作业结束保持source outbox原序。不能沿当前CommandBus两次FailAsync提前作不可撤销typed包装/raw许可。最终同cause引用还不足以证明单一故障；独立dispose/unit阶段抛同一个Exception对象也撤销资格，按独立发生阶段保留证据及cause引用，不被Includes去重吞掉。
- replay原Transactional对应普通Write direct领域读取，后续MirrorMerge/Create/batch/Town事务外原序，不改ReadOnly/nested bus，不重复业务/ACK，不提前Main/Compose/deferred encoding。原主事务/Freeze/Settled与UseStackable登记分段保持。
- §5、T-004及Verification：只闭合上述输入/能力及共享回归。RecordedOutbound新旧接口alias同Registering、同outboxes/provider，保inFlight/真实encoder注错，不让窄接口绕wrapper；新handle是方法参数，不扩CMD构造注入清单。

完整T-001～012路线、其余依赖/目标/要求和P1既有F-07决定保持。T-004不继承未经同口径证明的“旧三个细粒度replay调用”数量：当前已核ReplayStateProjectionDelivery只有一处ReadReplayStateCommand dispatch，ledger/Joust的Refresh调用属于上游不同层；改按实际legacy调用者/层级逐行留账，不预设数量。原Next Planning Boundary的T-001被明确标为历史起点；当前下一Useful Unit是首次显式Effect/Main与完整UseStackable，待本轮gate及检查点后由主控给精确Task路径，P2不预写详细Task或提前迁BuyItem双CLR族等相邻业务。

## 风险基础及待执行gate

新的material-risk basis是来源证明被伪造/串用、业务错误误识别为encoder、独立第二故障被raw资格或按引用去重掩盖、reader暗读旧Auth.Metadata，以及wrapper绕过使测试假绿。P2为这些风险给共享数据流/归属和最后裁决规则；具体API名及有价值测试安排留临近Task，不能以本记录冒称实现或测试通过。

当前必须取得匹配P2的fresh有界adversarial challenge及独立Plan review，目标为本Plan/本记录/routing override。routing仅把当前制品定位为`reviews/spec-b2-challenges.md`、`reviews/spec-b2-review.md`、`reviews/plan-p2-challenges.md`、`reviews/plan-p2-review.md`；门强度/流程不变，旧P1审查原文留存。本Plan `state: ready`表示可供审查，不是producer自批。

P2 gate通过后由主控做amendment-only本地检查点，之后才授权临近Task规划/新产品写；不要求尚未授权的Task11实现反过来作为本Plan批准前提。后继证据须保原两条EffectOperationReplayTests的登记encoder注错、真实当前数量/字节/无重复ACK；补同cause/Committed/no new fence/source close、相同IOException业务/非许可/typed负对照、双故障含同对象独立阶段、跨request/target/closed/catch拒绝及可编译来源验证mutant。仍须原事务/无nested bus/无Compose、同provider真实outbox及Required/BestEffort保护。

## 前十Task及完成边界

Task01–10各自已接受结果与证据保持，不重写或扩大。Task10产品检查点新effect CMD和Main仍disabled；它完成mandatory context/ApplyEffects及真实来源参数，不证明新admission/Main/本次raw保留已实现。Task04普通postcommit/typed默认围栏仍正确，P2只是后继两个具名兼容出口的必要共享设计。

已接受Task10正式02数量为8523＝8503pass＋2准确原Dimensionfail＋18skip，8499＋N24－D0；仅引用已有Result，不是本轮重跑。首次实现本P2时需验证所改共享边界与既有失败/事务/生命周期保护，不能把B2批准或前十Task结果当该实现已通过。

不更改根/子契约、baseline或terminal，不重开sealed A，不宣告Spec/根完成；Astra及用户分支收尾仍是原保留门。中文、原工作区/分支、不push/PR/合并/远端issue/部署/清理、不运行df_game_r/DNF.exe等原限制保持。
