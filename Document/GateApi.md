# gate 接口

网关（`DNF-JP-86-Launcher` 仓）与单机启动器访问服务端的 HTTP 接口。部署、联调或修改这组接口前阅读。实现在 `Server/src/Dfo.Server.HttpApi`（`GateOptions`、`GateAuthenticationMiddleware`、`GateAuthFailureLimiter`、`GateEndpoints`）。

## 端点

三个端点只挂在 LauncherAuth 监听面（`LauncherAuth:ListenUrl`），需要 `LauncherAuth:Enabled=true`。从 GmWeb 端口用正确的方法访问返回 404，不认证、不计数。

方法不匹配（如 `GET …/login`、`POST …/status`）时一律返回 405 并带 `Allow` 头，不认证、不计数。这一行为不受 token 配置和监听端口的影响：未配置 token 时、在 GmWeb 端口上，用错误的方法访问 gate 路径同样返回 405。

| 端点 | 用途 | 就绪条件 |
|---|---|---|
| `GET /api/gate/v1/status` | 网关轮询服务端状态、比对 PVF；单机启动器取频道地址等 | HTTP 与游戏服务都就绪，且能取到 PVF 摘要 |
| `POST /api/gate/v1/register` | 注册账号 | HTTP 就绪 |
| `POST /api/gate/v1/login` | 登录，签发游戏票据 | HTTP 与游戏服务都就绪 |

register/login 的请求体为 JSON `{loginId, password}`（不超过 8192 字节）。响应：

| 端点 | 成功 | 失败 |
|---|---|---|
| register | 201 `{loginId}` | 400 `invalid_request`（可带 `fieldErrors`）、409 `account_exists` |
| login | 200 `{mId, ticket, expiresAtUtc, channel}` | 400 `invalid_request`、401 `invalid_credentials` |

错误响应体统一为 `{code, message}`，部分 400 另带 `fieldErrors`。未就绪返回 503 `service_unavailable`。

原启动器接口（路径前缀 `…/launcher/v1`，含 bootstrap、register、login）已删除，访问返回 404。一键启动包与旧版启动器不再能登录，需改用单机启动器并配置 token。

## 认证

请求头 `Authorization: Bearer <token>`。scheme 不区分大小写，scheme 与 token 之间可有一个或多个空格；token 部分原样比较；HTTP 服务器会去掉头值首尾的空白。

| 情况 | 响应 |
|---|---|
| 服务端未配置 token | 三个端点用正确的方法访问都返回 404 `not_found`，不读请求体，不计数 |
| token 正确 | 进入端点；不受限流影响 |
| token 缺失、错误或用了其他认证方式 | 401 `unauthorized`，带 `WWW-Authenticate: Bearer` |
| 同上，且来源 IP 已被限流 | 429 `too_many_requests` |

login 的 401 有两种 `code`：`unauthorized` 表示 token 错误，`invalid_credentials` 表示账号或密码错误。网关原样转发服务端响应，启动器据此区分提示。

## 配置

| 键 | 默认值 | 说明 |
|---|---|---|
| `LauncherAuth:GateToken` | 未配置 | 环境变量 `LauncherAuth__GateToken`。去掉首尾空白后为空即未配置 |
| `LauncherAuth:GateAuthFailureLimit` | 10 | 一个窗口内的 token 失败上限，正整数 |
| `LauncherAuth:GateAuthFailureWindowSeconds` | 900 | 窗口长度（秒），正整数 |
| `LauncherAuth:Launch` | `a21` | status 的 `launch` |
| `LauncherAuth:AdminUrl` | 空 | status 的 `admin`，须为 http/https 绝对地址 |
| `LauncherAuth:PublicChannel:Host`、`:Port` | 仓库 `appsettings.json` | status 与 login 响应的 `channel` |

以上值在 Host 启动时读取一次，修改后需重启。限流、`AdminUrl` 的值不合法时 Host 启动失败。gate 配置无论 LauncherAuth 是否启用都会校验，所以 token 等值不合法时，只开 GmWeb 的 Host 也会启动失败。

### token

- 配置管线顺序：`appsettings.json` → `appsettings.{Environment}.json` → 环境变量 → 命令行 → `experience.json`，后者覆盖前者。token 只放在部署机的 `appsettings*.json` 或环境变量里，环境变量优先；不要放进命令行或 `experience.json`，也不要提交到仓库。发布脚本 `Tool/Release/release.py` 拒绝打包非空的 `LauncherAuth:GateToken`，发布冒烟不继承构建机上的同名环境变量。
- 至少 32 个字符，只允许可打印 ASCII 且不含空白（0x21–0x7E）。不满足时 Host 启动失败，错误信息指出键名和违反的规则，不含 token 值。
- 启用 LauncherAuth 但未配置 token 时，启动日志有一条 Warning，gate 端点全部 404。
- 生成：

  ```powershell
  [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
  ```

- token 等同凭据。网关与单机启动器共用同一个 token。轮换步骤：改服务端配置 → 重启 → 网关重新绑定 → 更新单机启动器的 `Launcher.ini`。

### 限流

