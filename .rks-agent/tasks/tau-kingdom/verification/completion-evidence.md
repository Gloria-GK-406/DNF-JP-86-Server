---
kind: rks-verification
decision: pass
---
# Completion Evidence: 牛头怪乐园实现交付

验证范围是 TaskContract revision1 的实现与条件部署分支，不是实机验收或已部署声明。独立审查及 RQ-004 精确澄清复审均 approved，见 ../reviews/change-review.md。

## 新鲜检查

工作分支 task/next-round-20261013，接受基线 3ad1467bf287d06986e37760fceba2c2b09164b0。

| 检查 | 结果 | 本地工件（仓库根 artifacts/tau-kingdom-20261015/） |
|---|---|---|
| Release solution build | 0 warning / 0 error | build.log |
| Tau targeted | 11 passed / 0 failed / 0 skipped | targeted.log / tests/tau-targeted.trx |
| Application 全量实际 server PVF | 3918 passed / 0 failed / 0 skipped | application-server-pvf.log / tests/application-server-pvf.trx |
| Application 全量无 PVF | 3440 passed / 0 failed / 400 skipped | application-no-pvf.log / tests/application-no-pvf.trx |
| Infrastructure 全量实际 server PVF | 905 passed / 0 failed / 2 skipped | Dfo.Server.Infrastructure.Tests.log / tests/Dfo.Server.Infrastructure.Tests.trx |
| Persistence 全量 | 468 passed / 0 failed / 0 skipped | Dfo.Server.Persistence.Tests.log / tests/Dfo.Server.Persistence.Tests.trx |
| Protocol 全量 | 1520 passed / 0 failed / 0 skipped | Dfo.Server.Protocol.Tests.log / tests/Dfo.Server.Protocol.Tests.trx |
| whitespace | git diff --check 通过 | 控制器和 reviewer 均执行 |

构建使用 `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1 -nodeReuse:false /p:BuildInParallel=false`。测试使用对应 Server/tests/<project>/<project>.csproj，Release、--no-restore、-m:1、-nodeReuse:false，最终全量均生成 trx；已有构建后的检查使用 --no-build。

实际运行服务配置的 PVF 为 artifacts/sao-ticket-20261014/Script.server.pvf，SHA256 91902D91AA0015024B75F55BE037FE7C2900FD66432459B62578CA0E865DEBF4。客户端 PVF FDDBB938361D4CA817598AE479E2F951B47E1DB64E096943167D20014A97AF98 缺少仅服务端需要的 DGN60/7100 迷宫修补；误用它的初跑有3项内容失败，不能冒充最终通过。初跑另有1项新增命令保护清单失败，已修复并被正确 PVF 全量覆盖。不为过测试修改 PVF。

## 成功条件映射

- SC-1/2：TauKingdomTests 真 PVF 入口、26级/无票/多人/镜像拒绝、冻结等级、三房间首房50怪、权重7、0疲劳、事务回滚不扣票；Boss表原路径3本以及85/86边界。普通地图生成/怪物/掉落在同一全量回归覆盖。
- SC-3：TauDailyRefillTests 的05:59/06:00、重复、回滚、离线多日只当前周期、普通批次与独立周期隔离；空/Tau-only登录仍领取普通批次。TauKingdomTests 的跨6点未发布→发布真实commandbus补检和重试；SelectCharacterLifecycleTests 证明工作流在publication之后补检；OnlineExpiredItemScheduleTests 真实注册调度器跨6点同时执行清理与补票、释放会话后不调度。所有普通补给原回归通过。
- SC-4：ExperienceItemUseTests 固定四书、百分比向下取整、20..84、过期、经验失败共同回滚、重复请求与ACK/绝对库存/0025经验投影；Infrastructure声明解析测试。旧etc路径仅扣书的RED已记录在worker证据/测试历史，最终GREEN由全量覆盖。
- SC-5：活动2215默认开启及显式禁用保留、006C合法字符串、无0482；分层与命令/通知注册受全量架构测试约束。
- SC-6：实际进程记录 processes-pre-deployment.json，DNF.exe PID6536 来自 DNFClient-A21-protocol2；旧服务器PID16080仍在sao-core runtime。因此选择用户授权的“仅准备/报告，等指定部署时间”分支，没有停服/线上替换/玩家数据库或PVF变更。待部署产物与最终身份另存 deployment-status.json，不能当作部署回证。

## 验收边界

所有自动化检查证明实现可交付。真实A21入口、经验条更新、3房间完整游玩与跨6点客户端行为仍由用户部署后测试，清单见 Document/TauKingdom-20261015.md。数据库没有 schema 变更；未修改或创建用户的测试角色。
