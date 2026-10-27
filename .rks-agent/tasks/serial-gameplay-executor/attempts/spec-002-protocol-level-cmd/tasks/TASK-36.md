# TASK-36 — 宠物改名／孵化、共享效果与 CERA 票据入口归并

本 Task 承接 Plan T-004 剩余 Rename／Hatch／SharedEffect／GenCeraTicket，收口物品与效果簇。关联消息 `65636/65638/65787/65601`，以及 RenameCreatureCommand、HatchCreatureCommand、SharedEffectCommand 三条旧命令；其他行不闭合。TASK-35（`36811698`）已归并四 opening，并补齐生产 sender 的旧新关闭证据，本次沿用该实证标准，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-35（已提交）。
- Outcome: 四入口各为完整协议 CMD，handler 只入队；原世界检查、槽位转换、领域动作、投影和错误路径归属正确，三条旧 fine／注册／纯转发删净，共享帮助器仍有消费者则保留并明确去向。
- Boundary: 四入口及必需直接共享能力／接线／正式测试、七条 ledger 行、AGENTS 清单和本 Task。Rename／SharedEffect 保原 Main 与 footprint；Hatch 保原普通写、GenCeraTicket 保无事务及拒绝→提示顺序，不凭名字赋予效果账本或恢复权限。
- Intentional change: 默认保持行为。若旧／新请求生命周期必然产生有意可见差异，按 process-reset §4 在 Spec 变化表追加具体旧新行为、理由和守护测试，随本次快审审查，不另修订 Spec／Plan；不得改契约或中心机制。
- Result review: required；一次 sol6.1 执行、一次 Astra 快审，不生成挑战。

## Detailed Plan

1. 先在当前旧路径补／跑正式表征，覆盖各路成功／拒绝、真实提交点、完整帧序与相关资产；Rename／SharedEffect 的重复请求与实际 committed replay encoder 失败先取得旧直接 oracle。保生产 OutboundSender→受控 transport 的实际发送／CloseAsync及物理替换隔离，不能用只登记的 recorder 冒称物理结果。
2. 迁入唯一协议 CMD，保 Rename 可见广播→自身 ACK／物品、SharedEffect 自身 ACK／绝对库存／效果→其他精确玩家、Hatch ACK／物品／宠物详情及 current-world 门、GenCera 拒绝和提示原顺序。使用现有 typed projections/outbox，不注入旧 sender／bus、造新 ambient 或扩大 raw／WriteResult／self 权限；仅经旧路径实证的既有 committed replay 许可可声明。
3. 旧新用例重跑并补必要动作／提交／scope／取消／不确定、精确收件人和重试保护；原直接 fine 业务测试转领域 driver 时保原断言。TASK-33 的 legacy 状态矩阵目前借 SharedEffectCommand，删除它时用明确测试专用 legacy 形状保留判据，不能删弱该矩阵。结构接线、旧调用检索及有限家族判据同步。
4. 此单元收尾 T-004：Release solution build 零警告错误；require_escalated 环境按项目串行运行八项目正式 full（Application、Protocol、Networking、Persistence、Infrastructure、Host、HttpApi、Pvf）及本单元聚焦，保持原 xUnit 并行／5m、console detailed＋DFO_GAMEPLAY_ASSERTIONS。准确核两条旧 Dimension 基线、全部新增失败与逐项目数量，日志本机忽略目录。
5. 只闭七条实际交付 ledger 行，validate／selftest 及 original／非目标行不变检查；AGENTS 只增清单。写一页 Result，独立快审后主控提交，并向用户报 T-004 四项迁移数量、这一簇有意变化、风险及八项目正式结果；之后继续 T-005 Rental。

## Result

Outcome: done

**交付。** 四消息 `65636/65638/65787/65601` 归唯一 whole CMD；handler 只转原 request/context，三 fine／注册／转发删净。Rename／Shared 保 Main 与足迹，Hatch 普通 Write、GenCera 无事务；帧序、资产、current 门与 replay 保留。CreatureProjectionDelivery 其他消费者保原归属；TASK33 改用 test-only LegacySettlementProbe，矩阵断言保留。仅七 ledger 行闭合：消息 61／98／14，fine 48／156，来源 1／72／12，保护 5／过渡 8 仍 pending；全部 original／非目标行不变。AGENTS 仅增清单，Plan 不改。

**选择。** 旧 HEAD `36811698` 先跑正式 oracle。可独立纯准备 patch：`verification/logs/task36-pure-preparation.patch`（直接 `git apply --cached`），仅 CreatureVisibleRecipients 完整抽取、SharedEffect Compose 抽取后旧 handler 调用；HEAD＋它正式旧路 **27/27**，随后已恢复最终代码。整 Task 快审后可先纯准备、再 whole 生命周期提交。具体有意差异只增 Spec VC-B7：三写路 scope 归 whole 后保原已登记帧、typed 收口；Shared 首次 required encoder 改默认 Main 保护。Rename owner／peer NOTI 错误仍原 catch／逐目标关闭／继续，无新增 owner Block。未扩 raw／WriteResult／self 权限。

