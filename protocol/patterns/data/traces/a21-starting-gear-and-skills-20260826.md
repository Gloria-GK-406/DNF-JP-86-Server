# A21 建号发的是什么：起始装备与初始技能

**2026-08-26。** 判据有三层，互相独立：

1. **参照会话的字节**，`ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt`，
   458 帧，一次完整的新手副本流程。
2. **参照会话自己的库**，`ServerS4A21/Server/DfoServer/bin/Debug/Data/inventory.db`，
   即产出上面那些字节的行。
3. **上游的构造代码**，`InitialCharacterEquipment.cs`、`InitialCharacterSkills.cs`、
   `UserInfoSubtype1Builder.cs`、`ItemListProtocolWriter.cs`。上游是产出参照字节的那台
   服务端，所以读它是取证不是猜。

本仓这一侧的字节取自同日的中转记录与 `entryprobe`，对账脚本 `cmpentry.py`。

---

## 职业号就是性别：先把命名钉住

`character/character.lst` 的号是**转职前的职业**，男女算两个号：

| job | `.chr` | 名字 | 名字的来源 |
|---:|---|---|---|
| 0 | `Swordman/Swordman.chr` | 鬼剑士（男） | 实机确认；其教学向导 461 是 `aicharacter/swordman/aganzo_tu`（阿甘左），与鬼剑士线吻合 |
| 9 | `Swordman/DSSwordman.chr` | — | 本仓与上游都叫它 DarkKnight（`DarkKnightInitialSkillLayout`、job==9 的特判）；中文名无出处 |
| 11 | `Swordman/ATSwordman.chr` | — | `AT` 前缀与 `ATGunner`/`ATFighter`/`ATMage` 同族，推测是同职业的另一性别；**未证实** |
| 13 | `DemonicLancer/DemonicLancer.chr` | 魔枪士 | 起始武器 114000000 解析为 `character/demonicLancer/weapon/pike/…` 见习长枪 |

**档案只给目录名，不给中文名。** `.chr` 里没有 `[name]`，也没有职业名表；上面能填的两格都是别的
证据倒推出来的，填不出的就空着。

把目录名 `Swordman` 直译成"剑士"会同时盖住 0 和 11，而这两个号的初始技能和起始装备并不相同。
本仓建号发的是 `job = 0`，即**鬼剑士（男）**，未转职（growtype 0）。

## 参照角色不是鬼剑士

这件事先说，因为不知道它会把后面每一条都读错。

参照角色 `1122` 的武器是 `114000000`，在 A21 档案里是
`character/demonicLancer/weapon/pike/114000000.equ`，**见习长枪**，耐久 45。
所以它是 **job 13 魔枪士**，不是 job 0 鬼剑士。

对照 `character/DemonicLancer/DemonicLancer.chr` 的 `[initial value][skill]`：

```
179 7   174 1   169 1   2 1   3 1   511 1
```

与参照 SKILLINFO 里的六条完全一致。也就是说，参照里的技能 id `2`、`3` 是魔枪士的
**鬼印珠**和**自动格挡**，不是鬼剑士的 `5` 鬼斩、`46` 上挑。拿它当鬼剑士的期望值会得出
"我们的技能 id 错了"这个假结论。

## 起始装备来自 `[create equipment list]`

每个职业的 `.chr` 都有这一段。鬼剑士（job 0）：

```
[create equipment list]
[weapon] 27600   [coat] 10400   [pants] 12400
[/create equipment list]
```

- `27600` = `破损的武士刀`，耐久 45
- `10400` = `残破的牛皮背心`，耐久 33
- `12400` = `残破的牛皮短裤`，耐久 27

**槽位名是 QuotedString，不是标签。** 段的开合（`[create equipment list]` /
`[/create equipment list]`）是 Identifier，段内的 `[weapon]` 这些是带反引号的字符串。
只按标签匹配的读法会看到一个空段，然后给每个角色发一件装备都不带——而 `dump` 打印出来的
形态对此毫无提示，两者看上去一模一样。上游那条正则里的可选反引号 `\]`?\s*(\d+)`
就是这个意思。

参照的库里对应三行，`list_type = 3`（装备容器）：

| slot | itemId | 耐久 | 说明 |
|---:|---:|---:|---|
| 12 | 114000000 | 45 | 武器 |
| 14 | 10400 | 33 | 上衣 |
| 16 | 12400 | 27 | 下装 |

耐久与档案里各自 `.equ` 的 `[durability]` 逐个吻合，这是"这三行确实由
`[create equipment list]` 生成"的独立佐证。

槽号出自上游 `EquipmentType`：0..11 是时装槽（A21 在武器前插了 `AuraSkinAvatar`），
12 武器、13 称号、14 上衣、15 护肩、16 下装、17 鞋、18 腰带、19 项链、20 手镯、
21 戒指、22 辅助装备、23 魔法石、24 耳环。

## 装备条目在 USERINFO subtype 1 里是 60 字节

参照 subtype 1 的 body 是 519 字节，装备区 180 字节整，3 条 × 60。

```
偏移  长度  字段
  0     1   slot
  1     4   itemId
  5     4   实例值（每件随机，用来区分同型两件）
  9     1   attribute
 10     2   durability
 12     4   clearAvatarId / sealFlag
 16     4   enchantCardId
 20     1   enchantUpgradeCount
 21     1   amplifyType
 22     2   amplifyValue
 24     1   编年史条数 n，随后 n × 8
 25     4   expireTime
 29     1   纹章条数 n，随后 n × 4
 30     2   rune
 32     1   随机属性条数 n；n = 0 时到此为止
 33    10   尾块：genuine / emancipate / trade / u16 / 3 × u8 / remainUse / 0
 43    17   A21 追加：u32 0、u32 8、9 字节零
