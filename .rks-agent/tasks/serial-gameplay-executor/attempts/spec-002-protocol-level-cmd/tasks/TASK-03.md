# TASK-03 — 注册、来源与实际工作单元的开放迁移账

## Task Unit Contract

- Revision: 1（标识本契约与详细计划）
- Outcome and references: 完成父 [Plan P1](../plan.md) T-002 剩余的迁移基线与对照账，对应 [Spec B1](../spec.md) RQ-001～008、RQ-012～015及AC-001～005/008/009：按真实注册冻结消息/旧CMD全集，建立非协议来源与事务入口索引，给每项留有证据的后续去向及工作单元闭合位置。交付可重复导出/校验工具、冻结基线及逐簇维护的开放账；本Task不迁协议，不把pending登记为已对应或已完成。
- Owned boundary: 本Task；本B目录下文列明的两个probe工具、三个迁移制品，以及 `verification/logs/task-03-*` / `verification/evidence/task-03-*`。最终不修改Server、Tool、AGENTS、父Spec/Plan/契约、已提交Task或A封存制品。
- Required checks: 两次独立运行的注册基线稳定且与源码接线交叉闭合；注册身份/基数/来源覆盖与原文位置核对；校验器拒绝缺行、重复和无证据提前闭合；索引的真实事务入口、四项保护与过渡机制去向检查；最终产品零差异、证据/数量账、diff-check。具体命令和接受条件见下文。
- Dependencies: TASK-02（已提交 `aa1e732b0af5cc0040521213422833d2a9b9a5ce`，四保护及F-132禁用判据；间接消费TASK-01的已清理基线）。
- Result review: required — 后续每簇以本账作为范围与保留项输入；漏注册/来源或把声明Kind当实际事务会到末期才发现，机器数量检查不能代替真实入口/事务判断。独立快审需检查全集交叉对照及pending边界。

## Detailed Plan

### 1. 起点、授权与精确制品

起点HEAD为上述TASK-02检查点，原分支 `task/refactor-session-20261021`，数量账8365。开工确认HEAD、工作区及已提交依赖；现仅本Task计划是新文件。沿父授权执行和本地检查点，不推送/PR/合并/远端修改/部署/清理，不运行df_game_r/DNF.exe。当前无.codegraph，按实际源码及注册扩展读取。

除Task与按前缀归属的日志/证据，持久输出固定为：

| 路径（相对本B目录） | 责任 |
|---|---|
| `verification/probe-tools/migration-export.cs` | 仅装配ServiceCollection并导出当前真实CLR注册，不启动服务或执行业务 |
| `verification/probe-tools/migration-ledger.py` | 在独立临时目录生成console项目/运行导出、提取源码位置、生成或校验数据；支持内存坏数据自检 |
| `verification/migration-baseline.json` | 本Task起点的冻结注册、源码定位/哈希及来源/事务入口清单，不随之后迁移重写历史 |
| `verification/migration-ledger.json` | 与冻结基线按稳定ID全覆盖的开放对照，后续Task只闭合实际所改簇 |
| `verification/migration-ledger.md` | 人可读入口：口径、数据字段、来源/事务/保护索引、更新及闭合规则、下一有用边界 |

不引入通用schema、运行时审计或新Tool项目。临时console csproj、bin/obj只放`/private/tmp`的本轮独立目录；必要生成的csproj/命令及实际运行脚本作为task-03证据保存。不要把绝对本机路径写成不可复用ProjectReference；Python按自身位置定位当前工作区，再为本轮临时项目产生绝对引用。不要修改全局环境、用户配置或读取凭据。若现接口不能按该方案装配，先报告具体入口证据与最小调整，不顺手改产品。

### 2. 冻结真实注册，源码与运行时双向核对

沿 `Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs` 的生产模块装配顺序，创建ServiceCollection/AddLogging及空Configuration，依次调用Application、Infrastructure、Persistence、Networking、Protocol的ConfigureServices。只枚举ServiceDescriptor的ImplementationInstance中公开的 `MessageHandlerRegistration` / `CommandHandlerRegistration`；不BuildServiceProvider、不解析PVF/SQLite、不Start Host。

