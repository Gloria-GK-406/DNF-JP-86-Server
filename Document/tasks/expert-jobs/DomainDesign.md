# JP86 副职业领域服务设计

建议用 **一个角色副职业入口、四个按角色取得的职业服务、一套副职业存档**。角色的任务、技能、金币、背包和摊位仍由各自领域管理。副职业服务调用这些业务能力，在现有玩法事务里完成一次就职、放弃、制作或摊位结算。

本设计参照本地可见的 `origin/main` `cada01a0e`。该版本已有副职业状态与配方表，写入口主要是建角时插入默认行。现有专家商店服务已接入选中会话清理，目前只记录摊主或摊主会话，适合作为摊位运行态的扩展点。 [副职业存档表](../../../Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs) · [现有摊位服务](../../../Server/src/Dfo.Server.Application/Sessions/ExpertStoreDomainService.cs)

## 角色怎样取得副职业服务

`IExpertJobDomainService.Get(CharacterId)` 返回一个绑定角色的 `ICharacterExpertJob`。它的形状类似现有的 `characters.Get(id)`，只绑定角色 ID，按需读取当前状态。副职业服务放在独立领域，角色的一般属性、经验和技能仍经现有角色接口处理。 [角色领域接口](../../../Server/src/Dfo.Server.Abstractions/Characters/ICharacterServices.cs) · [角色事实约定](../../../Document/CurrentCharacterFacts.md)

```csharp
var expertJob = expertJobs.Get(characterId);
var disjointer = await expertJob.GetDisjointerAsync(ct);
if (disjointer is null)
    return DisjointResult.WrongProfession;

return await disjointer.DisjointAsync(operation, ct);
```

| 接口或方法 | 职责 |
| --- | --- |
| `ICharacterExpertJob.GetStateAsync` | 读取当前职业、经验、派生等级、放弃次数、设备和已学配方的只读状态 |
| `ICharacterExpertJob.JoinFromQuestAsync` | 消费服务端已验证的任务授职结果，在任务完成事务中建立副职业。输入绑定本角色及任务，授职类型来自任务定义 |
| `ICharacterExpertJob.GiveUpAsync` | 检查当前职业、连接任务与金币，协调收费、相关任务/技能/专属物品清理、清空职业状态和关闭摊位 |
| `GetDisjointerAsync` → `IDisjointerDomainService?` | 玩家分解机结算、修理、升级。分解机等级与职业等级分别判断 |
| `GetEnchanterAsync` → `IEnchanterDomainService?` | 学习设计图、卡片转宝珠、装备提取、附魔摊位结算及设备修理 |
| `GetAlchemistAsync` → `IAlchemistDomainService?` | 学习设计图、炼金制作和装备提取 |
| `GetDollControllerAsync` → `IDollControllerDomainService?` | 学习设计图、人偶制作和装备提取 |

四个 getter 以角色当前职业为准。遇到无职业或职业不符即返回 `null`，实际操作中的资格拒绝通过业务结果返回，例如材料不足、设备等级不够或摊位已经关闭。职业服务实例由副职业入口创建，内部依赖可以共享。调用方在当前工作单元内取得并使用实例，实例仅持有角色标识和依赖。写入时在同一事务内再校验职业及所需的精确会话、配方、装备或设备事实。放弃、重新就职、回选或换角之后，下一条命令重新取得职业服务和业务输入。任何 getter 的结果都只表示查询时的职业。

例如，玩家进入他人的分解摊位时，服务端从访客绑定解析出**摊主**，用摊主 ID 调用 `GetDisjointerAsync`，以顾客作为原物、产物及支付的归属者。顾客可以没有副职业。开店和进店通过专家商店入口管理，分解和附魔的资格及结算仍由相应职业服务决定。

## 一个角色怎样存储

| 事实 | 保存归属 |
| --- | --- |
| 职业类型 `0–4`、累计职业经验、放弃次数 | `character_expert_job` 按 `character_id` 一行 |
| 分解机等级和耐久、附魔设备耐久 | 复用同一行的现有列。仅当前职业对应的设备字段参与业务 |
| 已学配方 | `character_expert_job_recipes` 按 `(character_id, recipe_id)` 去重。配方 ID 的规则解释来自当前 PVF |
| 角色等级、职业授予的技能、任务、金币、物品 | 现有角色、技能、任务及库存领域 |
| 职业等级、卡片资格、配方产物、设备满耐久和费用 | 从经验、设备等级及当前内容规则计算 |
| 开店、店名、标价、位置、访客绑定 | `IExpertStoreDomainService` 管理的城镇运行态 |

