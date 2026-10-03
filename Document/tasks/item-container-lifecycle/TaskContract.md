---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 物品取出记录与容器内引用

## Context

### Source Request

用户：先将代码回退到执行这个任务契前的状态，然后启动一个新的任务契约开始这次重构吧。

承接用户明确设计：record 是从 container Take 出来、原容器已不再持有、可交给其他 container Put 的完整物品；Get 返回容器内物品引用，包含定位信息，用于属性修改和 Update。换装用双方 Take 后 Put；强化成功用 Get／修改／Update，需要销毁时 Take 后交给第三方 DeleteItem(record)。物品类型是 record 的字段，消除重复物品表达。

### Current Situation

上一任务 unified-item-record 未提交，已按用户要求恢复代码至 32039260ded8d251c5052dfcec597213c561668a。旧尝试文档和补丁留作历史，不作为新设计。当前工作区为 E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage，分支 task/warehouse-storage。

## Goal

建立一套清晰的物品生命周期：容器外由完整 record 承载物品；容器内由绑定位置的引用访问和修改同一物品；取出、放入、更新和销毁具有不同语义，装备与宠物行为使用这套机制，消除多种重复物品模型和转换通路。

## Scope

### In

- 回退上一轮未接受的代码；从既有已接受基线开始重构。
- ItemRecord 统一承载模板、数量、类型和实例属性，包括必要身份及未知兼容字段。
- 容器 Get／Take／Put／Update、容器内引用和独立销毁入口，覆盖装备、时装、宠物和材料。
- 换装、仓库转移、整理、拆合、消耗、创建发放及装备／宠物属性修改等现有调用迁移。
- 清理被替代的业务物品模型、写入入口及重复分类；必要数据库和协议转换局限在边界。
- 回归测试、独立结果评审、必要的架构现状更新。

### Out

- 新增玩法、改变强化或交易等游戏规则、改变报文布局、解释未知协议字段。
- 部署、操作运行数据库、真实客户端操作、推送、PR、合并或删除工作区。

## Constraints

- 保持已有事务和项目分层；取出、接收及销毁必须能随同一用例回滚。
- 独立物品移动和修改保留 UID；堆叠数量守恒。属性、时装／宠物细节和未知字段不得因重构丢失。
- 引用只能更新它所代表的当前容器内物品，不得在取出、换位或跨事务后误改另一物品。
- 保持容量、固定槽、准入、实际所有权、锁定、期限及现有业务限制。
- 所有修改仅在上述工作区，新文档置于 Document/tasks/item-container-lifecycle；旧尝试证据保留。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 代码从上一任务前的已接受基线开始；新实现遵循用户的取出 record／容器内引用区分 | 回退证据、接口和调用审计 |
| SC-2 | Take 后来源为空，record 可被接收一次并保持完整状态；拒收及异常不会丢物或复制 | 真实事务移动、交换、拒绝及回滚测试 |
| SC-3 | Get 不移除物品，属性修改经引用及统一 Update 生效；失效引用不会误改替代物品 | 装备／宠物交叉更新、移动失效和事务生命周期测试 |
| SC-4 | Take 不等于销毁；DeleteItem 明确结束独立物品生命周期，消耗材料保持数量语义 | UID／数据库行生命周期、消耗、重复接收／销毁及回滚测试 |
| SC-5 | 物品属性与类型具有统一表达，旧重复模型和专用转移／新建写入通路退出业务层 | 类型及生产调用审计、完整状态往返测试 |
| SC-6 | 现有准入、归属和协议行为兼容，受影响功能回归通过 | Application、Persistence、Protocol、真实 PVF 和必要集成回归 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 用户：先将代码回退到执行这个任务契前的状态，然后启动一个新的任务契约开始这次重构吧。 | 精确回退上一轮未提交代码，建立并执行本契约范围的本地重构、验证、独立评审和一次验收结果提交 |

## Requires Confirmation

- 推送、PR、合并、部署和运行数据库操作需要另行授权。
