# TASK-29 — Cera 购买整协议 CMD 与精确买家失败保护

## Task Unit Contract

- Revision: 2（本契约与详细计划共同修订）。
- Outcome and references: 将 CeraShopPurchase 的旧 handler 编排和一次 Transactional fine 完整迁入唯一协议 CMD，薄 handler 只入队，旧 fine 及注册／调用删净。复用当前 WriteResultAsync、KnownStorage commit-only 结果及单精确买家的 NonRequest 足迹；保正常业务与健康确认回滚原响应，落实已批准 VC-B4 的默认失败、精确 Fence／连接阻断／DiscardExact／关闭和既有出站次序。消费 Plan P5 T-004、Spec B4 RQ-001/003～009/013～015、AC-001～005/008/009及 VC-B2/B4；不新增 core/API、Main、效果重放、raw 出口或恢复开关。
- Owned boundary: §2 精确八个产品／测试路径和两份 actual canonical ledger，仅闭合 Cera logical65600／原 fine 一行；本 Task、`verification/evidence/task-29-inspection/` 和 `verification/logs/task-29-*` 新证据。Spec／Plan／routing／契约、Task28及所有旧证据、共享 core/Support、消息／decoder／encoder和其它测试只读。
- Required checks: 原 focused／声明参数／完整NUL before；原Task28真实decoder七例对照及 VC-B4 有价值行为 RED；新 same-provider decoder→whole→原domain/SQLite/outbox、真实sender＋受控transport 的完整资产／native阶段／cause／精确actor及实际发送关闭次序；原强用例及 Host 真竞争；一次有价值可编译消费者 mutation RED→恢复 GREEN；最终同freeze Release solution无增量构建0警告0错误、Application/Protocol/Networking/Host四 fresh full及逐case库存、原DFO74/7与来源bundle、Golden15/Char10、完整capture/pure family／ledger strict+原八selftest；nonowned SHA、行尾、所有新增authored whitespace和manifest/NUL路径闭合。
- Dependencies: TASK-28 的有界旧Cera取证已独立approved并committed done于 `f42efb55bedad71dc1196100e39255bc483ed72c`；其完整Task35298B、SHA `9c8e5a720b8a887e114331b3ddcd4232ac3c67a43967dbf7aca7c5893e32d24a`。同时消费主控已完整核验的 B4/P5 纯治理检查点 `c74650a7869a6799448464a9a7c2883e513091ff`，不是未经批准的后继设计。
- Result review: required — 错误恢复会在已扣费／不确定时发送普通拒绝，漏回滚会留下半购物车，错误actor会围栏或清除替代角色；后继继续删旧模型会放大缺陷。原强测试、真实故障与独立局部快审共同守该风险，Result后须fresh reviewer，不能producer自批。
- Task-specific authorization: 产品／测试编辑仅§2；若实际需要改 core/API、消息字节、额外参与者、恢复来源或已批准可见边界，先返主控，不扩大白名单。没有 push／PR／merge／远端、版本／tag、部署、客户端运行或工作区清理授权；本地检查点由主控在 required review 后执行。Astra通过且用户明确分支收尾的外部门不变。

## Detailed Plan

### 1. 起点、已批准边界和已接受输入

唯一目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`。开始时核HEAD为上述 amendment-only检查点、index空，除本新Task外没有其它变化。本文B指 `.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E指 `B/verification/evidence/task-29-inspection`。不改主仓库／其它工作区。

| 当前治理输入 | bytes / SHA-256 |
|---|---|
| Spec B4 ready R2 | 42632 / `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60` |
| Plan P5 | 71863 / `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0` |
| B4 review | 7624 / `19d7a2d0a5075f89858ab1c32b70d575fbabe350a4e62f45d8f7528eeb659848`，approved当前R2；immutable CH-1在R1成立且R2已解决 |
| P5 review | 8426 / `c355eabec0d2607b6a39ef0ee84c829f281bcd6be8503decc3ee02d5e31a7244`，approved；匹配fresh挑战0/0 |
| 主控双gate消费 | 5918 / `6fe22b16850e523f9bfcc912973d10bcc055acb08b8aa9cf92dfdbdb5ac7874a` |
| 当前 routing | 5507 / `eafec301c273372456d4ff522804e73dda61095171db565591c59bc249d2d03d`，gate overrides已指B4/P5 |

先重读P5 §4 Cera三段、P5直接验证、T-004/NextBoundary，和B4 Cera专节／VC-B4／AC-003～005；root/childContract、baseline、A seal和门强度不变。主控readiness后fresh producer实施，无自动Task-planreview。

Task28的 `report.md`、`final-01/` 七例、source-freeze04及原日志是已提交before：A晚期action原生19/1811、B真实SQLiteCommit19/787均完整确认回滚、原ACK1/no-fence/no-close；C1非fallible违约资产已提交却Uncertain，C2 SQL＋Rollback第二cause虽实际回滚却Uncertain，C3 fine真实scope清理时Committed，三者旧均被转失败ACK／正常返回／无围栏关闭。source-freeze04 SHA `623b5e8fcd727aa34668aa7d81b6873aa380e174e2711302920571f855d3d7d5`，manifest SHA `dfb1dfd50d2ad029ae0402e3123b901bdd8aa4d7e51c0d3accbc52e01c9c6314`。不回写这些已接受制品，也不假称本Task重新生成其before。

Task27八full9014＝8994pass＋原exact2 Dimensionfail＋18skip仍是当前库存：App5237／Protocol1778／Networking199／Host203／HttpApi57／Infrastructure976（974+2skip）／Persistence484／Pvf80。Task28和B4/P5没有新增xUnit或产品，不冒称新full。性能单条≤2秒和最终八freshfull仍由T-012验收，本Task耗时只作观察。

### 2. 精确物理白名单与只读边界

路径相对worktree。八个源码路径中六个原存在、两个新增；一个原fine文件删除。不要用宽目录名解释为额外权限。

