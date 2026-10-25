# TASK-32 旧结算／失败边界调查

本报告只交付旧行为事实，不选择恢复设计、不迁移、不授新能力、不关闭 ledger 行。固定 HEAD `e878c4ede03bc46e313b3bb3fa64de845d77e160`；产品、正式测试、工具、canonical、Task01～31、Spec/Plan 全部只读。当前七个健康场（Lottery 两个 mode 分列）与后续故障统一在 `matrix-04/`，52 场由 `case-manifest.json` 具名，`checks-final-02.json` 逐场核验通过；旧诊断轮原样保留。

## 同源装配和来源

独立 net10 Release probe 引用当前 Application.Tests，显式用其原 ShopFixture/CeraShopFixture；原私人 Recipe/Epic 内容类经窄反射构造，Lottery/Vending/Rental 取实际 PVF（73,070,078B，SHA256 `d45cf58b1a2b3d16d6b35abe0d4e302e052f0b6bc43e9d3e98d815fed54aef84`），Lottery 固定合法 reward20002、Vending 原随机源票0。所有规则服务、领域、SQLite、注册 decoder、dispatcher、legacy fine、发件箱、编码适配器都在同一个原 provider。没有第二 Host/provider/outbox、产品 hook、伪 Completion、手造请求或手动 flush。

实际每个接收帧先调用原 `GameInboundFrameCounter.Observe`，再由同一 ordinal/fresh/sequence 和完整 body 构造 metadata，查原 selector/type 注册 decoder，调用原 dispatcher。不同物理替换连接有独立原 counter。输出保存 selector/type、logical、body hex、完整 raw hash/length、decoder/消息类型。序号30的相同 body 重发 ordinal2/fresh=false；新31为 ordinal3/fresh=true；再迟到30为 ordinal4/fresh=false。已实际关闭的来源只记 TransportUnavailable，不伪造还能接收。

恢复的是 GameplayQueue first production GameplayOutboxes factory，故 same-provider GameplayCloseCauses 保留；末端为原 OutboundSender 加独占受控 physical transport。`Bindings`、原 outbox slots/unit 状态、完整 adapter 帧、sender report、实际 SendAsync/CloseAsync 与 off-executor 均在 raw。状态观察、effect/领域/store wrapper 只转发原对象；scope fault 由真实 DI resolved scoped IAsyncDisposable 的 DisposeAsync 触发，未手调 Dispose 冒 scope。Dispose 使用 resolve 阶段已取得的 accessor 和仍可用 root provider，未从 disposing scoped provider 再取服务。

观察查询会在同一个真实 transaction 内提前 enlist 同一 SQLite participant，这是明确的测量影响；每个 transaction 至多一个 fallible participant。C1/C2 另加 nonfallible participant 只用于故障控制。probe 使用既有 friend 程序集名 `Dfo.Server.Networking.Tests`，它的真实 Location/MVID/SHA 是 E/probe/bin 路径，绝不冒正式 Networking.Tests；见 `matrix-04/assemblies.json`、`assembly-and-source-binding.json`。正式 focused 的实际日志 DLL 路径及依赖文件 PE 身份另在 `formal-module-identities.json`，不是注入正式 testhost 的 AppDomain 观察器。

`build-09` 与 `matrix-04` 全部源 freeze 相同：`ef32d638a547f104919459f2a72456558dfe2f1a65cbdfe4c74efb74e248d74d`。加载的全部产品依赖与正式 test DLL 目录中的 copy 逐 SHA/MVID 相同。Snapshot/Delivery 原 Task31 bytes、初次复制和后续改动说明在 `source-provenance/`、`copy-provenance-sealed.json`。完整当前 source/caller 图在 `source-current/`、`source-map-final.json`、`callers.txt`、`test-callers.txt`；不是拿旧 baseline 行号当当前源。

## 实际单元和影响边界

