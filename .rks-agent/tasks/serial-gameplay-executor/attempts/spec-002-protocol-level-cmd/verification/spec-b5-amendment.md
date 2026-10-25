# Spec B5 amendment：Mailbox 即时自结算与不可恢复失败收口

## 来源、授权与前版精确身份

这是受命的 WHAT 修订。唯一 workdir 为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`，起点 HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf` 且 clean。本 writer 使用 using-rks-agent 的受限子任务规则与 writing-specs，不重路由、不修契约、不选恢复 API，也不实施产品／测试。

主控权威为已提交 [TASK-30](../tasks/TASK-30.md) 的「Controller Consumption R1／后继受影响 WHAT/HOW 的主控裁定」完整末节。TASK-30 Result R1、fresh independent approved quick review 和主控消费证明的是有界旧调查，均不代替 B5 Spec gate。主控沿根 TaskContract revision 4 的 Goal 与 child revision 1 的 Goal「除经审查的 Spec 逐条列出的有意变化外」及既有 auto gate，选择本次具名变化。根 Source Request 的用户“你来做主吧……Astra……过了我们才收尾”与继续完成 Spec2 的授权仍适用；这不是用户新追加协议要求，也没有解除后续独立评审／收尾限制。

| 只读前版／权威 | bytes | SHA256 |
|---|---:|---|
| 根 `../../../TaskContract.md` revision 4 | 32812 | `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4` |
| child `../TaskContract.md` revision 1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| B4 Spec ready R2，现逐 bytes 存于 [spec-b4-before.md](evidence/spec-b5-amendment/spec-b4-before.md) | 42632 | `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| 当前未修改 P5 `../plan.md` | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| 已提交 `../tasks/TASK-30.md` | 28640 | `b7df96bbce279817f4f81e28de1063d52cabe531fcb927a6b68442d80e91344c` |
| Task30 `evidence/task-30-inspection/manifest-final.json` | 49129 | `543c8b8ba28e21e10506fc18079f80e5f2ad2820c646a0f57269f8666ab5a373` |

B 的 Spec baseline 仍为 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`，已接受 A seal、产品冻结与历史 Task 不动。上述身份是作者写前实读核对；快照直接复制 B4 原 bytes，无模板重排或行尾改写。本轮只允许改 `spec.md`，新建本记录和该快照。

## 实际冲突与取证精度

权威运行链为 [freeze-07](evidence/task-30-inspection/freeze-07/) → [build-07 原日志](logs/task-30-build-07.log) → [final-03 原日志](logs/task-30-probe-final-03.log)／[final-03 原记录](evidence/task-30-inspection/final-03/)。14 场独占 fixture 通过真实注册 decoder→同 provider dispatcher→原 fine→真实领域／SQLite→原 outbox／production OutboundSender；59 focused＝Application49（Core30 含 partial14＋ProtocolIntegration19）＋Protocol codec10，全通过、零跳过。这些是已提交旧调查的历史证据，作者未重跑，不冒迁移后或本轮新测试通过。

旧 [MailboxDomainService.RunAsync](../../../../../../Server/src/Dfo.Server.Application/Mail/MailboxDomainService.cs) 在 action 的 catch 内真实 Rollback，不是延迟 RequireRollback；action 抛回时 IsSettled=true、Current=null、Completion=ConfirmedRollback。原 standalone／ambient caught refusal 后 Commit 必 throw 的共享强用例保护的是资产不能部分提交。现有 P5 的 commit-only 结果 span 不能直接当作已授权的 action 自结算恢复；本 WHAT 不指定应新增或复用哪个 API。Cera 的足迹及 VC-B4 也不是邮箱依据，原邮箱 IAccount-only 未建立 selected scope。

