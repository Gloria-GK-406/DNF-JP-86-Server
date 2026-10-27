# TASK-21 — InventorySort 三入口与两个完整 NonEffect 协议 CMD

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 内排序／锁定／解锁这一最小簇：logical 65556、66250、66251 的真实 decoder 分别进入两个精确 typed whole；旧 SortItem／SortLock fine、handler 业务编排和注册删净，原两种通知投影继续复用。三个请求均为 NonEffect，保原普通事务、原字段／槽位／在线／world／cargo 限制、完整资产及 ACK／snapshot／locks 行为。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015与RQ-013/017本簇义务；AC-001～005、007/009/011局部结果、F-34/F-112。不宣告T-004或Spec完成。
- Owned boundary: §2的12个生产／文档／测试物理路径、本Task、`verification/evidence/task-21-inspection/` 与 `verification/logs/task-21-*` 新材料；canonical migration-ledger.json/md仅§8的五个ID。领域／规则／存储／消息codec／共享执行机制与公共测试support只读，不能给本簇增加Main、NonRequest footprint、ledger/replay、raw许可或commit-result恢复。
- Required checks: 真正产品前源身份／fresh注册捕获及实际旧decoder Sort、Toggle/Unlock、动作SQL与真实native Commit oracle；迁后同provider逐项对应，保原强case和完整资产／metadata／字节／批次／通知隔离。验证NonEffect同序号再次执行、原正常拒绝和stale意图、真实已提交后投影Failed隔离与实际外抛默认收口；至少一条有效可编译sort-order mutation RED→逐byte恢复GREEN。最终同freeze Release 0警告0错误、Application/Protocol/Networking三个无filter full、其它四程序集fresh assertion controls，原8883逐display/status/multiplicity N-D、DFO原74/7 controls、Golden15、Characterization10、capture/五ID闭账/strict及原八坏副本/全路径manifest与非owned SHA闭合。
- Dependencies: 仅 [TASK-20](TASK-20.md) revision 3／Result R1／fresh Independent Quick Review R1 approved／Controller Consumption，已在 `68f6d572f4b32cd2aa5987e373d889a8b3e0a33d` committed done。其已接受骨干与三出口属于输入，不重开或改写Task20。B3/P4及matching approved gates继续有效，Spec baseline仍10a7218a779493494b138bd6b43cc0fc1a84aa6a。
- Result review: required — 隐藏Load写入移出原事务、普通拒绝被新participant guard提前抛出、锁定动作被错误去重，或把通知Failed隔离改成外抛，都会破坏资产／拒绝／投递语义；计数和codec不足以证明。fresh独立结果快审须实读原新事务与投影位置、下面的本簇空participants依据、真实旧新oracle／native阶段、mutation和完整增减账后才由主控checkpoint。主控做普通Task readiness，不新增独立Task-plan gate。

## Detailed Plan

### 1. 接手身份与验证口径

唯一workdir为`/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，分支`task/refactor-session-20261021`，接手HEAD为依赖检查点。主控已核TASK-01～20 committed done、稳定唯一ID／DAG，21尚未使用；执行前复核HEAD／分支／index空与本Task唯一writer。没有`.codegraph/`时用rg；若届时已有索引遵守AGENTS先CodeGraph。

使用当前B3/P4、routing及其消费记录，不复活旧门。继承中文、本地实施／主控checkpoint与Co-Authored-By约定；不push/PR/merge/tag/Release/版本递增/远端issue、游戏进程、部署或cleanup；A及Task01～20、旧raw／审查／快照均immutable，不建Task22。当前治理文档里Task20的“待检查点”是其历史R1文字，以已提交Result＋review＋主控消费和当前Git为准。

当前业务库存为Task20实际八full **8883＝8863pass＋2准确原Dimensionfail＋18skip**；App5112、Protocol1772、Networking199合7083，其余 **1800＝Host203＋HttpApi57＋Infrastructure976＋Persistence484＋Pvf80**，这些是Task20真实full。本Task只对前三项目新full，另四程序集新跑control；不能把1800库存称本次八full，也不把四controls加进业务N。套件时长／内存只记录；单CMD≤2秒仍T-012最终测量。

### 2. 精确路径与只读边界

以下均相对workdir；12路径＝8生产／文档＋4测试，预计7改／3新／2删。删的是旧fine文件；receipt保原形状搬到同namespace新whole文件，不留下旧命令兼容入口。 现有源保原BOM／CRLF／mixed行尾，按bytes定点改动；不整文件换行／无关格式化，尤其ProtocolModule及两个通知字节段。

| 路径 | 允许变更 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/InventorySort/SortItemHandler.cs` | 入站handler仅bus一次，绑定GameSortItemCommand；从`InventorySorted`起的原通知record／composer字节段保持 |
| `Server/src/Dfo.Server.Protocol/Game/InventorySort/SortLockHandler.cs` | 入站handler仅bus一次，锁／解锁共GameSortLockCommand；从`SortLockChanged`起通知段字节保持 |
| `Server/src/Dfo.Server.Protocol/Game/InventorySort/SortItemCommand.cs` | 删除旧fine record／handler／Result；SortItemReceipt精确record移至新whole文件 |
| `Server/src/Dfo.Server.Protocol/Game/InventorySort/SortLockCommand.cs` | 同上，保SortLockReceipt原形状迁移 |
| 新 `Server/src/Dfo.Server.Protocol/Game/InventorySort/GameSortItemCommand.cs` | typed SortItemRequest＋原Dispatch／Metadata，完整普通事务／原拒绝ACK／原投影 |
| 新 `Server/src/Dfo.Server.Protocol/Game/InventorySort/GameSortLockCommand.cs` | typed SortLockRequest，绑定Toggle与Unlock两个logical；原映射／普通事务／投影 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 仅本簇三route与两fine→两whole；原decoder／encoder／notification注册不变 |
| `AGENTS.md` | 当前协议路由增加本簇；仅记NonEffect、普通事务和原投影现状，不写过程或指向.rks-agent |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySortTests.cs` | 原12Fact／断言意图保；手造handler与旧fine/stale驱动改真实wire，原直接domain事务case保留 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/WarehouseStorageTests.cs` | 仅tower case两次Sort及其旧fine helper改真实wire；其余warehouse／domain／closed-slot断言保留 |
| 新 `Server/tests/Dfo.Server.Application.Tests/Inventory/InventorySortProtocolCommandFlowTests.cs` | 本簇before/after raw driver、观测／SQL/native／非效果重复／隔离／mutation目标；有界test-only helper可供上述两文件复用，不改公共support |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 精确三logical→两whole、两个CLR、Direct/Protocol／无exit、旧fine类型／注册清零 |

