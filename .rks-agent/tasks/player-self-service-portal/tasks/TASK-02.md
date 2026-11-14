# TASK-02 — 后台受信代理识别（#305，GM 侧）

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付配置项 `GmWeb:TrustedProxies`（解析、规范化、非法值启动失败），以及只作用于 GmWeb 监听端口、位于 `GmAccessBoundary` 之前的 `ForwardedHeaders` 接入；GM 侧的 HTTPS 判断、同源检查、会话 Cookie `Secure`、登录限流来源键因此使用解析后的来源与协议，未配置时行为不变；启动器网关接口不受影响。对应 Plan T-001 与 SD-4、SD-5（GM 侧来源键）、Plan Verification；覆盖 RQ-030、RQ-031、RQ-032、RQ-034，AC-030、AC-031（GM 侧）、AC-033、AC-034（网关不变、IPv4 映射地址、非法配置）。AC-031/AC-032/AC-034 的玩家侧用例、`GmLoginLimiter` 的玩家实例（RQ-033）属于 T-003，不在本单元。
- Owned boundary:
  - `Server/src/Dfo.Server.GmWeb/GmWebOptions.cs`（新增 `TrustedProxies` 及其解析与校验）
  - `Server/src/Dfo.Server.GmWeb/GmWebModule.cs`（`ConfigureMiddleware` 中按端口接入 `ForwardedHeaders`）
  - `Server/src/Dfo.Server.GmWeb/GmAccessBoundary.cs`：预期不改逻辑；只允许在 `SecureTransport` 上补一行说明"作用于解析后的值"。`GmAuthenticationEndpoints.cs` 预期不改（来源键已是 `Connection.RemoteIpAddress`），测试证明不够时才动，并在结果中说明。
  - `Server/tests/Dfo.Server.Host.Tests/GmTestHost.cs`（增加按请求指定直连来源的测试能力，现有参数与行为不变）
  - `Server/tests/Dfo.Server.Host.Tests/GmTrustedProxyHttpTests.cs`（新建，GM 侧 HTTP 用例）
  - `Server/tests/Dfo.Server.Host.Tests/GmWebOptionsTests.cs`（新建，配置解析用例）
  - `Server/tests/Dfo.Server.Host.Tests/GateHttpHostTests.cs`（`GateHost.StartAsync` 增加附加配置参数；新增"网关不受影响"用例）
  - `Document/gm-web-admin.md`「访问与审计」一节中"本阶段没有反向代理信任配置"一段改写为受信代理的配置与语义说明（该句在本单元完成后即为错误记载）
  - 不改 `Dfo.Server.HttpApi`（LauncherAuth / gate）生产代码，不改 Application / Persistence，不加任何玩家侧代码，不改 `appsettings.json`。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 零警告（`KnownNetworks` 在 .NET 10 已过时，用了会以警告即错误失败）。
  - 本单元新增测试：`GmWebOptionsTests`、`GmTrustedProxyHttpTests`、`GateHttpHostTests` 中的新用例全部通过。
  - 回归：`Dfo.Server.Host.Tests` 中 `Gm*`、`GateHttpHostTests`、`CredentialEntryHttpTests`、`LauncherHttpHostStartupTests`、`LauncherAuthenticationEndToEndTests` 原样通过（AC-030 的"现有 GM 测试原样通过"即以此为证）；`Dfo.Server.HttpApi.Tests` 全部通过。
- Dependencies: 无。
- Result review: required — 本单元改的是后台的访问控制前提：来源地址与协议决定 HTTPS 拒绝、同源检查、Cookie `Secure` 和登录限流。做错的后果是安全性退化且平时不显形——例如信任范围多放了一个默认回环网络、映射地址未规范化、转发层数未限制、处理波及网关端口，都会让伪造的 `X-Forwarded-*` 生效或让网关客户端 IP 改变，而功能性的"能登录"测试照样通过；T-003 的玩家侧放行用例与 SD-2 的共用边界都建立在本单元得出的解析值上。必需检查只能覆盖本计划列出的具体情形，需要独立审查确认没有漏掉的信任路径。

## Detailed Plan

### 1. 配置解析（`GmWebOptions`）

给记录增加 `IReadOnlyList<IPAddress> TrustedProxies`（`init` 属性，默认空数组，避免改动现有位置参数）。只在 `GmWeb:Enabled=true` 时解析（与 `ListenUrl`、`IconPack` 的既有做法一致：后台关闭时这些配置不生效，也不校验）。

