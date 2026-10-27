# TASK-23 R1 局部映射及实证边界

当前结果只交付 B3/P4 T-004 中 logical65554/wire0012。一个 GameDeleteItemCommand<bool> 接原精确 DeleteItemRequest、Dispatch 与 Metadata；三个 fine 类型、处理器、文件及注册删除。handler 的唯一 bus 调用、Protocol route/CLR闭合由原全图注册与架构规则检查。无 Main、NonRequest footprint、ledger、attempt、replay、新 raw许可或 WriteResult 恢复。

| 原边界 | 新边界 | 直接证据 |
|---|---|---|
| 未知op：不查session/不映射/不事务，原wire槽拒绝 | whole同序，无Write | 原强无效shape参数保留、真实 wire helper；前置映射故障无Attempt/零资产变化 |
| op1只有ToBusinessSlot窄catch；ToGameSlot在catch外；随后session/online | 完整原序，无reserved/safe新门 | whole代码、两reserved/safe真实旧新成功向量；Protocol三个unmapped实际whole执行mapper、null其它能力被触及即失败 |
| op1 fine IAccountCommand/Transactional，无selected/settlement marker | ordinary Write([],Ordinary,None) | actual ObserveUnits检查NonEffect/empty声明；真实domain/clock同一open非只读Tx；正常拒绝Committed |
| carried discard有receipt后同Tx QuestHoldingsChanged | whole action接原Quest服务，无receipt或shared不报告 | 真实最后一件任务trigger0→1、partial无变化单ACK；quest observer Same Tx，原领域暂存快照1（SQL准备阶段再刷），真实deferredFK Commit失败后全列原样 |
| op2/3先session/entries/reserved/online，才mapper、原ConsumeAsync多costAPI | 只wire单cost进同一ordinaryWrite；domain接口保持 | 两op carried/shared/耗尽、无效shape参数、stale proxy材料静默；多cost原两参数及新超wiredomain全资产测试保 |
| 原材料预检所有来源→逐项扣款/Enqueue MaterialConsumed同Tx | 原domain全部只读；材料事件observer Same Tx | shared+carried先验不足无扣；已扣shared并发count2事件后carriedDELETE19/1811，shared/carried/events/deliveries全回滚 |
| 材料preparedAt在fine结束外DateTimeOffset.UtcNow | whole Write结束外原静态UTC，未换FakeClock | 实际projection时无ambientTx、PreparedAt在wireStart/actualUTC窗口内；所有精确帧字节旧新对应 |
| 原普通通知逐target Compose后一次preparedbatch，caller忽略Failed | 原composer/encoder整文件只读，同ICommandProjections/EventService实例 | discard ACK→可选资源→可选quest；材料ACK→carried绝对更新/清槽/shared余量；shared耗尽只有ACK。两通知各实际composer/actualencoder故障：Warning同cause、无batch/close、Committed资产不逆转 |
| 直接ACK编码失败，pre无Tx与normalCommitted区别 | pre原raw；normal默认typed PostCommitPublicationFailure | op1与op2各真实旧新encoder注错：inner Same、EndedAJob、完整Root/Parent/Depth/Source/SourceConnection/CommandType日志一致，物理sourceClose Single+Assert.Same并ReferenceEquals输出，ACK0/nofence/全资产不变。明确外壳变化，不normalize同异常 |
| actionSQL错误/原实际SQLite participant Commit失败 | 默认sticky外抛，不改拒绝/许可 | discard任务与material各action19/1811、native同participant deferredFK19/787/Commit栈/FK1/child>0/正常完成stage，原cause Same、ConfirmedRollback全列exact，去trigger同rawseq真实重做 |
| NonEffect同rawseq从当前库存重新执行 | 原普通重做，不去重/重放 | op1/op3连续扣2次，耗尽后同seq拒绝；原ledger CWT无此物理连接entry |

## 真正 before、观察器和对应比较

