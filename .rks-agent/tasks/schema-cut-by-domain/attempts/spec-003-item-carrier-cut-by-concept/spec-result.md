---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 物品承载按概念重切

## Delivered

- 一件物品由 `items` 一处承载，谁拿着它是这一行的列而不是它在哪张表：角色背包、账号仓库、称号册、以及时装才有的那几件事，四张表并成一张 16 列的表。
- 称号册的 `category` 与容器类型之间有了明确的双向变换（存为 `19 + category`，读回减 19），角色背包的读取与清扫都排除那五个容器类型。
- 租借由两张列语义相同的表并成 `character_rentals`，键 `(character_id, item_id)` 第一次被声明出来。
- `character_item_values` 按 `list_kind` 拆成 `character_item_cooldowns` 与 `character_item_effects`；库里最清晰的一处「用一列重新解释另一列」消失。
- 上一代的 `character_items` 与 `account_cargo_items` 退役，连同只依赖它们的两处迁移守卫。
- 裁定交给物品侧回答的五处悬置事项各有结论并回写裁定，其中 D-15 与初判相反。
- 领域记录一字未改。库形状 49 → 44 张表。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` — `ItemCarrierShapeTests` 27 条，列集与裁定的逐表列表机械比对，九张源表不存在 |
| SC-2 | 同上 — `TitleBookCarrierTests` 8 条，第二轮评审逐条实测可证伪 |
| SC-3 | 同上 — 形状守卫加租借表征期望值未改 |
| SC-4 | 同上 — 两张拆分表都不含 `list_kind`，两个领域列表内容与顺序不变 |
| SC-5 | 同上 — 两张上一代表与两处迁移守卫不存在 |
| SC-6 | 同上 — 九张源表约束逐条落点；D-15、D-16 各以部分唯一索引恢复并说明强度差异 |
| SC-7 | 同上 — `AvatarCarrierRulesTests` 5 条，其中两条此前全仓无人守 |
| SC-8 | 同上 — 全量 1100 通过 / 2 跳过 / 0 失败；三份表征期望值只改了两个不上线的代理键 |
| SC-9 | 同上 — 四次可证伪性演示，均先红后绿；评审另做五次独立实测 |
| SC-10 | 同上 — 69 个类零违反；领域记录 diff 为空 |

## Repository Result

- Changed paths: 24 个已跟踪文件加四个新文件（`ItemCarrierShapeTests.cs`、`AvatarCarrierRulesTests.cs`、`TitleBookCarrierTests.cs`，以及本 attempt 的过程工件），另有裁定的多处改动。
- Baseline or accepted result: `5aba3a9dcea2b302797eb33097ae923a7a824a45`（本 Spec 未提交，交由父控制器接受）
- Parent rollback required: no

## Successor Guidance

1. **技能侧现在可以开工。** 它与物品侧不相交，两处跨块耦合早已由 spec-002 消化。落地后表数 44 → 43，即裁定给的目标形状。
2. **称号册今天只有读路径，正变换没有实现者。** `list_type = 19 + category` 这一半目前只由夹具写。谁来建写称号册的路径，必须用同一个变换，并把「存为 19..23」那条断言从夹具自证改成对生产代码的守卫。第二轮评审记为 R2-1。
3. **`items` 的写入路径要留意称号册。** 清扫用的是补集（`NOT BETWEEN 19 AND 23`）而不是白名单；将来若加入第六种背包容器，补集会自动把它包含进来，这多半是对的——但若加入的是第六种**非背包**容器，补集就错了。评审记为 R2-3。
4. **D-15 的强度差异是有意接受的。** 恢复后的唯一索引在「同一个角色持有两行携带同一 UID」这半边比今天更严。裁定已写明理由。若将来真有需要两行共享一个时装 UID 的功能，这条要重新裁定。
5. **一条方法上的教训，与上一块同源。** 本成果唯一的 A 级缺陷（一次保存删光称号册）出现在**计划点名要写、而我漏写了的那条断言**所覆盖的位置上。计划把它列为 T-002 第 4 步，我做了第 1、2、3、5 步。教训不是「要更小心」，而是**计划里逐条列出的步骤，完成时要逐条回读一遍**——漏做一步不会有任何信号。
6. 第一轮九条、第二轮三条 B 级观察全部记录在 `reviews/change-review.md`。

## Artifacts

- `Server/src/Dfo.Server.Persistence/Inventory/InventoryItemTables.cs`、`InventoryStateTables.cs`、`SqliteItemStore.cs` — 四张目标表的读写
- `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` — 行类型与接口
- `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs` — 形状与约束守卫
- `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarCarrierRulesTests.cs`、`TitleBookCarrierTests.cs` — 只活在代码里的规则的守卫
- `Docs/Refactor/TableOwnershipRuling.md` — 五处悬置事项的结论与两条合并后果
- `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-003-item-carrier-cut-by-concept/` — spec、plan、implementation-state、两轮评审、verification
