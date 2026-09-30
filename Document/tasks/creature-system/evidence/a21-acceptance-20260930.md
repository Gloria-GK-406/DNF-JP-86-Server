# A21 宠物实机验收与孵化请求 — 2026-09-30

## 来源与判断

- 用户在本会话明确反馈 P01–P09 全部验收通过，P10 已点击发送。按 [Human Test List](../Human-Test-List.md) 的普通路径记录 `ClientVerified`，不延伸到未测的失败、多人、特殊成长或主动技能路径。
- 当前部署为 `creature-login-fix-20260930/runtime`，构建代码对应 `c4b283fa`；服务 PID25932 仍运行。本次不重启服务、不写数据库。
- 服务日志来源：`E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-login-fix-20260930/server.stdout.log`（GB18030）。保存的 [原始摘录](a21-acceptance-20260930.log) 为 UTF-8 转码，保留内容和源行号；SHA-256：`d28e0162aa258b13c245e5322110e70b37ce52c5acd90927495939c4eb4bb176`。
- 本次读取日志共3768行，`fail:` 为0，stderr为0字节；存在未处理请求等警告，不能称全部协议无错误。客户端 FrameCapture 尚无实际输出文件，以下请求是**服务端解帧后完整 body 日志**，不是客户端原始加密帧。

## P01–P09 已接受范围

登录详情、移动换宠、宠物装备及分区整理、重登保存、改名、普通地下城经验/饱食、手动喂食、城镇在线恢复、离线不累计，均按用户报告通过。P08/P09 的具体时间与前后数值没有独立快照，用户验收是其玩家可见行为证据。

日志支持：13:27:46 改名事务处理并发送 NOTI0065、CMD0064；本次有12条普通宠物经验通知；13:31:24 使用模板24的饲料，请求 CMD002C 的完整 body 为 `BD0007970200001800000000000000`，服务记载槽189剩余119，随后发送响应与宠物状态。

实际 SQLite 只读核验（角色2/1123；`quick_check=ok`）：

| 对象 | 当前值 |
|---|---|
| 穿戴宠物 | list3/slot25，UID950，template63010，core键950 |
| 成长与名字 | level4、exp12、饱食99；名字 UTF-8 字节 `E6B58BE8AF95E5908DE5AD97`（测试名字） |
| 其余普通宠物 | UID948/template63000/list7槽17；UID949/template63003/list7槽40；core键均同UID |
| 饲料/改名卡 | list7槽189/template24/count119；槽190/template25/count9；启动前分别120/10 |
| 两枚蛋 | UID959/list7槽0 与 UID960/list7槽3，均template63022、同UID core键、level1/exp0/饱食100 |

快照说明当前持久化状态，不单独证明每步的全部中间行为。装备交接中的两项真实PVF `CreatureGoesBackOnTests` 失败仍待定位，P02实机通过不替代其修复。

## P10 实际请求

```text
2026-09-30 13:47:07.423 +08:00 warn: Dfo.Server.Networking.Runtime.GameNetworkPlugin[0]
      Unhandled Game frame Game:03-00-00-00: selector=0x01 type=0x0066 seq=31 body(3B)=070000
```

- `WireCaptured / Confirmed`：本次蛋右键确认实际发送 Game CMD `0x0066 HATCH_CREATURE`，完整 body 长3字节，内容 `07 00 00`。
- `High`：结合当前 list7槽0 的蛋及 [70请求读取证据](tw70.md)，候选布局为 `u8 itemSpace=7, i16LE slot=0`。单个零槽样本不能独立证明槽字段符号或所有取值；静态构包点仍未找到。
- `CurrentImplementation / Confirmed`：服务将请求记为 Unhandled，未走已有孵化领域动作；只读数据库中两枚蛋仍63022。故 **P10请求取证完成，孵化功能尚未通过**。不能把同号 S2C NOTI0066 的经验通知当作孵化响应。
- 下一步严格接入该请求的网络入口、命令事务及成功/失败响应和库存/宠物投影，再验收63022→63023、同UID同槽、初始状态及重登保存。此次无需用户重复发送。

完整宠物任务仍进行中；普通进化及其他条件项、特殊成长和技能等边界见验收列表。
