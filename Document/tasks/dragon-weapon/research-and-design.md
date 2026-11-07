# 龙跃武器（#281）：证据与实现方案

基线：`main` 35049db76（0.2.12），分支 `feature/dragon-weapon-281`，工作区 `DNF-JP-86-Server-group7`。
卷轴升星（CMD 0x010F + `[emancipate]` 模板转换）与 #282 镇魂开光共用，由 #282 实现通用切片；
本任务负责成长值（熟练度）本身，并在 0x010F 的龙跃券分支提供成长值校验与升星后的成长值处理。

客户端 dump：`DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144`，下文地址均为 VA。
PVF：`DNFClient-A21-protocol/Script.pvf`（SHA-256 `01f1be68…c5189`）。

## 1. 证据

### 1.1 PVF（Confirmed）

- 每把武器 7 星，**每星独立模板**，如 `equipment/character/swordman/weapon/ssword/101000042.equ`
  「龙跃之短剑[1星] - 剑神」101000042→…→101000048。共 178 条链（172 条有名字，6 条空名占位），
  覆盖全部职业武器类型，后缀为二觉名；`[minimum level]` 均 75，`[grade]` 73～79，7 星 `[rarity]` 3。
- 星级：`.equ` 的 `[emancipate]`→`[equipment grow] 1 <星>`（group 1）；同块 `[input]`（10000224 小铁柱的火炉使用券）
  与 `[output]`（下一星模板）。
- 获取：二觉后任务链「二次觉醒 - 龙跃武器 (1/3)(2/3)(3/3)」，NPC 小铁柱（124）。(2/3) 的
  `[reward selection int data]` 按转职选 1 星武器；(3/3) 是 seeking 任务，付 500 万金币重新领取。
- 卷轴：10012055 初级（ticket 2，升星后强化等附加效果消失）、10012056 高级（ticket 3，保留），
  小铁柱商店 55（`itemshop/jun.shp`）出售，连同火炉券 10000224。
- `etc/equipment_grow.etc`：

  ```
  [group] 1 [max level] 7
  [exp acquire] death [point] 5 [kind] 4 1 100 2 100 3 300 4 140
    [level correction] 3 3 10 5 90 10  [difficulty correction] 100 100 100 100 100
  [exp base] 星 maxExp 自动标志 + [penalty] fatigue 30 20
    1 1794 1 / 2 3174 1 / 3 4830 0 / 4 6141 0 / 5 8694 0 / 6 11868 0 / 7 0 0
  ```
- 武器说明：击杀普通、稀有、精英、领主怪物获得成长值；难度越高越多；**在亡者峡谷中无效**；
  每天不消耗一定蓄能值，第二天减少 20% 成长值。

### 1.2 A21 客户端（ClientStatic）

- **物品字段**（Confirmed，NOTI 0x000E handler 0x0117D720 的 0x0117E224/0x0117E244）：101 字节条目
  +0x4D u16 → 物品成长值（+0x88），+0x4F u8 → conditionCount（+0x90）。即 ItemCore[74] = 成长值，
  ItemCore[76] = conditionCount。000D 与穿戴 NOTI 路径未追到写入点（High）。
- **conditionCount = 当日维持疲劳**（Confirmed）：tooltip 0x02463E6D 用字符串 0x6A40
  「維持疲労度 %d/%d」，分母是当前星 `[penalty] fatigue` 的 30。
- **equipment_grow 解析**（High）：0x01668F3B 加载到 0x3A75B34；`[exp base]` 三列存表项 +0/+4/+8；
  `[level correction]` 固定 6 个整数，`[difficulty correction]` 固定 5 个。客户端只在 01E1 handler、
  卷轴拦截与 tooltip 读 exp base；**`[exp acquire]` 在客户端没有读取者**，增量由服务端计算。
- **NOTI 0x01E1**（handler 0x01188130，布局 Confirmed）：`u8 invType | u16 slot | u16 expGained |
  u16 curExp | u8 conditionCount`；curExp 夹到本星 maxExp；expGained>0 播放 `grow_expup00.ani`。
- **NOTI 0x01E2**（handler 0x0116B800）：`u8 changeType | u8 count | count×{u8 invType, u16 slot,
  u32 newItemId, u16 exp, u8 conditionCount}`；1 = 升星（按 newItemId 生成新物品），2 = reset（只改 exp/cond）。
- **卷轴本地拦截** 0x02419450（Confirmed）：满星、exp base 第三列为 1（1、2 星）、成长值 < 本星 maxExp、
  耐久不足、带异界气息时客户端直接拒绝。所以 1→2、2→3 只能满值自动升星。
- 0x010F 构包 0x02416BA0、回包 0x0111A950 的细节见 #282 的任务资料（共享）。

### 1.3 70 服务端（70Reference）