**检查。** 最终冻结 artifact 的 `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1` **0警告／0错误**；项目串行 `DFO_GAMEPLAY_ASSERTIONS=<独立目录> dotnet test <项目.csproj> -c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，保原 xUnit 并行。聚焦加 `--filter FullyQualifiedName~CreatureAndEffectProtocolCommandFlowTests` **28/28**。same-provider decoder／SQLite／actual encoder／生产 OutboundSender→transport.CloseAsync 核旧新 owner／peer、scope、取消、SQL action／native Commit、原 entry／Ordinal／Committed、同 ID 替换关闭 0；登记 fixture 的 SQL 回滚重试不冒称物理关闭后重发。最终补强 replay 断言后重跑 Application full，其余七项编译输入未变，沿用本次 full。

| 项目 | 总数 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|
| Application | 5369 | 5351 | 2 | 16 |
| Protocol | 1779 | 1779 | 0 | 0 |
| Networking | 199 | 199 | 0 | 0 |
| Persistence | 484 | 484 | 0 | 0 |
| Infrastructure | 976 | 974 | 0 | 2 |
| Host | 203 | 203 | 0 | 0 |
| HttpApi | 57 | 57 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 0 |

8 项 `5369+1779+199+484+976+203+57+80=9147=9127通过+2失败+18跳过`（16＋2）。App exit1 只有旧 Dimension 内容基线，无新增失败：
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

七程序集断言 **74＝62＋7＋5×1**，control／预期反例齐，outside／exe／无归属 0，parser exit0；最终 App 仍 62。strict ledger validate／八坏例 selftest、七行／original 不变、diff check 通过。本机忽略日志：`verification/logs/task36-full/`；最终 build／focus／App／精确汇总：`verification/logs/task36-final/`。

**遗留。** 待独立 Astra 快审；两 Dimension 基线、A21 实机验收未解。T-004 收簇，T-005 未开工。未提交／推送／启动游戏／部署；产品／测试停写交审。

```yaml
kind: rks-verification
decision: manual_required
```

独立 Task 结果快审尚未执行。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**范围。** 审查 Contract r1／本次 Result、`36811698` 上全部 TASK-36 未提交变更及纯准备补丁；采用 reviewing-work 的 Task Unit、Code、Deliverable 判据，按 process-reset 的新流程连同 VC-B7 一起快审。结论仅覆盖本 Task，不表示 Spec B 封存或实机验收。

**实际核对。** 四入口绑定唯一 whole CMD，三个 fine 及注册删除；Rename／Shared 仍单 Main、原 footprint，Hatch 仍 ordinary Write，GenCera 无事务。shared Compose 与 Creature 可见收件人／发送抽取保原 current、tower、逐目标异常处理；旧 delivery 的其他消费者保留。Rename owner 编码失败不发 ACK；peer 失败只关失败目标，后续 peer 与 owner ACK 继续。测试的 SenderProbe 确实构造生产 OutboundSender，经引用身份解析的 transport 执行 SendAsync／CloseAsync，不是仅记录登记。replay 核原 cause、entry／Ordinal／Committed、资产不二扣及同 ID 替换连接关闭 0；原 TASK-33 legacy 判据换成 test-only Probe，未放宽断言。未改 core、raw 类型、WriteResult 或 self 权限。

**变化与提交边界。** VC-B7 与源码／旧新日志一致：旧 fine scope 三路 raw、无 Block、零发送；新 scope typed、保成功发送后关闭，Rename／Shared Block，Hatch 不增 Block；Shared 初次 required 编码失败新增 Main 默认保护。Rename 可见 NOTI 的 catch 隔离保持。独立在临时目录将 `task36-pure-preparation.patch` 应用于 HEAD 成功，Creature 文件与最终版本完全一致，Shared Compose 方法与最终版本完全一致；补丁只是原算法抽取及旧调用接回，未带 whole 路由／生命周期语义。支持主控先提交该纯准备，再提交其余语义迁移。

**证据。** 已逐项读实际日志：旧正式 15/15、HEAD＋纯准备 27/27，最终 focus 28/28；final Release build 0 警告／0 错误。final Application 5369＝5351 通过＋2 旧 Dimension 失败＋16 跳过，其余七项目 full 与 Result 一致，合计 9147＝9127＋2＋18。scope 日志新 Rename／Shared／Hatch 物理 sends 为 2／1／1，旧均 0，关闭最后发生；不把 batch 数当帧数。七程序集断言 74（62＋7＋5），control 齐，outside／exe／无归属均 0。strict ledger／八坏例 selftest 通过；独立比较 JSON 只有四消息和三命令变化，original 及其他行相同。`git diff --check` 通过。

无阻断发现。保留两条既有 Dimension 内容失败与 A21 未实机验证边界；本次没有重跑八项目全量，评审依据上述本轮冻结源码、正式原始日志及独立静态／补丁核对。主控可消费本 R1 后按约定提交，不据此推送或分支收尾。

## 主控消费与核验

```yaml
kind: rks-verification
decision: pass
```

接受 Astra R1 approved 与当前实际差异、最终冻结测试及 VC-B7 直接旧新证据的一致性；只判定本 Task 与 T-004 收簇，不宣称整个 Spec 或实机完成。八项目 9147＝9127 通过＋2 既有 Dimension 内容失败＋18 跳过，原有断言/数量及七条迁移行已核。纯准备已单独提交 `1438e2f9`（旧路线 27/27），余下 whole 语义、正式测试、变化表与本 Result 随最终 Task 提交。后继 T-005 租赁两入口，原非协议来源与过渡项保持未闭合。
