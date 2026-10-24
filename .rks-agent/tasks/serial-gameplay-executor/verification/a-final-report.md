# 结果 A 的最终汇报（spec-001：单执行器、提交后发件箱与删锁）

- 日期 2026-10-24。冻结点 `2bdb1afd`；Spec 基准 `a02bce46`；规划检查点 `b76504bb`。本单元开始时 HEAD 为 `51c294fa`。
- **TASK-35 修订（2026-10-24，基准 `9aeb638c`）**：按三份完整结果评审（`reviews/spec-001-final-review-code.md`、`-tests.md`、`-spec.md`）改了 §3、§6、§8、§9，新增 §10「交外部审核重点」。TASK-35 改了生产代码（§10 最后一组），**代码冻结点由 `2bdb1afd` 前移到 TASK-35 的提交**；本文其余各处写的「冻结点 `2bdb1afd`」指 TASK-35 之前的那次冻结，M-3 的数字仍代表封存状态的理由见 `tasks/TASK-35.md` 文末「第 5 条：热路径」。
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
| VC-3 | 组队进本或两塔进入时某成员发送失败：只断开该成员，其他成员与请求者照常进入 | 扩展到两塔进入 | `PartySelectDungeonHandlerTests.A_follower_whose_transport_fails_is_closed_alone_and_the_party_still_enters`；两塔各一条 `A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower` | TASK-23 第 1 轮评审 F1 的主控方处置第 1 条（`50d7bce4`）。**事后追认**：执行时命中停止条件 2 而没有停下，评审指出后才接受（§10） |
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

- **TASK-35 的构建与正式全量**（本单元改了生产代码，以这一次为准）：见 `logs/task-35-build-test.txt`：构建 0 警告；总数 8350 ＝ 8315 ＋ 35，通过 8329、失败 3、跳过 18（8329 ＋ 3 ＋ 18 ＝ 8350）。失败 3 ＝ 两条基线 `DimensionDungeonTests` ＋ 1 条 SQLite 并行释放偶发，已按主控方处置 (A) 登记为第五个栈根（`tasks/TASK-35.md`「Result」及文末处置；不归因依据与整项目复现 0／3 见 `t009-closure.md` §2.3，触发机制仍是未观测推断）；表征 10 通过、0 跳过；取证 73 条，反例类与对照之外 0。原始取证 `evidence/t35-formal-ev/`。下面一条是 TASK-34 在冻结点 `2bdb1afd` 上的那一次，保留作对照。
- **最终构建与正式全量（TASK-34）**（`t009-closure.md` §2；原始输出 `logs/task-34-build-test-raw.txt`；原始取证已复制到 `evidence/t34-formal-ev/`）：
  - 构建 0 警告、0 错误；
  - 总数 8315，通过 8294、失败 3、跳过 18。失败 3 ＝ 两条基线 `DimensionDungeonTests` ＋ 1 条 SQLite 并行释放偶发。后者是新栈根，经主控方处置补了不归因证据：`TestGameplay` 包装与失败路径无交集，复现 0 次。之后登记进 AGENTS.md，属 RQ-083 允许的范围。两点限定：
    - 推断的触发机制（进程级 `ClearAllPools` 与并行测试类）在 A 之前就有；A 在 Application.Tests 新增了 3 处这样的调用，与该机制有交集，可能提高了这条偶发的出现概率（推断，未观测；F-130）；
    - 有判别力的复现是整项目 3 次。单类 30 次在构造上不具判别力：这个类的 4 个文件都不调用 `ClearAllPools`，按上面的推断，单独跑它本来就不会复现；
  - 表征 10 通过、0 跳过；挂起转储 0；
  - 取证 73 条全在反例类与对照里，`exe:` 0。`exe:` 0 只能读作「没有看到」，不能读作「已证明没有」：打包进程一侧只认得打印到输出里的断言异常，被吞掉又没打印的看不见（F-61 残余），正式全量里也没有一次打包进程真实产出 `exe:` 记录的对照；
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
- F-130（TASK-35 由「交主控方另行立项」改来）：进程级 `ClearAllPools` 与并行测试类，B 首个单元只改测试处理；
- F-131（TASK-35 新增）：RQ-050 末句的实现精度，B 把关连接并入协议 CMD 时复核；
- F-132（TASK-35 新增，前提提醒）：提交后组装失败会被记成「已确认回滚」，B 在工作单元内登记组装之前先分开（`transition-mechanisms.md` §5）；
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
- F-128 ②③：RQ-100 点名范围之外的两份文档过期或不完整；
- 测试基础设施与取证缺口：F-37、F-38、F-39、F-45、F-55、F-71、F-103。

