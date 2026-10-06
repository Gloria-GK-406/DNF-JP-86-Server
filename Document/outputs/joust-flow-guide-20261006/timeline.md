# A21 马战可能的模拟发包时间线（阶段2）

2026-10-06。输入为阶段1 [protocol-catalog.md](protocol-catalog.md) 与本轮 ClientStatic 原始dump取证。[scenarios.json](scenarios.json)供纯模型/页面播放，**不发真实网络，不写数据库，不宣称原服顺序或实机验收**。T+编号仅表示操作顺序，不是秒数。

## 模拟前提与四层边界

初始状态来自一次**假定已发生的manager reset**：state0；结果窗口新建scene0、counter-1；库存100。service45存在、网络ready，level20达到模拟要求17，材料槽7足够。manager构造未显式写state，登录必reset没有动态证明，因此模拟初值不是原生登录保证。

默认八名骑士index0..7，页面名字A..H均为模拟名，不是实际PVF。对阵固定`0vs1、2vs3、4vs5、6vs7`；七条winner/loser依次`(0,1)、(3,2)、(4,5)、(7,6)、(3,0)、(4,7)、(3,4)`，模拟冠军3，aux均0。aux是轨迹索引，不是比分、padding或伤害值。

- **C2S/S2C原生消费**：006C、04E6/7/8及ACK、04B8/9/BA/BB。首14B opaque保持Unknown，模型不伪造认证内容。
- **DOMAIN模拟**：接受下注、扣物品、结算、创建邮件。客户端读包证据不定义这些服务端写入规则。
- **共享投影模拟**：库存数量更新。这里不为共享库存或邮件业务编造马战专属opcode。
- **CompatibilityExtension**：0190插件的B8 marker处理，单独标识，不能混为原生初始化能力。

## 完整闭环候选

以下对应`native-candidate-full`。ACK与资料投影、库存与ACK的先后仅为可演示候选，未获得原服实帧顺序；阶段业务名称同样是模拟策略名称，不由rawState号直接证明。

| 概念时刻 | 输入/派生行为 | 观察与边界 |
|---|---|---|
| 入口初始化 | S2C NOTI006C提供093D活动入口 | EventManager入口门开放；已解析链没有初始化马战manager非零state。006C其它活动/全局UI副作用不被简化模型穷举 |
| 轮次初始化候选 | S2C B8 `{round:1,state:1}` | manager1、scene1；带字符串ID12BCB直接公告，没有原生silent位 |
| 打开结果页 | `openUI` | 选择已识别保留/show-init分支：先派生04E6，再同步检查已有manager state；不能等回包才解除state0拒绝 |
| 成功打开后的投注页初始化 | F6050→F5CC0→显示1FB | 自动派生04E8。结果页open本体不重放当前scene，不自动再次冠军。是否每次重开都走show-init仍Unknown |
| 资料查询响应候选 | ACK04E6 success1/result0；B9；BA | ACK只ensure投注窗口，不能替代manager state，也不自动show。B9八条资料、BA个人投注投影 |
| 首次历史查询响应 | ACK04E8 success1/result0、空逻辑记录 | 原生success非0读固定3504B，完整body3505B；数组为页面逻辑表示，不能据此缩短线帧 |
| 选择及确认下注 | 选骑士3、数量10、物理槽7；C2S04E7 | 客户端请求只是请求；不是扣除证明。fixture已满足模拟UI/材料前提 |
| 接受下注 | DOMAIN `acceptBetBusiness` | 模拟服务端ledger增加10，服务端库存100→90；与读包/客户端UI分离 |
| 扣除投影与确认 | `inventoryProjection(90)`、ACK04E7 result0、BA total10/骑士3=10 | 客户端库存看到90；ACK刷新输入控件；BA显示个人投注。不由ACK或BA本身扣除服务端物品 |
| 封盘候选 | B8 state3 | manager3、直接公告12BBD，**不改变scene**；state!=1的投注控件限制属于独立消费链 |
| 第一组候选起播 | B8 state4→scene13；BB stage0 | 当前scene13时BB0→scene3，开始前置动画；此处B8 state4有直接公告12BBE |
| 第一组本地推进 | `localAdvance`两次 | 抽象scene3→4→5；scene5不自行进入6 |
| 第二组候选起播 | 再B8 state4→scene13；BB stage1 | 此条件下BB1→scene6；并非仅stage号足够 |
| 第二组本地推进 | `localAdvance`两次 | scene6→7→8；scene8不自行进入9 |
| 第三组候选起播 | 再B8 state4→scene13；BB stage2 | 此条件下BB2→scene9；已经交付完整第三组胜负 |
| 第三组本地推进 | `localAdvance`两次 | scene9→10→11→12；scene11 setter立即递归12，冠军字符串ID12BBF可公告，不等待BB3 |
| 结束展示候选 | B8 state2→scene2 | 带直接公告12BCC。本例**不自动再发BB3**，否则无条件scene12可再次冠军公告 |
| 结算/邮件 | DOMAIN `settleBusiness(winner3,mailCount1)` | 模拟结算并创建一封邮件；不是BB、ACK或历史查询的隐含写入。本页面不模拟邮件协议家族 |
| 历史查看 | 再C2S04E8，ACK历史记录 | 只读；记录末4B以valueRaw保留Unknown，不命名为Odds。无advance、扣除、结算或新邮件通知 |

