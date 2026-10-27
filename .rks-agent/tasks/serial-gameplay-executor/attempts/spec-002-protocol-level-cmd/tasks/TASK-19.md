# TASK-19 — Warehouse 六消息族完整效果协议 CMD

## Task Unit Contract

- Revision: 2（同时标识本契约与详细计划；r1原文身份及本次有界方法修订见 §8）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：Warehouse 六个已注册 logical 共用唯一 `GameWarehouseCommand`，吸收原 handler/fine 的身份、槽位映射、时钟、领域调用、异常映射、日志及投影，删除 `WarehouseCommand`。保持一段真实 Main、最低 Inventory 投影、原 receipt 快照、金币/材料/票券/容量与回放语义，特别保留 `0x01AF` 无任何 ACK。覆盖[Spec B2](../spec.md) RQ-001、003～009、012、014、015，以及 RQ-013/017 的本簇迁移记录与当前文档；AC-001～005、007～009/011 的局部义务及 F-34/F-88/F-93/F-112。未迁来源、最终清零、最终八 full/测量仍由父 Plan 后继承担，不宣称 T-004 或 B 完成。
- Owned boundary: §2 精确11个生产/文档/测试物理路径、本 Task 与 task-19 新证据；`migration-ledger.json/md` 仅 §7 六 message ID 与一个 fine ID。其它 DTO/codec/composer、领域/规则/接口/存储、reader/sink、transport/helper、共享 core、父契约/Spec/P3/root/A/旧 Task/review/证据只读。不得拆成六个 whole，不扩大到 Move/Sort 或创建共享协调器。
- Required checks: 保留真正产品前 fresh registration capture/family及四旧完整 wire 的已录事实；原 observer 的 item_core 只录成 System.Byte[]，原前时点失败stage与前后BLOB全hex义务尚未满足，按 §8 唯一允许的明确 late exact-HEAD reconstruction补足，不能称原前时点已录全core。迁后同输入/同 provider实际对照，items两具名数据库记录时间列仅跨run按关系比较、每个原值保留且每run回滚前后精确不变，其它资产/UIDslots/完整core/帧仍逐字段/byte；deposit与01AF各action SQL/真实SQLite Commit两阶段、原ACK或无ACK/entry原Ordinal retry/current replay仍全保。六类型/正常RefusedCommitted/早拒无Main/具名异常与clock/NotiBus/直接ACK/独立E+cleanup及原业务/Throws/SQL/UID/并发/codec/Golden/表征检查不变。observer改源后freeze02上完整Release0/0＋三full＋四actual controls/74+七control/分类违规/Golden15/Char10/capture/strict原八坏副本/Counter与源边界核验；不以formal01替代最终02，mutation复用条件见 §6、§8。
- Dependencies: 仅 [TASK-18](TASK-18.md) revision1／Result R1／fresh required 独立 approved 与主控精确消费后的 committed done，本地检查点 `3118ad0907b69e7207bb910564c247c846c51eaa`（parent `c03ae3476d52fe8afaf9165047877fb7958ae046`）。其已消费的 Main/唯一 ledger/typed driver、Task15 WriteResult/带 cause/来源与后续保护是传递输入，不另列并行依赖、不重开共享 gate；Spec baseline 保持 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。Task01～18 已唯一 committed done、DAG acyclic及初期19 unused为原控制器结论；本次延续同一TASK-19身份的task-rework，不另建Task。
- Result review: required — 错误迁移可能在账本错误允许重试时重复扣金币/票券、错误升级容量、丢账号与角色两边一致性，或把已提交/guard 故障伪装普通拒绝；编译、codec 和总用例数不能证明这些状态。fresh 独立结果快审须核原前时点/晚期重建两层证据、真实阶段/资产/异常、删除路径及七ID，并明确判定r2补证方法及原core缺口是否充分、两列跨run比较是否严格；主控技术处置不是独立approval。主控消费结果快审后才本地检查点；Task-plan由主控检查readiness，不增加独立Task-plan审批门。

## Detailed Plan

### 1. 已接受起点与真正产品前顺序

实施者先核 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`、HEAD 等于依赖检查点、唯一写者与 owned 状态；读当前 AGENTS、架构哲学/重构偏好、B2/P3及依赖完整结果/审查/消费。无 `.codegraph/` 则不调用 CodeGraph。继承根授权的中文、本地实施/验证/主控 checkpoint（Co-Authored-By）；不 push/PR/merge/tag/Release/版本递增/远端 issue/游戏/df_game_r/DNF.exe/部署/cleanup，不创建 TASK-20。出现白名单外必要 caller、接口或共享机制依赖，保 source 与最小方案报主控，不静默扩写。

r1起点延续库存 **8781＝8761pass＋2准确原 Dimensionfail＋18skip**；Task18 同源三 full 为 App5012（4994pass/2fail/16skip）＋Protocol1770pass＋Networking199pass＝6981，Task18 N20/D0。其余1800仅 Task15 final03 业务库存：Host203、Http57、Infrastructure976、Persistence484、Pvf80（其中 Infra2skip 未在 Task18 业务 full 重验）。四 control-only 实际数另列，不称八 full，不称本 Task 性能验收完成；Task19 formal01已自然实跑的历史及后续02要求见 §8。

r1起点 capture173logical/203cmd＝26Protocol＋1Internal＋176Legacy，148Tx/42Direct/9ReadOnly/4Workflow；message31Protocol/142Legacy；pending128message/176fine/72source/5protection/8transition。

**先保存前时点，再迁产品。** 最小 preflight 保存 owned existing 原 bytes/BOM/行尾、三新增预期 absent、删除清单与非owned tracked SHA，不复制整仓/旧档/bin/obj。此时 Handler、旧 Command、Module、AGENTS 四 existing 生产/文档路径仍逐 byte 等 HEAD，新 whole absent；真正运行 fresh ConfigureServices-only registration capture 与 pure family sidecar（只构造实际 registry，不 BuildProvider/启动业务），保存四 existing/第五 absent 的 SHA/bytes、实际 HEAD/cwd/command/UTC/exit/raw 输出。使用现 `verification/probe-tools/migration-ledger.py` 及 Task18 family 方法，工具只读，不新造共享机制门。

原初期顺序是完成fresh-before后只增owned最小测试/observer，在**旧route**实跑§5四条完整wire及原定向业务，保存最小old-route测试源/raw/meta与当时产品SHA。r1真正freshbefore及old02的33pass有效，但full-core观测缺口不能靠重建冒称前时点补齐；本r2只按§8保留原证据并新增明确标记的late reconstruction。若补证发现真实行为不等，保原日志报主控，不重写before或放宽oracle。before/focused/mutation/formal/reconstruction各轮独立prefix，全部诊断保留。

### 2. 精确白名单：生产/文档5＋测试6＝11物理路径

| 路径（相对 workdir） | 允许职责 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/Warehouse/WarehouseHandler.cs` | 原 `WarehouseHandler` 薄化为只交同 bus 的原 Input/Dispatch，加入 Protocol marker；旧 `ErrorCode` 随编排移入 whole。仅必要 using；同文件 `WarehouseChanged` 和 `WarehouseChangedHandler`/composer 区域逐 byte 保留 |
| 删除 `Server/src/Dfo.Server.Protocol/Game/Warehouse/WarehouseCommand.cs` | 完整删除旧 fine record/handler，不留兼容类型/转发/Obsolete；删除本身属于 manifest/freeze 核验对象 |
| 新 `Server/src/Dfo.Server.Protocol/Game/Warehouse/GameWarehouseCommand.cs` | 唯一 whole record/handler、原业务编排/窄异常处理/日志/错误码，显式 Main/WriteResult 与 unit 后投影/拒绝登记 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 六 logical 精确 `WarehouseRequest` 绑定同一 Direct/Protocol whole，仅既有 CommittedReplayCurrentStateBatch；删除一条 fine Tx 注册，原 decoder/encoder/notification 注册保持 |
| `AGENTS.md` | 仅补 Warehouse 当前 opt-in、旧 fine 删除、Main/原首快照与 01AF 无 ACK 必要事实，不全篇重写 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/WarehouseStorageTests.cs` | 原 Command helper 与 stale fine caller 改 §5 具名 typed 领域 driver；原直接 handler 成功投递 case 改外部真实 wire，保原全部 case/断言/SQL/Throws/UID/并发目的；只最小 internal 可见性供本 flow |
| 新 `Server/tests/Dfo.Server.Application.Tests/Inventory/WarehouseDomainTestDriver.cs` | 本族具名 Expand(host,owner,type,value/slot) 入口，同 provider、独立 TestGameplay、真实 Tx、Capture(owner,null)/current前后/原 clock；不开放任意 Func 平台，不造 footprint cap |
| 新 `Server/tests/Dfo.Server.Application.Tests/Inventory/WarehouseProtocolCommandFlowTests.cs` | 本族旧新 wire/四 action-native oracle、typed domain forwarding 与 whole observer、资产/账本/异常/投递/保护/mutation 业务目标 |
| `Server/tests/Dfo.Server.Application.Tests/Sessions/EffectSettlementRegistrationTests.cs` | legacy fine marker 名单只移 `WarehouseCommand`，保 `WarehouseRequest` 及全部其它名单/反例 |
| `Server/tests/Dfo.Server.Application.Tests/World/CommonExperienceEffectTests.cs` | legacy fine 名单只移同一项，保其余命令与原断言 |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` | 新六 logical/唯一 whole/exact CLR/Direct/Protocol/replay-only/无 fine marker/旧 fine 注册零证明；全部旧 case 保持 |

