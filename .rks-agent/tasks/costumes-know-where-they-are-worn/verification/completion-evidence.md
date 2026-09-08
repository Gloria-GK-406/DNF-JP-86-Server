---
kind: rks-verification
decision: pass
---

# 新鲜验证：时装知道自己穿在哪一格

## 被评估的确切主张

> 任务契约 `costumes-know-where-they-are-worn`（revision 1）的成功条件 **SC-1..SC-8 全部达成**，
> 成果可以接受提交。

## 本次验证的立场与边界

- **未采信执行方（`behavior-change-list.md`）与评审方（`reviews/change-review.md`）的任何结论。**
  下面每一条的证据都是本轮自己敲命令、自己读输出得到的。评审的 `approved` 只作为「已被别人看过」
  的背景，不作为任何一条 SC 的证据。
- **本轮未修改仓内任何产品或判据文件。** 唯一写入的是本文件。
  三次变异核实全部用**文件级备份 + `sha256sum`** 还原并核过散列，**未使用任何 git 还原类命令**
  （`git checkout --` / `git restore` / `git stash` 一次都没有出现）。
- 档案探针**全部走仓内既有工具 `Tool/PvfProbe`**（只读档案、只打印），**未新建也未删除任何仓内文件**；
  最后 `git status --short` 与验证开始时逐行一致（10 个 ` M` ＋ 2 个 `??`），见文末。
- 上游 `E:/Project/DNF/ServerS4A21/` **只读**（一次 `cat`）。
- 跑判据前确认过**没有 `Dfo.Server.Host` 进程**：
  `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"` 只有两个 dotnet 进程，
  分别是 VS Code C# Dev Kit 的 CPS 宿主与一个 `vstest.console.dll`，均非 `Dfo.Server.Host`。
  三次全量构建全部成功，无 MSB3027。

基线 `80bdb1445a1e29d1b3bf871351391f4c79cc1380`，分支 `costumes-know-where-they-are-worn`，未提交。

---

## 判据全量（SC-6 的主证据，也是多条 SC 的共同前提）

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

本轮跑了**四次**（一次开场、三次变异各一次、一次收尾）。未变异的两次（开场、收尾）逐项目：

| 项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Server.Protocol.Tests | 585 | 0 | 0 |
| Dfo.Server.Application.Tests | 1070 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 329 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 259 | 2 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| **合计** | **2361** | **2** | **0** |

`EXITCODE=0`（两次都是）。基线是 2335 / 2 / 0，差额 **+26**。
两条跳过是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`，
与基线同名同数。**本轮四次运行未出现既知的 `ObjectDisposedException: 'SQLitePCL.sqlite3'` 偶发。**

---

## 成功条件 → 证据

### SC-1 `ByName` 有且只有那 11 个时装键，槽号与对照表一致；判据逐键钉住，改错会红 — **达成**

**键与值（自己读的文件，不是 diff）：**

```
$ sed -n '58,88p' Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
$ grep -c '"\] = ' Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs   # 27
```

字典共 **27** 项（口径：文件里形如 `"] = ` 的行数，逐行可数）。其中 11 项为时装键，
`[hat avatar]`=0 / `[hair avatar]`=1 / `[face avatar]`=2 / `[coat avatar]`=3 / `[pants avatar]`=4 /
`[shoes avatar]`=5 / `[breast avatar]`=6 / `[waist avatar]`=7 / `[skin avatar]`=8 /
`[aurora avatar]`=9 / `[weapon avatar]`=10，**与契约 Context 的对照表逐行一致**；
其余 16 项自 `["[weapon]"] = 12` 起。

**「有且只有 11」由我自己的穷尽扫描独立复现**（不是引用执行方的数）：

```
$ PVF_ARCHIVE_PATH=... dotnet run -- values equipment/equipment.lst "[equipment type]"
first token after [equipment type] over 118479 files:
  ... 31 行直方图 ...
$ grep "avatar\]" eqtypes.txt | wc -l          # 11
$ grep "avatar\]" eqtypes.txt | awk '{s+=$1}'  # 66328
```

