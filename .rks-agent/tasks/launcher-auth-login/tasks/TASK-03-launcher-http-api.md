# TASK-03 — Launcher HTTP API、Host 与公共端点配置

## Task Unit Contract

- Revision: 1
- Outcome and references: 在 Host 内交付 v1 bootstrap/register/login HTTP adapter、读取前明文传输门、稳定错误 JSON、三类公共端点严格配置与 readiness；覆盖 RQ-003—RQ-005、RQ-013—RQ-016，AC-002—AC-005、AC-012—AC-013、AC-016；Plan BD-001、BD-004、T-005，并落实 plan challenges CH-2/CH-3。
- Owned boundary: 可新增 `Server/src/Dfo.Server.LauncherApi` 及测试项目，修改 Host、Infrastructure endpoint/options、solution/package 配置和相关 Host/Infrastructure 测试。只通过 TASK-01 的应用命令接缝认证，不修改 Game Login 协议或 WPF；不得提交 Git commit。
- Required checks: TDD RED/GREEN；bootstrap 200/503、注册/登录完整状态码与 JSON、内部异常安全映射、远程 HTTP 对合法/畸形 JSON 都在 body 零读取时拒绝、非法 allowlist/三类 endpoint 启动失败。若保留 HTTP 开关，关闭时两个隔离 Host 不启动 Kestrel/不争默认端口；若删除开关，所有 Host harness 必须显式隔离 HTTP 端口并证明既有 Channel/Game 生命周期不回归。运行 LauncherApi/Host/Infrastructure Release tests 和一次 Host 启动烟雾。
- Dependencies: TASK-01 已接受的认证命令和结果合同。

## Execution Round 1 — 2026-09-10

### Changed

- 新增 `Dfo.Server.LauncherApi`：提供 v1 `bootstrap`、`register`、`login` adapter，HTTP 层只调用
  TASK-01 的应用命令。所有错误返回稳定 JSON；内部异常只记录稳定错误码与 trace id，不回显异常、
  请求 body 或凭据。凭据路由在反序列化前执行 transport gate，非 loopback 明文 HTTP 无论合法或
  畸形 JSON 都以 `secure_transport_required` 拒绝且 body 零读取。
- Host 改为仅在 `LauncherAuth:Enabled=true` 时组合 WebApplication/Kestrel，并只绑定严格校验的
  `LauncherAuth:ListenUrl`；关闭时继续使用 Generic Host，不创建隐藏 HTTP listener。启动 hosted
  service 在认证服务、公共端点快照可解析且 Networking 已启用后才将 bootstrap 标为 ready。
- 新增 `IPublicEndpointSource` 及配置快照，分别严格校验公共 Channel bootstrap、每个
  `Channel:Channels[*]` 广告的公共 Game IPv4/端口、Game LOGIN 后续公共端点；三类值不从 bind
  地址或彼此推断。`appsettings.json` 明确提供三类 loopback 示例，Launcher HTTP 默认关闭。
- 新增 LauncherApi、Host 与 Infrastructure 测试，覆盖全部状态码/JSON、真实 Kestrel + SQLite
  并发注册、错误凭据等价响应、HTTP 关闭的端口隔离、非法配置 fail-fast、异常日志脱敏和公共端点
  投影。

当前 TASK-03 的 21 个产品/测试 owned 文件为：