实读 caller：生产只有 WarehouseHandler→WarehouseCommand、Module Tx；测试只有 WarehouseStorageTests:97 stale、:304～305 helper，以及两 marker 字符串名单。StorageTests:214～215 原外层 TestGameplay＋Empty metadata 直 handler 另行改真实外部 decoder/dispatcher/fresh sequence。EffectRequestTransport:105 已支持六种 body，公共 transport 不需更改。其它 Move/Sort helper/caller 留原协议，现 `InTransaction(Func<Task>)` 不扩为 Warehouse 通用 driver。

只读 `WarehouseMessages.cs` 全文件 Request/Response/decoder/encoder、`WarehouseCodecTests`、`InventoryExpansionDomainService`/接口/rules/所有 Persistence/schema、WarehousePurchaseTests/WarehouseMoveSynchronizationTests、其它功能测试与共享 support、CommandWorkUnits/Request/ledger/reader/sink/通知总线。新 observer 可 forwarding 原实际领域 singleton，不能第二 provider、复制业务或给生产 logical 注册测试 whole。若真实发现新必需 caller，先报主控，不扩大整目录授权。

### 3. 六 logical 共用 whole，保持 Main 与异常的实际位置

| logical / type | 原语义 | 首成功通知（沿原 composer） |
|---|---|---|
| 65841 / 0131 | account create | ACK→Main full→AccountCargo full |
| 65842 / 0132 | account upgrade | 同上 |
| 65843 / 0133 | deposit 原 signed int amount | 同上，成功 ACK 带 receipt.Account.Cargo.Value32 |
| 65844 / 0134 | withdraw 原 signed int amount | 同上 |
| 65944 / 0198 | personal ticket upgrade | ACK→Main full→PersonalCargo full |
| 65967 / 01AF | main ticket upgrade | **只有 Main full；成功、拒绝及异常均无 ACK** |

record 为 `IProtocolCommand<InventoryExpansionResult>`，Message 原 WarehouseRequest、Frame 原 Game metadata、Dispatch 原 NetworkDispatchContext。thin handler 实现 `IProtocolCommandMessageHandler`，仅构造/await 同一 bus。whole 注入原 sessions/online 查询、具名 mappings、expansion、TimeProvider、ICommandWorkUnits/ICommandProjections/IGameCommandOutbox/logger，沿现合法注入面，无 bus/sender/locator/当前请求 DI/内容/规则/第二执行器，无 CMD→CMD 或 legacy 转发。

FindByConnection→online 查询→0198/01AF 把 Main wire slot 映 BusinessSlot→验证最初请求 Selected 与精确 session→唯一 Main。初始 `ProtocolEffectRejected` 即使晚出现选角也不升权；无 actor/online 或 mapping 拒绝不 Main。不能把 domain 的 CurrentAsync（InWorld、非 dungeon、非 tower、belongs-to）前移到 whole 造成正常拒绝从原 MainCommitted 变 Rejected，也不能把原规则/负 amount 判定提前。其它 request 变体或 Selected/session 不匹配按程序错误出路。

本 domain 没有 recorder、ApplyEffects 或自开事务，**不加 cap、NonRequestFootprint、领域接口或共享机制**；Main 原 minimum `ReplayProjectionNeeds.Inventory` 足够，参与者为 originally selected actor，动态 SQLite facets 沿实际写加入。正常返回领域 Refused 不 RequireRollback，仍 MainCommitted/最低 Inventory；不得因 Receipt null 把整类拒绝回滚。

消费已有 `WriteResultAsync`，action 只把**实际 clock.GetUtcNow＋原 expansion 方法调用**放在窄 try。clock 原在 fine 内、由外层 handler catch 覆盖，不能挪出该兼容 span。unit/request/Main/current/参与者/Freeze/cleanup/投影/编码均不纳入业务 try，不外 catch 整个 WriteResult。

catch 原顺序及映射：`InventorySlotMappingException`→InvalidRequest、whole 不记录 handled Error（即使来自实际 domain 内部也保持原 handler 的分类）；`InventoryExpansionException`→原 Rejection、whole 不记录 handled Error；其它非 OperationCanceledException 且非 ContainsUncertain E→StateChanged，并保原 Error cause/级别。每条 catch 在 unit 内保存本地原因并 `RequireRollback(E)` 后返回动作 outcome；RequireRollback 自身 guard 不吞。单元完全结束后才 mapper/log/ACK；实际 CommitRolledBack 消费其原 Cause，用同一纯业务 mapper，不把 guard/sticky/Unknown/typed/cancel/cleanup/已持久提交伪装此值。业务 catch 不能清 sticky、复活权威或再结算。

preMain mapping catch 同样只覆盖 mapping；catch 外再拒绝登记，直接 ACK encoder 抛 `InventorySlotMappingException` 也不能被当映射拒绝吞掉。拒绝信息日志保原 Type/Reason，普通 E Error 保原 Type/Character。日志按真实路径取证：preMain mapping 原本没有 fine pipeline Error；domain 内 mapping/InventoryExpansionException 原 handler 只 Info，但原 fine pipeline 可能记录 Error，迁后随 fine 删除只保 whole 的原 named mapper/Info，逐项披露实际差异，不能称“两Error→一Error”。只有普通 SQL/通用 catch 才可按旧实得 pipeline＋handler 双Error→whole 单 handled Error记账；记录 category/次数与原 cause/级别，不恢复 legacy pipeline。

