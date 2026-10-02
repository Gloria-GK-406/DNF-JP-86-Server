---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: child_execution
phase_state: in_progress
active_attempt: spec-005
last_transition: child-created
transition_evidence: ./attempts/spec-005-account-cera-grants/activation.md
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin/Document/tasks/gm-web-admin
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin
  branch: feature/gm-web-admin
  baseline: 07e1f607777c082a09e4614d5d81cf137dee41ce
  baseline_evidence: ./workspace.md
---



# Multi-Spec Map

## Multi-Spec Boundary

启动骨架、认证、查询、邮件、点券分别可以独立审查、验证和提交，所有子任务串行共用一个工作区。

## Overall Direction

按项目与启动骨架 → 管理员和审计 → 服务/账号/角色查询 → 官方邮件 → 账号点券发放 → 集成收尾推进。只创建当前最小稳定子合同，后续具体设计从已验收结果继续收敛。

后续连接的代码事实（尚非已接受设计）：`MailBuilder.FromSystem`/`IMailboxDomainService`已支持系统邮箱持有附件、领取才入背包；普通装备必须用新分配的 detached UID，不能伪造模板为实例。当前邮箱投影把标题、换行和正文合并，并有512字节编码边界，后续编辑/预览需保证可完整表示。`IAccountTable`提供账号三币读取/CAS，现通用EffectExecutionContext要求角色归属，点券发放不能通过虚构角色ID绕过无角色账号。账号执行管线已有多账号有序租约与共同SQLite事务；通知本来在提交后best effort，可通过Abstractions通知契约与Protocol投影衔接。最终CI与release.py仍缺Node构建/测试和干净checkout的GM资源检查，留在最终集成阶段闭合。

## Controller Cursor

按 multi-spec-workflow 的 transition/validation 引用执行；每次激活或恢复检查唯一 active child、合同与依赖链接、HEAD 基线和路径包含关系。

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 模块启动和前端发布 | in_progress | spec-001，0630348d | 最终管理配置集成 | aligned |
| SC-2 | 管理员权限与追踪 | satisfied | spec-002验收证据，416f26c9 | 后续写操作复用边界并回归 | aligned |
| SC-3 | 查询和在线状态 | satisfied | spec-003验收证据，573be46b | 后续模块回归共用边界 | aligned |
| SC-4 | 官方邮件 | satisfied | spec-004当前验收证据，07e1f607 | A21客户端MAIL清单交用户验收 | aligned |
| SC-5 | 点券发放 | not_started | none | 完整能力 | aligned |
| SC-6 | 集成和验收材料 | in_progress | spec-001/002/003构建、发布及模块验证 | 标准 CI、完整回归和最终验收表 | aligned |

## Spec Attempts

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-project-foundation/TaskContract.md | 项目与启动骨架 | SC-1, SC-6 | completed | attempts/spec-001-project-foundation/spec-result.md | 0630348d |
| 2 | spec-002 | attempts/spec-002-administrator-access/TaskContract.md | 管理员认证与审计 | SC-2, SC-6 | completed | attempts/spec-002-administrator-access/spec-result.md | 416f26c9 |
| 3 | spec-003 | attempts/spec-003-player-directory/TaskContract.md | 服务概览与玩家查询 | SC-3, SC-6 | completed | attempts/spec-003-player-directory/spec-result.md | 573be46b |
| 4 | spec-004 | attempts/spec-004-official-mail/TaskContract.md | 官方邮件编辑与发送 | SC-4, SC-6 | completed | attempts/spec-004-official-mail/spec-result.md | 07e1f607 |
| 5 | spec-005 | attempts/spec-005-account-cera-grants/TaskContract.md | 按账号发放点券 | SC-5, SC-6 | running | pending | active |

## Evaluation after spec-002

从已接受416f26c9和封存结果复评：SC-2 satisfied，SC-3/4/5尚未实现，SC-1/6保留最终集成缺口；所有约束aligned。child-sealed/child-accepted证据见workspace.md，root gaps-remain后进入successor_decision。

下一最小稳定结果为服务概览、全服账号/角色只读查询及当前在线/频道/位置。根task授权覆盖，已接受的QueryPlayers和会话边界复用，不修改资产，不切换现服；只涉及当前进程，没有外部未决前置。safe-successor→child_modeling，下一身份spec-003-player-directory。

## Evaluation after spec-003

从sealed result、fresh verification与573be46b干净接受证明复评：SC-2/3 satisfied；SC-4/5 not_started；SC-1/6仍保留最终集成和验收清单缺口，约束全部aligned。child-sealed→child-accepted证据见workspace.md，root gaps-remain后进入successor_decision。

下一最小稳定结果为官方邮件编辑（草稿/模板/预览）、明确选择单个或多个角色发送合法附件及可追溯记录。复用管理员SendMail与已接受玩家查询，在隔离库验证；现服和真实玩家不操作。根原授权覆盖，没有未决外部前置；safe-successor→child_modeling，身份spec-004-official-mail。重试不重复邮件、通知不倒退持久化事实与客户端可表示边界构成本child风险控制重点。

## Evaluation after spec-004

从当前sealed result、fresh verification和07e1f607干净接受证明复评：SC-2/3/4 satisfied；SC-5尚未实现；SC-1/6保留CI、完整发布集成与最终验收清单缺口。所有根约束aligned。全量与本机Chrome验证已完成，真实客户端MAIL验收仍由用户执行，不提升证据等级。

根gaps-remain后下一最小稳定结果为按实际账号/币种增加三种点券，支持无角色账号、单个或明确选择的批量账号、前后余额/记录及提交后当前钱包提醒。复用账号租约/事务、GrantCera身份与权限；不能用虚构角色0套入角色effect。根原task授权覆盖，不切换现服或真实发放，无未决外部前置。safe-successor进入child_modeling，确定身份spec-005-account-cera-grants；实际设计留给下一Spec。