读取规则（本单元定下）：

- 来源：`GmWeb:TrustedProxies` 节的子项值（JSON 数组 / `GmWeb__TrustedProxies__0` 环境变量），加上该节自身的标量值按逗号拆分（方便 `--GmWeb:TrustedProxies=1.2.3.4,5.6.7.8` 命令行写法）。两者都没有即为空列表。
- 每一项去掉首尾空白后必须是**单个 IP 地址**，否则抛 `InvalidOperationException`，消息含 `GmWeb:TrustedProxies` 与出错的原值。判为非法的情形：空项；`IPAddress.TryParse` 失败（含主机名、CIDR `10.0.0.0/8`、带端口 `127.0.0.1:80`）；IPv4 非标准写法（`TryParse` 接受但 `ToString()` 与输入不一致，如 `127.1`、`0x7f.0.0.1`）；带 IPv6 作用域（`ScopeId != 0`，如 `fe80::1%3`）；未指定地址 `0.0.0.0`、`::`（作为"代理地址"没有意义，避免被误读为"信任全部"）。
- 规范化：`IsIPv4MappedToIPv6` 的地址转为 IPv4（`MapToIPv4()`），去重后保存。这样配置写 `::ffff:x` 与写 `x` 等价；中间件自身已把来源的映射形式按 IPv4 比对（Plan SD-4），两者合起来满足 RQ-034"规范化后比较"的两个方向。

### 2. 管道接入（`GmWebModule.ConfigureMiddleware`）

`TrustedProxies` 为空：一行不变，只 `UseMiddleware<GmAccessBoundary>()`——未配置时管道与现在完全相同（RQ-030 由构造保证）。

非空：在 `UseMiddleware<GmAccessBoundary>()` 之前加

```csharp
application.UseWhen(context => context.Connection.LocalPort == options.ListenUrl!.Port,
    branch => branch.UseForwardedHeaders(forwarded));
```

`forwarded` 在模块构造时建好一次：`ForwardedHeaders = XForwardedFor | XForwardedProto`，`ForwardLimit = 1`，`KnownIPNetworks.Clear()`、`KnownProxies.Clear()`（默认值含 `127.0.0.0/8` 与 `::1`，必须清掉），再逐个 `KnownProxies.Add(...)`。不设 `RequireHeaderSymmetry`（保持 false）：代理只给 `X-Forwarded-For` 不给 `X-Forwarded-Proto` 时，来源改为客户端、协议保持 http，远程请求随之被 HTTPS 规则拒绝；若设为 true，整条转发信息会被丢弃、请求回落为"来自代理本机"，在回环代理部署下反而被当作安全连接。

位置说明：feature 中间件排在 `UseHttpApiMiddleware`（异常、路由、监听端口边界、网关认证）之后，网关认证只对网关端口的网关路由起作用，且 `HttpApiHostingExtensions` 要求各功能端口互不相同，所以按 `LocalPort` 分支后网关端口的请求永远不经过 `ForwardedHeaders`（RQ-034）。路由在转发处理之前完成，本应用的路由不依赖来源与协议，不受影响。

### 3. 解析值在 GM 侧的效果（预期无需改代码，由测试证明）

- `SecureTransport`：`Request.IsHttps` 读的是被替换后的 `Scheme`，回环判断读的是被替换后的 `RemoteIpAddress`，即 RQ-032 的语义；"代理本身不带转发头的本机请求"保持直连回环，仍视为本机。
- 同源检查：`{Request.Scheme}://{Request.Host}` 中 `Scheme` 已为原始协议；`Host` 依赖代理保留 `Host` 头（本单元不处理 `X-Forwarded-Host`，与 #305 建议一致）。
- 会话 Cookie：`Secure = Request.IsHttps`。
- GM 登录限流：`RemoteIpAddress!.ToString()` 为解析后的客户端地址。

若某项测试表明不能自然生效，才在所有权范围内修改对应文件，并在结果中说明。

### 4. 测试设施

