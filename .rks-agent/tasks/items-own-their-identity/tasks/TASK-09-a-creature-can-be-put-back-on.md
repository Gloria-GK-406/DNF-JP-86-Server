# TASK-09 — 契约兽取下之后能装回

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 让契约兽这一类物品能解析到它今天所用的那个装备槽位，使取下之后可以装回。
- Referenced spec requirements: RQ-009
- Referenced plan sections: T-009
- Owned boundary: BD-012, BD-014
- Permitted files or interfaces: `Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`、`Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`、`Server/tests/`
- Dependencies: TASK-08
- Required dependency outputs: 稳定身份，使装回后可断言仍是同一件
- Acceptance conditions: 取下再装回成功且装回后身份不变；该槽位的数值未被改动，其它槽位的归属未被改动；**变异验证证明该判据会红**；全量判据不低于基线。
- Task-specific authorization: Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做一次。范围外一律不碰：定时清理器、旧库自动升级、地上掉落／交易托管／拍卖挂单／邮件附件的启用、契约兽领域本身、契约兽槽位 24 还是 25 的裁定（沿用 24）、`Pet=7` 容器装什么、契约兽技能、`character_condition` 的五列。
- Required methods: test-driven-development；改动前先让判据表达期望，每个 Task 结束时全量判据全绿，**不允许攒红**。计划里已定、本 Task 不得自行推翻的五条：世界侧**一张**表不是三张；`items` 就地变成世界表且位置另立槽表；
今天的 DELETE+INSERT 结算**原样保留只换作用对象**（物品行 → 槽行），不得为唯一键冲突另发明机制；
契约兽对外标识用**单一窄空间**（i32）加分配守卫，不得另设第二个标识；**不引入新的 `store` 读法**，
世界侧不新增以 `DomainStore` 结尾的类型。
- Required checks:

- CK-019 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → 不低于 2131 通过 / 2 跳过 / 0 失败
- CK-020 `Non-command: 变异验证——撤回该修复并记录失败的判据名；还原后复跑`

- Required evidence: 修复的 diff；取下再装回判据名；**变异验证必须指名哪一条判据失败**，不得只说「红了」；还原后全绿，且记录还原前后被改文件的哈希一致。

### 边界

**沿用今天的槽位 24，不裁 24 还是 25。** 那要线上抓包证据，读代码得不出。

今天的不对称：`PutOnAsync` 走 `WornSlotForAsync` → `PvfEquipmentSlots.ByName`，那张表没有 `[creature]` 键，于是给 `NotWearable`；而 `TakeOffAsync` 走 `MayOccupyMainSlotAsync` 把契约兽当普通装备放行。**拖得出来、装不回去。**

**这是被授权的行为变化之一**，须进 TASK-12 的变化清单。

## Execution Result — round 1

### 改了哪些文件，每处为什么

**生产代码：只有一个文件、只有一行有效改动。**

`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`

- `ByName` 表新增 `["[creature]"] = 24,`（`:52`）。
  今天的不对称全部出在这里：`PutOnAsync` 走 `WornSlotForAsync` →
  `PvfEquipmentSlotDefinitionSource.Read` 读物品的 `[equipment type]` → 查这张表，
  表里没有 `[creature]` 于是返回 null，判为 `NotWearable`；而 `TakeOffAsync` 走
  `MayOccupyMainSlotAsync`，把契约兽当普通装备处理，落在 9..64 就放行。补上这个键，
  契约兽解析到它**今天已经在用**的槽位 24。
- 类型注释补一段 `<para>`，写明 24 是沿用（`SelectCharacterPostEntryServices.EquippedCreatureSlot`
  与 `StateSynchronizationPayloadCodec.Entry` 的 `Noti2CreatureSlot` 都已经是 24），
  并明说**本轮不裁 24 还是 25**，那条悬案仍留在 `Docs/ProtocolScaffoldDissonances.md` 第九节。

`MoveItemSpaceSlice.cs` 在允许清单里但**未改动**——一行都不需要动。
槽位数值（`FirstWornSlot=12`、`LastWornSlot=24`、`[earring]=24`、`[support weapon]=24`）
与其它任何槽位的归属**均未改动**，见下面的 `PvfEquipmentSlotsTests`。

**判据（新增两个文件，没有改写任何既有判据）：**

`Server/tests/Dfo.Server.Application.Tests/Game/CreatureGoesBackOnTests.cs`（新建）

