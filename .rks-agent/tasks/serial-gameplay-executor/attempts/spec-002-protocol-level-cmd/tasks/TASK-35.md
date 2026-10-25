# TASK-35 — 剩余四个 opening 协议归并

本 Task 承接 Plan P7 T-004 及 §4 四 opening whole／六 replay owner 前置要求，迁移 CompoundItem、LotteryOpening、VendingMachine、EpicCraft。对应消息 `65561/65563/65751/66323` 与各自同名旧细命令四行；不关闭其他来源、保护或过渡行。TASK-33 已独立修复 NoRoom，TASK-34（`6bb6a8e8`）完成入口文档收拢；前序机制可直接复用，本次取证并入迁移而不另建调查 Task，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-34（已提交），沿用已批准 B6／P7 与 TASK-32 调查的边界，不冒充其旧 encoder 已验证。
- Outcome: 四消息各由唯一协议 CMD 吸收原 handler／delivery／fine 的编排，薄 handler 只提交原 request/context；删除各自旧 fine、注册与纯转发，显式请求／footprint／原事务和帧序保持。
- Boundary: 四切片、实际接线、必需的领域直接入口与测试、八条迁移账行、AGENTS 清单和本 Task。复用现有 runner／单元／登记能力，不修改共享 core、扩大恢复来源、raw／self 权限或引入新 ambient、scope 模拟。共享命令仍有未迁调用者则保留并明确归属。
- Intentional change: whole 请求／scope 生命周期按已批准 VC-B6；保持各自原动作与健康确认回滚 ACK，whole scope／独立 premium 失败按既有默认精确参与者保护，不假称纯方法移动。若出现批准范围外的可见差异，先报主控；独立纯搬迁不可混入语义提交。
- Result review: required；一次 sol6.1 执行，一次独立 Astra 快审，无逐 Task 挑战。

## Detailed Plan

1. 在旧产品上补正式表征，复用现有 fixture／OpeningProtocolCommandFlowTests：各路真实注册 decoder→dispatcher→Main／SQLite 的健康／拒绝、资产与帧序；各路 committed replay 的实际 CurrentStateBatch encoder 固定故障，核原 cause、同 entry／Ordinal、Committed、不二次结算／无新增 fence 和真实来源关闭。旧证据先通过才切对应路由；保存本机日志与提交号引用，不复制源码或整库快照。
2. 各 whole 用原领域入口、一次 `WriteResultAsync(MainSettlement, [精确 actor])` 和活跃 unit.Footprint；只在原动作恢复范围窄 catch，RequireRollback(cause) 后返回非空结果，单元完整结束才映原 ACK。守卫／登记／cleanup 不可恢复；action SQL 和真实 SQLite fallible Commit 确认回滚分开证明。Lottery mode1 原 Main 后独立 premium ReadOnly 保持，不能并事务或调用旧 bus。只取得各自已证的 committed replay 许可，不增加首成功许可。
3. 同正式测试在新路由重跑：健康和回滚资产、错误码／完整帧序、同 entry／Ordinal retry、当前投影、取消／不确定／rollback 第二因、提交后 scope 与 premium 失败、真实 replacement 物理隔离；以既有有效保护用例补具体受影响路径，不能用未触发 fixture 宣称通过。按真实归属删除旧命令／delivery，并更新有限注册／架构消费者判据，新增四 WriteResult 消费者由十到十四。
4. 最终 Release 构建零警告错误；在 require_escalated 正式环境按项目串行跑受影响 Application／Protocol／Networking 全项目与聚焦用例，保持各程序集原并行／5m，console detailed＋DFO_GAMEPLAY_ASSERTIONS。准确列两条旧 Dimension 基线，任何新增失败单独处理。T-004 尚有 Rename／Hatch／SharedEffect／GenCeraTicket，本单元不冒功能簇结束；簇末仍须八项目 full。
5. 仅闭合八条实际交付 ledger 行，指向本 Task／正式测试；使用既有 migration-ledger.py validate／selftest，不为过检放宽工具。AGENTS 仅增清单，Spec／Plan 保持。写一页 Result 后独立快审，主控提交，再继续 T-004 其余四消息。

## Result R2

Outcome: done

**交付。** 四消息 `65561/65563/65751/66323` 各归唯一 whole CMD，handler 只传原 request/context，四旧 fine／注册和 LotteryOpeningDelivery 已删。原 Main／足迹／ACK／资产／帧序保持；Compound 参数转槽、Vending 非法输入仍在 Main 前拒绝，Lottery mode1 保 Main 后独立 premium ReadOnly。共享 ReadPremiumSnapshotCommand 仍归 premium report／timer；WriteResult 消费者 10→14。runner、raw／self、Spec／Plan 不变，AGENTS 只增一行。只闭八 ledger 行：消息 57 closed／102 pending／14 retained，命令 45／159，来源 1／72／12；保护 5、过渡 8 仍 pending。

