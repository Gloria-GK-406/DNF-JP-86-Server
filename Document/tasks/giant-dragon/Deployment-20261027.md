# 巨龙验收部署（2026-10-27）

2026-10-27 21:16（Asia/Shanghai）按用户新指示使用默认端口启动，A21玩家实机结果仍待反馈。用户本轮明确授权核实当前服务/数据库、备份并切换；本记录不表示用户验收通过。

## 当前验收入口

- 运行内容提交：`32ff430d30df437503d636ffe5c2b9481a09230c`，版本 `0.2.10`，分支 `codex/167-giant-dragon`。
- 当前启动PID：`45260`。运行目录为本工作区 `artifacts/giant-dragon/live-20261027-204411/host`；实际EXE、完整启动参数、程序集版本及监听见该目录上级 `live-process.json`。
- 客户端：`E:\Project\DNF\DNFClient-A21-protocol\Dfo.Launcher.exe`。Launcher.ini已指向 `http://127.0.0.1:18080/`。同次备份根有双击入口 `启动巨龙实测.bat`。先退出旧游戏及启动器，再启动此入口并使用已有账号。
- Channel TCP7001、Game TCP10011、Launcher HTTP18080、GM HTTP18081。GM入口为 [本地管理台](http://127.0.0.1:18081/gm)。`Channel:UseScriptCatalog=false`，单频道11，已核对真实监听；UDP发现开启，实际端口12311/12312，绑定探测响应已验证。先验单人，多人实机仍待验收。
- 原玩家库绝对路径：`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`。启动参数显式指定该路径，不是工作区空库或历史备份。
- 玩法开关：`GiantDragon.Enabled=true`、`RestoredVariantsEnabled=true`。

## 现役资源与备份

所有数据库、现役配置及完整运行产物存于本工作区的私有、gitignored目录：
`artifacts/giant-dragon/live-20261027-204411`。

1. `player-before.db`：在线SQLite backup API快照。
2. `player-at-switch.db`：停止最终确认的原Host后，再经SQLite backup API制作的最终一致快照；对应JSON保存行数及SHA-256。完整性检查为ok，112张表，4账号、9角色、658物品、650物品槽、15个活动任务。
3. `player-after-start.db`与`live-data-check.json`：启动候选后的一致快照与逐表双向EXCEPT对比。112张表未发现数据变化，完整性ok。这个结果只描述启动后核对时刻，不抹除后续正常玩家操作。
4. `rollback/host-current`：原守护祭坛修复候选 `0.2.10+91df1c2f` 的完整运行产物、配置及GM静态资源。原运行位置亦未改写。备份期间原服务从诊断版PID32864更换为PID47584；已确认该任务结束本轮部署后，重新核实并仅停止PID47584。另存诊断版备份 `rollback/host`。
5. `rollback/Script.pvf`、`rollback/Launcher.ini`：本次客户端修改前原件。原现役PVF SHA为 `3822A9ED183CD17F74DFEE9C11B8F2E84AF596B23972A737846022148FB05C6B`。

与研究D45源的31项巨龙原生输入逐项比对相同，记录在 `pvf-source-audit.json`。基于现役3822源生成恢复包，保留其他补丁。只更改原定4个文件，恢复16项任务，其他712006个文件条目保持不变；读取回验见资源旁JSON。

本次客户端与服务端PVF均为：
`01F1BE685D46647961DF65207A36304777DB20EF5A3D03C68EFAA92F8B0C5189`。
服务端指向本次私有运行根的 `Script-giant-dragon-active.pvf`；客户端装入同一份恢复包。protocol2目录仅在后续连接排查中将Launcher.ini的旧25080地址备份并改为18080，未替换该目录PVF；恢复入口验收仍使用本页protocol测试目录。候选最初构建缺少GM静态文件，已补入当前服务原有wwwroot；GM首页及其JS/CSS均返回200。

马戏团独立探针PID40848使用另一份库和默认端口；本次未停止、改写或复用。

## 本次验证

- 新现役恢复包聚焦回归：81通过，0失败/跳过，见 `active-resource-tests.log` 与 `active-resource.trx`。
- 切换前在 `compatibility.db` 备份副本启动候选：Launcher/GM及资源就绪，112表结构与数据未变，检查Host已停止。
- 实际玩家库启动：Launcher bootstrap ready、GM health ready、GM首页和引用资源200；实际4个TCP及2个UDP监听与预期一致，两个UDP端点响应已核验。
- 客户端安装PVF与服务端包SHA相同，Launcher配置指向18080。
- 既有全量回归及其2项异界基线失败、18项文档基线差异保留在 [Verification.md](Verification.md)，不因部署成功改写成全绿。
- 默认端口切换前另存当前数据库快照与配置，见 `default-backup.log` 及 `appsettings-before-default.json`；当前身份见 `default-process.json`。TCP与UDP分别检查占用，NVIDIA Overlay使用的UDP10011保持不动。
- 正常登录、入口可达、真实战斗、计时、机关、奖励、退出/重登与跨日仍需按 [实机清单](Human-Test-List.md) 验证。

## 第一轮测试

1. 正常退出旧游戏和启动器，双击本次 `启动巨龙实测.bat`，登录已有账号，选择84级以上角色；先记录疲劳/金币/材料。
2. 从素喃工坊左侧通道进入月溪镇，检查两组月轮山选图；先测试普通世界图30的“纳特拉的复仇”（3200），再测“双子巨人的背叛”（3201）、“圣地：龙之魂”（3202）。恢复世界图98有同名变体，以日志DGN ID区分。
3. 每张普通图确认出生房怪物、走门/回访、Boss及机关、掉落/翻牌；普通三图仅入场扣8疲劳，切房不再扣。回城正常退出重登，核对资产和疲劳。
4. 普通三图通过后推进NPC20008的恢复任务链。3536需完成20720，3537需完成20722；3538允许20728进行中，消耗任务栏10002520；3539需完成20728并消耗任务栏10002519。两人通关目标先保留到多人验收。
5. 任一入口不可达、拒绝、黑屏或Boss卡住，反馈测试步骤、图名、角色和大概时间/截图，保留现场，不用重置玩家库。若条件不够则先反馈，未经用户指定不改角色资产或前置任务。

## 恢复方式

需要恢复服务时，先让玩家正常退出，核实当前PID和EXE仍属于本次候选，再停止它；确认客户端PVF仍是本次01F1哈希后，用 `rollback/Script.pvf`、`rollback/Launcher.ini` 还原本次客户端改动。按 `switch-inputs.json` 中PreviousExe和PreviousArguments重新启动原 `91df1c2f` Host，核对25001/25011/25080/25081及ready。

服务回退默认仍使用同一份当前玩家库，以保留正常测试所得进度，不自动覆盖为旧备份。只有需要回滚玩家数据且用户另行授权时，才在所有写入者停止后恢复一致快照。不要直接复制主文件忽略WAL，也不要按本页历史PID停止后来属于其他程序的进程。
