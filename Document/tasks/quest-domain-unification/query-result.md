# T-001 查询与资格归拢结果

基线：424da804；工作分支 task/despair-tower。仅实现与自动化验证，未提交、未部署。

## 接口与职责

- `IQuestQueryDomainService.QueryAsync / ReadAcceptedAsync / ReadAvailableAsync` 提供三个只读查询。生命周期未接／已接／已完成；`Accepted.CanComplete` 单独表达可提交；已有完成历史且再次接取时，以当前已接状态优先。未知定义明确 IsKnown=false、CanAccept=false。
- `IQuestStateRuleService` 使用统一持有事实计算有效 trigger 和每项材料缺口；供实时查询、登录修复、运行中刷新、任务助手材料需求及提交预演复用。
- `IQuestFactsDomainService` 读取当前角色职业／成长／经验派生等级／已接已完成／当前宠物以及 Main、Shared 和金币。共享材料只取账号容器；稀疏缺条目视为0，Main历史副本排除。宠物通过 AmbientStoreAccess 只读会话读取，因此查询不依赖调用者预先开启事务。
- `IQuestAvailabilityRuleService` 的实现代码已从 World 搬至 Quest；World 保留薄投影适配，不存在 Query→World→Query 的依赖循环。
- `QuestStateRuleService.ApplyClearedRequirements` 是任务前置完成目标的唯一缺口计算函数，已供统一 Evaluate、QuestLog.Accept 初值和 Progress.AdvanceClearedQuestDependents 使用；T-002可复用它移除 CompleteAsync 中剩余公式。

## 已迁移调用点

| 调用点 | 新路径 |
|---|---|
| SelectCharacterQuestEntryService | 生产使用统一查询；只持久化原登录快照的普通槽差异并投影NOTI |
| SeekingQuestProgressDomainService | facts→共享rule→只持久化普通日志槽差异 |
| QuestMaterialRequirementDomainService | facts→共享rule→筛选缺少的正ItemId，不写进度 |
| QuestCompletionEffectRuleService.EffectiveAsync | 原签名保留为共享rule薄适配，支持外部库存预演facts |
| QuestProgressDomainService.Clear依赖推进、QuestLog.Accept初值 | 同一ApplyClearedRequirements |
| WorldContentEntryRuleService.ReadOffers/MeetsQuestRequirements | QuestAvailabilityRuleService |
| CycleDungeonRuleService | 生产直接Quest资格rule；旧构造World适配仍进入同一rule |
| ReplayStateDomainService | 直接Quest资格rule |
| QuestOfferRefreshDomainService.ReadAvailable | 生产走QuestQuery；Refresh所需World其他内容仍由World.Load组合，任务部分为薄适配 |

材料／实时适配器依赖facts与rule，而不依赖完整Query门面：Query的Circle授权会触及地下城入场→Cell→任务助手→材料需求，直接回依赖Query会产生DI循环。三个查询口和适配器共享计算器及事实读取，不复制业务判断。

## 特殊行为

- 镜像Query直接只读当前RunId的镜像记录和规则：虚拟short.MaxValue槽仅在返回列表存在；已领奖为Completed、GivenUp为未接但不可再接；更换RunId不继承前局状态。无普通log写入。
- 登录写回只遍历原快照普通任务；实时写回只遍历普通Carried，不把镜像虚拟槽落库。
- Circle仍需当前intent与原CanAccept授权；Title/Circle/Crack不进入普通可接列表。DailyRandom不可重复的旧规则未改变。
- 多通道中只重算材料低通道，NPC拜访与其他目标通道保留；regional residual规范化保留。

## 构造兼容

现有测试直接构造World、登录、Seeking、材料需求、CompletionEffect、Cycle、OfferRefresh服务。新增依赖使用尾部可选参数，缺省时只构造同一Quest规则的适配实例；不存在第二套条件公式。生产DI明确注册4个新接口，测试已验证实际服务图可解析。

## 验证

- Application项目构建：0警告、0错误。
- 相关专项：141通过、0失败、0跳过；覆盖新增查询／状态、镜像只读生命周期、Seeking、登录、World资格、Cycle内容、QuestProgress和QuestLog。
- 新测试覆盖共享0＋Main遗留副本、差1／足量、金币、多通道、无事务只读查询、查询不写进度/库存/金币、提交Effective与运行时一致、历史完成与当前已接优先、升级后读取当前等级、所有权拒绝、特殊类别过滤、镜像虚拟槽不落普通log。
- git diff --check通过；tracked C#原BOM/LF已保持，仅保留语义差异。
- 测试环境PVF_ARCHIVE_PATH指向 artifacts/avatar-socket-text-20261011/Script.server.pvf。

## T-002／集成剩余

1. 普通CompleteAsync的ClearedQuestRequirements缺口仍有公式，请改调用ApplyClearedRequirements；它的资产执行／镜像Finish／完成券按计划下一阶段拆分。
2. 接取与放弃CMD目前类型路由未移动。普通Accept原本不强制等级／前置门禁，本次遵循兼容性不新增；需要记录为保留行为风险，不能因CanAccept查询新增就偷偷改变Accept。
3. Accept HandBackAsync附近历史“部分发物品失败不回滚”注释与统一delivery现状不符，随接取编排审计清理。
4. 尚未跑全Application／Protocol／Persistence，也未验证新部署或客户端；交由集成串行完成。
