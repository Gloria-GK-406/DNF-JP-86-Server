# 奖励发放迁移矩阵

基线：`4aec17baa083271f322012043511f9f2845112db`。证据层级：CurrentImplementation / 自动化存储与协议编码测试；不是 ClientVerified。

## 结构和职责变化

此前来源各自调用底层授予或发送邮件，满包可能忽略奖励、拒绝整个操作，绝望塔另有单独邮件回退。

现在：来源判定资格、决定奖励和数量、扣费/标记领取 → 同一事务内调用 `IItemDeliveryDomainService` → 先填兼容堆叠和空槽，剩余实例交邮件托管 → 提交后 EventBus 投递邮件提示与来源的绝对背包投影。统一服务不决定掉落概率、入场条件或防重复键；失败使来源状态一起回滚。

## 已切换的系统物品奖励

路径均相对于仓库；`Application` = `Server/src/Dfo.Server.Application`，`Protocol` = `Server/src/Dfo.Server.Protocol`。

| 制造者/入口 | 当前交付路线 | 旧假设的修正与验收 |
|---|---|---|
| 普通免费、金币付费、黑钻翻牌 `Protocol/Game/CardReward/SelectCardCommand.cs` | 直接 Delivery | 邮件也算交付成功；领取/支付状态仍同事务；只投影实际入库槽 |
| 死亡塔/迷妄塔结算 `Application/World/IllusionTower/IllusionTowerRewardDomainService.cs` | 直接 Delivery | 全部候选保留，邮寄装备使用实际实例种子展示；死亡/放弃结算共享此路径 |
| 塔内直接任务物品（同文件） | 直接 Delivery | 持有限额计入未领取邮件，避免重复刷满包奖励 |
| 绝望塔结算 `Application/World/DespairTowerDomainService.cs` | 批量 Delivery | 删除自有邮件回退，使用统一托管和事务 |
| 教程 `Protocol/Game/TaggedIdReport/GrantTutorialRewardCommand.cs` | 批量 Delivery | 满包不丢奖励；未知物品/缺失容器不能假装完成 |
| 接取任务授予物 `Protocol/Game/QuestAcceptance/AcceptQuestCommand.cs` | 批量 Delivery | 不再在来源自行寻找空槽/静默跳过 |
| 普通/循环任务完成 `Application/Quests/QuestCompletionEffectRuleService.cs` | GrantBasicCharacterItemEffect → Delivery | 预检查允许物理奖励溢出；任务完成、材料扣除与奖励同事务 |
| 镜像阿拉德任务 `Application/Quests/MirrorAradQuestRuleService.cs` | 同上 | 保留任务生命周期和唯一领取规则 |
| 击杀/破坏物直接任务奖励 `Protocol/Game/MonsterDeath/PendingQuestItemGrants.cs`，由 ApplyMonsterKill / ApplyObjectDestruction 调用 | 直接 Delivery | 加入未领取邮件数量检查；缺失任务包报错并回滚 |
| 普通礼包/可选礼包、装扮包、魔盒（含批量）`Application/Inventory/OpeningEffectPlanRuleService.cs` | GrantPreparedItemEffect → Delivery | 邮件奖励返回独立快照，不依赖背包槽；预检查允许溢出；装扮详情透传 |
| 幸运罐/双倍罐 `Application/Inventory/LotteryOpeningDomainService.cs` | 上述 Prepared Effect | 删除“背包更新数必须等于奖励件数”检查；ACK直接读取奖励快照；满包/一格双倍均测试 |
| 售货机 `Application/Inventory/VendingMachineDomainService.cs` | 同上 | 邮件展示的 null 槽不计入背包更新列表；满包扣币并寄奖励，ACK可编码 |
| 强化破坏返还/分解产物 `Application/Inventory/EquipmentEffectPlanRuleService.cs` | GrantStackableEffect → Delivery | 产物不因材料包满而丢失；共享材料先填剩余容量再邮寄；扣费/装备销毁一起回滚 |
| 每日补充 `Application/Inventory/DailyRefillDomainService.cs` | 直接 Delivery | 领取周期照常持久化；未领取邮件计入持有量，防跨天重复补发 |
| 每周签到 `Application/Events/WeeklyAttendanceDomainService.cs` | Delivery / MailOnly | 原本就是邮件奖励，保留邮件交付方式及签到幂等标记 |
| 骑士马战竞猜 `Application/Events/JoustDomainService.cs` | Delivery / MailOnly | 按原冻结奖励快照交付，保留结算唯一性 |
| 任务助手人偶 `Application/Accounts/QuestHelperPuppetDomainService.cs` | Delivery / MailOnly | 保留背包/未领取邮件去重与发放标记 |

## 全部低层制造/写入调用的归类

这里列的是确实仍创建/授予/写入物品的生产位置，不将未修改文件误说成已迁移。

