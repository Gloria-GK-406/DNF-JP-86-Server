# JP86 副职业实现方案

2026-11-23 · 调研与设计初稿

建议先实现四职业共用的就职、放弃、状态保存和在线刷新，再以分解师完成第一个可玩的职业闭环。随后用炼金术师验证制作与提取，接入控偶师，最后补附魔师的卡片、宝珠制作和摊位。ServerS4A21 提供 A21 业务和协议的参照实现，70 服务端可以交叉核对业务规则。配置读 JP86 当前 PVF，存在分歧的包体和客户端刷新顺序以 A21 构包、解析与实机抓包裁定。

## 1. 参照版本和内容

| 来源 | 本轮核对 | 证据边界 |
| --- | --- | --- |
| ServerS4A21 `1870c85` | 任务授职、放弃、设备、摊位、配方、提取及协议源码 | 上游实现参照。文档、历史协议摘录和当前代码有个别分歧 |
| 70 `df_game_r` | ELF 符号和 x86 调用、读包指令 | 70 的行为证据，仅用于下面有定位的结论 |
| 台服 70 PVF `Script.20130704.pvf` | 旧格式读取器核对职业索引、经验档、费用和技能 | 独立的版本资料，与本地 70 ELF 的配套关系未验证 |
| JP86 `963ec359b`、dev PVF 与 A21 上游客户端 PVF | 现有协议和服务端接入情况，两个 PVF 的职业配置 | JP86 代码和内容现状。新职业流程尚未实机采集 |

两份 86 PVF 的 `character/expertjob.lst`、`character/expertjob.etc` 和四份 `.exj` 完整解码结果一致。客户端与服务端应使用同一内容版本，任务、配方和物品仍需按各自索引解析。dev PVF SHA-256 为 `a97132dda0970b3e0ce7c3de8052acf0be2664f52e26c819b1d8cb6d427cfcd2`，A21 上游客户端 PVF 为 `8fa275a3fc91b482d66a2dcd744acf3d309ba95eadc23bbbb0c209e9c2dedfd7`。 [dev PVF](../../../../../../ClientStore/mounts/dev/content/Script.pvf) · [A21 上游 PVF](../../../../../../DNFClient-A21-upstream/Script.pvf) · [70 PVF](../../../../../../Research/tw70-pvf-archive/Script.20130704.pvf)

| 类型 | 职业 | dev PVF 就职任务 | 授予技能 | 专属物品与设备 |
| --- | --- | --- | --- | --- |
| 1 | 附魔师 | 2702 | 191 Lv1 | 初始设备耐久 300。后续任务 11007 给提取器 2600482 |
| 2 | 炼金术师 | 2708 | 193 Lv1 | 后续任务 11013 给提取器 2600463 |
| 3 | 分解师 | 2710 | 194 Lv1 | 分解机初始耐久 300，设备作为职业状态 |
| 4 | 控偶师 | 2712 | 192 Lv1 | 后续任务 11016 给提取器 2600474 |

四条就职任务均从 `n_quest/quest.lst` 的有效记录查出，要求角色等级 20–99，NPC 索引均为 144，任务提交物均为 `3037 × 100`。它们声明 `[job change quest] 20`、`[reward type] [expert job]`，并用 `[collision quest]` 指定其他职业的互斥任务。目录还存有拼写为 `nomal_60_...` 的 60 级副本，登记的任务索引指向 `normal_60_...`。类型编号和技能 ID 在 86 与台服 70 PVF 中一致。86 的四职业经验表各有 11 条，首末行数值为 20、1195，70 各有 9 条，首末为 224、2054。86 的放弃费用是 1,000 / 10,000 / 100,000 / 1,000,000，70 PVF 是 100,000 / 200,000 / 400,000 / 1,000,000。职业经验、职业等级和可用设备等级各自读取适用规则。

## 2. 上游与 70 的业务对照

70 符号中的地址是 ELF 虚拟地址。样本 SHA-256 为 `cf45f4e8514c3bf560dd322c146f8441249f6c28246ec8566dcf9ebc4e1b2eb7`。 [70 服务端](../../../../../../Research/1995chen-dnf/other/等级补丁/70/df_game_r)

