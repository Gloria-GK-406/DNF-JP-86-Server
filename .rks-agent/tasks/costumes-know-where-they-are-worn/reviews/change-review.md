---
kind: rks-review
verdict: approved
---

# 变更评审：时装知道自己穿在哪一格（scope = change）

评审方独立执行，**未采信执行方任何自评结论**；下面每一条的证据都是本次自己跑出来的。
基线 `80bdb1445a1e29d1b3bf871351391f4c79cc1380`，工作区 `costumes-know-where-they-are-worn`，未提交。

**本评审未修改仓库任何文件**，除本文件。一次变异核实用文件级备份 + `sha256sum` 还原并核过散列
（见「变异核实」一节），未使用任何 git 还原类命令。探针全部走仓内既有工具 `Tool/PvfProbe`
（只读档案、只打印），**没有新建也没有删除任何仓内文件**；核实见文末。

---

## 判定摘要

| | |
|---|---|
| Verdict | **changes_required** |
| 阻塞性发现 | **3 条**（B-1 / B-2 / B-3），全部属于「改了什么没有一条不漏地记下来」这一族 |
| 非阻塞观察 | **10 条**（N-1 … N-10） |
| 生产代码缺陷 | **0 条**。补的 11 个键、槽号、既有键值、`PutOnAsync` 的可执行语句，全部核过，无误 |

**核心判断：这次改动本身是对的，三个来源我逐条独立复现，一处不差；`PutOnAsync` 那个点名
的论证我逐步核过，成立且限定说得准确。挡住它的是记账——两处失效记载留在执行方自己动过的
那两个文件里（一处生产注释、一处判据名），第三处是 `AGENTS.md` 里关于这张表的三个错数。**

---

## A. 契约 SC-1..SC-8 逐条

### SC-1 `ByName` 有且只有那 11 个时装键，槽号一致，判据钉住，改错会红 — **达成**

```
$ sed -n '58,88p' Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
```

字典 27 项：新增 11 项 `[hat avatar]`=0 / `[hair avatar]`=1 / `[face avatar]`=2 /
`[coat avatar]`=3 / `[pants avatar]`=4 / `[shoes avatar]`=5 / `[breast avatar]`=6 /
`[waist avatar]`=7 / `[skin avatar]`=8 / `[aurora avatar]`=9 / `[weapon avatar]`=10；
其余 16 项（`[weapon]`=12 起）**逐字未动**——

```
$ git diff -- Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
@@ -34,6 +58,17 @@  ... 字典段 11 行 "+"，0 行 "-"
```

槽号与契约 Context 对照表逐行一致。「改错会红」由 M1 变异实测（见 G 节）。

### SC-2 时装拖到它自己的装扮槽能穿上，两端都落 — **达成**

`TheAvatarBagIsServedTests.A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`
断言 `Moved` + `ItemId` + 装扮栏槽 0 为空 + 装备栏槽 3 是它（两端都断）。
主背包那条 `A_costume_is_worn_out_of_the_bag_too` 只断了目的端（见 N-10）。

### SC-3 拖到别的装扮槽被拒为 `ItemCannotOccupySlot` 且带模板号 — **达成**

两条：`TheAvatarBagIsServedTests.A_costume_does_not_go_on_a_costume_slot_that_is_not_its_own`
（`ItemCannotOccupySlot` + `ItemId` + 目标槽仍为空）；
`RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_aimed_at_the_wrong_costume_slot_is_recorded_with_its_template_and_that_slot`
（整句比对日志行，`item 101500690 12->2 refused as ItemCannotOccupySlot`）。

### SC-4 非时装的穿戴行为逐值不变；原有键值一个未改 — **达成**

三条独立证据：

1. 字典 diff 零行 `-`（上）。
2. `MoveItemSpaceSlice.cs` 的**可执行语句与基线逐字符相同**——

```
$ git show 80bdb144:.../MoveItemSpaceSlice.cs | grep -v '^\s*///' > /tmp/base_slice.txt
$ grep -v '^\s*///' .../MoveItemSpaceSlice.cs > /tmp/cur_slice.txt
$ diff /tmp/base_slice.txt /tmp/cur_slice.txt      -> 无差异；两侧各 475 行
$ git diff -U0 -- .../MoveItemSpaceSlice.cs | grep -E '^[+-]' \
    | grep -v '^[+-][+-]' | grep -vcE '^[+-]\s*///'   -> 0
```

（这同时回答了 E 的第三小问：本轮**确实只改注释**。）

3. `ByName` 的第二个读者见 D 节：14 个职业的创建表零个 `avatar]` 名，我自己扫过。

### SC-5 `PutOnAsync` 的点名被做掉，结论/理由/证据写进注释且有判据钉住 — **达成，但被 B-2 污染**

结论「不需要加判定」，论证成立（逐步核证见 E 节），判据
`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list` 钉住且**非空真**
（M1 让它变红，见 G 节）。**但同一个文件里 `Nothing_may_be_worn_in_a_costume_slot`
仍以相反的命题命名并写在文档里（B-2）**，同一结论在同一文件里有两份互相矛盾的记载。

### SC-6 全量判据 ≥ 2335/2/0，退出码 0；字节锚 62 帧不变 — **达成**

我自己跑的：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo
Networking     0 失败 /   52 通过 / 0 跳过
Protocol       0 失败 /  585 通过 / 0 跳过
Pvf            0 失败 /   60 通过 / 0 跳过
Host           0 失败 /    6 通过 / 0 跳过
Persistence    0 失败 /  329 通过 / 0 跳过
Infrastructure 0 失败 /  259 通过 / 2 跳过
Application    0 失败 / 1070 通过 / 0 跳过
EXITCODE=0
```

合计 **2361 通过 / 2 跳过 / 0 失败**，与执行方所报一致，**+26** 相对基线。
逐项目对基线：Protocol 582→585（+3）、Infrastructure 236→259（+23），差额口径成立。
跑前确认无 `Dfo.Server.Host` 进程（`tasklist | grep -i Dfo` 无命中）。
**未复现既知偶发**（`ObjectDisposedException: 'SQLitePCL.sqlite3'`）。

字节锚：`Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs` **不在 `git diff --name-only` 里**，
且 Protocol.Tests 585 条全绿——编码器未被波及。

### SC-7 「`AuraSkinAvatar` 在武器之前」两处更正 + 量具说明书更新 — **达成（更正了三处，多于契约要的两处）**

```
$ grep -rni "武器之前|武器前|ahead of the weapon" --include=*.md --include=*.cs .
```

现存的活记载里已无该说法：`PvfEquipmentSlots.cs:29-32`、`NOTI-0x0002.md:691-694`、
`traces/a21-starting-gear-and-skills-20260826.md:83-84` 三处都就地更正。
唯一残留在 `.rks-agent/tasks/one-length-for-the-jewel-socket/reviews/change-review.md:139`，
那是历史评审引用当时原文，**不算活记载，不要求改**。
量具说明书新增 `### 2026-09-08` 一节。**但见 B-3：同一节上方未更正的部分。**

### SC-8 变化清单正向反向都报、从代码出发独立扫、声明的命令都真的敲下去 — **未达成**

见 B-1 / B-2 / B-3 / N-6。

---

## B. 三个来源是否真的互证 —— **是，我自己全部复现，一处不差**

### 来源三：上游枚举（我自己去 `E:/Project/DNF/ServerS4A21` 读的，只读）

`Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs:6-42`：

```
HatAvatar = 0, HairAvatar = 1, FaceAvatar = 2, CoatAvatar = 3, PantsAvatar = 4,
ShoesAvatar = 5, BreastAvatar = 6, WaistAvatar = 7, SkinAvatar = 8,
AuroraAvatar = 9, WeaponAvatar = 10, AuraSkinAvatar = 11,
AuroraIllusionAvatar = AuraSkinAvatar, Weapon = 12, ...
```

**11 个名字与序号与契约表逐行吻合**；`WeaponAvatar = 10` 在 `AuraSkinAvatar = 11` **之前**，
所以「A21 在武器之前插了 AuraSkinAvatar」确实是错的，本轮的更正**方向正确**。

同文件 `EquipmentTypeInfo.TextToType` 另给出 `AuraSkinAvatar` 的**四个**拼法
（`[aura skin avatar]` / `[aurora illusion avatar]` / `[aurora skin avatar]` / `[aurora change avatar]`），
这四个在本档案里**一个都不出现**（下面的扫描证明），所以「槽 11 无键可补」这个结论
在四个别名下都成立——**比执行方写的还硬一点**。

### 来源二：档案读出（我自己查了 **12** 个模板，不是 3 个）

```
$ PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- lst equipment/equipment.lst <id>
413550000 -> [hat avatar]      413560000 -> [hair avatar]    413570000 -> [face avatar]
413500000 -> [coat avatar]     413510000 -> [pants avatar]   413540000 -> [shoes avatar]
413520000 -> [breast avatar]   413530000 -> [waist avatar]   413580000 -> [skin avatar]
413590000 -> [aurora avatar]   413600000 -> [weapon avatar]
101500690 -> [coat avatar]     <- 判据里当「另一件时装」用的那个模板，确实也是上衣时装
```

**11/11 与契约表逐行吻合。**

### 来源一：实机量得 —— 说法**没有被写得比证据强**，但证据比 issue #11 要的弱

- **没有被写过头**：契约、类注释、判据文档都写「2026-09-07，所有者操作」，
  本轮的清单第一节只列了**本轮做的三次档案扫描**，**没有一处声称本轮做过实机**。
  时间、施行者、来源层次（槽号=实机 / 键名=档案 / 枚举=第三方佐证）都标得清楚。**这一条通过。**
- **但**：issue #11 第一条接受条件原文是「每条附上量到它的那行日志」。
  仓里能查到的是上一件契约里的**转写表**
  （`.rks-agent/tasks/the-avatar-bag-is-a-container-too/TaskContract.md:78-89`），
  以及上衣那格的三次散文转写（`6->3`、`0->3`、`185->3`）。
  **11 条原始日志行有 10 条在仓里不存在。** 见 N-7。
- 这不影响本件的可信度，因为**槽号在本仓另有一条完全独立的支撑**：
  「模板 → `[equipment type]`」是我从档案读的，「`[equipment type]` → 槽号」是我从上游枚举读的，
  两者相乘就给出全部 11 个数，与实机表一致。**三源互证成立。**

---

## C. 槽 11 的空缺 —— **结论成立，数字我自己复现，一位不差**

我用仓内既有工具重跑了穷尽扫描（**不是核 `AGENTS.md` 的表，是重扫档案**）：

```
$ PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- values equipment/equipment.lst "[equipment type]"
first token after [equipment type] over 118479 files:
  17668 [weapon]        8391 [coat avatar]    8325 [hat avatar]     8194 [pants avatar]
   8070 [shoes avatar]  7929 [hair avatar]    7565 [breast avatar]  7028 [face avatar]
   7000 [waist avatar]  4114 [coat]           4066 [pants]          3956 [shoes]
   3841 [shoulder]      3822 [waist]          2954 [skin avatar]    2848 [ring]
   2755 [amulet]        2745 [wrist]          2206 [support]        1796 [title name]
   1333 [magic stone]    707 [creature]        449 [weapon avatar]   423 [aurora avatar]
     65 [artifact red]    50 [charm]            47 [artifact blue]    47 [flag]
     31 [artifact green]  28 [name tag]         26 [support weapon]
```

**口径与复算（读者可当场重算）：**

- 扫描面 = `equipment/equipment.lst` 解析出的 118479 个模板文件，逐个取**第一处**
  `[equipment type]` 后的第一个 token。输出里**没有 `(absent)` 一行**，即 118479 个全部读到了值。
- 31 行数值相加 = **118479**，与扫描面完全相等 → 没有模板被漏计。
- 以 `avatar]` 结尾的名字 **恰好 11 个**，无第 12 个（`[aura skin avatar]` 等四个别名零命中）。
- 11 项相加：8391+8325+8194+8070+7929+7565+7028+7000+2954+449+423 = **66328**。

**三个数（118479 / 11 / 66328）全部独立复现。「无键可补」成立。**
「这不是本轮的新发现，`AGENTS.md` 早有同表」这个说法也成立——`AGENTS.md:649-667` 的
11 行时装数与我扫出来的逐项相同。**记法诚实。**
（**但那张表自己已经因为本轮改动而变成错的了，见 B-3。**）

---

## D. `ByName` 的第二个读者 —— **无行为变化，我自己验证过；且没有第三个读者**

### 有几个读者：**恰好两个**，不多不少

```
$ grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs
Server/src/.../PvfEquipmentSlotDefinitionSource.cs:76        <- 读者一（一件装备问「我穿在哪」）
Server/src/.../PvfInitialEquipmentDefinitionSource.cs:122    <- 读者二（建号起始装备）
Server/src/.../PvfEquipmentSlots.cs:56                       <- 定义
Server/src/.../MoveItemSpaceSlice.cs:648                     <- 注释（见 B-1）
（其余 31 处全在 Server/tests）
```

`FirstWornSlot` / `LastWornSlot` 在**生产里零消费者**（`grep` 的 5 处 = 2 处声明 + 3 处判据），
所以它们不构成第三个读者。**没有第三个读者被漏掉。**

### 既有判据是不是真的覆盖它：**是，而且是补键之后才真的覆盖**

