# TASK-01 — 安全账号认证与一次性票据持久化核心

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付现代密码、受控旧 MD5 迁移、注册、HTTP 登录签票和原子票据消费的 Data/Abstractions/Application/Persistence 核心；覆盖 Spec RQ-004—RQ-009、RQ-013—RQ-014，AC-003—AC-008、AC-012；Plan BD-002—BD-003、T-001—T-003，并落实 plan challenge CH-1 的先读后写并发检查。
- Owned boundary: 可修改 `Server/src/Dfo.Server.Data`、`Dfo.Server.Abstractions`、`Dfo.Server.Application`、`Dfo.Server.Persistence` 中认证直接相关代码与 module 注册，`Server/Directory.Packages.props` 及对应四个测试项目。不得修改 Host、Infrastructure、Protocol、Networking、Launcher 或用户文档；不得提交 Git commit。
- Required checks: 按 TDD 逐个记录有价值行为的 RED/GREEN；运行认证相关 Application/Persistence focused tests，再运行 `dotnet test` 对 Data/Abstractions 无独立测试之外的 Application 与 Persistence 测试项目 Release。必须证明旧库 schema 幂等、注册竞态、现代记录不同盐、逐 AccountId allowlist、签票不存原值、60 秒边界、错误密码不签票、消费复用/篡改/过期/账号不匹配/并发，以及 SQLite 延迟事务写升级交错的已处理结果。
- Dependencies: 批准的 `../plan.md` revision 对应 SHA-256 `316F2F5CAE2EF011E27F462199AF78CB985A1FE878DA6C99932592AC9D69F1EA`；批准的 `../spec.md` revision 2。

## Execution Round 1 — 2026-09-10

### Changed

在基线 `e567c803b99260786aaddb0d2d4612dc3334b6f4` 的共享工作树中完成认证核心，使用本 durable Task 控制执行，未提交 Git。

- Data 新增 `AuthenticationStatus`、注册结果和签票结果；签票结果的 `ToString()` 只报告状态。
- Abstractions 新增 `IAuthenticationDomainService`、`IAuthenticationPersistence`、`IPasswordCredentialRuleService`。Application 新增 `RegisterAccountCommand`、`AuthenticateAndIssueTicketCommand`，两个命令由 ApplicationModule 注册为 Transactional；命令处理器只注入领域服务。秘密命令使用普通 class，默认字符串表示不包含字段值。
- 密码记录使用 `dfo-identity-v3:` + IdentityV3，公开生产构造路径固定 SHA512 / 220000 次；低成本构造器仅 internal、供测试注入。支持框架验证和 rehash、未知格式拒绝、缺失账号 dummy 验证、逐 AccountId 旧 MD5 固定时验证与同事务升级。
- `LauncherAuth:LegacyMd5Migration:AllowedAccountIds` 在 module 配置时严格解析，默认空，非正数、重复、范围、通配符、标量、全局开关及未知子项均失败关闭。
- 新增 `login_tickets` schema 和 SQLite 认证操作；只存 SHA256 摘要、AccountId、UTC ticks 形式的签发/过期/消费时间。签发使用 32 字节 CSPRNG Base64Url，固定 60 秒；兑换为带精确账号名/摘要/未消费/严格过期条件的单条 UPDATE RETURNING，复用既有领域事务。
- 旧库兼容、注册竞态、密码成本/盐、迁移与角色关联、票据并发/时间/回滚、真实 SQLite 交错和秘密日志均有检查。既有 schema 总表数断言由 44 更新为 45，新增 `login_tickets` 精确列断言。

Owned diff（25 个代码/项目/测试文件；过程证据另列）：