**118479 条 equipment 索引，以 `avatar]` 结尾的 `[equipment type]` 恰好 11 种，覆盖 66328 个模板，
没有第 12 种。** 与契约 Assumptions 的三个数一位不差。
（顺带独立复现了 `AGENTS.md` 的 C-8 修正数：表外剩下的 6 种是
`[artifact red]` 65 ＋ `[charm]` 50 ＋ `[artifact blue]` 47 ＋ `[flag]` 47 ＋ `[artifact green]` 31 ＋
`[name tag]` 28 ＝ **268** 个模板，与它写的「只剩 6 个、268 个模板」吻合。）

**判据逐键钉住 —— 由变异 M1 / M2 实证，见「变异核实」一节。**

### SC-2 时装拖到它自己的装扮槽能穿上，判据走通 `PutOnAsync` 并证明两端都落 — **达成**

`TheAvatarBagIsServedTests` 里两条，两个不同的源列表：

- `A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`：断言 `Moved`、
  `ReadAsync(Avatar, AvatarSlot)` 为 `null`、`ReadAsync(Equipment, 3)` 的 `ItemId` 为 `CostumeTemplate`
  ——**源清空 + 目标落定，两端都验**。
- `A_costume_is_worn_out_of_the_bag_too`：主背包出发，同样断言 `Moved` 与目标落定。

两条都在本轮的 585 条 Protocol 判据里通过；M2（删 11 个键）时两条都变红，说明它们真的依赖那些键。

### SC-3 拖到别的装扮槽被拒为 `ItemCannotOccupySlot` 且带模板号 — **达成**

两处：

- `TheAvatarBagIsServedTests.A_costume_does_not_go_on_a_costume_slot_that_is_not_its_own`：
  上衣时装（槽 3）拖向槽 5 → `Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status)`
  ＋ `Assert.Equal(CostumeTemplate, result.ItemId)` ＋ 目标槽仍为 `null`。
- `RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_aimed_at_the_wrong_costume_slot_is_recorded_with_its_template_and_that_slot`：
  断言**日志整行字面量**
  `Move item space game-avatar-refusal character=60: item 101500690 {BagSlot}->2 refused as ItemCannotOccupySlot`
  ——模板号在行里，槽号在行里，状态在行里。

代码侧核对（自己读的 `MoveItemSpaceSlice.cs:405-407`）：

```csharp
if (slot.Value != command.DestinationSlotIndex)
    return ItemMoveResult.Refused(ItemMoveStatus.ItemCannotOccupySlot, piece.ItemId);
```

带 `piece.ItemId` 的两参重载，正是「带模板号」。

### SC-4 非时装的穿戴行为逐值不变；`ByName` 原有键值一个未改、既有判据一条未动 — **达成**

**(a) 原有键值逐字未改 —— 用剥注释后的比对证明，而不是看 diff 的排版：**

```
$ git show 80bdb144:.../PvfEquipmentSlots.cs > pes-base.cs
$ sed -E 's@[[:space:]]*//.*$@@' <file> | grep -v '^[[:space:]]*$'   # 两侧同法剥注释与空行
$ diff pes-base.stripped pes-cur.stripped
6a7,17
>             ["[hat avatar]"] = 0,
   ...（共 11 行）
$ diff ... | grep -c '^<'   # 0
$ diff ... | grep -c '^>'   # 11
```

**删除行 0、新增行 11**，`["[weapon]"] = 12` 起的每一行逐字节相同。

**(b) 生产代码只动了两个文件**（`git diff --stat` 的 `Server/src` 部分只有
`PvfEquipmentSlots.cs` 与 `MoveItemSpaceSlice.cs`），后者只改注释（见 SC-5 的 (c)）。

**(c) `ByName` 的读者我自己扫过，只有两个**：

```
$ grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs
```

生产侧仅 `PvfEquipmentSlotDefinitionSource.cs:76` 与 `PvfInitialEquipmentDefinitionSource.cs:122`
（其余命中是类声明、常量、注释与判据）。**没有第三个读者。**

**(d) 第二个读者（建号起始装备）的行为逐值不变 —— 我自己从档案侧独立核，不靠判据：**

```
$ PvfProbe dump character/character.lst          # 14 个职业 0..13
$ PvfProbe dump character/<每一个>.chr | sed -n '/\[create equipment list\]/,/\[\/…\]/p'
```

