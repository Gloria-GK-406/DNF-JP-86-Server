# NOTI `0x02F7` `CHARAC_DUNGEON_LICENSE_INFO`

<!-- rks:machine:begin -->

> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**
> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。
> 本区之外的一切内容属于人工区，重新生成不会触碰。

## 身份

| 项 | 值 | 来源 |
|---|---|---|
| Endpoint | Game | 帧协议基线 |
| Direction | Server → Client | 接收注册表普查 |
| Command 族 | `0x00`（NOTI） | 客户端两张注册表的分表证据 |
| Type | `0x02F7` | 同上 |
| 名称 | `CHARAC_DUNGEON_LICENSE_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 2 + n×12   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x20]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x40]` |
| &nbsp;&nbsp;↳&nbsp;4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x3c]` |
| &nbsp;&nbsp;↳&nbsp;8 | 4 | `u32` | 当前星级通关累计，供下一星达成条件显示 | High | A21 VA00551080匹配地下城、00551099读取记录+8并按ETC Field5-1封顶；Document/tasks/darkelf-ruins/evidence/client-clear-condition.txt；用户2026-10-30选图截图，取自 `[ebp - 0x44]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D575D0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 当前实现

本仓发送 `u16 count + count*(dungeonId:u32, license:u32, clears:u32)`，三个区域共享星级与累计，各发送当前星级对应的一张DGN。

ClientStatic / High：接收入口 VA011575D0 将三元组交给004C0DF0，保存到遗迹管理器的记录表。
原生条件窗口00550F80按当前DGN查找对应记录，在00551099读取记录+8，按静态目录Field5减1封顶；
二星目录Field5为4，所以显示分母3。00551381将该计数和需求交给dstr109A1格式化。
有界原始指令及当前客户端只读缓存核对见[计数证据](../../../Document/tasks/darkelf-ruins/evidence/client-clear-condition.txt)。

ClientObserved：用户2026-10-30截图为选图右上角“3星达成条件：2星地下城通关3次（0/3）”，
服务器真实累计非零，原生缓存第三字段为0。见[原截图](../../../Document/tasks/darkelf-ruins/evidence/clear-condition-before.png)。
CurrentImplementation：第三字段改为已持久化的当前星级通关累计，不增加或重算累计；已有选图及结果阶段均复用同一编码。
用户2026-10-30反馈修正后显示2/3，计数字段取得限定界面确认；没有据此声称已获得完整WireCaptured或升星流程ClientVerified。首次选图缓存的登录预热与证据见[流程](../../patterns/flow-darkelf-ruins.md)。
