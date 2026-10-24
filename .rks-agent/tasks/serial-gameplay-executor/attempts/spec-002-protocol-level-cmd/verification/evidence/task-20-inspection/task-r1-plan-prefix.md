# TASK-20 — CollectionBox 完整协议 CMD 与首次成功 batch 出口

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一有用单元：四个 CollectionBox wire decoder 汇入一个 logical `66533`、一个精确 `CollectionBoxRequest` 与唯一 `GameCollectionBoxCommand`；吸收旧 handler/fine 的完整用例，删净本切片 fine record/handler/注册/caller。原一个 Transactional Main、mandatory footprint、具名拒绝、逐条 clock、库存→完整 boxes/selected 重复→ACK 与一次 exact physical batch 保持。与消费者同时交付最小 `SettledInitialBatch`、穷尽状态及 batch/required adapter 矩阵，保第三具名出口的原 raw encoder 结果。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015及 RQ-013/017 的本簇记录/当前文档；AC-001～005、007～009/011 的局部义务，F-34/F-112。本 Task 不宣告 T-004、B 或根任务完成。
- Owned boundary: §2 精确15个生产/文档/测试物理路径、本 Task 和 `task-20` 新证据/日志；`migration-ledger.json/md` 仅 §9 两个 ID。旧 CollectionBox 领域/规则/存储/纯领域测试、原 NetworkSettlement cases、opaque 接口、Game outbox 透传、公共 support/transport、其余协议与 shared core 均只读。不能拆出无人消费的 enum 空壳，不能给四 opcode 建四 logical/whole，不能用第二 provider/outbox 或 fake whole 替代生产消费者。
- Required checks: 按 §3 真正产品前 fresh capture/family/source SHA/UTC，然后真正旧 decoder Select/Put 固定 actual encoder cause 直接 oracle；新 whole 同 provider 对应复跑，直接验真实 entry/原 Ordinal/Committed/无新 fence/整失败 batch 未登记/source close/current replay。保原拒绝、纯业务/PVF/SQL/metadata/字节/帧序/物理目标；新增成功 Put→Withdraw wire、真实 replacement 发生事实与拒绝对照。§4 先真实 new request catch 后 actual SQLite write/return guard probe，不能把静态风险当已证 bug；若实证所需修正越界，依 §10 返回主控。§7 全 phase/来源/默认保护/两旧许可/P3/独立清理矩阵，至少一个有效可编译 provenance mutation RED→恢复 GREEN。最终 freeze 上完整 Release 0警告0错误＋八项目无 filter full，原并行/5m/deadline/Skip、DFO 原74＋七 actual controls及新增 intentional 逐类、Golden15、Characterization10、原8828逐 display/status/multiplicity Counter N-D、capture/strict/原八坏副本与完整路径 manifest 全闭合。
- Dependencies: 仅 [TASK-19](TASK-19.md) revision 2／Result R1／fresh required 独立 approved、主控完整消费并 committed done，产品检查点 `dba3ede68e15f892afd35534db9fed7ed00d04a8`。其已接受共享骨干、唯一 ledger/typed driver、P3 commit-only/来源/RequireRollback 保因及旧两许可为传递输入，不重开其 Task/review。B3/P4 匹配 gates 和 amendment-only 检查点 `d6708faf9b544662fc9d976dba369fbf410f719f` 是已接受的当前治理输入，**不是新增 Task 依赖**；Spec baseline 仍 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — shared phase/provenance 或 caller sticky 错误会让提交后普通业务故障免围栏、foreign attempt 的新调用者 catch 后继续写，或令已提交 CollectionBox 重复移动资产；codec、编译与总数量不能证明这些状态。fresh 独立结果快审须核真正 before 与 actual encoder、两端 guard、失败阶段/完整资产/清理发生事实、raw/typed 矩阵、非owned边界与两行闭账；主控消费批准后才本地检查点。主控做 Task readiness，不另加独立 Task-plan review gate。

## Detailed Plan

### 1. 接手身份与执行次序

