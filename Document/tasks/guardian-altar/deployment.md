# 守护者祭坛部署准备

状态：候选已生成，未切换玩家服务。候选代码版本0.2.10，分支`codex/168-guardian-altar`。
当前主干版本核对为0.2.9；并行功能合并前仍须再次核对PATCH，不自动升MINOR或创建Release。

## 可审查产物

工作区：`C:/Users/Gloria/.codex/worktrees/1890/DNF-JP-86-Server`。

| 产物 | 位置／结果 |
|---|---|
| 发布产物 | `artifacts/guardian-altar/runtime/`，Release框架依赖构建，含新构建GM静态资源 |
| 候选配置计划 | `artifacts/guardian-altar/candidate-settings.json`，尚未安装为运行配置 |
| 发布身份 | `artifacts/guardian-altar/candidate-manifest.json`，程序集、配置与静态资源哈希 |
| 一致备份 | `artifacts/guardian-altar/player-backup.db`，SQLite只读源backup API，包含已提交WAL |
| 备份记录 | `artifacts/guardian-altar/backup-manifest.json`，4账号、9角色、658物品，110张业务表 |
| 迁移副本 | `artifacts/guardian-altar/compatibility.db`，候选Host已成功启动、71项预热0失败；全部网络组件关闭 |
| 隔离网络冒烟 | `artifacts/guardian-altar/network-probe-result.json`；副本PID46816，TCP仅25001／25011／25080／25081、UDP0，Launcher与GM均ready、GM静态页面200，已结束自有助手并恢复默认关闭配置 |
| 兼容结果 | `artifacts/guardian-altar/compatibility-result.json`，integrity_check=ok，仅新增两张祭坛表，accounts／characters／items／item_slots逐行哈希不变 |

备份、私有配置和数据库均留在Git忽略的artifacts内，不提交。以上备份是兼容性检查输入；
实际切换前必须重新取得一致备份，不能把本次快照当作未来部署时刻的最新玩家数据。
兼容性助手PID22532已按EXE精确核实后结束，TCP／UDP端点均为0；没有停止玩家服务。

## 玩家库与占用核对

主干实际玩家库路径：
`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。
来源为玩家服务启动记录及环境覆盖，已用只读SQLite备份核对；不使用本工作区空库替代。

2026-10-27本轮曾观察遗忘之地候选PID42760使用该库；随后它已离线，最新7001／18080监听者为
马戏团ProbeHost（PID17484，`fbd8/.../artifacts/circus-probe/default-host/ProbeHost.dll`，游戏端口22001）。
这些是时点快照，切换前必须重新查所有Host／dotnet进程、TCP／UDP、EXE、参数与实际数据库覆盖。
不能以进程名筛选遗漏dotnet托管的ProbeHost，也不能凭旧PID决定停止目标。

当前玩家部署PVF：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256
`3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B`，已由当前轮启动日志确认并独立执行祭坛测试。
不能因常用客户端副本路径相似就替换这份部署内容。

## 拟用隔离端点

候选计划：频道25001、游戏25011、Launcher HTTP 25080、GM HTTP 25081，均在25000–25999内。
`Channel.UseScriptCatalog=false`，只配置一个频道，发布配置完整替换频道及GamePorts数组；
禁用固定端口UDP发现。否则默认`10000+channel.Id`会覆盖手写GamePorts，或环境数组残项带回旧端口。
实际切换须核对所有配置源、继承环境、最终有效监听和启动日志；客户端登录器需指向候选频道／HTTP端点。
如统一协调要求复用默认端口，先明确释放时段，再重新生成并检查完整有效配置。

## 切换步骤

1. 协调当前玩家库的唯一验收时段、允许结束的准确进程及恢复目标；没有授权不结束任何已有服务。
2. 重新核实数据库、PVF、EXE、配置覆盖和端口；记录原服务提交、版本、环境及恢复命令，
   取得新的SQLite一致备份。不要裸复制活动主文件忽略WAL，也不要并行开启两个写同库的Host。
3. 在新备份副本复核迁移兼容性后，候选使用主干实际玩家库启动；保留原运行产物和配置。
   候选配置计划中的数据库路径不是当前已启动声明。
4. 核实实际PID、程序集0.2.10、PVF哈希、TCP／UDP与健康状态，提供用户明确连接方式。
5. 按[编号实机流程](acceptance.md)测试，记录真实结果。出现问题先定位失败层，再修复并重测。
6. 恢复服务时保留测试期间合法玩家进度；新表是加法迁移，不因回退程序删除表或直接恢复旧库覆盖新资产。
   只有明确需要数据回滚且已授权时才讨论恢复数据库。

用户实机通过之前不创建PR；云端明确通过和当前提交CI通过之前不合并。