| 产品路径（4） | 变化 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseHandler.cs` | 改为现有协议CMD薄handler模式；不留身份／业务／catch／投影编排 |
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/CeraShopPurchaseCommand.cs` | 删除原fine与handler；不保兼容／Obsolete／转发类型 |
| `Server/src/Dfo.Server.Protocol/Game/CeraShopPurchase/GameCeraShopPurchaseCommand.cs`（新） | 唯一whole及handler，非null结果 `CeraShopPurchaseResult`，容纳原编排与P5两路拒绝映射 |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 原fine注册替一个Protocol注册及精确消息绑定；Cera无RegistrationExits；message route改Protocol族 |

| 测试路径（4） | 变化 |
|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Inventory/CeraShopPurchaseTests.cs` | 仅fixture的旧fine/直接handler接法与所需using／窄Cera本地wire helper；原方法／参数／业务断言保持 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/NameTagPurchaseTests.cs` | 仅nearby/private-ACK一例把setup job与生产Handle调用分开，保原setup及全部断言；防新dispatcher自等 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/CeraShopPurchaseProtocolCommandFlowTests.cs`（新） | 本协议永久回归：真实decoder／core／domain、两路回滚、VC-B4、精确参与者、正常对照及无Main／无幂等语义；Cera专属窄观察器，不建共享框架 |
| `Server/tests/Dfo.Server.Host.Tests/GmCeraHttpTests.cs` | 仅真grant/shop竞争用例把三次旧fine调用换whole，保逐receipt及97810／三operation等全部断言和真实HTTP并发 |

两份canonical为 `B/verification/migration-ledger.json` 和 `B/verification/migration-ledger.md`；仅实际Cera消息65600及对应旧fine行的目标／结果／证据更新，其它行逐项不变。Task与E／logs自有新路径另记。原Messages.cs、CeraShopPurchaseMessageDecoder.cs、CeraShopPurchaseResponseEncoder.cs、CeraShopPurchaseCompleted.cs和所有其它Protocol/Networking/Abstractions/Application生产源、共享TestGameplay/TestEffectFootprints/RecordedOutbound等Support全程只读。

已实际检索旧fine符号，当前生产只在旧文件、旧handler和ProtocolModule，测试只在Cera fixture／Host竞争。`ExplicitEffectExecutionTests.Ordinary_Cera_and_daily_coin_requests_use_the_real_dispatchers_non_effect_source`不含fine引用，保持只读；它原来手造request的证明范围不能冒为decoder证据。CeraFixture另被二十余类复用，保其原source只读并以focused／Appfull覆盖传导。

### 3. 产品前封存、原强用例和有价值RED

用 `git ls-files -z`封存实际完整tracked集合，每项current bytes＝HEAD；既有owned（八源码中的六原路径＋两canonical）与nonowned互斥且并集完整，两个new source此时明确不存在。保存tracked NUL、源／测试声明／Theory参数／行尾／BOM、HEAD／branch／index／UTC和最小原源快照；不能以C-quoted文本漏中文路径。所有nonowned最终仍逐SHA相同。消息／decoder／encoder／通知组包文件虽只读也保存hash，后验原字节不变。

先用原Release focused跑CeraShopPurchaseTests、CeraShopRestrictionTests、CeraShopExpirationTests、NameTagPurchaseTests、DevilContractPurchaseTests、CeraCargoCapacityRefreshTests、WarehousePurchaseTests、ExplicitEffectExecutionTests；Protocol的CeraShopCodecTests、Host竞争case另保原真实baseline。保存完整display/status/multiplicity及原断言／驱动。原PVF路径与Skip边界沿现环境，不用Skip掩盖原pass→skip或错误filter零例。共享fixture其它类由最终Appfull证明，必要窄focused可按实际新增，不扩大编辑权限。

沿原 `migration-ledger.py capture --repo <worktree> --output <E/capture-before-01>`和已接受family-export的真实纯注册方法捕获source/runtime/pure-family/exit owner；工具只读，driver只在E。参考起点173 routes／201 commands，35Protocol＋1Internal＋165Legacy，137Tx／51Direct／9RO／4Workflow，46消息ProtocolCommand＋127LegacyQueued、14telemetry；不以人手计数代替capture。

VC-B4是有意行为新增，先从Task28必要探针逐byte复制到E自己的before子目录，在未迁产品上保留真decoder/旧fine装配，运行C1或C3，并让独立新期望检查“发生最终失败／精确actor保护”而实际得到旧ACK/no-fence作为行为RED。保probe本身原exit0和新期望检查的原exit1，明确失败谓词与旧实际证据吻合；不是编译错误、注错未达或改旧expected求绿。复用Task28完整七例before，不强制重复所有旧case；若新RED只能因fixture失败而非旧行为不满足，纠正接法并保诊断，不能算行为RED。

所有native使用require_escalated、原cwd／UTC／完整命令／exit／资源与独立新prefix，保stdout/stderr；不TERM活跃native或父runner，不覆盖旧日志或证据。

### 4. 唯一whole和原交易分段

whole为 `sealed record GameCeraShopPurchaseCommand(NetworkDispatchContext Dispatch, CeraShopPurchaseRequest Input) : IProtocolCommand<CeraShopPurchaseResult>`，Message/Frame沿现有Game协议命令模式返回Input与显式metadata；非效果消息不增加IEffectSettlementRequest。非null结果使Host可保实际receipt断言，薄handler忽略结果值但完整await总线。

构造器只需原领域／查询服务、TimeProvider、ICommandWorkUnits、ICommandProjections、IGameCommandOutbox和具名logger；不注入sender、IEventService、bus、locator、内容／规则／表／持久化或旧ambient。原cart日志、session lookup／online判定、receipt/refusal日志全部按原顺序迁入。缺session或online的原NoSession拒绝不进入写单元；其余须本次ProtocolNonEffect形状，创建含原session、`Dispatch.Connection`同对象和Input.Metadata的唯一SelectedSessionRecipient。

一次 `WriteResultAsync`的声明为Ordinary、Footprint NonRequest、participants仅该actor，名称具名CeraPurchase即可。不把原一次交易拆为多事务，不新开ReadOnly，不建立Main／operation ledger，也不为其它角色附加参与者。actor/current/footprint取得与runner守卫在业务try之外；action先取unit.Footprint，再仅在原clock／`ICeraShopDomainService.PurchaseAsync`窄调用try内保存CeraShopPurchaseRejected及普通非取消／nonuncertain异常，调用 `unit.RequireRollback(cause)`后正常返回一个私有非nullActionOutcome。RequireRollback自身的失败不在这个catch里再次被吞；正常domain返回拒绝不RequireRollback。

单元完全结束后才解构Returned成功／正常拒绝／已RequireRollback拒绝，或现runner签发的CommitRolledBack(cause)。业务mapper沿旧CeraShopPurchaseRejected.Reason/Message，普通故障沿StorageFailure/Message和原CERA Error；不把unit/lease/scope、typed/取消、unknown或sticky/guard故障映正常拒绝。此处复用KnownStorage/cause/ConfirmedRollback/current/healthycleanup门，不改runner／结果类型或伪造完成事实。

结果有receipt时仍先原成功日志，再TheseCharacters的CeraShopPurchaseCompleted；NameTagChanged才按原visibility.Resolve得到TheseSessions、投CharacterUserStateRefresh。沿原ICommandProjections，不改原通知handler组包和扩容→产品ACK→wallet→可选gold→inventory→pet→restriction→premium/state次序；无receipt沿原拒绝日志和错误码NoRoom4／InsufficientFunds11／其它1，仅IOutbox登记CMD。原ErrorCode可移入whole私有helper，不留thin handler业务。

注册把 `CeraShopPurchaseCommand`那一行替为GameCera whole及确切CeraShopPurchaseRequest/ClientMessageType.GameCeraShopPurchase绑定，route由RegisterMessageHandler改现RegisterProtocolMessageHandler。Cera没有任何raw exit，只有此一whole；不改邻近协议注册。实际capture再确认source/runtime/pure family只有本行族别/类型变化，旧fine符号在Server生产和测试清零（历史E／Task不追零）。

### 5. 原测试接缝，保业务意图与真正网络入口

`CeraShopFixture.Buy/Command(session, entries)`原来是直接fine并返回领域结果，很多强用例刻意Assert.Throws SqliteException／CeraShopPurchaseRejected，或传旧session验证domain拒绝；不能改为吞错whole后把这些断言改成弱Refused。把这个helper明确改成**领域测试接缝**：由原provider的TestGameplay.RunAsync进入真实job，开其原DomainTransactionScope写lease，创建现有 `Support.TestEffectFootprints.Lease(session)`，调用真实ICeraShopDomainService.PurchaseAsync及原Clock，正常时真实Commit；异常以AbortAsync(cause)保原因及真实回滚后重抛，finally原leaseDispose。事务与footprint生命周期按原顺序结束，不嵌套打开已有事务、不用LegacyFootprint／新测试命令／fake domain或SQL代购买。该接缝只证明领域＋真实事务，结果中准确列明它不证明new whole／core footprint；专门的wire/flow证据承担后者。旧selection／persistowner和全部原异常断言因此保留。

`CeraShopFixture.Handle(input)`则改为**生产入口测试接缝**：按现decoder字段在test-only Cera本地helper完整写body（first、second、count、coupon及每entry所有avatar/selections/coupon tail），通过真实注册IGameMessageDecoder→同provider INetworkMessageDispatcher，无外层TestGameplay／NetworkCommandAuthorization.Enter。metadata明确selector1/type0x0040／本次ordinal及RawBody；不把不同输入统一成水商品或丢选项。Input.InvalidReason是decoder诊断而非wire字段，原invalid-input那例应送确实malformed的body、保原拒绝／无副作用／后续合法成功断言并记录driver差异，不能发明InvalidReason线上字段。Handle不直接new request或调薄handler冒decoder。

NameTagPurchaseTests的nearby/private-ACK例当前在TestGameplay包内调用Handle。仅将fixture构造／peer setup与生产调用分离：注册／鉴权／Select／EnterWorld／Town setup仍在原provider的单独TestGameplay job，Handle在job外await；保原两CurrentUserState、单Extended、单购买ACK／wallet及setup语义。其它case和共享helpers不改；若发现其它隐蔽外包先准确报主控，不能自行扩白名单。

Host真竞争case的三次购买改为 `commands.SendAsync(new GameCeraShopPurchaseCommand(new(user.Connection), new CeraShopPurchaseRequest(actual metadata, 原Input)))`，在外部与三个真实HTTP grants同时入队，不包TestGameplay。它仍拿真实CeraShopPurchaseResult／receipt，保原100000起点、产品100039、原重复账号HTTP payload／三operation和最终97810。该case是执行器／HTTP竞争，不冒真正decoder；新flow和现Networking integration负责网络输入证明。

新增Cera专属Flow测试复用CeraFixture的真实modules/SQLite/注册sender和现测试接缝。永久回归至少覆盖§6的正常与两路回滚／VC-B4和NonEffect；观察器只委托真实domain/units，不fake结果或自签CommitRolledBack。不得为访问Networking internal新增friend或项目引用：实际物理sender后端的完整对照可放E的既有friend名称独立probe，和xUnit对core／资产／登记目标的证明范围分层，不能拿RecordedOutbound.Discard当真实CloseAsync。

### 6. 真正新路径对照与精确保护

复制Task28的必要Program/Observation/Delivery到E新项目适配新whole wrapper；原项目引用App.Tests、既有friend名Dfo.Server.Networking.Tests和窄反射fixture可复用，但实际Location/MVID/SHA与正式Networking.Tests DLL区分。所有原production assembly加载copy须匹配当前build输出。仅probe内替IOutboundSender为原OutboundSender＋记录转发器＋受控内存transport，同provider／真实adapter／outbox不换；setup可用TestGameplay，真实dispatcher entry不包。先成功wire证明sender确收编码帧且没有预先关闭，再跑矩阵。原观察全表查询可能提前enlist同SQLite参与者、controlled participant与内存transport局限沿Task28披露。

| 场景 | 直接验收 |
|---|---|
| success、余额不足正常拒绝、NoSession／online缺失、malformed cart | 原success帧和受众／顺序；正常refusal ACK11/1、资产不变但原空Commit如实记录；前置无session/online不写。健康各例不Fence／Blocked／close；全包截断不购买有效prefix，原codec和真实TCP无选角integration只读原断言通过 |
| A 晚期action SQL | 水＋宠物、gold成本和原trigger19/1811；throw前wallet/gold/首grant/events已暂存且无receipt。新action正常Returned拒绝但真实CompletionConfirmedRollback；全生产表所有列NULL/BLOB exact before=after，原ACK1实际Delivered、无Fence／close；去trigger后只收费一次新合法购买 |
| B actual native Commit | grant INSERT＋DEFERRABLE FK，action先receipt、同真实连接FK1/child>0；SqliteTransaction.Commit19/787，KnownStorage/FallibleCommit的cause同对象且CommitRolledBack仅清理后回值。完整allcolumn rollback、原ACK1/noFence/noClose，去trigger合法购买；controlled Prepare或action ABORT不冒该证据 |
| C1 nonfallible | 仅受控nonfallible participant Commit抛固定cause，真实SQLite资产已提交、CompletionUncertain；最终typed保原Inner，不发StorageFailureACK／不正常结束，单精确买家Fence／Blocked／DiscardExact、actual sourceClose和继承链成立 |
| C2 rollback双因 | 真实SQL1811经RequireRollback(cause)后真实Abort，controlledRollback第二cause；typed Uncertain及Aggregate保原SQL／第二cause引用，即使allcolumn实际回滚也不能返回普通拒绝。精确买家默认保护与真实sourceClose成立 |
| C3 actual whole scope | 在真正newwhole DI scope解析probe IAsyncDisposable，body正常返回后才Dispose抛固定cause；购买Committed不撤销，cause进入默认PostCommit包装，精确actor保护与sourceClose成立。记录scope故障前已登记的原success消息和最终实际bytes→close顺序，不追加StorageFailureACK、不要求零帧 |
| 精确旧actor／其它身份 | 在C3可达的unit提交后／scope故障前，用受控真实状态替换或建立另一session/runtime和另一物理context；记录引用／generation及初始/最终状态，证明只对原actor精确Fence/DiscardExact/原physical close，不误清、误围栏或误关替代身份/其它连接。不能仅改ConnectionId字符串或assert方法名。若同一物理来源有逻辑身份变化，仍按已批准精确来源关闭，不能把该来源正常关闭误写为允许关闭另一physical context |
| NonEffect／默认隔离 | 真实context为ProtocolNonEffect、一次Ordinary NonRequest、一actor及显式metadata、无Main或ledger；同一合法cart再次提交可再次购买，不冒同序号幂等。取消／guard／已有sticky等不得进入可恢复结果，复用现core正反例并按实际新consumer触点补窄case；不改默认Write/ReadOnly或其它消费者 |

actor保护至少记录 Standing.Fenced、SelectedSessionSafety.IsBlocked、DiscardExact的真实目标／runtime前后、close条目与实际transport.CloseAsync、原SourceChain/CloseCauses；不能以默认discard后端未记录close推断无关闭。物理sender是受控内存transport上的原实现，非TouchSocket／TCP或A21实机。每例新fixture/独占数据库；C1/C3不能自动重发已提交购物车来冒安全重试。

A/B同run rollback比较完全原样，不归一任何time/GUID或丢NULL/BLOB。对Task28与本Task跨run业务不变对照，只允许已从实际schema及各自run开始/结束证实的具名CURRENT_TIMESTAMP列，按row identity／原时间范围／顺序处理；另仅允许具名启动迁移行`marriage_housing_migrations(version=1).applied_at`的显式UTC毫秒来源差异：其拥有者`MarriageHousingMigration.Apply`用`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`写入，必须先验证schema／唯一version=1／Int64类型、值落在该次真实native run UTC界内，且在同case所有before／staged／after／retry阶段原值不变。保留原始数值和来源／时间校验表后才允许跨run等价，不删除该列、不推广到其它显式时间或任意数值；事件event_id/batch_id/origin_instance_id仅用各kind/per-case、贯穿所有阶段（包括staged/rollback/retry）的非零双射，保delivery FK／batch／origin关系，不能广泛删字段。VC-B2的fine日志/command身份和VC-B4的C组结果／actor保护差异单列，不冒原JSON逐byte相同。原cause“同对象”指各自run内真实传播引用，不声称跨进程对象同一。

### 7. 有价值消费者变异与恢复

优先用真实B nativeCommit oracle保护新增消费者的结果映射：临时把whole对 `CommitRolledBack` 的原拒绝映射改为重新抛其原cause（局部且可编译），不动core／fixture／expected。Release编译通过后B仍是真native787／ConfirmedRollback和完整资产回滚，但应因原拒绝ACK缺失／来源关闭等实际结果而RED，证明不是只测runner或错误类型。完整保mutationdiff／源SHA／native及oracle输出，随即恢复exact product bytes、重建后B和正常/A相关对照GREEN，最终freeze前核恢复。

若选择动作侧删除RequireRollback(cause)代替上述变异，须先判原领域自身是否已自动RollbackOnly，只有真实资产／提交差异会红才有价值；不强制跑两个等价mutation，不把fixture或compile失败当RED，也不为造红弱化下层保护。此工作不改变正式断言或添加有意DFO违规。

### 8. 最终同源正式检查和库存

产品／测试／ledger定稿后冻结所有现存新源（删除行保HEAD前身份）和driver。执行完整：

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

保持原xUnit类并行、原5m deadline和Skip；每轮保存raw detailed输出及真实exit（App只允许原两exact失败的exit1，不mask）。两条原失败是 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 和 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，原形状 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`。Socket48／ConnectionRefused不是baseline；新增失败保原轮次并定位，有依据时同freeze重跑受影响完整项目，不能只挑成功行、调port/timeouts/parallel/Skip求绿。源改则新freeze和相应checks，不冒旧输出来自新源。

