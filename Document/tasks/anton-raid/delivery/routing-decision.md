# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `2`

## SDD Readiness

契约已独立通过。现有真实招募证据、修订设计、参照实现和 dev PVF 可用于完成剩余业务。客户端时序的未确认项由完整构建后的集中实机处理。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 小队和冻结编组承接进图，通关和在线状态决定奖励资格。全链路共享一个交付和验收边界；详细计划保留这些依赖，避免部分实现误放行进图或发放资产。
- Reroute conditions: 正式玩家库迁移或单独发布出现独立授权和验收时重新决定。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: 复用已隔离的服务、客户端及玩家库，使用带计划的执行、重点资产回归和独立代码审核。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Workspace: `E:/Project/DNF/DNF-JP-86-Server-anton-raid`, linked worktree, `feature/anton-raid`; preexisting changes: none
- Spec baseline: `11407e919232fbfb6a493c5504207bc14b25e18b`
- Gates: implementation `auto`, review `auto`, verification `auto`
- Bound methods: implementation `test-driven-development` for observable regressions
- Artifact path overrides: none
