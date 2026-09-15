---
kind: rks-review
verdict: changes_required
---

# Change Review: 城镇区域生命周期与主动退本语义归一

## Review Target

- 基线：`1e75dc85`
- 目标：当前未提交工作树
- 范围：完整代码变更，包括区域领域模型、会话桥、城镇投影、选角/选图/进本/回城/跨区/同区移动/断线生命周期、Party 联动、GiveUp/EPLP/BackToVillage 语义、DI 与测试
- 判据：`TaskContract.md` revision 1、`spec.md`、`plan.md`、`reviewing-work/references/code.md`

## Verdict

`changes_required`。区域键已从 listener port 分离为 `ChannelId + TownId + AreaId`，生产代码中的旧 `SessionPresence` 模型和注册也已清除，区域 store 的基本 lease compare-and-remove、移动修订、私有 gate 和 Party directory 镜像有直接测试；但当前实现存在四个会破坏权威区域视图或 replacement 安全的实质缺陷。

## Findings

### F-001 组队取消选图会用较旧的 direct roster 覆盖后续成员到达

- 位置：`Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs:269-303`；`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:224-277`
- 关联：RQ-005、RQ-006、RQ-010；AC-003；生命周期表“取消选图”
- 证据：`BackToVillageHandler` 逐个调用 `EnterForTownReturnAsync`，但只保存每次调用当时生成的 direct frames，直到所有成员都完成 Enter 后才一次性发送。`EnterForTownReturnAsync` 在每次 Enter 内部立即把 newcomer 发布给已进入成员。因而 A 先进入时缓存的 roster 不含 B；B 随后进入并即时通知 A；最后批量发送 A 的旧 direct roster，又把 B 从 A 的客户端区域视图中抹掉。现有测试只断言每个连接收到回城帧，没有断言跨两次 Enter 与最终 direct delivery 的全局顺序和最终 roster。
- 后果：队伍取消选图后，先处理的成员可再次看不到后处理成员，正好重现本任务要消除的“回城后缺人物”状态；领域 store 正确但客户端被旧投影覆盖。
- 最小修正：不要在逐人 Enter 中同时即时发布、又延迟发送其旧 direct snapshot。可在每名成员 Enter 后立即发送该成员 direct frames，再允许下一名的 arrival 发布；或先完成全部区域提交，再从最终权威 snapshot 为每名返回者统一生成 direct frames 和 newcomer/roster 投影。新增两名以上返回者的顺序测试，断言任一收件人的最后一份 `AREA_USERS` 包含全部应见成员。

### F-002 副本尚未成功创建就先移出城镇，拒绝/回滚路径不恢复区域成员

