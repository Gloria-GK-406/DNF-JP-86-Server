# TASK-26 — MoveItemSpace 完整 NonEffect CMD 与库存移动领域能力

## Task Unit Contract

- Revision: 1（本契约与详细计划同一修订）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一最小有用单元：logical65555／wire0013 归唯一 GameMoveItemSpaceCommand；原网络 handler 的映射、一次事务、结果、仓库／外观／Housing 投影归 whole，原 fine 中的移动算法归可直接复用的库存移动领域能力，删净旧 fine 两类型和 caller。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015、RQ-013/017本簇义务及AC-001～005、007/009/011局部结果、F-34/F-112；不宣告T-004或Spec完成。
- Owned boundary: §2的27个物理生产／文档／测试路径、本Task、verification/evidence/task-26-inspection/及verification/logs/task-26-*新材料；canonical migration-ledger.json/md仅改§9两个ID。原低层移动／cargo／creature／内容／Persistence／执行器共享机制与公共support只读；原mixed slice消息和codec／encoder块逐byte保持。无Main、footprint、ledger、replay、attempt、新raw许可或P3恢复。
- Required checks: 产品前完整NUL tracked分区和真正旧registered decoder／fine／SQLite／同provider actualoutbox oracle封存；算法逐方法迁移对照、全部旧caller强能力、真实新旧资产／UID／core／帧批次、单Write／原postTx读取和两catch降级、SQLaction与sameparticipant nativeCommit回滚、编码失败外壳及原cause／关闭／chain／fence；有行为价值的restatement guard资产mutation RED→bytes恢复GREEN。最终同freeze Release0/0及八项目无filterfull、原8991逐display/status/multiplicity N-D、DFO74/7、Golden15/Characterization10、runtime/purefamily／两ID／strict与原八selftest及八物理坏副本、完整manifest/nonowned SHA。
- Dependencies: 仅 [TASK-25](TASK-25.md) r1／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在6f9917e5f7ee48ab50ce3378c5be7fef1b333ba4 committed done。B3/P4及matching gates继续有效，Spec baseline仍10a7218a779493494b138bd6b43cc0fc1a84aa6a；A与旧Task不重开。
- Result review: required — 本单元搬迁多端点移动、反向与restatement判定，新增真实领域接口并迁18个旧测试源，错置一次写入／后置降级或弱化UID断言可丢物品、逆向移动或漏ACK。fresh独立结果快审须实读原新算法、全caller、同provider真实rollback／mutation及局部测试接缝，不以机械替换或数量相等代替；主控消费后才checkpoint。不另设Task-plan独立gate。

## Detailed Plan

### 1. 身份、授权与当前库存

唯一workdir为/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021，branch task/refactor-session-20261021，接手HEAD 6f9917e5f7ee48ab50ce3378c5be7fef1b333ba4。主控已核26唯一未用、onlydependency25 committed done、DAG／clean／index空；执行者接手时重核身份和唯一writer。没有.codegraph/，用rg，不自行建索引。

沿既有governed／long-running／task-units，中文、本地Co-Authored-By约定保持。主控readiness后由fresh producer实施；执行者不stage/commit/push/PR/merge/remote issue/版本/tag/Release/部署/cleanup，不运行df_game_r/DNF.exe，不建Task27；旧Task/A seal/证据只读。本计划不自批执行结果。

当前8991＝8971pass＋原准确Dimension2fail＋18skip。Task25 formal01三fresh full为App5215（5197/2/16）、Protocol1777pass、Networking199pass；其它1800＝Host203＋HttpApi57＋Infrastructure976（2skip）＋Persistence484＋Pvf80仍是Task20库存。此单元新增Abstractions领域契约和Application服务／DI且迁跨App/Protocol测试能力，最终取八项目fresh full统一重建库存，不把前1800库存冒本轮结果。suite资源和总耗时只观察，单CMD≤2秒留T-012。Task21旧TCP Connection refused根因未证，本轮通过不冒已修，也不按登记偶发豁免。

