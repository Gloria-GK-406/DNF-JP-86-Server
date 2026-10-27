---
kind: rks-review
target: .rks-agent/tasks/dungeon-run-party-roster/TaskContract.md
target_revision: 1
verdict: approved
reviewer: independent subagent (reviewing-work deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：dungeon-run-party-roster（revision 1）

## 范围

- 目标：`.rks-agent/tasks/dungeon-run-party-roster/TaskContract.md`，frontmatter `revision: 1`、`state: ready`。
- 重点：Current Situation 的事实陈述是否与代码一致；Goal、Scope 是否忠实于用户原话与 issue #202；约束和成功条件有没有把机制写成无依据的硬要求；Root Intent Coverage 的处置是否合规；Assumptions 是否需要在契约层面解决；是否满足 ready 条件。
- 只审查，不修改契约或代码，不审查实现方案。

## 依据

- `reviewing-work` 的 Deliverable 参考：完整性、正确性、来源忠实度、可用性、范围、无依据的断言。
- `/Users/puxianguo/.claude/skills/modeling-task-contracts/SKILL.md`：「Separate Outcomes from Mechanisms」、Create or Revise 第 5–7 步、「Readiness」。
- 来源：issue #202、#183、#187（`gh issue view`，三者都是 OPEN）；契约 Authorization 逐字记录的用户原话（审查者看不到会话原文，按契约记录采信）；历史任务 `.rks-agent/tasks/party-key-dungeon-membership-refactor/`（TaskContract rev1、spec.md）。
- 仓库规则：`AGENTS.md`、`Document/RefactorPreferences.md`、`Document/Architecture.md`、`Document/GameplayExecution.md`。
- 代码基准：HEAD 与 `origin/main` 都是 `1cff7218`，工作区只有未跟踪的本任务目录。下文路径相对 `Server/src/`。

## 关键证据

### Current Situation 逐条核对

| 契约陈述 | 代码核对 | 结论 |
|---|---|---|
| 副本由 `DungeonRunDomainService` 持有，聚合绑定入场队伍的 `PartyKey`，单人为虚拟键 | `Application/World/DungeonRunDomainService.cs:126-129`（有队伍服务时取 `FindParty(...).Key`，否则 `PartyKey.Virtual`）；`:1740,1749` | 一致 |
| 入场快照 `entryParticipants` 除入场队长外无人读取 | `:1890` 只用于 `EntryPurposeOwner`；另外 `:1790-1791` 在无队伍服务时整份返回 | 基本一致；后一处读取由同条后半句的“回退”说明覆盖。另有一份入场派生的 `participantsByCharacter`（`:1733,1760,1797-1798`）提供 AccountId，并承担“∩ 入场快照”的过滤，契约没有点名（见非阻断风险 7） |
| `ActiveParticipants` 可变，结束时移除角色 | `:1766,1823`；`DetachParticipant`（`:1435-1475`）由 `EndAsync`、`End`、`TryAbortEntry`、清场回城、全灭、入场时清旧局调用 | 一致（“EndAsync 时”是简化说法） |
| 按角色到副本的索引 `runsByCharacter` | `:37`，被约 20 个入口用来按角色找副本 | 一致 |
| `Participants` = 队伍当前成员 ∩ 入场快照 ∩ ActiveParticipants，槽位、队长取队伍实时值；无队伍服务时退回入场快照 | `:1786-1804` | 一致 |
| 接口注释称 frozen participants | `Abstractions/World/IDungeonRunDomainService.cs:442,451` | 一致 |
| 放弃副本先结束副本再退队 | `Protocol/Game/GamePartyTransitionCommandHandler.cs:331-341`（`EndAsync` 成功后才 `MutatePartyAsync(Leave)`） | 一致 |
| 断线拆除先结束副本再退队 | `Application/Sessions/SelectCharacterLifecycleServices.cs:123-139`（先 `EndAsync`），`:172-183`（再 `CleanupCharacter`），每步隔离失败 | 一致 |
| 退队、踢人先改队伍再结束副本 | `Protocol/Game/GamePartyTransitionCommandHandler.Stages.cs:161-178`（`MutatePartyStateAsync`） | 一致 |
| 整队退出、续选副本逐个成员结束副本，队伍保留 | `GamePartyTransitionCommandHandler.Exit.cs:19-28`；`Protocol/Game/DungeonSelection/DungeonSelectionCommandPhases.cs:80-90` | 代码形状一致；但后者在请求者不是队长时只结束请求者一人（`:118-136`），见非阻断风险 1 |
| 任一路径只改一边，`Participants.Single(...)` 或 `Leader` 会抛异常 | `ViewFor` 用 `Participants.Single(characterId)`（`:1878`）；`Leader`（`:1805-1806`）只在入场时读取（`:150,164`） | 方向正确，后果略有夸大：更常见的表现是视图里没有队长，`IsCurrentLeader` 对所有人都为假，过图被拒（`:503-506,562-566`），而不是抛异常。SC-6 的“恰有一名队长”已覆盖这一表现，不影响契约 |
| 运行中的队伍当前无法增员：组队要求角色在城镇区域，入场会离开城镇区域，没有显式守护 | `Protocol/Game/Party/PartyCommandSlice.cs:288-312`（`TryCapture` 要求 `townAreas.Find` 非空）；`PartyProtocolCommands.cs:160-166,227-233` 邀请、接受两端都要捕获成功；`Application/Parties/PartyService.cs:493` `ValidateTownPair`；选图与入场都离开城镇区域（`DungeonSelectionCommandPhases.cs:214-218`，`DungeonEntry/DungeonEntryCommandPhases.cs:343-347`）；`PartyService` 不引用副本服务 | 一致 |
| 前一任务确立 `PartyKey`，规格要求运行中队伍只能按退出语义缩减或换队长、图外角色不能凭入队进入现有副本、副本不改绑 | 历史 `spec.md:25` 与 AC-003（`:45`） | 一致 |

### Goal、Scope 与来源的对应

- 用户原话有三层意思：队伍实例是人员承载者，单人也是虚拟单人队伍；副本和队伍关联，而不是和人关联；人找副本要先找所属队伍，再找该队伍的副本。Goal 逐层承接了这三层意思，“不再维护按角色到副本的独立索引”“角色 → 队伍 → 副本”都是用户直接点名的机制，有用户授权，不属于无依据的机制约束。
- issue #202 的建议手法是“只留一个来源”，Scope In 1 删除可变集合、角色索引和无队伍回退分支，Scope In 5 更正注释，与 issue 一致，没有越权扩大。
- Scope In 4 “运行中副本绑定的队伍不能增员”比 issue 更强，但出处是历史规格第 25 行“运行中队伍只允许按已定义的退出语义缩减成员或更换队长”，已由 RI-6 承接，不是自行扩张。现有可达路径上这条约束已经成立，显式守护不改变可观察行为，与约束 1 不冲突。
- 历史规格允许运行中“更换队长”。契约没有单列这一点，但约束 1（行为不变）和 SC-1（视图随队伍变化）已经覆盖，不会被误读为禁止换队长。
- Scope Out 的三项都没有和来源意图冲突：两塔、祭坛和占用登记归 #187；其余可选依赖归 #183；推送、PR、合并、部署在 Requires Confirmation 与 Authorization 中一致排除。

### 约束与成功条件

- 约束 1“可观察行为不变，结构与行为修正分开说明验证”出自 `RefactorPreferences.md`“结构搬迁与行为变更分别说明和验证”。约束 2、3 出自历史规格。三条都有依据，也没有指定实现手段。
- “删除无队伍服务时的回退分支”是机制，依据是 #202 的“只留一个来源”和 `RefactorPreferences.md` 对可空依赖绕结构问题的判据，并且只限于本服务的队伍依赖，其余可选依赖交给 #183。依据充分。
- SC-1～SC-8 每行都有可观察证据。SC-1 的“拒绝第二份名单的自动测试”措辞模糊（见非阻断风险 4），但有 `RefactorPreferences.md`“用拒绝反例守护”作依据，不构成过度约束。

### Root Intent Coverage

- RI-1～RI-10 各出现一次，处置值都合法，没有 `unresolved`。RI-1、RI-2、RI-3 是从用户一句话里拆出的三个可独立验收的部分，拆分合理，没有为了显得完整而重复计数。
- RI-5 判 `not_applicable`：理由成立。`Document/GameplayExecution.md` 规定玩法读写只在唯一 FIFO 执行器上进行，短 `lock(sync)` 保留；`PartyService` 不引用 `IDungeonRunDomainService`（`Application/Parties/` 下检索无结果），只存在“副本锁 → 队伍锁”一个方向，没有锁序反转。注意：这条理由依赖“队伍服务不反向调用副本服务”，Scope In 4 的守护如果放进 `PartyService` 并反查副本，理由就要重新评估（见非阻断风险 5）。
- RI-8 → #183、RI-9 → #187 判 `preserved_upstream`：两个 issue 都是 OPEN，正文分别点名 `DungeonRunDomainService` 的可空依赖与 `DungeonParticipationDomainService` 之外的多套成员登记，归属具体、与本契约职责不同。本契约自己的结果（本服务的队伍依赖改为必需、副本名单单一来源）留在 Scope In 1，没有推给上游，满足第 7 步的条件。
- RI-10 是流程意图，落在 Requires Confirmation，属于允许的确认边界，没有把执行进度写进契约。

### Assumptions

- 假设 1“个人离开副本都会同时离开队伍、运行中队伍无法增员”是整个 Goal 的前提：一旦存在“离开副本但保留队伍”的路径，Goal（成员只由队伍回答）就会和约束 1（行为不变）冲突。契约把它列为可见假设，并规定规格阶段按代码复核、有例外就回报用户，这种处理符合技能“未确认的重要前提放进 Assumptions”的要求。本次核查没有发现可达的例外路径，只发现一个需要规格阶段确认可达性的候选（非阻断风险 1），所以不需要在契约层面先解决。
- 假设 2（A21 是否允许个人离开副本但保留队伍，未取证）如实标注了证据边界，按现行实现处理，与约束 1 一致。

### Readiness

- Goal、Scope 与用户原话、#202 一致，没有歧义到会影响验收边界的程度。
- Authorization 只有一行 `task`，逐字记录用户原话，范围（含阶段提交，不含推送、PR、合并、部署）与 Scope Out、Requires Confirmation 一致。
- 每行成功条件都有可观察证据；Assumptions 可见；没有 Open Questions；Root Intent Coverage 合规；硬性机制约束都有用户、仓库规则或历史规格的依据。
- 满足 `state: ready`。

## 发现

无阻断发现。

## 非阻断风险（交给 Spec 处理，或在契约下次修订时顺手更正）

1. **“续选副本”的归类和假设 1 的候选例外。** `DungeonSelectionCommandPhases.cs:118-136` 的 `ResolveReturningMembersAsync` 在请求者不是队长、或是虚拟队伍时只返回请求者本人，随后 `:80-90` 只结束这一人的副本，队伍不变。如果此时副本仍在，这就是“离开副本但保留队伍”的路径，和 Current Situation 把它列为“整队结束”的说法、以及假设 1 都不符。从入场意图的生命周期看（选图时 `Record`，入场时 `Consume`，整队退出结束副本后才 `RestoreIfAbsent`），该处遇到运行中副本的可能性很低，像是防御性代码。建议 Spec 先确认它能不能遇到运行中副本：能遇到就按假设 1 回报用户，遇不到就按整队路径处理并写明理由。另外，“续选副本”这个叫法容易和 EPLP 的再挑战选项（整队退出路径内）混淆，Spec 宜写明指的是选图界面的回城请求。
2. **整队结束路径的清单不完整。** Scope In 3 和 SC-4 列了整队退出、续选副本、清场回城、全灭结束，漏了入场中止 `TryAbortEntry`（`DungeonRunDomainService.cs:400-421`，由 `DungeonEntryCommandPhases.cs:443-454` 的 `RollbackEntry` 调用）。这条路径直接比对 `ActiveParticipants`，删除该集合后必须改写。另外，`PreparePartyEntry` 入场时会把参与者从旧局拆下（`:153`），新模型下“同一队伍旧局未结束又开新局”该怎么处理，也需要 Spec 说明。Goal 的概括句和 SC-8 已部分覆盖，所以不阻断；建议 Spec 把这两条列入路径清单并配测试。
3. **SC-5 构造态与 SC-6 的关系，以及“图外角色”的定义。** SC-5 要求“即使队伍状态被构造为含图外角色也得不到资格”，这意味着副本仍要保留一份不可变的入场集合来过滤（Goal 允许，因为它是不可变事实）。但有两种边界没有说清：(a) 构造态里图外角色成了队长时，副本视图会出现零名队长，与 SC-6“恰有一名队长”冲突；(b) 已离开的入场者重新入队时，“队伍 ∩ 入场集合”会让他重新算作成员，而 SC-3 要求离开者不属于副本。建议 Spec 把“图外角色”定义为“不属于本局当前成员的角色，包括已离开的入场者”，并规定构造态下的队长判定。
4. **SC-1 的“拒绝第二份名单的自动测试”措辞模糊。** 这句话可以理解为“队伍变化时视图随之变化”的行为测试，也可以理解为扫描聚合里有没有可变成员集合的结构判据。两种都有 `RefactorPreferences.md` 的依据。建议 Spec 写明采用哪一种，或两种都用，避免实施时各取一种。
5. **显式增员守护的放置会影响 RI-5 的理由。** RI-5 的理由之一是“`PartyService` 不反向调用副本服务”。如果 Scope In 4 的守护放进 `PartyService` 并查询副本服务，就会出现“队伍锁 → 副本锁”的反向顺序。单执行器下仍然不会死锁，但这条理由就不成立了，而且还会新增一条 Application 内的跨领域依赖。建议 Spec 选择守护位置时保持单向依赖（例如放在协议 CMD 编排层，或通过只读接缝注入），否则要回头更新 RI-5 的理由。
6. **“本局状态已清理”的范围。** 现在的 `DetachParticipant`（`:1435-1475`）除了从集合中移除，还要释放占用登记（`participation.ReleaseCommitted`）、恢复虚弱回复、关闭战斗标志、清理结算和翻牌领取，而 `EndAsync` 还会记录离场生命周期并设置失败虚弱。新模型下整队结束可能直接删除整局，不再逐人拆离，容易漏掉这些副作用。约束 1 在可观察层面已经覆盖，建议 Spec 把这些副作用列为每条离开或结束路径的验收项。
7. **两处事实陈述可以更精确。** 一是 `entryParticipants` 在无队伍回退分支中也被整份读取，并非只读入场队长；`participantsByCharacter` 这份入场派生字典承担 AccountId 来源和过滤，没有点名。二是“`Leader` 会抛异常”在现有代码中只出现在入场时，名单不一致的常见后果是视图里没有队长、过图被拒。这两点不影响 Goal 和验收，可在下次修订时更正。
8. **“普通地下城副本”的覆盖面。** 大会（`TournamentDungeonDomainService.cs:99`）、镜像、深渊、SAO 等布局，以及祭坛的底层副本（`AltarDungeonEngine.cs` 通过 `runs.Find(runId)` 依赖普通副本）都经由同一个聚合创建。Scope Out 中的“祭坛等各自独立的运行时成员管理”只指它们自己的状态存储，不包括底层的普通副本。建议 Spec 写明这些玩法的底层副本属于本次改动范围，并把相关测试（如 `SaoTowerDomainTests`、`AltarCellGateTests`、大会测试）纳入回归。
9. **行为不变的证据形式。** 约束 1 没有对应的专门成功条件，目前只靠 SC-8 的全量测试。`RefactorPreferences.md` 要求声称行为不变时，要有能失败的依据（字节基线、表征测试等）。现有协议测试大多已断言帧和收件人，大概率够用。建议 Spec 点名哪些现有测试承担表征作用，对缺少覆盖的离开路径补充表征测试。

## 结论

`verdict: approved`。Current Situation 的事实陈述与 `1cff7218` 的代码基本一致，偏差只在措辞精度上；Goal、Scope 忠实于用户原话和 issue #202，增员守护有历史规格依据，没有越权扩大；约束与成功条件中的机制要求都有用户、仓库规则或历史规格的依据，每行都有可观察证据；Root Intent Coverage 合规，RI-5 的 `not_applicable` 和 RI-8、RI-9 的 `preserved_upstream` 都成立；两条 Assumptions 可见，并规定了处理方式。契约满足 ready 条件。上述非阻断风险交给 Spec 处理，其中第 1、2 条直接关系到假设 1 的复核和路径清单，建议在规格中优先落实。本批准不代表任务完成，也不能替代后续的完成验证。
