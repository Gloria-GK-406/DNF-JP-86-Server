# 路由决策

- 契约：`./TaskContract.md` 修订 2，独立审核通过。
- 结构：`single-spec-workflow`。共享房间事务、正式结算与任务写入形成一条验收链。
- 控制：`sdd-standard` 与 `planned-delivery`。频道、已有权益和数据库的顺序值得提前固定，执行、评审和验证使用自动门槛。
- 工作区：沿用 `DNF-JP-86-Server-pvp` 的 `feature/pvp-duel-phase1` 和该分支上的未提交 PvP 实装。现有变更是本轮要衔接的功能基线，保留其工作状态。
- 基线 HEAD：`89c7e611c50430e73ffc2b86a6096430bda95bd2`。本地活跃测试服及数据库不进入操作范围。
