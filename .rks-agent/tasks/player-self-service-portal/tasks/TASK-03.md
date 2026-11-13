# TASK-03 — 玩家身份、两前缀共用边界与管理员控制

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付 Plan T-003 的全部内容：
  - SD-2 两前缀共用的单一后台边界；
  - SD-3 玩家会话与 Cookie；
  - SD-1 玩家命令骨架（执行器内依次判定写意图、总开关、会话）；
  - SD-1a 玩家拒绝契约，包括独立的响应映射和身份类拒绝；
  - SD-5 玩家登录限流的独立实例；
  - SD-9 审计（操作者种类与账号的搭配规则、玩家登录 / 登出与管理员设置 / 踢人的审计、`actor` 筛选、新动作名）；
  - 玩家路由：`/auth/status`、`/auth/csrf`、`/auth/login`、`/auth/logout`、`/auth/session`、`/me`；
  - 管理员路由：玩家后台设置读写、有效会话列表、踢单个账号、全部踢下线。

  覆盖 RQ-001–RQ-007（点券 / 邮件部分除外）、RQ-020、RQ-021、RQ-023、RQ-024（登录 / 设置 / 踢人）、RQ-033；验收 AC-001–AC-004、AC-006、AC-007、AC-008(b)、AC-020、AC-021（登录与总开关部分，以及"子开关关闭后仍可登录查看"）、AC-023、AC-024（登录 / 设置 / 踢人 / 筛选 / 迁移前记录 / 登录名脱敏）、AC-031（玩家侧）、AC-032（玩家侧）、AC-034（槽位占满）。同时处理 plan-review R-1、R-2，TASK-01 审查遗留（审计操作者与账号的搭配），TASK-02 审查遗留（AC-031/032 玩家侧用例）。AC-005 属 T-005，AC-008(a)(c) 属 T-004，不在本单元。
