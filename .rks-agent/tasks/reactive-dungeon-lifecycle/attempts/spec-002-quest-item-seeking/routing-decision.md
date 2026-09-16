---
kind: rks-routing-decision
decision: single-spec
---

# Routing Decision

本子任务只有一个连贯验收边界：PVF 来源决定掉落，权威拾取决定背包，背包再派生 seeking Trigger。三者必须在一个事务结果中成立，拆成多个 Spec 会留下可部署的半闭环，因此采用 Single Spec、SDD Standard、direct plan。

