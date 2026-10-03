# TASK-04 — 协议入口、结算展示与精确回城

## Task Unit Contract

- Revision: 1
- Outcome and references: T-003 与 T-004 网络返回部分；RQ-004–009。真实注册入口驱动现有迷妄领域对象，逐层投影、一次奖励展示、主动/超时回城和断线清理闭合。
- Owned boundary: Protocol/Data 的塔消息、CMD、endpoint delivery、既有入口最小分流；必要领域查询/返回计时状态与 SelectedSessionCleanup 接线；对应自动化测试。不得用普通 DungeonCell 或普通奖励替代塔实现。
- Dependencies: TASK-01/02/03 完成的内容、动作和真实奖励 receipt；研究文档中的 ClientStatic/70Reference 布局。本计划已准备，只有TASK-03结果验收后才开始产品写入。
- Required checks: 各编码器字节向量、009F 畸形输入、真实 DI/注册/派发路径；选塔不走普通布局；过期身份/房间/帧不变更；全加载后启动；清场前不换层、最后层不再换层；成功/失败 receipt 投影不重新发奖；队伍稀疏槽；回城定时捕获旧run不可结束新run；普通入口回归。

## Detailed Plan

1. 遵循现行协议切片约定，在对应目录定义 008E/008F/0090/0091/0092 通知及 009F 请求，补 GameProtocol、MessageType、ProtocolModule 的 decoder/encoder/handler 注册。008F 每演员为 listIndex/u32、uid/u16、template/u32、level/type/box/boxIndex 各u8，地图物品 count=0。0090 必须编码完整305字节合法基线，不截成仅首字段；0091 为每接收成员的结算XP与四个真实队伍槽的候选组，空项与实例add_info按补证；0092 是继续挑战资格。
2. 跨切片组合放在 Game endpoint 层 delivery，避免一个新切片直接引用其他切片业务。CMD handler 只注入 DomainService/DomainStore（及允许的 TimeProvider），调用塔用例；不得在 Protocol 中解析PVF、决定奖励或直接写资产。
3. SelectDungeonHandler 的0010在已有选图意图/队长检查后、普通布局解析前分流。用领域查询判断是否为受支持的塔，不把11001业务知识散在处理器里。构建精确session、真实party slot；进入期间使用统一投影锁→按账号排序的 selected-session transition locks，提交前重新校验。普通入图行为保留。
4. 塔入场投影保留必要 USERINFO、ACK、成员状态、UDP host/support 等普通入图前置，改发008E+008F；门票扣减投影消费实际debits，不扣普通疲劳，不发普通DungeonInfo/StartMap。失败的网络投影不能重新发奖或静默假称回滚门票。精确失败连接的关闭放在账号锁释放之后，避免cleanup重入死锁。
5. 共享0025加载、0027死亡、0028角色死亡、002D移动、0048结果选项、002A放弃、0084回城，在普通逻辑前识别塔归属并委派endpoint。009F mode1启动，mode2为清场/换层信号。换层目标由dgn自动选择，不接收客户端任意层；仅服务端清场事实允许前进。最后层 kill 已完成结算，不等客户端不可信字段发奖。必要死亡ACK/通知和001E继续沿用已注册消息。
6. 每次动作在await前捕获精确Session+RunId+RoomVersion，再在锁内重验。真实网络 Metadata.InboundOrdinal>0 且 !SequenceIsFresh 的请求在绑定当前挑战/消费意图前丢弃，防止旧连接帧绑定到重入后的新run；Empty metadata仅供直接测试。无全局协议RoomVersion字段，不能宣称任意新sequence伪造旧payload可被识别。UID在同挑战全程不复用。
7. 新接受击杀投影死亡、各成员XP与任务变化；最后击杀可同时带即时XP及结算XP，保证两个已提交award都被表达，最终显示的等级/总经验一致。结算以既有receipt展示0090→0091→0092，并投影真实库存、金币余额、经验/任务更新；不在网络层重新生成候选。失败同理，实际通过层数与奖励计算层数分别使用。
8. 0048 state1 option0/1/2依70证据分别3/3/1秒后回城；option3保留结算界面；未操作60秒回城。计时选择/到期状态由迷妄dgn持有，常量政策由塔规则表达。增加最小领域选择/到期查询用例和可确定性测试的sweep，Protocol定时服务只驱动投影与用例。回调捕获run身份，重验到期与精确session，不能按character找新run并结束。不要把option0自动重进塔；回城后新入场重新检查并消耗门票。
9. 回城投影使用现有TownAreaProjectionDelivery及当前角色/位置，先在事务中解除塔成员归属，再回城；主动放弃可单成员，结束选项按当前塔队长及存活精确成员处理。SelectedSessionCleanup在党队清理之前解除精确塔成员，即使旧session已经撤销也应清理。成员离开使剩余全死时保留其结算receipt；不得漏投影。其余Party操作/城镇限定能力的跨模式审计在下一单元处理。
10. 测试既有公共入口而非仅直接调用编码器/领域替身；生产DI必须解析实际奖励服务、塔handler及timer。测试只用隔离SQLite/假连接，不启动用户Host或客户端，不改玩家数据库；所有.NET与其它写入者串行。任务完成记录实际命令和结果，不提交Git。

