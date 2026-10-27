# TASK-11 诊断与证据限制

各 task-11 原 log/meta 保留；逐条命令、实际 exit、elapsed、编译错误和失败身份另见 diagnostic-run-inventory.json。编译/驱动错误不计行为 RED；正式轮不拼接。

- red-01 缺测试 using/接口类型、compile-01 缺 IProtocolCommandMessageHandler using、compile-02 旧 fine 实际 caller 尚未迁完、compile-03 测试不可访问 internal GameCommandOutbox、focused-01 ServiceCollection.Add 驱动、diagnostic-app-01 ValueTask/Task 驱动、r4-focused-01 GameplayCallChain 无 Id：都是可避免的编译闭包诊断。没有伪称这些是测试保护红。
- red-02 编译成功后明确打到当时尚未启用的 Effect/Main 初始行为。它只能证明启用前现状，不能单独证明唯一 Main/存储/出处保护。
- focused-02 实际78=52pass/26fail；focused-03 为78=55pass/23fail：旧 Use fake/direct handler 尚在 TestGameplay 内 nested；SameID fixture 的 OnlineStateRegistry 与 RecordedOutbound table 不一致。修为同provider真实 bus/ledger/Main/outbox，协议入口在测试作业外；setup/观察在明确job。保原物理对象两张表一致，exact-target守卫不放宽。focused-04 为92/92。
- focused-05/06 各55=54pass/1fail：新无selected ACK后重新选中驱动，05真实失败为Duplicate container or default for Main，06为Selected connection generation is no longer current；修为复用原已注册container并按ReleaseExact后的真实新generation重新发布，非生产规则/队列/期限变化。focused-07/08通过。
- diagnostic-app-02 为4784=4762pass/6fail/16skip：2准确原Dimension、四个本Task接线/判据迁移问题（新Admit/Finalize写表分类；旧Main disabled判据；Freeze错误克隆原注册对象；effect作业label仍预期inbound）。按Task r3精确修复，保原Freeze38个Assert.Same。
- 原Second_primary的首次nested wholeCMD迁移会先触旧executor ProgramError/SelfWait，不再达到原第二Main目标；不能据它改原机制期望。按r3改为同真实CMD内两个Main，真实SQL一次/第二action0/同entry和projection/Committed/sticky默认围栏。Later_readonly原注册实为Transactional，现同CMD Main后ordinaryWrite directReader，保原slot69/-1/0x000E oracle。Refusal_only保旧SharedEffect兼容证据，新Rejected另证。
- 新Rejected后选中测试曾仅因ReleaseExact已推进generation而在EnterWorld提前失败；修为新generation真实完成选中后，明确达到Rejected Main拒绝，不能将旧弱oracle算保护证明。
- mutation-red-01/02：可编译地移除 actual encoder provenance 条件，让mere attempt错获raw。真实SQL count2/entry Committed后fence断言RED；finally恢复原bytes/SHA，green各1/1。final02 mutation绑定最终phase源码，见mutation-result-final.json。
- formal01：完整build0/0及八full自然完成。误在替换Later方法时以next Fact切片覆盖了紧邻Window Theory两个原case，导致8546=8526pass+2原fail+18skip。原block从before逐字恢复（SHA见test-source-audit），不计N/D。此轮不可用于最终接受。
- r4-focused-02及后续真实token负例：无伪造token/private反射；未完SQLiteunit/rollback cleanup时拒签且encoder0；实际rollback失败仍默认typed/fence。已签token期间结构上不能并存未完unit，签发后也不得开新unit；scopeDispose独立阶段确实可达。
- scope-propagation-red-01：closed guard C由actual adapter记录、scope catch传播再次计数，单因Ref断言RED。snapshot首修green66，但未覆盖已存在body E组合，故只保留诊断。
- formal02：完整八full自然完成，8553=8532pass+3fail+18skip。两个原Dimension以外，Networking原ChannelControlMessagesIntegrationTests.Protocol_business_failure_is_downgraded_but_a_refused_job_is_still_an_error(refuseJob: True)在145行ConnectAsync发生Connection refused；之前已await host.StartAsync，refuseJob StopAsync在连接之后，尚未进入业务/拒队断言。FailureLogs为该test本地捕获，交错Console无法证明是哪一个host；GetAvailablePort释放端口是现源码行为，但根因未证。未改非owned harness/并行/timeout，未新增baseline。原方法精确定向diagnostic-01两case2/2通过只说明未复现，不称已修。formal02之后产品因下项修复，亦非最终轮。
- scope-combination-red-01：真实E+C及E+Aggregate(C,D)分别误计3/4，真实存储/Committed/encoder/fence先成立。red-02含Dispose吞closed guard，4例1pass/3fail。最终每request显式scope cleanup phase只在该阶段识别传播，保留原wrapper/aggregate和独立D，先前body同E在dispose再次抛仍独立两因；吞guard也sticky失败。green-01共93/93，含全部核心旧workunit/request/default保护与新矩阵。没有ambient/global tracker。

最终核验仅采用freeze03后的完整build03和八full03。尚未形成Spec/最终≤2秒性能/A21实机验收/其它协议迁移接受结论；暂留LegacyReplay、LegacyFootprint和Lifecycle适配仍按原开放账pending。任何最终新失败均须按真实实例继续诊断，不借本清单增加允许基线。

最终数量工具初稿用dict按display name去重，Infra原976行中仅974唯一显示身份，故工具断言失败（非native/test失败）。修为Counter按status/name保重复次数，逐原case multiplicity比较，不删除重复显示case或改native数量。
