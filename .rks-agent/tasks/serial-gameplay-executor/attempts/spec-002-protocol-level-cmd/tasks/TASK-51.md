# TASK-51 — 普通任务四入口归并

本 Task 承接 T-007，迁移 AcceptQuest（65567）、GiveUpQuest（65568）、SetQuestTrigger（65569）、FinishQuest（65570）及各自四条 fine，共八行。TASK-50 已获唯一 Astra 批准并由主控接受，以59ed0200／1788011a分段本地提交，工作区干净；已披露的测试调度异常有有效串行补验，不留产品返工。目标未偏离，本组保持任务状态、奖励结算与投影边界，复用现有单元和重放能力，不扩大恢复、raw、规则读取或跨簇契约。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-50；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 四唯一 Protocol／Direct whole 与薄 handler，四 fine 类型／handler／注册及生产调用清零。Accept／GiveUp／Trigger 保原普通 `[]`／None 写事务；Finish 保一个 Main、原精确 selected／physical 与最低 Inventory／Quests 重放需求。券、称号、特殊玩法及事件来源不在本项闭合。
- Nature/Gates: 保持行为迁移；whole 固有 scope／首次投递失败差异以正式旧新证据追加具名 VC，随唯一 fresh Astra 结果快审。一个 sol6.1 执行、一个 Astra 快审，无挑战；不修订 Spec B8／Plan P10。需要改变核心／契约时先交主控；纯测试准备与迁移分段本地提交。

## Detailed Plan

1. **先钉旧行为。** 基线1788011a先跑四切片现有 codec／handler／真实事务强例，连同 OrdinaryQuestEffect、MirrorAradQuest、CycleQuestFlow、SeekingQuestCompletion、CareerReconciliation、SouthernDaleCompatibility、MarriageEngagement及相关 CommonExperience。最小正式旧新 oracle 必须覆盖实际 decoder→dispatcher／SQLite／DI Dispose／encoder／production outbox、四条真实事务与资产／任务事实、原帧序、精确来源／root／physical／关闭原因；Finish 的成功同帧重放与实际重放 encoder、动作内晚 SQL、native Commit、初始 encoder、取消／递归 typed 分开验证。保健康回滚后的合法重试和资产一次；已真正关闭的连接不得假作仍可用。所有必要 fixture 和断言尽量在产品切换前收齐，已有强例不重复堆矩阵。
2. **三个普通入口。** 原 fine 都是 plain ICommand，不能因带账号／角色字段扩大 selected 或引入 Main。Accept 保 Crack／Wedding／Circle／Southern 分支、重复接受0x12、日志写入与事件物品 InventoryThenMail、无主背包后的原 GiveUp 正常返回、持有量进度与 Mirror 合并；不把原正常拒绝改为强制回滚。GiveUp 保 Crack 与普通任务簿路径、当前任务和 Mirror 投影；Trigger 保 ClientQuestTriggerReport、缺 ClientResponse 报错、Refused 与 NotCarried 回零的区别。ACK 与后续材料／进度／当前视图／offers 原位置和次序不变。
3. **完成结算与错误边界。** Finish 只把原一个结算段改为 Main，unit.Footprint 传原 completion.FinishAsync；奖励、消耗、金币、经验、任务、成长写仍同事务。原 fine 只在 action 内把普通异常包装为 QuestFinishRejectedException，原 handler 仅将该专用拒绝映 ACK22；native Commit 的普通存储异常在 wrapper 之外，不能仿照 TASK-50 扩成通用失败 ACK。用现有 WriteAsync 和窄 action catch／RequireRollback 保同一包装 cause、真实确认回滚后再回应；普通业务拒绝原样提交，取消／递归 typed／guard／cleanup 继续默认传播。最低 replay needs 不因某次发经验帧而扩大；原 committed replay owner 的 raw 资格先由旧正式 encoder 判据证明。
4. **原投影与归属。** 保先 published view、结算前时间戳；成长时重载并先发 SkillState，再 ACK；拒绝附带进度、Mirror 完成的早返回、普通 offers／当前点数与 NotificationExperience、材料／经验／外观／库存／可接任务／进度／Housing 及成长后虚弱投影均保原顺序。可见收件人保 Illusion 精确 run 或 Town／普通 run 已准入集合。只用现有 IDungeonRunProjectionDomainService 的同实例 Find 替代具体 registry 注入；虚弱的 IsInWorld→当前 Value→NOTI 可由本 whole 直接拥有原查询与登记，不注入旧 Delivery，不新增转发领域服务或第二份状态。其他实际消费者和来源保留。
5. **闭合与测试适配。** 四 whole 不注入总线、旧 fine／handler／Delivery 或任意回调。原 fine 的 ExecutionKind.Transactional 防误注册断言随被删除类型退出，改用实际 whole 的显式单元／注册边界证明，不能伪造 legacy context 来迎合旧测试。保原业务、codec、资产与顺序断言；原真实命令集成例仍走真实新入口，纯算法／投影 fixture 明示其边界。仅 Finish 声明原 CommittedReplayCurrentStateBatch，不授首次成功 raw／Self；实际程序集核四唯一 binding 与四 fine 零。
6. **冻结交付。** 非增量 Release solution 0警告0错误，聚焦与 Application／Protocol／Networking 三项目 full；按实际共享影响扩大，不无故重跑已有效项目。项目严格串行、原类并行／5m／detailed／独立 DFO；直接核 child run.exit 和自然 end，不把外层脚本0当测试结果，不复用已运行输出目录。对50有效库存逐 display／outcome／重数闭合，保原两项 Dimension 和18skip。若需最终旧源复验，先生成完整 CURRENT recovery patch、固定 HEAD／branch／owned manifest、检查全部 runner 结束，由主控读脚本并执行，finally 逐文件恢复。只闭八行，保 original／顺序／其他行；Result 后 strict／八坏例／含新文件 diff-check，停写交唯一 fresh Astra，主控接受后提交并继续 T-007，不外部收尾。

