---
kind: rks-review
verdict: approved
disposition: aligned
---

# 累积工作漂移审查：玩家自助后台（#329）与受信代理（#305）

```yaml
kind: rks-review
verdict: approved
disposition: aligned
```

## 审查对象与依据

- 目标：分支 `task/player-self-service-portal-329`，`git log 20bcaf37c..HEAD`（规划提交 `b5942637d` 之后共 8 个实现提交，HEAD `0fd7b39d1`），`git diff 20bcaf37c HEAD`（109 个文件）。未跟踪的 `verification/`（主控放置的 9 张浏览器截图）只作旁证。
- 依据：`TaskContract.md`（revision 2）、`spec.md`、`plan.md`（task-units，SD-1–SD-10）、`reviews/plan-review.md` 的 R-1–R-5、`tasks/TASK-01`–`TASK-07` 的计划、Result 与独立快审；UI 设计图 `E:\Project\DNF\Document\玩家自助后台UI设计图-20261113.html`（只核对登录页文字）。
- 准则：reviewing-work 的 Drift 准则。只判断累积工作是否仍对准已批准的目标、边界与路线，不重复各单元的功能正确性审查，也不代替完成核验。
- 审查者独立于计划与实现。只读核对，没有改动除本文件外的任何文件，没有启停进程，没有读取 `agent-credentials.env`。

## 结论

累积工作仍在 TaskContract 的范围与已批准的路线上。T-001–T-007 都已落成对应的 TASK，Spec 的 RQ/AC 都有承接。SD-1–SD-10 的核心决定都按原样实现。Scope Out 五项都没有被触碰：分支未推送，没有 PR，没有改 `appsettings`、`csproj`、`package*.json`。

几处超出单元所有权边界或扩展共享决定的改动，在 TaskContract 的授权范围内都有依据，也都已在各 Task 中披露并经独立快审接受，不构成漂移。仍欠的是 T-007 中由主控负责的收尾产出：证据汇总、用户测试服启动与交付、在线收信由用户验证、AC-051 的一个失败用例。这些属于尚未完成的计划内工作，不是方向偏离，但在声称完成之前必须补齐（见“未完成的计划内产出”）。

## 工作包覆盖（Plan T-001–T-007）

| 工作包 | 承接 | 提交 | 覆盖与对照 |
|---|---|---|---|
| T-001 受信代理 | TASK-02 | `222c47fa7` | 实现了 SD-4：`GmWeb:TrustedProxies` 非空时，才在后台监听端口上用 `UseWhen(LocalPort)` 接入 `ForwardedHeaders`（`ForwardLimit=1`，清空 `KnownIPNetworks`/`KnownProxies`）；未配置时管道不变。RQ-030–032、RQ-034，AC-030、AC-031（GM 侧）、AC-033、AC-034 有用例。`GmAccessBoundary` 只加了一行说明，网关没有改动。 |
| T-002 表结构与持久化 | TASK-01 | `c23bbb92d` | 实现了 SD-6：表结构 v7，新增三张表，`gm_audit` 加两列；DbMigrate 新增 `[6]`，迁移幂等，加列前先查列是否存在；设置不落行即缺省关闭；只存成功操作；`UsageAsync` 按币种汇总。RQ-050、AC-050 有覆盖，`DatabaseSchemaMigration.md` 已补上一条。 |
| T-003 身份与管理员控制 | TASK-03 | `d7bfa8144` | 实现了 SD-1、SD-1a、SD-2、SD-3、SD-5（玩家实例）、SD-9。两前缀共用同一个边界，总开关先于会话判断，Cookie `Dfo.Player.Session`/`Path=/api/player`。防伪令牌按身份绑定（`player:` 前缀），两个限流实例独立。管理员设置版本冲突返回 409（R-1），AC-003 用例动态枚举两侧路由（R-2）。RQ-001–007、020、021、023、024、033 有覆盖。 |
| T-004 加点券与记录 | TASK-04 | `7f0d58d9b`、`79f7cb4b5` | 判定顺序按 SD-1：重放先于子开关与限额。复用 SD-8 规定的 GM 点券规则（`IGmCeraRuleService.Decide`、钱包、提醒）。限额按币种分开计数，按游戏日切。回执带公共五字段。RQ-007/010/012/013/022/024（点券部分），AC-008(a)(c)、AC-010/012/013/021/022/024 有覆盖。 |
| T-005 发邮件与物品目录 | TASK-05 | `db45667db` | 按 SD-8 抽出 `OfficialMailDelivery`，GM 与玩家共用，GM 邮件回归测试全部通过，真实 PVF 下复跑也通过。SD-8a 的玩家目录见下文 N-2。"至少一项附件或正文"在 `Normalize` 之前判定（R-3）。归属判定的四种拒绝回答一致，请求体拒绝未声明字段（RQ-011 装备高级选项）。在线提醒沿用 GM 的 `MailCreated` 事件路径。 |
| T-006 前端 | TASK-06 | `d6cd73cbf` | 实现了 SD-10：`Portal` 顶层壳；登录页切换玩家 / 管理员，默认玩家，总开关关闭时不显示玩家选项；玩家三页加已关闭页；结果未确认时用原编号重试；管理员「玩家后台」页；审计页加"玩家自助"筛选。`ItemPicker`/`itemInfo` 按来源区分前缀，GM 侧缺省不变。RQ-040–044、AC-040 有覆盖，既有 GM 前端测试原样通过。 |
| T-007 集成验证与用户测试准备 | TASK-07 | `0fd7b39d1` | worker 部分已完成：全量构建与测试、DailyRefill 测试改为放宽等待、发布、两份独立的库副本迁到 v7、两个启动脚本（端口在 43xxx）、编号用户测试流程文档（含 R-4、R-5、限额提醒）。主控部分尚未完成，见下文。 |

