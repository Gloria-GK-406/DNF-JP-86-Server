# 数据库结构版本与迁移

当前结构版本为 **v49**。主干 v21 之后依次加入公会结构。早期公会分支曾独立使用 v14 至 v40，升级工具会根据库内的实际表识别这条历史分支。

服务端只支持当前这一种表结构。旧库的升级不在服务端里做，而由独立工具 `Tool/DbMigrate` 在停服时执行。

## 服务端：只认当前版本

- 表结构的唯一声明在 `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`，版本号是其中的 `Version`，记在数据库文件头的 `PRAGMA user_version`。
- 打开数据库时（`SqliteDatabase` 构造）：
  - 没有任何表的空库：按声明建出全部表、触发器和索引，写入当前版本号。
  - 版本号等于 `Version`：直接使用，不做任何结构改动。
  - 其余情况（包括没有版本号的旧库）：拒绝启动，提示停服、备份后运行迁移工具。
- 服务端不再补列、不再执行数据迁移，也不保留迁移标记表。

## 迁移工具：Tool/DbMigrate

发布包里自带自包含的迁移工具，部署机不需要安装 .NET：

```bash
tools/DbMigrate/DbMigrate <数据库路径>
```

从源码运行：

```bash
dotnet run --project Tool/DbMigrate -- <数据库路径>
```

- 在数据库所在目录先用 `VACUUM INTO` 生成一致备份 `<数据库>.v<原版本>-<UTC 时间>-<唯一标识>.bak`，再逐步执行迁移，最后写入新版本号。`--no-backup` 可跳过备份，仅用于测试。
- 已是当前版本时直接退出；版本比工具认识的更新、或库里一张表都没有时报错退出。
- 每一步把版本 N 带到 N+1，登记在 `Migrator.Steps`。一步可能分多个事务执行，失败时版本号不变，用备份恢复后排查。
- 版本 0 → 1（`Legacy/LegacyUpgrade.cs`）：针对版本号出现之前的旧库，按旧服务端启动时的顺序执行建缺失表、补缺失列、外观、邮件金币、婚姻关系、婚姻房屋、共享材料、马战 ID 等全部旧迁移，然后删除退役列（`RetiredColumnMigration`）和两张迁移标记表。各步骤先查实际表结构再动手，不假设旧库来自哪个版本。
- 版本 1 → 2（`Migrator.AddDropDrawMultiplier`）：新增掉落点额外抽取倍率表 `drop_draw_multiplier`。
- 版本 2 → 3（`ManagedEventActivationUpgrade.cs`）：新增活动已应用状态及全服疲劳免扣设置，原角色疲劳账本不变；缺省免扣关闭，启动时对账活动当前状态。
- 版本 3 → 4（`ExtraEquipmentSlotBackfill.cs`）：只改数据、不改结构。为已完成任务 649 的角色打开辅助装备位（`ex_equip_slot_stat` bit0），为已完成任务 650 的角色打开魔法石位（bit1）；只做按位或，重复执行无变化。见 #308。
- 版本 4 → 5（`CharacterRelationsUpgrade.cs`）：新增角色关系表 `character_relations`（好友、黑名单，按 `kind` 区分）及按目标反查的索引，既有数据不变。
- 版本 5 → 6（`AdventureGroupColumnRetirement.cs`）：删除 `characters.manage_level`。冒险团改由服务端按账号角色等级现算，不落库；该列从无写入者，各行恒为 0，删除不丢数据。库里已无该列时跳过。迁移后 0.3.0 及更早的服务端无法再打开该库。
- 版本 6 → 7（`AuctionTablesUpgrade.cs`）：新增拍卖行三张表 `auction_listings`（挂单，物品拍卖与金币寄售共用）、`auction_custody`（挂单托管的物品）、`auction_trades`（成交记录）及其索引，既有数据不变。
- 版本 7 → 8（`PrivateStoreCustodyUpgrade.cs`）：新增个人商店托管表 `private_store_custody`（摊位上陈列的物品，按卖家与陈列格），既有数据不变。
- 版本 8 → 9（`AuraSkinSlotUpgrade.cs`）：新增 `characters.aura_skin_slot`，记录角色是否已用扩展券开启光环幻化栏，既有角色一律为 0（未开启）。见 #327。
- 版本 9 → 10（`PlayerPortalUpgrade.cs`）：新增玩家后台设置 `player_portal_settings`、玩家会话 `player_portal_sessions`、玩家操作记录 `player_portal_operations` 三张表及其索引；`gm_audit` 增加 `actor_kind`（缺省 `gm`，既有审计记录归为管理员操作）与 `account_id`，已有该列时跳过。不写设置行，表中无行即缺省值：玩家后台关闭、点券与邮件子开关开启、限额全 0。其它数据不变。迁移后只认表结构版本 9 的服务端无法再打开该库。见 #329。
- 版本 10 → 11（`AuctionQuantityOwnershipUpgrade.cs`）：活跃物品拍卖的当前数量改从托管读取；
  将关闭挂单原余量保留到 `quantity_at_close`，删除 `quantity_remaining`，按件挂单的持久总价归一为初始报价。
  初始数量、成交记录、金币寄售及托管物品不变。迁移前检查活跃物品挂单与托管的数量、模板和物品保存形态，
  不一致时报出挂单 ID 并中止，不自动修补或选一边覆盖。列操作保留扩展列、索引及编号水位。
