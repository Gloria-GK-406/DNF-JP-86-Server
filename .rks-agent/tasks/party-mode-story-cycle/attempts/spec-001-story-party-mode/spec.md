---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: A21 Story 队伍模式闭环

## Outcome

交付当前 A21 可消费的 Story 队伍设置与领域行为。结果必须从客户端的 Story 选择和 `0x000C` 构包证明
`ModeTargetRaw` 及相关设置字节的含义与合法范围，把已证规则收敛进现有集中规则服务，并以真实创建、
编辑和至少一个 Story 特有操作闭合客户端行为；未知内容保持 opaque，不用 Normal 或快速匹配语义填空。

## Scope Boundary

包含 Story 的上游对照、当前 A21 静态构包/消费、原生帧、规则/领域/协议实现、通知顺序、成员与离队组合、
协议资料和客户端验收。不包含 Cycle、公共目录 Issue #20、独立快速匹配队列、Story 副本战斗/任务/奖励。
若客户端证明快速匹配是 Story 基本创建/进入不可分割的前置，停止本 Spec 并交父控制器，而非扩大实现。

## Known Baseline

- `PartyCategory.Story=0`；设置为不可变 12-byte canonical block，类别在 `[9]`，`ModeTargetRaw` 为
  `[10..11]` little-endian `u16`。
- C2S `0x000C` 已严格区分 direct 与 inline-title 形状，并通过 Command/领域服务原子创建或更新。
- canonical `[1]` 会规范化为 0；容量必须为 1..4 且不得低于成员数；只有当前精确会话的队长可编辑。
- `PartyModeRuleService` 目前只证明 Normal target 必须为 0；Story/Cycle target 仍开放保存，不能把开放
  状态误述为已确认全范围合法。
- 设置通知按 party generation/revision 串行并重查精确成员 audience；既有 Normal 和频道隔离为回归基线。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 从固定上游来源和当前 A21 Story UI/构包/读取链确定 Story category、target/default、标题/容量及其他相关设置字节的含义、来源和约束；冲突需明确裁决。 |
| RQ-002 | Story 的无队创建与既有队编辑只接受已证明合法的设置组合；畸形、尾随、非法 target/category/capacity、非队长、旧会话和竞态均无状态或通知副作用。 |
| RQ-003 | 合法 Story 设置通过现有 Command → 领域/规则 → Notification 边界原子生效；投影保持正确 canonical 字节、title、generation/revision 和精确成员 audience。 |
| RQ-004 | Story 下邀请、接受、成员变化、普通成员离队、队长解散与会话清理保持确定性；只新增被证据证明的 Story 特有状态转换。 |
| RQ-005 | 当前 A21 对原生 Story 创建、编辑及至少一个模式特有操作给出可归因的 C2S/S2C、服务端结果和 UI 行为；静态、自动化与实机证据分级一致。 |
| RQ-006 | 既有 Normal、设置基础、队伍生命周期和频道隔离无回退；协议专页、样本、trace、index/catalog 与实现一致。 |

## State and Projection Invariants

- 拒绝路径保持同一 party generation/revision、设置、标题、成员、邀请和通知向量。
- 创建产生一个新 generation；同队设置编辑只增加 revision，不改变 partyId、generation、成员或邀请所有权。
- 通知中的 Story 设置来自成功操作返回的不可变 snapshot，不从稍后的可变 registry 重组旧结果。
- 迟到的旧 revision、旧 selected-session generation 或已离队成员不得收到/覆盖较新 Story 状态。
- Story 特有目标的有效性由规则服务集中决定；codec 只负责严格字节形状，handler 不复制领域规则。

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 证据表逐项给出 Story 相关 wire offset/宽度、构包来源、消费者/约束与置信度；至少一份原生创建帧确定 category 和 target/default，未闭合字段明确保持 unknown。 |
| AC-002 | RQ-002 | 字节级 codec 与领域/handler 测试覆盖合法创建/编辑以及截断、尾随、非法 target/category/capacity、非队长、旧会话和竞态；所有拒绝均证明状态与通知未变。 |
| AC-003 | RQ-003 | 自动化证明 canonical 字节、title、partyId、generation/revision、成员 audience 和乱序通知行为；Application 到 Infrastructure 的禁止引用断言保持通过。 |
| AC-004 | RQ-004 | Story 设置下的邀请/接受/成员变化/普通成员离队/队长解散/清理回归通过；若新增特有转换，其正反路径及失败原子性有测试。 |
| AC-005 | RQ-005 | 同一固定客户端身份下记录原生 Story create、edit 和至少一个 Story 特有操作的原始帧、Host 接受/拒绝、对应 S2C 与用户可见结果；不能归因时不得升级 `ClientVerified`。 |
| AC-006 | RQ-006 | 聚焦与全量适用测试逐项 Passed，Release build 0 warning/error，协议资料一致性检查通过；完整结果独立审查 approved，审查后新鲜验证逐项覆盖 AC-001..006。 |

## Evidence and Stopping Criteria

- `Confirmed static`：当前固定 A21 映像上的完整构包/读取/分支或资源引用可机械复现。
- `Confirmed runtime`：同一 exact session 中有原始输入、服务端结果、直接相关输出和 UI 状态。
- `Implemented`：服务端按已证边界实现且自动化通过，但尚缺当前客户端归因。
- `Hypothesis`：只有命名、相邻字段或单侧推断；不得进入规则拒绝边界。

若 Story UI 只通向快速匹配、target 进入无法静态约束的危险索引、固定客户端身份失配，或授权内手段无法
取得原生 Story 创建/编辑，则停止后续产品收窄并形成证据 blocker；不得以测试构造值代替真实语义。

## Success Evidence

- EV-001（AC-001）：固定输入清单、上游/客户端静态字段表和原生 Story 创建帧。
- EV-002（AC-002/003/004）：RED/GREEN 规则、领域、handler、通知顺序与既有生命周期回归。
- EV-003（AC-005）：Story create/edit/特有操作的定向帧、Host 结果与 UI 时间线。
- EV-004（AC-006）：协议一致性结果、完整审查和审查后 completion verification。
