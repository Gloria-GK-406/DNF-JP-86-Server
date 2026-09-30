# 异界气息与异次元属性：证据及方案

## 证据边界

| 来源 | 已核对事实 | 限制 |
|---|---|---|
| `70Reference` | `Research/1995chen-dnf/other/等级补丁/70/df_game_r` 的 `Dispatcher_PurifyItem::read` `0x08218b94` 顺次读 `short/item_idx/short/item_idx`；`Dispatcher_InvestItemAmplifyOption::dispatch_sig` `0x082194f4` 读动作及装备/材料字段。 | 70 版不能证明 A21 请求尾部、材料或响应。 |
| `UpstreamReference` | `ServerS4A21/Server/DfoServer/Network/Parsers/Inventory/{PurifyItemRequestParser,InvestItemAmplifyOptionRequestParser}.cs` 把前 12/14 字节解释为目标槽/模板与材料槽/模板、操作和选项。`Game/Inventory/Services/InventoryEquipmentMutationService.cs` 以 `AmplifyType & 0x80` 识别未鉴定，净化随机赋予四属性、清除置零；赋予/扭转按 `amplifyitem.etc` 材料表校验并拒绝已有升级。 | 上游错误码/ACK 和随机权重仍需本 A21 实帧校准；其在线状态不是本服持久化证明。 |
| `ClientStatic` | 解包 A21 `DNF.unpacked-b1-debug.exe` VA `0x016FBCE6` 的 `0x00CC` 构包路径到 `0x016FBD36` 依次写两个短数与两个物品索引，固定 12B。`0x00CD` VA `0x016EB8DB–0x016EB93D` 写 `u8,u16,u32,u16,u32,u8`，action `2` 才经 VA `0x016EB946–0x016EB9C9` 的虚调用追加动态字符串，action `0/1` 固定 14B。客户端 `0x00CC` 响应 handler RVA `0x00D274E0`、`0x00CD` RVA `0x00D3EAD0` 先检查成功标志，成功分支读取材料/目标及属性字段，`0x00CD` 失败读错误字节。 | 无实际 C2S/S2C 帧、GUI 验收；`0x00CD` action2 动态尾内容/结果未闭合，明确拒绝。 |
| `ContentStatic` | A21 `Script.pvf` 的 `etc/amplifyitem.etc`：`[equip level const] 55`；`[purify material] 1183 1`；`[purify only material] 10000408 1`；`[invest option]` 的 1184 为 physical attack/力量，`[reinvest option]` 的 1188 同类、1284 为 all；四个 `[option data]` 项权重各 2500、基值各 5，稀有度权重按表。装备 `101010339` 可用（Lv75、稀有度 2）。 | 内容表不是 UI 可操作性或掉落生成证明；尚无自然产生 `0x80` 污染装备的本服路径。 |
| `CurrentImplementation` | PVF 内容源决定材料数量、属性与初值，规则服务执行状态转换；选中角色/当前槽绑定、装备实例与材料经同一命令事务结算，保持装备 UID 和其他字段；两条入口有 ACK 及权威两槽刷新。普通强化已有非零 `AmplifyType` 拒绝。 | 本服行为不等同 A21 客户端效果；实际增幅升级仍未实现，action2 安全拒绝。 |

## 可实现边界

`0x00CC` 仅允许未鉴定 `AmplifyType=0x80` 的合格装备；净化材料按 PVF 四属性权重随机确定类型，清除材料置类型/数值为 0。`0x00CD` action0 是从无属性、无普通升级装备赋予属性；action1 对已有 1–4 属性且无升级装备扭转，不能选择与原类型相同属性。PVF 特定属性材料覆盖客户端所选项；`[all]` 材料要求所选项 1–4。等级低于 55、稀有度低于 2、头衔、错误身份/槽/模板、锁定/过期、材料不符均拒绝。规则服务只读内容源，不信任请求中的属性数值。

所有结算由账户/在线用户独占事务串接：先校验装备与材料、规则/数量，再消费材料并写同一装备 core，提交后 ACK 和刷新；同连接同序列重放不再次消费，持久化异常回滚。`0x00CD` action2 未接资产路径；净化之外的 `[gold purify material]` 暂隔离，不能按名字猜成普通净化。

## 待集中实机验证

请求尾部、S2C 额外字段、客户端 GUI 状态、提示文案与重登一致性均待 A21 抓帧。污染装备的自然掉落来源仍未闭合；人测若无现成装备，需使用隔离测试数据库准备 `AmplifyType=0x80` 样本，不能在用户真实存档上直接改表。
