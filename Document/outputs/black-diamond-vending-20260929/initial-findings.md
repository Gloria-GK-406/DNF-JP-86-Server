# 黑钻售货机：70端初步取证与A21采样准备

日期：2026-09-29。仅70Reference及当前PVF静态数据；未实现抽奖，未把70响应格式当作A21事实。

## 测试准备

1123（角色2，账号4）的普通钻石硬币7454增加100枚：原1，现101。沿用材料槽130、原物品822，同时更新item_slots.count和82B item_core中偏移5的数量。停服后备份数据库再事务修改，quick_check=ok。恢复原运行版本，HTTP ready。备份位于 artifacts/black-diamond-icon-20260929/before-vending-coins.db。

## 当前PVF

普通NPC80（npc/vendingmachine_pc.npc）与高级NPC112（npc/chn_high_class_vendingmachine.npc）同时摆在map/town/underfoot/underfoot_event.map，分别x2825/y189和x1219/y189。普通机在凯丽x2581与卡妮娜x2937之间。

etc/vendingmachine.lst以1映射pcroom.vm；该文件group1的material为7454×1，output表存在。此处仅确认数据，不将NPC编号与机器表编号混用。

## 70Reference

样本SHA256：cf45f4e8514c3bf560dd322c146f8441249f6c28246ec8566dcf9ebc4e1b2eb7

线性反汇编与调用符号见tw70.asm.txt。

- Dispatcher_UseVendingMachine::dispatch_sig，VA0821C2E6，长度4061：先要求CUser state==3并拒绝交易中状态。
- VA0821C360/0821C3A4/0821C3E8顺次读取u32/u32/i16，共10B。第一个交给_getVendingMachine、第二个交给_getVendingMachineInfo；第三个作为背包扣料槽位（0821CCA2→0821CCD7）。因此70语义为机器表ID、组ID、材料槽。
- _getGiveItem VA0821BF82：从组+0xC取随机范围上界并减1，randInt后按行+4阈值严格大于随机数选中，返回行+0物品及+8数量。这符合累计区间采样结构；组+0xC及行+4如何由PVF导入生成尚未跟完，不能宣称分母固定。
- 主流程有背包容量检查、材料类型/数量检查、delete_item、_putItemIntoUser、可选_getBonusItem、_notifyItemInfo路径。抽取重试/附赠/特殊奖励的全部政策尚未收敛。
- VA0821CFF5..0821D00B构造CMD 0xDA成功响应，写成功字节、请求材料槽、后续结果及背包条目。70 opcode是0xDA，不能照搬到A21。

## A21对照与待采样

现有protocol/game/client-to-server/0x00D7.md记录USE_VENDING_MACHINE，构包布局同为u32/u32/u16（10B）；仍需本次普通机实帧确认实际type和值。当前没有新增handler。用户点击普通机并尝试抽一次后，从服务日志/客户端帧核对请求；A21响应handler需独立确认后再实现。
