# GM工具新背包数据结构说明

> **这份文档描述的是参照服务端 `ServerS4A21`，不是本仓。**
> 它讲的背包模型（`character_new_items` 一张表、`item_uid` 只是行主键、
> `ItemCore.Value` 对时装是 `AvatarUid`、真源是 `InventoryService`）
> **本仓一条都不成立**：`character_new_items`、`InventoryService`、`AvatarUid`
> 这三个名字在 `Server/src` 里各自 0 命中。
>
> 本仓今天的形状是 **`items` 世界表 + `item_slots` 位置表**：物品有跨位置不变的
> `item_uid`，槽行经 `core_uid` 指名它，时装细节是 `items` 上的六列。
> 权威描述在 `AGENTS.md`「物品：有身份的东西，和它在哪，是两张表」一节。
>
> **它保留下来是因为参照实现的业务语义有价值**——槽位区段的划分、
> `category → list_type` 的映射、随身金币的表达，这些是本仓做同一件事时的参照。
> **但不要拿它当本仓事实去取证。** 已经发生过：至少六个任务把它当权威引用过，
> 其中两处用它确定时装 `Value` 的语义与穿戴槽号，而这两条本仓都另有出处。


本文面向 GM 工具重写。当前背包已经从旧 `character_items + extra_json`、`character_equipped_entries.raw_entry`、旧 DTO/InvenItem 过渡到在线 `InventoryService + ItemCore + Detail` 模型。GM 工具不要再直接构造旧 DTO，也不要再依赖 `extra_json` 字节段名称。

## 总体规则

- 在线角色的真源是 `InventoryService`。GM 直接改数据库只适合离线角色；在线角色需要下线重进或由服务端提供 GM 入口同步到在线模型。
- 主背包、穿戴栏、时装栏、宠物栏、个人仓库、账号仓库统一用 82 字节 `ItemCore` 表示。
- 时装和宠物有额外 detail。`ItemCore.Value` 对时装是 `AvatarUid`，对宠物是 `CreatureUid`。
- `character_new_items.item_uid` 只是物品行主键，不再等于时装 UID。
- 名称装饰卡、收集箱、成就完成状态不是普通背包物品，不要强行塞进 `character_new_items`。
- 晶体碎片持久化在账号表 `accounts.cube_*` 字段，运行时映射成主背包 354-359 虚拟槽，不写 `character_new_items`。
- 所有 `item_core` BLOB 都是小端序，长度必须严格等于 82。
- 空槽通常没有数据库行。代码内存里空槽用 `ItemCore.Init()` 表示。
- 金币槽 `list_type=0, slot_index=0` 的 `itemId=0` 是合法虚拟物品，判断空槽不能只看 `ItemId == 0`。
- 当前代码已清理 `SqliteAssetService` / `IAssetService` / `IInventoryStore` / `CommonInventoryItem` 等旧物品门面和旧 DTO。GM 工具不要为了兼容重新引入这些结构。

## 新表结构

### character_new_items

角色级物品主表。背包、穿戴栏、时装栏、宠物栏、个人仓库都从这里加载。

```sql
CREATE TABLE IF NOT EXISTS character_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_scope TEXT NOT NULL CHECK (owner_scope IN ('character', 'account')),
    owner_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(owner_scope, owner_id, list_type, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_character_new_items_character_space
    ON character_new_items(character_id, list_type, slot_index);
```

字段语义：

| 字段 | 语义 |
|---|---|
| item_uid | 数据库行 ID，只标识这一行，不参与协议物品 UID 语义 |
| owner_scope | 当前角色级物品固定写 `character` |
| owner_id | 当前实现是 scope-local owner key：`owner_scope='character'` 时写 `character_id` |
| character_id | 角色 ID |
| list_type | 物品空间，见下文 `InventoryListType` |
| slot_index | 空间内槽位 |
| item_core | 82 字节 `ItemCore` |
| created_at / updated_at | 数据库维护时间 |

GM 写入建议：角色物品使用 `owner_scope='character'`、`owner_id=character_id`、`character_id=character_id`。账号仓库不要写这张表，使用 `account_cargo_new_items`。

