# TASK-30 — 邮箱旧协议与领域自结算边界取证

## Task Unit Contract

- Revision: 1。
- Outcome and references: 为 P5 T-005 邮箱六消息／五 handler／共用 MailboxDelivery＋MailboxCommand 簇建立真正 decoder→dispatcher→旧 fine→真实领域／SQLite／原发件箱的旧行为证据，区别领域 action 内主动 Rollback、真正 fallible Commit 及不确定／scope 清理失败。实证哪些旧响应需要保持、哪些事实需要主控受影响 Plan HOW／Spec WHAT 裁定；不实施迁移或预选新恢复接口。消费 B4 RQ-001/003/006～009/013/014、AC-001～005/008及原保护，VC-B4 仅 Cera，不向邮箱扩张。
- Owned boundary: 本 Task；`verification/evidence/task-30-inspection/` 下新专用探针／观察器／脚本／原日志与报告；`verification/logs/task-30-*` 新日志。所有原 tracked（含 Server、测试、配置、文档、两个 canonical、既有工具、Task01～29、A及Spec/Plan/routing）严格只读。本 Task 不关闭任何 ledger 行。
- Dependencies: [TASK-29](TASK-29.md) r2／Result R1／fresh independent approved／主控消费，已 committed done 于 `6e21892a9d2959a6b7c0bdad96fae63dbb87ef5a`，最终 Task47753B、SHA `81d9d3ef3a8b92850971a784f9dcf4f81ec88946b8f33425b22f1b426345f920`。其余旧 Task／探针仅作只读方法参考，不作未交付依赖。
- Required checks: 起点完整 NUL tracked bytes／blob map与HEAD；真实入口装配与各声明成功控制；下述最小自结算／native Commit／不确定／cleanup矩阵、cause图／全部资产／ACK与实际关闭；原 Mailbox 强用例 focused；探针 Release build/run 原命令与退出码；最终所有原 tracked＝before＝HEAD、精确新增路径 manifest、authored whitespace与native自然退出。Task29 9029 库存仅引用，不冒新全量或性能结果。
- Result review: required — 后继迁移方式与可见行为裁定依赖本证据；必须 fresh independent result quick review，主控消费后才本地检查点。
- Authorization: 仅调查和证据。不能改 MailboxDomain 自Rollback、runner/core/API、原测试／共享调用方或 canonical；不写新 Spec/Plan／Task31，不发明 VC／raw许可／participants。无 stage/commit/push/PR/merge/remote issue/version/tag/deploy/游戏/清理；独占临时 fixture DB按其既有拥有生命周期使用，不接触用户库／ClearAllPools。

## Detailed Plan

### 1. 当前治理与开始条件

