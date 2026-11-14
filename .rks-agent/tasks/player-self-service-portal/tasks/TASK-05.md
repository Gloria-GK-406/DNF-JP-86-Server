# TASK-05 — 玩家发邮件与玩家物品目录

## Task Unit Contract

- Revision: 1（标识本契约及其详细计划）
- Outcome and references: 交付 Plan T-005：
  - SD-8：从 `GmOfficialMailDomainService.SendAsync` 抽出"为一个收件角色建邮件、创建物品、投递"的共享投递组件，GM 与玩家共用，GM 对外行为不变；
  - SD-8a：玩家物品目录 `GET /api/player/v1/mail/items`、`GET /api/player/v1/mail/items/{id}`、`GET /api/player/v1/items/{id}/icon`，以及协调方裁定补入的 `GET /api/player/v1/items/icon-pack`、`GET /api/player/v1/items/{id}/tooltip`；
  - 玩家发信命令：SD-1 判定顺序、重放、归属裁定、物品字段限制、邮件子开关、单封金币与每日封数限额、成功与失败审计（SD-9）；记录接口出现邮件记录。

  覆盖 RQ-004、RQ-007（邮件）、RQ-011、RQ-012、RQ-013（邮件）、RQ-022（邮件）、RQ-024（邮件）；验收 AC-005、AC-008（邮件的并发与关闭竞态）、AC-011（服务端部分，含在线角色收信提醒路径）、AC-012（邮件，含收件角色删除后原编号重放仍返回原结果）、AC-013（邮件部分）、AC-021（邮件子开关）、AC-022（邮件：单封金币上限、每日封数）、AC-024（邮件）。依据 plan.md 的 SD-1、SD-1a、SD-8、SD-8a、SD-9。同时处理遗留：
  - plan-review R-2：AC-003 动态枚举须包含玩家物品目录路由；玩家目录照 GM 写成执行器外只读路由；
  - plan-review R-3："至少一项附件或正文"在 `GmMailRuleService.Normalize` 填入默认正文之前判定；玩家物品搜索的选择键与允许规则与 GM 邮件一致；
  - TASK-04 留给 T-005 的约定：邮件回执带五个公共字段并用 `PlayerPortalReceipts.Serialize`；重放校验 `Kind == Mail`；每日封数用 `PlayerPortalDailyUsage.MailsAsync`；`PlayerSendMail` 加入 `PlayerRouteReplyMapper.Writes`；更新 `GmGameplayRouteTests` 计数。
- Owned boundary:
  - Data：新建 `Server/src/Dfo.Server.Data/PlayerPortal/PlayerPortalMail.cs`。
  - Abstractions：`Server/src/Dfo.Server.Abstractions/PlayerPortal/IPlayerPortalDomainServices.cs` 追加 `IPlayerPortalMailDomainService`。
  - Application：
    - `Administration/`：新建 `OfficialMailDelivery.cs`；改 `GmOfficialMailDomainService.cs`（`SendAsync` 的逐收件人循环改为调用投递组件，构造参数随之调整）、`AdministrationRegistration.cs`（注册投递组件）。不改 `GmMailRuleService`、`IGmMailRuleService`、GM 请求与回执类型、`GmOfficialMailCommands.cs`。
    - `PlayerPortal/`：新建 `PlayerPortalMailDomainService.cs`；改 `PlayerPortalRuleService.cs`（邮件内容检查、映射、内容哈希、限额）、`PlayerPortalCommands.cs`（发信命令、处理器、注册）、`PlayerPortalRegistration.cs`。
  - HttpApi：`HttpRouteOperation.cs` 只在枚举末尾追加 `PlayerSendMail`。
  - GmWeb：
    - 改 `PlayerPortalEndpoints.cs`（发信路由与请求体解析、五条执行器外只读路由）、`PlayerRouteMapping.cs`（`Writes` 加 `PlayerSendMail`、发信成功响应、执行器外路由的失败映射）；
    - `GmOfficialMailEndpoints.cs` 只把 `ItemFilter` 由 `private` 改为 `internal` 并增加一个可选的错误构造参数（缺省仍抛原来的 `GmMailException`，GM 行为不变）。
  - 测试：
    - `Server/tests/Dfo.Server.Host.Tests/`：新建 `PlayerPortalMailHttpTests.cs`、`PlayerPortalMailCatalogHttpTests.cs`；改 `PlayerPortalTestSupport.cs`（邮件限额设置与发信帮助方法）、`GmGameplayRouteTests.cs`（计数与豁免清单）、`PlayerPortalBoundaryHttpTests.cs`（AC-003 断言新路由在枚举中）、`PlayerPortalRecordsHttpTests.cs`（邮件记录）、`GmItemIconHttpTests.cs`（只把 `WritePack` 改为 `internal` 供复用）。
    - `Server/tests/Dfo.Server.Application.Tests/PlayerPortal/`：新建 `PlayerPortalMailRuleTests.cs`。
  - 文档：`Document/gm-web-admin.md`「玩家自助后台」一节补发信、物品目录接口、邮件回执字段与 `PlayerSendMail` 审计口径。
  - 不改 Persistence（`IPlayerPortalPersistence` 与 `IGmMailPersistence` 已具备所需读写；如确实缺能力，停下记 Blocker）。不改 GM 邮件的路由、请求类型、规则服务与命令处理器，不改 GM 物品与图标路由的授权（RQ-051、SD-8a）。不改点券（T-004 已交付）与前端（T-006）。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 零警告零错误。
  - 本单元新增测试全部通过：Host.Tests `PlayerPortalMail*`（含目录）、`PlayerPortalRecords*`；Application.Tests `PlayerPortalMailRuleTests`。
  - `GmGameplayRouteTests`、`PlayerPortalBoundaryHttpTests` 通过（计数与清单见详细计划第 5 节）。
  - **GM 邮件回归防线（抽取投递组件后必须全部通过）**：Host.Tests `GmOfficialMailHttpTests`、`GmMailAttachmentHttpTests`、`GmRequestedCancellationAuditTests`、`ItemCatalogHttpTests`、`GmItemIconHttpTests`；Application.Tests `GmMailEquipmentOptionsTests`、`HttpRouteMechanismTests`、`MechanismBusinessIndependenceTests`。
  - 回归：Host.Tests 的 `Dfo.Server.Host.Tests.Gm*`、`PlayerPortal*`、`ItemCatalog*`、`HttpAuditedFailureBoundary`；Application.Tests 中名字含 `Gm` 与 `PlayerPortal` 的测试。
  - 变异核对（第 6 节），核对后恢复原代码并在结果中记录。
  - 行尾：`git ls-files --eol` 核对改动的已跟踪文件仍为 `w/lf`，新建文件不含 CR。
- Dependencies: TASK-01（`IPlayerPortalPersistence`、`PlayerPortalOperation`、用量与记录查询）、TASK-03（`AuthorizeAsync`、`PlayerRouteMapping`、`PlayerRouteReplyMapper`、`PlayerPortalAudit`、边界的玩家前缀处理、`GmAuditActors.PlayerActions` 已含 `PlayerSendMail`）、TASK-04（`PlayerPortalReceipts`、`PlayerPortalDailyUsage.MailsAsync`、写操作的 `outcome` 映射、`PlayerPortalCeraHttpTests.BoundaryGate`、记录接口）；均已提交（`c23bbb92d`、`d7bfa8144`、`7f0d58d9b` + `79f7cb4b5`）。
- Result review: required —
  - 抽取投递组件改动的是 GM 官方邮件的发放路径（模块边界与共享实现）：抽错会让 GM 邮件少发、错发或在批次中途部分提交。GM 现有测试是防线，但它们只覆盖已写的情形，抽取后的组件是否与原循环逐行等价需要独立审查。
  - 这是玩家自助发放资产（物品、装备、金币）：判定顺序、重放先于子开关 / 归属 / 限额、"至少一项"先于默认正文、归属裁定三种情形回答一致，任何一处错了都会造成越权发放、多发或误拒；只有特意构造的顺序、并发、删除后重放、跨账号用例才能暴露，"能发信"的功能测试照样通过。
  - 发信接口、回执形状与物品目录是 T-006（发邮件页、记录页、`ItemPicker`）的输入，形状错了会被前端建在上面。

