---
kind: rks-review
target: .rks-agent/tasks/online-identity-state-machine/TaskContract.md
target_revision: 1
verdict: approved
reviewer: independent subagent (reviewing-work, deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：online-identity-state-machine（revision 1）

## 审查范围

- 目标：`.rks-agent/tasks/online-identity-state-machine/TaskContract.md`，frontmatter `revision: 1`、`state: ready`。
- 审查对象是契约本身是否可作为下游 Spec 的边界：目标与范围是否清晰、Scope Out 与来源意图是否冲突、Root Intent Coverage 的处置是否成立、机制性约束是否有依据、Success Conditions 是否有可观察证据、Current Situation 是否与代码一致、是否满足 ready 条件。
- 不审查实现方案，不修改契约或代码。

## 依据

- `reviewing-work` skill 的 Deliverable 参考（`references/deliverable.md`）：完整性、正确性、来源忠实度、可用性、范围、无依据的断言。
- `/Users/puxianguo/.claude/skills/modeling-task-contracts/SKILL.md` 的「Separate Outcomes from Mechanisms」「Create or Revise」第 5–7 步（Root Intent Coverage）与「Readiness」。
- 来源：GitHub issue #186；契约逐字记录的用户授权原话（会话原文审查者无法访问，按契约记录采信）；用户声明 AGENTS.md 只作参考。
- 代码基准：worktree 当前 HEAD `7fc8acdc`，分支 `task/refactor-session-20261021`，工作区仅有未跟踪的 `.rks-agent/tasks/online-identity-state-machine/`。

## 关键证据

### 来源与关联 issue

- #186（OPEN）：四份“当前选中角色/会话”存储分在三把锁后，建议“先设计并裁定唯一真源 → 删副本”，倾向并入 `OnlineStateRegistry` 单锁。契约 Goal 写成结果层面的“一个组件、一个一致性边界”，没有把 issue 的倾向手法写死，处理得当。
- #185（OPEN）：“会话仍是当前”63 处手写，建议与 X7 一起做。契约 RI-8 交给它，同时 Scope In 2 只迁移读取被删存储的调用点，边界自洽。
- #182（OPEN）：自取会话租约泛滥、加锁原语。#204（OPEN）：两塔命令并发声明不一致，建议随 X3 抽统一加锁原语。RI-9 把“加锁原语与租约”交给这两个仍开启的 issue，归属成立。
- #208（OPEN）：由本次讨论创建，正文写明“#186 只记录、不处理”，内容正是位置两份与专家商店无写入方，与 RI-7 和用户“第三条记录一个issue”一致。
- 四个 `preserved_upstream` 指向的 issue 都真实存在、仍开启、职责与本契约不同，也没有推掉本契约自己的结果。

### Current Situation 抽查

| 契约陈述 | 代码核对 | 结论 |
|---|---|---|
| `SelectedSessionDomainService.sessions`：选角准备即写入，一把锁 | `SelectedSessionDomainService.cs:11-12` 独立 `sync` + `Dictionary<AccountId,…>`；`SelectCharacterPhaseCommands.cs:105` 准备阶段 `sessions.Select(session)` | 一致 |
| `SelectedSessionRuntimeDirectory.entries`：发布时写入，带“是否存位置”，一把锁 | `SelectedSessionRuntimeDirectory.cs:9-15`；`SelectCharacterLifecycleServices.cs:395` `runtimes.Publish(session, …PersistsPosition)` | 一致 |
| `OnlineStateRegistry.activeByAccount` 与 `OnlineUser.CurrentCharacterId`：发布时写入，第三把锁 | `OnlineStateRegistry.cs:35,104-118,343` | 一致 |
| `SelectedSessionSafety.blocked`：隔离集合 | `SelectedSessionSafety.cs:21-28`，独立 `gate` | 一致（但漏了同一类里的连接级隔离，见非阻断风险 2） |
| 孤儿窗口：断线回调靠 `FindByConnection` 补清 | `OnlineUserConnectionObserver.cs:75-87`，注释写明协议拒绝先删 OnlineUser、选中目录仍持有会话 | 一致 |
| “是否存位置”等于 `!channel.IsTransient`，频道可由 `ListenerPort` 解析 | `EntryPlacementRuleService.cs:46`、`CharacterCurrentStateQueryDomainService.cs:93`；`OnlineUserConnectionObserver.cs:40` `channels.ResolveAsync(connection.ListenerPort, …)` | 一致 |
| 四个服务“按角色 ID 存，每份另带会话副本做代数比对，各自一把锁” | 位置（`CharacterPositionDomainService.cs:29-30`）、血蓝（`CharacterVitalRuntimeDomainService.cs:9-10`）符合；物品冷却（`CharacterItemCooldownDomainService.cs:23`）是 `ConcurrentDictionary<(CharacterId,Key),long>`，不带会话、不加锁；灵药（`SpiritPotionDomainService.cs:10-11`）不带会话副本，靠锁内查 `FindByCharacter` 判定 | **部分不准确**（见非阻断风险 1） |

### 授权与范围

- Authorization 行逐字记录了用户原话，覆盖“完成这部分的重构”“写任务契约”“sdd规模你自己确定”“做完后再告诉我”。授权范围限定在本 worktree 内实施、验证、本地检查点提交，排除合并、推送、PR、部署、清理，与 Scope Out 最后一条一致。
- 当前分支是 `task/refactor-session-20261021`，与授权所写的工作区一致。

## 逐项判断

1. **目标与范围**：Goal 用结果语言写出两件事：“他是谁”只有一个真源；“他怎么样”只有一个归属，不加业务锁，并发不丢，代数隔离。另外要求协议行为不变。Scope In 六条都能追溯到 RI-1～RI-6；Scope Out 每条都有处置：#185、#182/#204、#208 交给上游，跨角色聚合以“除非直接读取被删存储”为界，协议、DB、客户端和集成动作按授权排除。没有发现与来源意图冲突的 Scope Out。
2. **机制性约束的依据**：
   - 约束 1（代数精确）：依据是既有不变量和实质风险（旧会话串改新会话），可以接受；括号里“既有行为”对冷却不完全成立，见非阻断风险 1。
   - 约束 2（不加业务锁且并发不丢）：“不加锁”来自用户原话；“并发不丢”的依据是命令管线对同一账号取共享租约，有实质风险依据。
   - 约束 3（独立服务，不并入角色领域服务）：用户原话。
   - 约束 4（一次迁完、不留转发层/`[Obsolete]`）：依据是 `Docs/Agent/RefactorPreferences.md` 记录的用户原话。注意：原话在 §2（第 28 行），§3 第 5 条是对应手法，契约写的“§3-5 记录的用户偏好”出处略有偏差，不影响效力。
   - 约束 5（协议回包与持久化结果不变）：与 Goal 一致，属于重构的兼容义务。
   - Scope In 4（由频道推导）和 Scope In 5（发布建立、拆除移除）属于机制，但都是用户认同的方向（RI-4、RI-5），不算过早写死。没有发现把 Agent 自拟的实现细节（锁形态、数据结构、类名）提升为硬约束的情况。“在线身份组件”是否就是改造后的 `OnlineStateRegistry`，契约没有限定，留给了 Spec，这样处理正确。
3. **Success Conditions 证据**：SC-1～SC-7 每条都有可观察证据（代码检索、差异、针对性单测/回归/并发测试、构建与测试输出及数量说明、最终汇报）。SC-2 的“同一一致性边界”主要靠实现检查，单测只能辅助，作为契约级证据描述已经够用。
4. **Root Intent Coverage**：RI-1～RI-10 每条只出现一次，处置值都合法，没有 `unresolved`。四条 `preserved_upstream` 都指向真实、开启、职责不同的 issue。RI-10（做完告知）有用户原话依据，SC-7 承接。
5. **Readiness**：没有 Open Questions；两条 Assumptions 可见，并给了偏离时的处理方式；授权可追溯；没有 `unresolved`；没有缺依据的硬机制约束。满足 `state: ready`。

## 发现

无阻断发现。

## 非阻断风险（建议在 Spec 中显式处理，或在契约下一次修订时顺手更正）

1. **Current Situation 对冷却和灵药的描述不准确，约束 1 的“既有行为”只部分成立。** 冷却 `CharacterItemCooldownDomainService` 是无锁 `ConcurrentDictionary`，键是 (角色, 冷却键)，不带会话、没有代数比对，`Start` 只按 `CharacterId` 写入（`ItemEffectHandlers.cs:60`）。灵药 `SpiritPotionDomainService` 不存会话副本，在自己的锁内查 `ISelectedSessionDomainService.FindByCharacter` 来判定（`ApplyForSession`、`ClearIfUnselected`）。现有生命周期也不对称：灵药在发布时 `Clear`（`SelectCharacterLifecycleServices.cs:393`）；冷却与容器只在拆除后、且角色不再被选中时才清（同文件 `:196-204`）。后果：Spec 可能把冷却的代数隔离当成“保持既有行为”，不为它准备能失败的行为证据；也可能忽略“拆除时角色已被重新选中则不清冷却”这条现有分支的语义。按 `SelectCharacterPhaseCommands.cs:88-105`，顶替时先清理旧会话再 `Select` 新会话，所以按会话存放大概率与现状等价，但需要在 Spec 中逐条确认并用测试钉住。建议修正：把这句改成按服务如实描述；约束 1 的括号改为“位置、血蓝为既有行为；冷却、灵药为本次补齐”。
2. **`SelectedSessionSafety.connections`（连接级隔离栅栏）没有归属。** 同一个类除了 `blocked`，还有按 `NetworkConnectionContext` 存的 `ConditionalWeakTable`，被 `NetworkMessageDispatcher.cs:22,29`、`ReplayStateProjectionDelivery.cs:34` 和 `Capture/DemandCurrent` 读取。Goal 要求“已隔离”阶段由一个组件回答，SC-1 却只点名 `blocked`。如果连接栅栏留在原处，“已隔离”这个事实仍分在两处。建议 Spec 明确：连接栅栏是独立事实（连接可能比会话活得久），还是并入在线身份。
3. **“线上用户 ID”指代不清。** 结合 `SelectedSessionDomainService.FindByWireUserId`，这里应是 `WireUserId`，但读者容易把它和 `OnlineUserId`（在线用户 ID）混淆。建议 Spec 直接写标识符。
4. **“不加业务锁”的边界和验证。** 需要在 Spec 中定义“业务锁”：比如 `ConcurrentDictionary` 的原子操作和 CAS 是否允许。还要覆盖“拆除后迟到的更新不得复活条目”这类竞态（例如不能用会插入新条目的 `AddOrUpdate`）。SC-5 只要求“并发更新不丢失”，没有说明“未加锁”用什么证据确认，建议补上代码检查即可。
5. **接口去留与约束 4 的关系。** Scope In 5 写“删除原服务”，Scope In 1 只点名删除 `ISelectedSessionRuntimeDirectory`。`ICharacterPositionDomainService` 等四个接口、`ISelectedSessionDomainService`（约 228 个文件引用）、`IActiveCharacterDomainService` 是删除，还是由新组件直接实现，契约没有说。由新组件直接实现不违反“无转发层”，薄适配器则违反。建议 Spec 逐个接口裁定，SC-1/SC-5 的检索判据要与之对齐。
6. **拆除时从频道推导“是否存位置”的失败语义。** 频道解析是异步的（`channels.ResolveAsync`），并且依赖频道配置在会话期间不变。Spec 需说明解析失败时如何计入清理失败，以及配置不变这个前提。
7. **文档范围。** `AGENTS.md:125,214-224,251-254` 和 `Docs/CharacterEntryFacesInventory.md:6` 描述了将被删除或改变的结构，`AGENTS.md:251` 还写着“在线状态那把锁不能拆成三把”。用户声明 AGENTS.md 只作参考，但它仍是“描述这些结构的仓库文档”。建议 Spec 明确 SC-6 的文档清单是否包含 AGENTS.md，避免交付后文档与代码矛盾。
8. **改动面大。** `OnlineUserState.CharacterActive` 和 `CurrentCharacterId` 分别出现在约 30 和 34 个文件中。它们是否算“被删存储”，决定了 Scope In 2 与 #185 的实际分界。建议 Spec 先列出受影响调用点清单，并标明每个调用点原来的接受范围。

## 结论

`verdict: approved`。revision 1 的目标、范围、根意图处置、授权和成功条件满足 ready 条件，没有阻断发现。上面 8 条非阻断风险交给 Spec 阶段处理；其中第 1 条是事实性偏差，建议契约下次修订时更正。本批准不代表任务完成，也不能替代后续的完成验证。
