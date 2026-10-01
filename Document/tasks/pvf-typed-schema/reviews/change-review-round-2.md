---
kind: rks-review
verdict: changes_required
---
# PVF 全量迁移修复后完整复审

## 目标与判定

目标为基线 `828e769600122f23db2b244e8cfc44ef400d0ffe` 至本轮派发时冻结的完整未提交 candidate，包括全部53个内容主题、通用 Schema/PvfArchive、Probe、新增及修改测试、任务文档。依据 TaskContract revision 1、spec.md、direct plan.md、适用 AGENTS.md 和 reviewing-work 的 code/deliverable 准则。初轮报告保存于 `change-review-round-1.md`。

判定 `changes_required`。初轮大部分损坏路径已实质修复，错误声明及通用截断入口已修正；仍有下列入口保留坏数据的合法子集，违反 RQ-002/003、AC-002/003，阻止完整批准。发现已即时通知控制器，控制器随后开始同 Spec 修复。本报告记录本轮开始 target，不批准报告生成期间的新改动，不自动关闭重修的问题。需重新冻结后复审修正、直接回归与相互影响。

审查只读源码、既有 Probe、TRX 和扫描；没有运行 dotnet、修改业务代码、提交或部署。方法及摘录是本轮读取时的证据，重修期间行号会变化。

## R1：SkillPoint 在验证前丢掉声明，并截断 SP 尾项

`PvfSkillPointDefinitionSource.ReadAllIntSections` 在本轮 target 中先执行 `result[section] = Array.AsReadOnly(values.ToArray())`，之后 `ReadSkill.Values` / `First` 才 Decode 字典最后值。`[required level] "bad" [required level] 1` 或坏 maximum-level 节后跟合法节可生成可用规则，早期坏节无诊断。合法 last-wins 本身不是错误，错误是早期声明没有经过验证。

同入口另外只读取 `Values(usesTp ? SpecialPurchaseCostField : PurchaseCostField)`，未选择的已知成本节即使错类型也不验证；`Read` 直接使用 `Enumerable.Range(0, sp.Count / 2)`，`[sp table] 1 100 2` 会丢掉末值2。ReadFreeGrants 已补齐 Int32 和完整 pair，不再将其报告为未做。

最小修正：在压缩或选择业务投影前，逐节验证全部已知声明，再保留确认合法的覆盖规则；SP 每个声明检查完整配对，不能跨残缺声明拼行。增加早坏后好、未选成本坏型、SP 奇数尾项、合法覆盖的实际入口合成回归；诊断包含脚本路径和字段。

## R2：QuestParameter 丢弃坏负数和残缺 sentinel

`PvfQuestParameterSource.Read:71/73` 仍使用 `Int32List.Decode(...).Where(value => value >= 0)`，只校验 kind，不校验表结构。`100 -2 126 -1` 可变成可用 `[100,126]`；缺失/移位 sentinel 的经验表仍按剩余正数索引，坏层级价格无诊断。

独立运行已有 Release Probe，只读真实 `n_Quest/questParameter.etc`：第326个 token 后经验表为 `100,-1 / 100,-1 / 126,-1 / ...`；第727个 token 后金币表为 `72,101,130,158,187,...` 连续非负整数。源码“两个表都交替”的 remarks 不准确，不能机械要求金币表成对。

最小兼容修正：接受完整非负序列；含负值时，只接受完整 `value>=0,-1` 对，再保持原投影。拒绝其他负数、移位或残缺 pair。更新备注，补两种合法形状及坏 sentinel/负数/尾项回归。Difficulty 的 quoted-letter/Int32 Custom 及 penalty 标量/重复拒绝已关闭初轮问题。

## R3：Town 结构错误被解释为区域缺失或完整前缀

`PvfTownDefinitionSource.ReadTown:130-140` 新 `[area]` 直接替换 draft；无 open 的 `[/area]` 被忽略；EOF 直接返回 TownDefinition，不报告未关闭区域。合法区域后跟未关闭区域会得到部分可用 town，损坏与区域缺失混同。`AreaDraft.ToDefinition:356` 只检查 minimap 是否关闭，没有要求 area ID/map 头完整；闭合但缺 map 的块也可进入 Areas。

