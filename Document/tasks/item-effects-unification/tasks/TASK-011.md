# TASK-011 — 同库组合验证、遗漏检查与最终交付整合

## Task Unit Contract

- Revision: 1
- Outcome: 补齐实际同库组合证据，动态注册无漏项，完整纳入范围的活跃写入路径可追溯；新鲜全方案回归、实现说明及人工验收清单齐备。Plan T-006，覆盖全部AC与SC-1—8的最终整合。
- Dependencies: 001—010已接受，010独立approved且正式停笔。真实effects、事务保护、当前状态重放及统一请求账本实际可用。
- Owned: 四组实际SQLite组合测试、动态注册检查、Host测试绝对临时库、最终遗漏审查与coverage、实现说明、human-test-list、全方案验证和本单位Result。
- Excluded: 不改变概率费用、不增加尚未实现道具；普通no-refund技能学习等既定豁免保持；不修改用户数据库／服务／客户端／PVF，不部署／push／PR／merge／清理／中间提交。

## Detailed Plan

1. 阅读verification/final-integration-findings.md及004／009／010实际源与证据。所有源定位先rg --files或rg -l再按实际路径读取；没有CodeGraph目录，不运行索引。唯一production/tests/dotnet writer，独立actor只读，全部dotnet串行。
2. Feed以同一临时绝对SQLite路径及scope的实际SqliteItemDomainStore/PersistentItemContainer与pet实例验证：物品24 CreatureConsumable local0/wire189数量2，UID101饥饿50，成功源1／饥饿80；实际facet flush之后late Prepare失败保持2／50且无运行投影，再重试成功。不得用fake源数量冒充同库证据。
3. Coupon使用实际LearningFixture／quest与Main consumable local4/wire69的真实物品源，验证物品扣除与quest rows同库提交、latefailure全回滚、重试；不再以bonus_sp替代源或剔除Consume作为组合证据。
4. Water物品3 Main wire69＋mixed skills同库：late INSERT触发器失败水与全部技能保持，正常重试一致完成；保留有意义原unit tests，不扩大普通no-refund学习范围。
5. Rename使用实际SqliteCreatureDomainStore／PetInstance和物品25 CreatureConsumable local0/wire189，同库验证源／名称成功、latefailure／retry；UID、template、growth均保留。不能以FakeCreatureStore代表真实同库。
6. 枚举整个Abstractions assembly的非abstract DomainEffect，与实际DI IEffectHandler.EffectType精确集合比较，检查重复及解析。不能硬编码当前31或仅扫Effects目录；保留18Request／20wire／20main及3XP＋ReadReplay的精确注册检查。
7. CarriedGoldCompositionTests两provider改唯一临时绝对Database:Path，仅释放／清理自身库；不启动Host服务、不使用默认runtime Data/dfo-server.db。
8. 最终活跃caller扫描纳入所有新增未跟踪source，coverage列入口→规则→executor→实际底层写入；审查费用、奖励、状态及旧writer关闭。明确底层effect执行、生命周期、范围外；不把定义当旁路。普通no-refund Learn、questAccept/drop、shop/move/combat、tutorial/recovery及petclock/hatch/growth具名豁免；固定等级券未实现仍范围外。发现范围内缺口细致修正并对症回归，不隐性扩大范围。
9. 完善实现说明，给当前真实API的典型规则数组／领域执行／instance基础变化／提交后投影例子。创建human-test-list.md：顺序列普通使用／宠物／装备／设计图开包专用品／任务经验及重登。每项准备、操作、期望、证据状态明确；使用→返回选角→重进→连续使用保留seq-reset Unknown抓取，不发明策略。全部实机HumanPending，未部署不写已验收。
10. 新鲜complete solution build及全部8 test csproj串行执行，既有PVF_ARCHIVE_PATH。使用临时runsettings关闭assembly/collection parallel、MaxParallelThreads=1，diag／执行证据确认生效，避免ClearAllPools跨collection干扰；不做生产pool扩展修复。所有DB临时绝对路径，socket隔离。记录完整命令、TRX Counters、实际skip及原因，保留每次失败，不把未经证明失败称baseline；若新问题修正则重跑必要项目及最终build。
11. Result/checks/coverage包含四组合实际触发失败点、动态集合、全方案实际数、范围外与HumanPending，git diff --check及新增未跟踪文件复读。代码／测试／文档／dotnet全部结束后明确正式停笔；独立阶段审计与根接受后再进入父任务独立drift、fresh challenges、final review和verification，最后一个完整本地结果commit。

