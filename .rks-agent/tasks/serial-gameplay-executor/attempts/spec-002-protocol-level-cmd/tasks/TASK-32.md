# TASK-32 — 剩余四 opening 与租赁两路的旧结算／失败边界取证

## Task Unit Contract

- Revision: 1（本契约与详细计划共同版本）。
- Outcome and references: 在 TASK-31 已提交的当前源上，为 T-004 的 CompoundItem／LotteryOpening／VendingMachine／EpicCraft 与 T-005 的 RentEquipment／ChargeRentPoint 建立可复核的旧 decoder→dispatcher→legacy fine／Main→真实领域／SQLite→原发件箱／发送边界证据。区别正常返回拒绝、RequireRollback、action 抛错、真实 fallible Commit、已提交后的 fine scope、独立刷新及 dispatcher scope 失败；给主控后续受影响 gate 提供事实，不选择新恢复 WHAT／HOW、不迁移、不关闭行。对应 B5 RQ-001/003～009/013～015、AC-001～005/008～009；本单元完成不代表 T-004、T-005 或 Spec B 完成。
- Owned boundary: 本 Task 的执行追加；全新 `verification/evidence/task-32-inspection/` 下的专用探针、脚本、快照、报告与原始证据；全新 `verification/logs/task-32-*`。除此之外的 tracked 文件全部只读，尤其 Server 产品／正式测试／Support／csproj、两个 canonical 文件及其中七个已闭 Mailbox 对象、其余 ledger 行、既有工具、TASK-01～31 与旧 R1/R2/R3 证据、Contract／Spec／Plan／routing、A、协议知识库。新 evidence 不覆盖已有同名输出。
- Required checks: §3～7 的真实入口与成功控制、六路正常拒绝和 action 资产边界、按真实共享边界选取的 native Commit／C1／C2／scope／刷新区分、全部实际生产表逐列回滚比较、精确账本／帧／来源关闭／替换隔离、原强用例 focused、probe Release build 0 警告 0 错误、原 capture／ledger 检查及 Result 自身 postwrite、完整 Git NUL nonowned 身份与精准 manifest／行尾／四类 whitespace／native 自然退出。Task31 的 9075 库存仅为已接受历史输入，不冒本轮 full。
- Dependencies: [TASK-31](TASK-31.md) revision 1、Result R3、fresh Independent Quick Review R3 approved、Controller Consumption R3；已 committed done 于 `e878c4ede03bc46e313b3bb3fa64de845d77e160`（parent `db4e17c4ad4a2ee6fb69658bb3e890fbfca8ec1c`）。完整 Task 85124B／SHA256 `309a1571ef61254f828871376b1d1621ec2e50bc77022c5925b6368a9640b264`。递归依赖不重复泛列，旧探针仅作只读方法来源。
- Result review: required — 后续迁移与受影响行为裁定将消费本证据；漏掉 Main 已提交、scope 位置或刷新事务会使错误恢复结论被后续实现放大，成功控制和静态 catch 本身不能排除。须 producer 之外 fresh independent result quick review，由主控完整消费后才本地检查点；不另造 Task-plan reviewer。
- Task-specific authorization: 仅父授权内的有界调查和证据。不得改产品／正式测试／共享契约或 canonical，不授本簇 SelfSettledAction／新 WriteResult／raw 资格，不造新 VC、whole CMD、恢复方案或 TASK-33。producer 不自审、不 stage/commit；实际结果及自身 postwrite 封存后停止 writer/native，交主控。不 push/PR/merge/远端 issue/version/tag/release/deploy/运行 `df_game_r`／`DNF.exe`／清理其它工作区、服务或数据库。

## Detailed Plan

### 1. 唯一起点与调查边界

每次工具和 native 调用显式 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`；开始核 HEAD 为上述 e878、index 空、除本新 Task 外没有未解释改动。B=`.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E=`B/verification/evidence/task-32-inspection`。Task 32 是全新稳定 ID；执行不得改原计划前缀。

