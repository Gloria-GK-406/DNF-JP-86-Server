# TASK-02 — Game LOGIN 票据兑换与提交后身份发布

## Task Unit Contract

- Revision: 1
- Outcome and references: 用一次性票据替换 Game 0x0001 的密码/自动建号语义，在领域事务持久提交后发布 OnlineUser，成功时保持 CMD 与六条通知，拒绝/投递失败关闭并清理连接；覆盖 RQ-009—RQ-014、AC-008—AC-012、AC-018；Plan BD-003、T-004、T-006。
- Owned boundary: 可修改认证相关 Application OnlineState/command、Protocol Game Login、必要的抽象网络关闭接缝及 Application/Protocol/Networking 测试。不得修改 HTTP API、Host 组合根、Infrastructure endpoint、WPF、用户文档；不得提交 Git commit。
- Required checks: TDD RED/GREEN；验证 transaction participant Prepare 不发布、SQLite commit 失败/取消保持匿名且票据可由第二连接兑换、持久提交后 participant 不因 cancellation 失败、连接消失/发布或发送失败时票据不可重放且无可用身份；无效票据不自动建号、不发成功 CMD/通知并关闭；成功帧/六通知顺序及 roster/选角/进城回归。运行相关 Application、Protocol、Networking Release tests。
- Dependencies: TASK-01 已接受的认证/票据契约与实现。

## Execution Round 1 — 2026-09-10

### Changed

在 TASK-01 Round 2 已接受内容之上完成 Game 票据登录闭环，沿用基线
`e567c803b99260786aaddb0d2d4612dc3334b6f4` 的共享工作树，未提交 Git。

- 将 Game `0x0001` 的第二个 dstring 和协议/命令命名从 `PasswordHash` 改为
  `Ticket`，保留 A21 的两个 dstring 及已知尾部兼容；旧
  `LoginGameUserCommand` 改为 `RedeemGameLoginCommand`，注册为
  `Transactional + OnlineUser Exclusive`。
- 新增 `IGameLoginAuthenticationDomainService`。兑换命令先通过 TASK-01 的原子票据消费
  取得既有 AccountId，再为同一 OnlineUser 登记内存事务 participant。该 participant
  `Prepare` 只验证当前映射仍为同一匿名连接并捕获 connection/generation；唯一可失败的
  SQLite participant 成功提交后，`CommitCanFail=false` 的 `Commit` 才发布身份，并忽略此时的
  cancellation。目标在耐久提交后消失或漂移时提交为非失败 no-op，票据保持已消费；Rollback
  不发布身份。
- 删除生产 `IAccountDomainService` / `AccountDomainService.LoginAsync`，并移除
  `IAccountRepository` 的生产 DI 注册，Game 路径不再调用 `GetOrCreateAsync`。旧 provisioning
  类只作为无生产引用、无 DI 注册的兼容实现保留；测试 fixture 直接调用它建立旧场景。
- Game handler 仅在兑换命令返回且精确身份已发布后构造/发送成功 CMD，再按既有顺序发送
  game options、hotkeys、两条 auction service、honour standing、inform notice。无效票据、
  事务失败/取消、发布目标丢失、设置读取或任一发送失败均不发明失败帧，记录固定非敏感类别，
  精确移除该 connection 的 OnlineUser 并请求关闭；耐久提交后的失败不恢复票据。
- 将网络关闭接缝明确为 `RequestCloseAsync`：它只保证关闭请求已调度。TouchSocket 的实际
  `CloseAsync` 在 receive callback 外异步执行，避免 callback 等待自身完成；既有选角/返回选人
  关闭调用同步更新，关闭原因通用化为 protocol handling failed。
- 新增 participant 顺序/取消/回滚/目标丢失、真实 SQLite 提交前取消重放、耐久提交失败重放、
  耐久提交后目标丢失不可重放、handler 成功顺序/拒绝/发送失败/日志秘密、decoder/注册模式及
  真实 TouchSocket 成功与无效票据关闭测试。成功 socket 测试逐帧断言 CMD + 六通知，并继续
  GET_USERINFO、返回选人、选角/进城既有回归；无效票据不建号、不消费原票据，第二连接可兑换。

