---
kind: rks-review
verdict: changes_required
---
# PVF 字段全量迁移完整审查

## 目标与依据

本报告为独立完整代码及交付审查。目标基线为 `828e769600122f23db2b244e8cfc44ef400d0ffe`，目标为主任务派发审查时的全部未提交 candidate：58 个 tracked diff 文件，以及 `Dfo.Pvf/Schema`、两个 Infrastructure 合成测试、`FieldReadingTests` 和整个任务文档。检查了全部 53 个内容主题的修改及相关既有解析器，而非只检查字符串替换。审查过程中控制器开始同 Spec 修复；下列位置和摘录记录初轮缺陷，不能用本报告批准修复后的新 target。新 target 必须统一冻结后再审。

依据：本工作区及上层 AGENTS.md、TaskContract revision 1、spec.md、direct plan.md，`reviewing-work/SKILL.md` 的 `references/code.md` 与 `references/deliverable.md`。没有配置额外 challenge set。审查不修改产品代码、不运行 dotnet、不提交或部署。执行了只读源检查、TRX 读取、字段扫描和 `git diff --check`。

## 判定

`changes_required`。静态名称迁移及档案绑定方向正确，但类型声明尚未全面约束实际读取。多个旧 reader 仍能将错类型、非法重复、残缺行解释为缺失/default，部分声明还错误描述已有合法结构。已通过的真实档案回归无法证明这些损坏路径符合契约。

无需机械地让每处 `Tokens` 再执行一次无作用 Decode；已有专业解析器若完整校验形状并拒绝使用损坏数据，可以保留。以下发现针对实际过滤/默认/错误形状，而不是以“没有调用 Decode”作为唯一依据。

## F1：旧流式投影仍把损坏字段当作缺失或合法前缀（阻塞）

覆盖 RQ-002/003、AC-002/003。典型证据为初轮 `PvfCreatureRuleSource.ReadEquipment`：声明 `CreatureMinimumLevelField` 为 Int32、`CreatureExperienceAmountRateField` 为 Number，但读取只用 GetTag Offset；`section == minimumTag && token.Kind == Int32` 才赋值，字符串载荷被跳过并保留 minimum=0；经验 Single 直接 `AsSingle()`，NaN 不被拒绝。整个节的多值和重复可取最后值。`PvfDungeonDefinitionSource.Read:181-237` 同样把超范围 minimum/basis/experience 回落到先前默认，错类型在197行跳过。损坏档案由此仍产生可用业务记录。

同共因的已确认入口如下，方法名用于修复后定位；初轮行号仅辅助定位。

| 文件（位于 Server/src/Dfo.Server.Infrastructure/Content） | 具体入口及损坏行为 |
|---|---|
| PvfCollectionBoxDefinitionSource.cs | Build:105-114 仅按 token kind 赋值/收集；坏 Index/日期/列表无诊断；Draft.ToDefinition 截断奇数尾项仍返回盒子 |
| PvfCreatureDefinitionSource.cs | Project、TryReadCreatureEquipment、ReadAmbientLines：过滤错类型，标量取末值，列表坏值丢弃；空/重复标量无字段诊断 |
| PvfCreatureRuleSource.cs | ReadEquipment、ReadHunger：过滤错类型、末值覆盖；Number 非有限值保留；缺失与损坏混同 |
| PvfCreatureEquipmentLifetimeSource.cs | Build/ReadValue:49-70 坏 kind 返回 null；ResolveMetadata 中非法日期/period回落0，与缺失一致；重复/多值取首有效值 |
| PvfDailyActivityDefinitionSource.cs | Build:112、Draft.ToDefinition：非Int32跳过；level不足变 Any，allowance/reward残缺尾行静默丢弃 |
| PvfDropItemPoolSource.cs | ReadCreatable:120-143 错类型跳过、取首非负值、durability夹取；一份坏行可以改变掉落池而不报告 |
| PvfDungeonDefinitionSource.cs | Read/Flush/ProjectQuestConnection：错类型/范围默认、size/map/start/boss残缺行丢弃、坏quest connection变null |
| PvfDungeonRankGradeSource.cs | Read:50-58 仅收集整数；含坏token的rank表可变成空/缺项表，空表与缺失同为None |
| PvfDungeonRoomActorSource.cs | Read:148-216/FlushMonster、AiCharacterLevel:304：坏token跳过、残缺记录丢弃、缺词rank回落0；minimum info按第N个“有效整数”而非真实位置 |
| PvfEntryLifecycleContentSource.cs | ResolveItemMetadata:184-200 坏stack limit变0并夹取，坏type变null；仅取首值、未检查完整节/重复 |
| PvfExpertSkillContractDefinitionSource.cs | GetOverSkillPremiumTypes:24-27 调TryReadInt32跳过错误载荷；坏type不能可靠结束前一type的状态 |
| PvfInitialEquipmentDefinitionSource.cs | Read:135-143 错kind/残缺slot-item配对被跳过，返回部分装备授予 |
| PvfInitialSkillDefinitionSource.cs | Read:119-143 错kind/奇数尾项及超范围技能对被静默丢弃；IsActiveSkill忽略坏type并可取后续字符串 |
| PvfSkillPointDefinitionSource.cs | ReadFreeGrants:84/92 只收整数且只走完整pair，混合/奇数尾项静默丢弃，返回部分免费技能 |
| PvfQuestParameterSource.cs | Read:66-97：错kind跳过、reward中任意负数丢弃、penalty取首整数；坏可选penalty回落80/30/120/140，difficulty未核对完整letter/int对 |
| PvfTownDefinitionSource.cs | ReadMapGeometry:226/231-253 只收整数、忽略尾行；AreaDraft.Accept过滤不认识的kind，固定name/permission路径需与实际形状一并核对 |
| PvfTutorialRewardDefinitionSource.cs | Read:82-103 错kind、坏值、残缺triple丢弃，返回部分奖励 |
| PvfMailItemDefinitionSource.cs | Read:49-58 malformed/duplicate trade limit变null，跟未声明一样；malformed attach变空文本 |