- 位置：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:554-583`、`:596-611`
- 关联：RQ-003、RQ-005、RQ-006；AC-006
- 证据：handler 在 `visits.EnterPartyAsync` 之前对所有 attachment 调用 `LeaveAsync`。当 `EnterPartyAsync` 返回 `null` 时只发送 `DungeonNotFound` 并返回；当成员在布局解析期间被 replacement 时只 `RollbackEntry` 并返回。两条路径都没有重新 Enter 原城镇区域。领域位置锚点仍在，但区域反向索引和 Party directory 已被清空。
- 后果：一次被拒绝或回滚的进本请求会把在线角色留在“没有副本 run、也不属于任何城镇区域”的幽灵状态；商店、技能、移动和可见性随后均按不在城镇处理，直到另一次显式回城/重登。
- 最小修正：把区域 Leave 延后到 `EnterPartyAsync` 成功且 replacement 复核通过之后、发送成功结果之前；若必须提前提交，则所有拒绝、异常和 `RollbackEntry` 分支必须用捕获的 exact session、原区域和位置执行对称恢复并投影。增加 `DungeonNotFound` 与 replacement rollback 后区域/Party directory 仍保持原值的测试。

### F-003 回城 Enter 不受同一会话迁移闸保护，迟到旧会话可覆盖 replacement lease

- 位置：`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:201-225`；`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs:164-220`；`Server/src/Dfo.Server.Protocol/Game/DungeonClear/LeaveDungeonSlice.cs:103-139`；`Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs:261-287`；`Server/src/Dfo.Server.Application/World/TownAreaDomainService.cs:23-49`
- 关联：RQ-002、RQ-003、RQ-005；AC-002、AC-006
- 证据：`EnterForTownReturnAsync` 直接调用 `townAreas.Enter(member)`，提交前既不确认 `selectedSessions.FindByCharacter(...) == session`，也不在 `SelectedSessionTransitionGate` 内。`TownAreaDomainService.Enter` 对同角色同区域的不同 lease 无条件 `Replace`，并不也不应依赖 lease 数值判断新旧。GiveUp 只在退队子步骤短暂取得 gate，释放后才 Enter；EPLP 没有 gate；BackToVillage 的 current-session 检查与 Enter 之间同样存在窗口。
- 后果：旧连接在初始检查后被 replacement 时，迟到的回城处理仍可把 lease B 对应的当前区域成员替换回 lease A；随后 B 的正常移动/清理被 exact-lease 校验拒绝，而旧 A 的投影也不会解析到当前 session，形成无法正常维护的幽灵成员。GiveUp 还可在相同窗口结束 replacement 新建的 run，因为 `runs.Find/End` 也发生在取得 gate 之前。
- 最小修正：让回城的“确认当前 exact session → 结束 run/必要时退队 → 区域 Enter”处于同一个 account transition gate 内，并避免在已持有 gate 时嵌套获取；在区域提交前最后一次复核 current session。补充阻塞式 replacement 测试：旧请求暂停、B 完成选择/入区后放行旧请求，最终区域 lease、run 和 Party 必须仍属于 B。

### F-004 区域提交有 revision，但跨账号投影没有顺序保护，旧 roster 可晚于新 roster 到达

- 位置：`Server/src/Dfo.Server.Application/World/TownAreaDomainService.cs:82-106`、`:225-246`、`:266-305`；`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:171-198`、`:280-330`
- 关联：RQ-003、RQ-006、RQ-010；AC-003、AC-005
- 证据：区域服务在单锁内原子提交并把 `MembershipRevision` 放入不可变 snapshot，但 `TownAreaProjectionDelivery` 不读取或比较该 revision，也没有按区域/收件人串行化投影。不同账号的命令可并发执行：离开 A 先提交 rev2 后暂停发送，加入 C 再提交 rev3 并先向 B 发送包含 C 的视图，随后 A 的 rev2 `PublishRosterAsync` 仍会向 B 发送不含 C 的旧 roster。`NotificationBus` 只保证单次 publish 内逐收件人发送，不序列化两个并发 publish。
- 后果：领域实体保持正确，但客户端最终停在较旧成员视图；任意并发加入、离开或跨区均可产生该逆序，权威 snapshot 的 revision 目前没有实际保护作用。
- 最小修正：为城镇区域投影增加按 `TownAreaKey`（必要时再按收件人）的顺序边界，并在发送前拒绝低于已投影 revision 的事件；或在获得顺序权后重读并发送最新权威 snapshot。新增可控阻塞测试，强制 rev2 投影晚于 rev3 恢复，断言最后交付仍是 rev3 且旧事件不会覆盖。

## Checks

- `dotnet build Server/86JP.Server.slnx -c Release`：通过，0 warning / 0 error。
- 定向 Application 测试（TownArea、Registry、TownPresence、SessionGeneration）：24/24 通过。
- 定向 Protocol 测试（TownAreaProjection、TownProjectionHandler、PartySelectDungeon、LeaveDungeon、PartyTownProjection）：35/35 通过，但未覆盖上述四个交错/失败路径。
- `git diff --check 1e75dc85`：通过。
- 完整 `dotnet test Server/86JP.Server.slnx -c Release --no-build`：未通过。Protocol 的 43 个失败以缺失 `Script.pvf` 为主；Application 出现一次 `SQLitePCL.sqlite3` 已释放异常，单独详细复跑该用例通过。该环境结果不能作为 SC-7 的通过证据，且不改变以上代码级 findings。

## Non-blocking Observations

- 生产 `Server/src` 已无 `SessionPresence` / `ISessionPresenceDomainService` 引用；剩余同名类型仅位于测试兼容适配器。
- 生产 DI 已注册 `ITownAreaDomainService` 与 `TownAreaProjectionDelivery`，Release 组合编译通过；本轮未发现 Application → Infrastructure 新引用。
- `LegacyTownAreaTestAdapter.cs` 让大量旧 fixture 继续以旧 presence 词汇运行，并不能证明真实区域 store 的并发和 revision 语义。它不是生产模型回归，但应逐步以直接 `ITownAreaDomainService` fixture 替换，避免继续掩盖生命周期缺口。

## Round 2

```yaml
kind: rks-review
verdict: changes_required
```

### 结论

`changes_required`。F-001、F-003、F-004 已由最终快照批量回城、account transition gate 与按 `TownAreaKey` 串行且重读最新状态的投影路径关闭；F-002 只关闭了 `EnterPartyAsync` 拒绝和第一次 replacement 复核两条路径，进入流程后段仍存在“先离区、再 rollback、但不恢复区域”的确定性分支。

### Round 1 findings 复核

- **F-001：已关闭。** `SendTownReturnBatchAsync` 在持有全部目标区域投影 gate 时先从最终权威 snapshot 为每名 admission 重新生成 direct frames，并用一次 explicit delivery 发给所有返回者；之后才向非本批返回者发布 newcomer，避免旧 self-only roster 覆盖后到成员（`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:295-355`）。`Batch_return_builds_every_direct_roster_from_the_final_area_state` 直接覆盖两名返回者共享最终 roster。
- **F-002：未关闭。** 见 R2-F-001。
- **F-003：已关闭。** `TryAdmitForTownReturn` 在提交前复核 exact current session；EPLP、GiveUp 与 BackToVillage 均在 `SelectedSessionTransitionGate` 的 account 独占 lease 内完成 run 结束及 admission，BackToVillage 对多账号排序取锁并在锁内再次复核（`TownAreaProjectionDelivery.cs:235-263`；`LeaveDungeonSlice.cs:125-147`；`DungeonClearSlice.cs:176-209`；`DungeonSelectionSlice.cs:236-278,328-352`）。阻塞式 replacement 测试覆盖迟到旧请求不能覆盖新 lease。
- **F-004：已关闭。** `TownAreaProjectionGate` 以 `TownAreaKey` 为键；transfer、movement、leave、return、batch return 与 roster reconciliation 的发送阶段均获得同一 gate，并在 gate 内重读权威 area，而多区域取锁按 `ChannelId/TownId/AreaId` 稳定排序且去重（`TownAreaProjectionDelivery.cs:16,59-233,265-373,503-526`）。因此旧调用取得发送权后不会再投影其捕获的旧 snapshot；`Delayed_old_roster_projection_re_reads_the_latest_revision` 覆盖此行为。

### Round 2 findings

#### R2-F-001 SelectDungeon 后段 rollback 仍会把在线成员留在无 run、无城镇区域的状态

- 位置：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:604-616,684-744`
- 关联：Round 1 F-002；RQ-003、RQ-005、RQ-006；AC-006
- 证据：当前代码在 run 创建和第一次 session generation 复核之后调用所有成员的 `LeaveAsync`，但这并非最后一个 rollback 边界。随后任一成员 `ProgressAsync` 返回 `null` 会在 689-692 行 `RollbackEntry` 后直接返回；耗时读取后最终 generation 复核失败会在 713-725 行 rollback；`deliveryRegistry.Register`、fatigue、progress 或发送抛异常会在 740-743 行 rollback。`RollbackEntry` 只结束 run 并移除 delivery participant，不重新 admission 原城镇区域。
- 后果：上述分支中的未被 replacement 的在线成员最终既没有 dungeon run，也不属于原城镇区域；其位置锚点仍存在，但移动、商店/技能可用性、区域广播和 Party directory 都看到离区状态。多人进本时，只需另一成员在最终复核前被 replacement，就会让其余仍有效成员一起落入该状态。
- 最小修正：把 `LeaveAsync` 移到所有 frame 构造和最终 generation 复核均成功后的实际提交边界，并确保该边界之后不再走只执行 `RollbackEntry` 的路径；若仍需在可能抛错/rollback 的发送之前离区，则记录成功离区的 exact admission 信息，并在每个后续 rollback/异常分支对仍为 current 的成员对称恢复区域及 Party directory。新增测试至少覆盖 `ProgressAsync == null` 与最终 generation 复核失败，断言所有仍有效成员在 rollback 后仍拥有原 exact area lease。

