# TASK-04 — 玩家加点券与操作记录

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付 Plan T-004：
  - 玩家加点券命令：SD-1 判定顺序、重放、点券子开关、单次 / 每日限额、钱包入账、在线提醒、成功与失败审计；
  - 回执格式（记录页的时间、类型、内容摘要、结果），持久化只存 `receipt_json`；
  - 操作记录查询接口（分页、按类型筛选；邮件类型的数据由 T-005 产生）。

  覆盖 RQ-007（点券）、RQ-010、RQ-012、RQ-013、RQ-022（点券）、RQ-024（点券）；验收 AC-008(a)(c)、AC-010、AC-012（点券，含"成功后用量已满或子开关已关时原编号重试仍返回原结果"）、AC-013（点券部分）、AC-021（点券子开关；关闭后写请求得到 `portal_closed` 而非 500）、AC-022（点券，含日切与"被拒不计入"）、AC-024（点券成功与失败）。依据 plan.md 的 SD-1、SD-1a、SD-8（点券部分）、SD-9。同时处理遗留：
  - TASK-01 审查：点券类 `UsageAsync` 必须传币种，用测试固定（同日点券与代币券限额互不影响）；
  - TASK-03：限额判定沿用 `/me` 已定的"今日已用"口径；新路由更新 `GmGameplayRouteTests` 计数；失败审计用命令开始时识别到的账号身份（SD-1a）。
