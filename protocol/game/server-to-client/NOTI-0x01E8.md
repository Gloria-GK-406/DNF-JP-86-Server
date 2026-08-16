# NOTI `0x01E8` `CHARACTER_ADD_BUFF`

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
| Type | `0x01E8` | 同上 |
| 名称 | `CHARACTER_ADD_BUFF` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 17   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 其后 buff 记录的条数；上游恒写 1，且只跟一条 16 字节记录 | Hypothesis | 上游 Network/Builders/Dungeon/SpecialDungeonNotificationBuilder.cs:38 WriteByte(1)，:39-42 随后只写一条 buffId + 3 个 i32，取自 `[ebp - 0x811]` |
| 1 | 4 | `u32` | buff id（副本临时 buff 的编号） | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:39 WriteInt32(buffId)；唯一调用点 Network/Handlers/Dungeon/DungeonBuffNotificationSender.cs:28-32 传 addedBuffIds[i]，取自 `[ebp - 0x818]` |
| 5 | 4 | `u32` | builder 形参 field1；上游唯一调用点恒传 0，语义未知 | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:35,40 WriteInt32(field1)；唯一调用点 DungeonBuffNotificationSender.cs:28-32 三个 field 均传 0，取自 `[ebp - 0x838]` |
| 9 | 4 | `u32` | builder 形参 field2；上游唯一调用点恒传 0，语义未知 | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:35,41 WriteInt32(field2)；唯一调用点 DungeonBuffNotificationSender.cs:28-32 三个 field 均传 0，取自 `[ebp - 0x834]` |
| 13 | 4 | `u32` | builder 形参 field3；上游唯一调用点恒传 0，语义未知 | Confirmed | 上游 SpecialDungeonNotificationBuilder.cs:35,42 WriteInt32(field3)；唯一调用点 DungeonBuffNotificationSender.cs:28-32 三个 field 均传 0，取自 `[ebp - 0x83c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0051B180` |
| handler 所在区 | text |
| 注册路径 | forwarder |

NOTI 的 body **没有**结果信封，整个 body 归 handler——这与 CMD 相反，见 [game-receive](../../patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。

支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

尚无实机样本。取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，并在此说明产生它的客户端动作。

## 服务端实现映射

尚未实现。

## 待实机补全

- [ ] 触发时机（什么玩家动作/服务端状态导致它出现）
- [ ] 响应流程（是否有响应、响应身份与顺序）
- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