TASK-02 相对已接受 TASK-01 的增量 owned 产品/测试集合为 24 个文件：

```text
Server/src/Dfo.Server.Abstractions/Accounts/IAccountDomainService.cs (deleted)
Server/src/Dfo.Server.Abstractions/Authentication/AuthenticationServices.cs
Server/src/Dfo.Server.Abstractions/Networking/INetworkServices.cs
Server/src/Dfo.Server.Application/ApplicationModule.cs
Server/src/Dfo.Server.Application/Authentication/AuthenticationDomainService.cs
Server/src/Dfo.Server.Application/Users/OnlineStateRegistry.cs
Server/src/Dfo.Server.Application/Users/UserDomainServices.cs
Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkConnectionCloser.cs
Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs
Server/src/Dfo.Server.Protocol/Game/Login/GameLoginMessageDecoder.cs
Server/src/Dfo.Server.Protocol/Game/Login/GameLoginPayloadCodec.cs
Server/src/Dfo.Server.Protocol/Game/Login/LoginGameUserCommand.cs
Server/src/Dfo.Server.Protocol/Game/Login/Messages.cs
Server/src/Dfo.Server.Protocol/Game/ReturnToSelection/ReturnToSelectionHandler.cs
Server/src/Dfo.Server.Protocol/Game/SelectCharacter/SelectCharacterHandler.cs
Server/src/Dfo.Server.Protocol/ProtocolModule.cs
Server/tests/Dfo.Server.Application.Tests/Authentication/GameLoginAuthenticationTransactionTests.cs
Server/tests/Dfo.Server.Application.Tests/Authentication/GameLoginCommandTransactionTests.cs
Server/tests/Dfo.Server.Application.Tests/Game/GameLoginTests.cs
Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs
Server/tests/Dfo.Server.Application.Tests/Support/UserDomainServices.cs
Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs
Server/tests/Dfo.Server.Protocol.Tests/Game/CommandRegistrationTests.cs
Server/tests/Dfo.Server.Protocol.Tests/Game/Login/GameLoginCodecTests.cs
```

按路径排序，以 `path<TAB>SHA256`、删除项值 `DELETED`、行间 LF、UTF-8、无末尾 LF 计算，
上述当前内容 manifest SHA256 为
`315410B25D76274A4A4AC12BB00FD8AC57AE3C2CEFD649C7E234AE49EE510611`。关键内容 SHA256：
`OnlineStateRegistry.cs` = `C6F09A73817133191ADE9463EDC4D64651B70BC4A0D65112F71883FC25DDB0A8`；
`GameLoginHandler.cs` = `A91D577BE2430E620F8009E234B05C2273403036D6C762D444999BF5C8C3EC13`；
`GameLoginCommandTransactionTests.cs` = `084E33FAD7EEC999AEE25C05ADD96EB7E73431D174551B01EB03B2CDCC10F5F0`；
`GameLoginIntegrationTests.cs` = `117BC71D8A5E6EF94C0800CE1EE90B447C29AF1846191CE33C62E7542F4B9446`。
TASK-01 的 26 个已接受产品/测试文件及其既有证据仍在同一未提交 diff 中，不冒充本轮新增。

### Why

票据消费是持久真相，OnlineUser 是内存投影。把二者放进同一个领域事务、让 SQLite 成为唯一
`CommitCanFail=true` participant、让身份发布成为后提交且不可失败的 participant，可确保提交前
失败没有身份也没有票据消费，而提交后连接丢失不能倒转已经发生的单次消费。发布时重新检查
connection、generation、匿名状态和双向映射，避免旧连接的提交认证后来复用同一 OnlineUserId
或 connectionId 的对象。

handler 在命令后再次核对精确已发布身份，防止耐久提交后 no-op 被误当成可发送成功链；先加载
六条通知所需设置再发送 CMD，避免准备阶段失败时客户端只收到半条成功链。网络投递本身无法跨
七帧原子化，因此任一实际发送失败后的可恢复边界是关闭并删除该连接身份，同时保留票据已消费。