- Owned boundary:
  - Data：新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalAccess.cs`；`Server/src/Dfo.Server.Data/Administration/GmAdministration.cs` 只做 `GmAuditActors` 常量、`GmAuditFilter.Actions` 新动作名这两处增量。
  - Abstractions：新建 `Server/src/Dfo.Server.Abstractions/PlayerPortal/IPlayerPortalDomainServices.cs`。
  - Application：
    - 新建 `Server/src/Dfo.Server.Application/PlayerPortal/` 下的领域服务、规则、审计构造、命令与处理器、注册；
    - `Administration/GmAccessDomainService.cs` 只改 `GmAuditDomainService.AppendAsync` 的校验；
    - `HttpOperations/HttpOperationCommands.cs` 只加一行命令注册；
    - `ApplicationModule.cs` 只加一行领域注册。
  - HttpApi：`Server/src/Dfo.Server.HttpApi/HttpRouteOperation.cs`，只在枚举末尾追加值。
  - GmWeb：
    - 改 `GmAccessBoundary.cs`、`GmAuthenticationEndpoints.cs`（审计的 `actor` 参数）、`GmRouteMapping.cs`（管理员玩家后台路由的失败分支）、`GmWebModule.cs`（玩家限流实例、映射新端点）；
    - 新建 `PlayerRouteMapping.cs`、`PlayerPortalEndpoints.cs`、`GmPlayerPortalEndpoints.cs`。
  - 测试：
    - `Server/tests/Dfo.Server.Host.Tests/` 新建 `PlayerPortalTestSupport.cs`、`PlayerPortalAuthenticationHttpTests.cs`、`PlayerPortalBoundaryHttpTests.cs`、`GmPlayerPortalHttpTests.cs`；
    - 改 `GmGameplayRouteTests.cs`（路由清单与计数）；
    - `Server/tests/Dfo.Server.Application.Tests/` 新增审计搭配规则的用例。
  - 文档：`Document/gm-web-admin.md`「访问与审计」一节补玩家前缀、玩家会话、管理员玩家后台接口、审计 `actor` 筛选与新动作名。
  - 不改 Persistence（TASK-01 的 `IPlayerPortalPersistence` 与现有读接口已够用；如确实缺读能力，停下记 Blocker，不越界补）。不写加点券、发邮件、记录查询、玩家物品目录（T-004 / T-005），不改前端（T-006）。GM 原有路由的状态码、消息与响应字段不变（RQ-051），审计响应只按 SD-9 增加字段。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 零警告零错误。
  - 本单元新增测试全部通过：`PlayerPortal*`、`GmPlayerPortalHttpTests`、Application 中的审计搭配用例。
  - `GmGameplayRouteTests` 通过（新路由全部归类为命令或带理由的例外）。
  - 回归：
    - `Dfo.Server.Host.Tests` 中 `Gm*`、`GateHttpHostTests`、`CredentialEntryHttpTests`、`LauncherHttpHostStartupTests`、`LauncherAuthenticationEndToEndTests`；
    - `Dfo.Server.Application.Tests` 与 `Dfo.Server.Persistence.Tests` 中名字含 `Gm` 的测试；
    - `Dfo.Server.HttpApi.Tests` 全部。
- Dependencies: TASK-01（`IPlayerPortalPersistence`、`PlayerPortal*` record、审计两列与 `Actor` 筛选），TASK-02（`GmWeb:TrustedProxies` 与 `GmTestHost` 的 `peerOverride`）；两者均已提交（`c23bbb92d`、`222c47fa7`）。
- Result review: required — 这是访问控制的核心，后续单元都建立在它上面：
  - T-004 / T-005 的每个玩家写命令直接复用本单元的边界、`AuthorizeAsync` 判定、拒绝映射和审计构造，这里的错误会被逐层继承；
  - 边界被改成两前缀共用后，GM 侧一旦退化（身份串用、某个 GM 接口对玩家会话放行、防伪令牌在两种身份间通用、HTTPS 规则漏判某个前缀），"能登录、能用"的功能测试照样会通过；
  - 判定顺序错了（总开关在会话之后、判定在事务之外），只有特意构造的执行顺序用例才能暴露；
  - 必需检查只覆盖本计划列出的情形，需要独立审查确认没有漏掉信任路径或身份串用路径。

## Detailed Plan

### 0. 本单元先定下的本地决定（T-004 / T-005 / T-006 按此使用）

1. **路由。** 下列"玩家前缀"指 `/api/player/v1`，"管理员前缀"指 `/api/gm/v1/player-portal`。

   | 方法 | 路径 | 身份 | 执行方式 | 说明 |
   |---|---|---|---|---|
   | GET | 玩家前缀 `/auth/status` | 公开 | `OutsideGameplay` | 返回 `{ enabled }`。总开关关闭时也正常应答，这是前端判断是否显示玩家选项（RQ-040）的唯一入口，也是 RQ-021"一律拒绝"的唯一例外。 |
   | GET | 玩家前缀 `/auth/csrf` | 公开 | `OutsideGameplay` | |
   | POST | 玩家前缀 `/auth/login` | 公开 | `EnqueuesItself(Gm, PlayerLogin)` | |
   | POST | 玩家前缀 `/auth/logout` | 玩家会话 | 命令 | |
   | GET | 玩家前缀 `/auth/session` | 玩家会话 | `OutsideGameplay` | 返回边界已识别的身份，与 GM 同构。 |
   | GET | 玩家前缀 `/me` | 玩家会话 | 命令 | |
   | GET | 管理员前缀 `/settings` | GM 全权限 | 命令 | |
   | POST | 管理员前缀 `/settings` | GM 全权限 | 命令 | |
   | GET | 管理员前缀 `/sessions` | GM 全权限 | 命令 | |
   | POST | 管理员前缀 `/sessions/kick` | GM 全权限 | 命令 | 请求体 `{ accountId }`。 |
   | POST | 管理员前缀 `/sessions/kick-all` | GM 全权限 | 命令 | 请求体为空 JSON。 |

2. **`HttpRouteOperation` 末尾追加**，顺序固定为：`PlayerPortalSettings, PlayerPortalSettingsChange, PlayerPortalSessions, PlayerPortalKick, PlayerPortalKickAll, PlayerLogin, PlayerLogout, PlayerMe`。T-004 / T-005 继续往后追加。

3. **拒绝契约 SD-1a 的落地。**
   - 所有玩家前缀的失败响应都是 `{ message, code }`，写操作在 T-004 / T-005 再加 `outcome`。
   - 本单元用到的错误码：
     - `portal_closed` 403；
     - `unauthenticated` 401（无会话、过期、被踢、会话属于别的前缀）；
     - `invalid` 400；
     - `rate_limited` 429；
     - `unknown` 500；
     - **新增 `forbidden` 403**：边界上的传输与跨站拒绝，即需要 HTTPS、外站 `Origin`、`Sec-Fetch-Site: cross-site`、防伪令牌无效、路由缺少玩家授权标记（缺标记时按拒绝处理）。SD-1a 原清单没有覆盖这几类，这里补上一个统一的错误码，前端统一显示 `message`。
   - **凭据错误**（账号不存在、密码错误、字段不符合启动器规则、凭据在两次读取之间变了）一律返回 401、`code=unauthenticated`、`message="账号或密码错误。"`，状态码与消息完全相同（RQ-001）。这条只针对凭据错误：总开关关闭时的登录，无论是边界拦下，还是命令内判出（AC-002、AC-008(b)），都必须返回 403 `portal_closed`，不能返回 401（协调方裁定）。
   - 领域拒绝用新类型 `PlayerPortalException(PlayerPortalRefusal reason, string message)`，放在 `Data/PlayerPortal/` 下，写法与 `GmMailException` 一致。`PlayerPortalRefusal` 一次列全：`PortalClosed`、`FeatureDisabled`、`Unauthenticated`、`NotFound`、`LimitExceeded`、`RequestConflict`、`Invalid`、`SettingsConflict`，后续单元直接使用。其中 `SettingsConflict` 只用于管理员设置的版本冲突（第 7 项）。

4. **会话（SD-3）。**
   - 密钥为 `RandomNumberGenerator.GetBytes(32)` 的十六进制大写串（64 字符），库里只存 SHA-256 十六进制。哈希与格式校验写法照抄 `GmAccessDomainService.Hash` / `ValidSecret`，在 PlayerPortal 内各写一份私有方法，不跨域引用 GM 的私有实现。
   - 有效期固定 8 小时，不续期。
   - Cookie 名 `Dfo.Player.Session`，`Path=/api/player`，`HttpOnly`，`SameSite=Strict`，`Secure = Request.IsHttps`（解析后的值），`Expires` 为到期时间，`IsEssential`。登出删除 Cookie 时使用同一个 `Path`。
   - 玩家身份为 `PlayerIdentity(int AccountId, string LoginName, DateTimeOffset ExpiresAt)`。`LoginName` 是账号库里存的 `m_id`，在识别时一并读出，供 `/auth/session`、审计和后续单元使用。

5. **登录名脱敏（RQ-024）。**
   - 失败登录的审计只在**提交的登录名符合启动器账号命名规则（`AuthenticationInputRules`），且确实对应一个已存在的账号**时，记录该账号库中存的登录名与账号 ID。
   - 其余情况，操作者名称记固定占位文本 `GmAuditActors.UnrecordedPlayerName = "（未记录的登录名）"`，账号 ID 为空。
   - 理由：启动器的命名规则只限定"1–256 个可打印 ASCII 字符、不含问号"，大多数密码也符合这条规则。只按规则脱敏，挡不住"把密码误填进账号栏"这种情况。这个做法严于 spec 的字面要求，代价是对不存在的账号名的猜测尝试，审计里看不到猜的是哪个名字。**需协调方确认，见回报。**
   - 提交的密码在任何情况下都不进审计。

6. **审计中操作者与账号的搭配**（TASK-01 遗留）。在唯一的写入点 `GmAuditDomainService.AppendAsync` 中强制以下规则，违反时抛 `ArgumentException`，与现有字段校验同一处：
   - `ActorKind` 只能是 `gm` 或 `player`。
   - `gm` 记录的 `AccountId` 必须为空，`Action` 不能是玩家动作。
   - `player` 记录：
     - `AdministratorId` 必须为空，`AdministratorName` 非空；
     - `Action` 必须属于 `GmAuditActors.PlayerActions`，即 `PlayerLogin`、`PlayerLogout`、`PlayerGrantCera`、`PlayerSendMail`；
     - `AccountId` 必须大于 0。唯一例外是 `Action == "PlayerLogin" && Outcome == "Rejected"`：此时允许 `AccountId` 为空，但为空时 `AdministratorName` 必须等于占位文本。这样"记录了登录名"与"知道是哪个账号"始终同时成立。
   - `ChangePlayerPortal`、`KickPlayerSessions` 是管理员动作，记为 `gm`。

7. **管理员设置的输入与冲突（R-1）。**
   - 请求体为 `{ enabled, ceraEnabled, mailEnabled, limits: { ceraPerGrant, ceraPerDay, tokenCeraPerGrant, tokenCeraPerDay, mailGoldPerMail, mailsPerDay }, revision }`，其中 `revision` 是期望版本，0 表示从未保存过。反序列化使用 `UnmappedMemberHandling.Disallow`。
   - 缺少 `limits`、任何限额小于 0 或大于 `2147483647`（钱包余额上限）、`revision` 小于 0，都返回 400。
   - 版本不符时，领域服务抛 `PlayerPortalException(PlayerPortalRefusal.SettingsConflict, "设置已被其他管理员修改，请刷新后重试。")`，即玩家后台自己的拒绝类型（协调方裁定：不借用 `GmMailException`）。`SettingsConflict` 只在管理员设置中使用，玩家侧映射器不会遇到它。授权失败（401 / 403）沿用每日补给的 GM 授权写法。
   - `GmRouteReplyMapper` 在 `>= Initialization` 分支**之前**插入显式分支：五个管理员玩家后台操作交给 `GmPlayerPortalEndpoints.Failure`，映射为：
     - `HttpRouteInputException`：按其自带的状态码；
     - `PlayerPortalException(SettingsConflict)`→409，其余 `PlayerPortalException`→400；
     - `GmMailException`（GM 授权）：Unauthorized→401，Forbidden→403，其余→400；
     - `ArgumentException`、`JsonException`、`BadHttpRequestException`→400；
     - 其它→500，并记日志。
   - 响应体 `{ message }`，与 GM 现有页面一致。
   - 分支用操作集合判断，不用枚举范围，避免 T-004 / T-005 往后追加枚举值时误入这个分支。

8. **`/me` 的内容**（只含 RQ-003 允许的数据，加上本账号适用的开关与限额）：

   ```
   { accountId, loginName, balances: { cera, tokenCera },
     characters: [{ characterId, name, job, level, isOnline }],      // 只含未删除角色
     portal: { ceraEnabled, mailEnabled, limits, today: { cera, tokenCera, mails } } }
   ```

   - `today` 由 `IPlayerPortalPersistence.UsageAsync` 按 `IDailyResetRuleService.PeriodDay(now)` 取得：点券类必须传币种，`cera` 与 `tokenCera` 取 `Amount`，`mails` 取 `Count`。这是给 RQ-041"今日已用"用的。
   - 账号与角色读取复用 `IGmPlayerDirectoryDomainService.AccountAsync` 与 `CharactersAsync(new GmPlayerFilter(AccountId: id, ...))`，按 `Total` 翻页取全，然后滤掉 `IsDeleted` 并投影成上面的字段。不新写持久化。
   - `/me` 不接受任何参数，请求里附带的账号标识直接忽略（AC-004）。

9. **玩家登录限流（SD-5）。**
   - `GmWebModule` 增加 `services.AddKeyedSingleton(GmLoginLimiter.PlayerKey, (p, _) => new GmLoginLimiter(p.GetRequiredService<TimeProvider>()))`，`PlayerKey = "player"`。GM 的非键控注册保持不变。
   - 玩家登录端点用 `[FromKeyedServices(GmLoginLimiter.PlayerKey)]` 注入。
   - 来源键为 `Connection.RemoteIpAddress`，即受信代理解析后的地址。完成回调与 GM 相同：只有登录成功才退还本次计数。

### 1. Data 与 Abstractions

`Data/PlayerPortal/PlayerPortalAccess.cs`：

- 第 0 节第 4 项的 `PlayerIdentity`。
- `PlayerCredentialCheck(string LoginName, VerifiedLogin? Login, bool FinalAttempt)`，重写 `ToString`，不输出任何凭据。
- `enum PlayerLoginStatus { Succeeded, InvalidCredentials, PortalClosed, CredentialChanged }`。
- `PlayerLoginResult(PlayerLoginStatus Status, PlayerIdentity? Identity, string? SessionSecret)`，`ToString` 不输出密钥。
- 第 0 节第 3 项的 `PlayerPortalRefusal` 与 `PlayerPortalException`。
- `PlayerSettingsChange(bool Enabled, bool CeraEnabled, bool MailEnabled, PlayerPortalLimits? Limits, long Revision)`。
- `/me` 的视图 record：`PlayerAccountView`、`PlayerCharacterView`、`PlayerPortalView`、`PlayerUsageView`。

`GmAdministration.cs` 的增量：

- `GmAuditActors` 增加 `UnrecordedPlayerName` 与 `PlayerActions` 集合。
- `GmAuditFilter.Actions` 加入 `PlayerLogin`、`PlayerLogout`、`PlayerGrantCera`、`PlayerSendMail`、`ChangePlayerPortal`、`KickPlayerSessions`。后两个玩家动作名现在就加入，保证审计筛选清单一次完整，SD-9 已列出这些名字。

`Abstractions/PlayerPortal/IPlayerPortalDomainServices.cs`：

```csharp
public interface IPlayerPortalAccessDomainService
{
    ValueTask<bool> IsOpenAsync(CancellationToken token);                                // 普通读，边界与 status 用
    ValueTask<PlayerIdentity?> IdentifyAsync(string? secret, CancellationToken token);  // 普通读：格式→会话→未过期→账号名
    /// 命令内使用：总开关关闭 → PortalClosed；会话无效 → Unauthenticated。T-004/T-005 的每个写命令都调用它。
    ValueTask<PlayerIdentity> AuthorizeAsync(string? secret, CancellationToken token);
    ValueTask<PlayerLoginResult> LoginAsync(PlayerCredentialCheck check, CancellationToken token);
    ValueTask LogoutAsync(string secret, CancellationToken token);
}
public interface IPlayerPortalAccountDomainService { ValueTask<PlayerAccountView> MeAsync(string secret, CancellationToken token); }
public interface IPlayerPortalAdministrationDomainService
{
    ValueTask<PlayerPortalSettings> SettingsAsync(string gmSecret, CancellationToken token);
    ValueTask<PlayerPortalSettings> ChangeSettingsAsync(string gmSecret, PlayerSettingsChange change, CancellationToken token);
    ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> SessionsAsync(string gmSecret, CancellationToken token);
    ValueTask<int> KickAsync(string gmSecret, int? accountId, CancellationToken token);   // null = 全部
}
```

### 2. Application（`Application/PlayerPortal/`）

**`PlayerPortalAccessDomainService`**，依赖 `IPlayerPortalPersistence`、`IAuthenticationPersistence`、`IGmPlayerDirectoryPersistence`、`IGmAuditDomainService`、`TimeProvider`：

- `IdentifyAsync`：
  1. 密钥格式不对，返回 null；
  2. `FindSessionAsync(hash)`，查不到或 `ExpiresAt <= now` 时返回 null；
  3. 按账号 ID 读出账号的 `m_id`，读不到时返回 null。
- `AuthorizeAsync`：`ReadSettingsAsync`，关闭时抛 `PortalClosed("玩家后台已关闭。")`；然后 `IdentifyAsync`，为 null 时抛 `Unauthenticated("请先登录。")`。**先判开关、后判会话**，与边界顺序一致（RQ-021）。
- `LoginAsync(check)`，SD-1 顺序：
  1. `AcquireWriteAsync`。
  2. 读设置。关闭时返回 `PortalClosed`，不写会话，也不写审计（第 6 节）。
  3. 定位账号：登录名符合 `AuthenticationInputRules` 时调用 `IAuthenticationPersistence.FindAsync(check.LoginName)`，否则视为没有账号。
  4. 判定凭据是否仍有效：`check.Login` 非空，且账号存在，且 `AccountId`、`PasswordHash` 与 `check.Login` 的 `AccountId`、`VerifiedRecord` 一致。**不写回 `ReplacementRecord`**：玩家登录不承担重新哈希与旧 MD5 迁移，这些仍由启动器登录完成。
  5. 凭据有效：
     - `DeleteExpiredSessionsAsync(now)`；
     - 生成密钥，`SaveSessionAsync`；
     - 写审计 `PlayerLogin` / `Succeeded`，操作者为 player，带账号 ID 和库中登录名，对象为 `账号 {id}`；
     - 返回 `Succeeded`。
  6. `check.Login` 非空但记录已变，且 `!FinalAttempt`：返回 `CredentialChanged`，不写审计。
  7. 其余情况：按第 0 节第 5 项写审计 `PlayerLogin` / `Rejected`，原因为"凭据无效"，返回 `InvalidCredentials`。
- `LogoutAsync`：
  1. `AcquireWriteAsync`；
  2. `AuthorizeAsync(secret)`；
  3. `DeleteSessionAsync`；
  4. 写审计 `PlayerLogout` / `Succeeded`。

**`PlayerPortalAccountDomainService.MeAsync`**：`AuthorizeAsync`，然后按第 0 节第 8 项组装。

**`PlayerPortalAdministrationDomainService`**：

- 授权写法照抄 `GmDailyRefillDomainService.AuthorizeAsync`：用 `IGmAccessDomainService.IdentifyAsync` 识别，会话失效返回 401，不是全权限返回 403。
- `ChangeSettingsAsync`：
  1. `AcquireWriteAsync`；
  2. 授权；
  3. `PlayerPortalRuleService.Validate(change)`；
  4. `SaveSettingsAsync(new(...), change.Revision)`，返回 false 时抛冲突（第 0 节第 7 项）；
  5. 保存后 `Enabled == false` 时，在同一事务内 `DeleteAllSessionsAsync`（RQ-021"关闭的同时所有玩家会话失效"）；
  6. 写审计 `ChangePlayerPortal` / `Succeeded`，原因里写开关状态、六项限额、新版本号，关闭时再写清除的会话数；
  7. 返回读回的设置。
- `SessionsAsync`：授权，然后 `ListActiveSessionsAsync(now)`。
- `KickAsync`：
  1. `AcquireWriteAsync`；
  2. 授权；
  3. 按账号删或全部删；
  4. 写审计 `KickPlayerSessions`，对象为 `账号 {id}` 或 `全部玩家`，原因为"踢下线会话 N 个"；
  5. 返回 N。

**`PlayerPortalRuleService`**（internal）：只放设置校验。T-004 / T-005 的限额判定加在这里。

**`PlayerPortalAudit`**（internal static）：

- `Player(action, identity 或 (accountId, name), target, outcome, reason)` 与 `RejectedLogin(...)` 两个构造方法，统一生成 `GmAuditRecord`，固定 `ActorKind = player`、`AdministratorId = null`。
- T-004 / T-005 的成功与失败审计都通过它构造。

**命令**（`PlayerPortalCommands.cs`）：

- 命令类型：
  - `LoginPlayerPortalCommand(PlayerCredentialCheck)`
  - `LogoutPlayerPortalCommand(string Secret)`
  - `QueryPlayerMeCommand(string Secret)`
  - `QueryPlayerPortalSettingsCommand(string GmSecret)`
  - `ChangePlayerPortalSettingsCommand(string GmSecret, PlayerSettingsChange)`
  - `QueryPlayerPortalSessionsCommand(string GmSecret)`
  - `KickPlayerPortalSessionsCommand(string GmSecret, int? AccountId)`
- 都继承 `HttpOperationCommand`，`ToString` 已封闭，不会输出密钥。
- 处理器都先 `HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm)`。
- 业务拒绝经 `HttpOperationWork.ReplyAsync` 包成 `reply.Error`，写法同 `GmSettingsWork.Write`。登录不抛业务拒绝：失败审计必须随事务提交，所以以 `PlayerLoginResult` 返回，写法同 GM 登录。
- 新增 `PlayerPortalCommandRegistration.AddPlayerPortalCommands()`，在 `AddHttpOperationCommands` 中调用。
- `PlayerPortalRegistration.AddPlayerPortal()` 注册三个领域服务与规则服务，由 `ApplicationModule` 在 `AddAdministration()` 旁调用。

**审计规则**：在 `GmAuditDomainService.AppendAsync` 加入第 0 节第 6 项的校验。

### 3. 边界（`GmAccessBoundary`，SD-2）

改写 `InvokeAsync`，用一段代码处理两个前缀。前缀判定后依次执行（各步的拒绝都按前缀选择响应形状）：

1. 判定前缀：`gm` 对应 `/api/gm/v1`，`player` 对应 `/api/player/v1`，其它路径直接放行。
2. 端口不是 GmWeb 监听端口时返回 404。设置 `no-store`。
3. 是否公开：读 `GmPublicEndpoint` 标记。
4. HTTPS 规则，两个前缀共用 `SecureTransport`：
   - 非公开接口一律要求安全传输；
   - 公开接口中，GM 的 `health`、`auth/status` 与玩家的 `auth/status` 豁免，其余要求安全传输。
5. 只在玩家前缀上执行：路径不是 `/api/player/v1/auth/status` 且 `!IsOpenAsync()` 时，拒绝为 `portal_closed`（RQ-021：先于会话判断）。
6. 身份解析：
   - GM 前缀：只读 `Dfo.Gm.Session`，行为与现在完全相同。
   - 玩家前缀：只读 `Dfo.Player.Session`，调用 `IPlayerPortalAccessDomainService.IdentifyAsync`。识别成功时：
     - 身份存入 `context.Items[PlayerIdentityKey]`；
     - `context.User` 设为 `ClaimsIdentity`，认证类型 `"PlayerSession"`，其 `NameIdentifier` 值为 `"player:{accountId}"`。加前缀是为了让防伪令牌的身份绑定与 GM 的数字 ID 不可能撞值：GM 管理员 1 与玩家账号 1 的令牌不能互用。
7. 非公开接口的授权：
   - GM 前缀：保持现有的权限检查。
   - 玩家前缀：
     - 没有身份时拒绝为 `unauthenticated`；
     - 路由缺少 `PlayerSessionRequired` 标记时拒绝为 `forbidden`。缺标记即拒绝，与 GM 侧缺 `GmRequiredPermission` 时返回 403 同理。
8. 修改类请求：`Origin` 与 `Sec-Fetch-Site` 检查、防伪令牌校验，两个前缀共用同一段代码。

响应形状：

- GM 前缀保持 `{ message }` 与现有文案。
- 玩家前缀为 `{ message, code }`。HTTPS 拒绝的文案用"远程访问必须使用 HTTPS。"，其余沿用 GM 文案。
- 把 `Reject` 改为接收前缀，或拆成 `RejectGm` / `RejectPlayer` 两个方法；状态码与判定逻辑只保留一份。

`PlayerSessionRequired` 标记、`PlayerSessionCookie = "Dfo.Player.Session"`、`PlayerIdentityKey` 常量放在 `PlayerRouteMapping.cs` 或边界文件里，二者择一，不重复定义。

### 4. GmWeb 端点与映射

`PlayerRouteMapping.cs`：

- `Map(group, method, path, operation, parser)`：附加 `PlayerSessionRequired` 与 `HttpGameplayOperation(operation, PlayerRouteReplyMapper.Instance)`。
- `PlayerRouteReplyMapper`：
  - `PlayerPortalException` 按 SD-1a 映射：`PortalClosed`→403 `portal_closed`，`Unauthenticated`→401 `unauthenticated`，`FeatureDisabled`→403，`NotFound`→404，`LimitExceeded`→409，`RequestConflict`→409，`Invalid`→400。
  - `HttpRouteInputException`→其状态码，`code` 取 `invalid`。
  - `ArgumentException`、`JsonException`、`BadHttpRequestException`→400 `invalid`。
  - `reply.Outcome` 为 `unknown`、`auditFailed`，或执行器失败→500 `unknown`。
  - 其它→500 `unknown` 并记日志。
  - 成功时：`PlayerLogout` 返回 204 并按 `Path=/api/player` 删除 Cookie；其余返回 `Results.Ok(reply.Value)`。
- 不经过 `GmRouteReplyMapper`（SD-1a）。
- 提供 `PlayerPortalErrors.Json(status, code, message)`，边界与登录端点共用。

`PlayerPortalEndpoints.cs`：

- status、csrf、session 三个 `OutsideGameplay` 路由。理由文字仿照 GM 同类路由，例如"只读 SQLite 的查询：玩家后台总开关"。
- 登录端点 `LoginAsync`，结构仿照 GM 登录：
  1. JSON 且请求体不超过 8 KiB，`loginName` 与 `password` 都是非空字符串、各不超过 256 字符，否则返回 400 `invalid`；
  2. 玩家限流实例 `TryBegin`，失败时返回 429 `rate_limited` 并带 `Retry-After: 900`；
  3. 最多两轮：
     - `ILauncherCredentialReader.VerifyLoginAsync`，在执行器外；
     - `HttpGameplay.RunAsync(context, Gm, new LoginPlayerPortalCommand(new(name, verified.Login, FinalAttempt: attempt == 1)))`；
     - 结果为 `CredentialChanged` 且是第一轮时，重试一次。写法参照启动器 `AuthenticateAsync`。
  4. 按结果作答：
     - `Succeeded`：写入 Cookie，返回 `{ accountId, loginName, expiresAt }`；
     - `PortalClosed`：403 `portal_closed`；
     - 其余：401 `unauthenticated`，消息为"账号或密码错误。"。
  5. `finally` 中调用 `complete(succeeded)`。
- logout、me 两个命令路由，用 `PlayerRouteMapping.Map` 注册。

`GmPlayerPortalEndpoints.cs`：五条管理员路由，用 `GmRouteMapping.Map` 注册，权限 `GmPermission.All`，密钥取 `Dfo.Gm.Session`；请求体读取仿照 `GmDailyRefillEndpoints.Read`；`Failure` 映射见第 0 节第 7 项。

`GmAuthenticationEndpoints.QueryAudit`：
- 重复参数检查加入 `actor`；
- 新增 `actor` 参数，取值必须在 `GmAuditFilter.Actors` 中，否则返回 400"操作者筛选无效。"；
- 改为构造 `new GmAuditFilter(action, outcome, actor)`。

`GmWebModule`：注册玩家限流实例；在 `MapEndpoints` 中映射 `PlayerPortalEndpoints` 与 `GmPlayerPortalEndpoints`。

### 5. 测试

`PlayerPortalTestSupport.cs`（Host.Tests）：

- `SeedAccountAsync(host, id, name, password, cera, tokenCera)`：密码用 `host.Services.GetRequiredService<IPasswordCredentialRuleService>().Hash(password)` 生成，写入 `accounts.password_hash`。
- 角色插入沿用 `GmPlayerDirectoryHttpTests.SeedAsync` 的列写法，并能设置 `delete_flag`。
- `OpenPortalAsync(host, ...)`：直接写 `player_portal_settings` 行。管理员接口的用例另用 HTTP 路径。
- `PlayerClient`：独立 `CookieContainer` 的 `HttpClient`，带 `CsrfAsync()`（访问 `/api/player/v1/auth/csrf`）、`LoginAsync`、`PostAsync`。
- 取 `Set-Cookie` 的工具。
- 不带 Cookie 容器（`UseCookies=false`）的原始客户端，用于手写 `Cookie` 头。

`PlayerPortalAuthenticationHttpTests.cs`：

- **AC-001**：
  - 开启后用正确密码登录，返回 200，`Set-Cookie` 含 `Dfo.Player.Session`、`path=/api/player`、`httponly`、`samesite=strict`，不含 `secure`；
  - 用错误密码、不存在的账号、带问号的登录名分别登录，三者的状态码与响应体逐字节相同；
  - 登出返回 204，之后 `/me` 返回 401 `unauthenticated`。
- **AC-002**：总开关关闭时，正确密码登录返回 403 `portal_closed`，csrf 也返回 403 `portal_closed`，`/auth/status` 返回 200 `{ enabled:false }`。
- **AC-004**：
  - 账号 A 有角色 a1、a2（a2 已删除），账号 B 有角色 b1；
  - A 访问 `/me` 与 `/me?accountId=<B>`，两次都只返回 A 的余额（只有点券与代币券两个字段）和 a1，不含 a2、b1；
  - 字段集合严格等于第 0 节第 8 项。
- **AC-006 / RQ-005**：用 `GmTestClock`：
  - 推进 7 小时后请求仍然通过；
  - 再推进 1 小时多，返回 401。
  - 两步合起来证明不续期。
- **AC-008(b)**：
  - 用 `configureServices` 包装 `ILauncherCredentialReader`（参照 `GmCeraHttpTests` 的 `CeraGate`）：真实校验完成后发出 `Entered` 信号，等待 `Release`；
  - 在这段时间里，管理员经 HTTP 关闭总开关，然后放行；
  - 登录返回 403 `portal_closed`，没有 `Set-Cookie`，`player_portal_sessions` 为 0 行。
- 凭据在两次读取之间变化：
  - 包装器在放行前改写 `accounts.password_hash`，第一轮命令看到记录已变，端点重试；
  - 第二轮重新校验失败，返回 401；
  - 断言只有一条 `PlayerLogin` / `Rejected` 审计。
- 不写回 rehash：
  - 构造一个需要重新哈希的记录（例如旧 MD5 且账号在 `LegacyMd5Migration` 白名单，或 `RehashNeeded` 的写法，二者择一）；
  - 玩家登录成功后 `password_hash` 不变。

`GmPlayerPortalHttpTests.cs`：

- **AC-020**：
  - 新库 GET settings 读出关、开、开、全 0、版本 0；
  - 非全权限管理员（`permissions: "QueryPlayers,SendMail,GrantCera,ViewAudit"`）读、写都返回 403；
  - 用版本 0 保存成功（返回版本 1），再用版本 0 保存返回 **409**（R-1），用 1 保存成功；
  - 限额为负、超过上限、带未声明字段、缺少 `limits`，都返回 400。
- **AC-021（总开关部分）**：
  - A 已登录；管理员关闭总开关后，A 的 `/me` 返回 403 `portal_closed`，`player_portal_sessions` 为 0 行；
  - 重新开启后，A 原来的 Cookie 访问 `/me` 返回 401；A 重新登录返回 200；
  - 只关点券子开关（或只关邮件子开关）时，A 仍可登录并读取 `/me`，`portal.ceraEnabled` 为 false。
- **AC-023**：
  - 再插入一条已过期的会话；
  - 列表含 A、B 的有效会话，不含过期会话，且带登录名；
  - 踢 A 后，A 返回 401，B 仍为 200；
  - 全部踢下线后，B 也返回 401，会话为 0 行。
- **AC-024**：
  - 本单元产生的登录成功与失败、登出、修改设置、踢人，各有一条审计；
  - 玩家记录的 `actorKind=player`，带 `accountId` 与登录名；
  - `GET /api/gm/v1/audit?actor=player` 只返回玩家记录，且 `total` 正确；`actor=gm` 只返回管理员记录；`actor=x` 与重复的 `actor` 都返回 400；`action=PlayerLogin` 可用；
  - 用 SQL 插入一条只含旧列的 `gm_audit`（迁移前记录），从接口读出 `actorKind=gm`、`accountId=null`；
  - 脱敏：分别以 `"Pa ss?word"`（不符合命名规则）和 `"no-such-account"`（符合规则但账号不存在）为登录名提交失败登录。检索 `gm_audit` 全部文本列，不得出现这两个原文，也不得出现所提交的密码；对应记录的操作者名称为占位文本，`account_id` 为空。另以存在的账号名提交错误密码，记录中带该账号 ID 与登录名。

`PlayerPortalBoundaryHttpTests.cs`：

- **AC-003（R-2）**：
  - 从 `EndpointDataSource` 枚举所有 `/api/gm/v1/` 非公开路由，路由参数一律替换为 `1`，**显式断言清单包含** `GET /api/gm/v1/mail/items`、`GET /api/gm/v1/mail/items/{itemId:int}`、`GET /api/gm/v1/items/{itemId:int}/icon`，以及管理员玩家后台的五条路由；
  - 以只带玩家会话 Cookie（加玩家防伪令牌）的请求逐一调用，全部返回 401"请先登录管理后台。"；
  - 反向：枚举所有 `/api/player/v1/` 非公开路由，以只带 GM 会话 Cookie 的请求调用，全部返回 401 `unauthenticated`；
  - 交叉：把 GM 会话密钥放进 `Dfo.Player.Session`、把玩家会话密钥放进 `Dfo.Gm.Session`，分别访问对方前缀，都返回 401；
  - 防伪令牌不能跨身份使用：GM 身份取得的令牌用于玩家登出，返回 403 `forbidden`；反之亦然。
  - 枚举是动态的，T-004 / T-005 新增的路由会自动纳入。
- **AC-007**：对玩家登录与玩家登出各验证：
  - 缺少或错误的防伪令牌，返回 403 `forbidden`；
  - 外站 `Origin`，返回 403；
  - `Sec-Fetch-Site: cross-site`，返回 403；
  - `remoteConnection: true` 时，登录与 csrf 返回 403（需要 HTTPS），`/auth/status` 返回 200；
  - 非 GmWeb 端口：用 `LauncherHost.Create()` 的设置让同一主机带启动器端口（参照 R1 用例），启动器端口地址从 `IServerAddressesFeature` 取得，在该端口请求玩家 `auth/status` 与 `auth/login`，都返回 404；
  - 配置受信代理 `192.0.2.1` 并开启 `peerOverride`，由代理转发 `X-Forwarded-For: 198.51.100.20`、`X-Forwarded-Proto: http` 访问玩家登录，返回 403（需要 HTTPS）。
- **AC-031（玩家侧）**：配置代理 P，并开启 `peerOverride`：
  - 经 P 转发、`X-Forwarded-For` 为 C1 的玩家登录输错 5 次，第 6 次返回 429；随后 C2 用正确密码登录返回 200；
  - C1 被玩家限流后，同一 C1 的 GM 登录返回 200；反向：C3 的 GM 登录被限后，C3 的玩家登录返回 200；
  - 非 P 来源伪造 `X-Forwarded-For`，按直连地址计数；
  - 请求构造复用 `GmTrustedProxyHttpTests` 的写法。
- **AC-032（玩家侧）**：
  - P 转发 `https` 时，玩家登录的 Cookie 带 `secure`；
  - 按 TASK-02 的做法把会话 Cookie 以非 Secure 形式、`Path=/api/player` 放回 Cookie 容器，再取新的防伪令牌；
  - 登出时 `Origin: https://<本站主机>` 返回 204，外站 `Origin` 返回 403；
  - 非 P 来源伪造 `X-Forwarded-Proto: https` 时，Cookie 不带 `secure`。