- Owned boundary:
  - Data：新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalCera.cs`。
  - Abstractions：`Server/src/Dfo.Server.Abstractions/PlayerPortal/IPlayerPortalDomainServices.cs` 追加点券领域接口与记录查询方法。
  - Application（`Server/src/Dfo.Server.Application/PlayerPortal/`）：
    - 新建 `PlayerPortalCeraDomainService.cs`、`PlayerPortalReceipts.cs`、`PlayerPortalDailyUsage.cs`；
    - 改 `PlayerPortalRuleService.cs`（点券规范化、限额、内容哈希）、`PlayerPortalAccountDomainService.cs`（记录查询；"今日已用"改走共用读取）、`PlayerPortalCommands.cs`（两个命令、处理器、注册）、`PlayerPortalRegistration.cs`。
  - HttpApi：`HttpRouteOperation.cs` 只在枚举末尾追加 `PlayerGrantCera, PlayerOperations`。
  - GmWeb：改 `PlayerPortalEndpoints.cs`（两条路由、请求体与查询参数解析）、`PlayerRouteMapping.cs`（写操作的 `outcome`、加点券成功响应）。
  - 测试：
    - `Server/tests/Dfo.Server.Host.Tests/` 新建 `PlayerPortalCeraHttpTests.cs`、`PlayerPortalRecordsHttpTests.cs`；改 `PlayerPortalTestSupport.cs`（限额设置与加点券帮助方法）、`GmGameplayRouteTests.cs`（计数）。
    - `Server/tests/Dfo.Server.Application.Tests/` 新建 `PlayerPortal/PlayerPortalCeraRuleTests.cs`。
  - 文档：`Document/gm-web-admin.md`「玩家自助后台」一节补加点券与记录接口、回执字段、`outcome`。
  - 不改 Persistence（TASK-01 的 `IPlayerPortalPersistence` 已具备重放、用量、记录分页所需查询；如确实缺能力，停下记 Blocker）。不改 GM 点券的规则服务、领域服务、提醒服务与其路由（RQ-051）。不写邮件（T-005），不改前端（T-006）。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 零警告零错误。
  - 本单元新增测试全部通过：Host.Tests `PlayerPortalCera*`、`PlayerPortalRecords*`；Application.Tests `PlayerPortalCeraRuleTests`。
  - `GmGameplayRouteTests` 通过（计数见详细计划第 5 节）。
  - 回归：
    - Host.Tests 的 `Gm*` 与 `PlayerPortal*`（含 TASK-03 的 AC-003 动态枚举用例，它会自动纳入本单元新路由）、`HttpAuditedFailureBoundaryTests`；
    - Application.Tests 中名字含 `Gm` 的测试（含 GM 点券相关）与 `PlayerPortal`。
  - 变异核对（第 6 节），核对后恢复原代码并在结果中记录。
- Dependencies: TASK-01（`IPlayerPortalPersistence`、`PlayerPortalOperation`、用量与记录查询）、TASK-03（`AuthorizeAsync`、`PlayerRouteMapping.Map`、`PlayerRouteReplyMapper`、`PlayerPortalAudit`、`GmAuditActors.PlayerActions` 已含 `PlayerGrantCera`）；均已提交（`c23bbb92d`、`d7bfa8144`）。
- Result review: required — 这是资金入账：
  - 判定顺序、重放先于子开关与限额、限额按币种分开计数，任一处错了都会让玩家多入账或该入账的被拒；"能加点券"的功能测试照样通过，只有特意构造的顺序、并发、跨币种、日切用例才能暴露；
  - 回执格式与记录接口是 T-005（邮件回执要遵守同一组公共字段）和 T-006（记录页、加点券表单）的输入，形状错了会被后续单元建在上面；
  - 失败审计跨两个工作单元、并经执行器失败审计机制，漏记或误记身份只靠测试列出的情形覆盖，需要独立审查确认没有入账路径绕过判定或审计。

## Detailed Plan

### 0. 本单元先定下的本地决定（T-005 / T-006 按此使用）

1. **路由。**

   | 方法 | 路径（玩家前缀 `/api/player/v1`） | 执行方式 | `HttpRouteOperation` | 说明 |
   |---|---|---|---|---|
   | POST | `/cera/grant` | 命令（`PlayerRouteMapping.Map`） | `PlayerGrantCera` | 请求体 `{ requestId, currency, amount }` |
   | GET | `/operations` | 命令（`PlayerRouteMapping.Map`） | `PlayerOperations` | 查询参数 `kind`（`cera` / `mail`，可省）、`page`（默认 1）、`pageSize`（默认 20，1..100） |

   - 两条路由都只从会话取账号；请求体与查询串里的任何账号标识都不改变操作对象（RQ-003）。
   - 记录查询只受总开关与会话约束，不受子开关约束（RQ-021"子开关关闭只拒绝对应操作，玩家仍可登录查看"）。

2. **请求体。** `PlayerCeraRequest(Guid RequestId, string Currency, int Amount)`：`JsonUnmappedMemberHandling.Disallow`，三个字段都 `JsonRequired`，数字严格（`JsonNumberHandling.Strict`），`requestId` 用与 `GmCeraEndpoints.RequestIdConverter` 相同写法的 UUID 字符串转换器（在 GmWeb 内另写一份私有转换器，不跨端点引用私有类型）；请求体须为 JSON 且不超过 4 KiB。解析失败（非 JSON、超长、未声明字段、缺字段、`amount` 超出 int、`requestId` 为空 GUID）一律 400 `invalid`，**不写审计**——此时命令里没有识别出身份（SD-1a）。

3. **币种与数量。** 币种只放行 `Cera`、`TokenCera`（区分大小写，与 GM 的值相同）；`HappyTokenCera` 与其它值 400 `invalid`（Spec Scope）。数量为 1..2147483647。规范化写在 `PlayerPortalRuleService.NormalizeCera`，不改 GM 的 `GmCeraRuleService.Normalize`（它要求账号列表与原因，不适用）。

4. **加点券命令内的判定顺序**（SD-1，全部在执行单元的同一个命令事务里）：

   1. `IPlayerPortalPersistence.AcquireWriteAsync`；
   2. `IPlayerPortalAccessDomainService.AuthorizeAsync(secret)`：先总开关（`PortalClosed`），再会话（`Unauthenticated`）；
   3. `NormalizeCera`（纯函数，不读库）→ 计算内容哈希；
   4. **重放**：`FindOperationAsync(account, requestId)`。已有记录时：`Kind == Cera` 且 `PayloadHash` 相同 → 直接返回存下的回执（`replayed = true`），**不再判子开关与限额，不写审计**；否则（内容不同或该编号已用于邮件）→ `RequestConflict`"该编号已用于其它内容；新操作请使用新编号。"；
   5. 子开关：`settings.CeraEnabled == false` → `FeatureDisabled`"加点券已关闭。"；
   6. 限额（`PlayerPortalRuleService.CheckCeraLimits`）：
      - 单次上限：`Cera` 取 `CeraPerGrant`，`TokenCera` 取 `TokenCeraPerGrant`；上限 > 0 且数量 > 上限 → `LimitExceeded`；
      - 每日上限：`Cera` 取 `CeraPerDay`，`TokenCera` 取 `TokenCeraPerDay`；已用量取自 `PlayerPortalDailyUsage.CeraAsync(portal, account, currency, day)`（见第 5 项）；上限 > 0 且 `已用 + 数量 > 上限`（用 `long` 计算）→ `LimitExceeded`；
      - `day = IDailyResetRuleService.PeriodDay(now)`，`now` 在本步取一次，写入记录时用同一个 `day` 与 `now`；
   7. 钱包：`IAccountWalletDomainService.ReadAsync`（读不到或钱包数据异常 → `Invalid`，写法同 `GmCeraDomainService.Wallet`）→ `IGmCeraRuleService.Decide(before, new GmCeraInput([account], currency, amount, "玩家自助"))`（SD-8；它抛的 `GmCeraException` 转成 `PlayerPortalException(Invalid, 原消息)`，例如余额上限）→ `TryApplyAsync(account, before, after)`，返回 false → `Invalid`"账号余额已改变，请刷新后重试。"（`AcquireWriteAsync` 之后不应出现，作为防线）；
   8. 写入：`SaveOperationAsync(new(account, requestId, Cera, currency, amount, hash, receiptJson, day, now))`，然后写成功审计（第 7 项），返回回执。

   内容哈希：`PlayerPortalRuleService.CeraPayloadHash(currency, amount)` = `SHA-256("cera\n{currency}\n{amount}")` 的十六进制大写。只含规范化后的内容，不含账号（主键已含账号）。

5. **"今日已用"口径共用。** 新建 `internal static class PlayerPortalDailyUsage`：
   - `CeraAsync(portal, accountId, currency, day)` → `UsageAsync(accountId, Cera, currency, day).Amount`，`currency` 参数类型为非空 `string`，调用方无法传 null；
   - `MailsAsync(portal, accountId, day)` → `UsageAsync(accountId, Mail, null, day).Count`（T-005 的每日封数判定也用它）。
   - `PlayerPortalAccountDomainService.MeAsync` 的 `today` 改为调用这两个方法，行为不变。这样 `/me` 显示的今日已用与限额判定只有一份代码。

6. **回执（`receipt_json`）。**
   - 公共字段（T-005 的邮件回执必须同样带上，记录页只依赖这五个字段）：`requestId`、`kind`（`cera` / `mail`）、`committedAt`、`summary`、`result`。
   - 点券回执在公共字段之外带 `currency`、`amount`、`before`、`after`（`before` / `after` 为 `{ cera, tokenCera }`，不含 `HappyTokenCera`，与 RQ-003 的可见范围一致）。
   - `summary`：`点券 +{amount}` 或 `代币券 +{amount}`；`result`：`成功`。持久化只存成功的操作（TASK-01 决定），所以记录里的结果恒为成功；被拒的请求只进管理员审计。
   - 类型：Data 中 `PlayerCeraReceipt(Guid RequestId, string Kind, DateTimeOffset CommittedAt, string Summary, string Result, string Currency, int Amount, PlayerBalancesView Before, PlayerBalancesView After)`。
   - 序列化统一由 `PlayerPortalReceipts`（Application）完成，选项为 `JsonSerializerDefaults.Web`，与 ASP.NET 输出的命名一致；它同时提供 `Kind` 常量（`cera`、`mail`）与把存储字符串解析成 `JsonElement` 的方法。
   - **加点券的响应与重放都从同一个存储字符串解析出回执**：首次成功与原编号重试返回的 `receipt` 逐字段相同（RQ-012"返回原结果"）。
   - 记录接口原样返回存储的回执（解析为 `JsonElement`），不按类型重新投影；T-005 新增邮件回执时记录接口不需要改。

7. **审计（SD-9）。** 全部经 `PlayerPortalAudit.Player` 构造，动作 `PlayerGrantCera`，操作者为玩家（账号 ID + 库中登录名），对象 `账号 {id}`：
   - 成功：结果 `Succeeded`，原因 `{点券|代币券} +{amount}，余额 {before} → {after}，编号 {requestId:N}`；
   - 业务拒绝（`FeatureDisabled`、`LimitExceeded`、`RequestConflict`、`Invalid`）：结果 `Rejected`，原因 `未提交：{拒绝消息}，编号 {requestId:N}`；
   - 执行单元中非 `PlayerPortalException` 的业务失败：结果 `RolledBack`，原因 `未提交：存储或规则处理失败，编号 …`；
   - 执行器失败审计机制给出的事实：`Unknown`（原因"提交状态暂未确认，编号 …"）或 `RolledBack`；
   - **不写失败审计的情形**：`PortalClosed`、`Unauthenticated`（命令开始时没有识别出身份，或身份类拒绝本身）、请求解析失败、重放命中。
   - 失败审计使用准备单元识别出的 `PlayerIdentity`，写入时不再用会话密钥重新识别（SD-1a）。
   - 长度：原因受 `GmAuditDomainService` 的 512 字符约束，拒绝消息都是本单元或 GM 规则服务的固定短句，不会超长。

8. **命令结构**（仿 `GmCeraCommandHandler.GrantAsync`，`GrantPlayerCeraCommand` 注册时声明 `CommandFailureAuditCapability.AuditedFailure`）：
   1. 准备单元（`WriteAsync`，`CommandFailureAuditRole.Preparation`，名 `prepare-player-cera`）：`access.AuthorizeAsync(secret)` → `PlayerIdentity`。失败 → 回复错误、`outcome = "rejected"`，不写审计。
   2. 执行单元（`WriteAsync`，`CommandFailureAuditRole.Execution`，名 `grant-player-cera`）：`cera.GrantAsync(secret, request)`，即第 4 项全部判定与写入。准备单元只用来在命令开始时拿到审计身份；执行单元仍完整重判，RQ-007 的原子性只依赖执行单元。
   3. 执行单元的业务失败（`c.Request.Failure is null` 且 `BusinessFailure`）：按第 7 项决定是否写失败审计；写的话另开一个写单元 `record-player-cera-failure`（5 秒超时，调用 `cera.RecordFailureAsync(identity, requestId, outcome, reason)`，内部 `RequireTransaction` + `AcquireWriteAsync` + 审计）。审计写入失败 → 回复 `outcome = "auditFailed"`。否则回复错误与 `outcome = "rejected"`（`PlayerPortalException`）或 `"rolledBack"`（其它）。
   4. 执行器失败（`c.Request.Failure is not null`）：已识别身份时经 `ICommandFailureAudit.RecordAsync` 按其事实写 `Unknown` / `RolledBack` 审计，然后重抛；`HttpGameplay` 把它转成带 `outcome` 的回复。未识别身份时直接重抛。
   5. 成功（含重放）后，在执行单元之外调用 `IGmCeraReminderDomainService.RemindAsync([account])`（5 秒超时），与 GM 相同地放在 catch 之后，保证不会为未提交的入账发提醒。结果 `attempted` / `offline` 放进 `HttpRouteReply.Reminder`。
   6. **提醒失败**（`RemindAsync` 抛出业务异常）：入账已提交，回复仍为成功，`reminder = "failed"`，映射器记错误日志。这与 GM 的做法（500 + `outcome: unknown`）不同，理由：玩家前端只按 `code` 分支，500 会让玩家以为没到账；原编号重试本来就会走重放。**需协调方确认，见回报。**

9. **响应映射（`PlayerRouteReplyMapper`）。**
   - 成功：`PlayerCeraGrantResult` → 200 `{ receipt, replayed, reminder }`；记录页 → 200 `{ items, total, page, pageSize }`。
   - 失败：写操作集合（本单元为 `PlayerGrantCera`；T-005 加 `PlayerSendMail`）的失败响应为 `{ message, code, outcome }`；`outcome` 为空时按 GM 映射器同样的规则补为 `rejected`（不确定异常补 `unknown`）。非写操作保持 `{ message, code }`。
   - 状态码与 `code` 不变（TASK-03 已实现）：`PortalClosed`→403 `portal_closed`、`FeatureDisabled`→403 `feature_disabled`、`LimitExceeded`→409 `limit_exceeded`、`RequestConflict`→409 `request_conflict`、`Invalid`→400 `invalid`；`unknown` / `auditFailed` / 执行器失败 → 500 `unknown`，`outcome` 原样带出（`auditFailed` 对外显示为 `unknown`）；执行器确认回滚的消息为"未提交，请稍后用原编号重试。"，其余 500 沿用"结果暂未确认，请稍后重试。"。
   - 用操作集合判断，不用枚举范围。

### 1. Data 与 Abstractions

`Data/PlayerPortal/PlayerPortalCera.cs`：

- `PlayerCeraRequest`（第 0 节第 2 项）。
- `PlayerCeraReceipt`（第 0 节第 6 项）。
- `PlayerCeraGrantResult(JsonElement Receipt, bool Replayed)`。
- `PlayerOperationsView(IReadOnlyList<JsonElement> Items, long Total, int Page, int PageSize)`。

`IPlayerPortalDomainServices.cs` 追加：

```csharp
public interface IPlayerPortalCeraDomainService
{
    /// 执行单元：SD-1 全部判定与入账；重放命中时返回原回执。
    ValueTask<PlayerCeraGrantResult> GrantAsync(string? secret, PlayerCeraRequest request, CancellationToken token);
    /// 失败审计：身份来自命令开始时的识别，不再用会话密钥识别（SD-1a）。
    ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, string outcome, string reason, CancellationToken token);
}
```

`IPlayerPortalAccountDomainService` 追加 `ValueTask<PlayerOperationsView> OperationsAsync(string secret, PlayerPortalOperationKind? kind, int page, int pageSize, CancellationToken token);`。

### 2. Application

- `PlayerPortalRuleService` 增加：
  - `(string Currency, int Amount) NormalizeCera(PlayerCeraRequest request)`：`RequestId` 为空 GUID、币种不在两种之内、数量 ≤ 0 → `PlayerPortalException(Invalid, …)`；
  - `string CeraPayloadHash(string currency, int amount)`；
  - `void CheckCeraLimits(PlayerPortalLimits limits, string currency, int amount, long usedToday)`：消息为"单次最多 {上限}。"与"今日剩余 {上限 − 已用}。"（剩余不小于 0）。
- `PlayerPortalCeraDomainService`，依赖 `IPlayerPortalPersistence`、`IPlayerPortalAccessDomainService`、`PlayerPortalRuleService`、`IGmCeraRuleService`、`IAccountWalletDomainService`、`IGmAuditDomainService`、`IDailyResetRuleService`、`IDomainTransactionAccessor`、`TimeProvider`：`GrantAsync` 按第 0 节第 4 项实现；开头 `RequireTransaction`（同 GM）。`RecordFailureAsync` 校验 `outcome` 只能是 `Rejected` / `RolledBack` / `Unknown`。
- `PlayerPortalReceipts`、`PlayerPortalDailyUsage`（第 0 节第 5、6 项）。
- `PlayerPortalAccountDomainService.OperationsAsync`：`AuthorizeAsync`，校验 `page` 1..1000000、`pageSize` 1..100（不合法 → `Invalid`），调用 `portal.OperationsAsync(identity.AccountId, kind, page, pageSize)`，把每条 `ReceiptJson` 解析成 `JsonElement`。
- 命令（`PlayerPortalCommands.cs`）：`GrantPlayerCeraCommand(string Secret, PlayerCeraRequest Request)`、`QueryPlayerOperationsCommand(string Secret, PlayerPortalOperationKind? Kind, int Page, int PageSize)`。处理器按第 0 节第 8 项；记录查询沿用现有 `Write` 帮助方法（与 `/me` 相同的命令事务写法）。处理器构造参数增加 `IPlayerPortalCeraDomainService`、`IGmCeraReminderDomainService`、`ICommandFailureAudit`；如果加在现有处理器里使依赖过多，可把点券两条命令放到新的 `PlayerPortalCeraCommandHandler`，二者择简。
- `PlayerPortalRegistration` 注册 `IPlayerPortalCeraDomainService`。

### 3. HttpApi 与 GmWeb

- `HttpRouteOperation` 末尾追加 `PlayerGrantCera, PlayerOperations`。
- `PlayerPortalEndpoints.Map` 增加两条路由（第 0 节第 1 项）：
  - 加点券解析器：读请求体（第 0 节第 2 项），返回 `new GrantPlayerCeraCommand(PlayerRouteMapping.Secret(c)!, request)`；非 JSON 或超长抛 `HttpRouteInputException(400, "请求字段无效。")`；
  - 记录解析器：`kind`、`page`、`pageSize` 任一重复或取值非法 → `HttpRouteInputException(400, "查询参数无效。")`；`kind` 只认 `cera`、`mail`；其它查询参数（如 `accountId`）忽略。
- `PlayerRouteMapping.cs`：按第 0 节第 9 项扩展 `PlayerRouteReplyMapper`；`Error` 增加可选 `outcome` 参数。

### 4. 测试

`PlayerPortalTestSupport.cs` 增加：

- `SetLimitsAsync(host, ceraPerGrant = 0, ceraPerDay = 0, tokenCeraPerGrant = 0, tokenCeraPerDay = 0, mailGoldPerMail = 0, mailsPerDay = 0)`：直接更新 `player_portal_settings` 行（先确保行存在）；
- `PlayerClient.GrantAsync(csrf, currency, amount, Guid? id = null)`；
- 读取余额、`player_portal_operations` 行、`PlayerGrantCera` 审计行的小工具（`host.ReadAsync` 组合即可）。

`PlayerPortalCeraHttpTests.cs`（每个用例都断言余额，以及 `player_portal_operations` 的行数或内容）：

- **AC-010**：A 加 `Cera` 1000 → 点券 +1000、代币券不变，响应 `receipt` 的公共字段与 `currency`/`amount`/`before`/`after` 正确、不含 `happyTokenCera`；加 `TokenCera` 同理；`amount` 为 0、-1，币种为 `HappyTokenCera`、`Gold`、`cera`（大小写不同）→ 400 `invalid`、余额不变；余额为 2147483000 时加 1000 → 400 `invalid`、余额不变、无操作记录；请求体带 `accountId` 字段 → 400；查询串带 `?accountId=<B>` → 只加给 A。
- **提醒**：用 `configureServices` 替换 `IGmCeraReminderDomainService` 为记录调用的假实现：成功与重放都以 `[A]` 调用、响应 `reminder` 为假实现的返回值；另一用例假实现抛 `IOException` → 200、`reminder = "failed"`、入账一次。再用真实提醒服务跑一次，无在线会话时 `reminder = "offline"`。
- **AC-012**：
  - 同一编号同一内容提交两次 → 第二次 200、`replayed = true`、`receipt` 与首次逐字段相同、余额只加一次、操作记录一行、成功审计一条；
  - 同一编号不同数量、不同币种 → 409 `request_conflict`、`outcome = "rejected"`、不入账、写一条 `Rejected` 审计；
  - 账号 B 用 A 用过的编号 → 正常入账（编号按账号区分）；
  - **成功后用量已满**：每日上限 1000，加 1000 成功；原编号重试 → 200 重放；
  - **成功后子开关已关**：关闭点券子开关后原编号重试 → 200 重放；新编号 → 403 `feature_disabled`；
  - 两次重试都不入账、不加审计。
- **AC-021（点券子开关）**：关闭点券子开关 → 加点券 403 `feature_disabled`、写 `Rejected` 审计；`/me` 与记录接口仍 200，`portal.ceraEnabled = false`。总开关关闭后加点券 → 403 `portal_closed`（不是 500），无审计、不入账。
- **AC-008(a)**：用 `configureServices` 包装 `IPlayerPortalAccessDomainService`（原实现以 `ActivatorUtilities.CreateInstance` 构造），`IdentifyAsync` 在被武装时发出 `Entered` 信号并等待 `Release`（边界调用它，命令内的 `AuthorizeAsync` 不经过这个闸门）。加点券请求通过边界后停在闸门；管理员经 HTTP 关闭总开关；放行 → 403 `portal_closed`、`outcome = "rejected"`，余额不变、无操作记录、无 `PlayerGrantCera` 审计。
- **AC-008(c)**：每日上限 1500，先成功加 1000；闸门改为"等两个请求都通过边界再一起放行"，并发提交两笔不同编号的 500 → 恰好一个 200、一个 409 `limit_exceeded`；余额共 +1500，操作记录 2 行，`Rejected` 审计 1 条。
- **AC-022**：
  - 单次上限 1000：加 1001 → 409 `limit_exceeded`；加 1000 → 200；
  - 每日上限 1500（同上单次上限）：前面被拒的 1001 不计入（否则加 1000 会失败），再加 500 → 200（恰好等于上限），再加 1 → 409；
  - 钱包上限拒绝的请求也不计入：另设每日上限，先触发一次余额上限拒绝，再加满每日上限仍成功；
  - **币种互不影响**（TASK-01 遗留）：`CeraPerDay = 1000`、`TokenCeraPerDay = 1000`：加点券 1000 → 代币券 1000 仍 200 → 点券 1 被拒、代币券 1 被拒；`/me` 的 `today` 为 `{ cera: 1000, tokenCera: 1000 }`；
  - **日切**：用 `GmTestClock`，从 `host.Services` 取 `IDailyResetRuleService`，把时钟推进到下一个 `PeriodStart`；重新登录（会话可能已过期），点券 1 → 200；新记录的 `period_day` 为新的 `PeriodDay`，与旧记录不同；`/me` 的 `today.cera` 为 1；
  - 全部为 0：加 2000000000 → 200（仅受钱包上限约束）。
- **AC-024（点券）**：上述成功与各类拒绝在 `gm_audit` 中的行：`action = PlayerGrantCera`、`actor_kind = player`、`account_id` 为 A、`administrator_name` 为 A 的登录名、`administrator_id` 为空；`GET /api/gm/v1/audit?actor=player&action=PlayerGrantCera` 返回它们且 `total` 正确；总开关关闭、未登录、请求解析失败的请求不产生审计。
- **执行器失败**：
  - 仿 `GmCeraHttpTests.UncertainPostCommitRecoversOriginalKeyWithoutSecondCredit`：包装 `IGmAuditDomainService`，在 `PlayerGrantCera / Succeeded` 记录写入时登记一个提交即失败的事务参与者 → 500 `unknown`、`outcome = "unknown"`；审计中有一条 `Unknown`，带 A 的账号 ID；原编号重试 → 200 重放，余额只加一次。
  - 用触发器让 `PlayerGrantCera` 审计全部写入失败 → 500 `unknown`、余额不变。

`PlayerPortalRecordsHttpTests.cs`（**AC-013 点券部分**）：

- A 依次成功加点券 3 笔（每笔之间推进时钟 1 秒）、被拒 1 笔，B 成功 1 笔；A 的记录只含 A 的 3 笔，按时间倒序，`total = 3`，每条含五个公共字段且 `result = "成功"`；B 的记录不出现在 A 的结果里；
- `pageSize=2` 分两页，`total` 与内容正确；`kind=cera` 返回 3 笔；`kind=mail` 返回空、`total = 0`；
- `kind=x`、重复的 `kind`、`page=0`、`pageSize=101` → 400 `invalid`；`?accountId=<B>` 被忽略；
- 记录项与加点券响应里的 `receipt` 逐字段相同；
- 点券子开关关闭时记录接口 200；总开关关闭时 403 `portal_closed`。

`GmGameplayRouteTests.cs`：`gm:` 命令数 48 → 50，`HttpGameplayOperation` 数 46 → 48，`EnqueuesItself` 数保持 4；例外清单不变。三个数字在结果中列出。

`Application.Tests/PlayerPortal/PlayerPortalCeraRuleTests.cs`：`NormalizeCera`（两种币种通过，其余币种、0、负数、空编号被拒）、`CeraPayloadHash`（币种或数量不同则不同、相同则相同）、`CheckCeraLimits`（0 不限、恰好等于上限通过、超出 1 被拒、两种币种取各自的上限字段）。

### 5. 执行顺序

1. Data 与 Abstractions → `HttpRouteOperation` 追加 → 规则服务与其 Application 测试（先写失败用例）。
2. `PlayerPortalDailyUsage`、`PlayerPortalReceipts`，`MeAsync` 改用共用读取，先跑 TASK-03 的 `/me` 用例确认不变。
3. 点券领域服务、记录查询、命令与注册。
4. GmWeb 路由与映射器。
5. Host 测试逐个转绿；更新 `GmGameplayRouteTests`。
6. 文档：`Document/gm-web-admin.md`「玩家自助后台」补 `POST cera/grant`（请求体、判定顺序、重放、限额按币种分开计数并按游戏日切、响应 `{ receipt, replayed, reminder }`、失败带 `outcome`）、`GET operations`（参数与回执公共字段），以及审计中 `PlayerGrantCera` 的成功与失败记录。
7. 检查（长命令放后台）：
   - `dotnet build Server/86JP.Server.slnx`；
   - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~PlayerPortal|FullyQualifiedName~GmGameplayRouteTests"`；
   - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~PlayerPortal"`；
   - 回归：
     - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~Dfo.Server.Host.Tests.Gm|FullyQualifiedName~HttpAuditedFailureBoundary"`；
     - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~Gm"`。
   - Host.Tests 满载时可能因就绪预算超时偶发失败（`Document/BuildAndTest.md`），遇到时单独重跑失败用例确认。

