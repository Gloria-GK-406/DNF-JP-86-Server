---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 移除会话登录快照

## Planning Choice

使用 direct：核心改动是一个运行期状态查询边界，所有消费者应沿同一接口迁移；保持一份详细计划和一次完整集成验证比拆成可独立交付的单元更能防止形成两个并存真源。

## Approach and Shared Decisions

保留 `SelectedSessionIdentity` 和发布标记。运行期需要完整协议投影时，以精确代际作门，再从角色、物品、账号、任务/世界内容及频道规则构造一次性的当前视图。查询不得执行选角特有的恢复或初始化副作用。只需要单一事实的消费者直接问对应领域，避免为经验加成、状态比率等反复加载整份视图。异步持久化读取必须向上传播异步，不阻塞同步路径。

## Work and Dependencies

| ID | Covers | Executable work and check |
|---|---|---|
| T-001 | RQ-001,RQ-004 | 写目录代际/发布测试；把目录缩到精确代际和持久化标记，调整生命周期接口。 |
| T-002 | RQ-002,RQ-003 | 建立无选角副作用的当前视图查询；验证物品从当前仓储加载、旧代际拒绝。 |
| T-003 | RQ-002,RQ-003 | 将城镇、副本、组队、角色详情及任务/结算消费者逐类迁移；删除 ReplaceExact 链。每类运行相关测试。 |
| T-004 | RQ-003,RQ-005 | 经验、技能和高级道具等仅需局部事实者改为领域查询；清除手动同步和无用租约脏写回。 |
| T-005 | RQ-001-RQ-005 | 更新 AGENTS/相关文档，审计无旧快照引用，构建和测试，独立完整结果审查。 |

## Verification

先用针对目录与耐久投影的红绿测试驱动边界，再检查所有生产 `ISelectedSessionRuntimeDirectory` 消费者、全部解决方案构建和相关测试。客户端实机复验留给部署后，不提前声称已通过。
