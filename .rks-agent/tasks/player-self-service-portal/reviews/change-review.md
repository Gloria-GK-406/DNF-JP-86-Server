---
kind: rks-review
verdict: changes_required
---

# 完整结果审查：玩家自助后台（#329）与受信代理（#305）

```yaml
kind: rks-review
verdict: changes_required
```

## 审查对象、范围与依据

- 目标：分支 `task/player-self-service-portal-329`，HEAD `0fd7b39d1`，`git diff 20bcaf37c HEAD`（109 个文件：服务端、`Tool/DbMigrate`、`Web/gm-admin`、`Document/`）。未跟踪的 `verification/browser/` 9 张截图作为 AC-041 证据。
- 依据：`TaskContract.md` revision 2（SC-1–SC-13）、`spec.md`（RQ/AC）、`plan.md`、`reviews/drift-review.md`（approved / aligned）、`reviews/change-challenges.md`（CH-1–CH-6）、`tasks/TASK-01`–`TASK-07`。
- 准则：reviewing-work 的 Code 与 Deliverable 准则（混合交付：代码 + 运维/用户测试文档）。
- 审查者独立于计划、实现、漂移审查与挑战生成。只读；除本文件外未改动任何文件，未 commit，未启停进程，未读取 `agent-credentials.env`。

## 本审查独立运行的检查

| 检查 | 结果 |
|---|---|
| `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "PlayerPortal\|TrustedProxy\|GmWebOptions\|GmGameplayRoute\|GateHttpHost"`（`PVF_ARCHIVE_PATH` 指向 protocol 客户端 PVF，只读） | 95/95 通过 |
| `dotnet test Tool/DbMigrate.Tests` | 30/30 通过（含结构一致性与 v6→v7 迁移用例） |
| `Web/gm-admin`：`npx vitest run` | 第 1 轮与 dotnet 测试并发运行：191/192，`items.test.ts` 的 `afterEach` 钩子超时（10 s，耗时 22.5 s，整轮 85 s）；第 2 轮单独运行：192/192 通过（18.6 s）。判为负载下的钩子超时，`items.test.ts` 不在本分支改动内，不阻塞，但与 TASK-07 处理过的 DailyRefill 同属一类，记为残余风险 |

全量 `dotnet test` 与零警告构建沿用 TASK-07 Result 的记录（11424 例，唯一失败为 `MirrorDungeonEntryTests` 在北京时间 00:00–06:00 必然失败，本分支未触及相关代码；本审查未重跑全量）。

## 挑战裁定

| ID | 裁定 | 是否需修 | 说明 |
|---|---|---|---|
| CH-1 | confirmed（机制成立）→ downgraded 为残余风险 | 本关不阻塞；PR / 线上开启前必须处置 | 见下 |
| CH-2 | confirmed → downgraded | 建议同批补一句文档（非阻塞） | 见下 |
| CH-3 | **confirmed** | **需修（阻塞）** | 违反 SC-11 / RQ-051，见 F-1 |
| CH-4 | **confirmed** | **需修（阻塞交付用户）** | 影响 SC-12 / AC-061，见 F-2 |
| CH-5 | downgraded | 不需修 | 见下 |
| CH-6 | confirmed（机制成立）→ downgraded | 不需修（可选加固） | 见下 |

### CH-1：玩家请求无频率 / 在途限制可拖慢串行执行器

- 事实核对成立：`PlayerPortalEndpoints.cs` 中 `logout`、`me`、`cera/grant`、`mail/send`、`operations` 都经 `PlayerRouteMapping.Map` 注册为执行器命令；`GameplayExecutor` 是无界 FIFO（`CreateUnbounded`）；`HttpGameplay.RunAsync` 以 `CancellationToken.None` 入队；玩家侧除登录外没有并发或频率上限；限额默认 0 且只约束成功量。
- 不判为"必须修复的安全缺陷"，理由：
  1. 契约与 Spec 没有任何吞吐 / 频率要求，这不是对已批准要求的违反；Spec 的 Accepted Risks 只覆盖了登录侧。
  2. 攻击者必须持有有效游戏账号与有效玩家会话（身份可识别），写操作全部入审计；管理员可单账号踢下线或一键关闭总开关止损（RQ-021、RQ-023 已实现并有测试）。
  3. 功能默认关闭（D1），本轮交付目标是本地用户测试，不涉及线上开放。
- 但这是一条**未登记**的、在线上开放后真实存在的可用性风险（HTTP 可以高并发，`/me` 读、写操作每次都占执行器）。处置要求（不阻塞本关，但在建 PR 或线上开启前必须完成其一）：
  - 最小代码修正：在 `GmAccessBoundary` 玩家分支识别出身份后、入队前，加按账号的在途上限（例如每账号同时 1 个写请求 + 少量读请求，超出回 429 `rate_limited`，前端已有该 code），附一条 Host 测试；或
  - 写入 Spec Accepted Risks 并在交付时请用户裁定。

