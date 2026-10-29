---
kind: rks-verification
decision: manual_required
---

# 遗忘之地候选验证

验证主张：分支`codex/77-forgotten-land`已变基至`b9ffc750`主干，版本保持主干`0.2.10`。已安排的主玩法和森林红色晶石修复通过用户验收，用户授权关闭不限次及准备PR。当前运行服务保留已验收的变基前产物，仅切回正常入场额度，不补扣验收期间的次数。

## 当前实现

专用探索计划由`DungeonRunMode.LostLand`持有，统一入场路由选择对应领域服务；共享能力表明确禁止普通Boss结束、翻牌发奖、难度解锁及普通重挑战。有效额度视图及准入扣次决定由`ILostLandRuleService.QuotaAsync`根据疲劳／历史次数facts和本服配置返回，业务服务只取事实及执行决定。热血计算由必需注入的`ILostLandFeverRuleService`承担，热血状态、截止时间和击杀事实由既有run owner持有；领主共享保底由已提交击杀推导，没有第二份领取状态。

胶囊房间转换、变身药水扣量、修理券扣量与装备耐久复用现有事务和容器；角色道具事实读取主干的`ICharacterItemUseDomainService`，不回退主干角色分层。修理券入口复用唯一`GameRequestFreshness`，其城镇／副本共用的单入口例外有具名用途并受既有次数和未登记位置反例保护。

巨龙时间奖励和遗忘之地共享保底在同一个死亡提交中组合，各自保持玩法类型边界。森林150／151敌对判定由`LostLandEnemyRules`统一提供，地区掉落仍限制在红色晶石区域。新增森林热血计分修正有行为RED与自动回归，尚未另行部署／实机确认。规则服务职责归位保持既有算法与数值，不增加并行状态、服务定位或可空依赖。

## 当前验证

规则职责归位后完整构建0警告0错误、1652项Unit及99项专项通过，均无跳过。对应证据为`evidence/review-rules-build.log`、`review-rules-units.log`和`review-rules-focused.log`；原始变基验证保留如下。版本声明与主干保持一致，开发PR不修改版本号。

| 检查 | 结果 | 证据 |
|---|---|---|
| 完整Release构建 | 0警告、0错误 | `evidence/rebase-build-final.log` |
| 服务端Unit，与PR CI同口径 | 1652项通过，0失败、0跳过：PVF37、应用815、内容23、网络20、协议757 | `evidence/rebase-units-final.log`；TRX保存在私有测试目录 |
| 遗忘之地、修理券、死亡事务及结构专项 | 99项通过，0失败、0跳过；包括真实森林两房间、共享保底、防重复、持久拾取、胶囊／变身、计时结束、真实占位及依赖图 | `evidence/rebase-focused-final.log` |
| 协议Unit与加载／计时专项 | 779项通过，0失败、0跳过，包含Unit集合及22项加载／计时专项 | `evidence/rebase-protocol-final.log` |
| 真实PVF内容与物件奖励池 | 42项通过，0失败、0跳过 | `evidence/rebase-pvf-content.log` |
| GM前端 | 13个测试文件、158项通过 | `evidence/rebase-gm-tests.log` |
| Unit报告判据 | 3项通过 | `evidence/rebase-report-guard.log` |
| 文档同步检查 | 检查未通过；失败诊断与主干源码／catalog逐项相同，无本次新增缺项，不称为通过 | `evidence/documentation-comparison.txt`及对应日志 |
| 正常额度恢复 | 仅私有appsettings变化，74个程序／资源保持；SQLite backup API一致备份、无监听副本启动、真实库115张表保持、默认端口及就绪通过 | [当前配置](AcceptanceOverrides.md)及`evidence/live-normal-quota-final-check.json` |

测试集合有重叠，不将各行相加作为独立用例总数。构建与测试未部署变基后的源码，也不能替代客户端反馈。

## 用户验收与未验证边界

已确认范围集中维护于[主玩法验收](Human-Test.md)；掉落细则及森林修复证据见[击杀产出](DropPolicy.md)，道具与修理券分别见[道具专项](TeleportDiagnosis.md)和[修理券](RepairCoupon.md)。1分钟原生到期自动总结及回城已获用户确认；完整20分钟到期未独立反馈。

## Unverified Conditions

- 变基适配及追加森林热血修正尚未另行部署／实机复测。当前服务的已验收版本及正常额度策略见[配置](AcceptanceOverrides.md)。
- 正常次数实机扣减／耗尽、100疲劳边界与自然跨日；全部13个任务链、死亡／复活与客户端关闭后的恢复、最终入口精确拒绝，仍保留[剩余清单](Remaining-Acceptance.md)。
- CMD0211动态生成请求仍未接入；不把原生对象表现或本轮已验收主流程推广为任意客户端生怪授权。
- 云端PR审核与CI独立于以上本地验证；未取得当前提交通过结果前，不宣称云审核通过或合并完成。
