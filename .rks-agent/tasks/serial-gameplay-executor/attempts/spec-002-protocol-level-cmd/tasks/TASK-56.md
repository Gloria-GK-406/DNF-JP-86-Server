# TASK-56 — 轻量查询、报告与协议应答归并，收口 T-007

本 Task 承接 T-007 最后一组：CeraQuery（65599）、CreatureResponseReport（65637）、CreatureScriptMessage（65658）、ClientSpecStatistic（65707）、StoryPause（65727）、ClientSpecProfile（65730）、SessionMetrics（65786）、MercenaryInfo（65978）、ClientHardwareDetail（66013）、SecurityStatus（66130）、RosterFollowUp（66216）、RuntimeTingStatistic（66227）、ContentsPlayInfo（66241）、ShopPurchaseCount（66252）、ConnectionCheck（66781）十五消息与 QueryShopPurchaseCounts 一 fine，共十六行。TASK-55 已获唯一 Astra 批准、主控接受，并以 23ebeaa8／7275969e 分段本地提交，开工前工作区干净。目标未偏离：每个入站有整协议 CMD，保原日志、静态应答和读取／即时投影，不把无需事务的操作包装成写。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-55；承接 T-007 的相关 RQ／AC，RQ-001／003～009／013／014、AC-001～005／008。
- Outcome: 十五消息各有唯一 Protocol／Direct whole binding；原有限 map-entry 家族可共用具名 whole，其余按实际语义组织。保原日志类别及语义。所有操作原无事务，保 direct 读取、日志、sender／event projection，不造 Write 或 ReadOnly unit、Main、footprint、raw、Self、selected 保护。QueryShopPurchaseCounts 删除，其原调用算法在 whole 内直调原领域能力。
- Nature/Gates: 保行为迁移；只将正式旧新证明的默认生命周期差异追加 VC 随唯一 fresh Astra 结果审查；不改 Spec／Plan／core。T-007 功能簇收口须八项目 fresh full，并作簇报告；不外部收尾。

## Detailed Plan

1. **旧新表征。** 先跑既有 codec／golden／handler 和真实查询强例；新正式测试用实际 decoder／dispatcher／生产 encoder／outbox／physical，覆盖十五入口、身份有无及 selected 与 InWorld 差异、完整准确响应字节和无帧日志路径。保原 malformed payload 拒绝；强旧断言适配真实入口，不能只证明假 bus 被调用。测试准备先在旧源跑绿。
2. **纯应答。** ContentsPlayInfo 原成功字节加18零（19B）、MercenaryInfo 原成功字节加6零、ConnectionCheck 严格空 body 且应答单字节成功，MercenaryInfo 则沿原 decoder 忽略 body、SecurityStatus 输入至少8B且回复6零（不补成功前缀）、RosterFollowUp 输入2B与原 sentinel、应答原拒绝码0。身份门、字段与日志严格按原源码，不统一成新鉴权政策。StoryPause body 至少2B、忽略尾部，且仅 FindSession，不增加 InWorld；按原 selector NOTI 0xAA 与两个输入字节回送。
3. **报告。** CeraQuery 空 body、RuntimeTing 一字节的原 map-entry 家族及 SecurityStatus 分支保持。CreatureResponse exact2B、CreatureScript 11B头加0..256B数据，只按原 selected 归属写日志不回包。ClientSpecStatistic／Profile／HardwareDetail／SessionMetrics 保 attribution 和语义日志，完整 codec/descriptor不变，无状态写／帧。十四条保留遥测 ledger 不因名称类似而迁入或关闭。
4. **购买计数。** 原 QueryShopPurchaseCounts 是 Direct，whole 直接保持 shops.PurchaseCounts(session,npc,clock) 顺序与原TheseCharacters投影；Project Failed仍忽略，不新增事务或 Fence，不复制业务算法。其他仍消费的共享帮助器不能提前删除。
5. **失败与边界。** 真实首encoder／actual scope.Dispose／日志或查询异常的旧新表征，精确原连接关闭与同ID替换隔离；按实际无写语义断言，不能套用其他 Task 的已提交 typed 模板。保持原原因、不捏造业务拒绝。十五 binding 唯一，旧 fine 类型／生产引用／注册清零，薄 handler 无旧业务。过时“no command bus”注释同步。
6. **收口。** 只闭十六 ledger 行，AGENTS 补清单行，并将“其他与主设计的偏差”中 CLIENT_SPEC_STATISTIC 的旧直读／inbound 作业三行同步为现有协议 CMD；保留紧随其后的纯遥测注册／注入判据，不扩写逐协议历史。最终旧复验完整CURRENT备份与HEAD／owned／index锁，所有runner结束后主控执行finally恢复。恢复后 fresh非增量Release solution 0warning/error，八项目full严格串行、原类并行／5m／detailed／DFO。逐display／outcome／重数对比，原两Dimension失败及18skip不放宽；runtime、strict／八坏例、含新增文件diff-check齐备。Result一页、纯测试准备patch、所有证据来源与实际限制落盘后STOP，唯一fresh Astra审查、主控接受本地分段提交并汇报T-007计数／有意差异／风险／八full结果，随后进入T-008。

