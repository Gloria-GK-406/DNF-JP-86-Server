# Spec 对抗性挑战

- 目标：`spec.md`（frontmatter `state: ready`，`contract.revision: 2`；文件 SHA-256 前缀 `9952fc0bea36b5f2`）
- 治理依据：`TaskContract.md` revision 2（SHA-256 前缀 `a1f7789f0a591b5d`）、`task-contract-review.md`；参考 issue #305 正文与验收、设计方案 `E:\Project\DNF\Document\玩家自助后台设计方案-20261113.md`、UI 设计图
- 代码参照（只读）：worktree 基线 `20bcaf37c`；`GmAccessBoundary.cs`、`GmAuthenticationEndpoints.cs`、`GmLoginLimiter.cs`、`GmWebModule.cs`、`GateAuthenticationMiddleware.cs`、`LauncherCredentialReader.cs`、`PasswordComputationLimiter.cs`、`GmCeraDomainService.cs`、`CharacterDatabaseSchema.cs`、`Document/FeatureDevelopment.md` §4、`Document/DatabaseSchemaMigration.md`
- 攻击范围：访问控制与越权（会话串用、范围裁剪、存在性泄露）、开关即时生效与竞态、幂等与限额统计边界、受信代理转发头与 HTTPS 判断、迁移与默认值、用户测试对主干库的风险、与契约的一致性
- 未运行任何探针；以下证据均为源码 / 文档锚点

## CH-1: 玩家接口的传输与跨站防护没有被要求，也没有被验收

- Risk: high-risk
- Failure and impact: 现有的"远程必须 HTTPS"、监听端口限定、`Cache-Control: no-store`、Origin / `Sec-Fetch-Site` 检查与 antiforgery 全部写在 `GmAccessBoundary` 里，且只对 `/api/gm/v1` 前缀生效。设计方案把玩家路由放到另一个前缀 `/api/player/v1`，并另写一个边界中间件。Spec 的 RQ-031/RQ-032 只笼统地说"后台"，没有一条要求玩家接口（尤其是公开的玩家登录）同样拒绝远程明文、做同源检查与 antiforgery、在 HTTPS 下给玩家会话 Cookie 加 `Secure`。AC-031–AC-033 也没有写明要对玩家与管理员两侧各测一遍：AC-032 只说"登录成功的 Cookie"，AC-033 只说"非公开接口"，而玩家登录本身就是公开接口。照现有 AC 实现，新写的玩家边界即使漏掉 HTTPS 规则，所有测试仍会通过。后果是：后台端口一旦被直连（比如 NAS 端口转发），游戏账号密码会以明文传输；玩家写接口还可能没有 CSRF 防护。契约 SC-9 明确要求"玩家与管理员登录都覆盖"，Spec 在这里低于契约。
- Evidence: `GmAccessBoundary.cs` 第 20 行 `if (!context.Request.Path.StartsWithSegments("/api/gm/v1")) { await next(context); return; }`，HTTPS、Origin 与 antiforgery 检查都在它之后；设计方案 §2 末行与 §3 写着"玩家路由另开前缀，边界中间件另写一个"；TaskContract SC-9（"玩家与管理员登录都覆盖"）；spec RQ-031、RQ-032、AC-032、AC-033。
- Minimum correction: 在 RQ 中写明玩家接口（含公开的玩家登录）与管理员接口执行同一套传输规则：远程须 HTTPS、只在后台监听端口受理、修改类请求做同源检查与 antiforgery、HTTPS 下会话 Cookie 带 `Secure`。AC-032、AC-033 各补一组玩家侧用例（玩家登录、玩家写接口）。

## CH-2: 开关、会话、归属与限额的判定没有要求和写入原子完成，并发与关停竞态没有验收

- Risk: high-risk
- Failure and impact: RQ-021 只写了"立即生效"和"下一次请求被拒"，RQ-022 只写了"已成功数量加本次超过上限时拒绝"；但没有规定这些判定必须与入账 / 投递在同一次串行执行中、对最新状态重新做一遍。设计方案让 `PlayerAccessBoundary` 在 HTTP 入口查进程内快照，实现时很容易把开关、会话甚至限额的判定留在入口或执行器外的只读查询里，只把写入放进 CMD。具体有三条路径：
  1. 玩家脚本并发发出 N 个加点券请求。每个请求读到的"今日已用"都还没包含其它请求，于是一起越过每日上限。
  2. 请求已经通过入口检查、正在排队，这时管理员关闭总开关或子开关（同一事务里删除了会话）。排在后面的入账照样执行，管理员看到"已保存"之后仍有新的入账。
  3. 玩家登录在执行器外校验完密码，管理员此时关闭总开关；登录 CMD 没有再查开关，照样写入新会话。重新开启后，这个本应失效的会话又能用，违背 RI-15"关闭后已有会话失效"。

  AC-021、AC-022 都是顺序用例，上述缺陷都能通过。限额和开关是管理员仅有的两项控制手段（RI-5、RI-7），出事后已入账的点券和已投递的物品无法自动收回。
