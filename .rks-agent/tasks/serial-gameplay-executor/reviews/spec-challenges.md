---
kind: rks-adversarial-challenges
target: .rks-agent/tasks/serial-gameplay-executor/spec.md
target_state: ready
contract_revision: 2
baseline: a02bce46
---

# Spec 对抗挑战：serial-gameplay-executor

- **目标**：`.rks-agent/tasks/serial-gameplay-executor/spec.md`（`state: ready`，对应 TaskContract revision 2），基线 `a02bce46`。
- **治理来源**：同目录 `TaskContract.md`（r2）、`reviews/task-contract-review-r2.md`、`research/01`–`05`；仓库 `AGENTS.md`、`Docs/Agent/RefactorPreferences.md`。
- **攻击范围**：这份 Spec 会不会让实施者交付错误的东西，或者让「行为不变」「顺序保证」「保护意图不丢」在实际中做不到。重点看：执行器、背压、HTTP 与生命周期之间的等待关系；发件箱与今天在 CMD 中途读取送达结果的流程；工作单元与事件约束是否自洽；调用链与深度上限；RQ-091 的口径；附录 B、F 的边界；handler 与 CMD 的一一对应。
- **方法**：在全新上下文中只读核对代码。下文路径相对 `Server/src/`，测试路径相对 `Server/tests/`。没有运行测试，也没有修改任何文件（本文件除外）。

---

## CH-1：请求级保护靠 AsyncLocal 帧传递，跨过入队点就静默失效

- Risk: high-risk
- Failure and impact:
  今天账本、登录权威与会话复查这三层保护，都在 dispatcher 或账本里用 AsyncLocal 设一个帧，再在**同一条异步流**里调用 handler，下游的管线行为从帧里读出数据。RQ-020 把 handler 改成「入队后等待」。一个 `Channel` 加单消费者的执行器，自然实现方式下不会把入队方的 ExecutionContext 带到消费方，于是 CMD 在执行器上读到的帧是 `null`。偏偏这几处对 `null` 的处理都是**放行**，不是报错：
  1. `BeginMain` 遇到 `frame is null` 直接返回空 scope，`Settled` 也直接返回。「一请求一主结算」的检查和已提交标记都不会执行。
  2. 账本的 `finally` 看到条目仍是 `Pending`，就把它记成 `Rejected`。客户端重发同一序号时，本该重放已提交的投影，现在却抛出「cannot retry a Rejected operation」，然后断线。RQ-023(b) 的两个保护同时消失，而且没有任何异常提示。
  3. `NetworkCommandAuthorization.DemandCurrent` 只在帧非空且失效时才拒绝，帧为 `null` 时永远放行。原本「加锁之后复查登录是否被顶替」这一层就没了。
  4. dispatcher 的 `IsBlocked`、`IsSuperseded`、`CaptureAuthority`，以及账本的 `FindByConnection`、`Capture`，如果留在入队前，就会在传输线程上读执行器拥有的状态（附录 F 已把 `OnlineStateRegistry`、`LoginSessionRegistry` 列为「改为执行器内访问」）。而且检查结果在排队期间会过期：排在前面的 CMD（例如另一条连接的顶替登录）可能已经改了状态。

  Spec 只要求这些保护「在 CMD 内的位置等价」（RQ-023），却没有说今天承载它们的是哪些机制。附录 F 删除了 45 号行为（`BeginMain`、`DemandRecipient`、`Capture` 都在里面）；58 号 `SelectedSessionSettlementBehavior`（承担 `DemandCurrent`、`Settled`/`Failed` 和不确定时的 `BlockAsync`）在全文**一次都没出现**，既不在删除栏，也不在保留栏。RQ-022 又把 39 个 `IRequestSettlementCommand` 赖以识别主结算的命令类型并掉。这样一来，实施者删干净、编译通过，但保护已经不在了。`EffectOperationReplayTests`、`CommonExperienceEffectTests` 属于调研 05 列为「测门或排队」、需要改写的测试，改写后未必还会走真实的入队路径，AC-009 不一定拦得住。