唯一 workdir 为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`。接手 HEAD 为 `d6708faf9b544662fc9d976dba369fbf410f719f`，parent 为依赖产品检查点；此 amendment-only 提交未改变 Server tree。主控已证明 TASK-01～19 唯一 committed done、DAG acyclic、20 unused、简短 goal check aligned，index 空且所有 writer 已停止。执行者复核这些当前身份、owned 状态及唯一共享写者；不自行回退历史。

治理输入为 B3 `32948 bytes / SHA-256 1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`、P4 `61551 bytes / db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`；对应 approved review 是 `reviews/spec-b3-review.md`（`42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52`）与 `reviews/plan-p4-review.md`（`82611a76c0d4ee0dd264357dbd4c8d2c164050b919bc14e593cdac8ee01bbbbb`），主控消费为 `verification/spec-b3-plan-p4-controller-consumption.md`，routing 已指向 B3/P4。依此实施，不复用旧 B2/P3 gate 代替新门，也不重写历史。

执行者先实读上述目标/消费、当前 AGENTS 与两架构偏好、TASK-19 结果/快审/消费及相关源。工作区当前没有 `.codegraph/`，故跳过 CodeGraph；若接手时已有索引则遵守仓库先 CodeGraph 的规则。继承根授权的中文、本分支本地实施/验证/主控 checkpoint（Co-Authored-By）；执行者不 stage/commit，不 push/PR/merge/tag/Release/版本递增/远端 issue，不运行 df_game_r/DNF.exe、部署或 cleanup，不创建 TASK-21，不改 A/root/旧 Task/review/证据。

已接受库存为 **8828＝8808 pass＋2 exact 旧 Dimension fail＋18 skip**。最新三个 full 为 App5058（5040 pass＋2 fail＋16 skip）＋Protocol1771 pass＋Networking199 pass＝7028；其余 **1800＝Host203＋Http57＋Infrastructure976＋Persistence484＋Pvf80** 仍 Task15 库存，含 Infra2skip，不是本 Task 新八 full。当前 capture 是173 logical/203 command＝27 Protocol＋1 Internal＋175 Legacy，147 Transactional＋43 Direct＋9 ReadOnly＋4 Workflow；message37 Protocol/136 Legacy，其中 telemetry14。

执行顺序固定为：最小 before/source 与 fresh capture/family → 未迁旧 route 的直接 oracle、guard probe与必要旧故障基线 → 最小 shared/whole 改造 → focused 新旧对应与安全矩阵 → provenance mutation/恢复 → final source freeze → 新完整 Release/八 full → Counter/DFO/Golden/表征/capture/源边界 → 两行 ledger/Result/strict/manifest → 停写交 fresh required review/main consume。普通 fixture 修正留诊断，不为每次调查增加新 gate；只有实证否定方法/必要 scope 或白名单问题才返回主控。

### 2. 精确白名单：生产/文档9＋测试6＝15物理路径

路径均相对 workdir。没有授权整目录，内嵌 fine 的移除不等同删除整个切片文件。

| 路径 | 允许职责 |
|---|---|
| `Server/src/Dfo.Server.Application/Commands/CommandRegistrationAttempt.cs` | enum 加通用 `SettledInitialBatch`；将 Enter 收敛为具名 batch/required-explicit 入口与私有校验核心；保 owner/job/target/single-call/来源，不加 public phase/caller 签章能力 |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs` | DemandRegistration 穷尽 switch；共同 committed/exact target/Main 已结束条件及三个真实阶段；未知 fail closed，其余 Main/footprint/ledger 状态原样 |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs` | token batch 用新具名 batch Enter，required 仍固定自己的具名入口；保 actual encoder 窄 catch、普通重载/查找/Register/BestEffort/关闭语义；仅 §4 实证需要时补此现显式边界的 sticky |
| `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs` | 仅为新 exit 补 Protocol/effect binding compatibility；保原两 exit、mixed-family、unknown/duplicate/freeze 规则，Application 无 CollectionBox CLR 名单 |
| `Server/src/Dfo.Server.Application/Commands/CommandRequestContext.cs` | 将**现有 DemandCurrent()** 改为 public，原 job/closed/Healthy/authority/未完成 attempt 校验体不变，供 whole 前后复用；sticky 修正只限 §4 实证必要处；§8 provenance mutant临时改后逐 byte 恢复。不得公开更弱 DemandRegistrationCurrent、造新通道或改终判模型 |
| `Server/src/Dfo.Server.Protocol/Game/CollectionBox/CollectionBoxSlice.cs` | 删除内嵌 `CollectionBoxCommand`/handler；`CollectionBoxHandler` 薄化并加 Protocol marker；错误映射随用例移入 whole。request/minimum、四 decoder、ACK/Contents encoder/Full 保原 bytes 或仅必要 using/具名搬移，不改语义/字节 |
| 新 `Server/src/Dfo.Server.Protocol/Game/CollectionBox/GameCollectionBoxCommand.cs` | 唯一 whole record/handler，显式 Main/footprint、窄 named rejection、current、原逐条组帧与成功 initial batch/拒绝普通 batch |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | logical66533 精确 `CollectionBoxRequest` 绑定唯一 Direct/Protocol whole，声明 `CommittedReplayCurrentStateBatch`＋`SettledInitialBatch`，删一条 old fine Tx；四 decoder/原 encoder注册保持 |
| `AGENTS.md` | 仅当前 CollectionBox opt-in/旧 fine 删除/原Main/第三具名出口与默认保护事实；将当前“两出口”描述就地对齐，保其它协议/历史边界，不写过程或指向 .rks-agent |
| `Server/tests/Dfo.Server.Application.Tests/Commands/ExplicitEffectCommandTests.cs` | 扩现 synthetic registered Matrix probe 的三 phase/callkind/target/生命周期/两端 sticky及独立清理矩阵；实际 SQLite 写与终局断言，原所有反例保留，不替换真实 CollectionBox logical |
| `Server/tests/Dfo.Server.Application.Tests/Commands/ProtocolCommandExecutionTests.cs` | 新 exit 的 registry兼容/freeze/unknown/duplicate 直接正反例；保原 required全effect/replay至少一effect及 mixed-family 语义 |
| 新 `Server/tests/Dfo.Server.Application.Tests/Sessions/CollectionBoxProtocolCommandFlowTests.cs` | 本族旧/新 decoder whole flow、固定 encoder cause/SQL/native/完整资产/entry/sourceclose/current、成功 Withdraw、physical replacement、业务默认保护/来源mutant目标；所有 local observer/DDL/driver 留此文件 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy fine marker 名单只减 `CollectionBoxCommand`；request minimum及其它全部名单/断言保持 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | legacy fine 名单只减同一项，其余保持 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 四 wire→同logical/唯一whole/exactCLR/Direct/Protocol/仅两个指定exit、fine类型及注册零证明，原case保持 |

实际 caller 核实：fine 生产只在 `CollectionBoxSlice.cs` 原114与 `ProtocolModule.cs` 原331；测试无直接 new fine/handler，只两处 marker 字符串名单。`Characters/CollectionBoxTests.cs` 直接真实 domain＋Tx，纯 Throws/PVF/metadata/SQL/满包NoRoom/noMail/Extend不消费目的保持，完全只读，**不新增 selected-session/footprint/domain driver**。原 `Sessions/CollectionBoxNetworkSettlementTests.cs` 全 case/断言只读；其手造 request 的 counter 不与新 flow 的 SendRaw counter 混用。

只读 `IGameCommandOutbox.cs`、`ICommandMessageOutbox.cs`：已有 attempt batch/explicit 重载足够，Game 只把同一 opaque 对象传下去。只读公共 `Inventory/EffectRequestTransport.cs`：新 flow 直接 `SendRaw`，本地写四种 body，不给 Body switch 添公共功能；只读 RecordedOutbound/OutboundRegistrationParts/ShopFixture。只读 CollectionBoxDomainService/RuleService/Abstractions/SelectionTable/ItemDelivery/所有 Persistence/schema、CommandBus/WorkUnits/ledger/reader/sink/GameplayJob；后者若成为实证必要修改，必须 §10 task-rework，不默写。

### 3. 真正产品前 source、registration 与旧 route 直接 oracle

任何产品编辑前创建新 `verification/evidence/task-20-inspection/` 与 `verification/logs/task-20-*` 前缀；每轮 before/focused/mutation/formal 用新编号，输出不存在才开始，不覆盖旧记录。保存15路径现有文件原 bytes/BOM/行尾/SHA，两个新路径预期 absent；记录内嵌 fine/handler 将删除的精确区域及 before SHA、非owned tracked SHA、HEAD/branch/index/command/cwd/真实 UTC。只保本 Task 最小 before 源与实际旧 driver，不复制整仓、bin/obj 或旧证据。

在 products仍逐 byte等HEAD、new whole不存在时，用既有 `verification/probe-tools/migration-ledger.py capture` 和 Task19 pure runtime-family sidecar方法新鲜采实际注册；只 ConfigureServices＋构造 registry，不 BuildServiceProvider/启动业务。原工具只读，新的 task20 runner/check脚本只放本 Task evidence。保存 capture JSON、pure family/exits、源证明、命令/UTC/exit/完整日志；先核 §1 起点数与全部已迁 family/exits，再增加 owned 测试，不用旧 Task19 capture 冒本次 freshbefore。

随后只增加新 flow/必要 local观察，在**真正旧 decoder→dispatcher→legacy fine/Main→SQLite→同 provider Registering/actual outbox/encoder** 路径实跑至少 Select4 与 Put1：

1. 每 case 固定唯一 IOException cause 对象，用现 FaultingEncoder 的实际 EncodeBatch 注错入口与 local计数/target/message记录，证明已进入实际 encoder 而非 callback/transport；断 `Assert.Same(cause, thrown)`，不能只 Throws IOException 类型。同 provider的 INetworkMessageSender/ICommandMessageOutbox alias同 Registering，IGameCommandOutbox沿实际adapter，不能另建容器或把fault移到发送端。
2. Select raw body `[4]`/type03E5；Put body `[box,0,sourceSlotLE,itemIdLE]`/type03E6，使用完整 Game metadata/raw length/hash、真实 all-frame counter与原 physical context。本地明确 seed/catalog、输入sequence、ordinal/fresh、decoder产物/action，不能手造 request 后直接dispatcher替代真正decoder。
3. 通过真实 ledger唯一 CWT 的只读观察提取实际 entry，记录引用、原Ordinal、Selector/Type/Sequence/Length/Hash、Projection/最低needs、Committed；在首次操作、失败后及 replay之间证明同entry/原Ordinal。新 whole observer只typed转发实际生产handler；旧路径可由实际领域singleton forwarding及ledger观察，不给real logical另注册fake whole。
4. 实际持久结果已生效（Select selected4；Put Main少1/Box增1、完整metadata/UID保留），整个失败batch未登记；保此前已登记条目的顺序和末尾exact source-close，直接核 selected session/connection fence没有新增。记录 actual encoder次数、batch/entries/frames/close的精确 physical引用，不能仅凭持久结果间接推Committed/nofence。
5. 外部独立setup事务改变当前 selection/boxes/库存，再用同raw/sequence重发，实际当前 reader被调用、原Execute调用不再增加、无原ACK/不再移动第二件，当前Full及Inventory适用投影/帧序正确。重放沿原ordinaryWrite、MirrorMerge→Create→batch→Town，不重复Main。RecordedOutbound.Discard不真实关socket，这只证明账本/围栏/当前投影，不称真实socket关闭后仍可收包。

保完整raw输出、观察JSON与最小旧测试/driver源码，包含所有item列/slots/selection/适用事件冷却。**所有 BLOB 无损 hex，SQL NULL维持NULL**，不能把byte[]经Convert.ToString写成System.Byte[]，也不能用hex(NULL)空串抹NULL。完整item_core及可空扩展都实际读取；跨run只允许具名 `items.created_at`/`items.updated_at` 按实际SQL记录时间关系比较并保每个原值/row/run及真实UTC窗口，不能忽略整row/core/全部时间。每run rollback前后全列含这两时间仍精确相等；其余确定字段、body/hash/bytes/UID/数量/receipt等逐字段或byte。before缺口不得迁后回填；若真实oracle否定方法，保证据报主控。

### 4. 共享 guard 风险先以实际 new request 辨明

静态事实仅为风险：现 `CommandRegistrationAttempt.Enter` catch只对attempt.owner Failed；`DemandRegistrationCurrent`遇closed/job失配直接throw；`BeginRegistrationAttempt`部分permission/state守卫也直接throw。现 PreviousAttempt不catch、liveforeign只看ownerdrain，均未证明新caller catch后实际持久写/正常return会被阻止。不能写成已证bug，也不能假定已有测试充分。

在同 provider的现 Matrix synthetic registeredprobe中扩具名模式，保真实协议request/context/唯一queue/outbox。先旧owner完整request成功/closed的previous token，再真正新request在自己的handler中catch同token登记guard；分别尝试后续ordinaryWrite的实际SQLite金币等可观察写，并尝试正常return。另以现明确恶意cross-job fixture保liveowner/foreign token，foreign caller须是真正新显式request，保两job/request/entry/closure引用、guard cause、ownerdrain及两端终局；不能只用独立TestGameplay收到throw冒当前newrequest终局。合理的有界Ready/Release仅调度现fixture，finally释放并等两端自然收口，不增GameplayJobHost。

观察：实际SQL delegate是否进入、staged/durable值、request.Failure/Closed、旧ownerstate/drain、当前requeststate、typed/raw/每个participant fence/sourceclose。至少区分catch后write与catch后return两种；测试不能以计数器代实际持久写。再对Begin permission/unknown phase/state/unit未结束等guard，用本次request显式调用后catch再写/return，辨明是否sticky；与alreadyfailed/unhandledthrow原case保持区分。

若现其它守卫已阻止，保真实区分证据与反例，不为假设加代码。若实际RED允许继续，仅在 §2现有request/attempt/adapter显式边界最小sticky修正，保持原cause/两端拒绝与所有既有保护，重新跑同actualwrite/return GREEN。Begin自己的guard可在本request边界记录失败；foreign问题若现界面**无法获得当前caller的安全显式失败落点**，不凭token.owner冒充caller，不悄加ambient/DIcurrent/jobslot/从事务取request/第二host，不滥用AssertNotOnExecutor/selfwaitfault产生另一类错误。此时保RED、精确源码与最小新增API/文件方案（例如现GameplayJob的ProgramError/DemandHealthy挂点确有必要才提出）交主控 task-rework，未批准前不修改 `GameplayJob.cs` 或其它白名单外路径，也不带缺口交付。未知fixture本身不预设blocked，普通可达性诊断继续有界修正。

### 5. 最小共享三阶段与真实 adapter 闭合

`CommandRegistrationExit`只加通用机制名 `SettledInitialBatch`。Registry对新项要求已注册 Protocol且绑定为effect形状；与required一样不能赋给含non-effect的mixed family。原replay“至少一effect”兼容面保持；unknown值、duplicate exit、非Protocol、空/错binding均拒绝，不把新exit声明本身当来源证明，不在Application识别具体Protocol类型。

`ExplicitEffectRequest.DemandRegistration`共同先查exact物理对象、Entry.Committed、Main已结束，再穷尽三项：required＝非replay＋settled；replay＝IsReplay＋其ordinaryWrite已确认；initial＝非replay＋settled。未知default抛，不能维持“非required全部replay”二分。request签发仍验证实际注册许可、当前job/健康/未closed、无unit/未完成operation、无未完成attempt；不创建永久exemption/success receipt或第二凭据框架。

Attempt采用内部具名 `EnterBatch` 与 `EnterRequiredExplicit`（局部拼写可等价），共享private校验核心：batch仅initial/replay、required仅原required；phase由sealed内部attempt携带，actual调用种类由adapter固定，caller没有public phase参数。保request/job、exact NetworkConnectionId及ReferenceEquals物理target、entered/Completed单次、state阶段与全部失败传播；未知phase/callkind fail closed。

ActualOutbox token batch改用EnterBatch，required改具名required入口；普通无token overload不变。Game→Abstractions已有opaque参数保持**原同一对象**，不新接口、不包装可任意业务Func、不绕Registering直取底层adapter。只有现实际 EncodeMessage/EncodeBatch窄catch内部记原EncoderFailure并原样throw；typed转换、callback、lookup、取消、outbox.Current/target/buffer/Register均无编码章，不扩大catch来源。

保持现CommandBus一次终判：body记失败→scope活着时drain全部已启动/未await工作→Close→scope cleanup记录独立发生→最后一次FailAsync→原outbox条目/来源关闭。raw仍需最终唯一同encodercause、无typed或额外独立error；同一对象在unit/scope再次独立抛出也撤销raw，正常传播不重复计数。原默认Write/ReadOnly sticky、P3 CommitRolledBack只actualcommit同来源/同phase/ConfirmedRollback/全cleanup且无guard、RequireRollback两因保留、Main claim不复位、Freezeprojection不强清、Compose/F-132禁用均不动。

**前后current只有一个真源。** 主控已明确授权在RequestContext将现DemandCurrent公开，保其完整body及更弱DemandRegistrationCurrent为internal；whole在原前后位置调用同一explicitrequest实例。不新增接口、locator、注入豁免或请求通道，不在whole复制StandingOf/online/account/currentcharacter组合判定，不通过第二Write或提前BeginAttempt替代。直接反例验证closed/job/Healthy/authority/unfinished attempt仍拒绝，且原同request/recipient检查由此复用。

### 6. 一个 whole、一个 Main、原业务/组帧位置

选择 `GameCollectionBoxCommand(NetworkDispatchContext Dispatch, CollectionBoxRequest Input) : IProtocolCommand<bool>`，Message为原Input、Frame为原Game metadata。bool只表示本次成功分支完成（true）与静默/拒绝（false），不是ledger/提交证明；thin handler只构造/await同bus并忽略结果。如此whole泛型始终notnull，不用nullable CollectionBoxMutation作协议结果。局部action结果可用具名sealed outcome携mutation或原error，保实际成功分支可判定，不扩为共享泛型框架。

whole构造器仅现允许的 `ISelectedSessionDomainService`、`ICollectionBoxDomainService`、`IInventorySlotMappingRuleService`、`TimeProvider`、`ICommandWorkUnits`、`IGameCommandOutbox`（如实际需日志才ILogger）。不注入SelectedSessionSafety、bus/sender/closer/registry/locator/内容或规则；不加domain recorder参数。按本连接解析actor，actor null保原静默；初始ProtocolEffectRejected不因后到selection升权或claim Main，保持无原成功回包。其它request变体、捕获Selected与actor不一致为程序错误；用显式selected.Recipient，不再implicit Capture。

原前置current在mapping/主单元之前，以request.DemandCurrent调用。Put把Main wireSource映BusinessSlot（原mapping异常→0x15）；其它action不强行映source。不要把domain中owner/catalog/selectedbox/item/锁/票券判定提前。前置mapping catch只包mapping；拒绝普通batch在catch外，后续encoder抛同类型不能误入业务catch。

有效分支唯一 `units.WriteAsync(context, new(...,[selected.Recipient], MainSettlement), ...)`，**不用WriteResultAsync/CommitRolledBack恢复本族**。action原序为unit.Footprint.DemandActive(actor)→actualboxes.ExecuteAsync→仅Inventory.Count>0时RecordInventorySlots(actor,changes.Slot)。DemandActive与RecordSlots/runner守卫不纳入业务catch，不能被ArgumentOutOfRange/Mapping catch吞；mandatoryfootprint不是no-op。Select最低CollectionBox，其余Inventory|CollectionBox来自原request；Box持久坐标list241编码不是普通inventory槽，不把它们交RecordInventorySlots。

窄try只包原实际domain Execute，catch四类保原过滤/映码：CollectionBoxRejected→原ErrorCode；ContainerAdditionException仅NoRoom→04；ArgumentOutOfRange与InventorySlotMapping→15。catch保存原cause并 `unit.RequireRollback(cause)` 后正常返回拒绝outcome；RequireRollback自身guard不再catch。一般SQL/native/普通E/cancel/typed继续原异常出口，不新增StorageFailure/ACK恢复，不外catch整个WriteAsync。主单元完全结束后再登记拒绝；前后current均在原业务catch外。错误码原样：Select/Extend的domain拒绝5F；Withdraw MissingItem01/其它15；Put UnknownBox5F/MissingItem12/LockedOrTimed17/其它15；NoRoom04、mapping/range15。

actual domain只读保持：先Session→BelongsTo，Extend永久拒绝在catalog前、不耗ticket。Select写selection→ApplyDecidedWrites→ReadFull；Put TakeMain1原record→FlushMain→Put同record到Box→读Main剩余→writeSelection→Apply→Read；Withdraw Box.Take原record→InventoryOnlyDeliver（noMail）→changes→writeSelection→Apply→Read。domain无自Rollback；ItemDelivery catch只在未settled时RequireRollback再throw，不能套Mailbox自行settled模型。保完整UID/core/细节与真实Tx/facets。

Main结束后原二次current仍先于组帧。真实成功mutation才按原顺序：逐Inventory change `ToGameSlot`→按item/clear建立帧，**每条分别调用一次原clock.GetUtcNow**→`CollectionBoxContentsMessage.Full(state)`完整boxes按boxid及selected重复→原ACK。循环mapping/clock/Full均在Main和业务catch之外，不统一时间戳、不移入unit或encoder许可span。全部业务组帧完成才 `request.BeginRegistrationAttempt(SettledInitialBatch, command.Dispatch.Connection)`，以原exact context一次RegisterBatch；拒绝用普通无tokenbatch，无initial许可。失败batch不能拆成逐帧/先库存后ACK多个登记。

Module四decoder03E5/6/7/8仍同logical66533，注册一个精确CollectionBoxRequest binding，exits明确为原replay＋newinitial，无required许可。fine内嵌record/handler与Tx注册删除，两marker只减本fine；不留alias/Obsolete/legacy转发。默认replay由同bus已接受分支读current/ordinaryWrite/投影，whole不自己二结算或借replay开初次许可。

### 7. 真实 wire、失败阶段与 shared 安全矩阵

新flow的localDefinitions可沿原两box1/4；`ICollectionBoxDomainService` observer同时转发ReadAsync/ExecuteAsync至同actualsingleton，记录实际Tx/阶段/资产，不复制业务；replay Read真实可观察。whole observer只包装actualtypedhandler，不改生产logical注册归属。所有setup在独立TestGameplay/Tx中完成，SendRaw在外部入站，不在TestGameplay再排whole；同fixture用一个真实transport counter，不混手工counter。

**主路径与兼容。** §3 Select/Put旧新同raw/cause/seed对应重跑；保原NetworkSettlement全部case和断言，不改手工driver求绿。新覆盖四action真正decoder成功/拒绝及NoSession/初始Rejected late-selection、精确actor/最低needs/一次Main、namedcatch分类与普通E不ACK。新增**成功Put→Withdraw**：实际成功Put后真实03E7撤回同原record；比Main/Box/selection/UID/core全metadata、Box清空与库存落位、库存→Full/selected→ACK、逐条clock；再改变世界后同序号currentreplay无原ACK/不再撤第二次。Full bag NoRoom/noMail与Extend不耗ticket由原pure/PVF测试full继续保护，必要wire对照只新增，不删除Throws或用whole拒绝替代pure异常。

补真实physicalreplacement观察：原BeforeSend hook依赖Batches.Count但未证明触发，原case保只读。新case在**实际callback次数/阶段**明确替换同ID物理context，断Connections.TryGet确为replacement、hook真正执行、oldcontext不是replacement、batch每条仍oldcontext且replacement无收件；与replacement前失去current/targetguard拒绝对照区分。原whole后current与initialtargetguard各触发点需按实际位置说明；不把注册entry叫真physical送达，不以测试名代发生事实。

**两种真实SQLite故障，按失败成本取最小Put路径。** 在旧route先跑、迁后同provider重跑Put的一条actionSQL与一条nativefallibleCommit，不为形式整齐把每action都做native：

- action：actual `character_collectbox_state` INSERT…ON CONFLICT UPDATE是UPSERT，fixture `BEFORE INSERT` trigger可覆盖已有行/冲突；Put已Take/Flush/Box.Put后selection写时RAISE(ABORT)得到Sqlite19/1811。实际participant同连接记录Main/Box staged物品及selection阶段/原cause/stack；这不是nativeCommit。
- native：fixture临时DEFERRABLE FK parent/child，actual selection UPSERT后的trigger添child；在当前真实SqliteDatabase participant验证PRAGMA foreign_keys=1、child存在、完整mutation/Apply/selectionstage已成功，随后真实SqliteTransaction.Commit报19/787并确认rollback。只从当前participant读取staged事实，外部ShopFixture.Sql/Scalar只seed/DDL/前后durable，不手插外部坏行/fake participant冒commit。触发器须按实际UPSERT路径证明可达，不猜只挂UPDATE。

两路原一般SQL均不在namedcatch：原raw cause外抛、ConfirmedRollback/无ACK/整batch未登记、原sourceclose/无新增fence；真实rollback恢复items全列/BLOB/UIDslots/selection/事件等，去trigger同fingerprint同entry原Ordinal retry只成功一次，再同seq只current无原ACK/重复移动。nativeFreeze后projection可能非null，保事实、不清mainclaim/Ordinal。未实跑前只称方法可达，不把旧domain directSQL或provider方法probe计协议通过；routinefixture问题保诊断再修，有证据否定要求才报主控。

**三phase安全矩阵。** 以现registered Matrix probe扩机制反例，真实CollectionBoxflow证明actualconsumer；不能在真实logical注册测试fakewhole。至少覆盖以下可区分观察，保所有原case：

| 情况 | 需要看到的结果 |
|---|---|
| initial实际encoder，Select/Put正控；成功Withdraw若补encoder则另列实际结果 | 同原cause/raw、Committed、原entry/Ordinal、失败batch未登记、无新fence、sourceclose/currentreplay，不外推所有action已注错 |
| 同IOException来自Full/clock/库存mapping等业务组帧、current、lookup、wrapper callback、guard、Register | actualencoder未发生/无来源章；已提交按默认typed/全participants fence保cause，拒绝未提交按实际默认出口；不把类型当许可 |
| 拒绝batch/其它协议/ordinary无permit或错permit | 无initialraw；保是否已提交对应的默认保护；registered许可与实际encoder来源是AND |
| 无许可、未知enum/phase、首次借replay、replay借initial、required借batch、batch借required | 启动或本次guard拒绝；零越界编码/登记/后续持久写，catch不能成功；不改变原两出口正反例 |
| 错request/job/physicaltarget/调用种类、closed/leaked/single-call reuse | §4观察两端终局与实际SQL；已有未处理throw不代catch后禁写证据，exact旧target不误fence replacement |
| encoder失败业务catch后再unit或正常return | sticky保原cause、实际SQL不变、最终失败，无复活Main/clearfailure |
| independent unit/scope cleanup额外E，及同encodercause对象在另一阶段独立再抛 | raw撤销、typed/aggregate保每次发生/各cause，全部精确participants fence；正常传播不误算第二发生 |

Full/clock业务故障可用localforwardingobserver/有界TimeProvider在actualdomain已返回后制造并证明发生位置，不将其塞encoder。guard/Register等core位置沿可实际触发的现fixture精确注错；不能为取得表面覆盖改publicsupport。unit/scope cleanup借core Matrix原Dispose/rollbackgate形状，initial/replay/required均有适用对照；真实多participant Main默认fence保另一账号精确recipient。scope仍活着时drain、Close后dispose、最终raw判定及独立同对象发生记录实核；原MonsterDeath/祭坛typed发布、defaultWrite/ReadOnly sticky、P3GuardFailure/RequireRollback两因/commit-only门随Appfull保持。

### 8. 有价值 mutation、源冻结与八项目正式检查

最少一条**真实provenance**可编译mutation，首选仅将 `CommandRequestContext.RetainsRawEncoderFailure` 最后实际EncoderFailure同引用AND错误放宽为 `attempts.Any()`。新真实CollectionBox initial成功batch在Registering.BeforeSend抛固定同IOException：正品必须证明callback真正发生、actualencoder未调用、资产已Committed、默认typed＋精确fence/保cause；mutant应错误raw/no新增fence而使该行为断言RED，旁边actualencoder正控保持可达。测试输出证明错在来源保护，而不是编译/注册/fixture/超时。若该局部mutant因实际实现形状变化不再有效，按同一风险选择最小可编译真实guard放宽，记录具体事实，不以未触发冒RED。

finally恢复mutation前bytes/SHA，重跑同目标及正控GREEN，确认无遗留mutant再freeze。结构归并已有强wire/注册断言，不为纯搬迁另造无价值RED；whole固定BoxId1使Select4错SQL/Full是可用业务候选，但不强制额外第二mutation。原两许可已接受mutation/正反例不删弱；受改后若其目标语义变化则实核有效性并相应重跑，不拿历史compile失效当保护。

最终sourcefreeze覆盖§2全部存在路径及任何明确删除/内嵌fine删除证明、actualtests/case目标，产SHA/bytes。使用Task19已验native runner/caffeinate/FSEvents方法，但**新task20prefix与独立脚本**，不改旧工具/原输出。native dotnet用 `require_escalated` 保本机FSEvents服务，记cwd/HEAD/command/UTC起止/exit/elapsed/resources/完整console/DFO路径；自然完成，不TERM父runner/活跃test、不补造退出码；异步等待间保持沟通。

正式在同一finalfreeze运行：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

第二条分别对 `Dfo.Server.Application.Tests`、`Dfo.Server.Protocol.Tests`、`Dfo.Server.Networking.Tests`、`Dfo.Server.Host.Tests`、`Dfo.Server.HttpApi.Tests`、`Dfo.Server.Infrastructure.Tests`、`Dfo.Server.Persistence.Tests`、`Dfo.Pvf.Tests` **八个无filter full**，确认真实csproj路径后执行；允许项目串行，xUnit类并行/原5分钟deadline/原Skip保持。不复制Task19三full＋四control脚本的filter冒八full；本Task共享core验收不能沿用1800库存。build需exit0/0warning/0error；App仅两准确原Dimensionfail可接受：`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，同Dungeon7100/maze0/cell(1,0)/entranceLeft InvalidDataException；其余新失败按AGENTS先取得完整类型/stack/身份再归因，注册偶发只按其实际规则与真复跑，不默并旧基线。