## Readiness / Attention Check

010真实接口与独立approved已确认；只读最终准备指出四个可补的实际fixture缺口，无新范围内旁路。已有fatigue／quest／XP同库证据不重复造镜像测试。001—010成果及全部父验收条件保持连续，禁止以局部通过提前结束父任务。

## Result

```yaml
kind: rks-task-result
decision: pass
controller_acceptance: accepted
```

11步骤实施与单位内完整检查完成，等待根及独立阶段接受。实际同库四组使用同一临时绝对DatabasePath／scope的真实item/creature/quest/skill stores和PersistentItemContainer；没有剔除源Consume或以bonus标量／FakeCreatureStore替代。Food24 Pet189源2／UID101 stomach50成功1／80；Rename25同槽与实际name成功；两者latePrepare在原SqliteDatabaseParticipant实际facet flush后读到新值才故障，rollback与retry证明源和目标同生共退、growth保留。Coupon实际Main69源与quest全sections在clear flag晚INSERT trigger回滚/retry；Water3与mixed skills在skill3晚INSERT trigger完整回滚/retry。八组合＋whole-Abstractions动态effect DI精确集合一例9/0/0，纳入完整Application。

Host两个composition provider改唯一绝对tempDB，仅释放/清理自身pool/DB，无用户服务启动。最终活跃caller扫描包括untracked源；coverage当前总表更新，并明确执行器底层和普通no-refund／技能栏build／accept-drop／selection／shop-mail-combat／tutorial-recovery／petclock-hatch-growth等豁免。旧writer/六guard/Lottery-Vending cache扫描零匹配；新规则能力不扩展。implementation.md含真实典型API；human-test-list.md顺序24项全部HumanPending，sequence-reset取证仍Unknown，不发明策略。

fresh完整solution build0警告0错误；8完整test projects串行：Application2599、Protocol1226、Networking86、Persistence392、Infrastructure473＋2skip、HttpApi50、Host26、Pvf60，合计4912passed／0failed／2skip（4914 total）。两skip是现可读PVF时missing_archive负例，精确理由见checks。runsettings effective testhost payload、actual xUnit collections-off与所有TRX区间peak1证明隔离生效。各build、TRX/stdout/diag完整保留，git diff --check clean。首9专项5/4与两次编译失败及修正原因保留，不称baseline或行为RED。

同库新组合是actual domain＋scope，不冒称network/CommandBus；008—010相关实际bus/SQL/TCP/physical close证据保持分层。未部署、未修改用户数据库/PVF/client、无中间commit，不标ClientVerified；所有源码/新增测试/说明/coverage及正式evidence复读。详细命令、失败点和边界见[checks](../verification/TASK-011-checks.md)与[runner evidence](../verification/TASK-011-runner-evidence.md)。

本Result、checks和coverage全部写完后进入正式停止点，production/tests/docs/dotnet不再改动或运行，交由根及独立只读阶段接受。这里只结束最后implementation unit；父任务独立累计漂移／fresh挑战／final review／verification及最后一个完整本地结果commit仍须由根完成，不能据此提前结束父任务。

### 控制器接受

2026-10-01：实现者正式停笔，独立[TASK-011-stage-audit](../reviews/TASK-011-stage-audit.md) approved。根复读实际同库新测试／LearningFixture／捕获SQLparticipant、动态DI／Host临时库、实现说明与24项人工清单、coverage当前表／活跃caller分类和正式checks；独立解析8份TRX确认4912通过、0失败、2条件跳过，最终build零警告错误，diffcheck clean。接受011；所有11个单位完成，进入父任务独立累积漂移检查，后续挑战／最终审查／verification及一个完整提交仍未省略。
