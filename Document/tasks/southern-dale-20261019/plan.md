---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 南部溪谷

## Planning Choice

一个主执行者按接入顺序维护完整计划；不需要任务单元文件。业务回归先保护共享身份和资产，再补最小领域接缝。

## Approach and Shared Decisions

- RQ-001：在EventManagementDomainService汇总按ID去重。69是相同空详情依赖；稳定保留首项，若遇到同ID内容冲突不能默默掩盖。主服务注册重复仍拒绝。
- RQ-002/003：增加SouthernDale内容源、规则服务、领域服务和受管入口活动；遵守规则输入由规则服务拥有。复用Admission与Visit，DGN100独立投影033F。南部主活动采用DGN已有schedule标识作为本服配置身份，空link避免猜测原生点击语法，不把策略当A21官方活动证明。
- RQ-004：南部票加入既有独立游戏日规则，在线周期和选角发布补检发送事务命令；与登录按同物品领取键结算。复用派发和通知模式，不改变其他普通补货。
- RQ-006：在MonsterDeathDomainService权威clear判定后、提交actor death前加入南部奖励，与SAO同一用例事务。独立持久领取真源以run/角色唯一，先领取再派发，失败整事务回滚；重复不能重发。运行态不缓存第二份资产。使用既有InventoryThenMail与物品变化事实。
- RQ-005/007：真PVF审计全部候选布局与房间；特殊房间沿用普通APC/独立掉落，不误走手动深渊。物件协议仍用已证0027。配方/收集任务通过既有路径核验。

## Work and Dependencies

| Work | Detailed action and relevant boundaries | Checks |
|---|---|---|
| T-001 | 为EventManagement列表创建最小重复69行为RED；实现合并，再接南部受管活动、调度状态及启停组合。更新旧牛头测试，关闭牛头仍保留南部依赖。 | 相同/冲突身份、单开双开全关、未开始到期、登录/在线投影 |
| T-002 | SouthernDale内容读取必要声明（队伍限制/禁练习/无惩罚/钥匙），规则层提供catalog；领域准入校验模式/活动/等级/重复run并委托Admission。接EnterDungeonCommand，不复制协议。 | 真PVF内容、全16MAP、等级/票/失败/回滚/组队准入 |
| T-003 | 票2680738独立06:00日切；新增在线事务命令和登录发布补检，配置过滤只选目标票，复用既有补货/投递。 | RED/GREEN跨日、交错、重登/重建、满包待领邮件与旧记录 |
| T-004 | 新建最小南部领取store/SQLite唯一表与Schema，注册模块；南部CompleteClearAsync核验权威claim和全队，发奖后推送刷新；MonsterDeath调用。 | 重复、不同run、失败回滚、重建幂等、全队；调用端/事件/分层覆盖 |
| T-005 | 以真内容测试房间、物件/APC、掉落、魔罐/金币罐配方与任务语义。修复测试暴露的局部缺口，范围仍遵循Spec。补protocol人工区和实现/实机步骤。 | no-PVF回归、真PVF覆盖、构建；不伪造wire证据 |

## Verification

每块先保护实际行为，非业务结构调整只跑既有测试。实现检查通过后本地implementation提交；独立审查完整diff，对照Contract/Spec/Plan；修正后fresh verification并seal。不部署、推送、合并或关闭未实机验收issue。