## Result

- Outcome: done（执行 round 1；最终验证 `task56-final-r2`，待唯一 fresh Astra 快审及主控接受）。
- **Changed:** 十五消息各唯一 Protocol／Direct binding，共十四 whole（仅原 Cera／RuntimeTing 家族共用），薄 handler 只交原消息与精确 Dispatch；删除 QueryShopPurchaseCounts fine，原领域查询／时钟顺序与 TheseCharacters 当前投影保留。原无事务、日志类别／descriptor、完整静态 bytes、无账号应答、Story selected-only、Creature 角色归属及 Shop Project Failed 忽略保持；无新工作单元／Main／footprint／raw／Self／选中保护。仅十六 ledger 行闭合，AGENTS 表增一行并同步同范围过时现状／no-command 注释，Spec 只追加 VC-B36，Plan 未改。
- **Why:** 原硬件／metrics／Roster 强例改真实 dispatcher，Roster 双 CMD 数提升为完整生产帧计数；ShopFixture.Query 改真实 decoder，保原 SQLite 累计计数断言。纯准备 patch 独立。Shop fine scope 收入 whole 后原响应组装先于 scope.Dispose，该时点差异与 whole 归并不可分；仅同原 cause 的 send→close 差异列 VC-B36，不套用提交／typed 语义。
- **Checks:** 主控执行最终旧复验 App80／Protocol76 全 pass，39 owned／8704非 owned 逐 bytes 恢复。最终非增量 Release solution 0warning／0error，八项目 fresh full 9652＝9632pass＋2原 Dimension fail＋18skip；App5859、Protocol1790、Net203、Persistence484、Infra976、Host203、HttpApi57、Pvf80。逐 display／outcome／重数仅 App新增46、删0，其余0／0；失败 Name／Message与skip身份不变。原DFO parser74＝67negative＋7control，outside／exe／orphan0。实际冷 runtime十五binding／Direct／空 exits／Self None，旧fine类型／handler／注册0；真实provider／invoker由正式flow覆盖。ledger strict／八坏例及含所有untracked的whitespace均通过。证据 `verification/task56-verification.md`、`verification/logs/task56-final-r2/`、`verification/evidence/task56/`。
- **Remaining:** 无本Task阻塞项；T-007范围交主控接受并汇报，T-008／余23消息／86fine／来源／保护／过渡仍按Plan待做，不启动TASK-57。未做A21实机／专项性能测量、Git写入、外部操作或服务部署。执行者STOP，交主控唯一fresh Astra。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**结论：本 Task 范围无阻塞发现。** 审查合同 r1／执行 round 1、`7275969e41044fe709f42e158a9acb13d34f8cca` 后未提交变更，以 `task56-final-r2` 为最终验证轮。依据 process-reset、当前 AGENTS、Spec B8／Plan P10 的 T-007 边界和 reviewing-work 的 Task Unit／Code 判据独立审查；本结论不封存 Spec B。

