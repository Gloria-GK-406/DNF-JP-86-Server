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

## 2026-10-03 首次入场崩溃补证

ClientRuntime / Confirmed + ClientStatic / High：实机首次入塔在处理 CMD `0012` 扣券成功回执时进入塔属性更新路径，RVA `022D81B9` 读取空指针 `+8`。旧服务端先发 `008E/008F`，后发 `0012/000E`；当前修正将已提交的扣券回执与背包更新前移至塔模式切换之前。样本、调用栈、静态地址和回归结果见 [入场崩溃记录](../../../Document/illusion-tower-entry-crash-20261003.md)。此记录不是成功入塔验收，修正后仍待 ClientVerified。

## 2026-10-04 死亡之塔接入

CurrentImplementation / Confirmed：当前实现同时服务死亡之塔11000（当前PVF为45层）和迷妄之塔11001（15层），分别读取对应入场/奖励内容。死亡塔在008E之前发送已扣券回执和临时Main完整快照；退塔后发送默认Main完整快照。共享晶体随角色当前绑定切换，实际账号存储归属不变。

实现、证据与待实机项目见 [死亡之塔记录](../../../Document/DeathTowerImplementation-20261004.md)。尚未将死亡塔标为ClientVerified。

## 2026-10-05 offset7 ClientStatic 补证

ClientStatic / Confirmed：body offset 7（u8）是 `CNRDQuickPartyManager` 的**快速组队随机奖励类型**，不是塔内普通道具禁用布尔值。客户端源路径/方法名字符串解密确认 owner；`Etc/QuickParty.etc` 的解析器 VA `02BBF810` 将 `[increase exp]` 映射为0，将未匹配类型保留为11（0x0B）。11也是该管理器初始化/重置时使用的无奖励哨兵；0是合法经验奖励类型，两者不等价。

取值链（均为VA）：`0114AD99` 将接收局部字节初始化为11，`0114ADA0` 读取最后1B，`0114ADBB` 零扩展；`0114ADC9 → 0130DE20` 取得 owner（`[03A5C990]` 所指对象的 `+0xB8`），`0114ADD0 → 01587B00` 将值写入 owner `+0x22C` 的u16。setter只赋值，**不设置独立启用字段 `+0x220`**，也不直接刷新UI或施加效果。

消费者 `0158A4F0` 在 `+0x220 == 0` 或 `+0x22C == 11` 时返回空；否则以奖励类型查询 `+0x58` 的配置表。该表由 QuickParty.etc 解析器 `02BC0490` 经 `02BC0FA6 → 02BBF810` 得到枚举、在 `02BC1183/02BC118A` 插入，形成加载与读取闭环。0在启用条件满足时可查询经验奖励记录；不能仅由收到0推定实际加成已生效。offset6不因本补证获得新的语义确认。

0..10的完整tag映射、角色效果/数值修正/说明UI消费者、镜像哈希及可复核反汇编见[独立研究记录](E:/Project/DNF/Document/upstream-tower-test-20261004/008E-offset7-client-analysis.md)。本补证替代2026-10-03“尾字节语义尚未恢复”中对offset7的未知判断，不改变其他字段或既有历史记录。

**验证边界：**本结论为ClientStatic；实际入塔时快速组队启用状态、0与11的可见表现尚未实机确认。本次正向链没有连到背包普通物品可用性或红色绘制，不能据此把0→11作为红色问题的已证实修复；不升级为ClientVerified。

**处理决定（2026-10-05）：**先保留取证结论，暂不修改服务端 offset7 的发送值0；快速组队奖励接入时再确定发送策略及验收。普通道具禁用另由客户端 `[tower limit of stackable item]` 与场景/角色状态共同判定，静态调用链见[塔道具禁用取证](E:/Project/DNF/Document/upstream-tower-test-20261004/塔道具禁用调用链-续查.md)。保留普通背包后的实际禁用表现仍待实机验证。
