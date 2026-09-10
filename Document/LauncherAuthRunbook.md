# 启动器认证发布与联调手册

本文说明如何发布 `Dfo.Server.Host` 与 .NET 10 WPF 启动器、配置三类独立的公共端点，
以及如何在本机完成一次不启动真实游戏的 loopback 验证。第一版只支持单一登录服；生产域名、
证书签发、自动更新和公网部署不在本次交付范围内。

## 1. 安全边界

- `Launcher.ini` 只能保存 HTTP API 根地址 `BaseUrl`。Channel/Game 地址、端口、账号、密码、票据和
  `DNF.exe` 路径都不能写入该文件。
- 明文 HTTP 只允许 loopback（例如 `http://127.0.0.1:8080/`）。远程客户端必须使用 HTTPS。
- 启动器不会跟随 HTTP 3xx；应把 `BaseUrl` 直接配置为最终 HTTPS/loopback origin。这样 POST body 中的
  密码不会被重定向到第二个地址。
- 当前原型在勾选“保存账号和密码”并成功登录后，会把 `loginId`、`password` 与 `remember` 明文保存到
  `%LOCALAPPDATA%\DfoLauncher\preferences.json`；取消勾选并成功登录会清空已保存值。该文件应按敏感信息
  处理，不得提交、共享或写入日志。正式发布前必须改为 Windows 凭据保护方案。服务端仍只持久化版本化
  密码记录和票据摘要，不持久化原始票据。
- 原始票据默认 60 秒过期且只能成功兑换一次。Game 登录失败、连接中断或成功链投递失败后，应重新在
  启动器登录取得新票据，不能重放旧票据。

## 2. 从仓库发布

在仓库根目录执行：

```powershell
dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj `
  -c Release --self-contained false -o artifacts/launcher-auth/host

dotnet publish Launcher/Dfo.Launcher/Dfo.Launcher.csproj `
  -c Release -r win-x64 --self-contained false -o artifacts/launcher-auth/launcher
```

这两项是 framework-dependent 发布，运行机需要 .NET 10 Runtime；启动器还需要 .NET 10 Desktop Runtime。
发布结果中的 `Launcher.ini.example` 是唯一示例配置。复制为同目录 `Launcher.ini` 后再修改：

```ini
[Launcher]
BaseUrl=http://127.0.0.1:8080/
```

启动器最终必须与要启动的 A21 `DNF.exe` 位于同一目录；它固定使用该目录作为工作目录，并把 35 槽 A21
字符串作为一个进程参数传入。部署时把 `artifacts/launcher-auth/launcher` 中的文件复制到游玩客户端目录，
再创建 `Launcher.ini`。不要覆盖客户端原有的 `DNF.exe`、`Az.dll`、插件或资源，也不要向只读取证目录
`DNFClient-ClientHackLab-20260812` 部署。

## 3. Host 配置

### 3.1 监听值与广告值不是一回事

启用认证闭环至少要同时设置 `LauncherAuth:Enabled=true` 与 `Networking:Enabled=true`。以下三类公共端点
互相独立，允许使用不同 IPv4 主机；代码只验证格式与映射完整性，不会探测 NAT、防火墙、证书或公网
可达性，也不会从 bind 地址或另一类端点推断缺失值。

| 类别 | 配置键 | 客户端在哪里使用 | 对应本地监听 |
|---|---|---|---|
| 公共 Channel bootstrap | `LauncherAuth:PublicChannel:Host`、`LauncherAuth:PublicChannel:Port` | HTTP login 响应及 A21 参数槽 1—2 | `Networking:ChannelPort` |
| Channel 广告的公共 Game | 每个 `Channel:Channels:n:Host`、`:Port` | Channel Info；客户端随后连接并兑换同一票据 | 按索引一一对应 `Networking:GamePorts:n` |
| Game 登录后的后续端点 | `Game:EndpointHost`、`GameLogin:ConnectionPort`、`GameLogin:EndpointPortA`、`GameLogin:EndpointPortB` | 成功 `CMD 0x0001` | 本 Host 不据此自动创建监听器 |

