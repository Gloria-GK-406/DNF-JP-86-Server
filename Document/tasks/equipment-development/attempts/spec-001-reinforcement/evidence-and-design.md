# 普通强化：证据对照与实施设计

## 样本身份和证据等级

| 样本 | SHA-256 | 用途 |
|---|---|---|
| `Research/1995chen-dnf/other/等级补丁/70/df_game_r` | `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` | 70Reference，ELF32/i386，有符号表；地址为 ELF VA |
| `DNFClient-A21-debug/DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` | A21 客户端身份 |
| `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe` | `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A` | ClientStatic 反汇编载体；下文 PE VA = RVA + `0x00400000` |
| `DNFClient-A21-debug/Script.pvf` | `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41` | A21 本地内容数据，非运行帧 |

工具：`llvm-nm -C -S`、`llvm-objdump -d --demangle --start-address ... --stop-address ...` 和仓库 `Tool/PvfProbe`（设置 `PVF_ARCHIVE_PATH`）。本项没有普通强化实帧或玩家 UI 观察，故无 `WireCaptured` / `ClientVerified` 结论。

## 四源对照

| 决策点 | 70Reference | UpstreamReference | ClientStatic / A21 PVF | 本服决策 |
|---|---|---|---|---|
| 入口及请求 | `Dispatcher_UpgradeItem::dispatch_sig` `0x08200789–0x082009c3` 读取 `u8,u16,u32,u16,u16,u32,string`，70 命令 `0x53` | `Network/Parsers/Inventory/ItemUpgradeRequest.cs` 从 A21 body 读 `u16 method @0`, `i16 targetSlot @2`, `i32 targetId @4`, `i16 materialSlot @8`, `i16 ticketSlot @10`, `i32 nameLen @12` 与 UTF-8 名称 | `protocol/game/client-to-server/0x0050.md` 构包 RVA `0x012FD5E9`、`0x012FD947`，CMD type `0x0050`；字段静态下界相符，但虚调用分支未闭合 | 精确解包，限长度；客户端名称不作为物品权威。不能照搬 70 的 opcode。 |
| 准入 | `_CheckCondition` `0x08546342–0x08546780` 要求已识别、合法装备类型/等级、满耐久、足够材料和金币，并检查破坏产物空位；`ProcUpgrade` `0x085477b6–0x0854781a` 校验槽位与模板，`0x0854803f–0x085480b6` 拒绝普通强化与增幅状态不符 | `Game/Inventory/Services/InventoryItemUpgradeService.cs:29–153` 同类资格、锁定、耐久、增幅门 | A21 请求提供目标槽与模板，不含可信的资产状态 | 从当前角色实例与 PVF 元数据重新核验，普通强化 mode 只接受 0；禁止未净化/已增幅装备进入本路径。 |
| 表与材料 | `upgrade_table_t::importUpgradeTable` `0x08900c26–0x08900f6d` 从 `[table]` 建运行时规则；活动还可改费、材料量 | `Tool/PvfLib/Models/UpgradeTableFile.cs:419–438` 把每行 19 数值解释为成长、失败权重、处罚、材料 ID/数量；`ItemUpgradeTableProvider` 计算费用 | A21 `etc/upgrade.etc` 的 `normal` 首行 19 数值，索引 7 = 0、8 = 1、9 = 0、10 = 3037、11 = 10；第 5 行目标 +5 的索引 7 = 10000、8 = 2、9 = 1、10 = 3037、11 = 50。还含 `[cost]`、`[cost weights by rarity]`、`[type]`、`[cost weight by upgrade level]` 等 | 从真实 A21 PVF 读取，缺表/越界时拒绝；不硬编码上游表值。19 列解释属 UpstreamReference + PVF 数据联合推断，需要测试及实机验收。 |
| 随机与惩罚 | `_DoProcUpgrade` `0x08547564–0x08547791` 用 `upgrade_info_t+0x20` 作失败阈值，活动/装备可调整，`randInt(100000)` 抽样，低于阈值失败，否则 `IncUpgrade`；`_ProcPenalty` `0x08546bba–0x08547559` 类型 1 保持、2 降级、3 破坏；`GetPenaltyType` `0x089003ee–0x08900435` 有稀有度破坏阈值 | `InventoryItemUpgradeService.cs:156–195,610–698` 按 `100000` 权重抽样、保护券、处罚；`ItemUpgradeTableProvider.cs:76–87` 的破坏阈值处理 | A21 PVF 普通表含这些权重/处罚字段；`[destroy level by rarity]` 的 `common`、`uncommon` 为 10；`[max upgrade level by rarity]` 两者为 50 | T-002 必须把随机源注入并测试边界；不能在未确定产物容量与保护券语义时执行销毁。70 活动与装备概率增益不默认启用。 |
| 保护与扣费 | `ProcUpgrade` `0x08547a49–0x08547c7d` 解析辅助槽；`0x08548198` 先定成败，`0x08548318–0x085485ce` 扣金、辅助券和材料；自动保护券分支在 `_ProcPenalty` `0x08546dc4–0x0854707f` | `InventoryItemUpgradeService.cs:176–205,564–608` 扫描强化保护道具；`InventoryItemUpgradeCommitService.cs` 提交库存事务 | A21 请求含辅助槽。真实 A21 PVF 的 `stackable/event/common/event_7804.stk` 声明 `[action type] [protect equipment]`，解释为破损时归零；`stackable/cash/chn_20150507_New_Strengthen/chn_upgrade_care_10007840.stk` 声明 `[new protect equipment] 10`，解释为 +11 以上失败退到 +10 且优先于普通券。无实帧。 | 只用 A21 PVF 标签/参数识别保护券，区分归零与保留 +10；不凭任意物品 ID 扣券。结算整体在一个服务端命令事务中。 |
| 回包与刷新 | 70 `IItemUpgrade::item_upgrade_system` `0x080fc86a–0x080fc8f4` 成功发 `0x53`，错误发双字节 error；普通结果后更新库存；公告用 `0x56` 广播 | `Network/Builders/Inventory/ItemUpgradeAckBuilder.cs` 给 A21 `0x0050` 的字段顺序；`InventoryHandler.Equipment.cs` 先 ACK 再物品、金币刷新 | A21 `0x0050` handler RVA `0x00D3D950`（PE VA `0x0113D950`）在 `0x0113DA21–0x0113DABB` 依次读 `u8,u16,u32,u16,u8,u8,u8,u8,u8,u16,u16`；读取原语 VA `0x027C5230/52A0/5310` 分别是 1/2/4 字节。结果码等于 3 的分支 `0x0113E24F–0x0113E304` 额外读 `u8 count`，每条 `u16 slot,u32 itemId,u32 count`。CMD 通用双字节错误信封见 `protocol/patterns/game-receive.md` | 成功结果字段长度/顺序按 ClientStatic 与上游共同约束；破坏结果必须带产物列表计数，即使 0 件。ACK 之后再投递已提交的库存和金币视图；错误只发 `0,reason`，原因值需核对客户端接受表。 |

