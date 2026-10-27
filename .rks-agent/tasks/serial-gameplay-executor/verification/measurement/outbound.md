# 单条请求对单个连接的出站字节（精确，空闲 Host、仅涉及的连接）

口径：一个连接从这次请求发出到它的栅栏应答到达之间收到的全部帧字节（含 15 字节帧头，不含栅栏应答本身）。请求者用请求后紧跟的步骤栅栏收口；同伴在请求者的栅栏应答之后再发一个栅栏收口。序列取自 TASK-01 的表征步骤。

| 步骤 | 请求 | 连接 | 角色 | 字节 | 帧数 |
|---|---:|---:|---:|---:|---:|
| solo login | LOGIN | solo | 请求者 | 339 | 7 |
| solo create | CREATE_CHARACTER | solo | 请求者 | 134 | 2 |
| solo first select | SELECT_CHARACTER | solo | 请求者 | 39285 | 195 |
| solo return to selection | RETURN_SELECT_CHARACTER | solo | 请求者 | 157 | 3 |
| solo reselect | SELECT_CHARACTER | solo | 请求者 | 39227 | 193 |
| solo return to selection 2 | RETURN_SELECT_CHARACTER | solo | 请求者 | 157 | 3 |
| solo create second | CREATE_CHARACTER | solo | 请求者 | 222 | 2 |
| solo select second | SELECT_CHARACTER | solo | 请求者 | 39376 | 195 |
| leader set user area 1:1 | SET_USER_AREA | leader | 请求者 | 52 | 2 |
| leader set user area 1:1 | SET_USER_AREA | member | 同伴 | 0 | 0 |
| member set user area 1:1 | SET_USER_AREA | member | 请求者 | 308 | 5 |
| member set user area 1:1 | SET_USER_AREA | leader | 同伴 | 283 | 4 |
| leader position | SET_USER_POSITION | leader | 请求者 | 24 | 1 |
| leader position | SET_USER_POSITION | member | 同伴 | 24 | 1 |
| leader invites | REQUEST_PEER | leader | 请求者 | 0 | 0 |
| leader invites | REQUEST_PEER | member | 同伴 | 28 | 1 |
| member accepts | RESPONSE_PEER | member | 请求者 | 212 | 5 |
| member accepts | RESPONSE_PEER | leader | 同伴 | 214 | 5 |
| leader opens dungeon selection | ENTER_SELECT_DUNGEON | leader | 请求者 | 608 | 5 |
| leader opens dungeon selection | ENTER_SELECT_DUNGEON | member | 同伴 | 608 | 5 |
| leader selects dungeon 144 | SELECT_DUNGEON | leader | 请求者 | 1367 | 10 |
| leader selects dungeon 144 | SELECT_DUNGEON | member | 同伴 | 1367 | 10 |
| leader finishes loading | FINISH_LOADING | leader | 请求者 | 36 | 2 |
| leader finishes loading | FINISH_LOADING | member | 同伴 | 0 | 0 |
| member finishes loading | FINISH_LOADING | member | 请求者 | 36 | 2 |
| member finishes loading | FINISH_LOADING | leader | 同伴 | 0 | 0 |
| leader leaves the dungeon | EPLP | leader | 请求者 | 521 | 10 |
| leader leaves the dungeon | EPLP | member | 同伴 | 521 | 10 |
| leader position after run | SET_USER_POSITION | leader | 请求者 | 24 | 1 |
| leader position after run | SET_USER_POSITION | member | 同伴 | 24 | 1 |
| leader opens dungeon selection 2 | ENTER_SELECT_DUNGEON | leader | 请求者 | 608 | 5 |
| leader opens dungeon selection 2 | ENTER_SELECT_DUNGEON | member | 同伴 | 608 | 5 |
| leader selects dungeon 144 again | SELECT_DUNGEON | leader | 请求者 | 1367 | 10 |
| leader selects dungeon 144 again | SELECT_DUNGEON | member | 同伴 | 1367 | 10 |
| leader finishes loading 2 | FINISH_LOADING | leader | 请求者 | 36 | 2 |
| leader finishes loading 2 | FINISH_LOADING | member | 同伴 | 0 | 0 |
| member finishes loading 2 | FINISH_LOADING | member | 请求者 | 36 | 2 |
| member finishes loading 2 | FINISH_LOADING | leader | 同伴 | 0 | 0 |
| leader leaves the dungeon 2 | EPLP | leader | 请求者 | 521 | 10 |
| leader leaves the dungeon 2 | EPLP | member | 同伴 | 521 | 10 |
| member leaves the party | LEAVE_PARTY | member | 请求者 | 100 | 2 |
| member leaves the party | LEAVE_PARTY | leader | 同伴 | 139 | 3 |
| leader invites again | REQUEST_PEER | leader | 请求者 | 0 | 0 |
| leader invites again | REQUEST_PEER | member | 同伴 | 28 | 1 |
| member accepts again | RESPONSE_PEER | member | 请求者 | 212 | 5 |
| member accepts again | RESPONSE_PEER | leader | 同伴 | 214 | 5 |
| leader returns to selection in party | RETURN_SELECT_CHARACTER | leader | 请求者 | 159 | 3 |
| leader returns to selection in party | RETURN_SELECT_CHARACTER | member | 同伴 | 176 | 5 |
| leader reselects | SELECT_CHARACTER | leader | 请求者 | 39231 | 193 |
| leader reselects | SELECT_CHARACTER | member | 同伴 | 0 | 0 |

本次最大值：**39376 字节**，SELECT_CHARACTER（solo select second，请求者 solo）。

**这是新建角色（job 0，只有起始物品）上的实测值，不是选角出站量的上界。**（第 2 轮补注；工具现在会在生成的 outbound.md 里自动写这一段。）
选角序列里有多帧随角色数据增长。例如 ITEM_LIST（NOTI 0x000D）的长度是「5 B ＋ 条目数 × 101 B」
（`protocol/game/server-to-client/NOTI-0x000D.md`），本次的 1520 B 就是 5 ＋ 15 × 101。背包、装扮栏、任务也都会随游玩增长。
所以按这个值取 2 倍（负载下观察到的最大值 39467 B，取 2 倍得 78934 B；第 1 轮观察到 39427 B），只能算 RQ-036 下限的下限。
上界要按各容器的容量推算，或者用容器装满的角色实测（见 T-003 主控方处置）。