最小充分修正：对已知字段按专业作用域收集完整值并执行正确的 typed/custom 校验；只允许真正未声明字段进入原默认分支；损坏抛含路径/字段的 InvalidDataException，或明确 unreadable/diagnostic 拒绝使用。整数行检查完整行宽和范围；重复必须按所在块的合法重复规则处理，不能全脚本无差别禁止同名。新增针对这些生产入口的合成坏type/额外值/截断/重复用例及合法块重复用例。

## F2：已有部分专业解析器仍接受损坏子集或错误状态（阻塞）

这些主题虽然有局部严格校验，其余路径仍可以把损坏信息投影为可用内容。

| 文件/入口 | 证据与影响 |
|---|---|
| PvfCeraShopDefinitionSource.Load、AddIntegers/AddColumn:139-148/463-466 | 限购、支付类型、service集合只保留有效整数，列读取不验完整stride；坏restriction可被去掉。catalog只走完整stride，最后残行被丢弃。FirstTypeText:255-257只取首值，重复type无诊断 |
| PvfEquipmentDisjointDefinitionSource.ReadTable/Numbers:325-331 | Numbers将非数值/NaN过滤；在足够有效前缀后加入坏token，仍可返回table。cube index取首有效整数，未校验完整混合行 |
| PvfEquipmentMagicSealContentSource.ReadValueRange:140-149、ReadQuantities:209、ReadUnsealCost:226、ReadChangeCost:244 | 值范围过滤非整数并夹取；quantity/cost只走完整pair/4/3宽行，坏/残缺行被跳过，仍返回先前合法规则/成本；ResolveGroupIds也需校验其完整行形状 |
| PvfEquipmentEnchantContentSource.Read:73-82 | string data只检查Count>=2，再对Skip(1)逐个Name且Any匹配；一个合法target字符串与一个非字符串坏token混合仍返回可用附魔定义。EquipmentType也只读首值 |
| PvfNpcShopDefinitionSource.ReadItem:200-201 | attach取第一节，坏kind/额外值/重复attach不会使readable=false，仍返回未封装的可用商品；role也是FirstOrDefault，需按完整role结构验证 |
| PvfPremiumServiceDefinitionSource.ReadExperienceRate/ReadGrowthBossEquipmentBonusPercents:78/94 | type只检查Count==1，直接读取Value而不检查Kind；坏type若Count!=1则沿用前一type，后面的奖励被错归属。ReadContractLevels先过滤混合节亦可保留旧type/旧结果 |
| PvfPackageOpeningDefinitionSource.ParsePremiums:220-237、Parse:246/272 | premium type/item/term的Number损坏时返回0、重复term后值覆盖；package type取第一值，重复lottery cost后值覆盖且不加diagnostic |
| PvfExtraDropContentSource.Read:94-99、PvfIndependentDropTableSource.IsGrowthBonusDungeon:57-60 | ancient dungeon的错kind/空节被视为未设置；坏类型声明能改成ordinary/growth可用类别；其余Tokens混合解析仍可保留专业规则 |
| PvfDropItemReader.Get:72-73 | item category以Any有效字符串判断，非字符串混合/重复字段未被拒绝；声明却为String |
| PvfMonsterDeclaredDropTableSource.Load:27 | 先排除Values.Count==0；存在空[item]被当无表跳过，无法区分损坏与未声明 |

