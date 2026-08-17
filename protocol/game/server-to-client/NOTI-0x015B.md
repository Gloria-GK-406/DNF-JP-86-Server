# NOTI `0x015B` `USER_APC_INFO_TOD`

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
| Type | `0x015B` | 同上 |
| 名称 | `USER_APC_INFO_TOD` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 518   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 塔层数 layer：上游对同一次 START_MAP 连发两包，第一包写 0（基准层），第二包写当前层数 | Confirmed | 上游 Network/Builders/Dungeon/TowerOfDespairApcInfoBuilder.cs:39 BuildBody 首字节 WriteByte(layer)；:31-32 分别以 0 与 (byte)floor 调用；发包点 Network/Handlers/Dungeon/DungeonMapHandler.cs:592-599，取自 `[ebp - 0xa30]` |
| 1 | 255 | `dstringw` | 角色名 player.Name（dstr） | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:40 WriteDstr(player.Name)，取自 `[ebp - 0x410]` |
| 256 | 1 | `u8` | 角色等级 player.Level | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:41 WriteByte(player.Level)，取自 `[ebp - 0xa1f]` |
| 257 | 1 | `u8` | 职业 player.Job | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:42 WriteByte(player.Job)，取自 `[ebp - 0xa1e]` |
| 258 | 1 | `u8` | 转职/成长类型 player.GrowType | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:43 WriteByte(player.GrowType)，取自 `[ebp - 0xa20]` |
| 259 | 255 | `dstringw` | 公会名（dstr）；上游恒写空串 | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:44 WriteDstr(Array.Empty<byte>()) 并注释 guild name，取自 `[ebp - 0x610]` |
| 514 | 4 | `u32` | 公会 id；上游恒写 0 | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:45 WriteInt32(0) 并注释 guild id，取自 `[ebp - 0xa30]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x005FFBA0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
