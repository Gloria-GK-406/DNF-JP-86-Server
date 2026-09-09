# 协议清单

本页记录**已在线观察到、正在做或已做完**的协议：各自处于哪一阶段、细节在哪个文档。
细节不写在这里——每行链接到该协议自己的文档。

> **找一条这里没有的协议？** 去 [Game 协议全量索引](game/index.md)。
> 那里覆盖静态普查可见的全部 1410 条协议身份，每条都有官方名与静态分析得出的 body 初稿；
> 它是**地图**，本页是**进度**。两者的关系见[清单的完整性](#清单的完整性)。
>
> **想知道这些协议怎么被串起来用的？** 那是**流程层**，不在本页也不在单条协议文档里：
> [world-entry](patterns/world-entry.md)（选角色到站进城里）、`patterns/flow-*.md`。
> 本页按 type 排列，回答"有哪些、做到哪一步"；流程层按业务顺序排列，回答"怎么用"。
> 四层分界见 [README](README.md#四层每种事实各有一个归处)。

## 阶段

| 阶段 | 含义 |
|---|---|
| `未知` | 已在线观察到报文，但布局或语义尚未确立。可能只有长度、方向和出现时机。 |
| `确定语义` | 布局、字段宽度、条件分支与编码方式已有直接（指令级）证据，且至少一个真实样本可被无损解释。 |
| `实现服务端功能` | 服务端已解码为 typed message 并有消费者，parser/codec 与 handler 有回归测试。 |
| `双向验证结束` | 真实客户端端到端验证通过，双向行为（含"是否需要响应"）已用可观察结果确认。 |

进入 `双向验证结束` 需同时满足：

1. 方向、触发时机、是否需要响应均已确认（**触发时机与响应顺序记在流程层文档里**，
   不写进协议文档——协议文档只回答"这些字节是什么"）；
2. 客户端构包或解包路径有指令级证据；
3. 字段顺序、宽度、条件分支和编码方式已记录；
4. 至少一个真实样本可由记录的布局无损解释；
5. 服务端 parser/codec 与 typed message 有回归测试；
6. 实机客户端验证通过，并记录可观察结果。

## Game / Client → Server

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` | `0x0001` | LOGIN | Query | 未知 | — |
| `0x01` | `0x0002` | `SET_UDP_IP_PORT` | Report / 无响应 | 实现服务端功能 | [0x0002.md](game/client-to-server/0x0002.md) |
| `0x01` | `0x0003` | EXIT | Query | 未知 | — |
| `0x01` | `0x0004` | `SELECT_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0004.md](game/client-to-server/0x0004.md) |
| `0x01` | `0x0005` | `CREATE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0005.md](game/client-to-server/0x0005.md) |
| `0x01` | `0x0006` | `DELETE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0006.md](game/client-to-server/0x0006.md) |
| `0x01` | `0x0007` | `RETURN_SELECT_CHARACTER` | Mutation / 有响应 | 双向验证结束（A21） | [0x0007.md](game/client-to-server/0x0007.md) |
| `0x01` | `0x0008` | GET_USERINFO | Query / 有响应 | 双向验证结束 | [0x0008.md](game/client-to-server/0x0008.md) |
| `0x01` | `0x000F` | `ENTER_SELECT_DUNGEON` | Query / 推 NOTI | 实现服务端功能 | [0x000F.md](game/client-to-server/0x000F.md) |
| `0x01` | `0x0010` | `SELECT_DUNGEON` | Query / 有响应 | **已观察，未实现**（开副本运行） | [0x0010.md](game/client-to-server/0x0010.md) |
| `0x01` | `0x0015` | `BUY_ITEM` | Mutation / 有响应 | 实现服务端功能（A21，部分商品实机通过） | [0x0015.md](game/client-to-server/0x0015.md) |
| `0x01` | `0x0016` | `SELL_ITEM` | Mutation / 有响应 | ClientVerified（A21 普通装备）；堆叠物自动化通过，特殊装备未覆盖 | [0x0016.md](game/client-to-server/0x0016.md) |
| `0x01` | `0x001D` | `BUY_SKILL` | Mutation / 有响应 | 实现服务端功能（A21 普通加点，待实机） | [0x001D.md](game/client-to-server/0x001D.md) |
| `0x01` | `0x0023` | `SET_USER_POSITION` | Report / A12 无响应，A21 推 `NOTI 0x0016` | 实现服务端功能 | [0x0023.md](game/client-to-server/0x0023.md) |
| `0x01` | `0x003F` | `CERA` | Report / 无响应 | 实现服务端功能 | [0x003F.md](game/client-to-server/0x003F.md) |
| `0x01` | `0x0040` | `BUY_CERASHOP_ITEM` | Mutation / 有响应 | 已观察，未实现（A21，19字节购买请求） | [0x0040.md](game/client-to-server/0x0040.md) |
| `0x01` | `0x0046` | `CARD_SELECT_RIGHT_STATE` | Query / 有响应；**另在 `0x0045` 的应答之后主动追发一帧** | 实现服务端功能 | [0x0046.md](game/client-to-server/0x0046.md) |
| `0x01` | `0x0047` | `SELECT_CARD` | Mutation / 有响应（认领不到牌位时不答） | 实现服务端功能 | [0x0047.md](game/client-to-server/0x0047.md) |
| `0x01` | `0x008F` | `CHANGE_TUTORIAL_FLAG` | Report / 无响应 | 双向验证结束 | [0x008F.md](game/client-to-server/0x008F.md) |
| `0x01` | `0x00AB` | `CLIENT_SPEC_STATISTIC` | Report / 无响应 | 双向验证结束 | [0x00AB.md](game/client-to-server/0x00AB.md) |
| `0x01` | `0x00C2` | `FRAME_LAG_STATISTICS` | Report / 无响应 | 双向验证结束 | [0x00C2.md](game/client-to-server/0x00C2.md) |
| `0x01` | `0x00C5` | `SAVE_GAME_OPTION_1` | Report / 无响应 | 实现服务端功能 | [0x00C5.md](game/client-to-server/0x00C5.md) |
| `0x01` | `0x00FA` | `LODING_TIME_REPORT`（分段指标流） | Report / 无响应 | 双向验证结束 | [0x00FA.md](game/client-to-server/0x00FA.md) |
| `0x01` | `0x0117` | `LAG_STATISTICS`（多态信封） | Report / 无响应 | 未知 | [0x0117.md](game/client-to-server/0x0117.md) |
| `0x01` | `0x01BA` | `MERCENARY_INFO` | Query / 有响应；**另在入场批次主动发一帧** | 实现服务端功能 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| `0x01` | `0x01DD` | `COMPATIBILITY_INDEX` | Report / 无响应 | 双向验证结束 | [0x01DD.md](game/client-to-server/0x01DD.md) |
| `0x01` | `0x01F8` | `INFORM_NOTICE_2ND` | Report / 无响应 | 实现服务端功能 | [0x01F8.md](game/client-to-server/0x01F8.md) |
| `0x01` | `0x0252` | `SECURITY_STATUS` | Report / 无响应 | 实现服务端功能 | [0x0252.md](game/client-to-server/0x0252.md) |
| `0x01` | `0x0286` | `CHARAC_VIEW_HIDDEN_CHARAC_INFO` | Query / 有响应 | 实现服务端功能 | [0x0286.md](game/client-to-server/0x0286.md) |
| `0x01` | `0x02A8` | `LOAD_EXTEND_CHARACS` | Report / 无响应 | 双向验证结束 | [0x02A8.md](game/client-to-server/0x02A8.md) |
| `0x01` | `0x02B3` | `STATICS_RUNTIME_TING` | Report / 无响应 | 实现服务端功能 | [0x02B3.md](game/client-to-server/0x02B3.md) |
| `0x01` | `0x02B5` | `CHECK_DOUBLE_CHARACTER_NAME` | Query / 有响应 | 实现服务端功能 | [0x02B5.md](game/client-to-server/0x02B5.md) |
| `0x01` | `0x02C1` | `CONTENTS_PLAY_INFO` | Query / 有响应 | 实现服务端功能 | [0x02C1.md](game/client-to-server/0x02C1.md) |
| `0x01` | `0x02CC` | `SHOP_PURCHASE_COUNT` | Query / 有响应 | 实现服务端功能（A21，待实机） | [0x02CC.md](game/client-to-server/0x02CC.md) |
| `0x01` | `0x04DD` | `CHECK_USER_CONNECTION` | Query / 有响应 | 实现服务端功能 | [0x04DD.md](game/client-to-server/0x04DD.md) |