其它四项目（HttpApi57、Infrastructure976、Persistence484、Pvf80）只有source/其原dependent生产源都未变的已接受Task27完整full可引用，准确列源／轮次；不称本Task八fresh。原9014按八个实际选取full逐完整display/status/multiplicity Counter对照，N/D/rename/status/断言及驱动变化逐case解释，原strong cases不减少，新增Flow数量以实际结果为准；不预造N。所有原参数／Theory与保留断言有声明/有界diff证明，fixture改动与只读传播范围分别列。

正式DFO用本Task四fresh full的独立目录与Task27其余三带control的原full JSONL（HttpApi/Infrastructure/Persistence），建立新的final bundle逐bytes复制每assembly一个完整来源，保存原path/round/bytes/SHA provenance；Pvf无control。沿原violations.py CLI／expected九类反例67条＋七actual controls＝74records，不改expected／assemblies求过，不双算control；所有新intentional若实际需要先报主控，不能暗吞。独立E probe零DFO只能说明probe观察，不冒七control。若任何full重试，保所有raw，再新bundle选择完整项目来源而非拼行。

Golden15 current＝HEAD＝原f01dc226逐bytes、Characterization10逐display pass/0skip；原CeraCodec所有向量、Network无选角TCP整帧／连接继续可用断言保持。套件时长/资源不代表单CMD≤2秒。

