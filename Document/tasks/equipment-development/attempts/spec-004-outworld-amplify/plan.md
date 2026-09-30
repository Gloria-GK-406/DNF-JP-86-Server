---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：异界气息与异次元属性

## Planning Choice

直接 Plan：先完成证据门，再以一个装备实例事务串接内容规则、领域结算、协议投递；净化与赋予/扭转共用状态字段但各有请求格式。用户要求快速开发，保留结果独立审查与新鲜验证。

## Approach

- 规则只读 A21 PVF，内容源解析 `etc/amplifyitem.etc` 的装备等级下限、四类选项权重/基值、稀有度权重及净化/赋予/扭转材料。核心状态由 `AmplifyType` 的 `0x80` 位和低位属性编码承载，`AmplifyValue` 由服务端计算；属性赋予与扭转不读取请求中的模板之外的客户端数值。
- 先写真实 PVF 与领域/协议失败测试；以选中角色、容器当前行和事务作为权威。净化随机可注入种子，失败/重放不得重复结算。成功后回权威结果及槽位刷新。
- 固定包 `0x00CC` 12 字节，`0x00CD` action 0/1 固定 14 字节；action 2 动态尾隔离。70/上游只用作交叉检查。人测清单不冒称已验证。

## Work and Dependencies

| Work | Executable action | Depends on | Covers |
|---|---|---|---|
| T-001 | 核对 70 ELF、上游、A21 构包/接收和真实 `amplifyitem.etc`；记证据及未决，修正协议人工区。 | `4dbd73b4` 基线 | RQ-001 |
| T-002 | 内容源与领域 TDD，目标/材料校验、状态转换、随机属性、事务与重读测试。 | T-001 闭合规则 | RQ-002、RQ-003 |
| T-003 | 协议字节/handler TDD，响应与权威槽刷新、重放保护；Human Test List。 | T-002 | RQ-004、RQ-005 |

## Verification

定向测试、构建和相关回归；真实 PVF 全量与既有基线失败分开报告。完整增量独立审查，修复后再做新鲜验证；用户 A21 GUI/抓帧后续集中执行。
