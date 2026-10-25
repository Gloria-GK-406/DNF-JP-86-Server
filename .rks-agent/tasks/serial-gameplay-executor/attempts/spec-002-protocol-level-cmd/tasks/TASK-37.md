# TASK-37 — 租装备与幸运星购买协议归并

本 Task 承接 Plan P7 T-005 与 §4 Rental whole／六 replay owner 的边界，处理 RentEquipment、ChargeRentPoint 两入口及其独立刷新。对应消息 `66511/66512` 和 RentEquipmentCommand、ChargeRentPointCommand、ReadRentalRefreshCommand 三条旧命令；不闭其他行。前一 TASK-36（`e91fa5b6`）已收口 T-004，TASK-33 的 NoRoom 修复继续守护；邮箱已在 TASK-31 完成，市场只有已知出站包、以实际注册复核 T-005 覆盖，不凭包名发明入站任务，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-36（已提交），消费既有 B6／P7 及 TASK-33 正常回滚修复。
- Outcome: 两完整协议 CMD、薄 handler，各保一次 Main 与成功后的独立普通 Write 刷新；三旧 fine／接线／纯转发删净，原 domain、拒绝、资产、帧序与重放保持。
- Boundary: Rental 切片、接线、必要正式测试、五条迁移账行、AGENTS 清单和本 Task。复用 `WriteAsync`，不授 WriteResult 或 self 恢复，不改共享 core／领域规则／外部配置。只有已证旧重放 encoder 出口可声明既有 committed replay 许可。
- Intentional change: 已批准 VC-B6 的 whole scope／独立 refresh 默认失败保护；正常 NoRoom 继续 Result2／ConfirmedRollback，普通拒绝仍原空提交，动作 SQL／真实 Commit 异常仍无 ACK 并关闭来源。若出现范围外可见差异，按 reset 变化表与本 Task 快审处理，不改契约或中心方案。
- Result review: required；一次 sol6.1 执行、一次 Astra 快审，无挑战。

## Detailed Plan

1. 在旧路由正式测试钉住两路成功／拒绝、真实 Main／refresh 提交点、完整资产和帧序；各自 committed replay 的 actual encoder 故障需经同 provider 生产 OutboundSender→transport.CloseAsync 实证后才迁移。保真 SQLite action/native Commit、原 cause、同 entry／Ordinal、Committed／ConfirmedRollback、精确替换隔离，不把 recorder 关闭登记冒物理关闭。
2. Rent Main 在领域调用前 DemandActive，成功后记录 TouchedSlots；Charge 保最低 Inventory|Rental 投影。各自仅成功后 ordinary Write 按 Panel→角色库存→账号库存→clock 读刷新，不能改只读或并 Main。保 Rent 库存→ACK→panel／可选外观，Charge ACK→main库存→panel；请求当前性/精确参与者与异常无 ACK原边界保持。读取共享 refresh 全调用方后删除它，不改 LuckyStarChanged／领域共享调用。
3. 保 TASK-33 正式满包回归；旧测试管线观察器随 fine 删除换实际 domain／事务观察，继续证明暂扣→完整回滚→Result2→同 entry／Ordinal 重试且只租一次。补正常拒绝、scope／第二刷新失败／取消／不确定第二因，核 Main持久结果不逆转、真实已登记帧存活与发送→关闭、仅精确目标保护。
4. 冻结产品／测试后跑 Release build、聚焦、T-005 收簇八项目正式 full；require_escalated、项目串行、原 xUnit 并行／5m、console detailed＋DFO。准确记录两条 Dimension 基线、全部新增失败／跳过和数量，日志本机保留。若改断言使构建输入改变，只重跑受影响检查，不在全量中持续改代码。
5. 只闭五条 ledger 行，validate／selftest／original 与非目标行保持，AGENTS 仅增清单。一页 Result＋独立快审，主控将可独立纯准备和 whole 语义分开提交；收簇报告后继续 T-006 队伍协议。

## Result

Outcome: done

**交付。** RentEquipment／ChargeRentPoint 两消息归唯一 whole CMD，薄 handler 只入队；三条旧 fine／注册／调用者删净。原 Main 后仅成功另开普通 Write 刷新，Panel→角色库存→账号库存→clock、两路帧序、资产、续租 UID／绝对到期、minimum replay、Rent 足迹保留。使用显式 selected.Recipient／selected.DemandCurrent 与 ICommandProjections，不改领域／subscriber／共享 core，不增 WriteResult／self／raw 类型。实际市场接线仅 AuctionService 出站 encoder，无未纳入的市场入站。仅五 ledger 行闭合：消息63／96／14，fine51／153，来源1／72／12；保护5／过渡8仍 pending，original 与非目标行全部不变；AGENTS 仅增清单。

**选择与差异。** 旧 HEAD `e91fa5b6` 正式两路 decoder→SQLite→实际编码登记→生产 OutboundSender→受控 transport.SendAsync／CloseAsync，最终 **30/30** 后恢复 whole 对照；未把 RecordedOutbound.Discard 的登记当物理关闭。没有可独立交付的纯代码抽取；三 fine 删除与 whole 生命周期切换不可分，不夹领域重构。VC-B6 scope／独立刷新默认保护落实，另仅增 Spec 表 VC-B8：首次 required encoder 旧 raw／无 Block，新 typed／精确 Block，原 cause、Main Committed、无发送／实际关闭保持；本矩阵直接断言 Block，不冒称另测 Fence／DiscardExact。正常 NoRoom＋后续 scope 错误仍 **ConfirmedRollback／raw／无 Block**，Result2 先发送后关闭，不能泛化成全部 scope 围栏。TASK33 满包暂扣97→全库回滚→Result2→同 entry／Ordinal 重试只租一次，改为真实 domain／事务观察，原强断言保留。

