---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 取出物品与容器内引用的生命周期

## Why and Outcome

ItemRecord 表达调用方持有、可交给容器接收或销毁的完整物品；Get 返回容器内引用，不能直接当作可 Put 的物品。物品字段只定义一次，容器引用和行为接口使用该数据，不另维护 Core、世界物品、读取物品等多套业务形状。

## Scope

覆盖 TaskContract 全部范围。既有协议 DTO 和数据库列在边界转换；游戏规则不变。旧方案回退结果见 ../unified-item-record/spec-result.md。

## Lifecycle

| 起始状态 | 操作 | 结果 |
|---|---|---|
| 槽位占用 | Get | 返回当前位置引用，物品仍在槽位 |
| 槽位占用 | Update(引用,属性变更) | 保留物品身份和位置，更新状态；旧的已取出／替换／跨事务引用拒绝 |
| 槽位占用 | Take | 槽位为空，返回脱离容器的完整 record；物品未被销毁 |
| 新建 | 创建 | 返回初始化完整、可接收的 record |
| 脱离容器 | Put | record 被接收，位置占用；不能重复 Put／Delete 同一个 record |
| 脱离容器 | DeleteItem | 结束物品生命周期；独立行删除，堆叠物按数量销毁 |
| 任意待决变更 | 回滚 | 恢复原物品、位置、属性和数量；旧事务引用／record 不可继续写入 |

材料部分取出、拆合及消费应保持数量守恒；操作可以采用专用数量入口，但不得绕过 record 接收／销毁和引用更新语义。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | record 无容器位置，携带模板、数量、类型、必要身份和全部实例状态；Get 返回有位置归属的引用，普通读取不能制造可接收 record。 |
| RQ-002 | Take 立即腾空来源且保留独立物品 UID／状态；Put 接收 detached record，一次 record 只能被一个合法目的地接收或销毁。创建也生成同一 record。 |
| RQ-003 | 引用用于当前容器占用，Update 不接受脱离物品，不隐式移位或新建；Take 后即使同 UID 放回原槽，旧引用也失效；只读快照不能冒充可写引用。 |
| RQ-004 | 显式 DeleteItem 结束已取出物品；事务不允许无意遗留未安置的取出物品。异常整体回滚，移动中间读取／Flush 不提前销毁物品。 |
| RQ-005 | 装備区交换以 Take／Put 完成；强化／附魔等成功修改经 Get／引用／Update，需要销毁时 Take／Delete。装备和宠物行为接口不保存另一份可变真源。 |
| RQ-006 | 减少重复业务模型：StoredItem、ContainerSlotItem、InventorySlotContent、InventoryItemCreationFacts、ItemTransferSource 及 PutTransferred／PutNew 退出生产通路；InventoryStorageItem 若无必要直接消除，必要时只做位置投影。Core 编码形状仅留在兼容边界，普通业务使用统一物品属性。 |
| RQ-007 | 已有物品类型／特性统一作为物品数据字段，身份存储判断不再要求调用者额外提供 ItemWorldClass；PVF 权威分类仍须校验。材料数量、装备及宠物时装细节、未知字节均保存。 |
| RQ-008 | 全部现有相关消费者迁移，容量／固定槽／准入／归属／锁定／期限及协议 golden 不改变。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-006, RQ-007 | 接口与生产调用审计证明 Get／Take 的语义分离；物品属性具有同一表达，删除的业务模型和旧写入入口无生产引用；兼容 Core 不再作为普通业务读写入口。 |
| AC-002 | RQ-002, RQ-004 | 真实 SQLite 下 Take 后来源为空而物品行保留；跨容器 Put 保留 UID／细节；同模板同属性两物品交换不串 UID，重复接收／伪造／错误所有者被拒。 |
| AC-003 | RQ-003, RQ-005 | Get 不改变位置；引用修改后当前容器读取可见；移出再放回、换入另一物品、跨事务均使旧引用失效；装备实例和宠物状态交替更新无覆盖。 |
| AC-004 | RQ-004, RQ-005 | 取出与销毁可分别观测；显式删除移除独立物品，未安置取出物品不允许静默提交；拒收／异常／回滚不丢失或复制。 |
| AC-005 | RQ-002, RQ-007, RQ-008 | 材料拆合及消耗数量正确，不兼容状态不合并；时装／宠物全部细节和未知字段往返；准入、容量、账号归属保持。 |
| AC-006 | RQ-005, RQ-008 | 换装、仓储、强化、发放、宠物与消耗回归通过；Application／Persistence／Protocol、真实 PVF Infrastructure 及必要集成测试通过，协议 golden 未改写。 |

## Success Evidence

接口及类型审计说明结构结果；真实事务行为测试说明生命周期；既有回归和 golden 保证兼容。数据库与实机证据分开，本任务不声称 ClientVerified。
