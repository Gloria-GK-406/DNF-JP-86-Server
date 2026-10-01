# A21 角色物品槽位：有界证据裁决

## 当前数据只读补充

2026-09-29，主执行者用SQLite `mode=ro`检查当前运行库：Equipment/list3的slot>=22无记录；Pet/list7只有角色000（character1）的slot0，实例73，core kind5、模板63022。可用该角色采样宠物穿戴请求。该快照不是历史不存在冲突的证明，也不是新映射兼容测试；未改运行库。

2026-09-29，只读取证。`Equipment`、`Pet`、`GuildMedal` 分别指线上 list type 3、7、38；以下数字均是该 list 内的 **wire 槽号**，不是新 store 的内部索引。`70Reference` 和上游 S4A21 代码都不能自动升级为 `ClientStatic` 或 `WireCaptured`。

| 问题 | 当前可用结论 | 证据等级与界限 |
|---|---|---|
| 穿戴普通装备 | 上游 A21 `EquipmentType`：武器 12、称号 13、护肩 15、辅助武器 24；装扮 0..11。A21 实帧已见主背包 9 → 装备 15 的护肩移动。 | 15 为 `WireCaptured`；完整 12..24 为上游参考。槽 11 是上游 `AuraSkinAvatar`，本档案没有相应模板，不等于协议保留空洞。见 [移动记录](../../../../protocol/game/client-to-server/0x0013.md) A21 节、[上游枚举](../../../../../../../ServerS4A21/Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs)。 |
| 穿戴宠物 | **A21 未裁决。** 上游 A21 枚举与投影取件点均为 25；本仓 `PvfEquipmentSlots` 与入场服务使用 24，但源码明确承认此值只是在维持旧实现，缺少真实穿戴样本。24 与上游辅助武器冲突。 | `UpstreamReference / High` 支持 25；A21 `WireCaptured`、`ClientVerified` 均缺。不得把 24 或 25 写成已验证事实；迁移旧存储 24 时尤其不能静默重释。见 [冲突记录](../../../../Docs/ProtocolScaffoldDissonances.md) 第九节、[本仓表](../../../../Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs)、[上游投影](../../../../../../../ServerS4A21/Server/DfoServer/Game/Inventory/Builders/Noti2InventoryProjectionBuilder.cs) 约 210..216 行。 |
| 穿戴宠物装备 | 上游 A21 为红/蓝/绿神器 26/27/28；它的 `PetInventoryLayout` 从同一枚举取值。重构前基线映射 25..27 整体早一格，且会把 25 错当神器。 | `UpstreamReference / High`，尚无 A21 穿戴实帧。见 [上游枚举](../../../../../../../ServerS4A21/Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs) 34..36 行、[上游宠物布局](../../../../../../../ServerS4A21/Server/DfoServer/Game/Inventory/Pets/PetInventoryLayout.cs)。 |
| 宠物背包第一格、分区 | 上游 A21 `Pet=7`：宠物 0..139，宠物装备 **140..188**，宠物消耗品 189..239。若采用上游布局，宠物装备首格是 **140**，业务局部索引为 0。 | `UpstreamReference / High`，**不是** A21 客户端首格的直接证明。现有 A21 参照 `NOTI 0x000D` 的宠物列表为空，无法证明 140；2026-09-22 实机观察到“宠物栏显示但未落首个空位”，数据库具体拾取样本却在主背包 40，说明至少有发放归属错误，不能反推客户端首格。见 [上游边界](../../../../../../../ServerS4A21/Server/DfoServer/Game/Inventory/Services/ItemSlotBoundService.cs) 34..39、90..102 行、[A21 列表帧](../../../../protocol/game/server-to-client/NOTI-0x000D.md) 约 180..184 行、[用户实测](../../../宠物装备入包分区错误-20260922.md)。 |
| 勋章页签 | A21 参照入场有空的 `listType=38` 勋章列表，这是 `WireCaptured`；上游列表 38 内勋章 0..48、守护宝石 49..97，穿戴勋章为装备 list 3 / slot 31。 | 列表存在可直接采用；页签范围与穿戴槽仅 `UpstreamReference`，空列表未验证边界。此次只预留归属，不声称勋章玩法可用。见 [A21 列表帧](../../../../protocol/game/server-to-client/NOTI-0x000D.md) 180..184 行、[上游边界](../../../../../../../ServerS4A21/Server/DfoServer/Game/Inventory/Services/ItemSlotBoundService.cs) 40..45 行、[上游枚举](../../../../../../../ServerS4A21/Server/DfoServer/Game/ItemUpgrade/EquipmentType.cs) 40 行。 |

## 重构前基线映射与证据落差

基线 `9c630591` 的 `Server/src/Dfo.Server.Abstractions/Inventory/InventorySlotMappingRuleService.cs` 把装备 0..10 当装扮、11..23 当普通装备、24 当宠物、25..27 当宠物装备、29 当装备扩展；把宠物背包分成 0..139 / 140..188 / 189..239，把勋章分成 0..48 / 49..97。这些是重构前的 `CurrentImplementation` 记录，并非客户端证明；迁移后的实现见本任务结果，不能用此历史段落描述最新代码。`PvfEquipmentSlots.cs` 同时把 `[earring]`、`[support weapon]`、`[creature]` 都映为 24，故按模板分类会让两个不同业务物进入同一穿戴槽。上游枚举还有 `NameTag=29`、`Charm=30`、`GuildMedal=31`，所以当前 29 的“普通装备扩展”命名也未获 A21 实证。

