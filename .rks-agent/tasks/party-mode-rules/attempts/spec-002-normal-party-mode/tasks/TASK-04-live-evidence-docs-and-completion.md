# TASK-04 — 当前 A21 实机闭环、协议归档与完成控制

## Task Unit Contract

- Revision: 2 (current-A21 correction; identifies this contract and its detailed plan)
- Outcome and references: 在 TASK-02/TASK-03 最终实现上完成 Plan T-004、RQ-006/RQ-007、
  AC-006/AC-007，并汇总 AC-001..AC-005：以当前 A21 的定向帧、Host 处理结果与用户 UI 观察验证
  Normal 预设标题、多人队长解散和最后成员离队；同步协议资料并完成完整审查与新鲜验证。
- Owned boundary: 本 Spec 的 `evidence/task04/`、本 Task/Spec result/review/verification 工件、
  `protocol/game/client-to-server/0x000C.md`、`protocol/game/server-to-client/NOTI-0x0009.md`、相关最小
  samples、`protocol/patterns/data/traces/`、`protocol/game/index.md`、`protocol/catalog.md`；若最终实帧
  证明当前实现有偏差，可在本 Spec 已授权的 Party Data/Abstractions/Application/Protocol 与测试内做
  最小修正。不得实现公开队伍目录、Story/Cycle 专属规则或普通成员编辑权限变化，不改客户端二进制、
  插件或基准目录。
- Required checks: capture helper self-test；当前 A21 的 create/title、leader disband、last-member leave
  定向 C2S/S2C + Host + UI 对齐；资料证据等级一致；Release solution build 0 warning/error；固定 PVF 的
  全部适用测试和分层断言；逐项结果核对；staged/unstaged `git diff --check`；完整结果独立审查与审查后
  fresh completion verification。
- Dependencies: TASK-01 runtime verdict 与定向 helper；TASK-02 的 Normal/leader lifecycle；TASK-03 的
  canonical/title/retirement delivery；两份当前 A21 客户端与用户一次最小 UI 操作。

## Detailed Plan

1. 建立 `evidence/task04/`，冻结 accepted baseline、当前 diff、A21 EXE/dstr/PVF/ClientHack 配置哈希。
   重跑 TASK-01 的 5/5 probe、spec-001 的 14/14 probe及 `capture_normal_proxy.py --self-test`；复制而不
   覆盖历史帧，只让本轮 final capture 写入新 JSONL。确认 DNF/Host/中转和标准/后端端口均未占用。
2. 先构建当前 worktree Release solution。用环境覆盖启动隔离 Host：Channel/Game 后端为
   `17001/20010/20011`，Launcher API 为 `18080`，对客户端广告标准 `7001/10010/10011`，PVF 固定到
   `DNFClient-A21-protocol/Script.pvf`，数据库复用已授权的主 Host Release 数据库以保留测试账号。
   Host 原始输出只保留在活动终端/仓库外临时位置；仓库内最终只写经过筛选的 Party 接受/失败摘录。
3. 启动已有定向 TCP 中转，Channel 透明转发且不落盘；Game 只记录 C2S/CMD `0x000C/0x000D` 与 S2C
   `0x0099/0x000B/NOTI 0x0009`。所有自动准备完成后只向用户提出一次操作请求：
   - A、B 启动当前 A21 launcher，进入同一频道；
   - A 从无队状态选择一个能辨认的预设队名创建 Normal，报告创建后的可见队名；
   - B 加入；A 通过游戏内正常“离开队伍”，B 报告队伍是否被解散；
   - B 再正常离开/选择移除最后一人队伍，报告是否回到无队状态；两端随后正常退出。
   若 UI 提供自定义标题，可额外做一次；不要求非队长设置或公开列表，因为它们不属于本 Spec。