```text
Server/86JP.Server.slnx
Server/src/Dfo.Server.Abstractions/Configuration/IPublicEndpointSource.cs
Server/src/Dfo.Server.Data/Configuration/PublicEndpointModels.cs
Server/src/Dfo.Server.Host/appsettings.json
Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj
Server/src/Dfo.Server.Host/Modules/ServerModuleRegistrationExtensions.cs
Server/src/Dfo.Server.Host/Program.cs
Server/src/Dfo.Server.Infrastructure/Configuration/PublicEndpointSource.cs
Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs
Server/src/Dfo.Server.LauncherApi/Dfo.Server.LauncherApi.csproj
Server/src/Dfo.Server.LauncherApi/LauncherApiApplicationExtensions.cs
Server/src/Dfo.Server.LauncherApi/LauncherApiModule.cs
Server/src/Dfo.Server.LauncherApi/LauncherApiOptions.cs
Server/src/Dfo.Server.LauncherApi/LauncherApiReadiness.cs
Server/tests/Dfo.Server.Host.Tests/LauncherHttpHostStartupTests.cs
Server/tests/Dfo.Server.Infrastructure.Tests/Configuration/PublicEndpointConfigurationTests.cs
Server/tests/Dfo.Server.LauncherApi.Tests/Dfo.Server.LauncherApi.Tests.csproj
Server/tests/Dfo.Server.LauncherApi.Tests/LauncherApiArchitectureTests.cs
Server/tests/Dfo.Server.LauncherApi.Tests/LauncherApiOptionsTests.cs
Server/tests/Dfo.Server.LauncherApi.Tests/LauncherApiPipelineTests.cs
Server/tests/Dfo.Server.LauncherApi.Tests/Usings.cs
```

按路径排序，以每行 `path<TAB>SHA256`、UTF-8、LF 分隔且无末尾 LF 计算的 manifest SHA256 为
`B732146CC389F1AD76E4A806E2B59C51F9CCCF7F0A85088C2865BD2F78A15D57`。

### Why

HTTP adapter 需要与认证领域、持久化和 Game 协议保持单向依赖，因此 adapter 不直接引用
Infrastructure/Persistence/Protocol，而通过命令总线与 `IPublicEndpointSource` 读取已经启动校验的
结果。transport gate 位于 JSON 读取之前，才能把“非 loopback 明文请求不得读取或验证密码”变成
可自动化观察的不变量，而不仅是 handler 内的约定。

HTTP 开关保留既有测试和默认服务器的无 Kestrel 行为；启用分支显式使用同一 content root、配置源
和模块组合，并在 listener 启动前解析 ListenUrl、allowlist 及三类公共端点。公共广告值独立建模，
避免将进程 bind 地址或当前客户端 Host 头误当作部署可达地址。

TDD value gate 选择真实 Host listener：修改前，在现有 Host 上启用 Launcher 配置仍无法连接
bootstrap；修改后，同一测试必须从配置 URL 取得精确 ready JSON。其余 adapter 单测/集成测试保护
错误外观、读取顺序、并发结果与关闭开关等可观察结果。

### Checks

所有命令均在本 Task 工作树执行，日志/TRX 位于 `../evidence/`。

- RED：`EnabledLauncherApi_ListensOnlyOnTheConfiguredUrlAndReturnsReadyBootstrap` 因现有 Host 未监听而
  `TaskCanceledException`，1 Failed、exit 1；`task03-round1-red.log/.trx`。
- GREEN：同一真实 Host 测试 1/1 Passed；`task03-round1-green.log/.trx`。
- LauncherApi Release 全量 34/34 Passed；覆盖 bootstrap 200/503、register 201/400/409、login
  200/400/401、错误凭据同形、内部异常 500、远程 HTTP 合法/畸形 body 零读取及架构/配置；
  `task03-launcherapi-release.log/.trx`。
- Host Release 全量 16/16 Passed；包含真实 Kestrel + SQLite 八路并发注册准确 1 个 201 / 7 个
  409、正确登录 200、错误/缺失账号逐字节相同 401、HTTP 关闭的双 Host 共享未使用端口，以及七类
  非法配置在 listener 前失败；`task03-host-release.log/.trx`。
- Infrastructure Release 在设置真实 `PVF_ARCHIVE_PATH` 的全量运行中为 300 Passed + 2
  NotExecuted。两条精确条件分支
  `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
  `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
  均因 `An archive is reachable from this working tree.` 按设计 Skip；未将其表述为单次 302/302。
  随后清空 `PVF_ARCHIVE_PATH` 隔离运行这两条，2/2 Passed；证据分别为
  `task03-infrastructure-release.log/.trx` 与
  `task03-infrastructure-missing-archive-release.log/.trx`。公共端点 focused 为 16/16 Passed。
