# 镇魂 → 释魂（开光）参照调查

日期：2026-11-07。分支：`feature/requiem-weapon-282`（Issue #282）。只读调查，未改代码、数据库，未启动服务。

来源标签：`70Reference`（70 服务端二进制）、`UpstreamReference`（ServerS4A21 源码，参照，不是本仓事实）、
`CurrentImplementation`（本仓代码与协议资料）、`Unknown`（未找到或未闭合）。附录里的 PVF 事实标 `ContentStatic`，
只作旁证。置信度：Confirmed / High / Hypothesis。

## 0. 结论速览

| # | 结论 | 来源 | 置信度 |
|---|---|---|---|
| 1 | “开光”在服务端就是**设计图合成**：玩家在背包里使用释魂设计图（`[stackable type] [recipe] 1`），把镇魂武器作为材料换成对应释魂武器。没有独立的“开光”opcode | UpstreamReference + 70Reference + ContentStatic | High |
| 2 | 最可能承载开光请求的协议号是 **CMD `0x0019` COMPOUND_ITEM**（C2S 7/9B：`u32 槽位, u8 来源模式, u16 数量[, u8, u8]`；S2C 同号 CMD 结果） | UpstreamReference（实机记录 recipe/2651400 正常）+ CurrentImplementation（同号已接入普通设计图） | High |
| 3 | 上游**不做成败判定**：忽略 `[success rate]`/`[fail output]`，一律成功，原地把源武器改成产物模板 | UpstreamReference | Confirmed |
| 4 | 70 **有成败判定**：先扣全部输入（含源武器和设计图），再按成功率掷骰；失败时把产出换成 `[fail output]`，为空则什么都不给 | 70Reference | Confirmed（反汇编） |
| 5 | 继承：上游保留整份实例（强化、增幅、锻造、附魔、品级等），只把封装／交易状态改按产物 PVF 初始化；70 在 `[maintain ability]==1` 时复制强化、品级种子、附魔、增幅、锻造，否则按 recipe upgrade/amplify 表重算 | UpstreamReference / 70Reference | Confirmed（代码），字段语义 High |
| 6 | 本仓 `0x0019` 现在**接不住**释魂配方：内容源要求 `[recipe] 0` 并拒绝带 `[success rate]` 的配方，材料扫描不含装备栏，产物按新物品发放，不会原地转换 | CurrentImplementation | Confirmed |

## 1. 70 服务端（`70Reference`）

对象：`E:/Project/DNF/Research/1995chen-dnf/other/等级补丁/70/df_game_r`（ELF32，共 97,021 个函数符号）。
方法沿用 `Document/outputs/heras-research-20261016/70/collect.py`：从 `.symtab` 读符号，用 capstone 反汇编，
把立即数解析成符号或 `.rodata` 字符串；字符串交叉引用的做法是在 `.text` 里搜字符串 VA 的小端编码。
脚本和中间产物放在会话临时目录，没有写进仓库。

### 1.1 检索范围与阴性结果

- 用正则 `upgrade|requiem|soul|kaiko|reinforce` 搜函数符号，命中 1,079 条，大多是 STL 模板实例；
  另用 `compound|recipe|ModItemAttr|purify|emancipat|transform|success_?rate|failure` 补搜。
- **未找到**名字含 requiem、soul、kaiko（开光）、鎮魂 的函数或 ASCII 字符串，Confidence High（有界阴性：
  没有穷举无符号函数和非 ASCII 文本）。70 里没有专门的“开光”功能，它由通用设计图合成承担。

### 1.2 设计图合成主链（与开光直接相关）

