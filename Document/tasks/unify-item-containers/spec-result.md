---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# 交付结果：统一物品容器与共享材料

## Delivered

- 角色/账号容器以统一所有者、容器种类和普通物品操作读写。11种晶体/品级灵魂使用账号共享物品容器，已知材料槽映射、金库存取与现有生产/消费入口全部接入，旧余额结算删除。
- 一次性 SQLite 迁移保留合法数量，重复启动不增发，失败回滚；历史材料实体冲突明确拒绝，不猜测合并。入场、增量和 replay 投影来自当前容器，保留原 A21 wire 形状与零值通知边界。
- 更新模型/迁移说明、协议实现记录和集中实机清单。用户已确认的金库及教程验收保留，本次共享材料客户端行为仍待实机验证。

## Verification

[完成证据](verification/completion-evidence.md) 覆盖全部成功条件；[独立评审](reviews/change-review.md) 为 approved。具体测试口径、原失败、修正与最终结果均由完成证据索引，不重复建立状态表。

## Repository Result

- 工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`；分支：`task/warehouse-storage`。
- 接受基线：`fd14e7172b0b6a1b39d194b342e4421d470e69bf`；本次完整生产、测试、文档及任务证据通过一次本地结果提交接受，不改写历史。
- 变更集中于 Abstractions/Data/Application/Persistence/Protocol 的容器与材料路径、对应回归测试、Document 与 protocol 记录。
- Parent rollback required: no。保留分支和工作树；未部署、推送、创建PR、合并或清理，线上库保持原状。

## Artifacts

- [模型及旧库迁移](../../AccountMaterialBag.md)
- [集中实机清单](../../SharedMaterialHumanTest-20261003.md)
