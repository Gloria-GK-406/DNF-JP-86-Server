---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan：装备附魔

## Planning Choice

采用逐步 Task Unit。`0x0110` 目前只有静态请求下界，宠物目标分支与结果格式尚待查明；先完成 70/上游/A21/PVF 四源对照，才能固定领域资格与客户端投递。若证据显示请求并非装备附魔入口，T-001 及时修正方案而不让后续照着名字写代码。

## Shared Approach

- 装备实例身份、`EnchantCardId` 等存储和物品投影以已接受基线代码为准。服务端根据当前角色物品、材料内容与目标部位重新计算，绝不接收客户端声称的附魔值。
- 研究 70 附魔入口和上游 `EnchantByBeadRequest`、服务及 ACK；A21 PE 的 `0x0110` 构包与接收读取路径独立验证，真实 `Script.pvf` 决定宝珠适配与附魔字段。
- 完整结算沿用账户命令及 SQLite 事务；在证实目标、材料、覆盖、费用、奖励和容量后才扣除。共享 opcode 的宠物目标按边界显式拒绝或经过独立宠物领域服务，不能误写装备。
- 回包/通知在提交后发，失败只发有证据支持的错误。未知字段或无实帧不补成假事实，Human Test List 负责后续用户验收。

## Work and Dependencies

| Work | Delivered outcome | Depends on | Covers |
|---|---|---|---|
| T-001 | 70 ELF、上游、A21 PE/协议记录、A21 PVF 的附魔证据矩阵；闭合可实现规则与未知/停门，形成事务/协议设计。 | 已接受基线 `e1d4c216`。 | RQ-001、RQ-002、RQ-004 |
| T-002 | 内容源与装备附魔领域结算，验证部位适配、覆盖、材料、原 UID、拒绝/提交失败/重试，宠物目标边界。 | T-001 的规则和内容判定。 | RQ-002、RQ-003 |
| T-003 | A21 入站/成功错误回包、提交后装备/材料投影与 Human Test List，检验重登与既存宠物回归边界。 | T-001 的报文证据和 T-002 的领域结果。 | RQ-004、RQ-005、整体集成 |

## Verification

T-001 逐主张标 `70Reference`、`UpstreamReference`、`ClientStatic`、`WireCaptured`、`CurrentImplementation` 或 `Unknown`。T-002/003 用确定输入和真实 SQLite/PVF 样本检查原 UID、覆盖、守恒、回滚、重放、投递与重登；对所有证据支持的 ACK 分支做字节级断言。最终按 AC-001—005 独立完整审查和新鲜验证；用户 GUI 结果独立保留。

## Next Planning Boundary

先详细规划并执行 T-001；若 A21 的宠物分支或附魔覆盖关键语义无法安全闭合，将具体缺口、可验证停点和影响提交到本子项控制器，不让 T-002 猜测扣费或改装备。