| 已证旧事实 | 与 B5 的实际关系 |
|---|---|
| 真实 Send 晚期 action SQL19/1811、Claim GoldLimit 均在 domain 内自回滚，完整生产列恢复，原 ACK 分别3／10 | 保旧健康拒绝响应、资产和即时结算；不改成第二事务／延迟回滚。SQL19/1811 是 action 源，不能冒 native Commit |
| fine body 返回后真正 SqliteTransaction.Commit19/787；KnownStorage／FallibleCommit、同 cause、ConfirmedRollback、全列回滚、ACK3 | 保旧健康提交失败拒绝；不以 controlled Prepare 代替该证据 |
| C1 SQLite 已 durable，非 fallible Commit 违约 typed Uncertain；旧 ACK3／正常返回／连接健康 | 新 VC-B5 不普通恢复，保 durable／Uncertain 并默认失败／精确来源关闭 |
| C2 实际 SQL ID1 后 Rollback 固定第二 cause ID2，实际 SQLite 全列回滚；typed ID3→Aggregate ID4→ID2，缺 SQL ID1 | 新 VC-B5 保 typed Uncertain，不由快照假称确认回滚；共享 RunAsync 最终错误图须可达两个原 cause 对象。SQL ID1 旧时只有测试侧 RepositoryObserver logger 留同引用，production logger 只见 typed；不伪称旧产品已保双因 |
| C3 真实 fine DI DisposeAsync 抛独立 cause；事前 Committed／Current=null／unitCommitted／slots=[]，之后只有旧 ACK3 | 新 whole 的真实 scope 清理失败不能普通恢复。未来 whole body 前已登记的原 success／normal refusal 帧可先于 scope 错误存在，按原机制保留后关闭；旧 slots=[] 不能证明新故障零帧 |

C1／C2／C3 的旧结果均有真实 sender 的 CloseAsync=0、空 close entries／recorded chain、Fence=0、DiscardExact=0、IsBlocked=false、原 Standing 有效及同一物理 reference 健康 Query 控制。空链仅负观察，不是正向关闭／disconnect 链证明；新精确来源关闭与继承链必须另取迁移后证据。

**后继勘误（只更正本次引用精度，不改 Task30）：** Task30 report 的 single／multi 表行“各4帧”和 Send 正文把两种发送合写，不能作为单附件帧数依据。权威 [actual-frame-checks.json](evidence/task-30-inspection/actual-frame-checks.json)、[matrix-summary.json](evidence/task-30-inspection/matrix-summary.json) 与 final-03 原 bytes 表明：single 是 CMD005E 16B＋两条 NOTI000E 各119B，共3帧（ACK→stack变化→gold）；multi 是 CMD013B 16B＋三条 NOTI000E 各119B，共4帧，资产组装按原 updates→cleared→gold。原 checker 与 raw 正确，旧调查 Result 不依赖该文字小计；保留 report／review／Task 原文，fresh B5 reviewer 须核这条精度。B5 不声称 single 有三条资产帧。所有 frame 数只计原请求，Retry／HealthyControl 不混入。

## 修订范围与保持边界

B5 在既有 RQ-006／007 加专节引用，新增 Mailbox WHAT 专节及 VC-B5，在 AC-003／004／005／011 补对应验收与汇报引用，并追加 B5 修订记录。“其余”排除范围更新为 VC-B1～B5。17 个 RQ、11 个 AC 的 IDs、VC-B1～B4、三个 raw 出口及其 provenance／cleanup 条件保持，不追改 B1～B4 历史记录。

网络可见变化只限 Mailbox 六条 NonEffect 消息的 typed 不确定、取消、已有 sticky／guard、未知完成或独立 unit／lease／CMD scope 错误：不追加普通失败 ACK 把它恢复成成功结束，走现有默认失败、精确物理来源关闭／原链。邮箱原空 selected 保护范围和全部 current／session 身份门保持，无 Cera actor／footprint／新增 Fence／DiscardExact；若真实新事实要求不同保护范围，须返主控和受影响 gate，不静默改。成功、正常拒绝及原健康完整确认回滚的 MailException／普通 action 和 KnownStorage native Commit 拒绝响应、持久化资产、事件与帧序保持。

