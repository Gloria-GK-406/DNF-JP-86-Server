# TASK-45 — 击杀与物件破坏归并

本 Task 承接 T-007，祭坛纯唤醒增量受 B7／P9 有界门约束，迁移 MonsterDeath（65575）及本切片五条、两塔两条旧命令，共八行台账。TASK-44 已由独立 Astra 批准并提交 `2dfb4bc9`／`cc212b99`，保留下来的塔命令仍按真实消费者逐项收口。目标未偏离；两塔击杀是本消息的实际分支，本次不提前关闭其它塔／离场／Timer来源或T-008。

## Task Unit Contract

- Revision: 3；Dependencies: TASK-44；承接RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 一个MonsterDeath完整协议CMD，薄handler只交解包输入和精确来源。删除`Game.MonsterDeath.ProcessMonsterDeathCommand`／`ApplyObjectDestructionCommand`／`GrantKillExperienceCommand`／`AdvanceHuntEnemyQuestCommand`／`ApplyMonsterKillCommand`及`Game.KillBossTowerActorCommand`／`KillIllusionTowerActorCommand`，先核实际消费者再闭行。
- Boundary: 普通怪物／APC／物件与两塔击杀的原事务、奖励、投影和失败保护；已有共用算法的真实去向及测试。无新Main／raw／Self／footprint许可，无共享执行器或关闭契约变更。一个sol6.1执行、一个fresh Astra快审；Result review required。

## Detailed Plan

1. **正式旧行为先绿。** 在`cc212b99`跑MonsterDeathTransactionTests、MonsterDeathHandlerTests、HuntEnemy／KillExperience／Object codec与实际两塔击杀测试，保全队SQLite、事务身份、怪物与奖励预留、重复上报、任务事件及封顶经验断言。仅补缺失的关键生产链oracle：真实decoder／dispatcher／事务／actual encoder／production sender，核ordinary全参与者、object原空保护及两塔差别；fine Dispose→whole Dispose、实际投影失败、精确physical替换与失败后不重发奖。已有领域／算法driver不冒生产scope证明。
2. **一条入站原路由。** 保Boss HasRun优先且物件报告直接忽略、Illusion仅非物件Kill及原stale短路、无普通run的EmptyDeath ACK。普通受众依原current party／registry／solo选择，过滤InWorld并按角色去重，报告者必须在内；所有当前角色经验／等级／点数、任务及帧字段仍取原真源，不回用选角副本。
3. **普通与物件保各自边界。** 普通一次ordinary Write，沿P8 DeclaredNonRequest声明实际全参与者owners，并保原外层Capture的全部精确会话／物理连接和显式本帧metadata保护；不要缩成仅报告者。原MonsterDeathDomainService内全队奖励、actor／participant claims、map-clear事件仍同事务；失败与重试不可重复支付。提交后保creature room EXP→clear Evaluate→EXP/death/auto-killed/quest/available/tournament→clear projection→Altar wake顺序与原失败后果。物件原plain命令仍一次ordinary Write、空selected／无footprint，保完整精确recipient集合、run/version/account验证、claim、failIfFull授予和holdings进度原子性；不得套用普通kill全员围栏。
4. **两塔保原事实与保护。** 各一次ordinary Write直接调用原Kill领域方法，owners按原run实际成员声明；Boss原全成员selected保护及报告者physical捕获，Illusion原selected=[]，owners不等于围栏集合。保RewardingMutation提交后发布、实际击杀／结算回值、帧顺序、逐成员投影失败隔离与当前性门；默认失败差异必须由本次旧新正式测试确认后列VC，不为保raw扩大许可。需要局部physical cleanup时沿TASK-43／44现有contexts准入，不关闭replacement或失去contexts的旧peer，不改共享关闭器。
5. **只收真实闭包。** 新CMD不注入旧Delivery／Coordinator、不嵌套bus；共用编排由CMD拥有，旧真实消费者用同算法窄适配。QuestFrames仍被Altar调用，只保共享纯投影；DungeonClearProjection仍有其它消费者，保同算法及其读取前后current门。Registry的旧killResult存储／两个方法只有实际零消费者证据才删，避免为无用Protocol DTO增抽象接口；原registry实例不换。三个无生产调用的fine仍需迁移有价值正式测试到真实领域／新路由后删除，不删断言求绿。新共用代码按endpoint上提，命令归本slice。
6. **最终核验与交付。** 产品／测试冻结后Release solution build；聚焦及Application／Protocol／Networking完整，实际Host／DI或用例受影响则Host完整。正式dotnet首次require_escalated，原类并行／项目串行／5m／detailed／各自DFO/TRX。对Task44最终r4逐display/status/重数和原两Dimension／skip核差集；保原DFO expected与control。核八目标之外／original／行序、strict／八坏例／实际runtime唯一绑定与七fine闭包、全部新旧文件diff-check。Result一页写交付、非常规选择、命令／数量／失败身份、限制；有独立旧绿的纯准备分段，无可分产品迁移说明理由。执行完成停写交fresh快审，主控消费后本地提交，继续T-007；无外部操作或提前封存。

