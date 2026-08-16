# NOTI `0x024A` `RAID_DUNGEON_STATE`

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
| Type | `0x024A` | 同上 |
| 名称 | `RAID_DUNGEON_STATE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 12 + n×8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 顶层此处=固定常量 0（上游写死）；重复组内此处=副本 ID | Confirmed | 上游 RaidPacketBuilder.cs:337 恒写 0，:341 写 dungeonState.Key（BuildDungeonState），取自 `[ebp - 0x14]` |
| 4 | 4 | `u32` | 顶层此处=副本状态条目数；重复组内此处=该副本的状态值，上游取值 0=开放（Progression.cs:720 孵化场开启、:839 孵化场恢复）、2=未开放（AntonDefinitions.cs:61-70、117-126 两阶段初始表）、3=已通关（Progression.cs:525） | Confirmed | 上游 RaidPacketBuilder.cs:338 写 dungeonStates.Count，:342 写 dungeonState.Value，取自 `[ebp - 4]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |
| &nbsp;&nbsp;↳&nbsp;4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| 8\* | 4 | `u32` | infectionDungeonId：被感染的孵化场副本 ID，0 表示无（构造器默认值）。注意线格式上该字段位于重复组之后，本表把定长字段前置排列，故此 offset 非真实字节位置 | High | 上游 RaidPacketBuilder.cs:344 在循环之后写 infectionDungeonId（默认 0，见 :331）；调用点 Progression.cs:720/525/839 传 _infectionDungeonByRaid 中经 SelectAntonInfectionHatchery（:754-762）选出的孵化场 ID，取自 `[ebp - 0x10]`。**位置为名义值**（按 n=0 排布） |

带 `*` 的偏移是**名义值**：偏移累加时**没有计入其前重复组的任何一次重复**，即按重复次数 **n = 0** 排布。真实位置 = 名义值 + Σ(该组宽度 × 实际重复次数)。实现时必须按计数字段累加，不能照抄这个数字。

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008F6CC0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |

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
