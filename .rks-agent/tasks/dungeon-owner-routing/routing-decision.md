# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-owner-routing/TaskContract.md`
- Contract revision: `2`（独立审查 approved：`reviews/task-contract-review.md`）

## SDD Readiness

意图、范围与可观察结果已稳定：用户确认“查当前状态与处理分开、组件内部也分离”“同类写法一次做完”，并在陈旧帧规则上选择“一并统一”。契约 SC-1～SC-8 都有可观察证据。执行方向已知：入口判陈旧 → 以副本占用登记定位 → 单次路由到独立通道，0x000F 为唯一具名例外。剩余的不确定性都是可依代码证据在规格阶段收敛的实现事实，不改变任务语义：
- 各命令在各位置的现行通道对照；
- 组队类整队归属的定位口径；
- 已知不一致窗口的落点；
- 测试替身补登记的方式。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 改动横跨 13 个命令、7 个处理器类与大量测试替身，其中一部分是用户授权的行为变化，另一部分要求严格不变。Direct 的具体失败路径有两条：
  - 漏掉某个命令在某个位置（城镇、无会话、会话代次窗口、组队整队归属）的现行通道，把结构搬迁误做成行为变化，而现有测试几乎不覆盖陈旧帧与塔内路径，问题会被放过；
  - 把授权的陈旧帧统一与结构搬迁混在一起，无法分辨哪些差异是授权的。

  规格先钉住“位置 → 通道”对照与陈旧帧变化清单，计划把“先表征、再搬迁、再统一陈旧帧”排成可单独验证、单独提交的阶段，整体审查与完成验证再核对契约条件。这些控制直接防住上述两条失败路径，值得额外时间。所有改动共享同一个验收边界，阶段之间按依赖串行，没有可独立验收或独立回滚的结果，因此为单一规格。
- Reroute conditions:
  - 规格阶段发现某个命令的现行分发无法用“定位一次、单通道”表达且不能作为具名例外时，回到用户对齐；
  - 发现占用登记与现行查询存在会改变通道、又不能按“服务不认可即走现行去向”处理的窗口时，回到用户对齐；
  - 出现可独立验收、独立回滚的子结果时，改走 `multi-spec-workflow`。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`
- Rationale: Lite 没有计划阶段。本任务的关键风险在先后依赖：
  - 表征测试必须在结构搬迁前提交；
  - 定位能力与测试替身补登记必须先于命令迁移；
  - 陈旧帧统一必须与结构搬迁分开提交。

  早期选错定位口径或测试替身形状，会扩散到 13 个命令和大量测试后才在最终审查暴露。计划阶段的显式排序与分阶段提交正好防住这一点。Governed 的独立挑战针对安全、权限、资金或不可恢复数据等高后果资产，本任务只改协议层分发与进程内路由，改动可由提交回滚，Standard 已足够。

## Execution Configuration

- Profile: `planned-delivery` with `sdd-standard`; long-running: `no`
- Spec baseline: `7adec858d0b21b0eb4ec1e80e3d7f5e70db99a6c`
- Gates: `spec: none`; `plan: none`（本次用户未要求审阅规格与计划）；`implementation: auto`；`review: auto`；`verification: auto`
- Bound methods: `implementation: test-driven-development`
- Artifact path overrides: `none`