建议用一个类型化的领域状态读取两张现有表，并用同一 SQLite 事务更新它们。登录和在线刷新从该状态生成 USERINFO subtype0 与 `0x00CD`。两种投影使用相同的职业及经验事实，避免后到的 USERINFO 覆盖旧状态。建角沿用默认记录，旧角色的缺省情况按无职业处理，已有的有效状态可继续读取。初始耐久在就职时由 PVF 写入，持久化的 0 耐久可以表示设备已经耗尽。放弃时把职业、经验、配方和设备清零，更新放弃计数。重新入职从该职业的起始状态开始。删除角色时清理两张表的角色数据。现有字段足够表达本次已知行为，方案以复用表结构为主。 [角色入口组装](../../../Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs) · [选角编码](../../../Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs)

## 业务怎样落到模块

| 位置 | 内容 |
| --- | --- |
| `Abstractions/ExpertJobs` 和 `Data/ExpertJobs` | 服务契约，类型化的输入、结果和状态。调用方只提交领域参数 |
| `Application/ExpertJobs` | 角色入口与四职业业务。配方学习、生产计划、提取和经验增长由用到它们的职业共享，职业各自保留特有公式和输入。规则服务根据 PVF 和调用方提供的事实计算资格、费用和结果 |
| `Application/Quests` | 任务系统验证完成条件，在现有奖励结算事务调用授职；可接任务规则查询副职业类型和派生等级 |
| `Application/Sessions` | 扩展现有专家商店，保存店主选中会话、摊位实例和访客绑定。复用回选、断线与换角清理，接入离区与耐久归零；事件按真实受众发出 |
| `Persistence` 和 `Infrastructure/Content` | 分别实现副职业状态存储和 PVF 内容解析。加入同一数据库事务的现有载体 |
| `Protocol/Game` | 各协议切片解析请求、声明工作单元和受众、按领域结果生成 ACK 或通知。就职和放弃后的职业信息从统一状态投影 |

**就职。** 任务本身验证前置任务、等级、互斥和交付物。完成时传入经服务端验证的授职结果，副职业入口再次检查角色当前无职业。材料、任务进度、职业初始化、配方与应授技能在一次提交内完成；后续任务照其 PVF 奖励发提取器。

**放弃。** 读取当前职业及放弃计数，从配置决定费用与清理范围，再调用任务、技能、库存领域完成清理。金币、身份、设备、配方及任务等持久变更共同提交。摊位和访客按精确实例清退，放弃失败时继续保持原营业状态。其他职业及普通技能的资料由其原 owner 管理。

**生产或提取。** 校验真实道具槽、配方和职业等级，按 PVF 做结果和库存空间规划。同一操作产生的材料消耗、产品、经验及跨级自动学习共同提交。玩法判定失败按规则结算，满包、无资格及未提交异常按各自结果处理。

**摊位双人结算。** 先用服务器的访客绑定定位摊主，工作单元声明双方精确会话，再在写入处复核店主、区域及摊位实例。涉及的物品、双方金币、店主耐久和职业经验一起提交，自用合并成一个参与者。耐久归零关店与正常关店使用同一个摊位实例边界。现有执行器及工作单元对 Main 和多 owner footprint 各有约束，落地时先验证可用的双人结算路径，两个角色的资产必须由一个事务结算。提交结果不确定时走本仓既有的会话保护；提交成功后的投影失败通过重新读取当前状态恢复。 [玩法执行与事务](../../../Document/GameplayExecution.md) · [现有任务完成服务](../../../Server/src/Dfo.Server.Application/Quests/QuestCompletionDomainService.cs)

协议和内容证据沿用 [上游与 70 对照](UpstreamEvidence.md)。例如关摊尾部、放弃刷新形状和双人附魔时序仍需 A21 实机确认，领域接口不依赖这些包体细节。