| logical / wire | 真实 Main | 额外既有单元 |
|---|---|---|
|65561 / 0019|CompoundItemCommand → RecipeCraft → OpeningInventory/ApplyEffects|无独立 refresh|
|65563 / 001B|LotteryOpeningCommand → Lottery → OpeningInventory/ApplyEffects|mode1 成功后 ReadPremiumSnapshotCommand，ReadOnly，在 Main catch 外|
|65751 / 00D7|VendingMachineCommand → Vending → OpeningInventory/ApplyEffects|无独立 refresh|
|66323 / 0313|EpicCraftCommand → pieces → OpeningInventory/ApplyEffects|无独立 refresh|
|66511 / 03CF|RentEquipmentCommand → EquipmentRental，stars/设备/UID/link|成功后另 ReadRentalRefreshCommand，Transactional|
|66512 / 03D0|ChargeRentPointCommand → AccountLuckyStar，gold→star CAS→事件|成功后另同一 ReadRentalRefreshCommand，Transactional|

六个 Main 均 Transactional。Rental refresh 不是 Main 的一部分，也不是名称所暗示的纯只读：handler 依次 Panel、当前角色库存、账号库存、clock，在其独立事务中运行。成功路径实证两个不同 Tx identity；mode1 premium 则是第二个 ReadOnly Tx。正常拒绝没有凭空第二 Tx。

四 opening 的共同 Source/Apply/ApplyEffects、真实 effect handler、ItemDelivery/DomainStore 都有实际观察。租赁新设备走 InventoryOnly，NoRoom 不进入 Mailbox；ItemDelivery 其它 overflow/MailOnly 才调用 Mailbox.SendPreparedAsync/RunAsync。ReplayStateDomainService 会调用 rental Panel，具名 LegacyReplay 读取沿原适配保留；Rental 的 LuckyStarChangedSubscriber/PanelRefresh 另有调用。AccountLuckyStar 还用于 ItemUseEffect、Dungeon clear 及 persistent recovery。OpeningInventory/ApplyEffects 其它现存消费者含已迁 opening、技能/升级/疲劳/附魔等；ItemDelivery 涉及任务/日补货、掉落、两塔、住房/婚礼等。逐文件命中均已保存，这些共享 caller 的存在不表示它们全部故障都在本 Task 动态跑过；只按下述原强类和52场实际覆盖计。

## 真实行为矩阵

健康七场均取得真实成功 ACK、原领域写及物理帧。Compound 消耗图纸/物理材料/账号材料；Lottery0 一件设备，Lottery1 两个独立设备及 round/total counters；Vending 消耗 coin、给4个7463；Epic pieces/两类材料/新设备；Rental 新设备后续新序号续租保持原 UID/link，clock前进2小时后绝对 deadline 从1788955200变1788962400，stars100→97→94；Charge gold5000000→4800000、stars100→102，新序号再一次合法购买。成功帧原顺序保留：Compound ACK→inventory；Lottery ACK→inventory(两条)→可选premium；Vending ACK→inventory；Epic ACK→pieces→inventory；Rent inventory→ACK→panel；Charge ACK→main snapshot→panel。

每路有真实正常拒绝。四 opening 是领域 throw/确认回滚，原 mapper 发失败；Rent invalid quote（Rejection2）和 Charge insufficient gold（Rejection4）是正常 return/空提交，ledger 为 Committed，完整资产未变。Compound SourceMode1 早门不进 fine，entry 最终 Rejected；之后相同帧重发实际被 ledger 拒绝并关闭来源，那是第二次请求的 root chain，不把它误当初次早拒绝即关闭。

