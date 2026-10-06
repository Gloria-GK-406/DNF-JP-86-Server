# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户接受前一轮具体设计并明确要求契约和执行。单一验收边界为容器记录统一，身份、状态、数量与业务行为兼容可由真实存储回归验证。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: Direct 容易只改包装而遗漏身份结算、附加状态或旧调用入口；用户要求契约，完整 Spec 和独立结果评审可控制这些遗漏。模型、存储和消费者必须一起交付，没有独立验收子成果。
- Reroute conditions: 发现必须改变线上格式/数据库、业务归属规则或需要独立上线边界时重新评估；普通兼容修复仍属本任务。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 身份结算和完整状态读回必须先确定，否则批量迁移消费者会把旧的猜身份语义固化进新接口，结果评审时返工代价高。明确接口与依赖顺序的 Plan 在这里比 Lite 有价值。保持原有业务授权和归属边界，仅在隔离工作区及临时数据库验证，不操作运行数据，不需要额外挑战/审批阶段。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`
- Branch: `task/warehouse-storage`
- Accepted baseline: `32039260ded8d251c5052dfcec597213c561668a`
- Source main: `6ee658cdf14208d819360c40b3b47c4717ae3499`
- Proof: 创建任务工件之前 git status --short 为空；git-dir 为主仓库 .git/worktrees/warehouse-storage，common-dir 为主仓库 .git，无 superproject；复用既有工作区，不创建嵌套工作区。
- Durable artifacts: `Document/tasks/unified-item-record/`；测试日志置于忽略的 `artifacts/unified-item-record/`。
