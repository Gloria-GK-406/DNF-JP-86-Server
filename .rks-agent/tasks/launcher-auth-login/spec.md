---
kind: rks-spec
revision: 3
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: WPF 启动器与一次性票据登录链

> Revision 3 用户验收取舍（2026-09-10）：启动器是功能优先的临时登录页，用户明确要求不再为精确双 DPI
> 实窗矩阵投入额外工作。视觉验收保留四态离屏布局/截图和当前桌面 DPI 的真实启动烟雾，不要求切换系统
> 显示缩放，也不要求四态分别在 100%/150% 的真实 HWND 中重复取证；功能、可达性和无阻塞布局边界不变。

## Why and Outcome

玩家应只在独立 WPF 启动器中输入账号密码。启动器通过一个 HTTP BaseUrl 完成服务可用性检查、注册和登录，取得服务端下发的公共频道入口与短时一次性票据，再按 A21 已有启动参数形状启动 `DNF.exe`。Game `CMD 0x0001 LOGIN` 只兑换票据并绑定既有账号，不再接收密码或自动建号；认证成功后，现有登录响应、账号状态、角色列表、选角和进城链路保持不变。

## Scope

包含：

- 同仓库内可运行的 .NET 10 WPF 启动器、登录/注册界面、加载和错误状态，以及对应自动化测试。
- `Launcher.ini` 的单 BaseUrl 配置、HTTP bootstrap/register/login 接口和 Host 内接线。
- 账号注册、现代密码验证信息、旧账号迁移策略、短时一次性票据及 Game LOGIN 兑换。
- 公共 Channel bootstrap、Channel Info 中的公共 Game 端点及 Game 登录响应后续端点的配置投影，A21 参数构造、`DNF.exe` 启动、运行说明和示例配置。

不包含：

- 客户端二进制或 ClientHack 修改、游戏内置登录 UI、自动更新、补丁分发、多区选择。
- 邮箱验证、找回密码、第三方登录、管理后台、生产域名/TLS 证书签发或公网部署。
- 组队功能，以及成功登录后的既有角色/世界业务重构。

## Definitions and Invariants

- **公共 Channel bootstrap 端点**：HTTP 登录响应及 A21 启动参数第 1、2 槽使用的 Channel 入口 IPv4 地址与 TCP 端口。
- **公共 Game 端点**：Channel Info 为每个频道广告、供 A21 随后连接并兑换票据的 Game IPv4 地址与 TCP 端口。
- **Game 登录后续端点**：现有 A21 `CMD 0x0001` 成功响应中的 `EndpointHost`、`ConnectionPort`、`EndpointPortA`、`EndpointPortB` 所表达的部署端点。
- 上述三类端点都是部署广告值，不等同于进程 bind 地址或监听网卡；它们必须分别配置、分别校验，可以使用不同主机，但必须指向同一逻辑部署中能连续完成 Channel 发现、票据兑换和后续连接的服务。
- **原始票据**：仅在一次成功 HTTP 登录响应中返回并进入一次 A21 启动参数的随机不透明值。
- **票据摘要**：服务端用于查找票据的单向摘要；数据库不得持久化原始票据。
- **现代密码记录**：可识别版本、带每账号随机盐、采用受支持自适应密码 KDF 的验证记录；它与旧 `password_hash` 中可能存在的 32 位十六进制摘要不是同一语义。
- **旧 MD5 迁移 allowlist**：服务端配置中按 `AccountId` 逐项列出的、已由运维在系统外确认可使用其历史 MD5 作一次迁移证明的账号集合。缺省为空，不允许通配符、范围或“全部旧账号”开关；32-hex 形状本身不产生信任。
- **一次性**：同一张有效票据发生并发兑换时，至多一个请求能成功；只有满足账号绑定、未过期、未消费的兑换才可认证连接。
- 票据数据库消费提交成功是认证发布边界：提交前 OnlineUser 必须保持匿名且不得发送成功消息；提交后票据即不可重放，之后的身份发布或连接投递失败不能把票据恢复为可用。
- 服务端时间为票据有效性的唯一真源；当 `now >= expiresAtUtc` 时票据已过期。
- 账号名按原样、区分大小写匹配；不得静默 trim 或大小写折叠。密码不得 trim、规范化或回显。

## Public Interfaces

### Launcher.ini