## Detailed Plan

### 0. 本单元先定下的本地决定（T-006 按此使用）

1. **路由**（玩家前缀 `/api/player/v1`）。

   | 方法 | 路径 | 执行方式 | 元数据 | 说明 |
   |---|---|---|---|---|
   | POST | `/mail/send` | 命令（`PlayerRouteMapping.Map`） | `HttpRouteOperation.PlayerSendMail` | 请求体见第 2 项 |
   | GET | `/mail/items` | 执行器外只读 | `PlayerSessionRequired` + `OutsideGameplay("只读 SQLite 的查询：玩家身份识别与物品目录")` | 查询参数与 GM `/api/gm/v1/mail/items` 相同 |
   | GET | `/mail/items/{itemId:int}` | 执行器外只读 | 同上 | 物品详情 |
   | GET | `/items/{itemId:int}/icon` | 执行器外只读 | `PlayerSessionRequired` + `OutsideGameplay("GM 自身的内存状态：GmIconPackStore；玩家身份识别")` | 图标 PNG |
   | GET | `/items/icon-pack` | 执行器外只读 | 同上 | 图标包可用性 `{ available, count, generatedAt, pvfSha256 }`，与 GM 同一响应 |
   | GET | `/items/{itemId:int}/tooltip` | 执行器外只读 | `PlayerSessionRequired` + `OutsideGameplay("PVF 内容源与缓存：物品说明；玩家身份识别")` | 悬停提示，与 GM 同一 `IItemTooltipRuleService` |

   - 执行器外路由照 GM 同类路由的写法（R-2）：边界已做总开关与会话判断，路由内再经领域服务 `AuthorizeCatalogAsync` 判一次总开关、会话与**邮件子开关**（子开关关闭 → 403 `feature_disabled`）。
   - 物品搜索与详情调用 `IItemCatalogDomainService`，选择键取 `IGmMailRuleService.SelectionKey`（即 `"mail"`，允许规则 `PermanentOrdinaryItemRules.Allows`），与 GM 邮件目录完全相同（R-3：搜得到的就发得出去）。
   - 图标与 GM 图标接口同一个 `GmIconPackStore`，不按物品用途过滤（与 GM 一致），缓存头同为 `private, max-age=86400`；不存在 → 404 `not_found`"图标不存在。"。
   - 图标包探测与悬停提示（协调方裁定补入 SD-8a 范围，不升 revision）：授权同上（总开关、会话、邮件子开关），实现与 GM 共用 `GmIconPackStore.Describe` 与 `IItemTooltipRuleService.FindAsync`，响应形状与 GM 相同；提示不存在 → 404 `not_found`"未找到该物品。"，读取失败 → 记日志、500 `unknown`。T-006 的图标组件与悬停提示只需换前缀即可复用。
   - 不改任何 GM 路由与其授权。

2. **请求体。**
   - 类型：`PlayerMailRequest(Guid RequestId, int CharacterId, string Title, string Body, PlayerMailItem[] Items, int Gold)`，`PlayerMailItem(int ItemId, int Count)`；两者都 `JsonUnmappedMemberHandling.Disallow`，全部字段 `JsonRequired`。
   - 解析选项：`JsonSerializerDefaults.Web`、`NumberHandling = Strict`、`RespectNullableAnnotations = true`（`title: null` 等直接是解析失败），`requestId` 用 `PlayerPortalEndpoints` 里已有的私有 `RequestIdConverter`（拒绝空 GUID）。
   - 请求体须为 JSON 且不超过 8 KiB（标题、正文各 ≤ 511 字节、附件 ≤ 10 件，8 KiB 足够）。
   - 解析失败（非 JSON、超长、未声明字段——包括物品上的 `equipment` 及任何装备高级选项字段、缺字段、null、数字越界或非整数、空 GUID）一律 400 `invalid`"请求字段无效。"，**不写审计**（SD-1a：命令里还没有识别出身份）。这就是 RQ-011 / AC-011"附件含装备高级选项字段 → 被拒"的实现点。

3. **玩家层的内容检查与映射**（`PlayerPortalRuleService`，纯函数，在命令内识别身份之后执行，失败为 `PlayerPortalException(Invalid, …)`）：
   - `CheckMailContent(PlayerMailRequest)`，**必须在调用 `GmMailRuleService.Normalize` 之前**（R-3）：
     - `Items` 中有 null 元素 → `Invalid`"请求字段无效。"（映射时会空引用，必须先挡）；
     - `CharacterId <= 0` → `Invalid`"请求字段无效。"（不是一个可能存在的角色，不涉及归属）；
     - "至少一项附件或正文"：`!string.IsNullOrWhiteSpace(Body) || Items.Length > 0 || Gold > 0`，否则 `Invalid`"邮件至少要有正文或附件。"。空白正文按 GM 规则会被换成默认正文，所以与 GM 的"留空"判断用同一个 `IsNullOrWhiteSpace`；只有标题不算正文。
   - `ToMailInput(PlayerMailRequest)` → `new GmMailInput(Title, Body, [CharacterId], Items.Select(x => new GmMailItem(x.ItemId, x.Count)).ToArray(), Gold)`，装备选项一律为 null（SD-8）。
   - 之后调用 `IGmMailRuleService.Normalize(input, sending: true)`（GM 规则不改）；它抛的 `GmMailException` 转成 `PlayerPortalException(Invalid, 原消息)`。标题正文长度、控制字符、附件件数、重复物品、金币非负、显示内容 511 字节都由它判，消息与 GM 相同。
   - `MailPayloadHash(GmMailInput normalized)` = `SHA-256(UTF-8("mail\n" + JsonSerializer.Serialize(normalized)))` 的十六进制大写；只含规范化后的内容（收件角色、标题、规范化后的正文、按原顺序的物品、金币），不含账号。不复用 GM 的 `PayloadHash`，避免两侧哈希定义互相牵连。
   - `CheckMailLimits(PlayerPortalLimits limits, int gold, long mailsToday)`：
     - `MailGoldPerMail > 0 && gold > MailGoldPerMail` → `LimitExceeded`"单封金币最多 {上限}。"；
     - `MailsPerDay > 0 && mailsToday + 1 > MailsPerDay`（`long` 计算）→ `LimitExceeded`"今日邮件已达上限（{上限} 封）。"；
     - 0 表示不限。

4. **发信命令内的判定顺序**（SD-1，全部在执行单元的同一个命令事务里，`PlayerPortalMailDomainService.SendAsync`）：
   1. `RequireTransaction`；`IPlayerPortalPersistence.AcquireWriteAsync`；
   2. `access.AuthorizeAsync(secret)`：总开关（`PortalClosed`）→ 会话（`Unauthenticated`）；
   3. `CheckMailContent` → `ToMailInput` → `Normalize` → `MailPayloadHash`（都不读库）；
   4. **重放**：`FindOperationAsync(account, requestId)`。已有记录时：`Kind == Mail` 且 `PayloadHash` 相同 → 返回存下的回执（`replayed = true`），**不再判子开关、归属、限额，不写审计、不发信**；否则（内容不同或编号已用于点券）→ `RequestConflict`"该编号已用于其它内容；新操作请使用新编号。"（与点券侧同一句）。因此收件角色删除后、子开关关闭后、额度用满后的原编号重试都返回原结果（AC-012）；
   5. 子开关：`settings.MailEnabled == false` → `FeatureDisabled`"发邮件已关闭。"；
   6. **归属**（RQ-004）：`IGmMailPersistence.RecipientsAsync(new GmPlayerFilter(1, 1, CharacterId: id))` 取未删除角色（SQL 已含 `delete_flag=0`），再用 `ICharacterTable.FindOwnerAsync` 复核归属（与 GM `TargetsAsync` 同样的两重检查）。角色不存在、已删除、`AccountId` 不是会话账号、复核不一致，**四种情形抛同一个** `PlayerPortalException(NotFound, "收件角色不存在。")`，响应 404 `not_found` 逐字相同；
   7. 限额：`now = clock.GetUtcNow()`、`day = IDailyResetRuleService.PeriodDay(now)` 只取一次；`CheckMailLimits(settings.Limits, input.Gold, await PlayerPortalDailyUsage.MailsAsync(portal, account, day))`；
   8. 物品：`IGmMailRuleService.PrepareItemsAsync(input, now)`（`GmMailException` → `Invalid`，消息不变，即 AC-011"物品不合法 → 沿用 GM 邮件的同一拒绝"）；
   9. 投递：`OfficialMailDelivery.DeliverAsync(new CharacterId(id), input.Title, input.Body, grants, input.Gold, now + OfficialMailDelivery.Lifetime)`（第 1 节）；
   10. 写入：`SaveOperationAsync(new(account, requestId, Mail, Currency: null, Amount: gold, hash, receiptJson, day, now))`，再写成功审计，返回回执。

   归属放在物品准备之前，保证对他人角色的请求在创建任何物品之前就被拒（AC-005 断言物品表不变）。

