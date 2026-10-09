---
kind: rks-review
verdict: approved
---

# 固定成就条件实时事件检查：独立结果审查

## 审查目标与判据

- 基线：`d0427ce3ecc987ab76050f0e714fe919c7188fcf`。
- 目标：`task/achievement-state-events` 当前 tracked/untracked 产品、测试、职责文档 diff；审查时尚未提交。
- 约束来源：本任务 TaskContract revision 1、spec.md，仓库 AGENTS.md；判据为 `reviewing-work/SKILL.md` 与 `references/code.md`。
- 已检查统一 Item/Creature facet 入口、可靠事件事务边界、共享材料账号角色分流、规则范围与依赖链、查询只读及业务/成就分离。没有并行启动构建或测试。

## 初轮发现（已修复）

### F-1：未初始化的角色无法通过物品事件完成固定条件后继

**影响**：账号共享材料事件检查所有角色，但没有既有成就进度行的离线角色，完成持有条件后不会同时解锁依赖它的 `clear quest` 条件。后继要等登录或另一个执行 Prepare 的事件才补上，与本次事件触发及依赖链收尾目标不符。

**实际代码路径**：

1. `AchievementDomainService.cs:187` 只把匹配该容器的 `seeking` / `equipped item` 交给 CheckSatisfiedAsync；`clear quest` 后继不在这批 candidates 中。
2. `CheckSatisfiedAsync` 在 `:79` 只为本批实际满足的规则 EnsureAsync，因此未初始化角色的后继仍没有进度行。
3. 随后的 `ReconcileAuthoritativeAsync` 在 `:149` 调用 CompleteAsync；后者 `:120` 明确对 `before is null` 返回 false，不会初始化后继。
4. TitleBook 的 AchievementCompleted 订阅仅授予相应称号，也不会代为初始化/完成依赖成就。该事件随后会被成功标记消费。

**最小复现输入**：沿用 SharedMaterial 测试中的两个未 Prepare 角色，将 FakeDefinitions 设置为 `9001: seeking [7463,1000]`、`9002: clear quest [9001]`，向账号共享材料写入 1000 个 7463 后 Drain。当前路径仅创建/完成 9001，9002 缺失。本结论来自逐分支静态推导，审查者未自行执行测试或修改测试源。

**最小修复**：在受支持、已满足且满足前置约束的 `clear quest` 后继被完成前补齐进度行，或让固定条件检查包含其受支持依赖闭包；不需要为了补齐行而全量读取无关物品/装备事实。加上述未 Prepare 角色、共享材料、两级或多级依赖及重复投递回归。

## 已核对的成立部分

- 持久化 Take、Put（含合并）、Update 的模板/数量变化统一报告事件；无直接物品写入业务调用成就/通知。
- Creature facet 即便先被打开，也向同一个 Item facet 传递事件入口，孵化模板变化能够登记。
- 事件 Enqueue 和物品写入经相同数据库 participant 结算，提交后唤醒；同事务同容器登记合并。异常及回滚不会留下有效事件。
- Main/SharedMaterial、Avatar、Pet、Equipment 的筛选与事实读取保持一致；仓库、塔内临时物品不计入持有条件。
- 共享材料按账号角色分流并排除已删除角色；消费键包含角色和 EventId，重试事务回滚及重放幂等路径成立。
- 查询 GetAsync 路线无补检写入；已激活及完成的相关规则会跳过角色状态快照。
- QuestCompleted 在奖励及职业效果应用后入队，职业检查使用最终 Career 状态；任务型称号继续依赖正式完成记录。

## 验证证据与边界

- 已读取 `verification/state-events-core.trx`：95 executed、95 passed、0 failed。
- 核心新增回归覆盖物品事务回滚/合并、数量更新、仓库转移、账号多个角色、重放、装扮/穿戴筛选、Creature facet 孵化、任务后职业状态。
- 全量 Application 回归仍由控制器执行；控制器报告两条等待时序失败正在复验，审查未将其归因于本变更，也未以核心通过代替全量结论。
- 本审查不建立 ClientVerified 或部署就绪结论。

## 修复轮复审

- 当前目标为上述 diff 加 F-1 修复；产品改动仍局限原七个 tracked 文件与原两个新增事件入口文件，测试为原新增测试文件内扩展共享材料用例。
- `ReconcileAuthoritativeAsync` 仅在规则受支持、等级/模式/前置约束均通过、所需依赖全部成立后，为缺失行执行 EnsureAsync，再 CompleteAsync。没有引入查询写入、历史计数推测或任务受理绕过，也不额外读取无关物品/装备状态。
- 共享材料用例现在在两个未 Prepare 角色上验证 `seeking -> clear quest -> clear quest`，随后保留重复 envelope 投递无新完成事件的断言。
- 已读取 `verification/dependency-red.trx`：修复前 1 executed、1 failed，明确 Expected 3 / Actual 1，证实 F-1；修复后 `verification/state-events-final.trx` 为 95 executed、95 passed、0 failed，该扩展共享材料用例 Passed。F-1 关闭。
- 已读取 Application 全量结果：3307 executed、3304 passed、3 failed；隔离复跑为 3 executed、2 passed、1 failed。两条等待时序失败隔离已通过；剩余 NotificationRegistration 的 `'UnhandledProbe'` 日志断言失败，在主干基线复跑文件中同样为 1 executed、1 failed 且错误文本一致。未将其称作全量通过；控制器仍负责验证记录和部署身份。
- 已读取 `baseline-notification-permissions.trx`：同一主干基线 NotificationRegistration 在提升执行权限后 1 executed、1 passed。支持日志写入权限差异的归因；控制器正在复跑当前分支全量，本审查没有提前声称该轮通过。

## 结论

`approved`：F-1 已有实际修复前失败与修复后通过证据，本次授权范围内没有未解决的阻断性缺陷。该 verdict 是实现结果审查批准，不替代部署就绪或客户端实测验收。
