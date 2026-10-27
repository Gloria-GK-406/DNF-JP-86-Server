# TASK-20 — CollectionBox 完整协议 CMD 与首次成功 batch 出口

## Task Unit Contract

- Revision: 3（同时标识本契约与详细计划；r1/r2 原前缀与两次有界 task-rework 见 §11/12）
- Outcome and references: 交付 [Plan P4](../plan.md) T-004 的下一有用单元：四个 CollectionBox wire decoder 汇入一个 logical `66533`、一个精确 `CollectionBoxRequest` 与唯一 `GameCollectionBoxCommand`；吸收旧 handler/fine 的完整用例，删净本切片 fine record/handler/注册/caller。原一个 Transactional Main、mandatory footprint、具名拒绝、逐条 clock、库存→完整 boxes/selected 重复→ACK 与一次 exact physical batch 保持。与消费者同时交付最小 `SettledInitialBatch`、穷尽状态及 batch/required adapter 矩阵，保第三具名出口的原 raw encoder 结果。覆盖 [Spec B3](../spec.md) RQ-001、003～009、012、014、015及 RQ-013/017 的本簇记录/当前文档；AC-001～005、007～009/011 的局部义务，F-34/F-112。本 Task 不宣告 T-004、B 或根任务完成。
- Owned boundary: §2 精确18个生产/文档/测试物理路径、本 Task 和 `task-20` 新证据/日志；`migration-ledger.json/md` 仅 §9 两个 ID。旧 CollectionBox 领域/规则/存储/纯领域测试、原 NetworkSettlement cases、opaque 接口、Game outbox 透传、公共 support/transport、其余协议与 shared core 均只读。不能拆出无人消费的 enum 空壳，不能给四 opcode 建四 logical/whole，不能用第二 provider/outbox 或 fake whole 替代生产消费者。
- Required checks: 按 §3 真正产品前 fresh capture/family/source SHA/UTC，然后真正旧 decoder Select/Put 固定 actual encoder cause 直接 oracle；新 whole 同 provider 对应复跑，直接验真实 entry/原 Ordinal/Committed/无新 fence/整失败 batch 未登记/source close/current replay。保原拒绝、纯业务/PVF/SQL/metadata/字节/帧序/物理目标；新增成功 Put→Withdraw wire、真实 replacement 发生事实与拒绝对照。§4 保留两个 previous-token/caller 原 RED 及 r2 同目标2GREEN；原8个 typed/独立清理强例与 lateForeignScope 原 RED须在 r3 窄 Bus/Executor 收口上同目标 GREEN、不弱断言，扩 unknown-token/Begin/liveforeign/first-error/off-executor、generic无request与显式request独立cause对照；三合法 encoder raw 不得被加 ProgramError。实证要求超出 r3 窄边界时依 §10 返回主控。§7 全 phase/来源/默认保护/两旧许可/P3/独立清理矩阵，至少一个有效可编译 provenance mutation RED→恢复 GREEN。最终 freeze 上完整 Release 0警告0错误＋八项目无 filter full，原并行/5m/deadline/Skip、DFO 原74＋七 actual controls及新增 intentional 逐类、Golden15、Characterization10、原8828逐 display/status/multiplicity Counter N-D、capture/strict/原八坏副本与完整路径 manifest 全闭合。
- Dependencies: 仅 [TASK-19](TASK-19.md) revision 2／Result R1／fresh required 独立 approved、主控完整消费并 committed done，产品检查点 `dba3ede68e15f892afd35534db9fed7ed00d04a8`。其已接受共享骨干、唯一 ledger/typed driver、P3 commit-only/来源/RequireRollback 保因及旧两许可为传递输入，不重开其 Task/review。B3/P4 匹配 gates 和 amendment-only 检查点 `d6708faf9b544662fc9d976dba369fbf410f719f` 是已接受的当前治理输入，**不是新增 Task 依赖**；Spec baseline 仍 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。
- Result review: required — shared phase/provenance 或 caller sticky 错误会让提交后普通业务故障免围栏、foreign attempt 的新调用者 catch 后继续写，或令已提交 CollectionBox 重复移动资产；codec、编译与总数量不能证明这些状态。fresh 独立结果快审须核 r2 窄 API和 r3 Bus/Executor 最小性、错误合成/首错优先/late caller全参与者围栏、原8强例/两个caller/late RED→GREEN、三个合法 raw 的原 cause/无 ProgramError、真正 before 与 actual encoder、失败阶段/完整资产/清理发生事实、raw/typed 矩阵、新八 full、非owned边界与两行闭账；主控消费批准后才本地检查点。主控做 Task readiness，不另加独立 Task-plan review gate。

## Detailed Plan

### 1. 接手身份与执行次序

