---
kind: rks-verification
decision: pass
---

---
kind: rks-verification
decision: pass
---

# TASK-41 核验摘要

范围仅为三条城镇／传送协议、一个 fine、F-88-teleport 与 F-109 参数；不代表 T-007 或 B 完成。HEAD `48511dae` 加本轮未提交改动，无 index／commit／外部操作。本判定仅表示本 Task 必要检查达到原基线接受条件；独立快审与主控接受仍待后继。

## 旧行为与纯准备

- `logs/task41-old/old-app.trx` 旧 Teleport11/11；`old-flow.trx` 加真实 encoder／fine DI Dispose／deferred FK native Commit 后16/16。原先 committed 初次 batch／scope 外抛同一 IOException，无新增 actor 围栏；replay 保 raw。
- `old-protocol.trx` 原相关54/54，`prepare-protocol.trx` 三文件纯结构抽取后旧路由36/36。纯结构 patch：`logs/task41-old/pure-preparation.patch`。
- `old-area2.trx` 14/14：临时旧 Area 原方法体来自 `TownProjectionSlice.cs@48511dae`，仅改类名／注册，复用上述已验证的同一纯抽取算法。只恢复 Area 注册；组合用例的 Step 仍走新路由，新增旧 Step 动态 oracle 不在其声明内。临时类已删除，最终无第二份旧编排。核实际 SQL/native Commit、婚房先提交普通返回位置再 RememberReturn、拒绝、婚礼外观及 actual encoder。
- F-88 新真正 decoder／dispatcher 路径保错误代次零 Main／零帧／未扣物及原位置、冷却、精确租约 post 条件。`logs/task41-mutation/{mutation,mutation-wire}.trx` 去 guard 可编译，分别因 Main=1 与多出实际拒绝帧红；guard恢复，最终强单例通过。

## 最终语义与断言变化

- 三条唯一 CMD 与薄 handler；Teleport fine 类型／注册／调用删除。CMD 直接消费领域／work units／projections／outbox，同原 registry 与位置实现实例；legacy Town 仅窄适配复用 CMD partial 算法，Move 保原单 NOTI 登记。Area 每次原保存对应 ordinary Write([])，101目的原无操作不加单元；Teleport 仅原 actor Main＋unit.Footprint，replay 最低 Inventory｜TownLocation。
- VC-B10 仅已实证的初次登记／scope／Area 提交后默认 typed 差异；Teleport entry 仍 Committed，同时精确 actor Fence／物理 Block／真实 DiscardExact；同 provider 原来源 chain 与 production sender→受控 transport.CloseAsync、同 ID 不同物理 context 隔离有直接断言。replay actual encoder 保同 cause／entry／Ordinal／Committed且无新增围栏。未声称已覆盖替换后新 generation 的全部组合，也未冒 A21 实机。
- F-109 仅移除无业务使用的 represented 参数；真实 recipients／调用顺序与不复活门原断言保留。两处 fake 字串只删 `<-represented` 尾段；其余假接口签名和 handler fixture 仅改驱动。legacy fine 库存清单删 Teleport；原 FailBatch 用例按 VC-B10 保资产／cause并改 typed 与 Block。新增19例（Teleport5＋Area14），无删除。

## 执行中诊断

- `task41-final/` 首轮 App 是主动取消的旧冻结输入：主控发现生产 Move 的单 NOTI 被单消息 explicit 代替，已修回 RegisterNoti并去测试专用 override 后再冻结；不算正式 full。
- `task41-final-r2/full-Networking` 202通过／1失败：`ShutdownOrderWireTests.Stopping_the_host_runs_every_open_connections_teardown_on_the_executor_before_it_stops` 在 ConnectGameAsync:127 Connection refused。同轮并行 console 有 AddListen Bind(48) Address already in use；LifecycleWireHost.Port 先取临时端口即释放，存在窗口，但日志没有 host／port，不能唯一关联失败实例或定位占用者。原参数单例1/1及未变冻结输入 Net r3完整203/203是独立证据；没有改并行、超时、用例或既有 AGENTS 身份清单。
- `task41-final-r4` 的额外 PVF 失败精确为已登记 IllusionTowerPublicFlowTests／64502 orb_wind.equ／minimum level缺失／PvfEquipmentDefinitionSource:160；按 AGENTS 原参数独立5/5才归入该身份。
- 同轮 `KnownRollbackWorkUnitTests.An_unawaited_result_is_drained_and_unfinished_guard_prevents_native_commit_while_preserving_cleanup(failRollback:True)` 暴露测试时序竞争：assigned／entered不证明真实 invoker 已执行 DemandFinished，立即release能让 native Commit 抢跑；实际错误图含 unfinished＋typed（内含真实FK SQL＋独立cleanup）。原两分支重复5轮，True五轮均红、False五轮均绿。按主控裁定仅在测试中有界观察真实 request.Failure 已是 unfinished guard 后再 release，不调用 guard／不注入 Failure／不固定 sleep，最终 typed、无SQL和零行强断言不变；修正后两分支＋F88 3/3。此独立纯测试 patch 为 `logs/task41-old/pure-test-timing.patch`。
- 准备应用顺序：在 `48511dae` 先应用 pure-test-timing.patch，再 pure-preparation.patch（两者不相交），形成纯准备提交；随后应用本轮协议迁移余量。whole 路由与其原默认失败行为不可分，不保另一套编排或新 raw 许可。

## 最终检查

- 最终 solution Release build 在 `logs/task41-final-r5/build.log`，0警告／0错误。原环境 require_escalated、原类并行、项目串行、Release／no-build／no-restore／-m:1／5m／detailed及各项目独立DFO/TRX均保持。
- 聚焦 App77/77、Protocol40/40（`task41-final-r2/focus-*`），后补 F88 与时序修正强单例3/3（`task41-app-diagnosis/fixed`）。App最终 r5：5481＝5463通过＋原2 Dimension失败＋16skip；Protocol r2：1781通过；Networking r3：203通过；Host r2：203通过。四项目合计7668＝7650通过＋2失败＋16skip。未因只有测试时序变化重复未受影响三项目，不称八项目 fresh。
- 两最终失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均7100/maze0/(1,0)/Left缺compatible map。逐display／status／重数相对TASK-40及Host的TASK-39：App N19/D0，另三项目0/0；原跳过保持。每个最终目录的 `*-nd.json` 留实际对账。
- DFO parser分别退出0，总71＝原67反例＋4control；outside／exe／orphan均0，日志为r5的 dfo-Application、r2的 dfo-Protocol／Host、r3的 dfo-Networking。原始失败、取消和诊断均保留，不混入最终通过数。
- 台账只变三消息／一个fine／F-88五行，所有original与非目标对象保持，按HEAD插入顺序写回；preclose、strict、八坏例selftest与diff-check见r5。当前消息76/159、fine61/204、来源1/73、保护2/5、过渡0/8；F-109无独立canonical行，其实际闭合证据归本Task，不动A封存follow-up文本。
