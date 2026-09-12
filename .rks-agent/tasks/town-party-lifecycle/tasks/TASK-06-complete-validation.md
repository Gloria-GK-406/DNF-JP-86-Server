# TASK-06 — 组队自动化收口与名册后 HP 修复

## Task Unit Contract

- Revision: 6
- Outcome and references: 收口既有组队自动化，并按 Round 5 实机证据先修复名册建立后的初始 HP，覆盖 Spec RQ-001..RQ-014、AC-001..AC-012，Plan T-006；角色详情与最终双客户端验收分别由 TASK-07/TASK-08 接续。
- Owned boundary: 测试/构建结果、任务 worktree 的 Release Host 运行、当前客户端抓包与服务端日志归档，以及只为实测校准所需的本任务代码/测试/协议文档修正；不部署或修改客户端二进制，不合并、不推送。
- Required checks: 先以失败顺序测试证明形成投影缺少名册后的 HP 摘要，再证明接受路径对双方按 `0x0099 -> 0x0008 -> 0x000B -> 0x0009 -> 0x0099` 发布同一快照；运行组队定向测试、Release 构建和逐项 TRX。当前 UI 最终闸转移至 TASK-08，本单元完成不等于 Spec 或根任务完成。
- Dependencies: TASK-05 后的完整实现和资料库。

## Runtime Rework Amendment — Round 3

- Trigger: 2026-09-11 第二轮 A21 双客户端实测中，`[gate]` 私有区域隔离已生效，但接受邀请后两端仍在同一毫秒、同一客户端读取原语 `0x023C5318` 异常退出。
- Classification: `task-rework`。当前 `PartyRosterEncoder` 的 type-0 `NOTI 0x0009` 固定为 60 字节；同版本上游完成版提交 `211129e` 依据 A21 运行时解析器 `0x01172000..0x011726B9` 固定为 65 字节。修正仍属于本 Task 的实机校准边界，不改变 Spec、形成阶段或授权范围。
- Detailed plan: 先把现有逐字节编码测试改为 A21 解析器要求的 65 字节及关键 offset，并确认它因生产代码仍为 60 字节而失败；随后只修正 type-0 roster 编码，使信息块为 `info0 + conditional empty raw-dstr + info1..info11`，保持八个五字节槽、三字节 roster tail 和 `hasExtra=0`；运行聚焦测试与必要回归后重启 Release Host，只复测邀请/接受以确认该单变量根因。
- Boundary: 本轮不修改或部署客户端二进制，不同时引入上游的 formation 重播或 UDP relay。已发现的头像栏退队安全补丁属于 TaskContract Scope Out，待服务端单变量复测通过后另行取得范围授权。
- Required evidence: RED/GREEN 命令与决定性输出；修正后 body 长度 65，名册从 offset 21 开始、leader slot 位于 offset 62、`hasExtra` 位于 offset 64 且为 0；双端新 fault 记录不再出现相同 `ReadU32` 越界签名。

## Task Plan Review — Revision 3

```yaml
kind: rks-review
verdict: changes_required
```

- **审查目标与边界**：Task Unit Contract revision 3 的 `Runtime Rework Amendment — Round 3`；父级为 TaskContract revision 2、Spec revision 2、`mode: task-units` 的 Plan T-006，依赖 TASK-05 已完成。应用 Task Unit 执行前详细计划审查标准；本结论只判断该单变量修订是否足以开始，不判定实现或完整任务完成。
- **已确认的可执行性**：当前生产 `PartyRosterEncoder` 明确分配 60 字节并从 offset 16 写八个五字节槽，现有逐字节测试也锁定 60 字节；上游 `origin/master` 提交 `211129e` 的 `PartyInfoNotiBuilder` 则在 `info0 == 0` 时先写四字节空 raw-dstr，再写 `info1..info11`，随后写八槽、三字节尾和 `hasExtra`，其自测固定 type-0 为 65 字节。故修订提出的 roster 起点 21、leader slot 62、`hasExtra` 64 与 65 字节总长算术一致；先改现有逐字节测试取得 RED、再只改 type-0 编码取得 GREEN，具备真实失败点，也没有越出服务端实机校准授权或混入客户端补丁、formation 重播、UDP relay。
- **阻塞发现 1 — 实机检查缺少“路径确已消费”的正向证据**：当前 `Required evidence` 只要求“不再出现相同 fault”以及可由单元测试证明的 65 字节/offset；邀请未成功接受、`0x0009` 未实际投递或客户端未消费时，同样可能没有新 fault，因而不能证伪原根因。最小修正是在本轮 Release Host 复测中明确要求：服务端日志与双端抓包能关联到同一次有效接受，两个接收方都实际收到 65 字节 body 的 `NOTI 0x0009`，随后两个进程继续存活且双方显示同一两人队伍；再以无新增同签名 fault 作为补充证据。
- **阻塞发现 2 — 协议资料库会被本改动直接写旧**：当前 `protocol/game/server-to-client/NOTI-0x0009.md` 人工区仍明确记录 type-0 固定 60 字节、名册从 offset 16 开始、尾部位于 56，并保留“客户端接受 60 字节”的待验证项；详细计划没有安排随编码改为 65 字节而同步这些字段、实现映射和待验证表述。这会直接违反 RQ-012/SC-6 及 Task Unit 的校准边界。最小修正是把该页纳入 GREEN 后的同轮更新：改成 65/21/61..64 的当前实现形状，记录 `211129e` 与本轮实机证据来源，并在双端正向验证前保持 `Implemented`/待实机验证，不提前标记 `ClientVerified`。

