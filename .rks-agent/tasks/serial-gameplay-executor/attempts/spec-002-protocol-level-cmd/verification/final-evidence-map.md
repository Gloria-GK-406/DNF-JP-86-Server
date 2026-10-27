# B 完整验收证据映射

对象为Spec B10、产品0c5acb958f673d3caf4352c3914960bed78a2c68。这里只组织已执行证据；完整独立门已在reviews/spec-002-final-review.md批准，最终验证以completion-evidence.md为准。正式八项目采用final-run-map明确组合，不把历史局部测试升格为最终运行。全部测试类定位于Server/tests，原始TRX、console、DFO位于本目录logs；各TASK与verification保留旧行为、实际生产边界和测试能力限制。

运行时捕获在TASK66最终生产token冻结后完成；随后只有空白与测试夹具补正，最终源码清单和Git产品绑定保留。正式构建与组合测试源哈希为2cd7b909da3d58d1166df489ba20e25b360ef9dae47c3c287a4c06f403b5c67c。运行库存173消息、159业务+14遥测、172命令、137实现；strict pending0，8坏例按原因拒绝。完整测试、Golden与DFO结论见b-final-report，性能见final-measurement。

## 17项需求

| RQ | 最终核验重点 | 证据位置 |
|---|---|---|
| 001 | 159业务消息唯一绑定、14遥测；薄handler不查状态／登记 | 最终runtime crosscheck；ProtocolCommandExecutionTests、CommandRegistrationTests、handler结构反例 |
| 002 | 73 typed来源／12原技术路由；取消、来源链、合并、FIFO、drain | canonical sources；TASK57／62～64 verification；最终来源/生命周期/事件测试 |
| 003 | 整用例在具体CMD、无共同基类、不转发另CMD；原分段可见 | TASK66源码／diff／结构正反例；ledger commands |
| 004 | executor上建立显式request、原物理身份／权威；singleton无请求ambient | CommandRequestContextTests、ProtocolCommandExecutionTests、ExplicitEffectExecutionTests；旧ambient源审计 |
| 005 | 三协议状态与TrustedInternal区分；closed／leaked不降级 | ProtocolNonEffectFootprintTests、DeclaredNonRequestFootprintTests、CommandBusExecutorTests及真实SQLite反例 |
| 006 | #185、Main、ordinal／replay、所有实际参与者current与精准失败保护 | ExplicitEffectCommandTests、CommandWorkUnitsTests、各实际ProtocolCommandFlowTests；VC各具名边界 |
| 007 | 204旧命令及其它自开事务逐项对应，不合并选角／补偿事务 | canonical actual_units/participants；事务tests、各flow；TASK61 |
| 008 | 已提交与回滚区分；Compose生产禁用不冒已修；事件边界 | CommandWorkUnitsTests、CommandSelfSettledWorkUnitsTests、DeferredOutboxCompositionArchitectureTests、EventConsumptionFailureBoundaryTests |
| 009 | 封闭具名注入、无sender/queue/bus/content/规则/locator/null/Func能力 | CommandHandlerArchitectureTests及其拒绝反例；TASK66实际扫描 |
| 010 | App25自有读写池清理、类并行／期限／skip不弱化 | TASK01，最终源码与八项目运行身份 |
| 011 | F127只降写端完成／已断日志，正常故障及业务结局保持 | TASK01实际transport对照＋最终Networking |
| 012 | 6请求ambient、5pipeline、oldfine/routes及8过渡清零 | TASK65删除记录；最终source检索／runtime／strict、LockRemovalSourceTests |
| 013 | 18F加F128①／变长字段各有结果或准确保留 | 最终报告交接表、task66-documentation；F131／F132限制 |
| 014 | A持续不变量及会拒绝的反例不减；执行器单Error与独立错误分开 | 最终DFO、旧删除用例去向、规则反例、TASK65日志分类 |
| 015 | fresh Release 0/0，最终八数量／failure／skip准确；10Char／15Golden | 最终source freeze、native raw／TRX／inventory、static audit、DFO；不称零失败 |
| 016 | 相同G1/参数15场、每条CMD≤2秒；分位／max／吞吐／wait／冷启 | 最终freeze、run-meta、原summary、A/B比较、环境sampler、停止门检查 |
| 017 | AGENTS／架构哲学／新增协议与login文档，完整变化／限制／报告 | TASK66 docs；最终B报告／评审／本地seal，禁止外部操作保持 |


