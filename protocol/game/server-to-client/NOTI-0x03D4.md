# NOTI `0x03D4` `WEDDING_CEREMONY`

## 2026-10-17 婚礼角色顺序

CurrentImplementation：两个固定30B姓名统一按新娘、再新郎排序，不依赖预约发起方。ClientStatic / Confirmed：cinema从03D4 context取两人，008884A0(flag1)取第二人，在00888924..00888928回答新郎誓词；flag0为新娘。03D7启动演出前广播双方当前PVF force-move坐标；原版镜头走位顺序仍待历史视频及实机验证。旧Snapshot JSON属性名保留以兼容cleanup队列。


## A21 人工读取边界与普通准备投影（2026-10-16）

ClientStatic / Confirmed：handler RVA00895B10固定读取79B，没有CMD结果信封。原先把offset47后的数据笼统记作32B字符串的结论不够精确：本区是30B字符串再加独立u16。人工裁定优先于下方机器区的unknown块。

| Body offset | Size | Enc | 原生消费与当前实现 |
|---:|---:|---|---|
| 0 | 4 | u32 LE | 与婚姻manager+5C的对象编号比较；本服按接收者的配偶/订婚对象CharacterId编码，两端分别计算 |
| 4 | 4 | u32 LE | 教堂area，写manager+0C；通知图标把当前记录该字段与角色area比较；本服填已分配Room.AreaId |
| 8 | 4 | u32 LE | 后续计算该值减当前秒时钟；本服按固定开场Unix秒编码 |
| 12 | 4 | u32 LE | 婚礼所在频道，RVA00F9D072传给dstr11CE2；本服填Room.ChannelId |
| 16 | 1 | u8 | 当前普通档写0；其它档未接入 |
| 17 | 30 | bytes | 原生字符串转换；本服填申请者名称，UTF-8完整前缀且以0结尾，双方角色名排序仍待实机 |
| 47 | 30 | bytes | 原生字符串转换；本服填另一方名称，同上 |
| 77 | 2 | u16 LE | 请柬剩余额度，写manager+74，经RVA008918B0 getter用于RVA00885E7E额度文案；本服按当前PVF普通档每人10份减持久已派送数量编码 |

CurrentImplementation：普通婚礼接受后，及已预约角色重登的03F3查询后，为各接收者发送准备通知。数据库内部room token不进入正文；新人通知回调以构包参数(1,0,0)形成03F7，正文携带上下文area，服务器据当前预约定位房间。此通知只表达准备上下文；到点成功结算另发03D7和03D0阶段4。预约确认后服务端自动迁移双方，图标用于离场后重入。

当前可证明的是上述读取、赋值与分支；固定开场时刻策略属于CurrentImplementation。历史截图中额度0是上一版占位；本次额度、自动入场、动画及结算仍待用户实机测试。见[定向补证](../../../Document/MarriageChurchEntryEvidence-20261016.md)、[请求](../client-to-server/0x03F7.md)与[阶段范围/测试步骤](../../../Document/MarriageChurchEntry-20261016.md)。

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
| Type | `0x03D4` | 同上 |
| 名称 | `WEDDING_CEREMONY` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 79   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 79 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x135]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00895B10` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

## 实帧边界

尚无本次完整NOTI03D4正文归档；不能以79B编码测试或双端城镇投影单测标为ClientVerified。
