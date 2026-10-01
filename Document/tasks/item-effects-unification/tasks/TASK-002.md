# TASK-002 — 装备改造与共用分解效果结算

## Task Unit Contract

- Revision: 1
- Outcome and references: 装备强化／增幅、附魔、异次元、魔法封印、锻造、NPC／便携分解均以完整typed effects计划经共享执行器结算。Plan T-002，RQ-002—003/005/008，AC-002/004/007。
- Dependency: TASK-001共享DomainEffect、EffectExecutionContext、ApplyEffectsResult、typed注册与所有权校验；执行前父代理核对该单位结果。
- Owned boundary: 装备／资产效果契约、基础handlers及注册、只读完整库存／账户预检、六个装备领域服务和必要规则计划接缝、直接相关测试、coverage本分区与本Task结果。保留instance基础setter及协议receipt；不能迁移制作／开包／任务／经验业务，不动服务／运行DB／PVF，不提交。
- Required checks: 相关构建；装备五类与分解、身份及协议回归；保护／失败产物计划、连续同UID字段修改、扣除腾槽／多个产物竞争、账户先扣后返、后续故障整批回滚；扫描旧领域写入和最终receipt。

## Detailed Plan

1. 扩展显式基础effects：物理／账户材料扣除、金币扣除、装备强化等级／附魔／红字／封印／锻造设置、装备销毁、常规物品／账户产物发放。装备身份使用UID与所有者／预期模板，handler只调用基础增减／setter，不读取成功率、不选择保护、不抽奖；禁止ReinforceEffect、DisjointEffect等整业务包装。
2. 领域收集会话、装备、余额、保护候选和库存facts；规则构建完整列表及原receipt所需metadata。复用原报价、概率、失败规则、自动保护优先和随机数输入，不重新实现PVF。失败计划明确正常费用、条件消券、等级惩罚／销毁及全部返材料。
3. 抽出既有强化／分解standing模拟，按有序effects一次模拟同槽累计、销毁和消耗腾槽、物理产物竞争及账户余额先扣后加；保留wire数量限制。Grant Validate不能只看初始满包；预检不写状态，真实写入只在共享执行器。后续开包的时装／期限／宠物prepared grants可独立扩展，不能把全部VirtualResource当作账户晶体。
4. 六个领域服务只保留事实收集、规则计划、完整预检、一次ApplyAsync和结果投影；删除手工Consume／TrySpend／Set／Destroy／Add结算段。执行阶段失败异常逃出事务，不能吞异常正常返回造成部分提交。
5. 分解NPC与便携使用同一计划规则；机器最后一件清槽纳入模拟，以便奖励使用其合法释放位置。原模拟遗漏此释放位置属于本次显式化的容量修正，记录为行为修正并加反例回归。
6. receipt基于真实结果与最终状态：同槽只保留最终Core，奖励另外保留增量，MaterialRemaining在所有effects后读取。保留ACK、NOTI、账号零晶体过滤及现有提交后重放机制，不顺手新增固定等级券、升级宝珠或封印卷轴。
7. 使用既有回归守住结构重构；对真实缺口／新整数组行为增加有意义测试并记录RED/GREEN或采用既有证据原因。串行build/test，真实PVF使用PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf。收尾扫描六个服务的写入调用、规则与handler对应、UID字段保护、receipt最终态，更新覆盖与Changed/Why/Checks/Remaining。

## Planning Evidence

共享API已在TASK-001生产输出中可见：精确类型注册、全类型识别、逐条Validate与Apply、当前事务及Account/Character归属校验。单条Validate不能替代整数组容量模拟。原强化ValidateRewards包含累计产物／保护／账户余额；分解旧Plan未移除耗空机器槽。现有装备instance每次按UID读取当前Core并只改字段，保留其所有权、跨事务寿命和销毁保护。Opening后继需要更丰富prepared grants，不能以普通id/count发放替换其时装能力／期限／宠物facts。

## Implementation Result

### Changed / Why

- 六个装备领域服务已转为facts → EquipmentEffectPlanRuleService完整计划 → 只读预检 → 一次Apply → 实际receipt。强化／增幅的成功判定仍复用原规则，失败规则只在成功判定失败时执行，保护选择、概率、费用、失败材料均保持原输入与抽签顺序。没有Reinforce／Disjoint等opaque业务effect。
- 新增InventoryEffects、基础金币／物理与账号材料／装备字段及销毁handlers。基础装备setter仍以UID读取事务当前Core，保持多字段连续操作和其他字段，不以旧snapshot覆盖。常规GrantStackable只适用普通可堆叠物理奖励；账户材料显式Kind/ItemID，未将所有VirtualResource视为晶体。Grant底层Add仅处理技术placement，不隐含源消费、冷却或抽奖。
- InventoryEffectPreflightDomainService累计模拟整数组：消费与销毁腾槽、多个物理奖励竞争、账户先扣后返与上限、金币累计扣除、旧UID销毁后拒绝。执行器先完整识别所有handler类型，再做整数组预检与逐条Validate，预检失败抛出；领域先预检仅投影原业务拒绝。所有生产构造均要求预检，不提供绕过路线。
- IInventoryEffectPreviewHandler提供后继明确扩展口；IInventoryEffectPreviewState共享槽位、账号余额与GoldAsync/SetGold standing。不认识的新InventoryEffect拒绝，不静默忽略。后继prepared grants必须保留精确creation、时装、期限、宠物facts及其他虚拟奖励。
- receipt由真实ApplyChanges投影；最终槽位每槽只读一次，奖励另保留增量，强化失败奖励保留规则顺序，MaterialRemaining在整数组执行后读取。沿既有ACK／NOTI／重放逻辑，本单元未移动通知到effect。
- 明确行为修正：耗尽最后一个便携分解机时，其合法快捷格可被产物使用，旧Plan漏算此腾槽。共享消费guard会拒绝过期／锁定便携机器，避免原子结算使用无效来源；旧分解没有该判定。分解NoRoom→InventoryFull，UnsupportedItem→CannotDisjoint，InvalidTarget／InsufficientAssets→ItemUnavailable，沿既有0x04／0x13协议映射；不支持产物不再误报容量满。
- 测试故障替身原依赖内容读取次数；新双层预检改变读取次数后，改为检查前一奖励确实已暂存在事务再注入后续故障，仍验证整批回滚。ItemIdentity架构例外补EquipmentEffectTarget及TASK-001既有AddPetStomachEffect单体身份，不改宠物生产逻辑。

