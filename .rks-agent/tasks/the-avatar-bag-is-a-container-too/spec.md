---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 装扮栏也是一个容器

## Why and Outcome

玩家拖动装扮栏里的时装，服务器答：

```
Move item space Game:01-00-00-00 character=1: item 0 0->3 refused as ListNotServed
```

`{ItemId}` 是 **0**，因为拒绝发生在读到物品之前。根因不在移动逻辑，而在
**装扮栏这个容器从来没有被绑定**：入场时只绑了主背包与装备栏
（`SelectCharacterEntryInventoryDomainService.cs:74`），处理器因此只服务三种列表对，
凡沾装扮栏的一律在 `MoveItemSpaceSlice.cs:280` 被拒。

要交付的结果是：**装扮栏和别的容器一样被对待**——它被绑定，进出它的移动被服务，
拒绝发生在读到物品之后因而说得出是哪一件。

**本件不让时装穿得上。** 槽位映射是 issue #11。本件之后把时装拖到装扮槽仍会被拒，
但拒绝理由从「这个列表我不服务」变成「我读到了这一件，但不知道它该进哪个槽」，
且日志带得出 `{ItemId}`。**这个区别就是本件的成果**，AC-005 把它写成必须钉住的条款。

## Scope

**做**：绑定装扮栏；服务装扮栏↔装扮栏、↔主背包、↔装备栏；给装扮栏一条自己的槽位准入规则；
判据（含反向）；变化清单。

**不做**：不碰 `PvfEquipmentSlots.ByName`（#11）；不碰 `listParam16`（#12）；
不造助战外观限制；不改协议帧；不引入装扮栏扩容概念；不改主背包 band 规则。

## Definitions

- **列表对**：一次移动请求的 `(SourceListType, DestinationListType)`。
- **本轮新服务的四类**：`Avatar→Avatar`、`Avatar↔Main`、`Avatar↔Equipment`。
- **装扮栏准入**：哪些物品可以待在装扮栏的哪些槽。上游读数是**区间 `0..209`、只收时装**。
- **仍不服务的列表对**：个人货柜(2)、宠物(7)、账号金库(12)、称号册(19..23)、快捷栏(29) 等
  作为源或目的地的一切组合。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 入场后装扮栏是一个已绑定的容器，形状取入场投影里该容器的 `Parameter`。 |
| RQ-002 | 装扮栏的槽位准入由**规则服务**回答，而不是在容器或处理器里分支。见 `RQ-002 的出处`。 |
| RQ-003 | 装扮栏内的换位被服务：落空格、与另一件交换，都真的落库。 |
| RQ-004 | 装扮栏 ↔ 主背包双向被服务，两端各按自己那一侧的准入规则判。 |
| RQ-005 | 装扮栏 ↔ 装备栏双向被服务，即请求走得到 `PutOnAsync` / `TakeOffAsync`。 |
| RQ-006 | 仍不服务的列表对继续被拒为 `ListNotServed`。**放行面不得超出 RQ-003..RQ-005。** |
| RQ-007 | 装扮栏准入：槽位在 `0..209` 内，且物品是时装（`ItemWorldClass.Avatar`）。区间外或非时装被拒。 |
| RQ-008 | 除上述被授权的行为变化外，移动的其余行为逐值不变；协议帧的字节布局不变。 |
| RQ-009 | 涉及装扮栏的移动与既有移动一样在事务边界内结算，两端一起成立或一起不成立。 |
| RQ-010 | 交回变化清单，正向与反向都报，从代码出发独立扫。 |

### RQ-002 的出处

不是设计偏好，是**代码里已经写好的扩展点**。`StoreBackedItemContainer.DecideMoveAsync`
（`ItemContainers.cs:590-593`）的注释：

> The band consulted is the main list's, which is the only list this server serves.
> **A second served list is a second band, and it belongs in the rule service beside this one**
> rather than as a branch here.

本件正是那个「第二个被服务的列表」。按它指的路走：`IItemSlotRuleService` 长出装扮栏那一问，
`DecideMoveAsync` 问「这个容器的」而不是写死主背包。

