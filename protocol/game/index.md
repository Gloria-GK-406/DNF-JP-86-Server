# Game 协议全量索引

本页由 [`patterns/data/generate-docs.py`](../patterns/data/generate-docs.py) 生成，覆盖静态普查可见的全部协议身份。**重新运行会整体覆盖本文件。**

协议身份 = endpoint + direction + command 族 + type。同一 type 在 CMD 与 NOTI 下是两条毫不相干的协议——`0x0289` 在 CMD 是 `HARDCORE_MURDERER`、在 NOTI 是 `HONOR_LEVEL_INFO`，两族各有自己的文档，因此两族各自成表。

标 **(手写)** 的条目是人工逆向并经实机验证的权威记录，生成器不会改写它们；其余为静态分析初稿，`unknown` 表示证据不足而非尚未查看。

## Body 状态的含义

| 状态 | 含义 | 下一步该做什么 |
|---|---|---|
| 布局已闭合 | 字段序列与宽度由指令逐条读出，无分支、无变长原语 | 可直接据以实现；用真实样本复核总长 |
| 布局已闭合（含计数重复组） | 同上，但含按计数重复的字段组 | 需从样本或计数字段确定重复次数 |
| 布局部分可得 | 读出了部分字段，但存在分支、变长原语或多态尾部 | 补一个真实样本，或对分支点下断点 |
| 结构未知 | 字段边界无法由静态分析确定 | 只能靠运行时手段；静态再投入无收益 |

## Client → Server（全部为 CMD 族）

客户端的出站帧构造点把 command 字节写死为 `0x01`，因此**客户端只发 CMD、从不发 NOTI**。一条 CMD 的请求与其响应共用同一个 type、同属一次交换，记在同一篇文档内。

