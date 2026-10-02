# Routing Decision

- TaskContract: `./TaskContract.md`
- Contract revision: 1

## SDD Readiness

官方邮件的编辑、明确收件人、附件发送及持久化记录是独立可验收结果；根授权覆盖。现有邮箱和事务边界已明确，具体API与草稿/模板存储方式在此范围内选择，不预建点券模块。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 邮件发放容易在超时重试时重复资产、批量失败后部分误发，或新装备附件复用UID。Spec限定对玩家可观察的提交与重试结果，独立完整审查验证生命周期和事务路径；有限治理成本比后续资产修复更低。编辑到发送是一个稳定接受边界。
- Reroute conditions: 广播/定时运营或真实玩家操作改变授权范围，回到root successor decision。

## Controller Selection

- Controller: `sdd-governed`
- Profile source: `focused-delivery`
- Rationale: 写入玩家资产有明确重复发放与生命周期破坏后果，独立Spec挑战、路线和完整审查保护这些风险；采用planless focused，边界清晰无需另外Plan。主控持有游标与接受操作，产品交bounded worker，挑战和审查使用不同fresh上下文。
