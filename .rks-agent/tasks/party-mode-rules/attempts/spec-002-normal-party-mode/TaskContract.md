---
kind: rks-task-contract
revision: 3
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-4
  - SC-7
  - SC-8
depends_on: []
---

# TaskContract: A21 Normal 队伍模式闭环

## Context

### Source Request

根任务要求在初始共享规则服务接受后，按 `Normal -> Story -> Cycle` 顺序逐类研究并实现真实差异，
最后再审计整体建模。用户允许在该授权边界内根据证据选择实现方式，但只有强证据证明当前模型产生错误
所有权、错误状态转移或无法表达已确认差异时，才提前做聚焦重构。

最终实机验收后，用户依据当前 A21 UI 明确纠正生命周期：

> “队长退队后就应该直接就散了，因为 UI 显示了队伍已解散。可能新版本和我之前的老版本逻辑不一样。”

### Current Situation

接受基线 `d1351ce6047ea84db1c764efc3ad9466781f07af` 已完成三类设置值对象、集中规则服务、严格
`0x000C`、type-1 通知和回归玩家队伍标志归零。当前 A21 静态证据显示 Normal 类别为 `2`，客户端
自动/默认构包路径在无合法现有队伍时写 target `0`；现行服务端默认设置仍为 target `0xFFFF`，已有
Normal 实机编辑样本也写 target `0`。这些值的合法范围、默认含义和 Normal 生命周期约束尚未闭合。

已有实机证明两人 Normal 队伍可编辑容量并消费 type-1 通知，普通成员离队后幸存队长保留单人队；最终
实机又证明队长离队时当前 A21 UI 将旧队视为已解散，即使服务端尝试投影新队也不会表现为队长转移。

## Goal

只针对 Normal 模式，查明并实现当前 A21 证据支持的目标/default、创建/编辑和相关生命周期规则，完成
真实 Normal 创建、编辑及队长/成员离队处置的客户端可观察闭环，同时保持已接受的共享设置与组队生命周期
不回退。

## Scope

### In

- 先复核上游 Normal 行为，再定位当前 A21 的 Normal 创建/编辑/default/移除相关构包、读取与 UI 文本，
  以可归因运行时样本解决 target `0` 与服务端默认 `0xFFFF` 的差异。
- 在既有 `IPartyModeRuleService` 和领域边界中表达仅有证据支持的 Normal 目标/default 与编辑约束；
  若证据表明两值承担不同 wire 或生命周期含义，应显式建模而非静默互换。
- 保持 canonical S2C 设置 `[1]=0` 产品策略、未知字节无损、队长/当前会话/容量/代际校验，以及
  handler -> Command -> domain/rule -> notification 分层；C2S 与 S2C 前导字段按当前 A21 已确认顺序
  显式投影，不得用原样复制误清预设标题索引。
- 用当前 A21 验证 Normal 的无队创建、既有队编辑，并完成队长解散与最后成员离队的等价处置；
  记录真实 C2S/S2C 帧、服务端接受/拒绝与 UI 结果。
- 更新自动化回归和 `protocol/` 专页、样本、trace、index/catalog 的证据状态。

### Out

- Story、Cycle 的目标资料表、进入资格、返回、奖励或其他模式特有行为。
- 公开“查找队伍”目录、同城镇/大区广播、列表刷新/排序，以及普通成员编辑权限的产品规则变化。
- 与 Normal 队伍设置无直接证据关系的副本战斗、P2P/UDP 或奖励模拟。
- 没有当前 A21 可归因证据时把推断升级为 `ClientVerified`。

## Constraints

- 研究顺序仍为上游 -> 当前 A21 静态 -> 真实运行时；发生冲突时以后两者为准并保留冲突记录。
- 不得为了消除 target `0` / `0xFFFF` 表面差异而先选一个值；必须先证明它们是等价默认、不同状态，
  或其中一个是现行实现错误。
- Normal 规则优先作为既有集中规则服务的局部扩展；当前实帧已经证明 decoder 的 C2S→canonical
  前导字段投影与标题回送需要聚焦修正，但不改变共享类型、领域边界或 Story/Cycle 范围。
- 实机验证只使用隔离 Host/中转和定向帧白名单；结束时关闭本轮进程与监听，不修改基准客户端目录。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Normal 的类别、target/default 与相关设置字段边界有一致解释，`0` 与 `0xFFFF` 的差异被证据闭合而非猜测。 | 上游对照、当前 A21 构包/读取证据、至少一份可归因 Normal 创建样本，以及冲突结论。 |
| SC-2 | Normal 创建/编辑只接受已证明合法的组合；非法 target、非法容量、越权、旧会话或竞态均不污染状态或错误通知。 | 规则/领域/Command/codec 正反测试和状态前后对照。 |
| SC-3 | Normal 创建、既有队编辑、成员变化与队长解散/最后成员处置形成确定性领域/通知路径，并在当前 A21 UI 中表现一致。 | 顺序/audience/代际自动化测试，以及真实创建、编辑、解散/移除的帧和 UI 结果。 |
| SC-4 | 已接受的邀请、接受、名册、HP、详情、离队、清理、`[1]=0` 与三类共享设置基础不回退。 | 聚焦回归、全量适用测试逐条 Passed、Release build 0 warning/error 与分层断言。 |
| SC-5 | 协议资料准确区分 Normal 已确认事实、实现选择和仍未解决项，且不提前声称 Story/Cycle。 | 专页/样本/trace/index/catalog 一致性审查与独立完整结果审查。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根契约记录的用户授权：“工作区清理都做完后开新的任务契约。我们开始工作吧……你可以有更高的自由度来选择工作方式。”该授权建立在用户确认“初始规则服务 -> 逐类实现 -> 最终建模审计”的顺序上。 | 在根任务共享 worktree 内研究、实现、测试和记录本 Normal 子结果；通过审查与新鲜验证后创建一个本地接受提交；失败时仅执行可证明的精确工作流回滚。 |

## Requires Confirmation

- 当前 A21 UI 的创建/编辑/移除操作需要用户配合时，应提出一次明确操作请求；纯等待不作为任务循环。
- merge、push、PR、发布、回滚已接受提交或清理根任务 worktree/分支不在本子规格授权内。

## Assumptions

- `PartyCategory.Normal=2` 已有静态与实帧双重证据；`ModeTargetRaw=0` 是否是 Normal 唯一 canonical 值、
  `0xFFFF` 是否只属于旧服务端 placeholder，仍必须由本子规格证明。

## Revision 2 Evidence Correction

两次当前 A21 无队 Normal 创建实帧均写 target `0`，裁决 `0xFFFF` 为现行 placeholder 错误。用户选择
预设队名时 C2S `[1]` 分别为 `3`/`6`，而当前实现原样复制后把它当 canonical 回归标志归零，导致
“无队伍名”；静态读写路径确认 C2S 前两字段必须交换为 S2C canonical 顺序。两人队长发送空 body
`0x000D` 后当前服务端解散全队；当时依据旧版上游行为暂定为应转移队长。详见
`evidence/task01/runtime-verdict.md`。

## Revision 3 Current-A21 Correction

最终实机中，服务端按 Revision 2 实现先清旧队、再向幸存者投影新 party id，但当前 A21 UI 仍明确显示
“队伍已解散”。用户据此确认当前版本的队长离队语义就是整队解散，而不是旧版的自动转移。Revision 3
以当前客户端为准，撤销 `PartyRetirement`/replacement 设计，只保留已独立验证的 Normal target `0`、
C2S 字段交换与标题回送修复。详见 `evidence/task04/runtime-correction.md`。
