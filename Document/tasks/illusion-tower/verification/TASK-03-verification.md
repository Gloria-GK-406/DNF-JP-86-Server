---
kind: rks-verification
decision: pass
---

# TASK-03 有界验证

验证对象是TASK03奖励后端及TASK02既有行为兼容，不是整个迷妄之塔Spec完成。基线762d0cb4加已接受TASK01/02未提交内容。执行者为tower_rewards；所有.NET命令串行。

## RED/GREEN

2026-10-03，新增 `Defeat_commits_real_experience_and_one_settlement_receipt` 后，在尚未接入奖励的owner上运行：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~Defeat_commits_real_experience --no-restore --verbosity quiet
```

exit 1，1失败0通过；团灭结果Completion.Settled=false，未到后续资产断言。实现后修正测试夹具的SQLite列名exp，并显式配置内容源；最终同一行为测试通过。其余新增用例是实现后验证，不作为额外TDD周期声称。

## 最终检查

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~IllusionTower|FullyQualifiedName~Experience|FullyQualifiedName~CommandHandlerArchitectureTests' --no-restore --verbosity quiet
git diff --check
```

测试exit 0：204通过、0失败、0跳过。diff检查exit 0。PVF身份沿用TASK01 inputs：SHA256 `3b2fe3da115fee949282558a864a319fc9ca197e1d1f22e656e74316a0f04b06`；本轮按该现成路径读表，没有修改档案。

| 条件 | 决定性结果 |
|---|---|
| 塔独立经验公式、成员等级、APC倍率、排除普通bonus | 规则50级普通692/APC2076、40级APC1431；两成员真实Kill分别2076/1431，禁止普通bonus的测试替身未触发 |
| 完成/失败奖励层、最终cap、分解一致 | 真实1层失败与5层失败分别按1/4；实际XP写入与receipt.Base/Gained/Experience均为1,100,000封顶 |
| 候选数/确定性、金币截断、空项、独立时装 | 固定G1000→175→612→686→384→4224；4候选；确定种子复现，时装分支为空，default等级阈值与Normal难度加项生效 |
| 真实内容源可消费 | 当前PVF50级普通692/APC2076、1层clear5065；15层4候选含真实金币与有耐久的装备，16种种子均重复一致 |
| 真实资产、满包、不重复提交 | 四装备候选仅两格可放，2交付2未交付；正确耐久与实例展示值；重复Die/Kill资产不再变化 |
| 状态与资产回滚 | XP SQL触发器失败回滚金币/死亡/结算；外层事务中断回滚即时XP、最终装备和receipt，随后可重新正常结算 |
| 基础任务事实 | 真实任务64/65行随APC杀死递减，重复Kill不变；两层测试第一层不推进整塔clear、末层才推进 |
| 清理与下一次入场资格 | 过期成员不获得奖励且不阻断清理；余券只读检测不扣券；无券返回false |
| 既有能力兼容 | 现有经验、塔生命周期、命令构造依赖架构检查均通过 |

网络通知重试与ClientVerified不属于此有界判定；后续协议单元必须仅重放receipt，不能再次调用奖励资产能力。
