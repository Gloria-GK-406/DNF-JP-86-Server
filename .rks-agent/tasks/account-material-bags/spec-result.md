---
kind: rks-spec-result
spec: ./spec.md
state: complete
---

# Result：账号级晶体与品级灵魂单一真源

## Delivered

- 新增按 `AccountId` 解析的 `AccountMaterialBag`、领域服务和 SQLite Store；六种晶体与五种品级灵魂统一保存在账号记录，同账号角色共享、跨账号隔离。
- 十一种资源的 kind、itemId、虚拟槽统一由 `AccountMaterialCatalog` 定义；入场和增量投影不再各自维护映射。
- 装备分解先分类产物：账号资源不占角色背包，普通材料沿用既有到货规则，宇宙灵魂明确保留为角色材料。
- 装备删除、便携分解机消费、角色材料写入与账号材料增加加入同一个领域事务，任一路径失败整体回滚。
- 地面拾取、翻牌、接任务、交任务、怪物任务直发和教程奖励统一经过 `IItemGrantDomainService`；容器最终写入边界拒绝账号材料旁路，避免角色槽形成第二真源。
- 入场 `NOTI 0x000D` 投影 `354..364`；分解和其他到货后用 `NOTI 0x000E` 发送最终绝对数量，分解 ACK 仍表达本次产量。
- 更新账号材料架构文档及 `NOTI 0x000D/0x000E` 协议记录，明确本服策略与 A21 证据边界。

## Review and verification

- 独立完整结果复审：`approved`，首轮两项 finding 全部闭合。
- Fresh build：0 warnings、0 errors。
- Fresh serial solution tests：3542 passed、0 failed、2 conditionally skipped。
- 聚焦测试：Application 35/35，相关 Protocol 216/216；`git diff --check` 通过。
- 详细证据：`verification/completion-evidence.md`。

## Repository result

- Baseline：`b94308ce42585ac0b88e303b0010131931b74265`。
- Result：分支 `task/account-material-bags` 的单一本地验收提交。
- 尚未合并、部署、启动或清理工作区；这些操作需要用户后续明确授权。
- Parent rollback required：no。

## Retained boundaries

- 不做历史错误角色材料行迁移或删除；用户会在实机测试前自行清理。
- 宇宙灵魂 `3257..3260` 继续属于角色普通材料。
- 未建模的购买/开盒账号材料来源目前事务失败而不是错误落角色槽；后续接入复用统一 Grant 服务。
- 客户端实际刷新效果留待合并启动后的 A21 实机验收。
