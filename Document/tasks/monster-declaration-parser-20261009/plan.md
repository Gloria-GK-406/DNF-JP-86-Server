---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：完整声明与消费者投影

## Planning Choice

一个详细 Plan 管理统一模型、token 解析和消费者接线。调查可并行；生产文件写入按明确文件边界串行，不创建 Task Unit 注册表。

## Approach and Shared Decisions

新 `DungeonActorSpawnDeclaration` 保留资源语义，`DungeonMapActorDeclarations` 保留条件/附属信息。`PvfMapActorDeclarationParser` 不读取角色状态；内容源缓存声明并适配现有 actor API。运行 actor 携带其声明来源；房间规则设置模板投影身份和清房资格，编码器不解析资源。

## Work and Dependencies

| ID | Work | Covers |
|---|---|---|
| T-001 | 汇总子代理全量PVF/上游/70调查，确定游标边界、精英尾部、条件与对象索引；记录到survey。 | RQ-001 |
| T-002 | 先写真实22036、21314消费者失败测试并观察RED，再实现统一模型、变长token解析、精准错误、条件/附属保留与source缓存。 | RQ-002,RQ-003,RQ-006 |
| T-003 | 接线规则/Cell/START_MAP，保留ordinary/APC索引、dummy不参与战斗收益，对象模板使用来源父子编号与延迟标记，波次清房不被Boss抹除。 | RQ-004,RQ-005 |
| T-004 | 执行纯语法边界测试、全MAP结构扫描、内容/房间/通关/深渊/循环回归；派独立审查并修正。 | RQ-001..RQ-006 |
| T-005 | 当前runtime配置/DB/进程身份确认后publish，停止已验证旧实例，备份DB，隐藏启动新实例，检查健康/监听/程序集hash。旧产物保留。 | RQ-007 |

## Verification

RED保护切房合法资源可构建以及对象模板完整性，旧测试无法覆盖变长字段和模板来源。新语法测试直接断言消耗与声明，不冻结实现类选择。串行dotnet tests；设置实际PVF路径，明确跳过与未执行项。部署构建包含已接受baseline，程序集版本携带任务来源；不提升PR基础版本。用户验收追击Boss房、根特防御波次及普通/深渊房间。