**`GmTestHost`**：在现有 `RemoteFeature` 的思路上增加一个可选参数（例如 `bool peerOverride`），启用时包一层 feature，在内层 `ConfigureMiddleware` 之前插入测试中间件：请求带 `X-Test-Peer: <ip>` 时把 `Connection.RemoteIpAddress` 设为该地址并删除此头。这样"直连来源"在转发处理之前被设定，等价于从该地址连入；不带此头时就是真实的 `127.0.0.1` 回环连接。`remoteConnection` 及其他参数行为不变。受信代理通过已有的 `configuration` 参数传入（`["GmWeb:TrustedProxies:0"] = "192.0.2.1"`），`GmWebModule` 在叠加该字典之后才构造，可直接生效。

**会话 Cookie 带 `Secure` 后的客户端坑**：`HttpClientHandler` 的 `CookieContainer` 不会在明文 http 连接上回送 `Secure` Cookie，而测试连接本身是 http。凡需要"经代理以 https 登录后再发需要会话的请求"的用例，从登录响应的 `Set-Cookie` 取出 `Dfo.Gm.Session` 的值，以非 `Secure` 的 `Cookie` 加入该客户端的 `CookieContainer`（或手动带 `Cookie` 头），再取新的防伪令牌（令牌与身份绑定）。

**`GateHttpHostTests.GateHost.StartAsync`**：增加可选的附加配置字典，合并进 `LauncherHost.Create()` 的设置。

### 5. 用例（地址取文档保留段：代理 P=`192.0.2.1`，客户端 C1=`198.51.100.11`、C2=`198.51.100.12`）

`GmWebOptionsTests`（直接调用 `GmWebOptions.FromConfiguration`）：

- 未配置 → 空列表；IPv4、IPv6、数组与逗号标量两种写法都能读出；`::ffff:192.0.2.1` 规范化为 `192.0.2.1` 并与重复项合并。
- 上文列出的每类非法值 → `InvalidOperationException`，消息含 `GmWeb:TrustedProxies`。
- `GmWeb:Enabled=false` 时非法值不报错。
- 启动层面：`GmTestHost.StartAsync(configuration: 非法值)` 抛出且消息含配置项名（AC-034 非法配置，证明错误会让 Host 启动失败）。

`GmTrustedProxyHttpTests`：

- **AC-030 未配置**：真实回环连接带任意 `X-Forwarded-For`（每次换值）与 `X-Forwarded-Proto: https`：同一登录名连续输错 5 次后第 6 次 429（限流仍按直连地址）；正确登录的 Cookie 不带 `Secure`；`Origin: https://<本站主机>` 的修改请求被拒（协议仍是 http）。现有 `RemoteHttpRejectsSpoofedForwardingHeaders` 等 GM 测试原样通过作为其余部分的证据。
- **AC-031（GM 侧）**，配置 `[P]`，经 `X-Test-Peer: P` 且带 `X-Forwarded-Proto: https`：
  - `X-Forwarded-For: C1` 输错 5 次后第 6 次 429；随后 `X-Forwarded-For: C2` 用正确密码登录 200。
  - `X-Forwarded-For: C2, C1`（左侧为客户端自带的伪造项）计入 C1，同样 429——证明 `ForwardLimit = 1` 只取最近一跳。
  - 非 P 来源（不带 `X-Test-Peer` 的真实回环，此配置下不受信）每次换 `X-Forwarded-For` 输错，第 6 次 429——伪造头不起作用，按直连地址计数。
- **AC-032（GM 侧，供 T-003 对照）**，配置 `[P]`：经 P 转发 `https` 登录，Cookie 带 `Secure`；`Origin: https://<本站主机>` 的登录与登录后的 `POST /api/gm/v1/auth/logout` 通过，外站 `Origin` 被拒 403；非 P（真实回环）伪造 `X-Forwarded-Proto: https` 登录成功但 Cookie 不带 `Secure`，且 `Origin: https://<本站主机>` 被拒。
- **AC-033**，配置 `[127.0.0.1]`，真实回环即为 P：`X-Forwarded-For: 198.51.100.20` + `X-Forwarded-Proto: http` → `/api/gm/v1/auth/session` 与登录均 403 且消息为需要 HTTPS；只带 `X-Forwarded-For`、不带协议头 → 同样 403；不带任何转发头 → 取防伪令牌与登录均 200（视为本机）。
- **AC-034 映射地址**：配置 `[127.0.0.1]`，`X-Test-Peer: ::ffff:127.0.0.1` + `X-Forwarded-For: C1` + `X-Forwarded-Proto: https` → 登录 Cookie 带 `Secure`，且对 C1 输错的计数与另一 `X-Forwarded-For` 值互不影响（证明被当作受信代理）；反向：配置 `[::ffff:192.0.2.1]`，`X-Test-Peer: 192.0.2.1` 转发 `https` → Cookie 带 `Secure`。对照：配置 `[P]`、`X-Test-Peer: 192.0.2.2` 转发 `https` → 来源仍为非回环远程 http，403。