- **实现与变化边界。** 逐份核旧源码、当前十四 whole／薄 handler／接线：十五 binding 唯一，只有 Cera／RuntimeTing 共用；Shop fine 删除，查询仍按 session→online→clock→PurchaseCounts，再走 TheseCharacters，Project Failed 仍忽略。日志类别／字段、严格空 ConnectionCheck 与忽略 body 的 Mercenary、Story selected-only 与 NOTI 0xAA、Security 六零及其余原响应 bytes 保持。没有新增工作单元、Main、footprint、raw／Self 权限或 selected 保护，core 未改。VC-B36 仅记 Shop 的实际 scope 释放时点变化：旧零帧、新一帧后关闭，同原 cause、无事务／Fence；与 whole 合并不可分。
- **正式证据。** 独立解析旧 `task56-old-final-r1` TRX 为 App80／Protocol76 全通过，核其脚本及27份旧产品备份确为 HEAD；七文件纯测试准备 patch 在内存中逐 hunk 对 HEAD 适用且得到当前测试。新46例含十五真实 decoder／dispatcher 入口、归属／selected 差异、七实际 encoder 故障、Contents／Shop 两个实际 scope、日志／查询异常和冷注册；原硬件／metrics 语义断言保留，Roster 两个生产 CMD 0x2A8 的 body 均为0000。**两处 scope 试验不外推十四 whole 全覆盖。**
- **最终检查。** 核 fresh 非增量 Release build 原日志为0警告／0错误；八份 run.json／详细日志及原TRX确认严格串行、5m、DFO、自然退出。逐 display／outcome／重数对原有效基线：9652＝9632通过＋2原Dimension失败＋18跳过；仅App新增46、删除0，其余无增删，失败Name／Message及skip身份不变。重新调用原 violations.py 八项目均通过，74＝67反例＋7control，outside／exe／orphan均0。runtime实际DLL与正式测试DLL逐bytes一致，十五binding／十四whole、Direct、空exits／Self None及旧fine类型／handler／注册清零；冷probe仅证明登记，实际provider由正式flow覆盖。
- **冻结与收口。** 追加本页前独立核39 owned／34 source与8704 nonowned、HEAD／branch／status／空index及最终CURRENT备份一致；旧冻结到最终源码仅恢复Shop的DomainEvents using，未改行为／测试。ledger仅本十六行改变，original、行序及非目标不变；strict、八坏例和含新增源码的whitespace复查通过，十四遥测保留。未重跑build/test，未改产品／测试、Git或其他记录。

**限制：** 未做A21实机或专项性能验收；受控physical替换不等同真实重登录。交主控消费本次局部批准；本审查仅追加本页，随后 **STOP**。

## Controller Consumption R1

主控读取唯一 fresh Astra 的 Independent Quick Review R1，接受本Task的局部 approved，无待返工项。独立核最终旧 App80／Protocol76、39 owned／34 source／8704非owned精确恢复，最终源码只恢复Shop的一条必要using；fresh build零警告错误，八full原TRX逐display／outcome／重数为9652＝9632通过＋2原Dimension失败＋18跳过，仅App增46／删0，失败Name／Message及skip完全不变，所有run自然结束且串行。独立原DFO parser74、runtime实际DLL与最终build逐bytes、十五绑定／十四whole／fine清零、strict与八坏例、十六账行原始／非目标／行序及所有新增文件whitespace均通过。记录：`verification/logs/task56-final-r2-main-inventory.json`、同目录`main-dfo-*`，最终恢复资料`/private/tmp/task56-finalbackup-r1`；主控仅纠正验证摘要中的using错误时间措辞并同步恢复清单，产品和测试未变。

T-007在本Task收口；累计消息136/159、fine118/204、内部来源2/73、保护2/5、过渡0/8。VC-B36仅商店Direct fine scope收入whole的已证send→close时点差异，无事务或新增保护。接受限上述正式覆盖；未做A21实机／专项性能验收，不封存B或授权外部收尾。接着按纯测试准备与协议归并分别本地提交，随后T-008两塔先正式表征共享HOW，所需P11须有界独立审查再实施。