只读：InventorySort下两个Messages/codec及SortItemMapping；InventorySortDomainService、InventoryEntryServices、LegacyWornItemNormalizer及所有domain接口、PVF/规则、Persistence/schema、CommandBus/WorkUnits/Safety/ledger/attempt/outbox/NotificationBus/EventService。只读公共ShopFixture、RecordedOutbound、EffectRequestTransport；本地SendRaw可用其既有API或在本测试文件复现真实GameInboundFrameCounter→注册decoder→dispatcher，不给公共Body switch加Sort。`SortItemCodecTests.cs`纯Throws/LE/映射全只读。InventorySlotBoundaryArchitectureTests实际不引用本两fine，无理由顺手修改。若出现必要新caller／接口／共享源超此边界，先保实证交主控，不默加文件。

### 3. 真正before与最小driver

先在产品／AGENTS逐byte等接手HEAD、新两个whole absent时，建立E21最小owned-before bytes／SHA／BOM／行尾和非owned tracked SHA库存（排本Task与新证据），记录真实UTC／HEAD／分支／index／命令；不复制整仓／bin/obj或Task20资料。capture使用现`verification/probe-tools/migration-ledger.py`到全新`capture-before-01/`，配同一pure family／exit读取方法；源与runtime表核173logical/203cmd，28Protocol＋1Internal＋174Legacy，146Tx＋44Direct＋9ReadOnly＋4Workflow，message38Protocol／135Legacy。原legacy Fine时点必须由实际旧source SHA／注册证明，不能迁后重建冒before。

产品迁移前只先加本地driver与真正旧route的最小行为oracle，跑：

1. Sort真实body `[0,1,2]`（装备按Level）、锁定／解锁真实3B LE body，通过注册decoder→dispatcher；记录metadata所有原字段与raw length/SHA、精确physicalcontext、完整资产及原prepared batch帧字节。四次Toggle/Toggle/Unlock/Unlock保首02CA、后三02CB和body `[1,0,28,0]`。原12Fact及相关warehouse case先取得旧结果。
2. 同fixture Sort动作SQL故障与真实native Commit故障，具体方法见§6；保原cause引用／阶段／实际事务Completion／零失败batch／source-close与是否fence的真实观察，不能预假设legacy每个失败都是typed或围栏。
3. 成功投影实际encoder／composer普通故障：证明原NotificationBus Warning＋Failed、业务提交、该recipient整batch未登记且调用者正常结束；直接Sort拒绝ACK路径另测，不能与成功projection混称同一出口。选择原callback／encoder真正注错，保原cause与发生计数。

source快照、driver及原raw/meta封before manifest，后续各轮新编号，输出存在即拒绝覆盖。before若普通fixture列名／种类／counter／挂载错误，保诊断后自治修正；编译或fixture失败不是行为RED。若真正旧route否定下述边界，报主控并保源，不预改兼容政策。

### 4. 两个typed whole与本簇普通事务

使用`GameSortItemCommand(Dispatch, SortItemRequest Input)`与`GameSortLockCommand(Dispatch, SortLockRequest Input)`，Message/Frame引用原输入／metadata，结果可用非null bool表示“receipt存在／原用例成功”，不用nullable TResult，不当提交证明。两个receipt原record签名保持；旧SortItemResult/SortLockResult仅旧fine包装，无剩余caller后删除，不为旧测试硬留API。handler只送同bus一次，whole不调bus／queue／旧handler；constructor仅现domain服务、TimeProvider、ICommandWorkUnits、ICommandProjections及需要的Game outbox／槽映射例外。

**由主控核实并裁定的本簇参与者范围：** 两旧fine只实现IAccountCommand，没有ISelectedSessionCommand/IRequestSettlementCommand；SelectedSessionExecutionBehavior在此没有捕获recipient／选中参与者或围栏。原world/online检查在fine已开启的Tx里正常return null。因此两个新`WriteAsync`均显式`CommandWorkUnitDeclaration(name, [], Ordinary, None)`；不凭receipt.Session额外加`[actor]`，否则runner在action前的Safety会把原正常拒绝提前变throw。无报告者／Main／footprint，不需NonRequest；这个决定只属于本簇原范围，不推广其他协议。ProtocolNonEffect仍由现Bus的login/connection authority和request关闭／健康规则保护，不能用空participants绕过这些规则。实际不确定或已提交后外抛按既有FailAsync判定typed、保cause及原精确participants范围（此处空）；不伪称额外围栏或强改core。

SortItem whole保原位置顺序：