### 9. 迁移账、唯一变化和完整收口

同freeze跑原capture＋pure family，并与before完整rows/decls/metadata/bindings/exits对照：只允许Cera logical65600由LegacyQueued→ProtocolCommand，旧fine类型换唯一GameCera whole，Transactional注册变Protocol Direct；预期总routes173/commands201不变，families36Protocol＋1Internal＋164Legacy，136Tx／52Direct／9RO／4Workflow，messages47ProtocolCommand＋126LegacyQueued、14telemetry保持。以实际工具结果验此预期，若不同先定位，不改expected蒙混。Cera无raw许可，三种原exit及全部owner/phase逐项同前，不能只比总数。

canonical仅消息65600及原fine对应行（原完整ID从ledger实读，不凭类型名猜）闭合，指本Task真实whole／一次普通写／NonRequest／原拒绝与VC-B4证据；消息已无Main/重放，不能写成效果ledger。其它message/command/source/protection/transition行逐bytes或规范对象保持；current strict和原八selftest完整执行，按原工具拒绝原因核对。不改工具、baseline／Spec/Plan、source/protection/transition结果或伪造F-122行，不新造无价值物理坏副本。

最终tracked NUL仍完整，所有nonowned current SHA＝before＝HEAD，含历史Task28／所有它的source/observations和未触协议／tests；owned新增／删除／修改逐项列，删除fine保原HEAD bytes/SHA且现路径确实不存在。当前new source都纳入freeze／检查，不能靠git diff漏未跟踪文件。保每个原文件LF／CRLF／BOM，只定点编辑；raw snapshots/logs不为whitespace重写，新authored逐文件显式查trailing/space-before-tab/blank-EOF，Git检查用cr-at-eol正确处理CRLF。

