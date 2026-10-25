# TASK-55 — 教程奖励、耐久报告与契约使用入口归并

本 Task 承接 T-007，闭合 DecreaseDurability（65584）、TaggedIdReport（65679）、PremiumServiceUsageReport（66415）三消息及 DecreaseDurability／GrantTutorialReward／ConsumeDungeonBuff 三 fine，共六行。TASK-54 已获唯一 Astra 批准、主控接受，并以 dd0250a6／a805e9e3 分段本地提交，开工前工作区干净。目标未偏离：保持三个原普通写及 Premium slot4 的独立只读语义，保教程完成标记／实际到达奖励、耐久与每日契约用量，不把它们变成效果账本请求。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-54；承接 RQ-001／003～009／013～015、AC-001～005／008。
- Outcome: 三唯一 Protocol／Direct whole 与薄入口，原三 fine 清零。所有普通写和 Premium ReadOnly 均 [] participants／None footprint：三个旧命令都没有 ISelectedSession，不能见到 selected 就增保护。无 Main／效果 ledger／raw／Self；ReadPremiumSnapshot fine 仍供日切，暂不关闭。
- Nature/Gates: 保持行为迁移；默认 whole 生命周期的可见差异只以正式旧新实测追加 VC，唯一 fresh Astra 随结果审；不改 Spec／Plan、核心或许可。纯测试准备独立提交，不外部收尾。

## Detailed Plan

1. **旧行为与强例。** 先跑 App TutorialRewardPlacement／TutorialCompletion／AccountMaterialBag／ExitFlushHandler、EquipmentDurability、PremiumServiceUsageReport／DungeonBuffUsage／PremiumProjection 等实际相关强例及对应 Protocol codec。补注册 decoder／dispatcher、真实 SQLite／run participant、生产 outbox／encoder／physical 的旧新表征；保教程完成与奖励原子、满包邮件／真实物品实例、耐久槽12／30／拒绝槽／零耐久不再写、每日十次／同run重复／日切不让旧run重扣／过期权益，以及失败后counter＋run marker一起回滚的原断言。
2. **Tagged 奖励。** Attribution只用于日志，不增加鉴权早退；RewardFlag非0时在原事务外查 DeclaredFor，随后查session。存在session即执行一个 [] 普通写，即使declared为空；FlagIndex31原CompleteOnce成功才继续，无新增升级逻辑。完成标记→Main容器存在性→InventoryThenMail投递保持同事务、顺序及实际placed回值；原receipt到wire槽／shared材料转换与 checked cast 保持。ACK只报实际入库，随后可选共享材料NOTI，保原DateTimeOffset.UtcNow位置；未选角照原日志和空ACK，无Main／序号幂等。
3. **耐久。** 保 session／online 前置门，原固定槽换算只捕获 InventorySlotMappingException；有效槽才一个 [] Write 调原 DecreaseAsync。receipt走原TheseCharacters即时投影，Project Failed继续忽略；其他结果回CMD error17，不新增工作单元／保护或补发。相同帧重复仍依原耐久业务结算，不借效果账本去重。
4. **Premium。** 保 exact76B、u16(1)、slot3/4、尾零decoder。slot3先捕获runId再检查InWorld；一个 [] Write 内再检查InWorld、匹配run且未使用→TryUse→Stage，否则原拒绝／不消费；成功消费、run marker与SQLite在原同事务。回值仍 premium→usage→clock。slot4仍 [] ReadOnly，从原InWorld→premium→usage→clock顺序取数；与保留的 ReadPremiumSnapshotCommandHandler 共享实质读取算法，不能复制第二份或让whole嵌套派发fine。原 IsCurrent 后只发状态CMD，不重复施加客户端本地效果，日志不变。
5. **失败与结构。** 正式旧新覆盖真实action／native提交回滚、scope.Dispose、首个actual encoder和替换物理连接隔离；分别守无selected保护、Premium只读与三个写的默认差异、Durability Project Failed忽略。没有业务catch的不映拒绝。三fine类型／handler／生产引用／注册清零，ReadPremium与日切来源保持pending。原测试对旧fine的直接依赖适配真实新入口或原有领域能力，不造只为测试保留的旧业务转发层；纯准备与结构适配分开。
6. **冻结交付。** 最终旧源复验须完整CURRENT备份、锁HEAD／branch／owned／空index，全部runner结束后交主控全文读脚本并执行finally逐文件恢复。恢复后fresh非增量Release solution 0warning/error及App／Protocol／Networking full，原类并行／5m／detailed／独立DFO，按实际共享影响扩展。逐display／outcome／重数守原两Dimension失败与18有效skip，不新加豁免。仅六ledger行、AGENTS清单一行与实测VC；Result、runtime、strict／八坏例、含新增文件diff-check完成后STOP交唯一fresh Astra，主控接受后分段本地提交。

