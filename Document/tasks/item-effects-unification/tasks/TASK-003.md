# TASK-003 — 制作与全部现有开包奖励计划

## Task Unit Contract

- Revision: 1
- Outcome and references: 设计图、礼包、时装包、魔盒单／批次、袖珍罐、售货机币开物品的源自耗、材料金币、产物、权益、幸运、配额和冷却都在完整effects中。Plan T-003，RQ-001—003/006/008，AC-002/005/007。
- Dependency: TASK-002实际资产／装备效果、IInventoryEffectPreflightDomainService及IInventoryEffectPreviewHandler／State共享累计standing。执行前控制器接受该依赖输出。
- Owned boundary: 上述业务与规则构建、prepared／虚拟资源／权益／幸运／双倍配额effects和基础handler、统一preview必要扩展、DI及直接回归；旧OpeningInventory结算／接口及后三个待迁移消费者的必要编译适配、本Task结果与coverage。不得实施疲劳／任务券／技能完整业务、任务奖励或经验，不动运行服务／DB／PVF，不提交。
- Required checks: 所有开包／制作／售货机及相关协议、权益配额回归；容量腾槽／多产物争位、期限能力身份、源不耗／冷却、满包／限额／后期存储故障全部回滚；扫描本分区写入及未迁移消费者。

## Detailed Plan

1. 领域明确读取source facts和源规则；内容／抽签规则一次决定所有产物，完整计划规则列出源、钥匙、各堆物理／账户材料消耗、金币、全部奖励和副作用。移除consumed[0]推断规则和图纸默认prepend自耗；自耗由对应规则显式决定，保留各类型既有数量行为。
2. 新增prepared grant effects表达InventoryItemCreationFacts／OpeningPreparedGrant必要字段：时装能力、期限、耐久／封印、宠物初始facts及独立装备身份。注册preview extension沿同一standing模拟，Apply只做已确定产物的基础创建／堆叠。不能用普通id/count Grant丢字段，也不能将所有VirtualResource当作账户晶体。
3. 共享preview补金币与character Main虚拟槽standing接缝，物理、账户、复活币／虚拟资源各按既有路由累计。源消费先腾槽，多个产物同一模拟；保留wire条目限制。Apply的Validate不以初始空槽否定前序消费腾槽，执行时再次核对当前目标；异常逃出事务。
4. 权益延长、SeriaLuck终值、双倍罐本轮与每日总次数明确typed effects。双倍罐读取只读原始facts（资格、period、原round/total），规则决定两项next，handler只按预期写入，不调用TryUseDoublePotAsync重新做业务判定。权益只执行明确增量／决定值，不能藏在GrantPrepared里。
5. 移除本分区所有手工Consume／Add／Put／Clear／Extend／TrySetLuck／TryUseQuota及旧Settle调用。保留技术flush和receipt只读投影。每槽只投影最终Core，产物增量单独保留，UID从实际写入读取；保持CMD先于绝对NOTI、零晶体过滤、既有提交后缓存和合法下一次请求。
6. VendingMachine使用持有币开物品，纳入相同规则计划，保留黑钻资格、抽取、满包、并发与重试语义；不扩商城购买。
7. 旧Settle的FatiguePotion／QuestClearCoupon／LearnSkill退款消费者尚待T-004，为保持编译可改为具名临时消费结算接缝：只支持grants=[]、明确源facts与消费／冷却列表，内部走共享执行器；不把它当作完整后继业务迁移，也不包装LegacyBusinessEffect。coverage逐个列未迁移的其余状态；后继单位删除该过渡接缝。
8. 串行构建／测试，PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。结构调整采用既有高价值回归；真实缺口新增行为测试并准确记录RED／GREEN。检查Source、完整规则计划、registered handler、底层写入和最终receipt覆盖；更新Changed/Why/Checks/Remaining与接口交接。

## Planning Evidence

Opening旧Settle在消费、premium、virtual、prepared物理创建、隐式冷却等分支写入；领域另写幸运、双倍两种计数器、金币和图纸账户材料。旧算法已处理扣除腾槽、期限／能力／宠物facts，宜沿统一preview扩展复用而非重写。后三个未迁移消费者都只消费、不授予，可用有界过渡接口保持构建，但最终不可保留业务旁路。售货机是币开物品而非商城，已通过入口census确认属于同分区。

## Implementation Result

### Changed / Why