- 先按原Dispatch查session、online；保持`SortItemMapping.TryMap`的短路顺序。缺失／映射失败直接原ListType、false、1的ACK，不开旧fine原来未开的Tx。
- 原来会dispatch fine的分支恰好一次普通Write：action中按原顺序复验IsInWorld和connection对应OnlineUserId；仅Personal/AccountCargo检查dungeon run或tower。正常拒绝仍return无receipt并完成原空事务，不新增catch／RequireRollback或异常→拒绝映射。
- action继续`SortAsync → characterInventory.LoadAsync → accountInventory.LoadAsync → clock.GetUtcNow`，全部在**同一次原写事务**。LoadAsync实际先跑LegacyWornItemNormalizer，可Take/Put/ApplyDecidedWrites；不可换ReadCurrent、移ReadOnly/提交后、少读account，亦不可把其效果另开unit。 新旧本地forwarding observer实际运行原Load，记录两Load和最终clock与Sort所见同一非只读DomainTransaction引用／未结束状态，避免只以调用计数证明位置。
- unit完全结束后：receipt空直接登记原失败ACK；有receipt沿ICommandProjections对TheseCharacters([receipt.Session.CharacterId])投影InventorySorted。原composer的角色／connection／world检查、AccountCargo分支及ACK→full snapshot→locks保持。receipt仅当次投影数据，不放入会话／当前状态缓存。

SortLock whole先保原session/online静默门；只有槽映射处catch InventorySlotMappingException静默，其他异常不宽吞。原会dispatch分支恰好一次普通Write，action原world/online检查后ChangeLock，nullable bool结果只有非null才形成原receipt。unit结束后仍TheseCharacters＋SortLockChanged；Toggle解锁回02CB、显式Unlock重复也成功回02CB。无actor、无在线、非法映射、空槽／不可排序等保持各自原静默行为。

接线：SortItem绑定(65556,SortItemRequest)；SortLock whole精确绑定(66250,SortLockRequest)、(66251,SortLockRequest)，唯一handler共享两个logical；两whole都是Direct/Protocol，无RegistrationExits（null或empty）。原三个decoder、两个encoder、两个notification handler保留；旧两个Transactional注册及fine实现／caller删净。

### 5. 原投影隔离、stale意图与强保护

原EventService.ProjectAsync在原Tx结束后即时运行；NotificationBus按recipient一次lookup、所有messages一个prepared batch，AttemptAsync内部普通composer/encoder等错误记Warning并返回Failed，caller自身取消例外。新whole必须复用ICommandProjections及原notification段，不复制组帧、不拆三次登记、不把Failed主动throw／typed／fence／source-close，更不能用raw attempt冒保留。caller原本忽略投影delivery结果，新whole继续原处理。投影scope创建／释放等**实际外抛**和直接拒绝ACK的外抛才按现request默认终局；在truebefore分别记录适用事实，不用“所有encoder都该raw”或“所有postcommit都throw”的泛化。

旧stale测试直接给fine旧session，wire本身没有旧session字段。迁驱动保持拒绝和旧资产／flag不变的业务意图，不声称wire携带旧身份：可在本地test使用有界只读selected-query forwarding proxy，FindByConnection返回实际先前session，IsInWorld／Standing等仍问真实registry（已选另一角色），制造旧handler捕获stale事实；原、新真实wire均验证Sort正常失败ACK、Lock静默及原角色物品位置／UID／flag不变。必须保原旧／新对照和局限；不能把stale case仅替换成任意invalid frame或删资产断言。公共fixture、真实registry、领域实现不为此更改。

原InventorySortTests保12个Fact身份／断言意图：锁槽原地／压紧只目标band；UID／完整core／Durability／Value；同template独立实例排列与第二次幂等；piles不合堆、other band不动；显式未提交domain Tx完整回滚；worn不可排序；成功ACK/full/locks及gold100000；lock reload/只改flag/unlock恢复原core；四ACK序；非法／空槽不造items；stale两路不变。直接domain Tx case继续直接domain，不为整齐都改whole。已有默认排序／稀有度／过期／type规则不改。

WarehouseStorageTests只迁tower两次Sort驱动：AccountCargo真实请求保失败ACK且资产不动，Consumable真实请求保成功ACK→full→locks；原two receipt断言改为对应确切帧／资产结果说明驱动变化，不硬留旧Result或削成“未抛异常”。closed legacy slot20的原domain case及其它warehouse断言全部保持。

新flow补：NonEffect同seq两次Toggle实际1→0并两次正确ACK；同seq Sort在外部真实改变位置后重新执行当前sort，不得去重／当前重放；每次metadata透传含Selector/Type/Sequence/InboundOrdinal/SequenceIsFresh/raw length/SHA。新explicit context为ProtocolNonEffect、无effect ledger admission/Main/attempt；正常未选择账号／无world／online不匹配／非法Category／unsupportedCondition按原规则（有效page不支持condition回default，不一律拒绝）。必要read-only观察／trace仅在本测试文件，禁止生产current-request通道、第二Host/DI容器/outbox。

### 6. 实际SQL/native、完整观察与mutation

实际注错点已核`Persistence/Inventory/SqliteItemDomainStore.cs:158`：Sort Take/Put移位会向item_slots INSERT，随后同DomainTransaction交唯一SQLite participant。用fixture放非紧凑装备确保真实INSERT可达：

- action故障可在本fixture item_slots 对目标owner/list/slot的BEFORE INSERT执行RAISE(ABORT)，实际19/1811，Sort action抛原SqliteException，stack不含SqliteTransaction.Commit；全资产回滚。
- native故障在同fixture建父／子表，外键`DEFERRABLE INITIALLY DEFERRED`；item_slots实际AFTER INSERT向child写不存在parent。读取正在用的同participant的`PRAGMA foreign_keys==1`、child>0、完整已移位stage，证明Sort action已正常结束（含必要Load／clock完成）后才实际SqliteTransaction.Commit抛19/787，不能用fake participant/Prepare异常或INSERT时报错冒native。可沿Task20 observer方法作本地forwarding wrapper/prepare观察，但不改原工具或保留Task20专属逻辑依赖。

