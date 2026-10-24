# 结果 A 的最终汇报（spec-001：单执行器、提交后发件箱与删锁）

- 日期 2026-10-24。冻结点 `2bdb1afd`；Spec 基准 `a02bce46`；规划检查点 `b76504bb`。本单元开始时 HEAD 为 `51c294fa`。
- 读者：外部审核（GPT Astra）与用户。本文是根 SC-11 的证据。每个论断都给出证据位置，推导过程不重抄；未关闭事项引用 F 编号，内容在 `follow-ups.md`。
- **审核时需要知道的上下文**（根契约 RI-16，`plan.md` R3「Next Planning Boundary」）：
  - 结果 A 封存后不重开。审核对 A 的意见，由根契约安排为编号接在 B 之后的修正子 Spec；在不跨边界的前提下，也可以并进子 Spec B；
  - 审核默认在 B 完成之后对整个分支一次进行，审核结论不是 A 封存或 B 开始的前提；
  - 推送、开 PR 与收尾，在 A、B 都封存并且审核通过之后才进行（根契约 Requires Confirmation 第 2 项）。

## 1. 结果

A 交付了以下内容，各项的证据见 §9 的 AC 表：
- **一个执行器**：进程内唯一的玩法执行器 `GameplayExecutor`，先进先出、单消费者，作业只经入队点 `IGameplayQueue` 进入；
- **全部入口为作业**：入站协议、连接生命周期、定时、事件消费、GM／HTTP 写操作、启动作业；
- **提交后发件箱**：作业内的一切出站与关连接只登记进发件箱，工作单元提交后才组装，回滚或结果不确定即丢弃；作业结束时按登记顺序交给发送端；
- **每连接发送队列**：分必达与尽力，跨连接有序组，有交付期限与队列上限，没有重试；执行器从不等待发送；
- **删锁**：玩法路径上的门、信号量、全局事务门及其管线行为、「锁内复查、重试」循环全部删除；
- **调用链与深度上限**：覆盖全部来源，超限按规定落定；
- **判据**：运行期判据没有开关，结构判据与普查各配反例。

请求走向（与 `AGENTS.md`「架构设计」一致）：

```
TCP 字节 → 传输解包 → dispatcher：组织一个 inbound 作业，入队并等待
  └ 执行器（唯一、串行）：在作业内建请求级帧 → handler → 总线内联执行细粒度命令（过渡形态）
       → 领域服务 → SQLite 工作单元；出站只登记进本作业的发件箱
  └ 作业结束：发件箱把已提交的条目按登记顺序交给发送端
发送端（执行器外，每连接一个队列）→ 传输写出；超时、超限或失败时关闭该连接
定时源、事件组循环、连接生命周期、GM／HTTP 路由、启动服务：同样只组织作业、入队、等待
```

今天的边界：handler 仍在入站作业内编排细粒度命令；`SendAsync` 在作业内内联；请求级帧是 AsyncLocal；每条细粒度命令仍包着五层管线。这些是过渡机制，共 6 项（§7），由子 Spec B 删除。

## 2. 规划停点的三项默示决定（根 SC-11、`spec.md` R3-4）

任务目录内没有 rev 2 规划停点汇报的落盘记录，本节的复述就是这一项的证据（根契约 SC-11 Required evidence；`reroute-t008.md` 主控方裁定第 7 条）。

用户对规划的回应是根契约 Authorization 一行所引的原话：「看着没问题了，模型也简化了很多。不过这个改动比较危险，走gov的sdd路线吧。……」。三项决定都是 Agent 提议、用户没有反对，按默示接受：

| 决定 | Agent 当时的提议与理由（根契约） | 用户的回应 | A 封存时的事实 | 对 B 的含义 |
|---|---|---|---|---|
| 单执行器不分流，只预留分流键（RI-4；根 Scope Out 第 1 项） | 先单执行器、预留分流键，理由是跨频道操作与共用 SQLite | 「用户随后不再提分流，并回复『看着没问题了』」（RI-4 理由栏） | `GameplayShardKey`／`GameplayShardSelector` 只返回同一个执行器（`GameplayExecutorOrderTests.Every_shard_key_selects_the_one_executor`，本次通过）；单执行器的实测见 §6 | B 不分流；分流键原样保留 |
| 短 `lock(sync)` 与 PVF 内容缓存锁暂留（RI-8；根 Scope Out 第 2 项） | 去留表：短 `lock(sync)` 先留后删 | 「看着没问题了」（RI-8） | 短 `lock` 逐层 Application 184、Protocol 11、Persistence 3（附录 F「保留」栏）。Infrastructure 141 行，全是 PVF 内容源与规则初始化的锁。A 在执行器外的 Networking 组件里新增短锁 10 行（`transition-mechanisms.md` §3） | 对 A、B 同样适用；它们的后续删除另行处理 |
| 不换数据库，也不回内存副本（RI-9；根 Scope Out 第 3 项） | 持久化保持每段 SQLite 事务，不回内存副本，不换 MySQL | 用户问「直接上新的mysql的话……还有多少意义？」，Agent 给出理由后用户没有再坚持（RI-9） | `Server/src/Dfo.Server.Persistence` 在 `a02bce46` → `2bdb1afd` 之间没有改动（`git diff --stat` 为空）；每条 Transactional 命令仍是一个 SQLite 事务（RQ-024 的 A 一半，`t009-docs-and-appendices.md` §9） | B 迁移保留项与工作单元对照（附录 C、根 SC-13），不改持久化模型 |

