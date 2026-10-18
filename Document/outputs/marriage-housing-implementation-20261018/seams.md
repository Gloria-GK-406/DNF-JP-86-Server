# 婚房实现接缝核对（只读调查）

日期：2026-10-18。范围：当前代码、已提交 A21 静态证据；没有修改生产代码，没有运行客户端验证。主代理正在并行实现，行号只作为当前快照定位，方法名优先。

## 1. 属性：当前代码的真实边界

**CurrentImplementation / Confirmed**：当前没有完整的服务端装备、BUFF 战斗属性聚合器。

- `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:46` 的 `BaseStatsAsync` 只读取职业、等级，再调用 `ICharacterStatRuleService.GetStats`。
- `CharacterStatRuleService.GetStats` 是职业等级表，不能注入角色婚姻状态；否则规则服务变成有状态角色查询，破坏现有纯静态规则边界。
- `CharacterEntryStateServices.cs:243` 的 `CharacterEntryStateDomainService.LoadAsync` 用 `CharacterBaseStatProjection.Apply` 覆盖持久化兼容字段；`CharacterStateProjectionDomainService.RefreshAsync` 同样覆盖基础字段。
- `Server/src/Dfo.Server.Data/Characters/CharacterModels.cs:279` 明确注释：`CharacterDerivedStatsStorageRecord` 是兼容字段，不是完整 equipment/buff-derived 模型。当前该结构也没有独立攻击、全属强、物理暴击、魔法暴击、城镇移动速度等 PVF 婚房属性的独立字段。
- `CharacterStatValues.ToFixedPoint` 把职业表值乘 10；这不能推出所有婚戒/PVF token 都统一乘 10。尤其 `move speed 20` 与 `village moving speed 10` 不能混成一个 `MoveSpeed += value * 10`。

**ClientStatic / Confirmed**：`03D8` 真实家具列表进入 native 家具 map 后，调用 RVA `00881DF0` 汇总家具值，并写本角色 `+0x388`；见 `a21/handlers-disassembly.txt:2508`、`2514`。`03D1` 和 `03F4` 成功共用 RVA `00892960` 设置戒指等级。参考报告还记录了客户端依据家具列表恢复面板属性的上游实机观察（仅 `UpstreamReference / High`）。

### 最小可靠实现建议

1. `HousingReadView` 提供不可变的 `MarriageAttributeSnapshot`（RingLevel、FurnitureValue、所选 PVF tier、原始 token/value），只依赖关系只读、HousingState、PVF 内容，绝不依赖完整 MarriageDomainService、效果执行器、角色属性投影。
2. 持久化只存家具位置和戒指等级，不存“已经叠加过的角色属性”。纯函数每次从基础值及这份 snapshot 计算一次；重复查询结果相等，换宠、重登都不累加。
3. 客户端优先只通过 native `03D0 / 03D1 / 03D8` 状态让其自行应用。**不要同时把相同婚姻 BUFF 塞入 USERINFO 基础字段**；否则 native 可能再叠一次。要声称服务端战斗计算已支持，必须新增显式 effective-stat 查询/模型并接到真实战斗消费者；当前没有消费者，不能把 `DerivedStats +=` 称为完整战斗实现。
4. 服务端 effective-stat 查询显式接收用途或环境，如 Normal / Dungeon / Arena。Arena 返回零婚房/婚戒增量；不要在底层 `CharacterEntryStateDomainService.LoadAsync(accountId,charId)` 猜场景，因为该 API 没有 listener/channel 参数，且会被配偶资料和支援角色读取。
5. 竞技场已知判定点：`CharacterCurrentStateQueryDomainService.ReadAsync:55,91` 从 session listener 解析 `channel.IsPvp`；`SelectCharacterPreparationService.PrepareAsync` 已有 `channelPolicy.PvpSkillProfileApplies`。只有这些上层具有可靠场景上下文。客户端是否自动按竞技场屏蔽仍需 A21 实机验证，不要发空家具快照模拟“竞技场清资产”，离开后可能丢状态。

## 2. 客户端重建时需要重发的接缝

统一建立婚姻只读投影构建器，接受选中 session，顺序生成真实 `WeddingInfo(03D0)`、配偶外观（如 native 必需）、`CoupleRoom(03D8)`，以及必要 `Ring(03D1)`。上游观察推荐 `03D0 → 03F3 → 03D8`，不是本仓 ClientVerified。本人房间快照不能广播给所有看见角色的路人。

