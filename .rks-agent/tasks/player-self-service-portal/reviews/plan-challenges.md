# Plan 对抗性挑战

- 目标：`plan.md`（`mode: task-units`，`state: ready`；SHA-256 前缀 `e97824ad397cc84b`）
- 依据：`TaskContract.md` revision 2（`a1f7789f0a591b5d`）、`spec.md`（已批准，`1a4fc3b2d6262cff`，见 `reviews/spec-review.md`）
- 代码参照（只读）：worktree 基线 `20bcaf37c`
- 攻击范围：SD-1~SD-10 与现有代码是否相符、能否满足 Spec（重点：RQ-006/RQ-007 的原子性与边界、SD-4 在共享管道中的作用范围与 IPv4 映射、SD-8 抽取邮件投递对 GM 的影响）；单元拆分与依赖是否有遗漏或倒置；RQ/AC 是否都落到工作包；风险动作之前有无保护。
- 已运行的非破坏性探针：在 scratchpad 用 `dotnet fsi` 直接调用 ASP.NET Core 10.0.10 的 `ForwardedHeadersMiddleware`，确认三点：`KnownProxies=[127.0.0.1]` 时，来自 `::ffff:127.0.0.1` 的请求会被解析；配置成映射形式、实际来自 `127.0.0.1` 的请求不会被解析；`::1` 与 `127.0.0.1` 互不匹配。另确认 `IPAddress.IsLoopback(::ffff:127.0.0.1)` 返回 true。因此 SD-4 的"同时放入两种形式"足以满足 AC-034，不在下文挑战之列。

## CH-1：玩家发邮件页要"复用 ItemPicker"，但物品搜索、详情、图标都只有 GM 接口，没有任何工作包提供玩家侧的对应接口

- Risk: high-risk
- Failure and impact：
  1. **ItemPicker 写死了 GM 接口。** SD-10 要求玩家发信页复用 `ItemPicker`（RQ-041"物品搜索与数量"）。但 `ItemPicker` 经 `useGmRead` 请求 `/api/gm/v1/<purpose>/items`；详情经 `useItemDetails` 走同一前缀；图标直接取 `/api/gm/v1/items/{id}/icon`；`api.ts` 还把 CSRF 与所有请求固定在 `/api/gm/v1/`。按 RQ-002/SD-2，这些接口对玩家会话一律 401。
  2. **没有单元负责补接口。** T-005 的交付只有共享投递组件和发信命令，T-003 只有登录、状态、会话、`/me` 与管理员控制，T-006 被定义为"只消费接口"。照计划执行，T-006 才会发现玩家选不了物品、看不到图标。
  3. **两条出路都不好。** 一是回头改已过 required 审查的 T-005 或 T-003（边界的公开/鉴权划分也要随之扩展）；二是图省事，放宽 GM 物品接口，让玩家会话也能调用。后者直接违反 RQ-002，前端测试和 AC-003 未必能发现（AC-003 只列了会话、目录、点券、邮件、审计、设置这几类接口）。
  4. **一并要定的事。** 玩家搜索的范围应与 GM 邮件的 `SelectionKey = "mail"` 及 `Allows` 规则一致（契约 Constraints 3），否则玩家能搜到但发不出去，或反过来。是否受邮件子开关约束，也要一起定。
- Evidence：`Web/gm-admin/src/ItemPicker.tsx` 第 43–45 行（`useGmRead(\`${apiPurpose[purpose]}/items?...\`)`、`useItemDetails(apiPurpose[purpose], ...)`）；`Web/gm-admin/src/itemInfo.tsx` 第 86 行（`/api/gm/v1/items/${itemId}/icon`）；`Web/gm-admin/src/api.ts` 第 22、27 行；`Server/src/Dfo.Server.GmWeb/GmOfficialMailEndpoints.cs` 第 26–29 行（物品搜索与详情要求 GM 身份）；plan「Work and Dependencies」中 T-005、T-006 的交付列；SD-10。
- Minimum correction：在 T-005（或 T-003）的交付中加入玩家侧的物品搜索、详情、图标接口（`/api/player/v1/...`，与 GM 邮件同一选择键与允许规则），并在 SD-10 写明 `ItemPicker` 与 `useItemDetails` 改为按调用方注入 API 前缀，不能直接复用 GM 路径。AC-003 的反向用例要覆盖这些 GM 物品接口。

## CH-2：SD-1 写死了命令内的判定顺序，却没有给幂等重放留位置；照此实现，重试一笔已成功的请求会被限额或归属拒绝

