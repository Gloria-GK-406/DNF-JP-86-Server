---
kind: rks-review
verdict: approved
---

# spec-004 完整结果独立审查

日期：2026-09-30。独立 reviewer 对基线 `4dbd73b48952c3158004167de6f02be14767df39` 至当前暂存与未暂存的完整增量做只读审查，覆盖 TaskContract、Spec 的 AC-001..005、Plan、取证设计、Human Test List、协议记录、生产代码和测试。审查未运行构建/测试，也未编辑或提交；新鲜验证另见 `../verification/completion-evidence.md`。

## Round 1：changes_required

1. 中：`OutworldReplayGuard` 未缓存失败结果，`0x00CD` 同属性扭转首次返回 `0x17`、同连接同序号重放改为通用 `0x11`；资产未双扣，但失败响应不一致。要求缓存原失败码并测试。
2. 中：普通强化虽已有非零 `AmplifyType` 拒绝，spec-004 未测试 `0x80` 与已鉴定 `1–4` 两类状态下装备、材料、金币均不变；AC-004 准入断言不足。

## Round 2：approved

独立 reviewer 复核实际完整增量，确认 `OutworldReplayGuard.cs` 缓存失败错误码、`OutworldHandler.cs` 按原码重放；`EquipmentOutworldHandlerTests.cs` 覆盖同属性失败与 action 2 重放。`EquipmentReinforcementDomainTests.cs` 新增 `0x80` 和 `3` 两类状态的资产不变断言。`git diff HEAD --check` 无空白错误；未发现剩余高、中严重度阻断项。

批准的是代码与文档审查门，非运行验证或 A21 实机验收。真实客户端帧/GUI、污染装备自然生成和 action 2 动态尾仍未闭合；文档保持 `ClientStatic`，不冒称 `ClientVerified`。