5. **回执（`receipt_json`）。**
   - 类型：`PlayerMailReceipt(Guid RequestId, string Kind, DateTimeOffset CommittedAt, string Summary, string Result, int CharacterId, string CharacterName, string Title, string Body, IReadOnlyList<PlayerMailReceiptItem> Items, int Gold, DateTimeOffset ExpiresAt)`，`PlayerMailReceiptItem(int ItemId, int Count, string? Name, string Kind)`（`Kind` 为 GM 预览的 `equipment` / `stackable`）。
   - 公共五字段与点券相同（TASK-04 约定）：`kind = PlayerPortalReceipts.Mail`（`"mail"`），`result = PlayerPortalReceipts.Succeeded`，序列化用 `PlayerPortalReceipts.Serialize`。
   - `Body` 为规范化后的正文（留空时即 GM 默认正文），与实际投递内容一致；不含邮件 ID、装备 UID、账号 ID。
   - `CharacterName`：收件记录 `NameBytes` 严格 UTF-8 解码；缺失或非法时为 `角色 {id}`。
   - `Summary`：`发给 {角色名}：` + 以"、"连接的部分：有物品时 `物品 {种数} 种`，金币 > 0 时 `金币 {gold}`；两者都没有时为 `正文`。例：`发给 小明：物品 2 种、金币 500`。
   - 首次成功与重放都从同一个存储字符串解析回执（RQ-012），记录接口原样返回，不需要改。

6. **审计（SD-9）。** 全部经 `PlayerPortalAudit.Player` 构造，动作 `PlayerSendMail`（定义为 `PlayerPortalMailDomainService.Action` 常量，与点券同样做法），操作者为玩家（账号 ID + 库中登录名），对象 `角色 {characterId}`：
   - 成功：`Succeeded`，原因 `{summary}，邮件 {mailId}，编号 {requestId:N}`；
   - 识别出身份后的业务拒绝（`FeatureDisabled`、`NotFound`、`LimitExceeded`、`RequestConflict`、`Invalid`）：`Rejected`，原因 `未提交：{拒绝消息}，编号 …`。`NotFound` 也照常记录所请求的角色 ID——只有管理员看得到审计，玩家得到的回答仍不区分三种情形；
   - 执行单元中非 `PlayerPortalException` 的业务失败：`RolledBack`，原因 `未提交：存储或规则处理失败，编号 …`；
   - 执行器失败审计机制给出的事实：`Unknown`（"提交状态暂未确认"）或 `RolledBack`；
   - **不写失败审计**：`PortalClosed`、`Unauthenticated`、请求解析失败、重放命中；
   - 失败审计使用准备单元识别出的 `PlayerIdentity`，不再用会话密钥重新识别（SD-1a）；原因受 512 字符约束，拒绝消息都是固定短句或 GM 规则短句。

7. **命令结构**（照 `PlayerPortalCeraCommandHandler`，新建 `PlayerPortalMailCommandHandler`；`SendPlayerMailCommand(string Secret, PlayerMailRequest Request)` 注册时声明 `CommandFailureAuditCapability.AuditedFailure`）：
   1. 准备单元 `prepare-player-mail`（`CommandFailureAuditRole.Preparation`）：`access.AuthorizeAsync(secret)` → `PlayerIdentity`。
   2. 执行单元 `send-player-mail`（`CommandFailureAuditRole.Execution`）：`mail.SendAsync(secret, request)`，即第 4 项全部判定与写入；RQ-007 的原子性只依赖执行单元。
   3. 业务失败：按第 6 项决定是否写失败审计，写在独立写单元 `record-player-mail-failure`（5 秒超时，`mail.RecordFailureAsync(identity, requestId, characterId, outcome, reason)`，内部 `RequireTransaction` + `AcquireWriteAsync` + 审计）；审计写入失败 → `outcome = "auditFailed"`；否则 `rejected`（`PlayerPortalException`）或 `rolledBack`（其它）。
   4. 执行器失败：已识别身份时经 `ICommandFailureAudit.RecordAsync` 写 `Unknown` / `RolledBack` 后重抛；未识别身份时直接重抛。
   5. 成功（含重放）直接返回结果。**没有同步提醒步骤**：在线角色的新邮件提醒由 `MailboxDomainService` 投递时入队的 `MailCreated` 领域事件在提交后经 `MailCreatedNotificationSubscriber` 发出，与 GM 邮件同一路径（RQ-011"沿用 GM 邮件"）；未提交的投递不会发事件，重放不投递也就不会重复提醒。
   - 点券与邮件两个处理器的失败审计流程相同；执行者可以把共同部分提成私有帮助方法，前提是点券处理器行为不变（`PlayerPortalCeraHttpTests` 全部通过），否则保持两份。

8. **响应映射（`PlayerRouteReplyMapper`）。**
   - `Writes` 加入 `PlayerSendMail`，失败带 `outcome`（规则同 TASK-04）。
   - 成功：`PlayerMailSendResult(JsonElement Receipt, bool Replayed)` → 200 `{ receipt, replayed }`。不带 `reminder`：提醒在提交后由领域事件发出，请求内无从得知（GM 邮件对此固定回答 `unconfirmed`，玩家前端不需要这个字段）。
   - 新增 `internal static Task<IResult> PlayerRouteMapping.OutsideAsync(HttpContext, Func<Task<IResult>>)` 给五条执行器外路由用：`PlayerPortalException` → 与命令路由相同的 `code`（复用映射器里按 `Reason` 的分支，改为 `internal static`）；`HttpRouteInputException` → 400 `invalid`；`KeyNotFoundException` → 404 `not_found`"未找到该物品。"；`ArgumentException` → 400 `invalid`（原消息，如"此物品不支持当前用途。"）；`OperationCanceledException` 照常向上抛；其它 → 记错误日志、500 `unknown`。
   - 物品查询参数：调用 `GmOfficialMailEndpoints.ItemFilter(context, message => new HttpRouteInputException(400, message))`；该方法改为 `internal`、增加可选的错误构造参数，缺省仍构造原来的 `GmMailException(Invalid, message)`，GM 调用点不传参数、行为不变。

### 1. 共享投递组件（SD-8）

新建 `Application/Administration/OfficialMailDelivery.cs`：

```csharp
/// 为一个收件角色建一封系统邮件、创建附件物品并投递；GM 官方邮件与玩家发信共用。必须在命令事务内调用。
internal sealed class OfficialMailDelivery(IItemLifecycleDomainService lifecycle, IMailboxDomainService mailbox, IGmMailPersistence persistence)
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    internal async ValueTask<OfficialMailDelivered> DeliverAsync(CharacterId recipient, string title, string body,
        IReadOnlyList<GmMailPreparedItem> grants, int gold, DateTimeOffset expiresAt, CancellationToken token);
}
internal sealed record OfficialMailDelivered(long MailId, int[] EquipmentUids);
```