- Risk: high-risk
- Failure and impact：
  1. **顺序里缺了重放。** SD-1 规定命令内"先取写意图，再读开关、会话、归属、用量，再写入"，并声明"单元内不再改"，但没有说按 (account_id, request_id) 查已有记录的重放判定排在哪里。照字面实现，重放判定会落在限额和归属之后。
  2. **点券场景。** 每日上限 1500，请求 R（1000）成功，随后另一笔 500 也成功，今日用量变成 1500。R 因网络超时被前端以原编号重试，限额判定 1500 + 1000 > 1500 → 拒绝。
  3. **邮件场景。** R 发给 a1 成功后，a1 被删除或转移；重试时归属判定失败 → 拒绝。
  4. **对玩家的后果。** 两种情况都违反 RQ-012"同一编号同一内容只生效一次并返回原结果"：玩家看到的是失败，钱或邮件其实已经到了。GM 端对未确认结果的提示正是"用原编号查询或重试"，玩家前端若照搬，就会稳定落入这条路径；玩家随后换新编号重发，又会造成重复入账或发信（只受限额兜底）。
  5. **现有验收发现不了。** AC-012 只测无限额、归属不变时的重放，AC-022 不含重试，所以这个缺陷能通过全部验收。
  6. **GM 先例的顺序相反。** GM 的 `GrantAsync` 与 `SendAsync` 都是授权之后立即查 `previous`/`old` 并直接返回，之后才计算变化或核对收件人。
- Evidence：plan SD-1；`Server/src/Dfo.Server.Application/Administration/GmCeraDomainService.cs` `GrantAsync`（`Authorize` → `OperationAsync` → 返回 `previous.Receipt` → `Changes`）；`GmOfficialMailDomainService.cs` `SendAsync`（`OperationAsync` → `Match` → 返回，之后才 `TargetsAsync`）；`GmRouteMapping.cs` 中 `"结果暂未确认，请用原编号查询或重试。"`；spec RQ-012、AC-012、AC-022。
- Minimum correction：在 SD-1 的顺序中写明"总开关、会话之后，先做重放判定（同内容返回原结果，不同内容拒绝），再做子开关、归属与限额判定"，或者明确给出另一种取舍及其理由。AC-012 增加一例：限额已满或收件角色已删除后，以原编号重放仍返回原结果。

## CH-3：玩家拒绝的对外契约（"已关闭"、会话失效等的状态码与错误码）和失败审计的身份来源都没写进共享决定；照搬 GM 写法，关闭或踢人后的写请求会变成 500 "结果未确认"

- Risk: high-risk
- Failure and impact：T-003、T-004、T-005 的服务端与 T-006 的前端都依赖同一份拒绝契约：RQ-021 要求"已关闭"先于会话判断，RQ-042 要求前端据此切到已关闭页或回登录页。但 SD-1~SD-10 都没有定下这份契约，也没有规定玩家操作失败时审计从哪里取账号。照现有 GM 模式实现，会出现三处问题：
  1. **失败审计失败，结果变成"未确认"。** GM 的点券与邮件命令在拒绝后另开一个单元 `RecordFailureAsync`，其中再次用会话密钥 `Authorize`；会话类拒绝（`Unauthorized`/`Forbidden`）不记失败审计。玩家侧若照搬，总开关关闭会删掉全部会话（RQ-021），被踢的会话也一样。此时"已关闭"拒绝会进入失败审计 → 用密钥找不到身份 → 审计失败 → `Outcome = "auditFailed"` → 映射成 500 "结果暂未确认"。前端收不到"已关闭"码，不会切到已关闭页，玩家还被引导去"用原编号重试"。会话过期时同理，前端也不会回到登录页（RQ-005）。
  2. **追加的枚举值会落进别人的错误映射。** SD-7 规定 `HttpRouteOperation`"追加"玩家操作值。若玩家路由沿用 `GmRouteReplyMapper`，它用 `operation is >= HttpRouteOperation.Initialization` 把所有排在后面的值交给 `GmInitializationEndpoints.Safe`。`Safe` 对 `GmCeraException` 这类未列出的异常一律回 500"配置保存或读取失败"。复用 `IGmCeraRuleService.Decide` 时，余额上限等普通拒绝就会显示成服务器故障。
  3. **现有验收拦不住。** AC-008(a) 只断言"被拒、余额不变"，500 也能通过；AC-021 用读请求验证"下一次请求被拒为已关闭"即可通过。这些缺陷可以一路带到 T-006 和实机阶段。
