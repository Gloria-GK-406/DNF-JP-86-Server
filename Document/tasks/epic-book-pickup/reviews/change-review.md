---
kind: rks-review
verdict: approved
---

# 史诗碎片完整变更独立审查

- 审查日期：2026-10-08。
- 目标：`task/abyss-research`，基线/HEAD `a8a168df0a47e43809237daf07bc3e212a0d6946` 上当前未提交的史诗图鉴变更及新增文件。
- 判据：根工作区及仓库 `AGENTS.md`、本任务 `TaskContract.md` revision 1、`spec.md`，`reviewing-work` 的 `references/code.md` 和 `references/deliverable.md`。
- 范围：图鉴领域/规则服务、PVF 映射、账号 SQLite 计数、旧持有迁移、普通地面拾取分流、登录/拾取通知、相关新测试及 `NOTI-0x0465.md`/catalog。
- 排除：预存 `protocol/game/client-to-server/0x0010.md` 与 `Document/abyss-research-20261007/manual-hell-refusal-ui-lock.md`。审查未修改产品代码，未运行 dotnet。

## 结论

批准当前范围。未发现需要修复的阻断缺陷；本结论不代替主代理的最终完成核验，也不构成客户端实机验收。

## 关键核对

| 要求 | 实际检查与结论 |
|---|---|
| PVF 识别/未知 ID | `PvfEpicBookDefinitionSource` 从 `[piece list]` 读取映射；`EpicBookRuleService` 拒绝非正数和重复标识；未知 ID 不进入图鉴到账。已检查真实 PVF 测试的 710 对映射及深渊映射一致性。 |
| 账号累计/只读 | SQLite 主键为账号与碎片 ID；加法先用 long 再饱和到 int.MaxValue；`GetBalancesAsync`/`GetBookAsync` 只读，余额存储无查询时初始化/写回。只读事务拒绝写入，重读和账号隔离测试覆盖该路径。 |
| 普通拾取兼容/满额消费 | 金币分支保持在前，碎片分支在背包查找前；未识别的物品继续原背包容量与授予路径。满额仍 staging 地面消费并返回成功；仅 Added 为正时发送图鉴更新。重复地面编号不再次到账。 |
| 拾取失败原子性 | 图鉴写入使用既有 `SqliteDatabase` ambient 事务；地面使用同一 `DomainTransaction` 的 `GroundDropCommitParticipant`。prepare 全部完成后先提交 SQLite，再提交已准备的地面状态；异常释放 claim，事务撤销恢复余额和地面。新增拾取测试覆盖回滚、普通满包、临界/已满和成功后二次拾取。 |
| 旧持有迁移 | 启动 StartingAsync 中在网络接入前运行迁移。迁移识别 template custody、校验 core/账号/正数量，在一条 SQLite 事务中合并饱和余额并删除旧槽位；删除或校验失败时全部撤销。二次运行因已移除持有不重复计数。保留 `items` core 不等于保留普通持有，且避免删除其他引用。普通物品保留测试与强制失败回滚测试覆盖该选择。 |
| 登录/拾取投影 | 登录投影位置保持 GuildMedal → EpicBook → UsableCount；mode 0 读取账号全量余额，mode 1 发累计值。拾取发 ACK → 地面移除 → 变更余额；其他队员只有移除通知。空余额保留五零基线。 |
| 协议证据边界 | 已直接检查上游 `ServerS4A21` HEAD `c20dba5` 的 `A21UsableCount0465BodyBuilder`，与 `u8 mode + u32 count + (u32 pieceId,u32 balance)` 一致。文档将非空布局标为上游实现证据，并明确尚无本客户端非空实帧/消费者静态确认，不宣称 ClientVerified。 |

## 验证证据

直接检查 `../verification/` 内本轮 TRX 的结果及相关新增测试内容：

- `persistence.trx`：19/19，含饱和/重读/只读、迁移重跑、普通物品保留与失败回滚。
- `protocol.trx`：109/109，含 GroundPickup、SelectCharacter 和图鉴编码/投影。
- `pvf.trx`：3/3，无跳过，含真实档案的 710 对映射及深渊规则检查。
- `application.trx`：424/424，含 Transactions、Characters 与 CommandHandlerArchitecture。
- 主代理报告本轮整套 solution build 为 0 警告、0 错误；评审者未重新运行构建。
- 当前范围的 `git diff --check` 未发现空白错误；仅有既有行尾转换提示。

## 保留边界

非空图鉴全量/增量展示、跨角色与重登显示，以及碎片使用目的槽 0 的 NOTI 0x0027 的客户端实际效果，仍需后续实机验收。当前契约明确不含部署、运行中数据库操作或客户端补丁，这些不阻断本轮批准。

## 续轮：测试补强与当前目标确认

2026-10-08，复审同一基线上的两项测试补强及 `workspace.md` 更新，结论保持 **approved**。产品行为变更沿用前述完整审查；本轮未运行 dotnet，新增断言执行结果由主代理随后 fresh build/test 核验，前述 TRX 统计不作为这些新增断言已经执行的证据。

- `CarriedGoldCompositionTests`：在真实 Persistence/Infrastructure/Application 模块组成的容器中解析 `IEpicBookDomainService`，能检出图鉴领域服务或其依赖注册遗漏，未改变原测试的业务路径。
- `SelectCharacterProductionStateTests.ApplicationProducedResult_EncodesTheCompleteStaticStream`：将 `QueryOnlyEpicBook` 注入实际 `SelectCharacterPreparationService`，通过 `PreflightAsync`/`PrepareAsync` 取得结果后断言余额为 13，且完整流含唯一图鉴消息。写方法和初始化方法均抛异常，可阻止登录准备误用写入路径；余额不是在结果生成后人工填入。该测试与原图鉴消息内容/位置测试共同覆盖领域读取至登录投影。
- `workspace.md`：临时数据库确实由测试写到系统临时目录并清理；TRX 位于任务 `verification/`。已只读计算两份排除资料的 SHA-256，与文档新增值完全一致。

未发现补强引入的缺陷或原批准范围的直接回归，客户端待验收边界保持不变。