- **AC-034（槽位占满）**：
  - 从 `host.Services` 取键控的玩家限流实例，直接用 1024 个不同来源调用 `TryBegin` 占满槽位；
  - 此时 GM 登录（回环）返回 200，玩家登录返回 429 `rate_limited`。

`GmGameplayRouteTests.cs`：

- 路由前缀过滤加入 `/api/player/`；
- `Exempt` 加入 `GET /api/player/v1/auth/csrf`、`GET /api/player/v1/auth/session`、`GET /api/player/v1/auth/status`；
- 按实际新增路由更新 `gm:` 命令数、`HttpGameplayOperation` 数与 `EnqueuesItself` 数（由 3 变为 4）。三个数字都要写成精确值，并在结果中列出。

Application.Tests：新增 `GmAuditActorRulesTests`，覆盖第 0 节第 6 项的每条规则：

- 合法的 gm 记录与 player 记录，以及"未知账号的失败登录 + 占位名"放行；
- 以下情形都抛 `ArgumentException`，且不调用持久化：
  - player 记录没有账号 ID；
  - player 记录带了 `AdministratorId`；
  - gm 记录带账号 ID；
  - 未知的 `ActorKind`；
  - player 记录使用管理员动作；
  - 未知账号的失败登录却记录了原始名字。