两路原、新真实decoder对照均保原cause引用、Completion、assets前后、失败未登记batch、真实source-close／fence观察。去trigger后同seq再执行是NonEffect新业务；不得造entry/Ordinal/ConfirmedRollback重放状态。SortLock改flag可补真实UPDATE items action故障（读取同次原core全字段），保事务／通知不提前成功。已确认回滚／native错误继续原外抛，不新增StorageFailure/commit-only恢复。rollback／lease cleanup不确定和实际外抛postcommit故障保全部独立cause及现默认收口，不能自行清失败或catch后续写；共享已有保护随Appfull保持，不扩本Task去改core。

资产观察至少items全部列、item_slots所有列、gold、相关账号／角色参数、domain_events与deliveries等实际触及表；nonnull BLOB无损hex、SQL NULL保null，82B core/可空扩展/UID/锁都不能退成System.Byte[]。same-run SQL/native rollback全部原列exact。跨独立run仅SQL items.created_at/updated_at可按实际runner UTC窗口／created≤updated具名比较；实际原EventService生成的event_id/batch_id/origin_instance_id若出现不同，沿Task20已独立接受的具名关系方法：有效非零、每类跨phase双射、sequence/type/schema/payload/time/所有其它列精确、Delivery FK无孤儿／batch与origin关系不丢，rollback瞬态和retry重新生成ID不合并，原raw全保。不要忽略整表／任意GUID或假定未检查字段相等；其它变化保诊断交主控。

至少一条有价值可编译mutation：仅在新SortItem whole调用点暂把实际mapped order换Default；用真实`[0,1,2]`、高level item及两个同template独立UID／锁槽，精确最终顺序必须行为RED，旁边Default／锁槽正控保持可达。原domain Level测试单独通过不能代替whole吞order保护；记录mutant实际行为／完整资产和source SHA，finally按原bytes恢复并同目标GREEN后才freeze。若seed导致两种order相同，修fixture使风险真实可见，不能称无差别运行RED；不强制给纯搬迁再造第二mutation。

### 7. 最终freeze与检查

先focused原强case＋新旧wire对应／投影Failed／SQL/native／NonEffect重复／stale等，再mutation恢复，冻结§2全部存在源与两删除项before SHA／原receipt迁移／通知字节段及所有非owned tracked SHA。复核current production不存在旧fine/new oldcaller/legacy callback；全仓检索而非只看文件删除。若formal后改影响源／测试，重新freeze并重跑受影响检查，不能拿旧formal冒新版本。

正式使用新Task21 runner/prefix、FSEvents可用的`require_escalated`环境；可读Task20/既有runner方法而不重写旧脚本或原输出。记录cwd/HEAD/command/UTC/exit/elapsed/resources与完整console/DFO。先`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，必须exit0/0warning/0error；然后Application、Protocol、Networking三个实际csproj分别：

```text
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

无filter，保xUnit原类并行／5m/deadline/Skip；父runner自然结束，不TERM活跃test。App仅两准确旧DimensionDungeon fail可接受：Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均InvalidDataException／Dungeon7100／maze0／cell(1,0)／entrance Left；不并入新失败。其它项目均0fail。

同freeze另跑Host/HttpApi/Infrastructure/Persistence四程序集各自原GameplayAssertionRecorderControlTests，保七actual controls齐。DFO用原violations.py CLI和原expected逐类count（当前原74：App62/Net7/Protocol1/另四各1）；outside/exe/orphan及控制范围外0，新增故意违规须具名分类，不放宽expected。exe0仅观察。Golden15逐byte对HEAD和已接受参考f01dc226，Characterization10逐display pass/0skip；原pure codec文件保持。

Counter用Task20全部八full raw作当前基线，前三以新full替换、其余1800保Task20库存；原8883按(display,status,multiplicity)逐case相减，D原则0，新增全passed，任何rename／status／删除逐项有依据。四controls只取证不另计业务N；报告每项目和总`8883+N-D`，原stale／tower assertions转driver目的变化与新增／删除测试分别列，不能靠缩参数或删case求闭合。

### 8. Capture／五ID闭账／交接

在finalfreeze fresh after capture＋同pure family/exits实际差分：仅三message routes及两个fine→两个whole；预期173logical/203command不变，30Protocol＋1Internal＋172Legacy，144Tx＋46Direct＋9ReadOnly＋4Workflow；message41Protocol/132Legacy、telemetry14。必须核actual，不拿expected替代实测，旧三raw许可注册及所有其它family/exits逐项不变。

canonical ledger只改以下五ID（其它baseline/original/rows/组逐值等HEAD，md旧前缀保留，只append本Task实际证据）：

```text
message:65556
message:66250
message:66251
command:Dfo.Server.Protocol.Game.InventorySort.SortItemCommand
command:Dfo.Server.Protocol.Game.InventorySort.SortLockCommand
```

每行挂原fine一Tx→ordinary Write、无selected参与者／无Main/footprint/replay、原通知prepared batch/Failed隔离、完整source／before／新wire／SQL/native/mutation/frames证据及旧fine删除去向。预期pending118message/172fine/72source/5protection/8transition；不关闭其它来源／保护／过渡行。原validator preclose通过后，按executing-tasks追加Result及literal`Outcome: done`，strict及原工具原八类selftest真运行。原八类物理临时副本可修改该反例语义真正需要的telemetry/transition等行，但canonical仍限上面五ID；不能为套五ID限制换弱伪例、改工具或旧基线。

