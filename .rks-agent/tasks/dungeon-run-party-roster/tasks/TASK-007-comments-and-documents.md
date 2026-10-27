# TASK-007 — 副本成员模型的注释与文档

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付 Plan T-005，覆盖 Spec RQ-011、AC-013。接口注释与仓库文档描述的副本成员模型与新结构一致，不再称成员“冻结”。
- Owned boundary:
  - `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`、`IMonsterDropDomainService.cs`，只改注释；
  - `Document/ReactiveDungeonCellRuntime.md`、`Document/StoryPartyDungeon-20260914.md`。
- Required checks:
  - 构建无错误、无警告（XML 注释引用要能解析）；
  - 生产源码中不再出现 `frozen participant`；
  - 文档中只保留一个规则归属，其余位置引用它。
- Dependencies: TASK-006
- Result review: not-required — 只改注释与文档，不影响编译产物的行为。错误表述由最终的整体审查对照 Spec 检查；构建能暴露失效的注释引用。

## Detailed Plan

1. 改写 `IDungeonRunDomainService` 的类注释：副本归属一支队伍、成员由队伍回答、经队伍找副本、离开须先退队、运行中队伍不增员。同时更新 `End(CharacterId)` 的注释，以及 `TryClaimParticipantKillEffect` 注释里“frozen participant”的措辞。
2. `MonsterDropContext` 注释改为“生成时的副本成员”。
3. 在 `Document/ReactiveDungeonCellRuntime.md` 新增“副本成员”一节，作为规则的唯一归属，并更新职责表中的描述。`StoryPartyDungeon-20260914.md` 中过时的“副本领域态保存槽位和队长标记”一句改为引用该节。

## Result

- Outcome: done
- Round: 1；owned change：上述 4 个文件，只改注释与文档。
- Changed:
  - `IDungeonRunDomainService`：类注释改为“副本归属一支队伍（真实或虚拟），成员是队伍当前成员中属于入场名单的角色，槽位与队长取队伍值；副本除入场名单与各入场者本局状态外不持有名单；经所属队伍找副本；离开须先退队；运行中队伍不增员”。`End(CharacterId)` 注释说明它是不含生命周期与失败虚弱的同步版 `EndAsync`。`TryClaimParticipantKillEffect` 的措辞改为“entrant”。
  - `MonsterDropContext`：改为“生成时的副本成员，而非城镇队伍”。
  - `ReactiveDungeonCellRuntime.md`：目标段与职责表改为“所属队伍键与入场名单”。新增“副本成员”一节，写明成员定义、角色找副本、个人离开、整局结束、各路径的逐人副作用、运行中队伍不增员，以及断线时队伍清理失败的残余边界（TASK-005 审查 R1）。
  - `StoryPartyDungeon-20260914.md`：过时描述改为引用“副本成员”一节。
- Checks:
  - `dotnet build Server/86JP.Server.slnx`：0 错误、0 警告。
  - `grep -rn -i 'frozen participant' Server/src`：无结果。
  - 副本成员规则只在 `ReactiveDungeonCellRuntime.md` 维护，`StoryPartyDungeon` 以链接引用。
