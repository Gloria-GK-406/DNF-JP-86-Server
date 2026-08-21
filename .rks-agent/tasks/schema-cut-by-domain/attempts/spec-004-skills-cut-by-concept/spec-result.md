---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 技能按概念重切

## Delivered

- `character_skills` 加一个 `mode` 列吸收 `character_pvp_skills`。两组页是同一张表的两组行，从不共用一行，也从不互相解释：每一列在两个 mode 下含义完全相同。
- 只在 PvP 一侧声明的四条保证逐条存续，按页的那两条用 `mode` 限定：`CHECK (mode <> 'pvp' OR page_index BETWEEN 0 AND 1)`、`UNIQUE (character_id, mode, page_index, skill_id)`、六列 `NOT NULL`、级联外键。
- 三条保证因此也开始约束普通行。三处收紧逐一证明无害，其中 `UNIQUE` 那一条依赖档案里的一个事实，已钉成一条常驻断言。
- 两张职业专属页表按红线保持分开，「非本职业持有即拒绝」第一次有了用例。
- 库形状 44 → **43 张表**，即裁定给出的目标形状。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` — `SkillCarrierShapeTests` 七列与旧表消失；43 张表 |
| SC-2 | 同上 — 两条读取各自只答自己那组 |
| SC-3 | 同上 — 五条约束逐条断言，六列 `NOT NULL`，行为层十余条拒绝与接受 |
| SC-4 | 同上 — 三处收紧逐一证明；`UNIQUE` 的前置条件由真实档案断言守住 |
| SC-5 | 同上 — 两张表仍在；`AJobSpecificPageHeldByAnotherJobIsRefused` 两例 |
| SC-6 | 同上 — `SeedingTheDuelPagesWritesThemOnceOnTheDuelSide` |
| SC-7 | 同上 — 全量 1132/2/0；表征期望值一字未改 |
| SC-8 | 同上 — 五次可证伪性演示；第二轮评审独立重做七处，七处全红 |
| SC-9 | 同上 — 68 个类零违反；领域记录 diff 为空 |

## Repository Result

- Changed paths: 13 个已跟踪文件加两个新测试文件（`SkillCarrierShapeTests.cs`、`SkillCarrierTests.cs`），以及本 attempt 的过程工件与裁定的一处追加。
- Baseline or accepted result: `1a77ef8a1de23ebb0d6ba283f44ca178b2496953`（本 Spec 未提交，交由父控制器接受）
- Parent rollback required: no

## Successor Guidance

1. **库形状到达目标：43 张表。** 根契约的四块（物品域、角色本体、技能域、账号侧）全部落地——账号侧本就没有自己的工作，裁定的结论是「三块，不是四块」。根控制器现在应当能把 SC-5 判为满足并进入根层终验。
2. **`character_skills` 不在可补列清单里。** 旧形状的库不会被补成新形状，会被替换。若将来要恢复补列能力，`mode` 需要一个默认值，而给它默认值等于替一行猜它属于哪一组页——那不是补列该做的事。
3. **`UNIQUE` 现在依赖档案。** 若某个职业将来在 `[initial value]` 里声明了两次同一个主动技能，建那个职业的角色会在第二行失败。守卫是 `PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice`，它是 `[SkippableFact]`——没有档案时会跳过，这一点评审记为 B 级。
4. **级联外键在运行时不生效**（全仓无 `PRAGMA foreign_keys=ON`）。`character_skills` 与 `character_pvp` 的 `ON DELETE CASCADE` 今天都只是声明。既有状况，今天也没有硬删角色的路径；谁要加删角色，先想清楚这个。
5. **一条方法上的教训，这一块最尖锐。** 合并把「哪一组页」从**表名**降级成语句里的**字符串字面量**。表名写错编译不过，字面量写错什么都不会发生。我给读的那一半立了守卫，给写的那一半没有——而写错的后果只在实机显形。**凡是把一个由类型或表名承担的区分改成由值承担的地方，写入侧和读取侧都要各自有一条只有它能打红的用例。**
6. 两轮评审共十一条 B 级观察记录在 `reviews/change-review.md`，其中两条已接受并改。

## Artifacts

- `Server/src/Dfo.Server.Persistence/Characters/CharacterSkillTables.cs` — 一张表一个类，七个方法各自指名 mode
- `Server/src/Dfo.Server.Abstractions/Persistence/CharacterTables.cs` — 一个接口、一个行类型
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/SkillCarrierShapeTests.cs` — 形状与四条保证
- `Server/tests/Dfo.Server.Persistence.Tests/Characters/SkillCarrierTests.cs` — 写入侧与读取侧的 mode 守卫、职业专属页拒绝
- `Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfInitialSkillDefinitionSourceTests.cs` — `UNIQUE` 所依赖的档案事实
- `Docs/Refactor/TableOwnershipRuling.md` — 三条收紧与它们所依赖的事实