**检查。** 最终 `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1` **0警告／0错误**。项目串行 `DFO_GAMEPLAY_ASSERTIONS=<独立目录> dotnet test <项目.csproj> -c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，保原 xUnit 类并行；最终聚焦附 `--filter` 覆盖 Rental／旧 wire／注册／经验／架构，**77/77**。动作1811／真实 native Commit787 两路无 ACK＋全库回滚＋来源实际关闭；scope 真 DI IAsyncDisposable、第二刷新异常／取消／独立 rollback cause、原 entry／Ordinal／Committed 与同 ID 替换／无关 transport 隔离均有正式断言。受控 transport 不冒称 A21 实机。

| 项目 | 总数 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|
| Application | 5395 | 5377 | 2 | 16 |
| Protocol | 1779 | 1779 | 0 | 0 |
| Networking | 199 | 199 | 0 | 0 |
| Persistence | 484 | 484 | 0 | 0 |
| Infrastructure | 976 | 974 | 0 | 2 |
| Host | 203 | 203 | 0 | 0 |
| HttpApi | 57 | 57 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 0 |

`5395+1779+199+484+976+203+57+80=9173=9153通过+2失败+18跳过`；对 TASK36 原 display/status/重数 **N=26／D=0**，新增均为 Rental 正式用例。App exit1 只有原两条 Dimension 内容基线：
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`
- `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`

最终八项日志在 `verification/logs/task37-final/`。整轮后只将新测试改为既有 PvfFact／PvfTheory 前置，重建、77聚焦及 App full（`application-pvf.log`）再过，归档存在、用例／跳过数不变；其余七项目不消费该 App 测试文件，来源为同目录整轮。七程序集 DFO **74＝62＋7＋5×1**，control／预期反例齐、outside／exe／无归属0，最终 App 单独62也通过 parser。首诊断轮曾有 Safety 注入架构失败，已由显式 selected 请求守卫替换；Networking 一次 Connection refused 最终199/199未重现，不纳入基线或推定环境根因，诊断日志留 `task37-full/`。

**遗留。** 待独立 Astra 快审；两 Dimension 基线与 A21 验收保持未解。T-005 收簇，后继 T-006；未提交／推送／部署。strict ledger／八坏例 selftest、五行／original／非目标比对、diff check 通过；产品／测试停写交审。

```yaml
kind: rks-verification
decision: pass
```

主控核验仅针对 TASK-37 本地完成声明：已亲读生产差异、Result 与 Astra R1 approved，独立汇总最终八项目 TRX 为9173＝9153通过＋两条准确 Dimension 基线失败＋18跳过；核五条 ledger 及全部 original／非目标不变，diff check通过。新测试属性变更后的 App 证据及其余七项来源如上，未将诊断轮失败或 A21 实机纳入通过声明。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

本结论仅覆盖 TASK-37 revision 1 当前未提交 Result 和所属 11 文件差异（基线 `e91fa5b6`，`task/refactor-session-20261021`），不代表整个 Spec B 完成。按 process-reset-2026-10-25、reviewing-work 的 Task Unit／Code 判据，亲读子 TaskContract、Spec Rental／VC-B6／新增 VC-B8、Plan P7 T-005／§4、实际产品与测试差异及旧新正式日志；未生成挑战、未改产品或测试、未重复全量。

- 两入口各归唯一 whole CMD，handler 仅入队；三旧 fine 类型／生产引用和注册已删。各保一次 Main，成功后独立 ordinary Write 依 Panel→角色库存→账号库存→clock 刷新；Rent 足迹、Charge 最低投影、原帧序、拒绝边界保持。两 owner 仅授既有 `CommittedReplayCurrentStateBatch`，未扩大 WriteResult／self／raw 或共享 core，领域与 subscriber 未改。
- 亲读新增正式测试、NoRoom 原强断言和 SenderProbe：实际 decoder／SQLite／encoder 登记接生产 OutboundSender，再由受控 transport 实际 SendAsync／CloseAsync 观察；真实 DI IAsyncDisposable、SQL1811／native Commit787、刷新异常／取消／独立回滚 cause、同 ID 物理替换隔离有对应断言。NoRoom 仍暂扣97后全库回滚、Result2、原 entry／Ordinal 重试只租一次；NoRoom 后 scope 错误保 ConfirmedRollback／raw／无 Block 和已登记 Result2 存活。
- 核旧 `task37-old/final30.log` 30/30 与新 `task37-final/focus-pvf.log` 77/77。VC-B8 的旧 raw／无 Block→新 typed／精确 Block、Main Committed／零发送／实际关闭对照成立；此矩阵直接证明 Block，不扩称独立观测 Fence／DiscardExact。限定 committed replay 原 cause／原条目和资产不重结算保持。
- 核 `build-pvf.log` 0警告／0错误、最终 `application-pvf.log` 与其余七项目正式日志：9173＝9153通过＋2失败＋18跳过。两失败准确为 Result 列出的 DimensionDungeonTests；最终 Networking 199/199。没有将首诊断轮 Safety 架构失败或一次 Connection refused 算作基线。最终测试证据仍是受控传输，不能代替 A21 实机验收。
- 独立结构化比较 ledger：只改两个消息、三个 fine 行，original 与其他行不变；AGENTS 仅增清单，Spec 仅追加 VC-B8 及对应范围编号。`git diff --check` 通过。

未发现本次局部快审需要返工的缺陷。两条既有 Dimension 内容失败及 A21 实机边界继续按 Result 披露，由主控消费本评审并负责后续提交。

主控已消费 R1：无未解阻断，按本 Task 不含可独立纯准备的事实作一次本地实现检查点。T-005 收簇后继续 T-006，不代表 Spec B 完成，不推送／开 PR／合并。