E保存真实before引用与新的RED／GREEN／mutation／完整native／比较／capture/ledger／source身份、strong coverage和局限；manifest每个whole-file bytes/SHA，自己排hash、Task精确pre-Result prefix避循环，Git NUL changed∪untracked与manifest精确一致。无原证据覆盖，无ignored bin/obj强加Git或清理其它工作区。先全部native自然结束再append Result，实际交付成立才 `Outcome: done`，列完整Task／prefix／freeze／manifest身份和剩余局限；producer停止writer/native交主控fresh required快审，不自批、不stage/commit或开启Task30。此单元不是Spec002整体完成，B4/P5之外WHAT／core缺口返主控。

## Task Contract Amendment R2 — 具名初始化UTC与真实关闭链观察

主控于2026-10-25核实两项已出现的证据接法事实后作本地窄澄清。producer已明确全部native自然结束、暂停repo writer；本修订只改本Task，随后重新readiness并解锁producer。原r1计划28409B SHA `184fea7186811a3278228f34e67ba218d95482b8e87fab1615558c8b59d51c68`保存在E的`task-plan-prefix.md`，原诊断与raw不回写；本r2完整身份由主控交接外报，后继另存r2 prefix，不将旧r1快照冒当前prefix。

1. §6的跨run时间许可原只列CURRENT_TIMESTAMP，无法涵盖已实见的初始化行。拥有源`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:321`声明`marriage_housing_migrations(version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL)`；`MarriageHousingMigration.cs:11–12,21–24`仅在version1不存在时以`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`写入。它是本fixture建库迁移时间，不是购买业务状态。主控实际读取旧Task28 final01和本Task after01全部case／primary JSON：唯一version1、Int64、各stage和retry原值完全不变，旧值全落旧run整数毫秒界1792880269098～1792880270623，新值全落本after01 run界1792883666227～1792883667413。后继正式选取round必须从其真实metadata重新取界，不能借这些旧界求过。只对这一确切行／列按上述先验来源／类型／范围／同case稳定性验证后的跨run差异作等价处理，保存原值及校验；其它显式UTC或数值无此许可，新增差异仍准确返主控。同run回滚仍全部110生产表（nativeFK再加probe表）全部列／NULL／BLOB原样比较，不作任何归一。

