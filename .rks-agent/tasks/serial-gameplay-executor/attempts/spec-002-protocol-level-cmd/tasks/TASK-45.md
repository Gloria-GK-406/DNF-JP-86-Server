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
