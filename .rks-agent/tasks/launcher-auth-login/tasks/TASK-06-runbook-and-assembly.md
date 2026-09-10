# TASK-06 — 运行手册、示例配置与整体验收装配

## Task Unit Contract

- Revision: 1
- Outcome and references: 在 `Document` 完成 Host/Launcher 发布联调、三类 endpoint、TLS/forwarded headers、逐 AccountId 迁移、日志安全与排错说明，提供只含 BaseUrl 的 Launcher 示例 INI，并完成交付物装配自检；覆盖 RQ-002、RQ-014—RQ-016，AC-006、AC-016—AC-017；Plan T-010—T-011 的文档/装配部分。
- Owned boundary: 可修改 `Document/LauncherAuthRunbook.md`、Launcher 示例配置、发布相关非二进制项目文件与必要 README 引用；可修正前序任务暴露的文档/配置一致性小缺陷并记录。不得改客户端二进制、部署到只读基准目录、提交 Git commit或宣称最终 SDD 完成。
- Required checks: 自动断言示例 INI 只有 `[Launcher] BaseUrl`；核对文档所有配置键与当前 options/appsettings；执行 clean publish 到任务工作树内临时输出并做 Host+Launcher loopback smoke；运行整仓 Release build 与每测试项目逐项 test，记录任何环境限制。最终独立漂移、change review 和 fresh verification 由 controller 在本 Task quick review 通过后另行执行。
- Dependencies: TASK-04 的服务端闭环和 TASK-05 的 WPF 启动器均已接受。

## Execution Round 2 — 2026-09-10

### Changed

- 修复 QR1-F1 的文档过度声明。运行手册现在按实际 gate 条件区分：非本机代理以 HTTP 连接 Host 时在
  body 前拒绝；同机代理经 127.0.0.1 HTTP 会命中 loopback 例外而允许，Host 无法证明外部客户端原本
  使用 TLS。
- 对同机代理明确要求 Host 只 bind loopback、只允许该代理访问、代理强制外部 HTTPS 且不存在绕过入口；
  同时保留“不消费/不信任 forwarded headers”的当前事实和未来仅可信代理名单的变更边界。排错表同步
  删除“一切 HTTP 上游都会报 secure_transport_required”的错误推论。

三个 owned 文件的当前 manifest SHA256 为
`C9A8B5D00F4B084EE7770501B03A661E57B08D813D782AF20D86082AD4740789`。

### Checks

- 自动断言两类 HTTP 上游、Host 无法证明原始 TLS、同机四项安全条件和 forwarded headers 默认边界均
  在文档中存在，旧统一拒绝措辞不存在；`task06-round2-proxy-boundary.log`。
- 这是纯文档修正；Round 1 的 clean publish、发布 Launcher→Host loopback smoke、唯一 BaseUrl、完整
  Release build 与九项目逐项 TRX 不受影响。`git diff --check` 仍为 exit 0。

### Remaining

TASK-06 Execution Round 2 已完成 QR1-F1 的最小修正，等待新的独立 quick review；未修改产品行为、
registry、既有 review 或 Git 历史。

## Execution Round 1 — 2026-09-10

### Changed

- 新增 `Document/LauncherAuthRunbook.md`，从发布、配置、loopback 联调到排错完整说明三类独立公共端点、
  bind/advertise/NAT 边界、远程 HTTPS、默认不信任 forwarded headers、逐 AccountId 旧 MD5 迁移和
  日志秘密约束。文档明确当前 Host 未启用 forwarded headers；代理要么到 Kestrel 保持 HTTPS，要么另做
  受信代理名单与中间件的安全变更，不能把任意头当成 TLS 证明。
- `Launcher.ini.example` 保持唯一 `[Launcher] BaseUrl`。Launcher 项目现在把该示例复制到普通输出与
  publish 目录，使文档中的干净发布步骤能够直接取得模板；未加入任何 endpoint、账号、密码或路径设置。
- 未修改服务端认证、Game 协议、客户端二进制或只读取证目录，也未向实际客户端部署。

三个 owned 文件的 manifest SHA256 为
`C0B605C04D481028472A271871CD00F7768E5E3D2DD63CF84B5CB4A22F766A34`。

### Checks

