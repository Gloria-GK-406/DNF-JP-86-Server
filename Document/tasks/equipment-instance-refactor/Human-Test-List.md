# 装备实例重构最后验收

TASK-008失败规则及完整损毁产物已通过独立复核并部署，当前PID40292 ready。新增材料验收请使用[Failure-Human-Test-List](Failure-Human-Test-List.md)。以下TASK-007金额、券数量和PID为上一部署记录，用户此后强化、丢弃保护券及白装损毁已经改变资产；不得把这些旧绝对数值作为当前恢复目标。

当前状态：**重新部署完成，可以继续验收**。TASK-006随机值/失败阈值日志已启用，TASK-007四券过期字段已去除、三个客户端PVF更新、额外5000万金币到账。此前连续成功根因仍未知；实际旧服务已有自然保护失败，不将纯抽样正常当作人测通过。

## 当前版本
- 运行目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-reinforcement-probability-20260930/runtime`，PID40060；bootstrap ready，7001/10010/10011/18080由同一进程监听，stderr空。三个客户端实际PVF hash均与各自候选一致。
- 基线6e60c4c6加当前未提交结果；完整33个Server文件集合指纹 `93fad50a72ab3fe88cc2ed55b3134f3755d3e0de4915bcd9e4412ed22f1ddd75`，诊断runtime已切换，代码/概率与TASK-006复核相同。当前契约rev3及[期限部署完整复核](reviews/change-review-expiry.md)保留原结构/保护门。
- 正确普通强化字段：Attribute低5位/ReinforcementLevel；独立附魔升级次数EnchantUpgradeCount保持，高3位密封次数保持。实例/规则/领域分工不变，wire与82字节schema不变。
- 原库在线备份：新工件目录`before-field-fix.db`及新发放`artifacts/equipment-supplies-20260930/before-grant.db`，quick_check=ok。发放后基线金币10089516/无色59796；用户长剑至+15的冻结快照金币5007846/无色58456，随后sequence123保护失败回10，最新核对金币2992701/无色58236、超级券49，其他三券各50。详见[发放清单](verification/supplies-grant.md)及[现场诊断](verification/probability-diagnosis.md)。

## 已付费长剑：精确恢复已完成

用户实际操作「传承 : 冥域魔神的逆光长剑」UID689/模板101040127/主背包绝对槽9，18:28:20成功0→1，但写入独立附魔次数而非强化位段。

用户明确回复“恢复一样吧”后，已应用并独立核对[恢复结果](verification/asset-recovery.md)：本次成功+1已写入Attribute低5位，附魔次数恢复原0；其余80字节、UID、槽、耐久、金币/无色保持，不追加扣费。请不要为恢复结果再对这件剑重复点击强化。

此前恢复为+1；之后用户将长剑连续强化至+15，sequence123自然失败被超级券保至+10。当前等级以最后实际提交为准，正常退出重登应保留退出前记录。原恢复是已付费结果恢复，不替代修复版R-01的一次新强化。

## 修复版本的最终顺序

| ID | 操作 | 通过标准 | 状态 |
|---|---|---|---|
| E-01 | 查看[当前典型例子](implementation-examples.md) | 认可规则决定、领域结算、实例修改状态的分工 | 待用户反馈 |
| R-01 | 用未变的「女王御赐皮靴」UID339/模板100260176/+0/Dur20，普通强化执行一次0→1 | 即时显示+1，仅扣450金币/10无色；以点击前后差值核对；core9低位1、core17保持0，UID/其余字段保持，只有一次提交；四种保护券均不减少 | 待用户 |
| R-02 | 正常退出游戏，再登录1123 | R-01装备+1及实际余额保留，无重复扣费；UID689长剑与退出前最后记录一致，独立附魔次数0 | 待用户 |

原长剑对应的一次操作已经支付；恢复它无需再强化。R-01使用另一件原有低费皮靴验证新请求的即时更新，不能把原版本成功动画或直接恢复数据替代新版本GUI验收。若资产因其他操作变化，用点击前后差值核对，不能机械套用绝对余额。

## 证据
运行环境server.stdout.log的UPGRADE_ITEM、server.stderr.log及发放基线；[保护当前验证](verification/protection-current.md)。当前所需程序集证据汇总4557通过/0失败/2条件skip：两个变化测试程序集新全量通过，其他六个未变目标引用原完整结果；不是本轮全solution测试命令。公开生产模块/真实PVF/副本probe证明原运行基础；这些不替代用户反馈或凭抓帧配置冒认WireCaptured。

其他养成及原[普通强化完整清单](../equipment-development/attempts/spec-001-reinforcement/Human-Test-List.md)未验项继续后续执行。用户最后通过前不提交本地完整结果。

契约rev3保护券另按[保护券清单](Protection-Human-Test-List.md)继续，先P-00确认金币52992701/无色58236/长剑+10和四券50/49/50/50及不再过期；R-01同时证明保护券成功不扣，不能代替真实失败保护效果。[期限与部署证据](verification/protection-expiry-current.md)保存追加资金和全部资产保持的检查。
