# TASK-04 — HTTP→Channel Info→Game 的服务端闭环验证

## Task Unit Contract

- Revision: 1
- Outcome and references: 建立真实 Host、HTTP、Channel/Game socket 的完整配置投影与票据登录证据，覆盖 RQ-009—RQ-012、RQ-016，AC-009、AC-011、AC-017—AC-018；Plan CK-001、T-007。
- Owned boundary: 主要修改 Host/Networking/Protocol 集成测试与共享 test harness；只可为闭环暴露出的同规格局部缺陷修改对应生产代码并明确记录。不得修改 WPF 或用户文档；不得提交 Git commit。
- Required checks: 从 HTTP JSON 取得非 loopback 广告 Channel tuple，经测试路由完成真实 Channel 握手并实际解码 Channel Info，再使用响应 tuple 连真实 Game socket兑换同一票据；三类 host 使用不同文档值，不能直接读取 Game listener 作为下一跳。逐帧验证登录 CMD、六通知、第三类 endpoint、AccountId 后续读取，以及拒绝/提交后故障路径。运行 focused test 后运行 Host、Networking、Protocol Release tests。
- Dependencies: TASK-02 的 Game 票据链和 TASK-03 的 HTTP/endpoint 链均已接受。

## Execution Round 1 — 2026-09-10

### Changed

- 新增 `Server/tests/Dfo.Server.Host.Tests/LauncherAuthenticationEndToEndTests.cs` 与
  `EndToEndHostHarness.cs`。测试拉起已编译的真实 Host 进程并走真实 Kestrel、SQLite、Channel/Game
  TouchSocket：HTTP 注册/登录只从 JSON 读取 `203.0.113.10:37101`，交给测试侧私有路由连接本机临时
  Channel listener；完成 `0x0B`、`0x09`、`0x11` 握手并实际 zlib/AES 解密 `0x12` Channel Info，
  再只以其中解出的 `198.51.100.24:37102` 查测试路由、连接临时 Game listener 并兑换同一原始票据。
  测试逐帧断言 initial NOTI、LOGIN CMD、`00AD/01C7/00B7/00B7/0289/01A1` 六通知，逐值读取第三类
  `192.0.2.20` 与三个独立端口；随后发送真实 GET_USERINFO 并收到 roster、hidden info、honour，证明
  后续消费者能从该连接读取已发布 AccountId。Harness 不向用例公开实际 Game listener port，不能把
  bind 配置当下一跳。
- 闭环红灯暴露同规格局部生产缺陷：Game connection observer 以实际 listener port 查询频道，但
  `ChannelEntryPolicy` 原来只与 Channel Info 的公共广告端口比较；两者按规格分离时得到 ChannelId 0，
  首条 Game NOTI 无法发送。新增独立 `ChannelListenerOptions`，从 `Networking:GamePorts` 保存按频道顺序
  的进程内监听真相；entry policy 先按 listener 索引映射广告 catalog 的频道身份，不改写
  `ChannelServerInfo.Port` 的广告语义。仅 `LauncherAuth:Enabled=false` 的旧部署保留按广告端口匹配的兼容
  fallback。
- HTTP enabled 的公共端点构造新增 fail-fast：`Channel:Channels` 与 `Networking:GamePorts` 必须数量
  一致，监听端口不得重复，完整公共 Game tuple 不得重复。这样 bootstrap 不能在某个 listener 无法
  唯一映射频道时仍进入 ready。`PublicEndpointConfigurationTests` 与 `ChannelAdmissionTests` 分别覆盖
  数量/重复拒绝、广告与 bind 分离、广告端口不被当成本地 listener，以及 disabled legacy fallback。