Result交付实际before时点／源与新whole验证内容、空participants原范围／staleproxy局限／Failed隔离、每个失败阶段、保留与迁驱动的断言、new/removed case加法、三full及1800库存/controls区别、DFO/Golden/Char/capture/strict/mutation与局限。完整manifest列全部实际source/evidence/log/脚本/本Task／两ledger的SHA/bytes及两删除项原SHA，能与实际Git changed+untracked精确闭合；防Task自身／manifest循环哈希，保明确Result前缀identity。before所有snapshot immutable，非owned库存只明确排canonical两ledger；其它路径逐SHA不变，不能笼统声称原全未变。

完成checks/Result/manifest后停止共享写及native，交fresh required独立结果快审；执行者不自批／stage／commit。主控消费后才本地checkpoint并检查剩余P4覆盖。RecordedOutbound非真实socket/A21验收、单CMD最终测量、T-004余项、最终Spec审查／Astra／用户收尾均仍后继。

### 9. 执行中有界选择与返主控条件

本地driver/observer命名、真实seed/DDL限定条件、原支持的metadata计数方法、focused filter、trace JSON字段和安全结果包装可由executor收敛；普通fixture诊断保日志后自治修正，不为每次修正加门。不得改变本簇空participants／原正常拒绝／投影隔离判定、原Load事务位置或metadata/asset断言。出现实证必须扩大12路径／改共享机制、旧wire否定原假设、native方法确不可达且无等价真实Commit路线、需要新兼容豁免／WHAT/VC/提交点改变时，停止受影响产品推进，保最小源与完整证据／候选交主控；不先弱断言、预选新raw/P3恢复或自行终结Spec。


### 10. Controller disposition — empty-committed 拒绝 ACK 边界（2026-10-25）

主控消费真正旧wire `task-21-empty-committed-before-01` 及 `old-oracles-04`（56/56）与 `empty-committed-boundary-report.md`，并实读父RQ-023(c)/子契约Goal/B3 RQ-006与VC/P4默认收口及实际WorkUnits、GamePlugin。fresh独立只读边界复核已approved，原文见`verification/evidence/task-21-inspection/empty-committed-boundary-review.md`；它只判这个解释，不代本Task的required Result review。

处置为 **continue，同r1既定方法**：保原一个ordinary Write([],Ordinary,None)、判定位置、空事务实际Committed及原空参与者范围；不跳过／改Rollback／拆单元，不加第四raw许可或新Core能力，不修订B3/P4。原r1 25140B／SHA-256 `f83ddb2c32d4842277aac22eb4aba658fb314765e7db02eb9c7be561a5f843a7`前缀与42项before快照immutable。本项把§4/5已要求的默认终局落实到新实证，不变更Outcome/范围/依赖/接口/HOW/检查强度，不另设Task-plan gate。

迁后对此准确case必须实际验证并保留两份不同的分类事实：旧raw IOException原对象；新default `TransactionOutcomeUncertainException(PostCommitPublicationFailure)`且inner为同一原cause。原same-run全资产精确不变、原空Tx仍Committed、失败ACK零登记、原exact physical source-close恰一次／原顺序、无新增fence、无ledger/Main/attempt、最终异常仍`GameplayJob.EndedAJob=true`并保完整链。原before source/raw/断言不改弱；新对应断言明确检验内部外壳差异，不把其字段抹平后声称“异常完全相同”。原成功投影Warning+Failed/caller正常及三合法raw出口不变。该结论仅此已证NonEffect空参与者分支，不用VC-B2泛授任意异常分类变化；若迁后有其它字段/帧/关闭/持久化/围栏/错误消费变化再报主控，不能据本解释放行。

主控沿现已授权Task21继续，producer从已封before后开始产品迁移；唯一writer及所有不推送/部署/清理限制保持。最终fresh required Result review独立审定本例新实测与其余完整结果，当前没有产品迁移通过或Task完成结论。

## Result

### R1 — TASK-21 r1／2026-10-25（实际 UTC 2026-10-24），待 required fresh 独立结果快审／主控检查点

