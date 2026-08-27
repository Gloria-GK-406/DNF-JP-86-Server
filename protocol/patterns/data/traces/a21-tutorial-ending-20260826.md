# A21 新手副本的结尾:穿装备、奖励药、升级、通关

**2026-08-26。** 判据仍是三层:

1. **对照抓包。** 8/22 那次上游 `ServerS4A21` 会话跑完了整个教学(4 次
   `CHANGE_TUTORIAL_FLAG`,含结束用的 31),归档在
   `ServerS4A21/Server/DfoServer/bin/Debug/packet_log.reference-20260823.txt`。当日那份
   `packet_log.txt` 只跑到入场,**没有结尾**,所以本篇的参照全部取自 8/22 那份。
2. **上游源码。** `DungeonTutorialHandler`、`DungeonSettlementHandler`、
   `InventoryInsertService`、`InventoryMoveService`、`DungeonNotificationBuilder`。
3. **档案。** `equipment/*.equ` 的 `[equipment type]`、`character/ExpTable.tbl`。

前一轮把[清房与过图](a21-room-clear-20260826.md)打通之后,实机一路走到 Boss 房,暴露出四堵墙。

---

## 一、穿不上装备

玩家把刚捡到的护肩从背包 9 号槽拖到装备栏 15 号槽,本仓回 `00 17 00 03` —— 码 23
「无法移动物品」。原因是 `MoveItemSpaceCommandHandler` 写死了**只服务 Main→Main**,跨列表一律
`ListNotServed`。

**参照有逐字节样本,而且是同一件东西。** 8/22 那位魔枪士穿的也是 14400、也是从背包 9 号槽到
装备 15 号槽:

```text
C->S CMD 0x0013  00 09 00 40 38 00 00 01 00 00 00 03 0F 00 ...
S->C CMD 0x0013  01 00 09 00 01 00 00 00 03 0F 00
```

回包就是本仓成功路径原有的 11 字节形状,**后面什么都不跟**。

**目标槽由服务端自己算。** 物品的 `.equ` 声明 `[equipment type]`,值是引号词:14400 是
`[shoulder]`,27600 `[weapon]`,10400 `[coat]`,12400 `[pants]`。名字到槽号的那张表与角色创建
读的 `[create equipment list]` 同源,于是提成了一处共享:创建时放进 15 号槽的那件,正是玩家能
拖进 15 号槽的那件。客户端本地就会算,不会提交它认为非法的拖放 —— 正因如此服务端不能采信请求。

装备容器此前**根本没有绑定**:入场只注册了主背包。两端都得是容器,穿脱才是一次两端一起落定的
移动。脱下来是同一 type 两个列表对调,已实现但**没有抓包样本**:参照角色穿上就再没脱过。

## 二、奖励的血瓶发了,但发错了格子

第三间房清空后客户端发 flag 38,本仓照档案给了一瓶药、回了正确形状的确认,玩家却看不到。

```text
本仓 : 01 01 41 00 1A 21 00 00 01 00 00 00   → 槽 65
参照 : 01 01 03 00 1A 21 00 00 01 00 00 00   → 槽 3
```

**差别在放东西的规则,不在这一帧。** 上游 `InventoryInsertService.TryPlanInsertByDefaultRule`:

```csharp
var emptyFirst = canUseQuickSlot && item.ItemKind == KindConsumable ? QuickSlotRange : range;
var emptySecond = canUseQuickSlot && item.ItemKind == KindConsumable ? range : QuickSlotRange;
```

`QuickSlotRange = (3, 8)`。**消耗品先给快捷栏,栏满才退回 65–120**;其余可上栏的种类反过来;
生物及其装备/饲料不上栏;装备不参与这段判断(上游把不可堆叠的整个排除在外)。

药落进背包,快捷栏就是空的,而教学下一步是"按键喝掉它" —— 那一步做不下去,玩家看到的就是
"没发药"。

顺带把"能不能上栏"这条规则合并到了一处:`ItemSlotRuleService.AllowsQuickSlot`。此前放置规则和
占位规则各带一份排除名单,两份可以互相走散 —— 那样就能把东西放到一个拖放永远到不了的格子里。

## 三、`CMD 0x01E4 TUTORIAL_LEVEL_UP` 无人应答

客户端在 Boss 房发了这一帧(0 字节),服务端不认得。

**这是教学里唯一真正发经验的地方。** 之前每次击杀的 `NOTI 0x0025` 报的都是当前值,不加不减。
上游把等级**赋值**到 2、经验赋值为 `ExpTable.tbl` 的第一项,于是打得多打得少的人走到这一步都
停在同一处。参照回四帧:

```text
NOTI 0x0025 EXP                 83B
NOTI 0x0015 ACCEPTABLE_QUEST_LIST 25B   ← 本仓未发
NOTI 0x0002 USERINFO           579B     ← 本仓未发
CMD  0x01E4                      1B  [01]
```

**本仓少发中间两帧**,因为它们都是把整份已发布的入场状态重投影一次,而会话快照眼下没有局部
更新的路径。已发的那两帧带着新等级。记在 `Docs/ProtocolScaffoldDissonances.md`。

发布中的会话快照会就地改掉再发帧 —— 之后每一帧的等级都从那里读,包括击杀确认,留着旧值会让
整局都报 1 级。

## 四、通关不跳转:缺的第一帧只有一个字节

