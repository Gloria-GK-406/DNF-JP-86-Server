# TASK-003 — A21 普通强化命令、投递与人工验收包

## Task Unit Contract

- Revision: 1
- Outcome and references: 将 TASK-002 已验证的普通强化结算接入 A21 CMD `0x0050`，提交后投递结果与资产状态，并交付 Human Test List；覆盖 Plan T-003、Spec RQ-005/RQ-006 及 AC-005/AC-006，同时完成 AC-004 的同序列重放/账户串行保护。
- Owned boundary: `Server/src/Dfo.Server.Protocol/Game/`、`ProtocolModule.cs`、`GameProtocol.cs`、`Server/src/Dfo.Server.Data/Networking/` 消息枚举及对应协议/集成测试，`protocol/game/client-to-server/0x0050.md` 人工区及本子项目录文档。可为去重增加专用小型协议服务，不改变全局帧语义。普通强化业务规则不再重写。
- Required checks: 真实 A21 请求宽度/边界、`method 0` 准入及其他模式拒绝；客户端 handler RVA `0x00D3D950` 的成功 body 与破坏数组、错误双字节信封；成功 ACK → 库存/虚拟材料/金币更新顺序；命令提交失败无 ACK 成功；同连接同序列重放不二次扣费；真实 SQLite 重登状态；Human Test List 和整套相关测试。
- Dependencies: TASK-001、TASK-002 已完成；A21 `0x0050` 尚无实帧，所有静态/上游结合推断保持标记。

## Detailed Plan

1. 按 A21 构包记录与上游 parser 实现 `0x0050` decoder：`u16 method, i16 targetSlot, i32 expectedId, i16 materialSlot, i16 optionalSlot, i32 nameLen, nameBytes`。检验长度与上限，只接受普通 mode 0；名称的实际编码未由客户端静态路径证实，因此保留原字节且不参与权威结算。主背包虚拟槽 358 可以作为账户透明材料入口，不能误映射成实物槽。
2. 建立 `IAccountCommand` 的普通强化 command/handler，借已有账户 gate + unit-of-work 把领域结果提交。查明 sequence 的实际增量/回绕边界，建立局部、容量有界的同连接同序列重放防护；同一报文不得二次结算，回放给请求者一致的结果，提交失败不能缓存成功。
3. 编码 `0x0050` 成功/错误 ACK。成功 body 先 `01`，再按 A21 handler 读取序列写 mode、材料槽/余量、可选槽、旧等级、结果码、新等级、目标槽、可选槽；结果码 3 时追加 `u8 count + N×(u16 slot,u32 itemId,u32 count)`。错误固定 `00,reason`，reason 必须属于已静态定位的客户端分支；类型 1/2 增幅与高级强化在本 Spec 返回错误，不调用普通结算。
4. 使用提交后通知组合 ACK、物理槽刷新、账户虚拟材料刷新、金币刷新；先确认客户端真实读取要求和现有 `GameInventoryDelivery` 用法，保证无成功回包先于事务提交。必要公告仅在有 A21 直接证据时发送，不照搬 70 广播 opcode。
5. 先写字节级与 handler 集成测试，覆盖正常成功、普通失败、保护、破坏含产物、错误、畸形报文、同序列重放、两请求串行、提交失败和重载。完成 `Document` Human Test List，明确样本装备/材料、操作、预期、重登及抓包点，并标记未实机确认。

## Result

已接入 A21 `CMD 0x0050`：限长解码并保留名称原字节，非普通 method 明确拒绝；账户命令事务提交后发送成功 ACK、槽位/虚拟材料/金币投影，失败仅发送错误信封；同连接同序列有容量上限的重放缓存。成功与破坏产物 ACK 的字节级测试、事务提交失败/重放集成测试均已加入。`Human-Test-List.md` 覆盖成功、失败、保护、破坏、重登及抓帧定位，尚未由用户实机执行。

最终针对性结果：普通强化应用测试 38/38（含同序列换目标/切角色、虚拟材料错配和真实缺项的 +19 费用回退），协议测试全量 1176/1176，真实 A21 PVF 的 Infrastructure 测试 403 通过、2 跳过。完整 Application 测试在设置真实 PVF 时为 2205 通过、3 失败（两项 `CreatureGoesBackOnTests` 的宠物种子提交失败和一项单独重跑通过的 `PaidCardFlipCostTests`），本项不把该全量失败写成通过。独立审查与最终验证见上级目录的 `reviews/`、`verification/`。A21 `0x0050` 仍无实帧，故不能标 ClientVerified。
