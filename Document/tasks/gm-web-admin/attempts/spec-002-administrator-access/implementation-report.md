# Spec-002 实现报告

基准：`0630348d`。范围为当前 approved Spec 的管理员访问与审计；产品改动未提交，根控制器、合同、其他 child 的文件由主控管理，不属于本 worker 的 owned diff。

## Changed

- `Data/Administration` 定义独立管理员、权限、会话和审计模型；`Abstractions/Administration` 暴露访问、审计领域与持久化接缝。
- `Application/Administration` 实现首次初始化、安全密码验证、8 小时持久化会话、当前权限/启用状态校验、退出撤销及审计。登录、退出、初始化、审计分页均由已有命令总线承载，命令处理器仅注入领域服务。
- `Persistence/Administration` 使用当前 `SqliteDatabase` ambient transaction；管理员、会话与审计 3 张新表及索引只在统一 Schema 声明。会话原文只通过受控 Cookie 传输，数据库保存 SHA-256 摘要。
- `GmWeb` 增加默认封闭的 API 边界、显式匿名白名单/权限 metadata、实际连接传输判断、ASP.NET antiforgery 实际 JSON 写请求验证、bootstrap 与有容量边界的并发登录限流。未注册的 GM API 对匿名返回 401，对已登录身份返回 403；不同监听端口仍隔离。
- React 登录、初始化提示、错误反馈、当前身份、权限菜单、退出及中文审计分页已接入。审计 401 返回登录，403 清空列表并提示；每分钟刷新当前身份。Vite 代理保留 Host，使开发请求继续使用同源校验。
- 更新 `Document/gm-web-admin.md`；新增真实 HTTP、并发/容量限流、审计事务和 UI 交互验证。原 Schema 精确表数量由 48 更新为 51，保留旧断言并增加 3 张新表完整列断言；命令架构 sweep 从仅 Protocol 扩展到 Application + Protocol。

## Why

沿用已有框架 `PasswordHasher` 契约，不引入第二套密码算法。随机 256 位 opaque Cookie 与服务器存储的会话摘要使注销、过期、禁用和权限变化可以每次请求验证，而不相信客户端旧权限。CSRF 使用进程临时 DataProtection 密钥，不写系统用户默认目录；服务重启后 GM 会话仍有效，但客户端重新取得 CSRF。

审计查询通过事务命令读取总量与页面，使用同一 SQLite 快照；审计 append 不创建独立事务，后续业务写入与记录可共同提交或回滚。限流在 KDF/排队前原子预留，活动来源窗口不被容量淘汰；容量满时拒绝新来源，避免“淘汰后绕过”。

## Checks

