---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: 玩家自助后台（#329）与受信代理识别（#305）

## Planning Choice

配置了 `attention_review: on-drift`，且后续细节依赖前面的产出（表结构与持久化接口决定身份层写法，身份层决定玩家操作与前端的接口），工作横跨服务端多层、迁移工具、前端与实机准备，需要可交接、可恢复的单元，因此用 task-units：本 Plan 定路线、共享接口与分解，细节在各 Task Unit 中渐进编写。

## Approach and Shared Decisions

后续单元都依赖下面这些决定，现在定下，单元内不再改：

- **SD-1 路由与执行。** 玩家接口挂在 GmWeb 现有的路由组（同一监听端口），前缀 `/api/player/v1`。和 GM 路由一样，走 `RunsOnGameplay(GameplayCallSource.Gm, …)`，每条路由是一条在串行执行器上运行的命令；登录与 GM 登录相同，先在执行器外校验密码，再把校验结果交给自己入队的登录命令。RQ-007 的原子性依靠"判定与写入都在同一条命令、同一个命令事务里"来满足。写命令内的判定顺序固定为：取写意图（与 GM 点券发放的 `AcquireWriteAsync` 相同）→ 总开关 → 会话 → **重放判定**（同账号同请求编号已有成功记录：内容一致直接返回原结果，不再判子开关、归属与限额；内容不一致则拒绝）→ 子开关 → 归属 → 限额用量 → 写入。入口中间件只做身份识别以便快速拒绝，不作为授权依据。
- **SD-1a 拒绝契约。** 玩家接口的失败统一返回 `{ message, code, outcome? }`，用独立的响应映射（不经 `GmRouteReplyMapper`，避免落入其按枚举范围的分支）：`portal_closed`（403，总开关关闭）、`feature_disabled`（403，子开关关闭）、`unauthenticated`（401，无会话、过期或被踢）、`not_found`（404，角色不存在 / 不属于自己 / 已删除，消息一致）、`limit_exceeded`（409）、`request_conflict`（409，同编号不同内容）、`invalid`（400，字段或规则不合法，含 GM 规则服务的拒绝消息）、`rate_limited`（429）、`unknown`（500，结果未确认）。前端只按 `code` 分支。玩家写操作的失败审计用命令开始时识别到的账号身份记录，不再用会话密钥重新识别；没有识别出身份的请求不写失败审计。
- **SD-2 边界。** 把 `GmAccessBoundary` 扩展为同时处理 `/api/gm/v1` 与 `/api/player/v1` 的单一后台边界：监听端口限定、HTTPS 规则、`Origin` 与 `Sec-Fetch-Site` 检查、防伪令牌，这些两侧共用同一段代码（RQ-006 靠构造保证）。身份解析按前缀分开：GM 前缀只认 GM 会话 Cookie，玩家前缀只认玩家会话 Cookie。玩家前缀上，总开关关闭的判断先于会话判断（RQ-021）。防伪令牌沿用现有 antiforgery 配置与 `X-GM-CSRF` 请求头，玩家侧提供自己的 `/api/player/v1/auth/csrf`。
- **SD-3 会话与 Cookie。** 玩家会话密钥的生成和存储方式与 GM 相同：随机 32 字节，库里只存 SHA-256；有效期 8 小时。Cookie 名 `Dfo.Player.Session`，`Path=/api/player`，`HttpOnly`、`SameSite=Strict`，`Secure` 取自解析后的协议。
- **SD-4 受信代理（#305）。** 新配置项 `GmWeb:TrustedProxies`（IP 列表，默认空），启动时校验，非法值报错并给出该配置项名。非空时，只在 GmWeb 监听端口的请求上（`UseWhen` 按 `LocalPort` 分支）、在 GmWeb 边界之前接入 `ForwardedHeaders`（`XForwardedFor | XForwardedProto`，`ForwardLimit = 1`）。清空默认的已知网络与 `KnownProxies`（.NET 10 用 `KnownIPNetworks`，不用已过时的 `KnownNetworks`，否则警告即错误），只放入配置的地址；中间件本身把 IPv4 映射形式与 IPv4 视为同一地址（挑战者已用 ASP.NET Core 10.0.10 实测），由测试固定。LauncherAuth 端口不经过它。`SecureTransport` 保持"`IsHttps` 或回环"，作用在解析之后的值上（RQ-032）。
- **SD-5 限流。** `GmLoginLimiter` 注册为两个独立实例（GM、玩家），来源键为解析后的 `RemoteIpAddress`（RQ-033）。
- **SD-6 数据（表结构 v7）。**
  - `player_portal_settings`：单行；总开关、两个子开关、六项限额、revision。缺省行为"关 / 开 / 开 / 全 0"。
  - `player_portal_sessions`：token_hash、account_id、created_at、expires_at。
  - `player_portal_operations`：account_id、request_id、kind、currency 或金币、amount、payload_hash、receipt_json、period_day、committed_at；主键 (account_id, request_id)，按 (account_id, kind, period_day) 建索引供每日用量统计。
  - `gm_audit` 增加 `actor_kind TEXT NOT NULL DEFAULT 'gm'` 与 `account_id INTEGER`。
  - `CharacterDatabaseSchema.Version = 7`；DbMigrate 新增步骤 `[6]`（复制 v7 的声明语句，参照 `CharacterRelationsUpgrade`；建表用 `IF NOT EXISTS`，给 `gm_audit` 加列前先查列是否存在，因为旧库升级路径可能已按当前声明建过表）。
