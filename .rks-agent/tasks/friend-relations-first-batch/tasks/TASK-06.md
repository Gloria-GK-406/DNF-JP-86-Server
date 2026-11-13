# TASK-06 — 好友在线推送（上线、下线、升级、转职、删除）

## Task Unit Contract

- Revision: 1
- Outcome and references: Plan T-005、SD-6、SD-8。把某角色加为好友的在线玩家（观察者）在该角色上线、下线、升级、转职、被删除时，不用重登就能看到变化，其他人收不到。覆盖 RQ-050 – RQ-055，并按 Spec AM-2 执行；验收 AC-050。
- Owned boundary:
  - Game 层新增好友推送的通知 record、处理器与登记。放在 `Server/src/Dfo.Server.Protocol/Game/` 下，命名自定（例如 `FriendPresenceNotifications.cs`），在 UnitedFriend 切片的注册里登记。
  - 下线：
    - `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs`（清理计划里记录好友观察者）；
    - `Server/src/Dfo.Server.Data/World/SessionEntryModels.cs`（`SelectedSessionCleanupNotificationPlan` 增加一个 init 属性）；
    - 三个投递点：`StateSynchronization/SupplementalSynchronizationMessages.cs` 的 `CleanupNotificationDelivery`、`ReturnToSelection/GameReturnToSelectionCommand.cs`、`SelectCharacter/GameSelectCharacterCommand.cs` 中顶替旧会话的清理投递。
  - 上线：`SelectCharacter/GameSelectCharacterCommand.cs` 入场发布之后。
  - 升级：订阅已有的 `CharacterLevelChanged`。
  - 转职：
    - `Server/src/Dfo.Server.Application/Effects/QuestCompletionEffectHandlers.cs`，转职与觉醒两处 `SetGrowTypeAsync` 成功后发布新领域事件；
    - 事件 record 放在 Abstractions，位置与 `CharacterLevelChanged` 同类；
    - 在 Application 的注册里 `AddDomainEvent`。
  - 删除：`Server/src/Dfo.Server.Protocol/Game/DeleteCharacter/GameDeleteCharacterCommand.cs`。
  - 对应测试，包括 Application.Tests、Protocol.Tests 中现有的选角／清理／删角色测试为适配所做的修改。
  - 协议页：`NOTI-0x0112.md`、`NOTI-0x0111.md` 的推送时机说明，以及 catalog 对应行。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx`；
  - 设置 `PVF_ARCHIVE_PATH` 后，Protocol.Tests、Application.Tests、Host.Tests、Networking.Tests 全部通过；
  - 新增测试覆盖 AC-050：
    - 上线：观察者收到 0x0112，ch 为实际频道号；非观察者收不到；
    - 下线：断线、回选人、顶替三个出口，观察者都收到 0x0112，ch=0；
    - 升级、转职：观察者收到全量列表与黑名单列表；
    - 删除：在线观察者收到 subcmd 2 与全量列表；
    - 0x0112 的 bl 位反映接收者是否拉黑了对方；
    - 顺序用例：连续变化后，最后一份列表反映最终状态；
    - 观察者重连后，旧会话收不到推送。
- Dependencies: TASK-05
- Result review: required — 跨选角、清理、删角色、领域事件多个生命周期挂点。漏掉任何一个出口，用户可见的表现都是「好友一直显示在线」这类难以察觉的错误；三个投递点的一致性也只能靠审查确认。

## Detailed Plan

**共享件**（来自 TASK-05，位于 `Dfo.Server.Protocol.Game`）
- 帧：`FriendPresenceMessage(ushort Channel, byte[] Name, bool Blacklisted)`、`FriendRemovedMessage`、`FriendListMessage`。
- 构建：`FriendListRefresh.BuildAsync(owner, relations, identities, sessions, channels, zone, ct)`，其 `.Messages` 依次为全量列表和 0x0078 列表。
- `FriendServerGroup.Own`。

**通知形态**

所有推送都用 `NotificationAudience.TheseCharacters(观察者)` 投影，处理器按收件人组装。NOTI 处理器只注入 `*DomainService`。

- `FriendCameOnline(CharacterId Who)`：处理器读 Who 的身份名、所在频道号（AM-2）、「收件人是否拉黑了 Who」，产出 `FriendPresenceMessage(ch, name, bl)`。
- `FriendWentOffline(CharacterId Who, byte[] Name)`：名字由发起方带上，因为下线时会话已经释放；产出 `FriendPresenceMessage(0, name, bl)`。
- `FriendListChanged(TimeZoneInfo Zone)`：处理器为每个收件人调 `FriendListRefresh.BuildAsync`，返回 `.Messages`。升级、转职与删除后的全量都用它。
  - 时区由发起方带入：CMD 或订阅者可以注入 `TimeProvider`，处理器不行。
  - 只有内存投影可以带 `TimeZoneInfo`；如果走持久化事件，就改带时区 ID。
- 删除另有 `FriendRemoved(byte[] Name)`，产出 `FriendRemovedMessage([name])`，排在 `FriendListChanged` 之前登记。

**观察者集合**：`ICharacterRelationQueryDomainService.ObserversOfAsync(who, Friend)`，再只保留在世界中的（`sessions.TryGetInWorld`），并排除 who 自己。

**挂点**
1. **上线**：在 `GameSelectCharacterCommand` 入场发布（`PublishCommittedEntryAsync`）之后，用只读单元读出观察者，再 `projections.ProjectAsync`。
   - 顶替旧会话时，顺序是：先投递旧会话清理（含好友下线），再在新会话入场后发上线。两条 0x0112 先下线后上线，与客户端行为相符。
2. **下线**：照 1:1 对话 `ConversationDepartures` 的做法。
   - `SelectCharacterLifecycleServices.CleanupAsync` 在释放会话前，取得离开者的名字与观察者，记入计划的新属性 `FriendDeparture`（Who、Name、Observers）。这里的读取是 async 的，现有 `Run(Func<bool>)` 是同步的，所以要放在 Run 序列之外、会话释放之前，或者先读好再进入 Run。
   - 共享的 `FriendDepartureNotices.For(plan)`（Game 层，参照 `ConversationDepartureNotices`）给出受众与通知。
   - 三个投递点都在 1:1 离开通知之后加上它。三处缺一不可，测试逐个覆盖。
   - 读取失败时不得阻断清理：清理的其他部分照常，下线通知可以缺失，但要记录失败。
3. **升级**：在 Protocol 层新增 LiveOnly 订阅者（参照 `Game/Achievements/AchievementsRegistration.cs` 的 `AchievementEventSubscriber`），订阅 `CharacterLevelChanged`。
   - 订阅者里读观察者，再 `ProjectAsync(FriendListChanged)`；需要事务时遵循订阅者现有的写法。
   - 合并键用角色 ID，连续升级只推一次。
4. **转职**：新增领域事件 `CharacterGrowTypeChanged(CharacterId, int GrowType)`。
   - 在转职与觉醒两个效果处理器 `SetGrowTypeAsync` 成功之后 `EnqueueAsync`；效果处理器本身就在可写业务事务内。
   - 订阅者与升级共用一个类，或者写两个，执行时选简单的那个。
5. **删除**：`GameDeleteCharacterCommand` 的写单元在 `DeleteCharacterAsync` 成功后，同单元调用 `ICharacterRelationDomainService.PurgeAsync(characterId)`，得到 `FriendObservers`。
   - 已删角色的名字用请求里的 `RawName`，它就是删除时核对过的名字。
   - 提交后，对仍在世界中的观察者依次投影 `FriendRemoved(name)` 与 `FriendListChanged(zone)`。
   - 删除失败时不调 Purge。

**测试**（测试先行）
- 优先用 Application.Tests 里能驱动选角、回选人、断线的现有夹具（`SelectCharacterLifecycleTests`、`SelectedSessionPartyCleanupTests`、`CleanupNotificationDeliveryTests` 一类）加两个角色。
- 每个挂点：观察者与非观察者各一个，断言帧与 ch／bl。
- 顺序用例与重连用例见 Required checks。

**文档**：`NOTI-0x0112.md`、`NOTI-0x0111.md` 写推送时机（CurrentImplementation），catalog 中 0x0112 改为已有发送方。

## Result

- Outcome: done
- Round: 1，worker 实现。owned change 为当前全部未提交 diff，不含 TASK-07.md。
- Changed:
  - `Game/FriendPresenceNotifications.cs`：
    - 通知：`FriendCameOnline`、`FriendWentOffline`、`FriendListChanged(Zone)`、`FriendRemoved`，各自带处理器；
    - `FriendObservers`：读取在世界中的观察者，排除本人；
    - `FriendDepartureNotices.For(plan)`；
    - LiveOnly 订阅者 `FriendObserverRefreshSubscriber`，订阅 `friend.level-refresh` 与 `friend.grow-type-refresh`，按角色 ID 合并。
  - 挂点：
    - 上线：`GameSelectCharacterCommand` 在入场发布之后调用 `AnnounceToFriendsAsync`，观察者在只读单元 `ReadFriendObservers` 中读取。
    - 下线读取：`SelectCharacterLifecycleServices` 在会话处于 InWorld、且尚未释放时读取，结果记入计划的 `FriendDeparture`。
    - 下线投递：三个投递点都在 1:1 离开通知之后发出，分别是断线 `SupplementalSynchronizationMessages`、回选人 `GameReturnToSelectionCommand`、顶替 `GameSelectCharacterCommand`。
    - 删除：`GameDeleteCharacterCommand` 在同一写入单元里调用 `PurgeAsync`；提交后对在世界中的观察者投影 `FriendRemoved` 与 `FriendListChanged`。
    - 转职、觉醒：新增事件 `CharacterGrowTypeChanged`，在 `QuestCompletionEffectHandlers` 两处 `EnqueueAsync`，并登记该领域事件。
  - 数据：`SessionEntryModels` 新增 `SelectedSessionFriendDeparture`，以及 init 属性 `FriendDeparture`。
  - 测试：
    - 新增 `FriendPresencePushTests`（真实 DI、SQLite、NotificationBus、编码器）与 `TestCharacterRelations` 桩；
    - 在选角、清理、回选人、断线投递、效果相关的现有测试中补用例，并适配构造参数。
  - 文档：`NOTI-0x0112.md` 的推送时机、`NOTI-0x0111.md`、catalog。
- Why:
  - **R1 已证**：通知处理器返回的 CMD 0x0078 经同一个 sender 按各自编码器编码。升级与删除用例断言帧依次为 `(0,0x0111)`、`(1,0x0078)`。
  - **下线读取失败**：（Round 1 的做法，已被 Round 2 取代，见下）
  - **未配置频道的端口**：上线处理器对该收件人报失败，不发会被客户端显示成「已下线」的 ch=0。
- Checks（最终源码，已设 `PVF_ARCHIVE_PATH`）：
  - build：0 警告，0 错误；
  - Protocol.Tests：2282 通过，0 跳过；
  - Application.Tests：6700 通过，0 跳过；
  - Host.Tests：243 通过，1 跳过（原有）；
  - Networking.Tests：218 通过；
  - RED：接上挂点之前，9 个 Application 用例、2 个 Protocol 用例按预期失败。
- Remaining：上下线提示行、同频道显示频道号等客户端表现，待 T-007 实机确认。
- Round 2 (rework after quick review round 1: changes_required, B1):
  - `SelectedSessionCleanupService` now catches a failed friend-departure read itself. It logs a Warning through the injected `ILogger<SelectedSessionCleanupService>`, leaves `FriendDeparture` null, and does not add the failure to `failures`. Return to selection therefore keeps its connection and response, and a replacing selection no longer rolls back the earlier settled steps.
  - The `SelectedSessionPartyCleanupTests` case is renamed to `A_failed_friend_read_drops_only_the_friend_notice`. It asserts that the cleanup does not throw, that `FriendDeparture` is null, that the 1:1 departure is still planned, and that the session is released.
  - The five test construction sites pass `NullLogger`.
  - `NOTI-0x0112.md` is corrected to match.
  - N1 (a failed online-announcement read fails the selection) is left as is. A failed command work unit stays failed even when caught (`Document/GameplayExecution.md`). It is a separate reverse query (`ObserversOfAsync`) made after the entry is published, so a failure there closes a connection that has already entered; that is unlikely, since the same database was just read in this command. Remaining: move the keeper read into the pre-publication `ReadSelectedCharacterRelations` unit and filter for in-world at send time.
- Quick review round 2: approved (independent context). R2, which would assert the Warning and add end-to-end tests for return to selection and replacement, is optional and not done.
  - Checks (after a fresh build, with `PVF_ARCHIVE_PATH` set): build 0 warnings, 0 errors. Application 6700, Protocol 2282, Host 243 (1 skip), Networking 218; all passed.
