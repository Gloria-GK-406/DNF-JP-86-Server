---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 以物品特性子集表达容器准入

## Why and Outcome

2026-10-03，基准 c2d647e2。业务调用者读取物品特性和容器接受集合，不再重复解释客户端 kind 数字。该集合筛选承担大类准入；槽位范围、固定目录、交易资格和容量继续各自负责。

## Scope

限 TaskContract 的现有容器重构，不新增存储列或协议字段。共享材料进个人金库后特性不变，当前数量的所有者由该容器决定。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | core 的只读派生 Traits 表示 Material、Equipment、Consumable、AccountShared 及现有隔离所需 Quest、Profession、Avatar、Creature；宠物装备/食物、副职业材料、时装徽章由特性组合表示。None 不代表合法物品。 |
| RQ-002 | 每个业务容器暴露 AcceptedTraits；通用判断为非空且属于接受集合的子集。未知位不能被现有容器接受。接受集合不隐含必需位。 |
| RQ-003 | 已有槽位、仓库和快捷栏大类判断使用同一分类/接受表；真实准入以模板 placement 为准，不将 record 上的派生字段当作可修改的新授权事实。 |
| RQ-004 | 交易条件、期限、锁定、容量、共享固定槽、堆叠、交换反向校验、UID、事务、持久化与协议投影保持；未知 kind/空类型拒绝。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | kind 1..11 的类别映射覆盖现有分类；十一种共享材料在共享容器及两类仓库中的 Traits 一致；普通材料没有 AccountShared。真实 PVF 的药品/礼盒映射均为 Consumable。 |
| AC-002 | RQ-002 | 接受矩阵覆盖现有业务空间；Material 与 Material+AccountShared 的子集语义、None 和未知位拒绝有直接检查。 |
| AC-003 | RQ-003 | 移动、仓库、到达快捷栏的筛选收敛；直接业务容器写入也检查大类。审阅确认模板权威与同一分类源，且无新增序列化真相。 |
| AC-004 | RQ-004 | 已有仓库交易限制、共享材料拆分合并/归还/角色隔离、容量、身份及协议回归通过；缺失或未识别类型不能通过快捷栏排除式判断。 |

## Success Evidence

已有行为测试作为保行为重构的主要证据；补充未知类型拒绝（防止旧排除式白名单放行）、新子集契约的矩阵检查和共享特性稳定性断言。按受影响路径运行应用、持久化、协议与真实 PVF 测试。独立评审检查调用链和未迁移白名单；这里只证明 CurrentImplementation，不升级为 ClientVerified。