| 故障位置 | 四 opening 的实际结果 | Rent / Charge 的实际结果 |
|---|---|---|
|中后段真实 SQL19/1811|原普通 catch 映拒绝，ConfirmedRollback，无来源关闭|无该普通 catch；ConfirmedRollback、无 ACK、来源实际关闭|
|真实 native Commit19/787|Compound、Lottery1 实测：body先返回，失败回滚后原 catch 映拒绝|Rent、Charge 实测：body先返回，失败回滚后原错误外抛，来源实际关闭|
|C1 durable 后 nonfallible Commit 违约|Compound代表：durable 保留，Uncertain/Blocked，无普通拒绝恢复|Rent、Charge各自同边界实测|
|C2 action SQL + nonfallible Rollback 第二因|Compound代表：完整回滚，但 typed RollbackFailure/Uncertain；两原cause可达|Rent、Charge各自同边界实测|
|Main真实fine scope在Committed后Dispose失败|Compound、Lottery0/1、Vending、Epic逐路实测：被原catch映失败ACK，ledger仍Committed，来源健康|Main已Committed、无refresh、无ACK，普通scope cause外抛并关闭来源；无新增Fence/Block|
|外层dispatcher真实scope失败|Compound成功与正常拒绝均已有原帧登记/物理发送后关闭来源|Rent、Charge成功与正常拒绝分别已有原帧发送后关闭来源|

每路 SQL 的位置是实际生产写：Compound/Vending/Epic reward item_slots（此前成本已暂存），Lottery0 reward item_slots、Lottery1 cumulative counter INSERT（reward/round已暂存），Rent rental relation INSERT（stars及新设备已暂存），Charge accounts lucky_star CAS（gold已扣）。不以 decorator throw 代原 SQL。四个 native 场是独占 fixture 的真实 DEFERRABLE FK/child trigger：FK1、child暂存、fine-body-return、原 fallible provider Commit failure、19/787、KnownStorage/FallibleCommit 同cause及完整回滚都直接记录。Rental Release 栈没有单列 SqliteTransaction.Commit，而保留 DbTransaction.CommitAsync→SqliteDatabaseParticipant.CompleteAsync:194；当前源 pending 明确为 SqliteTransaction，此调用失败才签该 storage fact。优化/async栈省略可能解释缺帧，但没有JIT诊断证明原因；`native-stack-qualification.json` 明确纠正早期诊断中“已内联”的推测，oracle不依赖此推测，不补造栈。

C1 原固定 commit cause同对象可达最终 `NonFallibleCommitContractViolation`；真实 SQLite 已 durable，不强断回滚。C2 原 SQL 对象及独立 Rollback IOException 同对象可达 typed RollbackFailure；即使全部 SQL 资产已回滚，Completion仍 Uncertain。三类出口的 C1/C2 均直接观察到 Fence、physical Block、DiscardExact 和来源实际关闭1；两个原 selected catch 可导致重复登记保护/关闭条目，不能把 close entries 数当 actual CloseAsync 数。

**真实满包不是健康ACK2。** Rent NoRoom 场为原设备带32格（9～40）实占；stars100→97暂存，领域正常返回 Rejection6/InventoryFull、RequireRollback。UOW CompletionConfirmedRollback、RollbackOnly、全资产及rowid精确恢复；但 Frozen=false、Projection=null，随后 Settled 抛 PostCommitPublicationFailure，ledgerBlocked、精确session Fenced/physicalBlocked、零帧、两个close entries（一个Closed、另一个Discarded）、actualClose1，无 refresh。该结果与 direct domain测试的正常返回不等价，是旧结算层的现状。本 Task 不修、不预选新的恢复契约。

**独立刷新故障不回滚 Main。** Lottery1 的原 premium usage ReadAsync、Rent/Charge 的原 PanelAsync 都在原 read 已 await 完成后注入固定独立 cause；这些不是 native storage fault。Main仍Committed，第二独立 ReadOnly/Tx 确认回滚；原 catch 外错误导致零ACK/原来源关闭，无新增Fence/Block。Rent/Charge 的 refreshscope 另证明第二 Tx 已Committed之后其自己的真实DI scope失败，同样不撤销 Main。没有合并事务，也没有把拒绝场补一个 refresh。