`LauncherAuth:ListenUrl` 是 Launcher HTTP API 的 Kestrel bind URL，也不是公共 Channel 地址。
`Channel:PublicServerIp` 是既有 Channel 配置；显式 `Channel:Channels` 中的 Host 才是本认证闭环逐频道广告的
公共 Game 地址。`Channel:Channels` 与 `Networking:GamePorts` 数量必须相同、顺序对应；监听端口和公网
广告端口可因 NAT 不同，但部署者必须保证外部映射确实通向对应索引的 listener。

本机联调可使用如下 `appsettings.json` 形状（保留文件中的其他业务配置）：

```json
{
  "LauncherAuth": {
    "Enabled": true,
    "ListenUrl": "http://127.0.0.1:8080",
    "PublicChannel": { "Host": "127.0.0.1", "Port": 7001 }
  },
  "Networking": {
    "Enabled": true,
    "ChannelPort": 7001,
    "GamePorts": [10010, 10011]
  },
  "Channel": {
    "PublicServerIp": "127.0.0.1",
    "Channels": [
      { "Id": 10, "Name": "#ch.10", "Host": "127.0.0.1", "Port": 10010 },
      { "Id": 11, "Name": "#ch.11", "Host": "127.0.0.1", "Port": 10011 }
    ]
  },
  "Game": { "EndpointHost": "127.0.0.1" },
  "GameLogin": {
    "ConnectionPort": 10128,
    "EndpointPortA": 17200,
    "EndpointPortB": 17200
  }
}
```

启用 Launcher API 后，上述广告值缺失、非 IPv4、端口越界、Game 映射数量不一致或重复时，Host 会在
启动期失败，而不是静默退回客户端自身 loopback。

### 3.2 HTTPS 与反向代理

远程部署应让 Kestrel 直接监听 HTTPS，并通过标准 Kestrel 证书配置提供证书；例如把
`LauncherAuth:ListenUrl` 设为 `https://0.0.0.0:8443`，再用安全的部署环境配置
`ASPNETCORE_Kestrel__Certificates__Default__Path` 和证书密码。启动器则只配置最终公开 URL：

```ini
[Launcher]
BaseUrl=https://login.example.com:8443/
```

当前 Host **不启用 forwarded headers 中间件，也不信任客户端发送的 `X-Forwarded-*`**。凭据 gate 只看
Kestrel 实际收到的 `Request.IsHttps` 和 TCP 对端是否 loopback，因此必须区分两种 HTTP 上游：

- 非本机代理以 HTTP 连接 Host 时，对端不是 loopback，Host 会在读取凭据 body 前返回
  `secure_transport_required`；该上游必须改为 HTTPS。
- 同机代理经 `127.0.0.1` 以 HTTP 连接 Host 时，会命中开发用的 loopback 例外而被允许；Host 无法从
  forwarded headers 证明外部客户端原本是否 HTTPS。若采用此模式，必须让 Host 只 bind loopback、只由
  同机代理访问，并由代理强制外部 HTTPS、禁止任何绕过代理的入口。此时原始传输保证由代理和本机边界
  承担，不能描述成 Host 已验证外部 TLS。

最清晰的边界仍是 Kestrel 直接 TLS，或代理到 Kestrel 的上游也使用 HTTPS。若以后要让 Host 消费代理
声明，必须作为单独安全变更：在凭据 gate 之前启用 Forwarded Headers，只列出准确的
`KnownProxies`/`KnownNetworks` 并限制转发层数；禁止“信任所有代理”。

## 4. 本机 loopback 联调

1. 在发布后的 Host 目录检查 `appsettings.json`，按 3.1 启用 HTTP 与网络监听。数据库相对路径以 Host
   可执行文件目录为基准；首次启动会创建/升级 schema。
2. 从 Host 发布目录启动：

   ```powershell
   dotnet Dfo.Server.Host.dll
   ```

3. 在另一个 PowerShell 验证 bootstrap；成功结果应包含 `apiVersion = 1`、`status = ready`：

   ```powershell
   Invoke-RestMethod http://127.0.0.1:8080/api/launcher/v1/bootstrap
   ```

4. 把示例复制为 Launcher 发布目录中的 `Launcher.ini`，保持同一 loopback BaseUrl，然后启动
   `Dfo.Launcher.exe`。界面应先显示检查状态，再进入登录/注册表单。用注册页创建新账号后回到登录页；
   登录成功会取得公共 Channel 地址和一次性票据。只有在准备实际启动客户端时，才把整个 Launcher 发布
   内容部署到 `DNF.exe` 同目录并执行登录。
