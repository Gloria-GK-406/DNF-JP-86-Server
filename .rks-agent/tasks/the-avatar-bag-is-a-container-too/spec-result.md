---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 装扮栏也是一个容器

## Delivered

装扮栏（`InventoryContainerType.Avatar` = 1）从来没有被绑定为容器，所以凡涉及它的移动请求
在读到物品之前就被拒为 `ListNotServed`，日志里 `{ItemId}` 是 0。本轮把它绑上，并服务涉及它的列表对。

| | 之前 | 现在 |
|---|---|---|
| 入场绑定 | 只绑主背包与装备栏 | 三个都绑 |
| 装扮栏 → 装扮栏 | `ListNotServed` | 交给该容器的 `MoveToAsync` |
| 装扮栏 ↔ 主背包 | `ListNotServed` | 由新的 `CarryAcrossAsync` 搬运 |
| 装扮栏 → 装备栏 | `ListNotServed`（`item 0`） | 走到 `PutOnAsync`；**仍被拒**，但理由是 `NotWearable` 且**带模板号** |
| 装备栏 → 装扮栏 | `ListNotServed` | 走到 `TakeOffAsync`，落进装扮栏 |

装扮栏的槽位准入是一条**写在规则服务里**的规则（`IItemSlotRuleService.CanOccupySlot`）：
`0..209` 且 `WorldClass == Avatar`，读自上游 `ItemSlotBoundService`。
放行面**恰好**是上表四类，其余列表对继续被拒——这一点由五条反向判据钉住。

判据新增 46 条（3 个文件），修改既有 2 个文件；协议与设计文档更正 7 处、经核后不改 3 处。

## Verification

`./verification/completion-evidence.md`（`decision: pass`，第四轮）。
SC-1..SC-8 的逐条证据与四次独立变异都在那里，不在此重复。

评审：`./reviews/change-review.md`（四轮独立评审，终判 `verdict: approved`）。

## Repository Result

- Changed paths：已跟踪 16 个（`Server/src` 6、`Server/tests` 3、`AGENTS.md`、`Docs/` 3、`Document/` 1、`protocol/` 2），
  未跟踪 3 个新判据文件加 `.rks-agent/tasks/the-avatar-bag-is-a-container-too/` 下的流程产物
- Baseline or accepted result：`7147d2e96417337de65e3fd481b1a28a3d29ae8b`
- Parent rollback required：no

## Successor Guidance

### 给做 issue #11（时装槽位映射）的人

- **`AGENTS.md` 的量具说明书本轮更正过，先读它。** 要点：实机观测到的目标列是 `Equipment`(=3)，
  那条路（`PutOnAsync` → `NotWearable` → 带模板号）**可执行语句逐字符未变，量具照旧管用**；
  而目标列写成 `Avatar`(=1) 的那条路**现在是哑的**——时装会被真的搬进装扮栏，一行拒绝都没有。
- **0..11 的映射已经量全**，在契约的 Context 里：11 件骑士时装 → 11 个互不相同的槽，连续无缺口。
  上衣 `413500000 → 3` 有三次独立吻合。**槽 11 没量到**（没有对应物品），
  文档推测是 `AuraSkinAvatar`，**那是推断不是实测**。
- **`PutOnAsync` 有一处点名等你回来。** 它现在不判断「被顶下来那件能否回到源格」，
  理由是**今天装扮槽上站不了任何东西**（`PvfEquipmentSlots.ByName` 最低只到 12，
  取槽的两条通路——穿戴与建号起始装备——都只读这张表）。**这是空真，而 #11 正是要给 0..11 补键。**
  补完之后这个前提消失，必须回去重新判断。注释里写了这件事，也写明了
  **不得推广成「穿戴槽决定类别」**——`ByName` 把 `[earring]`、`[support weapon]`、`[creature]`
  同映槽 24，那是现成的反例。

### 另开 issue 的两件

- **快捷栏 / 契约兽**：耳环与契约兽同映槽 24，戴耳环会把契约兽顶进背包，而契约兽进不了快捷栏
  （`AllowsQuickSlot` 排除 kind 5）。本仓 `Docs/CreatureDomainDesign.md` 记着这条，
  真正的痛点是「**再也装不回去**」。本轮曾顺手把它挡住，又因越出授权面而回退——**不要顺手改它**。
- **`listParam16` 在装扮栏的语义**（issue #12）未清，本轮不碰。

### 一条经过与结论

本件的产品代码在**第一轮评审之后就没再被查出过缺陷**（四轮评审 + 四轮验证，
其中评审第二至四轮、验证全部三轮都判「产品代码零缺陷」）。
唯一那条真缺陷（`PutOnAsync` 少一次准入判定），**最终的正确处置是把它回退掉**——
所有者的判断是：该检查的不是能不能互换，而是**能不能装备**；能装备就意味着交换合法。

**其余七条阻塞全部出在记载，而记载的每一次修正都制造了下一处错**：
第二轮 2 处假声称 → 第三轮 3 处 → 第四轮 1 处 → 验证三轮又各抓出一处。
第三轮评审的诊断值得原样带走：

> 每修一处就写进约一处新的不实……**修法是让它变短，不是让它更准确。**

照做之后（删掉逐轮自述、只留主线）评审即判 `approved`。
本轮新增两条可复用的教训，都已写进 `behavior-change-list.md`：

1. **搜索集是声明的，不是执行的**——声明的范围要与真正敲下去的命令逐条对一遍；
   而「承认没执行」之后**只补跑一条也不算执行**。
2. **改完一张表要回头数一遍行数**——R-7 那一行随被删的小节一起消失，而表头仍说七条；
   同一节的总数前后写错三次。

## Artifacts

- `./behavior-change-list.md`（正向 5 条、反向 7 条、非行为改动 5 条、被推翻的说法 4 条，
  含搜索集、搜法与其两次失败的经过）
- `./spec.md`（RQ-001..RQ-010 / AC-001..AC-011 / EV-001..EV-008 / AS-001..AS-004 / NA-001..NA-002）
