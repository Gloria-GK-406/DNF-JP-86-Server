# 变化清单：时装知道自己穿在哪一格

契约 `./TaskContract.md` revision 1（SC-1..SC-8）／Spec `./spec.md`（RQ-001..RQ-009）。
基线 `80bdb144`，未提交。

本清单**正向与反向都报**，且**从代码出发独立扫**，不是核对 Spec 的条目。

---

## 一、扫法

上一件在搜索集上栽了两次：**范围写对了但没照它执行**（`.md` 一次都没扫），
以及**承认之后只补跑了五条 `grep` 里的一条**。SC-8 因此要求：
**声明的每一条命令都要真的敲下去，并把命中数写进清单。**

### 真的执行过的命令与命中

`.md` 侧（排除 `.rks-agent/` 与 `.worktrees/`）：

```
grep -rln "NotWearable"                       --include=*.md .   # 3 文件
grep -rln "avatar]"                           --include=*.md .   # 2 文件
grep -rln "穿不上\|穿戴槽\|槽位映射"            --include=*.md .   # 9 文件
grep -rln "PvfEquipmentSlots"                 --include=*.md .   # 4 文件
grep -rln "AuraSkinAvatar\|AuroraIllusion"    --include=*.md .   # 3 文件
```

`.cs` 侧：

```
grep -rn "FirstWornSlot\|LastWornSlot"  Server/src Server/tests --include=*.cs   # 5 命中（生产 2、判据 3）
grep -rn "PvfEquipmentSlots"            Server/src Server/tests --include=*.cs   # 34 命中（基线 31）
grep -rn "WornSlotForAsync"             Server/src Server/tests --include=*.cs   # 7 命中
```

**这两条初版没写命中数，而其中一条的结果我根本没读。** 由独立评审指出（其 B-1）：
`PvfEquipmentSlots` 那些命中里就有 `MoveItemSpaceSlice.cs` `ResolveAsync` 的一段生产注释，
写着「`ByName` carries no name ending in `avatar]`」与「101500690 会被拒为 `NotWearable`」——
**两句都被本轮推翻**，而我跑了那条命令、没看输出。

**这正是 SC-8 点名要防的第三层：范围写对了、命令也敲了，但没照结果做事。**
上一件栽的是「声明了没执行」，本件栽的是「执行了没读」。**下一个人：命中数要写下来，
写不出命中数就等于没扫。**

档案侧（探针，用完即删，`git status` 已确认无残留）：

```
逐件读 11 个模板的 [equipment type]                                        # 11/11 有值
全档案 equipment 索引扫 avatar] 结尾的类型                                  # 118479 条 → 11 种 / 66328 模板
逐职业读 [create equipment list] 里的槽名                                   # 14 职业 → 零个 avatar] 名
```

**这三次档案扫描是本件的承重证据**，第三次尤其重要：它回答的是「补键会不会改变建号」，
而**判据全绿答不了这个问题**——今天没有任何用例走建号表里的装扮槽名。

### 一处要如实说的

第二次扫描（118479 条 → 11 种 / 66328 模板）**不是本件的新发现**：
`AGENTS.md` 里早有一张同样的表，数字逐项吻合（`[weapon avatar]` 449、`[aurora avatar]` 423、
11 个时装名相加 66,328）。本轮是**独立复现**，不是首次测得。

---

## 二、正向：可观察的行为变化，共 3 条

| # | 变化 | 之前 | 现在 |
|---|---|---|---|
| F-1 | 时装拖到**它自己的**装扮槽 | `NotWearable`（带模板号） | **穿上**，两端都落 |
| F-2 | 时装拖到**别的**装扮槽 | `NotWearable` | `ItemCannotOccupySlot`（带模板号） |
| F-3 | `WornSlotForAsync` 对时装的答案 | `null` | `0..10`（按 `[equipment type]`） |

F-3 是前两条的成因，单列是因为它有**第二个读者**（见 R-1）。

### 没有第四条

非时装的穿戴与移动**逐值不变**：`ByName` 原有的每一个键与值逐字未动
（`git diff` 里那些行没有出现），`PvfEquipmentSlotDefinitionSource` 与
`PutOnAsync` 的可执行语句一行未改。