| VA | 符号 | 作用 |
|---|---|---|
| `0x081EAA5C` | `DisPatcher_Compound::dispatch_sig` | 读 `get_int` + `get_byte` + `get_short`，与 A21 `0x0019` 前 7B 形状一致；先查服务限制和物品锁，再调 `compound_item`；失败时用 `put_byte` 发 2 字节错误 |
| `0x0867218C` | `CUser::compound_item(int,bool,ushort,int)` | 取设计图 → 校验 → 数量换算 → 校验材料和背包空间 → `_ProcCompoundCore` → 发结果、刷新、记日志 |
| `0x08671102` | `CUser::_ProcCompoundCore(...)` | 扣材料 → 掷成败 → 换失败产出 → 插入产出 → 继承或重算装备属性 → 广播 |
| `0x08671FDE` | `CUser::isSuccessCompound(int rate)` | 成功率判定 |
| `0x08671EB2` | `CUser::copyItemOption(Inven_Item& dst, Inven_Item& src)` | 属性继承 |
| `0x0867306E` | `CUser::setRecipeEqiupmentOption(...)` | 不继承时按表重算 |
| `0x08682082` | `CUser::getCompoundUpgrade(const CStackableItem*, stEquipCondition)` | 按 `[recipe upgrade table]`/`[recipe amplify table]` 及其 2nd 版查表加随机 |
| `0x08507F1A` | `CInventory::CheckEquipmentItemCondition(...)` | 找出并校验作为材料的源装备 |
| `0x08693A78` | `RecipeInfo::changeOutput(vector&)` | 失败时把产出清空，再换成失败产出 |
| `0x08513D44` | `RecipeInfo::additionalInfo(STStackableScript&)` | 把 PVF 字段拷进 RecipeInfo |
| `0x089AEEC8` | `ImportStackableScript` | 解析 stackable 脚本标签 |

**PVF 标签映射**（Confirmed，字符串交叉引用加存储偏移）：`ImportStackableScript` 用 `ScanInt` 读入
`[success rate]`→`+0x4F0`、`[broadcast type]`→`+0x4F4`、`[maintain ability]`→`+0x4F8`，`[fail output]` 读成
(id,count) 列表存到 `+0x4FC`。`additionalInfo` 再把它们拷到 RecipeInfo 的 `+0x34/+0x38/+0x3C/+0x40`。
RecipeInfo 位于 CStackableItem 的 `+0x174`，所以 `_ProcCompoundCore` 里读的 `+0x1A8/+0x1AC/+0x1B0` 依次是
成功率、广播类型、属性保留。

### 1.3 校验与错误码（`compound_item` 与 `CheckEquipmentItemCondition`）

| 条件 | 错误码 | 置信度 |
|---|---|---|
| 设计图不存在或不是 recipe | `0x11`(17) | Confirmed |
| `_CheckCompoundSkill` 失败（制作技能） | `0x16`(22) | Confirmed |
| 产出中装备多于 1 件（`_IsCompoundResult2MoreEquip`） | `0x17`(23) | Confirmed |
| 材料不足（`CheckNeedItemList`） | `0x15`(21) | Confirmed |
| 装备类材料数量大于 1 | `1` | Confirmed |
| 背包里有**两件及以上**同模板源装备（有歧义） | `0x12`(18) | Confirmed |
| 源装备被锁（`CUser::CheckItemLock`） | `0xD5`(213) | Confirmed |
| 源装备模板查不到 | `0x11` | Confirmed |
| 源装备增幅类型为 `0x80` | `0x13`(19) | Confirmed（`0x80` 的含义 Unknown） |
| 源装备耐久不等于满耐久 | `0xD8`(216) | Confirmed |
| 空位不足（`CalBlankSlot`/`CalNeedSlot`） | `4` | Confirmed |

- 源装备的查找范围：装备类背包段，另外扫一次槽位 3..8（槽位含义未核实，Hypothesis：快捷栏或其他保留段）。
  只要匹配到唯一一件，就把整份 61B `Inven_Item` 拷出来作为“源”。
- `stEquipCondition` 记录：有源装备、源强化值、是否已增幅、增幅类型、增幅值、封装状态（attach type 为 0 或
  实例首字节非 0 时为 1）、再封装次数。

### 1.4 成败与扣费（`_ProcCompoundCore`）

1. **先扣全部输入**：用 `delete_item` 删设计图和所有材料（含源装备），每删一项写一条 7B 变化行
   （`u8, u16, u32`）。Confirmed。
2. `isSuccessCompound(rate)`：GM 强制模式 1 必成、2 必败；`rate==0` 视为必成；否则 `get_rand_int(100) < rate`
   为成功。Confirmed。
   - **单位注意**：70 按百分比掷骰；A21 PVF 的释魂设计图写的是 `[success rate] 1000`，按 70 逻辑就是必成。
     A21 原服的单位（百分比还是千分比）属 Unknown。
3. 失败时调 `RecipeInfo::changeOutput`，把产出换成 `[fail output]`。失败产出为空就写 `0` 后结束，
   **材料已经扣掉，不返还**。Confirmed。
