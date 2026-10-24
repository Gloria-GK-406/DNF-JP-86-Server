# TASK-19 — Warehouse 六消息族完整效果协议 CMD

## Task Unit Contract

- Revision: 1（同时标识本契约与详细计划）
- Outcome and references: 交付[Plan P3](../plan.md) T-004 下一有用单元：Warehouse 六个已注册 logical 共用唯一 `GameWarehouseCommand`，吸收原 handler/fine 的身份、槽位映射、时钟、领域调用、异常映射、日志及投影，删除 `WarehouseCommand`。保持一段真实 Main、最低 Inventory 投影、原 receipt 快照、金币/材料/票券/容量与回放语义，特别保留 `0x01AF` 无任何 ACK。覆盖[Spec B2](../spec.md) RQ-001、003～009、012、014、015，以及 RQ-013/017 的本簇迁移记录与当前文档；AC-001～005、007～009/011 的局部义务及 F-34/F-88/F-93/F-112。未迁来源、最终清零、最终八 full/测量仍由父 Plan 后继承担，不宣称 T-004 或 B 完成。
- Owned boundary: §2 精确11个生产/文档/测试物理路径、本 Task 与 task-19 新证据；`migration-ledger.json/md` 仅 §7 六 message ID 与一个 fine ID。其它 DTO/codec/composer、领域/规则/接口/存储、reader/sink、transport/helper、共享 core、父契约/Spec/P3/root/A/旧 Task/review/证据只读。不得拆成六个 whole，不扩大到 Move/Sort 或创建共享协调器。
- Required checks: 产品前 fresh registration capture/family、旧完整 wire oracle，迁后同输入/同 provider 实际对照；deposit 与 01AF 各 action SQL/真实 SQLite Commit 两阶段，资产/receipt/原 ACK 或无 ACK/entry 原 Ordinal retry/current replay；六类型成功/拒绝、正常 RefusedCommitted、早拒无 Main、mapping/具名领域异常/时钟分类、原 NotiBus Failed 与直接 ACK 默认保护、独立 E+cleanup 保因。原纯领域业务/Throws/SQL/UID/并发与 codec/Golden/表征断言保留，owned whole 有价值可编译 mutation 红绿恢复；完整 Release0/0、App/Protocol/Networking 三 full、另四 actual DFO controls、原74/七control/新违规分类、Golden15/Characterization10、fresh capture/strict/原八坏副本、逐 display/status/multiplicity 的 N－D 与源边界核验，详见 §5～7。
- Dependencies: 仅 [TASK-18](TASK-18.md) revision1／Result R1／fresh required 独立 approved 与主控精确消费后的 committed done，本地检查点 `3118ad0907b69e7207bb910564c247c846c51eaa`（parent `c03ae3476d52fe8afaf9165047877fb7958ae046`）。其已消费的 Main/唯一 ledger/typed driver、Task15 WriteResult/带 cause/来源与后续保护是传递输入，不另列并行依赖、不重开共享 gate；Spec baseline 保持 `10a7218a779493494b138bd6b43cc0fc1a84aa6a`。Task01～18 已唯一 committed done、DAG acyclic、19 unused 的控制器结论沿用。
- Result review: required — 错误迁移可能在账本错误允许重试时重复扣金币/票券、错误升级容量、丢账号与角色两边一致性，或把已提交/guard 故障伪装普通拒绝；编译、codec 和总用例数不能证明这些状态。fresh 独立结果快审须核原前时点、真实阶段/资产/异常与删除路径及七 ID，主控消费后才本地检查点。Task-plan 由主控检查 readiness，不增加独立 Task-plan 审批门。

## Detailed Plan

### 1. 已接受起点与真正产品前顺序

