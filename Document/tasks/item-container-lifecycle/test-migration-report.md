# T004 第一批测试迁移交接

## 本批范围及结果

仅修改 tests 和本报告，未修改生产 src、未提交。

已迁移三处集中夹具：

- `Persistence.Tests/Support/InventorySide.cs`：移除已删除的 EntryLease 接口/Save 适配，接入 Store/Lifecycle；DailyRefill 注入 Lifecycle；InventoryEntryAssembly 新构造参数。仍保留每次调用的真实 DomainTransactionScope。
- `Application.Tests/Inventory/ItemUseFixtures.cs`：FakeMainInventory Seed 明确 Get/Take/Delete/Create/Put；Rows 在读取事务内 GetAll 后抽取值并关闭事务。
- `Application.Tests/Inventory/NpcShopPurchaseTests.cs`：ShopFixture SeedCore 和 Shared 设置数量明确删除旧条目后创建/接收新记录；Durability 在事务内读取 Snapshot。种子值适配保留原 Core 全字段，未改变 golden 字节。

其余已迁移：

- `Persistence.Tests/Support/VolatileItemDomainStore.cs`：同步类型与 legacy kind 校验、普通模板冻结、仅 Creature 允许业务模板变化、Equipment InstanceValue 可以修改、Avatar/Creature wire identity 冻结，以及零复活币仅允许角色 Main/1 的窄例。
- `Application.Tests/Inventory/EquipmentInstanceTests.cs`：改 Snapshot 读取和 Get/Take/Put/Update；移动后旧 instance 失效，即使同 UID 回到同槽也不复活；重新提取 instance 才继续修改；Create 已分配 UID、Put 后可以提取 instance；删除后相同 Core 新建得到不同 UID。该文件尚不能单独编译运行，其他 Application 测试声明错误仍阻断项目。
- `Application.Tests/Mail/MailboxProtocolIntegrationTests.cs`：TemporaryMailContainer 装饰器完整委托新能力接口。
- 六个只读/奖励结果 fake：Application `PaidCardFlipCostTests`、`QuestEnemyDropDomainServiceTests`、`SeekingQuestProgressDomainServiceTests`；Protocol `CardRewardTests`、`GroundPickupTests`、`AdvanceHuntEnemyQuestCommandTests`。Get/GetAll 返回 ItemReference/Snapshot，移除旧双槽读取；这些 fake 原定位只回答 Add 和读取，新的 Take/Put/Update 仍明确不支持，不声称覆盖真实生命周期。后续如业务确实进入这些能力，需要接入共享 Volatile/真实容器，不要添加旧兼容接口。
- `Infrastructure.Tests/Content/PvfMailItemDefinitionSourceTests.cs`：纯规则输入 Core 转 Snapshot，原断言不变。
- 新增 `ItemLifecycle.Tests/VolatileLifecycleGuardTests.cs` 四例，验证上面的内存后端护栏。

## 验证证据

- `dotnet test Server/tests/Dfo.Server.ItemLifecycle.Tests --no-restore -v quiet`：**42/42 通过**。日志 `artifacts/item-container-lifecycle/test-migration-lifecycle.log`。
- `dotnet build Server/86JP.Server.slnx --no-restore -v quiet`：**0 警告 / 114 错误，未通过**。最后日志 `artifacts/item-container-lifecycle/test-migration-handoff-build.log`。
- 父交接基线是 194 错误；本批消除了部分声明层错误，114 不代表总迁移量，因为剩余缺失类型会掩盖更深层调用错误。
- `git diff --check -- Server/tests` 通过。整体 diff check 仍报若干父已有 src 行尾空白/EOF 空行，本批未改这些生产文件。
- 修改时按各文件原 BOM/line endings 写回；没有改动协议预期字节。

## 剩余编译集中点

1. Protocol MoveItemSpace 四个 dictionary fake：`EitherEndMayBeHoldingItTests`、`TheAvatarBagIsServedTests`、`TheCharacterChangesAsYouDressItTests`、`RefusedEquipIsRecordedWithItsTemplateTests`。建议共享 Volatile-backed 容器，并让相关 DirectMoveBus/fixture 使用同一个事务范围；不要各 fake 发自己的 ItemRecord 或靠 UID 找回旧引用。
2. Application creature fixture：`TestCreatureCollection` 仍缺 `FindItemAsync/ReadItemAsync/WriteAsync`；`CreatureNamingDomainServiceTests` 和 `CreatureTransformationDomainServiceTests` 的业务容器还停在 ContainerSlotItem/Core 写入。
3. Application 剩余小声明：`SkillBuildResetTests`、`CharacterContainerDomainServiceTests`、`FatiguePotionTests`、`QuestClearCouponDomainServiceTests`、`CeraShopTestContent`、`NpcShopRuleTests`、`EquipmentReinforcementFailureRuleTests`、`CharacterEntryInventoryTests`。
4. Persistence 旧 store 行为测试与 EntryLease 测试：`SqliteItemDomainStoreTests`、`WorldRowSurvivesTheMoveTests`、Avatar/CreatureStateFollows、TwoAvatars、`ClassificationRefusalTests`、`SqliteEntryInventoryMutationRepositoryTests`。EntrySave 已无生产 API，必须改为显式生命周期操作，保留身份/细节/回滚断言；不可恢复旧 lease 兼容入口。
5. 声明层清理后预计暴露更多 rule/receipt 的 Core→ItemSnapshot 输入，以及原 InventoryStorageItem→ItemReference 的读取位置/UID/细节转换。