`PvfEquipmentSlotsTests.No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
（`PvfEquipmentSlotsTests.cs:92-113`）遍历 job 0..31，把 `GetAsync` 吐出的所有
`grant.SlotIndex` 收进集合，断言恰好等于 `{ByName["[weapon]"], ByName["[coat]"], ByName["[pants]"]}`。

- **补键之前它答不了这个问题**：那时 `avatar]` 名在读取时被丢弃（`PvfInitialEquipmentDefinitionSource.cs:122-124`
  查不到就把 `pendingSlot` 置空），创建表里有没有它，这条判据都是绿的。
  Spec RQ-005 对这一点的描述准确。
- **补键之后它真的钉住了**：只要创建表里出现任何 `avatar]` 名，就会多出一个 0..10 的
  `SlotIndex`，集合不再相等，判据变红。**它现在是绿的 ⇒ 零个 `avatar]` 名。**
- 覆盖面核过：`character/character.lst` 只有 **14 个职业，id 0..13**，全部 `< 32`，
  循环没有漏掉任何职业。

### 我自己去档案查的（不依赖判据）

```
$ dotnet run --project Tool/PvfProbe -- dump character/character.lst 200
0 Swordman  1 Fighter  2 Gunner  3 Mage  4 Priest  5 ATGunner  6 Thief
7 ATFighter 8 ATMage   9 DSSwordman 10 CreatorMage 11 ATSwordman 12 Knight 13 DemonicLancer
$ 逐个 dump .chr，取 [create equipment list] ... [/create equipment list]
```

**14 个职业，无一例外**：`[weapon] N [coat] 10400 [pants] 12400`。
**零个 `avatar]` 名。建号行为逐值不变。** 清单 R-1 的记法准确。

---

## E. `PutOnAsync` 那个点名 —— **论证成立，限定说得准确，判据非空真**

结论「不需要加判定」。我把论证拆成四步逐步核：

| 论证的一步 | 核证 | 结论 |
|---|---|---|
| 「一件东西只能穿到它档案条目声明的那个槽」 | `MoveItemSpaceSlice.cs:406-408`：`slot.Value != command.DestinationSlotIndex` → `ItemCannotOccupySlot` | **成立** |
| 「装扮槽 `0..10` 只被那 11 个 `avatar]` 名声明」 | `ByName` 里 0..10 恰好是那 11 个键；**补键之后仍然可核**，且由 `PvfEquipmentSlotsTests.Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged` 钉住（时装名 ⊆ 0..10 且铺满 0..10，非时装名 ⊆ 12..24，两段无交集） | **成立，且仍可核** |
| 「两端都是时装 ⇒ 源列表收得回」（装扮栏） | `ItemSlotRuleService.CanOccupySlot`（`:57-59`）：Avatar 列表 = `0..209` **且** `WorldClass == Avatar`。关键的一环执行方没写出来但**确实成立**：`PvfEntryLifecycleContentSource.ClassifyEquipmentType`（`:245-253`）把 `WorldClass` **就用同一个 `[equipment type]` 是否以 `avatar]` 结尾**判出来——所以「声明 0..10」与「`WorldClass == Avatar`」是**同一件事的两种问法**，不是两个需要互相担保的事实 | **成立** |
| 「时装在主背包里必在装备段」 | `CanOccupyMainSlot`（`:64-73`）= 自身 band 或快捷栏 3..8；`AllowsQuickSlot`（`:84-86`）把 `KindAvatar=8` 排除；`WornPieceMetadata`（`:198-209`）给**所有**装备索引模板同一 band **9..64**，与世界类别无关 → 任意时装可落 9..64 的任意格，且只能落那里 | **成立** |

**限定「只覆盖移动能到达的状态」：该说，而且说对了，没说过头。**
注释写的「存储里可以有一件时装待在移动放不进去的格子……那是存储与规则不一致，不是这次交换造成的」
正是上面第四步的补集（主背包 0..2 与 65+ 对时装不可达），**限定的边界与代码边界重合**。

**「不得推广成『穿戴槽决定类别』」那段仍然正确**：`ByName` 把 `[earring]`/`[support weapon]`/`[creature]`
同映 24，反例现成（顺带一提：上游枚举把 `Creature` 放在 25，这个不一致是既有悬案，
`PvfEquipmentSlots.cs:46-53` 已记，**不属本件**）。

**判据不是空真**：M1（把 `[coat avatar]` 从 3 改成 4）让
`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list` 变红，说明它真的走通了一次交换。

**「本轮只改注释」核实**：见 SC-4 第 2 条，可执行语句逐字符相同，`diff` 无输出。

**唯一的问题是 B-2**：同一个文件里 `Nothing_may_be_worn_in_a_costume_slot` 仍在说反话。

---

## F. 既有判据有没有为迁就实现而被改写 —— **没有；那次整体删除合理，替代的四条确实更强**

四条改写逐条核：

| 用例 | 我的核验 | 判定 |
|---|---|---|
| `PvfEquipmentSlotsTests` 表范围判据 | 原断言 `All(ByName.Values, InRange(12,24))` 在补键后**必然为假**，不改就是攒红。新断言**更强**：不只说「时装名在 0..10」，还说「铺满 0..10，一格不缺」+「非时装名仍在 12..24」+ 两常量仍为 12/24。**没有放宽** | **合理** |
| `The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it` → `..._and_names_its_worn_slot` | `Assert.Null(...)` → `Assert.Equal(3, ...)`。**从「没有答案」变成「答案是 3」，是收紧不是放宽**。`WorldClass == Avatar` 那条原断言原样保留 | **合理** |
| `An_avatar_refused_as_unwearable_...` → `An_avatar_aimed_at_the_wrong_costume_slot_...` | 仍是整句日志比对，只把 `NotWearable` 换成 `ItemCannotOccupySlot`；`MoveItemSpaceResponse.NotMovable` 那条断言未动。它保护的性质（**读到物品之后的拒绝说得出是哪一件**）逐字保留 | **合理** |
| `Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` **整体删除** | 该用例断言 `NotWearable`，是本件要推翻的那件事本身，**留着必红**。替代的四条：①`A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`（`Moved` + `ItemId` + **两端都断**）②`A_costume_does_not_go_on_a_costume_slot_that_is_not_its_own`（错槽被拒 + 目标仍空）③`A_costume_is_worn_out_of_the_bag_too`（第二条源列表）④`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`（交换）。被删的那条只断 1 个状态 + 1 个 id；四条合计断 11 个状态 | **合理，替代确实更强** |

**「没有任何断言被放宽或删除」**这个说法，我逐条核过，**成立**。

---

## G. 判据质量

### 恒真断言：**没有**

23 条逐条看过：11 条 `A_costume_is_worn_in_the_slot_it_was_measured_in`（走真实档案，
`PvfEquipmentSlotDefinitionSource.GetAsync(itemId)` → 槽号，**从模板出发而不是断言字典**，
符合 AC-001；同时对照 `ByName[equipmentType]`）；11 条
`A_costume_is_what_the_archive_calls_an_avatar`（对 11 个真模板断 `WorldClass == Avatar`）；
1 条 `No_name_reaches_the_twelfth_costume_slot`。**没有一条恒真。**

### `_ = slot; _ = equipmentType;` 是不是在借 `MemberData` 凑数

**不是凑数，但是借。** 那条 Theory 对 11 个真模板各断一次 `WorldClass == Avatar`，
11 次断言各有内容，不是同一个命题被复制 11 遍的假象。
但它只用得上三列里的一列，却挂在三列的 `MemberData` 上，**用两个 discard 把另外两列丢掉**。
换成 `TheoryData<int>`（或把 11 个 id 单列）能说同一件事而不需要 discard。
**非阻塞（N-4）。** 计数影响：这 11 条把新增数从 12 抬到 23，读者若把「23 条」当成
「23 个不同命题」会高估——但清单第六节写明了口径（23 = 11+11+1，由文件可数），**没有误导**。

### `No_name_reaches_the_twelfth_costume_slot` 断言表而不断言档案 —— **取舍站得住，但那条判据本身几乎全余**

- **诚实性通过**：它的注释自己写「**If a twelfth name ever appears, this is the case that will
  still be claiming there are eleven**」，把局限说到了顶。
  「asserts the consequence of that search rather than repeating it」是准确措辞，不是掩饰。
- **取舍合理**：我自己跑那次扫描要逐个打开 118479 个模板，放进每次都跑的套件里不合算；
  AS-004 也明写这是「当前档案的事实，不是协议的事实」。
- **但**：它的两条断言 `Assert.Equal(11, costumes.Length)` 与
  `DoesNotContain(pair.Value == 11)` **已经被** `PvfEquipmentSlotsTests.Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged`
  完全蕴含（后者断 11 条且 `InRange(0,10)`，`InRange(0,10)` 直接排除 11）。
  **它没有增加任何约束。** 见 N-3。
- 净效果：**AS-004（无第 12 种）在判据里零保护**，只有一次性扫描 + 注释。
  这是可接受的（我本轮自己重扫复现了），但**成果里应写明这一点**，
  不要让「23 条判据」给人以它被钉住的印象。

### 变异核实抽核（M1，我自己做的）

```
$ cp .../PvfEquipmentSlots.cs $SCR/PvfEquipmentSlots.cs.bak
$ sha256sum .../PvfEquipmentSlots.cs
0d94e163b7549f8461231e28ca4e69e3b42189b446ff556d38b539221eaaeeb9
$ sed -i 's/\["\[coat avatar\]"\] = 3,/["[coat avatar]"] = 4,/' .../PvfEquipmentSlots.cs
$ dotnet test Dfo.Server.Infrastructure.Tests.csproj   -> 失败 2 / 通过 257 / 跳过 2
$ dotnet test Dfo.Server.Protocol.Tests.csproj         -> 失败 4 / 通过 581
```

红的 **6 条**（与执行方所报数字一致）：

```
PvfEquipmentSlotsTests.Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged
CostumeSlotsWereMeasuredTests.A_costume_is_worn_in_the_slot_it_was_measured_in(413500000, 3, "[coat avatar]")
TheAvatarBagIsServedTests.A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot
TheAvatarBagIsServedTests.A_costume_is_worn_out_of_the_bag_too
TheAvatarBagIsServedTests.What_comes_off_a_costume_slot_goes_back_into_the_avatar_list
RefusedEquipIsRecordedWithItsTemplateTests.The_archive_calls_this_template_an_avatar_and_names_its_worn_slot
```

还原：

```
$ cp $SCR/PvfEquipmentSlots.cs.bak .../PvfEquipmentSlots.cs
$ sha256sum -c $SCR/slots.sha256   ->  OK
$ git status --porcelain           ->  10 M + 2 ??（与变异前完全相同）
```

**M1 的结论成立**：改一个数字，**四个**判据文件同时变红，说明这些数被三条不同的路读到。
（执行方写的是「三个判据文件」——见 N-5。）

---

## H. 整体

| 项 | 核验 | 结论 |
|---|---|---|
| 分层 | 改动只落在 `Infrastructure/Content`（一个内部静态字典）与 Protocol 的一段注释。新判据文件在 `Infrastructure.Tests`，只引 `Abstractions.Content` + `Infrastructure.Content` | **未越层** |
| 切片架构 | `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests`（`Application.Tests`）1070 条全绿 | **仍被守住** |
| 新增/改动协议帧 | 生产侧只有字典字面量与注释；`git diff --numstat` 里没有任何 codec / message / payload 文件 | **零** |
| 字节锚 | `SelectCharacterAcceptedTranscriptTests.cs` 不在 diff 中，Protocol.Tests 全绿 | **未变** |
| `TreatWarningsAsErrors` / CS9113 | `Server/Directory.Build.props:6` 开启；全量构建 + 测试退出码 0 | **通过** |
| 新通电的路 | `GameInventoryDelivery.CarriesAppearanceBlock`（`:166-172`）对 `(Equipment, kind 8)` 答 `true`；`StateSynchronizationPayloadCodec.Entry.cs:162-163` 对 NOTI-2 的 kind 8 有守卫。**两条路本轮一字未改**，但第一次能被真时装走到——清单第七节已写明，**记法准确**。我另核了「穿上会不会丢 `AvatarDetail`」：`ItemContainers.WriteOneAsync` 的注释与 `SqliteItemDomainStore.FillAsync:601-606` 说明六项事实按**物品身份**随行而非随写入携带，所以移动不丢 | **无新缺陷** |

---

# 阻塞性发现

## B-1（阻塞）`MoveItemSpaceSlice.cs:648` 的生产注释仍在陈述被本轮推翻的前提

**错在哪**

`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:647-651`：

```csharp
// `{ItemId}` goes in beside the two ends the request already named. That pair is what a
// slot map can be read from: `PvfEquipmentSlots.ByName` carries no name ending in
// `avatar]`, and template 101500690 -- which the archive declares under `[coat avatar]` --
// was seen reaching `PutOnAsync`, getting nothing back from the archive and being refused
// as `NotWearable`, ...
```

`ByName` 现在**恰好带着 11 个以 `avatar]` 结尾的名字**；模板 101500690 现在**不会**被拒为
`NotWearable`——本轮的 `An_avatar_aimed_at_the_wrong_costume_slot_...` 正是把这条日志断言
从 `NotWearable` 改成了 `ItemCannotOccupySlot`。**这段注释的两个事实句都已为假。**

**影响哪条要求 / 实际后果**

- **SC-8**（「正向与反向都报、从代码出发独立扫」）与 **RQ-009**。
- 清单 S-3 声称 `MoveItemSpaceSlice.cs` 的注释改动就是 `PutOnAsync` 那一段；
  正向表下面写「`PutOnAsync` 的可执行语句一行未改」——**同一个文件里另一段注释失效了，没被记**。
- 这段是「量具怎么用」那条路的说明。下一个来量槽号的人读到
  「`ByName` carries no name ending in `avatar]`」会以为量具还在原状态，
  而 `AGENTS.md` 新加的 2026-09-08 一节说的正相反。**两处活记载互相打架。**

**证据（这一条尤其能说明失败模式）**

执行方**声明并执行过**能命中它的那条命令：

```
$ grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs   # 我实测 35 命中
Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:648: ...
```

清单第一节列了这条命令，**但没有写命中数，也没有对命中做处置**——
正是契约 SC-8 点名的第二种形态：「范围写对了但没照它执行」。

**要做什么**：就地更正该段（按本仓惯例可「原文留记 + 加注」），或删去已失效的两句事实陈述。

---

## B-2（阻塞）`TheAvatarBagIsServedTests` 里留着一条用例，其**名字与文档正好是本轮交付的反面**

**错在哪**

`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs:240-262`：

```csharp
/// <summary>
/// Nothing is worn in a costume slot, so nothing is displaced out of one.            // :241
/// </summary>
/// <remarks>
/// ... nothing can be: a piece goes on the slot its archive
/// entry names, and the lowest slot <c>PvfEquipmentSlots.ByName</c> names is twelve.  // :246
/// This asserts that emptiness at the only place it can be seen from here ...
[Theory] [InlineData(OrdinaryTemplate)] [InlineData(CostumeTemplate)]
public async Task Nothing_may_be_worn_in_a_costume_slot(int template)                 // :259
```

三处都已为假：`ByName` 最低命名的槽现在是 **0**；装扮槽上**能**站住东西；
「nothing is displaced out of one」正是本轮新判据
`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list` 证伪的那句。

**它仍然是绿的，纯属巧合**：`AvatarSlot = 0`（`:52`），两个模板是
`OrdinaryTemplate = 10018`（槽 14）与 `CostumeTemplate = 413500000`（`[coat avatar]` → 槽 **3**），
两个都 ≠ 0，所以还是 `ItemCannotOccupySlot`。**判据没红，命题已假。**

**影响哪条要求 / 实际后果**

- **SC-5**：契约要求「结论、理由、证据都写进去」。现在同一个文件里
  `A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`（`:150` 起）说时装能穿进装扮槽，
  一百行之后 `Nothing_may_be_worn_in_a_costume_slot` 说不能。**结论有两份互相矛盾的记载。**
- **SC-8** 与清单 T-1..T-3。T 节自称「四条既有用例被改，跨三个文件，
  **每一条都是钉着「时装没有穿戴槽」这个前提的**」——**这第五条是所有里面把那个前提写得最直白的
  （它的 summary 就是那句话），却没被扫到。**
- 一条名字说反话的判据，比没有判据更糟——它会被当成「这条规则还在」的依据。

**要做什么**：改名 + 改文档（例如改成「一件东西不能穿到它自己没声明的槽」，
这正是它今天实际断言的东西），或与 `A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name`
（`:272` 起）合并；并把这次改写按契约要求「单独说明它对应哪一条」补进清单 T 节。

---

## B-3（阻塞）`AGENTS.md` 的时装一节：六处现在时假陈述 + 三个关于这张表的错数，全部未更正

**错在哪**（行号为当前工作区）

| 行 | 原文 | 现状 |
|---:|---|---|
| 566 | `- **时装取得下、穿不上。** 见下面「时装」一节。` | 穿得上了 |
| 611 | `#### 时装：种类已经对齐，但仍然穿不上` | 小节标题本身已假 |
| 618 | `**穿不上的原因不是没写代码，是槽号一格都没有出处。**` | 已假 |
| **619** | `` `PvfEquipmentSlots.ByName` 的 **16 个键**里以 `avatar]` 结尾的是 **0 个** `` | **27 个键，11 个 `avatar]` 键** |
| 623 | `**没有给这张表补键，因为补键就等于写下一个没有出处的数。**` | 已补 |
| 625 | `**槽号出处找过三处，三处都不给。**` | 已假（第四处给了） |
| **642** | `` **`ByName` 认不出的类型名：17 个，覆盖 66,596 个模板。** `` | **6 个名字 / 268 个模板** |
| 649-667 | 那张表把 11 个时装名逐行列在「认不出的名字」下，末列写「是不是时装名 = 是」 | **11 行现在全部认得出** |
| **669** | `复算：... 两数相加 **66,596**；行数 11 ＋ 6 ＝ **17**。` | 分项数仍对，**两个合计与行数已假** |
| **672** | `` **`ByName` 的 **16 个键**里有两个在这次扫描中一次都没被用到** `` | **27 个键** |