`GateHttpHostTests` 新用例（AC-034 网关不变），`[Theory]` 对"配置 `GmWeb:TrustedProxies:0=127.0.0.1` / 未配置"各跑一遍，断言两次结果相同。测试客户端从回环连入，若转发处理误作用于网关端口，回环正好会被当作受信代理，因此能暴露错误：

- 设 `LauncherAuth:GateAuthFailureLimit=3`，用错误令牌、每次不同的 `X-Forwarded-For` 访问网关：前 3 次 401、第 4 次 429（网关失败计数按直连地址）。
- 用正确令牌带 `X-Forwarded-For: 198.51.100.7, 203.0.113.9` 调 `/api/gate/v1/register`：日志出现 `Gate register from 203.0.113.9`（网关自己取最后一跳；若被转发中间件处理过，该跳会被消费，日志会变成别的地址）。

### 6. 文档

改写 `Document/gm-web-admin.md`「访问与审计」第一段：默认不信任转发头；配置 `GmWeb:TrustedProxies`（IP 列表，数组或逗号分隔，非法值启动失败）后，只对这些直连地址发来的后台请求采用 `X-Forwarded-For` 最近一跳与 `X-Forwarded-Proto`，用于 HTTPS 判断、同源检查、Cookie `Secure` 与登录限流；代理须保留 `Host` 并覆盖（而非透传）这两个头；只作用于后台监听端口，网关接口照旧。同时删去"不要用代理头替代 HTTPS"一类与新语义冲突的措辞。

### 7. 执行顺序与验证

1. `GmWebOptions` + `GmWebOptionsTests`（先写失败用例）。
2. `GmWebModule` 接入；`GmTestHost` 增加来源指定能力；`GmTrustedProxyHttpTests` 逐条转绿。
3. `GateHost` 附加配置与网关用例。
4. 文档。
5. `dotnet build Server/86JP.Server.slnx`；本单元测试；按 Required checks 的回归集合运行，例如 `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~Dfo.Server.Host.Tests.Gm|FullyQualifiedName~GateHttpHostTests|FullyQualifiedName~CredentialEntryHttpTests|FullyQualifiedName~LauncherHttpHostStartupTests|FullyQualifiedName~LauncherAuthenticationEndToEndTests"` 与 `dotnet test Server/tests/Dfo.Server.HttpApi.Tests`（长命令后台运行；`Document/BuildAndTest.md` 记载 Host.Tests 满载时可能因就绪预算超时偶发失败，遇到时单独重跑失败用例确认）。

不在本单元处理：进程级 `ASPNETCORE_FORWARDEDHEADERS_ENABLED` 环境变量（默认主机构建器会据此全局启用转发处理）属于部署环境的既有行为，本单元不改动也不依赖它。

## Result

按 Revision 1 计划完成，未越出所有权范围，未提交。第 2 轮：按 Quick Review 1 的 F-1 修正空值处理，见下文"第 2 轮修正"。

### 改动

- `Server/src/Dfo.Server.GmWeb/GmWebOptions.cs`：新增 `TrustedProxies`（`init`，默认空）与 `GmWeb:TrustedProxies` 解析。
  - 数组子项与逗号分隔标量都读。整节值为空或只有空白（JSON 空数组 `[]`、空环境变量）视为未配置，得到空列表。
  - 下列情形抛 `InvalidOperationException`，消息含配置项名与原值：逗号写法中的空项（如 `a,,b`）、空的数组子项、非 IP、CIDR、带端口、方括号、非标准 IPv4、作用域、未指定地址。
  - 映射地址转 IPv4 并去重。只在后台启用时解析。