**14 个职业逐个打印，`[create equipment list]` 里出现的槽名只有 `[weapon]`、`[coat]`、`[pants]`
三种，零个 `avatar]` 名。** 所以补键不可能改变建号。
既有判据 `PvfEquipmentSlotsTests.No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
（`git diff` 显示该用例本轮**未被改动**）断言授予的槽集恰为 `{ByName["[weapon]"], ByName["[coat]"], ByName["[pants]"]}`
——若创建表出现时装名，槽集会多出 0..10 中的一个而变红。**它真的钉着这一条。**

**(e) 既有判据一条未动？** —— 按契约「正对应授权变化者除外，且每处改写要单独说明」的口径核：
被改的既有用例共 **5 条**（口径：`git diff` 里 `Server/tests` 三个既有文件中签名或断言发生变化的方法），
分别是 `PvfEquipmentSlotsTests` 的表范围判据、`RefusedEquip…` 两条、`TheAvatarBagIsServed…` 两条；
**每一条的断言改写都指向 F-1/F-2/F-3 这三条授权变化，且没有任何断言被放宽或删除**
（原「表里每个值都在 12..24」被换成更强的「11 个时装名占满 0..10 无缺口 ＋ 其余在 12..24」，
用 `Enumerable.Range(0,11)` 逐值比对）。非时装的 16 个键的逐键断言（`[weapon]`=12 … `[support weapon]`=24）
**一行未改**，仍在 `PvfEquipmentSlotsTests.cs:25-39`。

**(f) 非时装路径的逐值不变，我另做了一次代码级推演**（详见「独立判断三」末尾的 `TakeOffAsync` 一节）：
`WornSlotForAsync` 对时装的答案由 `null` 变为 `0..10`，
而所有以它为输入的判断都在与 `12..24` 的槽号比较，`null ≠ 12..24` 与 `0..10 ≠ 12..24` 结论相同——
**没有任何本次改动之前可达的状态，其结果发生变化。**

### SC-5 `PutOnAsync` 那个判断被做掉，结论/理由/证据写进注释，且有判据钉住结论 — **达成**

**(a) 注释里确实有结论、理由与限定**（自己读的 `MoveItemSpaceSlice.cs:357-388`）：
「**这就是那个决定**」「维持原样」「理由不再是空的」；理由是「交换两端是同一类东西，
且退回的那一格刚才正站着同一类东西」；并写明**覆盖范围**（只覆盖移动能到达的状态，
绕过服务器写的行不在内）与**不得推广**（`[earring]`/`[support weapon]`/`[creature]` 同映 24 的反例）。

**(b) 论证的两个承重前提我自己核过，都成立：**

- 「只有那 11 个 `avatar]` 名能到 `0..10`」——由我自己的穷尽扫描（SC-1）＋ 字典 27 项直接可数。
- 「装扮列表任意格都收时装；背包里的时装必在装备带，因为快捷栏不收时装」——
  `ItemSlotRuleService.CanOccupySlot`：`Avatar` 分支是 `0..209 && WorldClass == Avatar`；
  `AllowsQuickSlot` 明确排除 `KindAvatar = 8`（快捷栏 3..8）。
  且 `PvfEntryLifecycleContentSource.WornPieceMetadata` 给**所有**穿戴件（含时装）同一条主背包带
  `9..64`——**所以两件时装在主背包里共用同一条带，退回必然合法。** 论证成立。

**(c) 判据真的钉住结论 —— 我做了一次专门的变异（M3）来证明这一点，不采信任何人的说法：**
在 `PutOnAsync` 里加一行「装扮槽有位移则拒绝」的判定后跑全量，
**全仓恰好红 1 条，就是 `What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`。**
即：若有人日后真去加那个判定，会且只会被这条判据挡住。**钉得住。**

**(d) 生产文件确实只改了注释 —— 剥注释后与基线逐行比对：**

```
$ diff mis-base.stripped mis-cur.stripped
（无输出，exit 0）；两侧各 393 行
$ grep -n '/\*' MoveItemSpaceSlice.cs        # 无块注释
$ grep -n '"[^"]*//' MoveItemSpaceSlice.cs   # 无含 // 的字符串字面量
```

**剥掉全部 `//` 与 `///` 之后两侧完全相同**，且不存在会被这种剥法误伤的块注释或字符串。
`MoveItemSpaceSlice.cs` **确实只改了注释**。

### SC-6 全量 ≥ 2335 / 2 / 0 且退出码 0；字节锚 62 帧逐帧不变 — **达成**

全量见上表：**2361 / 2 / 0，`EXITCODE=0`**，两次独立运行一致。