已核治理输入：child Contract r1 8898B／`32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；Spec B5 53754B／`92faf293e52a7a09cc7a29641893ef5dd0cba82ab72bf32d9cb3e3db41b5a790`；Plan P6 ready R2 93207B／`1ec1dd1fcaa842e033e8b773fc5ed6fd0563199e8cbe3752fd68ac1e0a8c5691`。读 Plan §4 共享单元契约、T-004/T-005 和对应 Verification，Spec 请求状态／结算许可及默认保护、RQ/AC，Task31 Result R3／独立 R3 review／Controller Consumption R3。身份漂移或授权冲突先返主控，不改上游来适配调查。当前无 `.codegraph/`，因此用 `rg` 定位实际本仓代码；不以 ServerS4A21 或旧单体作为本仓证据。

Task31 实际 self 能力只授 Mailbox 一个产品 owner／六 logical；现存 WriteResult 实际 10 个 owner，P6 具名四 opening＋Cera 五个只是子集，计数勘误已独立接受。完整既有集合保持不变，不能据此给这里的四个**残余** opening 或租赁授新资格。保 Compose 禁用／F-132、三个 raw 出口及默认保护。T-004 的 Rename／Hatch／SharedEffect／GenCeraTicket 等其它残余不在本 Task；拍卖当前仅 outbound B7、无入站实现，不凭包名造市场入口。

### 2. 当前真实路线与证据索引

先在 E 保存逐行 source/registration/caller map，含当前文件 SHA、行号、原 canonical ID／state、实际消费者、真实事务与事件／投影点；保 original baseline 只读，不拿 baseline 行号当当前行号。目标六 logical 目前全 pending：

| logical／wire selector=1 | 当前切片及 Main fine | 原正常／失败出口与额外单元 |
|---|---|---|
| `message:65561` CompoundItem／0019 | `Protocol/Game/CompoundItem/CompoundItemSlice.cs`；`CompoundItemCommand`→`IRecipeCraftDomainService.CraftAsync` | decoder 7/9B；handler 参数门；fine 外窄 `OpeningRejectedException` 与普通非取消／非 uncertain catch；失败 ACK 由原 mapper 决定，成功 `OpeningCompleted` |
| `message:65563` LotteryOpening／001B | `Protocol/Game/LotteryOpening/LotteryOpeningSlice.cs`；`LotteryOpeningCommand`→`ILotteryOpeningDomainService.ExecuteAsync` | 4B、mode0/1；Delivery catch 只覆盖 Main；mode1 成功后另 `ReadPremiumSnapshotCommand`（ReadOnly），在 catch **之外**，随后原投影 |
| `message:65751` VendingMachine／00D7 | `Protocol/Game/VendingMachine/VendingMachineSlice.cs`；`VendingMachineCommand`→`IVendingMachineDomainService.UseAsync` | 10B；输入判定及 Main 在原 try，narrow／broad catch；成功 native ACK 在绝对库存之前 |
| `message:66323` EpicCraft／0313 | `Protocol/Game/EpicCraft/EpicCraftSlice.cs`；`EpicCraftCommand`→`IEpicCraftDomainService.CraftAsync` | 4B；先扣 account pieces，再 ApplyEffects；fine 外 narrow／broad catch，成功 ACK→pieces→库存与清槽 |
| `message:66511` RentEquipment／03CF | `Protocol/Game/Rental/RentalSlice.cs`；`RentEquipmentCommand`→`IEquipmentRentalDomainService.RentAsync` | 22B、实际 item/context 在 offset14/18；effect Main；拒绝通常直接回包，成功另 `ReadRentalRefreshCommand`（Transactional）及原帧／外观投影 |
| `message:66512` ChargeRentPoint／03D0 | 同 RentalSlice；`ChargeRentPointCommand`→`IAccountLuckyStarDomainService.PurchaseAsync` | 22B、mode/count 在 offset14/18；effect Main；成功另同一个 Transactional refresh，ACK→main snapshot→rental panel；失败不当然进入 refresh |

以上六个 Main fine 均按 `ProtocolModule` 当前 Transactional 注册；`ReadRentalRefreshCommand` 是第七个相关 fine，而非租赁 Main 的一部分。mode1 premium 读取是另一个既有 ReadOnly consumer，也有日切／usage report caller。核实际 handler／decoder／encoder／notification 注册、message marker／MinimumReplayNeeds、command marker、pipeline Order 和当前调用图；wire type 与 logical ID 分列，不能混用。

要读并串起的实际链：`Commands/CommandBus.cs` 的 legacy InlineAsync／真实 scope；`Dispatching/NetworkMessageDispatcher.cs` 的 legacy executor job／外层 scope／授权／EffectOperationLedger；`Pipeline/UnitOfWorkBehavior`、`SelectedSessionExecutionBehavior`、`SelectedSessionSettlementBehavior`；`Effects/EffectOperationContext`／Ledger／ReplayStateDomainService、SelectedSessionSafety。特别保 `TransactionStarted→Freeze→Commit→Settled` 与 `RollbackConfirmed→Settled` 的不同可达路径：Rental NoRoom 正常 `RequireRollback` 后，原 Fine Main 是否仍因无 Frozen/Projection 而失败、是否健康 ACK，必须实跑，不能由领域测试推断；不预写 bug 或默认结果。

四 opening 的共享链为各领域服务→`OpeningInventoryDomainService.SourceAsync/ApplyAsync`→`ApplyEffectsDomainService`→实际 effect handler／ItemDelivery／各 DomainStore facet→同数据库 SQLite participant。`OpeningInventory.ApplyAsync` 还执行 decided writes 和当前库存读取；Epic book、Lottery mode1 的 gold／premium quota/counters 等独立状态也要圈入。租赁链为 `EquipmentRentalDomainService` 的 starStore／rules／character／ItemDelivery(InventoryOnly)／rental links／itemStore；Charge 为 `AccountLuckyStarDomainService` 的 gold→star CAS→`LuckyStarChanged` 事件；refresh 还读 Panel／character inventory／account inventory／clock，不凭名称假定纯读无写。

盘点上述所有 fine 的生产／测试 caller，以及共享 `IEquipmentRentalDomainService`、`IAccountLuckyStarDomainService`、OpeningInventory／ApplyEffects、`ItemDelivery` 的真实非协议消费者；包括 replay／具名 LegacyReplay 适配、LuckyStarPanelRefresh／ChangedSubscriber、ItemUseEffect／Dungeon clear／persistent recovery 等实际命中。用引用和实际方法说明影响，不把接口命中都算本次动态测过。`ItemDelivery` 可能经 overflow／MailOnly 进入 Mailbox，Task31 已改共享 RunAsync；本轮以 e878 当前真实链核取，区分 InventoryOnly 不走 mail 的条件。只做 caller/边界事实，不迁移或删适配。

### 3. 独立 probe、真实装配和观察

在 E 建独立 net10 Release 项目，优先 ProjectReference 当前 Application.Tests 并复用 `NpcShopPurchaseTests.cs` 内 `ShopFixture`、`CeraShopPurchaseTests.cs` 内 `CeraShopFixture` 及各目标原测试内容／PVF 准备；允许窄反射构造 internal fixture 或使用已有 friend 身份，但记录 E probe 的真实 Location／MVID／SHA，不能冒正式测试程序集。不得改 friend 清单、可见性、产品 hooks、项目引用图或整个测试树。新 probe/观察器可从 Task31 `probe/{Delivery,Observation,Snapshot,Program}.cs`／R3 相关纯脚本复制**必要部分**；保存来源路径／完整 bytes／SHA、复制后 SHA、修改理由。旧 probe／freeze／raw 不改，不因当前 fine 不同就重写旧 driver 并称旧 oracle 新通过。

fixture configure 的最后阶段核现有 `RecordedOutbound.AddTo` 的真实 descriptors：其最后 `GameplayOutboxes(sender,logger)` 工厂缺 same-provider `GameplayCloseCauses`。保存并恢复原 `GameplayQueue.AddGameplayExecution` 的 **first production descriptor factory**，由同 provider 创建原 outboxes／causes；保实际注册 adapter、production encoders、原 OutboundSender，末端仅换独占受控 physical IOutboundTransport。不得新建第二 provider／outbox、手工补 Record 链、提前 flush、私改 slots／Completion 或用 recorder.Discard 代物理关闭。既有可转发 recorder/decorator 只观察真实调用，输出绑定到同 provider 的 bus／dispatcher／safety／store／outbox／sender 身份。

实际 wire 请求必须：真实 `GameInboundFrameCounter.Observe(sequence)`→对应 metadata 的 `InboundOrdinal/SequenceIsFresh`→`WithRawBody(原完整body)`→从同 provider 已注册 selector/type 解出消息→同 provider `INetworkMessageDispatcher.DispatchAsync`。可复用只读 `Inventory/EffectRequestTransport.cs` 的序列化方法，Rental 新 body 独立按当前 decoder 生成；不得手造 request／直接 handler/fine 冒 decoder 证明。metadata 与 body／decoder 输出同源。设置通过原 fixture TestGameplay／事务，实际 dispatcher 从执行器外 await，避免在 TestGameplay 作业内自等。

每场新独占 fixture，保存合法 source slot／商品／recipe／规则内容来源、原 session／online authority／physical reference；先一场健康 wire success 证明 driver，然后扩矩阵。单元观察用可转发的原领域接口／原 scoped invoker 或 pipeline decorator，调用原 factory／实现、不重写业务。抓取 domain-enter／中后段暂存／domain-return或throw／fine-body-return／unit结束／真实 scope dispose／handler登记／job终局。internal 状态只读，exception 用同引用 object ID 图保存 type、native codes、stack、Inner／Aggregate、独立 cause/occurrence；ILogger 也保真实引用，不能补造异常图。

逐阶段记录 Tx 原对象／Completion／IsSettled／RollbackOnly／Current／GuardFailure／CommitFailure／participants 身份与 CommitCanFail；实际 MainExecutionId、TransactionStarted、Frozen、Projection、RollbackConfirmed、ledger entry 原对象／State／Ordinal／LatestOrdinal／payload hash、footprint slots/areas、outbox slots及每 unit state／登记顺序。观察查询若提前 enlist 同一个 SQLite participant须披露；不得创建第二 fallible participant或跨 provider读写。probe 的普通 harness 修正只能发生在 E，保诊断和新 freeze。

### 4. 按真实差异取样的矩阵与 oracle

先把下列场景、注错点、成功控制、实际共享依据写入 E 的 case manifest；共享场景可以复用 driver／观察器，fixture 不共用。场数按真实边界确定，不做六消息×所有 fault 的无意义笛卡尔积；每个未动态覆盖的协议／阶段在报告明确保留。

1. **六路健康及正常拒绝。** 每路至少一场最小合法成功及一场原可达正常拒绝；Lottery 同时有 mode0 和 mode1 真成功。拒绝须分早门未进 fine、领域正常返回（如 Rental invalid quote／Charge insufficient gold）、领域 throw（opening missing material）、Rental InventoryFull 正常 RequireRollback；不是每种都当同一事务形状。Rent 要有新设备与续租原 UID／绝对 deadline，Charge 保 gold/star 变化和 ACK 次序，InventoryFull 以真实满带触发，不能 decorator 直接返回 NoRoom。记录是否进 refresh、Main/Tx/ledger/帧／Fence/Block/Discard/actual close 的实际结果；不给预计 ACK 当硬编码期望。
2. **每路关键真实 action SQL。** 从原 strong tests 的 trigger 点选择中后段写：Compound 成本之后的 reward/item_slots；Lottery mode1 已扣 pot/gold／quota 或 round/total 后的奖励／后续 counter；Vending 已扣 coin 后的奖励；Epic pieces／多项材料后 item_slots；Rent 已 spend stars／新 item 后 rental relation；Charge 已 debit gold 后 star CAS。按实际 SQL／store 顺序证明暂存写存在，触发 native 19/1811（或真实可达 native code）、原 cause、完整回滚和协议最终出口。不可用 decorator plain throw 或 preflight 拒绝代实际 SQL。保失败前业务值及全部生产表回滚，去 trigger 后区分**同帧同 sequence 重试**、**新 sequence 合法下一请求**。
3. **真正 native fallible Commit。** 沿已验证的 SQLite deferred FK 方法，仅在 fixture DB安装 DEFERRABLE 外键和作用于真实 action 的触发器；INSERT 成功、action／fine body 已返回，真实 `SqliteTransaction.Commit` 才报 19/787。至少覆盖共用四 opening 主链一例、Lottery mode1 若其额外事实与前例不同则独立一例、Rent 和 Charge 各自真实主写；其余仅有源码共享推论，不冒逐协议动态通过。核实际 ForeignKeys=1、child 暂存、原 stack／KnownStorage/FallibleCommit 同 cause、Completion 及全资产/ledger/ACK/close。ordinary ABORT、controlled Prepare 或 fake fallible participant 不算 native Commit。
4. **C1／C2 默认失败。** 至少对四 opening catch 共用链、Rent、Charge 三种实际出口分别选代表：C1 在真实 SQLite durable commit 后由已 enlist 的受控 `CommitCanFail=false` participant 在原 Commit 抛固定 cause，证明 durable／Uncertain及原保护；C2 在第2项真实 SQL cause 同时令一个 nonfallible participant 的原 Rollback 抛第二固定 cause，保两因同引用／typed reason／Completion／ledger和默认保护。说明参与者只为 fault control，不能称自然事故或 native Commit。四 opening 的普通 catch 过滤 uncertain，租赁原无同类普通 catch；按实际图判断，不套 Mailbox self 结果，不清失败求 ACK。真实旧异常图如丢因如实记；不能改产品补图。
5. **scope 的位置与已提交事实。** 四 opening 各测真实 legacy fine scope 在 Commit 后 DisposeAsync 抛普通 cause（可通过实际 resolved scoped wrapper／dependency，在原 scope 跟踪并 Dispose）；观察原 broad catch 是否映射拒绝，同时 ledger 是否已 Committed、来源是否仍关闭／参与者是否受保护。不能从 catch 语法判无 fence/close。Rent／Charge 分清 Main fine scope 与后继 refresh fine scope；再以代表四 opening 和租赁成功/正常拒绝控制，测试外层 legacy dispatcher 的实际 scope 在原 handler结束、已有帧登记后 dispose 失败。scope 必须由实际 DI resolve/dispose 触发，不能手调自建 disposable、在 body末尾直接 throw，或把两 scope混成一个。当前没有新协议 whole，不造未来 whole scope当本轮证据；报告将当前 fine vs dispatcher job/scope的事实留给后续 whole 对应。
6. **Main 已提交后独立刷新失败。** Lottery mode1 在已提交后，真实 `ReadPremiumSnapshotCommand` 的 ReadOnly 边界注入独立可追踪 fault，证明其在原 catch 之外，记录原持久写、ledgerCommitted、此前有无已登记原 ACK／投影及最终 close。Rent／Charge 成功后分别在 `ReadRentalRefreshCommand` 的独立 Tx／真实读取中注入可达 fault，不能合并到 Main，也不能从拒绝场臆造第二 Tx。记录当前 inventory/account读取引发的实际写／callback、scope及事件；若两调用共用同refresh fault可共享其故障方法，但保两个原协议次序控制。
7. **replay／下一请求和精确保护。** 健康成功、确认回滚、正常拒绝、C1/C2、fine scope和刷新失败的代表场保同 physical connection 上原 sequence／完整 raw body 重发、ordinal/fresh变化、随后新 sequence合法请求。核是否复用同 entry／原 Ordinal、是否只读当前投影／无第二结算／无第二原 ACK；必要在合法后续事务改当前资产再重放，区别当前投影与旧 receipt。真实已关闭或 blocked连接不能假装仍可执行：记录实际拒绝／静默／transport unavailable；另一个新 fixture或替换连接只称新请求控制。至少对实际产生保护／来源关闭的一场真替换 session/physical，直接观测旧 exact recipient与替代／无关连接，不以名字“Exact”当隔离证明。

每个 fault 必须有未注错成功控制、实际触发标记／阶段／原 cause，且实际目标路径保真。若只读边界内无法注入某 fault、无法取得必需暂存观察或真实 scope，保存最小证据和原因／未证范围返主控，不改源码 hook、私有提交状态或暗扩权限；不给缺口写通过。观察结论可以与旧测试名／静态预期不同，先诊断并报告，不预选改法。

### 5. 全资产、帧与来源关闭的比较口径

独占 fixture 的所有实际 production user tables由 sqlite schema 实际枚举，保存 schema／完整列／稳定 row identity（复合 PK 或经证明的 rowid）／SQLite type／NULL／BLOB bytes；不盲抄 Task31 的 110表等数量。安装注错 DDL后再取本场 before，另列临时 fault tables。before、关键暂存、action结束、fine结束、scope前、协议终局及 retry/下一请求各有 phase原始快照。同 run 的全部回滚按完整列原样相等，不 strip 时间／GUID／ID，不用几列 count冒全资产。

另给业务视图：原设备 UID/core／expire／rental link，drawing／coin／pot／各材料 stack，account virtual balance／gold／Cera／stars，Epic pieces，premium权益／quota/counters，mail/message/attachment/claimed及 outbox事件／callbacks；实际不存在或未触的状态明确。C1等 durable 场按实际 intended mutation比较，不强断回滚；refresh失败不撤销已提交 Main。跨 run 若必须比较，只沿现有具名时间／GUID **真实来源与关系**逐项核实：run界／同case稳定／类型／唯一映射／外键关系；没有根据的列不能归一。same-run rollback始终零归一。

原 adapter 编码的完整帧 bytes、CMD/NOTI selector/type／长度／顺序、batch和unit、实际提交 handoff、production OutboundSender report、physical SendAsync／CloseAsync及 off-executor事实全部保存；登记失败与物理发送失败分列。直接查询精确 session Standing/Fence、对应 physical IsBlocked、DiscardExact目标／调用、close entries／actual CloseAsync／same-provider close cause chain。既要有无新增保护的健康控制，也要有默认保护的实际失败，不把“无普通失败 ACK”写成零帧，不提前 flush／撤条目／补失败 ACK／伪造 chain。受控内存 transport不是TCP／A21，未运行真实 disconnect child须明示。

### 6. focused 原强用例、命令与证据身份

源和正式测试只读，故本调查不默认重复八 full／最终性能／完整 DFO。开始冻结原目标测试方法、参数、属性及强断言来源；重点保留并实际 focused运行：

- Application：`RecipeCraftTests`（account+physical成本、No_room、storage failure、late replay），`LotteryOpeningTests`（mode0/1、双奖励、round/total quota及后段失败），`VendingMachineTests`（coin/reward、storage retry、duplicate），`EpicCraftTests`（pieces+materials+equipment、真实提交失败、stale、帧与replay），`LuckyStarRentalTests`（NoRoom／relation故障、UID续租／期限／reopen、star原子性），`LuckyStarRentalWireTests`（purchase/rental重发、共享consumable原子性），`ApplyEffectsTests`、`ItemDeliveryTests`（实际共享 effects/overflow／原设备与整批回滚）。完整这些类，不以几个选中方法替代其参数化用例；每类原结果与新增 probe分列。
- Protocol：现有 `Game/CompoundItem/CompoundItemCodecTests.cs`、`Game/PackageOpening/LotteryOpeningCodecTests.cs`、`VendingMachineCodecTests.cs` 及 `RentalCodecTests.cs` 的真实类名／方法；Networking：`Integration/EpicCraftRegistrationTests.cs`。先从当前源码核 fully-qualified 类/filter，再执行，不猜不存在的 Epic codec类。若共享 caller调查暴露额外直接相关 strongcase，列精确来源／理由再增相应 focused；不泛跑所有无关 suite。

命令模板（展开为真实绝对路径／原 argv；shell不把 B/E缩写当已存在环境变量）：

```text
dotnet build <E>/probe/Task32.BoundaryProbe.csproj -c Release --no-incremental -m:1
dotnet run --project <E>/probe/Task32.BoundaryProbe.csproj -c Release --no-build -- <case-manifest> <new-round-output>
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --filter <上述八类的实际FullyQualifiedName或条件> --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --filter <上述四类实际条件> --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --filter <EpicCraftRegistrationTests实际条件> --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

