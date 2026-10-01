# TASK-009 — 无维护写的当前投影与整组编码发送

## Task Unit Contract

- Revision: 2
- Outcome: 为已提交操作的安全刷新提供受精确会话／同库事务保护的纯当前状态快照，以及在第一帧发送前完成整组验证编码的typed batch；不重放历史消费ACK或奖励增量。Plan T-006，RQ-008/009及AC-006/007/008的投影基础。
- Dependencies: TASK-008已独立检查并根接受：45／58、SelectedSessionSafety及精确connection/context边界实际可用。正式build0警告错误，Application702、Vending12、Protocol1218、Networking23、Persistence17全部通过零skip。
- Owned: InventoryEntry纯读接缝及范围内投影调用；bounded immutable replay read contracts/domain/command；typed当前刷新投影及限定exclusive生命周期借用；network/game batch与真实encoder／transport测试；本Task结果及coverage。
- Excluded: 不实施raw hash／operation ledger／主提交mark／effect footprint收集，不清历史业务guards（010接入后才关闭）；不改首次成功ACK／概率费用／通知顺序／zero crystal event过滤，不扩商城／战斗／宠物时钟，不commit/deploy/liveDB/PVF/client。

## Detailed Plan

1. 读verification/replay-design-findings.md和preparatory-findings.md及实际接口，确认所有read调用是否维护写；不把方法名Load当纯读证据。ICharacterInventoryEntryDomainService新增具名ReadCurrentAsync，生产只repository读取＋InventoryEntryProjection，不调用LegacyWornNormalizer。LoadAsync仍作selection entry维护。测试替身显式实现，不提供生产default到Load的隐式降级。
2. OpeningInventoryDomainService.ApplyAsync最终receipt改pure，RecipeCraftDomainService.RefreshAsync改pure（Craft成本读取本来是bags，勿误改）；SelectCharacterPreparationService.BuildResult(selectionEntry=false)的character inventory及CompleteSupportCharacterProjection同步改pure，selectionEntry=true保留维护。其他CeraShop／PartyMemberStatus／Sort查询具名记录现有边界，不趁本单位重写业务。真实legacy worn状态测试证明pure read不改库而entry维护仍有效。
3. Abstractions提供具名projection needs／touched physical slots和bounded snapshot契约，供010后继footprint使用；不含任意Func／旧receipt／历史增量。只存当前刷新所需事实，IReadOnlyList不等于immutable，复制pet name／avatar detail／skill extras与所有会在返回后引用的存储数组，使用ImmutableArray或等价不可变集合。Request type不能进入Application，也不能Protocol直接repository读取。
4. 新ReadReplayStateCommand为Transactional＋ISelectedSessionCommand，提供nonnull exact session；读取在45生命周期Shared→57串行→58→60单个SQLite快照内进行，前后DemandCurrent，禁止nested bus／Seeking.Synchronize／Recover／Reconcile／Ensure或pet Advance。沿现pure领域读取character/account inventories、character/quest/skills/builds、skill ledger/current XP、pet、fatigue／usage、premium等所需事实；可复用修正后CurrentView的pure部分，不能为方便搬入selection mutations。
5. 编码使用一次捕获At；fatigue与limited usage当前period纯读新增At接缝，原调用保留clock入口并delegates到实际At实现，跨日clock在await间改变也要使用同一次snapshot At；BlackDiamond extra fatigue本来仅configuration，勿发明expiry业务。premium rolling display如返回需按snapshot At构造。只宣称这些明确At覆盖的读时间，不声称冻结整个服务器时钟或改奖励估价。
6. Protocol提供typed current refresh service和可注册的Abstractions入口，接收原connection context／exact session／needs与touched slots；outer生命周期Exclusive覆盖单次read command至SendBatch完成，Read45借用其Exclusive下的Shared引用，禁止Shared升级。SelectedSessionTransitionGate新增具名BorrowOrAcquireExclusiveAsync，真实LeaseOwner记录实际模式；只能借同flow live Exclusive，live Shared明确拒绝升级，pending/canceled/closed不算持有。旧Shared借用与显式FIFO策略不变，回归007/008门测试。重放只返回当前绝对状态：000D库存全表（Main14 virtual含zero）及footprint当前空物理槽000E ItemId=-1；金币ItemId=0，不能误当清除；account materials当前余额，不重发扣除／获得旧popup。不要假定000D省略物理槽等价清空。010首次handler仍不持生命周期门；重放直接调用此Exclusive refresh，不能预持Shared再升级。
7. 当前pet0069保留UID/name/stomach等绝对值；skill0013固定两页；quest progress／offers只查询当前、不Report进度；XP当前level/total/SP/TP且ExperienceBonusLines.None；premium完整状态CMD036F；usage021E沿既有reset＋255 chunks。必须保留avatar kind8的detail，pet总数>255与skills/extras>u8明确拒绝，不能自创chunk。库存u16与quest ACK255不同。needs未知flag或非法touched slot必须明确拒绝。
8. INetworkMessageSender／IGameMessageSender增加具名SendBatchAsync(original NetworkConnectionContext,typed ordered messages,ct)。production按ID查entry后ReferenceEquals(expected)，只捕获该entry.Session；预检全部registered encoder／endpoint，整组所有实际frames完全编码、验证1MiB cap后才发送第一帧，发送始终使用捕获transport，不在replacement后改投同ID新Session。没有sequential SendAsync fallback或arbitrary raw-byte message。保持旧SendExplicit required/best-effort语义。
9. 真正behavior RED：现Opening/CurrentView receipt读导致Normalizer维护写（用实际legacy fixture）或旧sender逐个编码导致前reset已发、后pet/skill/avatar非法报错。不能把新增interface编译失败当RED。新snapshot与batch自动测试：当前而非旧值、空槽-1/金币0/virtual零、petname/avatar/extras冻结、scope wrongowner/stale与等待后替代拒绝、read失败无业务写、日界一致；晚非法message/encoder/endpoint/oversize全组零发送；相同IDcontext replacement不发送到新Session，发送途中断线可部分投影但不假称原子网络。
10. 逐部分复读所有新增未跟踪源码与实际read／send路径，证明没有维护写／历史ACK／奖励增量旁路。串行fresh build及Application current-view/inventory/selected snapshot、Protocol inventory/selection/refresh/encoders、Networking sender实际registry/transport regression；相关PVF设置既有archive路径，临时绝对DB/隔离socket，不启动用户服务。测试范围按实际触碰扩展一次，不机械跑无关全suite；最终全suite在011。
11. 写Result（实际TRX／RED／失败阶段／At边界／API／未完成010）和coverage，完成checks后明确停止writer/dotnet，再由根与独立只读阶段检查接受。不得把本单位接受写成全部AC-007已满足；010必须完成rawidentity、commit ledger与guards关闭，011必须最终整合验收。

