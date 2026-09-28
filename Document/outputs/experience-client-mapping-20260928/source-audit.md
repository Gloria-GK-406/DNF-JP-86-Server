# 经验加成来源与通知槽位只读审计

2026-09-28；主仓 `task/experience-premium-refactor`，参照仓 `ServerS4A21` 的 `master`。本文仅核对当前源码及既存证据，没有运行客户端或修改奖励。`ClientVerified` 仅用于已有 A21 实机探针直接测出的显示/减法行为；参照仓参数名、代码注释和 SelfTest 只证明其实现。

## 三层口径

`ExperienceBreakdown` 是**本次入账的业务来源**，`Total` 为字段求和；`ExperienceBonusLines.Of` 才把可展示来源投影到 `NOTI 0x0025`。当前 `Add(SeriaBlessing)` 写入 `Channel`，`Of` 再把 `Channel` 写到 `+0x1A`。这证明本仓选择让频道奖金显示为「赛丽亚的祝福奖励」，**不能证明 A21 官方将频道奖励定义为赛丽亚的祝福**。频道倍率来自频道脚本 `[server]` 与 `[dungeon]` 的匹配，且当前只在击杀时收集；见 `ExperienceBonusRules.cs:107-118`、`ExperienceRuleService.cs:43-54`、`ChannelScriptExperienceSource.cs:9-24`。账号 premium 当前经策略表按 type 取率，默认空，归 `Other/Unlisted`。已有 PVF 调查在 `premiumlist_new.etc` 把**成长契约资格**定位为 type 84、item 2660703 声明 `[bonus exp] 20`（`Document/outputs/premium-service-server-analysis/premium-service-server-pvf-investigation.md:24`）；但当前经验策略未自动读取该声明，也未按 type 84 归入 `GrowthContract`。黑钻资格类型未确认。结算 `CalculateAsync` 目前只给 `Base/ClearRank`，任务只给 `Base`；见 `ExperienceCalculationDomainService.cs:24-44`。

`NOTI 0x0025` 的**客户端 UI 行为**有较强直接证据：`Docs/ExperienceNoticeSlots.md` 记载 2026-08-28 同一 A21 调试客户端的两轮 14 槽标记法，第一轮 1..14，第二轮 2 的幂，并用两轮基础值回代。九个可用槽同时显示、参与基础值扣除。`+0x2F` 显示「Buff奖励」却不扣；`+0x33` 扣却不显示；`+0x22/+0x37/+0x47` 对这条弹字均惰性。上游 `ExpNotificationBuilder` 把 `+0x2F` 叫 elite、`+0x4B` 叫 removed channel，但客户端实际分别显示「Buff奖励」「赛丽亚的欢迎」；参照仓的标签不能覆盖探针。`NOTI 0x0023` 的 23 个固定槽，既有 `Docs/ExperienceNoticeSlots.md` 第七节明确记录当时未测；参照仓构造器注释声称 8 月 20/21 日标记法，仓内 SelfTest 只断言构包字节，本次未找到可复核的原始截图/帧，故以下 0023 的业务名称均保留 `UpstreamImplementation/待A21验证`。

参照仓当前 `ExpNotificationBuilder.Build` 保留 `memberRewardExp` **形参**只为兼容调用，函数立即丢弃该值，实际写出 83B，与本仓 83B codec 同形；没有独立 member-reward 字段。`protocol/patterns/data/meanings/14-mixed.tsv` 仍按旧 87B 形状把 `memberRewardExp` 标到偏移 9，后续字段整体错位，生成的 `protocol/game/server-to-client/NOTI-0x0025.md` 机器区因此不能作为当前布局依据。`generate-docs.py` 当前标记 A21 镜像；旧人工区的 A12 来源说明也已过时，不能靠那个标签解释错位。

## 来源到槽位

0023 固定槽偏移按 `17+4*i`，`i` 从 0 起；本仓 `DungeonClearPayloadCodec` 目前头部 `Base/ClearRank/0/0`，23 槽全零。0025 偏移为 body 偏移。