## Result

- Outcome: done（执行 round 1；最终验证 `task55-final-r2`，待唯一 fresh Astra 快审及主控接受）。
- **Changed:** 三消息各唯一 Protocol／Direct whole 与薄入口，删除 DecreaseDurability／GrantTutorialReward／ConsumeDungeonBuff 三 fine；三普通写与 Premium slot4 独立 ReadOnly 均 []／None，无 Main／效果 ledger／raw／Self 或新 selected 保护。教程保事务外声明、完成标记与实际到达奖励原子、ACK／共享材料顺序和等级不变；耐久保槽映射及 Project Failed 忽略；Premium 保捕获 run、InWorld 复查、counter＋run marker 同事务及 premium→usage→clock。ReadPremiumSnapshot 与日切保留并共用实质读取算法。仅六 ledger 行闭合，AGENTS 表增一行，Spec 仅 VC-B35。
- **Why:** 旧 direct fine 业务测试适配原领域能力／真实生产 GrantAsync 算法，handler 测试改合法 decoder／dispatcher；stale-run 的直接细命令断言转为正式报告捕获旧 run 后替换、零消耗、当前 run 未标记与下次消费的更强断言。纯准备 patch 单独保留。最终旧复验后只删除结果 DTO 的一个 EOF 空白行，经主控裁定无需重跑旧路线，保存精确 diff 后重新 fresh build＋三 full。
- **Checks:** 主控旧源码最终复验 App73／Protocol39 全 pass，并逐 bytes 恢复25 owned（含3删除）、8702非 owned。最终非增量 Release solution 0warning／0error；App5813＝5795pass＋2原 Dimension fail＋16skip，Protocol1790pass，Networking203pass；逐 display／outcome／重数新增25＋1，删0，失败 Name／Message 与 skip 不变。原 DFO parser70＝67negative＋3control，outside／exe／orphan0；实际冷 runtime 三唯一 binding／Direct Protocol／空 exits／Self None，三fine类型／handler／注册0；strict及八坏例均通过，含全部新增文件 whitespace 与完整最终备份。证据见 `verification/task55-verification.md`、`verification/logs/task55-final-r2/`、`verification/evidence/task55/`。
- **Remaining:** ReadPremiumSnapshot 与日切来源继续 pending；T-007／Spec002 未整体完成。仅三 fresh，其他五项目沿已接受历史库存；不称八 fresh。未做 A21 实机／专门性能测量、Git 写入、外部操作或 TASK-56。执行者 STOP，交主控唯一 fresh Astra。


## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**结论：approved；本 Task 范围未发现阻塞缺陷。** 唯一 fresh 独立审查，按 `reviewing-work` 的 Task Unit／Code 标准及 process-reset 一页快审；目标为合同 r1、执行 round 1、`task55-final-r2`／`task55-old-final-r2`，基线 `a805e9e3740889beb8281901f24056cda9c50be8`。审查前独立核对最终备份 `/private/tmp/task55-finalbackup-20261026`：27 owned／22 source 的存在性和 bytes、8701 non-owned、HEAD／status／空 index 一致；旧复验 CURRENT 与最终源码仅差已披露 DTO EOF 空白行。批准只覆盖这一冻结成果，不代表 T-007／Spec 封存。

