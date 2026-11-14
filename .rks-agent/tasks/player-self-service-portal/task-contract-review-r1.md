---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server\.rks-agent\tasks\player-self-service-portal\TaskContract.md
contract_revision: 1
reviewer: 独立审查子代理（Claude Opus 5.5，新会话，未参与意图对齐、契约建模或路由选择）
selection_basis: 用户与项目策略未要求最强审查者；按风险选用新鲜、独立、有能力的审查者
verdict: changes_required
---

# TaskContract 审查：玩家自助后台（#329），revision 1

## 权威来源集（均由审查者直接读取原文）

1. 用户原始需求（逐字，由控制方转交）及后续"先给我一个可能的UI设计图在右侧"。
2. 任务授权：助手问"D1–D6 按方案里的建议定吗？"，用户答"ok，写任务契约直接跑吧"。
3. GitHub issue #329（`gh issue view 329`，OPEN）与 #305（`gh issue view 305`，OPEN，无指派人）。
4. 设计方案 `E:\Project\DNF\Document\玩家自助后台设计方案-20261113.md`（含 §12 分步实施与 §14 D1–D6）。
5. UI 设计图 `E:\Project\DNF\Document\玩家自助后台UI设计图-20261113.html`（含玩家页、已关闭页、限额面板、在线会话"踢下线 / 全部踢下线"）。
6. 仓库规则：服务端仓 `AGENTS.md`、`Document/FeatureDevelopment.md`、`Document/DatabaseSchemaMigration.md`；工作区 `E:\Project\DNF\AGENTS.md`。
7. 现状核对：`main` 当前为 `20bcaf37c`（方案基于 `36b014a67`）；`Server/src` 中无 `ForwardedHeaders` / `TrustedProxies`，GM 登录限流取 `Connection.RemoteIpAddress`（`GmAuthenticationEndpoints.cs:44`），`GmAccessBoundary.cs:56` 把回环来源视为安全连接。

## 已核对的意图处置

- RI-1～RI-4（登录、只见自己、加点券、发邮件）：in_scope，与原话一致；"点券"含代币券由方案与设计图支持，用户已认可。
- RI-5（admin 随时关闭）：in_scope，SC-6 覆盖"关闭后新登录被拒、已登录下一请求被拒"。但 issue #329 验收第 3 条中的"重新开启后可再登录"未进入任何 SC（见发现 F5）。
- RI-6～RI-11（D1–D6）：处置与方案建议一致；D3、D4 以 Scope Out + SC-5 / Constraints 落地，可接受。
- RI-12（踢会话）：契约"踢单个或全部会话"与 UI 设计图一致，方案的"指定账号"未单列，可接受。
- RI-13、RI-14（审计可辨认、会话互不串用）：in_scope，SC-8、SC-3 覆盖。
- RI-15、RI-16（#305 拆分）：见发现 F3。
- RI-17、RI-18、RI-19：in_scope，SC-10、SC-4/5、SC-11 覆盖。
- RI-20（仓库规则）：只列了分层、零警告、补测试；`FeatureDevelopment.md` 的用户测试流程与 `DatabaseSchemaMigration.md` 的迁移要求未落到可观测验收（见 F4）。
- RI-21（合并/推送/PR/上线）：处置标签错误（见 F2）。

## Scope Out 交叉检查

| Scope Out 项 | 交叉的意图 | 结论 |
|---|---|---|
| 改密码、注册、看他人、模板/草稿 | 方案 §1"不做" | 有依据，合规 |
| 装备高级选项 | D3 | 有依据（用户采纳），合规 |
| 物品黑名单 | D4 | 有依据，合规 |
| 启动器内嵌入口 | D6 | 有依据，合规 |
| #305 其余部分 | 方案 §8、§12 第 0 步"修 #305（真实 IP / 协议）" | **偏离已认可方案且未说明**，见 F3 |
| 合并、推送、PR、发版、部署 | 仓库流程与授权边界 | 排除本身正确，但处置标签错误，见 F2 |

## 阻塞性发现

### F1 Root Intent Coverage 引用的 Scope In 条目号整体错位

Scope In 实际顺序：1 登录、2 可见范围、3 加点券、4 发邮件、5 操作记录、6 管理员设置、7 审计、8 登录限流、9 前端、10 测试。覆盖表引用错位：RI-5、RI-7、RI-12 引"第 7 条"（实为审计，应为第 6 条）；RI-13 引第 8 条（应为 7）；RI-15 引第 9 条（应为 8）；RI-11、RI-17 引第 10 条（应为 9）；RI-18 引第 6 条（应为 5）。可追溯性是这张表的全部用途，错位会让下游 Spec 按错误条目对账。

### F2 RI-21 误用 `authorized_change`

`authorized_change` 表示有实际授权支持的改变；而该行理由写的正是"未授权合并或上线"。原始需求也没有要求发布或部署。应改为 `not_applicable`（理由：源需求未要求；仓库规则要求用户测试通过、Codex 审核与 CI 后才合并，版本与发布需用户另行授权），并保留 Requires Confirmation 两条。

