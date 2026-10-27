# TASK-42 — 副本选择与循环准备归并

本 Task 承接 Plan P8 T-007，迁移 EnterSelectDungeon（65551）、BackToVillage（65668）、PrepareCycleDungeonEntry（66312），删除 PrepareCycleDungeonEntryCommand 与 ReleaseBossTowerSelectionCommand，共五行台账。TASK-41 已以纯准备 `6847b277`／完整迁移 `bf1f070f` 本地提交，保留同一城镇算法、位置提交时点及真实故障记录；本次消费已有普通 Write、声明足迹与两塔返城阶段。没有目标偏离；两塔选择释放／返回是这三个入口的实际依赖，按 Plan 允许的局部先后处理，不称 T-008 或仍有其它调用方的塔 fine 已完成。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-41；覆盖 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 三条唯一完整协议 CMD 与薄 handler，删除两条实际失去消费者的 fine；保持选择意图、全队精确当前性、界面帧序、一次性返回、防重复投影、循环准备与两塔提交／保护边界。
- Boundary: 三入口及实际依赖的选择／返城阶段、既有领域与原 registry 接缝、正式测试、五行 ledger、AGENTS清单及人工记录。不得注入旧 Delivery／Coordinator／sender／bus 作为新 CMD 的业务编排；共享旧调用方通过窄适配复用同一算法，不新增影子状态、raw、Self、恢复许可或核心能力。
- Intentional change: 默认保持行为；真实旧新 oracle 证明的默认失败／scope差异只追加 Spec 有意变化表随 Task 审。可独立纯结构准备分开提交；不可分部分在 Result 说明，不另修 Spec／Plan。
- Review: 一个 sol6.1 执行与一个 fresh Astra 快审；无 Task 挑战或只取证 Task。

## Detailed Plan

1. 先在旧路由跑正式表征：PartySelectDungeonHandlerTests、LeaveDungeonTests 的 BackToVillage、TheCharacterChangesAsYouDressItTests 的当前属性／教程延迟、DungeonSelection codec／wiring、CycleDungeonEntryTests／CycleQuestFlowTests，以及两塔相关 public flow／projection／footprint 用例。补真实 decoder／dispatcher／SQLite或原领域事务／actual encoder／production sender 的必要旧 oracle，核成功与拒绝、迟到跟随者仅ACK、缺成员／旧代次／stale、塔释放和返回、真实 scope／提交后故障、精确来源与替换隔离；不以 mock bus 次数代替业务断言。
2. EnterSelectDungeon 保 Boss 准备优先：先查 session／run，再判 stale／exact／leader，原 Release 事务对应 ordinary Write，保护原 run 全体 Sessions，释放成功才继续普通选择，拒绝即消费。该领域动作不需要效果足迹，不增加 Main。普通分支保全 roster、全部当前成员、原 Interactive／Directed／Rechallenge 意图，逐成员记录→Town.LeaveMany→当前属性刷新；最终精确过滤后按原 stat／ACK／state／UDP／housing／条件状态顺序投递，再逐成员 Mirror quota 投影。教程延迟、Despair／SAO读取和全队ID保持；quota原本只是即时事件投影，不造事务或fine。
3. BackToVillage 保初始有session才调塔分支、Boss先于Illusion、各 helper 原 stale 检查位置与消费规则。塔 Leave 各对应原普通事务，复用既有声明足迹，owner为原 run 成员；Boss保护原全员（报告者原物理连接，其余精确session），Illusion保持原空保护集合，不能把owners变为Fence集合。提交后调用同一现有返城／结算／精确失败关闭阶段；仍被其它入口调用的塔fine保持pending，不能嵌套dispatch或复制另一套流程。
4. 普通 Back 保 current view／当前位置、ClearExact 一次性意图；已消费跟随者只ACK，队长按原slot取得当前成员并清各自意图，两次原当前性检查保留。逐原成员 EndAsync 后只在成功时调用原 delivery registry 的 ParticipantDeparted（既有领域形状映射同实例），再来源ACK→Town.Enter；无原事务的内存动作不人为并成新持久事务。保原受众／顺序与成员失效不复活。
5. Cycle Prepare 原注册是 Direct：只读事实／规则及 TryReplaceExact 内存意图，不因 IAccountCommand 名称造写单元。保8字节解码、输入范围、无角色拒绝、选区／循环配对／旧任务共存和前后当前性，InvalidDataException 内容拒绝仍回原false ACK。已有测试的准备入口改真实 whole／handler驱动，任务与入场业务断言保留；验真实scope边界，不将独立typed／sticky故障伪装成普通内容拒绝。
6. 冻结后 Release solution build、相关聚焦及 App／Protocol／Networking full；若新增领域DI或宿主接线实际受影响再跑Host。全部正式dotnet从首次开始require_escalated，原类并行、项目串行、5m、detailed及各项目独立DFO/TRX。对TASK-41最终三项目逐display/status/重数核N/D，仅两条原Dimension失败及原跳过允许沿用；其它失败保现场并查因，不因复跑绿冒称既有偶发。台账仅五目标，original和非目标逐值保持、写回不重排其它行；strict／八坏例selftest／diff-check。Result一页说明交付、非常规选择、实际检查和局限，停写交快审；主控消费、本地分段提交后继续T-007。

## Result

