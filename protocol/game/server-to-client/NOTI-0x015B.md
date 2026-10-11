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
已读出字段合计 = 8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 塔层数 layer：上游对同一次 START_MAP 连发两包，第一包写 0（基准层），第二包写当前层数 | Confirmed | 上游 Network/Builders/Dungeon/TowerOfDespairApcInfoBuilder.cs:39 BuildBody 首字节 WriteByte(layer)；:31-32 分别以 0 与 (byte)floor 调用；发包点 Network/Handlers/Dungeon/DungeonMapHandler.cs:592-599，取自 `[ebp - 0xa74]` |
| 1 | 1 | `u8` | 角色名 player.Name（dstr） | Confirmed | 上游 TowerOfDespairApcInfoBuilder.cs:40 WriteDstr(player.Name)，取自 `[ebp - 0xa63]` |
| 2 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xa62]` |
| 3 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xa64]` |
| 4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xa74]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00A14EC0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：以下为当前人工核对布局，自动生成区保留原始提取结果；阅读完整布局以本节为准。 -->

# NOTI 0x015B — 绝望之塔动态 APC 资料

日期：2026-10-11。证据：ClientStatic / High（A21 RVA 0xA14EC0：层号、Dstr、职业等级前缀）；UpstreamReference / High（ServerS4A21 c20dba5f TowerOfDespairApcInfoBuilder）。完整尾部参考上游，尚无本次 WireCaptured / ClientVerified。

| 字段 | 类型 | 含义 |
|---|---|---|
| layer | u8 | 0 为基础资料；随后发送当前动态层号 |
| name | i32 长度 + bytes | 角色名 |
| level, job, growType | 3 × u8 | 等级、职业、转职 |
| guildName | Dstr | 当前空串 |
| guildId | i32 | 当前 0 |
| equipmentAndAvatarIds | 22 × i32 | 前十位置为装扮 0..9，第十索引为武器（A21 worn slot 12）；剩余为 0 |
| creatureName | Dstr | 当前空串（背包投影无宠物昵称） |
| creatureItemId | u32 | 当前穿戴宠物物品 ID，无则 0 |

CurrentImplementation：进入 10、20、…、90 层后在 START_MAP 后发送 layer 0 和当前层两个通知。外观使用现有 DrawnAppearance 显示映射，含透明装扮继承。此消息不授予客户端跳层权限。动态 APC 外观需实际客户端验收。