字节锚：

```
$ sha256sum Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs
8d4ac37f951ebfb95f49b658001bcae9464440cf6eca5ae93e1c6a7f7d404be3
$ git show 80bdb144:Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs | sha256sum
8d4ac37f951ebfb95f49b658001bcae9464440cf6eca5ae93e1c6a7f7d404be3
```

**与基线同一散列**，文件里 `Assert.Equal(62, identities.Length)` 仍在（`:163`），且该文件在本轮全量中全绿。
62 帧的哈希表逐条未动。

### SC-7 「`AuraSkinAvatar` 在武器之前」两处更正；`AGENTS.md` 量具说明书按「表已量全」更新 — **达成**

**(a) 上游枚举我自己读了**（`E:/Project/DNF/ServerS4A21/Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs`）：

```
WeaponAvatar = 10,
AuraSkinAvatar = 11,
AuroraIllusionAvatar = AuraSkinAvatar,
```

**11 在 10 之后。** 原记载确为假。

**(b) 更正落在两个记载出处**（外加一处生产注释，共三处）：
`protocol/game/server-to-client/NOTI-0x0002.md:690-694`、
`protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md:83-84`，
以及 `PvfEquipmentSlots.cs:29-32`。三处都是**原文留记 + 就地更正**。

**(c) 没有第四份存活的旧说法**：

```
$ grep -rn "在武器之前\|在武器前\|武器前插\|ahead of the weapon\|inserted the aurora" \
    --include=*.md --include=*.cs . | grep -v "^./.rks-agent/"
```

只剩上述三条**引用旧文以更正**的行，无残留主张。另核 `StateSynchronizationPayloadCodec.Entry.cs:291`
写的是「A21 added the aurora-illusion slot at eleven」——**本来就是对的**，不需要改。

**(d) 量具说明书已更新**：`AGENTS.md:689` 新增一节
「2026-09-08：那 11 个时装槽已经量完，表已填上」，写明表已量全、11 个键已进表、时装能穿上、
槽 11 无键的理由，并说明**量具本身留着**、三条注意事项的起因已变。
配套还就地作废了 `:566`「时装取得下、穿不上」、改写了 `:611` 的小节标题、
加注了 `:644` 与 `:675` 的三个过期数、以及 `:684` 那句「那张表里出现哪些名字没有测过」。

### SC-8 变化清单正向反向都报、从代码出发独立扫、每条命令都真的被执行并写下命中数 — **达成**

**清单声明的 8 条命令，我逐条重跑，命中数逐条复现：**

| 命令（口径：`.md` 侧为文件数 `-rl`，`.cs` 侧为行数 `-rn`；排除 `.rks-agent/` 与 `.worktrees/`） | 清单写的 | 我跑出来的 |
|---|---:|---:|
| `grep -rl "NotWearable" --include=*.md .` | 3 | **3** |
| `grep -rl "avatar]" --include=*.md .` | 2 | **2** |
| `grep -rl "穿不上\|穿戴槽\|槽位映射" --include=*.md .` | 9 | **9** |
| `grep -rl "PvfEquipmentSlots" --include=*.md .` | 4 | **4** |
| `grep -rl "AuraSkinAvatar\|AuroraIllusion" --include=*.md .` | 3 | **3** |
| `grep -rn "FirstWornSlot\|LastWornSlot" Server/src Server/tests --include=*.cs` | 5 | **5** |
| `grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs` | 34 | **34** |
| `grep -rn "WornSlotForAsync" Server/src Server/tests --include=*.cs` | 7 | **7** |

**八条全部复现，一条不差。** 档案侧的三次扫描我也独立重做了两次（穷尽扫描、14 职业创建表），
数字逐项吻合；第三次（逐件读 11 个模板的 `[equipment type]`）我读了全部 11 件而不是抽样，见下。

正向 3 条（F-1/F-2/F-3）与反向 2 条（R-1/R-2）我逐条核对代码，**陈述本身没有一条不实**；
「改完一张表回头数行数」也做到了（27 个键、11 个时装名、5 条被改用例都能当场数出来）。
反向表的一处**不完整**（不是不实）记在「非阻塞观察」第 4 条。

---

## 变异核实（三次，全部文件级备份 + `sha256sum`，未用任何 git 还原类命令）

备份与散列：

