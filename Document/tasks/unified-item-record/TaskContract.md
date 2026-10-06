---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 容器通用物品记录统一

## Context

### Source Request

用户：先按你的想法进行修改，写一个任务契约后进行任务把

承接已确认设计：通用带类型 record 统一容器内部物品表示；装备既能作为 record 存取，也能提取为 instance 执行业务。统一读写，保留物品身份、完整状态和容器所有权，移除重复包装及靠内容猜身份的路径。

### Current Situation

基准为 task/warehouse-storage 的 32039260ded8d251c5052dfcec597213c561668a。现有容器读取 ContainerSlotItem，写入 InventoryItemCoreState；StoredItem 对读写字段另作解释，独立物品转移依赖来源位置和内容匹配。装备 instance 已通过容器更新，但各层物品表示不统一。

## Goal

所有物品容器以同一种完整记录读写物品，独立物品身份在移动和状态修改中稳定，堆叠数量与附加状态不丢失；业务 instance 是该模型上的行为包装，业务层不再拼接多种不对称物品形状。

## Scope

### In

- 建立通用 ItemRecord，统一模板、特性、可选身份、数量和完整状态的表达。
- 容器及领域存储读写、物品移动/拆合/交换、新建和装备/宠物相关消费路径迁移。
- 按明确身份结算物品变更，维护唯一位置、来源所有权和领域事务回滚。
- 清理被替代的物品包装和专用转移写入入口；必要的数据库、协议投影留在边界。
- 更新对应测试与必要的架构现状说明。

### Out

- 新增玩法、改变协议布局、重新解释未知字段、增加物品细分类或重构无关领域。
- 在线部署、操作运行数据库、启动真实客户端、推送/PR/合并/清理工作区。

## Constraints

- 沿用当前事务、分层和容器归属规则；账号共享特性不代替实际所有权。
- 保留原有字段及物品附加状态，独立物品 UID 不能因表示重构而变化，堆叠物不因内部存储行拥有业务 UID。
- 容量、固定槽、交易限制、锁定、期限、装备业务规则与现有可观察行为保持兼容。
- 仅在 E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage 修改；新任务文档置于 Document/tasks/unified-item-record。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 容器读取和保存同一种完整物品记录，旧的重复物品模型不再作为业务读写通路 | 接口和生产调用审计、往返测试 |
| SC-2 | 同属性的不同装备移动、交换、反复修改后各自保留 UID，不能复制身份或越过来源归属 | 真实 SQLite 与容器事务测试 |
| SC-3 | 材料数量守恒，时装/宠物附加状态和未知字段随物品保存，失败操作整体回滚 | 拆合、移动、附加状态往返、拒绝和回滚测试 |
| SC-4 | 装备等 instance 包装同一条记录且通过统一保存路径更新，无独立可变副本 | instance 与容器交叉读取/修改测试 |
| SC-5 | 准入、容量、归属和协议投影保持兼容，现有受影响功能回归通过 | 对应项目测试及保留/删除概念核对 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 用户原文：先按你的想法进行修改，写一个任务契约后进行任务把 | 按前述通用 record 设计建立契约并执行本地重构、验证、独立评审及一个验收结果提交 |

## Requires Confirmation

- 推送、PR、合并、部署或修改运行数据库需要另行授权。
