---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：可组合广播作用域与附近角色频道隔离

## Delivered

- 新增消息主题无关的 `SessionPresenceScope`，以 flags 组合 channel、town、area、standing、shared 与
  exclude-self，全部组合按交集解释并严格校验锚点；空约束表示全服在线 presence。
- Presence 查询统一为 `Query(scope)`，返回稳定排序且保留 exact session identity 的当前记录；六条附近
  角色入口全部加入实际 listener、town、area、standing/shared 与 self exclusion。
- Notification audience 同时支持发送时动态 `Scoped` 和冻结 `TheseSessions`。exact audience 在解析、
  逐接收者重检与 retry 全程不追随 replacement，而 `TheseCharacters` 的既有 replacement 语义保持不变。
- 新人/AREA_USERS body、arrival/departure/position 及 cleanup 排除跨频道和 stale-generation 角色；需要
  同时形成 payload 与 audience 的路径使用同一筛选快照。

## Verification

- 完整结果 Round 2 独立复审为 `approved`，无剩余 P1/P2：
  [`change-review.md`](reviews/change-review.md)。
- 审查后新鲜完成验证为 `pass`，逐项覆盖 SC-1..SC-5 与 AC-001..AC-005：
  [`completion-evidence.md`](verification/completion-evidence.md)。
- Release build `0 warning / 0 error`；九项目逐 TRX 合计 `3113 Passed / 2 NotExecuted / 0 Failed`。

## Repository Result

- 接受前基线：`73a36166dfd2e6f2da85c8754a91ba724c2eab8f`。
- 本文件、验证证据及完整已审查 diff 由父 Multi Spec 控制器作为一个本地接受结果提交；无需父级回滚。

## Successor Guidance

- 公开“查找队伍”目录仍未实现。后继 Spec 应复用本次接受的 `SessionPresenceScope`，按频道 + 城镇 + 区域
  选择接收者，并独立闭合多队伍编码、创建/更新/移除差分与实际发布，不把 party 主题塞回 scope 模型。
- Story/Cycle 仍未研究；后继模式继续复用已接受的设置值对象、集中规则服务、exact session 与
  generation/revision 交付边界。

## Artifacts

- 核心 contract：`Server/src/Dfo.Server.Abstractions/Sessions/ISessionPresence.cs`、
  `Server/src/Dfo.Server.Abstractions/Notifications/INotificationServices.cs`。
- 核心实现：`Server/src/Dfo.Server.Application/Sessions/SessionPresenceDomainService.cs`、
  `Server/src/Dfo.Server.Application/Notifications/NotificationAudienceResolver.cs`、
  `Server/src/Dfo.Server.Application/Notifications/NotificationBus.cs`、
  `Server/src/Dfo.Server.Application/Notifications/NotificationRetryQueue.cs`。