```text
Server/Directory.Packages.props
Server/src/Dfo.Server.Abstractions/Authentication/AuthenticationServices.cs
Server/src/Dfo.Server.Application/ApplicationModule.cs
Server/src/Dfo.Server.Application/Dfo.Server.Application.csproj
Server/src/Dfo.Server.Application/Authentication/AuthenticationCommands.cs
Server/src/Dfo.Server.Application/Authentication/AuthenticationDomainService.cs
Server/src/Dfo.Server.Application/Authentication/AuthenticationInputRules.cs
Server/src/Dfo.Server.Application/Authentication/AuthenticationRuleService.cs
Server/src/Dfo.Server.Application/Authentication/PasswordCredentialService.cs
Server/src/Dfo.Server.Data/Authentication/AuthenticationModels.cs
Server/src/Dfo.Server.Persistence/PersistenceModule.cs
Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs
Server/src/Dfo.Server.Persistence/Accounts/SqliteAuthenticationPersistence.cs
Server/tests/Dfo.Server.Application.Tests/Authentication/AuthenticationInputTests.cs
Server/tests/Dfo.Server.Application.Tests/Authentication/LegacyMigrationPolicyTests.cs
Server/tests/Dfo.Server.Application.Tests/Authentication/PasswordCredentialTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Schema/SchemaOwnershipTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationConcurrencyTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationConsumptionTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationFailureTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationFixture.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationMigrationTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationRegistrationTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationSchemaTests.cs
Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationTicketTests.cs
```

关键最终内容 SHA256：`AuthenticationDomainService.cs` = `B1E96AE6C505C12DF5E835365828F75EA8DB7B03AB923A661862C176A004114C`；`SqliteAuthenticationPersistence.cs` = `5ADC7042264183968CBED6B7901C26B86E8AC2210742C8987059EEDF73A87824`；`AuthenticationConcurrencyTests.cs` = `30270F43AA4044E5D45CD603D511E8715CC51964639018D476A18843959AAFAD`。完整拥有边界由以上清单及 `git diff --name-only` / `git ls-files --others --exclude-standard Server` 确认。

### Why

CH-1 采用局部策略：密码登录在读取账号前，先在同一 SQLite participant 中执行零行 UPDATE 取得写意图，直到 KDF、可能的密码升级、签票和提交完成才释放。真实交错测试先让两个账号都读到凭据，A 提交后再允许 B 写，原实现实际等待 30 秒后报 SQLite Error 5；取得写意图后 B 在读之前等待，两个登录都提交成功。这避免过期 deferred snapshot 的读→写升级，不改全局事务模式、不重放已提交的认证命令。代价是密码验证期间占用该数据库的写锁；本任务选择的局部正确性策略并不承诺高并发吞吐或无限等待成功。

注册的第一条数据库操作已经是 INSERT，兑换的第一条操作已经是条件 UPDATE，二者没有登录的先读后写问题。旧账号只更新原行的 password_hash，账号 ID、创建时间、货币及角色关系由真实 SQLite 断言保持。

TDD value gate：保护对象是账号与票据的持久化事实、拒绝语义和时间边界；可发生的回归包括默认信任旧摘要、重复注册/消费、在事务失败后遗留升级、盐或 KDF 成本退化，以及 SQLite 升级失败。既有测试仅覆盖旧账号 GetOrCreate 与通用事务，不能观察这些新结果。新测试主要通过真实命令总线、真实 SQLite、可控时钟和框架密码验证观察结果，内部服务重排不改变判据；dummy 验证计数是 BD-002 明确要求的安全工作量边界。生产成本测试读取版本化密码记录中的算法与成本，验证持久凭据合同而非构造器形状。

### Checks

以下命令均在 `E:\Project\DNF\DNF-JP-86-Server\.worktrees\launcher-auth-login` 执行；表中的 GREEN 使用相同命令，另有明确标记的扩展过滤。RED 是执行当时观察到的行为失败，不是编译失败。伴随的拒绝/格式防护用例中有些从一开始即通过，它们属于防回归检查，不声称经历了 RED。