新加的 `### 2026-09-08` 一节（683 起）确实写了「时装现在能穿上」，
**但它在 611-672 的下方，且没有回头撤销上面任何一句**；
`AGENTS.md` 本身对同一件事的另一句（「那张表里出现哪些名字没有测过」）用的正是
「**这句已作废（2026-09-08）**」的就地更正体例——
**同一个改动、同一个文件里，这个体例被选择性地用了一次。**

**影响哪条要求 / 实际后果**

- **SC-8** 的末句原文：「**另：改完一张表要回头数一遍行数**（上一件的 R-7 随被删小节丢失而表头仍说七条）」。
  本轮往 `ByName` 加了 11 行，**没有回头数**——619 / 672 的「16 个键」和 642 / 669 的
  「17 个 / 66,596」都是这张表的派生计数，**三处全错**。这是契约点名要防的**同一个错法**。
- **SC-7** 后半「`AGENTS.md` 的量具说明书按「表已量全」更新」——新节加了，但同一节上游的前提未撤。
- 清单第五节声称「**本轮推翻的说法，共 5 处**」（C-1..C-5），**上面这一批一条都不在其中**。
- `AGENTS.md` 是本仓交给下一个 agent 的定位文档。只读到 611-672 的读者会得出
  「时装穿不上、表里没有 avatar 键、认不出 17 个名字」——**三条全错，
  而且其中两条是可以拿去当依据重算的数**。

**证据**

```
$ grep -n "时装取得下、穿不上|种类已经对齐，但仍然穿不上|的 16 个键|认不出的类型名：17 个|行数 11 ＋ 6|66,596" AGENTS.md
566 / 611 / 619 / 642 / 669 / 672      <- 全部在 git diff 的改动块之外
$ git diff AGENTS.md    ->  只有一个块（674-694 区间，+18 / -1）
```

本轮实扫的真值（我自己跑的，见 C 节）：认不出的名字 = `[artifact red]` 65、`[charm]` 50、
`[artifact blue]` 47、`[flag]` 47、`[artifact green]` 31、`[name tag]` 28，
**6 个名字，相加 268 个模板**。

**要做什么**：把 566 / 611 / 618 / 619 / 623 / 625 / 642 / 649-667 / 669 / 672 按本仓
「原文留记 + 就地加注作废」体例处理，并把「17 → 6」「66,596 → 268」「16 个键 → 27 个键」
三个数改对或标注作废；然后把这一批补进清单第五节，并更正「共 5 处」这个计数。

---

# 非阻塞观察

**N-1　`TheAvatarBagIsServedTests.cs:138-139` XML 文档畸形。**

```
    /// <summary>
    /// <summary>A costume is worn out of the avatar list, onto the slot the archive names for it.</summary>
    /// <remarks>
```

外层 `<summary>` 从未闭合，内层又嵌了一个完整的 `<summary>`，整段 `<remarks>` 落在外层内部。
是改写时把 `/// </summary>` 那一行替换掉留下的残留。测试项目未开 `GenerateDocumentationFile`，
所以 CS1570 不触发、构建通过，但它是 diff 里的编辑残留。

**N-2　`RefusedEquipIsRecordedWithItsTemplateTests.cs` 第一个用例挂了两个 `<remarks>`**（约 `:88-104`）。
应合并成一个。

**N-3　`No_name_reaches_the_twelfth_costume_slot` 的两条断言被另一条判据完全蕴含。**
`PvfEquipmentSlotsTests.Every_name_is_a_costume_slot_or_a_worn_one_and_the_worn_band_is_unchanged`
已断「11 条时装名」且「值 `InRange(0,10)`」，后者直接排除值 11。
所以这条新判据**不增加任何约束**。它的注释是诚实的（明写自己不重跑档案扫描），取舍也合理，
但**AS-004「无第 12 种」在判据里实际上零保护**——建议在完成证据里明说。

**N-4　`A_costume_is_what_the_archive_calls_an_avatar` 借了三列 `MemberData` 只用一列**
（`_ = slot; _ = equipmentType;`，`CostumeSlotsWereMeasuredTests.cs:78-79`）。
断言本身有内容（11 个真模板各断一次 `WorldClass == Avatar`），**不是凑数**，
但两个 discard 是设计味道；换 `TheoryData<int>` 能说同一件事。

**N-5　清单 M1 行的文件计数少了一个。**
写「**6** | **三个判据文件**都有」，而同一格的枚举里点名了**四个**文件；
我复现的 6 条红也确实分布在四个文件里（`PvfEquipmentSlotsTests`、`CostumeSlotsWereMeasuredTests`、
`RefusedEquipIsRecordedWithItsTemplateTests`、`TheAvatarBagIsServedTests`）。
与 B-3 同族的计数漂移，量级小得多。

**N-6　声明的 8 条搜索命令里，2 条没有写命中数。**
`.md` 侧 5 条全有，我逐条复现**全部吻合**（3 / 2 / 9 / 4 / 3）。
`.cs` 侧 3 条只有第一条有（5，我复现吻合）；
`grep -rn "PvfEquipmentSlots" ...`（我实测 **35**）与 `grep -rn "WornSlotForAsync" ...`（我实测 **7**）
**没有命中数**。SC-8 原文要求「声明的每一条命令都要真的敲下去，**并把命中数写进清单**」。
这两条里的第一条正是本该抓住 B-1 的那条。

**N-7　「实机量得」的原始日志在仓里只存三条转写，不是十一条。**
仓内可查到的是上一件契约的转写表
（`.rks-agent/tasks/the-avatar-bag-is-a-container-too/TaskContract.md:78-89`）与上衣那格的三次散文
（`6->3`、`0->3`、`185->3`）。issue #11 第一条接受条件原文要「每条附上量到它的那行日志」。
**本轮的措辞没有写过头**（时间、施行者、来源层次都标明，且未声称本轮做过实机），
所以不作为阻塞；但完成证据里宜写明「原始日志未归档，11 个数在本仓的可复算支撑是
档案键名 × 上游枚举」——这两者我本轮都独立复现过。

**N-8　两处「时装仍然穿不上」留在别处，但都在日期化的历史注里。**
`Document/avatar-container-entry-shape.md:102`（「2026-09-07 已补：…时装仍然穿不上」）与
`protocol/game/server-to-client/NOTI-0x000D.md:423`（引用块，标着「2026-09-07 更正」）。
两者都写在明确标日期的更正块内部，读作历史标注而非当前断言，
且前者相邻的条目已获 2026-09-08 加注。**属边界情形，未升级为阻塞**；若一并加注更干净。

**N-9　`AGENTS.md` 新节的标题层级把后文重挂了。**
`### 2026-09-08：那 11 个时装槽已经量完，表已填上`（`:683`）用的是 `###`，
而它前后的 `#### 时装…`（`:611`）与 `#### 一处影响面超出时装的行为变化`（`:727`）都是 `####`，
共同的父节是 `### 物品：有身份的东西，和它在哪，是两张表`（`:489`）。
插入 `###` 等于**关掉了「物品」这一节**，`:727` 起的内容因此挂到了「2026-09-08」下面。应为 `####`。

**N-10　`A_costume_is_worn_out_of_the_bag_too` 只断目的端。**
它断 `Moved` 与装备栏槽 3 是它，**没断主背包源格被清空**。
姊妹条 `A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot` 两端都断了；
Spec 对第二条源列表也只要求「同一件事从主背包出发也要有一条」，所以不算缺项，
补一行 `Assert.Null(await side.ReadAsync(Main, BagSlot))` 会让它与姊妹条等强。

---

## 我实际跑过的命令

```
tasklist | grep -i "Dfo"                                    # 确认无 Host 进程占用
git status --porcelain / git log --oneline -3 / git diff --stat / git diff --numstat
git diff -- <每个改动文件>                                   # 逐文件读 diff
git show 80bdb144:.../MoveItemSpaceSlice.cs | grep -v '^\s*///' > /tmp/base_slice.txt
grep -v '^\s*///' .../MoveItemSpaceSlice.cs > /tmp/cur_slice.txt
diff /tmp/base_slice.txt /tmp/cur_slice.txt                 # 无差异（475 vs 475 行）
git diff -U0 -- .../MoveItemSpaceSlice.cs | grep -E '^[+-]' | grep -v '^[+-][+-]' | grep -vcE '^[+-]\s*///'   # 0

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo                # 2361 / 2 / 0，退出码 0

# 上游（只读）
find E:/Project/DNF/ServerS4A21 -iname "EquipmentType.cs"
sed -n '1,80p' E:/Project/DNF/ServerS4A21/Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs

# 档案（仓内既有工具 Tool/PvfProbe，只读只打印）
PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- values equipment/equipment.lst "[equipment type]"
PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- lst equipment/equipment.lst <12 个模板号>
PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- dump character/character.lst 200
PVF_ARCHIVE_PATH=... dotnet run --project Tool/PvfProbe -- dump character/<14 个 .chr> 200000

# 源码核证
cat -n Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
cat -n Server/src/Dfo.Server.Infrastructure/Content/PvfInitialEquipmentDefinitionSource.cs
cat -n Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlotDefinitionSource.cs
cat -n Server/src/Dfo.Server.Application/Inventory/ItemMovementDomainService.cs
sed -n '1,90p'   Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs
sed -n '185,260p' Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
sed -n '250,470p' Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
sed -n '60,200p'  Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs
sed -n '95,175p'  Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs
cat -n Server/tests/Dfo.Server.Infrastructure.Tests/Content/CostumeSlotsWereMeasuredTests.cs
sed -n '80,140p'  Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentSlotsTests.cs
sed -n '130,290p' Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/TheAvatarBagIsServedTests.cs

# 搜索集复现
grep -rl "<5 个 .md 模式>" --include=*.md .                  # 3 / 2 / 9 / 4 / 3，全部吻合
grep -rn "FirstWornSlot\|LastWornSlot" Server/src Server/tests --include=*.cs   # 5
grep -rn "PvfEquipmentSlots"           Server/src Server/tests --include=*.cs   # 35
grep -rn "WornSlotForAsync"            Server/src Server/tests --include=*.cs   # 7
grep -rni "武器之前|武器前|ahead of the weapon" --include=*.md --include=*.cs .
grep -rn "NotWearable" --include=*.md . ; grep -rni "穿不上|穿戴槽|槽位映射|装扮槽" --include=*.md .
grep -rn "InventoryContainerType.Equipment" Server/src --include=*.cs
grep -rn "lowest slot|begin at twelve|0\.\.11|carries no name" Server/src Server/tests --include=*.cs
grep -n "^#\{1,6\} " AGENTS.md                               # 标题层级

# 变异核实 M1（文件级备份 + sha256，无任何 git 还原类命令）
cp .../PvfEquipmentSlots.cs $SCR/PvfEquipmentSlots.cs.bak
sha256sum .../PvfEquipmentSlots.cs | tee $SCR/slots.sha256
sed -i 's/\["\[coat avatar\]"\] = 3,/["[coat avatar]"] = 4,/' .../PvfEquipmentSlots.cs
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/...csproj   # 失败 2
dotnet test Server/tests/Dfo.Server.Protocol.Tests/...csproj         # 失败 4
cp $SCR/PvfEquipmentSlots.cs.bak .../PvfEquipmentSlots.cs
sha256sum -c $SCR/slots.sha256                               # OK
git status --porcelain                                       # 10 M + 2 ??，与变异前相同
```

**残留核实**：本评审没有在仓库内新建或删除任何文件。所有临时产物
（`testrun1.txt`、`eqtypes.txt`、`m1-infra.txt`、`m1-proto.txt`、`PvfEquipmentSlots.cs.bak`、
`slots.sha256`）都在会话 scratchpad 目录下，**不在仓库内**。
`Tool/PvfProbe` 是仓内既有工具，其 `bin/` `obj/` 在本次运行前已存在且被 gitignore；
`git status --porcelain` 输出与评审开始时**逐行相同**（10 个 `M` + 2 个 `??`）。
主检出、`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads` 全程未触碰；
`E:/Project/DNF/ServerS4A21` 只读。

---

## 放行条件

三条阻塞都在「记账」层，**不需要改任何生产逻辑，也不需要新增或修改任何断言的期望值**：

1. **B-1**：更正 `MoveItemSpaceSlice.cs:647-651` 那两句已失效的事实陈述。
2. **B-2**：给 `Nothing_may_be_worn_in_a_costume_slot` 改名并重写它的 summary/remarks
   （或与 `A_piece_may_not_be_worn_in_a_slot_its_archive_entry_does_not_name` 合并），
   并把这处改写按契约要求单列归因。
3. **B-3**：`AGENTS.md` 566 / 611 / 618 / 619 / 623 / 625 / 642 / 649-667 / 669 / 672
   逐处更正或加注作废，把三个错数（16 个键 ×2、17 个名字、66,596 个模板）改对，
   并顺手把 `:683` 的 `###` 降为 `####`（N-9）。
4. 把上述全部补进 `behavior-change-list.md`，并**重算第五节的「共 5 处」**；
   同时补上 N-6 那两条命令的命中数（35 / 7）。

改完请复跑全量判据（B-1/B-2/B-3 都不动可执行语句，预期仍是 **2361 / 2 / 0**）。

---
---

# 第二轮：整改后复审（独立评审方，fresh context）

**未采信执行方任何自评结论，也未采信第一轮的任何结论。** 第一轮标为「已核实为正确」的项目
我做了抽核而非全量重做（抽核项在文末列出）；三条阻塞与十条非阻塞逐条自己重新验。
**本轮同样未修改仓库任何文件**（除本文件的这一段追加）：没有做变异核实，没有新建或删除任何仓内文件；
临时文件（`base_slice.txt`、`cur_slice.txt`、测试输出）全部在会话 scratchpad 目录下。
复审开始与结束时 `git status --porcelain` 逐行相同：**10 个 `M` + 2 个 `??`**。
主检出与另两个 worktree 全程未触碰。

| | |
|---|---|
| Verdict | **changes_required** |
| 生产改动 | **仍然只有两处**，核过：字典 11 行、`MoveItemSpaceSlice.cs` 只改注释 |
| 判据 | **2361 通过 / 2 跳过 / 0 失败，退出码 0**，自己跑的，与执行方所报一致 |
| B-1 | **已解决**（新注释每一句我逐句核过，含「`NotWearable` 还留给谁」那句） |
| B-2 | **已解决**（断言确与基线逐字符相同；新名字/新文档与它实际断言的东西相符） |
| B-3 | **部分解决**——三个数（27 / 6 / 268）全对，我自己数过；**但第一轮点名的 10 处里还剩 2 处未动，且是最常被读到的那两处** |
| 本轮整改新引入的实质性不实 | **0 条** |
| 新发现的阻塞 | **1 条（B-4）**，不是本轮引入的，是**两轮都漏掉的同族第六处** |
| 仍未处置的非阻塞 | N-1 / N-2 / N-3 / N-4 / N-5 / N-7 / N-8 / N-9 / N-10（**它们本来就是非阻塞，不构成挡板**） |

**一句话：这次整改做对了绝大部分，B-1 与 B-2 干净利落；挡住它的是同一族记账的最后三处——
`AGENTS.md` 的一条总览条目与一个小节标题至今仍在说「时装穿不上」，
外加一条判据的 `<summary>` 仍在把「档案不给这件时装槽号」当作它的前提，
而它三行之下断言的就是「槽号是 3」。**

---

## 一、判据复跑（自己跑的）