正式 focused `--no-build` 前确保对应当前 test assembly已由本轮 Release构建；必要对该 csproj单独 `dotnet build -c Release --no-incremental -m:1`，不能使用未知旧DLL。native走本机 FSEvents可用的 require_escalated环境，保原类并行／5分钟deadline／Skip，不调大期限、改测试或吞失败。意外失败先按 systematic-debugging 保原raw与源身份；不得混入两个历史Dimension基线。构建生成物属于既有 build流程，不能修改原源／项目／正式测试获取成功。

每轮源freeze先于build/run，含 HEAD／tracked source bytes／driver bytes、真实加载程序集 Location／MVID／SHA及依赖copy、dotnet/SQLite/PVF版本／归档身份、argv/cwd、UTC start/end／自然exit／环境／console detailed原log。probe build须0 warning/0 error；run exit0仅代表该 manifest/checker跑完，不能冒所有吞错或full proof。driver修正后用新round＋新freeze，旧raw保留，不把前轮结论绑定到新DLL。

DFO_GAMEPLAY_ASSERTIONS用本轮新路径存实际观测；只按本轮真实case/control解释。Task31 八fresh full **9075=9055pass+原exactDimension2fail+18skip**、原DFO74/7、Golden15／Characterization10作为已接受历史输入注明来源／冻结身份；本轮 focused／probe零record、exe0或缺controls不能称新八full／74/7／Golden/Char通过。产品/正式测试未改，完整tracked身份负责不漂移，不为调查伪造mutation RED／GREEN。

