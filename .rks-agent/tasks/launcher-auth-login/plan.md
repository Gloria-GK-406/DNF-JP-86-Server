---
kind: rks-plan
spec:
  path: ./spec.md
  revision: 3
state: ready
---

# Implementation Plan: WPF 启动器与一次性票据登录链

## Approach

以当前 `Game 0x0001 LOGIN` 和 SQLite 账号行为的特征测试作为保护，先建立认证数据接缝和安全凭据/票据核心，再把 OnlineUser 身份发布纳入现有领域事务的“持久提交后”阶段。其上分别接入 HTTP adapter 与 Game 协议 handler，最后实现只依赖 v1 HTTP 合同的 WPF 启动器。服务端用一条真实 Host + Channel/Game socket 集成链验证 HTTP 签票、Channel Info 广告和 Game 兑换没有被测试捷径绕开；启动器的配置、状态机、参数和进程启动尽量下沉为无 UI 依赖的可测组件，WPF 只负责绑定与用户交互。

## Decisions Before Work

### BD-001: 项目与依赖边界

- 新增 `Server/src/Dfo.Server.LauncherApi` 作为 ASP.NET Core HTTP adapter；它可以引用 Data、Abstractions、Application，只映射请求/响应并派发应用命令，不引用 Persistence、Infrastructure、Networking 或 Protocol。
- 注册、密码登录/签票、Game 票据兑换命令及业务结果放在 `Dfo.Server.Application/Authentication`；账号/票据表和公共端点 source 契约放在 Abstractions，跨边界值放在 Data；SQLite 实现只放在 Persistence。
- `Dfo.Server.Protocol.Game.Login` 保留 A21 codec 和有序通知，改为派发应用层兑换命令并使用现有 `INetworkConnectionCloser` 处理拒绝/投递失败；不让 HTTP adapter 或协议 handler 直接访问表。
- 新增 `Launcher/Dfo.Launcher`（`net10.0-windows`、WPF）与 `Launcher/Dfo.Launcher.Tests`；两者纳入 `Server/86JP.Server.slnx` 的 Launcher solution folder，使既有一次 Release 构建覆盖整个交付物。

### BD-002: 密码记录与旧账号授权

- 现代记录使用 ASP.NET Core Identity `PasswordHasher<T>` 的 IdentityV3 版本化格式，生产配置固定 PBKDF2-HMAC-SHA512 220,000 次，并在数据库值外加本项目 scheme 前缀，便于拒绝未知格式和未来升级。使用框架随机盐、固定时间验证和 `SuccessRehashNeeded` 升级语义；测试通过注入较低 work factor 控制耗时，但生产默认不可被普通配置降级。
- 账号不存在时仍执行一次固定 dummy 现代记录验证，再返回与错误密码相同的外部结果，避免最直接的处理路径差异；外部合同仍只承诺相同状态码/消息形状。
- `LauncherAuth:LegacyMd5Migration:AllowedAccountIds` 解析为精确正整数集合，默认空且拒绝通配符、范围、重复和全局开关。只有集合中的现有 AccountId 才进入 MD5(UTF-8) 固定时验证与同事务升级；HTTP 不暴露该集合。

### BD-003: 票据和认证发布的事务边界

- 原始票据取 32 字节 CSPRNG 后作无填充 Base64Url；SQLite `login_tickets` 以 SHA-256 摘要为键，只保存 AccountId、签发/过期/消费 UTC 值。签票与可能发生的密码升级在同一 transactional command 中提交。
- 兑换用一条带摘要、精确 loginId、`consumed_at IS NULL` 和 `expires_at > now` 条件的原子 SQLite 更新/返回操作；并发只有一个事务得到 AccountId。
- 不拆成会释放 OnlineUser 独占门的两个命令。`OnlineStateRegistry` 为本次认证登记一个 `CommitCanFail=false` 的内存事务 participant：Prepare 只验证当前连接仍是匿名目标，不发布状态；领域事务先提交唯一可失败的 SQLite participant，再 Commit 身份 participant。这样数据库提交失败/取消不产生身份，持久提交后的身份发布异常不会回滚票据。
- Game handler 只在 command 返回（意味着提交与身份发布完成）后发成功链；从已提交到全部投递完成之间的异常一律关闭连接并清理该连接的 OnlineUser。清理失败也不得把票据恢复。

### BD-004: Host、传输和公共端点