跑前：`tasklist | grep -i -E "Dfo|Server.Host"` → 无命中，无 `Dfo.Server.Host` 进程。

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo
Protocol       0 失败 /  585 通过 / 0 跳过
Pvf            0 失败 /   60 通过 / 0 跳过
Networking     0 失败 /   52 通过 / 0 跳过
Host           0 失败 /    6 通过 / 0 跳过
Persistence    0 失败 /  329 通过 / 0 跳过
Infrastructure 0 失败 /  259 通过 / 2 跳过
Application    0 失败 / 1070 通过 / 0 跳过
[exited with code 0]
```

合计 **2361 / 2 / 0**，相对基线 `80bdb144`（2335 / 2 / 0）**+26**。
**未复现既知偶发**（`ObjectDisposedException: 'SQLitePCL.sqlite3'`）。

差额口径自核：Infrastructure +23 ＝ `CostumeSlotsWereMeasuredTests` 的 11 ＋ 11 ＋ 1
（我逐条数过该文件：两条 `[SkippableTheory]` 各挂 11 行 `TheoryData`，加一条 `[Fact]`）；
Protocol +3 ＝ `TheAvatarBagIsServedTests` 原地改名 1 条、新增 3 条。**清单第六节的 +26 拆法成立。**

M2（删掉 11 个键 → 红 19：Protocol 6、Infrastructure 13）我**没有重跑**，
但按判据内容逐条推演可复算：Infrastructure ＝ 逐键判据 11 ＋ `No_name_reaches_the_twelfth_costume_slot` 1
＋ 表范围判据 1 ＝ 13；Protocol ＝ 穿戴四条 ＋ `The_archive_calls_..._names_its_worn_slot`
＋ `An_avatar_aimed_at_the_wrong_costume_slot_...` ＝ 6。**两数都对得上，记法可信。**

---

## 二、B-1：**已解决**

### 现在那段注释我自己读了，逐句核

`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:647-657`：

| 它现在说的 | 我的核证 | 判定 |
|---|---|---|
| 「**while** `ByName` carried no name ending in `avatar]`，一件时装到 `PutOnAsync` 得不到答案、被拒为 `NotWearable`」 | 已改为过去时限定从句，描述的是补键之前的状态，属实 | **成立** |
| 「11 件时装逐件拖，就是这样量到 11 个槽的，那些数现在在表里」 | 与契约、类注释、`CostumeSlotsWereMeasuredTests` 的记法一致；来源层次（槽号＝实机、名字＝档案、枚举＝第三方）未被写过头 | **成立** |
| 「a costume aimed at the wrong slot is the ordinary `ItemCannotOccupySlot` refusal today」 | `MoveItemSpaceSlice.cs:406-407`：`slot.Value != command.DestinationSlotIndex` → `ItemCannotOccupySlot`。判据 `An_avatar_aimed_at_the_wrong_costume_slot_...` 整句比对 `... refused as ItemCannotOccupySlot` | **成立** |
| 「`NotWearable` is left for a template the archive names no slot for at all」 | **我自己找了今天所有还能触发 `NotWearable` 的路**，见下 | **成立，且没有说过头** |

### 「`NotWearable` 现在留给谁」——我自己扫的

```
$ grep -rn "NotWearable" Server/src --include=*.cs
:200  枚举声明
:405  PutOnAsync        <- WornSlotForAsync 答 null
:462  CarryAcrossAsync  <- SlotAdmission.ItemUnknown
:528  TakeOffAsync      <- SlotAdmission.ItemUnknown
:650/:654 注释本身
:698  ErrorCodeFor 映射
```

**三条生产路，逐条核它们的触发条件：**

- `:405` — `PvfEquipmentSlotDefinitionSource.GetAsync`（`:50-79`）在五种情形答 `null`：
  模板不在 `equipment.lst`、文件找不到、`[equipment type]` 标签未解析、读不出字符串、
  **或者名字不在 `ByName` 里**。最后一种今天仍然覆盖 6 个名字 / 268 个模板
  （`[artifact red/blue/green]`、`[charm]`、`[flag]`、`[name tag]`）。
- `:462` / `:528` — `ItemMovementDomainService.MayOccupySlotAsync`（`:20-23`）：
  `PlacementForAsync` 答 `null` 才是 `ItemUnknown`，也就是**档案根本没有这个模板的落位**。

**三条的触发条件都是「档案给不出落位」，与注释那句吻合，一条不多一条不少。**
时装这一侧确实已经全部离开 `NotWearable`（11 个名字全部命中 `ByName`）。
**这句话没有被写成「`NotWearable` 从此不可达」，限定准确。**

### 顺带核：这段改写没有动到可执行语句

```
$ git show 80bdb144:.../MoveItemSpaceSlice.cs | grep -v '^\s*///' | grep -v '^\s*//' > base
$ grep -v '^\s*///' .../MoveItemSpaceSlice.cs | grep -v '^\s*//'                     > cur
$ diff base cur        -> 无输出；两侧各 462 行
$ git diff -U0 80bdb144 -- .../MoveItemSpaceSlice.cs | grep -E '^[+-]' \
    | grep -v '^[+-][+-]' | grep -vcE '^[+-]\s*(///|//)'   -> 0
```

**本轮（以及整个变更集）在这个文件里只改注释，逐字符可证。**

---

## 三、B-2：**已解决**

### 「断言一个字没动」——与基线对比核过，属实

`git diff 80bdb144 -- TheAvatarBagIsServedTests.cs` 在这条用例上只有三段 `+/-`：
`<summary>` 一行、`<remarks>` 整段、方法名一行。
**`[Theory]`、两条 `[InlineData]`、`Build(...)`、`MoveAsync(...)`、两条 `Assert` 全部落在 diff 之外。**

它今天实际断言的是：

```csharp
var result = await side.MoveAsync(Main, BagSlot, Equipment, AvatarSlot /* 0 */);
Assert.Equal(template, result.ItemId);
Assert.Null(await side.ReadAsync(Equipment, AvatarSlot));
```

即「拖到槽 0 没成，且拒绝说得出是哪一件」，两个模板分别是普通上衣（槽 14）与上衣时装（槽 3）。

### 新名字与新文档是否与它实际断言的东西相符：**相符**

- 名字 `Nothing_goes_on_a_costume_slot_but_the_costume_whose_slot_it_is` 比它实测的范围略宽
  （它只试了槽 0），但**紧挨着的 `<summary>` 就把范围收回到「the first costume slot」**，
  且更宽的那一半由同文件的 `A_costume_does_not_go_on_a_costume_slot_that_is_not_its_own`（槽 5）
  与 `A_costume_is_worn_out_of_the_avatar_list_onto_its_own_slot`（槽 3 成功）分别钉住。
  **不构成误导。**
- `<remarks>` 的每一句我都核了：「槽零属于 hat avatar」（`ByName["[hat avatar]"] = 0`，属实）；
  「普通上衣（14）与上衣时装（3）都不能放那儿，且**被同一行拦下**」
  （两者都在 `:406-407` 那一行，属实）；
  「它此前还绿着，只是因为目标槽恰好是 0 而两个模板的槽都不是 0」（属实）。
- **它没有回避自己曾经说反话**：`<remarks>` 里写着
  "This used to say something stronger and, as of 2026-09-08, false"，
  把作废写在了明面上，符合本仓「原文留记 + 就地作废」的体例。

**B-2 关掉。**

---

## 四、B-3：**部分解决**——三个数全对，但第一轮点名的两处仍未动

### 数字部分：**全对，我自己数的**

| 数 | 我怎么核的 | 结果 |
|---|---|---|
| `ByName` 现在 **27** 个键 | 从 `ByName =` 到 `};` 之间数键行 → **27**；肉眼逐行数也是 11 ＋ 16 | **对** |
| 认不出的只剩 **6** 个名字 | `AGENTS.md` 扫描表里标「否」的行：`[artifact red]`、`[charm]`、`[artifact blue]`、`[flag]`、`[artifact green]`、`[name tag]` → **6 行** | **对** |
| **268** 个模板 | 65＋50＋47＋47＋31＋28 ＝ **268**，当场重算 | **对** |

第一轮点名的三个错数（16 个键 ×2、17 个名字、66,596 个模板）**都已就地加注作废并写出现值**；
原扫描表保持不动、并声明「它记的是当时的扫描」，**这是对的做法**——那张表是一次性扫描的原始记录。

### 未动的两处（**这是仍然挡着的部分**）

```
$ grep -rn "穿不上" AGENTS.md
566: - **时装取得下、穿不上。** 见下面「时装」一节。
611: #### 时装：种类已经对齐，但仍然穿不上
618: **穿不上的原因曾经不是没写代码，是槽号一格都没有出处。**   <- 已加「曾经」，OK
```

- **`AGENTS.md:566`** 落在小节 **`#### 今天没有的东西（逐条，不是「大概没有」）`**（`:557`）里。
  那个小节标题自己就在承诺逐条精确。这条条目今天是**假的**，且**没有任何限定词**，
  也不在任何日期块内部。**第一轮的 B-3「要做什么」把它排在第一位，本轮未动。**
- **`AGENTS.md:611`** 是小节标题本身，`#### 时装：种类已经对齐，但仍然穿不上`。
  第 620-621 行那条 `2026-09-08` 加注说的是「**下面这一整节**记的是当时的诊断」——
  **标题在它上面，不在「下面」的范围里**；`:566` 更是在整个小节之外。
- 623（「没有给这张表补键」）与 625（「槽号出处找过三处，三处都不给」）**被 620-621 那条
  「下面这一整节…」覆盖住了**，我判它们**已达标**，不再单列。

**为什么这两处算实质性**：`AGENTS.md` 是本仓交给下一个 agent 的定位文档，
`:566` 与 `:611` 分别是**总览条目**与**小节标题**——两个最常被略读命中的位置，
而它们说的正是本件推翻的那句头条。一个只读到这两行的读者会得出
「时装穿不上」，并可能据此重做本件、或在别处写下同样的话。
这不是措辞偏好，是**当前事实的假陈述**。

---

## 五、B-4（**新增阻塞**）：`RefusedEquipIsRecordedWithItsTemplateTests` 的 `<summary>` 仍把「档案不给时装槽号」当作它的前提

**这一条不是本轮整改引入的，是本轮与上一轮都漏掉的同族第六处**——
与 B-2 一模一样的错法，在**执行方本轮也动过的那个文件**里。

`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs:90-98`：

```csharp
/// <summary>
/// The premise: this template is an avatar, and the archive names no slot to wear it in.   // 已假
/// </summary>
/// <remarks>
/// Both halves matter. Without the first the refused piece is not an avatar and the record
/// says nothing about avatars; without the second the piece would be worn rather than refused
/// and there would be no record at all.                                                     // 已假
/// </remarks>
/// <remarks>                                       <- 2026-09-08 的更正块，追加在下面
/// This asserted that the archive named no worn slot for a coat avatar, ... now has
/// an answer: three. ...
/// </remarks>
[Fact]
public async Task The_archive_calls_this_template_an_avatar_and_names_its_worn_slot()
{
    ...
    Assert.Equal(CoatAvatarSlot /* 3 */,
        await archive.EquipmentSlots.GetAsync(CoatAvatarTemplate, CancellationToken.None));
}
```

**错在哪**

- `<summary>` 现在时陈述「the archive names no slot to wear it in」——
  三行之下的断言是 `Assert.Equal(3, ...)`。**`<summary>` 与方法名、与断言，三者互相矛盾。**
- 第一个 `<remarks>` 的「without the second the piece would be worn rather than refused and
  there would be no record at all」也已为假：这件上衣时装今天**确实会被穿上**
  （`A_costume_is_worn_out_of_the_bag_too` 走的就是它的同族路径），而记录照样有
  （`An_avatar_aimed_at_the_wrong_costume_slot_...` 断的就是那一行）。
- 执行方本轮在**同一个 `<summary>` 上方三行**加了 `private const byte CoatAvatarSlot = 3;`，
  却没有回头改这个 `<summary>`。

**与 B-2 的关系**：第一轮判 B-2 阻塞的理由原文是
「一条名字说反话的判据，比没有判据更糟——它会被当成『这条规则还在』的依据」。
**这里说反话的不是名字（名字已改对）而是 `<summary>`**，
而 `<summary>` 是 IDE 悬停与文档生成取的那一份，读者命中率不比名字低。
第一轮的 N-2 只把「两个 `<remarks>`」当成排版问题记了一笔，**没有看那个 `<summary>` 的内容**——
我把这归为**两轮共同的遗漏**，不是执行方本轮的新错。

**读者会做出什么错误判断**：打开这个文件想知道「档案给不给时装槽号」的人，
第一眼读到的是「the archive names no slot to wear it in」，且这句被冠以「The premise」。

**要做什么**（不改任何断言）：把 `<summary>` 改成它今天真正证明的东西
（例如 "The premise: this template is an avatar, and the archive names slot three for it"），
把第一个 `<remarks>` 的第二半改掉或标注作废，并把两个 `<remarks>` 合并成一个（顺手关掉 N-2）。

---

## 六、本轮整改**本身**有没有引入新的实质性不实：**没有**

标准只有一条：读者会不会据此做出错误判断。逐处核过如下。

### 清单第一节新写的那段教训（`behavior-change-list.md:36-43`）

| 句子 | 核证 | 判定 |
|---|---|---|
| 「这两条初版没写命中数，而其中一条的结果我根本没读」 | 与第一轮 N-6 描述一致；`PvfEquipmentSlots` 那条确实命中了 `MoveItemSpaceSlice.cs` 的失效注释 | **属实** |
| 「上一件栽的是『声明了没执行』，本件栽的是『执行了没读』」 | 与第一轮记载一致，且是对自己不利的表述，没有粉饰 | **属实** |
| `grep -rn "WornSlotForAsync" ... # 7 命中` | 我实测 **7** | **吻合** |
| `grep -rn "FirstWornSlot\|LastWornSlot" ... # 5 命中` | 我实测 **5** | **吻合** |
| `grep -rn "PvfEquipmentSlots" ... # 35 命中` | 我实测 **34** | **不吻合，见下（非阻塞）** |

**关于 35 vs 34**：这不是虚报，是**本轮整改自己把它改小了**。
基线该数为 **31**；本轮的改写从 `MoveItemSpaceSlice.cs`（2→1）与
`TheAvatarBagIsServedTests.cs`（1→0）各拿掉了一次提及，同时
`PvfEquipmentSlotsTests`（23→24）、`RefusedEquip...`（2→3）、新文件（+3）各加了几次，
净得 **34**。逐文件我都数了：

```
PvfEquipmentSlotDefinitionSource.cs 1 / PvfEquipmentSlots.cs 1 / PvfInitialEquipmentDefinitionSource.cs 1
MoveItemSpaceSlice.cs 1 / CostumeSlotsWereMeasuredTests.cs 3 / PvfEquipmentSlotsTests.cs 24
RefusedEquipIsRecordedWithItsTemplateTests.cs 3          合计 34
```

**属「可过期计数」，非阻塞**（N-11）。读者重跑得 34 会困惑，但不会因此对系统作出错误判断。

### 第五节新增的三行（C-6 / C-7 / C-8）

- **C-6**（`MoveItemSpaceSlice.cs` 日志段）：出处、内容、处置全部属实，**已就地改写**（见第二节）。
- **C-7**（`Nothing_may_be_worn_in_a_costume_slot`）：「此前还绿着纯属巧合（目标槽 0，两个模板的槽是
  14 和 3）」——我核过 `AvatarSlot = 0`、`OrdinaryTemplate = 10018`（`[coat]`→14）、
  `CostumeTemplate = 413500000`（`[coat avatar]`→3）。**属实。**
- **C-8**（`AGENTS.md` 三个数）：现值 27 / 6 / 268 我自己数过，**全对**。
  但**它把范围写成「时装节的三个数」，而第一轮 B-3 点的是十处**——
  C-8 只覆盖了其中的数字部分，`:566` 与 `:611` 两处**既没进 C-8，也没另立一行**。
  **这不是不实，是覆盖不全**（对应第四节）。

### 第五节末尾那段自省（`:160-163`）

> 「漏掉的三处有一个共同点——**它们都不在 `.md` 里**：两处在 `.cs`…，一处在 `AGENTS.md` 但是**数字**而不是说法。」

