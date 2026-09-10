# TASK-05 — .NET 10 WPF 登录启动器

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付严格单 BaseUrl 配置、v1 HTTP client、登录/注册/状态机、只记账号、35 槽 A21 参数与安全进程启动、功能优先 WPF 四态；覆盖 RQ-001—RQ-003、RQ-005、RQ-011、RQ-014，AC-001—AC-002、AC-005—AC-006、AC-010、AC-014—AC-015；Plan BD-001、BD-005、T-008—T-009。
- Owned boundary: 新增/修改 `Launcher/Dfo.Launcher`、`Launcher/Dfo.Launcher.Tests`，把项目接入 solution，允许为测试添加启动器专属 seam/package。不得修改服务端认证或 Game 协议行为、客户端二进制/只读目录或用户文档；不得部署到实际客户端目录；不得提交 Git commit。
- Required checks: TDD RED/GREEN；INI 全拒绝矩阵、bootstrap 版本/网络恢复、注册/登录/重复提交、偏好仅账号、秘密日志哨兵、35 槽和特殊字符、过期/缺 exe/进程失败。Release build/tests；启动 WPF smoke，保留登录/注册/加载/错误四态离屏布局/截图与当前桌面 DPI 的实窗证据，不切换系统显示缩放，且不能留下运行进程。
- Dependencies: TASK-03 已接受的 HTTP v1 请求/响应和错误合同。

## Execution Round 1 — 2026-09-10

### Changed

- 新增 `Launcher/Dfo.Launcher` 与 `Launcher/Dfo.Launcher.Tests`，两项目使用独立的
  `net10.0-windows`、集中包版本和严格编译属性，并接入 `Server/86JP.Server.slnx` 的 Launcher
  solution folder。没有给 Launcher 引入服务端实现依赖，也没有修改 Game/客户端二进制。
- `Launcher.ini` 解析器只接受 `[Launcher]` 下唯一 `BaseUrl`：拒绝缺失/重复/未知节键、相对或
  非 HTTP(S) URI、user-info/query/fragment，以及非 loopback 明文 HTTP；只规范化末尾 `/`。
  示例配置也只有该单一业务键。
- 本地偏好只定义 `loginId` 与 `remember`，保存到 `%LOCALAPPDATA%/DfoLauncher/preferences.json`，
  通过同目录临时文件、落盘 flush 和原子替换更新；关闭记忆时不保留账号，密码与票据没有偏好字段。
- 新增 typed Launcher API v1 client：固定 bootstrap/register/login 相对路径、typed DTO、默认 5 秒
  timeout、调用方 CancellationToken、稳定安全错误降级。全部路径使用 `async/await` 与
  `ConfigureAwait(false)`；调用方取消向上传播，timeout 与 network 分开映射，后续重试不受前次失败影响。
- `LauncherViewModel` 实现 ConfigError/Checking/Ready/Busy/Error/Launched 六态，以及登录/注册模式、
  bootstrap 版本与 ready 门、字段/全局错误、重试和 Interlocked 重复提交门。Busy 只显示 loading，
  不再和表单叠层；密码只由 `PasswordBox` code-behind 在当前提交传入，导航、重试及提交 finally 均清空。
- `A21LaunchArgumentBuilder` 逐值保持 35 槽模板，只在槽 1—4放 HTTP 登录结果中的 IPv4、端口、账号、
  票据，并拒绝无法无歧义编码的值。`GameLauncher` 在客户端时钟 `now >= expiresAtUtc`、缺少同目录
  `DNF.exe` 或构造/进程失败时返回安全错误；成功仅用 `ProcessStartInfo.ArgumentList.Add` 加一个参数，
  `UseShellExecute=false` 且工作目录为启动器目录。
- WPF 主窗口提供登录、注册、loading、错误、成功反馈及可访问的账号/密码控件。为可重复视觉验收增加
  `--visual-state=login|register|loading|error`，该模式跳过网络且绝不启动 DNF；视觉测试让每个状态/DPI
  在独立 STA 上下文中渲染，显式验证所有必需命名控件而不是只检查碰巧出现的元素。

上述 12 个产品文件、8 个测试文件、2 个 Launcher props 与当前 solution 文件共 23 个文件，按路径
排序，以每行 `path<TAB>SHA256`、UTF-8、LF 分隔且无末尾 LF 计算的 manifest SHA256 为
`A74932BEA5AC1B1FF18F2A8AB606B1D02717B2E785FBA631DA6BA6F5FBF21A7E`。

### Why