- 版本 11 → 12（`GameplayParameterStorageRetirement.cs`）：删除疲劳免扣和掉落倍率两张派生控制表。
  活动配置、通知协调记录、角色疲劳账本和其他玩家数据不变；旧控制值不迁回活动配置。
- 版本 12 → 13（`AntonRaidRewardUpgrade.cs`）：新增安图恩攻坚奖励记录，校正特定任务与成就的旧计数。
- 版本 13 → 14（`PvpRecordSplitUpgrade.cs`）：拆分 `character_pvp` 并删除该表。段位、评级段位进决斗战绩表 `character_pvp_record`（胜负平、连胜、最高连胜为 0，段位经验为 NULL，读作所在段位的下限，由规则服务按 PVF 补值）；`skills_initialized` 进决斗技能状态表 `character_pvp_skill_state`；语义不明的 `pvp_rank_point`、`pvp_stat_a`、`pvp_win_streak`、`pvp_lose_streak` 只在任一非 0 时原样存入审计表 `character_pvp_legacy`，无读取方。新增对局表 `pvp_matches`、对局结果表 `pvp_match_results` 及按角色的索引。原来没有 `character_pvp` 行的角色迁移后也没有战绩行，不补造段位。迁移后只认表结构版本 13 的服务端无法再打开该库。
- 版本 14 → 15（`PvpSkillBuildUpgrade.cs`）为角色的决斗技能状态增加专属的当前技能页，原有决斗技能初始化标记保留。
- 版本 15 → 16（`PvpTotalMatchTeamsUpgrade.cs`）新增 `account_pvp_total_match_teams`，按账号保存队名和三个稳定的角色 ID。迁移只建空表。
- 版本 16 → 17（`PvpMatchRatingResultsUpgrade.cs`）为普通决斗场 1 对 1 新增 `pvp_match_rating_results`，按战绩保存角色的赛季、定级场次和 RP 变化。新增索引支持按角色和赛季查询最近一次结算。保留已有战绩，新表从空表开始。升级前的服务端无法打开版本 17 的数据库。
- 版本 17 → 18（`PvpTotalMatchRatingResultsUpgrade.cs`）新增 `account_pvp_total_match_rating_results`。它按对局与账号记录巅峰王者战的赛季、定级场数和 GP 变化，索引支持读取账号当前赛季的最新场次。迁移保留队伍、已有对局和普通决斗 RP，新表从空表开始。
- 版本 18 → 19（`PvpDailyMissionsUpgrade.cs`）新增 `pvp_daily_missions`，按角色、游戏日和任务记录进度与完成奖励对应的对局。迁移仅建空表，保留队伍、战绩、RP、GP 和背包数据。
- 版本 19 → 20（`PvpDailyMissionMetricsUpgrade.cs`）新增 `pvp_daily_mission_metrics`，保存连胜的当前长度与胜利姿势的分项计数。原有任务进度和奖励凭据保留。
- 版本 20 → 21（`PvpColosseumUpgrade.cs`）新增 `pvp_colosseum_runs`，记录入场归属、游戏日、挑战进度、抽取后的奖励和领取状态。迁移仅建空表及索引。
- 版本 21 → 22（`Migrator.AddGuilds`）：新增公会、角色成员及申请关系。既有账号、角色、金币和物品数据保留，旧角色公会等级字段不参与成员关系迁移。
- 版本 22 → 23（`Migrator.AddRecommendedGuildChannel`）为公会增加推荐频道，既有公会初始为未设置。
- 版本 23 → 24（`Migrator.AddGuildDonationPoints`）为公会增加 GM 余额，为成员增加捐赠累计点数。既有公会和成员的新增字段均为 0。
- 版本 24 → 25（`Migrator.AddGuildDonationHistory`）新增公会金币捐赠明细表。累计 GM、成员贡献和金币余额保持原值，明细从升级后记录。
- 版本 25 → 26（`Migrator.AddGuildAttendance`）新增公会每日签到表。已有公会、成员及金币捐赠保持原值，升级后从空签到表开始。
- 版本 26 → 27（`Migrator.AddGuildRankSettings`）新增公会职级配置表。既有公会按默认职级和权限显示，签到、GM 和捐赠记录保持原值。
- 版本 27 → 28（`Migrator.AddGuildMemberRank`）增加 `guild_members.member_rank`。已有成员默认为 4，真实会长继续由唯一会长关系识别。签到、捐赠和职级权限覆盖保留。
- 版本 28 → 29（`Migrator.AddGuildAppearance`）新增公会公告、图标编号和累计已花费的 GM。既有公会使用空公告、默认图标 1 和已花费 0，保留成员、权限、捐赠和可用 GM。
- 版本 29 → 30（`Migrator.AddGuildContents`）新增 `guild_contents`，保存永久公会属性及组队传送次数。既有公会从空权益表开始，成员、图标、公告及 GM 保留。
- 版本 30 → 31（`Migrator.AddTimedGuildContentsAndWarehouse`）补充公会限时属性和经验的到期秒数，并新建共享仓库容量表。迁移保留既有永久属性及组队传送次数。已经拥有公会的角色从容量 0 起步，可购买扩容。
- 版本 31 → 32（`Migrator.AddGuildWarehouseItems`）新增公会仓库物品托管表。已购容量、成员、背包物品和已购权益保持原值。已有容量的仓库从空的托管表开始。
- 版本 32 → 33 新增 `guild_dungeon_progress` 和 `guild_dungeon_clears`，保存公会周期进度及每次通关的角色结算记录。两张表初始为空，保留既有公会成员、GM、仓库物品和已购权益。
- 版本 33 → 34 新增 `guild_warehouse_history`，保存仓库存入、取出和移位的记录。历史从升级后记录，原库位、物品实例和净化率继续使用原表。
- 版本 34 → 35 新增 `guild_board_posts`，保存告示栏正文、作者显示信息及发布时间。已有公会从空告示栏开始。
- 版本 35 → 36 为 `items` 新增 `guardian_gem_keys`，保存勋章的四个守护珠键。原有物品核心和 UID 保持原样，既有勋章的孔位初始化为空。
- 版本 36 → 37 为 `guild_members` 新增 `memo`，既有成员备注初始化为空，保留所属公会、职级和捐赠累计。
- 版本 37 → 38 新增 `guild_alliance_applications` 和 `guild_alliances`。已有公会从无同盟、无待审批申请状态开始。解散时由外键清理双方关联记录，原有成员、物品和公会权益保持原值。
- 版本 38 → 39 新增 `guild_gifts`。每天的送礼额度、已扣的 GM 和领取状态由同一条记录保存。接收方公会解散时，触发器作废其待领取礼物。历史保留赠送时的公会名称和内容。
- 版本 39 → 40 新增公会红包、领取和购买日账三张表。红包跟踪余款、份数、退款和结算时刻。会籍离开触发器记录发送者已离会，已有数据保持原值。
- 版本 40 → 41 新增公会日常贡献账本，以角色、行为和奖励日唯一记账。原有签到和红包记录保持原值。
- 版本 41 → 42 新增公会通关贡献账本，以角色和副本实例去重。原有贡献、净化和红包记录保持原值。
- 版本 42 → 43 新增角色的公会地下城礼盒进度。既有通关记录保留，礼盒进度从 0 开始。
- 版本 43 → 44 新增公会月度贡献明细与发奖记录。历史捐赠由贡献视图直接读取，原有红包与礼盒数据保留。
- 版本 44 → 45 为旧基地升级试验新增 `guilds.hall_stage`。试验撤回后仍保留迁移和该列，已迁移的数据库可以继续使用。现行公会逻辑不读写该列。
- 版本 45 → 46 新增 `guild_dungeon_extensions`，按公会记录地下城跳过一次重置后的有效周期。既有净化率、通关和个人礼盒进度保持原样。
- 版本 46 → 47 为公会增加勋章光环的纹章和装饰框编号。已有公会初始为未设置，原有会员、余额、物品和地下城进度保持原样。
- 版本 47 → 48 为每次入会分配独立的 `guild_members.membership_id`。迁移保留全部流水，并按升级前的归属口径将当前会籍的捐赠、月度事件、日常和通关贡献一次性绑定。迁移输出与加入时间同秒的历史记录数，供核对。今后同秒退回原会也从新会籍计算个人贡献。公会累计及角色、周期、副本的既有去重键继续生效。
- 版本 48 → 49 新增角色消耗品冷却截止表 `character_consumable_cooldowns`。绝对时间采用 UTC ticks。原始 `character_item_cooldowns` 和疲劳药的使用记录原样保留。
- 迁移工具不认识的表和列原样保留。

