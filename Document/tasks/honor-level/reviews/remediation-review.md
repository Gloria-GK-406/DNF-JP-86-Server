---
kind: rks-review
verdict: approved
target: cfc3cc3688e015ee1af25420584dfad7a8a7ea4c
baseline: 436682e0a160b03d4f943092a5769d818c77aebd
contract_revision: 2
---

# 荣誉品级测试修正复审

按 [任务契约](../TaskContract.md)、[规格](../spec.md) 和 reviewing-work 的 Code、Deliverable 判据复审最终候选。相对已审查提交，变更仅涉及四处协议测试。生产代码、协议和实机验收说明沿用[前轮审查](change-review.md)。

- 等级券与任务交付的测试装配在构造函数对应位置传入 `TestRequired.InitialHonor`，原有拒绝、回滚和通知断言保持。SQLite 选角用例通过同一依赖提供新账号荣誉，继续从 SQLite 核对原有永久任务奖励。
- 选角快照明确核对第 13 号索引的 `USERINFO` subtype 1 荣誉等级和本级经验，再将这 8 字节恢复为历史快照里的零值。其余字节继续使用原有哈希逐帧核对。
- 主干同 PVF 的 1,402 个非 Unit 协议用例通过。修正后，完整协议项目 2,390 个用例通过，覆盖此前发现的 24 处失败。以上支持测试修正和实现审查，A21 界面仍待[实机验收](../acceptance.md)。
