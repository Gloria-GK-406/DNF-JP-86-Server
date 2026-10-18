---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：无尽与极限祭坛

## Delivered

两个独立祭坛领域服务已接入真实PVF波次、动态Cell/清房门控、极限难度选择与超时、每日3次和无尽金币费用、任务/成就事实、奖励及主动离场结算。现有凭证/事件/奖励派发路径保持职责，协议人工区已补证据和旧推断校正。新运行包已启动，可供A21用户测试。

## Verification

[最终验证记录](verification/completion-evidence.md)覆盖SC-1—SC-5。[独立完整结果审查](reviews/change-review.md)为approved。实机验证不在自动化完成声明内。

## Repository Result

- 当前分支：task/next-round-20261013，沿用当前工作区。
- 接受基线：a375a148d7fe8a711a64f56959bd41067b4cf217；在review和verification通过后创建一个完整本地结果提交。
- 变更范围：Server下Abstractions/Application/Infrastructure/Persistence/Protocol/Data、相关测试；protocol目录及本任务Document。
- Parent rollback required：no。无关Tool/WireSmoke/__pycache__/保持原样且不纳入提交。
- 本任务不发PR、不合并、不清理分支。工作分支和部署回滚输入保留。

## Artifacts

[来源、采用策略与实机清单](research-and-implementation.md)。新运行包`artifacts/altar-dungeons-20261013/runtime`，版本0.2.5-altar.20261013。默认全天开放、历史最佳未持久化及断线不补未结算收益均已明确，原生UI/多人高延迟待用户验收。
