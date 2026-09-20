# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户已明确枚举边界、最高难度缺省与持久化语义，并于本轮授权“执行吧”。剩余细节可在此范围内核对，不扩展完整 #102。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求契约；难度在解包、角色权限、实例与换房中多处流转，遗漏任一路径会造成显示和实际难度不一致。一个规格与完整结果独立审查可保护同一验收结果，成本低于返工。
- Reroute conditions: 发现现有状态无法解释为连续最高难度，或需要新增组队/通关解锁政策时返回契约对齐。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `focused-delivery`
- Rationale: 路径已确定，类型迁移、领域读写和边界回归可以直接实施；无需额外规划或高风险治理阶段。

## Workspace

- Root: `E:/Project/DNF/DNF-JP-86-Server/.worktrees/dungeon-difficulty`
- Branch: `task/dungeon-difficulty`
- Accepted baseline: `026c4f9f50baa8e47d05260b45593d07f546d2e8`
- Source branch: `main`，保持不变。
- Startup artifact: TaskContract.md 二进制复制并校验 SHA256 后移除源副本；其他变更为空。
- Native worktree tool returned Not a git repository for parent cwd; shell fallback created and verified this sole task worktree.
