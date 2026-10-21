---
kind: rks-review
target: .rks-agent/tasks/online-identity-state-machine/TaskContract.md
target_revision: 2
verdict: approved
reviewer: independent subagent (reviewing-work, deliverable + modeling-task-contracts readiness rules)
---

# TaskContract 审查：online-identity-state-machine（revision 2）

## 范围

- 目标：`.rks-agent/tasks/online-identity-state-machine/TaskContract.md`，frontmatter `revision: 2`、`state: ready`。
- 重点：revision 2 的修订是否引入新的歧义、是否与来源意图冲突、是否过度约束；`SelectedSessionSafety` 连接级栅栏划出范围的理由是否成立；RI-6 的细化是否仍忠于用户授权；是否满足 ready 条件。
- 只审查，不修改契约或代码。不审查实现方案。上一轮结论（`reviews/task-contract-review-r1.md`）只用来核对 8 条非阻断风险在本轮的去向。

## 依据

- `reviewing-work` 的 Deliverable 参考：完整性、正确性、来源忠实度、可用性、范围、无依据的断言。
- `/Users/puxianguo/.claude/skills/modeling-task-contracts/SKILL.md` 的「Separate Outcomes from Mechanisms」、Create or Revise 第 5–7 步（Root Intent Coverage）和「Readiness」。
- 来源：issue #186、#185、#208（`gh issue view`，三者都是 OPEN）；契约 Authorization 逐字记录的用户原话（审查者看不到会话原文，按契约记录采信）；`Docs/Agent/RefactorPreferences.md`；用户声明 AGENTS.md 只作参考。
- 代码基准：worktree HEAD `7fc8acdc`，工作区只有未跟踪的 `.rks-agent/tasks/online-identity-state-machine/`。下文路径相对 `Server/src/`。

## 关键证据

### revision 2 新写入的事实陈述

| 契约陈述 | 代码核对 | 结论 |
|---|---|---|
| 冷却是无锁 `ConcurrentDictionary`，键为（角色, 冷却键），不带会话 | `Application/Inventory/CharacterItemCooldownDomainService.cs:23-36` | 一致 |
| 灵药不存会话副本，在自己的锁内查选中会话判定 | `Application/Characters/SpiritPotionDomainService.cs:10-37`（`ApplyForSession`、`ClearIfUnselected`） | 一致 |
| 灵药在发布时清；冷却在拆除后、且角色未被重新选中时清 | `Application/Sessions/SelectCharacterLifecycleServices.cs:393`；同文件 `:196-208` | 一致 |
| 连接撤销时在线用户立即删除，选中会话留到断线回调补清；撤销要立刻停止操作，清理要稍后在生命周期门下完成 | `Application/Authentication/LoginSessionRuntime.cs:43-45`（顶替时先 `TryDisconnectConnection`，注释写明立刻阻止角色操作、清理留给断线观察者）；`Protocol/Game/OnlineUserConnectionObserver.cs:76-87,123-131`（孤儿分支在 `lifecycleGate.AcquireAsync` 下清理） | 一致；“必要阶段”的判断有代码依据 |
| `WireUserId` | `Abstractions/Sessions/ISessionPresence.cs:26` `FindByWireUserId(ushort)`；`Data/World/SelectedSessionModels.cs:15` | 一致，r1 风险 3 已消除 |
| 一次迁完、删净的出处为 RefactorPreferences §2 | `Docs/Agent/RefactorPreferences.md` §2 有用户原话“一次性全迁移吧，没必要留一半” | 一致，r1 指出的出处偏差已更正 |
| `OnlineUserSnapshot` 对外提供当前角色与状态 | `Data/OnlineUsers/OnlineUserModels.cs:24-40`（`CurrentCharacterId`、`OnlineUserState.CharacterActive`）；#185 正文把“在线用户 `CharacterActive`+`CurrentCharacterId`”列为内联判定口径之一 | 一致；交给 #185 的归属成立 |

### 连接级栅栏划出范围的理由

`Application/Commands/SelectedSessionSafety.cs:21-28,59-65`：`blocked` 按 `SelectedSessionIdentity` 记录；`connections` 是以 `NetworkConnectionContext` 对象为键的 `ConditionalWeakTable`。`Application/Dispatching/NetworkMessageDispatcher.cs:22,29` 在解析任何会话或在线用户之前，就按连接对象拦截消息；`BlockAsync` 先立栅栏，再异步 `RequestCloseExactAsync`（`:72-74`）。

