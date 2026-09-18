# TASK-01 — Story wire、target/default 与生命周期证据

## Task Unit Contract

- Revision: 1 (identifies this contract and its detailed plan)
- Outcome and references: 为 Plan T-001、RQ-001/RQ-005 与 AC-001 交付当前 A21 Story 的可实施证据边界：
  固定上游与客户端输入，闭合原生 Story 选择到 C2S `0x000C` 构包及 S2C 设置消费，裁决 category、
  target/default、标题/容量与直接生命周期约束，并给出最小安全实机捕获方案。本单元不修改生产代码。
- Owned boundary: 只允许在本子规格 `evidence/task01/` 新增静态分析脚本、固定输入、字段表、裁决和捕获
  计划，并在本文件追加执行结果；可只读访问 `ServerS4A21`、当前 A21 B1、`client-debug-lab` 既有工具/
  文档及已接受 Normal 证据。不得修改生产/测试/`protocol/`、客户端文件、旧任务或其他 worktree。
- Required checks: (1) 记录所有结论输入的路径、大小、SHA-256 和版本/commit；(2) 上游行为与当前 A21
  证据分开，冲突以 A21/实机为准；(3) 逐项覆盖 12-byte 设置中 Story 相关字段，构包写入与消费至少各有
  直接证据或明确 unknown；(4) 所有 VA/RVA/文件偏移注明基准 ImageBase；(5) 不把 `0x023C` 公共目录
  或 `0x01BE` 快速匹配并入 Story 基本设置，除非静态调用链证明不可分割；(6) 分析脚本可重复运行，静态
  窗口带哈希，`git diff --check` 通过且工作树无产品 diff。
- Dependencies: 接受基线 `9a3a0b492da11f48525d1225d02513e0dd1cd458` 的 byte-exact `PartySettings`、
  严格 `0x000C`、Normal target=0、精确 session/revision 通知与已接受证据；无未解决外部 TaskContract 依赖。

## Detailed Plan

1. 建立 `evidence/task01/input-manifest.md`：固定当前 B1 EXE、`ServerS4A21` 源码树 Git 身份或目录输入、
   `client-debug-lab` HEAD、使用的 VMP/wrapper 映射和已接受 Normal 静态/运行时资料哈希；只引用实际消费的
   文件，避免把整个工具树当证据。
2. 从 `ServerS4A21/Server/DfoServer/Network/Parsers/Party/SetPartyInfoRequest.cs`、
   `Network/Handlers/PartyHandler.cs`、`Game/Party/` 与构包器向上下游追踪，记录上游对类别 0、target、标题、
   容量、创建/编辑和进入/离队的实际使用；没有模式分支时明确判为 opaque pass-through，而非 Story 规则。
3. 从已接受 Normal 的 `0x000C` 构包地址、设置对象偏移和 UI 入口出发，在当前 B1 中反向定位类别 0 的选择
   分支、target 控件/列表、默认初始化与提交路径。优先复用 `client-debug-lab` 的 wrapper/VMP 恢复成果；
   间接调用需解析 vtable slot 或保留准确 frontier，不能因遇到 wrapper 就停止。
4. 由 `0x000C` 写入点向前证明每个 Story 字段的数据源，由 S2C `0x0009` 设置读取点向后证明消费者、索引
   范围和 UI/状态分支；输出 `story-field-map.md`，列出 wire offset/宽度、source、consumer、constraint、
   confidence 和与 Normal/Cycle 的差异。
5. 检查 Story 创建/编辑之后紧邻的进入、返回、离队或目标变更路径；只记录直接依赖 category/target 的
   状态转换。单纯副本/任务/奖励代码不扩展范围；发现 `0x01BE` 或其他快速匹配依赖时画出边界并判断是否
   触发父级 reroute。
6. 建立可重复的静态 verifier，固定关键窗口/调用点/资源引用并输出 PASS/失败原因；形成
   `static-verification.md` 与 `story-verdict.md`，把 Confirmed static、High、Hypothesis 分开。
7. 基于静态结果写 `runtime-capture-plan.md`：列出原生 Story create/edit/特有操作、定向 allowlist、预期
   `0x000C/0x0009` 字节、Host 判定、UI 观察点和停止条件。只设计，不在本 Task 启动客户端或发送帧。
8. 重跑 verifier，核对证据哈希和 task-local diff；确认没有生产、测试、协议或客户端改动后记录本 Task
   结果。只有 verdict 给出可测试的合法 Story 边界，才允许父执行器规划 T-002。

## Result — Round 1 (`done`)

### Changed

- 在 `evidence/task01/` 新增固定输入清单、上游基线、逐字节 Story 字段图、静态验证记录、裁决与运行时
  捕获计划；没有修改生产、测试、`protocol/` 或客户端文件。
- 新增 `probe_story_party.py`，固定当前 B1/dstr、19 个 Story 语义窗口、6 条资源文本及固定上游 commit
  的 5 个 Git blob。

### Why

当前静态调用链已足以把 target 收敛为剧情任务/场景条目 ID（High）并确认 Story 禁止委任队长，但不足以
证明合法 wire target/default 或关联 opaque 字节。既有服务端已经无损接受 category 0；在原生帧到达前
新增拒绝规则会把未证推断变成兼容性风险，因此本 Task 明确停止在证据边界。

### Checks

- 固定探针新鲜运行 exit 0：B1/dstr 身份与 ImageBase PASS，`19/19` byte anchors、`6/6` resources、
  `5/5` upstream blobs PASS。
- `git diff --check` PASS；工作树变化只位于
  `.rks-agent/tasks/party-mode-story-cycle/**`，没有 product/test/protocol/client diff。
- `evidence/task01/task-verification.md` 对本 Task 的 outcome 与 required checks 给出 `decision: pass`。

### Remaining

- Spec AC-001/AC-005 仍缺当前 A21 原生 Story create、edit 与特有操作时间线；T-002 不得物化。阻断输入和
  复现步骤见 `evidence/task01/story-verdict.md` 与 `runtime-capture-plan.md`，由 Spec/父控制器处置。