原错误码保持：deposit GoldLimit95/其它10；withdraw GoldLimit94/其它10；create/upgrade InsufficientAssets22/其它19；0198全部19；01AF从不调用响应编码器。成功 unit 后仅原 TheseCharacters/WarehouseChanged 投影；首投递仍原 NotiBus Failed 逐 recipient 隔离，不能宣称同步发送异常或新增 raw。直接拒绝 ACK 仍默认保护；仅 existingCommittedReplay 许可，无新 registration exit。

### 4. 真实事务、receipt 与原强测试的保持

原 TransferGold：RequireTransaction→CurrentAsync→amount/tier/两边余额及限额→**account_cargo_state.value32 条件 UPDATE 先写**→character.TrySpendGold/AddGold→核 actual→SnapshotAsync。原 UseItem：查当前/载体/规则/参数→Take/Delete 一张票→ChangeCharacter 容量→Snapshot。account create/upgrade 按原等级/阶段/金币/材料收集→扣金→逐材料 consume→改 selection_key→Snapshot，不更改容量或 cost。

`SnapshotAsync` 在当前 Tx 内先 `items.SessionIn(...).ApplyDecidedWritesAsync`，再 Load character/account inventory，并返回原 at。必须用这份 receipt 给原 composer；不 commit 后重读第一响应、不更改 composer 或新建 snapshot 事务。重放则沿既有 current reader 的 ordinary Write/原投递，不再领域结算/原 ACK，不能用首次 receipt 假装 current。

WarehouseStorageTests 原全部 case/参数化 multiplicity 保留：tower 限制、错误 owner/container、account create/upgrade/material/重复请求、signed gold/限额/陈旧session Throws、later storage failure 原 SqliteException/回滚、personal ticket/当前容量、跨角色货仓 UID/core/details、withdrawal gold、20 并发保总量、容量与 binding/seal/实例限制、成功 committed ACK 和 replay。不得删 SQL/余额/UID/Throws 来统一驱动；原 codec 原字节与 01AF 无 response-handler 条件保持。

新 typed driver 每调用 `TestGameplay.RunAsync(host.Services, ...)`，原 owner 显式 `SelectedSessionSafety.Capture(owner,null)`/DemandCurrent 前后，Begin 真实 DomainTransactionScope；用原 clock 与类型 dispatch 直接实际 IInventoryExpansionDomainService，正常 Commit，catch E 真 AbortAsync(E) 后原 throw，finally Dispose。无 EffectFootprintLease/no-op recorder；该 domain 本就无 recorder 参数，不为样板添加。stale case 继续传原 stale owner、同样 Throws，不偷换为 whole 返回拒绝；其它纯业务原 helper 返回原 InventoryExpansionResult。

原成功投递 case 从外部 `EffectRequestTransport.Send` 经过真实 decoder/dispatcher/完整 metadata 与新鲜 seq，准备可在独立 setup job，但 Send 不在外层 TestGameplay，保 BeforeSend 的持久余额/容量、首 ACK/3帧与 frame 长度断言。现 replay seq7 deposit100→seq8 deposit250→seq7 已证 cargo350/carried99650、无重复 ACK/current NOTI，保完全相同序号/输入断言；与新 01AF 全 wire 成败补证区分。

### 5. 旧新完整 wire、失败阶段与本族保护

四oracle在r1已于产品前capture后实际旧route运行，迁后仍须同seed/raw metadata/body/hash/seq重跑；两层证据的已满足部分与BLOB缺口按§8区分，下面真实阶段/同provider要求不变：

1. **deposit action**：fixture account cargo 可用且足够 carried gold，actual `account_cargo_state.value32 UPDATE` 设 RAISE(ABORT) trigger，实际 SqliteException19/1811/堆栈证明 action 阶段，不称 native commit。错误10完整 ACK、原 Error cause/级别、两余额/其它资产还原，entry ConfirmedRollback/无 fence/source close。
2. **deposit native**：fixture DEFERRABLE FK child，由实际 value32 UPDATE 的 trigger 产生 child；同 provider **当前真实 participant 的 SqliteDatabase 连接**证明 PRAGMA foreign_keys=1、child存在、account 已增/character 已减、domain完整 receipt 已返回，然后真实 `SqliteTransaction.Commit` 抛19/787。不能用 fake participant、单独 provider、外部手插坏行或外部 Scalar 读取 staged 状态代替。
3. **01AF action**：真实可用 Main upgrade ticket（原 ExpansionContent 中2660296→Main参数8，50/57仅Personal；先核实际 ItemDefinition/placement可达及原业务当前 parameter，不改共享content或用假plan），actual `character_container_state.list_param16 UPDATE` 触发19/1811；先有真实票券消费 stage，随后容量更新失败，回滚票券/UID/slots/容量，**无任何 ACK/成功 NOTI**。
4. **01AF native**：同实际 list_param16 UPDATE 触发 deferred child，真实 domain receipt/ApplyDecidedWrites 后，同 Current participant 记录票券已少1、Main parameter已升级、child/FK1；真实 Commit19/787 后均恢复且完全无 ACK。此接口原 ChangeCharacter 先 INSERT-if-absent 再条件 UPDATE，trigger须命中真实 UPDATE，不能仅挂 INSERT 或猜列名。

每oracle仍要求raw E引用/level、真实阶段/Completion/entry/Ordinal/最低needs、Frozen projection事实、前后金币/货仓参数/完整票券core/UIDslots/容量及适用事件/投递/冷却资产。r1原core字段只有System.Byte[]，不能据此认定已录完整core；r2必须明确hex(item_core)及其它实际BLOB观测值，依§8补证。阶段内读取经同provider SqliteDatabase.ReadAsync/WriteAsync当前participant；ShopFixture.Sql/Scalar只seed/DDL/前后durable查询。DDL仅fixture生命周期，不改生产schema。失败无新增fence/source-close（以实际旧route证据为准）；去trigger同fingerprint、**同entry原Ordinal**retry恰好一次真实变更，后同seq只当前库存/无原ACK/无重复金币或票券。native Freeze后projection可非null，下一admission才清，不能清projection/main claim或重写Ordinal来满足断言。

旧与新对照raw帧body/hash/顺序、ACK error、silent branch、确定资产字段/receipt/真实stage逐字段；业务TimeProvider仍用原deterministic值，但它不控制SQLite CURRENT_TIMESTAMP。跨run仅items.created_at/updated_at两明确数据库记录时间列按§8关系比较并保原值，每个run回滚前后两列及完整core仍精确不变；不忽略整row/core/全部时间，不改生产clock/schema。原action trigger不是native；四oracle不冒称六分支所有native/实机均验收。其它差异、真实行为不等或fixture不可达须保日志报主控，不假票券成功或放宽native要求。

新 flow 还须覆盖六类型真实成功与拒绝/错误码；normal domainRefusedCommitted 对 early/mapping/noactor Rejected无Main；Main只一次/最低Inventory；InWorld/dungeon/tower/owner 判定仍在原 domain时点。初始Rejected晚selection不升权、下一fresh可正常，受控 query如必要须注明非自然 registry 路径，不伪造捕获权威。

typed forwarding observer 分别把 InventorySlotMappingException、InventoryExpansionException 和普通 E 送入实际调用 span，验证前两 whole 不记 handled Error/对应码、普通E有Error/StateChanged，并按 §3 分开核原pipeline日志，不伪称named异常旧新总Error数恒等；clock注错仅用本地有界TimeProvider，仍在原fine对应action try的原顺序内，验证同分类及cancel/typed不确定不恢复。此类注错只证明映射，不能替代四真实 SQL oracle。域无 cap/footprint，因此不造假 cap guard反例；共享 Main/current/guard、defaultWrite/ReadOnly、Unknown/来源/facet/closed/两 B2许可等原完整反例继续随 App full 运行。

