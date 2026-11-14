---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 玩家自助后台（#329）与受信代理（#305）

## Delivered

- **玩家身份**：玩家用游戏账号登录 `/gm/` 的玩家入口，会话独立（`/api/player/v1`、`Dfo.Player.Session`）。玩家与 GM 两类会话互不串用，玩家只能看到自己的账号和角色；越权访问被拒，响应不泄露对象是否存在。
- **自助操作**：
  - 给自己加点券 / 代币券；
  - 给自己的角色发官方邮件（物品与 GM 共用 `OfficialMailDelivery`，不开放装备高级选项）；
  - 查看自己的操作记录。
  - 写操作按"意图 → 总开关 → 会话 → 重放 → 子开关 → 归属 → 限额 → 写入"裁定，带幂等重放，每账号最多 2 个在途请求。
- **管理员控制**：
  - 总开关、点券与邮件子开关；
  - 每日限额（0 表示不限）、会话列表与踢下线。
  - 新库默认关闭。审计按操作者区分 gm 与玩家，概览只取管理员记录。
- **#305**：`GmWeb:TrustedProxies` 只在 GmWeb 端口启用转发头，安全连接按解析后的值判定；GM 与玩家登录限流分开。
- **数据库**：schema v10，附 DbMigrate 第 `[9]` 步（变基后由 v8 顺延）。
- **前端**：登录切换、玩家三页、已关闭页、管理员「玩家后台」页，以及审计的"玩家自助"筛选。

## Verification

- `verification/completion-evidence.md`：除用户实测外的条件都有证据。用户实测只覆盖 happy path（发邮件、加点券），其余未覆盖项已经用户裁定接受。

## Repository Result

- 变更的路径：服务端仓 `task/player-self-service-portal-329` 相对 `origin/main` `dbeba6aa7` 的全部提交。
- Spec baseline：`20bcaf37c7282cf06efe767fb3b0a992364f9653`（变基前），变基后基于 `dbeba6aa7`
- 回滚：不适用
- 是否需要父级回滚：否

## Artifacts

- 契约与评审：`TaskContract.md`（r2）、`task-contract-review.md`
- 审查：`reviews/drift-review.md`、`reviews/change-review.md`、`reviews/remediation-1.md`、`reviews/remediation-2.md`、`reviews/rebase-review.md`
- 用户测试与结果：`Document/tasks/player-self-service-portal/用户测试流程.md`
