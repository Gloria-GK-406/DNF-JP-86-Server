# 对抗性挑战：玩家自助后台（#329）与受信代理（#305）完整交付

- 目标：`git diff 20bcaf37c 0fd7b39d1`（分支 `task/player-self-service-portal-329`，工作区 `E:\Project\DNF\DNF-JP-86-Server-player-portal`）：服务端 `Server/`、迁移 `Tool/DbMigrate`、前端 `Web/gm-admin`、文档 `Document/`。
- 依据：`TaskContract.md` rev 2（SC-1–SC-13）、`spec.md`（RQ/AC）、`plan.md`（SD-1–SD-10）、`tasks/TASK-01~07.md`。
- 攻击范围：越权与会话串用、存在性泄露、开关竞态、重放与限额、资产多发、受信代理头与 HTTPS 判断、迁移与回滚、GM 原有功能回归、前端错误处理、部署默认值、文档与实现是否一致。
- 已运行的探针：`dotnet test Server/tests/Dfo.Server.Host.Tests --filter "PlayerPortal|TrustedProxy|GmWebOptions|GmPlayerPortal"`，76/76 通过（输出放在 scratchpad，没有写进工作区）。下面的挑战都不是这些测试能发现的问题。
- 查过、没发现问题的部分（不列为挑战）：前缀与 Cookie 隔离（GM 前缀只认 `Dfo.Gm.Session`，玩家前缀只认 `Dfo.Player.Session`，`Path=/api/player`；防伪令牌绑定 `player:` 前缀的身份）；跨类型编号复用会被拒为 `request_conflict`；收件角色"不存在 / 已删除 / 不是自己的"三种情况回答相同；关闭总开关时在同一事务里删除全部会话；`HttpRouteOperation` 的新值追加在末尾，`GmRouteReplyMapper` 原有的范围分支不会误吞新值；HTTP 功能端口互斥（`HttpApiHostingExtensions` 强制），转发头不会作用到网关端口；`OfficialMailDelivery` 与原有服务一样注册为单例，GM 邮件的循环体是原样抽出的；迁移只做加表、加列，旧审计记录取默认值 `gm`。

## CH-1：已登录玩家可以不受限地往游戏串行执行器里灌命令，拖慢整个游戏服
- Risk: high-risk
- 失败与影响：玩家的 `GET me`、`GET operations`、`POST cera/grant`、`POST mail/send`、`POST auth/logout` 每条请求都是在游戏玩法串行执行器上执行的命令（SD-1）。`HttpGameplay.RunAsync` 入队时用的是 `CancellationToken.None`，按注释，请求中途放弃后命令照样执行。玩家这一侧除登录外没有任何按会话或账号的并发 / 频率限制；限额默认全为 0，也就是不限，而且限额只约束"成功量"，不约束请求次数。`/me` 每次还要分页读角色目录，加点券和发信每次都要写钱包、建物品、写邮件、写回执和审计。
  以前能往执行器塞写命令的只有受信的管理员；现在任何一个普通玩家，拿到自己的会话后用脚本并发发请求（每个写请求先取一次 csrf 也不费事），就能占满执行器，拖慢所有在线玩家的游戏逻辑。玩家也可以只发合法请求（加 1 点券、发只有正文的信）制造大量数据库行和审计行。这条攻击路径不在 Accepted Risks 里（Accepted Risks 只讲了登录消耗密码计算），也没有任何测试覆盖。
- 证据：
  - `Server/src/Dfo.Server.HttpApi/HttpGameplay.cs:23-27`（放弃的请求照常执行）；
  - `Server/src/Dfo.Server.GmWeb/PlayerPortalEndpoints.cs:39-61`（上述路由都经 `PlayerRouteMapping.Map` 注册为执行器命令）；
  - `GmGameplayRouteTests` 中 `gm:` 命令数从 40 增到 51；
  - `PlayerPortalLimits.None` 与 `PlayerPortalSettings.Default`（限额全 0）。
  - 可复现探针：开启玩家后台，用一个玩家会话以 200 并发循环 `GET /api/player/v1/me`（或 `POST cera/grant`，`amount=1`，每次换新 `requestId`）。同时测量一条 GM 只读命令（如 `GET /api/gm/v1/overview`）或游戏内操作的延迟，再与基线对比。
- 最小修正：在边界或入队之前给玩家会话（按账号）加一个小的在途上限与频率上限，例如每账号同时只允许 1 个写请求、读写合计每分钟 N 次，超出时返回 `rate_limited`。如果决定不做，就把它写进 Accepted Risks，并由用户裁定。