原首成功通知 encoder Failed 经实际 NotiBus 隔离，仍 Committed/no新增fence；随后改变当前资产再同seq重放，证明只 current/无重复结算。直接拒绝 ACK 编码失败分别覆盖 earlyRejected、normalRefusedCommitted、ConfirmedRollback 实际保护出口（原 cause/source close；已提交默认typed/精确参与者fence）；特别 ACK 抛 mapping 异常不应落业务 mappingcatch。01AF从来没有 ACK编码注错路径，不用虚构 ACK证明保护。给本族一条真实action E＋独立 rollback/cleanup X连接性，含不同对象及同对象两次发生的有界参数化，保E/X、真实资产恢复、无普通成功结束、默认Blocked/fence/sourceclose；不是 fake commit故障当 native。取消/guard/登记sticky不能被业务 catch恢复，禁止新增 raw例外。

### 6. 有价值 mutation、冻结与正式检查

原mutation只在owned whole把deposit参数错误改为`Math.Abs(request.Value)`，wire amount=-100的正确行为为原ACK10/两余额不变/正常拒绝MainCommitted；mutant变合法deposit100，cargo+100/character-100/成功ACK，应以真实状态RED与旁边合法正对照证明。原纯领域negative不冒称whole覆盖，编译/fixture错误不是RED，finally恢复bytes/SHA后同caseGREEN才freeze。本r2若仅改观测器、产品whole仍SHA `149f5634ecbbfb75d281eb42a97d36ea25497bddada53959c77a88246640a050`且mutation目标语义/用例未变，可引用原真实RED/GREEN及恢复来源，不为重复流程制造新RED；若目标语义受改则重新核有效性/实跑，不改domain/rules/codec造红。

沿Task18已验native/runner/caffeinate/FSEvents执行方法，freshprefix记录command/cwd/HEAD/UTC起止/exit/elapsed/resources/console并自然结束；不TERM父runner/活跃测试、不补造exit或覆盖旧输出。observer改源后必须freeze02并重新完整 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 0warning/0error及三full＋四controls正式02；formal01只保实跑历史，不能充最终02source。App/Protocol/Networking各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`。保原类并行、5m、deadline、Skip；共享core不改，必要扩scope先报主控。

同新 DFO_GAMEPLAY_ASSERTIONS目录采三 full＋Host/HttpApi/Infrastructure/Persistence四actual原control-only，精确filter `FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`。原74 expected逐类/count（App62、Net7、Protocol1、其它四各1）/七程序集各唯一control、outside/exe/orphan0；exe0仅观察。新故意违规如本族加入须逐类/count说明，保持原expected不放宽。Pvf无control；四control不是业务full。Golden15逐byte对 `f01dc226` 与本HEAD，Characterization10逐名pass/0skip。

以 Task18 有效 App01/Protocol01/Networking01 三 full 的6981 raw display/status/multiplicity Counter比对原全保/真实N－D，延续总库存8781＋N－D；D0/rename0为目标，必须改名逐项旧→新与驱动目的，不靠Assert多重集或总数抵消。原两失败精确为 `Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException；原App16skip逐身份保留，另外1800/Infra2skip只Task15库存。新Bind/SQL/fixture失败原日志保留、按源定位，不登记成原基线或宣称Task17 Bind48根治。源变则新freeze/受影响正式轮，不拼不同源的full；耗时/内存仅观察，T-012最终新八full/单CMD≤2秒仍后继。

### 7. 六消息一 fine 闭账、删除证明与交付

fresh-current capture/family与§1 before逐row比，只六message route和一old fine→whole；全量family/Matrix/exits/遥测14及其它routes严格不动。预期173logical/203cmd保持，**27Protocol＋1Internal＋175Legacy，147Tx/43Direct/9ReadOnly/4Workflow，message37Protocol/136Legacy**；以实得证明，不能硬填预期或倒填before。新六bindings同一WarehouseRequest/唯一whole，不用反射枚举补未注册消息。

`verification/migration-ledger.json/md` 只闭 `message:65841`、`message:65842`、`message:65843`、`message:65844`、`message:65944`、`message:65967` 与 `command:Dfo.Server.Protocol.Game.Warehouse.WarehouseCommand`。记录原六caller/一个Tx、原domain内快照/实际参与者/Main最低Inventory、whole/测试/Result/artifacts与各阶段保护；original/baseline/其它rows不动，md原前缀保持。预期pending122message/175fine，72source/5protection/8transition不变，不顺闭其它Inventory/来源/保护/过渡。

新证据仅 `verification/logs/task-19-*` 和 `verification/evidence/task-19-inspection/**`，现 probe-tools只读复用。source freeze/manifest以11物理source paths为边界：**7 existing修改、3新增、1删除旧fine**，从HEAD重新核实际分类。删除路径必须记录最终 absent及HEAD beforeSHA/bytes；不能套用Task18全路径exists断言、因不存在遗漏manifest，或把删除+新建冒称原文件rename而漏审。保原bytes/BOM/行尾，WarehouseMessages全文件及Handler composer只读范围独立byte/hash核验，不整文件格式化；非owned tracked SHA除授权两ledger外精确不变，不复制整仓。保原 case delta/old-route最小源/四实际oracle/诊断/正式freeze同源/完整物理manifest与Task Result R1 prefix。

先preclose核真实artifacts，再由执行者追加Result后strict validate与原八坏副本全部拒绝；不能填不存在Result骗strict，不改validator parser/放宽坏例。本文件当前只有契约与详细计划；实施者后续按executing-tasks追加Result，不改committed计划历史。

最终停止共享写，交fresh required独立结果快审，仅在冻结的执行Result prefix后追加review；主控消费精确SHA/bytes/物理集合后本地checkpoint，执行者不自行stage/commit。报告唯一whole/旧fine零、六route/Main/最低needs、原快照/三或一帧与无ACK、金币/票券/容量/正常拒绝/具名异常、四旧新oracle真实阶段/原cause/Ordinalretry/current不重复、NotiBus/直接ACK/E+X默认保护、mutation恢复、数量实测与1800延续范围、删除manifest/七ID/非owned边界；明确原前时点core缺口、晚期补证/原named日志重建范围、两列跨run关系比较与独立快审判断，不称原before全core已录。remaining122message/175fine/72source/5保护/8过渡、T-012/Astra/Spec根封存与用户分支收尾仍后继，不宣称B完成。

### 8. r2 有界方法修订与主控处置（task-rework，非结果或批准）

**原因、已接受输入与身份。** 主控在r1执行中发现observer的`SELECT * items`经`Convert.ToString`把item_core写成`System.Byte[]`，失败stage及before/after BLOB未录hex；原真正fresh-before capture/family、old02四actual wire 33pass、真实action/native阶段、UID/slots/票数/参数、完整firstFrames与replay hex仍有效，但不能据此证明原前时点全core资产已录。原named日志三case本身也是late reconstruction，不是before；可独立支持原计划的named mapper需求，须始终标记其真实时点。保原所有raw/最小测试/生产快照、初期diagnostics与原失败材料，不能覆盖或回填。