- Evidence:
  - `Dfo.Server.Application/Dispatching/NetworkMessageDispatcher.cs:22`、`:25`、`:28-29`、`:42`：入队前做检查、`Enter` 授权帧、账本包裹 handler。
  - `Dfo.Server.Application/Effects/EffectOperationLedger.cs:24`、`:69`、`:74`：在 handler 外设帧；`Pending` 被改记为 `Rejected`。
  - `Dfo.Server.Application/Effects/EffectOperationContext.cs:60`（`if (frame is null) return new Scope(...)`）、`:104`（`Settled` 无帧即返回）。
  - `Dfo.Server.Application/Commands/NetworkCommandAuthorization.cs:23`（只有 `frame is { Active: true }` 时才检查）。
  - `Dfo.Server.Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:13-14`、`SelectedSessionSettlementBehavior.cs:13-30`；`UnitOfWorkBehavior.cs:52`、`:59`、`:62`（`TransactionStarted`、`RollbackConfirmed`、`Freeze`）。
  - 探查：`grep -n 'SelectedSessionSettlement\|58' .rks-agent/tasks/serial-gameplay-executor/spec.md`，没有命中。
- Minimum correction:
  在 Spec 中加一条要求：请求级上下文（物理连接上下文、捕获的登录权威、账本条目、入站元数据、选中会话接收者）作为协议 CMD 的显式数据，在执行器上、CMD 开始时建立，不依赖 AsyncLocal 跨入队点传递。dispatcher 的封锁、顶替检查在 CMD 开始时重做。对协议 CMD，账本与授权的「无帧」分支改成程序错误。附录 F 和附录 C 逐项列出 45、58、60 号行为的非锁职责（`BeginMain`、`DemandRecipient`、`DemandCurrent`、`TransactionStarted`、`Freeze`、`Settled`、`Failed`、`Block`），写明每一项迁到哪个工作单元的哪个位置，并说明主结算今后怎样识别。再补一条反例用例：经真实入站路径重发一个已提交的效果请求，必须得到重放，而不是断线。

## CH-2：多参与者隔离的载体被当作锁删掉，「提交后失败」又被混成了「投递失败」