## Game / Server → Client

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` CMD | `0x0001` | LOGIN response | Response | 实现服务端功能 | [CMD-0x0001.md](game/server-to-client/CMD-0x0001.md) |
| `0x00` NOTI | `0x0001` | `CHANNELINFO` 初始连接通知 | Notification | 实现服务端功能 | [NOTI-0x0001.md](game/server-to-client/NOTI-0x0001.md) |
| `0x01` CMD | `0x04DD` | `CHECK_USER_CONNECTION` response | Response | 实现服务端功能 | [0x04DD.md](game/client-to-server/0x04DD.md) |
| `0x00` NOTI | `0x0002` | 角色列表 | Notification | 实现服务端功能 | [NOTI-0x0002.md](game/server-to-client/NOTI-0x0002.md) |
| `0x01` CMD | `0x0003` | EXIT response | Response | 未知 | — |
| `0x00` NOTI | `0x0289` | `HONOR_LEVEL_INFO` | Notification | 实现服务端功能 | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |
| `0x00` NOTI | `0x0003` | `USER_STATE` | Notification | 实现服务端功能 | [NOTI-0x0003.md](game/server-to-client/NOTI-0x0003.md) |
| `0x00` NOTI | `0x0017` | `USER_AREA` | Notification | 编码器保留，**选副本时不发**——A21 参照只在回城路上发 | [NOTI-0x0017.md](game/server-to-client/NOTI-0x0017.md) |
| `0x00` NOTI | `0x001A` | UDP 主机通告 | Notification | 实现服务端功能 | [NOTI-0x001A.md](game/server-to-client/NOTI-0x001A.md) |
| `0x00` NOTI | `0x001B` | 进图选择状态 | Notification | 实现服务端功能（A21：37 字节，随 `0x0010` 的回包发） | [NOTI-0x001B.md](game/server-to-client/NOTI-0x001B.md) |
| `0x00` NOTI | `0x007C` | `ENTER_GAMEWORLD_COMPLETE` | **阶段闸点** | 双向验证结束 | [NOTI-0x007C.md](game/server-to-client/NOTI-0x007C.md) |
| `0x00` NOTI | `0x0009` | `PARTY_INFO` | Notification | 实现服务端功能 | [NOTI-0x0009.md](game/server-to-client/NOTI-0x0009.md) |
| `0x00` NOTI | `0x000E` | `UPDATE_ITEM_LIST` | Notification | 实现服务端功能（两个调用方：当日补给的真实增量、翻牌到货；批次里的空信封已撤） | [NOTI-0x000E.md](game/server-to-client/NOTI-0x000E.md) |
| `0x00` NOTI | `0x0016` | `USER_POSITION` | Notification / 兼作 `0x0023` 的应答 | 实现服务端功能 | [NOTI-0x0016.md](game/server-to-client/NOTI-0x0016.md) |
| `0x00` NOTI | `0x00CA` | `VILLAGE_OBJECT_LIST` | Notification | 实现服务端功能 | [NOTI-0x00CA.md](game/server-to-client/NOTI-0x00CA.md) |
| `0x00` NOTI | `0x0165` | `CHARAC_INVISIBLE_FALGS` | Notification | 实现服务端功能 | [NOTI-0x0165.md](game/server-to-client/NOTI-0x0165.md) |
| `0x00` NOTI | `0x021E` | `SET_USABLE_COUNT_INFO` | Notification | 实现服务端功能 | [NOTI-0x021E.md](game/server-to-client/NOTI-0x021E.md) |
| `0x00` NOTI | `0x0344` | `BUY_RESTRICT_ITEM_LIST`（A21 号，A12 为 `0x02DA`） | Notification | 实现服务端功能 | [NOTI-0x0344.md](game/server-to-client/NOTI-0x0344.md) |
| `0x00` NOTI | `0x036A` | `CONTRACT_OF_CUBE_INFO`（A21 号，A12 为 `0x0300`） | Notification | 实现服务端功能 | [NOTI-0x036A.md](game/server-to-client/NOTI-0x036A.md) |
| `0x00` NOTI | `0x03C1` | `EQUIPMENT_RENTAL_LIST`（A21 号，A12 为 `0x0357`） | Notification | 实现服务端功能 | [NOTI-0x03C1.md](game/server-to-client/NOTI-0x03C1.md) |
| `0x00` NOTI | `0x0465` | `EPIC_BOOK_INFO` | Notification | 实现服务端功能 | [NOTI-0x0465.md](game/server-to-client/NOTI-0x0465.md) |
| `0x01` CMD | `0x0239` | `SET_CLONE_TITLE`（闸点之后） | Notification-shaped CMD | 实现服务端功能 | [0x0239.md](game/client-to-server/0x0239.md) |
| `0x01` CMD | `0x036F` | `PREMIUM_SERVICE`（A21 号，A12 为 `0x0312`） | Notification-shaped CMD | 编码器保留，**入场批次不发**——A21 参照全程不发此号 | [0x036F.md](game/client-to-server/0x036F.md) |

角色生命周期五条 CMD 的响应与其请求同 type、同属一次交换，因此记在各自的 C→S 文档内而不另建
S→C 文档；理由见
[角色生命周期协议族](patterns/character-lifecycle.md#为什么请求和响应写在同一个文档里)。

S→C 的 command 取自服务端编码器（`GameLoginResponseEncoder` / `AccountCharacterRosterResponseEncoder`
/ `GameExitResponseEncoder`），并与客户端注册表归属交叉核对一致——**抓包对 S→C 的 command
字节零信息**，日志里的 `hdr` 是证据强度标志而非 command，详见
[game-receive](patterns/game-receive.md#抓包对-sc-的-command-字节零信息)。

服务端实现映射：CMD/NOTI 语义位于 `Dfo.Server.Protocol.Game`。每个出站 Game message
只属于 `IGameCmdMessage` 或 `IGameNotiMessage`，专属 encoder 固定写入 `0x01` 或 `0x00`，
专属 sender 方法在调用点约束类别。`Dfo.Server.Networking` 仅以不透明 `Selector + Type`
拆帧、路由和编码；全部 C→S Game decoder 由 Protocol 的 CMD helper 注册。

## A12 内容的退场

服务端服务的是 **A21**，而本资料库的绝大部分结论得出于 **A12** 客户端。两者的协议身份
本身就不一样（见上一节），所以 A12 的内容是**脚手架**，不是记录。

规则：

1. 一条协议在 A12 和 A21 上都存在、且 A12 已经做过的，**暂时保留** A12 的内容；
2. 该协议在 A21 上重做完成后，**整块删除** A12 的内容；
3. 终态：树里不再有任何 A12 内容。

为了让第 3 步可验证而不是靠记性，A12 的人工内容被包在

```html
<!-- rks:a12:begin -->  …  <!-- rks:a12:end -->
```

之间，A21 原生的页面则带 `<!-- rks:origin:a21 -->`。进度用
[`check-a12-residue.py`](patterns/data/check-a12-residue.py) 查：

```
python patterns/data/check-a12-residue.py . --list-mixed
```

它把每页归为 `a12-only` / `mixed` / `a21` / `empty` 四态。**`mixed` 就是待清单**——
那些页已经记下了 A21 的差异，但上面的字段表还是 A12 的。

**注意机器区。** 每页的机器区由静态普查生成，目前全部来自 A12 客户端（页脚的
`支持客户端 SHA-256` 就是证据）。人工区清干净了，机器区仍是 A12 的；真正离开 A12
要等对 A21 客户端重跑一次普查。检查器会单独报这个数。

## 阶段与 A21

**2026-08-24。** 上表的「阶段」列是在 **A12** 客户端上确立的。服务端现在服务 **A21**，
两者在若干协议上并不一致（`0x007C` 的 body 长度、闸点之后能不能继续发、四个号的归属……），
逐条差异记在各自文档的 `## A21` 一节，流程层差异见
[进入世界](patterns/world-entry.md)。