执行期发现 TouchSocket 在接收回调内 await `CloseAsync` 会等待该回调退出而形成自等待；无效票据
真实 socket 测试因此悬停。修正为显式“请求关闭”接缝并在回调外调度实际关闭后，同一测试稳定
收到 EOF；这改变了关闭接缝语义但未扩大业务边界。该首次悬停未另存为 TRX，不冒充自动化 RED。

TDD value gate：新增测试保护的是票据单次消费、持久提交/内存发布顺序、连接身份所有权及 A21
成功帧序；这些结果会被事务参与者排序、取消传播、旧自动建号入口或发送异常处理的改动破坏，
既有账号/OnlineUser 单元测试不能覆盖。测试以领域事务、真实 SQLite、真实 TouchSocket 和逐帧
解码观察结果，不绑定 handler 内部实现步骤。

### Checks

所有最终命令均在
`E:\Project\DNF\DNF-JP-86-Server\.worktrees\launcher-auth-login` 执行。

- TDD RED：新增 `MessageHandler_CommandFailureSendsNoSuccessAndRemovesTheAnonymousConnection`
  后，旧 handler 抛出 `InvalidOperationException("transaction failed")` 并保留匿名连接；1 failed，
  exit 1，证据 `../evidence/task02-round1-red.log/.trx`。实现后同类及扩展 GameLogin focused
  14/14 通过，证据 `task02-round1-application-focused.log/.trx`。
- Protocol focused：LOGIN codec/tail 兼容与 command registration 16/16；证据
  `task02-round1-protocol-focused.log/.trx`。
- Networking focused：真实 TouchSocket 成功链与无效票据关闭 2/2；证据
  `task02-round1-networking-focused.log/.trx`。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后的最终 Release 全项目：
  Application 1337/1337、Protocol 714/714、Networking 59/59，均 0 failed、0 skipped；证据分别为
  `task02-round1-application-release.log/.trx`、`task02-round1-protocol-release.log/.trx`、
  `task02-round1-networking-release.log/.trx`。
- 已解析上述七份 TRX 的每个 `UnitTestResult`：RED 准确为目标用例 1 Failed；三个 focused 为
  14、16、2 条 Passed；三个最终全项目为 1337、714、59 条 Passed，没有依赖顶层 summary。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning / 0 error；证据
  `../evidence/task02-round1-solution-build.log`。`git diff --check` 无输出、exit 0。

一次全量 Protocol 验证命令最初未在该 PowerShell 进程设置 `PVF_ARCHIVE_PATH`，43 个既有 PVF
用例按预期报 `Script.pvf was not found`；用相同输出路径、正确环境覆盖重跑为 714/714。最终证据
文件只代表正确环境的全绿运行，该环境失误没有引发产品改动，也不被分类为实现回归。

### Remaining

- TASK-02 owned outcome 已完成，无已知实现阻塞，等待 controller 安排独立 quick review；本轮未写
  review/verdict、未改 registry、未提交 Git。
- HTTP adapter/Host 组合、HTTP→Channel Info→Game 的跨端完整闭环属于后续 TASK-03/TASK-04；
  WPF 与运行说明属于 TASK-05/TASK-06。本轮真实 socket 从已签发票据进入 Game，证明 Game
  接缝与后续 roster/选角路径，不冒充尚未实现的 HTTP/Launcher 端到端验收。

## Independent Review Round 1

- Reviewed execution: 本 Task `Execution Round 1`；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2 与 approved Plan。使用 `reviewing-task-units-quick` / `reviewing-code` 的独立 Task 范围规则；本轮不裁定整个交付。
- Reviewed identity: HEAD `e567c803b99260786aaddb0d2d4612dc3334b6f4`；逐项核对所列 24 个增量文件，按上文口径独立重算 manifest 为 `315410B25D76274A4A4AC12BB00FD8AC57AE3C2CEFD649C7E234AE49EE510611`，与执行记录一致。核对相关事务、在线状态、断线 observer 和关闭接缝调用链，未修改产品、registry、其他 Task 或提交 Git。

