---
kind: rks-review
verdict: approved
---

# 完整改动独立审查

- 目标：隔离工作区相对于 `main` 基线 `dd691550eb91de8d8783bbf0fcd5e93ec15aa894` 的全部已跟踪差异、未跟踪源码/测试，以及任务契约、Spec、Plan、TASK-001～005 和 `Document/PartyKey-Dungeon-Membership-A21-Acceptance.md`。审查时目标尚未提交。
- 准则：`reviewing-work` 的 complete code 与 complete deliverable；重点对照 RQ-001～007、AC-001～007 的队伍身份、客户端 ID 换届、副本资格和个人掉落。

## 已复核的修复

### F-1：城镇移动抢占队长换届通知（已修复）

`PartyService.LeaveLocked` 在队长离队时同一修订先换客户端领导版本，并把旧队伍清除受众设为离队者和幸存者（`Server/src/Dfo.Server.Application/Parties/PartyService.cs:408-433`）。但通知在命令门释放后才投递。此间若幸存者的区域内移动触发 `PartyTownProjectionDelivery.RefreshMovedPartyAsync`，它从 PartyService 读到新修订和新客户端 ID（`Server/src/Dfo.Server.Protocol/Game/Party/PartyTownProjectionDelivery.cs:68-87`），向成员先发新 ID 的完整名册，并在同一队伍 stream 上设置 `LatestRevision = current.Revision`（同文件 `:138-140`）。之后离队通知进入 `PartyNotificationDelivery.DeliverAsync`，`revision <= stream.LatestRevision` 分支将整个换届当作过期通知（`Server/src/Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs:635-649`）；幸存者已在 `CurrentMembers` 中，因而不在 `staleClearAudience`，收不到旧 ID 的 type=3 清除。区域观察者的旧 ID 也可能保留。结果违反 AC-002 的“旧对象先退役，再建立新 ID”，并可能使幸存者同时持有旧/新队伍对象或错过保留队伍提示。

复核：修复后的 `PartyDeliveryStream.CurrentPartyId` 仅在完整队伍状态投递成功后推进（`PartyNotificationSlice.cs:662-665`）；城镇成员刷新在相同 generation 但客户端 ID 不同时退出，不提升修订水位（`PartyTownProjectionDelivery.cs:68-77`），观察者补发也跳过尚未完成的换届代际（同文件 `:199-213`）。新增 `PartyTownProjectionDeliveryTests.TownReadRepairCannotSupersedePendingLeaderRekeyClearForSurvivor` 按“旧投影→队长离队→城镇补发→换届通知”的交错断言幸存者旧 ID clear 先于新 ID full roster；生产修复前该测试为红，修复后为绿（修复及 red/green 执行由生产者报告；本审查静态复核代码与测试，未将其代替完成验证）。当前差异中 F-1 不再阻断。

## 结论

相对于上述基线的完整代码和交接文档在本次独立审查中没有剩余的阻断缺陷；`approved` 只表示当前改动可进入新鲜完成验证，不代表 A21 客户端效果已实机确认。

## 非阻断边界

- `PartyClientIdRegistry` 历史 `(PartyKey, LeadershipVersion)` 编号不回收，进程累计分配到 `ushort` 空间上限会明确失败（`Server/src/Dfo.Server.Protocol/Game/Party/PartyClientProjectionService.cs:12-36`）。TASK-002/TASK-005 已如实记录为长期容量风险；当前可测重构的身份语义不因之失效。
- A21 双客户端尚未执行，验收文档明确区分静态/自动化与客户端观察，没有虚称 `ClientVerified`。因此本次审查不能替代客户端效果确认。
