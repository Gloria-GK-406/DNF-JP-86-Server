# Routing Decision

- TaskContract: `./TaskContract.md`，revision 1。
- SDD readiness：用户已明确限定镜像入口和当局任务，不迁移普通 Quest，执行路线已由客户端与上游取证确定。
- Structural selection：single-spec-workflow。直接实现容易遗漏旧 RunId 清理与永久完成隔离；一份规格加完整结果独立审查能覆盖此风险，收益足以承担轻量流程。
- Controller：sdd-standard；planned-delivery。原先 Lite 的入口假设已因五种任务目标与奖励/清理的先后依赖而调整；先定义运行身份与入口，再实现当局任务，最后接线并独立审查，可避免任务实现依赖错误入场身份造成返工。复用当前 linked worktree `task/monster-declaration-parser`，基线 b9717c65968fb15fe013a7c46e6b2c045ac931be，启动前代码干净。
- Reroute：若发现需要改变普通任务生命周期或客户端协议，先重新确定范围。