- 制作、普通／自选礼包、时装包、魔盒单次／批次、袖珍罐、售货机都通过 OpeningEffectPlanRuleService 构建完整 effects。源消费由 IsConsumedOnUse 声明；制作所有物理堆／账户成本、钥匙、金币、产物、权益、幸运、双倍配额、冷却显式列出。内容和一次抽签沿原规则，handlers 不重抽、不重新执行业务规则。
- 五类新 typed effects：GrantPreparedItem、GrantOpeningVirtual、SetOpeningPremium、SetSeriaLuck、SetDoublePotUsage。prepared 保留能力、期限、耐久／封印、Avatar/Creature 创建 facts，实际 UID 沿 store 分配；虚拟奖励保留 character Main槽0—2、core ID/kind和展示ID，未误当账户晶体；宠物内部继续豁免。
- 五类 handler 同时注册 preview extension，沿共享 standing 累计物理容量、virtual数量、premium期限、luck与双倍原始两计数器。预检不写状态、不抽签；Validate 不按初始状态否定后序合法 expected 链；执行只应用决定值／当前CAS。原 round/total与 next 明确，整个计划使用同一 At，失败抛异常整批回滚。
- OpeningInventory 旧 Settle 和 Put/Clear/Extend／隐式冷却全部移除，只负责 Source、一次 Apply、技术flush及最终receipt只读投影。每个touched槽只读最终Core／UID，奖励增量另保留，重复权益只展示最后期限；沿既有 ACK→绝对NOTI、零晶体过滤与提交后重试缓存。
- 真实行为修正：旧 Settle 强制扣除合法不自耗礼包，现发奖励／启动冷却而源不消失。另旧Opening／共享guard在2040误拒 ExpireTime=int.MaxValue 永久源；Source、制作输入与共享preview/handler现一致保留永久哨兵。两项均记录实际RED→GREEN，不当作纯结构回归。
- 仅 FatiguePotion／QuestClearCoupon／LearnSkill退款暂留 SettleDeferredConsumptionAsync(session, sourceItemId, declaredConsumptions, at, ct)：明确源ID，只消费／冷却，不收grants，不包装业务。其 Refund／RecordUse、CompleteWithoutRewards／Report、技能／ledger写入明确待TASK-004；测试替身仅保持编译。

### Checks

全部dotnet串行；真实PVF为 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。证据仅CurrentImplementation，未实机。

| 检查 | 实际结果 |
|---|---|
| 可重复礼包反例 | 实现前RED：source count期望1实际0；后GREEN，最终专项含该测试 |
| 永久源2040反例 | 修正前RED：Opening.Source抛 Missing, expired or nonstackable source；后与可重复礼包合计2/2 GREEN，0跳过 |
| 首轮开包／制作／售货机及后三替身专项 | 108/108通过，0跳过 |
| 三条新共享执行回归＋计时失败项单独复跑 | 4/4通过：prepared满包即使caller commit仍零写入、同权益连续expected链、virtual累计溢出零写入；计时测试单独通过 |
| 新鲜solution build --no-restore --verbosity quiet | exit0，0警告／0错误 |
| 最终Application专项 | exit0，130通过／0失败／0跳过，含本分区／后三替身／ApplyEffects／EquipmentEffectExecution／第二轮全suite失败装备测试；`artifacts/task003-checks/opening-regression-final.trx` |
| Protocol开包／制作／premium usage／account material | exit0，42通过／0失败／0跳过；`artifacts/task003-checks/opening-protocol.trx` |
| Networking开包帧／制作注册 | exit0，5通过／0失败／0跳过；`artifacts/task003-checks/opening-frames.trx` |
| 全Application首轮 | exit1，2466通过／1失败／0跳过：`NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`，单独复跑通过；首轮工具输出保留，未存TRX |
| 全Application第二轮 | exit1，2469通过／1失败／0跳过：`EquipmentReinforcementDomainTests.Protected_attempt_plan_explicitly_lists_costs_and_only_failure_consumes_protection(succeeded: True)` 在ShopFixture构造的 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 抛 SQLitePCL.sqlite3 ObjectDisposedException，尚未执行业务；`artifacts/task003-checks/application-final.trx`。最终专项含两个理论案例且通过。多fixture ClearAllPools／并行collection可能竞态，未证明baseline，不能声称全suite通过 |
| 最终写入扫描／旧接口扫描／receipt／git diff --check | 本分区五个领域＋OpeningInventory无直接Consume/TrySpend/Put/Clear/Extend/TrySetLuck/TryUseQuota/旧Settle；五类handler具名基础写入；恰三条临时消费调用；diff无空白错误 |