| 能力 | ServerS4A21 做法 | 70 交叉证据与迁移判断 |
| --- | --- | --- |
| 就职和放弃 | 完成任务时初始化类型、经验、设备、配方和 PVF 技能。放弃先查当前职业的连接任务、费用与金币，再清任务、技能、配方、设备、专属物品及职业身份 | `CExpertJobMgr::OnGiveup` `0x0849E60C` 查询当前职业配置和计数，检查任务及金币，调用任务重置、金币支出、技能移除、类型和经验设置、计数写入。支持采用相同的业务边界，费用和清理范围取 86 PVF |
| 配方学习和制作 | 设计图走物品使用入口，按职业、等级及配方资格学习。炼金、控偶共用生产流程，附魔师的宝珠有卡片分支 | `CAlchemist::OnUseStackableItem` `0x085D1712` 查职业及等级，调用 `learnRecipe` 和 `SendExpertJobInfo`。`Dispatcher_CompoundItemByExpertJob::dispatch_sig` `0x0821EB66` 查职业/交易状态，读配方 ID 和数量，进入 `CUser::compound_item`。生产逻辑可以共享，A21 还带卡片槽位 |
| 装备提取 | 附魔、炼金和控偶共用入口，按职业、提取器、装备稀有度/状态选择基础、追加及大成功规则 | `Dispatcher_ExpertExtraction::read` `0x081D9336` 读一字节加两个 short，`process` `0x081D9412` 调用职业到提取器类型的映射和提取器查找。A21 参考请求另含目标物品栏类型，使用 A21 的六字节形状 |
| 分解摊位 | 建店、进入、分解、维修、升级各有校验。顾客付费给店主，店主获得职业经验并扣设备耐久，自用免去金币转移，耐久耗尽关店 | `OnCreateDisjointStore` `0x085D2D08` 查个人商店占用、耐久和等级。`OnRepairDisjointMachine` `0x085D35CE` 查配置并扣款补耐久。`OnUpgradeDisjointMachine` `0x085D37E8` 查设备和职业/角色等级、费用并设置新等级和耐久。`RequestDisjointItem::read` `0x081CFB28` 读两个 short 和一个 byte |
| 附魔摊位和宝珠 | A21 有独立的摊位附魔命令，交易双方的金币、卡片/材料、装备、店主经验和耐久一并结算。宝珠制作和已有宝珠上装另有入口 | 70 的 `CEnchanter::OnPutItemForEnchant`、`OnTryEnchant`、`OnAgreeEnchant` 分别在 `0x0849DF5E`、`0x0849DFCC`、`0x0849E07E`，并有自用与为他人附魔分支。A21 采用其自身的摊位协议 |

上游代码入口见 [副职业规范](../../../../../../ServerS4A21/Docs/副职业架构业务接入规范.md)、[就职](../../../../../../ServerS4A21/Server/DfoServer/Game/Quests/QuestCompletionApplicationService.cs)、[放弃](../../../../../../ServerS4A21/Server/DfoServer/Game/ExpertJob/ExpertJobGiveupApplicationService.cs)、[生产](../../../../../../ServerS4A21/Server/DfoServer/Game/ExpertJob/ExpertJobCompoundService.cs)、[提取](../../../../../../ServerS4A21/Server/DfoServer/Game/ExpertJob/ExpertJobExtractionService.cs)和[摊位](../../../../../../ServerS4A21/Server/DfoServer/Network/Handlers/ExpertJobStoreHandler.cs)。

## 3. JP86 状态、事务和配置

**角色状态。** 现有 `character_expert_job` 已同时承载职业类型、经验、放弃次数、设备数据，`character_expert_job_recipes` 已有角色与配方的联合主键。把两表作为一个角色副职业状态的持久化来源，在线读写和登录组包共享同一个读模型；进入角色时的 USERINFO subtype0 和 `0x00CD` 从同一状态派生。保留角色创建时的默认行，核对旧存档无职业或历史经验哨兵，让 `type=0` 线上经验为 0。设备 0 耐久同缺失设备分开处理，修复/迁移时不得把合法的耗尽状态补成满耐久。只在现有字段确实无法表达已证实的状态时新增迁移。 [现有表](../../../Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs) · [读模型](../../../Server/src/Dfo.Server.Application/Characters/CharacterEntryAssembly.cs)

