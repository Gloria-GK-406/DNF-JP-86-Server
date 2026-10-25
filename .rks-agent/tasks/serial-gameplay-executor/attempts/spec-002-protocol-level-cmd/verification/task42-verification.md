---
kind: rks-verification
decision: pass
---

# TASK-42 核验摘要

本判定只表示三消息／两fine的必要检查达到本Task接受条件，独立快审与主控接受仍待后继；不代表T-007、T-008或Spec B完成。检查基于 `bf1f070f` 加本Task未提交改动，无index／commit／外部操作。

## 旧行为与迁移

- 原路由 `logs/task42-old/old-protocol.trx` 120/120、`old-app.trx` 50/50。TownLeaveMany纯准备按原次序抽入共享partial；切路由前随 `old-wire-r9.trx` 的12例已编译并通过，其中普通Enter→Back一次性／当前位置直接经过抽取算法。原120/50在抽取前跑过。纯准备patch为本机 `logs/task42-old/pure-preparation.patch`；不进git。
- 12例新旧共用正式oracle覆盖Cycle真实decoder／dispatcher／encoder、输入拒绝／无任务写入、普通Enter/Back一次性与编码失败保内存事实、Boss Back真实事务提交后host编码／精确peer隔离。Cycle fine scope.Dispose原保cycle intent但false ACK，物理替换后旧ACK给replacement；新whole true ACK先绑定原来源，再保cause／原chain关闭原物理来源，replacement无帧无关闭、无selected围栏；9字节成功／单字节拒绝body另由实际transport与codec守护，见VC-B11。
- 后补三条直接失败边界：`logs/task42-old-boundaries/old-boundaries-r4.trx` 3/3；旧Enter/Back从 `DungeonSelectionSlice.cs@bf1f070f` 取完整原handler，仅临时改类名／注册，恢复原Boss PrepareSelection→真实旧Release fine；没有经过新work-unit阶段。临时类／旧方法／注册最终删净。新同用例 `task42-final-r4/new-boundaries-r2.trx` 3/3，最终r5聚焦与full又覆盖。
- VC-B12：Boss Release后actual selection encoder旧raw／无新增Fence，新typed保同cause／Committed、原run全员Fence、source原物理Block，资产／释放／意图保持。两塔Back scope旧在host前失败无帧，新scope在投影后失败，真实peer收到一条NOTI0x1A后原source关闭；Boss原全员保护，Illusion空selected保护且source/peer均无Fence、无Block。peer CloseAsync始终0，同ID replacement无帧／无关闭。Completion均直接观察真实DomainTransaction为Committed；不能把peer Fence说成peer物理关闭。
- 三whole直接消费领域／units／projections／outbox，复用同Town/塔partial；原Boss全run Sessions与Illusion空集合保持，声明owners独立，无新增Main、raw、Self或恢复许可。普通Back仅原End成功后调用同registry实例的ParticipantDeparted；Cycle原Direct内存准备无ordinary Write。两条fine类型／注册／消费者删除。runtime-capture实际三消息均唯一ProtocolCommand→Protocol注册，两旧fine注册0。

## 校准与诊断

- 旧准备红例是新增fixture库存／using／泛型约束校准、漏算原Weakness帧及旧replacement收到false ACK的假设被纠正。补充Release setup先遇peer票已耗尽导致真实retry拒绝，补足测试前置票数后旧绿，没有改产品。ACK物理字节断言曾误用17B偏移，按Game15B头修正。
- 迁后Illusion原测试把新whole包在旧executor／NonEffect中触发真实self-wait，已仅拆setup与真实入口，原奖励／返城／重入断言保持；Protocol两例旧ActivatorUtilities缺bus仅改driver。补充scope首轮错误读取被替换掉的fixture旧Recorder，改为已存在的真实peerPhysical.Sends断言；新旧物理发送0/1直接成立。均不列为既有基线失败。
- `task42-final-r2/full-Networking` 202/203：唯一失败 `CeraShopPurchaseIntegrationTests.A_captured_request_without_a_selected_character_gets_a_failure_and_the_connection_remains_usable`，:46首次ConnectAsync Connection refused，未进入协议。fixture与TcpServices均和HEAD未变；AvailablePort取0端口后释放，同轮console:494 Game及:745 Channel AddListen Bind(48)，没有host/port，不能唯一关联失败实例或定位占用者。原参数单例 `task42-final-r3/diagnosis-Networking` 1/1及同产品／原并行完整r3 203/203是独立通过证据；未改并行、timeout、Skip或通用异常白名单，不称已修复该偶发原因。

## 最终检查

- 最终solution Release build：`logs/task42-final-r5/build-final.log`，0警告／0错误。所有正式dotnet从首次起require_escalated，原环境／原类并行／项目串行，`-c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger console;verbosity=detailed`，各项目独立DFO与TRX。
- App r5 full：5498＝5480通过＋2原Dimension失败＋16skip，focus19/19；Protocol r2 full1781/1781、focus120/120；Networking r3 full203/203；Host r2 full203/203。四项目合计7685＝7667通过＋2失败＋16skip。只有App追加纯测试后重跑受影响门，没有无因重复其余三项目；不称八项目fresh。
- 两最终失败精确为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均7100/maze0/(1,0)/Left缺compatible map。逐display／status／重数对TASK-41：App N17/D0、其余N0/D0，旧skip保持；各最终目录nd.json有实际差集。
- DFO parser对App r5／Protocol及Host r2／Networking r3均退出0，总71＝原67反例＋4control；outside／exe／orphan均0，各目录violations.txt保原记录。
- 台账只五目标，original／所有非目标对象与行序逐值保持；preclose／strict／八坏例selftest及diff-check通过。当前消息79/159、fine63/204、来源1/73、保护2/5、过渡0/8。原始日志／TRX／runtime JSON留本机，不进git。

后续仍有其它入场／塔fine与簇尾8项目验收。Back的NoReturnView将补充scope例限定到host投影；selection CompleteView为受控生命周期投影；生产sender只接受控transport，不等于A21实机或完整连接拆除验收。纯Town抽取可独立准备；三whole与scope／默认失败边界不可分，保两套旧编排或新增raw均不在授权内。