- **SD-7 分层。**
  - Data：新增 `Dfo.Server.Data/PlayerPortal/` 放 record。
  - Abstractions：新增持久化与领域服务接口。
  - Application：新增 `Dfo.Server.Application/PlayerPortal/`，放领域服务、命令与处理器、规则（限额、开关）。
  - Persistence：新增 SQLite 实现。
  - GmWeb：新增玩家端点文件。
  - `HttpRouteOperation` 追加玩家与玩家后台设置的操作值。
  - Application 不引用 Infrastructure。
- **SD-8 复用 GM 的点券与邮件规则。**
  - 点券：用 `IGmCeraRuleService.Decide`（币种只放行 `Cera`、`TokenCera`）、`IAccountWalletDomainService.TryApplyAsync` 和 `IGmCeraReminderDomainService`。
  - 邮件：把 `GmOfficialMailDomainService.SendAsync` 中"为一个收件角色建邮件、创建物品、投递"的循环体，提取为 Application 内的共享投递组件，GM 与玩家共用。物品规范化沿用 `GmMailRuleService`。玩家请求使用自己的请求类型，物品只有 `{itemId, count}` 两个字段并拒绝任何未声明字段（RQ-011），映射到 GM 邮件输入时装备选项一律为空；不修改 GM 的请求类型与规则服务。GM 规则对玩家同样适用的既有行为照旧：正文为空时用 GM 邮件的默认正文，装备按 GM 邮件不带选项时的固定品质生成（Spec Constraints 要求同一套规则）。
  - GM 原有对外行为不变，现有 GM 测试作为回归防线。
- **SD-8a 玩家物品目录。** 玩家发信需要物品搜索、详情与图标：新增只读的 `/api/player/v1/mail/items`、`/api/player/v1/mail/items/{id}`、`/api/player/v1/items/{id}/icon`，与 GM 对应接口共用同一目录服务与图标包，只换授权（玩家会话 + 邮件子开关）；不放宽任何 GM 接口。前端 `ItemPicker` 与图标组件改为接收接口前缀，GM 侧传原前缀、行为不变。
- **SD-9 审计。**
  - `GmAuditRecord` 增加 `ActorKind` 与 `AccountId`。
  - GM 写入点不改语义，默认 `gm`。
  - 玩家写入 `player`，带账号 ID 与登录名；不符合命名规则的登录名记为固定占位文本，不记密码。
  - `GmAuditFilter` 增加操作者筛选（`actor=player`）与新动作名：`PlayerLogin`、`PlayerLogout`、`PlayerGrantCera`、`PlayerSendMail`、`ChangePlayerPortal`、`KickPlayerSessions`。
  - GM 审计接口响应增量返回 `actorKind`、`accountId`。
- **SD-10 前端。**
  - 在 `Web/gm-admin` 内实现。`main.tsx` 改为渲染一个顶层壳：先查 `/api/player/v1/auth/status` 与已有会话，再决定渲染登录页、`PlayerApp` 或 `AdminApp`。
  - 玩家侧新增 `playerApi.ts`（前缀 `/api/player/v1`；按 SD-1a 的 `code` 分支：`portal_closed` 切到已关闭页，`unauthenticated` 回登录页）与 `PlayerApp` 及其页面，复用 `ItemPicker`（传玩家前缀，见 SD-8a）、`look`、`ui`。
  - 管理员侧新增「玩家后台」页（全权限可见），审计页加"玩家自助"筛选。
  - 布局依据 UI 设计图。

## Work and Dependencies