---

## 三、反向：新增或改变的拒绝与放行路径，共 3 条

**从代码出发扫 `ByName` 的每一个读者，不是核对上面的正向表。**

| # | 位置 | 是什么 |
|---|---|---|
| R-1 | `PvfInitialEquipmentDefinitionSource`（**`ByName` 的第二个读者**） | 建号起始装备按「槽名 + 物品号」成对读，槽名查不到就**丢弃那一项**。补键之后，创建表里任何 `avatar]` 名会从「被丢弃」变成「真的发一件」。**实测：14 个职业的创建表只出现 `[weapon]`、`[coat]`、`[pants]`，零个 `avatar]` 名——所以建号行为逐值不变。** 另有既有判据 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 在钉这一条。 |
| R-2 | `PutOnAsync` 的 `ItemCannotOccupySlot` 分支 | 该分支本身未改，但**第一次对时装可达**：以前时装在上一行就 `NotWearable` 了。这不是新拒绝，是**一条既有拒绝的定义域扩大**。 |
| R-3 | `TakeOffAsync` 的 `DisplacedCannotOccupySlot` 分支（`MoveItemSpaceSlice.cs:541-546`） | **`WornSlotForAsync` 的第三个消费点**，它的输入也随本轮改变了：脱下装备时，若目的格已有东西，会问那件东西的穿戴槽。**改动前可达的每一个状态结果都没变**——那条路的目的列是主背包或装扮栏，被顶出来的那件若是时装，以前答 `null`（必不等于源槽 → 拒），现在答 `0..10`（源槽是装备栏槽 12..24，仍必不等于 → 仍拒）。**初版清单漏列了这个消费点**，由独立验证方补出。**它是枚举不全，不是记载不实。** |

**新增的生产拒绝：0 条。被移除的：0 条。被改变条件的：0 条。**
本件只往一个字典里加了 11 行。

**口径**：`WornSlotForAsync` 在生产里有 **3 个消费点**（`PutOnAsync` 取到达件的槽、
`TakeOffAsync` 取被顶出者的槽、以及 `IItemMovementDomainService` 的转发），
上表 R-2 与 R-3 覆盖了前两个。**初版只写了一个**——见 R-3。

### `PutOnAsync` 那个点名：结论是「不需要加判定」

上一件在那里留话，要求「给装扮槽补上名字的那个人」回来决定
「被顶下来那件能否回到源格」。**结论：不需要，且理由不再是空的。**

- 一件东西只能穿到它档案条目声明的那个槽 → **已经站在那里的那件也声明同一个槽**；
- 装扮槽 `0..10` 只被那 11 个 `avatar]` 名声明（全档案扫过）→ **两端都是时装**；
- 被顶下来那件回到的是**到达件刚才站的那一格**，在**同一个列表**里 →
  那个列表刚刚还容着一件时装 → 它收得回。

**覆盖范围写明了**：这只覆盖**移动能到达的状态**。存储里可以有一件时装待在移动放不进去的格子
（例如绕过服务器写的行），那种情形下交换会把它放回原处——**那是存储与规则不一致，不是这次交换造成的**。

**并已写明不得推广**成「穿戴槽决定类别」：`ByName` 把 `[earring]`、`[support weapon]`、
`[creature]` 同映槽 24，那是现成的反例，且是另一条 issue。

**这个结论有判据钉住**：`What_comes_off_a_costume_slot_goes_back_into_the_avatar_list`——
装扮栏里一件上衣时装，装备栏槽 3 已穿着另一件上衣时装，拖上去：新的穿上、旧的回到装扮栏。
**这条判据昨天写不出来**，因为装扮槽上站不了任何东西。

---

## 四、非行为改动（结构与记载）

