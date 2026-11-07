# NOTI `0x01E1` `GROWTH_ITEM_RENEW_DATA`

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
| Type | `0x01E1` | 同上 |
| 名称 | `GROWTH_ITEM_RENEW_DATA` | upstream NotiPacketType |
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
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x811]` |
| 1 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x82c]` |
| 3 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x828]` |
| 5 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x820]` |
| 7 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x811]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D88130` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 布局与语义（龙跃武器成长值）

ClientStatic / Confirmed（布局）、High（语义）。dump `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144`，
handler VA `0x01188130`（= 上表 RVA `0x00D88130` + 0x400000），读取原语 u8 `0x027C5230`、u16 `0x027C52A0`。

```text
BodyLength = 8
+0 u8  container   物品所在列表（龙跃武器穿戴时为 3 = Equipment）
+1 u16 slot        该列表内的线上槽号（武器 12）
+3 u16 gained      本次增加的成长值；>0 时播放 grow_expup00.ani
+5 u16 experience  当前成长值，客户端夹到该星 maxExp（equipment_grow.etc 的 [exp base]）
+7 u8  maintained  当日维持疲劳（tooltip 字符串 0x6A40「維持疲労度 %d/%d」的分子）
```

机器区把 offset 0 记成 `[ebp-0x811]`，实际是 `[ebp-0x812]`；offset 7 才是 `[ebp-0x811]`。
写入点：成长值经 0x027BB9B0 写物品 +0x88，维持疲劳经 0x027BB900 写 +0x90，与 0x000E 条目的
ItemCore[74]/[76] 是同一对字段。

CurrentImplementation：`Dfo.Server.Protocol/Game/EquipmentGrowth/EquipmentGrowthSlice.cs` 的
`GrowthItemRenewNotice`，随怪物死亡帧下发（同模板加成长值时）。成长值由服务端计算，见
[龙跃武器方案](../../../Document/tasks/dragon-weapon/research-and-design.md)。未取得 WireCaptured。有限 ClientVerified：用户 2026-11-07 实测龙跃武器击杀成长与升星显示正常，见 [实测](../../../Document/tasks/dragon-weapon/user-test.md)。
