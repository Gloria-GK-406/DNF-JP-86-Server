# TASK-004 — 实机强化等级映射修复

## Task Unit Contract
- Revision: 1
- Outcome and references: 用户R-01/R-02报告成功动画但即时信息/重登无等级。修复SC-3/4/6的实际字段错误，保持既有规则/费用/概率和instance分工。
- Owned boundary: Data.Inventory core只读强化语义、Application实例与实际强化等级读者、相关行为/持久化/wire测试；纠正任务和原装备任务文档错误语义，重新验证/独立复核/备份部署。没有新增wire布局或schema，没有自动修改生产资产。
- Dependencies: TASK-001至003当前产物；用户当前实机反馈、18:28:20真实强化日志、实际SQLite UID689记录、原ItemCore语义和客户端静态entry布局。
- Required checks: 行为RED证明错误字节；GREEN覆盖高3位密封次数和独立附魔次数、成功/失败/保护/销毁、repair/outworld读取及NOTI0E/重登0D；真实PVF与相关全量回归、Release、独立修复复核、服务ready；当前用户最后验收仍必需。

## Supported Diagnosis
- 实际请求character2/seq80/target absolute slot9，服务18:28:20记录old0→new1/Succeeded，发CMD0050和三条NOTI000E。
- UID689/template101040127当前SQLite core[17]=1、core[9]=0，updated_at对应强化时间。材料9806→9796。用户选用自己的仓库装备而非建议UID339；UID339没有强化，不用其记录推断持久化丢失。
- `Docs/GM工具_新背包表结构与ItemCore语义.md`的attr低5位为Upgrade，core[17]为附魔升级次数；旧对照`86JP/.../ItemCore.cs:183-186`一致。当前DrawnAppearance.cs也按Attribute&0x1F投影强化视觉；客户端静态0D entry字段+0x0A=ext_data1、+0x12=enchantUpgrade。存储writer与wire writer均保留这两个独立字段。
- 当前实现/旧测试/装备任务文档错误把EnchantUpgradeCount称作强化；NOTI已发、实际存储已写错误字段。没有证据要求再加一条未知NOTI。

## Detailed Plan
1. 先加有独立wire/存储oracle的测试：真实CommandBus+SQLite强化后core byte9低5位应增加、byte17附魔次数保持；即时NOTI0E与重读后的NOTI0D entry+0A相同，高3位密封计数保持。固定抽样，记录当前实现下的预期行为失败（不是编译RED）。
2. 在InventoryItemCoreState提供只读ReinforcementLevel=(Attribute&0x1F)，SetReinforcementLevel仅改Attribute低5位（保留高3位），合法level0..31，保留EnchantUpgradeCount。所有真正读取普通强化的rule/domain（强化/修理/异次元）使用该语义。附魔珠子拒绝升级的字段仍是EnchantUpgradeCount；不扩张升级珠子功能或整库迁移。
3. 对现有测试逐项按语义更新：强化fixture/expectation改Attr或ReinforcementLevel，附魔测试独立检验普通强化与附魔次数；保留所有错误/资产/事务检查。不能对所有EnchantUpgradeCount机械全局替换（codec、bead、appearance独立字段仍正确）。
4. GREEN及相关回归；根因文档明确改正早先错误，原测试证据和review保持历史范围。最终重新独立审查修复及直接回归，不冒认先前4510自动结果已证明客户端等级正确。
5. 原库另做当前状态在线备份，发布新的修复runtime并记录新source指纹；生产数据保持，准备UID689精确受影响字段的修复提案（byte9低位0→1、byte17恢复0），附before/after和无追加扣费说明。必须用户明确同意后才应用生产数据，不能自动全库转换。
6. 新版本ready后重新进入manual_required。经用户同意可恢复已付费UID689并核对登录/重登结果；修复版R-01仍用未变UID339执行一次正常强化，R-02正常退出重登。恢复已付费结果不替代新强化请求验收；最终结构与实机反馈门不变，不提前commit。

## Task Unit Result
- 已改正普通强化字段语义，全部对应读者、基础setter及原业务fixtures修复；独立附魔次数/密封次数受到明确原始byte oracle保护。
- 行为RED1fail（0xA1预期/0xA0实际）→GREEN1pass；装备跨程序集319pass；各程序集最终4515pass/0fail/2skip，包含一次文字golden适配后完整Persistence385pass。Releasebuild0警告/错误，实际模块+PVF+当前生产副本probe通过。详见[当前验证](../verification/field-fix-current.md)。
- PID41000新Host ready，备份和精确恢复提案/副本dry-run齐备，生产未造样本/恢复资产。代码/部署产出可进入独立review；已付费UID689恢复需要用户明确确认，SC6最终重验仍待验，不把纯等待登记todo、不提交。
