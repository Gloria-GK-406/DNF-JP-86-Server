# PVF 内容迁移实现说明

本阶段负责游戏内容层及固定探针字段迁移。生产读取按主题内静态 `PvfField<T>` 声明绑定档案；偏移仍由每个档案的 TagSet/Resolve 获取，未放进跨档案静态共享对象。完整字段表见 [content-coverage.md](content-coverage.md)。

## 类型及读取接口

- 整数、数值、严格 Single、文本和纯列表使用具名类型化声明。OneInt / Integer / Number / Field / First 等整数读取接收 `PvfField<int>`，OneFloat 接收 `PvfField<double>`；技能整数表接收 `PvfField<IReadOnlyList<int>>`，标量技能门接收 `PvfField<int>`，字符串读取接收 `PvfField<string>`。最简单的读取实际调用 `Decode` 或 `PvfDocument.TryRead`。
- 装备必需 minimum level / grade / rarity / equipment type 保持必需；前三个声明包含非负范围，rarity 上限 6；可选 durability 包含 UInt16 范围。原有 item ID、文件、缺失、重复、类型、范围诊断保留。
- `[stat change duration]` 是 Int32 + 可选 target string，使用 `Custom<int>` 明确完整形状。真实 7377 的目标为 `myself`，返回值仍为 7200000。Integer scalar 使用完整节解码，不跳过坏类型；损坏进入原有 unreadable 机制。`[exp bonus rate]` 仍只接受 Single 且严格正、有限。
- 同名 `[grade]` 在任务是文本、装备是整数；`[drop prob]` 在 monster/drop-rate 是纯整数 bands，在 clear-reward 是字符串命名 profile + integers。两主题分别声明，不共享错误形状。
- `stackable type` / `equipment type` / `action type`、日期的 Int32 或 string 变体、magic seal、混合 shop/package/quest rewards 使用 Tokens 并继续专用形状校验；没有改变业务 DTO、规则或 effect 结算。

## 固定投影补齐

CharacterStat / ExperienceParameter / InventorySortKey / EquipmentDurability / DungeonExperience 现均让固定字段的完整节经过对应 typed Decode 或 document.TryRead。角色属性按 initial、growtype、awakening 当前块校验，同块重复拒绝而跨块同名保留；经验列表逐节校验后保留追加顺序。Inventory 与 Durability 区分缺失默认值和存在但损坏：后者抛含字段/路径诊断，不再跳过坏类型或夹取值。

honor `[grade]` 实际包含 Int32/string/string/Int32/string/Int32 资源头及 level/experience 整数配对，已用 Custom<IReadOnlyList<int>> 全形状校验后保留原 integer 投影和经验累加。Inventory stackable type 用 Custom<string> 校验首字符串与可选整数操作数后返回首字符串；这些混合结构没有伪装成普通 scalar。未知字段保持原处理。

新增 `PvfTypedProjectionTests` 22个合成用例；此次补齐未运行dotnet，由父工作流统一验证。下方473/2skip证据是补齐之前的内容阶段结果，不能作为补齐代码的新鲜验证。

## 重复、嵌套及流式入口

NPC shop / category / booster、premium type、quest、magic seal 等仍保留原有有序流与作用域解析。没有把整个脚本无条件送进唯一字段读取器而误拒合法重复。未知节继续由原有 scanner 跳过或由既有拒绝策略处理。

Town minimap 标记和 DungeonRoomDrop hellparty 内嵌块标记是 QuotedString 载荷中的结构词，专业解析继续检查它们的现有 token kind；具名声明统一其固定名称。普通 opening/closing Identifier 使用声明身份。编号 growtype / awakening 和 package data 前缀使用 `PvfFieldFamily`；通用 `[/` 作用域关闭算法按未知字段配对保留。

无标签的 .lst、角色/宠物 ExpTable、按位置的本地化 price triple 仍由具名专业序列解析器处理，固定形状、行宽和取值位置登记在覆盖清单。GoldCarryLimit 实際含固定 `[gold limit from level]`，现按该字段 Int32List 读取；后续级别配对规则保留。

## 工具及动态边界

PvfProbe 的固定 `[name]` 查询使用独立 `ProbeFields.Name`，仍仅依赖 Dfo.Pvf。`values` / `where` 的 args[2] 是使用者运行时字段查询，保留动态入口。ArchiveJobFamilyTokens / EquipmentSlots / Package 类型、任务等级及奖励种类、NPC role、掉落 kind/mask 都是值枚举，不包装为字段。

## 验证

真实档案：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，只读。

1. `dotnet build Server/src/Dfo.Server.Infrastructure/Dfo.Server.Infrastructure.csproj --no-restore`：0 warning / 0 error。
2. `dotnet build Tool/PvfProbe/PvfProbe.csproj --no-restore`：0 warning / 0 error。
3. `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`：473 passed / 2 skipped / 0 failed，约18秒。两项原有 skip 为缺档案行为测试在当前有真实档案时不适用。原始成功输出见 content-test-output.log。
4. 固定 GetTag string / section.Name literal comparison / section case literal / string fixed arrays / sections-string helper scan：只有非PVF的 ChannelScriptExperienceSource 的4个频道文本 case 命中；其余固定入口已经声明。无提交、部署、PVF/客户端/数据库修改。

执行期间的首次严格解码回归暴露两个变化：duration 原来实际混合 Int32 + string（已用 Custom 明确形状）；sibling drop-rate 现在更早在 `[drop prob]` 拒绝不兼容命名 profile（原测试改为断言该精确拒绝）。首次两项失败的原始输出保留在本阶段工具调用转录中，后续成功日志未伪装成首次运行结果。

父工作流仍需完成：公开 GetTag(string) 编译收口、typed ItemUse 合成错误/重复覆盖保护、独立完整审查与完整服务端新鲜验证、最终结果提交。此文档的测试证据是 CurrentImplementation，不是客户端实机验收。

## 最终修订说明

以上阶段日志是历史生产记录，末尾“父工作流仍需完成”只描述当时状态。当前完整补齐、合法真实格式取舍和最终结果以 implementation.md、content-coverage.md、reviews/change-review.md 和 verification/completion-evidence.md 为准；最终生成清单为53主题/476声明。合法混合块的完整校验由专业解析器执行，未将 token 列表当作自动校验完成。