- 持久化用一个只记录调用的假实现。

### 6. 执行顺序

1. Data 与 Abstractions 类型 → `HttpRouteOperation` 追加 → 审计搭配规则及其 Application 测试（先写失败用例）。
2. Application 中的三个领域服务、规则、审计构造、命令、注册。
3. 边界改写。改完先跑一遍现有的 `GmAuthenticationHttpTests`、`GmTrustedProxyHttpTests`、`GmCeraHttpTests`，确认 GM 侧不退化，再继续。
4. GmWeb 端点、两个映射器、限流实例、审计 `actor` 参数。
5. Host 测试逐个转绿；更新 `GmGameplayRouteTests`。
6. 文档 `Document/gm-web-admin.md`：
   - 玩家前缀的路由与会话；
   - 共用边界，以及总开关先于会话判断；
   - 管理员玩家后台接口；
   - 审计 `actor` 筛选与新动作名，同时更新文中已有的 `action` 动作清单；
   - 玩家登录限流独立计数。
7. 检查（长命令放后台）：
   - `dotnet build Server/86JP.Server.slnx`；
   - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~PlayerPortal|FullyQualifiedName~GmPlayerPortal|FullyQualifiedName~GmGameplayRouteTests"`；
   - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~GmAudit"`；
   - 回归：
     - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~Dfo.Server.Host.Tests.Gm|FullyQualifiedName~GateHttpHostTests|FullyQualifiedName~CredentialEntryHttpTests|FullyQualifiedName~LauncherHttpHostStartupTests|FullyQualifiedName~LauncherAuthenticationEndToEndTests"`
     - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~Gm"`
     - `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter "FullyQualifiedName~Gm"`
     - `dotnet test Server/tests/Dfo.Server.HttpApi.Tests`
   - Host.Tests 满载时可能因就绪预算超时偶发失败，见 `Document/BuildAndTest.md`；遇到时单独重跑失败用例确认。