导出稳定排序的原始事实：消息枚举名和数值、Handler CLR全名/assembly、telemetry；命令CLR全名、handler/invoker的实际类型、ExecutionKind及ICommand结果类型。若可从handler泛型基类可靠取得消息CLR形状，一并记录来源；不能取得的显式标为待切片核实，不以猜测类名填入。原始数据与导出工具/模块源码HEAD及哈希绑定。时间、命令、临时路径等变动元数据放独立运行manifest，不混入要逐字比较的稳定payload。

当前应交叉得到：173消息/141实际handler；14遥测/2类（HiddenCharacterInfo、UnansweredReport），非遥测159/139；204命令、203实际handler；170 Transactional＋21 Direct＋9 ReadOnly＋4 Workflow。声明Kind只是事实字段，不预填实际提交点。消息范围只取真实注册，未接线枚举不补行；139不是最终CMD数。

源码侧检查 `ProtocolModule`、`ApplicationModule` 及五处扩展：CharacterNameTagExpiration、OnlineExpiredItemCleanup、BossTowerDailyRefill、TauKingdomDailyRefill、SouthernDaleDailyRefill。实际API为 `AddCommandHandler<...>`、`RegisterMessageHandler<...>`、`RegisterTelemetryMessageHandler<...>`。提取每个调用的文件/行号与原文类型参数，忽略注释/字符串伪命中；泛型参数按平衡尖括号处理，不能简单按逗号split。运行时完整类型身份为权威，短名/using别名需明确唯一解析；遇多义/未配对即报文件/行，不能静默删记录。源码与运行时按消息ID/命令CLR双向一一配对，不能只比总数。

命令源码位置分布应为Protocol163、Application36、五扩展各1。特别核对HousingBuyItem与BuyItem共享实际BuyItemCommandHandler，完整名/短名不算两类。基数或分布不符时先找实际差别，不硬编码生成预期行。留原始运行时JSON、源码提取结果及交叉校验报告供快审；无需把所有源码复制进制品。

### 3. 开放账的最少字段与闭合规则

以`message:<枚举数值>`及`command:<CLR全名>`作稳定ID；全部173消息和204旧命令保留一行，遥测有明确保留理由，不把它列作待迁业务。消息行关联原handler、已知调用的旧命令/共享Delivery/Coordinator、最终协议CMD、工作单元及证据；命令行关联原注册/Kind、原调用者、实际事务/参与者、并入/共用能力/删除去向及新位置。用各集合显式ID引用，不能复制出一套脱离基线的“完成统计”。

本Task可以从直接源码取得调用/帮助器关系，无法可靠静态确认的标`pending`并写待查的具体源码锚点；动态派发或共享帮助器不能靠文本无命中记成none。**原声明Kind、实际Begin/Commit/RequireRollback及自开边界、最终工作单元**是分开的字段。未逐项核实的实际边界/参与者和所有新CMD/新单元保持pending，不因Transactional就填“一个事务已对应”。

账的迁移状态仅需pending/closed及具名不适用原因。closed必须具备最终CMD/真实共用能力或删除去向、原实际边界和新边界逐项对应（没有边界须有依据）、参与者/具名保护的保留位置、相关测试/行为证据及完成Task；暂未形成这些证据的业务行不得closed。遥测可标保留而非声称已迁，解释其14/2日志例外。本次只建立基线，不给任何业务迁移行预置closed；后续每簇必须连同共享帮助器所有调用者闭合，不能留新CMD→legacybus隐藏路径。

校验模式至少检查：基线ID唯一、当前账与冻结全集完全同集、每个引用指向存在ID、遥测/业务分类一致、未闭合项的字段明确pending，以及closed所需证据字段实际存在。为证据路径做工作区内的存在性检查，不把含任意字符串视作证明；内容是否等价仍由每Task审查。校验器不据空壳文件/数量自动批准语义。

### 4. 来源、真实事务、四保护与过渡清单索引

来源是单独的小表，不能由204旧命令推导。先宽检索Server/src中IGameplayQueue、GameplayWork、GameplayTimerWork、CoalescingGameplaySource及HttpGameplay/RunsOnGameplay/EnqueuesItself/OutsideGameplay；保存位置清单。逐命中分类为真实producer、共享mechanism或明确只读/内容预热等排除，并链接真实入口方法。此宽扫描涵盖target-typed `StartupJob`，不能只搜`new GameplayWork`。后续新增来源须补账，现有机制命中不冒充独立业务CMD。