| 行为 | 实际 RED/GREEN 命令 | RED 决定性输出 / 退出码 | GREEN |
|---|---|---|---|
| 旧库升级与账号保留 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationSchemaTests --nologo` | table count Expected 1 / Actual 0；exit 1 | 原命令对应新测试通过；GREEN 扩展过滤为 `'FullyQualifiedName~AuthenticationSchemaTests\|FullyQualifiedName~SqliteAccountRepositoryTests'`，2 passed / exit 0 |
| 现代密码/盐/成本/验证/rehash | `dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --filter FullyQualifiedName~PasswordCredentialTests --nologo` | StartsWith expected `dfo-identity-v3:`，实际空；1 failed、4 companion checks passed；exit 1 | 5 passed / exit 0 |
| 注册与重复竞态 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationRegistrationTests --nologo` | 没有任何 Success，Assert.Single 失败；exit 1 | 1 passed / exit 0；6 并发注册只有一个创建成功 |
| 输入字段边界 | `dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --filter FullyQualifiedName~AuthenticationInputTests --nologo` | 无字段错误，Assert.True expected true / actual false；15 failed、4 companion checks passed；exit 1 | 19 passed / exit 0 |
| 签票/60 秒/dummy/错误密码 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationTicketTests --nologo` | Success 实为 InvalidCredentials；dummy expected 1 / actual 0；2 failed / exit 1 | 2 passed / exit 0 |
| 条件兑换/并发/过期/回滚 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationConsumptionTests --nologo` | 无消费赢家；未提交消费亦为 null；2 failed / exit 1 | 2 passed / exit 0；8 并发只一个成功 |
| 精确迁移 allowlist | `dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --filter FullyQualifiedName~LegacyMigrationPolicyTests --nologo` | 非法配置未抛、授权 ID 仍 false；13 failed / exit 1 | 13 passed / exit 0 |
| MD5 原行升级/modern rehash | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationMigrationTests --nologo` | 授权正确 MD5 返回 InvalidCredentials；旧 modern record 未升级；2 failed、4 refusal checks passed / exit 1 | 6 passed / exit 0 |
| CH-1 真实 SQLite 交错 | `dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationConcurrencyTests --nologo --logger 'console;verbosity=detailed'` | `Microsoft.Data.Sqlite.SqliteException : SQLite Error 5: 'database is locked'.`，InsertTicketAsync / UnitOfWorkBehavior 路径，30.8016 s，exit 1 | 1 passed，测试 878 ms / exit 0；最终全项目同用例亦通过 |

输入测试的一个 fixture 修正：xUnit MemberData 序列化把未配对 surrogate 替换为 U+FFFD，首次 GREEN 尝试因此 18 passed / 1 failed；把该输入在 Fact 方法内构造，确认直接 `Rune.DecodeFromUtf16` 拒绝后 19 passed。未删掉该边界，也未放宽生产规则。

额外结果检查（编写时已实现的行为，不冒充 TDD RED）：真实 SQLite trigger 拒绝签票时旧 MD5 升级一起回滚；提交前取消使票据仍可兑换；stale password CAS 拒绝；默认空 allowlist 的集成拒绝；捕获正常/失败命令日志、异常和命令/结果字符串，密码、账号名、现代记录、旧 MD5、原始票据、摘要哨兵均不出现。

最终命令与证据：

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-debug\Script.pvf'
dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-persistence-final.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence
dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-application-final.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence
git diff --check
```

- Persistence：352 passed、0 failed、0 skipped，exit 0；完整日志与逐项 TRX 为 `../evidence/task01-persistence-final.log` / `.trx`。
- Application：1325 passed、0 failed、0 skipped，exit 0；完整日志与逐项 TRX 为 `../evidence/task01-application-final.log` / `.trx`。
- 已解析两个最终 TRX 的每个 `UnitTestResult`，统计全部为 Passed；没有只依赖顶层 summary。
- 认证 focused Persistence：16 passed，证据 `../evidence/task01-authentication-focused.trx`；Application 的三个 focused 类共 5 + 19 + 13 = 37 个 case，最终全项目逐项再次通过。
- `git diff --check` 无输出、exit 0。Data/Abstractions 无独立测试项目，随上述构建编译。Application 引用仍只指向 Abstractions/Data；新命令的构造器通过既有 architecture 检查。

全项目失败分类及处理：

