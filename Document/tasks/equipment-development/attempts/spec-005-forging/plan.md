---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：装备锻造

## Planning Choice

直接 Plan：锻造只有一个 `GenuineUpgrade` 资产事务及 `0x01B7` 玩家入口；内容表、领域结算、协议投递有清晰依赖，无需拆成独立 Task Unit。以快速实现配合独立结果审查和新鲜验证。

## Approach

- 真实 PVF `etc/upgrade_separate.etc` 是材料与概率的权威，解析上限、各级成功权重和材料系数、装备 grade 对应基础数量及 rarity 权重；复用既有装备定义取得武器类型、grade、rarity、满耐久。表结构或数字越界时安全拒绝。
- 领域层用可控 roll 决定成功/失败，不从请求名字确定模板；材料扣除与同 UID 装备 `GenuineUpgrade` 写回在同一事务。失败 roll 是已结算尝试、要扣材料；业务拒绝与提交异常不扣。保留普通强化字段与金币。
- `0x01B7` 只接普通 variant0；完整校验有界动态字符串长度/尾部，异变体和畸形包不进事务。成功 ACK 遵循 A21 handler 读取顺序，提交后物品槽刷新；连接内序列重放缓存原结果，写入失败后可安全再试。

## Work and Dependencies

| Work | Executable action | Depends on | Covers |
|---|---|---|---|
| T-001 | 固化 70、上游、A21 和真实 PVF 对照；更新 `0x01B7` 人工协议记录和人测样本。 | `c15e6add` 基线 | RQ-001、RQ-005 |
| T-002 | 先写真实 PVF 与领域测试，再实现内容源、可控 roll、目标/材料规则及事务结算；含概率失败、回滚和重读。 | T-001 规则边界 | RQ-002、RQ-003 |
| T-003 | 先写字节/handler/真实 TCP 测试，再接解码、命令、ACK、重放和权威槽刷新；隔离 variant1/2。 | T-002 | RQ-004 |

## Verification

定向测试、构建及相关全量套件；真实 PVF 应显式运行，不把缺档跳过当通过。完整结果独立审查后新鲜复测。A21 GUI/抓帧留在 Human Test List，不能自动标 `ClientVerified`。
