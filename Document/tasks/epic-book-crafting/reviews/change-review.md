---
kind: rks-review
verdict: approved
---

# 史诗图鉴制作完整代码变更审查

审查日期：2026-10-09。独立审查上下文只读代码，未实施变更、未运行构建或测试。

目标：`task/achievement-state-events`，HEAD/接受基线 `92182b1b7073313df8dc85b5ebaed16661f6f2e7` 上的工作区完整 Server diff，含未跟踪新增代码与测试，共25个文件。按路径排序，将每个文件的 `路径 + 空格 + 小写SHA256` 用 LF 连接后计算的快照 SHA256：`a0d55100015661a2a83ff1fe2ca975250c898b83c3ef9aee1607272ac1634507`。追加复核的测试桩只新增 `TrySpendPiecesAsync => throw NotSupportedException()`，保留只读查询时不得写入的测试约束，生产实现未再变化。后续生产实现发生实质变化须重新审查。

依据：本目录 `../TaskContract.md`、`../spec.md`、仓库及工作区 AGENTS.md，以及 `reviewing-work/SKILL.md`、`references/code.md`、`references/deliverable.md`。审查范围是 CMD0313 实现、既有制作/图鉴/重放路径受影响的交互、测试与接线，以及 protocol/catalog.md、protocol/game/client-to-server/0x0313.md、spec-result.md 的实现与证据边界一致性；部署结果和原客户端实际表现由后续完成验证负责。

追加文档快照 SHA256：catalog `22a38c9c680e38c9fb7fd05fbdb7cf354a863f97fac8ccc56fef0a0bb6c90520`；0313 `9fc7cd011150b4ce5693fed014cee55b62337e3ce25ffd95bbeecefcd8b16f9f`；spec-result `e5f2183c5c9302cc2b94ac5324ec16efbcc2ad838e4169be9851676371a721e3`。三份文档的实现描述与代码一致，捕获请求、静态回包依据、CurrentImplementation 和待实机验证没有混为已验证成功闭环。静态逆向部分按已批准规格核对，未在本次审查重复逆向客户端。

结论：未发现有证据支持的实质阻塞缺陷，无待修正项。

| 重点 | 已检查的决定性证据 |
|---|---|
| 配方与分层 | `PvfEpicBookDefinitionSource.ReadRecipes` 从 output/recipe 读取配方并核对装备与碎片映射；`EpicBookRuleService` 检查成本、目标唯一性和碎片混入材料。协议只解包/路由/投影，规则由领域服务调用。 |
| 身份与成本范围 | `EpicCraftDomainService.CraftAsync` 校验当前选择、活动在线身份及角色归属；材料只扫描 QuickSlot、Consumable、Material、ExpertMaterial、SharedMaterial，不含个人/账号仓库。共享材料通过既有账号容器解析。锁定/过期材料在 `MaterialCosts` 过滤，并由 consumption effect 再校验。 |
| 原子性与单件装备 | 命令接线为 Transactional 且账号/在线用户 Exclusive；碎片 `TrySpendAsync` 与物品 store 复用环境事务及同一 SQLite participant。物品效应预检容量，装备走 `GrantPreparedItemEffect`、`ItemGrantDomainService`、`ItemLifecycleDomainService` 创建独立记录；业务异常由 UnitOfWork 回滚。未发现独立连接、提前提交或提前成功投影。 |
| 结果顺序与归零 | 成功后仅一次 `IEventService.ProjectAsync`；composer 依次返回成功 ACK、0465 mode1 绝对余额、物品更新与全部 cleared slots，共享材料空槽亦包含。碎片归零虽删除存储行，receipt 仍保留 count0。失败分支仅失败 ACK；不确定事务异常继续交由既有安全栅栏处理。 |
| 请求重放 | Request/Command 纳入既有 settlement 链路；minimum needs 包含 Inventory 与 EpicBook。已提交重放由 ledger 调用 Refresh，不再进入制作/ACK；碎片重新查询当前账号余额并用0465 mode0全量替换，不保留旧receipt余额。消耗槽 footprint 使共享材料空槽能重放刷新。 |
| 回归接线 | CMD0313 decoder/handler/encoder/command/notification 均在 ProtocolModule 注册。现有 settlement 注册、selected-session 命令枚举与非法 flag 用例按新能力更新，原断言语义保留。`RecipeCosts` 提取 MaterialCosts 后的既有规则未发生可见语义退化。 |

测试事实：已检查 `EpicCraftTests`、真实PVF配方断言、注册测试及受影响的现有测试源码。独立读取派发方生成的 `E:/Project/DNF/DNF-JP-86-Server/artifacts/epic-book-crafting-20261009/tests/epic-targeted.trx`：81 executed / 81 passed / 0 failed / 0 notExecuted；其中14个 EpicCraft 用例包含真实PVF黄金石制作、SQLite deferred FK 的真实提交失败回滚及过期会话拒绝。审查上下文未自行运行这些测试。

派发方另报告真实PVF710配方的 Infrastructure 用例1 passed、0 skip；完整 Application 首轮3319条中3315 passed、4 failed，失败来自新增协议/命令枚举及2048已变为合法flag后的旧期望，四处已修正并纳入上述targeted回归。追加独立读取同目录最终TRX：`application-final.trx` 为3321/3321 passed、0failed、0notExecuted；`networking-final.trx` 为2/2 passed；`protocol-final.trx` 为74/74 passed，后两项亦0failed、0notExecuted。以上是本次指定项目/筛选的通过事实，不扩大为整个解决方案全部测试已跑。

制作回归使用真实SQLite和生产解包器/dispatcher，覆盖成功扣除、碎片/材料不足、未知目标、背包满、写入和提交失败、锁定/过期材料、并发、成功帧顺序及共享空槽、已提交请求重放。提交失败用例先完成实际制作写入，再插入deferred FK违例，要求Commit抛SqliteException并检查碎片、携带/共享材料和装备恢复。

批准仅表示上述代码审查范围内无未解决阻塞缺陷，不证明部署已经完成、全套测试已经通过或原客户端达到 ClientVerified。数据库保全、回滚程序与新运行身份，以及制作后的客户端提示/UI/重登行为，仍按规格进入后续验证。