| Type | 名称 | Body 状态 | 长度规则 | 文档 |
|---|---|---|---|---|
| `0x0000` | `CHECK_CONNECTION` | 布局已闭合 | `8` | [0x0000.md](client-to-server/0x0000.md) |
| `0x0001` | `LOGIN` | 布局部分可得 | `—` | [0x0001.md](client-to-server/0x0001.md) |
| `0x0002` | `SET_UDP_IP_PORT` | ClientVerified（A21 双客户端组队路径） | `≥ 15`；当前真实样本 `31` | [0x0002.md](client-to-server/0x0002.md) |
| `0x0003` | `EXIT` | 布局已闭合 | `1` | [0x0003.md](client-to-server/0x0003.md) |
| `0x0004` | `SELECT_CHARACTER` | 布局已闭合 | `2` | [0x0004.md](client-to-server/0x0004.md) |
| `0x0005` | `CREATE_CHARACTER` | **手写记录** | 见文档 | [0x0005.md](client-to-server/0x0005.md) **(手写)** |
| `0x0006` | `DELETE_CHARACTER` | **手写记录** | 见文档 | [0x0006.md](client-to-server/0x0006.md) **(手写)** |
| `0x0007` | `RETURN_SELECT_CHARACTER` | 布局已闭合 | `0` | [0x0007.md](client-to-server/0x0007.md) |
| `0x0008` | `GET_USERINFO` | **手写记录；ClientVerified（mode 2/3）** | `3` | [0x0008.md](client-to-server/0x0008.md) **(手写)** |
| `0x0009` | `RECOVER_STAMINA` | 布局已闭合 | `2` | [0x0009.md](client-to-server/0x0009.md) |
| `0x000A` | `REQUEST_PEER` | ClientVerified（A21 type 0） | `7` 或 `9` | [0x000A.md](client-to-server/0x000A.md) |
| `0x000B` | `RESPONSE_PEER` | ClientVerified（A21 type 0；真实 body 待归档） | `7` 或 `16` | [0x000B.md](client-to-server/0x000B.md) |
| `0x000C` | `SET_PARTY_INFO` | ClientVerified（A21 12B direct 更新；inline-title 静态 High） | `12` 或 `16+n` | [0x000C.md](client-to-server/0x000C.md) |
| `0x000D` | `LEAVE_PARTY` | ClientVerified（A21 普通成员主动离队） | `0` | [0x000D.md](client-to-server/0x000D.md) |
| `0x000E` | `WALKOUT_PARTY_MEMBER` | 布局已闭合 | `1` | [0x000E.md](client-to-server/0x000E.md) |
| `0x000F` | `ENTER_SELECT_DUNGEON` | 布局部分可得 | `≥ 4` | [0x000F.md](client-to-server/0x000F.md) |
| `0x0010` | `SELECT_DUNGEON` | 布局部分可得 | `≥ 15` | [0x0010.md](client-to-server/0x0010.md) |
| `0x0011` | `SEND_MESSAGE` | 布局部分可得 | `—` | [0x0011.md](client-to-server/0x0011.md) |
| `0x0012` | `DELETE_ITEM` | 布局部分可得 | `≥ 18` | [0x0012.md](client-to-server/0x0012.md) |
| `0x0013` | `MOVE_ITEMSPACE` | 布局部分可得 | `≥ 28` | [0x0013.md](client-to-server/0x0013.md) |
| `0x0014` | `SORT_ITEM` | 布局部分可得 | `≥ 3` | [0x0014.md](client-to-server/0x0014.md) |
| `0x0015` | `BUY_ITEM` | 布局已闭合 | `16` | [0x0015.md](client-to-server/0x0015.md) |
| `0x0016` | `SELL_ITEM` | 布局已闭合 | `13` | [0x0016.md](client-to-server/0x0016.md) |
| `0x0017` | `REPAIR_EQUIPMENT` | 布局部分可得 | `≥ 7` | [0x0017.md](client-to-server/0x0017.md) |
| `0x0018` | `SET_ITEMTRADE_STATE` | 布局已闭合 | `1` | [0x0018.md](client-to-server/0x0018.md) |
| `0x0019` | `COMPOUND_ITEM` | 布局部分可得 | `≥ 9` | [0x0019.md](client-to-server/0x0019.md) |
| `0x001A` | `DISJOINT_ITEM` | 布局部分可得 | `≥ 9` | [0x001A.md](client-to-server/0x001A.md) |
| `0x001B` | `USE_LOTTERY_ITEM` | 布局已闭合 | `4` | [0x001B.md](client-to-server/0x001B.md) |
| `0x001C` | `CHANGE_SKILLSLOT` | 布局部分可得 | `≥ 7` | [0x001C.md](client-to-server/0x001C.md) |
| `0x001D` | `BUY_SKILL` | 布局部分可得 | `≥ 7` | [0x001D.md](client-to-server/0x001D.md) |
| `0x001E` | `INCREASE_STATUS` | 布局部分可得 | `≥ 2` | [0x001E.md](client-to-server/0x001E.md) |
| `0x001F` | `ACCEPT_QUEST` | 布局已闭合 | `4` | [0x001F.md](client-to-server/0x001F.md) |
| `0x0020` | `GIVEUP_QUEST` | 布局已闭合 | `4` | [0x0020.md](client-to-server/0x0020.md) |
| `0x0021` | `SET_QUEST_TRIGGER` | 布局部分可得 | `≥ 6` | [0x0021.md](client-to-server/0x0021.md) |
| `0x0022` | `FINISH_QUEST` | 布局已闭合 | `10` | [0x0022.md](client-to-server/0x0022.md) |
| `0x0023` | `SET_USER_POSITION` | 布局部分可得 | `≥ 7` | [0x0023.md](client-to-server/0x0023.md) |
| `0x0024` | `SET_USER_AREA` | 布局已闭合 | `16` | [0x0024.md](client-to-server/0x0024.md) |
| `0x0025` | `FINISH_LOADING` | 布局已闭合 | `8` | [0x0025.md](client-to-server/0x0025.md) |
| `0x0026` | `USE_SKILL` | 布局部分可得 | `≥ 9` | [0x0026.md](client-to-server/0x0026.md) |
| `0x0027` | `DIE_MONSTER` | 布局部分可得 | `≥ 54 + n×10` | [0x0027.md](client-to-server/0x0027.md) |
| `0x0028` | `DIE_CHARACTER` | 布局部分可得 | `≥ 6` | [0x0028.md](client-to-server/0x0028.md) |
| `0x0029` | `USE_COIN` | 布局已闭合 | `2` | [0x0029.md](client-to-server/0x0029.md) |
| `0x002A` | `GIVEUP_GAME` | 布局已闭合 | `0` | [0x002A.md](client-to-server/0x002A.md) |
| `0x002B` | `GET_ITEM` | 布局部分可得 | `≥ 22` | [0x002B.md](client-to-server/0x002B.md) |
| `0x002C` | `USE_STACKABLE` | 布局部分可得 | `≥ 15` | [0x002C.md](client-to-server/0x002C.md) |
| `0x002D` | `MOVE_MAP` | 布局部分可得 | `≥ 52 + n1×2 + n2×2 + n3×4` | [0x002D.md](client-to-server/0x002D.md) |
| `0x002E` | `SET_PLAY_RESULT` | 布局部分可得 | `≥ 42 + n×7` | [0x002E.md](client-to-server/0x002E.md) |
| `0x002F` | `DROP_ITEM` | 布局部分可得 | `≥ 0 + n×2` | [0x002F.md](client-to-server/0x002F.md) |
| `0x0030` | `DECREASE_DURABILITY` | 布局已闭合 | `1` | [0x0030.md](client-to-server/0x0030.md) |
| `0x0032` | `MAKE_PVP_ROOM` | 布局部分可得 | `≥ 13057` | [0x0032.md](client-to-server/0x0032.md) |
| `0x0033` | `ENTER_PVP_ROOM` | 布局已闭合 | `3` | [0x0033.md](client-to-server/0x0033.md) |
| `0x0034` | `SET_PVP_SEAT_STATE` | 布局已闭合 | `2` | [0x0034.md](client-to-server/0x0034.md) |
| `0x0035` | `SET_PVP_READY_STATE` | 布局已闭合 | `1` | [0x0035.md](client-to-server/0x0035.md) |
| `0x0036` | `SET_PVP_TEAM_MODE` | 布局部分可得 | `≥ 1` | [0x0036.md](client-to-server/0x0036.md) |
| `0x0037` | `DIE_PVP_CHARACTER` | 布局部分可得 | `≥ 7` | [0x0037.md](client-to-server/0x0037.md) |
| `0x0038` | `PVP_TIME_OUT` | 布局已闭合（含计数重复组） | `0 + n×4` | [0x0038.md](client-to-server/0x0038.md) |
| `0x0039` | `END_PVP_RESULT` | 布局已闭合 | `0` | [0x0039.md](client-to-server/0x0039.md) |
| `0x003A` | `RES_PVP_RANK` | 布局部分可得 | `≥ 70` | [0x003A.md](client-to-server/0x003A.md) |
| `0x003B` | `SET_PVP_MAP_INDEX` | 布局已闭合 | `2` | [0x003B.md](client-to-server/0x003B.md) |
| `0x003C` | `ADD_FRIEND` | 结构未知 | `—` | [0x003C.md](client-to-server/0x003C.md) |
| `0x003D` | `REMOVE_FRIEND` | 结构未知 | `—` | [0x003D.md](client-to-server/0x003D.md) |
| `0x003E` | `DEBUG_COMMAND` | 布局部分可得 | `≥ 6` | [0x003E.md](client-to-server/0x003E.md) |
| `0x003F` | `CERA` | 布局已闭合 | `0` | [0x003F.md](client-to-server/0x003F.md) |
| `0x0040` | `BUY_CERASHOP_ITEM` | 布局部分可得 | `≥ 19` | [0x0040.md](client-to-server/0x0040.md) |
| `0x0041` | `GEN_CERATICKET` | 布局已闭合 | `0` | [0x0041.md](client-to-server/0x0041.md) |
| `0x0042` | `REQUEST_PVPEXP_OF_WEEK` | 布局已闭合 | `0` | [0x0042.md](client-to-server/0x0042.md) |
| `0x0043` | `GUILD_MEMER_LIST` | 布局已闭合 | `0` | [0x0043.md](client-to-server/0x0043.md) |
| `0x0044` | `CALL_GUILD_CREATE_RIGHT` | 结构未知 | `—` | [0x0044.md](client-to-server/0x0044.md) |
| `0x0045` | `SCORE_SCROLL_STATE` | 布局已闭合 | `0` | [0x0045.md](client-to-server/0x0045.md) |
| `0x0046` | `CARD_SELECT_RIGHT_STATE` | 布局已闭合 | `0` | [0x0046.md](client-to-server/0x0046.md) |
| `0x0047` | `SELECT_CARD` | 布局部分可得 | `≥ 2` | [0x0047.md](client-to-server/0x0047.md) |
| `0x0048` | `EPLP_COMMAND` | 布局已闭合 | `2` | [0x0048.md](client-to-server/0x0048.md) |
| `0x0049` | `CALL_GUILD_LEVEL_UP` | 布局已闭合 | `0` | [0x0049.md](client-to-server/0x0049.md) |
| `0x004B` | `REQUEST_GUILD_ENTER` | 结构未知 | `—` | [0x004B.md](client-to-server/0x004B.md) |
| `0x0050` | `UPGRADE_ITEM` | 布局部分可得 | `≥ 140` | [0x0050.md](client-to-server/0x0050.md) |
| `0x0051` | `RESET_ITEM_ATTR` | 结构未知 | `—` | [0x0051.md](client-to-server/0x0051.md) |
| `0x0052` | `BUY_PRIVATE_STORE_ITEM` | 结构未知 | `—` | [0x0052.md](client-to-server/0x0052.md) |
| `0x0053` | `ENTER_PRIVATE_STORE` | 结构未知 | `—` | [0x0053.md](client-to-server/0x0053.md) |
| `0x0054` | `EXIT_PRIVATE_STORE` | 结构未知 | `—` | [0x0054.md](client-to-server/0x0054.md) |
| `0x0055` | `CREATE_PRIVATE_STORE` | 结构未知 | `—` | [0x0055.md](client-to-server/0x0055.md) |
| `0x0056` | `REMOVE_PRIVATE_STORE` | 结构未知 | `—` | [0x0056.md](client-to-server/0x0056.md) |
| `0x0057` | `COMPLETE_DISPLAY` | 结构未知 | `—` | [0x0057.md](client-to-server/0x0057.md) |
| `0x0058` | `MOVE_TO_GATE` | 结构未知 | `—` | [0x0058.md](client-to-server/0x0058.md) |
| `0x005A` | `ENTER_WARROOM` | 布局已闭合 | `2` | [0x005A.md](client-to-server/0x005A.md) |
| `0x005B` | `SET_WARROOM_SEAT_STATE` | 布局部分可得 | `≥ 2` | [0x005B.md](client-to-server/0x005B.md) |
| `0x005C` | `DIE_WARROOM_CHARACTER` | 布局部分可得 | `≥ 20` | [0x005C.md](client-to-server/0x005C.md) |
| `0x005E` | `MAILBOX_SEND` | 结构未知 | `—` | [0x005E.md](client-to-server/0x005E.md) |
| `0x005F` | `MAILBOX_EXTRACT_ITEM` | 布局已闭合 | `4` | [0x005F.md](client-to-server/0x005F.md) |
| `0x0060` | `MAILBOX_OPEN` | 结构未知 | `—` | [0x0060.md](client-to-server/0x0060.md) |
| `0x0062` | `QUICK_JOIN_ROOM` | 布局已闭合 | `1` | [0x0062.md](client-to-server/0x0062.md) |
| `0x0063` | `COMPOUND_AVATAR` | 布局部分可得 | `≥ 22` | [0x0063.md](client-to-server/0x0063.md) |
| `0x0064` | `RENAME_CREATURE` | 布局部分可得 | `—` | [0x0064.md](client-to-server/0x0064.md) |
| `0x0065` | `RESPONSE_CREATURE` | 布局部分可得 | `≥ 2` | [0x0065.md](client-to-server/0x0065.md) |
| `0x0066` | `HATCH_CREATURE` | 结构未知 | `—` | [0x0066.md](client-to-server/0x0066.md) |
| `0x006A` | `WORLD_CUP_HIT_COUNT` | 布局部分可得 | `≥ 8` | [0x006A.md](client-to-server/0x006A.md) |
| `0x006B` | `GM_COMMAND` | 结构未知 | `—` | [0x006B.md](client-to-server/0x006B.md) |
| `0x006C` | `REPORT_4_HACK` | 布局已闭合 | `2` | [0x006C.md](client-to-server/0x006C.md) |
| `0x006D` | `GUILD_WAR_INFO` | 布局已闭合 | `0` | [0x006D.md](client-to-server/0x006D.md) |
| `0x006E` | `PVP_HEART_BEAT` | 布局已闭合 | `0` | [0x006E.md](client-to-server/0x006E.md) |
| `0x006F` | `CODE_CHECKSUM` | 结构未知 | `—` | [0x006F.md](client-to-server/0x006F.md) |
| `0x0070` | `PVP_REQUEST_FIGHT` | 布局已闭合 | `0` | [0x0070.md](client-to-server/0x0070.md) |
| `0x0071` | `MOUSEREGISTER` | 布局已闭合 | `1` | [0x0071.md](client-to-server/0x0071.md) |
| `0x0076` | `REGISITER_TO_BLACKLIST` | 布局部分可得 | `—` | [0x0076.md](client-to-server/0x0076.md) |
| `0x0077` | `DELETE_TO_BLACKLIST` | 布局部分可得 | `—` | [0x0077.md](client-to-server/0x0077.md) |
| `0x0078` | `REQUEST_BLACKLIST` | 布局已闭合 | `0` | [0x0078.md](client-to-server/0x0078.md) |
| `0x0079` | `CHANGE_HOST` | 布局已闭合 | `1` | [0x0079.md](client-to-server/0x0079.md) |
| `0x007A` | `CREATURE_SCRIPT_MESSAGE` | 布局部分可得 | `—` | [0x007A.md](client-to-server/0x007A.md) |
| `0x007B` | `CHARACTER_STATISTIC` | 布局部分可得 | `≥ 24` | [0x007B.md](client-to-server/0x007B.md) |
| `0x007D` | `GUILDMEMBER_NAMING` | 布局部分可得 | `—` | [0x007D.md](client-to-server/0x007D.md) |
| `0x007E` | `SET_SUB_GUILD_MASTER` | 布局部分可得 | `—` | [0x007E.md](client-to-server/0x007E.md) |
| `0x0086` | `CHANGE_LETTER_STAT` | 布局已闭合 | `6` | [0x0086.md](client-to-server/0x0086.md) |
| `0x0087` | `CHANGE_CHARAC_NAME` | 布局部分可得 | `—` | [0x0087.md](client-to-server/0x0087.md) |
| `0x0088` | `QUERY_CHARAC_INFO` | 布局部分可得 | `—` | [0x0088.md](client-to-server/0x0088.md) |
| `0x0089` | `REPORT_MANNERLESS_USER` | 布局部分可得 | `—` | [0x0089.md](client-to-server/0x0089.md) |
| `0x008A` | `ALLDIE_MONSTER` | 布局已闭合 | `0` | [0x008A.md](client-to-server/0x008A.md) |
| `0x008B` | `GUILD_MEMER_LIST_NEXT` | 结构未知 | `—` | [0x008B.md](client-to-server/0x008B.md) |
| `0x008C` | `GUILD_ALL_MEMBER_LIST` | 布局已闭合 | `4` | [0x008C.md](client-to-server/0x008C.md) |
| `0x008D` | `GUILD_ALL_MEMBER_LIST_NEXT` | 结构未知 | `—` | [0x008D.md](client-to-server/0x008D.md) |
| `0x008E` | `RPY_HUMAN_CERTIFY` | 布局部分可得 | `≥ 10` | [0x008E.md](client-to-server/0x008E.md) |
| `0x008F` | `CHANGE_TUTORIAL_FLAG` | **手写记录** | 见文档 | [0x008F.md](client-to-server/0x008F.md) **(手写)** |
| `0x0091` | `COMPLETE_LOAD_ASSAULT` | 布局已闭合 | `0` | [0x0091.md](client-to-server/0x0091.md) |
| `0x0093` | `DIE_ASSAULT_PLAYER` | 布局部分可得 | `≥ 17` | [0x0093.md](client-to-server/0x0093.md) |
| `0x0094` | `REVIVAL_ASSAULT_PLAYER` | 结构未知 | `—` | [0x0094.md](client-to-server/0x0094.md) |
| `0x0097` | `CALL_GUILD_INVITE` | 布局部分可得 | `—` | [0x0097.md](client-to-server/0x0097.md) |
| `0x0098` | `REPLY_GUILD_INVITE` | 布局已闭合 | `1` | [0x0098.md](client-to-server/0x0098.md) |
| `0x0099` | `REQ_GUILD_SECEDE` | 布局已闭合 | `4` | [0x0099.md](client-to-server/0x0099.md) |
| `0x009A` | `NOTIFY_MESSAGE_TO_GUILD` | 布局部分可得 | `—` | [0x009A.md](client-to-server/0x009A.md) |
| `0x009B` | `GUILD_MASTER_DELEGATE` | 结构未知 | `—` | [0x009B.md](client-to-server/0x009B.md) |
| `0x009C` | `CHECK_GUILD_NAME_DOUBLE` | 布局部分可得 | `—` | [0x009C.md](client-to-server/0x009C.md) |
| `0x009D` | `CHECK_GUILD_ADDREASS_DOUBLE` | 结构未知 | `—` | [0x009D.md](client-to-server/0x009D.md) |
| `0x009E` | `OPEN_GUILD_CREATE_WINDOW` | 布局已闭合 | `0` | [0x009E.md](client-to-server/0x009E.md) |
| `0x009F` | `DEATH_TOWER_STAGE_CMD` | 布局部分可得 | `≥ 1` | [0x009F.md](client-to-server/0x009F.md) |
| `0x00A0` | `USE_BOOSTER_ITEM` | 布局部分可得 | `≥ 2` | [0x00A0.md](client-to-server/0x00A0.md) |
| `0x00A1` | `SECURITY_CARD_ISSUE` | 结构未知 | `—` | [0x00A1.md](client-to-server/0x00A1.md) |
| `0x00A2` | `SECURITY_CARD_DISUSE` | 布局已闭合 | `0` | [0x00A2.md](client-to-server/0x00A2.md) |
| `0x00A3` | `SECURITY_CARD_AUTH_REQ` | 布局已闭合 | `0` | [0x00A3.md](client-to-server/0x00A3.md) |
| `0x00A4` | `SECURITY_CARD_AUTH_RPY` | 结构未知 | `—` | [0x00A4.md](client-to-server/0x00A4.md) |
| `0x00A5` | `SECURITY_CARD_CERT_KEY` | 结构未知 | `—` | [0x00A5.md](client-to-server/0x00A5.md) |
| `0x00A6` | `CALL_PARTY_MEMBER_REALTIME_INFO` | ClientVerified（A21 当前在队查询） | `0` | [0x00A6.md](client-to-server/0x00A6.md) |
| `0x00A7` | `EVADE_ASSAULT` | 结构未知 | `—` | [0x00A7.md](client-to-server/0x00A7.md) |
| `0x00AB` | `CLIENT_SPEC_STATISTIC` | **手写记录** | 见文档 | [0x00AB.md](client-to-server/0x00AB.md) **(手写)** |
| `0x00AC` | `SECURITY_CARD_AUTH_CANCEL` | 结构未知 | `—` | [0x00AC.md](client-to-server/0x00AC.md) |
| `0x00B0` | `GMDEBUG_COMMAND` | 布局部分可得 | `≥ 176` | [0x00B0.md](client-to-server/0x00B0.md) |
| `0x00B1` | `JOIN_POWER` | 结构未知 | `—` | [0x00B1.md](client-to-server/0x00B1.md) |
| `0x00B2` | `SECEDE_POWER` | 结构未知 | `—` | [0x00B2.md](client-to-server/0x00B2.md) |
| `0x00B3` | `CHANGE_GUILD_NAME` | 布局部分可得 | `≥ 7` | [0x00B3.md](client-to-server/0x00B3.md) |
| `0x00B5` | `SDC_ACTIVESTATUS_CHECK` | 布局部分可得 | `≥ 1` | [0x00B5.md](client-to-server/0x00B5.md) |
| `0x00B6` | `AUCTION_ASK_AVERAGE_PRICE` | 布局部分可得 | `≥ 6` | [0x00B6.md](client-to-server/0x00B6.md) |
| `0x00B7` | `AUCTION_REGIST_ITEM` | 布局部分可得 | `≥ 28` | [0x00B7.md](client-to-server/0x00B7.md) |
| `0x00B8` | `AUCTION_REGIST_CANCEL` | 布局已闭合 | `9` | [0x00B8.md](client-to-server/0x00B8.md) |
| `0x00B9` | `AUCTION_BIDDING` | 布局部分可得 | `≥ 26` | [0x00B9.md](client-to-server/0x00B9.md) |
| `0x00BA` | `AUCTION_SEARCH_BY_ITEMKEY` | 布局部分可得 | `≥ 12 + n1×4 + n2×4` | [0x00BA.md](client-to-server/0x00BA.md) |
| `0x00BB` | `AUCTION_SEARCH_BY_NOITEMKEY` | 布局部分可得 | `≥ 14 + n×4` | [0x00BB.md](client-to-server/0x00BB.md) |
| `0x00BC` | `AUCTION_MY_REGISTED_ITEM_INFO` | 布局已闭合 | `1` | [0x00BC.md](client-to-server/0x00BC.md) |
| `0x00BD` | `AUCTION_MY_BIDDING_INFO` | 布局已闭合 | `1` | [0x00BD.md](client-to-server/0x00BD.md) |
| `0x00BF` | `DUNGEON_EVENT_STORY_PAUSE` | 布局已闭合 | `2` | [0x00BF.md](client-to-server/0x00BF.md) |
| `0x00C0` | `JOIN_POWER_WAR` | 结构未知 | `—` | [0x00C0.md](client-to-server/0x00C0.md) |
| `0x00C1` | `GOBLIN_PAD_STATUS` | 结构未知 | `—` | [0x00C1.md](client-to-server/0x00C1.md) |
| `0x00C2` | `FRAME_LAG_STATISTICS` | **手写记录** | 见文档 | [0x00C2.md](client-to-server/0x00C2.md) **(手写)** |
| `0x00C3` | `PVP_CHANNEL_INFO` | 布局已闭合 | `0` | [0x00C3.md](client-to-server/0x00C3.md) |
| `0x00C4` | `REQUEST_MATCH` | 布局已闭合 | `1` | [0x00C4.md](client-to-server/0x00C4.md) |
| `0x00C5` | `SAVE_GAME_OPTION_1` | 结构未知 | `—` | [0x00C5.md](client-to-server/0x00C5.md) |
| `0x00C6` | `SAVE_GAME_OPTION_2` | 布局部分可得 | `—` | [0x00C6.md](client-to-server/0x00C6.md) |
| `0x00C7` | `SECURITY_CARD_RETRANSFER` | 布局已闭合 | `0` | [0x00C7.md](client-to-server/0x00C7.md) |
| `0x00C8` | `CERA_IDENTIFY` | 结构未知 | `—` | [0x00C8.md](client-to-server/0x00C8.md) |
| `0x00C9` | `USE_EMBLEM` | 布局已闭合（含计数重复组） | `8 + n×7` | [0x00C9.md](client-to-server/0x00C9.md) |
| `0x00CA` | `DISJOINT_AVATAR` | 布局部分可得 | `≥ 6` | [0x00CA.md](client-to-server/0x00CA.md) |
| `0x00CC` | `PURIFY_ITEM` | 布局部分可得 | `≥ 12` | [0x00CC.md](client-to-server/0x00CC.md) |
| `0x00CD` | `INVEST_ITEM_AMPLIFY_OPTION` | 布局部分可得 | `≥ 142` | [0x00CD.md](client-to-server/0x00CD.md) |
| `0x00CE` | `ADD_AVATAR_SOCKET` | 布局部分可得 | `≥ 8` | [0x00CE.md](client-to-server/0x00CE.md) |
| `0x00CF` | `SHOP_COIN_EVENT` | 布局已闭合 | `0` | [0x00CF.md](client-to-server/0x00CF.md) |
| `0x00D0` | `USE_RANDOMBOX_ITEM` | 布局已闭合 | `5` | [0x00D0.md](client-to-server/0x00D0.md) |
| `0x00D1` | `UDP_CHARACTERISTIC` | 布局已闭合 | `7` | [0x00D1.md](client-to-server/0x00D1.md) |
| `0x00D2` | `ONEDAY_LETHE` | 布局已闭合 | `1` | [0x00D2.md](client-to-server/0x00D2.md) |
| `0x00D3` | `DISGUISE_REQUEST` | 布局已闭合 | `9` | [0x00D3.md](client-to-server/0x00D3.md) |
| `0x00D4` | `DISGUISE_CANCEL` | 布局已闭合 | `0` | [0x00D4.md](client-to-server/0x00D4.md) |
| `0x00D5` | `REQUEST_PC_ROOM_PLAYER_LIST` | 布局已闭合 | `0` | [0x00D5.md](client-to-server/0x00D5.md) |
| `0x00D6` | `REQUEST_PC_ROOM_PLAYER_COUNT` | 结构未知 | `—` | [0x00D6.md](client-to-server/0x00D6.md) |
| `0x00D7` | `USE_VENDING_MACHINE` | 布局已闭合 | `10` | [0x00D7.md](client-to-server/0x00D7.md) |
| `0x00D8` | `ASSERT_INFO` | 布局部分可得 | `≥ 6` | [0x00D8.md](client-to-server/0x00D8.md) |
| `0x00D9` | `OVERFLOW_INFO` | 布局已闭合 | `3` | [0x00D9.md](client-to-server/0x00D9.md) |
| `0x00DA` | `SERVER_MESSAGE_SEND` | 布局部分可得 | `—` | [0x00DA.md](client-to-server/0x00DA.md) |
| `0x00DB` | `SERVER_MESSAGE_CHECK` | 布局已闭合 | `1` | [0x00DB.md](client-to-server/0x00DB.md) |
| `0x00DE` | `GIVE_GIFT_TO_NPC` | 布局已闭合 | `13` | [0x00DE.md](client-to-server/0x00DE.md) |
| `0x00DF` | `GOBLIN_PAD_RESPONSE_CRYPT_KEY` | 结构未知 | `—` | [0x00DF.md](client-to-server/0x00DF.md) |
| `0x00E0` | `WRITE_GUILD_MEMBER_MEMO` | 布局部分可得 | `—` | [0x00E0.md](client-to-server/0x00E0.md) |
| `0x00E3` | `CHECK_CREATE_GUILD_AGIT` | 结构未知 | `—` | [0x00E3.md](client-to-server/0x00E3.md) |
| `0x00E4` | `CREATE_GUILD_AGIT` | 布局已闭合 | `0` | [0x00E4.md](client-to-server/0x00E4.md) |
| `0x00E5` | `DELETE_GUILD_AGIT` | 布局已闭合 | `0` | [0x00E5.md](client-to-server/0x00E5.md) |
| `0x00E6` | `POWER_WAR_INFO` | 结构未知 | `—` | [0x00E6.md](client-to-server/0x00E6.md) |
| `0x00E7` | `UPGRADE_GUILD_AGIT` | 布局已闭合 | `1` | [0x00E7.md](client-to-server/0x00E7.md) |
| `0x00EA` | `CREATE_DISJOINT_STORE` | 结构未知 | `—` | [0x00EA.md](client-to-server/0x00EA.md) |
| `0x00EB` | `REQUEST_DISJOINT_ITEM` | 布局部分可得 | `≥ 5` | [0x00EB.md](client-to-server/0x00EB.md) |
| `0x00EC` | `REPAIR_DISJOINT_MACHINE` | 结构未知 | `—` | [0x00EC.md](client-to-server/0x00EC.md) |
| `0x00ED` | `TELEPORT` | 布局已闭合 | `13` | [0x00ED.md](client-to-server/0x00ED.md) |
| `0x00EE` | `COMPOUND_ITEM_BY_EXPERT_JOB` | 布局已闭合 | `8` | [0x00EE.md](client-to-server/0x00EE.md) |
| `0x00EF` | `GIVEUP_EXPERT_JOB` | 布局已闭合 | `0` | [0x00EF.md](client-to-server/0x00EF.md) |
| `0x00F0` | `UPGRADE_DISJOINT_MACHINE` | 布局已闭合 | `0` | [0x00F0.md](client-to-server/0x00F0.md) |
| `0x00F1` | `ENTER_DISJOINT_STORE` | 结构未知 | `—` | [0x00F1.md](client-to-server/0x00F1.md) |
| `0x00F4` | `LOAD_COMPLETE_AFTER_ASSAULT` | 布局已闭合 | `0` | [0x00F4.md](client-to-server/0x00F4.md) |
| `0x00F7` | `GUILD_CARGO_PUSH_ITEM` | 布局部分可得 | `≥ 13` | [0x00F7.md](client-to-server/0x00F7.md) |
| `0x00F8` | `GUILD_CARGO_POP_ITEM` | 布局部分可得 | `≥ 11` | [0x00F8.md](client-to-server/0x00F8.md) |
| `0x00F9` | `GUILD_CARGO_MOVE_ITEM` | 布局已闭合 | `12` | [0x00F9.md](client-to-server/0x00F9.md) |
| `0x00FA` | `LODING_TIME_REPORT` | **手写记录** | 见文档 | [0x00FA.md](client-to-server/0x00FA.md) **(手写)** |
| `0x00FB` | `USE_SHARED_EFFECT_ITEM` | 布局已闭合 | `6` | [0x00FB.md](client-to-server/0x00FB.md) |
| `0x00FC` | `BUY_CERASHOP_LIMIT_ITEM` | 布局部分可得 | `≥ 5` | [0x00FC.md](client-to-server/0x00FC.md) |
| `0x00FD` | `ADD_HACKTYPE_CNT` | 布局已闭合 | `3` | [0x00FD.md](client-to-server/0x00FD.md) |
| `0x00FE` | `CHANGE_EMOTION` | 布局已闭合 | `2` | [0x00FE.md](client-to-server/0x00FE.md) |
| `0x00FF` | `DIE_BLOOD_MONSTER` | 布局部分可得 | `≥ 1 + n×2` | [0x00FF.md](client-to-server/0x00FF.md) |
| `0x0100` | `COMPOUND_EMBLEM` | 布局部分可得 | `≥ 1 + n×6` | [0x0100.md](client-to-server/0x0100.md) |
| `0x0102` | `BLOOD_ROUND_UI_PREPARE_FINISH_` | 布局已闭合 | `0` | [0x0102.md](client-to-server/0x0102.md) |
| `0x0103` | `REQUEST_CONDITION_EVENT_REWARD` | 布局已闭合 | `3` | [0x0103.md](client-to-server/0x0103.md) |
| `0x0104` | `CHANGE_ANOTHER_SKILL_TREE` | 布局已闭合 | `1` | [0x0104.md](client-to-server/0x0104.md) |
| `0x0105` | `GUILD_CARGO` | 布局已闭合 | `0` | [0x0105.md](client-to-server/0x0105.md) |
| `0x0106` | `GUILD_CARGO_HISTORY` | 布局已闭合 | `0` | [0x0106.md](client-to-server/0x0106.md) |
| `0x0107` | `FIGHT_VILLAGE_MONSTER` | 布局已闭合 | `2` | [0x0107.md](client-to-server/0x0107.md) |
| `0x0108` | `FINISH_VILLAGE_MONSTER_FIGHTING` | 布局已闭合 | `0` | [0x0108.md](client-to-server/0x0108.md) |
| `0x0109` | `UPGRADE_GUILD_CARGO` | 布局已闭合 | `0` | [0x0109.md](client-to-server/0x0109.md) |
| `0x010B` | `REQUEST_ITEM_LOCK` | 布局已闭合 | `3` | [0x010B.md](client-to-server/0x010B.md) |
| `0x010C` | `REQUEST_ITEM_UNLOCK` | 结构未知 | `—` | [0x010C.md](client-to-server/0x010C.md) |
| `0x010D` | `REQUEST_ITEM_UNLOCK_CANCEL` | 结构未知 | `—` | [0x010D.md](client-to-server/0x010D.md) |
| `0x010F` | `UPGRADE_CHRONICLE` | 布局已闭合 | `19` | [0x010F.md](client-to-server/0x010F.md) |
| `0x0110` | `ENCHANT_BY_BEAD` | 布局部分可得 | `≥ 6` | [0x0110.md](client-to-server/0x0110.md) |
| `0x0111` | `DUNGEON_NPC_BUFF_INFO` | 结构未知 | `—` | [0x0111.md](client-to-server/0x0111.md) |
| `0x0117` | `LAG_STATISTICS` | **手写记录** | 见文档 | [0x0117.md](client-to-server/0x0117.md) **(手写)** |
| `0x0118` | `HT_PS` | 布局已闭合 | `3` | [0x0118.md](client-to-server/0x0118.md) |
| `0x0119` | `HT_IS` | 布局已闭合 | `1` | [0x0119.md](client-to-server/0x0119.md) |
| `0x011B` | `PI` | 布局已闭合 | `4` | [0x011B.md](client-to-server/0x011B.md) |
| `0x011C` | `VERIFY_GOLD` | 布局部分可得 | `≥ 6` | [0x011C.md](client-to-server/0x011C.md) |
| `0x011E` | `REQUEST_ADD_PVP_BUDDY` | 结构未知 | `—` | [0x011E.md](client-to-server/0x011E.md) |
| `0x011F` | `RESPONSE_ADD_PVP_BUDDY` | 布局部分可得 | `—` | [0x011F.md](client-to-server/0x011F.md) |
| `0x0120` | `REMOVE_PVP_BUDDY` | 结构未知 | `—` | [0x0120.md](client-to-server/0x0120.md) |
| `0x0122` | `ADD_UNITED_SERVER_FRIEND` | 布局部分可得 | `—` | [0x0122.md](client-to-server/0x0122.md) |
| `0x0123` | `DELETE_UNITED_SERVER_FRIEND` | 布局部分可得 | `—` | [0x0123.md](client-to-server/0x0123.md) |
| `0x0124` | `CHECK_FINISH_LOADING` | 结构未知 | `—` | [0x0124.md](client-to-server/0x0124.md) |
| `0x0125` | `NCC` | 布局已闭合 | `5` | [0x0125.md](client-to-server/0x0125.md) |
| `0x0126` | `MI` | 布局已闭合 | `2` | [0x0126.md](client-to-server/0x0126.md) |
| `0x0127` | `CHANGE_CHARAC_SLOT` | 布局已闭合 | `8` | [0x0127.md](client-to-server/0x0127.md) |
| `0x0128` | `SECRET_SHOP_BUY_ITEM` | 布局部分可得 | `≥ 24` | [0x0128.md](client-to-server/0x0128.md) |
| `0x0129` | `SECRET_SHOP_OPEN_CLOSE` | 布局已闭合 | `1` | [0x0129.md](client-to-server/0x0129.md) |
| `0x012A` | `COMPLETE_LOAD_PVP` | 布局已闭合 | `0` | [0x012A.md](client-to-server/0x012A.md) |
| `0x012B` | `CONNECT_P2P_PVP` | 布局部分可得 | `≥ 1 + n×2` | [0x012B.md](client-to-server/0x012B.md) |
| `0x012C` | `BIDDING_ROUTING_ITEM` | 布局已闭合 | `1` | [0x012C.md](client-to-server/0x012C.md) |
| `0x012F` | `BREAK_GUILD` | 布局已闭合 | `0` | [0x012F.md](client-to-server/0x012F.md) |
| `0x0131` | `CREATE_ACCOUNT_CARGO` | 布局已闭合 | `0` | [0x0131.md](client-to-server/0x0131.md) |
| `0x0132` | `UPGRADE_ACCOUNT_CARGO` | 布局已闭合 | `0` | [0x0132.md](client-to-server/0x0132.md) |
| `0x0133` | `DEPOSIT_MONEY` | 布局已闭合 | `4` | [0x0133.md](client-to-server/0x0133.md) |
| `0x0134` | `WITHDRAW_MONEY` | 布局已闭合 | `4` | [0x0134.md](client-to-server/0x0134.md) |
| `0x0136` | `REDEEM` | 布局部分可得 | `≥ 21` | [0x0136.md](client-to-server/0x0136.md) |
| `0x0138` | `CONNECT_LINK_CHARAC` | 布局已闭合 | `4` | [0x0138.md](client-to-server/0x0138.md) |
| `0x0139` | `DISCONNECT_LINK_CHARAC` | 布局已闭合 | `2` | [0x0139.md](client-to-server/0x0139.md) |
| `0x013B` | `MULTI_MAILBOX_SEND` | 结构未知 | `—` | [0x013B.md](client-to-server/0x013B.md) |
| `0x013C` | `OPERATE_RIDABLE_OBJECT` | 布局部分可得 | `≥ 7` | [0x013C.md](client-to-server/0x013C.md) |
| `0x013D` | `SELECT_ULTIMATE_DIFFICULTY` | 布局已闭合 | `1` | [0x013D.md](client-to-server/0x013D.md) |
| `0x0144` | `QUERY_CHARAC_INFO_MAILBOX` | 布局部分可得 | `—` | [0x0144.md](client-to-server/0x0144.md) |
| `0x0145` | `COMPOUND_ITEM_BIND_SHPERE` | 布局部分可得 | `≥ 7` | [0x0145.md](client-to-server/0x0145.md) |
| `0x014A` | `FIND_CHAR_NAME_USE_CHARAC_NO` | 结构未知 | `—` | [0x014A.md](client-to-server/0x014A.md) |
| `0x014B` | `SKILL_COMMAND_CUSTOMIZING` | 布局部分可得 | `≥ 1 + n×6` | [0x014B.md](client-to-server/0x014B.md) |
| `0x014C` | `SKILL_COMMAND_ALL_DEFAULT` | 布局已闭合 | `0` | [0x014C.md](client-to-server/0x014C.md) |
| `0x014E` | `AUCTION_BUY_ITEM_APIECE` | 布局已闭合 | `33` | [0x014E.md](client-to-server/0x014E.md) |
| `0x014F` | `CHANGE_PARTY_MEMBER_POSITION` | 布局已闭合 | `2` | [0x014F.md](client-to-server/0x014F.md) |
| `0x0151` | `SCAN_BOT_BY_DLL` | 布局已闭合 | `7` | [0x0151.md](client-to-server/0x0151.md) |
| `0x0152` | `USE_LIMIT_CUBE` | 布局部分可得 | `≥ 8` | [0x0152.md](client-to-server/0x0152.md) |
| `0x0153` | `REFRESH_GUILD_INFO` | 布局已闭合 | `0` | [0x0153.md](client-to-server/0x0153.md) |
| `0x0154` | `OPEN_GUILD_BOARD` | 布局已闭合 | `1` | [0x0154.md](client-to-server/0x0154.md) |
| `0x0155` | `WRITE_ON_THE_GUILDBOARD` | 布局部分可得 | `—` | [0x0155.md](client-to-server/0x0155.md) |
| `0x0156` | `DELETE_GUILDBOARD_TEXT` | 布局已闭合 | `4` | [0x0156.md](client-to-server/0x0156.md) |
| `0x015A` | `DONATE_GUILD_FUND` | 布局已闭合 | `4` | [0x015A.md](client-to-server/0x015A.md) |
| `0x015B` | `CHECK_JOIN_GUILD` | 结构未知 | `—` | [0x015B.md](client-to-server/0x015B.md) |
| `0x015C` | `REQUEST_JOIN_GUILD` | 布局部分可得 | `—` | [0x015C.md](client-to-server/0x015C.md) |
| `0x015D` | `CANCEL_JOIN_GUILD` | 布局已闭合 | `4` | [0x015D.md](client-to-server/0x015D.md) |
| `0x015E` | `APPROVE_JOIN_GUILD` | 布局已闭合 | `4` | [0x015E.md](client-to-server/0x015E.md) |
| `0x015F` | `DENY_JOIN_GUILD` | 布局部分可得 | `≥ 4` | [0x015F.md](client-to-server/0x015F.md) |
| `0x0160` | `GUILD_JOIN_LIST` | 布局已闭合 | `0` | [0x0160.md](client-to-server/0x0160.md) |
| `0x0169` | `REFUND_CERA_ITEM` | 结构未知 | `—` | [0x0169.md](client-to-server/0x0169.md) |
| `0x016A` | `PICKUP_CERA_ITEM` | 结构未知 | `—` | [0x016A.md](client-to-server/0x016A.md) |
| `0x016B` | `CASH_INVENTORY` | 结构未知 | `—` | [0x016B.md](client-to-server/0x016B.md) |
| `0x016C` | `BREAK_AWAY_QUEST_CHECK` | 布局已闭合 | `1` | [0x016C.md](client-to-server/0x016C.md) |
| `0x016D` | `JOIN_GUILD_INFO` | 布局已闭合 | `0` | [0x016D.md](client-to-server/0x016D.md) |
| `0x016E` | `SCAN_BOT_BY_DRV` | 布局已闭合 | `5` | [0x016E.md](client-to-server/0x016E.md) |
| `0x016F` | `ASK_REMATCH` | 布局已闭合 | `1` | [0x016F.md](client-to-server/0x016F.md) |
| `0x0170` | `SAVE_GAME_OPTION_QUICKCHATTING` | 布局部分可得 | `≥ 1` | [0x0170.md](client-to-server/0x0170.md) |
| `0x0172` | `ENCHANT_3RD_CHRONICLE_ITEM` | 布局部分可得 | `≥ 14` | [0x0172.md](client-to-server/0x0172.md) |
| `0x0173` | `GOLD_TAKE_INCREASING_AMOUNT` | 布局已闭合 | `5` | [0x0173.md](client-to-server/0x0173.md) |
| `0x0175` | `CHECK_SECURITY_PROTECTION` | 布局已闭合 | `0` | [0x0175.md](client-to-server/0x0175.md) |
| `0x0176` | `INTEGRATE_MATCH_PVP_SCORE` | 布局部分可得 | `≥ 2` | [0x0176.md](client-to-server/0x0176.md) |
| `0x0177` | `FAIR_PVP_SCORE` | 布局部分可得 | `≥ 8` | [0x0177.md](client-to-server/0x0177.md) |
| `0x0178` | `PVP_MISSION_HP_PERCENT` | 布局已闭合（含计数重复组） | `1 + n×2` | [0x0178.md](client-to-server/0x0178.md) |
| `0x0179` | `PVP_MISSION_WIN_POSE` | 布局已闭合 | `3` | [0x0179.md](client-to-server/0x0179.md) |
| `0x017A` | `WARROOM_WP_PER_MONSTER` | 布局部分可得 | `≥ 12` | [0x017A.md](client-to-server/0x017A.md) |
| `0x0183` | `REQUEST_DUNGEON_PARTY_LIST` | 结构未知 | `—` | [0x0183.md](client-to-server/0x0183.md) |
| `0x018B` | `REQUEST_VIDEO_OBSERVER_LOG` | 结构未知 | `—` | [0x018B.md](client-to-server/0x018B.md) |
| `0x018C` | `ABNORMAL_FUNCTION_CALL` | 布局部分可得 | `≥ 8` | [0x018C.md](client-to-server/0x018C.md) |
| `0x018D` | `EQUIPSLOT_SWITCH` | 结构未知 | `—` | [0x018D.md](client-to-server/0x018D.md) |
| `0x018E` | `EXPAND_EQUIPSLOT_FLAG_UPDATE` | 结构未知 | `—` | [0x018E.md](client-to-server/0x018E.md) |
| `0x018F` | `BUY_CHARAC_STATUS_USING_QP` | 结构未知 | `—` | [0x018F.md](client-to-server/0x018F.md) |
| `0x0190` | `CLEAR_USED_QP` | 结构未知 | `—` | [0x0190.md](client-to-server/0x0190.md) |
| `0x0191` | `UNSEAL_RANDOM_OPTION` | 布局已闭合 | `4` | [0x0191.md](client-to-server/0x0191.md) |
| `0x0194` | `CHANGE_HOST_WARROOM` | 布局已闭合 | `5` | [0x0194.md](client-to-server/0x0194.md) |
| `0x0195` | `VERIFY_PRIVATE_STORE_ITEM` | 布局部分可得 | `≥ 6` | [0x0195.md](client-to-server/0x0195.md) |
| `0x0197` | `REGENERATION_RANDOM_OPTION` | 布局部分可得 | `≥ 8` | [0x0197.md](client-to-server/0x0197.md) |
| `0x0198` | `UPGRADE_CARGO` | 布局已闭合 | `2` | [0x0198.md](client-to-server/0x0198.md) |
| `0x019B` | `PVP_MISSION_COMBO` | 布局已闭合 | `4` | [0x019B.md](client-to-server/0x019B.md) |
| `0x019C` | `TITLE_BOOK_PUT` | 结构未知 | `—` | [0x019C.md](client-to-server/0x019C.md) |
| `0x019D` | `TITLE_BOOK_GET` | 结构未知 | `—` | [0x019D.md](client-to-server/0x019D.md) |
| `0x019E` | `MONSTERCARD_BIND` | 布局已闭合（含计数重复组） | `2 + n×2` | [0x019E.md](client-to-server/0x019E.md) |
| `0x019F` | `CHARAC_SLOT_EXTEND_EFFECT` | 布局已闭合 | `0` | [0x019F.md](client-to-server/0x019F.md) |
| `0x01A0` | `EXPERT_EXTRACTION` | 布局部分可得 | `≥ 6` | [0x01A0.md](client-to-server/0x01A0.md) |
| `0x01A1` | `ACHIEVEMENT_TRIGGER` | 布局部分可得 | `≥ 10` | [0x01A1.md](client-to-server/0x01A1.md) |
| `0x01A2` | `REQUEST_EVENT_SERVER_LEVEL_UP` | 布局已闭合 | `3` | [0x01A2.md](client-to-server/0x01A2.md) |
| `0x01A3` | `CREATE_GROUP` | 布局部分可得 | `—` | [0x01A3.md](client-to-server/0x01A3.md) |
| `0x01A4` | `INVITE_MEMBER_FOR_GROUP` | 布局部分可得 | `—` | [0x01A4.md](client-to-server/0x01A4.md) |
| `0x01A5` | `LEAVE_FROM_GROUP` | 布局已闭合 | `4` | [0x01A5.md](client-to-server/0x01A5.md) |
| `0x01A6` | `CHANGE_GROUP_CHAT_USER_STATE` | 布局已闭合 | `1` | [0x01A6.md](client-to-server/0x01A6.md) |
| `0x01A7` | `EVENT_ALL_USER_GIFT` | 布局部分可得 | `≥ 1 + n×1` | [0x01A7.md](client-to-server/0x01A7.md) |
| `0x01A8` | `OTHER_USER_TITLE_BOOK_LIST` | 布局已闭合 | `2` | [0x01A8.md](client-to-server/0x01A8.md) |
| `0x01A9` | `ITEM_HYPERLINK_MESSAGE` | 结构未知 | `—` | [0x01A9.md](client-to-server/0x01A9.md) |
| `0x01AA` | `USER_HISTORY_LOG` | 布局部分可得 | `—` | [0x01AA.md](client-to-server/0x01AA.md) |
| `0x01AB` | `REFUND_SKILL` | 布局部分可得 | `≥ 7` | [0x01AB.md](client-to-server/0x01AB.md) |
| `0x01AD` | `ATTENDANCE_CHECK` | 布局已闭合 | `2` | [0x01AD.md](client-to-server/0x01AD.md) |
| `0x01AE` | `REQUEST_GOBLIN_PAD_IMG` | 结构未知 | `—` | [0x01AE.md](client-to-server/0x01AE.md) |
| `0x01AF` | `UPGRADE_INVENTORY` | 布局已闭合 | `2` | [0x01AF.md](client-to-server/0x01AF.md) |
| `0x01B0` | `SELECT_ITEM_GROWTH_POWER` | 布局部分可得 | `≥ 1 + n×1` | [0x01B0.md](client-to-server/0x01B0.md) |
| `0x01B1` | `REQUEST_SERIA_BUFF` | 布局已闭合 | `0` | [0x01B1.md](client-to-server/0x01B1.md) |
| `0x01B2` | `USE_CHESTNUT_STONE_ITEM` | 布局部分可得 | `≥ 2` | [0x01B2.md](client-to-server/0x01B2.md) |
| `0x01B3` | `PARTY_TELEPORT` | 布局已闭合 | `8` | [0x01B3.md](client-to-server/0x01B3.md) |
| `0x01B4` | `PARTY_TELEPORT_CONFIRM` | 布局已闭合 | `1` | [0x01B4.md](client-to-server/0x01B4.md) |
| `0x01B5` | `ABNORMAL_USE_STACKABLE` | 布局已闭合 | `8` | [0x01B5.md](client-to-server/0x01B5.md) |
| `0x01B6` | `CHANGE_RANDOM_OPTION` | 布局已闭合 | `5` | [0x01B6.md](client-to-server/0x01B6.md) |
| `0x01B7` | `UPGRADE_ITEM_SEPARATE` | 布局部分可得 | `—` | [0x01B7.md](client-to-server/0x01B7.md) |
| `0x01B9` | `MERCENARY_RETURN` | 布局已闭合 | `5` | [0x01B9.md](client-to-server/0x01B9.md) |
| `0x01BA` | `MERCENARY_INFO` | **手写记录** | 见文档 | [0x01BA.md](client-to-server/0x01BA.md) **(手写)** |
| `0x01BB` | `MERCENARY_COMPETITION` | 布局已闭合 | `6` | [0x01BB.md](client-to-server/0x01BB.md) |
| `0x01BC` | `REGISTER_QUICK_PARTY` | 布局部分可得 | `≥ 4 + n×5` | [0x01BC.md](client-to-server/0x01BC.md) |
| `0x01BD` | `CANCEL_QUICK_PARTY` | 布局已闭合 | `0` | [0x01BD.md](client-to-server/0x01BD.md) |
| `0x01BE` | `DIRECT_ENTRANCE_DUNGEON_QUICK_PARTY` | 布局已闭合 | `0` | [0x01BE.md](client-to-server/0x01BE.md) |
| `0x01BF` | `REQUEST_ASSAULT_PRICE` | 结构未知 | `—` | [0x01BF.md](client-to-server/0x01BF.md) |
| `0x01C0` | `SAVE_CHARACTER_OPTION` | 结构未知 | `—` | [0x01C0.md](client-to-server/0x01C0.md) |
| `0x01C1` | `EXCHANGE_RANDOM_ITEM_REWARD` | 布局已闭合 | `9` | [0x01C1.md](client-to-server/0x01C1.md) |
| `0x01C2` | `AVATAR_DISJOINT_RANDOM_REWARD` | 布局已闭合 | `6` | [0x01C2.md](client-to-server/0x01C2.md) |
| `0x01C5` | `FLOAT_RDATA_MODULATION` | 布局已闭合 | `8` | [0x01C5.md](client-to-server/0x01C5.md) |
| `0x01C6` | `REQ_URGENT_QUEST` | 结构未知 | `—` | [0x01C6.md](client-to-server/0x01C6.md) |
| `0x01C8` | `RESET_RANDOM_OPTION` | 布局已闭合 | `6` | [0x01C8.md](client-to-server/0x01C8.md) |
| `0x01C9` | `CLEAR_QUEST` | 结构未知 | `—` | [0x01C9.md](client-to-server/0x01C9.md) |
| `0x01CA` | `TOURNAMENT_REWARD_SELECT_STATE` | 布局已闭合 | `0` | [0x01CA.md](client-to-server/0x01CA.md) |
| `0x01CB` | `TOURNAMENT_REWARD_SELECT` | 布局已闭合 | `2` | [0x01CB.md](client-to-server/0x01CB.md) |
| `0x01CC` | `AVATAR_OPTION_CHANGE` | 布局已闭合 | `13` | [0x01CC.md](client-to-server/0x01CC.md) |
| `0x01CD` | `CHARACTER_STATUS` | 结构未知 | `—` | [0x01CD.md](client-to-server/0x01CD.md) |
| `0x01D1` | `LIMIT_NPC_BUY_ITEM` | 布局已闭合 | `8` | [0x01D1.md](client-to-server/0x01D1.md) |
| `0x01D2` | `QUERY_CHARAC_INFO_ADD_DATA` | 结构未知 | `—` | [0x01D2.md](client-to-server/0x01D2.md) |
| `0x01D4` | `SERIA_ROOM_DECO_INFO` | 布局已闭合 | `2` | [0x01D4.md](client-to-server/0x01D4.md) |
| `0x01D5` | `OBJECT_BRING_UP_USE_ITEM` | 布局部分可得 | `≥ 2` | [0x01D5.md](client-to-server/0x01D5.md) |
| `0x01D6` | `PRECHECK_SOLO_TELEPOART` | 布局部分可得 | `—` | [0x01D6.md](client-to-server/0x01D6.md) |
| `0x01D7` | `SOLO_TELEPOART` | 布局已闭合 | `15` | [0x01D7.md](client-to-server/0x01D7.md) |
| `0x01D9` | `SAVE_GAME_OPTION_CHATTING_EMOTICON` | 布局部分可得 | `—` | [0x01D9.md](client-to-server/0x01D9.md) |
| `0x01DA` | `REPORT_CLIENT_HACK` | 布局部分可得 | `≥ 6` | [0x01DA.md](client-to-server/0x01DA.md) |
| `0x01DC` | `IMAGE_COMMUNICATION_EQUIPMENT_USE` | 布局已闭合 | `0` | [0x01DC.md](client-to-server/0x01DC.md) |
| `0x01DD` | `COMPATIBILITY_INDEX` | **手写记录** | 见文档 | [0x01DD.md](client-to-server/0x01DD.md) **(手写)** |
| `0x01DE` | `INFORM_NOTICE` | 布局已闭合 | `4` | [0x01DE.md](client-to-server/0x01DE.md) |
| `0x01DF` | `P2P_STATISTICS` | 布局部分可得 | `≥ 2` | [0x01DF.md](client-to-server/0x01DF.md) |
| `0x01E0` | `VERIFY_CREATURE_QUEST` | 结构未知 | `—` | [0x01E0.md](client-to-server/0x01E0.md) |
| `0x01E1` | `REPORT_PVP_LAG` | 布局部分可得 | `≥ 6` | [0x01E1.md](client-to-server/0x01E1.md) |
| `0x01E2` | `VERIFY_PVP_LAG_USER` | 结构未知 | `—` | [0x01E2.md](client-to-server/0x01E2.md) |
| `0x01E3` | `COLLECT_ITEMS` | 布局部分可得 | `≥ 4` | [0x01E3.md](client-to-server/0x01E3.md) |
| `0x01E4` | `TUTORIAL_LEVEL_UP` | 布局已闭合 | `0` | [0x01E4.md](client-to-server/0x01E4.md) |
| `0x01E5` | `REQUEST_CHARAC_SKILL_INFO` | 布局已闭合 | `2` | [0x01E5.md](client-to-server/0x01E5.md) |
| `0x01E6` | `CRANE_START_USE` | 布局已闭合 | `2` | [0x01E6.md](client-to-server/0x01E6.md) |
| `0x01E7` | `CRANE_PICKUP` | 布局已闭合 | `6` | [0x01E7.md](client-to-server/0x01E7.md) |
| `0x01E8` | `SELECT_STRIKER` | 布局已闭合 | `3` | [0x01E8.md](client-to-server/0x01E8.md) |
| `0x01E9` | `REQUEST_INGAME_ADVERTISEMENT` | 结构未知 | `—` | [0x01E9.md](client-to-server/0x01E9.md) |
| `0x01EA` | `LOG_INGAME_ADVERTISEMENT` | 布局已闭合 | `8` | [0x01EA.md](client-to-server/0x01EA.md) |
| `0x01EB` | `AUTO_SKILL` | 结构未知 | `—` | [0x01EB.md](client-to-server/0x01EB.md) |
| `0x01EC` | `SKILL_INIT` | 结构未知 | `—` | [0x01EC.md](client-to-server/0x01EC.md) |
| `0x01ED` | `PC_ROOM_PLAY_TIME_REWARD` | 布局已闭合 | `0` | [0x01ED.md](client-to-server/0x01ED.md) |
| `0x01F0` | `BLUE_MARBLE` | 布局已闭合 | `2` | [0x01F0.md](client-to-server/0x01F0.md) |
| `0x01F1` | `GET_GROWTHCAPSULE` | 布局已闭合 | `0` | [0x01F1.md](client-to-server/0x01F1.md) |
| `0x01F3` | `USE_DYE` | 布局已闭合 | `4` | [0x01F3.md](client-to-server/0x01F3.md) |
| `0x01F7` | `USE_RIGHT_OF_CHANGE_GROW_TYPE` | 布局已闭合 | `3` | [0x01F7.md](client-to-server/0x01F7.md) |
| `0x01F8` | `INFORM_NOTICE_2ND` | 布局已闭合 | `4` | [0x01F8.md](client-to-server/0x01F8.md) |
| `0x01FB` | `SAVE_QUEST_NOTIFY` | 布局已闭合（含计数重复组） | `1 + n×4` | [0x01FB.md](client-to-server/0x01FB.md) |
| `0x01FC` | `BLUE_MARBLE_CONFIRM_INFO` | 布局部分可得 | `≥ 3` | [0x01FC.md](client-to-server/0x01FC.md) |
| `0x01FD` | `COMBO_SKILL_INFO` | 布局部分可得 | `≥ 2 + n×5` | [0x01FD.md](client-to-server/0x01FD.md) |
| `0x01FE` | `USE_RENAME_CARD` | 布局已闭合 | `2` | [0x01FE.md](client-to-server/0x01FE.md) |
| `0x01FF` | `COMBO_SKILL_EXTENSION_QUICK_SLOT_RESET` | 布局已闭合 | `1` | [0x01FF.md](client-to-server/0x01FF.md) |
| `0x0200` | `EQUIPED_CREATURE_CHANGE_INFINITY_CREATURE` | 布局已闭合 | `0` | [0x0200.md](client-to-server/0x0200.md) |
| `0x0201` | `SERIAROOM_ANIMATION_DECO_EVENT` | 布局部分可得 | `≥ 4` | [0x0201.md](client-to-server/0x0201.md) |
| `0x0202` | `BINGO_REWARD` | 布局已闭合 | `0` | [0x0202.md](client-to-server/0x0202.md) |
| `0x0203` | `BINGO_QUIZ` | 布局已闭合 | `4` | [0x0203.md](client-to-server/0x0203.md) |
| `0x0204` | `USE_STACKABLE_ACTION` | 布局部分可得 | `≥ 19` | [0x0204.md](client-to-server/0x0204.md) |
| `0x0205` | `DUAL_RAID_DUNGEON_JOIN` | 结构未知 | `—` | [0x0205.md](client-to-server/0x0205.md) |
| `0x0206` | `DUAL_RAID_DUNGEON` | 布局已闭合 | `1` | [0x0206.md](client-to-server/0x0206.md) |
| `0x0207` | `OPEN_CERAPACKAGE` | 布局部分可得 | `≥ 3 + n×5` | [0x0207.md](client-to-server/0x0207.md) |
| `0x0208` | `GET_ITEMBOX` | 布局部分可得 | `≥ 4` | [0x0208.md](client-to-server/0x0208.md) |
| `0x0209` | `REQUEST_INTEGRATED_MATCHING` | 布局部分可得 | `≥ 6` | [0x0209.md](client-to-server/0x0209.md) |
| `0x020A` | `CANCEL_INTEGRATED_MATCHING` | 布局已闭合 | `3` | [0x020A.md](client-to-server/0x020A.md) |
| `0x020B` | `MATCHING_DUNGEON_EXIT` | 布局已闭合 | `3` | [0x020B.md](client-to-server/0x020B.md) |
| `0x020C` | `CHANNEL_MOVE_SUCCESS` | 布局已闭合 | `5` | [0x020C.md](client-to-server/0x020C.md) |
| `0x020F` | `REQUEST_COLOSSEUM_PURCHASE_TICKET` | 布局已闭合 | `2` | [0x020F.md](client-to-server/0x020F.md) |
| `0x0210` | `REQUEST_UPDATE_COLOSSEUM_REWARD` | 结构未知 | `—` | [0x0210.md](client-to-server/0x0210.md) |
| `0x0211` | `SUMMON_MONSTER` | 布局已闭合 | `19` | [0x0211.md](client-to-server/0x0211.md) |
| `0x0212` | `RACING_DUNGEON_JOIN` | 结构未知 | `—` | [0x0212.md](client-to-server/0x0212.md) |
| `0x0213` | `RACING_DUNGEON_DISJOIN` | 结构未知 | `—` | [0x0213.md](client-to-server/0x0213.md) |
| `0x0214` | `RACING_DUNGEON_GOAL_IN_PLAYER` | 结构未知 | `—` | [0x0214.md](client-to-server/0x0214.md) |
| `0x0215` | `RACING_DUNGEON_RETURN_TO_VILLAGE` | 布局已闭合 | `0` | [0x0215.md](client-to-server/0x0215.md) |
| `0x0216` | `DUAL_RAID_DUNGEON_READY` | 结构未知 | `—` | [0x0216.md](client-to-server/0x0216.md) |
| `0x0217` | `DUAL_RAID_DUNGEON_REWARD` | 布局已闭合 | `1` | [0x0217.md](client-to-server/0x0217.md) |
| `0x0218` | `UPDATE_CONTRACT_OF_CUBE_INFO` | 布局已闭合 | `2` | [0x0218.md](client-to-server/0x0218.md) |
| `0x021E` | `SELECT_ZOMBIE` | 布局已闭合 | `2` | [0x021E.md](client-to-server/0x021E.md) |
| `0x021F` | `TO_BE_ZOMBIE` | 布局部分可得 | `≥ 6` | [0x021F.md](client-to-server/0x021F.md) |
| `0x0220` | `ZOMBIE_MODE_RESULT_SCORE` | 布局部分可得 | `≥ 4 + n×17` | [0x0220.md](client-to-server/0x0220.md) |
| `0x0222` | `SCORING_COMPUTATION` | 布局已闭合（含计数重复组） | `2 + n×2` | [0x0222.md](client-to-server/0x0222.md) |
| `0x0223` | `REQUEST_FOR_PARTICIPATION` | 布局已闭合 | `1` | [0x0223.md](client-to-server/0x0223.md) |
| `0x0224` | `MONSTER_MOVE_SYSTEM` | 布局已闭合 | `17` | [0x0224.md](client-to-server/0x0224.md) |
| `0x0227` | `PVP_TOURNAMENT_MATCH_LIST` | 布局已闭合 | `4` | [0x0227.md](client-to-server/0x0227.md) |
| `0x0228` | `PVP_TOURNAMENT_REQUEST` | 布局部分可得 | `≥ 1` | [0x0228.md](client-to-server/0x0228.md) |
| `0x0229` | `LOADING_CART` | 布局已闭合 | `4` | [0x0229.md](client-to-server/0x0229.md) |
| `0x022A` | `GOLDEN_COMMERCE_START` | 布局已闭合 | `0` | [0x022A.md](client-to-server/0x022A.md) |
| `0x022B` | `GOLDEN_COMMERCE_REWARD` | 布局已闭合 | `1` | [0x022B.md](client-to-server/0x022B.md) |
| `0x022C` | `CREATURE_FILLPOINT` | 布局已闭合 | `4` | [0x022C.md](client-to-server/0x022C.md) |
| `0x022D` | `CHURN_GRADE_ACTION_CLEAR` | 布局已闭合 | `1` | [0x022D.md](client-to-server/0x022D.md) |
| `0x022E` | `CARGO_TRANSPORT_ITEM` | 布局已闭合 | `9` | [0x022E.md](client-to-server/0x022E.md) |
| `0x022F` | `DUNGEON_MISSION_START` | 结构未知 | `—` | [0x022F.md](client-to-server/0x022F.md) |
| `0x0230` | `DUNGEON_MISSION_UPDATE` | 布局部分可得 | `≥ 4` | [0x0230.md](client-to-server/0x0230.md) |
| `0x0231` | `DUNGEON_MISSION_CHECK_SUCCESS` | 结构未知 | `—` | [0x0231.md](client-to-server/0x0231.md) |
| `0x0232` | `REQUEST_INSTANT_REINFORCE` | 布局已闭合 | `1` | [0x0232.md](client-to-server/0x0232.md) |
| `0x0233` | `FATIGUE_ACCELERATION_STATE_CHANGE` | 布局已闭合 | `1` | [0x0233.md](client-to-server/0x0233.md) |
| `0x0234` | `MINORITY_DETAIL_RESULT` | 布局已闭合 | `0` | [0x0234.md](client-to-server/0x0234.md) |
| `0x0235` | `MINORITY_REQUEST_QUESTION` | 布局已闭合 | `0` | [0x0235.md](client-to-server/0x0235.md) |
| `0x0236` | `MINORITY_QUESTION_ANSWER` | 布局已闭合 | `4` | [0x0236.md](client-to-server/0x0236.md) |
| `0x0237` | `MINORITY_REQUIRE_REWARD` | 布局已闭合 | `0` | [0x0237.md](client-to-server/0x0237.md) |
| `0x0238` | `DECIDE_LEVELUP` | 布局已闭合 | `4` | [0x0238.md](client-to-server/0x0238.md) |
| `0x0239` | `SET_CLONE_TITLE` | 布局已闭合 | `4` | [0x0239.md](client-to-server/0x0239.md) |
| `0x023B` | `INTEGRATE_MATCHING_DIRECT_COMPLETE` | 布局已闭合 | `4` | [0x023B.md](client-to-server/0x023B.md) |
| `0x023C` | `INTEGRATED_MATCHING_MODE_CHANGE` | 布局已闭合 | `3` | [0x023C.md](client-to-server/0x023C.md) |
| `0x023D` | `INTEGRATE_MATCHING_USER_COUNT` | 布局已闭合 | `0` | [0x023D.md](client-to-server/0x023D.md) |
| `0x0241` | `CREATE_UPGRADE_ROOM` | 布局已闭合 | `4` | [0x0241.md](client-to-server/0x0241.md) |
| `0x0242` | `DESTROY_UPGRADE_ROOM` | 结构未知 | `—` | [0x0242.md](client-to-server/0x0242.md) |
| `0x0243` | `JOIN_SPECTATOR_UPGRADE_ROOM` | 布局已闭合 | `4` | [0x0243.md](client-to-server/0x0243.md) |
| `0x0244` | `LEAVE_SPECTATOR_UPGRADE_ROOM` | 结构未知 | `—` | [0x0244.md](client-to-server/0x0244.md) |
| `0x0245` | `UPGRADE_ROOM_GIVE_ITEM_INFO` | 布局部分可得 | `≥ 1 + n×8` | [0x0245.md](client-to-server/0x0245.md) |
| `0x0248` | `REQUEST_COMBO_SCORE` | 布局已闭合 | `2` | [0x0248.md](client-to-server/0x0248.md) |
| `0x0249` | `COMBO_SCORE_INFO` | 布局已闭合 | `6` | [0x0249.md](client-to-server/0x0249.md) |
| `0x024D` | `UPGRADE_ROOM_PUT_UP_ITEM` | 布局已闭合 | `6` | [0x024D.md](client-to-server/0x024D.md) |
| `0x024E` | `UPGRADE_ROOM_UPGRADE_START` | 结构未知 | `—` | [0x024E.md](client-to-server/0x024E.md) |
| `0x024F` | `UPGRADE_ROOM_UPGRADE_CANCEL` | 布局已闭合 | `0` | [0x024F.md](client-to-server/0x024F.md) |
| `0x0250` | `UPGRADE_ROOM_MASTER_MESSAGE` | 布局部分可得 | `—` | [0x0250.md](client-to-server/0x0250.md) |
| `0x0252` | `SECURITY_STATUS` | 布局部分可得 | `≥ 8` | [0x0252.md](client-to-server/0x0252.md) |
| `0x0253` | `EVENT_NPC_DROP_ITEM_` | 布局已闭合 | `0` | [0x0253.md](client-to-server/0x0253.md) |
| `0x0254` | `REQUEST_PICK_EVENT_INFO` | 结构未知 | `—` | [0x0254.md](client-to-server/0x0254.md) |
| `0x0255` | `LETS_PICK_PRESENT` | 布局已闭合 | `2` | [0x0255.md](client-to-server/0x0255.md) |
| `0x0256` | `REQUEST_ADD_PICK_CHANCE` | 布局已闭合 | `0` | [0x0256.md](client-to-server/0x0256.md) |
| `0x0257` | `CREATE_EXPERT_JOB_STORE` | 布局部分可得 | `≥ 11` | [0x0257.md](client-to-server/0x0257.md) |
| `0x0258` | `ENTER_EXPERT_JOB_STORE` | 布局已闭合 | `2` | [0x0258.md](client-to-server/0x0258.md) |
| `0x0259` | `CLOSE_EXPERT_JOB_STORE` | 布局已闭合 | `0` | [0x0259.md](client-to-server/0x0259.md) |
| `0x025A` | `USE_ENCHANT_STORE` | 布局已闭合 | `13` | [0x025A.md](client-to-server/0x025A.md) |
| `0x025B` | `GET_EXPAND_EXP_GAGE_REWARD` | 布局已闭合 | `0` | [0x025B.md](client-to-server/0x025B.md) |
| `0x025C` | `UPGRADE_CARD` | 布局部分可得 | `≥ 4 + n×3` | [0x025C.md](client-to-server/0x025C.md) |
| `0x025E` | `USE_OBJECT_SCALE_EFFECT_IN_VILLAGE` | 布局已闭合 | `4` | [0x025E.md](client-to-server/0x025E.md) |
| `0x025F` | `CANCLE_OBJECT_SCALE_EFFECT_IN_VILLAGE` | 布局已闭合 | `0` | [0x025F.md](client-to-server/0x025F.md) |
| `0x0260` | `REQUEST_CHILDREN_DAY_EVENT_REWARD` | 结构未知 | `—` | [0x0260.md](client-to-server/0x0260.md) |
| `0x0261` | `REQUEST_EXP_KEEPING_EVENT_REWARD` | 布局已闭合 | `0` | [0x0261.md](client-to-server/0x0261.md) |
| `0x0262` | `REQUEST_SP_TEAM` | 布局部分可得 | `≥ 5` | [0x0262.md](client-to-server/0x0262.md) |
| `0x0264` | `REPAIR_EXPERT_JOB_STORE` | 布局已闭合 | `0` | [0x0264.md](client-to-server/0x0264.md) |
| `0x0265` | `APPENDAGE_DESTORY_OBJECT` | 布局已闭合 | `4` | [0x0265.md](client-to-server/0x0265.md) |
| `0x0266` | `STRONGEST_PVP_MEMBER_CHANNELINFO` | 布局已闭合 | `0` | [0x0266.md](client-to-server/0x0266.md) |
| `0x0267` | `UPDATE_EVENT_DUNGEON_TOP_RANK` | 布局已闭合 | `8` | [0x0267.md](client-to-server/0x0267.md) |
| `0x0268` | `TERA_PIECE_CLEAR_TIME_RANK_REQUEST` | 结构未知 | `—` | [0x0268.md](client-to-server/0x0268.md) |
| `0x026B` | `TIMER_MODIFY_INFO` | 布局已闭合 | `4` | [0x026B.md](client-to-server/0x026B.md) |
| `0x026C` | `SUMMON_TIME_LINE` | 布局已闭合 | `4` | [0x026C.md](client-to-server/0x026C.md) |
| `0x026D` | `SEA_CHASE_MINI_GAME_RESULT` | 布局部分可得 | `≥ 4` | [0x026D.md](client-to-server/0x026D.md) |
| `0x026E` | `REQUEST_UPDATE_SPEC_EVENT` | 布局部分可得 | `≥ 1 + n×2` | [0x026E.md](client-to-server/0x026E.md) |
| `0x026F` | `REQUEST_SPEC_EVENT_REWARD` | 布局已闭合 | `1` | [0x026F.md](client-to-server/0x026F.md) |
| `0x0270` | `GAIN_EAT_OBJECT` | 布局部分可得 | `≥ 20` | [0x0270.md](client-to-server/0x0270.md) |
| `0x0271` | `PVP_DAILY_WIN_COUNT` | 布局已闭合 | `0` | [0x0271.md](client-to-server/0x0271.md) |
| `0x0272` | `DELETE_PVP_FIGHT_DOLL_TICKET` | 布局已闭合 | `2` | [0x0272.md](client-to-server/0x0272.md) |
| `0x0273` | `SELECT_FIGHT_DOLL` | 布局已闭合 | `4` | [0x0273.md](client-to-server/0x0273.md) |
| `0x0274` | `USE_PVP_DOLL_SKILL` | 布局已闭合 | `4` | [0x0274.md](client-to-server/0x0274.md) |
| `0x0275` | `FIGHT_DOLL_RANK_IDX` | 结构未知 | `—` | [0x0275.md](client-to-server/0x0275.md) |
| `0x0276` | `ATTENDANCE_MARBLE_EVENT_INFO` | 结构未知 | `—` | [0x0276.md](client-to-server/0x0276.md) |
| `0x0277` | `ATTENDANCE_MARBLE_EVENT_DICE` | 布局已闭合 | `1` | [0x0277.md](client-to-server/0x0277.md) |
| `0x0278` | `USER_STATE_MOTION` | 布局部分可得 | `≥ 13` | [0x0278.md](client-to-server/0x0278.md) |
| `0x0279` | `GET_PCROOM_TIME_POINT_ITEM` | 布局已闭合 | `2` | [0x0279.md](client-to-server/0x0279.md) |
| `0x027A` | `GET_AVATAR_SPEC_EVENT` | 结构未知 | `—` | [0x027A.md](client-to-server/0x027A.md) |
| `0x027B` | `REQUEST_EVENT_DUNGEON_TOP_RANK_INFO` | 结构未知 | `—` | [0x027B.md](client-to-server/0x027B.md) |
| `0x027C` | `DUNGEON_CLEAR` | 结构未知 | `—` | [0x027C.md](client-to-server/0x027C.md) |
| `0x027D` | `DURING_LOGIN_TIME_EVENT_ACTION` | 布局已闭合 | `5` | [0x027D.md](client-to-server/0x027D.md) |
| `0x0280` | `RETURN_USER_REWARD_REQUEST` | 布局已闭合 | `0` | [0x0280.md](client-to-server/0x0280.md) |
| `0x0281` | `REQUEST_FATIGUE_USE_REWARD` | 结构未知 | `—` | [0x0281.md](client-to-server/0x0281.md) |
| `0x0282` | `ATTENDANCE_MARBLE_DOUBLE_EXIT` | 布局已闭合 | `0` | [0x0282.md](client-to-server/0x0282.md) |
| `0x0283` | `SET_EXPECT_PARTY_LIST` | 布局部分可得 | `≥ 1` | [0x0283.md](client-to-server/0x0283.md) |
| `0x0286` | `CHARAC_VIEW_HIDDEN_CHARAC_INFO` | **手写记录** | 见文档 | [0x0286.md](client-to-server/0x0286.md) **(手写)** |
| `0x0287` | `REBIRTH_HARDCORE_CHARAC` | 结构未知 | `—` | [0x0287.md](client-to-server/0x0287.md) |
| `0x0288` | `HARDCORE_CHARAC_LIST` | 结构未知 | `—` | [0x0288.md](client-to-server/0x0288.md) |
| `0x0289` | `HARDCORE_MURDERER` | 布局部分可得 | `—` | [0x0289.md](client-to-server/0x0289.md) |
| `0x028B` | `HARDCORE_RANK` | 布局部分可得 | `≥ 2` | [0x028B.md](client-to-server/0x028B.md) |
| `0x028C` | `REQUEST_EVENT_GIFT` | 布局已闭合 | `2` | [0x028C.md](client-to-server/0x028C.md) |
| `0x028E` | `DIMENSION_EXPERIENCE_USER_REWARD` | 布局已闭合 | `0` | [0x028E.md](client-to-server/0x028E.md) |
| `0x028F` | `DIMENSION_EXPERIENCE_MENTOR` | 布局已闭合 | `1` | [0x028F.md](client-to-server/0x028F.md) |
| `0x0290` | `DIMENSION_EXPERIENCE_CLEAR_REWARD` | 布局已闭合 | `1` | [0x0290.md](client-to-server/0x0290.md) |
| `0x0292` | `CHANGE_DECK_INFO` | 布局部分可得 | `≥ 0 + n×4` | [0x0292.md](client-to-server/0x0292.md) |
| `0x0293` | `RAID_ENTRY_COST_INFO` | 布局已闭合 | `1` | [0x0293.md](client-to-server/0x0293.md) |
| `0x0294` | `RAID_MOVIE_SKIP` | 布局已闭合 | `1` | [0x0294.md](client-to-server/0x0294.md) |
| `0x0295` | `SELECT_RAID_REWARD_CARD` | 布局已闭合 | `2` | [0x0295.md](client-to-server/0x0295.md) |
| `0x0296` | `RAID_DO_BEHAVIOR` | 布局部分可得 | `≥ 6` | [0x0296.md](client-to-server/0x0296.md) |
| `0x0297` | `RAID_SET_SYMBOL` | 布局已闭合 | `9` | [0x0297.md](client-to-server/0x0297.md) |
| `0x0298` | `RAID_MESSAGE` | 布局部分可得 | `—` | [0x0298.md](client-to-server/0x0298.md) |
| `0x0299` | `CREATE_RAID` | 布局部分可得 | `—` | [0x0299.md](client-to-server/0x0299.md) |
| `0x029A` | `LEAVE_RAID` | 布局已闭合 | `2` | [0x029A.md](client-to-server/0x029A.md) |
| `0x029B` | `START_RAID` | 布局已闭合 | `0` | [0x029B.md](client-to-server/0x029B.md) |
| `0x029C` | `SET_RAID_WAITING` | 布局已闭合 | `2` | [0x029C.md](client-to-server/0x029C.md) |
| `0x029D` | `REJOIN_RAID` | 布局已闭合 | `4` | [0x029D.md](client-to-server/0x029D.md) |
| `0x029E` | `RAID_MANAGER_WORK` | 布局已闭合 | `12` | [0x029E.md](client-to-server/0x029E.md) |
| `0x029F` | `MODIFY_RAID_INFO` | 布局部分可得 | `—` | [0x029F.md](client-to-server/0x029F.md) |
| `0x02A0` | `DISJOINT_COLOSSEUM_ITEM` | 布局部分可得 | `≥ 3` | [0x02A0.md](client-to-server/0x02A0.md) |
| `0x02A3` | `RECENT_FRIEND_LIST` | 布局已闭合 | `0` | [0x02A3.md](client-to-server/0x02A3.md) |
| `0x02A4` | `PVP_SEASON_REWARD` | 布局已闭合 | `2` | [0x02A4.md](client-to-server/0x02A4.md) |
| `0x02A5` | `GUNNER_2_AWAKENING_USER_REWARD` | 结构未知 | `—` | [0x02A5.md](client-to-server/0x02A5.md) |
| `0x02A6` | `GUNNER_2_AWAKENING_CLEAR_REWARD` | 布局已闭合 | `1` | [0x02A6.md](client-to-server/0x02A6.md) |
| `0x02A7` | `REQUEST_ITEM_GROWTYPE_EXPERIENCE` | 布局已闭合 | `0` | [0x02A7.md](client-to-server/0x02A7.md) |
| `0x02A8` | `LOAD_EXTEND_CHARACS` | **手写记录** | 见文档 | [0x02A8.md](client-to-server/0x02A8.md) **(手写)** |
| `0x02A9` | `START_SLOT_MACHINE` | 结构未知 | `—` | [0x02A9.md](client-to-server/0x02A9.md) |
| `0x02AA` | `EXECUTE_SLOT_MACHINE` | 结构未知 | `—` | [0x02AA.md](client-to-server/0x02AA.md) |
| `0x02AB` | `BINGO_RESET` | 布局部分可得 | `≥ 4` | [0x02AB.md](client-to-server/0x02AB.md) |
| `0x02B0` | `ILLUSION_UPGRADE_STATE` | 布局已闭合 | `2` | [0x02B0.md](client-to-server/0x02B0.md) |
| `0x02B1` | `EVENT_REWARD` | 布局部分可得 | `≥ 8` | [0x02B1.md](client-to-server/0x02B1.md) |
| `0x02B2` | `EVENT_REQUEST` | 布局部分可得 | `≥ 16` | [0x02B2.md](client-to-server/0x02B2.md) |
| `0x02B3` | `STATICS_RUNTIME_TING` | 布局已闭合 | `1` | [0x02B3.md](client-to-server/0x02B3.md) |
| `0x02B4` | `RESERVE_LEAVE_PARTY` | 布局已闭合 | `1` | [0x02B4.md](client-to-server/0x02B4.md) |
| `0x02B5` | `CHECK_DOUBLE_CHARACTER_NAME` | **手写记录** | 见文档 | [0x02B5.md](client-to-server/0x02B5.md) **(手写)** |
| `0x02B6` | `CRACK_OF_DIMENSION` | 布局已闭合 | `8` | [0x02B6.md](client-to-server/0x02B6.md) |
| `0x02B7` | `RAID_BUFF_SYSTEM` | 布局部分可得 | `≥ 3 + n×2` | [0x02B7.md](client-to-server/0x02B7.md) |
| `0x02B8` | `RAID_MONSTER_HP` | 布局部分可得 | `≥ 1` | [0x02B8.md](client-to-server/0x02B8.md) |
| `0x02B9` | `DUNGEON_BONUS_MONSTER` | 布局已闭合 | `4` | [0x02B9.md](client-to-server/0x02B9.md) |
| `0x02BC` | `DAILY_CHALLENGE_REWARD` | 布局已闭合 | `4` | [0x02BC.md](client-to-server/0x02BC.md) |
| `0x02BD` | `REQUEST_STUDY_JOIN_REWARD` | 布局已闭合 | `0` | [0x02BD.md](client-to-server/0x02BD.md) |
| `0x02BE` | `FARM_EVENT_ACTION` | 布局部分可得 | `≥ 3` | [0x02BE.md](client-to-server/0x02BE.md) |
| `0x02BF` | `UPDATE_SPEC_EVENT` | 布局部分可得 | `≥ 1 + n×5` | [0x02BF.md](client-to-server/0x02BF.md) |
| `0x02C1` | `CONTENTS_PLAY_INFO` | 布局已闭合 | `1` | [0x02C1.md](client-to-server/0x02C1.md) |
| `0x02C2` | `ENTRY_INTO_PARTY` | 布局已闭合 | `4` | [0x02C2.md](client-to-server/0x02C2.md) |
| `0x02C3` | `ENTRY_INTO_PARTY_FINISH` | 布局已闭合 | `0` | [0x02C3.md](client-to-server/0x02C3.md) |
| `0x02C5` | `JOIN_REAL_ESTATE` | 结构未知 | `—` | [0x02C5.md](client-to-server/0x02C5.md) |
| `0x02C6` | `LEAVE_REAL_ESTATE` | 结构未知 | `—` | [0x02C6.md](client-to-server/0x02C6.md) |
| `0x02C7` | `START_REAL_ESTATE` | 结构未知 | `—` | [0x02C7.md](client-to-server/0x02C7.md) |
| `0x02C8` | `REAL_ESTATE_USER_STATE` | 布局已闭合 | `1` | [0x02C8.md](client-to-server/0x02C8.md) |
| `0x02C9` | `UDP_PACKET_STATISTIC` | 布局已闭合 | `528` | [0x02C9.md](client-to-server/0x02C9.md) |
| `0x02CA` | `REQUEST_ITEM_SORT_LOCK` | 布局部分可得 | `≥ 3` | [0x02CA.md](client-to-server/0x02CA.md) |
| `0x02CB` | `REQUEST_ITEM_SORT_UNLOCK` | 结构未知 | `—` | [0x02CB.md](client-to-server/0x02CB.md) |
| `0x02CC` | `SHOP_PURCHASE_COUNT` | 布局已闭合 | `4` | [0x02CC.md](client-to-server/0x02CC.md) |
| `0x02CD` | `SLOT_MACHINE_UPDATE_ITEM_LIST` | 布局已闭合 | `2` | [0x02CD.md](client-to-server/0x02CD.md) |
| `0x02CE` | `SET_PCROOM_CHOICE_AND_FOCUS_MISSION` | 布局已闭合 | `1` | [0x02CE.md](client-to-server/0x02CE.md) |
| `0x02CF` | `GET_PCROOM_CHOICE_AND_FOCUS_ITEM` | 布局部分可得 | `≥ 2` | [0x02CF.md](client-to-server/0x02CF.md) |
| `0x02D0` | `TERRITORY_COMBAT_SYMBOL` | 布局部分可得 | `—` | [0x02D0.md](client-to-server/0x02D0.md) |
| `0x02D1` | `TERRITORY_COMBAT_RESPAWN` | 布局部分可得 | `≥ 16` | [0x02D1.md](client-to-server/0x02D1.md) |
| `0x02D2` | `TERRITORY_COMBAT_SITUATION_ROOM` | 布局已闭合 | `0` | [0x02D2.md](client-to-server/0x02D2.md) |
| `0x02D3` | `TERRITORY_COMBAT_SKILL` | 布局已闭合 | `8` | [0x02D3.md](client-to-server/0x02D3.md) |
| `0x02D4` | `TERRITORY_COMBAT_HP` | 布局已闭合 | `4` | [0x02D4.md](client-to-server/0x02D4.md) |
| `0x02D5` | `TERRITORY_COMBAT_SUPPLY_ITEM` | 结构未知 | `—` | [0x02D5.md](client-to-server/0x02D5.md) |
| `0x02D6` | `TERRITORY_COMBAT_REQUEST_SUPPLY_ITEM` | 布局已闭合 | `4` | [0x02D6.md](client-to-server/0x02D6.md) |
| `0x02D7` | `TERRITORY_COMBAT_CHANGE_ROLE` | 布局部分可得 | `≥ 11` | [0x02D7.md](client-to-server/0x02D7.md) |
| `0x02D8` | `TERRITORY_COMBAT_SECRET_PATH` | 布局已闭合（含计数重复组） | `8 + n×4` | [0x02D8.md](client-to-server/0x02D8.md) |
| `0x02D9` | `TERRITORY_COMBAT_REQ_MOVE_DUNGEON` | 布局已闭合 | `14` | [0x02D9.md](client-to-server/0x02D9.md) |
| `0x02DA` | `TERRITORY_COMBAT_RES_MOVE_DUNGEON` | 布局已闭合 | `8` | [0x02DA.md](client-to-server/0x02DA.md) |
| `0x02DB` | `TERRITORY_COMBAT_WAIT_ROOM` | 布局已闭合 | `8` | [0x02DB.md](client-to-server/0x02DB.md) |
| `0x02DC` | `TERRITORY_COMBAT_DUNGEON_RESULT` | 布局已闭合 | `54` | [0x02DC.md](client-to-server/0x02DC.md) |
| `0x02DE` | `DUNGEON_DAMAGE_INFO` | 布局部分可得 | `≥ 84` | [0x02DE.md](client-to-server/0x02DE.md) |
| `0x02DF` | `USE_GOLDCARD_REAL_ESTATE` | 布局已闭合 | `0` | [0x02DF.md](client-to-server/0x02DF.md) |
| `0x02E0` | `REJOIN_DUNGEON` | 布局已闭合 | `8` | [0x02E0.md](client-to-server/0x02E0.md) |
| `0x02E1` | `CANCEL_REJOIN_DUNGEON` | 布局已闭合 | `4` | [0x02E1.md](client-to-server/0x02E1.md) |
| `0x02E2` | `CHECK_GUILD_CREATE_PROMOTE_MSG` | 布局部分可得 | `—` | [0x02E2.md](client-to-server/0x02E2.md) |
| `0x02E3` | `MODIFY_GUILD_PROMOTE_MSG` | 布局部分可得 | `—` | [0x02E3.md](client-to-server/0x02E3.md) |
| `0x02E4` | `REQUEST_GUILD_CREATE_PERMIT` | 布局部分可得 | `—` | [0x02E4.md](client-to-server/0x02E4.md) |
| `0x02E5` | `REPLY_GUILD_CREATE_PERMIT` | 布局已闭合 | `1` | [0x02E5.md](client-to-server/0x02E5.md) |
| `0x02E7` | `REQ_GUILD_INFO_OF_MY_CHARS` | 布局已闭合 | `0` | [0x02E7.md](client-to-server/0x02E7.md) |
| `0x02E8` | `REQ_GUILD_SERCH_FOR_JOIN` | 布局部分可得 | `—` | [0x02E8.md](client-to-server/0x02E8.md) |
| `0x02E9` | `TODAY_GUILD_ATTENDANCE_DETAIL_INFO` | 布局已闭合 | `0` | [0x02E9.md](client-to-server/0x02E9.md) |
| `0x02EA` | `REQ_GUILD_MILEAGE_HISTORY` | 布局已闭合 | `0` | [0x02EA.md](client-to-server/0x02EA.md) |
| `0x02EB` | `CHANGE_GUILD_GRADE` | 布局已闭合 | `5` | [0x02EB.md](client-to-server/0x02EB.md) |
| `0x02EC` | `CHANGE_GUILD_GRADE_NAME` | 布局部分可得 | `—` | [0x02EC.md](client-to-server/0x02EC.md) |
| `0x02ED` | `VARIABLE_NEED_MATERIAL` | 布局部分可得 | `≥ 16` | [0x02ED.md](client-to-server/0x02ED.md) |
| `0x02EE` | `TRY_PUZZLE` | 布局已闭合 | `14` | [0x02EE.md](client-to-server/0x02EE.md) |
| `0x02EF` | `FPS_PERFORMANCE_STATISTIC` | 布局已闭合 | `424` | [0x02EF.md](client-to-server/0x02EF.md) |
| `0x02F0` | `SET_USER_AREA_PRE_CHECK` | 布局部分可得 | `≥ 12` | [0x02F0.md](client-to-server/0x02F0.md) |
| `0x02F1` | `REQ_GUILD_ALLIANCE` | 布局部分可得 | `—` | [0x02F1.md](client-to-server/0x02F1.md) |
| `0x02F2` | `REQ_GUILD_ALLIANCE_LIST` | 结构未知 | `—` | [0x02F2.md](client-to-server/0x02F2.md) |
| `0x02F3` | `CANCEL_REQ_GUILD_ALLIANCE` | 布局已闭合 | `4` | [0x02F3.md](client-to-server/0x02F3.md) |
| `0x02F4` | `CALL_GUILD_ALLIANCE_LIST` | 布局已闭合 | `0` | [0x02F4.md](client-to-server/0x02F4.md) |
| `0x02F5` | `APPROVE_GUILD_ALLIANCE` | 布局部分可得 | `—` | [0x02F5.md](client-to-server/0x02F5.md) |
| `0x02F6` | `DENY_GUILD_ALLIANCE` | 布局部分可得 | `≥ 4` | [0x02F6.md](client-to-server/0x02F6.md) |
| `0x02F7` | `SECEDE_GUILD_ALLIANCE` | 布局部分可得 | `—` | [0x02F7.md](client-to-server/0x02F7.md) |
| `0x02F8` | `BUY_GUILD_CONTENTS` | 布局已闭合 | `4` | [0x02F8.md](client-to-server/0x02F8.md) |
| `0x02F9` | `REQ_RECOMMEND_GUILD` | 布局已闭合 | `0` | [0x02F9.md](client-to-server/0x02F9.md) |
| `0x02FA` | `SEND_GUILD_PRESENT` | 布局已闭合 | `6` | [0x02FA.md](client-to-server/0x02FA.md) |
| `0x02FB` | `REQ_GUILD_PRESENTS_LIST` | 布局已闭合 | `0` | [0x02FB.md](client-to-server/0x02FB.md) |
| `0x02FC` | `REQ_GUILD_PRESENTS_HISTORY` | 布局已闭合 | `0` | [0x02FC.md](client-to-server/0x02FC.md) |
| `0x02FD` | `RECV_GUILD_PRESENT` | 布局部分可得 | `≥ 11` | [0x02FD.md](client-to-server/0x02FD.md) |
| `0x02FE` | `REGISTER_GUILD_SUPPORT_CHARAC` | 布局已闭合 | `0` | [0x02FE.md](client-to-server/0x02FE.md) |
| `0x02FF` | `REMOVE_GUILD_SUPPORT_CHARAC` | 布局已闭合 | `4` | [0x02FF.md](client-to-server/0x02FF.md) |
| `0x0300` | `RENT_GUILD_SUPPORT_CHARAC` | 布局已闭合 | `8` | [0x0300.md](client-to-server/0x0300.md) |
| `0x0301` | `RENT_CANCEL_GUILD_SUPPORT_CHARAC` | 布局已闭合 | `0` | [0x0301.md](client-to-server/0x0301.md) |
| `0x0302` | `INFO_GUILD_SUPPORT_CHARAC` | 布局部分可得 | `≥ 8` | [0x0302.md](client-to-server/0x0302.md) |
| `0x0303` | `USE_GUILD_SUPPORT_CHARAC` | 结构未知 | `—` | [0x0303.md](client-to-server/0x0303.md) |
| `0x0304` | `WEB_VIEWER_INFO` | 布局已闭合 | `1` | [0x0304.md](client-to-server/0x0304.md) |
| `0x0307` | `DESTROY_ASSAULT_OBJECT` | 布局部分可得 | `≥ 1` | [0x0307.md](client-to-server/0x0307.md) |
| `0x0308` | `REQUEST_CIRCLE_ENTER` | 布局部分可得 | `≥ 8` | [0x0308.md](client-to-server/0x0308.md) |
| `0x0309` | `CHECK_TERRITORY_COMBAT_DECLARATION` | 布局已闭合 | `0` | [0x0309.md](client-to-server/0x0309.md) |
| `0x030A` | `REQ_TERRITORY_COMBAT_DECLARATION` | 布局已闭合 | `6` | [0x030A.md](client-to-server/0x030A.md) |
| `0x030B` | `REQ_TERRITORY_COMBAT_DECLARATION_LIST` | 布局已闭合 | `0` | [0x030B.md](client-to-server/0x030B.md) |
| `0x030C` | `CHECK_TERRITORY_COMBAT_INDIRECT_PART` | 布局已闭合 | `2` | [0x030C.md](client-to-server/0x030C.md) |
| `0x030D` | `REQ_TERRITORY_COMBAT_INDIRECT_PART` | 布局已闭合 | `2` | [0x030D.md](client-to-server/0x030D.md) |
| `0x030E` | `REQ_TERRITORY_COMBAT_INDIRECT_PART_STATUS` | 布局已闭合 | `1` | [0x030E.md](client-to-server/0x030E.md) |
| `0x030F` | `REQ_TERRITORY_COMBAT_INDIRECT_PART_REWARD` | 结构未知 | `—` | [0x030F.md](client-to-server/0x030F.md) |
| `0x0310` | `RE_GROWUP_CHANGE` | 布局已闭合 | `15` | [0x0310.md](client-to-server/0x0310.md) |
| `0x0311` | `SKILL_QUICK_SLOT_SORT` | 结构未知 | `—` | [0x0311.md](client-to-server/0x0311.md) |
| `0x0312` | `CHANGE_GUILD_MARK` | 布局已闭合 | `1` | [0x0312.md](client-to-server/0x0312.md) |
| `0x0313` | `EPIC_BOOK_MAKE_ITEM` | 布局已闭合 | `4` | [0x0313.md](client-to-server/0x0313.md) |
| `0x0314` | `REQUEST_SERVER_CHARACTER_LIST` | 布局已闭合 | `0` | [0x0314.md](client-to-server/0x0314.md) |
| `0x0315` | `SCENARIO_MODE_CLEAR_QUEST` | 布局已闭合 | `4` | [0x0315.md](client-to-server/0x0315.md) |
| `0x0316` | `INPUT_USER_CONGRATULATORY_TELEGRAM` | 布局部分可得 | `—` | [0x0316.md](client-to-server/0x0316.md) |
| `0x0317` | `ASK_FOR_TERRITORY_COMBAT` | 布局已闭合 | `1` | [0x0317.md](client-to-server/0x0317.md) |
| `0x0318` | `REQ_TERRITORY_COMBAT_LIST` | 布局已闭合 | `0` | [0x0318.md](client-to-server/0x0318.md) |
| `0x0319` | `DECIDE_TERRITORY_COMBAT_LIST` | 布局部分可得 | `—` | [0x0319.md](client-to-server/0x0319.md) |
| `0x031A` | `REQ_DECIDE_TERRITORY_COMBAT_LIST` | 结构未知 | `—` | [0x031A.md](client-to-server/0x031A.md) |
| `0x031B` | `CHECK_TERRITORY_COMBAT_CHANNEL_ENTER` | 结构未知 | `—` | [0x031B.md](client-to-server/0x031B.md) |
| `0x031C` | `CHECK_TERRITORY_COMBAT_EXERCISE_MODE_TIME` | 布局已闭合 | `0` | [0x031C.md](client-to-server/0x031C.md) |
| `0x031F` | `REQ_TERRITORY_OPERATION_FUND_DISTRIBUTION` | 布局已闭合 | `2` | [0x031F.md](client-to-server/0x031F.md) |
| `0x0320` | `AVATAR_CONVERSION` | 布局部分可得 | `≥ 4 + n×6` | [0x0320.md](client-to-server/0x0320.md) |
| `0x0323` | `REQUEST_USER_CHANNEL` | 布局部分可得 | `—` | [0x0323.md](client-to-server/0x0323.md) |
| `0x0324` | `REMAKE_JUMPING_CHARACTER` | 布局已闭合 | `1` | [0x0324.md](client-to-server/0x0324.md) |
| `0x0325` | `INCUBATING_SYSTEM_REWARD` | 布局已闭合 | `0` | [0x0325.md](client-to-server/0x0325.md) |
| `0x0328` | `GUILD_CONTRIBUTE_HISTORY` | 布局已闭合 | `0` | [0x0328.md](client-to-server/0x0328.md) |
| `0x0329` | `REQ_REPRESENT_CHARACTER` | 布局部分可得 | `—` | [0x0329.md](client-to-server/0x0329.md) |
| `0x032A` | `REQUEST_NPC_FAVOR_OPERATION` | 布局部分可得 | `≥ 8` | [0x032A.md](client-to-server/0x032A.md) |
| `0x032B` | `GIVE_NPC_BUFF` | 布局部分可得 | `≥ 8` | [0x032B.md](client-to-server/0x032B.md) |
| `0x032C` | `LICENSE_DUNGEON_PLAY_RESULT` | 布局部分可得 | `≥ 5 + n×6` | [0x032C.md](client-to-server/0x032C.md) |
| `0x032D` | `LICENSE_DUNGEON_REQUEST_REWARD` | 布局已闭合 | `0` | [0x032D.md](client-to-server/0x032D.md) |
| `0x032E` | `EPIC_PIECE_EXCHANGE_CHOICE` | 布局已闭合 | `4` | [0x032E.md](client-to-server/0x032E.md) |
| `0x032F` | `EPIC_PIECE_EXCHANGE_MATERIAL_CHOICE` | 结构未知 | `—` | [0x032F.md](client-to-server/0x032F.md) |
| `0x0330` | `EPIC_PIECE_EXCHANGE_COMPLETE` | 结构未知 | `—` | [0x0330.md](client-to-server/0x0330.md) |
| `0x0331` | `RAID_OTHER_CHANNEL_USERINFO` | 布局已闭合 | `4` | [0x0331.md](client-to-server/0x0331.md) |
| `0x0332` | `RAID_OTHER_CHANNEL_REQUEST_JOIN` | 布局部分可得 | `≥ 9` | [0x0332.md](client-to-server/0x0332.md) |
| `0x0333` | `RAID_OTHER_CHANNEL_RESPONSE_JOIN` | 布局部分可得 | `≥ 13` | [0x0333.md](client-to-server/0x0333.md) |
| `0x0334` | `RAID_RECENT_FRIEND_LIST` | 结构未知 | `—` | [0x0334.md](client-to-server/0x0334.md) |
| `0x0335` | `RAID_MEMBER_CHANGE_STATE` | 布局已闭合 | `1` | [0x0335.md](client-to-server/0x0335.md) |
| `0x0336` | `RAID_USER_MOVE_CHANNEL_FAIL` | 布局已闭合 | `3` | [0x0336.md](client-to-server/0x0336.md) |
| `0x0337` | `REQ_TERRITORY_COMBAT_ALLIANCE_LIST` | 结构未知 | `—` | [0x0337.md](client-to-server/0x0337.md) |
| `0x0338` | `REQ_TERRITORY_COMBAT_POINT_LIST` | 布局已闭合 | `0` | [0x0338.md](client-to-server/0x0338.md) |
| `0x0339` | `SET_TERRITORY_OPERATION_FUND_DISTRIBUTE_TYPE` | 布局已闭合 | `1` | [0x0339.md](client-to-server/0x0339.md) |
| `0x033A` | `USE_GEM` | 布局已闭合 | `11` | [0x033A.md](client-to-server/0x033A.md) |
| `0x033B` | `SET_GUILD_RECOMMAND_CHANNEL` | 布局已闭合 | `0` | [0x033B.md](client-to-server/0x033B.md) |
| `0x033C` | `RAID_OTHER_CHANNEL_LIST` | 布局已闭合 | `1` | [0x033C.md](client-to-server/0x033C.md) |
| `0x033D` | `SET_GUILD_FLAG_AURA` | 布局已闭合 | `8` | [0x033D.md](client-to-server/0x033D.md) |
| `0x0340` | `COMPOUND_GEM` | 布局已闭合 | `38` | [0x0340.md](client-to-server/0x0340.md) |
| `0x034E` | `MAIN_HUD_INFO` | 布局已闭合 | `164` | [0x034E.md](client-to-server/0x034E.md) |
| `0x034F` | `REQ_BINGO_MARK` | 布局已闭合 | `1` | [0x034F.md](client-to-server/0x034F.md) |
| `0x0351` | `SEASON_SERVER_USER_INFO` | 结构未知 | `—` | [0x0351.md](client-to-server/0x0351.md) |
| `0x0352` | `ADVENTURER_MAKER_CREATE` | 布局部分可得 | `—` | [0x0352.md](client-to-server/0x0352.md) |
| `0x0353` | `ADVENTURER_MAKER_GROW_REQUEST` | 布局已闭合 | `2` | [0x0353.md](client-to-server/0x0353.md) |
| `0x0354` | `ADVENTURER_MAKER_INITIALIZE` | 结构未知 | `—` | [0x0354.md](client-to-server/0x0354.md) |
| `0x0355` | `ADVENTURER_MAKER_GROW_REST` | 布局部分可得 | `≥ 13` | [0x0355.md](client-to-server/0x0355.md) |
| `0x0356` | `ADVENTURER_MAKER_APPEAR_NPC` | 结构未知 | `—` | [0x0356.md](client-to-server/0x0356.md) |
| `0x0357` | `OPEN_AURA_SKIN_SLOT` | 布局已闭合 | `4` | [0x0357.md](client-to-server/0x0357.md) |
| `0x0358` | `COMPOUND_FLAG` | 布局已闭合 | `38` | [0x0358.md](client-to-server/0x0358.md) |
| `0x0359` | `SEASON_CHARAC_INFO` | 布局已闭合 | `60` | [0x0359.md](client-to-server/0x0359.md) |
| `0x035A` | `SEASON_CHARAC_CONVERT` | 结构未知 | `—` | [0x035A.md](client-to-server/0x035A.md) |
| `0x035C` | `SYNC_ITEM_SPACE` | 布局已闭合 | `1` | [0x035C.md](client-to-server/0x035C.md) |
| `0x035D` | `SEQUENTIAL_DUNGEON_INFO` | 布局已闭合 | `4` | [0x035D.md](client-to-server/0x035D.md) |
| `0x035E` | `SET_PVP_TOTAL_MATCH_TEAM` | 布局部分可得 | `—` | [0x035E.md](client-to-server/0x035E.md) |
| `0x035F` | `CHECK_PVP_TOTAL_MATCH_TEAM_NAME` | 布局部分可得 | `—` | [0x035F.md](client-to-server/0x035F.md) |
| `0x0360` | `SET_PVP_TOTAL_MATCH_TEAM_NAME` | 布局部分可得 | `—` | [0x0360.md](client-to-server/0x0360.md) |
| `0x0361` | `SET_PVP_TOTAL_MATCH_CHARAC_ORDER` | 布局已闭合 | `1` | [0x0361.md](client-to-server/0x0361.md) |
| `0x0362` | `TRANSFORMATION_ITEM` | 布局部分可得 | `≥ 8` | [0x0362.md](client-to-server/0x0362.md) |
| `0x0363` | `RAID_REQUEST_RAID_MEMBERS` | 布局已闭合 | `4` | [0x0363.md](client-to-server/0x0363.md) |
| `0x0364` | `RAID_CHECK_RAID_USER` | 布局已闭合 | `3` | [0x0364.md](client-to-server/0x0364.md) |
| `0x036D` | `EVENT_DNFTREND_GET_REWARD` | 布局已闭合 | `2` | [0x036D.md](client-to-server/0x036D.md) |
| `0x036F` | `PREMIUM_SERVICE` | 布局已闭合 | `76` | [0x036F.md](client-to-server/0x036F.md) |
| `0x0370` | `ANTIBOT_DELAY_LOG` | 布局已闭合 | `24` | [0x0370.md](client-to-server/0x0370.md) |
| `0x0371` | `EVENT_DUNGEON_DESTORY_OBJECT` | 布局已闭合 | `22` | [0x0371.md](client-to-server/0x0371.md) |
| `0x0372` | `EVENT_DUNGEON_CLEAR_ROOM` | 布局已闭合 | `20` | [0x0372.md](client-to-server/0x0372.md) |
| `0x0378` | `INFORM_LOCK_TIME` | 布局已闭合 | `4` | [0x0378.md](client-to-server/0x0378.md) |
| `0x0379` | `USE_EMBLEM_FOR_EQUIPMENT` | 布局部分可得 | `≥ 8 + n×7` | [0x0379.md](client-to-server/0x0379.md) |
| `0x037A` | `ADD_EQUIPMENT_SOCKET` | 布局部分可得 | `≥ 8` | [0x037A.md](client-to-server/0x037A.md) |
| `0x037B` | `CONVERT_EMBLEM` | 布局部分可得 | `≥ 12` | [0x037B.md](client-to-server/0x037B.md) |
| `0x0389` | `KILL_PLAYER_LOG` | 布局部分可得 | `≥ 10 + n×8` | [0x0389.md](client-to-server/0x0389.md) |
| `0x0390` | `EVENT_LOGIN_AND_GIFT` | 布局已闭合 | `0` | [0x0390.md](client-to-server/0x0390.md) |
| `0x0392` | `ADVANCE_ALTAR_START_GAME` | 布局已闭合 | `0` | [0x0392.md](client-to-server/0x0392.md) |
| `0x0393` | `ADVANCE_ALTAR_BUY_ITEM` | 布局已闭合 | `30` | [0x0393.md](client-to-server/0x0393.md) |
| `0x0394` | `ADVANCE_ALTAR_SET_SLOT` | 布局已闭合 | `28` | [0x0394.md](client-to-server/0x0394.md) |
| `0x0395` | `ADVANCE_ALTAR_UPGRADE_GAGE` | 布局已闭合 | `22` | [0x0395.md](client-to-server/0x0395.md) |
| `0x0396` | `ADVANCE_ALTAR_SUMMON_UNIT` | 布局已闭合 | `23` | [0x0396.md](client-to-server/0x0396.md) |
| `0x0397` | `ADVANCE_ALTAR_EXCHANGE_SLOT` | 布局已闭合 | `37` | [0x0397.md](client-to-server/0x0397.md) |
| `0x0398` | `ADVANCE_ALTAR_PAUSE` | 布局已闭合 | `15` | [0x0398.md](client-to-server/0x0398.md) |
| `0x0399` | `ADVANCE_ALTAR_GET_ACHIEVEMENT_REWARD` | 布局已闭合 | `18` | [0x0399.md](client-to-server/0x0399.md) |
| `0x039A` | `ADVANCE_ALTAR_RESET_STAR` | 布局已闭合 | `18` | [0x039A.md](client-to-server/0x039A.md) |
| `0x039D` | `REQ_IGA_VER` | 布局已闭合 | `16` | [0x039D.md](client-to-server/0x039D.md) |
| `0x039E` | `PERIOD_ITEM_REBUY_STATISTIC` | 布局已闭合 | `8` | [0x039E.md](client-to-server/0x039E.md) |
| `0x039F` | `ADD_EQUIPMENT_EFFECT` | 布局已闭合 | `22` | [0x039F.md](client-to-server/0x039F.md) |
| `0x03AA` | `QUIZ_REQ_ITEM` | 布局已闭合 | `20` | [0x03AA.md](client-to-server/0x03AA.md) |
| `0x03AB` | `QUIZ_RES_ITEM` | 布局已闭合 | `24` | [0x03AB.md](client-to-server/0x03AB.md) |
| `0x03AE` | `P2P_HOLE_PUNCHING_SUCCESS_RATE` | 布局已闭合 | `23` | [0x03AE.md](client-to-server/0x03AE.md) |
| `0x03AF` | `CHARACTER_CREATE_COUNT_PER_DAY` | 结构未知 | `—` | [0x03AF.md](client-to-server/0x03AF.md) |
| `0x03B0` | `USE_TITLE_CHANGE_ITEM` | 布局已闭合 | `4` | [0x03B0.md](client-to-server/0x03B0.md) |
| `0x03B4` | `DESTORY_DUNGEON_START` | 布局已闭合 | `22` | [0x03B4.md](client-to-server/0x03B4.md) |
| `0x03B5` | `COMPOUND_UNIQUE_ITEM` | 布局部分可得 | `≥ 5` | [0x03B5.md](client-to-server/0x03B5.md) |
| `0x03B8` | `EMBLEM_COLLECT_ITEM` | 布局已闭合 | `26` | [0x03B8.md](client-to-server/0x03B8.md) |
| `0x03B9` | `READ_HUNTING_SKILL` | 布局已闭合 | `15` | [0x03B9.md](client-to-server/0x03B9.md) |
| `0x03BA` | `UPGRADE_CARRY_GOLD` | 布局已闭合 | `15` | [0x03BA.md](client-to-server/0x03BA.md) |
| `0x03C1` | `USER_CHOICE_EVENT` | 布局已闭合 | `16` | [0x03C1.md](client-to-server/0x03C1.md) |
| `0x03C2` | `EVENT_ATTENDANCE_REWARD` | 布局已闭合 | `16` | [0x03C2.md](client-to-server/0x03C2.md) |
| `0x03C3` | `QQ_PCROOM_BENEFIT_REWARD` | 布局已闭合 | `16` | [0x03C3.md](client-to-server/0x03C3.md) |
| `0x03CC` | `SERIA_RIDABLE_IN_HIDDEN_TRUTH_DUNGEON` | 布局已闭合 | `15` | [0x03CC.md](client-to-server/0x03CC.md) |
| `0x03CE` | `BUY_MOON_FESTIVAL_ITEM` | 布局已闭合 | `18` | [0x03CE.md](client-to-server/0x03CE.md) |
| `0x03CF` | `RENT_EQUIPMENT_ITEM` | 布局已闭合 | `22` | [0x03CF.md](client-to-server/0x03CF.md) |
| `0x03D0` | `CHARGE_RENTPOINT` | 布局已闭合 | `22` | [0x03D0.md](client-to-server/0x03D0.md) |
| `0x03D1` | `REQUEST_RECEIVE_ATTANDANCE_REWARD` | 布局已闭合 | `18` | [0x03D1.md](client-to-server/0x03D1.md) |
| `0x03DB` | `CHAIN_LETTERS_REWARD` | 布局已闭合 | `18` | [0x03DB.md](client-to-server/0x03DB.md) |
| `0x03DC` | `SEAL_CREATURE` | 结构未知 | `—` | [0x03DC.md](client-to-server/0x03DC.md) |
| `0x03DD` | `DNF_WITH_FRIENDS_RECOMMEND` | 布局已闭合 | `24` | [0x03DD.md](client-to-server/0x03DD.md) |
| `0x03DE` | `DNF_WITH_FRIENDS_INFO` | 布局已闭合 | `14` | [0x03DE.md](client-to-server/0x03DE.md) |
| `0x03DF` | `DNF_WITH_FRIENDS_BUY` | 布局已闭合 | `16` | [0x03DF.md](client-to-server/0x03DF.md) |
| `0x03E1` | `EVENT_GROWTH_EQUIPMENT` | 布局已闭合 | `18` | [0x03E1.md](client-to-server/0x03E1.md) |
| `0x03E5` | `SELECT_COLLECTBOX` | 布局已闭合 | `1` | [0x03E5.md](client-to-server/0x03E5.md) |
| `0x03E6` | `ADD_COLLECTBOX_ITEM` | 布局部分可得 | `≥ 8` | [0x03E6.md](client-to-server/0x03E6.md) |
| `0x03E7` | `REMOVE_COLLECTBOX_ITEM` | 布局部分可得 | `≥ 5` | [0x03E7.md](client-to-server/0x03E7.md) |
| `0x03E8` | `EXTEND_COLLECTBOX_EXPIRY_DATE` | 布局已闭合 | `8` | [0x03E8.md](client-to-server/0x03E8.md) |
| `0x03EE` | `UNIQUE_BIND_SHPERE_INFO` | 布局已闭合 | `22` | [0x03EE.md](client-to-server/0x03EE.md) |
| `0x03EF` | `PREPARE_NEW_BALANCE` | 布局已闭合 | `19` | [0x03EF.md](client-to-server/0x03EF.md) |
| `0x03F0` | `WEDDING_REQUEST` | 布局已闭合 | `21` | [0x03F0.md](client-to-server/0x03F0.md) |
| `0x03F1` | `WEDDING_RESPONSE` | 布局已闭合 | `22` | [0x03F1.md](client-to-server/0x03F1.md) |
| `0x03F2` | `WEDDING_MONEY_GIFT` | 布局部分可得 | `—` | [0x03F2.md](client-to-server/0x03F2.md) |
| `0x03F3` | `WEDDING_CHARAC` | 布局部分可得 | `≥ 18` | [0x03F3.md](client-to-server/0x03F3.md) |
| `0x03F4` | `WEDDING_UPGRADE_RING` | 布局已闭合 | `19` | [0x03F4.md](client-to-server/0x03F4.md) |
| `0x03F5` | `WEDDING_USE_LOVE_POINT_ITEM` | 布局已闭合 | `18` | [0x03F5.md](client-to-server/0x03F5.md) |
| `0x03F6` | `WEDDING_SEND_LETTER` | 布局已闭合 | `22` | [0x03F6.md](client-to-server/0x03F6.md) |
| `0x03F7` | `WEDDING_ENTER_CEREMONY` | 布局已闭合 | `23` | [0x03F7.md](client-to-server/0x03F7.md) |
| `0x03F8` | `UPDATE_GUEST_COMMENT` | 布局部分可得 | `—` | [0x03F8.md](client-to-server/0x03F8.md) |
| `0x03F9` | `DELETE_GUEST_COMMENT` | 布局已闭合 | `23` | [0x03F9.md](client-to-server/0x03F9.md) |
| `0x03FA` | `LOAD_GUEST_COMMENT` | 布局已闭合 | `19` | [0x03FA.md](client-to-server/0x03FA.md) |
| `0x03FB` | `CHECK_MOVE_TO_PARTNER` | 布局已闭合 | `22` | [0x03FB.md](client-to-server/0x03FB.md) |
| `0x03FC` | `MOVE_TO_PARTNER` | 布局已闭合 | `34` | [0x03FC.md](client-to-server/0x03FC.md) |
| `0x03FD` | `COUPLE_INVEN_OPEN_CLOSE` | 布局已闭合 | `15` | [0x03FD.md](client-to-server/0x03FD.md) |
| `0x03FE` | `COUPLE_ROOM_MOVE_ITEM` | 布局已闭合 | `28` | [0x03FE.md](client-to-server/0x03FE.md) |
| `0x03FF` | `COUPLE_INVEN_DELETE_ITEM` | 布局已闭合 | `24` | [0x03FF.md](client-to-server/0x03FF.md) |
| `0x0400` | `COUPLE_TREE_PLANT` | 布局已闭合 | `20` | [0x0400.md](client-to-server/0x0400.md) |
| `0x0401` | `COUPLE_TREE_WATER` | 布局已闭合 | `20` | [0x0401.md](client-to-server/0x0401.md) |
| `0x0402` | `COUPLE_TREE_HARVEST` | 布局已闭合 | `0` | [0x0402.md](client-to-server/0x0402.md) |
| `0x0403` | `COUPLE_TREE_REMOVE` | 结构未知 | `—` | [0x0403.md](client-to-server/0x0403.md) |
| `0x0404` | `MAKE_COUPLE_GROWTHCAPSULE` | 布局已闭合 | `0` | [0x0404.md](client-to-server/0x0404.md) |
| `0x0405` | `GIVE_COUPLE_GROWTHCAPSULE` | 布局已闭合 | `0` | [0x0405.md](client-to-server/0x0405.md) |
| `0x0406` | `TAKE_COUPLE_GROWTHCAPSULE` | 布局已闭合 | `0` | [0x0406.md](client-to-server/0x0406.md) |
| `0x0407` | `DRAGON_DUNGEON_TIMEATTACK_RANKER_BOARD` | 布局已闭合 | `2` | [0x0407.md](client-to-server/0x0407.md) |
| `0x0408` | `BREAK_TRAP_RESULT` | 布局已闭合 | `0` | [0x0408.md](client-to-server/0x0408.md) |
| `0x0409` | `EVENT_CHANGE_CLASS` | 布局已闭合 | `0` | [0x0409.md](client-to-server/0x0409.md) |
| `0x040B` | `SELECT_RANDOM_FORTUNE` | 结构未知 | `—` | [0x040B.md](client-to-server/0x040B.md) |
| `0x040C` | `PLAY_BLUE_HORSE_SLOT` | 布局已闭合 | `1` | [0x040C.md](client-to-server/0x040C.md) |
| `0x040D` | `REWARD_BLUE_HORSE_SLOT` | 布局已闭合 | `1` | [0x040D.md](client-to-server/0x040D.md) |
| `0x0410` | `BINGO_STAMP_INFO` | 布局已闭合 | `26` | [0x0410.md](client-to-server/0x0410.md) |
| `0x0411` | `RETRY_BOOTER_INFO` | 布局已闭合 | `22` | [0x0411.md](client-to-server/0x0411.md) |
| `0x0412` | `OLYMPIC_MEDAL_INFO` | 布局已闭合 | `1` | [0x0412.md](client-to-server/0x0412.md) |
| `0x0413` | `VERY_DIFFICULT_HELL_PARTY` | 布局已闭合 | `18` | [0x0413.md](client-to-server/0x0413.md) |
| `0x0414` | `LEVEL_DECISION` | 布局已闭合 | `20` | [0x0414.md](client-to-server/0x0414.md) |
| `0x0415` | `COMPOUND_FOR_DIAMETRICALLY_ITEM` | 布局已闭合 | `19` | [0x0415.md](client-to-server/0x0415.md) |
| `0x0416` | `UNIQUE_ADVANCED_BIND_SHPERE` | 布局已闭合 | `62` | [0x0416.md](client-to-server/0x0416.md) |
| `0x0418` | `DOBLIN_FROM_STAR` | 布局已闭合 | `27` | [0x0418.md](client-to-server/0x0418.md) |
| `0x041D` | `EXCHANGE_CERA_POINT` | 布局已闭合 | `18` | [0x041D.md](client-to-server/0x041D.md) |
| `0x0421` | `PHOENIX_WEAPON_EVENT_REWARD` | 布局已闭合 | `18` | [0x0421.md](client-to-server/0x0421.md) |
| `0x0422` | `PHOENIX_WEAPON_EVENT_REWARD_MAIL_CHECK_CHARAC` | 布局已闭合 | `47` | [0x0422.md](client-to-server/0x0422.md) |
| `0x0423` | `PHOENIX_WEAPON_EVENT_REWARD_MAIL` | 布局已闭合 | `47` | [0x0423.md](client-to-server/0x0423.md) |
| `0x0424` | `PHOENIX_WEAPON_EVENT_CHANGE_PERIOD` | 布局已闭合 | `22` | [0x0424.md](client-to-server/0x0424.md) |
| `0x0425` | `PHOENIX_WEAPON_EVENT_CHANGE_WEAPON` | 布局已闭合 | `18` | [0x0425.md](client-to-server/0x0425.md) |
| `0x0426` | `FIND_SIGNS_FINAL_REWARD_EVENT` | 布局已闭合 | `0` | [0x0426.md](client-to-server/0x0426.md) |
| `0x0427` | `CELEBRATE_BIG_TRANSITION_EVENT_REWARD` | 布局已闭合 | `19` | [0x0427.md](client-to-server/0x0427.md) |
| `0x0428` | `_6TH_GOLDCOIN_` | 布局已闭合 | `19` | [0x0428.md](client-to-server/0x0428.md) |
| `0x0436` | `PUZZLE_GAME_CHOICE` | 布局已闭合 | `18` | [0x0436.md](client-to-server/0x0436.md) |
| `0x0437` | `PUZZLE_CHANCE_REWARD` | 布局已闭合 | `16` | [0x0437.md](client-to-server/0x0437.md) |
| `0x043F` | `CHARGING_CHARM_ENERGY` | 布局已闭合 | `22` | [0x043F.md](client-to-server/0x043F.md) |
| `0x0440` | `EVENT_QUICK_SLOT_REWARD` | 布局已闭合 | `18` | [0x0440.md](client-to-server/0x0440.md) |
| `0x0441` | `EVENT_TOTAL_ATTENDANCE_CHECK_THISWEEK` | 布局已闭合 | `14` | [0x0441.md](client-to-server/0x0441.md) |
| `0x0442` | `LEGEND_SWORD_INFO` | 布局已闭合 | `28` | [0x0442.md](client-to-server/0x0442.md) |
| `0x0447` | `BIND_PLUS` | 布局已闭合 | `145` | [0x0447.md](client-to-server/0x0447.md) |
| `0x0448` | `CARD_COMPOUND` | 结构未知 | `—` | [0x0448.md](client-to-server/0x0448.md) |
| `0x0449` | `DONATE_FOR_ECO` | 布局已闭合 | `34` | [0x0449.md](client-to-server/0x0449.md) |
| `0x044A` | `EVENT_ITEM_REQUEST` | 布局已闭合 | `0` | [0x044A.md](client-to-server/0x044A.md) |
| `0x0450` | `USE_RANDOMBOX_ITEM_EXPAND` | 布局部分可得 | `≥ 18` | [0x0450.md](client-to-server/0x0450.md) |
| `0x0451` | `BURNING_UP_TO_SPECIFIC_LEVEL` | 布局已闭合 | `18` | [0x0451.md](client-to-server/0x0451.md) |
| `0x0453` | `INCREASE_CHANCE_LOTTERY_RESET` | 布局已闭合 | `22` | [0x0453.md](client-to-server/0x0453.md) |
| `0x0454` | `GOAL_ATTAINMENT` | 布局已闭合 | `22` | [0x0454.md](client-to-server/0x0454.md) |
| `0x0455` | `CHRONICLE_DISJOINT` | 布局已闭合 | `19` | [0x0455.md](client-to-server/0x0455.md) |
| `0x0456` | `MAHJONG_STARTGAME` | 布局已闭合 | `15` | [0x0456.md](client-to-server/0x0456.md) |
| `0x0457` | `MAHJONG_GET` | 布局已闭合 | `15` | [0x0457.md](client-to-server/0x0457.md) |
| `0x0458` | `MAHJONG_DROP` | 布局已闭合 | `15` | [0x0458.md](client-to-server/0x0458.md) |
| `0x045C` | `FRAMELAG_PER_DUNGEON` | 布局已闭合（含计数重复组） | `4 + n×34` | [0x045C.md](client-to-server/0x045C.md) |
| `0x0474` | `GOD_GROWTH_SUPPORT` | 布局已闭合 | `18` | [0x0474.md](client-to-server/0x0474.md) |
| `0x0475` | `KARON_LETHE_FREE` | 布局已闭合 | `18` | [0x0475.md](client-to-server/0x0475.md) |
| `0x0476` | `COMMON_BUFF_VENDING_MACHINE_CHN` | 布局已闭合 | `18` | [0x0476.md](client-to-server/0x0476.md) |
| `0x0477` | `ALL_USER_GROWUP` | 布局已闭合 | `22` | [0x0477.md](client-to-server/0x0477.md) |
| `0x048D` | `ABOUT_HOPE` | 布局已闭合 | `18` | [0x048D.md](client-to-server/0x048D.md) |
| `0x048E` | `MYSTERIOUS_GRACE` | 布局已闭合 | `22` | [0x048E.md](client-to-server/0x048E.md) |
| `0x048F` | `LEAVE_IN_NATIONALDAY` | 布局已闭合 | `0` | [0x048F.md](client-to-server/0x048F.md) |
| `0x0490` | `NATIONAL_DAY_2015` | 布局已闭合 | `0` | [0x0490.md](client-to-server/0x0490.md) |
| `0x0491` | `EVENT_PVP_ACCOUNT` | 布局已闭合 | `18` | [0x0491.md](client-to-server/0x0491.md) |
| `0x0492` | `CLEAR_COMBO_WITH_PROLEAGUE` | 布局已闭合 | `15` | [0x0492.md](client-to-server/0x0492.md) |
| `0x0498` | `HIT_HUGE_PUMPKIN_INFO` | 布局已闭合 | `0` | [0x0498.md](client-to-server/0x0498.md) |
| `0x0499` | `HIT_HUGE_PUMPKIN_USE_AX` | 布局已闭合 | `18` | [0x0499.md](client-to-server/0x0499.md) |
| `0x049A` | `HOT_DEAL` | 布局已闭合 | `22` | [0x049A.md](client-to-server/0x049A.md) |
| `0x04A2` | `TECH_INDEX_IMG_RESOURCE_OPTIMIZE_UNIV` | 布局部分可得 | `≥ 2 + n×4` | [0x04A2.md](client-to-server/0x04A2.md) |
| `0x04AB` | `TERRITORY_COMBAT_CONDITION` | 布局已闭合 | `1` | [0x04AB.md](client-to-server/0x04AB.md) |
| `0x04AE` | `EVERYDAY_BOSS_TOWER` | 布局已闭合 | `18` | [0x04AE.md](client-to-server/0x04AE.md) |
| `0x04B0` | `FISHING_EVENT_ATTENDANCE` | 布局已闭合 | `18` | [0x04B0.md](client-to-server/0x04B0.md) |
| `0x04B2` | `GIFT_OF_SERIA` | 布局已闭合 | `14` | [0x04B2.md](client-to-server/0x04B2.md) |
| `0x04B4` | `YUNDY_EVENT` | 布局部分可得 | `≥ 13` | [0x04B4.md](client-to-server/0x04B4.md) |
| `0x04BC` | `CARD_GAME` | 布局已闭合 | `50` | [0x04BC.md](client-to-server/0x04BC.md) |
| `0x04BD` | `CARD_GAME_COMPOUND` | 布局已闭合 | `22` | [0x04BD.md](client-to-server/0x04BD.md) |
| `0x04BE` | `GUILD_MODE_CHANGE_CHN` | 布局已闭合 | `15` | [0x04BE.md](client-to-server/0x04BE.md) |
| `0x04C2` | `REQUEST_LAUNDRY` | 布局已闭合 | `19` | [0x04C2.md](client-to-server/0x04C2.md) |
| `0x04C3` | `FRIENDSHIP_HELL_PARTY_SELECT` | 布局已闭合 | `18` | [0x04C3.md](client-to-server/0x04C3.md) |
| `0x04C4` | `CONTRACT_OF_GUILD` | 布局已闭合 | `18` | [0x04C4.md](client-to-server/0x04C4.md) |
| `0x04C5` | `GROW_BEANSTALK_NPC` | 布局已闭合 | `18` | [0x04C5.md](client-to-server/0x04C5.md) |
| `0x04C6` | `NPC_GROW_UP_USE_ITEM` | 布局已闭合 | `18` | [0x04C6.md](client-to-server/0x04C6.md) |
| `0x04C7` | `P1_TOURNAMENT` | 布局已闭合 | `18` | [0x04C7.md](client-to-server/0x04C7.md) |
| `0x04CB` | `CHOICE_ROULETTE` | 布局已闭合 | `15` | [0x04CB.md](client-to-server/0x04CB.md) |
| `0x04CC` | `NEWBIE_AND_RETURENEE_BONUS_REWARD` | 布局已闭合 | `0` | [0x04CC.md](client-to-server/0x04CC.md) |
| `0x04D3` | `APPOINTED_DUNGEON_CLEAR` | 布局已闭合 | `18` | [0x04D3.md](client-to-server/0x04D3.md) |
| `0x04D4` | `REAL_ESTATE_USE_SHIELD_ITEM` | 布局已闭合 | `0` | [0x04D4.md](client-to-server/0x04D4.md) |
| `0x04D5` | `GUESS_NUMBER` | 布局已闭合 | `22` | [0x04D5.md](client-to-server/0x04D5.md) |
| `0x04D6` | `FIRST_LOGIN_REWARD_POPUP` | 布局已闭合 | `18` | [0x04D6.md](client-to-server/0x04D6.md) |
| `0x04D7` | `LOG_CONNECT_PROCESS` | 布局已闭合 | `22` | [0x04D7.md](client-to-server/0x04D7.md) |
| `0x04D9` | `LUCKY_BALLOON` | 布局已闭合 | `22` | [0x04D9.md](client-to-server/0x04D9.md) |
| `0x04DA` | `FOOD_FIGHTER_DUNGEON` | 布局已闭合 | `22` | [0x04DA.md](client-to-server/0x04DA.md) |
| `0x04DD` | `CHECK_USER_CONNECTION` | **手写记录** | 见文档 | [0x04DD.md](client-to-server/0x04DD.md) **(手写)** |
| `0x04DF` | `SNORKELING_INFO` | 布局已闭合 | `18` | [0x04DF.md](client-to-server/0x04DF.md) |
| `0x04E0` | `RAVEN_BRIDGE` | 布局已闭合 | `18` | [0x04E0.md](client-to-server/0x04E0.md) |
| `0x04E6` | `JOUST_INFO` | 布局已闭合 | `14` | [0x04E6.md](client-to-server/0x04E6.md) |
| `0x04E7` | `JOUST_BETTING` | 布局已闭合 | `23` | [0x04E7.md](client-to-server/0x04E7.md) |
| `0x04E8` | `JOUST_MATCH_HISTORY` | 布局已闭合 | `14` | [0x04E8.md](client-to-server/0x04E8.md) |
| `0x04E9` | `AT_DAILY_ATTENDANCE` | 布局已闭合 | `14` | [0x04E9.md](client-to-server/0x04E9.md) |
| `0x04EA` | `AVERAGE_PING_LOG_CHN` | 布局已闭合 | `10` | [0x04EA.md](client-to-server/0x04EA.md) |
| `0x04EB` | `HALLOWEEN_2016_CHN` | 布局已闭合 | `22` | [0x04EB.md](client-to-server/0x04EB.md) |
| `0x04EC` | `GET_GUILD_HONGBAO_LIST` | 布局已闭合 | `0` | [0x04EC.md](client-to-server/0x04EC.md) |
| `0x04ED` | `GET_GUILD_HONGBAO_POINT_LIST` | 布局已闭合 | `0` | [0x04ED.md](client-to-server/0x04ED.md) |
| `0x04EE` | `GET_GUILD_HONGBAO_HISTORY_LIST` | 布局已闭合 | `18` | [0x04EE.md](client-to-server/0x04EE.md) |
| `0x04EF` | `GIVE_GUILD_HONGBAO` | 布局已闭合 | `19` | [0x04EF.md](client-to-server/0x04EF.md) |
| `0x04F0` | `TAKE_GUILD_HONGBAO` | 布局已闭合 | `18` | [0x04F0.md](client-to-server/0x04F0.md) |
| `0x04F1` | `LOG_DOMAIN_CONNECT` | 布局已闭合 | `30` | [0x04F1.md](client-to-server/0x04F1.md) |
| `0x04F2` | `JEWELRY_BATTLE_START` | 布局已闭合 | `18` | [0x04F2.md](client-to-server/0x04F2.md) |
| `0x04F3` | `JEWELRY_BATTLE_JEWELRY_CHECK` | 布局已闭合 | `26` | [0x04F3.md](client-to-server/0x04F3.md) |
| `0x04F6` | `DPROTO_CALLBACK` | 结构未知 | `—` | [0x04F6.md](client-to-server/0x04F6.md) |