### 2. 精确范围与职责归属

27源＝7生产／文档＋20测试，预计22改／5新／0删文件。旧fine与业务结果在mixed slice内删，不伪造文件删除。保各文件BOM／CRLF／mixed行尾，按bytes定点改，不整文件格式化。

| 相对workdir路径 | 允许变化 |
|---|---|
| Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs | 删旧fine两个类型与已上提业务状态／结果；消息handler薄化。原消息record／payload codec／decoder／encoder块逐byte保持 |
| 新Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/GameMoveItemSpaceCommand.cs | typed whole、原mapper／session／一次Write、原日志／仓库与外观Housing读取／两catch／一次explicit登记、纯错误码映射 |
| 新Server/src/Dfo.Server.Abstractions/Inventory/IInventoryMoveDomainService.cs | 业务InventoryMoveRequest、ItemMoveStatus/ItemMoveResult及IInventoryMoveDomainService；无ICommand/handler/协议字段／GameSlot |
| 新Server/src/Dfo.Server.Application/Inventory/InventoryMoveDomainService.cs | internal实现，承接原fine完整移动算法及creature前后Advance，不依赖Protocol或Networking |
| Server/src/Dfo.Server.Application/ApplicationModule.cs | 只注册新领域服务；不添第二bus／host或隐式当前请求 |
| Server/src/Dfo.Server.Protocol/ProtocolModule.cs | 只65555 route及一fine→whole；原decoder／encoder注册不改 |
| AGENTS.md | 更新MoveItemSpace现状、领域归属和原Tx／后置投影／降级边界；不写过程或.rks-agent引用 |
| Server/tests/Dfo.Server.Application.Tests/Game/MoveItemSpaceHandlerTests.cs | 原业务／网络局部测试驱动改新domain或whole，保资产、echo、split/merge/swap及串行两个移动强目的 |
| Server/tests/Dfo.Server.Application.Tests/Game/CreatureGoesBackOnTests.cs | 原旧fine直接能力改真实新domain＋原scope，保normalize／pet来回与完整实例事实 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/ItemUseFixtures.cs | 清除DirectCommandBus旧move fine分支；保未迁Tutorial能力。可加App局部domain/whole测试适配，不改公共Testing项目 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/CharacterContainerDomainServiceTests.cs | 原存储接缝判据改为whole→新domain→business slot绑定，保UseStackable及实际保护目的 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySlotBoundaryArchitectureTests.cs | oldfine两BusinessSlot判据改业务InventoryMoveRequest；whole.Input精确wire DTO，保实际badmodel拒绝及其它协议 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/WarehouseMoveSynchronizationTests.cs | 原helper改真实wire／dispatcher，所有材料／cargo／expiry／full／split／rollback断言保 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/BusinessEquipmentSwapTests.cs | 原fine caller改domain＋原显式Tx；两次wear同一个Tx的超单wire能力保，UID不折叠 |
| Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarAppearanceTests.cs | 原fine helper改domain＋原scope或真实wire，保appearance／dye／UID／空槽及SQL回滚 |
| Server/tests/Dfo.Server.Application.Tests/Achievements/AchievementTitleBookMirrorTests.cs | 原直接构造fine改新domain，保镜像出槽删除而非转移及原scope |
| Server/tests/Dfo.Server.Application.Tests/Achievements/AchievementTitleBookTests.cs | 仅旧fine caller迁新domain＋原Tx，保其它Title能力／CRLF及强断言 |
| Server/tests/Dfo.Server.Application.Tests/World/IllusionTowerDomainServiceTests.cs | 仅三处旧move fine及类型名迁新domain＋原Tx，保TowerQuickSlot直接业务能力、persistent/volatile拒绝及其它Replay/Tower代码 |
| 新Server/tests/Dfo.Server.Application.Tests/Inventory/MoveItemSpaceProtocolCommandFlowTests.cs | 本族先旧后新真实wire／局部observer、完整资产／失败／mutation；可容纳本App局部驱动帮助 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheCharacterChangesAsYouDressItTests.cs | 原whole局部驱动／logger类型迁移，保ACK→USERINFO及当前视图／position flag／selection相关断言 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs | 原直接fine调用改domain＋原fixture Tx，保avatar/unknown/out-of-range及Session=null cargo能力 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs | 原网络局部驱动迁whole；实际模板／slot／reason日志字段与PVF取证目的保 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/EitherEndMayBeHoldingItTests.cs | fine/domain与whole局部驱动迁移，全部served pair／reverse／identity／restatement／swap／echo／日志断言保 |
| 新Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/MoveItemProtocolTestDriver.cs | 仅本测试程序集内的whole/bus/unit/outbox局部驱动，复用原fixture交易边界，不造生产兼容入口 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/ItemPathFrameTests.cs | 旧fine两真实SQLite能力迁domain＋原显式Tx，原完整frame字节向量不改 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/ItemRefusalCodeTests.cs | Move错误码映射owner迁whole或同切片纯帮助，原enum遍历/各具名code断言逐项保持，其它UseStackable不改 |
| Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs | 65555精确唯一whole／无exit／旧fine与handler缺席，原persistent-write判据改该whole内部显式单元，不将Direct误判无事务 |

