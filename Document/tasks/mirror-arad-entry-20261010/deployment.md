# 镜像阿拉德部署记录

用户授权：2026-10-10“先部署看看当前实现是否有效，然后给我一个测试集，我一个一个测”。这是前次仅本地实现之后的新部署授权。

- 源分支：`task/monster-declaration-parser`。
- 部署源码提交：`aea0c7361f4a9def60a40770a6518cabe27936b4`；文档提交不改变该产物身份。
- 运行版本：`0.2.3+mirror-arad.20261010.aea0c736`。
- 实际新进程：`49556`；启动时间：`2026-10-10T11:53:33.6109684+08:00`。
- 新运行目录：`E:/Project/DNF/DNF-JP-86-Server/artifacts/mirror-arad-entry-20261010/runtime`。
- 旧进程：`42528`，旧运行目录完整保留 `E:/Project/DNF/DNF-JP-86-Server/artifacts/quest-kill-objectives-20261010/runtime`。
- 数据库仍为原共享SQLite，不替换角色数据；新启动初始化两张独立镜像表。
- 停服后备份：`E:\Project\DNF\DNF-JP-86-Server\artifacts\mirror-arad-entry-20261010\database-before-20261010-115330`，包括当时存在的db/wal/shm；备份quick_check=ok。

## 已验证

发布成功，所有Dfo.Server程序集ProductVersion和SHA-256已记录。新进程路径与产物一致，7001/10010/10011/10100/18080/18081六个TCP监听均归属新进程；GM health和launcher bootstrap均ready，GM邮件页面HTTP200。stderr为空；只读确认新增character_mirror_quests和character_mirror_daily存在，1123的角色ID2/等级86保留。

机器凭据、完整程序集哈希和配置副本保留在仓库根忽略目录 `artifacts/mirror-arad-entry-20261010/deployment.json`。启动日志为同目录stdout.log/stderr.log。实际角色进图、任务UI及领奖仍为ClientVerified待验收，见[逐项实机测试集](client-test-set.md)。

## 回滚输入

旧运行目录、配置副本及停服数据库快照均保留。需要回滚时先核对当前运行进程再停止新服，启动保留的旧Host；两张新增表不会要求旧程序读取。不要直接覆盖测试期间已产生的新角色数据；若涉及数据库恢复，先评估并另行确认恢复范围。
