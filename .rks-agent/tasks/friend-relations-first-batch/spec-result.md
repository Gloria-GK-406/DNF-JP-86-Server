---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 角色关系系统与好友第一批（好友 + 黑名单）

## Delivered

- **统一的角色关系领域服务**：好友、黑名单两种 kind；只存角色 ID；被动、不推送；读写接口分离。结构版本 v5，配有 4→5 迁移。
- **好友协议**：`0x0122`/`0x0123`；NOTI `0x0111` 的全量与删除；NOTI `0x0112` 上下线。登录时发真实列表，紧跟着发黑名单列表。
- **黑名单协议**：`0x0076`–`0x0078`。生效范围是第一批的全部聊天频道，以及 1:1 建会话（双向拒绝，回结果码 77）。
- **在线推送**：上线、三个下线出口、升级、转职／觉醒、删角色，只发给观察者。
- **按名查找**：全服统一由角色领域负责，邮件、私聊、1:1、好友、黑名单共用。
- **客户端证据与文档**：A21 客户端证据与协议页、catalog；用户测试流程。

## Verification

- `verification/completion-evidence.md`（decision: pass）

## Repository Result

- Changed paths: `17eb734d4..HEAD`，分支 `feature/60-friend-relations`，worktree `E:\Project\DNF\DNF-JP-86-Server-friends`。未推送，未建 PR。
- Spec baseline: `17eb734d48d4698f466285df002430b19db1f0ae`
- Rollback: not applicable
- Parent rollback required: no

## Artifacts

- `reviews/change-review.md`：完整结果审查，Round 1 为 changes_required，Round 2 为 approved。
- `research/client-evidence.md`：A21 客户端静态证据。
- `Document/tasks/friend-relations/用户测试流程.md`

## Successor Guidance

以下是留给 #60 后续批次的事项，不属于本 Spec：

- 频道跟随 `0x0323`。用户实测后主动提起，下一批优先做。
- 最近联系人 `0x02A3`、师徒：在关系服务里新增 kind 即可，不要另建 owner。
- 黑名单接入组队邀请、交易、决斗、邮件。
- 审查中的非阻塞项：
  - N1：上线时读取观察者失败，会让已经入场的选角失败；
  - N2：升级、转职推送可以考虑改用 subcmd 3；
  - N4：删角色时不刷新拉黑了他的人；
  - N5：以后新增的删角色入口必须调用 `PurgeAsync`；
  - N10：私聊、1:1 改为直接注入身份查询服务。
- 合并前核对主干的结构版本号。本分支占用了 v5，如果主干同期也升了版本，需要按 `Document/DatabaseSchemaMigration.md` 重新排定版本号。