```
$ cp PvfEquipmentSlots.cs  <scratch>/PvfEquipmentSlots.cs.bak
$ sha256sum PvfEquipmentSlots.cs > <scratch>/pes.sha256
  0d94e163b7549f8461231e28ca4e69e3b42189b446ff556d38b539221eaaeeb9
$ cp MoveItemSpaceSlice.cs <scratch>/MoveItemSpaceSlice.cs.bak
$ sha256sum MoveItemSpaceSlice.cs > <scratch>/mis.sha256
  56efdf1e705c230788405d4d0d2dade18535cea20326d5484172fedd9731adea
```

### M1 —— 把 `[coat avatar]` 的槽 3 改成 4

`EXITCODE=1`，**红 6 条，跨 4 个判据文件**（Protocol 4 红 / Infrastructure 2 红）：

1. `CostumeSlotsWereMeasuredTests.A_costume_is_worn_in_the_slot_it_was_measured_in(413500000, 3, "[coat avatar]")`
2. `PvfEquipmentSlotsTests.Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged`
3. `RefusedEquipIsRecordedWithItsTemplateTests.The_archive_calls_this_template_an_avatar_and_names_its_worn_slot`
4. `TheAvatarBagIsServedTests.A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`
5. `TheAvatarBagIsServedTests.A_costume_is_worn_out_of_the_bag_too`
6. `TheAvatarBagIsServedTests.What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`

**只改一个数字，四个互不相同的判据文件同时变红** —— SC-1 的「逐键钉住，改错会红」成立。

### M2 —— 把 11 个键整段删掉（回到本件之前）

`sed -i '/avatar\]"\] = /d'`，`EXITCODE=1`，**红 19 条**（Protocol 6 / Infrastructure 13）：
`CostumeSlotsWereMeasuredTests` 的 11 条参数化用例 ＋ `No_name_reaches_the_twelfth_costume_slot`
＋ `PvfEquipmentSlotsTests` 的表范围判据（Infrastructure 13）；
`RefusedEquip…` 2 条 ＋ `TheAvatarBagIsServed…` 4 条（Protocol 6）。

### M3 —— 我自己另加的一次：给 `PutOnAsync` 加上那个「不需要加」的判定

在 `PutOnAsync` 读到 `displaced` 之后插入
`if (displaced is not null && command.DestinationSlotIndex < 12) return Refused(ItemCannotOccupySlot, piece.ItemId);`，
`EXITCODE=1`，**全仓恰好红 1 条**：
`TheAvatarBagIsServedTests.What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`。

**这是 SC-5「有判据钉住结论」的直接证明**：结论是「维持原样、不加判定」，
而唯一一条会因加判定而变红的判据，正是清单声称在钉它的那一条。

### 还原与核散列

```
$ sha256sum -c <scratch>/pes.sha256   # OK
$ sha256sum -c <scratch>/mis.sha256   # OK
$ git status --short                  # 与验证开始时逐行一致
```

三次变异后复跑全量：**2361 / 2 / 0，`EXITCODE=0`**。

---

## 被点名要独立判断的四件事

### 一、三源是否真的互证；「实机量得」是否写得比证据强

**上游枚举（来源三）—— 我自己读了整份文件。** `WeaponAvatar = 10`、`AuraSkinAvatar = 11`；
其 `EquipmentTypeInfo.TextToType` 把 `[hat avatar]`…`[weapon avatar]` 逐名映到 0..10，
**与本仓补的 11 个键名对名、号对号，一处不差。**

**档案读出（来源二）—— 我自己读了全部 11 件，不是 3 件。**
用仓内既有 `Tool/PvfProbe` 的 `lst equipment/equipment.lst <id>`，逐件取 `[equipment type]`：

```
413550000 -> [hat avatar]     413560000 -> [hair avatar]    413570000 -> [face avatar]
413500000 -> [coat avatar]    413510000 -> [pants avatar]   413540000 -> [shoes avatar]
413520000 -> [breast avatar]  413530000 -> [waist avatar]   413580000 -> [skin avatar]
413590000 -> [aurora avatar]  413600000 -> [weapon avatar]  （另核 101500690 -> [coat avatar]）
```

**11/11 与契约对照表逐行吻合。** 探针只读只打印，用完无残留（`git status` 已核）。

**实机量得（来源一）—— 这是三源里我唯一无法亲自复现的一源，如实说明：**

