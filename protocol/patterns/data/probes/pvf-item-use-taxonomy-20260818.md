# 探针产物：可堆叠物的「使用」分类

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-18
- 归档源：`DNFClient-debug/Script.pvf`
- 读法：`Tool/PvfProbe`，设 `PVF_ARCHIVE_PATH` 后：

```
PvfProbe values stackable/stackable.lst "[stackable type]"
PvfProbe values stackable/stackable.lst "[increase status type]"
PvfProbe where  stackable/stackable.lst "[stackable type]" "[unlimited waste]"
PvfProbe lst    stackable/stackable.lst 8474
PvfProbe name   stackable/stackable.lst "经验"
```

## 「使用」不是一条协议，是三条

客户端按物品类别自己决定发哪一帧，服务端不需要一条多态的使用管线：

| 协议 | 承载 | 备注 |
|---|---|---|
| [`0x0013 MOVE_ITEMSPACE`](../../../game/client-to-server/0x0013.md) | 穿戴装备 | Main 列表 → Equipment 列表，本质是移动 |
| [`0x002C USE_STACKABLE`](../../../game/client-to-server/0x002C.md) | 普通消耗品，**以及若干会改服务端状态的道具** | 见下方「不是纯客户端效果」 |
| [`0x001E INCREASE_STATUS`](../../../game/client-to-server/0x001E.md) | 经验类道具 | body 只有一个 i16：Main 背包槽位号 |

## `[stackable type]`：34,532 个文件，41 个取值

每个 `.stk` 都有这个标签。语法是 `<括号枚举> <整数子参数>`，子参数含义未定。

```
  9163 [recipe]              1141 [material]                34 [set]
  5598 [material expert job] 1105 [enchant waste]           29 [booster random]
  3509 [booster]              310 [cera booster]            25 [upgrade limit cube]
  2299 [waste]                237 [expert town potion]      22 [random reward item]
  1802 [usable cera package]  124 [disguise]                19 [auto use special buff]
  1718 [etc]                  106 [dye]                     18 [quest receive]
  1504 [booster selection]    101 [throw]                   18 [unlimited town and dungeon]
  1376 [upgradable legacy]    101 [town and dungeon]        15 [random upgradable legacy]
  1359 [cera package]          80 [legacy]                  13 [multi upgradable legacy]
  1210 [quest]                 76 [couple room item]        13 [teleport potion]
  1168 [avatar emblem]         64 [unlimited waste]         11 [avatar gift]
```

尾部还有 `[gold kaleido]` 11、`[waste instant item]` 10、`[scale effect]` 10、`[only effect]` 9、
`[creature expitem]` 7、`[creature]` 5、`[stackable legacy]` 5、`[global effect]` 4、
`[quiz item]` 3、`[apc legacy]` 1、`[feed]` 1、`[stackable]` 1 等，各 ≤ 4 个的另有十余种。

**`unlimited` 前缀 = 使用后不消耗**：`[unlimited waste]` 64、
`[unlimited town and dungeon]` 18、`[unlimited etc]` 2，合计 84 个道具。

## 冷却与场所也在归档里

小药瓶 8474（`stackable/tutorial/tutorial_8474.stk`）：

```
[stackable type]  [waste] 4
[stack limit]     1000
[cool time]       10000          ← 毫秒
[cooltime group]  0
[hp recovery]     + 300 1000 myself
```

不消耗的猫头鹰（`stackable/cash/owl_unlimited.stk`，`[unlimited waste]`）：

```
[cool time]           3000
[cooltime group]      70
[action usable place] [village] [dungeon]
```

APC 召唤道具（`stackable/professional/puppet/pvp_doll.stk`，全档唯一的 `[apc legacy]`）：

```
[stackable type]  [apc legacy] 1
[cool time]       1000
[cooltime group]  100
[int data]        6302 6250 85  6303 6250 85  ...   ← 可召唤的 APC 表
```

