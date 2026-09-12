---
kind: rks-review
verdict: approved
---

# Change Review：A21 Normal 队伍模式闭环

## 审查目标与范围

- Scope：`change`，分支 `task/party-mode-rules`，接受基线
  `d1351ce6047ea84db1c764efc3ad9466781f07af`；审查对象是当前 staged、unstaged 与 untracked 的完整
  spec-002 交付集合。
- 约束来源：TaskContract revision 3、Spec、Plan、四个 Task Unit 结果、静态探针、两轮定向原始帧、Host/UI
  观察、协议专页/index/catalog、自动化/TRX 以及生产与测试完整 diff。
- 本审查重新检查了代码路径和原始证据，没有把 executor 的结果摘要或协议状态标签直接当作事实。

## Verdict

`approved`。当前没有仍需修复的 P1/P2、验收缺口或证据越级；AC-001..AC-007 均有与最终实现一致的
证据。交付可以进入审查后的新鲜 completion verification。

## 审查中发现并已闭合的问题

首轮审查发现三类交付问题，均已在最终裁决前修正并复核：

1. `protocol/game/index.md` 的 C2S `0x000D` 状态一度只写普通成员离队，与专页、catalog 和当前 A21
   多人队长/单人队证据不一致；现已同步为三类主动离队均 `ClientVerified`。
2. Normal 捕获 wrapper 一度只比较消息 type，使 S2C `NOTI 0x000D ITEM_LIST` 被误收为 C2S
   `LEAVE_PARTY`。现已对方向、selector 和 type 做联合白名单，并增加同 type S2C 与错误 selector 反例。
   两份 JSONL 已清理为 35/25 条，独立扫描 `invalid=0`。
3. spec-001 Python `__pycache__` 和九个测试项目的未跟踪 `TestResults` 目录不属于本 Spec 交付边界；现已
   精确清理，最终 untracked 集合只包含 spec-002 工件及预期的协议样本/trace。

## 验收核对

- **AC-001**：固定 A21 构包/读取路径、两份真实无队 create 和编辑帧共同确定 Normal
  `ModeTargetRaw=0`；`0xFFFF` 被正确降为旧服务端 placeholder，而非与 `0` 等价的客户端状态。C2S
  `[special,titleIndex]` 与 S2C canonical `[titleIndex,special]` 的差异同时有静态、实帧和“标题丢失”UI
  反证。
- **AC-002**：decoder 对 direct 与 inline-title 两种形状交换前两字段，保留 tail/raw-dstr；规则服务只把
  canonical `[1]` 归零，Normal 默认 target 为 `0`，非零 target 确定性拒绝。byte-exact 用例覆盖预设标题、
  inline-title、默认值、合法归一化和非法组合。
- **AC-003/004**：无队创建、当前队长编辑、非队长/旧会话/非法输入拒绝、邀请不被失败消耗、状态前后
  不变与并发 revision 单调性均有领域/handler 回归；失败路径不发布通知，也没有绕过集中规则服务的
  Normal 写入。
- **AC-005**：普通成员离队只清本人并刷新同 generation 幸存者；`slot 0` 队长离队和单人队离队均清除
  全部旧 membership、删除旧 party，并只向旧精确成员投影旧 party id 的 type-3 clear。生产与测试中
  `PartyRetirement`/replacement retirement 路径已归零，既有四种迟到通知反例继续保护精确成员视图。
- **AC-006**：TASK-01 的最终等价 disband 路径已有 C2S 空 body `0x000D`、向两名旧成员发 clear、Host
  accepted 和 UI 解散的同轮证据；TASK-04 的 old-clear + replacement 轮次进一步反证旧版自动转移，并
  同轮复核 target `0`、字段交换和预设标题。最终代码撤销 replacement 后与 TASK-01 已实测路径一致；
  证据没有把 TASK-04 的错误中间实现冒认为最终行为。
- **AC-007**：协议专页、trace、index 和 catalog 已区分当前 A21 事实、固定上游冲突与未闭合边界；未引入
  Story/Cycle、公开队伍目录、普通成员编辑权限、P2P 或玩法效果。Release 全套留存结果为
  `3085 Passed / 2 Skipped / 0 Failed`，两项 skip 是提供真实 PVF 时按设计跳过的“档案缺失”反例。

## 独立验证

- 独立运行 Application `FullyQualifiedName~Parties`：`23/23` Passed。
- 独立运行 Protocol `FullyQualifiedName~Party`：`73/73` Passed。
- 逐项解析九份串行 TRX：`3085 Passed / 2 Skipped / 0 Failed`，与汇总一致。
- Normal 捕获器 self-test：基础重组/allowlist 与 Normal 空 body、同 type S2C、错误 selector 反例均通过。
- 两份定向 JSONL 联合白名单扫描：`35/25` 条，`invalid=0`；`git diff --check` 通过。
- 生产与测试范围搜索未发现 `PartyRetirement` 或 `Retirement` 残留。

## 边界与残余风险

TASK-04 实机轮次运行的是被随后否决的 replacement 中间实现，因此该轮对队长生命周期的证明性质是
反证，不是最终码复跑；最终 disband 分支则由 TASK-01 的同实现路径实测、最终代码复核及领域/投影回归
共同闭合。这个证据组合足以支持当前验收，但不得在后续资料中改写成“replacement 轮次直接验证了最终
服务端实现”。

公开队伍目录、可见范围广播、Story/Cycle 语义、P2P/UDP、奖励与玩法效果仍是明确的后继边界。当前
Normal 规则只闭合创建/编辑 wire、target/default 和离队生命周期，不应外推为这些能力已经实现。

## Disposition

批准当前结果。按既定工作流执行审查后的新鲜 build/test/diff completion verification，再由 controller
封存 spec-002；本审查不授权合并、推送或清理 worktree。
