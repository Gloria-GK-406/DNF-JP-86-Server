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
- SC-5：部署成功，以下运行与回滚输入证据已核对；用户可开始实机测试。

## 原生客户端边界

本次没有客户端/PVF改动。四轮动画、对手呈现、失败及领奖UI仍为待用户实机验收。运行中的比赛不跨服务重启恢复。

## 部署验证（2026-10-11 20:24 +08:00）

- 产品源码提交：`acf29009106a64dd0a29d399563ae9f8586370fe`；发布前源码干净。部署后仅补本任务证据并规范新增文档行尾，不改变产品。
- 版本：`0.2.4-tournament.20261011+acf29009`；PID43496，精确可执行路径 `E:/Project/DNF/DNF-JP-86-Server/artifacts/yellow-dragon-tournament-20261011-acf29009/runtime/Dfo.Server.Host.exe`。
- 可执行SHA256：`A78EE7C19CE2641570370615B68B00B37219D802088D4DC3494C011FD3FFB1DB`；8个关键程序集版本一致、SHA逐项记录于同目录上级的 `deployment.json`。
- 31个监听端口全部属于新PID；GM health及Launcher bootstrap均ready，GM邮件页HTTP200。启动stderr为空；随后再次读取进程路径及health仍正常。
- 权威数据库仍为仓库主目录 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，未重置角色资料；切换前db/wal/shm及SQLite一致性快照保存在 `artifacts/yellow-dragon-tournament-20261011-acf29009/database-before-20261011-202434`，quick_check=ok。
- 旧PID40756停止，旧 `artifacts/monster-death-transaction-20261011-711f489e/runtime` 的二进制/配置完整保留并核对哈希；部署脚本失败路径支持重新拉起旧包，不自动覆盖玩家数据库。
- PVF身份保持上述SHA；没有客户端改动。`deploy.ps1`、发布产物、stdout/stderr及 `deployment.json` 均在仓库根该artifact目录。

## 仓库处置

保留 `task/despair-tower` 与原工作区；无push/PR/合并/清理。主干仍 `67ffa14f82e8e789e53e7a22fe1af1b85627d57e`。产品接受提交后仅一次文档证据收尾提交，最终审计HEAD由Git读取在任务交付时报告，不在文档内自引用。
