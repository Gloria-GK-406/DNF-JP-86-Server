# A21 返回选人实机验证（2026-09-08）

实现：`94a243f6`；重启记录：`2f12e447`；Host PID `47028`。
客户端 SHA-256：`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。

用户在部署后测试“进入角色 → 选择频道 → 返回选人 → 再次进入”，反馈：
“ok，测试看起来没问题。”
服务端记录到同次运行中的两次返回完成、其后各一次重新选人发布；本次日志快照没有 Unhandled Game frame 或 fail 级别记录。

## 日志证据

源：`Server/src/Dfo.Server.Host/bin/Debug/net10.0/Logs/server-20260908-232038.stdout.log`。
快照 SHA-256：`164493f0b3b5bcc4506f68e1ac5ac93d24abf22af2c7cce89c7f997fb324457c`。
以下为源文件原行号与原始命令记录，保持先后顺序：

```text
38: Command PrepareSelectedCharacterSessionCommand handled by PrepareSelectedCharacterSessionCommandHandler as Transactional with execution 3f8fe903-36f5-49ea-8b83-91c2069c8569 in 852.6168 ms
42: Command PublishSelectedCharacterSessionCommand handled by PublishSelectedCharacterSessionCommandHandler as Workflow with execution cf3752db-ea13-4b26-993e-e08d2abc4167 in 3.0469 ms
72: Command PrepareReturnToSelectionCommand handled by PrepareReturnToSelectionCommandHandler as Transactional with execution 03cb18d3-97f0-4615-85f6-662a3496d083 in 2.2358 ms
74: Command CompleteReturnToSelectionCommand handled by CompleteReturnToSelectionCommandHandler as Workflow with execution 31344c45-6565-4ac4-b38c-835ad5e64407 in 7.3649 ms
82: Command PrepareSelectedCharacterSessionCommand handled by PrepareSelectedCharacterSessionCommandHandler as Transactional with execution 5ca5a4e5-5fab-4338-9f38-776e2248ddcd in 5.5745 ms
86: Command PublishSelectedCharacterSessionCommand handled by PublishSelectedCharacterSessionCommandHandler as Workflow with execution a08441c5-15e5-4eae-b807-1c71e7d4c982 in 0.1265 ms
106: Command PrepareReturnToSelectionCommand handled by PrepareReturnToSelectionCommandHandler as Transactional with execution 5c70306d-c35f-4d78-836c-bda45bc7829a in 0.486 ms
108: Command CompleteReturnToSelectionCommand handled by CompleteReturnToSelectionCommandHandler as Workflow with execution ccc9a332-9b7e-4ad8-a1dc-25b269dc7b4b in 0.3888 ms
116: Command PrepareSelectedCharacterSessionCommand handled by PrepareSelectedCharacterSessionCommandHandler as Transactional with execution 2a39a2e8-1182-4dd9-80e1-f29e24d4a693 in 3.4517 ms
120: Command PublishSelectedCharacterSessionCommand handled by PublishSelectedCharacterSessionCommandHandler as Workflow with execution 848ecbe3-9ab9-4a8c-98bb-09976064e79a in 0.0357 ms
```

## 结论与范围

- Confirmed：当前客户端可完成返回选人并再次进入，连续两轮通过；`0x0007` 本次交换标记“双向验证结束（A21）”。
- 此版本按实现发送 `01` 成功信封、名单及荣誉，守护者另先发空盾牌栏；本次没有单独归档完整出站字节流。
  具体发送顺序的逐帧断言来自自动测试，远端顺序来源仍保留于流程文档，不能将它们冒充此次实机抓包。
- 没有逐项差分验证名单每个外观字段，也没有验证所有职业、跨频道分支或异常失败码；用户未报告这些方面的问题不等于已逐项验证。

[协议](../../../game/client-to-server/0x0007.md) · [流程](../../flow-return-selection.md)
