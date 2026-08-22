---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 角色本体按概念重切

## Delivered

- 角色本体从「按选人协议包分段切」变成「按概念切」：`characters`（身份、位置、外观、进度）、`character_stats`（由等级与装备算出的数值）、`character_condition`（这个角色身上发生过什么、正处在什么当中）、`character_pvp`（决斗场的一行状态）、`character_settings`（玩家选的）、`character_expert_job`（副职业）。
- 五张只因为「每角色至多一行」而存在的表消失，其列迁入上述六张；`character_expert_job` 保留，因为它是一个概念而不只是一个窄表。
- 每日挑战的组与领取合为一行；`character_invisible_falgs` 更名。两处均为用户单独授权。
- 金币上限迁入派生数值，物品侧那一处读取随之改指。
- 六张表在建角色时各得一行，八个非零默认值逐一就位。
- 领域记录一字未改：由 `CharacterEntryAssembly` 里两个私有组合函数从六张表拼回。
- 库形状 53 → 49 张表。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` — `CharacterBodyShapeTests` 六条 Theory，列集与裁定账本机械比对 |
| SC-2 | 同上 — 五张折入表不存在，`character_expert_job` 存在 |
| SC-3 | 同上 — 级联外键与六个主键的断言；评审逐条追了九张源表的全部约束 |
| SC-4 | 同上 — `CharacterBodyCreationTests` 的六条行断言与八条默认值断言 |
| SC-5 | 同上 — 全量 1061/2/1（唯一红为已知假红，隔离 4/4）；三份表征用例期望值零改动 |
| SC-6 | 同上 — 两次可证伪性演示，均先红后绿 |
| SC-7 | 同上 — 一表一类零违反；领域记录 diff 为空 |

## Repository Result

- Changed paths: 31 个已跟踪文件加三个新文件（两份 `CharacterBodyTables.cs`、`CharacterBodyShapeTests.cs`、`CharacterBodyCreationTests.cs`），以及本 attempt 的过程工件与裁定的两处追加。
- Baseline or accepted result: `9c535a9715ebd142fd197026c80d711a3036abcc`（本 Spec 未提交，交由父控制器接受）
- Parent rollback required: no

## Successor Guidance

1. **物品侧与技能侧现在可以开工，彼此之间任意次序。** 裁定给的两处跨块耦合都已由本成果消化：金币上限已在 `character_stats`，`PvpSkillProfileService` 已改为读 `character_pvp` 的标记。
2. **将来写入「领取每日挑战」的代码必须写 `claimed_at`。** 合并后那一列就是领取本身，不再是旁注。已记入裁定 M-5。
3. **裁定的计数是历史。** 它描述的是改动前的 53 张表；每落一块，库形状就离它的起点更远、离它的目标形状更近。裁定开头已加时间锚点。
4. **一条方法上的教训**：本成果唯一的真缺陷（选项块的「没有」被读成「空的」，每次选人少四字节）出现在**唯一没有字节级证据覆盖的那条路上**——建角色未指名的列。表征用例守不住它，因为它们播种自己要读的东西。后续两块若有同类「创建时不写、读时有默认」的列，应照 `CharacterBodyCreationTests` 的样子先立守卫。
5. 七条不阻塞的观察记录在 `reviews/change-review.md` 的 B 级清单里，含两处过时注释与一处手搭夹具缺约束。

## Artifacts

- `Server/src/Dfo.Server.Abstractions/Persistence/CharacterBodyTables.cs`、`Server/src/Dfo.Server.Persistence/Characters/CharacterBodyTables.cs` — 六张目标表的行类型、接口与读写
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/CharacterBodyShapeTests.cs` — 形状与约束守卫
- `Server/tests/Dfo.Server.Persistence.Tests/Characters/CharacterBodyCreationTests.cs` — 出生守卫
- `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-002-character-body-cut-by-concept/` — spec、plan、implementation-state、review、verification
