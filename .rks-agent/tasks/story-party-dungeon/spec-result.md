---
kind: rks-spec-result
contract:
  path: ./TaskContract.md
  revision: 1
spec: ./spec.md
state: accepted
---

# Spec Result: 固定队伍共享剧情地下城首发入场

## Outcome

规格已实现并通过独立代码复审与自动化验证，达到 A21 双客户端实机测试条件。当前结果仍是服务端实现，不把尚未抓包确认的队员首发投递顺序声明为协议事实。

## Acceptance

| Criterion | Result | Evidence |
|---|---|---|
| AC-001 | Pass | 队长、非队长、成员不可附着、共享 RunId 与冻结成员测试通过。 |
| AC-002 | Pass | Directed 剧情模式取队长任务；Interactive 普通模式保持显式普通选择。 |
| AC-003 | Pass | 队伍首发投影、异常/取消/最终 generation 变化整体回滚测试通过。 |
| AC-004 | Pass | exact RunId/RoomVersion、队长权限、相邻移动及双端同步测试通过。 |
| AC-005 | Pass | 死亡 claim/commit、逐参与者 durable effect 选择性重试与固定掉落测试通过。 |
| AC-006 | Pass | 地面物品 reserve/commit/release、失败重试、成员竞争与离场提交屏障测试通过。 |
| AC-007 | Pass | 逐成员结算、任务、奖励卡、翻牌与独立离场测试通过。 |
| AC-008 | Pass | Release 全套 3243 Passed、0 Failed、2 NotExecuted。 |
| AC-009 | Pass | 冻结成员、连接代际拒绝、断线与三条 exact End 收口测试通过。 |

## Residual Validation

A21 双客户端仍需按 `Document/StoryPartyDungeon-20260914.md` 执行实机流程。若投递顺序与客户端状态机不兼容，应只调整 Protocol 编排，不回退共享局与逐参与者领域边界。