### Round 2 checks

- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --filter "FullyQualifiedName~TownAreaProjectionDeliveryTests|FullyQualifiedName~LeaveDungeonTests|FullyQualifiedName~SelectDungeonHandlerTests"`：38/38 通过。
- 上述定向测试完成 Release 编译，0 warning / 0 error。
- `git diff --check 1e75dc85`：通过。
- Round 1 已记录的完整测试环境阻塞（缺失 `Script.pvf`，以及一次不可复现的 SQLite disposed 异常）未在本轮重复消耗；本轮 verdict 来自可独立复现的代码级 rollback 路径，不依赖该环境问题。

## Round 3

```yaml
kind: rks-review
verdict: changes_required
```

### 结论

`changes_required`。Round 2 的 `ProgressAsync == null`、frame 构造异常以及两次显式 generation 复核失败现在都在离区之前 rollback，R2-F-001 所列的原始确定性分支已关闭；新增的 account transition gates 也按账号稳定排序并在离区前再次复核 exact session。不过新的 commit boundary 对“取得 gate 失败”和“多成员逐个离区时投影失败”没有闭合，仍能产生 run 与城镇区域成员事实不一致。

### 既有 findings 复核

- **F-001：保持关闭。** 批量回城仍先从最终权威 area 生成并一次发送所有返回者的 direct roster，再向非本批成员发布 newcomer。
- **F-002 / R2-F-001：原列分支已关闭，但同一验收问题仍被 R3-F-001、R3-F-002 阻塞。** `ProgressAsync == null`、最终 preparation generation 失败和 preparation 异常均发生在 `LeaveAsync` 之前；commit-boundary gate 内又复核一次 generation。
- **F-003：保持关闭。** EPLP、GiveUp、BackToVillage 的 admission gate 未回退；SelectDungeon 新增多账号 transition gate，且按 `AccountId` 排序、去重、逆序释放。
- **F-004：保持关闭。** `TownAreaProjectionGate` 及 gate 内重读权威 area 的路径未回退。

### Round 3 findings

#### R3-F-001 transition gate 获取取消会在 commit boundary 之前遗留 run 与城镇成员并存

- 位置：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:607-722,724-744,770-794`
- 关联：RQ-003、RQ-005；AC-006；Round 2 R2-F-001
- 证据：`EnterPartyAsync` 已创建 run，delivery registry、fatigue 与 frames preparation 也已完成；随后 729-730 行在 preparation 的 rollback `try/catch` 之外等待全部 account gates。`AcquireTransitionsAsync` 在取消或获取异常时只释放已取得的 gate 并重新抛出，不调用 `RollbackEntry`。此时尚未执行任何 `LeaveAsync`，因此角色仍属于城镇区域，而 dungeon run 已保留。
- 后果：一个在 gate 竞争期间被取消的实际进本请求会同时拥有活跃 run 与城镇 area lease，后续移动/商店/区域广播和副本参与对同一角色给出冲突事实；这不是 commit boundary 后允许保留 run 的 transport 失败，因为离区提交尚未开始。
- 最小修正：把 `AcquireTransitionsAsync` 纳入 pre-commit rollback 保护；仅在全部 gate 成功取得且 gate 内 generation 复核通过后跨越 commit boundary。新增可控 gate 阻塞 + cancellation 测试，断言取消后 run 被结束、delivery registry 清理且 exact area lease 保留。