已实读caller：生产只有原切片及ProtocolModule；现有18个测试源如上，其中7 Protocol、11 App。没有其它生产协议／HTTP调用旧move fine。所有直接API能力必须有去向，不保旧record的ICommand兼容别名、旧handler代理或仅换命名空间的fine总线。需要新物理路径或超白名单修改时带实际证据返主控。

只读包括低层IItemMovementDomainService及实现、CargoTransferDomainService、BusinessInventorySlot绑定、EquipmentWear/placement/slot rules、creature runtime、InventoryEntryAssembly/InventoryEntryServices、SelectedCharacterCurrentViewDomainService／CurrentState查询、HousingCurrentProjection、Persistence/schema、GameUserState／InventoryFullSnapshot codecs，以及CommandBus/WorkUnits/Job/发件箱/ledger/attempt。原MoveItemSpaceCodecTests整文件和Golden字节只读；Support/MoveItemFixture、公共RecordedOutbound/TestGameplay及项目引用不改。

### 3. 有界领域抽取与算法对照

这是同一完整消费者迁移，不先交无人消费的服务。whole只驱动use case；原fine约500行状态操作成为真正共用领域能力，既供whole一次调用，也供原直接领域测试/多步同Tx使用。新服务不回调old/new CMD或bus。

接口采用IInventoryMoveDomainService.MoveAsync(InventoryMoveRequest request,CancellationToken)。业务request保原CharacterId、SourceSlot、MoveCount、DestinationSlot、两个instance值的顺序／默认0，以及可选init Session；字段是BusinessSlot，不是GameSlot。Session=null仍是原直接业务能力，不能因为正式wire总能取session就加全局必填门。原AccountId计算属性仅服务待删IAccountCommand，不复制成隐式授权。

ItemMoveStatus及ItemMoveResult保原enum数值、MoveValue/ItemId、ActualDestinationSlot、CreatureChanges与两Refused工厂。它们同业务request、接口放Abstractions/Inventory：CreatureChanges元素CreatureRuntimeChange定义在Abstractions/Creatures，**不能放Data后令Data反向引用Abstractions**，也不为此搬其它Creature契约。新Domain实现只引用Abstractions/Data；Protocol的错误码属于协议，保在whole内纯映射，不把client code移入Application。