**用户**
- F-117：队伍标题的客户端上限，需要 Windows 端的 A21 客户端或其内存转储取证。

**外部审核**
- 完整的交审重点在 §10，不在这里另列。F-107、F-108、F-33 是 §10 中的三行。

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
- RQ-050 末句的实现精度：F-131（去向子 Spec B），见 §10。

## 9. AC／SC 状态总表

状态词（TASK-35 统一口径）：
- 「满足」：证据都在冻结点上，证据用例在正式全量中通过，而且结论不依赖下面三类中的任何一类；
- 「满足，附条件」：证据成立，但结论依赖以下之一，条件栏写出是哪一类、哪一行：(1) §4.2 的一条解读；(2) 证明边界（证据只覆盖一部分，或是读码、组合证据）；(3) 实现与需求字面不符，或保留下来的保护没有用例守着；
- SC 的状态取它所列 AC 中最弱的一个；
- 每一项附条件背后的授权来源与时点见 §10。

### 9.1 AC（A 的全部，含跨 A／B 的 A 一半）

| AC | 状态 | 条件与去向 | 证据位置 |
|---|---|---|---|
| AC-001 | 满足 | — | `GameplayExecutorOrderTests.Concurrent_producers_see_their_work_run_in_enqueue_order_one_at_a_time`、`GameplayExecutorOrderTests.A_failing_cmd_reaches_its_caller_and_the_next_cmd_still_runs`、`GameplayExecutorOrderTests.Every_shard_key_selects_the_one_executor`（`tasks/TASK-04.md` Result） |
| AC-002 | 满足 | — | 入队后等待：`GameplayExecutorSelfWaitTests.Enqueueing_from_inside_a_job_throws_logs_the_chain_and_fails_the_job`（TASK-04）；等待网络：`OutboundRealSocketTests.A_job_that_writes_to_a_connection_directly_fails_as_a_program_error`（N4，`t006-test-dispositions.md` TASK-23 §6） |
| AC-003 | 满足 | — | `t004-closure.md` §3 |
| AC-004 | 满足 | — | `t004-closure.md` §3 |
| AC-005 | 满足，附条件 | (1) §4.2「M-1～M-3 的判定口径」 | 执行位置一半：`PasswordComputationLimiterTests`（`t004-closure.md` §3）；测量一半：「M-3 满足」（`measurement/m3-summary.md` §1） |
| AC-006 | 满足，附条件 | (1) §4.2「RQ-061」（只拦写、不拦读；不设启动期例外）与「调度器排队中的运行合并」（RQ-013） | `t009-docs-and-appendices.md` §3、§4；`t009-criteria.md` §1、§2 |
| AC-007 | 满足，附条件 | (1) §4.2「RQ-012、RQ-031(c)「此前登记的消息」」：按连接计（TASK-23 主控方裁定第 2 条） | `t006-closure.md` §6 |
| AC-009（A 一半） | 满足，附条件 | (3) F-88、F-93：A 删锁时保留下来的当前性检查（抽查 `RecoverStaminaHandler` 的 `IsInWorld`、`TeleportHandler` 的 `session.Owns(previous)`）、`SelectedSessionSafety.BlockAsync` 的 `DiscardExact` 与 `EffectOperationLedger` 的 `DemandCurrent(recipient)`，删掉后没有用例变红；AC-009 的字面（既有测试断言不变并通过）成立，「由既有测试证明保护原样保留」对这几处不成立。去向：子 Spec B 迁移这些语句之前先补守护用例。TASK-33 记的另一项附条件「①的通过待最终全量确认」已解除：193 个方法、332 行全部通过 | ①②④ `t009-docs-and-appendices.md` §10、§7；③ `t009-logs-and-cases.md` §5；`t009-closure.md` §3.3 |
| AC-011 | 满足，附条件 | (2) 线级第 3 条降到发送端层（F-108）；(1) §4.2「RQ-012、RQ-031(c)」按连接计 | `t006-closure.md` §6 |
| AC-012 | 满足，附条件 | (2) XO-2～XO-7 的「超过上限即关闭」由发送端通用用例承担；XO-6 标 Required 的近似已写成 VC-10。RQ-033 的结论句已按实现更正为「每个未完成的前序各等一个上限」（R4，§10） | `t006-closure.md` §5；`t009-docs-and-appendices.md` §8 |
| AC-013 | 满足，附条件 | (1)(2) §4.2「AC-013 的人群项」：按适用范围成立（城镇同区他人 ≤ 782 人、PvP 大厅他人 ≤ 133 人），去向 F-113；没有生产写入方的字段不计入，去向 F-114。(3) §4.2「RQ-034」：编码改在登记点，与字面不符 | `t006-closure.md` §6 |
| AC-014 | 满足，附条件 | (2) DR-16 的证据用占位作业代表进入序列，不证明真实选角序列中称号事件的入队时机（`t006-closure.md` §11） | `t006-closure.md` §3 |
| AC-015 | 满足 | — | `t004-closure.md` §3；`t005-closure.md` §5 |
| AC-016 | 满足，附条件 | (3) RQ-050 末句的实现精度与字面不符（F-131），与 SC-6 一致；(1) §4.2「定时源的调用链」「深度超限关闭的判定」 | `t007-closure.md` §1 |
| AC-017 | 满足，附条件 | (1) §4.2「RQ-061」与「附录 F『改为执行器内访问』」（扩大为普查名单） | `t005-closure.md` §2；`t006-closure.md` §2；`t009-docs-and-appendices.md` §6 |
| AC-018（A 一半） | 满足 | — | `t009-criteria.md` §3（11 条改写）、§4（34 条新判据） |
| AC-019 | 满足，附条件 | (1) §4.2「AC-019 的 b1、b2」：「在基线上通过」读作「在基线上记录为已知缺陷，断言不改」 | `t009-closure.md` §5 |
| AC-020 | 满足，附条件 | (1) §4.2「RQ-083『AGENTS.md 已登记的偶发项』」（TASK-34 第四个栈根与 TASK-35 第五个栈根的登记，`t009-closure.md` §2.2、§2.3）。TASK-35 的正式全量见 §6 第一条 | `t009-closure.md` §2～§4 |
| AC-021（A 一半） | 满足，附条件 | (1) §4.2「M-1～M-3 的判定口径」 | `measurement/m3-summary.md` §1 |
| AC-022（A 一半） | 满足，附条件 | (1) §4.2「AC-022 的检索范围」（TASK-33 主控方裁定第 2 条）；范围外的 F-128 ②③ 交主控方 | `t009-docs-and-appendices.md` §11 |
| RQ-024（A 一半） | 满足 | — | `t009-docs-and-appendices.md` §9 |
| RQ-100（A 一半） | 满足 | — | `t009-docs-and-appendices.md` §1 |
| VC-1～VC-5、VC-7～VC-9 | 满足 | — | §3 |
| VC-6 | 满足，附条件 | (1) §4.2「VC-6 的取值」（F-107） | §3 |
| VC-10 | 满足，附条件 | (2) 证据为组合，第 3 句的衔接是读码 | `t009-docs-and-appendices.md` §8 |