rev 2 阶段 Spec 中的可见变化（`b76504bb` 上的附录 G）是 VC-1～VC-8。它们的原文与今天的差异见 §3。

## 3. 可见行为变化（附录 G：VC-1～VC-10）

| VC | 今天的文字（摘要） | 与 rev 2 原文的差异 | 证据用例（本次正式全量均通过） | 授权 |
|---|---|---|---|---|
| VC-1 | 多段 CMD（如选角）的出站消息在 CMD 完成后统一落线，内容与相对顺序不变 | 无 | `SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`（金样本 a 不变） | rev 2 |
| VC-2 | 未处理异常结束时，回滚或结果不确定的工作单元内登记的消息不发出 | 无 | `OutboxWorkUnitTests.Of_two_writable_transactions_only_the_one_that_commits_is_flushed` | rev 2 |
| VC-3 | 组队进本或两塔进入时某成员发送失败：只断开该成员，其他成员与请求者照常进入 | 扩展到两塔进入 | `PartySelectDungeonHandlerTests.A_follower_whose_transport_fails_is_closed_alone_and_the_party_still_enters`；两塔各一条 `A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower` | TASK-23 第 1 轮评审 F1 的主控方处置第 1 条（`50d7bce4`） |
| VC-4 | 怪物死亡、祭坛、清算在提交后发生传输层交付失败时，只关闭失败的连接 | 无 | `MonsterDeathHandlerTests.A_participant_whose_death_batch_fails_in_transport_is_closed_alone_and_nobody_is_fenced` | rev 2 |
| VC-5 | 钱包变化与 PvP 参与者通知不再重试；失败时断开该连接 | 无 | `NotificationDeliveryTests.A_recipient_whose_composition_failed_is_never_composed_again`（N14） | rev 2 |
| VC-6 | GM Cera 提醒只剩「已提交发送」与「不在线」 | 文字无；取值裁定为 `attempted`／`offline`，提醒异常时 HTTP 500（F-107） | `GmCeraReminderTests.An_online_account_is_reminded_and_the_result_is_attempted`、`GmCeraReminderTests.An_offline_account_in_the_batch_makes_the_result_offline_and_the_online_one_is_still_reminded` | rev 2；取值见 TASK-24 主控方裁定第 2 条 |
| VC-7 | 放弃副本、离队、换队长、踢出队员在重试用尽时不回包的分支消失；两塔协调器的确定性拒绝不属于本条 | 补「踢出队员」；补「`ExactSessions` 为假的拒绝不属于本条」 | `BossTowerProjectionConcurrencyTests.Give_up_outside_a_tower_runs_the_ordinary_path_once_and_answers_before_the_town` 等 4 条 | TASK-21 处置 L1（`09e4983c`） |
| VC-8 | 慢客户端在发送超时或队列超限时被断开 | 无 | `OutboxWireTests.A_client_that_stops_reading_is_closed_by_the_sender_while_another_connections_cmd_is_answered_at_once`；`OutboundSenderTests.A_delivery_that_exceeds_its_deadline_closes_the_connection` | rev 2 |
| VC-9 | `SET_PARTY_INFO` 的队伍标题超过 256 B 时请求被拒绝，发送者连接被关闭 | 新增 | `PartyCommandCodecTests.Set_party_info_rejects_a_title_longer_than_the_maximum`；`PartyTitleFanOutWireTests.A_party_title_at_the_limit_reaches_a_player_walking_in` | TASK-27 主控方裁定第 3 条（`f7bf0aa3`）；256 B 没有客户端证据（F-117） |
| VC-10 | 队伍状态按阶段推进（XO-6）时，某收件人在某一阶段交付失败：同一有序组里排在他之后的条目都不再交付；失败者被关闭，其余成员随后由断线清理收到当前队伍状态 | 新增 | 组合证据：`PartyNotificationTests.A_failed_delivery_in_one_formation_stage_skips_every_later_stage` ＋ `OutboundSenderTests.A_required_failure_stops_its_group_and_a_best_effort_failure_does_not`；第 3 句的衔接是读码（`t009-docs-and-appendices.md` §8） | TASK-30 主控方裁定第 3 条；TASK-33 主控方裁定第 5 条（`51c294fa`）；**满足，附条件** |