新internal InventoryMoveDomainService保持原constructor能力：IBusinessInventorySlotDomainService、IItemMovementDomainService，及原可选ICreatureRuntimeDomainService／ISelectedSessionDomainService／ICargoTransferDomainService。原低层ItemMovement由CargoTransfer使用，若将cargo反注入低层会成依赖环；因此高层库存移动组合两者，低层和cargo整源只读。ApplicationModule只TryAddSingleton此高层服务，不添运行态副本／缓存／门或locator。

产品前保存方法边界；迁后逐方法body对应：HandleAsync→MoveAsync、MoveCoreAsync、PutOnAsync、TakeOffAsync、CarryAcrossAsync、Reversed、AlreadyCarriedOut、IsCarried/IsWorn及StatusFor。仅允许参数类型/方法名/无用CommandExecutionContext透传及必要XML类型引用的机械调整，不能借搬迁改逻辑。上下文原在fine业务中不读取，去掉它不生成另一显式请求通道。

必须保持：

1. touchesPet时按CharacterId从原session服务找人，creatures.Advance在MoveCore前后各一次；原CreatureChanges拼接返回，不因wire最终未用而删掉。
2. persistent/volatile backing不相等先拒绝。cargo分支保Session!=null且CharacterId相等、绑定两端、两instance AlreadyCarriedOut、原cargo.Move及ActualDestinationSlot/结果映射；不添普通移动current/world门。
3. 同槽Unchanged、carried/worn各组合、未服务list拒绝、binding缺失及同backing/跨backing选择保持。
4. AlreadyCarriedOut仍只按原namedValue>0、named端空、另一端ItemId相同；两方向都检查。Reversed只交换两端并清两identity，一次reversal规则不改。原注释披露restatement尚未实机观察的限度保留，不把推导当A21抓包结论。
5. PutOn/TakeOff/CarryAcross的wear/slot/CanWear、mirror去除、open count、displaced可返回性、PrepareForWear、take/put／flush及OnePiece顺序保持；不借此修注释列的其它历史语义或#209/#210。
6. 同backing交给原moves.MoveWithinBackingAsync，数量／merge／swap由原拥有者决定；StatusFor穷尽映射保，不加默认宽松fallback。两个相同core的不同UID仍不同物品，copy appearance不吞实例身份。

### 4. 真正产品前证据与wire

先核27源原存在22项bytes等HEAD、5新源absent，保存最小before源／SHA／bytes／行尾、HEAD/branch/index/UTC/实际命令和完整raw；Task及新证据另归属，不复制全仓/bin/obj。git ls-files -z完整分区tracked＝原存在owned＋nonowned、无遗漏/交集，每项current bytes等HEAD；two actual canonical B/verification/migration-ledger.json/md键必须先存在map，最终才可排除。旧中文C-quoted路径不能丢。mixed pure块和原case声明/参数独立保存。

产品/AGENTS修改前以原migration-ledger.py向Task26新prefix捕source/runtime，另沿原pure family/exits方法；预期173logical/201cmd、34P/1I/166Legacy、138Tx/50Direct/9RO/4Workflow、45messageProtocol/128Legacy、telemetry14，全部actual核，不拿算式代capture。

先仅新增本族flow/driver/observer，通过真正注册decoder→dispatcher→原handler/fine→SQLite→sameprovider实际Registering/outbox/encoder取旧oracle。selector1/type0013；body原最短14B：source space@0、signed index@1、source instance int32@3、count int32@7、destination space@11、signed index@12；18B起destination instance@14，常见24B尾余字节按原decoder忽略但完整metadata/SHA保。至少实际14B与含destination identity的18/24B可达输入；原<14拒绝和全部codec向量保持，不自改长度或尾字节规则。使用原WithRawBody/GameInboundFrameCounter，同对象、seq/ordinal/fresh/body长度与SHA观测；真实wire不能外包TestGameplay造成self-wait。