- `GameLoginIntegrationTests` 保留普通无效票据的真实 EOF/不消费/第二连接可兑换证据，并新增一次
  postcommit 真实 socket 故障：测试装饰实际 `IGameMessageSender`，只令第一条 `GameLoginResponse`
  投递失败。首连接收到 EOF、OnlineUser identity 被清理且 SQLite `consumed_at` 已提交；第二连接重放
  同票据仍收到 EOF，消费行数保持 1。未重复扩展 TASK-02 已接受的 precommit cancel/commit-fail 范围。

本轮 9 个 owned 产品/测试文件，按路径排序，以每行 `path<TAB>SHA256`、UTF-8、LF 分隔且无末尾 LF
计算的 manifest SHA256 为
`6DB0C9E7EDB2D847D937B540E9C3DBD53397172B9285C08CE9775796B42131E5`。关键内容 SHA256：

```text
Server/src/Dfo.Server.Application/Channel/ChannelEntryPolicy.cs  5921112C57FBC2585E493652E0C788FF3FF4F6605E16413300F8702E826B68A9
Server/src/Dfo.Server.Infrastructure/Configuration/PublicEndpointSource.cs  DF27EA1ADF63E2093B524F3418A541C49D98A3B8B6A88107886BEB27D6B1FFA0
Server/tests/Dfo.Server.Host.Tests/EndToEndHostHarness.cs  EC96F18A8091C411343E766B99209E8B1971B4334E909B9F36904D4D4317C544
Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs  66D3290F18E8C2A810C1F1E570F1A47DD1C32230DC8BD23A8B4ECE2A1655064C
```

### Why

CK-001 要求公共广告值与 bind 值分离，并要求客户端选择链的数据来源可审计。把三类公共 host 设为
互不相同的文档 IPv4、把前两跳的公共端口也与本机临时 listener 分开，能够让任何“直接使用已知
Game port”或“把 bind 当 advertisement”的捷径在对应 hop 失败；路由字典只存在于测试侧，因此结果
只证明配置投影和服务端协议链闭合，不冒充公网、NAT 或防火墙可达性证明。

闭环首次到达 Game listener 后没有首帧，不是网络偶发等待：Host 日志与源码共同定位到 listener port
和 advertised port 的事实混用。按相同索引保存独立 listener 真相是最小修复；启用 HTTP 时同时检查
一一映射和歧义，避免把运行时 ChannelId 0 延迟到客户端连接时才暴露。disabled fallback 维持旧部署
行为，但不会削弱新 Launcher 路径的 fail-closed 边界。

成功 CMD 只能在票据消费提交和身份发布后发送，因此在 `GameLoginResponse` 投递点故障能精确观察
postcommit 语义：连接/identity 必须清理，而 durable ticket 不能恢复。第二真实连接的重放拒绝和数据库
消费计数共同证明这不是仅检查异常类型的单元测试。

### Checks

证据均位于 `../evidence/`，从本 Task 工作树生成：

- TDD RED：仅加入闭环用例入口、尚无专用 harness 时，Host 测试编译以 CS0103
  `EndToEndHostHarness` 不存在失败；构建失败未生成 TRX，证据 `task04-round1-red.log`。
- GREEN：最终同一闭环用例 1/1 Passed；`task04-round1-green.log/.trx`。
- focused：Host 闭环 1/1、Networking Game login（成功、普通 reject、postcommit delivery fault）
  3/3、Infrastructure 公共端点 17/17、Application channel mapping/legacy 9/9，分别见
  `task04-round1-focused-host.log/.trx`、`task04-round1-focused-networking.log/.trx`、
  `task04-round1-focused-infrastructure.log/.trx`、`task04-round1-focused-application.log/.trx`。