最小充分修正：对每个相关整节/作用域验证所有token、完整宽度和标量唯一性，不能在验证前过滤；损坏不得沿用上一个块的type或已经收集的合法prefix。保持原有专业范围及未知内容处理。现有完整严格的分支不需重写；加入能证明“合法prefix + 一个坏token/残行”不会变成可用规则的生产回归。

## F3：静态声明与合法形状不一致（阻塞）

覆盖 RQ-002、AC-002。这些声明目前因未执行Decode而没有让真实PVF测试红，但使类型层不可信，后续补齐读取会误拒合法PVF。

| 声明 | 实際合法/现有专业形状 | 最小修正 |
|---|---|---|
| QuestParameter.DifficultyField:22 为Int32List | 文件remarks及Read:66-84明确quoted letter/Int32交替 | 声明为Custom难度映射或明确混合Tokens，并完整校验每对 |
| Reinforcement.TypeField:19 为Int32 | Read:168调用NumberList(...,12)，是一组>=12项数值权重 | 改为数值列表/适当Custom；保持既有有限值/非负校验 |
| Quest.ExpertjobLevelField:34 为Int32 | ProjectExpertJob:866-871要求type/level两个非负整数 | 改为配对Custom或Int32List并保持精确pair校验 |
| Quest.IgnoreQuestLevel4ExpField:43 为空Marker | Project:260-264注释明确合法声明carry1，但业务按presence读取 | 声明真实载荷形状并投影presence；不能直接调用现有Marker.Decode |

还应在F1/F2修正时逐一核对scalar/list/mixed声明，尤其Quest的文本聚合、Package/CeraShop复合表。允许内容枚举值仍为普通字符串；它们不是固定字段。

## F4：截断记录检测仅覆盖Document，保留stream入口仍接受损坏档案（阻塞）

覆盖 RQ-003、AC-003。`PvfDocument.Open:27`检查字节长度整除5，但 `PvfArchive.OpenScript:127-137`直接构造reader；`PvfTokenReader.Read:93-100`在剩余不足一个record时直接返回false。于是一个合法`[rank grade]`或其它stream字段前缀追加1-4字节，会被当正常EOF，F1中的stream入口继续返回业务记录。新核心的Truncated_record测试只经过Document，未守住这些实际调用。

最小充分修正：在通用档案/reader入口统一验证script token记录完整性，提供档案路径诊断；或对所有stream入口等价验证。追加保留stream入口的截断回归。通用层仅验证记录完整性，不引入游戏字段。

## 全主题检查覆盖

下表的“局部无额外发现”仅指所检查分支未发现独立于上述共因的阻塞缺陷，不表示完整任务批准。