4. 用 `insert_event_items` 插入（成功或失败的）产出。插入数量和预期不符就记 trace 并走错误路径。Confirmed。
5. 对每件插入的装备：
   - 成功，且 `[maintain ability]==1`：`copyItemOption(新装备, 源装备)`。
   - 成功，但未声明保留：只要源条件里有装备，就调 `setRecipeEqiupmentOption`。它按 recipe upgrade/amplify
     表，依据源强化值重算产物强化值（日志串 `mat_up_val/out_up_val/mat_seal`）；源有增幅时调
     `stAmplifyOption_t::assign/identify` 重新赋增幅；还会按产物 attach type（3/4）调整再封装次数。High。
   - **失败**，且 `[maintain ability]==1`：同样对失败产出调 `copyItemOption`，所以退回的武器保留原属性。Confirmed。
   - 成功且 `[broadcast type]` 非 0：调 `compoundSystemMessage` 和 `compoundGuildMessage`，发全服和公会广播。Confirmed。
6. 产物是 `insert_event_items` **新插入的物品**，不是原地改模板。封装和交易状态取产物模板的默认值，
   `copyItemOption` 不碰这两项。High。

### 1.5 属性继承细节（`copyItemOption`，Confirmed 指令级）

复制项：`SetUpgrade(GetUpgrade)`（强化）、`set_add_info(get_add_info)`（装备品级种子，High）、
`+0x33` 字节的低 5 位（锻造等级，High）、`+0x11` 的 4 字节（增幅，Confirmed：随后调用
`stAmplifyOption_t::getAbilityType/Value` 读取它）、`+0x0D` 的 4 字节（附魔卡，High）。
写日志 `Compound+,%u,...`。

**不复制**：耐久（产物取新值）、封装字节、再封装次数、交易限制、物品锁。随机词条和徽章在 70 的 61B 结构里
没有对应位置，不复制。

### 1.6 其他相关但不承载开光的符号（检索记录）

| VA | 符号 | 说明 |
|---|---|---|
| `0x082006D0` | `Dispatcher_UpgradeItem::dispatch_sig` | 普通强化（本仓 `0x0050` 已有） |
| `0x0811E66E` | `WongWork::CItemUpgrade_Separate::ProcUpgrade` | 锻造（本仓 `0x01B7` 已有） |
| `0x0821EB66` | `Dispatcher_CompoundItemByExpertJob::dispatch_sig` | 副职业制作，带 `[compound rate]` |
| `0x08227E00` | `Dispatcher_Compound_Equipment_Upgrade_Card` | 装备升级卡合成，不是武器转换 |
| `0x08218CB6` | `Dispatcher_PurifyItem::process` | 净化 |
| `0x08225EC4` | `Dispatcher_Compound_ExtreamItem` | 极限装备合成，有 `_convertInput_To_Output`，规则不同 |
| `0x080F49B8..` | `Event_Upgrade_Jar` | 活动强化罐 |
| `0x08200B08` | `Dispatcher_ModItemAttr` | 改物品属性，未深入 |
| — | `[upgrade with legacy]` / `[notice legacy with upgrade]` | 在 `importServerParameterScript` 里，是继承强化的服务器参数，未接到 compound 主链，Hypothesis |

## 2. 上游 ServerS4A21（`UpstreamReference`）

仓库 `E:/Project/DNF/ServerS4A21`，HEAD `04e04b4`。已读 `AGENTS.md`。下文路径相对
`Server/DfoServer/`。

### 2.1 检索结果

- 关键词 `镇魂|鎮魂|释魂|釋魂|requiem|开光|開光|kaiko|npc.?item.?upgrade|item upgrade`：除去 ItemUpgrade（强化）
  的命中，真正相关的只有：
  - `Game/Inventory/Services/InventoryCompoundItemRecipeService.cs:356` 的注释（释魂系列 `[sealing]`）。
  - 镇魂地下城区域 26（`Network/Handlers/Dungeon/AntonNormalConquestNotifier.cs:67,125`、
    `SelfTests/SequentialDungeonInfoProtocolSelfTest.cs:18,92`）。
  - 终极镇魂武器礼盒 10003675（`SelfTests/BoosterSelectionNumSelfTest.cs:14,66`，单选 1 个）。