- 合同指定 Release 全量：Host 17/17、Networking 60/60、Protocol 在设置真实
  `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后 714/714，均 0 failed / 0 skipped；
  `task04-round1-host-release.log/.trx`、`task04-round1-networking-release.log/.trx`、
  `task04-round1-protocol-release.log/.trx`。
- 因本轮修改 Application/Infrastructure，额外全量回归：Application 1340/1340；Infrastructure
  301 Passed + 2 NotExecuted，后两条正是设置真实 archive 时互斥的“missing archive”条件用例；本轮
  public endpoint focused 17/17 无跳过。证据 `task04-round1-application-release.log/.trx`、
  `task04-round1-infrastructure-release.log/.trx`。
- 已逐个解析上述最终 10 份 TRX 的每个 `UnitTestResult`：除 Infrastructure 两条预期
  `NotExecuted` 外全部 `Passed`，无 Failed 或未分类 outcome。
- `dotnet build Server/86JP.Server.slnx -c Release` 成功，0 warning / 0 error；
  `task04-round1-solution-build.log`。`git diff --check` 无输出、exit 0。

### Remaining

TASK-04 执行结果等待 controller 安排独立 quick review。本轮自动化证明受控本机路由下的 Host 配置
投影、协议 hop 与事务后故障语义，不宣称三个文档 IPv4 在公网实际可达；没有修改 WPF、用户文档、
registry、既有 review 或其他 Task，没有创建 Git commit。

## Independent Review Round 1

- Reviewed execution: TASK-04 `Execution Round 1`，Task Unit Contract revision 1；核对 TaskContract、批准 spec 的 AC-009 / AC-011 / AC-017 / AC-018、plan CK-001 / T-007，以及 TASK-02 / TASK-03 的已接受边界。
- Scope: 本轮 9 个 owned 产品/测试文件及所引用证据。独立重算路径排序的 manifest SHA256 为 `6DB0C9E7EDB2D847D937B540E9C3DBD53397172B9285C08CE9775796B42131E5`，与执行报告一致。
- Review method: `reviewing-task-units-quick` 与 `reviewing-code`；只写本评审，不更改产品、registry、其他 Task 或 Git 提交。

### QR1-F1 — [P2] disabled 的索引匹配覆盖了原有按端口识别频道的语义

`Server/src/Dfo.Server.Application/Channel/ChannelEntryPolicy.cs:24` 无条件先按 `Networking:GamePorts` 的索引匹配 catalog，再在未命中时执行 legacy fallback。`ApplicationModule.cs:69` 在 `LauncherAuth:Enabled=false` 时也注入非空 listener 列表，因此 fallback 并不能保持旧部署行为。旧实现按 `channel.Port == listenerPort` 查找，并不要求两个配置数组顺序相同。

独立复现用当前 Release DLL 的真实 policy，仅 catalog 接缝用测试替身：catalog 顺序为 `ch.100:10161, ch.11:10011`，listener 顺序为 `[10011, 10161]`，`AllowAdvertisedPortFallback=true`。原本正确且无歧义的 disabled 配置得到：

```text
disabled listener=10011, expectedChannel=11, actualChannel=100, kind=LevelRestricted, level1Refused=True
disabled listener=10161, expectedChannel=100, actualChannel=11, kind=General, level1Refused=False
```

这会把普通频道变成等级限制/临时位置频道，同时绕过实际限制频道的等级门，违反本 Task 明确保持 disabled legacy 行为的边界。当前 `ChannelAdmissionTests` 的 legacy helper 注入空 listener 列表，无法覆盖真实 DI 的冲突分支。

最小修正：disabled 路径保持原有按广告端口匹配的优先级/语义；enabled 才使用严格一一对应的 listener 索引真相。补充非空 listener、两个列表不同序及限制频道分类的回归，并保留 enabled 广告/bind 分离的现有断言。独立复现项目位于 `C:\Users\Gloria\AppData\Local\Temp\task04-independent-review\proof.csproj`，`dotnet run -c Release` exit 0；它证明 policy 行为，不冒充 socket 实测。

### QR1-F2 — [P2] AC-009 的真实 socket 提交前故障验收没有落地

spec `AC-009` 明确要求通过真实 Game socket 分别令票据消费提交失败、提交前取消，验证无成功 CMD/六通知、始终匿名并关闭/拒绝、事务未提交/票据未消费，且第二连接还能兑换一次。本 Task 合同显式承接 AC-009；TASK-02 `Independent Review Round 2 / Evidence Limits` 明确保留真实 SQLite commit 故障及跨端故障给 TASK-04，并非已豁免或已完成。

本轮 `GameLoginIntegrationTests` 实际只有成功、普通 invalid ticket、postcommit delivery failure 三条真实 socket 用例。旧 Application 测试的模拟 durable participant / SQLite 命令层取消证明事务排序，但不覆盖 receive callback、真实连接关闭和 SQLite 持久状态共同发生时的提交前故障。执行报告称“不重复扩展 TASK-02 已接受的 precommit cancel/commit-fail 范围”与前述接受限制矛盾，因此这是缺失的要求证据，不是已证明产品在该路径出错。

最小修正：按 AC-009 增加两个真实 Game socket 故障用例，保留真实生产事务与 SQLite 消费链，在真实 SQLite 消费 UPDATE 已执行但未提交时，于 commit path 分别注入普通提交异常和提交前 cancellation / OCE；断言上述帧、身份、EOF/关闭、实际 SQLite rollback、数据库及第二连接兑换结果。不要求底层 `SqliteTransaction.CommitAsync` 自身制造不可控错误，但不能以纯内存 durable participant 代替真实未提交 SQLite 消费。更新本 Task 的证据说明，并复跑合同要求的 Release 回归。

### Evidence and non-blocking limits

- 逐项解析本轮 10 份 TRX：GREEN 1、focused Host 1 / Networking 3 / Infrastructure 17 / Application 9；全量 Host 17、Networking 60、Protocol 714、Application 1340，均逐条 Passed。Infrastructure 全量为 301 Passed + 2 NotExecuted，后两条确为 missing-archive 条件测试；不是本轮公共端点用例跳过。最终 Host / Networking / Protocol 运行开始于 15:19:19 / 15:20:05 / 15:20:57（+08:00），晚于最后产品/测试修改 15:17:31。
- 独立 Release `--no-build` 定向复跑 Host `LauncherAuthenticationEndToEndTests` 1/1、Networking `GameLoginIntegrationTests` 3/3、Application `ChannelAdmissionTests` 9/9，全部 exit 0，逐条 TRX Passed。结果在上述临时目录 `reviewer-host.trx` / `reviewer-networking.trx` / `reviewer-application.trx`。现有绿灯不覆盖两项 finding。
- 已核对 HTTP JSON 的 Channel tuple 被真正传入测试路由，真实 `0x0B/0x09/0x11` 请求和 `0x12` 解密后所得 Game tuple 再选下一跳；测试未直接拿实际 Game listener port 连接。三个广告 host 与跟进端口各自断言；LOGIN CMD + 六通知逐帧顺序成立。GET_USERINFO 证明连接可被后续消费者使用；目前空账号断言仅比较消息类型，不是多账号数据归属隔离的独立证明，不另列本轮阻塞。
- postcommit 测试在生产 sender 接缝注入第一次 LOGIN CMD 投递异常，真实 socket 收 EOF，identity 消失，真实 SQLite 消费行为 1；第二连接重放仍被拒绝。它证明投递故障后的清理，不冒充真实物理网络故障，也不能替代 AC-009。
- enabled 的端点数量、重复 listener 与重复公共 tuple 校验成立，Application 项目没有新增 Networking 引用；此次局部建模方向可行，但需修正 QR1-F1。受控路由证据不证明公网/NAT/防火墙可达。`git diff --check` 无输出。

Verdict: `failed`

## Execution Round 2 — 2026-09-10

### Changed

- 修复 QR1-F1：`ChannelEntryPolicy` 现在把两种部署模式明确分支。禁用 LauncherAuth 时只按
  catalog 的 advertised port 保持历史频道身份，不再让非空且顺序不同的 `GamePorts` 抢先按索引
  改写频道；启用 LauncherAuth 时才使用启动期已校验的一一索引 listener mapping，并继续拒绝把
  advertised port 当 bind port。`ChannelAdmissionTests` 新增 catalog 为限制/普通、GamePorts 为
  普通/限制的故意逆序回归，逐项断言 ChannelId、频道种类与等级拒绝，防止两个频道串换。
- 补齐 QR1-F2 / AC-009 的两个真实 Game socket + 真实 SQLite 用例：普通 commit-path failure 与
  precommit cancellation 各一条独立 Fact。测试只装饰 `IGameLoginAuthenticationDomainService`；
  inner 先沿生产链完成真实 SQLite `TryConsumeTicketAsync` 并准备身份 participant，返回真实
  `AccountId` 后，装饰器才向同一 ambient transaction 加入一次性 test participant，分别在
  `PrepareAsync` 抛 `InvalidOperationException` 或 `OperationCanceledException`。没有增加生产 fault
  seam，也没有用纯内存 ticket participant 替代 SQLite。
- 两条故障用例都在首连接发 LOGIN 前确认匿名，并由 fault participant 在抛错瞬间再次记录
  `OnlineUserState.Connected`、`AccountId=null`；首连接随后未收到任何完整 LOGIN CMD 或六 NOTI 即
  EOF/清理，实际数据库 `consumed_at` 行数为 0。一次性 fault 已解除后，第二个真实 socket 使用同一
  票据收到 LOGIN CMD 与按序六 NOTI，身份发布为 Authenticated，关闭后数据库消费行数为 1。

本轮只改 3 个产品/测试文件。按路径排序，以每行 `path<TAB>SHA256`、UTF-8、LF 分隔且无末尾 LF
计算的 manifest SHA256 为
`D8A26C352031AC1917E5351F82FA5857ADB6D802285B843AC1E522DEBBD060D5`：

```text
Server/src/Dfo.Server.Application/Channel/ChannelEntryPolicy.cs  49FE5053EDF6689B1923E6A333C05007D31573E46D3747915E4A7680F7D2B602
Server/tests/Dfo.Server.Application.Tests/Channel/ChannelAdmissionTests.cs  55ABA61319FDC8D63E6DC45AA625D634B94DAE8C432B516F313FDA3EEDF63B1F
Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs  318FE02E2E20A7ED38493994CD529F5D259C2526C1B989B4BB724D9F5704F2E7
```

### Why

disabled 兼容标志代表的是旧配置的完整识别语义，而不是索引映射失败后的次级兜底；先尝试索引会在
两个合法配置列表顺序不同时静默赋予错误频道规则。因此两种模式必须互斥：disabled 以广告端口为
真相，enabled 才以经过 fail-fast 校验的 listener 顺序为真相。

AC-009 需要同时观察传输、事务内身份和持久化结果。把 fault 放在真实认证 inner 返回之后，能证明
SQLite UPDATE 已在 ambient transaction 中命中，且身份 participant 已 enlist；再在提交阶段的
Prepare 抛错，恰好位于 SQLite durable commit 与身份发布之前。这样真实事务倒序 rollback 后的
首 socket EOF、匿名快照、数据库未消费与同票据第二次成功共同构成一个跨边界结果，不依赖内部调用
次数或模拟票据存储。

### Checks

证据均位于 `../evidence/`，汇总见 `task04-round2-verification.yaml`：

- F1 RED：故意逆序用例 1/1 Failed，明确为 expected ChannelId 11、actual 100；
  `task04-round2-f1-red.log/.trx`。修复后全部 ChannelAdmissionTests 10/10 Passed；
  `task04-round2-f1-green.log/.trx`。
- F2 RED：故意尚未安装测试装饰器时两条用例 2/2 Failed，均为期望 EOF 但实际收到成功帧；
  `task04-round2-f2-red.log/.trx`。只加入测试 DI 接线后 2/2 Passed；
  `task04-round2-f2-green.log/.trx`。
- focused：Application Channel 10/10、Networking GameLogin 5/5、Host 完整闭环 1/1；分别见
  `task04-round2-focused-application.log/.trx`、`task04-round2-focused-networking.log/.trx`、
  `task04-round2-focused-host.log/.trx`。
- 合同指定 Release 全量：Host 17/17、Networking 62/62、Protocol 在真实
  `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 下 714/714，均 0 failed / 0
  skipped；见 `task04-round2-host-release.log/.trx`、`task04-round2-networking-release.log/.trx`、
  `task04-round2-protocol-release.log/.trx`。