启动器只信任一个 HTTPS/loopback HTTP API 根地址，使 Channel 入口、Game 路由、账号和票据都只能来自
已接受的 v1 登录响应，而不能由本地宽松配置重新注入。typed 短超时 client 与显式状态机把网络失败、
版本不兼容、服务不可用、认证拒绝和启动失败都收束为可重试状态；原子操作门保证等待期间不会重复签票
或重复创建进程。

35 槽参数由固定模板和受控动态槽生成，再经 `ArgumentList` 单项传输，避免 shell 拼接与特殊字符改变
参数边界。密码框不做 ViewModel binding，本地文件没有秘密字段，生命周期日志只接受固定事件码，因而
正常、拒绝与异常路径都不需要保存或记录密码、票据或完整参数。

### Checks

证据均位于 `../evidence/`，汇总见 `task05-round1-verification.yaml`：

- TDD RED：配置 parser 尚为占位实现时，首个契约测试 1/1 按预期因 `NotSupportedException` Failed；
  `task05-round1-red.log/.trx`。最终 Release Launcher 全量 53/53 Passed，逐项 TRX 无 failed、skipped
  或未分类结果；`task05-round1-green.log/.trx`。
- 53 条包括配置 19、偏好 3、HTTP 6、A21/进程 10、ViewModel 7、视觉 8。HTTP 覆盖 caller
  cancellation、timeout、network 与恢复；状态测试覆盖配置不发请求、版本/网络重试、注册/登录、字段
  错误、启动失败、重复提交及 Busy/form 互斥；进程测试逐槽验证模板、单参数、特殊字符、过期点、缺 exe、
  启动异常与秘密哨兵。
- 视觉子集 8/8 Passed；`task05-visual-render.log/.trx`。四态分别以真实 RenderTargetBitmap 96/144
  DPI 元数据输出 760×560 / 1140×840 PNG，每条独立 STA case 显式断言应出现的品牌、标题、表单/
  loading/错误及操作控件为 Visible、非零且在根边界内，同时断言互斥面板 Collapsed。controller 已人工
  复查全部 8 张新图完整且无裁剪。
- 本机保持 `AppliedDPI=168`（约 175%）不变，controller 对 Release EXE 的真实窗口另做 login smoke；
  截图 `task05-live-login-current-dpi.png` 显示账号/密码、登录/注册及主按钮完整可达。进程随后关闭，最终
  `Dfo.Launcher` 进程数为 0；preview 未发送网络请求且未启动 DNF。
- PNG SHA256：login 96/144 为 `5EE28460...5A9` / `BAA1634A...E69`，register 为
  `0157B88E...CA3` / `EA3C7F43...A62`，loading 为 `51776403...188` / `DACB64E6...9DAA`，
  error 为 `81565285...0A0` / `F9804AA9...120`；current-DPI live 图为 `2579FD0A...BF3C`。
  完整值记录在 verification YAML。
- `task05-security-sentinel.log`：11 处产品 event log 调用全部为固定字面量生命周期码，未发现
  Console/Debug/Trace/ILogger/file-append/StreamWriter sink；无 `ProcessStartInfo.Arguments` 赋值，
  存在单一 `ArgumentList` 与 `UseShellExecute=false`；preferences 仅两字段，PasswordBox 无绑定且有
  4 个清空点，示例 INI 仅 BaseUrl。运行时哨兵同时证明安全错误、偏好和捕获日志不含密码/票据/完整参数。