| # | 内容 |
|---|---|
| S-1 | `PvfEquipmentSlots.ByName` 加 11 行；原有各行逐字未动。 |
| S-2 | 同文件的类注释重写：说明这 11 个数是**实机量得**、名字是**档案读出**、上游枚举是第三方佐证；说明槽 11 为何空着；并**更正**「A21 在武器之前插了 aurora-illusion avatar」——上游枚举明写它在 11、武器在 10。 |
| S-3 | `MoveItemSpaceSlice.cs` 的注释两处：`PutOnAsync` 里把「今天装扮槽上站不了任何东西所以无从发生」换成上面那个结论；`ResolveAsync` 的日志段那条已成假话的注释改写（C-6）。 |
| S-4 | 判据：新增 1 个文件 23 条（`CostumeSlotsWereMeasuredTests`）；修改既有 3 个文件、**五条用例**（见 T 节）。 |
| S-5 | 记载：`AGENTS.md`（三处，含 C-8 的三个数）、`NOTI-0x0002.md`、`Document/avatar-container-entry-shape.md`、`Docs/CreatureDomainDesign.md`、`protocol/patterns/data/traces/a21-starting-gear-and-skills-20260826.md`，加两处 `.cs` 里的注释（C-6、C-7）。逐处见第五节那张表。 |

### T 节：被修改的既有判据

**三个文件，五条用例，每一条都是钉着「时装没有穿戴槽」这个前提的** ——
而那个前提正是本件要推翻的。**没有任何断言被放宽或删除。**

**初版这里写「四条」，漏了第五条**（`Nothing_may_be_worn_in_a_costume_slot`）——
它把那个前提写得最直白，却因为目标槽恰好是 0 而**继续绿着**。由独立评审 B-2 补出，见下表末行。

| 用例 | 之前钉什么 | 现在钉什么 | 归因 |
|---|---|---|---|
| `PvfEquipmentSlotsTests` 的表范围判据 | 表里**每个**值都在 12..24 | 11 个 `avatar]` 名占满 `0..10`；其余在 12..24；两段之外没有 | F-3。原断言在时装槽有名字之后必然为假；新断言说的是它当初真正在保护的东西 |
| `RefusedEquip…The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it` | 档案给上衣时装**没有**槽号 | 给的是 **3** | F-3。用例改名 |
| `RefusedEquip…An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` | 拖到槽 2 得 `NotWearable` | 拖到槽 2 得 `ItemCannotOccupySlot`（上衣时装的槽是 3） | F-2。用例改名；**它证明的性质没变**——「读到物品之后做出的拒绝会说出是哪一件」 |
| `TheAvatarBagIsServedTests.Wearing_a_costume_out_of_the_avatar_list_is_refused_by_the_archive_not_by_the_list` | 时装从装扮栏拖到装备栏被拒 | **删除**，换成四条：穿上（两种源列表各一）、拖错槽被拒、交换 | F-1。上一件的注释把这条拒绝称作「本件今天全部的交付」，本件正是它作废的时候 |
| `TheAvatarBagIsServedTests.Nothing_may_be_worn_in_a_costume_slot` | 名字与文档都说「装扮槽上穿不了任何东西」「表里最低的槽是 12」 | 改名为 `Nothing_goes_on_a_costume_slot_but_the_costume_whose_slot_it_is`；断言不变，文档改写为它现在真正证明的事 | F-3。**断言一个字没动**——它本来就在断言「拖到不属于自己的槽会被拒」，只是名字与文档说的是另一回事 |

---

## 五、本轮推翻的说法，共 12 处

