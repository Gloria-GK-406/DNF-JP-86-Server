---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: A21 Cycle 队伍模式闭环

## Outcome

交付当前 A21 可消费的 Cycle 队伍设置与领域行为。结果必须从客户端 Cycle 选择、区域来源、无区域分支、
`0x000C` 构包和 S2C 设置消费共同证明 `ModeTargetRaw` 及关联字段的含义和值域；只把已证规则纳入现有
集中规则/领域链，并以真实创建、编辑和至少一个 Cycle 特有操作闭合行为。未知字节保持 opaque，不复用
Story 或 Normal 的 target 语义。

## Scope Boundary

包含 Cycle 的固定上游对照、当前 A21 静态构包/消费、原生帧、规则/领域/协议实现、通知顺序、成员与
离队组合、协议资料和客户端验收。不包含已封存 Story、公共目录 Issue #20、独立快速匹配、Cycle 副本
战斗/奖励。若客户端证明快速匹配是 Cycle 基本创建/进入不可分割的前置，停止本 Spec 并交父控制器。

## Known Baseline

- `PartyCategory.Cycle=1`；设置为不可变 12-byte canonical block，类别在 `[9]`，`ModeTargetRaw` 为
  `[10..11]` little-endian `u16`。
- C2S `0x000C` 已严格区分 direct 与 inline-title，并通过 Command/领域服务原子创建或更新。
- canonical `[1]` 按既有产品策略规范化为 0；容量 1..4 且不得低于成员数；只有当前精确会话队长可编辑。
- 规则服务只证明 Normal target=0；Cycle target 当前为透明保存，不能把“可保存”误述为全 `u16` 合法。
- 设置通知按 party generation/revision 串行并重查精确 audience；Normal 与频道隔离是回归基线。
- 旧静态总览证明 category 1 进入循环区域显示路径并存在 dstr 48605“没有循环地下城区域”，但没有闭合
  区域表、默认/sentinel 或原生创建帧。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 从固定上游与当前 A21 Cycle UI/构包/读取链确定 category、区域 target/default、无区域状态、标题/容量及关联字段；冲突明确裁决。 |
| RQ-002 | Cycle 无队创建与已有队编辑只接受已证明合法组合；畸形、尾随、非法 target/category/capacity、非队长、旧会话和竞态均无状态或通知副作用。 |
| RQ-003 | 合法 Cycle 设置通过现有 Command → 领域/规则 → Notification 边界原子生效；投影保持 canonical 字节、title、generation/revision 和精确 audience。 |
| RQ-004 | Cycle 下邀请、接受、成员变化、普通成员离队、队长解散与会话清理确定；只新增直接证据证明的 Cycle 特有转换。 |
| RQ-005 | 当前 A21 原生 Cycle create、edit 和至少一个特有操作具有可归因的 C2S/S2C、服务端结果与 UI 行为；静态、自动化、实机等级一致。 |
| RQ-006 | Normal、设置基础、队伍生命周期和频道隔离无回退；协议专页、样本、trace、index/catalog 与实现一致。 |

## State and Projection Invariants

- 拒绝路径保持同一 party generation/revision、设置、标题、成员、邀请与通知向量。
- 创建产生新 generation；同队编辑只增加 revision，不改变 partyId、generation、成员或邀请所有权。
- 通知中的 Cycle 设置来自成功操作的不可变 snapshot，不从稍后的可变 registry 重组旧结果。
- 迟到旧 revision、旧 selected-session generation 或已离队成员不得收到/覆盖较新 Cycle 状态。
- Cycle 目标有效性由集中规则服务决定；codec 只负责严格字节形状，handler 不复制领域规则。
- 未闭合的 raw 字节无损往返；不得用 Cycle 推断修改 Story 或 Normal 设置。

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | 字段表逐项给出 Cycle 相关 offset/宽度、构包来源、消费者/约束和置信度；至少一份原生 create 帧确定 category、target/default 与无区域裁决，未知字段明确保留。 |
| AC-002 | RQ-002 | codec 与领域/handler 测试覆盖合法 create/edit 以及截断、尾随、非法 target/category/capacity、非队长、旧会话和竞态；拒绝均证明状态/通知未变。 |
| AC-003 | RQ-003 | 自动化证明 canonical 字节、title、partyId、generation/revision、audience 与乱序通知；Application→Infrastructure 禁止引用保持通过。 |
| AC-004 | RQ-004 | Cycle 设置下邀请/接受/成员变化/普通成员离队/队长解散/清理回归通过；新增特有转换有正反与失败原子性测试。 |
| AC-005 | RQ-005 | 同一固定客户端记录原生 create、edit、至少一个 Cycle 特有操作的原始帧、Host 判定、对应 S2C 与 UI 结果；不能归因不得升级 `ClientVerified`。 |
| AC-006 | RQ-006 | 聚焦与全量适用测试逐项 Passed，Release build 0 warning/error，协议资料一致；完整结果独立审查 approved，审查后新鲜验证覆盖 AC-001..006。 |

## Evidence and Stopping Criteria

- `Confirmed static`：固定 A21 映像上的完整构包/读取/分支或资源引用可机械复现。
- `Confirmed runtime`：同一 exact session 中有原始输入、服务端结果、直接输出和 UI 状态。
- `Implemented`：服务端按已证边界实现且自动化通过，但尚缺客户端归因。
- `Hypothesis`：只有命名、邻接或单侧推断；不得进入拒绝规则。

若 Cycle UI 只通向快速匹配、target 是无法安全约束的危险索引、无区域状态与合法 target 无法区分、固定
客户端身份失配，或授权内手段无法取得原生 Cycle create/edit，则停止产品收窄并形成 blocker；不得用
测试构造值替代真实语义。

## Success Evidence

- EV-001（AC-001）：固定输入、上游/客户端字段图、无区域裁决和原生 Cycle create 帧。
- EV-002（AC-002/003/004）：RED/GREEN 规则、领域、handler、通知顺序与生命周期回归。
- EV-003（AC-005）：Cycle create/edit/特有操作的定向帧、Host 与 UI 时间线。
- EV-004（AC-006）：协议一致性、完整审查和审查后 completion verification。