- 自动 runbook/配置核对通过：示例仅 1 section/1 key；文档 13 个部署键与当前 options/source 的 11 个
 直接读取键逐项匹配，三类端点、可达性、TLS/forwarded headers、allowlist 均有显式边界；
  `task06-runbook-contract.log`。
- Host 与 `win-x64` Launcher 均从任务本地临时目录干净 publish 成功，发布后的
  `Launcher.ini.example` 与源码 SHA256 相同。首个带 `--no-restore` 的 RID 探针按预期暴露
  NETSDK1047；确认原因是尚无 win-x64 restore asset 后，文档所列允许 restore 的命令成功；
  `task06-publish.log`。
- 发布 Host 使用隔离端口启动 HTTP、Channel 和两个 Game listener；手工探针及正常启动的发布 Launcher
  各产生一次 `GET /api/launcher/v1/bootstrap` 200，结果为 v1/ready。Launcher 初始化 1.5 秒后仍存活，
  未启动 DNF；随后精确关闭 smoke 进程，四个端口和 Launcher 进程均归零；
  `task06-loopback-smoke.log`。
- Release solution build 为 0 warning / 0 error。九个测试项目逐项目执行，带真实 A21 PVF 的运行共有
  2960 Passed / 0 Failed / 2 conditional Skipped；两条缺档案反向测试在另一个无 PVF TRX 中均 Passed，
  所以每个测试身份都在所需环境中取得通过。完整结果与环境诊断见 `task06-test-summary.md` 和对应 TRX。
- `git diff --check` 无输出、exit 0；最终无 Dfo.Launcher、DNF 或 smoke 端口残留。

### Recovered Exception

第一次整仓逐项目测试沿用没有 `PVF_ARCHIVE_PATH` 的当前 shell：依赖档案的用例被跳过，Protocol 产生
42 个直接 `Script.pvf was not found` 和 1 个下游 socket EOF。确认失败控制流均从缺档案开始后，使用
工作区内 72,894,573 字节 A21 `Script.pvf` 重跑九项目并全部达到预期；同时保留无档案运行，让两条专门
验证缺档案错误的用例也实际执行通过。没有为取得绿灯修改产品行为或删除失败证据。

### Remaining

TASK-06 Execution Round 1 的实现和直接验证已完成，等待 controller 安排独立 quick review。本轮没有
修改 registry、review 或创建 Git commit；最终漂移、完整 change review 和逐 AC fresh verification 仍由
controller 在 Task quick review 通过后执行。

## Independent Review Round 1

- Reviewed execution: `Execution Round 1`；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2、Plan T-010—T-011；局部覆盖 RQ-002 / RQ-014—RQ-016、AC-006 / AC-016 / AC-017。依据 `reviewing-task-units-quick` 与 `verification-before-completion` 独立核对文档、发布和装配证据，不作最终 Spec 验收。
- Reviewed identity: 独立重算三个 owned 文件 `Document/LauncherAuthRunbook.md`、`Launcher/Dfo.Launcher/Dfo.Launcher.csproj`、`Launcher/Dfo.Launcher/Launcher.ini.example` 的 manifest SHA256 为 `C0B605C04D481028472A271871CD00F7768E5E3D2DD63CF84B5CB4A22F766A34`，与执行记录一致。任务本地遗留 publish 输出不属于产品 manifest。本轮只追加此评审；未修改产品、registry、其他 review 或创建提交。

### QR1-F1 — [P2] TLS 终止代理的 HTTP 上游被文档一概写成拒绝，遗漏 loopback 例外

`Document/LauncherAuthRunbook.md:107—111` 断言未启用 forwarded headers 就意味着代理通过 HTTP 转发时，Host 总会在读凭据前返回 `secure_transport_required`；`:190` 的排错行重复这一不带条件的结论。实际 `CredentialTransportGateMiddleware`（`Server/src/Dfo.Server.LauncherApi/LauncherApiApplicationExtensions.cs:168—172`）按当前连接的 `Request.IsHttps` 和实际 `RemoteIpAddress` 判断：非 HTTPS 且非 loopback 才拒绝。因此同机代理通过 `127.0.0.1`/`::1` 明文连接 Host 时，loopback 例外会允许它进入认证；未启用 forwarded headers 意味着 Host 无法识别代理之前的客户端地址/传输状态，而不是一定视其为远程。

