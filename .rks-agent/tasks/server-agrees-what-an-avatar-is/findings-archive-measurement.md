# 档案实测：`[equipment type]` 到底写了什么

只测量，不改生产代码，不做设计判断。

## 测量装置与口径

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/server-agrees-what-an-avatar-is`，
  分支 `server-agrees-what-an-avatar-is`，HEAD `565d33fe`。
- 档案：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`（72,894,573 字节，mtime 2017-01-05），
  经 `PVF_ARCHIVE_PATH` 传入，`ContentArchive.Available` 为真、`ContentArchive.CreateProvider()` 打开。
- 一次性探针 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/AvatarSlotProbeTests.cs`
  （`[SkippableFact]`），**测完已删除**。它照抄 `PvfEquipmentSlotDefinitionSource.Read` 的通路：
  `PvfListFile.Read(archive, "equipment/equipment.lst")` -> `TryAdd` 首次胜出 -> `archive.FindFile`
  -> `archive.GetTag("[equipment type]")` -> `archive.OpenScript` -> `reader.TryMovePast(tag)`
  -> `reader.TryReadSectionValue` 循环 -> `archive.ResolveString`。
  与生产读法唯一的差别：探针把该节的**全部**值都记录（不只第一个字符串），并且遍历全部模板。
- 探针跑了两遍（第二遍只多打印多节文件的路径），两遍输出逐字节相同 —— 可复现。
- **计数口径**：
  - 一行 = `equipment.lst` 的一个模板 id。id 无重复（见下），所以「行数 = 模板数」。
  - `[equipment type]` 一节里遇到的**第一个**字符串 token 记作「名字」（生产读法也只取第一个）。
  - 「原始形态」= `archive.ResolveString(token.Value)` 逐字，未做任何处理；下表中空格显示为 `·`，
    首尾用 `<< >>` 夹住以便看出有无空白。
  - 「规范化形态」= 生产 `ClassifyEquipmentType` 用的那一套：删反引号 -> `Trim()` -> `ToLowerInvariant()`。
  - 名字后面那个数 = 同一节内的 `Int32` token。

### 扫描的完整性自证（实测输出原文）

```
archive=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf
list.rows=118479
list.distinctIds=118479
list.duplicateIdRows=0
tag.resolved=True
scan.missingFile=0
scan.noEquipmentTypeSection=0
scan.noStringValue=0
scan.filesWithMoreThanOneSection=2
distinctNormalizedNames=31
multiSection: id=26243 path=equipment/character/common/title/empirehero_red.equ sections=2
multiSection: id=26244 path=equipment/character/common/title/empirehero_blue.equ sections=2
```

即：`equipment.lst` 列了 118,479 行、118,479 个互不相同的 id（`TryAdd` 一次都没落空），
其中 **118,479 个文件全部找得到、全部有 `[equipment type]` 节、全部有字符串值**，
没有一个模板被漏掉或读不出。后面各表计数之和 = 118,479（`awk` 复核通过），
所以下面的清单是**穷尽的**，不是抽样。

只有 2 个文件写了两节 `[equipment type]`（两个 `[title name]` 称号模板），
生产读法取第一节，本次测量也取第一节。

---

## Q1 — `[equipment type]` 的全部不同取值与出现次数

**恰好 31 个不同取值**（规范化后 31 个；原始形态也恰好 31 个，**一一对应，
没有任何一个名字有第二种拼写**）。完整清单，按规范化名字排序，无截断：

| # | 规范化形态 | 原始形态（`·`=空格，`<< >>`=边界） | token 种类 | 出现次数 |
|---|---|---|---|---|
| 1 | `[amulet]` | `<<[amulet]>>` | QuotedString | 2755 |
| 2 | `[artifact blue]` | `<<[artifact·blue]>>` | QuotedString | 47 |
| 3 | `[artifact green]` | `<<[artifact·green]>>` | QuotedString | 31 |
| 4 | `[artifact red]` | `<<[artifact·red]>>` | QuotedString | 65 |
| 5 | `[aurora avatar]` | `<<[aurora·avatar]>>` | QuotedString | 423 |
| 6 | `[breast avatar]` | `<<[breast·avatar]>>` | QuotedString | 7565 |
| 7 | `[charm]` | `<<[charm]>>` | QuotedString | 50 |
| 8 | `[coat avatar]` | `<<[coat·avatar]>>` | QuotedString | 8391 |
| 9 | `[coat]` | `<<[coat]>>` | QuotedString | 4114 |
| 10 | `[creature]` | `<<[creature]>>` | QuotedString | 707 |
| 11 | `[face avatar]` | `<<[face·avatar]>>` | QuotedString | 7028 |
| 12 | `[flag]` | `<<[flag]>>` | QuotedString | 47 |
| 13 | `[hair avatar]` | `<<[hair·avatar]>>` | QuotedString | 7929 |
| 14 | `[hat avatar]` | `<<[hat·avatar]>>` | QuotedString | 8325 |
| 15 | `[magic stone]` | `<<[magic·stone]>>` | QuotedString | 1333 |
| 16 | `[name tag]` | `<<[name·tag]>>` | QuotedString | 28 |
| 17 | `[pants avatar]` | `<<[pants·avatar]>>` | QuotedString | 8194 |
| 18 | `[pants]` | `<<[pants]>>` | QuotedString | 4066 |
| 19 | `[ring]` | `<<[ring]>>` | QuotedString | 2848 |
| 20 | `[shoes avatar]` | `<<[shoes·avatar]>>` | QuotedString | 8070 |
| 21 | `[shoes]` | `<<[shoes]>>` | QuotedString | 3956 |
| 22 | `[shoulder]` | `<<[shoulder]>>` | QuotedString | 3841 |
| 23 | `[skin avatar]` | `<<[skin·avatar]>>` | QuotedString | 2954 |
| 24 | `[support weapon]` | `<<[support·weapon]>>` | QuotedString | 26 |
| 25 | `[support]` | `<<[support]>>` | QuotedString | 2206 |
| 26 | `[title name]` | `<<[title·name]>>` | QuotedString | 1796 |
| 27 | `[waist avatar]` | `<<[waist·avatar]>>` | QuotedString | 7000 |
| 28 | `[waist]` | `<<[waist]>>` | QuotedString | 3822 |
| 29 | `[weapon avatar]` | `<<[weapon·avatar]>>` | QuotedString | 449 |
| 30 | `[weapon]` | `<<[weapon]>>` | QuotedString | 17668 |
| 31 | `[wrist]` | `<<[wrist]>>` | QuotedString | 2745 |

合计 118,479 = 模板总数。

### Q1 结论

1. **31 个取值，一个不多一个不少。** 上表就是全部。
2. **原始形态里没有反引号，也没有首尾空白。** 每个名字都是 `QuotedString` token，
   `ResolveString` 出来就是干净的 `[xxx]`（反引号是 PVF 源码里的定界符，不进字符串池）。
   规范化前后除大小写外完全一致 —— 档案里全是小写。
   既有判据断言过的那两种脏形态（带反引号的 coat avatar、带首尾双空格的大写 COAT AVATAR），
   **在真实 `equipment/` 档案里一次都没出现**；那是防御性归一化，不是对档案形态的记载。
   这里只报测量结果，不评价该防御该不该留。
3. `ByName` 表里的 `[title]` 和 `[earring]` 两个键，**在 `equipment.lst` 的 118,479 个模板里
   一次都没被用过**（见 Q4b 的 `ABSENT FROM ARCHIVE SCAN`）。本次测量只能证明
   「`equipment/` 下没有」，不能证明「档案里别处也没有」。

---

## Q2 — 以 `avatar]` 结尾的名字

实测输出原文：

```
=== Q2: names ending in "avatar]" (normalized) ===
[aurora avatar] | 423
[breast avatar] | 7565
[coat avatar] | 8391
[face avatar] | 7028
[hair avatar] | 7929
[hat avatar] | 8325
[pants avatar] | 8194
[shoes avatar] | 8070
[skin avatar] | 2954
[waist avatar] | 7000
[weapon avatar] | 449
avatarSuffixNameCount=11
```

### Q2 结论

**恰好 11 个，分别是：**
`[aurora avatar]`、`[breast avatar]`、`[coat avatar]`、`[face avatar]`、`[hair avatar]`、
`[hat avatar]`、`[pants avatar]`、`[shoes avatar]`、`[skin avatar]`、`[waist avatar]`、
`[weapon avatar]`。

计数口径：规范化后（小写、无反引号、已 Trim）以 `avatar]` 结尾的**不同名字**的个数，
与 `PvfEntryLifecycleContentSource.AvatarEquipmentSuffix` 的判定完全一致。
这 11 个名字覆盖 66,323 个模板。

`PvfEntryLifecycleContentSource.cs:32-35` 的注释说「hat avatar、coat avatar，
**and the nine others**」= 2 + 9 = 11。**测出来就是 11，注释这一条属实。**

顺带：`PvfEquipmentSlots` 类注释说「Zero through eleven are the avatar slots」= 槽 0..11 共 12 个。
时装**名字**是 11 个，不是 12 个。本次测量不判断这 1 个差额意味着什么，
只如实报出「11 个名字 vs 12 个槽位」这个数字上的不对齐。

---

## Q3 — `[artifact *]` / `[charm]` / `[flag]` / `[name tag]` 是否存在

实测输出原文：

```
=== Q3: probe names ===
[artifact red] | present=True | count=65
[artifact blue] | present=True | count=47
[artifact green] | present=True | count=31
[charm] | present=True | count=50
[flag] | present=True | count=47
[name tag] | present=True | count=28
```

```
=== Q3b: names NOT in PvfEquipmentSlots.ByName and NOT in the probe list ===
[aurora avatar]
[breast avatar]
[coat avatar]
[face avatar]
[hair avatar]
[hat avatar]
[pants avatar]
[shoes avatar]
[skin avatar]
[waist avatar]
[weapon avatar]
```

### Q3 结论

1. **六个全部存在**：`[artifact red]` 65 次、`[artifact blue]` 47 次、`[artifact green]` 31 次、
   `[charm]` 50 次、`[flag]` 47 次、`[name tag]` 28 次。
2. **除这六个和 11 个时装名之外，没有别的名字了。**
   31 = 14（`ByName` 里被真实用到的）+ 6（上面这批）+ 11（时装）。
   `ByName` 的 16 个键里有 14 个在档案中真实出现，`[title]` 和 `[earring]` 没有；
   14 + 6 + 11 = 31，与 `distinctNormalizedNames=31` 吻合。
   也就是说：**`ByName` 认不出的名字恰好有 17 个，分别是 Q3b 的 11 个时装名，
   加上 `[artifact red]`、`[artifact blue]`、`[artifact green]`、`[charm]`、`[flag]`、`[name tag]`。**
   这 17 个名字覆盖 66,591 个模板（66,323 时装 + 268 其他）。

---

## Q4（决定性）— 名字后面那个数是什么

`[equipment type]` 一节的值 token 数**恒为 2**（一个字符串 + 一个 Int32），
31 个名字、118,479 个文件无一例外（探针 `valueTokenCounts` 列全是 `2:<该名字的全部文件数>`）。
所以「名字后面那个数」是唯一确定的，不存在读错位置的歧义。

### Q4a：每个名字后面那个数的**取值集合**（`值:该值出现的文件数`）

| 规范化名字 | 档案里那个数的取值集合 | 不同取值个数 |
|---|---|---|
| `[amulet]` | 16:39, 18:45, 19:22, 20:2512, 21:134, 22:3 | 6 |
| `[artifact blue]` | 10:1, 15:40, 17:6 | 3 |
| `[artifact green]` | 10:1, 20:30 | 2 |
| `[artifact red]` | 10:1, 15:57, 17:7 | 3 |
| `[aurora avatar]` | 0:423 | **1（恒为 0）** |
| `[breast avatar]` | 0:7565 | **1（恒为 0）** |
| `[charm]` | 1:50 | **1（恒为 1）** |
| `[coat avatar]` | 0:8391 | **1（恒为 0）** |
| `[coat]` | 14:42, 15:35, 16:75, 17:913, 18:858, 19:801, 20:707, 21:581, 22:81, 23:21 | 10 |
| `[creature]` | 0:707 | **1（恒为 0）** |
| `[face avatar]` | 0:7028 | **1（恒为 0）** |
| `[flag]` | 21:47 | **1（恒为 21）** |
| `[hair avatar]` | 0:7929 | **1（恒为 0）** |
| `[hat avatar]` | 0:8325 | **1（恒为 0）** |
| `[magic stone]` | 16:13, 18:15, 19:7, 20:1000, 21:293, 22:5 | 6 |
| `[name tag]` | 1:28 | **1（恒为 1）** |
| `[pants avatar]` | 0:8194 | **1（恒为 0）** |
| `[pants]` | 13:35, 14:35, 15:74, 16:912, 17:847, 18:799, 19:700, 20:568, 21:75, 22:21 | 10 |
| `[ring]` | 1:1, 16:42, 18:49, 19:23, 20:2584, 21:146, 22:3 | 7 |
| `[shoes avatar]` | 0:8070 | **1（恒为 0）** |
| `[shoes]` | 11:36, 12:36, 13:74, 14:896, 15:819, 16:776, 17:688, 18:546, 19:64, 20:21 | 10 |
| `[shoulder]` | 12:34, 13:34, 14:73, 15:874, 16:797, 17:740, 18:670, 19:531, 20:66, 21:22 | 10 |
| `[skin avatar]` | 0:2954 | **1（恒为 0）** |
| `[support weapon]` | 21:26 | **1（恒为 21）** |
| `[support]` | 16:9, 18:6, 19:173, 20:1259, 21:756, 22:3 | 6 |
| `[title name]` | 1:1796 | **1（恒为 1）** |
| `[waist avatar]` | 0:7000 | **1（恒为 0）** |
| `[waist]` | 11:36, 12:34, 13:73, 14:871, 15:792, 16:758, 17:662, 18:525, 19:49, 20:22 | 10 |
| `[weapon avatar]` | 0:449 | **1（恒为 0）** |
| `[weapon]` | 1:10, 17:1375, 18:484, 19:111, 20:581, 21:275, 22:6190, 23:6715, 24:1909, 25:18 | 10 |
| `[wrist]` | 10:1, 16:39, 18:42, 19:22, 20:2524, 21:114, 22:3 | 7 |

31 个名字里，17 个是单值（11 个时装 + `[creature]` + `[charm]` + `[name tag]` + `[title name]`
+ `[flag]` + `[support weapon]`），14 个是多值。

### Q4b：档案的数 vs `ByName` 给的槽号（实测输出原文）

```
=== Q4b: ByName slot vs archive numbers, for names ByName lists ===
[weapon] | byNameSlot=12 | archiveNumbers=1:10,17:1375,18:484,19:111,20:581,21:275,22:6190,23:6715,24:1909,25:18
[title name] | byNameSlot=13 | archiveNumbers=1:1796
[title] | byNameSlot=13 | ABSENT FROM ARCHIVE SCAN
[coat] | byNameSlot=14 | archiveNumbers=14:42,15:35,16:75,17:913,18:858,19:801,20:707,21:581,22:81,23:21
[shoulder] | byNameSlot=15 | archiveNumbers=12:34,13:34,14:73,15:874,16:797,17:740,18:670,19:531,20:66,21:22
[pants] | byNameSlot=16 | archiveNumbers=13:35,14:35,15:74,16:912,17:847,18:799,19:700,20:568,21:75,22:21
[shoes] | byNameSlot=17 | archiveNumbers=11:36,12:36,13:74,14:896,15:819,16:776,17:688,18:546,19:64,20:21
[waist] | byNameSlot=18 | archiveNumbers=11:36,12:34,13:73,14:871,15:792,16:758,17:662,18:525,19:49,20:22
[amulet] | byNameSlot=19 | archiveNumbers=16:39,18:45,19:22,20:2512,21:134,22:3
[wrist] | byNameSlot=20 | archiveNumbers=10:1,16:39,18:42,19:22,20:2524,21:114,22:3
[ring] | byNameSlot=21 | archiveNumbers=1:1,16:42,18:49,19:23,20:2584,21:146,22:3
[support] | byNameSlot=22 | archiveNumbers=16:9,18:6,19:173,20:1259,21:756,22:3
[magic stone] | byNameSlot=23 | archiveNumbers=16:13,18:15,19:7,20:1000,21:293,22:5
[creature] | byNameSlot=24 | archiveNumbers=0:707
[earring] | byNameSlot=24 | ABSENT FROM ARCHIVE SCAN
[support weapon] | byNameSlot=24 | archiveNumbers=21:26
```

### Q4 结论

**1）档案里那个数不是槽号。** 三重证据，任一条单独就足以否掉：

- **同一名字下有多种数。** 14 个被真实用到的 `ByName` 名字里，有 11 个各带 2–10 个不同的数。
  槽号必须一一对应，这里不是。
  例：`[shoulder]` 后面的数取遍 12,13,14,15,16,17,18,19,20,21 —— 十个值，而槽只有一个。
- **数与 `ByName` 槽号不重合。** `[shoulder]` 的 `ByName` 槽号是 15，档案里 15 只占 874/3841
  （22.8%），其余 2,967 个 `[shoulder]` 模板后面写的是别的数。
- **单值的那几个名字也对不上。** `[title name]` 档案恒为 1、`ByName` 槽号 13；
  `[support weapon]` 档案恒为 21、`ByName` 槽号 24；`[creature]` 档案恒为 0、`ByName` 槽号 24。
  **凡是单值的，没有一个等于 `ByName` 的槽号。**

**2）注释说的「sub-type」成立。**
`PvfEquipmentSlotDefinitionSource` 类注释「the number after it is a sub-type the client uses
to pick an icon and a sprite, and nothing here reads it」——「不是槽号、是名字之下的再分类」
这一半**被测量证实**：同名多值、值域随名字而变、且与槽号无关。
（「用来挑图标和精灵」这一层本次测不到，那要看客户端；「nothing here reads it」是对代码的陈述，
`PvfEquipmentSlotDefinitionSource.Read` 确实只取字符串不取数字 —— 属实。）

从数值形状看，它更像**装备等级档/材质档**：`[shoes]` 的 11..20、`[shoulder]` 的 12..21、
`[coat]` 的 14..23 都是十连号且分布形状相似（中间厚两头薄），随部位整体平移一位。
**这是形状观察，不是结论** —— 本次没有测过它与等级/材质的对应，不作断言。

**3）关于「这个表是不是时装槽号的出处」—— 测出来：不是。**
11 个时装名后面那个数**全部恒为 0**（66,323 个时装模板无一例外）。
如果它是时装槽号，11 个时装就会共用槽 0，这与「0..11 是时装槽」直接矛盾。
**档案的 `[equipment type]` 第二个数，不能作为时装槽号的出处。**
时装槽号必须另找来源 —— 本次测量不指出该去哪找，只报「不在这里」。

---

## Q5 — 时装模板具体长什么样

`.equ` 是编译过的 token 流，不是文本文件；下面是 `PvfTokenReader` 逐记录读出的原样
（`record[n]` 是记录在脚本里的字节偏移，`·`=空格，`<< >>`=字符串边界）：

```
=== Q5b: raw token dump around [equipment type] for three avatar templates ===
--- [aurora avatar] id=42700 path=equipment/character/swordman/avatar/aura/aura_42700.equ ---
  record[89] Identifier `[equipment type]`
  record[94] QuotedString <<[aurora·avatar]>>
  record[99] Int32 0
  record[104] Identifier <<[emblem·socket·default]>>