- Evidence: spec RQ-021、RQ-022、AC-021、AC-022 都没有并发或交错条件；设计方案 §6"`PlayerAccessBoundary` 每个请求都查快照"；§4.1 第 4 步"登录 CMD 内：再读一次开关"只写在设计方案里，Spec 没有收录；现有 `GmCeraDomainService.GrantAsync` 先 `AcquireWriteAsync` 再 `Authorize`、再查幂等、再写入，GM 侧已有正确的先例可循。
- Minimum correction: 新增一条要求：总开关、子开关、会话有效性、角色归属与限额，都在执行入账 / 投递 / 写会话的同一事务内对最新状态判定（入口快照只用于提前拒绝）。再补验收：并发提交 N 个合计超过每日上限的请求，只有不超限的部分成功；"已通过入口、尚未执行"的请求在关闭总开关后执行时被拒；关闭期间完成密码校验的登录不留下会话。

## CH-3: #305 改动的作用范围与受信代理地址匹配没有边界

- Risk: low-risk
- Failure and impact: 
  1. **作用范围。** GmWeb 与启动器 gate 接口共用同一个 ASP.NET 应用，`GmWebModule.ConfigureMiddleware` 注册的中间件对所有监听端口生效。若按 issue 建议"接入 `ForwardedHeaders`"而不限定在 GmWeb 监听端口，gate 的失败限流键（`Connection.RemoteIpAddress`）和 `GateClientAddress` 解析会一起改变。gate 的 `X-Forwarded-For` 由网关追加，可能被提前消费或改写，而 issue #305 明确把 LauncherAuth 排除在范围外。RQ-030 说的是"后台的受信代理"，但没有禁止影响其它监听端口，AC-030 也只回归 GM 测试。
  2. **地址匹配。** Caddy 回源时可能经 `::1` 或 IPv4 映射地址（`::ffff:127.0.0.1`）连接，配置里只写了 `127.0.0.1` 就会静默不匹配，线上照旧共用一份限流，而且没有任何诊断信息。

  两项都能在实现、部署时发现并修正，所以定为 low-risk。
- Evidence: `GmWebModule.cs` 中 `ConfigureMiddleware(... ) => application.UseMiddleware<GmAccessBoundary>()`，由 `HttpApiHostingExtensions.cs` 第 38 行对整个应用注册；`GateAuthenticationMiddleware.cs` 第 44 行（限流键）与第 103–111 行（取最后一跳 XFF）；issue #305"LauncherAuth（gate 接口）不在此范围"；spec RQ-030、AC-030。
- Minimum correction: RQ-030 加一句"转发头只对后台监听端口的请求生效，gate 等其它端口行为不变"，AC-030 补 gate 回归。受信代理匹配应覆盖 IPv4 映射地址，并在配置了代理却收到非受信来源的转发头时留下可观察的日志（或至少在文档中写明 `::1` 与 `127.0.0.1` 的区别）。

## CH-4: 新开放的公开玩家登录会带来未鉴权的资源消耗

- Risk: low-risk
- Failure and impact: 
  1. **限流器容量。** `GmLoginLimiter` 的来源表容量是 1024，满了以后对所有新来源一律返回 429。#305 之后来源键改为真实 IP，持有 IPv6 /64 的单个攻击者就能轻易造出 1024 个来源，让所有新来源的玩家在 15 分钟内登录不了。RQ-033 只把玩家和管理员的限流分开，没有处理这一点；同样的手法也能用在管理员登录上，攻击期间没有现成会话的管理员登不进去，也就关不了开关。
  2. **密码计算槽位。** 玩家登录与启动器共用 `PasswordComputationLimiter`（容量为 CPU 核数、等待队列无界）。网页登录洪泛会挤占启动器登录的哈希槽位，玩家进不了游戏。
  3. **审计写入放大。** RQ-024 要求每次登录失败都写审计（经执行器写库），未鉴权的请求因此会产生写库放大，审计里还会出现攻击者随意填写的登录名（包括用户误把密码填进账号栏的情况），只有 `ViewAudit` 权限的管理员也能看到。

  总开关默认关闭、管理员能随时关掉，所以这些问题可以事后控制，定为 low-risk。
