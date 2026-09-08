# 角色状态按领域重切

2026-09-05，基准 `main` @ `51cd99c2`。**只读分析，未改动任何代码。**

配套阅读：[会话大聚合剩下三块的清点](CharacterEntryFacesInventory.md)（事实来源）与
[Table Ownership Ruling](Refactor/TableOwnershipRuling.md)（持久层的裁定，已落地）。

## 前提：债不在持久层，在它上面那层

`TableOwnershipRuling.md` 已经把整个角色数据库**按概念而不是按协议分段**重切过一遍，
裁定 **53 张表变 43 张**。**今天的 schema 正好是 43 张表**，
`character_subtype0_fields`、`character_subtype1_fields`、`character_init_flags`、
`character_gold_limits`、`character_crystal_contract`、`character_dimension_flags` 都已不存在。
**那份裁定已经执行完了。**

所以晶体契约在库里是 `character_condition.crystal_cube_type/grade`；成长武器是
`character_growth_weapon_stages`；每日挑战是三张表；霸王契约是
`account_premiums(premium_type, end_time)`。**它们在库里早就分开了。**

把它们重新糊回一起的是 `Data` 层的三个 record ——
`CharacterPublicEntryState` / `CharacterProgressionEntryState` / `CharacterFeaturesEntryState`
**还是包的形状，而它们下面的表已经不是了**。这也解释了 `CharacterEntryAssembly` 为什么那么大：
它的职责就是把已经按概念分好的 33 张表，重新拼回包的形状。

**这份文档要回答的是那份裁定有意不答的问题**：它自称「a ruling, not a plan」，
只裁表与列的归属。**领域服务归谁、按什么顺序建，它没裁**，而且 16 条 Deferred 里有 8 条的
责任人直接写着「Whoever builds daily challenges / quests / mail / achievements」。

## 一张表装了 15 个领域

`character_condition` 48 列，逐列归类，**0 列无法归类**：

| 领域 | 列数 | 列 |
|---|---:|---|
| 表现层 | 7 | `costume_flag` `aura_flag` `title_display_flag` `pet_display_flag` `emotion_index` `action_byte` `mood_value` |
| **宠物** | 5 | `creature_field1..4` `creature_buffer` |
| 次元 | 4 | `dimension_flag1..4` |
| **疲劳／体力** | 4 | `stamina` `fatigue_penalty` `fatigue_update` `fatigue_display_update` |
| 冠军突破 | 3 | `champion_break_key_id` `champion_break_mode` `champion_break_value` |
| 频道 | 3 | `channel_type` `channel_display_mode` `server_group_id` |
| 硬核模式 | 3 | `is_hardcore_mode` `is_hardcore_dead` `hardcore_death_count` |
| 链接槽 | 3 | `link_slot_enabled` `link_type_a` `link_type_b` |
| 晶体契约 | 2 | `crystal_cube_type` `crystal_cube_grade` |
| 变身 | 2 | `disguise_kind` `is_disguised` |
| 回归用户 | 2 | `is_return_user` `return_user_flag` |
| 网吧 | 3 | `pc_room_id` `pc_room_state` `is_premium_pc_room` |
| 惩戒 | 1 | `chat_ban_end_time` |
| 活动角色 | 1 | `is_event_character` |
| 个人商店 | 1 | `is_private_store` |
| **含义未定** | 3 | `black_count` `chaos_point` `trailing_byte` |
| （键） | 1 | `character_id` |

**这不是裁定没做好，是裁定自己认了的账。** 原文：

> 十六列（五、五、六）是残渣，说出来比硬安一个要伸展着才盖得住的概念名要好。

它把能按证据命名的都拿出去了（PvP、专家职业、代码自己叫 settings 的、派生数值、身份列），
剩下的**大部分**回答同一个问题——「这个角色身上发生过什么、正处在什么当中」。
剩下的**小部分**回答不了，它记成了 D-2 / D-4 / D-11 而不是硬安名字。

## 领域清单

「主人」一列：✅ 有领域服务且真的写；⚠️ 有服务但空转、只读或不覆盖这些列；❌ 无。

### A. 角色本体

| 领域 | 库里在哪 | 主人 | 缺口 |
|---|---|:--:|---|
| 身份 | `characters`(job, grow_type, name, slot_index, …) | ✅ | — |
| 成长（等级/经验/SP/TP） | `characters`(level, exp, bonus_sp, bonus_tp) | ✅ | 入场态里还有第二份经验副本 |
| 位置 | `characters`(town_id, area_id, pos_x/y, direction) | ✅ | — |
| **战斗属性** | `character_stats` 20 个 `stat_*` + `current_hp_mp_percent` | ❌ | **冻在创建时的 1 级基础值，升级/换装都不重算** |
| 外观 | `characters.appearance_blob` | ❌ | blob 仍无写入者；世界内形象与**选人界面**已都改成从实时装备现算，只剩入场那一批里的角色列表帧还读它（2026-09-08 更正，见表下） |
| 称号 | `characters.clone_title_item_id` ＋ `title_display_flag` ＋ `items` 容器 19-23 | ❌ | 无写入者；`MoveItemSpace` 只认 `Main`/`Equipment` |
| 名牌 | `characters`(name_tag_item_id, name_tag_expire_time) | ❌ | 无写入者 |
| 管理权限 | `characters.manage_level` | ❌ | 无写入者 |
| 公会 | `characters`(guild_level, guild_power_war) | ❌ | 整个系统不存在 |

