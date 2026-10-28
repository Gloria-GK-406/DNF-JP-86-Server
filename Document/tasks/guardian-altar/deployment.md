# 守护者祭坛部署准备

状态：按用户授权维护实机验收候选，版本0.2.10，分支`codex/168-guardian-altar`。
初版基线为`21c1dfd78f0e7866eb40e878305dcd019ea99f5e`；入图修复后实际运行提交和PID以
`artifacts/guardian-altar/active-deployment.json`所指目录的`running-process.json`及`readiness.json`为准。
当前主干版本核对为0.2.9；并行功能合并前仍须再次核对PATCH，不自动升MINOR或创建Release。

## 当前默认端口验收

用户2026-10-28明确授权使用默认端口部署；遗忘之地会话同时已收到用户“继续修复、不要启动服务器”的指令。
本轮仅交接核实后的原默认实例，保存其EXE／启动身份、工作目录、配置和私有环境供恢复；没有修改其他工作区。
当前运行二进制为`0.2.10+91df1c2f110fd4b935690eff96499332e3110195`，
频道7001、Launcher18080、GM18081，UDP0；直接启用主干脚本目录，游戏端口从主干etc派生。
祭坛验收选普通11频道（游戏10011），公开频道地址为`127.0.0.1:7001`，须与实际频道监听一致。
Launcher／GM ready、GM页面200；实际PID与监听以就绪文件为准，不能直接复用文档中的历史PID。

`Channel.UseScriptCatalog=true`，`Channel.ScriptPath`直接引用
`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/Resources/Channel/channel_info.etc`，
不修改其内容或编码，不另写频道etc，不维护手工频道数组。
主干源文件和主干运行副本SHA256均为`C1407121715461DA1C92E7A6ECC4DF52DA85C07CAB233DD4DDEC10D83230F338`。
使用已有`IncludePvpChannels=false`，普通／非竞技频道16条；竞技频道10050等端口被Codex旧TCP连接占用，
完整竞技列表会导致整组Game监听失败，未停止Codex或修改端口来抢占。
当前16个游戏监听加频道与HTTP共19个TCP监听已核对。竞技频道恢复须先解决端口占用并重新核验。

线级探针验证了握手、ASCII版本66、脚本和Info响应：脚本解密后4760字节与主干etc逐字节一致，
Info含16个正确名称／地址／端口。证据为`channel-wire-verification.json`；不据此宣称客户端界面已通过。
默认端口切换必须同步`LauncherAuth.PublicChannel.Port`，不能只改HTTP与TCP监听；
旧值25001会使登录器仍把客户端导向隔离端口，即使默认HTTP健康返回ready也看不到频道列表。

本轮目录为`artifacts/guardian-altar/default-20261028T070017Z/`，
`active-deployment.json`已指向该目录。原默认写入者停止后，通过SQLite只读源backup API
取得最新`player-before-switch.db`，包含已提交WAL；不恢复旧库或注入测试资产。
启动前后账号4、角色9、物品667、槽位659逐行哈希一致，integrity_check=ok。
`private/`保存前一实例的恢复输入，不提交；当前频道修正前的新备份为`player-before-pve-channel.db`，
最终核心资产不变证明为`live-data-check-main-channel.json`，当前监听与脚本证明为`readiness.json`。

当前玩家服务PVF哈希已不同于初版验证输入，保留现有文件，使用最新玩家库副本完成全网络关闭的兼容启动，
并针对当前PVF补核祭坛相关85项及内容2项，均0失败0跳过。当前输入哈希见README和就绪文件。
实际客户端`DNFClient-A21-protocol2/Launcher.ini`已为`http://127.0.0.1:18080/`，无需再次编辑。
用户须退出旧会话后重新登录，再从[第2步入图](acceptance.md)继续实机验证。

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

## 初始实机实例（证据快照）

2026-10-27 18:54（UTC+8）核验：PID22792，程序集`0.2.10+21c1dfd78f0e7866eb40e878305dcd019ea99f5e`。
运行目录为本工作区`artifacts/guardian-altar/live-20261027T105137Z/host/`，
`active-deployment.json`指向本轮状态目录，`running-process.json`与`readiness.json`记录精确EXE、参数、数据库及健康检查。
PID只作本轮快照，任何后续停止／重启都须重新核对，不能直接复用旧PID。

