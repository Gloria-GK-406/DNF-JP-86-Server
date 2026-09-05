---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 物品有自己的身份，槽只说它在哪

## Delivered

- **`items` 从「角色的物品表」变成世界表**：身份（`item_uid`）＋ core ＋ 类型专属列
  （时装六列、契约兽八列），**位置四列退役**。`item_uid` 跨任何位置变动与任何写入都不变。
- **新增 `item_slots`**：`(owner_scope, owner_id, list_type, slot_index)` 说「哪个容器的哪一格」，
  `(kind, id, count, core_uid)` 说「放着什么、几个、那 82 字节住在世界表哪一行」。
  `core_uid` 一个列解掉两笔债：孤儿查询不再需要位置列挑出堆叠物，
  上一版为躲唯一键冲突而设的「抬起到哨兵槽再落下」整套机制摘掉。
- **分类由档案回答，不由存储里那个种类字节回答**：唯一实现 `ItemPlacementRuleService.ClassifyAsync`
  **签名只收模板号**（`int itemId`），物理上取不到库里那个不可靠的字节。
- **两处被授权的行为变化**：契约兽取下之后能装回去（RQ-009）；契约兽的种类判定取档案的解释
  1 → 5（RQ-010）。**其余逐值不变，由 62 帧字节锚守着。**
- **旧库作废重建**，无迁移机制、无兼容读路径（所有者裁定）。

## Success Evidence

新鲜验证见 `verification/completion-evidence.md`（`decision: pass`），
其证据为验证方**自己重跑**所得，未复用执行方或评审方的断言。

| Condition | Evidence |
|---|---|
| SC-1 | `AvatarDetailFollowsTheIdentityTests`、`WorldRowSurvivesTheMoveTests`：移动/穿脱后身份值与实例状态逐字段相同 |
| SC-2 | `item_slots` 八列一种形状；评审 scope `change` 独立确认无第二种槽表达 |
| SC-3 | 拆堆两堆各自可用且不继承源堆标识；`SplitPileTests` |
| SC-4 | `ItemPlacementRuleService.ClassifyAsync` 签名只收模板号；`ItemClassificationTests` 与 `ClassificationRefusalTests` 覆盖「库里 `ItemKind` 与档案解释不一致」 |
| SC-5 | `ReadUnreferencedAsync` ＋ 其判据。**限定**：该方法今天零 DI 注册、零生产调用方，只有判据能调（契约允许的形状，验证方已写明） |
| SC-6 | `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature` |
| SC-7 | `CreatureKindAgreesWithTheArchiveTests`（注意其中的恒真陷阱，承重的是第三条断言） |
| SC-8 | 全量 **2216 / 2 / 0**，退出码 0（基线 2087/2/0）；字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 的 sha256 与 `git show HEAD:` **逐字符相同**（`1424a4a8…`），62 条整表比对全绿 |
| SC-9 | `behavior-change-list.md`：两处授权变化及其全部连带后果，加「新增的生产拒绝路径（全部 13 条）」一节，每条给位置、生产可达性、判据有无 |
| SC-10 | `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 两个文件 sha256 与 HEAD 逐字符相同；另新增 `CommandRegistrationTests.The_commands_that_reach_a_persistent_write_are_transactional`（纯加强） |
| SC-11 | 全仓 `DomainStore` 结尾类型集合与 HEAD **完全一致**（本轮零新增；世界侧走 `IWorldItemTable`／`IItemSlotTable`，命名未污染） |
| SC-12 | `AGENTS.md`「物品：有身份的东西，和它在哪，是两张表」一节，含「本轮明确没有做的」与「已知缺陷与覆盖缺口」 |

## Repository Result

- Changed paths：已跟踪 60 个文件（`+2905/−476`），未跟踪的非 `.rks-agent` 新文件 18 个
  （4 个 `Docs/*.md` ＋ 14 个新判据文件），加 `.rks-agent/tasks/items-own-their-identity/` 下 19 份流程产物。
- Baseline：`51cd99c2`（接受提交由工作区控制器做，本文件不记它自己所在的那个提交哈希）。
- Parent rollback required：no。

## Successor Guidance

后来者必须带上的事实，**每条都是这一轮踩出来的**：

1. **契约兽八列在世界行被重建时不会被抢救，而时装六列会。** `FillAsync` 的 `INSERT` 完全不写
   `creature_*` 八列。今天到不了生产（没有通路创建契约兽世界行），
   **下一个成果就是契约兽领域，一开工就会踩上**。已写进 `CarriedAvatarsAsync` 的 `<remarks>`。
2. **时装的 8／1 不一致仍在，且它让一道守卫与一个写入方互相矛盾**：
   `AddAsync` 对时装写 kind 1，而 `SaveAsync` 的守卫拒绝 kind≠8 的时装。
   今天不可达——五条发放通路对真实档案穷尽后时装 0 个，决定性的一条是
   **66328 个时装模板里声明 `[creation rate]` 的是 0 个**。**修了 8／1 这条矛盾自然消失。**
3. **堆叠物的 core 不可约到「模板号 + 数量」**（`Docs/ItemWorldModel.md` 更正 C-1）。
   所以「无身份的东西不在世界表留行」做不到，且不是工作量问题。
4. **`SqliteWorldItemTable` 有一个有意留下的守卫缺口**（生产零调用方、零 DI 注册）。
   谁第一次给它接线，谁要补那道时装唯一性守卫。
5. **写「行为变化清单」时两个方向都要扫。** 本轮的清单最初漏掉了 13 条新增拒绝，
   原因是核法只从授权变化的**起因**往下游追、加一个全量判据——
   而全量判据全绿**恰恰**意味着没有用例走到那些新拒绝上。
6. **一条与本成果无关、未修的仓库隐患**：`.gitignore:26` 的 `*.pvf` 匹配目录
   `Server/src/Dfo.Pvf`（本仓 `core.ignorecase = true`，已实测坐实）。
   **今后在该项目里新建的文件对 `git add` 静默不可见。** 需所有者裁定，应另开任务。

## Artifacts

- `TaskContract.md`（revision 2）、`routing-decision.md`、`sdd-execution.yaml`
- `spec.md`、`plan.md`、`behavior-change-list.md`
- `tasks/TASK-01..13-*.md`（十三个 Task Unit，共 21 轮独立快审）
- `reviews/change-review.md`（`approved`，含复评与控制器处置）
- `verification/completion-evidence.md`（`pass`）
- `Docs/ItemWorldModel.md`（含更正 C-1、C-2）、`Docs/CreatureDomainDesign.md`、
  `Docs/CharacterDomainCut.md`、`Docs/CharacterEntryFacesInventory.md`
