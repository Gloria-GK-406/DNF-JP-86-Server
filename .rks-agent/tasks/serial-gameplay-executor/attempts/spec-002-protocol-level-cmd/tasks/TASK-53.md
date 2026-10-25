# TASK-53 — 称号簿、成就与周签到入口归并

本 Task 承接 T-007，闭合 StoreTitle（65948）、WithdrawTitle（65949）、AchievementTrigger（65953）、QueryTitleBook（65960）、SetTitleAppearance（66105）、WeeklyAttendance（66625）六消息及七个原 fine，共十三行。TASK-52 已获唯一 Astra 批准并由主控接受，以6653e095／2a9a09a6分段本地提交，工作区干净。目标未偏离：保称号实例／镜像、成就事实和签到邮件的原事务及ACK顺序；相关可靠事件和日切来源仍留后继。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-52；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 五个唯一 Protocol／Direct whole（Store／Withdraw具名共享同一mutation类型），六薄入口；删除 StoreTitle／WithdrawTitle／EquipCarriedTitleReplacingMirror／QueryTitleBook／SetTitleAppearance／ReconcileAchievement／ClaimWeeklyAttendance 七fine及仅有编排。所有写均普通事务、精确原selected保护、None footprint；Query仍普通ReadOnly保护请求者。没有Main／效果ledger／raw／Self。
- Nature/Gates: 保持行为迁移；whole默认生命周期若产生有意可见差异，先以正式旧新证据追加VC随同Task唯一fresh Astra快审，不改Spec B8／Plan P10。一个sol6.1执行、一个Astra结果快审，无挑战；核心／契约变化先交主控。纯测试准备独立提交，不外部收尾。

## Detailed Plan

1. **先钉旧行为。** 基线2a9a09a6先跑 Achievements下TitleBook／Mirror／StateEvent／QuestUnlock强例与Protocol TitleBook／Appearance／AchievementWire／WeeklyAttendance，以及Host的GmEventsHttpTests（含真实weekly decoder／dispatcher领取邮件）。补真实注册decoder／dispatcher→SQLite／production outbox与实际encoder／DI Dispose最小正式旧新判据；覆盖真实称号原件／镜像交换和回滚、成就只读现有事实不接受客户端delta、签到claim＋MailOnly同事务／重复不发第二封、Query跨角色的请求者保护。保既有完整实例属性／ownership／源UID／封印与满包断言，不能换成假bus只验调用。
2. **称号三种mutation。** 保完整20B及space／slot／category／index前置门，Withdraw的TBM1（0x314D4254）只走原EquipCarriedTitleReplacingMirror，保持拒绝的3／13坐标。原slot mapping在事务外且在原catch范围；同one ordinary WriteResult [原actor]／None承接原领域调用，窄action catch以同cause RequireRollback，健康native Commit回滚映原0x0A，取消／递归typed／guard／cleanup默认传播；确认回滚后才写原Warning。结果ToGameSlot仍在原catch之外，不扩大恢复。称号／镜像真正能力留原领域服务，可靠事件仍原提交后队列，不提前改event来源。
3. **查询与外观。** Query仍只读事务，participants只含请求者，不能把被查目标加成受保护参与者；保owner16、listener、目标InWorld前置及请求者／目标双重投递前重检，无效静默。Appearance保完整4B、非负ID与原session／online门，一ordinary Write [actor]／None；原普通action／native错误不映业务拒绝。原无库存移动、ACK字段、事件与当前投影不变。
4. **成就与签到。** Reconcile仍一个ordinary WriteResult [actor]／None，客户端三个delta仅观察；保原普通失败日志与nullable ACK，缺session／坏请求也回原ACK。Weekly只按session进入，不增online／InWorld门，clock在原事务内读取；一个ordinary Write [actor]／None，claim与邮件仍由原领域服务同事务结算，失败照原传播。随后TheseSessions先AttendanceCompleted再EventsChanged，原Project Failed忽略语义保持，不升级required。事件投递／状态订阅无新CMD嵌套，不关来源行。
5. **实际失败与闭合。** 正式旧新覆盖action与native Commit、取消／递归typed、真实scope／初次编码、精确physical与替换隔离，并按原参与者分别证明默认保护；有意变化只写已观测边界。所有6消息唯一binding，七fine实际生产引用／类型／注册清零，旧强例适配真实新入口；孤立算法／投影fixture明确范围。无新能力或白名单；仅十三行ledger，AGENTS清单一行，必要VC，Spec／Plan其他不改。
6. **冻结交付。** Release solution非增量0警告0错误，聚焦及Application／Protocol／Networking／Host全量（Host已有真实weekly入口回归）；如其他实际共享影响扩大则据实加项目，核心改动先回主控。项目严格串行、原类并行／5m／detailed／独立DFO，核真正child end／exit及逐display／outcome／重数，对52基线保两项Dimension与18有效skip。最终旧源复验须先备完整CURRENT、锁HEAD／branch／owned与全部runner结束，由主控读完整脚本后执行并finally逐文件恢复，再fresh构建与full。Result后strict／八坏例／含新文件diff-check并停写，交唯一fresh Astra；主控接受后本地分段提交并继续T-007。

## Controller Boundary Note

旧正式表征发现 QueryTitleBook 的真实领域读取无法在当前 ReadOnly 事务中完成：`TitleBookDomainService.ReadClientProjectionAsync` 经 `SqliteItemDomainStore.SessionIn` 无条件登记 `ItemLifecycleScope`，后者不支持 ReadOnly，触发原 `DomainTransaction.Enlist` 拒绝。主控复核源码后裁定：本 Task 保原只读单元与相同失败，不改底层事务或以 Write 绕开；真实原域判据直接守原失败／回滚／无成功帧，另具名投影替身仅验证目标门与投递，不冒真实称号查询成功。此缺陷作为本地后续事项披露，不新增全量失败豁免、不改 Spec／Plan，也不写外部 issue。