- 执行方**本轮确实没有做实机**，数据来自上一件（#10，2026-09-07）的服务器日志。
  这一点契约 Scope Out 与 `behavior-change-list.md` 第七节都写明了，**没有被藏起来**。
- **仓内能找到的实机留痕只有 1 条**（口径：`grep -rn "4135[0-9]0000\|413600000" --include=*.md .`
  排除 `.rks-agent/` 的命中）：`Document/avatar-container-entry-shape.md:93-94` 记着
  `item 413500000 6->3 refused as NotWearable` 与装扮栏内的 `0->3`。
  **另外 10 件的那 10 行日志，仓内任何 `.md` 都没有留痕**，只存在于 TaskContract 的对照表里。
- 生产注释与判据注释里的说法（「measured against a live client -- eleven costumes dragged onto the
  costume bar one at a time」）是**过去时的陈述**，没有说本轮做的，也**没有注日期、没有指向那 11 行日志存在哪里**。

**判断：措辞没有把证据说强，但可追溯性偏弱。** 关键在于——即使把「实机量得」整条拿掉，
**这 11 个数仍然被我亲自核过的另外两源独立支撑**（档案给名、上游枚举给号，逐行吻合），
所以**表里的数是对的这一点不依赖那条我无法复现的证据**。
因此这不构成阻塞，列为非阻塞观察第 5 条。

### 二、`ByName` 的第二个读者；有没有第三个被漏掉

**第二个读者 `PvfInitialEquipmentDefinitionSource`（建号起始装备）：行为逐值不变，我从档案侧独立证过。**
它按「槽名 → 物品号」成对读 `[create equipment list]`，槽名查不到就把 `pendingSlot` 清空、丢弃那一项。
补键之后任何 `avatar]` 名都会从「被丢弃」变成「真的发一件」——所以问题变成「创建表里有没有 `avatar]` 名」。
我把 `character/character.lst` 解出的 **14 个职业**（0..13）逐个 dump，
`[create equipment list]` 一律只有 `[weapon] / [coat] / [pants]`，**零个 `avatar]` 名**。
**建号行为不可能改变。** 既有判据 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
（本轮未改动）会在这一点被破坏时变红。

**有没有第三个读者？没有。** `grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs`
的 34 条命中里，`ByName` 的实际取值只出现在上述两处生产代码，其余是类声明、两个常量、注释与判据。
另外我把范围放宽扫了 `avatar]` 字面量与 `IEquipmentSlotDefinitionSource` 的注入点：
`PvfEntryLifecycleContentSource` 也用 `"avatar]"` 后缀，但它**直接读档案的 `[equipment type]`**、
**不查 `ByName`**，所以不受补键影响。

**但有一个「第三个下游消费点」执行方的反向表没有列出**，我自己扫 `WornSlotForAsync` 的 7 条命中时找到的：
`MoveItemSpaceSlice.cs:541-546`（`TakeOffAsync`）——脱下时若目标背包格已有东西，
会拿那件东西的 `WornSlotForAsync` 与被腾空的槽比较，不等就拒 `DisplacedCannotOccupySlot`。
补键之后这一处的**输入**变了（时装从 `null` 变成 `0..10`）。
我逐状态推演了它：**本次改动之前可达的状态，结果一个都没变**
（脱下普通装备到一个放着时装的背包格：`null ≠ 14` 与 `3 ≠ 14` 同样拒绝；
而槽 `0..10` 在改动前根本站不了东西，那些状态是本次授权变化才产生的）。
**所以 SC-4 不受影响**，但反向表少列了一处，记为非阻塞观察第 4 条。

### 三、`PutOnAsync` 那个论证成不成立；钉它的判据是否真钉住

**论证成立。** 我把它拆成四步逐步核，四步都有代码依据（不是引用注释）：

1. 「一件东西只能穿到它档案条目声明的那个槽」——`PutOnAsync:405` 的
   `slot.Value != command.DestinationSlotIndex` 即此，且是**唯一**入口。