## 4. Spec 修订与解读（主控方授权的每一处）

### 4.1 修订：`b76504bb..HEAD` 中改过 `spec.md` 的 15 个提交

逐 hunk 列在 `logs/task-34-spec-revisions.txt`：共 52 个 hunk（5 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 5 ＋ 1 ＋ 1 ＋ 2 ＋ 2 ＋ 1 ＋ 18 ＋ 1 ＋ 3 ＋ 8 ＝ 52，即下表 hunk 数一栏之和），逐提交合计 ＋212／－78，净变化为 ＋199／－65（有的行被不止一个提交改过）。归不进下表任何一行的 hunk 为 0。

| 提交 | hunk 数 | 位置与性质 | 授权出处 |
|---|---:|---|---|
| `53c72543`（TASK-02） | 5 | RQ-050 补「关连接 → 生命周期」的继承口径；RQ-053(a) 补「深度检查发起的关闭是新根」。两处是需求文字的澄清，超出了 TASK-02 合同写的「只写附录」。附录 A 补最长合法链统计，附录 D 补 XO-1～XO-7，属附录对账 | 分三项写：<br>① RQ-050：TASK-02 第 1 轮结果评审 N-1（`reviews/task-02-result-review.md:64`）与主控方原话「主控方对第 1 轮的处置：……N-1 写进 RQ-050 正文」（`:118`），第 2 轮评审判为澄清（`:129`）；授权充分。<br>② RQ-053(a)：出自第 2 轮评审 R2-N3（`:155`），评审写明「R2-N3 交给主控方在 T-002 前处置」（`:162`）；实际由执行方在提交前写入（`tasks/TASK-02.md:45`），任务目录里没有主控方的单独裁定。主控方在 TASK-34 补作追认：这一澄清与 RQ-050 同属一组，主控方提交 TASK-02 时已接受，现补记为显式授权（`tasks/TASK-34.md`「主控方处置（第 1 轮评审后）」第 3 条）。授权强度：**提交即接受＋本次追认**。<br>③ 附录 A、D：TASK-02 合同（`tasks/TASK-02.md:8`） |
| `1eea890f`（TASK-09） | 1 | 附录 B「纯遥测 handler」行填清单 | TASK-09 待裁定第 6 条获准（`tasks/TASK-09.md:295`） |
| `bfbcf012`（TASK-14） | 2 | 附录 B 五行末尾追加清单；新增一行「不碰玩法状态的 HTTP 路由」 | TASK-14 主控方裁定第 12 条与 RQ-011（`tasks/TASK-14.md:665`） |
| `6331b23a`（TASK-15） | 1 | 附录 B「启动预热」行写清单（RQ-091 要求列入附录 B，该行原文写着「实施时逐个列出」） | TASK-15 合同（`tasks/TASK-15.md:45`） |
| `c3115f9a` | 1 | RQ-091 接受线改为单条 CMD ≤ 2 s，原阈值改作对照用的目标值 | 用户 2026-10-22 原话（RQ-091 修订段逐字引用）；根契约 RI-15 |
| `f13e612a`（TASK-16） | 5 | 附录 A、B 的事实性文字与清单，不改 RQ／AC | TASK-16 合同（`tasks/TASK-16.md:23`、`:55`）；附录 A 原文「实施时逐项对账」 |
| `0c9c59c8`（TASK-18） | 1 | 附录 B「开始监听前的启动期工作」末句：不设启动期例外 | TASK-18 待裁定第 4 条获准（`tasks/TASK-18.md:32`）；F-16 |
| `09e4983c`（TASK-21） | 1 | VC-7 补「踢出队员」与「`ExactSessions` 不属于本条」 | TASK-21 处置 L1（`t005-closure.md` §5） |
| `50d7bce4`（TASK-23） | 2 | 附录 A 第 4 行（顶替后的关闭由那条 CMD 登记）；VC-3 扩展到两塔进入（放宽可见变化） | TASK-23 主控方裁定第 6 条；TASK-23 第 1 轮评审 F1 的主控方处置第 1 条（`t006-closure.md` §10） |
| `1ee1c91e`（TASK-24） | 2 | RQ-031(d) 末句、AC-011 第 4 条：「封锁并关闭」更正为「封锁，入站时来源连接关闭」 | TASK-24 主控方裁定第 1 条（`t006-closure.md` §10） |
| `f7bf0aa3`（TASK-27） | 1 | 新增 VC-9 | TASK-27 主控方裁定第 3 条 |
| `bc735cd0`（改道 R3） | 18 | 验收边界收窄：A／B 拆法（R3-1～R3-4），frontmatter、标题加注与 31 处标记 | 用户 2026-10-23 的结构决定；根契约 r4、子契约 r2 与本修订三份审查通过（R3「生效」段） |
| `a9c79e98`（TASK-29） | 1 | 新增 RQ-053(d)：超限的拆除改作新根执行，再关根连接 | TASK-29 主控方裁定第 1 条 |
| `122576d5`（TASK-30） | 3 | 附录 F「保留」补口令哈希限流、「改为执行器内访问」扩大为普查名单；附录 B 补两处；新行「请求级帧（过渡）」 | TASK-30 主控方裁定第 1 条（`t009-criteria.md` §7） |
| `51c294fa`（TASK-33） | 8 | R4：附录 A 第 3、5、8 行，附录 B 三行，附录 F 短 `lock` 计数（事实性修订）；新增 VC-10，并按评审 L1 收窄其第一句；R4 修订记录 | TASK-30 主控方裁定第 3 条；TASK-33 主控方裁定第 1、5 条；TASK-33 结果评审 L1 |