## 11项接受条件

| AC | 实际证据与边界 |
|---|---|
| 001 | RQ001～003/009；最终runtime173/159/14与172/137，strict五账闭合、8反例；真实注册与结构正反例，TASK66 |
| 002 | RQ004/005；显式request、三协议状态/可信内部、closed/leaked拒绝与真实SQLite非效果反例 |
| 003 | RQ006；#185/Main/ordinal/current/footprint及精确保护，真实unit/各簇flow和F88/F93反例 |
| 004 | RQ007/008；204旧命令actual_units/participants逐点表、选角分段和补偿不合并；真实事务/rollback/自结算测试 |
| 005 | RQ004～009/014；VC-B1～40具名变化及失败、编码、清理、投影顺序与精确来源；各Task强flow/物理wire证据，不以组件spy替代 |
| 006 | RQ010/011；TASK01旧新transport及App25自有池清理；最终Networking/App保持类并行、期限与精确skip |
| 007 | RQ012；TASK65清零及TASK66结构，最终无旧模型/转发替代，常设AsyncLocal分类明确 |
| 008 | RQ002/013；73来源/12技术保留，F07与两塔真实FIFO/水位/排空、18交接表，F131具名保留 |
| 009 | RQ014/015；fresh build0/0，9817=9797/2/18，原失败/skip身份，10Char、15Golden、DFO68+7controls，测试库存与43去向 |
| 010 | RQ016；final-measurement，16原工具场，最大356.0821ms；真实下降/污染/默认无样本及合成局限披露 |
| 011 | RQ013/017；TASK66文档、LOGIN例子、40VC/交接/六根条件、b-final-report；完整review、completion-evidence与spec-result承载各自门；根状态另见根map |

## 六项根条件

| 根条件 | B证据归属 |
|---|---|
| SC-2 | AC001/002/007 |
| SC-8B | AC002/003/005/006/008/009；A不变量和已接受残余持续 |
| SC-9B | AC010 |
| SC-10B | AC007/011 |
| SC-11B | AC011 |
| SC-13 | AC003/004/005/007 |

A原封存证据不修改。集成评审须复核唯一executor/queue、outbox提交后发送与失败隔离、删锁、Reliable/LiveOnly、深度/调用链、依赖投递结果承接；不是用B五账数代替这些不变量。A已披露CH1/CH2、F131精度、F132禁用Compose、性能未达原目标与人群/实机边界继续保留。根map只有在B seal后才能记这些六项satisfied。

## 40项有意变化

下表仅索引，精确许可范围以spec.md对应VC整行为准；没有把VC-B40外推为正常业务拒绝、事务点、帧序或受众可随意改变。各TASK验证文件和最终正式TRX联合支持持续证据。