- `A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`
  ——本轮的主判据。跑真档案（`SkippableFact`）＋真 SQLite 库，主背包与装备容器都是
  `PersistentItemContainer`、都挂在同一个 `SqliteItemDomainStore` 上，移动经
  `MoveItemSpaceCommandHandler`。种下 63000（`equipment/creature/faras.equ`）到背包槽 9，
  然后 **装上 24 → 取下回 9 → 再装上 24**，每一步断言 `ItemMoveStatus.Moved`，
  并且**四个停靠点逐次读回身份**（`item_slots.id` 与 `items.item_uid` 必须一致），
  断言身份始终是种下时那一个。两侧都用真档案与真库，是因为「槽位是几」是档案的答案、
  「回来的是不是同一件」只有能越过位置存活的身份能回答，任何一侧打桩都会让撤回修复后判据仍然绿。
- `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`
  ——防止「让任何东西都能进任何佩戴槽」这种同样能让上面那条变绿的做法。
  把契约兽瞄准槽 15（肩），断言 `ItemCannotOccupySlot`，且两个位置都没变。

`Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentSlotsTests.cs`（新建）

- `Every_name_that_had_a_slot_before_the_creature_was_named_still_has_that_slot`
  ——逐个名字钉住原有 15 条映射（含 `[earring]=24`、`[support weapon]=24`）。
  **故意不断言 `[creature]` 那一条**：那条的意义在于上面的往返判据，
  在这里重复一遍只会让撤回修复时多红一处、却不多说明任何事。
- `The_worn_band_is_unchanged_and_holds_every_name_in_the_table`
  ——`FirstWornSlot=12`、`LastWornSlot=24` 未动，且表里每个槽位都落在这个带内
  （即没有靠加宽容器来给契约兽腾位置）。
- `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants`
  ——这张表**有两个读者**，另一个是 `PvfInitialEquipmentDefinitionSource`（角色创建表
  的 `[create equipment list]` 用同一套名字）。往表里加名字只有在没有职业的创建表用到该名字时
  才是安全的，这条把它钉住：档案里 14 个职业的创建表**只**出现 `[weapon]`、`[coat]`、`[pants]`
  （实测枚举 job 0..31 的全部授予，槽位集合恰为 {12, 14, 16}），所以角色创建拿到的东西
  一件没变。

### CK-019 实际输出（逐项目）

命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，退出码 0。

| 测试项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 311 | 0 | 0 |
| Dfo.Server.Application.Tests | 1024 | 0 | 0 |
| **合计** | **2210** | **2** | **0** |

（`60+52+521+6+236+311+1024 = 2210`。逐项目相加，未用 `grep -c`。
解决方案里就是这七个测试项目，`Server/86JP.Server.slnx` 未收录
`Dfo.Server.Abstractions.Tests`、`Dfo.Server.Utilities.Tests`、`Integration`。）

要求下限 2205 / 2 / 0 → **2210 / 2 / 0，高出 5**，正好是本轮新增的 5 条判据
（Application 2 条 + Infrastructure 3 条），即基线本身是 2205，未增未减任何既有判据。