| 来源或显示名 | 本仓入账与 0025 | 参照仓 0025 调用/字段 | 参照仓 0023 字段 | 证据界限与风险 |
|---|---|---|---|---|
| 基础、评分 | 击杀 `Base`；通关 `Base/ClearRank`。0025 基础行由累计经验差减各可用行推得；通关 0023 头 `+0/+4` | `level/totalExp` 为入账后快照 | `clearBaseExp/scoreBonusExp` 头两个 u32 | 0023 两个头的**语义**仍是参照命名，位置结构已知；评分不得重复叠入 Base。 |
| 频道 | `Channel` 只在击杀计算；0025 `+0x1A` 显示「赛丽亚的祝福奖励」 | 上游 `seriaBlessingBonusExp` 可写 `+0x1A`，其旧 `channel` 在 `+0x4B` 被置零 | 当前构造器无频道参数 | `+0x1A` 客户端 UI 名已测；频道→此 UI 名是本仓策略，非 A21 官方业务同义。`+0x4B` 实为「赛丽亚的欢迎」，不能用作频道。 |
| 成长契约 | `GrowthContract` 字段与 0025 `+0x1E` 已就位，当前经验策略未产此 Kind；已有 PVF 调查确认资格 type 84、`[bonus exp] 20` | 击杀调用传 `growthContractBonusExp`，来自 premium effect 对怪基础经验计算 | 上游通关 `growthContractExp`→`i=9`；已获击杀的 `monsterGrowthContractExp`→`i=18`，后者由客户端 K[6] 显示「成长之契约」 | 同名契约有**本次通关**与**此前击杀**两份，不能合并发奖；type 84→本仓经验倍率的应用路径仍未接入。 |
| 精灵秘药/经验药 | `SpiritPotion` 预留为 0；0025 无已证实的专用行，`Of` 不写它 | 结算 `experiencePotionBonusExp` 被传作 `fatigueBuffBonusExp`→0025 `+0x15` | `experiencePotionExp`→`i=1`；客户端将此槽投影为 J[7]，静态明细名「远古精灵的秘药」 | `+0x15` 的客户端名是「疲劳值Buff」，不能反推它就是秘药；`i=1` 的另一条 `category=3` 显示名仍 Unknown。上游效果源是在线物品效果 `[exp bonus rate]`，取最高有效率，按**通关 pre-potion 总额**算；不是击杀加成。 |
| 疲劳值 Buff | `FatigueBuff` 预留；0025 `+0x15` | 上游借此槽发结算经验药 | 上游 `i=17` 注释称杀怪侧「疲劳值Buff」，构造器实际写 0 | 0025 UI 名与秘药业务源不同；不能因上游传参借用就把两者合并。 |
| 疲劳燃烧 | `FatigueBurning` 预留；0025 `+0x26` | 构造器参数 `fatigueBurnBonusExp` 可写 `+0x26`，本次列举的生产调用均未传 | 上游注释 `i=10` 通关与 `i=16` 击杀侧，但构造器均写 0；客户端 K[2] 将 `i=16` 显示「疲劳值燃烧」 | 同一文案分通关/击杀口径；尚无本仓真实数值源。 |
| 组队 | `Party` 预留；当前组队倍率已包含在击杀 `Base`，0025 `+0x05` 为可用行但生产无单独来源 | `partyRewardExp` 参数可写 `+0x05`，生产调用未传 | `partyClearBreakdownExp` 头 `+8`；上游注释 `i=13` 组队、`i=14` 幸运角色，但构造器写 0 | 不得把已包含在基础公式中的人数系数再当一笔 Party 加算。 |
| 黑钻 | 本仓无明确 `BlackDiamond` 字段/Kind；若配置 premium rate，当前走 `Other/Unlisted` 且只在击杀收集 | 0025 无黑钻专用已测行 | `blackDiamondExp`→`i=2`；客户端 J[2] 明细名「黑钻」；上游按 premium type `{1,17}` 与 `clearBase×10%` 算 | 客户端标签已获静态证据；上游类型与倍率仍只是其策略，不是本仓/A21 官方资格/倍率权威。 |
| 网吧 | `InternetCafe` 预留；0025 `+0x2A` | 参数 `internetCafeBonusExp` 可写 `+0x2A` | 上游注释 `i=3`「QQ网吧」但写 0 | 0025 行名已测；0023 仅注释。 |
| 极速成长、赛丽亚欢迎、公会 | 对应 `RapidGrowthBuff/SeriaWelcome/Guild` 预留；0025 分别 `+0x43/+0x4B/+0x4F` | 上游三槽恒 0，并误把 `+0x4B` 称频道 | 0023 无已证实对应 | 三个 UI 名由客户端探针确认，业务资格与倍率仍 Unknown。 |
| 装扮、宠物、冒险团、装备纹章 | 本仓未产相应明细；无 0025 已证实专用行 | 上游击杀装备加成计入真实 awardedExp，但 0025 无对应参数 | 头 `+12` 装扮；`i=5`→J[1]「宠物奖励」，`i=7`→J[6]「冒险团奖励」，`i=20`→K[4]「装备加成奖励」 | 装备/契约的击杀累计在上游结算帧复述，不能二次加到 clear grant；头部装扮的客户端文字关联尚未闭合。 |
| 精英/命名怪 | 本仓没有独立加成字段；怪物 rank 参与基础估值 | 上游 `eliteMonsterKillBonusExp`→`+0x2F`，自身代码称只作显示分解，命名倍率已含于 `baseExp` | 分数四元组有 champion/super/boss 摘要 | `+0x2F` 在客户端显示「Buff奖励」且不从基础扣；上游称 elite 并不能证明此槽适合命名怪。 |