- **未找到**名为“开光／释魂”的独立 handler、opcode 或服务。Confirmed（全仓 grep，排除 `_reference-backup-*`）。
- git 记录：`1c1afd8 fix(compound-item): 合成产物封装态按产物自身PVF初始化`、
  `a8b3272 fix(compound-item): 合成奖励ACK保留位清零修复带强化装备闪退`。两条提交说明都写明对
  “官方镇魂→释魂（recipe/2651400）”做过实机测试，结果正常。

### 2.2 调用路径与协议

```
CMD 0x0019 COMPOUND_ITEM (Network/Core/PacketTypesA21.cs:1256)
 → InventoryHandler.Handle_ENUM_CMDPACKET_COMPOUND_ITEM   Network/Handlers/InventoryHandler.CompoundItem.cs:11
   → TryParseCompoundItemRequest                          同文件 :79
   → InventoryRecipeSeparateUpgradeCommitService.TryCommitCompoundItem
        Game/Inventory/Services/InventoryRecipeSeparateUpgradeCommitService.cs:20
        （OnlineInventoryMutationCommitCoordinator.TryCommit，内存修改与落库作为一个提交）
     → InventoryCompoundItemRecipeService.TryCompoundItemRecipe   Game/Inventory/Services/InventoryCompoundItemRecipeService.cs:11
   → CompoundItemAckBuilder.Build / BuildError            Network/Builders/Inventory/CompoundItemAckBuilder.cs
   → 对被替换的槽先发空 UPDATE_ITEM_LIST，再发全部变化槽的 UPDATE_ITEM_LIST，扣过金币时发金币刷新
```

**请求**（Confirmed，`:79-95`）：`u32 SourceValue`、`u8 SourceIsItemId(==1 时 SourceValue 是模板 id，否则是 Main 槽号)`、
`u16 RequestedCount(为 0 拒绝)`。body 不足 7B 即拒绝，不检查尾部。

**响应**（Confirmed，`CompoundItemAckBuilder.cs`）：
- 失败：`u8 0, u8 errorCode`。
- 成功：`u8 1`；`u8 删除行数` + 每行 `u8 kind, i16 slot, i32 value`（7B；装备整件删除时 kind=1，
  否则 kind 为 ListType；value 是剩余数量，删光时为删除数量）；`u8 奖励行数` + 每行
  `u8 kind(装备=1), i16 slot, i32 templateId, i32 value(装备取品级种子，否则取数量)` 再加 **22B 全零**；
  包尾 **12B 全零**（`:10,21,57`）。
- 提交 `a8b3272` 的结论：奖励行 11..32 字节必须是 0，客户端在这里读到非 0（强化、增幅等）会卡死或闪退；
  真实属性靠随后的 UPDATE_ITEM_LIST 下发。UpstreamReference + 上游实机，High。

### 2.3 规则（`InventoryCompoundItemRecipeService.cs`）

| 规则 | 位置 | 置信度 |
|---|---|---|
| 设计图解析：`[stackable type]` 必须是 `[recipe]`（**不检查**后面的 0/1）；先试 `[int data]` 编码，不行再读 `[input item]`/`[output item]`；金币取输入里 itemId==0 的那一项 | `:368-404` | Confirmed |
| **忽略** `[success rate]`、`[fail output]`、`[maintain ability]`、`[broadcast type]`：全仓 grep 不到这几个标签 | 全仓 | Confirmed |
| 材料不足返回 21，金币不足返回 22，空间不足返回 4，其他返回 17 | `:48,106,113,167` 等 | Confirmed |
| 装备转换的触发条件：materials[0] 和 outputs[0] 都是装备，且换算后数量都是 1（不是 1 就返回 17） | `TryPrepareEquipmentTransform :222-283` | Confirmed |
| 源装备的选法：在 Main 列表按槽号升序找**第一件**模板相符的装备；找不到返回 21。**不检查**歧义、锁、耐久、是否穿戴 | `:263-281` | Confirmed |
| 先在克隆背包上演算一遍（扣材料、扣设计图、转换、试发奖），再在真背包上执行；金币先扣 | `:73-104` | Confirmed |
| 原地转换：拷贝源 `ItemCore`，只改 `ItemId/ItemKind`，写回原槽，再从普通产出里去掉这一件 | `TryApplyEquipmentTransform :316-355`、`TryRemoveTransformedOutput :285` | Confirmed |
| **属性继承**：强化、增幅、锻造、附魔、品级全部保留（整份拷贝）；`SealFlag` 改按产物 PVF 的 IsSealed（释魂系 `[sealing]`，所以产物是封装状态），`TradeRestriction` 归 0 | `ApplyOutputItemDefaults :355-366`；提交 `1c1afd8` | Confirmed |
| 成败：**没有**失败分支，配方合法就一定成功 | 全文件 | Confirmed |
| 实例 UID：`ItemCore.Copy()` 后原槽写回，看上去沿用原实例；上游的 ItemCore 是否带独立 UID 字段未核 | `:331-336` | Hypothesis |