### 7. 原工具、完整性、结果与停止

用原 `B/verification/probe-tools/migration-ledger.py`，不改工具／expected／baseline／ledger。E保存本轮原registration capture（带实际 tool/exporter SHA及source/runtime crosscheck）；它是取当前六路／fine注册和集合不漂移证据，不负责行为等价。命令为：

```text
python3 .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/verification/probe-tools/migration-ledger.py capture --repo /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021 --output <E>/capture-01
python3 .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/verification/probe-tools/migration-ledger.py preclose --current-task .rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd/tasks/TASK-32.md --output <E>/ledger-preclose.json
```

工具当前只要求 closed行的completion_task文件含结果／done字面，不识review／round／commit。本调查不被任何 canonical completion_task引用，原六目标与refresh仍pending，Task31七对象不改；故 Result之前preclose／strict可能已 `completion_results_pending=[]`，这是原工具的预期，不是TASK-32已交付／已审。不得照 Task31硬写只含本Task的pending，也不得假闭行求通过。preclose只传上述精确existing Task路径，不传裸ID；本轮不运行 initialize。

开始保存完整 `git ls-files -z` 路径＋HEAD blob/current bytes/SHA、index、当前 untracked NUL及本Task精确计划前缀；原tracked全部 nonowned。末尾保存同全集 current/before/HEAD逐path完整比较，不漏中文／旧证据／上游／工具／Golden；Task31完整SHA和旧R1/R2/R3资料保持。实际 tracked changed／untracked 新增 NUL精准等本Task＋E＋task-32日志前缀的 manifest，不用目录前缀粗略断言代逐文件归属；保任一原untracked不受扰。manifest列每路径bytes/SHA／来源类型，排除自身hash，Task只绑定原精确pre-Result prefix避免循环，完整最终Task身份停止writer后外报。