2. 「装扮槽 `0..10` 只被那 11 个 `avatar]` 名声明」——我自己的穷尽扫描 ＋ 27 项字典可数。
3. 「所以交换两端都是时装」——由 1、2 直接推出。
4. 「退回的那一格刚才正站着到达件，同一个列表容得下时装」——
   `ItemSlotRuleService`：装扮列表 `0..209` 只收 `WorldClass == Avatar`；
   主背包 `AllowsQuickSlot` 排除 `KindAvatar = 8`（快捷栏 3..8 收不了时装）；
   且 `WornPieceMetadata` 给**所有**穿戴件同一条主背包带 `9..64`——
   **两件时装在主背包里共用同一条带**，所以退回必然合法。

**限定也写得准确**：注释明说只覆盖「移动能到达的状态」，绕过服务器写进去的行不在内；
并明确不得推广成「穿戴槽决定类别」，给了 24 号槽的现成反例。**没有把结论说过头。**

**钉它的判据真的钉住** —— 见 M3：加上那条判定后，全仓恰好红 1 条，就是它。

### 四、`MoveItemSpaceSlice.cs` 是否真的只改了注释

**是。** 见 SC-5 的 (d)：两侧同法剥掉全部 `//` 与 `///` 并去空行后 `diff` **无输出**，
各 393 行；且该文件**不含块注释、不含带 `//` 的字符串字面量**，所以这种剥法不会误伤。

---

## 非阻塞观察（5 条，均不影响任何一条 SC 的达成）

1. **`TheAvatarBagIsServedTests.cs:138-139` 的 XML 文档注释畸形**：
   `/// <summary>` 之后紧跟另一整行 `/// <summary>…</summary>`，外层 `<summary>` 从未闭合，
   随后的 `<remarks>` 被嵌进去。构建全绿（该判据项目未开 `GenerateDocumentationFile`），
   属排版缺陷，四轮评审未点到。
2. **`RefusedEquipIsRecordedWithItsTemplateTests.cs:99-115` 同一方法上挂了两段连续的 `<remarks>`**，
   内容近乎重复。同为排版/冗余，不影响语义（两段说的都是真话）。
3. **`AGENTS.md:689` 的标题层级是 `###` 而它前后同级内容是 `####`**（`:611`、`:733`），
   共同父节是 `:489` 的 `###`。后果：`:733` 那节「一处影响面超出时装的行为变化：普通装备的拖动」
   （**基线就有的、讲的是更早一轮副作用的内容**）现在在结构上挂到了「2026-09-08」这一节底下；
   `:620` 那句「结论见本节末尾的补记」也指不到 `:689`。
   第四轮评审记为 N-9 并按题面标准判为非阻塞，我同意——它是排版，内容本身仍然为真。
   **但它确实有让读者把 `:733` 误读成本件副作用的风险，建议下一件顺手把 `###` 改成 `####`。**
4. **变化清单的反向表少列一处**：`MoveItemSpaceSlice.cs:541-546`（`TakeOffAsync` 的
   `WornSlotForAsync(displaced)`）也是补键后输入发生变化的地方。
   清单的反向节自述范围是「扫 `ByName` 的每一个读者」，按那个范围它是完整的（2 个读者）；
   但 SC-8 要求「从代码出发独立扫」，而清单自己跑过 `WornSlotForAsync` 的 7 条命中却没把这一条写进表。
   **我逐状态核过：改动前可达的状态结果一个都没变，所以不是不实陈述，是枚举不全。**
5. **「实机量得」的可追溯性**：11 件里只有 1 件（`413500000 → 3`）的日志行在仓内有留痕；
   生产注释与判据注释未注明测量日期、也未指向那 11 行日志的存放处。
   槽号本身由上游枚举独立佐证（我亲自核过），所以结论可靠；
   但**若日后有人要复查「这 11 个数是怎么来的」，仓内只能查到 1 条**。

---

## 判定

**`pass`。**

SC-1 到 SC-8 **每一条都有本轮自己取得的当前证据**：判据 2361/2/0 退出码 0 跑了两次、
字节锚散列与基线相同、三源里的两源我亲自复现且第三源的缺失不影响结论、
`ByName` 的两个读者与建号档案我自己扫过、注释-only 用剥注释比对证明、
搜索集 8 条命令的命中数逐条复现、
以及三次变异（含我自己另加的 M3）证明 SC-1 的逐键钉住与 SC-5 的结论钉住都是真的。

上列 5 条观察全部属于排版、冗余、可追溯性与枚举完整性，
**没有一条会让读者对代码的实际行为或对下一件工作形成错判**，故不升级为阻塞。
