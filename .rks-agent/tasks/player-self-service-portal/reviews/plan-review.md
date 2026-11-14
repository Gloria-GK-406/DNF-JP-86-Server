---
kind: rks-review
verdict: approved
---

# Plan 审查

- 审查对象：`plan.md`（`mode: task-units`，`state: ready`；当前 SHA-256 前缀 `f88c395ad6215a68`，即挑战生成后作者修订的版本；挑战针对的是 `e97824ad397cc84b`）
- 依据：`TaskContract.md` revision 2（`a1f7789f0a591b5d`）、`spec.md`（已批准，`1a4fc3b2d6262cff`）、`reviews/plan-challenges.md`（`8cd4a1bbbab93724`，CH-1~CH-6）
- 代码参照（只读）：worktree 基线 `20bcaf37c`，工作区除 `.rks-agent/` 外无改动
- 采用标准：reviewing-work `references/plan.md`，按高层 Plan（task-units）分辨率审查：路线、共享决定、分解与依赖、Spec 覆盖、风险前置保护；不要求未来 Task 的细节
- 独立性：审查者未参与 Plan 编写，也未生成挑战

## 结论

`approved`。当前 Plan 的路线可行，共享决定已覆盖其它单元依赖的、难以回退的选择（会话与 Cookie、边界、命令内判定顺序、拒绝契约、表结构 v7、分层、邮件投递复用、玩家物品目录、审计、前端外壳）；Spec 的 RQ-001–RQ-061 与 AC-001–AC-061 全部落到工作包，挑战指出的覆盖缺口均已补齐；依赖顺序与下一单元（T-002）合理；触及玩家库和用户环境的动作（T-007）有副本、端口和配置还原保护。没有阻塞发现。下文列出的残余事项属于单元内细节，在对应 Task Unit 计划中处理即可。

## 决定性证据

- **判定顺序与重放（SD-1）**：顺序固定为写意图 → 总开关 → 会话 → 重放 → 子开关 → 归属 → 限额 → 写入，与 GM 先例 `GmCeraDomainService.GrantAsync` 中 `AcquireWriteAsync` → `Authorize` → 返回 `previous` 的顺序一致（`GmCeraDomainService.cs` 第 84 行起）。
- **拒绝契约（SD-1a）**：玩家路由使用独立映射，不经 `GmRouteReplyMapper`。核实了映射器中 `operation is >= HttpRouteOperation.Initialization` 会把追加的枚举值交给 `GmInitializationEndpoints.Safe`，而 `Safe` 对未列出的异常回 500（`GmRouteMapping.cs` 第 41 行；`GmInitializationEndpoints.cs` 第 40–65 行）。因此独立映射是必要的，Plan 已经写明。
- **玩家物品目录（SD-8a）**：核实 GM 物品搜索与详情是 `OutsideGameplay` 只读路由，且要求 GM 身份（`GmOfficialMailEndpoints.cs` 第 26–31 行）；图标在 `GmItemEndpoints.cs`。新增玩家侧三个只读接口、前端改为注入前缀、不放宽 GM 接口，能补上原先缺失的这块。
- **邮件规则（SD-8）**：核实 `GmMailRuleService.Normalize(sending: true)` 会给空正文填默认文本（第 26 行），不带选项的装备取 `EquipmentQualityTiers.Highest`（第 75–80 行）。Plan 把这两点作为"沿用 GM 规则"的既有行为写明，并规定玩家请求使用独立的 `{itemId, count}` 类型且拒绝未声明字段。
- **覆盖核对**：逐条核对了 Spec 的 RQ/AC 与工作包覆盖列（详见挑战裁定 CH-4）。AC-032 依赖 T-001 的解析值，T-003 已声明依赖 T-001；AC-007 中经代理的明文请求用例同理。
- **分层**：SD-7 把领域服务与规则放在 Application，SQLite 实现放在 Persistence，符合"Application 不引用 Infrastructure"。

## 挑战裁定