> **2026-09-08 更正：外观那一行原来的「角色列表走这个冻住的 blob」不再成立，只成立一半。**
> 画角色列表的那两帧（客户端请求 `0x0208` 的选人界面，以及建角色成功之后推的那条刷新，
> 两者同一个 `AccountCharacterRosterPayloadCodec`）已改成从实时穿戴现算，和世界内形象走同一条
> 规则（`Application/Characters/DrawnAppearance.cs`）。现算的入口是
> `CharacterRosterService.ListByAccountWithAppearanceAsync`；**同名少一截的 `ListByAccountAsync`
> 是刻意保下来的廉价 listing**，它一行穿戴都不读，供入场校验、名次解析、建角色查槽、删角色查名用。
> 仍读 blob 的是**入场那一批里的角色列表帧**：`CharacterEntryStateTables` 读列，
> `SelectCharacterPreparationService.BuildRosterEntryAsync` 解码。**两个界面因此不一致**——
> 一个画当前穿戴，一个画创建时那一份；这是刻意留下的边界，不是遗漏。
> blob 本身仍然只在角色创建时写入一次，`Server/src` 下零条 UPDATE（本仓代码读出）。

### B. 角色身上发生的事（`character_condition`）

| 领域 | 主人 | 缺口 | Deferred |
|---|:--:|---|---|
| **疲劳／体力** | ⚠️ | 领域服务是**占位空转**；`Total`/`Used` **连列都没有** | — |
| **晶体契约（魔方）** | ❌ | 角色侧两列 ＋ **账号侧六列**（`accounts.cube_black/white/red/blue/clear/gold`），跨两层 | — |
| 冠军突破 | ❌ | 三列 | **D-4：含义未定** |
| 次元 | ❌ | 四个 flag ＋ `character_dimensions` 每项一行 | **D-4 / D-10：`dim_key` 可能是判别列** |
| 表现层 | ❌ | 七列服务器**不解释、直接透传上线** | **D-11：是「发生的事」还是「玩家选的」未定** |
| 硬核模式 | ❌ | 三列 | D-11（`is_hardcore_mode` 是否创建时固定） |
| 网吧 | ❌ | 三列 | — |
| 回归用户 | ❌ | 两列 | — |
| 频道归属 | ⚠️ | `IChannelDirectoryDomainService` 存在但**不写这三列** | — |
| 个人商店 | ⚠️ | `IExpertStoreDomainService` **只在内存里**开关；`is_private_store` 列无人写 | — |
| 惩戒 | ❌ | `chat_ban_end_time` | — |
| 活动角色 | ❌ | `is_event_character` | D-11 |
| 链接槽 | ❌ | 三列，**含义未知** | — |

### C. 玩法系统

| 领域 | 库里在哪 | 主人 | 缺口 | Deferred |
|---|---|:--:|---|---|
| **宠物／契约兽** | `character_creatures` ＋ `condition` 五列 ＋ `stats.equipped_creature_level` | ⚠️ | 领域服务**只写 `field04`**；PvP 身份帧不问它（**唯一对外可见缺陷**） | **D-2：五列在错的表里** |
| **邮箱** | `mailbox_messages/recipients/attachments` | ❌ | **全仓没有任何邮件协议**；四张表全只读 | **D-1** |
| **每日挑战** | 三张表 ＋ `character_daily_counters` | ❌ | 无写入者、无领取协议、无期号边界 | **D-8 / D-9 / D-10** |
| 每日日程 | `character_daily_schedule_states` | ❌ | 无写入者 | **D-12：`mode_or_state` 可能是判别列** |
| 成就 | `character_achievement_complete` | ❌ | 无写入者、无推进点 | **D-14：`achievement_id` 可能判别 `p1..p4`** |
| 副本许可 | `character_dungeon_permissions` | ❌ | 通关不写 `clear_state`；也没有入场后重发的通路 | — |
| 成长武器 | `character_growth_weapon_stages` | ❌ | 无写入者 | — |
| 收集箱 | `character_collectbox_slots` ＋ 内容档 | ❌ | 无收集通路；协议壳已由内容档驱动 | — |
| **专家职业** | `character_expert_job`(7 列) ＋ `_recipes` | ❌ | 无写入者；**双份读取**；`Protocol` 哑弹 | — |
| PVP | `character_pvp`(8 列) ＋ `character_pvp_missions` | ⚠️ | 有大厅、无结算；`Results` 硬编码 `[]` | D-4：`pvp_stat_a` |
| 佣兵支援 | `character_mercenary_support` | ❌ | 无写入者 | — |
| 购买限制 | `character_buy_restrict_items` | ❌ | 无写入者 | D-14 |
| 金币上限 | `character_stats`(gold_carry_limit, auction_gold_limit) | ❌ | 有 `IGoldLimitRuleService` 与四档阈值，**差一个写入者** | — |

