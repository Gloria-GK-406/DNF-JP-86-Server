# 实现变更对抗挑战

- Target path: `E:\Project\DNF\DNF-JP-86-Server\.worktrees\launcher-auth-login` 的完整未提交实现工作树。
- Target revision: `HEAD e567c803b99260786aaddb0d2d4612dc3334b6f4` 之上的当前未提交变更，检查日期 `2026-09-10`。
- Governing sources: `TaskContract.md` revision 1、`spec.md` revision 3、`plan.md`（指向 spec revision 3）。
- Attack scope: 从认证安全、共享持久化可用性、启动主路径恢复和短时票据生命周期反推可导致批准结果失败的实现缺陷；不依赖实现者或漂移审查者的结论。本轮不重开 revision 3 已明确取舍的精确双 DPI 实窗矩阵，也不对最终美术提出挑战。

## CH-1: 未认证的高成本密码校验占有 SQLite 全局写者

- Risk: high-risk
- Failure and impact: 每个格式合法的登录请求在查账号、执行真实或 dummy 的 220,000 次 PBKDF2 之前先取得 SQLite 写意图，并把该写者资格一直持有到命令提交。远程攻击者无需有效凭据即可并发发送错误登录，把 SQLite 唯一写者串行占在高成本 KDF 上；排队请求还会继续做昂贵校验。结果不只是不让攻击者登录，而是票据消费及同一数据库中的无关 Game 写事务也会等待或超时，网络可达的登录入口因此能使批准结果中的注册、登录、票据兑换和既有游戏写链整体不可用。现有 HTTPS gate 只保护传输，不提供请求速率或并发上限；普通功能测试也不会覆盖这种跨请求资源争用，事后部署恢复不能消除代码内的锁放大。
- Evidence: `Server/src/Dfo.Server.Application/Authentication/AuthenticationDomainService.cs:27-45` 在 `AcquireWriteIntentAsync` 后才执行账号查询、dummy/真实密码验证和可能的 rehash；`Server/src/Dfo.Server.Persistence/Accounts/SqliteAuthenticationPersistence.cs:9-13` 明确说明零行更新取得写者并保持到 KDF、插票和提交；`Server/src/Dfo.Server.Application/Authentication/PasswordCredentialService.cs:16-25` 固定生产迭代数为 220,000；全仓未见 Launcher API 的 rate/concurrency limiter。可复现探针是并发发送大量格式合法的未知账号或错误密码 `/login`，同时在同一数据库执行一个无关 transactional Game 写，记录其等待/超时和登录吞吐。
- Minimum correction: 不在执行 PBKDF/dummy 校验期间持有 SQLite 写者；校验后在短事务中以条件更新/重读重试保护凭据迁移与签票原子性，并为未认证的注册/登录 KDF 增加有界并发或速率控制。增加并发错误登录不能饿死同库无关写事务的回归探针。

## CH-2: 可选账号偏好写失败会阻断已经认证成功的启动主路径

- Risk: low-risk
- Failure and impact: HTTP 登录已经成功并取得 60 秒票据后，ViewModel 无条件先写 `%LOCALAPPDATA%` 偏好，再调用 `gameLauncher.Launch`；即使用户没有勾选“记住账号”，也会写入空偏好。若目录只读、配额耗尽、文件被占用或原子替换失败，异常会被外层捕获并转成通用“操作失败”，游戏完全不启动，短时票据被白白丢弃。用户重试仍会经过同一失败点，所以这不是界面声称的可恢复重试；但它依赖本地文件系统故障，故作为非阻塞低风险项保留。
- Evidence: `Launcher/Dfo.Launcher/ViewModels/LauncherViewModel.cs:205-227` 在成功登录后 `await preferences.SaveAsync(...)`，只有它返回后才调用 `gameLauncher.Launch`；`:239-245` 将任意异常映射为错误状态。`Launcher/Dfo.Launcher/Preferences/LauncherPreferencesStore.cs:47-92` 的保存路径会创建目录、临时文件、flush、replace/move，写失败不降级；现有 ViewModel 测试的 fake store 未覆盖抛错。用一个 `SaveAsync` 抛 `UnauthorizedAccessException` 的 store 即可断言当前 `IGameLauncher` 从未被调用。
- Minimum correction: 把非敏感、可选的偏好保存与核心启动成败解耦；偏好失败最多提示“账号未能记住”，不得阻止使用已取得票据启动。补一个 preference save 抛错但 Game launcher 仍恰好调用一次、且不泄露票据的测试。

## CH-3: 一分钟票据在数据库中永久累积

- Risk: low-risk
- Failure and impact: 每次成功 HTTP 登录都新增一行 `login_tickets`，但消费、过期后没有删除或保留期策略，也没有按过期时间清理的索引。长期运行或登录重试频繁时，短时票据表只增不减，数据库文件和主键索引无界增长；这会逐步增加存储、备份和写维护成本，最终可能影响同一共享 SQLite 数据库的认证及游戏写路径。问题不会在当前有限次数的功能测试中显现，因此现在不阻塞一次性与原子兑换结论，但应在长期运行前处理。
- Evidence: `Server/src/Dfo.Server.Persistence/Accounts/SqliteAuthenticationPersistence.cs:24-40` 只实现 `INSERT` 与将 `consumed_at` 置值的 `UPDATE`；`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs:199-205` 定义表但没有保留/清理结构。对 `Server/src` 全局检索 `login_tickets` 未发现 `DELETE`、清理任务或 retention policy。可复现探针是循环成功登录并消费或等待过期，随后断言表行数仍与历史成功登录总数相同。
- Minimum correction: 定义有界保留期，并在可控维护点批量删除早于保留边界的已消费或已过期行；为清理条件建立必要索引，并以持久化测试证明活跃未消费票据不被删、历史行数可收敛。
