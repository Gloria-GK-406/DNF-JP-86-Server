---
kind: rks-verification
decision: pass
---

# 领主之塔候选交付验证

验证对象为TaskContract revision 1、Spec全部条件，代码提交`884bafd4e5717217e12d9dbc428b80ee8c7fe532`。本结论为完整服务端候选实现、受控本机部署及可执行验收交付完成；用户原生客户端验收按契约Scope Out单列，不将其声称为完成。独立评审见`../reviews/change-review.md`，已读取并确认approved且目标匹配。

## 条件与证据

| 条件 | 结果及决定性证据 |
|---|---|
| SC-1 入口与活动准入 | pass。BossTowerFoundationTests覆盖默认/关闭033F投影，BossTowerDomainServiceTests覆盖关闭直接入场拒绝；真实PVF头部3505可用。原生可见性已列入用户清单。 |
| SC-2 全队扣票、回滚与补票 | pass。全队缺票不收费、外层abort恢复资产与占用、Prepare reservation排斥交错事务；Boss补票覆盖06:00、旧claim、GM有效配置和邮件计数。全量Application 3680通过/464跳过、零失败。 |
| SC-3 疲劳、虚弱与消耗品 | pass。领域准入免疲劳且专用死亡/离开不进入普通虚弱；实际handler复活币拒绝，人偶拒绝且不扣、普通消耗品仍可用；独立评审核对真实接线。 |
| SC-4 阶段、协议与生命周期 | pass。八层等待合法move、当前k层全灭、重复与替代session、结果页队长变化及协议实际handler回归；Protocol1587通过/60跳过、Networking95通过。TouchSocket合成002D→0334→0025→001E等链通过。原生视觉时序待用户测试。 |
| SC-5 奖励、任务与容量 | pass。Boss规则验证逐怪/终局加成仅一次及最终上限；autoroot真实收件者投影、终局冻结receipt、SQLite故障回滚、重放及满包邮件用例通过；评审核对击杀、掉落及任务事实接线。 |
| SC-6 NPC与兑换开启 | pass。真实当前地图NPC1010/商店1118正反授权、材料与交付事务、兑换开启回归通过；评审核对无客户端自报权限、无重复材料费用。 |
| SC-7 运行态与资产边界 | pass。替代session不能改旧局、精确清理及退出不退票/不发终奖；内存run不跨重启恢复，SQLite资产沿用；部署前后108表全部相同。 |
| SC-8 完整候选部署可测试 | pass。必需检查、独立评审、备份与副本演练通过；现服PID41348，12程序集版本/哈希绑定实现提交，bootstrap/GM ready，31TCP/2UDP，旧进程退出，stderr空；验收清单已交付。 |

## 当前检查与记录

全部路径相对仓库根：

- `artifacts/lord-tower-implementation-20261019/full-r2/release-{build,test}.log`、对应exit文件及8份TRX：Release零警告零错误；7598项，6712通过、886跳过、零失败；test-summary.json逐项目汇总。命令为`dotnet build Server/86JP.Server.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true`及`dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 --logger trx --blame-hang-timeout 3m --blame-hang-dump-type none -- xUnit.MaxParallelThreads=1`，清除PVF_ARCHIVE_PATH后执行。
- 同目录`boss-real-pvf.{log,trx}`：同一候选程序集，设置现服PVF并以`--filter FullyQualifiedName~BossTower`运行Application，17通过、零跳过、零失败；覆盖真实八阶段29MAP及规则/领域。
- `artifacts/lord-tower-implementation-20261019/full/`：前端158项及构建、WireSmoke5项、Release guard11项通过，构建保留已有chunk提示；无改动前端/Python，后续修复仅C#和文档，不重复无关检查。
- 首轮42项失败及真实原因保留于`full/failures.json`，对应修复聚焦结果在`regression/`，最终完整重跑清零；不通过跳过或取消生产检查掩盖失败。
- `artifacts/lord-tower-20261019/`：publish.log、source-manifest、备份审计、rehearsal-comparison、post-start-comparison、deployment-receipt和fresh-verification。20:31:58独立于deploy脚本再次读取真实进程/程序集/健康/监听/配置/PVF，结果均匹配；启动日志确认8阶段29地图。
- `full-r2/preexisting-check.json`：18项既有未跟踪文件哈希未变。实现checkpoint90个路径精确匹配owned manifest，next-round基线仍`6f19c07b4cd2b5c2656198dbfb725d80544bcf13`。

## 实机边界

`Document/LordTower-Acceptance-20261019.md`列出入口、实际加载/走门、逐怪与结算、NPC、按钮、消耗品/退出/全灭、多人和06:00补票。这里的自动socket、ClientStatic与70Reference均未升级为原生WireCaptured/ClientVerified；issue不提前验收关闭。seal只归档本次候选交付证据，不改变已部署代码。