**这句自我否定**：`AGENTS.md` 就是 `.md`。后半句的限定（「但是数字而不是说法」）救回了意思，
读者不会因此对系统作出错误判断，但**这句话字面为假**。
另外它接着说「`.md` 侧的五条命令我确实逐条敲过且命中数吻合；栽在 `.cs` 侧那两条上」——
C-8 那处其实**是 `.md` 侧的命令命中过、却没读出数字漂移**，与「只栽在 `.cs` 侧」的自述不完全相符。
**非阻塞（N-12）**，属措辞而非事实。

### T 节新增的末行

「断言一个字没动——它本来就在断言『拖到不属于自己的槽会被拒』」：
断言未动我与基线逐字符核过（第三节）；「本来就在断言…被拒」——
它断的是 `result.ItemId == template` 与目的槽仍为空，**没有断状态码**，
但「目的槽仍为空」蕴含了「没穿上」，作为散文描述**站得住**。**属实。**

### 回头改过的计数——**我自己数的**

| 计数 | 文件实际状态 | 判定 |
|---|---|---|
| 第五节「共 **8** 处」 | 表里 C-1..C-8，**8 行** | **一致** |
| T 节「**五条**用例」 | 表里 5 行；S-4 也写「五条用例」 | **一致**（其中一条是原地改名 + 改断言，清单描述为「删除，换成四条」，与 `git diff` 的呈现方式不同但事实等价） |
| S-4「新增 1 个文件 **23** 条」 | 11 ＋ 11 ＋ 1 ＝ **23**，逐条数过 | **一致** |
| S-4「修改既有 **3** 个文件」 | `PvfEquipmentSlotsTests`、`RefusedEquip...`、`TheAvatarBagIsServed`，**3 个** | **一致** |
| S-5「`AGENTS.md`（**三处**）」 | 对应 C-3 / C-4 / C-8 三项 | **一致**（若按 diff 的改动块数则不止三处，属口径差异） |
| 第六节 M1「**三个**判据文件都有」（`:187`、`:190`） | 同一格枚举点了**四个**文件，红的 6 条也分布在四个文件里 | **不一致——第一轮 N-5 未处置**（非阻塞，见下） |

### 有没有因这轮改动而变得自相矛盾或悬空的地方

- **悬空引用**：`T-1..T-3` 已清，全文无残留；C-1..C-8 与 F/R/S 各节编号自洽。
- **自相矛盾**：除上面 `:160-163` 那句自我否定外，**没有**。
- **一处口径小滑**：S-3 把日志段那条注释归在「`PutOnAsync` 的注释两处」名下，
  而它其实在 `MoveItemSpaceHandler.ResolveAsync` 里（同文件不同类）。行内文字自己写明是「日志段」，
  **不误导，非阻塞（N-13）**。

---

## 七、前一轮已通过的部分有没有被破坏：**没有**

| 项 | 本轮抽核 | 结论 |
|---|---|---|
| 生产改动仍只有两处 | `git diff --name-only 80bdb144 -- Server/src/` → 恰好 `PvfEquipmentSlots.cs` 与 `MoveItemSpaceSlice.cs` 两个文件 | **成立** |
| 字典 11 行、原有 16 行未动 | 逐行读过 `:58-88`，`["[weapon]"] = 12` 起 16 行与基线逐字符相同，`FirstWornSlot = 12` / `LastWornSlot = 24` 未动 | **成立** |
| `MoveItemSpaceSlice.cs` 只改注释 | 剥注释后与基线 `diff` 无输出（各 462 行），非注释 `+/-` 行数 **0** | **成立** |
| 字节锚 | `git diff --name-only` 里没有任何 `*Transcript*` 文件；Protocol.Tests 585 条全绿 | **未变** |
| 架构判据 / 分层 | Application.Tests 1070 条全绿（含切片架构判据）；改动只落在 `Infrastructure/Content` 与 Protocol 的注释，新判据文件只引 `Abstractions.Content` ＋ `Infrastructure.Content` | **未越层** |
| 放行面 | `PutOnAsync` 的可执行分支一行未改；本件唯一新可达的放行是「时装到它自己的槽」，由四条判据钉住 | **无扩大** |
| `TreatWarningsAsErrors` | 全量构建 + 测试退出码 **0** | **通过** |
| `PvfEquipmentSlotsTests` 表范围判据 | 本轮未再改动，断言仍是「11 个时装名铺满 0..10 ＋ 其余在 12..24 ＋ 两常量 12/24」，未放宽 | **未被削弱** |
| 三源互证 / 118479 / 11 / 66328 / 建号 14 职业零 `avatar]` 名 | 第一轮已自行复现，本轮**按题面抽核**：`ByName` 27 键与槽号逐行对照过、扫描表 6 行「否」求和 268 对上、`No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 仍绿 | **未被破坏** |

---

## 八、非阻塞观察（含第一轮未处置的）

**第一轮的 10 条里，只有 N-6 被处置（补上了两个命中数）。其余 9 条原样留着。
它们本来就是非阻塞，我不把它们升级成挡板**，只逐条记明现状：

| # | 现状 |
|---|---|
| N-1 | **未处置。** `TheAvatarBagIsServedTests.cs:138-139` 仍是 `/// <summary>` 后面紧跟一整个 `/// <summary>…</summary>`，外层从未闭合。本轮又在这段下面重写了 `<remarks>`，畸形照旧。 |
| N-2 | **未处置。** `RefusedEquip…:93-104` 仍挂两个 `<remarks>`。**并且其第一个 `<remarks>` 与 `<summary>` 的内容已为假——升级为 B-4。** |
| N-3 | **未处置。** `No_name_reaches_the_twelfth_costume_slot` 的两条断言仍被 `Every_name_is_a_costume_slot_or_a_worn_one_...` 完全蕴含；AS-004 在判据里仍是零保护。 |
| N-4 | **未处置。** `CostumeSlotsWereMeasuredTests.cs:78-79` 的 `_ = slot; _ = equipmentType;` 仍在。 |
| N-5 | **未处置。** 清单 `:187`/`:190` 仍写「三个判据文件」，实为四个。**这一条与 B-3 同族（回头没数）**，但量级小、且红的条数（6）自身正确，**维持非阻塞**。 |
| N-6 | **已处置。** 两条命令补上了命中数；其中 `PvfEquipmentSlots` 那条现在实测是 34 而非 35（见 N-11）。 |
| N-7 | **未处置。** 11 条实机日志仍只有 3 条转写在仓里；清单第七节未加这句说明。**措辞仍未写过头**，维持非阻塞。 |
| N-8 | **未处置。** `Document/avatar-container-entry-shape.md:102` 与 `NOTI-0x000D.md:423` 的「时装仍然穿不上」仍在，**两处都在明确标日期的更正块内部**，读作历史标注，维持非阻塞。 |
| N-9 | **未处置。** `AGENTS.md:689` 仍是 `###`，其前后同级内容是 `####`（`:611`、`:733`），共同父节是 `:489` 的 `###`。后果确认存在：`:733`「一处影响面超出时装的行为变化」现在挂在「2026-09-08」这一节底下；且 `:620` 那句「结论见**本节末尾**的补记」在结构上指不到 `:689`（它是父级的兄弟，不在本节内）。 |
| N-10 | **未处置。** `A_costume_is_worn_out_of_the_bag_too` 仍只断目的端。 |
| **N-11**（新） | 清单 `:32` 的 `PvfEquipmentSlots` 命中数写 **35**，今天实测 **34**——本轮改写自己把它改小了。属可过期计数。 |
| **N-12**（新） | 清单 `:161`「它们都不在 `.md` 里」字面自我否定（下一句就说「一处在 `AGENTS.md`」）；且「只栽在 `.cs` 侧那两条上」与 C-8（`.md` 侧、命令扫到过）不完全相符。属措辞。 |
| **N-13**（新） | 清单 S-3 把日志段那条注释归在「`PutOnAsync` 的注释」名下，实际在 `ResolveAsync`。行内已写明是「日志段」，不误导。 |
| **N-14**（新） | `AGENTS.md:698-699`「下面三条注意事项也仍然成立，只是**第 1 条与第 3 条**描述的现象，起因已经变了（时装现在有槽号了）」——第 3 条讲的是「剩余秒数应画出约 11.4 年」，与时装有没有槽号无关；这个指向像是笔误。三条注意事项各自的内容我核过**都仍然正确**，所以只是指向不准，读者不会算错任何数。 |
| **N-15**（新） | `MoveItemSpaceSlice.cs:655` 长 121 字符，是本轮重排注释留下的未回绕行（同文件其余超 100 的行均为基线既有）。仓内无 `max_line_length` 约束，纯观感。 |

---

## 九、明确表态

### A. 还有没有「读者会据此做出错误判断」的实质性不实？—— **有，3 处**

1. **`AGENTS.md:566`** `- **时装取得下、穿不上。** 见下面「时装」一节。`
   → 无任何限定，落在「今天没有的东西（逐条，不是「大概没有」）」小节里。**第一轮已点名，未动。**
2. **`AGENTS.md:611`** `#### 时装：种类已经对齐，但仍然穿不上`
   → 小节标题本身为假；`:620` 的作废加注只覆盖「下面这一整节」，覆盖不到标题，更覆盖不到 `:566`。
   **第一轮已点名，未动。**
3. **`RefusedEquipIsRecordedWithItsTemplateTests.cs:91` 的 `<summary>` 与 `:95-97` 的第一个 `<remarks>`**
   → 现在时陈述「the archive names no slot to wear it in」，与三行之下的
   `Assert.Equal(3, ...)` 和方法名 `..._and_names_its_worn_slot` 直接矛盾。**两轮共同遗漏。**

**除这三处外，我没有找到第四处实质性不实。** 本轮整改**自身**引入的实质性不实：**0 条**。

### B. 是否只剩措辞 / 编号 / 可过期计数？—— **不是**

上面三处都是**当前事实的假陈述**，不是措辞偏好，因此不适用「判 approved 并列为非阻塞」。
真正属于措辞 / 编号 / 可过期计数的（N-11 到 N-15，以及第一轮遗留的 N-1..N-5、N-7..N-10）
**我全部列为非阻塞，一条都没有升级。**

### C. 仍 `changes_required`：还差这 3 件，一次就能做完

| # | 要做什么 | 是「实质性不实」还是「可以更完整」 |
|---|---|---|
| **1** | `AGENTS.md:566`：按本仓「原文留记 + 就地加注作废」体例处理，或直接改写为「时装取得下、**已可穿上**（2026-09-08）」 | **实质性不实** |
| **2** | `AGENTS.md:611`：标题改写或紧跟一行加注（例如「时装：种类已经对齐；穿不上的问题已于 2026-09-08 解决，本节记的是当时的诊断」） | **实质性不实** |
| **3** | `RefusedEquipIsRecordedWithItsTemplateTests.cs`：把 `<summary>`（`:91`）与第一个 `<remarks>`（`:95-97`）改成它今天真正的前提（档案给这件时装的槽是 **3**），并把两个 `<remarks>` 合并 | **实质性不实**（合并 `<remarks>` 那一半是「可以更完整」） |

**顺手做完更干净、但不构成挡板**（**可以更完整**，做不做都放行）：

- 把上面三处补进 `behavior-change-list.md` 第五节（会从 8 处变成 9 或 10 处，**记得回头数**）；
- N-5：清单 `:187`/`:190` 的「三个判据文件」改成「四个」；
- N-11：把 `:32` 的 `35` 改成 `34`，或注明「该数会随本件自身的注释改写变动」；
- N-12：`:161` 那句「它们都不在 `.md` 里」重写；
- N-9：`AGENTS.md:689` 的 `###` 降为 `####`；
- N-1 / N-2：两处 XML 文档畸形；
- N-14：`AGENTS.md:698` 的「第 1 条与第 3 条」指向订正。

**这三件都不动可执行语句、不动任何断言、不动任何期望值。** 改完复跑全量，预期仍是 **2361 / 2 / 0**。

---

## 十、我在第二轮实际跑过的命令

```
tasklist | grep -i -E "Dfo|Server.Host"                       # NO_HOST_PROCESS
git status --porcelain            （复审开始与结束各一次，逐行相同：10 M + 2 ??）
git rev-parse HEAD                # 80bdb1445a1e29d1b3bf871351391f4c79cc1380
git diff --stat 80bdb144
git diff 80bdb144 -- Server/src/
git diff 80bdb144 -- AGENTS.md
git diff 80bdb144 -- Docs/CreatureDomainDesign.md Document/avatar-container-entry-shape.md \
                     protocol/game/server-to-client/NOTI-0x0002.md \
                     protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md
git diff 80bdb144 -- Server/tests/.../TheAvatarBagIsServedTests.cs
git diff 80bdb144 -- Server/tests/.../RefusedEquipIsRecordedWithItsTemplateTests.cs
git diff 80bdb144 -- Server/tests/.../PvfEquipmentSlotsTests.cs
git diff --name-only 80bdb144 -- Server/src/
git diff --name-only 80bdb144 | grep -i transcript              # 无命中
git log -3 --format="%h %ad %s" --date=short -- .../MoveItemSpaceSlice.cs   # 80bdb144 = 2026-09-07

PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo                  # 2361 / 2 / 0，退出码 0

# 注释-only 核实（临时文件在 scratchpad，不在仓内）
git show 80bdb144:.../MoveItemSpaceSlice.cs | grep -v '^\s*///' | grep -v '^\s*//' > $SCR/base_slice.txt
grep -v '^\s*///' .../MoveItemSpaceSlice.cs | grep -v '^\s*//'                     > $SCR/cur_slice.txt
diff $SCR/base_slice.txt $SCR/cur_slice.txt                     # 无输出，各 462 行
git diff -U0 80bdb144 -- .../MoveItemSpaceSlice.cs | grep -E '^[+-]' \
  | grep -v '^[+-][+-]' | grep -vcE '^[+-]\s*(///|//)'          # 0

# 源码与判据逐处读
sed -n '340,470p' / '495,560p' / '600,700p'  .../MoveItemSpaceSlice.cs
sed -n '55,95p'                              .../PvfEquipmentSlots.cs
cat -n                                       .../ItemPlacementRuleService.cs
sed -n '1,60p'                               .../ItemMovementDomainService.cs
sed -n '50,110p'                             .../PvfEquipmentSlotDefinitionSource.cs
cat -n                                       .../CostumeSlotsWereMeasuredTests.cs
sed -n '40,70p' / '136,144p' / '178,200p' / '236,300p'  .../TheAvatarBagIsServedTests.cs
sed -n '60,175p'                             .../RefusedEquipIsRecordedWithItsTemplateTests.cs
sed -n '558,572p' / '605,700p' / '689,750p'  AGENTS.md
sed -n '95,115p'                             Document/avatar-container-entry-shape.md
sed -n '415,430p'                            protocol/game/server-to-client/NOTI-0x000D.md
cat -n                                       .rks-agent/.../behavior-change-list.md

# 计数与搜索（全部自己数的）
从 `ByName =` 到 `};` 之间数键行                                                 # 27
grep -rn "NotWearable"          Server/src --include=*.cs                       # 3 条生产路
grep -rn "PvfEquipmentSlots"    Server/src Server/tests --include=*.cs | wc -l  # 34
grep -rn "WornSlotForAsync"     Server/src Server/tests --include=*.cs | wc -l  # 7
grep -rn "FirstWornSlot|LastWornSlot" Server/src Server/tests --include=*.cs | wc -l   # 5
grep -rn "PlacementForAsync|WornSlotForAsync" Server/src --include=*.cs
grep -rn "SlotAdmission"        Server/src --include=*.cs
grep -rn "穿不上|穿不了|no avatar slot|carries no name|names no slot|lowest slot" \
     --include=*.md --include=*.cs .   （排除 .rks-agent/ 与 .worktrees/）
grep -n "^#{1,6} " AGENTS.md                                                    # 标题层级
git grep -c "PvfEquipmentSlots" 80bdb144 -- 'Server/src/*.cs' 'Server/tests/*.cs'  # 基线 31
awk 'length>100' （五个改动过的 .cs 文件）                                       # 行长
```

