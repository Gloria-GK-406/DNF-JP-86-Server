# TASK-30 邮箱旧边界调查

2026-10-25；基准 `6e21892a9d2959a6b7c0bdad96fae63dbb87ef5a`，分支 `task/refactor-session-20261021`。仅 evidence，原产品、测试、canonical、Contract、B4/P5、历史 Task 均未修改。本结果不关闭 ledger 行，不代表邮箱迁移、T-005 或 Spec002 完成。

## 可复核结果

权威探针为 `freeze-07/` → `task-30-build-07` → `task-30-probe-final-03` → `final-03/*.json`，14 场各用新独占 ShopFixture。`check-evidence.py` 新运行通过，见 `checks.json`、`matrix-summary.json`、`column-comparison.json`、`cause-checks.json`、`actual-frame-checks.json`。早轮原日志和 source freeze 保留；不能将早轮当最终 DLL。

| 场景 | 真实结果／Tx Completion | 原 ACK | 实际帧数／批数 |
|---|---|---:|---:|
| single / multi Send | 各自真实 decoder，Committed | 0 | 各 4／1 |
| Claim Gold | 原系统邮件附件编码，Committed | 0 | 2／1 |
| Read / Delete | 原 action 2／0，Committed | 0 | 各 1／1 |
| Open | 真实系统信件 inbox，Committed | 0 | 2／1 |
| Query | 查 shop2，Committed | 0 | 1／1 |
| 不支持 action 99 | 不进 fine，无 Tx | 21 | 1／1 |
| Send 晚期 action SQL | native 19/1811；领域 action 内自回滚，ConfirmedRollback | 3 | 1／1 |
| Claim 晚期 GoldLimit | 先 MarkClaimed，再实际 AddGold 上限拒绝；领域 action 内自回滚，ConfirmedRollback | 10 | 1／1 |
| native fallible Commit | fine body 已返回后 native 19/787，ConfirmedRollback | 3 | 1／1 |
| C1 nonfallible Commit 违约 | SQLite 已 durable；Uncertain / NonFallibleCommitContractViolation | 3 | 1／1 |
| C2 action SQL＋Rollback 第二因 | SQLite 实际全列回滚；Uncertain / RollbackFailure | 3 | 1／1 |
| C3 fine DI DisposeAsync | 原 body 返回且已 Committed 后，真实 scope 清理抛错 | 3 | 1／1 |

上表的帧数是每场原请求终局，合法新发送／健康 Query 控制另存 `Retry` / `HealthyControl`，不混入原请求数字。14 场 dispatch 均正常返回；真实精确会话 Standing 无 Fence／撤销、物理 IsBlocked=false，转发观察器实际 Fence=0、Creature.DiscardExact=0，关闭条目=0、production sender 的 CloseAsync=0、记录关闭链为空。四种 C/拒绝后的健康 Query，以及 action/commit 去注错后的合法新发送均通过同一物理 context。去注错后的新发送不是 Main 或同序号幂等重放。

## 真入口与传输

`probe/Program.cs` 只用原 ShopFixture 的 public ctor/properties 窄反射：其自身构建一个 provider，原 Infrastructure/Persistence/Application/Protocol 模块与 SQLite 不变。准备独立用该 provider 的原 TestGameplay；实际请求不放在外层 TestGameplay 中。对 `GameMessageDecoderRegistration` 的 selector/type 找到真实 decoder，以完整 body 解包，再给同 provider 的 `INetworkMessageDispatcher`；未 new Mailbox 请求对象、未直调 handler／fine 冒充入站。

实际六路为 01/005E、013B、005F、0086、0060、0144，对应六枚举消息、五 handler、一个 MailboxDelivery 和原 IAccount-only Transactional MailboxCommand；Read/Delete 是同一 action 消息的两个真目标。single/multi 的第一个附件 type 字段差异、后续附件字段、claim 的 0x40000000 偏移、Query 长度前缀均来自当前 decoder。所有进入 fine 的场景在真实执行中 SelectedSessionCommandScope.Current=null，没有足迹或 Main。命令注册与 marker 的静态边界见原 `MailboxCommands.cs:15`、`ProtocolModule.cs:185`；不将 Cera DemandEffects 或 VC-B4 推及邮箱。

保留 RecordedOutbound 原 registration adapter／原 encoder wrapper（故障开关未启用），恢复 GameplayQueue 的**第一 production GameplayOutboxes 工厂**。最终每场直接反射确认 causes 是该 provider 的 `GameplayCloseCauses`、sender 就是观察 Delivery；没有缺 causes 的最后测试工厂，也没有手工 Record 链。Delivery 内使用原 production OutboundSender 和受控内存 IOutboundTransport；实际 Resolve 按 physical context 的 ReferenceEquals 判断。原注册帧 bytes 与真实 SendAsync bytes 逐批相等，逐 15B Game 帧解析完整，出站发生在执行器外，所有报告为 Delivered。未使用 RecordedOutbound.Discard 代表物理关闭。

