# DNF 86JP 协议资料库

本目录记录 86JP 客户端的协议结论及其证据：不只是“代码现在怎么写”，还要回答“为什么相信它是对的”。

本页只说明**资料如何组织**。协议本身的细节一律在各协议文档内，不写在这里。

## 四层：每种事实各有一个归处

混层是这个资料库最大的历史问题——一条全局不变量被抄进 682 份文档、一个流程问题被向
1411 份协议文档各问一次。四层的分界就是为了让"这条事实该写在哪"永远只有一个答案。

| 层 | 回答什么 | 在哪 | 数量与变化 |
|---|---|---|---|
| **帧层** | 全部协议共享的包结构不变量：帧头、CMD 两字节结果信封、CMD/NOTI 是两张注册表、入站流与读取原语 | [game-frame](patterns/game-frame.md)（**含 RVA/VA 约定，先读**）、[game-receive](patterns/game-receive.md) | 两页，长期稳定；改一次影响全部 1411 条 |
| **协议层** | **这些字节是什么，凭什么这么说** | `game/client-to-server/*.md`、`game/server-to-client/NOTI-*.md` | 千余份，持续变动 |
| **流程层** | 协议**如何被编排**成一个有业务语义的流程 | [flow-login](patterns/flow-login.md)（连接→停在选人界面）、[world-entry](patterns/world-entry.md)（选角色→站进城里）、[flow-dungeon-entry](patterns/flow-dungeon-entry.md)（点开副本列表→进第一个房间）、[flow-item-effects](patterns/flow-item-effects.md)（物品／奖励统一结算与通知依赖）、[flow-equipment-disjoint](patterns/flow-equipment-disjoint.md)（分解请求→事务结算→背包刷新）、[flow-creature-lifecycle](patterns/flow-creature-lifecycle.md)（宠物普通生命周期与限定验收）、[game-settings](patterns/game-settings.md)、[character-lifecycle](patterns/character-lifecycle.md) | 按业务流程增长 |
| **方法层** | 证据有哪些**种类**、各能回答什么、引用怎么写、错了怎么办 | [evidence.md](evidence.md)（判决规则）、[methodology.md](methodology.md)（工作顺序） | 两页 |

三条分界规则，不清楚时按这个判：

- **字段的存在属于协议层；某个流程里这个字段被填成什么值属于流程层。**
  "`0x0017` 有 area 字段"是协议；"选图状态下 area 被改写成 `0xFF`"是流程。
- **协议文档不回答"什么时候触发""响应顺序如何"** —— 那是流程层的问题。
- **一条事实横跨多条协议就升到帧层**，不要在每份文档里各写一遍。

证据不是一层：它没有自己的目录，**它出现在它支撑的那个主张旁边**。规则见 [evidence.md](evidence.md)。

不进这个目录的两类东西：**业务设定的数值与规则**（疲劳上限、每日恢复时点之类，那是游戏设定
不是逆向发现）和**我们服务端的领域设计**（该建什么表、怎么算——去仓库根 `Docs/`，协议文档
只放链接）。

## 从哪里开始

| 目的 | 去处 |
|---|---|
| **客户端到底有哪些协议**（A21 静态索引：846 个 CMD 交换页、839 个 NOTI 页；另列实帧/发送行为证明的静态盲区例外） | [Game 协议全量索引](game/index.md) |
| 有哪些协议**正在做/已做完** | [协议清单](catalog.md) |
| 某条协议的全部细节 | 清单里该行链接的协议文档 |
| **证据该怎么写、冲突时谁赢、错了怎么删** | [evidence.md](evidence.md) |
| 从未知报文到实现完成的工作顺序 | [methodology.md](methodology.md) |
| 客户端支持的 type 全集（两个方向、两个族） | [patterns/data/protocol-identity.tsv](patterns/data/protocol-identity.tsv) |
| 开始做一条协议 | 它的文档**已经存在**（见全量索引），直接在其人工区往下写 |
| 手写一篇全新记录（非 Game 端点等） | 复制 [记录模板](templates/message.md) |

## 目录组织

```text
protocol/
├── catalog.md              进度清单：正在做/已做完的协议
├── evidence.md             方法层：证据的种类、效力顺序、引用与删除规则
├── methodology.md          方法层：工作顺序
├── patterns/               帧层（game-frame / game-receive）与流程层（world-entry / flow-*）
│   └── data/
│       ├── traces/         归档的运行时读取跟踪与帧抓包，供文档引用
│       └── probes/         归档的一次性探查产物（PVF 表、内存快照摘录等）
│   └── data/               机器产出的普查/索引数据，及产出它们的脚本
│       └── meanings/       字段含义分片（生成器的输入，不是产物）
├── templates/message.md    协议文档模板
└── game/
    ├── index.md            全量索引：静态可见的每一条协议
    ├── client-to-server/
    │   ├── <type>.md       CMD 请求（含其响应），如 `0x00AB.md`
    │   └── samples/
    └── server-to-client/
        ├── NOTI-<type>.md  NOTI 推送，如 `NOTI-0x0289.md`
        └── samples/
```