- 方法体**逐行搬移** `GmOfficialMailDomainService.SendAsync` 中 `foreach (var target in targets)` 的循环体：`MailBuilder.FromSystem(...).WithText(...).ExpiresAt(...)`、按 `grant.Preview.Kind == "equipment"` 逐件 `lifecycle.CreateAsync` 并 `AddEquipment`、堆叠物一次 `CreateAsync` 并 `AddStackable`、`gold > 0` 时 `AddGold`、`PreparedMailAttachment` 的组装、`mailbox.SendPreparedAsync`、`persistence.AssertMailWireRangeAsync`。顺序与异常不变；保留原注释（固定品质种子）。
- GM `SendAsync` 改为：`var expires = at + OfficialMailDelivery.Lifetime;`，循环内 `var delivered = await delivery.DeliverAsync(new CharacterId(target.CharacterId), input.Title, input.Body, grants, input.Gold, expires, token); sent.Add(new(Recipient(target), target.AccountId, delivered.MailId, delivered.EquipmentUids));`。其余（授权、重放、`TargetsAsync`、账号比对、回执、操作记录、审计）一字不改。构造参数去掉不再直接使用的 `IMailboxDomainService`、`IItemLifecycleDomainService`，加 `OfficialMailDelivery`。
- `AdministrationRegistration` 用 `TryAddSingleton<OfficialMailDelivery>()` 注册（与 GM 邮件领域服务同为单例）。
- 抽取做成第一步，先跑 GM 邮件回归防线全部转绿，再写玩家侧（第 5 节）。

### 2. Data 与 Abstractions

`Data/PlayerPortal/PlayerPortalMail.cs`：`PlayerMailItem`、`PlayerMailRequest`（第 0 节第 2 项）、`PlayerMailReceipt`、`PlayerMailReceiptItem`（第 0 节第 5 项）、`PlayerMailSendResult(JsonElement Receipt, bool Replayed)`。

`IPlayerPortalDomainServices.cs` 追加：

```csharp
/// 玩家给自己账号下的角色发邮件（RQ-011），以及发信用的物品目录（SD-8a）。
public interface IPlayerPortalMailDomainService
{
    /// 执行器外只读：总开关、会话、邮件子开关。
    ValueTask<PlayerIdentity> AuthorizeCatalogAsync(string? secret, CancellationToken token);
    ValueTask<ItemCatalogPage> SearchItemsAsync(string? secret, ItemCatalogFilter filter, CancellationToken token);
    ValueTask<ItemCatalogDetails> ItemDetailsAsync(string? secret, int itemId, CancellationToken token);
    /// 执行单元：SD-1 全部判定与投递；成功过的编号同内容时返回原回执，先于子开关、归属与限额。
    ValueTask<PlayerMailSendResult> SendAsync(string? secret, PlayerMailRequest request, CancellationToken token);
    /// 失败审计：身份来自命令开始时的识别（SD-1a）。
    ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, int characterId, string outcome, string reason, CancellationToken token);
}
```

### 3. Application

- `PlayerPortalRuleService` 增加 `CheckMailContent`、`ToMailInput`、`MailPayloadHash`、`CheckMailLimits`（第 0 节第 3 项）。
- `PlayerPortalMailDomainService`，依赖 `IPlayerPortalPersistence`、`IPlayerPortalAccessDomainService`、`PlayerPortalRuleService`、`IGmMailRuleService`、`IGmMailPersistence`（只用 `RecipientsAsync`）、`ICharacterTable`、`IItemCatalogDomainService`、`OfficialMailDelivery`、`IGmAuditDomainService`、`IDailyResetRuleService`、`IDomainTransactionAccessor`、`TimeProvider`：
  - `AuthorizeCatalogAsync`：`access.AuthorizeAsync(secret)`，再读设置，`MailEnabled == false` → `FeatureDisabled`"发邮件已关闭。"；
  - `SearchItemsAsync` / `ItemDetailsAsync`：先 `AuthorizeCatalogAsync`，再 `catalog.SearchAsync(filter, gmMailRules.SelectionKey)` / `catalog.ItemAsync(itemId, gmMailRules.SelectionKey)`；
  - `SendAsync`：第 0 节第 4 项；
  - `RecordFailureAsync`：`outcome` 只能是 `Rejected` / `RolledBack` / `Unknown`。
- `PlayerPortalCommands.cs`：`SendPlayerMailCommand`、`PlayerPortalMailCommandHandler`（第 0 节第 7 项）、注册 `AddInternalCommandHandler<SendPlayerMailCommand, HttpRouteReply, PlayerPortalMailCommandHandler>(failureAudit: CommandFailureAuditCapability.AuditedFailure)`。
- `PlayerPortalRegistration` 注册 `IPlayerPortalMailDomainService`。

### 4. HttpApi 与 GmWeb

- `HttpRouteOperation` 末尾追加 `PlayerSendMail`。
- `PlayerPortalEndpoints.Map`：
  - `PlayerRouteMapping.Map(group, "POST", Prefix + "/mail/send", HttpRouteOperation.PlayerSendMail, async c => new SendPlayerMailCommand(Secret(c)!, await ReadMailAsync(c)))`；`ReadMailAsync` 照 `ReadCeraAsync` 写（第 0 节第 2 项），解析失败抛 `HttpRouteInputException(400, "请求字段无效。")`；
  - 五条执行器外路由（第 0 节第 1 项），各自 `group.MapGet(...).WithMetadata(new PlayerSessionRequired(), new OutsideGameplay(...))`，处理函数包在 `PlayerRouteMapping.OutsideAsync` 里；图标路由注入 `GmIconPackStore`，先 `AuthorizeCatalogAsync` 再取图标，`itemId <= 0` 或取不到 → 404 `not_found`。
- `PlayerRouteMapping.cs`：第 0 节第 8 项。
- `GmOfficialMailEndpoints.cs`：只改 `ItemFilter` 的可见性与可选参数（第 0 节第 8 项末条）。

### 5. 测试

`PlayerPortalTestSupport.cs` 增加：

- `SetMailLimitsAsync(host, mailGoldPerMail = 0, mailsPerDay = 0)`（照 `SetCeraLimitsAsync`）；
- `PlayerClient.SendMailAsync(csrf, characterId, title = "", body = "测试", items = null, gold = 0, Guid? id = null)`，`items` 为 `object[]`，便于构造带多余字段的物品；
- 读取 `mailbox_messages` / `mailbox_attachments` / `items` 行数与 `PlayerSendMail` 审计行的小工具（`host.ReadAsync` 组合即可）。

数据约定：账号 A（id 1，"alice"，角色 a1 = 11、a2 = 12 已删除），账号 B（id 2，"bob"，角色 b1 = 21），不存在的角色 9999。物品用测试内容中 GM 邮件测试已在用的 ID：堆叠物 `3`、装备 `27312`；不支持的物品用 GM `UnsupportedAttachmentsAreRejectedBeforeAnyAssetIsCreated` 已用的某一组参数。

`PlayerPortalMailHttpTests.cs`（每个用例断言邮箱与物品表行数，必要时断言 `player_portal_operations`）：

- **AC-011**：
  - A 给 a1 发「物品 3 ×2 + 金币 500」→ 200，`replayed = false`；a1 邮箱新增一封系统邮件，附件为堆叠物 3 ×2 与金币 500（`mailbox_attachments`）；回执五个公共字段齐全，`kind = "mail"`、`characterId`、`characterName`、`items`、`gold`、`body`、`expiresAt`（= `committedAt + 30 天`）正确，`summary = "发给 {a1 名}：物品 1 种、金币 500"`；
  - 装备 27312 ×1：物品核心的属性种子为固定最上级值（与 GM 测试同一断言 `999999998`），证明走的是同一套 GM 规则；
  - 物品带 `equipment` 字段、带任意其它字段（如 `upgrade`）、请求体带 `accountId` → 400 `invalid`，不写审计、不建邮件；
  - 不支持的物品 → 400 `invalid`，`message` 与同一内容经 GM `/api/gm/v1/mail/send` 得到的 `message` 逐字相同；写一条 `Rejected` 审计，物品表不变；
  - 标题或正文超过 511 字节、附件 11 件 → 400 `invalid`，消息与 GM 相同；
  - **在线收信提醒路径**：用 `configureServices` 把 `INotificationBus` 替换为记录调用的实现（照 GM 的 `ThrowingNotifications`，但不抛出），并用 `GmPlayerDirectoryHttpTests.ConnectAsync` / `Publish` 让 a1 处于在线（`/me` 中 `isOnline = true`）；发信后 `DomainEventDispatcher.DrainOnceAsync` + 执行器落定 → 恰好一条 `MailDelivered`，受众为 `TheseCharacters([a1])`；原编号重放后再落定，仍只有一条。
