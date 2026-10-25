---
kind: rks-verification
decision: pass
---

# TASK-44 核验摘要

判定只覆盖三消息与六fine的本次执行及无新增回归，等待唯一fresh Astra快审与主控接受；不代表T-007、T-008或Spec B完成。基于`1515397c`＋本次未提交改动，无index／commit／外部操作。

## 旧新行为与证据层次

- 迁移前原路由Protocol聚焦`logs/task44-old/Protocol` **24/24**，App旧oracle＋真实SQLite DungeonVisit／塔public flow `task44-old-oracle-r2/Application` **29/29**。补齐后的最终正式oracle在精确HEAD旧产品`task44-old-final/Application` **20/20**，新产品`task44-start-real/Application` **20/20**，并全部包含在最终App full。旧重跑临时恢复的字节仅在内存保留并在finally恢复，不复制源码证据。
- **15例生产链**：真实decoder→dispatcher→领域事务／普通双方真实SQLite疲劳→actual encoder→production outbox→受控physical sender；**5例机制方法级**：实际旧Direct subscriber与新投影hook，通过受控INotificationBus检查快照受众／Failed，不冒编码器或发送链。原强业务／codec／字节／顺序断言保留；既有小夹具改为同生产算法driver，注释明确不证明bus／最终scope。Networking实际TCP Stage线级完整保留，准备Load改为显式领域事务。
- 普通ready直接原TryMarkParticipantReady，无Tx／虚构Committed；registry前后current／run／version／Ready门、城镇仅001E及release→mechanism→quota→首次weakness保持。普通Move仍调用生产wrapper的TryMoveAsync，**一次ordinary空selected、无footprint**，真实room与双方首次疲劳原子提交；重访、免疲劳、零余额及SQL失败回滚由原DungeonVisit正式用例守护。原current／admission／MayLeave、受众fallback与StartMap／Altar／Hell→fatigue保留；FindCurrent与原helper共用一份算法，同singleton registry只窄扩Find。
- Boss Load与leader Start准确**两次独立事务**，最后加载者可为peer；全run参与者保护。第二失败例实际调用原Start取得**事务内Fighting候选**后抛同cause，第一Committed、第二ConfirmedRollback，事务外仍Ready、无加载帧；新whole按先前已提交unit默认typed并保护原全员，不称整个数据库结果不确定。Illusion Load只Ready，三个动作各原独立ordinary、空selected，无Fence／Block；Stage2不回复不推进，Stage1才Fighting。Boss坐标和原Exact／stale优先级保持，StartBoss fine仍供departure真实消费者。
- **VC-B15**：普通ready scope旧release前失败无帧、新release＋首次weakness已登记后同raw原因失败，仍无Tx／Fence；普通Move已提交后的scope／未吞encoder由raw改默认typed，无新增selected围栏，原已登记room→fatigue仍实际交付。Boss旧Load.Dispose止于Ready无帧，新whole.Dispose在两Committed、Fighting和各一条001E之后；Illusion始终Ready，scope新已有001E。两塔encoder失败继续逐成员收集／精确close／dispatcher正常返回，没有擅改为whole失败。scope／encoder的真实帧数、cause、source关闭及原链直接断言，Fence不等于撤帧或peer物理关闭。
- **VC-B16**：本次两塔加载／换层catch捕获原physical Context，保IsInWorld和共享contexts准入。同ID source＋peer实际contexts替换时新replacement与已换走旧physical均send0／close0；仅online query变化而contexts仍原物理时只关原物理。旧逻辑ID误关replacement已在同oracle读出，共享OutboxConnectionCloser／F-83／RQ-035不改。
- 机制快照恢复重新FindByCharacter取当前selection，保空／未Started静默、原通知和Failed同IOException；少一层旧Direct subscriber scope，不宣称scope生命周期完全相同。Timer真变化事件及subscriber保持原路，未关闭来源行。

## 最终正式检查

所有正式dotnet首次require_escalated，Release、原类并行、项目串行`-m:1`、5m hang限制、`console;verbosity=detailed`与各自DFO/TRX。**最终统一取`logs/task44-final-r4`**，build零警告错误；每项目run.json提前记录cwd／HEAD／branch／环境，之后记起止及真实退出，命令与完整console留本机。

| 项目 | 最终结果 | 对TASK-43最终逐display／status／重数 |
|---|---:|---:|
| Application | 5529＝5511通过＋2原失败＋16原skip，退出1 | N20／D0 |
| Protocol | 1781通过，退出0 | N0／D0 |
| Networking | 203通过，退出0 | N0／D0 |
| Host | 203通过，退出0 | N0／D0 |

合计**7716＝7698通过＋2原失败＋16skip**；差集在各项目nd.json，skip身份保持。仅失败为`DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`、`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，仍原7100／maze0／(1,0)／Left无compatible map，未修本范围外内容。

DFO parser四项目均退出0：**71＝67原反例＋4control**（App62／Protocol1／Net7／Host1），outside／exe／orphan均0；各dfo-summary.txt保具体分类。此处只核四个实际运行项目，不冒七／八程序集证明。

中间轮保为诊断：首轮Protocol driver构造缺outbox造成7失败已修；App r1多出两个已删除fine的旧名单断言，更新为实际剩余fine并增三whole Direct／Protocol断言；r2的nullable断言编译错误已修。r3 App已无新增回归，但随后只收紧真实Start失败oracle，因此不当最终测试freeze。旧encoder异常本来被收集吞掉的首轮表征校准也保留原始日志，不作旧绿依据。上述诊断不归为baseline失败，旧轮缺少的元数据未伪补。

最终runtime capture及源码crosscheck通过：三消息各唯一ProtocolCommand、三个whole各一个binding，共用Game endpoint算法handler；六fine注册0，StartBoss注册1且pending。九台账目标以外逐对象、全部original和行序保持，preclose／strict／八坏例selftest及含全部新增文件的diff-check通过，消息**84/159**（pending75＋telemetry14）、fine**72/204**（pending132）、来源**1/73**、保护**2/5**、过渡**0/8**。

纯测试准备可独立提交`DungeonLoadingFailureBoundaryTests.cs`、`LoadingMechanismRecoveryTests.cs`及`DungeonEntryFailureBoundaryTests.cs`的probe扩展，最终三文件确已在旧HEAD编译并20/20。产品归并与whole scope默认失败变化不可分；不保两份业务编排、不增raw／Main／Self掩盖差异。受控physical／通知bus不等于A21实机验收；其它Kill／Pickup／Die／Leave／Timer与簇尾八项目仍待后续。
