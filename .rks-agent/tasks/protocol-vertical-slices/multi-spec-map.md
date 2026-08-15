---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: "激活前校验全部返回空错误：无活动子任务且 Spec Attempts 表无 running/active 行；标识 spec-001 为首个尝试（无已封存尝试，编号不复用）；路径 attempts/spec-001-di-inbound-message-decoding/ 经规范化证明包含于 task_root 之内；子 TaskContract 为 ready，parent 指向 ../../TaskContract.md，contributes_to 为 SC-1/SC-3/SC-4/SC-5/SC-6，depends_on 为空序列（合法，无未决外部前置，已接受的内部历史由 workspace.baseline 表示）；子级 routing-decision.md 已完成 SDD Readiness、Marginal Control Value 与 Structural Selection（选定 single-spec-workflow 并给出相邻层级的排除理由），仅保留 Controller Selection 待 single-spec-workflow 在实例化 Profile 前补全。已追加恰好一行 running/active 并设置 active_attempt。前序 safe-successor 证据：记录到一个有界稳定成果：入站解包由 DI 注册表驱动。它完全落在根 task 授权范围内（根 Scope/In 第 1、3、4 条逐条覆盖），有单一验收面（入站接线由注册完成且线级行为不变），其证据可独立复核（SC-1/SC-4/SC-5/SC-6 加 SC-8/SC-10/SC-12 的复证），且可在不依赖任何后继成果的情况下被接受或回滚——出站编码与切片重排此时均未触及。它是三个成果中唯一无前置依赖者，故为唯一安全后继。SC-3 仅能因此进入 in_progress：该条同时要求出站接线，须待第二个成果。无约束被分类为 at_risk 或 violated，无需和解型子任务。不在此创建或激活子契约。前序 gaps-remain 证据：在接受基线 06b24e7 上重算根覆盖：12 条成功条件全部 not_started，无已接受证据。当前缺口——两处入站 CreateMessage switch（4+10 分支）与 sender 的 9 分支出站 switch 仍在；无解包器注册表，故 SC-3/SC-6 无机制；MessageHandlerRegistry 的枚举完备性检查仍在，故 SC-5 不成立；14 个协议仍散在 Data/Networking/Application 三个项目，故 SC-7/SC-9 未开始；SC-11 的边界规则在 AGENTS.md 中尚未记录且现状未审计。SC-4/SC-8/SC-10/SC-12 在基线上成立但须随每次接受复证，故不计为已交付进度。全部 6 条根约束分类为 aligned：基线编译 0 警告、563 项测试 0 失败、分层引用图未放宽、线级行为未动。存在缺口且无违反约束，进入后继决策。前序 workspace-ready 证据：唯一任务工作树建立于 .worktrees/protocol-vertical-slices，分支 codex/protocol-vertical-slices，自 06b24e7 创建并经身份复证（toplevel/git-dir/common-dir 证明为链接工作树而非子模块，非嵌套）。启动期任务工件已以哈希校验的方式移入同一仓库相对路径，源检出恢复干净且 main 顶端不变。工作树内基线复验：编译 0 警告 0 错误，全量测试 563 项 498 通过 / 65 跳过 / 0 失败。"
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/protocol-vertical-slices/.rks-agent/tasks/protocol-vertical-slices
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/protocol-vertical-slices
  branch: codex/protocol-vertical-slices
  baseline: 06b24e72acddc230a74f54f671eec416f7f3a15b
  baseline_evidence: |
    身份：git worktree list 显示两个工作树——源检出 D:/project/DNF/DNF-JP-86-Server 在 main@06b24e7，任务工作树
    D:/project/DNF/DNF-JP-86-Server/.worktrees/protocol-vertical-slices 在 codex/protocol-vertical-slices@06b24e7。
    工作树内 git-dir=.git/worktrees/protocol-vertical-slices、common-dir=.git，superproject 为空，确证为链接工作树且非子模块。
    .worktrees/ 由 .git/info/exclude:7 排除，未改动 .gitignore。
    工件搬迁：TaskContract.md 与 routing-decision.md 自源检出移入任务工作树的同一仓库相对路径，sha256 前后一致
    （TaskContract 7338d9fb…21066b，routing-decision 3db9bbf0…2340b），源副本已删除，源检出 git status 为空，main 顶端仍为 06b24e7。
    产品基线（工作树内，SDK 10.0.201，自仓库外 cwd 构建以绕过 global.json 的 10.0.302 锁定）：
      dotnet build 86JP.Server.slnx -> 已成功生成，0 警告 0 错误（TreatWarningsAsErrors 开启）
      dotnet test  86JP.Server.slnx -> 0 失败；Pvf 60（51/9）、Application 265（255/10）、Networking 113（113/0）、
                                       Infrastructure 121（75/46）、Host 4（4/0）；合计 563，498 通过 65 跳过
    已知基线脆性：首次在工作树运行全量测试时 Dfo.Server.Host.Tests 出现 1 次失败（4 项中 3 通过），紧随其后的单独重跑与
    全量重跑均 4/4 通过。该套件会拉起打包后的 exe 监听真实端口，且该次运行紧接源检出的全量测试之后，端口释放竞争为
    嫌疑原因但未证实（失败消息未捕获）。记录于此以免后续 Spec 将同类瞬时失败误判为自身回归。
    工具链注记：需先 dotnet build-server shutdown 或传 -nodereuse:false，否则残留 MSBuild 节点会锁定工作树的 obj 文件。
