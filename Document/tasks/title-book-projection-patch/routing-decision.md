# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已授权研究、实现及部署，永久原件/佩戴镜像、自由称号勾选与正常装备边界已确定。具体hook和投影实现留给计划阶段验证，不影响目标含义。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户要求先详细研究。已有多次单点修复遗漏原生前置移动及书行状态；先固定客户端/服务端协议与调用顺序，可避免写完一端后返工另一端。客户端插件与服务端投影共同满足一个可验收结果，不能独立完成。
- Reroute conditions: 若目标镜像或实际原生入口不同且无法安全识别，暂停相应写入重新核对；若出现独立功能范围，另行评估契约。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 需要先解决等待队列与来源投影的耦合选择，再实现；显式Plan比Lite事后审查更早发现错误接口。无不可恢复持久化改动，备份与目标校验足以保护本地测试，因此不追加Governed。

## Workspace baseline

- 服务端：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`，`task/abyss-research`，HEAD `9da9b83ee2ad3f42502d575254d8a176e6bbfeac`。按用户持续要求复用深渊分支。
- 保留基线未提交集：`protocol/game/client-to-server/0x0010.md`、`Document/abyss-research-20261007/manual-hell-refusal-ui-lock.md`、`Document/abyss-research-20261007/random-hell-deferred-issue.md`、`Document/tasks/achievement-title-book/native-title-reaudit-20261008.md`、`Document/tasks/achievement-title-book/verification/free-title-readonly-20261008-211815.jsonl`。它们不归本任务回滚/提交。
- ClientHack：`E:/Project/DNF/DNF-JP-86-ClientHack/.worktrees/title-book-projection`，`task/title-book-projection`，基线 `2aaddbf`；源main未变。
- 本任务所有写入限于上述两仓工作根与授权部署目录，契约/证据放Document。跨仓验收分别提交本任务结果，不触及保留集。