- 因本轮修改 Application 生产策略，额外以同一真实 PVF 运行 Application Release 全量
  1341/1341，0 failed / 0 skipped；`task04-round2-application-release.log/.trx`。
- 已逐个解析本轮 11 份 TRX 的每个 `UnitTestResult`：3 个 RED 结果为预期 Failed；所有 GREEN、
  focused 与 Release 结果全部 Passed，无 skipped 或未分类 outcome。
- `dotnet build Server/86JP.Server.slnx -c Release` 成功，0 warning / 0 error；
  `task04-round2-solution-build.log`。`git diff --check` 无输出、exit 0。

### Remaining

TASK-04 Execution Round 2 已修复 QR1-F1/F2，等待 controller 安排新的独立 quick review。本轮没有
修改 WPF、用户文档、registry、既有 review 或其他 Task，没有创建 Git commit。

## Independent Review Round 2

- Reviewed execution: `Execution Round 2` 及 TASK-04 累积 owned outcome；Task Unit Contract revision 1、root TaskContract revision 1、approved Spec revision 2、approved Plan CK-001 / T-007，重点核对 QR1-F1 / QR1-F2 和 AC-009 / AC-011 / AC-017 / AC-018 的本 Task 边界。依 `reviewing-task-units-quick` 做新独立局部复审，不裁定其他 Task 或整个 Spec。
- Reviewed identity: 独立重算 Round 2 所列三个产品/测试文件的各自 SHA256 与 manifest，结果 `D8A26C352031AC1917E5351F82FA5857ADB6D802285B843AC1E522DEBBD060D5` 与执行记录及 `../evidence/task04-round2-verification.yaml` 一致。另核对累积 listener/options/config、Host harness 与真实 Game fault 路径；未改产品、registry、其他 Task 或提交 Git，只追加此 review。

