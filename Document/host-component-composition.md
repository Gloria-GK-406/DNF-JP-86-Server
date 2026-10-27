# 单进程服务端组件组合

## 入口与依赖

`Dfo.Server.Host` 是唯一可执行程序和组合根，一次构建 Generic Host。公共 Application、Infrastructure、Persistence 由它注册一次，再接入 HTTP、GM 和游戏组件：

```text
Host
├── HttpApi（Kestrel、HTTP 中间件、启动器路由）
├── GmWeb（独立管理员、查询、官方邮件、账号点券）
├── GameServer（游戏装配、内容预热）
│   ├── Networking（Channel/Game TCP、可选 UDP）
│   └── Protocol（报文/命令处理）
└── Application + Infrastructure + Persistence（共同服务图）
```

HttpApi 只引用 Abstractions、Application、Data，不依赖 Networking/Protocol/Persistence。HTTP 功能按主题组织于该组件。

GameServer 装配已有网络/协议模块，不复制其源码，也不注册第二套公共服务。Host 不直接依赖 Networking/Protocol。共用登录会话、在线状态、CommandBus、事务入口及数据库，无 RPC 和第二个 IServiceProvider。登录会话在线持续有效、断线5分钟宽限，重登替换和进程重启失效，见 [登录会话](login-session-lifecycle.md)。

## 配置与开关

兼容现有 `LauncherAuth:*`、`Networking:*` 和公开地址配置键，HTTP 路径不变。内容根仍为可执行程序目录，工作目录不影响资源查找。

组合根在选择可选组件前加载统一配置：appsettings.json → appsettings.{Environment}.json → 环境变量 → 命令行 → experience.json（保留原来的独立平衡文件最后覆盖顺序）。环境选择为命令行 environment、DOTNET_ENVIRONMENT、ASPNETCORE_ENVIRONMENT、Production 的优先顺序。监听开关/地址在启动时决定，修改后需重启。

| HTTP | 游戏网络 | 结果 |
|---|---|---|
| 关 | 关 | 不开放 HTTP/TCP/UDP，仍可检查公共服务和静态内容 |
| 关 | 开 | 原 Channel/Game 与可选 UDP；没有隐藏 Kestrel 默认端口 |
| 开 | 关 | bootstrap/注册可用，登录返回 503 service_unavailable，不签发游戏票据 |
| 开 | 开 | HTTP 与游戏共享服务图，原完整登录流程 |

`Networking:UdpDiscoveryEnabled` 只在游戏网络启用时生效。HTTP 仅在启用时装配 WebHost，它由同一个 Generic Host 管理。HTTP 本身 ready 与游戏会话入口 ready 分开，后者经 Abstractions 中 `IGameServerReadiness` 提供。

上表的 HTTP 开关指 `LauncherAuth:Enabled`。GM 另由 `GmWeb:Enabled` 独立控制：GM 或 Launcher 任一启用才启动共同 Kestrel，各自使用独立监听端口，路由不跨端口提供。GM 单开时无需游戏网络，管理员查询/邮件/点券共用同一领域和持久化服务；配置、权限及操作说明见 [gm-web-admin.md](gm-web-admin.md)。

## 生命周期

1. 构建一次服务容器并验证配置。
2. GameServer 在 `IHostedLifecycleService.StartingAsync` 记录启动配置并预热已有 PVF 内容；此阶段完成后才进入任一 hosted service 的 StartAsync。
3. 启动 TCP/UDP、HTTP 监听。整个 Host 发出 ApplicationStarted 后，HTTP 标记 ready，游戏在网络启用条件下报告 ready。
4. ApplicationStopping 立即撤销 ready，再由宿主统一停止各监听器与释放服务。

PVF 缺失允许启动、具体内容请求明确失败；存在但损坏的 PVF 或预热失败会使启动失败，不能提前接受游戏或 HTTP 请求。异步预热传递取消令牌，现有同步表初始化保持原接口。

## 构建与发布

解决方案包含 GameServer、HttpApi 和 GmWeb。输出仍是一份 `Dfo.Server.Host`，标准候选打包先构建 GM 前端，再发布组件程序集、`wwwroot/gm`、appsettings、experience 和频道资源；Windows/Linux 自包含包流程不拆分。构建机需要 Node/npm，运行发布包无需 Node，流程见 [server-ci-release.md](server-ci-release.md)。

组件构建与自动化检查不赋予 ClientVerified；容器部署与真实客户端验收需各自提供证据。
