---
kind: rks-verification
decision: pass
---

# TASK-52 核验摘要

本结论只核 TASK-52 四行迁移的执行结果；基线530c2a5f，分支task/refactor-session-20261021。等待主控唯一 fresh Astra 快审及提交，不代表T-007或Spec完成。

## 交付边界

唯一 `GameQuestClearCouponCommand`（Protocol／Direct）吸收旧0x0204三分支全部编排，薄handler只交原消息与精确dispatch。原完整19B／slot／container／reserved／Action门、事务外实际物品分类、Entry优先于Action58疲劳／77任务券保持。唯一被选Main保同request Recipient／physical、unit.Footprint；原最低仍Inventory|ItemUsage及Action58的Fatigue，动态Heras／quests足迹不改。普通动作与native Commit健康确认回滚仍拒绝并允许原entry／Ordinal重试，取消和递归typed／独立失败不取得恢复。仅增加批准的既有EntryAllowance领域 `Supports` 查询，不直读规则／内联item ID／加转发领域服务／改白名单或机制。

三条投影链、券两个独立辅助catch与原日志保持。初次 `OpeningCompleted` 的Failed原先被忽略，三路actual encoder注错均继续忽略、无新增Close／Fence；不升级required。疲劳后原ReadOnly读取经 `ItemUsageSnapshotReader` 实质共享，原fine／Delivery继续供选角与日切调用；新独立单元ordinary／[]／None，InWorld→clock→snapshot、二次current及每帧current门、FullSnapshot预验证后reset／255chunks、排序ceil cooldown／255chunks／无reset保持。`ItemUsageProjectionTests`等仍专守投影算法。

## 先旧后新证据

新增 `CouponProtocolCommandFlowTests` 30项，真实注册decoder→dispatcher→所属领域／SQLite、实际DI Dispose、production outbox／actual encoder及受控physical sender；三路实际消费与状态、完整首帧序、普通action／deferred-FK native Commit回滚、同帧原entry／Ordinal资产一次、取消／递归typed、初次Project Failed、committed-replay raw及same-ID物理替换隔离均直接核验。三路scope及独立ItemUsage RO错误直接核Committed、cause、实际帧→Close、StandingOf.Fenced／Block与委托原Creature服务的DiscardExact；scope case未另捕获domain内entry引用／Ordinal，引用与Ordinal由成功、回滚重试及replay用例直接守护。旧scope为正常返回且实际发送拒绝ACK、无Close／Fence，新scope保成功投影后默认typed／精确保护；RO后失败原raw改默认typed／精确保护，仅Spec VC-B32。

主控最终旧源复验 `logs/task52-old-final-r1`：默认env App86＝85pass＋1原PVF skip、Protocol73pass，两个child exit0。固定HEAD／branch／空index、21个owned路径与完整CURRENT备份，reverse-check／finally逐文件恢复及全部bytes核验通过，报告 `evidence/task52-old-final-report.json`，恢复目录 `/private/tmp/task52-oldcheck-20261026/`。纯准备 `evidence/task52-pure-preparation.patch` 仅四个App测试路径，与这次旧实测完全相同；新结构注册／薄handler测试不混入纯准备。原fatigue／Heras拒绝强例改走实际decoder入口，其他规则、周期、cooldown、完整帧和真实consumable回滚断言保留。无网络LearningFixture／固定receipt测试仅证明算法与原领域事务，不冒admission／scope或真实物理关闭。logical回滚重试夹具未物理关闭；真正Close后的transport用例不再伪造其网络重发。

迁后聚焦 `logs/task52-new-focus-r6/Application`：86＝85pass＋1原skip；Protocol聚焦 `logs/task52-protocol-focus-r1/Protocol`：74pass。实际完整首帧类型：Entry 0204→000E→0227；Fatigue 0204→000E→0024→021E→021E→00AC；Coupon 0204→000E→0164→023F→0015，ACK完整body另有直接字节断言与原codec金样。

## 最终冻结验证

`logs/task52-final-r1/build` 的solution非增量Release／-m:1／--no-restore，child exit0，0警告／0错误。随后三个项目严格串行，均Release／5m／console detailed／独立DFO与TRX，原类并行及Skip不变；`--no-build --no-restore`消费该fresh build。App5708＝5690pass＋2原fail＋16skip（child exit1）；Protocol1787pass、Networking203pass（child exit0）。两fail精确为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`；原7100／maze0／cell(1,0)／Left缺图，Message与51逐字相同。`comparison.json`按display／outcome／重数：App N30D0，Protocol N2D1（旧unsupported-null依赖fixture替换为薄handler委托，新增唯一binding判据），Net N0D0；旧新聚焦身份均在full保留，唯前述诚实替换的旧Protocol fixture除外。`logs/task52-main-inventory.json`记录三个fresh项目；沿用TASK-51摘要的五个历史项目为：Host 203（task49-final-r2/Host）；Persistence 484（task48-final-r2/Persistence）；Infrastructure 976（task48-final-r2/Infrastructure）；HttpApi 57（task48-final-r2/HttpApi）；Pvf 80（task48-final-r2/Pvf）。三个fresh为5708＋1787＋203＝7698，五个历史为203＋484＋976＋57＋80＝1800，合计9498＝9478pass＋2fail＋18skip，不称八项目fresh。

原DFO parser逐actual assembly核验：App62／Protocol1／Net7，共70＝67反例＋3control，原各类expected不变，outside／exe／orphan0；命令与输出见 `dfo-check.json`及各项目`dfo-check.txt`。`evidence/task52/runtime-r2/runtime.actual.json`从实际已编译程序集／DI登记核唯一66052 binding、Direct／Protocol、仅CommittedReplayCurrentStateBatch／Self None与三fine类型／handler／注册0；provider实际解析由full的CommandRegistrationTests守护。runtime首轮仅探针nullable注释编译错误，修正探针后第二轮自然exit0，不改产品或测试。该登记探针未启动Host／游戏／数据库。原三个fine生产引用0，两个旧编排删净。

迁移账只改四目标行，所有original／顺序／其余行不变，见 `owned-ledger-check.json`；AGENTS仅表增一行、Plan不变。Result后strict／八坏例及含新增文件diff-check输出见 `evidence/task52/ledger/`与最终`diff-check.json`；所有runner结束与最终owned manifest另留本机。未做A21实机、自然故障、专门性能验收、Git暂存／提交／推送、PR、tag、部署、外部issue或清理。产品/tests保持冻结，执行者STOP。