## Readiness / Attention Check

008正式停笔，根读实际新未跟踪Safety／45／58及freshCounters和Fatigue actual入口；独立TASK-008-stage-audit approved。真实producer API、hiddenNormalizer接缝、所有wire约束与未知seq证据已经具名准备。目标仍是一个效果计划安全结算一次；本单位仅建立当前状态刷新基础，权限及下一集成边界明确，可以开始。生产与dotnet仅一个writer，其他审计只读。

### 实施中检查后的有界修正

Revision2：根与独立阶段检查沿实际Party MonsterDeath recipient命令／secondary NOTI调用确认：read command返回时57释放，outer Shared允许另一recipient Shared结算先提交和通知，之后旧snapshot可能覆盖新client状态；010同connection ledger不能串行其他reporter的3XP。故只把current refresh的outer lease改Exclusive，并增加真实模式借用，第一执行业务handler策略不改。必须用真实bus／SQLite和batch发送barrier验证XP／quest等recipient结算在读到发送完成之间被阻塞；现结构反例尚不等于已执行RED，结果如实记录实际运行。另需保留quest signed int的uint位模式（unchecked，与现progress通知一致）和Appearance纯SupportCreatureMarkerProjection补齐后freeze；不扩大FullSelection既有checked路径或BlackDiamond配置业务。


## Result

```yaml
kind: rks-task-result
decision: pass
claim: TASK-009 Revision 2 bounded current state projection and prepared typed batch
controller_acceptance: accepted
```

### 实现与后继 API