## CH-2：线上没配 `GmWeb:TrustedProxies` 就打开玩家后台，会重现 #305 的"全员锁死"，而且没有任何提示
- Risk: high-risk
- 失败与影响：
  - 默认配置（`appsettings.json` 的 `GmWeb` 节没有 `TrustedProxies`）下，经 Caddy 进来的请求来源都是 `127.0.0.1`。`PlayerPortalEndpoints.cs:171` 用这个来源地址作为玩家登录的限流键，同一来源 15 分钟内累计失败 20 次就整体限流。所以玩家之间正常的输错，或者一个人故意输错 20 次，就会让所有玩家 15 分钟内都登录不了。这正是 Spec「Why」要避免的结果。
  - 文档与界面都没写"开放玩家后台前必须先配受信代理"。管理员在 UI 上一键就能打开总开关（`/gm/player-portal`），容易漏掉这一步。
  - 即使配了也可能无声失效：如果 Caddy 上游写成 `localhost`、实际经 `::1` 连入，而配置的是 `127.0.0.1`，转发头被静默忽略。ASP.NET 只在 Debug 级别记日志，服务端行为退回"全员共用一个键"。
  - 记忆中的线上现状（`dnf-server-deploy` 记录，**需核实 Caddyfile**）：Caddy 先拒绝外站 `Origin`，再剥掉 `Origin` 头，作为 #305 的临时绕行。如果这条规则只写了 `/api/gm*`，`/api/player/*` 的 `Origin: https://…` 会原样到达服务端。未配受信代理时服务端看到的 `Scheme` 是 http，与 `Origin` 对不上，玩家登录及所有写操作都会被拒为 403"跨站请求被拒绝"。
- 证据：
  - `Server/src/Dfo.Server.Host/appsettings.json:77-81`；
  - `GmLoginLimiter.TryBegin`（同一来源窗口内累计 20 次）；
  - `GmAccessBoundary.cs:82`（同源检查用 `Request.Scheme`）；
  - `Document/gm-web-admin.md` 的「访问与审计」「玩家自助后台」两节都没有写这个前置条件；
  - 可复现：用 `GmTestHost`（不配 TrustedProxies），从回环地址以不同 `loginName` 发 20 次错误的玩家登录，第 21 次用正确账号密码登录，预期得到 429。
- 最小修正：
  1. 在 `gm-web-admin.md`「玩家自助后台」开头写明：经反向代理对外时，开启前必须配置 `GmWeb:TrustedProxies`，且地址要与代理实际连入的地址族一致；并核对 Caddy 对 `/api/player/*` 的 `Origin` 处理。
  2. 可选：服务端在请求带有 `X-Forwarded-For`、但直连地址不在受信列表（或列表为空）时，记一条一次性 Warning 日志。

## CH-3：GM 概览页的"需要处理"和"最近操作"被玩家记录混入，GM 原有功能的含义变了（SC-11 / RQ-051）
- Risk: high-risk
- 失败与影响：`OverviewView.tsx` 这次没有改动，它查审计时不带 `actor` 参数。玩家后台开启后：
  - 玩家输错密码（`PlayerLogin` / `Rejected`）、超限额、子开关关闭等被拒操作，都会进入"需要处理"（`outcome=attention&pageSize=100`），常驻"N 条拒绝 / 回滚待你看一眼"。
  - 玩家登录失败多了以后（正常输错，或有人在 CH-2 场景下刷），最近 100 条窗口会被玩家记录占满，把管理员自己的 `GrantCera` / `SendOfficialMail` `Unknown`（结果未确认）记录挤出去。在另一台浏览器上值班、没有本地 sessionStorage 待确认项的管理员就看不到这些未确认结果，可能重新发一次，造成重复发放。
  - 概览的明细行与"最近操作"直接显示 `administratorName`（玩家记录里放的是玩家登录名），没有"玩家"标记。玩家注册一个与管理员同名的游戏账号后，他的记录在概览里与管理员操作无法区分。审计页加了标记（`AuditView.tsx:55`），概览页没有。
- 证据：
  - `Web/gm-admin/src/OverviewView.tsx:39-40`（两次审计查询都没有 `actor`）、`:107`（明细只拼 `administratorName`）；
  - `GmAuditFilter`（不带 `actor` 时返回全部操作者）；
  - `PlayerPortalAudit.RejectedLogin` 每次失败登录都写一条 `Rejected`；
  - 可复现：在一条 GM `Unknown` 审计之后写入 100 条以上的玩家 `Rejected` 登录审计，打开概览，"需要处理"里已看不到这条 GM `Unknown`。
- 最小修正：概览的两次审计查询加 `actor=gm`（或把玩家记录单独分组显示），明细中玩家记录显示"玩家"标记；补一条前端测试固定这一行为。

