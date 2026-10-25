# TASK-52 — 0x0204 三条消耗分支归并

本 Task 承接 T-007，闭合 GameQuestClearCoupon（66052）以及 QuestClearCouponCommand、UseFatiguePotionCommand、UseEntryAllowanceCommand，共四行。TASK-51 已经唯一 Astra 批准并由主控接受，以967f18e3／530c2a5f分段本地提交，工作区干净。目标未偏离：把同一入站的三条既有业务路径归入一个 whole，保原分支优先级、结算与投影，不提前关闭共享读取命令或事件来源。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-51；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 一个唯一 Protocol／Direct whole 与薄 handler，三个 fine 及仅本入口消费的旧 EntryAllowance／Fatigue handler 编排清零。每次只执行被选中的一个 Main，保原精确 actor／physical、动态 footprint 与 request 原 minimum；原后续只读单元独立保留。
- Nature/Gates: 保持行为迁移；whole 默认生命周期的有意可见差异用旧新正式证据追加 VC，随本 Task 唯一 fresh Astra 快审，不修订 Spec B8／Plan P10。一个 sol6.1 执行，一个 Astra 结果快审，无挑战；核心／契约变化先交主控。纯测试准备独立提交，仍不外部收尾。

## Detailed Plan

1. **先钉旧行为。** 基线530c2a5f先跑 HerasEntryCoupon、FatiguePotion、QuestClearCoupon、RealConsumableIntegration、ItemUsage／UsageQuota 等实际相关强例。补最小正式旧新判据，覆盖三分支真实 decoder／dispatcher、SQLite、production outbox／实际 encoder／DI Dispose，真实消费及任务／疲劳／次数变化、同帧重放资产一次与原 entry／Ordinal、action 与 native Commit 回滚、取消／递归 typed、精确 physical 关闭与替换隔离。既有 Heras 42／58／77 实际物品优先及重放配额、疲劳原 handler 集成、真实券规则／回滚断言保留；不以假 bus 代替生产入口。
2. **先识别实际物品，再看 Action。** 保完整19B、槽位／容器／reserved／Action<=255的原前置条件；先 EntryAllowance，之后疲劳58，最后券77。保入口无session／online拒绝、selected未in-world静默、实际slot查询在原事务外的位置。新增 `Supports(int itemId)` 查询到已有且实际负责 TryUse 的 IEntryAllowanceItemDomainService，由它使用原规则；whole不直接读规则、不内联物品ID、不加转发服务，也不能先开Main只为判断分支。
3. **三个原 Main 与失败语义。** 原三个fine都是Main Transactional，保一个被选分支、原调用及unit.Footprint。原catch对普通action与native存储失败均可拒绝；使用已批准 WriteResultAsync／动作内窄catch+RequireRollback，只有健康确认回滚才能映原拒绝，取消／递归typed／guard／cleanup默认传播，不扩恢复资格。Entry识别前的普通读取失败仍原拒绝；Fatigue的OpeningRejected回拒绝后仍刷新使用量，其他普通失败只回拒绝；券保原日志及拒绝编码。重放最低仍由原request Action决定，不因实际物品或本次经验／任务结果擅改。
4. **保三条投影链。** Entry先TheseSessions OpeningCompleted再Heras UsageQuotaViewChanged；Fatigue先取当前疲劳、TheseCharacters OpeningCompleted含Following Fatigue，再使用量；券先独立尝试任务簿／Mirror合并、再独立尝试当前view／offers，保两处原辅助失败吞并范围，最终原OpeningCompleted顺序。Project返回Failed原先被忽略的地方继续保持。只消费已有 ICommandProjections／outbox与真实领域服务，不注入旧 Delivery。Fatigue使用量原 ReadItemUsageSnapshotCommand 为 ReadOnly、仅IAccount无selected：新ordinary ReadOnly []／None，原InWorld→clock→snapshot、重检、FullSnapshot全量预编码验证+reset／255分块、cooldown排序／向上取整／分块且无reset、每帧InWorld检查均保持。共享纯投影复用；旧ReadItemUsageSnapshot及Delivery仍有选角／日切消费者，不能在此闭合。
5. **闭合边界。** 三fine／旧编排实际消费者清零；唯一消息binding，原CommittedReplayCurrentStateBatch资格只给该whole，不给首次成功raw／Self。原strong测试按实际新入口适配；孤立算法／投影fixture明示范围，保资产、规则、完整帧序与原失败cause。仅四行ledger，AGENTS仅清单一行，必要VC随Result审，其他事件与来源留后继。
6. **冻结交付。** Release solution非增量0警告0错误，聚焦与Application／Protocol／Networking full；若实际共享影响扩大则据实加项目，核心改动先回主控。原类并行／5m／detailed／独立DFO，项目严格串行、唯一输出目录，逐child end／exit核验。对51有效库存逐display／outcome／重数闭合，保两项Dimension与18skip。最终旧源复验先备完整CURRENT、固定HEAD／branch／owned manifest与所有runner结束，由主控读脚本并执行，finally逐文件恢复。Result后strict／八坏例／含新文件diff-check，停写交唯一fresh Astra；主控接受后本地分段提交并继续。

## Result

