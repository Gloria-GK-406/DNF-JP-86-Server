# TASK-011 独立阶段检查

```yaml
kind: rks-review
verdict: approved
```

- 对象：TASK-011 Revision 1、实现者正式停止 production/tests/docs/dotnet 后的当前工作树，包含新未跟踪测试、最终 Result/checks/coverage/实现说明/人工清单及全部正式证据。
- 范围：最后实施单位的四组同库补证、动态注册、Host临时库、活跃caller分类、完整回归及交付整合。采用 reviewing-work 的 Task Unit 与 Code criteria；不是父任务累计漂移、最终挑战或最终审查。
- 检查方式：只读实际源码、fixture/API、调用者上下文、已有TRX/stdout/diag及最终构建记录。未运行dotnet、修改生产或测试、提交或部署；仅写本文件。无遗留本单位阻断项。

## 四组实际同库证据

已读取新增 RealConsumableIntegrationTests 全文、LearningFixture 与实际数据库/事务API。fixture 使用实际 Infrastructure/Persistence/Application modules、唯一绝对临时Database:Path、同一DomainTransactionScope、SqliteItemDomainStore/PersistentItemContainer、SqliteCreatureDomainStore及quest/skill表；静态content/clock/current-view为具名确定性替身。没有剔除Consume、以bonus_sp/tp冒充源，或用FakeCreatureStore冒充本组合落库。

1. 饲料24：CreatureConsumable local0/Pet wire189实物源2，UID101/template63001/stomach50。成功源1、stomach80；运行projection在提交前为空。晚Prepare通过提交前捕获的**原SqliteDatabaseParticipant**读取同一pending SQL，确认源1与stomach80已经flush，再抛IOException；回滚源2/stomach50、无projection，重试源1/80，UID及成长字段保留。
2. 改名卡25：同实际宠物、源槽与真实PetInstance。晚Prepare同样确认原participant中源1/name82A0已写后失败；回滚源2/name80，重试源1/name82A0，stomach、UID、template、experience/level/mode/tail保留。
3. Coupon：实际Main69物品10_006_007源2。clear-flag晚INSERT触发器失败后源、carried、重复tracked、packed依赖进度与cleared均恢复；DROP trigger后源1、完成101、tracked/依赖正确更新，reserved bits保留，没有SP/经验奖励。
4. Water：实际水3 Main69与mixed退款/更新/新增skills。skill3晚INSERT触发器在前面技能变化后故障，实物源与目标技能保持；重试源1、决定值0/3/2，其他page/character及bonus_tp保留。原有unit替身测试未删除或冒充本组合。

首专项5通过/4失败保留。初版晚Prepare使用ambient SqliteDatabase.Read时，DomainTransaction.CommitAsync已置IsSettled，scope.Current为null，另开连接只能读旧视图；它不能作为flush成功证据。最终捕获原participant，PreparedScalar直接CommandAsync绑定既有pending transaction；其存在由Enlist factory throw反证，未新造SQL参与者。Coupon初版低9bit预期也已按实际位模式修正。第二专项9/0/0及完整Application包含同一源码。两次漏namespace构建失败是编译诊断，不称行为RED或baseline。

这些是实际domain＋scope/SQL组合；network/CommandBus/precise transport边界由008—010独立证据承担，不混称同一测试。

## 注册、临时库与活跃路径

动态检查枚举整个Abstractions assembly所有非abstract DomainEffect，与actual DI实例化后的IEffectHandler.EffectType精确集合比较，逐type只有一个handler，并解析shared executor；包含Inventory命名空间普通效果，未硬编码31或仅扫Effects目录。18Request/20wire/20main及3XP/ReadReplay的既有精确注册测试仍纳入完整Application。

Host两个CarriedGoldComposition provider明确Database:Path为Guid唯一绝对临时文件；provider先dispose，再只ClearPool自身connection和删除自身库。composition用例未启动Host或访问默认runtime DB；原Host测试的隔离子进程与listener不被说成用户服务。

独立搜索Server/src实际Consume/TrySpendGold/SetProgress/Grow/Rename/AddStomach及skills调用，含新增未跟踪源；与caller-scan及coverage分类一致。六设备、Recipe、Opening/Lottery/Vending和专用品/QuestCompletion的业务变化经完整rules→Apply，基本写入在具名handler/instance/container。共用ApplyAwardAsync实际生成CharacterExperienceEffect，不按方法名误判旧Award旁路。六guard和Lottery/Vending缓存扫描为零。

no-refund豁免未扩大：LearnAsync在任何table写入前，以任一Refund!=0将**整批**转LearnRefundAsync/effects；mixed update/insert全部随退款迁移。剩余direct SetLevel/Insert只all Refund0普通学习，旧循环Remove退款条件不可达。SwapSlots/Switch/build选择、selection维护、quest accept/drop/trigger、mail/shop/sale/repair/discard/move、combat材料耐久、card/ground、tutorial/recovery和petclock/hatch/growth具名范围外。基础RefundAsync/RecordUseAsync定义无生产caller；不把定义或Rename领域入口当底层writer。

coverage当前总表已修正Water/Coupon证据分区与XP008保护状态；历史段落明确保留历史，未当当前pending。implementation的规则数组、Apply、装备基础setter、pet基础变化、XP决定及提交投影与实际API一致。human-test-list顺序24项含准备/操作/期望，全部HumanPending；未部署、未发资产或替换客户端/PVF，H23保留同连接Return→Select→连续使用的sequence-reset Unknown取证，固定等级券等未实现能力不列成功期望。

## 正式完整验证

停笔后重新读取全部8个TRX及最终build，按实际UnitTestResult outcome复核：

| 完整项目 | passed | failed | skipped |
|---|---:|---:|---:|
| Application | 2599 | 0 | 0 |
| Protocol | 1226 | 0 | 0 |
| Networking | 86 | 0 | 0 |
| Persistence | 392 | 0 | 0 |
| Infrastructure | 473 | 0 | 2 |
| HttpApi | 50 | 0 | 0 |
| Host | 26 | 0 | 0 |
| Pvf | 60 | 0 | 0 |

合计4912通过、0失败、2跳过（total4914）；四组专项9与完整Application重叠，不累加。Infrastructure两条missing_archive负例因当前PVF可达而Skip，已沿测试Skip.If与stdout核对；Counters.notExecuted=0不能覆盖实际两条NotExecuted。最终solution build0警告/0错误，git diff --check无输出。

runsettings值在实际testhost payload可见；8份stdout均明确Starting collections=off。独立重新解析8个TRX非零长度start/end区间，结束同timestamp先于开始，全部peak1，与runner-evidence一致。此证据说明本次测试执行隔离生效，未推广为所有业务内部异步流程没有并发，也未扩为生产pool修复。实际8项目串行、无filter；原失败、TRX/stdout/diag均保留。

## 验收界限与交接

本单位达到自己的最终整合目标；只证明CurrentImplementation与自动验证。seq-reset、无nonce古老raw碰撞、TouchSocket实际回调调度证据界限及I/O可部分投影沿010保留；同ID新context需distinct exact Session、postdurable Probe不是实际socketclose等没有被最终说明扩大。

没有部署、用户数据库/PVF/client变更或中间commit。本批准不替代父drift、fresh challenges、final review、verification及最后完整本地结果commit；不标ClientVerified或宣布人工验收完成。阶段审查现停止，交由root控制后继父流程。