### Checks

全部串行执行；真实PVF环境变量为 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。自动验证属于CurrentImplementation，不构成实机ClientVerified。

| 检查 | 实际结果／证据 |
|---|---|
| 便携机器最后一件释放快捷格 | 实现前focused test RED：1失败，装备耐久仍45；实现后同测试GREEN并纳入最终相关组 |
| 分解前置／不支持拒绝码 | focused两测试实现映射前RED：2失败，错误回0x04；最终组GREEN。`artifacts/task002-checks/disjoint-rejection-red.log/.trx` |
| `dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet` | exit0，0警告／0错误；`artifacts/task002-checks/build.log` |
| Application相关组：Equipment、MagicSealHandlerTests、ApplyEffectsTests、UseStackableHandlerTests、CreatureRuntimeDomainServiceTests、ItemIdentityArchitectureTests | exit0，400通过／0失败／0跳过；`equipment-regression-final.log/.trx`。含结构既有回归、6个执行／安全回归、2个分解拒绝码回归及2个强化计划分支；该数字为filter组总数，非仅装备测试 |
| Protocol相关组：Reinforce、Outworld、Forging、Enchant、MagicSeal、Disjoint、UseStackable、AccountMaterial | exit0，38通过／0失败／0跳过；`equipment-protocol.log/.trx` |
| Networking：OutworldFrameIntegrationTests、MagicSealFrameIntegrationTests、ForgingFrameIntegrationTests | exit0，3通过／0失败／0跳过；`equipment-frames.log/.trx` |
| 六个装备服务实际写入扫描、`git diff --check` | 未发现六个领域中的Consume／TrySpendGold／Set／Destroy／Add直接结算；diff无空白错误。域外Repair／Durability明确保留 |

回归命令完整filters：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests --no-build --filter 'FullyQualifiedName~Equipment|FullyQualifiedName~MagicSealHandlerTests|FullyQualifiedName~ApplyEffectsTests|FullyQualifiedName~UseStackableHandlerTests|FullyQualifiedName~CreatureRuntimeDomainServiceTests|FullyQualifiedName~ItemIdentityArchitectureTests' --logger 'trx;LogFileName=equipment-regression-final.trx' --results-directory artifacts/task002-checks --verbosity quiet
dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-build --filter 'FullyQualifiedName~Reinforce|FullyQualifiedName~Outworld|FullyQualifiedName~Forging|FullyQualifiedName~Enchant|FullyQualifiedName~MagicSeal|FullyQualifiedName~Disjoint|FullyQualifiedName~UseStackable|FullyQualifiedName~AccountMaterial' --logger 'trx;LogFileName=equipment-protocol.trx' --results-directory artifacts/task002-checks --verbosity quiet
dotnet test Server/tests/Dfo.Server.Networking.Tests --no-build --filter 'FullyQualifiedName~OutworldFrameIntegrationTests|FullyQualifiedName~MagicSealFrameIntegrationTests|FullyQualifiedName~ForgingFrameIntegrationTests' --logger 'trx;LogFileName=equipment-frames.trx' --results-directory artifacts/task002-checks --verbosity quiet
rg -n 'ConsumeAsync|TrySpendGoldAsync|Set.*Async|DestroyAsync|PutAsync|ClearAsync|AddAsync' Server/src/Dfo.Server.Application/Inventory -g 'Equipment*DomainService.cs'
git diff --check
```

结构迁移由已有行为回归保护，未人为制造结构RED。真实SQLite晚提交故障测试覆盖金币、物理材料、账户材料、装备字段同库回滚；TASK-001饲料测试仍为SQLite pet／character与FakeMainInventory混合参与者，真实同库饲料验证留最终整合。

### Remaining

- TASK-002代码与文档已准备结果检查，未自行更新控制器接受状态；无本单元已知阻塞。
- TASK-003制作／开包、TASK-004任务与专用消耗品、TASK-005经验、TASK-006完整扫描与最终自动／人工验收尚未完成。新共享预检接口须由后继prepared grant接入，不能宣称开包已迁移。
- 未提交、部署、启动／停止服务，未修改运行数据库或PVF；固定等级券未实现。装备修理／战斗耐久仍保留原instance接口；实机最终验收待整体集成后进行。

控制器检查（2026-10-01）：已读实际计划规则、六服务调用与共享handlers／preview／receipt，扫描手工写入零命中并核对最终400／38／3回归与构建记录。本单位边界接受，TASK-002置done。

独立阶段遗漏检查：effects_stage_audit只读检查完整effects、顺序容量／余额、异常边界、UID字段保护、最终槽与奖励增量及随机／保护结果，未发现可复现生产遗漏。来源锁预检与执行的最新稳定源码一致；父代理此前疑点为旧快照，未列缺陷。检查未独立运行测试／SQLite注入／实机，不将静态结论当新增自动证据。下一步TASK-003，完整任务仍在实施。
