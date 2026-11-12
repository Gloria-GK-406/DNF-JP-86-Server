---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 聊天单入口路由与第一批频道

## Delivered

- CMD `0x0011` 单入口按 mode 分流：区域 3/45/46、队伍 2/9（NOTI `0x000C`），私聊 1/7（NOTI `0x0041`），频道喇叭 13 与服务器喇叭 11/12/57（扣物品，NOTI `0x0076`），公会 6 静默，其余 mode 与 `0x01A9` 回拒绝。
- 1:1 对话窗口：`0x01A3` 建会话（双方 NOTI `0x016A`，名单 [对方, 自己]），mode 43 只投对方（NOTI `0x016F`，失败静默），`0x01A5`、断线、回选人、选角顶替结束会话并通知留守方（NOTI `0x016E`），`0x01A6` 接收忽略。
- 范围模型 `ChatScope` 封闭层级与单参数 `IChatScopeDomainService.MembersOf`；会话成员由 `ChatConversationDomainService` 内存保存。
- 协议记录与 catalog、编号用户测试流程 `Document/tasks/chat-routing/用户测试流程.md`。

## Verification

- `verification/completion-evidence.md`（decision: pass）

## Repository Result

- Changed paths: `Server/`（Data、Abstractions、Application、Protocol 与测试）、`protocol/`、`Document/tasks/chat-routing/`、本任务目录
- Spec baseline: `9712ecacc`
- Rollback: not applicable
- Parent rollback required: no

## Artifacts

- 结果评审：`reviews/change-review.md`（第一批）、`reviews/change-review-conversation.md`（1:1 对话）
- 逆向结论：`research/conversation-re-20261112.md`
- 客户端窗口标题补丁在 DNF-JP-86-ClientHack 分支 `task/conversation-title`，不属于本仓库