| 剩余调用位置 | 保留理由/约束 |
|---|---|
| `Application/Inventory/ItemDeliveryDomainService.cs` | 本次统一物理奖励入口；创建一次、同一实例去向变更；批量附件每封最多10项 |
| `Application/Inventory/ItemGrantDomainService.cs`、`ItemGrantDelivery.cs`、`ItemLifecycleDomainService.cs`、`ItemContainers.cs`、`CharacterBusinessInventoryDomainService.cs` | 容器/生命周期基础设施；为下列明确例外服务，不是另一个奖励来源 |
| `Application/Effects/InventoryEffectHandlers.cs`、`QuestCompletionEffectHandlers.cs` | 仅 VirtualResource 分支保留底层授予；物理分支均调用 Delivery |
| `Application/Effects/OpeningEffectHandlers.cs` | GrantOpeningVirtualEffect 保留虚拟槽更新；GrantPreparedItemEffect 物理分支走 Delivery |
| `Application/Inventory/OpeningEffectPlanRuleService.cs`、`RecipeCraftDomainService.cs` | 材料合成/配方制作显式 AllowMail=false，调用 Delivery/InventoryOnly；保留制作必须入库保证 |
| `Application/Inventory/EpicCraftDomainService.cs` | 委托上述配方制作；史诗制作成功仍需有背包容量 |
| `Application/Inventory/AvatarCompoundDomainService.cs` | 消耗两件装扮的合成置换；指定输出槽，属于制作，不改变为邮寄产物 |
| `Application/Inventory/NpcShopDomainService.cs`、`CeraShopDomainService.cs` | 玩家购买；原容量/扣费/货币协议不改，保留 ApplyPlannedAsync |
| `Protocol/Game/GroundPickup/GroundPickupSlice.cs` | 玩家地面拾取；满包保留地面物，不转邮件。拾取史诗碎片分流至图鉴，继续独立领域计数 |
| `Application/World/IllusionTower/TowerInventoryDomainService.cs` | 塔内临时快捷物品，离开副本即销毁；不能变成永久邮件 |
| `Application/Characters/CharacterRosterService.cs`、`Initialization/InitializationAssetGrants.cs` | 新建角色/管理初始化资产，已在规格明确排除；不把初始化语义扩成系统奖励 |
| `Application/Achievements/TitleBookDomainService.cs` | UnlockAsync 确实创建成就称号实例并放到指定称号簿槽，属于专属领域解锁资产；Equip 创建镜像。不能送到普通背包或邮件，否则破坏目录解锁/镜像关系 |
| `Application/Administration/GmOfficialMailDomainService.cs` | 显式管理员邮件：用户指定收件人、正文和附件状态，保留创建+SendPrepared；与游戏自动发奖区分，不能改成交付背包 |
| `Application/Mail/MailItemTransfers.cs`、`MailboxDomainService.cs`、`MailboxSendDomainService.cs` | 邮件发送/领取与托管；领取满包保留原附件，不能再次转邮件导致循环 |
| `Application/Inventory/ItemMovementDomainService.cs`、`InventorySortDomainService.cs`、`LegacyWornItemNormalizer.cs` | 已有资产移动、排序和兼容位置修正；没有奖励制造 |
| `Application/World/DungeonCellFactory.cs`、`MonsterDropDomainService.cs`、`HellRewardDomainService.cs`、`QuestEnemyDropDomainService.cs` 及对应 Drop Rule | 决定地面掉落描述，尚未交付背包；DirectQuestItem 分支最终通过上述 PendingQuestItemGrants / 塔奖励路径交付 |
| 经验/金币/点券/疲劳/权益/史诗图鉴 Effect 与服务 | 非普通物理物品，继续领域计数；不能转成邮件堆叠资产 |

## 反向扫描及局限

执行 `python Document/tasks/item-delivery/tools/scan-reward-routes.py`。脚本扫描整个生产 Application / Protocol 的底层授予依赖、所有实例创建调用、计划写入、新入口、奖励 Effect 和 prepared mail 写入，生成 [migration-scan.md](migration-scan.md)。出现未归类文件则非零退出，提醒新增奖励厂家需要裁定。它不是语法树或完整数据流证明；结论还结合 `PutAsync/AddAsync/Grant/Reward` 语义反查及独立审查。

独立审查从未修改的制造者反查，首轮发现并修复邮件类型/详情、宠物运行态、开罐和售货机后置检查、共享材料部分容量、塔奖励展示种子六类缺陷，详见 [审查记录](reviews/change-review.md)。

实机待验证：背包满/部分容量下翻牌、任务、礼包与罐子，邮件提示和领取；装扮/宠物邮件展示与领取。自动化编码成功只证明现有 codec 可以构包，不代替客户端行为验收。
