# NOTI `0x021B` `CREATE_EXPERT_JOB_STORE`

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
| Type | `0x021B` | 同上 |
| 名称 | `CREATE_EXPERT_JOB_STORE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 271 + n×1   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 摊位类型 kind：0=拆解机(DisjointMachine)，3=附魔店(EnchantShop)；只有 kind=3 才写出尾部计数与重复组 | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:31 WriteByte((byte)store.Kind) 为 body 首字节；ExpertJobStoreKind.cs:5-6 定义 0/3；分支见同文件 40-51，取自 `[ebp - 0x22c]` |
| 1 | 2 | `u16` | 摊主 UserId（u16）：与 CLOSE 通知 NOTI 0x021C 用的是同一个 id | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:32 WriteUInt16(store.OwnerUserId)；同文件 58 关摊通知写同一字段，取自 `[ebp - 0x21c]` |
| 3 | 256 | `dstringw` | 摊位名（dstr = int32 长度 + 原始字节）：内容原样来自 CREATE 命令里的名字字节，服务端不做转码 | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:33 WriteDstr(store.NameBytes)；GamePacketWriter.cs:59-64 定义 dstr；NameBytes 源头见 CreateExpertJobStoreRequest.cs:22-30，取自 `[ebp - 0x210]` |
| 259 | 1 | `u8` | 摊位所在城镇 id TownId（u8） | High | 上游 ExpertJobStorePacketBuilder.cs:34 WriteByte(store.TownId)，位于变长 name dstr 之后，按写序与提取到的字段逐一对上（提取层把 dstr 按 256 字节定长记账，故绝对偏移是记账值而非实测值），取自 `[ebp - 0x213]` |
| 260 | 1 | `u8` | 摊位所在区域 id AreaId（u8） | High | 上游 ExpertJobStorePacketBuilder.cs:35 WriteByte(store.AreaId)，name dstr 之后按写序第 2 个字段，取自 `[ebp - 0x212]` |
| 261 | 2 | `u16` | 摊位坐标 X（int16） | High | 上游 ExpertJobStorePacketBuilder.cs:36 WriteInt16(store.PositionX)，name dstr 之后按写序第 3 个字段，取自 `[ebp - 0x224]` |
| 263 | 2 | `u16` | 摊位坐标 Y（int16） | High | 上游 ExpertJobStorePacketBuilder.cs:37 WriteInt16(store.PositionY)，name dstr 之后按写序第 4 个字段，取自 `[ebp - 0x228]` |
| 265 | 4 | `u32` | 摊位使用费（金币，int32） | High | 上游 ExpertJobStorePacketBuilder.cs:38 WriteInt32(store.Cost)，name dstr 之后按写序第 5 个字段；费用用途见 DisjointMachineService.cs:95-96，取自 `[ebp - 0x22c]` |
| 269 | 1 | `u8` | 常量 1（上游命名 PlayerAttachedStoreMode）：builder 恒写 1，不随运行时数据变化 | High | 上游 ExpertJobStorePacketBuilder.cs:39 WriteByte(PlayerAttachedStoreMode)，常量定义在同文件 8；name dstr 之后按写序第 6 个字段，取自 `[ebp - 0x214]` |
| 270 | 1 | `u8` | 后续重复组的元素个数 n（u8）：仅当 kind=3（附魔店）时才写出，kind=0 时 body 到上一字段为止 | High | 上游 ExpertJobStorePacketBuilder.cs:40-44 仅在 store.Kind == EnchantShop 分支里 WriteByte(CardQualificationLevels.Count)，随后 45-50 按该计数循环，取自 `[ebp - 0x211]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | 单个元素：一个附魔卡资格等级（u8），整组即该附魔师当前可处理的资格等级表 | High | 上游 ExpertJobStorePacketBuilder.cs:45-50 按计数循环 WriteByte(store.Enchanter.CardQualificationLevels[index])；该表由专家职业经验换算而来（EnchanterConfigProvider.cs:95 GetCardQualificationLevels）；用途见 EnchanterStoreUseService.cs:47 校验配方资格，取自 `[ebp - 0x216]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0093BE80` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