来源表覆盖：

- NetworkConnectionLifecycleDispatcher连接建立/断开及深度拒绝补救：记录精确source与root/继承cause差异；F-131保留现状及精度差距。
- GameServerStartupService的epic-book、experience-storage两写；GmBootstrap的executor外密码准备与单段写；Joust启动恢复。
- DomainEventDispatcher事件泵/重试/超深拒绝结算；due读取在执行器外，Reliable、LiveOnly、Direct三类原先后单列。
- ScheduledTaskHostedService按definition ID的调度；CardReward延迟producer的离开作业/延时/再入队/drain。
- HTTP/GM：记录HttpGameplay共同入口、实际注册路由/方法/模板及默认RunsOnGameplay归属；EnqueuesItself和OutsideGameplay的具体路由/理由单列，不能把整个GM group当作已证明每条终态内部CMD。
- 各timer/手动Refresh入口：LoginSession、RoomMechanism、Altar、PartyWipe、CreatureClock、Weakness、Marriage、TournamentSweep、Boss/Illusion以及Fatigue/Altar/Dimension/GoldSilverHorn/Premium/ReviveCoin/Mirror/ItemUsage/Tournament/Heras日切。实际类型/入口以本轮扫描为准，手动与周期共用业务可同一来源行但分别列调用点。
- 测试入口：记录现TestGameplay/直发bus/queue等测试适配边界及调用文件索引；测试专用命令和反例不计生产204，不把每个测试case造为生产来源。

每来源行记录原producer/位置、合并键/忙轮次、链和深度/source、目标输入、取消/停机排空/入队拒绝、原业务动作与最终内部CMD/单元pending。GameplayTimerWork当前None入队、body持caller token、Tick失败Warning继续等语义直接取拥有源码，不因重命名丢失。F-07扫描/子CMD与Premium完整fingerprint方案引用P1，不在此提前声明实现。

实际事务索引至少检索并解释：UnitOfWorkBehavior的只读/写、RequireRollback、Freeze/Commit/Abort；DomainEventDispatcher的claim/消费/完成失败标记；TransactionalDungeonVisit与DungeonLifecycle的ambient复用/自开；EpicBook启动自开；BossTower/IllusionTower；Mailbox/ItemGrantDelivery条件边界；CharacterStateAccess/AmbientStoreAccess、CreatureRuntime及ItemContainers的持久/易失自开。宽检索DomainTransactionScope/DomainTransaction/Begin/Commit/RequireRollback的生产位置，并对机制定义、调用和非事务同名噪音分类。RequireRollback的rental、Sao、SouthernDale、BossTower、DungeonAdmission、ItemDelivery、QuestHelperPuppet等领域消费不能只列pipeline。索引不把一个全局Begin引用表说成204条已核实的事务对应；逐簇闭合责任留在各命令/来源行。

四项保护索引引用TASK-02的最终测试名、单点变异日志及旧源位置：RecoverStamina.IsInWorld、Teleport.Owns(previous)、Safety.DiscardExact、Ledger Capture后二次DemandCurrent。新CMD归属/测试适配位置pending。后续handler薄化时要把观察入口迁到真实保护新归属，保原静默/不开始结算/精确丢弃/序号语义，不能原样冻结0bus次数或连旧handler一起删守护。F-132引用12程序集禁用判据及坏例，准确为机制未修复、生产禁用；新登记接口不得暴露该API。

把T1～T6及P1明确允许的legacy路由/LegacyRequestAdapter等临时机制作为删除对照项，记原位置、后续owner、最终检索/行为证据pending；不把常设GameplayJob身份、环境事务和发件箱适配器当残留。

### 5. 执行与验证顺序

