# Routing Decision

- TaskContract: `./TaskContract.md`，revision 1，ready。

## SDD Readiness

用户明确授权契约及立即重构；所有验收同属容器准入边界，无阻塞问题。

## SDD Value

具体风险是遗漏共享特性导致仓库存取失效，或位筛选覆盖掉交易、容量与所有权规则。契约与独立结果评审用于锁定保行为边界；不增加实施前用户批准。

## Structural Selection

- `single-spec-workflow`：一个完整验收结果，无独立子任务合同。

## Controller Selection

- `sdd-lite` / `focused-delivery`：直接实施与一次独立完整结果评审足够。没有迁移、部署或需单独排序的生产依赖，不建立 Plan/Task 注册表。
- gates 均为 auto，依据本次执行授权。

## Workspace Evidence

- canonical root：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。
- branch：`task/warehouse-storage`；accepted baseline：`c2d647e2e46395d4df542fa565d742be2c81470a`。
- 工件创建前 `git status --short` 为空；Git dir 为主库 `.git/worktrees/warehouse-storage`，common dir 为主库 `.git`，无 superproject。沿用当前隔离工作区。
- main：`6ee658cdf14208d819360c40b3b47c4717ae3499`，不修改。
- durable artifacts：`Document/tasks/container-admission-flags/`；验证日志：`artifacts/container-admission-flags/`（忽略目录）。所有写入路径均解析为上述根下路径。