移交子 Spec B（R3-1）、不在本表的有：AC-008、AC-010，以及 AC-009、AC-018、AC-022 的 B 一半。

### 9.2 SC（子契约的 SC-1、SC-3～SC-12）

| SC | 摘要（子契约） | 对应的 AC 与记录 | 状态 |
|---|---|---|---|
| SC-1 | 唯一执行器，全部入口经唯一入队点作为作业进入；执行器外的部分逐项列明 | AC-001、AC-003、AC-004、AC-006；附录 A／B 对账（`t009-docs-and-appendices.md` §3、§4）；RQ-061（`t009-criteria.md` §1、§2） | 满足，附条件（AC-006 依赖 §4.2 的两条解读） |
| SC-3 | 出站在提交后发出；顺序、批次、必达、隔离与跨连接顺序成立 | AC-007、AC-011、AC-012、AC-013、VC-10；`t006-closure.md` | 满足，附条件（AC-007、AC-011、AC-012、AC-013、VC-10 的条件见 §9.1，都已有去向） |
| SC-4 | 门、信号量、全局事务门及其管线行为、重试循环删除；其余原语逐项去留 | AC-017；`t005-closure.md` §2、§3；`t009-docs-and-appendices.md` §6；`transition-mechanisms.md` §3 S2、S3 | 满足，附条件（AC-017 依赖 §4.2 的两条解读） |
| SC-5 | 事件组件保留，Reliable、LiveOnly 与重试经执行器执行，语义不变 | AC-015；`t004-closure.md` §3；`t007-closure.md` | 满足 |
| SC-6 | 深度上限、调用链日志与超限处理覆盖全部来源 | AC-016；`t007-closure.md` | 满足，附条件。条件：RQ-050 末句的实现精度——CMD 已登记关闭、发送端还没执行时传输层先断开，这次断开仍按继承处理（TASK-29 主控方裁定第 5 条，`t007-closure.md` §4 第 2 条）；F-131，去向子 Spec B |
| SC-7 | 依赖投递结果的每项行为都有承接，可见变化逐条列出 | AC-014；附录 E、G；VC-1～VC-10（§3） | 满足，附条件（AC-014 的 DR-16；VC-10 的组合证据） |
| SC-8 | 构建与全量测试、线级与表征断言、选角端到端证据成立 | AC-019、AC-020；`t009-closure.md` §2～§5；`SelectCharacterSuccessCharacterizationTests.Login_create_and_select_produce_the_recorded_transcript`；TASK-35 的正式全量 | 满足，附条件（AC-019、AC-020 各依赖 §4.2 的一条解读） |
| SC-9 | 有实测数字与口径，单条 CMD ≤ 2 s 即接受 | AC-005 的测量一半、AC-021；`measurement/m3-summary.md` | 满足，附条件（判定口径依赖 §4.2「M-1～M-3 的判定口径」） |
| SC-10 | 文档与 A 封存状态一致，过渡机制写成今天的状态与已知缺口 | RQ-100 与 AC-022 的 A 一半；`t009-docs-and-appendices.md` §1、§11；`transition-mechanisms.md` §4 | 满足，附条件（AC-022 的检索范围按 §4.2） |
| SC-11 | 最终汇报列出结果、可见变化、遗留问题、过渡机制清单与验证结论，并复述三项默示决定 | 本文（§1～§3、§6～§8、§10；§2 是复述） | 满足 |
| SC-12 | 过渡机制逐项列明，清单之外没有残留 | `transition-mechanisms.md`；`logs/task-34-transition.txt` | 满足 |