| 挑战 | 原风险 | 裁定 | 当前处置 |
|---|---|---|---|
| CH-1 玩家侧缺物品搜索 / 详情 / 图标接口 | high | confirmed → 已处理 | SD-8a 新增 `/api/player/v1/mail/items`、`/mail/items/{id}`、`/items/{id}/icon`，与 GM 共用同一目录服务与图标包，授权为玩家会话 + 邮件子开关，不放宽 GM 接口；前端 `ItemPicker` 与图标组件改为注入前缀。T-005 交付包含 SD-8a。挑战还要求 AC-003 的反向用例覆盖 GM 物品接口，Plan 未逐字写出；但 T-003 覆盖 AC-003，且 SD-8a 明写"不放宽任何 GM 接口"，留给 T-003/T-005 的测试补上（见残余事项 R-2）。 |
| CH-2 判定顺序缺少重放位置 | high | confirmed → 已处理 | SD-1 写明重放判定在会话之后、子开关 / 归属 / 限额之前：内容一致返回原结果，不一致拒绝。T-004 的覆盖列写明"成功后用量已满或开关已关时原编号重试仍返回原结果"，T-005 写明"重放于收件角色删除后仍返回原结果"。 |
| CH-3 拒绝契约与失败审计身份来源 | high | confirmed → 已处理 | SD-1a 定下 `portal_closed`/`feature_disabled`/`unauthenticated`/`not_found`/`limit_exceeded`/`request_conflict`/`invalid`/`rate_limited`/`unknown` 的状态码与错误码，使用独立的响应映射；失败审计用命令开始时识别到的账号身份，未识别出身份的请求不写失败审计。这与"总开关先于会话"的顺序自洽：已关闭时会话已被清掉，不写审计，也就不会出现 `auditFailed` 被映射成 500 的问题。T-004 覆盖列断言"关闭后写请求得到 `portal_closed` 而非 500"。管理员侧的设置路由仍走 GM 映射，见残余事项 R-1。 |
| CH-4 AC 归属与按单元回归 | low | confirmed → 已处理 | AC-024 已拆分到 T-003（登录 / 设置 / 踢人 / 筛选 / 迁移前记录 / 脱敏）、T-004（点券）、T-005（邮件）；AC-013 的邮件部分在 T-005；AC-031 的玩家侧在 T-003；RQ-007 与 AC-008 的邮件并发与关闭竞态在 T-005。Verification 第 1 条要求凡改动 GmWeb、GM 领域服务、审计或持久化的单元，提交前都运行 `Dfo.Server.Host.Tests` 的 `Gm*` 测试与相关 GM 测试。 |
| CH-5 KnownNetworks 过时 / 迁移重复列 / GM 邮件规则副作用 | low | confirmed → 基本处理，余项 deferred 到 T-005 / T-007 | SD-4 改用 `KnownIPNetworks`；SD-6 规定建表用 `IF NOT EXISTS`，加列前先查列是否存在；SD-8 明确了独立请求类型、默认正文与固定最高品质。余下两点：RQ-011 的"至少一项附件或正文"须在规范化之前检查（否则默认正文会让空信通过）；"装备为最上级"未写进用户测试流程。两者都只影响单个单元，且分别由 T-005 的 RQ-011 / AC-011 与 T-007 的 RQ-061 承接（见 R-3、R-4）。 |
| CH-6 T-007 端口与客户端配置保护 | low | confirmed → 已处理 | T-007 写明：agent 自用测试服使用独立库副本，端口段包含游戏端口；只有在不改动用户客户端配置，或改动可记录并还原的前提下才做在线收信，否则列为待用户验证项；为用户另备一份未被 agent 实测写过的迁移副本与测试服务。启用回退时 SC-5 证据的处理见 R-5。 |

## 残余事项（非阻塞，由对应 Task Unit 计划承接）

- **R-1（T-003）**：管理员侧新增的玩家后台设置、会话列表、踢人路由若追加到 `HttpRouteOperation` 末尾并沿用 `GmRouteReplyMapper`，会落入 `GmInitializationEndpoints.Safe`。该函数只把 `InitializationConflictException` 映射为 409，其它未列出的异常一律 500。为满足 AC-020"带旧版本号保存被拒为冲突"，设置冲突须抛出 `Safe` 能识别的异常，或给这些路由单独映射。AC-020 的测试会直接暴露这个问题，代价小。
- **R-2（T-003 / T-005）**：AC-003"玩家会话调用管理员接口被拒"的用例应包含 GM 物品搜索、详情与图标接口，用来固定 SD-8a 的"不放宽 GM 接口"。另外，SD-1 说"每条路由都在串行执行器上运行"，但 GM 物品目录是 `OutsideGameplay` 只读路由；玩家物品目录按 GM 的同类做法处理即可，不必强行放进执行器。
- **R-3（T-005）**：RQ-011 的"至少一项附件或正文"应在玩家层、`Normalize` 填入默认正文之前判定；玩家物品搜索的范围应与 GM 邮件的选择键与允许规则一致，保证搜得到的物品也发得出去。
- **R-4（T-007）**：在编号用户测试流程中注明：玩家发出的装备一律是固定最高品质（沿用 GM 不带选项时的规则），以及重放会返回原结果而不受随后关闭的子开关影响。这两点都是 Plan 选定的行为，用户实测时应当知情。
- **R-5（T-007）**：如果在线收信回退为"待用户验证"，`verification/completion-evidence.md` 中 SC-5 / AC-011 的在线部分须如实记为待用户验证，不得记为已通过。

## 审查范围说明

本审查只批准 Plan 层面的路线与分解，并不证明任何实现正确。T-002 起各单元的细节计划、结果审查（T-001、T-003、T-004、T-005 设为 required）与最终验证仍按配置的关卡进行。
