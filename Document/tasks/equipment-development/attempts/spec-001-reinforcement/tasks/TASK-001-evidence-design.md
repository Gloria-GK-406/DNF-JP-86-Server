# TASK-001 — 普通强化证据与设计

## Task Unit Contract

- Revision: 1 (identifies this contract and its detailed plan)
- Outcome and references: 交付能指导普通强化结算与 A21 投递的证据对照及设计决策；覆盖 Plan T-001、Spec RQ-001、RQ-002、RQ-003、RQ-005 和 AC-001。
- Owned boundary: 本子项的 `Document/tasks/equipment-development/attempts/spec-001-reinforcement/` 取证/设计 Markdown，及有直接证据支撑时的 `protocol/` 普通强化人工记录或语义源；只读使用 70 ELF、上游、A21 客户端及 PVF，不改产品代码、测试、客户端或参考端。
- Required checks: 核对各二进制/PVF 身份；70 端入口与关键调用、上游实现、A21 构包与响应读取均有可复核位置；费用/概率/惩罚/保护各结论区分证据来源；设计覆盖失败原子性和回包，未知项不被当作已证实规则。
- Dependencies: 无；基线 `a790a243` 已通过解决方案构建。

## Detailed Plan

1. 记录 70 `df_game_r`、A21 DNF.exe 与 Script.pvf 的哈希、版本及可用工具。沿 70 `Dispatcher_UpgradeItem::dispatch_sig` → `WongWork::CItemUpgrade::ProcUpgrade` → `_CheckCondition`、`_DoProcUpgrade`、`_ProcPenalty` 读入参、拒绝、扣费、成功/惩罚路径；只把反汇编明确支持的行为标为 70Reference。
2. 顺着上游 `ItemUpgradeRequest`、`InventoryItemUpgradeService`、`ItemUpgradeTableProvider`、`ItemUpgradeAckBuilder` 和库存提交通路提炼候选规则。逐项与 70、A21 `protocol/game/client-to-server/0x0050.md` 及客户端响应 handler 对照；冲突写成明确差异，不用上游数值填客户端静态空白。
3. 用 `Tool/PvfProbe` 读取真实 A21 `etc/upgrade.etc` 及必要的强化券、保护券脚本，核对表行语义和材料/惩罚阈值。若有客户端响应读取或构包分支未闭合，提取最小静态证据；确需实帧才能决定资产语义的点列为阻断项。
4. 在本子项目录提交一份证据与设计 Markdown：行为决策表、状态和事务边界、结果投递顺序、可复用的现有接口、剩余未知与 T-002/T-003 的输入。只修改有直接新证据的协议记录，保留机器生成区的来源边界。
5. 执行本 Task 的检查：路径和哈希可重现、引用可解析、每个高风险决定有证据或明确停门；将实际结果及剩余项记录在本文件末尾。研究任务无需制造行为测试。

## Result

- Changed: 新增 [evidence-and-design.md](../evidence-and-design.md)，记录四种样本哈希、70 虚拟地址、上游路径、A21 静态回包读取顺序与真实 PVF 的强化表和保护券样本；未改业务代码或协议机器生成区。
- Why: 70 使用 `0x53`，A21 请求是 `0x0050`；只有分开来源并核对响应 handler，才能防止直接搬运旧服报文或凭经验扣费/销毁。
- Checks: `Get-FileHash` 核对四个样本；`llvm-objdump` 复核 A21 VA `0x0113DA21–0x0113DABB` 及 `0x0113E24F–0x0113E304`；`PvfProbe tokens/lst/where` 复核 `etc/upgrade.etc` 和保护券脚本；上游 ACK builder 与 A21 读取宽度一致。无实帧，不能声称 ClientVerified。
- Remaining: 破坏产物容量/数量、保护券自动选择、费用附加效果和 A21 错误 reason 仍须在 T-002/T-003 过停门。若无法安全支持破坏，不得吞产物或改写处罚，须明确拒绝该路径。T-002 可开始实现普通强化规则、资产事务与确定性测试。