- **源码与边界。** 逐段对照原 fine／handler 与三个 whole：教程归属只记日志，声明仍在事务外，有 session 即空参与者 Write；完成标记、Main 容器检查和 InventoryThenMail 同事务，ACK 只用实际到达，checked 槽换算／共享材料顺序及等级保持。耐久原槽映射、CMD17、TheseCharacters 和 Project Failed 忽略保持。Premium 先捕获 run，再查 InWorld，写内复查并同事务记 counter／run marker；slot4 为独立 ReadOnly，与保留日切 fine 共用实质 PremiumSnapshotReader。未增 Main／raw／Self／selected 保护或核心许可。
- **正式表征。** 阅读 `ReportProtocolCommandFlowTests.cs` 全部25例及原强例适配：真实 decoder／dispatcher、SQLite／run participant、DI Dispose、registered encoder／生产 outbox／physical 断言覆盖提交、action 与 native Commit 分别回滚、原 cause 与精确连接隔离。旧 stale-run 细命令断言由真实报告捕获旧 run 后替换、当前 run 不消费且下次才消费的正式用例接替；原领域／GrantAsync 辅助未被冒称完整入口。VC-B35 的写 scope／首 encoder typed 差异、slot4 raw 及帧时点、耐久失败忽略均有旧新实证，接受该具名生命周期差异。
- **原始验证。** 直接解析真实 TRX／run.json，逐 display／outcome／重数比 TASK54：App 5795 pass＋2 原 Dimension fail＋16 skip，Protocol1790 pass，Networking203 pass；新增25／1／0、删除0，失败 Name＋Message 与 skip 不变。旧 App73／Protocol39 全通过且纳入新库存；三 full 严格串行，最终 fresh build 日志为0警告／0错误。以原 `violations.py` 独立复查70条（67反例＋3控制），outside／exe／orphan 均0。读取 runtime-r2 实际JSON及探针源码，加载 DLL 与最终构建输出逐 bytes 相同；冷探针只证明登记数据，真实 provider／invoker 由正式注册及 flow 测试承担。
- **范围与记录。** 独立比较 ledger 确认仅三消息／三 fine 六行改变，original／非目标／顺序不变；重新运行 strict 与八坏例全部通过。pure-preparation.patch 仅新增正式 flow 文件；全部 owned 含新文件 whitespace clean。ReadPremiumSnapshot 及日切来源继续 pending；有效库存其余五项目为历史证据，未冒称八 fresh。未做 A21 实机或专门性能验收。

证据：[`task55-verification.md`](../verification/task55-verification.md)、`verification/logs/task55-final-r2/` 与 `task55-old-final-r2/` 原始日志／TRX、`verification/evidence/task55/runtime-r2/`、`eof-format-only.patch`；上述关键源码和原断言均由本审查直接读取。仅追加本段；未运行 dotnet、未修改产品／测试、未做 Git 写入或旧源 swap。**STOP，交主控消费。**

## Controller Consumption R1

主控接受唯一 fresh Astra 的 approved，已实际读取审查段及其指向的源码与正式证据；本 Task 完成，允许本地分段检查点，继续 TASK-56，不代表 T-007／B 封存。主控独立核最终 r2 的原 argv／自然结束／串行顺序、逐 display／outcome／重数：App＋25、Protocol＋1、Net零差且无删除，两条失败的 Name／Message 和 skip 不变；原 DFO parser、strict／八坏例均通过，三 runtime binding 及加载 DLL 与最终构建对应。最终27 owned 恢复备份与 HEAD／status／空 index 已逐项核对；旧复验后仅 DTO 末尾空白行差异经精确 diff 核实。迁移账仅六目标行，original／非目标／顺序不变；产品／测试冻结，含新文件 whitespace clean。

接受 VC-B35 的具名默认生命周期差异，维持空参与者、原只读与投影 Failed 边界。纯测试准备独立提交，其后提交 whole／原测试适配与人写记录；原始日志、TRX、恢复副本继续只留本机。未授权或执行外部收尾，ReadPremiumSnapshot 与日切等后继范围保持 pending。
