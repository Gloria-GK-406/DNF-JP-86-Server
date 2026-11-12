# 1:1 会话逆向结论（2026-11-12）

两路只读调查的结论摘要。地址：A21 为 VA（= 协议页 RVA + 0x400000），来源 `DNF.unpacked-b1-debug.exe`（SHA-256 1F8244D3…B00DD）静态反汇编；70 为 `df_game_r` VA。均无实机抓包。

## 70 服务端（70Reference）

- 没有实现 1:1 会话（Confirmed）：`PacketDispatcher::PacketDispatcher()`@08590a2e 中 0x1A2–0x1A7 槽位为空，CREATE_GROUP／INVITE／LEAVE／CHANGE_STATE 收到后经 `doDispatch`@08594922 返回 0 被静默丢弃；community、monitor 等进程也无会话结构。
- mode 43 在 `GameWorld::send_chat_msg`@086c975e 跳转表（只覆盖 0..0x28）之外，不投递（Confirmed）。
- 错误码（auction DWARF `ENUM_ERROR`，`DNFShared/CSCommonError.h`；值 Confirmed，game 同值 High）：77 BLACK_USER、100 NOT_EXIST_USER、101 CREATE_GROUP_FAILED、102 NOT_EXIST_GROUP。
- 编号对照：70 NOTI CREATE_GROUP 0x169 … GROUP_CHAT_USER_STATE 0x16F 对应 A21 0x16A … 0x170；CMD 0x1A3–0x1A6 不变。
- 70 的 CMD 0x014A ONE_TO_ONE_CHAT_STATE 是 GM 禁止某用户密语的监管功能，与窗口无关。

## A21 客户端（ClientStatic）

NOTI handler 注册（024EC373–024EC416，Confirmed）：016A `024EAFA0`、016B `024EB8B0`、016C `024EB380`、016D `024EBDF0`、016E `024EA5E0`、016F `024EA700`、0170 `024EA8E0`。

### NOTI 0x016A（`024EAFA0`）

- 布局（Confirmed）：`u8 result`；result ∉ {77,100,101} 时再读 `u32 convId, u8 n, n × dstr name`。
- 77：弹 4763（黑名单）。100、101：静默。其余任何值（含 0）按成功处理。
- dstr 名字须 0 < len < 128 字节，否则只消耗长度字段并用默认名，后续错位（ReadDStrW `0274FCB0`）。
- n = 0 不建窗。成功时每条都新建窗口，不按 id 去重（Confirmed）。窗口 `+0x184 = names[0]`，`+0x230 = convId`，`+0x1F4 = 0`。
- 标题（High）：成员 ≤1 显示自己名；>1 取 names[0] 为对方。客户端本地建窗时名单是 [对方, 自己]，因此服务端给每个接收方发 [对方, 自己]。
- 发起方（发过 0x01A3、`[0x3B75084]` = 1）收到后窗口抢焦点；被邀方窗口创建但只闪烁。双方各需收到一条才有窗口。

### CMD 0x01A3（构包 `024E7EB0`）

- body：`dstr 目标名`（Confirmed）。入口为玩家右键菜单（`02596311`，High）等，经 `024EAB20`。
- 发送后不本地开窗、无超时、无提示（High）。选项 0x1B／0x1C 开启时客户端自行拦截。

### 窗口内发言（`024EF150`，构包 `024EF38B`）

- `0x0011`：`u8 mode`（会话 id ≠ -1 时 43；-1 时 33；跨服情形 37）、`u16`（按名查到的 uid 或 0，服务端忽略）、`u32 aux`（mode 43 时为会话 id）、`dstr text`、`dstr name`（普通模式为**发送者自己的名字**）、`u8 server`（0）。尾部两字段无条件写入（Confirmed）。
- 发出后本地把自己的话写进窗口（High），不进主聊天框（High）。窗口路径没有连发冷却。
- 特殊状态（High）：`[game+0xF48]` ∈ {8, 0xD, 0x14, 0x16, 0x17, 0x18}（`0163D300`／`0163D150`，含决斗场）时，`024EAB20` 不发 `0x01A3` 而是本地建窗，会话 id 保持 -1，窗口内发言因此为 mode 33（跨服情形 37）。

### NOTI 0x016F（`024EA700`）

- 布局（Confirmed）：`u32 convId, dstr sender(<128B), dstr text(<1024B)`。
- 按 id 找窗口，找不到静默丢弃；sender 等于自己名字时丢弃（Confirmed）；疑似屏蔽名单检查（Hypothesis）。

### CMD 0x01A5 与 NOTI 0x016E

- `0x01A5`：窗口关闭（`024EEE70`）且会话 id ≠ -1 时发送，body `u32 convId`；之后客户端把窗口 id 置 -1 并摘除窗口（High）。
- `0x016E`（`024EA5E0`，Confirmed）：`u32 convId, dstr name(<128B)`；按 id 找窗口，删同名成员，显示 46460「%s さんが退場しました。」；窗口不关、id 不变，留守方仍可发 mode 43。

### 其他

- `0x016B/016C/016D/0170`、CMD `0x01A4`：多人群聊与成员状态，不属于 1:1 最小闭环。
- CMD `0x01A6`：`u8` 状态，只在有窗口时发，客户端无应答 handler，可忽略。
- CMD `0x0149`：A21 无构包点。
- `0x0011` 应答 `0111B480`：只处理 ok = 0，按通用错误表弹框，与 mode 无关；mode 43 被拒会弹框而窗口内已显示的话不回滚。

## 与参照服务端 ServerS4A21 的差异

- `0x016A` 名单顺序：参照写 [自己, 对方]，客户端会以 names[0] 为对方，应为 [对方, 自己]。
- 参照对进行中会话会重发 `0x016A`，客户端会出现重复窗口。
- 参照把 mode 43 尾部注释为目标名，实际是发送者自己的名字。
