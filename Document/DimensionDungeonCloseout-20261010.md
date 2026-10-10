# 异界单人基础可玩一期收尾

日期：2026-10-10。分支：task/monster-declaration-parser。验收代码：fa7acdeeb59c9011ff833d0f01e6a2ca2229d20a。
用户本轮授权更新协议与 issue，核查需要服务端协调的机关；本轮没有实现新机制、改客户端/PVF、部署、提版或推送。

## 验收结论

用户在跨职业掉落部署后反馈：“看起来符合预期了。那异界是不是可以算一期验收了？”本次按**已测试的单人基础可玩一期**记录接受，包含玩家已体验的异界入场、房间推进、战斗与奖励闭环，以及取消职业锁池后的掉落表现。
这是有限范围的 ClientVerified；并无逐图、逐难度、逐异常步骤反馈或新归档实帧，不能据此填写全部测试项通过。

| 项目 | 当前状态与证据边界 |
|---|---|
| CMD0010异界业务分流、共用入场 | 已实现；用户整体接受已测试单人闭环。分类依据PVF标记，并非仅7100。 |
| 次数持久化、DirectAsync扣次、NOTI0326 | CurrentImplementation与自动化覆盖；不把耗尽、日切、重登、回滚等全部UI分支扩大为实机通过。 |
| 跨职业怪物掉落、免费/付费翻牌 | 已部署，用户接受基础表现；各档概率和长期经济未做实机统计。 |
| 抗魔不足削属性 | ClientObserved：用户观察为客户端处理。本期不重复实现服务端削属性；准确公式、特殊例外和多人交互未静态闭合。 |
| 专属机关与组队 | 继续核查；基础可玩接受不代表所有机关阶段、全部教学变体与多人同步通过。 |

用户未要求模拟原服概率；当前参数是按70生成风格制定的本服策略，具体见 [掉落策略](DimensionDungeonDropPolicy-20261010.md)。70原表调查保留为 [历史研究](outputs/tw70-chronicle-drop-research-20261010/findings.md)，其中“当前实现”指该报告的a472a70f调查基准，不能当作fa7acdee现状。

## 已验收部署身份

- 部署收据：仓库主目录 artifacts/dimension-drop-20261010-2315/deployment.json；ActivatedAt 2026-10-10T23:16:15+08:00。这是该次部署记录，非持续在线状态证明。
- Version：0.2.3-dimension-drop.20261010.r1+fa7acdeeb59c9011ff833d0f01e6a2ca2229d20a。
- Host SHA-256：42CFA8734D6CF97F53D70406C92F9487FFD4A7226E49CE4A259A7551E12FC95A。
- PVF：artifacts/dimension-dungeon-20261010/Script.dimension.pvf；SHA-256 ADAC21543D89AE1A8228C6D6031757BB54A5A15F996AF062B331B09795EBF096。
- 旧运行目录及该次停服数据库备份保留；本次不修改数据库。
- 收据中的 ClientAcceptance=Pending 是发布当时的历史状态；部署后的用户接受以上节为准，不回写历史收据。

既有验证（代码fa7acdee）：Application 3577通过、Protocol 1439通过、Release定向54通过，均0跳过；Release全方案0警告/0错误。本轮仅改文档与 issue，不重跑整套业务测试；另运行只读PVF盘点工具，检查链接、自动区不变与diff空白规范。

## 服务端机关边界

[静态盘点](outputs/dimension-mechanisms-20261010/README.md)覆盖13个带异界标记DGN、138张关联MAP的直接声明引用；未发现直接引用疑惑之村电梯对象1111/1112/1113。这个结果只排除了直接复用该控制对象，不能证明所有异界机关都在客户端独立执行。

| 候选 | 已有依据 | 仍需确认 |
|---|---|---|
| 哥布林碉堡，MAP16101/53071、OBJ61982 | MAP子怪模板；OBJ含召唤参数和销毁后创建61986的声明 | 场景动态怪与服务端预生成的对应、死亡编号及清房资格；是否需要服务端阶段控制 |
| 虚无箱子，MAP17054、OBJ10285/10286 | 箱子管理器/箱子对象；管理器动作配置为空 | 客户端内建控制与服务端授权/广播的边界，空脚本不能解释成无机制 |
| 虚无Boss地板，MAP17058、OBJ14133 | OBJ引用Action/_AutoTile_Boss.act | ACT/客户端控制类和阶段同步，不凭动画文件推断完整流程 |
| 黑色大地米歇尔，MAP15083、OBJ10362 | 特殊对象关联子怪模板 | 变身/生成和清房判定；是否需要独立消息 |
| NOTI0222 | A21 RVA0x00D62230可见u8下界，上游只有枚举命中 | 完整布局与用途未知，不能仅凭DIMENSION_SPACE_INFO名称绑定上述机关 |

若后续证明确需服务端协调，复用 [RoomMechanism生命周期与通知管线](dungeon-room-mechanisms-20261010.md)，每种机关定义自己的状态规则和已证实协议。NOTI00FE只确认电梯语义，不作为任意动画或多波次的通用消息。不得把GOBLIN_PAD安全密码协议误认成哥布林地图机关。

## GitHub收口

- [#74 异界地下城](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/74)：记录一期接受，保持OPEN，剩余地图机关、多人与完整异常边界继续追踪。
- [#54 异界与编年装备成长](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/54)：校准装备获取已接入，气息、成长/升级与专用分解等不因基础掉落完成而关闭。
- [#124 掉落重构](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/124)：校准异界专属来源已实现及有限验收，保留多人隔离、模拟/性能等总任务范围。
- [#145 专属机关与服务端协调边界核查](https://github.com/Gloria-GK-406/DNF-JP-86-Server/issues/145)：承接上述候选，不把静态线索直接登记成已复现bug。

协议同步：[CMD0010](../protocol/game/client-to-server/0x0010.md)、[NOTI0326](../protocol/game/server-to-client/NOTI-0x0326.md)、[NOTI0222](../protocol/game/server-to-client/NOTI-0x0222.md)、[catalog](../protocol/catalog.md)。自动生成区不改，未知字段继续保留未知。
