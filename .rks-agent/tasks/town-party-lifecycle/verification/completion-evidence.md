---
kind: rks-verification
decision: manual_required
---

# 完成验证：`town-party-lifecycle`

**被验证的声明**：分支 `task/town-party-lifecycle`、基线
`355aaf5cb6714044b4781185ec3118007bc97bec` 上的当前变更集已通过自动化与当前 A21 双客户端 UI
live oracle，但尚未满足 TaskContract revision 3 对 S2C 逐帧运行时证据的全部要求，不能结束 SDD。

## 决定性证据

- **SC-3/SC-8 与 SC-1/SC-2/SC-9/SC-10 的 UI 部分，AC-002..AC-004/AC-009/AC-011..AC-013**：
  2026-09-12 当前 A21 双客户端 live oracle 通过；赛丽亚 `[gate]` 互不可见、共享 `[normal]` 互相出现，
  邀请与接受后双方显示同一两人队伍且初始 HP 立即出现，mode-3 详情窗口显示目标，普通成员离队后
  本人无队、队长保留单人队。Release Host 同一窗口记录两端端点上报、邀请/接受、详情、离队与幸存者
  实时查询。证据：`protocol/patterns/data/traces/a21-town-party-detail-20260912.md`。
- **SC-3/SC-4/SC-5，AC-001/AC-005/AC-006**：领域、严格 decoder、通知投影和生命周期测试覆盖主动离队、
  最后一人解散、连接结束/返回选人/替代会话、邀请拒绝与竞态、两个真实 31 字节端点样本、畸形不污染和
  缺失端点安全回落。当前 A21 普通成员主动离队提供了要求的实机正向证据。
- **SC-6，AC-008**：C2S/NOTI 专页、六份真实 C2S 样本、`protocol/game/index.md`、
  `protocol/catalog.md` 与实机 trace 可互相导航；无法由当前 UI 单独归因的 `NOTI 0x0008/0x000B`
  保持 `Implemented`，未捕获的 S2C body 和范围外类型保持明确边界。
- **SC-7，AC-007/AC-010**：合并前 Release solution build 成功，0 warning、0 error；
  `.rks-agent/tasks/town-party-lifecycle/verification/premerge-20260912` 的 10 份新鲜 TRX 经逐项解析合计
  3050 Passed、0 Failed、0 Other/NotExecuted；
  Application→Infrastructure 分层测试包含在通过集合中。前一目标的独立审查曾为 `approved`；
  post-live 复审因证据缺口改为 `changes_required`。最终
  `git diff --check` 通过。

## 未验证条件

- 本轮 ClientHack 未部署逐帧抓包插件，S2C 的真实 body 仍待归档。需要重复最小邀请/接受、初始 HP、
  mode-3 详情与离队路径，并归档双端 C2S/S2C frame capture 或等价的逐帧发送/读取记录，才能满足
  SC-1/SC-2、SC-9/SC-10 的契约证据要求。
- 当前 Information 日志只有 C2S handler 语义行；它不能证明单独的 `NOTI 0x0008` 与 `0x000B`
  实际被发送或消费。确定性编码器、顺序测试和成功 UI 支持实现正确性，但不能替代该运行时记录。
- 单人提示中的“移除队伍”、成员完全缺失端点时的回落消费、战斗 P2P/UDP 中继与三类队伍设置属于
  TaskContract Scope Out 或后续验证；它们不是本次完成阻塞的原因。

## 判定

`manual_required`。生产与测试代码未发现新缺陷，功能/UI 实机结果成功；完成阻塞仅为需要用户参与的
带逐帧抓包最小复测。在该证据归档前，TASK-08 与根 SDD 保持未完成。

## 集成记录 — 2026-09-12

用户在知悉上述证据边界后接受当前可运行结果。任务提交 `7aec0c18` 已通过普通 merge 纳入主分支
`06c6a12a`。合并后 Release build 为 0 warning、0 error；
[`postmerge-final-20260912`](postmerge-final-20260912) 的十份 TRX 逐条统计为
3050 Passed、0 Failed、0 Other/NotExecuted。完整收尾和一次已诊断的墙钟测试时序异常见
[`integration-closeout.md`](integration-closeout.md)。本段只记录集成事实，不改变上述
`manual_required` 判定。
