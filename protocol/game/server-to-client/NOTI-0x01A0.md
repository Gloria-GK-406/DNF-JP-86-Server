# NOTI `0x01A0` `MANAGE_LEVEL_UP`

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
| Type | `0x01A0` | 同上 |
| 名称 | `MANAGE_LEVEL_UP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：结构未知** — 静态分析无法得出字段边界。

原因：handler 的取字段调用进入了被打包/混淆的代码区，静态跟不下去。

本协议**没有可用的字段表**。上面的原因即其不可知的成因；
要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。

静态跟不下去的调用目标：`0x05800213`

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x010E5C50` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 2026-11-13 冒险团升级推送（CurrentImplementation，待实机）

**body 为空。**
- 70 服务端对应 NOTI 的 body 为空（70Reference）。
- A21 handler 落在打包区，静态布局未知；本仓按空 body 发送。实机若证明不成立，撤掉推送需用户确认。

**客户端行为（ClientStatic）。**
- 链路：`0x014E5C50` → `0x014E5AF0`，在 `0x014E5B78` 把冒险团管理器等级**加 1**。
- 随后播放 dstr 46535「冒险团已升为 Lv%d」与动画（`Document/outputs/notice-flags-20260924/investigation.md` 第 3 节）。
- 因为是加 1，跨几级就发几帧。

**服务端触发。** 订阅 `CharacterLevelChanged`，策略 LiveOnly，不设合并键：

1. 由冒险团领域服务计算 a、b：
   - 把该角色的等级分别换成事件的 before 和 after，账号其余未删除角色取当前等级，按 `charactermanage.etc` 算出两个冒险团等级 a 和 b；
   - 规则见本页下方。
2. 只有 b > a 时才推送，并且要求账号的已选角色在世界中（城镇或副本）。冒险团下降、不在世界中、角色已删除时都不推。
3. 推送 b−a 帧。
4. 每次等级变更最多推一次：LiveOnly 先提交消费、从不重放，跨进程或过期（默认 30 秒）的事件跳过。
5. 错过的升级在下次进入世界时，由 USERINFO1 和 `0x01BA` 的绝对值改正。

**规则（70Reference，`df_game_r` 符号）。**
- 角色点数 = 各级 `[point bonus]` 之和，累加到角色当前等级；
- 账号点数 = 各角色点数之和，封顶在 `[manage level point]` 第 `[manage level max]` 个阈值（A21 为 175350）；
- 等级 = 账号点数达到的最高阈值序号，最大为 10。

**未证实。**
- A21 的空 body；
- 升级券路径与改变成长类型或开槽的完成任务路径是否会多加一级：两者都在同一命令里先发带新等级的 USERINFO1。

两项都待用户按 `Document/tasks/adventure-group/` 的测试流程确认。

**判据。** `Server/tests/Dfo.Server.Protocol.Tests/Game/AdventureGroupPushTests.cs`、`Server/tests/Dfo.Server.Application.Tests/Characters/AdventureGroupTests.cs`。