### Findings and Prior Finding Disposition

未发现本 Task 范围内新的明显阻塞缺陷；QR1-F1 / QR1-F2 均已闭合。

- QR1-F1：当前 policy 在 `AllowAdvertisedPortFallback=true` 时只按 catalog 的 advertised port 查找并直接结束该分支，不再尝试 listener 索引。真实 DI 在 LauncherAuth disabled 时设置该值为 true，故非空、顺序不同的 GamePorts 不再改写历史频道身份。故意逆序回归逐项覆盖普通/限制频道的 ChannelId、Kind 与低等级拒绝；enabled 分支仍仅匹配 GamePorts 索引，不把广告端口当本地 bind。配置、catalog 与 listener 均沿各自 `GetChildren()` 顺序投影；启动校验拒绝数量不等、重复 listener、重复公共 tuple，不在 policy 中重新按端口排序串换频道。
- QR1-F2：两个独立 Fact 使用真实 TouchSocket、真实生产 CommandBus/DomainTransaction、真实 `SqliteAuthenticationPersistence` 和 SQLite 文件。测试 decorator 等待真实 inner `RedeemTicketAsync` 返回非空 AccountId 后才向当前 ambient transaction enlist 一次性 fault；该 inner 必须先完成真实 `UPDATE ... RETURNING account_id` 并 `PrepareAuthentication`，不是返回预设账号或用内存票据替代。数据库命令绑定同一 pending `SqliteTransaction`，没有在 inner 返回前提交。
- 两种 fault 分别在 coordinator 的 Prepare 阶段抛普通异常或 OCE，故发生于唯一可失败 SQLite commit 和内存身份 Commit 之前；抛错点记录 OnlineUser 为 Connected、AccountId=null。coordinator 逆序、不可取消 rollback 后，首连接没有任何完整后续帧便 EOF，随后 transport/OnlineUser 清空，独立 SQLite 查询 consumed=0。第二真实连接沿原生产链兑换同一票据，逐值收到 LOGIN CMD、按序六 NOTI，发布 Authenticated 身份，随后 consumed=1。故当前证据补齐了先前缺失的传输/身份/真实 rollback 组合，而不是只验证抛异常类型。没有新增生产 fault seam。