历史行的「双向验证结束」默认描述 A12，不能直接视为 A21 已验证。
**2026-09-08：`0x0007 RETURN_SELECT_CHARACTER` 已取得当前 A21 指令证据，并完成两轮返回选人、
再次进入的实机验证**，上表单独标记 A21；见[本次实机记录](patterns/data/traces/a21-return-selection-live-20260908.md)。
其余协议应以各自最新证据判定，不再用“所有协议都未验证”的全局结论代替逐条状态。

> 上面这句原本还写着「没有对 A21 客户端做过静态普查」。那一条已经不成立：2026-08-24 对 A21
> 镜像重跑了完整的身份普查与布局提取（[锚点](patterns/data/probes/a21-census-anchors.md)），
> 整棵文档树的机器区都由它重生成。下面「清单的完整性」一节里的数字仍是 A12 的，尚未换。

### 2026-08-25：入场批次逐帧对齐

本仓的入场批次与 A21 参照会话已**逐帧对齐**：两侧各 66 帧，协议多重集差为空。过程与全表见
[逐帧对账](patterns/data/traces/a21-entry-batch-diff-20260825.md)，实机会话见
[进城实录](patterns/data/traces/a21-live-entry-20260825.md)。

这一轮里有两条**被推翻的旧结论**，按规则连同原因保留：

