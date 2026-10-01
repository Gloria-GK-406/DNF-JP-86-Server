> 2026-09-30实机语义更正：此前将 `EnchantUpgradeCount` 当普通强化等级的解释错误。普通强化为 `Attribute & 0x1F`（`ReinforcementLevel`）；`EnchantUpgradeCount` 是独立附魔升级次数。以下早先实现/测试记录保留其当时范围，不证明客户端普通强化等级正确。TASK-004纠正读写和wire/存储oracle；升级珠子效果仍不在当前支持范围，拒绝行为保持。

# TASK-002 — 规则服务与实例调用迁移
## Task Unit Contract
- Revision: 1
- Outcome and references: 全部现有养成/修理/耐久使用新装备实例，规则与状态操作分离；T-002、RQ-002至004、AC-002至004。
- Owned boundary: Abstractions装备规则契约/Data决定模型、Application Inventory装备规则与业务调用、ApplicationModule规则注册、相关现有测试适配及ItemIdentityArchitectureTests；删除旧临时EquipmentInstance，最终真正实例可改名EquipmentInstance。不要改Protocol wire/handler、Persistence基础身份实现、生产数据或任务控制器。
- Required checks: 现有全部装备养成/repair/durability/disjoint/swap回归，新实例测试与架构测试；构建；检索旧writer及领域直接内容依赖。
- Dependencies: TASK-001的EquipmentInstances.cs接口、真实store暂存UID投影和9项新实例行为证据；实际方法均已可读。
## Detailed Plan
1. 先读evidence/baseline-boundaries.md及现有各业务，按IEquipmentDomainService.Of(owner).FindAtAsync(slot)取得实例；ReadAsync读取core/当前slot；各Set返回最新facts。原slot与expectedTemplate仍用于请求准入，UID用于实际修改。
2. 强化抽取IEquipmentReinforcementRuleService：规则服务独占装备内容、升级行、费用公式、处罚、保护适用/优先级与销毁产物公式，接收只读core/保护facts/显式draw，返回不可变报价和决定。报价用于原资源准入，确定结果在原准入通过后才draw；可用prepared rule instance或Quote+TryReinforce，不让领域重复计算规则。自动保护的库存查询/期限校验留在领域，适用和选择优先级归规则。
3. 附魔新增规则服务接管珠子到卡片映射/升级珠子拒绝等静态规则；资源/过期/锁校验仍原语义。修理费用移出旧EquipmentInstance到修理规则服务，精确保留float32与零耐久0费不写、repair-all整体报价/拒绝语义。
4. 既有异次元/魔法封印/锻造规则保留；领域通过SetDimensionalAttribute/SetMagicSealOptions/SetForgingLevel等应用，不再core with+Put。强化通过SetReinforcementLevel或Destroy。修理/自动修理/耐久也通过实例；分解只适配facts和Destroy，工具/产物通用容器写入不属于装备writer。
5. 全部调用关闭后删除旧snapshot EquipmentInstance及Repair方法；无养成逻辑移入真正实例。修订ItemIdentityArchitectureTests：确认已接受宠物API的旧基线失败，允许仅世界实例API/facts的UID，保留通用库存/pile无必填身份及行键不外泄约束，不整体关闭检查。
6. 复用原行为回归，不为结构接口写镜像测试。需要针对真实缺口加测试时明确oracle；原业务公式/错误码/资产/时间/PVF兼容保持，不以新接口简化为改变玩法。
7. 设置PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf跑装备相关完整filter（含MagicSeal、AutoRepair、BusinessEquipmentSwap、ItemIdentityArchitecture及CommandHandlerArchitecture、EquipmentInstance）；记录通过/失败/skip。运行solution build与diff --check，附owned diff及实际规则API；不commit、不部署。