| # | 说法 | 出处 | 处置 |
|---|---|---|---|
| C-1 | 「A21 在**武器之前**插了 `AuraSkinAvatar`（槽位 11）」 | `PvfEquipmentSlots` 类注释、`NOTI-0x0002.md:690`、`traces/a21-starting-gear-and-skills-20260826.md:83` | **三处都就地更正**。上游枚举 `WeaponAvatar = 10`、`AuraSkinAvatar = 11`——在武器**之后** |
| C-2 | 「`PvfEquipmentSlots.ByName` 没有以 `avatar]` 结尾的键」 | `Document/avatar-container-entry-shape.md:95` | 原文留记 + 加注「已补，上衣正是 3」 |
| C-3 | 「那张表里出现哪些名字**没有测过**」（指建号表） | `AGENTS.md` | **就地更正**：测过了，既有判据 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 在钉它；本轮又独立扫了一遍 |
| C-4 | 量具说明书的前提「时装还没有槽号，所以要靠拒绝行去量」 | `AGENTS.md` | **加一节 `2026-09-08`**：表已量完、11 个键已进表、时装能穿上；量具本身留着（对下一个槽号问题仍管用），并写明三条注意事项的**起因已经变了** |
| C-5 | 「那张表**没有 `[creature]` 键** → `NotWearable`」 | `Docs/CreatureDomainDesign.md:231` | **不是本轮推翻的**（更早的成果就加了 `[creature]`=24），但本轮扫到，就地加注说明它记的是当时状态；**该节其余部分仍然成立且已单独立项** |
| C-6 | 「`PvfEquipmentSlots.ByName` carries no name ending in `avatar]`」＋「101500690 … refused as `NotWearable`」 | **生产注释** `MoveItemSpaceSlice.cs` 的 `ResolveAsync` 日志段 | **就地改写**。由独立评审 B-1 发现——它就在我声明并跑过的那条 `grep` 的命中里，我没读输出 |
| C-7 | 判据 `Nothing_may_be_worn_in_a_costume_slot` 的**名字 + summary + remark** 三处：「装扮槽上穿不了任何东西」「表里最低的槽是 12」 | `TheAvatarBagIsServedTests.cs` | **就地改写并改名**为 `Nothing_goes_on_a_costume_slot_but_the_costume_whose_slot_it_is`。它此前**还绿着纯属巧合**（目标槽 0，两个模板的槽是 14 和 3）。由评审 B-2 发现 |
| C-9 | 判据 `The_archive_calls_this_template_an_avatar_and_names_its_worn_slot` 的 `<summary>` 与第一个 `<remarks>`：「the archive names **no** slot to wear it in」「没有第二半就会被穿上而不是被拒、根本不会有记录」 | `RefusedEquipIsRecordedWithItsTemplateTests.cs` | **就地改写**。与 C-7 同族——`<summary>` 说着与三行之下的断言相反的话。**是两轮共同的遗漏**：我这轮还在它上方三行加了 `CoatAvatarSlot = 3`，却没回头读它。由评审 B-4 发现 |
| C-10 | 「**时装取得下、穿不上**」——写在「今天没有的东西（逐条，不是『大概没有』）」清单里，零限定 | `AGENTS.md:566` | **就地划掉并加注**「2026-09-08 已修」 |
| C-11 | 小节标题「时装：种类已经对齐，**但仍然穿不上**」 | `AGENTS.md:611` | **就地改写**标题。前一轮加的作废注写的是「下面这一整节」，**覆盖不到标题本身** |
| C-12 | `RefusedEquipIsRecordedWithItsTemplateTests` 的**类级 `<remarks>`** 两处：「none of the names in it ends in `avatar]`」「the archive gives it no worn slot」 | 同文件 `:26`、`:41` | **就地改写**。**这是同一句话的第三个副本**，且在**打开文件第一眼**的位置；生产注释正是把读者指到这个文件来的。由第三轮评审 B-5 发现 |
| C-8 | `AGENTS.md` 时装节的三个数：「`ByName` 的 **16** 个键」「认不出 **17** 个类型名、**66,596** 个模板」及其复算行 | `AGENTS.md` | **就地加注**：表现在 **27** 个键；认不出的只剩 **6** 个、**268** 个模板。原表不动（它记的是当时的扫描）。由评审 B-3 发现 |

**搜法**：把本件推翻的三件事各自的主题词（`AuraSkinAvatar` 的位置、`avatar]` 键的有无、
时装能不能穿）在第一节列出的搜索集里各扫一遍。

**初版报「共 5 处」，那是不全的。经三轮独立评审补出，现共 12 处**（C-1..C-12）。
`.md` 侧的五条命令我确实逐条敲过且命中数吻合；**栽的是另外两类**：

