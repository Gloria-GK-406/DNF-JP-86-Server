# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-party-roster/TaskContract.md`
- Contract revision: `1`（独立审查 approved：`reviews/task-contract-review.md`）

## SDD Readiness

意图、范围与可观察结果已稳定：用户确认“副本和队伍关联而不是人”“人物找副本先找所属队伍”，契约 SC-1～SC-8 均有可观察证据。执行方向已知：副本成员只问队伍、删除自持名单与按角色索引、调整离开路径、显式守护运行中队伍不增员。剩余不确定性（选图回城非队长路径能否到达、入场中止与入场拆离旧局的处理、“图外角色”的定义、整队结束时逐人拆离副作用的保持）均属规格阶段可依代码证据收敛的实现事实，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户在看到改动规模（副本核心服务约 60 处、队伍接口与 12 个测试替身、约 150 处测试构造与约 40 处多人入场用例）后明确选择“先写规格和分阶段计划给我审，再逐阶段实施”。Direct 的具体失败路径是：在约 1000 个依赖副本服务的测试与多条离开路径之间一次性改动，容易遗漏某条路径的先后调整或某种整队结束的副作用，出现“人已退队、副本仍留其本局状态”且难以定位。规格先钉住“谁在本局”的单一来源、各离开与结束路径的语义及测试替身形状，计划再把改动排成可单独验证、单独提交的阶段，这些控制直接降低上述遗漏与返工，值得额外时间。所有改动共享同一个验收边界（副本成员只问队伍且行为不变），阶段之间按依赖串行，不存在可独立验收或独立回滚的结果，因此为单一规格。
- Reroute conditions: 规格阶段发现存在必须保留的“个人离开副本但保留队伍”路径，或运行中队伍确有合法增员路径，导致“副本成员即队伍成员”不成立时，回到用户对齐模型；若出现可独立验收、独立回滚的子结果（例如队伍接口改动需单独交付），改走 `multi-spec-workflow`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 只有规格与整体结果审查，没有计划；而本任务的关键风险在于改动的先后依赖（先有角色→队伍查询与测试替身，副本内部才能去掉自持名单，离开路径才能调整顺序），早期选错测试替身形状或索引方式会扩散到数百处测试后才在最终审查暴露，计划阶段的显式排序与分阶段提交正好防住这一点。Governed 的独立挑战与计划审批针对安全、权限、资金或不可恢复数据等高后果资产；本任务只动进程内存中的副本运行时，改动可由提交回滚，且用户本人会审阅规格与计划，Standard 已足够。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `1cff7218ddff1570dcd48eb07b779283ee7c6542`
- Gates: `spec: none`（与计划一并交用户审阅）；`plan: hitl`（用户审阅规格与分阶段计划后才进入实施）；`implementation: auto`；`review: auto`；`verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