- Outcome: done（执行结果，等待主控唯一 fresh Astra 快审）。
- **交付：** `GameQuestClearCouponCommand` 与薄 handler 归并三路，删除三个 fine 及两个旧编排；一个被选分支的 Main，保实际物品优先、原 recipient／physical、unit.Footprint 和 request 原 minimum。疲劳使用量保独立 ordinary ReadOnly／[]／None，共享原读取算法，原共享 fine／Delivery 与其他来源不关闭。迁移账只闭四行，AGENTS仅增一行；Spec只追加 VC-B32。
- **非常规选择：** 只为实际拥有 TryUse 的既有 EntryAllowance 领域服务增加 `Supports` 查询，继续从原 definitions 答复并在 Main 外分类。三路初次 Project Failed 仍被原调用方忽略，不改成 required。原 scope 与疲劳独立RO错误归 whole 的默认生命周期差异按 VC-B32 明列，不加 raw／Self 或改核心。
- **检查：** 主控最终旧源复验 App86＝85通过＋1原PVF跳过、Protocol73通过，两个 child exit0，21文件精确恢复。新聚焦同86＝85＋1；最终 `dotnet build Server/86JP.Server.slnx --no-incremental -c Release -m:1 --no-restore` 0警告／0错误；串行三个full：App5708＝5690通过＋2原Dimension失败＋16跳过，Protocol1787通过、Networking203通过。两失败精确为 `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`、`All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均原7100／(1,0)／Left缺图，Message与51逐字相同。逐display／outcome／重数App N30D0、Protocol N2D1、Net N0D0；DFO70＝67反例＋3control，outside／exe／orphan0。实际程序集／DI唯一binding与三fine清零通过；Result后strict及八坏例全通过。命令、child结束状态与证据见 `verification/task52-verification.md`。
- **遗留：** 等主控唯一快审与提交；T-007／Spec未封，未做实机／部署／外部issue／Git写入或清理。产品与正式测试冻结，执行者 STOP。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**结论：本 Task 无阻塞发现。** 独立审查合同 r1／当前 Result 与最终21路径变更（基线 `530c2a5f`，`task/refactor-session-20261021`；`task52-final-owned-manifest-r2.json`），采用 reviewing-work 的 Task Unit／Code 判据，受 process-reset、Spec B8／Plan P10 与当前 AGENTS 约束；本轮无挑战、无产品修改、无 dotnet 重跑。

- **实现：** 对照 HEAD 原三路，确认实际物品优先、唯一被选 Main、原 request recipient／physical／minimum 与动态 footprint；普通动作 RequireRollback 及 native Commit 结果映射没有扩展核心恢复权限。三个 fine 与两个旧编排删除，唯一 whole 的注册只保原 replay 出口／Self None。疲劳另用 ordinary ReadOnly／空 participants／None，静态 Reader 保原算法及共享消费者；完整投影次序、券两处独立辅助 catch 与初次 Project Failed 的忽略语义保持。
- **VC-B32：** actual DI Dispose、独立使用量读取与 production outbox／encoder／受控 physical sender 的旧新用例支持所列默认 typed、精确 Fence／Block／DiscardExact 和关闭差异；旧 scope 实际发送拒绝 ACK，不能由空的 ShopSender.Messages 推断无帧。同 entry／Ordinal、资产一次及替换 physical 隔离由成功／回滚重试／raw replay 专例验证；未把 scope 用例没有的 entry 引用断言算作已测。
- **证据：** 独立解析 `task52-old-final-r1` 与 `task52-final-r1` 原始 TRX／run：旧 App 85通过＋1跳过、Protocol73通过；30新增路径旧新均通过。新 build 0警告／0错误，三个 full 严格串行，App5690通过＋2原失败＋16跳过、Protocol1787通过、Networking203通过。逐结果库存相对51为 App新增30／删0、Protocol新增2／删1、Net零变化，两项 Dimension 的失败身份与 Message 完全相同。DFO原输出70＝67反例＋3control，额外／exe／orphan为0；runtime登记、strict和八坏例记录支持闭合。另逐文件核当前16个产品／测试路径与旧源复验前 CURRENT 备份一致。
- **范围：** 迁移账只有66052及其三fine四行变化，original与其他行保留；Spec只增 VC-B32，AGENTS只增清单一行。批准仅覆盖本 Task；三个 fresh 项目加五个历史结果不等于八项目 fresh，全量并非全绿，不代表 T-007／Spec封存或 A21实机验收。提交及后续推进交主控。

## Controller Consumption R1

主控接受唯一 fresh Astra 的 approved／无阻塞发现，TASK-52 本地完成。已实际读三分支 whole 与旧 HEAD 对照，亲自执行有完整备份及 finally 恢复的旧源复验，独立核21路径恢复、16产品／测试路径冻结一致；旧 App85通过＋1原skip、Protocol73通过。新构建0警告0错误、三项目全量严格串行；逐display／outcome／重数及两项原Dimension失败Message独立复算一致，DFO按原expected解析70项且无额外记录。9498是三fresh加五历史的有效合计，不冒八fresh。

四行迁移账唯一变更、全部original／顺序／其余行不变、Result后strict及八坏例、含新文件的diff-check与纯准备可独立暂存均通过。按用户流程分开提交四文件纯测试准备与其余迁移实现／结构判据／人写记录；原始输出只留本机。继续T-007下一有用单元，不封簇或Spec，不执行外部收尾。