1. 初次 Application 无 PVF：1208 passed / 116 skipped / 1 failed。失败用例 `NpcShopPurchaseTests.Rejected_payment_only_sends_a_failure_and_leaves_balances_unchanged` 是 `ObjectDisposedException: SQLitePCL.sqlite3`，栈根 `CharacterDatabaseSchema.KeepWritersAndReadersApart → ExecuteScalar`，对应 AGENTS 已列栈根。按 systematic-debugging 完整读取技能并保留 `task01-application-release.log/.trx`；同一用例 `--no-build --filter FullyQualifiedName~NpcShopPurchaseTests.Rejected_payment_only_sends_a_failure_and_leaves_balances_unchanged` 隔离复跑 exit 0，证据 `task01-application-retry.trx`。该 PRAGMA 路径未修改，新 schema 声明在它之后执行；未改 NpcShop 或全局连接池行为。带实际 PVF 的完整复跑 1325/1325，最终同样 1325/1325。分类为与仓库已知并行 disposal 故障一致的非持续失败；并发释放的精确外部时序未在本任务重新证明，不声称已修复其底层原因。
2. 首次 Persistence：351 passed / 1 failed。`SchemaOwnershipTests.EveryTableIsDeclaredSomewhereRatherThanByAccident` 固定 Expected 44 / Actual 45，新增 login_tickets 正是第 45 表。更新原快照并增加精确列断言；该类 3/3，最终全项目 352/352。首轮证据 `task01-persistence-release.log/.trx` 保留。

### Remaining

- 按 controller 已确认的跨 Task 交接，旧 `AccountDomainService.LoginAsync → AccountProvisioning.GetOrCreateAsync` 及旧 Game 特征测试暂留，使 TASK-02 接线前仍可构建并回归。新注册/HTTP 签票/票据消费核心不调用该旧路径。TASK-02 必须关闭生产 Game 自动建号路径、接入带 OnlineUser 独占门的兑换命令及持久提交后的身份发布；不能把本 Task 结果当作最终 Game 认证已关闭旧入口的证据。
- HTTP 适配、公共端点及响应映射、Game 提交/发布/投递失败、Launcher 及实机闭环属于后续已登记 Task。本 Task 的公共消费接缝只提供事务内条件消费，不能独自发布身份。
- 无本 Task 已知实现阻塞；本记录是 executor 结果，未写 independent review/verdict，也未更改任务注册表状态。

## Independent Quick Review — Round 1

- Reviewed execution: `Execution Round 1 — 2026-09-10`；Task Unit Contract revision 1，含该轮明确记录的旧 Game 入口移交 TASK-02 边界。独立评审只覆盖本 Task，不裁定 Spec 或其他 Task。
- Reviewed inputs: 本 Task 合同、Changed/Why/Checks/Remaining；仓库 `AGENTS.md`；Spec revision 2 的 RQ-004—RQ-009、RQ-013—RQ-014、AC-003—AC-008、AC-012；Plan BD-002/BD-003、T-001—T-003。Plan 实际 SHA256 与合同中的 `316F2F5CAE2EF011E27F462199AF78CB985A1FE878DA6C99932592AC9D69F1EA` 相同。
- Reviewed diff identity: HEAD `e567c803b99260786aaddb0d2d4612dc3334b6f4`；`git diff --name-only -- Server` 与 `git ls-files --others --exclude-standard Server` 合并去重后正好是本轮声明的 25 个文件。逐文件读取 tracked diff 与新增文件，三个关键文件 SHA256 均与上文相同。为覆盖其余文件，按路径排序，以每行 `path<TAB>SHA256`、行间 LF、无末尾 LF 的 UTF-8 manifest 计算的 SHA256 为 `8E708E4B6EF0FAD73B1F00A3998D75DBCB0D0047C536025C9D692F618CD9E4E3`。未见超出 owned boundary 的产品改动；评审只追加本节。
- Evidence checked: 独立解析 `../evidence/task01-persistence-final.trx` 的 352 条及 `../evidence/task01-application-final.trx` 的 1325 条 `UnitTestResult`，全部 Passed，相关 `.log` 的用例与汇总相符；最终运行时间分别为 12:11:33—12:12:14、12:12:31—12:12:50（+08:00），晚于所审 25 文件最后修改时间 12:11:00。认证 focused TRX 为 16 Passed；本轮各行为 RED/GREEN 命令和决定性输出记录在 Checks，最终 TRX 重新覆盖相同测试类；早期 RED 未另有独立日志文件，不能把本评审说成重新执行过 RED。旧全测的确分别有 NpcShop disposal 和 44/45 表数量失败，隔离 retry 与最终全测支持本轮所述处置。评审运行 `git diff --check` 无输出、exit 0，未重复整套测试。
- Confirmed local checks: 生产 DI 注册只能选择公开无参密码构造器，固定 IdentityV3 / 220000，低成本构造器 internal；scheme、框架盐/验证/rehash 及生产记录算法/成本测试匹配。逐 AccountId 配置默认空、非法项拒绝；不存在账号执行 dummy verify。旧 MD5 只在授权、严格形状、固定时匹配后原行 CAS 升级，升级与签票共享 SQLite participant，触发器失败测试证明共同回滚。票据只保存摘要，条件 UPDATE 绑定精确账号并守护未消费状态，并发至多一个提交成功。登录写意图通过持久化接缝取得、零行更新不改变业务事实，未改全局 deferred 模式；KDF 期间持有全库写锁的吞吐代价已如实披露。新增秘密命令/签票结果字符串与捕获日志哨兵检查未展示敏感值泄漏。HTTP/Game 发布/Launcher 仍按移交边界留给后续交付。