- 最终 `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning / 0 error；
  `task05-round1-solution-build.log`。`git diff --check` 无输出、exit 0。

### Recovered Exception

初版视觉测试在同一 STA 中顺序复用多个未显示 Window，并只遍历“已经可见”的元素，产生了缺失品牌栏、
表单与标题仍假绿的 PNG。这是执行期发现的验收假设错误：最终改为每个状态/DPI 独立 STA，并对该状态
必须存在的命名控件作正向可见/尺寸/边界断言、对其他面板作反向互斥断言；旧图已被覆盖且未计入证据。
修正不改变业务范围，8 张新图和真实 current-DPI 窗口均已人工复核。

### Remaining

TASK-05 Execution Round 1 的实现与直接验证已完成，等待 controller 安排独立 quick review。本轮没有
修改服务端认证/Game 协议行为、客户端二进制、用户文档、registry 或既有 review，没有部署，也没有创建
Git commit。

## Independent Review Round 1

- Reviewed execution: TASK-05 `Execution Round 1` / Task Unit Contract revision 1；核对 root TaskContract revision 1、approved Spec revision 2 的 RQ-001—RQ-003 / RQ-005 / RQ-011 / RQ-014、AC-001—AC-002 / AC-005—AC-006 / AC-010 / AC-014—AC-015，以及 Plan BD-001 / BD-005 / T-008—T-009。依 `reviewing-task-units-quick` 与 `verification-before-completion` 规则做独立局部审查，不依赖执行者的通过结论。
- Reviewed identity: 阅读 Launcher 产品、测试、props、solution 接入与所列证据；独立重算 23 文件 manifest 为 `A74932BEA5AC1B1FF18F2A8AB606B1D02717B2E785FBA631DA6BA6F5FBF21A7E`，与报告相同。本轮只追加此 review；诊断项目及新测试输出仅在独立临时目录，没有修改产品、registry、其他 review 或提交 Git。

### QR1-F1 — [P1] 自动重定向绕过 loopback HTTP 的凭据传输边界

`Launcher/Dfo.Launcher/Api/LauncherApiClient.cs:53` 用默认 handler 创建 HttpClient，保留自动重定向。INI parser 只校验初始 BaseUrl；允许的 loopback HTTP 地址若返回 307/308 到非 loopback HTTP，HttpClient 会在未重新检查安全条件的情况下把原 POST body（含密码）转发过去。初始配置合法并不能保证真正携带凭据的目的地址仍合法，违反本 Task 的 HTTP 安全门和密码只经 HTTPS/本机例外传输的要求。

独立复现使用当前 Release 的真实 `LauncherConfigurationParser`、`LauncherApiClientFactory`、`LauncherApiClient`，以及只运行于本机临时端口的真实 Kestrel：初始 BaseUrl 是 `http://127.0.0.1:<port>/`，LOGIN 返回 307 到同一台机器的非 loopback IPv4 `/capture`。仅使用自造诊断字符串，未发送用户凭据或访问外部服务器；只在诊断进程中清空默认 proxy，防止请求经过系统代理。实际输出：

```text
redirect: initial_loopback=True, destination_loopback=False, destination_https=False, captured_dummy_password=True, result=review_complete
```

最小修正：该固定 v1 client 默认禁止自动重定向，并将 3xx 映射为安全可重试错误；若业务确需跳转，则逐跳在发送 body 前执行同等或更严的 scheme/目标校验，不能事后检查最终 URI。新增真实 handler/本机 HTTP 307/308 回归，证明非法下一跳完全收不到凭据，同时保留正常 loopback HTTP 与 HTTPS。

### QR1-F2 — [P2] 配置的 timeout 不覆盖响应正文，启动器可永久停在加载状态

`LauncherApiClient.cs:112` 使用 `HttpCompletionOption.ResponseHeadersRead`，随后 `:117` / `:150` 的 JSON 正文读取只收到调用方 token。HttpClient 的 Timeout 在请求返回 headers 后不再约束这些 body reads。服务器一旦正常返回响应头、随后挂起正文，bootstrap 会一直 Checking，注册/登录会一直 Busy；当前 UI 没有可取消按钮，重复提交门持续锁住，因而不满足有界超时和可恢复重试的要求。

独立复现将真实 client 的 HttpClient.Timeout 设为 100 ms，handler 立即返回真实 StreamContent，正文 stream 等待其读取 token；分别测试成功 200 与错误 503 的正文路径。观察超过四倍 deadline 仍未完成，仅显式取消调用方 token 后退出：

```text
body_timeout: status=200, configured_ms=100, elapsed_ms=490, completed=False
body_timeout: completes_only_after_caller_cancel=True
body_timeout: status=503, configured_ms=100, elapsed_ms=465, completed=False
body_timeout: completes_only_after_caller_cancel=True
```

最小修正：为整个 HTTP 操作建立覆盖 send、成功 body 和错误 body 的统一 deadline，并保持调用方取消与自身 timeout 的不同外观；也可选择实际覆盖正文的受控读取方式。新增“立即返回 headers、正文阻塞”的成功/错误双分支测试，断言 deadline 内返回 request_timeout、状态可重试且后续请求正常，不能只让假 handler 主动抛 `TaskCanceledException` 来证明超时生效。

### QR1-F3 — [P2] 字段校验原因永远位于被折叠的表单中

`LauncherViewModel.cs:361` 的 `ApplyError` 将状态切为 Error 并设置 `FieldErrorText`；`:125` 的 `IsFormVisible` 只在 Ready 为 true。然而唯一的 `FieldErrorText` 绑定位于 `MainWindow.xaml:78` 的 FormPanel 内，而 ErrorPanel 只绑定 GlobalError。因此 API 400 的具体账号/密码原因不会被用户看见，只显示“请求字段无效”；点击重试又在恢复 Ready 前清空字段原因。现有 `Api_field_error_is_displayed_without_secret_or_launch` 仅验证 ViewModel 字符串存在，并未验证实际可见性。

独立使用真实 ViewModel、真实 MainWindow/绑定和只返回指定 API 400 的测试接缝复现；状态对应如下：