新authored脚本／项目／报告／Task逐项检查 `blank-at-eol`、`blank-at-eof`、`space-before-tab`、`cr-at-eol`，保存行尾/BOM；copy/raw按真实bytes保留provenance，不用CRLF例外放过所有trailing。确认本Task native进程全部自然结束、无本probe/testhost残留；不清理其它进程／服务／库。

证据齐后只在本Task追加真实执行结果：改动／为什么／实际检查与数量、每协议真实单元和caller、action vs commit／正常返回 vs RequireRollback、已提交 fine／外层 scope／刷新事实、完整资产／账本／帧／来源关闭、可动态证明／仅静态共享／未测与需主控裁定事项。done仅表示此有界调查执行完成待required review，不能表述六路迁移或粗包完成；有实质缺口则按实际范围返主控，不预填假结果或自选新恢复能力。

结果实际写入后，再真实运行原 `validate --output <E>/ledger-final-strict.json` 与 `selftest --output <E>/ledger-eight-bad.json`，作为**结果自身 postwrite**完整性检查；八类坏数据全部由原工具拒绝，expected不改。首次结果不得预记这两项通过；实际运行后补录时间／argv／exit和工具局限，再核最终Result／Task／manifest身份。检查失败保raw并如实修正交付，不伪造review或commit、不假闭pending行。停止全部writer/native，交主控 readiness后指定的fresh independent result reviewer，主控消费再决定本地checkpoint及下一有用单元；producer不规划后继 Task。

## Result R1 — TASK-32/r1 有界旧行为调查

Outcome: done

本轮只完成旧四残余opening及租赁两路的证据调查，待fresh required independent Result review与主控消费，不是生产迁移、T004/T005或Spec完成；producer未自审、未stage/commit/remote/version/tag/deploy/游戏/清理。原29144B计划前缀 SHA256 `180650f92a2783250bf8e5f01f8b5305a223497aa6f6fea13598fc6184827a31` 精确不变，全部tracked产品/正式测试/上游/canonical/旧Task与证据只读，无WHAT/HOW、恢复来源、SelfSettled/WriteResult/raw、VC或ledger闭行变化。

