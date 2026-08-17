# 探针产物：可步行的镇间通路及其等级门槛

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-17
- 归档源：`DNFClient-debug/Script.pvf`，前 1 MiB SHA-256 `e8d8523fe75988068077a506645b814a9ab2d5eddb2044f50f00ac4e78a7f797`
- 读法：遍历 `town/town.lst` 的 25 个镇，逐个解析 `.twn` 的 `[area]` 块，
  取地图路径里带 `_Go` 的区域（该命名标记通往另一个镇的通路）及其 `[need level]`

```text
town   2 Underfoot.twn
      area  1 lvl  17  Town/Underfoot/Underfoot_GoSilverCrown.map
      area  3 lvl  17  Town/Underfoot/Underfoot_GoSewer.map
      area 10 lvl  27  Town/Underfoot/Underfoot_GoIcewall.map
```

**25 个镇里只有暗黑城有可步行的镇间通路，且全部要 17 级以上。**

引用它的主张：[`0x0024 SET_USER_AREA`](../../../game/client-to-server/0x0024.md) 的实机样本
至今只覆盖**换区**，没有**换镇**——测试用的 10 级角色在银色村庄，而银色村庄的三个区域
（到达点、主街、决斗场入口）里没有任何跨镇通路，**走不出这个镇**。

因此[城镇的持久化](../../world-entry.md#城镇的持久化)那条链的最后一环仍未实机走通：
要走通它，需要一个能站在暗黑城的角色（17 级），或者走传送那条路。

## 顺带的一条否证

"走出城镇"与"换镇"不是一回事。银色村庄能走出**区域**，四次跨区都成功，
但那四次的镇号全是 1——**换区成功不等于换镇被验证过**。
