---
kind: rks-verification
decision: pass
---

# A21 实体商城购买完成验证

验证声明：合同SC-1至SC-5的服务端实现、隔离自动化验证及可实测Release产物已经交付；本声明不包含真实客户端成功UI或购买后续发行为。基线b54978c08c63a7a6fd0351c9b4abd93cd215afbe，工作区 `.worktrees/cera-shop-purchase`，分支 `feature/cera-shop-purchase`。独立审查 [approved](../reviews/change-review.md)。

## 当前结果与证据

| 条件 | 决定性结果 | 证据 |
|---|---|---|
| SC-1 | 真实PVF商品110000为物品3×1/3600；时装价格档位、蛋species、宠物装备和礼包/权益边界可复核。17项商城内容测试通过；独立审查用PvfProbe再次确认110000。 | `t005-full/` Infrastructure TRX；[内容证据](../../../../protocol/patterns/data/traces/a21-cera-shop-content-20260909.md) |
| SC-2 | 已注册实帧19字节请求；双变长列表与每条券尾游标解析，每个截断前缀和尾随数据整包拒绝；成功24字节、错误1/4/11为23字节。真实TCP畸形与无会话拒绝后仍可退出。 | `t005-full/` Protocol/Networking TRX（7项codec、1项TCP）；[指令证据](../../../../protocol/patterns/data/traces/a21-cera-shop-wire-20260909.md) |
| SC-3 | 真实PVF→生产规则→SQLite购买水余额96400/道具1；时装/宠物/宠物装备/纯实体礼包实际组合余额73020、期限/等级/奖励重读正确。合成普通装备/所有实例、叠加多槽、并发购买及实际事务独立连接重读通过；故障发生于扣费和第一件之后仍全量回滚。 | `t005-archive/`；`t005-full/` Application和Persistence TRX |
| SC-4 | 无空间、余额不足、同包后项未知/权益/混合权益、零受影响行、失效会话拒绝无部分扣费/发货；洗点道具保留，角色已学技能及SP/TP不变。提交后通知使用提交回执，发送失败不重购不撤销。 | `CeraShopRuleTests`、`CeraShopPurchaseTests`、`CeraShopStorageTests`，均在全量TRX通过 |
| SC-5 | 七项目完整回归2653 Passed/0 Failed/2条件跳过，58项新增商城检查全部Passed；两个缺PVF用例另在无PVF环境补跑2 Passed/0 skipped。最终Release构建0警告/0错误，独立审查approved，文档和产物可用于后续授权实测。 | `t005-full/results-summary.json`及原始TRX；`t005-no-archive/`；`final-build.log`；[交付说明](../../../../Document/CeraShop-PhysicalPurchase-20260909.md) |

## 检查命令和新鲜度

2026-09-09在任务工作树执行，全部exit0：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet build Server/86JP.Server.slnx -c Release --verbosity quiet
dotnet test Server/86JP.Server.slnx -c Release --no-build --logger trx --results-directory .rks-agent/tasks/cera-shop-purchase/verification/t005-full --verbosity quiet
# 缺档环境补证仅影响测试子进程
$env:PVF_ARCHIVE_PATH=$null
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -c Release --no-build --filter FullyQualifiedName~A_missing_archive --logger trx --results-directory .rks-agent/tasks/cera-shop-purchase/verification/t005-no-archive --verbosity quiet
```

逐条UnitTestResult统计，而非使用summary的notExecuted推测失败。完整回归后没有产品或测试代码变更，审查期间仅补文档和证据；审查approved后再次Release构建，日志为 `final-build.log`。最终对完整暂存差异执行 `git -c core.whitespace=cr-at-eol diff --cached --check` 通过；原始TRX含Windows CRLF与测试stdout混合换行，保留原证据字节，不以默认CR尾提示误判产品缺陷。新建协议/交付文档本地链接与review-new-files.json的产品SHA256已核对。所有故障注入仅在临时SQLite；TCP测试用临时端口，没有启动或替换用户Host。

当前PVF SHA256=`19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`。可运行产物 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll` SHA256=`DABA77B851A0A4E254E85F1532399BFADA822D7C75168539794A8A570F074C5E`。

运行环境边界：main HEAD仍为基线且状态干净；用户Host18116仍为16:01:33启动的原进程。本任务没有修改用户真实余额/角色物品、切换Host、合并或清理。完成后保留独立任务分支供用户选择集成；实际UI/自动续发、使用道具与退点继续按交付说明的边界处理。
