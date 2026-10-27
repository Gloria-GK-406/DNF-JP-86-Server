# Spec B4 amendment：Cera 购买拒绝与默认失败收口

## 来源、授权与精确目标

这是结果 B 的受影响 WHAT 修订，只将 Cera 购买旧路径的故障结果与现有显式 CMD 失败不变量之间的差异明列为 VC-B4。不是产品实现、Plan 修订、恢复 API 设计或 Spec 自批。主控的明确裁定在已提交 [TASK-28](../tasks/TASK-28.md) 的 Controller Consumption R1；检查点为 `f42efb55bedad71dc1196100e39255bc483ed72c`，Task 完整35298B、SHA `9c8e5a720b8a887e114331b3ddcd4232ac3c67a43967dbf7aca7c5893e32d24a`，其 required fresh 独立 quick review 已 approved。该批准只证明旧行为调查，不批准 B4 或后继实现。

child TaskContract Goal 已明示“除经审查的 Spec 逐条列出的有意变化外”保持 A；本修订沿该授权和原 auto Spec／Plan gate，不追加用户 HITL，不修改根／子契约。A 已封存，B 不重新设计执行器、出站、事务或调用链，也不改写 A 的历史结论。若受影响审查发现超出此授权的实际矛盾，返主控处理，不能以本记录替代授权或批准。

唯一工作目录仍 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`。写前 HEAD 为上述 TASK-28 检查点且工作区／index干净；本 writer 仅写当前 Spec、本记录和新 B3 原文快照，没有 native、产品／测试、ledger、routing、Plan 或 git mutation。

| 身份 | bytes / SHA-256 |
|---|---|
| 本次 [Spec B4](../spec.md) | 40180 / `ca7f64fd4dfcb23a32ffd731d196dea9e9a83ef092e00991fde89393ea2a06c4` |
| [Spec B3 原文逐 bytes 快照](evidence/spec-b4-amendment/spec-b3-before.md) | 32948 / `1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`；与写前 HEAD 的 spec.md 完全相同 |
| child Contract r1 | 8898 / `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| 原 Plan P4 | 61551 / `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`；本轮不改 |
| 原 routing-decision | 4751 / `db94843b5ececc4e27d3919e8159bf209c9a38332715fb4e0eb03308a69c6ff9`；本轮不改 |
| 原 B3 challenge / review | `d61c61fa5f8aad20a3518b45619878b38927027a9e280d3da881f5750cf80fac` / `42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52`；历史保留，不代 B4 gate |
| 原 P4 challenge / review | `7b23d10a1cd1eee526449351fb89e281645793b752b0de4423874d8a1c98644d` / `82611a76c0d4ee0dd264357dbd4c8d2c164050b919bc14e593cdac8ee01bbbbb`；历史保留 |
| TASK-28 原报告 / manifest | 8709B `601a55e13b8da7bded7c8783c3bcea4f6cf66f0fa265438c3c95b62a2194d50e` / 21161B `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314` |

