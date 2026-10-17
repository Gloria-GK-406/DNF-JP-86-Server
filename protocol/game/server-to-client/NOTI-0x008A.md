# NOTI `0x008A` `TELEPORT`

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
| Type | `0x008A` | 同上 |
| 名称 | `TELEPORT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 7   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |
| 2 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |
| 4 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| 6 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D60BA0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## A21 强制站位（2026-10-17）

证据等级：**ClientStatic / Confirmed**，婚礼双端视觉效果待验收。
镜像 SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，
对应 EXE SHA-256 见上方。入口 RVA `00D60BA0` 完整消费 7 字节后返回，无附加尾部读取。

| 偏移 | 编码 | 含义及消费证据 |
|---:|---|---|
| 0 | u16 | 在线 user ID；`D60BCC..D60BD0` 调用 `15E2EF0` 查人，再经 `15DF190` 取角色对象 |
| 2 | u16 | X；`D60BEE` 零扩展后传给对象虚表 `+AC` |
| 4 | u16 | Y；`D60BEA` 零扩展后传给对象虚表 `+AC` |
| 6 | u8 | 朝向控制；`D60C15..D60C1F` 将 `value != 4` 传给虚表 `+138`。此字段更广泛的方向枚举语义尚未闭合 |

`D60BFA..D60C00` 对目标对象调用 `vtable+AC(X,Y,0)`，随后调用 `vtable+28`。
整条路径没有「user ID 等于本人就跳过」的分支。
相反，[0016](NOTI-0x0016.md) 的 `D8D22C..D8D233` 对本人跳过坐标处理，
不能将普通移动同步当作本机角色强制站位。

CurrentImplementation：`ForcedTownPositionEncoder` 编码 `008A`；婚礼开场前
`TownAreaProjectionDelivery.ForcePositionAsync` 更新同区域位置事实，并向本人及当前可见同区域会话
发送相同坐标；全部站位投影完成后才发送 `03D7`。普通移动仍使用 `0016`。
编码、双角色多收件人和会话替换/私有可见性有自动化保护；这些检查不代表 ClientVerified。