### Finding QR1-F1 — P2：已选角连接重复 LOGIN 被拒会跳过会话清理

`Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs:139` 在所有拒绝路径中先调用 `TryDisconnectConnection`，之后才请求关闭 transport。该 handler 没有将输入限定为匿名连接，所以已经选角/进城的连接再次发送 LOGIN（例如复用已消费票据）也会进入这条拒绝路径。

`OnlineStateRegistry.TryDisconnectConnection`（`Server/src/Dfo.Server.Application/Users/OnlineStateRegistry.cs:134`）只移除在线连接、用户及该账号的 active 映射，不调用 selected-session 清理。随后真实断线 observer 在 `Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs:70` 因找不到 OnlineUser 直接返回，永远到不了 `:93` 的 `cleanup.CleanupAsync`。因此 `SelectedSessionDomainService` 中的会话残留；正常清理所负责的在场/PvP、库存 lease、位置/运行快照、容器/冷却释放和离场位置保存也被绕过。实际后果是关闭连接后仍存在失去在线所有者的角色会话及资源，违反本 Task 的失败清理与选角/进城回归边界，以及 RQ-010 / RQ-012 的连续状态要求。

独立复现使用本轮当前 Release DLL 的真实 `OnlineStateRegistry`、`SelectedSessionDomainService`、`GameLoginHandler`、`OnlineUserConnectionObserver`：建立已认证并发布角色的连接，令命令返回无效票据结果，执行 handler 后再执行真实 observer；仅命令拒绝结果、close 请求和 cleanup 调用计数使用测试替身。`dotnet run --project proof.csproj -c Release` exit 0，决定性输出如下。此为真实领域/handler 组合复现，不冒充真实 socket 复现。

```text
before: online=True, selected=True
after_handler_and_disconnect: online=False, selected=True, cleanup_calls=0, close_requests=1
```

临时复现源码位于 `C:\Users\Gloria\AppData\Local\Temp\task02-review-22e8fabc43894c94874d64849a3a44f6\Program.cs` / `proof.csproj`，未改仓库测试或产品。

最小修正：拒绝必须同时收口该连接拥有的精确 selected session 与在线身份，遵守既有 online-user / lifecycle gate 顺序；可以在移除在线身份前完成对应清理，也可以调整断线清理入口，使在线映射已撤销时仍能按精确 selected-session ownership 清理。不能仅删除目录项而漏掉 lease、presence、runtime 和保存。补充已选角连接再次提交无效/复用票据的回归：无新成功帧，连接关闭，OnlineUser、selected directory 及其所持资源全部释放，替代连接/会话不受损；保留匿名拒绝和提交后失败票据不可重放检查。

### Evidence and Local Checks

