# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已授权全范围重构和分段检查。契约界定现有物品使用、装备改造、任务经验结算及宠物饲料，新增未实现物品功能和部署排除。允许内部实施选择，无阻塞问题。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 所有入口共用一个效果执行契约，完整写入覆盖与整批事务正确性构成同一个接受边界。直接改写容易遗漏隐藏冷却／配额／保护返奖写入并导致持久资产部分成功；长期分段计划与独立遗漏审查能在各入口迁移及最终整合时约束此风险，控制成本与用户明确要求多检查相符。
- Reroute conditions: 出现必须独立发布／回滚的新功能，或改变现有概率、经验语义及外部部署范围时重新裁定；不因文件数增加自动拆Spec。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `long-running`
- Rationale: 普通最终回归不能发现测试未涉及的资产写入旁路。跨角色／账户资产、装备用UID保护和内存提交的遗漏可能持久化为重复奖励或数据丢失，因此要求独立Spec／Plan挑战、分段写入检查与最终完整结果审查；Standard只有普通结果审查不足以预先检验统一执行策略的漏项。

## Workspace

复用现有linked worktree `E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap`，分支 `task/solo-play-roadmap`，接受基线 `b5506390a2e97f7254d67997510b0b8cca90a4aa`。Git dir为主仓库 `.git/worktrees/solo-play-roadmap`，common dir为主仓库 `.git`，非submodule。开始时产品工作区干净；任务启动后仅本目录为声明的持久任务工件。实现期间不提交中间阶段；最终审查与验证通过后做一个本地接受提交。