- **R-3 / RQ-011 至少一项**：
  - 正文 `""` 与 `"   "`、无物品、金币 0 → 400 `invalid`"邮件至少要有正文或附件。"，写 `Rejected` 审计，不建邮件（证明判定在默认正文填入之前）；
  - 只有标题、正文为空 → 同样被拒；
  - 正文为空、金币 100 → 200，存下与投递的正文都是 GM 默认正文"此为官方邮件，无需回复。"。
- **AC-005**：A 对 b1、a2（已删除）、9999 发信 → 三个响应状态码均为 404、`code = "not_found"`，响应体字符串逐字相同；所有邮箱、物品表、操作记录不变；三条 `Rejected` 审计，对象分别为 `角色 21` / `角色 12` / `角色 9999`。另测 `characterId = 0` → 400 `invalid`。
- **AC-012**：
  - 同一编号同一内容两次 → 第二次 200、`replayed = true`、`receipt` 原始 JSON 与首次逐字相同；邮件一封、操作记录一行、成功审计一条；
  - 同一编号改金币、改物品顺序、改收件角色（A 的另一角色）→ 409 `request_conflict`、`outcome = "rejected"`，不发信，写 `Rejected` 审计；
  - 编号已被 A 的加点券用过 → 发信 409 `request_conflict`；
  - 账号 B 用 A 用过的编号给 b1 发信 → 正常成功；
  - **收件角色删除后重放**：成功后 `UPDATE characters SET delete_flag=1 WHERE character_id=11`，原编号同内容重试 → 200 重放，邮件仍一封；新编号 → 404 `not_found`；
  - **子开关关闭后、每日封数用满后重放**：同样返回 200 重放；
  - 以上重试都不投递、不加审计。
- **AC-021（邮件子开关）**：关闭邮件子开关 → 发信 403 `feature_disabled`、写 `Rejected` 审计；`/me`、记录接口 200，`portal.mailEnabled = false`；加点券仍 200。总开关关闭后发信 → 403 `portal_closed`（不是 500），无审计、不发信。
- **AC-008（邮件）**，复用 `PlayerPortalCeraHttpTests.BoundaryGate`：
  - (a) 发信请求通过边界后停在闸门，管理员经 HTTP 关闭总开关，放行 → 403 `portal_closed` 且带 `outcome`（证明是命令内判出），不发信、无审计；
  - (a′) 同样的竞态改为管理员只关闭邮件子开关 → 403 `feature_disabled`、`Rejected` 审计、不发信；
  - (c) `MailsPerDay = 2`，先成功一封；闸门等两个请求都通过边界再一起放行，并发两封不同编号 → 恰好一个 200、一个 409 `limit_exceeded`；邮件共 2 封、操作记录 2 行、`Rejected` 审计 1 条。
- **AC-022（邮件）**：
  - `MailGoldPerMail = 500`：金币 501 → 409 `limit_exceeded`；500 → 200；
  - `MailsPerDay = 2`：被拒的那封不计入；再成功一封（共 2）→ 第三封 409；物品不合法被拒的请求同样不计入；
  - 加点券成功不占邮件封数，`/me` 的 `today.mails` 与限额判定一致；
  - **日切**：`GmTestClock` 推进到下一个 `PeriodStart`，重新登录后再发一封 → 200；新记录 `period_day` 不同；`today.mails = 1`；
  - 全 0：金币 2000000000 → 200（只受 GM 既有规则约束）。
- **AC-024（邮件）**：上述成功与各类拒绝在 `gm_audit` 中：`action = PlayerSendMail`、`actor_kind = player`、`account_id` 为 A、`administrator_name` 为 A 的登录名、`administrator_id` 为空；`GET /api/gm/v1/audit?actor=player&action=PlayerSendMail` 返回它们且 `total` 正确；总开关关闭、未登录、解析失败、重放命中不产生审计。
- **执行器失败**（照 `PlayerPortalCeraHttpTests` 的两个用例）：
  - 包装 `IGmAuditDomainService`，在 `PlayerSendMail / Succeeded` 写入时登记提交即失败的事务参与者 → 500 `unknown`、`outcome = "unknown"`；审计有一条带 A 账号的 `Unknown`；原编号重试 → 200 重放，邮件只有一封；
  - 触发器让 `mailbox_attachments` 插入失败 → 500、`outcome = "rolledBack"`，邮件、物品、操作记录全部回滚，写一条 `RolledBack` 审计。

`PlayerPortalMailCatalogHttpTests.cs`（SD-8a）：

- 玩家 `GET mail/items`（无参数、`itemId=27312`、`itemId=<GM 目录排除的物品>`）与管理员 `GET /api/gm/v1/mail/items` 同参数的响应 JSON 相同（R-3：同一选择键与允许规则）；`mail/items/27312` 与 GM 详情相同；被排除的物品详情 → 400 `invalid`，不存在的物品 → 404 `not_found`；查询参数重复或非法 → 400 `invalid`；
- 图标：配置 `GmWeb:IconPack`，用 `GmItemIconHttpTests.WritePack` 写包 → 玩家 `items/27312/icon` 200、`image/png`、`Cache-Control: private, max-age=86400`；不存在的图标 → 404 `not_found`；
- 图标包探测：玩家 `items/icon-pack` 与 GM 同名接口响应相同（无包时 `available = false`，有包时数量等字段一致）；悬停提示：玩家 `items/{id}/tooltip` 与 GM 同名接口响应相同，不存在的物品 → 404 `not_found`；
- 邮件子开关关闭 → 五条路由都 403 `feature_disabled`；总开关关闭 → 403 `portal_closed`；未登录 → 401 `unauthenticated`；
- 不放宽 GM 接口：玩家会话访问 GM 的 `mail/items`、`mail/items/{id}`、`items/{id}/icon` 仍 401（`PlayerPortalBoundaryHttpTests` 已覆盖，这里不重复）。

`PlayerPortalBoundaryHttpTests.Neither_session_opens_the_other_prefix`（AC-003）：玩家路由枚举里加断言包含 `POST /api/player/v1/mail/send`、`GET /api/player/v1/mail/items`、`GET /api/player/v1/mail/items/{itemId:int}`、`GET /api/player/v1/items/{itemId:int}/icon`、`GET /api/player/v1/items/icon-pack`、`GET /api/player/v1/items/{itemId:int}/tooltip`；GM 路由的必含清单补上 `GET /api/gm/v1/items/icon-pack`、`GET /api/gm/v1/items/{itemId:int}/tooltip`；动态循环会自动让它们接受"GM 会话 → 401 `unauthenticated`"检查。

`PlayerPortalRecordsHttpTests.cs`（**AC-013 邮件部分**）新增一例：A 成功加点券 1 笔、发信 2 笔（之间推进时钟），被拒发信 1 笔；B 发信 1 笔 → A 的记录 `total = 3`，按时间倒序；`kind=mail` 只返回 A 的 2 封、每条五个公共字段齐全且与发信响应的 `receipt` 逐字段相同；`kind=cera` 只返回点券；B 的邮件不出现。