--- [breast avatar] id=40200 path=equipment/character/swordman/avatar/neck/aneck_muffler.equ ---
  record[79] Identifier `[equipment type]`
  record[84] QuotedString <<[breast·avatar]>>
  record[89] Int32 0
  record[94] Identifier <<[avatar·type·select]>>
--- [coat avatar] id=29202 path=equipment/character/fighter/at_avatar/coat/acoat_default.equ ---
  record[64] Identifier `[equipment type]`
  record[69] QuotedString <<[coat·avatar]>>
  record[74] Int32 0
  record[79] Identifier <<[animation·job]>>
```

三个模板：

| `[equipment type]` 名字 | 模板 id | `equipment/` 下的相对路径 |
|---|---|---|
| `[aurora avatar]` | 42700 | `character/swordman/avatar/aura/aura_42700.equ` |
| `[breast avatar]` | 40200 | `character/swordman/avatar/neck/aneck_muffler.equ` |
| `[coat avatar]` | 29202 | `character/fighter/at_avatar/coat/acoat_default.equ` |

还原成 PVF 源码形态（制表符分隔）就是：

```
[equipment type]
	`[aurora avatar]`	0
[emblem socket default]
	...
```

### Q5 结论

- 时装模板的 `[equipment type]` 节结构与普通装备**完全一样**：一个 `QuotedString` 名字 +
  一个 `Int32`，紧接着就是下一个 identifier。没有任何额外字段标记它是时装。
- **能把它认成时装的，只有名字本身**（`avatar]` 后缀）。后面那个 0 不携带信息。
- 时装模板集中在 `character/<职业>/avatar/<部位>/` 与 `character/<职业>/at_avatar/<部位>/` 两种
  目录下，但这只是路径习惯；本次没有测过路径是否可靠地蕴含时装身份，**不要拿路径当判据**。

---

## 各问小结

| 问 | 答 |
|---|---|
| Q1 | 31 个不同取值，穷尽清单见上；原始形态无反引号无空白，与规范化形态一一对应 |
| Q2 | 恰好 11 个 `avatar]` 名字（aurora / breast / coat / face / hair / hat / pants / shoes / skin / waist / weapon），注释的「2 + nine others」属实 |
| Q3 | 六个探测名**全部存在**（65 / 47 / 31 / 50 / 47 / 28 次）；除它们与 11 个时装名外再无第三类未知名字 |
| Q4 | 那个数**不是槽号**，是名字之下的 sub-type（同名多值、与 `ByName` 槽号不重合、时装恒为 0）；注释这一条属实，且它**不是**时装槽号的出处 |
| Q5 | 时装模板的 `[equipment type]` 节与普通装备同构，唯一区分信号是名字后缀 |

## 未能测出的事项（不以推断填补）

- 那个 sub-type 数**具体代表什么**（等级档？材质档？客户端图标索引？）—— 本次没测，不作断言。
- 客户端是否真的用它「挑图标和精灵」—— 需要看客户端，服务器侧的档案里测不到。
- `[title]` 和 `[earring]` 在**档案别处**（如创角表）是否出现 —— 本次只扫了 `equipment/` 下的
  118,479 个模板，只能说「这批里没有」。
- **时装槽号的真正出处** —— 只测出「不是 `[equipment type]` 的第二个数」，没有找到它在哪。
- 「时装槽 0..11 共 12 个」与「时装名 11 个」的 1 个差额从何而来 —— 没测。

## 收尾自证

- 探针文件 `Server/tests/Dfo.Server.Infrastructure.Tests/Content/AvatarSlotProbeTests.cs` 已删除。
- `git status --porcelain` 输出**一行**：`?? .rks-agent/tasks/server-agrees-what-an-avatar-is/`
- 删除探针后全量 `dotnet test Server/86JP.Server.slnx`（带 `PVF_ARCHIVE_PATH`）：
  Protocol 521 + Networking 52 + Pvf 60 + Host 6 + Persistence 316 + Infrastructure 236(+2 skip)
  + Application 1025 = **2216 通过 / 2 跳过 / 0 失败**，与基线一致。
- 未提交任何内容，未执行任何 git 还原类命令，未触碰主检出与 client-debug-lab。


---

## 修正案 F-1 — 本文件的一处小计与自己的表对不上

由控制器在处置 QR-07-1 时补记。**原文保留不动，此处订正。**

本文件的探针输出原文逐名列出 11 个时装名的模板数
（423／7565／8391／7028／7929／8325／8194／8070／2954／7000／449）。
**这 11 个数相加是 66328。**

但本文件的结论句写成「这 11 个名字覆盖 **66,323** 个模板」，并据此得出
「17 个认不出的名字合计覆盖 **66,591** 个」。**两处都少了 5。**

**正确的数**：时装 **66,328**，17 个认不出的名字合计 **66,596**。

两次独立重测（TASK-07 执行者一次、QR-07-1 评审者一次，读法均照
`PvfEquipmentSlotDefinitionSource.Read`）与本文件那张**表**逐个相同，
只与本文件的**结论句**不同——即表是对的，小计算错了。

旁证：非时装六项 65+47+31+50+47+28 = **268** 两版一致，差额全部落在时装小计这一项；
上一个成果的三处独立记载均写 66328。

**教训**：本文件是本轮唯一一次穷尽档案的测量，它的表被后续多份产物引用，
而**被引用的是它的结论句而不是它的表**。一个由多项相加得出的数，
**要么当场写出加法，要么不要写小计**。
