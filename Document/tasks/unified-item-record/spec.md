---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 容器以通用物品记录读写

## Why and Outcome

普通容器操作消费完整 ItemRecord；装备等 instance 在同一记录之上提供行为。物品位置在容器外层，不属于物品本身。业务层不再丢掉身份/附加状态后依靠存储猜回。

## Scope

覆盖任务契约全部范围。保留协议 DTO、数据库 Row、既有 Core 编码与未知字段；不要求为了类型命名改数据库布局。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 单槽读取返回 ItemRecord，保存接受 ItemRecord；批量读取仅以槽位包装同一记录，无另一份物品状态。模板、只读 Traits、数量、可选 UID 和完整状态可从记录取得。位置和所有权不写进物品记录。 |
| RQ-002 | 已有独立物品写入以明确 UID 保留身份，未知/重复 UID 或未释放原位置的复制被拒；跨所有者移动保持既有角色与账号归属范围。新建有独立身份，堆叠物无业务 UID。 |
| RQ-003 | 同模板不同实例、同 Core 不同实例、跨槽/跨容器多跳及原位修改保持各自身份。材料拆合数量守恒且不合并不兼容状态。 |
| RQ-004 | 时装和宠物附加状态与全部 Core 字段往返保存；新建输入完整，不再把读写附加状态解释成不同含义。事务内读自己的写，回滚恢复物品与位置。 |
| RQ-005 | 装备 instance 可由记录验证并提取，继续事务绑定与按 UID 定位，通过统一记录保存；宠物已有行为路径与新记录相容。 |
| RQ-006 | 删除 StoredItem、ContainerSlotItem、ItemTransferSource 及旧 Put/PutTransferred/PutNew 生产写入通路，InventoryStorageItem 仅表达位置加记录，InventorySlotContent 不再作为重复模型。 |
| RQ-007 | 准入、容量、固定槽、期限、锁定、交易、实际所有权和协议投影保持兼容；现有数据库无需部署迁移。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-006 | 接口和所有生产写入方审计表明读写使用相同记录；旧模型和专用转移写入入口无生产定义/引用。 |
| AC-002 | RQ-002, RQ-003 | SQLite 测试覆盖相同 Core 的两件装备换位、跨容器多跳、状态改变后继续移动；UID 各自稳定且复制/伪造来源被拒。 |
| AC-003 | RQ-003, RQ-007 | 材料拆合、不同状态拒合并、个人/账号仓库移动保持数量及归属；容量和准入回归通过。 |
| AC-004 | RQ-004 | 时装/宠物非默认细节及未知字段在读取、移动、修改、提交重读后保留；异常回滚不丢物不复制。 |
| AC-005 | RQ-005 | 从装备记录提取 instance 修改后，容器读到相同 UID 的最新数据，跨事务句柄仍被拒；现有宠物行为回归通过。 |
| AC-006 | RQ-007 | 受影响 Application/Persistence/Protocol 及相关 Infra/集成回归通过；协议 golden 不因本重构改变。 |

## Success Evidence

新增行为测试与现有真实 SQLite/领域事务/协议投影测试共同验证；完成证据逐项链接 AC 和日志。编译器用于定位调用迁移遗漏，不以编译通过代替行为验证。
