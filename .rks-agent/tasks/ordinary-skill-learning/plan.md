---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 普通技能学习

## Approach

先实现完整领域学习动作与事务证据，再接入 CMD 0x001D 切片。规则扩展放在已有 CharacterSkillRuleService，学习事实/持久化由 CharacterSkillLearningDomainService 收口，命令仅调用领域服务。请求和应答格式属于 Protocol。

## Decisions Before Work

- 等级字段是增量；先准备最终技能图，再检查同包前置，重复 ID 拒绝。
- 现有技能保留槽与附加配置；新技能槽位规则依据参照服，不能把所有正等级技能强塞默认 extra=01。
- 只在所有检查成功后写库，写入异常沿命令事务传播；响应在提交之后发送。
- 格式错误由 decoder 拒绝；格式有效但退点/其他模式由领域拒绝并回应。校验算法只属于 Protocol。
- 实机未知处按证据置信度记载，不改客户端或替换运行实例来绕过等待。

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-002, RQ-003, RQ-004 | 增加学习请求/结果领域值与服务；扩展共享规则槽位能力；使用当前会话/角色/页与预算，完整准备后保存。 | 新增行为测试先证实 RED；成功、失败、同包前置、点数不足、槽位以及真实 SQLite 回滚/提交/重读。 |
| T-002 | RQ-001, RQ-005 | 消费 T-001：新增 LearnSkill 消息、解包、应答编码、handler/事务命令，ProtocolModule 与消息目录接线。 | 真实请求/应答向量、边界和校验测试；handler/线级注册路由和提交后响应。 |
| T-003 | RQ-006 | 保存有限技能片段及摘要，不复制含账户信息的完整日志；更新协议与 catalog；构建/回归/独立评审/最终证据。 | 每项验收有明确证据，Release 产物可供实测；当前运行 Host 不被替换。 |

## Choices During Implementation

页授权与普通场景判断沿用当前会话/角色模型；遇到尚未证明支持的状态保守拒绝。失败码以客户端分支证据为先，普通成功 extra 以已有存储为准。生产写入者串行，独立只读研究与评审可以并行。
