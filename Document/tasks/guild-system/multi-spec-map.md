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
| SC-01 | 迁移、成员唯一和身份 | in_progress | spec-001 基础验证，spec-002 选角与角色详情身份投影 | 名册及玩法资格 | aligned |
| SC-02 | 创建与入会 | in_progress | spec-001 创建、申请与审批事务，spec-002 A21 单人创建与扣费 | 名册、搜索、推荐与申请审批的客户端入口 | aligned |
| SC-03 | 治理与退出 | in_progress | spec-001 退出、踢出、解散、删角保护 | 职务、资料管理等其余行为与 A21 入口 | aligned |
| SC-04 | 社交与聊天 | in_progress | spec-002 用户确认推荐频道可设置，库中保存 CH10 | 聊天、邀请、频道跳转及其他社交 | aligned |
| SC-05 | 成长与权益 | not_started | 无 | PVF、周期、收益规则和实现 | aligned |
| SC-06 | 设施、仓库和勋章 | not_started | 无 | 场景、资产、装备端到端 | aligned |
| SC-07 | 支援、礼物和红包 | not_started | 无 | 资格、交付和结清 | aligned |
| SC-08 | 公会地下城与公会战 | not_started | 无 | 玩法证据、周期与结算 | aligned |
| SC-09 | 范围与证据 | in_progress | spec-002 已归档建会、身份及推荐频道证据 | 其他功能的证据和覆盖 | aligned |
| SC-10 | 构建、测试与验收 | in_progress | spec-001、spec-002 相关回归及隔离验收记录 | 后续分期测试和正式验收准备 | aligned |

## 子任务

| # | Attempt | TaskContract | 结果边界 | 贡献 | Result | Git result |
| ---: | --- | --- | --- | --- | --- | --- |
| 1 | spec-001 | [任务契约](attempts/spec-001-persist-guild-membership/TaskContract.md) | 公会创建和原子成员关系 | SC-01、SC-02、SC-03 | [完成](attempts/spec-001-persist-guild-membership/spec-result.md) | `872a4808c173d60ab05dd42d4852992049937369` |
| 2 | spec-002 | [任务契约](attempts/spec-002-create-guild-in-a21/TaskContract.md) | A21 单人建会、身份恢复与推荐频道验收 | SC-01、SC-02、SC-04、SC-09、SC-10 | [完成](attempts/spec-002-create-guild-in-a21/spec-result.md) | `0d1e7dbb8eebbe3f9c3232a91a2aa598b0c4169d` |
| 3 | spec-003 | [任务契约](attempts/spec-003-view-guild-roster/TaskContract.md) | A21 公会成员名单 | SC-01、SC-02、SC-09、SC-10 | | |