**旁支参照**（不承载开光，但规则可借鉴）：
- `Game/Inventory/Services/InventoryEmancipateService.cs:94-139`（成长券解放转换）：新建产物 core，保留源封装状态；
  按券上的 `*_max` 截断强化、增幅、锻造；`PreservesEquipmentAttributes` 的券整份保留。这说明“转换后保留多少”
  在上游是由内容声明驱动的。
- `Game/ExpertJob/ExpertJobCompoundService.cs:141-149`（`0x00EE`）：按次掷 `Next(100) >= rate` 判失败，失败只是
  少产出，材料照扣。上游唯一真正实现了“制作成功率”的地方。
- 普通强化（`0x0050`，`InventoryHandler.Equipment.cs:198`、`InventoryItemUpgradeService`）与锻造
  （`0x01B7`）：本仓已有对应实现，这里不展开。

## 3. 本仓现状（`CurrentImplementation`）

### 3.1 可复用的能力

| 能力 | 位置 | 说明 |
|---|---|---|
| `0x0019` 协议切片 | `Server/src/Dfo.Server.Protocol/Game/CompoundItem/`（`CompoundItemSlice.cs`、`GameCompoundItemCommand.cs`、`CompoundItemRegistration.cs`） | 已接 7/9B 请求、背包槽模式 0、数量 1..1000；结算放在单个 `MainSettlement` 工作单元，失败回滚；成功后投影 CMD 结果和库存刷新；同序列号重放只发绝对刷新。协议页：`protocol/game/client-to-server/0x0019.md`、`protocol/game/server-to-client/CMD-0x0019.md` |
| 规则与领域分层 | `Dfo.Server.Abstractions/Inventory/IRecipeCraft.cs`；`Dfo.Server.Application/Inventory/RecipeCraftDomainService.cs` | `IRecipeContentSource` → `IRecipeCraftRuleService`（只返回需求和产出）→ `IRecipeCraftDomainService.CraftAsync`（校验、扣费、发放）。扩展点清楚 |
| PVF 设计图读取 | `Dfo.Server.Infrastructure/Content/PvfRecipeContentSource.cs` | 已解析 `[input item]`/`[output item]`，带缓存和 `stackable.lst` 索引预热 |
| 账户和虚拟材料的扣除 | `RecipeCraftDomainService` 的 `plans.RecipeCosts`（SharedMaterial） | 1287 设计图的 3262 走账户余额；释魂配方里的 3037 无色小晶块很可能走同一路径（Hypothesis，待核） |
| 装备实例效果 | `Dfo.Server.Abstractions/Effects/InventoryEffects.cs`：`SetEquipmentUpgradeEffect`、`SetEquipmentEnchantmentEffect`、`SetEquipmentDimensionalEffect`（增幅）、`SetEquipmentForgingEffect`、`SetEquipmentMagicSealEffect`、`DestroyEquipmentEffect`；`ConsumeFromContainerEffect` | 新产物如需逐字段复制属性，可以组合这些效果；源武器的消耗可以用 Destroy 或 Consume |
| 已有装备领域服务 | `Dfo.Server.Application/Inventory/` 下的 Reinforcement（`0x0050`）、Reseal（`0x0051` 黄金蜜蜡，改封装和次数）、Forging（`0x01B7`）、Enchant、MagicSeal（`0x01C8`/`0x0191`）、Quality、Outworld、Disjoint（`0x001A`） | 可参考“同事务改装备实例、保留 UID、生成刷新”的写法；`EquipmentReinforcementFailureRuleService` 可参考业务失败与系统失败的分离 |
| 礼盒选择 | `Dfo.Server.Infrastructure/Content/PvfPackageOpeningDefinitionSource.cs`（含 selection 解析） | 镇魂武器礼盒 10003675 的选取可能直接复用，未逐项核对 |