每组先state4是让3/6/9起播**可达的候选输入**，不是确认原服每阶段都会这样广播；它会带公告。其他原生起播/恢复入口和原服state5发送时点仍Unknown。

## BB填槽与本地计时

所有BB保持七个4B槽。fixture stage0仅前4有效其余FF，stage1前6有效末FF，stage2/3全部有效。**4/6/7是本例服务端填槽策略；原生实际消费6/7/7/7**。未匹配index和FF后续安全没有完整实机验证，模型显示此假设而不能把它伪装成原生count字段。

本地计时参数9100、插值帧索引与PVF轨迹vector耗尽条件是ClientStatic；单位/约9.1秒仍High，前置动画和隐藏窗口tick调度未闭合。因此`localAdvance`只表示一步“前置动画完成”或“轨迹耗尽”，不写固定秒数。隐藏窗口是否tick用独立模型选项表示假设，默认不推进，不能从get-or-create成功推断真实tick继续。

## 对照场景与预期

| 场景ID | 用途 | 应观察到的边界 |
|---|---|---|
| `missing-state-open` | 006C、ACK、B9/BA已到仍无state初始化 | 同步state0拒绝open；04E6已经发出不说明open会等响应。资料成功不是非零state |
| `missing-event-gate` | state1已有但无093D | 通用入口被活动表门挡；入口与manager为两道独立门 |
| `current-server-skip` | 当前实现输入对照 | 第一组scene5后仅BB1直接8，跳过第二组起播；Final无包；End state2+BB3→12 |
| `only-add-stage2` | 仅恢复BB2 | scene8收到stage2直接12，不能修正第三组起播顺序 |
| `readonly-repeat-query` | 重复04E6/04E8 | 服务端模拟ledger、库存、邮件数不变；不发B8生命周期公告。UI投影更新不等于写库 |
| `repeat-bb3-champion` | 同round重复BB3 | 两次无条件进入12；存在重复冠军公告路径，没有旧round/state去重 |
| `mid-join-native` | 原生中途恢复尝试 | state4直接公告，BB2从13重播第三组并本地冠军；不是静默/精确已播放时间恢复 |
| `marker-b8-only` | 兼容marker作用域 | 启用0190时snapshot4正常写manager/scene13但跳B8直接公告；随后的BB3仍冠军公告 |
| `marker-local-champion` | marker+BB2本地推进 | B8直接公告静默不抑制本地scene12冠军链 |
| `marker-without-plugin` | 相同marked字节但无0190 | signed=-124保存到manager；scene未正常映射，不是原生silent状态 |
| `state5-counter-chain` | counter轮转 | BB2设counter2；state5→11/12并写0，再连续state5→5/8/11/12，counter0→1→2→0 |
| `state5-before-results` | 未BB的counter-1 | state5写manager5，但counter不匹配0/1/2，不发生上述轮转 |
| `remove-event-no-reset` | 取消活动入口 | eventList=false不擅自resetmanager或scene；随后open被入口门挡。已打开窗口是否原生关闭不据此猜测 |
| `state1-query-feedback` | 查询回送state1错误对照 | 已显示窗口每次scene1消费派生04E6且B8公告；有界输入展示反馈链，不运行无限循环 |
| `bet-ui-history-init` | 独立show1FB | 显示初始化自动04E8；ensure对象不等于show初始化 |

查询纯读是服务端契约：读包静态证据不会强制服务端只读。反之查询虽不写库，若响应包含BB3/特定BB2，仍可能发客户端冠军公告；必须分别检查领域副作用和客户端显示副作用。

## 兼容marker与证据锚点

`stateSnapshot`只为展示0190_JoustStateSync扩展。启用插件时，raw0x80..0x85在manager setter和scene consumer两处解除高位，原始B8 raw仍供公告门使用，所以跳过B8直接公告。**BB、scene12及scene0/1已显示自动查询都未被这一hook全面抑制**；snapshot5且counter2照样可进入12。禁用插件时相同marker保存为signed负值，不正常映射scene；不能把它当原生silent机制。

| 证据 | RVA / VA |
|---|---|
| 006C reader、093D gate | `01019E30 / 01419E30`；`010172D0 / 014172D0` |
| manager reset / getter | `001B64C0 / 005B64C0`；`02446D50 / 02846D50` |
| 结果open→查询→已有state门 | `000F6050 / 004F6050`，`000F60C8 / 004F60C8`、`000F60CF / 004F60CF` |
| 投注页显示初始化 / 查询历史 | `000F4B10 / 004F4B10` |
| 投注writer / ACK刷新 | `000F2B10 / 004F2B10`；`000D20B0 / 004D20B0` |
| B8 state与公告 | `000D2690 / 004D2690`；`000FCBD0 / 004FCBD0` |
| B9 / BA | `000D2330 / 004D2330`；`000D23E0 / 004D23E0` |
| BB stage消费 / 本地推进 | `000FCCA0 / 004FCCA0`；`000FB7F0 / 004FB7F0` |
| scene11→12 / 冠军聊天 | `000FB35F / 004FB35F`；`000FB70C / 004FB70C`、`000FB742 / 004FB742` |

原始证据见上一阶段`joust-client-reanalysis-20261006/scenes.md`、`initialization.md`、`protocol-set.md`及本轮`opening-review.md`。所有字符串正文仍Unknown；页面以ID和模拟名称表达，不将截图中文倒推为Confirmed。
