# 商城装扮失败与黑钻无反馈修复

2026-09-09 用户实机反馈：装扮选期限/属性后购买失败，宠物蛋成功，点击黑钻没有反馈，随后无法关闭商城。

## 修复结果

装扮商品 630019 的目录标记 1 被误当成普通商品付款列并拒绝。现在按装扮自己的价格档位读取金额与期限，仅接受当前档案中已观察到的 0/1 标记，未知标记仍拒绝。630018/630019/630020 的 7 天、30 天、永久与不同选择属性均通过真实 PVF + SQLite 持久化测试。

黑钻按钮发送的 CMD0041 原先未处理。现在返回失败信封，再以 NOTI01F7 显示“黑钻网页服务暂未开放。”，不生成虚假网页票据。黑钻权益服务仍未实现；真实客户端能否显示提示并正常关闭商城，需要用户复测。

实现路径：`PvfCeraShopDefinitionSource.cs`、`Protocol/Game/GenCeraTicket/GenCeraTicketSlice.cs`、共用的 `Protocol/Game/GameServerNotice.cs` 及注册；原有入场拒绝通知复用同一编码，字节格式保持一致。

协议指令、PVF 身份、实机日志来源与置信度见 [取证记录](../protocol/patterns/data/traces/a21-cera-feedback-20260909.md)。宠物蛋成功有用户反馈与服务端提交日志，但这次无完整双向抓包，不把整条购买协议升级为 ClientVerified。

## 验证

原始证据目录：[Verification/CeraShop-Feedback-20260909](Verification/CeraShop-Feedback-20260909)。

- 修复前：三项真实档案装扮测试失败；黑钻 TCP 用例因无应答超时。
- 定向回归：装扮购买、价格解析、持久化、协议与入场通知回归通过。
- 完整回归：设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 后，执行 `dotnet test Server/86JP.Server.slnx -c Debug --logger trx --results-directory Document/Verification/CeraShop-Feedback-20260909/full`。逐项统计 UnitTestResult：2660 Passed、0 Failed、2 NotExecuted。两个跳过项是已有的“缺少档案”测试，在本次提供档案时按条件跳过。
- Release Host 构建：0 警告、0 错误。
- 新运行实例 PID41684，启动于 19:42:35，监听 7001/10010/10011；标准错误日志为空。使用原玩家数据库与同一 PVF。
- 新实例端口10011在线烟测：CMD0041 得到 `00 01`、NOTI01F7 的完整中文提示，随后 EXIT 得到 `01`。只使用未登录连接，不执行玩家购买。结果见 [live-smoke.json](Verification/CeraShop-Feedback-20260909/live-smoke.json)。此检查证明部署版本已提供新应答，不代替客户端 UI 测试。

## 运行数据与复测

重启前通过 SQLite backup API 保存一致备份并通过 integrity_check：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Data/backups/before-cera-feedback-fix-20260909-194219.db`。未重置玩家余额或移除已购宠物蛋。当前运行日志：`Server/src/Dfo.Server.Host/bin/Release/net10.0/Logs/server-20260909-194235.stdout.log`。

重新进入客户端后，复测该骑士装扮的期限与属性购买；再点击黑钻，确认出现不可用提示，并在关闭提示后尝试关闭商城。客户端 UI 的这两项结果仍待反馈。
