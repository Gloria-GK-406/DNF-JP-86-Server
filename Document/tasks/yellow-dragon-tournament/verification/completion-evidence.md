# 验证与部署证据

## 软件验证

工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/despair-tower`，分支 `task/despair-tower`；接受基线 `711f489e8cdf33e22f95e07a2d140d217afc2e09`。测试使用当前部署PVF `artifacts/avatar-socket-text-20261011/Script.server.pvf`，SHA256 `49D4CAFA402DAA93E9C60EFAC8550C20789BB5BD9F99DD7B9235FD0F20666E7A`。

| 检查 | 命令及决定性结果 |
|---|---|
| Release | `dotnet build Server/86JP.Server.slnx -c Release --no-restore -v:q`；exit0，0警告/0错误 |
| Application | `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-build --no-restore`；3685通过/0失败/0跳过 |
| Protocol | `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore`；1453通过/0失败/0跳过 |
| Persistence | `dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -c Release --no-restore`；468通过/0失败/0跳过 |
| 真实内容 | `PvfTournamentDungeonContentSourceTests`，实际PVF解析1通过/0跳过 |
| 差异 | `git diff --check`；exit0 |

TRX保存在仓库根 `artifacts/tournament-tests/`，最终全量分别为 `tournament-application-all-final.trx`、`tournament-protocol-all-final.trx`、`tournament-persistence-all-final.trx`；内容测试 `tournament-content.trx`。全量首轮两处失败是封闭注册名单未加入3种新命令，补齐后定向2通过且最终全量通过；未放宽原有断言。

行为覆盖包括四轮才结算、未加载/越轮/重复死亡拒绝、入场与领奖回滚、缺票/多人/模式拒绝、死亡淘汰、复活限制、每行一次领取、自动翻牌过期/旧run保护，以及真实满材料栏奖励邮件附件。

## Success Conditions

- SC-1：research与协议人工区记录字段/来源、项目规则取舍与未知项。
- SC-2/SC-3：领域、协议及实际统一派发路径的反例与回滚测试通过。
- SC-4：上述构建和全量回归；独立审查见 `../reviews/change-review.md`。
- SC-5：待部署并验证进程、端口、HTTP、程序集与数据库备份。

## 原生客户端边界

本次没有客户端/PVF改动。四轮动画、对手呈现、失败及领奖UI仍为待用户实机验收。运行中的比赛不跨服务重启恢复。
