# A21 入场批次逐帧对账（2026-08-25）

> **2026-08-27 重取。** 本页首次写下时，本仓的角色建出来既没有装备也只有一半技能，
> 三个 USERINFO 子类型因此比参照短一大截。装备与技能页修好之后重跑，六帧差异还是那六帧，
> 但**其中两帧已经长度相同**，另两帧的差额缩到 2 字节。下表与下一节都是重取后的数字，
> 旧数字连同它们为什么变，一并留在最后一节。
>
> **参照文件要指名。** 上游输出目录里的 `packet_log.txt` 已于 2026-08-27 被一次新的
> 通关抓包覆盖。本页与各协议页引的都是 08-23 那一局，它以
> `packet_log.reference-20260823.txt` 存着；比对脚本必须指名这个文件，
> 指向目录默认名会静默比到另一局角色上去（会多报六帧不同）。

本仓的入场批次与 A21 参照会话的**逐帧比较**，两侧各 66 帧。

## 怎么取的

- **本仓侧**：`scratchpad/entryprobe.py` 用线级客户端走完「连接 → LOGIN → SELECT_CHARACTER」，
  记录服务端发出的每一帧。不驱动 GUI，因此与客户端能否接受无关——它记录的是**我们发了什么**。
  跑之前擦库重建，由服务端自己的 `CREATE_CHARACTER` 建号（当日补给每角色每天只发一次，
  第二次进入同一角色会少掉那一帧，用旧库比出来的差异是「哪次先跑」而不是代码）。
- **参照侧**：`ServerS4A21` 跑通一局副本留下的 458 帧 `packet_log.txt`，取第 56..121 帧
  （SELECT_CHARACTER 应答起，到 `ITEM_LOCK_LIST` 止）。原始字节已按「引用前先归档」抄进
  [a21-reference-entry-bytes-20260825](a21-reference-entry-bytes-20260825.md)——本页与各协议页
  引用的每一串字节都能在那里逐帧查到。
- 比较脚本：`cmpentry2.py`，与重取的字节一同存档在
  `.rks-agent/tasks/a21-world-entry-protocol/attempts/spec-003-world-entry-sequence/verification/`。
  按顺序对齐，标 `!` 协议不同、`~` 长度不同、`b` 长度同而字节不同。
  它按 **S→C 位置**取参照窗口；早先那版按行号取，十一行 C→S 落进窗口内，会把 66 帧数成 55 帧。

## 结果

**66 帧对 66 帧，协议多重集差为空**——每个 (族, type) 在两侧出现的次数相同，顺序相同。
**其中 60 帧的 body 与参照逐字节相同**，不只是长度相同；余下 6 帧就是下一节那六条，每条的差额都能用角色数据算平。
`#54 ENTER_GAMEWORLD_COMPLETE` 与 `#12 USERINFO 子类型 1` 都在这 60 帧里。

> **这个「差为空」只在当天首次进入该角色时成立。** 唯一的 `0x000E` 是当日补给的增量，
> 补给每角色每天只发一次；同一天第二次进场时本仓一帧都不发，而参照有一帧。
> 参照那一局是不是首次进场无从判断，所以无法区分「A21 总是发」与「因为补给发生了才发」。