## Runtime Rework Amendment — Round 4

- Revision response: 接受 Revision 3 的两项发现。本轮 GREEN 后同步更新 `protocol/game/server-to-client/NOTI-0x0009.md`：将 type-0 当前形状改为 65 字节、信息块/名册/尾部 offset 改为 5..20/21..60/61..64，记录上游 `211129e` 的 A21 运行时解析器证据；双端正向实测前保持 `Implemented` 与待验证状态。
- Positive live oracle: Release Host 复测必须把同一次有效 `RESPONSE_PEER` 与两个接收方的 S2C `NOTI 0x0009` 关联起来，证明两端实际收到并消费 65 字节 body；随后两个 DNF 进程继续存活且双方 UI 显示同一两人队伍。fault 日志无新增 `0x023C5318` 同签名只作补充反证，不能单独判定通过。
- Remaining method and boundary: 其余 RED→GREEN 方法、关键 offsets、单变量服务端范围及客户端二进制/formation 重播/UDP relay 排除项沿用 Round 3，不变。

## Task Plan Review — Revision 4

```yaml
kind: rks-review
verdict: approved
```

- **审查目标与边界**：仅复审 Task Unit Contract revision 4 的 `Runtime Rework Amendment — Round 4` 是否充分闭合 Revision 3 的两项 `changes_required`；父级依据为 TaskContract revision 2、Spec revision 2、`mode: task-units` 的 Plan T-006，以及 `sdd-execution.yaml` 当前 implementation cursor。本结论只批准该修订进入执行，不证明实现、实机结果或完整任务完成。
- **Finding 1 已闭合 — 正向实机 oracle**：Round 4 不再以“无新增 fault”作为单独通过条件，而是要求把同一次有效 `RESPONSE_PEER` 与两个接收方的 S2C `NOTI 0x0009` 关联，证明双方实际收到并消费 65 字节 body，随后两个进程继续存活且 UI 显示同一两人队伍；`0x023C5318` 无新增同签名仅作为补充反证。因此未投递、未消费或接受未成立均不能产生伪通过。
- **Finding 2 已闭合 — 协议资料同步**：Round 4 明确把 `protocol/game/server-to-client/NOTI-0x0009.md` 纳入同轮 GREEN，要求同步 65 字节总长、`5..20/21..60/61..64` 区间、上游 `211129e` 证据，并在双端正向实测前保持 `Implemented`/待验证，足以避免编码与资料库继续停留在 60 字节旧形状或提前宣称 `ClientVerified`。
- **继续执行条件已满足**：TaskContract revision 2 的 Scope In 明确授权实机校准与协议资料同步，Scope Out 继续排除客户端二进制；Spec revision 2 的 RQ-007/RQ-008/RQ-012、AC-004/AC-008/AC-009 与本轮修订一致；父 Plan 为 `task-units` 且 T-006 正是完整自动化、Release Host 实测及校准边界；`sdd-execution.yaml` 位于 `current_stage: implementation`、`state: in_progress`，TASK-01..05 为 `done`、唯一当前单元 TASK-06 为 `in_progress`。依赖、授权和 cursor 均允许按该详细计划继续，无剩余阻塞发现。