| VC | 变化主题 | 实施证据入口 |
|---|---|---|
| B1 | 已完成写端／断开发送失败的日志级别 | TASK-01 |
| B2 | 统一 CMD 作业身份与重复 Error 收口 | TASK-04、65 |
| B3 | 日切／配额逐精确会话 FIFO，允许批间交错 | TASK-63 |
| B4 | Cera whole 默认失败与精确买家保护 | TASK-28、29 |
| B5 | Mailbox 不可恢复失败、原空保护范围与自回滚原因图 | TASK-30、31 |
| B6 | 租赁健康 NoRoom 修复与 opening／rental whole 边界 | TASK-32、33、35、37 |
| B7 | 宠物改名／孵化、共享效果的 whole scope | TASK-36 |
| B8 | 租赁首次 required batch 编码默认失败 | TASK-37 |
| B9 | 恢复虚弱、日复活币的精确默认失败边界 | TASK-40 |
| B10 | Teleport 首次库存编码与 whole scope | TASK-41 |
| B11 | cycle intent 准备的 scope 位置 | TASK-42 |
| B12 | Boss ReleaseForSelection 后 required 编码 | TASK-42 |
| B13 | 普通／两塔入场的 whole 提交后失败 | TASK-43 |
| B14 | 入场清理绑定原物理连接，隔离同 ID replacement | TASK-43 |
| B15 | 加载／换房／换层的 whole scope | TASK-44 |
| B16 | 两塔加载清理的精确物理关闭 | TASK-44 |
| B17 | 普通／两塔击杀的 whole 失败边界 | TASK-45 |
| B18 | 两塔击杀清理的精确物理关闭 | TASK-45 |
| B19 | 拾取 whole scope 与原各塔保护集合 | TASK-46 |
| B20 | 两塔拾取清理的精确物理关闭 | TASK-46 |
| B21 | 死亡／复活的各自事务与 whole 失败 | TASK-47 |
| B22 | 两塔死亡清理的精确物理关闭 | TASK-47 |
| B23 | 结算／翻牌 whole 生命周期及不重复结算 | TASK-48 |
| B24 | 删除同作业内无推进依据的自动翻牌三次重试 | TASK-48 |
| B25 | 普通 EPLP 原 End、登记及 scope 先后 | TASK-49 |
| B26 | 两塔结果选择、逐成员独立结算与 whole scope | TASK-49 |
| B27 | 祭坛退出实际报告者 physical 保护 | TASK-49 |
| B28 | 技能／属性／经验票等六入口 whole scope | TASK-50 |
| B29 | LearnSkill／IncreaseStatus 首次编码默认失败 | TASK-50 |
| B30 | 接／弃／触发／完成任务的 whole scope | TASK-51 |
| B31 | 四任务入口首次直接 ACK 编码默认失败 | TASK-51 |
| B32 | 0x0204 三种消耗品的 whole scope | TASK-52 |
| B33 | 称号／成就／外观／周签到的具名默认失败 | TASK-53 |
| B34 | 设置／已读六入口的 whole scope | TASK-54 |
| B35 | TaggedId／耐久／Premium slot3 的 whole scope | TASK-55 |
| B36 | 购买次数查询响应与 scope 先后 | TASK-56 |
| B37 | 两塔真实内部回城的合法 footprint 来源 | TASK-57 |
| B38 | 两塔扫描／逐离场／逐 run，允许 FIFO 交错 | TASK-57 |
| B39 | 两塔内部逐项 whole scope 的累计精确保护 | TASK-57 |
| B40 | 冲刺阶段基础设施失败统一默认收口；业务差异仍单列 | TASK-58～65，用户流程 §8 |

## 18项交接与另列事项

| 项 | 已实施或保留边界／边界 | 证据入口 |
|---|---|---|
| F-07 | 日切／配额scan及精确session子CMD、FIFO交错、水位只随真实接受、整轮drain | TASK63 verification，最终Application调度测试 |
| F-12 | 全部原事务点到显式单元，不能用204数量冒逐点等价 | canonical ledger actual_units／participants |
| F-34 | 协议级CMD及显式context，五类迁移归属闭合 | ledger／logs/final-runtime/runtime.json |
| F-58 | closed/leaked请求不得转成可信内部，真实泄漏判据与source反例 | TASK65 verification；ProtocolNonEffectFootprintTests／DeclaredNonRequestFootprintTests／CommandBusExecutorTests |
| F-69 | 从A8350到最终逐display库存、真实测试耗时及环境；耗时不作2秒CMD门 | 最终八run.json／A原console |
| F-86 | 删除无生产推进依据的3次AlreadyFlipped重试；当前作业内不能等后作业释放 | TASK48 verification／VC-B24 |
| F-88 | selected-not-world／wrong-generation prior lease两个旧保护先红绿再迁移 | ledger两protection行、TASK02／40／41 |
| F-93 | 精确Discard不清替换世代、捕获后替换不消费ordinal | ledger两protection行／TASK02／65 |
| F-109 | 删除representedSessions空校验参数，保刷新结果 | TASK41／migration-ledger.md |
| F-110 | bus作业开始建立outbox与精确source，无登记错误也关闭；删dispatcher手工Current | TASK65／CommandJobEntry／wire |
| F-112 | 实际业务flow同provider真实单位与outbox，不把component fake计作提交证明 | 各ProtocolCommandFlowTests／最终八 |
| F-122 | Registered命名闭合；摘要不等物理送达，另RegistrationAccepted保真实接受 | TASK27＋TASK63，NotificationOutcome及登记结果测试 |
| F-124 | 精确PvP notification/handler/composer绑定，缺失／合法错handler反例 | TASK61 verification／结构判据 |
| F-127 | 仅已完成写端／断开分类降日志，不改Failed、Resolve、通告或普通故障 | TASK01旧新transport证据 |
| F-130 | App25处自有规范化写读池清理，类并行不关闭；不宣称全部SQLite偶发根因 | TASK01旧新及最终八 |
| F-129 | Channel已执行失败Debug、未入队拒绝Error，真实TCP及复原变异 | TASK04 r3及最终Networking |
| F-131 | 明确保留：登记close尚未执行、传输先断仍继承登记链；不是唯一物理原因证明 | TASK61／64、Spec RQ013、最终报告遗留 |
| F-132 | 生产Compose禁用且IL正反例守护；未修机制本身 | ledger protection／Host DeferredOutboxCompositionArchitectureTests |
| F-128① | 当前MId/Ticket、GameLoginCMD文档；票据内存与launcher凭据持久化区分 | Document/ProtocolCommands.md |
| 无编号变长扇出字段 | 6decoder/5field已有具体边界，不凭空加generic string guard或改线上值 | task66-documentation／decoder差异审计 |