**`CanOccupyMainSlot` 现有的行为不得改变**——它今天被主背包的移动与到达路径共用，
改它会波及所有物品。

### RQ-007 的依据与一处必须现场查证的事

区间与种类**读自上游** `ItemSlotBoundService`：`GetAvatarOpenRange(0)` 恒返回 `0..209`
并忽略参数；`TryGetItemKindBySlot(Avatar, slot)` 对该列表恒答 `KindAvatar`（`:137-139`）。
**不是实机测得。**

判别用 `ItemPlacement.WorldClass == ItemWorldClass.Avatar`，不用 `ItemKind == 8`：
上一件成果（`server-agrees-what-an-avatar-is`）把「这是什么」收敛到了 `WorldClass`，
而 `ItemKind` 是存储行携带的字节，两者曾经不一致。

**必须现场查证的一件**：RQ-004 的「装扮栏→主背包」要过主背包的 band 判定
（`CanOccupyMainSlot`），而那用的是**档案给这件时装的 band**。
**如果档案不给时装任何主背包 band，这个方向将永远被拒，RQ-004 就是空的。**
实现阶段必须用真实档案查一个时装模板（例如 `413500000`）的 `SlotStart` / `SlotEnd`，
并把结果如实写进交付物：

- 若有 band → RQ-004 双向都可达，判据照做；
- 若无 band → **停下报告**（契约 Requires Confirmation 第二条），
  不得为了让 RQ-004 成立而去改主背包的 band 规则。

**已知的相邻事实，不足以回答这个问题**：本轮实机把 11 件时装用 SQL 塞进主背包，
客户端正常显示并允许从那里拖走。那证明的是**客户端**接受这种状态，
不是**本仓的准入规则**允许一次移动把它放进去。两件事不同。

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 入场之后 `FindContainer(characterId, Avatar)` 交出一个容器，其形状的 `Parameter` 等于入场投影里该容器的参数；拿掉绑定后判据会红。 |
| AC-002 | RQ-003 | 见 `AC-002 场景`。 |
| AC-003 | RQ-004 | 见 `AC-003 场景`。 |
| AC-004 | RQ-005 | 装扮栏槽 0 的一件时装被请求移到装备栏槽 3：请求走到 `PutOnAsync`，被拒为 **`NotWearable`**（不是 `ListNotServed`），且结果**带着该物品的 itemId**。反向（装备栏→装扮栏）走到 `TakeOffAsync`。 |
| AC-005 | RQ-005 | **拒绝理由变了这件事本身被钉住**：同一个请求在基线上是 `ListNotServed` 且 `ItemId == 0`，现在是 `NotWearable` 且 `ItemId != 0`。这是 issue #10 第二条接受条件的全部内容。 |
| AC-006 | RQ-006 | 见 `AC-006 清单`——**本 Spec 最重要的一条**。 |
| AC-007 | RQ-007 | 槽 `210`（区间外）被拒；一件非时装（`WorldClass != Avatar`）被移进装扮栏被拒；槽 `0` 与 `209` 的时装被接受。 |
| AC-008 | RQ-008 | 字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧逐帧不变；全量判据 ≥ 2289 通过 / 2 跳过 / 0 失败，退出码 0。 |
| AC-009 | RQ-009 | 见 `AC-009`。 |
| AC-010 | RQ-002 | 装扮栏的准入答案来自规则服务；`CanOccupyMainSlot` 的现有行为逐值不变（既有判据不得改期望值）。 |
| AC-011 | RQ-010 | 变化清单按契约 SC-8 的四层口径给出搜索集与搜法，正向与反向都报。 |

### AC-002 场景

```gherkin
Given 角色的装扮栏槽 0 有一件时装，槽 5 是空的
When 请求把槽 0 移到槽 5
Then 移动成功，槽 5 有那件时装、槽 0 空
  And 结果带着该物品的 itemId

Given 角色的装扮栏槽 0 与槽 5 各有一件不同的时装
When 请求把槽 0 移到槽 5
Then 两件交换，且都仍在装扮栏内
```

### AC-003 场景