- Evidence：`Server/src/Dfo.Server.Application/Administration/GmCeraCommands.cs`（catch 分支中 `RecordFailureAsync` 与对 `Unauthorized or Forbidden` 的排除）、`GmCeraDomainService.RecordFailureAsync`（先 `Authorize(secret)`）；`Server/src/Dfo.Server.GmWeb/GmRouteMapping.cs` 第 30–33 行（`auditFailed`/`unknown` → 500）与第 41 行（`>= HttpRouteOperation.Initialization`）；`GmInitializationEndpoints.cs` `Safe`（未列出的异常 → 500）；`GmCeraRuleService.Decide` 抛 `GmCeraException`；plan SD-1、SD-7、SD-9、SD-10；spec RQ-005、RQ-021、RQ-024、RQ-042、AC-008、AC-021。
- Minimum correction：在共享决定中补一条玩家拒绝契约，内容包括：已关闭、未登录、子开关关闭、限额、归属、冲突各自的状态码与机器可读错误码；玩家路由使用自己的响应映射，不靠枚举范围落入 GM 分支；哪些拒绝写失败审计，审计账号从命令携带的账号 ID 取，不依赖拒绝时可能已删除的会话。AC-008(a)、AC-021 断言写请求返回的是"已关闭"码，AC-006 断言写请求在会话过期时返回未登录码。

## CH-4：RQ/AC 的分配与回归防线有缺口：几条 AC 被记在做不完它的单元里，现有 GM 测试要到 T-007 才全量运行

- Risk: low-risk
- Failure and impact：
  1. **几条 AC 被记在做不完它的单元里。**
     - AC-024（"上述各操作在审计中各有一条记录"）整条记在 T-003，但 `PlayerGrantCera`、`PlayerSendMail` 到 T-004/T-005 才出现；T-004、T-005 的覆盖列里没有 AC-024。
     - AC-013（记录可按类型筛选，含邮件）只在 T-004，此时还没有邮件记录；T-005 没有接手。
     - AC-031 的玩家侧（C1/C2 分别计数、玩家与管理员互不影响）：T-001 写明只做 GM 侧，T-003 的覆盖列里也没有 AC-031。
     - RQ-007 对邮件的推论（关闭邮件子开关后排队的发信被拒、并发发信不超过每日封数）不在 T-005 的覆盖列。

     后果是单元审查按覆盖列核对时，这几项会被当成"别处已做"而漏掉。
  2. **回归防线不按单元运行。** SD-8 把"现有 GM 测试"当作回归防线，但 Verification 写的是每个单元只跑"其所写测试"，全量 `dotnet test` 要到 T-007。T-001 改共享管道、T-003 重写 `GmAccessBoundary`、T-005 抽取 GM 邮件循环体、T-002 改 `GmAuditRecord`，这些都可能让 GM 行为回归，却要到前端完成之后才暴露。好在 T-007 必然会跑全量，所以只定为 low-risk。
- Evidence：plan「Work and Dependencies」覆盖列与「Verification」第 1 条；SD-8"现有 GM 测试作为回归防线"；spec AC-013、AC-024、AC-031、RQ-007；现有 GM 回归测试位于 `Server/tests/Dfo.Server.Host.Tests/Gm*HttpTests.cs`。
- Minimum correction：把上述 AC 的对应部分补进 T-004/T-005/T-003 的覆盖列；在 Verification 中规定 T-001、T-002、T-003、T-005 提交前运行 `Dfo.Server.Host.Tests` 中现有的 GM 测试（或全量测试）。

## CH-5：SD-4、SD-6、SD-8 有三处照字面执行会碰到的现有约束