## 最终证据入口

- `logs/final-g1/`：原始16场、冻结、参数对照、分类/采样、primary/pooled/environment/ramp对照和final-run-map。
- `logs/final-runtime/`：实际注册捕获及source交叉检查；`logs/final-doc-audit/`：最终decoder声明核对与账路径历史。
- `logs/task66-manifests/`：最终2862源项、r2/r4差分、focused结果；正式各run目录保留失败轮。
- `logs/task66-final-r4-main-inventory.json`、`logs/task66-final-r4-main-dfo-*.log`：已重新读取准确failure/skip/negative/control。
- `logs/final-g1/final-static-audit.json`和final-characterization-diff.patch：15 Golden原字节、10表征；人工消费diff仅注释/说明改名，无掩码或断言放宽。
- `logs/final-g1/a-to-task66-final-r4-inventory.json`、`logs/final-removed-test-map.json`：最终43项与TASK65逐身份比较相同；8350+1510−43=9817，TASK66仅+7结构case，43原显示实例替换/改名沿TASK65逐项去向，无新增删除。

报告的“有证据”保留每项证据的实际强度：组件spy、真实领域事务、注册执行器、物理连接和线级分别承担其范围。2条Dimension失败及18skip是唯一明确测试接受边界；TitleQuery真实ReadOnly拒绝并未修复。八项目计时是测试运行器报告时间，不能当CMD耗时或同口径内存比较。

## A持续不变量的正式测试对照

以下逐类从本轮正式TRX重读，全部Passed；数量是显示实例，不等于DFO负例数量。语义和断言强度仍由当前源码/历史diff与完整独立评审核对，不由这个统计替代。

| A条件 | 本轮直接对应的正式用例 |
|---|---|
| SC-1 | GameplayExecutorOrderTests（4）；GameplayExecutorSelfWaitTests（5）；GameplayWriteAssertionTests（52）；GameplayExecutionRegistrationTests（3）；GameplayJobHostClosureTests（1） |
| SC-3 | GameplayOutboxTests（14）；OutboxWorkUnitTests（4）；OutboxWireTests（6）；OutboxExplicitDeliveryTests（3）；OutboxRegistrationTests（10） |
| SC-4 | LockRemovalSourceTests（18）；GameplayStateCensusTests（6）；GameplayWriteEntryTableTests（9） |
| SC-5 | DomainEventDispatchTests（40）；DomainEventSchedulingTests（1）；EventConsumptionFailureBoundaryTests（7）；DomainEventEntryTests（22） |
| SC-6 | GameplayCallDepthTests（7）；LifecycleCallChainTests（5）；GameplayExecutorLoggingTests（3） |
| SC-7 | TimerRegistrationAcceptanceTests（3）；NotificationDeliveryTests（17）；SelectCharacterAcceptedTranscriptTests（11）；SelectCharacterCompensationCharacterizationTests（4） |

`logs/final-preseal-proof.json`同时重算2862项源码SHA，确认与正式r4源码相同；A原Spec/Plan/Tasks/Reviews/Verification与A子目录的Git差异只有原已授权B草案迁出（明确排除项），A封存证据本身未改。根契约自B基准不变，子契约自规划检查点不变。

## 完整审查消费

独立Astra完整B verdict为approved，A+B实质集成通过；CH-1静态确认但低风险非阻断。主控按其最低披露要求，将action内已请求取消可能记Error及未有专项native探针的边界原文纳入b-final-report，未改产品、取消资格或任何测试断言。此文的状态措辞和证据链接同步不是新验收条件；最终verification及B seal仍须以实际门为准。
