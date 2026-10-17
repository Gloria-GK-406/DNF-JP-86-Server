# TASK-006 — 03D0 状态直接由阶段投影

## Task Unit Contract

- Revision: 1
- Outcome and references：对应 Spec RQ-002 和 AC-002。03D0 的 state 字节直接由快照中的阶段投影得出，不再由 `IsMarried` / `IsCeremonyReserved` 两个布尔值拼出来。阶段到线上字节的对应关系属于协议投影，归 Protocol 所有。这一缺口由 TASK-005 核对代码时发现：`MarriageStageRules.WireState` 没有任何生产调用方。
- Owned boundary：
  - `MarriageEngagementSnapshot`：新增 `Stage`，两个布尔值改为由它计算得出；
  - `MarriageDomainService.SnapshotAsync`；
  - `MarriageStageRules`：移除 `WireState`；
  - `MarriageEngagementPayload`：Protocol 侧的投影；
  - 测试接线：把 Application 中 `WireState` 的穷举测试移到 Protocol.Tests；codec 测试的快照构造改为设置 `Stage`。
  - 线上字节不变。
- Required checks：
  - 构建 0 警告；
  - 全量测试通过；
  - 表征测试 `Wedding_info_state_byte_follows_each_relationship_stage` 不改断言，照样通过。

## Detailed Plan

1. 修改快照、实体、Protocol 投影这三处。投影的映射表沿用 Spec Definitions；没有关系或未订婚时，`IsEngaged` 为 false，整帧仍然全为 0。
2. 移除 `MarriageStageRules.WireState`。它的穷举测试改为针对 Protocol 的映射；codec 测试的两处快照改为设置 `Stage`，期望字节不变。
3. 跑构建和全量测试。

## Result

### Changed

- `MarriageEngagementSnapshot`：新增 `Stage`，默认值为 Engaged；`IsMarried` 和 `IsCeremonyReserved` 改为由 `Stage` 派生的只读属性。
- `MarriageDomainService.SnapshotAsync`：直接写入 `Stage = relation.Stage`。
- `MarriageStageRules`：移除 `WireState`。
- `MarriageEngagementPayload`：新增 Protocol 侧的 `WireState(MarriageStage?)`，`Info` 改用它计算 state 字节。
- 测试：
  - `Wire_state_projects_each_stage` 从 `MarriageRelationLedgerTests` 移到 `MarriageProposalCodecTests`，7 组数据不变，改为调用 Protocol 的映射；
  - codec 测试中两处快照构造由 `IsMarried = true` / `IsCeremonyReserved = true` 改为设置 `Stage`，期望字节不变。

### Why

- **阶段到 03D0 字节的映射放在 Protocol**：按 AGENTS 的分层，线上取值属于协议投影。
- **`Stage` 默认为 Engaged**：现有 codec 测试构造快照时只给关系和对方资料，原语义就是「已订婚」（state 1）；生产路径总是显式写入阶段。空快照的 `IsEngaged` 为 false，整帧仍然全为 0。

### Checks

- **构建**：0 警告，0 错误。
- **全量测试**（日志在会话 scratchpad 的 `mda-task006-full2.log`）：
  - 第一轮：Protocol 有 2 条失败。原因是 `Stage` 当时为可空、默认 null，按上面的 Why 修正后消失。
  - 第二轮：只有 Host.Tests 的 `GmOfficialMailHttpTests.UnknownPostDurableCommitFailureReturnsUnknownAndOriginalKeyRecoversCommittedReceipt` 失败。错误是端口 `127.0.0.1:7544` 已被占用（`AddressInUseException`），与婚姻无关，属于 AGENTS 所说的「拉真实进程或 socket 的用例在满载全量下会假红」。
  - 单独复跑 Host.Tests：总数 159，通过 149，跳过 10，失败 0。
  - 其余项目通过：Protocol 1588 条中通过 1528，Application 4009 条中通过 3583，其它项目与前几轮相同。
- **表征测试**：`Wedding_info_state_byte_follows_each_relationship_stage` 断言未改，通过。
