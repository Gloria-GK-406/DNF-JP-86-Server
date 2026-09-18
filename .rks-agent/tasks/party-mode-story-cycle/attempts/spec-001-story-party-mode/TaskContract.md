---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-3
  - SC-4
depends_on: []
---

# TaskContract: A21 Story 队伍模式闭环

## Context

### Source Request

根任务根据用户“队伍列表后面再处理，我们继续推进功能开发”的新优先级，从已接受的队伍设置、Normal
模式和频道隔离基线继续 Story，再推进 Cycle。本子规格只交付 Story 的独立可验收结果。

### Current Situation

接受基线 `9a3a0b492da11f48525d1225d02513e0dd1cd458` 已能保存 byte-exact 12 字节设置，并确认
`PartyCategory.Story=0`、`Cycle=1`、`Normal=2`。规则服务目前只对 Normal 强制 target=0；Story 的
`ModeTargetRaw` 合法范围、默认来源、创建/编辑解释和模式特有生命周期仍未证明。已有 `0x000C` strict
decoder、原子设置更新、revision/generation 通知和 Normal 回归可复用，但不能把 Normal target 规则套给
Story。

## Goal

查明并实现当前 A21 Story 队伍的设置字段、目标/default、创建/编辑约束及与该模式直接相关的生命周期，
形成真实 Story 创建、编辑和至少一个模式特有行为的客户端可观察闭环，同时保持既有队伍与频道行为不回退。

## Scope

### In

- 先复核上游 Story 行为，再静态定位当前 A21 的 Story 选择、目标列表、`0x000C` 构包、设置读取和直接
  生命周期分支；冲突时以当前 A21 和可归因运行时证据为准。
- 确定 Story `ModeTargetRaw`、容量及其他 12 字节设置字段的合法组合、默认值与无队创建/既有队编辑语义；
  没有消费者证据的字段继续保持 opaque。
- 在既有 `PartySettings`、`IPartyModeRuleService`、领域原子更新和通知链中实现 Story 专属规则；畸形、
  非法目标、越权、旧会话、容量和竞态失败不得改变队伍或错误发送通知。
- 对 Story 创建、编辑、成员变化、队长/成员离队及发现的直接模式特有状态进行自动化回归；只实现被证据
  证明属于 Story 设置/队伍生命周期的行为。
- 使用当前 A21 取得可归因 C2S/S2C 帧与 UI 结果，并同步协议专页、样本、trace、index/catalog 和证据等级。

### Out

- Cycle 专属规则和最终三类建模审计。
- GitHub Issue #20 的公共队伍目录、`0x023C` 和列表初始/差分投影。
- “快速组队”匹配队列；若静态证据证明它是 Story 创建/进入不可分割的前置，则停止并回到根控制器评估。
- Story 副本战斗模拟、任务推进、奖励、掉落或与队伍设置无直接证据关系的玩法。

## Constraints

- 不得从枚举名、UI 文本或当前数值单独猜测 target 语义；字段命名必须有构包来源与消费约束。
- 静态 parser-safe、服务端接受和 UI 可消费是不同证据层，必须分别记录。
- handler 只调用 Command 实施业务并通过 Event/Notification 通知；Command 使用领域实例和领域/规则服务；
  Application 不得引用 Infrastructure。
- 先用自动化锁定行为，再实施最小规则/协议改动；真实客户端验收前不得标记 `ClientVerified`。
- 不改客户端二进制或配置；实机阶段只使用隔离 Host/代理与定向捕获，结束后释放本轮进程和端口。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Story 类别、target/default 和相关设置字段有一致解释，合法/非法值由证据而非猜测划分。 | 固定上游来源、当前 A21 构包/读取调用链、至少一份原生 Story 创建帧及冲突裁决。 |
| SC-2 | Story 创建/编辑只接受已证明合法组合；畸形、非法目标、容量、越权、旧会话和竞态拒绝不污染状态。 | codec、规则、领域和 handler 正反测试及状态/通知前后对照。 |
| SC-3 | Story 创建、编辑、成员变化和直接模式特有生命周期形成确定性领域/通知路径，并被当前 A21 正确消费。 | audience/顺序/代际测试，以及真实创建、编辑和至少一个 Story 特有操作的帧与 UI 结果。 |
| SC-4 | 已接受的 Normal、邀请、接受、名册、实时状态、详情、离队、清理和频道隔离不回退。 | 聚焦回归、全量适用测试逐项 Passed、Release build 0 warning/error 和分层断言。 |
| SC-5 | Story 协议资料、样本、trace、代码和测试一致，且不提前声称 Cycle、公共目录或快速组队。 | 协议一致性检查、完整结果独立审查和审查后新鲜验证。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约记录的用户授权：“任务已经派发了,我们继续推进功能开发……队伍列表……后面再处理”。 | 在根任务共享 worktree 内研究、实现、测试和记录 Story 子结果；通过审查与新鲜验证后创建一个本地接受提交；失败时只执行可证明的精确工作流回滚。 |

## Requires Confirmation

- 当前 A21 的 Story UI 操作需要用户配合时，提出一个明确操作请求；纯等待不作为任务循环。
- merge、push、PR、发布、已接受提交回滚或 worktree/分支清理不在本子规格授权内。

## Assumptions

- Story wire 类别 `0` 是高置信静态起点；target/default 和模式特有行为在取得当前 A21 构包/消费或实帧
  证据前保持未知。
