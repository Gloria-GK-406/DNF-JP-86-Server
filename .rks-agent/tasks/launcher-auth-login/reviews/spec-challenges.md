# Spec adversarial challenges

- Target path: `.rks-agent/tasks/launcher-auth-login/spec.md`
- Target revision: SHA-256 `5CAC0B29FE1B4C5033B3D2FB7D23828F9DE5C56C821A0C04B202460C48A00E30`
- Governing sources: `.rks-agent/tasks/launcher-auth-login/TaskContract.md` revision 1，尤其 Goal、Constraints、SC-3/SC-4/SC-6/SC-7 与旧账号密码语义 Assumption；仓库 `AGENTS.md` 的应用分层、领域事务与协议证据约定；`C:/Users/Gloria/.agents/skills/generating-adversarial-challenges/SKILL.md`。
- Attack scope: WPF → HTTP 注册/认证 → 一次性票据 → A21 参数/频道发现 → Game 身份绑定的可信边界与验收充分性；独立检查现有源码、协议记录与测试。仅进行只读核验，没有运行真实客户端、修改数据库或修改目标规格。本记录不作 verdict，后续修订关闭挑战时保留本记录。

## CH-1: 32-hex 形状不能把历史自由填写的值升级为账号所有权证明

- Risk: high-risk
- Failure and impact: RQ-007 将任何形状合法且能匹配输入密码的历史值直接作为迁移凭据，但旧写入路径从未验证过它属于账号所有者。可信场景是调试/固定启动器为多个实际使用者的账号写入同一个已知默认密码摘要，或未认证建号者填写自己选择的摘要；升级后知道该默认密码的人即可取得这些账号的 Game 票据，并把旧值永久升级为自己的现代认证凭据。形状检查与固定时间比较均不能弥补来源缺口，且 AC-007 会把这种迁移当作成功。受损的是合同要求的安全旧账号初始化和正确身份绑定；迁移一旦执行并发出票据，后续普通测试或回滚现代哈希不能撤销已经获得的账号访问。
- Evidence: `TaskContract.md` Assumptions 明确当前账号没有可信的已验证密码语义。`spec.md:132` RQ-007 与 `spec.md:152` AC-007 只要求形状和摘要匹配。`Server/src/Dfo.Server.Application/Users/UserDomainServices.cs` 的 `AccountDomainService.LoginAsync` 直接调用 `GetOrCreateAsync`；`Server/src/Dfo.Server.Application/Accounts/AccountProvisioning.cs` 的 `GetOrCreateAsync` 只校验长度然后原样插入；`Server/src/Dfo.Server.Persistence/Accounts/AccountTables.cs` 的 `InsertIfAbsentAsync` 持久化来值且已有账号不校验密码。只读查看 `E:/Project/DNF/DNFClient-A21-debug/本地游戏S4.bat:2` 与 protocol 客户端同名批处理，可见固定账号及固定 32-hex 值。可复现探针：在隔离库为两个账号预置同一已知密码的合法 MD5，验证规格是否允许同一默认密码自动迁移并取得两者身份；这验证的是来源策略缺口，不是 MD5 碰撞。
- Minimum correction: 明确允许自动迁移的可信来源/显式启用边界；对无法证明来源的旧行默认拒绝自动迁移，并给出保持 AccountId 的受控初始化办法。验收区分“格式合法但来源不可信”与“明确允许迁移”的账号，不能仅按 32-hex 放行。

## CH-2: 消费票据的数据库事务与在线身份发布之间缺少失败不变量