### Finding QR1-F1 — P2：等待写锁期间过期的票据仍可兑换

`Server/src/Dfo.Server.Application/Authentication/AuthenticationDomainService.cs:51` 在进入 SQLite 前取得 `clock.GetUtcNow()`；`Server/src/Dfo.Server.Persistence/Accounts/SqliteAuthenticationPersistence.cs:31`—`:40` 将该固定值用于 `expires_at>@now` 与 `consumed_at=@now`。若兑换在有效期最后一秒到达，但被另一个认证/游戏写事务阻塞到过期以后，更新仍按等待前的时间通过。实际影响是已过期票据可被成功消费并交给下游认证发布，违背 RQ-009 / AC-008 的服务端过期边界。

独立 SQL 级复现（不冒充整条 .NET 集成测试）：临时 SQLite WAL 库预置 `expires_at=60`，连接 A 先取得写锁；连接 B 以捕获时间 59 执行生产逐字相同的条件 UPDATE RETURNING，确认它在等待；把可控时钟推进至 61 后释放 A。实际输出为 `{"captured_now":59,"actual_now":61,"expires_at":60,"returned_account":1,"consumed_at":59}`，复跑 exit 0。该复现与上述生产取时/绑定调用链共同证明缺陷。现有 `ExpiryIsExclusiveAndUncommittedConsumptionCanBeRetried` 在调用前设置时钟，没有覆盖取时后等锁跨越过期点。

最小修正：兑换在同一事务内先取得写意图，再读取 TimeProvider 并执行原条件 UPDATE；也可采用等价的、保证取得写锁后刷新判定时间的局部方案。补真实 SQLite 交错测试：另一连接持锁，兑换开始等待，将时钟推进到恰好过期和已过期，释放锁后应返回 null、保持未消费；同时保留未到期成功与并发一次性检查。无需修改全局事务模式或其他 Task。

Verdict: `changes_required`

## Controller Disposition — Round 1

- Decision: `task-rework`。
- Basis: `QR1-F1` 以生产同形 SQL 和真实锁等待证明取时发生在写锁获取前，直接违反 RQ-009 / AC-008 的 `now >= expiresAtUtc` 拒绝边界；修正只涉及本 Task 的消费时序与定向测试，不改变批准 Spec、Plan 中心方案或 owned boundary。
- Required rework: 在同一认证事务中先取得写意图，再读取服务端时钟并执行条件消费；新增真实 SQLite 持锁、兑换等待、时钟推进到等于/超过过期点后释放的 RED/GREEN，且保留未过期、并发一次性和未提交可重试证据。完成后追加 Execution Round 2，并由新的独立 quick reviewer 复审本 finding 及直接回归。