独立干净发布 Host 的真实 HTTP 复现：实际连接为 `127.0.0.1:18089`，POST login 携带 `X-Forwarded-For: 198.51.100.20` 和 `X-Forwarded-Proto: http`，使用仅本次诊断的不存在账号与虚构密码。结果为 `401 invalid_credentials`，不是 `400 secure_transport_required`；这证明请求已通过 transport gate 到达认证路径，且转发头没有改变 gate 判断。没有真实外部客户端或代理部署，复现只模拟同机代理到 Host 的上游 hop；它足以反驳文档的无条件拒绝声明。

影响：部署者可能错误依赖 Host 的所谓“必然拒绝”保障前端明文连接；当同机代理转发了外部 HTTP 时，Host 会因为实际 peer 是 loopback 而无法代替代理阻断。该问题是运行手册对当前安全边界的误述，影响 AC-016，而不是本轮要求变更产品行为。

最小修正：明确区分非 loopback HTTP upstream（拒绝）与 loopback HTTP upstream（按开发例外允许，Host 无法判断原始客户端 TLS）；保留远程必须 HTTPS、推荐直接 Kestrel TLS/上游 HTTPS 的部署约束，并明确由代理强制前端 HTTPS，不能把“忽略 forwarded headers”当作端到端传输证明。如以后需要基于原始客户端信息执行 gate，再单独实施受信代理配置和中间件安全变更。同步修正排错表，不必修改本轮产品代码。

### Independent Evidence and Checks

- 严格检查源示例：忽略空行/注释后仅 `[Launcher]` 与 `BaseUrl=http://127.0.0.1:8080/` 两行。项目文件同时设置 `CopyToOutputDirectory` 和 `CopyToPublishDirectory=PreserveNewest`；按文档允许 restore 的命令独立 clean publish Host 与 win-x64 Launcher 均 exit 0，发布示例与源码 SHA256 一致。
- 独立发布与诊断目录为 `C:\Users\Gloria\AppData\Local\Temp\task06-independent-review-4487427b4ef14814ab743c99cc85c833`。`smoke.ps1` 在隔离的 HTTP 18089 / Channel 17009 / Game 11019、11029 上启动发布 Host；bootstrap 得到 v1/ready；发布 Launcher 正常初始化，Host 日志明确记录第二次 bootstrap 200。Launcher 1.8 秒后仍存活。随后精确关闭两个 smoke 进程，四个 listener 和 Launcher 进程均归零，DNF 进程数为 0；未部署或启动真实游戏。
- 配置逐项对照 `LauncherApiOptions`、`PublicEndpointConfiguration`、`NetworkingOptions`、`ChannelSourceOptions`、Host appsettings 及 `AuthenticationRuleService`：HTTP 开关/bind、三类公共端点、Channel/Game listener 映射和逐 AccountId allowlist 都有实际读取对应，无新增虚构键。三类 host 可不同、广告端口可因 NAT 不同、仅验证完整/可编码而不证明可达、后续端点不自动创建 listener 的描述准确。上述 TLS 代理结论是本轮唯一发现的实质文档偏差。
- 旧 MD5 操作与源码一致：默认空集合、显式正整数 AccountId、拒绝重复/未知子键/非数值，现代前缀实际为 `dfo-identity-v3:`，示例只查询账号身份、长度或升级布尔值，没有要求打印旧摘要或密码记录。日志禁区与 RQ-014 一致；独立 smoke 日志不含诊断密码/请求 body。完整 AC-006 安全哨兵的最终汇总仍由 controller 负责，不把本次仅含虚构凭据的 smoke 扩大为完整秘密覆盖证明。
- 独立逐条解析九份 `task06-tests-pvf/*.trx`：Launcher 60、Pvf 60、Application 1341、Host 17、Infrastructure 301、LauncherApi 50、Networking 62、Persistence 355、Protocol 714 为 Passed，共 2960；只有两条 Infrastructure `NotExecuted`，没有 Failed 或其他结果。两个 skip 的 testId 分别为 `c383e5d8-0690-0a66-6c4b-1e74f61a682f` 和 `dc672fea-14a9-da4a-1e4f-87185cc98c48`，均与无 PVF Infrastructure TRX 中的 Passed 项精确相同，源码也明确只在存在档案时跳过缺档案反向测试。未把初次缺 PVF 失败运行算作绿灯。
- 使用实际 A21 PVF 独立重跑 Host 全部 17/17 Passed（含 HTTP→Channel Info→Game 闭环）及 Launcher 全部 60/60 Passed；逐条 TRX 无其他 outcome，分别为本次临时目录内 `reviewer-host.trx`（17:45:07 +08:00 开始）与 `reviewer-launcher.trx`（17:45:35 开始）。视觉输出隔离到 `visual/`，未覆盖前序证据。
- 独立完整 Release solution build 成功，0 warning / 0 error；`git diff --check` exit 0。原三文件最晚变更为 17:29:20，执行者 PVF 测试和本次独立验证均晚于该时间。本轮验证不改变既有真实 DPI / 真实客户端 / 公网可达性方面的证据边界。