### 6. 变异核对（核对后恢复原代码，在结果中记录）

- 点券用量改为不传币种（`UsageAsync(..., null, ...)`，绕过 `PlayerPortalDailyUsage` 的非空参数）：币种互不影响的用例应当失败。
- 把重放判定挪到子开关与限额之后：AC-012"成功后用量已满 / 子开关已关"用例应当失败。
- 每日上限判定改为 `已用 + 数量 >= 上限`：AC-022"恰好等于上限通过"的用例应当失败。

## Result

结论：按 Revision 1 完成，五项本地决定均已经协调方确认。未提交，改动都留在工作区。

### 改动

- Data：新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalCera.cs`，含 `PlayerCeraRequest`（拒绝未声明字段，三个字段必填）、`PlayerCeraReceipt`、`PlayerCeraGrantResult`、`PlayerOperationsView`。
- Abstractions：`IPlayerPortalDomainServices.cs` 新增 `IPlayerPortalCeraDomainService`（`GrantAsync`、`RecordFailureAsync`）和 `IPlayerPortalAccountDomainService.OperationsAsync`。
- Application（`PlayerPortal/`）
  - 新建 `PlayerPortalCeraDomainService`：按第 0 节第 4 项的顺序判定并入账。
  - 新建 `PlayerPortalDailyUsage`：点券用量的 currency 参数为非空 `string`，并做非空校验。
  - 新建 `PlayerPortalReceipts`：用 Web 命名序列化与解析回执。
  - `PlayerPortalRuleService` 增加 `NormalizeCera`、`CeraPayloadHash`、`CheckCeraLimits`。
  - `PlayerPortalAccountDomainService`：`/me` 的今日用量改用共用读取，新增记录查询。
  - `PlayerPortalCommands`：新增 `GrantPlayerCeraCommand`、`QueryPlayerOperationsCommand`；点券放在独立的 `PlayerPortalCeraCommandHandler`（准备 / 执行 / 失败审计 / 提醒）；注册时声明 `AuditedFailure`。
  - `PlayerPortalRegistration` 增加一行注册。
- HttpApi：`HttpRouteOperation` 末尾追加 `PlayerGrantCera, PlayerOperations`。
- GmWeb
  - `PlayerPortalEndpoints`：新增两条路由。加点券请求体限 4 KiB，数字严格解析，`requestId` 由私有转换器解析，拒绝空 GUID。记录接口校验查询参数。
  - `PlayerRouteMapping`：写操作集合（目前只有 `PlayerGrantCera`）的失败响应带 `outcome`（`auditFailed` 对外显示为 `unknown`）。`reminderFailed` 且带回执时按成功作答（`reminder: "failed"`）并记错误日志。执行器确认回滚时的消息为"未提交，请稍后用原编号重试。"。
- 测试
  - Host.Tests 新建 `PlayerPortalCeraHttpTests`（12 例）、`PlayerPortalRecordsHttpTests`（1 例）。
  - `PlayerPortalTestSupport` 增加 `SetCeraLimitsAsync`、`BalanceAsync`、`PlayerClient.GrantAsync`。
  - `GmGameplayRouteTests` 计数：`gm:` 命令 48→50，`HttpGameplayOperation` 46→48，`EnqueuesItself` 保持 4。
  - Application.Tests 新建 `PlayerPortal/PlayerPortalCeraRuleTests`（17 例）。
- 文档：`Document/gm-web-admin.md`「玩家自助后台」补充加点券、记录、`outcome` 与 `PlayerGrantCera` 审计口径。

### 与计划的出入

- 动作名 `PlayerGrantCera` 定义为 `PlayerPortalCeraDomainService.Action` 常量，没有加到 `GmAuditActors`：`GmAdministration.cs` 不在本单元的所有权范围内。`GmAuditActors.PlayerActions` 早已包含这个名字，审计搭配校验照常生效。
- 计划第 0 节第 2 项把空 GUID 的 `requestId` 归为解析失败，实现放在 GmWeb 的转换器里；`NormalizeCera` 中的同一检查只作防线。
- 提醒失败的回复不新增字段，而是复用 `HttpRouteReply`：`Value` 仍是回执，`Outcome` 为 `reminderFailed`，`Error` 为原异常。由映射器按成功作答并记日志。
- `PlayerPortalAudit.cs` 没有改动，成功与失败审计都直接用已有的 `PlayerPortalAudit.Player` 构造。

### 验收对照

| AC / 遗留 | 用例 |
|---|---|
| AC-010 | `Adding_cera_or_token_cera_credits_only_that_currency_and_refuses_anything_else`：只加对应币种；回执字段齐全且不含 `happyTokenCera`；0、负数、其它币种、大小写不符、钱包上限都返回 400；请求体带 `accountId` 返回 400，查询串的 `accountId` 被忽略；五种解析失败不写审计，六种识别后的拒绝各写一条 `Rejected` |
| AC-010（提醒） | `The_reminder_goes_to_the_own_account_and_a_failed_one_leaves_the_grant_a_success`；无在线会话时为 `offline`（同上一例） |
| AC-022（全 0） | `Zero_limits_leave_only_the_wallet_rule` |
| AC-012 | `A_request_id_succeeds_once_and_replays_even_after_the_limit_or_the_switch_closes`：额度用完与子开关关闭后原编号都返回原回执（原始 JSON 逐字相同）；不同数量、不同币种返回 409；另一账号可以用同一编号 |
| AC-021（点券） | `Closing_the_cera_switch_refuses_only_grants_and_a_closed_portal_answers_portal_closed` |
| AC-008(a) | `Closing_the_portal_after_a_grant_passed_the_boundary_refuses_it_inside_the_cmd`：响应 403 `portal_closed` 且带 `outcome`，证明是命令内判出的；不入账、无审计 |
| AC-008(c) | `Two_concurrent_grants_never_pass_the_daily_limit_together` |
| AC-022 | `Limits_count_only_succeeded_grants_and_a_new_game_day_starts_again`：被拒的 1001 不计入；恰好达到上限可通过；日切后重新计数，`period_day` 分成两个游戏日，`/me` 的今日用量归 1 |
| TASK-01 遗留 | `Each_currency_has_its_own_daily_limit_and_a_wallet_refusal_counts_nothing` |
| AC-024（点券） | `Player_grants_are_audited_as_the_player_and_filtered_by_actor`：未登录请求不写审计；`actor=player&action=PlayerGrantCera` 只返回这两条 |
| 执行器失败 | `An_unconfirmed_commit_is_audited_under_the_player_and_the_same_id_replays`：审计写为 `Unknown`，带账号 1；原编号重试走重放，只入账一次。`An_unwritable_audit_answers_unknown_and_credits_nothing` |
| AC-013（点券） | `Records_list_only_the_own_succeeded_operations_newest_first` |

### 检查结果

- `dotnet build Server/86JP.Server.slnx`：0 警告、0 错误。行尾归一后又构建了一次，结果相同。
- 新增测试：Application.Tests `~PlayerPortal` 通过 17。Host.Tests `PlayerPortalCera|PlayerPortalRecords|GmGameplayRouteTests` 通过 20，跳过 1（`GmGameplayRouteTests` 中原本就标了跳过的用例）。
- 回归
  - Host.Tests `Dfo.Server.Host.Tests.Gm*|PlayerPortal*|HttpAuditedFailureBoundary`：通过 196，失败 0，跳过 11（都是原本就标了跳过、需要真实 PVF 的用例）。
  - Application.Tests `~Gm|~PlayerPortal`：通过 51，失败 0，跳过 11（原有跳过）。
  - 行尾归一后复跑 Host.Tests `PlayerPortal|GmGameplayRouteTests|GmPlayerPortal`：通过 37，跳过 1。

### 变异核对

三项都已恢复原代码（与备份逐字比对一致），并重新构建。

- 点券用量改为不传币种（`UsageAsync(..., null, ...)`）：`Each_currency_has_its_own_daily_limit…` 失败。
- 把重放判定挪到子开关与限额之后：`A_request_id_succeeds_once_and_replays_even_after_the_limit_or_the_switch_closes` 失败。
- 每日上限判定改为 `>=`：Host 中 4 例失败（AC-012、币种独立、AC-008(c)、AC-022），Application 中"恰好等于上限"的 2 例失败。

### 其它

- 执行中用脚本编辑时，曾把 7 个原为 LF 的已跟踪文件写成 CRLF，已全部改回 LF。`git ls-files --eol` 显示改动文件均为 `w/lf`，新建文件不含 CR。

### 留给后续单元

- T-005 的邮件回执必须带公共字段 `requestId, kind("mail"), committedAt, summary, result`，序列化走 `PlayerPortalReceipts.Serialize`，记录接口无需改动。重放判定需要校验 `Kind == Mail`，点券侧已对"编号被邮件占用"返回 `request_conflict`。每日封数用 `PlayerPortalDailyUsage.MailsAsync`。把 `PlayerSendMail` 加入 `PlayerRouteReplyMapper.Writes`，并更新 `GmGameplayRouteTests` 计数。
- T-006：加点券响应为 `{ receipt, replayed, reminder }`；失败时按 `code` 分支，`outcome` 只用于提示。`reminder: "failed"` 时仍按成功处理。

## Result Review

### Independent Quick Review R1

- 审查对象：TASK-04 契约 Revision 1、详细计划与上方 `## Result`（执行轮 1）；基线 `d7bfa8144` 之上的未提交改动（11 个已跟踪文件 +260/−24，另 8 个新建文件/目录，均在契约所有权范围内）。
- 适用标准：reviewing-work 的 Task Unit + Code（本地范围）；上位依据 plan.md SD-1、SD-1a、SD-8（点券）、SD-9，spec.md RQ-007/010/012/013/022/024、AC-008(a)(c)/010/012/013/021/022/024，RQ-051。
- 审查者独立于该单元的计划与实现。

