# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户明确授权按已讨论方案编写契约并实施。独立塔服务、dgn/TowerRoom、自动层推进、生命周期和奖励闭环边界已确定；协议细节需在实现中补证，不能用未确认字段推导资产。交付为服务端实现/自动化验证和集中实机验收清单。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 入场归属、层版本、敌对 APC、失败计层和奖励提交耦合；直接逐文件实现容易在普通副本兼容路由或最终奖励上遗漏分支。用户要求任务契约；一个 Spec 和有依赖的实现单元使领域事实先固定，再接协议和资产，最终独立评审整个闭环。它们属于同一可运行迷妄之塔结果，不拆独立 Spec。
- Reroute conditions: 实证否定 dgn/TowerRoom 中央结构或必须另行决定玩法政策时重开边界；不能因代码量或暂时测试失败升级治理。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `long-running`
- Rationale: 相比 Lite，显式计划与分阶段恢复点可防止先接普通 Cell、后补塔状态导致成片返工，并保留内容/协议证据缺口至完整验收。当前仅本地代码和隔离测试，既有事务设施可验证，无需 Governed 的独立 Spec/Plan 审批链。

## Workspace

- canonical root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`
- branch: `task/warehouse-storage`
- accepted baseline: `762d0cb4e89e3d9bfb666d2642b4ee4ba727ed31`
- 本会话持续工作的隔离分支，研究已提交；复用而不嵌套新 worktree。Git dir 为主仓 `.git/worktrees/warehouse-storage`，common dir 为主仓 `.git`，无 superproject，开始前 status 为空。
- durable artifacts: `Document/tasks/illusion-tower/`。所有产品与测试修改均在该 canonical root 内；保留其他工作区、运行实例及数据。验收后仅一次本地结果提交，不推送/合并/清理。
