---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# 实施计划

## Approach

保留现有 SQLite 动态事实与事务设施。在 Data 定义无依赖值，在 Abstractions 定义规则和内容契约，在 Infrastructure 解析 PVF，在 Application 组合不可变规则并迁移消费者。规则实例可以持有只读声明和接受显式 facts，不读取角色数据库。现存技能 slot/extra_values 属于动态配置与协议投影，保留存储格式。

## Decisions Before Work

职业值由 job、转职索引、觉醒阶段组成，打包适配集中；支持集合来自 PVF。属性内容保持未丢失精度的声明，规则服务准备 1—86 表。技能源缓存只按静态职业/技能标识，不按角色等级或已学集合；兼容现有入场定义可作过渡投影，但成本算法只有一处。基础属性不覆盖装备等尚未建模的贡献；历史基础属性列不再作为当前基础属性真源。

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-001, RQ-002 | 职业值、完整成长声明、共享属性规则与初始化；创建接入。 | 真实 PVF 成长/初值、职业转换、全范围与性能测量。 |
| T-002 | RQ-001, RQ-003, RQ-004 | 技能不可变规则、成本及免费基线；移除已学组合缓存；迁移选角和余额算法。依赖 T-001 职业值。 | 真实技能案例、多值上限/零上限、累计 SP/TP、前置以及现有入场回归。 |
| T-003 | RQ-005, RQ-006 | 角色当前属性/技能事实入口、选角/升级等消费者迁移，临时库回滚重载验证，更新架构说明。依赖前两项。 | 当前查询与 SQLite 事务测试、全套构建/TRX、独立完整审查。 |

## Choices During Implementation

通过 live code 决定现有兼容接口保留量和持久字段投影接缝，不改变存储数据与对外帧布局。对于缺失或不能解释的 PVF 组合明确拒绝并记录证据；不要凭职业名推断支持范围。
