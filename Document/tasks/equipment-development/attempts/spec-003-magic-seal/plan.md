---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：魔法封印装备

## Planning Choice

采用 `direct`：一个证据门后接一个装备实例结算链和两条候选请求，细节可在同一 Plan 维护；无需为每条 opcode 建 Task Unit。用户要求快速开发，保留完整结果审查和最终验证。

## Approach and Shared Decisions

- 已接受装备实例的随机属性三元组和变化字段是持久化/投影载体；`SealFlag` 有独立的物品封装/交易语义，未经 A21 证据不用于魔法封印状态。规则输入经 Abstractions 内容源与 Application 规则服务取得，命令/协议层不直读 PVF。
- A21 构包/接收与真实 PVF 决定可接协议、费用和选项范围；70 与上游作为参照，差异和未知写入证据。请求字段只作槽位/操作选择，装备、费用与结果由服务端事实重算。
- 复用账户命令执行闸和 SQLite 事务；先校验，再在一个提交边界写装备及金币。材料槽因 A21 字段语义未闭合而拒绝、不扣物；取得新证据后才扩展材料结算。提交后投递权威槽与必要 ACK；失败不投递成功。

## Work and Dependencies

| Work | Executable action | Depends on | Covers |
|---|---|---|---|
| T-001 | 核对 70 ELF/资料、上游随机词条服务、A21 `0x0191/0x01B6/0x0197/0x01C8` 构包与接收、真实 PVF 的随机属性/费用表；在证据设计文档裁定哪些路径可实现、哪些隔离，并补 `protocol/` 人工区。 | `8ab85ebd` 接受基线 | RQ-001、RQ-004 |
| T-002 | 基于真实 PVF 样本先写合法解封/词条变更、无权/状态错误、回滚与重读行为测试，确认预期 RED；实现内容解析、规则与领域事务结算，保留 UID 和无关字段。 | T-001 可用的规则与费用身份 | RQ-002、RQ-003 |
| T-003 | 对已闭合入口写 decoder/encoder/handler 字节及投递测试，确认 RED；接线并实现提交后的权威装备/金币刷新与失败响应；补固定样本 Human Test List。 | T-001 的线语义和 T-002 结算结果 | RQ-004、RQ-005 |

## Verification

先跑定向内容、领域、协议和 handler 测试及构建；再跑相关项目回归，真实 PVF 全量与已记录的两项宠物基线失败分开报告。独立审查整个增量，最终新鲜验证逐条对照 AC-001—005；未做 A21 GUI/抓帧不标 `ClientVerified`。
