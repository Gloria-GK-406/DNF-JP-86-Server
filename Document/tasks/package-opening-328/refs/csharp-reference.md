# 两套 C# 参照服务端的礼包解析与开启（逐类对照）

调查日期 2026-11-14。两套源码均只读，本文不含任何拷贝到本仓的代码。

## 约定

- **U** = 上游 `E:\Project\DNF\ServerS4A21\`。下文 `U:Game/...` 省略前缀 `Server\DfoServer\`；PVF 解析器在 `U:Tool/PvfLib/Models/StackableItemFile.cs`（下称 **U-PVF**）。
- **T** = 第三方商业端反编译 `E:\Project\DNF\Research\third-party-s4a21-v3.3\decompiled\`。`T:` 后接 `DfoServer\` 下的命名空间目录；PVF 解析器在 `T:PvfLib/PvfLib/StackableItemFile.cs`（下称 **T-PVF**）。反编译行号以当前文件为准。
- 「代码明确」= 读代码可直接确认；「推测」= 根据 PVF 样本或命名推断，参照实现没有给出依据。
- PVF 样本来自当前客户端 `E:\Project\DNF\DNFClient\Script.pvf`，用本仓 `Tool/PvfProbe` 的 `dump/values/where` 命令读出。

## 0. 参照实现的整体结构（两套相同，除非特别注明）

### 0.1 协议入口

| CMD | U 注册 | 处理函数 | 走向 |
|---|---|---|---|
| 0x00A0 `USE_BOOSTER_ITEM` | `U:Network/Protocol/GameProtocolHandler.cs:599`；T `GameProtocolHandler.cs:908` | `Handle_OPEN_SELECTABLE_PACKAGE`（`U:Network/Handlers/InventoryHandler.Package.cs:1068-1123`） | 先用自选包布局解析，走 `TryOpenSelectablePackage`；失败则回退 `TryHandleBoosterOpen` → `TryUseBoosterItem`（随机包与 [cera package] 也走这里） |
| 0x0207 `OPEN_CERAPACKAGE` | U `:613`；T `:953` | `Handle_OPEN_AVATAR_PACKAGE`（`:1012-1066`） | 先按装扮包解析走 `TryOpenAvatarPackage`；失败回退 `TryHandleOpenPackage0207`（`:1465-1490`） |
| 0x0218 | U `:614`；T `:954` | `Handle_USE_BOOSTER_ITEM` | 同 `TryHandleBoosterOpen` |
| 0x00D0 / 0x0450 | U `:624` 等 | `Handle_OPEN_MAGIC_BOX(_SINGLE)` | `TryUseBoosterItem`，不带选中项 |
| 商城购买 | — | `InventoryCeraShopRuntimeService.TryResolveMallAutoOpenRequests`（`U:Game/Inventory/Services/InventoryCeraShopRuntimeService.cs:979-1027`） | [cera package]、[cera booster]、魔锤套装（10007472–10007477）购买时直接展开发内容物，不进背包 |

### 0.2 客户端请求布局（参照实现的解析，代码明确）

- 0x00A0 普通/自选：`slot u16 + categoryIndex u16 + itemId u32 × N + flag u8`，N≥1，任何 id≤0 整包拒绝（`U:Game/Inventory/SelectablePackage.cs:27-77`）。U 的自测记录了抓包：10006309 传承防具礼盒 selnum=0，body[2..4] 为分类索引，后接该分类全部 5 个 id 和 flag；10003675 selnum=1 只带 1 个 id（`U:SelfTests/BoosterSelectionNumSelfTest.cs:32-72`）。
- 0x00A0 装扮多选：`slot u16 + ctx u16 + itemId u32 × N + N u8 + (itemId u32 + option u8) × N`，两组 id 必须逐个一致（`SelectablePackage.cs:79-114`）。
- 0x0207：`slot u16 + N u8 + (itemId u32 + option u8) × N`（`U:Game/Inventory/AvatarPackage.cs:25-56`）；回退路径只取 id 且丢弃小于 1000 的 id（`InventoryHandler.Package.cs:2018-2041`）。
- 尾字节 `flag`（`SelectionFlag`）在自选包路径被原样当作 `AvatarAbilityNo` 写进新物品（`U:Game/Inventory/Services/InventorySpecialConsumableService.cs:505,539`）。flag 的语义参照实现没有说明（推测：装扮属性选择）。

### 0.3 PVF 读取

`StackableItemFile.Parse` 先按树建好节点，再统一抽奖励（U-PVF `:457-467`）：

- `BoosterRewards` ← `[booster info]` 的每个子节点（`ParseBoosterInfo`，`:603-617`），每个子节点一个组号（从 1 递增），按「加权」规则解析（`AddWeightedRewards`，`:834-904`）。
- `BoosterSelectionRewards` ← 每个 `[booster select category]` 的子节点（`ParseBoosterSelection`，`:619-634`），组号 = 分类序号（从 1 起，按出现顺序），按「成对」规则解析（`AddPairRewards`，`:906-971`）。
- `PackageRewards` ← `[package data]`（id,count 对）+ `[package data include usable period]`（id,count,days 三元组）（`:636-676`）。
- `[package data selection]` **不在** PvfLib 内解析，只在商城购买服务里另读（见第 1 节）。
- `[booster category num]` 读首个整数存入 `BoosterCategoryNum`（`:416`），全仓未使用；`[booster select category]` 头上的两个数（如 `0 0`）也未使用，分类索引完全按出现顺序。

### 0.4 发放公共规则（代码明确）

- 统一经 `InventoryRewardGrantService.TryPlanOne`（`U:Game/Inventory/Services/InventoryRewardGrantService.cs:476-506`）分流：premium/服务道具 → 激活服务；账号点券券（2681917 / 2681762）→ 钱包；史诗碎片 → 碎片钱包；0、2、复活币、账号仓库币 → 虚拟计数；其余按物品种类插入对应背包。
- 非堆叠物（装备、装扮、宠物）按数量拆成多个独立请求（`InventorySpecialConsumableService.cs:927-979`）。
- 期限：奖励行带 `UsablePeriodDays` 时为「现在 + 天数」（`:993-998`）；否则 `InventoryCreateService` 按目标物品自身的 `[usable period]` / `[expiration date]` 计算（`U:Game/Inventory/Services/InventoryCreateService.cs:470-496`）。自选包路径还会把目标物品的绝对到期时间写进奖励，已过期则整包拒绝（`SelectablePackage.cs:222-223,327-338`；`InventorySpecialConsumableService.cs:447-449,490-492,524-526`）；商城展开则跳过已过期的静态物品（`skipExpiredStaticItems`）。
- 交易属性：装备/装扮只按目标物品自身 `[attach type] [sealing]` 设 `SealFlag`（`InventoryCreateService.cs:329-347`）。`TradeRestriction` 在礼包开启中从不设置（全仓搜索仅强化解放等处写 1）。
- 背包放不下：先在规划副本上排布，放不下的交给 overflow sink（例如邮件），sink 拒收则整包回滚、返回背包满（`InventorySpecialConsumableService.cs:587-675`）。
- 不认识的物品（两个 lst 都查不到，`ItemKind == "special"`）在自选包路径直接拒绝，史诗碎片除外（`:494-497,528-531`）。

---

## 1. Package 类里的 `[package data selection]`

**支持情况**

- U、T 都**只在商城购买时**支持（代码明确）。背包内开启时两者都**忽略**这些段，只发固定部分：
  - U 背包开启：`[cera package]` → `stackable.PackageRewards`（`U:Game/Inventory/Services/InventoryPackageRewardResolver.cs:102-106`），不读 `[package data selection]`。
  - T 背包开启：同样（`T:DfoServer.Game.Inventory/InventoryPackageRewardResolver.cs:144-148`），且 T 让 0x0207 回退路径也接受 [cera package]，同样只发 `PackageRewards`（`T:.../InventorySpecialConsumableService.cs:266-274`）。
  - [booster]/[cera booster]/[booster random] 带 `[package data selection]` 时，两者都只按 `[booster info]` 抽奖，选择段被忽略。
- 商城购买（U `InventoryCeraShopRuntimeService.cs:994-1007,1073-1141,1165-1194`；T `InventoryCeraShopRuntimeService.cs:891-1060`）：
  - 每个 `[package data selection]` 段 = 一组；段内是 `itemId count` 对，`itemId<=0` 跳过。
  - 固定部分 `PackageRewards` 全发；装扮固定件若请求带装扮选项，必须每件都有选项。
  - 每组必须且只能选一个：`groupIndex` 不越界、不重复，`selectionIndex` 在组内越界即拒绝；最后要求「已选组数 == 组数」。
  - 选中项不带期限（`UsablePeriodDays=0`）、AbilityNo 0。
  - T 在固定部分额外调用 `ResolveServicePackageDurations`（见第 7 节）。

**PVF 字段语义**（样本 `stackable/cash/2012_new_year_pack/2012_new_p_ag1.stk`，见 `Document/pvf-diff-20261113/diff.txt:496141-496149`）

```
[package data]  2689874 1 2690002 1 …   ← 固定发放 (id, count)
[package data selection] 2681484 1 2681485 1                 ← 第 0 组，二选一
[package data selection] 2681464 1 2681466 1 … 2681478 1     ← 第 1 组，八选一
```

**客户端需带**：只有商城购买格式已知。商城请求单件时，在 commodityNo 后依次是 `装扮数 u8 + (itemId u32 + option u8)×n`，再 `选择数 u8 + (packageItemId u32 + groupIndex u16 + selectionIndex u16)×m`（`U:Network/Parsers/CeraShop/CeraShopPurchaseRequest.cs:87-140`；T 同构 `T:DfoServer.Network.Parsers.CeraShop/CeraShopPurchaseRequest.cs:178-194`）。注意是**组内序号**而不是物品 id。背包内开启这类包时客户端带什么，两套都没有处理，也没有抓包证据。

**硬编码**：无（只有魔锤套装 10007472–10007477 的特例，见第 11 节）。

## 2. `[booster info]` 的 `[charactor]` 段

**支持情况**：U、T 都支持（代码明确），实现一致。

**解析**（U-PVF `:801-832,999-1064`；T-PVF `:949-984,1107-1170`）

- 职业标签取数据中第一个 `[xxx]`；数值用 `-?\d+(\.\d+)?` 抽取，允许小数权重（按最大小数位数整体放大成整数）。
- 数值总数必须是 3 的倍数，否则整段丢弃（不报错）。
- 从第 4 个数开始每 3 个为 `(itemId, weight, count)`。
- **drawCount 取第 1 个数**（U-PVF `:1031`，T-PVF `:1135`），第 2、3 个数被忽略。
- 每个 `[charactor]` 子节点自成一组（组号随 `[booster info]` 子节点递增）。

**PVF 字段语义**（按样本推断，参照实现只部分采用）

```
[charactor]
[swordman] 1 99 1 2683422 100000 1
[/charactor]
```

职业标签之后三个数推测为 `最低等级 最高等级 抽取次数`，之后为 `(itemId, weight, count)` 组。依据：同一礼包里有 `1 99` 与 `1 85`、`1 70`、`1 86` 并存；第三个数有 `2` 的样本（`[at swordman] 1 99 2 44 5000 1 …`），按「抽取次数」理解最合理。
→ **参照实现的偏差（代码明确）**：用第 1 个数（最低等级，几乎总是 1）当抽取次数，第 3 个数为 2 时会少抽；等级范围完全没有检查。

**发放规则**

- 开启时读角色职业（DB `characters.job`），映射为标签：`swordman, fighter, gunner, mage, priest, at gunner, thief, at fighter, at mage, demonic swordman, creator mage, at swordman, knight`（U `InventoryPackageRewardResolver.cs:378-384`、`InventorySpecialConsumableService.cs:575-585`）；T 末尾多一个 `demonic lancer`（`T:.../InventoryPackageRewardResolver.cs:430-434`）。不区分转职和觉醒。
- 只保留无职业标签或标签与角色相同的条目，再按组加权抽取（`RollBoosterRewards`，U `:288-320`）。
- 职业对不上时没有奖励，`TryResolvePackageRewards` 返回 false，开启失败，不消耗礼包（`InventorySpecialConsumableService.cs:162-175`）。本仓普查中 416 个「无奖励」里，按参照实现的读法有多少是职业池，参照代码不能回答。
- 客户端不需要额外字段；职业由服务端自取。

## 3. `[booster selection num]` 为 0 且多奖励

**支持情况**：U、T 都支持，实现相同（代码明确）。

**语义**（`U:Game/Inventory/BoosterSelectionGrantResolver.cs:11-84`；T 同名文件 `:7-80`）

- `selnum <= 0` = **选分类、整类发放**：客户端在 body[2..4] 给出分类索引，并带上该分类的全部 id（抓包见 0.2）。服务端要求每个选中 id 都在该分类内，然后发该分类全部去重后的物品，不管客户端带了几个 id。
- `selnum = K > 0`：选中 id 数不能超过 K，且都必须在指定分类内，只发选中的 id。
- 选中 id 重复、≤0、分类越界：整包拒绝。
- 回退入口（0x0218 等没有分类索引）用选中 id 反查唯一包含它们的分类；分类不唯一时，selnum≤0 只发选中 id；完全没有选中信息且 selnum≤0 时发整表（U `InventoryPackageRewardResolver.cs:117-130`）。

**PVF 样本**：`stackable/10153503.stk`，`[booster category num] 1 6`，`[booster selection num] 0`，6 个分类，每类 5 件装备。
`[booster category num]` 的两个数推测为「是否按类别（职业）先选」+「类别数」，参照实现不读。`[booster category name]` 是界面上的类别名，参照实现不读。

## 4. 选择段里的其他标签

各标签均在 `AddPairRewards`（U-PVF `:906-971`；T-PVF `:1041-1085`）处理。

| 标签 | U | T | PVF 语义（样本） |
|---|---|---|---|
| `[stackable]`、`[equipment]`、`[etc]`、`[creature]` 等通用段 | 支持：`(itemId, count)` 对。**个数为奇数时首个数被当成组号**（`:956-957`），这是启发式写法 | 同 U | 样本 `chn_emblembox_10004971.stk`：`[stackable] 2500031 1 …`，纯 (id,count) 对 |
| `[avatar]`（选择段内） | 支持：个数是 4 的倍数时按 `(id, count, x, y)` 取前两个，后两个忽略（`:911-929`） | 同 U | 后两个字段多为 `3 0`（见第 6 节） |
| `[default select]` | **当作奖励**：首数 n，后接 n 个 id，每个 count=1（`:931-950`） | **整段忽略**（T-PVF `:1065-1068`） | 见下 |
| `[booster equipment upgrade]` | 忽略（`:952-954`），装备按 +0 发 | **支持强化**，见下 | 强化等级 |
| `[random probability]` | 忽略 | 作为 `upgrade=255` 时的随机强化表 | `等级 权重` 对 |
| `[booster equipment separate]` | 忽略 | 忽略（只有 GM 发放支持锻造） | 推测为锻造等级（样本值 0/3/4/5） |
| `[booster equipment grade]` | 忽略 | 忽略（T 的品级 `EquipmentQualityGrade` 只允许 GM 发放，见 `T:.../InventoryCreateService.cs:96-115`） | 推测为装备品级（样本值 3/4） |
| `[cera]` | 通用 `(id,count)` 对 | 同 U | 有两种写法，见下 |
| `[action usable place]` | 与开启无关 | 同 U | 礼包使用地点限制（`[village]`、`[seria room]`、`[dungeon]`…），是顶层字段，不在选择段内 |
| `[booster category num]` | 读入但不用 | 同 U | 见第 3 节 |

**`[default select]`（代码明确 + 样本推断）**

样本 `stackable/event/chn_event_darkknight_updated_2013/chn_darkkinght_box_lv30_2749765.stk`：

```
[booster selection num] 4
[booster select category] 0 0
[default select] 3 2749770 2749785 2749771
[equipment] 2220106 1 … 2224102 1          ← 5 选 1
[stackable] 2749770 5 2749785 1 2749771 5   ← 与 default select 同一批 id
```

所有样本都满足 `selnum = default 个数 + 1`，`default select` 的 id 都出现在同类的 `[stackable]`/`[equipment]` 里。推测 `[default select]` 是界面默认勾选（可能锁定）的必发项，selnum 已把它们计入。
→ U 的处理会产生**数量错误**：`[default select]` 先被解析，count=1；发放时 `definition.TryGetReward(itemId)` 取第一个匹配（`SelectablePackage.cs:149-162`），于是 `2749770` 只发 1 个而不是 5 个。
→ T 丢弃 `[default select]`，数量取自 `[stackable]`，是对的；但两者都**不强制**客户端必须带上默认项，只校验数量 ≤ selnum。

**`[booster equipment upgrade]` / `[random probability]`（T 新增，代码明确）**

- 解析（T-PVF `:718-790`）：每个分类读一次 `[booster equipment upgrade]`，值必须是单个 0–31 的数，或 255；255 时必须有 `[random probability]`，内容为 `等级 权重` 对，等级 0–31、权重 ≥0、总权重 >0 且不溢出，否则**抛 FormatException**（PVF 载入失败）。强化值只挂到同分类内 `RewardKind == "equipment"` 的条目上，`[stackable]` 等不受影响。
- 发放：`ResolveEquipmentUpgrade`（`T:.../InventoryPackageRewardResolver.cs:84-121`）在 255 时按权重随机，每件拆分后的装备各自独立随机（`T:.../InventorySpecialConsumableService.cs:840-859`）；写入 `ItemCore.Upgrade` 只在 `ItemCreateReason.PackageOpen` 且物品种类为装备（kind 1）时生效（`T:.../InventoryCreateService.cs:74-77`）。
- 链路：`SelectablePackageDefinitionResolver.ParseBoosterSelectCategories` 把强化信息带进分类条目（`T:.../SelectablePackageDefinitionResolver.cs:107-142`），自选主路径传入 `AddRewardRequest`（`InventorySpecialConsumableService.cs:443`）。
- 样本：`stackable/10012555.stk`，每类 `[booster equipment upgrade] 255` + `[random probability] 1 20 2 20 3 20 4 20 5 20`（+1~+5 各 20%）；`chn_grow_weapon_box_10005392.stk` 为固定 `12`，同时带 `[booster equipment separate] 5`。

**`[cera]` 的两种写法（样本）**

- `stackable/10151835.stk`：`[cera] 33 1`，即 (id,count)。
- `stackable/chn_490002820.stk`：`[cera] 490002779 1 1 7`，4 个数。U、T 都按两对解析成 `(490002779×1)` 和 `(1×7)`（代码明确）；后者显然是误读。推测与选择段 `[avatar]` 一样是 `(id, count, x, y)`。该样本 selnum=1，所以只有客户端选中的 490002779 会发；selnum=0 时会多发「物品 1 ×7」。

## 5. 自选礼包不同类别之间有重复物品 ID

- U 主路径（0x00A0，有分类索引）：先用 `BoosterSelectionGrantResolver` 校验「选中 id 都在客户端指定的分类内」，这一步按分类区分；但随后取奖励行用全局 `definition.TryGetReward(itemId)`，返回第一个包含该 id 的分类里的条目（`InventorySpecialConsumableService.cs:485-488`）。**同 id 在不同分类数量不同时，U 会发错数量**（代码明确）。
- T 改为 `definition.TryGetReward(request.SelectionContext, itemId)`，取指定分类里的条目（`T:.../InventorySpecialConsumableService.cs:430`）。这是 T 相对 U 的修正。
- 回退路径（无分类索引）：先找唯一包含全部选中 id 的分类（`BoosterSelectionGrantResolver.cs:86-103`）；不唯一时 selnum≤0 只发选中 id，selnum>0 只要求每个 id 属于某个分类。T 在取奖励行时同样只在唯一分类内查找（`T:.../InventoryPackageRewardResolver.cs:219-263`）。
- 两者都**不拒绝**重复 id 的包。

## 6. `[avatar]` / `[special avatar]` 5 元组的扩展字段

**支持情况**：U、T 都**忽略扩展字段**（代码明确）。

- `[booster info]` 下：个数满足 `n ≥ 6 且 (n-1) % 5 == 0` 时，首数为 drawCount，之后每 5 个取 `(itemId, weight, count)`，第 4、5 个数被丢弃（U-PVF `:843-866`；T-PVF `:992-1011`）。
- 选择段 `[avatar]`：4 元组只取 `(id, count)`（U-PVF `:911-929`）。
- 没有任何期限、交易限制或属性来自这两个数。随机包路径发放时 AbilityNo 固定为 0（`InventorySpecialConsumableService.cs:896-925`）。
- 两套源码都找不到 10007316 或 101600023 的硬编码，也没有任何礼包开启路径写 `TradeRestriction`（代码明确）。

**PVF 样本与推测**

- `stackable/cash/luckybag_swordman.stk`（[cera booster]）：`[special avatar] 1 … 40734 1000 1 0 6  40734 1000 1 0 17  40734 1000 1 0 22  40734 1000 1 0 1 …`。同一 id 带不同的第 5 个数，推测第 5 个数是装扮属性（能力）编号，即 AbilityNo。
- `stackable/cash/chn_20141111_SAO_Package/weaponavatarbox1_10007316.stk`：`[avatar] 1 101600023 100000 1 3 0`（池中是武器）；`diff.txt` 中选择段 4 元组普遍是 `id 1 3 0`。第 4 个数的含义两套参照都没有给出，本仓「3 = 不可交易」的解释在参照里找不到依据。

## 7. 奖励是服务类道具

**参照实现的判定**（代码明确）

- 「服务」只认两类（`U:Game/Premium/PremiumService.cs:22-50`）：
  1. `etc/premiumlist_new.etc` 中 `[item] id [term] 天数` 登记的道具（`PremiumCatalog.cs:20-22,72-80`）；
  2. `etc/cerashop.etc` 中恶魔契约 8 个槽的服务道具（`DevilContractCatalog.cs:52,107-118`）。
- 这些奖励在 `TryPlanOne` 第一步被识别为 `Premium`，**不进背包**（`InventoryRewardGrantService.cs:486-487,724-751`），开启成功后由 handler 调 `PremiumService.ActivateAndNotify` 激活（`InventoryHandler.Package.cs:1041-1046,1092-1097`）。count 表示份数，每份时长取自目录。
- 不在这两个目录里的「服务类」道具（例如 `[name tag]` 改名卡这类可堆叠消耗品），参照实现**当普通道具插入背包**，没有特殊处理。本仓把 cerashop.etc 服务分类、服务路径都并入 Service 后拒绝开启，参照里没有对应的拒绝。
- 账号点券券 2681917（Happy Token Cera）、2681762（Token Cera）直接入账（`U:Game/Inventory/SpecialRewardRouter.cs:25,32,34-63`）；物品 0、2、复活币、账号仓库币走虚拟计数（`InventoryService.cs:859-884`）。

**T 新增**

- `ResolveServicePackageDurations`（`T:.../InventoryPackageRewardResolver.cs:174-183`、`T:DfoServer.Game.Premium/DevilContractCatalog.cs:160-179`）：[cera package] 里的恶魔契约服务道具，count 按「商城商品时长 × count ÷ 道具单位时长」换算，除不尽则抛异常。
- 其他与服务相关的新增：点券券 490734050（[booster] 类，`[int data]` 为点券数，直接加到账号，`T:.../CeraVoucherDefinition.cs`、`InventorySpecialConsumableService.cs:118-134`）；黑钻兑换道具（`:109-112`）；角色栏扩展券（`:95-108`）。

## 8. `[stackable type] [creature]` 宠物消耗品被当成宠物本体

**参照实现明确区分**（代码明确）：

- 宠物本体 = **装备**文件里 `[equipment type] creature` → `KindCreature`（`U:Game/Inventory/ItemMetadataResolver.cs:1210-1218`）。
- `stackable` 文件的 `[stackable type] [creature]` 或 `[feed]` → `KindCreatureConsumable`（`:1201-1208,1230-1233`），是可堆叠物，放宠物栏消耗品区（`U:Game/Inventory/Services/ItemSlotBoundService.cs:103-106`，宠物本体区 0–139、宠物装备 140–188，消耗品区为 `PetConsumableSlotStart..End`）。ID 25 宠物换名卡的使用也要求 `KindCreatureConsumable`（`U:Game/Inventory/Pets/PetCreatureRenameService.cs:78`）。
- 礼包开启不另作判断，交给上面的通用分类。[booster info] 里的 `[creature]` 池（如 luckybag 的 `1 63007 1000 1 …`）是 drawCount + (id, weight, count)，id 是装备类宠物。

## 9. 奖励物品的 `[attach type]` 解析异常

**参照实现不校验**（代码明确）：开启发放时不读奖励的 `[attach type]`，也不会因此拒绝。

- 只有两处用到：
  1. `IsSealed`：`AttachType.Trim('[',']',' ') == "sealing"` 时设 `SealFlag=1`（`ItemMetadataResolver.cs:66`；`InventoryCreateService.cs:334,347`）；
  2. 仓库插入校验，只对个人/账号仓库生效（`InventoryInsertService.cs:770-845,935-955`）。
- 因此缺失、`[sealing trade]`（装备 143 个）、不闭合的 `` `[account` ``（stackable 2 个）都能正常发放。`[sealing trade]` 不会被视为封装。
- 当前 PVF 分布（PvfProbe `values`）：stackable `[trade]` 26342、`[account]` 7009、`[free]` 6213、`[trade delete]` 1832、`[trade limit]` 820、缺失 54、`[account` 2；equipment `[trade]` 99199、`[sealing]` 10063、`[free]` 6663、`[trade delete]` 2035、`[account]` 198、缺失 178、`[sealing trade]` 143。

## 10. `[booster info]` 里 `[equipment]`/`[etc]` 等池格式

**参照实现宽松**（`AddWeightedRewards`，U-PVF `:834-904`；T-PVF `:986-1039`，代码明确）：

- `n ≥ 4 且 (n-1) % 3 == 0`：首数为 drawCount，之后为 `(id, weight, count)`。
- 否则：**没有 drawCount**（=1），从 0 开始按三元组读，多出的 1–2 个数静默丢弃。
- `[broadcast]`：`n ≥ 5 且 (n-1) % 4 == 0` 时为 drawCount + `(id, weight, count, 广播标志)`；T 会读广播标志（`T-PVF :1087-1105`，随机盒 `:901`）。
- 同名段重复出现（如两个 `[etc]`）各自成组，互不合并（U-PVF `:881-884` 注释）。
- id≤0 的行跳过；weight 取 `max(0, w)`；count 取 `max(1, c)`。组总权重为 0 时整组跳过，不报错。

**参照实现会误读的写法（代码明确 + 样本）**：`stackable/event/chn_event_20150618_startserver/chn_30days_pack_10007846.stk`（[cera booster]，恶魔契约 30 天全套）

```
[booster info]
[cera] 2681934 1 2681931 1 2681932 1 2681933 1 2681929 1 2681930 1 2682205 1 2682206 1
```

这是 8 个 (id, count) 对、共 16 个数。参照实现按 `(16-1)%3==0` 取 drawCount=2681934，后面的三元组全部错位。U、T 的恶魔契约购买都直接读这份 `BoosterRewards`（U `DevilContractCatalog.cs:150-157`；T `:126-136`），会因 id=1 不是服务道具而拒绝。所以参照实现实际上**不支持**这种 [booster info] 下的固定对写法。

## 11. 其他 stackable 开箱类型（参照支持、本仓未处理或处理不同）

| 类型/条件 | 实现 | 说明 |
|---|---|---|
| [cera package]、[cera booster] 商城购买直接展开 | U `InventoryPackageRewardResolver.cs:251-272`、`InventoryCeraShopRuntimeService.cs:979-1071`；T 同 | 购买 n 件就展开 n 次，同 id 同期限合并 |
| 魔锤套装 10007472–10007477（[booster]） | U `InventoryPackageRewardResolver.cs:11-14,322-344`；T `:13-19,370-395` | 硬编码：不抽奖，固定发 10007367 魔锤 + 10007368 魔锤盒；也会在商城购买时展开 |
| 2682272 赛利亚幸运值盒 | U `InventorySpecialConsumableService.cs:143-275`；T `:156-240` | 幸运值满触发双倍；T 可用 `event-magic-box.config.json` 替换奖池（`T:.../MagicBoxConfiguration.cs`） |
| [random upgradable legacy] | U `InventoryPackageRewardResolver.cs:111-115`、U-PVF `:726-773` | `[RANDOMBOX]` 下每个 `[int data]` 一组，从第 4 个数起 `(id, weight, count, 广播)`；`[sealing removal item]` 为开盒材料（`ResolveNeedMaterial`） |
| [legacy]、[upgradable legacy]（抽奖罐） | U `Game/Lottery/LotteryItemDefinitionProvider.cs:48-54`、U-PVF `:678-724` | `[int data]` 为 (id, weight) 对或 (id, weight, count) 三元组 |
| [usable cera package] 装扮礼包 | U `AvatarPackage.cs:117-156`、`InventorySpecialConsumableService.cs:340-400,1404-1427` | 固定 `[package data]`；客户端为每件装扮带 option，件数必须与包内装扮数一致 |
| 账号点券券、碎片、虚拟币作为奖励 | 见第 7 节 | 不进背包 |
| T：点券券 490734050 | `T:.../CeraVoucherDefinition.cs` | [booster] 类，`[int data]` = 点券数 |
| T：[quiz item] | `T:.../QuizItemService.cs:116` | 答题道具 |
| T：[random reward item]（节日烟花） | `T:.../FestivalFireworkDefinition.cs:27` | |
| T：[random upgrade separate] | `T:DfoServer.Game.ItemUpgrade/SeparateUpgradeTicketDefinition.cs:39` | 随机锻造券 |
| T：卢克稀有礼盒兼容保护 | `T:DfoServer.Game.Raid.Luke/LukeRewardBoxCompatibility.cs` | 10160379–10160382、10160384 中有未适配装备时拒绝开启，不消耗 |

## 附：两套的差异汇总（T 相对 U）

1. 新增 `[booster equipment upgrade]` + `[random probability]` 强化发放（第 4 节）。
2. 丢弃 `[default select]`，避免 U 的数量错误（第 4 节）。
3. 自选主路径按分类取奖励行，修正重复 id 跨分类时的数量问题（第 5 节）。
4. 0x0207 回退路径接受 [cera package]（只发固定部分）。
5. 恶魔契约服务包按时长换算 count；点券券、黑钻兑换、角色栏扩展券等特殊道具（第 7 节）。
6. 职业标签多一个 `demonic lancer`；随机盒与 `[broadcast]` 读广播标志。
7. 未变：`[charactor]` 的 drawCount 读取偏差、5 元组扩展字段被忽略、`[package data selection]` 只在商城支持、`[booster equipment separate/grade]` 不支持、`[cera]` 4 元组误读、[booster info] 固定对写法误读。