- Host 切换为 Web SDK / `WebApplication` 组合根，同时保留现有模块和 content-root 行为；`AddServerModules` 泛化到 `IHostApplicationBuilder`。`Dfo.Server.LauncherApi` 提供 service registration、credential-path transport gate、统一错误映射和 endpoint mapping。
- `LauncherAuth` 配置显式声明 HTTP 开关/监听 URL、公共 Channel bootstrap IPv4/端口及逐 AccountId 迁移集合；当前 `Channel:Channels[*].Host/Port` 与 `Game:EndpointHost`、`GameLogin` 端口分别作严格广告值校验。启用 API 时不允许依赖代码内 loopback fallback。bootstrap readiness 同时检查认证存储、端点投影和 Networking 已启用。
- `/register`、`/login` 的明文远程拒绝在 JSON body binding 之前执行：仅 `Request.IsHttps` 或实际 remote IP 为 loopback 可进入凭据读取。反向代理 TLS 终止必须按运行文档配置受信任 forwarded headers；不默认信任客户端伪造头。
- API 不启用请求/响应 body 日志；异常只映射稳定错误码和 trace id。注册/登录的业务拒绝使用 typed result，而不是把包含账号或秘密的异常文本写入日志。

### BD-005: 启动器状态与本地数据

- `Launcher.ini` 使用仓库内小型严格解析器，不引入支持宽松合并的通用 INI 行为；解析成功只产生规范化 BaseUrl。记住账号单独写 `%LOCALAPPDATA%/DfoLauncher/preferences.json`，内容只有 loginId 和 remember 标志，且采用原子替换；任何密码/票据都只留在当前操作内存。
- 启动器状态机明确为 ConfigError/Checking/Ready/Busy/Error/Launched；bootstrap 成功且版本为 1 才开放提交，所有失败回到可重试状态。HTTP client 使用 typed DTO、短超时、CancellationToken 和安全错误降级，不记录 body。
- A21 参数由固定 35 槽 builder 生成，使用 `ProcessStartInfo.ArgumentList.Add` 传入唯一参数、`UseShellExecute=false`、工作目录为 launcher 所在目录；进程 seam 允许测试捕获参数而不启动真实游戏。

### CK-001: 广告链的受控测试路由

