---
kind: rks-verification
decision: pass
---

# TASK-43 核验摘要

本判定仅为两消息／三fine的执行核验，待独立快审与主控接受；不代表T-007、T-008或Spec B完成。基于 `647a00ae` 加本Task未提交改动，未index／commit／外部操作。

## 行为与边界

- 精确旧HEAD产品（临时恢复源与原测试，最终无临时旧路由）`logs/task43-old/old-final.trx` **11/11**；新相同11例在 `task43-final-r2/focus-Application/results.trx` 与5个原entry/publicflow合计**16/16**。两塔／普通各自覆盖actual encoder和真正fine Dispose→whole Dispose，以及peer contexts/query双替换、仅encoder的query-only变体；普通两拒绝直接观察early **Committed**、late layout配置拒绝通过原Admission.ExecuteAsync.RequireRollback成为 **ConfirmedRollback**。真实decoder→dispatcher→原领域/SQLite疲劳→actual encoder→production sender；未把bus mock次数冒生产oracle。
- 普通／Boss仍一次ordinary Write、原全attachments保护，Illusion ordinary且selected=[]；三Enter原未消费footprint，新不加Main／owners／raw／Self／恢复许可。普通#185逻辑abort撤run，已提交疲劳不回滚；SupersededSameRun不误abort，registry仍同DI实例，只窄扩Register。保stale→Consume、配对与原mode优先级、receiver-first、三XO followers-first及所有batch后共用TownLeaveMany；Boss材料先于0333/0334、Illusion先于008E/008F。
- **VC-B13**：旧fine外raw cause改whole默认typed同cause／Committed；普通／Boss原全附件Fence，Illusion source与peer均无Fence／Block。scope旧投影前失败无成功帧，新source和peer各有1个实际batch，直接解析15B帧头核ACK→state→UDP→各自地图顺序后source按原chain关闭；Fence不等于撤已登记帧或物理关peer。
- **VC-B14**：entry附件阶段捕获原物理Context，cleanup显式传入局部字典，测试实际online查询会返回replacement。旧两塔逻辑ID清理误关source replacement，新replacement零send/close。peer contexts已替换时沿现有精确契约drop，旧peer与replacement都close0；query-only而contexts仍原peer时原peerclose1、新physical零send/close。两塔原IsInWorld守卫保留；普通partial隔离不扩到未登记requester。共享OutboxConnectionCloser／F-83／RQ-035未改。
- 原强业务／字节断言保留。最小driver直接调用同CMD算法、测试事务或whole body；它不证明dispatcher／最终scope／typed，生产链oracle另列。原Illusion publicflow的旧executor套新whole触发self-wait，仅改为受控whole-body driver，不改core／并行／skip。CompleteView只控生命周期投影；受控physical transport不是A21实机或完整连接拆除验收。

## 最终正式检查与轮次

所有正式dotnet首次即require_escalated；Release、原类并行、项目串行、5m、detailed、独立DFO/TRX。命令见各本机run.json，原始日志不进git。

- `task43-final-r1` 四full已完成。冻结自查随后发现两塔catch漏搬原IsInWorld，补回原位置；**最终App／Protocol取r2**，其solution build零警告错误、App full **5509＝5491通过＋2原失败＋16skip**，Protocol **1781/1781**。r2聚焦App **16/16**、Protocol **186/186**。
- **Networking／Host取r1**，分别 **203/203**、**203/203**；仅两行tower entry cleanup当前性门恢复不触及其已测成功入口／传输／DI路径，未把改前四full冒为同一次最终freeze，也不称八项目fresh。
- 合计 **7696＝7678通过＋2原失败＋16skip**。逐display／status／重数对TASK42最终基线：App **N12/D1**（11新增oracle＋`Registered_CMD_commits_manual_entry_and_ticket_debit_without_an_outer_transaction`诚实改名为`Production_routing_algorithm_commits_manual_entry_and_ticket_debit_without_an_outer_transaction`），其余 **N0/D0**，skip身份保持。两个失败仅 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，原7100/maze0/(1,0)/Left缺compatible map。各最终full目录nd.json为实差集。
- 最终DFO parser四项目退出0：**71＝原67反例＋4control**，outside／exe／orphan均0；实际明细在各最终violations.txt。中间fixture校准／self-wait从未称baseline或最终通过。
- r2 runtime capture核两消息各唯一ProtocolCommand→同一whole Protocol注册、两binding，三fine注册0；源码三fine与旧普通Delivery及塔Enter生产caller均清零。ledger只五目标，original／非目标逐值及行序保持；preclose、strict、八坏例selftest、diff-check均通过。当前消息81/159、fine66/204、来源1/73、保护2/5、过渡0/8。

本Task无可独立交付的纯搬迁：替换三次legacy dispatch与whole DI scope覆盖投影必然改变默认失败边界；不保双份旧编排或新增raw来掩盖差异。精确cleanup改动亦显式列VC-B14。其它塔方法/fine及簇尾八项目验证仍待后续，原始日志保本机。
