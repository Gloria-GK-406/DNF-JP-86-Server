# A21 商城商品与发货声明取证

日期：2026-09-09。范围：当前只读 PVF；不是购买成功的实机证据。

来源：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA-256 `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。商品目录、字段值、token 类型及标签存在性为 **Confirmed（归档事实）**；购买时展开 `[cera package]`、期限单位为天、物品创建策略为 **High（参照行为）**，需购买实机回归。

## 目录格式

`etc/cerashop.etc` 的 Identifier 是分节，数值只接受 Int32；QuotedString 名称不能当作数值偏移读取。

| 分节 | 行宽 | 零基列与语义 |
|---|---:|---|
| avatar | 6 | 0 商品号、1 物品、2 档位（从1开始）、3/4 额外支付、5 价格；负价格查装备的档位 |
| coin/item/material/recoveryitem/premium/creature/community item | 9 | 0 商品号、1 物品、2 数量、3 金币、4 未解释支付、5 点券、6 名称、7/8 尾字段 |
| package/regular package/community package | 11 | 0 商品号、1 礼包、2 通常为0（不是零奖励）、4 点券、5 名称；其它条件不静默丢弃；默认尾8/9为-1，尾10为分类 |
| visual | 8 | 0 商品号、1 装备、5 点券、6 名称；当前全部是 name tag 权益 |
| selectable character premium | 9 | 1 权益物品、5 点券、6 名称；含商品号-1的标题行 |
| charac premium package | 9 | 1 权益物品、2 点券、3 名称 |

真实入口：`[item]` token 209148—209156 是 `110000,3,1,0,0,3600,名称,0,0`。道具3路径 `stackable/cash/river_lethe.stk`，`[stackable type]` 的值是 QuotedString `[etc]`，不是新的 Identifier；购买交付一个遗忘河之水，不执行退点。

`[buy only cera]` / `[buy only cera point]` 是**物品ID**集合；`[not stackable buy]` 是**商品号**（100075/100494）。`[buy restrict item]` 四整数一组，第二列商品号。尚未兑现的购买限制产生诊断。`[restrict buy level cashshop]` 是购买等级；`[minimum level]` / `[maximum level]` / `[usable job]` 是使用条件，不能混作购买限制。

## 时装与宠物

商品120000/120001/120002均交付39032×1；目录档位1/2/3不是数量。路径 `equipment/character/swordman/avatar/cap/agear_gdwyvern2.equ` 的 `[avatar type select]` 前三组是：

```
7,0,0,300,0,0,0
30,0,0,660,0,0,0
0,0,0,1980,0,0,0
```

第四组 `0,0,0,4500,0,0,2` 后跟两个 QuotedString `[A socket]`。解析必须按最后一列跳过socket字符串，不能对整个节盲做7列整数切分。价格组前四列是期限、金币、其它支付、点券；4/5未解释标志及socket档仍可明确拒绝。期限声明保留原值，下游按明确的日期边界兑现，负值拒绝。

同装备 `[avatar select ability]` token78—97为五个四元组 `index,QuotedString stat,QuotedString operator,numeric amount`，合法能力号0—4。当前内容源仅接受可解释的这种属性选项形状；其它能力结构返回诊断。无客户端实测证明未知variant可使用。

商品105016→63022×1/19800；`equipment/creature/egg_bobo.equ` 的 `[creature species]`=20，`creature/creature.lst` 20→`Bobo/Bobo.cre`，`[start level]`=1。蛋仍为63022，不能用 `[output index]`=63023自动孵化，也不能用文件stem寻找宠物（egg_bobo不同于bobo）。其他饱食度/经验等新实例字段不由本证据定义。

商品101075→2747175×1/1580；装备类型 `[artifact blue]` 归宠物装备。此分类由商城声明返回，不能沿用公共装备元数据的普通kind1推导目标背包。

## 礼包与权益

102425/102805→2683477，目录数量列0，点券5300；`stackable/cash/chn_20140311_occupation/chn_occupation_special_2_cera_2683477.stk` 声明 `[cera package]` 与 `[package data include usable period]`：`2683470,1,0;2683473,1,0`，购买等级50。发货组件类型继续单独查内容；2683470的使用动作不在购买时执行。

101235→2682779；`cash/chn_20130226_Level_Potion_Package/chn_level_4059_potion_Package.stk` 的 `[package data]` 有9个二元组，`[package data selection]` 有 `2682768×50` 或 `2682769×50`。选择组保持独立，不一次发出所有选项。三元组的第三列保留使用期限。只对 `[cera package]` 声明展开；普通可使用礼包、booster与随机箱保留箱子实例。

103945→490700700混有33、45、10000389、2660410等权益；下游必须查完所有组件并整包拒绝。幸运魔锤10007473的特殊购买行为尚未解释，源明确诊断。

权益识别来自以下联合证据，绝不能把所有 `[etc]` 一并禁售或一并当实体：

- `etc/premiumlist_new.etc` 的 `[target item]` 与嵌套 `[item]` 集合；尤其35由target item给出，33由item给出。
- `etc/cerashop.etc` 的 visual / selectable character premium / charac premium package，以及 `[common character premium detail]` 八整数组第三列。
- `etc/accountcargo.etc` `[upgrade info]` 每六整数最后一列为商品号。
- 有出处的权益路径：`cash/skilltree_plusitem.stk`、`cash/safe_upgradekit*.stk`、`cash/chn_account_cargo/account_cargo*.stk`、`cash/character_slotkit.stk`、`cash/inven_upgradekit*.stk`、`cash/chn_20140819_avatar_inven/*`、`cash/chn_character_rename.stk`。

必需目录或权益识别文件不存在/空白时返回MissingContentFile诊断，不能在权益集合为空后继续按实体出售。未知商品/物品返回null；所有声明诊断由下游购买规则显式拒绝或有界兑现。

## 可复现检查

```
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet run --project Tool/PvfProbe -- lst equipment/equipment.lst 39032 350
dotnet run --project Tool/PvfProbe -- tokens equipment/character/swordman/avatar/cap/agear_gdwyvern2.equ 120
dotnet run --project Tool/PvfProbe -- lst stackable/stackable.lst 2682779 220
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --filter FullyQualifiedName~PvfCeraShopDefinitionSourceTests
```

新增测试核对真实报价/能力/蛋species/礼包组件/服务分类，并用synthetic typed行核对字符串价格、未知支付、残缺礼包和未知档位可拒绝。它们不证明网络应答或SQLite事务，相关验证由后续阶段承担。

补充边界：物品自身top-level `[expiration date]` / `[usable period]` / `[trade limit]` 的非默认声明影响实例创建；当前商城DTO尚未承载这些来源，因此返回 `UnsupportedInstanceDeclaration`，不忽略后出售。booster内部奖励中的同名标签不作为购买实例事实读取。它们与商品档位/礼包组件提供的期限是不同来源，后两者保留给购买规则兑现。
