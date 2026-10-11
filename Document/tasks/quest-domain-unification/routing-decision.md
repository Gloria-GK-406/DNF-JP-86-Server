# Routing Decision

- TaskContract: `./TaskContract.md`，revision1。
- Readiness：用户已确认三个查询口、只读计划及统一事务结算，双子代理已确定普通／循环／镜像／完成券边界。
- Workflow：single-spec-workflow，一个一致的Quest职责重构及兼容性验收边界。
- Value：本次已发生材料判定遗漏，Direct容易再次只改普通路径。明确迁移矩阵和独立完整结果审查降低旁路遗漏。
- Controller：sdd-standard，planned-delivery。先共享状态计算，再搬迁调用方，最后拆普通／镜像结算及完成券，顺序依赖会影响测试与兼容性；Lite不承载这份迁移依赖。已有DB备份和事务回归保护，无需更重控制。
- Reroute：发现必须改变客户端协议或原任务玩法时重新收敛；不擅自扩大范围。
- Workspace：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`；branch `task/despair-tower`；accepted baseline `424da80417f020d5e852e2807d3526dec54fc392`。
- Baseline：git root/branch/HEAD核验一致，tracked clean。既有`Document/outputs/tournament-scheduling-20261011/`为保留调查证据，不纳入本次提交。
