---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: ./root-verification.md
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin/Document/tasks/gm-web-admin
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gm-web-admin
  branch: feature/gm-web-admin
  baseline: f63b7c67473f2f4d8f7593f704c9e15d4f1c5e38
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
| SC-1 | 模块启动和前端发布 | satisfied | spec-001启动矩阵；spec-006当前package及a4195c31 | 无开发缺口，实机待验 | aligned |
| SC-2 | 管理员权限与追踪 | satisfied | spec-002验收证据，416f26c9 | 后续写操作复用边界并回归 | aligned |
| SC-3 | 查询和在线状态 | satisfied | spec-003验收证据，573be46b | 后续模块回归共用边界 | aligned |
| SC-4 | 官方邮件 | satisfied | spec-004及root F-001接受f63b7c67；56前端回归 | A21客户端MAIL清单交用户验收 | aligned |
| SC-5 | 点券发放 | satisfied | spec-005验收证据，aa5b6636 | A21三币展示交用户验收 | aligned |
| SC-6 | 集成和验收材料 | satisfied | spec-006及root当前approved/fresh证据；f63b7c67候选 | 根accepted-baseline最终验证 | aligned |

## Spec Attempts

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-project-foundation/TaskContract.md | 项目与启动骨架 | SC-1, SC-6 | completed | attempts/spec-001-project-foundation/spec-result.md | 0630348d |
| 2 | spec-002 | attempts/spec-002-administrator-access/TaskContract.md | 管理员认证与审计 | SC-2, SC-6 | completed | attempts/spec-002-administrator-access/spec-result.md | 416f26c9 |
| 3 | spec-003 | attempts/spec-003-player-directory/TaskContract.md | 服务概览与玩家查询 | SC-3, SC-6 | completed | attempts/spec-003-player-directory/spec-result.md | 573be46b |
| 4 | spec-004 | attempts/spec-004-official-mail/TaskContract.md | 官方邮件编辑与发送 | SC-4, SC-6 | completed | attempts/spec-004-official-mail/spec-result.md | 07e1f607 |
| 5 | spec-005 | attempts/spec-005-account-cera-grants/TaskContract.md | 按账号发放点券 | SC-5, SC-6 | completed | attempts/spec-005-account-cera-grants/spec-result.md | aa5b6636 |
| 6 | spec-006 | attempts/spec-006-release-integration/TaskContract.md | 构建发布与最终验收集成 | SC-1, SC-6 | completed | attempts/spec-006-release-integration/spec-result.md | a4195c31 |

## Evaluation after spec-002

从已接受416f26c9和封存结果复评：SC-2 satisfied，SC-3/4/5尚未实现，SC-1/6保留最终集成缺口；所有约束aligned。child-sealed/child-accepted证据见workspace.md，root gaps-remain后进入successor_decision。

下一最小稳定结果为服务概览、全服账号/角色只读查询及当前在线/频道/位置。根task授权覆盖，已接受的QueryPlayers和会话边界复用，不修改资产，不切换现服；只涉及当前进程，没有外部未决前置。safe-successor→child_modeling，下一身份spec-003-player-directory。

## Evaluation after spec-003

从sealed result、fresh verification与573be46b干净接受证明复评：SC-2/3 satisfied；SC-4/5 not_started；SC-1/6仍保留最终集成和验收清单缺口，约束全部aligned。child-sealed→child-accepted证据见workspace.md，root gaps-remain后进入successor_decision。

下一最小稳定结果为官方邮件编辑（草稿/模板/预览）、明确选择单个或多个角色发送合法附件及可追溯记录。复用管理员SendMail与已接受玩家查询，在隔离库验证；现服和真实玩家不操作。根原授权覆盖，没有未决外部前置；safe-successor→child_modeling，身份spec-004-official-mail。重试不重复邮件、通知不倒退持久化事实与客户端可表示边界构成本child风险控制重点。

## Evaluation after spec-004