### Independent Evidence and Checks

- 独立逐项解析本轮 11 份 TRX 的每个 `UnitTestResult`：F1 RED 1 Failed（expected 11 / actual 100），F2 RED 2 Failed（预期 EOF、实际无异常返回成功帧）；F1/F2 GREEN 分别 10、2 Passed；focused Application 10、Networking 5、Host 1 Passed；最终 Release Host 17、Networking 62、Protocol 714、Application 1341 Passed，无 skipped 或未分类 outcome。日志、执行报告及 verification YAML 的数字与注入层次一致。
- 新鲜度：三个修复文件最晚修改 15:41:15（+08:00）；最终 Host / Networking / Protocol / Application 分别于 15:42:55 / 15:43:38 / 15:43:55 / 15:44:34 开始，均晚于修改；最终 solution build 日志为零警告/错误。评审 `git diff --check` 无输出、exit 0。
- 评审于 15:50:46—15:50:47（+08:00）独立启动 Release `--no-build` 定向复跑：`ChannelAdmissionTests` 10/10、`GameLoginIntegrationTests` 5/5、`LauncherAuthenticationEndToEndTests` 1/1、`PublicEndpointConfigurationTests` 17/17，全部 exit 0；逐项 TRX 均 Passed。临时结果目录为 `C:\Users\Gloria\AppData\Local\Temp\task04-review-round2-eb85c69bb96f46519bcf26ee95b4e867\`，文件为 `reviewer-application.trx` / `reviewer-networking.trx` / `reviewer-host.trx` / `reviewer-infrastructure.trx`。
- 抽查完整广告链：HTTP JSON 的非 loopback Channel tuple 确实驱动测试侧路由；随后真实 Channel 请求、AES/zlib 解码出的 Channel Info Game tuple 再驱动下一跳，未直接拿已知 Game listener port 连接。LOGIN 成功响应与六通知按序读取，第三类 host/三个端口分别断言，GET_USERINFO 后续消费者继续成功。三个文档 host 的差异没有被本地 bind 覆盖。
- 抽查 postcommit fault：测试仅装饰 production sender，第一次 LOGIN response 投递抛错；真实首连接 EOF、identity 清理、SQLite consumed=1，第二真实连接重放仍 EOF，消费数不恢复。该测试连同新增两项 precommit 回归在本次 5/5 中通过。

### Evidence Limits

普通异常与 OCE 是测试 participant 在 commit-path Prepare 注入，不是实际令底层 `SqliteTransaction.CommitAsync` 或操作系统网络出错；这符合 QR1-F2 指定的最小纠正边界，且真实 SQLite pending UPDATE / rollback 保留。匿名状态由发包前、fault 时刻快照及生产提交顺序共同支持，不宣称连续时序采样。EOF 检查直接证明无完整成功帧，不冒充逐字节零流量统计。广告链仅证明受控本机路由的配置投影与协议连续性，不证明公网可达；空账号 GET_USERINFO 仍不单独证明多账号数据隔离。本轮通过限于此 Task 的修复及局部检查。

Verdict: `passed`
