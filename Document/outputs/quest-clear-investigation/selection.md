# `10006007` 候选任务筛选：A21 PVF 证据与保守规则

调查日期：2026-09-23。只读查询 `E:/Project/DNF/DNFClient-A21-debug/Script.pvf`，使用仓库既有 `Tool/PvfProbe/bin/Release/net10.0/PvfProbe.dll` 的 `lst`、`values`、`where`；对照当前 `QuestContent.cs`、`PvfQuestDefinitionSource.cs` 与 `WorldContentEntryRuleService.cs`。以下 **PVF 确认** 指档案声明，**建议** 指本服可实施候选，不是官服算法或实机效果。未改生产代码、客户端、进程或数据库。

## 文案与档案里的分类

PVF 的 `stackable.lst` 将 `10006007` 指到 `event/chn_event_20150813_sheepfarm/chn_questclear_10006007.stk`。其 `[action type]` 为 `[quest clear]`、`[maximum level] 84`，说明为“自动完成当前Lv的所有主线(包含前置)及普通任务”，并明确“无法获得任务奖励”。这是**物品文案**，没有给出任务 grade、等级范围端点、前置 OR 分支或结果消息的算法。

`PvfProbe values n_quest/quest.lst '[grade]'` 对清单收录的 3,441 个任务的首个 grade 值计数：`[epic]` 757、`[side]` 180、`[common unique]` 275、`[achievement]` 466，**`[normal]` 为 0**（另以 `where ... '[grade]' '[normal]'` 验证 0）。`PvfQuestDefinitionSource.GradeTokens` 虽定义 `Normal`，但当前档案没有该值；不能直接把文案中的“普通任务”机械翻译成 `QuestGrade.Normal`，也不能无证地把 `[side]` 或 `[common unique]` 全部当“普通”。`[circle]` 另有 480 条首值，当前 `GradeTokens` 不识别，解析器会保守跳过无法解释的定义（`PvfQuestDefinitionSource.cs:16-39,96-115`）；它也不是可直接代用的普通任务集合。

| 样本（`quest.lst` 收录） | PVF 中影响本题的声明 | 说明 |
|---|---|---|
| `101` `silvercrown/epic_1_silvercrown_1.qst`「青之守护者塔娜」 | `[epic]`、`[job][all]`、`[grow type]-1`、`[level] 1 99` | 主线样本。`Maximum=99` 不等于“任务属于 99 级”；只凭 `L ∈ [1,99]` 在 84 级会误把它当作“当前等级”目标。 |
| `1776` `new_quest/silvercrown/epic_1_luteon_3.qst`「噩梦封印者」 | `[epic]`、`[level] 1 99`、`[pre required quest] 101`、普通物品奖励 | 主线前置链正例；“含前置”需要沿任务 ID 递归求闭包，不能只清当前任务。 |
| `3000` `zelba/epic_85_zelba_01.qst`「战争结束」 | `[epic]`、`[level] 85 99`、前置 `2531` | 券自身上限 84；85 级任务不应成为 84 级券的目标，即使其前置在 84 级可见。 |
| `4073` `shonan/yellow/day_35_yellow_01.qst` | `[epic]`、`[level] 69 99`、`[exposed by npc] 0`、前置 `4080`（不在本 `quest.lst`） | grade 叫 Epic 不足以证明该任务是普通主线目标；隐藏/缺定义前置应停止该分支，不能凭名称强行补全。 |
| `62 → 63` `Towers/epic_20_TowerOfIllusion_{1,2}.qst` | 实际为 `[side]`、`[level] 40 99`，63 前置 62 | Side 确有普通侧线样态，但路径中 `epic` 字符串不等于 grade；是否属于文案“普通”待客户端/实机证明。 |
| `7803` / `7807` `job/s_weapon_master_04.qst` / `s_soulbringer_04.qst` | `[common unique]`、`[job change quest] 1`、`[level] 15 99`、`[reward type][grow type]` 分别奖励成长类型 1/2，并相互列在 `[collision quest]` | “全清可接任务”会同时标记互斥转职分支，却因“不获奖励”不给角色成长类型，从而破坏职业线。 |
| `159` `Epicjob/achievement_50_Epicjob_doom_7.qst` | `[achievement]`、`[job change quest] 2`、`[grow type] 4`、`[level] 50 99`、前置 158、`[reward type][awakening type]` | 觉醒线既不是 Epic 也不是普通 grade；仅按等级批量标记会丢失觉醒效果。 |
| `13513` `special/slot/epic_83_slot_03.qst` | `[side]`、`[level] 83 99`、`[special reward status]` 有物攻等永久属性 | Side 范围内也有关键角色状态收益；“无任务奖励”与这类效果如何处理，不能只按 ItemReward 空值推断。 |

