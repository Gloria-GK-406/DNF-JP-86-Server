---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---
# Specification: 绝望之塔单人闭环

## Why and Outcome
玩家进入绝望塔时按角色进度挑战下一层，通关获得当前内容支持的奖励并保存，重新登录继续下一层。

## Scope
楼层与最低等级从PVF读取；单人、无疲劳；按本服每日重置周期检查资格。100层后停止，不静默夹回100层重复领奖。

## Requirements and Acceptance
| Requirement | Acceptance |
|---|---|
| RQ-001 按角色进度选择层；请求任意塔DGN不能跳层 | AC-001 未通关进入1层，已通关n层进入n+1；100层后拒绝；组队/深渊/练习拒绝 |
| RQ-002 资格→凭证→收费→入场，空cost有效 | AC-002 每日首次入场免费；当日失败/退出后重试需邀请函；当日通关后邀请函可继续推进，最多通关三层；不足拒绝，失败组图/事务回滚不扣票或次数。此为当前PVF兼容本服规则，非70完整复刻 |
| RQ-003 权威通关一次性奖励和推进 | AC-003 仅服务器确定本层完成可推进，重复结算无重复奖励；奖励、长期进度、当日通关计数同事务；重登重启保留 |
| RQ-004 专用客户端投影，不走普通翻牌替代绝望塔结算 | AC-004 发送已核定的015C奖励与物品刷新；动态楼层需要的015B按已知布局；不编造0392 NOTI |
| RQ-005 可复现部署与回退 | AC-005 保留旧runtime和数据库备份，新runtime健康就绪，给出真实客户端测试集 |

## Evidence
Document/SpecialSoloDungeonsExploration-20261011.md及附件区分ContentStatic、70Reference、UpstreamReference、ClientStatic。新领域/事务/协议测试证明CurrentImplementation；用户测试前不标ClientVerified。
