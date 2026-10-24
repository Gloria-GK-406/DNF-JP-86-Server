# TASK-07 — 四个名册协议 CMD 与创建／删除显式写段

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付父 [Plan P1](../plan.md) T-003 的四名册协议闭包，对应 [Spec B1](../spec.md) RQ-001/003～005/007～009/012/014/015、AC-001/002/004/005/007/009：AccountRoster、CheckCharacterName、CreateCharacter、DeleteCharacter 各有一个完整非效果协议 CMD；handler 只交付原请求和精确 dispatch；原两写段显式化，带外观共享读取直接使用已有领域能力，旧四细命令和注册删除。原鉴权、状态、帧序、提交点、持久结果保持。
- Owned boundary: 下文精确源码／测试／AGENTS、本 Task、B 开放迁移账及 task-07 前缀日志／证据；冻结 baseline、已提交 Task、父 Spec/Plan/契约、根控制器和 A 封存只读。不迁 OtherDetail、选角或其它协议，不启用效果／MainSettlement／Compose，不改骨干、认证算法、规则、存储结构或恢复玩法／账号门。
- Required checks: 原生命周期／codec／外观断言保持，新真实 CMD／同 provider 发件箱 flow、实际初始化回滚和创建提交后读取失败区分、有效行为坏变异；完整 Release 构建，完整 Application／Host／Protocol／Persistence／Networking 五项目及其实际 DFO，另 HttpApi／Infrastructure 各运行实际正 control；注册 capture／八行闭合、严格校验、数量／范围／行尾检查，详见下文。
- Dependencies: TASK-06（`d2e07630c3b46a348d63e065e8ee45d0945e17e6`，已独立 approved，LOGIN／频道领域完成；最终数量 8446）；间接消费 TASK-04 的同总线／普通 WorkUnits／显式请求和 typed 登记骨干，以及 TASK-05 的完整 Channel 路线与真实线证据。
- Result review: required — 四条接线、两段持久事务及创建提交后登记／读取顺序改变承载位置。遗漏会留下半创建数据、已提交写被误记回滚，或删除后多发名册导致客户端异常，普通 codec／薄入口替身验证不能独立发现。结果需快审实际事务、旧断言去向、原字节和真实持久失败区分后才提交。

## Detailed Plan

### 1. 起点与精确边界

先核上述 HEAD、原 `task/refactor-session-20261021` 分支、除本 Task 无其它未提交改动和 Task 唯一身份。读 AGENTS、ArchitecturePhilosophy 与 RefactorPreferences；当前没有 .codegraph，按 rg／拥有源码读。消费 TASK-06 Result R1 及 r3/r4 更正：不能把 Transactional 名称、错误文案、测试中故意可失败的 participant 或 SQLite fixture 的存在当作生产 SQLite enlist 证明。新结论以本簇实际源码及真实持久观察为准。

沿父授权实施、验证及本地检查点，中文，不 push／PR／合并／远端 issue／部署／清理，不运行 df_game_r／DNF.exe；主控组织结果快审与 Git 提交。本 Task 的计划由主控作 readiness 检查，不增加独立 Task-plan approval。执行中若发现白名单外必需改动，先给主控实际调用点和最小增补，不泛改其它功能或已提交治理历史。

允许生产修改：