```text
field_error: state=Error, text=账号格式错误。, form=Collapsed, error=Visible, global=请求字段无效。
field_error_after_retry: state=Ready, field_text=<null>
```

最小修正：让字段原因在当前错误状态的可见 UI 中显示（例如放到 ErrorPanel），或设计可编辑的字段错误态，但保留 Busy 与表单互斥及重复提交门。补一条从实际 API fieldErrors 进入错误状态的 WPF 可见性/布局回归，不能只渲染不含字段错误的 network-error preview。

### Independent Checks and Evidence Limits

- 独立诊断源码为 `C:\Users\Gloria\AppData\Local\Temp\task05-independent-review-b8e681452278411b965100f29a86074d\Program.cs` / `proof.csproj`，直接引用当前 Release DLL；`dotnet run --project proof.csproj -c Release` exit 0，输出如上。超时复现使用受控正文 stream；重定向复现使用真实本机 HTTP；字段错误复现使用真实 WPF 绑定但不启动游戏。专用 Kestrel 已停止、诊断进程已退出。
- 独立逐项解析原 TRX：RED 为指定 parser 用例 1 Failed；最终 Launcher 53 Passed；视觉 8 Passed。最晚 owned 修改 16:26:55，视觉/最终测试开始于 16:27:10 / 16:27:43（+08:00），证据晚于源码。现有全绿没有覆盖以上三条组合路径。
- 评审另于 16:38:29（+08:00）运行完整 Launcher Release `--no-build` 测试，53/53，exit 0，逐项均 Passed；新视觉输出重定向到诊断目录下 `visual/`，没有覆盖原评审证据。TRX 为同目录 `reviewer-launcher.trx`。评审完整 Release solution build 也成功，0 warning / 0 error。
- 独立查看原 8 张 96/144 DPI 四态 PNG 和真实窗口截图，画面完整，未见所展示状态的控件裁剪；全部 9 张 SHA256 与 YAML 一致。PNG 尺寸为 760×560 / 1140×840，pHYs 换算约 95.9866 / 143.9926 DPI（整数像素/米舍入）。96/144 是离屏 RenderTargetBitmap 的输出分辨率，不冒充系统显示设置切换；真实窗口图为 747×554，图像 metadata 为 120 DPI，单凭该文件不能证实当时窗口实际显示 DPI 等于 registry AppliedDPI=168。此限制不独立列为阻塞，但不可据此宣称完成所有实际显示器 DPI 行为测试。
- 静态抽查：INI 确实只接受唯一 BaseUrl、拒绝未知/重复键节和初始不安全 URL；偏好只有 loginId/remember、原子替换、不保存密码票据；PasswordBox 未绑定秘密且导航/重试/提交 finally 清空；11 处 log.Write 调用都是固定事件码；35 槽模板、同目录 DNF.exe、单 ArgumentList、UseShellExecute=false、过期点/缺文件/进程异常安全错误均有现有测试。以上成立不抵消 QR1-F1 的下一跳安全缺口。
- 检查结束时 `Dfo.Launcher` 进程计数为 0；没有启动真实 DNF、改显示设置或部署到客户端目录。`git diff --check` 无输出、exit 0。由 controller 处置三项返工，修复后须进行新独立复审；本轮不宣称 TASK-05 或完整交付完成。

Verdict: `failed`

## Execution Round 2 — 2026-09-10

### Changed

- 修复 QR1-F1：`LauncherApiClientFactory` 现在显式创建
  `HttpClientHandler { AllowAutoRedirect = false }`，固定 v1 client 不跟随任何 3xx；客户端把收到的
  3xx 直接映射为不含 Location 或秘密的 `redirect_not_allowed` 可重试错误。测试使用两个真实本机
  Kestrel：合法 loopback BaseUrl 的第一跳返回 307/308，第二跳捕获端逐项证明 request count 为 0、
  password body 为 null；同一默认 factory 对第一台 Kestrel 的正常 bootstrap 仍成功。
- 修复 QR1-F2：client 构造时固定整个操作的 deadline；每次请求建立调用方 token 的 linked CTS，并把
  同一个 deadline token 传给 `SendAsync`、成功 JSON body 和错误 JSON body。生产 factory 的 HttpClient
  自身 timeout 设为 Infinite，避免双重计时器，默认操作 deadline 仍为 5 秒。deadline 触发稳定返回
  `request_timeout`；若调用方 token 已取消则不吞掉 OCE，继续保持 caller cancellation 的不同外观。
- 修复 QR1-F3：API 返回非空 `fieldErrors` 时，ViewModel 回到 `Ready` 而不是进入一般 `Error`，保持
  FormPanel 可见、字段文本有序显示，并在 finally 释放操作门后立即允许修改和重提。没有字段原因的网络、
  认证和启动错误仍进入 Error/Retry。`FieldErrorTextBlock` 现在有明确名称，真实 MainWindow 测试验证其
  可见、非零，账号输入可编辑且主按钮启用。
