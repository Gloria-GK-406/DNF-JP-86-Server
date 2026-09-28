# A21 精灵秘药对照采样准备（2026-09-28）

## 当前部署与范围

- 功能分支：`task/experience-premium-refactor`。
- 仅在 `MonsterDeathDecoder` 和 `UseStackableDecoder` 增加可关闭的 Trace 完整 body 十六进制日志，包含帧元数据；不修改经验算法或协议解码结果。
- 开关为对应解码器的日志类别 Trace；默认设置下不会生成完整报文日志。
- 本次部署复用 `artifacts/experience70-20260928/runtime`，日志写入 `artifacts/spirit-potion-capture-20260928/server.stdout.log`。启动 PID 38536。
- 数据库仍使用现有 Host Release 目录的 `Data/dfo-server.db`，已离线备份并给角色2（1123）在Main槽71放入3瓶7377。备份及发放明细见采样 artifacts 目录。
- 服务端内容仍读取 protocol/Script.pvf；protocol2客户端已单独移除7377的 `impossible contents/enable` 禁用项。服务端目前没有实现该禁用项校验，也没有精灵秘药效果/倍率状态。

## 实机顺序

同一个角色、同一个地下城：先不使用秘药击杀3只普通怪；使用1瓶7377；再击杀3只尽量同类型的普通怪。此操作尚待用户执行，不得将准备完成当作字段差分完成。

使用 `extract_capture.py SERVER_LOG OUTPUT_JSONL` 提取帧。保留完整body，按变量记录数定位后缀；未知字段使用中性名称。脚本的前后阶段按首次观察到7377使用请求区分，必须结合服务端成功日志确认药物使用被接受；阶段标签本身不是药效成立证据。

## 验证

- MonsterDeath及UseStackable相关测试：64通过，0失败。
- Release发布成功，HTTP 18080、Channel 7001、Game 10010/10011开始监听。
- 尚无本轮完整实帧，不能宣称原始未知字段没有变化。
- 70端证据由同目录findings.md单独记录，不将70字段偏移套用到A21。
