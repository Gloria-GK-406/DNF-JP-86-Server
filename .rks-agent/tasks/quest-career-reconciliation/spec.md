---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---
# Specification: 转职任务即时生效
## Why and Outcome
任务7840已确认缺失grow type奖励。本结果补齐普通转职，不创建另一套技能/属性规则。
## Scope
TaskContract Scope。当前运行服务和数据库不自动改变；交付待部署验收代码。
## Requirements
| ID | Requirement |
|---|---|
| RQ-001 | PVF grow type奖励解析成目标转职；合法目标与任务完成在同一事务生效，无效目标拒绝并回滚。 |
| RQ-002 | 统一技能整理依职业规则保留适用已学等级和槽位，删除不适用技能，补齐免费/自动等级，两页幂等；保留达人合法技能。 |
| RQ-003 | 复用点数账本、职业基础属性、角色身份和装备外观投影。 |
| RQ-004 | 成功转职先通知完整技能表，再完成任务ACK，再发送新角色属性/身份和任务投影；普通任务原行为不变。 |
## Acceptance Criteria
| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 合法转职持久化、普通任务回归、非法/重复结算不赠送；故障回滚职业/技能/任务。 |
| AC-002 | RQ-002 | 两页保留通用技能槽位/付费等级，删除职业不适用技能，合并免费等级，重复整理稳定，达人越级不误删。 |
| AC-003 | RQ-003 | 新职业基础属性/点数由既有服务重算，新身份投影反映转职。 |
| AC-004 | RQ-004 | 录制发送消息顺序0013→0022→新角色状态；特殊ACK字节向量和普通ACK回归。 |
## Success Evidence
自动测试覆盖AC-001..004，实机通过与否由用户随后验证，不将测试替代ClientVerified。上游顺序和特殊ACK为代码推导证据。