- 只计 token 失败（401/429 的情况），按 TCP 对端地址计数，不看 `X-Forwarded-For`。账号或密码错误不计入。
- 同一来源在一个窗口内失败达到上限后，窗口剩余时间内的 token 失败返回 429；窗口过后恢复 401。
- 有效 token 始终放行，所以换 token 后立即可用，同一来源下配错 token 的调用方也不会锁住持有正确 token 的调用方。
- 服务端前面有反向代理时，所有请求按代理地址计数。
- 限流只压制错误 token 的噪声，防猜测依靠 token 的长度与随机性。

## status 字段

200，`application/json`，字段名 camelCase：

| 字段 | 类型 | 取值 |
|---|---|---|
| `version` | string | 与 GM `server-info` 的 `version` 同源 |
| `state` | string | 固定 `running` |
| `pvfSha256` | string | 当前 PVF 的 SHA-256，64 位十六进制，与 `server-info` 同源 |
| `startedAt` | string | Host 进程启动时间，UTC，ISO 8601 |
| `launch` | string | `LauncherAuth:Launch` |
| `channel` | `{host, port}` | `LauncherAuth:PublicChannel`，与 login 响应相同 |
| `gamePorts` | int[] | `Networking:GamePorts`，与 `server-info` 同一读取规则（无合法值时为 `[10011]`） |
| `login` | string | 固定 `/api/gate/v1` |
| `admin` | string | `LauncherAuth:AdminUrl`；为空且 GmWeb 启用时为 `GmWeb:ListenUrl` 去掉末尾 `/` 后加 `/gm/`；仍为空时不输出 |
| `registration` | bool | 固定 `true` |

GmWeb 只监听本机地址时，推导出的 `admin` 对网关无用，需在 `LauncherAuth:AdminUrl` 写可达地址。

以下任一情况返回 503 `service_unavailable`：HTTP 未就绪、游戏服务未就绪、PVF 摘要取不到（缺 PVF 或读取失败）。

## 调用方如何解读 status 结果

| 结果 | 含义 | 网关、单机启动器应如何提示 |
|---|---|---|
| 200 | 正常 | — |
| 401 `unauthorized` | token 错误 | token 错误 |
| 429 `too_many_requests` | token 错误次数过多，正被限流 | 按 token 错误处理 |
| 404 | 未配置 gate token，或路径、端口不对 | — |
| 503 `service_unavailable` | 服务端未就绪 | 暂不可用 |
| 连接失败或超时 | — | — |

401 与 429 都只表示 token 失败。按轮换步骤重新绑定之前，网关用旧 token 轮询会先得到 401，同一来源达到失败上限后在窗口剩余时间内得到 429，两者应给出同一种提示。每次重启加载 PVF 期间 status 返回 503，属于暂时状态。

## 客户端 IP

token 正确的请求带可解析的 `X-Forwarded-For` 时，取最后一个逗号分隔项作为客户端 IP，否则取 TCP 对端地址。客户端 IP 目前只写入 register/login 日志。token 错误的请求中的 `X-Forwarded-For` 不写日志、不参与限流。

## 端口暴露

网关和单机启动器都直连 LauncherAuth 监听端口。只用网关时，可以只对网关开放这个端口；有单机启动器直连时，按单机启动器所在的网络开放。

## HTTPS

服务端不按传输方式限制 register/login：token 正确的请求经明文 HTTP 访问也会处理。跨公网部署必须由运维在 LauncherAuth 监听端口上配置 HTTPS，否则口令和 token 以明文传输。

- 启用 HTTPS：把 `LauncherAuth:ListenUrl` 写成 `https://` 地址，并配置 Kestrel 证书（ASP.NET Core 标准键 `Kestrel:Certificates:Default:Path`、`:Password`，与其他配置同一管线）；或者把服务端放在反向代理之后，由代理终止 TLS。
- 放在反向代理之后时，限流按代理的 IP 计数；`X-Forwarded-For` 的最后一项是代理追加的地址，日志中的客户端 IP 因此是代理所见的上一跳。

## HTTP 接口分布

| 监听面 | 调用方 | 接口 | 认证 |
|---|---|---|---|
| GmWeb（`GmWeb:ListenUrl`） | GM 页面 | `/api/gm/v1/*`、`/gm/*` | GM 会话，不变 |
| LauncherAuth（`LauncherAuth:ListenUrl`） | 网关、单机启动器 | `/api/gate/v1/*` | gate token |
| Channel/Game（TCP） | 游戏客户端 | 游戏协议 | 票据 |

## 联调用户测试顺序

1. 按服务端流程，用当前主干数据库准备测试服务，并配置 `LauncherAuth__GateToken`。
2. 网关后台绑定服务端：先填错误的 token，应提示 token 错误；再填正确的 token，状态应为"健康"，PVF 一致。
3. 联网版启动器经网关注册新账号、登录、启动游戏，应进入选人界面。退出后重新登录。
4. 单机启动器在 `Launcher.ini` 中配置地址和 token，用已有账号登录并进入游戏。
5. 把单机启动器的 token 改错，应提示 token 错误，而不是账号或密码错误。
6. 回归：GM 后台登录，并查看服务端信息页。