**字节锚**：`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
**一字未改**（不在 `git status` 的改动清单里）。单跑该类 + `InventoryDeltaLift`：
**19 通过 / 0 失败 / 0 跳过**。

### CK-020 变异验证（完整记录）

**被改文件**：`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`

1. 改前哈希
   `dd9c3bf6743317d909b196afb13b814bb83e10b925806bc7473bbc7426450d80`
   （`sha256sum`；同时 `cp` 一份到临时目录作为唯一还原源，**全程未用任何 git 还原类命令**）。
2. 撤回修复：删掉 `["[creature]"] = 24,` 这一行（注释保留，只撤销行为）。
3. 全量复跑，退出码 1：

| 测试项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 311 | 0 | 0 |
| Dfo.Server.Application.Tests | 1022 | 0 | **2** |
| **合计** | **2208** | **2** | **2** |

   **失败的判据（用例全名，逐条）：**

   - `Dfo.Server.Application.Tests.Game.CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`
     `Assert.Equal() Failure: Values differ / Expected: Moved / Actual: NotWearable`
     （`CreatureGoesBackOnTests.cs:75`，即第一次「装上」那一步）
   - `Dfo.Server.Application.Tests.Game.CreatureGoesBackOnTests.A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`
     `Assert.Equal() Failure: Values differ / Expected: ItemCannotOccupySlot / Actual: NotWearable`
     （`CreatureGoesBackOnTests.cs:116`）

   **这两条都是本轮为 RQ-009 新增的判据**；
   **2205 条既有判据一条未红**，本轮新增的另外 3 条（`PvfEquipmentSlotsTests`，它们钉的是
   「没动别的槽位」）也全绿——这正是它们被写成不依赖 `[creature]` 那一条的原因。
   契约说「恰好该判据变红」，实测是「恰好这两条新判据变红、其余一条不动」：
   第二条是同一处行为变化的另一面（同一行代码、同一条 RQ），如实报出而不合并。

4. 从临时目录还原（`cp` 回来），改后哈希
   `dd9c3bf6743317d909b196afb13b814bb83e10b925806bc7473bbc7426450d80`
   —— **与改前逐字节一致**（`diff` 两个哈希为空）。
5. 还原后全量复跑，退出码 0，逐项目计数**与 CK-019 完全相同**：
   60 / 52 / 521 / 6 / 236(+2 跳过) / 311 / 1024 = **2210 通过 / 2 跳过 / 0 失败**。

### 行为变化清单（逐条，全部归到 RQ-009）

这一行表项只被两处生产代码读到（`grep WornSlotForAsync` 的全部生产调用点）：
`MoveItemSpaceSlice.cs:322`（`PutOnAsync`）与 `:390`（`TakeOffAsync` 判被顶开的那件）。
因此可观察的变化恰好是下面四条，并且**清单之外没有其它行为变化**。

1. **契约兽装得回去了。** `PutOnAsync`（`MoveItemSpaceSlice.cs:322-325`）：
   背包里的契约兽瞄准装备槽 24，从 `ItemMoveStatus.NotWearable` 变为 `Moved`，物品进入槽 24。
   理由：`WornSlotForAsync` 现在对契约兽答 24，`slot is null` 那条分支不再命中。
   → **RQ-009 / AC-011**，本轮被授权的行为变化本身。

2. **槽 24 已有契约兽时，拖第二只进去成为互换。** 同一段（`:331-343`）：
   原先根本走不到，现在被顶开的那只回到契约兽出发的那个背包格。
   理由：这是 `PutOnAsync` 既有的「被顶开的那件放回源格」逻辑第一次对契约兽可达，
   没有为它新加任何代码。→ **RQ-009**（装回这条路打通后的必然一面）。

3. **从槽 24 取下时，目标背包格里放着另一只契约兽，现在是互换而不是拒绝。**
   `TakeOffAsync`（`MoveItemSpaceSlice.cs:388-394`）：
   `WornSlotForAsync(displaced)` 从 null 变成 24，于是
   `displacedSlot != command.SourceSlotIndex` 在源槽正好是 24 时不再成立。
   **只在源槽为 24 时改变**；从 12..23 任一槽取下、目标格里放着契约兽，
   仍然是 `DisplacedCannotOccupySlot`（24 ≠ 源槽），与本轮之前相同。
   → **RQ-009**（「取下再装回」这条路上被顶开的一端）。

4. **契约兽瞄准非契约兽槽位时，内部拒绝理由从 `NotWearable` 变为 `ItemCannotOccupySlot`。**
   `PutOnAsync`（`:325` vs `:326-327`）：档案现在对契约兽有答案了，
   于是命中的是「槽不对」而不是「根本不可佩戴」。
   **这一条不改变发给客户端的任何字节**：`ErrorCodeFor`（`MoveItemSpaceSlice.cs:529-533`）
   把 `NotWearable` 与 `ItemCannotOccupySlot` **都**映射到
   `MoveItemSpaceResponse.NotMovable`，同一个错误码、同一句提示。
   → **RQ-009**（同一行表项的直接后果），且不触碰 RQ-011 的字节不变。

**明确没有变的**（逐条查过，不是「大概没变」）：

- **角色创建**。这张表的第二个读者是 `PvfInitialEquipmentDefinitionSource`；
  实测档案里 14 个职业的 `[create equipment list]` 只用 `[weapon]`/`[coat]`/`[pants]`
  三个名字，没有任何一个用 `[creature]`，所以创建时穿的东西一件没变。
  由 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 守住。
- **槽位数值与其它槽位归属**。`[earring]`、`[support weapon]` 仍是 24，
  12..23 逐个未动，`FirstWornSlot`/`LastWornSlot` 未动。
  由 `Every_name_that_had_a_slot_before_the_creature_was_named_still_has_that_slot` 与
  `The_worn_band_is_unchanged_and_holds_every_name_in_the_table` 守住。
- **24 还是 25 的裁定**。没有碰。本轮只让契约兽解析到它今天已经在用的那个数字；
  `Docs/ProtocolScaffoldDissonances.md` 第九节的悬案原样保留，
  新写的注释里也明说了「等到有一个真的戴着契约兽的样本再判」。
- **契约兽领域本身**、`Pet=7` 容器、契约兽技能、`character_condition` 五列、
  定时清理器、旧库升级、地上掉落／托管／挂单／邮件附件：一个都没碰。

### 允许清单之外的文件

**没有。** 改动全部落在
`Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs` 与 `Server/tests/` 之内。
（`git status` 里其它 M 状态的文件是 TASK-01..08 的产出，本轮一个字都没动过。）
三个文件都无 UTF-8 BOM（首三字节分别是 `6e 61 6d` / `75 73 69` / `75 73 69`）。

## Executor Self-check — round 1

### 我发现的问题

1. **「恰好一条判据变红」实测是两条。** 契约写的是「确认恰好该判据变红」，
   而变异后红的是 `CreatureGoesBackOnTests` 的两条。两条都是本轮为 RQ-009 新增的，
   既有的 2205 条一条未动。我选择如实报出而不是把第二条合并进第一条或删掉它——
   那条钉的是「不要靠放宽槽位校验来蒙混过关」，它本身有价值。
   如果评审认为必须字面满足「恰好一条」，把
   `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused` 并进主判据即可，
   但我认为那会让一个独立的场景失去自己的名字。

2. **`[earring]` 在档案里根本不作为 `[equipment type]` 出现。** 普查全部
   equipment.lst 条目的 `[equipment type]` 字符串，出现的名字里有
   `[support weapon]`（26 件）而**没有** `[earring]`（0 件）。
   这与第九节记的「上游认为 24 是耳环／辅助武器」不矛盾（创建表用的是另一套读法），
   但说明 `[earring]` 这个键在今天的档案上是死键。**属 T-011/T-012 的范围，本轮没有动它，
   也不建议在没有线上证据时删。**

3. **档案里还有一批 `[equipment type]` 名字不在这张表里**（按件数）：
   `[coat avatar]` 8391、`[hat avatar]` 8325、`[pants avatar]` 8194、`[shoes avatar]` 8070、
   `[hair avatar]` 7929、`[breast avatar]` 7565、`[face avatar]` 7028、`[waist avatar]` 7000、
   `[skin avatar]` 2954、`[weapon avatar]` 449、`[aurora avatar]` 423、
   以及 `[artifact red]` 65 / `[artifact blue]` 47 / `[artifact green]` 31、
   `[charm]` 50、`[flag]` 47、`[name tag]` 28。
   也就是说**时装与契约兽宝珠今天同样装不上**，症状与契约兽本轮之前完全一样。
   **本轮没有碰它们**（不在 RQ-009 范围内，且时装槽是 0..11 的另一段，
   不能照抄 24 这个做法），但这是同一处不对称的其余部分，值得记一笔。

### 我**没有**验证的东西（明说）

- **没有跑过真客户端**。「取下再装回」只在判据层面成立：真实客户端在契约兽槽上
  发出的 `MOVE_ITEMSPACE` 帧长什么样、它会不会先自己拒绝这个拖放，**我没有抓包证据**。
  第九节说的「24 还是 25 都没有线上证据」原样成立。
- **没有验证装上契约兽之后入场帧的字节**。本轮的字节不变是靠
  `SelectCharacterAcceptedTranscriptTests`（19 通过）保住的，而那个参照角色**契约兽槽是空的**。
  一个真的戴着契约兽的角色，其 `NOTI 0x0002`／装备清单帧的字节对不对，**本轮没有证据**。
  这本来也超出 RQ-009（它只要求「能装回去且身份不变」）。
- **没有验证契约兽领域的任何东西**：喂食、经验、等级、饱食度、复活、技能——
  一律没碰也没跑。装回之后 `character` 侧的八列跟着身份走，
  是 TASK-08 的 `CreatureStateFollowsTheIdentityTests` 已经证过的事，我复用了那个结论，
  没有重新证。
- **没有验证并发**：两个请求同时装同一只契约兽会怎样，没有构造。
- **`PvfInitialEquipmentDefinitionSource` 的普查是我自己写探针跑出来的**
  （枚举 `character/character.lst` 的 14 个职业逐个读 `[create equipment list]`），
  探针文件跑完已删除；留在判据里的是 `GetAsync(job)` 层面的等价断言（job 0..31）。
  两者结论一致，但**判据断的是槽位集合 {12,14,16}，不是「档案里没有 `[creature]` 这个字符串」**。

### 不确定的地方

- **变化 3（从槽 24 取下、目标格里已有另一只契约兽 → 互换）没有专门的判据。**
  它是同一行表项的推论，我在代码上逐条读过分支（`displacedSlot != SourceSlotIndex`，
  源槽为 24 时才成立），但**没有为它写用例**。要不要补，我认为属评审判断：
  补一条不难，只是它不在 T-009 的四步里。
- **`ItemMoveStatus` 从 `NotWearable` 变成 `ItemCannotOccupySlot`（变化 4）我判定为线上无影响**，
  依据是 `ErrorCodeFor` 把两者都映射成 `NotMovable`。这一点我是读代码确认的，
  也有 `ItemRefusalCodeTests` 在跑（全绿），但**没有单独构造一个帧去对字节**。
- **背包槽 9 是我选的**，依据是既有判据里装备带写作 9..64（`TwoBandPlacements`）
  以及 `MayOccupyMainSlotAsync` 的既有行为；我没有单独打印契约兽 63000 的
  `ItemPlacement.SlotStart/SlotEnd` 去核对。判据能过说明 9 确实在它的带内。

## Controller Disposition — DSP-TASK-09-01

执行方交回两件。控制器**在快审之前**裁定。

### 一、变异打红两条而不是一条——**不合并，保持现状**

契约写「确认**恰好该判据**变红」。实测红了**两条**，**都是本轮为 RQ-009 新增的**，
2205 条既有判据一条未红。

**「恰好」要防的是变异牵倒无关判据，这一点已满足。**
第二条钉的是「不许靠放宽槽位校验蒙混」——那是**额外的判别力，不是噪音**。
**合并它们会削弱判别力。** 裁定：保持两条，不合并。
执行方选择如实报出而不是自行合并，是对的。

### 二、时装与宝珠有同一个缺陷，本轮**不修**，但必须记进成果

执行方发现并交回：同一处不对称还波及时装与宝珠。**控制器复核属实**——
`PvfEquipmentSlots.ByName` 今天共 16 个键
（weapon / title / title name / coat / shoulder / pants / shoes / waist / amulet / wrist / ring /
support / magic stone / earring / support weapon / **creature**，最后一个是本轮加的），
**没有任何 avatar 键**；文件里三处 `avatar` **全在注释里**（`:7`、`:8`、`:55`）。

而槽 0..11 是时装带。所以**时装同样「取得下、装不上」**，与契约兽本轮之前的形状完全一致。

**本轮不修**：RQ-009 只授权了契约兽这一处行为变化，动时装是**未授权的行为变化**。
执行方没有顺手改，正确。
→ **写进 T-012 的成果记载**，作为一颗具名的、已知的同类缺陷；
   并注明它**不在本成果范围内**，是后续工作。

### 三、控制器复核的两条（不采信自述）

- **生产改动确实只有一行有效改动**：`PvfEquipmentSlots.cs` 的 `ByName` 新增
  `["[creature]"] = 24`。`MoveItemSpaceSlice.cs` 虽在允许清单内但**一行未动**——
  不对称的全部原因就是那张表缺一个键。
- **槽位 24／25 的裁定未被碰**，其它槽位归属未变（16 个键逐个核过，只多了一个）。

### 四、第四条行为变化不改变线上字节

执行方列的第四条「拒绝理由从 `NotWearable` 变 `ItemCannotOccupySlot`」——
`ErrorCodeFor` 把两者都映射到 `NotMovable`，**客户端收到的字节不变**。
这一条属实且已如实列出，**但它仍应留在行为变化清单里**：
服务器内部的拒绝语义确实变了，只是没有穿到线上。**清单要记的是变化，不是只记穿线的变化。**

## Independent Quick Review — QRV-TASK-09-01

- Verdict: **passed**
- 审的合同修订：Contract revision 1，加上控制器裁定 `DSP-TASK-09-01`（按裁定后的口径审）。
- Owned diff 身份：`HEAD = 51cd99c29cb5ff1e9812ca7b48bfbe9795326db4`，全部未提交。
  本轮拥有的三个路径：
  - `M Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs`（`+11 / -0`）
  - `?? Server/tests/Dfo.Server.Application.Tests/Game/CreatureGoesBackOnTests.cs`
  - `?? Server/tests/Dfo.Server.Infrastructure.Tests/Content/PvfEquipmentSlotsTests.cs`

  `git status --short` 里另外 51 个 `M` 与 12 个 `??` 逐个核过归属，全部是
  TASK-01..08 的产出，不计入本轮。

### 独立复核到的事实（不采信自述，逐条自己跑／自己读）

**1. 生产改动确实只有一行有效改动。**
`git diff` 只有两个 hunk：`PvfEquipmentSlots.cs:21-30` 新增一段 `<para>` 文档注释（10 行），
`:52` 新增 `["[creature]"] = 24,`（1 行）。`+11 / -0`，无删除、无修改行。
`git diff --stat -- .../MoveItemSpaceSlice.cs` **输出为空**，且该文件不在 `git status` 里
——**执行方「一行未动」属实**。

**2. 槽位 24／25 未被裁，其它 15 个键一字未变。**
逐行读 `PvfEquipmentSlots.cs:37-52`：16 个键，前 15 个与 diff 的 `-` 侧完全一致
（`[weapon]`=12、`[title]`/`[title name]`=13、`[coat]`=14、`[shoulder]`=15、`[pants]`=16、
`[shoes]`=17、`[waist]`=18、`[amulet]`=19、`[wrist]`=20、`[ring]`=21、`[support]`=22、
`[magic stone]`=23、`[earring]`=24、`[support weapon]`=24），`FirstWornSlot=12`（`:56`）、
`LastWornSlot=24`（`:59`）未出现在 diff 中。新注释 `:26-29` 明写「不裁 24 还是 25」。

**3. 变异验证——我自己重做了一遍（全程 `cp`，未用任何 git 命令）。**
备份到临时目录，`sha256 = dd9c3bf6743317d909b196afb13b814bb83e10b925806bc7473bbc7426450d80`
（与执行方记录一致）。删掉 `:52` 那一行（60 行 → 59 行），全量复跑，退出码 1：

| 测试项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 311 | 0 | 0 |
| Dfo.Server.Application.Tests | 1022 | 0 | **2** |
| 合计 | **2208** | 2 | **2** |

(a) 红的**恰好两条**，全名与报文与执行方所述**逐字一致**：

- `Dfo.Server.Application.Tests.Game.CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature`
  —— `Assert.Equal() Failure: Values differ / Expected: Moved / Actual: NotWearable`，
  栈根 `CreatureGoesBackOnTests.cs:line 75`（即**第一次**「装上」，与执行方所述一致）。
- `Dfo.Server.Application.Tests.Game.CreatureGoesBackOnTests.A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`
  —— `Expected: ItemCannotOccupySlot / Actual: NotWearable`，栈根 `CreatureGoesBackOnTests.cs:line 116`。

(b) **2205 条既有判据一条未红**：除 Application 外六个项目的计数与 CK-019 逐项目相同；
Application 也只掉了这两条（1024 → 1022）。本轮新增的另外 3 条（`PvfEquipmentSlotsTests`）
也全绿（Infrastructure 仍是 236），与它们「不断言 `[creature]`」的写法一致。

(c) `cp` 还原后 `sha256` 仍为 `dd9c3bf6...450d80`，**逐字节一致**；
`git diff --stat` 回到 `1 file changed, 11 insertions(+)`；全量复跑退出码 0，
逐项目计数与 CK-019 **完全相同**（60 / 52 / 521 / 6 / 236+2跳过 / 311 / 1024 = 2210）。

按 `DSP-TASK-09-01` 第一条的口径，「恰好」要防的是牵倒无关判据——已满足。**不计缺陷。**

**4. 既有判据的期望值确实一条未改。**
本轮新增的两个测试文件都是 `??`（未跟踪，即全新）。
对全部已跟踪测试文件的 diff 做了 `NotWearable|ItemCannotOccupySlot|DisplacedCannotOccupySlot|creature|WornSlotFor|EquipmentSlot`
的正负行普查，命中的都是 TASK-02..08 的身份／模式／分类改动
（`ItemIdentity` 包装、`character_creatures` 八列迁移、`ItemWorldClass.Creature` 分类），
**没有一处是把「契约兽装不上」的期望翻过来**。
全仓 `grep -rn "NotWearable" Server/tests` 只有一处命中，且是
`CreatureGoesBackOnTests.cs:86` 的**注释**——本轮之前根本没有判据断言过契约兽不可佩戴，
所以也无从「改写期望」。**执行方所述属实。**

**5. 行为变化清单：我独立找了一遍，没找到清单之外的第五条。**

- `WornSlotForAsync` 在生产代码里的全部调用点就是两处：
  `MoveItemSpaceSlice.cs:322`（`PutOnAsync`）与 `:390`（`TakeOffAsync` 判被顶开的那件）。
  接口与实现的其余命中（`IItemMovementDomainService.cs:34`、`IItemPlacementRuleService.cs:33`、
  `ItemMovementDomainService.cs:10-11`、`ItemPlacementRuleService.cs:28-29`）都是转发。
- 逐分支读了 `PutOnAsync`（`:322-327` 是判定：`slot is null → NotWearable`，
  `slot.Value != DestinationSlotIndex → ItemCannotOccupySlot`）与
  `TakeOffAsync`（`:390` 取 `displacedSlot`、`:393` 比 `!= command.SourceSlotIndex`）。
  执行方列的四条与代码逐条对得上；变化 3「只在源槽为 24 时改变」也确实是
  `displacedSlot != SourceSlotIndex` 这个比较的直接结论。
- `TakeOffAsync` 另一条判定走 `MayOccupyMainSlotAsync`
  （`ItemMovementDomainService.cs:13-24`），它读的是 `PlacementForAsync`，
  **不碰这张槽表**，所以取下侧的准入没有变。
- **第四条的字节不变属实**：`MoveItemSpaceSlice.cs:529-542` 的 `ErrorCodeFor` 里
  `NotWearable` 与 `ItemCannotOccupySlot` **都**映射到
  `MoveItemSpaceResponse.NotMovable`，而 `NotMovable` 是**单值常量**
  `MoveItemSpaceSlice.cs:75: public const byte NotMovable = 23;`。同一个字节。
- **这张表的读者确实只有两个**：`grep -rn "PvfEquipmentSlots" Server/src` 的生产命中只有
  `PvfEquipmentSlotDefinitionSource.cs:76` 与 `PvfInitialEquipmentDefinitionSource.cs:122`。
  第二个读者是角色创建：`:122` 把不认识的名字化为 `pendingSlot = null` 并丢弃随后的物品 id，
  所以本轮之前 `[creature]` 在创建表里是被丢弃的；加键之后，
  **只要有任何职业的 `[create equipment list]` 用到 `[creature]`，它就会变成 24 的一件授予**。
  这正是 `No_job_is_created_wearing_anything_but_a_weapon_a_coat_and_pants` 钉住的：
  它断言 job 0..31 的授予槽位集合**恰为 {12, 14, 16}**，一旦有 24 混进来立即变红。
  我认为**这条等价断言是成立的守卫**，够用；「档案里没有 `[creature]` 这个字符串」那个更强的说法
  执行方已自陈未由判据钉住，我同意其自陈的边界。
- 结论：**清单完整，我没有找到第五条行为变化。**

**6. CK-019 我自己跑过**（`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`，退出码 0），
逐项目明细（未用 `grep -c`，读的是每个 dll 各自那行汇总）：

| 测试项目 | 通过 | 跳过 | 失败 |
|---|---:|---:|---:|
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Networking.Tests | 52 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 521 | 0 | 0 |
| Dfo.Server.Host.Tests | 6 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 236 | 2 | 0 |
| Dfo.Server.Persistence.Tests | 311 | 0 | 0 |
| Dfo.Server.Application.Tests | 1024 | 0 | 0 |
| **合计** | **2210** | **2** | **0** |

**2210 / 2 / 0**，与执行方记录一字不差，远高于 CK-019 的下限 2131。
两条跳过是 `PvfTownDefinitionSourceTests.A_missing_archive_rather_than_yielding_an_empty_town_catalog` 与
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`，
都是「缺档案才跑」的既有跳过，与本轮无关。