5. 完整游戏链路应依次观察：HTTP login 返回公共 Channel → Channel 握手/Channel Info 返回所选公共
   Game → Game LOGIN 兑换同一票据 → 成功 CMD 与六条账号通知 → 成功 CMD 中的第三类后续端点。

不要把 bootstrap 200 当成公网可达证明；至少还要从真实客户端所在网络分别验证三类地址的 TCP 路由。

## 5. 旧 MD5 账号的逐项迁移

`LauncherAuth:LegacyMd5Migration:AllowedAccountIds` 缺省为空。32 位十六进制形状本身不产生信任，也没有
通配符、范围或“全部旧账号”开关。

迁移单个账号时：

1. 停止 Host，并按 SQLite 一致性方式备份数据库（包括 WAL 状态）；不要在运行中的文件上做普通复制。
2. 用只读查询确认账号的内部 `account_id`，不要把 `password_hash` 打印到终端、工单或日志：

   ```sql
   SELECT account_id, m_id, length(password_hash) AS credential_length
   FROM accounts WHERE m_id = $login;
   ```

3. 在系统外核实该 AccountId 的历史 MD5 确实可作为一次迁移证明，然后按正整数逐项加入配置：

   ```json
   {
     "LauncherAuth": {
       "LegacyMd5Migration": {
         "AllowedAccountIds": [41]
       }
     }
   }
   ```

4. 重启 Host，让该账号用已核实的旧密码成功登录一次。服务端在同一账号行原子替换为现代记录，保留
   AccountId、角色和账号状态。
5. 立刻从 allowlist 删除该 AccountId 并再次重启。可用只返回布尔值的查询确认升级，不要导出记录：

   ```sql
   SELECT password_hash LIKE 'dfo-identity-v3:%' AS upgraded
   FROM accounts WHERE account_id = 41;
   ```

未列入、密码错误、空值、非 32-hex 或未知记录都必须继续得到统一的 `invalid_credentials`，不得签票或
改行。allowlist 含重复值、非正整数、通配符、范围或未知子键时，Host 会拒绝启动。

## 6. 日志与排错

日志可记录 trace id、稳定错误码、内部 AccountId、公共端点和不含秘密的生命周期结果。禁止记录：

- HTTP 请求/响应 body、密码、现代密码记录、旧 MD5；
- 原始票据、票据摘要、完整 A21 进程参数；
- 带 user-info 的 URL、SQL 参数、异常中可能携带的凭据。

常见现象：

| 现象/错误码 | 检查项 |
|---|---|
| bootstrap `503` | `LauncherAuth:Enabled`、`Networking:Enabled`、数据库可初始化，以及三类公共端点是否全部有效。 |
| `secure_transport_required` | Kestrel 实际收到非 loopback HTTP；改为直接/上游 HTTPS。同机 loopback HTTP 代理不会产生此码，其外部 TLS 必须由代理策略和不可绕过的本机 bind 边界保证。 |
| `redirect_not_allowed` | BaseUrl 指向会跳转的地址；改为最终 HTTPS/loopback origin。 |
| `request_timeout` / `network_error` | DNS、证书、监听地址、防火墙与 5 秒完整响应 deadline；修复后直接重试。 |
| `invalid_credentials` | 账号不存在与密码错误故意使用同一外部语义；旧账号另检查逐 AccountId 迁移流程。 |
| 启动器提示缺少 `DNF.exe` | Launcher 发布文件尚未部署到游玩客户端的 `DNF.exe` 同目录。 |
| Channel 能连、Game 不能连 | 对照 `Channel:Channels:n` 与 `Networking:GamePorts:n` 的索引、NAT 和防火墙。 |
| Game 登录后断开 | 检查系统时钟、60 秒票据窗口和服务端稳定原因码；重新 HTTP 登录取得新票据，不要重放。 |
| 成功 CMD 内地址不可达 | 单独核对 `Game:EndpointHost` 与 `GameLogin` 三个端口；它们不从 Channel/Game listener 推断。 |

排错时应保留稳定错误码、trace id、时间和所处 hop，但在共享日志前先确认没有上述秘密。