| 旧结论 | 出处 | 推翻它的证据 |
|---|---|---|
| `GAME_OPTION` 的大小差是数据差异，不是布局差异 | [a21-world-entry-20260823](patterns/data/traces/a21-world-entry-20260823.log) `[S]` 节 | 客户端自己发的 `CMD 0x00C5` body 是 **154 = 4 + 150**，即它的选项表就是 150 字节；服务端发 134 不会解析失败，但那不是同一张表 |
| `ITEM_LIST` 的大小差是数据差异 | 同上 | 参照主背包声明 14 条、条目区 1414 字节，`1414/101` 整除——条目宽度本身变了 |

两条都属于同一类误判：**「大小不同不等于布局不同」是对的，但它不是「大小不同就一定是数据」**。
判据是能不能用角色数据把差额算平；算不平就要继续查。

## 清单的完整性

上面两张表只列**已在线观察到**的协议，不是客户端支持的全集。两个方向都已做过全量普查，
且**每一条普查到的身份现在都有自己的文档**——见 [Game 协议全量索引](game/index.md)：

| 方向 | 锚点 | 结果 | 数据 |
|---|---|---|---|
| C→S | `MakeHeader` 构包点 | 1082 点 / **604 type**（`0x0000`–`0x043D`） | [protocol-identity.tsv](patterns/data/protocol-identity.tsv) |
| S→C | `Register` 注册点 + 两个运行时转发器 | 1271 点 / **1266 个 `(command,type)` 对**（CMD 538 + NOTI 728，**零未解析**） | 同上 |

