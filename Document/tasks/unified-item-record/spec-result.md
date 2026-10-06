---
kind: rks-spec-result
outcome: failed
terminal_stage: implementation
---

# Spec Result: 通用物品记录旧方案已替换

## Failure or Block

- Category: reality_mismatch
- Basis: 用户澄清 ItemRecord 必须是 Take 后脱离容器的物品，而 Get 应返回容器内引用；旧 Spec 的“单槽读取返回 ItemRecord”与此不相容。用户明确要求回退代码并建立新任务契约。
- Terminal consequence: 旧方案停止，不再评审或接受。没有生成实现提交；后续由 ../item-container-lifecycle/TaskContract.md 接续。

## Repository Result

- Baseline: 32039260ded8d251c5052dfcec597213c561668a。
- 已分类的旧尝试 tracked 代码和测试使用 git restore --source=<baseline> --staged --worktree 按路径恢复；两个新增源码文件在验证位于工作区后移除。
- 回退后 git diff HEAD --exit-code 返回 0；status 仅含本任务历史文档；main 仍为 6ee658cdf14208d819360c40b3b47c4717ae3499 且干净。
- 未提交补丁和新增源码副本保存在本地忽略目录 artifacts/unified-item-record/rollback；原测试日志仍保留，但不能证明新设计。
- Parent rollback required: no，用户指定的代码回退已完成。

## Successor Guidance

- 不恢复旧的“读取快照即 record”方案；新实现从基线独立建立取出、接收、引用更新和显式销毁语义。
