# Routing Decision

- TaskContract: `.rks-agent/tasks/sealed-treasure/TaskContract.md`
- Contract revision: 1；独立审查 `task-contract-review.md` 为 approved，绑定同一路径和 revision。

## SDD Readiness

用户已决定本类免疲劳并授权按调查设计实施。暂定票据、次数与补给、无额外退出退票、孤儿任务和部署边界均已明确。交付是服务端实现与自动验证，A21 人工验收单列。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 一个活动副本闭环，独立拆分验收会掩盖门票到运行再到奖励的接线缺口。Direct 的主要风险是遗漏零疲劳准入、失败回滚和跨房幂等；一份明确验收规格及独立完整结果审查可以在有限成本内保护这些要求。
- Reroute conditions: 若既有协议无法承载且必须另做客户端逆向才能交付，重新判定范围与证据；若发现需要生产部署或持久化迁移，不自动扩大本轮授权。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 通用生命周期和事务边界已有实现，当前局部设计可由主代理直接完成；没有需要另写持久计划或强制人工审批的未决取舍。独立契约和结果审查足以防止遗漏及证据升级。

## Execution Configuration

- Profile: `focused-delivery` with `sdd-lite`; long-running: no
- Spec baseline: `2764030f4504c8e4f08c4780f75d230ee8add39a`
- Gates: spec none; implementation auto; review auto; verification auto
- Bound methods: implementation `test-driven-development`; other stages none
- Artifact path overrides: none
- Workspace: `E:/Project/DNF/DNF-JP-86-Server`, current-checkout
- Branch: `task/sealed-treasure-20261019`，从 baseline 新建；源分支 `task/next-round-20261019` 未前进。
- Baseline evidence: Git root 和 common-dir 都指向当前仓库，HEAD 为 baseline，无已跟踪修改或暂存内容。任务外未跟踪路径不与本图实现重叠。
- Preexisting changes: `Document/MarriageHousing-Deployment-20261018.md`, `Document/Opus-WeddingInvestigation-Report-20261016.md`, `Document/outputs/asuna-slot-reuse-20261015/`, `Document/outputs/housing-interaction-fix-20261018/verification.md`, `Document/outputs/marriage-housing-20261018/a21/__pycache__/`, `Document/outputs/npc-shop-gain-fix-20261017/`, `Document/outputs/wedding-force-position-20261017/`, `Document/outputs/wedding-retest-20261017-121649/`, `Document/调查报告-亚丝娜篮子同格复用不同步-20261015.md`, `Tool/ClientStringPatch/__pycache__/`, `Tool/WireSmoke/__pycache__/`。均不纳入提交或清理。
