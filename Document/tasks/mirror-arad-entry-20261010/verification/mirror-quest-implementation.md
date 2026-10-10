# 镜像本局任务实现交接

## 实现边界

- `IMirrorAradQuestDomainService` 管理当前镜像 RunId 的接取、进度、交付和放弃。状态存入独立 `character_mirror_quests`，普通 active/cleared 记录不写入镜像任务。
- 查询只合并当前运行实例对应的镜像任务。普通任务的 Accept/GiveUp/Finish/Report、完成券和 Replay 投影保留此合并，避免其他刷新覆盖镜像 UI。
- 五类目标包括击杀、通关/不复活通关、指定 MAP 清理、限时通关、清理房间数。客户端 trigger 不能直接推进任务；击杀按 RunId/Cell/Actor 去重，房间按 Cell 去重。限时从 AcceptedAt 到权威 ClearedAt；房间总数使用清理集合，不能以 VisitedCells 代替。
- Finish 从当前 ClearOpen/ClearedAt 直接判定，不依赖延后经验结算事实。奖励、领取标记、每日次数和事件共享 SQLite 事务；重复领奖不再次发奖。GiveUp 保留本局 tombstone，不能清掉已领奖标记后再接取领奖。
- PVF `crackofdimensionlist.etc` 的等级奖励、每日通关奖励上限和阈值奖励纳入共用 Effect 计划。每日入场次数在 UTC+8 日期账本中记录，团队先整体检查再保留次数。
- 离场 Reliable 订阅按 CharacterId + RunId 删除。旧离场事件不会删新局；异步清理不是当前任务授权条件。MirrorQuestChanged 由 LiveOnly 订阅投影，通知消费时再读当前快照。

## 接线

- `ApplicationModule.ConfigureServices` 在 `AddEventDispatching()` 后调用 `AddMirrorAradQuests()`。
- 扩展文件：`Application/Quests/MirrorAradQuestRegistration.cs`。注册 IMirrorAradQuestRuleService、IMirrorAradQuestDomainService、IMirrorDungeonQuotaDomainService；注册 MirrorQuestChanged 事件、DungeonParticipantLeft Reliable 订阅、MirrorQuestChanged LiveOnly 订阅。
- `PersistenceModule.ConfigureServices` 注册 `IMirrorAradQuestDomainStore -> SqliteMirrorAradQuestDomainStore`；`CharacterDatabaseSchema` 添加两张独立表。
- `ProtocolModule.ConfigureServices` 注册 `MirrorQuestViewChangedHandler`。
- QuestDefinition/PVF reader 增加 ObjectiveSubType 和 ObjectiveArguments，复用已有 QuestContentRuleService，不重复开档。
- DungeonRunState 增加 ClearedCells；DungeonRunDomainService.ViewFor 从实际 Cell actors 的 BlocksClear/IsDead 派生，不是访问集合。

## 验证

2026-10-10 定向运行：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~MirrorAradQuestTests|FullyQualifiedName~QuestProgressDomainServiceTests|FullyQualifiedName~MirrorDungeonEntryTests" --logger "trx;LogFileName=mirror-quest.trx" --results-directory Document/tasks/mirror-arad-entry-20261010/verification --verbosity minimal
```

结果 41 通过，0 失败，0 跳过。见 `mirror-quest.trx`、`mirror-quest-tests.log`。

其中新增 MirrorAradQuestTests 7 项：五类目标、重复接取/领奖、当前 ClearOpen 直接领奖、奖励拒绝和跨表事务回滚、旧离场/新局隔离、30 普通任务合并、团队次数原子拒绝及 UTC+8 跨日、PVF 等级奖励/每日奖励、真实 A21 catalog 全候选目标解析。SQLite schema 经 LearningFixture 实际建库、读写和回滚；奖励 Effect 测试用同 SQLite 事务的写入 double，未实机领取真实 PVF 奖励。

尚未运行完整 solution 回归、真实 Event dispatcher 的离场消费集成测试，未部署、未做 ClientVerified。

## 根代理收尾接缝

- 02B6/0278 协议向量和专用拒绝码；共用入口路由。
- 所有实际死亡、清房（含初始空房/dummy）、复活与离场生产端；ClearedAt 使用统一 TimeProvider。
- 真实 dispatcher Reliable 清理事务和最终通知的集成回归。
- 最终协议/客户端验收：普通任务已满 30 条时追加镜像任务的客户端容量、奖励入包和刷新、本局退出清理。服务端 NOTI 023F 使用动态数量；不抢占普通任务 slot。
- ClearMap 明确按 MAP 事件匹配；不沿用上游将同一个目标数值同时解释成历史 DGN 的兜底。若未来 PVF 新声明以 DGN 为目标，应新增显式目标类型。
- 历史地下城的怪物角色等级缩放/revision table 保持当前行为，作为独立残余限制。