- Risk: high-risk
- Failure and impact:
  `IMultiSelectedSessionCommand.Sessions` 不只是取锁用的名单。它是**冻结的参与者集合**：45 号行为逐个对它做 `DemandCurrent`，事务结果不确定时逐个对它 `BlockAsync`（封锁会话并丢弃生物运行时）。8 个命令依赖它：进本、离开副本、怪物死亡、祭坛×2、两塔、婚礼。附录 F 却要求把它删掉，「不留类型、注册或转发」。

  另一方面，怪物死亡在提交之后用一个大 `catch` 包住全部后续工作，包括排队房间经验、评估通关、组帧、投递、通关投影和祭坛唤醒。任何一步失败，都会封锁**全部** captured 参与者，并转成 `PostCommitPublicationFailure`。现有测试 `Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 用的触发条件，是**运行时认领发布被拒**（内存中的领域动作），不是网络发送失败。

  Spec 的 DR-4 / VC-4 把这一类都写成「提交后投递失败 → 只关闭失败的连接，不再隔离同局的其他会话」。RQ-031(c) 又规定：CMD 因未处理异常结束时，只关闭**来源连接**。实施者照字面做，会出现三个后果：
  1. 删掉参与者集合；
  2. 按 VC-4 改写上面那条测试；
  3. 组帧或运行时认领这类非传输失败发生时，同局其他成员的击杀、掉落已经提交，他们却没收到通知，也没被封锁，会话照常继续。

  这正是 DR-4 声称要保住的「状态已提交而客户端不知道时，不让会话继续」。今天保护这一点的测试，恰好在 VC-4 允许改写的范围内，后续检查发现不了。
- Evidence:
  - `Dfo.Server.Application/Commands/Pipeline/SelectedSessionExecutionBehavior.cs:19`、`:27`、`:36`。
  - `Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs:440-444`（不确定提交时封锁全部参与者）、`:455-497`（提交后整段失败都封锁全部参与者）。
  - `Dfo.Server.Application/Commands/SelectedSessionSafety.cs:59-66`、`:75-82`。
  - `Dfo.Server.Protocol.Tests/Game/MonsterDeath/MonsterDeathHandlerTests.cs:927-952`、`:1400`（触发条件是认领发布被拒）。
  - 探查：`grep -rln IMultiSelectedSessionCommand Server/src` 命中 8 个命令文件，加上管线行为。
- Minimum correction:
  1. 附录 F 把「参与者集合」从删除栏移到保留并迁移，作为 CMD 数据保留。RQ-023(c) 写明：结果不确定时，封锁参与者集合中的**每个**会话。
  2. DR-4 / VC-4 拆成两种情形：一是传输层交付失败，按 RQ-036 处理；二是提交之后、CMD 之内的非传输失败（组帧、运行时认领、评估），仍然封锁全部参与者。
  3. 点名上面那条测试保持断言不变。

## CH-3：发件箱只说「登记消息」，没说投影在何时组装、按谁的状态组装

- Risk: high-risk
- Failure and impact:
  今天的 92 处 `ProjectAsync` 不是登记一条现成的消息，而是**惰性组装**：在发布那一刻解析人群，然后按每个收件人调用 `ComposeAsync`，组装时会读取当前领域状态（例如 `TitleBookChangedHandler` 用 `views.TryLoadAsync` 读出当前视图）。组装与发送共用每收件人 2 秒的期限。`EventService` 明确禁止在可写事务内投影，理由是投影「不能看到未提交的状态」。

  Spec 没有规定组装发生在哪一刻，只在附录 B 暗示发送端只处理不可变消息。RQ-031(b) 又预设消息可以在工作单元内登记。实施者面前有三种做法，每一种都会破坏 Spec 自己的某条要求：
  1. **刷出时在发送端组装**：在执行器外读取执行器拥有的状态，违反 RQ-010；而且读到的是之后若干条 CMD 改过的状态，帧内容随之变化，违反 VC-1「消息内容不变」。
  2. **CMD 结束时统一组装**：多段 CMD 里前面登记的投影，会读到后面阶段的结果。例如选角在补货之前推送的状态帧会带上补货结果，补货增量再推一次，客户端看到的数量就会出错。
  3. **登记时在工作单元内组装**：看到的是未提交的状态，也就是今天被禁止的情形；遇到事务结果不确定时，这些条目该保留还是丢弃，Spec 也没有规定。

  能拦住内容漂移的证据也不够：RQ-080 的表征测试只断言「完整帧序」；选角金样本绕过工作流（r2 审查已确认这一点）。内容变了，没有任何一条判据会失败。
- Evidence:
  - `Dfo.Server.Application/DomainEvents/EventService.cs:62-63`。
  - `Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs:209-218`（组装时读当前视图与会话）。
  - `Dfo.Server.Application/Notifications/NotificationBus.cs:28`、`:61`、`:208`（2 秒期限，在发布时解析人群并组装）。
  - `research/03` §2「composer 在发送前异步读领域状态」；Spec 附录 B 第 2 行、RQ-031(b)、RQ-080。
- Minimum correction:
  在 Spec 中定下组装点。例如：人群解析与逐收件人组装都在 CMD 内进行，发生在**登记它的那个工作单元提交之后、下一个工作单元开始之前**；不在工作单元内登记的条目，在登记时组装。发送端只接收已组装好的帧。RQ-080 的表征测试改为断言完整帧字节，不只断言顺序。

## CH-4：深度超限要求「不入队」，但 delivery 仍在库里待处理，结果是自旋活锁

- Risk: high-risk
- Failure and impact:
  按 RQ-040，Reliable 和 LiveOnly 的 delivery 都是持久化的行，分发循环保持不变：一轮处理条数为 0 时，用 `NextDueAsync` 计算等待时间；如果有已经到期却没被处理的行，等待时间就是 0，循环立刻重读。

  RQ-053 规定超限时「不入队这条 CMD」，(c) 只要求记日志，(a) 只要求关连接，都没说那一行 delivery 怎么办。22 个 LiveOnly 订阅和所有以连接为根的链在超限后，那一行会一直保持到期且未处理的状态。分发循环于是不停地读到它，又不停地拒绝入队，形成 CPU 自旋和 `Error` 日志洪水。重启也解决不了：按 RQ-051，重启后它成为新根、深度归零，本来就是无限的链会重新跑满上限，然后再卡住。

  (b) 要求把 Reliable 标为 `Terminal`，这是一次 SQLite 写。按 RQ-061，它必须发生在执行器上的工作单元里；可规则又说这条 CMD 不入队。实施者只能再造一条 CMD 去写这个状态，而那条 CMD 继承同一条链，同样超限。

  另外，RQ-052 要求常量至少是「最长合法链」的 2 倍，但没说怎么统计：按订阅图静态推导，还是实测？关连接引起的生命周期 CMD、失败重试是否继承链？实施初期难以得出一个可复核的数。用户要的是「无限连锁记录错误后掉线」，结果遏制机制本身先变成了故障源。
- Evidence:
  - `Dfo.Server.Application/DomainEvents/DomainEventDispatcher.cs:178-187`（处理条数为 0 时按 `NextDueAsync` 等待，过期即零延迟）、`:69`（`ReadDueAsync` 每轮重读）、`:127-145`（LiveOnly 也要先写消费决定）。
  - `Dfo.Server.Application/DomainEvents/EventService.cs:31-34`（每个匹配订阅一行 delivery）。
  - Spec RQ-051、RQ-053(a)–(c)、RQ-061、AC-016（「无连接链只记日志」被写成了验收条件）。
- Minimum correction:
  规定超限必须**落定那一行 delivery**：Reliable 写 `Terminal`，LiveOnly 以「深度超限」为理由写完成。这次落定在执行器上的一个工作单元里进行，并且豁免深度检查。写明失败重试、关连接引起的 CMD 如何继承调用链。给出最长合法链的统计方法（例如由订阅注册与 `EnqueueAsync` 调用点推导出事件图，取最长路径），并让判据钉住这个数。

## CH-5：RQ-091 的口径既可能「虚假达标」，也可能逼出错误设计

- Risk: high-risk
- Failure and impact:
  RQ-091 是验证「单执行器够用」这一核心假设的唯一关口，但口径留了几个缺口：
  1. **负载组成没有定义。** 「400 CMD/秒」「混合负载」由实施者自选。只要 CMD 串行执行，p99 等待就取决于长 CMD 出现的频率：一条 150 ms 的 CMD 执行期间，约有 60 条 CMD 进入队列，其中约三分之二要等 50 ms 以上。现有主机日志里，`CreateCharacterCommand` 用了 147 ms，`RegisterAccountCommand` 用了 91 ms（含哈希）。选角一次有 15 个事务。选用轻量的负载组成就能达标，选用真实的组成就可能不达标。
  2. **测量要求「预热」，正好避开了新模型独有的风险。** 成就和称号目录是 `Lazy`，不在启动预热清单里；第一次使用时会扫描整个 `n_quest/quest.lst`。今天这只卡住一条连接，在单执行器上会冻结全服所有玩家。
  3. **没有基线对照。** 今天的全局事务门已经串行化了全部 Transactional 命令，吞吐上限有一部分早就存在。不达标时，无法分辨原因是设计还是原本就有的瓶颈。
  4. **测量环境不代表发布环境。** SQLite 只设了 `journal_mode=WAL`，`synchronous` 用默认值，每次提交的落盘开销强烈依赖机器；「本机」的数字推不到 Windows/Linux 发布包上。

  后果有两种。一种是虚假达标，上线后长 CMD 或冷加载让全服卡顿，而且没有别的检查覆盖延迟。另一种是不达标后为了压低排头阻塞，拆分 CMD 或把读放回执行器外，违反 RQ-020、RQ-024、RQ-010。
- Evidence:
  - Spec RQ-090、RQ-091（只写「400 CMD/秒的负载」「混合负载」，并要求「预热」）。
  - `Dfo.Server.Infrastructure/Content/PvfAchievementDefinitionSource.cs:9-17`、`:52`（成就与称号目录都是 `Lazy`，前者读全部任务文件）；`Dfo.Server.GameServer/GameServerStartupService.cs:54-86`（预热清单里没有成就和称号目录）。
  - `Document/tasks/event-joust/verification/host-http.log` 中 `CreateCharacterCommand ... in 147.4376 ms`、`RegisterAccountCommand ... in 91.7493 ms`。注意：日志行为位于管线最外层（Order 0），这些数字包含在门上等待的时间，只能当作量级参考。
  - `Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:145`（只设置了 WAL）。
- Minimum correction:
  1. 负载组成取自一次真实录制（例如 `Tool/WireSmoke`），写明各类 CMD 的比例，其中包括选角、建角、进本这些长 CMD。
  2. 另报单条 CMD 执行时长的 max 与 p99.9，并给出上限。
  3. 加一轮不预热的冷启动测量；或者规定执行器内会触达的全部 `Lazy` 内容源都必须进入启动预热，并在附录 B 列出。
  4. 用同一套测量工具在 `a02bce46` 上跑出基线。
  5. 记录 `synchronous` 等设置与磁盘类型。

## CH-6：等待关系与停机顺序没有写成约束，关连接和停服可能互相等死

- Risk: low-risk
- Failure and impact:
  TouchSocket 关闭一条连接时，会等这条连接正在执行的收包回调结束。RQ-005 又让收包回调等待它入队的 CMD 完成。这两条叠加，只有在「CMD 完成」不包含刷出与关闭、而且执行器永远不等待关闭时才安全。今天靠关连接器里的 `Task.Yield` 分离执行和 `DrainingTcpService` 的注释维持这一点，Spec 没有把它写成约束。

  如果实施者为了保持「回包先于下一个包」，把 RQ-005 的完成信号放到发件箱交付之后，那么 CMD 关闭自己的来源连接时（例如登录被拒），就会自己等自己。

  停机同理：如果执行器先于 TCP 服务或 Kestrel 停止，收包回调和 HTTP 会一直等那些永远不会执行的 CMD，停机挂住；断开时的生命周期 CMD（拆除时落库位置）也不再执行，玩家最后的位置丢失。

  自锁这一种很可能被 AC-007 的线级测试当场发现，所以定为 low-risk。停机丢位置这一种目前没有测试覆盖。
- Evidence:
  - `Dfo.Server.Networking/Runtime/TouchSocketNetworkConnectionCloser.cs:30-44`。
  - `Dfo.Server.Networking/Runtime/TcpServices.cs:14-20`、`:70-84`。
  - `Dfo.Server.Networking/Runtime/NetworkPlugins.cs:31-43`（关闭回调里依次等待各个观察者）。
  - Spec RQ-005、RQ-012、RQ-015 都没有规定完成信号的定义和停机顺序。
- Minimum correction:
  补三条约束：
  1. RQ-005 的完成信号在执行器执行完毕时发出，不等待交付；
  2. 关连接不能由执行器等待，也不能由收包回调所等待的任何东西等待；
  3. 停机时先停止接受新连接和 HTTP 请求，再把断开引起的生命周期 CMD 执行完，最后停止执行器。

  另外补一条停机落库的测试。

## CH-7：「一一对应」的计数单位有歧义，并把纯遥测高频包也推进了全局队列

- Risk: low-risk
- Failure and impact:
  `ProtocolModule` 共有 173 条 `RegisterMessageHandler`，按 `ClientMessageType` 注册，对应 141 个 handler 类。其中 `UnansweredReportHandler` 一个类注册了 13 个消息号（包括 `GameUseSkill`、`GameSyncItemSpace`），`WarehouseHandler` 注册了 6 个。

  RQ-020 / RQ-070(c) / AC-008 说的「每个入站协议消息类型恰有一个协议 CMD」「一一对应」，按消息号算是 173，按 CLR 类型或 handler 算是 141，两种算法下的判据写法不同。按 173 算，同一份逻辑要拆出好几个 CMD 类型；按 141 算，又谈不上「一一」。

  另外，RQ-020 要求每个已注册的类型都进执行器。这样一来，19 个纯协议或只记日志的 handler（调研 02 的 a 类），包括技能释放这类高频包，也都要进全局队列，并受 RQ-005 约束逐包等待。它们并不改状态、不写库，也不发包，不属于 RQ-010 要求必须进执行器的范围。这些包会占用 RQ-091 的吞吐余量。
- Evidence:
  - `Dfo.Server.Protocol/ProtocolModule.cs:974`、`:975`、`:992`（同一个 handler 注册多个消息号）。
  - 探查：`grep -o 'RegisterMessageHandler<[^>]*>' Server/src/Dfo.Server.Protocol/ProtocolModule.cs | sort | uniq -c` 显示 14 个类注册了多次，去重后共 141 个类。
  - `research/02` §1 a 类。
- Minimum correction:
  明确计数单位。例如：每个 `ClientMessageType` 恰好映射到一个协议 CMD 类型，一个 CMD 类型可以服务多个消息号；判据检查这个映射是全函数。同时说明纯遥测类是否允许留在附录 B（只记日志、不入队），或者明确它们入队的代价已计入 RQ-091 的负载。
