# NOTI `0x001F` `ENABLE_CLEAR_DUNGEON`

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
| Type | `0x001F` | 同上 |
| 名称 | `ENABLE_CLEAR_DUNGEON` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：结构未知** — 静态分析无法得出字段边界。

原因：handler 的取字段调用进入了被打包/混淆的代码区，静态跟不下去。

本协议**没有可用的字段表**。上面的原因即其不可知的成因；
要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。

静态跟不下去的调用目标：`0x04C006A2`、`0x04ED8D44`、`0x052E4E6F`、`0x0539F153`、`0x05800213`、`0x0581A3BD`、`0x0585D2F7`、`0x081F96C9`

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D49950` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
## A21

**2026-08-26 实机,整条结局都挂在这一个字节上。** 玩家打完 Boss 房最后一只怪之后无事可做 ——
没有出口、没有结算画面,线上只剩心跳 —— 因为这一帧从来没发过。归档见
[a21-tutorial-ending-20260826](../../patterns/data/traces/a21-tutorial-ending-20260826.md)。

```text
body = 00        ← 一个字节,值为零
```

上游 `DungeonNotificationBuilder.BuildEnableClearDungeon` 就是 `new byte[] { 0x00 }`。
注释指明它对应 `df_game_r CParty::ClearDungeon`。

**2026-08-26 的触发观察：Boss 房清完时出现此帧。** 2026-09-18 的当前实现已经改为
确认最终 Boss 死亡即可判定通关，并把同一 Cell 的其余活怪标为死亡；它们不另发经验或掉落。
此前“必须等 Boss 房最后一只怪死亡”只适用于那次观察，不再描述当前服务端规则。
该规则与对应任务推进的代码/测试已完成，A21 当前版本的最终 Boss 死亡 → 本通知 →
结算画面仍需成对实帧与客户端表现核验。普通房间的清场与本通关通知要分开判定。
2026-08-26 的样本中紧跟着发 [NOTI 0x0117 SECRET_SHOP_NPC](NOTI-0x0117.md)；
这一帧的当前发送时机仍应随结算链复验。

**在此之前客户端什么都不问。** 它随后才发 [`CMD 0x002E SET_PLAY_RESULT`](../client-to-server/0x002E.md)。
