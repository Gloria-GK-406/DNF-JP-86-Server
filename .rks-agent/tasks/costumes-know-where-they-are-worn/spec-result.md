---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 时装知道自己穿在哪一格

## Delivered

`PvfEquipmentSlots.ByName` 里一个以 `avatar]` 结尾的键都没有，所以档案说不出时装穿在哪一格，
`WornSlotForAsync` 返回 null，`PutOnAsync` 拒为 `NotWearable`。**11 个键补上了，时装能穿上。**

| 槽 | `[equipment type]` | 槽 | `[equipment type]` |
|---:|---|---:|---|
| 0 | `[hat avatar]` | 6 | `[breast avatar]` |
| 1 | `[hair avatar]` | 7 | `[waist avatar]` |
| 2 | `[face avatar]` | 8 | `[skin avatar]` |
| 3 | `[coat avatar]` | 9 | `[aurora avatar]` |
| 4 | `[pants avatar]` | 10 | `[weapon avatar]` |
| 5 | `[shoes avatar]` | | |

**生产改动只有两处**：这个字典加 11 行；`MoveItemSpaceSlice.cs` **只改注释**
（剥掉注释后与基线逐行 diff 无输出，独立验证方核过两次）。

判据新增 23 条，修改既有 3 个文件 5 条用例；记载更正 12 处。

## Verification

`./verification/completion-evidence.md`（`decision: pass`）。
SC-1..SC-8 的逐条证据与三次独立变异都在那里。

评审：`./reviews/change-review.md`（四轮独立评审，终判 `verdict: approved`）。

## Repository Result

- Changed paths：已跟踪 10 个（`Server/src` 2、`Server/tests` 3、`AGENTS.md`、`Docs/` 1、`Document/` 1、`protocol/` 2），
  未跟踪 1 个新判据文件加 `.rks-agent/tasks/costumes-know-where-they-are-worn/` 下的流程产物
- Baseline or accepted result：`80bdb1445a1e29d1b3bf871351391f4c79cc1380`
- Parent rollback required：no

## Successor Guidance

### 下一步就是实机测，要看的是这个

**本件之后，两条从未通过真时装的路第一次通电**：

- **NOTI `0x0002` 子类型 1** 的穿戴条目（玩家自己的穿戴，每次进游戏都发）；
- **ITEM_LIST 装备栏容器**里的时装条目。

**两者都会写外观块**——`u32(18) + 18 字节珠宝槽 + u32(4) + 两个 u16 颜色`。
那段编码在 `ec2ef1b6` 与更早的成果里写好并有判据（含把珠宝槽从 30 改成 18 那次），
**但从来没有真时装走过它们**。所有者已说好「做完以后我们再一起测试」，这就是要测的地方。

**本件不保证客户端把时装画得出来**，只保证服务器接受穿戴请求并落库。

### 三源互证，但其中一源在仓内查不到

| 源 | 状态 |
|---|---|
| 档案的 `[equipment type]` | 可复现（验证方逐件读过全部 11 件） |
| 上游 `EquipmentType` 枚举 | 可复现 |
| **实机量得的槽号** | **仓内只有上衣那一件留痕**，另外 10 行日志没有归档 |

**这不影响结论**——抽掉实机那一源，另两源仍独立支撑全部 11 个数。
**但要复核「实机」那一列的人，仓内没有东西可查。**

### 槽 11 是空的，且这是档案的事实不是协议的事实

上游枚举有 `AuraSkinAvatar = 11`，而本档案**没有任何模板声明它**：
全档案 118479 条 equipment 索引，以 `avatar]` 结尾的类型恰好 11 种、66328 个模板，无第 12 种。
**换一版档案要重扫。**

### `PutOnAsync` 的那个点名已经做掉了

上一件留话要求「给装扮槽补上名字的那个人」回来决定「被顶下来那件能否回到源格」。
**结论：不需要加判定**，理由写在注释里，且不再是空的——
两端都是时装（只有那 11 个名字能到 `0..10`），被顶下来那件回到的是到达件刚才站的那一格。
覆盖范围写明只覆盖**移动能到达的状态**；并明写**不得推广**成「穿戴槽决定类别」
（`[earring]`/`[support weapon]`/`[creature]` 同映槽 24 是现成反例，那是 issue #13）。

结论有判据钉住，验证方另做变异证明：**加上那条判定，全仓恰好红 1 条**，就是钉它的那条。

### 一条经过与结论

产品代码**四轮评审 + 一轮验证全部零缺陷**，改动只有「字典加 11 行 + 改注释」。
**五条阻塞里有四条是同一句陈旧说法的四个副本**——
「表里没有 `avatar]` 键 / 档案不给时装槽号」，分别藏在：生产注释、判据方法 `<summary>`、
类级 `<remarks>` 的两段。**每一轮只改了当时被点名的那一处，没往上下翻**，分三轮才改完。

**而它们全都在本轮声明并跑过的那条 `grep` 的命中里。** 两条教训已写进变化清单：

1. **写不出命中数就等于没扫。** 上一件栽的是「声明了没执行」，本件栽的是「执行了没读」。
2. **扫说法不够，还要扫数字与标题。** 按主题词 `grep` 命中不到「16 个键」这种**数字**，
   也命中不到写在**小节标题**里的断言——上一轮加的作废注写「下面这一整节」，覆盖不到标题本身。

## Artifacts

- `./behavior-change-list.md`（正向 3 条、反向 3 条、非行为改动 5 条、被推翻的说法 12 处，
  含搜索集与它两次失败的形态）
- `./spec.md`（RQ-001..RQ-009 / AC-001..AC-009 / EV-001..EV-008 / AS-001..AS-004 / NA-001..NA-002）