- 独立逐项解析七份 `../evidence/task02-round1-*.trx`：RED 为所述 handler 异常用例 1 Failed；focused 为 Application 14、Protocol 16、Networking 2 条 Passed；最终 Release 为 Application 1337、Protocol 714、Networking 59 条 Passed，无失败/跳过。对应日志汇总一致；solution build 日志为零警告/错误。最晚产品修改为 13:00:02，最终三项目运行分别开始于 13:03:43、13:04:41、13:04:58（+08:00），证据晚于修改。
- 评审另于 13:17:13—13:17:14 启动精确 Release `--no-build` 复跑：Application filter `FullyQualifiedName~GameLogin|FullyQualifiedName~SelectCharacterLifecycleTests|FullyQualifiedName~ReturnToSelection`，36/36 Passed；Networking filter `FullyQualifiedName~GameLoginIntegrationTests`，2/2 Passed。逐项核对临时目录中的 `reviewer-application.trx` / `reviewer-networking.trx`，两命令均 exit 0。上述绿灯不覆盖 QR1-F1 的重复 LOGIN 组合路径。
- 已核对 Prepare 不发布、唯一可失败 participant 先提交、随后身份 Commit 忽略 cancellation 并重新检查 connection/generation/匿名状态；消费继承 TASK-01 的同事务原子条件更新和过期边界。注册确为 Transactional + OnlineUser Exclusive；生产 Game 自动建号服务和 DI 入口已关闭。成功 CMD 与六通知保持顺序，handler 的固定错误类别不包含账号或票据；未见当前日志调用直接记录命令/消息 body。
- `RequestCloseAsync` 已明确只保证调度，当前 production callers 全部更新。SelectCharacter 和 ReturnToSelection 都在 gate 释放后请求关闭，精确回归通过；真实无效票据测试收到 EOF，支持解除 receive callback 自等待的改法。QR1-F1 来自 LOGIN 提前删除在线映射，不是单纯的异步关闭命名问题。
- 证据限制：提交前取消用真实 SQLite；提交失败与提交后目标丢失的命令测试使用模拟 durable participant，证明领域事务/身份顺序，不等同于真实 SQLite commit 故障或真实 socket 故障。真实 socket 当前只有成功和普通无效票据。为核对明确交接，仅阅读 TASK-04 合同，确认其承担 AC-009 / AC-018 的真实跨端故障验证；本限制不另列本轮阻塞，但不得把这些最终 AC 宣称为已完成。
- 评审 `git diff --check` 无输出、exit 0。本次唯一阻塞 finding 为 QR1-F1；由 controller 处置返工，修复后应进行新独立复审。

Verdict: `failed`

## Execution Round 2 — 2026-09-10

### Changed

仅处理 `QR1-F1`，增量修改两个产品/测试文件，未改变 Game handler、网络关闭接缝或其他 Task：

- `Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs`：Game transport 断开时，若
  `OnlineUser` 已被协议拒绝路径即时移除，但 selected directory 仍按该 connection 持有精确会话，
  observer 仍以 AccountId 获取 `SelectedSessionTransitionGate` 独占租约，在锁内重新核对
  connection→session 映射后调用完整 selected-session cleanup。原有在线映射存在路径与该补偿路径
  共用同一个 cleanup 方法，保留 `includeOwner=false`、清理通知投递、
  `SelectedSessionCleanupFailure` 通知兜底和通用异常补偿语义。
- `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs`：新增真实组合回归，
  完成一次选角发布后向同一 handler 重复 LOGIN 并令兑换拒绝，观察无新增成功帧、OnlineUser 即时
  移除、close request 已记录；随后执行真实 disconnect observer，断言 cleanup 恰好一次且 selected
  directory 释放。另覆盖 observer 等待 lifecycle gate 时会话被替代：旧会话不得触发 cleanup，
  replacement 的 account/character/wire/connection 四个索引全部保持，旧 connection 索引消失。

本轮两个文件 SHA256：

```text
Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs  E9A03023BD56999B4367604EFC27FBE2BD93EBDD29CE2137D3C5691FB8E4A61F
Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs  456B18B4D1C6BCDDE66EC6E8EAA1DF975E53CBF86ADAA2FBADCDEE1BF9F54B5B
```

将 observer 加入 Round 1 的增量 owned 清单后，TASK-02 当前 25 个产品/测试文件按 Round 1 相同
口径计算的 manifest SHA256 为
`22FB7A44B7DFA402A411C557E1328D87E9D7FA430F26CD8AA435FF0257F507E4`。

### Why

拒绝 handler 必须立即撤销匿名、已认证或已选角连接的可用身份，不能等待异步 transport close；
因此不把清理移动到 handler，也不把 `RequestCloseAsync` 改回等待实际断开。身份移除后仍可用于
精确恢复清理所有权的事实，是 selected directory 的 connection→immutable session 映射。通过
该映射取得 AccountId lifecycle gate，再在锁内核对同一 record，可调用既有 cleanup service
释放 presence、PvP、库存 lease、位置/运行时、容器/冷却及 selected directory，而不复活身份。