无龙跃，也没有装备熟练度机制。可参照的是模板转换：`GrowthEquipEvent::SendInfinityGrowthWeapon`
0x080F74B2 与 `CUser::copyItemOption` 0x08671EB2，保留强化、品级种子、附魔卡、增幅、锻造；
不保留 GUID、耐久、封印次数、随机选项、交易限制。击杀时修改物品实例的先例：
`CUser::CheckQuestMonster` 0x0866CB04（`update_item` 后发 0x0E）。

### 1.4 上游 ServerS4A21

无成长值实现。0x010F `InventoryHandler.ChronicleGrowth.cs` 把龙跃券当普通 emancipate 转换
（`InventoryEmancipateService.cs:143` ticket 1/3/6/7/19 保留强化等）；ItemCore 74..78 当未知字段透传。

### 1.5 本仓现状（CurrentImplementation）

- ItemCore 82 字节已原样持久化 [74] TailUnknown0、[76] TailUnknown1，并写入背包条目与穿戴 NOTI，
  **不需要改表**。
- 存储层禁止装备原位换模板（`SqliteItemDomainStore.cs:180`、`RunItemDomainStore.cs:123`，仅宠物例外）；
  自动升星和卷轴升星都需要装备模板转换能力，与 #282 共用，统一由一处放开。
- 击杀链路 `MonsterDeathDomainService` 单事务覆盖全队；`MonsterRank` 0..3 = 普通/稀有(Champion)/
  精英(SuperChampion)/领主；宠物房间经验是“穿戴物随战斗成长”的最近先例。
- 疲劳行 `CharacterFatigueRow(Used, ConsumedToday, UpdatedAt)` 已有当日消耗量；每日周期统一走
  `IDailyResetRuleService`；`character_daily_counters` 可存时间戳计数，无需迁移。

## 2. 方案

### 2.1 内容源
`Infrastructure/Content/PvfEquipmentGrowthSource`：读 `etc/equipment_grow.etc`（group → maxLevel、
acquire[death]{point, kind→rate, levelCorrection[6], difficulty[5]}、star → {maxExp, autoOnly, fatiguePenalty(a,b)}）；
物品侧 `[equipment grow] group star` 与下一星模板来自 #282 的 emancipate 内容源（共享，不重复解析）。

### 2.2 规则与领域服务
- `EquipmentGrowthRuleService`（纯规则）：每次击杀增量、是否满值、自动升星判定、卷轴可用判定、每日惩罚。
- `EquipmentGrowthDomainService`：
  - 击杀：在怪物死亡事务内，对每个参与者**穿戴武器槽**上的成长武器加成长值（排除亡者峡谷 run kind），
    `Update` 写 ItemCore[74]，投影 NOTI 0x01E1。满值且本星为自动星时，同事务内换成下一星模板、
    成长值归 0、投影 NOTI 0x01E2 changeType=1。
  - 疲劳：角色消耗疲劳时，把当日维持疲劳写进穿戴中成长武器的 ItemCore[76]（封顶为本星阈值 30）。
  - 换日：按游戏日（`IDailyResetRuleService`）结算，未达阈值的成长武器成长值减少 20%，
    conditionCount 归 0，投影 NOTI 0x01E2 changeType=2；结算日记在 `character_daily_counters`。
  - 0x010F 龙跃券（ticket 2/3）分支：校验成长值 ≥ 本星 maxExp、非自动星、非满星；升星后成长值归 0。
- 字段保留：自动升星沿用高级卷轴语义（强化、增幅、锻造、附魔、品质、UID、绑定都保留，只换模板与成长值）；
  卷轴的初级/高级差异由 #282 的转换规则处理。

### 2.3 协议
新增 NOTI 0x01E1、0x01E2 编码切片（`Dfo.Server.Protocol/Game/EquipmentGrowth/`），补协议页与 catalog；
ItemCore[74]/[76] 改名为 `GrowthExp` / `GrowthConditionCount`，入场与背包条目照常下发。

### 2.4 事务与不重复计数
成长值在怪物死亡事务内与击杀奖励一起提交；死亡事务已按 actor 去重，重放的死亡帧不会重复加。
自动升星与成长值写入同事务；失败整体回滚。

### 2.5 验证
自动测试：增量公式、满值封顶、自动升星（1→2、2→3）、3 星满值不自动升、7 星不增长、亡者峡谷不增长、
未穿戴不增长、重复死亡帧不重复、每日惩罚边界（跨 06:00、已达阈值不罚）、ItemCore 读写往返。
用户测试顺序见实现后的测试清单。

## 3. 证据缺口（需用户裁定，见第 4 节）

1. `[kind]` 1..4 与怪物分类对应：PVF 和客户端都没写明。
2. `[level correction] 3 3 10 5 90 10` 六元组含义：客户端无读取者，无参照。
3. 每日惩罚的范围：只罚穿戴中的，还是角色持有的全部成长武器；离线多天是否逐日累计。

## 4. 裁定

（待用户回复后填写）