**就职与放弃。** 在任务完成事务接上 `ExpertJob` 奖励，校验当前无职业和任务前置/互斥，原子完成材料交付、任务进度、职业初始化和两页技能投影所需数据。任务可见性给 `ExpertJobRequirement` 增加类型和等级判断，并覆盖接取、领取及能绕过常规 NPC 流程的完成入口。放弃按 `[giveup cost]` 的计数档收费，最后一档封顶。参考上游用 `[connect quest list]` 做当前职业成立校验，用 `[clear quests]` 决定清理任务，再移除 `[skill]`、`[giveup delete item]` 所列状态及职业配方/设备。清理职业关联技能应保留普通技能，重新就职保留放弃计数。三个有提取器的职业按任务领取专属道具。 [任务可见性](../../../Server/src/Dfo.Server.Application/Quests/QuestAvailabilityRuleService.cs) · [奖励规则](../../../Server/src/Dfo.Server.Application/Quests/QuestRewardRuleService.cs) · [上游放弃配置](../../../../../../ServerS4A21/Server/DfoServer/Game/ExpertJob/ExpertJobGiveupConfigProvider.cs)

**内容与结算。** Infrastructure 从 `expertjob.lst`、`.etc`、职业 `.exj` 及被引用的任务/物品定义加载只读配置。Application 组合职业命令，复用库存的锁定、空间规划、材料消费、产物发放和装备变更。配方只持久化已学 ID。生产数量、材料、成功率、职业经验和升级自动解锁按 PVF 决定，普通配方、卡片转宝珠及装备提取分别保留其输入检定与结果规则。生产随机失败、非法请求、满包和事务失败应有各自结算语义。现有 `0x001A` 继续提供 NPC/便携分解。玩家分解机复用装备与库存校验，并按自身机台等级、装备状态和稀有度读取产物表。上游共享使用 `etc/disjoint.etc` 的通用装备灵魂定义，其余摊位产物在本版 PVF 校验。普通宝珠上装 `0x0110` 继续用现有入口。 [现有分解命令](../../../Server/src/Dfo.Server.Protocol/Game/DisjointItem/GameDisjointItemCommand.cs) · [普通宝珠上装](../../../protocol/game/client-to-server/0x0110.md) · [普通配方领域服务](../../../Server/src/Dfo.Server.Application/Inventory/RecipeCraftDomainService.cs)

**摊位与并发。** 摊位是城镇运行态，按店主选中会话和区域归属。访客绑定到具体摊位实例，开店校验职业/设备、城镇位置、占用和费用，进店及交易重新校验双方会话、区域、店主和摊位代次。重复开关、关店与结算并发、店主掉线/换图和重开要共享串行化边界。顾客离开仅清理访客绑定，店主离开或设备耗尽关闭摊位并清理其全部访客。切入区域时补发当前摊位，店主恢复外观与旁观者关摊通知按收件人投影。

结算接入 JP86 的 `ICommandWorkUnits` 和库存事务，进入写入边界时声明请求者与摊主的选中会话、库存写入范围。自用合并成同一参与者。一次提交物品和双方金币、设备耐久、职业经验及自动学习配方，提交后才发 ACK、背包、职业和摊位通知。第一条双人链路须验证现有工作单元对两方背包写入的声明与回滚能力。提交前失败不发布成功变化，提交后投影失败走状态重同步，避免再次扣费。协议层按现有独立切片接入，受众与包体留在 Protocol 的投影和编解码中。 [工作单元](../../../Server/src/Dfo.Server.Application/Commands/ICommandWorkUnits.cs) · [协议注册](../../../Server/src/Dfo.Server.Protocol/ProtocolModule.cs)

## 4. A21 协议落点与待定项