- 测试项目增加 `Microsoft.AspNetCore.App` framework reference，仅用于默认 factory 的真实本机 Kestrel
  回归；没有给产品项目增加 ASP.NET 依赖。Round 2 只修改上述三个产品文件及四个 Launcher 测试文件，
  未触碰服务端认证/Game 行为、客户端二进制或用户文档。

当前 12 个 Launcher 产品文件、8 个测试文件、2 个 Launcher props 与 solution 文件共 23 个文件，
按路径排序，以每行 `path<TAB>SHA256`、UTF-8、LF 分隔且无末尾 LF 计算的 manifest SHA256 为
`10CC898725A7556206092A0A82EB3A1ADFB24824DDAA1871AA8A83EBBDB1BD29`。

### Why

初始 BaseUrl 安全并不能授权后续目标；禁止自动重定向把安全判断固定在用户明确配置且已严格校验的唯一
origin 上，避免 307/308 原样携带密码越过 loopback/HTTPS 边界。deadline 也必须属于整个 API 操作而
不是只属于“收到 headers”之前，否则服务端可以通过挂起任一响应正文永久占用 Checking/Busy 状态。

字段校验拒绝与网络/服务故障的恢复动作不同：前者需要在原表单就地修正并重提，后者才需要重新检查服务。
因此保持既有六态枚举，以 `FieldErrors.Count > 0` 精确分流到 Ready/form，既避免增加含混状态，也保留
一般 Error/Retry 语义与 Busy/form 互斥。

### Checks

证据位于 `../evidence/`，汇总见 `task05-round2-verification.yaml`：

- 定向 RED 共 7 条：6 Failed + 1 Passed。307/308 两条得到错误 outcome 并实际触发旧自动跳转；
  success-200/error-503 两条 concrete stalled `StreamContent` 都越过 150 ms，只在两秒 caller watchdog
  取消时抛 OCE；ViewModel 得到 Error 而非 Ready，真实 WPF 找不到应命名/可见的字段错误控件。caller
  cancellation 对照组原本即正确向上传播并可恢复，故 1 Passed；`task05-round2-red.log/.trx`。
- 最小产品修复后同一 7 条全部 Passed；`task05-round2-green.log/.trx`。重定向第二 Kestrel 对 307/308
  均为零请求/零 body，普通 loopback bootstrap 成功；两个阻塞 body 在约 150 ms 返回
  `request_timeout` 后第二请求成功，caller cancellation 仍抛 OCE；字段错误保持可见表单并成功立即重提。
- 最终 Launcher Release 全量 59/59 Passed，逐项 TRX 无 failed、skipped 或未分类结果；
  `task05-round2-full.log/.trx`。新增净 6 条由 307/308 两 case、success/error stalled body 两 case、
  stalled-body caller cancellation 一 case、真实 WPF 字段错误一 case组成；原 ViewModel case 改为更强
  行为断言，故总数从 53 增至 59。
- 四态×96/144 DPI 的独立 STA 视觉矩阵再次 8/8 Passed，PNG 的尺寸、DPI metadata、必需控件和边界
  断言保持；`task05-round2-visual.log/.trx`。最终八张 PNG SHA256 与 Round 1 的已人工接受图一致，
  完整值及 current-DPI live 图 hash 记录在 Round 2 verification YAML。
- `task05-round2-security-sentinel.log` 为 pass：11 处 event log 仍全为固定字面量事件码、无未批准日志
  sink；新增确认 `AllowAutoRedirect=false` 和 linked token 覆盖 send/success body/error body；其余单一
  ArgumentList、`UseShellExecute=false`、偏好两字段、PasswordBox 无绑定/四处清空、INI 单 BaseUrl 均保持。