结论：理由成立。这道栅栏挡的是“这条连接上后续到达的任何消息”，与这条连接当前有没有会话、有没有在线用户记录无关。它必须在在线用户记录被删除之后、传输真正关闭之前继续有效；同一连接回到选角后重新选角时，它也必须仍然有效。这两种情况都超出单个会话记录的生命周期。Scope In 1 的措辞是“会话隔离集合”，RI-2 说的也是会话阶段，所以划出范围没有截断来源意图，Root Intent Coverage 不需要为此另加一行。

还有一个旁证：`blocked` 现在从不移除，并入会话记录后会随清理消失。由于 `SelectedSessionIdentity` 带 `ConnectionGeneration`，被拆除的会话本来就无法通过 `DemandCurrent` 的 `FindByCharacter(...) == session` 判断，所以语义不变。生产代码里 `IsBlocked(session)` 只有 `SelectedSessionSafety` 内部在读，Spec 只需再确认测试里没有依赖它。

### RI-6 的细化

原意图是“断线/协议拒绝的孤儿状态随合并解决”。revision 2 改为“该阶段必要，由同一条记录显式表达、可查询、清理后消失”。孤儿问题的实质，是“已撤销”这一事实只能从几份存储彼此不一致间接看出，清理还要靠 `FindByConnection` 兜底；“推迟清理”本身不是问题。代码证据见上：撤销必须立即生效，清理必须在生命周期门下进行。细化后，孤儿（无主、靠不一致推断）这一缺陷仍然被消除，只是不再承诺消灭一个必要的时间段。这属于用户“细节你来把持”授权内的表达调整，没有缩小用户要解决的问题，也没有扩大范围。SC-3 用“可直接查询、清理后消失”承接，证据可观察。细化忠于授权。

### 约束与 Success Conditions

- “不加业务锁”定义为不用 `lock`/`Monitor`/信号量，并发集合原子操作不算。依据是用户原话“不用锁”，定义没有超出原话，也给了可检查的判据（SC-5 的实现检查）。可行性检查：每会话一个运行时对象、按会话匹配替换或移除、迟到写入只落在已摘除的对象上，这种做法可以同时满足“不丢更新、不复活”，所以约束组合不自相矛盾。
- “保留下来的接口须由新组件直接实现”：与“无转发层”一致，具体留哪些接口交给 Spec，没有过度约束（r1 风险 5 的边界已经说清）。
- 代数精确约束区分了既有行为与本次补齐，r1 风险 1 已基本消除（剩余偏差见非阻断风险 1）。
- SC-1～SC-7 每行都有可观察证据；SC-4 补了迟到更新，SC-5 补了“无互斥锁”的实现检查。

### Readiness

- Goal、Scope 与用户认同的方向一致。新增的两条 Scope Out 都有归属或理由：快照读者交给 #185（OPEN，正文点名该口径）；连接栅栏有代码依据，见上。
- Root Intent Coverage：RI-1～RI-10 各出现一次，处置值都合法，没有 `unresolved`；四条 `preserved_upstream` 指向的 #208、#185、#182/#204 仍开启，职责与本契约不同。
- Authorization 只有一行 `task`，逐字记录用户原话，范围与 Scope Out 最后一条一致。
- Assumptions 可见，新增的“会话期间频道配置不变”给出了前提；没有 Open Questions；revision 已递增到 2。
- 满足 `state: ready`。

## 发现

无阻断发现。

## 非阻断风险（交给 Spec 处理，或在契约下次修订时顺手更正）

