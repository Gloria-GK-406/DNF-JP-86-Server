---
kind: rks-verification
decision: manual_required
---

# 完成证据：玩家自助后台（#329）与受信代理（#305）

- 结论对象：TaskContract revision 2 的 SC-1–SC-13
- 分支：`task/player-self-service-portal-329`，提交 `0e1cecb31`（基线 `20bcaf37c`）
- 审查结果：
  - `reviews/drift-review.md`：approved，aligned
  - `reviews/change-review.md`：第 2 轮 approved，并确认了 remediation 2

## 声明

服务端、迁移与前端的实现均已完成，自动化证据和 agent 测试服上的浏览器实测覆盖了下表中标为"有证据"的条件。

**本次不声称以下两项已通过**，留待用户实机测试：

- 在线角色的实机收信；
- 用户测试服的就绪与用户验收。

## 本次运行的检查

| 检查 | 结果 | 证据 |
|---|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告 0 错误 | remediation 2 之后重跑 |
| 全量 `dotnet test`（设 `PVF_ARCHIVE_PATH`，在 `10bc4fd5e` 上运行） | 9 个测试项目共 11,426 例：通过 11,420，失败 3，跳过 3 | `verification/final/dotnet-test.log`；TRX 在 scratchpad 的 `final-trx/` |
| 失败 1：`LockRemovalSourceTests` | 本分支引入；remediation 2 已修复，修复后 18/18 通过 | `reviews/remediation-2.md` |
| 失败 2：`MirrorDungeonEntryTests(boundary: "quota")` | 分支未改动相关代码（相对 `20bcaf37c` 不涉及 World、Mirror、日切）。用例按北京时间自然日写额度，服务按 06:00 日切查询，北京时间 00:00–06:00 必然失败；本次在 04:1x 运行 | TASK-07 Result；`reviews/change-review.md` |
| 失败 3：`OutboundRealSocketTests(unreadInput: True)` | 分支未改动 Networking；同一组单独连跑 3 次 13/13 通过，属全量并行负载下的真实 socket 偶发失败 | 本次复跑输出 |
| remediation 2 之后的定向回归 | Host `~PlayerPortal` 49/49；`LockRemovalSourceTests` 18/18 | `reviews/remediation-2.md` |
| `Web/gm-admin` `npm test` | 16 个文件 195/195 通过 | `verification/final/npm-test.log` |
| `npm run build` | 通过，仅有原本就有的 chunk 体积警告 | remediation 1 记录 |
| 跳过的 3 例 | Infrastructure 2 例：有 PVF 时按设计跳过的"缺档案"用例；Host 1 例：Windows 上没有 SIGTERM | 日志 |

## 逐条对应

| SC | 结论 | 决定性证据 |
|---|---|---|
| SC-1 登录、登出、错误密码、关闭时拒绝 | 有证据 | `PlayerPortalAuthenticationHttpTests`（TASK-03）；浏览器 `verification/browser/01-player-account.jpg` |
| SC-2 只见自己的数据，越权被拒且不泄露存在性 | 有证据 | `PlayerPortalBoundaryHttpTests`、`PlayerPortalMailHttpTests`（三种归属失败响应逐字相同，TASK-05） |
| SC-3 两类会话互不串用 | 有证据 | `PlayerPortalBoundaryHttpTests`：动态枚举两侧全部非公开路由，含 GM 物品搜索、详情、图标（TASK-03 审查） |
| SC-4 加点券、重放、记录 | 有证据 | `PlayerPortalCeraHttpTests`、`PlayerPortalRecordsHttpTests`（TASK-04）；浏览器 `02-cera-granted.jpg`（955,300 → 1,055,300）、`04-records.png` |
| SC-5 发信、在线收信、拒绝装备选项、记录 | 部分有证据 | 服务端：`PlayerPortalMailHttpTests`（含在线提醒路径与真实 PVF 用例，TASK-05 审查）。浏览器：`03-mail-sent.jpg`；库中核对邮件 97 已写入角色 2 的邮箱（物品 ×1、金币 500、30 天有效）。**在线角色的游戏内实机收信未验证**，见下文 |
| SC-6 总开关、子开关、重开、会话、踢人、新库默认关闭 | 有证据 | `GmPlayerPortalHttpTests`、`PlayerPortalCeraHttpTests`、`PlayerPortalMailHttpTests`、迁移测试。浏览器：关闭后旧会话失效、重开后可再登录；已打开的玩家页切页后进入已关闭页（`05-portal-closed.jpg`）；关闭时登录页无玩家选项（`00-login-closed-admin-only.jpg`） |
| SC-7 限额边界、0 表示不限 | 有证据 | `PlayerPortalCeraRuleTests`、`PlayerPortalMailRuleTests`、Host 限额用例（TASK-04/05，含变异核对） |
| SC-8 审计与玩家自助筛选 | 有证据 | `GmAuditActorRulesTests`、`GmPlayerPortalHttpTests`；浏览器 `06-audit-player-filter.jpg`（4 条玩家记录带"玩家"标记与账号）；概览页只取管理员记录（remediation 1 的前端测试） |
| SC-9 受信代理（#305）全部验收，两侧 | 有证据 | `GmTrustedProxyHttpTests`、`GmWebOptionsTests`、`GateHttpHostTests`（TASK-02）；玩家侧 AC-031/032（TASK-03） |
| SC-10 前端页面与手机宽度 | 有证据 | 前端 195 例；浏览器 `07-mobile-account.jpg`、`08-mobile-mail.jpg`（375px，scrollWidth 等于 clientWidth，无横向滚动） |
| SC-11 GM 原有功能不变、零警告 | 有证据（附说明） | 零警告构建；GM 相关测试全部通过；全量中两例非本分支失败已如上归因；GM 概览回归已由 remediation 1 修复 |
| SC-12 用当前主干库准备用户测试并交付 | 未完成 | 已完成：用户副本 `artifacts/player-portal-329/db/user/dfo-server.db`（主干 Release 库的 backup 副本，已迁到 v7，账号 4、角色 9，与原库一致，原库未变，TASK-07）；启动脚本 `run-user-server.sh`；流程文档 `Document/tasks/player-self-service-portal/用户测试流程.md`。**测试服尚未启动**：UDP 12311/12312 被另一会话的 41xxx 测试服占用，需要用户决定 |
| SC-13 迁移结构一致、默认关闭、审计归 gm | 有证据 | `Tool/DbMigrate.Tests` 30/30，含结构一致性与 `PlayerPortalMigrationTests`；两份主干库副本实际迁移结果（TASK-07） |

## 共享决定的落地差异（勘误）

- SD-8a 的玩家物品目录实为五条路由。在原定三条之外，经主控裁定补入了 `items/icon-pack` 与 `items/{id}/tooltip`。
- SD-1a 的错误码在原定之外新增了两个：
  - `forbidden`（403）：边界拒绝时返回；
  - `rate_limited`（429）：除登录外表示超出每账号 2 个在途请求的上限，登录时仍表示失败次数限流（remediation 1）。

## Unverified Conditions

- **SC-5 / AC-011 在线部分**：在线角色在游戏客户端里实际收到玩家邮件，需要用户按测试流程第 8 步实机验证。
- **SC-12 / AC-061**：用户测试服未启动，用户尚未验收。启动需先处理 UDP 端口占用；启动后要填写流程文档的"就绪情况"和交付提交号。
- **合并前**：分支落后 `origin/main` 17 个提交，主干已用结构版本 7 与迁移步骤 `[6]` 承载拍卖行表。本分支变基时需把玩家后台迁移顺延为版本 8、步骤 `[7]`，并重新验证。
