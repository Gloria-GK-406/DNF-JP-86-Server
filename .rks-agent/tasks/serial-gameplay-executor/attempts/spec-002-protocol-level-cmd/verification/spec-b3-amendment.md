# Spec B3 amendment：CollectionBox 首次成功 batch 编码登记的旧出口

## 身份、授权与范围

- 目标：[Spec B3](../spec.md)；承接 B2，子 [TaskContract](../TaskContract.md) revision 1、根 [TaskContract](../../../TaskContract.md) revision 4 不改。Spec baseline 保持 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- 主控已接受 TASK-19 的 fresh review/main consume，并完成本地检查点 `dba3ede68e15f892afd35534db9fed7ed00d04a8`；本轮不重开 TASK-19，也不修改其产品、Task、Result、review、ledger 或源证据。
- 主控明确授权本作者仅修改 `spec.md`、新建本记录及 [B2 前版快照](evidence/spec-b3-amendment/spec-b2-before.md)。快照逐字节保存：29265 bytes，SHA-256 `9e02a9471da4fb7d617371f04efe55810983fccacbaebd1bab781dc74ea69228`。
- 裁定来自根 Goal/Scope B/SC-13、父 RQ-023(c) 与子 B Goal 的保 A 行为要求：CollectionBox 搬入新 whole 后，不得把原首次成功 batch 的实际登记 encoder 故障新增为 typed 不确定性及参与者围栏。仅补第三具名旧出口，不新增 VC、不改中心方案、根/子契约或 A。

本轮是文档落稿与源码/既有测试的静态复核，没有运行 native、构建、测试或新故障探针；没有执行 git/stage/commit，没有修订 Plan P3 或路由状态，也没有创建 TASK-20。本稿不是 challenge/review 结果或 gate 通过证明。

## 冲突与真实证据层次

父 RQ-023 要求保护原样保留且在 CMD 内位置等价，不能根据新协议 CMD 的语法外壳扩大旧细粒度 CMD 的失败保护范围。反过来也不能由本出口推出一般编码或 IOException 均免围栏。原受保护提交后业务/typed 发布、MonsterDeath/祭坛的全部精确参与者范围保持。

以下路径相对本工作区仓库根；行号来自本轮静态读取，不是新运行报告。

