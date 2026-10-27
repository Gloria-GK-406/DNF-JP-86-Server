# SellItem Task22：原边界与迁移判据

## 真正 before 与接缝

HEAD a6f19c44f0b3a539ca08100aadd76146c0411512，原八存在源逐 byte HEAD，新 whole 缺席；capture-before-01 和 pure family-before-01 均先完成，再运行旧真实 decoder→dispatcher。old-oracles-02 实际 73/73＝ItemSale51＋KnightCoin2＋架构6＋新 flow14。新 flow 13 条结构化 observation 与 sourceproof 等35项封在 before-manifest，后续不改。01 observer原会给正常返回拒绝额外 SQL read participant，原raw保 diagnostics；02只在真实成功receipt/exception时读取action stage，normal拒绝不触SQL，不人为扩原参与者。

ItemSale51 case声明/参数全保：helper从手造handler改真实13B body/原registered decoder/dispatcher；原直接 domain 与纯投影测试仍各守原目的。旧stale fine请求可携旧session，但wire无旧身份字段；新旧用同provider forwarding selected-query proxy返回真实previous session，domain IsInWorld仍询问实际registry。旧bad sum和旧角色gold/count断言保，新增加实际domain拒绝和ACK1，不能称wire携带旧身份。

KnightCoin两个原真实PVF参数及原购买命令不变。Sell原receipt接缝改按granted BusinessSlot映射到真实GameSlot并打线，保Gold98000、差18000、库存0；新单成功ACK精确slot/count2/gold98000取代旧Result.Rejection.None/receipt非null。不是删掉价差断言或把PVF替fake。

架构原六case/其它协议判据全保，仅Sell旧fine形状改为whole.Input精确SellItemRequest/request.GameSlot及真实domain.SellAsync.slot BusinessSlot。原六domain无GameSlot/InventoryContainerType和receipt无wire地址仍保；新增实际BadBusinessModel(GameSlot)必须由同反射判据拒绝。原unmappable实际组合case继续先mapper抛异常，不以坏sum提前吞掉。

## 一个 ordinary Write 的来源与语义

原fine只IAccountCommand；OnlineUserId字段实际未被fine/domain读取，没有selected/settlement marker或足迹。因此使用空声明participants、Ordinary/None，不借Sort推断、不新捕actor fence。不加Main、NonRequest、Load、NPC权限、online ID或world前置门。

实际保持 mapping → session query → sum → session/online；合法时clock在Write action内作为原domain参数求值。ObserveClock和ObserveSales直接Assert.Same同一open非只读DomainTransaction，传入now相同。正常Result.Refused仍Committed；只ItemSaleRejectedException保存原cause，catch外RequireRollback(cause)，完整unit结束才构造拒绝/ACK，普通SQL/native/cancel/typed不宽catch。原信息日志字段迁whole，旧fine重复Error按VC-B2消除；原domain缺价/坏价ERROR及上下文仍由原51case守着。

原Data ItemSaleResult/Receipt、ItemSaleCompleted整文件、codec/decoder/encoder与共享support/core均逐SHA只读。成功用ICommandProjections原composer，单ACK、不补库存NOTI、不重放；普通composer/actual encoder故障Warning同cause、NotificationBus原Failed隔离、caller忽略结果且正常完成，卖出仍已提交，不fence/close。此处测试直接观测Warning同cause、实际无batch/close和完整已提交资产；Failed返回值依未改的EventService→NotificationProjectionSubscriber→NotificationBus实现传回，whole仍不读取它。

## 三种直接拒绝 ACK 故障

旧truebefore三路均raw同cause；本case原始栈不修改。新pre没有Write和提交，rawSame；cap有真实部分gold staged写、ConfirmedRollback完整资产恢复后ACK失败，仍rawSame。normal domain返回拒绝实际Committed，无declared或动态participants，new default typed PostCommitPublicationFailure、InnerException同原cause，明确和旧raw外层分类不同。检查closed ProtocolNonEffect/HasCommittedUnit/empty committed participants/empty attempts/完整GameplayCallChain以及Error日志相同Root/Parent/Depth/Source/SourceConnection/CommandType。三路各自ACK登记0、encoder1、source-close Single/IsClose并Assert.Same实际f.Connection，JSON由ReferenceEquals导出，EndedAJob true、nofence、全资产same-run exact。

