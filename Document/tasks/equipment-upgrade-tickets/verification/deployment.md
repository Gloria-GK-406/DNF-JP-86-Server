# 固定等级券部署
2026-10-01；基准提交c8f25b6ce2cd6bce01a00fe672405788c9d69641，候选源码指纹见source-fingerprint.json。

- 发布目录：E:/Project/DNF/DNF-JP-86-Server/artifacts/equipment-upgrade-tickets-20261001/runtime。
- 发布Dfo*.dll逐项匹配本次通过的Release构建；原appsettings、experience.json、Resources复制到新目录。
- 一致SQLite副本smoke.db与独立27001/20010/20011/28080端口、禁用UDP完成HTTP200/ready启动检查；48表行内容摘要与副本启动前一致。smoke PID29368已停止。
- 切换前确认无DNF客户端及旧服务游戏连接，核对可执行路径后停止旧PID3336，原runtime保留。
- 通过SQLite backup API保存正式pre-deploy.db，quick_check=ok；启动新PID15484。
- 正式API http://127.0.0.1:18080/api/launcher/v1/bootstrap 返回HTTP200、status ready；7001/10010/10011/18080全部属于PID15484。
- 正式启动后的48表排序行内容摘要与切换前完全一致。四类券仍各3张，7804/10007840保护各2张；没有资产发放或修改。PVF及正式配置SHA256保持不变，stderr为空、stdout无fail级别消息。
- 原始部署日志、数据库备份与摘要留在上述artifact目录；DLL/config/PVF和源码指纹见deployment-evidence.json，正式listener证据见artifact/listeners.json。

客户端/PVF无需替换。立即界面及重登表现仍由T01—T04人工验收；部署健康不升级为ClientVerified。接受提交身份由最终Git结果返回，含有本文件的提交不会在本文件自引用。