2. 当前ShopFixture的RecordedOutbound替换了生产GameplayOutboxes factory：`Server/tests/Support/RecordedOutbound.cs:83–84`的last descriptor构造不带GameplayCloseCauses，生产`Server/src/Dfo.Server.Application/Execution/GameplayQueue.cs:110–117`的first descriptor则解析同provider的真实causes并传入。主控已读两处确认，当前after01的RecordedCloseChains空不能作为已验证继承链通过。允许仅E内after probe的configure恢复选择这个原生产first descriptor／factory，保持同provider的实际adapter／outbox／domain／SQLite和原OutboundSender＋受控transport；记录所选descriptor及同provider causes绑定、source/assembly freeze，再重建重跑矩阵取得实际close cause/chain。不得改共享Support／core、手调GameplayCloseCauses.Record或伪造链，不替换结果／提前flush。旧Task28和before证据保原样，其无close结论有条目0＋actualClose0等独立证据，不因本观察接法补强改写旧事实。

产品8路径＋两canonical、B4/P5的WHAT/HOW、事务／恢复来源／参与者、正式检查和所有其它边界不变。§6仅修正一个已证初始化UTC的严格跨run判据；真实production factory恢复仅属E观察器接法，不新增生产机制。原App新focused中generation快照和typed Completion期望的普通fixture纠正保每轮诊断，不混作新VC或放宽原强断言。当前readiness可继续：source/core/旧证据所有权未扩，新的timestamp及close-chain验收明确。Result／独立quick review必须绑定本r2及最终源freeze，主控本段不是结果批准。


## Result

Outcome: done

本结果仅为 TASK-29 r2 的有界实现交付，required fresh result review／主控检查点仍待，不表示 Spec002 整体完成或 producer 自批。执行路径、8 个源码路径＋2 canonical 的权限未扩大；未 stage／commit／push／PR／merge／远端／版本／tag／部署／客户端／清理或开启 TASK-30。

- 唯一 GameCeraShopPurchaseCommand 返回原非空 CeraShopPurchaseResult，薄 handler 只 await 总线；旧 fine／注册／caller 从 Server 清零。一次 Ordinary WriteResultAsync、NonRequest、唯一原买家（物理 context 与显式 metadata）保持原 domain 交易、投影顺序与消息字节，无 Main／操作账本／幂等重放／新 raw。
- 原 focused App110／Protocol7／Host1 均先取得 before；迁后 App125（原110＋新15）和真 HTTP grants／三 whole 竞争均通过。原测试方法／参数和 Cera176、NameTag50、Host93 个 Assert 语句保留；领域 Buy/Command 接缝与真实 decoder Handle 的证明范围分别记入 strong-coverage.json。
- 真实新 decoder→dispatcher→whole→原 domain／SQLite／outbox 的十二模式均通过；A 为原生1811晚期 action，B 为真实 SqliteTransaction.Commit787／KnownStorage／FallibleCommit／原 cause 引用和完整全列 ConfirmedRollback，原 ACK1 实际 Delivered、健康不围栏关闭，并可去 trigger 后新购买。
- VC-B4 行为 RED 使用原 C3 Committed／原失败 ACK／noFence/noClose 与新精确保护期望，probe exit0、oracle exit1。新 C1／C2／C3 保 typed 原因及双因、精确 Fence／物理 Block／DiscardExact／实际 sourceClose；C3 真实 whole scope Dispose 在 body 返回后抛，已登记成功帧由原发送器先发后关。真实替代 session／runtime／另一物理 context 保留；取消／typed／sticky 新窄例不映普通拒绝。
- after02 恢复选择原 production GameplayOutboxes factory，实证 causes 与同 provider 解析值同对象；实际 RecordedCloseChains 等于 SourceChain。after01 的空链和旧 generation／typed 测试期望诊断均保留，不冒为通过证据。没有手工 Record 链、改 Support 或 core。
- 七旧新 case 的110生产表（native FK 场景另含 probe 表）所有 schema／列／NULL／BLOB 与每阶段／retry 完整比较；同 run 回滚全部原样相等。跨 run 仅具名 SQL CURRENT_TIMESTAMP、经 r2 允许并按各自真实 run 界／version1／Int64／全阶段稳定验证的 housing.applied_at，及每 case／每 kind 非零 GUID 双射；原值、row identity、时间顺序和 FK／batch／origin 关系保留。正常／普通拒绝／A／B 的实际完整帧与 Task28 逐 bytes 同。
- 可编译 EDI 消费者 mutation 保 native787／原 Commit 栈／同 cause／全列回滚，RED 精确命中原拒绝 ACK 消失与实际 sourceClose1；恢复原 bytes／SHA 后重建，同 A／B／success／refusal oracle GREEN。未改 core／fixture／expected 求红。
- 正式 Release solution --no-incremental 0警告0错误；App／Protocol／Networking／Host 四 fresh full 依次自然结束。原9014逐 display/status/multiplicity 全保，N15/D0/rename0/status0→9029＝9009pass＋原 exact Dimension2fail＋18skip；其它四项目准确引用 source-unaffected Task27完整 full。没有 Socket48／ConnectionRefused 新失败。Golden15＝HEAD＝f01dc226，Char10全部 pass／0skip。
- DFO 新 bundle 逐 bytes 复制本四 fresh full 和 Task27 HttpApi／Infrastructure／Persistence 完整 JSONL，SHA／round／path 来源齐全；原 CLI／九类 expected 不变，74＝原67反例＋7实际 control 通过，无新 intentional。Pvf 无 control；probe 零记录不冒 control 证明。
- 实际 capture＋pure family 仍173 routes／201 commands，仅 logical65600／旧 fine→whole 两登记变化；36 Protocol＋1 Internal＋164 Legacy，136 Tx／52 Direct／9 RO／4 Workflow，47 Protocol消息＋126 Legacy消息，14telemetry，原三许可种类及所有 owners／phase 完整同前，Cera 无许可。canonical 仅两实际 ID 闭合；preclose02 通过，最终 strict 与原八 selftest 输出由同目录及 logs/task-29-ledger-* 封存。
- 完整 tracked NUL 15284＝原 existing owned8＋nonowned15276；nonowned current SHA＝before＝HEAD。新2源码／删除 fine 单列，消息／decoder／encoder／通知原 bytes 不变，原文件 LF／BOM 形态保留；最终 authored whitespace、完整变更 NUL／manifest 闭合见相应文件。