## CH-4：用户测试流程要求"玩家保持登录时管理员去改设置"，但同一浏览器里玩家会话优先，流程没说怎么做；顺手换成 127.0.0.1 还会冲掉主干的登录
- Risk: high-risk
- 失败与影响：
  - `Portal.tsx` 的设计是：同一浏览器里有有效的玩家会话时，一律进入玩家界面，管理台进不去（`gm-web-admin.md` 也写了"玩家优先"）。
  - 用户测试流程第 6、10、11、12 步都要求"玩家已登录（且保持登录），管理员在「玩家后台」修改限额 / 子开关 / 总开关 / 踢人"。按文档只用 `http://localhost:43081/gm/` 一个地址、一个浏览器的话，打开 `/gm/` 只会看到玩家界面。第 11 步"玩家保持登录，管理员关闭总开关"在单个浏览器配置下根本做不到，用户会卡住，或把它报成"找不到管理台"的缺陷。
  - 用户如果自己想到换主机名、用 `127.0.0.1:43081` 开管理台，由于 Cookie 不区分端口，会与主干后台 `127.0.0.1:18081` 的 `Dfo.Gm.Session` 互相覆盖。RQ-060 特地选 `localhost` 就是为了避开这一点。
  - SC-12 要求测试流程交给用户后能执行，这个问题直接影响 SC-12 的交付质量。
- 证据：
  - `Web/gm-admin/src/Portal.tsx:10-12` 注释与 `probe()` 第 29-35 行；
  - `Document/tasks/player-self-service-portal/用户测试流程.md` 第 53、73、78、84 行；全文没有"另一个浏览器 / 无痕窗口"之类的说明；
  - 第 16 行"两边的登录 Cookie 按主机名分开"。
- 最小修正：在测试流程的前置条件里写明"管理员操作请用另一个浏览器或无痕窗口，地址仍用 `localhost:43081`，不要用 `127.0.0.1`"，并在第 6、10–12 步标注是哪个窗口执行。

## CH-5：玩家"结果未确认"的原编号只存在内存里，刷新页面就丢，可能导致重复入账或重复发信
- Risk: low-risk
- 失败与影响：
  - 加点券或发信返回 `unknown`（网络错误、30 秒超时、Caddy 502/504，此时服务端可能已经提交）后，`PlayerApp` 把 `requestId` 与内容保存在 React 状态里。用户刷新页面，或会话过期后重新登录，编号就没了。再提交会生成新编号，如果第一次其实已经提交，就会第二次入账或第二次发信。
  - GM 侧的待确认发放存在 sessionStorage 里，刷新不丢（`gm-web-admin.md:38`）；玩家侧没有采用同样做法。
  - 影响只限玩家自己的账号；设了每日限额时，重复的那笔会被限额挡住。所以可以等用户测试之后再处理。
- 证据：`Web/gm-admin/src/PlayerApp.tsx` 的 `slots` / `pendingOf`（只在 `useState` / `useRef` 中）；`Document/gm-web-admin.md:52`"保存在玩家界面这一层"。
- 最小修正：把待确认项按 `accountId + path` 存进 sessionStorage（与 GM 侧一致），或在文档的 Accepted Risks 里写明刷新会丢编号。

## CH-6：受信代理转发了解析不了的 `X-Forwarded-For` 时，请求会回落为代理本身的回环地址，被当作"本机安全连接"
- Risk: low-risk
- 失败与影响：
  - `ForwardedHeadersMiddleware` 遇到第一条解析不了的 `X-Forwarded-For` 值（如 `unknown`、混淆写法）时会直接返回，什么都不改，`RemoteIpAddress` 仍是代理的 `127.0.0.1`，`Scheme` 仍是 http。
  - `GmAccessBoundary.SecureTransport`（`:93`）把回环当作安全连接，于是这类请求绕过了"远程必须 HTTPS"，与 RQ-032"经代理转发的远程明文请求被拒"相悖，同时又回到所有人共用一个限流键的状态。
  - 前提是代理把客户端可控的转发值透传过来。Caddy 默认会改写这个头，所以概率低；文档已要求代理"改写（而不是透传）这两个头"。
- 证据：在 `GmTrustedProxyHttpTests` 中仿照 `Forwarded_remote_plain_http_is_refused_and_the_proxys_own_request_is_local` 写探针：配置 `Trusting("127.0.0.1")`，请求带 `X-Forwarded-For: unknown`、`X-Forwarded-Proto: http`，`GET /api/gm/v1/auth/session`。按 RQ-032 应得到 403"需要 HTTPS"，预计实际得到 401（已经通过了 HTTPS 规则）。玩家前缀同理。
- 最小修正：配置了受信代理时，如果请求来自受信代理、带着 `X-Forwarded-For`，但中间件没有应用它（没有生成 `X-Original-For`），就按远程明文拒绝；补上这条测试。
