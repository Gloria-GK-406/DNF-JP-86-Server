---
kind: rks-verification
decision: manual_required
---

# Party accept clear verification

## Claim

新成员接受组队邀请时，服务端先仅向接受者投递 `PARTY_INFO type=3` 清窗，再按既有顺序投递实时状态、接受回执、P2P 端点、完整名册和实时状态重放。实现与自动化验证已完成；是否消除 A21 客户端“连接中”超时仍需双客户端实机确认。

## Automated evidence

- TDD RED：`AcceptCreatesStableSlotsAndConsumesTheInviteOnlyOnce` 在基线返回空 `ClearAudience`，预期 `[invitee]`，如期失败。
- TDD RED：`Formation_is_published_in_cross_recipient_phases` 在基线拒绝 acceptance 与 clear 组合，如期失败。
- focused GREEN：上述接受结果测试 1/1；投递顺序及非法清窗受众测试 2/2。
- relevant regression：Application Parties/OnlineUsers 50/50；Protocol Party 89/89。
- fresh full Debug TRX：`C:\Users\Gloria\AppData\Local\Temp\dnf-party-clear-rerun-debug-7d6ee34b72e244ed9adf466bf941da90`，9 个 TRX，3247 Passed、0 Failed、2 NotExecuted。
- Release build：0 warning、0 error。
- `git diff --check`：通过。

首次全套运行中，通用通知 50ms 截止测试被运行机调度延迟到 2.598 秒；该用例独立连续复跑 5/5 通过，随后新的完整串行全套 0 Failed。没有为该非组队抖动修改生产代码。

## Unverified Conditions

- A21 双客户端接受邀请后是否立即完成 P2P 连接并保持队伍。
- 两端进入剧情地下城选择区及共享副本是否不再因该连接超时被自动退队。