## Result

- Outcome: done
- **交付：** 六消息改走五个唯一 Protocol／Direct whole，Store／Withdraw共用称号mutation；七fine及其handler／注册／生产引用删除。仅十三行迁移账闭合，全部original、行序与其余行保持；AGENTS清单增一行。原普通事务、精确请求者保护与None足迹、称号实例／镜像／槽位、成就事实、签到MailOnly同事务和两次投影顺序保留；可靠事件和日切来源留T-010。
- **非常规选择：** 未修生产Query的既有ReadOnly生命周期登记缺陷，真实旧新均相同原因回滚／无帧；QueryRows只守只读协议门与帧，EventView省略其他活动，Host原真实weekly入口补完整投影覆盖。whole默认scope／初次编码／结果地址换算差异仅追加VC-B33；不增加raw、Self、Main或机制。纯测试准备单独交主控提交；这些默认生命周期差异随whole归并不可再拆成独立业务算法修改。
- **检查：** 主控最终旧源复验App111／Protocol55／Host11全部通过，三child exit0，十九路径精确恢复。fresh非增量Release构建0警告／0错误；原类并行、5m、detailed、独立DFO/TRX且项目串行：App5753＝5735通过＋2原Dimension失败＋16跳过，Protocol1788通过，Networking203通过（r2），Host203通过。两失败为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，Message与52逐字相同。逐display／outcome／重数App新增45删0、Protocol新增7删6、Net／Host零变化；原6假fine ACK例改6薄委托，强行为由新App真实链和原66域例承接。DFO71＝67反例＋4control，额外／exe／orphan0；实际程序集／DI登记探针通过。完整证据见 `verification/task53-verification.md`；Result后strict／八坏例及含新增文件diff-check均通过。
- **遗留：** Net首轮唯一失败是OutboxWireTests首次TCP连接拒绝；混合日志有Bind48但无实例／端口归属，未证明根因。原参数单例和冻结全量复验通过，未修复或新增基线豁免。Query缺陷保留为范围外后续；等待唯一fresh Astra快审和主控提交。未作实机、专门性能验收、Git写入／外部收尾；产品与正式测试冻结，执行者STOP。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

独立结论：本 Task 范围内未发现阻断缺陷。审查对象为 `2a9a09a6` 上 TASK-53 Contract r1／Controller Boundary Note／Result 对应的十九个未提交路径；本轮只追加本评审，未运行 dotnet、改产品测试或执行 Git 写入。依据 `reviewing-work` 的 Task Unit／Code 判据、process-reset §3～4、handoff §3及当前 Spec／Plan 的相关边界，不替代 T-007 或 Spec 完整结果验收。

- 实际源码和差异确认六消息绑定五个 Direct／Protocol whole、七 fine 删除；普通写的精确 actor／None、Query ReadOnly 仅请求者参与保持。称号及成就仅 action 窄 catch 以原 cause 要求回滚，健康 native Commit 结果才恢复原拒绝；slot 输入换算仍事务前，结果换算仍 catch 外。签到仅 FindSession、unit 内 clock、claim＋MailOnly 原子，AttendanceCompleted→EventsChanged 两个 Failed 继续忽略；未加入 Main／raw／Self 或新机制。
- 已读新增 45 项正式测试与原测试差异，抽查原始 TRX／run、恢复报告、程序集登记及 DFO 证据：旧源 App111／Protocol55／Host11 全通过；最终 App5735通过＋2原Dimension失败＋16跳过，Protocol1788、Host203、Networking r2 203通过。两项 Dimension 名称与 Message 独立对比 TASK-52 一致。四 fresh 与四历史库存分开记述准确；未将 Networking 首轮拒绝连接解释为已证根因或已修复。
- VC-B33 与正式旧新断言相符：已提交写的 scope／首次编码／结果换算采用默认精确保护；Query 只读仍 raw、无新增围栏，成功投影接缝的 scope 帧时点变化明列。真实 Query 原有生命周期登记拒绝由独立用例守护，QueryRows／EventView 的替身边界已披露，未冒称生产 Query 成功或 A21 实机通过。仅十三个 ledger 对象变化，original 与其他行不变；可靠事件和日切来源未提前闭合。

保留的非阻断事项：真实 Query 既有 ReadOnly 缺陷、Networking 首轮连接失败根因未证，均由主控继续持有；本批准仅覆盖冻结结果和已披露的 VC-B33。交主控消费并按授权分段本地提交，评审者 STOP。

## Controller Consumption R1

主控接受唯一 fresh Astra 的 approved，TASK-53 通过。本轮独立阅读新旧五类 whole、注册、测试及 VC-B33，亲自完成最终旧源复验并核对十九文件恢复；再次按逐 display／outcome／重数核四项目全量、原两项 Dimension 的完整失败消息及全部 runner 串行结束，原 DFO parser 71 条、runtime 六绑定／七 fine 清零、strict 和八坏例均成立。十三目标之外的 ledger、所有 original 与行序保持，十四产品／测试文件仍与最终旧复验的 CURRENT 相同；只提交人写记录和产品测试，原始证据留本机。

接受范围包括已披露的 VC-B33，未扩为生产 Query 成功、网络首次失败根因已证或八项目新鲜全量。Query 的既有只读缺陷继续作为范围外事项；Networking 首轮连接拒绝保留原记录，以同冻结单例和全量复验为本 Task 的验证依据，不新增基线豁免。按已授权流程分开提交纯测试准备与 whole 迁移，随后继续 T-007 设置与通知入口；不执行外部收尾。