捕获 orphaned session 与取得 lifecycle gate 之间允许发生替换，所以锁内必须重新读取
connection 映射并作 record 相等比较；不同 connection 的 replacement 会在目录的双向索引中替换
旧记录，旧 observer 随即 no-op。该条件与所有 cleanup service 的 exact-session 删除共同保护替代
会话。普通断线仍先持有 OnlineUser exclusive gate，再进入同一 lifecycle/cleanup 方法，原顺序不变。

TDD value gate：受保护结果是拒绝后既无可用 OnlineUser，也无遗留 selected-session 资源；现实
回归正是 handler 先删除身份导致 observer 早退，已有匿名拒绝测试和普通断线测试分开均为绿色，
无法观察两个真实组件组合后的泄漏。新测试从实际选角发布开始，驱动真实 handler、close request
及 observer，并以 cleanup 调用和 directory 所有索引为 oracle；内部清理实现重排不会改变判据。

### Checks

所有命令仍在本 Task 工作树执行，证据位于 `../evidence/`。

- RED：仅新增第一条组合测试、尚未改产品时，
  `RejectedRepeatedLoginStillLetsDisconnectObserverCleanupTheSelectedSession` 在
  `Assert.Single(fixture.Cleanup.Calls)` 失败，实际为空；1 Failed、exit 1。证据
  `task02-round2-red.log/.trx`。
- GREEN：同一精确过滤 1/1 Passed；证据 `task02-round2-green.log/.trx`。
- Application focused 过滤 GameLogin、`UserServiceTests`、`SelectCharacterLifecycleTests`：54/54 Passed，
  包含普通匿名/已认证清理、原断线 gate 顺序、cleanup failure、QR1-F1 及 replacement 四向索引保护；
  证据 `task02-round2-application-focused.log/.trx`。
- 真实 TouchSocket GameLogin focused：2/2 Passed，继续证明成功链和无效票据 EOF；证据
  `task02-round2-networking-focused.log/.trx`。
- 设置真实 `PVF_ARCHIVE_PATH` 后的最终 Release 全量：Application 1339/1339、Protocol 714/714、
  Networking 59/59，均 0 failed、0 skipped；证据 `task02-round2-application-release.log/.trx`、
  `task02-round2-protocol-release.log/.trx`、`task02-round2-networking-release.log/.trx`。
- 已解析本轮七份 TRX 的每个 `UnitTestResult`：RED 精确为目标用例一条 Failed；其余依次为
  1、54、2、1339、714、59 条 Passed，无非预期 outcome。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning / 0 error；证据
  `task02-round2-solution-build.log`。`git diff --check` 无输出、exit 0。

### Remaining

`QR1-F1` 的执行修复与直接回归已完成，等待 controller 安排新的独立 quick review。本轮未写
review/verdict、未改 registry、未提交 Git；Round 1 对 TASK-04 真实跨端故障验证的证据限制不变。

## Independent Review Round 2

- Reviewed execution: `Execution Round 2` 及其累积 TASK-02 owned outcome；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2 与 approved Plan。依 `reviewing-task-units-quick` 执行独立局部复审，重点重审 QR1-F1 及修复直接影响，不裁定 Spec 终态或后续 Task 验收。
- Reviewed identity: HEAD `e567c803b99260786aaddb0d2d4612dc3334b6f4`；按既定算法独立重算当前 25 个 owned 产品/测试文件 manifest 为 `22FB7A44B7DFA402A411C557E1328D87E9D7FA430F26CD8AA435FF0257F507E4`，与 Round 2 一致。两个修复文件的内容指纹及最后修改时间亦核对；本轮只追加此 review，未改产品、registry、其他 Task 或提交 Git。

### Findings and QR1-F1 Disposition

未发现本 Task 范围内新的明显阻塞缺陷；QR1-F1 已由当前修复闭合。

