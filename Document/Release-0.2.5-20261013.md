# 0.2.5 发布记录

2026-10-13 用户授权将 `task/despair-tower` 创建 PR、提升版本、合并后发布 Release。目标基线为 main `67ffa14f`（0.2.4）；本次 PATCH 提升一次至 0.2.5。使用 merge commit 保留原分支提交。

## 变更范围

- 统一副本入场凭证，协调门票、次数与运行状态的事务提交及失败回滚。
- 实现绝望之塔持久楼层、每日免费资格／邀请函、通关奖励和选图状态同步；出售物品统一移除 NPC／商店／场景限制，保留物品与金币校验。
- 接入黄龙与青龙单人大会，从各自 PVF 读取门票、对手、疲劳、经验和奖励；单人自动补选提前到奖励界面的有效窗口。
- 统一被动奖励派发及满包邮件，主动开包维持背包容量约束；怪物死亡相关状态、任务和奖励统一在死亡结算事务内处理。
- 统一任务查询与交付计划，修复共享材料的登录校准、任务条件与提交消费；清房和持久化物品变化经可靠事件推进相关任务，再同步客户端任务快照。

实现与边界见 [绝望之塔](DespairTower.md)、[入场凭证](DungeonAdmission.md)、[死亡结算](MonsterDeathTransaction.md)、[大会与共享材料修复](outputs/tournament-autoflip-awakening-20261011.md)、[任务事件](QuestProgressEventModel.md)、[奖励派发](tasks/item-delivery/delivery-policy.md)及[任务统一结果](tasks/quest-domain-unification/spec-result.md)。

## 验收边界

绝望之塔周目重置／兑换、装扮奖励池和完整经济掉率尚未恢复。大会当前仅支持单人，自动翻牌时限不保证高延迟网络下的显示效果。任务的纯金币通用变更事件、部分特殊击杀目标和放弃任务道具回收仍未覆盖。以上功能的完整 A21 实机边界按各自记录保留，发布不将静态分析或自动测试升级为 ClientVerified，也不自动关闭未完成的 issue。

本次不包含性能测试分支的后续提交。历史大会调查草稿继续保留在本地工作区，当前行为以已提交的实现记录为准。

## 验证与发布

本地完整内容回归使用部署档案 `artifacts/avatar-socket-text-20261011/Script.server.pvf`，SHA-256 为 `49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A`。日志及 TRX 位于当前工作区 `artifacts/release-0.2.5-checks/`。

Release 构建通过，0 警告、0 错误；完整 .NET 回归 6,910 通过、0 失败、2 条条件跳过（有档案时不执行的缺档案场景）。各项目通过数：Application 3,728、Protocol 1,460、Persistence 468、Infrastructure 874、Host 159、HttpApi 50、Networking 91、Pvf 80。

GM 前端 148 项测试及生产构建、发布保护 11 项、WireSmoke 5 项通过。协议同步检查覆盖 145 个入站及 215 个出站身份，目录 366 个身份，缺口 0；检查器自测通过。补齐大会 7 个目录身份，并更新旧补选时限和青龙范围说明。

首次构建的 NuGet 网络限制及工具测试临时 Git 权限失败已重试解决，没有关闭漏洞检查或过滤测试；前端按 lockfile 安装依赖后验证通过，构建仍提示既有的大 chunk 警告。

PR 双平台 CI 全部通过后合并；主干 CI 通过后从合并提交创建 `v0.2.5`。Server Release 工作流重新执行 Windows x64／Linux x64 测试、打包、启动冒烟及 SHA-256 检查后发布附件，实际结果以对应 Actions 为准。

发布包不包含 PVF、角色数据库或客户端二进制。本次发布不更换正在运行的部署；升级应备份并保留现有数据库、匹配的服务端 PVF 和部署配置。
