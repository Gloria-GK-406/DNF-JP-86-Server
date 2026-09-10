---
kind: rks-review
verdict: approved
---

# 计划审查

- 日期：2026-09-10。
- 审查者：独立子代理 `plan_reviewer`；未参与计划生产或对抗问题生成。
- 依据：完整读取 `reviewing-plans/SKILL.md`、合同 revision 1、规格 revision 2、规格批准复审、计划及匹配挑战；另核验当前源码、项目结构与官方框架资料。
- 合同：`../TaskContract.md` revision 1。
- 批准规格：`../spec.md` revision 2，SHA-256 `6970C36B34E013C91563F3AEF220D3AF5633FB9912EBDDD3EDBBE9F45F4890FE`，与 `spec-review.md` 的 revision 2 批准复审一致。
- 计划：`../plan.md`，未声明数值 revision；实读 SHA-256 `316F2F5CAE2EF011E27F462199AF78CB985A1FE878DA6C99932592AC9D69F1EA`。
- 挑战：`./plan-challenges.md` 的目标摘要与实读计划一致。
- 源码基准：`e567c803b99260786aaddb0d2d4612dc3334b6f4`；下列源码路径相对任务工作树根目录。
- 证据限度：本次为计划可行性审查，未实现功能、运行故障探针或执行 WPF/游戏客户端。以下静态风险不表示已观察到运行事故。

结论：`approved`。计划覆盖批准规格的认证、迁移、票据消费与身份发布、端点发现、启动器和验收边界。三项挑战均有可信技术依据，但可在所属执行单元内廉价验证和修正，不需要实施前修订计划。以下检查随 T-002/T-003/T-004/T-005 落实，不能以本批准代替通过证据。

## 挑战裁定

### CH-1 — confirmed，low-risk，非阻塞

