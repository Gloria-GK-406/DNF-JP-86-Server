---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/TaskContract.md
target_revision: 1
verdict: changes_required
reviewer: independent subagent (reviewing-work, deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：serial-gameplay-executor（revision 1）

## 范围

- 目标：`.rks-agent/tasks/serial-gameplay-executor/TaskContract.md`，frontmatter `revision: 1`、`state: ready`。
- 重点：
  - 是否忠实表达用户原话与对齐决定；
  - Root Intent Coverage 的处置是否有依据；
  - 是否把 Agent 机制固化成了硬约束；
  - Scope 与 Success Conditions 是否自洽、可观察；
  - 授权与停点；
  - ready 条件。
- 只审查，不修改契约或代码。不审查 Spec 和 Plan（目前也还不存在）。

## 依据

- `reviewing-work` 的 Deliverable 参考。
- `modeling-task-contracts`：「Separate Outcomes from Mechanisms」、Create or Revise 第 5–8 步、「Readiness」。
- 来源：契约 Source Request 与 Authorization 中记录的用户原话。审查者看不到会话原文，按契约记录采信。
- 现状调研：`research/01`–`05`。
- 仓库规范：`AGENTS.md`、`Docs/Agent/RefactorPreferences.md`、`Docs/Agent/ArchitecturePhilosophy.md`。
- issue（只读 `gh issue view`）：#182、#204、#209、#210，均为 OPEN。
- 代码基准：worktree HEAD `a02bce46`。下文 `src/`、`tests/` 均相对 `Server/`。

## 抽查核实的事实

| 契约里的说法 | 核实结果 |
|---|---|
| 管线 8 层：0/45/50/55/57/58/60/100 | 属实，见 `src/Dfo.Server.Application/Commands/Pipeline/*.cs` 的 `Order` |
| 事务型命令已被全进程单名额信号量串行化 | 属实，`TransactionalExecutionBehavior.cs`，`SemaphoreSlim(1, 1)`，Order 57 |
| 141 个 `ClientMessageHandler` | 属实（脚本统计 141）。命令处理器统计到 203 个类，与 204 的出入不影响结论 |
| Reliable 超限「标为死信」不与「不改表结构」冲突 | 属实。`domain_event_deliveries.state` 已有 `CHECK(state IN(0,1,2))`，`DomainEventDeliveryState.Terminal`＝2，`FailAsync(terminal: true)` 现成可用 |
| #182 原建议是「抽一个可内联的加锁原语」；#204 是两塔多账号声明不一致导致的死锁陷阱 | 属实。RI-13 记作 `authorized_change` 有依据：用户改走执行器方向，删锁后 #204 的前提随之消失 |
| #210 是城镇区域租约的身份区分问题，不是锁 | 属实。放在 Scope Out 合理 |

## 对审查重点的判断

### 1. 忠实度与 Root Intent Coverage

- RI-1、RI-2、RI-3、RI-5、RI-6、RI-7 与记录的原话逐条对应，没有发现夸大。
  - RI-2 的「handler 只做解包与入队」来自用户原话：「CMD直接把其他的都吸收了」「命令变成唯一实体……以协议为单位」。
  - RI-6 的两条细则（无连接只记错误、Reliable 标死信）由 Agent 补充，用户回复了「看着没问题了」，处置有依据。
- **RI-4（按频道分流）记为 `authorized_change`**：
  - 用户的原话是明确的设计要求：「CMD内部再根据channel做分流」。
  - 改成「单执行器，分流键预留」的依据是：Agent 提出、用户没有反对，之后用户对整体模型说了「看着没问题了」。这是默示接受，可以追溯，但证据偏弱。
  - 改动可逆（分流键已预留），单执行器的串行语义也比按频道分流更强。所以不阻断，处理见非阻断风险 1。
- **RI-8（短 `lock(sync)` 先留）**：用户的问题是「大部分锁都可以扔了？」。保留短锁符合「大部分」，`authorized_change` 成立。
- **RI-9（持久化不变）**：
  - 处置 `in_scope` 本身合理：这是保守选择，与 Goal「持久化结果不变」一致。
  - 但证据列说「用户问 MySQL 时 Agent 给出理由」，Source Request 里却没有记录用户问 MySQL 的那句原话，来源锚点不完整。见非阻断风险 1。
- **RI-13（#182、#204）**：见上表，成立。
- 每条意图只出现一次，处置值合法，没有 `unresolved`。
- RI-12 把「一次性推送」记作 `in_scope`，位置指向 Scope Out 6。字面上有点别扭，但含义清楚（本任务不推送，推送留到分支收尾），不影响执行。
- 与 Scope Out 的交叉都有明确处置：
  - Out 1 ↔ RI-4；
  - Out 2 ↔ RI-8；
  - Out 3 ↔ RI-9；
  - Out 6 ↔ RI-12；
  - #209、#210 不是本任务的来源意图。

### 2. 机制是否被过度固化

| 契约里的机制 | 出处 | 判断 |
|---|---|---|
| 协议级 CMD、handler 只解包入队 | 用户原话 | 用户决定，放在契约层恰当 |
| 深度计数、调用链日志、超限断开源连接 | 用户原话 | 用户决定 |
| 无连接只记错误、Reliable 标死信 | Agent 细则，用户明确回复认可 | 已接受的决定 |
| 执行器不等待网络发送；只刷出已提交结果 | Agent 提出，用户未反对 | 有实质风险依据：单执行器一旦等待慢连接就会全服停顿（今天每收件人期限 2 秒）；未提交就发出会泄露回滚前的状态（今天 `ProjectAsync` 在可写事务内会抛）。作为结果约束成立 |
| 「发件箱」「每连接发送队列」两个名词（写在标题、Goal、Scope In、SC-3） | Agent 机制 | 这是上一行结果几乎唯一的自然实现，不必删。但验收应当按结果判，见非阻断风险 2 |
| 分流键「预留成一个函数」 | Agent 机制 | 粒度很细，但无害 |
| 「纯搬迁与语义改动分开提交」「删净」「判据配反例」 | `RefactorPreferences.md` §2、§3 及用户引语 | 有规范出处，成本低 |
| 单数据库写者 | 实质风险（AGENTS.md 记载的「先读后写等满 30 秒」） | 成立 |

没有发现缺乏出处、又会限制正当实现的硬性机制约束。

### 3. Scope 与 Success Conditions

- 五份调研的关键面都已进入 Scope In：
  - 入口：收包、生命周期、33 个后台服务、52 个订阅、GM/HTTP、延时计时器、效果账本重放、房间机制；
  - 依赖投递结果的约 15 处，以及依赖同步发送异常的十几处；
  - 判据冲突：`CommandHandlerArchitectureTests`、效果结算名单、槽位边界、嵌套 dispatch；
  - 绕过全局门的 SQLite 写者（Constraint「单数据库写者」）；
  - 测试冲击。
- Scope In 与 Scope Out 没有互相矛盾。
- 有一处 SC 与 Scope 冲突，构成阻断，见下。另有几处字面过绝对，属于非阻断。

### 4. 授权与停点

- Authorization 记录的原话完整，授权范围与停点表述准确：
  - 授权内容：调研、契约、路由、Spec、Plan 的编写与验收，以及规划检查点提交；
  - 实施属于同一任务，但要等用户 compact 后明确开始；
  - 不授权合并、推送、PR、远端 issue。
- 「Requires Confirmation：进入实施阶段」来自用户自己设的停点（「完成spec和plan的编写和验收后等待。我compact一下再开始执行」），不是 Agent 把授权人为拆成两次。没有越权，也没有漏授权。

### 5. Readiness

- 没有 Open Questions，没有 `unresolved`，假设可见，授权只有一行 `task`。
- 但 SC-8 的写法会让合规完成无法通过验收，见阻断项 1。修正之前不满足「每个 SC 都有可观察、可达成的证据」。

## 阻断项

### 阻断项 1：SC-8 要求「线级集成测试……原样通过」，与本契约自己的 Scope 和「删净」约束冲突

**问题。** SC-8 要求「线级集成测试、codec 向量、选角金样本原样通过」。但 `tests/Dfo.Server.Networking.Tests/Integration/` 里有几类文件直接依赖本契约要删除或改写的东西，不改就过不了：

1. **4 个文件用细粒度命令准备数据**。Scope In 规定这些命令要「并入、改成应用服务或删除」：
   - `GameLoginIntegrationTests.cs:65/69` 等用 `RegisterAccountCommand`、`AuthenticateAndIssueTicketCommand`；
   - `BossTowerFrameIntegrationTests.cs:69-72` 另用 `RedeemGameLoginCommand`；
   - `IllusionTowerStageFrameIntegrationTests.cs:91/118/122/135` 用 `RedeemGameLoginCommand`、`EnterIllusionTowerCommand`、`LoadIllusionTowerCommand`、`KillIllusionTowerActorCommand`；
   - `CardRewardReconnectIntegrationTests.cs` 也引用了 `ICommandBus`。
2. **`TouchSocketNetworkMessageSenderExplicitDeliveryTests.cs:38-61`** 断言 Required 发送失败时异常同步抛回调用方。这正是 Scope In「发件箱、执行器不等待发送」要替换的语义。
3. **`HandlerFailureConnectionIntegrationTests.cs:77-80`** 替换了 `INetworkMessageDispatcher`，断言「handler 抛异常 → 无回包、关连接」。这条路径的形态会随「handler 只解包入队」改变。

`research/05-tests.md`「线级与字节级证据」一节已经列出了这些例外，但 SC-8 没有把它们排除。

**后果。** 合规的实现（删掉细粒度命令和旧发送语义）在最终验收时必然不满足 SC-8。要么到很晚才回头修订契约，在 governed 路线上这意味着下游产物全部失效；要么为了让测试「原样通过」而保留旧命令或旧接缝，这又违反 Constraint「一次到位、删净：不留旧门、旧管线行为、转发层」。

**要求的修改（最小即可）。** 把 SC-8 中「原样通过」改为按结果表述，例如：

> 线级集成测试对线上字节、帧序和连接开关的断言不变。只因准备数据或测试接缝被删除而改动的文件，逐个列出，并说明断言未改。以被替换的发送或分发语义为测试对象的用例，按「测试数量与断言变化逐条说明」处理。codec 向量与选角金样本原样通过。

按 `modeling-task-contracts` 第 10 步，修改后递增 `revision` 并重新审查。

## 非阻断风险（交给 Spec / Plan 处理）

1. **RI-4、RI-8、RI-9 的依据是默示接受，最好在规划停点就让用户看到。**
   - 契约只承诺在「最终汇报」里单独说明「不按频道分流」。用户原本明确提出过按频道分流，原版也是每个频道一个进程。
   - 建议在 Spec/Plan 验收后的停点汇报中，把以下几项列为「Agent 提议、用户未明确表态」，让用户在动工前、成本最低的时候有机会否决：
     - 单执行器，不分流；
     - 短锁暂留；
     - 不换数据库、不回内存副本。
   - RI-9 的来源锚点建议补上用户问 MySQL 的原话。
2. **「发件箱」「每连接发送队列」应当按结果验收。** SC-3 的证据写的是「发件箱与发送队列测试」。验收时应以这几条结果为准，具体结构由 Spec 自由选择：
   - 执行器不等待网络；
   - 只刷出已提交的结果；
   - 单连接内顺序不变；
   - 慢连接不拖累其他连接；
   - 今天依赖的跨连接顺序有等价保证。
3. **Goal 与 Constraint 的措辞不一致。** Goal 写「除**本契约**列出的有意变化外……不变」；Constraint 3 却规定可见行为变化「在 **Spec** 中逐条列出」，而契约本身一条具体变化也没有列。按字面，Spec 列出的任何变化都会被判违反 Goal。建议 Spec 审查按「契约或经审查的 Spec 列出的变化」理解，下次修订契约时顺手统一措辞。
4. **SC-1「执行器外的例外只剩启动前一次性工作」字面过绝对。** 按设计，下面这些本来就在执行器外：
   - 解包；
   - 发送队列刷出；
   - HTTP 只读查询（如 `GmAccessBoundary.IdentifyAsync`、邮件物品查询）；
   - `A21UdpDiscovery`；
   - PVF 缓存；
   - GM 自身的内存状态（`GmLoginLimiter`、`GmIconPackStore`）。

   Scope In 也允许「执行器外仍要访问的共享结构……逐项说明」。Spec 应按 Goal 的定义（会改游戏状态、写库或产生出站消息）界定「入口」，并把这些执行器外的部分逐项列入对账。
5. **「CMD」「NOTI」与 AGENTS.md 的协议术语撞名。** 在 AGENTS.md 里，CMD/NOTI 是 Game 出站首字节 `0x01`/`0x00` 的分类。在本契约里，CMD 指执行单元，「NOTI」指一切出站消息。如果判据与 SC-3 按字面只管「NOTI」，`0x01` 类回包和 Channel 端点回包可能漏网。Spec 应先定义术语，并让判据覆盖全部出站消息。
6. **选角缺少能失败的端到端证据。**
   - `SelectCharacterAcceptedTranscriptTests` 只把 `SelectedCharacterStateMessageFactory.Create(result)` 的结果经编码器算 SHA256（`:454-473`），不经过 handler 或工作流。
   - Networking.Tests 与 Host.Tests 里没有任何经真实连接覆盖选角的用例（检索 `SelectCharacter` 零命中）。
   - 选角恰恰是改动最大的工作流：15 条命令、阶段间发送、补偿，并且会改成整条 CMD 结束后统一刷出。
   - 契约 Current Situation 说金样本「可以原样作为行为不变的证据」，对这个工作流说过头了：金样本保护不了 62 帧之后的补货、任务、宠物、配额帧的顺序，也保护不了失败补偿路径。
   - Spec 应按 Constraint 4「行为不变要有能失败的证据」为选角补上覆盖全序列的证据。调研 05 提到的 `Tool/WireSmoke` 是现成选项之一。
7. **单执行器上的重计算与入站背压。**
   - `PasswordCredentialService` 使用 PBKDF2，迭代 220000 次（`src/Dfo.Server.Application/Authentication/PasswordCredentialService.cs:16`），被启动器注册/登录（`AuthenticationDomainService.cs:21/34/37/43`）和 GM 登录（`GmAccessDomainService.cs:19/28`）调用。
   - 启动器端点不需要认证，也没有限流（HttpApi 里找不到限流器）。如果这些计算整体放进执行器，每次请求都会让全服停顿，形成可被利用的拖慢面。
   - 「handler 只解包入队」如果不等待 CMD 完成，就会失去今天每条连接逐包 `await` 带来的天然背压，单个客户端可以无限灌队列。
   - Spec 应说明重计算放在哪里，并给出队列上限、背压或公平性的处理方式。SC-9 的测量应包含这两类情况，Assumption 里「不够就回到用户」也要有判定口径。
8. **调用链跨 Reliable 持久化边界的传递。**
   - 深度超限要把 Reliable delivery 标为死信，说明调用链必须能跨过「事件入库 → 稍后作为 CMD 消费」这一段。
   - 在「不改表结构」的前提下，根、父、深度不能落在新列里。
   - Spec 应写明传递方式（例如只在进程内传递、重启后清零）及其后果。
9. **SC-5「仅在线跳过」措辞疑似写反。** 今天 LiveOnly 的语义是离线、旧进程或过期时跳过。建议 Spec 按今天的语义写准。
10. **SC-4 的锁清单没点名几类信号量。** 调研 04 列出的「清单外 SemaphoreSlim」包括：
    - `EffectOperationLedger` 每连接一把；
    - `DomainEventDispatcher` 的 groupGates；
    - Weakness、Joust 的信号量；
    - ScheduledTask 的并发槽。

    它们不在 SC-4 按「账号、在线用户、区域、副本局、塔局、角色」列举的范围内。「删除前后的锁清单对账」应给每一项写出去留。

## 结论

`changes_required`，只有 1 个阻断项。

契约忠实表达了用户提出的执行模型：
- 单一串行单位；
- 协议级 CMD 吸收一切；
- 事件组件保留，监听后转 CMD；
- 深度上限，超限掉线；
- 玩法锁删除。

授权范围与停点准确，可以追溯到用户原话。没有发现缺乏出处的硬性机制约束。RI-4、RI-8、RI-9、RI-13 的处置都有依据，其中前三条依赖默示接受，建议在规划停点向用户明示。

唯一的阻断是 SC-8「线级集成测试原样通过」：本契约自己的调研已经列出必须改动的那几个文件，合规的实现因此必然过不了这条验收。按阻断项 1 的最小修改改写，并递增 revision 后复审，即可通过。