`GmGameplayRouteTests.cs`：`gm:` 命令数 50 → 51，`HttpGameplayOperation` 数 48 → 49，`EnqueuesItself` 保持 4；`Exempt` 按 ordinal 序插入 `GET /api/player/v1/items/icon-pack`、`GET /api/player/v1/items/{itemId:int}/icon`、`GET /api/player/v1/items/{itemId:int}/tooltip`、`GET /api/player/v1/mail/items`、`GET /api/player/v1/mail/items/{itemId:int}`（五条执行器外只读路由；列表须保持 ordinal 排序）。结果中列出数字。豁免清单注释所指的另一任务 spec 附录 B 不改（同 TASK-03 做法）。

`Application.Tests/PlayerPortal/PlayerPortalMailRuleTests.cs`：

- `CheckMailContent`：正文非空白、仅物品、仅金币各通过；空正文 / 空白正文 / 只有标题且无附件被拒；null 物品、`CharacterId <= 0` 被拒；
- 把 `CheckMailContent` 与真实 `GmMailRuleService.Normalize` 串起来：空正文无附件在 `Normalize` 之前被拒，而单独调用 `Normalize` 会填入默认正文并通过——固定 R-3 的先后关系（用 `ActivatorUtilities` 或测试替身构造 `GmMailRuleService` 不便时，只测 `CheckMailContent` 并在 Host 测试中覆盖先后关系）；
- `ToMailInput`：收件人为 `[CharacterId]`，每件物品 `Equipment == null`，顺序保留；
- `MailPayloadHash`：收件角色、标题、正文、物品顺序、数量、金币任一不同则不同，相同则相同，且与点券哈希的前缀不同；
- `CheckMailLimits`：0 不限；金币恰好等于上限通过、超 1 被拒；封数 `已用 + 1` 恰好等于上限通过、超出被拒。

### 6. 变异核对（核对后恢复原代码，在结果中记录）

- 把重放判定挪到归属判定之后：AC-012"收件角色删除后重放"用例应当失败。
- 把 `CheckMailContent` 挪到 `Normalize` 之后（用规范化后的正文判断）：R-3"空正文无附件被拒"用例应当失败。
- 归属判定去掉"账号是否为会话账号"的比较：AC-005 对 b1 的用例应当失败。
- 每日封数判定改为 `已用 + 1 >= 上限`：AC-022 / AC-008(c) 用例应当失败。
- 投递组件里删去 `AssertMailWireRangeAsync` 调用：GM `ExhaustedWireIdentifiersRollBackWithoutCreatingUnrepresentableMail` 应当失败（证明 GM 回归防线确实经过抽出的组件）。

### 7. 执行顺序

1. 投递组件抽取（第 1 节）→ 构建 → GM 邮件回归防线（Required checks 所列）全部通过。
2. Data、Abstractions、`HttpRouteOperation` 追加 → 规则服务与其 Application 测试（先写失败用例）。
3. 玩家邮件领域服务、命令与注册。
4. GmWeb 路由、`ItemFilter` 可见性、映射器。
5. Host 测试逐个转绿；更新 `GmGameplayRouteTests`、`PlayerPortalBoundaryHttpTests`、`PlayerPortalRecordsHttpTests`。
6. 文档：`Document/gm-web-admin.md`「玩家自助后台」补：
   - `POST mail/send`：请求体、"至少一项附件或正文"、判定顺序（重放先于子开关、归属与限额）、归属拒绝不区分三种情形、物品与文字规则同 GM 官方邮件（装备固定最上级品质、30 天有效、留空正文用默认正文）、响应 `{ receipt, replayed }`、在线角色由提交后的新邮件提醒通知；
   - `GET mail/items`、`GET mail/items/{id}`、`GET items/{id}/icon`、`GET items/icon-pack`、`GET items/{id}/tooltip`：目录与 GM 邮件目录同一选择规则，图标与提示与 GM 同一实现，需玩家会话且邮件子开关开启；
   - 邮件回执字段；`PlayerSendMail` 审计口径（含 `RolledBack`，顺带补 TASK-04 审查遗留的点券 `RolledBack` 说明）。
7. 检查（长命令放后台）：
   - `dotnet build Server/86JP.Server.slnx`；
   - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~PlayerPortal|FullyQualifiedName~GmGameplayRouteTests"`；
   - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~PlayerPortal"`；
   - GM 邮件回归防线与回归：
     - `dotnet test Server/tests/Dfo.Server.Host.Tests --filter "FullyQualifiedName~Dfo.Server.Host.Tests.Gm|FullyQualifiedName~ItemCatalog|FullyQualifiedName~HttpAuditedFailureBoundary"`；
     - `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~Gm|FullyQualifiedName~HttpRouteMechanism|FullyQualifiedName~MechanismBusinessIndependence"`；
   - Host.Tests 满载时可能因就绪预算超时偶发失败（`Document/BuildAndTest.md`），遇到时单独重跑失败用例确认；
   - 行尾：`git ls-files --eol` 查看改动的已跟踪文件，新建文件逐个确认不含 CR（如 `grep -c $'\r'`）；用脚本编辑过的文件尤其要查。

## Result

结论：按 Revision 1 完成，协调方裁定的两条补充路由（玩家 `items/icon-pack` 与 `items/{id}/tooltip`）已并入第 0 节第 1 项。未提交，改动都留在工作区。

### 改动

- Application / Administration
  - 新建 `OfficialMailDelivery`：把 `GmOfficialMailDomainService.SendAsync` 中逐收件人的循环体逐行搬入，原有注释保留。循环体负责建邮件、按件创建装备、建堆叠物、附金币、组装附件、`SendPreparedAsync` 和线号范围断言。另提供 `Lifetime = 30 天`。
  - GM `SendAsync` 的循环改为调用该组件。构造参数去掉 `IMailboxDomainService`、`IItemLifecycleDomainService`，加入 `OfficialMailDelivery`。其余代码未动。
  - `AdministrationRegistration` 把组件注册为单例。
- Data：新建 `PlayerPortalMail.cs`，含 `PlayerMailItem`、`PlayerMailRequest`（两者都拒绝未声明字段、全部必填）、`PlayerMailReceipt`、`PlayerMailReceiptItem`、`PlayerMailSendResult`。
- Abstractions：新增 `IPlayerPortalMailDomainService`，方法为 `AuthorizeCatalogAsync`、`SearchItemsAsync`、`ItemDetailsAsync`、`SendAsync`、`RecordFailureAsync`。
- Application / PlayerPortal
  - 新建 `PlayerPortalMailDomainService`，按第 0 节第 4 项的顺序判定后投递。
  - `PlayerPortalRuleService` 增加 `CheckMailContent`、`ToMailInput`、`MailPayloadHash`、`CheckMailLimits`。
  - `PlayerPortalCommands` 增加 `SendPlayerMailCommand` 与 `PlayerPortalMailCommandHandler`，结构与点券处理器相同；注册时声明 `AuditedFailure`。
  - `PlayerPortalRegistration` 增加一行注册。
- HttpApi：`HttpRouteOperation` 末尾追加 `PlayerSendMail`。
- GmWeb
  - `PlayerPortalEndpoints`：新增 `POST mail/send`（请求体上限 8 KiB，数字严格解析，`RespectNullableAnnotations`）和五条执行器外只读路由：`mail/items`、`mail/items/{id}`、`items/icon-pack`、`items/{id}/icon`、`items/{id}/tooltip`。
  - `PlayerRouteMapping`：`Writes` 加入 `PlayerSendMail`；发信成功响应为 `{ receipt, replayed }`；新增执行器外路由用的 `OutsideAsync` 失败映射；`Refusal` 改为 `internal`。
  - `GmOfficialMailEndpoints.ItemFilter` 改为 `internal`，增加可选的错误构造参数。缺省仍抛 `GmMailException`，GM 调用点未改。