#### R3-F-002 首名成员的离区投影失败会阻止后续成员提交离区

- 位置：`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:724-767`；`Server/src/Dfo.Server.Protocol/Game/TownProjection/TownAreaProjectionDelivery.cs:209-233,425-473`
- 关联：RQ-003、RQ-005、RQ-006、RQ-008；AC-006、AC-007
- 证据：commit boundary 内按 attachment 逐个 `await townAreaProjection.LeaveAsync`。`LeaveAsync` 先同步 `LeaveExact`，然后等待 area gate、roster notification 和 Party projection；这些锁等待/网络投影均可抛错。若第一名成员在领域离区后、投影阶段抛错，SelectDungeon 循环立即退出，第一名已离区，后续成员仍在城镇，但同一个 party run 已为所有 participant 提交。这里“transport 失败后不 rollback run”只保护了已处理成员，不能保证剩余成员完成领域提交。
- 后果：组队进本可形成同一 run 中部分成员不在城镇、部分成员仍在城镇的撕裂状态；后续成员的 Party directory、区域可见性和商店/移动可用性仍错误地表明其处于城镇。
- 最小修正：将全体成员的领域 `LeaveExact` 提交与可失败投影拆开，确保所有 exact leave 均先完成，再进行 best-effort roster/Party 投影和 first-room delivery；或提供一个 batch leave/commit API，使投影失败不会阻断其余成员的区域提交。新增两成员测试，让第一名的 roster/Party 投影抛错，断言两人的 area lease 都已移除、Party directory 都为非城镇且 run 保持已提交。