```gherkin
Given 角色的装扮栏槽 0 有一件时装，主背包某个合法格是空的
When 请求把它从装扮栏移到主背包那一格
Then 依 RQ-007 的现场查证结果二选一：
     档案给时装 band  → 移动成功，两端各自更新
     档案不给 band    → 被拒为 ItemCannotOccupySlot，且这一结论被如实写进交付物
```

反向（主背包 → 装扮栏）：一件时装从主背包移进装扮栏空格应当成功；
一件**非**时装从主背包移进装扮栏应当被拒（这一条同时服务 AC-007）。

### AC-006 清单

**放行面不得超出授权。** 本仓吃过这个亏：一次变化清单漏了 13 条新增生产拒绝，
根因是「全量判据全绿恰恰意味着没有用例走到那些新路径上」。
本件最省事的错误写法是把处理器改成「任意容器到任意容器」——**那样所有判据都会绿**，
因为今天没有任何用例去拖个人货柜或宠物栏。

所以要求：**至少三个仍不服务的列表对各有判据**，断言它们仍被拒为 `ListNotServed`，
且这些判据在「把处理器改成任意到任意」时会红。三个至少要覆盖：

1. 一个作为**源**的未服务列表（如 个人货柜 → 主背包）；
2. 一个作为**目的地**的未服务列表（如 主背包 → 宠物栏）；
3. 一个**两端都未服务**的（如 个人货柜 → 账号金库）。

### AC-009

一次跨容器移动（装扮栏↔主背包、装扮栏↔装备栏）的两端在**同一个事务边界内**结算。
判据：让写入在两端之间失败，两端都不变——或用既有的事务判据形态（本仓已有先例，照用）。
**不得**用「两次独立写入都成功了」来充当这一条的证据。

## Success Evidence

| ID | 证明什么 | 怎么取 |
|---|---|---|
| EV-001 | AC-001 | 绑定处的 diff 与钉住它的判据名；拿掉绑定的变异结果 |
| EV-002 | AC-002 / AC-003 / AC-004 / AC-007 | 新增判据名与它们各自证明的命题一一对照 |
| EV-003 | AC-005 | 同一请求在基线与现在的拒绝码与 itemId 对照 |
| EV-004 | AC-006 | 三条反向判据的名字；**以及把处理器改成「任意到任意」时它们会红**的变异结果 |
| EV-005 | RQ-007 的现场查证 | 真实档案里那个时装模板的 `SlotStart`/`SlotEnd` 实际取值 |
| EV-006 | AC-008 | 全量判据输出；字节锚比对结论 |
| EV-007 | AC-010 | 全仓搜 `CanOccupyMainSlot` 的命中与它们是否被改动 |
| EV-008 | AC-011 | 变化清单 |

## Assumptions and Non-acceptance

| ID | |
|---|---|
| AS-001 | 装扮栏区间 `0..209`、只收时装：**上游源码读出**，非实机。 |
| AS-002 | 上游服务装扮栏↔装备栏；其 `IsAvatarEquipMutation` 拦截只对受限助战角色生效（上游三段合取条件读出）。本仓无此概念，不造。 |
| AS-003 | `SnapshotAsync` 对非主背包容器抛，但生产零调用方（本轮全仓扫描），绑定装扮栏踩不到。`GetContainers` 同样零生产调用方。 |
| AS-004 | 客户端允许时装待在主背包并从那里拖向装扮槽——**实机观测**（本轮 11 件）。**客户端是否提供「主背包↔装扮栏」的拖拽未观测**，本件按「服务它」实现，不假称观测过。 |
| NA-001 | **本件不使时装可穿。** 做完之后拖到装扮槽仍被拒为 `NotWearable`。任何交付物不得把这写成缺陷或未完成——它是 AC-004/AC-005 明确要求的状态。 |
| NA-002 | **实机不作为本件的验收手段。** 可以跑一次看拒绝码是否如预期变化，但那是佐证不是判据；`NotWearable` 与 `ListNotServed` 在客户端都表现为「拖不动、弹回去」，玩家侧**看不出区别**。 |
