# Preflight

2026-10-14。角色范围已由用户明确选择“仅角色1123的限时物品”。源基准df570d87，现有唯一无关文件Tool/WireSmoke/__pycache__/。

运行目标：artifacts/scheduled-task-dispatcher-20261014/runtime/Dfo.Server.Host.exe，版本0.2.5-scheduled-tasks.20261014+df570d87。数据库路径来自该runtime的appsettings.json：Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db；PVF来自artifacts/avatar-socket-text-20261011/Script.server.pvf。既有数据库完整性检查ok。

已只读确认1123=character_id2；目标示例490002925、490003187、10100300各有PVF有限期声明且ExpireTime=0。历史改期仅限其character所有容器且排除称号簿；账号仓库/邮件不变。正式操作前必须再次验证PID/exe/配置/角色映射、生成全量dry-run清单并备份，不能复用旧查询当作操作许可。

新实例代码和测试可执行；生产数据库目前未修改。操作失败时SQLite事务自动回滚；运行切换失败可重启旧exe，旧runtime与PVF保留。备份与字节差分在实际受控改期时补齐，以便用户登录前检查。不得调用登录清理或直接删除来替用户验收。

## 操作前预检补充

已由独立审查纠正历史识别方式：world槽位的id是实例UID，必须从item_core解码真实模板ID，且校验slot.kind与身份对应。重新导出167种模板，全部可识别；182个非称号簿角色槽位中19个限时目标、162个永久/无期限、1个虚拟金币排除。旧“67个未知/类型不符”清单已作废，不能据其执行。

PVF路径和SHA256由lifetimes-pvf.json固定，脚本在每次执行前核对。prepare_expired_test.py仅支持1123，改前核对dry-run，拒绝覆盖备份；事务内检查其他所有表指纹、schema、slot行和items非期限字段保持不变。独立审查已确认当前代码和脚本无未决阻断，实际操作后的最终审查仍需postcheck。