## Execution Round 2 — 2026-09-10

### Changed

仅处理 QR1-F1，增量产品/测试改动两处：

- `Server/src/Dfo.Server.Application/Authentication/AuthenticationDomainService.cs`：`ConsumeTicketAsync` 在同一领域事务先等待 `AcquireWriteIntentAsync`，成功取得锁后才读取 `clock.GetUtcNow()`，随后执行原有原子条件消费。SHA256：`64BD62FA92C20ACDD06414D92BF47710A9A0B7B1D2E88CA1C67DCF63460C83DD`。
- 新增 `Server/tests/Dfo.Server.Persistence.Tests/Accounts/AuthenticationExpiryContentionTests.cs`：真实 SQLite 另一连接持有写锁，消费已进入持久化操作且尚未完成时推进可控时钟，再释放写锁。覆盖释放时刻为过期前 1 tick、恰好过期、过期后 1 tick，分别断言成功、拒绝、拒绝；两条拒绝均检查数据库仍未消费。SHA256：`B39D19B1E8A2B14675E7385E34275D35C415082CF4FD83F11133D1B45B5A0D77`。

SQL 条件、SQLite participant、全局事务模式和其他认证行为未修改；未写 registry、review/verdict 或 Git commit。

### Why

旧逻辑在等待 SQLite 写锁之前固定了过期判断时刻。新增测试从有效期最后一秒发起消费，在持锁期间推进时钟，确实复现“过期后仍返回 AccountId”。局部拆出取得写锁一步，使判断时刻反映锁等待结束后的时间，保持现有严格 `expires_at > now` 条件和一次性消费事务。

这条新测试保护 RQ-009 / AC-008 的服务端过期边界。原测试只在调用前设置时间，无法覆盖等待跨越过期点。新增测试的持久化观察包装只发出进入信号，所有 SQL、锁、事务和返回结果均来自真实 SQLite；没有模拟数据库成功/拒绝，且使用原子时钟避免跨线程时间读写撕裂。

### Checks

工作目录沿用 Round 1。RED 和 GREEN 均已保存完整控制台日志与逐项 TRX，可供独立核对。

```powershell
# 修改生产代码前的真实 RED：exit 1，2 failed / 1 passed
dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationExpiryContentionTests --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-round2-red.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence

# 最小修复后的 GREEN：exit 0，3 passed
dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --filter FullyQualifiedName~AuthenticationExpiryContentionTests --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-round2-green.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence

# 直接回归：exit 0，19 passed
dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --no-build --filter FullyQualifiedName~Authentication --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-round2-focused.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence

$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-debug\Script.pvf'
# 全项目 Release：exit 0，1325 passed
dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-round2-application.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence
# 使用本轮 GREEN 已构建的 Release 产物：exit 0，355 passed
dotnet test Server/tests/Dfo.Server.Persistence.Tests -c Release --no-build --nologo --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=task01-round2-persistence.trx' --results-directory .rks-agent/tasks/launcher-auth-login/evidence
git diff --check
```

- RED：`ticksAfterExpiry: 0` 和 `ticksAfterExpiry: 1` 均为 `Assert.Equal() Failure: Expected: False / Actual: True`，确为过期票据返回账号；未过期 `-1` 控制分支通过。日志/TRX：`../evidence/task01-round2-red.log` / `.trx`。
- GREEN：同一测试三分支全部通过，拒绝后未消费；证据 `task01-round2-green.log/.trx`。
- 直接回归 19/19，包括原八路并发准确一次、未提交消费回滚、提交前取消可再消费和原 CH-1；证据 `task01-round2-focused.log/.trx`。
- Application 1325/1325、Persistence 355/355，全为 Passed、0 failed、0 skipped；已逐个解析最终 `UnitTestResult`，完整日志及 TRX 为 `task01-round2-application.log/.trx`、`task01-round2-persistence.log/.trx`。本轮没有出现需要另行分类的意外失败。
- `git diff --check` 无输出、exit 0。

### Remaining

