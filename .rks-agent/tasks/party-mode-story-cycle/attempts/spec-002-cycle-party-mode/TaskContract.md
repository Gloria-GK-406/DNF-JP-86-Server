---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-2
  - SC-3
  - SC-4
depends_on: []
---

# TaskContract: A21 Cycle 队伍模式闭环

## Context

### Source Request

根任务根据用户“队伍列表后面再处理，我们继续推进功能开发”的优先级，从已接受的队伍设置、Normal
模式与频道隔离基线推进 Story、Cycle。Story attempt 因缺当前 A21 原生帧已封存；本子规格独立交付
Cycle，不语义恢复 Story，也不把 Story target 推断复用于 Cycle。

### Current Situation

接受基线 `9a3a0b492da11f48525d1225d02513e0dd1cd458` 已能 byte-exact 保存 12 字节设置，并确认
`PartyCategory.Story=0`、`Cycle=1`、`Normal=2`。规则服务只对 Normal 强制 target=0；Cycle 的
`ModeTargetRaw` 资料来源、无区域状态、创建/编辑约束和直接生命周期尚未闭合。当前 A21 静态总览只证明
Cycle 显示路径会使用 target 查找循环区域，并有“没有循环地下城区域”文本，不足以定义合法值域。

## Goal

查明并实现当前 A21 Cycle 队伍的设置字段、区域目标/default、创建/编辑约束及与该模式直接相关的
生命周期，形成真实 Cycle 创建、编辑和至少一个模式特有行为的客户端可观察闭环，同时保持既有队伍与
频道行为不回退。

## Scope

### In

- 固定上游与当前 A21 输入，静态定位 Cycle 选择、区域来源/列表、无区域分支、`0x000C` 构包、设置读取
  和直接生命周期；冲突时以当前 A21 与可归因实测为准。
- 确定 Cycle `ModeTargetRaw`、容量及其他相关设置字节的合法组合、默认值与无队创建/已有队编辑语义；
  没有消费者证据的字段继续 opaque。
- 在既有 `PartySettings`、`IPartyModeRuleService`、领域原子更新与通知链内实现被证明的 Cycle 专属规则；
  畸形、非法区域、越权、旧会话、容量和竞态失败不得改变状态或错误通知。
- 对 Cycle 创建、编辑、成员变化、队长/成员离队及直接模式特有状态做自动化回归；只实现与 Cycle 设置/
  队伍生命周期有直接证据的行为。
- 使用当前 A21 取得可归因 C2S/S2C 与 UI 结果，并同步协议专页、样本、trace、index/catalog 和证据等级。

### Out

- 已封存 Story 的语义恢复或实现，以及最终三类建模审计。
- GitHub Issue #20 的公共队伍目录、`0x023C` 与列表投影。
- 独立快速匹配队列；若静态证据证明它是 Cycle 基本创建/进入不可分割的前置，停止并回根控制器评估。
- Cycle 副本战斗模拟、奖励、掉落或与队伍设置无直接证据关系的玩法。

## Constraints

- 不得从 Story、Normal、枚举名或 UI 文本直接类推 Cycle target；字段名和值域必须有构包来源和消费约束。
- 静态 parser-safe、服务端接受、当前 A21 UI 可消费分层记录；实机归因前不得标记 `ClientVerified`。
- handler 调 Command 实施业务并通过 Event/Notification 通知；Command 使用领域实例和领域/规则服务；
  Application 不得引用 Infrastructure。
- 先用自动化锁定已证行为，再实施最小规则/协议改动；unknown 字节继续无损保存。
- 不改客户端二进制或持久配置；实机只用隔离 Host/代理与定向捕获，结束后释放本轮进程和端口。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Cycle 类别、区域 target/default 与相关字段有一致解释，合法/非法值由证据划分。 | 固定上游、当前 A21 构包/读取/无区域调用链、至少一份原生 Cycle 创建帧及冲突裁决。 |
| SC-2 | Cycle 创建/编辑只接受已证明合法组合；畸形、非法目标、容量、越权、旧会话和竞态拒绝不污染状态。 | codec、规则、领域、handler 正反测试及状态/通知前后对照。 |
| SC-3 | Cycle 创建、编辑、成员变化和直接模式特有生命周期形成确定性领域/通知路径，并被当前 A21 消费。 | audience/顺序/代际测试，以及真实创建、编辑和至少一个 Cycle 特有操作的帧与 UI 结果。 |
| SC-4 | 已接受的 Normal、邀请、接受、名册、实时状态、详情、离队、清理和频道隔离不回退。 | 聚焦回归、全量适用测试逐项 Passed、Release build 0 warning/error 与分层断言。 |
| SC-5 | Cycle 协议资料、样本、trace、代码和测试一致，不提前声称 Story、公共目录或快速组队。 | 协议一致性检查、完整结果独立审查和审查后新鲜验证。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约记录的用户授权：“任务已经派发了,我们继续推进功能开发……队伍列表……后面再处理”。 | 在根任务共享 worktree 内研究、实现、测试和记录 Cycle 子结果；通过审查与新鲜验证后创建一个本地接受提交；失败时只执行可证明的精确工作流回滚。 |

## Requires Confirmation

- 当前 A21 的 Cycle UI 操作需要用户配合时，提出一个明确操作请求；纯等待不作为任务循环。
- merge、push、PR、发布、已接受提交回滚或 worktree/分支清理不在本子规格授权内。

## Assumptions

- Cycle wire 类别 `1` 是高置信静态起点；target 的精确区域表、default、无区域 sentinel 和模式特有行为在
  取得当前 A21 构包/消费或实帧证据前保持未知。
