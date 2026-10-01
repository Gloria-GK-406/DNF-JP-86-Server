# Routing Decision
- TaskContract: `./TaskContract.md`
- Contract revision: 4
## SDD Readiness
2026-09-30继续授权失败规则服务及完整损毁产物，rev4新增SC10；保留同一未完成规格的实例、规则和事务职责。沿用Task Units模式，由TASK008实现，再完整独立review、切换与最后人测。用户已正常退出游戏；该确认允许候选通过自动检查后切换，不替代候选检查或最后验收。
2026-09-30用户授权补物资及强化保护闭环，revision2在同一未完成装备任务延展现有强化路径；原Task历史和SC6待验状态保留，新保护条件也须最后验收。沿用原controller/profile，无重建执行模板或另建worktree。
用户明确执行授权，接受职责分工并要求契约与最后验收。范围基于现有业务，未证增幅玩法不扩张；各成功条件有可观察证据。
## Structural Selection
- Selected workflow: single-spec-workflow
- Rationale: 用户要求完整重构与最终验收。直接逐文件修改容易保留旧core写入、混入规则或漏掉修理/耐久，实例基础若错误会传播到后续结算。单一规格、渐进依赖计划和独立结果评审保护共同的装备状态边界，值得控制成本。各用例一起交付同一结构重构，没有独立发布/回滚需求。
- Reroute conditions: 必须增加业务规则/协议或改变物品存储身份时重新对齐；发现独立验收/回滚边界才切Multi。
## Controller Selection
- Controller: sdd-standard
- Profile source: long-running
- Rationale: 先实例身份再迁移多调用方，跨上下文易漏完整覆盖，Lite缺少渐进依赖与全局回看；已有事务和可验证备份恢复，Standard的计划、行为回归和独立结果评审足够，不增加Governed的多轮产物批准。
## Workspace
- root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap
- branch: task/solo-play-roadmap
- accepted_baseline: 6e60c4c67417ce06b88e9e99277fb1e4c0239726
- baseline evidence: git root/git-dir/common-dir证明linked worktree，无submodule，HEAD一致，任务开始status为空。复用当前会话任务分支，不创建嵌套worktree。此契约文件为新增durable task artifacts。