- `Server/src/Dfo.Server.GmWeb/GmWebModule.cs`：列表非空时，在 `GmAccessBoundary` 之前用 `UseWhen(LocalPort == GmWeb 端口)` 接入 `UseForwardedHeaders`（`XForwardedFor | XForwardedProto`，`ForwardLimit = 1`，清空 `KnownIPNetworks`、`KnownProxies` 后只放配置地址）。列表为空时管道与原来相同。
- `Server/src/Dfo.Server.GmWeb/GmAccessBoundary.cs`：只在 `SecureTransport` 上加一行说明，逻辑未改。`GmAuthenticationEndpoints.cs` 未改：限流来源键、Cookie `Secure`、同源检查都自然使用解析后的值，由下列测试证明。
- 测试：
  - 新建 `GmWebOptionsTests.cs`（21 例，含第 2 轮新增 3 例）、`GmTrustedProxyHttpTests.cs`（8 例）。
  - `GmTestHost.cs` 增加 `peerOverride` 参数与测试专用的 `X-Test-Peer` 包装：该请求头在 GmWeb 中间件之前改写直连来源。按协调方要求，它只存在于测试工程，生产代码中没有任何按请求头改写来源的路径（`Server/src` 中检索 `X-Test-Peer` 与 `RemoteIpAddress =` 均无结果）。
  - `GateHttpHostTests.cs` 中 `GateHost.StartAsync` 增加附加配置参数，并新增"网关不受影响"用例（配置 / 未配置各一例）。
- `Document/gm-web-admin.md`「访问与审计」：删去"没有反向代理信任配置"的错误记载，改写为受信代理的配置与语义。

### 验收对照

| AC | 证据 |
|---|---|
| AC-030 | `Without_trusted_proxies_forwarding_headers_change_nothing`：限流键、Cookie、同源检查不受转发头影响。现有 GM 测试（含 `RemoteHttpRejectsSpoofedForwardingHeaders`、`RealHttpsIssuesSecureCookie`）原样通过 |
| AC-031（GM 侧） | `Clients_behind_a_trusted_proxy_are_limited_separately_and_forged_headers_count_by_connection`：C1 被限而 C2 可登录；`C2, C1` 计入 C1；非受信来源按直连地址计数。另有 `Only_one_forwarded_hop_is_read_even_when_it_is_a_listed_proxy` |
| AC-032（GM 侧，供 T-003 对照） | `Forwarded_https_from_the_proxy_sets_secure_and_passes_same_origin_while_a_forged_one_does_not` |
| AC-033 | `Forwarded_remote_plain_http_is_refused_and_the_proxys_own_request_is_local`：协议为 http 与只带 XFF 的两种情形都被拒；不带转发头视为本机 |
| AC-034 映射地址 | `A_mapped_peer_matches_an_IPv4_entry`、`A_mapped_entry_matches_an_IPv4_peer`、`A_peer_outside_the_list_stays_a_remote_plain_http_connection` |
| AC-034 非法配置 | `GmWebOptionsTests` 中各非法值用例（含 `a,,b`），以及 `An_invalid_entry_stops_the_host_from_starting` |
| RQ-030 默认空 | `Unconfigured_trusts_no_proxy`、`An_empty_or_blank_setting_value_means_no_proxies("" / "   ")`、`An_empty_JSON_array_means_no_proxies` |
| AC-034 网关不变 | `Gm_trusted_proxies_leave_the_gate_client_address_and_failure_limit_unchanged(false/true)` |

### 检查结果

- `dotnet build Server/86JP.Server.slnx`：0 警告 0 错误（第 2 轮修正后重跑）。
- 本单元测试：第 1 轮 28 例全部通过；第 2 轮修正后 31 例全部通过。
- 回归：`Dfo.Server.Host.Tests` 中 `Gm*`、`GateHttpHostTests`、`CredentialEntryHttpTests`、`LauncherHttpHostStartupTests`、`LauncherAuthenticationEndToEndTests` 共通过 197 例、跳过 12 例。跳过的都是需要 `PVF_ARCHIVE_PATH` 真实 PVF 的内容集成用例，与本单元无关。`Dfo.Server.HttpApi.Tests` 146 例全部通过。
- 变异核对（核对后已恢复原代码）：
  - 不清空 `KnownIPNetworks` 并去掉端口条件：AC-031、AC-032 两例失败。
  - `ForwardLimit = 2`：`Only_one_forwarded_hop…` 失败。
  - 第 2 轮：把空值判断恢复为修正前的 `section.Value is { } scalar`，新增的 3 例（`""`、`"   "`、JSON `[]`）全部失败。由此确认 JSON 空数组确实读成 `""`，且新用例能区分修正前后的行为。