端到端测试给三类公共端点配置不同的非 loopback 文档地址，并维护只存在于测试侧的“广告 tuple → 本机临时 listener”路由。测试必须从 HTTP JSON 取 Channel tuple、按该 tuple 路由并完成真实 Channel socket 握手、实际解码 Channel Info，再按响应 tuple 路由到真实 Game socket；测试不得直接读取 Host 的 Game listener 配置作为下一跳。这验证配置投影和协议选择链，同时不把公网/NAT 可达性伪装成自动化证明。

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-004-RQ-010, RQ-013 | 在 Data/Abstractions 建立认证结果、账号凭据更新和票据表契约；在 Persistence 扩展 `accounts` 操作并加入 `login_tickets` schema/table。先保留/改写旧 Game 登录特征测试，明确“不再自动建号”的预期。 | Persistence 测试证明 schema 幂等升级、注册唯一性、条件更新、票据只存摘要、过期/账号不匹配/复用/并发消费边界；旧数据库可打开。 |
| T-002 | RQ-004-RQ-007, RQ-014 | 实现现代密码 service、输入规则、逐 AccountId 迁移 policy，以及 transactional Register / AuthenticateAndIssueTicket commands 的账号部分；删除生产登录对 `AccountProvisioning.GetOrCreateAsync` 的依赖，必要时仅保留兼容测试 helper。 | 表驱动 Application/Persistence 测试覆盖注册竞态、不同盐、现代验证/rehash、dummy 验证、allowlist 缺失/非法/无授权/错误 MD5/成功迁移，确认 AccountId 与角色关系不变且秘密不进入结果/日志。 |
| T-003 | RQ-005, RQ-008, RQ-009, RQ-014 | 完成签票、摘要持久化、60 秒 TimeProvider 边界和原子兑换领域服务/commands；签票与迁移共享一次事务。 | 固定时钟和并发测试逐值验证熵/字符集/过期点、错误密码不签票、数据库无原票据、同票据准确一次消费。 |
| T-004 | RQ-009, RQ-010, RQ-012, RQ-013 | 为 OnlineStateRegistry 加入准备后发布的认证 transaction participant，把 Game redeem command 注册为 Transactional + OnlineUser Exclusive；提供精确连接清理路径。 | UnitOfWork 故障注入覆盖提交失败、提交前取消、提交后连接消失/发布失败：提交前始终匿名且第二连接仍能兑换；提交后失败票据不可重放且原连接无可用身份。 |
| T-005 | RQ-003-RQ-005, RQ-013-RQ-016 | 新建 LauncherApi project，定义 v1 endpoints、transport gate、error mapping、endpoint/readiness source；转换 Host 为 WebApplication，接线配置验证并显式更新 appsettings 的三类公共端点。 | LauncherApi/Host 集成测试覆盖 bootstrap 200/503、register 201/400/409、login 200/400/401/503、内部 500 安全映射、远程 HTTP 在 body bind 前拒绝、非法端点/allowlist 启动失败及 Application 禁止引用规则。 |
| T-006 | RQ-009-RQ-014 | 把 `GameLoginRequest.PasswordHash`/旧 command 语义改为 Ticket，Game handler 兑换成功后沿用既有成功 CMD + 六条通知，所有拒绝及提交后投递失败走无敏感原因类别和连接关闭；不发明失败帧。 | Protocol/Networking 测试证明 decoder 兼容尾部、拒绝无成功帧/通知/自动建号且连接关闭；成功顺序、AccountId、roster/选角/进城回归保持。日志哨兵无账号、票据、body。 |
| T-007 | RQ-009, RQ-010, RQ-012, RQ-016 | 按 CK-001 建立真实 Host + HTTP + Channel/Game sockets 的闭环集成测试，并分别注入三类广告值。 | 测试从 HTTP login 开始，实际消费 Channel Info 后兑换票据，断言第三类端点和六条通知；修改任一广告配置会在对应 hop 被观测，不能靠已知端口绕过。 |
| T-008 | RQ-001-RQ-003, RQ-005, RQ-011, RQ-014 | 建立 Launcher 项目及可测核心：严格 INI、偏好、v1 HTTP client、状态 ViewModel、A21 参数 builder、时间检查和进程 seam。 | Launcher.Tests 覆盖全部配置拒绝、bootstrap 版本/网络恢复、注册/登录状态、重复提交门、只记账号、35 槽与特殊字符、过期/缺 exe/启动失败、日志和异常秘密哨兵。 |
| T-009 | RQ-001, RQ-011 | 实现功能优先 WPF MainWindow：服务状态、登录/注册切换、密码框、记住账号、忙碌遮罩、字段及全局错误、重试和启动反馈；密码只在 view code-behind 的当前提交边界传给 ViewModel。 | WPF 真实窗口启动烟雾通过；四态离屏布局/截图和当前桌面 DPI 实窗检查确认无遮挡、不可达按钮或重复提交，不切换系统显示缩放。 |
| T-010 | RQ-002, RQ-015, RQ-016 | 在 `Document/LauncherAuthRunbook.md` 写 Host/Launcher 发布与联调、三类公共端点、TLS/forwarded-header 边界、逐 AccountId 迁移流程、秘密日志约束和排错；Launcher 示例 INI 只含 BaseUrl。 | 从干净发布目录按文档进行 loopback Host + Launcher 烟雾；脚本/测试断言示例 INI 唯一业务键，文档列出的配置与实际 options 一致。 |
| T-011 | RQ-001-RQ-016 | 执行最终漂移检查、安全哨兵、整仓 Release 构建、每个测试项目的逐项测试、WPF 发布/启动和端到端烟雾；记录可追溯证据。 | 零 warning/error；所有既有/新增测试逐项通过；每个 AC 有新鲜证据，任何跳过或环境限制显式记录并在最终审查前处理。 |

## Choices During Implementation

- `PasswordHasher<T>` 的测试 wrapper、result 类型的局部命名和 SQLite SQL 形状可在红灯测试下收敛，但不得改变 BD-002 的生产算法/成本与版本化边界。
- LauncherApi endpoint 采用 route group、endpoint filter 或 middleware 的具体组合由实现决定；远程明文 gate 必须先于 body 读取，错误 JSON 保持规格逐值稳定。
- 真实 socket 测试复用现有 `ChannelHostStartupTests` fixture 还是提取新的共享 Host harness，可按重复量决定；CK-001 的数据来源限制不可放松。
- WPF 的颜色、字体和微布局由实现时视觉检查调整，不能因此改变状态机、可重试性或敏感数据生命周期。
