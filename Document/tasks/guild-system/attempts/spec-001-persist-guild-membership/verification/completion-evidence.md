---
kind: rks-verification
decision: pass
---

# 公会基础完成验证

- 产品提交为 `4988cb016f73318083bef2c963c9ba0b969f2967`，由 [独立审查](../reviews/change-review.md) 通过。
- 对此版本运行 `dotnet test Server/86JP.Server.slnx --no-restore --nologo -m:1 -v quiet -p:UseSharedCompilation=false`，筛选公会、库结构、角色花名册命令、迁移与架构测试，使用开发 PVF `A97132DD…` 和各测试自建 SQLite 临时库。当前筛选排除 `The_production_graph_has_no_loop`，其失败在未修改的 `cada01a0e` 与任务分支均复现，原因均是测试组合的 `DropMaterializationLimits` 缺失。其余 176 项通过、退出码 0。
- 执行前的针对性迁移测试曾在原基线真实失败，三张公会表计数为 0，当前同用例通过。使用允许本地测试宿主通信的执行环境运行 .NET 测试。

| 条件 | 核对结果 |
| --- | --- |
| AC-1 | `GuildMigrationTests` 验证空库和 v13 升级均有三张公会表，旧账号 ID、角色名、点券与金币保留，重复迁移无动作。基线结构计数回归通过 |
| AC-2 | `GuildDomainTests` 验证创建、唯一会长、重复和重名拒绝、余额不足、缺价与写入中途回滚。开发 PVF 的创建费提取测试为 300000 |
| AC-3 | 真实 SQLite 验证申请归属和留言、两会分隔、会长和普通成员读取范围不同、审批的一次性关系、名册／公开身份一致 |
| AC-4 | 真实 SQLite 验证成功退会、踢人和解散、解散时成员和待审批者均受影响、软删申请者的清理。真实命令总线测试验证会员删除拒绝时写单元回滚和只返回失败 ACK |

本期验证范围是服务端核心能力与数据库迁移。公会业务的 A21 入口、显示、全套协议和用户验收由根任务的后续阶段验证。