Verdict: `failed`

## Independent Review Round 2

- Reviewed execution: `Execution Round 2`；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2、Plan T-010—T-011。本轮依 `reviewing-task-units-quick` 与 `verification-before-completion` 只复核 QR1-F1 的文档修订及直接一致性，不替代完整交付审查。
- Reviewed identity: 三个 owned 文件独立重算 manifest 为 `C9A8B5D00F4B084EE7770501B03A661E57B08D813D782AF20D86082AD4740789`，与 Execution Round 2 及 verification YAML 一致。临时 publish 输出仍排除在 manifest 外。本轮仅追加此节，未修改产品、registry、其他 review 或 Git 历史。

### Finding Disposition

QR1-F1 已闭合。现有正文明确按 Kestrel 实际 `Request.IsHttps` / TCP peer 区分：非 loopback HTTP 在读取 body 前拒绝；同机代理经 `127.0.0.1` HTTP 命中 loopback 例外，允许进入认证。文档同时明确 Host 未启用/不信任 forwarded headers、无法证明原始客户端 TLS，且同机模式必须 loopback-only bind、仅代理访问、代理强制外部 HTTPS、无绕过入口；这些是部署约束，不声称 Host 会自动建立代理隔离。未来消费代理声明仍需单独的中间件/精确信任名单安全变更。排错表采用同一条件，当前 runbook 中未发现旧“所有 HTTP 代理上游都拒绝”的措辞残留，无新的局部阻塞项。

### Independent Evidence and Checks

- 重新核对 `CredentialTransportGateMiddleware` 与 middleware 接线：判断条件、拒绝后立即 return、gate 先于 endpoint 内 `ReadCredentialsAsync` 的执行顺序与修订一致；Host/LauncherApi 源码未配置 forwarded headers。
- 于 17:50:14（+08:00）独立执行两组 transport 测试共 12/12 Passed，exit 0，逐条 TRX 只有 Passed：8 项非 loopback 明文用例明确断言 `secure_transport_required`、body ReadCount=0、command calls=0；4 项许可传输用例覆盖 loopback HTTP / remote HTTPS 与注册/登录路径。证据为 `C:\Users\Gloria\AppData\Local\Temp\task06-independent-review-4487427b4ef14814ab743c99cc85c833\reviewer-round2-transports.trx`。
- Round 1 独立真实发布复现仍适用：loopback HTTP 携带表示远程原始来源/明文的 forwarded headers，结果为 `401 invalid_credentials`，证明实际 loopback 例外，而非外部 TLS 证明。当前 Host 与 LauncherApi Release DLL 的 SHA256 均与该独立发布目录中的对应 DLL 相同，发布示例也与源示例 SHA256 相同。本轮无产品行为变动，不重复创建真实代理或公网部署来扩张证据范围。
- Round 1 已独立核验的 clean publish、发布 Launcher→Host smoke、唯一 BaseUrl、九项目逐项 TRX、两条条件 skip 的同 testId 反向 Passed，以及独立 Host 17/17、Launcher 60/60、Release build 0/0 继续支持其原范围；不把它们表述为本轮重新执行的整仓测试。运行手册其余配置、三类端点、逐 AccountId 迁移和日志章节保持原义。
- `git diff --check` exit 0；检查时 Launcher / DNF 进程总数为 0。后续最终漂移、完整 change review、逐 AC fresh verification 及接受决定仍归 controller。

Verdict: `passed`