与 TASK-34 规划时的表相比，只有一处不同：`53c72543` 除了附录，还改了 RQ-050 与 RQ-053(a) 的需求文字。两处的授权强度不同：RQ-050 有主控方对评审 N-1 的明确处置；RQ-053(a) 当时没有单独裁定，只能算主控方提交时接受，由主控方在本单元评审后补作追认（见表内 ②）。

### 4.2 解读（文字未改，含义经主控方裁定）

| 解读 | 内容 | 出处与授权 |
|---|---|---|
| RQ-012、RQ-031(c)「此前登记的消息」 | 按连接计：关闭连接 X 只等此前登记给 X 的消息；跨连接的先后只由附录 D 的有序组表达 | TASK-23 主控方裁定第 2 条（`t006-closure.md` §10） |
| RQ-034「整批编码……在发送端保持」 | 编码改在登记点完整完成，发送端只收帧；编码失败仍在调用点抛出。F-97 又推翻了 TASK-23 裁定里与次序相关的部分，改为「先判位置、后编码」 | TASK-23 主控方裁定第 1 条（备选 B）；TASK-30 主控方裁定第 2 条（`t009-criteria.md` §6） |
| VC-6 的取值 | `attempted`（保留字符串，含义「已提交发送」）与 `offline`；提醒异常时 HTTP 500，`outcome` 为 `unknown`（F-107） | TASK-24 主控方裁定第 2 条、评审后处置第 1 条 |
| AC-011 线级第 3 条 | 证据降到「执行器 ＋ 真实登记适配器 ＋ 真实发送端」层（F-108） | TASK-24 主控方裁定第 3 条；TASK-25 主控方裁定第 5 条 |
| AC-013 的人群项 | 按字面的实测口径满足；保守上界口径写适用范围（F-113）；「未计入」第一条维持不计入（F-114） | TASK-26 主控方裁定（执行前）第 1、2 条 |
| 调度器排队中的运行合并 | 「排队中的运行按最新时刻合并」从「等槽期间」扩大到「执行器排队期间」 | TASK-22 主控方裁定第 2 条；评审 L2（`t005-closure.md` §8） |
| RQ-061 | 只拦写、不拦读；不设启动期例外 | TASK-18 主控方裁定（F-16、F-57） |
| 定时源的调用链 | 卡牌计时器与其余定时源按 RQ-050 字面保持新根 | TASK-29 主控方裁定第 4 条（F-28，`t007-closure.md` §2） |
| 深度超限关闭的判定 | 以被拒链的 `SourceConnection` 非空判定「根来源是客户端连接」 | TASK-29 主控方裁定第 6(b) 条（F-29） |
| RQ-050 末句（传输层自发的关闭是新根）的实现精度 | CMD 已登记关闭、发送端还没执行时，传输层若先因交付失败或客户端断开而关闭了这个连接，这次断开仍按继承处理，与 RQ-050 末句的字面不符。影响只在链的字段与深度上；附录 A 担心的级联（交付失败 → 关同伴 → 同伴的清理通告再失败）中没有 CMD 登记的关闭，仍全是新根 | TASK-29 主控方裁定第 5 条（方案 A，`tasks/TASK-29.md:80`「交外部审核时要能对上」）；登记在 `t007-closure.md` §3 第 5 条、§4 第 2 条 |
| M-1～M-3 的判定口径 | S3 按 2 s 口径读；S4 无停止效力；收尾窗口与就绪探测按规则 P 识别（F-119） | TASK-20 主控方裁定；TASK-28 主控方裁定与回报后裁定；TASK-31、TASK-32 主控方裁定（`measurement/m3-summary.md` 文首） |
| 附录 F「改为执行器内访问」 | 扩大为普查名单：唯一出处是 `GameplayWriteEntryTableTests.Types` 与 `GameplayStateCensusTests.Exemptions`。覆盖面扩大，交审时说明 | TASK-30 主控方裁定第 1 条（`t009-criteria.md` §1 末、§7） |
| AC-019 的 b1、b2 | 「在基线上通过」对这两条读作「在基线上记录为已知缺陷，断言不改」 | TASK-34 主控方裁定第 6 条（`t009-closure.md` §5） |
| AC-022 的检索范围 | 现状文档（AGENTS.md、`Docs/Agent/`）为 0；范围外的一份过期现状描述列入 F-128 | TASK-33 主控方裁定第 2 条 |
| RQ-083「AGENTS.md 已登记的偶发项」 | 新栈根补上不归因证据并复现 0 次后，追加进登记表，再按已登记偶发处理 | TASK-34 主控方处置（Blocker 之后）第 1、2 条（`t009-closure.md` §2.2） |

