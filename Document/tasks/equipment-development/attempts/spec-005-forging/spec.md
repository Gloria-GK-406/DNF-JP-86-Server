---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：装备锻造

## Outcome and Evidence Boundary

角色通过 A21 `0x01B7` 普通锻造入口，以真实 PVF 规则尝试提高武器的独立 `GenuineUpgrade`。成功升一级、概率失败保级，两者消耗规定材料；拒绝与提交失败不扣物。客户端得到静态 handler 可读的结果及权威物品刷新。实机帧/GUI 后续集中验收。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 70、上游、A21 静态与真实 PVF 的入口、字段、概率、材料和响应证据分层记录；动态名、卷券尾标志和错误码未知单列。 |
| RQ-002 | 普通锻造只接受角色自有的合格武器与 PVF 指定材料；错槽、错模板、无权、锁定、过期、耐久不足及超过上限均拒绝且不变更资产。 |
| RQ-003 | 合格尝试按 PVF 消耗材料，成功仅增加 `GenuineUpgrade`，概率失败保级；不收无证据金币，不改 `EnchantUpgradeCount` 或 UID/其它属性；事务异常回滚且同序号重放不重复结算。 |
| RQ-004 | `0x01B7` 固定请求字段与普通动态尾受严格结构校验，卷券变体隔离；成功/失败响应按 A21 静态读取顺序编码，提交后刷新权威目标与材料。 |
| RQ-005 | Human Test List 为固定 PVF 样本给成功、概率失败、拒绝和重登步骤，并区分自动与未实测。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 证据定位到 70 ELF 符号/地址、上游路径、A21 RVA/VA 和 PVF 表/样本；未知不冒称 `ClientVerified`。 |
| AC-002 | RQ-002 | 确定性测试覆盖可锻造武器及无权、错槽/模板、锁定、过期、非武器、非满耐久、上限、材料不足拒绝与资产不变。 |
| AC-003 | RQ-003 | 可控 roll 测试覆盖成功/失败阈值、材料数量、UID/普通强化独立；SQLite 事务失败、重读及重放证明无半结算/双扣。 |
| AC-004 | RQ-004 | 字节/handler/真实 TCP 测试证明普通 `0x01B7` 请求、ACK、错误和装备/材料刷新；畸形动态尾及 variant1/2 在扣料前隔离。 |
| AC-005 | RQ-005 | Markdown 清单能供用户集中 A21 验收，标明本轮未运行客户端。 |

## Stop Boundaries

动态名字节不证明目标身份，不能用于选择材料或装备；身份一律按请求槽+模板与当前所有权核对。variant1/2 的卷券语义未闭合，不触碰资产。无 PVF 金币声明则不推断费用。不能由上游 ACK 样例推定 GUI 已实际验证。