8. 变异核对（核对后恢复原代码，在结果中记录）：
   - 把边界里总开关的判断挪到身份解析之后：AC-002 / AC-021 中"已关闭而非未登录"的断言应当失败；
   - 去掉 `NameIdentifier` 的 `player:` 前缀：跨身份防伪令牌用例应当失败；
   - 让玩家登录改用 GM 的限流实例：AC-031 / AC-034 应当失败。

## Result

结论：按 Revision 1 完成（含协调方对本地决定 4、6 的裁定）。未提交，改动均留在工作区。

### 改动

- Data
  - 新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalAccess.cs`：`PlayerIdentity`、`PlayerCredentialCheck`、`PlayerLoginStatus`、`PlayerLoginResult`、`PlayerPortalRefusal`（含 `SettingsConflict`）、`PlayerPortalException`、`PlayerSettingsChange`、`/me` 视图 record、`PlayerSessionsKicked`。
  - `Administration/GmAdministration.cs`：`GmAuditActors` 增加 `UnrecordedPlayerName`、`PlayerLogin`、`PlayerActions`；`GmAuditFilter.Actions` 增加六个新动作名。
- Abstractions：新建 `PlayerPortal/IPlayerPortalDomainServices.cs`，含访问、账号、管理三个领域服务接口。
- Application
  - 新建 `PlayerPortal/` 下的文件：
    - `PlayerPortalAccessDomainService`
    - `PlayerPortalAccountDomainService`
    - `PlayerPortalAdministrationDomainService`
    - `PlayerPortalRuleService`
    - `PlayerPortalAudit`
    - `PlayerPortalCommands`：7 个命令、处理器与注册
    - `PlayerPortalRegistration`
  - `GmAccessDomainService.cs`：`GmAuditDomainService.AppendAsync` 加入操作者与账号的搭配校验。
  - `ApplicationModule.cs`、`HttpOperationCommands.cs`：各加一行注册。
- HttpApi：`HttpRouteOperation` 末尾按计划顺序追加 8 个值。
- GmWeb
  - `GmAccessBoundary` 改为两前缀共用一个边界。
  - 新建 `PlayerRouteMapping.cs`（`PlayerSessionRequired` 标记、玩家响应映射器、Cookie 与错误写法）、`PlayerPortalEndpoints.cs`、`GmPlayerPortalEndpoints.cs`。
  - `GmRouteMapping` 在 `>= Initialization` 之前加入按操作集合判断的分支。
  - `GmAuthenticationEndpoints` 增加 `actor` 参数。
  - `GmLoginLimiter` 增加 `PlayerKey`。
  - `GmWebModule` 注册键控的玩家限流实例，并映射新端点。
- 测试
  - Host.Tests 新建 `PlayerPortalTestSupport`、`PlayerPortalAuthenticationHttpTests`（7 例）、`GmPlayerPortalHttpTests`（4 例）、`PlayerPortalBoundaryHttpTests`（6 例）。
  - `GmGameplayRouteTests`：前缀过滤加入 `/api/player/`，例外清单加入 3 条玩家路由。三个计数改为：`gm:` 命令 40→48，`HttpGameplayOperation` 39→46，`EnqueuesItself` 3→4。
  - Application.Tests 新建 `Administration/GmAuditActorRulesTests`（16 例）。
- 文档：`Document/gm-web-admin.md`「访问与审计」补充共用边界、两个限流实例、`actor` 筛选与新动作名；新增「玩家自助后台」一节，写明玩家接口、错误码、会话和管理员接口。

### 越出所有权范围的一处

`Server/src/Dfo.Server.Application/Authentication/AuthenticationInputRules.cs`：把启动器的账号命名规则提取成 `ValidLoginId`，`Validate` 改为调用它，行为不变。

原因：登录名脱敏需要单独判断"名字是否符合命名规则"。不提取的话，只能用假密码调用 `Validate`，再去读结果里的 `loginId` 键，写法别扭。这个方法 internal，只增不改。启动器相关回归（`CredentialEntryHttpTests`、`LauncherAuthenticationEndToEndTests`、`HttpApi.Tests`）全部通过。

### 执行中的计划修正

- 计划写的"非全权限管理员"权限串 `QueryPlayers,SendMail,GrantCera,ViewAudit`，合起来就是 `All`（= 15），用它测不出拒绝。测试改为 `QueryPlayers,SendMail,GrantCera`，与 `GmDailyRefillHttpTests` 的写法一致。
- AC-031 的玩家侧用例在计划写法基础上加强。原写法只是"同一登录名输错 5 次"，即使两种登录共用一个限流实例，这个测试也照样通过（限流按登录名分别计数）。现在用多个登录名把同一来源的 20 次总额度用完，再验证另一种登录仍可用。两个方向都这样测。

### 验收对照

| AC | 用例 |
|---|---|
| AC-001 | `A_player_signs_in_and_out_and_every_credential_failure_answers_alike` |
| AC-002 | `A_closed_portal_refuses_login_as_closed` |
| AC-003、R-2 | `Neither_session_opens_the_other_prefix`：动态枚举全部 GM 与玩家的非公开路由，显式断言包含物品搜索、物品详情、图标接口与五条管理员玩家后台路由；同时验证把会话密钥放进对方 Cookie 名下也会被拒 |
| AC-003（防伪令牌） | `An_antiforgery_token_does_not_cross_identities` |
| AC-004 | `A_player_reads_only_the_own_account_and_live_characters` |
| AC-006 / RQ-005 | `A_session_expires_eight_hours_after_login_however_it_is_used` |
| AC-007 | `Player_requests_pass_the_same_cross_site_and_transport_rules`：防伪令牌、来源、`Sec-Fetch-Site`、远程明文、经代理转发的明文、启动器端口返回 404 |
| AC-008(b) | `Closing_between_the_password_check_and_the_login_cmd_leaves_no_session` |
| 凭据变化重试 | `A_record_changed_after_the_check_is_checked_again_and_then_refused` |
| 不写回 rehash | `A_player_login_never_rewrites_the_password_record` |
| AC-020、R-1 | `Settings_default_closed_need_full_permission_and_refuse_a_stale_revision`：旧版本号返回 409 |
| AC-021（总开关、子开关仍可登录查看） | `Closing_ends_sessions_at_once_and_feature_switches_keep_the_player_signed_in` |
| AC-023 | `Administrators_list_and_kick_player_sessions` |
| AC-024 | `Audit_marks_player_acts_filters_by_actor_and_never_records_a_stray_name`：迁移前记录、`actor` 筛选、登录名脱敏、密码不入审计 |
| AC-031（玩家侧） | `Behind_a_trusted_proxy_player_and_gm_logins_are_limited_per_client_and_apart` |
| AC-032（玩家侧） | `Forwarded_https_from_the_proxy_secures_the_player_cookie_and_origin_while_a_forged_one_does_not` |
| AC-034（槽位占满） | `A_full_player_limiter_leaves_the_gm_login_alone` |
| TASK-01 遗留 | `GmAuditActorRulesTests` |

### 检查结果

- `dotnet build Server/86JP.Server.slnx`：0 警告、0 错误。
- Host.Tests：`Dfo.Server.Host.Tests.Gm*`、`PlayerPortal*`、`GateHttpHostTests`、`CredentialEntryHttpTests`、`LauncherHttpHostStartupTests`、`LauncherAuthenticationEndToEndTests` 合计 229 例：通过 217、失败 0、跳过 12。跳过的都是代码里原本就标了跳过、需要真实 PVF 的用例，TASK-02 时也是这些。
- Application.Tests `~Gm`：通过 34（含新增的 16 例）、跳过 11（原有跳过）。
- Persistence.Tests `~Gm`：通过 8。
- HttpApi.Tests：通过 146。

### 变异核对

三项核对后都已恢复原代码，并重新构建。

- 把边界中总开关的判断挪到身份解析之后：`A_closed_portal_refuses_login_as_closed` 与 `Closing_ends_sessions_at_once…` 失败。
- 去掉 `NameIdentifier` 的 `player:` 前缀：`An_antiforgery_token_does_not_cross_identities` 失败，返回 204 而不是 403。这说明 antiforgery 的身份绑定不区分认证类型，前缀是必需的。
- 玩家登录改用 GM 的限流实例：`Behind_a_trusted_proxy…` 与 `A_full_player_limiter…` 失败。这里的 AC-031 失败，是按上文加强后的用例；改写前的版本在该变异下仍会通过。

### 留给后续单元

- T-004、T-005 的写命令：沿用 `IPlayerPortalAccessDomainService.AuthorizeAsync`（命令内先判总开关、再判会话）、`PlayerRouteMapping.Map` 与 `PlayerRouteReplyMapper`、`PlayerPortalAudit.Player`。
- 新路由会自动进入 AC-003 的枚举用例，同时需要更新 `GmGameplayRouteTests` 的计数。
- `/me` 的 `today` 已按 `UsageAsync` 计算：点券类传入币种，邮件取笔数。T-004 的限额判断应使用同一口径。

## Result Review

### Independent Quick Review — R1

```yaml
kind: rks-review
verdict: approved
```

- 审查对象：TASK-03 Contract Revision 1 与其 `## Result`（执行轮次 1）；改动为工作区未提交 diff（HEAD `222c47fa7` 之上，13 个修改文件 + 新建 PlayerPortal 切片、三组 Host 测试、`GmAuditActorRulesTests`）。
- 依据：Task Unit 快审 + Code 准则（局部范围）；plan.md T-003 / SD-1、SD-1a、SD-2、SD-3、SD-5、SD-7、SD-9；spec.md RQ-001–007、RQ-020/021/023/024/033、RQ-051 及对应 AC。审查者独立于计划与实现，未修改任何生产文件。
- 复跑检查（审查者本机）：`dotnet build Server/86JP.Server.slnx` 0 警告 0 错误；Host.Tests `PlayerPortal*|GmPlayerPortal*|GmGameplayRouteTests|GmAuthentication*|GmTrustedProxy*` 45 通过 / 1 原有跳过；Application.Tests `~GmAudit` 16 通过；回归 Host.Tests `Gm*|GateHttpHostTests|CredentialEntryHttpTests|LauncherHttpHostStartupTests|LauncherAuthenticationEndToEndTests` 204 通过 / 12 原有 PVF 跳过；HttpApi.Tests 146 通过；Application.Tests `~Gm` 34 通过 / 11 原有跳过。与 Result 所报一致。