## 5. 共享契约与语义的变化

| 变化 | 今天的含义 | 出处 |
|---|---|---|
| `NotificationOutcome.Delivered`（Abstractions） | 含义由「已交给传输」变为「已组装、编码并登记进发件箱」，连接已不在而被丢弃的也记作它；不改名，改名交子 Spec B（F-122） | TASK-23 主控方裁定第 4 条；AGENTS.md「领域事件与即时投影」 |
| `IGameMessageSender`／`INetworkMessageSender`／`IEventService.ProjectAsync`／`INetworkConnectionCloser` | 在作业内由「发送」变为「登记进本作业的发件箱」，消息在登记时编码；在执行器外调用是程序错误 | RQ-030、RQ-031；AGENTS.md「发件箱」 |
| `ICommandBus.SendAsync` | 两种语义：作业内内联；执行器外包成 `bus:` 作业入队并等待（过渡机制 T1） | Plan 共享决定；R3-2 |
| 编码的位置与次序 | 在登记点完成；F-97 把次序定为「先判位置、后编码」，推翻了 TASK-23 裁定里的次序部分 | `t009-criteria.md` §6 |
| 附录 F「改为执行器内访问」 | 扩大为普查名单（§4.2） | `t009-criteria.md` §1 末 |
| 队列层的泄漏流 | 带着已结束作业令牌的流入队被拒绝，记 `queue-leaked-flow` 取证 | TASK-29 主控方裁定第 2 条（`t007-closure.md` §1 末） |
| RQ-053(d) | 关连接引起的生命周期作业超限时，改作新根执行拆除，再关根连接 | TASK-29 主控方裁定第 1 条 |
| GM Cera 对外字段 | `reminder` 只剩 `attempted`／`offline`；提醒抛出时 HTTP 500（F-107） | TASK-24 |
| 队伍标题上限 256 B | 服务端策略，没有客户端上限证据（VC-9，F-117） | TASK-27 |
| 发送端日志级别 | 交付失败而传输层已认不出这条连接的，记 `Information` 并带后缀 `; the connection was already gone`。写端先完成的形态仍记 `Warning`，是已知缺口（F-127） | TASK-31（F-121）；AGENTS.md「发送端」 |

## 6. 验证结论

- **最终构建与正式全量**（`t009-closure.md` §2；原始输出 `logs/task-34-build-test-raw.txt`）：
  - 构建 0 警告、0 错误；
  - 总数 8315，通过 8294、失败 3、跳过 18。失败 3 ＝ 两条基线 `DimensionDungeonTests` ＋ 1 条 SQLite 并行释放偶发。后者是新栈根，经主控方处置补了不归因证据：`TestGameplay` 包装与失败路径无交集，复现 0 次。之后登记进 AGENTS.md，属 RQ-083 允许的范围。两点限定：
    - 推断的触发机制（进程级 `ClearAllPools` 与并行测试类）在 A 之前就有；A 在 Application.Tests 新增了 3 处这样的调用，与该机制有交集，可能提高了这条偶发的出现概率（推断，未观测；F-130）；
    - 有判别力的复现是整项目 3 次。单类 30 次在构造上不具判别力：这个类的 4 个文件都不调用 `ClearAllPools`，按上面的推断，单独跑它本来就不会复现；
  - 表征 10 通过、0 跳过；挂起转储 0；
  - 取证 73 条全在反例类与对照里，`exe:` 0；
  - 日志信号与 TASK-31 逐项对照，两项随负载变化的已逐条归因。
