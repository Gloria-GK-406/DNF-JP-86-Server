# NOTI `0x008E` `DEATH_TOWER_INFO`

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
| Type | `0x008E` | 同上 |
| 名称 | `DEATH_TOWER_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 6   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 死亡之塔副本 id dungeonId | Confirmed | 上游 DeathTowerPacketBuilder.cs:23 `w.WriteUInt32((uint)dungeonId)`；调用点 DeathTowerCoordinator.cs:87 传 tower.Config.DungeonId，取自 `[ebp - 0x210]` |
| 4 | 2 | `u16` | 本塔终点层 endStage（即总层数） | Confirmed | 上游 DeathTowerPacketBuilder.cs:24 `w.WriteUInt16(endStage)`；调用点 DeathTowerCoordinator.cs:87 传 (ushort)tower.Config.TotalStages，取自 `[ebp - 0x210]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D4A8F0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-03 静态补证与实现边界

ClientStatic / Confirmed：当前镜像 handler VA `0114A8F0` 顺序读取 `u32 dungeonId + u16 totalLevels + u8 + u8`，body共8B。两个尾字节在 `0114ACF3/0114ADA0` 读取，完整业务语义尚未恢复。

当前迷妄内容为11001、15层。入场前置与逐层运行的实现/验证入口见 [迷妄之塔任务](../../../Document/tasks/illusion-tower/spec.md)，输入身份及反汇编见 [研究报告](../../../Document/IllusionTowerResearch-20261003.md)。首层等待全员装载后才放行是当前实现选择，仍需A21验收。

尚无本次 WireCaptured / ClientVerified；自动化编码与运行测试不能替代客户端验收。