这是本Sell实际before/after证据，不借Task21代签；无新registration exit或raw许可，生产default处理不改。RecordedOutbound只证明调用，不能冒实际socket/A21关闭。

## 真实 SQL、native Commit、资产与 mutation

同实际SQLite participant内 action BEFORE UPDATE OF count RAISE(ABORT)19/1811，native AFTER UPDATE OF count插入缺parent的DEFERRABLE child；实际PRAGMA foreign_keys=1/child>0、domain正常返回、gold100031/qty9和全部stage，随后SqliteTransaction.Commit19/787，CommitFailure.Cause与外抛原cause Same。两路最终ConfirmedRollback，完整items/item_slots/stats/parameters/cargo/events/deliveries同run exact，BLOBhex/SQL NULL/82Bcore不丢；去trigger后同seq重做成功，不replay。跨run仅items.created_at/updated_at各runner UTC窗口与三具名事件GUID双射/FK关系校准，原raw全保，不忽略整表或抹异常。

唯一mutation删RequireRollback原句，不改domain/cap/测试：cap100030、oldgold100000、单价31、qty10卖1，实际ACK22/qty10却gold100030，精确资产断言RED；cap0另因Committed≠ConfirmedRollback RED，两个正常卖出参数仍pass。finally原4246B whole SHA142d5f765930d5864e306ff1e151b87e5b2adddb38f089daeaa10313e987c0e0恢复，同四目标4/4。变异前已输出完整actualassets及实际帧0116001100000000000000000000000016（ACK错误22）；接口静态类型JSON的ack只显示MessageType，不把缺ErrorCode属性误当未发ACK。首次mutation评估driver预猜3pass1fail/查ErrorCode失败仅是诊断，保v1原文，按已完成真实raw分析为2pass2fail，不另造RED。

## 正式源身份与后继

freeze01首build仅新增注册测试namespace拼写错，保raw；修本owned断言后freeze02覆盖10物理路径，9存在/1删除；产品whole未变。正式源为source-freeze-02，后续source-final-checks核before35、原非owned14155（仅排canonical两ledger）和Golden15。正式结果、Counter和DFO以formal-summary/test-delta/assertions-check为准；另五套1800是Task20库存，不冒本Task八freshfull。单CMD≤2秒留T012，A21实机/Spec最终核验/Astra与主控checkpoint后继；本执行者不自行stage/commit或关后续Task。

## r2 受控日志身份接缝适配

formal02 Net原199个display完整，198pass/唯一mapping日志匹配超时fail，四controls未运行；不冒最终通过。主控及Task22 r2 §10确认这是VC-B2预期CMD作业身份从inbound:GameSellItem迁GameSellItemCommand，**不是新的错误消费/关闭政策**；初诊报告“error consumption boundary”措辞保留为当时诊断记录，由本段及r2正式处置澄清。

r2新增第11owned Networking/Integration/InventorySlotMappingFailureIntegrationTests.cs原7413B/d9c0b43f…，current=HEAD=原14157 map，另封单源before。仅ErrorsContaining literal从Gameplay inbound:改Gameplay GameSellItemCommand failed:，逐byte等先前最小候选7432B/139b29…；全部其它bytes、原LogLevel.Error/Single/异常类型/255/65/第二真实连接精确ACK/10秒/Host/端口/parallel均不动。精确原TCP target新编号1/1 GREEN360ms后才封freeze03，原十source逐byte等freeze02、10存在/1删；三full/四controls重新完整运行同03，不能拿target绿替全量。Taskr1 snapshot、before35/map、mutation、freeze02和正式02全immutable。最终14154非owned核法只排原map实有的canonical2ledger+新ownedNet一键，新增源单literal另证。