实施者先核 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`、branch `task/refactor-session-20261021`、HEAD 等于依赖检查点、唯一写者与 owned 状态；读当前 AGENTS、架构哲学/重构偏好、B2/P3及依赖完整结果/审查/消费。无 `.codegraph/` 则不调用 CodeGraph。继承根授权的中文、本地实施/验证/主控 checkpoint（Co-Authored-By）；不 push/PR/merge/tag/Release/版本递增/远端 issue/游戏/df_game_r/DNF.exe/部署/cleanup，不创建 TASK-20。出现白名单外必要 caller、接口或共享机制依赖，保 source 与最小方案报主控，不静默扩写。

当前延续库存 **8781＝8761pass＋2准确原 Dimensionfail＋18skip**；最新 Task18 同源三 full 为 App5012（4994pass/2fail/16skip）＋Protocol1770pass＋Networking199pass＝6981，Task18 N20/D0。其余1800仅 Task15 final03 业务库存：Host203、Http57、Infrastructure976、Persistence484、Pvf80（其中 Infra2skip 未在 Task18 业务 full 重验）。四 control-only 实际数另列，不称八 full，不称本 Task 性能验收完成。

当前 capture173logical/203cmd＝26Protocol＋1Internal＋176Legacy，148Tx/42Direct/9ReadOnly/4Workflow；message31Protocol/142Legacy；pending128message/176fine/72source/5protection/8transition。

**先保存前时点，再迁产品。** 最小 preflight 保存 owned existing 原 bytes/BOM/行尾、三新增预期 absent、删除清单与非owned tracked SHA，不复制整仓/旧档/bin/obj。此时 Handler、旧 Command、Module、AGENTS 四 existing 生产/文档路径仍逐 byte 等 HEAD，新 whole absent；真正运行 fresh ConfigureServices-only registration capture 与 pure family sidecar（只构造实际 registry，不 BuildProvider/启动业务），保存四 existing/第五 absent 的 SHA/bytes、实际 HEAD/cwd/command/UTC/exit/raw 输出。使用现 `verification/probe-tools/migration-ledger.py` 及 Task18 family 方法，工具只读，不新造共享机制门。

完成 fresh-before 后，只增 owned 最小测试/observer，在**旧 route** 实跑 §5 四条完整 wire oracle及原定向业务，保存最小 old-route 测试源、raw/meta和当时产品 SHA。旧/新方法须先核实际 write 点；若旧实得否定异常/无 ACK/事务假设，保失败日志向主控报告，不先迁产品再用 HEAD 重建冒称 fresh-before。before/focused/mutation/formal 各轮不同 prefix，诊断文件保留。

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

产品前 capture 后新增四 oracle，各从同 provider 的实际 singleton domain 经真实入站、ledger、原 Tx/SQLite/outbox 路径运行，迁后同 seed/raw metadata/body/hash/seq/oracle 重跑：

1. **deposit action**：fixture account cargo 可用且足够 carried gold，actual `account_cargo_state.value32 UPDATE` 设 RAISE(ABORT) trigger，实际 SqliteException19/1811/堆栈证明 action 阶段，不称 native commit。错误10完整 ACK、原 Error cause/级别、两余额/其它资产还原，entry ConfirmedRollback/无 fence/source close。
2. **deposit native**：fixture DEFERRABLE FK child，由实际 value32 UPDATE 的 trigger 产生 child；同 provider **当前真实 participant 的 SqliteDatabase 连接**证明 PRAGMA foreign_keys=1、child存在、account 已增/character 已减、domain完整 receipt 已返回，然后真实 `SqliteTransaction.Commit` 抛19/787。不能用 fake participant、单独 provider、外部手插坏行或外部 Scalar 读取 staged 状态代替。
3. **01AF action**：真实可用 Main upgrade ticket（原 ExpansionContent 中2660296→Main参数8，50/57仅Personal；先核实际 ItemDefinition/placement可达及原业务当前 parameter，不改共享content或用假plan），actual `character_container_state.list_param16 UPDATE` 触发19/1811；先有真实票券消费 stage，随后容量更新失败，回滚票券/UID/slots/容量，**无任何 ACK/成功 NOTI**。
4. **01AF native**：同实际 list_param16 UPDATE 触发 deferred child，真实 domain receipt/ApplyDecidedWrites 后，同 Current participant 记录票券已少1、Main parameter已升级、child/FK1；真实 Commit19/787 后均恢复且完全无 ACK。此接口原 ChangeCharacter 先 INSERT-if-absent 再条件 UPDATE，trigger须命中真实 UPDATE，不能仅挂 INSERT 或猜列名。

每 oracle 保存 raw E 引用/level、真实阶段/Completion/entry/Ordinal/最低 needs、Frozen projection 事实、前后金币/货仓参数/全部票券 core UID/slots/容量及适用事件/投递/冷却资产。阶段内读取经同 provider SqliteDatabase.ReadAsync/WriteAsync 当前 participant；ShopFixture.Sql/Scalar 只 seed/DDL/前后 durable 查询。DDL仅 fixture生命周期，不改生产 schema。失败无新增 fence/source-close（以实际旧 route 证据为准）；去 trigger 同 fingerprint、**同 entry 原 Ordinal** retry 恰好一次真实变更，后同 seq 只当前库存/无原 ACK/无重复金币或票券。native Freeze 后 projection 可非null，下一 admission 才清，不能清 projection/main claim 或重写 Ordinal 来满足断言。

旧与新对照 raw 帧 body/hash/顺序、ACK error、silent branch、确定资产字段/receipt/真实 stage 逐字段；时间字段用同 deterministic TimeProvider，无法固定的真实字段保原值并精确列明关系比较范围，不忽略整帧/全部 item。原 action trigger 不是 native；本四 oracle 也不冒称六分支所有 native/实机均验收。oracle异常/fixture不可达保原日志、具体源定位并向主控收敛，不能给假票券成功/放宽 native要求。

新 flow 还须覆盖六类型真实成功与拒绝/错误码；normal domainRefusedCommitted 对 early/mapping/noactor Rejected无Main；Main只一次/最低Inventory；InWorld/dungeon/tower/owner 判定仍在原 domain时点。初始Rejected晚selection不升权、下一fresh可正常，受控 query如必要须注明非自然 registry 路径，不伪造捕获权威。

typed forwarding observer 分别把 InventorySlotMappingException、InventoryExpansionException 和普通 E 送入实际调用 span，验证前两 whole 不记 handled Error/对应码、普通E有Error/StateChanged，并按 §3 分开核原pipeline日志，不伪称named异常旧新总Error数恒等；clock注错仅用本地有界TimeProvider，仍在原fine对应action try的原顺序内，验证同分类及cancel/typed不确定不恢复。此类注错只证明映射，不能替代四真实 SQL oracle。域无 cap/footprint，因此不造假 cap guard反例；共享 Main/current/guard、defaultWrite/ReadOnly、Unknown/来源/facet/closed/两 B2许可等原完整反例继续随 App full 运行。

原首成功通知 encoder Failed 经实际 NotiBus 隔离，仍 Committed/no新增fence；随后改变当前资产再同seq重放，证明只 current/无重复结算。直接拒绝 ACK 编码失败分别覆盖 earlyRejected、normalRefusedCommitted、ConfirmedRollback 实际保护出口（原 cause/source close；已提交默认typed/精确参与者fence）；特别 ACK 抛 mapping 异常不应落业务 mappingcatch。01AF从来没有 ACK编码注错路径，不用虚构 ACK证明保护。给本族一条真实action E＋独立 rollback/cleanup X连接性，含不同对象及同对象两次发生的有界参数化，保E/X、真实资产恢复、无普通成功结束、默认Blocked/fence/sourceclose；不是 fake commit故障当 native。取消/guard/登记sticky不能被业务 catch恢复，禁止新增 raw例外。

### 6. 有价值 mutation、冻结与正式检查

仅 owned whole 的 deposit 参数错误改为 `Math.Abs(request.Value)`，候选 wire amount=-100：正确行为原 ACK10、两余额不变、正常 domain拒绝 MainCommitted；错误实现变合法 deposit100，cargo+100/character-100/成功 ACK。新 wire 资产/帧/账本 oracle 应 RED，旁边合法 deposit100/withdraw 等正对照应通过。原纯领域 negative case不能冒称 whole覆盖。先确认原 fixture/metadata/余额可达；只编译成功且实际状态差异才算 RED，缺服务/metadata/timeout不算。finally精确恢复原 source bytes/SHA，同组完全相同case GREEN后才 source freeze，不改 domain/rules/codec来制造红。

沿 Task18 已验 native/runner/caffeinate/FSEvents 执行方法，freshprefix 完整记录 command/cwd/HEAD/UTC起止/exit/elapsed/resources/console，自然结束；不 TERM 父runner/活跃测试、不补造exit或覆盖旧输出。完整 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 必须0warning/0error。App/Protocol/Networking各独立无filter full：`dotnet test Server/tests/<project>/<project>.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1`。保原类并行、5m、deadline、Skip；共享core不改，若必须改先由主控修scope/正式full范围。

同新 DFO_GAMEPLAY_ASSERTIONS目录采三 full＋Host/HttpApi/Infrastructure/Persistence四actual原control-only，精确filter `FullyQualifiedName=Dfo.Server.Testing.GameplayAssertionRecorderControlTests.An_off_executor_violation_in_this_assembly_reaches_the_recorder`。原74 expected逐类/count（App62、Net7、Protocol1、其它四各1）/七程序集各唯一control、outside/exe/orphan0；exe0仅观察。新故意违规如本族加入须逐类/count说明，保持原expected不放宽。Pvf无control；四control不是业务full。Golden15逐byte对 `f01dc226` 与本HEAD，Characterization10逐名pass/0skip。

以 Task18 有效 App01/Protocol01/Networking01 三 full 的6981 raw display/status/multiplicity Counter比对原全保/真实N－D，延续总库存8781＋N－D；D0/rename0为目标，必须改名逐项旧→新与驱动目的，不靠Assert多重集或总数抵消。原两失败精确为 `Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，仍 Dungeon7100/maze0/cell(1,0)/entrance Left InvalidDataException；原App16skip逐身份保留，另外1800/Infra2skip只Task15库存。新Bind/SQL/fixture失败原日志保留、按源定位，不登记成原基线或宣称Task17 Bind48根治。源变则新freeze/受影响正式轮，不拼不同源的full；耗时/内存仅观察，T-012最终新八full/单CMD≤2秒仍后继。