任何产品/AGENTS改动前，原11现存owned源逐byte HEAD、新whole absent；捕获203命令/173logical的source/runtime及pure family。仅新局部测试先落入原产品，真实registered decoder→dispatcher→原fine→实际domain/SQLite→同provider outbox/encoder跑 old-oracles04 69/69与原codec01 11/11。before-manifest43项封存后才迁产品，原driver在before-driver.cs。最初局部编译/通知快照观察器诊断保raw，没有算作TDD RED。

原非NUL git ls-files观察器漏192个C-quoted中文/旧TRX，初draft before-source.json保留作诊断；before-source-02在产品前逐路径current=HEAD实证14352=11owned+14341nonowned，两个canonical路径明确在verification且存在。原map不改写，最终只排canonical2键，14339其余原tracked逐SHA保持。此校正是观察账，不是产品行为变化。

30组真实旧新观察由task23-oracle-compare.py逐组比较：全列items/item_slots/characters/stats/容器参数/cargo、活动任务/clear flags/acceptance/tracked、domain_events/deliveries；BLOB保hex/NULL保null。same-run回滚无任何豁免；跨run仅items.created_at/updated_at逐行核真实UTC窗口/先后，以及EventService event_id/batch_id/origin_instance_id非零、分类型跨phase双射/Delivery FK关系；其他列/payload/time/count/82Bcore全值相同。原生失败transient事件和retry事件不合并身份。完整raw均保，具体结果见focused及final对应JSON。

新wire不含旧session身份。stale用同provider局部只读query proxy返回真实旧session，IsInWorld仍询实际切角后registry；这是原拒绝/静默业务路径证据，不冒网络字段证据。ItemDiscard原stale fine改真实domain+原显式事务；SkillMaterial原多cost/duplicate-location/stale改真实IMaterialConsumptionDomainService+原显式事务，两个参数、所有原强业务目的保留，无hidden批量wire或旧fine兼容层。单cost真实wire另有强保护。

## 有价值 mutation 与测试驱动迁移

protected outcome是op3请求1从shared2只扣1且MaterialConsumed.Count=1；数量分支意外+1会真损失资产/错误成就事件。原测试未同时观察完整库列/事件/实际wire/同provider，因此增加耐结构变更的资产oracle，其保护收益来自真实全链边界。结构搬迁不造编译RED。仅op3数量临时+1可编译，实际剩0/事件2先落output，再资产断言RED；op1/op2正控可达pass；finally恢复whole原bytes/SHA，同三个目标GREEN。material-mutation-result.json保完整实际资产与事件。

原ItemDiscard/SkillMaterial方法及InlineData保留；驱动改真实decoder并把setup/directdomain保在TestGameplay，wire本身不包TestGameplay自等待。Slot架构仅迁discard到whole.Input/wire GameSlot/domain BusinessSlot及MaterialCost BusinessSlot，六接口/receipt/其它协议/真实坏model拒绝保。Protocol codec只动原三unmapped driver与其局部outbox，纯19B/18&20拒绝、安全字段/encoder向量/原composer字节块保持。

## 错误消费与后继限制

只读当前NetworkPlugins.cs Game catch仍按GameplayJob.EndedAJob把已结束作业降Debug并return，由原作业outbox完成sourceclose；新old四ACK路径均直接断言EndedAJob及精确物理sourceClose，无新错误消费/关闭/围栏政策。CommandBus仍Drain/Close/scope cleanup/FailAsync默认判定，三具名raw表未加项；普通成功通知异常由原NotificationBus捕获Failed，不能称whole外抛。VC-B2的日志command identity由inbound:GameDeleteItem到GameDeleteItemCommand明确单列。

本Task只做三full+四freshcontrol；其它1800是Task20接受full库存，不冒八freshfull。套件资源不作单CMD2秒证明；T-012最终测量、Spec审查/Astra和用户分支收尾仍后继。RecordedOutbound精确关闭登记不是真实socket关闭或A21实机验收。执行者不stage/commit或remote，结果交fresh required独立快审，主控消费后checkpoint。