从当前sealed result、fresh verification和07e1f607干净接受证明复评：SC-2/3/4 satisfied；SC-5尚未实现；SC-1/6保留CI、完整发布集成与最终验收清单缺口。所有根约束aligned。全量与本机Chrome验证已完成，真实客户端MAIL验收仍由用户执行，不提升证据等级。

根gaps-remain后下一最小稳定结果为按实际账号/币种增加三种点券，支持无角色账号、单个或明确选择的批量账号、前后余额/记录及提交后当前钱包提醒。复用账号租约/事务、GrantCera身份与权限；不能用虚构角色0套入角色effect。根原task授权覆盖，不切换现服或真实发放，无未决外部前置。safe-successor进入child_modeling，确定身份spec-005-account-cera-grants；实际设计留给下一Spec。

## Evaluation after spec-005

从sealed result、current独立approved与fresh pass、aa5b6636干净接受证明复评：SC-2/3/4/5 satisfied；SC-1/6剩余CI前端准备、标准候选包静态资源和HTTP检查、完整运行说明与整体验收表。所有约束aligned，真实A21接收和余额展示保持用户待验。

根原授权覆盖最终集成；最小稳定结果为标准CI/本地打包可从源码构建GM资产、随Host独立配置运行，候选包检查无需Node运行时、不夹带环境数据，并提供最终清单。没有未决外部前置；Linux/远程CI只配置和静态核对，本机Windows实测，不宣称已执行远程平台。

下一身份为spec-006-release-integration，结果仅CI/本地候选发布集成与最终验收材料；根task授权覆盖，不操作现服、真实资产或外部Release。基于已接受模块与确定发布接缝，safe-successor进入child_modeling。

## Evaluation after spec-006

接受baseline a4195c31及最近sealed result重新核根合同：SC-1..6均satisfied，约束全部aligned。当前5373/54/11完整自动检查和原生Windows干净候选成立；既有独立启动矩阵/权限/会话/邮件领取/三币并发持久化证据与当前全量代码一致。GM01..21交付清楚保留A21和Linux/远程CI未执行边界。六attempt唯一且全completed，active为空；没有开发范围缺口，真实部署不在根授权。

root-satisfied进入root_review；请求独立完整根结果审查当前接受源码及契约覆盖。分支/工作区保留，不merge/push/清理，不修改封存结果。

## Root Review

### Round 1 — changes_required, local rework

独立完整根审查目标原baseline5fbe2dad→接受a4195c31，报告 [root-review-r1.md](reviews/root-review-r1.md)，SHA F1DED513609A0AFFE66D1D36ED474A321C2A1C953F63783089DF765158B82A59。除F-001外根功能完整且无其它实质缺陷；SC-4/6暂降为in_progress，未知失权重试约束violated，不能宣称根完成。

根review仍in_progress，局部修复路线明确、原授权/接受边界不变，按 [root-rework.md](root-rework.md) 修正当前根完整结果，待独立重审和fresh验证。六sealed结果/提交不改写；不应用尚有未解决缺陷的review-approved，也不为可本地修正的缺陷提前封存terminal根block。

### Round 2 — approved and accepted

当前重审 [root-review.md](reviews/root-review.md) approved，SHA185410C5；其余完整根检查引用不可改写r1。两文件当前manifest380FE146逐字节匹配接受f63b7c67，fresh [root-rework-verification.md](root-rework-verification.md) pass，接受clean/parent/source证明见workspace.md。F-001已解决，无未解阻塞，SC-1..6恢复satisfied、全部constraints aligned；六历史child仍completed，active null。

review-approved进入root_verification，最终只从接受baseline及当前源码/资源身份作完成判断。

## Root Verification

[根完成验证](root-verification.md) decision pass。当前接受baseline f63b7c67，source main5fbe2dadclean；完整5373服务端同源码、当前56前端/11工具、clean接受提交最终Win候选/HTTP/文档和代码身份通过。独立Root Review approved，所有SC satisfied及constraints aligned，6 unique completed rows/no active断言通过。真实A21/Linux/真实断网边界保持人工待验。

verification-passed原子封存completed/completed。开发和自动验证完成；分支/工作区保留，外部部署/整合另行授权。