Boss 房最后一只怪死后本仓什么都没发,客户端就只剩心跳。参照的顺序是:

```text
RECV 0x0027 最后一只怪
SEND NOTI 0x001F ENABLE_CLEAR_DUNGEON   1B  [00]     ← 出口在这里打开
SEND NOTI 0x0117 SECRET_SHOP_NPC        4B  [E8 03 00 00]
RECV CMD  0x002E SET_PLAY_RESULT       53B            ← 玩家踩通关点
SEND NOTI 0x0022 PLAY_RESULT           16B
SEND NOTI 0x0025 EXP                   83B
SEND NOTI 0x0023 CLEAR_DUNGEON_REWARD 354B
```

**在收到 `0x001F` 之前客户端一个字都不问**,所以这一帧不发,后面整条链一帧都不会来。

判断时机需要知道"这间房清空了没有",本仓因此在运行状态里按(格子, 运行序号)记住已死的 actor。
**AI 角色不计** —— 教学第一间房那位阿甘左是友方,等它死房间永远不会清。

`SET_PLAY_RESULT` 53 字节里上游**只读偏移 10** 的那个 u8(客户端自己算的评级点)。参照那帧
偏移 10 与 11 都是 `0x5A`,回包 `NOTI 0x0022` 偏移 6 原样写回 `0x5A` —— 这是"读的是 10"的证据。
**通关用时不在请求里**,服务端自己计时,所以运行状态多记了一个开局时刻。

`NOTI 0x0023` 的布局对得上而不是抄的:参照那帧的每个非零偏移都落在上游构包器算出的位置 ——
条数在 155、列表从 159 起、免费牌那一对在 240/245、七个牌位标记在
249/258/267/276/285/294/303、付费花费在 312。本仓无逐对象记录,整体前移 80 字节,同样几处落在
160、169/178/187/196/205/214/223、232。**本仓这一局什么都不发,帧里也就这么写** —— 通关奖励需要
经验公式、评级奖励表和翻牌生成器,三样都没有。

## 五、顺带:上游也不回的那些帧,现在是"有意不回"

`0x0026 USE_SKILL`、`0x035C SYNC_ITEM_SPACE`、`0x00D9 OVERFLOW_INFO`、
`0x04F1 LOG_DOMAIN_CONNECT`、`0x04D7 LOG_CONNECT_PROCESS` 在参照会话里都是**收到 n 次、回 0 次**
(依次 3/6/1/1/2)。此前它们和真正的漏洞共用一条 `Unhandled Game frame` 告警。

**这两者在线上无法区分、含义却相反:一个是洞,一个就是协议本身。** 共用一条日志行的那段时间里,
每个真正的洞都埋在同样的噪声里 —— `GET_ITEM` 就是这么在六条一样的告警后面躺了一整轮。现在它们
注册在 `Game/UnansweredReports/`,一个 handler、六个 wire key,记 debug 级。

`0x0030 DECREASE_DURABILITY` 也注册了,但**参照会话从未发过它** —— "上游也不回"这句话在它身上
说不出口,能说的只是本仓不建模耐久损耗。这条差别写进了 Dissonances。

## 线级验证

`clearprobe2.py` 从入场一路走到结算,每一步的号码都从服务端自己的帧里读:

```text
reward       CMD  0x008F (12B)
pickup       CMD  0x002B (1B) + NOTI 0x0027 (18B)
equip        CMD  0x0013 (11B)   01 00 09 00 01 00 00 00 03 0F 00   ← 与参照逐字节相同
levelup      NOTI 0x0025 (83B) + CMD 0x01E4 (1B) [01]
lastkill     NOTI 0x0025 + NOTI 0x0026 + NOTI 0x001F (1B) + NOTI 0x0117 (4B)
playresult   NOTI 0x0022 (16B) + NOTI 0x0025 (83B) + NOTI 0x0023 (274B)
161 帧,连接始终存活
```

库里对得上:装备栏 15 号槽 `kind=1 item=14400 value=25015874 dur=25`,`value` 正是落地时
`START_MAP` 宣告过的实例值;角色 `level=2 exp=1000`。

**这证明的是服务端答了什么,不是客户端接受什么。** 后者要一次真实会话。

## 还没做的

- **翻牌与回城。** 参照在 `NOTI 0x0023` 之后还有 `CMD 0x0045 SCORE_SCROLL_STATE`、
  `0x0046 CARD_SELECT_RIGHT_STATE`、一次 `MOVE_MAP`、`CMD 0x0047 SELECT_CARD` 与
  `NOTI 0x000E UPDATE_ITEM_LIST`,然后 `CMD 0x002A GIVEUP_GAME` 回城
  (`NOTI 0x0003/0x0017/0x0018`),最后 `CMD 0x008F` flag 31 收尾。**一帧都没做**,等实机走到
  那里再说 —— 照着构包器盲写一整段没人验证过的代码,不如让客户端指出下一堵墙在哪。
- **击杀掉落。** `NOTI 0x0026` 的掉落条数仍恒为 0。参照那边是 103/117 字节的带掉落版本。
- **金币拾取。** 另一个 117 字节模板,教学不掉金币。
- **通关奖励与经验发放。** 见上,`NOTI 0x0023` 全零。
- **升级后的两帧跟进。** `NOTI 0x0015` 与 `NOTI 0x0002`,见第三节。