Changed：仅本Task追加、新 `verification/evidence/task-32-inspection/` 与 `verification/logs/task-32-*`。完整交付以E的 `report.md`、`case-manifest.json`、`matrix-04/`、`checks-final-02.json`、source/caller/provenance/asset/assembly资料及原native日志为准。最终probe build09 Release --no-incremental 0warning/0error；matrix04先跑七健康控制，再完成全部52具名场，自然exit0；最终sourcefreeze SHA256 `ef32d638a547f104919459f2a72456558dfe2f1a65cbdfe4c74efb74e248d74d` 与build09完全相同。

同provider真实原counter Observe→metadata/full raw hash→registereddecoder→dispatcher→legacy fine/Main→原domain/SQLite/effects→原adapter/first production GameplayOutboxes(factory含sameproviderCloseCauses)→原OutboundSender/controlled physical transport全链取证。未手造请求/完成状态、无第二provider/outbox/提前flush。四opening共享OriginalOpeningInventory/ApplyEffects、六Main及独立RentalRefresh Tx/双倍Lottery Premium ReadOnly的源、生产/测试caller和实际事务位置均已分别保存；其余共享caller只按源码/原强类范围说明，不冒全部动态覆盖。

Checks与实际发现：

- 七健康wire控制（Lottery0/1分列）全部成功、原ACK/投影帧序与实际物理发送相符。Rent新设备及新序号续租同UID/link，绝对deadline随clock前进2小时从1788955200到1788962400；stars100→97→94。Charge真实gold5000000→4800000、stars100→102。六路正常拒绝、Compound早门，以及每路中后段真实SQL19/1811均有独占fixture和暂存观察。
- 四opening原普通catch把健康确认回滚的SQL失败映原拒绝，无来源关闭；Rent/Charge原无该catch，同类ConfirmedRollback仍错误外抛、零ACK及实际sourceclose。Native四场为Compound、Lottery1、Rent、Charge：真实FK1/DEFERRABLE child暂存/原bodyreturn→真实SqliteDatabaseParticipant pending.CommitAsync→原19/787/同cause KnownStorage-FallibleCommit→全回滚。Rental观察栈保DbTransaction.CommitAsync→CompleteAsync:194，未单列SqliteTransaction.Commit；结合pending明确SqliteTransaction的当前源核来源，不伪造栈，不把尚未证明的内联原因当oracle（见native-stack-qualification）。
- C1选Compound共享opening出口、Rent、Charge各实测SQLite durable之后controlled nonfallible违约，typed NonFallibleCommitContractViolation/Uncertain/ledgerBlocked，durable资产保留；C2各是真SQL1811＋独立controlledRollback第二因，两原对象都可达最终typedRollbackFailure，全部资产已回滚仍CompletionUncertain。三类出口的Fence/physicalBlock/DiscardExact/原sourceClose及原sameproviderchain直接记录，不按快照改事实、不新增普通失败ACK。
- 四opening逐路真实Main fine DI scope在Committed后Dispose失败，被原catch映失败ACK而ledger仍Committed/source健康；Rent/Charge Main fine scope失败则无refresh/ACK、source关闭，未新增Fence/Block。真实dispatcher scope在Compound/Rent/Charge成功及正常拒绝的原帧已登记后失败，原帧先物理发送再关闭；实际不同physical/session替换及无关玩家保活，替代的合法新请求成功，只关旧source。没有以scope/body手抛或手动Dispose冒证据。
- NoRoom实wire与directdomain不同：真实32格满装备带，stars100→97暂存；domain正常返回InventoryFull/Rejection6并RequireRollback，UOW ConfirmedRollback/全资产恢复，但Frozen=false/Projectionnull后Settled抛PostCommitPublicationFailure，ledgerBlocked/Fence/Block、无ACK2/refresh，两个close entries仅actualClose1。作为旧行为事实供主控裁定，不修产品、不预授恢复。
- Lottery1原premium usage ReadAsync、Rental两路原PanelAsync分别在独立真实read已完成后注独立cause；MainCommitted保留，第二ReadOnly/Tx ConfirmedRollback，catch外零ACK/sourceclose、无新增Fence/Block。Rental refresh的真实DI scope另证第二Tx已Committed后失败，不撤销Main。无把拒绝场臆造refresh或合并Tx。
- 全部实际生产110表/644列及native另2表3列保存schema、完整列、读出类型、NULL/BLOB。唯一无PK的item_slots最终轮实际SELECT rowid并绑定同排序；需回滚场same-run逐列及rowid零归一相等，C1/提交后故障按durable变化记录。观察查询提前enlist同一个SQLite participant已披露，未造第二fallible participant。
- 同物理sequence30重发真实ordinal2/freshfalse，新31为ordinal3/freshtrue，迟到30为ordinal4/freshfalse。健康Committed只刷新当前投影/无第二原ACK；健康回滚去trigger后同序号复用原entry/原Ordinal1再结算，新序号另立entry。真实已close的来源记TransportUnavailable，不伪造仍可接帧；替代只称新合法请求。
- 原完整focused142/142：App八类118（Recipe15/Lottery27/Vending12/Epic14/Rental11/RentalWire3/ApplyEffects11/ItemDelivery25），Protocol四codec23，Networking EpicRegistration1；原fully-qualified/逐display/status、参数、attrs/强断言原bytes不改，Release来源新鲜、原并行/5m/Skip。probe真实加载Location/MVID/SHA与正式目录依赖copy一致；正式focused的实际日志DLL路径之PE身份单列，不冒testhost AppDomain观察。
- 原capture source/runtime交叉173message/201command；六目标及八相关fine（含独立refresh/premium）仍pending，原七Mailbox对象及其它ledger完全不动。原preclose精确Task32路径于Result前实际exit0/valid/pending[]；本调查不被canonical completion_task引用，pending[]为工具语义，不当交付或review批准。原工具/exporter SHA保存，无expected/工具更改。
- Result前完整NUL核22675 tracked路径集精确相同、逐path current=before=固定HEAD、tracked diff/index空；原Task31/历史R1R2R3/中文路径均在全集，不以目录前缀替代。原Task计划前缀及原untracked保留，新增路径全部仅Task32/E/task32logs。pre-result-authored四类whitespace清。结果写后原strict/八self尚未预记，将实际运行再补录并封精准manifest。