1. 核对工作区/已提交依赖；读取拥有源码，建立上述轻量制品与导出器。导出只编译和读取注册，不改产品或执行玩法。
2. 本机`require_escalated`环境跑两次独立capture，使用不同临时项目/唯一日志目录；记录原命令、cwd/HEAD、UTC起止、退出码、环境及实际取得的资源数字。示例稳定接口：`python3 <B>/verification/probe-tools/migration-ledger.py capture --repo <工作区> --output <本轮目录>`；最终脚本可选择等价清晰参数，并在README中给可复现命令。两次运行的规范化payload逐字/哈希一致，运行manifest独立保存。
3. 以第一次经过源码交叉校验的payload生成冻结baseline和开放账；第二次独立导出验证完全同集、类型身份和Kind分布，确认所有源码接线有唯一运行时对应、反向无遗漏。工具遇未知/多义应失败并给具体位置，不以过滤求一致。
4. 完成来源及事务宽检索逐命中分类、人可读索引和四保护/过渡项引用；实际具体事务尚未审定的行保留pending。按当前源确认下一最小贯通候选Channel ScriptVersion与GmBootstrap的输入锚点即可，不预先创建Task或闭合其行。
5. 运行validate与内存坏数据自检：至少缺一条真实消息、缺一条真实旧命令、重复ID、遥测误归类、断引用、将尚pending的业务行伪标closed而缺边界/证据各应返回非零或明确拒绝；原始数据不改写，变异副本只在本轮证据/临时目录。原版全部通过。此项是数据工具的有效区分，不制造业务TDD RED或拿编译错误当RED。
6. 最终核对Server/Tool/AGENTS无diff、已有Task/父制品/A封存不变；只持久化白名单B文件。`git -c core.whitespace=cr-at-eol diff --check`及新文件尾空白检查；原始工具日志保持原文。记录N=0、D=0、数量账8365不变。

本单元不改变产品/测试，不要求重跑8350+的业务套件、CMD测量或整解决方案完整构建；临时console实际成功编译/两次运行及数据交叉验证是本单元的适当检查。使用require_escalated是既有FSEvents环境要求，保持生产项目引用/编译约束；不调整原测试并行、期限、Skip。命令失败先保留原日志再查具体原因，不以新增测试或改产品让导出工作。

结果须列实际文件/注册基数/来源覆盖、未闭合责任、稳定导出与反例检查证据、产品零差异和8365不变。required独立快审后主控提交；后续T-003首次骨干/贯通以本开放账及TASK-02守护为输入。所有业务行pending不表示本Task未交付基线，也不等于Spec/迁移已完成。


## Execution refinements — 源码事实与数据锚点

本轮只构建证据工具／制品，没有产品改动。初版initialize的生命周期方法锚点写成非现有名字，initialize-01原日志重现ValueError；按实际DispatchConnectedAsync/DispatchDisconnectedAsync修正，不算行为RED。源码逐命中核查还更正工具对模板`/gm/{**path}`的解析（只把独立path参数当局部helper，不误跳字符串）并单列Joust周期入口；事务扫描显式纳入BeginSnapshot，ordering.Begin/runtime.Begin列为非事务噪音。CardReward RunTimerAsync实际建新Timer root，而不是继承发布作业链，按当前拥有源码保留此事实。

主控复读发现Recover原位锚点误指旧细CMD80行；改为完整handler检查行，并断言该行位于RecoverStaminaHandler声明之后，最终为142。上述都是尚未提交基线的本轮数据纠正；原始命令日志和中间校验保留，不改任何已封存结论，不因此扩展迁移范围。

## Result

