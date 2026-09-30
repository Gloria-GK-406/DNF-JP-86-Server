---
kind: rks-review
verdict: approved
---

# spec-005 完整结果独立审查

日期：2026-09-30。独立 reviewer 以 `c15e6add5590d5f957210763a0b1c118a93982c3` 为基线，对暂存的锻造子项完整增量做只读审查，包括契约、Spec AC-001..005、Plan、取证、Human Test List、协议记录、生产代码和测试。审查者未编辑、提交或运行会写工作树的测试；新鲜运行证据另见 `../verification/completion-evidence.md`。

## Round 1：changes_required

1. 中：`EquipmentForgingDomainService` 直接读取装备定义及 PVF 内容并调用纯规则，越过项目的领域服务→规则服务分界。要求由规则服务独占这些内容读取，领域服务只传当前装备事实。
2. 中：AC-002 的错槽资产不变断言不足。要求至少覆盖目标与材料同槽、错误容器，并断言装备、材料、金币均不变。

## Round 2：approved

新增 `IEquipmentForgingRuleService`/`EquipmentForgingRuleService`，装备定义、耐久及 PVF 决策由规则服务承担；领域服务只传装备 ID、耐久、锻造等级、材料 ID 与 roll。`EquipmentForgingDomainTests` 补同槽、错误容器、无效槽测试，装备、材料、金币均未变。复核完整暂存增量后未发现剩余高、中严重度阻断项，`git diff --cached --check` 清洁。

非阻断残余：目标与材料同时不合格时，调整后的校验次序可能先返回材料错误；错误码语义需 A21 实帧校准，不影响资产结算。审查批准不等于实机验收；真实客户端帧/GUI、卷券变体仍未闭合，不标 `ClientVerified`。
