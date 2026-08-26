# A21 选图与进入副本

**2026-08-26,同日第三次实机。** 玩家在城里打开选图界面然后停下,把那一段交给我。这一篇要回答的是
两个问题:选图那一步到底缺不缺,以及"点击进入"缺什么。

---

## 一、选图不缺:四帧同序

```text
#143 C->S CMD  0x000F  ENTER_SELECT_DUNGEON  00 00 00 00
#144 S->C NOTI 0x0002  USERINFO              (579B)
#145 S->C CMD  0x000F                        01
#146 S->C NOTI 0x0003  USER_STATE            01 01 00 01
#147 S->C NOTI 0x001A  UDP_HOST              00
```

参照会话(8/22)同一段是 `RECV 0x000F → SEND NOTI 0x0002 / CMD 0x000F / NOTI 0x0003 / NOTI 0x001A`
—— **四帧同序**,长度差只是角色不同。**这一步没有缺口。**

之后客户端**没有再发 `CMD 0x0010`**,会话到此为止。选图界面上有哪些副本是客户端用自己那份 PVF 按
等级与任务状态画的,服务端不参与,所以线上看不出玩家是主动停下还是列表点不动。

## 一(订正)、选图缺一帧,而且是同一个坑第五次

**上面那节的结论是错的。** 玩家问了一句"有没有可能我走到了副本入口,但选图 UI 根本没出来" ——
按这个方向重查,发现我只比了帧的形状,没比请求的内容:

```text
参照   C->S CMD 0x000F  15 27 00 00   ← 0x2715 = 10005,教学副本
实机   C->S CMD 0x000F  00 00 00 00
```

参照那一帧是**教学的自动突发**,它紧接着就发了 `0x0010`,根本没等界面。**两份抓包里都没有"玩家走
到副本门口打开选图 UI"的样本**,所以"四帧同序"只证明了"对教学那次请求一致"。

上游在四帧之后还有一段:

```csharp
if (!isA21TutorialEntry)
    await session.SendPacketAsync(... 0x001B, BuildA21EnterSelectDungeon(...));
else
    // defer A21 tutorial NOTI 27 until CHANGE_TUTORIAL_FLAG
```

**`NOTI 0x001B` 是选图界面自己的状态帧,每次选择只发一次。** 普通情况发在 `ENTER_SELECT_DUNGEON`,
只有教学首次进入才推迟到客户端上报 flag 30 之后。本仓此前只在 `SELECT_DUNGEON` 的回答里发它 ——
因为用来建模的那次会话是教学,它在那条线上就出现在那里。

**这是同一个坑第五次咬人**,前四次分别是:actor 表(参照那两间房没有怪)、掉落块(参照前两间房不声明
可破坏物)、重访(参照没走回头路)、以及本条。每一次都是"参照会话在这条路径上什么都没做,于是我们的
空答案和它逐字节相同"。

判定用等级:上游问"等级是不是 1 且教学未标记可跳过",本仓没有那个标记 —— 这里没有跳过教学的路径 ——
所以只问等级。两者只在"跳过了教学的角色"上不同,而那是本仓到不了的状态。

线级实测两条分支:

```text
等级 1  0x000F → NOTI 0x0002 / CMD 0x000F / NOTI 0x0003 / NOTI 0x001A
        0x0010 → CMD 0x000F / NOTI 0x001B / 0x019F / 0x001C / 0x001D
等级 2  0x000F → NOTI 0x0002 / CMD 0x000F / NOTI 0x0003 / NOTI 0x001A / NOTI 0x001B
        0x0010 → CMD 0x000F / NOTI 0x019F / 0x001C / 0x001D
```

**玩家的角色当时是 2 级**,所以走的正是缺帧的那条分支。

## 二、"点击进入"也不缺,缺的是拒绝

线上看不出来的事,自己驱一次就能分辨。`selectprobe.py` 各开一条连接,对三个副本走
`0x000F` → `0x0010`:

```text
副本 10000(教学)   进入 = CMD 0x000F, NOTI 0x001B, 0x019F, 0x001C, 0x001D(65B)
副本 144  (正式)   进入 = CMD 0x000F, NOTI 0x001B, 0x019F, 0x001C, 0x001D(126B)
副本 5000 (训练室) 进入 = 什么都没有
```

**144 是一个正式副本**(`StormOfMetastasis/Abnoba/Redcrystal_Forest`,`[minimum required level] 1`),
服务端日志:`maze=3 start=(0,0) room=55612 boss=(5,0) seed=303882551`,`START_MAP` 126 字节、带
actor 与掉落。**教学和正式副本走的是同一条路,进入这件事本身没有缺口。**

5000 是风振训练室,档案里没有迷宫,布局解不出来。此前本仓在这种情况下**一帧都不发** —— 到玩家那里
就是一个按了没反应的按钮,和"服务端挂了"分不开。

上游 `DungeonAdmissionRejectBuilder` 的拒绝体是 3 字节,发在**同一 type** 上:

```text
00 | 错误码 | 上下文
```

| 错误码 | 上游理由 |
|---|---|
| `0x01` | DungeonUnavailable / Unknown |
| `0x08` | NotPartyLeader |
| `0x11` | MissingRequiredItem |
| `0x13` | InvalidSelectionState |
| `0x15` | DungeonNotFound |
| `0x16` | InsufficientFatigue |
| `0xAD` | MemberEntryLimitReached / MissingPermission(上下文写队员槽位) |
| `0xF6` | DailyEntryLimitReached |

**客户端为这些码画什么句子还没读过。** 移动与使用那两类的拒绝码是从客户端字符串表里读出来的,这一类
不是;所以本仓按"服务端判定了什么"命名,不按"玩家看到什么"。

**本仓只发得出 `0x15 DungeonNotFound`。** 那是它唯一真的能判定的一条。等级门要档案的
`[minimum required level]` 加上上游那个 `WorldMap.IsStoryDungeon` 豁免 —— 少了豁免就会拒绝客户端
本来就提供的剧情副本,拿一个不忠实的门换沉默不划算。记在 `Docs/ProtocolScaffoldDissonances.md`。

线级实测:5000 与 65535 都回 `CMD 0x0010` 3 字节、连接存活;144 照常五帧进入。

## 三、又一个只有本仓见过的 type

`CMD 0x039D REQ_IGA_VER`,16 字节,在城里站着时发一次:

```text
00 5B 91 38 60 F5 11 3E 00 00 00 00 38 32 00 00
```

**两份参照抓包都没有它。** 与 `0x011B PI`、`0x0030 DECREASE_DURABILITY` 同一类:注册进"有意不回"
是为了让它不被误当成漏洞,但"上游也不回"这句话在它身上说不出口。

## 四、这一趟仍未应答的一条

`CMD 0x001F ACCEPT_QUEST` 又出现了一次,body 与上一趟逐字节相同(`1F 00 F8 03`,任务 1016)。
参照是**回的**,所以它是真缺口;没有实现的原因是任务表只有读路径。见
[a21-room-rules-and-leaving-20260826](a21-room-rules-and-leaving-20260826.md) 第五节。

## 还没做的

- **进入的准入规则。** 等级、疲劳、入场道具、每日次数 —— 一条都没有。拒绝帧的形状齐了,判定没有。
- **`CMD 0x001F ACCEPT_QUEST`。** 要先有任务接受的落库。
- 其余见前两篇。
