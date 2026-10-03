# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已授权任务。目标为已有容器与共享材料归一化，排除未完成玩法及线上部署；范围和可观察验收见契约。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户要求契约；共享材料迁移、旧读写关闭、入场读取与协议投影必须共同成立，单独接口收敛不能验收。一个 Spec 保持这一完整边界，计划把数据库约束修正置于新账号容器写入之前，避免最后评审才发现重复来源或入场失败。
- Reroute conditions: 实际证据证明必须改变对外玩法、无法保留已有数据，或新增独立玩法验收范围时重新裁定。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 缺少本任务有价值的依赖顺序控制；Standard 的渐进任务计划及完整独立评审足以覆盖已有 SQLite 事务和可恢复的离线迁移。当前只生成代码并在临时数据库验证，不执行线上不可逆变更，暂不增加 Governed 的多轮工件审核。

## Workspace Evidence

- 2026-10-03 核验当前 linked worktree：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。
- 分支 `task/warehouse-storage`；accepted baseline `fd14e7172b0b6a1b39d194b342e4421d470e69bf`；任务工件创建前完整 `git status --short` 为空。
- Git dir 与 common dir 分离，无 superproject；沿用当前金库任务的后续修复隔离，不创建嵌套工作区。
- durable artifact root：`Document/Tasks/unify-item-containers/`。开发过程中不创建阶段提交；完整结果评审及新鲜验证通过后一次本地提交。