## Evidence and boundaries

详见 [奖励与投影补证](../reward-and-projection-evidence.md)、[总体研究](../../../IllusionTowerResearch-20261003.md) 与 protocol 中 NOTI-0x008E–0092、C2S 0x009F 记录。第一层保留全员加载门；70立即001E不是A21实机必需证据。0090/0091即时顺序与70阶段timer的时序差异作为human test list项目。不可将服务器fixture通过提升为ClientVerified。

## Result — round 1, 2026-10-03

- Changed: 新增 008E/008F/0090/0091/0092 NOTI 与 009F C2S 的消息、codec、注册和 handler；各协议位于独立目录，共享 `IllusionTowerCommands` 与 `IllusionTowerProjectionDelivery` 上提到 Game endpoint 根。SelectDungeon、FinishLoading、MonsterDeath、CharacterDeath、MoveMap、LeaveDungeon、DungeonClear 与 0048 EPLP 入口在普通路径前按塔归属分流。入塔沿用必要用户状态、ACK、UDP host/support 投影，改发 008E/008F，并表达实际门票 debit。
- Changed: 塔动作捕获 Session/RunId/RoomVersion，在 run 独占门后按 AccountId 排序获取 selected-session transition 门并重验；过期 sequence 在消费意图前拒绝。全员加载、启动、击杀、逐层推进、最后层结算、死亡/失败、主动离开、断线清理及超时回城均通过现有领域命令驱动。网络投影按接收成员隔离失败，连接关闭在门释放后执行；后台 sweep 隔离单个 run 与单个接收者失败。
- Changed: 0048 state=1 的 option 0/1/2 更新领域回城期限，option 3 保持 `ReturnDueAt=null`；选择提交成功后立即返回 0048 ACK，009F 不返回该 ACK。新增 `ReprojectCompletionAsync`，只从已提交 receipt 重放结算帧，不调用奖励或状态 mutation；真实 SQLite 用例令首次发送失败后重放，金币、经验和装备数量均保持一次写入。
- Changed: 新增 `ProjectCommittedDepartureAsync` 作为后续 party 流程可复用的已提交离塔投影接口。调用者先完成 `Leave`；接口自身按 run→排序账号门重验，只向仍属于相同 run/version 的剩余成员投影 Ready/settlement，并且只把仍是当前 exact session、且未进入任何塔的指定离塔成员送回城镇。`LeaveAndReturnAsync` 在提交并释放原门后调用该接口，断线队列也委托该接口。
- Why: 已提交领域状态不能因网络失败重新执行奖励或伪装回滚；receipt 是重放的唯一资产事实。关闭失败连接可能同步进入 selected-session cleanup，因此必须在 run/account 门释放后关闭。回城回调若只按 CharacterId 查找，会误结束重入后的新挑战，所以返回与延迟投影均携带并重验精确身份及 run/version。
- Checks: [TASK-04 bounded verification](../verification/TASK-04-verification.md)。Application Illusion Tower 44通过、0失败、0跳过；Protocol 塔与 SelectDungeon 43通过、0失败、0跳过；真实 TCP 009F 隔离集成1通过；CommandHandlerArchitecture 13通过；Protocol 全量1192通过、0失败、59个既有PVF条件跳过；Protocol构建0警告/0错误；`git diff --check`通过。
- Remaining: 本单元完成服务器协议与投影边界，未做 A21 实机验收，008E–0092 的最终客户端表现、0090/0091时序、option 0/1/2/3 与60秒返回体验仍按 [human test list](../human-test-list.md) 集中验证。70Reference/ClientStatic与服务器自动化不能提升为ClientVerified。未启动生产 `Dfo.Server.Host`、未部署客户端或服务端、未访问玩家数据库；真实009F测试在临时端口内用 `HostBuilder` 启动了隔离Host与临时SQLite，测试结束即释放。

### Stable protocol consumer contract

- `ReprojectCompletionAsync(TowerActionContext, CancellationToken)` 只接受仍匹配 exact Session/RunId/RoomVersion 且已有 settled receipt 的挑战；它可重复投影 receipt，但不得调用奖励能力或再次写入经验、金币、物品。
- `ProjectCommittedDepartureAsync(TowerActionResult, SelectedSessionIdentity?, IGameServerMessage?, CancellationToken)` 只消费已经提交的离塔结果，不执行 `Leave`。剩余成员投影与可选回城都在 run→排序账号门内重验，发送失败记录后在门外关闭对应连接。
- `TowerActionResult.SettlementCreated` 只在本次动作把未结算结果推进为已结算时为 true；普通重复动作不得再次主动广播结算。显式恢复发送必须调用 receipt 重放入口。
- 0048 option选择的 ACK 与延迟回城分离：成功选择立即 ACK；option3没有计时返回，option0/1/2及默认60秒由领域期限和 sweep 驱动。009F仅负责启动/换层信号。
