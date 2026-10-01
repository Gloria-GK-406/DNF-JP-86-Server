# TASK-011 最终整合检查

对应TASK-011 Revision1，基于001—010已接受的当前未提交实现。本轮源及自动检查证明CurrentImplementation，实机HumanPending；未部署或修改用户运行库／PVF／客户端。最终父审查、verification与一次完整本地commit由根后继控制，非本文件替代。

## 实际同库组合与新注册检查

新增 `RealConsumableIntegrationTests` 使用真实Infrastructure/Persistence/Application模块，LearningFixture唯一绝对临时Database:Path与同一DomainTransactionScope，实际SqliteItemDomainStore、PersistentItemContainer、SqliteCreatureDomainStore、quest/skill tables。输入规则和固定clock为确定性测试声明；不剔除Consume、不用bonus_sp/tp作为实物源、不用FakeCreatureStore替代pet落库。

- 饲料：24，CreatureConsumable local0／Pet wire189，源2；宠物UID101／template63001／stomach50。生产UseAsync规则→shared Apply→pet.AddStomach。success源1／stomach80；晚Prepare用提交前捕获的原SqliteDatabaseParticipant.CommandAsync确认两个facet已经SQL flush、运行projection仍空，再抛IOException；实际rollback源2／stomach50、无投影，retry1／80。UID/template/experience90/level1/mode/tail保留。
- 改名：25，同一CreatureConsumable local0／wire189源2＋真实PetInstance。晚Prepare原SQLparticipant确认源1＋name82A0已flush后IOException；rollback源2／旧name80，retry源1／name82A0，UID/template/growth/stomach50保留。
- Coupon：Main Consumable local4／wire69实际物品10_006_007源2。clear-flag晚INSERT触发器失败时实际source／carried rows／重复tracked holes／packed dependent progress／cleared rows全部恢复；DROP trigger后正常retry源1，完成101、tracked删除、dependent低9bit变2且reserved bits保留；没有SP/经验奖励。
- Water：实际水3 Main wire69源2＋退款/更新/新增mixed skill batch。skill3 lateINSERT触发器在前面skill改变后故障，源和全部技能保持，随后retry源1与三条技能决定值0/3/2；其他page及其他character不变，bonus_tp没有被冒充支付扣减。
- 四组success/failure+retry八例与动态注册一例共9，TASK-011-four-second.trx 9/0/0。它们是实际domain＋scope／同数据库组合，不冒称network/CommandBus；008—010另有真实bus／SQL／TCP安全证据。
- 动态注册枚举整个Abstractions assembly所有非abstract DomainEffect（含Inventory命名空间普通4类型），与真实DI IEffectHandler.EffectType精确集合相等，每type只有一个解析成功的handler，shared executor实际resolve。既有真实18Request／20wire／20main＋3XP／ReadReplay注册检查保留并进入完整Application。
- CarriedGoldCompositionTests两个provider采用唯一Guid绝对临时库，provider先dispose，随后只ClearPool自身connection与删除自身DB；composition用例不启动Host。完整Host测试中原有startup集成fixture仅启动其隔离测试子进程／listener与临时库，不是用户服务。

