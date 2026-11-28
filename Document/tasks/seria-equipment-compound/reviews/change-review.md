---
kind: rks-review
verdict: approved
---

# 赛利亚物品合成实现审查

独立复审对象为 `67cb3f931b0f1a122e2148cdbd8152e1056c9092`。按 `reviewing-work` 的 Code 与 Deliverable 判据，检查从规格基线 `818cbeed7` 到当前提交的功能实现、协议记录和实机步骤，重点复查原实现 `7743f68d` 对应的发现。范围依据[任务契约第 3 版](../TaskContract.md)、[规格](../spec.md)和[执行配置](../routing-decision.md)。

## 上轮发现

- **随机请求部位校验已修复。** 领域服务只限制来源槽；定向模式仍要求 PVF 所列部位包含底材的细分组，随机模式忽略部位。新增六字节用例 `09 00 01 00 10 00`，验证随机携带 16 可以结算、原帧不重复扣料、下一帧可再次结算。定向携带不匹配部位仍拒绝。 [规则服务](../../../../Server/src/Dfo.Server.Application/Inventory/EquipmentCompoundRuleService.cs#L17) · [回归测试](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/SeriaEquipmentCompoundTests.cs)
- **协议总表链接已修正。** `0x0197` 使用独立的赛利亚条目，链接请求页、结果页和本任务实机清单，并注明证据等级。 [协议总表](../../../../protocol/catalog.md#2026-11-28-赛利亚物品合成)

## 当前核验

- 使用开发 PVF 独立运行合成与效果路由 Application 用例，12 项通过且无跳过。Networking 的合成注册和相邻魔法封印帧 2 项通过。核对唯一工作单元中的底材销毁、角色及账号扣料和新 UID，提交后先发送同槽清除和库存增量，再发送结果槽。测试覆盖拒绝、写入回滚、重放以及后续合成。 [结算回归](../../../../Server/tests/Dfo.Server.Application.Tests/Inventory/SeriaEquipmentCompoundTests.cs) · [Networking 注册](../../../../Server/tests/Dfo.Server.Networking.Tests/Integration/SeriaEquipmentCompoundRegistrationTests.cs)
- 依据执行方提交的全量 Protocol 结果，2607 项通过，选角帧序快照 1 项失败，已在原始主干复现相同的第 32 帧差异。独立运行协议文档对账，主干和当前版本均未能解析现行消息枚举，分别报告 708 与 711 项失败。增加的三项均为该解析限制对新注册的诊断。已人工核对本次双向协议页、总表正文链接和生产注册。 [文档对账脚本](../../../../protocol/patterns/data/check-server-documentation-sync.py)
- 编号步骤包含材料核对、随机、定向、失败提示、重登以及可选的神器和传说样本。最终候选环境验证及 A21 用户反馈由后续验收跟进，当前材料界面、弹窗和封装表现仍待实测。 [实机清单](../Human-Test-List.md)
