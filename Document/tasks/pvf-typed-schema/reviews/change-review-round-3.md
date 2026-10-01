---
kind: rks-review
verdict: approved
---
# PVF 全量迁移第三轮独立复审

## 目标与判定

`approved`：本轮冻结代码与交付接口审查通过，前两轮阻塞发现 F1–F4、R1–R4 已关闭，未发现需要继续修正的具体阻塞缺陷。批准代码审查不代表整个 TaskContract 已完成；主任务仍须执行审查后的新鲜 Release 全套验证、生成最终结果证据并完成本地结果提交。

目标基线为 `828e769600122f23db2b244e8cfc44ef400d0ffe`，目标为第三轮派发时冻结的完整未提交 candidate：全部53内容主题、通用 Schema/PvfArchive、Probe、所有新增及修改测试、当前任务文档。依据 TaskContract revision 1、spec.md、direct plan.md、适用 AGENTS.md 和 reviewing-work 的 code/deliverable 准则。完整主题检查矩阵及历史缺陷保存在 `change-review-round-1.md`、`change-review-round-2.md`；本轮复用已检查的未变部分，重新读取 R1–R4 的实际生产修正、68项新增直接回归及相关通用接口，并检查相互影响，未以生产者报告代替源检查。

冻结源码指纹：相对基线修改及非忽略 untracked 的 Server/Tool `.cs`、`.csproj` 共72个文件，按相对路径排序，将 `path|SHA256(file)` 用 LF 连接并加末尾 LF，再以 UTF-8 计算 SHA256，结果为 `ee501f4bdf5ad1b381a100ef810cff326bb453822324b9942c136aa0a4826281`。文档与验证产物不包含在该源码指纹中。

审查只读源码、现有TRX与扫描，只修改本报告；未运行 dotnet、改业务、提交或部署。最终结果文档尚待主任务按实际验证结果生成，不把其当前缺席当作本轮代码缺陷。

## R1–R4 关闭证据

| 发现 | 实际修正与直接回归 |
|---|---|
| R1：SkillPoint早坏后好/未选成本/SP尾项 | `ReadAllIntSections` 遍历 Document 的每个 occurrence，先查 Known 并 Validate，再写字典保留合法 numeric last-wins。未选中的 TP/SP 成本仍经过验证；每个 SP、prerequisite、skill/awakening-skill 声明独立检查 pair。First 使用实际 path。11项合成测试经公开生产入口覆盖坏type/空/额外标量、早坏后好、两种未选成本、早期奇数prerequisite/SP及合法末值输出。 |
| R2：QuestReward任意负数过滤 | 两表改为 Custom，完整 Int32 序列先解码；纯非负序列原样返回，含负值则逐对要求非负 value 与 -1，之后才投影。不再用 Where 丢掉任意负数。源码 remarks 区分真实经验表与金币表。10项直接案例覆盖两表的两种合法形状、-2、残缺、移位，错误断言包含 path/field。 |
| R3：Town区域丢失/缺头/跨节拼行 | ReadTown 拒绝未闭合重开、孤立close、EOF未闭合；AreaDraft检查 ID/map 头顺序和完整性、minimap位置/次数/四个Int32。Geometry以 segmentStart 在每次换节、close、EOF检查独立段完整行，两个坏半行不能拼接。14项直接案例覆盖坏area、合法permission恢复后quoted父参数、两类geometry的完整重复及半行。未知kind/modifier/permission内容仍保留原专业语义。 |
| R4：Slot/ExtraDrop/ItemUse复合type合法前缀 | Slot与ExtraDrop用 Custom<string> 完整验证首string及可选非负Int32 subtype，Document.AtRoot/TryRead隔离嵌套并拒绝根层重复。Slot坏数据返回不可用null，ExtraDrop抛诊断；ItemUse Custom token列表由helper实际 Decode，坏型进入 unreadable。RootSectionValues保留根层首值记录位置，action helper恢复游标并继续专用参数解析，未将action强行限成type形状。33项生产/接口用例覆盖各入口name-only/subtype/合法嵌套、空/错tail/Single/额外token/负subtype/重复、嵌套-only extra-drop，以及疲劳/混合action游标。 |

Town `[/permission]` 后的 QuotedString 值是父area参数，专业 reader 恢复采集正确；将它们机械视作 Marker 的载荷会误拒合法真实档案。现实现仅对可按普通节判定的 Name/CloseArea执行预验证，permission边界由有序reader承担；合法控制案例验证 minimap/modifier不污染 gate坐标。这是实际格式修正，不是弱化area头或行完整校验。

## 完整迁移与接口一致性

前两轮53主题矩阵仍适用于当前结果，R1–R4涉及的 SkillPoint、QuestParameter、Town、EquipmentSlot、ExtraDrop、ItemUse 本轮已重新核对，相关 StackableAction/helper 同步检查。其余初轮明确发现已有对应逐声明/完整专业形状校验和回归，未退回仅 Offset/Name 装饰声明。

F3的四个真实形状修正保留：Difficulty letter/int映射、Reinforcement numeric权重、Quest expertjob-level整数序列、ignore-quest-level-4-exp Int32 flag；Quest target-character操作数和status三元组亦保留。F4仍由通用 OpenScript 在全部script stream入口拒绝非5字节对齐记录，Document亦保持对应检查。

合法重复/嵌套按原主题语义处理：minimum-level全验证后last-wins、monster合法空item前缀加单非空表、Cube多ID、Dungeon多候选、按作用域的重复块和未知字段均未强制全脚本唯一。业务DTO、规则与effect结算未改；真实回归支持已有输出兼容，不升级为 ClientVerified。

Dfo.Pvf.Schema仍只有通用名称、token kind、类型/decoder、可选性和结构机制，无游戏依赖；静态声明不持有archive或Offset，Resolve按传入archive绑定。公有 GetTag只接收 PvfField，string查找在内部；不同偏移档案共用声明、scalar/list/finite/optional/embedded-marker以及直接stream截断的核心测试守住接口边界。Tokens明确由专业consumer校验，不声称仅声明Tokens就完成形状检查。

Tool固定name使用 ProbeFields.Name；values/where运行时外部字段、合法枚举值、未知scope配对不属于残留固定字段。独立字段扫描 PASS，检查1020个生产/工具源；git diff --check通过。覆盖文档记录53主题/474具名声明及字段族/动态边界，生成脚本与源码声明形式一致；扫描只保护固定语法，完整性结论来自实际源与回归审查。

## 已有验证与最终交付边界

独立读取落盘TRX：核心 `reworked-core.trx` 为80 passed/0 failed；真实 Infrastructure `review-round-2-rework-compatible.trx` 为718 passed/0 failed/2 notExecuted。新增三组实际TRX结果分别11、33、24，全部 Passed，共68项。两项未执行仍是有真实档案时不适用的既有“缺档案失败”测试。前次 `review-round-2-rework.trx` 实际707 passed/11 failed/2 notExecuted，implementation.md如实保留permission错误归属及修正历史；没有把它当成功证据。

implementation.md、usage-examples.md与当前源码及兼容取舍一致；content-migration.md明确旧阶段“仍需完成”属于历史记录，当前状态以最终说明和证据为准。run-final-checks.ps1 的worktree定位、串行 Release solution build、8项目测试、Probe build、退出码记录和失败即停接口正确。

本报告批准当前冻结结果的完整代码/交付接口审查，允许主任务进入最终验证阶段。最终 Release全套尚未在本报告中建立通过证据；spec-result、completion-evidence及干净本地结果提交仍由控制器完成。任何随后业务/测试改动需按实际影响重新检查，不能沿用本指纹批准不同源码。