先旧后新有界选取：同list split/merge/swap与reverse；carried/worn put-on/take-off及相同core UID；avatar／mirror／persistent-volatile直接能力由原强测试与代表oracle共同护；personal/account cargo（含自动空位ActualDestinationSlot和shared材料／overflow拒绝）真实wire；原无session拒绝、normal domain拒绝、两个postTx降级和Housing失败后部分appearance保留、actualencoder故障、SQL action/nativeCommit及同seq。相邻case可合并同样覆盖，不为排列组合穷举重复矩阵。

旧strong suite先在原产品加新driver上跑，以实际filter列出全部18源所属测试class及原codec，保存原方法/参数名单和display/status Counter；before manifest与driver/source封后才迁产品。不迁后重建before、不覆盖输出。普通fixture/using/DDL/observer错误自治修正并保raw，不冒行为RED或逐项新增gate。

### 5. whole、原一次Write与原postTx读取

GameMoveItemSpaceCommand(Dispatch,MoveItemSpaceRequest Input):IProtocolCommand<bool>，Message/Frame保原Input/Metadata。bool仅表达原成功状态，非提交/投递证明。网络handler只一次bus；whole可注入新IInventoryMoveDomainService、原selected/currentView/slotMapping、character/account inventory entry、TimeProvider、原可选housing查询、ICommandWorkUnits/IGameCommandOutbox和logger；不注入sender/bus/queue、行网关/内容/执行器/locator。

原fine只有IAccountCommand＋Transactional，无selected/Main/settlement/recorder；每个有session的Resolve分支对应一次ordinary Write(context,new("MoveItemSpace",[]),action)，action只调用新domain的MoveAsync。空selected保持原范围，不代表无SQLite或不检查request权威；normal拒绝仍正常Commit，不全拒绝RequireRollback。不新增Main/NonRequest footprint/ledger/replay/attempt，不能把原业务AlreadyCarriedOut换成效果序号去重。

保持网络编排的全部顺序：

1. 先按原捕获identity（仅后Housing输入）；Resolve内按原source ToBusiness、destination ToBusiness、两次ToGame，再取session。mapper异常原外抛，不新catch或先做session/online门。
2. Resolve无session原Warning、返回Silent失败response及null result；没有Write，仍走最后原explicit回包。有session才Write把CharacterId／BusinessSlots／count／两identity／Session交domain。
3. unit完成后原Moved/Unchanged/MirrorRemoved映成功、ActualDestinationSlot有值才重新映wire destination；原完整Info字段保。其它status保原itemId/reason日志及纯ErrorCodeFor，未映状态仍throw。
4. frames从response开始。result!=null且response任一端是Personal/AccountCargo时，原AppendCargoState在事务外执行：重新取session→character.ReadCurrentAsync→account.LoadAsync→IsInWorld→clock→两端Distinct原序追加full snapshots。即使拒绝也可补当前cargo快照；不只在Moved才补。
5. 仅Moved/MirrorRemoved尝试原RefreshSession(draw=原response任一端Equipment)。重新取session，!draw/sessionnull返回null；currentViews.TryLoad为null不画。否则原Compose→frames.AddRange(appearance)，然后用最初identity取HousingCurrentProjection并追加。
6. 两个原catch仍是catch(Exception) when(!callerToken.IsCancellationRequested)，保原Error级业务诊断／原cause与消息文本，并继续最后登记；不换为异常类型过滤或删日志。catch内不clearFailure、不给新写权。取消已请求时不捕获；typed／guard若实际原抛出路径带sticky，不能捕获后恢复成功，依真实before／既有保护归因。
7. **Housing抛出时appearance已经AddRange，原catch保已追加appearance**；不要按“appearance失败ACK alone”的注释把列表回退/重建。Cargo/appearance两catch都保同一mutable frames的原部分追加结果。
8. 最后一次outbox.RegisterExplicitAsync(connectionId,new GameExplicitMessageDelivery(frames,[]))，原required direct／零best-effort、消息类别/顺序/精确目标保持；不拆多次登记或改NotificationBus投影。