结论：七个工作包都已落成，依赖顺序与 Plan 一致（先 T-002，再 T-001，再 T-003，之后 T-004、T-005，最后 T-006、T-007），没有出现绕过 T-003 授权骨架的写操作路径。

## Spec 与 TaskContract 覆盖

- RQ-001–007、010–013、020–024、030–034、040–044、050–051 都有实现和对应用例，AC-001–AC-050 在各 Task 的验收对照表中逐条落到具体用例。
- SC-1–SC-11、SC-13 的自动化部分已具备。SC-6/SC-8/SC-10 的浏览器部分在 `verification/browser/` 已有截图：登录页关闭态、玩家账号、加点券、发信、记录、已关闭页、审计"玩家自助"筛选、手机宽度的账号页与发信页。逐条对应的文字证据还没有写（见下文）。
- RQ-060/AC-060：原库路径 `...\bin\Release\net10.0\Data\dfo-server.db` 取自主干 Release 的 `Database:Path`，与前几个功能测试使用的主干库来源一致。原库为 schema v0，经 DbMigrate 迁移 0→7。用两份独立的 backup 副本，用户副本迁移后哈希封存，原库主文件与 `-wal` 的哈希未变。后台用 `localhost` 访问，与主干的 `127.0.0.1` 分开。这些都符合 Spec。
- Scope Out 1–5 没有越界：没有改密码、注册、模板等功能；没有装备高级选项（请求体拒绝未声明字段）；没有物品黑名单；没有启动器入口；没有推送或 PR（`ahead 9, behind 17`，远端没有本分支）。

## 已批准决定的执行情况

- SD-1–SD-10 的核心决定都按原样实现，没有被推翻。判定顺序、原子性、拒绝契约、两前缀共用边界、会话与 Cookie、代理配置、两个限流实例、v7 表结构、分层（Application 不引用 Infrastructure）、复用 GM 规则、审计 `actorKind`/`accountId`/`actor` 筛选、前端顶层壳，都与 Plan 一致。
- Plan-review R-1–R-5 都已承接：R-1 对应 TASK-03 的显式操作集合分支并返回 409；R-2 对应 AC-003 的动态枚举，玩家目录写成执行器外只读路由；R-3 在 TASK-05 处理；R-4 写进流程文档第 7、10 步；R-5 由 TASK-07 交接，主控须照此记录。
- 各 Task 中经主控裁定的本地决定，例如 TASK-03 的 `portal_closed` 用 403、设置冲突用独立的拒绝类型，TASK-04 的提醒失败仍按成功作答，TASK-06 的 ★1/★2/★4 与内联检索，TASK-07 的 UDP 处理，都在 Spec 允许的局部设计自由内，不改变验收含义。

## 超出单元边界或扩展共享决定的改动（均判为有授权依据，不阻塞）

### N-1 `AuthenticationInputRules.ValidLoginId` 抽取（TASK-03 越出单元所有权）

- 事实：`Server/src/Dfo.Server.Application/Authentication/AuthenticationInputRules.cs` 把原有的命名规则条件提成 `ValidLoginId`，`Validate` 改为调用它。逐项核对，条件等价（非空、≤256、去首尾空白后不变、可打印 ASCII 且不含 `?`）。
- 授权依据：TaskContract 的 Authorization 覆盖"服务端与前端改动"以实现 Scope In。RQ-024 的脱敏要求单独判断"登录名是否符合命名规则"，这次抽取直接服务于 Scope In 7。改动不改变行为，启动器回归全部通过，TASK-03 Result 已披露，独立快审 R1 判为"影响为零，接受"，随后由主控提交。
- 遗留：它绕过了 TASK-03 契约"越界先停下"的流程（契约只对 Persistence 写明了 Blocker 规则，对 Application 列出的是具体文件），属于单元层面的流程偏差，不是 TaskContract 层面的范围蔓延。不需要返工。

### N-2 共享决定在单元内被扩展：SD-8a 由三条路由变为五条，SD-1a 增加 `forbidden`