- 测试
  - Host.Tests 新建 `PlayerPortalMailHttpTests`（13 例）、`PlayerPortalMailCatalogHttpTests`（3 例）。
  - `PlayerPortalRecordsHttpTests` 增加 1 例（AC-013 邮件部分）。
  - `PlayerPortalTestSupport` 增加 `SetMailLimitsAsync`、`MailboxAsync`、`PlayerClient.SendMailAsync`。
  - `PlayerPortalBoundaryHttpTests` 的 AC-003 断言新路由出现在两侧路由枚举中，GM 侧补上 `icon-pack` 与 `tooltip`。
  - `GmItemIconHttpTests.WritePack` 改为 `internal`，供复用。
  - `GmGameplayRouteTests` 的计数和豁免清单按第 5 节更新。
  - Application.Tests 新建 `PlayerPortal/PlayerPortalMailRuleTests`（11 例）。
- 文档：`Document/gm-web-admin.md`「玩家自助后台」补充以下内容：发信接口与判定顺序；物品目录、图标与提示接口；邮件回执字段；`PlayerSendMail` 审计口径（顺带补上 TASK-04 审查遗留的 `RolledBack` 说明）。

### 与计划的出入

- 计划第 5 节的 Host 用例按主题合并成 13 个方法，覆盖面不变：
  - AC-011 的物品、装备、多余字段与"同 GM 消息"合为一例；
  - AC-008(a) 与 (a′) 合为一个 Theory。
- `characterId <= 0`、空 `requestId` 与 null 物品在规则服务里判为 `invalid`。除 `characterId <= 0` 这条计划已写明外，另两条只是解析器之后的防线（解析器已拒绝空 GUID 和 null）。
- 物品目录测试对比的是玩家与 GM 两侧的状态码和成功响应正文，不比较错误消息：GM 对不存在的物品回原异常消息，玩家固定回"未找到该物品。"，`code` 为 `not_found`。
- 提示接口的读取失败由 `OutsideAsync` 统一回 500 `unknown`，消息为"物品资料读取失败，请稍后重试。"，与 GM 提示接口相同。差别在于提示源抛 `ArgumentException` 时，玩家侧回 400 `invalid`，GM 侧回 500。

### 验收对照

| AC / 遗留 | 用例 |
|---|---|
| AC-011 | `Items_and_gold_reach_the_own_character_by_the_gm_mail_rules`：<br>• 物品 3×2 + 金币 500 写入 a1 邮箱，回执 12 个字段齐全，有效期 30 天；<br>• 装备带固定最上级种子 `999999998`；<br>• `equipment`、其它物品字段、`accountId`、null 与缺字段这五种请求都回 400，不写审计；<br>• 不支持的物品、超长标题、11 件附件的拒绝消息与 GM 逐字相同 |
| AC-011 在线提醒 | `An_online_recipient_is_told_of_the_new_mail_once`：a1 在线（`/me` 显示在线），提交后恰好一条发给 a1 的 `MailDelivered`；重放后仍只有一条 |
| R-3 / RQ-011 | `A_mail_needs_a_body_or_an_attachment_judged_before_the_default_body`：<br>• 空正文、空白正文、只有标题都被拒并写 `Rejected` 审计，不建邮件；<br>• 空正文加金币可以发出，正文为 GM 默认文案。<br>Application 中 `The_gm_rules_alone_would_let_an_empty_mail_through` 固定两者的先后关系 |
| AC-005 | `Another_accounts_a_deleted_and_a_missing_character_are_refused_alike`：三种情形的响应体逐字相同，均为 404 `not_found`；邮箱、物品、操作记录都不变；三条 `Rejected` 审计的对象分别是所请求的角色 |
| AC-012 | `A_request_id_delivers_once_and_replays_even_after_the_recipient_is_deleted`：<br>• 封数用满、子开关关闭、收件角色删除后，原编号都返回原回执（原始 JSON 逐字相同）；<br>• 改金币、改物品顺序、改收件人、编号被点券占用，均回 409；<br>• 另一账号可以用同一编号 |
| AC-021（邮件） | `Closing_the_mail_switch_refuses_only_mail_and_a_closed_portal_answers_portal_closed`；目录侧见 `Every_catalog_route_needs_the_portal_the_session_and_the_mail_switch` |
| AC-008(a)(a′) | `Closing_after_a_mail_passed_the_boundary_refuses_it_inside_the_cmd`：关总开关回 `portal_closed`，关邮件子开关回 `feature_disabled`，两种响应都带 `outcome`；均不发信 |
| AC-008(c) | `Two_concurrent_mails_never_pass_the_daily_count_together` |
| AC-022（邮件） | `Mail_limits_count_only_delivered_mails_and_a_new_game_day_starts_again`：<br>• 单封金币 501 被拒；<br>• 被拒的邮件与点券入账都不计入封数，恰好达到上限可以发出；<br>• 日切后重新计数，`today.mails` 归 1；<br>• 限额全 0 时金币 2000000000 可以发出 |
| AC-024（邮件） | `Player_mails_are_audited_as_the_player_and_filtered_by_actor`：<br>• 重放、未登录、解析失败、总开关关闭都不写审计；<br>• `actor=player&action=PlayerSendMail` 只返回成功与被拒两条 |
| 执行器失败 | `An_unconfirmed_commit_is_audited_under_the_player_and_the_same_id_replays`：写 `Unknown` 审计，原编号重放，只投递一封。<br>`A_storage_fault_rolls_the_whole_mail_back_and_is_audited_apart`：回 500 `rolledBack`，全部回滚，写 `RolledBack` 审计 |
| AC-013（邮件） | `PlayerPortalRecordsHttpTests.Mail_records_are_the_own_sent_mails_as_they_were_answered` |
| SD-8a / R-3 范围 | `The_catalog_answers_as_the_gm_mail_catalog_does`：18 个查询与 GM 的状态码一致，成功时正文逐字相同。<br>`Icons_and_tooltips_are_the_gm_ones`：图标包状态、图标字节、缓存头与提示都与 GM 相同 |
| R-2 / AC-003 | `PlayerPortalBoundaryHttpTests.Neither_session_opens_the_other_prefix`：新路由在枚举中，GM 会话访问它们回 401 `unauthenticated` |

### Checks

- `dotnet build Server/86JP.Server.slnx`：0 警告、0 错误。抽取投递组件后、全部完成后、变异核对恢复后各构建一次，结果相同。
- 抽取后的 GM 邮件回归（先于玩家侧实现运行）：Host.Tests `GmOfficialMail|GmMailAttachment|GmRequestedCancellation|ItemCatalog` 通过 53，跳过 4（都是需要真实 PVF 的用例）。
- 本单元测试
  - Application.Tests `~PlayerPortal`：通过 28，含新增 11 例。
  - Host.Tests `PlayerPortalMail|PlayerPortalRecords|GmGameplayRouteTests|PlayerPortalBoundary` 首轮通过 29、失败 2、跳过 1（原有跳过）。两处失败都是测试自身写错：
    - 缓存头断言写成了字符串比较，改为与 GM 图标测试相同的逐项断言；
    - SQL 里的 `kind` 取值大小写写错，存储值为小写 `mail`。

    修正后 `PlayerPortalMail` 16 例全部通过。这些用例也都包含在下面的回归中。
- 回归
  - Host.Tests `Dfo.Server.Host.Tests.Gm*|PlayerPortal*|ItemCatalog*|HttpAuditedFailureBoundary`：通过 215，失败 0，跳过 12，都是原本就标了跳过的用例：真实 PVF 用例 11 个，`GmGameplayRouteTests` 原有的 1 个。
  - Application.Tests `~Gm|~PlayerPortal|~HttpRouteMechanism|~MechanismBusinessIndependence`：通过 79，失败 0，跳过 11（原有跳过）。
  - 其中 GM 邮件防线 `GmOfficialMailHttpTests`、`GmMailAttachmentHttpTests`、`GmRequestedCancellationAuditTests`、`ItemCatalogHttpTests`、`GmItemIconHttpTests`、`GmMailEquipmentOptionsTests`、`HttpRouteMechanismTests`、`MechanismBusinessIndependenceTests` 全部通过。用真实 PVF 的 GM 邮件用例（如 `ActualPvf*`）本机未配置 PVF，仍为跳过，抽取后的组件在真实内容下没有自动化覆盖。