### D. 已经有主人的

任务（`IQuestDomainStore` ＋ 任务簿实例）、技能与暗帝连招（`IDarkKnightComboDomainStore`；
**技能页本身还没有 store**）、设置与热键（`ICharacterSettingsDomainService`，**只读**）、
背包与物品（`IItemDomainStore` 一整套）、随身金币（`ICarriedGoldRuleService`）。

### E. 账号侧

| 领域 | 库里在哪 | 主人 | 缺口 |
|---|---|:--:|---|
| **账号权益（霸王契约等）** | `account_premiums(premium_type, end_time)` | ❌ | 无写入者。**疲劳上限要吃它**（基础 156 ＋有效期内权益） |
| 账号货币 | `accounts`(cera, token_cera, happy_token_cera, lucky_star) | ⚠️ | 部分有写 |
| 账号成长 | `accounts`(honor_exp, growth_capsule_exp) | ⚠️ | 部分有写 |
| 抽奖 | `account_increase_chance_lottery_progress` | ❌ | 无写入者 |
| 仓库 | `account_cargo_state` ＋ `items`(account scope) | ✅ | — |

**总计约 40 个领域，其中有真正主人的 8 个。**

## 排序：真正的分界不是大小，是含义定没定

有一批块**做不了**，不是因为难，是因为**没人知道那些列是什么意思**。
`TableOwnershipRuling` 已经把它们记成 Deferred，并说明它们今天的家是
「landed where they are read from rather than where their meaning puts them」——
**暂放，不是裁定**。

**含义未定，先做等于猜**：冠军突破（D-4）、次元（D-4/D-10）、每日日程（D-12）、
成就（D-14）、表现层七列（D-11）、`black_count`／`chaos_point`／`trailing_byte`（D-4）、
链接槽。这些要先做**逆向**，不是先做领域。

**含义清楚，可以直接建**：疲劳、战斗属性、邮箱、每日挑战、副本许可、成长武器、宠物、
专家职业、晶体契约、账号权益、金币上限、收集箱、称号。

### 建议的前三块

**一、疲劳。** 唯一一块「只差把空转换成真账」的：设计已由所有者裁定（存 used 不存 remaining、
上限＝基础 156 ＋有效期内权益现算、惰性期号不用定时器）、seam 已在、拒绝码 `0x16` 已在、
调用点有变异验证钉着。它还会顺带立起两样后面反复要用的东西：
**每日 6 点的期号边界**（每日挑战、每日复活币都要用；既有的每日补货是 UTC+8 0 点，那条不能动），
以及**账号权益的读取通路**（`account_premiums`，晶体契约与网吧也要用）。

**二、战斗属性。** 最基础，**而且今天是错的**——27 个字段冻在创建时的 1 级基础值，
升级不变、换装不变。它是战斗系统的地基。代价是它依赖装备与技能两个还没归位的面，
所以要么排在它们之后，要么本轮只做「随等级重算」这一半并说明另一半没做。

**三、宠物。** 体量最小，且是**唯一一条对外可见的缺陷**（别人看到死宠且永不刷新）。
主人早已存在，只是 PvP 身份帧没问它。同时可以决定 D-2 那五列要不要搬家。

**邮箱不建议早做**：它是唯一一个连**协议都完全不存在**的块（没有 handler、没有 command、
没有 opcode），要从收信、读信、领附件、领金币四条路一起建，且 D-1 说它还要先定义
「一封信里的物品是什么」。它体量最大、依赖最多。

## 这份分析没做的

1. **没有核对 A21 抓包**。哪些帧该继续发、字段的真实语义，都要对着参考会话定。
   本文只谈领域归属。
2. **含义未定的那批，没有尝试去定**。那是逆向工作，需要抓包与客户端反汇编，
   不是读服务器代码能得出的。
3. **账号侧只粗分了**。`TableOwnershipRuling` 明说账号侧「ends up with no change at all」，
   所以那边的表是原样的，按领域切过没有——本文按库表列出，未做与角色侧同等深度的核查。
4. **没有估工作量**。每块的大小差一个数量级以上，但本文只排了依赖与含义，没排工时。