```text
#    reference                    ours
----------------------------------------------------------------------------
0   b CMD  0x0004 261  SELECT_CHARACT CMD  0x0004 261  SELECT_CHARACT
1   ~ NOTI 0x0002 188  USERINFO    NOTI 0x0002 190  USERINFO   
2     NOTI 0x0166 11   TITLE_BOOK_LIS NOTI 0x0166 11   TITLE_BOOK_LIS
3     NOTI 0x0166 11   TITLE_BOOK_LIS NOTI 0x0166 11   TITLE_BOOK_LIS
4     NOTI 0x0166 11   TITLE_BOOK_LIS NOTI 0x0166 11   TITLE_BOOK_LIS
5     NOTI 0x0166 11   TITLE_BOOK_LIS NOTI 0x0166 11   TITLE_BOOK_LIS
6     NOTI 0x0166 11   TITLE_BOOK_LIS NOTI 0x0166 11   TITLE_BOOK_LIS
7     NOTI 0x0167 4    ACHIEVEMENT_LI NOTI 0x0167 4    ACHIEVEMENT_LI
8     NOTI 0x0015 25   ACCEPTABLE_QUE NOTI 0x0015 25   ACCEPTABLE_QUE
9     NOTI 0x0164 30004 CLEAR_QUEST_LI NOTI 0x0164 30004 CLEAR_QUEST_LI
10  b NOTI 0x0013 70   SKILLINFO   NOTI 0x0013 70   SKILLINFO  
11    NOTI 0x0069 1    CREATURE_ITEM_ NOTI 0x0069 1    CREATURE_ITEM_
12  b NOTI 0x0002 519  USERINFO    NOTI 0x0002 519  USERINFO   
13    NOTI 0x0003 4    USER_STATE  NOTI 0x0003 4    USER_STATE 
14    NOTI 0x00CA 1    VILLAGE_OBJECT NOTI 0x00CA 1    VILLAGE_OBJECT
15    NOTI 0x0005 2    DUNGEON_PERMIS NOTI 0x0005 2    DUNGEON_PERMIS
16    NOTI 0x000D 1419 ITEM_LIST   NOTI 0x000D 1419 ITEM_LIST  
17    NOTI 0x000D 5    ITEM_LIST   NOTI 0x000D 5    ITEM_LIST  
18    NOTI 0x000D 5    ITEM_LIST   NOTI 0x000D 5    ITEM_LIST  
19    NOTI 0x000D 3    ITEM_LIST   NOTI 0x000D 3    ITEM_LIST  
20    NOTI 0x000D 9    ITEM_LIST   NOTI 0x000D 9    ITEM_LIST  
21    NOTI 0x000D 3    ITEM_LIST   NOTI 0x000D 3    ITEM_LIST  
22    NOTI 0x0465 5    EPIC_BOOK_INFO NOTI 0x0465 5    EPIC_BOOK_INFO
23    NOTI 0x021E 1    SET_USABLE_COU NOTI 0x021E 1    SET_USABLE_COU
24    NOTI 0x0286 22   DAILY_CHALLENG NOTI 0x0286 22   DAILY_CHALLENG
25    NOTI 0x00AD 162  GAME_OPTION NOTI 0x00AD 162  GAME_OPTION
26    NOTI 0x01C7 203  HOTKEY_OPTION NOTI 0x01C7 203  HOTKEY_OPTION
27  ~ NOTI 0x006C 17   EVENT_INFO  NOTI 0x006C 3    EVENT_INFO 
28    NOTI 0x0187 4    CHARACTER_OPTI NOTI 0x0187 4    CHARACTER_OPTI
29    NOTI 0x01B9 1    INFORM_NOTICE_ NOTI 0x01B9 1    INFORM_NOTICE_
30    NOTI 0x015F 1    TOTAL_ITEM_MAK NOTI 0x015F 1    TOTAL_ITEM_MAK
31    NOTI 0x00AC 1    LOAD_COOLTIME_ NOTI 0x00AC 1    LOAD_COOLTIME_
32    NOTI 0x00AE 1    LOAD_EFFECT_IT NOTI 0x00AE 1    LOAD_EFFECT_IT
33    NOTI 0x017B 11   AURA_TYPE_COOL NOTI 0x017B 11   AURA_TYPE_COOL
34    NOTI 0x0381 8    USER_BUY_RESTR NOTI 0x0381 8    USER_BUY_RESTR
35    NOTI 0x0381 8    USER_BUY_RESTR NOTI 0x0381 8    USER_BUY_RESTR
36    NOTI 0x021F 4    CARD_BOOK_INFO NOTI 0x021F 4    CARD_BOOK_INFO
37    NOTI 0x03C1 8    EQUIPMENT_RENT NOTI 0x03C1 8    EQUIPMENT_RENT
38    NOTI 0x00FB 2    ITEM_LOCK_LIST NOTI 0x00FB 2    ITEM_LOCK_LIST
39    NOTI 0x00CD 2    EXPERT_JOB_INF NOTI 0x00CD 2    EXPERT_JOB_INF
40    NOTI 0x00B1 1    SHOP_COIN_EVEN NOTI 0x00B1 1    SHOP_COIN_EVEN
41    NOTI 0x036A 2    CONTRACT_OF_CU NOTI 0x036A 2    CONTRACT_OF_CU
42    NOTI 0x03D8 204  COUPLE_ROOM NOTI 0x03D8 204  COUPLE_ROOM
43    NOTI 0x025B 9    SEQUENTIAL_DUN NOTI 0x025B 9    SEQUENTIAL_DUN
44    NOTI 0x0331 1    LIMIT_IPGNO NOTI 0x0331 1    LIMIT_IPGNO
45    NOTI 0x01EB 3    USER_FATIGUE_A NOTI 0x01EB 3    USER_FATIGUE_A
46    NOTI 0x0061 6    MAILBOX_MAIL_L NOTI 0x0061 6    MAILBOX_MAIL_L
47    NOTI 0x0158 1    RESET_ONE_A_DA NOTI 0x0158 1    RESET_ONE_A_DA
48    NOTI 0x02D5 16   ACCOUNT_TUTORI NOTI 0x02D5 16   ACCOUNT_TUTORI
49    NOTI 0x02D5 16   ACCOUNT_TUTORI NOTI 0x02D5 16   ACCOUNT_TUTORI
50    NOTI 0x02D5 16   ACCOUNT_TUTORI NOTI 0x02D5 16   ACCOUNT_TUTORI
51    NOTI 0x01A8 1    RESET_CRANE_NE NOTI 0x01A8 1    RESET_CRANE_NE
52    NOTI 0x0009 2    PARTY_INFO  NOTI 0x0009 2    PARTY_INFO 
53    NOTI 0x0344 4    BUY_RESTRICT_I NOTI 0x0344 4    BUY_RESTRICT_I
54    NOTI 0x007C 8    ENTER_GAMEWORL NOTI 0x007C 8    ENTER_GAMEWORL
55    NOTI 0x0002 25   USERINFO    NOTI 0x0002 25   USERINFO   
56    CMD  0x01BA 7    MERCENARY_INFO CMD  0x01BA 7    MERCENARY_INFO
57    NOTI 0x0035 13   CERA        NOTI 0x0035 13   CERA       
58    NOTI 0x0111 8    UNITED_SERVER_ NOTI 0x0111 8    UNITED_SERVER_
59    NOTI 0x0016 9    USER_POSITION NOTI 0x0016 9    USER_POSITION
60    NOTI 0x000E 508  UPDATE_ITEM_LI NOTI 0x000E 508  UPDATE_ITEM_LI
61    NOTI 0x0165 3    CHARAC_INVISIB NOTI 0x0165 3    CHARAC_INVISIB
62    CMD  0x0239 5    SET_CLONE_TITL CMD  0x0239 5    SET_CLONE_TITL
63  ~ NOTI 0x0002 103  USERINFO    NOTI 0x0002 105  USERINFO   
64    NOTI 0x0289 8    HONOR_LEVEL_IN NOTI 0x0289 8    HONOR_LEVEL_IN
65    NOTI 0x00FB 2    ITEM_LOCK_LIST NOTI 0x00FB 2    ITEM_LOCK_LIST
```