- Abstractions `Effects/IReplayStateProjection.cs` 提供 `ReplayProjectionNeeds`、`ReplayProjectionRequest(Needs, ImmutableArray<BusinessSlot> TouchedSlots)`、具名 immutable snapshot 及 `IReplayStateDomainService`／`IReplayStateProjectionDelivery`。Unknown flags、default slots、virtual addresses、无对应 replay 的 physical containers 明确拒绝。Pets/name、avatar detail/jewels、skill extras/combos、appearance/name 等 legacy arrays 在捕获和每次 outbound adapter 两个方向复制，内层集合冻结。
- Application 注册 `ReadReplayStateCommand` 为 Transactional／nonnull ISelectedSessionCommand，domain 要求 ambient transaction、读取真实 owner table、前后 DemandCurrent。只根据 needs 读取所需领域：Inventory-only 不访问 fixture 尚无实现的 skill ledger；不重播 selection entry／59 frames，不 nested bus、不调用 Seeking.Synchronize、Recover、Reconcile、Ensure 或 pet Advance。Appearance worn-pet marker 用纯 SupportCreatureMarkerProjection 和 snapshot At 完成，再 freeze。
- `ICharacterInventoryEntryDomainService.ReadCurrentAsync` 的生产实现只 repository＋InventoryEntryProjection；没有 default 回退 Load。旧 Load 的 LegacyWornNormalizer 保留。Opening final receipt、Recipe Refresh、Select current-view 及 support current-view 已改 pure；selection entry 主角及 support 仍维护。
- 新 `BalanceAtAsync`／`GetSnapshotAtAsync`／premium `ReadAtAsync` 接缝由原 clock 入口 delegates。snapshot 只捕获一次 At；fatigue、usage 当前 period/cooldown、premium usage period、BlackDiamond rolling display、库存 wire lifetime 使用此 At。BlackDiamond eligibility 和 extra fatigue 仍由 configuration 控制，不新增 expiry 业务，不宣称冻结所有服务器时间／奖励估价。
- Protocol 的 `IReplayStateProjectionDelivery.RefreshAsync(originalConnection, exactSession, request, ct)` 先捕获旧 recipient 和 original context，再借／取得 outer Exclusive，保护单次 readonly command 到整组发送。`SelectedSessionTransitionGate.BorrowOrAcquireExclusiveAsync` 依据 LeaseOwner 实际模式；已有 Exclusive 可经 Shared child 再借 Exclusive，held Shared 明确拒绝升级，pending/canceled/closed 不算 Held。Read45 借该 Exclusive 的引用，原显式 Acquire FIFO 和 ordinary Shared 语义保留。010 不可预持 Shared 再调用此刷新，也不可把首次业务 dispatcher 改成跨 handler 持此门。
- 刷新只包括需要的当前绝对状态：六张 000D 库存全表、Main14 virtual（包括 zero balances/金币 ItemId0）、footprint 当前空物理槽 000E ItemId=-1；0069 pet current UID/name/stomach、0013 固定两页 skills、023F current quest progress/visibility/offers、0025 current level/XP/SP/TP＋ExperienceBonusLines.None、0024 fatigue、021E reset＋255 chunks、完整 premium CMD036F 和必要两条 NOTI2 appearance。没有重复消费 ACK、奖励增量、原扣除／获得 popup。Quest trigger 保持既有 unchecked uint bit pattern，-1/int.MinValue 不误 overflow；quest ID 仍 checked u16。Inventory count 用 u16，不套 pet/skill 的 u8 限制。
- Network/Game `SendBatchAsync(expectedContext, typed ordered messages, ct)` 明确新增，不提供 sequential SendAsync fallback。Production actual registryReferenceEquals→captured Session；全部 registration/endpoint 和实际编码 frames／1MiB cap 预检完毕后才第一帧发送，每 frame 自有 copy。发送全程不再按 ID 查目标，保留原 Debug selector/type/frame/message 日志。旧 SendExplicit required/best-effort 行为未改。

### 实际检查

环境、完整命令和源码复读见 [TASK-009-checks](../verification/TASK-009-checks.md)。最终 Counters 已直接解析 TRX；重叠专项不累加覆盖数。全部 dotnet 串行，PVF 环境是既有 archive path，DB 为临时绝对路径，没有启动用户服务。

| 范围 | 实际结果 | 证据 |
|---|---|---|
| Fresh complete solution build，最后只改 Architecture test 后再构建 | 0 警告／0 错误 | [build](../verification/TASK-009-build.txt) |
| 完整 Application 项目（非过滤专项、非全方案） | 2567通过／0失败／0跳过 | [Application](../verification/TASK-009-application.trx) |
| 完整 Protocol 项目 | 1226通过／0失败／0跳过 | [Protocol](../verification/TASK-009-protocol.trx) |
| Networking actual batch／旧 Explicit／exact close／encoder registry／inbound counter | 21通过／0失败／0跳过 | [Networking](../verification/TASK-009-networking.trx) |
| 新 snapshot＋gate 专项，亦在完整 Application 中 | 23通过／0失败／0跳过 | [snapshot/gate](../verification/TASK-009-snapshot-gate.trx) |
| 旧 sequential sender 真行为 RED | 0通过／1失败 | [encoding RED](../verification/TASK-009-red.trx) |
| 旧 outer Shared read→send 真行为 RED | 0通过／1失败 | [ordering RED](../verification/TASK-009-ordering-red.trx) |
| 首次完整 Application 揭示前单位遗漏 | 2566通过／1失败／0跳过；随后修正 | [first full App](../verification/TASK-009-application-rename-classification-failure.trx) |
| diffcheck＋新未跟踪 source 复读 | clean；领域／Protocol／gate／sender／实际新 tests 已读 | [checks](../verification/TASK-009-checks.md) |