## Server → Client（NOTI 族，服务端主动推送）

NOTI 是单向推送通道，客户端从不发送。其 body 没有 CMD 的两字节结果信封。

| Type | 名称 | Body 状态 | 长度规则 | 文档 |
|---|---|---|---|---|
| `0x0000` | `CHECK_CONNECTION` | 结构未知 | `—` | [NOTI-0x0000.md](server-to-client/NOTI-0x0000.md) |
| `0x0001` | `CHANNELINFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0001.md](server-to-client/NOTI-0x0001.md) |
| `0x0002` | `USERINFO` | 布局部分可得；subtype 3 ClientVerified | subtype 3 最小 `165` | [NOTI-0x0002.md](server-to-client/NOTI-0x0002.md) |
| `0x0003` | `USER_STATE` | 布局部分可得 | `≥ 4` | [NOTI-0x0003.md](server-to-client/NOTI-0x0003.md) |
| `0x0004` | `STAMINA` | 布局部分可得 | `≥ 1` | [NOTI-0x0004.md](server-to-client/NOTI-0x0004.md) |
| `0x0005` | `DUNGEON_PERMISSION` | 布局部分可得 | `≥ 2 + n×5` | [NOTI-0x0005.md](server-to-client/NOTI-0x0005.md) |
| `0x0006` | `USER_LEAVE` | 布局部分可得 | `≥ 2` | [NOTI-0x0006.md](server-to-client/NOTI-0x0006.md) |
| `0x0007` | `REQUEST_PEER` | ClientVerified（A21 邀请 UI；真实 body 待归档） | `13` | [NOTI-0x0007.md](server-to-client/NOTI-0x0007.md) |
| `0x0008` | `RESPONSE_PEER` | Implemented（接受链 UI 通过；待逐帧归因） | `7` | [NOTI-0x0008.md](server-to-client/NOTI-0x0008.md) |
| `0x0009` | `PARTY_INFO` | ClientVerified（A21 type-0 refresh、type-1 18B、普通成员 clear；type-1 22B 静态） | baseline `2`；type-0 `65`；type-1 `18/22`；clear `5` | [NOTI-0x0009.md](server-to-client/NOTI-0x0009.md) |
| `0x000A` | `WALKOUT` | 布局部分可得 | `≥ 2` | [NOTI-0x000A.md](server-to-client/NOTI-0x000A.md) |
| `0x000B` | `USER_UDP_IP_PORT` | Implemented（双端已上报建队 UI 通过；待逐帧归因/P2P） | `1 + n×22` | [NOTI-0x000B.md](server-to-client/NOTI-0x000B.md) |
| `0x000C` | `MESSAGE` | 布局部分可得 | `≥ 4` | [NOTI-0x000C.md](server-to-client/NOTI-0x000C.md) |
| `0x000D` | `ITEM_LIST` | 布局部分可得 | `≥ 12` | [NOTI-0x000D.md](server-to-client/NOTI-0x000D.md) |
| `0x000E` | `UPDATE_ITEM_LIST` | 布局部分可得 | `≥ 104` | [NOTI-0x000E.md](server-to-client/NOTI-0x000E.md) |
| `0x000F` | `CHANGE_ITEMTRADE_ITEM` | 布局部分可得 | `≥ 101` | [NOTI-0x000F.md](server-to-client/NOTI-0x000F.md) |
| `0x0010` | `CANCEL_ITEMTRADE` | 布局部分可得 | `≥ 7` | [NOTI-0x0010.md](server-to-client/NOTI-0x0010.md) |
| `0x0011` | `STATE_ITEMTRADE` | 布局部分可得 | `≥ 3` | [NOTI-0x0011.md](server-to-client/NOTI-0x0011.md) |
| `0x0012` | `FINISH_ITEMTRADE` | 布局部分可得 | `≥ 6` | [NOTI-0x0012.md](server-to-client/NOTI-0x0012.md) |
| `0x0013` | `SKILLINFO` | 布局部分可得 | `≥ 13` | [NOTI-0x0013.md](server-to-client/NOTI-0x0013.md) |
| `0x0015` | `ACCEPTABLE_QUEST_LIST` | 布局部分可得 | `≥ 3 + n×2` | [NOTI-0x0015.md](server-to-client/NOTI-0x0015.md) |
| `0x0016` | `USER_POSITION` | 布局部分可得 | `≥ 9` | [NOTI-0x0016.md](server-to-client/NOTI-0x0016.md) |
| `0x0017` | `USER_AREA` | 布局部分可得 | `≥ 10` | [NOTI-0x0017.md](server-to-client/NOTI-0x0017.md) |
| `0x0018` | `AREA_USERS` | ClientVerified（A21 gate/normal audience） | `≥ 12` | [NOTI-0x0018.md](server-to-client/NOTI-0x0018.md) |
| `0x0019` | `FISHING` | 结构未知 | `—` | [NOTI-0x0019.md](server-to-client/NOTI-0x0019.md) |
| `0x001A` | `UDP_HOST` | 布局部分可得 | `≥ 1` | [NOTI-0x001A.md](server-to-client/NOTI-0x001A.md) |
| `0x001B` | `ENTER_SELECT_DUNGEON` | 布局部分可得 | `≥ 4` | [NOTI-0x001B.md](server-to-client/NOTI-0x001B.md) |
| `0x001C` | `DUNGEON_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x001C.md](server-to-client/NOTI-0x001C.md) |
| `0x001D` | `START_MAP` | 布局部分可得 | `≥ 14` | [NOTI-0x001D.md](server-to-client/NOTI-0x001D.md) |
| `0x001E` | `FINISH_LOADING` | 布局部分可得 | `≥ 5` | [NOTI-0x001E.md](server-to-client/NOTI-0x001E.md) |
| `0x001F` | `ENABLE_CLEAR_DUNGEON` | 结构未知 | `—` | [NOTI-0x001F.md](server-to-client/NOTI-0x001F.md) |
| `0x0020` | `DIE_STATE` | 布局部分可得 | `≥ 4` | [NOTI-0x0020.md](server-to-client/NOTI-0x0020.md) |
| `0x0021` | `FAIL_CLEAR_DUNGEON` | 布局部分可得 | `≥ 1` | [NOTI-0x0021.md](server-to-client/NOTI-0x0021.md) |
| `0x0022` | `PLAY_RESULT` | 布局部分可得 | `≥ 16` | [NOTI-0x0022.md](server-to-client/NOTI-0x0022.md) |
| `0x0023` | `CLEAR_DUNGEON_REWARD` | 布局部分可得 | `≥ 127` | [NOTI-0x0023.md](server-to-client/NOTI-0x0023.md) |
| `0x0024` | `FATIGUE` | 布局部分可得 | `≥ 8` | [NOTI-0x0024.md](server-to-client/NOTI-0x0024.md) |
| `0x0025` | `EXP` | 布局部分可得 | `≥ 83 + n×8` | [NOTI-0x0025.md](server-to-client/NOTI-0x0025.md) |
| `0x0026` | `DIE_MONSTER` | 布局部分可得 | `≥ 3` | [NOTI-0x0026.md](server-to-client/NOTI-0x0026.md) |
| `0x0027` | `GET_ITEM` | 布局部分可得 | `≥ 5` | [NOTI-0x0027.md](server-to-client/NOTI-0x0027.md) |
| `0x0028` | `DROP_ITEM` | 布局部分可得 | `≥ 112` | [NOTI-0x0028.md](server-to-client/NOTI-0x0028.md) |
| `0x0029` | `PVP_ROOM_INFO` | 布局部分可得 | `≥ 13` | [NOTI-0x0029.md](server-to-client/NOTI-0x0029.md) |
| `0x002A` | `PVP_ROOM_STATE` | 布局部分可得 | `≥ 11` | [NOTI-0x002A.md](server-to-client/NOTI-0x002A.md) |
| `0x002B` | `PVP_SEAT_STATE` | 布局部分可得 | `≥ 8` | [NOTI-0x002B.md](server-to-client/NOTI-0x002B.md) |
| `0x002C` | `PVP_READY_STATE` | 布局部分可得 | `≥ 2` | [NOTI-0x002C.md](server-to-client/NOTI-0x002C.md) |
| `0x002D` | `START_PVP` | 布局部分可得 | `≥ 2` | [NOTI-0x002D.md](server-to-client/NOTI-0x002D.md) |
| `0x002E` | `DIE_PVP_CHARACTER` | 布局部分可得 | `≥ 2` | [NOTI-0x002E.md](server-to-client/NOTI-0x002E.md) |
| `0x002F` | `END_PVP` | 布局部分可得 | `≥ 11` | [NOTI-0x002F.md](server-to-client/NOTI-0x002F.md) |
| `0x0030` | `PVP_RECORD` | 布局部分可得 | `≥ 38` | [NOTI-0x0030.md](server-to-client/NOTI-0x0030.md) |
| `0x0031` | `REQ_PVP_RANK` | 结构未知 | `—` | [NOTI-0x0031.md](server-to-client/NOTI-0x0031.md) |
| `0x0032` | `KEY_COUNT` | 布局部分可得 | `≥ 1` | [NOTI-0x0032.md](server-to-client/NOTI-0x0032.md) |
| `0x0033` | `MONEY_FULL` | 布局部分可得 | `≥ 9` | [NOTI-0x0033.md](server-to-client/NOTI-0x0033.md) |
| `0x0034` | `FRIEND_INFO` | 结构未知 | `—` | [NOTI-0x0034.md](server-to-client/NOTI-0x0034.md) |
| `0x0035` | `CERA` | 布局部分可得 | `≥ 9` | [NOTI-0x0035.md](server-to-client/NOTI-0x0035.md) |
| `0x0036` | `BUDDY_IN` | 布局部分可得 | `≥ 6` | [NOTI-0x0036.md](server-to-client/NOTI-0x0036.md) |
| `0x0037` | `BUDDY_OUT` | 布局部分可得 | `≥ 6` | [NOTI-0x0037.md](server-to-client/NOTI-0x0037.md) |
| `0x0038` | `BUDDY_CONN_LIST` | 结构未知 | `—` | [NOTI-0x0038.md](server-to-client/NOTI-0x0038.md) |
| `0x0039` | `PVPEXP_OF_WEEK` | 布局已闭合 | `4` | [NOTI-0x0039.md](server-to-client/NOTI-0x0039.md) |
| `0x003A` | `GUILD_ENTER` | 布局部分可得 | `≥ 8` | [NOTI-0x003A.md](server-to-client/NOTI-0x003A.md) |
| `0x003B` | `GUILD_SECEDE` | 布局部分可得 | `≥ 9` | [NOTI-0x003B.md](server-to-client/NOTI-0x003B.md) |
| `0x003C` | `GUILD_MARK_CHANGE` | 布局部分可得 | `≥ 4` | [NOTI-0x003C.md](server-to-client/NOTI-0x003C.md) |
| `0x003D` | `GUILD_DISMISS` | 结构未知 | `—` | [NOTI-0x003D.md](server-to-client/NOTI-0x003D.md) |
| `0x003E` | `GUILD_CHAT_MSG` | 结构未知 | `—` | [NOTI-0x003E.md](server-to-client/NOTI-0x003E.md) |
| `0x003F` | `GUILD_CHAR_NAMES` | 结构未知 | `—` | [NOTI-0x003F.md](server-to-client/NOTI-0x003F.md) |
| `0x0040` | `GUILD_MEMBER_LOGIN_OUT` | 布局部分可得 | `≥ 2` | [NOTI-0x0040.md](server-to-client/NOTI-0x0040.md) |
| `0x0041` | `MESSAGE_OTHER_CHANNEL` | 布局部分可得 | `≥ 2` | [NOTI-0x0041.md](server-to-client/NOTI-0x0041.md) |
| `0x0042` | `CERA_SPECIALITEM` | 布局部分可得 | `≥ 2` | [NOTI-0x0042.md](server-to-client/NOTI-0x0042.md) |
| `0x0043` | `CARD_SELECT_COMPLETE_STATE` | 结构未知 | `—` | [NOTI-0x0043.md](server-to-client/NOTI-0x0043.md) |
| `0x0044` | `CERA_GROUP_HISTORY` | 布局部分可得 | `≥ 3 + n×4` | [NOTI-0x0044.md](server-to-client/NOTI-0x0044.md) |
| `0x0045` | `SERVEREVENT` | 布局部分可得 | `≥ 5` | [NOTI-0x0045.md](server-to-client/NOTI-0x0045.md) |
| `0x0046` | `GUILD_INFO` | 布局部分可得 | `≥ 10` | [NOTI-0x0046.md](server-to-client/NOTI-0x0046.md) |
| `0x0047` | `GUILD_CREATE` | 布局部分可得 | `≥ 4` | [NOTI-0x0047.md](server-to-client/NOTI-0x0047.md) |
| `0x004E` | `CREATE_PRIVATE_STORE` | 布局部分可得 | `≥ 9` | [NOTI-0x004E.md](server-to-client/NOTI-0x004E.md) |
| `0x004F` | `REMOVE_PRIVATE_STORE` | 布局部分可得 | `≥ 2` | [NOTI-0x004F.md](server-to-client/NOTI-0x004F.md) |
| `0x0050` | `FROCE_REMOVE_PRIVATE_STORE` | 结构未知 | `—` | [NOTI-0x0050.md](server-to-client/NOTI-0x0050.md) |
| `0x0051` | `COMPLETE_DISPLAY` | 布局部分可得 | `≥ 2` | [NOTI-0x0051.md](server-to-client/NOTI-0x0051.md) |
| `0x0052` | `BUY_PRIVATE_STORE_ITEM` | 布局部分可得 | `≥ 24` | [NOTI-0x0052.md](server-to-client/NOTI-0x0052.md) |
| `0x0053` | `PRIVATE_STORE_ITEM` | 布局部分可得 | `≥ 6` | [NOTI-0x0053.md](server-to-client/NOTI-0x0053.md) |
| `0x0054` | `PRIVATE_STORE_LIST` | 布局部分可得 | `≥ 1 + n×10` | [NOTI-0x0054.md](server-to-client/NOTI-0x0054.md) |
| `0x0055` | `PRIVATE_STORE_INFO` | 布局部分可得 | `≥ 2 + n1×50 + n2×4 + n3×3` | [NOTI-0x0055.md](server-to-client/NOTI-0x0055.md) |
| `0x0056` | `SERVER_BROADCAST` | 布局部分可得 | `≥ 1` | [NOTI-0x0056.md](server-to-client/NOTI-0x0056.md) |
| `0x0057` | `WARROOM_INFO` | 布局部分可得 | `≥ 12` | [NOTI-0x0057.md](server-to-client/NOTI-0x0057.md) |
| `0x0058` | `WARROOM_STATE` | 布局部分可得 | `≥ 7` | [NOTI-0x0058.md](server-to-client/NOTI-0x0058.md) |
| `0x0059` | `WARROOM_SEAT_STATE` | 布局部分可得 | `≥ 7` | [NOTI-0x0059.md](server-to-client/NOTI-0x0059.md) |
| `0x005A` | `START_WARROOM` | 结构未知 | `—` | [NOTI-0x005A.md](server-to-client/NOTI-0x005A.md) |
| `0x005B` | `WARROOM_MONSTER_SPAWN` | 布局部分可得 | `≥ 3 + n×8` | [NOTI-0x005B.md](server-to-client/NOTI-0x005B.md) |
| `0x005C` | `CLEAR_WARAREA_REWARD` | 布局部分可得 | `≥ 34` | [NOTI-0x005C.md](server-to-client/NOTI-0x005C.md) |
| `0x005D` | `WARROOM_GET_ITEM` | 布局部分可得 | `≥ 4` | [NOTI-0x005D.md](server-to-client/NOTI-0x005D.md) |
| `0x005E` | `ADD_KP` | 布局部分可得 | `≥ 3` | [NOTI-0x005E.md](server-to-client/NOTI-0x005E.md) |
| `0x005F` | `WARROOM_DIE_STATE` | 布局部分可得 | `≥ 7` | [NOTI-0x005F.md](server-to-client/NOTI-0x005F.md) |
| `0x0060` | `CLEAR_WARAREA_FP` | 布局部分可得 | `≥ 2 + n1×2 + n2×9` | [NOTI-0x0060.md](server-to-client/NOTI-0x0060.md) |
| `0x0061` | `MAILBOX_MAIL_LIST` | 布局部分可得 | `≥ 104 + n1×4 + n2×3` | [NOTI-0x0061.md](server-to-client/NOTI-0x0061.md) |
| `0x0062` | `MAILBOX_REMOVE_MAIL` | 布局部分可得 | `≥ 4 + n×4` | [NOTI-0x0062.md](server-to-client/NOTI-0x0062.md) |
| `0x0063` | `MAILBOX_ALARM` | 布局部分可得 | `≥ 2` | [NOTI-0x0063.md](server-to-client/NOTI-0x0063.md) |
| `0x0064` | `DIED_CREATURE` | 布局部分可得 | `≥ 2` | [NOTI-0x0064.md](server-to-client/NOTI-0x0064.md) |
| `0x0065` | `RENAME_CREATURE` | 布局部分可得 | `≥ 2` | [NOTI-0x0065.md](server-to-client/NOTI-0x0065.md) |
| `0x0066` | `GAIN_EXP_CREATURE` | 布局部分可得 | `≥ 8` | [NOTI-0x0066.md](server-to-client/NOTI-0x0066.md) |
| `0x0067` | `CREATURE_STATE` | 布局部分可得 | `≥ 8` | [NOTI-0x0067.md](server-to-client/NOTI-0x0067.md) |
| `0x0068` | `RESPONSE_CREATURE` | 布局部分可得 | `≥ 2` | [NOTI-0x0068.md](server-to-client/NOTI-0x0068.md) |
| `0x0069` | `CREATURE_ITEM_LIST` | 布局部分可得 | `≥ 1 + n×14` | [NOTI-0x0069.md](server-to-client/NOTI-0x0069.md) |
| `0x006A` | `EVOLUTE_CREATURE` | 布局部分可得 | `≥ 4` | [NOTI-0x006A.md](server-to-client/NOTI-0x006A.md) |
| `0x006B` | `REVIVAL_CREATURE` | 布局部分可得 | `≥ 2` | [NOTI-0x006B.md](server-to-client/NOTI-0x006B.md) |
| `0x006C` | `EVENT_INFO` | 布局部分可得 | `≥ 56 + n×2` | [NOTI-0x006C.md](server-to-client/NOTI-0x006C.md) |
| `0x006D` | `GUILD_WAR_END` | 布局部分可得 | `≥ 1` | [NOTI-0x006D.md](server-to-client/NOTI-0x006D.md) |
| `0x006E` | `GUILD_WAR_RESULT` | 布局部分可得 | `≥ 1` | [NOTI-0x006E.md](server-to-client/NOTI-0x006E.md) |
| `0x006F` | `CODE_CHECKSUM_RANGE` | 结构未知 | `—` | [NOTI-0x006F.md](server-to-client/NOTI-0x006F.md) |
| `0x0070` | `PVP_TURN_PLAYER` | 布局部分可得 | `≥ 1 + n×5` | [NOTI-0x0070.md](server-to-client/NOTI-0x0070.md) |
| `0x0071` | `PVP_REQUEST_FIGHT` | 布局部分可得 | `≥ 1` | [NOTI-0x0071.md](server-to-client/NOTI-0x0071.md) |
| `0x0072` | `CREATURE_MESSAGE` | 布局部分可得 | `≥ 4` | [NOTI-0x0072.md](server-to-client/NOTI-0x0072.md) |
| `0x0073` | `BOSS_DIE_CHECK` | 布局部分可得 | `≥ 4` | [NOTI-0x0073.md](server-to-client/NOTI-0x0073.md) |
| `0x0074` | `SEAL_DOOR_ITEMS` | 结构未知 | `—` | [NOTI-0x0074.md](server-to-client/NOTI-0x0074.md) |
| `0x0075` | `GUILD_WAR_START` | 结构未知 | `—` | [NOTI-0x0075.md](server-to-client/NOTI-0x0075.md) |
| `0x0076` | `MEGAPHONE_MSG` | 布局部分可得 | `≥ 6` | [NOTI-0x0076.md](server-to-client/NOTI-0x0076.md) |
| `0x0077` | `CREATURE_SCRIPT_MESSAGE` | 布局部分可得 | `≥ 4` | [NOTI-0x0077.md](server-to-client/NOTI-0x0077.md) |
| `0x0078` | `PVP_REVIVE` | 布局部分可得 | `≥ 2` | [NOTI-0x0078.md](server-to-client/NOTI-0x0078.md) |
| `0x0079` | `TIME_CHECK` | 结构未知 | `—` | [NOTI-0x0079.md](server-to-client/NOTI-0x0079.md) |
| `0x007A` | `SP_RETURN_INFO` | 布局部分可得 | `≥ 23` | [NOTI-0x007A.md](server-to-client/NOTI-0x007A.md) |
| `0x007C` | `ENTER_GAMEWORLD_COMPLETE` | 结构未知 | `—` | [NOTI-0x007C.md](server-to-client/NOTI-0x007C.md) |
| `0x007D` | `REQ_HUMAN_CERTIFY` | 布局部分可得 | `≥ 8` | [NOTI-0x007D.md](server-to-client/NOTI-0x007D.md) |
| `0x007E` | `GM_REQUEST_MID` | 布局部分可得 | `≥ 4` | [NOTI-0x007E.md](server-to-client/NOTI-0x007E.md) |
| `0x007F` | `GUILD_MEMBER_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x007F.md](server-to-client/NOTI-0x007F.md) |
| `0x0080` | `MOVE_ASSAULT_PLACE` | 布局部分可得 | `≥ 13 + n1×4 + n2×2` | [NOTI-0x0080.md](server-to-client/NOTI-0x0080.md) |
| `0x0081` | `COMPLETE_LOAD_ASSAULT` | 布局部分可得 | `≥ 2` | [NOTI-0x0081.md](server-to-client/NOTI-0x0081.md) |
| `0x0082` | `START_ASSAULT` | 结构未知 | `—` | [NOTI-0x0082.md](server-to-client/NOTI-0x0082.md) |
| `0x0083` | `DIE_ASSAULT_PLAYER` | 布局部分可得 | `≥ 2` | [NOTI-0x0083.md](server-to-client/NOTI-0x0083.md) |
| `0x0084` | `END_ASSAULT` | 布局部分可得 | `≥ 4` | [NOTI-0x0084.md](server-to-client/NOTI-0x0084.md) |
| `0x0085` | `REVIVAL_ASSAULT_PLAYER` | 布局部分可得 | `≥ 2` | [NOTI-0x0085.md](server-to-client/NOTI-0x0085.md) |
| `0x0086` | `VIEW_ASSAULT_ICON` | 布局部分可得 | `≥ 13 + n×3` | [NOTI-0x0086.md](server-to-client/NOTI-0x0086.md) |
| `0x0087` | `LEAVE_ASSAULT_PLACE` | 布局部分可得 | `≥ 2` | [NOTI-0x0087.md](server-to-client/NOTI-0x0087.md) |
| `0x0088` | `CHANGE_HP` | 布局部分可得 | `≥ 6` | [NOTI-0x0088.md](server-to-client/NOTI-0x0088.md) |
| `0x0089` | `CLOSE_ASSAULT` | 布局部分可得 | `≥ 1` | [NOTI-0x0089.md](server-to-client/NOTI-0x0089.md) |
| `0x008A` | `TELEPORT` | 布局部分可得 | `≥ 7` | [NOTI-0x008A.md](server-to-client/NOTI-0x008A.md) |
| `0x008B` | `GIVE_ALARM` | 结构未知 | `—` | [NOTI-0x008B.md](server-to-client/NOTI-0x008B.md) |
| `0x008C` | `CHANGE_GUILD_MEMBER_GRADE` | 布局部分可得 | `≥ 1` | [NOTI-0x008C.md](server-to-client/NOTI-0x008C.md) |
| `0x008D` | `GUILD_MESSAGE_TO_GUILD_MEMBER` | 结构未知 | `—` | [NOTI-0x008D.md](server-to-client/NOTI-0x008D.md) |
| `0x008E` | `DEATH_TOWER_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x008E.md](server-to-client/NOTI-0x008E.md) |
| `0x008F` | `START_DEATH_TOWER_MAP` | 布局部分可得 | `≥ 25` | [NOTI-0x008F.md](server-to-client/NOTI-0x008F.md) |
| `0x0090` | `DEATH_TOWER_STATE_RANKING` | 布局部分可得 | `≥ 27 + n×12` | [NOTI-0x0090.md](server-to-client/NOTI-0x0090.md) |
| `0x0091` | `DEATH_TOWER_STATE_REWARD` | 布局部分可得 | `≥ 5 + n×8` | [NOTI-0x0091.md](server-to-client/NOTI-0x0091.md) |
| `0x0092` | `DEATH_TOWER_STATE_EPLP` | 布局部分可得 | `≥ 1` | [NOTI-0x0092.md](server-to-client/NOTI-0x0092.md) |
| `0x0093` | `CALL_GUILD_INVITE_TO_INVITED` | 布局部分可得 | `≥ 8` | [NOTI-0x0093.md](server-to-client/NOTI-0x0093.md) |
| `0x0094` | `REPLY_GUILD_INVITE_TO_CALLER` | 布局部分可得 | `≥ 1` | [NOTI-0x0094.md](server-to-client/NOTI-0x0094.md) |
| `0x0095` | `GUILD_MASTER_DELEGATE` | 结构未知 | `—` | [NOTI-0x0095.md](server-to-client/NOTI-0x0095.md) |
| `0x0096` | `HAS_BEEN_GUILD_MASTER` | 结构未知 | `—` | [NOTI-0x0096.md](server-to-client/NOTI-0x0096.md) |
| `0x0097` | `GUILD_SECEDE_TO_USER` | 结构未知 | `—` | [NOTI-0x0097.md](server-to-client/NOTI-0x0097.md) |
| `0x0098` | `SECURITY_CARD_AUTH_REQ` | 布局部分可得 | `≥ 5` | [NOTI-0x0098.md](server-to-client/NOTI-0x0098.md) |
| `0x0099` | `PARTY_MEMBER_REALTIME_INFO` | ClientVerified（A21 双端初始 HP） | `1 + n×5` | [NOTI-0x0099.md](server-to-client/NOTI-0x0099.md) |
| `0x009A` | `NEW_FD` | 结构未知 | `—` | [NOTI-0x009A.md](server-to-client/NOTI-0x009A.md) |
| `0x009B` | `TOTAL_POWER_WAR_POINT` | 布局部分可得 | `≥ 8` | [NOTI-0x009B.md](server-to-client/NOTI-0x009B.md) |
| `0x009F` | `POWER_WAR_START` | 结构未知 | `—` | [NOTI-0x009F.md](server-to-client/NOTI-0x009F.md) |
| `0x00A0` | `POWER_WAR_END` | 布局部分可得 | `≥ 5` | [NOTI-0x00A0.md](server-to-client/NOTI-0x00A0.md) |
| `0x00A1` | `POWER_WAR_START_TIME` | 布局已闭合 | `4` | [NOTI-0x00A1.md](server-to-client/NOTI-0x00A1.md) |
| `0x00A2` | `POWER_WAR_END_TIME` | 布局部分可得 | `≥ 9` | [NOTI-0x00A2.md](server-to-client/NOTI-0x00A2.md) |
| `0x00A3` | `POWER_WAR_PROLONG` | 布局部分可得 | `≥ 4` | [NOTI-0x00A3.md](server-to-client/NOTI-0x00A3.md) |
| `0x00A4` | `SET_CHARAC_UNLIMIT_CHALLENGE` | 布局部分可得 | `≥ 1` | [NOTI-0x00A4.md](server-to-client/NOTI-0x00A4.md) |
| `0x00A5` | `AA_STARTINFO` | 布局部分可得 | `≥ 2` | [NOTI-0x00A5.md](server-to-client/NOTI-0x00A5.md) |
| `0x00A6` | `AA_TIME` | 布局部分可得 | `≥ 1` | [NOTI-0x00A6.md](server-to-client/NOTI-0x00A6.md) |
| `0x00A7` | `MANGLE_HEADER` | 结构未知 | `—` | [NOTI-0x00A7.md](server-to-client/NOTI-0x00A7.md) |
| `0x00A8` | `HAS_NO_GO_WITH_APC_QUEST_IN_PARTY` | 布局部分可得 | `≥ 2` | [NOTI-0x00A8.md](server-to-client/NOTI-0x00A8.md) |
| `0x00A9` | `AUCTION_ALARM` | 布局部分可得 | `≥ 15 + n×3` | [NOTI-0x00A9.md](server-to-client/NOTI-0x00A9.md) |
| `0x00AA` | `DUNGEON_EVENT_STORY_PAUSE` | 布局部分可得 | `≥ 4` | [NOTI-0x00AA.md](server-to-client/NOTI-0x00AA.md) |
| `0x00AB` | `MOUSE_REGIST_DISCARD` | 结构未知 | `—` | [NOTI-0x00AB.md](server-to-client/NOTI-0x00AB.md) |
| `0x00AC` | `LOAD_COOLTIME_ITEM_INFO` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x00AC.md](server-to-client/NOTI-0x00AC.md) |
| `0x00AD` | `GAME_OPTION` | 结构未知 | `—` | [NOTI-0x00AD.md](server-to-client/NOTI-0x00AD.md) |
| `0x00AE` | `LOAD_EFFECT_ITEM_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x00AE.md](server-to-client/NOTI-0x00AE.md) |
| `0x00AF` | `MATCH_STATE` | 布局部分可得 | `≥ 1` | [NOTI-0x00AF.md](server-to-client/NOTI-0x00AF.md) |
| `0x00B0` | `SECURE_INPUT_MSG_TRADING` | 布局部分可得 | `≥ 1` | [NOTI-0x00B0.md](server-to-client/NOTI-0x00B0.md) |
| `0x00B1` | `SHOP_COIN_EVENT` | 布局部分可得 | `≥ 1` | [NOTI-0x00B1.md](server-to-client/NOTI-0x00B1.md) |
| `0x00B2` | `USE_RANDOMBOX_ITEM` | 结构未知 | `—` | [NOTI-0x00B2.md](server-to-client/NOTI-0x00B2.md) |
| `0x00B3` | `SET_DURABILITY` | 布局部分可得 | `≥ 4` | [NOTI-0x00B3.md](server-to-client/NOTI-0x00B3.md) |
| `0x00B4` | `ONE_DAY_LETHE_INIT` | 布局部分可得 | `≥ 2` | [NOTI-0x00B4.md](server-to-client/NOTI-0x00B4.md) |
| `0x00B5` | `GLOBAL_EFFECT_INFO` | 布局部分可得 | `≥ 18` | [NOTI-0x00B5.md](server-to-client/NOTI-0x00B5.md) |
| `0x00B6` | `POWER_WAR_REWARD_CHARACTER_RANKING` | 布局部分可得 | `≥ 8 + n×4` | [NOTI-0x00B6.md](server-to-client/NOTI-0x00B6.md) |
| `0x00B7` | `AUCTION_NOTIFY_AUCTION_SERVICE` | 布局部分可得 | `≥ 2` | [NOTI-0x00B7.md](server-to-client/NOTI-0x00B7.md) |
| `0x00B8` | `MINIINFO_DUNGEON_TO_VILLAGE` | 结构未知 | `—` | [NOTI-0x00B8.md](server-to-client/NOTI-0x00B8.md) |
| `0x00B9` | `MINIINFO_GUILD_INFO` | 结构未知 | `—` | [NOTI-0x00B9.md](server-to-client/NOTI-0x00B9.md) |
| `0x00BA` | `PCROOM_PLAYER_LOGIN_LOGOUT` | 布局部分可得 | `≥ 2` | [NOTI-0x00BA.md](server-to-client/NOTI-0x00BA.md) |
| `0x00BB` | `SERVER_EVENT_PHASE` | 布局已闭合 | `3` | [NOTI-0x00BB.md](server-to-client/NOTI-0x00BB.md) |
| `0x00BC` | `POWER_WAR_WINNER_SIDE` | 布局部分可得 | `≥ 1` | [NOTI-0x00BC.md](server-to-client/NOTI-0x00BC.md) |
| `0x00BD` | `CREATE_GUILD_AGIT` | 结构未知 | `—` | [NOTI-0x00BD.md](server-to-client/NOTI-0x00BD.md) |
| `0x00BE` | `DELETE_GUILD_AGIT` | 结构未知 | `—` | [NOTI-0x00BE.md](server-to-client/NOTI-0x00BE.md) |
| `0x00BF` | `GUILD_AGIT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x00BF.md](server-to-client/NOTI-0x00BF.md) |
| `0x00C0` | `SERVER_MESSAGE_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x00C0.md](server-to-client/NOTI-0x00C0.md) |
| `0x00C1` | `HACKSHIELD_CLIENT_REQUEST` | 结构未知 | `—` | [NOTI-0x00C1.md](server-to-client/NOTI-0x00C1.md) |
| `0x00C2` | `NPC_FAVOR` | 布局部分可得 | `≥ 2` | [NOTI-0x00C2.md](server-to-client/NOTI-0x00C2.md) |
| `0x00C3` | `NPC_MOOD` | 布局部分可得 | `≥ 2 + n×3` | [NOTI-0x00C3.md](server-to-client/NOTI-0x00C3.md) |
| `0x00C4` | `RETURNEE_COUNT` | 布局部分可得 | `≥ 1` | [NOTI-0x00C4.md](server-to-client/NOTI-0x00C4.md) |
| `0x00C5` | `GUILD_MEMBER_MEMO` | 结构未知 | `—` | [NOTI-0x00C5.md](server-to-client/NOTI-0x00C5.md) |
| `0x00C6` | `MILEAGE` | 结构未知 | `—` | [NOTI-0x00C6.md](server-to-client/NOTI-0x00C6.md) |
| `0x00C7` | `DISCONNECT_REASON` | 布局部分可得 | `≥ 8` | [NOTI-0x00C7.md](server-to-client/NOTI-0x00C7.md) |
| `0x00C8` | `POWER_WAR_POINT_AFTER_ASSAULT` | 布局部分可得 | `≥ 1` | [NOTI-0x00C8.md](server-to-client/NOTI-0x00C8.md) |
| `0x00C9` | `POWER_WAR_PROCESS_INFO` | 布局部分可得 | `≥ 20` | [NOTI-0x00C9.md](server-to-client/NOTI-0x00C9.md) |
| `0x00CA` | `VILLAGE_OBJECT_LIST` | 布局部分可得 | `≥ 2` | [NOTI-0x00CA.md](server-to-client/NOTI-0x00CA.md) |
| `0x00CB` | `REQUEST_DISJOINT_ITEM` | 布局部分可得 | `≥ 9` | [NOTI-0x00CB.md](server-to-client/NOTI-0x00CB.md) |
| `0x00CC` | `NEXON_CASH_EVENT` | 结构未知 | `—` | [NOTI-0x00CC.md](server-to-client/NOTI-0x00CC.md) |
| `0x00CD` | `EXPERT_JOB_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x00CD.md](server-to-client/NOTI-0x00CD.md) |
| `0x00CE` | `CREATE_DISJOINT_STORE` | 布局部分可得 | `≥ 12` | [NOTI-0x00CE.md](server-to-client/NOTI-0x00CE.md) |
| `0x00CF` | `HELLPARTY_VALUE` | 布局部分可得 | `≥ 4` | [NOTI-0x00CF.md](server-to-client/NOTI-0x00CF.md) |
| `0x00D0` | `BLOOD_DUNGEON_STATE_RANKING` | 布局部分可得 | `≥ 24` | [NOTI-0x00D0.md](server-to-client/NOTI-0x00D0.md) |
| `0x00D1` | `BLOOD_DUNGEON_STATE_REWARD` | 布局部分可得 | `≥ 2 + n×9` | [NOTI-0x00D1.md](server-to-client/NOTI-0x00D1.md) |
| `0x00D2` | `BLOOD_MONSTER_SPAWN` | 布局部分可得 | `≥ 4 + n×17` | [NOTI-0x00D2.md](server-to-client/NOTI-0x00D2.md) |
| `0x00D3` | `START_BLOOD_MAP` | 布局部分可得 | `≥ 8` | [NOTI-0x00D3.md](server-to-client/NOTI-0x00D3.md) |
| `0x00D4` | `BLOOD_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x00D4.md](server-to-client/NOTI-0x00D4.md) |
| `0x00D5` | `BLOOD_CRAZY_MONSTER_SPAWN` | 布局部分可得 | `≥ 5` | [NOTI-0x00D5.md](server-to-client/NOTI-0x00D5.md) |
| `0x00D6` | `BLOOD_ROUND_MAX_TIME` | 布局部分可得 | `≥ 10` | [NOTI-0x00D6.md](server-to-client/NOTI-0x00D6.md) |
| `0x00D7` | `BLOOD_ROUND_INTERVAL_TIME` | 布局部分可得 | `≥ 5` | [NOTI-0x00D7.md](server-to-client/NOTI-0x00D7.md) |
| `0x00D8` | `POWER_UP` | 布局部分可得 | `≥ 5` | [NOTI-0x00D8.md](server-to-client/NOTI-0x00D8.md) |
| `0x00D9` | `CLOSE_DISJOINT_STORE` | 布局部分可得 | `≥ 2` | [NOTI-0x00D9.md](server-to-client/NOTI-0x00D9.md) |
| `0x00DA` | `CHANGE_GUILD_NAME_TO_GUILD_MEMBERS` | 结构未知 | `—` | [NOTI-0x00DA.md](server-to-client/NOTI-0x00DA.md) |
| `0x00DB` | `USE_SHARED_EFFECT_ITEM` | 布局部分可得 | `≥ 7` | [NOTI-0x00DB.md](server-to-client/NOTI-0x00DB.md) |
| `0x00DC` | `POWER_WAR_POWER_UP_TIME` | 布局部分可得 | `≥ 4` | [NOTI-0x00DC.md](server-to-client/NOTI-0x00DC.md) |
| `0x00DD` | `POWER_WAR_POWER_UP_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x00DD.md](server-to-client/NOTI-0x00DD.md) |
| `0x00DE` | `PUNISH_END_TIME` | 布局部分可得 | `≥ 15` | [NOTI-0x00DE.md](server-to-client/NOTI-0x00DE.md) |
| `0x00DF` | `POWER_WAR_RESULT` | 布局部分可得 | `≥ 25` | [NOTI-0x00DF.md](server-to-client/NOTI-0x00DF.md) |
| `0x00E0` | `POWER_WAR_STRAIGHT_VICTORIES` | 布局部分可得 | `≥ 6` | [NOTI-0x00E0.md](server-to-client/NOTI-0x00E0.md) |
| `0x00E1` | `ITEM_LIMIT_EDITION_INFO` | 布局部分可得 | `≥ 2 + n×62` | [NOTI-0x00E1.md](server-to-client/NOTI-0x00E1.md) |
| `0x00E2` | `POWER_WAR_BAN_ABUSER` | 结构未知 | `—` | [NOTI-0x00E2.md](server-to-client/NOTI-0x00E2.md) |
| `0x00E3` | `POWER_WAR_HP` | 结构未知 | `—` | [NOTI-0x00E3.md](server-to-client/NOTI-0x00E3.md) |
| `0x00E4` | `REVIVAL_CHARAC_TIMEOUT` | 结构未知 | `—` | [NOTI-0x00E4.md](server-to-client/NOTI-0x00E4.md) |
| `0x00E5` | `CHARAC_DAILY_CONNECTION` | 结构未知 | `—` | [NOTI-0x00E5.md](server-to-client/NOTI-0x00E5.md) |
| `0x00E6` | `ASK_LEAVE_PARTY` | 布局部分可得 | `≥ 1` | [NOTI-0x00E6.md](server-to-client/NOTI-0x00E6.md) |
| `0x00E7` | `POWER_WAR_USER_JOIN_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x00E7.md](server-to-client/NOTI-0x00E7.md) |
| `0x00E8` | `LOCK_IN_SERIA_ROOM_MESSAGE` | 结构未知 | `—` | [NOTI-0x00E8.md](server-to-client/NOTI-0x00E8.md) |
| `0x00E9` | `REQUEST_EXIT` | 布局部分可得 | `≥ 2` | [NOTI-0x00E9.md](server-to-client/NOTI-0x00E9.md) |
| `0x00EA` | `CONDITION_EVENT_CLEAR_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x00EA.md](server-to-client/NOTI-0x00EA.md) |
| `0x00EB` | `BLOOD_DUNGEON_CLEAR_COUNT` | 布局已闭合 | `2` | [NOTI-0x00EB.md](server-to-client/NOTI-0x00EB.md) |
| `0x00EC` | `BLACKLIST_PROBLEM_CHILD_TIME` | 布局部分可得 | `≥ 8` | [NOTI-0x00EC.md](server-to-client/NOTI-0x00EC.md) |
| `0x00ED` | `GUILD_CARGO` | 布局部分可得 | `≥ 107` | [NOTI-0x00ED.md](server-to-client/NOTI-0x00ED.md) |
| `0x00EE` | `VILLAGE_MONSTER_LIST` | 布局部分可得 | `≥ 1 + n×22` | [NOTI-0x00EE.md](server-to-client/NOTI-0x00EE.md) |
| `0x00EF` | `VILLAGE_MONSTER_POSITION` | 布局部分可得 | `≥ 1 + n×6` | [NOTI-0x00EF.md](server-to-client/NOTI-0x00EF.md) |
| `0x00F0` | `UPDATE_VILLAGE_MONSTER_LIST` | 布局部分可得 | `≥ 10` | [NOTI-0x00F0.md](server-to-client/NOTI-0x00F0.md) |
| `0x00F1` | `START_VILLAGE_ATTACKED` | 布局部分可得 | `≥ 4` | [NOTI-0x00F1.md](server-to-client/NOTI-0x00F1.md) |
| `0x00F2` | `END_VILLAGE_ATTACKED` | 结构未知 | `—` | [NOTI-0x00F2.md](server-to-client/NOTI-0x00F2.md) |
| `0x00F3` | `FIGHT_VILLAGE_MONSTER` | 结构未知 | `—` | [NOTI-0x00F3.md](server-to-client/NOTI-0x00F3.md) |
| `0x00F4` | `TAKE_SCREEN_SHOT` | 布局部分可得 | `≥ 4` | [NOTI-0x00F4.md](server-to-client/NOTI-0x00F4.md) |
| `0x00F5` | `MOVE_MAP` | 结构未知 | `—` | [NOTI-0x00F5.md](server-to-client/NOTI-0x00F5.md) |
| `0x00F6` | `CREATE_VILLAGE_MONSTER` | 布局部分可得 | `≥ 20` | [NOTI-0x00F6.md](server-to-client/NOTI-0x00F6.md) |
| `0x00F7` | `UPDATE_VILLAGE_ATTACKED` | 布局部分可得 | `≥ 12` | [NOTI-0x00F7.md](server-to-client/NOTI-0x00F7.md) |
| `0x00F8` | `STARTED_VILLAGE_ATTACKED` | 布局部分可得 | `≥ 16` | [NOTI-0x00F8.md](server-to-client/NOTI-0x00F8.md) |
| `0x00F9` | `FINISH_VILLAGE_MONSTER_FIGHTING` | 布局部分可得 | `≥ 10` | [NOTI-0x00F9.md](server-to-client/NOTI-0x00F9.md) |
| `0x00FA` | `VILLAGE_ATTACKED_REWARD` | 结构未知 | `—` | [NOTI-0x00FA.md](server-to-client/NOTI-0x00FA.md) |
| `0x00FB` | `ITEM_LOCK_LIST` | 布局部分可得 | `≥ 2 + n×8` | [NOTI-0x00FB.md](server-to-client/NOTI-0x00FB.md) |
| `0x00FC` | `ITEM_UNLOCK` | 布局部分可得 | `≥ 3` | [NOTI-0x00FC.md](server-to-client/NOTI-0x00FC.md) |
| `0x00FE` | `ELEVATOR_CLEAR_TIME_CHECK` | 布局部分可得 | `≥ 2` | [NOTI-0x00FE.md](server-to-client/NOTI-0x00FE.md) |
| `0x00FF` | `EXERCISE_MODE_CLEAR` | 布局部分可得 | `≥ 4` | [NOTI-0x00FF.md](server-to-client/NOTI-0x00FF.md) |
| `0x0101` | `ADD_EFFECT_ITEM_INFO` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x0101.md](server-to-client/NOTI-0x0101.md) |
| `0x0102` | `ARS_ALERT` | 结构未知 | `—` | [NOTI-0x0102.md](server-to-client/NOTI-0x0102.md) |
| `0x0103` | `MAX_LEVEL_LETHE_REWARD` | 结构未知 | `—` | [NOTI-0x0103.md](server-to-client/NOTI-0x0103.md) |
| `0x0104` | `ITEM_EFFECT` | 布局部分可得 | `≥ 6` | [NOTI-0x0104.md](server-to-client/NOTI-0x0104.md) |
| `0x0105` | `EPLP_RECHALLENGE` | 布局部分可得 | `≥ 1` | [NOTI-0x0105.md](server-to-client/NOTI-0x0105.md) |
| `0x0108` | `ZERO_EXP` | 结构未知 | `—` | [NOTI-0x0108.md](server-to-client/NOTI-0x0108.md) |
| `0x0109` | `POWER_WAR_VICTORY_BONUS` | 布局部分可得 | `≥ 16` | [NOTI-0x0109.md](server-to-client/NOTI-0x0109.md) |
| `0x010A` | `HOLD_VICTORY` | 布局部分可得 | `≥ 7` | [NOTI-0x010A.md](server-to-client/NOTI-0x010A.md) |
| `0x010B` | `VICTORY_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x010B.md](server-to-client/NOTI-0x010B.md) |
| `0x010C` | `PROPER_DUNGEON_CLEAR_COUNT` | 布局已闭合 | `2` | [NOTI-0x010C.md](server-to-client/NOTI-0x010C.md) |
| `0x010D` | `REQUEST_ADD_PVP_BUDDY` | 布局部分可得 | `≥ 1` | [NOTI-0x010D.md](server-to-client/NOTI-0x010D.md) |
| `0x010E` | `PVP_BUDDY_IN` | 结构未知 | `—` | [NOTI-0x010E.md](server-to-client/NOTI-0x010E.md) |
| `0x010F` | `PVP_BUDDY_OUT` | 结构未知 | `—` | [NOTI-0x010F.md](server-to-client/NOTI-0x010F.md) |
| `0x0110` | `PVP_BUDDY_CONN_LIST` | 结构未知 | `—` | [NOTI-0x0110.md](server-to-client/NOTI-0x0110.md) |
| `0x0111` | `UNITED_SERVER_FRIEND_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x0111.md](server-to-client/NOTI-0x0111.md) |
| `0x0112` | `INOUT_UNITED_SERVER_FRIEND` | 布局部分可得 | `≥ 4` | [NOTI-0x0112.md](server-to-client/NOTI-0x0112.md) |
| `0x0113` | `NCC` | 布局部分可得 | `≥ 4` | [NOTI-0x0113.md](server-to-client/NOTI-0x0113.md) |
| `0x0114` | `BREAK_AWAY_REWARD` | 布局部分可得 | `≥ 1` | [NOTI-0x0114.md](server-to-client/NOTI-0x0114.md) |
| `0x0115` | `RENEWAL_LETHE_REWARD` | 结构未知 | `—` | [NOTI-0x0115.md](server-to-client/NOTI-0x0115.md) |
| `0x0116` | `TRADE_PUNISH_END_TIME` | 结构未知 | `—` | [NOTI-0x0116.md](server-to-client/NOTI-0x0116.md) |
| `0x0117` | `SECRET_SHOP_NPC` | 布局部分可得 | `≥ 4` | [NOTI-0x0117.md](server-to-client/NOTI-0x0117.md) |
| `0x0118` | `SECRET_SHOP_ITEM_LIST` | 布局部分可得 | `≥ 4 + n×22` | [NOTI-0x0118.md](server-to-client/NOTI-0x0118.md) |
| `0x0119` | `COMPLETE_LOAD_PVP` | 布局部分可得 | `≥ 2` | [NOTI-0x0119.md](server-to-client/NOTI-0x0119.md) |
| `0x011A` | `CONNECT_P2P_PVP` | 结构未知 | `—` | [NOTI-0x011A.md](server-to-client/NOTI-0x011A.md) |
| `0x011B` | `ONLIE_PRELIMINARY_TEAM` | 布局部分可得 | `≥ 6` | [NOTI-0x011B.md](server-to-client/NOTI-0x011B.md) |
| `0x011C` | `SELECT_ROUTING` | 布局部分可得 | `≥ 4` | [NOTI-0x011C.md](server-to-client/NOTI-0x011C.md) |
| `0x011D` | `ROUTING_RESULT` | 布局部分可得 | `≥ 6` | [NOTI-0x011D.md](server-to-client/NOTI-0x011D.md) |
| `0x011E` | `TEMPORARY_PASSPAD_ALARM` | 结构未知 | `—` | [NOTI-0x011E.md](server-to-client/NOTI-0x011E.md) |
| `0x011F` | `USE_SAMSUNG_RANDOMBOX_ITEM` | 结构未知 | `—` | [NOTI-0x011F.md](server-to-client/NOTI-0x011F.md) |
| `0x0120` | `USE_GOBLIN_RANDOMBOX_ITEM` | 结构未知 | `—` | [NOTI-0x0120.md](server-to-client/NOTI-0x0120.md) |
| `0x0121` | `BREAK_GUILD` | 布局部分可得 | `≥ 6` | [NOTI-0x0121.md](server-to-client/NOTI-0x0121.md) |
| `0x0122` | `LIMIT_ITEM_USAGE` | 布局部分可得 | `≥ 12` | [NOTI-0x0122.md](server-to-client/NOTI-0x0122.md) |
| `0x0123` | `QUEST_TRIGGER_LIST` | 布局部分可得 | `≥ 2 + n×6` | [NOTI-0x0123.md](server-to-client/NOTI-0x0123.md) |
| `0x0124` | `REDEEM_LIST` | 布局部分可得 | `≥ 106` | [NOTI-0x0124.md](server-to-client/NOTI-0x0124.md) |
| `0x0125` | `CHARAC_LINK_MESSAGE` | 结构未知 | `—` | [NOTI-0x0125.md](server-to-client/NOTI-0x0125.md) |
| `0x0126` | `CHARAC_LINK_BONUS` | 布局部分可得 | `≥ 12` | [NOTI-0x0126.md](server-to-client/NOTI-0x0126.md) |
| `0x0127` | `DISCONNECT_LOW_LEVEL_LINK_CHARAC` | 布局部分可得 | `≥ 1` | [NOTI-0x0127.md](server-to-client/NOTI-0x0127.md) |
| `0x0128` | `SPAWN_RIDABLE_OBJECT` | 布局部分可得 | `≥ 20` | [NOTI-0x0128.md](server-to-client/NOTI-0x0128.md) |
| `0x0129` | `ULTIMATE_DIFFICULTY_UI` | 布局部分可得 | `≥ 4` | [NOTI-0x0129.md](server-to-client/NOTI-0x0129.md) |
| `0x012A` | `DUNGEON_INOUT_MESSAGE` | 布局部分可得 | `≥ 5` | [NOTI-0x012A.md](server-to-client/NOTI-0x012A.md) |
| `0x012B` | `MEMBER_BONUS_FATIGUE_LIMIT` | 结构未知 | `—` | [NOTI-0x012B.md](server-to-client/NOTI-0x012B.md) |
| `0x012C` | `EVENT_RIDABLE_INFO` | 结构未知 | `—` | [NOTI-0x012C.md](server-to-client/NOTI-0x012C.md) |
| `0x012D` | `PREMIUM_GOLD_CARD` | 结构未知 | `—` | [NOTI-0x012D.md](server-to-client/NOTI-0x012D.md) |
| `0x012F` | `TIME_SYNC` | 布局已闭合 | `4` | [NOTI-0x012F.md](server-to-client/NOTI-0x012F.md) |
| `0x0130` | `RETURN_USER` | 布局部分可得 | `≥ 4` | [NOTI-0x0130.md](server-to-client/NOTI-0x0130.md) |
| `0x0133` | `TODAY_GUILD_MEMBER` | 布局已闭合 | `1` | [NOTI-0x0133.md](server-to-client/NOTI-0x0133.md) |
| `0x0134` | `JOIN_GUILD_INFO` | 布局部分可得 | `≥ 13` | [NOTI-0x0134.md](server-to-client/NOTI-0x0134.md) |
| `0x0137` | `ACHIEVE_GUILD_ATTENDANCE` | 结构未知 | `—` | [NOTI-0x0137.md](server-to-client/NOTI-0x0137.md) |
| `0x0138` | `COMPLETE_CONDITION_PASS_GATE` | 布局部分可得 | `≥ 5` | [NOTI-0x0138.md](server-to-client/NOTI-0x0138.md) |
| `0x0139` | `BREAK_AWAY_QUEST_DATA` | 布局部分可得 | `≥ 5` | [NOTI-0x0139.md](server-to-client/NOTI-0x0139.md) |
| `0x013A` | `ANSWER_REMATCH` | 布局部分可得 | `≥ 3` | [NOTI-0x013A.md](server-to-client/NOTI-0x013A.md) |
| `0x013B` | `SPECIFIC_EVENT_NOTIFICATION` | 结构未知 | `—` | [NOTI-0x013B.md](server-to-client/NOTI-0x013B.md) |
| `0x013C` | `CASH_INVENTORY` | 结构未知 | `—` | [NOTI-0x013C.md](server-to-client/NOTI-0x013C.md) |
| `0x013D` | `END_PVP_RESULT` | 结构未知 | `—` | [NOTI-0x013D.md](server-to-client/NOTI-0x013D.md) |
| `0x013E` | `REGISTER_MATCH` | 结构未知 | `—` | [NOTI-0x013E.md](server-to-client/NOTI-0x013E.md) |
| `0x013F` | `REMATCH_REMAIN_COUNT` | 布局部分可得 | `≥ 4` | [NOTI-0x013F.md](server-to-client/NOTI-0x013F.md) |
| `0x0140` | `TEAM_LEADER` | 布局已闭合 | `2` | [NOTI-0x0140.md](server-to-client/NOTI-0x0140.md) |
| `0x0141` | `DEBUG_FILE_PRINT` | 结构未知 | `—` | [NOTI-0x0141.md](server-to-client/NOTI-0x0141.md) |
| `0x0142` | `PUNISH_CANCEL` | 布局部分可得 | `≥ 4` | [NOTI-0x0142.md](server-to-client/NOTI-0x0142.md) |
| `0x0143` | `PVP_MISSION_LIST` | 布局部分可得 | `≥ 19` | [NOTI-0x0143.md](server-to-client/NOTI-0x0143.md) |
| `0x0154` | `WARROOM_WP_PER_MONSTER` | 布局部分可得 | `≥ 12` | [NOTI-0x0154.md](server-to-client/NOTI-0x0154.md) |
| `0x0155` | `PVP_BONUS_COUNT` | 布局已闭合 | `2` | [NOTI-0x0155.md](server-to-client/NOTI-0x0155.md) |
| `0x0156` | `EXPAND_EQUIPSLOT_INFO` | 布局部分可得 | `≥ 2 + n1×59 + n2×3` | [NOTI-0x0156.md](server-to-client/NOTI-0x0156.md) |
| `0x0157` | `FAIR_PVP_CHANNEL_FIRST_LOGIN` | 结构未知 | `—` | [NOTI-0x0157.md](server-to-client/NOTI-0x0157.md) |
| `0x0158` | `RESET_ONE_A_DAY_ITEM_SHOP` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x0158.md](server-to-client/NOTI-0x0158.md) |
| `0x0159` | `WARROOM_PENALTY_WP` | 布局部分可得 | `≥ 1 + n×5` | [NOTI-0x0159.md](server-to-client/NOTI-0x0159.md) |
| `0x015A` | `SECU_SERVICE_STATE` | 结构未知 | `—` | [NOTI-0x015A.md](server-to-client/NOTI-0x015A.md) |
| `0x015B` | `USER_APC_INFO_TOD` | 布局部分可得 | `≥ 8` | [NOTI-0x015B.md](server-to-client/NOTI-0x015B.md) |
| `0x015C` | `TOD_CLEAR_REWARD` | 布局部分可得 | `≥ 15` | [NOTI-0x015C.md](server-to-client/NOTI-0x015C.md) |
| `0x015D` | `CHARAC_QP_POINT` | 结构未知 | `—` | [NOTI-0x015D.md](server-to-client/NOTI-0x015D.md) |
| `0x015F` | `TOTAL_ITEM_MAKING_SKILL_POINT` | 布局部分可得 | `≥ 1 + n×3` | [NOTI-0x015F.md](server-to-client/NOTI-0x015F.md) |
| `0x0160` | `GAIN_ITEM_MAKING_SKILL_POINT` | 布局部分可得 | `≥ 4` | [NOTI-0x0160.md](server-to-client/NOTI-0x0160.md) |
| `0x0161` | `CHANGE_HOST_WARROOM` | 布局部分可得 | `≥ 1` | [NOTI-0x0161.md](server-to-client/NOTI-0x0161.md) |
| `0x0162` | `GIVE_ITEM` | 结构未知 | `—` | [NOTI-0x0162.md](server-to-client/NOTI-0x0162.md) |
| `0x0164` | `CLEAR_QUEST_LIST` | 结构未知 | `—` | [NOTI-0x0164.md](server-to-client/NOTI-0x0164.md) |
| `0x0165` | `CHARAC_INVISIBLE_FALGS` | 布局部分可得 | `≥ 3` | [NOTI-0x0165.md](server-to-client/NOTI-0x0165.md) |
| `0x0166` | `TITLE_BOOK_LIST` | 布局部分可得 | `≥ 11 + n×22` | [NOTI-0x0166.md](server-to-client/NOTI-0x0166.md) |
| `0x0167` | `ACHIEVEMENT_LIST` | 布局部分可得 | `≥ 16` | [NOTI-0x0167.md](server-to-client/NOTI-0x0167.md) |
| `0x0168` | `ACHIEVEMENT_COMPLETE` | 布局部分可得 | `≥ 18` | [NOTI-0x0168.md](server-to-client/NOTI-0x0168.md) |
| `0x0169` | `EVENT_SERVER_AUTO_QUEST_CLEAR_REWARD_DATA` | 布局部分可得 | `≥ 10` | [NOTI-0x0169.md](server-to-client/NOTI-0x0169.md) |
| `0x016A` | `CREATE_GROUP` | 布局部分可得 | `≥ 6` | [NOTI-0x016A.md](server-to-client/NOTI-0x016A.md) |
| `0x016B` | `INVITE_MEMBER` | 布局部分可得 | `≥ 7` | [NOTI-0x016B.md](server-to-client/NOTI-0x016B.md) |
| `0x016C` | `INVITE_MEMBER_LIST` | 布局部分可得 | `≥ 5` | [NOTI-0x016C.md](server-to-client/NOTI-0x016C.md) |
| `0x016D` | `GROUP_MEMBER_LIST` | 布局部分可得 | `≥ 5` | [NOTI-0x016D.md](server-to-client/NOTI-0x016D.md) |
| `0x016E` | `LEAVE_USER_FROM_GROUP` | 布局部分可得 | `≥ 4` | [NOTI-0x016E.md](server-to-client/NOTI-0x016E.md) |
| `0x016F` | `MESSAGE_GROUP_CHAT` | 布局部分可得 | `≥ 4` | [NOTI-0x016F.md](server-to-client/NOTI-0x016F.md) |
| `0x0170` | `GROUP_CHAT_USER_STATE` | 布局部分可得 | `≥ 5 + n×1` | [NOTI-0x0170.md](server-to-client/NOTI-0x0170.md) |
| `0x0171` | `EVENT_ALL_USER_GIFT` | 结构未知 | `—` | [NOTI-0x0171.md](server-to-client/NOTI-0x0171.md) |
| `0x0172` | `CHECK_PICK_UP_RANDOM_OPTION_ITEM` | 布局部分可得 | `≥ 1` | [NOTI-0x0172.md](server-to-client/NOTI-0x0172.md) |
| `0x0173` | `MESSAGE_HYPER_LINK` | 布局部分可得 | `≥ 5 + n×140` | [NOTI-0x0173.md](server-to-client/NOTI-0x0173.md) |
| `0x0174` | `MESSAGE_OTHER_CHANNEL_HYPER_LINK` | 布局部分可得 | `≥ 3` | [NOTI-0x0174.md](server-to-client/NOTI-0x0174.md) |
| `0x0175` | `MEGAPHONE_MSG_HYPER_LINK` | 布局部分可得 | `≥ 7 + n×140` | [NOTI-0x0175.md](server-to-client/NOTI-0x0175.md) |
| `0x0177` | `EVENT_INIT_INFO` | 布局部分可得 | `≥ 5 + n×2` | [NOTI-0x0177.md](server-to-client/NOTI-0x0177.md) |
| `0x0178` | `EVENT_HISTORY_INFO` | 布局部分可得 | `≥ 1 + n×2` | [NOTI-0x0178.md](server-to-client/NOTI-0x0178.md) |
| `0x0179` | `START_GIVE_ITEM_GROWTH_POWER_SYSTEM` | 布局部分可得 | `≥ 3` | [NOTI-0x0179.md](server-to-client/NOTI-0x0179.md) |
| `0x017A` | `PARTY_TELEPORT_STATUS` | 布局部分可得 | `≥ 6 + n×3` | [NOTI-0x017A.md](server-to-client/NOTI-0x017A.md) |
| `0x017B` | `AURA_TYPE_COOLTIME` | 布局部分可得 | `≥ 1 + n×5` | [NOTI-0x017B.md](server-to-client/NOTI-0x017B.md) |
| `0x017C` | `SHOW_EFFECT` | 布局部分可得 | `≥ 2 + n×2` | [NOTI-0x017C.md](server-to-client/NOTI-0x017C.md) |
| `0x017D` | `SECURITY_REWARD` | 布局部分可得 | `≥ 13` | [NOTI-0x017D.md](server-to-client/NOTI-0x017D.md) |
| `0x017E` | `SOLOPLAY_REJECT_MSG` | 布局部分可得 | `≥ 8` | [NOTI-0x017E.md](server-to-client/NOTI-0x017E.md) |
| `0x017F` | `MADE_QUICK_PARTY` | 布局部分可得 | `≥ 1` | [NOTI-0x017F.md](server-to-client/NOTI-0x017F.md) |
| `0x0180` | `CANCEL_QUICK_PARTY` | 结构未知 | `—` | [NOTI-0x0180.md](server-to-client/NOTI-0x0180.md) |
| `0x0181` | `UPDATE_QUICK_PARTY` | 布局部分可得 | `≥ 4` | [NOTI-0x0181.md](server-to-client/NOTI-0x0181.md) |
| `0x0183` | `TOURNAMENT_INFO` | 布局部分可得 | `≥ 18 + n×6` | [NOTI-0x0183.md](server-to-client/NOTI-0x0183.md) |
| `0x0184` | `TOURNAMENT_MAP_INFO` | 布局部分可得 | `≥ 29 + n×18` | [NOTI-0x0184.md](server-to-client/NOTI-0x0184.md) |
| `0x0185` | `TOURNAMENT_CLEAR_REWARD` | 布局部分可得 | `≥ 8 + n1×14 + n2×10 + n3×10` | [NOTI-0x0185.md](server-to-client/NOTI-0x0185.md) |
| `0x0186` | `REMAIN_GHOST_TIME` | 布局部分可得 | `≥ 8` | [NOTI-0x0186.md](server-to-client/NOTI-0x0186.md) |
| `0x0187` | `CHARACTER_OPTION` | 布局部分可得 | `≥ 4` | [NOTI-0x0187.md](server-to-client/NOTI-0x0187.md) |
| `0x0192` | `LIMIT_NPC_BUY_ITEM_CHANGE_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0192.md](server-to-client/NOTI-0x0192.md) |
| `0x0193` | `CHARACTER_STATUS` | 结构未知 | `—` | [NOTI-0x0193.md](server-to-client/NOTI-0x0193.md) |
| `0x0194` | `LIMIT_NPC_BUY_ITEM_INFO_ALL` | 布局部分可得 | `≥ 12` | [NOTI-0x0194.md](server-to-client/NOTI-0x0194.md) |
| `0x0195` | `CHRISTMAS_TREE_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x0195.md](server-to-client/NOTI-0x0195.md) |
| `0x0196` | `SERIA_ROOM_DECO_INFO` | 布局部分可得 | `≥ 0 + n×1` | [NOTI-0x0196.md](server-to-client/NOTI-0x0196.md) |
| `0x0197` | `OBJECT_BRING_UP_USE_ITEM` | 布局部分可得 | `≥ 1` | [NOTI-0x0197.md](server-to-client/NOTI-0x0197.md) |
| `0x019C` | `EMOTICON_CONF_INFO` | 布局部分可得 | `≥ 2 + n×4` | [NOTI-0x019C.md](server-to-client/NOTI-0x019C.md) |
| `0x019D` | `BOOSTER_GAGE` | 布局部分可得 | `≥ 2` | [NOTI-0x019D.md](server-to-client/NOTI-0x019D.md) |
| `0x019F` | `TAG_CHARACTER_INFO` | 布局部分可得 | `≥ 36 + n×4` | [NOTI-0x019F.md](server-to-client/NOTI-0x019F.md) |
| `0x01A0` | `MANAGE_LEVEL_UP` | 结构未知 | `—` | [NOTI-0x01A0.md](server-to-client/NOTI-0x01A0.md) |
| `0x01A1` | `INFORM_NOTICE` | 布局部分可得 | `≥ 1 + n×1` | [NOTI-0x01A1.md](server-to-client/NOTI-0x01A1.md) |
| `0x01A2` | `VERIFY_PVP_LAG_USER` | 结构未知 | `—` | [NOTI-0x01A2.md](server-to-client/NOTI-0x01A2.md) |
| `0x01A3` | `PVP_LAG_USER_PENALTY` | 布局部分可得 | `≥ 3` | [NOTI-0x01A3.md](server-to-client/NOTI-0x01A3.md) |
| `0x01A4` | `GM_CHECK_LAG_OCCUR` | 结构未知 | `—` | [NOTI-0x01A4.md](server-to-client/NOTI-0x01A4.md) |
| `0x01A5` | `GM_CHECK_LAG_COUNT` | 结构未知 | `—` | [NOTI-0x01A5.md](server-to-client/NOTI-0x01A5.md) |
| `0x01A6` | `GM_CHECK_KICK_OUT_ROOM_COUNT` | 结构未知 | `—` | [NOTI-0x01A6.md](server-to-client/NOTI-0x01A6.md) |
| `0x01A7` | `TIME_GATE_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x01A7.md](server-to-client/NOTI-0x01A7.md) |
| `0x01A8` | `RESET_CRANE_NEED_MATERIAL` | 布局部分可得 | `≥ 1` | [NOTI-0x01A8.md](server-to-client/NOTI-0x01A8.md) |
| `0x01A9` | `PC_ROOM_PLAY_TIME` | 结构未知 | `—` | [NOTI-0x01A9.md](server-to-client/NOTI-0x01A9.md) |
| `0x01AA` | `SKILL_INIT` | 结构未知 | `—` | [NOTI-0x01AA.md](server-to-client/NOTI-0x01AA.md) |
| `0x01AC` | `MILEAGE_DATA` | 布局部分可得 | `≥ 9` | [NOTI-0x01AC.md](server-to-client/NOTI-0x01AC.md) |
| `0x01AE` | `EVENT_ITEM_MAIL_ALRAM_TOOLTIP_` | 结构未知 | `—` | [NOTI-0x01AE.md](server-to-client/NOTI-0x01AE.md) |
| `0x01AF` | `BLUE_MARBLE_ROOM_INFO` | 布局部分可得 | `≥ 3 + n×3` | [NOTI-0x01AF.md](server-to-client/NOTI-0x01AF.md) |
| `0x01B0` | `BLUE_MARBLE_DICE_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x01B0.md](server-to-client/NOTI-0x01B0.md) |
| `0x01B1` | `BLUE_MARBLE_START` | 布局部分可得 | `≥ 1` | [NOTI-0x01B1.md](server-to-client/NOTI-0x01B1.md) |
| `0x01B2` | `BLUE_MARBLE_CLEAR_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x01B2.md](server-to-client/NOTI-0x01B2.md) |
| `0x01B3` | `BLUE_MARBLE_ACTION_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x01B3.md](server-to-client/NOTI-0x01B3.md) |
| `0x01B4` | `BLUE_MARBLE_TILE_GIVE_UP_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x01B4.md](server-to-client/NOTI-0x01B4.md) |
| `0x01B5` | `BLUE_MARBLE_ENTER_COUNT_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x01B5.md](server-to-client/NOTI-0x01B5.md) |
| `0x01B7` | `MAX_EQUIP_LEVEL` | 布局部分可得 | `≥ 2` | [NOTI-0x01B7.md](server-to-client/NOTI-0x01B7.md) |
| `0x01B9` | `INFORM_NOTICE_2ND` | 布局部分可得 | `≥ 1 + n×1` | [NOTI-0x01B9.md](server-to-client/NOTI-0x01B9.md) |
| `0x01BC` | `REQUEST_ASSAULT` | 布局部分可得 | `≥ 3` | [NOTI-0x01BC.md](server-to-client/NOTI-0x01BC.md) |
| `0x01BD` | `ANSWER_REQUEST_ASSAULTERS` | 布局部分可得 | `≥ 3` | [NOTI-0x01BD.md](server-to-client/NOTI-0x01BD.md) |
| `0x01BF` | `RETURN_USER_LOGIN` | 结构未知 | `—` | [NOTI-0x01BF.md](server-to-client/NOTI-0x01BF.md) |
| `0x01C0` | `COMBO_SKILL_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x01C0.md](server-to-client/NOTI-0x01C0.md) |
| `0x01C1` | `CHARAC_QUEST_PIECE` | 布局部分可得 | `≥ 1` | [NOTI-0x01C1.md](server-to-client/NOTI-0x01C1.md) |
| `0x01C3` | `NEW_CHARAC_CREATURE_EVENT_INFO` | 布局部分可得 | `≥ 1 + n×2` | [NOTI-0x01C3.md](server-to-client/NOTI-0x01C3.md) |
| `0x01C4` | `SERIAROOM_ANIMATION_DECO_EVENT` | 布局部分可得 | `≥ 2` | [NOTI-0x01C4.md](server-to-client/NOTI-0x01C4.md) |
| `0x01C5` | `BINGO_DATA` | 布局部分可得 | `≥ 93 + n×2` | [NOTI-0x01C5.md](server-to-client/NOTI-0x01C5.md) |
| `0x01C6` | `BINGO_MARK` | 布局部分可得 | `—` | [NOTI-0x01C6.md](server-to-client/NOTI-0x01C6.md) |
| `0x01C7` | `HOTKEY_OPTION` | 布局部分可得 | `≥ 1` | [NOTI-0x01C7.md](server-to-client/NOTI-0x01C7.md) |
| `0x01C8` | `DUAL_RAID_PARTY_GROUP_INFO` | 布局部分可得 | `≥ 3 + n×5` | [NOTI-0x01C8.md](server-to-client/NOTI-0x01C8.md) |
| `0x01C9` | `MULTIBOX_FAIL_CNT_OVER` | 结构未知 | `—` | [NOTI-0x01C9.md](server-to-client/NOTI-0x01C9.md) |
| `0x01CA` | `START_SCRIPT_RELOAD` | 结构未知 | `—` | [NOTI-0x01CA.md](server-to-client/NOTI-0x01CA.md) |
| `0x01CB` | `END_SCRIPT_RELOAD` | 布局部分可得 | `≥ 6` | [NOTI-0x01CB.md](server-to-client/NOTI-0x01CB.md) |
| `0x01CC` | `JUMPING_CHARACTER_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x01CC.md](server-to-client/NOTI-0x01CC.md) |
| `0x01CD` | `MATCHING_COMPLETE` | 布局部分可得 | `≥ 2` | [NOTI-0x01CD.md](server-to-client/NOTI-0x01CD.md) |
| `0x01CE` | `PVP_MATCH_INFO` | 布局部分可得 | `≥ 11` | [NOTI-0x01CE.md](server-to-client/NOTI-0x01CE.md) |
| `0x01CF` | `DUAL_RAID_DUNGEON_START` | 结构未知 | `—` | [NOTI-0x01CF.md](server-to-client/NOTI-0x01CF.md) |
| `0x01D0` | `DUAL_RAID_DUNGEON_MOVE_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x01D0.md](server-to-client/NOTI-0x01D0.md) |
| `0x01D1` | `MOVE_DUAL_RAID_BOSS_DUNGEON` | 布局部分可得 | `≥ 1` | [NOTI-0x01D1.md](server-to-client/NOTI-0x01D1.md) |
| `0x01D2` | `EXIT_DUAL_RAID_USER` | 结构未知 | `—` | [NOTI-0x01D2.md](server-to-client/NOTI-0x01D2.md) |
| `0x01D3` | `DUAL_RAID_DUNGEON_MAP_CLEAR` | 布局部分可得 | `≥ 3` | [NOTI-0x01D3.md](server-to-client/NOTI-0x01D3.md) |
| `0x01D4` | `DUAL_RAID_DUNGEON_RESULT_REWARD` | 布局部分可得 | `≥ 5` | [NOTI-0x01D4.md](server-to-client/NOTI-0x01D4.md) |
| `0x01D5` | `REWARD_ITEMS` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x01D5.md](server-to-client/NOTI-0x01D5.md) |
| `0x01D6` | `ENTER_RACING_DUNGEON` | 结构未知 | `—` | [NOTI-0x01D6.md](server-to-client/NOTI-0x01D6.md) |
| `0x01D7` | `ENTER_RACING_DUNGEON_FAIL` | 结构未知 | `—` | [NOTI-0x01D7.md](server-to-client/NOTI-0x01D7.md) |
| `0x01D8` | `RACING_DUNGEON_GOAL_IN_PLAYER` | 布局已闭合（含计数重复组） | `1 + n×7` | [NOTI-0x01D8.md](server-to-client/NOTI-0x01D8.md) |
| `0x01D9` | `RACING_DUNGEON_PLAY_RESULT` | 布局部分可得 | `≥ 1 + n×7` | [NOTI-0x01D9.md](server-to-client/NOTI-0x01D9.md) |
| `0x01DA` | `RACING_DUNGEON_JOIN_COUNT` | 布局已闭合 | `1` | [NOTI-0x01DA.md](server-to-client/NOTI-0x01DA.md) |
| `0x01DB` | `DUAL_RAID_DUNGEON_READY_COMPLETE` | 结构未知 | `—` | [NOTI-0x01DB.md](server-to-client/NOTI-0x01DB.md) |
| `0x01DC` | `GET_ITEMBOX` | 布局部分可得 | `≥ 4` | [NOTI-0x01DC.md](server-to-client/NOTI-0x01DC.md) |
| `0x01DD` | `GENERATE_ITEMBOX` | 布局部分可得 | `≥ 18` | [NOTI-0x01DD.md](server-to-client/NOTI-0x01DD.md) |
| `0x01DE` | `SELECT_ZOMBIE` | 布局部分可得 | `≥ 1` | [NOTI-0x01DE.md](server-to-client/NOTI-0x01DE.md) |
| `0x01DF` | `ZOMBIE_MODE_RESULT_SCORE` | 布局部分可得 | `≥ 43` | [NOTI-0x01DF.md](server-to-client/NOTI-0x01DF.md) |
| `0x01E0` | `MONSTER_MOVE_SYSTEM` | 布局部分可得 | `≥ 23` | [NOTI-0x01E0.md](server-to-client/NOTI-0x01E0.md) |
| `0x01E1` | `GROWTH_ITEM_RENEW_DATA` | 布局部分可得 | `≥ 8` | [NOTI-0x01E1.md](server-to-client/NOTI-0x01E1.md) |
| `0x01E2` | `GROWTH_ITEM_CHANGE_DATA` | 布局部分可得 | `≥ 12` | [NOTI-0x01E2.md](server-to-client/NOTI-0x01E2.md) |
| `0x01E3` | `LOADING_CART` | 布局部分可得 | `≥ 1 + n×6` | [NOTI-0x01E3.md](server-to-client/NOTI-0x01E3.md) |
| `0x01E4` | `GOLDEN_COMMERCE_START` | 结构未知 | `—` | [NOTI-0x01E4.md](server-to-client/NOTI-0x01E4.md) |
| `0x01E5` | `GOLDEN_COMMERCE_REWARD` | 布局部分可得 | `≥ 9` | [NOTI-0x01E5.md](server-to-client/NOTI-0x01E5.md) |
| `0x01E6` | `CHURN_GRADE_ACTION` | 布局部分可得 | `≥ 17` | [NOTI-0x01E6.md](server-to-client/NOTI-0x01E6.md) |
| `0x01E8` | `CHARACTER_ADD_BUFF` | 布局部分可得 | `≥ 17` | [NOTI-0x01E8.md](server-to-client/NOTI-0x01E8.md) |
| `0x01E9` | `CHARACTER_DEL_BUFF` | 布局部分可得 | `≥ 1 + n×4` | [NOTI-0x01E9.md](server-to-client/NOTI-0x01E9.md) |
| `0x01EA` | `CHARACTER_BUFF_DUNGEON` | 布局部分可得 | `≥ 2 + n×4` | [NOTI-0x01EA.md](server-to-client/NOTI-0x01EA.md) |
| `0x01EB` | `USER_FATIGUE_ACCELERATION_STATE` | 布局部分可得 | `≥ 3` | [NOTI-0x01EB.md](server-to-client/NOTI-0x01EB.md) |
| `0x01EC` | `MINORITY_STATE` | 布局部分可得 | `≥ 4` | [NOTI-0x01EC.md](server-to-client/NOTI-0x01EC.md) |
| `0x01EF` | `DO_YOU_JOIN_STUDY` | 布局部分可得 | `≥ 5` | [NOTI-0x01EF.md](server-to-client/NOTI-0x01EF.md) |
| `0x01F0` | `LETS_START_STUDY` | 布局部分可得 | `≥ 2` | [NOTI-0x01F0.md](server-to-client/NOTI-0x01F0.md) |
| `0x01F1` | `DECIDE_LEVEL` | 布局部分可得 | `≥ 5` | [NOTI-0x01F1.md](server-to-client/NOTI-0x01F1.md) |
| `0x01F2` | `DECIDE_MAIN_REWARD` | 结构未知 | `—` | [NOTI-0x01F2.md](server-to-client/NOTI-0x01F2.md) |
| `0x01F6` | `DNF_VIP_SERVICE` | 布局部分可得 | `≥ 1` | [NOTI-0x01F6.md](server-to-client/NOTI-0x01F6.md) |
| `0x01F7` | `SERVER_NOTICE_MESSAGE` | 布局部分可得 | `≥ 1` | [NOTI-0x01F7.md](server-to-client/NOTI-0x01F7.md) |
| `0x01F8` | `GM_PVP_MATCHING_RATING` | 布局部分可得 | `≥ 2` | [NOTI-0x01F8.md](server-to-client/NOTI-0x01F8.md) |
| `0x01F9` | `INTEGRATE_MATCHING_PLAYER_UID_LIST` | 布局部分可得 | `≥ 23 + n×2` | [NOTI-0x01F9.md](server-to-client/NOTI-0x01F9.md) |
| `0x01FA` | `INTEGRATE_MATCHING_COMPLETE` | 结构未知 | `—` | [NOTI-0x01FA.md](server-to-client/NOTI-0x01FA.md) |
| `0x01FB` | `INTEGRATE_MATCHING_STATE` | 布局部分可得 | `≥ 1` | [NOTI-0x01FB.md](server-to-client/NOTI-0x01FB.md) |
| `0x01FC` | `INTEGRATED_MATCHING_MODE_CHANGE` | 布局部分可得 | `≥ 3` | [NOTI-0x01FC.md](server-to-client/NOTI-0x01FC.md) |
| `0x01FE` | `INTEGRATE_MATCHING_USER_COUNT` | 布局部分可得 | `≥ 5 + n×8` | [NOTI-0x01FE.md](server-to-client/NOTI-0x01FE.md) |
| `0x0200` | `UPGRADE_ROOM_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x0200.md](server-to-client/NOTI-0x0200.md) |
| `0x0201` | `UPGRADE_ROOM_UPGRADE_ITEM_STATE` | 布局部分可得 | `≥ 8` | [NOTI-0x0201.md](server-to-client/NOTI-0x0201.md) |
| `0x0204` | `GIVE_USER_INFO` | 布局部分可得 | `≥ 10` | [NOTI-0x0204.md](server-to-client/NOTI-0x0204.md) |
| `0x0205` | `UPGRADE_ROOM_GIVE_ITEM_INFO` | 布局部分可得 | `≥ 11` | [NOTI-0x0205.md](server-to-client/NOTI-0x0205.md) |
| `0x0206` | `COMBO_SCORE_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x0206.md](server-to-client/NOTI-0x0206.md) |
| `0x0207` | `UPGRADE_ROOM_PUT_UP_ITEM` | 布局部分可得 | `≥ 9` | [NOTI-0x0207.md](server-to-client/NOTI-0x0207.md) |
| `0x0208` | `UPGRADE_ROOM_UPGRADE_START` | 结构未知 | `—` | [NOTI-0x0208.md](server-to-client/NOTI-0x0208.md) |
| `0x0209` | `UPGRADE_ROOM_UPGRADE_CANCEL` | 结构未知 | `—` | [NOTI-0x0209.md](server-to-client/NOTI-0x0209.md) |
| `0x020A` | `UPGRADE_ROOM_LIST_INFO` | 布局部分可得 | `≥ 18` | [NOTI-0x020A.md](server-to-client/NOTI-0x020A.md) |
| `0x020B` | `UPGRADE_JOIN_SPECTATOR_USER_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x020B.md](server-to-client/NOTI-0x020B.md) |
| `0x020C` | `LEAVE_SPECTATOR_USER_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x020C.md](server-to-client/NOTI-0x020C.md) |
| `0x020D` | `CREATE_UPGRADE_ROOM_INFO` | 布局部分可得 | `≥ 10` | [NOTI-0x020D.md](server-to-client/NOTI-0x020D.md) |
| `0x020E` | `DESTROY_UPGRADE_ROOM_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x020E.md](server-to-client/NOTI-0x020E.md) |
| `0x020F` | `UPGRADE_ROOM_MASTER_MESSAGE` | 结构未知 | `—` | [NOTI-0x020F.md](server-to-client/NOTI-0x020F.md) |
| `0x0210` | `SPECIFIC_MONSTER_APPEAR` | 布局部分可得 | `≥ 1 + n×19` | [NOTI-0x0210.md](server-to-client/NOTI-0x0210.md) |
| `0x0211` | `FREE_UPGRADE_AVAILABLE_COUNT` | 布局部分可得 | `≥ 1` | [NOTI-0x0211.md](server-to-client/NOTI-0x0211.md) |
| `0x0214` | `DUNGEON_SYSTEM_MESSAGE` | 布局部分可得 | `≥ 1` | [NOTI-0x0214.md](server-to-client/NOTI-0x0214.md) |
| `0x0215` | `VILLAGE_ATTACKED_MY_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x0215.md](server-to-client/NOTI-0x0215.md) |
| `0x0216` | `USE_ENCHANT_STORE` | 布局部分可得 | `≥ 8` | [NOTI-0x0216.md](server-to-client/NOTI-0x0216.md) |
| `0x0217` | `UXRETURNER_UXLOG_PERMISSION` | 布局部分可得 | `≥ 4` | [NOTI-0x0217.md](server-to-client/NOTI-0x0217.md) |
| `0x0218` | `AVAILABILITY_CHILDREN_DAY_EVENT_REWARD` | 布局部分可得 | `≥ 1` | [NOTI-0x0218.md](server-to-client/NOTI-0x0218.md) |
| `0x0219` | `EXP_KEEPING_AMOUNT` | 布局已闭合 | `5` | [NOTI-0x0219.md](server-to-client/NOTI-0x0219.md) |
| `0x021A` | `UPDATE_MILEAGE_DATA` | 布局部分可得 | `≥ 13` | [NOTI-0x021A.md](server-to-client/NOTI-0x021A.md) |
| `0x021B` | `CREATE_EXPERT_JOB_STORE` | 布局部分可得 | `≥ 15 + n×1` | [NOTI-0x021B.md](server-to-client/NOTI-0x021B.md) |
| `0x021C` | `CLOSE_EXPERT_JOB_STORE` | 布局部分可得 | `≥ 2` | [NOTI-0x021C.md](server-to-client/NOTI-0x021C.md) |
| `0x021E` | `SET_USABLE_COUNT_INFO` | 布局部分可得 | `≥ 1 + n×5` | [NOTI-0x021E.md](server-to-client/NOTI-0x021E.md) |
| `0x021F` | `CARD_BOOK_INFO` | 布局部分可得 | `≥ 4 + n×4` | [NOTI-0x021F.md](server-to-client/NOTI-0x021F.md) |
| `0x0221` | `UPDATE_EXPERT_JOB_STORE` | 布局部分可得 | `≥ 5` | [NOTI-0x0221.md](server-to-client/NOTI-0x0221.md) |
| `0x0222` | `DIMENSION_SPACE_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x0222.md](server-to-client/NOTI-0x0222.md) |
| `0x0223` | `STRONGEST_PVP_MEMBER_CHANNELINFO` | 布局部分可得 | `≥ 3` | [NOTI-0x0223.md](server-to-client/NOTI-0x0223.md) |
| `0x0224` | `EVENT_DUNGEON_TOP_RANK_INFO` | 布局部分可得 | `≥ 9 + n×9` | [NOTI-0x0224.md](server-to-client/NOTI-0x0224.md) |
| `0x0225` | `TERA_PIECE_CLEAR_TIME_RANK_INFO` | 布局部分可得 | `≥ 2 + n1×5 + n2×9` | [NOTI-0x0225.md](server-to-client/NOTI-0x0225.md) |
| `0x0226` | `CONDITION_PASS_GATE_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x0226.md](server-to-client/NOTI-0x0226.md) |
| `0x0227` | `DUNGEON_ENTER_COUNT_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x0227.md](server-to-client/NOTI-0x0227.md) |
| `0x0228` | `UPDATE_SPEC_EVENT` | 布局部分可得 | `≥ 7` | [NOTI-0x0228.md](server-to-client/NOTI-0x0228.md) |
| `0x0229` | `INFO_SPEC_EVENT` | 布局部分可得 | `≥ 2 + n×8` | [NOTI-0x0229.md](server-to-client/NOTI-0x0229.md) |
| `0x022A` | `CARD_BIND_UPGRADE_DAY_EVENT` | 布局已闭合 | `8` | [NOTI-0x022A.md](server-to-client/NOTI-0x022A.md) |
| `0x022B` | `GIVE_FIGHT_DOLL_TICKET` | 结构未知 | `—` | [NOTI-0x022B.md](server-to-client/NOTI-0x022B.md) |
| `0x022C` | `FIGHT_DOLL_LIST` | 布局部分可得 | `≥ 8` | [NOTI-0x022C.md](server-to-client/NOTI-0x022C.md) |
| `0x022D` | `GAUGE_OBJECT_BAR_DATA` | 布局已闭合 | `4` | [NOTI-0x022D.md](server-to-client/NOTI-0x022D.md) |
| `0x022E` | `UPDATE_GOLD_ONLY` | 布局部分可得 | `≥ 2` | [NOTI-0x022E.md](server-to-client/NOTI-0x022E.md) |
| `0x022F` | `MINIMAP_ICON_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x022F.md](server-to-client/NOTI-0x022F.md) |
| `0x0230` | `USER_STATE_MOTION` | 布局部分可得 | `≥ 15` | [NOTI-0x0230.md](server-to-client/NOTI-0x0230.md) |
| `0x0231` | `COLLECT_SKILL_COUNT_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0231.md](server-to-client/NOTI-0x0231.md) |
| `0x0232` | `PCROOM_TIME_POINT` | 布局部分可得 | `≥ 17` | [NOTI-0x0232.md](server-to-client/NOTI-0x0232.md) |
| `0x0233` | `AVATAR_SPEC_EVENT_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0233.md](server-to-client/NOTI-0x0233.md) |
| `0x0234` | `ACCOUNT_MATERIAL_STORAGE_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0234.md](server-to-client/NOTI-0x0234.md) |
| `0x0235` | `DURING_LOGIN_TIME_EVENT_INFO` | 布局部分可得 | `≥ 13` | [NOTI-0x0235.md](server-to-client/NOTI-0x0235.md) |
| `0x0237` | `RETURN_USER_REWARD_STATE` | 布局部分可得 | `≥ 1` | [NOTI-0x0237.md](server-to-client/NOTI-0x0237.md) |
| `0x0238` | `PHASE_START_SUMMON_LINE` | 布局部分可得 | `≥ 9` | [NOTI-0x0238.md](server-to-client/NOTI-0x0238.md) |
| `0x0239` | `FATIGUE_USE_REWARD_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x0239.md](server-to-client/NOTI-0x0239.md) |
| `0x023A` | `ATTENDANCE_MARBLE_BONUS_CHANCE` | 结构未知 | `—` | [NOTI-0x023A.md](server-to-client/NOTI-0x023A.md) |
| `0x023C` | `EXPECT_PARTY_LIST` | 布局部分可得 | `≥ 4` | [NOTI-0x023C.md](server-to-client/NOTI-0x023C.md) |
| `0x023D` | `EVENT_GIFT_AVAILABILITY` | 布局部分可得 | `≥ 2 + n×3` | [NOTI-0x023D.md](server-to-client/NOTI-0x023D.md) |
| `0x023E` | `CREATABLE_HARDCORE_CHARAC` | 布局部分可得 | `≥ 1` | [NOTI-0x023E.md](server-to-client/NOTI-0x023E.md) |
| `0x023F` | `ACCEPTED_QUEST` | 布局部分可得 | `≥ 4 + n×6` | [NOTI-0x023F.md](server-to-client/NOTI-0x023F.md) |
| `0x0242` | `DIMENSION_EXPERIENCE_INFO` | 布局部分可得 | `≥ 7 + n×1` | [NOTI-0x0242.md](server-to-client/NOTI-0x0242.md) |
| `0x0243` | `UPDATE_PVP_MISSION` | 布局部分可得 | `≥ 19` | [NOTI-0x0243.md](server-to-client/NOTI-0x0243.md) |
| `0x0244` | `UPDATE_NEW_DAILY_PVP_MISSION` | 布局部分可得 | `≥ 20` | [NOTI-0x0244.md](server-to-client/NOTI-0x0244.md) |
| `0x0245` | `SEND_DECK_INFO` | 布局部分可得 | `≥ 0 + n×4` | [NOTI-0x0245.md](server-to-client/NOTI-0x0245.md) |
| `0x0246` | `HARDCORE_STATUE` | 布局部分可得 | `≥ 2` | [NOTI-0x0246.md](server-to-client/NOTI-0x0246.md) |
| `0x0247` | `RAID_DO_BEHAVIOR` | 布局部分可得 | `≥ 6` | [NOTI-0x0247.md](server-to-client/NOTI-0x0247.md) |
| `0x0248` | `RAID_SET_SYMBOL` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x0248.md](server-to-client/NOTI-0x0248.md) |
| `0x0249` | `RAID_DUNGEON_PARTICIPATION_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x0249.md](server-to-client/NOTI-0x0249.md) |
| `0x024A` | `RAID_DUNGEON_STATE` | 布局部分可得 | `≥ 6 + n×5` | [NOTI-0x024A.md](server-to-client/NOTI-0x024A.md) |
| `0x024B` | `RAID_CHANGE_DUNGEON_STATE` | 布局部分可得 | `≥ 6` | [NOTI-0x024B.md](server-to-client/NOTI-0x024B.md) |
| `0x024C` | `RAID_STATE` | 布局部分可得 | `≥ 2` | [NOTI-0x024C.md](server-to-client/NOTI-0x024C.md) |
| `0x024D` | `RAID_MESSAGE` | 布局部分可得 | `≥ 1` | [NOTI-0x024D.md](server-to-client/NOTI-0x024D.md) |
| `0x024E` | `RAID_MEMBER_STATE` | 布局部分可得 | `≥ 3` | [NOTI-0x024E.md](server-to-client/NOTI-0x024E.md) |
| `0x024F` | `RAID_LIST` | 布局部分可得 | `≥ 4 + n×25` | [NOTI-0x024F.md](server-to-client/NOTI-0x024F.md) |
| `0x0250` | `RAID_MODIFY` | 布局部分可得 | `≥ 8` | [NOTI-0x0250.md](server-to-client/NOTI-0x0250.md) |
| `0x0251` | `RAID_WAITING_LIST` | 布局部分可得 | `≥ 5` | [NOTI-0x0251.md](server-to-client/NOTI-0x0251.md) |
| `0x0252` | `RAID_WAITING_MODIFY` | 布局部分可得 | `≥ 4` | [NOTI-0x0252.md](server-to-client/NOTI-0x0252.md) |
| `0x0253` | `PREPARE_START_RAID` | 结构未知 | `—` | [NOTI-0x0253.md](server-to-client/NOTI-0x0253.md) |
| `0x0255` | `RAID_SET_TIMER` | 布局部分可得 | `≥ 13` | [NOTI-0x0255.md](server-to-client/NOTI-0x0255.md) |
| `0x0256` | `RAID_REMAIN_TIME` | 布局部分可得 | `≥ 5` | [NOTI-0x0256.md](server-to-client/NOTI-0x0256.md) |
| `0x0257` | `RAID_ENTRY_COST_INFO` | 布局部分可得 | `≥ 4 + n×5` | [NOTI-0x0257.md](server-to-client/NOTI-0x0257.md) |
| `0x0258` | `RAID_MOVIE_SKIP` | 结构未知 | `—` | [NOTI-0x0258.md](server-to-client/NOTI-0x0258.md) |
| `0x0259` | `RAID_REWARD_LIST` | 布局部分可得 | `≥ 12` | [NOTI-0x0259.md](server-to-client/NOTI-0x0259.md) |
| `0x025A` | `RAID_RESULT` | 布局部分可得 | `≥ 10` | [NOTI-0x025A.md](server-to-client/NOTI-0x025A.md) |
| `0x025B` | `SEQUENTIAL_DUNGEON_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x025B.md](server-to-client/NOTI-0x025B.md) |
| `0x025C` | `DUNGEON_CLEAR_LIMIT_TIME_START` | 布局部分可得 | `≥ 8` | [NOTI-0x025C.md](server-to-client/NOTI-0x025C.md) |
| `0x025F` | `RECENT_FRIEND_LOGIN_INFO` | 布局部分可得 | `≥ 4 + n×3` | [NOTI-0x025F.md](server-to-client/NOTI-0x025F.md) |
| `0x0260` | `PVP_SEASON_REWARD` | 布局部分可得 | `≥ 5` | [NOTI-0x0260.md](server-to-client/NOTI-0x0260.md) |
| `0x0261` | `PVP_SEASON_WARM_UP_FINISH` | 布局部分可得 | `≥ 6` | [NOTI-0x0261.md](server-to-client/NOTI-0x0261.md) |
| `0x0262` | `PVP_COLOSSEUM_CONTEXT` | 布局部分可得 | `≥ 18` | [NOTI-0x0262.md](server-to-client/NOTI-0x0262.md) |
| `0x0263` | `PVP_COLOSSEUM_REWARD` | 布局部分可得 | `≥ 4 + n×12` | [NOTI-0x0263.md](server-to-client/NOTI-0x0263.md) |
| `0x0264` | `DUNGEON_FEVER_TIME` | 布局部分可得 | `≥ 1` | [NOTI-0x0264.md](server-to-client/NOTI-0x0264.md) |
| `0x0265` | `PVP_DISCONNECT_NOTI` | 结构未知 | `—` | [NOTI-0x0265.md](server-to-client/NOTI-0x0265.md) |
| `0x0266` | `MISSION_CAPTURE_OBJECT_START` | 布局部分可得 | `≥ 5` | [NOTI-0x0266.md](server-to-client/NOTI-0x0266.md) |
| `0x0267` | `MISSION_CAPTURE_OBJECT_RESULT` | 布局部分可得 | `≥ 4` | [NOTI-0x0267.md](server-to-client/NOTI-0x0267.md) |
| `0x0268` | `CLEAR_PARTY_BUFF_CARD` | 布局部分可得 | `≥ 5` | [NOTI-0x0268.md](server-to-client/NOTI-0x0268.md) |
| `0x0269` | `GUNNER_2_AWAKENING_EVENT_INFO` | 布局部分可得 | `≥ 7` | [NOTI-0x0269.md](server-to-client/NOTI-0x0269.md) |
| `0x026A` | `CHARAC_REWARD_AS_LEVEL_EVENT_INFO` | 布局已闭合 | `1` | [NOTI-0x026A.md](server-to-client/NOTI-0x026A.md) |
| `0x026B` | `GROWTYPE_EXPERIENCE_EVENT_INFO` | 布局已闭合 | `1` | [NOTI-0x026B.md](server-to-client/NOTI-0x026B.md) |
| `0x026D` | `EVENT_END` | 布局已闭合 | `2` | [NOTI-0x026D.md](server-to-client/NOTI-0x026D.md) |
| `0x0270` | `LEVEL_AIMING_EVENT_INFO` | 布局部分可得 | `≥ 12` | [NOTI-0x0270.md](server-to-client/NOTI-0x0270.md) |
| `0x0271` | `ILLUSION_UPGRADE_EVENT_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x0271.md](server-to-client/NOTI-0x0271.md) |
| `0x0272` | `ILLUSION_UPGRADE_STATE` | 布局部分可得 | `≥ 5` | [NOTI-0x0272.md](server-to-client/NOTI-0x0272.md) |
| `0x0273` | `CLEAR_RAID_BUFF` | 结构未知 | `—` | [NOTI-0x0273.md](server-to-client/NOTI-0x0273.md) |
| `0x0274` | `QUEST_DUNGEON_CLEAR_COUNT` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x0274.md](server-to-client/NOTI-0x0274.md) |
| `0x0276` | `DUNGEON_RESTRICT_EVENT_INFO` | 结构未知 | `—` | [NOTI-0x0276.md](server-to-client/NOTI-0x0276.md) |
| `0x0278` | `CRACK_OF_DIMENSION` | 布局部分可得 | `≥ 4` | [NOTI-0x0278.md](server-to-client/NOTI-0x0278.md) |
| `0x0279` | `CRACK_DAILY_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0279.md](server-to-client/NOTI-0x0279.md) |
| `0x027A` | `RAID_BUFF_SYSTEM` | 布局部分可得 | `≥ 15` | [NOTI-0x027A.md](server-to-client/NOTI-0x027A.md) |
| `0x027B` | `RAID_MONSTER_HP` | 布局部分可得 | `≥ 11 + n×4` | [NOTI-0x027B.md](server-to-client/NOTI-0x027B.md) |
| `0x027F` | `EVENT_NPC_DYNAMIC_DISPLAY` | 布局部分可得 | `≥ 1` | [NOTI-0x027F.md](server-to-client/NOTI-0x027F.md) |
| `0x0282` | `LINKED_DUNGEON_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0282.md](server-to-client/NOTI-0x0282.md) |
| `0x0284` | `PAYBACK_CHARAC_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0284.md](server-to-client/NOTI-0x0284.md) |
| `0x0285` | `PAYBACK_CHARAC_REWARD` | 布局部分可得 | `≥ 1` | [NOTI-0x0285.md](server-to-client/NOTI-0x0285.md) |
| `0x0286` | `DAILY_CHALLENGE` | 布局部分可得 | `≥ 28` | [NOTI-0x0286.md](server-to-client/NOTI-0x0286.md) |
| `0x0287` | `DAILY_CHALLENGE_CLEAR_DUNGEON` | 布局已闭合 | `4` | [NOTI-0x0287.md](server-to-client/NOTI-0x0287.md) |
| `0x0288` | `DIMENSION_DUNGEON_STUDY_EVENT_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0288.md](server-to-client/NOTI-0x0288.md) |
| `0x0289` | `HONOR_LEVEL_INFO` | **手写记录** | 见文档 | [NOTI-0x0289.md](server-to-client/NOTI-0x0289.md) **(手写)** |
| `0x028A` | `RAID_INOUT_SYSTEM` | 布局部分可得 | `≥ 4` | [NOTI-0x028A.md](server-to-client/NOTI-0x028A.md) |
| `0x028B` | `FARM_EVENT_INFO` | 布局部分可得 | `≥ 9 + n×3` | [NOTI-0x028B.md](server-to-client/NOTI-0x028B.md) |
| `0x028C` | `USE_COIN_LIMIT_EVENT_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x028C.md](server-to-client/NOTI-0x028C.md) |
| `0x028F` | `ITEMTRADE_REG_ITEM_FINISH` | 结构未知 | `—` | [NOTI-0x028F.md](server-to-client/NOTI-0x028F.md) |
| `0x0291` | `DUNGEON_VISIT_MAZE` | 布局部分可得 | `≥ 16` | [NOTI-0x0291.md](server-to-client/NOTI-0x0291.md) |
| `0x0292` | `ENTRY_PARTY_WAIT` | 布局部分可得 | `≥ 2` | [NOTI-0x0292.md](server-to-client/NOTI-0x0292.md) |
| `0x0293` | `ENTRY_INTO_PARTY_FINISH` | 布局部分可得 | `≥ 10` | [NOTI-0x0293.md](server-to-client/NOTI-0x0293.md) |
| `0x0294` | `ENTRY_INTO_PARTY_UPDATE` | 布局部分可得 | `≥ 9` | [NOTI-0x0294.md](server-to-client/NOTI-0x0294.md) |
| `0x0297` | `REAL_ESTATE_START` | 布局部分可得 | `≥ 4` | [NOTI-0x0297.md](server-to-client/NOTI-0x0297.md) |
| `0x0298` | `REAL_ESTATE_USER_STATE` | 布局部分可得 | `≥ 3` | [NOTI-0x0298.md](server-to-client/NOTI-0x0298.md) |
| `0x0299` | `REAL_ESTATE_MEMBER_INFO` | 布局部分可得 | `≥ 5 + n×4` | [NOTI-0x0299.md](server-to-client/NOTI-0x0299.md) |
| `0x029A` | `REAL_ESTATE_MAP_INFO` | 布局部分可得 | `≥ 9 + n×5` | [NOTI-0x029A.md](server-to-client/NOTI-0x029A.md) |
| `0x029B` | `REAL_ESTATE_REMAIN_TIME` | 布局部分可得 | `≥ 4` | [NOTI-0x029B.md](server-to-client/NOTI-0x029B.md) |
| `0x029C` | `REAL_ESTATE_STATE` | 布局部分可得 | `≥ 1` | [NOTI-0x029C.md](server-to-client/NOTI-0x029C.md) |
| `0x029D` | `REAL_ESTATE_USER_SCORE` | 布局部分可得 | `≥ 6` | [NOTI-0x029D.md](server-to-client/NOTI-0x029D.md) |
| `0x029E` | `PCROOM_CHOICE_AND_FOCUS` | 布局部分可得 | `≥ 19` | [NOTI-0x029E.md](server-to-client/NOTI-0x029E.md) |
| `0x02A0` | `SHOP_PURCHASE_COUNT` | 结构未知 | `—` | [NOTI-0x02A0.md](server-to-client/NOTI-0x02A0.md) |
| `0x02A2` | `UDP_PACKET_COLLECT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x02A2.md](server-to-client/NOTI-0x02A2.md) |
| `0x02A4` | `REAL_ESTATE_GOLD_CARD_INFO` | 布局部分可得 | `≥ 1 + n×3` | [NOTI-0x02A4.md](server-to-client/NOTI-0x02A4.md) |
| `0x02A5` | `REAL_ESTATE_TAX_INFO` | 结构未知 | `—` | [NOTI-0x02A5.md](server-to-client/NOTI-0x02A5.md) |
| `0x02A6` | `REAL_ESTATE_REWARD_INFO` | 布局部分可得 | `≥ 1 + n×14` | [NOTI-0x02A6.md](server-to-client/NOTI-0x02A6.md) |
| `0x02A7` | `HELL_PARTY_MONSTER_INFO` | 布局部分可得 | `≥ 4 + n×8` | [NOTI-0x02A7.md](server-to-client/NOTI-0x02A7.md) |
| `0x02A8` | `DISCONN_DUNGEON_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x02A8.md](server-to-client/NOTI-0x02A8.md) |
| `0x02A9` | `REJOIN_DUNGEON` | 布局部分可得 | `≥ 2` | [NOTI-0x02A9.md](server-to-client/NOTI-0x02A9.md) |
| `0x02AA` | `CANCEL_REJOIN_DUNGEON` | 布局部分可得 | `≥ 2` | [NOTI-0x02AA.md](server-to-client/NOTI-0x02AA.md) |
| `0x02AB` | `REJOINABLE_DUNGEON` | 布局部分可得 | `≥ 4` | [NOTI-0x02AB.md](server-to-client/NOTI-0x02AB.md) |
| `0x02AC` | `PVP_CLEAR_COMBOMISSION_LIST` | 布局部分可得 | `≥ 3 + n×1` | [NOTI-0x02AC.md](server-to-client/NOTI-0x02AC.md) |
| `0x02AD` | `REQUEST_GUILD_CREATE_PERMIT` | 结构未知 | `—` | [NOTI-0x02AD.md](server-to-client/NOTI-0x02AD.md) |
| `0x02AE` | `REPLY_GUILD_CREATE_PERMIT` | 布局部分可得 | `≥ 1` | [NOTI-0x02AE.md](server-to-client/NOTI-0x02AE.md) |
| `0x02AF` | `CANCEL_GUILD_CREATE` | 结构未知 | `—` | [NOTI-0x02AF.md](server-to-client/NOTI-0x02AF.md) |
| `0x02B0` | `EVENT_DUNGEON_ENTER_COUNT_INFO` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x02B0.md](server-to-client/NOTI-0x02B0.md) |
| `0x02B1` | `TERRITORY_COMBAT_RESPAWN` | 布局部分可得 | `≥ 25` | [NOTI-0x02B1.md](server-to-client/NOTI-0x02B1.md) |
| `0x02B2` | `TERRITORY_COMBAT_SYMBOL` | 布局部分可得 | `≥ 5` | [NOTI-0x02B2.md](server-to-client/NOTI-0x02B2.md) |
| `0x02B3` | `TERRITORY_COMBAT_SKILL` | 布局部分可得 | `≥ 4 + n×12` | [NOTI-0x02B3.md](server-to-client/NOTI-0x02B3.md) |
| `0x02B4` | `TERRITORY_COMBAT_ENTRY_DUNGEON_WAIT` | 结构未知 | `—` | [NOTI-0x02B4.md](server-to-client/NOTI-0x02B4.md) |
| `0x02B5` | `TERRITORY_COMBAT_HP` | 布局部分可得 | `≥ 4 + n×6` | [NOTI-0x02B5.md](server-to-client/NOTI-0x02B5.md) |
| `0x02B6` | `TERRITORY_COMBAT_SUPPLY_ITEM` | 布局部分可得 | `≥ 0 + n×4` | [NOTI-0x02B6.md](server-to-client/NOTI-0x02B6.md) |
| `0x02B7` | `TERRITORY_COMBAT_REQUEST_SUPPLY_ITEM` | 布局部分可得 | `≥ 4` | [NOTI-0x02B7.md](server-to-client/NOTI-0x02B7.md) |
| `0x02B8` | `TERRITORY_COMBAT_CHANGE_ROLE` | 布局部分可得 | `≥ 4` | [NOTI-0x02B8.md](server-to-client/NOTI-0x02B8.md) |
| `0x02B9` | `TERRITORY_COMBAT_SECRET_PATH` | 布局部分可得 | `≥ 8` | [NOTI-0x02B9.md](server-to-client/NOTI-0x02B9.md) |
| `0x02BA` | `TERRITORY_COMBAT_REQ_MOVE_DUNGEON` | 布局部分可得 | `≥ 8` | [NOTI-0x02BA.md](server-to-client/NOTI-0x02BA.md) |
| `0x02BB` | `TERRITORY_COMBAT_WAIT_ROOM` | 布局部分可得 | `≥ 4` | [NOTI-0x02BB.md](server-to-client/NOTI-0x02BB.md) |
| `0x02BC` | `TERRITORY_COMBAT_NOTICE` | 布局部分可得 | `≥ 1` | [NOTI-0x02BC.md](server-to-client/NOTI-0x02BC.md) |
| `0x02BD` | `TERRITORY_COMBAT_SCORE` | 布局部分可得 | `≥ 1027` | [NOTI-0x02BD.md](server-to-client/NOTI-0x02BD.md) |
| `0x02BE` | `TERRITORY_COMBAT_ACQUIRE_SUPPLY_ITEM` | 布局部分可得 | `≥ 14` | [NOTI-0x02BE.md](server-to-client/NOTI-0x02BE.md) |
| `0x02BF` | `STORY_BOOK_INFO` | 布局部分可得 | `≥ 4 + n×48` | [NOTI-0x02BF.md](server-to-client/NOTI-0x02BF.md) |
| `0x02C1` | `PUZZLE_EVENT_INFO` | 布局部分可得 | `—` | [NOTI-0x02C1.md](server-to-client/NOTI-0x02C1.md) |
| `0x02C2` | `CASH_SHOP_PURCHASE_LIMIT_INFO` | 布局部分可得 | `≥ 2 + n×8` | [NOTI-0x02C2.md](server-to-client/NOTI-0x02C2.md) |
| `0x02C3` | `RENT_GUILD_SUPPORT_CHARAC` | 布局部分可得 | `≥ 65 + n1×4 + n2×4` | [NOTI-0x02C3.md](server-to-client/NOTI-0x02C3.md) |
| `0x02C4` | `LIST_GUILD_SUPPORT_CHARAC` | 布局部分可得 | `≥ 19 + n×13` | [NOTI-0x02C4.md](server-to-client/NOTI-0x02C4.md) |
| `0x02C5` | `GUILD_SUPPORT_CHARAC_RENT_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x02C5.md](server-to-client/NOTI-0x02C5.md) |
| `0x02C8` | `TERRITORY_COMBAT_CLEAR_DUNGEON` | 布局部分可得 | `≥ 5` | [NOTI-0x02C8.md](server-to-client/NOTI-0x02C8.md) |
| `0x02CA` | `TERRITORY_COMBAT_DECLARATION_LIST` | 布局部分可得 | `≥ 9` | [NOTI-0x02CA.md](server-to-client/NOTI-0x02CA.md) |
| `0x02CD` | `PROPER_DUNGEON_CLEAR_EVENT_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x02CD.md](server-to-client/NOTI-0x02CD.md) |
| `0x02CE` | `SERVER_CHARACTER_LIST` | 布局部分可得 | `≥ 2 + n×4` | [NOTI-0x02CE.md](server-to-client/NOTI-0x02CE.md) |
| `0x02D0` | `DEVELOPER_CT_EVENT_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x02D0.md](server-to-client/NOTI-0x02D0.md) |
| `0x02D2` | `NEW_GROWTH_WEAPON_EVENT_INFO` | 布局部分可得 | `≥ 9` | [NOTI-0x02D2.md](server-to-client/NOTI-0x02D2.md) |
| `0x02D3` | `TERRITORY_COMBAT_INFO_TREASURE_HOUSE` | 布局已闭合 | `2` | [NOTI-0x02D3.md](server-to-client/NOTI-0x02D3.md) |
| `0x02D4` | `TERRITORY_GUILD_INFO` | 布局部分可得 | `≥ 7` | [NOTI-0x02D4.md](server-to-client/NOTI-0x02D4.md) |
| `0x02D5` | `ACCOUNT_TUTORIAL_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x02D5.md](server-to-client/NOTI-0x02D5.md) |
| `0x02D8` | `GROWTYPE_CHANGE_JOB_INFO` | 布局已闭合 | `1` | [NOTI-0x02D8.md](server-to-client/NOTI-0x02D8.md) |
| `0x02DD` | `CHARAC_NAME_CHANGE_LOG` | 布局部分可得 | `≥ 1 + n×5` | [NOTI-0x02DD.md](server-to-client/NOTI-0x02DD.md) |
| `0x02DE` | `SCENARIO_MODE_DIFFICULTY_APPEAR` | 布局部分可得 | `≥ 1` | [NOTI-0x02DE.md](server-to-client/NOTI-0x02DE.md) |
| `0x02DF` | `JUMPING_JUMPING_EVENT_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x02DF.md](server-to-client/NOTI-0x02DF.md) |
| `0x02E0` | `INCUBATING_SYSTEM` | 结构未知 | `—` | [NOTI-0x02E0.md](server-to-client/NOTI-0x02E0.md) |
| `0x02E2` | `TERRITORY_COMBAT_EVENT_INFO` | 布局部分可得 | `≥ 44` | [NOTI-0x02E2.md](server-to-client/NOTI-0x02E2.md) |
| `0x02E3` | `TERRITORY_COMBAT_STATUS` | 布局部分可得 | `≥ 1` | [NOTI-0x02E3.md](server-to-client/NOTI-0x02E3.md) |
| `0x02E5` | `GUILD_DUNGEON_INFO` | 布局部分可得 | `≥ 17 + n×8` | [NOTI-0x02E5.md](server-to-client/NOTI-0x02E5.md) |
| `0x02E6` | `GUILD_DUNGEON_REPEAT_BONUS_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x02E6.md](server-to-client/NOTI-0x02E6.md) |
| `0x02E7` | `NPC_FAVOR_POINT_INFO` | 布局部分可得 | `≥ 1 + n×12` | [NOTI-0x02E7.md](server-to-client/NOTI-0x02E7.md) |
| `0x02E8` | `EPIC_PIECE_EXCHANGE_NPC` | 布局部分可得 | `≥ 1` | [NOTI-0x02E8.md](server-to-client/NOTI-0x02E8.md) |
| `0x02E9` | `EPIC_PIECE_EXCHANGE_METERIAL_LIST` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x02E9.md](server-to-client/NOTI-0x02E9.md) |
| `0x02EA` | `RAID_OTHER_CHANNEL_WAITING_MODIFY` | 布局部分可得 | `≥ 40` | [NOTI-0x02EA.md](server-to-client/NOTI-0x02EA.md) |
| `0x02EB` | `RAID_OTHER_CHANNEL_WAITING_LIST` | 布局部分可得 | `≥ 43` | [NOTI-0x02EB.md](server-to-client/NOTI-0x02EB.md) |
| `0x02EC` | `RAID_OTHER_CHANNEL_LIST` | 布局部分可得 | `≥ 4 + n×25` | [NOTI-0x02EC.md](server-to-client/NOTI-0x02EC.md) |
| `0x02ED` | `RAID_OTHER_CHANNEL_USERINFO` | 布局部分可得 | `≥ 52` | [NOTI-0x02ED.md](server-to-client/NOTI-0x02ED.md) |
| `0x02EE` | `RAID_OTHER_CHANNEL_REQUEST_JOIN` | 布局部分可得 | `≥ 50` | [NOTI-0x02EE.md](server-to-client/NOTI-0x02EE.md) |
| `0x02EF` | `RAID_OTHER_CHANNEL_RESPONSE_JOIN` | 布局部分可得 | `≥ 5` | [NOTI-0x02EF.md](server-to-client/NOTI-0x02EF.md) |
| `0x02F0` | `RAID_RECENT_FRIEND_LOGIN_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x02F0.md](server-to-client/NOTI-0x02F0.md) |
| `0x02F1` | `RAID_OTHER_CHANNEL_CHARAC_NAME_CHANGE_LOG` | 布局部分可得 | `≥ 9` | [NOTI-0x02F1.md](server-to-client/NOTI-0x02F1.md) |
| `0x02F2` | `RAID_OTHER_CHANNEL_USER_TITLE_BOOK` | 布局部分可得 | `≥ 5 + n×27` | [NOTI-0x02F2.md](server-to-client/NOTI-0x02F2.md) |
| `0x02F3` | `RAID_OTHER_CHANNEL_USER_PVP_SCORE` | 布局部分可得 | `≥ 70 + n×74` | [NOTI-0x02F3.md](server-to-client/NOTI-0x02F3.md) |
| `0x02F4` | `RAID_MEMBER_CHANGE_STATE` | 布局部分可得 | `≥ 3` | [NOTI-0x02F4.md](server-to-client/NOTI-0x02F4.md) |
| `0x02F5` | `RAID_MEMBERS_STATE` | 布局部分可得 | `≥ 4 + n×3` | [NOTI-0x02F5.md](server-to-client/NOTI-0x02F5.md) |
| `0x02F6` | `RAID_USER_MOVE_CHANNEL_FAIL` | 布局部分可得 | `≥ 40` | [NOTI-0x02F6.md](server-to-client/NOTI-0x02F6.md) |
| `0x02F7` | `CHARAC_DUNGEON_LICENSE_INFO` | 布局部分可得 | `≥ 2 + n×12` | [NOTI-0x02F7.md](server-to-client/NOTI-0x02F7.md) |
| `0x02F8` | `LICENSE_DUNGEON_DAY_INDEX_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x02F8.md](server-to-client/NOTI-0x02F8.md) |
| `0x02F9` | `LICENSE_DUNGEON_CLEAR_INFO` | 布局部分可得 | `≥ 21` | [NOTI-0x02F9.md](server-to-client/NOTI-0x02F9.md) |
| `0x02FA` | `LICENSE_DUNGEON_SHOT_COUNT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x02FA.md](server-to-client/NOTI-0x02FA.md) |
| `0x02FB` | `LICENSE_DUNGEON_INCOUNT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x02FB.md](server-to-client/NOTI-0x02FB.md) |
| `0x02FC` | `ANCIENT_GATE_VALUE` | 布局部分可得 | `≥ 4` | [NOTI-0x02FC.md](server-to-client/NOTI-0x02FC.md) |
| `0x030D` | `ITEM_AVERAGE_PRICE_LIST` | 布局部分可得 | `≥ 3 + n×10` | [NOTI-0x030D.md](server-to-client/NOTI-0x030D.md) |
| `0x030E` | `HELL_PARTY_CLEAR` | 结构未知 | `—` | [NOTI-0x030E.md](server-to-client/NOTI-0x030E.md) |
| `0x030F` | `USE_BINGO_BOX_RESULT` | 布局部分可得 | `≥ 1` | [NOTI-0x030F.md](server-to-client/NOTI-0x030F.md) |
| `0x0310` | `PROPER_DUNGEON_EVENT` | 结构未知 | `—` | [NOTI-0x0310.md](server-to-client/NOTI-0x0310.md) |
| `0x0312` | `CREATE_CHARAC_FIRST_LOGIN` | 结构未知 | `—` | [NOTI-0x0312.md](server-to-client/NOTI-0x0312.md) |
| `0x0313` | `SEASON_CHARAC_INFO` | 布局已闭合 | `60` | [NOTI-0x0313.md](server-to-client/NOTI-0x0313.md) |
| `0x0314` | `ALEADY_RAID_REWARD` | 结构未知 | `—` | [NOTI-0x0314.md](server-to-client/NOTI-0x0314.md) |
| `0x0315` | `ADVENTURER_MAKER_USER_DATA` | 布局部分可得 | `≥ 21 + n×2` | [NOTI-0x0315.md](server-to-client/NOTI-0x0315.md) |
| `0x0316` | `ADVENTURER_MAKER_LEVEL_UP` | 布局部分可得 | `≥ 6 + n×2` | [NOTI-0x0316.md](server-to-client/NOTI-0x0316.md) |
| `0x0319` | `ANTON_AWAKENING_MODE_REWARD` | 布局部分可得 | `≥ 4 + n×15` | [NOTI-0x0319.md](server-to-client/NOTI-0x0319.md) |
| `0x031A` | `PVP_TOTAL_MATCH_CHARACTER_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x031A.md](server-to-client/NOTI-0x031A.md) |
| `0x031B` | `PVP_TOTAL_MATCH_TEAM_INFO` | 布局部分可得 | `≥ 3` | [NOTI-0x031B.md](server-to-client/NOTI-0x031B.md) |
| `0x031C` | `PVP_TOTAL_MATCH_INFO` | 布局部分可得 | `≥ 9 + n×2` | [NOTI-0x031C.md](server-to-client/NOTI-0x031C.md) |
| `0x031D` | `PVP_TOTAL_MATCH_FIGHT` | 布局部分可得 | `≥ 5` | [NOTI-0x031D.md](server-to-client/NOTI-0x031D.md) |
| `0x031E` | `START_SET_PVP_TOTAL_MATCH_CHARAC_ORDER` | 结构未知 | `—` | [NOTI-0x031E.md](server-to-client/NOTI-0x031E.md) |
| `0x0320` | `PVP_TOTAL_MATCH_SCORE` | 布局部分可得 | `≥ 31 + n×14` | [NOTI-0x0320.md](server-to-client/NOTI-0x0320.md) |
| `0x0321` | `PVP_TOTAL_MATCH_CHARAC_OPTION` | 布局部分可得 | `—` | [NOTI-0x0321.md](server-to-client/NOTI-0x0321.md) |
| `0x0322` | `PVP_TOTAL_MATCH_CHARAC_SKILL_COMMAND` | 布局部分可得 | `≥ 7` | [NOTI-0x0322.md](server-to-client/NOTI-0x0322.md) |
| `0x0323` | `PVP_TOTAL_MATCH_CHARAC_COMBO_SKILL` | 布局部分可得 | `≥ 1` | [NOTI-0x0323.md](server-to-client/NOTI-0x0323.md) |
| `0x0326` | `DIMENSION_GATE_ENTRANCE_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0326.md](server-to-client/NOTI-0x0326.md) |
| `0x0327` | `RAID_MEMBERS` | 布局部分可得 | `≥ 5` | [NOTI-0x0327.md](server-to-client/NOTI-0x0327.md) |
| `0x0328` | `RAID_DISCONN_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x0328.md](server-to-client/NOTI-0x0328.md) |
| `0x0329` | `EVENT_ONTIME` | 布局部分可得 | `≥ 5` | [NOTI-0x0329.md](server-to-client/NOTI-0x0329.md) |
| `0x032A` | `EVENT_DNFTREND_STATE` | 布局部分可得 | `≥ 14` | [NOTI-0x032A.md](server-to-client/NOTI-0x032A.md) |
| `0x032B` | `EVENT_DNFTREND_REWARD_STATE` | 布局部分可得 | `≥ 1` | [NOTI-0x032B.md](server-to-client/NOTI-0x032B.md) |
| `0x032C` | `EVENT_DNFTREND_ALL_REWARD_STATE` | 布局部分可得 | `≥ 24` | [NOTI-0x032C.md](server-to-client/NOTI-0x032C.md) |
| `0x032D` | `AUTO_USE_SPECIAL_BUFF_ITEM_USE_ITEM` | 布局部分可得 | `≥ 4` | [NOTI-0x032D.md](server-to-client/NOTI-0x032D.md) |
| `0x0331` | `LIMIT_IPGNO` | 布局部分可得 | `≥ 1 + n×4` | [NOTI-0x0331.md](server-to-client/NOTI-0x0331.md) |
| `0x0332` | `CHARAC_PREMIUM_NAME_TAG` | 布局部分可得 | `≥ 8` | [NOTI-0x0332.md](server-to-client/NOTI-0x0332.md) |
| `0x0333` | `BOSS_TOWER_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x0333.md](server-to-client/NOTI-0x0333.md) |
| `0x0334` | `START_BOSS_TOWER_MAP` | 布局部分可得 | `≥ 11` | [NOTI-0x0334.md](server-to-client/NOTI-0x0334.md) |
| `0x0335` | `BOSS_TOWER_STATE_REWARD` | 布局部分可得 | `≥ 4 + n×9` | [NOTI-0x0335.md](server-to-client/NOTI-0x0335.md) |
| `0x0336` | `BOSS_TOWER_STATE_EPLP` | 布局部分可得 | `≥ 1` | [NOTI-0x0336.md](server-to-client/NOTI-0x0336.md) |
| `0x033B` | `CON_VALENTINE_EVENT` | 布局已闭合 | `4` | [NOTI-0x033B.md](server-to-client/NOTI-0x033B.md) |
| `0x033C` | `CERASHOP_BONUS_ITEM` | 布局部分可得 | `≥ 13` | [NOTI-0x033C.md](server-to-client/NOTI-0x033C.md) |
| `0x033D` | `PUNISH_CHATTING` | 布局部分可得 | `≥ 4` | [NOTI-0x033D.md](server-to-client/NOTI-0x033D.md) |
| `0x033F` | `DAILYSCHEDULE_CONTENTS_STATE` | 布局部分可得 | `≥ 16` | [NOTI-0x033F.md](server-to-client/NOTI-0x033F.md) |
| `0x0340` | `SAFE_MODE` | 布局部分可得 | `≥ 1` | [NOTI-0x0340.md](server-to-client/NOTI-0x0340.md) |
| `0x0342` | `LIMIT_ITEM_INFO` | 布局部分可得 | `≥ 12` | [NOTI-0x0342.md](server-to-client/NOTI-0x0342.md) |
| `0x0343` | `SERVER_BROADCAST_TIME_MESSAGE` | 布局部分可得 | `≥ 4` | [NOTI-0x0343.md](server-to-client/NOTI-0x0343.md) |
| `0x0344` | `BUY_RESTRICT_ITEM_LIST` | **手写记录** | 见文档 | [NOTI-0x0344.md](server-to-client/NOTI-0x0344.md) **(手写)** |
| `0x0353` | `PERSONAL_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0353.md](server-to-client/NOTI-0x0353.md) |
| `0x035B` | `ADVANCE_ALTAR_STAGE_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x035B.md](server-to-client/NOTI-0x035B.md) |
| `0x035C` | `ADVANCE_ALTAR_START_MAP` | 布局部分可得 | `≥ 24 + n×12` | [NOTI-0x035C.md](server-to-client/NOTI-0x035C.md) |
| `0x035D` | `ADVANCE_ALTAR_SPAWN_UNIT` | 布局部分可得 | `≥ 38` | [NOTI-0x035D.md](server-to-client/NOTI-0x035D.md) |
| `0x035E` | `ADVANCE_ALTAR_UPDATE_STAGE_CLEAR` | 结构未知 | `—` | [NOTI-0x035E.md](server-to-client/NOTI-0x035E.md) |
| `0x035F` | `ADVANCE_ALTAR_TEST_CHECK_GAGE` | 布局已闭合 | `8` | [NOTI-0x035F.md](server-to-client/NOTI-0x035F.md) |
| `0x0360` | `ADVANCE_ALTAR_EPLP_STATE` | 布局部分可得 | `≥ 5` | [NOTI-0x0360.md](server-to-client/NOTI-0x0360.md) |
| `0x0361` | `ADVANCE_ALTAR_CLEAR_STAGE` | 布局部分可得 | `≥ 37 + n1×12 + n2×12 + n3×5` | [NOTI-0x0361.md](server-to-client/NOTI-0x0361.md) |
| `0x0362` | `ADVANCE_ALTAR_UPDATE_RIDABLE` | 布局部分可得 | `≥ 62` | [NOTI-0x0362.md](server-to-client/NOTI-0x0362.md) |
| `0x0366` | `EVENT_LOGIN_AND_GIFT` | 布局已闭合 | `4` | [NOTI-0x0366.md](server-to-client/NOTI-0x0366.md) |
| `0x0367` | `EVENT_PLANT_TREE` | 布局已闭合 | `4` | [NOTI-0x0367.md](server-to-client/NOTI-0x0367.md) |
| `0x0368` | `READY_USER_INFO` | 布局部分可得 | `≥ 1 + n×9` | [NOTI-0x0368.md](server-to-client/NOTI-0x0368.md) |
| `0x0369` | `CANCEL_USER` | 布局部分可得 | `≥ 4` | [NOTI-0x0369.md](server-to-client/NOTI-0x0369.md) |
| `0x036A` | `CONTRACT_OF_CUBE_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x036A.md](server-to-client/NOTI-0x036A.md) |
| `0x036C` | `MISS_TITLE` | 布局部分可得 | `≥ 2` | [NOTI-0x036C.md](server-to-client/NOTI-0x036C.md) |
| `0x036E` | `SUCCESS_CHAMPION_BREAK_SYSTEM` | 结构未知 | `—` | [NOTI-0x036E.md](server-to-client/NOTI-0x036E.md) |
| `0x036F` | `PVP_TOURNAMENT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x036F.md](server-to-client/NOTI-0x036F.md) |
| `0x0371` | `CHANNEL_MOVE_FOR_MATCHING` | 布局部分可得 | `≥ 17` | [NOTI-0x0371.md](server-to-client/NOTI-0x0371.md) |
| `0x0375` | `DUNGEON_MISSION_TIMER_ACTION` | 布局部分可得 | `≥ 6` | [NOTI-0x0375.md](server-to-client/NOTI-0x0375.md) |
| `0x0376` | `CHRISTMAS_PACKAGES_PURCHASE_COUNT` | 布局部分可得 | `≥ 2` | [NOTI-0x0376.md](server-to-client/NOTI-0x0376.md) |
| `0x0377` | `PREMIUM_EXPIRE_NOTI` | 布局部分可得 | `≥ 4 + n×8` | [NOTI-0x0377.md](server-to-client/NOTI-0x0377.md) |
| `0x0383` | `FIRST_TIME_LOGIN` | 结构未知 | `—` | [NOTI-0x0383.md](server-to-client/NOTI-0x0383.md) |
| `0x0385` | `UPDATE_ITEM_LIST_GOLDEFFECT` | 布局部分可得 | `≥ 104` | [NOTI-0x0385.md](server-to-client/NOTI-0x0385.md) |
| `0x0386` | `HIT_BALL_SEED` | 布局部分可得 | `≥ 4` | [NOTI-0x0386.md](server-to-client/NOTI-0x0386.md) |
| `0x038A` | `PUNISH_SHOW_TIPS` | 布局部分可得 | `—` | [NOTI-0x038A.md](server-to-client/NOTI-0x038A.md) |
| `0x038C` | `INSPECTION_NOTICE` | 布局部分可得 | `≥ 2` | [NOTI-0x038C.md](server-to-client/NOTI-0x038C.md) |
| `0x038D` | `REQUEST_MOVING_CHANNEL_SELECTING_SCENE` | 结构未知 | `—` | [NOTI-0x038D.md](server-to-client/NOTI-0x038D.md) |
| `0x038E` | `SP_TEAM_INFO` | 布局部分可得 | `≥ 12` | [NOTI-0x038E.md](server-to-client/NOTI-0x038E.md) |
| `0x038F` | `UPDATE_SP_TEAM_INFO` | 布局部分可得 | `≥ 7` | [NOTI-0x038F.md](server-to-client/NOTI-0x038F.md) |
| `0x0391` | `OBJECT_DESTROY_REWARD` | 布局部分可得 | `≥ 324` | [NOTI-0x0391.md](server-to-client/NOTI-0x0391.md) |
| `0x0393` | `PUZZLE_STATE_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x0393.md](server-to-client/NOTI-0x0393.md) |
| `0x0396` | `FIRST_LOGIN_INFO` | 布局部分可得 | `≥ 2` | [NOTI-0x0396.md](server-to-client/NOTI-0x0396.md) |
| `0x039A` | `USE_GOLD_COUNT_FOR_PVP_SKILL` | 布局已闭合 | `4` | [NOTI-0x039A.md](server-to-client/NOTI-0x039A.md) |
| `0x039B` | `UPGRADE_CARRY_GOLD` | 布局部分可得 | `≥ 1` | [NOTI-0x039B.md](server-to-client/NOTI-0x039B.md) |
| `0x03A2` | `USER_CHOICE_INFO` | 布局部分可得 | `≥ 11` | [NOTI-0x03A2.md](server-to-client/NOTI-0x03A2.md) |
| `0x03A3` | `EVENT_RETURN_USER_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x03A3.md](server-to-client/NOTI-0x03A3.md) |
| `0x03A4` | `EVENT_ATTENDANCE_DATA` | 布局部分可得 | `≥ 6` | [NOTI-0x03A4.md](server-to-client/NOTI-0x03A4.md) |
| `0x03A5` | `QQ_PCROOM_BENEFIT_DATA` | 布局部分可得 | `≥ 6` | [NOTI-0x03A5.md](server-to-client/NOTI-0x03A5.md) |
| `0x03BD` | `MONSTER_CATCH` | 布局部分可得 | `≥ 5` | [NOTI-0x03BD.md](server-to-client/NOTI-0x03BD.md) |
| `0x03C0` | `EVENT_MOON_FESTIVAL_DATA` | 布局部分可得 | `≥ 7` | [NOTI-0x03C0.md](server-to-client/NOTI-0x03C0.md) |
| `0x03C1` | `EQUIPMENT_RENTAL_LIST` | 布局部分可得 | `≥ 8 + n×8` | [NOTI-0x03C1.md](server-to-client/NOTI-0x03C1.md) |
| `0x03C2` | `FIRST_BUY` | 布局部分可得 | `≥ 4` | [NOTI-0x03C2.md](server-to-client/NOTI-0x03C2.md) |
| `0x03C3` | `SUPPORT_ATSWORD_LEVEL_UP_ATTANDANCE_STATUS` | 布局部分可得 | `≥ 44` | [NOTI-0x03C3.md](server-to-client/NOTI-0x03C3.md) |
| `0x03C4` | `SUPPORT_ATSWORD_LEVEL_UP_FULL_LEVEL_REWARD` | 布局部分可得 | `≥ 16` | [NOTI-0x03C4.md](server-to-client/NOTI-0x03C4.md) |
| `0x03C5` | `CHAIN_LETTERS_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x03C5.md](server-to-client/NOTI-0x03C5.md) |
| `0x03C6` | `RECEIVE_CHAIN_LETTERS` | 布局部分可得 | `≥ 4` | [NOTI-0x03C6.md](server-to-client/NOTI-0x03C6.md) |
| `0x03C7` | `REVENUE_SANCTION` | 布局部分可得 | `—` | [NOTI-0x03C7.md](server-to-client/NOTI-0x03C7.md) |
| `0x03C9` | `DNF_WITH_FRIENDS_INFO` | 布局部分可得 | `≥ 49` | [NOTI-0x03C9.md](server-to-client/NOTI-0x03C9.md) |
| `0x03CB` | `DNF_WITH_FRIENDS_LOGIN_COUNT_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x03CB.md](server-to-client/NOTI-0x03CB.md) |
| `0x03CC` | `DNF_WITH_FRIENDS_BUY_COUNT_INFO` | 布局部分可得 | `≥ 16` | [NOTI-0x03CC.md](server-to-client/NOTI-0x03CC.md) |
| `0x03CD` | `WEDDING_REQUEST` | 布局部分可得 | `≥ 11` | [NOTI-0x03CD.md](server-to-client/NOTI-0x03CD.md) |
| `0x03CE` | `WEDDING_RESPONSE` | 布局部分可得 | `≥ 12` | [NOTI-0x03CE.md](server-to-client/NOTI-0x03CE.md) |
| `0x03CF` | `WEDDING_AVATAR` | 布局部分可得 | `≥ 67` | [NOTI-0x03CF.md](server-to-client/NOTI-0x03CF.md) |
| `0x03D0` | `WEDDING_INFO` | 布局部分可得 | `≥ 15` | [NOTI-0x03D0.md](server-to-client/NOTI-0x03D0.md) |
| `0x03D1` | `WEDDING_UPGRADE_RING` | 布局部分可得 | `≥ 1` | [NOTI-0x03D1.md](server-to-client/NOTI-0x03D1.md) |
| `0x03D2` | `WEDDING_DIVORCE` | 布局部分可得 | `≥ 1` | [NOTI-0x03D2.md](server-to-client/NOTI-0x03D2.md) |
| `0x03D3` | `CHARAC_LOVE_POINT` | 布局部分可得 | `≥ 8` | [NOTI-0x03D3.md](server-to-client/NOTI-0x03D3.md) |
| `0x03D4` | `WEDDING_CEREMONY` | 布局部分可得 | `≥ 79` | [NOTI-0x03D4.md](server-to-client/NOTI-0x03D4.md) |
| `0x03D5` | `WEDDING_LETTER` | 布局部分可得 | `≥ 78` | [NOTI-0x03D5.md](server-to-client/NOTI-0x03D5.md) |
| `0x03D6` | `WEDDING_INVITED_GUESTS` | 布局部分可得 | `≥ 1 + n×4` | [NOTI-0x03D6.md](server-to-client/NOTI-0x03D6.md) |
| `0x03D7` | `WEDDING_CEREMONY_BEGIN` | 布局部分可得 | `≥ 68` | [NOTI-0x03D7.md](server-to-client/NOTI-0x03D7.md) |
| `0x03D8` | `COUPLE_ROOM` | 布局部分可得 | `≥ 17 + n×6` | [NOTI-0x03D8.md](server-to-client/NOTI-0x03D8.md) |
| `0x03D9` | `COUPLE_INVEN` | 布局部分可得 | `≥ 8 + n×10` | [NOTI-0x03D9.md](server-to-client/NOTI-0x03D9.md) |
| `0x03DA` | `COUPLE_ROOM_UPDATE` | 布局部分可得 | `≥ 23` | [NOTI-0x03DA.md](server-to-client/NOTI-0x03DA.md) |
| `0x03DB` | `COUPLE_ROOM_REFRESH` | 布局部分可得 | `≥ 15` | [NOTI-0x03DB.md](server-to-client/NOTI-0x03DB.md) |
| `0x03DC` | `COUPLE_TREE` | 布局部分可得 | `≥ 13` | [NOTI-0x03DC.md](server-to-client/NOTI-0x03DC.md) |
| `0x03DD` | `COUPLE_SIMPLE_INFO` | 布局部分可得 | `≥ 14` | [NOTI-0x03DD.md](server-to-client/NOTI-0x03DD.md) |
| `0x03DE` | `COUPLE_TREE_HARVEST_REWARD` | 布局部分可得 | `≥ 2 + n×8` | [NOTI-0x03DE.md](server-to-client/NOTI-0x03DE.md) |
| `0x03DF` | `COUPLE_GROWTH_CAPSULE` | 布局部分可得 | `≥ 7` | [NOTI-0x03DF.md](server-to-client/NOTI-0x03DF.md) |
| `0x03E0` | `COUPLE_GROWTH_CAPSULE_GAGE` | 布局部分可得 | `≥ 4` | [NOTI-0x03E0.md](server-to-client/NOTI-0x03E0.md) |
| `0x03E1` | `LET_ME_HELP_YOU_FRIEND` | 布局部分可得 | `≥ 8` | [NOTI-0x03E1.md](server-to-client/NOTI-0x03E1.md) |
| `0x03E3` | `EVENT_GROWTH_EQUIPMENT` | 布局部分可得 | `≥ 24` | [NOTI-0x03E3.md](server-to-client/NOTI-0x03E3.md) |
| `0x03E9` | `SAO_CURRENT_FLOOR` | 布局部分可得 | `≥ 8` | [NOTI-0x03E9.md](server-to-client/NOTI-0x03E9.md) |
| `0x03EA` | `SAO_CLEAR_REWARD` | 布局部分可得 | `≥ 6` | [NOTI-0x03EA.md](server-to-client/NOTI-0x03EA.md) |
| `0x03EB` | `COLLECT_BOX` | 布局部分可得 | `≥ 12` | [NOTI-0x03EB.md](server-to-client/NOTI-0x03EB.md) |
| `0x03F3` | `BUY_COUNT_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x03F3.md](server-to-client/NOTI-0x03F3.md) |
| `0x03F5` | `DRAGON_DUNGEON_CHANNEL_BUFF_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x03F5.md](server-to-client/NOTI-0x03F5.md) |
| `0x03F6` | `SLOT_MACHINE_INFO` | 布局部分可得 | `≥ 6` | [NOTI-0x03F6.md](server-to-client/NOTI-0x03F6.md) |
| `0x03F7` | `SLOT_MACHINE_ADD_CHANCE` | 布局部分可得 | `≥ 1` | [NOTI-0x03F7.md](server-to-client/NOTI-0x03F7.md) |
| `0x03F8` | `EVENT_CHANGE_CLASS` | 布局部分可得 | `≥ 8` | [NOTI-0x03F8.md](server-to-client/NOTI-0x03F8.md) |
| `0x03F9` | `HELP_GROWTH` | 布局部分可得 | `≥ 5` | [NOTI-0x03F9.md](server-to-client/NOTI-0x03F9.md) |
| `0x03FB` | `SKILL_INDEX` | 布局部分可得 | `≥ 1 + n×6` | [NOTI-0x03FB.md](server-to-client/NOTI-0x03FB.md) |
| `0x03FC` | `OLYMPIC_MEDAL_INFO` | 布局部分可得 | `≥ 12` | [NOTI-0x03FC.md](server-to-client/NOTI-0x03FC.md) |
| `0x03FD` | `FISHING_2ND_BUFF_INFO` | 布局部分可得 | `≥ 5` | [NOTI-0x03FD.md](server-to-client/NOTI-0x03FD.md) |
| `0x03FE` | `BUY_COUNT_INFO_SECOND` | 布局已闭合 | `4` | [NOTI-0x03FE.md](server-to-client/NOTI-0x03FE.md) |
| `0x0400` | `RECOVERY_FATIGUE` | 布局部分可得 | `≥ 1` | [NOTI-0x0400.md](server-to-client/NOTI-0x0400.md) |
| `0x0401` | `RECOVERY_FATIGUE_USE` | 布局部分可得 | `≥ 2` | [NOTI-0x0401.md](server-to-client/NOTI-0x0401.md) |
| `0x0402` | `FORTUNE_DATA` | 布局部分可得 | `≥ 10` | [NOTI-0x0402.md](server-to-client/NOTI-0x0402.md) |
| `0x0403` | `FORTUNE_BROADCAST` | 布局部分可得 | `≥ 7` | [NOTI-0x0403.md](server-to-client/NOTI-0x0403.md) |
| `0x0404` | `BLUE_HORSE_CHARAC_DATA` | 布局部分可得 | `≥ 6` | [NOTI-0x0404.md](server-to-client/NOTI-0x0404.md) |
| `0x0405` | `ACTIVITY_POINT` | 布局部分可得 | `≥ 13` | [NOTI-0x0405.md](server-to-client/NOTI-0x0405.md) |
| `0x0406` | `ACTIVATE_CHARACTER` | 布局部分可得 | `≥ 12` | [NOTI-0x0406.md](server-to-client/NOTI-0x0406.md) |
| `0x0407` | `USER_APC_INFO_TOS` | 布局部分可得 | `≥ 8` | [NOTI-0x0407.md](server-to-client/NOTI-0x0407.md) |
| `0x0408` | `TOS_CLEAR_REWARD` | 布局部分可得 | `≥ 15` | [NOTI-0x0408.md](server-to-client/NOTI-0x0408.md) |
| `0x040B` | `TERA_PIECE_CLEAR_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x040B.md](server-to-client/NOTI-0x040B.md) |
| `0x040E` | `PHOENIX_WEAPON_EVENT_DATA` | 布局部分可得 | `≥ 4` | [NOTI-0x040E.md](server-to-client/NOTI-0x040E.md) |
| `0x040F` | `FIND_SIGNS_FINAL_REWARD_EVENT_DATA` | 布局部分可得 | `≥ 2` | [NOTI-0x040F.md](server-to-client/NOTI-0x040F.md) |
| `0x0410` | `CELEBRATE_BIG_TRANSITION_EVENT_DATA` | 布局部分可得 | `≥ 5` | [NOTI-0x0410.md](server-to-client/NOTI-0x0410.md) |
| `0x0411` | `GM_ITEM_SIMULATION` | 布局部分可得 | `≥ 16 + n×12` | [NOTI-0x0411.md](server-to-client/NOTI-0x0411.md) |
| `0x041D` | `UPDATE_PVP_SEASON2` | 布局部分可得 | `≥ 4` | [NOTI-0x041D.md](server-to-client/NOTI-0x041D.md) |
| `0x0420` | `PUZZLE_GAME_INFO` | 布局部分可得 | `≥ 14` | [NOTI-0x0420.md](server-to-client/NOTI-0x0420.md) |
| `0x0421` | `ECO_EVENT_RESPONSE` | 布局部分可得 | `≥ 85` | [NOTI-0x0421.md](server-to-client/NOTI-0x0421.md) |
| `0x0429` | `DECREASE_CHARM_ENERGY` | 结构未知 | `—` | [NOTI-0x0429.md](server-to-client/NOTI-0x0429.md) |
| `0x042A` | `EVENT_QUICK_SLOT_INFO` | 布局部分可得 | `≥ 8` | [NOTI-0x042A.md](server-to-client/NOTI-0x042A.md) |
| `0x042B` | `GOLD_SILVER_HORN_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x042B.md](server-to-client/NOTI-0x042B.md) |
| `0x042D` | `EVENT_TOTAL_ATTENDANCE` | 布局部分可得 | `≥ 450` | [NOTI-0x042D.md](server-to-client/NOTI-0x042D.md) |
| `0x042F` | `LEGEND_SWORD_INFO` | 布局部分可得 | `≥ 46` | [NOTI-0x042F.md](server-to-client/NOTI-0x042F.md) |
| `0x0431` | `HELL_PARTY_BOOSTER_GAGE` | 布局部分可得 | `≥ 8` | [NOTI-0x0431.md](server-to-client/NOTI-0x0431.md) |
| `0x0432` | `DUNGEON_ENTER_COUNT_INFO_INACCOUNT` | 布局部分可得 | `≥ 1 + n×8` | [NOTI-0x0432.md](server-to-client/NOTI-0x0432.md) |
| `0x0434` | `EVENT_VALUE_TO_CLIENT` | 布局部分可得 | `≥ 20` | [NOTI-0x0434.md](server-to-client/NOTI-0x0434.md) |
| `0x0435` | `EVENT_ROULETTE_BUFF` | 结构未知 | `—` | [NOTI-0x0435.md](server-to-client/NOTI-0x0435.md) |
| `0x043F` | `BURNING_UP_TO_SPECIFIC_LEVEL` | 布局部分可得 | `≥ 6` | [NOTI-0x043F.md](server-to-client/NOTI-0x043F.md) |
| `0x0440` | `EQUIP_AVATAR_CONDITION_DROP_INFO` | 布局部分可得 | `≥ 4` | [NOTI-0x0440.md](server-to-client/NOTI-0x0440.md) |
| `0x0442` | `INCREASE_CHANCE_LOTTERY_ALL` | 布局部分可得 | `≥ 204` | [NOTI-0x0442.md](server-to-client/NOTI-0x0442.md) |
| `0x0444` | `GOAL_ATTAINMENT` | 布局部分可得 | `≥ 13` | [NOTI-0x0444.md](server-to-client/NOTI-0x0444.md) |
| `0x0445` | `MAHJONG_GAME_COUNT` | 布局部分可得 | `≥ 2` | [NOTI-0x0445.md](server-to-client/NOTI-0x0445.md) |
| `0x0446` | `MAHJONG_GAME_PLAY` | 布局部分可得 | `≥ 72` | [NOTI-0x0446.md](server-to-client/NOTI-0x0446.md) |
| `0x045A` | `BLIND_PURCHASE` | 布局部分可得 | `≥ 21 + n×16` | [NOTI-0x045A.md](server-to-client/NOTI-0x045A.md) |
| `0x045D` | `KARON_LETHE_FREE` | 布局部分可得 | `≥ 17` | [NOTI-0x045D.md](server-to-client/NOTI-0x045D.md) |
| `0x045F` | `ALL_USER_GROWUP` | 布局部分可得 | `≥ 51` | [NOTI-0x045F.md](server-to-client/NOTI-0x045F.md) |
| `0x0460` | `SERVER_CONST_KEY_VALUE` | 布局部分可得 | `≥ 4 + n×13` | [NOTI-0x0460.md](server-to-client/NOTI-0x0460.md) |
| `0x0465` | `EPIC_BOOK_INFO` | **手写记录** | 见文档 | [NOTI-0x0465.md](server-to-client/NOTI-0x0465.md) **(手写)** |
| `0x0466` | `CHARACTER_MANAGE_INFO` | 布局已闭合 | `5` | [NOTI-0x0466.md](server-to-client/NOTI-0x0466.md) |
| `0x046E` | `PVP_EVENT_ACCOUNT` | 布局部分可得 | `≥ 24` | [NOTI-0x046E.md](server-to-client/NOTI-0x046E.md) |
| `0x046F` | `CLEAR_COMBO_WITH_PROLEAGUE` | 布局部分可得 | `≥ 1` | [NOTI-0x046F.md](server-to-client/NOTI-0x046F.md) |
| `0x0474` | `HOT_DEAL` | 布局部分可得 | `≥ 8` | [NOTI-0x0474.md](server-to-client/NOTI-0x0474.md) |
| `0x0482` | `TAU_KINGDOMG_INFO` | 布局部分可得 | `≥ 1` | [NOTI-0x0482.md](server-to-client/NOTI-0x0482.md) |
| `0x0483` | `TERRITORY_COMBAT_LIST_DETAIL` | 布局部分可得 | `≥ 53` | [NOTI-0x0483.md](server-to-client/NOTI-0x0483.md) |
| `0x0484` | `CHECK_TERRITORY_COMBAT_DECIDED_LIST` | 布局已闭合 | `1` | [NOTI-0x0484.md](server-to-client/NOTI-0x0484.md) |
| `0x0487` | `LUCKY_PICK_EVENT` | 布局部分可得 | `≥ 1` | [NOTI-0x0487.md](server-to-client/NOTI-0x0487.md) |
| `0x048A` | `FISHING_EVENT_ATTENDANCE` | 布局部分可得 | `≥ 6` | [NOTI-0x048A.md](server-to-client/NOTI-0x048A.md) |
| `0x0499` | `CARD_GAME_REWARD_DATA` | 布局部分可得 | `≥ 51` | [NOTI-0x0499.md](server-to-client/NOTI-0x0499.md) |
| `0x049A` | `ACCOUNT_DUNGEON_INOUT_COUNT` | 布局部分可得 | `≥ 5` | [NOTI-0x049A.md](server-to-client/NOTI-0x049A.md) |
| `0x049C` | `FRIENDSHIP_HELL_PARTY_INFO` | 布局部分可得 | `≥ 32` | [NOTI-0x049C.md](server-to-client/NOTI-0x049C.md) |
| `0x049D` | `INTEGRATE_EVENT_DATA` | 布局部分可得 | `—` | [NOTI-0x049D.md](server-to-client/NOTI-0x049D.md) |
| `0x049E` | `P1_DATA` | 布局部分可得 | `≥ 292` | [NOTI-0x049E.md](server-to-client/NOTI-0x049E.md) |
| `0x049F` | `P1_DAILY_DATA` | 布局部分可得 | `≥ 15` | [NOTI-0x049F.md](server-to-client/NOTI-0x049F.md) |
| `0x04A0` | `NPC_GROW_UP_USE_ITEM` | 布局部分可得 | `≥ 4` | [NOTI-0x04A0.md](server-to-client/NOTI-0x04A0.md) |
| `0x04A4` | `TERRITORY_COMBAT_COMPULSORY_LEAVING_IN_EXERCISE_MODE` | 结构未知 | `—` | [NOTI-0x04A4.md](server-to-client/NOTI-0x04A4.md) |
| `0x04AD` | `REAL_ESTATE_SHIELD_ITEM` | 布局部分可得 | `≥ 1` | [NOTI-0x04AD.md](server-to-client/NOTI-0x04AD.md) |
| `0x04AE` | `EVENT_REAL_ESTATE_ENTRANCE_COUNT` | 布局部分可得 | `≥ 4` | [NOTI-0x04AE.md](server-to-client/NOTI-0x04AE.md) |
| `0x04B0` | `LUCKY_BALLOON` | 布局部分可得 | `≥ 8` | [NOTI-0x04B0.md](server-to-client/NOTI-0x04B0.md) |
| `0x04B1` | `FOOD_FIGHTER_DUNGEON` | 布局部分可得 | `≥ 4` | [NOTI-0x04B1.md](server-to-client/NOTI-0x04B1.md) |
| `0x04B3` | `VACCINE_TOWER_TIMEOUT` | 结构未知 | `—` | [NOTI-0x04B3.md](server-to-client/NOTI-0x04B3.md) |
| `0x04B8` | `JOUST_STATE` | 布局部分可得 | `≥ 3` | [NOTI-0x04B8.md](server-to-client/NOTI-0x04B8.md) |
| `0x04B9` | `JOUST_INFO` | 布局部分可得 | `≥ 90` | [NOTI-0x04B9.md](server-to-client/NOTI-0x04B9.md) |
| `0x04BA` | `JOUST_BETTING_INFO` | 布局部分可得 | `≥ 46` | [NOTI-0x04BA.md](server-to-client/NOTI-0x04BA.md) |
| `0x04BB` | `JOUST_MATCH_RESULT` | 布局部分可得 | `≥ 31` | [NOTI-0x04BB.md](server-to-client/NOTI-0x04BB.md) |
| `0x04BC` | `TAKABLE_GUILD_HONGBAO_COUNT` | 布局部分可得 | `≥ 8` | [NOTI-0x04BC.md](server-to-client/NOTI-0x04BC.md) |
| `0x04BD` | `RAID_USER_FINISH_LOAD` | 结构未知 | `—` | [NOTI-0x04BD.md](server-to-client/NOTI-0x04BD.md) |
| `0x04BE` | `ITEM_DROP_CONTROL` | 布局部分可得 | `≥ 5` | [NOTI-0x04BE.md](server-to-client/NOTI-0x04BE.md) |
| `0x04BF` | `JEWELRY_BATTLE_STOP` | 布局部分可得 | `≥ 12` | [NOTI-0x04BF.md](server-to-client/NOTI-0x04BF.md) |

### S→C 的 CMD 响应

CMD 的响应与其请求同 type、同属一次交换，因此记在该 type 的 **C→S 文档**内，上表不重复列出。下面是唯一的例外：一条在该约定确立之前就已单独成篇的手写记录，其请求文档反向链接到它。

| Type | 名称 | 文档 |
|---|---|---|
| `0x0001` | `LOGIN` 响应 | [CMD-0x0001.md](server-to-client/CMD-0x0001.md) **(手写)** |

### 客户端不接收、但服务端会发的 NOTI

本索引的表来自**接收注册表普查**，所以只覆盖客户端注册了 handler 的 type。下面这条没有 handler——客户端收到就丢——但 A21 参照服务器在入场批次里发它，本服务端也发，因此它有线上行为可记。

| Type | 名称 | 文档 |
|---|---|---|
| `0x0381` | `USER_BUY_RESTRICT_UPDATE_ITEM_LIST` | [NOTI-0x0381.md](server-to-client/NOTI-0x0381.md) **(手写)** |

## 覆盖与边界

本索引的完整性受两处静态盲区限制，两者都已在各自文档中显式标注：

- **C→S**：被混淆的第二代码区扫不到构包点。84 条 CMD 只在接收侧留下响应 handler，其请求构包器不在 `.text` 中。
- **S→C**：约四分之一的 handler 完全不读 body，无法从指令推出布局；另有一部分取字段调用进入打包区。

方法与证据见 [game-frame](../patterns/game-frame.md) 与 [game-receive](../patterns/game-receive.md)。
