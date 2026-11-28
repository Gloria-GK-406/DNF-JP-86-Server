---
kind: rks-review
verdict: approved
---

# 消耗品 CD 重构独立评审

目标为 `task/consumable-cooldown-policy` 修复后工作树的源码、测试与交付说明，基线 `e79884827631be73e3da2651b06345dd58b50624`。依据为工作区任务契约修订 3、[Spec](../spec.md)、[Plan](../plan.md) 和 `reviewing-work` 的 Code、Deliverable 标准。

- 持久化预检曾与提交使用不同时间，导致到期边界可能在预检放行后抛异常。已复核修复，业务保留同一个结算时点，普通入口、开盒源读取与专用入口传递该时间。边界测试覆盖预检时实际时钟已到期、结算基准仍未到期的情况。 [判定服务](../../../../Server/src/Dfo.Server.Application/Characters/CharacterItemUseDomainService.cs) · [截止边界回归](../../../../Server/tests/Dfo.Server.Application.Tests/Characters/CharacterItemUseTests.cs)
- 遗忘之地错误入口保护曾按动作名拒绝所有骑乘模板。修复后对移动和变身检查专用地下城限制 `[4000]`，与已接入内容的判定对应。回归同时检验专用券无法由普通命令扣除以及普通骑乘继续可用。 [入口规则](../../../../Server/src/Dfo.Server.Application/Inventory/ItemUseEffectRuleService.cs) · [普通命令回归](../../../../Server/tests/Dfo.Server.Application.Tests/Game/UseStackableHandlerTests.cs)
- 复核了新表的角色与共享键、到期覆盖条件、既有事务内写入、旧值表原样保留及选角顺序。疲劳药只从既有次数账本计算截止。旧次数规则的可选解析通过应用规则服务，领域快照按现行维护归属过滤来源。 [持久化](../../../../Server/src/Dfo.Server.Persistence/Inventory/SqliteConsumableCooldownTable.cs) · [配额规则](../../../../Server/src/Dfo.Server.Application/Inventory/ItemUsageRuleService.cs) · [迁移回归](../../../../Tool/DbMigrate.Tests/ConsumableCooldownMigrationTests.cs)

副本间人偶行为、经验秘药 99 组是否能从触发件的 0x00AC 恢复其余模板的客户端图标，以及第三位开启的瞬移药重登表现均以 A21 后续验证为准。当前 dev 的装备修理道具没有正数且第三位开启的组合，完成情况按实际模板记载。独立评审查阅了实现及回归，完整测试结果由完成验证记录。 [A21 验收清单](../user-test.md)