**残留核实**：第二轮**没有在仓库内新建或删除任何文件**，也**没有做任何变异**（因此没有备份/还原动作）。
临时产物 `base_slice.txt`、`cur_slice.txt` 与测试输出文件全部在会话 scratchpad 目录下，不在仓库内。
复审结束时 `git status --porcelain` 与开始时**逐行相同**（10 个 `M` + 2 个 `??`）。
主检出、`.worktrees/client-debug-lab`、`.worktrees/entries-are-what-the-client-reads` 全程未触碰；
`E:/Project/DNF/ServerS4A21` 本轮**未读取**（第一轮已读；本轮的上游枚举结论按抽核采信其记法，
且我另有档案侧与表侧两条独立核证支持同一结论）。

# 第三轮：整改后复审（独立评审方，fresh context）

**未采信执行方任何自评结论**；下面每一条都是本轮自己读、自己数、自己跑出来的。
基线 `80bdb1445a1e29d1b3bf871351391f4c79cc1380`，工作区未提交。
**本轮未修改仓库任何文件**，只追加本节；**未做任何变异**，因而未动任何备份/还原；
未使用任何 git 还原类命令；未触碰主检出与另两个 worktree；`ServerS4A21` 本轮未读。

| | |
|---|---|
| Verdict | **changes_required** |
| 前两轮阻塞的处置 | B-1 已解决（第二轮判定，本轮复核仍成立）／B-2 已解决／**B-3 已解决**／**B-4 已解决** |
| 本轮整改自身引入的实质性不实 | **0 条** |
| 新增阻塞 | **1 条（B-5）**——与 B-1／B-2／B-4 完全同族的**第四处**，两轮都没读到的位置：`RefusedEquipIsRecordedWithItsTemplateTests` 的**类级 `<remarks>`** |
| 新增非阻塞 | 1 条（N-16） |
| 生产代码缺陷 | **0 条**（第三轮仍然如此） |

**一句话**：这一轮把上一轮点名的三件事都做对了，做得准、没说过头，也没有把别处改坏。
挡住它的是同一个错法的**第四处**——而且是最显眼的那一处：执行方本轮修好了那个方法的
`<summary>`（B-4），却没有往上翻 65 行去看**同一个文件的类级注释**，那里用现在时写着
「`PvfEquipmentSlots.ByName` … none of the names in it ends in `avatar]`」。
这正是第一轮 B-1 挡下的那句话，一字不差的同义句，只是换了个位置。

---

## 一、判据复跑（本轮自己跑的）

跑之前确认无 `Dfo.Server.Host` 进程（`tasklist` 只有两个 `dotnet.exe`：VS Code 的
`ProjectSystem.Server.BuildHost` 与一个 IDE 侧 `vstest.console`，均非 host，`wmic` 读过命令行）。

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

| 套件 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Protocol.Tests | 585 | 0 | 0 |
| Networking.Tests | 52 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Host.Tests | 6 | 0 | 0 |
| Persistence.Tests | 329 | 0 | 0 |
| Infrastructure.Tests | 259 | **2** | 0 |
| Application.Tests | 1070 | 0 | 0 |
| **合计** | **2361** | **2** | **0** |

**退出码 0**，一次跑完，无偶发。跳过的两条是 `A_missing_archive_*`（档案可达才跳过，符合预期）。
日志里 `warning|警告` **0 次命中**，`TreatWarningsAsErrors=true`（`Server/Directory.Build.props:6`）——
**SC-6 达成**，与执行方所报 2361/2/0 一致（这是我自己跑出来的，不是采信）。

---

## 二、B-4：**已解决**

`RefusedEquipIsRecordedWithItsTemplateTests.cs` 那个方法（现 `:90-107`）：

- `<summary>`（`:91`）现在是 "The premise: this template is an avatar, and the archive **names the
  slot it is worn in**." —— 与方法名 `..._and_names_its_worn_slot`、与 `:116` 的
  `Assert.Equal(CoatAvatarSlot /* 3 */, …)` **三者一致**。
- 第一个 `<remarks>`（`:93-99`）改写为「第二半变了……now says which slot -- three」，
  **没有说过头**：它没有声称槽三是本判据测出来的，只说档案现在给得出。
- 第二个 `<remarks>`（`:100-107`）是历史块，全程过去时（"This asserted…", "which was true while…"），
  **读作作废注，正确**。

顺带核：本轮在这个文件上**只动了注释、方法名与那一处断言**（`Assert.Null` → `Assert.Equal(3)`）
和一处日志期望（`NotWearable` → `ItemCannotOccupySlot`），`git diff` 逐行读过，
**没有放宽或删除任何断言**。

N-2（两个 `<remarks>` 并列）**未合并**，仍在——维持非阻塞。

---

## 三、B-3 余下两处：**已解决，且加注后整节自洽**

```
$ grep -rn "穿不上\|穿不了\|不能穿\|无法穿" --include=*.md .   （排除 .rks-agent/ 与 .worktrees/）
```

命中 9 处，逐处判：

| 出处 | 现文 | 判定 |
|---|---|---|
| `AGENTS.md:566` | `- ~~**时装取得下、穿不上。**~~ **2026-09-08 已修**：11 个时装槽名已进 …，时装能穿上。` | **已解决**。删除线 + 就地加注，是本仓「原文留记 + 加注作废」的既有体例；落在「今天没有的东西（逐条…）」里也不再骗人 |
| `AGENTS.md:611` | `#### 时装：种类已经对齐，穿不上的那一半已于 2026-09-08 修好` | **已解决**。标题本身现在为真 |
| `AGENTS.md:618` | 「穿不上的原因**曾经**不是…」 | 上一轮已加限定，仍成立 |
| `Document/avatar-container-entry-shape.md:102` | 在明确标日期的更正块内部 | 维持非阻塞（N-8） |
| `protocol/…/NOTI-0x000D.md:423` | 同上，块内 | 维持非阻塞（N-8） |
| `Document/vmp-agent-next-task.md:96`「时装…不能穿、不能试」 | 落在 §3，而该文件**第 3-7 行的抬头**写明「本文第 3~5 节已作废（2026-09-07 当日更晚）……本文保留是为了留下当时的判断依据，不是待办清单」 | **不构成问题**（本轮新查的一处，两轮都没扫到，但它已被自己的抬头作废） |
| `probes/cmd-reason-messages-20260818.md:88,100` | 客户端提示语转写，与本件无关 | 无关 |
| `patterns/world-entry.md:642`、`traces/a21-tutorial-ending-20260826.md:17` | 讲的是「跨列表拒绝」与教程装备，非时装槽 | 无关 |

**加注之后整节是否仍自洽**：是。`:620-621` 那条「下面这一整节记的是当时的诊断」
现在与已改写的标题（`:611`）方向一致，不再互相打架；`:623`「没有给这张表补键」、
`:625`「槽号出处找过三处」都在该注的覆盖范围内；`:644-646`、`:674-676` 两处数字加注
把 16/17/66,596 都标成了当时值。**没有别的句子还在说「穿不上」。**

数字复核（自己数的）：`ByName` 现 **27** 键（`sed` 取 `ByName =` 到 `};` 之间，`grep -c` 键行 = 27）；
扫描表标「否」的 6 行相加 = **268**；11 个时装名相加 = **66,328**。三个数全对。

---

## 四、N-11 / N-12 / N-13：处置核实

| # | 现文 | 判定 |
|---|---|---|
| N-11 | 清单 `:32` 改为 `# 34 命中（基线 31）` | **已解决且属实**。我实测今天 **34**（逐文件：`PvfEquipmentSlotDefinitionSource` 1／`PvfEquipmentSlots` 1／`PvfInitialEquipmentDefinitionSource` 1／`MoveItemSpaceSlice` 1／`CostumeSlotsWereMeasuredTests` 3／`PvfEquipmentSlotsTests` 24／`RefusedEquip…` 3）；基线用 `git grep … 80bdb144` 实测 **31**。**两个数都读者可复算** |
| N-13 | S-3 现写「`ResolveAsync` 的日志段那条」；C-6 出处现写「`MoveItemSpaceSlice.cs` 的 `ResolveAsync` 日志段」 | **已解决**。归属正确（该注释在 `MoveItemSpaceHandler.ResolveAsync`，`:645-660`） |
| N-12 | 「它们都不在 `.md` 里」已删除，改写为两类错法 | **原句已清除**，但**改写本身有一处新的归因不准** → 见 N-16 |

---

## 五、本轮整改自身有没有引入新的实质性不实：**没有（0 条）**

标准只有一条：读者会不会据此做出错误判断。逐处核过。

### 清单第五节新增的三行

| # | 核证 | 判定 |
|---|---|---|
| C-9 | 出处（`RefusedEquip…cs`）、被推翻的说法（`<summary>` + 第一个 `<remarks>`）、处置（就地改写）、归因（评审 B-4、两轮共同遗漏、「我这轮还在它上方三行加了 `CoatAvatarSlot = 3` 却没回头读它」）—— **逐项与文件实况吻合**，且是对自己不利的表述 | **属实** |
| C-10 | `AGENTS.md:566`，「零限定」的描述与基线原文吻合；处置「就地划掉并加注」与现文吻合 | **属实** |
| C-11 | `AGENTS.md:611` 标题；「前一轮加的作废注写的是『下面这一整节』，覆盖不到标题本身」—— 与 `:620` 原文吻合 | **属实** |

### 自己数的计数

- **第五节「共 11 处」**：表里 C-1…C-7、C-9、C-10、C-11、C-8 —— **11 行，对得上**。
  （C-8 被排在 C-11 之后，编号乱序，纯排版，非阻塞。）
- `grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs` —— **34**，与清单一致。
- 前一轮已核过的计数本轮抽核仍一致：T 节「五条用例」= 表 5 行；S-4「新增 1 文件 23 条」
  （11＋11＋1）；S-4「修改既有 3 个文件」。

### 有没有变得自相矛盾或悬空

- **悬空引用**：C-1…C-11 全部有正文对应；S-5 提到的「C-6、C-7」与第五节对得上；无 T-1..T-3 残留。
- **自相矛盾**：本轮改动没有制造新的互相打架的句子。
- **唯一一处新的归因不准**：见 N-16（非阻塞）。

---

## 六、前两轮已通过的部分有没有被破坏：**没有**

| 项 | 本轮自己怎么核的 | 结论 |
|---|---|---|
| 生产改动仍只有两处 | `git diff --name-only 80bdb144 -- Server/src/` → 恰好 `PvfEquipmentSlots.cs`、`MoveItemSpaceSlice.cs` | **成立** |
| `MoveItemSpaceSlice.cs` 只改注释 | 两版各自剥掉整行注释后 **均 462 行**，`diff` **无输出** | **成立** |
| 字典只加不改 | `git diff --unified=0` 的 `-` 行只有 **3 行类注释**，字典段零删除行；原有 16 行与 `FirstWornSlot`/`LastWornSlot` 未动 | **成立** |
| 字节锚 | `git diff --name-only 80bdb144` 中无任何 `*Transcript*`；Protocol.Tests 585 全绿 | **未变** |
| 架构判据 / 分层 | Application.Tests 1070 全绿（含切片架构判据）；改动只落在 `Infrastructure/Content` 与 Protocol 的注释 | **未越层** |
| 放行面 | `PutOnAsync` 可执行分支一行未改（由「只改注释」的剥离比对直接蕴含） | **无扩大** |
| `TreatWarningsAsErrors` | `Server/Directory.Build.props:6` = true；全量构建+测试退出码 0，日志零 warning | **通过** |

---

## 七、B-5（**新增阻塞，1 条**）：`RefusedEquipIsRecordedWithItsTemplateTests` 的**类级 `<remarks>`** 仍用现在时说「表里没有 `avatar]` 名」

`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs`

```csharp
21  /// <summary>
22  /// What the log says when the server will not put a piece on, and why the avatar case is read
23  /// off it.
24  /// </summary>
25  /// <remarks>
26  /// <c>PvfEquipmentSlots.ByName</c> is written out one name at a time and none of the names in it
27  /// ends in <c>avatar]</c>. Template 101500690, ...
...
41  /// The archive is the real one. The premise the instrument rests on -- that this template is an
42  /// avatar and that the archive gives it no worn slot -- is asserted rather than assumed, because a
43  /// double saying so would keep saying so after the archive stopped.
```

**错在哪**

1. **`:26-27` 现在时、零限定、完全为假**：表里今天有 **11** 个以 `avatar]` 结尾的名字
   （就是本件加的那 11 行）。这句话与第一轮 B-1 挡下的生产注释
   （"`ByName` carries no name ending in `avatar]`"）是**同一句话的同义改写**。
2. **`:41-42` 与它自己的断言直接矛盾**：它说「本工具赖以成立的前提——档案不给它穿戴槽——
   是被断言而非被假定的」。`:110-118` 那条断言今天是 `Assert.Equal(CoatAvatarSlot /* 3 */, …)`，
   断的正好是反面。执行方本轮把 `:91` 的 `<summary>` 改对了，**却没往上翻到类级注释**。
3. **位置是最显眼的一处**：类级 `<summary>`/`<remarks>` 是打开文件第一眼、也是 IDE 悬停在类名上
   取的那一份；而生产注释 `MoveItemSpaceSlice.cs:656` 正是把读者指到**这个文件**来的
   （"The cases are in `RefusedEquipIsRecordedWithItsTemplateTests`"）。
4. **它就在执行方声明并跑过的那条 `grep` 的命中里**：`:26` 含 `PvfEquipmentSlots`，
   是 34 命中中的一条——**与清单自己招认的「跑了没读」是同一次没读**。

**读者会做出什么错误判断**：顺着生产注释翻到这个文件、想知道「档案到底给不给时装槽号」的人，
第一眼读到的是「表里没有一个名字以 `avatar]` 结尾」，并被告知「这个前提是被断言过的」。
这与 B-1／B-2／B-4 挡下的是同一个错判，量级不比它们小。

**这不是措辞偏好**：不是排版、不是编号、不是可过期计数，是**当前事实的现在时假陈述**。
第一轮为生产注释里的同一句话判了阻塞，第二轮为方法 `<summary>` 里的同一句话判了阻塞；
对**类级注释**里最显眼的那一份放行，标准会前后不一。

**要做什么**（不动任何断言、任何期望值、任何可执行语句）

- 把 `:26-27` 改成过去时或加限定（例如 "…was written out one name at a time and, until
  2026-09-08, none of the names in it ended in `avatar]`；the eleven costume names went in then"）；
- 把 `:41-42` 的「the archive gives it no worn slot」改成它今天真正断言的东西
  （档案给这件上衣时装的槽是 **3**），或整段标注作废；
- 顺手（**不构成挡板**）把这一处补进 `behavior-change-list.md` 第五节，成为 C-12，
  并**回头把「共 11 处」重算成「共 12 处」**；同时可把第五节的「两类错法」按 N-16 订正。

**这一处改完，本件在我这边就没有剩余的实质性不实了。** 预期复跑仍是 2361/2/0。

---

## 八、非阻塞观察

**第一轮 10 条里 9 条、第二轮 5 条里 3 条仍原样留着（N-6/N-11/N-13 已处置）。
它们本来就是非阻塞，我一条都不升级。** 新增 1 条：