## 仍不同的六帧，及其归类

**判成「数据差异」的门槛是能用角色数据把差额算平。** 下面六行都算平了；算不平的两条
（`GAME_OPTION`、`ITEM_LIST`）正是本轮从「数据」翻案成布局的那两条。

| # | 帧 | 参照 | 本仓 | 归类 |
|---:|---|---:|---:|---|
| 0 | CMD `0x0004` SELECT_CHARACTER | 261 | 261 | **数据**：仅偏移 5..8 的创建时间戳不同，其余 257 字节逐字节相同 |
| 1 | NOTI `0x0002` 子类型 0 | 188 | 190 | **数据**：两边定长基数都是 **157**，各带 1 件外观（`157+8+23 = 188`）；只差名字——参照 4 字节，本仓 `Nagisa` 6 字节 |
| 10 | NOTI `0x0013` SKILLINFO | 70 | 70 | **数据**：长度相同，差的是两个职业技能 id——参照角色是魔枪士，本仓是鬼剑士 |
| 12 | NOTI `0x0002` 子类型 1 | 519 | 519 | **数据**：长度相同，17 段差异全是角色数据（职业属性、装备 id、实例值） |
| 27 | NOTI `0x006C` EVENT_INFO | 17 | 3 | **数据**：参照服务器有一个进行中的活动（id `0xB5`），本仓发空表。这条与角色无关，是两台服务器的活动配置不同 |
| 63 | NOTI `0x0002` 子类型 2 | 103 | 105 | **数据**：同样只差名字的 2 字节 |