**R1 退回与修正。** R1 的原 25 例使用关闭登记 sink，不能证明真实 transport 关闭；历史日志 `physicalClose=True` 是超额表述，原日志保留，当前测试已改为 `registeredClose=True`。R2 只改测试／记录，新增 11 例经同 provider 的原 outbox／实际 encoder／原异常出口，自动提交至生产 OutboundSender 和按 context 引用寻址的受控 IOutboundTransport，直接观察 SendAsync／CloseAsync；不手工 flush 或补链。五 replay owner 路径（四协议，Lottery0/1）各为实际 send→close、同原 cause／entry／Ordinal／Committed 且无新增 fence；四 whole scope 为已保帧发送后关闭，独立 premium 失败只关闭、不发成功帧，二者保 typed 原 cause 与精确 fence。same-id 不同 context 的原物理连接实际关闭 1 次，替换连接关闭 0 次。真实关闭后不在该连接重发；原登记测试只证明登记／账本重试。whole scope／premium 的默认保护仍属已批准 VC-B6，无产品语义返工。

**检查。** R1 旧表征 13/13、迁后业务聚焦 102/102、最终登记／生命周期 25/25 与三 full 原日志均保留。R2 原始日志在本机忽略目录 `verification/logs/task-35/r2/`：以 `/private/tmp` owned diff 保护五产品文件，临时恢复 `6bb6a8e8` 四旧切片＋ProtocolModule，实际注册逐例断言 legacy（新 whole 文件虽存在但未注册）；旧 production sender oracle **6/6**，随后 finally 精确恢复五文件。`old-sender-run.json` 记 baseline／argv／UTC／exit0／恢复结果；未改旧日志或提交源码副本。

恢复后正式 require_escalated、原程序集并行：`dotnet build Server/86JP.Server.slnx -c Release -m:1` **0警告／0错误**；`DFO_GAMEPLAY_ASSERTIONS=<各轮目录> dotnet test Server/tests/Dfo.Server.Application.Tests -c Release --no-build -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，加 `--filter FullyQualifiedName~RemainingOpeningProtocolCommandFlowTests` 的聚焦 **36/36**；Application 全量 **5341＝5323通过＋2失败＋16跳过**（exit1），均自然结束，见 `formal-runs.json`。比 R1 精确增加 11 例，旧业务断言不变。两失败仍为既知 Dimension 内容基线，无新增失败：
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

本轮 Application 原 violations.py 逐类期望通过：61反例＋1控制，outside／exe／无测试归属0。产品精确冻结不变，Protocol **1778通过**、Networking **199通过** 明确引用 R1 正式结果，未冒充本轮新跑。ledger strict validate／八坏例 selftest、八 canonical 行及 baseline 原字段不变、`git diff --check` 通过。

**遗留。** 待 Astra R2 定点复核；两条 Dimension 基线、T-004 其余四消息、簇末八项目 full 与 A21 实机验收仍未完成。未运行游戏／部署，未提交／推送。

```yaml
kind: rks-verification
decision: manual_required
```

独立 R2 结果复核尚未执行。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: changes_required
```

评审对象：Contract revision 1／本次 Result，`6bb6a8e8` 上当前完整未提交 owned diff（四切片及四 whole、ProtocolModule、受影响测试、八条 ledger、AGENTS 一行）。按 reviewing-work 的 Task Unit＋Code／Deliverable 判据做局部快审；依据 process-reset、P7 §4 和 VC-B6，不作完整 Spec 审查、无挑战。

**唯一阻断：实际发送／关闭证据仍停在登记替身。** `RemainingOpeningProtocolCommandFlowTests.cs:69–85` 的 replay 用例只断言 `Outbound.Entries` 中有 close，随即在同一 fixture connection 重发，并输出 `physicalClose=True`；scope／premium／replacement 用例也只检查此记录器。其 `ShopFixture → RecordedOutbound.AddTo` 实际注册的是 `XoSubmissions(new Discard())`（`Server/tests/Support/RecordedOutbound.cs:59–77`），`Discard.Submit` 是空方法（:423–428），没有生产 `OutboundSender` 或物理连接 `CloseAsync`。因此这些测试能证明原 cause、登记条目、账本和同 id 的 context 引用隔离，却不能证明 P7 六 replay owner 前置及 VC-B6 明确要求的真实 sender 关闭、已有帧实际发送→关闭次序或物理替换隔离。三项目 full 中既有 TCP replay 用例只测 UseStackable 健康重放，不能替代本四路故障出口。当前 Result 的“真实来源关闭／same-id 物理连接隔离”以及日志 `physicalClose=True` 均超过证据范围。

