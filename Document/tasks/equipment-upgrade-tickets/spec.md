---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---
# Specification: 固定等级券

## Outcome and Evidence
保留0x0050 method0强化/method1增幅，按所选物理材料槽中的券区分NPC与券场景。PVF的 `[equipment reinforcement ticket]` 和 `[equipment amplify reinforcement ticket]` 为目标等级、百分比成功率、apply mode、apply value。当前所选券为fixed/-1：7275为12/100，7814为13/100，10008100为11/90，10008107为12/90。概率失败保留原等级依据ServerS4A21的Ticket场景penalty1；这属于UpstreamReference，并非本次实帧成功证据。

## Requirements and Acceptance
| ID | Requirement | Acceptance |
|---|---|---|
| RQ-001 / AC-001 | 静态具名声明解析完整形状，缺失与损坏分开，未知合法模式明确不支持 | 四种真实券值正确；坏型、范围、尾项、重复拒绝；非券仍走原NPC |
| RQ-002 / AC-002 | 固定券成功覆盖为指定等级，可覆盖较高原等级；概率来自券 | 100%成功、90%边界和失败可重复验证；不同目标、原等级均正确；不是+1 |
| RQ-003 / AC-003 | 概率失败保留原等级，成功/失败只消费1张券，无金币/无色/矛盾/保护券费用或返材料 | effect计划完整，领域资产差分、身份和属性保持，最后券槽清空 |
| RQ-004 / AC-004 | 强化券用于普通装备，增幅券用于已辨识且有效的异次元装备；原禁用/耐久/锁定等准入保持 | 武器、无耐久首饰及不合法目标；错kind、过期/锁定券、无券、不支持模式/来源都不支付 |
| RQ-005 / AC-005 | 提交后回应/库存通知立即显示，数据库与内存一致 | 实际领域/handler、回滚和重复处理检查；重登等实机待验 |
| RQ-006 / AC-006 | NPC和既有保护仍可用、冻结版本审查后新鲜验证并部署 | 相关及完整回归、审查approved、部署版本/备份/监听/ready及人工清单 |

明确不新增保护券参与概率券失败；不把票券套入普通强化成本与NPC等级上限表。未知非fixed/-1模式明确拒绝，不能误当普通材料或猜测含义。