## 已获击杀经验与结算

本仓 `ApplyMonsterKillCommand` 在事务内发击杀奖；`DungeonRunDomainService.CommitParticipantKillEffect` 只在成功提交每参与者效果后，把 `award.Breakdown.Base` 按怪物 rank 汇入 `DungeonKillExperienceSummary`（`Ordinary/Champion/SuperChampion/Boss`），其注释明确“Already granted kill EXP”。`DungeonClearPayloadCodec` 把摘要中的 `Champion/SuperChampion/Boss` 写到 0023 分数四元组，`SetPlayResultHandler` 的**新增入账**仅 `GrantClearExperienceCommand` 返回的通关 `Base/ClearRank`。击杀摘要是展示/归档，不应进入结算 `Breakdown.Total`。目前摘要只存击杀 Base，未保留击杀各加成来源；要投影上游的“杀怪成长契约/装备”细分须先补权威记账摘要，不可从总数猜拆。

参照仓同样区分：`DungeonKillApplicationService.cs:827-888` 已将命名怪倍率纳入怪物基础值，另算 growth/equipment 后组成 `awardedExp`；`DungeonParticipantExperienceRuntime` 保存怪物基础、契约、装备与各类摘要；`DungeonSettlementHandler.cs:1537-1555` 从 run 的快照提取已获击杀摘要，`BuildClearDungeonReward` 只序列化这些字段，并显式忽略传入的 `monsterExp`（A21 固定尾部保持 0）。因此凡标有 Monster 的结算字段皆先视为**过去已付奖励的展示**，而非通关新增经验。

客户端静态补证进一步确认 0023 `i=1→J[7]` 的 J 明细名是「远古精灵的秘药」，`i=16→K[2]` 是「疲劳值燃烧」，`i=18→K[6]` 是「成长之契约」，各由数值数组与同索引标签表的绘制链支持。`i=1` 还流向 `+0x65C` 的类别 3 项，该类别的显示名仍未闭合；J/K 标签不可借给这一路。`K[5]` 汇入了进入新增合计的 `i=8` 和 `i=15`，因此 **K 整组不能统称已获击杀摘要**。完整标签注册与 RVA 见 `source-ui-labels.md`、`noti0023-k-labels.asm.txt` 和 `noti0025-label-crosscheck.md`。

## 优先风险与下一证据

1. **P0 守恒与生成源**：0025 的 `+0x2F/+0x33` 不可用于正常明细；`+0x4B` 不可作频道。本仓现有 codec 正确留零；旧机器区混入错位的上游语义（尤其并不存在的 member-reward 字段），应从 `14-mixed.tsv` 等生成源修正，而非只改生成页。
2. **P0 防双奖**：0023 中 `monsterGrowthContractExp`、装备和 rank 摘要只是击杀阶段已获经验；当前 `DungeonKillExperienceSummary` 汇总 Base 的边界必须保持。新增结算加成时仅通关新价值进账。
3. **P1 结算展示**：本仓 0023 23 固定槽全零，故 `SpiritPotion`、黑钻等即便将来入账，也尚无本仓可验证的非零结算实测。客户端静态补证已闭合 `i=1→J[7]` 秘药、`i=2→J[2]` 黑钻、`i=16→K[2]` 燃烧、`i=18→K[6]` 成长等明细名，详见 `source-ui-labels.md` 与 `noti0025-label-crosscheck.md`；`+0x65C` 类别名称和最终合计的视觉对应仍需单独验证。下一步以唯一标记值结算帧核对头四项、23 槽及最终合计。
4. **P1 来源验证**：精灵秘药的 PVF `[exp bonus rate]` 与“通关”使用方式有上游代码证据，尚缺本仓 A21 物品实例/生效和 UI 对照；频道奖励映射成「赛丽亚的祝福」也只是目前展示策略。成长契约已有 type 84 与资源倍率声明，缺的是本仓经验策略接线和 A21 效果验收；黑钻的资格 type 未证。疲劳燃烧、赛丽亚欢迎、公会、网吧的资格/倍率仍未在本轮闭合。

主要源：主仓 `Server/src/Dfo.Server.Abstractions/World/ExperienceBreakdown.cs`、`IExperienceBonusDomainService.cs`、`Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs`、`Game/DungeonClear/DungeonClearSlice.cs`、`Docs/ExperienceNoticeSlots.md`；参照仓 `Server/DfoServer/Network/Builders/Dungeon/{ExpNotificationBuilder,DungeonNotificationBuilder}.cs`、`Network/Handlers/Dungeon/{DungeonProgressNotificationProjector,DungeonSettlementHandler,DungeonKillApplicationService}.cs`、`Game/Inventory/ExperienceBonusEffectService.cs`。旧 70 调查只作模型线索，未用作 A21 字段权威。