唯一 workdir `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，branch `task/refactor-session-20261021`，HEAD必须为上述 Task29 检查点，index空且只有本新 Task可变。B为`.rks-agent/tasks/serial-gameplay-executor/attempts/spec-002-protocol-level-cmd`，E为`B/verification/evidence/task-30-inspection`。

原 current child Contract8898B SHA `32d83900f17cbfbdf30ab69e40ca07bd478a8a6368951b8215c94c6edce921d6`；Spec B4 ready R2 42632B SHA `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60`；Plan P5 71863B SHA `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0`。先读 Task、P5§4自结算限制／T-005／验证，以及相关Spec保护；复核身份，漂移返主控而不覆盖。Plan Next段Cera是已由Task29交付的历史临近选择；T-005仍未覆盖，本调查属于其迁移前必要事实，不自行改Plan或宣布整个T-004/T-005完成。

主控已核29个Task ID／路径唯一、committed Outcome、实际Dependencies anchor存在且DAG无环；30为下一unused。本单元与总目标对齐：一次有界旧事实调查，避免把P5第五个Cera consumer或VC-B4推成通用邮箱许可；不开展与迁移无关研究。

### 2. 已核实际结构与问题

实际源码入口（行号导航，执行时读当前）：

- `Protocol/Game/Mailbox/MailboxDelivery.cs:54–101`：只读session门→旧bus，MailException/InvalidData/所有非取消异常转ACK；发送成功为ACK→assets，claim为assets→ACK，open为ACK→inbox。catch覆盖fine整个scope等待，投影在catch外。
- `MailboxCommands.cs:15–49`：唯一 IAccount-only Transactional fine，操作Send／Claim／Read／Delete／Open／Query；无selected marker、footprint或Main。Send后Assets.Prepare及gold读、Claim后Assets.Prepare都仍在原事务内。
- `MailboxSendWire.cs`、`MailboxWire.cs`、`MailboxIds.cs`与ProtocolModule实际注册：65630/0x005E单附件发送、65851/0x013B多附件发送、65631/0x005F领取、65670/0x0086 action(0删／2读)、65632/0x0060打开、65860/0x0144收件人查询。六消息均NonEffect；Send两个消息共用CLR／handler，但真实body编码形状不同。
- `Application/Mail/MailboxDomainService.cs:171–190`：standalone自己Begin/Commit/Dispose，ambient借用事务，但任何catch都直接对当前事务RollbackAsync后throw；可使action尚未结束时IsSettled=true、Current=null。该catch不传原cause，回滚二次失败是否在typed异常图保住原action必须动态观察，不由语法猜测。
- `Application/Transactions/DomainTransaction.cs:211–275`与Scope:37–38：IsSettled≠RollbackOnly，Completion是实际事实；settled后Current=null。当前 WriteResult的恢复仅Commit窄span，P5明确Mailbox未授权，不能把已自结算称正常RequireRollback。
- `Mail/MailboxCoreIntegrationTests.cs:456–470,530–536`：真实SQL失败和caught refusal后Commit必须throw、资产保持的共享领域契约。原测试不能为迁移方便改弱。
- 共享调用者须读实际相关段：ItemDelivery溢出/MailOnly、GmOfficialMail prepared附件及整批audit、QuestHelperPuppet撤销/替换、Housing harvest、Wedding settlement。已有IsSettled分支如实保留；MailCreated LiveOnly订阅按消费时投影，不能前移。

旧selected scope为空只是待动态核实的预期；邮箱没有Cera DemandEffects依据，不因此新增actor、围栏或恢复策略。结果要给出真实状态及后继决策输入，不能替controller裁定新WHAT。

### 3. 最小真实 harness 与观察

E内建独立net10 Release可执行ProjectReference现有Application.Tests，复用当前真实ShopFixture（NpcShopPurchaseTests的internal类有public ctor(Action<IServiceCollection>?)及Services/Connection/Session/OnlineUser），可用精确类型／public构造／属性窄反射装配，或既有friend测试程序集名访问。若采用friend，记录其E/bin Location/MVID/SHA并明确不是原正式Networking.Tests。不改项目引用图、friend清单或生产可见性，不复制整个测试树。

按MailboxProtocolIntegrationTests:428原fixture配置FreeMailItemDefinitions／HighGoldLimit的等价仅测试内容控制，保持原业务模块／SQLite实例；可复制这两个小测试内容源并记录原source。构造fixture/setup独立执行，不在外层TestGameplay里await真正dispatcher造成自等。设置用同provider原TestGameplay／事务；实际协议请求必须经已注册decoder的实际selector/type＋完整body解包，再送同provider INetworkMessageDispatcher。不能用new Mailbox*Request、直fine／handler充当decoder证明。六路全部用真实body，包括single/multi特有type字段、claim attachment ID编码、Read/Delete action、Open空body及Query长度前缀；不要猜最小有效prefix。

窄decorator逐参数委托原IMailboxDomainService／IMailboxSendDomainService；需要观察fine可用原descriptor委托wrapper，不替代原业务或管线。记录真实domain-enter／return／throw、fine-return／throw、scope Dispose、Tx原对象、Completion／IsSettled／IsRollbackOnly／GuardFailure／CommitFailure、Current、GameplayJob及selected scope。精确反射只读internal状态，不能改私有状态或伪造Completion；cause保存同对象ID、完整类型／native code／原stack／Inner和Aggregate图，ILogger保存真实引用与category/level。

每场景新独占fixture。所有生产user tables保存schema、完整列、稳定rowidentity、每列值和NULL/BLOB；基线、暂存、action结束、协议终局、必要retry各phase均落盘。同run资产回滚逐列原样相等，不归一时间或GUID。注错表DDL安装后入基线，单列清楚；观察ReadAsync可能提前enlist同一既有SQLite participant，记录并保证没有第二fallible。真实只读观察不可写；测试SQL注错用fixture拥有方法，setup表不误称产品变更。

发送路径必须原registration adapter／原outboxes→production OutboundSender→受控内存transport，保存实际batch/frame bytes、unit state、report、Send/Close次数／顺序和物理reference。参考Task29 Delivery接法，但独立读实际生产GameplayQueue的first factory，确保原GameplayCloseCauses同provider绑定；RecordedOutbound最后factory缺该参数，不能据空链猜没有链。只恢复原production descriptor工厂，不手工Record链、不改Support/core、不提前flush、不另建provider/outbox。无close也要真实sender处理、exact session Standing／IsBlocked、closeentries、actualClose、source/recorded链和连接仍可用控制；原RecordedOutbound.Discard不算物理关。明确controlled transport不是TCP/A21，不模拟未运行的disconnect child。

### 4. 必须取得的有界矩阵

先跑一个有效wire成功控制证明harness，再逐场景扩展，保每次原命令／源冻结／加载DLL身份。以下场景可共享观察器但不能共享fixture或用静态猜测替代：

1. 六实际消息路由均有成功控制（Action读与删分别真实目标）；Send至少一场真实equipment＋stack＋gold转移并保设备UID／core，single与multi各自真实decoder。Claim以真实系统／prepared邮件setup后原attachment编码进入领取，核实际资产及ACK顺序。Query非自己收件人、Open真实inbox；另核不支持action早拒绝不进fine。MailCreated事件原消费与发帧时机分清，不强制把LiveOnly刷新和原reply扁平化。
2. 真实晚期Send action SQL：原BEFORE INSERT mailbox_messages触发RAISE(ABORT)，先实际取equipment／stack／gold，捕获native19/1811及自Rollback时点；必须证明暂存变化存在、原全部生产列最终回滚、Current/Completion及旧拒绝ACK／无或有围栏关闭。去trigger后合法新发送成功，不能叫幂等retry。
3. Claim真实晚期领域拒绝（如mark claimed后AddGold触及实际limit，或原可达等价业务失败）：证明已暂存claimed／资产后自Rollback、原MailFailure、完整回滚与原ACK；保真实sameprovider，不用decorator直接throw替代此项。读取/删除等共用RunAsync的边界由源码与成功控制限定，不能从Send推每条故障已动态测。
4. 真正native fallible Commit：沿此前已证SQLite deferred FK方法，在实际mail INSERT/claim写的trigger内成功写DEFERRABLE child；action实际返回receipt／fine body返回后真实SqliteTransaction.Commit报19/787。核FK1、child暂存、原Commit栈、KnownStorage/FallibleCommit cause同对象、ConfirmedRollback和全列资产／旧ACK／实际关闭。Prepare受控throw不能替代nativeCommit。与action自结算并排保存。
5. 三组旧广catch边界：C1原SQLite已durable提交后的受控nonfallible Commit违约；C2在场景2真实action SQL同时注入一个nonfallible participant Rollback固定第二cause，观察自Rollback是否保两原cause／typed图和真实Completion；C3真实旧fine DI scope DisposeAsync在原body返回／commit后抛固定cause。每组均证明注错确实到达、真实资产提交/回滚、不确定种类、旧ACK/异常、实际精确Fence／physical block／Discard／close及链。只记录，不决定应该如何修。C2即使typed图丢原cause，也保logger原SQL引用与实际观察，不“补”图求通过。
6. 共享领域控制：原focused MailboxCoreIntegrationTests含DatabaseFailureAndCaughtRefusalCannotCommitPartialAssets／caught Commit throw、MailboxProtocolIntegrationTests全部，以及Protocol.Tests MailboxProtocolTests实际原filter运行并保原方法／参数／断言数及结果。它们分别证明standalone/ambient共享契约、原手造handler业务强用例、codec；不称这些focused已经端到端。若原测试出现意外失败，先系统诊断保raw；不得删断言、改期限/并行/Skip或把非登记失败混作Dimension基线。

除非观测缺口要求，本单元不扩大到取消／replacement／每条操作全部C矩阵；这些未测边界在报告明确。新增注错属于受控事实，不称生产incident。没有永久xUnit新增、产品mutation或八full义务；不为了满足test-value制造编译RED。

### 5. 原命令、完整性与交付

脚本把原argv/cwd/HEAD、UTC start/end、自然exit、elapsed、环境与源freeze写入E或本Task logs。native用require_escalated使FSEvents可用，按既有正式测试配置console detailed／原deadline；探针Release --no-incremental，不能只跑旧DLL。每轮诊断和改变前source快照保留，不覆盖raw；freeze与实际loading DLL／production Release copy SHA及MVID核同。后续再改driver需新freeze，早轮不得冒最终通过。

DFO_GAMEPLAY_ASSERTIONS用本轮新路径观察，原counter/controls不得改。本轮probe零记录仅是观察，focused不冒74／7或Task29八selected库存。当前9029＝9009pass＋原2exactDimensionfail＋18skip只引用，未运行部分不称fresh。Golden／canonical／Spec/Plan原bytes由完整tracked map覆盖，无需新建旧坏副本或重复全量。

先完整`git ls-files -z`逐path封存bytes/SHA256/git blob／current=HEAD；两个canonical和中文路径都在map，原tracked全部nonowned。末尾同集合及逐SHA复核不变；新Git NUL untracked精确等本Task/E/logs manifest，manifest self排hash，Task绑定精确pre-Result prefix避免循环，完整Task身份停止writer后外报。原源码／日志copy标raw保持bytes；新authored按blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol检查，不能用CR例外放过所有trailing。记录native全部自然结束，writer停止后交fresh reviewer。

Result仅在所需证据齐后append，Outcome:done表示有界调查完成并待required review／主控commit，不表示邮箱或Spec完成。报告区分已动态证明／静态共享推论／未测：真实路由与注册、standalone/ambient自结算、action vs commit、cause精度、typed和scope旧可见结果、全部资产／frames／实际close、对共享caller后继影响及具体尚需主控裁定处。不得预写P6/B5、通用API、恢复所有settled或blanket participants/fence方案。

确切harness普通编译／命名／导入／观察器接线问题可在E内自主纠正并保诊断；需要改原产品/测试/API、变WHAT/共享契约、超白名单、或真实路径不可维持则保最小直接证据返主控，producer不自行扩大权限。Result之后停止所有writer/native，主控读实际证据→fresh required quick review→本地检查点→再选择迁移的受影响gate。
