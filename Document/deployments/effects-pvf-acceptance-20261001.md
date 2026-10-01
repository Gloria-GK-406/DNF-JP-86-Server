---
kind: rks-verification
decision: pass
---
# effect / PVF 重构验收版本部署

用户授权：“你来部署吧”。本次为后续独立部署操作，不改变已封存的两份重构实现结论。

- 部署源码：`a0e916c0bdbaeeac153a87c5328bc6ea15d03e49`，分支 `task/solo-play-roadmap`。
- 现场就绪确认：2026-10-01T18:26:06.906540+08:00（北京时间）。
- 正式运行目录：`E:\Project\DNF\DNF-JP-86-Server\artifacts\effects-pvf-acceptance-20261001\runtime`；PID3336。
- 旧运行目录：`E:\Project\DNF\DNF-JP-86-Server\artifacts\amplification-crystal-fix-20261001\runtime`；旧PID44296已停止并保留文件。
- 数据库：`E:\Project\DNF\DNF-JP-86-Server\Server\src\Dfo.Server.Host\bin\Release\net10.0\Data\dfo-server.db`；正式切换前通过SQLite backup API备份至artifact目录 `pre-deploy.db`，quick_check=ok。
- PVF：`E:\Project\DNF\DNFClient-A21-protocol\Script.pvf`；SHA256 `3b2fe3da115fee949282558a864a319fc9ca197e1d1f22e656e74316a0f04b06`。

## 部署与验证

以 `dotnet publish Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj -c Release --no-build --no-restore` 从已验证Release结果发布到新目录；保留原appsettings、experience.json、Resources和数据库/PVF绝对路径。所有发布DLL与已验证Release结果哈希一致。

切换前确认无DNF客户端及旧服务已建立连接。使用一致数据库副本、独立端口27001/20010/20011/28080且关闭UDP发现完成smoke，HTTP200/ready；全部原表数据不变，随后停止smoke。再次确认无客户端与连接后停止旧服务，取得新鲜正式备份并启动新服务。

正式API `http://127.0.0.1:18080/api/launcher/v1/bootstrap` 返回HTTP200、status ready；7001、10010、10011、18080全部属于新PID3336。新启动日志确认使用目标运行目录、原正式PVF和118479条装备索引；stderr为空。正式DB quick_check=ok，48张表的排序行内容摘要与正式备份完全一致；PVF/appsettings哈希不变。

原始证据全部保留在 `E:/Project/DNF/DNF-JP-86-Server/artifacts/effects-pvf-acceptance-20261001/`：publish.log、preparation.json、pre-deploy.db、pre/post-deploy-tables.json、deployment.json、listeners.json、stdout/stderr.log和smoke日志/副本。配置及数据备份仅本地保留。

## 实机验收

[24项验收表](../tasks/item-effects-unification/human-test-list.md)仍全部HumanPending。只确认部署和启动健康，不升级为ClientVerified；固定等级强化/增幅券仍在原契约范围外。玩家资产、客户端与PVF内容均未改，按原客户端入口登录后从H01开始。

本次源代码无新修改，只有部署记录和验收表状态更新；其独立文档提交身份由操作结束后返回，不在记录中回填自身SHA。任务分支与工作区保留。