原postTx读取位置保持，不强加ReadOnly或Write单元：CharacterInventory.ReadCurrent不走LoadAsync的LegacyWornNormalizer；Account.Load经InventoryEntryAssembly.LoadAccount读ownership/cargo/state/resources/locks再纯投影；currentView在两侧IsInWorld之间ReadCurrentState。这些原读取／纯帧组装由whole在原unit外调用，测试实际观察无当前写Tx及捕获降级结果，不把潜在隐藏写当已有事实。若真正before发现必须显式化的自开写／旧额外边界，带具体source/调用/结果返主控，不能凭猜测扩大事务或借普通ReadOnly制造sticky而改catch结果。

### 6. 原caller、局部测试与真实故障

18旧测试源全部有去向，保所有方法/InlineData、原asserted bytes、UID/物品数/日志字段及实际domain能力。允许改驱动/类型owner/import及必要断言接缝，Result逐文件说明，不能宣称所有assertion逐byte未变。

- 原直接fine／handler业务测试改为真实InventoryMoveDomainService.MoveAsync＋原DomainTransactionScope或MoveItemFixture.RunAsync；原Session=null仍能普通移动、cargo无Session仍NoBound、TowerQuickSlot直接业务地址/volatile载体不可用wire硬拼。两个wear在同一Tx、相同core交换不同UID、pet、mirror、appearance copy/rollback都保原多步能力，不一律缩成一条wire。
- 原局部网络handler测试由薄handler→本地bus→真实whole驱动，局部workunit适配仍调用原fixture/scope的真实交易；outbox适配可接原RecordedOutbound/sender，仅为保此类测试现有隔离。明确这是局部编排测试，不能冒新bus/core/网络或sameprovider native证据。可复用新Protocol测试driver及App flow内部helper；不改公共Testing项目或Support/MoveItemFixture，不造生产转发兼容。
- ItemUseFixtures的DirectCommandBus清除旧move fine分支；未迁Tutorial分支仍按原语义保。需要该fixture的move测试转domain或新whole局部驱动，不留ICommand<oldMoveResult>假类型。Protocol四大套原fake容器仍实际custody和原Tx，非只记录调用后返回成功。
- CharacterContainer判据把原“move fine ctor有business binder”迁成whole注新领域能力、新domain ctor有binder且无DailyRefill；UseStackable原判据保。SlotBoundary新增业务request两BusinessSlot、whole exactInput的GameSlot边界及真实badmodel拒绝，原六接口/其它协议保持。ErrorCodeFor原所有enum逐项code/未知值抛错目的保，仅owner迁whole。CommandRegistration保原其它Transactional注册，Move改Direct/Protocol＋真实单元测试，非删掉持久写保护。

新flow真实provider至少保下列有因果的oracle：