`[cool time]` 有 6,254 个道具声明，`[cooltime group]` 有 1,713 个。同组共享冷却。

## `[increase status type]`：只有经验，没有等级

全档 34,532 个文件里只有 **264 个**带这个标签：

```
   120  [expUp]                    固定值
   100  [expPercentUp]             百分比
    41  [expUpByPercent]           百分比
     3  [expUpByCrackOfDimension]  无数值，上游明确拒绝
```

**全档没有任何「直接提升等级」的道具标签。** 升级是加经验之后重算出来的结果，
不是独立机制——上游 `ExperienceItemUseResult` 里的 `PreviousLevel`/`NewLevel`
是发放经验后算出来的。

名字带「升级」的那批都不是升级券：金库升级工具改的是仓库容量，
`40Lv升级礼包`（`event_7911`）是 `[cera booster]`，靠 `[booster info]` 发放别的道具。

经验值胶囊（`chn_stk_Ria_growth_capsule_30000.stk`）：

```
[stackable type]        [etc] 0
[increase status type]  [expUp] 30000
[/increase status type]
```

百分比型按**当前等级那一段的经验区间**折算，不是按总经验：
`(该级阈值 - 上一级阈值) × pct / 100`（上游 `ExperienceItemDefinition.CalculateGain`）。

## 老经验书是废的

`增加100经验值的书`（id 1034，`stackable/book_exp1.stk`）**没有** `[increase status type]`，
只有 `[stackable type] [etc] 1` 和 `[use wav] EXPERIENCE_UP_ITEM`——数值 100 不在文件里。

上游的判定要求两件事同时成立：带经验类 `[increase status type]`，
且 `[stackable type]` 是 **variant 0 的 `[etc]`**。老书是 `[etc] 1` 且无效果标签，
两条都不满足，在参考实现里直接不支持。

所以归档里并存两代机制：老的按 id 硬编码，新的用 `[increase status type]` 声明。

## `[stackable type]` 不足以分派行为

金库升级工具（`stackable/cash/safe_upgradekit.stk`）声明的是 `[stackable type] [etc] 0`
——**和经验值胶囊完全相同**。两者的区别只在于胶囊多一个 `[increase status type]`，
而金库工具没有任何可区分的行为标签。

上游因此是**按 itemId 硬编码**识别它的：
`InventoryCargoUpgradeRule.IsAccountCargoUpgradeToolItem(item.ItemId)`。

### `0x002C` 不是纯客户端效果

上游 `Handle_ENUM_CMDPACKET_USE_STACKABLE` 是一条特判链，按序尝试：

1. 频道限制的传送消耗品拒绝
2. 专家职业配方学习 → 改角色技能
3. 账号仓库扩容工具 → 改账号存储容量
4. 个人仓库扩容券 → 同上
5. 装备效果符文 → 改装备
6. 兜底：通用消耗 1 个

前五条都会改服务端状态，且都不是从 `[stackable type]` 推出来的。

## 上游的失败姿态：白名单，遇未知即拒

`ExperienceItemDataProvider` 维护一张 `AllowedRootTags`，`.stk` 里出现任何表外标签
就整体拒绝该道具，理由记为 `unreviewed PVF behavior tag [xxx]`。
同样地，效果与其他行为混在一个文件里、`[expUpByCrackOfDimension]` 这种归档里没有数值的，
一律拒绝而不是猜。

宁可某个道具用不了，也不给错的数值。

## 与本服的关系

- `0x001E INCREASE_STATUS` 尚未实现。文档里 body 只读出 offset 0 的槽位号一个字段（Confirmed）
- 当前 `0x002C` 实现对 list 0 里任何非装备都消耗 1 个，
  对 84 个 `unlimited*` 道具是**直接销毁**——已知缺陷，尚未修
- 冷却（`[cool time]` / `[cooltime group]`）完全没有实现
- `[action usable place]` 没有实现