实际 fine/dispatcher scope位置均有 scope前 snapshot、ledger、Tx、outbox slots和实际frame；dispatcher scope 失败时已登记的成功/拒绝帧按原次序先发送再关闭。replacement场在实际 dispatcher scope清理时建立不同physical/session和无关玩家：只关闭原physical对象（raw保存该run真实ID），替代与无关连接/standing保活、未fence，替代后续合法新请求成功。报告不使用名称Exact代隔离证据。

健康成功和 committed正常拒绝的同帧重发只读当前投影，不二次结算、不重发原ACK；成功之后先执行新序号，再迟到重放旧序号，资产仍是新序号之后的当前值。四 opening 的 confirmed rollback 去trigger后，相同sequence复用原entry和原Ordinal1，才执行新的Main；新sequence另立entry。C1/C2、真实关闭后的scope/refresh场不假装还能在旧socket接请求，raw明确TransportUnavailable；替代连接只叫新的合法请求。

## 全资产、验证及局限

独占实际生产 user tables **110表/644列**；native另两故障表3列，共112/647。逐表保存schema、完整列、SQLite读出类型、NULL/BLOB、复合PK；唯一无PK的 item_slots 在最终轮另外读取真实rowid并同稳定排序绑定，未拿全列当身份。全部需回滚场same-run逐列及rowid零归一相等；C1和Main已提交后故障则按真实durable变化记录。`business-views-final.json`另列gold/stars、设备core/UID/link、材料/pieces、premium/counters、邮件及domain event/delivery，完整原始快照仍在每场raw。不跨run泛strip时间/GUID，不把无邮件触达当邮箱全故障验证。

原强测试完整focused **142/142**：Application八类118（Recipe15/Lottery27/Vending12/Epic14/Rental11/RentalWire3/ApplyEffects11/ItemDelivery25），Protocol四codec类23，Networking EpicCraftRegistration1。源码/参数/属性/断言未改；按实际fully-qualified过滤，原并行、5m、Skip保持，三项目本轮Release构建0警告0错误。逐display/status在 `focused-all-identities.json` 和原console detailed logs。最终probe build09 0/0，matrix04自然exit0；严格checker全部52通过。build01/02编译诊断、success01默认ImmutableArray序列化错误、checks01的错误比较假设均保留，最终结论只绑定当前driver/freeze/raw。

本轮指定 DFO_GAMEPLAY_ASSERTIONS 新路径没有生成记录；focused和probe缺少正式expected/control全量，因此**不据零记录或exit0声称新DFO74/7/八full**。Task31 9075=9055pass+原exactDimension2fail+18skip、DFO74/7、Golden15/Characterization10仅为已提交历史，未冒本 Task新跑。无产品/正式测试变更，未造测试计数增减或mutation RED/GREEN。

原 capture实际173消息/201命令，source/runtime交叉与6目标/8相关fine的当前注册保持；原工具preclose于Result前以精确Task32路径运行，valid、pending[]。本Task未被canonical completion_task引用，pending[]只是工具语义，**不是Task32交付/独立review通过**。六消息、Main、refresh/premium均pending，原七Mailbox对象、全部旧证据及其它canonical行未动。原tool/exporter及原source身份在 `tool-identities.json` / `capture-01/manifest.json`。

本报告的动态证明只覆盖52场具名入口/阶段；Vending/Epic未逐路native，四opening C1/C2只以Compound共用链代表，dispatcher以Compound/Rent/Charge代表。其他路径仅有共享源码依据，不升级为动态通过。受控nonfallible/scope/read故障不是自然生产事故；physical transport为内存控制，不是TCP/A21；未新运行真实disconnect child、游戏、性能2秒或八freshfull。现有fine中catch/NoRoom等事实供主控后续受影响门消费，本调查done不代表T004/T005或Spec完成。