1. full资产包含items／item_slots全列82Bcore/BLOBhex/NULL/UID/容器owner/list/slot/count、人物/stats/gold、container/account cargo参数、mirror/appearance相关实际表、events/deliveries及相关creature状态；按所用case补实际owner字段，不只比数量。成功帧ACK及cargo/USERINFO/Housing逐byte/目标/一batch；相同core不同UID的两端均可辨。
2. action SQL以目标slot写入后第二端INSERT/UPDATE/DELETE trigger RAISE(ABORT)产生19/1811，证明已有一端取走／其它已stage写全部恢复；不是只在首读前抛。另同participant deferredFK missing-parent child，seed后启用限定actor/item/slot的AFTER写trigger，FK1/child>0/action正常完成后真正SqliteTransaction.Commit19/787，CommitFailure.Cause/外抛Same、native stack/ConfirmedRollback、完整资产恢复。去trigger同seq按当前事实成功；不以fake Prepare/普通trigger冒native或加第二fallible participant。
3. 普通无session拒绝（noWrite）与normal domain拒绝/成功（WriteCommitted）的actualencoder各真实注固定cause，核原raw与新默认PostCommitPublicationFailure差异如实际成立，InnerException Same、HasCommittedUnit/Completion、空selected/attempts、精确Single sourceclose Assert.Same/ReferenceEquals、Root/Parent/Depth/Source/SourceConnection/CommandType、EndedAJob、原fence/资产/ACK0。postTx默认壳差异须明确，不normalize成“完全同异常”，不添第4raw许可。
4. cargo读取故障、appearance/currentView故障各在原catch实际span注错，原unit已Committed、原Error同cause、资产保持并仍ACK；Housing故障在appearance之后，保ACK+已追加appearance且不假称只有ACK。caller取消与真实typed/guard不确定错误不要伪装普通Failed或擅清sticky。每条故障记录实际原scope、target/frame数及cause，RecordedOutbound仅证明登记/关闭条目，非实际socket。
5. 同seq按当前物品事实执行：有identity的原restatement被MoveAlreadyMade拒绝而不逆向；合法反向新输入仍可移动。无identity／不同payload的NonEffect原行为保，不套ledger冲突；ledger不存在。实际metadata同对象、14/18/24B完整bodySHA/ordinal/fresh与资产/帧共同观察。

same-run rollback全列exact、零豁免。跨run仅实际owning source明确的SQL CURRENT_TIMESTAMP列按同UID/表主键、run UTC窗口/created≤updated校准，以及EventService三个GUID逐case跨phase非零双射/DeliveryFK/batch/origin；其它core/BLOB/NULL/payload/帧/固定clock时间精确，不能泛忽略expiry/随机值或把native stage和retry事件ID合并。若本族真实不可控输出不同先查实际来源，保raw、不用宽normalize求同。

### 7. 有效行为mutation与恢复

选择新domain的AlreadyCarriedOut原具名保护作有界mutation：仅临时令此判定返回false，或等价去掉一个本次可达方向的判断；其它移动规则/dispatcher不动。真实registered wire先将一件物品65→3，再以相同带原identity的请求restatement；原会保持slot3并拒绝，mutant会反向把真实物品移回65。完整UID/slot资产oracle在assert前留actualrows/frames，必须真实RED；fresh合法drag正控仍可达pass。此保护是原服务语义，不以method是否存在或调用数当RED，也不把无效fixture／编译／timeout当RED。

finally逐byte还原被mutate的新domain源码，核唯一本语义差分和SHA，复跑同目标全GREEN再freeze；可按实际代码形状调整mutation拼写，不改变被保护事实、不修改低层domain或留测试开关。移植全部算法本身按body对照和原强测试验证，不为纯签名搬迁制造其它结构RED。

### 8. 最终freeze与正式核验

focused完成全部旧强测试/原新oracle/SQL/native/降级／编码／metadata/同seq/mutation恢复后，全部先前native自然结束，freeze27源（27final present，0物理删除，其中5新）、mixed纯块和原方法/参数/迁移变化说明；检索Server/src和Server/tests旧fine两类型及ICommand消费者清零，仅明确缺席断言字符串可留。无hidden bus/兼容入口，新domain模型无ICommand或协议依赖，新服务/接口的DI graph可解析。正式后改受影响产品/测试须新freeze和相关正式重跑，旧绿不能覆盖新源。

require_escalated可用环境运行Task26新runner/native（sandbox FSEvents会挂），保存cwd/HEAD/UTC/actualcommand/exit/elapsed/resources/完整console/DFO目录。原parallel、5m、Skip自然完成，不TERM活跃native/父runner、不覆盖原输出：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