本次主控明确裁定**同Task19 task-rework**：仍Spec B2/P3/T-004、同11 source/七ledger ID与原语义，不新Spec/父Plan中心变化、B3、新raw或共享机制gate。planner只修本文件契约/详细计划方法；主控readiness后才由原executor继续补证。此处技术裁定只授权方法修订及后续有界实施，不代替required fresh独立结果审查。

r1原文只读快照由主控保存于`/tmp/task19-plan-r1-prefix.md`，实核 **27120bytes／SHA-256 `ccd77703fd464a1e9cfb480ee6d9fd1d7e1249060e98b2dcf8c50fce91aba95d`**；后由executor逐byte复制到本Task E19的`task-plan-r1-prefix.md`封存并核SHA，不能改此原文。r2只改变未完整满足的观测/对照方法及后续正式复验要求，不把计划历史改写成已经成功取证。主控授权修订前executor已停全部共享写/无native，主控已核HEAD仍`3118ad0907b69e7207bb910564c247c846c51eaa`、stage空、freeze01（`source-freeze-01.json` SHA `daec01505f1b84da0589b0a903899c68014fe5cd91a8811d73ded4dffefedd5c`）的10exists＋旧fine absent精确。

**唯一补证路线：明确late exact-HEAD旧route重建。** 在当前已迁whole后，不能再制造“真正产品前全hex”。executor先逐byte封存当前精确11source状态（10exists与fine absent）、必要测试/observer源与metadata；再只在已授权路径/fixture观测范围内重建依赖HEAD `3118ad0`的旧route生产bytes，并用原old-route必要测试、完全同四case/seed/raw输入/action/native触发点运行。重建的每个旧生产路径必须与该HEAD逐SHA/bytes相等，observer仅改显式`hex(item_core)`等BLOB无损输出、保原其它字段/原值，不复制旧输出当新运行。保存此次真实UTC/command/cwd/HEAD/exit/完整raw/源SHA与比较脚本，文件/输出标签均明确`late reconstruction`；原before、reconstruction-old、当前new三层来源不能合并冒充一个前时点。

重建期间不得转成假provider/participant/业务plan或改内容/规则/schema；仍同provider、Current实际participant、closed实际事务/entry证据，native须FK1/真实child/staged余额或票券/参数/receipt/实际Commit19/787，action为真实19/1811。finally逐byte恢复封存的10exists/fine absent，再核全部11路径SHA/bytes/删除状态、whole原SHA与非owned边界。原测试最小source/旧raw保留不改，只新的必要observer源按现授权修订。恢复失败立刻保材料报主控并停产品/正式运行，不借重新生成文件或扩大scope遮掩。

**时间列的窄关系比较。** 已定位`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:407`中items.created_at/updated_at的`DEFAULT CURRENT_TIMESTAMP`，以及`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:348`更新时的`updated_at=CURRENT_TIMESTAMP`；它们是SQLite记录时间，跨run受实际SQL时钟影响，不能靠业务TimeProvider声称固定。只允许跨旧重建/new运行比较中，对**items.created_at与items.updated_at这两列**记录原值、所属run/row/来源及记录时间关系，不要求独立run字面相等；不得删除这两列或改生产时钟/schema。每个run内失败前/确认回滚后的两列与完整item_core仍必须精确相等，其它所有确定资产、UID/slots/count/parameter/帧逐字段/byte等价。若差异超过这两具名记录时间列、core改变、真实行为不等或阶段不成立，保原材料报告主控，不默增归一字段、不忽略整row/core/全部时间。

**重新冻结与审查。** formal01已自然完成：完整Release0/0，App5058＝5040pass＋两准确原Dimfail＋16skip，Protocol1771pass、Networking199pass、其它四control各1pass；它们是freeze01实际历史，保留全部原meta/console，不能充freeze02最终证据。补观测器/对照并恢复后重新freeze02，按§6完整Release＋无filter三full＋四actual controls正式02自然跑完，原并行/5m/期限/Skip不变；Counter对Task18原6981逐display/status/multiplicity核，预期N47但只以最终实得N－D计延续8781＋N－D，另1800只Task15库存。原mutation产品whole不变且目标语义未变时按§6保原RED/GREEN来源，不造重复RED；其它源或目标有变则据实际重核所需检查。最终source/manifest/preclose/strict原八坏副本/Result prefix统一指向r2及freeze02，required freshreview必须明确判断原缺口与此补证方法是否足够，不能将本主控处置当approved。

## Result

### R1 — TASK19 r2／2026-10-25（实际实跑UTC 2026-10-24），待 required fresh 独立结果审查／主控检查点