- Risk: high-risk
- Failure and impact: RQ-013 要求票据消费有事务，RQ-012 又要求沿用现有身份绑定链，但现有链在命令返回前立即修改内存 OnlineUser，而 SQLite 提交在命令返回后发生。若实现直接用票据兑换替换原仓储调用，数据库提交失败或取消时，票据可以回滚成未消费，而在线身份曾经已经对查询方可见；随后另一连接可重新兑换同一票据。网络异常最终断线可能清除状态，但不能替代“未提交兑换不得形成可使用身份”的要求，也没有证据保证所有观察者都等到断线清理。普通并发成功测试只证明数据库竞争，不能证明该跨存储边界。风险直接落在 SC-4 的一次性认证与 SC-7 的会话身份一致性上，需要规格现在明确成功发布的边界。
- Evidence: `spec.md:134-138` RQ-009/RQ-012/RQ-013 与 `spec.md:153` AC-008 没有提交失败、取消或发布失败条件。`Server/src/Dfo.Server.Application/Users/UserDomainServices.cs:29-34` 在领域调用中立即 `state.Authenticate`；`Server/src/Dfo.Server.Application/Users/OnlineStateRegistry.cs:36-41` 只持普通锁，`OnlineUser.Authenticate` 直接赋值身份/状态/代次，没有事务登记；`Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:34-36` 先执行命令后提交。`Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` 的 Game 异常处理记录并重新抛出，不能证明内存修改随 SQLite 回滚。可复现探针：阻挡/注入票据事务 Commit 失败，在失败前后读取身份并派发受认证保护的操作，再于另一连接兑换该票据；另覆盖连接消失导致身份发布失败。
- Minimum correction: 补充外部不变量及故障验收：任何兑换未成功提交都不得使连接获得可用身份；消费已提交但身份发布失败必须保持拒绝，不能恢复成可重放票据；异常/取消后无成功帧或可继续使用的认证状态。具体事务参与、发布顺序或补偿机制留给 Plan。

## CH-3: HTTP 返回可访问频道入口，不足以保证 A21 随后能到达同一认证后端

- Risk: high-risk
- Failure and impact: 新 HTTP 配置可以正确广告远程频道 IPv4，现有 `Channel:PublicServerIp` 和 `Game:EndpointHost` 却仍保留各自的 `127.0.0.1` 默认值。启动器得到有效票据并成功创建 DNF 进程后，A21 从频道协议收到客户端自身的 loopback Game 地址，无法到达签发票据的服务端。AC-005 的 HTTP 响应投影、AC-010 的参数逐槽、AC-011 直接连 Game socket、AC-014 的进程创建均可以通过；本地 loopback 联调也不会揭露这一失败。规格把公共端点严格定义为初始频道入口，却未明确后续广告链的一致性验收，容易交付一个仅在服务端本机能工作的启动器。影响合同要求的后端联动与客户端可访问公共端点；等真实远程发布才发现将需要追查多条配置和协议路径。
- Evidence: `spec.md:32`、`:106` 仅规定公共频道入口；`:156` AC-011 直接验证 HTTP 票据到 Game，`:159` AC-014 只验证创建进程。`Server/src/Dfo.Server.Host/appsettings.json:11` 的 `Channel:PublicServerIp` 与 `:21` 的 `Game:EndpointHost` 是独立 loopback 配置；`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` 的 `ChannelSourceOptions.FromConfiguration` 从前者生成频道记录；`Server/src/Dfo.Server.Protocol/Channel/Info/ChannelInfoResponseEncoder.cs:21-22` 把记录中的 Host/Port 发给客户端；`Server/src/Dfo.Server.Protocol/Game/Login/GameLoginHandler.cs:34-40` 又独立读取 Game 成功响应端点。现有 `Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs:71` 直接连接测试掌握的 Game 端口。可复现探针：只改变新公共频道配置、保留旧默认项，按 HTTP → Channel Info → 所广告 Game 地址顺序解析连接；检查得到的 Game 地址是否仍为客户端 loopback，而不是绕过频道发现直接使用测试端口。
- Minimum correction: 明确部署时初始频道入口、频道广告的 Game 端点与后续必要端点的关联/校验责任，并要求一条从 HTTP 返回入口开始、实际消费 Channel Info 后到达同一认证后端的验收。至少用不同于 loopback 的广告值验证完整投影；文档列出必须协调的服务端配置。实际客户端的凭据槽传递也应由这条链的证据确认，不能仅用手工构造 dstring 代替。