### Round 3 checks

- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --filter "FullyQualifiedName~TownAreaProjectionDeliveryTests|FullyQualifiedName~LeaveDungeonTests|FullyQualifiedName~SelectDungeonHandlerTests|FullyQualifiedName~PartySelectDungeonHandlerTests"`：39/39 通过。
- 上述命令完成 Release 编译，0 warning / 0 error。
- `git diff --check 1e75dc85`：通过。
- 新增测试证明普通 refusal、`ProgressAsync == null` 与 preparation 阶段 replacement rollback 保留仍有效成员的 area；未覆盖 gate 获取取消或逐成员离区投影失败，因此不反驳本轮 findings。

## Round 4

```yaml
kind: rks-review
verdict: approved
```

### 结论

`approved`。Round 3 的两项 finding 均已关闭，本轮未发现新的阻塞问题。进本流程现在把所有可回滚的 preparation 放在离区之前；account transition gates 获取失败显式 rollback；全部 gate 成功且再次通过 exact-generation 复核后，先同步提交所有成员的 area departure，再发送首房间，最后逐项投影 departure。由此 transport/投影失败不再回滚已提交 run，也不会让同一 run 的后续成员滞留城镇。

### Findings 关闭情况

- **F-001：关闭。** `SendTownReturnBatchAsync` 仍从最终权威 area 为所有返回者生成 direct roster，并在 newcomer 投影之前一次交付，未回退到逐人缓存旧 snapshot 的顺序。
- **F-002 / R2-F-001：关闭。** refusal、`ProgressAsync == null`、frame/preparation 异常以及 preparation/final generation 失败均在 area departure 前 rollback；成功路径的实际进本 departure 发生在明确 commit boundary。
- **F-003：关闭。** EPLP、GiveUp、BackToVillage 的 admission 仍受 account transition gate 保护；SelectDungeon 对全体 attachment 按 `AccountId` 排序取 gate，并在持锁期间复核 exact generation、提交 departure 和发送首房间，replacement 无法插入其间。
- **F-004：关闭。** `TownAreaProjectionGate`、gate 内重读最新 area 以及多区域稳定排序/去重均保留。
- **R3-F-001：关闭。** `AcquireTransitionsAsync` 外层新增 pre-commit `try/catch`；获取取消或异常时先释放已取得的 gate，再由 handler 对所有 participant 执行 `RollbackEntry`。`Cancelling_while_acquiring_commit_gates_rolls_back_before_leaving_town` 断言 run 清理且两名成员保留 exact area lease（`Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:729-739,794-818`）。
- **R3-F-002：关闭。** `TownAreaProjectionDelivery.TryLeave` 只同步提交 area/Party-directory 事实并返回不可变 departure；`ProjectDepartureAsync` 单独执行可失败投影。SelectDungeon 在任何首房间发送或 departure 投影前遍历全体 attachment 完成 `TryLeave`，因此后续投影失败不会中断剩余成员的领域离区提交。`A_projection_failure_cannot_split_party_members_between_town_and_run` 断言两名成员均已离区且 run 未 rollback（`TownAreaProjectionDelivery.cs:225-277`；`DungeonEntrySlice.cs:741-790`）。

### Round 4 checks

- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --filter "FullyQualifiedName~TownAreaProjectionDeliveryTests|FullyQualifiedName~LeaveDungeonTests|FullyQualifiedName~SelectDungeonHandlerTests|FullyQualifiedName~PartySelectDungeonHandlerTests"`：41/41 通过。
- 上述命令完成相关项目 Release 编译，0 warning / 0 error。
- `git diff --check 1e75dc85`：通过。
- Round 1 已记录的完整测试环境依赖问题未改变；本轮批准针对代码变更及可运行的受影响定向范围，不把缺失 `Script.pvf` 误报为产品回归。
