# 2026-09-30 实机强化字段错误

> 后续状态：用户明确同意后，UID689精确恢复已经执行且独立核对通过，见[恢复结果](asset-recovery.md)。以下提案和“未应用”描述保留诊断时的历史状态。

## 用户实际结果

用户反馈：强化成功，但装备信息未更新，重新登录角色也看不到强化等级。本轮R-01/R-02不通过，最终验收门保持未通过。

18:28:20服务日志：character2、seq80、absolute slot9，old0/new1/outcome Succeeded；随后CMD0050与3条NOTI000E。实际目标为UID689、模板101040127，而非早先建议的UID339。实际PVF名称为「传承 : 冥域魔神的逆光长剑」。UID339仍未强化。

SQLite实际目标已持久化：core82字节中byte17=1、byte9=0，updated_at对应18:28:20（数据库UTC10:28:20）。当前无色9796；金币89516，包含用户此前搬装备/修理等实际操作，不用早先皮靴450费用机械解释这件剑的资产变化。

## 根因与证据边界

旧实例SetReinforcementLevel及强化/修理/异次元读者使用EnchantUpgradeCount（core17），旧业务测试同样以此为oracle。该字段实为独立附魔升级次数，普通强化应为Attribute(core9)低5位，高3位是密封次数。

- ItemCore原始对照：`E:/Project/86JP/Server/DfoServer/Game/Inventory/Models/ItemCore.cs:183-186`，Upgrade读Attr&0x1F，写保留Attr&0xE0；与仓库`Docs/GM工具_新背包表结构与ItemCore语义.md:458/462/517`一致（UpstreamReference）。
- 当前外观投影`Application/Characters/DrawnAppearance.cs`已按Attribute&0x1F构建装备状态，附魔次数字段另作Flag20（CurrentImplementation，与上述独立字段语义一致）。
- `protocol/game/server-to-client/NOTI-0x000D.md`客户端静态entry偏移表：+0A为ext_data1，+12为enchantUpgrade；共用inventory writer把Attribute写+0A、EnchantUpgradeCount写+12，0D/0E布局无须改变（ClientStatic布局+CurrentImplementation语义映射）。
- 实际数据库与已发送通知支持“已写错误字段”，不支持“没有落库”或“没有发送库存NOTI”。客户端正确等级显示仍需修复版本实机重验；不因这些代码/日志结论提前标ClientVerified。

最小修复：只读ReinforcementLevel语义，实例仅设置Attribute低5位，普通强化读取/修理倍率/异次元等级准入也使用它；独立附魔次数和高3位保持。没有添加未知通知、改变schema/wire布局或新玩法。

新增测试通过真实CommandBus/SQLite及注册库存encoder读取明确字节，而非getter断言。首次有效RED core9 expected0xA1/actual0xA0，exit1；高3位0xA0且独立附魔次数非零的fixture使测试能区分三个字段。具体GREEN与后续验证见TASK-004结果。

## 已付费UID689的精确修复提案（未应用）

原部署前备份中UID689附魔次数为0，当前错误强化写为1。当前受影响装备的在线备份：主仓库`artifacts/equipment-reinforcement-field-fix-20260930/before-field-fix.db`，quick_check=ok。

提案在当前core上仅做：

| 持久化偏移 | 当前 | 目标 | 原因 |
|---|---|---|---|
| 9 Attribute | 0x00 | 0x01 | 将本次成功的+1写入普通强化低5位，保留高3位 |
| 17 EnchantUpgradeCount | 0x01 | 0x00 | 恢复原备份中独立附魔次数0，消除本次错误写入 |

其余80字节、UID/模板/当前槽/耐久48、金币89516/无色9796保持；不再次扣金币或材料，不返还或猜测其他用户操作的费用，不对全库自动转换。

可复核工件：同目录`recovery-proposal.json`完整before/after82字节；`apply-approved-recovery.py`默认只写隔离dry-run，已确认changed_offsets=[9,17]且资产不变。生产应用要求用户明确同意、角色离线、UID689仍属于Character2、完整core与提案相符；任何条件变化均停止并重新检查。当前未用--apply，生产仍是错误字段状态。

用户已明确“已正常退出游戏”，可以进行服务切换；该反馈不等于授权资产修复。待代码、自动/独立复核、新版本ready后再请求此精确提案的确认。