注意：从字段命名看，`owner_id` 更适合作为账号 ID；但当前表的唯一键是 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`，如果在不改唯一键的情况下把角色物品 `owner_id` 改成账号 ID，同账号多个角色会在相同 `list_type + slot_index` 上冲突。若后续要统一数据库语义，建议把角色物品唯一键调整为 `UNIQUE(character_id, list_type, slot_index)`，并让 `owner_id` 明确表示账号 ID，或者直接移除 `owner_scope/owner_id` 这组冗余字段。

### account_cargo_new_items

账号仓库物品表。

```sql
CREATE TABLE IF NOT EXISTS account_cargo_new_items (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL,
    character_id INTEGER,
    list_type INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(account_id, slot_index),
    FOREIGN KEY (account_id) REFERENCES accounts(account_id) ON DELETE CASCADE
);
```

字段语义：

| 字段 | 语义 |
|---|---|
| account_id | 账号 ID |
| character_id | 最近操作角色或辅助关联，可为空 |
| list_type | 固定为 `12` (`AccountCargo`) |
| slot_index | 账号仓库槽位，0-63 |
| item_core | 82 字节 `ItemCore` |

### character_avatar_detail

时装 detail 表。只有 `ItemCore.ItemKind == 8` 的时装需要关联本表。

```sql
CREATE TABLE IF NOT EXISTS character_avatar_detail (
    item_uid INTEGER PRIMARY KEY,
    owner_id INTEGER NOT NULL DEFAULT 0,
    character_id INTEGER NOT NULL DEFAULT 0,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_date INTEGER NOT NULL DEFAULT 0,
    clear_avatar_id INTEGER NOT NULL DEFAULT 0,
    jewel_socket BLOB NOT NULL CHECK(length(jewel_socket) = 30),
    color1 INTEGER NOT NULL DEFAULT 0,
    color2 INTEGER NOT NULL DEFAULT 0,
    delete_date INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_character_avatar_detail_character
    ON character_avatar_detail(character_id);
```

字段语义：

| 字段 | 语义 |
|---|---|
| item_uid | AvatarUid，必须与对应时装 `ItemCore.Value` 相同 |
| owner_id | 账号 ID。新运行时创建时传入 `InventoryService.AccountId` |
| character_id | 角色 ID |
| item_id | 时装模板 ID，应与 `ItemCore.ItemId` 相同 |
| expire_date | 到期 Unix 时间戳，0 表示无限期 |
| clear_avatar_id | 透明/克隆外观 ID，0 表示使用 `item_id` |
| jewel_socket | 固定 30 字节，5 组时装孔位 |
| color1 / color2 | 染色值，按 u16 使用 |
| delete_date | 删除时间，当前正常数据为 0 |

UID 分配表：

```sql
CREATE TABLE IF NOT EXISTS character_avatar_uid_sequence (
    avatar_uid INTEGER PRIMARY KEY AUTOINCREMENT
);
```

新建时装时应先从该序列表申请 AvatarUid，再写入 `ItemCore.Value` 和 `character_avatar_detail.item_uid`。如果 GM 工具手动指定 UID，必须保证大于 0、唯一，并同步推进序列表，避免后续服务端分配重复。

### character_creatures

宠物 detail 表。`ItemCore.ItemKind == 5` 的宠物通过 `ItemCore.Value` 关联本表 `creature_key`。

```sql
CREATE TABLE IF NOT EXISTS character_creatures (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL,
    creature_key INTEGER NOT NULL,
    field04 INTEGER NOT NULL DEFAULT 0,
    mode_flag INTEGER NOT NULL DEFAULT 0,
    progress_value INTEGER NOT NULL DEFAULT 0,
    mode1_field0a INTEGER NOT NULL DEFAULT 0,
    mode1_field0b INTEGER NOT NULL DEFAULT 0,
    field_after_value INTEGER NOT NULL DEFAULT 0,
    creature_text BLOB,
    tail_flag INTEGER NOT NULL DEFAULT 0,
    extra_json TEXT NOT NULL DEFAULT '{}',
    PRIMARY KEY (character_id, sort_order),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_character_creatures_key
    ON character_creatures(character_id, creature_key);
```

字段语义：

| 字段 | 语义 |
|---|---|
| character_id | 角色 ID |
| sort_order | detail 表内排序主键，新增时取该角色当前最大值 + 1 |
| creature_key | CreatureUid，必须与对应宠物 `ItemCore.Value` 相同 |
| field04 | `Stomach`，饱食度；大于 0 时在线状态视为存活 |
| mode_flag | 宠物模式标记 |
| progress_value | 宠物经验 |
| mode1_field0a / mode1_field0b | 宠物模式 1 附加字段 |
| field_after_value | 宠物等级 |
| creature_text | 宠物名原始字节 |
| tail_flag | 尾部标记 |
| extra_json | 遗留扩展字段，当前新 detail 不依赖它 |

UID 分配表：

```sql
CREATE TABLE IF NOT EXISTS character_creature_uid_sequence (
    creature_uid INTEGER PRIMARY KEY AUTOINCREMENT
);
```

宠物到期时间目前不直接存入 `character_creatures`。运行时 `CreatureDetail.ExpireDate` 由 PVF 到期规则解析，部分协议会使用剩余秒数。

### character_container_state

角色容器状态表，当前主要用于个人仓库容量。

```sql
CREATE TABLE IF NOT EXISTS character_container_state (
    character_id INTEGER NOT NULL,
    list_type INTEGER NOT NULL,
    list_param16 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, list_type),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

`list_type=2` 表示个人仓库，`list_param16` 是容量参数。个人仓库默认容量为 8，最大槽位 0-151。

### account_cargo_state

账号仓库状态表。

```sql
CREATE TABLE IF NOT EXISTS account_cargo_state (
    account_id INTEGER PRIMARY KEY,
    selection_key INTEGER NOT NULL DEFAULT 0,
    value32 INTEGER NOT NULL DEFAULT 0,
    item_count INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
```

字段语义：

| 字段 | 语义 |
|---|---|
| selection_key | 账号仓库容量选择值，范围 0-64 |
| value32 | 账号仓库金币 |
| item_count | 协议/状态用数量字段 |

### accounts 晶体字段

晶体碎片是账号级货币，持久化在 `accounts` 表字段中，运行时由 `InventoryService` 加载为主背包 354-359 虚拟槽。

| itemId | slot | accounts 字段 |
|---:|---:|---|
| 3033 | 354 | cube_black |
| 3034 | 355 | cube_white |
| 3035 | 356 | cube_red |
| 3036 | 357 | cube_blue |
| 3037 | 358 | cube_clear |
| 3262 | 359 | cube_gold |

GM 修改晶体数量时应更新账号表对应字段；不要在 `character_new_items` 中新增 354-359 的行。

### character_name_tag_state

名称装饰卡状态。当前不作为普通 `ItemCore` 存储。

```sql
CREATE TABLE IF NOT EXISTS character_name_tag_state (
    character_id INTEGER PRIMARY KEY,
    item_id INTEGER NOT NULL DEFAULT 0,
    expire_time INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

同一个角色只能有一条名称装饰卡记录。新增时覆盖旧记录。`expire_time` 是到期 Unix 时间戳，不是剩余秒数；协议当前也发送到期时间戳。

### character_item_locks

物品锁表。它是快速构造锁列表包的索引表，不是物品真源。

```sql
CREATE TABLE IF NOT EXISTS character_item_locks (
    character_id INTEGER NOT NULL,
    equipment_lock_id INTEGER NOT NULL,
    inventory_list_type INTEGER NOT NULL,
    slot INTEGER NOT NULL,
    state INTEGER NOT NULL,
    remaining_seconds INTEGER,
    PRIMARY KEY (character_id, equipment_lock_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

字段语义：

| 字段 | 语义 |
|---|---|
| equipment_lock_id | 必须与对应物品 `ItemCore.EquipmentLockId` 相同 |
| inventory_list_type | 物品当前所在 `list_type` |
| slot | 物品当前所在槽位 |
| state | 当前只需要 0 解锁、1 锁定 |
| remaining_seconds | 旧倒计时字段，当前可为空 |

GM 移动物品时，如果 `ItemCore.EquipmentLockId != 0`，必须同步更新本表的 `inventory_list_type` 和 `slot`。删除物品时应删除对应锁记录。

### character_new_titlebook

新称号簿表。每个称号簿格子单独存一条 82 字节 `ItemCore`。

```sql
CREATE TABLE IF NOT EXISTS character_new_titlebook (
    character_id INTEGER NOT NULL,
    category INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_core BLOB NOT NULL CHECK(length(item_core) = 82),
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (character_id, category, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

分类映射：

| category | list_type | 名称 | 默认容量 |
|---:|---:|---|---:|
| 0 | 19 | general | 80 |
| 1 | 20 | specific | 170 |
| 2 | 21 | pvp | 50 |
| 3 | 22 | despair | 100 |
| 4 | 23 | event | 100 |

称号簿内的称号按装备 `ItemCore` 存储。佩戴称号簿称号时，服务端会从称号簿删除该 `ItemCore`，脱下时再全字节拷贝回称号簿。

### character_achievement_complete

成就完成/进度表。它与称号簿奖励相关，但不是物品表。

```sql
CREATE TABLE IF NOT EXISTS character_achievement_complete (
    character_id INTEGER NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    achievement_id INTEGER NOT NULL,
    p1 INTEGER NOT NULL DEFAULT 0,
    p2 INTEGER NOT NULL DEFAULT 0,
    p3 INTEGER NOT NULL DEFAULT 0,
    p4 INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (character_id, achievement_id),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

称号簿成就发放称号时，应创建装备类默认 `ItemCore`，再写入 `character_new_titlebook` 对应分类和槽位。

### character_collectbox_slots

收集箱只存 itemId，不存完整 `ItemCore`。

```sql
CREATE TABLE IF NOT EXISTS character_collectbox_slots (
    character_id INTEGER NOT NULL,
    box_index INTEGER NOT NULL,
    slot_index INTEGER NOT NULL,
    item_id INTEGER NOT NULL,
    PRIMARY KEY (character_id, box_index, slot_index),
    FOREIGN KEY (character_id) REFERENCES characters(character_id) ON DELETE CASCADE
);
```

放入收集箱时，背包扣除 1 个对应物品；取出时，根据 `item_id` 创建一个默认 `ItemCore` 并插回背包。

## InventoryListType

| 值 | 名称 | 说明 |
|---:|---|---|
| 0 | Main | 主背包，含金币/复活币/胜点虚拟槽 |
| 1 | Avatar | 时装背包 |
| 2 | PersonalCargo | 个人仓库 |
| 3 | Equipment | 穿戴栏 |
| 7 | Pet | 宠物栏 |
| 12 | AccountCargo | 账号仓库 |
| 19 | TitleBookGeneral | 称号簿 general |
| 20 | TitleBookSpecific | 称号簿 specific |
| 21 | TitleBookPvp | 称号簿 pvp |
| 22 | TitleBookDespair | 称号簿 despair |
| 23 | TitleBookEvent | 称号簿 event |
| 29 | QuickSlot | 快捷栏协议/系统枚举，不是普通新物品表空间 |
| 33 | KnightShieldEquipped | 骑士盾穿戴枚举 |
| 34 | KnightShieldCatalog | 骑士盾图鉴枚举 |

## 槽位边界

### 主背包 list_type=0

| 槽位 | 语义 |
|---|---|
| 0 | 金币虚拟槽，`ItemId=0`，`Count=金币数` |
| 1 | 复活币虚拟槽，`ItemId=1`，`Count=复活币数` |
| 2 | 胜点虚拟槽，`ItemId=2`，`Count=胜点数` |
| 3-8 | 快捷栏物品区，迁移和创建时应按 PVF 判断实际 `itemKind` |
| 9-64 | 装备栏完整范围 |
| 65-120 | 消耗品栏完整范围 |
| 121-176 | 材料栏完整范围 |
| 177-232 | 任务栏完整范围 |
| 233-288 | 副职业材料栏完整范围 |
| 289-351 | 徽章栏 |
| 352-353 | 预留，客户端无对应位置 |
| 354-359 | 晶体类虚拟槽，运行时来自 `accounts.cube_*` 字段 |

背包扩展阶段只影响 9-288 的开放末尾。实际开放末尾计算为：

```text
openEnd = fullEnd - (24 - mainExpandStageKey)
mainExpandStageKey in {0, 8, 16, 24}
```

例如装备栏完整范围 9-64，在未扩展时开放到 40，满扩展时开放到 64。

### 其他空间

| list_type | 槽位 | 语义 |
|---:|---|---|
| 1 | 0-209 | 时装背包 |
| 2 | 0-151 | 个人仓库，开放槽位受 `character_container_state.list_param16` 控制 |
| 3 | 0-10 | 穿戴时装 |
| 3 | 11-23 | 穿戴普通装备 |
| 3 | 24 | 穿戴宠物 |
| 3 | 25-27 | 穿戴宠物装备/神器 |
| 3 | 28 | 当前名称装饰卡不走新物品表 |
| 3 | 29 | 穿戴普通装备扩展位 |
| 7 | 0-139 | 宠物栏宠物 |
| 7 | 140-188 | 宠物装备 |
| 7 | 189-239 | 宠物消耗品 |
| 12 | 0-63 | 账号仓库，开放槽位受 `account_cargo_state.selection_key` 控制 |

## ItemKind

| 值 | 名称 | 语义 |
|---:|---|---|
| 0 | KindUnknown | 未知/空 |
| 1 | KindEquipment | 普通装备，称号也按装备处理 |
| 2 | KindConsumable | 消耗品 |
| 3 | KindMaterial | 材料 |
| 4 | KindQuest | 任务物品 |
| 5 | KindCreature | 宠物 |
| 6 | KindCreatureEquipment | 宠物装备 |
| 7 | KindCreatureConsumable | 宠物消耗品 |
| 8 | KindAvatar | 时装 |
| 9 | KindAvatarEmblem | 时装徽章 |
| 10 | KindExpertJobMaterial | 副职业材料 |
| 11 | KindSpecialMaterial | 特殊材料；当前金币/复活币/胜点虚拟槽也使用它 |

快捷栏和仓库无法只靠槽位完全确认类型，应该按 PVF 静态数据解析 `itemKind`。`Charm` 不再单独作为 `itemKind`，本质仍是装备，必要时按装备 PVF 的 `EquipmentType=Charm` 区分。

## ItemCore 82 字节结构

`ItemCore` 是服务端物品核心属性，不是 0x0D/0x0E 协议 entry。协议 entry 需要由协议 writer 根据 `list_type`、`slot`、`ItemCore`、detail 重新组装。

| 偏移 | 大小 | 字段 | 默认值 | 语义 |
|---:|---:|---|---|---|
| 0 | 1 | itemKind | 0 | 物品类型 |
| 1 | 4 | itemId | 0 | 物品模板 ID |
| 5 | 4 | value | 0 | 装备品级/实例值；堆叠数量；时装 AvatarUid；宠物 CreatureUid |
| 9 | 1 | attr | 0 | 装备低 5 位强化等级、高 3 位密封次数；堆叠物高 3 位可交易次数 |
| 10 | 2 | durability | 0 | 装备耐久；时装 ability_no |
| 12 | 1 | sealFlag | 0 | 装备封装状态；宠物蛋/孵化状态 |
| 13 | 4 | enchantCardId | 0 | 附魔卡 ID；封印宠物物品 ID 复用字段 |
| 17 | 1 | enchantUpgradeCount | 0 | 附魔升级次数 |
| 18 | 1 | amplifyType | 0 | 异次元属性类型，0x80 表示未净化 |
| 19 | 2 | amplifyValue | 0 | 异次元附加数值 |
| 21 | 4 | marker16 | -1 | 通用标记；封印宠物 key 复用字段 |
| 25 | 4 | chronicleOption0Id | 0 | 异界气息 0 ID |
| 29 | 1 | chronicleOption0CharacJob | 0 | 异界气息 0 职业 |
| 30 | 1 | chronicleOption0FirstGrowType | 0 | 异界气息 0 转职 |
| 31 | 1 | chronicleOption0EquipmentType | 0 | 异界气息 0 装备类型 |
| 32 | 1 | chronicleOption0OptionNo | 0 | 异界气息 0 选项编号 |
| 33 | 4 | chronicleOption1Id | 0 | 异界气息 1 ID |
| 37 | 1 | chronicleOption1CharacJob | 0 | 异界气息 1 职业 |
| 38 | 1 | chronicleOption1FirstGrowType | 0 | 异界气息 1 转职 |
| 39 | 1 | chronicleOption1EquipmentType | 0 | 异界气息 1 装备类型 |
| 40 | 1 | chronicleOption1OptionNo | 0 | 异界气息 1 选项编号 |
| 41 | 4 | expireTime | 0 | 绝对到期 Unix 时间戳；封印宠物到期时间复用字段 |
| 45 | 1 | emblemSocketCount | 0 | 装备已开孔数量 |
| 46 | 4 | emblemId1 | 0 | 装备徽章 1 |
| 50 | 4 | emblemId2 | 0 | 装备徽章 2 |
| 54 | 2 | rune | 0 | 武器符文/特效 |
| 56 | 1 | randomOption0Type | 0 | 魔法封印 0 类型 |
| 57 | 1 | randomOption0Value1 | 0 | 魔法封印 0 数值 1 |
| 58 | 1 | randomOption0Value2 | 0 | 魔法封印 0 数值 2 |
| 59 | 1 | randomOption1Type | 0 | 魔法封印 1 类型 |
| 60 | 1 | randomOption1Value1 | 0 | 魔法封印 1 数值 1 |
| 61 | 1 | randomOption1Value2 | 0 | 魔法封印 1 数值 2 |
| 62 | 1 | randomOption2Type | 0 | 魔法封印 2 类型 |
| 63 | 1 | randomOption2Value1 | 0 | 魔法封印 2 数值 1 |
| 64 | 1 | randomOption2Value2 | 0 | 魔法封印 2 数值 2 |
| 65 | 1 | randomOptionState | 0 | 魔法封印变更状态 |
| 66 | 1 | randomOptionChangedIndex | 0xFF | 变更槽位，0xFF 表示无目标 |
| 67 | 1 | randomOptionChangeState | 0 | 变更状态，语义未完全确认 |
| 68 | 1 | randomOptionChangeType | 0 | 变更后类型 |
| 69 | 1 | randomOptionChangeValue1 | 0 | 变更后数值 1 |
| 70 | 1 | randomOptionChangeValue2 | 0 | 变更后数值 2 |
| 71 | 1 | genuineUpgrade | 0 | 锻造等级 |
| 72 | 1 | emancipateEquipmentLevel | 0 | 额外增加使用等级限制 |
| 73 | 1 | tradeRestriction | 0 | 角色绑定/交易限制；时装合成/分解限制 |
| 74 | 2 | tailUnknown0 | 0 | 未确认尾部字段 |
| 76 | 1 | tailUnknown1 | 0 | 未确认尾部字段 |
| 77 | 1 | tailUnknown2 | 0 | 未确认尾部字段 |
| 78 | 1 | tailUnknown3 | 0 | 未确认尾部字段 |
| 79 | 1 | remainUseCount | 0 | 剩余使用次数 |
| 80 | 1 | sortLockFlag | 0 | 排序锁 |
| 81 | 1 | equipmentLockId | 0 | 物品锁 ID |

常用别名：

| 属性 | 实际字段 | 说明 |
|---|---|---|
| InstanceValue | value | 装备品级/实例值 |
| Count | value | 堆叠数量 |
| Uid | value | 通用 UID |
| AvatarUid | value | 时装 detail UID |
| CreatureUid | value | 宠物 detail UID |
| AbilityNo | durability | 时装属性编号 |
| Upgrade | attr 低 5 位 | 强化等级，范围 0-31 |
| ReSealCount | attr 高 3 位 | 已消耗密封次数 |
| StackTradeCount | attr 高 3 位 | 堆叠物可交易次数 |
| ChronicleOptionCount | 派生 | `chronicleOption0/1Id != 0` 的数量 |
| RandomOptionCount | 派生 | `randomOption0/1/2Type != 0` 的数量 |

## JewelSocket 30 字节结构

时装 detail 的 `jewel_socket` 固定 30 字节，5 组，每组 6 字节。

| 组内偏移 | 大小 | 字段 | 语义 |
|---:|---:|---|---|
| 0 | 2 | socketType | 孔位类型，u16 小端 |
| 2 | 4 | emblemId | 徽章 ID，i32 小端 |

组偏移计算：

```text
entryOffset = index * 6
index = 0..4
```

开孔数量由 `socketType != 0` 的组数派生。即使 30 字节全 0，0x0D/0x0E 时装 detail 协议仍会写 `len=30 + 30字节jewel_socket`。

常见孔位值：

| 孔位 | 值 |
|---|---:|
| A socket 红色 | 1 |
| B socket 黄色 | 2 |
| C socket 绿色 | 4 |
| D socket 蓝色 | 8 |
| S socket 白金 | 16 |
| M socket 彩色 | 65519 |

## Detail 与 ItemCore 的关联

### 时装

```text
character_new_items.item_core.ItemKind = 8
character_new_items.item_core.Value = AvatarUid
character_avatar_detail.item_uid = AvatarUid
character_avatar_detail.item_id = item_core.ItemId
```

协议中时装 `value` 字段不是直接写 `ItemCore.Value`，而是写 detail 的剩余有效期或相关协议值。因此 GM 工具只负责正确存 `AvatarUid` 和 detail 到期时间。

新增时装推荐流程：

1. 解析 PVF 得到 `itemKind=8`。
2. 创建 `ItemCore`，`ItemKind=8`，`ItemId=时装ID`，`Value=0`，`Durability/AbilityNo` 按业务选择。
3. 申请新的 AvatarUid。
4. 回填 `ItemCore.Value=AvatarUid`。
5. 写入 `character_new_items` 目标槽位。
6. 写入 `character_avatar_detail`，`item_uid=AvatarUid`，`jewel_socket` 必须 30 字节。
7. 如果 PVF 有默认开孔标签，应在 `jewel_socket` 中提前写入对应 socketType。

### 宠物

```text
character_new_items.item_core.ItemKind = 5
character_new_items.item_core.Value = CreatureUid
character_creatures.creature_key = CreatureUid
```

新增宠物推荐流程：

1. 解析 PVF 得到 `itemKind=5`。
2. 创建 `ItemCore`，`ItemKind=5`，`ItemId=宠物ID`，`Value=0`。
3. 申请新的 CreatureUid。
4. 回填 `ItemCore.Value=CreatureUid`。
5. 写入 `character_new_items` 目标槽位。
6. 写入 `character_creatures` detail。默认 `field04=100`，`field_after_value=1`，经验为 0。

## GM 常见操作建议

### 发放普通物品

1. 用 PVF 或服务端同等规则解析 `itemKind`。
2. 按 `ItemSlotBoundService` 规则选择 `list_type` 和空槽。
3. 构造 82 字节 `ItemCore`。
4. 写入 `character_new_items`。
5. 如果是带 detail 的时装/宠物，必须同时写 detail 表，并保证 UID 关联正确。

普通装备默认值建议：

| 字段 | 建议 |
|---|---|
| ItemKind | 1 |
| ItemId | 装备 ID |
| Value | 随机实例值/品级值 |
| Durability | PVF 耐久 |
| SealFlag | PVF 默认封装状态 |
| Marker16 | -1 |
| RandomOptionChangedIndex | 0xFF |

堆叠物默认值建议：

| 字段 | 建议 |
|---|---|
| ItemKind | 2/3/4/9/10/11 |
| ItemId | 物品 ID |
| Count | 数量 |
| ExpireTime | 期限道具写绝对 Unix 时间戳，否则 0 |
| StackTradeCount | 如果 PVF 有交易次数限制，写入 `Attr` 高 3 位 |
| Marker16 | -1 |
| RandomOptionChangedIndex | 0xFF |

### 插入已有物品

已有物品移动或从称号簿、收集箱等系统取出时，优先全字节拷贝 `ItemCore`。不要重新生成强化、增幅、附魔、徽章、锁 ID 等字段。

### 删除物品

删除 `character_new_items` 或 `account_cargo_new_items` 行后，需要检查：

- 如果 `ItemCore.ItemKind == 8`，且没有其他物品引用该 `AvatarUid`，删除 `character_avatar_detail`。
- 如果 `ItemCore.ItemKind == 5`，且没有其他物品引用该 `CreatureUid`，删除 `character_creatures`。
- 如果 `ItemCore.EquipmentLockId != 0`，删除 `character_item_locks` 对应记录。

### 移动物品

移动只改变容器和槽位，`ItemCore` 应全字节保留。

如果被移动物品有锁：

```text
ItemCore.EquipmentLockId != 0
```

必须同步更新：

```sql
UPDATE character_item_locks
SET inventory_list_type = @newListType,
    slot = @newSlot
WHERE character_id = @characterId
  AND equipment_lock_id = @lockId;
```

### 金币/复活币/胜点/晶体

金币、复活币、胜点走主背包 0-2 虚拟槽，并持久化为 `character_new_items` 的虚拟 `ItemCore`。晶体走主背包 354-359 虚拟槽，但持久化在 `accounts.cube_*` 字段。

| slot | itemId | 语义 |
|---:|---:|---|
| 0 | 0 | 金币 |
| 1 | 1 | 复活币 |
| 2 | 2 | 胜点 |

当前持久化创建的 core 为：

```text
ItemKind = KindSpecialMaterial (11)
ItemId = slotIndex
Count/Value = 数量
```

因此 `slot=0,itemId=0` 不是空物品。GM 工具做空槽扫描时必须排除 0/1/2 虚拟槽。

晶体映射如下：

| slot | itemId | accounts 字段 |
|---:|---:|---|
| 354 | 3033 | cube_black |
| 355 | 3034 | cube_white |
| 356 | 3035 | cube_red |
| 357 | 3036 | cube_blue |
| 358 | 3037 | cube_clear |
| 359 | 3262 | cube_gold |

### 称号簿

称号簿物品使用 `character_new_titlebook`，不是 `character_new_items`。

- 成就获得称号：创建装备类默认 `ItemCore`，写入称号簿目标 category/slot。
- 背包称号放入称号簿：从背包扣除或移除对应 `ItemCore`，写入称号簿。
- 佩戴称号簿称号：从称号簿删除该 `ItemCore`，全字节拷贝到穿戴栏称号槽。
- 脱下称号簿称号：从穿戴栏全字节拷贝回称号簿。

称号簿和穿戴栏/背包之间的操作应在同一事务内完成。

### 收集箱

收集箱只保存 `item_id`：

- 放入：背包扣 1 个物品，写 `character_collectbox_slots`。
- 取出：删除收集箱记录，根据 `item_id` 创建默认 `ItemCore`，插入背包。

收集箱内物品不保留强化、附魔、锁、徽章等实例属性。

### 名称装饰卡

名称装饰卡只保存一条角色状态：

```text
character_name_tag_state.character_id
character_name_tag_state.item_id
character_name_tag_state.expire_time
```

新增名称装饰卡时覆盖同角色旧记录。`expire_time` 是到期 Unix 时间戳，不是剩余秒数。

## 旧结构使用边界

以下结构只应作为迁移期或协议边界参考，不应作为 GM 新写入目标：

- `character_items.extra_json`
- `character_equipped_entries.raw_entry`
- `CommonInventoryItem`
- `AvatarInventoryItem`
- `PetInventoryItem`
- `InventoryItemView`
- `EquippedItemView`
- `InvenItem`
- `PrefixData0E / MiddleData1A / TailData2F / Reserved*`

GM 工具重写时应直接面向：

```text
character_new_items / account_cargo_new_items
item_core(82B)
character_avatar_detail
character_creatures
character_new_titlebook
character_item_locks
character_name_tag_state
character_collectbox_slots
inventory_audit_log_v2(只读审计)
```