唯一 workdir 为 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`。初始接手 HEAD 为 `d6708faf9b544662fc9d976dba369fbf410f719f`，parent 为依赖产品检查点；此 amendment-only 提交未改变 Server tree。主控初期已证明 TASK-01～19 唯一 committed done、DAG acyclic、20 unused、简短 goal check aligned；本 r3 延续同一 Task20，不重新分配身份。HEAD仍该检查点、index空，r2已存在未提交的授权产品/测试及冲突证据，全部writer/native已停；当前局部执行事实见 §12。执行者复核当前 owned 状态及唯一共享写者，不把初期clean/newwholeabsent冒当前状态、不自行回退历史。

治理输入为 B3 `32948 bytes / SHA-256 1464202d6bf31f3507b580e8349cf766c57ceca0f1e3525e79ded20e457ebe05`、P4 `61551 bytes / db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`；对应 approved review 是 `reviews/spec-b3-review.md`（`42d37ed6073dd53a1904a77e8b847501c5d468f19bcc17e737c42fd39c1c4a52`）与 `reviews/plan-p4-review.md`（`82611a76c0d4ee0dd264357dbd4c8d2c164050b919bc14e593cdac8ee01bbbbb`），主控消费为 `verification/spec-b3-plan-p4-controller-consumption.md`，routing 已指向 B3/P4。依此实施，不复用旧 B2/P3 gate 代替新门，也不重写历史。

执行者先实读上述目标/消费、当前 AGENTS 与两架构偏好、TASK-19 结果/快审/消费及相关源。工作区当前没有 `.codegraph/`，故跳过 CodeGraph；若接手时已有索引则遵守仓库先 CodeGraph 的规则。继承根授权的中文、本分支本地实施/验证/主控 checkpoint（Co-Authored-By）；执行者不 stage/commit，不 push/PR/merge/tag/Release/版本递增/远端 issue，不运行 df_game_r/DNF.exe、部署或 cleanup，不创建 TASK-21，不改 A/root/旧 Task/review/证据。

已接受库存为 **8828＝8808 pass＋2 exact 旧 Dimension fail＋18 skip**。最新三个 full 为 App5058（5040 pass＋2 fail＋16 skip）＋Protocol1771 pass＋Networking199 pass＝7028；其余 **1800＝Host203＋Http57＋Infrastructure976＋Persistence484＋Pvf80** 仍 Task15 库存，含 Infra2skip，不是本 Task 新八 full。当前 capture 是173 logical/203 command＝27 Protocol＋1 Internal＋175 Legacy，147 Transactional＋43 Direct＋9 ReadOnly＋4 Workflow；message37 Protocol/136 Legacy，其中 telemetry14。

r1 时的执行顺序为最小 before/source 与 fresh capture/family → 未迁旧 route 直接 oracle、guard probe与旧故障基线；这些前时点材料已实际产生并封存，身份及保留方式见 §11，不能为 r2 重造 before。r2 已完成窄 API/whole初迁、两个caller目标GREEN，受影响focused与late探针暴露 §12 两个收口缺口，原49before不重造。当前 r3 主控readiness后继续：两新增路径当前/HEAD身份及 r3-owned-before → §4 r3 窄修正、原8强例/两caller/late RED目标GREEN与新增控制 → 原剩余flow/安全矩阵 → provenance mutation/恢复 → final source freeze → 新完整 Release/八 full → Counter/DFO/Golden/表征/capture/源边界 → 两行 ledger/Result/strict/manifest → 停写交 fresh required review/main consume。普通fixture诊断不新设gate；只有超出 r3 已限定方法的实证scope/白名单问题才返回主控。

### 2. 精确白名单：生产/文档12＋测试6＝18物理路径

路径均相对 workdir。没有授权整目录，内嵌 fine 的移除不等同删除整个切片文件。

| 路径 | 允许职责 |
|---|---|
| `Server/src/Dfo.Server.Application/Commands/CommandBus.cs` | r3新增：仅 ExplicitAsync 原 scope drain→Close→dispose/EndScopeCleanup 后、原唯一 FailAsync 前，对现 request!=null 调既有 DemandHealthy；catch 原对象 request.Failed并重新合入 failure。不得再 DemandCurrent、复开request、增FailAsync第二轮或改入队/来源/其它route |
| `Server/src/Dfo.Server.Application/Execution/GameplayExecutor.cs` | r3新增：仅原 ProgramError 最终选择点用private纯引用因果树检查；body failure含同一P引用则保完整body failure，否则维持原P优先/cancelled=false。不限typed；不改slot/first-wins/queue/drain/hooks/close/log/Host |
| `Server/src/Dfo.Server.Abstractions/Execution/GameplayJob.cs` | r2 唯一新增生产物理路径：只加 public static void RecordProgramError(Exception error)，ArgumentNull 验证，Active 非 null 复用 token.Fault(error)/既有 Interlocked first-wins，无 active 不记录/不造异常/不入 job；保已加XML说明；不改任何其它字段或作业 API/Host/Token/EndHooks/排空语义，r3的原终局选择和显式收口桥仅由另两行限定，准确边界见 §4 |
| `Server/src/Dfo.Server.Application/Commands/CommandRegistrationAttempt.cs` | enum 加通用 `SettledInitialBatch`；具名 batch/required-explicit Enter 校验与原 DemandTarget 窄物理 guard，失败保 valid token.owner.Failed(error)并 RecordProgramError(error)标实际 caller 后原 throw；unknown token无owner也标caller。保原 lookup 时点/target/single-call/encoder来源，不加 public phase/caller 签章能力 |
| `Server/src/Dfo.Server.Application/Effects/ExplicitEffectRequest.cs` | DemandRegistration 穷尽 switch；共同 committed/exact target/Main 已结束条件及三个真实阶段；未知 fail closed，其余 Main/footprint/ledger 状态原样 |
| `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs` | token batch 用新具名 batch Enter，required 仍固定自己的具名入口；保 actual encoder 窄 catch、普通重载/查找/Register/BestEffort/关闭语义。ProgramError 只在 Enter/原 DemandTarget 的窄 guard 内，不能扩大至 Core 或本文件的 encoder catch |
| `Server/src/Dfo.Server.Application/Commands/CommandHandlerRegistry.cs` | 仅为新 exit 补 Protocol/effect binding compatibility；保原两 exit、mixed-family、unknown/duplicate/freeze 规则，Application 无 CollectionBox CLR 名单 |
| `Server/src/Dfo.Server.Application/Commands/CommandRequestContext.cs` | 将**现有 DemandCurrent()** 改为 public，原 job/closed/Healthy/authority/未完成 attempt 校验体不变，供 whole 前后复用；BeginRegistrationAttempt 本实例 permission/state/unit/lifecycle 等 guard catch 用 Failed(error)，并以 §4 API 标 actual caller 后原 throw；不得给其它 current/business 错误普遍加 ProgramError。§8 provenance mutant临时改后逐 byte 恢复；不得公开更弱 DemandRegistrationCurrent、造新通道或改终判模型 |
| `Server/src/Dfo.Server.Protocol/Game/CollectionBox/CollectionBoxSlice.cs` | 删除内嵌 `CollectionBoxCommand`/handler；`CollectionBoxHandler` 薄化并加 Protocol marker；错误映射随用例移入 whole。request/minimum、四 decoder、ACK/Contents encoder/Full 保原 bytes 或仅必要 using/具名搬移，不改语义/字节 |
| 新 `Server/src/Dfo.Server.Protocol/Game/CollectionBox/GameCollectionBoxCommand.cs` | 唯一 whole record/handler，显式 Main/footprint、窄 named rejection、current、原逐条组帧与成功 initial batch/拒绝普通 batch |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | logical66533 精确 `CollectionBoxRequest` 绑定唯一 Direct/Protocol whole，声明 `CommittedReplayCurrentStateBatch`＋`SettledInitialBatch`，删一条 old fine Tx；四 decoder/原 encoder注册保持 |
| `AGENTS.md` | 仅当前 CollectionBox opt-in/旧 fine 删除/原Main/第三具名出口与默认保护事实；将当前“两出口”描述就地对齐，保其它协议/历史边界，不写过程或指向 .rks-agent |
| `Server/tests/Dfo.Server.Application.Tests/Commands/ExplicitEffectCommandTests.cs` | 扩现 synthetic registered Matrix probe 的三 phase/callkind/target/生命周期/两端 sticky、late foreignscope/独立清理矩阵及 r3 显式提交后 P/E 保因控制；实际 SQLite 写与终局断言，原所有反例保留，不替换真实 CollectionBox logical |
| `Server/tests/Dfo.Server.Application.Tests/Commands/ProtocolCommandExecutionTests.cs` | 新 exit 的 registry兼容/freeze/unknown/duplicate 直接正反例；保原 required全effect/replay至少一effect及 mixed-family 语义。可在本文件补 r3 generic无显式request的first/unrelated/含P嵌套aggregate控制，沿现执行器/fixture，不新增第7测试文件 |
| 新 `Server/tests/Dfo.Server.Application.Tests/Sessions/CollectionBoxProtocolCommandFlowTests.cs` | 本族旧/新 decoder whole flow、固定 encoder cause/SQL/native/完整资产/entry/sourceclose/current、成功 Withdraw、physical replacement、业务默认保护/来源mutant目标；所有 local observer/DDL/driver 留此文件 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy fine marker 名单只减 `CollectionBoxCommand`；request minimum及其它全部名单/断言保持 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | legacy fine 名单只减同一项，其余保持 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 四 wire→同logical/唯一whole/exactCLR/Direct/Protocol/仅两个指定exit、fine类型及注册零证明，原case保持 |

实际 caller 核实：fine 生产只在 `CollectionBoxSlice.cs` 原114与 `ProtocolModule.cs` 原331；测试无直接 new fine/handler，只两处 marker 字符串名单。`Characters/CollectionBoxTests.cs` 直接真实 domain＋Tx，纯 Throws/PVF/metadata/SQL/满包NoRoom/noMail/Extend不消费目的保持，完全只读，**不新增 selected-session/footprint/domain driver**。原 `Sessions/CollectionBoxNetworkSettlementTests.cs` 全 case/断言只读；其手造 request 的 counter 不与新 flow 的 SendRaw counter 混用。

只读 `IGameCommandOutbox.cs`、`ICommandMessageOutbox.cs`：已有 attempt batch/explicit 重载足够，Game 只把同一 opaque 对象传下去。只读公共 `Inventory/EffectRequestTransport.cs`：新 flow 直接 `SendRaw`，本地写四种 body，不给 Body switch 添公共功能；只读 RecordedOutbound/OutboundRegistrationParts/ShopFixture。只读 CollectionBoxDomainService/RuleService/其它 Abstractions/SelectionTable/ItemDelivery/所有 Persistence/schema、WorkUnits/ledger/reader/sink；CommandBus/GameplayExecutor仅表中 r3 两处窄收口与private纯引用检查可写，其余职责逐byte保持。GameplayJob仅已授权窄方法可写，已加XML说明保留，其现 DemandHealthy/Current/IsOnExecutor/Host/Token/EndHooks等不变。只读 Execution/GameplayExecutorSelfWaitTests.cs 的所有原control/断言；测试仍原6路径，除此以外必要修改须 §10 task-rework。

### 3. 真正产品前 source、registration 与旧 route 直接 oracle

r1 真正产品前已创建 `verification/evidence/task-20-inspection/` 与 `verification/logs/task-20-*`，先保存原15路径的现有文件 bytes/BOM/行尾/SHA、当时两个新路径 absent；capture/family、guard RED 与四 old oracle 的实际 before 49项已封存，详 §11，不重复制造或回填。r2 对第16路径的 before 身份保持原记录；r3 对新增第17/18路径 Bus/Executor，先核当前逐 byte等 d6708faf HEAD及原 nonowned SHA，另存新 `r3-owned-before/` 最小源与对应源manifest/owned清单，再按 §4 改两窄收口。不改原 before-source/before-manifest/13775 nonowned、r1/r2prefix、r2冲突17source或late6manifest历史。后续 focused/mutation/formal 每轮新编号，输出不存在才开始，不覆盖旧记录。记录内嵌 fine/handler 将删除的精确区域及 before SHA、非owned tracked SHA、HEAD/branch/index/command/cwd/真实 UTC。只保本 Task 最小 before 源与实际旧 driver，不复制整仓、bin/obj 或旧证据。

在 products仍逐 byte等HEAD、new whole不存在时，用既有 `verification/probe-tools/migration-ledger.py capture` 和 Task19 pure runtime-family sidecar方法新鲜采实际注册；只 ConfigureServices＋构造 registry，不 BuildServiceProvider/启动业务。原工具只读，新的 task20 runner/check脚本只放本 Task evidence。保存 capture JSON、pure family/exits、源证明、命令/UTC/exit/完整日志；先核 §1 起点数与全部已迁 family/exits，再增加 owned 测试，不用旧 Task19 capture 冒本次 freshbefore。

随后只增加新 flow/必要 local观察，在**真正旧 decoder→dispatcher→legacy fine/Main→SQLite→同 provider Registering/actual outbox/encoder** 路径实跑至少 Select4 与 Put1：

1. 每 case 固定唯一 IOException cause 对象，用现 FaultingEncoder 的实际 EncodeBatch 注错入口与 local计数/target/message记录，证明已进入实际 encoder 而非 callback/transport；断 `Assert.Same(cause, thrown)`，不能只 Throws IOException 类型。同 provider的 INetworkMessageSender/ICommandMessageOutbox alias同 Registering，IGameCommandOutbox沿实际adapter，不能另建容器或把fault移到发送端。
2. Select raw body `[4]`/type03E5；Put body `[box,0,sourceSlotLE,itemIdLE]`/type03E6，使用完整 Game metadata/raw length/hash、真实 all-frame counter与原 physical context。本地明确 seed/catalog、输入sequence、ordinal/fresh、decoder产物/action，不能手造 request 后直接dispatcher替代真正decoder。
3. 通过真实 ledger唯一 CWT 的只读观察提取实际 entry，记录引用、原Ordinal、Selector/Type/Sequence/Length/Hash、Projection/最低needs、Committed；在首次操作、失败后及 replay之间证明同entry/原Ordinal。新 whole observer只typed转发实际生产handler；旧路径可由实际领域singleton forwarding及ledger观察，不给real logical另注册fake whole。
4. 实际持久结果已生效（Select selected4；Put Main少1/Box增1、完整metadata/UID保留），整个失败batch未登记；保此前已登记条目的顺序和末尾exact source-close，直接核 selected session/connection fence没有新增。记录 actual encoder次数、batch/entries/frames/close的精确 physical引用，不能仅凭持久结果间接推Committed/nofence。
5. 外部独立setup事务改变当前 selection/boxes/库存，再用同raw/sequence重发，实际当前 reader被调用、原Execute调用不再增加、无原ACK/不再移动第二件，当前Full及Inventory适用投影/帧序正确。重放沿原ordinaryWrite、MirrorMerge→Create→batch→Town，不重复Main。RecordedOutbound.Discard不真实关socket，这只证明账本/围栏/当前投影，不称真实socket关闭后仍可收包。

保完整raw输出、观察JSON与最小旧测试/driver源码，包含所有item列/slots/selection/适用事件冷却。**所有 BLOB 无损 hex，SQL NULL维持NULL**，不能把byte[]经Convert.ToString写成System.Byte[]，也不能用hex(NULL)空串抹NULL。完整item_core及可空扩展都实际读取；跨run只允许具名 `items.created_at`/`items.updated_at` 按实际SQL记录时间关系比较并保每个原值/row/run及真实UTC窗口，不能忽略整row/core/全部时间。每run rollback前后全列含这两时间仍精确相等；其余确定字段、body/hash/bytes/UID/数量/receipt等逐字段或byte。before缺口不得迁后回填；若真实oracle否定方法，保证据报主控。

### 4. caller guard 窄失败落点与 r3 完整收口

r1 的静态风险现已由 `task-20-guard-before-02` 两个真正行为 RED 证成，原日志/测试源与范围报告均保留，不能继续称纯假设。两个 case 先经同 provider/同 bus 完成 owner 的真实 ProtocolEffectSelected request/required 登记取得已完成 token，再建立真正新 request/new job/new entry；caller 在 handler 内捕获旧 token 的 Enter guard。return case 最终正常返回、caller Failure=null；write case 实际 ordinary WriteAsync 内 ICharacterCarriedGoldTable.AddAsync(+37)，FindAsync staged100037，durable100000→100037、SQLdelegate1。两例只有旧 owner.Failed，owner Committed/closed、caller Rejected/closed、无新增 fence/sourceclose、actual encoder总1。`guard-before-01` 的不存在 GetAsync 编译错误仅诊断，不能作 RED。准确输入身份见 §11。

r2 已授权、r3继续保持的最小 API：`GameplayJob.cs` **只新增** `public static void RecordProgramError(Exception error)`。先 ArgumentNull 验证；`Active is { } token` 时调用现 `token.Fault(error)`，复用原 Interlocked.CompareExchange 的 first-program-error first-wins；无 Active 时不记录、不造另一异常、不新入 job。调用方继续原样 throw 原 guard。方法不发帧/写世界/取得权威，不清 slot，不增加字段、AsyncLocal/currentRequest/DI/locator/jobslot/Func/第二 Host；不改 DemandHealthy、Current、IsOnExecutor、GameplayJobHost、Token/Fault/EndHooks、queue/drain 或既有首错槽/first-wins；完整failure选择仅按下文r3限定。这是既有 opaque 边界无法安全获得 caller request 时，标记实际当前 caller job 的窄失败出口，不是第二请求通道或 A 重设计。

调用只落在真正 registration guard：

1. `CommandRegistrationAttempt` 具名 Enter 校验 catch 纳入 unknown-token、request/job/closed、phase/call-kind/ID/reuse 等校验；valid token 保 owner.request.Failed(error)，再 RecordProgramError(error) 标**实际 caller Active job**，原 throw。unknown-token 无 owner 仍标实际 caller。按现源，exact physical `DemandTarget` 发生在后续 Core 内、required 的实际对象要 lookup 后才取得；保持原时点/接口，将其窄 guard 自身按同样 owner.Failed＋caller ProgramError＋原 throw 收口，可共用 private guard helper，不为形式搬进 Enter。此 catch 不得包整个 RegisterBatchCore、typed 转换、lookup/普通 Register 或任何 actual encoder span。
2. `CommandRequestContext.BeginRegistrationAttempt` 的本实例 permission/state/unit/lifecycle/未完成 attempt 等 registration guard，用本 request.Failed(error)，同 API 标实际 Active caller，原 throw；不改业务/Storage/P3 回滚处理。现有 DemandCurrent 仅按 r1 改 public，**完整校验 body 原样**，更弱 DemandRegistrationCurrent 仍 internal，不顺便将所有 current/business 错误全局打 ProgramError；发现其它实际问题先报主控。
3. actual encoder E 仍只走现 EncoderFailed(request) 的真实来源标记，**不调用 RecordProgramError**；否则会把合法 raw 降成程序错误。非 encoder callback/current/lookup/Register 仍不盖 EncoderFailure 章，其原默认处理不变。不能让 failed(任意E) 的宽 helper 无差别附 ProgramError，不能滥用 self-wait fault API 或以 token.owner 标记冒 caller 已失败。

先用完全同名两原 RED 目标取得 GREEN：后续真实 SQL delegate0、gold100000不变，caller 不再正常return，最终保原 guard cause 引用、owner原 Committed/entry/Ordinal，不新增第二次encoder；直接记录两端 request Failure/Closed、entry、typed/raw、实际 fence/sourceclose，不预编造所有分支应完全相同。不能删弱原目标或把验证改成仅计数器。新 marker 不覆盖已有 first ProgramError；若先有其它错误，原首错仍由 DemandHealthy/作业终局决定，本次 guard 仍须在其 owner/request 记录及原 throw 中保留，不以首错覆盖为由丢独立发生事实。

继续扩 unknown-token、本实例 Begin permission/unknown phase/state/open unit 等 guard 的捕获后 actual SQLite write/return；现两个 previous closed-owner RED 不外推这些矩阵。live-owner/foreign 对照中，foreign caller 必须是真正新显式 request，保 owner/caller/job/entry/closure及 ownerdrain，不仅独立 TestGameplay收到throw；沿现恶意 cross-job fixture的有界 Ready/Release、finally 释放并等待两端自然收口，不新增 GameplayJobHost。off-executor API 非 null 无 active 时无副作用，原 closed/foreign guard 仍抛原对象、不复活 owner/无帧权威，null仅 ArgumentNull；不得把 off-executor变成默许或制造另一错误掩盖原 guard。

正控仍须证明三个合法出口的 actual encoder raw **没有 ProgramError**：required、committed replay、initial 都保同原 cause/Committed/nofence/sourceclose/current；默认业务/来源不明/guard仍默认保护，独立 unit/scope cleanup（含同 cause 在独立阶段重抛）不丢各因/发生事实。r2触发的已证终局冲突已按下述r3处置，不因原返主控句重复停点；新冲突才按 §10 报告，不能放宽原断言或方法。共享最终八full、原两许可/P3/所有 §7 矩阵不减。

#### r3：显式 request 导入 late 首错，Executor 保完整因果 failure

r2 程序错误标记已令两个caller目标GREEN，但原focused58＝50pass＋8fail。八个原强例的body收口已按原规则形成typed/aggregate并围栏，随后Executor原 `failure=programError` 无条件覆盖wrapper/cause。另一个lateForeignScope真实RED更窄地证明只改Executor还不够：新caller Main已Committed，scope在Close后吞旧owner token guard，只有旧owner.Failed、caller Failure=null；actualcaller DemandHealthy能抛同guard，原Bus未看见它而无typed/fence。原twoRED、r2两个GREEN、八fail及lateRED各自证据保留，不相互冒充；身份见 §12。

**Bus仅补已有显式 request 的末端桥。** 在原 scope活着时Drain→Close→scopeDispose/独立错误记录及EndScopeCleanup完成后、原唯一CommandWorkUnits.FailAsync前，若现request!=null，调用既有GameplayJob.DemandHealthy；catch原对象 `request.Failed(error)`，再取 `request.Failure` 合入原最终failure。此时request已Close，**不得重新DemandCurrent**或复开权威；不用currentrequest locator/DI/ambient/jobslot/新host，不新增FailAsync第二轮，不改Storage/encoder来源。此前P已传播至request时由原Includes去重；late foreign caller漏记的P进入该request后，按真实HasCommittedUnit/全部精确participant等现有FailAsync规则typed/fence。相同对象在scope独立发生的次数仍由原scopePhase记录，这次导入是首错传播，不能又算独立一次或抹掉既有独立记录。

该桥只按**实际执行边界**判，不按Protocol/Internal名字：只要进入ExplicitAsync且request!=null（包括实际TrustedInternal形状）就统一复查，无来源特判。显式request已观察独立E/cleanup和首P时应完整合因，不能引用generic首错控制省略E；仍按现FailAsync实际提交事实决定typed/fence，非一律编造提交。

**Executor只改原ProgramError选择点。** 用一个private纯引用因果树判断failure本身、InnerException及Aggregate.InnerExceptions是否包含**同一个**programError对象（ReferenceEquals）。若包含，保已有body最终failure及全部wrapper/独立cause，适用于所有composite/直接同P，不只TransactionOutcomeUncertainException；若failure为空或不含同P引用，仍原P优先，cancelled=false。首错槽/Interlocked first-wins仍P，不加槽/清理/例外来源章，不按异常type/message签许可，不改排队/排空/Host/EndHooks/close/log次数。原generic无显式request body的无关E不含P，仍被首P优先；不能把“body任意非空就保留”作为修复。

修正后首先同目标重跑以下断言，不删弱：

- 原八强display：`A_completed_attempt_is_rejected_after_Close_even_on_its_original_job_during_scope_disposal` 的 swallowed true/false；`A_real_live_attempt_is_rejected_on_another_job_before_encoding_and_the_owner_is_drained`；`A_same_ID_replacement_at_registration_cannot_borrow_the_original_attempt`；`A_real_completed_attempt_cannot_register_twice_in_the_same_request`；`A_real_pending_rollback_cleanup_cannot_issue_an_attempt_and_its_failure_keeps_all_causes_and_the_fence`；`Encoder_failure_and_a_scope_guard_propagation_count_each_real_cause_once_and_keep_an_extra_scope_cause` 的 extra true/false。保typed、原E/C/D精确引用/原发生数、全部participants/fence，不把表征失败归Dimension基线。
- 原两个caller已GREEN继续同目标GREEN：actualSQLite delegate0/gold100000、不能正常return、原guard/ownerentry/Ordinal不变。lateForeignScope同名原RED转GREEN：两个真实不同request/job均已MainCommitted、caller旧token guard经scope catch/swallow后仍当前request有失败、最终typed含原guard并fence caller的全部精确participant；owner原entry/Ordinal保持，两次actualencoder/资产3→2→1、保已提交batch→exactsourceclose，不伪造回滚或重复结算。
- 在原6个test白名单内补控制：先Record P再Q，DemandHealthy仍同P、slot仍P；generic/无显式request GameplayWork body后来无关E仍P优先；body含P的嵌套aggregate＋独立D保原aggregate/wrapper/所有引用且无重复；显式request在已有提交且P＋独立E/cleanup时默认typed保两因，slot仍P，不能套genericunrelated结果删因。原Execution/GameplayExecutorSelfWaitTests只读，原swallowed-selfwait/laterunrelated/depth control保持并随Appfull实跑。
- 三合法actualencoder raw仍GREEN且无ProgramError；default来源、P3/GuardFailure/RequireRollback、独立scope同cause两次、unit/drain/Close/end顺序全保。未修的矩阵不由九个focused目标外推通过；最后完整Release/八full/8828 Counter等仍必须在最终sourcefreeze实跑。

r3不改变 B3/P4 的WHAT/VC/原事务/三raw来源/P3/A seal；这是现有收口两处缺口的最小桥与选择规则，不重新设计执行器。若又有实证需要越两窄点、改变first-wins或其它可见结果，保全部失败source/raw报主控，不放宽原assert求绿。

### 5. 最小共享三阶段与真实 adapter 闭合

`CommandRegistrationExit`只加通用机制名 `SettledInitialBatch`。Registry对新项要求已注册 Protocol且绑定为effect形状；与required一样不能赋给含non-effect的mixed family。原replay“至少一effect”兼容面保持；unknown值、duplicate exit、非Protocol、空/错binding均拒绝，不把新exit声明本身当来源证明，不在Application识别具体Protocol类型。

`ExplicitEffectRequest.DemandRegistration`共同先查exact物理对象、Entry.Committed、Main已结束，再穷尽三项：required＝非replay＋settled；replay＝IsReplay＋其ordinaryWrite已确认；initial＝非replay＋settled。未知default抛，不能维持“非required全部replay”二分。request签发仍验证实际注册许可、当前job/健康/未closed、无unit/未完成operation、无未完成attempt；不创建永久exemption/success receipt或第二凭据框架。

Attempt采用内部具名 `EnterBatch` 与 `EnterRequiredExplicit`（局部拼写可等价），共享private校验核心：batch仅initial/replay、required仅原required；phase由sealed内部attempt携带，actual调用种类由adapter固定，caller没有public phase参数。保request/job、exact NetworkConnectionId及原 DemandTarget 的 ReferenceEquals物理target/lookup时点、entered/Completed单次、state阶段与全部失败传播；未知phase/callkind fail closed。Enter＋DemandTarget 仅真正guard按 §4 owner.Failed/actualcaller ProgramError 收口；Begin自身guard同理，actualencoder/宽 Core catch绝不标 ProgramError。

ActualOutbox token batch改用EnterBatch，required改具名required入口；普通无token overload不变。Game→Abstractions已有opaque参数保持**原同一对象**，不新接口、不包装可任意业务Func、不绕Registering直取底层adapter。只有现实际 EncodeMessage/EncodeBatch窄catch内部记原EncoderFailure并原样throw；typed转换、callback、lookup、取消、outbox.Current/target/buffer/Register均无编码章，不扩大catch来源。

保持现CommandBus一次终判：body记失败→scope活着时drain全部已启动/未await工作→Close→scope cleanup记录独立发生/EndScopeCleanup→§4 r3 对现request复查DemandHealthy并导入原首错→最后一次FailAsync→Executor仅保含同P引用的完整body failure→原outbox条目/来源关闭。raw仍需最终唯一同encodercause、无typed或额外独立error；同一对象在unit/scope再次独立抛出也撤销raw，正常传播不重复计数。原默认Write/ReadOnly sticky、P3 CommitRolledBack只actualcommit同来源/同phase/ConfirmedRollback/全cleanup且无guard、RequireRollback两因保留、Main claim不复位、Freezeprojection不强清、Compose/F-132禁用均不动。

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

沿既有migration-ledger.py原验证入口做preclose，执行者追加本Task **literal `Outcome: done`** Result后strict及**原八坏副本**均真运行且拒绝，副本只放新evidence、只变本次两授权ID以验证门，不改原八种坏例语义或篡改Task结果求绿。其余完整Result字段由executing-tasks按configuredskill完成，本规划稿不提前写Result/review/status。manifest列所有实际变化物理路径/SHA/bytes及删除（本次预计无整个生产文件删除，但内嵌fine删除须另有source证明），Task完整前缀身份、evidence/log/脚本、ledger两路径；最终非owned比较从原13775 map精确排除新授权GameplayJob/GameplayExecutor/CommandBus三生产路径及两ledger，实际五键均存在，故核 **13775－3－2＝13770** 其它原路径逐SHA不变；可保存新的排除副本，但原map/49before、r1/r2prefix、r2冲突17source和late6manifest均immutable，不声称原13775全部未变。三生产路径由18path sourcefreeze、两ledger由两ID闭账另证。index空；检查不漏untracked、不用rename漏审、不把bin/obj纳证据。

交付Result说明：真正旧route前时点和新whole分别实跑了什么、cause/entry/SQL/资产/帧对照、两端probe观察与最小修正依据、任何独立错误/mutation/诊断、build与新八full实际数量、DFO/Golden/Char/strict/Counter/manifest及局限。成功Withdraw若有新的encoder实跑才声称该动态故障覆盖；Discard始终不是physicaltransport，合成测试不冒A21实机。共享core安全通过不等于单CMD≤2秒测量/最终T012已完；剩余pending、Astra/Spec根封存及用户分支收尾留后继。

### 10. 何时交主控重规划，哪些局部选择可留实现

实现者可安全选择localoutcome具体私有record、内部Enter/privateguard helper拼写、newflow局部observer/JSON字段、fixtureseed/可达的准确trigger写法与focusedfilter；都须保上文结果/边界/观察，不为此新增gate。r2 已接受的caller RED/窄API和r3已接受的两个收口缺口按 §4 修正，不因旧返主控条件重复停点。普通未知fixture或第一次诊断失败不预设阻塞。出现下列**新的实证**才停止相关产品推进，保原raw/source/最小方案交主控：旧decoderoracle否定第三出口或普通SQL等价；必须超出18路径/已限定API及Bus/Executor两窄点新增生产caller/helper或修改其它Job API/语义；r3修正后仍不能阻止foreign caller写/正常返回、仍丢typed/独立cause或latefence，或产生新的Spec/首错/drain终判冲突；必须改变WHAT/VC/事务点/旧许可/P3/Compose决定。不能为求绿放宽原断言，不能借“routine实现”暗扩恢复、框架或请求通道，也不要求用户重复批准已有授权。

全部必要检查完成、Result/manifest冻结后停止所有共享写与native，供fresh required result reviewer读取；reviewer追加批准后由主控精确消费、完成本地checkpoint。执行者不自批、不stage/commit；不因预算或Task20局部done提前完成B，不创建后续Task。


### 11. r2 主控有界 task-rework 处置与不可改的前时点输入

以下保留 r2 当时处置与源身份为历史输入；当前继续按 §12 的r3边界，不重做已执行API或将r2改前状态冒当前状态。

2026-10-25 主控沿同 Task20 身份授权本 r2，原因是 P4 已明确 deferred 的 foreign/previous attempt caller-sticky 风险已被实际 probe证成，现 opaque token只有owner，无安全caller request入口，需复用现job首错槽标记当前caller。只在 r1 的15路径上加一条生产物理路径 GameplayJob.cs，形成10生产/文档＋6测试＝16；API/调用边界详 §4。此技术处置不是 Spec WHAT、Plan P4中心方法、VC/事务/许可/P3/Compose 或 A 的改变，不重开 Spec/Plan gates、不引入 Task-plan review gate。根/子契约、B3/P4/routing、Task依赖与required Result review强度保持。

原 r1 **40006 bytes／SHA-256 6d1eb67703aac174f90458b184022b278fdbec549d0ca2f1957b25cff13d9c73** 已逐 byte 存于 `verification/evidence/task-20-inspection/task-r1-plan-prefix.md`；本 r2 不覆盖该prefix，不把本修订写成r1原时点已授权API。`before-manifest.json` **14417 bytes／SHA-256 08f4bcd072012b3a8d39baabe11bf7ce180f44b0f8188ada3f19eb99b556fbbf** 的49项原bytes/SHA已由主控核，本作者只读复核全部49项匹配。`before-source.json` **2498458 bytes／a1153d7271f3993dd15bc6e3d5ed215e4c1d86ae666cd35c1db1c732c77d5ebe** 保原15path/13775 nonowned；GameplayJob本来就在该nonowned库存且与r2改前的当时当前/HEAD精确相同：**10140 bytes／d15fab499b3d750249f18759bd43597f21a4e325d4a23ace3f3ce72dd714b849／LF270、CRLF0**。后继改前先核当前/HEAD逐bytes等价，另存 `r2-owned-before/` 此单一路径最小源/identity与源manifest，再改API。原map实际同时含新授权GameplayJob与migration-ledger.json/md三键，r2当时要求核13775－1－2＝13772其它原路径不变；r3追加两生产路径后的当前13770核对见 §9，所有排除键逐条证明，不称原13775全未变；原map与49snapshot immutable（49manifest无live源路径），可另建r2比较副本，绝不覆盖原49manifest/13775 map或重新生成before冒原捕获时点。

已实读 `guard-scope-report.md`、`task-20-guard-before-02.json/.log` 完整原输出与实际test/producer source；02真实17:21:59.081254～17:22:08.614485 UTC、exit1、两个行为FAIL，具体事实见 §4；01编译 GetAsync诊断保留。`before-seal-summary.json` 生成于17:26:40.183501 UTC，HEAD仍 d6708faf、products/AGENTS逐byte等HEAD、新whole absent、index空；当时只两owned测试改动：ExplicitEffectCommandTests及新增CollectionBoxProtocolCommandFlowTests。summary不是独立review或最终完成证明。

`task-20-old-oracles-02` 于17:26:04.335449～17:26:06.910721 UTC真实exit0/四pass：真正旧decoder Select4/Putfixedcause两个actualencoder directentry/Ordinal/Committed/nofence/failedbatch未登记/exactsourceclose/currentreplay；PutactionSQL19/1811与同participant actual SqliteTransaction.Commit19/787两路，FK1/child1/完整mutation、无损BLOB hex及SQL NULL、全资产rollback/同entry原Ordinalretry已在未迁产品实际跑。最小旧driver、before-observations.json（93073 bytes／8abfd65c126a323f30c42301c6358a7b25ecf0fb7b169e536903b9a2da631ff9）、raw/meta均留原前缀；不需要为r2重复制造这些before，不回填源或日志。它们不代表新whole、成功Withdraw新动态故障、guard扩矩阵、三raw ProgramError负控、mutation、最终Release/八full已完成；相应义务仍按原章节实跑。

r2作者当时仅写当前TASK契约/详细计划及本处置说明，未执行产品/API/native/build/test/Result/review/status或其它证据写入；其交付时尚未运行修正GREEN，不把主控裁定当结果批准。此后执行者实际两个GREEN及收口冲突见 §12，不能沿用此历史未跑描述当当前结果。最终fresh required Result review按当前r3判断完整范围，不提前terminal或创建Task21。


### 12. r3 主控 task-rework：保完整失败与 late caller 围栏

2026-10-25 主控沿同 Task20 身份授权本r3；全部writer/native已停，本作者只改当前Task及核存r2原前缀，不实施产品。r2原 **49402 bytes／SHA-256 9bc1d4f83681c7266e5bc396d31184ef6edb309291c33d9cb1afb0f85ad680aa** 已在 `task-r2-plan-prefix.md`，本作者读时已存在且逐byte等r2，保持immutable、不覆盖或另造重复快照。r1prefix/49before、r2-conflict-source原17source及late6manifest同样原bytes保留。本处置不写Result/terminal/review/status，不更改Spec/Plan/routing/gates或旧Task；主控readiness后另授权执行者。

实读 `r2-program-error-conflict.md`（2callerGREEN、whole-focused58＝50pass＋8fail及原ProgramError覆盖根因）、`r2-conflict-failure-blocks.txt` 的八display强例、`late-foreign-scope-report.md`/原observation/meta及actualBus/Executor源和只读SelfWait原控制。late probe在17:47:31.540485～17:47:40.582418 UTC真实exit1/1行为FAIL，非编译/fixture；owner/caller不同真实bus request和job，ownerCommitted/Ordinal1、callerCommitted/Ordinal2、两actualencoder、库存3→2→1，callerClose后scope吞旧ownerguard，currentcallerFailure=null/ProgramError同guard/最终raw/nofence，已登记batch后exactsourceclose。它证成Bus当前request未见late首错，而不是只有Executorwrapper选择问题；不抹掉原8例或声称修正已GREEN。

只新增两既存生产路径，在原16上形成**12生产/文档＋6测试＝18**，没有第7测试文件。修改前Bus当前/HEAD逐bytes一致 **15113 bytes／52f3d06ed636a5b87545a724473120705307f5239ee3cb33b8308c1219af9bf5／LF268、CRLF0**；Executor当前/HEAD逐bytes一致 **14548 bytes／d9e82394500e00922f6ab508019c9ed3c8f64467a07d4e6db3f053ecf78bc7b6／LF340、CRLF0**。执行者先另存 `r3-owned-before/` 两源与manifest/SHA/HEAD/UTC，不改它们原nonowned库存/旧source快照。GameplayJob已加的窄API及XML说明保持，不新增其它publicAPI/fields。

本作者只读核原49before全bytes/SHA；`r2-conflict-source/manifest.json` **4009 bytes／0bd4e85bc518ac5d9a847ce8750963232be62399a1b993496bb719f2decf8c6c** 的17个snapshot全匹配；`late-foreign-scope-manifest.json` **1844 bytes／4f3811fbb7b633cd9c14fd8a01c8b03cca0b63860c0efd554db6b9c052966ba5** 的6项snapshot/log/report全部匹配。原13775map五排除键实际都存在：GameplayJob、GameplayExecutor、CommandBus、两ledger，最终13775－3－2＝13770按§9核；原49/17/6清单不重新生成、原map不改，不把历史时点新增路径授权倒填进去。

主控限定HOW仅§4两个责任点：Bus在EndScopeCleanup后/唯一FailAsync前对现request复查已有DemandHealthy并合入原cause；Executor原选择点保含同P引用的body failure，否则原P优先，private纯引用树不限typed。无新slot/currentrequest/ambient/DI/Host，无第二FailAsync，不标encoder/Storage为ProgramError，不按类型/文字借raw许可；first-wins槽和排队/排空/endhooks/sourceclose/日志/事务均保。generic无显式request和ExplicitAsync有request按真实边界区分，不按来源名加豁免。这是维持B3/P4默认typed/全participants fence/完整独立cause及caller禁吞错的技术收口，不改WHAT/VC/三raw/P3/A seal，不重开B3/P4或新增用户门。

最终required fresh reviewer须独立判断r3最小性、包含同P的错误树选择、first/unrelated控制、显式提交后P＋独立E/cleanup完整保因、latecaller全部精确围栏、原8强例/两caller/lateGREEN、三合法raw原cause/无ProgramError，以及原真实before/metadata/native/BLOB/有效provenance mutation/finalRelease八full/8828Counter/DFO/两IDledger/strict八坏副本/manifest与非owned充分性。主控技术处置不是此批准；r3修正、本轮新控制和final验收均尚未运行，不能因两个r2GREEN或四wire通过提前宣布Task/B完成。作者交付后停全部共享写/native供主控readiness。


## Result

### R1 — TASK-20 r3／2026-10-25（实际 UTC 2026-10-24），待 required fresh 独立结果快审／主控检查点

- Outcome: done
- **Changed:** logical `66533`／四 wire `03E5–03E8`／精确 `CollectionBoxRequest` 归唯一 Direct／Protocol `GameCollectionBoxCommand<bool>`；thin handler仅把原Dispatch/Input交同bus一次。内嵌旧`CollectionBoxCommand` record/handler、唯一Transactional注册/caller删除，未删除整个切片文件；两legacy marker各只减本fine。18实际source＝16既有修改＋2新增；新增whole及新flow，原CollectionBoxDomainService/规则/存储/Persistence、pure与Network两测试整文件逐byte只读，公共opaque/Game透传/测试support未改。whole SHA `ddc6c8f7630251bad3ca70d351584c468e1376ad16efe42e18cd6378f42159db`；final source-freeze-01 **8049 bytes／SHA `46c16ac54ccb52eb5ad26ada6effc28a1498bc129786fdf34dafc0c8270c8fce`**。HEAD仍`d6708faf9b544662fc9d976dba369fbf410f719f`、原task分支，stage空、未commit。
- **Why／原用例位置:** whole bool仅表示成功/拒绝/无actor，不当提交凭据。原actor/null、初始Rejected不晚升权、preMain映Main槽、前后current均保持；前后current仅公开并复用同explicit request原DemandCurrent完整body，不复制Safety/隐式Capture。有效分支只一次Main WriteAsync，精确原recipient，`DemandActive → actual ExecuteAsync → 仅有Inventory变化才RecordInventorySlots`；Select minimum仅CollectionBox，其余Inventory|CollectionBox。四namedcatch只包actualdomain，保过滤/错误码/原cause，RequireRollback guard在catch外，unit完全结束后才拒绝普通batch；一般SQL/native/cancel/typed不catch、不借WriteResult恢复。Main后才逐change映射并各自GetUtcNow→Full boxes按id及selected重复→ACK，全部组好后initial attempt一次exact physical batch；拒绝没有initial许可。旧业务领域/Table/schema/UID/容器241坐标/无mail/Extend不耗券均原位置。
- **三具名出口／默认保护:** 通用SettledInitialBatch与消费者一起闭合，Application无CollectionBox CLR名单；registry新initial只全effect，原required全effect、replay mixed-family至少一effect保持。ExplicitEffectRequest穷尽三phase及未知default拒绝，batch仅initial/replay，required固定；opaque原对象经既有Game/Abstractions到同Registering/actualadapter。只有EncodeMessage/EncodeBatch原窄catch记EncoderFailure；callback、业务mapping/clock/Full、current/lookup/guard/Register无来源章。三actualencoder出口均以真实token证明ProgramError=null；同cause独立scope cleanup撤raw。原两许可、P3 commit-only/GuardFailure/RequireRollback保因、默认Write/ReadOnly、Main claim/Ordinal/Freeze projection及Compose禁用不放宽。
- **两次实证 rework／caller与最终失败:** 真before `guard-before-02` 两行为RED保全：真正新request捕获旧token guard后callerFailure=null且可正常return；write实际SQLite100000→staged/durable100037。r2窄RecordProgramError仅复用Active原first-wins槽，无active非null无副作用/null拒绝；Enter含unknown、原DemandTarget及Begin本实例guard保owner失败并标actualcaller，actualencoder不标。原两目标同名GREEN后，原8typed/独立cleanup强例实测被Executor原首P覆盖；再由真实lateForeignScope证明caller MainCommitted/Close后scope吞旧owner guard时callerFailure仍空、raw/nofence。所有失败raw/source均封存，未弱化断言。r3只补Bus EndScopeCleanup后/唯一FailAsync前DemandHealthy导入现explicit request原P，Executor只在body因果树含同P引用时保完整body，否则首P优先；不改slot、Host、queue/drain/hooks/事务/关闭/日志。原8强display、两caller和late同目标GREEN；scope独立同对象两次、E/C/D叶引用/发生数/全participants fence保留。first P再Q、generic无request无关E仍P、含P嵌套aggregate/wrapper、显式已commit P+独立E/cleanup、原SelfWait/depth控制均通过。
- **guard扩矩阵与边界:** unknown token、Begin permission/unknown phase/state/open unit/首次借replay、wrong batch/required call-kind、closed/reuse/sameID replacement等捕获后真实ordinary SQLite动作0/gold不变，不能成功结束；原同/多participant强反例保。live foreign两个case沿原故意恶意cross-job TestGameplay fixture，测试反射进入同provider **实际Bus ExplicitAsync完整生命周期**以建真实新request/entry，原production owner悬挂attempt；不是正常入站或fake真实logical/第二Host类型。两端真实不同request/job/entry、自然drain/Close，后续SQL0、owner已commit/Ordinal保、最终typed/fence保cause。该malicious caller为既有rootless job，source-close由production owner真实承担，raw明示没有虚构caller来源。
- **真正产品前与新whole直接对照:** fresh capture-before-01/pure family在product逐byte等HEAD且新whole absent时实际完成；随后old-oracles-02真旧decoder Select4/Put1两个fixed actualencoder cause直接oracle、Put actionSQL19/1811与native实际SqliteTransaction.Commit19/787两个oracle，完整before49snapshot/原测试源/raw/meta保留，不迁后补造。new whole同输入同provider最终formal对应四case均pass：直接same cause、真实同entry/原Ordinal、Committed或ConfirmedRollback、失败整batch未登记/保exact source-close、nofence、外部改世界后current-only replay、不重Main/移动/ACK。native真实participant FK1/child1、完整mutation/Apply/selection成功后才Commit19/787；action发生在真实UPSERT BEFORE INSERT19/1811；同run rollback item全列/BLOB/NULL/slots/selection/事件等精确相等，去trigger同fingerprint同entry原Ordinal只retry一次。native Freeze后projection可非null，未清main claim/原Ordinal。
- **观察校准（未改源/测试/freeze/raw）:** 首次跨run逐值比较揭示真实领域事件random GUID，原失败脚本与真实diagnostic保留。按主控明确observer澄清及owning `EventService.cs:11/30/86`、`SqliteDomainEventPersistence.cs:15–32`，只校准event_id/batch_id/origin_instance_id三类具名身份：有效非零GUID、各自old↔new双射、跨同oracle各phase同ID同映射；rollback瞬态与retry新GUID不强合并；event sequence/payload/type/schema/time全等、Delivery FK无孤儿/全部列与batch/origin分组保持。随机GUID排序仅观测按sequence/subscriber比较，不改wire顺序。四对应oracle其它确定资产/82Bcore/UID/可空扩展/每列/帧byte全相符；仅items.created_at/updated_at跨run作SQL UTC关系，72个字段出现原值/row/run/path及真实runner窗口均保，same-run rollback所有列仍exact，无ID/时间豁免。见oracle-before-after-checks.json及controller-clarifications.md；不是任意GUID正则忽略或丢整事件。
- **新真实flow:** 四action实际decoder原拒绝码、无actor/初始Rejected late-selection、minimum、全资产拒绝回滚保持。成功Put→Withdraw采用与actualcatalog一致的kind3 Material121，保原UID/完整82Bcore，Box清空、库存→完整盒/selected重复→ACK和各change clock；改世界同序号只current。成功Withdraw实际encoder固定cause动态注错已另实跑，Committed/原entryOrdinal/nofence/sourceclose/current-only retry，不再仅由Select/Put外推。真实replacement callback确实1次、Connections实际lookup为replacement，全部registered entry仍旧物理context，无replacement收件；current在actual Enter前失效则encoder0/默认typed。callback/clock/mapping/Full/current五来源反例保默认typed/fence，普通异常/无许可不借initial。
- **有效 provenance mutation:** 仅把RetainsRawEncoderFailure最后实际EncoderFailure同引用AND误放宽为attempts.Any；编译成功7目标＝5pass＋2行为FAIL，其中真实initial BeforeSend callback1/actualencoder0却错误raw/nofence（另current guard亦失保护），两个actualencoder正控仍pass。finally逐byte恢复Context SHA `8ffd6a1f0fca544ef2d90d055a62493d5eaedc34b320f60c20480a412092c746`，完全同7目标GREEN7/7后freeze；不是编译/fixture/超时RED。前置整合158/158、generic/原SelfWait矩阵102/102与Protocol注册18/18均有原raw；普通fixture编译/列名/真实catalog种类/generation/container重绑诊断保留，未更改产品业务来求绿。
- **最终同freeze Release与八真实无filter full:** build `--no-incremental` exit0／0warning／0error；App **5112＝5094pass＋2fail＋16skip**，Protocol1772pass，Networking199pass，Host203pass，HttpApi57pass，Infrastructure976＝974pass＋2skip，Persistence484pass，Pvf80pass；八full **8883＝8863pass＋2fail＋18skip**。两fail仍准确`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均Dungeon7100／maze0／cell(1,0)／entrance Left InvalidDataException，无新失败并入。原xunit并行/5m/deadline/Skip不变，各父runner自然结束。App118.046s、Host117.002s等及Host ru_maxrss10657300480 bytes仅suite资源观察，不是单CMD≤2秒验收。
- **数量／DFO／字节:** 逐display/status/multiplicity对Task19三full及Task15另1800原库存，原8828全部保，N55＝App54＋Protocol1、D0、rename0/statuschange0；本次另五项目1800已实际重新full，不再仅库存/controls。DFO原CLI真实exit0：原74逐类count不变，App62／Net7／Protocol1／Host/Http/Infra/Persist各1，七uniquecontrols，outside/exe/orphan0，新增intentional0；exe0仅观察。Golden15逐byte同时对f01dc226与HEAD，Characterization10逐displaypass/0skip。原两CollectionBox文件及所有原断言保留；两marker仅减一个fine名。
- **捕获／闭账／源边界:** fresh-after173logical／203cmd保持，仅message66533路线＋一fine→whole变化：28Protocol＋1Internal＋174Legacy，146Tx＋44Direct＋9ReadOnly＋4Workflow，message38Protocol／135Legacy、telemetry14；thirdexit只有实际whole且只有replay＋initial，其余family/exits逐项等before。canonical ledger仅两授权ID改变，其余baseline/original/groups/rows逐值等HEAD、md原前缀保持；pending121message／174fine／72source／5protection／8transition。preclose真实通过；Result后strict与原八临时坏副本实际输出见ledger-strict.json／ledger-eight-bad.json及日志。按主控本轮技术澄清：临时反例保原工具8类语义（所需telemetry/transition行可在副本中变），不把“坏副本只2ID”的冲突措辞变成更弱伪例；canonical仍严格两ID，工具/原基线不改。原13775map精确排GameplayJob/Executor/Bus及两ledger五键，**13775－3－2＝13770**其它逐SHA不变；原49before/17conflict/6late清单及r1/r2前缀immutable。Task r3计划60528B/SHA7df386d6…原前缀保，最终manifest-r1.json列全部实际source/evidence/log/脚本/Task及ledger SHA/bytes，未纳bin/obj、无整个源文件删除、stage空。
- **Remaining:** required fresh独立结果快审及主控完整消费/本地checkpoint仍待；本执行者不自批、stage或commit。RecordedOutbound.Discard不真实关闭socket，合成wire/SQLite/登记器不冒A21实机；T004其余pending、T012单CMD≤2秒测量、Astra/Spec根封存及用户分支收尾仍后继，不称B或根完成，不建Task21，不push/PR/merge/tag/Release/版本/issue/游戏/部署/清理。R1及manifest封存后停止全部共享写/native，交freshreview追加审查。
