# TASK-41 — 城镇移动与传送归并

本 Task 承接 Plan P8 T-007，迁移 SetUserPosition（65571）、SetUserArea（65572）、Teleport（65773）、旧 TeleportCommand，并闭合 F-88-teleport。TASK-40 已独立通过并以 `e8ea2f93`／`48511dae` 本地提交，保留原空保护参与者与成功通知隔离；本次继续消费既有 runner／足迹能力。没有目标偏离；按实际依赖收拢城镇共享投影及 F-109 无语义参数，未迁副本、选角、婚姻和 Timer 仍保各自入口与责任。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-40；覆盖 RQ-001／003～009／013～015，AC-001～005／008。
- Outcome: 三条唯一完整协议 CMD／薄 handler；旧 Teleport fine 的实际引用与注册删除。原城镇位置、区域可见性、持久化时点、频道／婚房／婚礼拒绝、传送扣物／冷却／位置和回包顺序保持；F-88 与 F-109 在真实新 owner 落定。
- Boundary: 三入口、被其实际调用的城镇／队伍投影阶段、必要位置领域接缝与原单例接线、有限 replay owner、正式测试、五行 ledger、AGENTS 清单及记录。不扩 shared core／raw／Self／恢复许可，不提前闭其它消息或来源。
- Intentional change: 默认保原行为；真实旧新 oracle 证明的 scope／提交后默认失败差异追加 Spec 变化表随 Task 审，不另修 Spec／Plan。可独立纯结构准备分开提交；不可分部分说明原因，不保另一套编排或嵌套 dispatch。
- Review: 一次 sol6.1 执行、一次 fresh Astra 快审；无 Task 挑战、无取证专用 Task。

## Detailed Plan

1. 先跑旧路由正式表征：TownProjectionHandlerTests、TownAreaProjectionDeliveryTests、TeleportConsumableTests、codec 与相关频道／婚房／队伍用例。补必要实际 decoder／dispatcher／SQLite／encoder／production sender 的旧 oracle，覆盖成功／拒绝、stale 会话或代次、同区域与跨区、频道限制、普通和独占区域、婚房往返／婚礼外观、写失败不发布位置，以及主提交后 scope／初次编码和 committed replay。控制 transport 与 A21 实机区别要披露；正式断言核资产、帧序、事务结果、原 cause、精确关闭与替换隔离。
2. CMD 吸收 handler 与 TownAreaProjectionDelivery 的实际读取、修改和投影顺序，旧调用方仅通过窄适配复用同一算法，不让新 CMD 注入旧 Delivery／Coordinator／sender／bus／locator。位置持久化经真正拥有该行为的领域形状暴露原实现，不把 ISelectedSessionPositionPersistence 加白名单或伪装成 Protocol 包装；不复制状态或规则。婚礼外观出口只做原确定性组包登记，不把婚姻整簇提前迁入。
3. SetUserPosition 保原精确成员检查、StepTo／MoveExact、来源回声再同区受众通知，无原事务的内存动作不人为并成新持久事务。SetUserArea 保 current view／IsInWorld／当前位置／Owns、内容和频道判定；原有持久写逐个对应普通 Write，提交后才更新内存与区域投影。特别保留婚房101先保存普通返回位置、再 RememberReturn，而 destination=101 的原持久化是无操作；不能把原已提交与随后内存动作合并回滚。保临时频道不保存、原已提交后取消及失败边界，不添加 selected 围栏。
4. Teleport 保 previous.Owns 在 slot／channel／主结算之前，显式 Main 沿原 actor、unit.Footprint 与 Inventory|TownLocation 最低投影；正常拒绝、真实回滚、成功扣一次、冷却／库存→城镇原序以及已提交重复请求读取当前投影均保持。有限 replay owner 注册闭合，不增加 raw 或可恢复入口；scope／编码故障按既有默认保护验收并记录确有差异。F-88 正式旧代次用例改真实 CMD 驱动后仍核未进入结算／未扣物，移除新 guard 的可编译变异须在真实行为断言上变红，再恢复。
5. F-109 只删 RefreshObservedParties 的 representedSessions 无语义参数及实际传递，逐调用方保持 recipients、顺序和当前精确读取／不复活门；旧 mock 中仅为该无用参数记录的字串断言需明确属于驱动适配，不能删真实受众／帧序断言。共享返城、离场、ForcePosition、选角和婚姻调用方仍通过同一算法，必要回归覆盖这些消费者，不冒其入口迁移完成。
6. 冻结后 Release solution build、相关聚焦，App／Protocol／Networking full；必要领域接缝／DI接线改动还跑 Host full。所有正式 dotnet 运行使用规定的 require_escalated 原环境，原类并行、项目串行、5m、detailed、各项目独立 DFO／TRX；不把 sandbox 结果当正式验收。按 TASK-40 三项目与 TASK-39 其它项目逐 display／status／重数核 N/D，保原两条 Dimension 身份与18总跳过口径。仅闭三消息／一 fine／F-88 五行，original 与非目标保持；strict／八坏例 selftest／diff-check。Result 一页说明交付、非常规选择、真实检查／局限、遗留，停写交 fresh 快审；主控消费并本地分段提交后继续 T-007。

## Result