### CH-2：线上未配 `GmWeb:TrustedProxies` 就开启玩家后台会重现 #305 锁死

- 行为本身符合契约：SC-9 / RQ-030 明确要求"未配置时行为与现在相同"；#305 的修复以配置受信代理为前提。修改线上 Caddy 与部署在 Scope Out 5。
- 缺口在运维文档：`Document/gm-web-admin.md`「访问与审计」写了 `TrustedProxies` 的语义，但「玩家自助后台」没有写"经反向代理对外开放前必须先配置受信代理，地址族要与代理实际连入地址一致"。Caddy 对 `/api/player/*` 的 `Origin` 处理属于部署核对项，无法在本仓验证。
- 建议：在「玩家自助后台」节首加一句前置条件，并在交付给用户的说明中把"线上开启前核对 TrustedProxies 与 Caddy 对 `/api/player/*` 的 Origin 处理"列为部署前提。非阻塞，可与 F-1/F-2 同批完成。

### CH-5：玩家"结果未确认"的原编号只在内存

- 机制成立，但实际后果不构成缺陷：加点券与发信对象都是玩家自己的账号与角色，玩家本来就可以随时再次提交同样的操作；设置了每日限额时重复的那笔会被限额挡住。`gm-web-admin.md` 已如实写明"保存在玩家界面这一层"。Spec 与 AC-040 未要求跨刷新保留。不需修。

### CH-6：受信代理转发解析不了的 `X-Forwarded-For` 时回落为回环

- 机制成立（`ForwardedHeadersMiddleware` 遇到第一条不可解析值即停止，不改 `RemoteIpAddress` 与 `Scheme`，`SecureTransport` 视回环为安全）。
- 前提是受信代理把客户端可控的值透传；Caddy 默认用真实对端地址改写该头，文档也要求代理"改写（而不是透传）"。攻击者能得到的只是绕过 HTTPS 规则（其连接本来就经代理）与共用限流键。按当前部署形态不可触发，降为可选加固，不需修。

## 阻塞性发现

### F-1（CH-3）：GM 概览页被玩家审计记录混入，GM 原有功能结果改变（SC-11 / RQ-051）

- 证据：`Web/gm-admin/src/OverviewView.tsx:39-40` 两次审计查询（`outcome=attention&pageSize=100` 与 `pageSize=20`）都不带 `actor`；本分支后不带 `actor` 时返回全部操作者（`SqliteGmAdministrationPersistence.QueryAuditAsync`）。`PlayerPortalAudit.RejectedLogin` 让每次玩家登录失败都写一条 `Rejected`，玩家的超限额、子开关关闭等拒绝也写 `Rejected`。概览明细行（`:107`）只拼 `administratorName`，没有"玩家"标记（审计页已有 `AuditActor`，概览页未用）。
- 后果：
  1. 玩家正常输错密码就会进入 GM 首页"需要处理"，常驻"N 条拒绝 / 回滚待你看一眼"——这是 GM 原有功能含义的改变，不属于 RQ-051 允许的"新增字段与筛选值"增量。
  2. "已读"只是本浏览器的客户端过滤，服务端窗口仍是最近 100 条；玩家拒绝记录累积超过 100 条后，管理员自己的 `GrantCera` / `SendOfficialMail` `Unknown` 记录会被挤出窗口。在另一台浏览器值班、没有本地待确认项的管理员看不到未确认发放，可能重发，造成重复发放——削弱了既有的防重复安全机制。
  3. 玩家与管理员同名时，概览明细无法区分。
- 最小修正：`OverviewView.tsx` 两次审计查询加 `actor=gm`（服务端已支持，迁移前记录为 `gm`）；补一条前端测试固定"概览只取管理员记录"。若希望在概览中也看到玩家问题，改为单独分组并使用 `AuditActor` 标记，但不能与 GM 记录共用 100 条窗口。修正后需重新发布 `artifacts/player-portal-329/host` 并更新测试流程中的提交号。

### F-2（CH-4）：用户测试流程在同一浏览器中无法执行第 6、10、11、12 步（SC-12 / AC-061）