- Outcome: done
- **Changed:** logical65556/66250/66251归两个typed `GameSortItemCommand<bool>`／`GameSortLockCommand<bool>`，Direct/Protocol、无registration exit。thin handler仅原Dispatch/Input经同bus一次；旧fine两个物理文件、Result/handler/Tx注册/caller删除，receipt原声明搬迁。12物理source＝7改＋3新＋2删；产品/文档8、测试4，领域/规则/存储/shared core/公共support/codec只读。final `source-freeze-02.json` SHA **03f9f30854ff9d254a2494ed4aaa7df3d41c993b9954f882a85c55050c3f3177**；两个whole SHA分别 **6be30d288d162be65fb81a83f00732d82a2ae01c90cf23e9cecd74b57e017869**／**ff950fa725c27be0fa9c6483d2013aa773a5abd82bfec7d286e10d3158b96b84**。HEAD仍68f6d572f4b32cd2aa5987e373d889a8b3e0a33d、原分支、index空，未stage/commit。
- **Why／原位置:** 两旧fine仅IAccountCommand、无selected/settlement marker，故保持一个ordinary Write([],Ordinary,None)及原空selected参与者范围；不新增actorguard/fence、Main/NonRequest/footprint/ledger/attempt/replay、raw许可或P3恢复。原world/online/cargo判定仍在action内正常拒绝，空Tx仍真实Committed，未改RequireRollback或重复precheck。Sort→原character.Load（隐藏LegacyWorn归一写）→原account.Load→clock在同一真实未结束写事务；forwarding observer调用原实现且逐引用证明。bool不当提交凭据，原receipt签名/通知record/composer字节后缀全等。unit后原拒绝ACK或ICommandProjections，复用单recipient prepared batch、ACK→full→locks；普通composer/encoder Warning+Failed/caller正常，不强改typed/close/fence。
- **真正before与原断言:** 产品/AGENTS逐byte等HEAD、新whole absent时fresh capture/family；旧实际decoder driver最终56/56（原InventorySort12＋Warehouse28＝40，加16新oracle），封42项before（manifest SHA **fbaff455a83be9ac3f22892b3f0712cac3487165384dafdcdcf17d4b62b33365**）后才产品迁移；快照/driver/raw/meta不变。迁后focused56/56、Protocolfocused31/31，最终full全保。原12 Fact身份/断言意图保，直接domain Tx仍直接domain。stale/tower迁wire驱动：wire不携旧session，仅本地只读query proxy返回真实旧捕获、world问真实registry，保Sort失败ACK/Lock静默及UID/core/flag/位置不变；tower两receipt判断改精确ACK/完整资产及成功batch序，其它warehouse断言未删。不是所有旧驱动/断言逐字原样。
- **原新10组实测:** Level真实[0,1,2]保UID/core/锁槽/完整帧，四次Toggle/Toggle/Unlock/Unlock首02CA/后三02CB；同seq Sort/Toggle实际再次执行业务，metadata原对象及Selector/Type/Sequence/Ordinal/Fresh/长度/SHA保。SQL真实item_slots INSERT RAISE(ABORT)19/1811在action抛原cause；native实际唯一SQLite participant FK1/child>0/完整sorted stage且Load/clock完成后才SqliteTransaction.Commit19/787，CommitFailure.Cause同对象。两路同run全部资产列/BLOBhex/NULL/events/deliveries exact回滚、失败batch零登记及原sourceclose/nofence，去trigger同seq正常再执行。Sort/Lock成功projection实际encoder与composer故障均Warning+Failed且已提交/caller正常；无Tx invalidpage直接ACK故障原raw cause。完整old/new stacks、metadata、bytes/batch/stage见before-observations、final-observations-02、oracle-before-after-checks及workunit-and-coverage-map。
- **empty committed ACK明确差异:** before证旧空TxCommitted/participants0、无world正常拒绝后ACK encoder IOException原对象raw；主控§10与fresh边界approved后同r1继续，无新门/Core/许可。新实际`TransactionOutcomeUncertainException`、Reason精确`PostCommitPublicationFailure`、InnerException同原cause，EndedAJob=true/完整root-parent-depth-source链。空TxCommitted/participants0、同run全资产不变、失败ACK零登记、无ledger/Main/attempt/fence、原顺序精确sourceclose一次。新final r2直接Assert.Same physical connection，JSON由ReferenceEquals产生。**旧empty只动态assert Single/IsClose/nofence，其hardcoded sourceCloseExact不是该case独立Same证据**；其它旧物理身份支持为共同sourceclose实现及旧SQLcase引用断言。旧56/driver不重造、不normalize成异常完全相同；比较器明确记录这一已审外壳差异与证据局限。
- **具名校准与mutation:** 跨run仅items.created_at/updated_at按实际runner UTC窗口/先后比较，**200处原字段出现**保run/row/path/原值；事件event_id/batch_id/origin_instance_id按非零GUID/跨phase三类双射，rollback瞬态与retry身份不合并，sequence/type/schema/payload/time/其它列精确、Delivery FK无孤儿且所有列/batch/origin关系保，raw全保；没有整表/GUID正则忽略。same-run rollback/empty所有列无豁免。Level→Default仅whole调用点可编译mutation，真实wire期望高level UID2却实际UID1为1行为RED，旁边锁序/default两正控passed；mutant SHA bbce074dbc7bd13b62d591113bb5e4bd386c579618e5d2cd81b51fb4b3c1ba93，finally全byte恢复，同3目标GREEN3/3，首次断言前输出完整actualassets。r2仅empty身份断言，mutation目标/产品未改。
- **freeze01诊断／final02:** 初轮freeze01 Release0/0、App5128/Protocol1773后Net199＝197pass＋2 Connection refused：`BossTowerFrameIntegrationTests.Raw_move_loading_kill_and_result_hover_use_registered_boss_frames`及`SelectCharacterCompensationCharacterizationTests.A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close`。保全部01 raw/stack/host日志/实际端口选择source；AGENTS登记偶发不涵TCP拒绝，未并入Dim/未登记新栈根，**根因未证**。相同freeze01两精确target诊断2/2，不能据此证明端口/环境根因。01自然结束后按授权仅empty物理身份强断言/derived JSON改freeze02，重新Release＋三full＋四controls；未改Networking/Host/timeout/parallel/Skip，不称App断言修复网络。
- **最终同freeze02验证:** Release --no-incremental真实exit0／0warning／0error；App **5128＝5110pass＋准确原Dim2fail＋16skip**（123.363s），Protocol **1773pass**（5.634s），Networking **199pass／0fail／0skip**（53.397s），三者均无filter、原xunit并行/5m/Skip、父runner自然结束。两Dim仍原Fusion/All_six displays、InvalidDataException/7100/maze0/(1,0)/Left。另Host/HttpApi/Infrastructure/Persistence各原control新跑1/1。原8883逐(display,status,multiplicity)全保，**N17＝App16＋Protocol1、D0、rename0/statuschange0**；加其余Task20实际full1800库存＝**8900＝8880pass＋2fail＋18skip**，不称本次八freshfull，四controls不额外加业务N。原DFO CLI expected实际exit0，原74（App62/Net7/Protocol1/另四各1）、七uniquecontrols、outside/exe/orphan0，新增intentional0；exe0仅观察。Golden15逐byte等HEAD/f01dc226，Characterization10逐display全pass/0skip。suite资源仅观察，非单CMD≤2秒。
- **capture／闭账／源:** fresh after173logical/203cmd，30Protocol＋1Internal＋172LegacyFine、144Tx＋46Direct＋9RO＋4Workflow，message41Protocol/132Legacy、telemetry14。差分仅3message routes及2fine→2whole，其它runtime/family/exits逐项全等；原三种exit和全部18个拥有者注册保持，新whole无exit。canonical仅五授权ID改变，其它group/baseline/original/row等HEAD、md原前缀保，pending118message/172fine/72source/5protection/8transition。preclose实际通过；Result后strict/原八selftest与八类落盘临时副本实际拒绝见ledger-strict／ledger-eight-bad／ledger-eight-bad-copies及日志。临时反例按原八语义改必要telemetry/transition行，canonical仍五ID、tool/基线只读。13989非owned库存仅排canonical两ledger，**13987逐SHA不变**；before42、r1 25140B及§10后27344B原前缀保持。source-final-checks核10当前＋2删除before SHA/receipt/通知字节，旧类型全仓仅测试缺席断言字符串。manifest-r1精确列实际全部source/evidence/log/脚本/Task/ledger及2删除before SHA，与Git changed+untracked闭合；Task Result前缀身份另记、manifest自身不循环哈希，无bin/obj。
- **Remaining:** required fresh独立结果快审、主控消费/本地checkpoint待；执行者不自批/stage/commit或建Task22。RecordedOutbound非A21真实socket关闭/实机验收；T-004余项、T-012单CMD≤2s、最终Spec/Astra审查、根封存及用户收尾仍后继，不称Spec2或根完成。不push/PR/merge/tag/版本/远端issue/游戏/部署/cleanup。Result/strict/manifest齐后停全部共享写与native，交fresh required review。