- Risk: low-risk
- Failure and impact：
  1. **SD-4：`KnownNetworks` 在 .NET 10 中已过时。** SD-4 写"清空默认的 `KnownNetworks`/`KnownProxies`"。在 .NET 10 里 `ForwardedHeadersOptions.KnownNetworks` 已标为 Obsolete，应改用 `KnownIPNetworks`。仓库开启了 `TreatWarningsAsErrors`，照写会直接编译失败。会立刻暴露，代价小。
  2. **SD-6：迁移步骤 `[6]` 不能只照抄声明语句。** `[6]` 被描述为"复制 v7 的声明语句，参照 `CharacterRelationsUpgrade`"，但 `gm_audit` 的两列只能 `ALTER TABLE ADD COLUMN`。未版本化的旧库走 `LegacyUpgrade`，它会调用当前的 `CharacterDatabaseSchema.DeclareTables`，可能已经建出带新列的 `gm_audit`，再执行 `[6]` 就会报重复列。该步骤需要像 `AdventureGroupColumnRetirement` 那样先用 `Shape.HasColumn` 判断。现有的 `A_migrated_database_has_exactly_the_shape_the_server_builds` 会立刻发现这个问题。
  3. **SD-8：沿用 GM 邮件规则会带来三个玩家可见的结果。** 这些结果 Plan 没有点明，玩家层需要自行处理，不应改动共享的 `GmMailRuleService`，否则 GM 行为会变：
     - `GmMailRuleService.Normalize(sending: true)` 会把空正文填成"此为官方邮件，无需回复。"，也不要求至少有一项附件或正文。RQ-011 的"至少一项"必须在玩家层另行检查。
     - 不带选项的装备一律套用最高品质档的固定种子（`EquipmentQualityTiers.Highest`），即玩家自助发出的每件装备都是"最上级"。这符合"沿用 GM 规则"的字面，但用户采纳 D3 时可能没有意识到，应在用户测试流程或方案中明示。
     - 物品请求必须反序列化成只含 `{itemId, count}` 的独立记录（未知成员一律拒绝）。不能直接用带 `Equipment` 属性的 `GmMailItem`，否则 RQ-011 要求拒绝的 `equipment` 字段会被静默接受。
- Evidence：`C:\Program Files\dotnet\packs\Microsoft.AspNetCore.App.Ref\10.0.10\ref\net10.0\Microsoft.AspNetCore.HttpOverrides.xml` 第 129–135 行（`KnownNetworks`："Obsolete, please use KnownIPNetworks"）；`Tool/DbMigrate/Legacy/LegacyUpgrade.cs` 第 186 行（`DeclareTables`）；`Tool/DbMigrate/AdventureGroupColumnRetirement.cs`（`HasColumn` 守卫）；`Tool/DbMigrate.Tests/SchemaVersionTests.cs` 第 139–165 行；`Server/src/Dfo.Server.Application/Administration/GmMailRuleService.cs` 中 `Normalize` 与 `EquipmentQualitySeeds.FixedFor(quality)`；`Server/src/Dfo.Server.Data/Administration/GmOfficialMail.cs` 第 8–9 行（`GmMailItem` 含 `Equipment`）。
- Minimum correction：SD-4 改写为 `KnownIPNetworks`；SD-6 注明 `[6]` 的 `ALTER` 先判断列是否存在；SD-8 注明以上三点由玩家层处理，GM 规则服务不改，并把"装备为最上级"写进给用户的测试流程。

## CH-6：T-007 的实机验证要占用的游戏端口和要改的客户端配置都没有保护说明

- Risk: low-risk
- Failure and impact：
  1. **游戏端口与主干冲突。** T-007 要求本地测试服"在线角色收信"（AC-011 实机部分），Plan 只写"agent 端口段"。在线收信需要测试服务的频道与游戏端口：默认 ChannelPort 7001、GamePorts `[10010, 10011, 10100]`，正被主干服务占用（另据记录，10011 还可能被 UU 远程占用）。
  2. **要改用户的客户端配置。** 游戏客户端要连到测试服，得改某个客户端目录 `ClientHack.ini` 的 `[ServerRedirect]`，或改登录器、gate 配置，这些都是用户自己的环境。RQ-060 禁止占用主干端口、不经同意停服，但 Plan 没有说明游戏侧端口如何避开主干，也没有说明用哪个客户端目录、改动后如何还原。
  3. **为何是 low-risk。** 端口冲突通常表现为测试服启动失败而不是破坏数据，所以定为 low-risk。但若随手改主干正在用的客户端目录，会影响用户。
  4. **副本状态会带到用户测试。** T-007 自己的实测会在主干库副本上为用户的真实角色加点券、发信，并可能让总开关保持开启；交给用户时，副本已不是"迁移后初始状态"。
- Evidence：`E:\Project\DNF\AGENTS.md`（GamePorts、ChannelPort 默认值；客户端目录与 `ClientHack.ini` 的 `[ServerRedirect]`）；spec RQ-060、AC-011、AC-060；plan T-007 行与 Verification 第 3 条。
- Minimum correction：在 T-007 中写明：频道与游戏端口同样取 agent 端口段；指定专用客户端目录，配置改动可还原；实测前另做一份副本，或在交付前把总开关恢复为关闭，并记录实测留下的数据。