DFO_GAMEPLAY_ASSERTIONS落本轮新目录；逐类核原74 expected（App62/Net7/Proto1/其余四各1）与七程序集actual独立control齐，不把exe0当所有吞错证明。任何新增故意违规逐class/count/路径列独立清单，原expected不放宽；outside/exe/orphan与对照外零按真实口径核。原Golden15逐byte对已接受参考/HEAD，Char10逐display pass/0skip，原wire/codec/frameorder/连接开关断言保持。

Counter基线按TASK19已接受三full raw与Task15另1800 raw，拼出8828逐display/status/multiplicity；新八full逐case比较N/D/rename/status，Theory每参数display单独计，不只比总数。原case原则D0且不删弱，若确有必要变化逐项依据，不能靠把手工driver换测少case隐藏删除。报告新总＝8828＋N－D及每项目pass/fail/skip加法，明确新真实八full与历史库存差别；四controls不另计入业务N。记录源改动是新增/删/只驱动准备/改断言，保原两CollectionBox文件case/断言SHA。若formal后改影响源/测试，重新freeze并重跑受影响检查；共享core final证据必须匹配最终source，不用旧formal代新源。

### 9. Capture、仅两ID闭账、Result 与 manifest

fresh-after capture＋同pure family/exits在final source上重采，预期173logical/203command保持，仅**一message route＋一fine→whole**：28Protocol＋1Internal＋174Legacy；146Transactional＋44Direct＋9ReadOnly＋4Workflow；message38Protocol/135Legacy，telemetry14。新whole仅logical66533/exactCollectionBoxRequest，四decoder未增logical；第三许可只本whole，既有family、旧两exit/Matrix及其它注册逐项保持。不拿Expected数代实际capture；变更集合须实际差分闭合。

