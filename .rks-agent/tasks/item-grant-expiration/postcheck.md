# Postcheck

Decision：retain。受控改期和部署成功，无需恢复。2026-10-14。

- 停服后重新导出真实core模板ID167种，PVF审计0未知，核对当前PVF路径和SHA；最新dry-run19目标、162永久/无期限、1虚拟金币排除。
- SQLite改前完整备份：artifacts/item-grant-expiration-20261014/rollback/database-before-expiration-test.db；改后启动前快照：同目录database-after-expiration-test.db，两个快照完整性ok且SHA已核验。
- 仅角色1123(character2)的19个限时槽位改为1791820800（UTC+8 2026-10-13 00:00）。items只有指定期限字段/41..44字节改变；items行数、所有slot行、其他所有表指纹与schema保持不变，没有直接删除。
- 详细清单historical-dry-run.json；实际差分与SHA见historical-change.json。启动后只读确认19个实例仍在且截止均为昨日，保留给用户正常登录验收。
- 新进程52176，exe为artifacts/item-grant-expiration-20261014/runtime/Dfo.Server.Host.exe；ProductVersion 0.2.5-item-expiration.20261014+snapshot.0c66b709624d4bef。
- Bootstrap ready、GM HTTP200、31个监听均由新PID持有，无缺端口；数据库完整性ok，结构未变。deployment-verification.json记录程序集/配置/PVF SHA及旧runtime。
- 未注册定时清理任务、未主动选角、未调用真实角色清理删除。自动登录清理领域回归已通过；实机验收仍由用户执行。

审查发现的代码/脚本问题均先修复后操作；旧67排除清单和UID当模板的逻辑没有执行过数据写入。启动后的合法后台业务可能改变运行态表，因此独立全库对照使用启动前两个快照，而非拿持续运行DB与旧备份作无差异承诺。