- 逐个解析上述最终 TRX 的 `UnitTestResult`：RED 精确 1 Failed，GREEN 1 Passed，LauncherApi 34
  Passed，Host 16 Passed，Infrastructure 300 Passed + 上述 2 NotExecuted，隔离无 PVF 分支 2
  Passed；没有未分类 outcome。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 为 0 warning / 0 error；
  `task03-solution-build.log`。独立 Host smoke 从 Release 输出启动真实进程，bootstrap 返回 200 和
  精确 `{"apiVersion":"1","status":"ready","registrationEnabled":true}`，Channel TCP 连接成功，
  stdout 含 `Application started`，进程及专用临时目录均清理；`task03-host-smoke-clean.log`。
- `git diff --check` 无输出、exit 0。

### Remaining

TASK-03 实现与执行验证完成，等待 controller 安排独立 quick review。本轮未修改 Game/WPF、用户运行
文档或 task registry，未写 review/verdict，未提交 Git。HTTP→Channel Info→Game 的完整跨端闭环与
真实 Game 故障注入仍按合同交给 TASK-04；运行说明和最终装配交给 TASK-06。

## Independent Review Round 1

- Reviewed execution: TASK-03 `Execution Round 1` / Task Unit Contract revision 1；完整阅读 root TaskContract revision 1、approved Spec revision 2、approved Plan，重点核对 RQ-003—RQ-005、RQ-013—RQ-016、AC-002—AC-005、AC-012—AC-013、AC-016、BD-001 / BD-004 / T-005 及 CH-2 / CH-3。依照 `reviewing-task-units-quick` / `reviewing-code` 的独立 Task 评审规则，本轮不裁定其他 Task 或完整 Spec。
- Reviewed identity: 逐项核对所列 21 个产品/测试文件及关联 Host/配置/认证调用路径，独立重算 manifest 为 `B732146CC389F1AD76E4A806E2B59C51F9CCCF7F0A85088C2865BD2F78A15D57`，与执行记录一致；approved Plan SHA256 仍为 `316F2F5CAE2EF011E27F462199AF78CB985A1FE878DA6C99932592AC9D69F1EA`。评审只追加本节，不改产品、registry、其他 Task 或 Git 提交。

### Finding QR1-F1 — P1：路由末尾斜杠绕过远程明文凭据读取门禁

`Server/src/Dfo.Server.LauncherApi/LauncherApiApplicationExtensions.cs:181` 的 `IsCredentialPath` 只做两个无末尾斜杠路径的精确比较，而 ASP.NET 实际 endpoint routing 同时接受 `/api/launcher/v1/login/`、`/api/launcher/v1/register/`。这些路径跳过 `:165` 的传输拒绝，随后真实路由仍执行同一凭据 endpoint；因而远程明文 HTTP 可以读取密码 body 并派发认证/注册命令，直接违反 CH-2、BD-004 和读取前 gate 的 Task 必须检查。

独立复现复用当前 Release 测试程序集的 `ApiHarness`：同一真实 middleware + endpoint routing pipeline，`scheme=http`、RemoteIpAddress=`198.51.100.20`，相同 ReadTrackingStream，命令结果由测试替身提供。`dotnet run --project proof.csproj -c Release` exit 0，实际结果如下；ReadCount 表示调用次数，不是字节数。

| 请求路径 | body | status / code | ReadCount | command calls |
|---|---|---|---:|---:|
| `/api/launcher/v1/login` | 合法 JSON | 400 / secure_transport_required | 0 | 0 |
| `/api/launcher/v1/login` | `{` | 400 / secure_transport_required | 0 | 0 |
| `/api/launcher/v1/login/` | 合法 JSON | 401 / invalid_credentials | 8 | 1 |
| `/api/launcher/v1/login/` | `{` | 400 / invalid_request | 8 | 0 |
| `/api/launcher/v1/register/` | 合法 JSON | 201 / 创建结果 | 8 | 1 |
| `/api/launcher/v1/register/` | `{` | 400 / invalid_request | 8 | 0 |