- Outcome: done
- **Changed:** 六已注册logical65841/65842/65843/65844/65944/65967及精确WarehouseRequest全部由唯一Direct／Protocol `GameWarehouseCommand`接管；thin handler只交原Input/Dispatch给同bus。旧WarehouseCommand.cs的record／handler、唯一Tx注册及真实caller删除，两旧marker名单各只减WarehouseCommand。精确11物理source＝7existing修改＋3新增＋1删除；最终10文件存在，旧fine1751bytes／HEAD-before SHA `27e7502e7fffa8a4bf98a57ab3fb663648646c6b3adab7ceff24dabbd2f8e351`，最终absent。source-freeze-02 SHA `8dddc74accee5ed893366f9d12c1d365faf5a999f1537b5738bbaeef3698b575`，whole始终原SHA `149f5634ecbbfb75d281eb42a97d36ea25497bddada53959c77a88246640a050`。父HEAD3118ad0907b69e7207bb910564c247c846c51eaa／原branch，未stage／commit。
- **业务与边界:** 原FindByConnection／online／ticket Main-slot映射后核最初Selected及精确session，仅原有效分支一次Main，最低Inventory；初始Rejected不因稍后选角升权。domain CurrentAsync的InWorld／dungeon／tower／owner、signed金币／等级／阶段／材料／限额仍原位置，正常Refused不RequireRollback，仍MainCommitted。原domain无recorder／ApplyEffects／自开事务，未添加cap、NonRequest、领域接口或额外事务边界。TransferGold仍cargo value32条件UPDATE→character gold；UseItem仍Take/Delete一票→character INSERT-if-absent后list_param16 UPDATE。SnapshotAsync原当前Tx ApplyDecidedWrites后加载character/account的receipt原样给composer，不postcommit重读首响应。0131–0134首ACK→Main→Account，0198 ACK→Main→Personal，0133/0134成功ACK金币取原receipt；01AF成功仅Main NOTI，拒绝和普通异常全部静默，无任何ACK编码路径。重放只读current、不重Main／结算／原ACK。
- **异常／投递保护:** 窄action try只包原clock.GetUtcNow及原实际domain调用，action E经RequireRollback(E)后完全退出unit，才mapper／log／ACK；真实已知CommitRolledBack消费原Cause同mapper，未catch整个WriteResult。preMain mapping catch外才登记拒绝ACK，ACK抛mapping不会被吞。domain mapping→InvalidRequest、InventoryExpansionException→原Rejection均无whole handled Error，保原refused Info；generic→StateChanged、原Error cause/级别。具名日志实际late旧route证明mapping／Expansion原fine pipeline1Error→whole0、generic旧pipeline＋handler2Error→whole1，不能统一称重复日志二减一。原NotiBus首成功encoder Failed仍逐目标隔离／Committed／nofence；改变资产后同seq只current。直接ACK earlyRejected／normalRefusedCommitted／ConfirmedRollback各默认出口，含ACK抛mapping；真实SQL E＋独立rollback X（不同对象／同一对象两次）保两cause、回滚资产、Blocked／fence／sourceclose。clock六注错／cancel／typed及共享原guard/Unknown/sticky/defaultWrite/ReadOnly和两B2出口反例均保。只有既有CommittedReplayCurrentStateBatch，无新raw许可。
- **原强测试保持:** WarehouseStorage原29个display/status/multiplicity全保，stale owner继续Throws、原SQL异常/回滚、tower／wrongownership、account create／upgrade／materials、signed金币／限额、personal容量、跨角色UID/core/details、20并发保总量、binding／seal与capacity断言未删弱；原codec和01AF无响应handler条件不改。具名typed WarehouseDomainTestDriver每调用同provider独立TestGameplay、Capture(owner,null)／current前后、原clock／实际domain、真实Tx Commit／catch Abort(E)原抛／finallyDispose，无footprint／任意Func平台／第二provider。原直handler成功case改外部真实decoder／dispatcher／完整metadata和新fresh seq，保提交后余额／容量、ACK第一和三帧长度；原seq7 deposit100→seq8 deposit250→seq7 current350/99650及无重复ACK完全保持。新六类型各成功／正常拒绝、GoldLimit95/94、early无Main、late选角不升权、真实tower／owner／DungeonRun、minimum／原receipt／帧序均实跑。world=false为本地受控IsInWorld查询，其余权威和current转发同实际singleton，明确非自然registry状态。
- **真正before与r2明确缺口:** 产品/doc四路径仍逐byte等HEAD、新whole absent时先fresh capture-before-01和pure runtime-family-before-01；随后只增最小observer／flow在旧route old-oracle02实跑四wire＋原业务33/33，原源/UTC/meta/console全部封存。原SELECT * items经Convert.ToString把BLOB记作System.Byte[]，失败stage完整core hex没有在真before记录；此缺口如实保留，不能称原时点全core已取证。原当时的实际SQL19/1811／真实SQLite Commit19/787、FK1／child／staged余额或票数／参数／receipt、UID／slots、全部firstFrames和replay hex及same-entry原Ordinal retry仍有效，未覆盖或回填原材料。
- **r2有界late补证:** 主控task-rework及TASK19 r2 §8/readiness后先保存r1原27120B/SHA ccd77703…与r2原33624B/SHA8bd7f3f5…前缀及11physical快照；新的late exact-HEAD重建明确发生于迁后。最终`late-reconstruction-r2-02/manifest.json`记实际16:28:58～16:29:08UTC、四旧产品逐SHA/bytes等3118ad0、old-minimal和named测试源、11restore-snapshot及finally11physical精确恢复/fine absent/whole149f…；late-core02四pass、late-named03三pass。四路径仍原同provider实际participant，deposit value32 UPDATE／01AF list_param16 UPDATE各action19/1811与真实receipt／ApplyDecidedWrites后SQLiteTransaction.Commit19/787；Main票2660296确为Main参数8、原placement可达，失败stage先票2→1、native参数0→8／child1，确认回滚恢复所有资产／UIDslots／原参数、deposit ACK10／01AF完全无帧。去trigger同fingerprint、同entry原Ordinal retry恰一次，随后current-only replay不重结算。native Freeze后projection可非null，未清main claim或重写Ordinal。补证可接受性仍由required freshreview判断，本主控处置不是approval。
- **精确比较范围:** `oracle-before-after-checks-r2.json`将真before、late-old和formal02-current分三层。真before可观测的原body/hash/metadata／ACK或静默／UIDslots／数额／参数／receipt／first/replay帧保持；不假比缺失的失败stage core。late-old与current所有82B item_core及其它确定字段、slots、receipt、全部帧逐字段／byte等价；仅items.created_at／updated_at跨run作具名实际SQL UTC秒时间关系，保24条run/row/path原值、updated>=created及真实runner窗口。SQL来源Schema:407 DEFAULT CURRENT_TIMESTAMP和SqliteItemDomainStore:348 updated_at=CURRENT_TIMESTAMP，非fixture业务Clock；同run真实rollback前后全列／core／两时间依然精确相等。没有忽略整row／core／其它时间／随机字段。首late-core01把optional hex(NULL)转空串的观测表示诊断及原raw保留，最终core02保SQL NULL、nonnull BLOB无损hex后重跑，未改schema/生产时钟/领域。
- **有价值mutation与恢复:** `mutation-result.json`保原whole TransferGold signed参数错误Math.Abs(-100)→+100的编译成功RED：真实cargo0→100／carried100000→99900／成功ACK body0164000000（原5B金币响应），资产oracle失败1、六真实合法类型正控pass。finally精确恢复whole149f…，完全同七目标GREEN7/7后freeze01；r2仅observer无损表示和AGENTS当前事实补正，whole与mutation目标／资产断言不变，沿r2允许复用原RED/GREEN，不重复造RED。无编译／fixture／registry错误冒称行为RED。
- **最终正式同源检查:** freeze02后`task-19-formal-*-02`全自然结束，完整Release --no-incremental exit0／0warning／0error。App无filter full5058＝5040pass＋两准确原Dimensionfail＋16原skip，Protocol1771pass，Networking199pass；三full7028＝7010pass＋2fail＋16skip。两失败精确Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，仍Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException。四其它actual exact control-only各1pass。DFO原74条（App62／Net7／Protocol1／其余四各1）、七uniquecontrol、outside/exe/orphan0，新故意违规0；exe0仅观察。Golden15逐byte同时对f01dc226与HEAD，Characterization10逐名pass／0skip。原类并行／5m／deadline／Skip无变化；formal01 freeze01整轮历史保留、不代最终02。Net两轮199绿不代表Task17旧Bind48根治。
- **数量／捕获／闭账／源边界:** Task18有效原三full6981逐display/status/multiplicity全部保，App N46／Protocol N1／Net N0、D0／rename0，延续8828＝8808pass＋2fail＋18skip；另1800仍仅Task15 final03库存（含Infra2skip未本次业务full重验），四control另列，不称八full或单CMD≤2秒完成。fresh capture-after-02／pure family实得173logical／203cmd保持，仅六message route及一oldfine→whole变化：27Protocol＋1Internal＋175Legacy，147Tx／43Direct／9ReadOnly／4Workflow，message37Protocol／136Legacy，telemetry14；六精确WarehouseRequest绑定／原全family/Matrix/exits严格保持。只闭七授权ID，其余baseline/original/rows等HEAD、md原前缀保持；preclose已真通过，追加本Result后strict与原八坏副本证据及最终全physical SHA/bytes／删除条目／R1prefix身份见`ledger-strict.json`、`ledger-eight-bad.json`、`manifest-r1.json`。非owned13557项除两授权ledger外13555逐SHA不变；WarehouseMessages全文件、Handler原composer区域及原case声明byte核，stage空。所有命令/cwd/HEAD/UTC/exit/elapsed/resources/完整console见`verification/logs/task-19-*`，证据根`verification/evidence/task-19-inspection/`。
- **Remaining:** required fresh独立结果快审必须判断r2late补证对原core缺口的充分性，随后主控精确消费／本地检查点；执行者未自批、stage或commit。pending122message／175fine／72source／5protection／8transition、T012最终新八full／单CMD≤2秒、Astra与Spec／根封存／用户分支收尾仍后继，不称B或T-004完成，不创建Task20，不push／PR／merge／tag／Release／版本／issue／游戏／部署／cleanup。交付后停全部共享写及native，freshreview只可追加到R1冻结前缀之后。