## Task Unit Execution Result — Runtime Rework Round 4

```yaml
kind: rks-task-unit-result
status: ready_for_review
```

- **RED**：先把 `PartyNotificationTests.Full_roster_notification_has_a_stable_byte_exact_layout` 的期望改为 65 字节，并固定名册起点 21、leader slot 62、`hasExtra` 64；运行 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Full_roster_notification_has_a_stable_byte_exact_layout"`，得到 `Expected: 65 / Actual: 60`，证明测试能捕获旧编码缺陷。
- **GREEN 实现**：`PartyRosterEncoder` 的 type-0 body 改为 65 字节；offset 5 写 `info0=0`，offset 6..9 写空 raw-dstr 长度，offset 10..20 写 `info1..info11`，八槽从 offset 21 开始，三字节 roster tail 位于 61..63，offset 64 写 `hasExtra=0`。没有改动客户端二进制、formation 重播或 UDP relay。
- **GREEN 测试**：同一聚焦测试 `1/1` 通过；`dotnet test ... --filter "FullyQualifiedName~Party"` 为 `44/44`；带 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 的完整 `Dfo.Server.Protocol.Tests` 为 `764/764`。
- **资料库同步**：`protocol/game/server-to-client/NOTI-0x0009.md` 已同步 65 字节和 `5..20/21..60/61..64` 布局，记录双端旧崩溃、上游 `211129e` 解析器证据，并在正向实测前维持 `Implemented`、不标记 `ClientVerified`。
- **Release 构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 通过，`0` 警告、`0` 错误。
- **全量 TRX**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "trx;LogFilePrefix=task06-final" --results-directory TestResults/task06-final` 共生成 9 份 TRX；逐条解析得到 3034 条 `UnitTestResult`，其中 3031 Passed、1 Failed、2 NotExecuted（两个缺失 PVF 的反向测试因本轮显式提供 PVF 而按设计 skip）。唯一失败是 `LauncherAuthenticationEndToEndTests.HttpChannelAndGame_UseOnlyTheAdvertisedTuplesAndPreserveTheAccountIdentity` 在 `EndToEndHostHarness.DisposeAsync` 删除 `whole-route.db-shm` 时遇到文件占用，业务断言没有失败。
- **失败分类与重跑**：该唯一失败按系统化调试流程归为并行整解执行下的 SQLite 临时文件清理竞争；不改代码，单独重跑失败用例为 `1/1` 通过，随后重跑整个 `Dfo.Server.Host.Tests` 为 `17/17` 通过。两份重跑 TRX 共 18 条结果，逐项为 18 Passed、0 Failed、0 Other。
- **静态完整性**：`git diff --check` 通过。当前结果只证明编码修正与自动化门；Revision 4 的双客户端正向 live oracle 尚未执行，因此 TASK-06 和 Spec 仍不得判定完成。

## Task Unit Result Review — Runtime Rework Round 4

```yaml
kind: rks-review
verdict: approved
```

- **审查目标、基线与范围**：独立 quick-review `Task Unit Execution Result — Runtime Rework Round 4`；治理依据为 TaskContract revision 2、Spec revision 2、approved Task Unit Plan revision 4 与 Plan T-006。owned-change identity 为基线 `355aaf5cb6714044b4781185ec3118007bc97bec` 上的当前 task worktree diff；本结论只批准本轮“type-0 roster 编码修正 + 自动化证据”进入 Revision 4 的双客户端 live oracle，不批准 TASK-06、Spec 或完整 TaskContract 完成。应用了 Task Unit、Code 与 Deliverable quick-review 标准。
- **65 字节修正成立**：同版本上游 `ServerS4A21` 提交 `211129e` 的 `PartyInfoNotiBuilder` 明确引用 A21 运行时 parser `0x01172000..0x011726B9`，并按 `info0`、`info0==0` 时四字节空 raw-dstr、`info1..info11`、八个五字节槽、三字节 roster tail、`hasExtra` 的顺序编码；其自测固定 type-0 为 65 字节及 offsets 21/61..64。当前 `PartyRosterEncoder` 与此逐项一致：5..20 为 16 字节信息块，21..60 为 40 字节名册，61..63 为 tail，64 为 `hasExtra=0`；leader slot 写在 62。现有 `PartyNotificationTests.Full_roster_is_one_a21_block_with_eight_slots_and_no_extra_records` 同时做完整 65 字节向量比较，并单独钉住 offset 21、62、64，能发现原 60 字节/offset 16 形状及关键错位。
- **协议资料与范围一致**：`protocol/game/server-to-client/NOTI-0x0009.md` 的 A21 人工区已把非空 refresh 标为固定 65 字节，记录 5..20/21..60/61..64、`211129e` 与旧双端越界症状，并保持 `Implemented`、显式列出待双客户端正向验证，未提前写成 `ClientVerified`。当前变更清单没有 ClientHack/客户端二进制、formation 重播或 UDP relay 路径，未发现本轮明显局部代码缺陷或范围越界。
- **全量红项不阻止进入 live oracle**：9 份全量 TRX 的 3034 条结果可复核为 3031 Passed、1 Failed、2 NotExecuted。唯一 Failed 的业务用例已经执行到清理阶段，异常为 `EndToEndHostHarness.DisposeAsync` 删除 `whole-route.db-shm` 时的文件占用，栈根只在递归删除；本任务 diff 未改 `Dfo.Server.Host.Tests` 或 Host harness。随后同一用例 `1/1` 通过、整个 `Dfo.Server.Host.Tests` `17/17` 通过，两份重跑 TRX 合计 18 Passed、0 Failed、0 Other，足以把该次失败归为一次性 SQLite 临时文件 cleanup 竞争，而非 roster 修正或业务回归。
- **非阻塞记录差异**：执行结果的 RED 命令仍写旧测试名 `Full_roster_notification_has_a_stable_byte_exact_layout`，当前源码与 GREEN TRX 中的实际名称是 `Full_roster_is_one_a21_block_with_eight_slots_and_no_extra_records`。这不影响现有逐字节判据或进入实机的结论，但该名称不能作为当前可重放命令；后续若补写 RED 原始日志，应按当时二进制/源码身份说明重命名关系。
- **剩余前提**：本批准不把 Spec AC-009/AC-010 判为满足。Revision 4 仍必须完成同一次有效 `RESPONSE_PEER` 到双方收到并消费 65B `NOTI 0x0009` 的正向关联，确认两进程存活且双方 UI 为同一两人队伍；随后还要完成普通成员离队。全量中的 1 Failed 与 2 NotExecuted 也意味着当前批次本身不能单独充当最终“所有 UnitTestResult 通过、无跳过”的完成证据，最终验收应另给满足该门槛的证据。

## Runtime Rework Amendment — Round 5

- **Trigger and disposition**：Revision 4 双端运行已经越过 65B 名册解析，随后双方持续发送空 body `CMD 0x00A6 CALL_PARTY_MEMBER_REALTIME_INFO`，当前服务端均记录为 `Unhandled`，队伍头像血条为空；邀请窗口也暴露 `NOTI 0x0007` 尾部仍为占位零。用户决定“只传 HP”，并授权已实现的邀请值先传、未实现值继续为 0；这是同一 live oracle 内可局部修正的 `task-rework`，不改变父 Spec 的组队投影边界。
- **Wire correction**：新增严格空 body 的 `0x00A6` decoder/handler；handler 只从当前精确会话经 Command 查询其当前队伍，再通过 Notification 向该请求会话回复 `NOTI 0x0099`，不增加 MP 字段。`0x0099` 的每成员 HP 字节取当前 selected-session runtime 中已有的 `CurrentHpMpPercent` 并限制到 `0..100`；不存在可用当前状态时取 0，help-abuse 仍为 0。
- **Invite projection**：`NOTI 0x0007 type=0` 固定 13B 按 A21 handler `0x0118C640` 与邀请窗口 dstr `0x55C2` 的已闭合映射写入：offset 3 的 `i32` 为复活币数量，offset 7 的 `u16` 为剩余疲劳，offset 9 的 `u16` 为虚弱/stamina，offset 11 的 `u16` 为抗魔。复活币从邀请者当前角色主背包虚拟槽 1 / item id 1 的服务端库存投影读取；疲劳从 `ICharacterFatigueDomainService.BalanceOfAsync(...).Remaining` 读取，当前实现会得到 188。stamina 与抗魔没有完整运行时业务写入/派生模型，按用户决定继续写 0。C2S `0x000A` 的 `peerValue` 保留为客户端上报证据但不作为服务端权威显示来源。
- **Architecture and snapshot boundary**：协议 handler 只解码、调用 Command 和触发 Notification；Application 的组队成员状态查询服务组合库存、疲劳和 selected-session runtime。邀请显示值在邀请 Command 内形成不可变快照；队伍实时 HP 在每次领域 snapshot 生成时捕获，保证同一 `0x0099` 的所有接收方看到同一组成员值。Application 不引用 Infrastructure，队伍聚合不复制或长期拥有库存/疲劳业务状态。
- **TDD and docs**：先以字节级邀请/实时通知测试、`0x00A6` 严格解码与 request-only 投递测试、Application 状态投影测试取得针对缺失行为的 RED；再做最小实现并跑 Party 聚焦回归、Application/Protocol 项目测试、Release build 与最终无跳过全量 TRX。同步 `0x00A6`、`NOTI 0x0007`、`NOTI 0x0099`、protocol index/catalog 的人工实现与 live 证据；在双端实际消费前仍保持 `Implemented`，不得提前标 `ClientVerified`。
- **Remaining live oracle**：重启本 worktree Release Host 后，同一次测试必须关联客户端 `0x00A6` 与定向 `0x0099`，观察双方 HP 显示；邀请 body 必须显示与服务端库存一致的复活币和当前 188 疲劳、另外两项为 0；随后仍需完成 Revision 4 的两端存活/同一队伍与普通成员离队验收。

## Task Unit Execution Result — Runtime Rework Round 5

```yaml
kind: rks-task-unit-result
status: ready_for_live_oracle
```

- **RED/GREEN（用户选择的展示值）**：Protocol Party 聚焦测试最初因缺少
  `PartyInviteDisplayState`、`IPartyMemberStatusDomainService` 及旧邀请接口而编译失败；实现后
  Protocol Party `48/48`、Application Party `21/21` 通过。逐字节断言固定邀请 body 为
  `uid + type0 + reviveCoins(i32) + remainingFatigue(u16) + stamina(u16) + antiMagic(u16)`，并固定
  `0x0099` 每成员 HP 字节来自 snapshot，不增加 MP。
- **实现边界**：新增严格空 body 的 C2S `0x00A6` decoder/handler、当前队伍 Query Command 和
  单连接 `0x0099` 投递；邀请复活币读取角色虚拟资源 slot 1/item 1，疲劳读取领域服务 Remaining，
  stamina/抗魔继续为 0；HP 读取精确 selected-session runtime 的 `CurrentHpMpPercent` 并钳制
  `0..100`。C2S `0x000A.peerValue` 只记录为客户端上报证据，不作为权威展示值。
- **系统化调试与回归**：首次完整测试时 Networking 13 项在连接阶段被拒绝；详细单用例日志证明
  TouchSocket listener 启动时急切解析新增状态服务，继而在无 Persistence 的 Networking 测试宿主中
  缺少 `ICharacterTable`。将库存服务解析延迟到真实邀请 Command 调用后，决定性
  `ChannelConnectIntegrationTests` 从失败恢复为 `1/1`，完整 Networking 为 `62/62`。这不改变
  生产 Host 的权威库存来源，也没有为无持久化宿主发明 fallback 数据。
- **Release 构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 通过，
  `0` 警告、`0` 错误；`git diff --check` 通过。
- **最终无跳过 TRX**：为同时覆盖“PVF 存在”和两个“PVF 缺失”反向用例，将 Infrastructure
  按环境互斥条件拆成 `301 + 2` 两份，其余八个测试项目各完整运行。`TestResults/task06-round5-final2`
  共 10 份 TRX；逐条解析 3038 个 `UnitTestResult`，得到 3038 Passed、0 Failed、0 Other。
- **资料库同步**：`0x00A6.md` 已记录 2026-09-11 空 body 实机观察与定向 `0x0099` 语义；
  `NOTI-0x0007.md` 已记录四个邀请展示字段的服务端来源；`NOTI-0x0099.md` 已改为当前 HP 投影并
  记录 `0x00A6` 请求响应关系；catalog/index 已同步。双客户端消费前均保持 `Implemented`、待实机。
- **剩余条件**：本结果只允许进入 Round 5 live oracle；在用户确认邀请值、双方 HP、两端存活、
  同一两人队伍以及普通成员离队前，TASK-06、AC-009/AC-010 和 Spec 仍不得判定完成。

## Verification — Runtime Rework Round 5 Readiness

```yaml
kind: rks-verification
decision: manual_required
```

- **Claim**：当前 Round 5 代码、文档与 Release Host 已具备继续双客户端 live oracle 的条件；
  不声称 TASK-06、Spec 或根 TaskContract 已完成。
- **自动化条件**：当前源码 Release build 为 0 warning/0 error；
  `TestResults/task06-round5-final2` 的 10 份 TRX 逐条解析为 3038 Passed、0 Failed、0 Other，
  其中 PVF present/missing 互斥用例分别运行且均未跳过；`git diff --check` 通过。满足 AC-010/SC-7
  的自动化执行部分。
- **运行就绪条件**：worktree Release Host 已成功加载指定 A21 `Script.pvf` 与根仓库现有数据库；
  PID 31504 当前监听 `0.0.0.0:7001`、`0.0.0.0:10010`、`0.0.0.0:10011` 和
  `127.0.0.1:18080`。

### Unverified Conditions

- 邀请 UI 是否显示库存一致的复活币、当前 188 疲劳、stamina 0、抗魔 0。
- 双方客户端是否各自以 C2S `0x00A6` 触发定向 `0x0099`，并显示成员 HP。
- 接受后两个进程是否继续存活、双方是否显示同一两人队伍，以及普通成员主动离队后 UI 是否一致。
- 上述当前 A21 UI/抓包/服务端日志尚未形成 EV-002，因此 AC-009、SC-1/SC-2/SC-3 与 TASK-06
  仍需人工实测后才能完成。

## Runtime Rework Amendment — Round 6

- Trigger: 2026-09-12 Round 5 实机中，65 字节名册已使双方稳定形成队伍，但初始头像 HP 为空；服务端形成顺序把 `NOTI 0x0099` 发在完整 `NOTI 0x0009` 之前，且只有邀请者随后主动发送空 body `0x00A6`，因此不能依赖双方客户端各自补查。
- Classification: `task-rework`。TaskContract/Spec 已扩到 revision 3，用户明确要求先修 HP、再实现角色详情。最终实机闸拆到 TASK-08，本 Task Unit 只完成 HP 的 TDD、实现、资料同步与自动化子结果。
- Detailed plan: 先把 `Formation_is_published_in_cross_recipient_phases` 改为要求第五阶段 `PartyRealtimeChanged`，确认当前四阶段实现失败；随后在 `PartyNotificationDelivery` 仅对带 `Acceptance` 的形成/新增成员计划，于 `PartyRosterChanged.Refresh` 完成后向相同 member audience 发布同一 snapshot 的第二次 realtime 通知。普通离队刷新和显式 `0x00A6` 定向响应保持原语义。
- Required evidence: RED 输出明确显示缺少第五次发布；GREEN 证明五阶段类型、audience 与 snapshot identity；组队协议定向套件、Release 构建和新鲜 TRX 逐项通过；`NOTI 0x0099` 与形成序列文档同步但在 TASK-08 实机前保持 `Implemented`。

## Task Unit Execution Result — Runtime Rework Round 6

- **RED**：将形成顺序测试扩为第五阶段后，原生产实现按预期失败：`Expected count: 5`、`Actual count: 4`，现有四次发布依次为 realtime、acceptance、endpoints、roster。
- **GREEN**：`PartyNotificationDelivery` 仅在 `plan.Acceptance` 存在时，于完整 roster 后向相同 member audience 发布同一 snapshot 的第二次 `PartyRealtimeChanged`。这没有改变普通离队刷新、显式 `0x00A6` 定向响应或领域状态。
- **定向回归**：Debug 组队协议套件 `46/46` 通过；Release 新鲜 TRX `TestResults/task06-round6/task06-round6-party.trx` 逐条解析为 46 Passed、0 Failed、0 Other。
- **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-restore` 成功，0 warning、0 error。
- **资料同步**：`NOTI-0x0099.md` 与 `NOTI-0x0009.md` 已记录 `... -> 0x0009 -> 0x0099`、同 snapshot 和名册后 HP 消费理由；在 TASK-08 双客户端确认前保持 `Implemented`。
- **Result**：TASK-06 的 HP 实现与自动化子结果完成，允许按用户指定顺序进入 TASK-07；这不满足 AC-009，也不代表 Spec/根任务完成。