### 3.2 缺口（释魂配方走当前 `0x0019` 会被拒或语义错误）

| # | 缺口 | 证据 | 置信度 |
|---|---|---|---|
| G1 | 内容源只接受 `[stackable type] [recipe] 0`；释魂设计图是 `[recipe] 1`，所以 `GetAsync` 返回 null，请求以 UnsupportedContent 拒绝 | `PvfRecipeContentSource.cs:62-64` | Confirmed |
| G2 | 带 `[success rate]` 的配方被直接拒绝（代码注释：概率配方不默认成功）。另外它检查的失败标签是 `[failure item]`/`[fail item]`，而 70 和 A21 PVF 实际用的是 **`[fail output]`**，还没有读取；`[maintain ability]`、`[broadcast type]` 也未处理 | `PvfRecipeContentSource.cs:65-67`、Fields 定义 | Confirmed |
| G3 | 材料只从 QuickSlot、Consumable、Material、ExpertMaterial、SharedMaterial 找，**不扫装备栏**，镇魂武器无法作为材料扣除 | `RecipeCraftDomainService.cs:51-52` | Confirmed |
| G4 | 产出一律按 `rewards.PrepareAsync` **新物品**发放（`GrantPreparedItemEffect`）。没有“原地换模板、保留 UID 和实例属性”的效果，也没有“新物品继承源属性”的组合；规则接口 `RecipePlan` 只有 Required/Produced，表达不了成败、失败产出和属性保留 | `RecipeCraftDomainService.cs:60-69`；`IRecipeCraft.cs:8` | Confirmed |
| G5 | 源装备校验缺失：唯一性（70 判 18）、锁（70 判 213）、满耐久（70 判 216）、是否穿戴、有效期，都没有 | 对照 1.3 | Confirmed（缺失）；规则是否适用 A21 Unknown |
| G6 | **CMD 结果编码风险**：当前产物行是 45B，把真实的 Attribute、耐久、封印、增幅、Genuine、Trade 等写进第 11 字节之后。上游实机发现这段非 0 会让客户端卡死或闪退（`a8b3272`），而释魂产物必然带强化、增幅、锻造；当前写法同样把 12B 不透明区放在每行内，上游是 22B 零值行加 12B 包尾。单产物时两边总长都是 45B，但字段内容不同 | `CompoundItemSlice.cs:52-64`；`CMD-0x0019.md` | 风险 High；A21 正确布局 Unknown，需实机校准 |
| G7 | 广播（`[broadcast type] 1`）没有对应实现。是否需要、用哪个 NOTI，Unknown | — | Unknown |
| G8 | 本仓 9B 请求的尾部两字节（当前只收 `00 01`）的含义，对释魂配方是否不同，Unknown | `0x0019.md` 人工区 | Unknown |
| G9 | catalog 状态：`0x0019` 为 WireCaptured + ClientStatic + 70Reference + ContentStatic + CurrentImplementation，未标 ClientVerified；人工区明确写着“既有装备升级……未开放” | `protocol/catalog.md:1489`、`0x0019.md` | Confirmed |

### 3.3 catalog 中相关条目一览

| 协议 | 名称 | 本仓状态（摘自 catalog） |
|---|---|---|
| `0x0019` C2S / CMD | COMPOUND_ITEM | 普通设计图已接；H14 由用户确认；释魂这类特殊配方隔离 |
| `0x001A` | DISJOINT_ITEM | ClientVerified（分解） |
| `0x0050` | UPGRADE_ITEM（强化、增幅 method1） | 已接；强化有 GUI 与重登反馈；增幅待验收 |
| `0x0051` | 品级调整 / 黄金蜜蜡再封装 | CurrentImplementation，未 ClientVerified |
| `0x00CC`/`0x00CD` | 净化 / 属性赋予 | 已接，静态 |
| `0x01B7` | UPGRADE_ITEM_SEPARATE（锻造） | 已接 variant0，待人工验收 |
| `0x0191`/`0x01B6`/`0x01C8` | 魔法封印解封 / 变更 / 重置 | 已接，`0x01C8` happy path 用户确认 |
| `0x00EE` | COMPOUND_ITEM_BY_EXPERT_JOB | 本次没查到本仓接入记录（Unknown） |