- 事实：Plan 写明"单元内不再改"。TASK-05 经主控裁定补入 `GET /api/player/v1/items/icon-pack` 与 `items/{id}/tooltip`；TASK-03 为边界上的传输与跨站拒绝新增错误码 `forbidden`（403）。
- 判断：两者都是对原决定的补全，不是改向。新增路由都是只读的，授权与原三条相同（总开关、会话、邮件子开关），实现与 GM 共用，没有放宽任何 GM 接口；AC-003 的枚举用例覆盖了它们。`forbidden` 补的是 SD-1a 原清单没有覆盖的拒绝类别。两者都服务于 Scope In 4、9，并已在 Task 中记录。
- 建议：主控在 `completion-evidence.md`，或 Plan 的勘误中记一句"SD-8a 实际为五条路由，SD-1a 增加 `forbidden`"，免得日后按 Plan 原文核对时误判为缺失或多出。不阻塞。

### N-3 GM 登录页文字删除（TASK-06 ★2）

- 事实：`LoginView.tsx` 左侧品牌区由"86JP 管理台"加标语、两行说明，改为只显示"86JP"；表单下"会话固定 8 小时…"一句也删掉了。纯管理员登录页（玩家后台关闭时）同样受影响。
- 授权依据：Scope In 9 写明"布局依据 UI 设计图"，用户看过设计图后回复了"ok"。本审查核对了设计图，`login-art` 只有 `<h2>86JP</h2>`，没有上述标语和说明（全文检索为 0）。这一项是 TASK-06 的 ★ 项，经主控确认后执行。RQ-051 约束的是 GM 的接口与结果，界面文字不在其中；删掉的说明文字也符合用户"界面不放解释文字"的偏好。
- 建议：用户测试流程或证据里可以顺带说明，GM 登录页的文字随设计图做了精简，免得用户把它当成回归。不阻塞。

### N-4 `OfficialMailDelivery` 抽取

- 由 Plan SD-8 明确规定，并列入 TASK-05 的所有权边界，不属于越界。独立快审逐行核对，抽取与原逻辑等价；变异核对（删去 `AssertMailWireRangeAsync`）证明 GM 回归测试确实经过抽出的组件。

### N-5 玩家侧与 GM 的有意差异

- TASK-04：提醒失败时仍返回 200 并带 `reminder: "failed"`，GM 在同样情况下返回 500。这项经主控确认，RQ-010 要求的"沿用 GM 的提醒"是指复用提醒服务本身，这一要求已满足。
- TASK-05：悬停提示接口在 `ArgumentException` 时，玩家侧返回 400，GM 侧返回 500。
- 两处差异都只在玩家侧，GM 行为没有变化。

### N-6 记录上的歧义（不影响实现）

- TASK-03 的 Result 写"含协调方对本地决定 4、6 的裁定"，但计划中标"需协调方确认"的是第 5 项（更严的登录名脱敏），标"协调方裁定"的是第 3、7 项，编号对不上。实现采用了第 5 项的更严做法：存在的账号才记录登录名与 ID，其余一律写占位文本。这比 SD-9 的字面要求更严，但没有违反 RQ-024/AC-024，因为不存在的账号本来就没有账号 ID，而 Spec 也没有要求记录猜测的名字。
- 建议：主控在 TASK-03 或证据中更正编号，明确第 5 项已经确认。不阻塞。

## 未完成的计划内产出（不是漂移，但完成前必须补齐）

1. `verification/completion-evidence.md` 还没有写。Plan 的 Verification 要求它逐条对应 SC-1–SC-13，包括 AC-041 的截图说明：每张截图对应哪一步，390px 下是否完成了记录与登出。
2. SC-12/AC-060/AC-061：用户测试服还没有启动，也还没有交付给用户。流程文档的"就绪情况"一栏待主控填写；UDP 12311/12312 被其它会话占用，是启动的前置阻碍。若要停掉其它会话的服务或关闭 UDP 探测，按 TaskContract「Requires Confirmation」和 TASK-07 的说明，应先征得用户同意。AC-060 的"测试服务可连接"目前只有只读打开的证明，Host 实际打开有待启动时补证。
3. SC-5/AC-011 的在线实机收信：按 R-5，必须记为待用户验证，不得记为已通过。
4. AC-051：全量测试中有 1 个失败，`MirrorDungeonEntryTests … (boundary: "quota")`。TASK-07 已把原因归于主干既有的时刻依赖缺陷（用自然日写入，而服务按游戏日 06:00 日切查询），本分支没有触及相关路径。完成证据必须附上这一说明，或在北京时间 06:00 之后复跑确认。
5. 用户反馈之前，不得声称已通过实机验收（Constraints / SC-12），也不得建 PR。另外分支目前落后 `origin/main` 17 个提交，建 PR 前要按仓库规则先变基。这一项在当前 Scope 之外，只作提醒。

## 判定理由

- 没有发现遗漏的 Spec 结果、越出 TaskContract 的范围、被推翻的已批准决定，或互相冲突的 Task 决定。
- 上面列出的越界与扩展改动，在实际效果上都服务于 Scope In，不改变验收含义，GM 侧行为有回归测试守住，且都已披露并经独立快审接受。
- 剩余事项都是 Plan 中已经排定的 T-007 主控部分，属于尚未完成的工作，不是方向偏离。

因此判定为 `approved` / `aligned`。本结论不代替 T-007 收尾后的完成核验，也不代替用户实测。