重复 LOGIN 拒绝仍先在 handler 撤销 OnlineUser / active 映射，不等待异步 transport close，不发送新的成功链。随后 observer 不再因 OnlineUser 已消失直接放弃清理，而从 selected directory 的 connection 映射取得不可变会话，获得该账号 lifecycle exclusive gate 后重新比较精确会话，调用既有完整 `ISelectedSessionCleanupDomainService` 和清理通知投递。正常 online 路径保留 online-user gate → lifecycle gate 顺序。

独立核对实际 `SelectedSessionCleanupService`：它不要求 OnlineUser 仍存在，按精确 session 处理位置保存、expert store、presence、PvP、inventory lease、positions、runtime、active、selected directory，并仅在角色已无 selected owner 时清容器/冷却；异常补偿仍尝试后续清理。故新增路径不是只删除目录项。目录删除与 lifecycle gate 内的二次检查使后到的断线清理不会再次清理同一已释放会话；等待期间发生 replacement 时旧 observer no-op。新增 replacement 回归检查 account / character / wire / connection 查询均保留新会话。

### Independent Evidence and Checks

- 独立解析七份 `../evidence/task02-round2-*.trx` 的每个 `UnitTestResult`：RED 精确为重复 LOGIN 组合用例 1 Failed；GREEN 1 Passed；Application focused 54 Passed；Networking focused 2 Passed；最终 Release 为 Application 1339、Protocol 714、Networking 59 条 Passed，无失败/跳过。RED 失败点为 cleanup 调用集合为空，符合 QR1-F1。最终执行日志与 TRX 一致，solution build 为 0 warning / 0 error。
- 修复产品最后修改为 13:24:56，回归测试最后修改为 13:27:12（+08:00）；最终三项目测试开始于 13:28:08、13:28:32、13:28:46，证据晚于当前修复。
- 评审于 13:36:42—13:36:46（+08:00）启动独立 Release `--no-build` 定向复跑，设置同一真实 `PVF_ARCHIVE_PATH`：Application filter `GameLogin|UserServiceTests|SelectCharacterLifecycleTests|ReturnToSelection|SessionGenerationOwnershipTests` 为 64/64；Networking `GameLoginIntegrationTests` 为 2/2；Protocol `GameLoginCodecTests|CommandRegistrationTests` 为 16/16。各命令 exit 0，逐项 TRX 均 Passed，结果位于 `C:\Users\Gloria\AppData\Local\Temp\task02-review-round2-14b0ea3aa5aa4aaea357ca572173c809\reviewer-application.trx` / `reviewer-networking.trx` / `reviewer-protocol.trx`。
- 抽查累积票据兑换边界：生产 Game 不再走自动建号入口；命令仍为 Transactional + OnlineUser Exclusive；消费继承同事务原子条件更新；身份 participant 在唯一可失败的持久提交之后忽略 cancellation 并重核 connection / generation / anonymous ownership。handler 在成功投递前重查身份，失败撤销连接身份，不使提交后的票据重新可用。
- 逐帧真实 socket 回归继续验证 LOGIN CMD 后六通知次序，以及普通无效票据收到 EOF；后者支持异步关闭接缝没有 receive callback 自等待。codec 仅重命名第二 dstring 的业务含义，未修改既有编码和尾部兼容。固定拒绝日志类别不包含凭据、ticket 或完整消息 body。`git diff --check` 无输出、exit 0。

### Evidence Limits

新增重复 LOGIN 组合回归使用真实 handler / observer / selected directory，但 cleanup 是记录调用并释放目录的测试替身；它直接证明清理接缝被调用一次，而非所有真实资源的 socket 端到端释放。实际完整清理代码及既有 `SessionGenerationOwnershipTests` 的资源 exact-owner / 异常后续清理 / 位置保存 / 冷却回归共同支持本次局部判断。未把一次 observer 调用的断言说成重复 callback 的实测；重复清理由锁内精确重查与目录释放的代码路径支持。

Round 1 已明确的真实 SQLite commit 故障、提交后真实 socket 故障及 HTTP→Channel→Game 跨端验收仍由 TASK-04 承担，当前通过不宣称 AC-009 / AC-018 全部完成。没有为此扩大本次修复范围。

Verdict: `passed`