## 这一轮改掉的十处

每一处都由上面的对账判定，改完后该帧在多重集里消失：

| 处 | 之前 | 之后 | 依据 |
|---|---|---|---|
| NOTI `0x0381` | 发在 `0x03EB` | 发在 `0x0381` | 参照就在 `0x0381` 上发这两帧，且 `0x03EB` 在 A21 上有 handler 而 `0x0381` 没有 |
| NOTI `0x000D` | 5 个容器、条目 84 字节、主背包 9 个虚拟槽 | 6 个、101 字节、14 个 | 参照主背包声明 14 条、条目区 1414 字节，`1414/101 = 14` 整除 |
| NOTI `0x000E` | 批次里发空表 + 之后再发真表 | 只发真表，位置按参照 | 参照在这个窗口里只有一帧 `0x000E` |
| CMD `0x01BA` | 只答请求 | 批次里也主动发一帧 | 参照在 town-ready 与 CERA 之间发 7 字节 |
| CMD `0x036F` | 批次里发 77 字节 | 不发 | 参照 458 帧里 `0x036F` 与 `0x0312` 都没出现过 |
| NOTI `0x00AD` | 默认选项表 134 字节 | 150 字节 | 参照主选项 blob 150 字节（75 个 `u16`） |
| NOTI `0x0289` | 取角色派生统计（0） | 取账号荣誉（1） | 参照在花名册与入场发的 body 完全相同 |
| NOTI `0x0002` 子类型 0 尾部 | 逐字段 81 字节（旧客户端形状） | 固定 64 字节块 | 产出参照的服务端把它写成常量，与抓到的 64 字节只差已知的补丁点 |
| NOTI `0x0002` 子类型 1 尾部 | 存储里的 dimension 列表（新角色为空） | 固定 26 键的卷 + 两段常量 | 同上；最后 187 字节与参照逐字节相同 |
| NOTI `0x0002` 子类型 2 | 等级后写两个进度值 | 写实测字面量 `0B 01` + 八个零 | 参照的入场那帧与花名册那帧逐字节相同 |


## 旧数字，以及它们为什么变

本页 2026-08-25 首次写下时，本仓的角色是**空手且只有一半技能**建出来的——建号路径没读
`[create equipment list]`，被动技能也没落在 102 起的槽位上。那时的三个子类型分别是
167 / 25 / 330，与参照的 188 / 70 / 519 差着一大截，当时把差额按「参照角色装备技能多」
算平，结论方向是对的，但**算平的是本仓自己的缺陷**，不是两个角色的正常差异。

装备与技能修好之后（见
[a21-starting-gear-and-skills-20260826](a21-starting-gear-and-skills-20260826.md)）重取：
`SKILLINFO` 与子类型 1 的长度已经与参照**完全相同**，子类型 0 与子类型 2 只剩名字长度那 2 字节。
六帧的集合没有变，变的是每一帧还差多少——这正是「数据差异」这个归类应该有的样子。
