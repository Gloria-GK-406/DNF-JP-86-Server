---
kind: rks-spec-result
state: accepted
spec: ./spec.md
---

# Spec Result: 响应式拾取投影

## Delivered

- 共享拾取由 `DungeonRun` 提交为带 RunId、递增 revision、拾取者、地面编号、结果种类与语义接收者的单一变更。
- `PickUpGroundDropCommand` 只在持久化奖励成功后提交变更；重复、无效与背包满不发布成功变更。
- `DungeonPickupProjectionDelivery` 成为唯一成功投影路径，负责把领域角色解析为冻结且仍有效的连接；`GetItemHandler` 不再枚举并直发其他参与者。
- 保留 A21 0x0027 的既有消息形态与拾取者身份；共享地面物在观察者侧消失，但服务端不伪造属于客户端 P2P 的拾取动作。
- replacement、离场、foreign Run、物品、金币、怪物掉落与重复拾取回归均已覆盖。

## Review and Verification

独立复审 verdict 为 `approved`。受影响定向测试、Release 构建与差异检查全部通过；完成证据见 `verification/completion-evidence.md`。