## Task Unit Result
- Decision: pass within TASK-002 boundary; 全量集成、独立结果复核、文档/部署与最后人工验收由后续任务负责。没有提交或部署，没有修改Protocol、Persistence基础身份实现、生产数据或控制器。
- 新增 `IEquipmentReinforcementRuleService.QuoteAsync(core)` 返回只读prepared `IEquipmentReinforcementRule`。规则拥有准入中的Seal/Amplify、PVF内容、耐久要求、+30/rarity上限、费用/材料报价、处罚、保护适用/优先级与销毁产物公式；领域保留会话、锁、材料/金币事实、保护库存/期限及产物空间检查。`TryReinforce(protection, draw)` 在原资源准入后调用，返回最终level、outcome、是否扣保护及销毁产物数量。未改变缺失level cost weight取1或失败阈值100000。
- 新增 `IEquipmentEnchantRuleService.CardAsync(beadCore, equipmentItemId)` 接管升级珠子拒绝与珠子到卡片内容映射；领域只检查世界事实与扣1珠子，再调用实例 `SetEnchantmentAsync`。
- 新增 `IEquipmentRepairRuleService.PrepareAsync()` 返回 `IEquipmentRepairRule.QuoteAsync(core)`，报价保留grade整数除法、float32逐步算术及最后截断，返回费用/目标耐久/是否写。`AutoRepairDurabilityAsync(core)` 保留无耐久跳过与非法耐久拒绝；自动修理的既定slot、broken、资格、每日额度与全有或全无结算未扩张。普通repair-all仍先完整报价再扣费，不可读装备不能导致部分收费。
- 强化、附魔、锻造、异次元、魔法封印、修理/自动修理、耐久全部经 `IEquipmentDomainService.Of(owner).FindAtAsync(slot)` 取得UID实例，按 `ReadAsync().Core` 判断并调用专用Set/Decrease/Destroy。receipt继续使用现有模型和协议投影。旧snapshot `EquipmentInstance.cs` 删除；真正的 `EquipmentEntityInstance` 改名 `EquipmentInstance`，没有养成公式移入实例。
- 分解以实例facts构造原 `EquipmentDisjointCandidate` 并用 `DestroyAsync` 移除装备。原通用容器写入仅保留分解工具和材料产物；该旁支原内容准入未扩张。本结果不声称已修复分解领域原有直接 `IEquipmentDisjointDefinitionSource` 依赖。
- `ItemIdentityArchitectureTests` 最小允许 `CreatureState`、`EquipmentInstanceFacts`、蛋/宠物/装备世界实例接口及其实现的UID。行网关白名单、通用ItemIdentity可空规则仍保持，新增负向判据确认generic `ContainerSlotItem`/`InventoryItemState`/`ItemPile`不能获世界身份豁免。基线宠物接口被旧判据错误拒绝的原因已关闭。

### Owned diff
- 新建：`Server/src/Dfo.Server.Abstractions/Inventory/EquipmentCultivationRules.cs`；`Server/src/Dfo.Server.Application/Inventory/EquipmentReinforcementRuleService.cs`、`EquipmentEnchantRuleService.cs`、`EquipmentRepairRuleService.cs`。
- 修改：`Server/src/Dfo.Server.Application/ApplicationModule.cs`（新增3项规则DI，保留TASK-001装备实例DI）、`Inventory/EquipmentInstances.cs`（真实实例改名）、8个 `Equipment{Reinforcement,Enchant,Forging,Outworld,MagicSeal,Repair,Durability,Disjoint}DomainService.cs`、`Server/tests/Dfo.Server.Application.Tests/Inventory/ItemIdentityArchitectureTests.cs`。
- 删除：旧 `Server/src/Dfo.Server.Application/Inventory/EquipmentInstance.cs`。
- TASK-001的Abstractions实例、ItemContainer/ItemDomainStore、SQLite staged UID和新实例测试改动仍在同工作树；它们不属于本Task新增改动。

### Checks
在工作树根设置真实PVF后执行以下命令：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet build Server/86JP.Server.slnx --no-restore -v quiet
dotnet test Server/tests/Dfo.Server.Application.Tests --no-restore --filter 'FullyQualifiedName~Equipment|FullyQualifiedName~MagicSeal|FullyQualifiedName~AutoRepair|FullyQualifiedName~BusinessEquipmentSwap|FullyQualifiedName~ItemIdentityArchitecture|FullyQualifiedName~CommandHandlerArchitecture' -v minimal
dotnet test Server/86JP.Server.slnx --no-restore --filter 'FullyQualifiedName~Equipment|FullyQualifiedName~MagicSeal|FullyQualifiedName~AutoRepair|FullyQualifiedName~BusinessEquipmentSwap|FullyQualifiedName~ItemIdentityArchitecture|FullyQualifiedName~CommandHandlerArchitecture' -v quiet
git diff --check
rg -n 'with|PutAsync|ClearAsync|DefinitionSource|ContentSource|EquipmentInstance.From' Server/src/Dfo.Server.Application/Inventory -g 'Equipment*DomainService.cs'
```

- solution build exit0，0 warning/0 error。
- Application装备filter exit0，237 passed、0 failed、0 skipped，包含9项新实例测试及身份/命令处理器架构判据。
- solution同filter exit0：Application237 + Infrastructure50 + Protocol23 + Persistence3 + Networking1 = 314 passed，0 failed、0 skipped；其它测试程序集未命中该filter。不把这条当成全量solution测试。
- `git diff --check`通过。反向writer检索中养成与修理/耐久无core-with+Put；强化残余core-with仅销毁材料空间模拟；分解Put/Clear仅工具/产物。旧 `EquipmentInstance.From/Repair/DisjointCandidate` 无生产调用。
- 现有业务、公式、协议回归直接复用；未为新接口shape或简单setter添加镜像测试。一次Python默认编码读中文测试文件失败已改为显式UTF-8，属于编辑工具错误而非测试或生产行为失败；没有据此修改规则。
