---
kind: rks-review
verdict: approved
---
# PVF 全量迁移第四轮独立复审

## 目标与判定

`approved`。本轮真实格式兼容修正和直接回归审查通过，未发现新的具体阻塞缺陷。批准仅适用于以下当前冻结 target；第三轮批准不推广到修正后的源码，首轮最终 Release 失败也不能作为整体通过证据。

目标基线为 `828e769600122f23db2b244e8cfc44ef400d0ffe`，目标为第四轮派发时冻结的完整未提交结果：53内容主题、通用 Schema/PvfArchive、Probe、全部测试与任务文档。依据 TaskContract revision 1、spec.md、direct plan.md、适用 AGENTS.md 和 reviewing-work code/deliverable 准则。前3轮报告保留于 change-review-round-1/2/3.md；复用已检查的未变部分，本轮重新读取 SkillPoint、EntryLifecycle、ItemUse 的实际修正、相关全部新/改测试、文档和直接调用影响。

当前72个修改及非忽略 untracked Server/Tool `.cs`、`.csproj` 的规范化源码指纹为 `b57df3617b80064a915bf10ee15e8ffb2df0400f599bb26c723b10ea78c38003`。算法明确为：相对路径保持Git的POSIX分隔符，Python `sorted()` 字符顺序排序，每行 `path|hashlib.sha256(file bytes).hexdigest()`，以LF连接并加末尾LF，UTF-8编码后SHA256。排除文档与验证产物。第三轮PowerShell文化排序指纹与其Python顺序指纹差异仅为4个测试文件的顺序，已独立确认，不能混用未注明排序的摘要。

审查只读源码、既有Probe、TRX与扫描，只更新本报告；未运行dotnet、改产品、提交或部署。

## 实际格式修正与关闭证据

### Fixed-level union与嵌套

SkillPoint.FixedLevelSkillField 改为 Custom<int>：一个Int32保留legacy base，空opening返回真实nested base0，其他载荷拒绝。ReadAllIntSections在原逐occurrence Validate基础上检查空opening/closing配对，拒绝未闭合、再开、嵌套scalar、孤立close或close带值；新增closing/contract具名声明，interval、add-per-interval、contract等每次子声明仍完整验证，再执行旧numeric last-wins。没有通过跳过空opening或过滤坏子字段掩盖失败。

独立只读Probe确认 `skill/Gunner/SatelliteLaser.skl` 第1576–1583个Token为无载荷opening、interval Int32 5、add Int32 1、contract Int32 1、匹配closing。新增14个生产入口合成案例覆盖真实nested、legacy scalar、两个合法闭合块last-wins、坏opening载荷、坏边界及早坏后好子字段。它们守住原R1完整验证要求，同时修正真实合法结构被误拒的问题。

### 重复技能文本

ReadStringSection 改为 Document.ReadAll(tag).FirstOrDefault；ReadAll先完整解码全部occurrence，才取first。独立查看基线源码，旧stream读取确实返回第一个string；Probe确认 `skill/DemonicSwordman/NotApplicable/IceWaveExp.skl` 两个name为不同的QuotedString。此修正恢复合法first-wins，没有取消晚期坏声明检查。新增3个直接案例覆盖合法first输出及早/晚坏型。

### Stackable多整数尾

EntryLifecycle.TypeName与ItemUse.StackableType Custom接受首type string加可选多个非负Int32，逐个检查全部尾token；装备equipment type继续限制可选单subtype。根层作用域、重复拒绝和action专业游标未改变。修正仅扩展明确合法stackable envelope，没有恢复仅取首string的R4漏洞。

独立Probe确认实际商城coupon路径 `stackable/cash/chn_20140311_occupation/chn_stk_avatar_option_discount_50_coupon_2683473.stk` 的载荷为 QuotedString `[coupon]`、Int32 26、Int32 0。4个新案例共同检查placement/use的合法coupon输出，以及末参数string/Single/negative均明确拒绝。Unlimited控制用例现在允许额外完整Int32；equipment额外Int32仍拒绝。原Entry坏tail用例已改为合法整数前缀加坏string，继续测试真正错误结构，未以删除坏数据回归换取通过。

## 完整接口与交付检查

前三轮53主题覆盖、F1–F4/R1–R4关闭证据适用于未变部分；当前修正重新验证SkillPoint所有已知occurrence、未选成本及每节pair的机制仍在，stackable完整kind/range/root/duplicate保护仍在。通用Schema/Archive没有游戏概念或跨档案静态offset，公有GetTag收口、直接stream记录截断、多档案与专业Tokens责任没有回退。合法重复/嵌套和旧业务输出规则得到保留，业务DTO/effect结算未修改。

独立字段扫描为PASS，检查1020个生产/工具源；git diff --check通过。覆盖文档当前为53主题/476具名声明，新增两个SkillPoint字段已登记。内容枚举值、未知scope算法、运行时Probe查询仍属于明确边界。implementation.md如实记录首次最终Release及后续兼容失败；历史474统计明确为历史阶段，当前count已更新。usage及最终串行验证接口无需生产变更。

## 验证证据与完成边界

独立读取实际TRX：

- 当前 `release-shape-final-infrastructure.trx`：739 Passed、0 Failed；UnitTestResult中另有2项既有NotExecuted。该TRX的Counters.notExecuted为0而total/executed为741/739，跳过结论来自实际result entries，不仅依赖Counters。
- 当前 `release-shape-final-application.trx`：2599 Passed、0 Failed、0 skipped。新增SkillPoint28项与TypeTail37项在Infrastructure TRX中均Passed。
- 当前兼容自检是Debug结果，核心未变部分既有80/0证据仍有效于局部审查；不能将这些结果称为审查后的完整Release全套。
- `attempt-1-release` 保留8个首轮最终尝试文件。该轮Application2586/13失败、后续shape Application2597/2失败，以及Infrastructure735/1失败均有保留记录，不将历史失败覆盖成成功。上轮局部Core/Infrastructure成功未证明完整Application兼容，当前修正和直接回归已弥补其证据缺口。

本报告批准当前冻结代码/交付接口审查，允许控制器按AC-006对该target重新执行最终串行10 checks：Release solution build、8测试项目、Probe build。完整新鲜通过、completion-evidence/spec-result、干净本地提交仍是主任务未完成工作。本报告不建立整体完成、最终验证通过或ClientVerified结论。随后若源码/测试变更，不得沿用本指纹批准另一target。