最终回归命令：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter 'FullyQualifiedName~Protected_attempt_plan_explicitly_lists_costs_and_only_failure_consumes_protection|FullyQualifiedName~PackageOpeningTests|FullyQualifiedName~RecipeCraft|FullyQualifiedName~Lottery|FullyQualifiedName~Vending|FullyQualifiedName~OpeningEffectExecutionTests|FullyQualifiedName~FatiguePotionTests|FullyQualifiedName~QuestClearCouponDomainServiceTests|FullyQualifiedName~SkillBuildResetTests|FullyQualifiedName~ApplyEffectsTests|FullyQualifiedName~EquipmentEffectExecutionTests' --logger 'trx;LogFileName=opening-regression-final.trx' --results-directory artifacts/task003-checks --verbosity quiet
dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-build --filter 'FullyQualifiedName~PackageOpening|FullyQualifiedName~CompoundItem|FullyQualifiedName~PremiumServiceUsageReport|FullyQualifiedName~AccountMaterial' --logger 'trx;LogFileName=opening-protocol.trx' --results-directory artifacts/task003-checks --verbosity quiet
dotnet test Server/tests/Dfo.Server.Networking.Tests --no-build --filter 'FullyQualifiedName~PackageOpeningIntegrationTests|FullyQualifiedName~RecipeCraftRegistrationTests' --logger 'trx;LogFileName=opening-frames.trx' --results-directory artifacts/task003-checks --verbosity quiet
rg -n 'SettleAsync|ConsumeAsync|TrySpendGoldAsync|PutAsync|PutNewAsync|ClearAsync|ExtendAsync|TrySetSeriaLuckAsync|TryUseDoublePotAsync|RenewDoublePotAsync' Server/src/Dfo.Server.Application/Inventory/PackageOpeningDomainServices.cs Server/src/Dfo.Server.Application/Inventory/OpeningInventoryDomainService.cs Server/src/Dfo.Server.Application/Inventory/RecipeCraftDomainService.cs Server/src/Dfo.Server.Application/Inventory/LotteryOpeningDomainService.cs Server/src/Dfo.Server.Application/Inventory/VendingMachineDomainService.cs
rg -n 'SettleDeferredConsumptionAsync' Server/src
git diff --check
```

### Remaining / API handoff

- 本单位结果待控制器接受；停止writer，未提交／部署／服务／运行DB／PVF修改。TASK-004／005与最终整体自动／人工验收未完成；保留上述两次全suite失败。
- `IOpeningInventoryDomainService.ApplyAsync` 收完整列表，返回 `OpeningEffectsApplication(Inventory, Applied)`；后三消费者完整迁移后删除具名临时接缝。
- 共享state新增 VirtualSlot/SetVirtualSlot、PremiumExpiry/SetPremiumExpiry、SeriaLuck/SetSeriaLuck、DoublePotCounters/SetDoublePotCounters，沿既有GoldAsync/SetGold。后继需最终Main holdings／GoldAfter时应扩Standing只读snapshot输出，不复制prepared放置算法；本单元未增加输出契约。
- `DoublePotFactsAsync(..., at)` 提供raw nullable Round/Total、同period已用次数／上限／资格，`ApplyDoublePotAsync`只按两个raw expected和next条件写入；`SetExpiryAsync(type, expected, next)`只应用已决定期限。legacy已知权益类型兼容，BlackDiamond仍配置控制。

### Controller acceptance

控制器已核对实际代码、最终TRX counters（130／42／5均0失败0跳过）及暂留消费调用（恰3条，source显式传入，没有consumed[0]推断）。本单位既定输出接受，可作为TASK-004依赖；两个完整Application失败事实保留到最终整合，不声称整项或全suite已经通过。纯投影／技术flush与基础handlers写入边界符合父Plan，生产已停止。

独立只读代理opening_stage_audit核对T003相对HEAD后，未发现新引入的确定性副作用遗漏或prepared创建回归。补查确认父AC-007仍有HEAD既存缺口：Package／Avatar／MagicBox重复同帧可再次Roll和消费；Lottery只记最后一条，A→B→A可重执行。它们不作为本Task已证明重放安全，明确进入最终replay单位；Vending现有(Character,Sequence)缓存已保护A→B→A。独立检查未运行build／test，不以此取代上表证据。