- `GmGameplayRouteTests` 计数：`gm:` 命令 50→51，`HttpGameplayOperation` 48→49，`EnqueuesItself` 保持 4；豁免清单增加 5 条玩家只读路由。豁免清单注释所指的另一任务 spec 附录 B 未改（同 TASK-03）。
- 行尾
  - `git ls-files --eol` 显示 16 个改动的已跟踪文件均为 `i/lf w/lf`；`git diff --stat` 与 `--ignore-cr-at-eol` 的统计一致（16 files, +309/−41），没有整文件行尾变更。
  - 7 个新建文件（本文件、`OfficialMailDelivery.cs`、`PlayerPortalMailDomainService.cs`、`PlayerPortalMail.cs`、`PlayerPortalMailRuleTests.cs`、`PlayerPortalMailCatalogHttpTests.cs`、`PlayerPortalMailHttpTests.cs`）逐字节统计 CR 均为 0。
  - 注意：本环境 Bash 中 `grep -c $'\r'` 的转义不可靠，会误报，改用 Python 逐字节统计。

### 变异核对

五项都已恢复原代码：恢复后三份源文件与变异前的 SHA-256 一致，并重新构建。

- 重放判定挪到归属之后：`A_request_id_delivers_once_and_replays_even_after_the_recipient_is_deleted` 失败。
- 内容检查改为用规范化后的正文：`A_mail_needs_a_body_or_an_attachment_judged_before_the_default_body` 失败。
- 归属判定去掉账号比较：`Another_accounts_a_deleted_and_a_missing_character_are_refused_alike` 失败。
- 每日封数改为 `>=`：Host 3 例失败（AC-012、AC-008(c)、AC-022），Application 1 例失败（恰好等于上限）。
- 投递组件去掉 `AssertMailWireRangeAsync`：GM `ExhaustedWireIdentifiersRollBackWithoutCreatingUnrepresentableMail` 的两组数据都失败，证明 GM 防线确实经过抽出的组件。

### 留给后续单元

- T-006
  - 发邮件：响应为 `{ receipt, replayed }`，没有 `reminder` 字段；收件人只列 `/me` 中的角色；拒绝按 `code` 分支。
  - 物品选择：用 `/api/player/v1/mail/items`、`mail/items/{id}`，查询参数与 GM 相同；图标组件改用 `/api/player/v1/items/icon-pack`、`items/{id}/icon`，悬停提示用 `items/{id}/tooltip`。邮件子开关关闭时这五条都回 403 `feature_disabled`，前端应在子开关关闭时不请求它们。
  - 记录页：邮件回执字段见 `Document/gm-web-admin.md`。
- T-007：用户测试流程里写明玩家发的装备为固定最上级品质（R-4），并加一步在线角色实机收信。

## Result Review

### Independent Quick Review R1

- 审查对象：TASK-05 契约 Revision 1 的执行结果（本文件 `## Result`），基线 `79f7cb4b5` 之上的未提交改动（16 个已跟踪文件 +309/−41，7 个新文件）。审查者未参与本单元的计划与实现。
- 依据：`reviewing-work` 的 Task Unit + Code 准则；plan.md T-005（SD-1、SD-1a、SD-8、SD-8a、SD-9），spec.md RQ-004/007/011/012/013/022/024 及对应 AC。审查角度：多发、越权、GM 行为变化。

```yaml
kind: rks-review
verdict: approved
```

核查结论：

1. **投递组件抽取等价**：逐行对照 `git diff` 中 `GmOfficialMailDomainService` 删除的循环体与 `OfficialMailDelivery.DeliverAsync`——建信、按件建装备并 `AddEquipment`、堆叠物一次 `CreateAsync` + `AddStackable`、`gold > 0` 时 `AddGold`、`records[index]` 组装附件、`SendPreparedAsync`、`AssertMailWireRangeAsync` 顺序与异常完全一致，注释保留。`at.AddDays(30)` → `at + Lifetime(30 天)` 对 `DateTimeOffset` 等值。GM 的授权、重放、`TargetsAsync`、账号比对、回执、操作记录、审计未动；`GmOfficialMailEndpoints` 只改 `ItemFilter` 可见性与可选错误构造（缺省仍抛原 `GmMailException`），GM 调用点未改。依赖与 GM 服务同为单例，生命周期不变。
2. **玩家发信判定**：`SendAsync` 顺序为写意图 → 总开关/会话 → `CheckMailContent`（在 `Normalize` 之前，按原始正文 `IsNullOrWhiteSpace` 判"至少一项"）→ `Normalize` → 哈希 → 重放（`Kind == Mail` 且哈希相同才回原回执，否则 409）→ 子开关 → 归属 → 限额 → `PrepareItemsAsync` → 投递 → 存记录 → 审计，全部在执行单元的同一事务内。归属两重检查（`RecipientsAsync` 已含 `delete_flag=0` + `FindOwnerAsync`），四种失败抛同一异常、同一消息；归属先于物品创建。请求类型 `JsonUnmappedMemberHandling.Disallow` + 全部 `JsonRequired` + `RespectNullableAnnotations`，`ToMailInput` 固定 `Equipment = null`，无法经玩家请求注入装备选项或账号。单封金币与每日封数在 `AcquireWriteAsync` 串行化后判定，`long` 计算、"+1 > 上限"。
3. **路由授权**：5 条执行器外只读路由都挂 `PlayerSessionRequired`，边界判总开关与会话，路由内再经 `AuthorizeCatalogAsync` 判总开关、会话与邮件子开关（`AuthorizeAsync` 只读，无事务需求）。GM 的目录、图标、提示路由与授权未改；`PlayerPortalBoundaryHttpTests` 的动态枚举已含新路由，并对它们做 GM 会话 → 401 的检查。
4. **测试有效性**：用例覆盖删除后/子开关关闭后/封数用满后的重放、三种归属失败响应体逐字比较、改金币/物品顺序/收件人/编号被点券占用的冲突、并发两封仅一封成功、执行器未确认提交与存储回滚；结果记录的五项变异核对（含删去 `AssertMailWireRangeAsync` 让 GM 用例失败）证明测试能暴露对应错误，且 GM 防线确实经过抽出的组件。
5. **行尾**：`git ls-files --eol` 16 个改动文件均为 `i/lf w/lf`；`git diff --stat` 与 `--ignore-cr-at-eol --stat` 一致（16 files, +309/−41）；7 个新文件用 Python 逐字节统计 CR = 0。
6. **真实 PVF 复跑**（本审查执行，`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`，只读）：
   - Host.Tests `GmOfficialMail|GmMailAttachment|PlayerPortalMail|ItemCatalog|GmRequestedCancellation|GmItemIcon|PlayerPortalRecords`：76/76 通过，0 跳过；其中 `ActualPvfEquipmentStackAndAccountMaterialsAreDetachedThenClaimedThroughExistingMailbox`、`ActualPvfConfiguredJewelrySurvivesClaimAndRestart`、`ActualPvfItemCatalogFiltersAndPagesOnlyMailableItems` 均实际运行并通过。结果中"抽取后的组件在真实内容下没有自动化覆盖"一条由此补齐。
   - Application.Tests `GmMail|PlayerPortal|HttpRouteMechanism|MechanismBusinessIndependence`：55/55 通过，0 跳过。

非阻塞残留（不要求本单元处理）：

- `PlayerRouteMapping.OutsideAsync` 把任意 `ArgumentException`（含 `ArgumentNullException` 等编程错误）都答成 400 `invalid`，提示接口在此与 GM（500）不同；结果已如实说明，影响仅限只读接口的错误分类。
- 目录路由的"总开关关闭 / 未登录"用例由边界先行拒绝，路由内对这两项的复判没有被单独触发；路由内独有的邮件子开关判定已被覆盖。
- 玩家可发的堆叠物数量只受 GM 邮件既有规则约束，玩家后台限额只有单封金币与每日封数（符合 RQ-020/RQ-022 的定义）；T-007 用户测试说明里宜提醒管理员按需配置限额。