**未找到**：catalog 里没有任何名为 item upgrade（物品升级）、开光、释魂、requiem 的独立条目。检索范围是
`protocol/catalog.md` 全文的 compound、recipe、upgrade、seal 等词。

## 附录 A：内容旁证（`ContentStatic`，来自 `DNFClient-A21-debug/Script.pvf`）

PVF SHA-256：`dc250d42a7de1123f7cf7813260ea9359695e71a05df6bc0b5341e8e250f27cf`（客户端副本；与服务端实际使用的
PVF 是否一致**未核**）。读取工具是本仓 `Tool/PvfProbe`，构建输出放到会话临时目录，仓库工作区没有变化。

- `stackable/recipe/2651400.stk`“释魂之真灵短剑 设计图”：
  - `[stackable type] [recipe] 1`；`[string data] [weaving]`；`[minimum level] 84`；`[attach type] [free]`；`[usable job] [all]`。
  - `[input item]`：101000027（镇魂之封灵短剑，`[trade]`，Lv85）×1、10000204 镇魂之石×15、10000327 灵魂之痕×75、
    10000224 小铁柱的火炉使用券×1、3167 上级元素结晶×250、3037 无色小晶块×10000。
  - `[output item]`：101000028（释魂之真灵短剑，`[sealing]`）×1。
  - `[success rate] 1000`；`[maintain ability] normal`（A21 是字符串，70 按 int 读，两版格式不同）；`[broadcast type] 1`。
  - `[fail output]`：101000027（原镇魂武器）×1 + 10150748 浓缩的灵魂×1。
  - `[explain]` 原意：有一定几率把镇魂武器升级为释魂武器；成功时保留原武器的强化、增幅、锻造和附魔，产物为
    封装状态；失败时除武器以外的材料全部消失；合成成功后武器特效消失。
- 整个 PVF 里带 `[success rate]` 的 stackable 正好 31 个，全是释魂设计图：2651400..2651427、10095704（苦无）、
  10154148（长枪）、10154149（战戟）。材料、成功率和失败产出（原武器 + 10150748）完全相同，只是武器 id 不同。
  上游提交里说的“28 个配方”少了这 3 个。
- `[maintain ability]` 出现在 52 个文件里（48 个 `normal`、4 个 `infinite upgrade event`），包括 5 个
  `chn_event_..._circus_dungeon/renewal/*_change` 物品和 10095691..10096052 一批，说明“保留属性的转换”不只用于释魂。

**交叉推论**（Hypothesis，需实机核实）：按 70 的语义，释魂开光会先扣全部材料；成功时产出释魂武器并保留
强化、品级、附魔、增幅、锻造；失败时退回原镇魂武器（属性保留）加 1 个浓缩的灵魂，其他材料全部损失。
`1000` 若是千分比，就是 100% 成功，失败分支在当前内容下实际不会触发；若按 70 的 `rand(100)` 算也是必成。
这和 explain 里的“一定几率”有出入，需要原服样本或 A21 客户端的提示逻辑来裁定。

## 附录 B：对设计的直接提示（不是实现决定）

1. 协议沿用 `0x0019`，不新开 opcode；先确认 A21 客户端对 `[recipe] 1` 设计图是否发同一个 9B 请求、尾部是否不同（G8）。
2. 规则层需要能表达：成功率及其单位、失败产出、属性保留方式（原地转换，或新物品加逐字段复制）、广播。
   `RecipePlan` 需要扩展，或者另开一个专门的规则对象。
3. 扣费顺序和失败语义要按 PVF 和 explain 设计，并用测试覆盖：失败时武器是“退回”（70 是新插入一件）还是
   “保留”（不扣），对 UID 和锁的影响不同。
4. 继承的最小集合（两个参照的交集）：强化、增幅（类型和值）、锻造、附魔卡、品级种子。封装取产物 PVF
   （`[sealing]` 即封装）；交易限制归 0。随机词条、魔法封印、徽章、Rune、再封装次数、锁、耐久在两个参照里
   都没有定论，需单独决策并在测试里写明。
5. CMD 结果的产物行先按上游实机可用的“11B 加 22B 零值，包尾 12B 零值”核对，避免带强化的产物导致客户端闪退（G6）。