配置文件位于启动器可执行文件旁，唯一允许的业务设置为：

```ini
[Launcher]
BaseUrl=https://server.example/
```

空行和注释不算设置。缺文件、缺值、重复键、未知节/键、相对 URI、非 HTTP(S) URI、含 user-info/query/fragment 的 URI均为配置错误。尾部 `/` 可被规范化。明文 `http` 只允许 loopback 主机（`localhost`、`127.0.0.1`、`::1`）；非 loopback 必须使用 `https`。

记住的账号名属于用户本地非敏感偏好，不写入 `Launcher.ini`；密码和票据不进入任何持久化启动器设置。

### HTTP API v1

所有响应使用 JSON。错误响应统一为：

```json
{
  "code": "stable_machine_code",
  "message": "可直接展示给用户且不泄露内部信息的中文消息",
  "fieldErrors": { "field": "reason" }
}
```

`fieldErrors` 仅在字段校验失败时出现。

#### GET /api/launcher/v1/bootstrap

成功返回 `200`：

```json
{
  "apiVersion": "1",
  "status": "ready",
  "registrationEnabled": true
}
```

只有 HTTP 层及认证依赖已可服务时才返回 `ready`；暂不可用返回 `503 service_unavailable`。

#### POST /api/launcher/v1/register

请求：

```json
{ "loginId": "player", "password": "plaintext-only-in-request" }
```

新账号名必须为 1 至 256 个 ASCII 字符，且不得包含 `?`、控制字符或首尾空白；新密码必须为 8 至 128 个 Unicode 标量值且不得包含 NUL。成功原子创建账号及现代密码记录，返回 `201` 与 `{ "loginId": "player" }`。重复账号返回 `409 account_exists`；非法字段返回 `400 invalid_request`。注册不得签发 Game 票据。

#### POST /api/launcher/v1/login

请求字段同 register；为兼容旧账号迁移，登录密码只要求非空、至多 128 个 Unicode 标量值。账号不存在和密码错误统一返回 `401 invalid_credentials`，响应内容不得暴露是哪一种情况。成功返回 `200`：

```json
{
  "loginId": "player",
  "ticket": "opaque-url-and-argument-safe-token",
  "expiresAtUtc": "2026-09-10T12:34:56Z",
  "channel": { "host": "203.0.113.10", "port": 7001 }
}
```

`channel` 必须来自经启动校验的公共 Channel bootstrap 端点配置，不能从 bind 地址、请求 Host 头或客户端输入推断。

凭据接口若经非 loopback 明文 HTTP 到达，返回 `400 secure_transport_required`，不得读取或验证密码。

### 服务端旧账号迁移配置

旧行默认不可信。唯一允许的迁移授权是服务端配置 `LauncherAuth:LegacyMd5Migration:AllowedAccountIds` 中逐个列出的正整数 AccountId；配置缺失等同空集合。配置不得提供全局启用、通配符、范围、按账号名匹配或“全部 32-hex”模式，非法项必须启动失败而不是放宽。运维只能在系统外核实具体账号归属后加入该 AccountId；HTTP API 不提供查询、添加或绕过 allowlist 的能力。完成迁移后，该账号按现代记录认证，不再依赖 allowlist。

### 服务端公共端点配置

启用启动器 HTTP 服务时，Host 必须把以下三类配置视为不同语义并在接受认证请求前完成格式、端口范围、必填项和协议可编码性校验：

1. HTTP login 响应下发的公共 Channel bootstrap IPv4/端口；
2. Channel Info 中每个可选频道广告的公共 Game IPv4/端口（当前配置投影为 `Channel:Channels[*].Host/Port`，不得因缺值静默回落到只对服务端本机有效的地址）；
3. Game `CMD 0x0001` 成功响应使用的后续公共端点（当前为 `Game:EndpointHost` 及 `GameLogin:ConnectionPort/EndpointPortA/EndpointPortB`）。

一类配置不得从另一类的 host/port、listener bind 值或请求内容推断；各 host 无需相同。启动校验只证明配置完整且可编码，不声称证明公网、NAT、防火墙或 DNS 的实际可达性。

### A21 启动参数

启动器把下列内容作为**一个参数**传给与启动器部署在同一目录的 `DNF.exe`，并把该目录作为工作目录：

