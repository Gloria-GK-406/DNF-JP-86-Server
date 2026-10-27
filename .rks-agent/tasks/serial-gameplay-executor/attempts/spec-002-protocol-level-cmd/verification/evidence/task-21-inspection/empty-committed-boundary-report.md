# TASK-21 before：空 ordinary 事务后的拒绝 ACK 分类边界

2026-10-25，实际UTC为2026-10-24。产品及AGENTS仍精确HEAD `68f6d572f4b32cd2aa5987e373d889a8b3e0a33d`，两个新whole absent；尚无产品迁移／Task Result／ledger更改。按主控消息暂停产品推进，本文仅交有界技术处置，不自定Spec终局或新增许可。

## 已实跑旧行为

`verification/logs/task-21-empty-committed-before-01.json/.log`：18:47:11.265942～18:47:20.006774 UTC，dotnet真实exit0，1/1通过。`task-21-old-oracles-04` 于18:48:43.012160～18:48:53.017379 UTC真实exit0，56/56通过，其中原InventorySort/Warehouse 40条、新flow16条，亦含完全相同本case。

精确case：`InventorySortProtocolCommandFlowTests.Refusal_ACK_after_the_original_empty_committed_transaction_retains_the_failure_cause_and_assets`。同provider seed角色1的真实物品，再真实Select角色2；本地只读query forwarding proxy只令FindByConnection返回原角色1 session，其IsInWorld继续问真实registry并返回false。真正注册decoder→dispatcher输入为selector1/type0014/sequence7/body000100（装备默认排序）。wire本身不携带旧session，这不是自然生产并发或新增request入口。

原SortItemHandler已捕获旧session/当前online，进入原Transactional SortItemCommand。proxy在原fine的IsInWorld处直接记录实际ambient DomainTransaction，fine按原正常拒绝return receipt=null；事务最终真实Completion=Committed。随后handler在fine之外直接发送失败ACK，实际FaultingEncoder只调用1次并抛固定同一IOException。assert直接Same原cause/raw、不是typed；该失败batch没有登记，最后exact旧physical source-close1；SelectedSessionSafety.IsBlocked=false；完整items/slots/stats/container/cargo/events/deliveries前后精确相同。完整raw cause/stack、metadata、资产见`empty-committed-before-observation.json`及原日志，没有做GUID/时间豁免。

这不同于此前直接prevalidation错误页ACK：那个分支未调用fine／无事务；也不同于成功通知prepared batch的encoder/composer失败：后者被原NotificationBus捕获为Warning+Failed、调用者正常结束。两种原路均已在old-oracles04分别真实通过。

## 最小新default静态预测（未迁、未运行新whole）

Task21当前规定普通WriteAsync([],Ordinary,None)仍保原world/online拒绝在action内，正常无receipt仍完成原空事务。现核心源：

- `Transactions/CommandWorkUnits.cs:127` 在普通CommitAsync成功后无条件`request.ConfirmCommit(participants)`；
- `Commands/CommandRequestContext.cs:172-173` 此方法置HasCommittedUnit=true，即使participants=[]；
- whole随后直接IGameCommandOutbox.RegisterCmdAsync失败，在Bus原ExplicitAsync收口（CommandBus.cs:93/128附近）记录并调用原唯一FailAsync；
- `CommandWorkUnits.cs:187-190` 先检查三个具名raw来源；本NonEffect、无exit/attempt不符合。HasCommittedUnit=true且原IOException非typed时构造PostCommitPublicationFailure typed wrapper；内层仍同cause。
- 接着按CommittedParticipants逐一Block，本簇原participants=[]，因此不会额外fence。作业原EndHooks仍因失败追加相同exact source-close。

故静态候选差异只有内部错误外壳raw IOException→typed(PostCommitPublicationFailure,inner=原cause)，预计资产/帧/批次/来源关闭与空参与者围栏范围不变。上述“新”仅为核心源码预测，尚无迁移运行证据，不能写成已测等价、已修或新的第四raw许可。主控需裁定它是否已由B3默认规则覆盖，还是影响父RQ023c/契约等价边界而需先过受影响修订；执行者不通过跳过空事务、RequireRollback、忽略HasCommittedUnit、扩大catch或改共享core来求旧断言绿。

## 证据与停止点

- `old-driver-source.cs` 是old-oracles04实际before驱动完整bytes（当前flow SHA `3e606917e634d75cbe5353880b5bb4f04917ea127bcb44d8e2c422a6747fa43f`）；完整产品前源库存见before-source.json/preproduct-source-proof.json，原Fine/handler在before/。
- capture-before01及runtime-family-before01真实导出，baseline173logical/203cmd、28P/1I/174Legacy、146Tx/44D/9RO/4W、message38P/135L。
- old-oracles01编译诊断、02仅observer误读ConditionalWeakTable.Count的诊断及原02driver都保；03实际50/50、04实际56/56。不是行为RED/TDD或产品回归。
- 同run action SQL19/1811与实际SqliteTransaction.Commit19/787 FK/child/Sort→两Load→clock同一open写Tx、原cause引用与完整全列rollback/retry已在04通过；native的CommitFailure.Cause直接Same最终外抛cause。

本文写完后封存before-manifest；全部writer/native停止，等待主控技术处置。不改Task、产品、canonical ledger或任何历史，不弱化原before断言。