| 触发路径 | 当前接缝 | 建议 |
|---|---|---|
| 选角 | `SelectedCharacterStateMessageFactory.Create:111` 固定空 `03D8`，随后 `WeddingInfo` | 已婚改真实快照；未婚保留 204 零兼容体。不要把当前顺序当 native 有序证据 |
| 换宠/移动装备 | `MoveItemSpaceSlice.RefreshSessionAsync:958` 产生 `CurrentUserStateMessage` | 返回 owner 当前状态后追加 owner 婚姻投影，不能只刷新角色外观 |
| 进入普通地下城 | `DungeonEntryProjectionDelivery.cs:206` 产生 receiver 及 peers 的 `ExtendedUserStateMessage` | 所有 USERINFO 完成后，仅追加 receiver 自己婚房/戒指状态；peer 的房间不能覆盖 receiver |
| 回地下城选择/回城 | `DungeonSelectionSlice.cs:667`、`TownAreaProjectionDelivery.ProjectTransferAsync` | 当前角色 USERINFO 重建后补投影，场景移交之后只发自己的资产 |
| 幻影塔入口 | `IllusionTowerProjectionDelivery.cs:155` | 同样可能重建 USERINFO，需纳入回归 |
| 请求重放 | `ReplayStateProjectionDelivery.Create:153` | CharacterAppearance 分支尾追加婚姻当前快照，或由新 Housing bit 强制覆盖 |
| 完成任务/等级券/物品过期 | `FinishQuestHandler.cs:229`、`LevelUpTicketSlice.cs:140`、`ExpiredItemsProjection.cs:71` | 同样 USERINFO 重建，建议统一 owner refresh helper，避免只补最初三条路径 |

`TownPresenceNotifications.AddCharacter` 也发送 USERINFO，但目标可能是旁观者，不能无条件追加源角色 `03D8`（该包没有房主标识，写的是接收方自己的 manager）。

## 3. 每日个人心意点 +1

最小接入位置：`SelectCharacterHandler.HandleMessageAsync:137` 在 `EnsureDailyContractPuppetsCommand(session)` 后、`PrepareSelectedCharacterStateCommand` 前，再执行 `EnsureMarriageDailyLovePointCommand(session)`。这保证选角准备读取的 engagement snapshot 已经包含当天新余额。

- 新 command 走 `Transactional`，`ISelectedSessionCommand + IAccountCommand`，account exclusive；如果同时操作共享关系 revision，沿已有 M 协调先收集夫妻账号再 `IMultiAccountCommand`，不要账号锁内反向取配偶锁。
- M 内验证 exact selected-session、角色归属、当前关系 Married，再向 ledger wallet 写个人 +1，最高 999。
- 使用 `IDailyResetRuleService.PeriodStart(clock.GetUtcNow())`。实现位于 `Application/Configuration/DailyResetRuleService.cs`，固定业务时区 UTC+8，具体重置时间来自 `Game:DailyResetTime`。
- 每日领取标记必须按 `character_id + period_start` 唯一，不按 relation_id 唯一；否则离婚重婚当天重复领取。钱包行仍按 relation+character 属于本段关系。
- claim 与 +1 同事务；到上限时如何登记“当天登录已领”必须确定，推荐仍登记，避免当天花点后通过重登补领。
- 只在选角流程变更，不放 `SnapshotAsync`、`ViewAsync`、`ReplayStateDomainService.ReadAsync` 等只读方法，也不放通用 inventory 日补给（不是物理材料）。
- 若计划“跨日仍在线也加1”，可参考 `TauKingdomDailyRefill.cs` 的 `DailyResetTaskSchedule` + 每角色事务 job；原始需求是每日登录，此项不是一期必需。Tau 选角后再复查跨 06:00 是防发布窗口竞态参考，不意味着一定固定 06:00。

## 4. 重放：bit、校验、读取、发送四处必须一起改

