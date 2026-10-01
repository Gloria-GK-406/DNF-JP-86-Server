# 强化失败结果服务 — 70参考与A21内容边界

本记录是静态证据（70Reference / Confirmed），不是A21实机验收。参考ELF `Research/1995chen-dnf/other/等级补丁/70/df_game_r` SHA256 `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7`。

原始反汇编与常数读回保存在 `artifacts/equipment-reinforcement-failure-20260930/reference-base.txt`、`reference-penalty.txt`、`reference-identity.json`。

- `_ProcPenalty` 0x08546bba：085470d9保存原强化等级，085470f2清零；0854714f调用 `DisJoint::GetResult`；085471ab–08547217收集正数量基础产物；0854723b按保存的原等级追加强化产物；085472d3批量放入背包。保护分支提前返回，不产生损毁材料。这是直接入包，不是地面掉落。
- `DisJoint::GetResult` 0x084733f6：价值乘float32常数1.100000023841858后截断，按卖价率换算，整数除法 `(sellPrice*1000)/cubeCreationConst` 后乘品质系数再除1000，截断并至少1个基础无色。该入口只调用基础品质附加，未调用NPC扩展奖励计算。
- 虚调用slot0x4c已从 `CEquipItem` vtable 0x08c96e20读回：目标08514e5e `IsRandomOption()`，读静态byte+0x1c8。`getItemSellPrice` 0898c86e按该标志选择价格表第1或第3卖价率；A21价格表分别200与30。随机属性装备不能一律按普通卖价率计算。
- `_GetAdditionalItem` 0x084732b4：用品质选择候选，均匀候选抽样；大成功概率是draw10000 < percent*100；数量是装备grade除所选normal/great除数后先存float32，再截断，至少1（08473367/084733be的fstp dword确认中间量化）。不用minimum level，不补随机小数余数。不能用未量化double除法代替float临时值的整数边界。
- `_getAdditionalDisjointItem` 0x08546938：修正grade满足最低门，且原等级大于奖励基准时，数量为 `(原等级-基准)^2 * 品质权重` 截断。平方常数来自0x08c9d308的double2.0。目标等级不参与该数量。

A21当前 `etc/upgrade.etc`：追加物品3243，grade门40，等级基准10；稀有权重1。因此稀有+11尝试+12失败损毁，追加痕迹为1。普通/高级没有追加痕迹权重，仍应有基础分解产物。实际数量必须以该装备静态内容和本次奖励抽样为准。

用户观察：普通白装12866（残破的紫榆绑腿）原+11在seq178尝试+12损毁，没有收到基础材料。历史请求不自动补发；新实现以新的操作及重登验收。
