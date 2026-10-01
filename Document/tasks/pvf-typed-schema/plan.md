---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---
# Implementation Plan: PVF 字段全量迁移

## Planning Choice
同一 API 驱动所有迁移，详细步骤由本计划统一管理，不另建任务注册表。

## Approach and Shared Decisions
- Dfo.Pvf 提供通用类型化声明和读取，档案偏移按实例绑定。
- Infrastructure 按格式/主题分组字段。复杂混合段落保留专业解析器并声明 token 列表/块形状，不强制单值化。
- 迁移 GetTag、名字比较、字典索引、字符串字段辅助函数及动态固定集合；枚举值和路径保留用途。
- 在公共 API 传播前建立合成保护，业务记录和 effect 不变，损坏不可被 catch/default 吞掉。

## Work and Dependencies
| Work | Action and checks | Covers |
|---|---|---|
| T-001 | 盘点所有入口/形状，确认真实 PVF，构建/内容测试基线 | RQ-001,005,006 |
| T-002 | 类型化字段与读取、合成保护类型/列表/缺失/损坏/重复/跨档案 | RQ-002,003,004 |
| T-003 | 全量迁移内容源和固定工具字段、分组验证、扫间接入口 | RQ-001,002,003,005 |
| T-004 | 全量覆盖保护与真实内容回归，清理旧入口、实现说明 | RQ-001,005,006 |
| T-005 | 完整构建/测试、独立审查修复、新鲜验证、一次提交 | RQ-006 |

依序执行，共享接口成立后传播，最后检查全部调用面。

## Verification
共享输出构建/测试串行。真实 PVF 为当前 DNFClient-A21-protocol/Script.pvf，只读不部署。保存各项目结果，标记跳过/基线。独立审查针对整个结果。