> **2026-08-16**：普查已重建为单一的[身份普查](patterns/data/census-identity.py)，
> 补上了此前缺的两样东西——每条记录的**族归属**（CMD 表 / NOTI 表），以及 439 个
> **运行时转发器**注册点。旧的两份 TSV 记载"尚未写回动态注册点"的缺口至此闭合。
> 新普查复现了旧记载的全部数字（604 / 538 / 728 / 两表共有 389），并把唯一一个
> 未解析注册点也解出了——它的首个 `push` 落在调用点前 67 字节，超出了旧脚本 64 字节的窗口。

**S→C 不能只看一个 type 数字。** 客户端按帧头第 0 字节
（command）把入站帧分给两张互不相干的注册表，它们对应两个**协议族**：

| 协议族 | command | 方向 | 客户端注册表 | 已普查 type | 空间容量 |
|---|---|---|---|---:|---:|
| **CMD** | `0x01` | 双向（C→S 请求 / S→C 响应） | 表 A | 538 | 1086 |
| **NOTI** | `0x00` | **仅 S→C**（服务端推送） | 表 B | 728 | 1036 |

**客户端只发 CMD**——`MakeHeader`（所有出站帧唯一必经点）把 command 写死成 `1`。
所以 C→S 的 604 个 type 全部属于 CMD 族，其最大值 `0x043D` = 1085 正好用满 1086 的容量。

