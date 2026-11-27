---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system
  branch: feature/guild-system
  baseline: cada01a0ec33ad9e3ec34fcad1ce2ca475244fd4
  baseline_evidence: git ls-remote 确认主干同一提交，git rev-parse 与 branch 核对工作区和分支，初始只含本任务文档
---

# 公会系统分期实施

## 分期边界

核心数据和成员事实可独立验证，A21 交互由实际帧和界面验收。仓库、礼物等资产操作，以及地下城、公会战等周期结算，各自有独立的不变量和回退条件。

## 实施方向

先交付基础关系与事务，再接 A21 创建及身份和成员管理、持久化聊天与社交。成长规则和资产托管分别接入，最后完成周期玩法和全量覆盖验证。每次只激活一个子任务，以当前代码和证据选定下一项。

## 成功条件进度

| SC | 要求 | 进度 | 已验收证据 | 缺口 | 约束 |
| --- | --- | --- | --- | --- | --- |
| SC-01 | 迁移、成员唯一和身份 | in_progress | spec-001 基础验证，spec-002 身份投影，spec-003 名册 | 其他入口的玩法资格 | aligned |
| SC-02 | 创建与入会 | in_progress | A21 建会、名册、搜索、推荐及[申请和同意](a21-guild-applications.md)实机通过，审批和重复提交在线路验证 | 撤回、拒绝、异常和重登组合的实机覆盖 | aligned |
| SC-03 | 治理与退出 | in_progress | spec-001 删角保护。A21 权限、任命、[图标公告](a21-guild-appearance.md)、[个人备注](../../../protocol/game/client-to-server/0x00E0.md)、[会长转让](../../../protocol/game/client-to-server/0x009B.md)和[金币改名](../../../protocol/game/client-to-server/0x02F8.md)已有实机验收记录。[退会、踢出和解散](a21-guild-social-and-warehouse.md)已经过隔离线路 | 旗帜待接入，退会相关界面待实机核对。中文长度 [#390](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/390) 和改名按钮状态 [#391](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/391) 作为客户端问题跟踪 | aligned |
| SC-04 | 社交与聊天 | in_progress | 推荐频道设置、上下线提示、实时名单及[公会聊天和切换频道](a21-guild-social-and-warehouse.md)已通过实机。主动邀请和[公会联盟](a21-guild-alliance.md)已通过隔离线路 | 联盟窗口与双入口实机验收，邀请界面、公告板及频道跳转完整回归 | aligned |
| SC-05 | 成长与权益 | in_progress | 捐赠和明细、签到和明细、[权益购买、组队传送和击杀经验](a21-guild-contents.md)已通过相关实机验证。[日常贡献](a21-guild-daily-contribution.md)的签到、聊天、重登去重和会友组队通关贡献币已实机验收，两类通关奖励通过隔离线路。[月度贡献和月初发奖](a21-guild-monthly-contribution.md)已有实机验收 | 公会地下城第 5 次奖励、限时属性数值和到期仍待单独实机覆盖。[旧公会升级](a21-guild-base-upgrade.md)已退出本期范围 | aligned |
| SC-06 | 设施、仓库和勋章 | in_progress | [基地入口、仓库扩容、材料存取](a21-guild-hall.md)已有验收记录。[仓内移位和历史](a21-guild-social-and-warehouse.md)已经过隔离线路。[证书兑换、勋章装备与强化、两种合成](a21-guild-certificate-and-medal.md)已实机通过，守护珠镶嵌已通过隔离线路 | 移位与历史界面、复杂装备、守护珠镶嵌的完整实机复测 | aligned |
| SC-07 | 支援、礼物和红包 | in_progress | [同盟礼物](a21-guild-gifts.md)已通过互赠、领取及双边收礼列表实机验收。[公会红包](a21-guild-red-packets.md)已通过原生页签、购买、发放、领取和代币券到账的实机验收 | 红包自然满 24 小时的结算与退款留待上线后观察。排行榜与历史界面尚未逐列验收。[志愿兵 #389](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/389)随独立功能交付 | aligned |
| SC-08 | 公会地下城与公会战 | in_progress | [伤城进图、通关、净化率与首层解锁](a21-guild-dungeon.md)、反复通关礼盒、[地下城延长券](a21-guild-dungeon-extension.md)、[莱茵轮换](a21-guild-medal-shop-rotation.md)和[2、3、4 人组队 Buff](a21-guild-dungeon-party-buff.md)已有实机验收 | 后续地图与自然周期切换留待正式部署反馈。随机深渊、周六翻倍和证书掉落需核对 A21 启用规则，永恒殿堂独立周期奖励仍待取证。[基地战已搁置](a21-guild-hideout-war.md) | aligned |
| SC-09 | 范围与证据 | in_progress | spec-002 已归档建会、身份及推荐频道证据 | 其他功能的证据和覆盖 | aligned |
| SC-10 | 构建、测试与验收 | in_progress | spec-001、spec-002 相关回归及隔离验收记录 | 后续分期测试和正式验收准备 | aligned |

## 子任务

| # | Attempt | TaskContract | 结果边界 | 贡献 | Result | Git result |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | spec-001 | [任务契约](attempts/spec-001-persist-guild-membership/TaskContract.md) | 公会创建和原子成员关系 | SC-01、SC-02、SC-03 | [完成](attempts/spec-001-persist-guild-membership/spec-result.md) | `872a4808c173d60ab05dd42d4852992049937369` |
| 2 | spec-002 | [任务契约](attempts/spec-002-create-guild-in-a21/TaskContract.md) | A21 单人建会、身份恢复与推荐频道验收 | SC-01、SC-02、SC-04、SC-09、SC-10 | [完成](attempts/spec-002-create-guild-in-a21/spec-result.md) | `0d1e7dbb8eebbe3f9c3232a91a2aa598b0c4169d` |
| 3 | spec-003 | [任务契约](attempts/spec-003-view-guild-roster/TaskContract.md) | A21 公会成员名单 | SC-01、SC-02、SC-09、SC-10 | [完成](attempts/spec-003-view-guild-roster/spec-result.md) | `da6f8a02e14ea7c114765e6125b91da39ab26774` |
| 4 | spec-004 | [任务契约](attempts/spec-004-donate-gold-for-gm/TaskContract.md) | A21 金币捐赠、公会 GM 和个人贡献 | SC-05、SC-09、SC-10 | [完成](attempts/spec-004-donate-gold-for-gm/spec-result.md) | `79ecd013c9b13db117137fa4e929496344733ac8` |
| 5 | spec-005 | [任务契约](attempts/spec-005-guild-member-presence/TaskContract.md) | A21 上下线提示与实时名单 | SC-01、SC-02、SC-04、SC-09、SC-10 | [完成](attempts/spec-005-guild-member-presence/spec-result.md) | `f2809adf9a70108b4b785b9f5b4d71fc37b2b220` |
| 6 | spec-006 | [任务契约](attempts/spec-006-guild-attendance/TaskContract.md) | A21 每日自动签到、进度与明细 | SC-05、SC-09、SC-10 | [完成](attempts/spec-006-guild-attendance/spec-result.md) | `f14cf4cd9` |
| 7 | spec-007 | [任务契约](attempts/spec-007-guild-management/TaskContract.md) | A21 会长管理权限、宣传与等级配置 | SC-03、SC-09、SC-10 | [完成](attempts/spec-007-guild-management/spec-result.md) | `63804bc8f` |
| 8 | spec-008 | [任务契约](attempts/spec-008-guild-role-and-discovery/TaskContract.md) | A21 成员任命、公会查找、账号公会与推荐 | SC-01、SC-02、SC-03、SC-09、SC-10 | 执行中 |  |