`SqliteDatabase.ReadAsync` 会令凭据读取进入 ambient participant；`SqliteDatabaseParticipant.CommandAsync` 创建 deferred 事务，而 `UnitOfWorkBehavior.ExecuteAsync` 只有一次执行与提交。账号读取、KDF、签票之间确有读快照升级为写事务的竞争窗口。[Microsoft.Data.Sqlite 文档](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions#deferred-transactions)确认这种升级可能失败，恢复需重试整个事务。计划没有宣称现有并发兑换测试已经解决并发认证，挑战成立。

T-002/T-003 执行时增加受控交错：两个正确凭据登录均完成读取，放行一个提交，再放行另一个写；覆盖不同账号和同一授权旧账号迁移，检查失败语义、凭据升级及票据状态。局部策略可在实现时确定；若采用重试，只覆盖持久提交前的完整认证事务，重新读取事实并验证，且最终仅返回已提交尝试的一张票据。不得仅重跑失败 SQL，也不得将 Game 的提交后发布或网络投递纳入重试。规格未承诺并发吞吐或成功率 SLA，此处不要求新增全局重试框架。

### CH-2 — confirmed，low-risk，非阻塞

[Minimal API filter 文档](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)中的 invocation context 已含供 handler 使用的 Arguments，typed DTO filter 不能单独证明绑定前拒绝。计划 BD-004 与 T-005 已明确 body binding 前 gate，因此末节的组合选项须受这一较强约束支配；这不是授权把 gate 延后。

T-005 使用 JSON 绑定之前的 middleware/等效 RequestDelegate，或先 gate 后显式读取 body。以首读可计数或抛错的 body stream，分别验证远程明文的合法、畸形 JSON 均在零读取时返回 `secure_transport_required`；只断言 command 未执行不够。可信代理头配置需保持显式，直接客户端伪造 forwarded headers 不得取得 HTTPS/loopback 资格。这些均属于计划已有传输边界的实现检查，无需限定绑定后 filter 的其他用途。

### CH-3 — confirmed，low-risk，非阻塞

现有 `Host/Program.cs` 使用 Generic Host；`ChannelHostStartupTests.StartHost` 仅隔离 Channel/Game 端口和数据库。改为 WebApplication 后，只跳过 endpoint mapping 不会表达“不开 HTTP listener”；[Kestrel 官方说明](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0#identify-the-default-endpoint)列明没有端点配置时会使用默认 HTTP 端点。挑战确实指出 Host 转换应验证的生命周期回归。

T-005 内确定开关的实际 server 生命周期：关闭时无 HTTP 监听，既有 socket 模块仍按原配置工作；启用时测试显式使用独占临时端口。以两个 HTTP 关闭、Channel/Game 端口互异的 Host 同时启动验证无默认端口争用，并检查必要初始化失败后没有遗留可用 listener。可以局部选择关闭时保留 Generic Host 构建分支等机制；不需要改变“同一 Host 组合根集成 HTTP”的核心方案，也不需要提前设计全套 hosting 抽象。

## 独立判断与执行边界

**密码方案可执行。** [ASP.NET Core 10.0 PasswordHasher 源码](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs)确认 IdentityV3 写入 PBKDF2-HMAC-SHA512、随机 128-bit salt、256-bit subkey，记录迭代次数并支持 rehash。220,000 次是计划显式选择，不能误当框架默认值；该选择满足规格的版本化随机盐自适应 KDF，不需另设安全标准或性能门槛。生产接线固定成本、测试降成本，以及未知/畸形格式的拒绝，应由 T-002 验证。scheme 前缀先验证再解包，不能把任意历史 Base64 自动当作可信现代记录。当前 Application 仅引用 Abstractions/Data；Identity Core 依赖可正常接入，并不要求引入 Persistence 或完整 Identity 用户体系。

**身份 participant 方案与现有事务顺序相容，但非失败提交声明有实际约束。** `Application/Transactions/DomainTransaction.cs` 先 Prepare 全部参与者，提交唯一 `CommitCanFail` 参与者，再提交其他参与者；后一阶段抛错会报告不可回滚的部分提交，不再回滚 SQLite。`OnlineUserExecutionBehavior.Order=50` 在 `UnitOfWorkBehavior.Order=60` 外持有执行门，所以一条兑换命令可完整保住门。当前 `UserDomainServices.cs` 的直接 `state.Authenticate` 必须由 T-004/T-006 关闭，不能保留旧发布写路径。

`CommitCanFail=false` 不是让普通连接消失异常被当作正常事务机制的许可。T-004 应让预期的连接丢失/代次变化成为受控的“不发布”结果，确保调用方不能把这种结果当作成功；已持久提交后也不能因原请求取消而撤销票据。实现具体结果载体可局部选择，无需预设计新协议。当前 `OnlineStateRegistry` 有同锁身份/连接集合与按精确连接清理接缝，可支撑这一实现。规格要求的异常注入仍须检查：提交前无身份且票据可在第二连接兑换；提交后发布或投递失败清理原连接且第二连接无法重放。BD-003 和 T-004 已安排这些检查，因此不存在需要先修计划的未决事务架构。

**端点链没有测试捷径。** 当前 `InfrastructureModule.ChannelSourceOptions` 的 Host/Port 缺省回退真实存在，`ChannelInfoResponseEncoder` 将频道 Host 写入 16 字节 ASCII 栏位，`GameLoginHandler` 又读取独立的后续端点。BD-004 要求启用 API 时验证原始必填配置，不能只验证已回退的 options；CK-001/T-007 以广告 tuple 路由并实际消费 Channel Info，足以暴露第二跳错误。三类 IPv4 不同的受控测试符合规格批准复审的解释，不声称公网可达，也不要求新建后续附属服务。

**WPF 与服务端边界足够清晰。** 配置、HTTP client、状态机、参数 builder 和进程 seam 可与 WPF 绑定隔离；ArgumentList 的单参数构造能保留 A21 模板，现有 GameLoginPayloadCodec 的两个 256-byte ASCII dstring 容纳计划中的账号与 43 字符票据并允许尾部。T-008/T-009 覆盖忙碌门、恢复、密码作用域和视觉检查。`Launcher/` 位于 `Server/Directory.Build.props` 与中央包管理文件的目录边界之外，实现时需为新项目显式设置/导入等效构建和包配置，保证纳入 solution 后确实落实 .NET 10、警告视为错误及测试发现；这是普通项目接线，不是新增计划阻塞。

T-001—T-007 的持久化、应用、身份、HTTP/协议和闭环顺序具有可用接缝，T-008—T-010 完成启动器与运行说明，T-011 汇集逐用例、发布及可视证据。命令处理器仍需遵守仓库的 DomainService/DomainStore 注入约束，秘密哨兵需经过现有会记录异常对象的 `CommandLoggingBehavior`，不能只覆盖 HTTP 输出。上述落实点均落在计划已有架构测试、日志测试和最终验收范围内；没有发现需要 bounded plan 修订、上游退回或新增独立接受边界的问题。
