---
kind: rks-review
target: .rks-agent/tasks/session-currency-checks/TaskContract.md
target_revision: 2
verdict: approved
reviewer: independent subagent (reviewing-work, deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 复审：session-currency-checks（revision 2）

## 范围

- 目标：`.rks-agent/tasks/session-currency-checks/TaskContract.md`，frontmatter `revision: 2`、`state: ready`。
- 重点：r1 两个阻断项是否消除；`IsSelected` 定义细化、拆除改用 `StandingOf` / `ReleaseExact` 是否超出授权；r1 七条非阻断风险的去向；revision 2 新引入的歧义、冲突与过度约束；ready 条件。
- 只审查，不修改契约或代码，也不审查 Spec / Plan。

## 依据

- `reviewing-work` 的 Deliverable 参考。
- `/Users/puxianguo/.claude/skills/modeling-task-contracts/SKILL.md` 的「Separate Outcomes from Mechanisms」、Create or Revise 第 5–7 步、「Readiness」。
- 来源：issue #185（OPEN）；#182、#204（均为 OPEN）；契约 Source Request 与 Authorization 中记录的用户原话（审查者看不到会话原文，按契约记录采信）；`Docs/Agent/RefactorPreferences.md`；上一轮审查 `reviews/task-contract-review.md`（r1）只用来核对各条意见的去向。
- 代码基准：worktree HEAD `ea6a1544`。下文路径相对 `Server/src/`。

## 关键证据

### 判定的现有实现（`Application/Users/OnlineStateRegistry.cs`）

| 成员 | 接受条件 |
|---|---|
| `FindExact`（`:305-308`） | `selections[session.AccountId].Session == session` |
| `StandingOf`（`:232-239`） | `FindExact` 非空即返回，带阶段、隔离标记和 `IsRevoked` |
| `IsRevoked`（`:310-314`） | 会话连接上没有在线用户、或账号不符、或（已入世时）Owner 不是该在线用户 |
| `IsInWorld`（`:241-245`） | `FindExact` 且阶段为 `InWorld` |
| `IsActiveExact`（`:247-256`） | `FindExact`、`InWorld`、Owner 的连接就是会话连接、Game 端点、账号一致、`byConnection` 指向 Owner |
| `ReleaseExact`（`:189-202`） | 只在 `FindExact` 非空时移除 |
| `Select`（`:139-157`） | 跨账号保证角色与线上号唯一 |

### 阻断项 1 的复核：`IsSelected` =「当前选中且连接仍归它」，再加保底规则

| 调用点 | 今天的口径 | revision 2 下的结果 |
|---|---|---|
| `SelectCharacterHandler.EnsureCurrent`（`Protocol/Game/SelectCharacter/SelectCharacterHandler.cs:540-548`），发布前 `:289/294/304/316/332` 及发布后各处 | `Find(account) == session`，且会话连接上有在线用户、账号一致 | 「选中且未撤销」= `FindExact` 且 `!IsRevoked`。它覆盖 `EnsureCurrent` 的全部条件，另加「已入世时 Owner 就是该在线用户」，**等价或更严，不放宽**。`:294` 挡在每日补货（`:301`）前的效果保留，r1 指出的「补货照样提交」不再成立 |
| 工作流各阶段命令调用的领域服务里的 `Find… == session`（例如 `SelectCharacterLifecycleServices.cs:381` 的 `Publish`） | 当前选中，任何阶段，连接撤销也接受 | 改为不接受已撤销连接。这是收紧，RI-4 已列出「选角工作流里的领域服务不再接受连接已撤销的会话」。持久化上的差别只在一个窗口里：连接已撤销时，某一步的事务从「先提交、下一处 `EnsureCurrent` 再中止」变成「当场拒绝并回滚」，方向安全 |
| 工作流里今天已按在世界口径检查的命令（`OnlineExpiredItemCleanup` 四连判、三个 `RefillOnline*Ticket` 的 `IsActiveExact`） | 在世界且连接仍归它 | 约束「用途规则」第 1 条写明「不放宽」，保底规则兜底，归 `IsInWorld` |
| `ClientSettingsDomainService.IsCurrent`（`Application/Accounts/ClientSettingsDomainService.cs:88-93`） | 连接身份、账号、在线用户代数一致，`FindByConnection == origin.SelectedSession`，允许 `SelectedSession` 为 null | 保底规则要求保留两级之外更严格的条件（代数、连接身份），所以不会放宽。null 分支见非阻断风险 1 |

结论：**阻断项 1 已消除。** 两级结构没变，第一级的含义按用户「这个会话到底有没有效」的原意补上了连接一侧，与 `EnsureCurrent` 一致；保底规则把「替换后不得更宽」写成了硬约束，正好对应 RI-4 只授权收紧这一点。

### 阻断项 2 的复核：Scope Out 4 保留的两类

- revision 2 把「以查不到决定不处理、不投递、不在线」的查找写进 Scope In 3、RI-2 和 SC-1，Scope Out 4 只保留两类：(1) 请求方按自己这条连接解析会话，之后交给命令或领域服务判定；(2) 生命周期清理为取出待释放会话所做的查找。r1 点名的 `NotificationAudienceResolver.cs:91`、`CharacterVisibleSessionDomainService.cs:34`、`RentalSlice.cs:164`、`WeaknessRecoveryService.cs:67` 都不属于这两类，现在都在范围内，SC-1 也检测得到。
- 两类边界对主体代码清楚、可执行：
  - 第 (1) 类：handler 按 `context.ConnectionId` 取自己的会话，之后由命令判定，取不到就返回。保留它不影响收紧，因为判定由命令侧的 `IsInWorld` / `IsSelected` 完成。
  - 第 (2) 类：`SelectCharacterPhaseCommands.cs:53` 的 `Find(command.AccountId)`（取出要替换的旧会话），以及 `OnlineUserConnectionObserver.cs:82/94/106` 的 `FindByConnection`（取出孤儿或待清理会话），都落在这一类。
- 边缘上还有几处缺口，两类都没覆盖，三个具名判定也表达不了，见非阻断风险 1。

结论：**阻断项 2 已消除**。剩下的缺口只出现在少数生命周期和设置路径，保底规则已经能决定它们的行为（保留，不放宽），不会造成范围失控。

### 拆除、补偿、断线改用 `StandingOf` / `ReleaseExact` 能否保持接受范围（代码抽查）

| 位置 | 今天 | 换成 | 是否等价 |
|---|---|---|---|
| `Application/Sessions/SelectCharacterLifecycleServices.cs:59` `ownsSelectedCharacter` | `FindByCharacter(session.CharacterId) == session` | `StandingOf(session) is not null` | 等价。`Select` 跨账号保证角色唯一，记录又以 `session.AccountId` 为键，所以两边都等于 `FindExact(session) != null`。阶段和是否撤销都不论，与今天一样 |
| 同文件 `:193` | `ReleaseExact(session)` | 不变 | — |
| `Protocol/Game/OnlineUserConnectionObserver.cs:138` `CleanupSelectedHeldAsync` 的守卫 | `FindByConnection(connectionId) != selected` 则返回 | `StandingOf(selected) is null` | 等价。`selected` 本来就是按这条连接取出来的，且一条连接只认证一个账号。今天的 `SingleOrDefault` 在理论上的重复连接下会抛异常，换成按账号查后不再抛，这不算放宽 |
| `SelectCharacterPhaseCommands.cs:342-363` 补偿处理器 | 只有 `cleanup.CleanupAsync` + `finally ReleaseExact` | 不变 | — |
| `SelectCharacterPhaseCommands.cs:65/99/136` 准备失败时 | `ReleaseExact(previous/session)` | 不变 | — |

结论：拆除、补偿、断线三条路径改用 `StandingOf` / `ReleaseExact` 后，接受范围与今天相同。这也是 revision 2 必须做的配套：`IsSelected` 加上连接一侧以后，如果拆除路径仍归到 `IsSelected`，就会漏掉已撤销连接留下的残留记录。

### 授权判断：`IsSelected` 的细化、拆除改路是否超出用户授权

- 用户原话（契约记录）：提出 `checksessionavaliable(session)`，语义是「这个会话到底有没有效」；「命名啥的还是你比较专业」；「其他的都OK，tryget也算能接受」；「来吧，开始行动吧」。
- 细化后，两级结构、用途划分（工作流问有效，其余问在世界）、只收紧不放宽都没变。实际带来的行为变化只有一处：工作流各阶段的领域服务不再接受已撤销连接。这类会话今天在下一处 `EnsureCurrent` 就会中止，所以变化只是中止得更早，方向更安全。拆除改用 `StandingOf` / `ReleaseExact` 不改变任何接受范围，属于机制选择。
- 判断：这属于用户交给 Agent 的细节，没有超出授权，**不必先停下来取得用户确认**。契约已在 Context 和 SC-7 承诺汇报时单独说明，这就够了。r1 说过「引入第三种语义需先确认」，revision 2 没有引入第三级，所以这个前提不触发。
- 可追溯性上有一个小瑕疵，见非阻断风险 5。

### r1 七条非阻断风险的去向

| r1 编号 | 内容 | revision 2 去向 | 判定 |
|---|---|---|---|
| 1 | 独占租约假设比实际窄 | Assumptions 改写为：跨账号目标、定时任务、事件订阅者能看到 `Selected` 阶段，收紧后会拒绝或跳过，由 Spec 列出并给测试；SC-3 加上「跨账号目标和非命令路径的收紧行为有说明」 | 已处理 |
| 2 | 投递的人群语义不清 | Scope In 4 与 SC-4：精确会话人群看完整会话，角色人群看「在世界」，投递前比对解析出的连接；必须送达的拆除通告不得丢弃。人群解析与投递两步都已点名 | 已处理。「看完整会话」的级别要结合用途规则读成 `IsInWorld`，见非阻断风险 4 |
| 3 | SC-1 与 Scope Out 1 字面冲突 | SC-1 加注「`SelectedSessionSafety` 按 Scope Out 1 处理」；Scope Out 1 允许改写表达式，但须保留连接对象比较 | 已处理 |
| 4 | 约束 2 比来源宽 | 改为结果层面的「重复检查的处理」：原来有保护的入口保留等价保护，会话命令内部的检查不全删，不为原本没有检查的入口新增检查 | 已处理 |
| 5 | 命名锁定 | 原来的命名硬约束已删除；Scope In 1 仍写出三个名字，但只是描述要提供哪些判定，用户也认同了这组名字 | 已处理（可接受） |
| 6 | RI-9 缺少来源锚点 | Source Request 补上原话「我们直接在这个分支全搞完了再一次性提交」 | 已处理。原话说的是「提交」，RI-9 读作「推送」；Authorization 记录用户是在看过「本地检查点提交」的方案后才说「开始行动」的，两者不冲突 |
| 7 | 计数口径 | Current Situation 改为「会话类约 300 行，另有约 20 行副本、塔的 `RunId` 比较不算」，并写明 `TownAreaMember.Owns` 不属于会话当前性；Scope Out 5 也把它排除在外 | 已处理 |

### Root Intent Coverage 与 Readiness

- RI-1～RI-9 各出现一次，处置值合法，没有 `unresolved`。
- RI-8 的 `preserved_upstream` 指向 #182、#204，两者都是 OPEN，职责与本契约不同，不涉及本契约自己的结果。
- 与 Scope Out 的交叉都有明确处置：Scope Out 1 对 RI-7（隔离留在管线）；Scope Out 3 对 RI-6；Scope Out 4 保留的两类不在 RI-2 的意图之内（RI-2 说的是「以查不到当作不在线」）；Scope Out 6 对 RI-9。
- 授权只有一行 `task`，范围与 Scope Out 6 一致。每条 SC 都有可观察的证据。Assumptions 可见，没有 Open Questions。
- 硬约束的出处：用途规则、两步走、DB 再查保留、隔离留在管线都来自用户认同的方案；保底规则来自 RI-4「只授权收紧」和 Goal「持久化结果不变」；「一次迁完、删净」来自 `RefactorPreferences.md` §2。没有发现缺少出处的机制性约束。
- 满足 ready 条件。

## 问题

### 阻断项

无。

### 非阻断风险（交给 Spec 处理）

1. **有几处查找既不属于 Scope Out 4 的两类，也没有具名判定能表达。** 这些查找要的是「是否还有任何选中记录」或「仍然没有会话」，例如：
   - `SelectCharacterLifecycleServices.cs:199`：释放后 `FindByCharacter(...) is null` 才清容器；
   - `ReturnToSelectionCommand.cs` 的 `CompleteReturnToSelectionCommandHandler`：`Find(account) != prepared.Session`，而 `prepared.Session` 可以为 null；
   - `OnlineUserConnectionObserver.cs:106`：`selectedBefore is null && selected is not null` 时重试；
   - `ClientSettingsDomainService.IsCurrent` 的 null 分支：要求这条连接仍然没有选中会话。

   用 `TryGetInWorld` 替换会放宽（把 `Selected` 阶段或已撤销的记录当成「没有」），保底规则禁止这样做；可是 SC-1 的豁免只列了两类。Spec 应逐条列出这几处，以保底规则为依据保留或另立具名判定，最终审查据此判 SC-1。如果控制者想避免完工时再起争议，可以在契约里把第 (2) 类放宽成「拆除、补偿、断线、返回选角路径内判断有无待释放或残留选中记录的查找」，并按规则递增 revision 再复审。
2. **「查不到就抛异常」的查找被收紧后，会把旁人的状态变成请求方的失败。** 例如 `SelectCharacterLifecycleServices.cs:333` `PlanPvpAsync`：`value.Session ?? FindByCharacter(...) ?? throw`。如果按「投影 → `IsInWorld`」收紧，PvP 大厅里某个参与者的连接一旦撤销，新进入者的选角就会失败并触发补偿。Spec 应把这类调用点单独列出，收紧时改为跳过，不要抛异常；或者明确保留原行为。SC-3 的选角回归测试应覆盖这种情况。
3. **用途规则第 1 条说的是工作流自己的会话。** 工作流领域服务里查别人的地方（`PlanTownPublicationAsync` `:300` 查周围角色、`CapturePartyRecipients` `:242/262`、拆除时的同伴人群 `:74`）只能用 `TryGetInWorld`，属于「投影、投递」。Spec 应写明这一读法，避免有人把「工作流 → `IsSelected`」套到旁人头上。
4. **几个细节语义要在 Spec 写死。**
   - 隔离标记：SC-2 要求「隔离」状态下答案正确，但没说正确答案是什么。按 RI-7「隔离留在管线」和保底规则，`IsSelected` / `IsInWorld` 应不看隔离标记。
   - 「精确会话人群看完整会话」应读作对精确会话做 `IsInWorld`。
   - `IsInWorld` 并入 `IsActiveExact` 后，要保留 Game 端点和 `byConnection` 指向 Owner 两个条件。
5. **RI-4 的证据列把「工作流领域服务不再接受已撤销连接」也归到「其他的都OK」名下。** 用户那句话是针对 r1 的定义说的。准确的出处是「用户交给 Agent 的细节，按 SC-7 汇报」，Context 里已经这样写了。这一点不影响执行，汇报时照 Context 的说法讲即可。
6. **下游产物已过期。** 同目录的 `spec.md`（frontmatter `contract.revision: 1`、`state: draft`）和 `plan.md` 是在 revision 1 下生成的。按 modeling-task-contracts 第 10 步，它们随 revision 2 失效，要重新对齐 revision 2 并重新审查后才能使用。顺带一提：`spec.md` 的 RQ-011 写着「由玩法入口自己检查 `IsInWorld`」，可能和契约「不为原本没有检查的入口新增检查」冲突，留给 Spec 审查处理。

## 结论

`approved`。revision 2 消除了 r1 的两个阻断项：

1. `IsSelected` 细化为「当前选中且连接仍归它」，与 `EnsureCurrent` 等价或更严，再加上保底规则，不会放宽任何检查，也不会让发布前的补货在连接被顶替后照样落库；
2. 「以查不到当作不在线」的查找已纳入范围，Scope Out 4 只保留两类，主体可执行。

`IsSelected` 的细化，以及拆除、补偿、断线改用 `StandingOf` / `ReleaseExact`，都在用户授予的细节自由之内。代码抽查确认拆除路径的接受范围不变，所以不必先停下来确认，按 SC-7 汇报即可。r1 七条非阻断风险都已处理。上面六条新风险交给 Spec 处理，其中第 1 条和第 6 条在写 Spec 时必须落实。