- Outcome: done
- **交付**：SetUserPosition／SetUserArea／Teleport 三条唯一协议 CMD 与薄 handler，删除原 Teleport fine。Town／Party投影由 CMD partial 持有同一算法，旧消费者窄适配复用；位置能力与旧接缝映同一真实实例，逐原提交点保 ordinary Write／Main。F-88真入站保错误代次0 Main／零帧及原post条件，去guard正式行为红后恢复；F-109仅删无语义 represented 参数，保真实受众／顺序／当前性断言。仅五目标ledger行闭合，AGENTS只增清单。
- **选择与变化**：Spec仅追加VC-B10（首成功batch／scope／Area提交后默认typed），不扩raw、Self或恢复许可；Teleport原entry仍Committed且精确Fence／Block／真实DiscardExact，replay原cause／entry／Ordinal／无新增保护保持。三文件纯结构patch与独立时序测试patch可先作纯准备，应用顺序和旧Area方法体来源、Step oracle限制见 `verification/task41-verification.md`。whole接入与原runner默认失败不可分，最终不保第二套旧编排。独立KnownRollback测试旧TCS只保证action等待、不能保证真实guard已执行，原五轮复现后仅改为观察真实Failure再放行；原typed／无SQL／零行强断言不变，未改core。
- **检查**：Release solution 0警告错误；旧Teleport16/16、相关Protocol54/54、纯准备旧路由36/36、补旧Area14/14（Step仍新route）；最终聚焦App77/77、Protocol40/40，后补强单例3/3。最终full：App5481＝5463pass＋2原Dimensionfail＋16skip，Protocol1781、Net203、Host203，共7668＝7650pass＋2fail＋16skip；两失败仅 DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource／All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot，7100/maze0/(1,0)/Left缺图。逐display/status/重数App N19/D0、其余0/0；DFO71＝原67反例＋4control，outside/exe/orphan0。所有正式dotnet从第一次使用require_escalated，原类并行／5m／detailed／独立DFO与TRX。
- **局限与遗留**：首轮旧冻结App主动取消，不作验收；Net r2有Connect refused且并行日志有Bind48，未唯一定位host/port或占用者，原参数单例1/1与完整Net r3通过另记。App r4额外PVF精确旧身份按AGENTS独立5/5；旧KnownRollback时序竞争五轮复现、修正后最终App r5仅原两失败。最终入口：r5 build／App／strict等、r2 Protocol／Host、r3 Net，详见核验摘要。仅受控transport，未作A21实机；T-007／B仍待后继，不提交、不推送、不PR或清理；执行结束停写，交唯一fresh快审。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**本 Task 局部快审通过，无阻断发现；不代表 T-007 或 Spec B 完成。** 审查目标为 `48511dae` 加本轮全部未提交 TASK-41 改动、Contract r1 与最终 Result；依据 Spec B6（含随 Task 追加的 VC-B10）、Plan P8 T-007、流程重置、当前 AGENTS，以及 reviewing-work 的 Task Unit／Code 判据。独立读取实际代码、旧版差异、测试与本机证据，未另派 agent、未重跑全量。

- 三入口唯一 whole CMD／薄 handler、Teleport fine 删除及有限 replay 注册闭合。Area 保原普通写提交后才发布内存；进入101先保存普通返回位置再 RememberReturn，目的101不造写。位置两接缝共用原实例；Move 仍单 NOTI。Town 算法抽到同一 CMD partial，legacy 窄适配；F-109 实际只去无语义参数，受众、顺序与当前性断言保留。
- Teleport Main 仅原 actor，F-88 在 mapping／channel／Main 前检查精确旧成员；真实入口核零 Main／零帧／资产、位置、冷却与租约，已读两份可编译变异红例。VC-B10 对照实际 encoder／DI Dispose，守原 cause、Committed entry、精确 Fence／Block／真实 DiscardExact；replay 保原 entry／Ordinal 与有限 raw，未扩大恢复许可。
- 独立解析最终 TRX：App r5 5463通过＋2原 Dimension失败＋16跳过；Protocol r2 1781、Host r2 203、Net r3 203全过；聚焦77／40及修正后3例全过。build 0警告错误、strict有效及8坏例拒绝记录匹配。JSON对 HEAD 仅三消息／一 fine／F-88五目标变化，original与非目标未变。两个准备 patch 文件集合不相交，并在临时目录对 HEAD 原文件按指定顺序 check／apply成功，未触 index。
- KnownRollback 只在测试有界观察真实 request.Failure 的 unfinished guard 后放行动作；未调用 guard、注入 Failure 或删减 typed／无 SQL／零行断言，可作为独立测试准备。纯抽取与 whole 默认失败语义分开提交的安排合理。

**保留限制：** 旧Area14例是恢复原方法体的对照，组合 Step 仍新路由；生产 sender 接受控 transport，不是 A21 实机。Net r2 connect refused 与并行 Bind48 尚不能唯一归因，不纳入既有偶发名单；原参数单例与 r3完整通过是独立后续证据。四项目总数7668不冒充八项目 fresh；两条原内容失败与后继簇验收责任不变。审查仅追加本段，无产品／测试／台账修改或提交。

## Controller Consumption R1

主控接受独立 R1 的 approved，无未处置阻断；实际阅读实现、故障用例、测试时序修正及评审追加。另直接解析四份最终 TRX，确认7668＝7650通过＋原2条Dimension失败＋16跳过，App新增19／丢失0，另三项目0／0；读取DFO实际71条，原67反例与4control之外无记录。重新执行strict、八坏例selftest、diff-check，并逐对象核对台账仅五目标变化，original及非目标全部保持。旧Step、Net未唯一归因、受控transport和非八项目fresh限制照留；不据此扩大验收。先提交两份互不相交的纯准备，再提交唯一whole及有意默认失败语义，提交后继续T-007。
