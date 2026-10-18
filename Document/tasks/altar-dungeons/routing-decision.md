# Routing Decision

- TaskContract: `Document/tasks/altar-dungeons/TaskContract.md`
- Contract revision: 1

## SDD Readiness

用户已授权两个祭坛完整实现并部署，独立服务和共享机制的方向已接受。每日限制的数值与协议细节属于实施研究输入，不改变目标。

## Structural Selection

- Selected workflow: single-spec-workflow
- Rationale: 两个祭坛依赖同一动态实体与清房约束，作为一次联合交付。Direct容易只接刷怪忽略队列间清房/每日时间限制和退出后的计时器；持久化Spec与顺序计划固定这些约束，完整结果审查防止遗漏，收益大于流程成本。
- Reroute conditions: 如当前客户端协议缺失到无法构建可验证流程，保留证据并重新评估范围；不得以猜测包体掩盖阻塞。

## Controller Selection

- Controller: sdd-standard
- Profile source: planned-delivery
- Rationale: 先建立波次与清房门控，再接两个领域服务和投影，最后部署的依赖顺序需要显式Plan，避免先写协议后返工。Lite缺少顺序控制；没有需要Governed额外强度的具体风险，旧数据部署有备份。