- **测试数量加法**（`t009-closure.md` §3）：7872 ＋ 596 － 153 ＝ 8315。ΣN 为 30 项，ΣD 为 8 项，加法写全在那一节。
- **测试文件改动**（`t009-closure.md` §4）：修改 363、新增 112、删除 7。断言或方法有变化的 71 个文件，每个都指得到处置记录；断言未改、只改了准备数据或驱动的 67 个文件逐个列出；修改过的线级文件里没有表示客户端可见变化的断言变化；可见变化逐条对到附录 G（§3）。
- **表征与金样本**（`t009-closure.md` §5）：表征的断言自引入点 `f01dc226` 起没有改，只去掉了 b1、b2 的 `Skip`；`Golden/` 与选角金样本 diff 都为空。
- **AC-009①**：193 个方法全部通过，共 332 行。TASK-33 记的 331 行少算了 1 行（`t009-closure.md` §3.3）。
- **测量 M-3**（`measurement/m3-summary.md` §1）：结论是「M-3 通过」。最关键的三个数：
  - 单条作业执行时长 max 269.3 ms，是 2 s 接受线的 0.13 倍；
  - (f) 冷启动最长单次停顿 1231.1 ms，是接受线的 0.62 倍，原目标 250 ms 的 4.9 倍；
  - ramp-A-2s 可持续吞吐 1163 包/秒。
  - 未达原目标值的项是后续性能优化（F-77～F-82、F-120），不阻断。
- **判据清单**：A 建立的判据 34 条，A 期间改写的判据 11 条，每条都有反例或变异（`t009-criteria.md` §3、§4）。
- **以下事项未做实机验收**：
  - 本结果的证据都是自动化测试与合成负载测量，不等同于 A21 客户端实机验收；
  - 测量用的是合成负载（`measurement/m3-summary.md` 文首）。

## 7. 过渡机制清单

正式清单在 `transition-mechanisms.md`，每项写有位置、存在理由、由 B 删除的方式（起点建议），以及 B 封存时可以直接运行的检索式。残留检索 S1～S8 中，归不进任何一类的命中为 0（同文件 §3）。

| 项 | 一句话 |
|---|---|
| T1 | `ICommandBus.SendAsync` 在作业内内联，在执行器外包成 `bus:` 作业入队并等待 |
| T2 | handler 在入站作业内顺序派发多条细粒度命令，每条 Transactional 命令一个事务，补偿由 handler 派发 |
| T3 | 五层管线（0／45／58／60／100）包在每一条细粒度命令外，45、58 依赖四个命令标记接口 |
| T4 | 请求级上下文是作业内的 AsyncLocal 帧（4 个类型、6 处），存在「没有帧就放行」的分支 |
| T5 | 细粒度命令失败时，`CommandLoggingBehavior` 与执行器各记一条 `Error` |
| T6 | 失败作业关闭来源连接，依赖作业已打开发件箱 |

## 8. 遗留问题及归属

按去向分组，每条一句话。完整内容见 `follow-ups.md`，去向汇总见 `t009-closure.md` §6。

**子 Spec B**
- F-07：每日任务、Premium、配额刷新在一个作业里遍历全部在线会话，是长作业，待拆分；
- F-12：附录 C 一半，即事务开启点与工作单元的对照；
- F-34：「每个消息类型一个协议 CMD」的归并，请求级帧改为显式数据；
- F-58：帧改为显式数据时，复核泄漏流的另一支；
- F-69：测试容器 10 秒排空看不见，B 继续逐单元续写耗时对比；
- F-86：卡牌协调器「预留冲突重试 3 次」，串行之后是否还需要，尚未判定；
- F-88：两处保留下来的当前性检查没有用例守着；
- F-93：两处保留下来的非锁语句没有用例守着；
- F-109：`representedSessions` 参数删门之后只剩空值校验；
- F-110：「失败作业总有发件箱」的结构保证（过渡机制 T6）；
- F-112：TASK-25 的包装各自持有独立发件箱，证明不了 RQ-031(b)；
- F-122：`NotificationOutcome.Delivered` 改名；
- F-124：「pvp 处理器是哪个」的注册断言今天只间接守着；
- F-127：写端先完成时的 Warning `Failed`，交 B 首个单元顺带修；
- F-128 ①：AGENTS.md「端到端示例：GameLogin」的旧名字；
- F-129（新增）：Channel 入站一支的 F-30 降级没有用例；
- 无编号：扇出变长字段的上限约定没有判据守着（AGENTS.md 已写明），是否设判据交子 Spec B。F-118 已在 TASK-33 关闭，按主控方处置第 4 条写在这里，不另开编号。

**后续性能优化（交主控方立项）**
- F-77～F-81：吞吐、入队等待、并发登录、冷启动停顿、选角 p99.9，都未达原目标值；
- F-82：合成负载覆盖不到的项；
- F-120：灌包连接自身的应答速率约减半。

**交主控方另行立项**
- F-01：13 个内容源各持一份「id → 路径」字典，约 500 MB；
- F-113：人群项没有人数上限；
- F-114：没有生产写入方的字段没有长度约束；
- F-116 (a)(b)：发送端或登记点的通用防线；
- F-125：PVF 缺 `[minimum level]` 时奖励结算抛错，属生产健壮性缺口；
- F-130（新增）：进程级 `ClearAllPools` 与并行测试类的关系；A 新增的 3 处可改为按路径 `ClearPool`，或放进同一个 collection（测试基础设施）；
- F-128 ②③：RQ-100 点名范围之外的两份文档过期或不完整；
- 测试基础设施与取证缺口：F-37、F-38、F-39、F-45、F-55、F-71、F-103。