| # | 现状 |
|---|---|
| N-1 | **未处置。** `TheAvatarBagIsServedTests.cs:138-139` 仍是 `/// <summary>` 后紧跟一整个 `<summary>…</summary>`，外层从未闭合。 |
| N-2 | **未处置。** `RefusedEquip…:93-107` 仍挂两个 `<remarks>`。**内容已改对**（B-4 已解决），只剩排版。 |
| N-3 / N-4 / N-7 / N-8 / N-9 / N-10 / N-14 / N-15 | **均未处置，现状与第二轮记载一致。** 其中 N-15 复核：`MoveItemSpaceSlice.cs:655` 长 **121** 字符，是该文件唯一 >120 的行，基线该文件 >120 的行 **0** 条——但 `.editorconfig` 无 `max_line_length`，构建零 warning，纯观感。 |
| N-5 | **未处置。** 清单 `:195` 仍写「三个判据文件都有」，同格枚举点名**四个**文件。与 B-3 同族但量级小，红的条数（6）本身正确，维持非阻塞。 |
| N-6 / N-11 / N-13 | **已处置。** |
| **N-16**（新） | 清单 `:166-170` 把两类错法写成「`.cs` 侧跑了没读（C-6/C-7/C-9）」与「`.md` 侧扫的是『说法』而没扫『数字』与『标题』（C-8/C-10/C-11）」，并解释「`grep` 那几个主题词……**也命中不到写在小节标题里的断言**」。**这半句字面为假**：第一节列的 `.md` 命令里就有 `grep -rln "穿不上\|穿戴槽\|槽位映射"`，而 C-10（`:566`）与 C-11（`:611`）**两行都含「穿不上」，都会被它命中**——`grep` 不区分标题与正文。C-10 更是一条普通「说法」，既不是数字也不是标题，归到这一类不合。**真正的错法是同一个「跑了没读」，只是 `.md` 侧用了 `-l`（只列文件）因而从未看见行**。这是本轮改写引入的新归因不准，但它是**执行方对自己方法的复盘**，不是对代码或系统的陈述；下一个人据此至多多加一道「扫数字/标题」的工序（无害），不会对代码或对下一件工作的对象形成错判。**维持非阻塞**，但建议随 B-5 一并订正为「`.md` 侧用了 `-l`，只看到文件没看到行」。 |

---

## 九、明确表态

### A. 还有没有「读者会据此做出错误判断」的实质性不实？—— **有，1 处**

**`RefusedEquipIsRecordedWithItsTemplateTests.cs:26-27` 与 `:41-42`（类级 `<remarks>`）**：
现在时、零限定地陈述「`PvfEquipmentSlots.ByName` 里没有一个名字以 `avatar]` 结尾」
与「档案不给这件时装穿戴槽，且这个前提是被断言过的」。两句在今天都为假，
后者与同文件 `:110-118` 的 `Assert.Equal(3, …)` 直接矛盾。见 B-5。

**除这一处外，我在全仓 `.cs` 与 `.md` 两侧各扫过一遍，没有找到第二处实质性不实。**
（`.cs` 侧用 `none of the names|no worn slot|gives it no|carries no name|cannot be worn|
has no costume|is empty always|names no worn slot|lowest slot ... twelve` 等词族扫，
其余命中全部是过去时或已在标日期的作废块内；`.md` 侧见第三节那张九行表。）

**本轮整改自身引入的实质性不实：0 条。** B-5 是两轮都没读到的旧文本，不是本轮写坏的。

### B. 是否只剩措辞 / 编号 / 可过期计数 / 排版？—— **不是**

B-5 是当前事实的现在时假陈述，且在文件最显眼的注释位置，与第一轮 B-1、第二轮 B-4
挡下的是同一句话，因此不适用「判 approved 并列为非阻塞」。
真正属于措辞 / 编号 / 可过期计数 / 排版的（N-1..N-5、N-7..N-10、N-14..N-16）
**我全部列为非阻塞，一条都没有升级**——包括本轮新出现的 N-16。

### C. 仍 `changes_required`：只差 1 件

| # | 要做什么 | 「实质性不实」还是「可以更完整」 |
|---|---|---|
| **1** | `RefusedEquipIsRecordedWithItsTemplateTests.cs` 的**类级 `<remarks>`**（`:26-27`、`:41-42`）：改成过去时/加日期限定，并把「档案不给它穿戴槽」换成它今天真正断言的「档案给的是 3」 | **实质性不实**（唯一挡板） |
| 2 | 把它补进清单第五节成 C-12，并把「共 11 处」重算为「共 12 处」 | **可以更完整**（不做也放行） |
| 3 | N-16：把「`grep` 命中不到写在小节标题里的断言」订正为「`.md` 侧用了 `-l`，只看到文件没看到行」 | **可以更完整** |
| 4 | N-5 / N-1 / N-2 / N-9 / N-14 / N-15 等 | **可以更完整** |

**第 1 件不动任何断言、任何期望值、任何可执行语句。**
若把第 1 件做掉，其余全部属于「可以更完整」，我这边即可判 `approved`。

**生产代码三轮零缺陷，本件的改动仍然只有「字典加 11 行 + 改注释」，这一点本轮再次核实成立。**

---

## 十、我在第三轮实际跑过的命令

```
# 工作区与差异面
git status --short
git rev-parse --abbrev-ref HEAD ; git rev-parse HEAD
git diff --stat
git diff -- Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
git diff -- Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
git diff -- AGENTS.md
git diff -- Docs/CreatureDomainDesign.md Document/avatar-container-entry-shape.md \
            protocol/game/server-to-client/NOTI-0x0002.md \
            protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md
git diff -- Server/tests/.../RefusedEquipIsRecordedWithItsTemplateTests.cs
git diff --name-only 80bdb1445a1e29d1b3bf871351391f4c79cc1380 -- Server/src/
git diff --name-only 80bdb1445a1e29d1b3bf871351391f4c79cc1380 | grep -i transcript

# 判据（跑之前先确认无 Dfo.Server.Host）
tasklist | grep -i -E "Dfo|dotnet|testhost|vstest"
wmic process where "name='dotnet.exe'" get ProcessId,CommandLine /format:list
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo          # 2361 / 2 / 0，退出码 0
grep -ci "warning|警告" <上面这次的完整日志>              # 0

# 计数（全部自己数的）
grep -rn "PvfEquipmentSlots" Server/src Server/tests --include=*.cs | wc -l          # 34
git grep -n "PvfEquipmentSlots" 80bdb144 -- "Server/src/*.cs" "Server/tests/*.cs" | wc -l   # 31（基线）
sed -n '/ByName =/,/^        };/p' .../PvfEquipmentSlots.cs | grep -c 键行            # 27
grep -c "\[Fact\]|\[Theory\]" .../TheAvatarBagIsServedTests.cs                       # 13

# 「只改注释」核实（无变异、无 git 还原）
git show 80bdb144:.../MoveItemSpaceSlice.cs > $SCRATCH/base-slice.cs
cp .../MoveItemSpaceSlice.cs $SCRATCH/now-slice.cs
sed 's/\r$//' … | grep -v -E '^[[:space:]]*//' > $SCRATCH/{base,now}-stripped.cs
wc -l $SCRATCH/base-stripped.cs $SCRATCH/now-stripped.cs      # 各 462
diff $SCRATCH/base-stripped.cs $SCRATCH/now-stripped.cs       # 无输出

# 陈旧说法搜索集（两侧各扫一遍）
grep -rn "穿不上|穿不了|不能穿|无法穿" --include=*.md .        （排除 .rks-agent/ .worktrees/）
grep -rn "avatar\]"    --include=*.md .                        （同上）
grep -rn "NotWearable" --include=*.md .                        （同上）
grep -rn "NotWearable" Server/src Server/tests --include=*.cs
grep -rniE "no name(s)? (in it )?(end|ending)|names no (worn )?slot|no slot for|nothing can be worn|no costume name|lowest slot .* twelve|begin at twelve" Server/src Server/tests --include=*.cs
grep -rniE "none of the names|no worn slot|gives it no|has no costume|carries no name|is empty always|are therefore empty|cannot be worn" Server/src Server/tests --include=*.cs

# 逐处读（sed，不列举全部）
sed -n '1,140p'  .../RefusedEquipIsRecordedWithItsTemplateTests.cs
sed -n '1,60p' / '136,152p' / '240,262p'  .../TheAvatarBagIsServedTests.cs
sed -n '550,760p' AGENTS.md
sed -n '10,224p'  behavior-change-list.md
sed -n '1,20p' / '70,115p' Document/vmp-agent-next-task.md
sed -n '830,1085p' reviews/change-review.md

# 工程约束
grep -rn "max_line_length" .editorconfig                       # 无
grep -rn "TreatWarningsAsErrors" --include=*.props . # Server/Directory.Build.props:6 = true
awk 'length>120 {...}' .../MoveItemSpaceSlice.cs .../PvfEquipmentSlots.cs   # 现 1 行(655,121)；基线 0 行
```

**本轮未做任何变异，因而未做备份/还原；未使用任何 git 还原类命令；
除本文件外未修改仓内任何文件（`git status --short` 复核，改动面与进场时一致）。**

---

# 第四轮：整改后复审（独立评审方，fresh context）

**未采信执行方任何自评结论，也未采信前三轮的任何结论。** 前三轮标为「已核实」的项目，
凡本轮结论依赖的，我都自己重跑或重读了一遍。**本轮除本文件外未修改仓内任何文件**，
未做任何变异（因而无备份/还原动作），未使用任何 git 还原类命令。

## 判定摘要

| | |
|---|---|
| Verdict | **approved** |
| B-5 处置 | **已解决**（`:26-27`、`:41-42` 两处都改对了，且我通读全文件确认**没有第四个副本**） |
| 新增阻塞 | **0 条** |
| 实质性不实 | **0 条** |
| 新增非阻塞观察 | 4 条（N-17 … N-20） |
| 生产代码缺陷 | **0 条**（第四轮仍然如此） |
| 判据 | **2361 通过 / 2 跳过 / 0 失败**，退出码 0，零 warning——本轮自己跑的 |

---

## 一、判据复跑（本轮自己跑的）

跑之前先确认无 `Dfo.Server.Host`：按 `Dfo.Server.Host`、`*Dfo*`、`*testhost*`、`*vstest*`
四个模式各查一遍，**全部无输出**。

```
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
```

七个套件逐个相加（我自己求的和，不是抄执行方的）：
585 + 60 + 52 + 329 + 6 + 259 + 1070 = **2361 通过**；跳过 0+0+0+0+0+2+0 = **2 跳过**；
**0 失败**，退出码 0。整份日志按 `warning|警告` 检索**零命中**（`TreatWarningsAsErrors=true`，
`Server/Directory.Build.props:6`）。

**我没有复跑基线 `80bdb144` 的 2335**——本轮改动面与上一轮相比只多了三处注释/文档编辑，
基线数由前三轮各自跑过；本轮要核的是「现在这一版是不是 2361/2/0」，是。

---

## 二、B-5：**已解决**

`Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs`

**我通读了整个文件（412 行），不是只看被点名的两处。** 逐句核如下。

| 位置 | 现在的说法 | 我怎么核的 | 判定 |
|---|---|---|---|
| `:26-27` | "…written out one name at a time, **and until 2026-09-08 none of the names in it ended in** `avatar]`" | 过去时 + 日期限定。基线 `ByName` 16 个键、`avatar]` 结尾 0 个；本轮加 11 行（`git diff` 逐行看过），今天 27 键、11 个 `avatar]` 键 | **真，且限定准确** |
| `:34` | "**The names are in the table now, so this file no longer reads a slot map off a refusal.**" | 与字典现状一致 | **真** |
| `:35-36` | 时装拖错槽是 `ItemCannotOccupySlot` | `MoveItemSpaceSlice.cs:406-407`：`slot.Value != DestinationSlotIndex` 即 `ItemCannotOccupySlot`；同文件 `:152-158` 的断言正是这条日志加 `NotMovable` | **真** |
| `:36-37` | "`NotWearable` **is left for a template the archive names no slot for at all**" | **我自己找全了今天还能触发 `NotWearable` 的路径**，共三条：`MoveItemSpaceSlice.cs:405`（`PutOnAsync`，`WornSlotForAsync` 返 `null`）、`:462`（`CarryAcrossAsync`）、`:528`（`TakeOffAsync`），后两条经 `SlotAdmission.ItemUnknown`。`ItemMovementDomainService.cs:22-23` 显示 `ItemUnknown` 的条件是 `PlacementForAsync` 返 `null`，枚举自注（`IItemMovementDomainService.cs:14`）为「档案根本没有声明这个 id 下能持有的物品」 | **三条路径全部满足这句话**（档案连物品都不声明的，当然也不给它槽号）。见 N-17：这句在后两条路径上**不够精确但不为假** |
| `:41-45` | 转引 `PutOnAsync` 自己的一段话 | 与 `MoveItemSpaceSlice.cs:353-356` 逐字比对 | **引用属实** |
| `:48-51` | 前提是「这个模板是什么、档案给它哪个槽」；"the premise went from *no worn slot* to *slot three*, and the assertion is what said so" | 与同文件 `:116-127` 的 `Assert.Equal(ItemWorldClass.Avatar, …)` 与 `Assert.Equal(CoatAvatarSlot /* 3 */, …)` 完全一致。**第三轮指出的「与自己的断言直接矛盾」已消除** | **真** |
| `:54-57` | "These tests do not establish which slot any avatar name belongs in… what it names for **the other ten** avatar types" | 11 个 `avatar]` 键减去本文件用到的 `[coat avatar]` 等于 10 | **真，数也对** |

**有没有说过头**：没有。`:54` 那一句反而是本文件里限定最紧的一句——它明确把
「档案说槽是 3」（测得到）与「实机客户端认为槽是 3」（测不到）分开，
与 `:87` 常量注释「measured against a live client」互相咬合，不越界。

**有没有第四个副本**：**没有。** 全文件按 `avatar]` / `no slot` / `NotWearable` /
`could not be worn` / `named no slot` 各扫一遍，剩下的命中是
`:51`（"went from *no worn slot* to *slot three*"，引号内是被推翻的旧前提）、
`:103-106`（方法级第一个 `<remarks>`，过去时加 2026-09-08 日期）、
`:109-114`（第二个 `<remarks>`，同上）、`:134`（"It **used to** drag… **because** the archive
named no slot"，过去时）——**全部是过去时或带日期的作废叙述**，无一是现在时假陈述。

---

## 三、本轮三处改动**本身**有没有引入新问题：**没有**

### 1. 类级 `<remarks>` 的 XML

- `:21-59` 共 39 行，**每一行都带 `///`**（判 `$0 !~ /^[[:space:]]*\/\/\//`，零命中）
  ——第三轮改写过程中一度丢失的 `///` 确已补回。
- 把 `:21-59` 剥掉 `/// ` 前缀后包进 `<doc>` 交 XML 解析器：**WELL-FORMED**。
  四个 `<para>` 全部闭合，`<b>`/`<c>` 全部配对。
- 该项目未开 `GenerateDocumentationFile`，`CS1570` 本就不会触发；但这里是**真的合法**，
  不是靠没开检查蒙混过去。

### 2. 重新断行的那段注释

`MoveItemSpaceSlice.cs:648-658`，逐行长度：78 / 93 / 100 / 94 / 97 / 95 / 99 / 96 / 94 / 117 / 76。

- **该文件现在 >120 的行 0 条，基线也是 0 条**——第三轮记的 N-15（`:655` 长 121）已消除，
  且**没有引入新的超长行**。同样按 >120 扫过本轮触及的全部 6 个 `.cs` 文件，**零命中**。
- 文意通顺、逻辑链完整（`{ItemId}` 曾是量槽号的唯一出口 → 11 个槽量完进表 →
  今天拖错槽是 `ItemCannotOccupySlot`，`NotWearable` 留给档案不给槽的模板 → 用例在哪个文件）。
- 见 N-18：`:657` 长 117，与上下文 93–100 的节奏不齐，纯观感。