最小修正：保留现有测试作为受控登记／账本重放证据，纠正其输出与 Result 措辞；补本四路旧／新 replay 的生产 `OutboundSender`＋可观察 `IOutboundTransport/IOutboundConnection.CloseAsync` oracle，并对新增 whole scope／premium 出口核实际发送与关闭顺序，至少一例真正同 id 不同物理连接替换不误关。可复用现有发送器测试设施，不要求实机、不改共享 core、不把已实际关闭连接冒作可重发连接。旧 route 证据从 `6bb6a8e8` 取得，不能只补新路径后追认旧许可；补测后更新受影响正式结果并交 R2 定点复核。

其余核查无明显局部缺陷：四条 Main／活跃 footprint／窄动作 catch／native commit 原 cause 映射符合 P7；Compound／Vending 早拒绝不 claim Main；Lottery premium 独立 ReadOnly 顺序保持，共享 fine 仍供 report／timer。旧业务测试仅换直接领域 driver，原断言未削弱；runner 未改，ledger JSON 比较只八目标行变化。已直接读取旧13／新102／最终25日志、build 0警告0错误及三 full 的自然结束记录：7307＝7289通过＋2失败＋16跳过；两失败均为既知 Dimension `7100` cell `(1,0)` 无 Left 入口内容异常。DFO 为67反例＋3控制、其余0；strict/selftest八坏例及本次 `git diff --check` 无异常。以上不代表本门通过，也不替代最终簇末全量或 A21 验收。

## Independent Quick Review R2

```yaml
kind: rks-review
verdict: approved
```

定点复核 Contract revision 1／Result R2，以及 `6bb6a8e8` 上当前未提交成果；沿用 R1 的 Task Unit＋Code／Deliverable 判据和 P7／VC-B6 边界。R1 原文保留，本轮仅裁决其发送／关闭证据缺口及直接回归，未扩大为完整 Spec 审查。

**R1 唯一阻断已闭合。** 已逐段检查新增 `Production_sender_*` 三组测试及 `SenderProbe`：fixture 最后注册的 `IOutboundSender` 是 `XoSubmissions(生产 OutboundSender)`；同 provider 的 `GameplayOutboxes`、原 adapter／encoder／异常出口自动投递至它，未手工补 close 或 flush。受控 transport 按 context 引用区分两个同 id 物理连接对象，SendAsync／CloseAsync 直接记录且等待真实 sender 的完成报告。因此不再只拿登记条目当实际关闭。

直接核对 `verification/logs/task-35/r2/old-sender.log` 与 `old-sender-run.json`：五路径逐例为 legacy 注册（四协议含 Lottery0/1），原 cause／entry／Ordinal／Committed 保持，实际 send→close；replacement 原连接关闭1次／替换0次，共6/6。baseline 为 `6bb6a8e8`，恢复记录为成功；旧日志未改写。新 `new-sender.log` 为11/11：五 replay 同样 send→close，四 whole scope 已保留帧发送后关闭，独立 premium 仅 close；same-id 替换再次证明旧1／新0。实际关闭测试没有再用已关闭连接重发；原重放测试保留在登记层并改称 `registeredClose=True`。Result R2 明确受控 transport 的证明范围，不冒 A21／真实 TCP 验收。

最终 build 0警告／0错误，`new-focused.log` 36/36；Application full 自然结束为5341＝5323通过＋2失败＋16跳过，两个失败仍是 R1 所列 Dimension `7100` cell `(1,0)` 缺 Left 入口，DFO 逐类检查无额外／exe／无归属记录。Protocol 1778、Networking 199 明确沿用 R1 的同产品正式结果；本轮未修改共享 runner 或扩大 raw／self 权限，`git diff --check` 通过。未发现本次补测造成的明显局部回归或剩余阻断。

本批准仅解除 TASK-35 的局部结果门；T-004 剩余消息、簇末八项目 full、已知 Dimension 基线及最终 A21 验收仍按原责任保留。评审只追加本段，未修改产品、Result 或提交状态。

## 主控消费与核验

```yaml
kind: rks-verification
decision: pass
```

仅判定 TASK-35 局部交付：已核 R2 Result、实际四 whole 差异、正式日志及 Astra R2 approved。R1 的物理发送证据缺口已由生产 sender 的旧／新正式测试闭合；产品未因返工改变，App 新 full 与未变产品的 R1 Protocol／Networking 来源明确。两条既有 Dimension 内容失败保留，不宣称八项目 full 或实机。迁移账确为八行且 original 全保持，AGENTS 仅增清单；提交后继续 T-004 其余四消息。