## Independent Quick Review R1 — TASK-21 r1／Result R1（2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

**身份及判据。** fresh独立结果审查，未参与本Task规划、生产或边界裁定；按reviewing-work的Task Unit＋Code＋Deliverable判据，实读Task r1/§10处置/R1全部、B3 RQ-001/004～009/015与有限raw/默认规则、P4工作单元与注入边界、当前AGENTS及两架构偏好。审查对象为HEAD `68f6d572f4b32cd2aa5987e373d889a8b3e0a33d`上的未提交局部结果；Task20在HEAD中有committed done、approved及主控消费。R1前缀**36067B／171c4216f8a3dc3d6d630512cbd7728977e9a4c46cbc5770e5c1a8004ef4d64d**，§10后计划27344B／a878a9e4…；freeze02 **03f9f30854ff9d254a2494ed4aaa7df3d41c993b9954f882a85c55050c3f3177**，manifest **012bc8b2c2a7d704918047b60152260ceb8bb0715a6ceacb665687ad691cea2d**。本批准不是Task-plan、完整Spec或最终验收。

**实际实现与原位置。** 已逐段读全部生产/文档diff及两个新whole，对照HEAD删除的fine与原handler。三logical／两个精确CLR绑定两个typed CMD，两个thin handler只交一次bus，旧fine/Result/handler/Tx注册及实际caller删除，receipt声明保持。原仅IAccount、无selected/settlement marker的空参与者范围有实际管线依据；新单位为一个ordinary Write([],None)，world/online/cargo正常拒绝仍在action，未加actor前置Safety拒绝、Main/足迹/账本/attempt/重放或恢复。Sort后character.Load→account.Load→clock仍在同unit，实际Load含LegacyWorn归一写，源码位置与forwarding observer同open写事务引用可支持这一点。Sort映射短路、Lock仅mapping异常静默、Toggle解锁和重复Unlock语义保留。原两个通知record/composer后缀逐byte核同，ICommandProjections继续TheseCharacters及单recipient prepared batch；NotificationBus普通composer/encoder仍Warning＋Failed隔离，调用者未新增throw/fence/close。

**原断言与新增保护。** 读完整原测试diff和新flow/本地driver/observer：原12Sort Fact及Warehouse其余用例保留，直接domain事务未强改；fine/手造handler驱动迁为真实注册decoder→dispatcher。stale原Receipt.Null改为精确失败ACK或静默并保资产目的，tower两receipt判断改明确ACK/资产/完整成功batch，不是仅未抛异常。查询proxy只在测试返回实际旧session，world仍查询真实registry；它不能证明wire携带旧session，R1边界说明准确。metadata原对象和全部字段/Hash、同序号继续执行业务、UID/core/锁/其他band/不合堆/回滚及ACK→snapshot→locks有现有或新增断言。新flow读取全部items列、无损BLOB hex/SQL NULL及slots/stats/参数/cargo/events/deliveries，没有System.Byte[]观测损失。

**真正before、SQL/native与对应比较。** 独立逐SHA核preproduct证据的既存产品/AGENTS等HEAD及两whole absent，old driver身份与42项before完整匹配；原old-oracles04真实56/56。最终raw中提取的新11条task21 observation与final JSON完全一致，旧10条也直接等原console提取。实读SQL注错/observer与实际记录：action19/1811在item_slots INSERT、sort未完成；native为同participant FK=1、child=3、sorted stage及两Load/clock全部完成之后SqliteTransaction.Commit19/787，Completion=ConfirmedRollback、原cause引用/全列same-run回滚/同seq再执行均有精确断言。独立截断比较器写报告部分，仅stdout重算10组对应、200个具名items时间字段；三种非零GUID跨phase双射、事件sequence/type/schema/payload/time/全部其它列、Delivery外键与batch/origin关系保持，rollback瞬态与retry身份没有合并；same-run回滚无任何豁免。随机身份生成点与持久化外键源码相符，未丢整事件或任意GUID字段。