### 3. 清单「共 12 处」与第五节实际行数

**我自己数的**：第五节表格 `^| C-\d+` 共 **12 行**，去重后 **12 个**，
序列为 `C-1 C-2 C-3 C-4 C-5 C-6 C-7 C-9 C-10 C-11 C-12 C-8`——
编号齐全无缺、无重复，**只是 C-8 排在末尾（顺序问题，第三轮已列为非阻塞，我不升级）**。
标题 `:144`「共 12 处」**与实际行数对得上**。

**但**：同一节末尾 `:164` 仍写「经**两轮**独立评审补出，**现共 11 处**（C-1..C-11）」——
重算只落到标题，**没落到这一句**。见 N-20。

### 4. M1 那行「四个判据文件」

`:198` 现读「**四个**判据文件都有」，同格枚举点名：逐键判据的上衣那条、表范围判据、
`The_archive_calls_this_template_an_avatar_and_names_its_worn_slot`、
`TheAvatarBagIsServedTests` 的三条穿戴判据——**确是四个不同文件**，
且 1+1+1+3 = **6**，与同行的「红了 6 条」自洽。**N-5 已解决。**

---

## 四、全仓再扫一遍这一族说法（**我自己设计的口径，真的执行了**）

**`.cs` 侧**（`Server/` 加 `Tool/`），一条正则扫「没有 avatar 名 / 不给槽 / 穿不上」全族：

```
none of the names | no name(s) .{0,20}(end|ending) | carries no name | names no (worn )?slot |
no worn slot | gives it no | has no (costume|slot) | no costume name | cannot be worn |
could not be worn | unwearable | not wearable | stood empty | are empty | is empty always |
no slot for | nothing came back | avatar slots
```

22 条命中，**逐条读过上下文**：

| 命中 | 判定 |
|---|---|
| `ItemPlacementContent.cs:9`、`PvfEntryLifecycleContentSource.cs:216`——「the **eleven** names ending in `avatar` are the avatar slots」 | 说的是**档案 `[equipment type]`** 的 11 个名字，与本件的 11 一致；基线原文，**真** |
| `PvfEquipmentSlots.cs:7/11/13/50` | `:11-13` 带「**until 2026-09-08**」；`:50` 说的是 `[creature]`，过去时且与时装无关。**真** |
| `MoveItemSpaceSlice.cs:649/654` | `carried`（过去时）加「refusal **today**」。**真** |
| `RefusedEquip…:37/51/110/111/134` | 见第二节，全部过去时或带日期。**真** |
| `TheAvatarBagIsServed…:142-144` | 「This case **used to** assert a refusal… the archive **named** no slot」——过去时，紧接 `:146` 写「The measuring is done and the names are in the table」。**真** |
| 其余 7 条（`CharacterRosterService`、`InventoryEntryAssembly`、`ExperienceBonusRules`、`CharacterEntryTables`、`SessionMetricsPayloadCodec`、`UseStackableSlice`、`SelectCharacterLifecycleTests`） | 与时装/`ByName` 无关，误命中 |

另按 `avatar]` 单扫 `.cs`（排除 11 个键字面量），命中 `MoveItemSpaceSlice.cs:369`
（"only the eleven `avatar]` names reach `0..10`"）、`TheAvatarBagIsServedTests.cs:204`（同句）、
`CostumeSlotsWereMeasuredTests.cs:94`、`PvfEquipmentSlots.cs:24`——**全部是现值，全部真**。

**`.md` 侧**，三个口径分别扫：**说法**（`avatar]|NotWearable|穿不上|穿不了|不能穿|无法穿`）、
**数字**（`16 个键|17 个名字|66,?596|无 avatar|零个 avatar|没有一个…avatar`）、
**标题**（`^#{1,6} .*(时装|装扮|穿不|avatar)`）。

- `AGENTS.md`：`:566` 已划删线并加「2026-09-08 已修」；`:611` 标题已改为「穿不上的那一半**已于
  2026-09-08 修好**」；`:619`/`:644`/`:673`/`:678` 四个数字点全部改成「**当时的** 16 个键」
  「**当时**认不出…17 个」并各带 2026-09-08 加注写出现值。
  **「16」我自己核过是基线真值**（27 减 11 等于 16），作为过去值成立。`:689` 起新增
  「2026-09-08：那 11 个时装槽已经量完」一节。**第一/二轮 B-3 点名的十处全部落地。**
- `Docs/CreatureDomainDesign.md:231`、`Document/avatar-container-entry-shape.md:95`、
  `NOTI-0x0002.md:690`、`traces/a21-starting-gear-and-skills-20260826.md:83`：
  逐处比对 `git diff`，加注/更正内容与代码现状一致。
- `spec.md` / `TaskContract.md` 里的「一个 `avatar]` 键都没有」：这两份是本件**自己的问题陈述**，
  描述的就是改动前的状态，**不是失效记载**。

**第四类藏身处：没有找到。** 三处仍带「时装仍然穿不上」字样的句子（见 N-19）
**全部落在明确标了日期的历史块内**，不是现在时断言。

---

## 五、前三轮已通过的部分有没有被破坏：**没有**

| 项 | 本轮怎么核的 | 结果 |
|---|---|---|
| 生产改动面 | `git diff --name-only 80bdb144 -- Server/src/` | **仍只有 2 个文件**：`PvfEquipmentSlots.cs`、`MoveItemSpaceSlice.cs` |
| 「只改注释」 | `git show 基线:MoveItemSpaceSlice.cs` 与现文件各去 `\r`、各删 `^\s*//` 行，`wc -l` 各 **462**，`diff` **无输出** | **可执行语句逐字符未变** |
| 字典 11 行 | `git diff` 逐行读：`[hat avatar]`=0 … `[weapon avatar]`=10，**11 行，槽 0..10 各一次**；`["[weapon]"] = 12` 起的既有各行**一字未动** | **成立** |
| 字节锚 | `git diff --name-only 80bdb144` 按 `transcript|anchor` 过滤 | **无命中**，锚文件未被触碰 |
| 分层 | `PvfEquipmentSlots` 仍是 `Dfo.Server.Infrastructure.Content` 的 `internal static`；未新增跨层引用 | **未破坏** |
| `TreatWarningsAsErrors` | `Server/Directory.Build.props:6` 为 `true`；本轮全量日志零 warning | **成立** |
| 放行面 | `git status --porcelain` 与进场时一致（10 改 + 2 未跟踪） | **未扩大** |

---

## 六、非阻塞观察（新增 4 条；旧的一条都不升级）

**第一轮 10 条、第二轮 5 条、第三轮 1 条里，N-5 本轮已解决**（见第三节第 4 点）；
`N-1`（`TheAvatarBagIsServedTests.cs:138-139` 外层 `<summary>` 从未闭合）、`N-2`、`N-3`、`N-4`、
`N-7` 至 `N-10`、`N-14`、`N-16` 仍原样留着，**均维持非阻塞，我一条都不升级**。新增：

| # | 现状 | 类别 |
|---|---|---|
| **N-17** | `RefusedEquip…:36-37`「`NotWearable` is left for a template **the archive names no slot for at all**」——这句在 `PutOnAsync` 那条路径上**逐字精确**；在另两条路径（`CarryAcrossAsync:462`、`TakeOffAsync:528`，经 `SlotAdmission.ItemUnknown`）上，更精确的因由是「档案**根本没有声明**这个 id 下的物品」。**这句话在三条路径上都为真**（不声明物品者必不给槽），只是对后两条不够细。所在段落本就在讲这个文件/这条路径，读者不会因此对代码形成错判。 | 可以更完整 |
| **N-18** | `MoveItemSpaceSlice.cs:657` 长 **117** 字符，上下文各行 93–100。未超 120（`.editorconfig` 也无 `max_line_length`），纯断行节奏。 | 可以更完整 |
| **N-19** | 三处仍含「时装仍然穿不上／根本没法用」字样的 `.md` 句子，**全部在带日期的历史块内**：① `protocol/game/server-to-client/NOTI-0x000D.md:423`，在 `> **2026-09-07 更正**` 引用块内，且其小节标题带 `<sup>[已于 2026-09-07 修复，原文留记]</sup>`；② `Document/avatar-container-entry-shape.md:102`，在「（2026-09-07 已补：…）」括注内，而**同一处上一条 bullet 已补上 2026-09-08 的「时装现在能穿上」**；③ `Document/vmp-agent-next-task.md:96`，属该文件第 3 节，文件抬头横幅已写「**本文第 3~5 节已作废（2026-09-07）**……保留是为了留下当时的判断依据，不是待办清单」。三处都符合本仓「原文留记 + 就地加注」的既有体例，且权威索引 `AGENTS.md` 已带 2026-09-08 给出现值。**最整洁的做法是在 ①② 各补一行 2026-09-08 加注**，但不做也不会让读者对代码或对下一件工作形成错判。 | 可以更完整 |
| **N-20** | `behavior-change-list.md:164` 仍写「经**两轮**独立评审补出，**现共 11 处**（C-1..C-11）」，而同节标题 `:144` 已改为「共 12 处」、表内 C-12 就在两行之上、实际已是**三轮**。重算只落到标题没落到这句。这是清单**对自己行数的记账**，表格本身是权威且当场可数，不涉及代码或下一件工作的对象。 | 可以更完整 |

---

## 七、明确表态

### A. 还有没有「读者会据此做出错误判断」的实质性不实？—— **没有。0 条。**

`.cs` 与 `.md` 两侧我各按自己设计的三个口径（说法 / 数字 / 标题）扫过，
命中逐条读过上下文：**没有一句是当前事实的现在时假陈述。**
第三轮 B-5 点名的两处已改对，且**同文件没有第四个副本**（全文件 412 行通读）。
`NotWearable` 的三条现存路径我自己找全并逐条比对了那句话，成立。

**本轮整改自身引入的实质性不实：0 条。**

### B. 判定 —— **`approved`**

生产代码**四轮零缺陷**；改动仍然只有「字典加 11 行 + 改注释」，
`MoveItemSpaceSlice.cs` 剥注释后与基线 `diff` 无输出；判据 2361/2/0、零 warning、退出码 0；
放行面、字节锚、分层、`TreatWarningsAsErrors` 均未被破坏。

### C. 剩下的四条（N-17 至 N-20）**全部属于「可以更完整」**

无一是实质性不实：N-17 是一句真话不够细，N-18 是断行节奏，
N-19 是三处带日期的历史块可以再加一行加注，N-20 是清单对自己行数的记账落后一句。
**按题面标准，这些不构成阻塞，我不升级。** 判 `approved`。

---

## 八、我在第四轮实际跑过的命令

```
# 工作区与差异面
git status --porcelain ; git rev-parse --abbrev-ref HEAD ; git rev-parse HEAD
git diff --stat 80bdb1445a1e29d1b3bf871351391f4c79cc1380
git diff --name-only 80bdb1445a1e29d1b3bf871351391f4c79cc1380
git diff --name-only 80bdb1445a1e29d1b3bf871351391f4c79cc1380 -- Server/src/
git diff --name-only 80bdb1445a1e29d1b3bf871351391f4c79cc1380 | grep -iE "transcript|anchor"
git diff 80bdb144 -- Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
git diff 80bdb144 -- protocol/.../a21-starting-gear-and-skills-20260826.md \
                     protocol/game/server-to-client/NOTI-0x0002.md \
                     Docs/CreatureDomainDesign.md Document/avatar-container-entry-shape.md

# 判据（跑之前确认无 Dfo.Server.Host）
Get-Process -Name "Dfo.Server.Host"                                      # 无
Get-Process | Where-Object 名字含 Dfo / testhost / vstest                # 无
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
  dotnet test Server/86JP.Server.slnx --nologo                           # 2361 / 2 / 0，退出码 0
grep -n "已通过!|失败:|警告|warning" 本次完整日志                          # warning/警告 零命中
grep -o "通过: *[0-9]*" 日志 | awk 求和                                   # 2361（自己求的和）

# 「只改注释」核实（无变异、无 git 还原）
git show 80bdb144:.../MoveItemSpaceSlice.cs | sed 去\r | grep -v '^\s*//' > base-stripped.cs
sed 去\r .../MoveItemSpaceSlice.cs          | grep -v '^\s*//' > now-stripped.cs
wc -l base-stripped.cs now-stripped.cs        # 各 462
diff  base-stripped.cs now-stripped.cs        # 无输出

# 行长（本轮触及的 6 个 .cs，现在与基线）
awk 'length>120' 上述 6 个文件                                    # 零命中
git show 80bdb144:.../MoveItemSpaceSlice.cs | awk 'length>120'   # 零命中
awk 'NR>=648 && NR<=658 {print NR, length}' .../MoveItemSpaceSlice.cs

# 类级 remarks 的 XML 与 /// 完整性
awk 'NR>=21 && NR<=59 && 不以 /// 开头 {print NR}' .../RefusedEquip....cs   # 零命中
sed -n '21,59p' .../RefusedEquip....cs | 剥 /// 前缀 > doc.xml
python: ET.fromstring('<doc>'+doc.xml+'</doc>')                            # WELL-FORMED

# 陈旧说法搜索集（我自己设计，说法 / 数字 / 标题 三个口径）
grep -rniE "none of the names|no name(s)? .{0,20}(end|ending)|carries no name|names no (worn )?slot|
            no worn slot|gives it no|has no (costume|slot)|no costume name|cannot be worn|
            could not be worn|unwearable|not wearable|stood empty|are empty|is empty always|
            no slot for|nothing came back|avatar slots" Server/ Tool/ --include=*.cs
grep -rn "avatar\]" Server/ Tool/ --include=*.cs
grep -rn "NotWearable" Server/src Server/tests --include=*.cs
grep -rn --include=*.md -E "avatar\]|NotWearable|穿不上|穿不了|不能穿|无法穿" .    （排除 .worktrees/）
grep -rn --include=*.md -E "16 个键|17 个名字|66,?596|十六个|无 avatar|零个 avatar" .
grep -rn --include=*.md -E "^#{1,6} .*(时装|装扮|穿不|avatar|Avatar)" .

# 计数（自己数的）
awk '/^## 五、/,/^## 六、/' behavior-change-list.md | grep -cE '^\| C-[0-9]+'                    # 12
awk '/^## 五、/,/^## 六、/' behavior-change-list.md | grep -oE '^\| C-[0-9]+' | sort -u | wc -l  # 12
grep -n "共 [0-9]*处|C-1\.\.C-|三个判据文件|四个" behavior-change-list.md

# 逐处读
cat -n .../RefusedEquipIsRecordedWithItsTemplateTests.cs          # 全文件 412 行
sed -n '370,540p' / '630,700p' / '360,375p' .../MoveItemSpaceSlice.cs
cat -n Server/src/Dfo.Server.Application/Inventory/ItemMovementDomainService.cs
sed -n '1,50p'   Server/src/Dfo.Server.Abstractions/Inventory/IItemMovementDomainService.cs
sed -n '609,700p' AGENTS.md
sed -n '120,200p' behavior-change-list.md
sed -n '125,175p' .../TheAvatarBagIsServedTests.cs
sed -n '82,110p' Document/avatar-container-entry-shape.md
sed -n '410,432p' protocol/game/server-to-client/NOTI-0x000D.md
sed -n '1,25p' / '80,110p' Document/vmp-agent-next-task.md
sed -n '630,650p' protocol/patterns/world-entry.md
sed -n '1,20p' Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs
sed -n '208,224p' Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
sed -n '1,30p' Server/Directory.Build.props ; grep -rn "GenerateDocumentationFile|NoWarn" Server/
```

**本轮未做任何变异，因而未做备份/还原；未使用任何 git 还原类命令；
未触碰主检出与另两个 worktree；`ServerS4A21` 本轮未读取；
除本文件外未修改仓内任何文件（`git status --porcelain` 复核，改动面与进场时一致）。**
