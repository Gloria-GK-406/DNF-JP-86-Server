---
kind: rks-plan
spec:
  path: ./spec.md
mode: task-units
state: ready
---

# Implementation Plan: 队伍设置模型与 `0x000C` 基础闭环

## Planning Choice

采用 `task-units`。最先要交付的是会约束后续代码的 A21 wire 证据；设置模型、领域原子性和通知细节
都必须消费这份结果，提前把所有实现细节写死会重现本任务要避免的“以旧上游猜当前客户端”。各单元仍
属于同一个 Spec 和一次最终接受提交，不形成新的独立验收边界。

## Approach and Shared Decisions

- 取证采用三层来源：`ServerS4A21 origin/master` 只描述上游行为；当前固定 SHA 的 A21 unpacked 客户端
  证明构包/解析分支；真实 C2S/S2C 帧与 UI 结果提供可归因运行时证据。来源等级不能互相替代。
- `Dfo.Server.Data.Parties` 持有协议无关、不可变的 `PartySettings`/`PartyCategory`/原始模式目标；
  `PartySnapshot` 携带同 revision 的设置副本。未知字节通过受控值对象无损保留，不把可变数组暴露给
  快照消费者。
- `Dfo.Server.Abstractions.Parties` 暴露 `IPartyModeRuleService`（名称可在不改变责任时微调）与设置领域
  操作；`Dfo.Server.Application.Parties` 是共享规则和 process-local party 状态的唯一写入边界。
- Protocol 的 `0x000C` handler 只做严格解码、当前会话 gate、Command 调用、日志与成功后的通知发布；
  不直接更改队伍，也不在协议层重写容量/类别判断。
- 现有 `PartyNotificationDelivery` 的 party-generation/revision stream 继续负责锁外有序投影。设置变更
  使用不可变 `PartyNotificationPlan`，新建和编辑采用客户端证据要求的不同消息阶段。
- TDD 从最窄可观察行为开始；每个后续单元必须先消费前一单元的持久证据和 GREEN 接口，不能绕过
  wire 证据另造兼容格式。

## Work and Dependencies

| Work | Covers | Delivered result | Boundary and dependency |
|---|---|---|---|
| T-001 A21 `0x000C`/type-1 证据基线 | RQ-004, RQ-008, RQ-009 | 上游当前 ref、客户端 create/edit 构包与 NOTI 消费路径、字段/长度/标题分支、候选响应 handler、可复现地址和置信度记录；必要的真实样本采集脚本/步骤 | 第一单元；只取证和记录，不改生产语义。它必须给出足以开始 codec/model 的明确布局，或证明 Spec 需要修订。 |
| T-002 设置模型与共享领域规则 | RQ-001, RQ-002, RQ-003, RQ-006, RQ-007 | 不可变设置值、类别/目标、默认 Normal、规则服务、PartyState/Snapshot/创建与更新领域转移、并发/代际保护的 RED/GREEN | 依赖 T-001 的确认字段；不注册网络协议、不实现模式特有玩法。 |
| T-003 协议与通知基础闭环 | RQ-004, RQ-005, RQ-008, RQ-009 | 严格 decoder、typed request、Command/handler 注册、新建/编辑消息阶段、type-1 encoder/handler/delivery 及 byte/audience/order/拒绝测试 | 依赖 T-001 wire 契约和 T-002 原子领域 API；任何新客户端差异先回到证据，而非宽松解析。 |
| T-004 集成取证、资料与完成验证 | RQ-010 及全体 | 当前 A21 最小 create/edit 实机闭环、可归因帧记录、协议专页/index/catalog、全量回归、完整结果审查与新鲜完成验证 | 依赖 T-002/T-003 GREEN。纯等待用户 UI 操作不作为 Task；准备完成后直接请求一次明确操作。 |

依赖顺序为 `T-001 → T-002 → T-003 → T-004`。若 T-001 发现 create/edit 不是同一规范设置状态，
或默认类别映射与当前静态结论冲突，停止后续实现并回到 Spec/根控制器；其他字段偏差在当前子规格内
更新证据后继续。

## Verification

- T-001 通过独立复算地址/字节路径和来源一致性检查，不以旧文档或上游注释自证。
- T-002/T-003 每项可观察行为先有能因缺失/错误实现而失败的 RED，再运行聚焦 GREEN 和相关 Party/
  Session/Notification 回归；并发测试使用有界同步点而不是墙钟猜测。
- T-004 运行 Release solution build、九个项目加 Infrastructure 缺档案场景的逐项目适用测试，逐条解析
  TRX；执行 `git diff --check` 和 Application→Infrastructure 分层断言。
- 实机验收至少关联合法 create、合法 edit、一次非法/无权更新不污染、完整新建状态与 type-1 设置刷新；
  未捕获的 S2C 不升级 `ClientVerified`。
- 完整结果由独立 reviewer 按 Spec、Plan、完整 diff、协议资料和运行时证据审查，随后再做新鲜完成验证。

## Next Planning Boundary

下一单元是 `TASK-01-a21-party-settings-wire-evidence.md`，只负责 T-001。其输入是当前 A21 固定客户端、
`ServerS4A21 origin/master` 和现有 `protocol/game/client-to-server/0x000C.md` /
`server-to-client/NOTI-0x0009.md`；它的输出必须先明确两种请求布局、12 字节规范设置和 type-1 读取边界，
之后才值得详细计划 T-002。