- 最终 `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning / 0 error；
  `task05-round2-solution-build.log`。`git diff --check` 无输出、exit 0；最终 `Dfo.Launcher` 进程数为 0。

### Recovered Exception

首版 F2 回归使用真实 Kestrel socket 挂起正文，但该传输路径会被 HttpClient 自身在 timeout 后关闭，因而
在旧产品上意外通过，不能复现评审指出的任意 `HttpContent` body-token 缺口。产品修改前即把 RED 改为
评审同构的真实 `StreamContent`：立即返回 headers，正文的实际异步 read 只响应传入 token、不会主动抛
timeout。最终 RED 在 success/error 两分支都稳定等待至两秒 caller watchdog，GREEN 则由统一 150 ms
deadline 取消正文 read；这项执行期测试策略修正不改变产品范围，也没有把主动抛
`TaskCanceledException` 当作超时证明。

### Remaining

TASK-05 Execution Round 2 已修复 QR1-F1/F2/F3 并完成直接回归，等待 controller 安排新的独立 quick
review。本轮没有修改 review、registry、服务端/Game、客户端二进制或用户文档，没有部署，也没有创建
Git commit。

## Execution Round 3 — 2026-09-10

### Changed

- 修复 QR2-F1：bootstrap 失败不再复用可把字段错误送回表单的认证错误路径。
  `CheckServiceAsync` 现在调用独立的 `ApplyServiceError`，无论失败响应是否携带 `fieldErrors`，都保持
  `State=Error`、`CanSubmit=false` 并清空字段文本；只有成功通过 bootstrap 的版本与 ready 状态检查后，
  注册/登录拒绝才进入 `ApplyCredentialError` 的可编辑 Ready/form 路径。
- 增加一条阶段边界回归：先让真实 typed client 取得 HTTP 400 + `fieldErrors` bootstrap 响应，尝试提交
  凭据，再以成功 bootstrap 重试；逐项断言失败阶段没有 login 请求、成功重试后才解锁。定向 GREEN 同时
  保留一般 bootstrap 错误与认证字段错误的既有行为。

当前 23 个 Launcher owned 文件的 manifest SHA256 为
`C842D84B929E9E699F951F300E484A2DFFCA0FC81D6811D43CA66D50CED3BAD7`。

### Checks

- TDD RED：新增边界用例 1/1 Failed，旧实现实际得到 `State=Ready`、`CanSubmit=true` 并发送 1 个 login
  请求；`task05-round3-red.log/.trx`。
- 同一用例修复后通过；包含一般 bootstrap 错误、QR2-F1 与认证字段错误的定向集合 3/3 Passed；
  `task05-round3-green.log/.trx`。
- Launcher Release 全量 60/60 Passed；逐项 TRX 只有 Passed，无 failed、skipped 或未分类结果；
  `task05-round3-full.log/.trx`。
- 完整 `Server/86JP.Server.slnx` Release build 为 0 warning / 0 error；
  `task05-round3-solution-build.log`。
- 新鲜静态 sentinel 确认固定事件日志、无未批准 sink、禁用 redirect、完整正文 deadline、bootstrap 与
  credential 错误路径分离、INI 单 BaseUrl，以及 `Dfo.Launcher` 进程数 0；
  `task05-round3-security-sentinel.log`。`git diff --check` 无输出、exit 0。

### Remaining

TASK-05 Execution Round 3 已完成 QR2-F1 的最小修复和直接回归，等待新的独立 quick review。本轮未修改
既有 review、registry、服务端/Game、客户端二进制或用户文档，没有部署，也没有创建 Git commit。

## Independent Review Round 2

- Reviewed execution: `Execution Round 2` 与三条原 finding；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2、Plan BD-005 / T-008—T-009。依 `reviewing-task-units-quick` 与 `verification-before-completion` 的独立局部规则，核对修复结果及直接回归，不裁定完整交付。
- Reviewed identity: 独立重算 23 文件 manifest 为 `10CC898725A7556206092A0A82EB3A1ADFB24824DDAA1871AA8A83EBBDB1BD29`，与 Round 2 及 verification YAML 一致。本轮只追加此 review；诊断和新测试输出在独立临时目录，无产品、registry、其他 review 或 Git 提交修改。

### Prior Finding Dispositions

- QR1-F1 已闭合：生产 factory 明确 `AllowAutoRedirect=false`，所有 3xx 在读取响应 body 前映射固定 `redirect_not_allowed`，错误不含 Location/密码。独立复跑两条真实 Kestrel 307/308 用例均证明第二服务器 request count=0、capturedBody=null，并保持普通 loopback bootstrap 可用。额外原样重跑 Round 1 的非 loopback 目标独立复现，得到 `captured_dummy_password=False, result=redirect_not_allowed`；不是只验证反射属性或把跨 origin 请求改为安全目标。
- QR1-F2 已闭合：每次请求建立独立 linked CTS，统一 token 覆盖 send、成功 JSON 及错误 JSON。工厂的 HttpClient timeout 为 Infinite，实际 deadline 默认 5 秒。独立复跑 200 / 503 stalled StreamContent 的 150 ms 有界超时和随后恢复通过；caller cancellation 仍传播 OCE，后续请求成功。原 Round 1 两个诊断请求也已在观察点前完成，不再无限等 caller cancel。
- QR1-F3 的凭据校验路径已闭合：非空 fieldErrors 保留 Ready，FieldErrorTextBlock 与 FormPanel 可见，操作门 finally 释放后可改账号立即重提；无 fieldErrors 的普通错误仍 Error。真实 MainWindow 绑定/编辑回归和 ViewModel 两次提交回归通过。原独立 WPF 复现输出 `state=Ready, form=Visible, error=Collapsed`。但共用错误处理影响 bootstrap，产生下述新直接回归。

### QR2-F1 — [P2] bootstrap 失败携带字段错误时也会开放凭据提交

`Launcher/Dfo.Launcher/ViewModels/LauncherViewModel.cs:317` 的 bootstrap 非成功分支调用共用 `ApplyError` 后立即返回；该 helper 在 `:363` 只按 FieldErrors 非空就把 State 改为 Ready。于是任何携带 fieldErrors 的失败 bootstrap 响应都会跳过后续 ApiVersion / Status 校验、展示表单并令 CanSubmit=true。apiClient 此时已经创建，SubmitAsync 会真的发出 LOGIN，尽管从未收到成功 v1 ready bootstrap。修复字段错误应只恢复已经过 bootstrap 的凭据表单，不能把失败的服务检查也解释为可提交状态，违反 RQ-003 / AC-001—AC-002 的前置门禁。

独立复现使用当前 Release 的真实 `LauncherApiClient` 和 ViewModel；仅 HTTP transport 由受控 handler 返回实际 JSON，bootstrap 响应为 HTTP 400：

```json
{"code":"invalid_request","message":"请求字段无效。","fieldErrors":{"request":"入口不支持当前请求。"}}
```

没有伪造成功 bootstrap，也没有绕过 typed client 的反序列化。InitializeAsync 后调用 SubmitAsync，结果如下：

```text
after_failed_bootstrap: state=Ready, form=True, can_submit=True, bootstrap_success_count=0
after_submit: login_requests=1, state=Error
```

该复现证明客户端对失败响应的状态错误，不声称当前配套后端正常 bootstrap 会产生此字段；错误 endpoint、不同版本服务或结构兼容的失败响应也不应被客户端提升为成功就绪。

最小修正：将“fieldErrors 回 Ready”限制在已通过 bootstrap 的 register/login 校验路径；CheckServiceAsync 的任何失败，无论是否含字段原因，均应保持 Error/不可提交，直至某次成功 bootstrap 同时验证版本 1 与 ready。新增失败 bootstrap 带 fieldErrors 的回归（至少初次初始化，可同时覆盖 retry）：断言 State=Error、CanSubmit=false、SubmitAsync 不发送凭据；之后成功 bootstrap 再恢复 Ready。保留现有 credential fieldErrors 原地修正/重提及普通 Error/Retry 测试。

### Independent Evidence and Checks

- 新 finding 的临时源码为 `C:\Users\Gloria\AppData\Local\Temp\task05-round2-independent-review-d61b0918a3b542bdb18dc938c607c92d\Program.cs` / `proof.csproj`；`dotnet run --project proof.csproj -c Release` exit 0，输出如上。原 Round 1 临时复现源码也以当前 Release DLL 重新构建执行，exit 0，证明三项原现象改变；没有修改其源码来制造“已修复”。
- 独立逐项解析四份 Round 2 TRX：RED 为 6 Failed + 1 Passed、GREEN 7 Passed、最终全量 59 Passed、视觉矩阵 8 Passed，无其他 outcome。最终源码/测试最晚修改 16:53:34，最终全量开始于 16:54:04（+08:00），对应当前 manifest。单独 visual TRX 开始于 16:51:15，早于最后测试调整；最终全量已重新包含全部视觉用例，未把旧单独 visual 运行冒充最终源码后的检查。
- 评审另于 17:01:52（+08:00）独立运行完整 Launcher Release `--no-build`，59/59 Passed、exit 0，逐条 TRX Passed；结果为上述 Round 2 临时目录的 `reviewer-launcher.trx`。新 PNG 输出到其 `visual/`，原证据未覆盖。独立完整 Release solution build 同样为 0 warning / 0 error。
- 全部原 9 张 PNG SHA256 与 Round 2 YAML 一致，也与先前亲自检查过的图像字节一致；四态矩阵的 96/144 为离屏输出分辨率，真实显示 DPI 的既有证据限制保持，不升级声明。字段错误的实际可见/可编辑路径由新增真实 MainWindow 测试覆盖。
- 核对静态 sentinel 与当前源码：固定事件码仍 11 处，无新增秘密日志 sink；自动跳转关闭，统一 deadline token 贯穿 body；PasswordBox 清空、偏好仅账号/remember、唯一 INI BaseUrl、35 槽与单 ArgumentList 均保持。检查结束时 Dfo.Launcher 进程数为 0，临时诊断服务器已停止；没有启动真实 DNF 或改系统显示设置。`git diff --check` 无输出、exit 0。
- 59 项绿灯不能覆盖 QR2-F1：现有 bootstrap 失败测试没有 FieldErrors，字段错误测试只走已经成功 bootstrap 的 credential 路径。当前唯一阻塞为该直接回归，由 controller 处置修复后安排新的独立复审。

Verdict: `failed`

## Independent Review Round 3

- Reviewed execution: `Execution Round 3`；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2、Plan BD-005 / T-008—T-009。本轮依 `reviewing-task-units-quick` 与 `verification-before-completion` 仅复审 QR2-F1 和修复直接影响，不替代完整 Spec 验收。
- Reviewed identity: 独立重算当前 23 个 owned 文件 manifest 为 `C842D84B929E9E699F951F300E484A2DFFCA0FC81D6811D43CA66D50CED3BAD7`，与 Execution Round 3 及 verification YAML 一致。修改仅为在本 Task 末尾追加评审；未修改实现、registry、其他 review 或创建提交。

### Findings and Disposition

- QR2-F1 已闭合。`CheckServiceAsync` 的失败结果只进入 `ApplyServiceError`，后者无条件选择 Error、清空 FieldErrorText；成功响应仍必须依次通过 ApiVersion=1 和 Status=ready 才进入 Ready。注册和登录拒绝单独使用 `ApplyCredentialError`，不会影响 bootstrap 门禁。
- 使用 Round 2 原独立复现源码与当前 Release DLL 重新构建运行，没有修改 handler 或诊断源码：失败 HTTP 400 + fieldErrors 后输出 `state=Error, form=False, can_submit=False, bootstrap_success_count=0`，随后 SubmitAsync 输出 `login_requests=0, state=Error`。新仓库回归还用真实 typed client 证明随后成功 bootstrap 重试才恢复 Ready/CanSubmit=true。
- QR1-F1/F2/F3 未见直接回归：禁用自动跳转及 307/308 零转发、成功/错误正文统一 deadline、caller cancellation 后恢复、凭据字段错误可见且可编辑立即重提均在本轮完整测试中再次通过。普通 bootstrap 错误及版本门禁仍通过；未发现新的局部阻塞项。

### Independent Evidence and Checks

- 逐条解析执行者 Round 3 TRX：RED 1 Failed、GREEN 3 Passed、完整套件 60 Passed，无其他 outcome。产品最后变更为 17:07:37（+08:00）；GREEN 开始 17:07:49，完整套件开始 17:08:12，均晚于当前产品修改，manifest 一致。
- 独立运行完整 Release solution build，exit 0、0 warning / 0 error；随后于 17:18:20（+08:00）运行 Launcher 完整 Release 测试，exit 0、60/60 Passed。独立 TRX 每条均为 Passed，位置为 `C:\Users\Gloria\AppData\Local\Temp\task05-round3-independent-review-0f094d6bef5144c2a15e9d0dcc158fde\reviewer-launcher.trx`；本次视觉 PNG 输出到该目录的 `visual/`，没有覆盖原证据。
- Round 2 原样复现项目仍为 `C:\Users\Gloria\AppData\Local\Temp\task05-round2-independent-review-d61b0918a3b542bdb18dc938c607c92d\proof.csproj`，本轮执行 exit 0，观测结果见上。
- 静态 sentinel 与当前错误路径一致：11 个固定事件日志、0 个未批准 sink、redirect 关闭、完整正文 deadline、INI 单 BaseUrl 均保持。`git diff --check` 无输出、exit 0；评审检查时 Dfo.Launcher 进程数为 0。没有部署客户端、启动真实 DNF 或更改显示设置。
- 既有视觉证据限制不变：96/144 DPI 矩阵是离屏渲染输出，不声称完成真实系统 DPI 切换测试；本次完整套件覆盖该矩阵及实际 MainWindow 字段错误可见/编辑测试。本结论仅覆盖 TASK-05 当前执行结果的独立局部复审，controller 负责接受和后续整体验收。

Verdict: `passed`

## User Acceptance Adjustment — 2026-09-10

用户针对最终 drift 检查提出的精确双 DPI 实窗补证明确指示：“意思意思得了，这种临时登录页没必要费这大劲啊”。
据此，root Spec revision 3 与 Plan 将 AC-015 / T-009 收敛回 TaskContract SC-8 的功能优先边界：保留四态
离屏布局/截图、当前桌面 DPI 的真实窗口 smoke 和无阻塞布局结论，不再要求切换系统缩放或四态逐一完成
100%/150% 真实 HWND 取证。实验性的窗口 DPI 注入测试未保留，产品代码和既有功能测试没有因此改变。
