# 任务完成券 10006007 / CMD 0x0204：本仓接入点调查

范围：2026-09-23 对 `main` 当前源码的只读调查。算法口径由另一调查确定；这里不推断“当前等级主线”、前置闭包或普通任务的筛选规则。已给实帧 `19B=450000000000004D0000000000000000000000` 仅证明一次请求样本（slot 69、offset 7 为 77），不据此定整个包体或响应格式。

## 现有代码事实

| 主题 | 入口与事实 |
|---|---|
| 协议 | `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:269` 只定义普通使用物品 `0x002C`；`Server/src/Dfo.Server.Protocol/ProtocolModule.cs:496,716` 注册该请求与 decoder，未见 `0x0204` 对应切片或注册。`UseStackableSlice.cs:116-145` 的普通使用物品解码不能当成 `0x0204` 布局。 |
| 物品使用 | `UseStackableSlice.cs:223-266` 的 `UseStackableCommandHandler` 校验物品槽、请求物品码及 `ICharacter.CanUseAsync` 后调用 `IItemUseEffectDomainService.PlanAsync/ApplyAsync`；`ProtocolModule.cs:320-321` 将其注册为事务命令。`UseStackableSlice.cs:310-350` 的 handler 从连接定位选中角色并映射槽。`Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs:144-231` 读取角色当前身份/等级做可用性判断。 |
| 物品效果边界 | `Server/src/Dfo.Server.Infrastructure/Content/PvfItemUseRuleDefinitionSource.cs:57-79` 目前只读取可消耗性、冷却、最低等级、职业与可用地点，不读取 `action quest clear`；`Server/src/Dfo.Server.Application/Inventory/ItemUseEffectDomainService.cs:23-92` 当前只规划消耗 1 件及冷却。不能把普通物品使用的成功结果视为任务完成券已执行。 |
| 任务定义 | `Server/src/Dfo.Server.Abstractions/Content/QuestContent.cs:30-73,211-267` 声明 `QuestGrade`、等级区间、职业限制、成长类型、前置组、问答依赖、冲突任务等。`Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs:16-36,120-140` 从 PVF 构建。`Server/src/Dfo.Server.Application/Quests/QuestContentRuleService.cs:14-31` 是规则读取入口；领域入口 `QuestDefinitionDomainService`。 |
| 现有资格规则 | `Server/src/Dfo.Server.Application/World/WorldContentEntryRuleService.cs:36-103,178-243` 计算可接列表，过滤已接/已清、等级、职业、成长类型、前置、问答/冲突等；其目标是“可接”，并非完成券目标算法，不能直接当券的筛选器。职业映射在 `Server/src/Dfo.Server.Application/Characters/CharacterJobFamilies.cs:26-48`。 |
| 任务日志 | `Server/src/Dfo.Server.Abstractions/Quests/IQuestLog.cs:130-258` 定义 `CarriedAsync`、`ClearedAsync`、`CompleteAsync`、`FinishAsync`；`Server/src/Dfo.Server.Application/Quests/QuestLog.cs:204-250` 中 `CompleteAsync` 只处理已接任务，清已接槽并以非零 mark 写已清记录，`FinishAsync` 另要求目标完成。没有现成“未接任务批量直接标记完成”领域接口；也没有奖励为零的批量完成命令。 |
| 存储与事务 | `Server/src/Dfo.Server.Persistence/Quests/SqliteQuestDomainStore.cs:125-282` 对应 `character_active_quests` 与 `character_invisible_flags`（后者是任务完成 mark），`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:227-253` 建表。`Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:27-41` 对 Transactional 命令统一提交、异常回滚。现有 `FinishQuestCommand.cs:112-253` 是任务/背包/经验/奖励同事务的样例，但会发奖励，不能复用其业务结果。 |
| 后继进度 | `Server/src/Dfo.Server.Application/Quests/QuestProgressDomainService.cs:232-270` 在 `QuestCleared` 后更新仍在做的“清任务”目标；批量完成若需要该语义，必须核对每项或汇总后触发，不能仅写位图。 |
| 通知与刷新 | `Server/src/Dfo.Server.Application/Quests/QuestOfferRefreshDomainService.cs:45-77` 从当前身份、等级、已接/已清日志重算可接列表。`Server/src/Dfo.Server.Protocol/Game/QuestGiveUp/GiveUpQuestHandler.cs:40-59` 是已接列表与可接列表的最小发送样例；`QuestCompletion/FinishQuestHandler.cs:247-260` 是完成后通知样例。现有 `AcceptedQuestProgressMessage`、`AvailableQuestListMessage` 分别在 `StateSynchronization/SupplementalSynchronizationMessages.cs:127` 与 `StateSynchronization/StateSynchronizationMessages.cs:112`。`QuestVisibilityMessage` 同文件 :121 当前只在角色入场构包发送，若券需刷新完成位图，需确认客户端所需增量/全量消息，不能推断已有例行完成通知足够。 |

## 接入边界建议

新增 `0x0204` 独立协议切片：decoder/消息/handler/command/encoder 在 `Dfo.Server.Protocol/Game`，由 `ProtocolModule` 接线。handler 做连接选中会话及当前角色一致性检查；事务 command 在同一个边界内复查指定槽持有 10006007、当前身份/等级和规则目标，写任务日志并消耗恰好一券。领域服务负责取得规则与执行任务状态变化，协议层负责 ACK 与通知。`IQuestLog.CompleteAsync` 可用于已接任务，但未接前置任务没有现成高层批量标记入口；避免在协议 handler 直接写 `character_invisible_flags`。事务成功后从真源重读任务状态，发送客户端已确认需要的完成状态、已接任务列表和可接列表通知；具体消息/顺序待 0x0204 响应及客户端效果证据确认。

## 应覆盖的测试

1. 协议样本解包/长度边界与 ACK 编码（待响应实帧）；保证 `0x0204` 不误入 `0x002C`。
2. 成功路径：目标含已接与未接前置任务，已接槽删除、每个完成 mark 有效、消耗一券、奖励/经验/金币均不变；任务清除依赖的在做进度同步。
3. 拒绝路径：无会话、切换角色、等级/职业不符、槽位错或数量不足、无适格任务、重复使用；每种拒绝均保持券和任务状态不变。
4. 同事务失败注入及并发重复请求：消耗和任务位图一起回滚/只成功一次；核对账号/角色归属。
5. 成功后通知含任务完成状态、已接列表与重算可接列表，顺序与实机抓包比对；重新登录的入场投影与即刻通知一致。

现有基线测试可参考 `Server/tests/Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs`、`Quests/QuestLogTests.cs`、`Quests/QuestProgressDomainServiceTests.cs` 与 `Quests/SeekingQuestCompletionTests.cs`。此文档未运行测试，也未修改生产代码。
