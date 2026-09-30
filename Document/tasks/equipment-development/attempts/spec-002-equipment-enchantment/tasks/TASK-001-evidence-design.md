# TASK-001 — 附魔四源取证与实施设计

## Task Unit Contract

- Revision: 1
- Outcome and references: 建立足以决定装备附魔准入、覆盖、资产结算与客户端结果投递的 70/上游/A21/PVF 证据表和设计；覆盖 Plan T-001、Spec RQ-001/002/004 与 AC-001。
- Owned boundary: 本子项 `Document/` 取证/设计 Markdown、有直接证据的 `protocol/` 人工区；只读 70 ELF、上游、A21 客户端、PVF，不改参考端、客户端或产品代码。
- Required checks: 70/上游/API 地址和二进制哈希可复核；`0x0110` 请求 6B 下界与 A21 成功/错误接收路径需分开确定；另评估 A21 `0x025A USE_ENCHANT_STORE` 是否属于本次附魔验收入口及其附魔师摊位先决状态；材料脚本目标规则和宠物分支明确；覆盖语义及失败/投递若未闭合则设实施停门，不凭来源名推定客户端事实。
- Dependencies: 已接受基线 `e1d4c216`，无未完成的外部合同依赖。

## Detailed Plan

1. 只读定位 70 附魔 dispatcher/业务符号，反汇编入参、目标/材料校验、属性写入、错误及回包；保留 70 自己的 opcode，不向 A21 移植帧号。
2. 阅读上游 `EnchantByBeadRequest`、对应 handler/领域服务、ACK builder、宠物分支及 PVF 配置读法；并查看卡片经附魔师摊位 `0x025A` 的独立入口、摊主/配方先决状态，列出候选资格、覆盖、消耗与出站顺序。
3. 复核 A21 `0x0110` 构包 RVA `0x01676B52`、接收 handler RVA `0x00D1B110` 及客户端字节读取路径；同时核对 `0x025A` 已闭合的 13B 请求是否有可用摊位运行前提。核对真实 A21 PVF 宝珠/卡片脚本与目标部位配置，选可复现样本；`0x0110` 机器记录只有 6B 下界，不能自行宣称精确体长。
4. 形成矩阵、状态/事务设计、宠物隔离方案、ACK/通知字段边界及 T-002/003 输入。未知关键规则列最小验证停点，若不能安全裁决资产变化则阻止后续结算实现。
5. 在本 Task Result 记录文件、哈希、工具和核验结果。取证任务不制造行为测试，协议人工记录只写有直接证据的结论。

## Result

- Changed: 新增 `evidence-and-design.md`；补 `protocol/game/client-to-server/0x0110.md` 人工区、`protocol/game/server-to-client/CMD-0x0110.md` 与 catalog。没有修改协议机器区、产品代码或参考端。
- Checks: 四个样本 SHA-256 已重算；`llvm-objdump` 复核 A21 VA `0x01A76B44–0x01A76B99` 构包和 `0x0111B12E–0x0111B14B` 成功读取；`PvfProbe lst` 复核宝珠 `2600294`、卡 `3600` 与武器 `101010437`；70 ELF 的 dispatcher/业务符号由 `llvm-nm/objdump` 定位，70 `0x0113` 与 A21 `0x0110` 分开记录。
- Design decision: 等级 0、有确定卡关联的宝珠可进入 T-002；目标附魔只改 `EnchantCardId`，保留已接受的装备强化等级、UID 和其他属性。上游将宝珠等级写入目标 `EnchantUpgradeCount` 会覆盖强化，不能照搬。宠物目标在装备路径明确隔离。
- Remaining: A21 `0x0110` 无实帧；升级宝珠与多卡随机语义、覆盖确认 UI 未闭合，先拒绝不安全样本。`0x025A` 附魔师摊位有独立的 13B 入口和跨玩家运行状态；是否纳入用户本轮验收已主动提问，在答复前不对其做资产变更。此未决点不阻断独立的宝珠路径实现，但会阻断整个附魔 Spec 的最终范围确认。