Send 成功是 ACK→三条 000E 库存／清槽／金币，Claim 是 000E 金币→ACK，Open 是 ACK→0061 inbox。所有固定错误均为一条 CMD ACK；C3 在实际 Dispose 故障前的 outbox slots 直接观察为空，因此它没有先登记成功投影，之后仅旧广 catch 的失败 ACK 发出。此处不把 Cera scope 的已登记条目事实移植到邮箱。

未实际产生关闭条目，故 recorded-chain 空是带同 provider 绑定和真实 sender 的负观察；source chain 则保有实际 inbound job root/ID/connection。没有模拟未运行的 disconnect 子作业，也不声称正向 close-chain 继承或 A21/TCP 验收。

## 真实事务、资产与 cause

每个阶段保存真实 Tx 对象身份、IsSettled、IsRollbackOnly、Completion、GuardFailure、CommitFailure、全部真实 participant 及 LastFailure、发件箱 unit state、Current 是否为空、job chain、selected scope。观察读取在 fine-enter 提前 enlist 该实例的同一 SQLite participant；每场只存在一个 fallible participant，未加入第二数据库或重造 Tx。代理逐参数转发原 domain/repository/fine，未代替任何领域行为或管线；C1/C2 的受控 nonfallible participant 和 C3 scoped resource 仅是明确测试注错。

基础 fixture 为 110 张生产 user table / 644 列；native Commit 另有 2 张注错表，安装后才取基线。`Snapshot` 保存 table schema、PRAGMA table_info、完整列稳定排序；复核脚本从实际主键／UNIQUE约束导出稳定rowidentity（item_slots为owner_scope/owner_id/list_type/slot_index）、所有行、每列类型和值，NULL 与 BLOB 单独编码。`column-comparison.json` 对各表**每一列**逐值比较并保存 before/after 列 SHA；没有归一时间、GUID、空值或 BLOB。action、claim-refusal、commit、C2 的同 run 基线与协议终局所有列完全相等。所有原始阶段快照仍在各场 JSON，不靠跨 run 等价取代回滚。

Send action 的 `repository-InsertAsync-enter` 已实际移走设备位置、减少 stack、扣金币且有暂存事件；SQL trigger 真在 INSERT 时 ABORT。Claim refusal 的 `repository-MarkClaimedAsync-return` 已暂存 claimed=1，再由真实金币上限差额检查抛 GoldLimit。两者 mailbox domain throw 观察已是 Current=null、IsSettled=true、IsRollbackOnly=false、Completion=ConfirmedRollback、unit=RolledBack；不同于正常 RequireRollback，也不同于 commit span。fine/旧 handler 不可能事后把它称为仍打开的业务事务。

multi 成功保留设备 UID=1 及 `items` 原设备行所有列；附件引用同 UID、同 exact core BLOB，stack 20→17，gold 100000→99970，附件种类 equipment/stackable/gold。`asset-and-scope-checks.json` 保存其同 run 判定；`final-03/multi.json` 保存完整值。C1/C3 的协议终局外部 SQLite 也保有这些 durable 变化，不因失败 ACK 被误记成回滚。

native Commit 场景在真实 mailbox INSERT trigger 中写入 DEFERRABLE child；action/fine 返回前 Snapshot 见 ForeignKeys=1 与暂存 child，原 SqliteTransaction.Commit 栈才抛 19/787。真实 CommitFailure 为唯一 SQLite source 的 KnownStorage/FallibleCommit，Cause 与该 SqliteException 相同对象 ID；最终 ConfirmedRollback。未用 Prepare 手抛替代 native Commit。

C1 原 fixed CommitFault 是 typed NonFallibleCommitContractViolation 的 inner，同对象 ID 保留，Tx/unit Uncertain，但真实 SQL durable。C3 fixed CleanupFault 在真实 fine scope DisposeAsync 抛出，事前 Completion/unit=Committed、Current=null、pending slots=[]；旧 MailboxDelivery 捕获同对象并发 ACK3。

C2 保留 SQL ID1/native19/1811，以及 fixed RollbackFault ID2。原 Domain.RunAsync 的无 cause `RollbackAsync()` 产生 typed ID3→Aggregate ID4→RollbackFault ID2，**typed 图不含 SQL ID1**。CommandLoggingBehavior 和 MailboxDelivery 的实际 production ILogger 均只记录 typed ID3；测试侧 `Task30.RepositoryObserver` ILogger 在原 repository throw 点保留原 SQL ID1（明确为观察日志，不冒生产日志）。完整异常类型、原 stack、Inner/Aggregate 边、native code、引用 ID 均在该场文件和 `cause-checks.json`。没有补图、改原因或修产品。