Release exit0/0warning/0error。八项目分别Application、Protocol、Networking、Host、HttpApi、Infrastructure、Persistence、Pvf全部无filterfull；七GameplayAssertionRecorderControlTests须在对应七fresh full中实际出现pass，已含本轮fresh controls，不用重复计业务N。App只容原两Dimension准确display Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot及InvalidDataException/Dungeon7100/maze0/cell(1,0)/entrance Left。其它失败先保实际身份/stack/raw，按AGENTS登记规则诊断复跑，不能调parallel/timeout/Skip或把Connection refused算baseline。

DFO原violations.py CLI保持74records／7actual controls／原expected逐类数和程序集／outside-exe-orphan0／新增intentional0，exe0仅观察；若真实需要新intentional须具名返主控而非放宽expected。Golden15逐byte同HEAD/f01dc226，Characterization10逐display全pass/0skip，MoveItemSpaceCodecTests及纯帧向量保持。逐(display,status,multiplicity)Counter以Task25三full+Task20其它1800库存为原8991，最终八fresh重建8991+N-D。全部原方法/参数保；必要类型display/rename/status差异逐case映射与理由，不仅净数相等；无实际删除预计D0不代结果。suite耗时/资源只记录，非单CMD≤2秒证明。

### 9. Capture、两ID闭账与交接

finalfreeze fresh after source/runtime/purefamily/exits；预期173logical/201cmd（一fine→whole总数不变）、35Protocol＋1Internal＋165Legacy、137Tx＋51Direct＋9ReadOnly＋4Workflow、message46Protocol/127Legacy、telemetry14。actual只65555 route及MoveItemSpace fine→GameMoveItemSpace whole变化；新业务request/service不是CMD，不计为fine或新增独立执行器实体。原其它commands/families/routes及三exit种类全部owner精确等HEAD。

canonical仅message:65555和command:Dfo.Server.Protocol.Game.MoveItemSpace.MoveItemSpaceCommand两个ID：记录原一次Tx→ordinary Write/空selected、原算法领域去向、postTx无新增unit、两catch降级／Housing部分appearance、原explicit批次、真实native与默认编码壳证据。其它baseline/original/rows/groups逐值等HEAD，md旧前缀只append。预计pending113message/165fine/72source/5protection/8transition，由原validator actual确认，不提前闭其它范围。

required checks齐后追加Result R1与literal Outcome: done、计划前缀bytes/SHA。保preclose/strict、原8selftest与8物理坏副本按原semantic实际拒绝；临时坏副本可改原需要行但canonical仅本两ID，不改工具/基线或弱化反例。完整manifest列全部source/evidence/script/log/Task/两ledger bytes/SHA，与Git NUL changed+untracked精确闭合；无文件删除不造deleted项。Task/manifest自身用清楚prefix避免循环，完整nonowned map只排两actual canonical后逐SHA原样，旧历史不改。

Result列实际领域抽取/方法对应、原caller强意图与类型/驱动/断言差异、真正before时点、full资产与UID、原一次写和两postread／catch、cause/外壳/close/fence、partialappearance/取消边界、SQLaction/native差别、同seq/guard mutation恢复、八fresh full/数量账/capture/strict/manifest/nonowned及局限。全部完成后停所有writer/native，交fresh required独立快审和主控消费后本地checkpoint；不自批Spec。RecordedOutbound/局部driver非真实socket/A21，原推导restatement不是实机捕获，T012测量/Astra/Spec最终核验封存/用户分支收尾后继。

### 10. 自治收敛与返主控

helper/observer/fixture/私有结果/日志泛型／必要XML引用／nativeDDL条件及mutation表达式可按实际接口自治收敛，保诊断不逐项设新门。不得把高层领域能力变成oldfine代理或另一个CMD、把Session=null能力删掉、弱化18源旧强测试、给postread强加sticky单元、把原catch部分frames清空、改取消filter／原错误码、借Move实现新raw/P3或改持久规则。实际before发现额外真实提交点／不可等价错误消费，或须扩27路径／共享core/接口／WHAT时，带最小完整实证返主控，不以猜测加事务、不宽normalize或假native求绿；不自选Spec终局。