**用户**
- F-117：队伍标题的客户端上限，需要 Windows 端的 A21 客户端或其内存转储取证。

**外部审核时说明的已接受偏离**
- F-107：GM Cera 对外字段的变化；
- F-108：AC-011 线级第 3 条的证据降级；
- F-33：只读或缺失上限特性时，超过 32 KiB 的请求体落临时文件，偏离 TASK-14「只用内存」（TASK-31 主控方裁定第 3 条）。

**信息（不需要动作）**
- F-40、F-41、F-42、F-44、F-46、F-70、F-95、F-96、F-100；
- 条件触发的 F-06：新增预热 W2 认不出的写法时，人工把关；
- 条件触发的 F-43：增加修改 GM 口令的写入方时，补用例。

**判据与证明的已知边界**
- F5：`CharacterContainerDomainService` 的 `Find`／`Get`／`GetContainers` 在事务内登记参与者，却按读归类，是有意的例外（`t009-criteria.md` §10）；
- F3：普查认不出藏在其他程序集类型后面的状态（`t009-criteria.md` §10）；
- F-06：预热识别的边界；
- `t006-closure.md` §11 列出的证明边界，例如 DR-16 的占位作业、F-61 的打包进程取证；
- VC-10 是组合证据，第 3 句的衔接是读码；
- F-33 的只读或缺失情形，证据是推理证据；
- 新栈根的成因是读码推断：进程级 `ClearAllPools` 与并行测试，没有句柄身份可以证实；A 新增的 3 处调用与推断的触发机制有交集（`t009-closure.md` §2.2，F-130）；
- RQ-050 末句的实现精度：CMD 已登记关闭、发送端还没执行时，传输层先断开，这次断开仍按继承处理，与 RQ-050 末句的字面不符（TASK-29 主控方裁定第 5 条；`t007-closure.md` §3 第 5 条、§4 第 2 条）。去向：随子 Spec B 复核，或在主控方立项的发送端精度改进中处理。

## 9. AC／SC 状态总表

状态词：「满足」只用于证据都在冻结点上、证据用例在本次正式全量中通过的；「满足，附条件」在表内写明条件与去向。

### 9.1 AC（A 的全部，含跨 A／B 的 A 一半）

| AC | 状态 | 条件与去向 | 证据位置 |
|---|---|---|---|
| AC-001 | 满足 | — | `GameplayExecutorOrderTests.Concurrent_producers_see_their_work_run_in_enqueue_order_one_at_a_time`、`GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs`、`GameplayExecutorOrderTests.Every_shard_key_selects_the_one_executor`（`tasks/TASK-04.md` Result） |
| AC-002 | 满足 | — | 入队后等待：`GameplayExecutorSelfWaitTests.Enqueueing_from_inside_a_job_throws_logs_the_chain_and_fails_the_job`（TASK-04）；等待网络：`OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error`（N4，`t006-test-dispositions.md` TASK-23 §6） |
| AC-003 | 满足 | — | `t004-closure.md` §3 |
| AC-004 | 满足 | — | `t004-closure.md` §3 |
| AC-005 | 满足 | — | 执行位置一半：`PasswordComputationLimiterTests`（`t004-closure.md` §3）；测量一半：「M-3 满足」（`measurement/m3-summary.md` §1） |
| AC-006 | 满足 | — | `t009-docs-and-appendices.md` §3、§4；`t009-criteria.md` §1、§2 |
| AC-007 | 满足，附条件 | 「此前登记的消息」按连接计（TASK-23 主控方裁定第 2 条），外部审核时说明 | `t006-closure.md` §6 |
| AC-009（A 一半） | 满足 | — 。TASK-33 记的附条件「①的通过待最终全量确认」已解除：193 个方法、332 行全部通过 | ①②④ `t009-docs-and-appendices.md` §10、§7；③ `t009-logs-and-cases.md` §5；`t009-closure.md` §3.3 |
| AC-011 | 满足，附条件 | 线级第 3 条降到发送端层（F-108），外部审核时说明 | `t006-closure.md` §6 |
| AC-012 | 满足，附条件 | XO-2～XO-7 的「超过上限即关闭」由发送端通用用例承担；XO-6 标 Required 的近似已写成 VC-10 | `t006-closure.md` §5；`t009-docs-and-appendices.md` §8 |
| AC-013 | 满足，附条件 | 人群项按适用范围成立（城镇同区他人 ≤ 782 人、PvP 大厅他人 ≤ 133 人），去向 F-113；没有生产写入方的字段不计入，去向 F-114 | `t006-closure.md` §6 |
| AC-014 | 满足，附条件 | DR-16 的证据用占位作业代表进入序列，不证明真实选角序列中称号事件的入队时机（`t006-closure.md` §11） | `t006-closure.md` §3 |
| AC-015 | 满足 | — | `t004-closure.md` §3；`t005-closure.md` §5 |
| AC-016 | 满足 | — | `t007-closure.md` §1 |
| AC-017 | 满足 | — | `t005-closure.md` §2；`t006-closure.md` §2；`t009-docs-and-appendices.md` §6 |
| AC-018（A 一半） | 满足 | — | `t009-criteria.md` §3（11 条改写）、§4（34 条新判据） |
| AC-019 | 满足 | b1、b2 的记法属 Spec 解读（§4.2），不是附条件 | `t009-closure.md` §5 |
| AC-020 | 满足 | — | `t009-closure.md` §2～§4 |
| AC-021（A 一半） | 满足 | — | `measurement/m3-summary.md` §1 |
| AC-022（A 一半） | 满足 | 检索范围按 TASK-33 主控方裁定第 2 条；范围外的 F-128 ②③ 交主控方 | `t009-docs-and-appendices.md` §11 |
| RQ-024（A 一半） | 满足 | — | `t009-docs-and-appendices.md` §9 |
| RQ-100（A 一半） | 满足 | — | `t009-docs-and-appendices.md` §1 |
| VC-1～VC-9 | 满足 | — | §3 |
| VC-10 | 满足，附条件 | 证据为组合，第 3 句的衔接是读码 | `t009-docs-and-appendices.md` §8 |