- Outcome: done（执行轮R1，`bf1f070f`＋本Task未提交改动；待fresh Astra快审与主控消费）。
- Changed: 三消息唯一whole CMD／薄handler，两fine类型、注册和实际消费者删除；Town/塔算法复用共享partial，Boss原全run保护、Illusion空selected与独立owners保持，Cycle无新增事务。台账仅五行，消息79/159、fine63/204、来源1/73、保护2/5、过渡0/8。AGENTS只增名字；VC-B11/B12逐项记录真实scope与提交后默认失败差异。
- Why: TownLeaveMany为可独立纯准备（本机patch；旧路由正式12/12已经过它）。whole迁移与fine scope收入whole后的ACK／typed／原参与者保护边界不可分，不能保第二套旧编排或增raw逃避。补充三条旧新真实oracle证明Boss Release后编码、Boss／Illusion Back scope；只临时恢复HEAD完整旧handler／Release fine作对照，最终删净。
- Checks: 最终Release solution build零警告错误；旧Protocol120/120、App50/50、新旧正式12例及补充三例均有旧绿对照。最终App r5 focus19/19、full5498＝5480通过＋2原Dimension失败＋16skip；Protocol r2 1781/1781、Net r3 203/203、Host r2 203/203。逐display／status／重数App N17/D0、其余0/0；DFO原67反例＋4control，无outside／exe／orphan；runtime三唯一route／两fine注册0，五行等价检查、strict、八坏例selftest与diff-check通过。命令／实际路径及诊断见[核验摘要](../verification/task42-verification.md)。
- Remaining: 两原Dimension失败仍为7100/maze0/(1,0)/Left缺map。Net r2新增Cera首connect拒绝保现场，原参数单例和原并行full重跑通过；Bind48无host/port不能唯一归因，不登记通用豁免。未完成T-007/T-008、未跑簇尾8项目、未验A21实机；NoReturnView／CompleteView和受控transport限制见摘要。未操作index／commit／push／PR／merge／tag／deploy或清理。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

本结论仅覆盖 TASK-42 执行 R1 的局部结果：`bf1f070f`＋当前未提交改动，Task Contract r1、Spec B6（本次仅 VC-B11／B12 及其范围索引）、Plan P8 T-007 与 process-reset 流程。按 `reviewing-work` 的 Task Unit／Code 判据独立检查实际 diff、相关原方法、正式测试与本机日志；未发现本范围内未解决的阻断缺陷，不代替 T-007／T-008 或 Spec B 最终验收。

- 三条消息均为唯一 ProtocolCommand 路由，handler 只发送完整命令；两条目标 fine 类型／注册已删，其余塔 fine 未误报闭合。普通 Enter／Back 的 roster、currentness、intent、一次性返回和逐成员次序保持。TownLeaveMany 原算法只抽取一次，旧适配与新 whole 共用；三产品文件纯准备 patch 可独立提交，whole 与 VC 故障生命周期变化应留在后续提交。
- Boss Release 是无 footprint 的 ordinary Write，Boss Back 沿用全 run 精确保护，Illusion Back 沿用空 selected 保护；两塔 footprint owners 与保护集合独立。复用既有 Party／Town 阶段，没有新增 Delivery 注入或嵌套 dispatch。Cycle 保留无事务的内存意图准备与普通 InvalidData 内容拒绝。
- VC-B11／B12 与实际测试断言相符：Cycle scope 故障在 true ACK 后保 cause 关闭原物理来源；Release 编码与两塔 Back scope 用例直接观察真实 DomainTransaction.Committed、原 cause／来源链和 replacement 隔离。Back 新路径真实 peer 收一条 host 帧且 CloseCalls 为零；Boss 全员 Fence 不等于全员物理关闭。NoReturnView／CompleteView 和受控 transport 限制已明列。旧三例 3/3、新三例 3/3、旧普通路径 12/12 的 TRX 已核。
- 独立解析最终 TRX：App 5480 pass＋2 原 Dimension 内容失败＋16 skip，focus 19/19；Protocol 1781、Networking 203、Host 203 全通过。按 display／status／重数重新比较 TASK-41，App N17/D0，其余 N0/D0；build 日志为零警告错误。核读 DFO 记录摘要、strict／八坏例 selftest、runtime capture，并独立逐值比较台账：仅指定五行变化，original、非目标对象与顺序保持；diff-check 通过。未例行重跑全套。

保留限制：Networking r2 首次 ConnectAsync 的 ConnectionRefused 确实存在，后续单例及原并行全项目通过只能支持当前检查通过，不能证明根因已修复；报告未将它列成通用豁免。仍需后继簇尾八项目验收，未声称 A21 实机通过。评审只追加本段，未改产品／测试／Spec／ledger 或 index／commit。

## Controller Consumption R1

主控接受独立 R1 的 approved，已阅读实际评审、实现与三条补充故障用例，无待处置阻断。独立解析最终四份 TRX：7685＝7667通过＋原两条Dimension失败＋16跳过；逐display／status／重数App N17/D0，其余N0/D0。直接核DFO实际71条及各项目outside／exe／orphan均0，重跑strict与八坏例selftest通过，逐对象核台账仅五目标、original与非目标保持。补充故障测试中真实peer host发送旧0／新1，peer不物理关闭；主控此前误读被替换的fixture计数器已核源码并纠正，不据误读推断BestEffort丢弃。保留Net首轮未唯一归因、受控投影／transport与非八项目fresh限制。先本地提交三文件TownLeaveMany纯准备，再提交whole及VC-B11／B12；完成后继续T-007，不执行外部发布或分支清理。
