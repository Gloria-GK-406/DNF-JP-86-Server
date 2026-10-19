# 领主之塔部署记录

2026-10-19，北京时间20:31部署完成；20:31:58再次核验运行身份和健康。完整候选版本已可供用户测试，原生客户端验收尚未完成；[#165](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/165)保留待验收状态。

## 运行身份

| 项目 | 实际值 |
|---|---|
| 分支 | `task/lord-tower-20261019` |
| 实现提交 | `884bafd4e5717217e12d9dbc428b80ee8c7fe532` |
| 程序版本 | `0.2.7+884bafd4e5717217e12d9dbc428b80ee8c7fe532`，12个Dfo程序集版本和SHA256均与manifest一致 |
| 当前进程 | PID `41348`；`artifacts/lord-tower-20261019/runtime/Dfo.Server.Host.exe` |
| 旧版本 | PID44736已退出；旧runtime保留于`artifacts/gold-silver-quota-fix-20261019/runtime/` |
| 数据库 | `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`，沿用原玩家数据 |
| PVF | `artifacts/scheduled-notice-pvf-20261019/Script.server.pvf`；SHA256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06` |
| 配置SHA256 | `B48818D03767091DDDB8C982268D5CB3AA72FF8E326AC96D80220D32FA8E0226`；经验表同样原样复制 |

启动日志确认`Boss tower content initialized: 8 stages, 29 maps`。Launcher bootstrap与GM health均为`ready`，GM页面HTTP200；31个TCP与2个UDP监听均归属新进程，端口集合与旧版本相同，stderr为空。客户端及PVF没有替换。

## 数据与回退材料

操作与证据目录为`artifacts/lord-tower-20261019/`。在线SQLite backup API备份为`online-backup.db`；正式停止精确旧进程后再次备份为`stopped.db`。备份完整性为`ok`，外键检查无异常，108张表逐表内容有摘要。

候选先以`rehearsal.db`副本禁用Networking、LauncherAuth与GM Web启动，确认无TCP/UDP监听、无stderr及数据库内容变化，再部署现服。启动后的`post-start.db`与停止时快照比较，108张既有表全部一致、无新增表。原始审计分别在`online-backup.json`、`rehearsal-comparison.json`、`stopped.json`和`post-start-comparison.json`。

`source-manifest.json`记录提交、配置、PVF、程序集和监听身份；`deployment-receipt.json`及`fresh-verification.json`记录部署和后续核验。保留旧runtime及全部备份；回退程序只换回旧runtime并继续使用当前数据库，不自动恢复备份覆盖测试期间资产。

## 检查与验收

- Release构建零警告、零错误；8项目7598项：6712通过、886跳过、零失败。无PVF模式的跳过未当作资源验证；真实PVF领主塔专项17项另行全部通过。
- 前端158项及构建通过；WireSmoke 5项、Release guard 11项通过。现有前端大chunk提示不影响构建。
- 独立完整结果评审`approved`，绑定上述实现提交；代码未在评审后变动。上述测试属于服务端自动证据，不等同于原生客户端验收。
- 过程证据在`artifacts/lord-tower-implementation-20261019/full-r2/`和`.rks-agent/tasks/lord-tower/`；当前功能分支保留，未合并、推送或创建PR，既有18个未跟踪文件哈希均保持。

请按[实机验收清单](LordTower-Acceptance-20261019.md)测试。先重登确认亡者峡谷入口，再验证扣票、八层走门、奖励、NPC兑换和结果三按钮；多人及06:00补票可随后补测。实现证据和策略边界见[实现说明](LordTower-Implementation-20261019.md)。