最小修正：让 gate 与路由匹配语义同源，例如在路由匹配后、endpoint 手工读取 body 前，按凭据 endpoint metadata 实施检查。若使用 Path 规范化方案，应验证全部实际可匹配等价形态，不能仅补一个单斜杠字符串。为 register/login 的合法及畸形 body 增加等价路由测试，远程 HTTP 必须始终 `secure_transport_required`、读取 0 次、命令 0 次，同时保留 loopback HTTP 和远程 HTTPS 的正常行为。

### Finding QR1-F2 — P2：缺失或错误 Content-Type 被映射为内部错误

`LauncherApiApplicationExtensions.cs:149` 调用 `ReadFromJsonAsync` 前未检查 JSON media type；`:154` 的输入解析 catch 不包含该 API 在缺失/错误 Content-Type 时抛出的 `InvalidOperationException`。这种客户端请求错误因此进入异常 middleware，返回 500 `internal_error`，而相同请求在声明 JSON 时正常返回 400 `invalid_request`。这破坏了本 Task 对非法请求与内部故障的稳定分类，让客户端被错误提示为服务端暂时故障，并产生无必要的内部错误日志。

独立使用同一真实 pipeline，在 HTTPS 凭据 endpoint 发送 body `{`：Content-Type 缺失为 500 / internal_error，`text/plain` 为 500 / internal_error，`application/json` 控制组为 400 / invalid_request。最小修正是在读取前显式验证支持的 JSON media type，把缺失/不支持类型映射到本 API 的稳定 400 `invalid_request`；避免通过吞掉所有 `InvalidOperationException` 掩盖真正内部故障。补 register/login 对缺失、不支持以及支持 media type 的回归。

### Challenge Dispositions and Evidence