恢复是结果条件：原 cause 同对象、真实 Completion／来源、当前 request／job／精确身份健康、无 sticky／guard、全部 unit／lease／scope cleanup 成功都必须真实成立。所有 settled、异常类型／文字或业务自报均不能授予恢复；独立阶段复抛同一对象仍须保独立错误事实。现有 Write／ReadOnly／WriteResult 五 consumer 的默认保护不放宽，不清失败、不扩大 raw、不开 Main／重放幂等、不中途 flush／撤消息。已登记原 success 或 normal refusal 按原 unit 状态和顺序保留后关闭，不承诺零帧。去注错是合法新发送。

共享原因图精度不能虚称仅网络：通过 MailboxDomain.RunAsync 的 standalone／ambient 调用遇原 action 错误及附加 Rollback 错误时，最终错误图同样保两个原 cause 及 typed Uncertain；不选择包装 API，也不把真实未确认回滚改成 ConfirmedRollback。受影响 caller 包括 ItemDelivery 溢出／MailOnly prepared、GM 多收件人 prepared 及整批 receipt／audit、QuestHelperPuppet 撤销／替换／日标记、Housing harvest、Wedding settlement／退款。所有健康事务原子性、即时自结算、caught refusal 后 Commit 必 throw 及现有强断言保持，不能为迁移方便删 IsSettled 防护或弱化测试。MailCreated／delivery 随业务同事务记录，LiveOnly 消费时投影的时序保持；不顺便改共享 caller 的协议响应、audit、事件或业务策略。

## 验证义务、局限与后续 gate

迁移后沿六路真实 decoder／同容器 domain／SQLite／outbox／production sender 直接对照已接受 before，分别验证健康成功／拒绝、自回滚与 native Commit、C1／C2／C3 及不能恢复的取消／unknown／guard／sticky／失效请求／额外 unit／lease 清理。核真实 cause 引用／typed 图、Completion／Current、全部生产列含 NULL／BLOB／UID／core／mail／claimed／事件、原 frame bytes／顺序、实际精确 physical CloseAsync 和原来源链；空 selected 不新增 Cera 保护且替换后的物理连接不误关。whole scope 必须观察 fault 前实际已有条目和最终发帧／置后关闭，不以“无新增普通失败 ACK”代“无帧”。共享原强用例及所触 caller 的原子性／即时结算和双因错误精度须有匹配证据；具体证明机制和测试选择交 P6／临近 Task，不在此提前计 pass。

Task30 的14场没有覆盖每条 Read／Delete／Open／Query 的完整 C 矩阵、取消／replacement、真实 disconnect child、TCP／A21、性能或新八项目全量；其共享 caller 主要是源码影响核查和原 focused，不可冒所有 caller 全故障端到端。当前9029＝9009pass＋原2 exact Dimensionfail＋18skip只引用 Task29 已接受库存，不是本轮 full。受控 participant／scope 错误不称生产事故，内存 transport 不称实机。旧 C2 原 SQL 只有测试侧日志留同引用的局限不被隐藏。

本记录和 Spec 均 metadata-only；未运行 native、未新增测试结果、未关 ledger。P5、根／child Contract、A、已提交 Task／工具／证据保持原 bytes。`state: ready` 只表示作者交付可审，绝非 approved。下一步为 fresh 有界 B5 challenge → 另一 fresh 独立且匹配本稿的 Spec review；approved 后才另行授权受影响 Plan P6，设计自结算事实／来源证明／有界恢复与 whole／shared cause 方法 → fresh Plan challenge/review → 主控消费及 amendment-only 本地检查点 → 才可详细规划／执行 Task31。旧 B4／P5／Task30 批准不得代替新 gate，本 writer 完成三路径后停止，不写挑战／review、P6、routing 或 Task31，不做 git mutation。Astra／用户收尾／不推送 PR 等既有门保持。
