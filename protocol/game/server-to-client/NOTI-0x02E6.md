# NOTI `0x02E6` `GUILD_DUNGEON_REPEAT_BONUS_INFO`

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
| Type | `0x02E6` | 同上 |
| 名称 | `GUILD_DUNGEON_REPEAT_BONUS_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x001BCC70` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本仓隔离线路验证使用 4 字节正文，`u32 LE` 表示个人礼盒进度，按百分点编码。样本值 0、5、10、20、85、90 已与持久化读数核对。暂无原服线上样本。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 用 A21 核对选图、通关后 HUD 填充以及礼盒发放后的清零表现
- [ ] 用原服样本核对满条触发时机及原始协议

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->

**ClientStatic。** A21 handler VA `005BCC9E` 读取 `u32`，将其写入礼盒奖励界面对象 `039A4AB0+0x40`。VA `005BAD46` 取同一字段决定进度条填充宽度，VA `005BAE85` 和 `005BAEEB` 使用礼盒道具 `10156320`。A21 PVF 公会地下城样式 0 的 `[repeat clear bonus reward]` 同样配置 `10156320 ×1`，`[level difference]` 配置 `10`。

**游戏规则。** 反复通关累积礼盒进度，发盒后重置。日服同期更新注明队伍人数增加进度，队内最高和最低等级相差至少 10 级时额外增加。日服公告未列出具体增量，采用同期韩服玩家资料记录的每人 5 个百分点和等级差翻倍。[日服官方更新](https://arad.nexon.co.jp/news/update_view.aspx?no=7398) · [同期韩服玩家资料](https://tcatmon.com/wiki/%EA%B8%B8%EB%93%9C_%EB%8D%98%EC%A0%84)

**CurrentImplementation / WireTest。** 进度随公会地下城选图初始化，成功结算后按角色刷新。通关达到 100% 时邮件发放一个礼盒并推送进度 0。范围、验证和实机步骤见 [A21 公会地下城](../../../Document/tasks/guild-system/a21-guild-dungeon.md)。

**ClientVerified。** 2026-11-27 玩家确认公会地下城奖励礼盒领取与开启符合预期。验收库核对到满条角色进度清零和一件已领取的礼盒。
