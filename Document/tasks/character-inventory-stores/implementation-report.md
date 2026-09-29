# 角色物品业务 store 实施报告

状态：T-002～T-004 的产品代码和对应文档已完成，主控最终全量验证通过；最终独立审查见 reviews/change-review.md，本地结果封存见 spec-result.md。未部署、未操作运行中的服务或生产数据库。

## 实施结果

| Plan | 完成内容 | 主要证据 |
|---|---|---|
| T-002 | `ICharacterBusinessInventoryDomainService` 按 Item、Avatar、Creature、Medal 归属提供业务容器。领域以 `BusinessSlot` 的局部索引读写，共享 `IItemDomainStore` 事务；旧 SQLite 位置由 `LegacyInventoryStorageCoordinates` 独立适配。勋章仅预留，技能快捷键未改。 | `ContainerAdditionTests.Two_business_containers_can_both_hold_local_slot_zero`；`Document/tasks/character-inventory-stores/boundary-design.md` |
| T-003 | 统一模板 placement 与发放目标；掉落、翻牌、任务、教程、商城、邮件、开包、NPC 商店和离线发放按同一归属选择容器。修正 `RangeFromMainSlots` 误把目标固定在 Main。移动、堆叠、交换、穿脱执行双向准入，并将完整实例的来源身份传至持久化适配；移除旧绝对索引 `IItemContainer.MoveToAsync` 路径。 | `ContainerAdditionTests`、`BusinessEquipmentSwapTests`、`RefusedEquipIsRecordedWithItsTemplateTests`、邮件与商店/开包回归 |
| T-004 | 入站协议 handler 将 wire 地址变为业务地址；Opening、Lottery、Vending、Mail、Durability、DailyRefill 等领域回执保持业务地址，在发送/入场投影处变为 wire 或旧存储地址。登录宠物查询和通知 marker 跟随候选穿戴槽。 | `OpeningDelivery`、`MailboxDelivery`、`InventorySlotProjectionDomainService`、`SelectCharacterEntryInventoryDomainService`；协议、入场和耐久测试 |

协议映射与持久化映射分开维护，数字相同也不互相委托。A21 实帧只支持部分已见普通装备位置；宠物穿戴 25、神器 26..28、Pet 装备首格 140 采用明确标注的 `UpstreamReference` 候选，**不声称 A21 ClientVerified**。证据和实机操作见 `evidence/slot-evidence.md` 与 `client-acceptance.md`。若客户端实帧推翻候选，应修改协议适配和证据，不应重写业务局部槽。

旧库穿戴宠物/神器兼容由 `LegacyWornItemNormalizer` 在入场前按模板、kind 和身份计算整批归位计划；冲突会整体拒绝，重复执行幂等，原本正确的普通装备物理 12..24 不平移。隔离 SQLite 测试核对 UID 和附加字段。历史上错误放到 Main 的宠物装备不自动归位，无法单靠位置安全判断其意图。生产库尚未运行该逻辑，部署前需按验收清单复核。

## 审查发现的回归保护

`reviews/core-pre-review.md` 的四项已按复审意见修复：穿戴交换在写前校验被换下物的返回槽；旧宠物归位后的入场查询改用业务槽；统一 Grant 创建宠物时补全 creature 事实、UID 与宠物列表记录，离线发放也使用同一构造；同模板非堆叠实例走交换而非堆叠合并。对应测试分别在 `RefusedEquipIsRecordedWithItsTemplateTests`、`CharacterEntryInventoryTests`/`CreatureGoesBackOnTests`、`CreatureKindAgreesWithTheArchiveTests`/`ContainerAdditionTests`、`BusinessEquipmentSwapTests`。

后续审查又发现两件 *core 完全相同而 UID 不同* 的装备会被旧存储按原位内容匹配，从而报移动成功但 UID 未交换。新增来源身份提示后，真实 SQLite 回归覆盖同后端交换、穿戴跨列表交换、同事务连续移动，以及移动后同事务普通属性更新；堆叠拆合与原位更新仍沿用已有身份语义。新增回归曾在修复前失败，修复后通过。开包对 Main 徽章区请求受控拒绝且不写入；邮件发送恢复对临时绑定的所有权拒绝；这些均有直接测试。

## 测试记录

测试使用 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 和隔离测试库；构建启用仓库的 warnings-as-errors。基线由主控运行：3828 通过、475 跳过（未设置真实 PVF），见 `verification/baseline-tests.log`。

- 首轮实现全量测试退出码 0：4236 通过、2 跳过，见 `verification/full-final.log`。之后根据独立审查继续补了同事务身份链等四个测试及邮件/Opening 防线，因此这份日志不是最终代码快照的全量结果。
- 最终代码快照的并行全量测试见 `verification/full-final-v2.log`：4240 通过、1 失败、2 跳过。唯一失败为未改动的 `NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one`，两个配置 50ms 的截止在并行全量下墙钟实测 4730ms，超过测试已有 2.5s 上限；通知源码和测试本轮无 diff。其余项目零失败。测试自身注释记录了并发全套下既有墙钟波动；未放宽断言或忽略失败。
- 同一失败项在独立单测中 1/1 通过，耗时 175ms，见 `verification/notification-focused.log`。主控随后以串行项目执行同一断言的完整解决方案：4241 通过、0 失败、2 跳过，退出码 0，见 `verification/controller-final.log`；命令为 `dotnet test Server/86JP.Server.slnx -m:1 -v quiet --nologo --logger "trx;LogFileName=inventory-controller-final.trx"`。真实 PVF 下两个互斥的“缺少档案”负向测试按预期跳过。

旧移动 API 的测试已改经业务槽与业务移动服务，保留原有实例/属性断言。`git diff --check` 通过；源码搜索没有 `IItemContainer.MoveToAsync` 生产调用，Application Inventory/Mail 中剩余 `ToGameSlot` 位于明确的投影/入场组装边界。没有为测试便利向业务层暴露需要手算的物理槽。代码交付的最终结论以 `reviews/change-review.md` 和 `verification/completion-evidence.md` 为准；实机验收仍未执行。

## 保留的边界与未决项

- `StoreBackedItemContainer`、`IItemDomainStore` 和 SQLite session 仍持旧 `owner/list/index`，仅作为共享事务/旧库物理适配；`LegacyInventoryStorageCoordinates` 是其唯一业务槽转换表。已有创建事实、实例 UID 和持久化 schema 保留。
- `InventorySlotMappingRuleService` 和 `InventorySlotProjectionDomainService` 分别用于协议入站映射和出站投影；入场旧快照组装中仍会把业务结果投影回存储/线级记录。业务服务不再以 `GameSlot` 回转后操作物品。
- 真实 A21 宠物/神器/勋章槽位仍待客户端抓帧或静态闭链；不能把候选编码测试提升为实机证据。未部署、未进入用户客户端，也未验证历史错放 Main 的人工修复方案。
