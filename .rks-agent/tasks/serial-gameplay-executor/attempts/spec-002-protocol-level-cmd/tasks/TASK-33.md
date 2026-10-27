# TASK-33 — 租装备满包时正常确认回滚的记账修复

本 Task 承接 Plan P7 §4 的独立 NoRoom 前置和 T-005，保持六消息 whole 迁移的后续顺序。关联迁移账 `message:66511` 与 `command:Dfo.Server.Protocol.Game.Rental.RentEquipmentCommand`，本次只修语义、不关闭任何迁移行。TASK-32（`da4130c8`）留下旧路径满包回滚后被缺少 Freeze/Projection 判定打断的调查，B6/P7 已批准修复；依 `../../process-reset-2026-10-25.md` 接续，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-32（已提交）。
- Outcome: 旧 Main 正常返回且真实回滚已确认时记为 ConfirmedRollback，Rent 满包返回既有结果码 2；清除满包条件后同帧重试复用原 entry/Ordinal，只完成一次合法租赁。
- Boundary: 仅 legacy `EffectOperationContext.Settled` 正常确认回滚收口、必要正式测试及本 Task 记录；不修改六消息路由、领域规则、mapper/encoder、显式 runner、恢复能力与 raw/self 权限。现有缺事务、未 Freeze/Projection、异常、取消、不确定及第二失败保护保持。
- Intentional change: Spec VC-B6 已批准的 NoRoom 正常拒绝替代旧错误关闭；独立语义提交，不混纯搬迁。
- Result review: required；执行 gpt-6.1-sol，一次独立 gpt-6-astra 快审；不生成逐 Task 挑战。

## Detailed Plan

1. 核对本工作区 AGENTS、P7 NoRoom 段与真实 UOW/Settled 调用链，以及其余正常 RequireRollback 的实际消费者。补正式测试，在未修产品上证明缺陷 RED、原合法与非法状态守护 GREEN；正式 wire 用例须经过实际 decoder/dispatcher。
2. 仅依据已有本次 Main 执行绑定和真实回滚确认事实作最小修正。正式用例核完整相关资产回滚、既有 ACK 字节、无新增保护/关闭、同 entry/Ordinal 重试、缺事务与缺 Freeze/Projection 的非法状态，以及异常/取消/不确定/第二失败不误恢复。
3. 跑聚焦回归及受影响测试项目全量，详细控制台与 DFO_GAMEPLAY_ASSERTIONS 日志保存在 verification/logs；仅 Markdown 结论进 git，不复制旧源码、不新增哈希身份表。不把未结束功能簇冒称八项目全量。
4. 写一页以内 Result（交付、非常规选择、实际检查、遗留），交独立快审；批准后由主控提交。迁移账数字保持不变，后继 TASK-34 收拢 AGENTS，随后按 Plan 迁移。

## Result R1 — 2026-10-25，待独立快审

- Outcome: done（执行完成；独立快审与主控消费见下）。

**交付。** 仅修改 legacy `EffectOperationContext.Settled`：本次 Main 已开始事务且 UOW 已确认真实回滚、正常返回时记 `ConfirmedRollback`；其余提交仍必须 Freeze 并准备 Projection。`For` 的执行绑定、异常／取消／不确定／第二失败路径和 explicit runner 未改。其余正常 RequireRollback 的 BossTower／DungeonAdmission 消费不属于这里的 `IRequestSettlementCommand`，未给它们新增效果恢复能力。六消息路由、领域规则、迁移账均未改。

新增 **7 条正式用例＝Rental wire 1＋结算矩阵 6**，无删除、无修改原有断言。wire 从实际注册 decoder、真实计数器和同 provider dispatcher 进入：32 装备格满、100→97 暂存、真实事务 ConfirmedRollback、全库所有表所有列（含 NULL/BLOB 和 item_slots 实际 rowid）前后逐值相等、既有 Result2 body `01 02 00 00 00`、无成功 refresh／Fence／Block／会话删除／关闭；去满包后同序号复用同 entry／原 Ordinal，只扣一次星并生成一个租赁关系，后续 Committed 当前投影不重复租赁。另核 invalid quote 正常拒绝仍为空提交 Committed。矩阵保持缺事务、未确认回滚又缺投影时失败，并核正常提交／回滚两类结果。

**选择。** 测试只读反射观察 ledger identity 和 fixture 数据库连接，不增产品测试接缝或为观测再 Admit 一次请求。资产比较直接枚举实际 schema，不保存源码副本或整库快照。产品只有上述独立语义修正，没有混入路由搬迁。

