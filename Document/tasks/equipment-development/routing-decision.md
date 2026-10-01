# Routing Decision

- TaskContract: `Document/tasks/equipment-development/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

五项装备功能、指定分支、参考来源、实现与后续集中实机验收的边界已明确。每项都有可观察的装备状态、消耗、回包和重登结果；具体概率、材料与报文细节属于该项 Spec 的取证和设计工作。用户授权建立任务契约并开始实现。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: 用户明确要求一个内聚功能一个 Spec。强化、附魔、魔法封印、异次元属性和锻造各有独立玩家操作、规则及验收结果，可以逐项审查并本地接受。直接同时改动容易把不同费用、失败和装备状态混为一套，造成资产结算错误；逐项 Spec 的证据、方案、结果复审和共享基线能在下项开始前发现这类错误，值得投入流程时间。
- Reroute conditions: 若 A21 证据证明某两项实际共享不可分割的一次操作与验收边界，先修订根契约和结构；若关键客户端语义无法静态确定且会决定资产变化，保留该子项未完成并先取得最小必要实机证据；若分支基线被外部改动，暂停子项接受并重新核对所有权。