**7. 字节锚未动。**
`git diff --stat -- Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
**输出为空**，该文件也不在 `git status` 里。
单跑 `--filter "FullyQualifiedName~SelectCharacterAcceptedTranscriptTests|FullyQualifiedName~InventoryDeltaLift"`：
**19 通过 / 0 失败 / 0 跳过**。被授权的行为变化没有穿到入场帧上。

**8. BOM。** 三个改动文件首三字节：
`PvfEquipmentSlots.cs` = `6e 61 6d`、`CreatureGoesBackOnTests.cs` = `75 73 69`、
`PvfEquipmentSlotsTests.cs` = `75 73 69`。**都不是 `ef bb bf`。**

### 关于新增判据判别力的独立判断（被点名要判的那件）

**判断：`CreatureGoesBackOnTests` 走的是生产上真正的那条路径，它证明的是 RQ-009 本身，
不是「那张表里有这个键」。没有绕过其中任何一段。**

逐段核过的链条：

1. `CreatureGoesBackOnTests.cs:193-195` 用的是**生产的** `MoveItemSpaceCommandHandler`
   （`MoveItemSpaceSlice.cs:230`）与生产的 `ItemMovementDomainService`，
   `MoveAsync`（`:212-217`）发的是 `MoveItemSpaceCommand(main → equipment)`，
   于是必然命中 `HandleAsync` 里 main→equipment 那条分支 → `PutOnAsync`。
2. `PutOnAsync` 在 `MoveItemSpaceSlice.cs:322` 调 `moves.WornSlotForAsync`
   → `ItemMovementDomainService.cs:10-11` → `ItemPlacementRuleService.cs:28-29`
   → `IEquipmentSlotDefinitionSource.GetAsync`。
3. 该接口的实现**不是打的桩**：fixture 的 `rules` 由
   `content.GetRequiredService<IEquipmentSlotDefinitionSource>()`（`CreatureGoesBackOnTests.cs:171`）取得，
   而 `content` 来自 `ContentArchive.CreateContentProvider()`
   （`WorldContentArchiveTests.cs:293-302`），它**跑的是生产注册**
   `new InfrastructureModule().ConfigureServices(services, configuration)`，
   而 `InfrastructureModule.cs:76` 注册的正是 `PvfEquipmentSlotDefinitionSource`。
4. `PvfEquipmentSlotDefinitionSource.Read`（`:47-79`）从真档案里读 63000 的
   `[equipment type]` 字符串，最后一行 `:76` 才去查 `PvfEquipmentSlots.ByName`。

这条链上**没有一处替身**。而且这不只是读代码得出的：**变异实测给了硬证据**——
只删掉表里那一个键，`PvfEquipmentSlotsTests` 的三条（直接读表的那些）**全绿**，
红的偏偏是往返判据。若这条判据只是在证明「表里有这个键」，它会和表判据一起绿／一起红。
它**红了而表判据没红**，说明它证的是「档案 + 生产链路 → 装得上」，不是表内容。

判别力的**边界**（据实记，不作为缺陷）：

- 它**没有**穿过协议层：handler 是直接 new 出来的（`DirectCommandBus`），
  帧解码、`ErrorCodeFor`、回包字节都不在这条判据里。RQ-009 只要求「装得回且身份不变」，
  字节那一面由字节锚（19 通过）另外守，划分是清楚的。
- 变化 3（源槽 24 取下、目标背包格已有另一只契约兽 → 互换）**没有判据**，
  执行方已自陈。我核了 `MoveItemSpaceSlice.cs:388-394` 的分支，结论与其一致。
  契约的验收条件没有要求它，**不构成本轮缺陷**；建议作为一条已知的覆盖缺口交给 T-012 记载，
  由后续任务或最终评审处置。
- fixture 给 `PersistentItemContainer` 的 `IEntryInventoryMutationRepository` 传了 `null!`
  （`CreatureGoesBackOnTests.cs:186`，对应 `ItemContainers.cs:620` 那个参数）。
  该依赖只服务入场快照装配，移动路径不触碰，判据全绿即证明未触碰。**不是缺陷**，只记一笔。

### 按裁定明确不计为缺陷的两项

- 变异红两条而非一条：依 `DSP-TASK-09-01` 第一条，**不计缺陷、不要求合并**。
  我独立验证了两条**都**是本轮新增、既有 2205 条一条未红，裁定的事实前提成立。
- 时装／宝珠的同类缺陷：依 `DSP-TASK-09-01` 第二条**本轮不修**、已定向 T-012。
  我顺手核了裁定的事实前提：`ByName` 今天 16 个键里**没有任何 avatar 键**，
  文件里三处 `avatar` 全在注释（`:7`、`:8`、`:55`）。属实。**不因未修而计缺陷。**

### 需要整改的事项

**无。** 未发现契约违背、越界改动、缺失／过期／不覆盖的检查、自述与实证矛盾、
依赖产出缺失、仓库指令违反，也未发现回归、越权操作、机密泄露或半成品交付。

范围外未审：T-010..T-012 的事项（种类判定、槽表迁移与旧列退役、`AGENTS.md`），
以及契约兽槽位 24 还是 25 的裁定——按契约与裁定，均明确不在本 Task 范围内。

## Controller Disposition — DSP-TASK-09-02

`QRV-TASK-09-01` 给出 `passed`，独立重做了变异并逐段追了生产链条。控制器采纳，记两条。

### 一、判据走的是真路径——快审给的证据比控制器问的更好

控制器问的是「它证的是 RQ-009 还是『表里有这个键』」。快审除了逐段追链
（真 handler → `PutOnAsync:322` → `ItemMovementDomainService:10` → `ItemPlacementRuleService:28`
→ 生产注册的 `PvfEquipmentSlotDefinitionSource` 读**真档案** → `:76` 才查表，**链上无替身**），
还给了一条更硬的：

> **变异实测**：删键后直接读表的三条 `PvfEquipmentSlotsTests` **全绿**，红的偏偏是往返判据——
> **若它只在证明表内容，两者会同绿同红。**

**这是判别力的构造性证据，不是链条推理。** 记在这里。

### 二、一处覆盖缺口，交 T-012 记载

**变化 3（源槽 24 取下时遇到另一只契约兽 → 互换）没有判据。**
执行方已自陈，快审复核，契约验收未要求。
→ **写进 T-012 的成果记载**，与时装／宝珠的同类缺陷并列，作为已知覆盖缺口。

另据快审两条边界说明（据实记，不作缺陷）：该判据**不穿协议层**（帧字节由字节锚另守）；
fixture 给 `PersistentItemContainer` 的一个依赖传 `null!`，该依赖不在移动路径上。