PVF 值查询还显示 `[reward type][grow type]` 共 42 个收录任务，路径均在 `job/`；`[awakening type]` 共 86 个，含上述 `Epicjob/` 样本。`[special reward status]` 有 11 个收录任务。它们是明确的排除/人工审查信号；`QuestDefinition.Rewards`、`JobChangeQuestCode`、`HasSpecialRewardStatus` 均已承载相关声明（`QuestContent.cs:70-86,211-267`，`PvfQuestDefinitionSource.cs:207-224,319-360`）。

## 候选规则（非官服断言）

1. **入口快照。** 角色等级 `L`、职业家族、成长类型、已接、已清标志、账号冲突状态与所持券在同一事务内重读；先要求 `1 ≤ L ≤ 84`。只在 `quest.lst` 收录且 `PvfQuestDefinitionSource` 成功投影的定义中选，未知/不合法 grade 与关系断链按不可自动清处理。已清者不再写；重复任务、每日任务不在目标集合。
2. **主线根。** 最保守可解释集合为 `Grade == Epic`、`RequiredLevel.Minimum == L` 且 `Maximum >= L`、职业与目标角色限制匹配、成长类型匹配、对 NPC 可见、非 event-only、无职位/觉醒/永久属性等特殊效果声明、非问答/互斥未决分支。这里用 `Minimum == L` 是对“当前Lv”的**建议解释**；`Minimum ≤ L` 会把所有低级 Epic 根一并直接清掉，`Minimum ≤ L ≤ Maximum` 也会因 `1..99` 等宽范围产生同样问题。要补旧主线依赖，用下一步的前置闭包，而非扩大根范围。
3. **前置闭包。** 对每个安全的主线根，沿 `CompletedQuestPrerequisiteGroups` 递归寻找尚未清的前置。该字段是“**任选一组，每组全部满足**”，并非所有组都要清（`WorldContentEntryRuleService.cs:178-201`）；优先选已满足的一组，否则只选与当前角色相容、等级 `Minimum ≤ L`、没有未来等级/互斥/职业效果风险的完整组。不能确定分支时跳过此根，避免一口气清互斥线路。历史前置的 `Maximum < L` 不应单独阻断闭包，因为它可能正是文案承诺的未完成旧前置；但当前根仍必须落在自己的等级带中。闭包中若遇 `GrowType/AwakeningType`、`JobChangeQuestCode`、`HasSpecialRewardStatus`、问答答案依赖、账号冲突或缺失定义，就停止该根，绝不把它当普通无奖励前置。环与深度/数量异常也停止。
4. **普通任务。** 当前档案没有 `[normal]` grade；先保留 `Normal` 规则入口，但本 PVF 预计选出零个。`Side` 是待验证的“普通”候选，不默认整类纳入。若产品决定覆盖 Side，仍只能按当前级别根、职业/成长/曝光及安全前置闭包筛，排除 `HasSpecialRewardStatus`、特殊奖励、问答和职业相关链；`13513` 是必须被排除的直接反例。`CommonUnique` 不能整体纳入，因为它包含实际转职分支。要兑现文案中“普通任务”的覆盖，需额外证据确认 A21 客户端的任务类别映射或针对该券的实际差分结果。
5. **执行语义。** 选中的已接与未接任务均只变更任务完成状态/已接槽及必要的任务列表投影，**不调用普通领奖或 `FinishQuest` 的奖励路径**；金币、经验、道具、成长类型、觉醒、永久属性都不发。若安全闭包不完整，不能局部标记该根。结果集、选择的 OR 组和排除原因宜可诊断，便于与后续实机差分；具体 `0x0204` ACK 与客户端刷新顺序另证。

`WorldContentEntryRuleService` 是“现在可接”规则，要求等级落在整个 `[Minimum,Maximum]`、前置已清、未接、非已清（`WorldContentEntryRuleService.cs:36-103,178-243`）。券的前置闭包恰要补尚未清的历史任务，所以不能直接复用可接列表作为唯一输入；但可复用其中职业映射、成长类型和关系校验的语义。尤其 `MatchesGrowType` 对 `JobChangeQuestCode=1/10/20` 作特判（`WorldContentEntryRuleService.cs:165-176`），不能因普通职业家族匹配就判定转职分支安全。

## 尚需证实

此方案只保证行为可解释且避免已知职业破坏，不保证与官服相同。必须从 A21 客户端/实机确认：“当前Lv”是否按最小等级精确匹配、`Side` 是否就是“普通”、隐藏 Epic 是否包含、已接但低于当前级的任务是否一并清、OR 前置组如何选、问答与碰撞任务如何处理、没有可清任务时是否消耗券、无奖励是否连经验/特殊角色状态也跳过，以及响应和任务列表刷新。`10006007` 的 `[maximum level] 84` 仅证明物品可用上界声明，不证明任务筛选上界的内部比较式。