1. `Abstractions/Effects/IReplayStateProjection.cs:14` 新 `ReplayProjectionNeeds.MarriageHousing = 32768`（先检查主线并发改动是否已占用）。
2. **同文件 `ReplayProjectionRequest.Validate:29` 白名单要加入此 bit**。只加 enum 会在 `EffectOperationContext.Freeze` 阶段抛出，主操作不能完成。
3. `ReplayStateSnapshot` 加住房当前状态（家具/仓库/树/戒指/本人个人点快照）。不存旧收获结果作为“重新给予”输入。
4. `Application/Effects/ReplayStateDomainService.ReadAsync` 根据 flag 在受保护事务中从纯 query facade 读取。读取后 exact-session safety 仍需保留。禁止读出时补每日心意点或懒投递奖励。
5. `Protocol/Game/ReplayStateProjectionDelivery.Create` 添加对应住房当前快照。只重绘当前资产，不再次发购买 ACK、获得数量播报、奖励增量、邮件投递；收获显示帧 `03DE` 本身也不应重演为发奖。
6. C2S 购买/戒指/种植/浇水/收获/清除请求实现 `IEffectSettlementRequest`，`MinimumReplayNeeds` 至少 Housing；凡消耗个人背包物品再含 Inventory，点钱包变更含 MarriageLovePoints。主 command 必须 `IRequestSettlementCommand`（且一个网络请求只有一个主 settlement）。
7. 直接取走物品的代码要调用 `IRequestReplayProjectionDomainService.RecordInventorySlots` 留下 touched slots；Effect 框架消费会自动记录。普通库存快照不会删除已空旧格，少 footprint 会遗留客户端残影。
8. 运行时 operation ledger 只覆盖本选中 session 的重发，不替代 durable harvest-round unique key、daily claim unique key。重启之后邮件唯一性还要靠 DB。

## 5. 编辑租约：清理必须 exact-generation

建议纯 `HousingEditLease` 以 relation_id 为 key，值为完整 `SelectedSessionIdentity`，使用一个同步锁或 `ConcurrentDictionary.TryUpdate/TryRemove(KeyValuePair)` 实现比较释放。

- 同 holder 重复 open 为幂等成功；同夫妻另一会话返回 native busy；读取/清理时允许排除已不是 current 的 holder。
- lease 不只存 CharacterId，否则旧连接断线清理会释放新连接刚打开的仓库。
- close CMD `03FD` 只 compare-release actor；错误格式不能释放其他持有人。
- **掉线/返选角/切角色统一接缝**：`Application/Sessions/SelectCharacterLifecycleServices.cs` 的 `SelectedSessionCleanupService.CleanupAsync`，与 `Run(() => expertStores.CloseExact(session))` 并列新增 `CloseExact(session)`。这段不在 `ownsSelectedCharacter` 条件里面，旧 session 也可安全尝试 exact cleanup。
- 覆盖调用链：`Protocol/Game/OnlineUserConnectionObserver.CleanupSelectedHeldAsync:145`，`ReturnToSelectionCommand:68`，`PrepareSelectedCharacterSessionCommandHandler` 对同连接切角色和其他连接替换的 cleanup。无需各处重复释放。
- **离开房间**：`TownProjectionSlice.SetUserArea` 成功 `townAreaProjection.TransferAsync` 之后 compare-release（不能在尚未通过 CanCross/CanLeave/持久化之前释放，失败移动应保留编辑态）。房间→本房间同 scene 的位置更新不释放。
- 其他 teleport/传送效果也可能 `TransferExact`，最可靠是抽一个 exact-scene-departure hook 在 `TownAreaProjectionDelivery` 成功转移接缝统一调用；不要仅补 native 门出口一条路径。
- 新实例 key 已进入 `TownAreaKey` 时，原地区和目的地区比较要包含 Instance。两对夫妻同 101/0 也是真实跨 scene。
- 若租约 open 与 DB 事务绑定，需要 transaction participant 的 rollback 撤销或仅在已确定成功后发布内存租约。`IDomainTransaction.RegisterAfterCommit` 是 best-effort 后提交回调，不宜拿它完成“失败可能不加锁”的核心互斥建立；业务成功不能早于互斥生效。

## 6. 关键回归（建议）

1. 同角色重复读 effective snapshot/重登/换宠，房间 value 与属性完全相同；置入→取回只改变一次；未摆放仓库家具无加成。
2. 同一 normal/PvP 环境连续来回不积累、不永久清空资产；客户端面板与 native ring badge 都需实机验收。
3. 只发 peer USERINFO 不改变自己的夫妻家具；两对夫妇同频道 101/0 互不可见。
4. 一天重复选角只 +1；跨 daily period +1；同日重婚不能再 +1；999 上限不溢出。
5. 收获双线程、请求重发、重启重试均只一对邮件，不能重抽。
6. A 持锁→断线→新 A 会话持锁→旧 A cleanup，不释放新 A；正常离房释放；被拒绝的离房不释放；关闭窗口重复 close 不影响对方。
