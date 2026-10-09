---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification：固定条件按业务事实即时检查

## Why and Outcome

满足现有固定条件后无需重新登录即可解锁。业务只报告事实，成就匹配规则，称号簿授予，通知投影继续经过 EventService。

## Scope

任务完成与当前职业、普通持有物品（Main 与账号共享材料）、装扮、已孵化宠物、穿戴装备。保留登录/升级补检。不增加未支持规则及过程累计。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | QuestCompleted 触发任务依赖及当前转职/觉醒检查，不用固定职业任务 ID 冒充职业状态。 |
| RQ-002 | 统一持久化物品写入报告容器变化事实，覆盖 Take/Put 和数量或物品类型变化（含孵化）；事件不得含成就 ID 或解锁指令。 |
| RQ-003 | 按相关范围筛选：Main/SharedMaterial 对应普通持有，Avatar 对应装扮，Pet 对应宠物，Equipment 对应穿戴状态。账号共享材料检查该账号角色；仓库与临时塔内物品不计入。 |
| RQ-004 | 事件与物品写入同事务持久化，提交后消费；同事务同容器合并报告，读取不发事件；回滚不产生奖励。 |
| RQ-005 | 跳过已完成/激活，重放幂等；任务型条件仍须正式完成；只读查询不执行补检。 |
| RQ-006 | 更新职责说明，验证并部署服务端；保留 SQLite、原配置和旧运行目录。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001, RQ-005 | 任务事件检查最终职业；接受未完成不能授予，重复事件只解锁一次。 |
| AC-002 | RQ-002, RQ-003 | 数量增减、孵化、装扮存取及穿戴只检查对应规则；账户变化影响其角色，仓库不触发持有条件。 |
| AC-003 | RQ-004, RQ-005 | 同容器一次报告；失败/回滚无有效事件；查询无事件；已解锁不读无关状态。 |
| AC-004 | RQ-006 | 构建及回归通过；独立审查批准；进程健康、端口和程序集身份明确；记录用户待测路径。 |

## Success Evidence

执行记录、独立审查、verification/completion-evidence.md 与 deployment.md。服务器测试为 CurrentImplementation，不冒充 ClientVerified。