- 回归集合（Host `Gm*`/Gate/Launcher 与 `HttpApi.Tests`）是第 1 轮的结果。第 2 轮只改了 `ReadTrustedProxies` 里标量值的一处判断，未重跑回归集合。

### 第 2 轮修正（F-1）

- `ReadTrustedProxies` 的整节标量值改为 `!string.IsNullOrWhiteSpace(section.Value)` 时才按逗号拆分。空或只有空白即视为未配置；非空标量中的空项仍逐项判非法。
- 新增用例 `An_empty_or_blank_setting_value_means_no_proxies`（`""`、`"   "`）与 `An_empty_JSON_array_means_no_proxies`（`AddJsonStream` 读 `"TrustedProxies": []`）。既有用例 `An_empty_entry_in_the_comma_form_fails_naming_the_setting`（`a,,b`）仍通过。
- `Document/gm-web-admin.md` 的写法（"JSON 数组，或逗号分隔的单个值；默认空"）已与修正后的行为一致，未再改动。

### 发现与说明

- **端口过滤无法单独由测试暴露。** 网关认证中间件（含失败计数与 `GateClientAddress` 解析）在 `UseHttpApiMiddleware` 中，排在所有 feature 中间件之前。因此即使去掉 `LocalPort` 条件，网关结果也不变。网关用例证明的是 AC-034 的整体结论"网关结果与未配置时相同"，端口条件属于纵深防护。
- **测试客户端的 Secure Cookie 回送。** 测试连接是明文 http，客户端不会回送带 `Secure` 的会话 Cookie。AC-032 用例把会话 Cookie 以非 `Secure` 形式重新放入 Cookie 容器，再继续发需要会话的请求。
- T-003 补玩家侧用例时，可直接复用 `GmTestHost` 的 `peerOverride` 与 `GmTrustedProxyHttpTests` 中的请求构造方式。

## Result Review

### Independent Quick Review 1

- 审查对象：TASK-02 契约 Revision 1 的执行结果（上文 `## Result`），基线 `c23bbb92d` 之上的未提交改动（`GmWebOptions.cs`、`GmWebModule.cs`、`GmAccessBoundary.cs`、`GmTestHost.cs`、`GateHttpHostTests.cs`、新建 `GmTrustedProxyHttpTests.cs`、`GmWebOptionsTests.cs`、`Document/gm-web-admin.md`）。
- 依据：本 Task 契约与详细计划；spec RQ-030–RQ-032、RQ-034，AC-030、AC-031（GM 侧）、AC-033、AC-034；reviewing-work 的 Task Unit + Code 准则（局部范围）。
- 复跑：`dotnet build Server/86JP.Server.slnx` 0 警告 0 错误；`GmTrustedProxyHttpTests|GmWebOptionsTests|GateHttpHostTests|GmHttp|RemoteHttp` 筛选 37 通过、1 跳过（需真实 PVF）。

```yaml
kind: rks-review
verdict: changes_required
```

#### 阻塞发现

**F-1 JSON 空数组 `"TrustedProxies": []` 使启动失败。**
- 证据：.NET 10 的 JSON 配置提供程序把空数组表示为该键的值 `""`（不是 `null`）。本审查用 `Microsoft.Extensions.Configuration.Json` 10.0.0 实测 `{"GmWeb":{"TrustedProxies":[]}}`：`GetSection("GmWeb:TrustedProxies").Value == ""`，无子项。`ReadTrustedProxies` 中 `section.Value is { } scalar` 对 `""` 成立，`"".Split(',')` 得到一个空项，`ParseProxy("")` 返回 `null`，于是抛 `InvalidOperationException`。环境变量 `GmWeb__TrustedProxies=`（空值）同理。
- 后果：文档写明配置为"JSON 数组……默认空"，详细计划也写明"两者都没有即为空列表"；但运维按最自然的写法在 `appsettings.json` 里放一个空数组（或模板里预留该键），后台就无法启动。属于失败封闭，不是安全退化，但与 RQ-030"默认空"及文档承诺的写法矛盾，T-003 或部署模板一旦预置该键就会触发。
- 最小修正：整节标量值为空或只有空白时按"未配置"处理（例如 `if (!string.IsNullOrWhiteSpace(section.Value)) entries.AddRange(...)`），逗号形式中的空项（`a,,b`）仍判非法；补一例 `GmWebOptionsTests`：`GmWeb:TrustedProxies = ""` → 空列表（可另加 JSON 空数组用例）。

#### 已核对、无问题