Spec baseline 仍为 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`，根 Contract r4、本轮没有修改的 child Contract r1 和 A seal 均保持；root Contract 当前 SHA `d3d1bc4d244388c975f7ef05573c5f867be5fe90ea6b8eb3ac5ce61a586e2ef4`。本文不把当前任务 HEAD 当作新 Spec baseline。

## 已证旧行为与实际冲突

作者实际读取了旧 handler／fine／领域、当前 CommandBus／CommandWorkUnits、执行器和 outbox 最终收口源码，TASK-28 报告与正式七例 JSON 的 Summary／cause 图。旧 CeraShopPurchaseCommand 仅实现 IAccountCommand，NonEffect；原 handler 在 await 整个 fine 的 try 外捕获业务拒绝和全部非取消异常，成功通知在 catch 外。TASK-28 的同 provider 真 decoder 路径直接证实 selected scope=null；不能据语法或别的协议推断已围栏。

| TASK-28 正式例 | 观察到的旧结束事实 | B4 消费的边界 |
|---|---|---|
| success / refusal | 成功 Committed，原 ACK→wallet→inventory 实际发送；不足余额正常返回拒绝的事务同样 Committed、资产未变、ACK11 | 保成功与正常拒绝，不将空提交一律改成回滚 |
| action | 晚期宠物 slot INSERT 原生19/1811；此前 wallet／gold／前 grant／events 已暂存；最终 ConfirmedRollback、110表全部列（含 NULL／BLOB）exact before=after，原 ACK1实际发送 | 保原可恢复动作失败响应，保完整回滚，不新增关闭／围栏 |
| commit | domain先receipt、FK1/child1；真实 SqliteTransaction.Commit 原生19/787；CommitFailure 为 SqliteDatabaseParticipant / KnownStorage / FallibleCommit，同 cause 经两条Error；112表完整回滚，ACK1实际发送 | 保真实确认回滚的原响应；不能拿类型／文字或 IsSettled 代实际完成事实 |
| nonfallible C1 | 受控非 fallible Commit 违约：资产已经提交，CompletionUncertain；typed NonFallibleCommitContractViolation 保原 Inner；仍被旧 handler 转 ACK1 | 不保这种普通拒绝恢复，进入既有默认失败收口；不自动重做已提交购买 |
| rollback C2 | 真实 SQL1811＋受控 Rollback 第二 cause，typed RollbackFailure→Aggregate 保两因；资产虽实际回滚，Completion仍Uncertain；旧 ACK1 | 局部资产快照不把不确定结果改称已确认回滚；保 typed／两因并失败 |
| scope C3 | 原 fine DI scope DisposeAsync 在购买 Committed 后抛固定cause；旧 handler捕获并 ACK1 | 独立清理失败不能作为普通 StorageFailure 拒绝恢复；保提交事实与 cause，走默认最终失败 |

七例旧 dispatcher 均正常返回，实际 sender 报 Delivered，close条目／实际CloseAsync／connection-failure 均0；Standing.Fenced、ConnectionRevoked、IsBlocked均false，精确source close-chain为空。A/B去 trigger 后经 decoder 的新合法购买成功；这不是 Main／同 entry／Ordinal replay 或幂等保证。

证据边界保持：C1/C2/C3 是受控故障，不声称真实生产已发生。传输为 production OutboundSender＋受控内存transport，非TCP／A21；domain-enter全表只读可能提前enlist同一个SQLite participant，已披露且未新增第二fallible。原记录的 Probe 采用既有friend名称，实际E/bin的assembly身份不冒原Networking.Tests full；原tracked15205字节不变。B4 writer没有重跑这些探针，也没有任何迁移后动态结果。

旧 Cera 将上述不确定／独立清理错误归成普通拒绝并正常结束，与新协议 CMD 的默认 sticky／typed 最终收口不同。为满足一个协议一个CMD而不另加通用恢复通道，主控选择在 Cera 明列这项有意变化；不以“原来有catch”推导可为所有故障恢复，也不把它当隐藏的纯结构搬迁。

## B4 明确的结果与非结果

1. **保原可恢复业务结果。** 成功、正常拒绝、原普通动作失败及提交阶段真实确认完整回滚后的拒绝原因／错误码／原响应与资产事实保持。请求必须仍健康、清理完成且无 typed／sticky／guard／独立错误；未知完成、取消或业务自报不能取得回滚恢复。只陈述结果边界，不指定新 API。
2. **新增 VC-B4 仅覆盖 Cera。** typed不确定、独立unit／lease／scope清理及其他默认不可恢复错误不再因旧 broad catch 转成 StorageFailure ACK并成功结束。保原因与实际提交事实，进入既有最终CMD失败、精确物理来源关闭与调用链收口。参与者按原业务事实／工作边界确定，不能为了本变化擅自增减；不承诺新的选中会话围栏。
3. **已登记条目按原机制完成。** 当前 CommandBus 在 drain、request结束、scope清理后才终判；已提交后的额外非typed失败可按默认 typed包装，保原cause。当前 outbox只保已提交单元和单元外条目，按登记顺序交 sender，失败来源关闭置后。因此scope失败前可能已有成功或原正常拒绝消息，B4不宣称“失败零帧”；不得新增撤回、提前flush、因cleanup追加拒绝ACK或把不确定单元改Committed。
4. **现有不变量不动。** 三个具名即时raw出口、其exact encoder provenance／request/job/phase/target/singlecause/cleanup条件、默认Write/ReadOnly sticky、KnownStorage／guard／unknown／uncertain／清理边界、Compose禁用和F-132未修结论保持。Cera没有第四raw出口，提交后的正常业务投影继续受既有保护；不改执行器、outbox、sender、环境事务或协议字段。

B4修改仅为RQ-006/007的专节引用、新Cera结果专节、VC-B4与“其余”排除、AC-003～005的对应验收和AC-011最终汇报引用，以及追加B4修订记录。17RQ／11AC、VC-B1/B2/B3及其他段落保持；旧B1/B2/B3记录原样保留，不追改历史修订当时的数量或结论。原Commit-result仅四opening的P4文字此轮完全不改；扩至Cera须后续受影响Plan HOW门。

## 后继验收、gate和停止

迁移后须沿同容器真实decoder路径与TASK-28原oracle对照：普通SQL／实际fallible Commit确认回滚仍原拒绝响应；C1/C2及独立CMD scope清理则保不确定／已提交事实和原因引用，不再作为普通拒绝正常结束，实际最终来源关闭／链、参与者、登记条目及物理发送顺序可直接查验。scope故障观察要覆盖其发生前已登记的消息，不能以“没有拒绝ACK”代“没有任何消息”。原成功／正常拒绝及其它既有强用例不削弱；具体测法、单元／scope映射和恢复能力留受影响Plan／临近Task，不能借本记录预批实现。

当前 `state: ready` 只是作者可供审稿的声明。顺序为：B4 fresh有界challenge → 另一fresh独立Spec review → approved后另行授权Plan P5方法修订 → fresh Plan challenge/review → 主控消费/routing更新及amendment-only本地检查点 → 后继详细Task规划与产品。旧B3/P4 approved不可代此gate。此writer不改Plan、routing、不创建Task29，不写新的恢复API／whole catch方案，不把新Spec当产品已完成。

本轮没有native、测试、capture、ledger闭账或测量。Task27已接受9014＝8994pass＋原exact2Dimensionfail＋原18skip仍只是引用库存，不是本轮八full；TASK-01～28、根／子契约、A封存、baseline和全部现有门强度保持。Astra待审、用户明确收尾才push/PR等限制不变；没有stage／commit／remote／版本／tag／部署／游戏／cleanup。