## 已知生产问题，仅记录未改测试绕过

父已确认待修：worn Put 准入、历史 account cargo kind8 无细节只读兼容、Avatar 正身份、零 counter 合并。尤其 `InventoryLoadCharacterizationTests.AnAvatarInTheCargoIsCarriedWithoutADetailRow` 应保留原兼容断言。Wire InventoryItemState.Core 仍是旧编码结构，而 equipment/opening/shop receipt Core 已是 ItemSnapshot；后续切勿全局把所有 `.Core` 都替换成 `.Snapshot`。

本批已收束，交还全部写入与构建权。


# T004 第二批：Protocol 测试迁移完成

## 范围与实现

本批仅写 `Server/tests/Dfo.Server.Protocol.Tests`、父代理明确同意的已链接共享 `Support/CharacterSide.cs` 构造接线，以及本报告；未修改生产代码或 golden 字节，未提交。

- 四个 MoveItemSpace 旧 dictionary 容器改为 `Support/MoveItemFixture.cs`：各场景的物理容器共享一个 VolatileItemDomainStore，每次命令通过真实 DomainTransactionScope 执行并提交。Get/Take/Put/Update 直接委托生命周期 session；初始化通过 Create/Put，结果读取抽取 Snapshot 后结束事务。保留故意错放物品的初始场景，由真实移动规则判断拒绝。
- Protocol 项目链接唯一 VolatileItemDomainStore 源码，没有复制实现。
- BuyItem、CompoundItem、DisjointItem、Creature receipt 输入改为 ItemSnapshot；保留原核心字段和所有协议字节断言。
- QuestClearVertical 种子改为 Create/Put，读取改为 Get/Snapshot。只需要 wire projection 的 ContainerInventoryProjection 通过 Snapshot 读取，不泄漏事务引用。
- CardReward 和 GroundPickup 的 Add/读取型 fake 补上实际路径需要的 UpdateAsync，并验证引用归属、容器、槽代际和身份不变。GroundPickup 夹具把 kind1 物品声明为非堆叠实例，保留 floor InstanceValue 的协议测试。它们仍只用于窄协议投影，不作为事务保管/回滚的证据。
- ItemPathFrame 和 ItemPathLive 将药水 SQL 种子从错误的 `kind=world` 改为 `kind=template`，保留 template ID、数量、原 item_core、真实移动及所有字节级断言；新 Put 准入正确识别其为堆叠物。
- CharacterSide 补齐 CharacterRosterService 的同库 store/lifecycle/transaction 依赖。

## 验证

- Protocol 编译：0 警告、0 错误。`artifacts/item-container-lifecycle/protocol-batch2-build.log`。
- 无 PVF 首轮完成：1182 通过 / 0 失败 / 59 跳过，不把此结果作为 PVF 场景通过证据。
- 设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，运行整个 Protocol 测试项目：**1246 通过 / 0 失败 / 0 跳过**。
- 其中四组迁移的 MoveItemSpace：EitherEndMayBeHoldingIt 22、TheAvatarBagIsServed 17、TheCharacterChangesAsYouDressIt 20、RefusedEquipIsRecordedWithItsTemplate 4，合计 **63/63 通过**。真实本地 TCP ItemPathLive 也通过。
- 最终命令：`dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore -v:q --logger "trx;LogFileName=protocol-batch2-pvf.trx" --results-directory artifacts/item-container-lifecycle`。
- 日志：`artifacts/item-container-lifecycle/protocol-batch2-pvf-test.log`；TRX：`artifacts/item-container-lifecycle/protocol-batch2-pvf.trx`。
- 本批路径 `git diff --check` 通过；文件行尾对齐仓库基线。未改生产 src 或任何既有字节期望。

## 交接边界

第一批列出的 Protocol 编译迁移集中点已完成；Application 与 Persistence 其余测试仍由父代理继续，未因本批通过声称解决方案全绿。

父代理本轮只读发现的生产待修仍保留：GroundPickup 应冻结 Creature 的 InstanceValue；CharacterContainerAddition 构造业务容器缺 lifecycle 注入。这些未被本批覆盖为生产成功证据，也未通过修改测试期望掩盖。

本批停止全部写入和构建，交还父代理。