### 行为证明与修正边界

- 首个 RED 用实际旧 sender/registry/ITcpSession proxy：先 reset 已发、后 encoder 抛出；要求全组 zero frames 失败。GREEN 的 PreparedBatchTests 共10例覆盖晚 unknown encoder、endpoint、实际 oversize、实际 Protocol pet256／skills256／extras256／avatar missing detail、同 ID replacement、encoder 重用 buffer、captured transport。不是 interface 编译错误 RED。
- 第二 RED 用真实 Production CommandBus 的 ISelectedSessionCommand GoldProbe、45／57／UoW60、真实 SQLite carried gold，以及 typed batch pre-send barrier。旧 Shared 在 read 返回后允许另一 selected settlement 提交，要求其排队的断言失败；outer Exclusive 后同一用例 GREEN。实际 PartyXP secondary recipient→ApplyMonsterKill→XP NOTI 链是此次结构风险的只读来源，不能把 GoldProbe 实测称为 PartyXP wire 竞态实测；未扩通知体系。
- 真实 legacy worn slot24 snapshot read 不移动 row，entry Load 才 normalize 到25且 UID一致；真实 SQLite snapshot 在另一个连接更新 gold/account cube 后仍返回同一旧视图，下一次 fresh snapshot 返回新值。late read failure 保持库存和 gold。wrongowner/stale、未知 needs、unsupported／virtual slots、真实队列中的 session replacement 拒绝，且门没有引用泄漏。
- 跨日 At 测试在 awaited静态 rule读取中将 clock 推到新业务日：fatigue／item usage／premium usage 和 rolling display 仍读捕获时刻；lazy reads 不改旧 SQL rows。Protocol 实际编码验证 gold0／virtual零、explicit physical -1、occupied touched 不清空、数组两向冻结、quest高位、XP无bonus、premium CMD036F、库存256+及usage reset/chunks。
- 真实 registered Protocol delivery＋production bus＋SQLite＋actual encoders 证明 fresh inventory 和真实 equipped pet slot25 marker 的两条 NOTI2；fixture sender 为明确 batch adapter。physical registry、captured Session、frame cap、late encoder zero-send 由独立 Networking production sender tests 给出。发送 I/O 中断可能只发部分当前投影，没有宣称网络原子性。
- 首次完整 Application 的唯一失败是 TASK-004 新 RenamePetEffect.ItemUid 漏同步 singular world-instance 类型分类；不是已证明 accepted baseline failure。根裁定只在既有 IsWorldInstanceIdentity 增加 typeof(RenamePetEffect)，原 negative generic/pile checks 保留并加明确正例，不改生产 UID、不泛放 effect、不重命名绕 scanner。首次失败已保留，fresh build 后完整2567全过。
- 开发阶段新 Protocol fixture 的 collection-expression 并非 byte[]，改成真实 mutable source；Appearance fixture 原只含技能 rows，补齐真实 required profile rows，均是测试准备修正，不改生产业务。准备 batch 注册时 generic registration 覆盖实例的 fixture错误、account zero resource被生产省略的旧断言均在正式GREEN前修正，没有拿这些当行为 RED。

### 控制器接受

2026-10-01：实现者正式停止production／tests／dotnet后，独立[TASK-009-stage-audit](../reviews/TASK-009-stage-audit.md) approved。根复读实际Exclusive租约模式、当前投影调用／marker／trigger、Result／checks、最终TRX Counters（Application2567、Protocol1226、Networking21，全0失败跳过）与fresh build0警告错误、diffcheck clean，接受009。只接受本单位，父AC-007仍需010统一入口。下一详细Task由真实API、接缝及只读fixture检查形成。

### 未迁范围与交接

CeraShop receipt、PartyMemberStatus 仍调用 entry Load；Sort 使用 business container。它们是本单位明确排除的旧边界，不宣称全仓库 Load 已 pure。

没有 raw request fingerprint、operation ledger、commit mark、effect footprint 收集或历史 guards 退出；TASK-010 必须用上述 exact-context／request／snapshot／batch API 完成。AC-007仍未全部满足，TASK-011仍需最终全方案、整合遗漏扫描和集中 human acceptance；本单位未 ClientVerified、未 commit/deploy/push/liveDB/PVF/client。Result/coverage 写毕后由实现者单独明确报告停止 production/dotnet，控制器及独立只读检查拥有后续状态变更。
