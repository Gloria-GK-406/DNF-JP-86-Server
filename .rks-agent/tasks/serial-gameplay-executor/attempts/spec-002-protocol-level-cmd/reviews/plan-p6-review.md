# Plan P6 ready R2 独立评审

```yaml
kind: rks-review
verdict: approved
```

## 精确目标、范围与独立性

本 reviewer 是 Plan 作者、R1 challenge generator 之外的 fresh independent reviewer，按 `reviewing-work/SKILL.md` 及 `references/plan.md` 的 **Spec 级、mode: task-units 高层 Plan** 标准评审。唯一工作区为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支 `task/refactor-session-20261021`，只读核验 HEAD `d3a5fec85b9ecaf633efd2157b65946928b008cf`。本批准只绑定下表当前 R2 的 HOW；不是 Task31 详细计划、产品正确性、测试通过、T-004/T-005 或 Spec002 完成的批准。

以下路径相对本子 Spec 目录：

| 制品 | bytes | SHA256 |
|---|---:|---|
| 当前 `plan.md` P6 ready R2 | 93207 | `1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691` |
| 当前 `verification/plan-p6-amendment.md` | 9487 | `f0b5f1a90ea1283653a7691dc69bbfb153c96422a1c8c43f2f928a4c0281c96c` |
| immutable `reviews/plan-p6-challenges.md`，R1 high1/low0 | 9554 | `d0d165bede921fb70a156ed945aa31f6e3f2268612841ea0e318fb53fde3f04e` |
| R1 Plan 快照 `verification/evidence/plan-p6-amendment/plan-p6-ready-r1-before.md` | 89736 | `eaba6ae255cbcb8120148644744e06b6097b2ea717e143f24c01b7a96b48e043` |
| R1 amendment 快照 `verification/evidence/plan-p6-amendment/plan-p6-amendment-r1-before.md` | 6993 | `f6920be220905ba1951a41b9a78a62f3bb04f9bab426c581206b1fca0eb96a47` |
| 原 P5 快照 `verification/evidence/plan-p6-amendment/plan-p5-before.md` | 71863 | `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| child `TaskContract.md` r1 | 8898 | `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6` |
| 权威 `spec.md` B5 | 53754 | `92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790` |
| fresh approved `reviews/spec-b5-review.md` | 9675 | `e85be4d072f30e8c3b24c1965a5e6d90902d10559c6d812e9505fb1dc1a80e66` |

已完整读取当前 Plan、child Contract、匹配 CH-1、P6 amendment，读取 B5 相关 WHAT／AC／VC、B5 review／amendment、根 Goal／Constraints／SC-8B／SC-13 和 Task30 Controller Consumption R1 完整末节；独立核对 R1→R2 差异及上述快照身份。根／child Goal 允许经审查逐条明列的有意变化，已批准 B5 是本轮 WHAT 权威；Task30 的批准只覆盖旧十四场／59 focused 调查。沿已建立 auto gate 评审，无新增 HITL。无 `.codegraph/`，按 AGENTS 跳过 CodeGraph；读取实际源码及架构哲学／重构偏好，不使用参照服务端材料冒本仓事实。

## 结论

**approved，无未解决的 blocking finding。** R1 的 CH-1 是成立的高风险规划缺口；当前 R2 已在机制责任和关键先后上作最小充分纠正。批准依据是 R2 明确的同步准入屏障、独立发生记录、真实完成来源和最终清理裁决，与当前真实 Tx／unit／outbox 生命周期相容；不是因为 CH 或 Plan 列出了未来测试。

本文件不重生 challenge，不改变 B5 WHAT、API 选择或 Plan；具体 Task31 白名单、逐命令、局部 fixture／driver 及 non-null whole 结果形状留门后收敛，符合 task-units 分辨率。高层整体路线、共享契约、依赖、验证及下一 Useful Unit 已可执行，无需预写所有未来 Task。

## CH-1 独立处置

**Disposition: confirmed（针对 R1）；当前 R2 已解决，不再阻断本 gate。**

1. **R1 真实窗口成立。** `DomainTransaction.cs:214–223` 在回滚参与者前即置 settled，`DomainTransactionScope.cs:37–38` 因而显示 Current=null。实际回滚完成后 `DomainTransaction.cs:233–249` 调 EndUnit，`:226–230` 调原 unit.Rollback；`GameplayOutbox.cs:364–368、391–395` 经 End→owner.Close，`:203–207` 清 open。原 BeginUnit／Register（`:127–142`）只守 EnsureWritable／open，`:266–270` 没有 self-settlement 屏障。此时普通登记会成为 unit=null 的存活条目；Scope.Begin（`:40–51`）也允许再次开启写事务。R1 在外层直接 await 的 observer catch 才进入 pending，确实迟于上述释放。

2. **下层 hook 是实际可达边界。** `MailboxSendDomainService.cs:56` 在直接 Send await 内还有 mailbox await；Task30 同 provider 的 `freeze-07/Observation.cs:198–208` 在 inner ValueTask 抛错后、向外 rethrow 前执行 Failed，`:191–196` 还运行真实观察。这足以形成 CH 所述“inner 已回滚、outer observer 尚未接错”的下层 catch/finally 窗口。whole delegate 本身没有 catch，并不能约束该下层。这里是源码可达性判断，没有运行新 probe，也没有把 hypothetical 已执行当事实。

3. **R2 把保护起点前移至唯一真实入口。** `plan.md:77、83` 规定本次受观察 AbortAction 刚进入、原 Tx 仍 Open 时，同步 pin 原 unit／owner，早于 settled／Current=null、任何 rollback await／参与者回调、End→Close 和下层看到完成；Close 仍可释放 open，但必须保留独立 pin。这样第二事务在 Scope.Begin 所调用的同一真实 BeginUnit 就被拒绝，普通 Register 在加入 slots 前被拒绝。屏障不依赖 outerobserver 是否已运行，因此切断了 CH-1 的两条原路径。`:77` 同时保持“真实全部 rollback／Completion 后才签 fact”，没有用预先 pin 伪造确认回滚。

4. **R2 没把新拒绝静默丢掉。** `plan.md:81、83、87` 要求下层 Register／Begin 拒绝拥有独立 guard occurrence/error，不能被随后 fact 发布、catch 或重抛旧 cause 覆盖；runner 以显式 unit 读取并合入 request sticky，observer 只消费／单次交接，不首次加锁、不解除。`:83、85、89` 只允许 runner 在全部 unit／lease cleanup、EndUnit 和健康检查后解除健康候选；有 guard／独立错误则不得 ActionRolledBack／普通 ACK 恢复。只存 unit／pending／自身 guard 的 owner barrier，与 Tx 只存操作事实、request 仍显式留在 runner 的边界一致，不要求新 request ambient／job slot／outbox。

5. **验证明确攻击原窗口，且可证伪。** `plan.md:242` 分别要求真实下层同 provider proxy 尝试普通 Register，以及 Begin→真实 SQLite write→Commit；最早 admission 拒绝、SQL／Commit 未到达、无新存活条目／第二 durable，proxy 捕获 guard 后原样重抛旧 cause，最终仍失败／保两因／来源关闭。延后 pin 至 outerobserver 或 Close 清 pin 的可编译 mutation 必须实际越过 admission 而 RED，恢复入口 pin／保 pin 后 GREEN。该测试义务足以验证已选择的机制，不以只测 whole 接到 signal 后的窗口或编译失败充数；这些均是后继执行责任，当前没有新运行结论。

## 其他机制与语义核验

- **来源、能力与恢复范围封闭。** 当前 `CommandWorkUnits.cs:108–122` 的 WriteResult 仅围真实 Commit，无法证明 action 内自回滚；`MailboxDomainService.cs:171–190` 确实在 action catch 即时回滚。P6 `plan.md:75–89` 因此选择独立 WriteSelfSettledResult 和 Returned／ActionRolledBack／CommitRolledBack，原五 consumer／默认 Write／ReadOnly 不放宽。只有真实 registration 的 SelfSettledAction、ordinary writable、ProtocolNonEffect、空 participants／None 能用；六 logical／五 CLR 唯一 whole 的 owner 判据归结构验证，Application 不硬编码邮箱。Tx fact 绑定本 Tx／call／occurrence／phase／cause／实际输出／Completion，普通 Rollback、runner 后补 Abort、旧／foreign fact 均无资格；sealed 单次交接和直接领域 await 限制可在现有显式 unit／lease 内实现。

- **完整 cleanup 与同对象第二发生没有被省略。** 当前 `CommandWorkUnits.cs:134–178` 和 `CommandRequestContext.cs:82–124` 已区分部分传播／独立 cleanup；P6 要把新来源的每次 guard／phase／occurrence接入显式收口，而非凭 Includes／ReferenceEquals消去独立发生。`CommandBus.cs:95–134` 已有 body→drain→Close→真实 scope dispose→一次 FailAsync 的末端位置；P6 保此顺序，单元结果不提前保证 CMD scope 成功。保原 cause 引用不意味着允许事后独立再次抛同对象。新实现须按计划证明这些行为；无证据支持要求在本高层 gate 先实现它们。

- **六路原边界和业务归属有真实对应。** 实读 ProtocolModule 的六 decoder／五 handler 注册、`MailboxSendWire.cs:8–45、66–69`、`MailboxWire.cs:7–37`、`MailboxCommands.cs:22–48`、`MailboxDelivery.cs:54–90`。P6 `:93–103` 将 Delivery／fine 的业务编排归唯一 whole，handler 只入队；Send／Claim Prepare 和 Send 当前 gold仍在原事务内。Open／Query 原查询事务、Query null正常提交后 ACK21、不支持 Action不进事务、无 session静默、精确 session／IsInWorld／OnlineUserId及物理身份门均有承接。恢复 ACK 前业务 current复核与机制 request/job健康分别有主责，空 selected不丢原门，也不新增 Cera actor／footprint／Main／Fence／DiscardExact。

- **共享 C2 纠正有最小源码路径。** `MailboxDomainService.cs:181–190` 原无 cause Rollback解释丢 SQL；`DomainTransaction.cs:251–280` 已能以 cause 聚合全部 Rollback错误。P6仅让原 catch把原 cause送入真实 AbortAction路径，同时保 Uncertain，具有直接可行性。`ItemDeliveryDomainService.cs:155–175` 与 `QuestHelperPuppetDomainService.cs:33–40、63–71` 的 IsSettled防护及即时自结算不删；实读 GM 多目标 prepared→mail→receipt／audit（`GmOfficialMailDomainService.cs:118–150`）、Housing harvest（`HousingOperations.cs:76–96`）、Wedding complete／refund（`WeddingSettlement.cs:48–100`）的同事务先后，与 `plan.md:248` 的原子性／共享双因验证责任一致。无观察 standalone／ambient不 pin、不得获得网络恢复资格，且 caught refusal后 Commit仍抛，与 `MailboxCoreIntegrationTests.cs:456–470、530–536` 的原强用例相符。

- **终局与来源关闭未重设计 A。** `GameplayOutbox.cs:213–240` 按原 unit状态保留条目，再在失败时追加来源 close；`GameplayOutboxes.Flush` 的原 chain记录与真实 OutboundSender close仍是实现位置。P6 `:105、244` 保 C1 durableUncertain、C2 typedRollbackFailure双因、C3 whole真实 scope默认失败／精确物理来源关闭及原 chain，不补普通失败 ACK。旧 fine返回后才登记消息（Delivery `:66、78–90`），而 whole body后 scope才 dispose（Bus `:95–119`），所以新 C3前可能已有 success／normal refusal，P6不以旧 slots=[]推零帧。正常 `OnlineUserConnectionObserver.cs:66–141` 的 disconnect／teardown保留；controlled结算的零 Discard不能冒实际关闭全链零。

## 原证据精度、验证与剩余路线

独立选字段读取 Task30 final-03，而未 dump资产全 JSON：action为 SQL19/1811／ConfirmedRollback／unitRolledBack／ACK3，claim-refusal为 GoldLimit／ConfirmedRollback／ACK10；commit在 fine-handler-return后由 KnownStorage／FallibleCommit／SQL19/787确认回滚。nonfallible为 Uncertain，rollback虽 ExactRollback=true仍为 Uncertain且原 typed图 ID3→Aggregate4→Rollback2不含SQL1；scope为 Committed且故障前 Current=null、unitCommitted、Slots=[]。C1/C2/C3旧 DispatchReturned=true、ACK3、实际 Close=0，正是 B5已批准改变的旧结果，不是 P6后继通过。

按真实 `Networking/Framing/FrameEncoders.cs:33–54` 的15B头长度／type偏移重新解析原 Transport.ActualFrames到chunk尾：single为 CMD005E16B＋2×NOTI000E119B，共3帧；multi为 CMD013B16B＋3×NOTI000E119B，共4帧。P6 `:238` 与权威勘误一致，未混 Retry／HealthyControl／LiveOnlyDrain。

已核四个删 fine受影响caller：Host两份 GM HTTP测试直接消费旧 Open／Claim结果及 UID/core／codec；App MailboxProtocolIntegration含旧selection归属拒绝（`:321–346`）、Claim及帧序；ItemDelivery含 prepared实例属性与类型扩展（`:154–182、239–284`）。P6 `:246` 明确保方法／参数／强断言、同provider真实driver和原HTTP业务；wire无法表达的旧领域强输入可用test-only真实领域＋原事务接缝，不造wire字段、不换provider／SQL预置求绿、不保legacy转发。高层未预写局部driver不构成缺陷。

P6 `:238–252` 覆盖六路真实 decoder→dispatcher→whole→Domain／SQLite→同adapter/outbox→production sender的 before/after、native787与self区分、真实来源/owner/cleanup反例、C组actualClose／chain／replacement物理隔离、共享原子性与LiveOnly先后、全部110表／列含NULL/BLOB同run原样回滚、完整NUL nonowned／精确owned／canonical七实际ledger行与数量账。共享core改变要求新鲜Release --no-incremental构建0/0和八项目full；9029=9009pass＋原两条exactDimensionfail＋18skip仅历史输入，DFO九expected类67＋七actualcontrols7=74、Golden15／Char10、原五consumer及三个raw完整owner／source／cleanup回归均保留，不改并行／5m／Skip。未把拟验项目、旧59focused或本次只读检查计为新full通过。

整体 `T-001～012` 路线与17RQ／11AC覆盖保留；T-004仍有8消息／7fine残余，T-005仍有Rental两个Main及独立Tx refresh；拍卖仅outboundB7而无入站，不制造业务。Mailbox是下一Useful Unit，机制／六consumer／删旧编排／保护验证同交，后簇无SelfSettledAction预授权。T-006～012、最终八freshfull／2秒CMD测量、Astra／用户收尾及不push／PR等门不变。当前评审没有替这些未做项关账。

## 交接与停止

本次只有只读源码／Markdown／原JSON／hash／diff检查和创建本文件，无build、testhost、probe、native、产品或测试修复；未修改Plan／amendment／CH／Spec／Contract／Task30／A／routing／ledger，未创建Task31，无git mutation。无新的missing prerequisite或需要返上游的blocking finding。Plan gate可由主控消费本匹配批准；仍须主控完成amendment-only本地检查点，再详细规划Task31，后续实现与result review按既有流程独立验证。

Reviewer完成后停止writer；本批准不越过上述门，不宣告整体完成。