移交子 Spec B（R3-1）、不在本表的有：AC-008、AC-010，以及 AC-009、AC-018、AC-022 的 B 一半。

### 9.2 SC（子契约的 SC-1、SC-3～SC-12）

| SC | 摘要（子契约） | 对应的 AC 与记录 | 状态 |
|---|---|---|---|
| SC-1 | 唯一执行器，全部入口经唯一入队点作为作业进入；执行器外的部分逐项列明 | AC-001、AC-003、AC-004、AC-006；附录 A／B 对账（`t009-docs-and-appendices.md` §3、§4）；RQ-061（`t009-criteria.md` §1、§2） | 满足 |
| SC-3 | 出站在提交后发出；顺序、批次、必达、隔离与跨连接顺序成立 | AC-007、AC-011、AC-012、AC-013、VC-10；`t006-closure.md` | 满足，附条件（AC-007、AC-011、AC-012、AC-013、VC-10 的条件见 §9.1，都已有去向） |
| SC-4 | 门、信号量、全局事务门及其管线行为、重试循环删除；其余原语逐项去留 | AC-017；`t005-closure.md` §2、§3；`t009-docs-and-appendices.md` §6；`transition-mechanisms.md` §3 S2、S3 | 满足 |
| SC-5 | 事件组件保留，Reliable、LiveOnly 与重试经执行器执行，语义不变 | AC-015；`t004-closure.md` §3；`t007-closure.md` | 满足 |
| SC-6 | 深度上限、调用链日志与超限处理覆盖全部来源 | AC-016；`t007-closure.md` | 满足，附条件。条件：RQ-050 末句的实现精度——CMD 已登记关闭、发送端还没执行时传输层先断开，这次断开仍按继承处理（TASK-29 主控方裁定第 5 条，`t007-closure.md` §4 第 2 条）；去向：随子 Spec B 复核，或在主控方立项的发送端精度改进中处理 |
| SC-7 | 依赖投递结果的每项行为都有承接，可见变化逐条列出 | AC-014；附录 E、G；VC-1～VC-10（§3） | 满足，附条件（AC-014 的 DR-16；VC-10 的组合证据） |
| SC-8 | 构建与全量测试、线级与表征断言、选角端到端证据成立 | AC-019、AC-020；`t009-closure.md` §2～§5；`SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript` | 满足 |
| SC-9 | 有实测数字与口径，单条 CMD ≤ 2 s 即接受 | AC-005 的测量一半、AC-021；`measurement/m3-summary.md` | 满足 |
| SC-10 | 文档与 A 封存状态一致，过渡机制写成今天的状态与已知缺口 | RQ-100 与 AC-022 的 A 一半；`t009-docs-and-appendices.md` §1、§11；`transition-mechanisms.md` §4 | 满足 |
| SC-11 | 最终汇报列出结果、可见变化、遗留问题、过渡机制清单与验证结论，并复述三项默示决定 | 本文（§1～§3、§6～§8；§2 是复述） | 满足 |
| SC-12 | 过渡机制逐项列明，清单之外没有残留 | `transition-mechanisms.md`；`logs/task-34-transition.txt` | 满足 |

封存时 `multi-spec-map.md` 各行的建议值（Progress、Evidence、Remaining gap、Constraint）见 `t009-closure.md` §7，最终值由主控方定。