ledger仅准改：

```text
message:66533
command:Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxCommand
```

actualunits记录原fine一个Tx→显式Main/defaultWrite、minimum/footprint/四namedcatch/SQL原异常/主后即时组帧；protection记录thirdexit的permit＋actualencoder＋finalcleanup AND、default/两端sticky/opaque传递/全participants；destination一唯一whole、一fine deleted（内嵌类型删除，不伪物理文件删除）。挂本Task真正before/focused/正式raw、sourcefreeze、有效mutation、Counter/资产/entry/sourceclose与sharedmatrix证据。`baseline/original`、其它rows/group全部逐值等HEAD，md原前缀byte保持，只追加本Task真实结果。预计pending **121message/174fine/72source/5protection/8transition**，不关闭额外保护/来源/过渡行，不把本次core变更称整个T-004已完。

沿既有migration-ledger.py原验证入口做preclose，执行者追加本Task **literal `Outcome: done`** Result后strict及**原八坏副本**均真运行且拒绝，副本只放新evidence、只变本次两授权ID以验证门，不改原八种坏例语义或篡改Task结果求绿。其余完整Result字段由executing-tasks按configuredskill完成，本规划稿不提前写Result/review/status。manifest列所有实际变化物理路径/SHA/bytes及删除（本次预计无整个生产文件删除，但内嵌fine删除须另有source证明），Task完整前缀身份、evidence/log/脚本、ledger两路径；源非ownedtracked逐SHA不变、index空。检查不漏untracked、不用rename漏审、不把bin/obj纳证据。