所有命令在 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin` 执行；共享 .NET 输出按序验证。原始证据位于 `artifacts/gm-tests/`（忽略产物，仍保留）。数据库由 fixture 创建到 `%TEMP%/dfo-gm-auth-<UUID>/test.db` 和 `%TEMP%/dfo-gm-audit-<UUID>/test.db`，既不读取也不写入当前服务数据库。

| Evidence | Command / outcome |
|---|---|
| `access-red-behavior.log` | 基线新增匿名访问验收测试，期望 401，实际 404，exit 1；是行为 RED。 |
| `access-first-green.log` | 同测试在实现访问边界后 1/1 通过。 |
| `access-final.log` | `dotnet test Server/tests/Dfo.Server.Host.Tests -c Release --no-build --filter 'FullyQualifiedName~GmAuthenticationHttpTests\|FullyQualifiedName~GmLoginLimiterTests\|FullyQualifiedName~GmWeb' --logger 'console;verbosity=normal'`，15/15。实际 shell 使用 filter 中的普通 `|`（不带反斜杠）。 |
| `host-full.log` | `dotnet test Server/tests/Dfo.Server.Host.Tests --logger 'console;verbosity=normal'`，45/45，包括继承启动/端口/launcher/game 回归。 |
| `persistence-full.log` | `dotnet test Server/tests/Dfo.Server.Persistence.Tests --logger 'console;verbosity=normal'`，396/396。 |
| `persistence-gm.log` | `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter 'FullyQualifiedName~GmAuditTransactionTests\|FullyQualifiedName~SchemaOwnershipTests' --logger 'console;verbosity=normal'`，6/6；filter 同上使用普通 `|`。 |
| `application-focused-final.log` | `dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --no-build --filter 'FullyQualifiedName~CommandHandlerArchitectureTests\|FullyQualifiedName~Authentication' --logger 'console;verbosity=normal'`，61/61；filter 同上使用普通 `|`。 |
| `solution-release-build.log` | `dotnet build Server/86JP.Server.slnx -c Release`，exit 0，0 警告/0 错误。 |
| `frontend-test-final.log` | 在 `Web/gm-admin` 执行 `npm test`，8/8 UI 交互通过；这些是 UI 行为验证，不作为真实后端鉴权证据。 |
| `frontend-build-final.log` | 同目录 `npm run build`，类型检查及发布构建通过；Vite 保留单包大于 500 KB 的构建提示，当前 JS 约 1.05 MB / gzip 336 KB。 |
| `frontend-dependencies-final.log` | 固定新增测试依赖版本，169 个包审计，0 vulnerabilities。 |

### Test-value 与失败记录

行为 RED 保护匿名用户不能读取管理私有 API 的 HTTP 契约；漏挂中间件或后来新增路由未声明权限是现实回归，可能泄露运营资料。真实 Kestrel 状态码作为观察点，不固定内部类/方法形状；原骨架只有健康和页面检查，编译不能替代此访问行为，因此新增测试具有足够保护价值。

前两次 fixture 启动失败分别缺少游戏组件的连接关闭契约和必需 Game 配置，记录为 `access-red.log`、`access-red-corrected.log`；它们不是行为 RED。修正测试装配后才得到上表中的 401/404 RED。其余扩展测试属于实现后的证据，不虚称全部按 RED-first 开发。

`access-integration-round1.log` 的 10/11 中失败为测试自签证书的 RSA 生命周期造成 TLS EOF；导出并重新加载 PFX 修复 fixture，round2 11/11，最终包含远程连接事实模拟 + 真实 TLS 的检查全部通过。`frontend-test-round1/2.log` 分别保留 Ant Design 登录按钮中文空格与定时器 fixture 捕获问题；修复测试定位和 60 秒定时器选择后，round3 与最终 8/8，未削弱产品行为断言。

### Acceptance 映射

| AC | 主要证据 |
|---|---|
| AC-001 | `UnconfiguredAdministratorKeepsPrivateApiClosedWithoutPreventingStartup`、`MissingBootstrapFieldAndExpiredSessionAreRejected`、`SessionSurvivesRestartWithoutBootstrapOverwritingIdentity`，以及 Host 全量启动回归。 |
| AC-002 | `AuditPaginationAndStoredRecordsContainNoCredentials`、`IdenticalPasswordsInIndependentDatabasesHaveDifferentSaltedRecords`；登录 JSON/原始 Cookie/数据库摘要与捕获日志分别检查。 |
| AC-003 | `LoginRequiresActualCsrfAndSessionLogoutRevokesCapturedCookie`、`PlayerTicketCannotReadAuditAndPermissionsUseCurrentStorage`、重启及过期测试。 |
| AC-004 | `CrossBrowserTokensAndCrossSiteOriginCannotCreateOrRevokeSession` 与真实 JSON 登录/退出 CSRF 检查。 |
| AC-005 | `RemoteHttpRejectsSpoofedForwardingHeaders` 以真实 HTTP 管线注入连接事实；`RealHttpsIssuesSecureCookie` 使用实际 TLS 监听及远程连接事实，普通回环 HTTP 登录另有真实监听证据。 |
| AC-006 | `FailuresAreAuditedAndLimitedWithoutRevokingExistingSession`、`GmLoginLimiterTests` 的并发预留、来源容量及名称容量测试。 |
| AC-007 | HTTP 审计分页/拒绝；`AuditFailureRollsBackLoginAndLogoutInSameTransaction`；Persistence 业务测试记录与审计共同提交/回滚及事务外写拒绝。 |
| AC-008 | `AdminApp.test.tsx` 的 8 个可观察中文交互；构建与主控浏览器检查入口。 |
| AC-009 | Application 架构 13 条包含新 handler；Schema 新表内容/重建 3 条；Host/Persistence 全量及 Release solution build。 |

## Remaining

没有当前实现范围内的产品阻塞。尚需主控独立 drift/challenge/final review、fresh verification 和实际浏览器验收（测试 UI 不能替代浏览器呈现）。尚未部署现行服务、初始化真实管理员、开放玩家/邮件/点券业务或提交结果。

浏览器入口：运行已构建的 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.exe`，使用新 GM 端口与新 `Database:Path`，关闭 `LauncherAuth:Enabled` 和 `Networking:Enabled`。通过该进程独有环境注入 bootstrap；纯隔离测试身份可用 `GmTestHost.cs` 定义，不将秘密抄到启动日志。访问 `/gm/`，按登录、操作记录、分页、退出顺序检查。未初始化路径另用没有 bootstrap 的新库验证。