4. 用户操作结束后停止本轮 Host/中转，确认无残留进程/监听。解析 final JSONL，按连接、sequence 和时间
   形成可审计序列：create `0x000C` 的 `[special,titleIndex]`、Normal target0；S2C type-0 信息块中的
   titleIndex；leader `0x000D` 后旧 party type-3 clear；last member `0x000D` 后同样只清旧队。若服务端
   曾发送 replacement full-state 而 UI 仍解散，将其记录为反证并撤销该实现。对齐 Host accepted 与用户
   可见结果，并扫描工件不含登录凭证/无关帧。
5. 将本轮环境、逐步操作、最小帧、Host 摘录、UI 观察和置信度写入新的 A21 Normal trace；新增必要的
   `0x000C` create / `NOTI 0x0009` transfer samples，不改写历史样本。更新 `0x000C.md`、
   `NOTI-0x0009.md`、game index 与 catalog：只有最终实现下能由 frame + Host + UI 三方归因的路径标
   `ClientVerified`；自动化或静态推导只标 Confirmed/High，公开目录缺口明确保持 Out。
6. 若实机结果与实现冲突，先保存原始差异与停止条件，再以失败回归固定并做最小修正；需要新增公开目录、
   模式玩法或不明协议形状时返回 controller，不以宽松 decoder 掩盖。修正后必须重跑相关自动化并只重测
   受影响的最小 UI 步骤。
7. 完成 Release solution build 与全部适用测试。设置固定 `PVF_ARCHIVE_PATH`，逐项目运行/核对 TRX 或
   等价机器可读结果，确认每个 `UnitTestResult` 无 Failed；另跑 Application architecture 与必要 Host/
   Networking integration，执行 `git diff --check`。把命令、项目计数、failed/skipped 与环境性排除写入
   `verification/completion-evidence.md`。
8. 汇总本 Task Result 与 `spec-result.md`，进入 configured review stage：独立 reviewer 按
   TaskContract/Spec/Plan/完整 diff、原始帧、协议证据等级和测试结果给出 verdict。修复所有阻断项并复审；
   approved 后按 `verification-before-completion` 运行一次新鲜验证。只有 AC 均有证据且无残留进程/端口，
   才由 controller 完成 spec-002；不在本 Task 合并、推送或清理 worktree。

## Result — Round 1

### Changed

- 以 `live-final-targeted-frames.jsonl`、Host 观察和用户 UI 结果纠正当前 A21 队长离队语义：删除
  replacement party、`PartyRetirement` 与退休通知流，恢复向全部旧成员 clear 后整队删除。
- 保留并实机复核 Normal target/default `0`、C2S `[special,titleIndex]` 到 S2C canonical
  `[titleIndex,special]` 的交换，以及预设/inline 标题编码修复。
- 新增三份最小帧样本和 Normal 实机 trace；同步 `0x000C`、NOTI `0x0009`、game index 与 catalog。

### Why

当前 A21 在明确收到 old clear + replacement full-state 后仍显示“队伍已解散”，直接推翻了按旧版上游
实现自动转移的假设。生命周期以当前客户端与用户观察为准；字段交换、标题和 target 有独立帧证据，
不随该纠正回退。

### Checks

- 队长离队语义 TDD：RED `2 Failed`，GREEN `2 Passed`。
- A21 设置/Normal 探针 `14/14 + 5/5`，捕获器两组 self-test PASS。
- Release build `0 warning / 0 error`；固定 PVF 的串行全套 `3085 Passed / 2 Skipped / 0 Failed`。
- 默认并行全套仅暴露既有 wall-clock 测试的调度抖动；独立 5 次、Application 全项目与串行全套均通过，
  未修改无关测试或生产代码。详见 `evidence/task04/automated-verification.md`。
- `git diff --check` PASS；实机轮次结束后无 Host/DNF/中转/任务端口残留。

### Remaining

- 进入已配置的独立完整结果审查；批准后再运行 fresh completion verification 并封存 spec-002。
- 公开队伍目录与可组合广播作用域由根控制器在 spec-002 接受后建立独立 spec，不混入本结果。
