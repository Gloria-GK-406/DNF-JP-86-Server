---
kind: rks-verification
decision: pass
---

# TASK-45 核验摘要

判定只覆盖本次MonsterDeath及七fine的执行与无新增回归，等待唯一Astra结果快审和主控接受，不代表T-007／T-008／Spec B完成。产品基线`cc212b99`，当前HEAD`c60fc996`只增加B7/P9规划门；本次产品与测试未提交。

## 旧新与实际边界

- 最终纯准备在精确旧产品／旧正式测试`cc212b99` **25/25**：`logs/task45-old-final-r9/Application`。其中**21例实际生产链、1例信号机制、3例原SQLite领域事务**。相同最终oracle新侧在`task45-oracle-final-r9/Application`加原实PVF public-flow两例共**27/27**；另一轮`task45-oracle-final-preold`的29/29含4例既有Illusion奖励投影，不能混作最终27例。
- 生产链是实际注册decoder→dispatcher→真实DomainTx→actual encoder→production outbox→受控physical sender。普通全队事务与allFence／Block／physical close、物件原空selected／无footprint、Boss原全员selected但仅报告者physical、Illusion空selected均正断言。全参与者owners与保护集合分别检查，没有增加Main／raw／Self或footprint许可。
- 普通先前序列、奖励／actor与participant预留、同SQLite事务身份和同SQL交易、全队回滚重试一次支付、map-clear事件及异步quest subscriber仍由原3个真实SQLite理论用例守护。原fine测试改调用真实领域或同whole算法driver，强业务／封顶／点数／帧序断言保留；这些driver不冒生产scope。实际普通DirectQuestItem授予守私有helper两次Demand：只传入原已有exact session，不查世界、不扩声明授权；旧合法null领域调用仍null。
- LabelProbe由本slice decoder读取同不可变启动singleton，作为非wire测量字段传入，薄handler只bus，whole不增诊断DI豁免；actual EXP总量与14槽true／false字节直接核对，不影响其它EXP编码入口。原普通EXP→death→自动击杀→任务→available／tournament→clear投影→Altar wake保持，纯QuestFrames直接消费既有MonsterKillEffects，Altar沿同纯helper；clear／tournament旧消费者仍用同算法窄适配。
- **VC-B17**：普通fine.Dispose原raw／无帧，新whole.Dispose在原事务Committed、EXP／death已登记后typed并保护全部精确physical；原ordinary发布失败的typed／全员保护保持。物件scope默认typed但仍不加围栏。Boss scope新已有各成员原奖励／库存／death，保护仍全员selected但仅reporterphysical；Illusion scope新已有death，无Fence／Block。两塔actual encoder仍逐成员收集／关闭后正常返回。原root链、失败后再次入站静默drop且Tx次数不增／资产不再支付直接断言，不能把blocked dispatcher说成必抛；普通secondary真实transport失败只关该physical、不Fence任何人，与编码故障分开。
- **VC-B18**：仅两塔Kill cleanup提前捕获physical，保原IsInWorld及现有contexts准入；query-only变化仍关原physical，contexts双替换则旧／新physical都drop／零关闭，新replacement零帧。旧同ID误关闭replacement由真实旧新oracle读出；共享closer／F-83／RQ-035不变。
- **B7/P9**：IAltarSchedulerWakeSignal只含Wake，原AltarWakeSignal同singleton别名，原body／try内成功投影后调用。旧新日志观察当前job、actual编码已完成再wake；scope异常前signal仍发生，先投影失败不wake。原Scheduler同实例、重复wake合并、取消、deadline及提前wake释放timer均有正式断言；判据只放精确接口，仿名接口、完整signal和旧Delivery仍拒绝。Timer来源不闭合。
- Registry旧killResult三个方法与store只有声明／census、无实际caller，均删除；不为死DTO扩抽象接缝。七fine产品类型／绑定删除，MonsterDeath一whole；其它Pickup／Die／Leave／Timer／Altar来源及T-007／T-008／Spec B仍未闭合。

## 最终正式检查

最终证据为**App `logs/task45-final-r3/Application`＋其余七项目`logs/task45-final-r2/<项目>`**。本次R1返工只改App单测试文件，产品／共享API／其它测试程序集未改，保r2产品Release solution build零警告错误及七项目full，不无因重跑：原类并行、项目串行`-m:1`、`--blame-hang-timeout 5m`、`console;verbosity=detailed`、独立DFO/TRX；runner在执行前写cwd／HEAD／branch／command／DFO／start，完成后写end及真实exit。正式命令形状`dotnet test Server/tests/<project>/<project>.csproj -c Release -m:1 --blame-hang-timeout 5m --logger "console;verbosity=detailed" --logger "trx;LogFileName=results.trx" --results-directory <each-project>`；完整实际argv见各run.json。

最终9339＝9319通过＋2原失败＋18原skip；最终App的TRX／run.json／nd.json在`task45-final-r3/Application`，其余七项仍在`task45-final-r2`；不称八项统一r2。