### F3 #305 的拆分偏离已认可方案，并与 SC-11 有未消解的张力

- 用户"ok"认可的方案把"修 #305（真实 IP / 协议）"列为第 0 步前置依赖；契约只取"玩家登录限流按真实客户端"，其余标为 `preserved_upstream`，但没有写明这是对已认可方案的收窄及理由。#305 作为仍打开的独立 issue，用作 `preserved_upstream` 的承接者可以成立，但收窄必须显式说明。
- #305 建议的机制是在 GM 网页管道最前面接入 `ForwardedHeaders`。GM 与玩家共用同一端口和管道（方案 §3），按这种做法实现会同时改变 GM 的限流键、`Request.IsHttps`、`Request.Scheme` 和 `GmAccessBoundary` 的回环判断，这与 SC-11"现有 GM 功能行为不变"直接冲突。契约没有说明真实客户端识别的作用边界，执行者只能自行取舍。
- 方案 §4.2 的玩家 Cookie"HTTPS 下 Secure"在代理之后同样无法生效，契约对此没有处置。

最小修正（任选其一并写明）：(a) 约束"受信代理解析只用于玩家登录限流的来源键，不改变 GM 管道与 `Request.Scheme/IsHttps`"，并把玩家 Cookie 在代理后的 Secure 明确列入 #305 承接；或 (b) 把 #305 整体纳入 Scope In，补 #305 自己的验收，并把 SC-11 改为"除 #305 规定的变化外 GM 行为不变"。两种都要在 RI-15/RI-16 注明相对方案 §12 第 0 步的取舍。

### F4 缺少既有数据库升级与用户测试两项仓库强制要求的验收

- 本功能新增两张表并给 `gm_audit` 加列（方案 §9.3）。按 `DatabaseSchemaMigration.md`，同一改动必须 `Version`+1、在 `Tool/DbMigrate` 增加 v6→v7 步骤并补 `DbMigrate.Tests`。SC-6 只验证"全新库默认关闭"。已部署的库要经迁移工具升级，迁移后功能是否默认关闭（D1）、既有 `gm_audit` 行是否被标为 `actor_kind='gm'`、数据是否保留，都没有验收。应新增一条 SC：v6 库经 DbMigrate 升级后结构与新建库一致、玩家后台默认关闭、既有审计记录归为 GM、其它数据不变。
- `FeatureDevelopment.md` §4 要求在自动验证后，用当前主干实际使用的数据库（先做一致备份，迁移先在副本上验证）准备用户测试，提供编号测试流程并主动请用户测试。契约只写"本地测试服实机验证"，没有说明由谁测、用哪个库，也没有说明本契约的终点是否包含"准备好用户测试并交给用户"。应写明终点（建议：自动验证 + 本地实测 + 按 §4 准备用户测试并交付编号流程；用户测试结果属于 Requires Confirmation / 后续 PR 前置条件），避免把仅由代理完成的实测当成用户验收。

### F5 issue #329 验收的"重新开启后可再登录"未被覆盖

在 SC-6 中补一句："重新开启总开关后玩家可再次登录，已被清除的旧会话不会恢复。"

## 非阻塞建议

- 方案基线 `36b014a67` 已落后于当前 `main` `20bcaf37c`；按 FeatureDevelopment §1 从最新 `main` 建工作区即可，不需要改契约。
- Assumptions 第 3 条提到可能与 #324、#321 撞 schema 版本号，与 `DatabaseSchemaMigration.md`"按合并顺序重新排定"一致，保留即可。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 逐条对照原始需求、issue #329 验收、方案 D1–D6 与 UI 设计图：核心意图齐全，越权、会话隔离、幂等、限额都有可观测 SC；但存在 F1–F5 | 修正前，下游 Spec 会继承错位引用、#305 边界歧义和迁移验收缺口；后两项会直接影响线上升级与 GM 回归判断 |
| efficiency | proxy | 范围与方案 §12 的 0–5 步一致，没有多余机制；F3 的歧义可能导致返工（GM 管道被改后与 SC-11 冲突） | 先明确边界，可避免实现后再裁定 |
| cost | proxy | 规模由方案推断：两组领域核心剥离、新表与迁移、新边界中间件、前端两套页面、全量测试；契约未额外加码 | 成本与需求相称；补迁移 SC 会小幅增加测试工作，但这是仓库强制要求 |
| cognitive burden | proxy | 契约约 120 行、表格清晰；F1 的条目号错位会让读者反复对账 | 修正编号后读者负担较低 |
| actual human participation | observed | 用户亲自认可方案与设计图，并以"ok，写任务契约直接跑吧"授权执行；此后没有人工审阅过本契约。用户实机测试按仓库规则是 PR 前置条件，契约尚未安排（F4） | 技术审查不能替代用户测试与合并/上线授权；契约需写明把用户测试交给用户的节点 |

## 结论

`changes_required`。F1–F5 都是可在原契约内做小改动修正的缺陷，不需要再向用户追问需求（F3 的取舍属于实现边界的显式化，不改变 #329 的用户要求）。修正后 revision 必须递增，并重新审查。