- **`.cs` 侧跑了没读**（C-6、C-7、C-9、C-12）——命令敲了，输出没看。
  **同一句话在同一个文件里有三个副本**（方法 `<summary>`、类级 `<remarks>` 两段），
  我分三轮才改完，每一轮都只改了当时被点名的那一处、没往上下翻；
- **`.md` 侧扫的是「说法」而没扫「数字」与「标题」**（C-8、C-10、C-11）——
  `grep` 那几个主题词命中不到「16 个键」这种数字，也命中不到写在小节标题里的断言。

**第二类是新的一层**：本清单第一节列的搜法全是按**说法**扫的，
而一份文档里最容易过期的恰恰是**数字**和**标题**。

---

## 六、证据

### 判据

| | 值 |
|---|---|
| 基线（`80bdb144`，合并后实测） | **2335 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 现在 | **2361 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 差额 | **+26** ＝ 新增 23（`CostumeSlotsWereMeasuredTests`）＋ 净增 3（删 1 加 4，见 T 节） |

字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧**未变**（该文件不在 diff 里）。

**一次既知偶发**：建契约前跑全量时 Application 套件红 1 条，单独复跑 1070 全绿。
**当时没抓到栈根**（grep 过滤掉了），所以只有「复跑即绿」这一条依据，不是完整的三条判别。
本件实现期间的历次全量未再复现。

### 变异验证（文件级备份 + `sha256sum`，用完立刻还原并核散列；未用任何 git 还原类命令）

| 变异 | 做了什么 | 红了几条 | 红的是谁 |
|---|---|---:|---|
| M1 | `[coat avatar]` 的槽 3 改成 4 | **6** | **四个**判据文件都有：逐键判据的上衣那条、表范围判据、`The_archive_calls_this_template_an_avatar_and_names_its_worn_slot`，以及 `TheAvatarBagIsServedTests` 的三条穿戴判据 |
| M2 | 把 11 个键**整段删掉**（回到本件之前） | **19** | Protocol 6、Infrastructure 13 |

M1 是本轮最有价值的一条：**只改一个数字**，三个互不相同的判据文件同时变红——
说明这 11 个数不是只被一处断言镜像着，而是从三条不同的路被读到。

还原后 `sha256sum -c` 均 `OK`，复跑 2361/2/0。

### 「原有键值一字未改」的证据

```
$ git diff -- Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
```

字典部分**只有 11 行 `+`，零行 `-`**；`["[weapon]"] = 12` 起的每一行都在 diff 之外。

---

## 七、本清单的已知局限

- **本件没有实机验证。** 所有者明说测试在交付之后（「做完以后我们再一起测试」）。
  本件保证的是**服务器接受穿戴请求并落库**，**不保证客户端把时装画得出来**。
- **一条从未通过真时装的路，本件之后第一次会通电**：NOTI `0x0002` 子类型 1 的穿戴条目、
  以及 ITEM_LIST 装备栏容器里的时装条目，**两者都会写外观块**。
  那些编码在更早的成果里写好并有判据（含珠宝槽发 18 字节那次），
  但**从来没有真时装走过它们**。**这正是「一起测试」该看的地方。**
- **槽 11 的空缺是当前档案的事实，不是协议的事实。** 换一版档案要重扫。
- **「实机量得」这一源在仓内只有 1 件可追溯。** 11 个槽号来自 2026-09-07 的实机日志，
  而仓内留痕的只有上衣那一件（`Document/avatar-container-entry-shape.md`），
  另外 10 行日志任何 `.md` 里都查不到——**日志本身没有归档**。
  由独立验证方指出。**这不影响结论**：即使抽掉这一源，11 个数仍被另两源
  （档案的 `[equipment type]`、上游枚举）独立支撑，而那两源验证方都亲自复现过。
  **但下一个人若要复核「实机」那一列，仓内没有东西可查。**
- **`[breast avatar]` 映到槽 6 而 `[waist avatar]` 映到槽 7** —— 这两个名字与中文部位名
  （胸部/腰带）的对应是从实机日志的模板号反推的，**档案没有说 `[breast avatar]` 是什么部位**。
  槽号本身是量到的，与命名无关。