QR1-F1 的执行修复及验证完成，等待 controller 安排独立复审；Round 1 所述 TASK-02 旧 Game 路径关闭及提交后身份发布交接保持有效。本轮无新增实现阻塞。

## Independent Quick Review — Round 2

- Reviewed execution: `Execution Round 2 — 2026-09-10`；Task Unit Contract revision 1，及 `Controller Disposition — Round 1` 对 QR1-F1 的局部返工要求。独立复审范围为 QR1-F1、其修复的直接回归，以及本轮检查证据；未重新裁定其余 Task 或整个 Spec。
- Reviewed inputs: 本 Task 合同、QR1-F1、controller disposition、Round 2；完整读取 `reviewing-task-units-quick/SKILL.md` 并遵循适用 `AGENTS.md`；直接核对 Spec RQ-009 / AC-008、Plan BD-003 / T-003。Round 1 的其余已审事实仅用于确定不变边界。
- Owned-diff identity: HEAD 仍为 `e567c803b99260786aaddb0d2d4612dc3334b6f4`。本轮两文件当前 SHA256 分别匹配上文 `64BD62FA92C20ACDD06414D92BF47710A9A0B7B1D2E88CA1C67DCF63460C83DD` 与 `B39D19B1E8A2B14675E7385E34275D35C415082CF4FD83F11133D1B45B5A0D77`。将新增交错测试排除，并在 Round 1 的 manifest 口径中仅以原记录的 service hash 替代当前 service hash，重算得到相同 `8E708E4B6EF0FAD73B1F00A3998D75DBCB0D0047C536025C9D692F618CD9E4E3`，确认其余 24 个产品/测试文件未漂移；本轮当前 service 与新增测试均已逐行读取。
- QR1-F1 closure: `ConsumeTicketAsync` 明确先 await `AcquireWriteIntentAsync`，再求值 `clock.GetUtcNow()`。取得写意图的零行 UPDATE 与随后条件 UPDATE RETURNING 都经同一 `SqliteDatabase.WriteAsync`，按 ambient domain transaction 和连接字符串复用同一 participant、connection 及 pending SQLite transaction；两次语句之间没有提交或释放事务。原 `expires_at > @now`、精确账号名、摘要与未消费条件保留。新增测试使用另一真实 SQLite connection 的 immediate transaction 持锁，消费在有效期最后一秒开始，经持久化入口信号同步后推进原子时钟、释放 blocker，并最终提交及查询真实表：过期前 1 tick 返回账号且有一条消费记录；恰好过期和过期后 1 tick 均返回 null 且无消费记录。观察包装没有替代数据库结果。
- Evidence checked: 独立逐项解析全部五份 `../evidence/task01-round2-*.trx`，并核对对应完整日志中的用例结果和汇总。RED 为 3 条中的 2 Failed / 1 Passed，失败准确对应 0 与 +1 tick 的 Expected False / Actual True；GREEN 三条均 Passed。focused 的 19 条全部 Passed，包含原八路并发准确一次、未提交消费可重试、提交前取消后可再消费及 CH-1。最终 Application 的 1325 条、Persistence 的 355 条 `UnitTestResult` 均为 Passed，无失败或跳过；Persistence 全测再次覆盖三条交错及上述直接回归。
- Evidence freshness: 两个增量文件最后修改时间分别为 12:23:28 与 12:22:56（+08:00）；GREEN 运行于 12:23:32—12:23:33，focused 于 12:23:35—12:23:37，最终 Persistence 于 12:24:02—12:24:19，Application 于 12:24:08—12:24:20，均晚于修改。Persistence 的 `--no-build` 使用已于本轮 GREEN 构建并验证的 Release 产物；没有后续产品文件修改使其失效。评审另运行 `git diff --check`，无输出、exit 0；未重复运行全套测试，也未把日志核验描述为独立重跑。
- Findings: 未发现本次返工范围内的阻塞缺陷；QR1-F1 的修复与直接回归证据充分。TASK-02 的旧 Game 入口关闭及提交后发布责任仍按原交接，不由本次 quick review 宣告完成。本评审只追加本节，不修改产品或任务控制状态；是否接受由 controller 裁定。

Verdict: `passed`