| 内容主题 | 审查结果 |
|---|---|
| CeraShop | F2 |
| CharacterStat | 实际Decode/块内重复保护；局部无额外发现 |
| ClearRewardRate | typed列表/Single及named-profile专业解析；局部无额外发现 |
| CollectionBox | F1 |
| CreatureDefinition | F1 |
| CreatureEquipmentLifetime | F1 |
| CreatureRule | F1 |
| DailyActivity | F1 |
| DropItemPool | F1 |
| DropItemReader | F2 |
| DropRateTable | typed列表及既有shape检查；局部无额外发现 |
| DungeonDefinition | F1 |
| DungeonExperienceProgression | Custom honor混合头及typed标量；局部无额外发现 |
| DungeonRankGrade | F1 |
| DungeonRoomActor | F1 |
| DungeonRoomDrop | 专业token/完整object/action解析可保留；stream重复/截断由F4及修复统一核对 |
| EntryLifecycle | F1 |
| EquipmentDefinition | 必需及可选值严格type/range/duplicate；局部无额外发现 |
| EquipmentDisjoint | F2 |
| EquipmentDurability | Document/read/UInt16范围；局部无额外发现 |
| EquipmentEnchant | F2 |
| EquipmentForging | 所读行完整校验，失败返回不可用table；局部无额外发现 |
| EquipmentMagicSeal | F2 |
| EquipmentOutworld | 所读混合行完整校验，失败返回不可用；局部无额外发现 |
| EquipmentReinforcement | F3，现有专业完整校验可保留 |
| EquipmentRepair | 错type/重复标为不可用；局部无额外发现 |
| EquipmentRequirement | 坏minimumlevel返回不可用，缺失独立为0；不能以无Decode单独报缺陷 |
| EquipmentSlot | 整体不可用时null；完整type/重复需随混合type修复核对 |
| ExperienceParameter | 已typed Decode且保留重复表追加；局部无额外发现 |
| ExpertSkillContract | F1 |
| ExtraDrop | F2 |
| GoldCarryLimit | 实际字段Int32List及业务配对；局部无额外发现 |
| IndependentDrop | F2，已有完整独立drop混合解析可保留 |
| InitialEquipment | F1 |
| InitialSkill | F1 |
| InventorySortKey | typed Custom及Document；局部无额外发现 |
| ItemSale | 明确permitted=false拒绝坏type/range/重复；局部无额外发现 |
| ItemUsageRule | typed FirstInt32及unreadable；局部无额外发现 |
| ItemUseRule | typed字段错误/重复用例；恢复action游标修正符合调用方需求 |
| MailItem | F1 |
| MonsterDeclaredDrop | F2 |
| NpcShop | F2 |
| PackageOpening | F2 |
| PremiumService | F2 |
| QuestDefinition | F3；专业整数/奖励/关系校验保留，标量字符串完整节/重复与marker载荷需同步核对 |
| QuestParameter | F1/F3 |
| Recipe | 现有完整pair检查及unsupported语义；局部无额外发现 |
| SkillPoint | F1，已typed技能定义与旧FreeGrants不同 |
| StackableAction | typed标量，fatigue流位置回归已补；其action完整参数/日期专业形状随复合入口核对 |
| Town | F1 |
| TutorialReward | F1 |
| VendingMachine | 专业完整Int32行/范围检查；局部无额外发现 |
| WorldDrop | typed列表及完整边界检查；局部无额外发现 |

工具固定name已声明；动态Probe `values/where`运行时查询、enum值和通用`[/`配对算法不计作残留固定字段。Dfo.Pvf新Schema未见游戏依赖，静态声明不保存archive/Offset；Resolve按传入档案绑定，多档案偏移差异测试正确覆盖此边界。未改业务DTO或effect结算源码。

## 交付与验证证据边界

字段扫描脚本实际PASS，扫描1020个生产/工具源；`git diff --check`通过。独立扩展扫描仅命中频道配置文本和通用关闭/枚举算法等合法例外。但扫描保护只证明固定字面量语法，无能力证明typed声明真实执行或shape正确；F1-F3正是脚本通过仍存在的缺陷。

已读取落盘TRX计数：baseline Infrastructure 473 passed/0 failed/2未执行；foundation初次54 passed/7 failed（共70），corrected70/0；typed core72/0；typed Infrastructure初次504/3/2未执行，corrected507/0/2未执行。不能把修正前TRX当作正在修复target的新鲜证明。主任务报告全量Release build 0 warnings/0 errors，但8个项目最终串行测试尚未完成，本审查没有运行dotnet，也没有宣称全服务端通过。

`implementation.md`与`content-migration.md`把现有53主题静态名称覆盖描述为“类型化读取/损坏明确拒绝”的部分表述超出F1/F2事实；修复后更新描述和content-coverage的真实形状。文档保留初次失败和ClientVerified边界的方向正确。`content-migration.md`的“父工作流仍需公开签名收口”属于旧阶段状态，应在最终交付时刷新为当前完成状态或明确历史阶段证据。

控制器需完成上述修正、新target独立批准、新鲜全量构建/测试、完整源覆盖复核、最终任务结果与本地干净结果提交。本报告是本轮完整审查失败证据，不建立整体完成或自动验证通过结论。
