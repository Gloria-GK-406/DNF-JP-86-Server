# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `2`
- Intake review: `./task-contract-review.md`，approved，精确绑定本路径revision 2。

## SDD Readiness

用户已接受设计并明确授权契约后实现。物品保管、单箱生效、PVF系列扩展与永久规则稳定；未知实机表现属于交付验收边界，不改变服务端软件结果。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 收集箱只有一个端到端验收边界。Direct容易遗漏隐藏库存重登与重放接线，已有审核实际发现这两类缺陷；固定Spec和完整独立结果审核能避免遗漏。用户主动要求契约，采用最轻SDD值得其时间。
- Reroute conditions: 实机或源码证明核心协议／原生属性路线不可用，或出现必须独立交付的客户端改造；先停止受影响动作并重新界定。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 已审核设计解决了耦合选择，不再复制一份Plan。只操作隔离工作区和测试数据库，有事务回滚测试及保留基线；无生产数据变更或无恢复的资产修改，无需更重控制。

## Workspace

原生工作区工具因本聊天根目录不是Git仓库返回Not a git repository；使用原生Git回退创建隔离工作区。
- workspace_root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/collection-box`
- task_branch: `task/collection-box`
- accepted_baseline: `bf283b868cef249ca731e5555b64d8d80202a556`
- source_branch: `task/next-round-20261019`，源码与已有未跟踪资料保留。
- startup_artifacts: 契约以Move-Item转移至相同相对路径，前后SHA相等；无启动提交。

## Execution Configuration

- Profile: `focused-delivery` with `sdd-lite`; long-running: `no`
- Spec baseline: `bf283b868cef249ca731e5555b64d8d80202a556`
- Gates: `spec: none; implementation: auto; review: auto; verification: auto`
- Bound methods: `implementation: test-driven-development`; other stages: none (use Profile defaults).
- Artifact path overrides: none. Defaults: `spec.md`, `spec-result.md`, `reviews/change-review.md`, `verification/completion-evidence.md`.
- Recompose: `python C:/Users/Gloria/.agents/skills/single-spec-workflow/scripts/compose_profile.py focused-delivery --controller sdd-lite`; do not persist composed YAML.

## Skill Update Transition

2026-10-19用户在实现中授权适应新版skill。旧流程下产品实现已发生、完整审核与最终验证尚未结束，也尚无本地提交。现先完成revision 2的独立intake（approved），同步当前Spec绑定，再创建本次接管的planning检查点；它不声称历史上先提交规划才开始实现。保留已有测试日志与有效结果，废除旧sdd-execution.yaml游标及独立implementation.md过程记录，进度改由本地检查点和审核／验证推导。接管后的检查、implementation与seal提交依照当前skill顺序执行；不移动既有任务工作区、不改写历史或扩展业务范围。

- workspace_kind: `linked-worktree`，复用既有工作区；非嵌套创建。
- preexisting_changes: `[]`，本工作区全部差异均属于本Spec；源工作区既有未跟踪研究资料保留在源工作区。
- durable_artifact_paths: `Document/tasks/collection-box`, `Document/CollectionBox-Implementation-20261019.md`, `protocol/`中本任务六份协议记录。
- baseline_evidence: 工作区canonical root、git-dir/common-dir、branch与HEAD已核对；源分支仍为上述baseline。