```yaml
kind: rks-review
verdict: approved
```

核查结论（按"多入账 / 误拒 / 越权"）：

- **判定顺序与原子性**：`PlayerPortalCeraDomainService.GrantAsync` 在执行单元内依次为 `RequireTransaction` → `AcquireWriteAsync` → `AuthorizeAsync`（总开关、会话）→ 规范化 → 重放 → 子开关 → 限额（`now`/`day` 只取一次，写入复用）→ 钱包 `Decide` + `TryApplyAsync` → 存记录 → 成功审计，全部在同一个命令事务内；准备单元只取审计身份，执行单元完整重判。命令在串行执行器上运行，关闭开关的命令与已排队的加点券命令不会交错。AC-008(a)（闸门停在边界后关总开关 → 命令内 403 `portal_closed`，无入账、无审计）与 AC-008(c)（1500 上限、已用 1000、并发两笔 500 → 恰一笔成功，余额 +1500）用例存在且通过。
- **重放**：先于子开关与限额；`Kind == Cera` 且哈希相同才返回存储回执，否则（含编号被邮件占用）`request_conflict`；哈希只含规范化后的币种与数量，主键含账号，B 可复用 A 的编号。AC-012 用例覆盖"额度用完 / 子开关关闭后原编号仍重放、原始 JSON 逐字相同、不入账不加审计"。
- **币种隔离与日切**：`PlayerPortalDailyUsage.CeraAsync` 的币种为非空 `string` 并校验；持久化 SQL 按 `account_id + kind + period_day + currency` 过滤；`/me` 与限额判定共用同一读取。限额用 `long` 计算、`>` 判定（恰好等于上限通过），0 = 不限。日切用例推进到下一 `PeriodStart` 后重新计数，`period_day` 分两日。被拒（限额、钱包上限）不计入的用例存在。
- **钱包规则复用 GM**：直接调 `IGmCeraRuleService.Decide`（余额上限拒绝转为 `invalid`）与 `IAccountWalletDomainService.TryApplyAsync`，没有平行规则；未改动任何 GM 规则 / 领域 / 提醒服务与路由（diff 中无 GM 文件），GM 点券回归用例通过（RQ-051）。
- **越权**：两条路由账号只取自会话；请求体 `JsonUnmappedMemberHandling.Disallow` 拒绝 `accountId`，查询串 `accountId` 被忽略（用例断言 B 余额不变）；记录查询 `WHERE account_id=@account` 取自会话身份，用例断言 B 的记录不出现在 A 的结果里。
- **失败审计**：身份来自准备单元的 `PlayerIdentity`，`RecordFailureAsync` 不再读会话（SD-1a）；`PortalClosed` / `Unauthenticated` / 解析失败 / 重放不写；`PlayerPortalException` → `Rejected`，其它业务失败 → `RolledBack`；执行器失败经 `ICommandFailureAudit` 写 `Unknown` / `RolledBack` 后重抛；审计写失败 → `auditFailed`，对外 500 `unknown`。结构与 `GmCeraCommandHandler` 一致。"提交未确认"用例：审计 `Unknown` 带账号 1，原编号重试走重放、只入账一次；"审计全部写失败"用例：500、不入账。
- **提醒**：在 catch 之后调用，未提交的入账不会提醒；业务异常时仍按成功作答（`reminder: "failed"`）并记错误日志（本地决定 8.6，Result 记已经协调方确认）。
- **测试能否暴露错误**：Result 记录的三项变异（不传币种、重放挪后、`>=`）均让对应用例失败；我复核了用例断言与上述实现的对应关系，未发现只靠"能加点券"就能通过的空断言。
- **独立复跑**：`dotnet build Server/86JP.Server.slnx` 0 警告 0 错误；Application.Tests `~PlayerPortal` 17/17 通过；Host.Tests `~PlayerPortal|~GmGameplayRouteTests|~GmCera|~HttpAuditedFailureBoundary` 55 通过、2 跳过（原有跳过）、0 失败。
- **行尾**：`git diff --stat` 与 `--ignore-cr-at-eol` 统计一致（11 files, +260/−24），已跟踪的改动文件 `git ls-files --eol` 均为 `i/lf w/lf`，没有整文件行尾变更。

非阻塞问题（提交前处理）：

1. **Result 的行尾声明与事实不符**："新建文件不含 CR"不成立——`PlayerPortalCeraDomainService.cs`（91/91 行）与 `PlayerPortalCeraHttpTests.cs`（436/436 行）为全 CRLF，其余新建文件为 LF。本工作树 `core.autocrlf=false`，原样提交会以 CRLF 入库，与同目录 LF 文件不一致。不影响正确性；建议提交前把这两个文件转为 LF（或更正 Result 的说法）。
2. `Document/gm-web-admin.md` 的审计口径只列了 `Succeeded` / `Rejected` / `Unknown`，未提执行器确认回滚或非业务拒绝时的 `RolledBack`；可顺手补一句，不阻塞。

残余风险（不阻塞）：提醒在 5 秒超时时抛 `OperationCanceledException`，不属于 `BusinessFailure`，会以 500 作答但入账已提交——与 GM 现有行为一致，原编号重试走重放，不会多入账。