```

三个计数块都是**动态**的：新做的装备三个都是 0，各占 1 字节。写成固定条数
（例如恒写 2 条编年史、3 条随机属性并把选项一并写出）会把每条撑到 74 字节，
条目区就成了 222 字节，整帧 561——比参照长 42 字节。

末尾 17 字节只在普通穿戴槽出现。时装槽（0..11）和宠物槽只有 9 字节零，公会勋章槽另有形状。

## SKILLINFO `0x0013`：两页，主动与被动分两段槽位

参照 body 70 字节，结构是 `页 × 2 + 尾 4`，每页 `u16 页头 + u8 条数 + 条数 × (slot, u16 id, level, u8 附加数)`。

- **主动进 0、1、2…，被动进 102、103、104…。** 这不是选择：参照把 `179`（物品分解，
  7 级）、`174`（基础精通）、`511`（基础防具精通）放在 `0x66/0x67/0x68`，把三个主动放在
  `0/1/2`。技能栏只画低位那一段，所以被动放在 102 之后既被持有、又不占键位。
- **第二页是第一页的副本。** 新角色两页内容完全相同。

本仓此前只写主动、只有一页，body 25 字节。补齐后 70 字节，与参照同长，逐字节只差
职业不同的两个 id。

## subtype 1 里第二页写 0

同一次会话里，SKILLINFO 两页都是 6 条，而 subtype 1 的第二页条数是 `00`。
这不是矛盾：上游 `UserInfoSubtype1Builder.A21ShouldOmitCopiedPage1` 明写——第二页若与
第一页逐条相同，subtype 1 就把它当空的写。两帧的规则本来就不同，一个报"持有什么"，
一个报"有什么不一样"。

## roster 外观只画到槽位 13

`characters.appearance_blob` 是 `u8 条数` + 每条 23 字节
（`slot(1) displayItem(i32) expansionLength(i32=4) expansion(4) state(1) linkItem(i32) enchant(u32) flag(1)`）。

参照的 subtype 2 只带 **1** 条——武器。上游 `IsA21RosterAppearanceSlot` 是
`0 <= slot <= 13`：时装十二槽、武器、称号。上衣和下装穿在身上但不进这里。
把三件都写进去会让这一帧比参照长 46 字节。

## 对账结果

以 job 0 鬼剑士对 job 13 魔枪士，入场批次 **66 帧对 66 帧**，其中 **60 帧 body 逐字节相同**。
其余六帧的差都能算清：

| # | 帧 | 参照 | 本仓 | 差因 |
|---:|---|---:|---:|---|
| 0 | `CMD 0x0004` | 261 | 261 | 角色数据 |
| 1 | `NOTI 0x0002` subtype 0 | 188 | 190 | 名字 4 vs 6 字节 |
| 10 | `NOTI 0x0013` | 70 | 70 | 两个技能 id（职业不同），共 4 字节 |
| 12 | `NOTI 0x0002` subtype 1 | 519 | 519 | 17 段差，全是职业属性、装备 id、实例值、技能 id |
| 27 | `NOTI 0x006C` | 17 | 3 | 活动表，早于本次的既有差异 |
| 63 | `NOTI 0x0002` subtype 2 | 103 | 105 | 名字 4 vs 6 字节 |

## 顺带读出来的两件事

**A21 的新手副本和 A12 不是同一张图。** `dungeon/Tutorial/Tutorial.dgn` 在 A21 是
3 × 2 四间房：`(0,1)=61000 (1,1)=61001 (2,1)=61002 (2,0)=61005`，起点 `(0,1)`，
Boss `(2,0)`。A12 是 5 × 3 七间房，Boss 在 `(3,0)`。凡是从 A12 布局推出来的房间数、
路径、地图 id 都不能沿用。

**槽位 177..181 是活动邀请函那一档。** 本仓入场时主背包比参照多五条，就是这五件
（神秘邀请函、领主的邀请函、纳特拉的龙鳞、导航人偶、牛头怪乐园通行证）。参照的库里也有
同样五行，但它是在 ITEM_LIST 之后用 `NOTI 0x000E UPDATE_ITEM_LIST` 补发的；本仓在快照
里就带上了。形状本身没问题，差的是时机。