- **伪造绕过**：转发处理只在 `TrustedProxies` 非空时接入；`KnownIPNetworks`、`KnownProxies` 都先清空再只加配置地址（默认的 `127.0.0.0/8`、`::1` 不残留；`AC-031` 中"真实回环不受信"用例实际证明了这一点）。列表非空才接入，避开了"两个列表都空 = 信任所有来源"的中间件语义。`ForwardLimit = 1` 只读最右一跳，`C2, C1` 与"该跳本身也是受信代理"两例都证明不会再往左走。IPv4 映射：配置侧 `MapToIPv4` 规范化，来源侧由中间件按映射比对，双向各有用例；`::ffff:0.0.0.0` 先映射再判未指定地址，顺序正确。
- **端口限定与网关**：`UseWhen(LocalPort == GmWeb 端口)`；网关认证中间件与 `GateClientAddress.Resolve` 在 `UseHttpApiMiddleware` 内、所有 feature 中间件之前执行，`Server/src` 中其余读 `RemoteIpAddress`/`Scheme`/`IsHttps` 的位置只有 GmWeb 的边界、登录限流、Cookie。网关用例对配置/未配置给出相同结果。端口条件无法被测试单独暴露，结果中已如实说明，属纵深防护，不阻塞。
- **未配置不变**：列表为空时 `ConfigureMiddleware` 只剩原来的 `UseMiddleware<GmAccessBoundary>()`；AC-030 用例 + 现有 GM 测试通过。
- **解析值的使用**：HTTPS 判断（`IsHttps` + 回环）、同源（`Request.Scheme`）、Cookie `Secure`（`IsHttps`）、限流键（`RemoteIpAddress`）均读被中间件替换后的值，AC-031/032/033/034 各有区分性用例（如映射来源用例若不受信则 Cookie 不会带 `Secure`）。
- **X-Test-Peer**：只在 `GmTestHost.PeerFeature`（测试工程）；`Server/src` 无此头、无 `RemoteIpAddress =` 赋值。
- **非法配置**：主机名、CIDR、端口、方括号、非标准 IPv4、作用域、未指定地址、空项均抛错且消息含配置项名；后台关闭时不解析；Host 启动失败有用例。
- **GmWebModule.cs 误 checkout 事件**：当前文件相对 `HEAD` 的差异只有新增的 `using Microsoft.AspNetCore.HttpOverrides;` 与 `ConfigureMiddleware` 改写，其余（服务注册、路由映射、静态页）与基线一致，内容完整。
- **文档**：`gm-web-admin.md` 改写与实现一致（除 F-1 所述空数组写法）。

#### 非阻塞残余风险

- 代理若透传而非改写 `X-Forwarded-For`/`X-Forwarded-Proto`，客户端可伪造回环地址或 https；文档已要求代理改写这两个头，属部署前提。

### Independent Quick Review 2

- 审查对象：第 2 轮执行结果（`## Result` 中"第 2 轮修正（F-1）"），当前未提交改动；契约仍为 Revision 1。只复核 F-1 及其修正引出的直接回归，其余结论沿用 Review 1。
- 复跑：`dotnet build Server/86JP.Server.slnx` 0 警告 0 错误；`GmTrustedProxyHttpTests|GmWebOptionsTests|GateHttpHostTests` 39 通过、1 跳过（需真实 PVF）。

```yaml
kind: rks-review
verdict: approved
```

- **F-1 已解决**：`ReadTrustedProxies` 改为 `!string.IsNullOrWhiteSpace(section.Value)` 时才拆分标量值，JSON `[]` 与空环境变量读成 `""` 时视为未配置。新增用例覆盖 `""`、`"   "` 与经 `AddJsonStream` 读入的真实 JSON 空数组；worker 回退修正后这 3 例失败，说明用例能区分修正前后的行为。
- **无直接回归**：逗号形式中的空项（`a,,b`）仍抛错；数组子项为空或空白（`GmWeb:TrustedProxies:0 = ""`）仍判非法，与"每项必须是单个 IP"一致；其余非法值与规范化路径未动。文档"JSON 数组……默认空"的写法现与实现一致。
- 非阻塞：第 2 轮未重跑完整回归集合（Host `Gm*`/Gate/Launcher、`HttpApi.Tests`），改动只限一处标量判断，影响面在本单元用例内已覆盖；最终完成核验时应随整体一并重跑。