- Outcome: done（实施轮R1，Task契约r1；待required独立快审与本地检查点，不表示业务迁移／Spec完成）。
- Changed: 两个B本地probe工具、冻结`migration-baseline.json`、开放`migration-ledger.json`和人可读`migration-ledger.md`，共五制品及本Task/证据。真实ServiceCollection注册导出、源码逐条双向核对、31非HTTP来源与54具体路由、真实事务拥有者与宽扫描、四保护/F132及T1～T6/计划legacy过渡项均有入口。最终五制品SHA见[owned-artifacts-sha256.json](../verification/evidence/task-03-inspection/owned-artifacts-sha256.json)。Server/Tool/AGENTS、旧Task、父制品、A封存全无diff。
- Why: 注册CLR身份避免短名/完整名分裂，源码位置交叉核对避免只比总数；声明Kind与实际工作单元严格分字段。当前消息/helper调用只给文件级词法候选并明确待查，不把候选当调用图；实际单元/参与者/最终CMD/新保护归属全部留给后续逐簇闭合。
- Checks — 捕获: 两次本机require_escalated独立临时console项目成功（10.832s/3.486s，各exit0），只ConfigureServices并枚举公开ImplementationInstance，没有BuildServiceProvider/Host启动/PVF/SQLite解析或业务执行。两轮规范化payload逐字一致，SHA256 `4861e139bb126adaefba9240d0371d582c22cab149277f1ce8902cf22dc3f043`。实际完整命令、临时csproj、UTC/环境/退出/耗时/children_maxrss与原console在`verification/evidence/task-03-capture-01/02`各manifest/console，资源为Darwin子进程bytes观测，不作时间/内存门槛。
- Checks — 注册闭合: 173消息/141handler，14遥测/2类，159业务/139handler；204旧命令/203handler，170Transactional+21Direct+9ReadOnly+4Workflow。Protocol163、App36、五具体扩展各1。全部377条源码↔runtime一一对应，handler及命令原定义源码/注册文件哈希逐项核对；HousingBuyItem/BuyItem共享实际BuyItemCommandHandler，不按源码文本误计。139不作为最终CMD数。
- Checks — 来源/事务: 85来源＝生命周期2+启动4+Timer21+事件2+通用调度1+延迟1+HTTP54；HTTP为39默认group入队、3自行入队、12明确排除。来源143代码命中逐项分为95producer/32机制/16HTTP注册或排除；事务221命中分为真实入口20、commit17、rollback-only14、机制/依赖及ordering7/runtime1同名非事务等。12个真实事务拥有者索引与2356个测试适配代码位置保留；后者是文件/行索引，不构造2356个生产来源。四保护引用TASK02真实RED/GREEN及新归属pending；F132明确生产禁用但机制未修。索引不是204实际事务等价审计。
- Checks — 有效校验: 最终`task-03-validate-final-03`原版exit0；`task-03-bad-data-final-03`exit0表示8个坏副本全部拒绝：缺真实消息、缺旧命令、重复ID、遥测误归、断引用、缺边界/证据提前closed、证据文件缺失、过渡项closed却无去向。原版不被变异。工具验证完整ID同集/唯一、原行不改、引用、分类、pending及closed映射/文件证据存在；语义等价仍由各Task独立审查。中间initialize/validate记录原样保留，未把工具编译/输入错误当TDD RED。
- Checks — 边界/数量: [source-checks.json](../verification/evidence/task-03-inspection/source-checks.json)确认全部原位hash现值、产品与已提交制品diff0、精确白名单和尾空白检查；`git -c core.whitespace=cr-at-eol diff --check`通过，新五制品及Task无尾空白。新增0/删除0，数量账8365不变；没有业务全量/DFO/性能运行，符合本证据单元的检查范围。复现与工具限制见[inspection README](../verification/evidence/task-03-inspection/README.md)。
- Remaining: 159业务消息、204旧命令、73需入队来源全部pending，14遥测/12 OutsideGameplay仅保原排除；5保护与8过渡的新归属/删除证据pending。下一最小贯通候选ScriptVersion+GmBootstrap仅给原入口，未创建Task、未预置closed。各实际提交/rollback/参与者与共享帮助器闭合，T003骨干、最终T011清零及T012完整核验/同工具测量仍待后续。基线在本检查点后冻结，后续只维护开放账和适配当前导出器，不改写原历史。不推送/PR/合并/远端状态。


## Independent Result Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

Reviewer: fresh independent `b_task03_review`，目标Task契约r1、ResultR1、base `aa1e732b0af5cc0040521213422833d2a9b9a5ce` 未提交制品；采用 `reviewing-work` Task Unit/Code/Deliverable，独立只读并复跑validate/selftest，没有capture或业务测试。

未发现阻止提交的局部缺陷。五制品当前SHA符合最终清单、Server/测试/既有文件无差异。两capture payload逐字及hash相同；导出器只装配注册不构造容器/启动业务，377条双向配对、真实CLR与共享handler口径成立。来源/HTTP/事务owner/保护索引与所查源一致，Recover已指handler142，CardReward明确newTimer root。业务/旧命令/需入队来源和保护/临时项全pending，词法候选不冒充callgraph、Kind不冒充实际事务。validate通过且同校验器拒八坏data；F132禁用未修表述准确。N0D0数量8365、不宣称已迁移/全量/Spec验收。

主控消费：批准只释放本Task检查点。T-002保护和基线均已交付，下一有用单元进入T-003共享骨干及ScriptVersion/GmBootstrap真实首贯通；逐簇维护开放账，冻结baseline不重写。