| 仓库路径 | 最小责任 |
|---|---|
| `Server/src/Dfo.Server.Protocol/Game/AccountRoster/GetAccountCharacterRosterHandler.cs` | 只交原请求／dispatch、一次入队并等待 |
| 删除同目录 `GetAccountCharacterRosterCommand.cs`；新增 `GameAccountRosterCommand.cs` | 原名册用例吸入一个协议 CMD，返回现 Data 的 AccountCharacterRoster |
| `Server/src/Dfo.Server.Protocol/Game/CharacterName/CheckCharacterNameSlice.cs`；新增同目录 `GameCheckCharacterNameCommand.cs` | 移除旧细命令／handler，原消息 handler 变薄；新 CMD 返回现 CharacterNameStatus |
| `Server/src/Dfo.Server.Protocol/Game/CreateCharacter/CreateCharacterSlice.cs`；新增同目录 `GameCreateCharacterCommand.cs` | 移除旧细命令／handler，薄入口；新 CMD 返回现 CharacterCreationResult |
| `Server/src/Dfo.Server.Protocol/Game/DeleteCharacter/DeleteCharacterSlice.cs`；新增同目录 `GameDeleteCharacterCommand.cs` | 同上，返回现 CharacterDeletionResult |
| `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` | 仅四消息／四命令 opt-in 接线及对应失真旧注释 |
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs` | 仅34～40附近旧 GetAccountCharacterRosterCommand 注释改成实际直接领域读取，接口／业务不动 |

新名字分别为 GameAccountRosterCommand、GameCheckCharacterNameCommand、GameCreateCharacterCommand、GameDeleteCharacterCommand，区别旧细粒度 API，不留 alias／旧 constructor fallback。相应 command handler 与命令同文件。原 CharacterName／Create／Delete slice 的 Messages、payload codec、decoder、encoder 逻辑／字节保持，只移走命令与 handler 编排、调整必要 using。AccountRoster 的 Messages、codec、encoder 与 OtherCharacterDetailSlice 全部只读。

允许测试改动仅：

- 新 `Server/tests/Dfo.Server.Application.Tests/Game/CharacterRosterCommandFlowTests.cs`：唯一新 App flow 文件，含四薄入口、实际协议 CMD／同容器 WorkUnits／发件箱及必要本地夹具。
- `Server/tests/Dfo.Server.Host.Tests/GmInitializationHttpTests.cs`：旧真实初始化创建／读取／回滚驱动迁移、必要本文件认证准备 helper、有限真实残留和提交后失败区分；不改 GmTestHost 或共享测试平台。
- `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs`：将本四协议接线和原 Create／Delete 的细粒度 Transactional 类型断言迁到新 route／真实显式写段证据，其它名单和坏例保留。

现有 CharacterLifecycleServiceTests、SqliteCharacterLifecycleTests、CharacterLifecyclePayloadCodecTests、AccountRoster codec／appearance／两屏测试、所有 OtherDetail 测试均只读并回归。TestGameplay、测试超时／skip／xUnit并行、项目引用／IVT、生产共享骨干不在修改范围。

AGENTS 仅更新当前 opt-in 列表加入四名册、对应两个显式写段／原 Direct 无事务说明、旧四命令归属及精确受影响例子。原353附近路线表述和415～425附近旧注册说明按实际改写；不泛重写全架构、不提前删除其它 legacy 或变更其规则。新协议的初始化／普通事务不能描述成效果账本，`[]` 不能描述成无事务参与者。

可写 B 的 `verification/migration-ledger.json`／`.md`、本 Task、`verification/logs/task-07-*` 与 `verification/evidence/task-07-*`。现 probe-tools 原样复用；确需改工具须先给主控证据，不自动扩大白名单。baseline／所有 original／旧 closed 行／已提交 Task／A 制品不改，不另造同一研究或计划的归档副本。

### 2. 新 CMD 形状和原顺序

四新 CMD 实现同一 `IProtocolCommand<TResult>`，携带原 `NetworkDispatchContext Dispatch` 和强类型原 request；Message 交回同一 request，Frame 是 `ProtocolFrameMetadata.Game(Request.Metadata)`。薄 handler 实现对应 protocol-handler marker，仅注入 ICommandBus（原必要日志可保，但不做业务），一次 SendAsync 并等待；不在 handler 查身份、登记或驱动另一命令。

CMD 仅注入实际使用的 IOnlineUserQueryDomainService、ICharacterDomainService、IGameCommandOutbox；Create／Delete 另注 ICommandWorkUnits，原日志按需保留。不能注 bus／sender／closer／队列／执行器／行表／内容源／规则／config 或 service locator。原四 request 都非 IEffectSettlementRequest，不能加该 marker、IRequestSettlementCommand 或选中效果参与者。

同一 bus 的 ProtocolNonEffect 路线已负责当前作业请求、物理来源和登录权威捕获；仍保原各 handler 的账号鉴权和拒绝文本。现四旧命令只带 IAccountCommand，不带 ISelectedSessionCommand；A 已删除账号／玩法门，ProtocolModule 旧 exclusive 注释不构成现行为，不加账号门、选中 generation 或任意锁。

逐项搬迁：

| 用例 | 原位置／真实形状 | 新位置与不可改变的次序 |
|---|---|---|
| Roster | GetAccountCharacterRosterHandler21～53；旧 GetRosterCommand 直接调 GetAccountRosterWithAppearanceAsync，Direct | CMD 鉴权 → 直接同领域读取 → 登记 NOTI AccountCharacterRosterResponse(query=true) → CMD HiddenCharacterInfoResponse → NOTI HonorLevelInfoNotice。无 Write／ReadOnly 单元 |
| CheckName | CheckCharacterNameSlice70～99；Direct | CMD 鉴权；RawName null 直接 Malformed，不入领域；否则直接 CheckNameAsync；保原名字日志／原状态；登记唯一 CMD response。无事务 |
| Create | CreateCharacterSlice106～143；旧细命令 Transactional | CMD 鉴权；RawName null 直接 MalformedRequest，不开单元／不入领域；否则普通 Write 内直接 CreateCharacterAsync，单元返回后原日志 → ACK 登记；只有 Created 才在 ACK 登记完成后直接 GetAccountRosterWithAppearanceAsync → NOTI roster(query=false) |
| Delete | DeleteCharacterSlice121～159；旧细命令 Transactional | CMD 鉴权；RawName null 直接 MalformedRequest，不开单元；否则普通 Write 内直接 DeleteCharacterAsync；写段结束后原日志 → 唯一 ACK，回显原请求 RosterIndex；不读取／刷新名册 |

写单元分别具名 `create-character`、`delete-character`，`CommandWorkUnitDeclaration(Name, [])`，默认 Ordinary。这里空集合只表达没有 selected-session recipients，真正 transaction participant 由下面实际领域路径加入。正常领域拒绝原先未 RequireRollback，不能自行新增这种语义；成功／拒绝都按旧段正常结束，异常仍通过真实 runner 回滚／不确定出口，不 catch 后另开创建／删除写段。

Create 的 ACK／外观读取／NOTI 均在写段外，不能提前查询名册、把 ACK 放进 delegate、用一个新事务包住整协议或让 CMD 调另一 CMD。已有 ICharacterDomainService.GetAccountRosterWithAppearanceAsync 就是真实共享能力，无需 wrapper／新领域服务。Data 结果保留可使原 Host Status／异常断言继续表达原业务，不把网络出站类别和命令返回值混为一谈。

删除领域现状保持：CharacterDomainService190～216 按本账号原始确认名字字节决定目标，不按请求 RosterIndex 找待删对象；index 只参与名字找不到时 NameMismatch／SlotEmpty 分类，并在响应中原样回显。原最低空闲 slot、名字字节／长度和创建规则、初装／属性／技能算法均不改。

### 3. 真实事务、初始化和参与者对照

开工保存四原 caller／command／registration 源码与实际边界快照到 task-07-mapping，不改 baseline。以下是已核源码路径，实施时再次核当前行与调用闭包，不能从 Kind 数字推断实际参与者全集：

1. CharacterDomainService117～176：规则／原名册／名字检查 → StartingStateAsync → repository.CreateAsync(NewCharacterSpec) → initialization.ApplyCharacterAsync。CharacterRosterService248～302 写人物、两页技能、初装、10复活币、condition／stats／pvp／settings／expertJob／container state；Delete306～308 只委托行表 MarkDeletedAsync。
2. SqliteDatabase29～47 用 Path.GetFullPath 构造连接串；83～104 要求当前写事务，以该连接串 enlist SqliteDatabaseParticipant。CharacterEntryTables215～243／269～274 分别 Insert／MarkDeleted，CharacterSkillTables61～77 同 database 写技能。
3. PersistenceModule50～57／207～212 给行表及 SqliteItemDomainStore 同一 databasePath；SqliteItemDomainStore13～24 使用相同规范化连接串作为 StoreIdentity。人物行表、物品 facet 和模板读取因此复用同数据库的一个 SqliteDatabaseParticipant，而不是每领域一个可失败参与者。该 participant 的 CommitCanFail=true（41），真实SQLite CommitAsync 在178～179、RollbackAsync在182；不能用假participant文案冒充这些操作。
4. 默认创建也会 PutNew 复活币（CharacterRosterService288～290）；物品路径还可加入 ItemLifecycleScope（Abstractions/Inventory/ItemLifecycleScope 的静态 Key，CommitCanFail=false）。SqliteItemDomainStore.PutAsync153／169 的 ContentsChangedAsync 经 InventoryContentsEventDomainService.RecordAsync11～19 加入其去重 Batch（false）并 enqueue 领域事实。EventService21～42 加入 EventBatch（83～101，false）；SqliteDomainEventPersistence14～33 将事件和 delivery 行写回同一SqliteDatabase参与者，commit后只唤醒。不是全部事务都仅有一个participant，更不是空selected集合即无参与者。
5. InitializationDomainService54～58 要求现事务并读取当前模板，非默认配置才调用 InitializationAssetGrants.CharacterAsync20～24：SetLevelAsync、个人货仓设置、GrantAllPersistentAsync。等级实际变化时 PlayingCharacter.Experience80～82 enqueue CharacterLevelChanged；库存事实和等级事实的加入条件以真实配置／初装／注册为准，具体fixture观察值不宣称为所有配置恒定全集。
6. 初始化 SetLevel 走 PlayingCharacter.Experience50～70 的直接 SetExp／skill reconcile；物品 GrantAllPersistent 走 ItemGrantDelivery16+ 的持久容器 addition，不是 AddExp(source)12～26 的 experienceEffects.ApplyAsync。普通创建／初始化不因含等级／资产就升级为效果请求，不加Footprint。原可靠事件写入／delivery与资产同数据库事务，异步消费仍保持既有后续来源，不在此迁事件CMD或订阅。
7. 删除有效目标的持久路径为 CharacterDomainService212 → CharacterRosterService306 → CharacterEntryTables269 → SqliteDatabase；不把创建的库存／事件批次强写为删除恒定参与者。早拒绝可能未触达数据库；只按真实分支记账。

UnitOfWorkBehavior 原 Transactional 段对应新 Write 的 Begin／Commit／真回滚；新 CMD 本身注册 Direct。写段内不做 outbox Compose、投影或网络等待，写段外 ACK 登记失败／外观读取失败不能撤回已提交人物或在同一次 CMD 中再开一次创建段。CommandWorkUnits 的粘性失败／已提交后错误由现骨干收口；无 selected recipients 不虚构对角色的围栏。记录实际来源关闭和异常性质，不给非效果新请求添加“一次性序号／票据”合同。

### 4. 全部旧 caller 与 Host 驱动保留

现场全 Server 的旧 API caller 只包括：

| 旧 API | 实际 caller／测试 |
|---|---|
| GetAccountCharacterRosterCommand | Roster handler29、Create slice138；Host GmInitializationHttpTests112；ICharacterServices36仅说明文字 |
| CheckCharacterNameCommand | CheckName slice82；无额外测试调用 |
| CreateCharacterCommand | Create slice116；Host GmInitializationHttpTests105／141；Protocol CommandRegistrationTests111 |
| DeleteCharacterCommand | Delete slice131；Protocol CommandRegistrationTests112 |

各自定义／细 handler／Module注册另计。执行前后用精确符号／词边界 rg 复核，不把新 Game*Command 名称当旧 API 命中，也不把冻结映射原文当生产残留。若发现新caller先回主控定位，不静默扩改白名单。

Host105／141 是创建初始化的真实业务／事务测试，不是可随手删除的setup。保现两个 PvfFact、原 CharacterCreationStatus／SQL／原异常断言，改为完整协议请求和精确 authenticated dispatch。112 通过同一连接的新Roster输入验证读配置／名册不重复发资产。使用现 Data返回值，无生产setup-only Internal CMD，不绕过新协议被测入口。

认证准备仅本文件小helper：

- 使用当前测试已有账号名／密码（initial-user-01、rollback-user），TestGameplay.Run(host.Services, online.RegisterConnection) 建真实在线连接。
- 在执行器外调用现 TestCredentials.LoginAsync(scope.ServiceProvider, loginName, password, default) 得票据。
- 用 TestGameplay.RunAsync(host.Services, ...) 包测试自己拥有的 DomainTransactionScope.Begin → IGameLoginAuthenticationDomainService.AuthenticateSessionAsync → Commit；再读 online.GetRequired。
- helper取得原 NetworkConnectionContext 后，从执行器外调用 bus.SendAsync(new GameCreateCharacterCommand／GameAccountRosterCommand)。不能把真正 bus／dispatcher 入口包在 TestGameplay 中。

这是已存在的 GmPlayerDirectoryHttpTests267～283 范式；不要借用其 playerNN 固定名或更新password_hash的SQL。只复用公开现有 TestCredentials／TestGameplay API，不改认证算法／GmTestHost／共享helper。本Host原本不观察真实TCP出站，继续以真实业务和SQL为主，出站登记由下一节真实同容器flow验证，不将unknown connection下丢弃描述成网络送达。

保原初始化成功断言：等级20、货仓24、配置物品20、账号Cera等不重复。保原失败后 characters／character_skills／item_slots 为空、账号资产不撤回。结合实际表写入，有限扩充 `items` 及 domain_events／domain_event_deliveries 前后集合或精确主题残留检查，证明初装／复活币／等级事实和delivery未漏出回滚；选不会误算先前账号初始化事实／并发event消费的稳定查询。新断言在CMD外检查，保原cause／类型，不用名字含database的fake异常声称真实SQLite回滚。

### 5. 新 flow、提交后失败和 test-value gate

唯一新 App flow 文件以同一 ServiceProvider 的实际 CommandBus、CommandWorkUnits、GameplayOutboxes、IGameCommandOutbox常设适配驱动四新CMD；本地可观察domain替身、连接contexts、encoder／发送端用于记录原连接／message／登记和flush次序。不能把另一个container或裸List替身当被测transaction的发件箱，不用生产任意Func内部CMD承载测试业务。替身仅证明被观察的调用／单元／登记，SQLite结论须来自Host真实路径。

最低区分覆盖：

- 四薄handler原request和dispatch各一次交付，Message同实例、Game metadata字段保持；原网络端点／listener／source不替换。
- 四协议未认证：领域／写单元／出站均不发生，原异常文本和类型不改。
- Roster三条登记严格原序、CMD／NOTI类别与query=true，读取带外观方法而非cheap listing。
- CheckName null不入领域且给Malformed；正常状态／原raw bytes原样；不凭共享原规则测试删除新入口守护。
- Create在领域调用处有真实ordinary写段，ACK登记时该段已结束；记录ACK登记完成后才读取带外观名册，只有Created刷新且query=false。Malformed不开单元、不读名册；其它拒绝不刷新。
- Delete真实ordinary写段结束后只ACK，无名册读取／刷新，回显原ushort index；领域现有“名字决定对象”和lowest slot／名字字节等断言保留，不镜像重写整套领域规则。
- 新注册四协议及请求形状准确，构造器／薄handler／禁止CMD调用CMD和非效果状态判据照常通过；普通初始化不出现MainSettlement／effect。

增加一个最小真实持久区分，可放在现 Host 文件内：通过新Create协议CMD真实创建，随后带外观读取故意失败；使用只装饰所需方法并委托真实ICharacterDomainService的本地探针，独立SQL连接确认身份／初装／模板资产已经提交，创建领域动作与普通Write各一次，读取时无环境事务。它不能被描述成ConfirmedRollback、不能在同一次CMD重开创建段；异常／来源关闭遵守现骨干。原ACK已经登记和保留顺序由同provider Appflow与该路径可观察登记证明，不把transport发送失败与读取失败混为一类。

不强制另建第二套初始化rollback镜像测试；优先扩前述旧Host失败合同。若一次有界fixture能同时证明真实写段、提交后读取失败和旧SQL断言，就复用它，不重复大规模PVF夹具。非效果后续同名请求按现NameTaken规则处理；不引入“同序号不能再结算”、一次性票据或禁止其它合法创建的新规则。

在实现前选明有价值oracle并记录test-value判断。机械改名／签名／driver不造编译RED。至少一个独立行为坏变异必须被区分，例如将成功Create的带外观读取提前到ACK前，普通成功字节可能不变而trace／时机oracle红；恢复后绿。若已有真实失败正好证明该风险，可复用实际红绿并说明，不堆镜像变异。按需要选择第二个有价值变异（把ACK／读取放进写delegate，或破坏初始化回滚使实际SQL残留被发现），不强制为数量造重复RED。

每轮独立日志／变异前后SHA、finally恢复原bytes；编译／缺配置／夹具未建立／timeout不算行为RED，不改原断言、skip、deadline、类并行求绿。CMD可能catch的区域只记录事实，在CMD外断言，避免Xunit异常被业务catch吞掉。

### 6. 当前注册、八行迁移与数量

TASK-06已接受capture实得173 messages／205 commands，命令route为5Protocol＋1Internal＋199Legacy，Kind168Transactional＋24Direct＋9ReadOnly＋4Workflow；消息5Protocol＋168Legacy。先核其实际capture／源码并保存本Task起点；四旧换四新，合理预期为：

- 消息173，9Protocol＋164Legacy；
- 命令205，9Protocol＋1Internal＋195Legacy；
- Kind166Transactional＋26Direct＋9ReadOnly＋4Workflow；
- pending业务154→150、旧命令199→195、执行器来源72不变。

这些是核对预期，不是结果；最终以新的 ConfigureServices-only capture源码↔runtime逐条双向核实，不构建第二业务host、不启动业务、不硬填数字。若实际数不同先逐行解释。

仅闭合八个现有冻结ID：message:66056（Roster）、message:66229（CheckName）、message:65541（Create）、message:65542（Delete），及command:Dfo.Server.Protocol.Game.AccountRoster.GetAccountCharacterRosterCommand、command:Dfo.Server.Protocol.Game.CharacterName.CheckCharacterNameCommand、command:Dfo.Server.Protocol.Game.CreateCharacter.CreateCharacterCommand、command:Dfo.Server.Protocol.Game.DeleteCharacter.DeleteCharacterCommand。

destination映到四新协议CMD；每行actual_units明确原Direct无事务／原Tx→ordinaryWrite，Create写段后ACK再读的实际位置、正常拒绝和异常结果；participants分开记录selected recipients空与上文实际动态transaction参与者。protection／shared helper位置使用实际领域能力，不新造baseline ID、不提前closed T3/T4或共享保护。其它closed行、所有original/baseline不变。

沿既有preclose只放行当前TASK-07待Result → 产品／映射预检 → 执行者写Result → strict validate与selftest的顺序，保持原8坏副本拒绝，不改validator迎合新行，不写Result前冒称strict完成。

测试数量起点8446：Pvf80、App4699、Host202、Http57、Infra976、Net199、Persist477、Protocol1756。逐文件列N/D/merged及原断言／driver变化，四薄入口的Theory展开按实得计，不把一个方法当一例；Host旧两PvfFact和原codec／服务／SQLite断言不删。原细命令Transaction类型断言改守四新route与实际显式写段，不留下无去向的保护空洞。全库新数量只作8446＋N－D闭合，不冒称未跑的三个完整项目已在本轮重跑。

### 7. 正式核验和交回

源冻结后完整Release构建0warning／0error，正式五项目如下；保持原xUnit类并行／skip／期限，native require_escalated＋caffeinate、每轮独立task-07 log/meta，保存cwd／HEAD／完整command／UTC起止／exit／耗时及实际Darwin资源。

```text
dotnet build Server/86JP.Server.slnx -c Release --no-incremental
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m -m:1
```

App只允许原两条DimensionDungeonTests：Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource、All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，均Dungeon7100／maze0／cell(1,0)／entrance Left原内容错误；保原App16skip、Infra历史2skip的身份与本轮范围区分。其它失败按AGENTS拥有源码根因诊断，不自动归为已知SQLite偶发。未选Pvf／Http／Infra完整套件，完整build覆盖其构建；最终T012仍要八项目全量。

本轮五完整测试程序集使用新鲜DFO目录，各有实际正control；按既有expected的程序集归属核对其完整反例类／数（App既有62、Net7及其它本轮受测程序集的原expected），未引入故意反例时不应新增违规。HttpApi／Infrastructure另实际运行各自现有正control，过滤精确名称从现取证工具／源码读取并将实得command落盘；不声称这两程序集全量或其未执行反例已覆盖。七程序集control必须齐且各唯一；把五full与两control的范围、实际records、各类expected／observed分别列明，不能沿用Task06的74便写“本轮完整74实测”。各轮／目录不拼接混合，检查外0／exe0／orphan0；exe0只为该次观测。若新测试故意触发保护，先定向核精确类／数再给新增expected理由，旧类数不放宽。

完整Net应含原Characterization10逐名通过／0skip即可，不额外重复相同表征；15Golden对f01dc226与d2e07630无diff，原payload／codec／帧序／连接断言保持。原F88／F93四保护与F132禁用结构判据保持，未在本Task声称迁完它们。新CMD注入面、薄入口、禁止嵌套dispatch、来源失败关闭、普通WorkUnits及发件箱原规则回归通过。

最终核精确白名单、其它源／旧Task／baseline／A SHA不变、旧四API生产及测试符号引用为零、四新CMD真实普通写段与无sender/bus注入、AGENTS只对应当前opt-in。CRLF／混合文件用bytes定点改，保存行尾／BOM前后；`git -c core.whitespace=cr-at-eol diff --check`及新文件空白检查通过。Task04已披露scope双故障残余若因实际新增scoped disposable组合触及，给主控实证与最小影响，不扩修骨干或声称自然消失。

受影响检查完成后由执行者写Result R1，列实际更改／重要选择／N-D／旧断言与驱动去向／真实参与者条件／有效RED-GREEN／五full加两control的准确范围／capture与strict／限制，交主控required独立快审及本地检查点。T003整体、OtherDetail／选角／其它功能、过渡清零、T012八项目与同工具性能／单条2秒、Astra审核和用户分支收尾授权均仍在后续范围，不在本单元冒称完成。



## Result

- Revision: 1（对应Task r1、当前HEAD d2e07630；未修改父验收或生产边界）
- Outcome: done
- Changes: 四薄入口仅把原request／精确dispatch一次交给同总线的GameAccountRoster／GameCheckCharacterName／GameCreateCharacter／GameDeleteCharacter CMD；旧四细API／注册删除，原messages、payload codec、decoder、encoder保持。名册／查名直接原领域读取，无新事务；创建／删除各唯一ordinary Write只调用原领域能力。创建commit→ACK登记完成→仅Created带外观读取→NOTI(false)，名册查询三消息原序／类别／query=true；删除只有ACK并回显原ushort index。无CMD调用CMD、无新门／效果／MainSettlement／Compose。
- Boundary evidence: 创建实际复用同规范化连接串的唯一可失败SQLite参与者；物品／库存事实／事件批次按原路径动态加入内存参与者，事实及delivery仍与资产同数据库事务。删除有效目标保持原人物行表写入，不套用创建批次。空selected recipients不等于无transaction participants；早拒绝原无RequireRollback，不私添语义。精确映射见[workunit-map](../verification/evidence/task-07-mapping/workunit-map.md)，领域／规则／存储／认证算法未改。
- Why / real fixtures: App flow只构建一个被测provider，复制公开ProtocolModule提供的IGameCommandOutbox生产descriptor，使internal typed adapter解析本provider真实GameplayOutboxes／WorkUnits／message outbox；未新增IVT／反射／第二provider。encoder登记点、事务participant commit和flush分别可观察，不以裸List冒充实际发件箱。Host原真实初始化两PvfFact改authenticated dispatch→新协议CMD，setup沿原真实认证事务范式；不将生产bus包TestGameplay，不增加生产Internal setup CMD。
- Original assertions / drivers: Host原Status、异常、等级20、货仓24、配置物品20、账号资产不重复及characters／skills／item_slots回滚断言全保，并扩items身份和创建主题event/delivery身份集合，避开后台状态字段。新增真实Host创建后外观读取故障，独立非池SQLite连接仍见身份／初装／模板资产；读取已无事务，创建一次。既有骨干以PostCommitPublicationFailure包装并保exact Inner cause，未称ConfirmedRollback或重开创建段。原Create/Delete fine Transactional类型断言改守四协议绑定＋真实显式单元／SQLite结果，其余名单和坏例保持。
- Test value / RED-GREEN: 新App17/17、Host初始化8/8定向通过；ACK前提前读取名册坏变异使3例中mode1在trace pos2红（另外2过），finally恢复原bytes/SHA后3/3绿，见[mutations](../verification/evidence/task-07-inspection/mutations.json)。编译约束／internal注册接缝错误、四个wrapper Equals及新增typed cause的初版oracle错误均保原轮日志，不作行为RED；没有为新oracle改产品算法或旧断言。
- Formal checks: 完整Release --no-incremental build0warning／0error。选定五full：App4716＝4698pass＋2准确原Dimensionfail＋16原skip（115.950s）、Host02 203pass（113.966s）、Protocol1757pass、Persistence477pass、Net199pass；共7352＝7334pass＋2旧fail＋16原skip。HttpApi／Infrastructure仅各运行一个实际正control，2/2通过，不冒称两full或八项目本轮全量。两内容失败均Dungeon7100／maze0／cell(1,0)／entrance Left原InvalidDataException，准确名字与原log在[formal-summary](../verification/evidence/task-07-inspection/formal-summary.json)。
- New observed validation failure: Host01原ComponentLifecycleTests.ComponentsOpenOnlyTheirConfiguredPortsAndReleaseThemOnStop(http:true,game:true)在line39 Channel CanConnect期望true实际false；邻近IChannelTcpService EADDRINUSE日志缺实例／port标识，不能证明是同一失败实例。原AvailablePorts释放ephemeral至Build/Start有候选占用窗口；具体根因未完全证明。原测试、TcpServices、GmTestHost端口／timeout／parallel代码与HEAD零diff，Task06同方法／Host full通过；按主控有界诊断，精确method四variants实跑4/4，再Host full fresh02为203/203。首轮原日志／诊断边界保留于[host-port-diagnosis](../verification/evidence/task-07-inspection/host-port-diagnosis.json)，未新增允许失败基线，未称修复端口竞态；交required快审判断当前局部证据。
- DFO / wire: fresh01为五full＋两control，七程序集unique control／74精确原expected，注意Host01有上述一条测试失败；final Host02另fresh02目录，单程序集control／1记录。各目录独立分类，外／exe／orphan均0，不拼接。exe0只是观测。Net全量含表征10逐名通过／0skip；15Golden对f01dc226／d2e07630实际bytes相同，三个codec／decoder／encoder原段逐字节保持。
- Counts / capture: N19/D0＝新App17＋Host1＋Protocol1。全库inventory8465＝原8446＋19；Pvf80／Http57／Infra976沿用Task06完整数量，后两仅本轮control重跑，非八full实跑。其余inventory App4716／Host203／Net199／Persistence477／Protocol1757。配置注册capture实得173消息（9Protocol＋164Legacy，其中14纯遥测）；205命令＝9Protocol＋1Internal＋195Legacy，166Tx／26Direct／9RO／4W。只关闭8冻结ID，其它closed／所有original／baseline不改。
- Source and process checks: 精确15物理source/test/AGENTS路径（1删除）、旧四API全Server引用0、薄入口／注入面／唯一普通写段、行尾／BOM／whitespace及保护路径检查通过；初始freeze后仅AGENTS给旧Transactional规则加路线限定，未改执行源码。App耗时与Task06 117.663s、Host与119.339s同量级，资源原meta照录，不把套件时间／内存当CMD2秒接受线。preclose仅当前Task待Result；Result后执行strict／selftest，以下最终证据确认。
- Remaining / scope: 本Task不代表T003或Spec完成。非遥测业务150、原细命令195、来源72、5保护／8过渡仍pending；OtherDetail、效果链、其余功能簇、T011清零、T012八full与同A工具性能／单条2秒、Astra审核及用户分支收尾门仍在后续。Task04 scope双故障残余未因本单元新增scoped disposable组合触及，未扩修。未gitadd／commit／push／PR／部署／启动下一Task；required独立结果快审和本地检查点由主控负责。

- Final closure checks: Result R1后strict有效且completion_results_pending为空，八坏副本全部拒绝；见validate-final.json／selftest-final.json及实际command/log/meta。manifest排除Task全文件，另绑定R1字节前缀供主控追加review。执行者未自批required review或提交。
