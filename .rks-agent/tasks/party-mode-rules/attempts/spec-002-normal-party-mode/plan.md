---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: A21 Normal 队伍模式闭环

## Planning Choice

选择 Task Units，因为规则实现的正确方向取决于第一单元对 Normal target/default 和最后成员处置的证据
结论；在结论出现前细化后续代码会重复或固化猜测。各单元分别保留可审计的取证、实现和实机结果，但
共同属于一个 Normal 接受边界。

## Approach and Shared Decisions

- 固定当前 A21 EXE/dstr/ClientHack 基线，先对照上游，再沿客户端 Normal 创建、自动设置、编辑和移除
  分支做静态闭合；运行时只抓 `0x000C`、与创建/名册/设置/清除直接相关的最小帧。
- 第一单元必须输出一份明确 verdict：Normal 的 `ModeTargetRaw=0` 与服务端 `0xFFFF` 是同义、不同状态
  或实现错误。后续规则与默认值只能消费该 verdict，不能从现有代码倒推事实。
- 当前实帧已证明 C2S 与 S2C canonical 的前两字段不是同序；后续 decoder 必须交换标题索引与
  回归/特殊队伍标志，规则只归零 canonical `[1]`，通知在 titleIndex 为 0 时回送独立 raw-dstr。
- 当前 A21 的队长 `0x000D` 语义为整队解散：清除全部旧成员并删除旧 party；普通成员离开才保留同一
  generation 的幸存者刷新。不得按旧版上游行为创建 replacement party。
- 保持接受基线的 `PartySettings`、`IPartyModeRuleService`、领域原子操作和通知 stream 作为共享接口。
  局部增加 Normal 规则即可时不新增策略层；若证据触发根契约的强重构信号，停止并返回控制器重评。
- 实机步骤使用隔离端口、定向 allowlist 和当前 A21 protocol/debug 客户端；每轮结束停止本轮进程，
  不修改基准客户端目录。需要用户点击时只提出一次明确动作，不把等待写入 Task registry。
- 公开队伍目录、同城广播、列表排序和普通成员编辑权限不参与任何单元验收。

## Work and Dependencies

| Work | Covers | Result boundary | Depends on |
|---|---|---|---|
| T-001 Normal target/default 与处置证据裁决 | RQ-001, RQ-006, RQ-007 | 固定上游/A21 静态证据、最小抓包方案和真实无队创建/最后成员处置样本；输出 `0`/`0xFFFF` verdict、字段置信度及实现约束。该结论本身不改生产规则。 | accepted spec-001 evidence |
| T-002 Normal 规则与领域行为 | RQ-002..RQ-005 | 按 T-001 的 target/title verdict 调整 Normal 规则；最初按旧版上游实现的队长转移，由 T-004 的当前 A21 证据纠正为整队解散。 | T-001 |
| T-003 协议投影与集成回归 | RQ-003..RQ-005, RQ-007 | 修正 C2S→canonical 前导字段交换与标题回送；最初加入的 replacement/retirement 投影由 T-004 删除，恢复旧 generation 的确定性 clear。 | T-002 |
| T-004 当前 A21 闭环、证据纠正、资料与完成控制 | RQ-005..RQ-007 | 复测 Normal 创建、标题、队长与最后成员离队；以客户端 UI 为准撤销错误的队长转移模型，归档证据并完成协议资料、全量回归、独立结果审查和 fresh verification。 | T-003 |

T-001 与 T-004 分开，是为了让第一次实帧裁决能先决定实现，而最终客户端验收必须针对实现后的结果；
同一帧只有在运行条件与最终代码完全一致时才能被两处共同引用。

## Verification

- T-001：固定哈希/地址探针、上游来源、客户端构包/读取路径和定向真实帧相互一致；verdict 必须能被
  一个反例推翻并明确记录停止条件。
- T-002/T-003：规则/领域/Command/codec/notification 的正反、状态前后、并发与 byte-exact 测试；
  Application Party、Protocol Party 和分层断言通过。
- T-004：当前 A21 用户可见创建/编辑/移除结果与帧/Host 记录一致；Release solution build 0 warning/
  error，全量适用测试逐项 Passed，协议 index/catalog/专页/trace 一致；独立完整结果审查批准后再运行
  fresh completion verification。

## Next Planning Boundary

先详细计划并执行 `TASK-01-normal-wire-and-lifecycle-evidence.md`。后续 Task 只在 TASK-01 给出可消费的
target/default verdict 和实际最后成员处置路径后细化；若实机动作尚需用户配合，先完成所有静态与捕获
准备，再提出最小操作请求。
