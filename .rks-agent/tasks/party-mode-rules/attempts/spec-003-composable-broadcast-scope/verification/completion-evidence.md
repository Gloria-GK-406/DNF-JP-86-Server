---
kind: rks-verification
decision: pass
---

# Completion Verification：可组合广播作用域与附近角色频道隔离

## Claim

当前完整结果满足子 TaskContract revision 1 的 SC-1..SC-5 以及 Spec AC-001..AC-005：服务端具备可组合、
严格校验的在线会话/通知作用域；六条附近角色路径均按实际 Game listener、town、area、站立与共享状态取
交集；需要冻结快照的通知保留 exact `SelectedSessionIdentity`，不会把旧频道事件转投给 replacement
session。结果保持 `TheseCharacters` 与 Party 的既有语义，未扩大到公开队伍目录或 Story/Cycle。

## Conditions and Decisive Evidence

- SC-1 / AC-001：`SessionPresenceScopeTests` 覆盖 Global、独立 channel/town、town+area、standing、shared、
  exclude-self 及合法交集；未知 flags、缺失或多余 listener/town/area、area 未带 town、非法端口均确定性
  拒绝。
- SC-2 / AC-002：多 listener、多 town/area、站立/非站立、共享/私有和 replacement generation 数据集
  证明 `Query(scope)` 只返回全部约束同时成立的 presence，按 `CharacterId` 稳定排序，并保留匹配记录的
  exact session identity。生产源码旧 `.InArea(` 调用为 0；`VisibleTownArea(...)` 定义之外恰有六个生产
  调用点。
- SC-3 / AC-003：audience、delivery 与 retry 回归证明 `Scoped` 只解析 presence 与当前 selected session
  一致的收件者；`TheseSessions` 在解析、逐接收者重检和重试时都不追随 replacement，包括 connection ID
  相同但 generation/listener 已变化的情况。`TheseCharacters` 仍保持输入顺序、重复项、离线忽略以及跟随
  当前会话/重连重试的既有语义。
- SC-4 / AC-004：SessionAdjacent、Town publication、SetUserArea、SetUserPosition 与 cleanup 行为测试覆盖
  六条生产路径的同频道正例、跨频道同 town/area 反例和 stale-generation 反例；跨频道或旧会话既不进入
  新人/AREA_USERS body，也不收到 arrival/departure/position，冻结 body 与 exact audience 来自同一筛选
  快照。
- SC-5 / AC-005：Round 2 独立完整结果审查为 `approved`，首轮 exact-session replacement P1 已关闭且无
  新 P1/P2：[`change-review.md`](../reviews/change-review.md)。审查后 fresh Release solution build 为
  `0 warning / 0 error`；设置真实 `PVF_ARCHIVE_PATH` 后串行运行九个 Release 测试项目，并逐项解析每个
  TRX `UnitTestResult`，合计 `3113 Passed / 2 NotExecuted / 0 Failed`。两项 NotExecuted 是提供真实 PVF
  时按设计跳过的缺失档案反例。
- 分层与边界：Application 项目只引用 Abstractions 与 Data，没有 Infrastructure `ProjectReference`；
  接受基线到当前的 Party 生产/测试目录 diff 为 0；staged/unstaged `git diff --check` 均通过。

## Preserved Boundaries

公开“查找队伍”目录的多队伍编码、创建/更新/移除 diff 与实际发布，具体全服/频道业务消息、Story/Cycle、
P2P、战斗及奖励仍是后继或明确 Out；本次 pass 不把这些能力升级为已实现。