正式 native 与 full 实际运行于 source-freeze-01；全量结束后，仅两 canonical 的目录证据 locator 修正为实际文件，并同步 MD。source-freeze-02 的另外30个产品／测试／driver／删除事实条目与01完全相同，运行时源码逐 byte 等价；没有声称 locator 改后重跑 full。原 preclose01 拒绝保留，派生库存 checker 的 Windows 路径解析纠正独立另存，最终 administrative driver 身份封存于 evidence-code-freeze.json。

身份：r1 原 prefix 28409B／184fea7186811a3278228f34e67ba218d95482b8e87fab1615558c8b59d51c68；当前精确 pre-Result r2 prefix 32294B／d6c954bc2ef7bd0109f6652114409cbe7fd0a29c744d3032fcdb1fb135575172。

source-freeze-01.json: 10881B / `ab1dbba3f9dfcffe972dcd81f9d155a75c542a208ee3ad0c752852feb71d0131`

source-freeze-02.json: 10986B / `f71d07a60280eb1edb74f8da35b7f7c2b77cf033f0769003a0f769addfa3cfa1`

最终 whole-file manifest 为 E/manifest-final.json，self 排 hash、Task 按上述精确 pre-Result prefix 绑定以避循环；完整 Task／manifest bytes/SHA 在停止 writer 后外报主控，native-stop.json 记录自然退出。详细证据入口为 E/report.md、verification.json、formal-summary-01.json、registration-equivalence.json、cross-run-equivalence.json、mutation/result.json。

局限：受控 transport 上的原 OutboundSender 证明实际 bytes／关闭与真实 cause 登记，未运行 TouchSocket／TCP／A21 客户端；生命周期 child 的通用机制由原 full lifecycle tests 覆盖，E 未模拟 TCP disconnected 回调。受控 nonfallible／rollback／scope 故障不是生产事故；观察全表读取可能提前 enlist 既有同 SQLite participant，没有新增 fallible participant。NonEffect retry 是新购买。四 fresh＋四复用不是八 fresh；单 CMD≤2秒、Astra／用户分支收尾及 Spec002最终门属于后继。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

fresh independent reviewer `b_task29_review`，应用 `reviewing-work` 的 Task Unit、Code、Deliverable 判据；与 producer 分离，仅审核 TASK-29 contract revision 2（含 R2 amendment）／Result R1 的本地 Cera 交付。唯一工作目录、branch 和 HEAD 均与本 Task 相符，HEAD 为 `c74650a7869a6799448464a9a7c2883e513091ff`。审核前完整 Task 为38675B／SHA `c2fc7d1167e3098f28425519287ce93c8a98d4b396bafa469d4b2edc3d8b48c6`，r2 pre-Result prefix 为32294B／`d6c954bc2ef7bd0109f6652114409cbe7fd0a29c744d3032fcdb1fb135575172`。

已读当前契约／amendment／Result、实际 owned diff 和两份新源码、原 handler／fine、原测试接缝与新15例 Flow、report／verification、Task28 before 与新探针／原始观察、相关 core 和仓库约束。直接核 B4 Cera 专节／VC-B4／相关 AC、P5 Cera 方法，以及匹配 approved reviews 和主控消费；Spec42632B／`9163ce4c…`、Plan71863B／`88920242…`及两门完整 SHA 与 Task 声明相符。没有把本轮扩大为整 Spec002 审核，也没有以 producer 的 pass 自述替代实际检查。

本范围未发现未解决 blocking finding，决定性检查如下：

- 实现只保唯一 whole 和一次 Ordinary WriteResultAsync；NonRequest 的 actor 明确携原 session、同一物理 context 与 metadata。足迹取得及 runner 检查在业务 try 外，原 clock／domain 的普通异常经 RequireRollback 保 cause，取消／typed／sticky 不转普通拒绝；commit 结果只消费原 runner 的 KnownStorage、同 cause、ConfirmedRollback 和健康清理条件。无 Main、重放账本、新 raw、LegacyFootprint 或嵌套 dispatch。原成功受众、两次投影次序、拒绝错误码及日志保留；原消息／codec／通知组包源未变。
- 独立只读重算 after02 十二模式 oracle，并读实际 production-first outbox factory、同 provider causes 绑定、真实 sender 和受控物理 transport。A1811／B787 保真实阶段、原 Commit 栈、同 cause、全表全列回滚、原 Delivered 拒绝 ACK 和健康无围栏关闭；去 trigger 后新购买成立。C1／C2 不把实际提交或实际回滚误称可恢复，typed／双因保留，精确 buyer Fence／物理阻断／DiscardExact／实际 close 与源链成立。C3 为实际 whole DI scope Dispose，body 已返回、Committed；已登记成功帧原序发送后关闭，没有追加 StorageFailure ACK。真实 replacement runtime 与另一物理连接保留。
- 七旧新 case 的全 schema／列／NULL／BLOB／各 stage 与 retry 比较已只读重算；同 run 回滚无归一。跨 run 的四个具名 SQL CURRENT_TIMESTAMP 列、唯一 version1 housing UTCms 和 GUID 双射满足 R2 窄规则；actual after02 自身 UTC 界为1792883917343～1792883919233ms，未借用 after01 界。正常／拒绝／A／B 的原完整实际帧相同；C组有意变化单列，旧 before 无 close 的事实保留。
- 原 Cera／NameTag／Host 的全部测试声明／参数与176／50／93个原 Assert 语句独立比对保留，原 Cera 测试 body 原样。Buy／Command 明确只证明真实 domain＋原事务＋既有 TestEffectFootprints.Lease；Handle／Flow 才走真实注册 decoder 和同 provider dispatcher。NameTag setup 与调用分开，Host 保真实 HTTP grants、三 whole receipts、97810及三 operation。旧 focused110／codec7／Host1及新125／Host1的原 metadata 为成功，永久新增确为15例。
- 真实旧 C3 probe exit0／新保护期望 exit1 是行为 RED。EDI 消费者 mutation 可编译，独立读原 raw 确认仍 native787／原 Commit 栈／KnownStorage FallibleCommit／同 cause／全列 ConfirmedRollback，而原 ACK 消失、实际 close1；原 oracle 的失败命中返回结果。exact whole6121B／SHA `53fb83cc95f13774ebf1e36af809497ea10b4099a16a4582ab50e320f12a1685` 已恢复，四个恢复 raw case 的只读 GREEN oracle 通过。未把早期编译或 fixture 诊断当行为 RED。
- 从八个实际选取 full 的 raw detailed log 独立重建 display/status/multiplicity Counter：9014→9029，仅15个新 Flow pass，D0／改名0／原状态变化0；9009pass＋原 exact Dimension2fail＋18skip，Char10全部 pass。四 fresh App5252／Protocol1778／Networking199／Host203的自然结束、原 exit和正式 build0警告0错误可追溯。另四项准确复用 Task27完整 full，实际 csproj 依赖闭包不含改动 Protocol 源；不是本 Task 八 fresh。Golden15逐 bytes current＝HEAD＝f01dc226。七份 DFO bundle 原完整来源 bytes／SHA相同，独立运行未改原 CLI 得74records／7controls，outside／exe／orphan0。
- source／runtime／pure-family 注册对照只读重算仅 logical65600及旧 fine→whole，173routes／201commands及36P＋1I＋164Legacy、136Tx／52Direct／9RO／4Workflow、47Protocol消息／126Legacy消息／14telemetry成立；原三 raw 种类及所有 owner／phase 完整相同。canonical 与 HEAD 逐对象核仅两实际 ID 改动，strict 及原八负例 selftest 以原函数在内存只读复算，与封存输出完全一致。
- 独立核完整 tracked NUL15284及8 existing owned／15276 nonowned分区；全部 nonowned before＝current＝HEAD，不漏中文路径，index空。manifest88710B／SHA `572cfd79c53d195bec218a7c2822c645bad0fc611a78b8fbf04d2d54e370413b`的290路径与 Git NUL 8trackedchanged＋282new精确相等，whole／Task prefix／删除fine的HEAD身份／manifest self排除按实际 schema 核实。所有 authored whitespace、既有行尾／BOM以及9个实际加载的生产／测试 DLL对 formal 输出相等均复核。

