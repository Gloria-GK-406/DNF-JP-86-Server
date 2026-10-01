# PVF 全量迁移实现结果

## 交付
通用 `Dfo.Pvf.Schema` 提供静态不可变 `PvfField<T>`、编号 `PvfFieldFamily`、按档案绑定的 Resolve/Decode 和普通节 `PvfDocument`。字段包含名称、结果类型、标量/列表/混合形状、可选性、范围和结构 token kind。公有 GetTag 只接收声明；内部名字查找仍属于档案实现，不暴露游戏概念。

53 个内容主题的固定字段及工具固定 name 查询迁移完成，主题列表见 content-coverage.md。重复/嵌套解析使用专业作用域校验，无标签序列不伪造标签。频道配置文本、内容枚举及探针外部查询保持各自用途。业务 DTO 和 effect 结算未改。

CharacterStat、ExperienceParameter、InventorySortKey、EquipmentDurability、DungeonExperience 的已知标量已补齐实际 typed Decode。可选字段缺失保留原业务默认；坏字段明确抛出 InvalidDataException，或由原 unreadable 判定拒绝使用。嵌入 QuotedString 标记与 Identifier 不混用，普通 document 拒绝误读嵌入结构。

## 检查与修正
- 基线真实 Infrastructure：473 passed / 0 failed / 2 skipped。
- 核心最终初轮：72 passed / 0 failed / 0 skipped。
- Infrastructure 补齐后第一次：504 passed / 3 failed / 2 skipped；新 first-string 校验的完整扫描破坏后续动作参数游标，疲劳药回归捕获，已恢复返回字符串之后的流位置。
- 修正后：507 passed / 0 failed / 2 skipped。又追加纯合成疲劳动作游标回归，最终全量结果由 completion-evidence.md 给出。
- 之前 duration 实际 Int32 + optional target 已用 Custom 明确；honor grade 混合头和整数对按真实结构解码。初次失败证据保留，不声称全程一次通过，也不声称行为TDD RED。
- 防回流：公开 GetTag 签名测试、完整源扫描脚本与独立代码审查；偏移不同的档案共享同一声明测试。

## 验收边界
本任务验收为全量声明/类型读取、合法数据兼容与自动验证。未修改客户端、PVF、数据库、服务运行环境；测试不升级为 ClientVerified。部署、推送、合并均不在契约内。

## 独立初审后的全范围补齐

初轮独立审查 changes_required，指出静态名称替换不足以防止旧 reader 过滤坏值、错误默认与局部形状声明。复审修复覆盖所有报告入口，保留初审于 reviews/change-review-round-1.md。通用 OpenScript 现在拒绝非5字节对齐记录；document 保留 bare Identifier 值并提供 AtRoot 根层视图；专业 reader 对已知字段逐节验证，合法重复和嵌套仍按业务语义投影。

宠物、收藏、活动、邮件、专家技能、商店、礼包、服务、Dungeon/Room/Entry/MagicSeal 以及其余根层/流式主题均补齐完整形状校验；新增合成坏类型、尾项、重复、非有限数值和不同作用域回归。覆盖清单由源码生成，当前53主题/474个具名字段，另有字段族和工具固定 name。

真实档案暴露了设计假设之外的合法旧结构：副本 map 含多个候选、cube index 有多个物品ID、怪物可先声明空 item 表再声明一个非空表、minimum required level 可按最后声明覆盖、Quest target character 含职业及两个整数操作数、special reward status 是字符串/操作符/数值三元组。修复均完整验证全部声明后保留旧输出；不以全局唯一标量规则拒绝这些合法结构。

重构回归历史（均保留 TRX）：reworked-core 80/0/0；reworked-infrastructure 616通过/22失败/2跳过；兼容修正轮643通过/2失败/2跳过；green命名的下一轮实际647通过/3失败/2跳过，3失败均为新合成用例的空equipment.lst前置错误，已补齐合法索引，不将其声称为通过。quest-shape-diagnosis 是故意失败的临时只读诊断，测试源已移除，不能当最终验收。最终新鲜结果统一见 verification/completion-evidence.md。

## 第二轮复审收口

第二轮独立审查仍为 changes_required，报告保留于 reviews/change-review-round-2.md。剩余四组已逐项修复：SkillPoint 全部已知声明在 last-wins/TP-SP选择前验证、每节SP/prerequisite完整配对；Quest reward接受完整非负序列或完整value/-1对，拒绝非法负值/移位/残缺；Town area配对/头部与每个geometry声明的行宽；EquipmentSlot、ExtraDrop、ItemUse的完整type形状、根层作用域和重复拒绝。

本轮新增68项合成用例（SkillPoint11、type33、structure24）。首先构建捕获静态声明初始化时自引用参数的nullable警告，已改用命名decoder方法；随后真实回归707通过/11失败/2跳过，11失败均为把 permission 关闭后恢复的父区域参数误当关闭marker的载荷。已恢复由专业作用域解析器处理这个边界，保留其合成控制用例。修正后的当前结果及最终Release全套结果见 completion-evidence.md。

## 完整Release发现的合法格式兼容

第三轮代码复审批准后，最终Runner的Release solution/Core/Infrastructure通过，但Application为2586通过/13失败。已保存首轮最终尝试于 verification/attempt-1-release；不能把这次局部成功当作全量通过。12条直接或间接失败关联fixed level skill真实嵌套块，1条关联商城coupon的stackable type两个整数参数。

SkillPoint现支持legacy单Int32 base与真实空opening/closing嵌套块的base0；已知子字段每节完整验证，块未闭合/重复opening/孤立close/坏payload明确拒绝。新增14个直接回归，保留合法重复闭合块last-wins。EntryLifecycle/ItemUse的stackable type与既有InventorySort/Cera/Package一致，首名称加可选完整非负Int32参数列表，equipment type仍为可选单subtype。新增4个coupon多参数控制/坏末参数用例。此前把任意额外Int视为坏型的2条测试按真实结构更正，错误字符串尾项仍拒绝。

修复后的Infrastructure第一轮735通过/1失败/2跳过，该失败仅为旧合成extra-Int错误预期，已改成完整Int前缀加坏字符串尾项。最终新鲜通过结果统一见 completion-evidence.md。本轮新增2个固定字段声明，最新生成清单为53主题/476声明；前文474是当轮历史统计。源指纹统一按Python ordinal路径排序，避免PowerShell文化排序导致同内容不同fingerprint。

Skill嵌套与coupon修正后的Application2597通过/2失败，剩余为IceWaveExp合法重复name被唯一字段API拒绝。Probe确认两次名字不同；原HEAD流式读取取第一字符串。当前改为ReadAll全验证后取first，新增3项合法first-wins与早/晚坏型保护。所有失败TRX保持原样，不覆盖作通过证据。

## 最终交付

第四轮独立复审approved后，对同一72文件源码指纹重跑Release solution、8测试项目和Probe共10项，全部exit 0。最终5198通过/0失败/2既有条件跳过，两个构建0警告/0错误；扫描1020文件PASS，git diff --check PASS。最终清单53主题/476声明，完整证据见 verification/completion-evidence.md；结果以唯一accepted-result本地提交封存并保留工作区。
