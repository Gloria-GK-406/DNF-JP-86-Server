# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: `2`

## SDD Readiness

目标已收敛为 ID `0x11` 主目录端到端闭环，快速组队、Story/Cycle、PVP 与 ID `0x14` 候补窗明确排除；
matching-info 已闭合为本地筛选配置，主目录的数据链、固定客户端、可观察完成标准和授权边界均清楚。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Rationale: 旧任务因窗口归属混淆而阻断。独立接受“阻塞证据闭合”“town-visible `0x0009` 投影”与
  “原生 UI 验收及必要修复”，可以在不引入错误 `0x023C` 实现的前提下保留每个结果的审查/回滚边界；
  用户明确要求所有 blocker 先解决，额外控制成本值得。
- Reroute conditions: 若后继运行时证据推翻 `0x0002 -> 0x0009` 的主目录数据链，或证明主目录必须依赖
  快速组队/地下城进入状态机，则停止并返回根控制器；ID `0x14` 的 unknown record 不得借机混入本任务。

## Controller Selection

不适用：Multi Spec 的每个子规格独立选择最低充分控制器。