## Independent Quick Review R1 — TASK-19 r2／Result R1（独立审查者 b_task19_review，2026-10-25）

```yaml
kind: rks-review
verdict: approved
```

### 对象、判据与冻结身份

本轮是 TASK-19 revision 2（含 §8 有界方法修订）／执行 Result R1 的 required fresh 独立局部快审，采用 reviewing-work 的 Task Unit、Code、Deliverable 判据。已读当前 AGENTS、架构哲学与重构偏好、Spec B2 本 Task 引用的 RQ／AC、Plan P3 T-004 与显式 Main／窄恢复／先旧后新取证要求，以及依赖 TASK-18 的 Result、独立 approved 与主控消费。未把主控 task-rework／readiness、计划改字或依赖批准当成本轮批准；未另派 agent，未运行 native／build／test，未写产品、测试、账本或证据，未 stage／commit。

基点为 `3118ad0907b69e7207bb910564c247c846c51eaa`，分支 `task/refactor-session-20261021`，工作区为本契约指定 worktree。追加前独立实核 Result R1 全前缀 **44071 bytes／SHA-256 `6a70036b03749b8c206a8e60974a6594c0c5aa8d441c9b8f387934ede8d90300`**，等于 `task-result-r1-prefix.md`；r2 计划前缀33624 bytes／SHA `8bd7f3f52fe265e1edc4dfd78aa92d91f8da1abeaec2be097652d1426a38220c`；保存的r1原文27120 bytes／SHA `ccd77703fd464a1e9cfb480ee6d9fd1d7e1249060e98b2dcf8c50fce91aba95d`。`source-freeze-02.json` SHA `8dddc74accee5ed893366f9d12c1d365faf5a999f1537b5738bbaeef3698b575` 的10当前存在源逐一匹配，另1旧fine确实不存在；实核分类为7修改／3新增／1删除。`manifest-r1.json` SHA `e9d2f71c69e66b304f80ebb95074bde7e6dd7f6127f03b30a04d546db4489158` 的223条（222存在SHA／bytes＋1删除）全匹配，加Task及manifest恰为 `git diff --no-renames` 与untracked的225实际物理变化路径，没有漏项或额外路径，暂存区为空。

### 决定性检查

1. **六条接管闭合，旧fine真实删除。** 实读 HEAD 与当前产品完整差异：65841／65842／65843／65844／65944／65967 全部用精确 `WarehouseRequest` 绑定唯一 Direct／Protocol `GameWarehouseCommand`，thin handler只交原 Dispatch／Input 给同一bus。旧 `WarehouseCommand.cs` 的record／handler、Tx注册及真实caller删除，没有兼容转发；源内精确旧名只余新注册测试的删除断言。删除项HEAD-before实核1751 bytes／SHA `27e7502e7fffa8a4bf98a57ab3fb663648646c6b3adab7ceff24dabbd2f8e351`，没有以rename漏审。两旧marker名单各只减本fine；AGENTS仅补本族当前事实。WarehouseMessages全文件保持，Handler从WarehouseChanged起的composer后缀与HEAD逐byte相同。

2. **原事务／领域位置和首receipt保持。** whole自己承接身份、槽位映射、原clock／domain调用、异常分类、日志及投影；没有bus／sender／locator或额外事务。原有效selected分支才开唯一Main、参与者是最初精确actor，初始Rejected不因晚selection升权；early/mapping拒绝不Main。实读未改domain：InWorld／dungeon／tower／belongs-to仍在CurrentAsync内，正常Refused不RequireRollback而MainCommitted；原域无recorder／ApplyEffects／自开事务，未强造cap／NonRequest／接口。TransferGold仍先cargo value32条件UPDATE、后character金币；UseItem仍先Take/Delete一票、后容量INSERT-if-absent及list_param16 UPDATE。SnapshotAsync仍在原Tx ApplyDecidedWrites后读character/account并交出receipt，首投递未commit后重读。最低Inventory和原动态SQLite facets保持。

3. **窄恢复与日志分类没有外扩。** 窄try只包clock.GetUtcNow及原实际domain调用，action E以RequireRollback(E)保存原原因，单元完全结束后才mapper／log／ACK；已知CommitRolledBack消费原Cause。实读只读CommandWorkUnits／CommandWorkUnit：实际CommitAsync span、KnownStorage同cause／ConfirmedRollback／guard与sticky复查、current／全部participant及cleanup要求保持；whole没有外catch整个WriteResult，投影／编码不在catch。具名mapping→InvalidRequest、InventoryExpansionException→原Rejection均不记whole handled Error，普通E→StateChanged保Error cause／级别和原拒绝Info。late具名旧日志是原pipeline1＋handler0→whole0；普通故障是原pipeline＋handler2→whole1，不能合称全部二减一。clock六种注错、cancel／typed、ACK抛mapping及E＋独立rollback X（含同对象再发生）均有真实通过用例，后两cause／Blocked／fence／source-close没有被普通拒绝吞掉。共享机制未改。

4. **真正迁移前证据有效，但原core缺口没有被消失。** 实读原meta／console、保存生产bytes和old最小测试：capture-before在16:12:32～16:12:41UTC，pure family在16:12:48～16:12:51，旧route old-oracle02在16:14:25～16:14:34真实33／33（原29＋四wire），生产四路径仍精确HEAD且新whole absent。原actual action19／1811与native SqliteTransaction.Commit19／787、FK1／child／staged金币或票券／容量／receipt、UIDslots、body/hash/metadata、firstFrames及replay字节仍可用。原Rows用Convert.ToString将item_core写作System.Byte[]，**失败stage及回滚前后完整BLOB hex并未在真正前时点记录**；本轮不把旧输出或计划文字当成已经取到的全core证据。原named旧route日志也始终按late reconstruction判定。

5. **r2补证对本局部行为保持充分，理由来自实际源与运行，而非处置授权。** 已独立核late-reconstruction-r2-02脚本／manifest／snapshot：16:28:58～16:29:08UTC发生于迁后，四旧生产路径重建bytes逐一等依赖HEAD和原before快照，whole虽存在但Module不注册它；所用old最小测试逐文本只变化两处items无损SELECT及nonnull byte[]转hex，原四case、seed／raw input／trigger／actualdomain／participant／失败阶段和断言不变，未改业务、schema、内容或clock。原SELECT全列与新显式22列顺序相符，optional BLOB保NULL。四oracle实际4／4与named3／3有各自raw／meta；finally的11物理before／after逐SHA相等，独立核10快照都等当前文件、旧fine恢复absent、whole仍 `149f5634ecbbfb75d281eb42a97d36ea25497bddada53959c77a88246640a050`。late01把hex(NULL)记空串的表示诊断仍保留，最终采fresh02，没有覆盖原材料。相同旧实现、只读且无损的observer与相同真实业务故障结合原真before完整wire／非core事实，足以弥补本次等价比较所缺的core观测；这不追认原前时点曾录全core，也不外推全部协议或A21实机。

6. **三层原始对象与时间比较独立重算通过。** 已读并以禁止写出文件的方式重新执行task19-oracle-compare-r2.py全部断言；另外独立从old-oracle02／late-core02／formal-App02原console提取四对象，逐对象等保存observations，没有拿派生JSON替代raw。late-old／current除真实stack、上述generic日志和两具名记录时间外整个对象相等：所有82Bcore、其余item字段（含NULL）、UID／slots／数额／参数／receipt、ACK或静默、完整first／replay帧、原entry.Ordinal及其它状态皆精确。true-before只将明确缺失的core另标不可比较，再核其余确定事实和完整帧相等，三层没有合并。时间仅items.created_at／updated_at两列：Schema:407的DEFAULT CURRENT_TIMESTAMP及ItemStore:348的updated_at=CURRENT_TIMESTAMP确为SQL来源，非业务TimeProvider；每原值／run／row／path均保留，重算24条UTC秒值、updated>=created及各实际runner窗口通过。每run失败前与确认回滚后的AssetState全列、两时间和core仍由实际查询精确相等断言保护；未忽略整row、全部core或其它时间。