- **一条协议一个文档。** 文档以协议号命名，因为号是稳定的而名字未必。文档内含该协议的
  身份、布局、样本、证据、服务端实现映射、验证结果、未解问题以及逆向工作日志——
  查一条协议只需要打开一个文件。
- **文件名必须能表达身份。** C→S 全部是 CMD（客户端的出站帧构造点把 command 写死为
  `0x01`），所以裸类型号就够。S→C 同时承载 CMD 响应与 NOTI 推送，两族共用一套 type 编号，
  文件名因此带族前缀——否则 NOTI `0x0001` 与 CMD `0x0001` 的响应会抢同一个文件。
- **机器区与人工区。** 生成的文档以 `<!-- rks:machine:begin -->` / `end` 圈出机器区，
  重新生成只覆盖该区，区外的一切都是人工领地；完全手写的文档没有机器区，生成器永不触碰。
- **机器区的 Body 表在 S→C 上只是下界。** 它由静态走图产出，普遍止步于打包区。当同一份
  文档里既有机器区的表又有「实测字段表」时，**以实测为准**——后者是客户端自己读出来的。
  `patterns/data/meanings/*.tsv` 仍是生成器的输入，但它不再是字段含义的唯一去处，
  也不再有门禁（原先那个校验器按静态边界拒绝真实结论，已于 2026-08-17 废除）。
- **协议身份 = endpoint + direction + command + type**，四者缺一不可。相同 type 在不同方向
  或端点可能是完全不同的消息，必须各自建行、各自建文档，不得覆盖已有含义。文档路径本身
  携带 endpoint 与 direction。
- **样本**按 `<type>-<日期>-<序号>.hex` 命名，只存 body 十六进制。

跨多条协议反复出现的 header、长度编码、列表模式、构包原语或定位技巧，应从单条协议文档
**提升**到 `patterns/`，并在该协议文档里回链。

## 与当前服务端对账

当前服务端源码只能回答“代码注册/可发送了什么”，不能证明协议号、布局、响应关系或客户端
效果正确。新增或删除 decoder/encoder 后，在仓库根运行：

```powershell
python protocol/patterns/data/check-server-documentation-sync.py --repo-root .
```

检查器会对账已注册 C→S decoder、encoder-backed S→C CMD/NOTI、catalog 登记、协议页和 catalog
本地链接。差集归零只代表**实现覆盖已被文档看见**；成熟度仍须由 A21 客户端逆向、线级样本与
真实客户端行为共同裁决。显式动态发送映射位于
[`server-wire-overrides.tsv`](patterns/data/server-wire-overrides.tsv)，它是静态分析补充输入，
同样不是协议真相。

## 证据等级

**等级**是单条结论的置信度；**种类**是证据的来源及其效力上限，两者不同，
见 [evidence.md 的效力顺序](evidence.md#效力顺序)。一条结论的等级不可能高过支撑它的
证据种类允许的上限——例如只有上游转录支撑的结论最高只能是 `Hypothesis`。

每项结论使用下列等级之一：

- **Confirmed**：由客户端构包/解包指令、服务器消费代码或可重复实机行为直接证明。
- **High**：多条独立证据一致，尚缺一条直接指令级证据。
- **Hypothesis**：有合理迹象，但仍需要反汇编、动态 trace 或对照样本确认。

`Hypothesis` 不得直接变成生产领域语义。可以先按 offset 和原始宽度建模，并保留未知名称。

## 安全边界

- 登录凭证、账号令牌、聊天隐私和可识别个人的数据不得提交到本目录。
- 硬件遥测样本只保留协议研究所需字段；不关联真实用户身份。
- 大型二进制、完整客户端和数据库不进入本目录，只记录哈希与可定位的 RVA。
- **被推翻的证据直接删除**，不要留着再写一条说明它错了：同时存在真假两份证据，
  会让每个后来的读者重新裁决一次，而错的证据往往被别的结论压着。判决规则见
  [evidence.md](evidence.md#错了就删)。走过的**弯路**是例外，它是"哪里不能走"的地图，
  留在各文档的「逆向工作日志」里，但必须标成弯路而不是结论。

## 适用范围

这里记录的协议均以当前 86JP 客户端为对象，默认仍是代码反推结论，不等同于官方协议说明。
客户端版本变化时，不得直接假定地址、字段或行为仍然成立；各协议文档均记录其绑定的客户端
SHA-256。