**empty committed边界的独立判断。** 父RQ-023(c)及B3有限raw表与默认规则不要求此NonEffect空参与者分支取得第四raw权利。实际原、新均保一个空Tx Committed；新明确typed PostCommitPublicationFailure且InnerException同原cause、EndedAJob=true并验证完整链，零ACK、全资产精确无变、空参与者无新增fence。GamePlugin按EndedAJob而非IOException/typed分流，原成功投影隔离没有被该分支改变。旧before原raw断言与原source仍保，比较器明确把分类差异单列，未声称异常完全相同。旧empty的sourceCloseExact原为硬编码且仅Single/IsClose/nofence，不能冒作独立物理Same；R1已披露该限制及共同关闭实现/旧SQLcase支持，新freeze02直接Assert.Same且输出真实ReferenceEquals。该证据范围可支持本例现有默认收口，没有借它放宽其它出口。

**行为RED及检查身份。** 实读mutation记录、实际raw及恢复身份：Level→Default可编译3目标为2pass＋1真实资产顺序FAIL，断言前有actual slots/core，恢复同3/3及当前whole SHA一致，非编译/fixture/超时伪RED。最终Release真实0warning/0error；02三项目metadata为无filter/原5m/同HEAD，App5128（5110pass＋2准确旧Dim＋16skip）、Protocol1773、Net199全pass。逐display/status/multiplicity对Task20八full重算原8883全部保，N17/D0，含其余Task20实际1800库存为8900=8880pass＋2fail＋18skip；该1800及四control未被冒称新full或重复计N。两个Dim的具体InvalidDataException/7100/maze0/(1,0)/Left相符，Characterization10全pass。四fresh controls各1/1，原DFO CLI只读重跑exit0、原expected及七程序集集合不变（仅次序不同），74/7 controls、outside/exe/orphan0；Golden15同时等HEAD与f01dc226。

**网络诊断及剩余界限。** 读01两SocketException Connection refused身份/完整保留输出和诊断：BossTower首Connect与CharacterizationHost.Connect尚未进入业务，不能归入AGENTS登记的SQLite或内容偶发。同freeze01两target2/2及final02 Net199/199仅证明相应运行通过，不证明端口或环境根因；R1保留未知根因且未声称修复。两freeze仅App新empty身份断言有别，所有产品相同；未改网络源码、并行、期限或Skip，01没有被抹掉。RecordedOutbound非真实关闭socket、合成非A21、套件资源非单CMD测量的限制仍成立。

**闭账与范围。** 独立核179项manifest的完整bytes/SHA/删除before，恰等180个实际changed路径；10当前源＋2删除、42before、13989减两ledger后的13987非owned全部相符，无stage。canonical逐值仅3message＋2fine五ID变化，其余rows/baseline/组及md前缀不动。原validator strict、自带原8类及8份物理坏副本均只读重新验证，按原理由拒绝；不是弱化成五ID伪例。fresh runtime与pure family原始数据截断重算：仅3route/2fine→2whole、30P/1I/172Legacy、144Tx/46D/9RO/4W、41P/132Legacy；新无exit，原三exit种类及全部拥有者注册保持。

**结论。** 未发现本Task局部未解决阻塞缺陷，R1证据足够供主控消费后本地checkpoint。仅追加本审查，未更改产品/Result/ledger/证据或运行native/build/test；Spec整体迁移、最终审查/测量/封存、Astra与用户分支收尾仍由后继处理，本approved不授远端操作。


## Controller Consumption — TASK-21 r1／Result R1（2026-10-25）

主控完整消费Task r1/§10处置/R1与fresh Independent Quick Review R1 approved；review后前缀43280B／SHA `95f238859d1d4f4ea2a46a2d1cb66acec3edbf21019f3cb6a666e7a258c2aba4`、R1前缀36067B／`171c4216f8a3dc3d6d630512cbd7728977e9a4c46cbc5770e5c1a8004ef4d64d`保持。reviewer已停写；本局部批准不代Spec整体审查或测量。

主控实读全部产品/文档变化、两个whole与原fine/handler及原测试迁驱动diff，核原空participants/原拒绝/Load归一写与clock同事务/成功投影Failed隔离；只读复算12源（10现有＋2删除before SHA）、before42、13987非owned，freeze01→02仅新App empty身份断言/输出改变，产品全同。核179项manifest（含Task R1前缀与两删除）与180实际changed精确闭合，stage空。完整raw Counter独立重算原8883全保、N17/D0、8900=8880pass+2准确旧Dim+18skip；1800明确Task20库存。原DFO CLI再跑exit0、74/7controls/outside-exe-orphan0，Golden15逐byte等HEAD/f01dc226，Characterization10由最终Net02全量通过。原strict与原八物理反例用未改validator逐项重验按原理由拒绝，canonical仅五ID变化/md旧48209B前缀保持。

empty边界沿§10与fresh独立解释/结果复核保精确范围：新实测defaulttyped/同cause inner、空Committed/空participants、全资产不变/无ACK/无新增fence/EndedAJob/精确一次物理sourceclose；旧before原raw及较弱物理引用观察如实保留，不称异常或证据强度完全相同。主控另只读重算10组对应/200个时间字段及三GUID跨phase双射、所有其它业务列/字节/外键/批次关系，未将empty分类差异normalize为相等。无第四许可、无共享core变化或提交点改动。

首轮Net01两个ConnectAsync Connection refused不并入基线、不登记未经证实的栈根；同freeze01 target2/2及最终freeze02 Net199/199通过，根因仍未证，App身份断言并非网络修复，原raw保全。此限制继续随最终交付披露，不能宣称已修网络根因。

TASK-01～20 committed done/21唯一/依赖仅20及DAG先前就绪证明保持，当前HEAD仍68f6d572、唯一writer已停，允许精确owned集合本地checkpoint，不改源或重复已齐native。P4 T-004尚pending118message/172fine，另72source/5protection/8transition未关闭；检查点后再规划下一最小簇（已有Sell只读事实），不以21个Task代Spec完成。原A封存、后继最终核验/单CMD≤2秒/Astra及用户分支收尾、不remote/版本/tag/部署/cleanup边界保持。
