# A21 `CMD 0x0012 DELETE_ITEM` 实机验证（2026-09-10）

## 环境

- 服务端代码：`main`，提交 `ed6d02a2a30554bc262664ddfa89fdad1cbfec42`。
- 运行产物：`Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll`。
- 数据库：显式连接原 Debug 角色数据库；验证前角色 1 有 72 个已占用物品槽。
- 源日志：`Server/src/Dfo.Server.Host/bin/Release/net10.0/Logs/server-20260910-101953.stdout.log`。

## 可观察结果

用户实机确认两次删除均正常完成。服务端日志记录：

```text
140 Command DeleteItemCommand handled by DeleteItemCommandHandler as Transactional ... in 14.6203 ms
142 Discard character=1 container=Main slot=9 item=27312 count=1
144 Command DeleteItemCommand handled by DeleteItemCommandHandler as Transactional ... in 1.3336 ms
146 Discard character=1 container=Main slot=65 item=10007476 count=1
152 Command AcknowledgeGameExitCommand handled by AcknowledgeGameExitCommandHandler as Direct ...
```

两次请求分别覆盖普通装备清槽与堆叠物整组清槽。退出后对正在使用的 SQLite 数据库只读核对：

- 角色 1 的已占用物品槽从 72 降为 70；
- `list_type=0, slot_index=9` 无行；
- `list_type=0, slot_index=65` 无行；
- 角色等级、金币等无关状态保持为 Lv85、66332。

运行实例在核对时仍监听 7001、10010、10011，标准错误为空；删除路径没有拒绝或异常。

## 验收结论

19 字节、`entryCount=1`、`operationType=1` 的 A21 普通主背包删除路径满足双向实机验收，
状态升级为 `ClientVerified`。18 字节 `operationType=0` 路径、批量、时装、宠物、穿戴栏与收集箱
仍在实现边界外，本次结论不覆盖这些分支。