- Evidence: `GmLoginLimiter.cs` 中 `SourceCapacity = 1024` 与 `if (sources.Count >= SourceCapacity) return false;`；`PasswordComputationLimiter.cs` 的容量为 `Environment.ProcessorCount`，`SemaphoreSlim.WaitAsync` 不设上限；`LauncherCredentialReader.VerifyLoginAsync` 共用该限流器；spec RQ-024、RQ-033。
- Minimum correction: 在 Spec 中注明 IPv6 来源按前缀（如 /64）聚合，或说明容量满时的取舍；给玩家登录的密码计算设置独立或有界的排队，使其不能无限挤占启动器；失败登录的审计是否记录原始登录名、是否按来源节流，由 Spec 明确。不需要完整设计。

## CH-5: 用户测试的"未触碰主干"与"使用了主干数据"不可核验

- Risk: low-risk
- Failure and impact: 
  1. **原库不变无法证明。** AC-060 要求"主干服务与原库未被改动"，但主干在运行中，原库持续被写，无法用哈希证明。
  2. **路径写错不会被发现。** 若测试服务的库路径写错（比如沿用了默认相对路径），服务端遇到空库会自动建全表并写入当前版本号（`DatabaseSchemaMigration.md`），"测试服务使用副本启动并可连接"仍然成立，用户却在空库上测试。
  3. **浏览器 Cookie 互相覆盖。** Cookie 只按主机隔离、不区分端口。主干后台（18081）与测试后台（41xxx）同在 127.0.0.1 时，`Dfo.Gm.Session` 与 `Dfo.Gm.Csrf` 会互相覆盖；antiforgery 密钥又是进程内临时的，于是会反复出现"请求验证失败"或掉登录，形成假失败。
  4. **与契约字面不一致。** SC-12 的字面是"用当前主干实际使用的数据库（先一致备份）启动"，`FeatureDevelopment.md` §4 写的也是"切换"模式；Spec 改为在副本上迁移后测试。路由决策把这一点列为 Spec 的取舍，方向合理（v7 库会让 v6 主干拒绝启动），但 Spec 没有写出理由，审查时可能被判为偏离契约。
- Evidence: spec RQ-060、AC-060；`Document/DatabaseSchemaMigration.md` 第 9–11 行（空库自动建表，版本不符则拒绝启动）；`GmWebModule.cs` 中 `UseEphemeralDataProtectionProvider()` 与 Cookie 名；TaskContract SC-12；`routing-decision.md`"迁移后用户测试库的处理"。
- Minimum correction: AC-060 改为可核验的证据：测试服务日志或进程打开的库路径等于副本路径；副本的 `user_version` 为新版本、账号与角色行数和备份一致；原库 `user_version` 仍为 6。RQ-060 用一句话写明"在副本上测试"的理由，并在测试流程中提示用户用不同主机名（如 `localhost` 与 `127.0.0.1`）或分开的浏览器配置访问两个后台。

## CH-6: 契约要求的部分浏览器证据缺失，回归条款与审计改动自相矛盾

- Risk: low-risk
- Failure and impact: 
  1. **浏览器证据缺口。** 契约 SC-6 要求"浏览器实测关闭后玩家页进入'已关闭'状态"，SC-8 要求"浏览器实测审计页"。Spec 的 AC-041 / EV-002 只安排了"登录 → 加点券 → 发邮件 → 记录 → 登出"的浏览器实测；已关闭页和审计筛选只有前端单元测试（AC-040）。最终审查按契约核证据时会缺两项。
  2. **回归条款矛盾。** RQ-051 说审计等 GM 接口"除 RQ-031/RQ-032 外不变"，但 RQ-024、RQ-043 明确改变了审计接口：新增玩家行、新增筛选值，响应中带操作者种类。实现者可能据 RQ-051 回避改动审计响应，也可能据此判定回归失败。
- Evidence: TaskContract SC-6、SC-8 的 Required evidence 列；spec AC-040、AC-041、EV-002、RQ-051、RQ-024、RQ-043；`GmAuthenticationEndpoints.QueryAudit` 用 `GmAuditFilter.Actions` 白名单校验筛选值。
- Minimum correction: AC-041 的浏览器实测加入"总开关关闭后玩家页进入已关闭页"和"审计页玩家自助筛选"两步并截图；RQ-051 的例外里加上 RQ-024 / RQ-043 对审计的改动。
