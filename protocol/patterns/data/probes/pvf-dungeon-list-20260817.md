# 探针产物：`dungeon/dungeon.lst` 的 id → 路径映射

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-17
- 归档源：`DNFClient-debug/Script.pvf`，前 1 MiB SHA-256 `e8d8523fe75988068077a506645b814a9ab2d5eddb2044f50f00ac4e78a7f797`
- 读法：`Dfo.Pvf` 打开归档 → `FindFile("dungeon/dungeon.lst")` → `OpenScript` 逐 token 读，
  `Int32` 与紧随其后的字符串成对（同 `PvfListFile.Read` 的语义）
- 全表 839 条，id 范围 1–20265。本页只留与教程入场相关的一段。

```text
    1 -> Act1/Lorien.dgn
    2 -> Act1/LorienInside.dgn
    3 -> Act1/MirkWood.dgn
10000 -> Tutorial/Tutorial.dgn
10001 -> Tutorial/Tutorial_timegate.dgn
10002 -> Tutorial/Tutorial_timegate2.dgn
10003 -> Tutorial/Tutorial_atslayer.dgn
10004 -> Tutorial/Tutorial_knight.dgn
```

引用它的主张：1 级新角色进城后向 `CMD 0x000F` / `0x0010` 填的目标 id 是 **10000**，
即教程副本 —— 见 [flow-dungeon-entry](../../flow-dungeon-entry.md)。

`10001`–`10004` 的存在同时说明教程本身按职业分化（`atslayer` = 女鬼剑、`knight` = 骑士），
这一点尚无实机观察。
