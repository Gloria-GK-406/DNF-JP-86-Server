# 部署与回滚

- 分支：`task/achievement-state-events`；基线：`d0427ce3ecc987ab76050f0e714fe919c7188fcf`。
- Runtime：`E:/Project/DNF/DNF-JP-86-Server/artifacts/achievement-state-events-20261009/runtime`。
- 程序集：`0.2.3+achievement-state-events.20261009.befdb629`；PID：38276。
- 源码 manifest 指纹：见同目录上级 `prepared.json` 的 SourceFingerprint；发布生产改动的逐文件 SHA-256 与部署程序集 SHA-256 同时保留。
- 7001、10010、10011、10100、18080、18081 均归新进程；bootstrap 与 GM health 均 ready。
- 数据库继续使用 `E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。
- 备份：`E:/Project/DNF/DNF-JP-86-Server/artifacts/achievement-state-events-20261009/before-server-start.db`；integrity_check=ok，SHA-256=`9c2e5f3267bf19eb8dd9b41f29891f1db2ab4a31f2d1a5c75960c962f8348bea`。
- `live-audit.json` 比对角色、活动任务、成就、称号、物品、属性及邮件表：changed=[]。
- appsettings.json、experience.json、Resources、wwwroot 沿用现有部署；PVF、客户端 EXE 与 0200 插件哈希核验一致，未更新客户端。
- 旧可执行文件保留：`E:/Project/DNF/DNF-JP-86-Server/artifacts/title-quest-unlock-routing-20261009/runtime/Dfo.Server.Host.exe`。
- 回滚应先核验运行身份，再停止本次进程并启动旧可执行文件。保持实时数据库；不得直接以启动前备份覆盖测试后玩家数据。部署脚本失败恢复旧进程而不还原数据库。
- 原始部署、配置、哈希、监听、健康响应及审核保留在根仓库上述 artifacts 目录。客户端验收 pending user test。
