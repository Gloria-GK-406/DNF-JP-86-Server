# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户批准blob保存回放方案并要求契约及实施；协议外壳已静态调查，字段语义明确排除。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 用户明确要求契约。直接实现容易漏掉登录与选角的不同回放、旧二进制键位和角色切换归属；一份规格及独立完整结果审查保护同一保存恢复闭环，值得有限文档成本。
- Reroute conditions: 当前客户端格式证据被推翻，或需要客户端补丁才能满足保存恢复边界。

## Controller Selection

- Controller: sdd-lite
- Profile source: focused-delivery
- Rationale: 方向已确定，无需独立任务规划。Lite规格与结果审查足够；不需要更强审批。用户明确要求复用当前工作分支，覆盖隔离工作区的默认要求。

## Workspace

- Root: E:/Project/DNF/DNF-JP-86-Server
- Branch: task/next-round-20261013
- Accepted baseline: 314bd46356a02c18e6d6f3ccb4354413f630fe1a
- Existing unrelated state: untracked Tool/WireSmoke/__pycache__/，保留且不纳入成果提交。