## 早期公会分支数据库

来自公会开发分支的 v14–v40 数据库同时含有 `guilds` 和旧决斗表 `character_pvp`。相同版本号的主干库走对应的主干升级步骤。使用同一条停服迁移命令，保留自动备份即可。

对公会分支库，工具先用一个事务记下原公会版本，并将迁移起点设为共有的 v13。接着执行正式的 v13 → v21 决斗迁移，拆分战绩并保留原值。到达 v21 后，工具在同一事务中读回并清除临时进度，将版本号置为原公会版本加 8，再执行剩余的公会步骤。旧公会 v20 从正式 v28 继续，旧公会 v40 在补齐决斗结构后直接到达 v48，既有会籍流水的归属维持原值。若决斗阶段发生错误，排除原因后重新运行工具，会从已经记下的步骤继续。

## 修改表结构时

任何改变 `CharacterDatabaseSchema` 声明的改动（新增表、列、索引，修改约束，删除列），在同一个改动里完成：

1. `Version` 加一。
2. 在 `Tool/DbMigrate` 增加从上一版本到新版本的步骤，并登记到 `Migrator.Steps`。
3. 在 `Tool/DbMigrate.Tests` 补用例。`SchemaVersionTests.A_migrated_database_has_exactly_the_shape_the_server_builds` 要求迁移后的结构与服务端新建库完全一致，漏了步骤会失败。

不同分支各自加版本会冲突，合并时需要按合并顺序重新排定版本号和步骤。

## 部署升级

1. 停服。
2. 运行迁移工具（自动备份）。
3. 启动新版本服务端。

列一旦删除，旧版本程序不能直接接着用这个库；回滚需恢复迁移前的备份和对应版本的程序。

## 测试

- 发布打包（`Tool/Release/release.py package`）把迁移工具自包含发布到包内 `tools/DbMigrate`，并在归档前用它迁移一个无版本号的旧库做冒烟：检查版本号、数据保留、备份文件，以及重复运行不改动。

- 迁移相关用例在 `Tool/DbMigrate.Tests`，已加入 `Server/86JP.Server.slnx`，随解决方案构建和测试。
- 服务端测试中手写精简表结构的用例，通过 `Support/HandWrittenShape.Adopt` 调用迁移工具把库带到当前版本后再打开。