Why：按真实共享边界抽取有价值矩阵，保六路各自action以及四种native/三种C1C2出口/真实scope/刷新，而不做所有协议×所有fault笛卡尔积。四opening已提交scope被catch、租赁NoRoom typed及刷新catch外等意外现状均如实保留，不修改产品求绿。所有诊断轮、native raw和新freeze保留；缺省ImmutableArray序列化等仅修E，最终结论不绑定失败轮。

Remaining：受控内存physicaltransport非TCP/A21；controlled participant/scope/read不是自然事故，未新跑disconnect child或2秒性能。Vending/Epic未逐路native、openingC1/C2只Compound代表、dispatcher以Compound/Rent/Charge代表，其余只列源码共享推论。本轮新DFO路径零记录/缺全量controls，不称新74/7或八full；Task31的9075/DFO74/7/Golden15/Char10仅已提交历史。无产品/正式测试变更，未造mutation或新testcount。后续恢复/迁移契约、受影响gate及独立result门均归主控；producer不规划下一Task。

### Result R1 postwrite 实际结果与停止协议

以上Result实际追加后，2026-10-25T02:52:34Z原 `migration-ledger.py validate --output .../ledger-final-strict.json` 实际exit0/strict valid/pending[]；02:52:35～36Z原 `selftest --output .../ledger-eight-bad.json` 实际exit0、八类坏数据全部rejected。原tool/expected/canonical未改，两个postwrite原argv/UTC/exit/log保存于task-32-ledger-{strict,eight}-postwrite-01，未以结果写前检查冒充自身postwrite。

术语说明：上文OriginalOpeningInventory指原 `IOpeningInventoryDomainService` → `OpeningInventoryDomainService` 生产链，并非另建或存在名为OriginalOpeningInventory的新类型。真实native资格按native-stack-qualification中实际provider/同cause/阶段成立，未证明JIT为何省略某栈帧。25个记录的native/工具运行都自然退出，已只读核无本probe/归属本工作区的testhost残留，未kill任何其它进程。

最终完整NUL/nonowned逐path current=before=固定HEAD、精准Task＋E＋task32logs manifest、四类authored whitespace/行尾和原untracked保留由final-*证据封存；manifest排自身hash，Task仅绑定原29144B前缀避免循环，完整最终Task身份停止writer后外报给主控。producer在封存后停止全部writer/native，交主控指定另一fresh required Result reviewer，未自行复审/提交/规划后继。


## Independent Quick Review R1 — TASK-32/r1 Result R1，fresh result review，2026-10-25

```yaml
kind: rks-review
verdict: approved
```

### 目标、范围与判据