证据、分表依据与三条独立佐证见
[game-receive 的 CMD 与 NOTI](patterns/game-receive.md#两张注册表--cmd-与-noti)。

**写 S→C 的 CMD 响应前必读**：每一条 CMD 的 body 都以两字节结果信封开头
（`u8 ok`，`ok == 0` 时再加 `u8 reason`），由客户端接收层在分发之前读掉，各 type 的载荷从
第 2 或第 3 字节才开始。把状态码直接放首字节会被读成"成功"，拒绝时漏掉 reason 字节会
**吃掉下一帧的首字节**。规则与指令见
[game-receive 的两字节结果信封](patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。
NOTI 没有信封。

方法见 [game-frame](patterns/game-frame.md) 与 [game-receive](patterns/game-receive.md)。
所有数字仍是**下限**：C→S 有被混淆到第二代码区、扫不到的构包器（已观察到的 C→S `0x0002`
就是其一）；S→C 的 handler 虽然拿不到帧缓冲，但会从全局入站流里取字段（读取原语见
[game-receive](patterns/game-receive.md#入站流与读取原语sc-的取字段方式)），
约 25% 的 handler 完全不读 body，这部分布局无法从指令推出。
上表的 538 / 728 已包含此前遗漏的 439 个运行时注册点，且**已写回**
[protocol-identity.tsv](patterns/data/protocol-identity.tsv)。

因此本清单的作用是"哪些协议已经在做/做完"，不是"客户端有哪些协议"；后者查
[Game 协议全量索引](game/index.md)。挑下一条协议时应先在索引里查它的构包点/handler 入口
与 body 初稿，再决定是否需要抓包。

### 已定语义但尚无实机样本

下列协议已有指令级结论，但**从未在抓包中出现过**（服务端只在选角色之后才发下行帧，
而选角色协议未实现），因此不进上面两张表。细节全部在族文档里，抓到样本后再逐条建独立记录。

| Command | Type | 方向 | 名称 | 文档 |
|---:|---:|---|---|---|
| `0x01` CMD | `0x00C5` | C→S | 主游戏选项上传 | [game-settings](patterns/game-settings.md) |
| `0x01` CMD | `0x00C6` | C→S | 按键槽上传 | 同上 |
| `0x01` CMD | `0x0170` | C→S | 快速聊天上传（带 bankIndex） | 同上 |
| `0x01` CMD | `0x01C0` | C→S | 角色设置上传 | 同上 |
| `0x00` NOTI | `0x00AD` | S→C | 游戏选项催传信号（body 被丢弃） | 同上 |
| `0x00` NOTI | `0x01C7` | S→C | 按键设置下发 | 同上 |
| `0x00` NOTI | `0x0187` | S→C | 角色设置下发 | 同上 |

## 命名与身份

- 名称来源必须可区分。**2026-08-16 起本清单内已无描述性命名**：上游源码树进入工作区后，
  `CmdPacketType`（1086 条）与 `NotiPacketType`（1036 条）覆盖了两族的全部 type 空间，
  此前的三个描述性命名均已换成官方名并在各自文档保留旧名映射：

  | 旧的描述性命名 | 官方名 | 文档 |
  |---|---|---|
  | `CLIENT_SPEC_PROFILE` | `FRAME_LAG_STATISTICS` | [0x00C2.md](game/client-to-server/0x00C2.md) |
  | `CLIENT_HARDWARE_DETAIL` | `COMPATIBILITY_INDEX` | [0x01DD.md](game/client-to-server/0x01DD.md) |
  | 荣誉等级下发 | `HONOR_LEVEL_INFO` | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |

  另有四条此前记为"未命名"的协议取得官方名：`0x008F` `CHANGE_TUTORIAL_FLAG`、
  `0x00FA` `LODING_TIME_REPORT`、`0x0117` `LAG_STATISTICS`、`0x02A8` `LOAD_EXTEND_CHARACS`。
  枚举值即 wire type——该映射经本清单中 16 条已双向验证协议交叉核对，含同号异义样例，零错位。
  **名称到位不等于字段到位**：官方名说明协议整体是什么，不说明每个字段是什么。
- 同一 type 在另一方向或端点出现时，必须新增独立行和独立文档，不能覆盖已有含义。
  `0x0001`、`0x0002`、`0x0003` 都已在两个方向观察到，即为此例。
- `未知` 行没有文档链接。它们的已知观察（长度、时机、字节片段）记在下方"未知协议的现有
  观察"里，避免重复发现；一旦开始逆向，就为它建独立文档并把观察搬进去。

## 未知协议的现有观察

采样自 2026-08-14 / 2026-08-15 四次实机会话（登录 → 角色列表 → 退出），使用
`[FrameCapture]` 全量双向抓包。**方向与长度是实测的**，语义均未确认。

### 登录握手段

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0001` | C→S | 73 | LOGIN。服务端已实现，含账号凭证与启动参数 auth hash（样本不得入库） |
| `0x0002` | S→C | 18 | 角色列表；样本为空名单。`0x0008` `mode=2` 的响应；服务端编码器含旧服移植的字面量尾部，未验证 |
| `0x0002` | C→S | 31 | **已定名：`SET_UDP_IP_PORT`**（上游 `CmdPacketType`，2026-08-16 由上游服务端日志 `SET_UDP_IP_PORT registered: bodyLength=31` 确认）。客户端上报自己的 UDP 端点，供 P2P 使用；本服务端不实现，客户端 handler 也不读 body。与 S→C `0x0002` 同 type 反方向。**条件发送**：五次会话中出现四次，2026-08-15 第五次会话完全没发（其后所有 seq 相应下移一位）。出现时只有一个 u32 变化（offset 12，`0x0005C0E2`/`0x0005C022`，像计数或时间戳），其余恒定，含 `0C000000` + 12 字节 ASCII 十六进制串 `30e3a448ed0f`。构包器在**被混淆的第二代码区**（抓包 stack[1] = `RVA 0x04876980`），`.text` 普查查不到 |

### 退出 flush 段

客户端**正常退出**时按固定顺序发出（Confirmed，四次会话逐帧一致）：

```text
0x0002(31B) → 0x00C2(361B) → 0x00FA(50B) → 0x0117(12B) → 0x0003 EXIT ⇄ → 0x008F(5B)
```

四条都已建独立文档：`0x00C2` / `0x00FA` / `0x008F` 均已双向验证结束，`0x0117` 仍为未知。
`0x008F` 严格说不属于 flush——它是 EXIT **响应**的反应，详见其文档。

> **A21 的这一段不一样（2026-08-27 实测）。** 顺序是
> `0x02EF(424B) → 0x00C2(362B) → 0x045C(21B) → 0x00FA(56B) → 0x0117(61B) → 0x0003 EXIT ⇄ → 0x02B3(1B)`，
> 且 **`0x00C2` 是 362 字节而不是 361**——A21 的包头比 A12 多一字节。本仓按 361 解码抛异常、
> **连接被拆**，表现是玩家在城里按退出游戏卡死。详见
> [0x00C2](game/client-to-server/0x00C2.md#a21包头是-25-字节不是-24--而且读错会掉线)
> 与 [a21-exit-and-return-20260827](patterns/data/traces/a21-exit-and-return-20260827.md)。

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0003` | C→S | 1 | EXIT 请求（`00`）。服务端已实现 |
| `0x0003` | S→C | 1 | EXIT 响应（`01`）。服务端已实现；目前唯一已观察到的**有响应**对 |

只进频道并停留在角色列表的会话不会出现 flush 段；**强制结束进程也不会触发 flush**。
要采样退出序列的协议，必须走游戏内正常退出。

### 抓包并不完整：seq 4 与 seq 6（**已解决**）

五次会话的发送 seq 全部形如 `0…3, 5, 7, 8, …`——**seq 4 与 seq 6 从未被抓到**。
seq 只在真正发送时递增（第五次会话少发一帧，其后每个 seq 都相应下移一位，证明了这一点），
所以这两个号确实被某次发送消耗了，只是抓包看不到。

抓包引擎挂在 `Cipher::Encrypt` 上，因此**任何不走加密路径的发送都会漏**。
影响范围：可以断言"某两帧之间没有**入站**帧"（入站有独立的 Decrypt 钩子），
但**不能**断言"客户端只发了这些帧"。各协议记录里的"无响应"结论不受影响，
"发送序列完整"这类表述则必须留有余地。

**2026-08-16 补上了缺的那一半仪器。** 服务端此前对没有 decoder 的入站帧是静默丢弃——
不记日志、不回字节；补上「未识别入站帧」日志后，这两帧当场现形：

| seq | Type | Body | 文档 |
|---:|---:|---:|---|
| 4 | `0x0286` | 0 | [0x0286.md](game/client-to-server/0x0286.md) |
| 6 | `0x01BA` | 0 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| 7 | `0x0002` | 31 | 见上「登录握手段」 |

客户端侧仍只记到 `0,1,2,3,5`，两边拼起来正好连续 `0–7`。

上面关于抓包的限制**依然成立**，但现在有了互补的第二个仪器：**服务端入站日志记录一切实际到达
的字节，不管发送方走没走加密**。两者盲区不重叠，合起来才能断言"客户端确实什么都没发"。
方法上的推论见
[角色生命周期协议族](patterns/character-lifecycle.md#顺带解决的旧悬案seq-4-与-seq-6)。

### 2026-08-26：副本段

`0x000F` / `0x0010` / `0x008F` / `0x0252` / `0x0025` / `0x00BF` 及其回包在**线级**上已与 A21
参照逐帧同形，见[副本段对照](patterns/data/traces/a21-dungeon-wire-20260826.md)。
**尚未由真客户端确认**——线级重放证明服务端答了什么，不证明客户端接受什么。

同时修掉一处会让排查代价极高的形状：解码器抛出的异常此前没有报告路径，
表现为连接静默消失。详见[进入世界](patterns/world-entry.md#断线的机制)。