**检查。** 日志均为本机忽略目录 `verification/logs/task-33/`，下列名称相对该目录。正式测试用 `DFO_GAMEPLAY_ASSERTIONS=<同目录>/assertions-<项目>-formal`，`dotnet test Server/tests/Dfo.Server.<项目>.Tests -c Release --no-build -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，按项目串行、保原程序集内部并行，全部自然结束：

| 检查 | 实际结果 | 日志 |
|---|---|---|
| 未修产品正式 wire RED（filter `FullyQualifiedName~Full_bag_normal_rollback`） | exit 1；1/1 失败，原 Settled 缺投影→PostCommitPublicationFailure | `red.log` |
| Release 解决方案／最终 Application 构建 | exit 0；0 warning／0 error | `build.log`、`build-final.log` |
| Application 全项目 | exit 1；5305＝5287 通过＋2 基线失败＋16 跳过，2.0426 分钟 | `application-formal.log` |
| Protocol 全项目 | exit 0；1778/1778，3.9136 秒 | `protocol-formal.log` |
| Networking 全项目 | exit 0；199/199，52.6326 秒 | `networking-formal.log` |
| 最终 Application focused | exit 0；108/108，7.8515 秒 | `focused-formal.log` |

focused 在上述命令加 `--filter 'FullyQualifiedName~LuckyStarRental|FullyQualifiedName~EffectOperationReplayTests|FullyQualifiedName~ExplicitEffectCommandTests|FullyQualifiedName~EffectSettlementRegistrationTests|FullyQualifiedName~CommandRegistrationTests'`。三 full 合计 **7282＝7264 通过＋2 失败＋16 跳过**；不是八项目全量。两失败完整身份为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 和 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，与既知内容基线相同。既有 `violations.py` 按 TASK-31 逐类期望检查三正式程序集均 exit 0：67 个具名反例＋3 个控制记录，outside／无测试归属均 0；见 `assertions-*-check.txt`。

首轮误在 sandbox 并发启动三个 full，已保留 `*-full.log`，未自然结束、没有完整通过主张；Networking 五条计时／socket取消失败与 Application `ExplicitEffectCommandTests.A_real_pending_rollback_cleanup_cannot_issue_an_attempt_and_its_failure_keeps_all_causes_and_the_fence` 的 line676 InFlight 完成断言失败均保留。识别并仅结束该轮确切进程树后，改为既定 require_escalated 环境和项目串行正式复跑，上述额外失败全部通过；环境／资源原因未独立确证，未改产品求绿、并行、期限或 Skip。

**遗留。** 无本 Task 已知产品遗留，等待独立快审；仅编码／注册／发件箱观察及真实 SQLite，不声称 TCP／A21 实机或完整原生故障笛卡尔积验收。未闭迁移行，后继仍为 TASK-34；未 commit／push／修改 main。


## Independent Quick Review R1 — 2026-10-25

```yaml
kind: rks-review
verdict: approved
```

**结论：本 Task 局部快审通过，无已证实的阻断缺陷；执行完成，待主控消费并提交。** 此结论不代表 spec-002、功能簇或 A21 实机验收完成。

审查目标为 Contract r1／Result R1，以及 `task/refactor-session-20261021` 当前未提交的四个 owned 文件：`EffectOperationContext.cs`、`LuckyStarRentalWireTests.cs`、`EffectOperationReplayTests.cs` 和本 Task。采用 `reviewing-work` 的 Task Unit、Code 判据；以流程重置文件、当前 AGENTS、P7 §4 独立 NoRoom 前置和 B6 VC-B6 为边界。独立读取完整差异及 UOW、结算行为、领域回滚和 ledger 重试控制路径，未改产品、Result 或提交状态。

- 产品只新增“已开始事务＋本次正常返回前真实确认回滚”收口，保留原执行绑定及非回滚的 Freeze／Projection 门。UOW 先等待回滚，结算层仅在 `next` 正常返回后调用 Settled；异常及 typed 失败仍走既有 Failed／保护路径，没有新增恢复权限或改 explicit runner。
- 新 wire 用例从注册 decoder／dispatcher 进入，直接核满包时暂扣、真实 ConfirmedRollback、全表列及 item_slots rowid 回滚、既有 Result2 字节、无成功刷新和保护；去满包后重用同 entry／Ordinal 只租一次，且区分正常空提交拒绝。六条矩阵守住缺事务与缺投影等非法状态。旧断言未削弱，迁移行未关闭。
- 已核原始 RED 的缺投影栈、两个构建 0 警告／0 错误及正式日志：Application 5305＝5287 通过＋2 失败＋16 跳过，Protocol 1778/1778，Networking 199/199，最终 focused 108/108；新增七例均实际通过。两失败的完整身份及 `7100 / cell (1,0) / entrance Left` 异常与 TASK-31 基线一致。三份 assertion 检查为 67 反例＋3 控制，非预期归属为 0；本次 `git diff --check` 通过。未再次运行全量。

**非阻断事项。** Result 尚未写流程字段 `Outcome: done`，主控消费时补齐即可，不需要产品返工或重测。首轮并发运行的额外失败未独立确证成因，保留为运行条件相关的未闭风险；正式串行项目复跑已通过这些用例，不将其解释成已证明的环境原因。证据仍限真实 SQLite 和编码／发件箱观察，三项目全量不冒八项目或 TCP／A21 实机；其余迁移与最终验收继续由后继单元承担。

## 主控消费

已核 Result R1、四文件差异、正式检查及 Astra 独立 approved 快审一致；补齐 Outcome 字段，无产品或测试变更。接受两条既有 Dimension 内容失败及首轮故障原因未确证的边界；本 Task 不关闭迁移行，按独立语义修复提交，下一单元 TASK-34。