交付Result说明：真正旧route前时点和新whole分别实跑了什么、cause/entry/SQL/资产/帧对照、两端probe观察与最小修正依据、任何独立错误/mutation/诊断、build与新八full实际数量、DFO/Golden/Char/strict/Counter/manifest及局限。成功Withdraw若有新的encoder实跑才声称该动态故障覆盖；Discard始终不是physicaltransport，合成测试不冒A21实机。共享core安全通过不等于单CMD≤2秒测量/最终T012已完；剩余pending、Astra/Spec根封存及用户分支收尾留后继。

### 10. 何时交主控重规划，哪些局部选择可留实现

实现者可安全选择localoutcome具体私有record、内部Enter方法拼写、newflow局部observer/JSON字段、fixtureseed/可达的准确trigger写法与focusedfilter；都须保上文结果/边界/观察，不为此新增gate。guard未知或第一次fixture失败不是预设阻塞。出现下列**实证**才停止相关产品推进，保原raw/source/最小方案交主控：旧decoderoracle否定第三出口或普通SQL等价；必须新增白名单外生产caller/共享helper/publicjobguard API；现explicitboundary无法阻止foreign新caller catch后write/return；必须改变WHAT/VC/事务点/旧许可/P3/Compose决定。不能借“routine实现”暗扩恢复、框架或请求通道，也不要求用户重复批准已有授权。

全部必要检查完成、Result/manifest冻结后停止所有共享写与native，供fresh required result reviewer读取；reviewer追加批准后由主控精确消费、完成本地checkpoint。执行者不自批、不stage/commit；不因预算或Task20局部done提前完成B，不创建后续Task。