---

# Multi-Spec Map

## Multi-Spec Boundary

根 TaskContract 的目标包含三个独立可接受、可验证、可回滚的成果，其验收性质彼此不同，合成单一 Spec 会使其中最有审查价值的性质失去验证点：

1. **入站解包 DI 化** —— 验收性质是「机制替换正确且线级行为不变」。触及 `Networking/Runtime/NetworkPlugins.cs` 与 `Application/Dispatching/MessageHandlerRegistry.cs`，完成后系统可用且完整。
2. **出站编码 DI 化** —— 同为机制替换，但触及 `TouchSocketNetworkMessageSender` 与 `SelectCharacter` 的 59 帧有序投递链路，风险等级与第 1 项不同，需独立的行为不变验收面。
3. **切片重排** —— 验收性质是「零逻辑变更」，与前两者根本不同：它要求 diff 中除文件位置、命名空间、using 与可见性外无任何行为改动（SC-9）。若与真实机制改动合并进同一个 diff，该性质不可验证。

三者依赖单向且顺序固定（切片重排必须在编解码脱离 Networking 之后），但边界独立——任一项可在不依赖后继成果的情况下被接受或回滚。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 入站不存在按消息号/type 选择消息构造的 switch，接线由注册完成 | not_started | none | `ChannelNetworkPlugin.CreateMessage`(4 分支) 与 `GameNetworkPlugin.CreateMessage`(10 分支) 仍在 | aligned |
| SC-2 | 出站不存在按消息 CLR 类型选择编码的 switch | not_started | none | `TouchSocketNetworkMessageSender.SendAsync` 9 分支仍在 | aligned |
| SC-3 | 新增协议接线只需新增文件与一行注册，不改既有文件 | not_started | none | 尚无注册机制 | aligned |
| SC-4 | 消息身份仍由 `ClientMessageType`/`ServerMessageType` 承载 | not_started | none | 基线已满足枚举存在，但注册形态尚未建立，待随 SC-1 一并确证 | aligned |
| SC-5 | 只登记未接线的枚举成员不导致启动失败 | not_started | none | `MessageHandlerRegistry` 完备性检查仍在 | aligned |
| SC-6 | 解包器产出的消息类型若无 handler，在启动时被发现 | not_started | none | 无解包器注册表，无该检查 | aligned |
| SC-7 | 每个协议的 DTO/解包/编码/消息 handler/命令/命令 handler 同目录 | not_started | none | 14 个协议仍散在 Data、Networking、Application 三个项目 | aligned |
| SC-8 | 编译期分层未放宽 | not_started | none | 基线成立；每次接受时复证 | aligned |
| SC-9 | 切片重排步骤未夹带逻辑变更 | not_started | none | 重排未开始 | aligned |
| SC-10 | 线级可观察行为不变 | not_started | none | 基线成立；每次接受时复证 | aligned |
| SC-11 | 消息 handler 不直接实施业务语义或状态迁移 | not_started | none | 现状未审计；AGENTS.md 尚无该边界规则 | aligned |
| SC-12 | 无回归，对齐 563 项测试基线 | not_started | none | 基线已采集；每次接受时复证 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-di-inbound-message-decoding/TaskContract.md` | 入站解包由 DI 注册表驱动：消除两处 `CreateMessage` 分支，启动检查由枚举完备性换为解包器×handler 接线闭合性，14 个入站协议解码逐字段不变 | `SC-1`, `SC-3`, `SC-4`, `SC-5`, `SC-6` | `running` | `pending` | `active` |