| 工作包 | 交付 | 覆盖 | 依赖 |
|---|---|---|---|
| T-001 受信代理 | SD-4、SD-5（GM 侧来源键）；两侧通用的边界判定所用的解析值；#305 全部验收测试（玩家侧的放行用例在 T-003 补齐） | RQ-030–032、RQ-034，AC-030、AC-031（GM 侧）、AC-033、AC-034（网关不变、映射地址、非法配置） | 无 |
| T-002 表结构与持久化 | SD-6 全部表与列、v7、DbMigrate 步骤与测试；设置、会话、操作记录、审计扩展的持久化接口与 SQLite 实现及其单元测试 | RQ-050、RQ-020（存储与默认值）、RQ-024（存储），AC-050 | 无 |
| T-003 玩家身份与管理员控制 | SD-1、SD-1a（框架与身份类拒绝）、SD-2、SD-3、SD-5（玩家实例）、SD-9；玩家登录 / 登出 / 状态 / 会话 / `/me`；管理员设置读写、会话列表与踢人；审计筛选与登录、设置、踢人的审计 | RQ-001–007（点券 / 邮件部分除外）、RQ-020、RQ-021、RQ-023、RQ-024（登录 / 设置 / 踢人）、RQ-033，AC-001–007、AC-008(b)、AC-020、AC-021（登录与开关部分）、AC-023、AC-024（登录 / 设置 / 踢人 / 筛选 / 迁移前记录 / 登录名脱敏）、AC-031（玩家侧）、AC-032、AC-034（槽位占满） | T-002；T-001（AC-031/032 依赖解析值） |
| T-004 玩家加点券与记录 | 加点券命令、重放、子开关、限额、提醒、失败审计；记录查询接口（含类型筛选，邮件类型在 T-005 产生数据） | RQ-007（点券）、RQ-010、RQ-012、RQ-013、RQ-022（点券）、RQ-024（点券），AC-008(a)(c)、AC-010、AC-012（点券，含"成功后用量已满或开关已关时原编号重试仍返回原结果"）、AC-013（点券部分）、AC-021（点券子开关、关闭后写请求得到 `portal_closed` 而非 500）、AC-022（点券）、AC-024（点券） | T-003 |
| T-005 玩家发邮件 | SD-8 抽出共享邮件投递组件、SD-8a 玩家物品目录；玩家发信命令、归属裁定、物品字段限制、重放、子开关、限额、失败审计；记录接口出现邮件记录 | RQ-004、RQ-007（邮件）、RQ-011、RQ-012、RQ-013（邮件）、RQ-022（邮件）、RQ-024（邮件），AC-005、AC-008（邮件并发与关闭竞态）、AC-011（服务端部分）、AC-012（邮件，含重放于收件角色删除后仍返回原结果）、AC-013（邮件部分）、AC-021（邮件子开关）、AC-022（邮件）、AC-024（邮件） | T-003（可与 T-004 先后进行，不能并行写同一工作区） |
| T-006 前端 | SD-10 全部页面与前端测试 | RQ-040–044，AC-040 | T-003–T-005 的接口 |
| T-007 集成验证与用户测试准备 | 全量构建与测试；agent 自用测试服（独立的库副本、agent 端口段含游戏端口）上的浏览器实测（含 390px、已关闭页、审计页）；能在不改动用户客户端配置或改动可记录并还原的前提下做在线角色实机收信，否则在用户测试流程中列为待用户验证项；为用户另备一份**未被 agent 实测写过**的主干库迁移副本与测试服务；编号测试流程文档 | RQ-051、RQ-060、RQ-061，AC-041、AC-051、AC-060、AC-061，以及 AC-011 的在线实机部分 | T-001–T-006 |

拆分理由：T-001 是独立的传输层改动，可单独验证、不碰业务；T-002 定下所有持久化接口，后续单元按接口编写；T-003 是访问控制核心，要先于任何玩家写操作完成并通过审查；T-004、T-005 各自复用 GM 的一块规则，失败面不同；T-006 只消费接口；T-007 需要整体结果。

每个单元都带自己的测试（服务端单元测试与 `Dfo.Server.Host.Tests` 里基于 `GmTestHost` 的 HTTP 测试，前端用 vitest）。T-001、T-003、T-004、T-005 涉及访问控制或资金入账，Result review 设为 required；T-002 的迁移有结构一致性测试兜底，T-006 有前端测试兜底，按单元情况决定。

## Verification

- 每个单元提交前运行其所写测试，并跑 `dotnet build Server/86JP.Server.slnx` 确认零警告；凡改动 GmWeb、GM 领域服务、审计或持久化的单元，同时运行 `Dfo.Server.Host.Tests` 的 `Gm*` 测试以及 Application / Persistence 测试中相关的 GM 测试作为回归，不留到 T-007。
- T-007 运行全量 `dotnet test Server/86JP.Server.slnx` 和 `Web/gm-admin` 的 `npm test`、`npm run build`。
- 本地测试服用 agent 端口段，浏览器验证 AC-041；游戏客户端验证在线角色收信。
- 用户测试按 RQ-060/RQ-061 准备，把编号流程交给用户。
- 证据汇总到 `verification/completion-evidence.md`，逐条对应 TaskContract 的 SC-1–SC-13。

## Next Planning Boundary

下一个单元是 **TASK-01 = T-002 表结构与持久化**。它决定所有后续单元使用的接口，且风险可由结构一致性测试与持久化单元测试直接暴露。

T-001 不依赖它，排在 TASK-02。T-003 的细节要等 T-002 的接口落定后再写；T-004、T-005 要等 T-003 的命令与授权骨架落定后再写，其细节依赖那里的会话与开关判定写法。
