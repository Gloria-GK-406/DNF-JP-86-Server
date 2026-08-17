# NOTI `0x01C0` `COMBO_SKILL_INFO`

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
| Type | `0x01C0` | 同上 |
| 名称 | `COMBO_SKILL_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 9   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 通知头，上游恒写 0 | Confirmed | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:79 output.Add(0) 为 body 首字节，取自 `[ebp - 0x10]` |
| 1 | 1 | `u8` | 技能页块数量（normalizedBlocks.Count） | Confirmed | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:80 output.Add((byte)normalizedBlocks.Count) 为 body 第二字节，取自 `[ebp - 0xd]` |
| 2 | 1 | `u8` | 第 1 个块的技能页索引 pageIndex（合法值 0/1） | High | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:81-82 紧随头部追加块；块内 :22 output.Add(pageIndex)，:140-141 解析同一字节为 pageIndex 且限制 <= 1，取自 `[ebp - 0x19]` |
| 3 | 1 | `u8` | 该页的连招根技能条目数量 rootCount | High | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:23 output.Add((byte)roots.Count)，:144 解析为 rootCount，取自 `[ebp - 0xf]` |
| 4 | 2 | `u16` | 第 1 个根技能 id（RootSkillId） | High | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:27 WriteUInt16(root.RootSkillId)，:154 解析为 RootSkillId，取自 `[ebp - 0x3c]` |
| 6 | 1 | `u8` | 该根技能的子技能数量 childCount | High | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:41 output.Add((byte)children.Count)，:158 解析为 childCount，取自 `[ebp - 0xe]` |
| 7 | 2 | `u16` | 第 1 个子（连招衔接）技能 id | High | 上游 Game/Skills/DarkKnightComboSkillInfoCodec.cs:43 WriteUInt16(child)，:164 解析为 ChildSkillIds 元素，取自 `[ebp - 0x40]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x009083E0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
