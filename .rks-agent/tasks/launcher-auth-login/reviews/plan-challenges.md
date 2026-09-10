# Plan adversarial challenges

- Target path: `.rks-agent/tasks/launcher-auth-login/plan.md`
- Target revision: 未声明数值 revision；实读 SHA-256 `316F2F5CAE2EF011E27F462199AF78CB985A1FE878DA6C99932592AC9D69F1EA`。
- Governing sources: `TaskContract.md` revision 1；`spec.md` revision 2；`reviews/spec-challenges.md`；`reviews/spec-review.md` 的 revision 2 有界复审；`C:/Users/Gloria/.agents/skills/generating-adversarial-challenges/SKILL.md`。
- Attack scope: 独立核验密码记录与项目依赖、SQLite/OnlineState participant 提交顺序和异常清理、WebApplication 的网络生命周期、凭据传输 gate、三类广告端点和受控 socket 路由、WPF 密码作用域与单参数启动、工作顺序与验收覆盖。源码路径均相对本任务工作树。仅作只读源码/测试/项目文件及官方框架资料核验；下列探针尚未执行，不把静态风险表述为已发生事故。本记录不给 verdict，形成后保持不变。

## CH-1: 凭据读取后的延迟事务写升级可能让正常并发登录失败

- Risk: low-risk
- Failure and impact: BD-002/BD-003 与 T-002/T-003 把凭据验证、必要升级和签票放入 transactional command，但没有区分当前 SQLite 延迟事务中的“先读后写”与兑换的一条条件更新。两个正常登录先后读取账号、在密码 KDF 期间重叠，其中一个签票提交后，另一个仍持旧快照，再插入票据或升级密码可能得到 SQLite 写升级错误；不必争抢同一账号，其他游戏写事务也能制造这个窗口。于是正确凭据落入通用内部失败，而计划里的并发兑换与注册唯一性检查仍可以通过。影响 RQ-005 的正常认证和同 Host 游戏写入共存。它是可以在 T-002/T-003 内用定向测试和局部事务策略处理的问题；规格没有额外并发可用性 SLA，因此不将其提升为必须改变主架构的阻塞项。
- Evidence: `plan.md:26` 的 220,000 次 KDF、`:32` 的签票/升级同事务，以及 `:59-60` 的工作和测试范围。当前 `Server/src/Dfo.Server.Persistence/Database/SqliteDatabaseParticipant.cs:87` 使用 `BeginTransaction(deferred: true)`；`Server/src/Dfo.Server.Persistence/Database/SqliteDatabase.cs` 的 `ReadAsync` 会让账号读取加入 ambient participant；`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:102` 开启 WAL。`Server/src/Dfo.Server.Application/Commands/Pipeline/UnitOfWorkBehavior.cs:34-36` 目前只执行一次命令和提交，没有重试。微软文档明确延迟事务从读升级为写时可以失败，处理时需要重试整个事务，不能只重试失败 SQL：[Microsoft.Data.Sqlite transactions](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions#deferred-transactions)。可复现探针：在隔离库使两个真实认证命令都完成凭据读取后暂停，放行第一个完成签票提交，再放行第二个写入；分别覆盖不同账号、同一授权旧账号迁移，以及并行普通游戏写入，记录外部结果和最终凭据/票据状态。
- Minimum correction: 在 T-002/T-003 的实现检查中加入上述先读后写交错，明确如何处理 SQLite 写升级冲突及失败的外部语义。若采用重试，限定在持久提交前重做整个认证事务，且不重复返回原始票据、不把提交后的 Game 身份发布或投递放入重试范围；不要求新增全局事务重试框架。

## CH-2: endpoint filter 不能单独承担 typed JSON endpoint 的读取前传输门禁

- Risk: low-risk
- Failure and impact: BD-004 正确要求 body binding 前拒绝，末节却把 route group、endpoint filter、middleware 组合留为实现选项。若 T-005 将安全检查直接实现为注册在 typed register/login endpoint 上的 filter，JSON DTO 已在进入 filter 前绑定。合法远程明文请求可能仍返回期望的 `secure_transport_required`，业务验证调用数也为零，但密码已经被框架读取；畸形 JSON 更可能先走绑定错误而未到安全 gate。这会违反规格“不得读取或验证密码”的边界。计划已经明确更强的要求及检查，因此这是需在局部实现中排除的框架陷阱，不是计划缺失安全意图，也不必禁止 endpoint filter 的其他用途。
- Evidence: `plan.md:41` 与 `:73`，T-005 `:62`。微软对 `EndpointFilterInvocationContext` 的说明和 typed body 示例显示 filter 接收的是供 handler 使用的 Arguments，并可直接取得已构造的 DTO：[Minimal API filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0#validate-an-object-with-a-filter)。可复现探针：给真实 endpoint pipeline 设置非 loopback RemoteIpAddress 和 HTTP scheme，body 使用“第一次读取即计数/抛错”的 stream，再分别发送合法和畸形 JSON；两者都必须在 body 零读取时得到稳定传输拒绝。仅 mock commandBus 证明没有验证密码不够。
- Minimum correction: T-005 落实时把读取前 gate 固定在 JSON 绑定之前的 middleware/等效 RequestDelegate 层，或显式先 gate 后手工读 body；补上 body 零读取与畸形 JSON 的测试。endpoint filter 可以继续用于绑定后的验证，毋须限定其他 endpoint 组织方式。

## CH-3: HTTP 关闭配置必须控制 listener 生命周期，不能只停止映射路由

- Risk: low-risk
- Failure and impact: BD-004 将整个组合根改为 WebApplication 并声明 HTTP 开关，但没有说明关闭时如何避免启动默认 Web server。自然的实现若只跳过 LauncherApi 注册/Map，WebApplication 仍会启动 Kestrel；没有显式 URL 时会绑定默认端口。现有仅配置 Channel/Game 临时端口的 Host 测试或只运行游戏服务的部署因此多出一个监听器；多个实例可因默认端口冲突而整机 Host 启动失败，连既有 Channel/Game 生命周期也受影响。该风险通常能被早期 Host 集成检查廉价发现和修正，不需要否定 WebApplication 方案或增加新的生产部署要求。
- Evidence: `plan.md:39-40`、T-005 `:62`；当前 `Server/src/Dfo.Server.Host/Program.cs:17`、`:73` 是 Generic Host。`Server/tests/Dfo.Server.Host.Tests/ChannelHostStartupTests.cs:161-191` 只给 Channel/Game 和数据库分配隔离配置，没有 HTTP listener 配置。官方文档说明没有 endpoint 配置时 Kestrel 使用 `http://localhost:5000`：[Kestrel default endpoint](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0#identify-the-default-endpoint)。可复现探针：关闭 LauncherAuth HTTP，启动两个 Channel/Game 端口不同的隔离 Host，确认两者均能完成原 Channel 握手且无额外 HTTP 监听；HTTP 启用测试显式分配独占临时端口。另在 required channel script/认证初始化失败时确认 HTTP 和既有 socket 都没有遗留可用监听。
- Minimum correction: 在 T-005 确定 HTTP 关闭时的 Web server 生命周期语义，并把上述开关/端口隔离/启动失败检查随 Host 转换一并执行，不推迟到最终整仓验收；保留当前 content root 和网络模块初始化保护。