## 主控边界裁定：B7／P9待独立审查

Altar尾部wake不移到薄handler返回之后，也不作为可执行callback输入。新增具名只含Wake的窄接口，复用原signal状态及其原调用位置，R1独立审查确认Spec RQ-009的能力类别遗漏，主控接受并先修订B7的具名范围与验收，再审Plan P9 R2的跨簇共享HOW；根／子／中心不变。其机制增量等待同Task唯一Astra的B7及P9批准与主控规划检查点，其它旧测试及独立产品归并可继续。最终八项目fresh full替代本Task原相关项目最低要求。Tournament纯帧按原slots／clock算法共用；LabelProbe是原不可变启动诊断输入；后续实际注册判据确认薄handler仅准bus／logger，故由本slice decoder读取同启动诊断值作为非wire测量字段显式交入，薄handler仅转交，whole不新增诊断配置DI豁免。实际decoder true／false经whole及EXP字节的测试须保原默认与诊断位置。


## Controller Consumption：B7／P9 规划门

主控已读取独立 R1 与 R2：接受 R1 对封闭能力类别的 F1，未把 Plan 豁免当作 Spec 授权；定点修订后，唯一 Astra 分别批准 B7 ready R1 与 P9 ready R2／Task revision 3。仅增加已具名的祭坛纯唤醒能力，精确接口复用原 singleton，原作业内成功投影后的调用位置及原生产者保持；根／子／中心、其它能力与来源行不变。审批只准本增量实施，不代表产品、Task Result或Timer闭合。

本次将 Spec／Plan／Task／审查／routing 五份文档独立本地提交后放行接口和精确判据；最终八项目 fresh full、旧新时点及错误路径、同实例／合并／取消／deadline与正反例由执行者交付，再由同一独立 reviewer 审完整结果。其它产品正在执行且不纳入此规划提交；无原始证据入库，无外部操作。


## Result