### 客户端成功结果 body（待实帧确认的字段语义）

CMD 通用成功标记 `u8 1` 由分发表处理。handler 随后固定读取：`u8 method; u16 materialSlot; u32 materialRemaining; u16 optionalSlot; u8 reserved; u8 oldLevel; u8 resultCode; u8 newLevel; u8 reserved; u16 targetSlot; u16 optionalSlot`。宽度与顺序是 ClientStatic；字段名称主要来自上游 ACK builder，故标为 High 而非直接客户端证实。仅 `resultCode == 3` 的分支追加产物 `u8 count + count × (u16 slot,u32 itemId,u32 count)`。`resultCode == 1/2` 不读取此数组。70 的 `0x53` 布局不能替代它。

### 内容数据的限定

`etc/upgrade.etc` token 1–4849 是 `normal` 表，第 5 行已显示失败权重及降级处罚，token 45022 起有公告等级 12；token 45299–45310 有破坏及最高等级的稀有度映射；token 45311 起是破坏产物配置，bonus item `3243`、最低装备等级常量 40、按稀有度的权重与等级校正。这些是 PVF 原值；费用的完整计算和多个表类型的选择仍取决于参考端算法，不是直接客户端行为。应对真实 PVF 解析做格式断言和样本测试，避免把浮点列错位。

