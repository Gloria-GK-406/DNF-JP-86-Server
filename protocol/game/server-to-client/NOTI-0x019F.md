# NOTI `0x019F` `TAG_CHARACTER_INFO`

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
| Type | `0x019F` | 同上 |
| 名称 | `TAG_CHARACTER_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 67 + n×4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | record 数量：有支援兵时写 1，空 body 写 0 | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:130 WriteUInt16(1) 为 body 首字段；Network/Builders/Init/StrikerSupportTagCharacterBodyBuilder.cs:27 空 body 为 {0x00,0x00}，取自 `[ebp - 4]` |
| 2 | 2 | `u16` | 映射后的角色 id（mappedCharacterId，取当前激活角色 id 低 16 位） | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:119 传 checked((ushort)activeCharacterId)，:158 WriteUInt16(mappedCharacterId) 为 record 首字段，取自 `[ebp - 8]` |
| 4 | 31 | `dstringw` | 支援兵角色名 dstr（i32 长度 + 原始字节） | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:159 WriteDstr(supportName)，取自 `[ebp - 0x44]` |
| 35 | 1 | `u8` | 支援兵等级 level | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:160 WriteByte(level)，取自 `[ebp - 1]` |
| 36 | 1 | `u8` | 支援兵职业 job | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:161 WriteByte(job)，取自 `[ebp - 1]` |
| 37 | 1 | `u8` | 打包的成长上下文 packedGrowContext（转职位与觉醒阶段位同字节） | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:163 WriteByte(packedGrowContext)，取自 `[esi + 4]` |
| 38 | 2 | `u16` | 所选支援技能 id（MercenarySupportState.SkillId） | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:124 传 state.SkillId，:164 WriteUInt16(selectedSkillId)，取自 `[ebp - 0x3c8]` |
| 40 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x7d]` |
| 41 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x7a]` |
| 42 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x84]` |
| 46 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xcc]` |
| 50 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xa8]` |
| 51 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0xdc]` |
| 53 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x88]` |
| 57 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xd4]` |
| 61 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xb4]` |
| 62 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x7f]` |
| 63 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0xc4]` |
| 65 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x3b9]` |
| 66 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x3c2]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 1 | `u8` | 技能快捷槽位 Slot | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:182 WriteByte(skill.Slot) 为每条技能项首字节，取自 `[ebp - 0x3c1]` |
| &nbsp;&nbsp;↳&nbsp;1 | 2 | `u16` | 技能 id | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:183 WriteUInt16(skill.SkillId)，取自 `[ebp - 0x3c0]` |
| &nbsp;&nbsp;↳&nbsp;3 | 1 | `u8` | 技能等级 | High | 上游 Network/Builders/Init/StrikerSupportTagCharacterPacketBuilder.cs:184 WriteByte(skill.Level)，取自 `[ebp - 0x3ba]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D61320` |
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