- Outcome: done（首次返工R2；产品freeze r2，最终App r3＋其余七项r2；未提交，待同Astra R2复核和主控接受）。
- Changed: MonsterDeath唯一whole／薄bus handler，普通／物件／两塔实际分支各一次ordinary；删除七fine与无消费者killResult store，保原全队SQLite、claims、current门和奖励／投影顺序。Quest／clear／tournament共用原算法，B7/P9精确wake接口别名同原signal；只闭八台账行。
- Why: 普通reward私有helper原丢session，现只原样传已拥有exact session，守第二次DirectQuestItem Demand；不扩footprint许可。诊断值经本slice decoder传非wire字段，handler／whole无新配置DI豁免。VC-B17列whole生命周期默认失败／已有帧差别，VC-B18仅Kill精确physical cleanup保contexts准入。归并与whole默认失败不可分；纯准备两oracle文件＋physical测试probe可分，最终旧cc212b99确25/25。
- Checks: 最终旧25/25＝21生产链＋1信号机制＋3原SQLite；新同25＋原public-flow2为27/27。产品r2 Release build零警告错误；最终App r3＋其余七项r2共9339＝9319pass＋2原Dimension＋18原skip。App5555＝5537＋2＋16，Protocol1781／Net203／Host203／Persistence484／HttpApi57／Pvf80全过，Infra976＝974＋2skip。逐display／status／重数App N26/D0（22 Task45＋4判据；原两排队理论名称及跨TResult强保障恢复），其它N0/D0；DFO74＝67原反例＋7control，outside/exe/orphan0。实际runtime一route／一Direct单binding／七fine0，八行之外及original／行序保持；strict／八坏例与含新文件diff-check通过。命令、轮次及失败身份见[核验摘要](../verification/task45-verification.md)。
- R1修复: 只改CommonExperienceEffectTests，第二条以测试专用已注册Transactional命令和不同QueuedKillResult走真实bus、原奖励领域及合法LegacyFootprint；保Prepare两结局、当前等级／资产／事务和Currentnull。Unknown probe仅第二TResult，围栏前后均未admit／无后续计价；聚焦3/3，App r3完整无新增失败。产品／其它程序集／pure-prep未改，旧25/25与七项目r2仍适用。
- Remaining: 原两Dimension仍7100/maze0/(1,0)/Left缺compatible map。r1新增public-flow旧fixture形状故障已修；Net两Connect refused同轮有Bind48，实例对应未记录，r2原条件203全过，不归已登记baseline。受控physical／信号不等于A21实机；T-007／T-008／Timer／Spec B未闭合。未动index、提交、推送、PR、tag、部署或真实游戏；已完成核验记录并停止全部写入，交独立审查。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: changes_required
```

审查执行 R1／最终 freeze r2，Task revision 3、B7／P9及本次 VC-B17／18；产品旧基线 `cc212b99`，规划 HEAD `c60fc996` 加当前未提交的 Task-45 产品／测试／台账差异。适用 `reviewing-work` 的 Task Unit／Code 标准；只评本 Task 的明显缺陷与契约，不重审整个 Spec，不以此前 Plan approved 代替结果判断。

**F1：CommonExperience 的跨返回类型排队保护被同类型替代。** `CommonExperienceEffectTests.cs:113–149` 原 `Different_result_commands...` 两个理论从 `ClearExperienceResult` 后排入 `ApplyMonsterKillResult`，现变为两个 `ClearExperienceResult`；`:164–192` 的 queued fence 用例也同步改成同类型。等待 Prepare、真实提交／回滚后定价、余额和事务断言仍在，但不再能检出“不同 TResult 的 CMD 未共享执行边界／保护”的回归；单 Kill 的领域 driver 不经过第二条 bus 入队，新增顺序执行的生产 oracle 也未补该维度。这不满足 RQ-014 对 A 顺序判据不得削弱的要求，不能只视为两理论更名。最小修正仅在测试：第二条使用另一返回类型的实际已注册 CMD，经真实 bus 排队、调用真实领域并读当前进度（可测试专用注册，不能直接 domain 调用代替入队），保原两种 Prepare 结局及不确定围栏前第二条不得定价／支付的断言。无需恢复已删除 fine 或改产品、Spec／Plan；更新 Result 和最终核验证据后复核此处。

其余本次已查范围未发现阻断：whole 的普通／object／两塔各保原工作单元与区别保护集合；窄 Wake 同 singleton、原受保护投影后位置、类型正反例与 signal 等待测试匹配 B7/P9。DirectQuestItem helper 仅传递已有 exact session；诊断值来自 decoder 启动配置且不改 wire。旧新 21 生产链＋1 信号测试及 3 SQLite 用例分层清楚；VC-B17／18 有真实 scope、encoder、物理替换／transport、Completion、关闭链和重试不再支付断言，共用 clear／quest／tournament 算法及 current 门未见改变。

独立读取八个最终 TRX 得 9339＝9319 pass＋2 原 Dimension（7100／maze0／(1,0)／Left）＋18 skip；逐 display／status／重数核得 App N28/D2，其余七项 N0/D0；旧最终25/25、新27/27，run.json及 Release build零警告错误吻合。已读 DFO summaries、runtime闭包、strict及八坏例，独立比较台账确实仅八目标变更、original／行序保持；纯准备补丁两新增 oracle 与当前文件一致。r1 Net失败缺精确实例对应，按报告保留取证限制，不追认为已登记基线。以上均是现有证据审阅，本 reviewer 未运行测试或改产品。

只追加本节并停写；此结论不关闭 T-007／T-008／Timer／Spec B，也不授权提交或外部操作。R1 规划审查和 R2 规划批准保留原记录。

## Independent Quick Review R2

```yaml
kind: rks-review
verdict: approved
```

**R1 F1 已解决；本 Task 结果快审通过。** 对象为 Task revision 3 的首次返工、HEAD `c60fc996` 上当前未提交结果；本轮按 `reviewing-work` Task Unit／Code 标准只复核 `CommonExperienceEffectTests.cs`、更新的 Result 与核验摘要，其他已审产品、旧新 oracle、接缝及台账范围沿用 R1，不扩大结论。

实际代码恢复 `ClearExperienceResult → QueuedKillResult` 两个不同 TResult 的真实 bus 排队。测试专用 `QueuedKillDomainCommand` 只在相关 fixture 注册为 Transactional／exact selected；其 handler 直接调用原 `IMonsterKillRewardsDomainService` 并使用 `context.LegacyFootprint`，不恢复产品 fine、不转发或嵌套 bus、也不绕过队列自开领域 driver。两条原名理论仍阻塞第一条 Prepare，验证第二条尚未计价；成功与回滚后分别读当前 level 2／1、累计 EXP 150／120，另核 Clear／Kill 两种计算 context、实际事务、SQL 余额及 Current=null。Unknown-fence 的 admission probe 只绑定第二 TResult，释放前和最终失败后均未进入，只有第一条计价／支付，原精确会话围栏及 physical 身份断言保持。已消除同类型替代导致的覆盖缩减，无新阻断项。

独立解析新聚焦 TRX 为3/3；`task45-final-r3/Application` 为5555＝5537 pass＋2 exact Dimension＋16 skip，失败仍为7100／maze0／(1,0)／Left。按 display／status／重数计算，对Task44r4为N26/D0，对App r2只恢复两个理论名；run.json 的真实命令、DFO、期限、HEAD和exit与摘要一致，DFO62＝61反例＋1control，outside／exe／orphan均0，diff-check通过。

最终证据明确采用 **App r3＋其余七项目 r2**，合计9339＝9319 pass＋2原失败＋18 skip；本次测试局部修复未要求重跑无受影响的七项目。R1已审的产品Release build、旧pure-prep25/25、新oracle27/27、runtime闭包及八目标台账证据继续适用，不把各轮冒称同一轮。批准只覆盖TASK-45局部结果，T-007／T-008／Timer／Spec B仍未闭合；r1 Net实例对应及A21实机限制仍保留。此次只读取证据并追加本节，未运行测试、修改产品或提交；停写交主控消费。

## Controller Consumption R2

主控接受唯一 Astra 的结果 R2 approved；R1指出跨返回类型测试缩减成立，首次返工仅修测试，已恢复真实 bus 两种 TResult、当前定价及围栏前禁止后续支付。主控独立读取实际修正与 r3 TRX：Application 5555＝5537通过＋原两Dimension失败＋16原跳过，对Task44r4新增26、无删除；原DFO parser核62条、原反例计数和单control均匹配，outside／exe／orphan为0。其余七项目r2、旧纯准备25/25、最终新oracle27/27、runtime唯一绑定及八行台账差集的此前核验继续有效；更新Result后重跑strict及八坏例均通过。

按用户流程将三文件纯测试准备独立提交，再提交产品归并、测试适配及本Task记录。VC-B17／18、同实例Wake、原不同保护集合与取证限制已纳入接受边界；不关闭T-007／T-008／Timer／Spec B，不做外部操作。下一Task继续GetItem及其实际两塔拾取分支。
