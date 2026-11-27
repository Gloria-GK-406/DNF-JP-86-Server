# 公会领域服务重构交付记录

本次在 `feature/guild-system` 现有工作树上完成 [任务契约](TaskContract.md) 第 1 版的五项目标。[设计方案](Spec.md) 列出了业务所有者及相互调用。工作树基线包括 `e493c238f` 和实施前的未提交内容。

## 关键行为

- 会籍服务统一处理审批和邀请入会。资料编辑及捐赠均有独立协议入口，捐赠扣金币、入公会 GM 和记个人流水仍在一次提交中完成。[会籍](../../../Server/src/Dfo.Server.Application/Guilds/GuildMembershipDomainService.cs) · [捐赠](../../../Server/src/Dfo.Server.Application/Guilds/GuildDonationDomainService.cs)
- 进城使用同一时刻依次记录签到和月贡献。任一项发生变化即刷新成员面板，两项均无变化时只向本人发送面板和基地信息。每日贡献币另行结算，普通奖励失败只回滚该笔奖励。[进城编排](../../../Server/src/Dfo.Server.Protocol/Game/GuildAttendance/GuildAttendanceDelivery.cs) · [四种变化组合与两种失败测试](../../../Server/tests/Dfo.Server.Application.Tests/Guilds/GuildAttendanceDeliveryTests.cs)
- 购买先做商品和权限检查，再验证可激活或可扩容，成功后在同一事务扣款并落库。领取盟会礼物以已存商品发放，沿用同一权益生效规则，接收方不再发生扣费。权益到期清理、传送次数和角色属性加成都接至权益服务。[购买](../../../Server/src/Dfo.Server.Application/Guilds/GuildContentPurchaseDomainService.cs) · [权益](../../../Server/src/Dfo.Server.Application/Guilds/GuildBenefitDomainService.cs) · [公会持久化回归](../../../Server/tests/Dfo.Server.Persistence.Tests/Guilds/GuildDomainTests.cs)
- 公会查找与推荐、捐赠流水、权益、盟会各有读取入口。面板把这些业务中需要展示的会籍、签到、职级、权益、盟会和月贡献数据组合为快照。本会与盟会分别读取，各成员从自己公会的快照取得个人贡献。[公会概览](../../../Server/src/Dfo.Server.Application/Guilds/GuildOverviewDomainService.cs) · [职级权限](../../../Server/src/Dfo.Server.Application/Guilds/GuildRankPolicy.cs)
- 建会草稿和已核准的改名在同一选角会话中互斥。更换选角、取消、重新检查失败及取消后旧检查迟到均有回归覆盖。[名称会话](../../../Server/src/Dfo.Server.Application/Guilds/GuildNameOperationSessionDomainService.cs)

## 验证结果

各行使用独立测试筛选，存在重叠。计数来自各项目 TRX 的执行结果。

| 检查 | 结果 | 对照 |
| --- | --- | --- |
| [解决方案构建](../../../artifacts/guild-service-boundaries-20261128/build.log) | 0 警告、0 错误 | 项目引用检查通过，13 个生产项目、32 条直接引用。 |
| [契约定向回归](../../../artifacts/guild-service-boundaries-20261128/targeted.log) | 199/199 通过 | 含领域、协议、持久化、内容、迁移和命令构造依赖检查。 |
| [完整迁移测试](../../../artifacts/guild-service-boundaries-20261128/migrations.log) | 62/62 通过 | 使用隔离数据，本次未变更 schema。 |
| [Unit 分类](../../../artifacts/guild-service-boundaries-20261128/unit.log) | 2,084 通过，2 项失败 | 命令构造器依赖检查已从失败转为通过。内容源注入、地下城请求判定位置两项原有失败仍在。后者与原记录的差异仅是源文件行号。[原记录](../../../artifacts/guild-refactor-20261128/unit-delivery.log) |
| [扩展跨域回归](../../../artifacts/guild-service-boundaries-20261128/cross.log) | 248 通过，8 项失败 | 7 个选角投影顺序断言和 1 个勋章容器拒绝码断言的测试名、错误消息均与[实施前记录](../../../artifacts/guild-refactor-20261128/cross-delivery)一致。 |
| [额外架构检查](../../../artifacts/guild-service-boundaries-20261128/structural.log) | 81 通过，2 项失败 | 测试组合缺少 `DropMaterializationLimits`，以及结算、队伍写入口分类过时。两项均在[本轮实施前副本](../../../artifacts/guild-service-boundaries-20261128/baseline-structural/baseline.trx)复现；改名许可的读取现已纳入分类。 |
| [其他直接调用方](../../../artifacts/guild-service-boundaries-20261128/callers.log) | 197 通过，1 项失败 | 永久任务奖励的选角测试夹具缺少 `IItemPlacementDefinitionSource`，与[本轮实施前副本](../../../artifacts/guild-service-boundaries-20261128/baseline-callers/baseline.trx)报错相同。 |

契约与最终代码均经独立审阅。验收证据为服务端自动化结果，本次未启动 A21 客户端、未操作玩家数据库，也未发布。

## PR 集成验证

分支已变基到主干 `1b0c55c93`。主干决斗场使用 schema v14–v21，合并后公会迁移从 v21 延伸至 v48。用三个历史提交的建表声明分别生成主干 v21、旧公会 v20 和 v40 的隔离库，升级后核对公会贡献、会员归属、决斗数据和新库结构。两个故障重试用例覆盖在决斗场迁移中断后恢复旧公会升级位置。[数据库迁移说明](../../DatabaseSchemaMigration.md)

此前 Unit 中的内容源注入和请求重放检查已处理。公会功能读取内容通过相应规则服务，协议入口按具体业务登记重放拒绝原因。选角测试核验公会地下城尾帧的收件人、发送顺序及荣誉与公会名同帧编码。勋章槽拒绝测试在已绑定的勋章容器内尝试放入药剂，并核对拒绝码和原物品。

| 合并前本地检查 | 结果 |
| --- | --- |
| Release 构建 | 0 警告、0 错误 |
| 全解决方案 `Category=Unit` | 2,098/2,098 |
| 公会、勋章、选角、副本和数据库等定向回归 | 356/356，含迁移测试 72/72 |
| GM 前端测试 | 195/195 |
| 项目引用和测试报告检查 | 13 个生产项目、32 条直接引用，相关工具测试 7/7 |

A21 实机证据沿用[公会功能验收记录](../guild-system/multi-spec-map.md)。本轮集成检查使用自动化和隔离库。基地战及未取证的周期行为继续在 #62 跟踪。