## Result

- Outcome: done（执行轮1，基线1788011a的本地未提交owned diff；等待唯一fresh Astra快审及主控接受）。
- Changed: 四个薄handler及唯一Protocol／Direct whole；Accept／GiveUp／Trigger各ordinary []／None，Finish单Main与原精确Recipient／footprint／minimum。四fine类型／handler／注册／生产调用清零，仅八行台账闭合；原算法、事务、奖励、成长与投影顺序保持。AGENTS仅表一行、Spec仅VC-B30／31，Plan未改。测试准备独立补丁`/private/tmp/task51-pure-preparation.patch`；whole生命周期差异随主体实现，不能与其scope归并拆开。
- Why: Finish保原动作内专用拒绝，用WriteAsync＋同cause RequireRollback，确认回滚完成才记录／回应；native Commit仍默认异常，不扩WriteResult恢复。消费原同实例run projection接口与Weakness当前查询，不增加别名状态／转发领域服务。原strong Shop测试保持实际decoder／whole bus／SQLite／encoder，无网络或固定receipt fixture只守算法／投影；旧guard诚实换实际注册与单元判据，资产及cause断言保留。
- Checks: 主控最终安全旧复验App156/156→Protocol79/79，39路径／17product完整恢复，最终准备仅一处XML注释差异；19oracle旧新通过，含真实四路scope／encoder、两组直接Fence／Block／DiscardExact、Finish原entry／Ordinal重放和physical替换隔离、晚action SQL与native Commit区别、取消／typed、完整成长帧序。新非增量Release solution **0警告／0错误**；严格串行full App **5678＝5660pass＋2原Dimension＋16skip**、Protocol **1786pass**、Net **203pass**。逐display／outcome／重数对50：App N19D0，Protocol N1D1仅guard替换，Net0/0；原失败消息与18有效skip不变。原DFO parser **70＝67negative＋3control**，outside／exe／orphan0；有效八项历史合计9467，非八fresh。runtime.actual四binding／fine0，生产源码0；Result后strict／八坏例／含newfiles diff-check，详见`verification/task51-verification.md`及本机`logs/task51-final-r1/comparison.json`。
- Remaining: T-007后续及T-012仍由主控继续，不封簇／Spec。受控fixture不代表A21、自然故障或专门性能验收；native失败后重试仅无physical夹具的logical账本证据，不称关闭连接可重试。全部runner自然结束，停写交快审；未Git或外部收尾。