| 项目 | 最终结果 | 对指定既有库存逐display／status／重数 |
|---|---:|---:|
| Application（r3） | 5555＝5537通过＋2原失败＋16原skip，exit1 | Task44r4 N26／D0 |
| Protocol | 1781通过，exit0 | Task44r4 N0／D0 |
| Networking | 203通过，exit0 | Task44r4 N0／D0 |
| Host | 203通过，exit0 | Task44r4 N0／D0 |
| Persistence | 484通过，exit0 | Task39-final-r3 N0／D0 |
| Infrastructure | 976＝974通过＋2原skip，exit0 | Task39-final-r3 N0／D0 |
| HttpApi | 57通过，exit0 | Task39-final-r3 N0／D0 |
| Pvf | 80通过，exit0 | Task39-final-r3 N0／D0 |

仅失败仍为`DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`、`DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，同原7100／maze0／(1,0)／Left无compatible map。18个skip身份及重数全部保持。App r3对Task44r4为N26/D0：22 Task45＋4能力判据新增；原两条`Different_result_commands_wait_for_prepare_then_price_committed_or_rolled_back_progress`名称恢复。测试专用QueuedKillDomainCommand只在这三例fixture注册为Transactional和exact selected，第二条不同QueuedKillResult经真实bus入队，handler直接调用原奖励领域、使用总线真实LegacyFootprint；无转发／嵌套bus／直接domain绕队列。两Prepare结局、Clear／Kill计算context、当前等级、真实Tx／SQL资产与Currentnull保留；Unknown的admission probe只绑定第二TResult，围栏前后未admit、仅首条计价的断言恢复。

诊断轮保本机：早期fixture构造／命名空间／空outbox编译错误、首次物件／塔预期帧与事务准备校准、Declarative footprint真实链暴露的私有helper丢session、薄handler硬判据拒绝diagnostics依赖均已修。r1 full第三App失败是原public-flow在已有job中调新薄bus触发真实nested guard，改同算法driver后原实PVF／回城／重入断言保留。r1 Net两个原测试Connect refused，同轮两次AddListen Bind48；GetAvailablePort释放到listen之间存在候选窗口，相关两文件／helper无diff，但混合日志无port／instance，**未记录精确对应**，不归已登记baseline；r2按原并行重跑203/203，无新增失败。r8加强retry断言误以为blocked dispatcher抛错，校准为真实静默drop／Tx次数不增，r9旧新再绿。旧日志缺少的字段不追补，不冒改前full。

纯准备只含`MonsterDeathFailureBoundaryTests.cs`、`MonsterDeathAltarWakeTests.cs`及`RemainingOpeningProtocolCommandFlowTests.cs`的SendFailure测试probe扩展，最终旧25/25覆盖其精确当前内容；补丁`/private/tmp/task45-pure-preparation.patch`。临时旧源只放内存并finally恢复，无源码快照／hash表。产品whole生命周期与默认失败边界不可分，不保第二份业务编排或以raw豁免掩盖差异；其它改为新driver的测试属于产品迁移提交。受控physical／信号与正式TCP不等于A21实机验收。


最终App r3重新跑原DFO parser，其他七项沿r2，共八次均exit0：74＝67原反例＋7control（App62／Protocol1／Net7／Host1／Persistence1／Infra1／Http1；Pvf0），每个有记录的程序集精确一control，outside／exe／orphan均0；原expected分类和数量保持，各dfo-summary.txt含判据输出。

实际模块注册capture在`evidence/task45/runtime`：MonsterDeath唯一ProtocolCommand消息路由，唯一Direct whole及单binding，七fine binding为0；该工具只执行模块登记，不build provider或启动服务。实际DI／decoder／scope由上述正式用例证明。只改八台账对象，全部original和行序保持；preclose／strict及八坏例selftest通过。含所有新增文件的diff-check通过。消息85/159（pending74＋telemetry14）、fine79/204（pending125）、来源1/73、保护2/5、过渡0/8；其它来源及未迁塔adapter保留。


## 首次结果快审R1 F1修复

独立R1判定同TResult替换削弱跨返回类型排队保护，主控接受。修复仅`CommonExperienceEffectTests.cs`，恢复原两理论名和第二条不同TResult的真实bus路径，不恢复产品fine。聚焦`task45-review-r1-focus/Application` **3/3**；随后App完整`task45-final-r3/Application`仍5555＝5537通过＋2exact Dimension＋16原skip，原并行／5m／detailed与独立DFO，N26/D0，DFO62且outside/exe/orphan0。相对App r2只有两理论恢复原名；r2原N28/D2是被R1要求纠正的诊断结果，不能当最终等价证据。七项r2、产品runtime／strict／台账与旧pure-prep25/25未受影响；本轮不重复其余七全量。Result已更新，等待同Astra R2复核，R1评审原文保留。