1. **“读到”的既有行为陈述和证据都不完整。** Goal 与约束 1 写“旧会话不能读到……新会话状态”，并称“位置、血蓝已是既有行为”。实际上，既有代码只保证位置、血蓝的“改不到/不复活”：`CharacterPositionDomainService.Move` 和 `ForgetExact` 都比对会话，`CharacterVitalRuntimeDomainService.RemoveExact` 也比对会话。读取却都按角色 ID 进行，不比对会话：`Find(CharacterId)`、`HpPercentOf(CharacterId)`，调用点如 `Protocol/Game/ReplayStateProjectionDelivery.cs:55`、`DungeonClear/DungeonClearSlice.cs:224`、`TownProjection/TownProjectionSlice.cs:265`；冷却 `PlayingCharacter.cs:173` 和灵药 `ExperienceBonusDomainService.cs:42` 也一样。SC-4 只检验“不影响”和“不复活”，没有检验“读不到”。后果：Spec 可能把位置、血蓝的读隔离当成已有能力而不补测试，也可能不迁移这些按角色读取的调用点；而由于共享租约和 `DemandCurrent`，实际暴露面很小，所以不构成阻断。建议：把约束 1 改成“位置、血蓝的写入与移除已按会话比对；四类状态的读取隔离与冷却、灵药的写入隔离按此补齐”，并在 SC-4 的证据里加入“旧会话读不到新会话状态”。
2. **阶段是否互斥没有说清。** Goal 写会话“处于已选中、在世界、已隔离、连接已撤销待清理中的哪一阶段”，读起来像互斥枚举。但现有语义里，“已隔离”可以与“在世界”“已撤销待清理”同时成立：`BlockAsync` 在连接未捕获时不会关连接，会话会以“隔离且仍在目录中”的状态留到断线清理。另外，“已撤销待清理”的会话在清理时仍须被视为角色的当前拥有者（`SelectCharacterLifecycleServices.cs:62` 的 `ownsSelectedCharacter`，以及同伴通知中的 `FindByCharacter`），但不能再接受操作。建议 Spec 明确隔离是独立维度还是阶段，并逐个调用点区分“拥有角色以便清理”与“接受操作”，落实 Scope In 2 的“保持原有接受范围”。
3. **`LoginSessionRegistry` 的连接级事实没有点名。** `LoginSessionRegistry` 的 `superseded`、`byConnection`、`CaptureAuthority` 也记录“这条连接是否已被顶替、绑定哪个账号”，`NetworkMessageDispatcher.cs:24-29` 会把它和连接栅栏一起判断。按 Goal 里“连接、账号……由一个组件回答，调用方不再拼接多处结果”的字面意思，可能有人把它也读成须并入的对象。它属于登录票据权限，性质与连接栅栏相同，按 Scope In 只需让“顶替”路径驱动在线身份的状态转移。建议 Spec 写明它保留在原处。
4. **快照与在线身份的一致性边界。** `OnlineUserSnapshot.CurrentCharacterId/State` 继续提供，而 `OnlineUser.CurrentCharacterId` 的存储要删除，所以快照必须从在线身份投影出来。`SelectedSessionSafety.Capture/DemandCurrent(recipient)` 会把快照里的账号、当前角色和会话放在一起比对；如果连接登记与在线身份不在同一一致性边界里，这个比对就变成跨边界拼接，违背 SC-2。建议 Spec 明确连接登记（账号绑定、代数、端点）与在线身份的关系，例如在线身份就是改造后的 `OnlineStateRegistry`。
5. **按频道推导时的失败语义（r1 风险 6 的剩余部分）。** `ChannelEntryPolicy.ResolveAsync` 每次都异步读取频道目录（`Application/Channel/ChannelEntryPolicy.cs:19`）。新增的假设只覆盖“配置不变”，没有覆盖“拆除时解析失败”。现有拆除路径知道是否存位置，不会因此失败。另外，现在只有已发布的会话才会存位置（`runtimes.TryGetPositionPersistence`），推导时须保留这个条件。建议 Spec 规定：解析失败时计入清理失败，并且不改变持久化结果。
6. **“协议行为不变”与“冷却、灵药补齐”之间的边界。** 正常替换路径中，账号生命周期门保证先清旧会话、再 `Select` 新会话（`Protocol/Game/SelectCharacter/SelectCharacterPhaseCommands.cs:88-105`），所以按会话归属与现状等价。只有旧会话的迟到写入这类交错，在补齐后会出现可观察差异。建议 Spec 把这类差异列为约束 1 授权的有意修正，不当作违反约束 5；对正常路径保留表征测试。
7. **沿用 r1 的风险 7、8。** 文档清单（AGENTS.md 只作参考，但仍描述这些结构）和受影响调用点清单仍交给 Spec。revision 2 已把快照读者划给 #185，缩小了风险 8 的分界不确定性。

## 结论

`verdict: approved`。revision 2 的修订没有引入阻断性的歧义、来源冲突或过度约束；连接级栅栏划出范围的理由有代码依据；RI-6 的细化忠于用户“细节你来把持”的授权；契约满足 ready 条件。上述非阻断风险交给 Spec 处理，其中第 1 条是事实陈述与证据缺口，建议下次修订时更正。本批准不代表任务完成，也不能替代后续的完成验证。