| 来源 | 支持的事实与局限 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/CollectionBox/CollectionBoxSlice.cs:18–23`、`:50–58`；`ProtocolModule.cs:326–331` | Select 的 minimum 为 CollectionBox，Put/Withdraw 含 Inventory。fine 仍 Transactional；执行时 mandatory footprint.DemandActive，领域 Execute 后记录库存槽位。迁移不能丢这些事实或改变主事务边界。 |
| `CollectionBoxSlice.cs:103–137`；`Application/Commands/Pipeline/SelectedSessionSettlementBehavior.cs:18–25`；`Application/Effects/EffectOperationContext.cs:102–107` | handler 在 fine 前后检查 current；fine 正常返回前完成主事务及 Settled。成功 result 非 null 后才组帧、取 clock、Full 并对原精确物理 context SendBatch。原序是库存变化 → 完整 boxes 加 selected 重复投影 → ACK；拒绝另走 137 的 batch。catch 只含具名四类业务情况，不 catch 一般 SQL 故障。 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/CollectionBoxNetworkSettlementTests.cs:31–50`、`:118–145` | Select/Put 首次 FailBatch 后 Assert.ThrowsAsync<IOException>，持久结果已生效；同 fixture、同 connection 的同序号后续请求读取变化后的当前 boxes/库存，Put 不再移动物品、不发原 ACK。测试手造 request，带真实 metadata/raw body 后直接调 dispatcher，绕过 decoder；只断言异常类型，没有固定 cause 引用。Committed/无新增 fence 由后续当前 replay 间接支持，不等于已直接观察真实 ledger entry/Ordinal、围栏或 source-close。 |
| 同测试`:148–172`；`CollectionBoxSlice.cs:121–137` | 既有测试覆盖成功 Put 的库存/box/ACK 顺序，Withdraw 在该测试里是拒绝；成功 Withdraw 共用成功 batch 源码路径，但没有现成动态 encoder 注错证据，不能写成已验收。 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/NpcShopPurchaseTests.cs:575–595`；`Server/tests/Support/RecordedOutbound.cs:71–88`、`:328–352`；`Server/tests/Support/OutboundRegistrationParts.cs:88–101` | ShopSender.FailBatch 经同容器 Registering.SendBatch 的 inFlight 标记，进入真实 OutboxNetworkMessageSender 的 encoder 调用，由 FaultingEncoder 抛错。它是适配器的真实登记编码故障，非 physical transport；错误文字不改变来源。 |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs:66–85`、`:159–183`；`Server/tests/Support/RecordedOutbound.cs:423–427` | batch 先 EncodeBatch 后 Register，故该 encoder 故障时整 batch 未登记；这是源码事实，旧两个 case 没有直接断言。fixture 的 Discard.Submit 不做物理发送或 socket 关闭，后续同连接 dispatch 不证明真实关闭后仍可收包。 |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs:54–59`；`Application/Commands/CommandRegistrationAttempt.cs:7–38`；`Execution/Outbox/OutboxAdapters.cs:159–165` | 现有 enum 仅两个出口；状态检查把非 RequiredExplicit 分支一概要求 replay，token batch 又硬绑定 CommittedReplayCurrentStateBatch。首次 CollectionBox 成功 batch 不能借 replay 许可。 |
| `Server/src/Dfo.Server.Application/Transactions/CommandWorkUnits.cs:183–197`；`Application/Commands/CommandRequestContext.cs:145–162` | 新 whole 没有适用的已登记许可/真实 encoder 来源时，已提交后的同类错误按默认 typed/fence 处理，因而直接迁移会改变原 Select/Put case。默认保护本身继续有效，需要有界补许可，不能整体放宽它。 |

## B3 的最小语义

RQ-006 出口表仅增加“CollectionBox 首次成功主结算结束后的成功 batch 即时登记”：原主事务、Freeze/Settled、fine 返回及工作单元结束后，对原精确物理连接一次登记原成功 batch。它不复用 replay 许可，不授予再次主结算权，不改变库存 → 完整 boxes/selected 重复 → ACK 的原序与分段。Select/Put 有上述既有间接行为证据；Withdraw 的成功路径目前只有静态共用路径证据，后继若声称动态覆盖须提供实际证据。

raw 资格同时要求已登记具名许可和实际适配器的窄 encoder 来源证明，本次不透明登记凭据不可伪造、不可复用，且绑定 request、job、阶段、调用与精确物理目标。所有已启动工作、drain、unit/scope cleanup 完成后，最终只能剩该同一原始 cause，无 typed 不确定性及额外独立错误，才原样上抛、保持 Committed、不新增参与者围栏，并保原 source-close 顺序与后续合法同序号当前投影重放、不重复结算/原 ACK。

排除拒绝 batch、Full()/clock 等业务组帧、current/lookup/guard/Register、其他协议及一般 IOException。相同类型或同一对象由独立业务/cleanup 阶段再次抛出，不能只按引用去重而忽略该独立失败；须撤销 raw 资格、保各因/独立发生事实，沿默认 typed/aggregate 语义保护全部精确参与者。业务 catch 不可继续写入或成功结束。真实 socket 关闭后的可重发性不在承诺中。

Spec 只确定这项语义与可判定证据，不确定新 exit/API、注册或 adapter 的实现方法；由门后的 Plan P4 选最小机制。Application 不得因此识别具体 Protocol CLR 类型，也不得添加隐式当前请求、环境通道或可包任意业务的豁免。

## 改动与保持项

- 定点修改 RQ-006 的具名出口表/许可与排除边界，AC-003 的旧/新直接证据及反例，AC-004/005 交叉引用、VC“其余”的当前三出口口径，追加 B3 修订记录。
- B2 修订记录原文保留；其“两个出口”描述是当时范围，不全局替换历史。17 RQ、11 AC、VC-B1/B2/B3、其余要求及 Spec baseline 均保持。
- 根/子合同、A、Plan P3 的 commit-result/默认 sticky、Compose 禁用和 F-132 未启用边界保持。不把新增出口作为 F-132 已修证据。
- TASK-01～19 的已接受范围未使用第三出口，不因此失效；B2 两许可与默认保护继续有效，不改写旧结果或审查。主控提供的当前库存为 TASK-19 最终 8828 = 8808 pass + 2 exact old Dimension fail + 18 skip；其中三个 full 7028，其他 1800 是 TASK-15 业务库存。本记录只引用该接手输入，不声称这是 B3 新运行的八项目结果。

## 后继补证与 gate

1. 本 B3 落稿后停止写入。主控先取得 fresh 有界 adversarial challenge，再由独立 reviewer 审查匹配当前 B3；`state: ready` 仅表示语义可供审查，不表示 approved。不得复用 B2 的批准替代此 gate。
2. B3 通过后，才另行授权 Plan P4 的受影响详细方法修订，随后 fresh Plan challenge/review；不在本记录提前编写 P4、迁移接口或 TASK-20。
3. Spec/Plan 匹配 gates 通过后，由主控做 amendment-only 本地检查点，随后才规划 TASK-20 与产品。旧 Task、Result、review/证据不改，路由不伪推进。
4. 后继产品 Task 须先在真正旧 decoder 路径重跑 Select/Put，固定实际 encoder cause，再以同容器新 decoder/dispatcher/Main/SQLite/原 outbox 对应重跑；直接核对 cause 同对象、真实同 ledger entry/原 Ordinal、Committed、无新增 fence、失败 batch 未登记、原 source-close 和后续当前投影/不二结算/不原 ACK。不能把本轮静态或旧手造 request case 当作这些直接证据；Withdraw 未动态注错继续如实标明。
5. 对照同 IOException 来自业务、guard、非许可出口及原 typed 发布时仍默认保护；额外独立 scope/unit cleanup（含独立阶段再次抛同一对象）撤销 raw，保各因与全部精确参与者围栏。覆盖错误 request/job/target、阶段/调用/许可、closed/leaked/reuse、catch 后继续写或成功结束，并用可编译且有行为价值的 provenance mutation 证明真实来源保护会红。
6. 新旧窄契约须共用同容器 Registering/adapter/outboxes 与原注错/观察路径，原帧序、Required/BestEffort 及来源关闭不放宽。共享核心变更后按受影响 Plan/Task 完成完整 Release build 与八项目 full 等既有验收及独立结果审查；此处是后继方法和验证义务，没有已跑结果，也不要求尚不存在的 TASK-20 提供结果。

全过程仍限同分支本地工作；不推送、PR、合并、部署、远端 issue 或清理工作区。