### 7. 六消息一 fine 闭账、删除证明与交付

fresh-current capture/family与§1 before逐row比，只六message route和一old fine→whole；全量family/Matrix/exits/遥测14及其它routes严格不动。预期173logical/203cmd保持，**27Protocol＋1Internal＋175Legacy，147Tx/43Direct/9ReadOnly/4Workflow，message37Protocol/136Legacy**；以实得证明，不能硬填预期或倒填before。新六bindings同一WarehouseRequest/唯一whole，不用反射枚举补未注册消息。

`verification/migration-ledger.json/md` 只闭 `message:65841`、`message:65842`、`message:65843`、`message:65844`、`message:65944`、`message:65967` 与 `command:Dfo.Server.Protocol.Game.Warehouse.WarehouseCommand`。记录原六caller/一个Tx、原domain内快照/实际参与者/Main最低Inventory、whole/测试/Result/artifacts与各阶段保护；original/baseline/其它rows不动，md原前缀保持。预期pending122message/175fine，72source/5protection/8transition不变，不顺闭其它Inventory/来源/保护/过渡。

新证据仅 `verification/logs/task-19-*` 和 `verification/evidence/task-19-inspection/**`，现 probe-tools只读复用。source freeze/manifest以11物理source paths为边界：**7 existing修改、3新增、1删除旧fine**，从HEAD重新核实际分类。删除路径必须记录最终 absent及HEAD beforeSHA/bytes；不能套用Task18全路径exists断言、因不存在遗漏manifest，或把删除+新建冒称原文件rename而漏审。保原bytes/BOM/行尾，WarehouseMessages全文件及Handler composer只读范围独立byte/hash核验，不整文件格式化；非owned tracked SHA除授权两ledger外精确不变，不复制整仓。保原 case delta/old-route最小源/四实际oracle/诊断/正式freeze同源/完整物理manifest与Task Result R1 prefix。

先preclose核真实artifacts，再由执行者追加Result后strict validate与原八坏副本全部拒绝；不能填不存在Result骗strict，不改validator parser/放宽坏例。本文件当前只有契约与详细计划；实施者后续按executing-tasks追加Result，不改committed计划历史。

最终停止共享写，交fresh required独立结果快审，仅在freeze R1 prefix后追加review；主控消费精确SHA/bytes/物理集合后本地checkpoint，执行者不自行stage/commit。报告唯一whole/旧fine零、六route/Main/最低needs、原快照/三或一帧与无ACK、金币/票券/容量/正常拒绝/具名异常、四旧新oracle真实阶段/原cause/Ordinalretry/current不重复、NotiBus/直接ACK/E+X默认保护、mutation恢复、数量实测与1800延续范围、删除manifest/七ID/非owned边界。remaining122message/175fine/72source/5保护/8过渡、T-012/Astra/Spec根封存与用户分支收尾仍后继，不宣称B完成。
