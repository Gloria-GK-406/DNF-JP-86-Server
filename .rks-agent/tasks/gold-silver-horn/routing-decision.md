# Routing Decision

- TaskContract: `.rks-agent/tasks/gold-silver-horn/TaskContract.md`
- Contract revision: `1`; colocated independent review `approved` for this exact identity.

## SDD Readiness

用户授权调查、实现、部署与验收清单，玩法边界稳定。042B第二槽与4123映射已由客户端loader闭合；购买材料与开启金币分工也已查证。原服开放时刻不作为本服常驻策略前提。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 一项可整体验收的活动玩法；Direct中容易只完成四房而遗漏持久扣次、拒绝回滚、选角/日切投影或奖励购买后的开启。短Spec固定这些验收点，独立完整结果审查交叉检查整条玩家循环，收益高于小量文档和审查成本。
- Reroute conditions: 发现必须修改客户端/PVF、独立不可接受的结算范围或无法闭合的协议前提时停止受影响实现并重评。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 核心路线已有证据和可复用管线，无须独立Plan/Task Units；Standard增加的顺序计划不改变当前结果。数据无需迁移，部署已具备副本演练和运行回退，未出现无恢复保障的持久数据损失路径。

## Execution Configuration

- Profile: `focused-delivery` with `sdd-lite`; long-running: `no`
- Spec baseline: `ccc0e845e3006d653567bf01dd86b31c4d266e34`
- Gates: `spec: none; implementation: auto; review: auto; verification: auto`
- Bound methods: `implementation: test-driven-development; review: reviewing-work; verification: verification-before-completion`
- Artifact path overrides: `none`
- Workspace: `E:/Project/DNF/DNF-JP-86-Server`, current-checkout, task branch `task/gold-silver-horn-20261019`.
- Baseline evidence: git root/common-dir both main checkout; original branch task/next-round-20261019 unchanged at baseline; no staged/tracked dirty files; task research/artifacts are owned; 18 preexisting file hashes unchanged and retained in artifacts/gold-silver-20261019/preexisting-files.json.