```text
9?{channel.host}?{channel.port}?{loginId}?{ticket}?1?0?6?0?2?1???0?0?0?9n2b1c8r3w7y?0?0?0?0?0?2?0?0?0?0?0?0?0?0?0?0?0?4D87
```

该字符串保持现有 35 个 `?` 分隔槽：仅槽 1 为公共频道 IPv4、槽 2 为十进制端口、槽 3 为账号名、槽 4 为原始票据；槽 0 和槽 5—34 逐值保持现有 A21 模板。参数必须绕过 shell 拼接。不存在/不可启动的 `DNF.exe`、票据在启动前已过期或进程创建失败，都显示明确错误且不得把密码或完整参数写入日志。

Game `CMD 0x0001 LOGIN` 的第一个 ASCII dstring 仍是账号名，第二个 ASCII dstring 改为原始票据；A21 其余尾部内容继续允许存在并保持原解码兼容。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 启动器必须提供登录、注册、服务检查、进行中和失败状态；网络/配置/校验/认证/启动失败均可恢复重试，进行中操作不可重复提交。可选择记住账号名，但密码在导航、失败后的重试准备和成功启动后均不得被持久化。 |
| RQ-002 | 启动器只能从 `Launcher.ini` 获取 BaseUrl，并按上述单设置契约严格校验；频道/Game 地址、端口、账号密码和可执行路径不得成为 INI 设置。 |
| RQ-003 | Host 必须提供上述版本化 bootstrap 接口；启动器在允许注册或登录前完成 bootstrap，并对版本不兼容或服务不可用给出明确状态。 |
| RQ-004 | register 必须只创建不存在的账号，原子写入现代密码记录；重复、竞态重复和非法输入产生稳定、可展示且不泄露内部细节的错误。 |
| RQ-005 | login 必须验证既有账号的凭据，并只在验证成功后签发含过期时间和公共 Channel bootstrap 端点的票据；账号不存在与密码错误使用同一外部语义。 |
| RQ-006 | 新建及升级后的密码记录必须使用版本化、每账号随机盐、自适应 KDF；相同密码在不同账号的持久化值不得相同。明文密码、现代密码记录、旧摘要及其可重放等价物不得进入 INI、启动参数、响应、常规日志或异常消息。 |
| RQ-007 | 历史 32-hex 值默认不可信。旧账号仅在其 AccountId 被服务端逐项列入默认空的迁移 allowlist、旧值严格符合 32 位十六进制 MD5 形状、且 `MD5(UTF-8(输入密码))` 固定时比较成功时，才允许本次登录并在同一账号行原子替换为现代记录。无授权时即使摘要匹配也返回 `401 invalid_credentials`、不签票且行不变；禁止全局迁移开关。成功迁移保持原 AccountId 及全部角色/账号状态，随后只按现代记录认证。 |
| RQ-008 | 每次成功 HTTP 登录必须生成至少 128 bit CSPRNG 熵、仅含 URL/命令参数安全 ASCII 且不含 `?`/空白/引号的原始票据；默认寿命为 60 秒。只持久化票据摘要、AccountId、签发/过期/消费状态，原始票据只返回一次。 |
| RQ-009 | Game LOGIN 必须以账号名和票据原子兑换既有 AccountId：账号不匹配、过期、已消费、篡改或不存在的票据均不得认证、自动建号、发送成功 CMD 或发送账号状态通知；并发兑换至多一个成功。票据消费事务成功提交前不得发布 OnlineUser 身份或发送任何成功消息；提交失败或提交前取消时连接保持匿名且无成功消息。 |
| RQ-010 | 由于当前没有经 A21 验证的 LOGIN 失败 body，票据拒绝不得发明成功形状的失败帧；服务端应终止该 Game 连接，并以不含账号名、票据或请求载荷的原因类别记录拒绝。消费提交成功后若连接消失、身份发布失败或成功消息无法投递，该连接必须关闭/拒绝且不得留下可继续使用的认证身份，但票据保持已消费，不能由另一连接重放。 |
| RQ-011 | 启动器必须按上述 35 槽模板启动 A21；HTTP 返回的公共频道端点、账号名和原始票据是唯一动态身份/路由值，密码、旧 MD5、BaseUrl 和固定本地服务器地址不得出现在参数中。 |
| RQ-012 | 只有票据消费成功提交后，才可把仍存活的当前匿名 OnlineUser 发布为票据所属 AccountId，并沿用当前链路发送现有 A21 `CMD 0x0001` 成功响应，然后按现有顺序发送 game options、hotkeys、两条 auction service、honour standing、inform notice；后续 roster、选角、进城读取同一 AccountId。 |
| RQ-013 | HTTP 适配层与 Game handler 只能驱动应用命令/领域服务；持久化经 Abstractions 接缝进入 Persistence。Application 不得引用 Infrastructure、Persistence、Networking 或 Protocol。账号创建、凭据升级和票据消费必须具有事务边界，Game 兑换继续受 OnlineUser 独占执行门保护。 |
| RQ-014 | 服务端与启动器日志不得记录请求/响应 body、密码、密码记录、旧摘要、原始/摘要票据、完整进程参数或带 user-info 的 URL。允许记录 trace id、稳定错误码、内部 AccountId、公共端点和不含敏感值的生命周期结果。面向用户的错误不得包含堆栈、SQL、文件系统内部或账号存在性旁路。 |
| RQ-015 | 同仓库运行说明必须分别说明三类公共端点配置及其仅做配置校验的边界、Host HTTP 启用、loopback HTTP/远程 HTTPS、逐 AccountId 旧账号 allowlist 的核实/加入/移除流程与默认拒绝、启动器部署位置及排错；示例 `Launcher.ini` 只能含 BaseUrl。 |
| RQ-016 | 从 HTTP 返回的 Channel bootstrap 入口开始，客户端必须能完成现有 Channel 握手、消费 Channel Info、选择其广告的公共 Game 端点并在该 Game 后端兑换同一票据；Game 登录成功响应再投影独立配置的后续公共端点。三类 host 不要求相同，但不得因未配置而静默使用客户端自身 loopback。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-002 | 给定合法单 BaseUrl INI，WPF 启动后先检查服务并可切换登录/注册；缺失、未知、多余或不安全 BaseUrl 时不发送请求且显示可修复错误。自动化验证状态转换、重复提交门和“只记账号不记密码”。 |
| AC-002 | RQ-003 | Host 集成测试证明 bootstrap 在依赖就绪时返回指定 v1 结构，在不可用时返回 503；启动器对不支持版本和网络失败保持可重试且不进入可提交认证状态。 |
| AC-003 | RQ-004, RQ-006 | API 集成测试证明合法注册返回 201 并可从新进程/新仓储实例读取同一账号；重复和并发重复只有一个创建成功；数据库值不是明文、带现代版本标识，且两个同密码账号的存储值不同。 |
| AC-004 | RQ-004, RQ-005 | 表驱动测试覆盖账号名、注册密码和登录密码的边界；非法输入返回 400 稳定字段错误。不存在账号与错误密码均逐值返回同一 401 代码/消息形状。 |
| AC-005 | RQ-005, RQ-008 | 正确现代密码返回 200、登录账号名、非空安全字符票据、可解析 UTC 过期时间与已配置公共 Channel bootstrap 端点；错误密码不创建票据。固定时钟证明默认过期时间恰为签发后 60 秒。 |
| AC-006 | RQ-006, RQ-014 | 自动化捕获服务端与启动器日志、异常和进程启动失败文本，使用哨兵密码/旧摘要/票据/摘要/完整参数检查均无命中；HTTP 成功与错误响应也不回显密码记录。 |
| AC-007 | RQ-007 | 预置历史 MD5 行后覆盖四组：未 allowlist 且摘要匹配仍逐值返回 401、无票据且行不变；已 allowlist 但密码错误不改变行；已 allowlist 且正确密码只成功一次迁移并保留 AccountId/角色关联，随后无需 allowlist 即按现代记录登录；空值、非 32-hex 和未知格式即使 allowlist 也不得迁移。配置缺失时集合为空，通配符/范围/全局开关不可表示或启动失败。 |
| AC-008 | RQ-008, RQ-009 | 持久化测试证明数据库没有原始票据；首次正确兑换成功，`now == expiresAtUtc`、篡改、复用、错误账号名均失败。对同一票据并发兑换时准确一个消费事务提交成功，其余失败，且只有提交成功后的连接可进入认证发布。 |
| AC-009 | RQ-009, RQ-010 | 真实 Game socket 故障测试分别令票据消费提交失败及提交前取消：两者均无 `CMD 0x0001` 成功帧或六条账号通知，OnlineUser 始终匿名并关闭/拒绝；事务未提交、票据未消费，故随后第二连接仍可正常兑换一次。普通无效票据同样不认证、不自动建号并关闭连接。 |
| AC-010 | RQ-011 | 参数构造测试逐槽断言 35 个值：槽 1—4 来自登录结果，所有其他槽与模板一致；参数不含密码、旧摘要、BaseUrl 或非返回端点。特殊字符输入不能改变槽数或触发 shell 解释。 |
| AC-011 | RQ-012, RQ-016 | 端到端集成测试从 HTTP login 响应取得非 loopback 公共 Channel bootstrap 配置，经受控测试网络连接该入口并完成现有 Channel 握手，实际解码/消费 Channel Info 后再连接其中广告的非 loopback 公共 Game 端点兑换同一票据；登录响应逐值投影第三类后续公共端点。三类 host 使用不同测试值仍成功，且测试不绕过 Channel Info 直接使用已知 Game 端口。随后逐帧收到现有 A21 成功 CMD 与六条有序账号通知，`GET_USERINFO`/返回选人读取同一 AccountId，既有选角/入场测试保持通过。该测试证明配置投影与协议链闭合，不宣称公网实际可达。 |
| AC-012 | RQ-013 | 项目引用/架构测试继续证明 Application 无禁用引用，HTTP adapter 与 Game handler 不直接持有持久化实现；命令注册证明注册、迁移/登录和兑换写路径受所需事务及执行门保护。 |
| AC-013 | RQ-014 | API 测试逐项断言 400/401/409/503 的稳定机器码和安全消息，凭据远程明文请求在验证前返回 `secure_transport_required`；内部异常对外统一为无堆栈的 `internal_error`。 |
| AC-014 | RQ-001, RQ-011 | 启动器进程烟雾测试证明合法结果会以单参数和正确工作目录启动同目录 `DNF.exe`；缺 exe、过期票据和进程失败不启动游戏并显示可重试错误。 |
| AC-015 | RQ-001 | 自动化离屏布局与截图覆盖登录、注册、加载和错误四态，并结合当前桌面 DPI 下的真实启动窗口烟雾，确认无控件遮挡、不可达操作或阻塞布局；无需切换系统显示缩放或逐档重复真实窗口取证，最终品牌美术不作为通过条件。 |
| AC-016 | RQ-015, RQ-016 | 运行说明和示例配置可从干净发布目录完成 Host + Launcher 本地 loopback 联调，示例 INI 检查仅有 `[Launcher] BaseUrl`；文档逐项列出三类服务端公共端点，明确它们可不同、不得从 bind/彼此推断且校验不证明公网可达，并说明远程必须 HTTPS、旧行默认不可信、逐 AccountId allowlist 无全局开关。 |
| AC-017 | RQ-001-RQ-016 | `Release` 构建零警告零错误，既有全部测试与新增后端、协议、启动器测试逐项通过；失败输出保留具体用例身份，不能只用汇总退出码代替。 |
| AC-018 | RQ-009, RQ-010, RQ-012 | 故障测试在票据消费已提交后分别令连接消失和身份发布/成功投递失败：原连接不得留下可继续使用的认证身份且无可用成功链，票据仍为已消费；随后第二连接兑换同一票据必须被拒绝且不能收到成功消息。 |

## Success Evidence

| ID | Covers | Evidence |
|---|---|---|
| EV-001 | AC-001-AC-014, AC-018 | 新增配置、ViewModel、HTTP、领域、持久化、协议与真实 Host/socket 自动化测试的逐用例结果。 |
| EV-002 | AC-011, AC-017 | 既有 Game 登录、账号状态、roster、选角及入场测试与新增 HTTP→Channel Info→Game 票据端到端测试结果。 |
| EV-003 | AC-015 | WPF 四态离屏布局/截图及当前桌面 DPI 的真实窗口启动截图或人工检查记录。 |
| EV-004 | AC-016 | 从发布目录按文档执行的本地 loopback 烟雾记录，以及仅含 BaseUrl 的示例 INI。 |
| EV-005 | AC-017 | 整仓 Release 构建及测试日志，含零警告/错误和每项测试身份。 |