## Rework Round 2 · F-1 / CH-1 身份响应乱序

### Changed

本轮仅修改 `Web/gm-admin/src/AdminApp.tsx`、`AdminApp.test.tsx` 和本报告。登录、退出、初始化刷新建立新的身份世代；所有相应异步成功、失败和 finally 状态写入只允许当前且仍挂载的世代执行。身份轮询在请求时捕获世代，并在切换期间停止新查询；当前身份引用同步更新，退出完成后即使旧 effect 尚未清理也不能再派发旧身份查询。卸载使旧世代失效。

审计视图以身份/世代为 key，旧视图的在途 401 不能清除新身份；切换中不让审计过期回调打断正在结算的登录/退出状态。退出按钮显示执行中状态。未改变服务端、权限/会话协议、Spec 或控制器。

### Why / Test-value

独立审查复现了退出后旧身份成功回复恢复旧菜单，并指出同一缺口使旧 401 清除新登录。后端拒绝旧 Cookie 不能保护界面的身份状态。回归测试使用 deferred Promise 控制真实消费者能遇到的响应顺序，观察登录表单、新身份名称和权限菜单；不依赖所选 ref/世代内部结构，因此在保留行为的内部重构下仍有效。已有 8 个正常交互与同步错误用例不足以捕获这种在途响应乱序。

### RED / GREEN / Checks

在修改产品前新增两个回归，并执行（工作目录 `Web/gm-admin`）：

```powershell
npm test -- --testNamePattern='旧身份'
```

`artifacts/gm-tests/ui-generation-red.log`：exit 1，两个回归均失败，8 个原用例按过滤条件 skipped。第一条退出后交付旧成功回复导致登录表单消失；第二条完成新登录后交付旧 401 导致新管理员名称消失。两条都是消费者状态不符合预期的行为 RED，不是编译或 fixture 失败。

```powershell
npm test
npm run build
```

`ui-generation-green.log` 首次 GREEN 为 10/10；随后整理审计回调及切换中边界后，`ui-generation-final.log` 再次 10/10，原 8 个用例全部保留通过。`ui-generation-build.log` 类型检查及 Vite 发布构建 exit 0，保留既有单包体积提示（约 1.05 MB / gzip 337 KB）。本轮没有其他测试失败，也没有运行 .NET 检查；服务端未变化，主控后续 fresh verification 会覆盖整个 accepted target。

### Remaining

F-1 的两个乱序回归均已通过，等待独立 bounded 复核，不自宣称最终审查完成。CH-2 作为非阻塞维护项保留：未来需要制定过期/撤销会话的事务内清理策略，保留未到期会话与审计；本轮未增加清理实现。
