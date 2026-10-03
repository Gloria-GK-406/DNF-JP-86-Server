---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 通用物品记录

## Planning Choice

一个完整详细计划足以指导相互依赖的模型、存储和调用迁移，无独立 Task 单元。任何委派写入按顺序执行，主代理负责集成与最终验证。

## Approach and Shared Decisions

- ItemRecord 是不含位置的完整不可变记录：Core（现有完整兼容状态）、WorldClass（经模板规则验证的静态分类）、Identity?、AvatarDetail?、CreatureDetail?；ItemId/Traits/Count/IsPile 为只读投影。Count 从 Core 唯一推导，不增添第二个数量字段；保留 Core.Value 的编码兼容但业务数量修改使用 WithCount。WorldClass 保留为分类元数据，不能由 UID 是否已分配猜新建物品类型。
- ItemSlot 只保存 SlotIndex + ItemRecord，作为批量读取和通用存储会话的定位包装。单槽容器读取直接返回 ItemRecord；SaveAsync(slot, record) 为唯一保存入口。CreateRecordAsync 负责由模板分类和新建状态构造记录，不替代 Save。
- SQLite 读回真实 slot.kind、UID 和全部附加状态；独立记录的 UID 来自 world 行，堆叠记录不暴露内部 core_uid。结算显式匹配 UID，删除来源/内容猜测。检查 UID 唯一最终位置、真实原位置被本批次释放、模板身份相容及角色/账号所有权合法关联。
- 新建独立记录在落库时获得 UID；容器在需要 instance 或投影之前的现有 Flush 边界得到该 UID。新建记录并非已有实例，不能把空 UID 当作堆叠物。
- 不新建通用属性字典，不改变 Core 编码；迁移后按 record with/WithCore/WithCount 更新状态并保留身份及细节。创建细节并入记录工厂参数，删除 InventoryItemCreationFacts 包装。
- 宠物完整状态写入统一进入 ItemFacet：WriteAsync/StageAsync 按 UID 读取最新记录后修改 detail，孵化/进化先保存模板 Core 再 Stage；仅保留复活条件更新兼容入口，其读屏障只在有条件待决时生效。在线小数饥饿值保存对应的整数基准与重置时间，完整记录修改整数后重新计时，其他细节修改保留小数。
- 保留实例接口与真实规则；装备增加 From(record) 的包装入口，其后读取和修改按 UID 重新定位，避免旧快照覆盖当前数据。

### 实施中确认的接口边界

- 宠物原有完整状态缓存会覆盖后续 record 保存，因此改为异步更新 ItemFacet 的最新记录；孵化/进化先保存模板 Core，再更新细节。受限条件复活只在存在待决条件时建立读取屏障，普通移动中途读取不提前结算布局。
- 登录表读取补全现有 Creature 列，InventoryStorageItem 包装同一 ItemRecord。历史账号仓库中无时装细节的行继续允许只读投影，新入库仍受完整状态及准入检查约束。
- 存储夹具中按堆叠模板测试的物品改为真实 template 位置；手写简化 schema 补齐已有宠物列；加载 golden 只展开储存属性，并将原值路径移到 Item 下。协议 golden 字节不更新。

## Work and Dependencies

### T-001 — 模型与身份存储（RQ-001—004）

新增 ItemRecord/ItemSlot 和行为测试；变更 IItemDomainStoreSession 的定位值类型。SQLite 读回完整状态并以 UID 结算；先验证相同 Core 交换、重复位置拒绝、外部 UID 拒绝、附加状态和回滚。保留所有者检查和现有交易业务规则。

### T-002 — 容器及生产消费者迁移（依赖 T-001；RQ-001、003—007）

ItemContainers 和 CharacterBusinessInventoryDomainService 改为单槽 record、批量 ItemSlot、统一 Save；新建由 CreateRecordAsync 或持有已验证模板的纯规则计划构造完整记录。逐个迁移移动/整理/消耗/发放/效果预演/装备/宠物/协议命令中的写入，更新保留 identity/details 的 with 表达式。InventoryStorageItem 改为位置包装记录；删除旧模型及专用入口，保留投影 DTO。

### T-003 — 回归与说明（依赖 T-002；全部 RQ）

迁移测试夹具，使存储测试显式携带已读 UID；不把旧的身份猜测预期保留为新 API 契约。补充从 record 提取装备与交叉读写测试，检查新建/更新/跨容器重复写等路径。更新架构现状，不扩写无关历史文档。

## Verification

顺序执行共享输出的 .NET 构建/测试。先运行新增模型/存储目标测试，再运行 Application、Persistence、Protocol 全项目，真实 PVF 下 Infrastructure；按实际受影响接线决定 Host/Networking 冒烟。独立结果评审后运行所需新鲜验证，零未解决阻断项才进行一次本地结果提交。原始日志保存在 artifacts/unified-item-record，完成证据记录命令、结果及条件覆盖。
