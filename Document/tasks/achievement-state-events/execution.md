# 实施记录

- 新工作区 `task/achievement-state-events`，基线 `d0427ce3ecc987ab76050f0e714fe919c7188fcf`；不修改 main、不调整客户端。
- 结构：统一持久化物品写入 -> `InventoryContentsChanged(ContainerId)` -> 可靠 EventService -> 成就按变化范围检查 -> `AchievementCompleted` -> 称号簿 -> 既有事件通知投影。业务不嵌入成就 ID。
- Take/Put/数量与模板 Update（包括 Creature facet 先打开的孵化）登记同事务事实，同事务同容器合并。查询和 detached 创建不发容器事实。
- QuestCompleted 在最终职业效果之后检查转职/觉醒。共享材料覆盖账户下未删除角色；仓库、称号簿和塔内临时物品不参与持有判断。
- 核心回归先 RED：职业/持有未解锁；实现后 GREEN。独立审查 F-1 指出离线未初始化角色依赖后继缺行；扩展两角色两级链先证实 RED，再仅对已满足后继 Ensure，最终 95 项通过，复审 approved。
- 初次 Application 全量 3304 通过/3 失败：两个超时断言隔离复跑通过；NotificationRegistration 错误在未修改 main 重现，正常权限下基线通过，确认沙箱日志权限影响。当前改动正常权限全量重跑 3307 通过/0 失败/0 跳过；不修改或屏蔽失败用例。
- 固定条件读消费时当前状态；不补造瞬时或累计历史。真实客户端实时解锁由用户部署后验收。