`ReadGeometry:238` 只对最终汇总列表检查 `%6/%4`：两个独立 town-movable 节分别含3个整数，会拼成可用6列行；两个 virtual 节各含2个整数同理。已有坏 kind/总尾项/minimap 检查是真修复，但未守住独立声明边界。

最小修正：检查已知 area opening/closing 配对、EOF、ID/map 头，以及每个独立 geometry 声明完整行。保留合法 nested skip 后继续采集，不对未知 kind、QuotedString modifier 或 permission 内部内容新增业务语义。Probe 独立确认 HendonMyre 的 minimap/kind 为 QuotedString、underfoot 的 permission 有嵌套 Identifier 子字段。补未闭合/再开/缺头/独立节残缺合成回归及真实 town 回归。

## R4：三个复合 type 仍取首字符串或任意匹配值

| 实际入口 | 证据及可用坏结果 |
|---|---|
| `PvfEquipmentSlotDefinitionSource.Read:76-85` | `TryMovePast`、首 string 后立即查 BusinessByName；不检查 subtype、完整节或重复。`[equipment type] "[weapon]" "bad"` 仍返回可用装备槽。 |
| `PvfExtraDropContentSource.Build:72-74` | `s.Values.Any(t => t.IsString && ...EndsWith("avatar]"))`；混合坏 token 或重复中只要有 avatar 文本就产生 Avatar 掉落。其 DropItemReader 仅验证 rarity/durability/category，不验证 equipment type。 |
| `PvfItemUseRuleDefinitionSource.TryReadFirstString:193` / `Read:99` | helper 仅检查 `!duplicate && values.Count>0 && values[0].IsString`，注释所称完整验证未检查尾部。`[stackable type] "[unlimited...]" "bad"` 可设置 IsConsumedOnUse=false 且 unreadable=false。 |

最小修正：按真实首字符串/可选 Int32 subtype 完整形状及所在根层合法重复规则校验；损坏抛来源/字段诊断或明确 unreadable 拒绝使用。保留未知枚举值的旧不支持结果。不要把 generic action helper 一律限成 type 形状；StackableAction 的 add-fatigue 已有专业两 token 校验和游标回归。补三个生产入口的坏 subtype、额外 token 和重复案例。

## 初轮关闭与完整53主题覆盖

F3 四个错误声明已纠正：Difficulty 是 letter/int 映射，Reinforcement type 是 numeric weights，Quest expertjob level 是整数序列，ignore quest level 4 exp 是实际 Int32 flag。Quest target-character 的文本加两整数操作数、special-reward-status 三元组也按实际 Token 验证。F4 通过 PvfArchive.OpenScript 的5字节记录对齐检查覆盖保留 stream 入口，核心测试增加直接 stream 截断用例。

“无额外发现”仅指所检查分支，没有覆盖本报告整体失败判定。既有复杂专业完整校验可保留，无需无作用重复 Decode。