本 reviewer 独立于 producer/planner，仅审本 Task 的有界旧行为调查、Result R1 及其真实 postwrite，不是 Task-plan review、完整 Spec/Plan gate 或未来恢复语义批准。实际 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`、HEAD `e878c4ede03bc46e313b3bb3fa64de845d77e160` 匹配；无 `.codegraph/`，因此直接检索当前源。

追加前目标 Task **38258B / SHA256 `bdabad86136feee814c8672fec18b536e5c6878337d10940d6481dbfe2a1b185`**；原计划前缀 **29144B / `180650f92a2783250bf8e5f01f8b5305a223497aa6f6fea13598fc6184827a31`**。E=`verification/evidence/task-32-inspection/` 的 manifest **191393B / `6389c8ab9febf5ca77a1f79954596e7f061a63d0548739a962ab588f4253cb08`**：独立逐项读取581项实际bytes/SHA全部吻合，连manifest自身恰为582真实Git新路径，Task按既定原前缀绑定而不循环绑定追加review。

已读 `reviewing-work/SKILL.md` 及 `references/task-unit.md`、`code.md`、`deliverable.md`；依 Task契约/详细计划/Result/postwrite、P6 §4～6及T-004/T-005、对应B5 RQ-001/003～009/013～015与AC-001～005/008～009，判定本地明显缺陷、遗漏和交付/证据矛盾。依赖Task31完整85124B/SHA `309a1571ef61254f828871376b1d1621ec2e50bc77022c5925b6368a9640b264`匹配，Result R3、fresh approved R3和Controller Consumption R3可用且保持原文；未将历史9075/DFO74/7等升级为本轮证据。

### 决定性实际检查

直接读当前probe的Program/Observation/Delivery/Snapshot、strict checker和实际原fixture/生产接线，而非仅依report。原ShopFixture在最后configure后只Build一个provider；probe保first production GameplayOutboxes factory和同provider causes，原adapter/OutboundSender后接受控physical transport。原 `GameInboundFrameCounter` 每帧ordinal递增、仅首次或latest+1为fresh；probe实际调用Observe、同metadata完整raw body、已注册decoder及dispatcher，无手造request或第二provider。真实fine scope由legacy `CommandBus.InlineAsync` 创建，dispatcher scope由 `HandleOnExecutorAsync` 创建；scope fault是实际resolved scoped IAsyncDisposable，未把body末尾throw或自建手动Dispose冒scope。

独立以原checker只读验证前缀检查全部matrix04原raw（没有执行其写output尾段），**52场/0失败**。直接抽核NoRoom、Lottery native、Rental native/C1、Charge C2、Epic fine、Rental refreshscope及dispatcher replacement等关键raw，并与当前生产domain、UOW/Settlement、Protocol catch和provider源交叉：

- 六Main是原Transactional，Rental refresh另一个Transactional，Lottery1 premium另一个ReadOnly且在Main catch之外；没有把Main已提交与第二单元混并。真实action1811的暂存写和确认回滚、四个native场FK1/child暂存/bodyreturn/原19-787/同cause KnownStorage-FallibleCommit及完整回滚均有原记录。Rental observed stack是DbTransaction.CommitAsync→CompleteAsync:194；当前pending明确SqliteTransaction，该源只在await其CommitAsync捕获原SqliteException后签该fact。未依赖“已内联”猜测或补造栈。
- NoRoom确实满32装备格、stars100→97暂存、domain正常返回Rejection6并RequireRollback，最终ConfirmedRollback且110表全列/实际rowid相等；Frozen=false/Projection=null后原Settled失败成为PostCommitPublicationFailure，ledgerBlocked/Fence/Block、零帧及actualClose1。两个close登记的Closed/Discarded不等于两次物理Close。此为旧行为发现，调查如实披露且未修产品求绿。
- C1是SQLite durable后controlled nonfallible违约，CompletionUncertain/ledgerBlocked；C2是真SQL1811加独立Rollback IOException，最终typed RollbackFailure的图同时保留原两cause引用。真实默认保护及source chain有raw，而非只由异常名推断。四opening已提交fine scope被原catch映拒绝、ledger仍Committed/source健康；Rental Main/refresh scope在实际Committed后失败的不同位置和关闭结果分开记录。
- dispatcher scope失败前已有实际登记帧，physical事件先Send后Close。replacement raw的source2108667/replacement45465720/unrelated6019324是不同physical，实际只关闭source，另外两者可用且未fence；replacement后续新请求实际发送成功。成功重发/迟到重放、确认回滚后复用原entry/Ordinal以及已Close后TransportUnavailable均使用真实counter/transport，不假装已关闭socket仍能接帧。

全资产Snapshot枚举实际sqlite schema、列/类型/NULL/BLOB和PK，唯一无PK item_slots另SELECT真实rowid。观察查询提前enlist同一SQLite participant的测量影响明确披露；每Tx至多一个fallible participant，C1/C2受控participant的用途明确。报告把same-run逐列零归一回滚与durable变化分开，没有跨run泛strip时间/GUID。共享OpeningInventory/ApplyEffects/ItemDelivery、Rental Replay/Panel与LuckyStar的ItemUse/Dungeon/persistent recovery caller已与当前源码命中交叉，未把静态caller存在冒动态全故障覆盖。

### 验证身份、限制与结论

独立核63个source-map当前bytes/SHA无漂移；build09与matrix04 freeze同为 `ef32d638a547f104919459f2a72456558dfe2f1a65cbdfe4c74efb74e248d74d`。实际加载10个Dfo程序集Location对应文件SHA逐项吻合；独立probe借friend名称的真实路径/MVID与正式Networking.Tests区别明确。build09原log为Release0warning/0error；focused三份原详细log/argv/exit分别118/23/1通过，共142，保原类/参数化边界、并行/5m且不是新full/DFO全量。25份native/tool metadata的原log SHA全部吻合，诊断exit134/1保留而未冒成功。

capture source/runtime为173message/201command，目标六logical与含premium/refresh的八fine仍pending；原canonical及七Mailbox对象保持。原postwrite strict/八self metadata、UTC/exit和实际输出相符：strict valid/pending[]及八坏例rejected；pending[]仅原工具completion_task语义，不是本独立review或闭行批准。独立重新逐path读取全部22675 tracked文件，current SHA与before及final记录HEAD SHA全相等、NUL路径集相同，且实际Git tracked diff/index空；manifest及63源身份核验共同支持无越界改动。四类authored whitespace/行尾检查及自然退出资料已封存。

未发现本 Task Result R1 范围内未解决的material finding，判 **approved**。该批准只说明有界调查的当前交付满足本地结果门；不授恢复WHAT/HOW、SelfSettled/WriteResult/raw或新VC，不关闭六pending，不选择下一Task，不代表T-004/T-005或Spec完成。受控transport非TCP/A21、受控fault非自然事故、Vending/Epic未逐路native、C1/C2/dispatcher代表抽样及零新DFO记录等限制与Result一致，不要求无价值的故障笛卡尔积或重复native。

本reviewer只APPEND本round；未改产品/正式测试/Result/计划/上游/canonical/manifest/raw，未新建文件、stage/commit/remote，也未启动native/broad重跑。追加后核Task新身份即停止writer，交主控消费与后续检查点裁定。


## Controller Consumption R1 — TASK-32/r1，2026-10-25

主控消费 fresh Independent Quick Review R1 的 `approved`，审核目标为 Result R1 38258B / `bdabad86136feee814c8672fec18b536e5c6878337d10940d6481dbfe2a1b185`，审核追加后 Task 45579B / `568c0483e4e5266c0f9464f7f793f3aab1e54a29191c6c6dc1f60482e11d2ee7`。无未解决 material finding；producer/reviewer 均已停止 writer/native。主控独立逐项验证 manifest 581 项 bytes/SHA、连 self 恰为582个真实 Git 新路径，tracked/index diff 空，Result 原前缀与 reviewer 绑定一致。已读 checker 全文，以只读前缀重新检查 matrix-04 原始52场，52场/0失败，没有运行写输出尾段或新 native。已消费 report、相关真实生产边界及审核的限制；本轮142 focused只是聚焦检查，不能冒新8 full/DFO74/7/实机或2秒验收。

裁定 `continue`：本 Task 的有界调查满足独立结果门，允许仅582个既定 Task/E/task-32 logs 路径的本地检查点提交。当前 HEAD/parent 仍为 `e878c4ede03bc46e313b3bb3fa64de845d77e160`；提交前四类 authored whitespace 与 exact path stage 再核，提交后核 parent、footer、实际 diff-tree 和清洁状态。manifest 的 Task 绑定原29144B计划前缀，main/review追加由最终检查点自身绑定，不改写已封存证据。

本结论不迁移产品、不开闭 canonical 行、不授 SelfSettled/WriteResult/raw 新权限，也不把六 pending 作为已完成。下一安全动作是主控依据本调查裁定剩余四 opening 与 Rental 的可见行为边界；凡改变已批准恢复/保护/作用域归属或修复 NoRoom 的 WHAT，先进入受影响 Spec/Plan amendment 独立 challenge/review，且 amendment-only 检查点在新实现 Task 之前。不以 Task32 的调查批准替代这些门。授权仍为只本地，不推送/PR/远端/版本/tag/部署/游戏执行或清理工作区。
