# 通用物品目录与用途准入

物品搜索、详情和装备配置能力属于通用物品服务。邮件、角色初始化、每日补给分别决定哪些物品可用于自身业务。

## 调用链

`HTTP → GM 领域服务（授权）→ IItemCatalogDomainService（用途筛选、分页）→ IItemCatalogRuleService → 内容源 → PVF`

- `IItemCatalogSource` / `PvfItemCatalogSource` 提供物品名称、ID、分类、装备类型、等级、稀有度及查询候选。
- 内容源保留两份物品索引中的元数据，不按邮件语义提前排除时装、宠物等模板；是否可选由用途规则决定。
- 目录规则服务读取物品声明和投递位置，构造通用详情；装备能力由 `IItemEquipmentRuleService` 提供。
- `IItemCatalogSelectionRuleService` 由各用途规则服务实现并注册。目录领域服务按用途选择规则，在分页前过滤；搜索与单项详情使用相同的准入规则。
- 无用途的内部目录查询提供通用元数据，不受邮件准入限制。用途来自领域服务，HTTP 客户端不能自行更改用途。

## 当前用途

| 用途 | 规则服务 | 准入范围 |
| --- | --- | --- |
| 邮件 | `IGmMailRuleService` | 支持的永久普通装备或堆叠物 |
| 角色初始化 | `IInitializationRuleService` | 永久普通背包物品；排除账号共有材料等特殊投递位置 |
| 每日补给 | `IDailyRefillConfigurationRuleService` | 永久普通背包堆叠物 |

永久普通物品的共同判定复用纯规则；各用途另外检查自身条件。查询不会构造临时邮件或准备发放。实际发送邮件仍单独检查数量、附件展开上限和装备配置；初始化仍检查初始背包容量；补给仍走自身的规则解析和发放流程。

## HTTP 与前端

现有 `/api/gm/v1/mail/items`、`/initialization/items`、`/daily-refill/items` 路径及 JSON 字段保持兼容。通用 DTO 位于 `Data.Inventory`，共享前端类型位于 `items.ts`，选择器为 `ItemPicker`。`mail.ts` 仅保留邮件数据和编辑逻辑。

PVF 目录缓存沿用现有实现：按 ID 查询按需读取；普通搜索首次加载目录后在内存筛选。当前没有运行中 PVF 热刷新机制。

## 回归验证

- 将邮件准入替换为全拒绝时，初始化、补给和无用途的目录查询仍能工作。
- 不支持的物品在分页前排除，搜索和详情准入一致；账号共有材料在邮件与普通背包用途之间保持不同结果。
- 边界测试限制通用目录、装备规则、初始化和补给规则对 Administration 的依赖，防止重新接入邮件契约。