## 正式执行命令与隔离

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet build Server/86JP.Server.slnx --no-restore -v quiet
# 下列模板逐一替换项目名，8个项目串行；无filter。
dotnet test Server/tests/<Project>/<Project>.csproj --no-build --settings Document/tasks/item-effects-unification/verification/TASK-011.runsettings --logger 'trx;LogFileName=TASK-011-<short>.trx' --results-directory Document/tasks/item-effects-unification/verification --diag Document/tasks/item-effects-unification/verification/TASK-011-<short>.diag.txt -v normal
```

实际顺序：Application、Protocol、Networking、Persistence、Infrastructure、HttpApi、Host、Dfo.Pvf。shell保存原LASTEXITCODE，任一失败停止串行链，日志／TRX/diag均保留。fresh build证据TASK-011-build-full.txt及全部8项目结束后的TASK-011-build-final.txt，均0warning0error；没有parallel dotnet、shared DLL build race或生产SQLite pool变更。

临时runsettings明确MaxCpuCount1，ParallelizeAssembly=false／ParallelizeTestCollections=false／MaxParallelThreads1、DiagnosticMessages=true。effective diag发送给testhost的RunSettings包含这些值；xUnit actual Starting消息明确`parallel test collections = off`，同时TRX非零长度UnitTestResult区间最大同时执行1。不是只保存settings文件或仅凭参数推测；详见TASK-011-runner-evidence.md及各raw diag。关闭测试collection并行避免既有ClearAllPools跨fixture干扰，不扩为生产pool修复。

正式完整计数如下；实际skip根据UnitTestResult outcome，不以Counters.notExecuted单值误判。

| 完整项目 | total | passed | failed | skipped | TRX |
|---|---:|---:|---:|---:|---|
| application | 2599 | 2599 | 0 | 0 | TASK-011-application.trx |
| protocol | 1226 | 1226 | 0 | 0 | TASK-011-protocol.trx |
| networking | 86 | 86 | 0 | 0 | TASK-011-networking.trx |
| persistence | 392 | 392 | 0 | 0 | TASK-011-persistence.trx |
| infrastructure | 475 | 473 | 0 | 2 | TASK-011-infrastructure.trx |
| httpapi | 50 | 50 | 0 | 0 | TASK-011-httpapi.trx |
| host | 26 | 26 | 0 | 0 | TASK-011-host.trx |
| pvf | 60 | 60 | 0 | 0 | TASK-011-pvf.trx |

合计：4914 total，4912 passed／0 failed／2 skipped。基于每个UnitTestResult outcome；Infrastructure Counters.notExecuted=0不能拿来覆盖两条实际skip。

两个skip精确理由：

- `Dfo.Server.Infrastructure.Tests.Content.PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`：An archive is reachable from this working tree.
- `Dfo.Server.Infrastructure.Tests.Content.PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`：An archive is reachable from this working tree.

## 活跃caller与交付文件

`verification/TASK-011-caller-scan.txt`保留rg --files全源发现及基础写入／旧writer/cache扫描；包括新增untracked源。coverage当前总表已更新：旧replay pending／Vending缓存／四组同库pending被当前事实替换，历史阶段段落保留。范围内15个领域入口源文件检查无直接完整结算旁路；effects handlers／consumable基础CAS／instance／container是实际底层执行，分类见coverage末尾。

ordinary no-refund Learn只all Refund0普通学习；mixed refund在首写前完整分流effects，后续旧循环Remove退款分支不可达，不误当active。技能栏SwapSlots/Switch/build选择、selection reconciliation、quest accept/drop/trigger、mail／shop／sale／repair／discard／move、combat材料耐久、card／ground pickup、tutorial/recovery、pet clock/hatch/growth为具名范围外。fatigue RefundAsync及usage RecordUseAsync仅基础定义，无生产caller。固定等级券／升级宝珠等未新增，不纳入人工成功期望。

新增`implementation.md`给真实Build数组／ApplyAsync／instance setter／XP decision及提交投影API典型例子；`human-test-list.md`按24项顺序列准备／操作／期望／状态，全部HumanPending，包括use→returnselection→reenter→连续两次use的C2S sequence-reset Unknown取证。重传与故障无需用户伪造frame／破坏运行库。

新未跟踪RealConsumableIntegrationTests全文件、Host修改源、runsettings、implementation/human/coverage和实际TRX已复读。末次git diff --check clean。当前单位不改变业务概率、费用、能力或现有明细语义；没有运行库改动或中间commit。

## 开发失败保留与证明限制

- TASK-011-build-first/second.txt各2编译错误：新测试漏Data.Characters namespace；补正确using后第三/第四/fresh完整build0warning0error。这是编译诊断，不算行为RED。
- TASK-011-four-first.trx 5passed/4failed保留。晚Prepare最初用SqliteDatabase ambient接口，此时DomainTransaction已IsSettled，scope.Current投null而从独立connection读到旧source2，不能据此声称已经在新SQL上故障；改为提交前捕获实际原participant，Prepare直接CommandAsync确认flush。Coupon初始预期忘低9bit mask，按现有bitpattern修正。第二专项9/0且完整App包含相同代码；未将这些失败叫baseline或隐藏。
- 既有007—010真实RED与开发失败文件继续保留。Network frame/序号、SQL/DI成功与源扫描都是CurrentImplementation；不是本轮ClientVerified。未知seq reset／无nonce古老raw碰撞／TouchSocket回调调度证据界限、发送I/O可部分投影、postdurableProbe不等真实socketclose等沿010说明保留。
