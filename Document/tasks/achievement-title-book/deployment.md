# 成就与称号簿部署（2026-10-08）

运行信息见 [deployment.json](deployment.json)。PID30128，程序集 `0.2.2+achievement-title-book.20261008.774f7b35`；部署检查时间15:28:04+08:00。

- 新运行目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/achievement-title-book-20261008/runtime`。
- 旧运行目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/epic-book-20261008-935f5aaa/runtime`，保留原配置和资源。
- 数据库沿用 `Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`；一致备份在新部署目录的 `before-stop.db`，SHA256 `63803d4f8ae9f644c0f07d75d95abaaaf7b03c2e272e278c4a03da8507d24f4a`。
- 7001/10010/10011/10100/18080/18081全部属于PID30128；Bootstrap和GM health为ready，GM页面200。
- 应用程序集SHA256 `AC8B43FE33EBFC97B61D63EF67FDDCB4B9C1B6AF21C2C4B870EF80BCC884C33B`；完整程序集、配置和PVF哈希在运行记录。
- 生产输入指纹 `7794E98371B1ABAC88ACF2B67BFC7604337CB60AD197136A629D4A410372C156`；基准774f7b35加本次已审改动，未改客户端/PVF。

先用一致副本及独立端口启动：HTTP均ready。首次行指纹比较将新增 `characters.last_inn_town_id` 误判为旧数据变化；逐旧列核对确认没有任何原值或行变化。比较器改为保留原列/行严格比较并另列新增结构，复验副本和实际部署都通过。新增列来自工作分支既有旅店修复；新增表为achievement_consumed_facts、title_book_unlocks及此前未部署的domain_events/domain_event_deliveries。

部署目录保留 `smoke-before.db`、`smoke.db`、`smoke-audit.first.json`、`smoke-audit.json`、`live-audit.json`、准备/启动脚本及日志。停止或恢复时必须先重新核对实际进程路径；不能把历史PID当作当前运行证明。数据库备份不会自动覆盖玩家测试后新增数据。

实机状态仍为待用户测试，步骤见 [human-test-list.md](human-test-list.md)。
