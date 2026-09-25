---
kind: rks-verification
decision: manual_required
---
# 打开邮箱接入准备

2026-09-24用户以000打开邮箱，CMD0060 seq28为0字节body。取证日志摘录已保存。本次改变仅将已有严格空体decoder和打开handler接入网络注册；核心查询、ACK再列表顺序不变。

回归先加入0060注册测试，未接入时失败；接入后Protocol全量1108通过，真实SQLite邮箱协议集成5通过。Release发布成功，git diff --check通过。见test-open-Protocol.log、test-open-Application.log及publish-open.log。

发布包artifacts/mailbox-open-20260924/runtime，复制当前配置和资源，Database.Path继续指向原主干SQLite。当前运行仍为mailbox-protocol-20260924进程19216，检测到10010在线连接，已向用户请求正常退出后再重启，尚未部署启用。用户需确认空邮箱UI可正常打开；后续发送请求仍需采集，不提前标记整个任务完成。

## 部署完成

用户回复“我退出了”后复查游戏端口无Established连接，停止已核对路径的旧进程19216。SQLite backup API备份至artifacts/mailbox-open-20260924/before-open-20260924-210749.db，quick_check=ok，邮箱数量0。原数据库路径保持不变，未修改角色资产。

新进程40116启动成功，ContentRoot=artifacts/mailbox-open-20260924/runtime；7001、10010、10011、18080均由该进程监听，stderr为空。回滚二进制保留于artifacts/mailbox-protocol-20260924/runtime，勿自动回滚数据库。下一步由用户以000打开邮箱确认UI；发送部分仍待取样。