- CH-2: `confirmed`，具体已落为 QR1-F1。标准路径的合法/畸形 JSON 读取前拦截成立，但门禁覆盖的路径集合小于真实路由集合。
- CH-3: `rejected`（就当前实现与已执行检查而言）。禁用分支为 Generic Host，启用分支才创建 WebApplication；双隔离 Host 测试通过，未用共享 HTTP 端口、输出无 Kestrel listener。评审额外启动启用 HTTP 的当前 Release Host，分别注入缺少 required channel script 和不可用数据库路径：两个进程均非零退出、未报告 Application started，退出后探测 HTTP、Channel、两个 Game 端口全部为 False；前者输出对应 Required channel script，后者对应 SQLite Error。该检查覆盖初始化失败清理，不宣称任意故障均已穷尽。
- 引用图满足 LauncherApi 只引用 Abstractions/Application/Data；真实 HTTP + SQLite 注册八路竞态和错误/缺失账号逐字节一致 401 的证据存在。bootstrap/凭据 readiness 503、合法 JSON fieldErrors、201/409/200 和受控内部异常的安全 JSON/日志测试通过；QR1-F2 是未覆盖的请求类型分支。三类公共端点从各自配置严格读取，缺失/非法值拒绝，allowlist 非法项启动失败；Host 两分支均使用 `AppContext.BaseDirectory`、保留 experience 配置与五个既有模块。实际公网可达性不在本配置检查证明范围内。
- 独立逐项解析本 Task TRX：RED 1 Failed、GREEN 1 Passed；最终 LauncherApi 34 Passed、Host 16 Passed；Infrastructure 300 Passed + 2 NotExecuted。两条 NotExecuted 的确分别是缺少 PVF 的 quest/town 分支，原因为 `An archive is reachable from this working tree.`；隔离无 PVF 的两个同名用例均 Passed，未把单次全测称作 302/302。其余 focused TRX 为 32、10、16 条 Passed。最终日志与这些逐项结果一致，solution build 为零警告/错误，Host smoke 为 bootstrap 200、精确 ready JSON 与 Channel TCP 成功。
- 新鲜度：owned 文件最晚修改 14:09:40（+08:00）；最终 Infrastructure/API/Host 分别于 14:10:21 / 14:10:22 / 14:10:24 开始，无 PVF 隔离运行于 14:18:28，均晚于修改。评审另于 14:29:05 运行 `dotnet test Server/tests/Dfo.Server.LauncherApi.Tests -c Release --no-build`，34/34 Passed、exit 0，已逐项解析独立 TRX；已有绿灯未覆盖两条 finding。
- 独立复现源码及产物在 `C:\Users\Gloria\AppData\Local\Temp\task03-review-0950aa9ea8814e40a2363c33dab303a9\`：`Program.cs` / `proof.csproj` 使用当前 Release 的真实请求 pipeline，`startup-failure.ps1` 使用真实 Host 进程；`reviewer-launcherapi.trx` 为独立复跑。没有修改仓库测试来制造结果。评审 `git diff --check` 无输出、exit 0。

Verdict: `failed`

## Execution Round 2 — 2026-09-10

### Changed

仅处理独立评审的 `QR1-F1` / `QR1-F2`，增量修改两个产品/测试文件：

- `Server/src/Dfo.Server.LauncherApi/LauncherApiApplicationExtensions.cs`：register/login route 绑定同一个
  `CredentialEndpointMetadata`；pipeline 显式按 exception middleware → `UseRouting` → credential
  transport gate → endpoint 的顺序执行。gate 从已经匹配的 endpoint metadata 判断凭据接口，不再维护
  独立手写路径集合，因此 `/login`、`/login/`、`/register`、`/register/` 及 routing 实际接受的等价形态
  共享同一读取前门禁。凭据读取前另以 `HasJsonContentType()` 明确验证 JSON media type；缺失或不支持
  类型返回既有 `400 invalid_request`，没有扩大异常 catch 或吞掉内部 `InvalidOperationException`。
- `Server/tests/Dfo.Server.LauncherApi.Tests/LauncherApiPipelineTests.cs`：尾斜杠 register/login 对合法与畸形
  body 均覆盖远程明文拒绝，并逐项断言 `secure_transport_required`、body `ReadCount=0`、command=0；
  缺失及 `text/plain` Content-Type 覆盖两接口的 400、零读取、零命令和哨兵不回显；标准
  `application/json` 与 `application/*+json` 畸形 body 继续映射 400。另证明尾斜杠路由在 loopback HTTP
  与远程 HTTPS 下仍正常派发。

本轮两个文件 SHA256：

```text
Server/src/Dfo.Server.LauncherApi/LauncherApiApplicationExtensions.cs  4711B823DEE54E497EE59661A4BB62CC0EBAD79115033F8E351B7AA19141FDB4
Server/tests/Dfo.Server.LauncherApi.Tests/LauncherApiPipelineTests.cs  A3E50A91EC7E474C22367DF475D12B30B064C3CE421F07EA09A4192D408DAC8D
```

按 Round 1 相同 21 个 owned 产品/测试文件与相同算法重算，当前 manifest SHA256 为
`BD3C607D1E428BBAF565E92A0D9A7EA458B7285963ECC750BF336CAB55EE4920`。

### Why

路由是否接受某个 URI 形态应由 ASP.NET endpoint routing 决定。将安全策略标在凭据 endpoint 上，并在
routing 完成后读取 metadata，使传输门与 handler 的真实可达集合共享事实来源；以后 route pattern 的
等价匹配或调整不会因忘记同步字符串比较而再次形成 body 读取旁路。gate 仍位于手工 JSON binding 和
命令派发之前。

Content-Type 是客户端输入合同的一部分，应在 body reader 前分类；用 `HasJsonContentType()` 接受框架
支持的 JSON 与 `+json` 语义，同时把缺失/不支持类型稳定映射成客户端错误。保留原有窄 catch 后，真正
来自命令或内部逻辑的 `InvalidOperationException` 仍由异常 middleware 安全映射为 500，而不会被误报
为 400。

### Checks

证据位于 `../evidence/`，均从本 Task 工作树运行：

- RED（未改产品）：新增/扩展的精确过滤共 16 条，8 Passed / 8 Failed、exit 1。四条 F1 尾斜杠
  case 中，合法 body 实际进入命令后返回 500，畸形 body 实际读入后返回 `invalid_request`；四条 F2
  缺失/`text/plain` × register/login 均实际返回 500。证据 `task03-round2-red.log/.trx`。
- GREEN：同一 16 条全部 Passed；远程明文标准/尾斜杠 × register/login × 合法/畸形 body 均为
  400 `secure_transport_required`、`ReadCount=0`、command=0；缺失/错误 Content-Type 均为 400
  `invalid_request`、`ReadCount=0`、command=0。证据 `task03-round2-green.log/.trx`。
- 增加合法传输控制组后的直接 focused 为 20/20 Passed；loopback HTTP 与远程 HTTPS 的尾斜杠
  register/login 均只派发一次并返回成功状态。证据 `task03-round2-focused.log/.trx`。
- LauncherApi Release 全量 50/50 Passed；Host Release 全量 16/16 Passed；公共端点
  Infrastructure 必要回归 16/16 Passed。证据分别为
  `task03-round2-launcherapi-release.log/.trx`、`task03-round2-host-release.log/.trx`、
  `task03-round2-infrastructure-focused.log/.trx`。
- 已逐个解析六份 Round 2 TRX 的 `UnitTestResult`：RED 精确 8 Failed + 8 Passed；其余依次为
  16、20、50、16、16 条 Passed，无跳过或未分类 outcome。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 为 0 warning / 0 error；
  `task03-round2-solution-build.log`。最终真实 Host smoke 为 bootstrap 200 精确 ready JSON、Channel
  TCP connected、stdout 含 `Application started`，进程和专用临时目录均已清理；
  `task03-round2-host-smoke.log`。
- `git diff --check` 无输出、exit 0。

### Remaining

`QR1-F1` / `QR1-F2` 的执行修复与直接回归已完成，等待 controller 安排新的独立 quick review。
本轮未修改其他产品文件、Game/WPF/用户文档、registry 或既有 review，未提交 Git；TASK-04/TASK-06
交接保持不变。

## Independent Review Round 2

- Reviewed execution: `Execution Round 2` 及 TASK-03 累积 owned outcome；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2 与 approved Plan。依 `reviewing-task-units-quick` 执行新独立局部复审，重审 QR1-F1 / QR1-F2 及其直接回归，抽查 RQ-003—RQ-005、RQ-013—RQ-016 与 BD-001 / BD-004 / T-005；不裁定完整 Spec 或其他 Task。
- Reviewed identity: 独立按既定算法重算 21 个 owned 产品/测试文件 manifest，结果为 `BD3C607D1E428BBAF565E92A0D9A7EA458B7285963ECC750BF336CAB55EE4920`，与 Round 2 记录一致。两个修复文件 SHA256 亦一致。本轮只追加此 review，未改产品、registry、其他 Task 或提交 Git。

### Findings and Prior Finding Disposition

未发现本 Task 范围内新的明显阻塞缺陷；QR1-F1 / QR1-F2 均已由当前修复闭合。

- QR1-F1：register/login endpoint 均携带同一 `CredentialEndpointMetadata`；显式 pipeline 顺序为 exception middleware → `UseRouting` → credential transport gate → endpoint。gate 判断的是已经匹配的 endpoint，不再维护比实际路由更窄的 Path 字符串集合，因此 routing 接受的等价形态必然进入相同门禁。两接口的标准路径及末尾斜杠路径，对合法和畸形远程明文 body 均在读取前返回 400 `secure_transport_required`，独立复跑逐例通过 `ReadCount=0`、command calls=0。loopback HTTP / 远程 HTTPS 尾斜杠控制组均正常派发一次。未用“只额外列举单个尾斜杠”代替路由同源修复。
- QR1-F2：`ReadCredentialsAsync` 在 body reader 前调用 `HasJsonContentType()`；缺失或不支持 media type 直接走既有 400 `invalid_request`，零 body 读取、零命令，响应不含哨兵秘密。支持的 `application/json` / `application/problem+json` 畸形 body 仍为 400；没有扩大 catch 到所有 `InvalidOperationException`。命令主动抛出含哨兵的 `InvalidOperationException` 的独立复跑仍返回 500 `internal_error`，响应/捕获日志不回显异常文本，未把内部故障误吞为输入错误。

### Independent Evidence and Regression Checks

- 独立逐项解析六份 `../evidence/task03-round2-*.trx` 的 `UnitTestResult`：RED 为 8 Failed + 8 Passed（失败对应四个尾斜杠 case 和四个缺失/错误 Content-Type case）；GREEN 16 Passed；focused 20 Passed；最终 LauncherApi 50 Passed、Host 16 Passed、Infrastructure endpoint focused 16 Passed，无跳过或未分类 outcome。RED 日志的实际响应与原 findings 一致。
- 新鲜度：产品修复最后修改为 14:34:44，新增允许传输控制组最后修改为 14:39:51（+08:00）。Host/Infrastructure 本轮运行于 14:36:30 / 14:36:29，均晚于产品修复；最终 API 于 14:40:23 开始，晚于最终测试修改。最终 solution build 日志为 0 warning / 0 error；14:38:14 的真实 Host smoke 记录 bootstrap 200、精确 v1 ready JSON、Channel TCP connected、Application started 和专用临时目录清理。
- 评审于 14:43:47（+08:00）独立启动当前 Release `--no-build` 复跑：LauncherApi 全量 50/50、Host 全量 16/16、Infrastructure `PublicEndpointConfigurationTests` 16/16，三命令均 exit 0；逐项 TRX 全 Passed。结果保存在 `C:\Users\Gloria\AppData\Local\Temp\task03-review-round2-4abc545f147441f2865ab5c79635d108\reviewer-launcherapi.trx` / `reviewer-host.trx` / `reviewer-infrastructure.trx`。
- 抽查 bootstrap / credential readiness 503、register 201/400/409、login 200/400/401、字段错误和非字段错误的 `fieldErrors` 形状、错误密码与缺失账号一致 401；现有真实 Kestrel + SQLite 八路注册竞态准确一次创建的 Host 测试再次通过。HTTP endpoint 仍只派发 TASK-01 命令，响应 Channel tuple 仅取已校验配置快照；三类广告端点独立读取，缺失/非法值不回落 listener，非法 allowlist fail-fast。
- Host 禁用分支仍仅使用 Generic Host，双隔离 Host 不占共享 HTTP 端口的回归通过；启用分支使用配置 ListenUrl、相同 content root / experience 配置及原模块，实际 Host listener 与启动失败检查通过。ready 仅在认证依赖可解析、端点快照可用且 Networking 启用时开放；停止时撤销。未声称该 readiness 是持续数据库健康探测。
- LauncherApi 项目引用仍准确限定为 Abstractions / Application / Data，无直接 Persistence / Infrastructure / Networking / Protocol 引用。异常只记录稳定类别及 trace id，无新增 body logging；安全异常测试再次通过。
- Infrastructure 产品本轮未变化，保留 Round 1 全量证据：独立解析为 300 Passed + 2 NotExecuted；隔离无 PVF 的两条对应条件分支为 2 Passed，未把前者误报为单次 302/302。评审 `git diff --check` 无输出、exit 0。

### Scope and Evidence Limits

读取计数和伪远端地址检查使用真实 ASP.NET middleware / endpoint routing pipeline，但命令是测试替身；真实 Kestrel + SQLite 成功/竞态/凭据失败由 Host 测试补证，不冒充公网 HTTPS 或代理部署验证。尾斜杠是本轮直接自动化覆盖的等价形态；其他可匹配形态受同一个 endpoint metadata 机制保护，没有宣称穷举所有 URI 字符串。HTTP→Channel Info→Game 跨端链、真实 Game 故障注入及最终运行文档仍由 TASK-04 / TASK-06 接续。

Verdict: `passed`