## Independent Quick Review R1

**approved — 阻塞发现：none。**

```yaml
kind: rks-review
verdict: approved
```

评审对象：Contract r1／执行轮1，基线 `1788011a` 上的 TASK-51 owned diff；按 `reviewing-work` 的 Task Unit／Code 判据及 process-reset、Spec B8 的 RQ／VC-B30～31、Plan P10 的 T-007 有界审查。独立核对43路径 manifest 与当前文件一致；本评审仅追加此节，不接管提交或封簇。

- 四个薄 handler 绑定唯一 Protocol／Direct whole；三个 ordinary 写保持空 selected／None，Finish 保唯一 Main、原精确 Recipient、unit.Footprint 和 minimum Inventory|Quests。独立对照旧 patch，四份业务方法体及辅助方法在显式 footprint 替换后相同。原奖励、消耗与成长事务未拆分；SkillState 先于 ACK，run 接口与 Weakness 原查询／投影归属符合合同。未发现新增 bus 转发、core／raw／Self 权限。
- Finish 仅在 action 内捕获原专用拒绝，以同 cause RequireRollback；WriteAsync 完整返回后才记录回滚并回应 ACK22。native Commit 普通 SQL、取消及递归 typed 保默认失败；普通业务拒绝未改强制回滚。正式 oracle 直接覆盖实际 DI Dispose、encoder、精确 Fence／Block／DiscardExact、原 entry／Ordinal 与 replay physical 替换隔离；VC-B30／31 与这些旧新差异一致。受控夹具、算法 fixture 与无 physical 的 logical retry 边界已有明确披露。
- 只读重算原始 TRX：最终旧源 App156／Protocol79 全通过（含19 oracle）；新 App5660pass＋2既有 Dimension fail＋16skip、Protocol1786pass、Networking203pass。build 日志为0警告0错误，run.json 的自然结束与严格串行顺序一致；两项失败身份及消息吻合披露。runtime 探针源码／输出、strict valid、八坏例均拒绝和恢复结果支持结果主张。有效历史八项总数未被冒称八项 fresh。
- 在内存中逆用完整 patch 独立比较迁移账：仅四 message（65567～65570）与四 fine 改变，全部 original、顺序及其他行保持；AGENTS 仅清单一行，Spec 仅追加两条 VC，未扩大 T-007 完成范围。

本轮未运行测试、服务、Git 或外部操作。结论仅通过 TASK-51 本地结果快审；A21、自然故障与专门性能验收未建立，后续由主控消费。STOP。

## Controller Consumption R1

主控接受唯一 fresh Astra 的 approved／无阻塞发现，TASK-51 本地完成。已独立读四 whole、最终测试与 Review，亲自执行安全旧源复验并核39路径精确恢复；最终纯准备仅一处XML注释差异。新构建0警告0错误，三full逐例复算与原失败消息相同，DFO按原expected重跑解析为62／1／7且无额外记录；有效9467仍是三fresh加五历史。Scope／initial encoder两组直接Fence、Block、委托实际服务的DiscardExact证据已按定稿确认。

strict与八坏例、八行唯一变更及全部original／顺序／非目标不变、含14新增文件的diff-check、纯准备可独立暂存均由主控复核。按用户流程分开提交21文件纯测试准备与17产品／5人写记录的whole迁移；原始输出只留本机。继续T-007后续，T-012统一验收不减，不封簇／Spec、不外部收尾。