实际玩家库：`E:/Project/DNF/DNF-JP-86-Server/Server/src/Dfo.Server.Host/bin/Release/net10.0/Data/dfo-server.db`。
切换前新备份为本轮目录的`player-before-switch.db`，经SQLite只读源backup API取得。
新副本迁移通过，正式启动后integrity_check=ok，账号／角色／物品／槽位逐行哈希与备份相同。
4账号、9角色、658物品，其中5个角色达到50级；未改玩家等级、星数、次数或解锁。

当前PVF：`E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，SHA256
`3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B`。
客户端`DNFClient-A21-protocol/DNF.exe`与静态分析A21身份一致。

原马戏团ProbeHost（当前观察PID40848）使用
`E:/Project/DNF/Other/CircusProbe-20261027-fbd8/artifacts/circus-probe/default-probe.db`独立副本库和其他端口，
本轮保留该进程；主干玩家库由本祭坛Host单独使用。不要把马戏团副本库当作主干玩家库。

实机登录器：`E:/Project/DNF/DNFClient-A21-protocol/Dfo.Launcher.exe`。
同目录`Launcher.ini`仅将BaseUrl切为`http://127.0.0.1:25080/`；原INI备份在本轮状态目录`Launcher.ini.before`。
恢复INI前须检查是否已被其他操作修改，避免覆盖后续配置。没有改动客户端EXE、ClientHack或PVF。

用户实际使用的`E:/Project/DNF/DNFClient-A21-protocol2/Dfo.Launcher.exe`也已指向25080，
原INI单独备份为本轮状态目录`Launcher.protocol2.ini.before`。已运行的游戏连接不会随INI变化，须退出后重登。
入口灰色时先核实实际连接：本轮应为频道25001、游戏25011；7001／10011属于其他服务，不能据此判断祭坛活动是否开放。
本轮错服诊断的脱敏证据保存在`entry-connection-diagnosis.json`；用户已在正确服务上确认按钮可点击。

## 隔离端口候选（恢复产物）

修复运行目录使用`artifacts/guardian-altar/entry-fixed-20261027/host/`，
保留原初版和诊断候选产物；玩家库仍为上述主干实际玩家库。
本轮新备份为同级`player-before-switch.db`，使用SQLite只读源backup API取得，不能裸复制活动主文件。
实际启动完成以同级`readiness.json`为准；没有该就绪证据不能要求玩家开始完整验收。
该隔离候选使用25001／25011／25080／25081；当前默认端口实例以前文及active-deployment.json为准。
不得将两个候选并行启动为同一玩家库的写入者。

## 隔离候选连接端点

已核实：频道25001、游戏25011、Launcher HTTP 25080、GM HTTP 25081，均在25000–25999内。
`Channel.UseScriptCatalog=false`，只配置一个频道，发布配置完整替换频道及GamePorts数组；
禁用固定端口UDP发现。否则默认`10000+channel.Id`会覆盖手写GamePorts，或环境数组残项带回旧端口。
实际切换须核对所有配置源、继承环境、最终有效监听和启动日志；客户端登录器需指向候选频道／HTTP端点。
如统一协调要求复用默认端口，先明确释放时段，再重新生成并检查完整有效配置。

## 切换步骤

1. 协调当前玩家库的唯一验收时段、允许结束的准确进程及恢复目标；没有授权不结束任何已有服务。
2. 重新核实数据库、PVF、EXE、配置覆盖和端口；记录原服务提交、版本、环境及恢复命令，
   取得新的SQLite一致备份。不要裸复制活动主文件忽略WAL，也不要并行开启两个写同库的Host。
3. 在新备份副本复核迁移兼容性后，候选使用主干实际玩家库启动；保留原运行产物和配置。
   本轮正式运行数据库以当前实机实例节和readiness.json为准。
4. 核实实际PID、程序集0.2.10、PVF哈希、TCP／UDP与健康状态，提供用户明确连接方式。
5. 按[编号实机流程](acceptance.md)测试，记录真实结果。出现问题先定位失败层，再修复并重测。
6. 恢复服务时保留测试期间合法玩家进度；新表是加法迁移，不因回退程序删除表或直接恢复旧库覆盖新资产。
   只有明确需要数据回滚且已授权时才讨论恢复数据库。

用户实机通过之前不创建PR；云端明确通过和当前提交CI通过之前不合并。