7. **真实阶段、重试与出站边界受资产判据保护。** 四oracle各经同provider SqliteDatabase的Current参与者读取FK1、child及未提交资产；它会按同连接字符串Enlist原participant，未另开provider或用fixture外部Scalar冒充stage。deposit为value32 UPDATE、01AF为真实list_param16 UPDATE：action19／1811发生时先票2→1；native action已返回完整receipt／ApplyDecidedWrites，票1／Main参数8或cargo100／carried99900／child1已stage后才真实Commit19／787。回滚后所有资产还原，deposit完整10 ACK，01AF完全无帧；去trigger同fingerprint／同entry原Ordinal只再生效一次，随后只current重放、无原ACK或重复结算，native Freeze后projection可非null。其它首成功仍ACK→Main→Account，0198 ACK→Main→Personal；01AF所有结果无ACK、成功只有Main。NotiBus首编码Failed仍逐目标隔离／Committed／nofence，改变资产后的同seq只当前库存；直接ACK的earlyRejected／normalRefusedCommitted／ConfirmedRollback默认保护保留。注册只既有CommittedReplayCurrentStateBatch，没有新增raw许可。

8. **原强测试与mutation有实际价值。** 原WarehouseStorage29个display/status/multiplicity及case声明bytes保留，源diff未删弱Throws／SQL／UID／core／容量／binding／seal／20并发等断言；typed Expand每调用同provider、独立TestGameplay、Capture(owner,null)与current前后、原clock／实际domain、真实Commit／Abort(E)／finally Dispose，没有任意Func平台。原直handler投递case改真实外部decoder／dispatcher／完整metadata／fresh seq，保提交余额和首ACK／三帧断言；原seq7→8→7 current350／99650断言未变。world=false只代理IsInWorld，其余authority/current转发同真实singleton，已明确非自然registry状态；tower／owner／DungeonRun仍自然领域状态。mutation只whole的signed amount误用Math.Abs(-100)，原raw编译成功、cargo0→100／gold100000→99900／成功ACK 0164000000导致资产RED，六合法正控pass；finally恢复whole原SHA、同七目标GREEN7／7。r2只改observer／当前文档且目标whole不变，复用这份RED/GREEN足够，不需再造重复RED。

9. **最终正式02与数量、闭账范围没有夸大。** 已读完整Release原console与meta，freeze02后no-incremental 0warning／0error；三full均无filter、保原并行／5m／期限／Skip。独立从raw逐display/status/multiplicity Counter对Task18有效App01／Proto01／Net01，原6981全部保留，App N46＋Proto N1＋Net N0、D0／rename0。App5058＝5040pass＋准确原Fusion／All_six两Dimensionfail＋16原skip，仍Dungeon7100／maze0／cell(1,0)／entrance Left；Proto1771pass／Net199pass，三full7028＝7010pass＋2fail＋16skip。四其它actual exact control各1pass另列；延续8828＝8808pass＋2fail＋18skip中的另外1800仍仅Task15 final03业务库存，含Infra2skip未本Task业务full重验。独立核七DFO JSONL实际74条及原逐类count（App62／Net7／Proto1／其余四各1）、七uniquecontrol、outside／exe／orphan0；exe0仅观察。Golden15逐byte同时等f01dc226及HEAD，Char10原console逐名pass。formal01保持旧freeze历史，不冒最终02或八full；Net绿也不称Task17 Bind48根治。

10. **清单与ledger局部闭合准确。** 独立重算fresh capture／family：173logical／203cmd保持，只六message路线及一fine→whole变化，27Protocol＋1Internal＋175Legacy、147Tx／43Direct／9ReadOnly／4Workflow，message37Protocol／136Legacy，telemetry14；其它family／Matrix／exits逐项不变。ledger对HEAD逐group／row只七授权ID变化，baseline／original及其它行不动，md原前缀逐byte保留；preclose、Result后strict valid与原八坏副本全部拒绝有各自原meta／console。非owned13557库存除两授权ledger外**13555逐SHA不变**，无隐藏core／schema／共享fixture／协议变更。初期compile／route／fixture／late01表示诊断均保留，没有当作行为RED或新增失败基线。

### 判决与局限

本 Task revision 2／Result R1 局部 **approved**：未发现未决blocking defect或必需证据缺口。r2明确late补证与两列时间关系方法对这个Warehouse局部迁移充分；原真正前时点完整core缺失仍是历史事实，不被本批准改写。可交主控消费精确R1前缀、freeze02、manifest与本轮追加审查后决定本地检查点；本审查未复跑产品测试，也不改变Task／Spec生命周期或替代主控完成验证。

pending122message／175fine／72source／5protection／8transition、T-012最终新八full与单CMD≤2秒、Astra及Spec／根封存／用户分支收尾仍待后继；不称B、T-004或全协议实机完成。未执行push／PR／merge／tag／版本／游戏／部署／cleanup。审查追加完成后停止写入。

## Controller Consumption R1 — 2026-10-25

主控已完整读取TASK-19 r2／Result R1及fresh b_task19_review的Independent Quick Review R1（approved），消费审查后56611bytes／SHA `0428701ec836aee50ba16c11d2d4d9f8bb3c090f070475d4992d6cf5b62e6ee6`。已独立核R1 44071bytes前缀、freeze02的10当前源＋旧fine absent，manifest SHA `e9d2f71c69e66b304f80ebb95074bde7e6dd7f6127f03b30a04d546db4489158`全部223条（222存在SHA／bytes＋1删除及其HEAD1751bytes原SHA）、225物理变化集合，stage空／精确父HEAD。已独立核Golden15对f01dc226、ledger仅七ID变化及原case声明／composer byte保持。

主控独立从原console复算Task18原6981逐display／status／multiplicity全部保留，N46＋1／D0，正式02三full7028＝7010pass＋两准确原Dimfail＋16skip；四actualcontrol另列，另外1800仍Task15库存，延续8828不冒八full或单CMD测量。已消费原DFO74／七control、strict及八坏副本、完整资产／异常保因／无新raw的证据。

主控只读重算r2四wire／24条时间关系比较全部断言，并独立核11restore-snapshot身份、finally相等及四旧产品actual HEAD SHA／bytes。已消费freshreview对r2充分性的独立判断：原真正before的core仅System.Byte[]缺口仍保；后来的exact-HEAD／无损observer补证明确标late，不能倒称pretime。只两items记录时间跨run关系比较，原值及同run rollback全列／core精确保持；其余确定资产、UIDslots与完整帧等价。主控技术处置与本独立批准不同，不以改契约替代证明。

授权精确225路径本地检查点及Co-Authored-By，不push／PR／merge／tag／版本／issue／部署／cleanup。提交后继续P3未完成范围（122message／175fine／72source／5保护／8过渡）；下一CollectionBox首次成功batch实际encoder的旧raw兼容问题须先受影响Spec／Plan修订及fresh门，不在Task中暗扩许可。A／根保持封存，T-012／Astra及用户收尾仍待。