| 内容主题 | 本轮检查结果 |
|---|---|
| CeraShop | 整数表/catalog/type完整性；初轮F2关闭 |
| CharacterStat | 分块完整数字及重复保护；无额外发现 |
| ClearRewardRate | typed列表/named-profile形状；无额外发现 |
| CollectionBox | 类型、日期、grid/extend和块配对；F1关闭 |
| CreatureDefinition | 全声明验证及合法末标量/追加列表/ambient；F1关闭 |
| CreatureEquipmentLifetime | 全声明、日期/period拒绝；F1关闭 |
| CreatureRule | 数值、有限值与type完整验证；F1关闭 |
| DailyActivity | scope及level/allowance/reward行宽；F1关闭 |
| DropItemPool | 全根层声明验证后保留首非负值；F1关闭 |
| DropItemReader | category全字符串列表；F2关闭 |
| DropRateTable | typed及已有表结构保护；无额外发现 |
| DungeonDefinition | minimum合法last-wins且全验证、完整各坐标结构；F1关闭 |
| DungeonExperienceProgression | mixed honor/typed标量；无额外发现 |
| DungeonRankGrade | 每节完整Int32List；F1关闭 |
| DungeonRoomActor | monster/AI行宽与实际词位置；F1关闭 |
| DungeonRoomDrop | 完整object/action专业解析；无额外发现 |
| EntryLifecycle | root/type/stacklimit/refill完整性；F1关闭 |
| EquipmentDefinition | 既有严格type/range/duplicate；无额外发现 |
| EquipmentDisjoint | 全finite数字与全部cube候选；F2关闭 |
| EquipmentDurability | root typed及UInt16；无额外发现 |
| EquipmentEnchant | 全stringData/type坏数据不可用；F2关闭 |
| EquipmentForging | 完整专业行及不可用判定；无额外发现 |
| EquipmentMagicSeal | scope及range/quantity/cost/group完整性；F2关闭 |
| EquipmentOutworld | 完整专业行及不可用判定；无额外发现 |
| EquipmentReinforcement | numeric权重与专业形状一致；F3关闭 |
| EquipmentRepair | 既有错误type/重复不可用；无额外发现 |
| EquipmentRequirement | 损坏不可用null/缺失0分离；不以无Decode判缺陷 |
| EquipmentSlot | R4 |
| ExperienceParameter | 每节typed，合法列表追加；无额外发现 |
| ExpertSkillContract | 每个type/over-skill完整标量；F1关闭 |
| ExtraDrop | ancient坏型修复；另R4 |
| GoldCarryLimit | 真实固定字段及完整业务配对；无额外发现 |
| IndependentDrop | ancient坏型修复、完整混合drop；F2关闭 |
| InitialEquipment | 完整slot/item pair及重复slot；F1主体关闭 |
| InitialSkill | 选中initial列表完整kind/pair/range与skill type；F1主体关闭 |
| InventorySortKey | root标量/mixed type；无额外发现 |
| ItemSale | 原permitted=false拒绝坏数据；无额外发现 |
| ItemUsageRule | typed标量及unreadable；无额外发现 |
| ItemUseRule | 标量/列表修复、游标保留；另R4 |
| MailItem | 完整attach/trade-limit/重复；F1关闭 |
| MonsterDeclaredDrop | 全item验证、合法空前缀/单非空；F2关闭 |
| NpcShop | attach坏型/重复整体unreadable；F2关闭 |
| PackageOpening | premium形状/duplicate type/cost；F2关闭 |
| PremiumService | 所有type包括早返回后数据；F2关闭 |
| QuestDefinition | 已知字段预验证/真实mixed形状；F3关闭 |
| QuestParameter | difficulty/penalty修复；另R2 |
| Recipe | 既有完整pair/unsupported；无额外发现 |
| SkillPoint | FreeGrants修复；另R1 |
| StackableAction | 专用action形状/日期及游标；无额外发现 |
| Town | kind/尾项/minimap修复；另R3 |
| TutorialReward | 完整Int32 triple，旧禁用行筛选保留；F1类型/尾项关闭 |
| VendingMachine | 完整专业行/range；无额外发现 |
| WorldDrop | typed/完整边界；无额外发现 |

Schema 无游戏概念或游戏项目依赖；静态 PvfField 保存名称、decoder、optional/token kind，不保存档案/offset，Resolve 按传入档案绑定。跨档案不同offset测试覆盖这一边界。Document 保留 occurrence，提供 singleton/ReadAll/ValidateFields/AtRoot，未将合法重复强制单值化。Probe固定name已声明；动态 values/where、内容枚举和未知关闭配对算法不是固定字段残留。

## 交付与验证证据边界

独立执行 check-field-access.py：PASS，扫描1020个生产/工具源；git diff --check通过。源码生成覆盖文档记录53主题/474具名字段；扫描只证明固定字段语法，不证明声明完整执行，R1–R4说明为何需要源审查。coverage generator 的根目录定位和目标输出限定正确，审查未运行会改写文档的 generator。

读取实际 TRX：reworked-core 为80 passed/0 failed；reworked-infrastructure-approved-candidate 为650 passed/0 failed/2 notExecuted。它们未覆盖本轮发现的坏组合，不能证明所有损坏路径完成。implementation.md 保留616/22、643/2、647/3失败历史和fixture修正，未把诊断失败TRX当作通过。

usage-examples.md 的标量、合法重复、Tokens专业责任与源码一致；“全部专业字段完整验证”的总述须修完本报告问题才能成立。run-final-checks.ps1 正确定位worktree，串行 Release solution build、8个测试项目、Probe build，记退出码且失败即停；尚未运行，脚本存在不是成功证据。最终新鲜全套验证、spec-result/completion evidence和本地提交由控制器执行。本报告不批准重修后的新target，不建立整体完成或 ClientVerified 结论。