`protocol/game/client-to-server/0x0013.md` 的机器区仅闭合 A21 构包点/handler；其后“快捷栏”样本明确来自 A12 客户端，不能用来证明 A21 宠物分区。A21 节的实帧仅有普通护肩 9→15，没有宠物或勋章移动。`protocol/game/server-to-client/NOTI-0x0002.md` 的宠物三元组和条目形状说明宠物投影独立，但参照角色没有穿戴宠物；`Docs/ProtocolScaffoldDissonances.md` 第九节明确记录 24/25 均无线上穿戴证据。

## 70 参考的独立边界

只读解析 `Research/1995chen-dnf/other/等级补丁/70/df_game_r` 的 ELF `.symtab` 与 x86 Capstone：`user_creature::CCreatureMgr::IsCreatureEquipmentScope(int)` 位于 VA `0x0833926c`，`0x08339275 cmp …,0x8b` 后还单独接受 `0x16`；`IsArtifactEquipSlot(int)` 位于 VA `0x083393fc`，依次比较 `0x17/0x18/0x19`。这只证明 **70 参考内部** 的判定常量（23..25 等），与上游 A21 的 26..28 不同。`CInventory::insertItemIntoCreatureEquipment` VA `0x0850a762` 调 `GetEmptyCreatureSlot` VA `0x0850a436`，后者再调用 VA `0x08501886`，并传入物品字段与两个范围输出地址；本次未追该调用点的范围规则。因此 70 不证明 A21 宠物装备背包首格为 140，也不能拿来解决穿戴 24/25。

## 有界 A21 客户端静态回溯

源为 `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，ImageBase `0x00400000`；以下均写 **VA**。先从 `DNF-JP-86-ClientHack/ClientHack/tools/literals-a21.csv` 找到 `CNRDItemManager::requestMoveItemSpace` 所在函数 `0x017012D0`、`CNRDInventoryWindow.cpp` 附近 `0x015BA9B0`，而不是扫全镜像 `25` 立即数。对 `0x017012D0` 做精确 direct-call xref 后，看到背包 UI 路径 `0x015B09E7` 与 `0x015B6B2A` 调用该通用移动入口；入口又在 `0x01701AA0` 调 `0x016FF840`，并在失败支路使用 `requestMoveItemSpace` 字符串。`protocol/game/client-to-server/0x0013.md` 所列 RVA `0x00B7DCE0` 对应 VA `0x00F7DCE0`：这里写入 type `0x0013` 后，从请求对象 `[ebx+0x118]` 与 `[ebx+0x11c]` 读取目标 list 与 slot，再交给 `0x0274D2E0` / `0x0274D310` 序列化。这闭合了“UI 所给地址 → 通用请求 → wire 字段”的一段路径，但没有得到宠物 UI 把 **25** 或 **140** 填入请求对象的那条路径。

还检查了文字锚 `UI/ChangeCreature/ChangeCreature.xui`（VA `0x0127ADF0`）与 `ENUM_ITEMSPACE_CREATURE`（VA `0x01649AA0`）：前者命中构造/初始化代码，后者命中列表类型名称映射，均不能推出穿戴槽或 Pet 分区首格。上述窄路径中未见可归因于宠物槽位的 `25` 或 `140` 常量；这**不是**对整个 A21 客户端的排除证明。静态补证若继续，应从真正的宠物背包 UI 拖动目标计算或空格选择函数追到 `requestMoveItemSpace` 的实参，再与请求对象字段交叉核对；当前没有可靠的该 UI 函数地址，故本轮停止，不凭任意立即数补结论。

第二个有界入口从**入站读取**而非拖动发送查起。同一 A21 dump 中，协议页给出的 `NOTI 0x0002` handler RVA `0x00FE91E0` 是 VA `0x013E91E0`；它按 subtype 分支，`0x013E9293` 对某一重复条目调用 `0x013E7340`，但这一调用本身没有指向固定的 Equipment/25。`NOTI 0x0069` handler RVA `0x00D63430` 是 VA `0x01163430`：`0x0116345F` 读条目数，循环里通过 `0x027C5310` 等原语读生物字段，`0x011634E1..0x01163567` 构造生物对象并提交；这条路径读取生物列表内容，未见固定装备槽的查找。再用精确字符串引用追 `literals-a21.csv` 的 `equip_creature`：唯一引用 VA `0x028B00D9` 把文字与数值 `0x0C3A` 注册进映射表，是标签注册而非已出战宠物取件。`DISCONN_SUB_INVENTORY_EQUIP_CREATURE_INVALID_TYPE` 的引用 VA `0x02FE4ACF` 同样是错误名注册。上述锚点都未形成“Equipment 列表 → 固定槽 25 → 已出战宠物”调用链，因此 **A21 穿戴 24/25 仍未知**；没有扩展到全镜像立即数搜索。

## 实施边界与最小补证

- 可按 **上游候选** 将内部 `CreatureEquipment[0]` 投影为 Pet/140，并将装备穿戴候选设为 Creature/25、神器/26..28；代码和测试需明确 `A21 未验证`，不要把候选当成已验收值。普通装备与宠物穿戴的业务归属应分开，避免 24 冲突。旧 24 持久化记录需靠物品 kind/模板与实例身份消歧；不能仅凭槽号批量改写。
- A21 客户端读写点尚未闭合：目前没有宠物装备拖入 Pet/140、宠物穿戴到 Equipment/25 或勋章 38 边界的客户端构包/解析静态证据。最小实机补证是针对真实宠物、神器、勋章物品分别记录一次 C2S 移动目标 `list,index` 与 S2C 物品刷新；宠物装备首空格还需用空 Pet 分区和可重复的拾取/商城样本验证。已有抓帧插件即可采样，**不必先 attach 客户端**。若 UI 无法产生操作或抓帧歧义，再在 A21 客户端构包点或列表解析处静态定位并交叉核对。没有这些证据时保持“未知”，不由 70、上游或当前实现代填。
