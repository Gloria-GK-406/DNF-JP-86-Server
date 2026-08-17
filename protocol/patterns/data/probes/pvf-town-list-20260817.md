# 探针产物：`town/town.lst` 的 id → 镇映射

归档一次性探查的结果，使引用它的主张可以解析。见 [evidence.md](../../../evidence.md#一次性探查--直接写进文档)。

- 归档时间：2026-08-17
- 归档源：`DNFClient-debug/Script.pvf`，前 1 MiB SHA-256 `e8d8523fe75988068077a506645b814a9ab2d5eddb2044f50f00ac4e78a7f797`
- 读法：`Dfo.Pvf` 打开归档 → `FindFile("town/town.lst")` → `OpenScript` 逐 token 读，
  `Int32` 与紧随其后的字符串成对；镇名取各 `.twn` 文件的 `[name]`
- 全表 25 条，id 范围 1–102。**没有 id 0。**

```text
    1 -> SilverCrown.twn            银色村庄
    2 -> Underfoot.twn              暗黑城
    3 -> Sewer.twn                  赫顿玛尔废墟
    4 -> Behemoth.twn               天帷巨兽
    5 -> ShelterOfNorth.twn         北方避难所
    6 -> Gent.twn                   根特
    7 -> Warfare.twn                末日之都坎特温
    8 -> GuildAgit.twn              公会大厅
    9 -> LuftHafen.twn              鲁夫特悬空海港
   10 -> Fair_PVP.twn               决斗场
   11 -> Shonan.twn                 虚祖首都 素喃
   12 -> TimeGate.twn               时空之门
   13 -> TimeGateRequiem.twn        时空之门 - 镇魂曲
   14 -> Slough.twn                 斯曼工业基地
   15 -> Twin_Raid.twn              时空之门
   17 -> SaintHorn.twn              圣者之鸣号
   19 -> BattleShip_Raid.twn        苍穹贵族号
   20 -> BattleShip_Normal.twn      苍穹贵族号
   21 -> WestCoast.twn              西海岸
   22 -> Jelva.twn                  克洛诺斯岛
   23 -> Event_SheepFarm.twn        （无 [name]）
   99 -> Chn_Tournament.twn         时空之门
  100 -> Chn_Shonan_Holy.twn        素喃郊外
  101 -> CoupleRoom.twn             婚房
  102 -> chn_kronos.twn             众神议会厅
```

引用它的主张：`0x0004` 响应偏移 218 是**镇 id**，写 0 会让客户端崩溃 ——
见 [`0x0004`](../../../game/client-to-server/0x0004.md#偏移-218-是入场镇-id不是槽位写-0-必崩)。
表里没有 id 0，这是"该字节不能为 0"的内容侧依据；`16`、`18`、`24`–`98` 同样缺号，
所以**该字节的合法值不是一段连续区间**。

## 两个镇的等级门槛

同一次探查顺带读出的，因为它是"该字节是镇 id"的一条独立佐证：

```text
SilverCrown.twn (1)   area 0 Gate_SilverCrown.map        [need level] 1    [gate] 474 234
                      area 1 SilverCrown_MainStreet.map  [need level] 1    [normal]
                      area 2 SilverCrown_WarRoom.map     [need level] 1    [dungeon gate] 21
Underfoot.twn   (2)   area 0..12 全部                     [need level] 17（area 9/10 为 27）
```

偏移 218 曾被误填为槽位，槽位 2 的角色因此被送进 id 2 = 暗黑城，而暗黑城**每个区域都要求 17 级**
——一个 1 级角色站在只有 17 级才进得去的镇里，本身就说明这一字节选的是镇，不是列表下标。

`[name]` 里没有"赛丽亚旅馆"，`town.lst` 里也没有它：登录后落到该镇赛丽亚旅馆是**客户端**
拿到镇 id 之后自己完成的落点，不是协议字段。