## 状态与事务设计

复审后补充两条边界：A21 真实 PVF 的 `normal` 表有目标 +20 行，但 `[cost weight by upgrade level]` 没有当前 +19 项；上游 `ItemUpgradeTableProvider.GetUpgradeLevelWeight` 对缺项返回中性系数 `1`，本服采用相同回退并以真实 PVF 表断言及领域结算测试覆盖。账号虚拟材料槽须与本次 PVF 行所需材料 ID 匹配；物理槽请求只消费指定的物理堆叠，不能暗中切到账户余额。`0x0050` 同连接序列缓存还绑定选中角色会话与完整请求指纹；同序列不同请求不能回放旧成功结果，切角色会清除旧会话缓存。以上属于 UpstreamReference + CurrentImplementation 联合判断，仍待实帧验证。

目标由选中角色的背包槽位与请求模板双重定位。装备持久化的普通强化等级是 `InventoryItemCoreState.EnchantUpgradeCount`；更新时复制原实例并只改变该字段，不重建或清空 `AmplifyType/Value`、封印与随机词条。角色金币、材料/辅助券、目标装备、可选破坏产物需在同一提交边界验证与写入。先完成资格、容量、费用与结果规划，再提交；提交失败无 ACK 成功、无资产变化。重发相同序列或并发双提交不得二次消费同一材料/目标。出站顺序以一次已提交结果为准：CMD `0x0050` ACK → 受影响装备/材料槽刷新 → 金币刷新 → 如需公告才发对应通知；任何投递失败不回滚已提交资产，应可经重载/刷新恢复。

## 未知和执行停门

1. **普通强化破坏**：70 会调用 `DisJoint::GetResult`，上游用 PVF `[disjoint]` 估算产物；当前没有 A21 实帧。T-002 若无法证明背包容量与产物规则，破坏阈值请求应显式拒绝而非静默改成保留/降级，更不能销毁后不给产物。
2. **保护券**：A21 PVF 已证实普通与超级保护券脚本语义；70 可自动搜索四个 ID，但 A21 的自动选券顺序、辅助槽是否参与、过期道具是否可用仍需客户端/实帧验证。未识别的保护券不得被当成通用护符。
3. **费用/权重效果**：活动、炼金和装备附加概率在 70 中存在，但 A21 当前效果来源和数据路径未证。MVP 以 PVF 静态表为基线，支持后续效果注入，不凭空给概率加成。
4. **错误 reason**：A21 通用错误信封明确。静态 handler VA `0x0113E56C–0x0113E59F` 以 `reason-4` 跳转，索引表 VA `0x0113E9E0`、目标表 VA `0x0113E980`，可定位非默认处理值：`04,07,08,09,0A,0D,0E,11,13,15,16,17,5F,67,68,76,7E,AF,B3,CE,D6,D9,EB,F1`（十六进制）。这只证明 handler 有分支，不证明每种含义，也不保证 `0x026181C0` 的前置错误处理不会截获。T-003 必须从该集合选择并核对 UI 语义，不能照抄上游任意枚举。
5. **实机**：无 `0x0050` 实帧、无强化 UI 观察。自动测试只能证明 CurrentImplementation 与 ClientStatic 的吻合；Human Test List 和用户验收独立进行。

## T-002 / T-003 输入

- T-002：实现 A21 PVF 普通强化表与相关材料/费用/产物读取，领域结算与随机源、并发/事务测试。先做无破坏路径，破坏/保护需过上述停门。
- T-003：精确入站解析和成功/错误 ACK，受影响物品与金币刷新；用 PE VA `0x0113D950` 的读取顺序校验编码，并列 Human Test List。错误 reason 先完成客户端接受表调查。