- 证据：`Portal.tsx` 的设计是有效玩家会话优先于管理员会话（`:10-12`、`probe()`），`gm-web-admin.md` 也如此写明。`Document/tasks/player-self-service-portal/用户测试流程.md` 第 6、10、11、12 步要求"玩家保持登录时管理员去改限额 / 子开关 / 总开关 / 踢人"，全文只给一个地址 `http://localhost:43081/gm/`，没有"另用一个浏览器或无痕窗口"的说明。第 13 行只说明了与主干 `127.0.0.1` 分开的原因。
- 后果：用户按文档在同一浏览器操作，玩家登录后打开 `/gm/` 只会看到玩家界面，第 11 步"玩家保持登录、管理员关闭总开关"无法完成，用户会卡住或误报"找不到管理台"。若用户自行改用 `127.0.0.1:43081`，`Dfo.Gm.Session` 会与主干 `127.0.0.1:18081` 互相覆盖（Cookie 不分端口）。
- 最小修正：在测试流程「前置条件」写明"管理员操作用另一个浏览器或无痕窗口，地址同样用 `localhost:43081`，不要用 `127.0.0.1`"，并在第 6、10、11、12 步标注各动作在哪个窗口执行。纯文档修改。

## SC / AC 满足情况

"已有证据"指本审查核对过的代码、测试或截图；"待验证"指尚欠的计划内产出。

| 条件 | 判断 | 证据 / 欠缺 |
|---|---|---|
| SC-1（AC-001、AC-002） | 已有证据 | `PlayerPortalAuthenticationHttpTests`；截图 00、01 |
| SC-2（AC-004、AC-005） | 已有证据 | `PlayerPortalBoundaryHttpTests`、`PlayerPortalMailHttpTests`（三种收件人拒绝回答一致） |
| SC-3（AC-003） | 已有证据 | 双向动态枚举路由的边界用例 |
| SC-4（AC-010、AC-012、AC-013） | 已有证据 | `PlayerPortalCeraHttpTests`、`PlayerPortalRecordsHttpTests`；截图 02、04 |
| SC-5（AC-011） | 自动化部分已有证据；**在线角色实机收信待用户验证** | `PlayerPortalMailHttpTests`；库中核对邮件 97 写入角色 2 邮箱（离线写入）。按 R-5 如实标注"待用户验证" |
| SC-6（AC-020、AC-021、AC-023、AC-008） | 已有证据 | Host 测试；截图 05（已关闭页） |
| SC-7（AC-022） | 已有证据 | 限额边界用例 |
| SC-8（AC-024） | 后端已有证据；**F-1 需修** | 审计页"玩家自助"筛选截图 06；GM 概览页受影响 |
| SC-9（AC-030–AC-034） | 已有证据 | `GmTrustedProxyHttpTests`、`GmWebOptionsTests`、`GateHttpHostTests`；CH-6 为可选加固 |
| SC-10（AC-040、AC-041） | 已有证据 | 前端 192/192；截图 01–08（电脑宽度全流程与 390px 账号、发信页） |
| SC-11（AC-051、RQ-051） | **不满足，需修 F-1** | 构建零警告与全量测试沿用 TASK-07 记录（MirrorDungeon 时刻依赖失败已如实归因，非本分支引入）；GM 概览行为改变 |
| SC-12（AC-060、AC-061） | AC-060 已有证据；**AC-061 文档需修 F-2；测试服未启动、未交付用户** | 用户副本迁移、计数、哈希记录在 TASK-07 与流程文档；测试服因 UDP 12311/12312 被其它会话占用未启动，需用户决定；流程文档"就绪情况"待主控填写 |
| SC-13（AC-050） | 已有证据 | DbMigrate 30/30；两份副本 `user_version=7`、`integrity_check=ok` |

### 尚欠的计划内产出（不是缺陷，但声称完成前必须补齐）

1. `verification/completion-evidence.md` 尚不存在（Spec EV-001 / EV-002 指定的位置）；需汇总自动化、浏览器与 AC-060 证据，并按 drift-review N-2 记一句"SD-8a 实为五条路由、SD-1a 增加 `forbidden`"。
2. 用户测试服启动与交付：等待用户裁定 UDP 端口（停掉占用的测试服，或在用户脚本中关闭 UDP 探测）；启动后填写流程文档"就绪情况"。
3. 在线收信（SC-5）与全部用户步骤以用户反馈为准；用户反馈前不声称实机验收通过。
4. F-1 修正后需重新发布 host 并更新流程文档中的提交号。

## 非阻塞残余风险

- CH-1：在 PR / 线上开启前加按账号在途上限，或登记为 Accepted Risk 并由用户裁定。
- CH-2：建议在 `gm-web-admin.md`「玩家自助后台」补一句受信代理前置条件；线上部署核对 Caddy 对 `/api/player/*` 的 `Origin` 处理。
- 前端测试在高负载下有钩子超时（本次 `items.test.ts`），单独运行稳定通过。
