# Spec B5 独立评审

```yaml
kind: rks-review
verdict: approved
```

## 评审目标、依据与独立性

这是作者／challenge generator 之外的 fresh independent Spec review，应用 `reviewing-work/SKILL.md` 及 `references/spec.md` 的结果、范围、约束、验收与最小充分纠正标准。唯一 workdir 为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`，审核 HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`。本批准只绑定以下精确目标及其 Mailbox WHAT 变化，不批准产品实现、Plan P6、Task31 或 Spec002 完成。

| 制品 | bytes | SHA256 |
|---|---:|---|
| 当前 `../spec.md` B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| `../verification/spec-b5-amendment.md` | 11251 | `70d86067ba8ebdba9e5df0fe1e0efb118d5c57ba4d03919ec6d2920863eb20ec` |
| B4 原字节快照 `../verification/evidence/spec-b5-amendment/spec-b4-before.md` | 42632 | `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| immutable `spec-b5-challenges.md` | 6854 | `5d25a35f5bfa08e074ed7b20d268c3804246273ffb80e0c26df4a66bf99fa701` |
| 未修订的 `../plan.md` P5 | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| 已提交 `../tasks/TASK-30.md` | 28640 | `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c` |

完整读取 child Contract revision 1、当前 B5、amendment、匹配 challenge；读取根 Contract revision 4 的 Goal／Constraints／SC-8B／SC-13、Task30 Result／独立审核及 Controller Consumption R1 完整末节，并核 P5 的 §4、T-005 和后继边界。根与 child Goal 允许经审查逐条列出的有意变化；Task30 调查批准不是 B5 批准，旧 B4／P5 也不替代新 gate。当前 auto gate 不要求新增用户 HITL。

## 结论与验收闭合

无未解决的阻断 finding，`approved`。B5 将已证旧缺口和有意变化明确限定为 Mailbox，不把不可恢复错误继续包装成普通 ACK／正常完成；共享 RunAsync 的双因精度也明确列为领域语义变化。它保留健康成功／拒绝／真实完整确认回滚、即时自结算及 shared caller 原子性，不以结构迁移为名暗改交易结果。WHAT 可实现、可判定，来源事实与机制 API 留 P6 选择符合本阶段分辨率。

独立与 B4 快照比对：RQ-001～017、AC-001～011 的 IDs 逐项相同；VC-B1～B4 各整行不变；RQ-006 三个具名即时编码出口的完整专节和 Cera 完整专节逐字不变。新增专节／VC-B5／RQ-006、007 的引用、AC-003～005／011 及修订记录共同表达新边界；VC“其余”排除 B1～B5，不再要求 C 组旧 ACK／健康连接与新默认失败同时成立。

- AC-001／002／006～010 的既有义务保持。Mailbox 六消息仍受注册映射、真实入站、显式请求、保护判据、完整回归、文档与测量约束；没有凭新增文字提前计测试通过。
- AC-003 明确原 NonEffect／空 selected 的范围、全部 current／session 身份门和精确物理来源关闭，不套 Cera actor／footprint／Fence／DiscardExact，也不豁免误关替换连接。
- AC-004 分别要求 action 内自回滚和 KnownStorage native Commit 的真实来源、Current／Completion、原 cause、完整资产／事件、六路原提交／投影点，以及 standalone／ambient caught refusal 后 Commit 必 throw 和共享强用例；不能用延迟 RequireRollback、第二事务或弱化旧断言取代。
- AC-005 逐列要求 C1 durable Uncertain、C2 实际 SQL＋Rollback 第二因、C3 whole 真实 scope 清理错误；把取消、未知来源／完成、sticky／guard、失效请求、独立 unit／lease／scope 和同对象独立二次发生排除恢复，并保持原五 consumer 默认保护。恢复所需来源、Completion、当前健康和全部 cleanup 均有反例方向，不等同于 IsSettled、快照、类型／文字或自报。
- AC-011 与 VC-B5 要求最终汇报披露具体变化、共享影响和未测边界；不改 A 或已提交调查制品。

## 独立源码与原记录核验

1. **真实自结算与 commit 来源可区分。** `MailboxDomainService.cs:171–190` 在 action catch 内 Rollback；`DomainTransactionScope.cs:37–38` 对已结算事务返回 null；`DomainTransaction.cs:118–123` 拒绝再次 Commit。`CommandWorkUnits.cs:108–122` 当前恢复 span 仅围 Commit，故 P5 不能直接充作 Mailbox action 的既有授权。B5 明确这项差别，且没有选定新 API。原 `MailboxCoreIntegrationTests.cs:456–470、530–536` 仍要求 standalone 失败资产不变、ambient 捕获拒绝后 Commit 抛错，与新 AC-004 一致。

2. **C2 真实丢因，B5 明确修正共享精度。** `MailboxDomainService.cs:181–190` 无 cause Rollback 与 `DomainTransaction.cs:273–280` 的组合，解释原 typed 图为何缺 action cause。独立选字段读取 `final-03/rollback.json`：SQL 19/1811 为 ID1，Rollback 为 ID2，typed ID3→Aggregate ID4→ID2，未达 SQL1；实际 Completion／unit 为 Uncertain，即使 Before==After 也不能改称 ConfirmedRollback。B5 要最终图可达两原对象且保持 typed 事实，不假称旧生产 logger 已保两因。`ItemDeliveryDomainService.cs:168–175` 的 IsSettled 防护、`GmOfficialMailDomainService.cs:118–150` 的多目标 prepared／receipt／audit，以及 QuestHelperPuppet、HousingOperations、WeddingSettlement 的实际 mail 调用均支持所列共享影响；原子性／即时结算与错误图精度的要求分别清楚，没有只限网络的遗漏。

3. **旧 A／C 组与新可见变化相符。** 独立读取 action、claim-refusal、commit、nonfallible、rollback、scope 的 Summary／Exceptions／必要 Stages／Transport 字段：action SQL19/1811 和 Claim GoldLimit 为 ConfirmedRollback；native Commit19/787 有 fine body 已返回的原阶段；C1为 Uncertain、C2为 Uncertain且实际回滚、C3为 Committed。C1／C2／C3均旧 dispatch 正常返回 ACK3、实际 CloseAsync=0。scope 故障前真实 CurrentIsNull=true、unit=Committed、Available=true、Slots=[]。这些支持 B5 的 before，不能当作迁移后证据。

4. **帧数后继勘误准确。** 对 final-03 原 Transport.ActualFrames 的完整 hex chunk，按出站 15B 头内长度逐帧解析到 chunk 尾：single 为 `(CMD,005E,16)`＋`(NOTI,000E,119)`×2，共3帧；multi 为 `(CMD,013B,16)`＋`(NOTI,000E,119)`×3，共4帧。未混 Retry／HealthyControl／LiveOnlyDrain。amendment 第36行纠正旧 report“各4帧”的文字精度准确；未要求覆盖旧 raw／review。

5. **whole 清理前条目与来源关闭有独立语义。** `MailboxDelivery.cs:66、78–90` 原在 fine 返回后登记 ACK／投影；`CommandBus.cs:95–134` 的 scope dispose 在 whole body／drain 之后；`GameplayOutbox.cs:226–240` 先按实际 unit 状态保留条目，再追加 failed source close。B5 正确要求观察清理前已经登记的 success／normal refusal 及最终顺序，未从旧 C3 空 Slots 推新 whole 零帧，也没有新增撤条目、early flush、raw 出口或幂等重放。

## 挑战处置与身份／生命周期边界

匹配 immutable challenge 的保留集为 high-risk 0／low-risk 0，不存在待裁决的编号 CH。该计数本身不构成 approval。本 reviewer 对其五项已尝试攻击分别独立核对：settled 代证／混淆 action 与 commit、共享双因与 caller、空 selected 误套 Cera 或漏 source close、旧 slots 推零帧、旧帧数与“其余”冲突，均未在当前精确稿发现阻断缺陷；候选按当前证据 **rejected**，具体依据见上文。没有通过增加假设问题或强行指定新接口制造 finding。

进一步核了正常 disconnect 的边界：`OnlineUserConnectionObserver.cs:66–141` 仍沿 lifecycle 工作清理精确旧 session，`SelectCharacterLifecycleServices.cs:184–203` 仍 EndExact／ReleaseExact 并只清无新归属容器。B5“不新增 Cera actor／Fence／DiscardExact”由同句的“原 IAccount-only 空 selected 参与者保护范围”和后文“selected 参与者集合”限定，表达本次结算保护；并非禁止真实 Close 后既有正常 teardown。RQ-002／014 的生命周期／链和保护持续不变量保持，当前文本没有要求禁用该清理。后继不能把 controlled in-memory transport 中的零 Discard／未模拟 disconnect child 外推成真实关闭全链零调用，亦不能把既有正常释放算作误伤。此处是对现有边界的解读和证据限制，不是新增 WHAT 或要求新 API。

## 限制、交接与停止

本轮只做只读源码／制品／原 JSON 字段解析及身份比对，没有 build、probe、testhost、native 或新迁移证据。Task30 的取消／replacement、各操作完整 C 矩阵、真实 disconnect／TCP／A21、性能、新八项目 full 及 caller 全故障端到端仍未由旧调查证明；B5 已要求对应新证据并如实披露，当前 Spec gate 不把未来运行结果当输入 prerequisite。

仅新建本文件，未修改原／未提交目标、Contract、Spec、amendment、P5、routing、产品、测试、旧 raw／review／Task；无 git mutation、Task31 或其他 writer。审核结束核对上述目标 bytes／SHA 与派单一致。主控消费本批准后才可进入受影响 Plan P6 的修订和 fresh challenge／review；仍须其消费及 amendment-only 本地检查点后才规划／执行 Task31。此批准不关闭 ledger、T-005、Spec002 或分支，不解除 Astra／用户收尾／不推送 PR 门。review writer 已停止。