| 流程 | 候选落点 | 本轮结论 |
| --- | --- | --- |
| 登录、就职、增长 | USERINFO subtype0、NOTI `0x00CD`、任务完成 ACK | JP86 已有 mode 0/1/2/3/4 编码。附魔 mode 1 还要求卡片资格与等级投影，目前现有角色组装没有填入这组数据。mode 2/4 带配方，mode 3 带机台等级/耐久。上游就职先发 USERINFO0、`0x00CD` 再发任务 ACK，chainType 20 的技能页与消耗条目布局需实机确认 |
| 放弃 | CMD `0x00EF` 空请求 | 上游当前投影用 mode 0 的 `0x00CD`，成功 ACK 另写余额和放弃计数。上游文档另有“放弃阶段名称串”的描述，需用客户端确认包体与顺序 |
| 学习、制作、提取 | 使用设计图的原有道具入口、CMD `0x00EE`、CMD `0x01A0` | 上游生产八字节为配方 ID、数量、卡片槽。提取六字节为职业、提取器槽、目标栏类型、目标槽。生产和提取需补采成功、失败及刷新顺序，提取器制作后特别核对背包全量 `0x000D` 和 `0x00CD` 重扫 |
| 创建、进入、关闭 | CMD `0x0257` / `0x0258` / `0x0259`，NOTI `0x021B` / `0x021C` | 创建有变长名称，摊位 kind 0 为分解、3 为附魔。这两个 kind 与职业类型编号各有定义。现有 `0x021C` 协议页只闭合前两字节的店主 UserId，上游当前 builder 发送四字节，追加 CharacterId。创建的变长字段与关店尾部应以 A21 实帧/消费补齐 |
| 玩家分解和设备 | CMD `0x00EB`、`0x0264`、`0x00F0`，NOTI `0x00CB`、`0x0221` | `0x00EB` 的 A21 writer 和上游当前 parser 均为五字节，最后一字节为栏类型。NPC/便携已有 `0x001A`。维修和升级按设备、经验、角色等级、金币分开校验，升级的满耐久条件以上游规则作候选并补实机 |
| 附魔摊位 | CMD `0x025A`、NOTI `0x0216` / `0x0221` | `0x025A` 请求已有 13 字节静态记录。需核对卡片资格、材料和成功/失败后的双方响应，以及耐久耗尽关店的顺序 |

参照 [0x00CD](../../../protocol/game/server-to-client/NOTI-0x00CD.md)、[0x00EB](../../../protocol/game/client-to-server/0x00EB.md)、[0x0257](../../../protocol/game/client-to-server/0x0257.md)、[0x021C](../../../protocol/game/server-to-client/NOTI-0x021C.md)、[上游五字节解析](../../../../../../ServerS4A21/Server/DfoServer/Network/Parsers/ExpertJob/DisjointMachineRequest.cs)、[关店 builder](../../../../../../ServerS4A21/Server/DfoServer/Network/Builders/ExpertJob/ExpertJobStorePacketBuilder.cs)和[放弃 projector](../../../../../../ServerS4A21/Server/DfoServer/Network/Handlers/ExpertJobGiveupNotificationProjector.cs)。旧分解摊位编号 `0x00EA`、`0x00F1` 等保留为调查线索，只有实际 A21 发出且确认语义后才接入。

## 5. 实施顺序和验收目标

1. **职业生命周期。** 配置、存档和任务串成一条链，分解师先走通真实任务获得技能和设备，放弃后能重新选择职业。覆盖互斥/重复完成、金币不足、任务与技能清理和重登一致性。四种 mode 至少逐个验证登录、就职和放弃刷新。
2. **分解师完整闭环。** 一人自用、两人开店/进店/结算、修理、升级、耐久归零和关店。用已有 `0x001A` 流程做回归。双人同时请求或店主离开时核对金币、原物、产物和耐久，验证单次结算与失败回滚。
3. **炼金和控偶。** 炼金先验证设计图、批量生产、获得提取器、装备提取和跨级自动学习。控偶复用链路并用本职业配方和提取表验收。产物药剂或人偶的使用分别核对现有道具入口，补齐能通过真实玩法触发的必要效果。
4. **附魔师。** 通过已学习配方、卡片资格、提取、卡片转宝珠和修理走通自身玩法，再验证自用/顾客摊位附魔。当前 PVF 另有怪物卡合成、升级与绑定区段，相应 A21 入口和规则需独立取证后确定实施范围。

A21 取证优先级为就职/放弃与重登、分解自用及双人结算、关店、批量生产与提取、最后附魔摊位。使用隔离服务与测试角色，记录客户端帧和参与角色的背包、金币、职业与任务变更。上面的验收目标来自设计，具体包序、库存差量、随机分支预期和通过数量由真实样本确定。本轮完成静态源码、ELF 调用和 PVF 对照，尚无新 A21 实机样本。
