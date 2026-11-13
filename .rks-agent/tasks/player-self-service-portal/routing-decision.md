# Routing Decision

- TaskContract: `.rks-agent/tasks/player-self-service-portal/TaskContract.md`（审查时位于主检出同一仓库相对路径，按 using-git-worktrees 以哈希校验原样迁入任务 worktree）
- Contract revision: `2`（`task-contract-review.md` verdict approved，contract_revision 2）

## SDD Readiness

意图、范围与验收已稳定：需求原话、issue #329/#305 验收、方案 D1–D6 均已采纳并逐条落到 SC-1–SC-13；中心实现方向（独立玩家身份与会话、从 GM 点券/邮件服务剥出身份无关核心、受信代理转发头、开关表与迁移）在已确认的方案中给出，其余细节属于授权内的实现裁量。剩余不确定只有两处实现取舍（迁移后用户测试库的处理、配置代理后回环是否仍视为安全连接），由 Spec 决定并配测试，不改变任务语义。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 用户明确要求先写任务契约再执行，表明接受流程耗时以换取精确；Direct 的具体失败路径是在十余项相互牵连的验收（会话隔离、范围裁剪、开关即时生效、限额、审计、#305 转发头、迁移、前端、用户测试准备）中遗漏一项或在长时间实现中偏离，尤其是越权读取或会话串用这类不易被肉眼发现的访问控制缺陷；Spec 固定验收、Plan 安排依赖顺序、对抗挑战与独立结果审查能拦住这些遗漏，代价值得。所有结果共享同一个登录与授权边界、同一次 schema 版本提升和同一组前后端接口，不能各自独立验收或回滚，因此是一个 Spec。
- Reroute conditions: 若实现中发现 #305 转发头改动必须与玩家后台分开发布（例如线上需要先单独上线 #305），或剥离 GM 核心导致必须先完成独立的重构才能继续，则改为 Multi Spec；若关键前提（启动器密码校验可复用、钱包/邮件核心可无身份依赖地调用）被证伪，回到契约修订。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `long-running`
- Rationale: 任务直接改变认证与访问控制（新增一类登录身份、会话、按会话裁定可见范围、转发头影响限流与同源检查），属于 Governed 硬下限；独立 Spec/Plan 挑战与完整结果挑战针对的正是越权与会话串用这类普通审查容易放过的失败。工作跨服务端多层、迁移工具与前端，单元多、跨多次上下文，选择 long-running 以 task-units 渐进分解并在偏离时做回顾性审查；Standard 没有独立挑战，不足以覆盖访问控制硬下限。

## Execution Configuration

- Profile: `long-running` with `sdd-governed`; long-running: `yes`（`compose_profile.py long-running --controller sdd-governed`，含 `attention_review: on-drift`）
- Spec baseline: `20bcaf37c7282cf06efe767fb3b0a992364f9653`（origin/main，Merge PR #331）
- Gates: `spec-review: auto; plan-review: auto; implementation: auto; drift-check: auto; final-review: auto; verification: auto`
- Bound methods: `implementation: test-driven-development; verification: 用户测试准备按 Document/FeatureDevelopment.md §4`
- Artifact path overrides: `none`
- Workspace: linked worktree `E:\Project\DNF\DNF-JP-86-Server-player-portal`，分支 `task/player-self-service-portal-329`（仓库规则 FeatureDevelopment.md §1 要求独立 worktree）
