# Spec B5 有界独立挑战集

本记录由 fresh challenge generator 按 `generating-adversarial-challenges` 生成；与 Spec 作者和后继 reviewer 独立。它是不可变的挑战记录，不是 Spec 批准、完整性证明或实现授权。

## 目标、权威与攻击范围

唯一 workdir：`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`；只读核验 HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`、分支 `task/refactor-session-20261021`。

| 目标／对照 | bytes | SHA256 |
|---|---:|---|
| `../spec.md` 当前 B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| `../verification/spec-b5-amendment.md` | 11251 | `70d86067ba8ebdba9e5df0fe1e0efb118d5c57ba4d03919ec6d2920863eb20ec` |
| `../verification/evidence/spec-b5-amendment/spec-b4-before.md` | 42632 | `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| `../tasks/TASK-30.md` 已提交调查与控制器裁定 | 28640 | `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c` |
| `../plan.md` 未修订 P5，仅核身份 | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |

权威读入：root TaskContract revision 4 的 Goal／SC-8B／SC-13，child TaskContract revision 1 的 Goal／范围，当前完整 B5 与 amendment，TASK-30 原计划、Result、独立 quick review、Controller Consumption R1。根与 child Goal 允许经审查逐条列明有意变化；不把旧“其余不变”或 Task30 调查批准解释成否决后继 VC-B5，也不把 auto gate 改成新增用户 HITL。

攻击范围是 B5 的 WHAT 是否会让 P6 实现错误或无法验收：Mailbox 六消息 NonEffect／空 selected 与真实来源身份；健康拒绝、自回滚和 native Commit 的可恢复边界；C1／C2／C3 不可恢复；共享 RunAsync 双因精度和即时原子性；当前性、独立清理、原 outbox 次序及其 AC 覆盖。具体 API、实现选择、P6 方法和下一 Task 不在本次决定范围。没有检索或改写 A 的封存结论。

## 保留挑战

**high-risk：0；low-risk：0。** 本轮没有保留满足 skill 标准的独立缺陷挑战，因而没有生成空洞 CH 编号。下列核查解释主要候选为何没有升级为 finding；它们不是要求 reviewer 照抄的批准理由，后继 reviewer 仍须自行判断当前稿。

## 已尝试的具体攻击及边界

1. **把 action 已自回滚误当成仍打开的事务，或仅凭 settled 恢复所有失败。** 当前 `MailboxDomainService.cs:171–190` 的 action catch 直接 Rollback；`DomainTransactionScope.cs:37–38` 使已结算事务的 Current=null；`DomainTransaction.cs:118–123` 拒绝再次 Commit。现行 `CommandWorkUnits.cs:108–122` 的可恢复 span 只围住 Commit，不能拿来证明 action 来源。B5 专节 `spec.md:111–115` 与 AC-004／005 已分别要求真实 action 自结算、native KnownStorage Commit、原 cause、当前健康和全部清理完成；明确禁止延迟 RequireRollback、第二事务、IsSettled／快照代证及放宽原五 consumer。缺一个具体 API 属 P6 的设计义务，不能在 Spec 阶段当作错误。

2. **C2 原 SQL 被吞，或只修网络而破坏共享 caller。** 原 `MailboxDomainService.cs:181–190` 以无 cause Rollback 进入 `DomainTransaction.cs:273–280`，真实双因时 typed 图只保 Rollback cause。独立读 `final-03/rollback.json`：SQL 为 ID1、Rollback 为 ID2、typed ID3→Aggregate ID4→ID2，确无 SQL ID1。B5 `spec.md:113–115`／VC-B5／AC-005 把此缺口明列为共享语义变化，要求两原对象在最终 typed 图可达，保留 Uncertain，覆盖 standalone／ambient；同时列出 ItemDelivery／GM／QuestHelperPuppet／Housing／Wedding 的原子性与即时结算。原 `ItemDeliveryDomainService.cs:168–175` 的 IsSettled 防护和 `GmOfficialMailDomainService.cs:118–150` 的整批收件人／receipt／audit 说明影响是真实的，稿件未用“仅网络变化”隐藏它。未要求每个 caller 的全部故障都已动态通过；要求所触 caller 有匹配证据，未覆盖如实报告。

3. **为补来源关闭套 Cera actor，或空 selected 导致漏关／误关替换连接。** 原 `MailboxCommands.cs:15–27` 是 IAccount-only，并有 IsInWorld／OnlineUserId 身份门；`MailboxDelivery.cs:56–59` 有原入口门。B5 `spec.md:104、112、117` 和 AC-003／005 明确保留这些门及空 selected，不新增 actor／footprint／Fence／DiscardExact，同时独立要求精确物理来源实际关闭和原链、不误关替换连接。`GameplayOutbox.cs:226–240` 的失败 source close 与 selected participant fencing 是两条独立机制，现稿没有把二者混为同一身份。

4. **从旧 C3 slots=[] 推迁移后故障零帧，撤回已提交成功帧或补普通错误 ACK。** 原 `MailboxDelivery.cs:66、78–90` 在 fine 返回后才登记结果；原 `CommandBus.cs:95–134` 的 whole scope 清理在 body 后；`GameplayOutbox.cs:226–240` 先按真实 unit 状态保留条目，再添加 failed source close。独立读 `final-03/scope.json`，原 scope 错误确在 fine 已 Committed 后，旧路径正常返回并实际只发错误 ACK、CloseAsync=0。B5 `spec.md:117`／VC-B5／AC-005 明列 whole 可以在清理前已有 success／normal refusal 帧，要求观察前置条目和实际发送后关闭，禁止撤回／提前 flush／以“未补失败 ACK”等同“零帧”；没有将旧阶段的空槽冒充新阶段事实。

5. **旧错误帧数或历史 VC“其余”约束误导迁移验收。** 只读解析 `final-03` 实际 transport bytes，按原 15B 头长度逐帧走完整 chunk：single＝CMD005E 16B＋NOTI000E 119B×2，共3帧；multi＝CMD013B 16B＋NOTI000E 119B×3，共4帧。action、claim-refusal、commit、C1、C2、C3 原错误响应均为单条17B CMD，原实际 close 为0。amendment 第36行准确勘误 report“各4帧”且不修改原调查；当前 VC“其余”已排除 B1～B5，AC-003／004／005／011 对应保护、事务、默认失败及汇报。17个 RQ／11个 AC IDs 保持，未见遗漏 Mailbox 新边界而继续要求旧 C 组 ACK／健康连接的现行断言。

## 证据与停止边界

上述读取与 JSON 字节解析均为非破坏性检查，没有运行 build／probe／testhost／native，没有修改产品、测试、Contract、Spec、amendment、P5、历史 Task、原 raw 或 A；只新建本文件。原 final-03 是旧行为证据，未冒称迁移后通过、TCP／A21 或新 full。本轮结束复核目标四项 bytes／SHA 与派单一致，P5 身份未变。

本记录不批准 B5，也不激活 P6 或 Task31。后续仍由另一 fresh reviewer 对当前精确稿评审，再由主控消费；本 generator 写入后停止所有 writer，保留记录 immutable。