最终 source-freeze-02 为10986B／SHA `f71d07a60280eb1edb74f8da35b7f7c2b77cf033f0769003a0f769addfa3cfa1`，32条现存／删除事实与当前源匹配；evidence-code-freeze 的37个 driver 逐 SHA 匹配（SHA `5828b5860818525e30edfe9b2e104a066e673fcfe3f2ddddde8873a5415fa8d5`）。正式 native 真正使用 freeze01；01→02仅两份 canonical locator 修正，余30条一致。当前报告对此披露正确，没有声称 freeze02 重跑 full，原 preclose01 拒绝及行政 checker 纠正保留。

批准仅通过本 Task required local result gate，供主控消费和本地检查点；不表示 Spec002 完成、最终八 fresh／单条2秒接受线、Astra 或用户分支收尾通过。原 sender＋受控内存 transport 并非 TCP／A21 实机，真实 close cause 登记不冒未运行的 disconnected 回调；受控故障、观察查询可能提前 enlist 同一 SQLite participant 等局限如实保留。

Reviewer 未运行 native、stage／commit／remote、改产品／测试／Result／证据／ledger／其他 metadata；唯一写入为本段追加。原38675B Task前缀保持完整，审核完成后停止写入，交主控推进。

## Controller Consumption R1

主控完整消费当前 Task r2／Result R1、report／verification、owned 实现和原测试驱动差异，以及 fresh Independent Quick Review R1；review approved，无未解决阻塞，producer／reviewer均已停止writer／native。消费前 Task45524B，SHA `cd0c83b09661ae9ad6c2dade58341ce32390eda7128c9c072c7e02e28d20c7ef`；原Result38675B前缀SHA `c2fc7d1167e3098f28425519287ce93c8a98d4b396bafa469d4b2edc3d8b48c6`保持，本段只追加，不改独立判断。

主控独立重建八selected full的display/status/multiplicity Counter：9029＝9009pass＋原exact Dimension2fail＋18skip，仅15新Flow pass、D0／旧状态漂移0；四fresh／四Task27 source-unaffected复用明确。另复算manifest全部290路径的whole／精确r2 prefix／deleted source／self排除和Git NUL精确并集；32条final freeze当前匹配，native freeze01→02仅两canonical证据locator差异，其余30条完整相同；37 administrative/native driver的SHA匹配。实际运行原DFO CLI stdout得74／7、outside／exe／orphan0；Golden15逐bytes current＝HEAD＝f01dc226，Char10原full全部pass。源码、source-freeze-02 `f71d07a60280eb1edb74f8da35b7f7c2b77cf033f0769003a0f769addfa3cfa1`、manifest `572cfd79c53d195bec218a7c2822c645bad0fc611a78b8fbf04d2d54e370413b`及冻结方法与独立目标相同。

原生A／B、四具名恢复、typed／scope及精确actor保护、production factory真实cause链、R2唯一bootstrap UTCms、same-run全列与跨run窄等价、EDI消费者mutation、15276 nonowned＝HEAD及canonical仅两行由实际报告／原source与fresh独立复算交叉确认；不把受控内存transport称TCP/A21，不把四fresh称八fresh。只许可本290路径的本地Task检查点，提交前后核cached精确范围／full authored whitespace／父提交／footer／clean；committed Outcome才计done。

剩余Plan T-005～012及最终八fresh、两秒接受线、Astra和用户收尾门仍在。按T-005临近边界，邮箱领域自Rollback的事实不同于本Cera；下一有用单元先作evidence-only旧decoder／事务／ACK／物理关闭取证，不把VC-B4或P5五consumer自动扩给Mailbox，不修改已提交Task或A封存结论。