#### 逐项核查（攻击者视角）

1. **会话串用**：`GmAccessBoundary` 按前缀分支，GM 分支只读 `Dfo.Gm.Session`，玩家分支只读 `Dfo.Player.Session`；端口、HTTPS（`SecureTransport`）、Origin / `Sec-Fetch-Site`、`antiforgery.ValidateRequestAsync` 是同一段代码，仅响应形状按前缀分派。玩家非公开路由缺 `PlayerSessionRequired` 即 403；GM 非公开路由沿用缺 `GmRequiredPermission` 即 403。`Neither_session_opens_the_other_prefix` 从 `EndpointDataSource` 动态枚举两侧全部非公开路由（显式断言含物品搜索、详情、图标与五条管理员玩家后台路由），并测了"把对方密钥放进本方 Cookie 名"。防伪令牌的身份绑定靠 `NameIdentifier = "player:{id}"`，变异核对证实去掉前缀会让跨身份用例失败。未发现漏掉的信任路径（无端点的玩家路径按非公开处理 → 401/403；路径前缀与 `PathString` 比较均大小写不敏感，尾斜杠只会更严）。
2. **范围裁剪**：`/me` 解析器只取 Cookie，领域服务以 `AuthorizeAsync` 得到的会话账号查询，角色再按 `AccountId` 与 `!IsDeleted` 过滤；投影字段与 RQ-003 一致。登录失败三类（错密码 / 不存在 / 不合规登录名）状态码与响应体逐字节相同，测试已断言。
3. **开关**：边界在身份解析前判总开关（`auth/status` 唯一例外，只回 `{ enabled }`，合理）；`AuthorizeAsync` 在命令内先判开关再判会话；`LoginAsync` 在 `AcquireWriteAsync` 后重读设置，AC-008(b) 用可控闸门验证"无会话、无审计、403 portal_closed"；关闭时同事务 `DeleteAllSessionsAsync`；重开后旧 Cookie 401、重新登录 200 均有用例。
4. **登录**：执行器外 `VerifyLoginAsync`（与启动器同规则），命令内比对 `AccountId` 与 `PasswordHash == VerifiedRecord`，记录变化时重试一次后拒绝；不写回 `ReplacementRecord`（旧 MD5 用例验证 hash 不变）。限流为键控独立实例，来源键为转发解析后的 `RemoteIpAddress`；加强后的 AC-031 用例与 AC-034 槽位占满用例能暴露"共用实例"错误（变异核对已证）。Cookie：`HttpOnly`、`SameSite=Strict`、`Path=/api/player`、`Secure = Request.IsHttps`（转发解析后），登出按同一 Path 删除。
5. **管理员**：边界 `GmRequiredPermission(All)` + 领域内再次 `IdentifyAsync` 并要求 `All`；版本冲突走显式操作集合分支映射 409（不落入 `>= Initialization`）；踢单个 / 全部后会话行被删、下一请求 401。审计写入点 `AppendAsync` 强制 actor/account 搭配（16 例覆盖每条规则）；`actor` 筛选校验与重复参数 400；未知登录名记占位文本、无账号 ID，原文与密码不入库（全列文本检索断言）。
6. **GM 与启动器不变**：GM 分支的身份、权限、HTTPS 判定与文案逐行等价于原实现；`AuthenticationInputRules.ValidLoginId` 抽取与原条件逐项等价（null/空、≤256、Trim 相等、可打印 ASCII 且非 `?`），启动器回归全绿。越界一处已在 Result 说明，影响为零，接受。
7. **测试有效性**：关键用例都以"错误实现会失败"的方式构造（判定顺序、令牌前缀、限流实例三项有变异核对记录；计划中"非全权限"权限串与 AC-031 写法的两处缺陷由执行者自行发现并修正）。

#### 非阻塞残余（不影响本单元通过，供后续单元 / T-006 知悉）

- 玩家登录端点在执行器失败或意外异常时直接重抛（写法同 GM 登录），GmWeb 无全局异常处理，响应是不带 `{ message, code: "unknown" }` 的裸 500，与 SD-1a "前端只按 code 分支" 有细微出入。建议 T-006 前端把无 `code` 的 5xx 视作 `unknown`，或日后在登录端点补一个兜底。
- 总开关关闭时登出被边界以 `portal_closed` 拒绝，不会清除浏览器 Cookie；会话行已被删，无安全影响。
- 匿名（未登录）防伪令牌在两前缀间可互用，这是 antiforgery 的固有行为；带身份的令牌不可互用，已验证。
- 身份识别经 GM 玩家目录读账号，若账号余额越界，目录读取会抛异常导致该账号请求 500（既有目录行为，非本单元引入）。