## 共享保护与尚待主控裁定

原 focused 新运行 Application 49=MailboxCore30（其中 QuestHelperPuppetTests partial14 已在30内）＋MailboxProtocolIntegration19；Protocol codec10；合计49＋10=59，全部通过、0跳过、0失败。raw console detailed、原 filter/argv/cwd/HEAD/UTC/自然退出保存在 `verification/logs/task-30-focused-*-01.*`，期限5m与原类并行未改。`original-test-contract.json` 列出全部方法、InlineData、参数及每方法原body SHA/Assert调用点；调用点数不是展开循环/分支/helper后的运行时 assert 次数，实际case身份和参数以 `focused-results.json` 为准。

其中原 `DatabaseFailureAndCaughtRefusalCannotCommitPartialAssets` 真运行 standalone SQL 失败及 ambient 内 caught refusal 后 Commit 必抛，原设备/金币/stack守护没有改弱。MailboxProtocolIntegration 原手造 request/handler 强用例保留自身业务价值，但不能冒 decoder端到端；codec10也不能单独冒SQLite入站。14场探针补的是本Task具名 decoder→dispatcher→fine→真实domain/DB/outbox/sender 链。

共享 caller 的下列事实来自当前源码，**本Task未逐个重跑其所有故障**：

| Caller | 仍须保留的共享边界及来源 |
|---|---|
| ItemDelivery | 溢出／MailOnly 创建 prepared records，同事务 SendPrepared；catch 看 IsSettled 再 RequireRollback（`ItemDeliveryDomainService.cs:157–174`）。不能先删领域自结算再假设所有上层仍防部分提交。 |
| GM official mail | 多收件人 prepared attachments，随后持久 operation receipt 和整批 audit（`GmOfficialMailDomainService.cs:113–153`）；自结算语义变化可能影响前面已处理收件人与整批审计原子性。 |
| QuestHelperPuppet | 同事务撤销旧system delivery、替换物品和日标记；catch 显式避开已 settled Tx（`QuestHelperPuppetDomainService.cs:32–71`）；当前Core partial14已真实通过。 |
| Housing harvest | 先 RecordHarvest，给双方发信，再保存 tree轮次（`HousingOperations.cs:85–95`）。 |
| Wedding settlement | 位置持久化、MailOnly礼物、双方礼金、宾客回礼／退款共存（`WeddingSettlement.cs:47–100`）。 |

MailCreated 的事实／delivery 与业务同事务；当前注册 `mail.arrival` 是 LiveOnly（ApplicationModule:218–221），消费时的 subscriber 才 Project 当前提醒。multi原请求返回时新 mail.arrival 尚 Pending；后续明确 DrainOnce 实际消费9条（含fixture准备产生的其他delivery），新投递条目0，shop2不在线；该消费不是原ACK的一部分。原focused另证明在线收件人提交后刷新与回滚不提醒；不能将LiveOnly消费前移为协议成功的一部分。

主控后继需据本证据决定：领域 action 内已自结算事实与协议工作单元的关系；C2 两因的保存义务；C1/C2/C3 旧失败ACK、durable/Uncertain事实及默认精确保护之间的可见行为取舍；这些选择对共享caller的影响及所需受影响 WHAT/HOW gate。当前 B4/P5 只给 Cera 的 VC-B4，不能用它直接改变邮箱；本Task未选新API、未写P6/B5/Task31、未扩大恢复来源/raw/participants/fence方案。

## 精度与未测范围

Read/Delete/Open/Query 的成功与共用 RunAsync 源码已核；没有逐条动态注入全部C矩阵。不扩展取消、replacement、全部操作故障、真实disconnect child或网络异常；没有实际客户端验收，没有性能接受结论。DFO_GAMEPLAY_ASSERTIONS 本次独立路径无记录，仅是本probe/focused观察，不能冒七程序集74/7。既有库存9029=9009pass＋2 exact Dimension fail＋18skip仅引用 Task29，未跑新八项目全量，不改既有失败身份或 Skip。

构建01因探针的IsSettled接口访问/TestGameplay可见性/using错误失败，构建05因观察器MailException命名空间错误失败；raw与各freeze保留。均只在E内纠正，原产品无修改；其余build成功，权威build07为0 warning/0 error。`source-freeze.json` 将每轮源和实际执行映射；`loaded-assembly-checks.json` 绑定最终加载MVID/SHA与原Release copy，friend名 `Dfo.Server.Networking.Tests` 的探针仅在E/bin，并非正式Networking.Tests测试。