封存时 `multi-spec-map.md` 各行的建议值（Progress、Evidence、Remaining gap、Constraint）见 `t009-closure.md` §7，最终值由主控方定。

## 10. 交外部审核重点（TASK-35 新增）

用户把执行中的决定交给主控方，前提是 GPT Astra 事后审核（根契约 RI-16）。所以凡是改变了验收、放宽或新增了可见行为、与需求字面不符、或改变了共享契约含义的地方，都列在这里；没有用户本人原话的，授权来源写「主控方」。本表是交审重点的唯一完整清单，其余各节只引用它。

栏目说明：
- **性质**：改变验收、放宽或新增可见行为、与字面不符、契约含义变化、证据降级、事实更正、已知缺口等；
- **授权来源**：用户本人（有原话），或主控方（裁定、处置、单元合同）；
- **授权时点**：「事前」指在实现落定、提交之前裁定；「事后追认」指先发生、后由主控方接受；「事后补列」指变化先在代码里、后写进附录 G。

| # | 项 | 性质 | 授权来源 | 授权时点 | 证据位置 |
|---:|---|---|---|---|---|
| 1 | RQ-091 接受线改为单条 CMD ≤ 2 s，原阈值改作对照用的目标值 | 改变验收 | **用户本人**（2026-10-22 原话，RQ-091 修订段逐字引用；根契约 RI-15） | 事前（M-1 之前） | `c3115f9a`；§4.1；`measurement/m3-summary.md` §1 |
| 2 | R3 拆分：A／B 两个子 Spec，A 的验收边界收窄，AC-008、AC-010 与跨 A／B 条目的 B 一半移交 B | 改变验收（收窄 A 的边界） | **用户本人**（2026-10-23 结构决定，`multi-spec-map.md`「Multi-Spec Boundary」引原话） | 事前（改道生效后才继续执行） | `bc735cd0`；`spec.md` R3；`verification/reroute-t008.md` |
| 3 | AC-007、RQ-012、RQ-031(c)「此前登记的消息」按连接计 | 解读（收窄了字面可读出的范围：跨连接的先后只由附录 D 的有序组表达） | 主控方（TASK-23 主控方裁定第 2 条） | 事前 | §4.2；`t006-closure.md` §6、§10；§9.1 AC-007 |
| 4 | VC-3 扩展到两塔进入（XO-3、XO-4 交付失败时只断开该成员） | 放宽可见行为 | 主控方（TASK-23 第 1 轮评审 F1 的主控方处置第 1 条） | **事后追认**：TASK-23 执行时命中停止条件 2（可见行为改变）而没有停下，评审 F1 指出后主控方才接受（`tasks/TASK-23.md:801`、`:828`、`:932`） | `50d7bce4`；§3 VC-3；`t006-closure.md` §11 |
| 5 | VC-9：队伍标题超过 256 B 时请求被拒、发送者连接被关闭 | 新增可见行为 | 主控方（TASK-27 主控方裁定第 3 条） | 事前 | `f7bf0aa3`；§3 VC-9；`t006-closure.md` §12。256 B 没有客户端证据（F-117，交用户） |
| 6 | F-115 的修复与 VC-9：T-006 的队列上限带来一条可由改包客户端触发的第三方断线；修法是服务端标题上限，修法本身就是 VC-9 | A 引入的缺陷，修复时新增可见行为 | 主控方（TASK-26 第 1 轮评审后处置归 TASK-27；TASK-27 主控方裁定第 3 条） | 事前（TASK-27 执行前） | `follow-ups.md` F-115、F-117；`t006-closure.md` §12 |
| 7 | VC-10：XO-6 阶段推进中某收件人交付失败，同组其后的条目都不再交付 | 新增可见行为 | 主控方（TASK-30 主控方裁定第 3 条；TASK-33 主控方裁定第 5 条） | **事后补列**：变化从 TASK-23（F-98）起就在代码里，到 R4（TASK-33）才写进附录 G；在此之前按 RQ-082 是没有列明的可见变化 | §3 VC-10；F-98；`t009-docs-and-appendices.md` §8；§9.1 VC-10（附条件：组合证据） |
| 8 | RQ-053(d)：关连接引起的生命周期作业超限时，改作新根执行拆除，再关根连接 | 新增需求条文 | 主控方（TASK-29 主控方裁定第 1 条） | 事前（写入 `a9c79e98` 后实现） | `t007-closure.md:24`；§5 |
| 9 | RQ-050 与 RQ-053(a) 的需求文字澄清（`53c72543`） | 澄清 | 主控方。RQ-050：TASK-02 第 1 轮评审 N-1 的处置；RQ-053(a)：执行方在提交前写入，主控方提交时接受 | RQ-050 事前；RQ-053(a) **提交即接受＋事后追认**（TASK-34） | §4.1 表内 ② |
| 10 | RQ-050 末句（传输层自发的关闭是新根）的实现精度 | 与字面不符 | 主控方（TASK-29 主控方裁定第 5 条，方案 A，`tasks/TASK-29.md:80`） | 事前 | §4.2；`t007-closure.md` §3 第 5 条、§4 第 2 条；F-131（去向子 Spec B）；§9.1 AC-016、§9.2 SC-6 |
| 11 | `NotificationOutcome.Delivered`（Abstractions）的含义由「已交给传输」变为「已组装、编码并登记进发件箱」，不改名 | 契约含义变化 | 主控方（TASK-23 主控方裁定第 4 条） | 事前 | §5；F-122（改名交 B）；AGENTS.md「领域事件与即时投影」 |
| 12 | RQ-034「整批编码……在发送端保持」：编码改在登记点完成，发送端只收帧 | 与字面不符（`t006-closure.md` §10 的定性；§4.2 记在解读表里） | 主控方（TASK-23 主控方裁定第 1 条备选 B）；次序部分由 TASK-30 主控方裁定第 2 条（F-97）推翻 | 事前 | §4.2；§5；`t009-criteria.md` §6；§9.1 AC-013 |
| 13 | 附录 A 第 4 行删去 `lifecycle:close-superseded`：顶替后的关闭由那条登录 CMD 登记 | 附录改写（入口来源少一项） | 主控方（TASK-23 主控方裁定第 6 条） | 事前 | `50d7bce4`；§4.1 |
| 14 | 附录 F「改为执行器内访问」扩大为普查名单（唯一出处 `GameplayWriteEntryTableTests.Types` 与 `GameplayStateCensusTests.Exemptions`） | 附录覆盖面扩大（收紧） | 主控方（TASK-30 主控方裁定第 1 条） | 事前 | `122576d5`；§4.2；`t009-criteria.md` §1 末、§7 |
| 15 | RQ-031(d) 末句与 AC-011 第 4 条：「封锁并关闭」更正为「封锁，入站时来源连接关闭」 | 事实更正（基准 `AltarProjection.cs` 只封锁、不关闭参与者，规格维度评审已核实） | 主控方（TASK-24 主控方裁定第 1 条） | 事前 | `1ee1c91e`；`t006-closure.md` §10 |
| 16 | F-33：上限特性只读或缺失时，超过 32 KiB 的 GM 请求体落临时文件，偏离 TASK-14「只用内存」 | 与单元合同不符（推理证据） | 主控方（TASK-31 主控方裁定第 3 条） | 事前 | 附录 B（R4 一行）；`t009-logs-and-cases.md` §6 |
| 17 | F-107：GM Cera 对外字段 `reminder` 只剩 `attempted`／`offline`；提醒抛出时 HTTP 500、`outcome` 为 `unknown` | 对外接口变化（VC-6 的取值） | 主控方（TASK-24 主控方裁定第 2 条、评审后处置第 1 条） | 事前 | §3 VC-6；§4.2；§9.1 VC-6 |
| 18 | F-108：AC-011 线级第 3 条的证据降到「执行器 ＋ 真实登记适配器 ＋ 真实发送端」层 | 证据降级 | 主控方（TASK-24 主控方裁定第 3 条；TASK-25 主控方裁定第 5 条） | 事前 | `t006-closure.md` §6；§9.1 AC-011 |
| 19 | F-125：PVF 缺 `[minimum level]` 时奖励结算抛错 | 已知缺口（生产健壮性，A 之前就有） | 主控方（交另行立项） | —（未处理，有去向） | `follow-ups.md` F-125 |
| 20 | F-127：写端先完成时发送端仍记 Warning 的 `Failed` | 已知缺口（日志级别） | 主控方（TASK-34 主控方裁定，交 B 首个单元） | —（未处理，有去向） | `follow-ups.md` F-127；§5 |
| 21 | F-130：A 新增 3 处进程级 `ClearAllPools`，与 SQLite 并行释放偶发推断的机制有交集；TASK-35 的新用例又增加了调用次数 | 已知缺口（测试基础设施，推断） | 主控方（TASK-34 主控方处置第 4 条；TASK-35 合同把去向改为 B 首个单元） | 事后（TASK-34 正式全量之后发现） | `t009-closure.md` §2.2、§2.3；`follow-ups.md` F-130 |
| 22 | AC-009（A 一半）由「满足」改为「满足，附条件」（F-88、F-93：保留下来的当前性检查与 `DiscardExact`、`DemandCurrent(recipient)` 没有用例守着） | 状态更正（披露） | 主控方（TASK-35 合同第 4 条 M-2） | 事后（完整结果评审后） | §9.1 AC-009；`t009-closure.md` §1、§7.1 |
| 23 | RQ-033 结论句改为「后继为每个未完成的前序各等至多一个上限」 | 需求结论句与实现对齐（放宽了原句给出的上界；实现与规则句不变） | 主控方（TASK-35 合同第 4 条 L2） | 事后（完整结果评审后） | `spec.md` R4 追加行；`reviews/task-05-result-review.md:69` |
| 24 | RQ-061 正文去掉「附录 B 的启动期例外除外」 | 事实更正（附录 B 早已不设这个例外） | 主控方（TASK-35 合同第 4 条 L-5） | 事后 | `spec.md` R4 追加行 |
| 25 | TASK-23 处置记录 R8、R12 的三处「承接」不成立，TASK-35 补两条用例并追加更正 | 证据缺口更正 | 主控方（TASK-35 合同第 3 条） | 事后 | `t006-test-dispositions.md` 文末「TASK-35 更正」 |
| 26 | **TASK-35 的生产修正（评审 L1）**：20 个经 `TickAsync` 的定时托管服务与登录过期清理，在作业以黏滞程序错误失败时会结束，宿主按默认 `StopHost` 停机（A 引入的失效模式）。修法：`GameplayTimerWork.TickAsync` 统一吸收作业失败（执行器记 `Error`，这里记不附异常的 `Warning`，服务继续下一拍），`LoginSessionMaintenanceService` 改为经它入队；另把 XO-1 的有序组写成块作用域（语义等价，评审 L3 的前提不成立，见 `tasks/TASK-35.md`） | 封存前修复 A 引入的缺陷；代码冻结点前移 | 主控方（TASK-35 合同第 1、2 条；修法由执行者在两条中选定） | 事后（完整结果评审后、封存前） | `tasks/TASK-35.md` Detailed Plan §1、Result；`TimerSourceJobFailureTests`、`GameplayTimerWorkTests`；`logs/task-35-*.txt` |

合计 26 项：用户本人授权 2 项（第 1、2 项），主控方授权 24 项，2 ＋ 24 ＝ 26。主控方授权的 24 项中：事后追认、事后补列或事后决定 9 项（第 4、7、9 项与第 21～26 项，3 ＋ 6 ＝ 9）；未处理、只有去向的已知缺口 2 项（第 19、20 项）；其余 13 项为事前（第 3、5、6、8、10～18 项，4 ＋ 9 ＝ 13）；9 ＋ 2 ＋ 13 ＝ 24。
